using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Serialization;

namespace ArknoNights.Battle.Core
{
    [DataContract]
    public sealed class BattleUnitAttributesPayload
    {
        internal BattleUnitAttributesPayload(
            BattleUnitAttributesSnapshot source)
        {
            Attack = source.Attack;
            Defense = source.Defense;
            MagicResistance = source.MagicResistance;
            MoveSpeedCentimetresPerSecond =
                source.MoveSpeedCentimetresPerSecond;
            AttackIntervalTicks =
                source.AttackIntervalTicks;
            BlockCapacity = source.BlockCapacity;
        }

        [DataMember(Name = "attack", Order = 1)]
        public int Attack { get; private set; }
        [DataMember(Name = "defense", Order = 2)]
        public int Defense { get; private set; }
        [DataMember(Name = "magicResistance", Order = 3)]
        public int MagicResistance { get; private set; }
        [DataMember(Name = "moveSpeedCentimetresPerSecond", Order = 4)]
        public int MoveSpeedCentimetresPerSecond { get; private set; }
        [DataMember(Name = "attackIntervalTicks", Order = 5)]
        public int AttackIntervalTicks { get; private set; }
        [DataMember(Name = "blockCapacity", Order = 6)]
        public int BlockCapacity { get; private set; }
    }

    [DataContract]
    public sealed class BattleBuffPayload
    {
        internal BattleBuffPayload(BuffPlaceholder source)
        {
            Id = source.Id;
            RawPayload = source.RawPayload;
        }

        [DataMember(Name = "id", Order = 1)]
        public string Id { get; private set; }
        [DataMember(Name = "rawPayload", Order = 2)]
        public string RawPayload { get; private set; }
    }

    [DataContract]
    public sealed class BattleSpawnSnapshotPayload
    {
        internal BattleSpawnSnapshotPayload(
            BattleUnitInstanceSnapshot source)
        {
            UnitId = source.UnitId;
            TypeId = source.TypeId;
            PlayerId = source.PlayerId;
            Side = source.Side;
            IsDynamicallyGenerated =
                source.IsDynamicallyGenerated;
            PositionXUnits = source.Position.XUnits;
            PositionYUnits = source.Position.YUnits;
            EliteLevel = source.EliteLevel;
            MaxHitPoints = source.MaxHitPoints;
            CurrentHitPoints = source.CurrentHitPoints;
            CurrentShield = source.CurrentShield;
            Attack = source.Attack;
            Defense = source.Defense;
            MagicResistance = source.MagicResistance;
            MoveSpeedCentimetresPerSecond =
                source.MoveSpeedCentimetresPerSecond;
            AttackIntervalTicks = source.AttackIntervalTicks;
            AttackAnimationDurationTicks =
                source.AttackAnimationDurationTicks;
            DamageType = source.DamageType;
            AttackMethod = source.AttackMethod;
            BlockCapacity = source.BlockCapacity;
            TauntLevel = source.TauntLevel;
            ActivationTick = source.ActivationTick;
            LifeDeduct = source.LifeDeduct;
            serializedBuffs = source.Buffs
                .OrderBy(item => item.Id, StringComparer.Ordinal)
                .ThenBy(
                    item => item.RawPayload,
                    StringComparer.Ordinal)
                .Select(item => new BattleBuffPayload(item))
                .ToArray();
        }

