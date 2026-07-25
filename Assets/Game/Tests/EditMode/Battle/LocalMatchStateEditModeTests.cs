using System.Linq;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Player;
using NUnit.Framework;
using UnityEngine;

namespace ArknoNights.Battle.Tests
{
    public sealed class LocalMatchStateEditModeTests
    {
        private const string CatalogPath = "BattleData/unit-catalog-v1";
        private const string MatchPath = "PlayerData/local-match-state-v1";

        [Test]
        public void FixedMatch_LoadsFourIndependentPlayersAndTheInitialFiveSlotShopDeterministically()
        {
            var first = LocalMatchStateLoader.LoadFromResources(CatalogPath, MatchPath);
            var second = LocalMatchStateLoader.LoadFromResources(CatalogPath, MatchPath);

            Assert.IsTrue(first.Success, Errors(first.Errors));
            Assert.IsTrue(second.Success, Errors(second.Errors));
            Assert.AreEqual("local-ui-player", first.State.LocalPlayerId);
            Assert.AreEqual("local-ui-player", first.State.ObservedPlayerId);
            Assert.AreEqual(4, first.State.Snapshot.Players.Count);
            CollectionAssert.AllItemsAreUnique(first.State.Snapshot.Players.Select(player => player.PlayerId));
            CollectionAssert.AllItemsAreUnique(first.State.Snapshot.Players.SelectMany(player => player.PlayerState.Units).Select(unit => unit.UnitId));

            var local = first.State.Snapshot.LocalPlayer;
            Assert.AreEqual(1, local.Level);
            Assert.AreEqual(7, local.Gold);
            Assert.AreEqual(400, local.Life);
            Assert.IsFalse(local.IsReady);
            Assert.AreEqual(5, local.ShopSlots.Count);
            CollectionAssert.AreEqual(new[] { "1000", "1000", "1000", "1000", "1000" }, local.ShopSlots.Select(slot => slot.UnitTypeId));
            CollectionAssert.AreEqual(new[] { 1, 1, 1, 1, 1 }, local.ShopSlots.Select(slot => slot.Price));
            Assert.AreEqual(first.State.Snapshot.CanonicalSummary, second.State.Snapshot.CanonicalSummary);
        }

        [Test]
        public void Purchase_AddsAUniquePlayerUnitAndClearsTheSlotAtomically()
        {
            var state = Load();
            var before = state.Snapshot;

            var result = state.TryPurchase(0);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(before.LocalPlayer.Gold - 1, result.Snapshot.LocalPlayer.Gold);
            Assert.IsTrue(result.Snapshot.LocalPlayer.ShopSlots[0].IsEmpty);
            Assert.AreEqual("local-ui-player-shop-0001", result.PurchasedUnitId);
            var added = result.Snapshot.LocalPlayer.PlayerState.Units.Single(unit => unit.UnitId == result.PurchasedUnitId);
            Assert.AreEqual("1000", added.TypeId);
            Assert.AreEqual(PlayerUnitZone.Staging, added.Zone);
            CollectionAssert.AllItemsAreUnique(result.Snapshot.Players.SelectMany(player => player.PlayerState.Units).Select(unit => unit.UnitId));
        }

        [Test]
        public void FailedShopCommands_LeaveGoldSlotsAndPlayerStateUntouched()
        {
            var state = Load();
            Assert.IsTrue(state.TryPurchase(0).Success);
            Assert.IsTrue(state.TryPurchase(1).Success);
            Assert.IsTrue(state.TryPurchase(2).Success);
            Assert.IsTrue(state.TryPurchase(3).Success);
            Assert.IsTrue(state.TryPurchase(4).Success);
            var beforeEmptyPurchase = state.Snapshot.CanonicalSummary;

            Assert.AreEqual(LocalMatchOperationCode.ShopSlotEmpty, state.TryPurchase(0).Code);
            Assert.AreEqual(beforeEmptyPurchase, state.Snapshot.CanonicalSummary);
            Assert.IsTrue(state.TryRefresh().Success);
            Assert.IsTrue(state.TryRefresh().Success);
            var beforeInsufficientRefresh = state.Snapshot.CanonicalSummary;

            Assert.AreEqual(LocalMatchOperationCode.InsufficientGold, state.TryRefresh().Code);
            Assert.AreEqual(beforeInsufficientRefresh, state.Snapshot.CanonicalSummary);
        }

