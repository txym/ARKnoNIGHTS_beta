using ArknoNights.Battle.Presentation;
using NUnit.Framework;

namespace ArknoNights.Battle.Tests
{
    public sealed class UnitWorldStatusBarLayoutEditModeTests
    {
        [Test]
        public void Layout_HidesFullHealthWithoutShieldAndShowsDamageOrShield()
        {
            Assert.IsFalse(UnitWorldStatusBarLayout.Calculate(100, 100, 0).IsVisible);
            Assert.IsTrue(UnitWorldStatusBarLayout.Calculate(100, 99, 0).IsVisible);
            Assert.IsTrue(UnitWorldStatusBarLayout.Calculate(100, 100, 1).IsVisible);
        }

        [Test]
        public void Layout_UsesTwoStepHealthCapacityAndRightAnchoredShieldWidths()
        {
            var withoutShield = UnitWorldStatusBarLayout.Calculate(100, 50, 0);
            Assert.That(withoutShield.MaxHitPointsWidth, Is.EqualTo(90f));
            Assert.That(withoutShield.CurrentHitPointsWidth, Is.EqualTo(45f));
            Assert.That(withoutShield.ShieldWidth, Is.EqualTo(0f));

            var withShield = UnitWorldStatusBarLayout.Calculate(100, 50, 50);
            Assert.That(withShield.MaxHitPointsWidth, Is.EqualTo(60f));
            Assert.That(withShield.CurrentHitPointsWidth, Is.EqualTo(30f));
            Assert.That(withShield.ShieldWidth, Is.EqualTo(30f));
        }

        [Test]
        public void Layout_InvalidMaximumHidesWithoutDivisionByZero()
        {
            var layout = UnitWorldStatusBarLayout.Calculate(0, 50, 10);
            Assert.IsFalse(layout.IsValid);
            Assert.IsFalse(layout.IsVisible);
            Assert.That(layout.MaxHitPointsWidth, Is.EqualTo(0f));
        }
    }
}
