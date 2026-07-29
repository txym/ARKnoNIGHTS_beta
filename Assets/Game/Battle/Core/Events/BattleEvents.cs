using System;

namespace ArknoNights.Battle.Core
{
    public enum BattleEventType
    {
        Spawn,
        Move,
        TargetChanged,
        BlockStarted,
        BlockEnded,
        Attack,
        Damage,
        Death,
        BattleEnded,
        Skill,
        HealthChanged,
        GateReached,
        PresentationStateChanged
    }

    /// <summary>Immutable presentation-neutral record emitted by the authoritative tick runner.</summary>
    public sealed class BattleEvent
    {
        internal BattleEvent(BattleEventType type, int tick, int sequence, string unitId, string unitTypeId, BattleSide? unitSide, string relatedUnitId, FixedPosition? fromPosition, FixedPosition? toPosition, DamageType? damageType, int damageAmount, int hitPointsBefore, int hitPointsAfter, int plannedDamageTick, int originalAnimationTicks, int effectiveAnimationTicks, BattleSide? winner, BattleStopReason reason, BattleUnitInstanceSnapshot spawnSnapshot, string animationKey = null)
        {
            Type = type;
            Tick = tick;
            Sequence = sequence;
            UnitId = unitId;
            UnitTypeId = unitTypeId;
            UnitSide = unitSide;
            RelatedUnitId = relatedUnitId;
            FromPosition = fromPosition;
            ToPosition = toPosition;
            DamageType = damageType;
            DamageAmount = damageAmount;
            HitPointsBefore = hitPointsBefore;
            HitPointsAfter = hitPointsAfter;
            PlannedDamageTick = plannedDamageTick;
            OriginalAnimationTicks = originalAnimationTicks;
            EffectiveAnimationTicks = effectiveAnimationTicks;
            Winner = winner;
            Reason = reason;
            SpawnSnapshot = spawnSnapshot;
            AnimationKey = animationKey ?? string.Empty;
        }

        public BattleEventType Type { get; }
        public int Tick { get; }
        public int Sequence { get; }
        public string UnitId { get; }
        /// <summary>Authoritative type identity for Spawn. Null for events that do not create a unit.</summary>
        public string UnitTypeId { get; }
        /// <summary>Authoritative side identity for Spawn. Null for events that do not create a unit.</summary>
        public BattleSide? UnitSide { get; }
        public string RelatedUnitId { get; }
        public FixedPosition? FromPosition { get; }
        public FixedPosition? ToPosition { get; }
        public DamageType? DamageType { get; }
        public int DamageAmount { get; }
        public int HitPointsBefore { get; }
        public int HitPointsAfter { get; }
        public int PlannedDamageTick { get; }
        public int OriginalAnimationTicks { get; }
        public int EffectiveAnimationTicks { get; }
        public BattleSide? Winner { get; }
        public BattleStopReason Reason { get; }
        public BattleUnitInstanceSnapshot SpawnSnapshot { get; }
        public string AnimationKey { get; }
    }
}
