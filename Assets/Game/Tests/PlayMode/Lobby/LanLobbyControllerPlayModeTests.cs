using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using ArknoNights.Lobby;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ArknoNights.Lobby.Tests
{
    public sealed class LanLobbyControllerPlayModeTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
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
