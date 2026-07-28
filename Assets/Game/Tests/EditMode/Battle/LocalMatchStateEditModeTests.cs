using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
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
        public void FixedMatch_LoadsFourIndependentPlayersAndTheInitialSixSlotShopDeterministically()
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

            Assert.IsTrue(first.State.Snapshot.Players.All(player => player.ShopSlots.Count == LocalMatchState.ShopSlotCount));
            Assert.IsTrue(first.State.Snapshot.Players.All(player =>
                player.ShopSlots.Select(slot => slot.UnitTypeId).SequenceEqual(
                    new[] { "1000", "1000", "1000", "1000", "1000", "1000" })));
            Assert.AreEqual(1, first.State.Snapshot.LocalPlayer.Level);
            Assert.AreEqual(7, first.State.Snapshot.LocalPlayer.Gold);
            Assert.AreEqual(400, first.State.Snapshot.LocalPlayer.Life);
            Assert.IsFalse(first.State.Snapshot.LocalPlayer.IsReady);
            Assert.AreEqual(first.State.Snapshot.CanonicalSummary, second.State.Snapshot.CanonicalSummary);
        }

        [Test]
        public void InitialShop_KeepsConfiguredGenerationOrderForEveryPlayer()
        {
            var catalog = UnitCatalogLoader.LoadFromResources(CatalogPath).Catalog;
            var source = Resources.Load<TextAsset>(MatchPath).text.Replace(
                "\"typeIds\": [\"1000\", \"1000\", \"1000\", \"1000\", \"1000\", \"1000\"]",
                "\"typeIds\": [\"5503\", \"1000\", \"5503\", \"1000\", \"5503\", \"1000\"]");
            var loaded = LocalMatchStateLoader.LoadFromJson(catalog, source);

            Assert.IsTrue(loaded.Success, Errors(loaded.Errors));
            foreach (var player in loaded.State.Snapshot.Players)
                CollectionAssert.AreEqual(
                    new[] { "5503", "1000", "5503", "1000", "5503", "1000" },
                    player.ShopSlots.Select(slot => slot.UnitTypeId));
        }

        [Test]
        public void OfferOrdering_SortsByRarityThenNumericTypeIdAndKeepsGenerationOrderForEqualKeys()
        {
            var orderingType = typeof(LocalMatchState).Assembly.GetType("ArknoNights.Player.ShopOfferOrdering");
            Assert.NotNull(orderingType, "The domain ordering helper is missing.");
            var sort = orderingType.GetMethod("Sort", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(sort);
            var rarities = new Dictionary<string, int>
            {
                ["9"] = 2,
                ["09"] = 2,
                ["10"] = 2,
                ["11"] = 2,
                ["100"] = 1
            };

            var actual = (string[])sort.Invoke(
                null,
                new object[]
                {
                    new[] { "10", "09", "100", "9", "11" },
                    new Func<string, int>(typeId => rarities[typeId])
                });

            CollectionAssert.AreEqual(new[] { "100", "09", "9", "10", "11" }, actual);
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
            Assert.AreEqual("local-ui-player-shop-0002", result.PurchasedUnitId);
            var added = result.Snapshot.LocalPlayer.PlayerState.Units.Single(unit => unit.UnitId == result.PurchasedUnitId);
            Assert.AreEqual("1000", added.TypeId);
            Assert.AreEqual(PlayerUnitZone.Staging, added.Zone);
            CollectionAssert.AllItemsAreUnique(result.Snapshot.Players.SelectMany(player => player.PlayerState.Units).Select(unit => unit.UnitId));
        }

        [Test]
        public void Purchase_SkipsLoadedMatchWideShopIdCollisionDeterministically()
        {
            var state = Load();
            Assert.IsTrue(state.Snapshot.Players.SelectMany(player => player.PlayerState.Units).Any(unit => unit.UnitId == "local-ui-player-shop-0001"));

            var first = state.TryPurchase(0);
            var second = state.TryPurchase(1);

            Assert.IsTrue(first.Success);
            Assert.IsTrue(second.Success);
            Assert.AreEqual("local-ui-player-shop-0002", first.PurchasedUnitId);
            Assert.AreEqual("local-ui-player-shop-0003", second.PurchasedUnitId);
            CollectionAssert.AllItemsAreUnique(state.Snapshot.Players.SelectMany(player => player.PlayerState.Units).Select(unit => unit.UnitId));
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
            var passiveRefresh = state.RefreshAllShopsAfterBattle();
            Assert.IsTrue(passiveRefresh.Success);
            CollectionAssert.AreEqual(
                new[] { "1000", "1000", "1000", "1000", "1000", "1000" },
                passiveRefresh.Snapshot.LocalPlayer.ShopSlots.Select(slot => slot.UnitTypeId));
        }

        [Test]
        public void ActiveRefresh_ReplacesFrozenLocalOffersClearsFreezeAndSortsWithoutChangingRemoteShops()
        {
            var state = Load();
            var remoteBefore = state.Snapshot.Players
                .Where(player => player.PlayerId != state.LocalPlayerId)
                .ToDictionary(
                    player => player.PlayerId,
                    player => player.ShopSlots.Select(slot => slot.UnitTypeId).ToArray());
            Assert.IsTrue(state.TryToggleFrozen(0).Success);

            var first = state.TryRefresh();

            Assert.IsTrue(first.Success);
            Assert.AreEqual(6, first.Snapshot.LocalPlayer.Gold);
            Assert.IsTrue(first.Snapshot.LocalPlayer.ShopSlots.All(slot => !slot.IsFrozen));
            CollectionAssert.AreEqual(
                new[] { "5503", "5503", "5503", "5503", "5503", "5503" },
                first.Snapshot.LocalPlayer.ShopSlots.Select(slot => slot.UnitTypeId));
            foreach (var remote in first.Snapshot.Players.Where(player => player.PlayerId != state.LocalPlayerId))
                CollectionAssert.AreEqual(remoteBefore[remote.PlayerId], remote.ShopSlots.Select(slot => slot.UnitTypeId));

            var second = state.TryRefresh();

            Assert.IsTrue(second.Success);
            CollectionAssert.AreEqual(
                new[] { "1000", "1000", "1000", "5503", "5503", "5503" },
                second.Snapshot.LocalPlayer.ShopSlots.Select(slot => slot.UnitTypeId));
        }

        [Test]
        public void PassiveRefresh_RefreshesAllPlayersForFreeWhileFrozenSlotsStayAndOnlyOpenSlotsSort()
        {
            var state = Load();
            Assert.IsTrue(state.TryRefresh().Success);
            Assert.IsTrue(state.TryPurchase(4).Success);
            Assert.IsTrue(state.TryToggleFrozen(0).Success);
            Assert.IsTrue(state.TryToggleFrozen(5).Success);
            var before = state.Snapshot;
            var changes = 0;
            state.Changed += _ => changes++;

            var result = state.RefreshAllShopsAfterBattle();

            Assert.IsTrue(result.Success);
            Assert.AreEqual(before.LocalPlayer.Gold, result.Snapshot.LocalPlayer.Gold);
            Assert.AreEqual(before.Version + 1, result.Snapshot.Version);
            Assert.AreEqual(1, changes);
            CollectionAssert.AreEqual(
                new[] { "5503", "1000", "1000", "5503", "5503", "5503" },
                result.Snapshot.LocalPlayer.ShopSlots.Select(slot => slot.UnitTypeId));
            Assert.IsTrue(result.Snapshot.LocalPlayer.ShopSlots[0].IsFrozen);
            Assert.IsTrue(result.Snapshot.LocalPlayer.ShopSlots[5].IsFrozen);
            foreach (var remote in result.Snapshot.Players.Where(player => player.PlayerId != state.LocalPlayerId))
            {
                CollectionAssert.AreEqual(
                    new[] { "1000", "1000", "1000", "5503", "5503", "5503" },
                    remote.ShopSlots.Select(slot => slot.UnitTypeId));
                Assert.IsTrue(remote.ShopSlots.All(slot => !slot.IsFrozen));
            }
        }

        [Test]
        public void BulkFreeze_MixedOccupiedSlotsChangeAtomicallyAndEmptySlotsStayUnfrozen()
        {
            var state = Load();
            Assert.IsTrue(state.TryToggleFrozen(0).Success);
            Assert.IsTrue(state.TryPurchase(5).Success);
            var changes = 0;
            state.Changed += _ => changes++;
            var result = state.TrySetOccupiedShopSlotsFrozen(true);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, changes);
            Assert.IsTrue(result.Snapshot.LocalPlayer.ShopSlots.Take(5).All(slot => slot.IsFrozen));
            Assert.IsTrue(result.Snapshot.LocalPlayer.ShopSlots[5].IsEmpty);
            Assert.IsFalse(result.Snapshot.LocalPlayer.ShopSlots[5].IsFrozen);
        }

        [Test]
        public void BulkFreeze_AllOccupiedFrozenSlotsUnfreezeWithOneNotification()
        {
            var state = Load();
            foreach (var slot in state.Snapshot.LocalPlayer.ShopSlots)
                Assert.IsTrue(state.TryToggleFrozen(slot.ShopSlotId).Success);
            var changes = 0;
            state.Changed += _ => changes++;
            var result = state.TrySetOccupiedShopSlotsFrozen(false);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, changes);
            Assert.IsTrue(result.Snapshot.LocalPlayer.ShopSlots.All(slot => !slot.IsFrozen));
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

        [Test]
        public void Purchase_WhenThirteenNonStackingStagingSlotsAlreadyExist_AddsTheNewUnitToOverflow()
        {
            var state = LoadPlayerState(13, PlayerUnitZone.Staging);
            var before = state.Snapshot;
            var notifications = 0;
            state.Changed += _ => notifications++;

            var result = state.TryAddPurchasedUnit("overflow-purchase", "1000");

            Assert.IsTrue(result.Success);
            Assert.AreEqual(before.Version + 1, result.Snapshot.Version);
            Assert.AreEqual(1, notifications);
            Assert.AreEqual(PlayerUnitZone.Overflow, result.Snapshot.Units.Single(unit => unit.UnitId == "overflow-purchase").Zone);
            Assert.AreEqual(13, result.Snapshot.StagingSlots.Count);
        }

        [Test]
        public void Purchase_WhenPlayerHasFortyEightUnits_RejectsWithoutChangingPlayerVersionOrEventCount()
        {
            var state = LoadPlayerState(48, PlayerUnitZone.Overflow);
            var before = state.Snapshot;
            var notifications = 0;
            state.Changed += _ => notifications++;

            var result = state.TryAddPurchasedUnit("capacity-purchase", "1000");

            Assert.AreEqual(PlayerOperationCode.UnitCapacityExceeded, result.Code);
            Assert.AreEqual(before.Version, result.Snapshot.Version);
            Assert.AreEqual(before.CanonicalSummary, result.Snapshot.CanonicalSummary);
            Assert.AreEqual(0, notifications);
        }

        [Test]
        public void Purchase_WhenPlayerUnitIdConflicts_RejectsWithoutChangingPlayerVersionOrEventCount()
        {
            var state = LocalPlayerStateLoader.LoadFromResources(CatalogPath, "PlayerData/local-player-state-v1").State;
            var before = state.Snapshot;
            var notifications = 0;
            state.Changed += _ => notifications++;

            var result = state.TryAddPurchasedUnit("local-1000-alpha", "1000");

            Assert.AreEqual(PlayerOperationCode.UnitIdDuplicate, result.Code);
            Assert.AreEqual(before.Version, result.Snapshot.Version);
            Assert.AreEqual(before.CanonicalSummary, result.Snapshot.CanonicalSummary);
            Assert.AreEqual(0, notifications);
        }

        [Test]
        public void Purchase_WhenGoldIsInsufficient_LeavesMatchAndPlayerVersionsAndEventCountsUnchanged()
        {
            var state = Load();
            Assert.IsTrue(state.TryPurchase(0).Success);
            Assert.IsTrue(state.TryPurchase(1).Success);
            Assert.IsTrue(state.TryPurchase(2).Success);
            Assert.IsTrue(state.TryPurchase(3).Success);
            Assert.IsTrue(state.TryPurchase(4).Success);
            Assert.IsTrue(state.TryRefresh().Success);
            Assert.IsTrue(state.TryRefresh().Success);
            var before = state.Snapshot;
            var matchNotifications = 0;
            state.Changed += _ => matchNotifications++;

            var result = state.TryPurchase(0);

            Assert.AreEqual(LocalMatchOperationCode.InsufficientGold, result.Code);
            Assert.AreEqual(before.Version, result.Snapshot.Version);
            Assert.AreEqual(before.CanonicalSummary, result.Snapshot.CanonicalSummary);
            Assert.AreEqual(before.LocalPlayer.PlayerState.Version, result.Snapshot.LocalPlayer.PlayerState.Version);
            Assert.AreEqual(0, matchNotifications);
        }

        private static LocalMatchState Load()
        {
            var result = LocalMatchStateLoader.LoadFromResources(CatalogPath, MatchPath);
            Assert.IsTrue(result.Success, Errors(result.Errors));
            return result.State;
        }

        private static PlayerState LoadPlayerState(int unitCount, PlayerUnitZone zone)
        {
            var catalog = UnitCatalogLoader.LoadFromResources(CatalogPath).Catalog;
            var units = new StringBuilder();
            for (var index = 0; index < unitCount; index++)
            {
                if (index > 0) units.Append(',');
                units.Append("{\"unitId\":\"purchase-fixture-").Append(index)
                    .Append("\",\"typeId\":\"1000\",\"zone\":\"").Append(zone)
                    .Append("\",\"eliteLevel\":0,\"buffs\":[{\"id\":\"nonstacking-").Append(index)
                    .Append("\",\"rawPayload\":\"\"}],\"formationX\":0,\"formationY\":0}");
            }

            var json = "{\"schemaVersion\":\"local-player-state-v1\",\"playerId\":\"purchase-fixture\",\"deploymentCost\":99,\"units\":[" + units + "]}";
            var loaded = LocalPlayerStateLoader.LoadFromJson(catalog, json);
            Assert.IsTrue(loaded.Success, string.Join("; ", loaded.Errors.Select(error => error.ToString())));
            return loaded.State;
        }

        private static string Errors(System.Collections.Generic.IReadOnlyList<LocalMatchValidationError> errors) => string.Join("; ", errors.Select(error => error.ToString()));
    }
}
