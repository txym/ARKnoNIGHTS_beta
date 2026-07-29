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
            Assert.AreEqual(196, coordinator.LocalEconomyState.Gold);
            Assert.IsTrue(coordinator.LocalEconomyState.IsReady);
            Assert.AreEqual(6, coordinator.LocalEconomyState.ShopSlots.Count);
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
            var source = Resources.Load<TextAsset>(MatchPath).text;
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
            Assert.IsFalse(rows.Single(row => row.PlayerId == "local-ui-player-3").IsConnected);
            Assert.IsTrue(rows.Single(row => row.PlayerId == "local-ui-player-3").ShowsLostConnection);
            Assert.IsFalse(rows.Single(row => row.PlayerId == "local-ui-player-4").IsConnected);
            Assert.IsTrue(rows.Single(row => row.PlayerId == "local-ui-player-4").HasExited);
        }

        [Test]
        public void ExitedPlayerPresentation_ReplacesTheAvatarAndKeepsTheDisconnectedOverlay()
        {
            var source = Resources.Load<TextAsset>(MatchPath).text;
            var catalog = UnitCatalogLoader.LoadFromResources(CatalogPath).Catalog;
            var load = LocalMatchStateLoader.LoadFromJson(catalog, source);
            Assert.IsTrue(load.Success, string.Join("; ", load.Errors.Select(error => error.ToString())));
            var rows = PlayerListPresentation.Build(new PlayerListObserverCoordinator(load.State));
            var exited = rows.Single(row => row.PlayerId == "local-ui-player-4");

            Assert.IsTrue(exited.HasExited, "Exit must remain distinct from a temporary disconnect.");
            Assert.IsTrue(exited.ShowsLostConnection, "The disconnected overlay must remain visible over the exit replacement avatar.");
            Assert.AreEqual("UI/Texture/player_list/equip_replace_avatart_bg", exited.AvatarResourcePath);
            Assert.NotNull(
                ArknoNights.UI.FormalHud.ShopReady.FormalHudSpriteLoader.Load(exited.AvatarResourcePath),
                "The replacement avatar asset must be loadable by the production HUD loader.");
            Assert.IsFalse(exited.IsConnected);
        }

        [Test]
        public void FourPlayerLayout_IsLeftAnchoredAndDoesNotOverlapRows()
        {
            var layout = PlayerListLayout.Calculate(1920f, 1080f, 4, true);

            Assert.IsTrue(layout.IsVisible);
            Assert.AreEqual(4, layout.Rows.Count);
            Assert.That(layout.LeftPadding, Is.EqualTo(8f).Within(0.001f));
            Assert.That(layout.Rows[0].Width, Is.EqualTo(116f * .85f).Within(0.001f));
            Assert.That(layout.Rows[0].Height, Is.EqualTo(126f * .85f).Within(0.001f));
            Assert.That(layout.Rows.Select(row => row.X), Is.All.EqualTo(8f + (116f - 116f * .85f) * .5f - 1f).Within(0.001f));
            Assert.That(layout.Rows[1].YMin - layout.Rows[0].YMax, Is.EqualTo(13f).Within(0.001f));
            Assert.That(layout.Rows.Zip(layout.Rows.Skip(1), (current, next) => current.YMax <= next.YMin), Is.All.True);
            Assert.That(layout.Background.X, Is.EqualTo(layout.LeftPadding).Within(0.001f));
            Assert.That(layout.Background.YMin, Is.EqualTo(164f).Within(0.001f));
            Assert.That(layout.Background.Width, Is.EqualTo(116f).Within(0.001f));
            Assert.That(layout.Background.Height, Is.EqualTo(534f).Within(0.001f));
            Assert.That(
                layout.Rows[0].YMin - layout.Background.YMin,
                Is.EqualTo(layout.Background.YMax - layout.Rows[3].YMax).Within(0.001f));
        }

        private static LocalMatchState Load()
        {
            var result = LocalMatchStateLoader.LoadFromResources(CatalogPath, MatchPath);
            Assert.IsTrue(result.Success, string.Join("; ", result.Errors.Select(error => error.ToString())));
            return result.State;
        }
    }
}
