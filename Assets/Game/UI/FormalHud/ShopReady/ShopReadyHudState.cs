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
        public string ShopUnitId { get; private set; } = string.Empty;
        public bool Submitted { get; private set; }

        public bool RequestPurchase(int shopSlotId)
        {
            return RequestPurchase(shopSlotId, string.Empty);
        }

        public bool RequestPurchase(
            int shopSlotId,
            string shopUnitId)
        {
            shopUnitId = shopUnitId ?? string.Empty;
            if (Kind == ShopReadyConfirmation.Purchase
                && ShopSlotId == shopSlotId
                && string.Equals(
                    ShopUnitId,
                    shopUnitId,
                    StringComparison.Ordinal))
            {
                if (Submitted) return false;
                Submitted = true;
                return true;
            }

            Kind = ShopReadyConfirmation.Purchase;
            ShopSlotId = shopSlotId;
            ShopUnitId = shopUnitId;
            Submitted = false;
            return false;
        }

        public bool RequestFixed(ShopReadyConfirmation requested)
        {
            if (requested != ShopReadyConfirmation.Upgrade)
                throw new ArgumentOutOfRangeException(nameof(requested));
            if (Kind == requested)
            {
                if (Submitted) return false;
                Submitted = true;
                return true;
            }

            Kind = requested;
            ShopSlotId = -1;
            ShopUnitId = string.Empty;
            Submitted = false;
            return false;
        }

        public void Resolve(bool accepted)
        {
            if (accepted)
            {
                Clear();
                return;
            }
            Submitted = false;
        }

        public void Reconcile(
            IEnumerable<ShopReadySlotViewState> slots)
        {
            if (Kind != ShopReadyConfirmation.Purchase) return;
            var current = (slots
                    ?? Enumerable.Empty<ShopReadySlotViewState>())
                .FirstOrDefault(item =>
                    item.ShopSlotId == ShopSlotId);
            if (current == null
                || !string.Equals(
                    current.UnitId,
                    ShopUnitId,
                    StringComparison.Ordinal))
            {
                Clear();
            }
        }

        public void Clear()
        {
            Kind = ShopReadyConfirmation.None;
            ShopSlotId = -1;
            ShopUnitId = string.Empty;
            Submitted = false;
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
            : this(
                source.ShopSlotId,
                source.UnitTypeId,
                source.UnitTypeId,
                source.Price,
                source.DeploymentCost,
                source.Rarity,
                source.DisplayName,
                source.PortraitResourcePath,
                source.IsEmpty,
                source.IsFrozen,
                !source.IsEmpty && gold >= source.Price,
                !source.IsEmpty)
        {
        }

        private ShopReadySlotViewState(
            int shopSlotId,
            string unitTypeId,
            string unitId,
            int price,
            int deploymentCost,
            int rarity,
            string displayName,
            string portraitResourcePath,
            bool isEmpty,
            bool isFrozen,
            bool canPurchase,
            bool canToggleFrozen)
        {
            ShopSlotId = shopSlotId;
            UnitTypeId = unitTypeId ?? string.Empty;
            UnitId = unitId ?? string.Empty;
            Price = price;
            DeploymentCost = deploymentCost;
            Rarity = rarity;
            DisplayName = displayName ?? string.Empty;
            PortraitResourcePath = portraitResourcePath ?? string.Empty;
            IsEmpty = isEmpty;
            IsFrozen = isFrozen;
            CanPurchase = canPurchase;
            CanToggleFrozen = canToggleFrozen;
        }

        public int ShopSlotId { get; }
        public string UnitTypeId { get; }
        public string UnitId { get; }
        public int Price { get; }
        public int DeploymentCost { get; }
        public int Rarity { get; }
        public string DisplayName { get; }
        public string PortraitResourcePath { get; }
        public bool IsEmpty { get; }
        public bool IsFrozen { get; }
        public bool CanPurchase { get; }
        public bool CanToggleFrozen { get; }

        public static ShopReadySlotViewState CreateProjection(
            int shopSlotId,
            string unitTypeId,
            int price,
            int deploymentCost,
            int rarity,
            string displayName,
            string portraitResourcePath,
            bool isEmpty,
            bool isFrozen,
            bool canPurchase,
            bool canToggleFrozen)
        {
            return CreateProjection(
                shopSlotId,
                unitTypeId,
                unitTypeId,
                price,
                deploymentCost,
                rarity,
                displayName,
                portraitResourcePath,
                isEmpty,
                isFrozen,
                canPurchase,
                canToggleFrozen);
        }

        public static ShopReadySlotViewState CreateProjection(
            int shopSlotId,
            string unitTypeId,
            string unitId,
            int price,
            int deploymentCost,
            int rarity,
            string displayName,
            string portraitResourcePath,
            bool isEmpty,
            bool isFrozen,
            bool canPurchase,
            bool canToggleFrozen)
        {
            return new ShopReadySlotViewState(
                shopSlotId,
                unitTypeId,
                unitId,
                price,
                deploymentCost,
                rarity,
                displayName,
                portraitResourcePath,
                isEmpty,
                isFrozen,
                canPurchase,
                canToggleFrozen);
        }
    }

    /// <summary>Read-only HUD projection. Economy, shop contents, readiness and commands stay owned by LocalMatchState.</summary>
    public sealed class ShopReadyHudState
    {
        private static readonly int[] LocalUpgradeCosts =
            { 4, 6, 8, 10, 12, 14, 16, 18 };

        private ShopReadyHudState(
            int level,
            int gold,
            int upgradeCost,
            bool isReady,
            bool shopVisible,
            ShopReadyConfirmation confirmation,
            bool formationInteractionEnabled,
            bool shopCommandsEnabled,
            IEnumerable<ShopReadySlotViewState> slots)
        {
            Level = level;
            Gold = gold;
            UpgradeCost = upgradeCost;
            IsReady = isReady;
            ShopVisible = shopVisible;
            PendingConfirmation = confirmation;
            FormationInteractionEnabled = formationInteractionEnabled;
            ShopCommandsEnabled = shopCommandsEnabled;
            Slots = new ReadOnlyCollection<ShopReadySlotViewState>((slots ?? Enumerable.Empty<ShopReadySlotViewState>()).OrderBy(slot => slot.ShopSlotId).ToArray());
        }

        public int Level { get; }
        public int Gold { get; }
        public int UpgradeCost { get; }
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
            return new ShopReadyHudState(
                player.Level,
                player.Gold,
                player.Level >= 1 && player.Level <= LocalUpgradeCosts.Length
                    ? LocalUpgradeCosts[player.Level - 1]
                    : 0,
                player.IsReady,
                shopVisible,
                confirmation,
                !player.IsReady,
                true,
                player.ShopSlots.Select(slot => new ShopReadySlotViewState(slot, player.Gold)));
        }

        public static ShopReadyHudState CreateProjection(
            int level,
            int gold,
            int upgradeCost,
            bool isReady,
            bool shopVisible,
            ShopReadyConfirmation confirmation,
            bool formationInteractionEnabled,
            bool shopCommandsEnabled,
            IEnumerable<ShopReadySlotViewState> slots)
        {
            return new ShopReadyHudState(
                level,
                gold,
                upgradeCost,
                isReady,
                shopVisible,
                confirmation,
                formationInteractionEnabled,
                shopCommandsEnabled,
                slots);
        }

        public ShopReadyHudState WithShopVisible(bool visible)
        {
            return new ShopReadyHudState(
                Level,
                Gold,
                UpgradeCost,
                IsReady,
                visible,
                PendingConfirmation,
                FormationInteractionEnabled,
                ShopCommandsEnabled,
                Slots);
        }

        public static ShopReadyConfirmation RequestConfirmation(ShopReadyConfirmation current, ShopReadyConfirmation requested)
        {
            if (requested == ShopReadyConfirmation.None) return ShopReadyConfirmation.None;
            return current == requested ? ShopReadyConfirmation.None : requested;
        }
    }
}