        [Test]
        public void Refresh_PreservesFrozenItemsAndReplacesOtherSlotsFromTheNextFixedPage()
        {
            var state = Load();
            Assert.IsTrue(state.TryToggleFrozen(0).Success);

            var result = state.TryRefresh();

            Assert.IsTrue(result.Success);
            Assert.AreEqual(6, result.Snapshot.LocalPlayer.Gold);
            Assert.IsTrue(result.Snapshot.LocalPlayer.ShopSlots[0].IsFrozen);
            CollectionAssert.AreEqual(new[] { "1000", "5503", "5503", "5503", "5503" }, result.Snapshot.LocalPlayer.ShopSlots.Select(slot => slot.UnitTypeId));
            Assert.IsTrue(state.TryPurchase(0).Success);
            Assert.IsTrue(state.Snapshot.LocalPlayer.ShopSlots[0].IsEmpty);
            Assert.IsFalse(state.Snapshot.LocalPlayer.ShopSlots[0].IsFrozen);
        }

        [Test]
        public void UpgradeReadyAndObservation_ChangeOnlyLocalSessionState()
        {
            var state = Load();
            var observedPlayerState = state.Snapshot.Players.Single(player => player.PlayerId == "local-ui-player-2").PlayerState.CanonicalSummary;

            var upgrade = state.TryUpgrade();
            Assert.IsTrue(upgrade.Success);
            Assert.AreEqual(2, upgrade.Snapshot.LocalPlayer.Level);
            Assert.AreEqual(3, upgrade.Snapshot.LocalPlayer.Gold);

            Assert.IsTrue(state.TryToggleReady().Success);
            Assert.IsTrue(state.Snapshot.LocalPlayer.IsReady);
            Assert.IsTrue(state.TryObserve("local-ui-player-2").Success);
            Assert.AreEqual("local-ui-player-2", state.ObservedPlayerId);
            Assert.AreEqual(observedPlayerState, state.Snapshot.Players.Single(player => player.PlayerId == "local-ui-player-2").PlayerState.CanonicalSummary);
            Assert.IsTrue(state.TryObserveLocalPlayer().Success);
            Assert.AreEqual(state.LocalPlayerId, state.ObservedPlayerId);
        }

        [Test]
        public void Upgrade_RejectsTheConfirmedMaximumLevelWithoutSpendingGold()
        {
            var catalog = UnitCatalogLoader.LoadFromResources(CatalogPath).Catalog;
            var source = Resources.Load<TextAsset>(MatchPath).text
                .Replace("\"initialLevel\": 1", "\"initialLevel\": 9")
                .Replace("\"initialGold\": 7", "\"initialGold\": 100");
            var loaded = LocalMatchStateLoader.LoadFromJson(catalog, source);
            Assert.IsTrue(loaded.Success, Errors(loaded.Errors));
            var before = loaded.State.Snapshot.CanonicalSummary;

            var result = loaded.State.TryUpgrade();

            Assert.AreEqual(LocalMatchOperationCode.MaximumLevelReached, result.Code);
            Assert.AreEqual(before, loaded.State.Snapshot.CanonicalSummary);
        }

        private static LocalMatchState Load()
        {
            var result = LocalMatchStateLoader.LoadFromResources(CatalogPath, MatchPath);
            Assert.IsTrue(result.Success, Errors(result.Errors));
            return result.State;
        }

        private static string Errors(System.Collections.Generic.IReadOnlyList<LocalMatchValidationError> errors) => string.Join("; ", errors.Select(error => error.ToString()));
    }
}
