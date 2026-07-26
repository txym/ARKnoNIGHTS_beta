using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ArknoNights.Player;

namespace ArknoNights.UI.PlayerListObserver
{
    /// <summary>Commands that change a local player's preparation formation.</summary>
    public enum PlayerListObserverFormationCommand
    {
        Deploy,
        Move,
        Swap,
        Retreat
    }

    /// <summary>The reason a formation command may or may not be submitted by an observing UI.</summary>
    public enum PlayerListObserverCommandPermission
    {
        Allowed,
        ReadOnlyObservedPlayer
    }

    /// <summary>
    /// Read-only observation coordinator for the local four-player fixture.
    /// It intentionally separates the player being rendered from the player that owns commands:
    /// LocalCommandPlayerState always remains the match local player, while DisplayedPlayerState
    /// follows the observer selection. It never creates, copies, or mutates PlayerState instances.
    /// </summary>
    public sealed class PlayerListObserverCoordinator : IDisposable
    {
        private readonly LocalMatchState matchState;
        private LocalMatchSnapshot snapshot;
        private bool isUnitSelected;

        public PlayerListObserverCoordinator(LocalMatchState matchState)
        {
            this.matchState = matchState ?? throw new ArgumentNullException(nameof(matchState));
            snapshot = matchState.Snapshot;
            matchState.Changed += HandleMatchChanged;
        }

        /// <summary>Raised after a match snapshot or list visibility changes. It carries no mutable state.</summary>
        public event Action Changed;

        public LocalMatchSnapshot Snapshot => snapshot;
        /// <summary>Always the local command owner, including while a remote player is displayed.</summary>
        public LocalMatchPlayerSnapshot LocalCommandPlayerState => snapshot.LocalPlayer;
        /// <summary>The current display target selected through LocalMatchState observation.</summary>
        public LocalMatchPlayerSnapshot DisplayedPlayerState => snapshot.Players.Single(player => player.PlayerId == snapshot.ObservedPlayerId);
        /// <summary>Shop, level, gold, and ready are always read from the local command owner.</summary>
        public LocalMatchPlayerSnapshot LocalEconomyState => LocalCommandPlayerState;
        public bool IsObservingAnotherPlayer => !string.Equals(snapshot.LocalPlayerId, snapshot.ObservedPlayerId, StringComparison.Ordinal);
        public bool IsPlayerListVisible => !isUnitSelected;
        public bool CanSubmitFormationCommand => GetFormationCommandPermission() == PlayerListObserverCommandPermission.Allowed;

        /// <summary>
        /// Returns a command gate for the HUD scene coordinator. The caller must use LocalCommandPlayerState as the
        /// command source when this returns Allowed; a non-local display target is intentionally read-only.
        /// </summary>
        public PlayerListObserverCommandPermission GetFormationCommandPermission()
        {
            return IsObservingAnotherPlayer
                ? PlayerListObserverCommandPermission.ReadOnlyObservedPlayer
                : PlayerListObserverCommandPermission.Allowed;
        }

        /// <summary>Overload keeps the requested operation explicit at the future input boundary.</summary>
        public PlayerListObserverCommandPermission GetFormationCommandPermission(PlayerListObserverFormationCommand command)
        {
            return GetFormationCommandPermission();
        }

        /// <summary>Selects only the player displayed by the presentation layer.</summary>
        public LocalMatchOperationResult TrySelectDisplayedPlayer(string playerId)
        {
            return matchState.TryObserve(playerId);
        }

        /// <summary>Restores the display target to the player that already owns all commands.</summary>
        public LocalMatchOperationResult TryReturnToLocalPlayer()
        {
            return matchState.TryObserveLocalPlayer();
        }

        /// <summary>
        /// Unit selection is a visibility concern only. It must not clear observation or write a formation.
        /// The HUD scene coordinator forwards its unified unit-selection lifecycle to this method.
        /// </summary>
        public void SetUnitSelected(bool selected)
        {
            if (isUnitSelected == selected) return;
            isUnitSelected = selected;
            Changed?.Invoke();
        }

        public void Dispose()
        {
            matchState.Changed -= HandleMatchChanged;
            Changed = null;
        }

        private void HandleMatchChanged(LocalMatchSnapshot next)
        {
            snapshot = next ?? matchState.Snapshot;
            Changed?.Invoke();
        }
    }

    /// <summary>Independent presentation data for exactly one player-list row.</summary>
    public sealed class PlayerListEntryPresentation
    {
        private const string ExitedAvatarResourcePath = "UI/Texture/player_list/equip_replace_avatart_bg";

        internal PlayerListEntryPresentation(LocalMatchPlayerSnapshot player, bool isLocalPlayer, bool isObservedPlayer)
        {
            PlayerId = player.PlayerId;
            DisplayName = player.DisplayName;
            AvatarResourcePath = player.HasExited ? ExitedAvatarResourcePath : player.AvatarResourcePath;
            Life = player.Life;
            IsConnected = player.IsConnected;
            HasExited = player.HasExited;
            IsLocalPlayer = isLocalPlayer;
            IsObservedPlayer = isObservedPlayer;
        }