        [DataMember(Name = "unitId", Order = 1)]
        public string UnitId { get; private set; }
        [DataMember(Name = "typeId", Order = 2)]
        public string TypeId { get; private set; }
        [DataMember(Name = "playerId", Order = 3)]
        public string PlayerId { get; private set; }
        [DataMember(Name = "side", Order = 4)]
        public BattleSide Side { get; private set; }
        [DataMember(Name = "isDynamicallyGenerated", Order = 5)]
        public bool IsDynamicallyGenerated { get; private set; }
        [DataMember(Name = "positionXUnits", Order = 6)]
        public int PositionXUnits { get; private set; }
        [DataMember(Name = "positionYUnits", Order = 7)]
        public int PositionYUnits { get; private set; }
        [DataMember(Name = "eliteLevel", Order = 8)]
        public int EliteLevel { get; private set; }
        [DataMember(Name = "maxHitPoints", Order = 9)]
        public int MaxHitPoints { get; private set; }
        [DataMember(Name = "currentHitPoints", Order = 10)]
        public int CurrentHitPoints { get; private set; }
        [DataMember(Name = "currentShield", Order = 11)]
        public int CurrentShield { get; private set; }
        [DataMember(Name = "attack", Order = 12)]
        public int Attack { get; private set; }
        [DataMember(Name = "defense", Order = 13)]
        public int Defense { get; private set; }
        [DataMember(Name = "magicResistance", Order = 14)]
        public int MagicResistance { get; private set; }
        [DataMember(Name = "moveSpeedCentimetresPerSecond", Order = 15)]
        public int MoveSpeedCentimetresPerSecond { get; private set; }
        [DataMember(Name = "attackIntervalTicks", Order = 16)]
        public int AttackIntervalTicks { get; private set; }
        [DataMember(Name = "attackAnimationDurationTicks", Order = 17)]
        public int AttackAnimationDurationTicks { get; private set; }
        [DataMember(Name = "damageType", Order = 18)]
        public DamageType DamageType { get; private set; }
        [DataMember(Name = "attackMethod", Order = 19)]
        public AttackMethod AttackMethod { get; private set; }
        [DataMember(Name = "blockCapacity", Order = 20)]
        public int BlockCapacity { get; private set; }
        [DataMember(Name = "tauntLevel", Order = 21)]
        public int TauntLevel { get; private set; }
        [DataMember(Name = "activationTick", Order = 22)]
        public int ActivationTick { get; private set; }
        [DataMember(Name = "lifeDeduct", Order = 23)]
        public int LifeDeduct { get; private set; }
        [DataMember(Name = "buffs", Order = 24)]
        private BattleBuffPayload[] serializedBuffs;
        public IReadOnlyList<BattleBuffPayload> Buffs =>
            new ReadOnlyCollection<BattleBuffPayload>(
                serializedBuffs
                ?? Array.Empty<BattleBuffPayload>());
    }

    [DataContract]
    public sealed class BattleEventPayload
    {
        internal BattleEventPayload(BattleEvent source)
        {
            Type = source.Type;
            Tick = source.Tick;
            Sequence = source.Sequence;
            UnitId = source.UnitId;
            UnitTypeId = source.UnitTypeId;
            UnitSide = source.UnitSide;
            RelatedUnitId = source.RelatedUnitId;
            FromPositionXUnits =
                source.FromPosition?.XUnits;
            FromPositionYUnits =
                source.FromPosition?.YUnits;
            ToPositionXUnits = source.ToPosition?.XUnits;
            ToPositionYUnits = source.ToPosition?.YUnits;
            DamageType = source.DamageType;
            DamageAmount = source.DamageAmount;
            HitPointsBefore = source.HitPointsBefore;
            HitPointsAfter = source.HitPointsAfter;
            PlannedDamageTick = source.PlannedDamageTick;
            OriginalAnimationTicks =
                source.OriginalAnimationTicks;
            EffectiveAnimationTicks =
                source.EffectiveAnimationTicks;
            Winner = source.Winner;
            Reason = source.Reason;
            AnimationKey = source.AnimationKey;
            SpawnSnapshot = source.SpawnSnapshot == null
                ? null
                : new BattleSpawnSnapshotPayload(
                    source.SpawnSnapshot);
            Attributes = source.AttributesSnapshot == null
                ? null
                : new BattleUnitAttributesPayload(
                    source.AttributesSnapshot);
        }

