using System.Linq;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Player;
using ArknoNights.UI.FormalHud.ShopReady;
using NUnit.Framework;
using UnityEngine;

namespace ArknoNights.Battle.Tests
{
    public sealed class ShopReadyHudStateEditModeTests
    {
        private const string CatalogPath = "BattleData/unit-catalog-v1";
        private const string MatchPath = "PlayerData/local-match-state-v1";

        [Test]
        public void Project_MapsExactlyFiveShopSlotsAndExposesTheirCatalogPrices()
        {
            var state = ShopReadyHudState.Project(Load().Snapshot, true, ShopReadyConfirmation.None);

            Assert.AreEqual(5, state.Slots.Count);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, state.Slots.Select(slot => slot.ShopSlotId));
            CollectionAssert.AreEqual(new[] { 1, 1, 1, 1, 1 }, state.Slots.Select(slot => slot.Price));
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
        public void RequestConfirmation_RequiresTheSameRefreshOrUpgradeActionTwice()
        {
            Assert.AreEqual(ShopReadyConfirmation.Refresh, ShopReadyHudState.RequestConfirmation(ShopReadyConfirmation.None, ShopReadyConfirmation.Refresh));
            Assert.AreEqual(ShopReadyConfirmation.None, ShopReadyHudState.RequestConfirmation(ShopReadyConfirmation.Refresh, ShopReadyConfirmation.Refresh));
            Assert.AreEqual(ShopReadyConfirmation.Upgrade, ShopReadyHudState.RequestConfirmation(ShopReadyConfirmation.Refresh, ShopReadyConfirmation.Upgrade));
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

            Assert.IsFalse(pending.RequestFixed(ShopReadyConfirmation.Refresh));
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
            var json = Resources.Load<TextAsset>(MatchPath).text.Replace("\"initialGold\": 7", "\"initialGold\": " + gold);
            var result = LocalMatchStateLoader.LoadFromJson(catalog, json);
            Assert.IsTrue(result.Success, string.Join("; ", result.Errors.Select(error => error.ToString())));
            return result.State;
        }
    }
}
