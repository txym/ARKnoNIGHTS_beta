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
            var multiProperty = loopType.GetProperty("MultiBattle");
            Assert.NotNull(multiProperty, "UI-009 scene integration must expose the active multi-battle coordinator.");
            var multi = multiProperty.GetValue(loop);
            Assert.NotNull(multi);
            Assert.AreEqual("Playing", multi.GetType().GetProperty("State").GetValue(multi).ToString());
            var matches = (System.Collections.IEnumerable)multi.GetType().GetProperty("Matches").GetValue(multi);
            Assert.AreEqual(2, matches.Cast<object>().Count());
            Assert.IsFalse((bool)hud.GetComponent("ArknoNights.Deployment.StateDrivenDeploymentController").GetType().GetProperty("InteractionEnabled").GetValue(hud.GetComponent("ArknoNights.Deployment.StateDrivenDeploymentController")));
            var first = matches.Cast<object>().Single(item => (string)item.GetType().GetProperty("MatchId").GetValue(item) == "match-ab");
            var firstInput = (ArknoNights.Battle.Core.BattleInput)first.GetType().GetProperty("Input").GetValue(first);
            Assert.AreEqual("local-5503-alpha", firstInput.Players.Single(player => player.Side == ArknoNights.Battle.Core.BattleSide.Home).Units.Single(unit => unit.Zone == ArknoNights.Battle.Core.UnitZone.Deployed).UnitId);
            loopType.GetMethod("AdvanceForTests").Invoke(loop, new object[] { 0.05f });
            var sharedTick = (double)multi.GetType().GetProperty("PresentationTick").GetValue(multi);
            Assert.That(sharedTick, Is.GreaterThan(0d));
            var results = matches.Cast<object>().Select(item => item.GetType().GetProperty("Result").GetValue(item)).ToArray();
            var observe = loopType.GetMethod("TryObserveBattlePlayer");
            Assert.NotNull(observe, "UI-009 must expose a battle-only observer switch for the player-list binding.");
            Assert.IsTrue((bool)observe.Invoke(loop, new object[] { "local-ui-player-4" }));
            Assert.AreEqual("match-cd", multi.GetType().GetProperty("SelectedMatchId").GetValue(multi));
            Assert.AreEqual(ArknoNights.Battle.Presentation.BattleObserverView.Away, multi.GetType().GetProperty("Observer").GetValue(multi));
            Assert.AreEqual(sharedTick, (double)multi.GetType().GetProperty("PresentationTick").GetValue(multi));
            CollectionAssert.AreEqual(results, matches.Cast<object>().Select(item => item.GetType().GetProperty("Result").GetValue(item)).ToArray());
            Assert.AreEqual(87, hud.PlayerState.DeploymentCost);
            Assert.IsFalse(hud.PlayerState.Snapshot.Units.Any(unit => unit.UnitId == "local-1000-overflow"));

            loopType.GetMethod("AdvanceForTests").Invoke(loop, new object[] { 1200f });
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
