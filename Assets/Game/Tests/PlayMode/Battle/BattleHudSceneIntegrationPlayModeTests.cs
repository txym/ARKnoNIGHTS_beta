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
            Assert.That(listBackground.GetComponent<RectTransform>().sizeDelta, Is.EqualTo(new Vector2(116f, 534f)));
            var localPlayerRow = playerListRoot.Find("Player_local-ui-player");
            Assert.NotNull(localPlayerRow);
            Assert.That(localPlayerRow.GetComponent<RectTransform>().anchoredPosition.x, Is.EqualTo(15.7f).Within(.01f));
            Assert.That(localPlayerRow.GetComponent<RectTransform>().sizeDelta, Is.EqualTo(new Vector2(116f * .85f, 126f * .85f)));
            Assert.NotNull(localPlayerRow.Find("AvatarBorder").GetComponent<Image>().sprite);
            Assert.Less(listBackground.GetSiblingIndex(), localPlayerRow.GetSiblingIndex());
            var avatar = localPlayerRow.Find("Avatar").GetComponent<RectTransform>();
            Assert.That(avatar.sizeDelta, Is.EqualTo(new Vector2(92f * .85f, 92f * .85f)));
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
            var life = localPlayerRow.Find("HealthBackground/Life").GetComponent<Text>();
            Assert.AreSame(
                ArknoNights.UI.StagingHudController.FormalNumericFont,
                life.font);
            Assert.AreEqual(HorizontalWrapMode.Overflow, life.horizontalOverflow);
            Assert.AreEqual(VerticalWrapMode.Overflow, life.verticalOverflow);
            var disconnectedRow = playerListRoot.Find("Player_local-ui-player-3");
            Assert.NotNull(disconnectedRow.Find("LostConnection"));
            Assert.That(
                disconnectedRow.Find("HealthBackground").GetComponent<Image>().sprite.name,
                Is.EqualTo("bg_hp"),
                "Disconnected players keep the normal health background; only the lost-connection icon represents disconnect.");
            var exitedRow = playerListRoot.Find("Player_local-ui-player-4");
            var exitedAvatar = exitedRow.Find("Avatar");
            var exitedLostConnection = exitedRow.Find("LostConnection");
            Assert.NotNull(exitedLostConnection, "Exited players keep the disconnected overlay over their replacement avatar.");
            Assert.That(
                exitedLostConnection.GetSiblingIndex(),
                Is.GreaterThan(exitedAvatar.GetSiblingIndex()),
                "The disconnected overlay must render after and above the replacement avatar.");
            Assert.That(
                exitedAvatar.GetComponent<Image>().sprite.name,
                Is.EqualTo("equip_replace_avatart_bg"));

            shop.GetType().GetMethod("SetShopVisible").Invoke(shop, new object[] { true });
            yield return null;
            var shopPanel = shopRoot.Find("ShopPanel");
            var shopPanelRect = shopPanel.GetComponent<RectTransform>();
            var expectedShopLayout = ArknoNights.UI.FormalHud.ShopReady.ShopReadyHudLayout.Calculate(
                shopRoot.rect.width,
                shopRoot.rect.height);
            Assert.That(
                shopPanelRect.anchoredPosition,
                Is.EqualTo(new Vector2(expectedShopLayout.ShopPanel.Left, expectedShopLayout.ShopPanel.Bottom)));
            Assert.That(
                shopPanelRect.sizeDelta,
                Is.EqualTo(new Vector2(expectedShopLayout.ShopPanel.Width, expectedShopLayout.ShopPanel.Height)));
            var levelPanelRect = shopRoot.Find("ShopLevelButton").GetComponent<RectTransform>();
            Assert.That(
                shopPanelRect.anchoredPosition.x + shopPanelRect.sizeDelta.x,
                Is.EqualTo(levelPanelRect.anchoredPosition.x + levelPanelRect.sizeDelta.x - 10f).Within(.01f),
                "The shop panel is shifted ten reference units left of the level control.");
            Assert.That(
                shopPanelRect.anchoredPosition.y + shopPanelRect.sizeDelta.y,
                Is.EqualTo(levelPanelRect.anchoredPosition.y + 40f).Within(.01f));
            var slotRects = shopPanel.Cast<Transform>()
                .Where(child => child.name.StartsWith("ShopSlot_"))
                .Select(child => (RectTransform)child)
                .OrderBy(child => child.anchoredPosition.x)
                .ToArray();
            Assert.AreEqual(6, slotRects.Length);
            Assert.That(slotRects.Select(slot => slot.anchoredPosition.x).Distinct().Count(), Is.EqualTo(6));
            Assert.That(slotRects.Select(slot => slot.anchoredPosition.y).Distinct().Count(), Is.EqualTo(1));
            Assert.That(slotRects.Select(slot => slot.sizeDelta), Is.All.EqualTo(new Vector2(237f, 262.5f)));
            Assert.That(slotRects.Select(slot => slot.Find("Background").GetComponent<Image>().sprite), Is.All.Not.Null);
            var firstSlot = slotRects[0];
            var lastSlot = slotRects[5];
            Assert.That(
                lastSlot.anchoredPosition.x + lastSlot.sizeDelta.x,
                Is.EqualTo(shopPanelRect.rect.width).Within(.01f),
                "The six-card group must be right aligned.");
            var upgrade = shopPanel.Find("UpgradeButton").GetComponent<RectTransform>();
            Assert.That(
                upgrade.anchoredPosition.x + upgrade.sizeDelta.x,
                Is.EqualTo(firstSlot.anchoredPosition.x - 12f).Within(.01f),
                "Upgrade belongs immediately left of the card group.");
            var upgradeCostBackground = upgrade.Find("UpgradeCostBackground");
            Assert.NotNull(upgradeCostBackground, "Upgrade price needs the same cost background language as product prices.");
            var upgradeCostRect = upgradeCostBackground.GetComponent<RectTransform>();
            Assert.That(
                upgradeCostRect.anchoredPosition.x + upgradeCostRect.sizeDelta.x * .5f,
                Is.EqualTo(upgrade.rect.width * .5f).Within(.01f));
            Assert.That(
                upgradeCostRect.anchoredPosition.y + upgradeCostRect.sizeDelta.y * .5f,
                Is.EqualTo(upgrade.rect.height - 15f).Within(.01f));
            var upgradeCostTextRect = upgrade.Find("Cost").GetComponent<RectTransform>();
            Assert.That(
                upgradeCostTextRect.anchoredPosition.y + upgradeCostTextRect.sizeDelta.y * .5f,
                Is.EqualTo(upgrade.rect.height - 12f).Within(.01f));
            var upgradeGradientRect = upgrade.Find("ConfirmationGradient").GetComponent<RectTransform>();
            Assert.That(upgradeGradientRect.anchoredPosition.y, Is.EqualTo(0f).Within(.01f));
            Assert.That(
                upgradeGradientRect.anchoredPosition.y + upgradeGradientRect.sizeDelta.y,
                Is.EqualTo(81f).Within(.01f),
                "The confirmation gradient must end at the visible level background's lower edge.");
            var slotCostBackground = firstSlot.Find("CostBackground").GetComponent<RectTransform>();
            Assert.That(
                slotCostBackground.anchoredPosition.x + slotCostBackground.sizeDelta.x * .5f,
                Is.EqualTo(firstSlot.rect.width * .5f - 1f).Within(.01f));
            Assert.That(
                slotCostBackground.anchoredPosition.y + slotCostBackground.sizeDelta.y * .5f,
                Is.EqualTo(firstSlot.rect.height - 15f).Within(.01f));
            var slotPrice = firstSlot.Find("Price").GetComponent<RectTransform>();
            Assert.That(
                slotPrice.anchoredPosition.x + slotPrice.sizeDelta.x * .5f,
                Is.EqualTo(slotCostBackground.anchoredPosition.x + slotCostBackground.sizeDelta.x * .5f).Within(.01f));
            Assert.That(
                slotPrice.anchoredPosition.y + slotPrice.sizeDelta.y * .5f,
                Is.EqualTo(slotCostBackground.anchoredPosition.y + slotCostBackground.sizeDelta.y * .5f + 3f).Within(.01f),
                "The price glyph needs a small visual lift inside the cost background.");

            var freeze = shopPanel.Find("FreezeButton").GetComponent<RectTransform>();
            var refresh = shopPanel.Find("RefreshButton").GetComponent<RectTransform>();
            var freezeIcon = freeze.Find("Icon").GetComponent<RectTransform>();
            var freezeLabel = freeze.Find("Label").GetComponent<RectTransform>();
            var refreshIcon = refresh.Find("Icon").GetComponent<RectTransform>();
            var refreshLabel = refresh.Find("Label").GetComponent<RectTransform>();
            Assert.That(freeze.anchoredPosition.x, Is.EqualTo(1126f).Within(.01f));
            Assert.That(refresh.anchoredPosition.x, Is.EqualTo(1368f).Within(.01f));
            Assert.That(freeze.anchoredPosition.y, Is.EqualTo(-30f).Within(.01f));
            Assert.That(refresh.anchoredPosition.y, Is.EqualTo(-30f).Within(.01f));
            Assert.That(
                freezeIcon.anchoredPosition.y + freezeIcon.sizeDelta.y * .5f,
                Is.EqualTo(freeze.rect.height * .5f + 9f).Within(.01f));
            Assert.That(
                freezeLabel.anchoredPosition.y + freezeLabel.sizeDelta.y * .5f,
                Is.EqualTo(freeze.rect.height * .5f + 9f).Within(.01f));
            Assert.That(
                refreshIcon.anchoredPosition.y + refreshIcon.sizeDelta.y * .5f,
                Is.EqualTo(refresh.rect.height * .5f + 9f).Within(.01f));
            Assert.That(
                refreshLabel.anchoredPosition.y + refreshLabel.sizeDelta.y * .5f,
                Is.EqualTo(refresh.rect.height * .5f + 9f).Within(.01f));
            var refreshCostBackground = refresh.Find("CostBackground");
            Assert.NotNull(refreshCostBackground, "Refresh price needs the same cost background as the other shop prices.");
            var refreshCostBackgroundRect = refreshCostBackground.GetComponent<RectTransform>();
            var refreshCost = refresh.Find("Cost").GetComponent<RectTransform>();
            var refreshCostText = refreshCost.GetComponent<Text>();
            Assert.That(
                refreshCostBackgroundRect.anchoredPosition.x + refreshCostBackgroundRect.sizeDelta.x * .5f,
                Is.EqualTo(refresh.rect.width * .5f).Within(.01f));
            Assert.That(
                refreshCostBackgroundRect.anchoredPosition.y,
                Is.EqualTo(0f).Within(.01f),
                "The refresh cost background's lower edge must touch the refresh artwork's lower edge.");
            Assert.That(
                refreshCost.anchoredPosition.y + refreshCost.sizeDelta.y * .5f,
                Is.EqualTo(refreshCostBackgroundRect.anchoredPosition.y + refreshCostBackgroundRect.sizeDelta.y * .5f + 3f).Within(.01f));
            Assert.AreEqual("cost_bg_1", refreshCostBackground.GetComponent<Image>().sprite.name);
            Assert.IsTrue(refreshCostText.gameObject.activeSelf);

            var setRefreshFreePresentation = shop.GetType().GetMethod("SetRefreshFreePresentation");
            Assert.NotNull(setRefreshFreePresentation, "The refresh HUD must expose a UI-only free-cost presentation hook.");
            var goldBeforeFreePresentation = (int)shop.GetType().GetProperty("State").GetValue(shop).GetType().GetProperty("Gold").GetValue(shop.GetType().GetProperty("State").GetValue(shop));
            setRefreshFreePresentation.Invoke(shop, new object[] { true });
            Assert.AreEqual("cost_free", refreshCostBackground.GetComponent<Image>().sprite.name);
            Assert.IsFalse(refreshCostText.gameObject.activeSelf);
            Assert.AreEqual(
                goldBeforeFreePresentation,
                (int)shop.GetType().GetProperty("State").GetValue(shop).GetType().GetProperty("Gold").GetValue(shop.GetType().GetProperty("State").GetValue(shop)),
                "The reserved presentation hook must not alter the current refresh economy.");
            setRefreshFreePresentation.Invoke(shop, new object[] { false });
            Assert.AreEqual("cost_bg_1", refreshCostBackground.GetComponent<Image>().sprite.name);
            Assert.IsTrue(refreshCostText.gameObject.activeSelf);

            var numericFont = ArknoNights.UI.StagingHudController.FormalNumericFont;
            var shopLevelText = shopRoot.Find("ShopLevelButton/Level").GetComponent<Text>();
            var upgradeLevelText = upgrade.Find("Level").GetComponent<Text>();
            Assert.AreSame(numericFont, shopLevelText.font);
            Assert.AreSame(numericFont, firstSlot.Find("Price").GetComponent<Text>().font);
            Assert.AreSame(numericFont, upgrade.Find("Cost").GetComponent<Text>().font);
            Assert.AreSame(numericFont, shopPanel.Find("RefreshButton/Cost").GetComponent<Text>().font);
            Assert.That(shopLevelText.color, Is.EqualTo(new Color(40f / 255f, 221f / 255f, 169f / 255f, 1f)));
            Assert.That(upgradeLevelText.color, Is.EqualTo(new Color(99f / 255f, 222f / 255f, 189f / 255f, 1f)));
            Assert.AreNotEqual(shopLevelText.color, upgradeLevelText.color);
            var shopChineseFont = Resources.Load<Font>("Fonts/FangZhengHeiTiJianTi-1");
            Assert.NotNull(shopChineseFont, "The approved shop Chinese font must be available from Resources.");
            Assert.AreSame(shopChineseFont, firstSlot.Find("UnitName").GetComponent<Text>().font);
            Assert.AreSame(shopChineseFont, shopPanel.Find("FreezeButton/Label").GetComponent<Text>().font);
            Assert.AreSame(shopChineseFont, shopPanel.Find("RefreshButton/Label").GetComponent<Text>().font);
            Assert.AreSame(shopChineseFont, shopRoot.Find("ReadyButton/Label").GetComponent<Text>().font);
            Assert.AreNotEqual(Color.white, shopPanel.Find("FreezeButton/Label").GetComponent<Text>().color);
            Assert.AreNotEqual(Color.white, shopPanel.Find("RefreshButton/Label").GetComponent<Text>().color);
            Assert.AreNotEqual(Color.white, shopRoot.Find("ReadyButton/Label").GetComponent<Text>().color);
            Assert.AreEqual("ready_icon", shopRoot.Find("ReadyButton/Icon").GetComponent<Image>().sprite.name);
            var readyRect = shopRoot.Find("ReadyButton").GetComponent<RectTransform>();
            var goldRect = canvas.Find("FormalHud/GoldCurrencyPanel").GetComponent<RectTransform>();
            var deploymentCostRect = canvas.Find("DeploymentCostPanel").GetComponent<RectTransform>();
            Assert.That(
                readyRect.TransformPoint(readyRect.rect.center).x,
                Is.EqualTo(goldRect.TransformPoint(goldRect.rect.center).x).Within(.01f));
            Assert.That(
                readyRect.TransformPoint(readyRect.rect.center).x,
                Is.EqualTo(deploymentCostRect.TransformPoint(deploymentCostRect.rect.center).x).Within(.01f));

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
            Assert.IsFalse(shopPanel.gameObject.activeSelf, "Opening unit information must close the shop.");
            shop.GetType().GetMethod("SetShopVisible").Invoke(shop, new object[] { true });
            yield return null;
            Assert.IsFalse(canvas.Find("FormalHud/UnitInformationPanel").gameObject.activeSelf, "Opening the shop must clear unit information.");
            Assert.IsTrue(shopPanel.gameObject.activeSelf);
            Assert.IsTrue(playerListRoot.gameObject.activeSelf);
            shop.GetType().GetMethod("SetShopVisible").Invoke(shop, new object[] { false });

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
            Assert.IsTrue(shopRoot.gameObject.activeSelf);
            Assert.IsTrue(shopRoot.Find("ShopLevelButton").gameObject.activeSelf);
            Assert.IsFalse(shopRoot.Find("ReadyButton").gameObject.activeSelf);
            shop.GetType().GetMethod("SetShopVisible").Invoke(shop, new object[] { true });
            yield return null;
            Assert.IsTrue(shopPanel.gameObject.activeSelf, "The local shop must be openable during battle.");
            var battleSnapshot = (LocalMatchSnapshot)loop.GetType().GetProperty("MatchState").GetValue(loop).GetType().GetProperty("Snapshot").GetValue(loop.GetType().GetProperty("MatchState").GetValue(loop));
            var battleGold = battleSnapshot.LocalPlayer.Gold;
            shop.GetType().GetMethod("Purchase").Invoke(shop, new object[] { 0 });
            shop.GetType().GetMethod("Purchase").Invoke(shop, new object[] { 0 });
            shop.GetType().GetMethod("RequestRefresh").Invoke(shop, null);
            shop.GetType().GetMethod("ToggleAllFrozen").Invoke(shop, null);
            shop.GetType().GetMethod("RequestUpgrade").Invoke(shop, null);
            shop.GetType().GetMethod("RequestUpgrade").Invoke(shop, null);
            yield return null;
            var battleShopState = shop.GetType().GetProperty("State").GetValue(shop);
            Assert.That((int)battleShopState.GetType().GetProperty("Gold").GetValue(battleShopState), Is.EqualTo(battleGold - 6));
            Assert.That((int)battleShopState.GetType().GetProperty("Level").GetValue(battleShopState), Is.EqualTo(2));
            Assert.That((bool)type.GetMethod("TryObservePlayer").Invoke(integration, new object[] { "local-ui-player-4" }), Is.True);
            yield return null;
            var multi = loop.GetType().GetProperty("MultiBattle").GetValue(loop);
            Assert.That(multi.GetType().GetProperty("SelectedMatchId").GetValue(multi), Is.EqualTo("match-cd"));

            loop.GetType().GetMethod("AdvanceForTests").Invoke(loop, new object[] { 1200f });
            Assert.That(loop.GetType().GetProperty("Phase").GetValue(loop).ToString(), Is.EqualTo("Battle"),
                "The formal HUD must remain in battle while terminal death presentation is pending.");
            var completionDeadline = Time.realtimeSinceStartup + 5f;
            while (loop.GetType().GetProperty("Phase").GetValue(loop).ToString() == "Battle" &&
                   Time.realtimeSinceStartup < completionDeadline)
                yield return null;
            Assert.That(loop.GetType().GetProperty("Phase").GetValue(loop).ToString(), Is.EqualTo("Preparation"),
                "Terminal death presentation did not settle before the formal HUD timeout.");
            yield return null;
            var match = loop.GetType().GetProperty("MatchState").GetValue(loop);
            var snapshot = match.GetType().GetProperty("Snapshot").GetValue(match);
            Assert.That(snapshot.GetType().GetProperty("ObservedPlayerId").GetValue(snapshot), Is.EqualTo("local-ui-player"));
            Assert.That((bool)snapshot.GetType().GetProperty("LocalPlayer").GetValue(snapshot).GetType().GetProperty("IsReady").GetValue(snapshot.GetType().GetProperty("LocalPlayer").GetValue(snapshot)), Is.False);
        }
    }
}
