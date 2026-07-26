using System.Collections;
using ArknoNights.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ArknoNights.Battle.Tests
{
    public sealed class UI010SceneIntegrationPlayModeTests
    {
        [UnityTest]
        public IEnumerator SampleScene_UI010ConnectsTheLocalShopObserverAndBattlePhaseWithoutDuplicatingPlayerState()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            for (var frame = 0; frame < 20; frame++) yield return null;

            var hud = Object.FindObjectOfType<ArknoNights.UI.StagingHudController>();
            Assert.NotNull(hud);
            var integration = hud.GetComponent("UI010SceneIntegrationController");
            Assert.NotNull(integration, "UI-010 must attach one formal scene integration controller.");
            var type = integration.GetType();
            var shop = type.GetProperty("ShopReady").GetValue(integration);
            var observer = type.GetProperty("Observer").GetValue(integration);
            Assert.NotNull(shop, "UI-010 must initialize the existing local-shop controller with the loop-owned LocalMatchState.");
            Assert.NotNull(observer, "UI-010 must initialize the existing four-player observer coordinator.");

            Assert.That((bool)type.GetMethod("TryObservePlayer").Invoke(integration, new object[] { "local-ui-player-2" }), Is.True);
            yield return null;
            Assert.That(hud.GetType().GetProperty("DisplayedSnapshot").GetValue(hud), Is.Not.Null);
            Assert.That(((PlayerStateSnapshot)hud.GetType().GetProperty("DisplayedSnapshot").GetValue(hud)).PlayerId, Is.EqualTo("local-ui-player-2"));
            var deployment = hud.GetComponent("ArknoNights.Deployment.StateDrivenDeploymentController");
            Assert.That(deployment.GetType().GetProperty("InteractionEnabled").GetValue(deployment), Is.False);

            Assert.That((bool)type.GetMethod("TryObservePlayer").Invoke(integration, new object[] { "local-ui-player" }), Is.True);
            yield return null;
            shop.GetType().GetMethod("ToggleReady").Invoke(shop, null);
            yield return null;
            Assert.That(deployment.GetType().GetProperty("InteractionEnabled").GetValue(deployment), Is.False, "Ready must lock only formation operations.");
            Assert.That((bool)shop.GetType().GetProperty("State").GetValue(shop).GetType().GetProperty("ShopCommandsEnabled").GetValue(shop.GetType().GetProperty("State").GetValue(shop)), Is.True);

            var loop = hud.GetComponent("PreparationBattleLoopController");
            loop.GetType().GetMethod("AdvanceForTests").Invoke(loop, new object[] { 30f });
            yield return null;
            Assert.That((bool)type.GetMethod("TryObservePlayer").Invoke(integration, new object[] { "local-ui-player-4" }), Is.True);
            yield return null;
            var multi = loop.GetType().GetProperty("MultiBattle").GetValue(loop);
            Assert.That(multi.GetType().GetProperty("SelectedMatchId").GetValue(multi), Is.EqualTo("match-cd"));

            loop.GetType().GetMethod("AdvanceForTests").Invoke(loop, new object[] { 1200f });
            yield return null;
            yield return null;
            var match = loop.GetType().GetProperty("MatchState").GetValue(loop);
            var snapshot = match.GetType().GetProperty("Snapshot").GetValue(match);
            Assert.That(snapshot.GetType().GetProperty("ObservedPlayerId").GetValue(snapshot), Is.EqualTo("local-ui-player"));
            Assert.That((bool)snapshot.GetType().GetProperty("LocalPlayer").GetValue(snapshot).GetType().GetProperty("IsReady").GetValue(snapshot.GetType().GetProperty("LocalPlayer").GetValue(snapshot)), Is.False);
        }
    }
}