        [DataMember(Name = "type", Order = 1)]
        public BattleEventType Type { get; private set; }
        [DataMember(Name = "tick", Order = 2)]
        public int Tick { get; private set; }
        [DataMember(Name = "sequence", Order = 3)]
        public int Sequence { get; private set; }
        [DataMember(Name = "unitId", Order = 4)]
        public string UnitId { get; private set; }
        [DataMember(Name = "unitTypeId", Order = 5)]
        public string UnitTypeId { get; private set; }
        [DataMember(Name = "unitSide", Order = 6)]
        public BattleSide? UnitSide { get; private set; }
        [DataMember(Name = "relatedUnitId", Order = 7)]
        public string RelatedUnitId { get; private set; }
        [DataMember(Name = "fromPositionXUnits", Order = 8)]
        public int? FromPositionXUnits { get; private set; }
        [DataMember(Name = "fromPositionYUnits", Order = 9)]
        public int? FromPositionYUnits { get; private set; }
        [DataMember(Name = "toPositionXUnits", Order = 10)]
        public int? ToPositionXUnits { get; private set; }
        [DataMember(Name = "toPositionYUnits", Order = 11)]
        public int? ToPositionYUnits { get; private set; }
        [DataMember(Name = "damageType", Order = 12)]
        public DamageType? DamageType { get; private set; }
        [DataMember(Name = "damageAmount", Order = 13)]
        public int DamageAmount { get; private set; }
        [DataMember(Name = "hitPointsBefore", Order = 14)]
        public int HitPointsBefore { get; private set; }
        [DataMember(Name = "hitPointsAfter", Order = 15)]
        public int HitPointsAfter { get; private set; }
        [DataMember(Name = "plannedDamageTick", Order = 16)]
        public int PlannedDamageTick { get; private set; }
        [DataMember(Name = "originalAnimationTicks", Order = 17)]
        public int OriginalAnimationTicks { get; private set; }
        [DataMember(Name = "effectiveAnimationTicks", Order = 18)]
        public int EffectiveAnimationTicks { get; private set; }
        [DataMember(Name = "winner", Order = 19)]
        public BattleSide? Winner { get; private set; }
        [DataMember(Name = "reason", Order = 20)]
        public BattleStopReason Reason { get; private set; }
        [DataMember(Name = "animationKey", Order = 21)]
        public string AnimationKey { get; private set; }
        [DataMember(Name = "spawnSnapshot", Order = 22)]
        public BattleSpawnSnapshotPayload SpawnSnapshot
        {
            get;
            private set;
        }
        [DataMember(Name = "attributes", Order = 23)]
        public BattleUnitAttributesPayload Attributes
        {
            get;
            private set;
        }
    }

    [DataContract]
    public sealed class BattleUnitCheckpointPayload
    {
        internal BattleUnitCheckpointPayload(
            BattleUnitCheckpoint source)
        {
            UnitId = source.UnitId;
            TypeId = source.TypeId;
            PlayerId = source.PlayerId;
            Side = source.Side;
            IsDynamicallyGenerated =
                source.IsDynamicallyGenerated;
            IsActivated = source.IsActivated;
            IsAlive = source.IsAlive;
            HasReachedGate = source.HasReachedGate;
            PositionXUnits = source.Position.XUnits;
            PositionYUnits = source.Position.YUnits;
            EliteLevel = source.EliteLevel;
            MaxHitPoints = source.MaxHitPoints;
            CurrentHitPoints = source.CurrentHitPoints;
            CurrentShield = source.CurrentShield;
            Attack = source.Attack;
            Defense = source.Defense;
            MagicResistance = source.MagicResistance;
            MoveSpeedCentimetresPerSecond =
                source.MoveSpeedCentimetresPerSecond;
            AttackIntervalTicks =
                source.AttackIntervalTicks;
            BlockCapacity = source.BlockCapacity;
            TargetUnitId = source.TargetUnitId;
            Action = source.Action;
            ActionStartTick = source.ActionStartTick;
            ActionSequence = source.ActionSequence;
            ActionDurationTicks = source.ActionDurationTicks;
            AnimationKey = source.AnimationKey;
            PresentationStateTag =
                source.PresentationStateTag;
            SpawnTick = source.SpawnTick;
            ActivationTick = source.ActivationTick;
            DeathTick = source.DeathTick;
            serializedBuffs = source.Buffs
                .Select(item => new BattleBuffPayload(item))
                .ToArray();
            serializedBlockedUnitIds =
                source.BlockedUnitIds.ToArray();
            serializedExecutionStateFields =
                source.ExecutionStateFields.ToArray();
        }

