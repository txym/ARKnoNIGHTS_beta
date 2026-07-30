using System;
using System.Collections;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ArknoNights.Lobby;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Main-thread composition root for LAN lobby, persistent Match session, scoped HUD,
/// local Battle streaming, reconnect recovery and direct return to the home view.
/// </summary>
[DisallowMultipleComponent]
public sealed class LanLobbyController : MonoBehaviour
{
    private const string RootName = "LanLobbyRoot";
    private const string ProfileAvatarKey = "LanLobby.Profile.AvatarIndex";

    private LanLobbyView view;
    private PreparationBattleLoopController preparationLoop;
    private LanDiscoveryService discovery;
    private LanRoomHost host;
    private LanRoomClient client;
    private IMulticastLock multicastLock;
    private LobbyProfile profile;
    private LanMatchSessionConfiguration matchConfiguration;
    private LanMatchRuntimeAssets runtimeAssets;
    private LanMatchRuntimeController matchRuntime;
    private IReconnectCredentialStore reconnectCredentialStore;
    private CancellationTokenSource reconnectCancellation;
    private Task<LanRoomClient> reconnectTask;
    private int operationVersion;
    private bool initialized;
    private bool gameplayStarted;
    private bool gameplayTransitionPending;
    private string pendingRuntimeExitStatus;

    private void Awake()
    {
        gameObject.name = RootName;
        DontDestroyOnLoad(gameObject);
        view = GetComponentInChildren<LanLobbyView>(true);
        if (view == null)
        {
            var viewObject = new GameObject("LanLobbyView");
            viewObject.transform.SetParent(transform, false);
            view = viewObject.AddComponent<LanLobbyView>();
        }
        multicastLock = AndroidMulticastLockFactory.Create();
        reconnectCredentialStore = new PlayerPrefsReconnectCredentialStore();
        profile = LoadProfile();
        LanMatchRuntimeConfiguration.TryCreateWithRuntimeAssets(
            out matchConfiguration,
            out runtimeAssets,
            out _);
        SubscribeView();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        if (matchConfiguration == null)
        {
            EnterHome("Match catalogs are unavailable.");
            initialized = true;
            return;
        }
        if (reconnectCredentialStore.TryLoad(out var reconnectCredential))
        {
            view.ShowHome(profile);
            view.SetStatus("Reconnecting to match...");
            BeginReconnect(reconnectCredential);
            initialized = true;
            return;
        }
        EnterHome(string.Empty);
        StartCoroutine(FindAndGatePreparationLoop());
        initialized = true;
    }

    private void Update()
    {
        if (!initialized) return;
        if (pendingRuntimeExitStatus != null)
        {
            var status = pendingRuntimeExitStatus;
            pendingRuntimeExitStatus = null;
            reconnectCredentialStore.Clear();
            EnterHome(status);
            return;
        }
        EnsurePreparationLoopIsGated();
        if (!gameplayStarted && discovery != null)
        {
            discovery.Tick(DateTimeOffset.UtcNow);
            view.BindDiscoveredRooms(discovery.DiscoveredRooms);
        }

        if (host != null)
        {
            host.Tick();
            if (host.Lifecycle == MatchSessionLifecycle.Ended)
            {
                pendingRuntimeExitStatus = "Match ended.";
                return;
            }
            var snapshot = host.Snapshot;
            if (!gameplayStarted)
            {
                view.BindRoom(snapshot, profile.PlayerId);
                if (snapshot != null && snapshot.HasStarted)
                    BeginGameplayTransition();
            }
        }
        else if (client != null)
        {
            client.Tick();
            if (client.HasEnded)
            {
                pendingRuntimeExitStatus = "Match ended.";
                return;
            }
            var snapshot = client.Snapshot;
            if (!gameplayStarted && snapshot != null)
            {
                view.BindRoom(snapshot, profile.PlayerId);
                view.SetLocalLatency(client.LatencyMilliseconds);
                if (snapshot.HasStarted) BeginGameplayTransition();
            }
            else if (client.IsReconnecting)
            {
                if (reconnectCredentialStore.TryLoad(
                    out var reconnectCredential))
                {
                    BeginReconnect(reconnectCredential);
                }
            }
        }
    }

