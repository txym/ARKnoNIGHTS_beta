using System;
using System.Collections;
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

        private static IEnumerator WaitForSceneBootstrap()
        {
            for (var frame = 0; frame < 20; frame++)
            {
                if (FindComponent("PreparationBattleLoopController") != null && FindComponent("LanLobbyController") != null)
                    yield break;
                yield return null;
            }
        }

        private static Component FindComponent(string typeName)
        {
            foreach (var behaviour in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
            {
                if (behaviour != null && string.Equals(behaviour.GetType().Name, typeName, StringComparison.Ordinal)) return behaviour;
            }

            return null;
        }
    }
}