        [DataMember(Name = "unitId", Order = 1)]
        public string UnitId { get; private set; }
        [DataMember(Name = "typeId", Order = 2)]
        public string TypeId { get; private set; }
        [DataMember(Name = "playerId", Order = 3)]
        public string PlayerId { get; private set; }
        [DataMember(Name = "side", Order = 4)]
        public BattleSide Side { get; private set; }
        [DataMember(Name = "isDynamicallyGenerated", Order = 5)]
        public bool IsDynamicallyGenerated { get; private set; }
        [DataMember(Name = "isActivated", Order = 6)]
        public bool IsActivated { get; private set; }
        [DataMember(Name = "isAlive", Order = 7)]
        public bool IsAlive { get; private set; }
        [DataMember(Name = "hasReachedGate", Order = 8)]
        public bool HasReachedGate { get; private set; }
        [DataMember(Name = "positionXUnits", Order = 9)]
        public int PositionXUnits { get; private set; }
        [DataMember(Name = "positionYUnits", Order = 10)]
        public int PositionYUnits { get; private set; }
        [DataMember(Name = "eliteLevel", Order = 11)]
        public int EliteLevel { get; private set; }
        [DataMember(Name = "maxHitPoints", Order = 12)]
        public int MaxHitPoints { get; private set; }
        [DataMember(Name = "currentHitPoints", Order = 13)]
        public int CurrentHitPoints { get; private set; }
        [DataMember(Name = "currentShield", Order = 14)]
        public int CurrentShield { get; private set; }
        [DataMember(Name = "targetUnitId", Order = 15)]
        public string TargetUnitId { get; private set; }
        [DataMember(Name = "action", Order = 16)]
        public BattleCheckpointAction Action { get; private set; }
        [DataMember(Name = "actionStartTick", Order = 17)]
        public int ActionStartTick { get; private set; }
        [DataMember(Name = "actionSequence", Order = 18)]
        public int ActionSequence { get; private set; }
        [DataMember(Name = "actionDurationTicks", Order = 19)]
        public int ActionDurationTicks { get; private set; }
        [DataMember(Name = "animationKey", Order = 20)]
        public string AnimationKey { get; private set; }
        [DataMember(Name = "presentationStateTag", Order = 21)]
        public string PresentationStateTag { get; private set; }
        [DataMember(Name = "spawnTick", Order = 22)]
        public int SpawnTick { get; private set; }
        [DataMember(Name = "activationTick", Order = 23)]
        public int ActivationTick { get; private set; }
        [DataMember(Name = "deathTick", Order = 24)]
        public int? DeathTick { get; private set; }
        [DataMember(Name = "buffs", Order = 25)]
        private BattleBuffPayload[] serializedBuffs;
        [DataMember(Name = "blockedUnitIds", Order = 26)]
        private string[] serializedBlockedUnitIds;
        [DataMember(Name = "executionStateFields", Order = 27)]
        private string[] serializedExecutionStateFields;
        public IReadOnlyList<BattleBuffPayload> Buffs =>
            new ReadOnlyCollection<BattleBuffPayload>(
                serializedBuffs
                ?? Array.Empty<BattleBuffPayload>());
        public IReadOnlyList<string> BlockedUnitIds =>
            new ReadOnlyCollection<string>(
                serializedBlockedUnitIds
                ?? Array.Empty<string>());
        public IReadOnlyList<string> ExecutionStateFields =>
            new ReadOnlyCollection<string>(
                serializedExecutionStateFields
                ?? Array.Empty<string>());
        [DataMember(Name = "attack", Order = 28)]
        public int Attack { get; private set; }
        [DataMember(Name = "defense", Order = 29)]
        public int Defense { get; private set; }
        [DataMember(Name = "magicResistance", Order = 30)]
        public int MagicResistance { get; private set; }
        [DataMember(Name = "moveSpeedCentimetresPerSecond", Order = 31)]
        public int MoveSpeedCentimetresPerSecond { get; private set; }
        [DataMember(Name = "attackIntervalTicks", Order = 32)]
        public int AttackIntervalTicks { get; private set; }
        [DataMember(Name = "blockCapacity", Order = 33)]
        public int BlockCapacity { get; private set; }
    }