    /// <summary>Test seam for the same receive-start path used after a host/client snapshot reports started.</summary>
    public void ReceiveStartForTests()
    {
        CompleteGameplayTransition();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (gameplayStarted) return;
        StartCoroutine(FindAndGatePreparationLoop());
    }

    private IEnumerator FindAndGatePreparationLoop()
    {
        for (var frame = 0; frame < 20 && preparationLoop == null; frame++)
        {
            EnsurePreparationLoopIsGated();
            if (preparationLoop != null) yield break;
            yield return null;
        }
    }

    private void EnsurePreparationLoopIsGated()
    {
        if (preparationLoop == null)
            preparationLoop = FindObjectOfType<PreparationBattleLoopController>();
        if (preparationLoop != null) preparationLoop.SetLobbyGate(true);
    }

    private void SubscribeView()
    {
        view.CreateRequested += CreateRoom;
        view.JoinRequested += JoinRoom;
        view.ProfileSaved += SaveProfile;
        view.ReadyRequested += SetReady;
        view.LeaveRequested += LeaveRoom;
        view.StartRequested += StartRoom;
    }

    private void UnsubscribeView()
    {
        if (view == null) return;
        view.CreateRequested -= CreateRoom;
        view.JoinRequested -= JoinRoom;
        view.ProfileSaved -= SaveProfile;
        view.ReadyRequested -= SetReady;
        view.LeaveRequested -= LeaveRoom;
        view.StartRequested -= StartRoom;
    }

    private void SaveProfile(LobbyProfile requested)
    {
        var avatar = requested == null ? 0 : Mathf.Clamp(requested.AvatarIndex, LobbyProfile.MinimumAvatarIndex, LobbyProfile.MaximumAvatarIndex);
        PlayerPrefs.SetInt(ProfileAvatarKey, avatar);
        PlayerPrefs.Save();
        profile = new LobbyProfile(profile.PlayerId, LobbyProfile.DisplayNameForAvatar(avatar), avatar);
        view.ShowHome(profile);
        view.SetStatus("Avatar changed.");
    }

    private void CreateRoom()
    {
        if (host != null || client != null) return;
        if (!RefreshRuntimeConfiguration(out var diagnosticCode))
        {
            view.SetStatus("Match catalogs are unavailable: " + diagnosticCode);
            return;
        }
        var version = ++operationVersion;
        StartCoroutine(CreateRoomRoutine(version));
    }

    private IEnumerator CreateRoomRoutine(int version)
    {
        view.SetStatus("Creating LAN room...");
        StopDiscovery();
        var task = LanRoomHost.StartAsync(
            profile,
            matchConfiguration);
        yield return WaitForTask(task);
        if (version != operationVersion)
        {
            if (task.Status == TaskStatus.RanToCompletion) ObserveStop(task.Result);
            yield break;
        }
        if (task.IsFaulted || task.IsCanceled)
        {
            EnterHome("Could not create room.");
            yield break;
        }

        host = task.Result;
        host.Tick();
        StartHostDiscovery();
        view.ShowRoom(host.Snapshot, profile.PlayerId);
        view.SetStatus("Room " + host.RoomCode + " created.");
    }

    private void JoinRoom(string roomCode)
    {
        if (host != null || client != null || discovery == null) return;
        if (!RefreshRuntimeConfiguration(out var diagnosticCode))
        {
            view.SetStatus("Match catalogs are unavailable: " + diagnosticCode);
            return;
        }
        if (!discovery.TryGetEndpoint(roomCode, out var endpoint))
        {
            view.SetStatus("Room is no longer available.");
            return;
        }

        var version = ++operationVersion;
        StartCoroutine(JoinRoomRoutine(version, endpoint, roomCode));
    }

