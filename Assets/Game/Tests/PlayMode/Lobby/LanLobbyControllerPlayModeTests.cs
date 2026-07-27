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
        public IEnumerator LobbyGate_FreezesPreparationUntilHostStart()
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

            Assert.That((bool)loopType.GetProperty("IsLobbyGateActive").GetValue(loop), Is.False);
            Assert.That((float)loopType.GetProperty("RemainingPreparationSeconds").GetValue(loop), Is.EqualTo(30f).Within(.05f));
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

        private static void AssertHome(global::LanLobbyView view)
        {
            Assert.That(view.transform.Find("LanLobbyRoot/Home").gameObject.activeSelf, Is.True);
            Assert.That(view.transform.Find("LanLobbyRoot/Room").gameObject.activeSelf, Is.False);
            Assert.That(view.StatusTextForTests, Is.EqualTo("Left room."));
        }

        private static IEnumerator WaitForTask(Task task)
        {
            for (var frame = 0; frame < 300 && task != null && !task.IsCompleted; frame++)
                yield return null;
            Assert.That(task, Is.Not.Null);
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

        private static string[] ListenerKeys()
        {
            return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
                .Where(endpoint => endpoint.Address.Equals(IPAddress.Any))
                .Select(endpoint => endpoint.Address + ":" + endpoint.Port)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }
    }
}