    [DataContract]
    public sealed class BattlePresentationCheckpointPayload
    {
        internal BattlePresentationCheckpointPayload(
            BattlePresentationCheckpoint source)
        {
            Tick = source.Tick;
            HomeLifeDamage = source.HomeLifeDamage;
            AwayLifeDamage = source.AwayLifeDamage;
            Outcome = source.Outcome;
            StopReason = source.StopReason;
            serializedUnits = source.Units
                .OrderBy(
                    item => item.UnitId,
                    StringComparer.Ordinal)
                .Select(item =>
                    new BattleUnitCheckpointPayload(item))
                .ToArray();
        }

        [DataMember(Name = "tick", Order = 1)]
        public int Tick { get; private set; }
        [DataMember(Name = "homeLifeDamage", Order = 2)]
        public int? HomeLifeDamage { get; private set; }
        [DataMember(Name = "awayLifeDamage", Order = 3)]
        public int? AwayLifeDamage { get; private set; }
        [DataMember(Name = "outcome", Order = 4)]
        public BattleOutcome? Outcome { get; private set; }
        [DataMember(Name = "stopReason", Order = 5)]
        public BattleStopReason? StopReason { get; private set; }
        [DataMember(Name = "units", Order = 6)]
        private BattleUnitCheckpointPayload[] serializedUnits;
        public IReadOnlyList<BattleUnitCheckpointPayload> Units =>
            new ReadOnlyCollection<BattleUnitCheckpointPayload>(
                serializedUnits
                ?? Array.Empty<BattleUnitCheckpointPayload>());
        public bool IsTerminal => Outcome.HasValue;
    }

    [DataContract]
    public sealed class BattleChunkPayload
    {
        public BattleChunkPayload(BattleSimulationChunk source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            SchemaVersion = "battle-chunk-payload-v1";
            BattleId = source.BattleId;
            SealedInputHash = source.SealedInputHash;
            ChunkIndex = source.ChunkIndex;
            PreviousCompletedTick =
                source.PreviousCompletedTick;
            CompletedTick = source.CompletedTick;
            AuthoritativeTickCount =
                source.AuthoritativeTickCount;
            serializedEvents = source.Events
                .OrderBy(item => item.Tick)
                .ThenBy(item => item.Sequence)
                .Select(item => new BattleEventPayload(item))
                .ToArray();
            EndCheckpoint =
                new BattlePresentationCheckpointPayload(
                    source.EndCheckpoint);
            IsTerminal = source.IsTerminal;
            StopReason = source.StopReason;
            Outcome = source.Outcome;
            HomeLifeDamage = source.HomeLifeDamage;
            AwayLifeDamage = source.AwayLifeDamage;
            FinalSecondSha256 = source.FinalSecondSha256;
        }