    private IEnumerator JoinRoomRoutine(int version, IPEndPoint endpoint, string roomCode)
    {
        view.SetStatus("Joining room " + roomCode + "...");
        var task = LanRoomClient.JoinAsync(
            endpoint,
            roomCode,
            profile,
            matchConfiguration,
            reconnectCredentialStore);
        yield return WaitForTask(task);
        if (version != operationVersion)
        {
            if (task.Status == TaskStatus.RanToCompletion) ObserveStop(task.Result, false);
            yield break;
        }

        if (task.IsFaulted || task.IsCanceled)
        {
            EnterHome("Room rejected or unavailable.");
            yield break;
        }

        client = task.Result;
        client.Tick();
        StopDiscovery();
        view.ShowRoom(client.Snapshot, profile.PlayerId);
        view.SetStatus("Joined room " + roomCode + ".");
    }

    private void SetReady(bool isReady)
    {
        if (host != null)
        {
            if (!host.TrySetReady(profile.PlayerId, isReady, out var failure)) view.SetStatus("Ready update rejected: " + failure + ".");
            return;
        }

        if (client != null) StartCoroutine(SetReadyRoutine(client.SetReadyAsync(isReady)));
    }

    private IEnumerator SetReadyRoutine(Task task)
    {
        yield return WaitForTask(task);
        if (task.IsFaulted || task.IsCanceled) EnterHome("Connection lost.");
    }

    private void StartRoom()
    {
        if (host == null) return;
        if (!host.TryStart(profile.PlayerId, out var failure))
        {
            view.SetStatus("Cannot start: " + failure + ".");
            return;
        }
        var initialization = host.HostInitialization;
        try
        {
            reconnectCredentialStore.Save(new ReconnectCredential(
                initialization.Snapshot.SessionId,
                IPAddress.Loopback.ToString(),
                host.TcpPort,
                profile.PlayerId,
                initialization.ReconnectToken,
                initialization.Manifest.ToDomain()));
        }
        catch (Exception)
        {
            host.AbortMatch("match.host.credential.persistFailed");
            reconnectCredentialStore.Clear();
            view.SetStatus("Cannot start: reconnect credential could not be saved.");
            return;
        }

        BeginGameplayTransition(host.StartBroadcastTask);
    }

    private IEnumerator CompleteGameplayAfterBroadcast(Task broadcastTask = null)
    {
        // Do not release host sockets until the authoritative Start snapshot has finished writing to guests.
        if (broadcastTask != null) yield return WaitForTask(broadcastTask);
        else yield return null;
        CompleteGameplayTransition();
    }

    private void BeginGameplayTransition(Task broadcastTask = null)
    {
        if (gameplayStarted || gameplayTransitionPending) return;
        gameplayTransitionPending = true;
        StartCoroutine(CompleteGameplayAfterBroadcast(broadcastTask));
    }

    private void CompleteGameplayTransition()
    {
        if (gameplayStarted) return;
        gameplayStarted = true;
        StopDiscovery();
        EnsurePreparationLoopIsGated();
        if (view != null) view.gameObject.SetActive(false);
        if ((host != null || client != null)
            && !EnsureMatchRuntime(out var diagnosticCode))
        {
            gameplayStarted = false;
            reconnectCredentialStore.Clear();
            EnterHome("Match initialization failed: " + diagnosticCode);
        }
    }

    private bool EnsureMatchRuntime(out string diagnosticCode)
    {
        if (matchRuntime != null)
        {
            diagnosticCode = string.Empty;
            return true;
        }
        if (runtimeAssets == null)
        {
            diagnosticCode = "match.runtime.assets.missing";
            return false;
        }
        matchRuntime = gameObject.AddComponent<LanMatchRuntimeController>();
        matchRuntime.ExitRequested += HandleRuntimeExitRequested;
        var initializedRuntime = host != null
            ? matchRuntime.InitializeHost(
                host,
                profile,
                runtimeAssets,
                out diagnosticCode)
            : matchRuntime.InitializeGuest(
                client,
                profile,
                runtimeAssets,
                out diagnosticCode);
        if (initializedRuntime) return true;
        matchRuntime.ExitRequested -= HandleRuntimeExitRequested;
        matchRuntime.DisposeRuntime();
        Destroy(matchRuntime);
        matchRuntime = null;
        return false;
    }

