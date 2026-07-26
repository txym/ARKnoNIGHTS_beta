using System.Collections;
using ArknoNights.Player;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ArknoNights.Battle.Tests
{
    public sealed class BattleHudSceneIntegrationPlayModeTests
    {
        [UnityTest]
        public IEnumerator SampleScene_FormalHudConnectsLocalShopObserverAndBattlePhaseWithoutDuplicatingPlayerState()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            for (var frame = 0; frame < 20; frame++) yield return null;

            var hud = Object.FindObjectOfType<ArknoNights.UI.StagingHudController>();
            Assert.NotNull(hud);
            var integration = hud.GetComponent("BattleHudSceneCoordinator");
            Assert.NotNull(integration, "The scene must attach one formal HUD coordinator.");
            var type = integration.GetType();
            var shop = type.GetProperty("ShopReady").GetValue(integration);
            var observer = type.GetProperty("Observer").GetValue(integration);
            Assert.NotNull(shop, "The formal HUD must initialize the local-shop controller with the loop-owned LocalMatchState.");
            Assert.NotNull(observer, "The formal HUD must initialize the existing four-player observer coordinator.");

            var canvas = hud.transform.Find("FormalBattleHudCanvas");
            var shopRoot = canvas.Find("ShopReadyHud") as RectTransform;
            var playerListRoot = canvas.Find("PlayerListPanel") as RectTransform;
            Assert.That(shopRoot.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(shopRoot.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(shopRoot.offsetMin, Is.EqualTo(Vector2.zero));
            Assert.That(shopRoot.offsetMax, Is.EqualTo(Vector2.zero));
            Assert.That(playerListRoot.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(playerListRoot.anchorMax, Is.EqualTo(Vector2.one));
            var localPlayerRow = playerListRoot.Find("Player_local-ui-player");
            Assert.NotNull(localPlayerRow);
            Assert.That(localPlayerRow.GetComponent<RectTransform>().anchoredPosition.x, Is.LessThan(150f));
            Assert.NotNull(localPlayerRow.Find("AvatarBorder").GetComponent<Image>().sprite);
            var disconnectedRow = playerListRoot.Find("Player_local-ui-player-4");
            Assert.NotNull(disconnectedRow.Find("LostConnection"));
            Assert.That(
                disconnectedRow.Find("HealthBackground").GetComponent<Image>().sprite.name,
                Is.EqualTo("bg_hp"),
                "Disconnected players keep the normal health background; only the lost-connection icon represents disconnect.");

            shop.GetType().GetMethod("SetShopVisible").Invoke(shop, new object[] { true });
            yield return null;
            var shopPanel = shopRoot.Find("ShopPanel");
            var slotRects = shopPanel.Cast<Transform>()
                .Where(child => child.name.StartsWith("ShopSlot_"))
                .Select(child => (RectTransform)child)
                .OrderBy(child => child.anchoredPosition.x)
                .ToArray();
            Assert.AreEqual(5, slotRects.Length);
            Assert.That(slotRects.Select(slot => slot.anchoredPosition.x).Distinct().Count(), Is.EqualTo(5));
            Assert.That(slotRects.Select(slot => slot.anchoredPosition.y).Distinct().Count(), Is.EqualTo(1));
            Assert.That(slotRects.Select(slot => slot.sizeDelta), Is.All.EqualTo(new Vector2(158f, 175f)));
            Assert.That(slotRects.Select(slot => slot.Find("Background").GetComponent<Image>().sprite), Is.All.Not.Null);

            Assert.That((bool)type.GetMethod("TryObservePlayer").Invoke(integration, new object[] { "local-ui-player-2" }), Is.True);
            yield return null;
            Assert.That(hud.GetType().GetProperty("DisplayedSnapshot").GetValue(hud), Is.Not.Null);
            var displayed = (PlayerStateSnapshot)hud.GetType().GetProperty("DisplayedSnapshot").GetValue(hud);
            Assert.That(displayed.PlayerId, Is.EqualTo("local-ui-player-2"));
            var deployment = hud.GetComponent("ArknoNights.Deployment.StateDrivenDeploymentController");
            Assert.That(deployment.GetType().GetProperty("InteractionEnabled").GetValue(deployment), Is.False);
            var formalHud = hud.GetComponent("FormalBattleHudController");
            formalHud.GetType().GetMethod("SelectObservedPreparationUnitForHud").Invoke(formalHud, new object[] { displayed.Units[0].UnitId });
            yield return null;
            Assert.IsFalse(playerListRoot.gameObject.activeSelf);
            Assert.IsTrue(canvas.Find("FormalHud/UnitInformationPanel").gameObject.activeSelf);
            formalHud.GetType().GetMethod("ClearSelectionForSceneTransition").Invoke(formalHud, null);
            yield return null;
            Assert.IsTrue(playerListRoot.gameObject.activeSelf);

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