        [DataMember(Name = "schemaVersion", Order = 1)]
        public string SchemaVersion { get; private set; }
        [DataMember(Name = "battleId", Order = 2)]
        public string BattleId { get; private set; }
        [DataMember(Name = "sealedInputHash", Order = 3)]
        public string SealedInputHash { get; private set; }
        [DataMember(Name = "chunkIndex", Order = 4)]
        public int ChunkIndex { get; private set; }
        [DataMember(Name = "previousCompletedTick", Order = 5)]
        public int PreviousCompletedTick { get; private set; }
        [DataMember(Name = "completedTick", Order = 6)]
        public int CompletedTick { get; private set; }
        [DataMember(Name = "authoritativeTickCount", Order = 7)]
        public int AuthoritativeTickCount { get; private set; }
        [DataMember(Name = "events", Order = 8)]
        private BattleEventPayload[] serializedEvents;
        [DataMember(Name = "endCheckpoint", Order = 9)]
        public BattlePresentationCheckpointPayload EndCheckpoint
        {
            get;
            private set;
        }
        [DataMember(Name = "isTerminal", Order = 10)]
        public bool IsTerminal { get; private set; }
        [DataMember(Name = "stopReason", Order = 11)]
        public BattleStopReason? StopReason { get; private set; }
        [DataMember(Name = "outcome", Order = 12)]
        public BattleOutcome? Outcome { get; private set; }
        [DataMember(Name = "homeLifeDamage", Order = 13)]
        public int? HomeLifeDamage { get; private set; }
        [DataMember(Name = "awayLifeDamage", Order = 14)]
        public int? AwayLifeDamage { get; private set; }
        [DataMember(Name = "finalSecondSha256", Order = 15)]
        public string FinalSecondSha256 { get; private set; }
        public IReadOnlyList<BattleEventPayload> Events =>
            new ReadOnlyCollection<BattleEventPayload>(
                serializedEvents
                ?? Array.Empty<BattleEventPayload>());
    }

    [DataContract]
    public sealed class FinalSecondHashPayload
    {
        internal FinalSecondHashPayload(
            string battleId,
            string sealedInputHash,
            int completedTicks,
            string finalSecondSha256)
        {
            SchemaVersion = "final-second-hash-v1";
            BattleId = battleId;
            SealedInputHash = sealedInputHash;
            CompletedTicks = completedTicks;
            FinalSecondSha256 = finalSecondSha256;
        }

        [DataMember(Name = "schemaVersion", Order = 1)]
        public string SchemaVersion { get; private set; }
        [DataMember(Name = "battleId", Order = 2)]
        public string BattleId { get; private set; }
        [DataMember(Name = "sealedInputHash", Order = 3)]
        public string SealedInputHash { get; private set; }
        [DataMember(Name = "completedTicks", Order = 4)]
        public int CompletedTicks { get; private set; }
        [DataMember(Name = "finalSecondSha256", Order = 5)]
        public string FinalSecondSha256 { get; private set; }
    }

    [DataContract]
    public sealed class BattleRecoveryDescriptor
    {
        internal BattleRecoveryDescriptor(
            string battleId,
            string sealedInputHash,
            int nearestCheckpointTick,
            int firstPublishedChunkIndex,
            int lastPublishedChunkIndex,
            bool isTerminal,
            int catchUpThroughTick)
        {
            SchemaVersion = "battle-recovery-v1";
            BattleId = battleId;
            SealedInputHash = sealedInputHash;
            NearestCheckpointTick = nearestCheckpointTick;
            FirstPublishedChunkIndex = firstPublishedChunkIndex;
            LastPublishedChunkIndex = lastPublishedChunkIndex;
            IsTerminal = isTerminal;
            CatchUpThroughTick = catchUpThroughTick;
        }

        [DataMember(Name = "schemaVersion", Order = 1)]
        public string SchemaVersion { get; private set; }
        [DataMember(Name = "battleId", Order = 2)]
        public string BattleId { get; private set; }
        [DataMember(Name = "sealedInputHash", Order = 3)]
        public string SealedInputHash { get; private set; }
        [DataMember(Name = "nearestCheckpointTick", Order = 4)]
        public int NearestCheckpointTick { get; private set; }
        [DataMember(Name = "firstPublishedChunkIndex", Order = 5)]
        public int FirstPublishedChunkIndex { get; private set; }
        [DataMember(Name = "lastPublishedChunkIndex", Order = 6)]
        public int LastPublishedChunkIndex { get; private set; }
        [DataMember(Name = "isTerminal", Order = 7)]
        public bool IsTerminal { get; private set; }
        [DataMember(Name = "catchUpThroughTick", Order = 8)]
        public int CatchUpThroughTick { get; private set; }
    }
}