    private void HandleRuntimeExitRequested(string status)
    {
        if (pendingRuntimeExitStatus == null)
            pendingRuntimeExitStatus = string.IsNullOrWhiteSpace(status)
                ? "Match ended."
                : status;
    }

    private void LeaveRoom()
    {
        reconnectCredentialStore.Clear();
        CancelReconnect();
        if (host != null && gameplayStarted)
        {
            host.AbortMatch("match.host.explicitQuit");
            reconnectCredentialStore.Clear();
        }
        gameplayStarted = false;
        gameplayTransitionPending = false;
        EnterHome("Left room.", true);
    }

    private void EnterHome(string status, bool notifyGuest = false)
    {
        pendingRuntimeExitStatus = null;
        gameplayStarted = false;
        gameplayTransitionPending = false;
        DisposeMatchRuntime();
        StopServices(notifyGuest);
        if (view == null) return;
        view.gameObject.SetActive(true);
        view.ShowHome(profile);
        view.SetStatus(status);
        StartHomeDiscovery();
        EnsurePreparationLoopIsGated();
    }

    private void StartHomeDiscovery()
    {
        if (discovery != null) return;
        try
        {
            multicastLock.Acquire();
            discovery = new LanDiscoveryService();
            discovery.Start();
        }
        catch (Exception)
        {
            discovery?.Dispose();
            discovery = null;
            multicastLock.Release();
            view.SetStatus("LAN discovery is unavailable.");
        }
    }

    private void StartHostDiscovery()
    {
        StopDiscovery();
        try
        {
            multicastLock.Acquire();
            discovery = host.CreateDiscoveryService();
            discovery.Start();
        }
        catch (Exception)
        {
            discovery?.Dispose();
            discovery = null;
            multicastLock.Release();
            view.SetStatus("Room created, but LAN discovery is unavailable.");
        }
    }

    private void StopDiscovery()
    {
        if (discovery != null)
        {
            discovery.Dispose();
            discovery = null;
        }

        multicastLock?.Release();
    }

    private void StopServices(bool notifyGuest)
    {
        operationVersion++;
        CancelReconnect();
        StopDiscovery();
        var oldClient = client;
        client = null;
        if (oldClient != null) ObserveStop(oldClient, notifyGuest);
        var oldHost = host;
        host = null;
        if (oldHost != null) ObserveStop(oldHost);
    }

    private static void ObserveStop(LanRoomClient roomClient, bool notifyGuest)
    {
        if (notifyGuest) _ = Observe(roomClient.LeaveAsync());
        else _ = Observe(roomClient.StopAsync());
    }

    private static void ObserveStop(LanRoomHost roomHost)
    {
        _ = Observe(roomHost.StopAsync());
    }

    private static async Task Observe(Task task)
    {
        try { await task; }
        catch (Exception) { }
    }

    private static IEnumerator WaitForTask(Task task)
    {
        while (task != null && !task.IsCompleted) yield return null;
    }

    private LobbyProfile LoadProfile()
    {
        var avatar = Mathf.Clamp(PlayerPrefs.GetInt(ProfileAvatarKey, 0), LobbyProfile.MinimumAvatarIndex, LobbyProfile.MaximumAvatarIndex);
        return new LobbyProfile(
            LocalProfileIdentity.GetOrCreate(
                new PlayerPrefsLocalProfileIdentityStore()),
            LobbyProfile.DisplayNameForAvatar(avatar),
            avatar);
    }

