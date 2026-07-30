using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace ArknoNights.Battle.Core
{
    public static class BattleOutcomeResolver
    {
        public static BattleOutcome FromLifeDamage(
            int homeLifeDamage,
            int awayLifeDamage)
        {
            if (homeLifeDamage < 0)
                throw new ArgumentOutOfRangeException(nameof(homeLifeDamage));
            if (awayLifeDamage < 0)
                throw new ArgumentOutOfRangeException(nameof(awayLifeDamage));
            if (homeLifeDamage < awayLifeDamage)
                return BattleOutcome.HomeWin;
            if (homeLifeDamage > awayLifeDamage)
                return BattleOutcome.AwayWin;
            return BattleOutcome.Draw;
        }

        public static BattleSide? ToWinner(BattleOutcome outcome)
        {
            switch (outcome)
            {
                case BattleOutcome.HomeWin:
                    return BattleSide.Home;
                case BattleOutcome.AwayWin:
                    return BattleSide.Away;
                case BattleOutcome.Draw:
                    return null;
                default:
                    throw new ArgumentOutOfRangeException(nameof(outcome));
            }
        }
    }

    public static class BattleInputSha256
    {
        public static string Compute(BattleInput input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));
            return ComputeCanonicalSummary(input.CanonicalSummary);
        }

        public static string ComputeCanonicalSummary(string canonicalSummary)
        {
            if (canonicalSummary == null)
                throw new ArgumentNullException(nameof(canonicalSummary));
            using (var sha256 = SHA256.Create())
                return Hex(sha256.ComputeHash(
                    Encoding.UTF8.GetBytes(canonicalSummary)));
        }

        internal static string Hex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var item in bytes)
                builder.Append(item.ToString("X2"));
            return builder.ToString();
        }

        internal static bool IsCanonicalHash(string value)
        {
            return value != null
                && value.Length == 64
                && value.All(item =>
                    item >= '0' && item <= '9'
                    || item >= 'A' && item <= 'F');
        }
    }

    public enum BattleCheckpointAction
    {
        Idle = 0,
        Move = 1,
        Attack = 2,
        Skill = 3,
        Death = 4
    }

    public sealed class BattleUnitCheckpoint
    {
        internal BattleUnitCheckpoint(
            RuntimeUnitState state,
            BattleUnitInstanceSnapshot snapshot,
            IReadOnlyList<BattleEvent> unitEvents,
            int checkpointTick)
        {
            UnitId = state.UnitId;
            TypeId = state.TypeId;
            PlayerId = state.PlayerId;
            Side = state.Side;
            IsDynamicallyGenerated = snapshot.IsDynamicallyGenerated;
            IsActivated = state.ActivationTick <= checkpointTick;
            IsAlive = state.IsAlive;
            HasReachedGate = state.HasExitedBattle;
            Position = state.Position;
            EliteLevel = state.EliteLevel;
            Buffs = new ReadOnlyCollection<BuffPlaceholder>(
                state.Buffs
                    .OrderBy(item => item.Id, StringComparer.Ordinal)
                    .ThenBy(item => item.RawPayload, StringComparer.Ordinal)
                    .ToArray());
            MaxHitPoints = snapshot.MaxHitPoints;
            CurrentHitPoints = state.CurrentHitPoints;
            CurrentShield = snapshot.CurrentShield;
            TargetUnitId = state.TargetUnitId;
            BlockedUnitIds = new ReadOnlyCollection<string>(
                state.BlockedUnitIds
                    .OrderBy(item => item, StringComparer.Ordinal)
                    .ToArray());
            SpawnTick = unitEvents
                .Where(item => item.Type == BattleEventType.Spawn)
                .Select(item => item.Tick)
                .DefaultIfEmpty(0)
                .First();
            ActivationTick = state.ActivationTick;
            DeathTick = unitEvents
                .Where(item => item.Type == BattleEventType.Death)
                .Select(item => (int?)item.Tick)
                .FirstOrDefault();
            PresentationStateTag = unitEvents
                .Where(item =>
                    item.Type == BattleEventType.PresentationStateChanged
                    && item.Tick <= checkpointTick)
                .OrderBy(item => item.Tick)
                .ThenBy(item => item.Sequence)
                .Select(item => item.AnimationKey)
                .DefaultIfEmpty(string.Empty)
                .Last();
            ExecutionStateFields =
                state.BuildCanonicalExecutionStateFields();

            ResolveAction(
                unitEvents,
                checkpointTick,
                IsAlive,
                DeathTick,
                out var action,
                out var actionStartTick,
                out var actionSequence,
                out var actionDurationTicks,
                out var animationKey);
            Action = action;
            ActionStartTick = actionStartTick;
            ActionSequence = actionSequence;
            ActionDurationTicks = actionDurationTicks;
            AnimationKey = animationKey;
        }

        public string UnitId { get; }
        public string TypeId { get; }
        public string PlayerId { get; }
        public BattleSide Side { get; }
        public bool IsDynamicallyGenerated { get; }
        public bool IsActivated { get; }
        public bool IsAlive { get; }
        public bool HasReachedGate { get; }
        public FixedPosition Position { get; }
        public int EliteLevel { get; }
        public IReadOnlyList<BuffPlaceholder> Buffs { get; }
        public int MaxHitPoints { get; }
        public int CurrentHitPoints { get; }
        public int CurrentShield { get; }
        public string TargetUnitId { get; }
        public IReadOnlyList<string> BlockedUnitIds { get; }
        public BattleCheckpointAction Action { get; }
        public int ActionStartTick { get; }
        public int ActionSequence { get; }
        public int ActionDurationTicks { get; }
        public string AnimationKey { get; }
        public string PresentationStateTag { get; }
        public int SpawnTick { get; }
        public int ActivationTick { get; }
        public int? DeathTick { get; }
        public IReadOnlyList<string> ExecutionStateFields { get; }

        private static void ResolveAction(
            IReadOnlyList<BattleEvent> unitEvents,
            int checkpointTick,
            bool isAlive,
            int? deathTick,
            out BattleCheckpointAction action,
            out int startTick,
            out int sequence,
            out int durationTicks,
            out string animationKey)
        {
            action = BattleCheckpointAction.Idle;
            startTick = 0;
            sequence = 0;
            durationTicks = 0;
            animationKey = string.Empty;
            if (!isAlive)
            {
                action = BattleCheckpointAction.Death;
                startTick = deathTick ?? checkpointTick;
                return;
            }

            var attack = unitEvents
                .Where(item =>
                    item.Type == BattleEventType.Attack
                    && item.Tick <= checkpointTick
                    && checkpointTick
                        <= item.Tick + item.EffectiveAnimationTicks)
                .OrderByDescending(item => item.Tick)
                .ThenByDescending(item => item.Sequence)
                .FirstOrDefault();
            if (attack != null)
            {
                action = BattleCheckpointAction.Attack;
                startTick = attack.Tick;
                sequence = attack.Sequence;
                durationTicks = attack.EffectiveAnimationTicks;
                return;
            }

            var skill = unitEvents
                .Where(item =>
                    item.Type == BattleEventType.Skill
                    && item.Tick <= checkpointTick
                    && checkpointTick
                        <= item.Tick + item.EffectiveAnimationTicks)
                .OrderByDescending(item => item.Tick)
                .ThenByDescending(item => item.Sequence)
                .FirstOrDefault();
            if (skill != null)
            {
                action = BattleCheckpointAction.Skill;
                startTick = skill.Tick;
                sequence = skill.Sequence;
                durationTicks = skill.EffectiveAnimationTicks;
                animationKey = skill.AnimationKey;
                return;
            }

            var move = unitEvents
                .Where(item =>
                    item.Type == BattleEventType.Move
                    && item.Tick == checkpointTick)
                .OrderByDescending(item => item.Sequence)
                .FirstOrDefault();
            if (move != null)
            {
                action = BattleCheckpointAction.Move;
                startTick = move.Tick;
                sequence = move.Sequence;
                durationTicks = 1;
            }
        }
    }

    public sealed class BattlePresentationCheckpoint
    {
        private BattlePresentationCheckpoint(
            int tick,
            IReadOnlyList<BattleUnitCheckpoint> units,
            int? homeLifeDamage,
            int? awayLifeDamage,
            BattleOutcome? outcome,
            BattleStopReason? stopReason)
        {
            Tick = tick;
            Units = units;
            HomeLifeDamage = homeLifeDamage;
            AwayLifeDamage = awayLifeDamage;
            Outcome = outcome;
            StopReason = stopReason;
        }

        public int Tick { get; }
        public IReadOnlyList<BattleUnitCheckpoint> Units { get; }
        public int? HomeLifeDamage { get; }
        public int? AwayLifeDamage { get; }
        public BattleOutcome? Outcome { get; }
        public BattleStopReason? StopReason { get; }
        public bool IsTerminal => Outcome.HasValue;

        internal static BattlePresentationCheckpoint Create(
            int tick,
            IEnumerable<RuntimeUnitState> runtimeUnits,
            IReadOnlyList<BattleEvent> events,
            IReadOnlyDictionary<string, BattleUnitInstanceSnapshot> snapshots,
            int? homeLifeDamage,
            int? awayLifeDamage,
            BattleOutcome? outcome,
            BattleStopReason? stopReason)
        {
            var immutableUnits = runtimeUnits
                .OrderBy(item => item.UnitId, StringComparer.Ordinal)
                .Select(item => new BattleUnitCheckpoint(
                    item,
                    snapshots[item.UnitId],
                    new ReadOnlyCollection<BattleEvent>(events
                        .Where(e => string.Equals(
                            e.UnitId,
                            item.UnitId,
                            StringComparison.Ordinal))
                        .OrderBy(e => e.Tick)
                        .ThenBy(e => e.Sequence)
                        .ToArray()),
                    tick))
                .ToArray();
            return new BattlePresentationCheckpoint(
                tick,
                new ReadOnlyCollection<BattleUnitCheckpoint>(immutableUnits),
                homeLifeDamage,
                awayLifeDamage,
                outcome,
                stopReason);
        }
    }

    public sealed class BattleSimulationChunk
    {
        internal BattleSimulationChunk(
            string battleId,
            string sealedInputHash,
            int chunkIndex,
            int previousCompletedTick,
            int completedTick,
            IReadOnlyList<BattleEvent> events,
            BattlePresentationCheckpoint endCheckpoint,
            bool isTerminal,
            BattleStopReason? stopReason,
            BattleOutcome? outcome,
            int? homeLifeDamage,
            int? awayLifeDamage,
            string finalSecondSha256)
        {
            SchemaVersion = BattleSimulationProducer.ChunkSchemaVersion;
            BattleId = battleId;
            SealedInputHash = sealedInputHash;
            ChunkIndex = chunkIndex;
            PreviousCompletedTick = previousCompletedTick;
            CompletedTick = completedTick;
            AuthoritativeTickCount = completedTick - previousCompletedTick;
            Events = new ReadOnlyCollection<BattleEvent>(
                events.OrderBy(item => item.Tick)
                    .ThenBy(item => item.Sequence)
                    .ToArray());
            EndCheckpoint = endCheckpoint;
            IsTerminal = isTerminal;
            StopReason = stopReason;
            Outcome = outcome;
            HomeLifeDamage = homeLifeDamage;
            AwayLifeDamage = awayLifeDamage;
            FinalSecondSha256 = finalSecondSha256;
        }

        public string SchemaVersion { get; }
        public string BattleId { get; }
        public string SealedInputHash { get; }
        public int ChunkIndex { get; }
        public int PreviousCompletedTick { get; }
        public int CompletedTick { get; }
        public int AuthoritativeTickCount { get; }
        public IReadOnlyList<BattleEvent> Events { get; }
        public BattlePresentationCheckpoint EndCheckpoint { get; }
        public bool IsTerminal { get; }
        public BattleStopReason? StopReason { get; }
        public BattleOutcome? Outcome { get; }
        public int? HomeLifeDamage { get; }
        public int? AwayLifeDamage { get; }
        public string FinalSecondSha256 { get; }
    }

    [DataContract]
    public sealed class BattleResolution
    {
        internal BattleResolution(
            string battleId,
            string sealedInputHash,
            int homeLifeDamage,
            int awayLifeDamage,
            int endTick,
            BattleStopReason terminalReason)
        {
            BattleId = battleId;
            SealedInputHash = sealedInputHash;
            HomeLifeDamage = homeLifeDamage;
            AwayLifeDamage = awayLifeDamage;
            Outcome = BattleOutcomeResolver.FromLifeDamage(
                homeLifeDamage,
                awayLifeDamage);
            EndTick = endTick;
            TerminalReason = terminalReason;
        }

        [DataMember(Name = "battleId", Order = 1)]
        public string BattleId { get; private set; }
        [DataMember(Name = "sealedInputHash", Order = 2)]
        public string SealedInputHash { get; private set; }
        [DataMember(Name = "homeLifeDamage", Order = 3)]
        public int HomeLifeDamage { get; private set; }
        [DataMember(Name = "awayLifeDamage", Order = 4)]
        public int AwayLifeDamage { get; private set; }
        [DataMember(Name = "outcome", Order = 5)]
        public BattleOutcome Outcome { get; private set; }
        [DataMember(Name = "endTick", Order = 6)]
        public int EndTick { get; private set; }
        [DataMember(Name = "terminalReason", Order = 7)]
        public BattleStopReason TerminalReason { get; private set; }
    }

    public sealed class BattleSimulationProducer
    {
        public const int AuthoritativeTicksPerFullChunk = 100;
        public const string ChunkSchemaVersion = "battle-simulation-chunk-v1";

        private readonly BattleRunner runner;
        private readonly List<BattleSimulationChunk> chunks =
            new List<BattleSimulationChunk>();
        private readonly ReadOnlyCollection<BattleSimulationChunk>
            readOnlyChunks;
        private int eventCursor;
        private int producedThroughTick;
        private BattleRunResult terminalResult;
        private BattleResolution resolution;
        private FinalSecondHashPayload finalSecondPayload;

        public BattleSimulationProducer(
            BattleInput input,
            string sealedInputHash)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));
            if (!BattleInputSha256.IsCanonicalHash(sealedInputHash))
                throw new ArgumentException(
                    "The sealed input hash must be an uppercase 64-character SHA-256 value.",
                    nameof(sealedInputHash));
            Input = input;
            SealedInputHash = sealedInputHash;
            runner = new BattleRunner(input);
            readOnlyChunks = new ReadOnlyCollection<BattleSimulationChunk>(
                chunks);
        }

        public BattleSimulationProducer(BattleInput input)
            : this(input, BattleInputSha256.Compute(input))
        {
        }

        public BattleInput Input { get; }
        public string SealedInputHash { get; }
        public IReadOnlyList<BattleSimulationChunk> Chunks =>
            readOnlyChunks;
        public int ComputedThroughTick => runner.CurrentTick;
        public int ProducedThroughTick => producedThroughTick;
        public bool HasFirstChunk => chunks.Count != 0;
        public bool IsTerminal =>
            runner.Status == BattleRunnerStatus.Stopped;

        public IReadOnlyList<BattleSimulationChunk> Advance(
            int authoritativeTickBudget)
        {
            if (authoritativeTickBudget < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(authoritativeTickBudget));
            if (authoritativeTickBudget == 0 || IsTerminal)
                return Array.Empty<BattleSimulationChunk>();

            var published = new List<BattleSimulationChunk>();
            for (var consumed = 0;
                 consumed < authoritativeTickBudget && !IsTerminal;
                 consumed++)
            {
                runner.Step();
                if (IsTerminal
                    || runner.CurrentTick - producedThroughTick
                        == AuthoritativeTicksPerFullChunk)
                    published.Add(PublishChunk());
            }
            return new ReadOnlyCollection<BattleSimulationChunk>(
                published.ToArray());
        }

        public BattleRunResult GetTerminalResult()
        {
            if (!IsTerminal)
                throw new InvalidOperationException(
                    "The producer has not reached a terminal state.");
            return terminalResult ?? (terminalResult =
                runner.CreateTerminalResult());
        }

        public BattleResolution GetBattleResolution()
        {
            if (resolution != null)
                return resolution;
            var result = GetTerminalResult();
            resolution = new BattleResolution(
                result.BattleId,
                SealedInputHash,
                result.HomeLifeDamage,
                result.AwayLifeDamage,
                result.CompletedTicks,
                result.StopReason);
            return resolution;
        }

        public FinalSecondHashPayload GetFinalSecondHashPayload()
        {
            if (finalSecondPayload != null)
                return finalSecondPayload;
            var result = GetTerminalResult();
            finalSecondPayload = new FinalSecondHashPayload(
                result.BattleId,
                SealedInputHash,
                result.CompletedTicks,
                BattleFinalSecondHasher.Compute(result));
            return finalSecondPayload;
        }

        public BattleRecoveryDescriptor CreateRecoveryDescriptor()
        {
            return new BattleRecoveryDescriptor(
                Input.BattleId,
                SealedInputHash,
                producedThroughTick,
                chunks.Count == 0 ? -1 : chunks[0].ChunkIndex,
                chunks.Count == 0 ? -1 : chunks[chunks.Count - 1].ChunkIndex,
                IsTerminal,
                producedThroughTick);
        }

        private BattleSimulationChunk PublishChunk()
        {
            var isTerminal = IsTerminal;
            if (isTerminal)
                terminalResult = runner.CreateTerminalResult();
            var immutableEvents = new ReadOnlyCollection<BattleEvent>(
                runner.Events.Skip(eventCursor).ToArray());
            eventCursor = runner.Events.Count;
            var checkpoint = runner.CreatePresentationCheckpoint();
            var completedTick = runner.CurrentTick;
            var chunk = new BattleSimulationChunk(
                Input.BattleId,
                SealedInputHash,
                chunks.Count,
                producedThroughTick,
                completedTick,
                immutableEvents,
                checkpoint,
                isTerminal,
                isTerminal
                    ? (BattleStopReason?)terminalResult.StopReason
                    : null,
                isTerminal
                    ? (BattleOutcome?)terminalResult.Outcome
                    : null,
                isTerminal
                    ? (int?)terminalResult.HomeLifeDamage
                    : null,
                isTerminal
                    ? (int?)terminalResult.AwayLifeDamage
                    : null,
                isTerminal
                    ? BattleFinalSecondHasher.Compute(terminalResult)
                    : null);
            chunks.Add(chunk);
            producedThroughTick = completedTick;
            return chunk;
        }
    }

    public static class BattleFinalSecondHasher
    {
        public const string SchemaVersion =
            "battle-final-second-sha256-v1";

        public static string Compute(BattleRunResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            if (result.FinalCheckpoint == null)
                throw new InvalidOperationException(
                    "A terminal presentation checkpoint is required.");

            using (var stream = new MemoryStream())
            {
                var writer = new CanonicalWriter(stream);
                writer.WriteString(SchemaVersion);
                writer.WriteString(result.BattleId);
                writer.WriteString(BattleInputSha256
                    .ComputeCanonicalSummary(
                        result.InputCanonicalSummary));
                var startTick = Math.Max(
                    1,
                    result.CompletedTicks - 19);
                var events = result.Events
                    .Where(item =>
                        item.Tick >= startTick
                        && item.Tick <= result.CompletedTicks)
                    .OrderBy(item => item.Tick)
                    .ThenBy(item => item.Sequence)
                    .ToArray();
                writer.WriteInt32(events.Length);
                foreach (var item in events)
                    WriteEvent(writer, item);

                var units = result.FinalCheckpoint.Units
                    .OrderBy(item => item.UnitId, StringComparer.Ordinal)
                    .ToArray();
                writer.WriteInt32(units.Length);
                foreach (var item in units)
                    WriteUnit(writer, item);

                writer.WriteInt32(result.CompletedTicks);
                writer.WriteInt32((int)result.StopReason);
                writer.WriteInt32(result.HomeLifeDamage);
                writer.WriteInt32(result.AwayLifeDamage);
                writer.WriteInt32((int)result.Outcome);
                using (var sha256 = SHA256.Create())
                    return BattleInputSha256.Hex(
                        sha256.ComputeHash(stream.ToArray()));
            }
        }

        private static void WriteEvent(
            CanonicalWriter writer,
            BattleEvent item)
        {
            writer.WriteInt32((int)item.Type);
            writer.WriteInt32(item.Tick);
            writer.WriteInt32(item.Sequence);
            writer.WriteString(item.UnitId);
            writer.WriteString(item.UnitTypeId);
            writer.WriteNullableInt32(
                item.UnitSide.HasValue
                    ? (int?)item.UnitSide.Value
                    : null);
            writer.WriteString(item.RelatedUnitId);
            writer.WritePosition(item.FromPosition);
            writer.WritePosition(item.ToPosition);
            writer.WriteNullableInt32(
                item.DamageType.HasValue
                    ? (int?)item.DamageType.Value
                    : null);
            writer.WriteInt32(item.DamageAmount);
            writer.WriteInt32(item.HitPointsBefore);
            writer.WriteInt32(item.HitPointsAfter);
            writer.WriteInt32(item.PlannedDamageTick);
            writer.WriteInt32(item.OriginalAnimationTicks);
            writer.WriteInt32(item.EffectiveAnimationTicks);
            writer.WriteString(item.AnimationKey);
            writer.WriteNullableInt32(
                item.Winner.HasValue
                    ? (int?)item.Winner.Value
                    : null);
            writer.WriteInt32((int)item.Reason);
            WriteSpawn(writer, item.SpawnSnapshot);
        }

        private static void WriteSpawn(
            CanonicalWriter writer,
            BattleUnitInstanceSnapshot item)
        {
            writer.WriteBoolean(item != null);
            if (item == null)
                return;
            writer.WriteString(item.UnitId);
            writer.WriteString(item.TypeId);
            writer.WriteString(item.PlayerId);
            writer.WriteInt32((int)item.Side);
            writer.WriteBoolean(item.IsDynamicallyGenerated);
            writer.WritePosition(item.Position);
            writer.WriteInt32(item.EliteLevel);
            writer.WriteInt32(item.MaxHitPoints);
            writer.WriteInt32(item.CurrentHitPoints);
            writer.WriteInt32(item.CurrentShield);
            writer.WriteInt32(item.Attack);
            writer.WriteInt32(item.Defense);
            writer.WriteInt32(item.MagicResistance);
            writer.WriteInt32(item.MoveSpeedCentimetresPerSecond);
            writer.WriteInt32(item.AttackIntervalTicks);
            writer.WriteInt32(item.AttackAnimationDurationTicks);
            writer.WriteInt32((int)item.DamageType);
            writer.WriteInt32((int)item.AttackMethod);
            writer.WriteInt32(item.BlockCapacity);
            writer.WriteInt32(item.TauntLevel);
            writer.WriteInt32(item.ActivationTick);
            writer.WriteInt32(item.LifeDeduct);
            writer.WriteInt32(item.Buffs.Count);
            foreach (var buff in item.Buffs
                         .OrderBy(value => value.Id, StringComparer.Ordinal)
                         .ThenBy(
                             value => value.RawPayload,
                             StringComparer.Ordinal))
            {
                writer.WriteString(buff.Id);
                writer.WriteString(buff.RawPayload);
            }
        }

        private static void WriteUnit(
            CanonicalWriter writer,
            BattleUnitCheckpoint item)
        {
            writer.WriteString(item.UnitId);
            writer.WriteString(item.TypeId);
            writer.WriteString(item.PlayerId);
            writer.WriteInt32((int)item.Side);
            writer.WriteBoolean(item.IsDynamicallyGenerated);
            writer.WriteBoolean(item.IsActivated);
            writer.WriteBoolean(item.IsAlive);
            writer.WriteBoolean(item.HasReachedGate);
            writer.WritePosition(item.Position);
            writer.WriteInt32(item.EliteLevel);
            writer.WriteInt32(item.MaxHitPoints);
            writer.WriteInt32(item.CurrentHitPoints);
            writer.WriteInt32(item.CurrentShield);
            writer.WriteString(item.TargetUnitId);
            writer.WriteInt32(item.BlockedUnitIds.Count);
            foreach (var blocked in item.BlockedUnitIds
                         .OrderBy(value => value, StringComparer.Ordinal))
                writer.WriteString(blocked);
            writer.WriteInt32((int)item.Action);
            writer.WriteInt32(item.ActionStartTick);
            writer.WriteInt32(item.ActionSequence);
            writer.WriteInt32(item.ActionDurationTicks);
            writer.WriteString(item.AnimationKey);
            writer.WriteString(item.PresentationStateTag);
            writer.WriteInt32(item.SpawnTick);
            writer.WriteInt32(item.ActivationTick);
            writer.WriteNullableInt32(item.DeathTick);
            writer.WriteInt32(item.Buffs.Count);
            foreach (var buff in item.Buffs)
            {
                writer.WriteString(buff.Id);
                writer.WriteString(buff.RawPayload);
            }
            writer.WriteInt32(item.ExecutionStateFields.Count);
            foreach (var field in item.ExecutionStateFields)
                writer.WriteString(field);
        }

        private sealed class CanonicalWriter
        {
            private readonly Stream stream;

            internal CanonicalWriter(Stream stream)
            {
                this.stream = stream;
            }

            internal void WriteBoolean(bool value)
            {
                stream.WriteByte(value ? (byte)1 : (byte)0);
            }

            internal void WriteInt32(int value)
            {
                unchecked
                {
                    stream.WriteByte((byte)((uint)value >> 24));
                    stream.WriteByte((byte)((uint)value >> 16));
                    stream.WriteByte((byte)((uint)value >> 8));
                    stream.WriteByte((byte)value);
                }
            }

            internal void WriteNullableInt32(int? value)
            {
                WriteBoolean(value.HasValue);
                if (value.HasValue)
                    WriteInt32(value.Value);
            }

            internal void WriteString(string value)
            {
                if (value == null)
                {
                    WriteInt32(-1);
                    return;
                }
                var bytes = Encoding.UTF8.GetBytes(value);
                WriteInt32(bytes.Length);
                stream.Write(bytes, 0, bytes.Length);
            }

            internal void WritePosition(FixedPosition value)
            {
                WriteInt32(value.XUnits);
                WriteInt32(value.YUnits);
            }

            internal void WritePosition(FixedPosition? value)
            {
                WriteBoolean(value.HasValue);
                if (value.HasValue)
                    WritePosition(value.Value);
            }
        }
    }
}
