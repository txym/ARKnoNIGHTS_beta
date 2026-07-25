using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ArknoNights.Battle.Core
{
    public sealed class BattleUnitInstanceSnapshot
    {
        public BattleUnitInstanceSnapshot(
            string unitId,
            string typeId,
            string playerId,
            BattleSide side,
            bool isDynamicallyGenerated,
            FixedPosition position,
            int eliteLevel,
            int maxHitPoints,
            int currentHitPoints,
            int currentShield,
            int attack,
            int defense,
            int magicResistance,
            int moveSpeedCentimetresPerSecond,
            int attackIntervalTicks,
            int attackAnimationDurationTicks,
            DamageType damageType,
            AttackMethod attackMethod,
            int blockCapacity,
            int tauntLevel,
            IEnumerable<BuffPlaceholder> buffs)
        {
            UnitId = unitId;
            TypeId = typeId;
            PlayerId = playerId;
            Side = side;
            IsDynamicallyGenerated = isDynamicallyGenerated;
            Position = position;
            EliteLevel = eliteLevel;
            MaxHitPoints = maxHitPoints;
            CurrentHitPoints = currentHitPoints;
            CurrentShield = currentShield;
            Attack = attack;
            Defense = defense;
            MagicResistance = magicResistance;
            MoveSpeedCentimetresPerSecond = moveSpeedCentimetresPerSecond;
            AttackIntervalTicks = attackIntervalTicks;
            AttackAnimationDurationTicks = attackAnimationDurationTicks;
            DamageType = damageType;
            AttackMethod = attackMethod;
            BlockCapacity = blockCapacity;
            TauntLevel = tauntLevel;
            Buffs = new ReadOnlyCollection<BuffPlaceholder>((buffs ?? Enumerable.Empty<BuffPlaceholder>()).ToArray());
        }

        public string UnitId { get; }
        public string TypeId { get; }
        public string PlayerId { get; }
        public BattleSide Side { get; }
        public bool IsDynamicallyGenerated { get; }
        public FixedPosition Position { get; }
        public int EliteLevel { get; }
        public int MaxHitPoints { get; }
        public int CurrentHitPoints { get; }
        public int CurrentShield { get; }
        public int Attack { get; }
        public int Defense { get; }
        public int MagicResistance { get; }
        public int MoveSpeedCentimetresPerSecond { get; }
        public int AttackIntervalTicks { get; }
        public int AttackAnimationDurationTicks { get; }
        public DamageType DamageType { get; }
        public AttackMethod AttackMethod { get; }
        public int BlockCapacity { get; }
        public int TauntLevel { get; }
        public IReadOnlyList<BuffPlaceholder> Buffs { get; }
    }
}