    private void BeginReconnect(ReconnectCredential credential)
    {
        if (credential == null
            || !credential.IsValid
            || reconnectTask != null)
        {
            return;
        }
        StopDiscovery();
        matchRuntime?.SetReconnecting(true);
        var oldClient = client;
        var retainedSnapshot = oldClient == null
            ? null
            : oldClient.MatchSnapshot;
        client = null;
        if (oldClient != null) ObserveStop(oldClient, false);
        reconnectCancellation = new CancellationTokenSource();
        reconnectTask = LanMatchReconnectCoordinator
            .ReconnectUntilAcceptedAsync(
                credential,
                reconnectCredentialStore,
                reconnectCancellation.Token,
                null,
                retainedSnapshot);
        StartCoroutine(ReconnectRoutine(reconnectTask));
    }

    private IEnumerator ReconnectRoutine(Task<LanRoomClient> task)
    {
        yield return WaitForTask(task);
        if (task != reconnectTask) yield break;
        reconnectTask = null;
        reconnectCancellation?.Dispose();
        reconnectCancellation = null;
        if (task.Status == TaskStatus.RanToCompletion)
        {
            client = task.Result;
            client.Tick();
            gameplayStarted = true;
            gameplayTransitionPending = false;
            EnsurePreparationLoopIsGated();
            if (view != null) view.gameObject.SetActive(false);
            if (matchRuntime != null)
            {
                if (!matchRuntime.RebindClient(client, out var rebindDiagnostic))
                {
                    reconnectCredentialStore.Clear();
                    EnterHome("Match recovery failed: " + rebindDiagnostic);
                }
            }
            else if (!EnsureMatchRuntime(out var runtimeDiagnostic))
            {
                reconnectCredentialStore.Clear();
                EnterHome("Match recovery failed: " + runtimeDiagnostic);
            }
            yield break;
        }
        DisposeMatchRuntime();
        var incompatible = task.Exception != null
            && task.Exception.GetBaseException()
                is MatchReconnectRejectedException rejected
            && rejected.Code
                == MatchReconnectRejectCode.CompatibilityMismatch;
        if (task.Exception != null
            && task.Exception.GetBaseException()
                is MatchReconnectRejectedException terminal
            && ReconnectCredentialPolicy
                .ShouldClearOnAuthoritativeRejection(
                    terminal.Code))
        {
            reconnectCredentialStore.Clear();
        }
        gameplayStarted = false;
        if (view != null)
        {
            view.gameObject.SetActive(true);
            view.ShowHome(profile);
            view.SetStatus(incompatible
                ? "Version incompatible. Update before reconnecting."
                : "Match recovery was rejected.");
        }
        if (!incompatible) StartHomeDiscovery();
    }

    private bool RefreshRuntimeConfiguration(out string diagnosticCode)
    {
        return LanMatchRuntimeConfiguration.TryCreateWithRuntimeAssets(
            out matchConfiguration,
            out runtimeAssets,
            out diagnosticCode);
    }

    private void CancelReconnect()
    {
        if (reconnectCancellation == null) return;
        reconnectCancellation.Cancel();
        reconnectCancellation.Dispose();
        reconnectCancellation = null;
        reconnectTask = null;
    }

    private void DisposeMatchRuntime()
    {
        if (matchRuntime == null) return;
        matchRuntime.ExitRequested -= HandleRuntimeExitRequested;
        matchRuntime.DisposeRuntime();
        Destroy(matchRuntime);
        matchRuntime = null;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        UnsubscribeView();
        if (host != null
            && host.Lifecycle != MatchSessionLifecycle.Lobby
            && host.Lifecycle != MatchSessionLifecycle.Ended)
        {
            host.AbortMatch("match.host.applicationQuit");
            reconnectCredentialStore.Clear();
        }
        DisposeMatchRuntime();
        StopServices(false);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindObjectOfType<LanLobbyController>() != null) return;
        new GameObject(RootName).AddComponent<LanLobbyController>();
    }
}