        public string PlayerId { get; }
        public string DisplayName { get; }
        public string AvatarResourcePath { get; }
        public int Life { get; }
        public bool IsConnected { get; }
        public bool HasExited { get; }
        public bool ShowsLostConnection => !IsConnected;
        public bool IsLocalPlayer { get; }
        public bool IsObservedPlayer { get; }
    }

    /// <summary>Builds read-only row data without loading sprites or assigning interaction permissions.</summary>
    public static class PlayerListPresentation
    {
        public static IReadOnlyList<PlayerListEntryPresentation> Build(PlayerListObserverCoordinator coordinator)
        {
            if (coordinator == null) throw new ArgumentNullException(nameof(coordinator));
            var snapshot = coordinator.Snapshot;
            return new ReadOnlyCollection<PlayerListEntryPresentation>(snapshot.Players
                .Select(player => new PlayerListEntryPresentation(
                    player,
                    string.Equals(player.PlayerId, snapshot.LocalPlayerId, StringComparison.Ordinal),
                    string.Equals(player.PlayerId, snapshot.ObservedPlayerId, StringComparison.Ordinal)))
                .ToArray());
        }
    }

    /// <summary>Pure left-side player-list geometry, independent of RectTransform and scene ownership.</summary>
    public static class PlayerListLayout
    {
        private const float ReferenceHeight = 1080f;
        private const float ReferenceLeftPadding = 8f;
        private const float ReferenceTopPadding = 164f;
        private const float ReferenceBackgroundWidth = 116f;
        private const float ReferenceBackgroundHeight = 534f;
        private const float ReferencePlayerScale = .85f;
        private const float ReferenceRowWidth = 116f * ReferencePlayerScale;
        private const float ReferenceRowHeight = 126f * ReferencePlayerScale;
        private const float ReferenceRowSpacing = 13f;

        public static PlayerListLayoutSnapshot Calculate(float viewportWidth, float viewportHeight, int rowCount, bool isVisible)
        {
            var scale = viewportHeight > 0f ? viewportHeight / ReferenceHeight : 1f;
            var leftPadding = ReferenceLeftPadding * scale;
            var rowWidth = ReferenceRowWidth * scale;
            var rowHeight = ReferenceRowHeight * scale;
            var rowSpacing = ReferenceRowSpacing * scale;
            var rows = new List<PlayerListRowLayout>(Math.Max(0, rowCount));
            var backgroundWidth = ReferenceBackgroundWidth * scale;
            var backgroundHeight = ReferenceBackgroundHeight * scale;
            var backgroundTop = ReferenceTopPadding * scale;
            var rowGroupHeight = rowCount <= 0 ? 0f : rowHeight * rowCount + rowSpacing * (rowCount - 1);
            var rowLeft = leftPadding + (backgroundWidth - rowWidth) * .5f - 1f * scale;
            var rowTop = backgroundTop + (backgroundHeight - rowGroupHeight) * .5f;
            for (var index = 0; index < rowCount; index++)
            {
                var yMin = rowTop + index * (rowHeight + rowSpacing);
                rows.Add(new PlayerListRowLayout(index, rowLeft, yMin, rowWidth, rowHeight));
            }

            var background = new PlayerListRectLayout(leftPadding, backgroundTop, backgroundWidth, backgroundHeight);
            return new PlayerListLayoutSnapshot(isVisible, leftPadding, background, new ReadOnlyCollection<PlayerListRowLayout>(rows));
        }
    }

    public sealed class PlayerListLayoutSnapshot
    {
        internal PlayerListLayoutSnapshot(bool isVisible, float leftPadding, PlayerListRectLayout background, IReadOnlyList<PlayerListRowLayout> rows)
        {
            IsVisible = isVisible;
            LeftPadding = leftPadding;
            Background = background;
            Rows = rows;
        }

        public bool IsVisible { get; }
        public float LeftPadding { get; }
        public PlayerListRectLayout Background { get; }
        public IReadOnlyList<PlayerListRowLayout> Rows { get; }
    }

    public struct PlayerListRectLayout
    {
        internal PlayerListRectLayout(float x, float yMin, float width, float height)
        {
            X = x;
            YMin = yMin;
            Width = width;
            Height = height;
        }

        public float X { get; }
        public float YMin { get; }
        public float Width { get; }
        public float Height { get; }
        public float YMax => YMin + Height;
    }

    public struct PlayerListRowLayout
    {
        internal PlayerListRowLayout(int index, float x, float yMin, float width, float height)
        {
            Index = index;
            X = x;
            YMin = yMin;
            Width = width;
            Height = height;
        }

        public int Index { get; }
        public float X { get; }
        public float YMin { get; }
        public float Width { get; }
        public float Height { get; }
        public float YMax => YMin + Height;
    }
}
