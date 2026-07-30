using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ArknoNights.Battle.Core
{
    public sealed class BattleUnitAttributesSnapshot
        : IEquatable<BattleUnitAttributesSnapshot>
    {
        public BattleUnitAttributesSnapshot(
            int attack,
            int defense,
            int magicResistance,
            int moveSpeedCentimetresPerSecond,
            int attackIntervalTicks,
            int blockCapacity)
        {
            Attack = attack;
            Defense = defense;
            MagicResistance = magicResistance;
            MoveSpeedCentimetresPerSecond =
                moveSpeedCentimetresPerSecond;
            AttackIntervalTicks = attackIntervalTicks;
            BlockCapacity = blockCapacity;
        }

        public int Attack { get; }
        public int Defense { get; }
        public int MagicResistance { get; }
        public int MoveSpeedCentimetresPerSecond { get; }
        public int AttackIntervalTicks { get; }
        public int BlockCapacity { get; }

        public bool Equals(BattleUnitAttributesSnapshot other)
        {
            return other != null
                && Attack == other.Attack
                && Defense == other.Defense
                && MagicResistance == other.MagicResistance
                && MoveSpeedCentimetresPerSecond
                    == other.MoveSpeedCentimetresPerSecond
                && AttackIntervalTicks
                    == other.AttackIntervalTicks
                && BlockCapacity == other.BlockCapacity;
        }

        public override bool Equals(object obj) =>
            Equals(obj as BattleUnitAttributesSnapshot);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Attack;
                hash = hash * 397 ^ Defense;
                hash = hash * 397 ^ MagicResistance;
                hash = hash * 397
                    ^ MoveSpeedCentimetresPerSecond;
                hash = hash * 397 ^ AttackIntervalTicks;
                return hash * 397 ^ BlockCapacity;
            }
        }
    }

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
            : this(
                unitId,
                typeId,
                playerId,
                side,
                isDynamicallyGenerated,
                position,
                eliteLevel,
                maxHitPoints,
                currentHitPoints,
                currentShield,
                attack,
                defense,
                magicResistance,
                moveSpeedCentimetresPerSecond,
                attackIntervalTicks,
                attackAnimationDurationTicks,
                damageType,
                attackMethod,
                blockCapacity,
                tauntLevel,
                buffs,
                0)
        {
        }

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
            IEnumerable<BuffPlaceholder> buffs,
            int activationTick,
            int lifeDeduct = 1)
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
            ActivationTick = activationTick;
            LifeDeduct = lifeDeduct;
            Attributes = new BattleUnitAttributesSnapshot(
                attack,
                defense,
                magicResistance,
                moveSpeedCentimetresPerSecond,
                attackIntervalTicks,
                blockCapacity);
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
        public int ActivationTick { get; }
        public int LifeDeduct { get; }
        public BattleUnitAttributesSnapshot Attributes { get; }
    }
}
