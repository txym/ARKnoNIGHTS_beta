using System;

namespace ArknoNights.Battle.Core
{
    public static class DamageCalculator
    {
        public static int Calculate(DamageType damageType, int attack, int defense, int magicResistance)
        {
            if (damageType == DamageType.True) return attack;
            var floorFivePercent = attack * 5 / 100;
            return damageType == DamageType.Physical
                ? Math.Max(attack - defense, floorFivePercent)
                : Math.Max(attack * (100 - magicResistance) / 100, floorFivePercent);
        }
    }
}
