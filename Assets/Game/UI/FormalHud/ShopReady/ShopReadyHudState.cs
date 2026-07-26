using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ArknoNights.Player;

namespace ArknoNights.UI.FormalHud.ShopReady
{
    public enum ShopReadyConfirmation
    {
        None,
        Purchase,
        Upgrade
    }


    /// <summary>UI-local, non-authoritative confirmation state. It never stores shop or player data.</summary>
    public sealed class ShopReadyPendingCommand
    {
        public ShopReadyConfirmation Kind { get; private set; }
        public int ShopSlotId { get; private set; } = -1;

        public bool RequestPurchase(int shopSlotId)
        {
            if (Kind == ShopReadyConfirmation.Purchase && ShopSlotId == shopSlotId)
            {
                Clear();
                return true;
            }

            Kind = ShopReadyConfirmation.Purchase;
            ShopSlotId = shopSlotId;
            return false;
        }

        public bool RequestFixed(ShopReadyConfirmation requested)
        {
            if (requested != ShopReadyConfirmation.Upgrade)
                throw new ArgumentOutOfRangeException(nameof(requested));
            if (Kind == requested)
            {
                Clear();
                return true;
            }

            Kind = requested;
            ShopSlotId = -1;
            return false;
        }

        public void Clear()
        {
            Kind = ShopReadyConfirmation.None;
            ShopSlotId = -1;
        }
    }

    public struct ShopReadyHudRect : IEquatable<ShopReadyHudRect>
    {
        public ShopReadyHudRect(float left, float bottom, float width, float height)
        {
            Left = left;
            Bottom = bottom;
            Width = width;
            Height = height;
        }

        public float Left { get; }
        public float Bottom { get; }
        public float Width { get; }
        public float Height { get; }

        public bool Equals(ShopReadyHudRect other)
        {
            return Left.Equals(other.Left) && Bottom.Equals(other.Bottom) && Width.Equals(other.Width) && Height.Equals(other.Height);
        }

        public override bool Equals(object obj) => obj is ShopReadyHudRect && Equals((ShopReadyHudRect)obj);
        public override int GetHashCode() => Left.GetHashCode() ^ Bottom.GetHashCode() ^ Width.GetHashCode() ^ Height.GetHashCode();
        public static bool operator ==(ShopReadyHudRect left, ShopReadyHudRect right) => left.Equals(right);
        public static bool operator !=(ShopReadyHudRect left, ShopReadyHudRect right) => !left.Equals(right);
    }

    public sealed class ShopReadyHudLayoutResult
    {
        internal ShopReadyHudLayoutResult(float scale, ShopReadyHudRect levelPanel, ShopReadyHudRect shopPanel, ShopReadyHudRect shopToggle, ShopReadyHudRect readyButton)
        {
            Scale = scale;
            LevelPanel = levelPanel;
            ShopPanel = shopPanel;
            ShopToggle = shopToggle;
            ReadyButton = readyButton;
        }

        public float Scale { get; }
        public ShopReadyHudRect LevelPanel { get; }
        public ShopReadyHudRect ShopPanel { get; }
        public ShopReadyHudRect ShopToggle { get; }
        public ShopReadyHudRect ReadyButton { get; }
    }

    /// <summary>Right-side HUD geometry. Its reference positions are visual defaults pending scene-level acceptance.</summary>
    public static class ShopReadyHudLayout
    {
        public static readonly ShopReadyHudRect ReferenceReadyButton = new ShopReadyHudRect(1740f, 400f, 180f, 60f);
        public static readonly ShopReadyHudRect ReferenceLevelPanel = new ShopReadyHudRect(1735f, 930f, 130f, 120f);
        public static readonly ShopReadyHudRect ReferenceShopToggle = ReferenceLevelPanel;
        public static readonly ShopReadyHudRect ReferenceShopPanel = new ShopReadyHudRect(250f, 550f, 1605f, 420f);

