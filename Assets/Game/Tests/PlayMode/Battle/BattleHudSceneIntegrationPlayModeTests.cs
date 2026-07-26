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
            var listBackground = playerListRoot.Find("Background");
            Assert.NotNull(listBackground, "The player list needs its shared black background.");
            Assert.AreEqual("bg_player_list", listBackground.GetComponent<Image>().sprite.name);
            var localPlayerRow = playerListRoot.Find("Player_local-ui-player");
            Assert.NotNull(localPlayerRow);
            Assert.That(localPlayerRow.GetComponent<RectTransform>().anchoredPosition.x, Is.LessThan(150f));
            Assert.NotNull(localPlayerRow.Find("AvatarBorder").GetComponent<Image>().sprite);
            Assert.Less(listBackground.GetSiblingIndex(), localPlayerRow.GetSiblingIndex());
            var avatar = localPlayerRow.Find("Avatar").GetComponent<RectTransform>();
            var health = localPlayerRow.Find("HealthBackground").GetComponent<RectTransform>();
            var self = localPlayerRow.Find("Self").GetComponent<RectTransform>();
            Assert.That(health.anchoredPosition.x - health.sizeDelta.x * health.pivot.x,
                Is.GreaterThanOrEqualTo(avatar.anchoredPosition.x - avatar.sizeDelta.x * avatar.pivot.x - .01f));
            Assert.That(health.anchoredPosition.x + health.sizeDelta.x * (1f - health.pivot.x),
                Is.LessThanOrEqualTo(avatar.anchoredPosition.x + avatar.sizeDelta.x * (1f - avatar.pivot.x) + .01f));
            Assert.That(health.anchoredPosition.y - health.sizeDelta.y * health.pivot.y,
                Is.GreaterThanOrEqualTo(avatar.anchoredPosition.y - avatar.sizeDelta.y * avatar.pivot.y - .01f));
            Assert.That(health.anchoredPosition.y + health.sizeDelta.y * (1f - health.pivot.y),
                Is.LessThanOrEqualTo(avatar.anchoredPosition.y + avatar.sizeDelta.y * (1f - avatar.pivot.y) + .01f));
            Assert.That(self.anchoredPosition.x - self.sizeDelta.x * self.pivot.x,
                Is.EqualTo(avatar.anchoredPosition.x - avatar.sizeDelta.x * avatar.pivot.x).Within(.01f));
            Assert.AreSame(
                ArknoNights.UI.StagingHudController.FormalNumericFont,
                localPlayerRow.Find("HealthBackground/Life").GetComponent<Text>().font);
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
            var firstSlot = slotRects[0];
            var lastSlot = slotRects[4];
            var shopPanelRect = shopPanel.GetComponent<RectTransform>();
            Assert.That(
                lastSlot.anchoredPosition.x + lastSlot.sizeDelta.x,
                Is.EqualTo(shopPanelRect.rect.width).Within(.01f),
                "The five-card group must be right aligned.");
            var upgrade = shopPanel.Find("UpgradeButton").GetComponent<RectTransform>();
            Assert.That(
                upgrade.anchoredPosition.x + upgrade.sizeDelta.x,
                Is.EqualTo(firstSlot.anchoredPosition.x - 8f).Within(.01f),
                "Upgrade belongs immediately left of the card group.");
            var upgradeCostBackground = upgrade.Find("UpgradeCostBackground");
            Assert.NotNull(upgradeCostBackground, "Upgrade price needs the same cost background language as product prices.");
            var slotCostBackground = firstSlot.Find("CostBackground").GetComponent<RectTransform>();
            Assert.That(
                slotCostBackground.anchoredPosition.x + slotCostBackground.sizeDelta.x * .5f,
                Is.EqualTo(firstSlot.rect.width * .5f).Within(.01f));
            Assert.That(
                slotCostBackground.anchoredPosition.y + slotCostBackground.sizeDelta.y * .5f,
                Is.EqualTo(firstSlot.rect.height).Within(.01f));

            var numericFont = ArknoNights.UI.StagingHudController.FormalNumericFont;
            Assert.AreSame(numericFont, shopRoot.Find("ShopLevelButton/Level").GetComponent<Text>().font);
            Assert.AreSame(numericFont, firstSlot.Find("Price").GetComponent<Text>().font);
            Assert.AreSame(numericFont, upgrade.Find("Cost").GetComponent<Text>().font);
            Assert.AreSame(numericFont, shopPanel.Find("RefreshButton/Cost").GetComponent<Text>().font);
            var boldFontProperty = typeof(ArknoNights.UI.StagingHudController).GetProperty("FormalBoldUiFont");
            Assert.NotNull(boldFontProperty, "Compact Chinese labels need an explicit heavier font boundary.");
            var boldFont = (Font)boldFontProperty.GetValue(null);
            Assert.AreSame(boldFont, firstSlot.Find("UnitName").GetComponent<Text>().font);
            Assert.AreSame(boldFont, shopPanel.Find("FreezeButton/Label").GetComponent<Text>().font);
            Assert.AreSame(boldFont, shopPanel.Find("RefreshButton/Label").GetComponent<Text>().font);
            Assert.AreSame(boldFont, shopRoot.Find("ReadyButton/Label").GetComponent<Text>().font);
            Assert.AreNotEqual(Color.white, shopPanel.Find("FreezeButton/Label").GetComponent<Text>().color);
            Assert.AreNotEqual(Color.white, shopPanel.Find("RefreshButton/Label").GetComponent<Text>().color);
            Assert.AreNotEqual(Color.white, shopRoot.Find("ReadyButton/Label").GetComponent<Text>().color);
            Assert.AreEqual("ready_icon", shopRoot.Find("ReadyButton/Icon").GetComponent<Image>().sprite.name);

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
            Assert.AreEqual("icon_ready", shopRoot.Find("ReadyButton/Icon").GetComponent<Image>().sprite.name);
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
