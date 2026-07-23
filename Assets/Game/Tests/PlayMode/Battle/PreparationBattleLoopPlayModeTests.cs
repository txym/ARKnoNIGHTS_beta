using System.Collections;
using System.Linq;
using ArknoNights.Battle.Demo;
using ArknoNights.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ArknoNights.Battle.Tests
{
    public sealed class PreparationBattleLoopPlayModeTests
    {
        [UnityTest]
        public IEnumerator SampleScene_AutoLoopsPreparationToBattleAndBackWithoutWritingCombatResultToPlayerState()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            for (var frame = 0; frame < 10; frame++) yield return null;

            var hud = Object.FindObjectOfType<ArknoNights.UI.StagingHudController>();
            Assert.NotNull(hud);
            var loop = hud.GetComponent("PreparationBattleLoopController");
            Assert.NotNull(loop, "UI-004 bootstrap did not attach the unique phase bridge.");
            var loopType = loop.GetType();
            Assert.AreEqual("Preparation", loopType.GetProperty("Phase").GetValue(loop).ToString());
            Assert.That((float)loopType.GetProperty("RemainingPreparationSeconds").GetValue(loop), Is.InRange(29f, 30f));

            loopType.GetMethod("AdvanceForTests").Invoke(loop, new object[] { 30f });
            yield return null;
            var demo = Object.FindObjectOfType<BattleDemoController>();
            Assert.NotNull(demo);
            Assert.AreEqual(BattleDemoState.Playing, demo.State, demo.Coordinator.LastError);
            Assert.IsFalse((bool)hud.GetComponent("ArknoNights.Deployment.StateDrivenDeploymentController").GetType().GetProperty("InteractionEnabled").GetValue(hud.GetComponent("ArknoNights.Deployment.StateDrivenDeploymentController")));
            Assert.AreEqual("local-5503-alpha", demo.Coordinator.Input.Players.Single(player => player.Side == ArknoNights.Battle.Core.BattleSide.Home).Units.Single(unit => unit.Zone == ArknoNights.Battle.Core.UnitZone.Deployed).UnitId);
            Assert.AreEqual(87, hud.PlayerState.DeploymentCost);
            Assert.IsFalse(hud.PlayerState.Snapshot.Units.Any(unit => unit.UnitId == "local-1000-overflow"));

            demo.Coordinator.Advance(1200f);
            Assert.AreEqual(BattleDemoState.Completed, demo.State, demo.Coordinator.LastError);
            yield return null;
            yield return null;

            Assert.AreEqual("Preparation", loopType.GetProperty("Phase").GetValue(loop).ToString());
            Assert.That((float)loopType.GetProperty("RemainingPreparationSeconds").GetValue(loop), Is.InRange(29f, 30f));
            Assert.AreEqual(BattleDemoState.Idle, demo.State);
            Assert.AreEqual(87, hud.PlayerState.DeploymentCost, "Combat death/HP/winner must not write back to persistent player state.");
            Assert.AreEqual(1, hud.PlayerState.GetUnits(PlayerUnitZone.Deployed).Count);
            Assert.NotNull(GameObject.Find("PreparationUnitViews"));
        }
    }
}
