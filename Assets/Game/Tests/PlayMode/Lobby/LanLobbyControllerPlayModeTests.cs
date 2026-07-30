using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Threading.Tasks;
using ArknoNights.Lobby;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ArknoNights.Lobby.Tests
{
    public sealed class LanLobbyControllerPlayModeTests
    {
        private readonly List<LanRoomClient> clientsToStop = new List<LanRoomClient>();
        private readonly List<LanRoomHost> hostsToStop = new List<LanRoomHost>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var client in clientsToStop)
            {
                if (client != null) yield return WaitForTask(client.StopAsync());
            }
            clientsToStop.Clear();

            foreach (var roomHost in hostsToStop)
            {
                if (roomHost != null) yield return WaitForTask(roomHost.StopAsync());
            }
            hostsToStop.Clear();

            foreach (var controller in Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                .Where(item => item != null && string.Equals(item.GetType().Name, "LanLobbyController", StringComparison.Ordinal))
                .ToArray())
            {
                UnityEngine.Object.Destroy(controller.gameObject);
            }

            // SampleScene owns a generic EventSystem. Without removing it, the following isolated
            // view fixture correctly reuses that external system and cannot exercise owned-system cleanup.
            foreach (var eventSystem in Resources.FindObjectsOfTypeAll<UnityEngine.EventSystems.EventSystem>())
            {
                if (eventSystem != null) UnityEngine.Object.Destroy(eventSystem.gameObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator LobbyGate_RemainsFrozenWhenSessionOwnsMatchAfterHostStart()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            yield return WaitForSceneBootstrap();

            var loop = FindComponent("PreparationBattleLoopController");
            var controller = FindComponent("LanLobbyController");
            var lobbyRoot = GameObject.Find("LanLobbyRoot");
            Assert.That(loop, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);
            Assert.That(lobbyRoot, Is.Not.Null);

            var loopType = loop.GetType();
            var controllerType = controller.GetType();
            Assert.That((bool)loopType.GetProperty("IsLobbyGateActive").GetValue(loop), Is.True);
            var before = (float)loopType.GetProperty("RemainingPreparationSeconds").GetValue(loop);
            yield return new WaitForSecondsRealtime(.35f);
            Assert.That((float)loopType.GetProperty("RemainingPreparationSeconds").GetValue(loop), Is.EqualTo(before).Within(.05f));

            controllerType.GetMethod("ReceiveStartForTests", BindingFlags.Instance | BindingFlags.Public).Invoke(controller, null);
            yield return null;

            Assert.That((bool)loopType.GetProperty("IsLobbyGateActive").GetValue(loop), Is.True);
            Assert.That((float)loopType.GetProperty("RemainingPreparationSeconds").GetValue(loop), Is.EqualTo(before).Within(.05f));
            Assert.That(lobbyRoot.GetComponentInChildren<global::LanLobbyView>(true).gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator ReloadingSampleScene_KeepsExactlyOneLobbyRoot()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            yield return WaitForSceneBootstrap();
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            yield return WaitForSceneBootstrap();

            // The gameplay transition hides the view but intentionally keeps its persistent root alive.
            Assert.That(Resources.FindObjectsOfTypeAll<global::LanLobbyView>().Length, Is.EqualTo(1));
            Assert.That(GameObject.Find("LanLobbyRoot"), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator ReplacedCreate_StopsCompletedStaleHostListener()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            yield return WaitForSceneBootstrap();
            var controller = FindComponent("LanLobbyController");
            var type = controller.GetType();
            var listenersBefore = ListenerKeys();

            type.GetMethod("CreateRoom", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
            type.GetMethod("LeaveRoom", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
            yield return null;
            yield return null;

            var listenersAfter = ListenerKeys();
            Assert.That(listenersAfter.Except(listenersBefore), Is.Empty, "A superseded successful create must release its unbound TCP listener.");
        }

        [UnityTest]
        public IEnumerator LeavingRoom_ShutsDownHostOrNotifiesGuestAndReturnsHome()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            yield return WaitForSceneBootstrap();

            var controller = FindComponent("LanLobbyController");
            var controllerType = controller.GetType();
            var hostField = controllerType.GetField("host", BindingFlags.Instance | BindingFlags.NonPublic);
            var clientField = controllerType.GetField("client", BindingFlags.Instance | BindingFlags.NonPublic);
            var profileField = controllerType.GetField("profile", BindingFlags.Instance | BindingFlags.NonPublic);
            var profile = (LobbyProfile)profileField.GetValue(controller);
            var view = controller.GetComponentInChildren<global::LanLobbyView>(true);
            Assert.That(hostField, Is.Not.Null);
            Assert.That(clientField, Is.Not.Null);
            Assert.That(profile, Is.Not.Null);
            Assert.That(view, Is.Not.Null);

            var localHostTask = LanRoomHost.StartForTestsAsync(profile, 0);
            yield return WaitForTask(localHostTask);
            var localHost = localHostTask.Result;
            hostsToStop.Add(localHost);
            var localHostPort = localHost.TcpPort;
            var localHostShutdownTasks = PrivateTasks(localHost, "acceptTask", "heartbeatTask");
            hostField.SetValue(controller, localHost);
            view.ShowRoom(localHost.Snapshot, profile.PlayerId);

            view.transform.Find("LanLobbyRoot/Room/LeaveAction").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();

            Assert.That(hostField.GetValue(controller), Is.Null);
            AssertHome(view);
            for (var frame = 0; frame < 120 && ListenerKeys().Any(key => key.EndsWith(":" + localHostPort)); frame++)
                yield return null;
            Assert.That(ListenerKeys().Any(key => key.EndsWith(":" + localHostPort)), Is.False,
                "Host Leave must close the authoritative room listener.");
            yield return WaitForTask(Task.WhenAll(localHostShutdownTasks));

            var remoteHostProfile = new LobbyProfile("remote-host", "Remote Host", 0);
            var remoteHostTask = LanRoomHost.StartForTestsAsync(remoteHostProfile, 0);
            yield return WaitForTask(remoteHostTask);
            var remoteHost = remoteHostTask.Result;
            hostsToStop.Add(remoteHost);
            var clientTask = LanRoomClient.JoinForTestsAsync(remoteHost.LoopbackEndpoint, profile);
            yield return WaitForTask(clientTask);
            var localClient = clientTask.Result;
            clientsToStop.Add(localClient);
            var localClientShutdownTasks = PrivateTasks(localClient, "readTask", "heartbeatTask");
            localClient.Tick();
            remoteHost.Tick();
            Assert.That(localClient.Snapshot, Is.Not.Null);
            Assert.That(remoteHost.Snapshot.Members.Select(member => member.PlayerId), Does.Contain(profile.PlayerId));
            clientField.SetValue(controller, localClient);
            view.ShowRoom(localClient.Snapshot, profile.PlayerId);

            view.transform.Find("LanLobbyRoot/Room/LeaveAction").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();

            Assert.That(clientField.GetValue(controller), Is.Null);
            AssertHome(view);
            for (var frame = 0; frame < 120; frame++)
            {
                remoteHost.Tick();
                if (!localClient.IsConnected
                    && remoteHost.Snapshot.Members.All(member => member.PlayerId != profile.PlayerId))
                    break;
                yield return null;
            }
            remoteHost.Tick();
            Assert.That(localClient.IsConnected, Is.False);
            Assert.That(remoteHost.Snapshot.Members.Select(member => member.PlayerId),
                Has.None.EqualTo(profile.PlayerId), "Guest Leave must remove only the guest from the authoritative room.");
            yield return WaitForTask(Task.WhenAll(localClientShutdownTasks));
        }

        [UnityTest]
        public IEnumerator GuestStartedLobbySnapshot_WaitsForMatchInitializationInsteadOfReturningHome()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            yield return WaitForSceneBootstrap();

            var controller = FindComponent("LanLobbyController");
            var controllerType = controller.GetType();
            var clientField = controllerType.GetField(
                "client",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var gameplayStartedField = controllerType.GetField(
                "gameplayStarted",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var matchRuntimeField = controllerType.GetField(
                "matchRuntime",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var profileField = controllerType.GetField(
                "profile",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var profile = (LobbyProfile)profileField.GetValue(controller);
            var view = controller.GetComponentInChildren<global::LanLobbyView>(true);

            var remoteHostProfile = new LobbyProfile(
                "lan-cccccccccccccccccccccccccccccccc",
                "Remote Host",
                0);
            var remoteHostTask = LanRoomHost.StartForTestsAsync(
                remoteHostProfile,
                0);
            yield return WaitForTask(remoteHostTask);
            var remoteHost = remoteHostTask.Result;
            hostsToStop.Add(remoteHost);

            var clientTask = LanRoomClient.JoinForTestsAsync(
                remoteHost.LoopbackEndpoint,
                profile);
            yield return WaitForTask(clientTask);
            var localClient = clientTask.Result;
            clientsToStop.Add(localClient);
            localClient.Tick();
            remoteHost.Tick();

            var lobbySnapshot = localClient.Snapshot;
            var startedWithoutInitialization = new LobbyRoomSnapshot(
                lobbySnapshot.RoomCode,
                lobbySnapshot.HostPlayerId,
                lobbySnapshot.Members,
                true,
                lobbySnapshot.Revision + 1);
            var snapshotField = typeof(LanRoomClient).GetField(
                "snapshot",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(snapshotField, Is.Not.Null);
            snapshotField.SetValue(localClient, startedWithoutInitialization);
            clientField.SetValue(controller, localClient);
            view.ShowRoom(startedWithoutInitialization, profile.PlayerId);

            yield return null;
            yield return null;

            Assert.That(
                clientField.GetValue(controller),
                Is.SameAs(localClient),
                "Lobby Start and MatchInitialized are separate frames; the first frame must not dispose the guest connection.");
            Assert.That(
                (bool)gameplayStartedField.GetValue(controller),
                Is.False);
            Assert.That(matchRuntimeField.GetValue(controller), Is.Null);
            Assert.That(view.gameObject.activeSelf, Is.True);
            Assert.That(
                view.transform.Find("LanLobbyRoot/Room").gameObject.activeSelf,
                Is.True);
            Assert.That(
                view.transform.Find("LanLobbyRoot/Home").gameObject.activeSelf,
                Is.False);

            var readyTask = localClient.SetReadyAsync(true);
            yield return WaitForTask(readyTask);
            for (var frame = 0;
                 frame < 120
                 && !remoteHost.Snapshot.Members.Single(
                         member => member.PlayerId == profile.PlayerId)
                     .IsReady;
                 frame++)
            {
                remoteHost.Tick();
                yield return null;
            }
            remoteHost.Tick();
            Assert.That(
                remoteHost.Snapshot.Members.Single(
                    member => member.PlayerId == profile.PlayerId).IsReady,
                Is.True);
            Assert.That(
                remoteHost.TryStart(
                    remoteHostProfile.PlayerId,
                    out var startFailure),
                Is.True,
                startFailure.ToString());

            var initializedTask = localClient.WaitForMatchInitializedAsync(
                TimeSpan.FromSeconds(2));
            var initializationDeadline = Time.realtimeSinceStartup + 3f;
            while (!initializedTask.IsCompleted
                   && Time.realtimeSinceStartup < initializationDeadline)
            {
                yield return null;
            }
            Assert.That(
                initializedTask.IsCompleted,
                Is.True,
                "Timed out waiting for MatchInitialized.");
            Assert.That(
                initializedTask.IsFaulted,
                Is.False,
                initializedTask.Exception == null
                    ? string.Empty
                    : initializedTask.Exception.ToString());
            for (var frame = 0;
                 frame < 20
                 && !(bool)gameplayStartedField.GetValue(controller);
                 frame++)
            {
                yield return null;
            }

            Assert.That(
                (bool)gameplayStartedField.GetValue(controller),
                Is.True,
                "The guest must transition after MatchInitialized is applied.");
            Assert.That(
                clientField.GetValue(controller),
                Is.SameAs(localClient));
            Assert.That(matchRuntimeField.GetValue(controller), Is.Not.Null);
            Assert.That(view.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void RuntimeCatalogsAndIsolatedPlayerPrefsStores_AreValidAndFailClosed()
        {
            var runtimeType = FindRuntimeType(
                "LanMatchRuntimeConfiguration");
            var tryCreate = runtimeType.GetMethod(
                "TryCreate",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(tryCreate, Is.Not.Null);
            var arguments = new object[] { null, null };
            Assert.That(
                (bool)tryCreate.Invoke(null, arguments),
                Is.True,
                arguments[1] as string);
            var configuration =
                arguments[0] as LanMatchSessionConfiguration;
            Assert.That(configuration, Is.Not.Null);
            Assert.That(configuration.IsValid, Is.True);
            Assert.That(
                configuration.CompatibilityManifest.UnitCatalogSha256,
                Does.Match("^[0-9a-f]{64}$"));
            Assert.That(
                configuration.CompatibilityManifest.AbilityCatalogSha256,
                Does.Match("^[0-9a-f]{64}$"));
            Assert.That(
                configuration.ShopCatalog.Entries,
                Has.Count.EqualTo(100));
            Assert.That(
                configuration.ShopCatalog.Entries.Count(entry =>
                    entry.IsShopEligible),
                Is.EqualTo(94));
            Assert.That(
                configuration.ShopCatalog.Entries
                    .Where(entry => !entry.IsShopEligible)
                    .Select(entry => entry.TypeId),
                Is.EquivalentTo(new[]
                {
                    "1000", "1137", "1138", "2033", "5504", "10002"
                }));

            var hashInputType = FindRuntimeType("CatalogHashInput");
            var hashInputConstructor = hashInputType.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(byte[]) },
                null);
            var hashMethod = runtimeType.GetMethod(
                "ComputeCanonicalHashForTests",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(hashInputConstructor, Is.Not.Null);
            Assert.That(hashMethod, Is.Not.Null);
            var first = hashInputConstructor.Invoke(
                new object[] { "a", new byte[] { 1 } });
            var second = hashInputConstructor.Invoke(
                new object[] { "b", new byte[] { 2 } });
            var forward = Array.CreateInstance(hashInputType, 2);
            forward.SetValue(first, 0);
            forward.SetValue(second, 1);
            var reverse = Array.CreateInstance(hashInputType, 2);
            reverse.SetValue(second, 0);
            reverse.SetValue(first, 1);
            Assert.That(
                hashMethod.Invoke(null, new object[] { forward }),
                Is.EqualTo(
                    hashMethod.Invoke(
                        null,
                        new object[] { reverse })));

            var identityKey =
                "LanMatch.Tests.Identity." + Guid.NewGuid().ToString("N");
            var credentialKey =
                "LanMatch.Tests.Credential."
                + Guid.NewGuid().ToString("N");
            try
            {
                var identityStoreType = FindRuntimeType(
                    "PlayerPrefsLocalProfileIdentityStore");
                var identityStore =
                    (ILocalProfileIdentityStore)Activator.CreateInstance(
                        identityStoreType,
                        new object[] { identityKey });
                var playerId =
                    LocalProfileIdentity.GetOrCreate(identityStore);
                var secondIdentityStore =
                    (ILocalProfileIdentityStore)Activator.CreateInstance(
                        identityStoreType,
                        new object[] { identityKey });
                Assert.That(
                    LocalProfileIdentity.GetOrCreate(
                        secondIdentityStore),
                    Is.EqualTo(playerId));

                var credentialStoreType = FindRuntimeType(
                    "PlayerPrefsReconnectCredentialStore");
                var store =
                    (IReconnectCredentialStore)Activator.CreateInstance(
                        credentialStoreType,
                        new object[] { credentialKey });
                var credential = new ReconnectCredential(
                    "match-test",
                    IPAddress.Loopback.ToString(),
                    12345,
                    playerId,
                    ReconnectTokenIssuer.Issue().RawToken,
                    configuration.CompatibilityManifest);
                store.Save(credential);
                var reloaded =
                    (IReconnectCredentialStore)Activator.CreateInstance(
                        credentialStoreType,
                        new object[] { credentialKey });
                Assert.That(
                    reloaded.TryLoad(out var loaded),
                    Is.True);
                Assert.That(loaded.SessionId, Is.EqualTo("match-test"));
                Assert.That(
                    ReconnectTokenIssuer.IsValidRawToken(
                        loaded.Token),
                    Is.True);

                PlayerPrefs.SetString(
                    credentialKey,
                    "corrupt-record");
                PlayerPrefs.Save();
                Assert.That(
                    reloaded.TryLoad(out _),
                    Is.False);
                Assert.That(
                    PlayerPrefs.HasKey(credentialKey),
                    Is.False);
            }
            finally
            {
                PlayerPrefs.DeleteKey(identityKey);
                PlayerPrefs.DeleteKey(credentialKey);
                PlayerPrefs.Save();
            }
        }

        [UnityTest]
        public IEnumerator HostStart_PersistsLocalCredentialAndAuthoritativeEndReturnsHome()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            yield return WaitForSceneBootstrap();

            var controller = FindComponent("LanLobbyController");
            var controllerType = controller.GetType();
            var hostField = controllerType.GetField(
                "host",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var profileField = controllerType.GetField(
                "profile",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var credentialStoreField = controllerType.GetField(
                "reconnectCredentialStore",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var profile = (LobbyProfile)profileField.GetValue(controller);
            var store = new MemoryCredentialStore();
            credentialStoreField.SetValue(controller, store);
            var hostTask = LanRoomHost.StartForTestsAsync(profile, 0);
            yield return WaitForTask(hostTask);
            var localHost = hostTask.Result;
            hostsToStop.Add(localHost);
            hostField.SetValue(controller, localHost);

            controllerType.GetMethod(
                    "StartRoom",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, null);
            yield return null;
            yield return null;

            Assert.That(
                localHost.Lifecycle,
                Is.EqualTo(MatchSessionLifecycle.Match));
            Assert.That(store.Credential, Is.Not.Null);
            Assert.That(
                store.Credential.SessionId,
                Is.EqualTo(localHost.SessionActor.SessionId));
            Assert.That(
                store.Credential.PlayerId,
                Is.EqualTo(profile.PlayerId));

            localHost.AbortMatch("match.test.authoritativeEnd");
            for (var frame = 0;
                 frame < 120 && hostField.GetValue(controller) != null;
                 frame++)
            {
                yield return null;
            }

            Assert.That(hostField.GetValue(controller), Is.Null);
            Assert.That(store.Credential, Is.Null);
            var view = controller.GetComponentInChildren<
                global::LanLobbyView>(true);
            Assert.That(view.gameObject.activeSelf, Is.True);
            Assert.That(
                view.transform.Find("LanLobbyRoot/Home")
                    .gameObject.activeSelf,
                Is.True);
        }

        private static void AssertHome(global::LanLobbyView view)
        {
            Assert.That(view.transform.Find("LanLobbyRoot/Home").gameObject.activeSelf, Is.True);
            Assert.That(view.transform.Find("LanLobbyRoot/Room").gameObject.activeSelf, Is.False);
            Assert.That(view.StatusTextForTests, Is.EqualTo("Left room."));
        }

        private static IEnumerator WaitForTask(Task task)
        {
            Assert.That(task, Is.Not.Null);
            var deadline = Time.realtimeSinceStartup + 5f;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(task.IsCompleted, Is.True, "Timed out waiting for LAN lifecycle task.");
            Assert.That(task.IsFaulted, Is.False, task.Exception == null ? string.Empty : task.Exception.ToString());
            Assert.That(task.IsCanceled, Is.False);
        }

        private static Task[] PrivateTasks(object owner, params string[] fieldNames)
        {
            var tasks = new Task[fieldNames.Length];
            for (var index = 0; index < fieldNames.Length; index++)
            {
                var field = owner.GetType().GetField(fieldNames[index], BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null, fieldNames[index]);
                tasks[index] = field.GetValue(owner) as Task;
                Assert.That(tasks[index], Is.Not.Null, fieldNames[index]);
            }
            return tasks;
        }

        private static IEnumerator WaitForSceneBootstrap()
        {
            EnsureControllerForIsolatedTest();
            for (var frame = 0; frame < 20; frame++)
            {
                if (FindComponent("PreparationBattleLoopController") != null && FindComponent("LanLobbyController") != null)
                    yield break;
                yield return null;
            }
        }

        private static void EnsureControllerForIsolatedTest()
        {
            if (FindComponent("LanLobbyController") != null) return;
            var controllerType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("LanLobbyController"))
                .FirstOrDefault(type => type != null);
            Assert.That(controllerType, Is.Not.Null);
            new GameObject("LanLobbyRoot").AddComponent(controllerType);
        }

        private static Component FindComponent(string typeName)
        {
            foreach (var behaviour in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
            {
                if (behaviour != null && string.Equals(behaviour.GetType().Name, typeName, StringComparison.Ordinal)) return behaviour;
            }

            return null;
        }

        private static Type FindRuntimeType(string typeName)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(typeName))
                .FirstOrDefault(candidate => candidate != null);
            Assert.That(type, Is.Not.Null, typeName);
            return type;
        }

        private static string[] ListenerKeys()
        {
            return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
                .Where(endpoint => endpoint.Address.Equals(IPAddress.Any))
                .Select(endpoint => endpoint.Address + ":" + endpoint.Port)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }

        private sealed class MemoryCredentialStore :
            IReconnectCredentialStore
        {
            public ReconnectCredential Credential { get; private set; }

            public bool TryLoad(out ReconnectCredential credential)
            {
                credential = Credential;
                return credential != null;
            }

            public void Save(ReconnectCredential credential)
            {
                Credential = credential;
            }

            public void Clear()
            {
                Credential = null;
            }
        }
    }
}
