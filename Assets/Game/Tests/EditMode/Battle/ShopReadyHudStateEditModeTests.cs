using System.Linq;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Player;
using ArknoNights.UI.FormalHud.ShopReady;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace ArknoNights.Battle.Tests
{
    public sealed class ShopReadyHudStateEditModeTests
    {
        private const string CatalogPath = "BattleData/unit-catalog-v1";
        private const string MatchPath = "PlayerData/local-match-state-v1";

        [Test]
        public void Project_MapsExactlySixShopSlotsAndExposesTheirCatalogPrices()
        {
            var state = ShopReadyHudState.Project(Load().Snapshot, true, ShopReadyConfirmation.None);

            Assert.AreEqual(6, state.Slots.Count);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5 }, state.Slots.Select(slot => slot.ShopSlotId));
            CollectionAssert.AreEqual(new[] { 1, 1, 1, 1, 1, 1 }, state.Slots.Select(slot => slot.Price));
            CollectionAssert.AreEqual(new[] { 2, 2, 2, 2, 2, 2 }, state.Slots.Select(slot => slot.DeploymentCost));
            Assert.IsTrue(state.ShopCommandsEnabled);
        }

        [Test]
        public void Project_MarksEmptyAndUnaffordableSlotsNonPurchasableWithoutInventingShopState()
        {
            var match = LoadWithGold(1);
            Assert.IsTrue(match.TryPurchase(0).Success);

            var state = ShopReadyHudState.Project(match.Snapshot, true, ShopReadyConfirmation.None);

            Assert.IsTrue(state.Slots[0].IsEmpty);
            Assert.IsFalse(state.Slots[0].CanPurchase);
            Assert.IsFalse(state.Slots[0].CanToggleFrozen);
            Assert.IsTrue(state.Slots.Skip(1).All(slot => !slot.IsEmpty && !slot.CanPurchase && slot.Price > state.Gold));
        }

        [Test]
        public void Project_KeepsFrozenNonEmptySlotPurchasable()
        {
            var match = Load();
            Assert.IsTrue(match.TryToggleFrozen(0).Success);

            var state = ShopReadyHudState.Project(match.Snapshot, true, ShopReadyConfirmation.None);

            Assert.IsTrue(state.Slots[0].IsFrozen);
            Assert.IsFalse(state.Slots[0].IsEmpty);
            Assert.IsTrue(state.Slots[0].CanPurchase);
            Assert.IsTrue(state.Slots[0].CanToggleFrozen);
        }

        [Test]
        public void RequestConfirmation_RequiresTheSameUpgradeActionTwice()
        {
            Assert.AreEqual(ShopReadyConfirmation.Upgrade, ShopReadyHudState.RequestConfirmation(ShopReadyConfirmation.None, ShopReadyConfirmation.Upgrade));
            Assert.AreEqual(ShopReadyConfirmation.None, ShopReadyHudState.RequestConfirmation(ShopReadyConfirmation.Upgrade, ShopReadyConfirmation.Upgrade));
        }


        [Test]
        public void PendingCommand_RequiresTheSamePurchasableSlotTwiceAndClearsOnInvalidation()
        {
            var pending = new ShopReadyPendingCommand();

            Assert.IsFalse(pending.RequestPurchase(2));
            Assert.AreEqual(ShopReadyConfirmation.Purchase, pending.Kind);
            Assert.AreEqual(2, pending.ShopSlotId);
            Assert.IsFalse(pending.RequestPurchase(3));
            Assert.AreEqual(3, pending.ShopSlotId);
            Assert.IsTrue(pending.RequestPurchase(3));
            Assert.AreEqual(ShopReadyConfirmation.None, pending.Kind);

            Assert.IsFalse(pending.RequestFixed(ShopReadyConfirmation.Upgrade));
            pending.Clear();
            Assert.AreEqual(ShopReadyConfirmation.None, pending.Kind);
        }

        [Test]
        public void Controller_PurchaseNeedsSecondSameClickAndAnExternalChangeClearsThePendingSlot()
        {
            var match = Load();
            var root = new GameObject("ShopReadyHudStateEditModeTests");
            try
            {
                var controller = root.AddComponent<ShopReadyHudController>();
                controller.Initialize(match);
                var beforeFirstClick = match.Snapshot.LocalPlayer.Gold;

                controller.Purchase(0);

                Assert.AreEqual(beforeFirstClick, match.Snapshot.LocalPlayer.Gold);
                Assert.IsTrue(match.TryRefresh().Success);
                var afterExternalRefresh = match.Snapshot.LocalPlayer.Gold;
                var refreshedPrice = match.Snapshot.LocalPlayer.ShopSlots[0].Price;

                controller.Purchase(0);

                Assert.AreEqual(afterExternalRefresh, match.Snapshot.LocalPlayer.Gold);
                controller.Purchase(0);
                Assert.AreEqual(afterExternalRefresh - refreshedPrice, match.Snapshot.LocalPlayer.Gold);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Controller_DoesNotEnterUpgradeConfirmationWhenGoldIsInsufficient()
        {
            var match = LoadWithGold(1);
            var root = new GameObject("ShopReadyHudInvalidConfirmationTests", typeof(RectTransform));
            try
            {
                var controller = root.AddComponent<ShopReadyHudController>();
                controller.Initialize(match);

                controller.RequestUpgrade();

                Assert.AreEqual(ShopReadyConfirmation.None, controller.State.PendingConfirmation);
                Assert.AreEqual(1, match.Snapshot.LocalPlayer.Level);
                Assert.AreEqual(1, match.Snapshot.LocalPlayer.Gold);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Controller_FreezeButtonTogglesEveryOccupiedSlotWithOneStateChange()
        {
            var match = Load();
            Assert.IsTrue(match.TryToggleFrozen(0).Success);
            Assert.IsTrue(match.TryPurchase(5).Success);
            var root = new GameObject("BulkShopFreezeTests", typeof(RectTransform));
            try
            {
                var controller = root.AddComponent<ShopReadyHudController>();
                controller.Initialize(match);
                controller.SetShopVisible(true);
                var changes = 0;
                match.Changed += _ => changes++;

                root.transform.Find("ShopPanel/FreezeButton").GetComponent<Button>().onClick.Invoke();

                Assert.AreEqual(1, changes);
                Assert.IsTrue(match.Snapshot.LocalPlayer.ShopSlots.Take(5).All(slot => slot.IsFrozen));
                Assert.IsTrue(match.Snapshot.LocalPlayer.ShopSlots[5].IsEmpty);
                Assert.IsFalse(match.Snapshot.LocalPlayer.ShopSlots[5].IsFrozen);

                changes = 0;
                root.transform.Find("ShopPanel/FreezeButton").GetComponent<Button>().onClick.Invoke();

                Assert.AreEqual(1, changes);
                Assert.IsTrue(match.Snapshot.LocalPlayer.ShopSlots.All(slot => !slot.IsFrozen));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Controller_RefreshExecutesOnFirstClickWithoutPendingConfirmation()
        {
            var match = Load();
            var root = new GameObject("ImmediateShopRefreshTests", typeof(RectTransform));
            try
            {
                var controller = root.AddComponent<ShopReadyHudController>();
                controller.Initialize(match);
                var before = match.Snapshot;

                controller.RequestRefresh();

                Assert.AreEqual(before.LocalPlayer.Gold - LocalMatchState.RefreshCost, match.Snapshot.LocalPlayer.Gold);
                CollectionAssert.AreEqual(
                    new[] { "1000", "1000", "1000", "1000", "1000", "1000" },
                    match.Snapshot.LocalPlayer.ShopSlots.Select(slot => slot.UnitTypeId));
                Assert.AreEqual(ShopReadyConfirmation.None, controller.State.PendingConfirmation);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Controller_ReadyArtworkUsesSlicedImagesWithoutChangingTheHitTarget()
        {
            var match = Load();
            var root = new GameObject("ReadyArtworkTests", typeof(RectTransform));
            try
            {
                var controller = root.AddComponent<ShopReadyHudController>();
                controller.Initialize(match);
                var readyRoot = root.transform.Find("ReadyButton");

                Assert.AreEqual(Image.Type.Sliced, readyRoot.Find("Background").GetComponent<Image>().type);
                Assert.AreEqual(Image.Type.Sliced, readyRoot.Find("Frame").GetComponent<Image>().type);
                Assert.AreEqual(Image.Type.Simple, readyRoot.GetComponent<Image>().type);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Controller_BattlePhaseKeepsShopVisibleAndAllowsPurchaseRefreshFreezeAndUpgrade()
        {
            var match = Load();
            var root = new GameObject("BattlePhaseShopTests", typeof(RectTransform));
            try
            {
                var controller = root.AddComponent<ShopReadyHudController>();
                controller.Initialize(match);
                controller.SetPreparationPhase(false);

                Assert.IsTrue(root.activeSelf);
                Assert.IsFalse(root.transform.Find("ReadyButton").gameObject.activeSelf);
                controller.SetShopVisible(true);
                Assert.IsTrue(controller.State.ShopVisible);
                Assert.IsTrue(root.transform.Find("ShopPanel").gameObject.activeSelf);

                controller.Purchase(0);
                controller.Purchase(0);
                Assert.IsTrue(match.Snapshot.LocalPlayer.ShopSlots[0].IsEmpty);
                Assert.AreEqual(199, match.Snapshot.LocalPlayer.Gold);

                controller.RequestRefresh();
                Assert.AreEqual(198, match.Snapshot.LocalPlayer.Gold);
                Assert.IsTrue(match.Snapshot.LocalPlayer.ShopSlots.All(slot => !slot.IsEmpty));

                controller.ToggleAllFrozen();
                Assert.IsTrue(match.Snapshot.LocalPlayer.ShopSlots.All(slot => slot.IsFrozen));

                controller.RequestUpgrade();
                controller.RequestUpgrade();
                Assert.AreEqual(2, match.Snapshot.LocalPlayer.Level);
                Assert.AreEqual(194, match.Snapshot.LocalPlayer.Gold);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Controller_AffinityRowsHideMissingRegionAndClearAfterSlotBecomesEmpty()
        {
            var catalog = UnitCatalogLoader.LoadFromResources(CatalogPath).Catalog;
            var json = Resources.Load<TextAsset>(MatchPath).text
                .Replace("\"initialLevel\": 1", "\"initialLevel\": 9")
                .Replace(
                    "\"shopTypeIds\": [\"1000\", \"5503\"]",
                    "\"shopTypeIds\": [\"5503\"]");
            var loaded = LocalMatchStateLoader.LoadFromJson(catalog, json);
            Assert.IsTrue(loaded.Success, string.Join("; ", loaded.Errors.Select(error => error.ToString())));
            var root = new GameObject("AffinityRowsTests", typeof(RectTransform));
            try
            {
                var controller = root.AddComponent<ShopReadyHudController>();
                controller.Initialize(loaded.State);
                var firstSlot = root.transform.Find("ShopPanel/ShopSlot_0/PortraitClip");
                Assert.IsTrue(firstSlot.Find("CostInfo").gameObject.activeSelf);
                Assert.IsFalse(firstSlot.Find("RegionInfo").gameObject.activeSelf);
                Assert.IsTrue(firstSlot.Find("OccupationInfo").gameObject.activeSelf);
                Assert.AreEqual("其他", firstSlot.Find("OccupationInfo/Value").GetComponent<Text>().text);
                Assert.IsFalse(firstSlot.Find("OccupationInfo/Icon").gameObject.activeSelf);

                controller.Purchase(0);
                controller.Purchase(0);

                Assert.IsFalse(firstSlot.Find("CostInfo").gameObject.activeSelf);
                Assert.IsFalse(firstSlot.Find("RegionInfo").gameObject.activeSelf);
                Assert.IsFalse(firstSlot.Find("OccupationInfo").gameObject.activeSelf);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Project_ReadyLocksOnlyFormationWhileShopAndFixedButtonsRemainAvailable()
        {
            var match = Load();
            Assert.IsTrue(match.TryToggleReady().Success);

            var state = ShopReadyHudState.Project(match.Snapshot, false, ShopReadyConfirmation.None);
            var layout = ShopReadyHudLayout.Calculate(1920f, 1080f);

            Assert.IsTrue(state.IsReady);
            Assert.IsFalse(state.FormationInteractionEnabled);
            Assert.IsTrue(state.ShopCommandsEnabled);
            Assert.IsTrue(state.Slots.All(slot => slot.CanPurchase));
            Assert.AreEqual(ShopReadyHudLayout.ReferenceReadyButton, layout.ReadyButton);
            Assert.AreEqual(ShopReadyHudLayout.ReferenceShopToggle, layout.ShopToggle);
            Assert.That(layout.ShopPanel.Width, Is.EqualTo(1605f).Within(.001f));
            Assert.That(layout.ShopPanel.Height, Is.EqualTo(420f).Within(.001f));
            Assert.That(layout.ShopPanel.Left, Is.EqualTo(250f).Within(.001f));
            Assert.That(
                layout.ShopPanel.Bottom + layout.ShopPanel.Height,
                Is.EqualTo(layout.LevelPanel.Bottom + 40f).Within(.001f),
                "The shop panel is shifted forty reference units upward.");
            Assert.That(
                layout.ReadyButton.Left + layout.ReadyButton.Width * .5f,
                Is.EqualTo(1830f).Within(.001f),
                "Ready must share the cost and gold panels' vertical centerline.");
        }

        private static LocalMatchState Load()
        {
            var result = LocalMatchStateLoader.LoadFromResources(CatalogPath, MatchPath);
            Assert.IsTrue(result.Success, string.Join("; ", result.Errors.Select(error => error.ToString())));
            return result.State;
        }

        private static LocalMatchState LoadWithGold(int gold)
        {
            var catalog = UnitCatalogLoader.LoadFromResources(CatalogPath).Catalog;
            var json = Resources.Load<TextAsset>(MatchPath).text.Replace("\"initialGold\": 200", "\"initialGold\": " + gold);
            var result = LocalMatchStateLoader.LoadFromJson(catalog, json);
            Assert.IsTrue(result.Success, string.Join("; ", result.Errors.Select(error => error.ToString())));
            return result.State;
        }
    }
}