        public static ShopReadyHudLayoutResult Calculate(float screenWidth, float screenHeight)
        {
            var scale = screenHeight <= 0f ? 1f : screenHeight / 1080f;
            return new ShopReadyHudLayoutResult(
                scale,
                ScaleRightAnchored(ReferenceLevelPanel, screenWidth, scale),
                ScaleRightAnchored(ReferenceShopPanel, screenWidth, scale),
                ScaleRightAnchored(ReferenceShopToggle, screenWidth, scale),
                ScaleRightAnchored(ReferenceReadyButton, screenWidth, scale));
        }

        private static ShopReadyHudRect ScaleRightAnchored(ShopReadyHudRect reference, float screenWidth, float scale)
        {
            var rightGap = 1920f - reference.Left - reference.Width;
            return new ShopReadyHudRect(screenWidth - (rightGap + reference.Width) * scale, reference.Bottom * scale, reference.Width * scale, reference.Height * scale);
        }
    }

    public sealed class ShopReadySlotViewState
    {
        internal ShopReadySlotViewState(LocalMatchShopSlotSnapshot source, int gold)
        {
            ShopSlotId = source.ShopSlotId;
            UnitTypeId = source.UnitTypeId;
            Price = source.Price;
            Rarity = source.Rarity;
            DisplayName = source.DisplayName;
            PortraitResourcePath = source.PortraitResourcePath;
            IsEmpty = source.IsEmpty;
            IsFrozen = source.IsFrozen;
            CanPurchase = !IsEmpty && gold >= Price;
            CanToggleFrozen = !IsEmpty;
        }

        public int ShopSlotId { get; }
        public string UnitTypeId { get; }
        public int Price { get; }
        public int Rarity { get; }
        public string DisplayName { get; }
        public string PortraitResourcePath { get; }
        public bool IsEmpty { get; }
        public bool IsFrozen { get; }
        public bool CanPurchase { get; }
        public bool CanToggleFrozen { get; }
    }

    /// <summary>Read-only HUD projection. Economy, shop contents, readiness and commands stay owned by LocalMatchState.</summary>
    public sealed class ShopReadyHudState
    {
        private ShopReadyHudState(LocalMatchPlayerSnapshot player, bool shopVisible, ShopReadyConfirmation confirmation, IEnumerable<ShopReadySlotViewState> slots)
        {
            Level = player.Level;
            Gold = player.Gold;
            IsReady = player.IsReady;
            ShopVisible = shopVisible;
            PendingConfirmation = confirmation;
            FormationInteractionEnabled = !player.IsReady;
            ShopCommandsEnabled = true;
            Slots = new ReadOnlyCollection<ShopReadySlotViewState>((slots ?? Enumerable.Empty<ShopReadySlotViewState>()).OrderBy(slot => slot.ShopSlotId).ToArray());
        }

        public int Level { get; }
        public int Gold { get; }
        public bool IsReady { get; }
        public bool ShopVisible { get; }
        public ShopReadyConfirmation PendingConfirmation { get; }
        public bool FormationInteractionEnabled { get; }
        public bool ShopCommandsEnabled { get; }
        public IReadOnlyList<ShopReadySlotViewState> Slots { get; }

        public static ShopReadyHudState Project(LocalMatchSnapshot snapshot, bool shopVisible, ShopReadyConfirmation confirmation)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var player = snapshot.LocalPlayer;
            if (player == null) throw new ArgumentException("Local player snapshot is required.", nameof(snapshot));
            return new ShopReadyHudState(player, shopVisible, confirmation, player.ShopSlots.Select(slot => new ShopReadySlotViewState(slot, player.Gold)));
        }

        public static ShopReadyConfirmation RequestConfirmation(ShopReadyConfirmation current, ShopReadyConfirmation requested)
        {
            if (requested == ShopReadyConfirmation.None) return ShopReadyConfirmation.None;
            return current == requested ? ShopReadyConfirmation.None : requested;
        }
    }
}
