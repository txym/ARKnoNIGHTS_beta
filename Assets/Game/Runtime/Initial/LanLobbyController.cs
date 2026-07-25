using System;
using System.Collections;
using System.Net;
using System.Threading.Tasks;
using ArknoNights.Lobby;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Main-thread bridge between the independent LAN lobby assembly and the existing local battle demo.
/// The room session only synchronizes lobby state; it deliberately never accesses PlayerState.
/// </summary>
[DisallowMultipleComponent]
public sealed class LanLobbyController : MonoBehaviour
{
    private const string RootName = "LanLobbyRoot";
    private const string ProfileNameKey = "LanLobby.Profile.Name";
    private const string ProfileAvatarKey = "LanLobby.Profile.AvatarIndex";
    private const string DefaultDisplayName = "Doctor";

    private LanLobbyView view;
    private PreparationBattleLoopController preparationLoop;
    private LanDiscoveryService discovery;
    private LanRoomHost host;
    private LanRoomClient client;
    private IMulticastLock multicastLock;
    private LobbyProfile profile;
    private int operationVersion;
    private bool initialized;
    private bool gameplayStarted;
    private bool gameplayTransitionPending;

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
        profile = LoadProfile();
        SubscribeView();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        EnterHome(string.Empty);
        StartCoroutine(FindAndGatePreparationLoop());
        initialized = true;
    }

    private void Update()
    {
        if (!initialized || gameplayStarted) return;
        EnsurePreparationLoopIsGated();
        if (discovery != null)
        {
            discovery.Tick(DateTimeOffset.UtcNow);
            view.BindDiscoveredRooms(discovery.DiscoveredRooms);
        }

        if (host != null)
        {
            host.Tick();
            var snapshot = host.Snapshot;
            view.BindRoom(snapshot, profile.PlayerId);
            if (snapshot != null && snapshot.HasStarted) BeginGameplayTransition();
        }
        else if (client != null)
        {
            client.Tick();
            var snapshot = client.Snapshot;
            if (snapshot != null)
            {
                view.BindRoom(snapshot, profile.PlayerId);
                view.SetLocalLatency(client.LatencyMilliseconds);
                if (snapshot.HasStarted) BeginGameplayTransition();
            }
            else if (!client.IsConnected)
            {
                EnterHome("Connection lost.");
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
        var name = requested == null ? DefaultDisplayName : NormalizeDisplayName(requested.DisplayName);
        var avatar = requested == null ? 0 : Mathf.Clamp(requested.AvatarIndex, LobbyProfile.MinimumAvatarIndex, LobbyProfile.MaximumAvatarIndex);
        PlayerPrefs.SetString(ProfileNameKey, name);
        PlayerPrefs.SetInt(ProfileAvatarKey, avatar);
        PlayerPrefs.Save();
        profile = new LobbyProfile(profile.PlayerId, name, avatar);
        view.ShowHome(profile);
        view.SetStatus("Profile saved.");
    }

    private void CreateRoom()
    {
        if (host != null || client != null) return;
        var version = ++operationVersion;
        StartCoroutine(CreateRoomRoutine(version));
    }

    private IEnumerator CreateRoomRoutine(int version)
    {
        view.SetStatus("Creating LAN room...");
        StopDiscovery();
        var task = LanRoomHost.StartAsync(profile);
        yield return WaitForTask(task);
        if (version != operationVersion) yield break;
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
        var task = LanRoomClient.JoinAsync(endpoint, roomCode, profile);
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
        StopServices(false);
        if (preparationLoop == null) preparationLoop = FindObjectOfType<PreparationBattleLoopController>();
        if (preparationLoop != null) preparationLoop.SetLobbyGate(false);
        if (view != null) view.gameObject.SetActive(false);
    }

    private void LeaveRoom()
    {
        EnterHome("Left room.", true);
    }

    private void EnterHome(string status, bool notifyGuest = false)
    {
        if (gameplayStarted) return;
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
        var name = NormalizeDisplayName(PlayerPrefs.GetString(ProfileNameKey, DefaultDisplayName));
        var avatar = Mathf.Clamp(PlayerPrefs.GetInt(ProfileAvatarKey, 0), LobbyProfile.MinimumAvatarIndex, LobbyProfile.MaximumAvatarIndex);
        return new LobbyProfile("lan-" + Guid.NewGuid().ToString("N"), name, avatar);
    }

    private static string NormalizeDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return DefaultDisplayName;
        value = value.Trim();
        return value.Length <= LobbyProfile.MaximumDisplayNameCharacters
            ? value
            : value.Substring(0, LobbyProfile.MaximumDisplayNameCharacters);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        UnsubscribeView();
        StopServices(false);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindObjectOfType<LanLobbyController>() != null) return;
        new GameObject(RootName).AddComponent<LanLobbyController>();
    }
}
