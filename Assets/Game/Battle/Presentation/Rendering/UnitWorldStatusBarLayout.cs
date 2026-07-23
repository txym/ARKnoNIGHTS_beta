using System;

namespace ArknoNights.Battle.Presentation
{
    /// <summary>
    /// Scene-independent geometry contract for DefaultUnit's world-space status bar.
    /// Values are presentation-only and never write back to Battle Core.
    /// </summary>
    public readonly struct UnitWorldStatusBarLayout
    {
        public const float TotalWidth = 90f;

        private UnitWorldStatusBarLayout(bool isValid, bool isVisible, float maxHitPointsWidth, float currentHitPointsWidth, float shieldWidth)
        {
            IsValid = isValid;
            IsVisible = isVisible;
            MaxHitPointsWidth = maxHitPointsWidth;
            CurrentHitPointsWidth = currentHitPointsWidth;
            ShieldWidth = shieldWidth;
        }

        public bool IsValid { get; }
        public bool IsVisible { get; }
        public float MaxHitPointsWidth { get; }
        public float CurrentHitPointsWidth { get; }
        public float ShieldWidth { get; }

        public static UnitWorldStatusBarLayout Calculate(int maxHitPoints, int currentHitPoints, int currentShield)
        {
            if (maxHitPoints <= 0) return new UnitWorldStatusBarLayout(false, false, 0f, 0f, 0f);

            var clampedHitPoints = Math.Max(0, Math.Min(currentHitPoints, maxHitPoints));
            var shield = Math.Max(0, currentShield);
            var totalCapacity = (float)maxHitPoints + shield;
            var maxHitPointsWidth = TotalWidth * maxHitPoints / totalCapacity;
            var currentHitPointsWidth = maxHitPointsWidth * clampedHitPoints / maxHitPoints;
            var shieldWidth = TotalWidth * shield / totalCapacity;
            var isVisible = shield > 0 || clampedHitPoints < maxHitPoints;
            return new UnitWorldStatusBarLayout(true, isVisible, maxHitPointsWidth, currentHitPointsWidth, shieldWidth);
        }
    }
}
