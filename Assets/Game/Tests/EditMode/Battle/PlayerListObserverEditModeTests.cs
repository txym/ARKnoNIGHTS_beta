using System.Linq;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Player;
using ArknoNights.UI.PlayerListObserver;
using NUnit.Framework;
using UnityEngine;

namespace ArknoNights.Battle.Tests
{
    public sealed class PlayerListObserverEditModeTests
    {
        private const string CatalogPath = "BattleData/unit-catalog-v1";
        private const string MatchPath = "PlayerData/local-match-state-v1";

        [Test]
        public void SelectingAnotherPlayer_ChangesOnlyDisplayedPlayerAndLeavesBothFormationSnapshotsUntouched()
        {
            var state = Load();
            var coordinator = new PlayerListObserverCoordinator(state);
            var localBefore = state.Snapshot.LocalPlayer.PlayerState.CanonicalSummary;
            var observedBefore = state.Snapshot.Players.Single(player => player.PlayerId == "local-ui-player-2").PlayerState.CanonicalSummary;

            var result = coordinator.TrySelectDisplayedPlayer("local-ui-player-2");

            Assert.IsTrue(result.Success);
            Assert.AreEqual("local-ui-player", coordinator.LocalCommandPlayerState.PlayerId);
            Assert.AreEqual("local-ui-player-2", coordinator.DisplayedPlayerState.PlayerId);
            Assert.IsTrue(coordinator.IsObservingAnotherPlayer);
            Assert.AreEqual(localBefore, coordinator.LocalCommandPlayerState.PlayerState.CanonicalSummary);
            Assert.AreEqual(observedBefore, coordinator.DisplayedPlayerState.PlayerState.CanonicalSummary);
        }

        [Test]
        public void ObservingAnotherPlayer_RejectsFormationCommandsWhileKeepingLocalEconomyAndReadyStateVisible()
        {
            var state = Load();
            Assert.IsTrue(state.TryUpgrade().Success);
            Assert.IsTrue(state.TryToggleReady().Success);
            var coordinator = new PlayerListObserverCoordinator(state);

            Assert.IsTrue(coordinator.TrySelectDisplayedPlayer("local-ui-player-3").Success);

            Assert.IsFalse(coordinator.CanSubmitFormationCommand);
            Assert.AreEqual(PlayerListObserverCommandPermission.ReadOnlyObservedPlayer, coordinator.GetFormationCommandPermission());
            Assert.AreEqual("local-ui-player", coordinator.LocalEconomyState.PlayerId);
            Assert.AreEqual(2, coordinator.LocalEconomyState.Level);
            Assert.AreEqual(3, coordinator.LocalEconomyState.Gold);
            Assert.IsTrue(coordinator.LocalEconomyState.IsReady);
            Assert.AreEqual(5, coordinator.LocalEconomyState.ShopSlots.Count);
        }

        [Test]
        public void ReturningToLocalPlayer_RestoresDisplayedPlayerWithoutChangingTheCommandTarget()
        {
            var coordinator = new PlayerListObserverCoordinator(Load());
            Assert.IsTrue(coordinator.TrySelectDisplayedPlayer("local-ui-player-2").Success);

            var result = coordinator.TryReturnToLocalPlayer();

            Assert.IsTrue(result.Success);
            Assert.AreEqual("local-ui-player", coordinator.LocalCommandPlayerState.PlayerId);
            Assert.AreEqual("local-ui-player", coordinator.DisplayedPlayerState.PlayerId);
            Assert.IsFalse(coordinator.IsObservingAnotherPlayer);
            Assert.IsTrue(coordinator.CanSubmitFormationCommand);
        }

        [Test]
        public void SelectingAUnit_HidesThePlayerListWithoutClearingTheObservedPlayer()
        {
            var coordinator = new PlayerListObserverCoordinator(Load());
            Assert.IsTrue(coordinator.TrySelectDisplayedPlayer("local-ui-player-2").Success);

            coordinator.SetUnitSelected(true);

            Assert.IsFalse(coordinator.IsPlayerListVisible);
            Assert.AreEqual("local-ui-player-2", coordinator.DisplayedPlayerState.PlayerId);
            coordinator.SetUnitSelected(false);
            Assert.IsTrue(coordinator.IsPlayerListVisible);
            Assert.AreEqual("local-ui-player-2", coordinator.DisplayedPlayerState.PlayerId);
        }

        [Test]
        public void FourPlayerPresentation_IndependentlyRepresentsAvatarLifeSelfObservedAndDisconnectedFlags()
        {
            var source = Resources.Load<TextAsset>(MatchPath).text.Replace(
                "\"playerId\": \"local-ui-player-4\", \"displayName\": \"Player 4\", \"avatarResourcePath\": \"UI/Texture/player_list/avatar_4\", \"life\": 400, \"isConnected\": true",
                "\"playerId\": \"local-ui-player-4\", \"displayName\": \"Player 4\", \"avatarResourcePath\": \"UI/Texture/player_list/avatar_4\", \"life\": 400, \"isConnected\": false");
            var catalog = UnitCatalogLoader.LoadFromResources(CatalogPath).Catalog;
            var load = LocalMatchStateLoader.LoadFromJson(catalog, source);
            Assert.IsTrue(load.Success, string.Join("; ", load.Errors.Select(error => error.ToString())));
            var coordinator = new PlayerListObserverCoordinator(load.State);
            Assert.IsTrue(coordinator.TrySelectDisplayedPlayer("local-ui-player-3").Success);

            var rows = PlayerListPresentation.Build(coordinator);

            Assert.AreEqual(4, rows.Count);
            Assert.AreEqual("UI/Texture/player_list/avatar_1", rows.Single(row => row.PlayerId == "local-ui-player").AvatarResourcePath);
            Assert.AreEqual(400, rows.Single(row => row.PlayerId == "local-ui-player").Life);
            Assert.IsTrue(rows.Single(row => row.PlayerId == "local-ui-player").IsLocalPlayer);
            Assert.IsTrue(rows.Single(row => row.PlayerId == "local-ui-player-3").IsObservedPlayer);
            Assert.IsFalse(rows.Single(row => row.PlayerId == "local-ui-player-4").IsConnected);
        }

        [Test]
        public void FourPlayerLayout_IsLeftAnchoredAndDoesNotOverlapRows()
        {
            var layout = PlayerListLayout.Calculate(1920f, 1080f, 4, true);

            Assert.IsTrue(layout.IsVisible);
            Assert.AreEqual(4, layout.Rows.Count);
            Assert.That(layout.Rows.Select(row => row.X), Is.All.EqualTo(layout.LeftPadding).Within(0.001f));
            Assert.That(layout.Rows.Zip(layout.Rows.Skip(1), (current, next) => current.YMax <= next.YMin), Is.All.True);
        }

        private static LocalMatchState Load()
        {
            var result = LocalMatchStateLoader.LoadFromResources(CatalogPath, MatchPath);
            Assert.IsTrue(result.Success, string.Join("; ", result.Errors.Select(error => error.ToString())));
            return result.State;
        }
    }
}
