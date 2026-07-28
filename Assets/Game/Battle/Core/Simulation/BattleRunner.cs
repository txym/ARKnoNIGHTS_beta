using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace ArknoNights.Battle.Core
{
    public enum BattleRunnerStatus { Ready, Running, Stopped }
    public enum BattleStopReason { None, Victory, MutualAnnihilation, MaxTicksReached, UnsupportedBlockingContention }

    public sealed class RuntimeAbilityState
    {
        private int currentSkillPoints;
        private int castCount;

        internal RuntimeAbilityState(AbilityDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            currentSkillPoints = definition.InitialSkillPoints;
        }

        public AbilityDefinition Definition { get; }

        internal void RecoverAutomaticSkillPoint()
        {
            if (currentSkillPoints < Definition.RequiredSkillPoints)
                currentSkillPoints++;
        }

        internal bool CanCast => currentSkillPoints >= Definition.RequiredSkillPoints;

        internal int ConsumeCast()
        {
            if (!CanCast) throw new InvalidOperationException("Ability is not ready to cast: " + Definition.AbilityId);
            currentSkillPoints -= Definition.RequiredSkillPoints;
            castCount++;
            return castCount;
        }

        internal void AppendStableSummary(StringBuilder builder)
        {
            builder.Append(',').Append(Definition.AbilityId).Append(':').Append(currentSkillPoints).Append(':').Append(castCount);
        }
    }

    public sealed class RuntimeUnitState
    {
        private readonly List<string> blockedUnitIds = new List<string>();
        private readonly List<RuntimeAbilityState> abilityStates;

        internal RuntimeUnitState(
            string unitId,
            string playerId,
            BattleSide side,
            UnitDefinition definition,
            UnitSnapshot source,
            BattlefieldCoordinate coordinate)
            : this(unitId, playerId, side, definition, source, coordinate, Enumerable.Empty<RuntimeAbilityState>())
        {
        }

        internal RuntimeUnitState(
            string unitId,
            string playerId,
            BattleSide side,
            UnitDefinition definition,
            UnitSnapshot source,
            BattlefieldCoordinate coordinate,
            IEnumerable<RuntimeAbilityState> abilities)
            : this(
                unitId,
                playerId,
                side,
                definition,
                FixedPosition.FromCell(coordinate),
                source.EliteLevel,
                source.Buffs,
                0,
                abilities)
        {
        }

        internal RuntimeUnitState(
            string unitId,
            string playerId,
            BattleSide side,
            UnitDefinition definition,
            FixedPosition position,
            int eliteLevel,
            IEnumerable<BuffPlaceholder> buffs,
            int activationTick,
            IEnumerable<RuntimeAbilityState> abilities)
        {
            UnitId = unitId;
            PlayerId = playerId;
            Side = side;
            TypeId = definition.TypeId;
            CurrentHitPoints = definition.MaxHitPoints;
            Position = position;
            Definition = definition;
            EliteLevel = eliteLevel;
            Buffs = new ReadOnlyCollection<BuffPlaceholder>((buffs ?? Enumerable.Empty<BuffPlaceholder>()).ToArray());
            ActivationTick = activationTick;
            abilityStates = (abilities ?? Enumerable.Empty<RuntimeAbilityState>()).OrderBy(item => item.Definition.AbilityId, StringComparer.Ordinal).ToList();
            AbilityStates = new ReadOnlyCollection<RuntimeAbilityState>(abilityStates);
            BlockedUnitIds = new ReadOnlyCollection<string>(blockedUnitIds);
        }

        public string UnitId { get; }
        public string PlayerId { get; }
        public BattleSide Side { get; }
        public string TypeId { get; }
        public int EliteLevel { get; }
        public IReadOnlyList<BuffPlaceholder> Buffs { get; }
        public int ActivationTick { get; }
        public int CurrentHitPoints { get; internal set; }
        public FixedPosition Position { get; internal set; }
        public bool IsAlive { get; internal set; } = true;
        public string TargetUnitId { get; internal set; }
        // Retained for callers that only need the first stable relation. Use BlockedUnitIds for capacity-aware state.
        public string BlockedUnitId => blockedUnitIds.Count == 0 ? null : blockedUnitIds[0];
        public IReadOnlyList<string> BlockedUnitIds { get; }
        public int NextAttackAllowedTick { get; internal set; }
        internal int AttackAnimationLockUntilTick { get; set; } = int.MinValue;
        internal int SkillAnimationLockUntilTick { get; set; } = int.MinValue;
        internal int MoveRemainder { get; set; }
        internal int MoveXNumeratorRemainder { get; set; }
        internal int MoveYNumeratorRemainder { get; set; }
        internal UnitDefinition Definition { get; }
        internal IReadOnlyList<RuntimeAbilityState> AbilityStates { get; }
        internal bool IsTargetable => abilityStates.All(item =>
            item.Definition.UnitTraitEffect == null
            || item.Definition.UnitTraitEffect.Kind != UnitTraitEffectKind.Untargetable);
        internal bool HasBlockingCapacity => blockedUnitIds.Count < Definition.BlockCapacity;
        internal bool IsBlocked => blockedUnitIds.Count != 0;
        internal bool HasBlockWith(string unitId) => blockedUnitIds.Contains(unitId);
        internal void AddBlock(string unitId)
        {
            if (HasBlockWith(unitId)) return;
            blockedUnitIds.Add(unitId);
            blockedUnitIds.Sort(StringComparer.Ordinal);
        }
        internal bool RemoveBlock(string unitId) => blockedUnitIds.Remove(unitId);
    }

    public readonly struct BattleStepTrace : IEquatable<BattleStepTrace>
    {
        public BattleStepTrace(int tick, BattleRunnerStatus status, BattleStopReason stopReason) { Tick = tick; Status = status; StopReason = stopReason; }
        public int Tick { get; }
        public BattleRunnerStatus Status { get; }
        public BattleStopReason StopReason { get; }
        public bool Equals(BattleStepTrace other) => Tick == other.Tick && Status == other.Status && StopReason == other.StopReason;
        public override bool Equals(object obj) => obj is BattleStepTrace other && Equals(other);
        public override int GetHashCode() => (Tick * 397) ^ ((int)Status * 17) ^ (int)StopReason;
        public override string ToString() => Tick + ":" + Status + ":" + StopReason;
    }

    public sealed class BattleUnitFinalState
    {
        internal BattleUnitFinalState(RuntimeUnitState state)
        {
            UnitId = state.UnitId;
            TypeId = state.TypeId;
            Side = state.Side;
            Position = state.Position;
            HitPoints = state.CurrentHitPoints;
            IsAlive = state.IsAlive;
        }

        public string UnitId { get; }
        public string TypeId { get; }
        public BattleSide Side { get; }
        public FixedPosition Position { get; }
        public int HitPoints { get; }
        public bool IsAlive { get; }
    }

    public sealed class BattleRunResult
    {
        internal BattleRunResult(string battleId, string homePlayerId, string awayPlayerId, string inputCanonicalSummary, IReadOnlyList<string> knownUnitTypeIds, int completedTicks, BattleStopReason stopReason, BattleSide? winner, IReadOnlyList<BattleStepTrace> trace, IReadOnlyList<BattleEvent> events, IReadOnlyList<BattleUnitFinalState> finalUnits, string stableSummary)
            : this(
                battleId,
                homePlayerId,
                awayPlayerId,
                inputCanonicalSummary,
                knownUnitTypeIds,
                completedTicks,
                stopReason,
                winner,
                trace,
                events,
                finalUnits,
                new ReadOnlyDictionary<string, BattleUnitInstanceSnapshot>(
                    new Dictionary<string, BattleUnitInstanceSnapshot>(StringComparer.Ordinal)),
                stableSummary)
        {
        }

        internal BattleRunResult(string battleId, string homePlayerId, string awayPlayerId, string inputCanonicalSummary, IReadOnlyList<string> knownUnitTypeIds, int completedTicks, BattleStopReason stopReason, BattleSide? winner, IReadOnlyList<BattleStepTrace> trace, IReadOnlyList<BattleEvent> events, IReadOnlyList<BattleUnitFinalState> finalUnits, IReadOnlyDictionary<string, BattleUnitInstanceSnapshot> unitSnapshots, string stableSummary)
        {
            BattleId = battleId;
            HomePlayerId = homePlayerId;
            AwayPlayerId = awayPlayerId;
            InputCanonicalSummary = inputCanonicalSummary;
            KnownUnitTypeIds = knownUnitTypeIds;
            CompletedTicks = completedTicks;
            StopReason = stopReason;
            Winner = winner;
            Trace = trace;
            Events = events;
            FinalUnits = finalUnits;
            UnitSnapshots = unitSnapshots;
            StableSummary = stableSummary;
        }

        public int CompletedTicks { get; }
        public string BattleId { get; }
        public string HomePlayerId { get; }
        public string AwayPlayerId { get; }
        public string InputCanonicalSummary { get; }
        public IReadOnlyList<string> KnownUnitTypeIds { get; }
        public BattleStopReason StopReason { get; }
        public BattleSide? Winner { get; }
        public bool IsResolved => Winner.HasValue;
        public IReadOnlyList<BattleStepTrace> Trace { get; }
        public IReadOnlyList<BattleEvent> Events { get; }
        public IReadOnlyList<BattleUnitFinalState> FinalUnits { get; }
        public IReadOnlyDictionary<string, BattleUnitInstanceSnapshot> UnitSnapshots { get; }
        public string StableSummary { get; }

        public bool TryGetUnitSnapshot(string unitId, out BattleUnitInstanceSnapshot snapshot)
        {
            return UnitSnapshots.TryGetValue(unitId, out snapshot);
        }
    }

    /// <summary>Explicit, deterministic tick driver. TASK-003 will add combat stages inside RunAuthoritativeTick.</summary>
    public sealed class BattleRunner
    {
        private const int AutomaticSkillPointGainIntervalTicks = BattleInput.TicksPerSecond / BattleInput.AutomaticSkillPointsPerSecond;
        private readonly List<RuntimeUnitState> runtimeUnits;
        private readonly List<BattleStepTrace> trace = new List<BattleStepTrace>();
        private readonly List<BattleEvent> events = new List<BattleEvent>();
        private readonly List<PendingAttack> pendingAttacks = new List<PendingAttack>();
        private readonly Dictionary<string, UnitDefinition> unitDefinitions;
        private readonly Dictionary<string, AbilityDefinition> abilityDefinitions;
        private readonly Dictionary<string, BattleUnitInstanceSnapshot> unitSnapshots = new Dictionary<string, BattleUnitInstanceSnapshot>(StringComparer.Ordinal);
        private readonly DynamicUnitIdAllocator dynamicUnitIdAllocator = new DynamicUnitIdAllocator();
        private int eventTick = int.MinValue;
        private int eventSequence;

        public BattleRunner(BattleInput input)
        {
            Input = input ?? throw new ArgumentNullException(nameof(input));
            if (BattleInput.TicksPerSecond % BattleInput.AutomaticSkillPointsPerSecond != 0)
                throw new InvalidOperationException("Automatic skill-point cadence must divide the authoritative tick rate evenly.");
            unitDefinitions = input.UnitDefinitions.ToDictionary(item => item.TypeId, StringComparer.Ordinal);
            abilityDefinitions = input.AbilityDefinitions.ToDictionary(item => item.AbilityId, StringComparer.Ordinal);
            runtimeUnits = BuildInitialUnits(input, unitDefinitions, abilityDefinitions);
            RuntimeUnits = new ReadOnlyCollection<RuntimeUnitState>(runtimeUnits);
            Events = new ReadOnlyCollection<BattleEvent>(events);
            Status = BattleRunnerStatus.Ready;
            foreach (var unit in runtimeUnits)
            {
                var snapshot = CreateSpawnSnapshot(unit, false);
                unitSnapshots.Add(unit.UnitId, snapshot);
                EmitSpawn(unit, snapshot);
            }
        }

        public BattleInput Input { get; }
        public int CurrentTick { get; private set; }
        public BattleRunnerStatus Status { get; private set; }
        public BattleStopReason StopReason { get; private set; }
        public IReadOnlyList<RuntimeUnitState> RuntimeUnits { get; }
        public IReadOnlyList<BattleEvent> Events { get; }

        public BattleStepTrace Step()
        {
            if (Status == BattleRunnerStatus.Stopped) return new BattleStepTrace(CurrentTick, Status, StopReason);
            Status = BattleRunnerStatus.Running;
            CurrentTick++;
            RunAuthoritativeTick();
            if (Status != BattleRunnerStatus.Stopped && CurrentTick >= Input.MaxTicks)
            {
                EndBattle(BattleStopReason.MaxTicksReached, null);
            }

            var step = new BattleStepTrace(CurrentTick, Status, StopReason);
            trace.Add(step);
            return step;
        }

        public BattleRunResult RunToCompletion()
        {
            while (Status != BattleRunnerStatus.Stopped) Step();
            var immutableTrace = new ReadOnlyCollection<BattleStepTrace>(trace.ToArray());
            var finalUnits = new ReadOnlyCollection<BattleUnitFinalState>(runtimeUnits.OrderBy(item => item.UnitId, StringComparer.Ordinal).Select(item => new BattleUnitFinalState(item)).ToArray());
            var homePlayerId = Input.Players.Single(player => player.Side == BattleSide.Home).PlayerId;
            var awayPlayerId = Input.Players.Single(player => player.Side == BattleSide.Away).PlayerId;
            var knownUnitTypeIds = new ReadOnlyCollection<string>(Input.UnitDefinitions
                .Select(definition => definition.TypeId)
                .OrderBy(typeId => typeId, StringComparer.Ordinal)
                .ToArray());
            var immutableUnitSnapshots = new ReadOnlyDictionary<string, BattleUnitInstanceSnapshot>(
                unitSnapshots.OrderBy(item => item.Key, StringComparer.Ordinal)
                    .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
            return new BattleRunResult(Input.BattleId, homePlayerId, awayPlayerId, Input.CanonicalSummary, knownUnitTypeIds,
                CurrentTick, StopReason, Winner, immutableTrace, new ReadOnlyCollection<BattleEvent>(events.ToArray()), finalUnits,
                immutableUnitSnapshots, BuildStableSummary());
        }

        public BattleSide? Winner { get; private set; }

        private void RunAuthoritativeTick()
        {
            RemoveInvalidPendingAttacks();
            AcquireTargets();
            ApplyMovement();
            EvaluateBlocking();
            CastReadyAbilities();
            StartAttacks();
            ResolveDueDamage();
            ResolveDeathsAndCleanup();
            EvaluateBattleEnd();
            if (Status == BattleRunnerStatus.Stopped) return;
            RecoverAutomaticSkillPointsAndCast();
        }

        private void RemoveInvalidPendingAttacks()
        {
            // A dead target cancels damage but not the already-started attack animation lock.
            pendingAttacks.RemoveAll(item => !FindUnit(item.AttackerUnitId).IsAlive);
        }

        private void AcquireTargets()
        {
            foreach (var unit in runtimeUnits.Where(IsActive).OrderBy(item => item.UnitId, StringComparer.Ordinal))
            {
                if (!unit.Definition.CanAttack)
                {
                    SetTarget(unit, null);
                    continue;
                }
                if (HasLiveTarget(unit)) continue;
                var selected = runtimeUnits.Where(candidate => IsActive(candidate) && candidate.IsTargetable && candidate.Side != unit.Side)
                    .OrderBy(candidate => DistanceSquared(unit.Position, candidate.Position))
                    .ThenBy(candidate => DistanceSquared(candidate.Position, GatePosition(unit.Side)))
                    .ThenBy(candidate => candidate.UnitId, StringComparer.Ordinal).FirstOrDefault();
                SetTarget(unit, selected == null ? null : selected.UnitId);
            }
        }

        private void ApplyMovement()
        {
            var intents = new List<MoveIntent>();
            foreach (var unit in runtimeUnits.Where(item => IsActive(item) && item.Definition.ActionMethod != 4 && !item.IsBlocked && !IsSkillAnimationLocked(item) && !HasTargetDeathAnimationLock(item)).OrderBy(item => item.UnitId, StringComparer.Ordinal))
            {
                if (!unit.Definition.CanAttack)
                {
                    var gate = OpposingGatePosition(unit.Side);
                    var nextGatePosition = MoveTowards(unit, gate, 0);
                    if (!nextGatePosition.Equals(unit.Position))
                        intents.Add(new MoveIntent(unit, unit.Position, nextGatePosition, null));
                    continue;
                }

                if (!HasLiveTarget(unit)) continue;
                var target = FindUnit(unit.TargetUnitId);
                if (IsInAttackRange(unit.Position, target.Position)) continue;
                var next = MoveTowards(unit, target);
                if (!next.Equals(unit.Position)) intents.Add(new MoveIntent(unit, unit.Position, next, unit.TargetUnitId));
            }
            foreach (var intent in intents)
            {
                intent.Unit.Position = intent.To;
                Emit(BattleEventType.Move, intent.Unit.UnitId, null, intent.RelatedUnitId, intent.From, intent.To, null, 0, 0, 0, 0, 0, 0, null, BattleStopReason.None);
            }
        }

        private void EvaluateBlocking()
        {
            foreach (var unit in runtimeUnits.Where(item => item.IsBlocked).OrderBy(item => item.UnitId, StringComparer.Ordinal).ToArray())
            {
                foreach (var otherId in unit.BlockedUnitIds.ToArray())
                {
                    var other = FindUnit(otherId);
                    if (!unit.IsAlive || other == null || !other.IsAlive || DistanceSquared(unit.Position, other.Position) >= FixedPosition.QuarterMetre * FixedPosition.QuarterMetre) EndBlock(unit, other);
                }
            }

            var proposals = runtimeUnits.Where(item => IsActive(item) && item.HasBlockingCapacity && HasLiveTarget(item))
                .Select(item => new BlockProposal(item, FindUnit(item.TargetUnitId)))
                .Where(item => item.Target != null && IsActive(item.Target) && item.Target.HasBlockingCapacity && DistanceSquared(item.Actor.Position, item.Target.Position) < FixedPosition.QuarterMetre * FixedPosition.QuarterMetre)
                .ToArray();

            foreach (var group in proposals.GroupBy(item => item.Target.UnitId, StringComparer.Ordinal).OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                // A target chooses incoming blockers: own target first, then taunt, then the candidate nearest its own gate, then ID.
                foreach (var proposal in group
                    .OrderBy(item => string.Equals(group.First().Target.TargetUnitId, item.Actor.UnitId, StringComparison.Ordinal) ? 0 : 1)
                    .ThenByDescending(item => item.Actor.Definition.TauntLevel)
                    .ThenBy(item => DistanceSquared(item.Actor.Position, GatePosition(item.Target.Side)))
                    .ThenBy(item => item.Actor.UnitId, StringComparer.Ordinal))
                {
                    if (!proposal.Actor.HasBlockingCapacity || !proposal.Target.HasBlockingCapacity || proposal.Actor.HasBlockWith(proposal.Target.UnitId)) continue;
                    StartBlock(proposal.Actor, proposal.Target);
                }
            }
        }

        private void StartAttacks()
        {
            foreach (var unit in runtimeUnits.Where(item => IsActive(item) && item.Definition.CanAttack && !IsAttackAnimationLocked(item) && !IsSkillAnimationLocked(item) && item.NextAttackAllowedTick <= CurrentTick).OrderBy(item => item.UnitId, StringComparer.Ordinal))
            {
                var target = GetAttackTarget(unit);
                if (target == null) continue;
                if (DistanceSquared(unit.Position, target.Position) >= FixedPosition.QuarterMetre * FixedPosition.QuarterMetre) continue;
                var effectiveTicks = Math.Min(unit.Definition.AttackAnimationDurationTicks, unit.Definition.AttackIntervalTicks);
                var damageTick = CurrentTick + effectiveTicks;
                unit.NextAttackAllowedTick = CurrentTick + unit.Definition.AttackIntervalTicks;
                unit.AttackAnimationLockUntilTick = Math.Max(unit.AttackAnimationLockUntilTick, damageTick);
                pendingAttacks.Add(new PendingAttack(unit.UnitId, target.UnitId, damageTick, unit.Definition.DamageType, unit.Definition.Attack, unit.Definition.AttackAnimationDurationTicks, effectiveTicks));
                Emit(BattleEventType.Attack, unit.UnitId, null, target.UnitId, null, null, unit.Definition.DamageType, 0, 0, 0, damageTick, unit.Definition.AttackAnimationDurationTicks, effectiveTicks, null, BattleStopReason.None);
            }
        }

        private void ResolveDueDamage()
        {
            var due = pendingAttacks.Where(item => item.DamageTick == CurrentTick).OrderBy(item => item.TargetUnitId, StringComparer.Ordinal).ThenBy(item => item.AttackerUnitId, StringComparer.Ordinal).ToArray();
            pendingAttacks.RemoveAll(item => item.DamageTick == CurrentTick);
            var valid = due.Where(item => FindUnit(item.AttackerUnitId).IsAlive && FindUnit(item.TargetUnitId).IsAlive).ToArray();
            foreach (var targetGroup in valid.GroupBy(item => item.TargetUnitId, StringComparer.Ordinal))
            {
                var target = FindUnit(targetGroup.Key);
                var hits = targetGroup.Select(item => new DamageHit(item, CalculateDamage(item, target))).ToArray();
                var before = target.CurrentHitPoints;
                var total = hits.Sum(item => item.Amount);
                target.CurrentHitPoints = Math.Max(0, before - total);
                foreach (var hit in hits) Emit(BattleEventType.Damage, hit.Attack.AttackerUnitId, null, hit.Attack.TargetUnitId, null, null, hit.Attack.DamageType, hit.Amount, before, target.CurrentHitPoints, 0, 0, 0, null, BattleStopReason.None);
            }
        }

        private void ResolveDeathsAndCleanup()
        {
            foreach (var unit in runtimeUnits.Where(item => item.IsAlive && item.CurrentHitPoints <= 0).OrderBy(item => item.UnitId, StringComparer.Ordinal).ToArray())
            {
                unit.IsAlive = false;
                Emit(BattleEventType.Death, unit.UnitId, null, null, null, null, null, 0, 0, 0, 0, 0, 0, null, BattleStopReason.None);
            }
            foreach (var unit in runtimeUnits.Where(item => !item.IsAlive).OrderBy(item => item.UnitId, StringComparer.Ordinal))
            {
                foreach (var otherId in unit.BlockedUnitIds.ToArray()) EndBlock(unit, FindUnit(otherId));
                foreach (var other in runtimeUnits.Where(item => item.IsAlive && item.TargetUnitId == unit.UnitId).OrderBy(item => item.UnitId, StringComparer.Ordinal)) SetTarget(other, null);
            }
            pendingAttacks.RemoveAll(item => !FindUnit(item.AttackerUnitId).IsAlive);
        }

        private void EvaluateBattleEnd()
        {
            var homeAlive = runtimeUnits.Any(item => item.IsAlive && item.Side == BattleSide.Home);
            var awayAlive = runtimeUnits.Any(item => item.IsAlive && item.Side == BattleSide.Away);
            if (homeAlive == awayAlive) { if (!homeAlive) EndBattle(BattleStopReason.MutualAnnihilation, null); return; }
            EndBattle(BattleStopReason.Victory, homeAlive ? BattleSide.Home : BattleSide.Away);
        }

        private void RecoverAutomaticSkillPointsAndCast()
        {
            if (CurrentTick % AutomaticSkillPointGainIntervalTicks == 0)
            {
                foreach (var caster in runtimeUnits.Where(IsActive).OrderBy(item => item.UnitId, StringComparer.Ordinal).ToArray())
                foreach (var abilityState in caster.AbilityStates.OrderBy(item => item.Definition.AbilityId, StringComparer.Ordinal))
                {
                    if (abilityState.Definition.ActivationKind != AbilityActivationKind.Timed) continue;
                    if (abilityState.Definition.SkillPointGeneration != SkillPointGeneration.Automatic) continue;
                    abilityState.RecoverAutomaticSkillPoint();
                }
            }

            CastReadyAbilities();
        }

        private void CastReadyAbilities()
        {
            foreach (var caster in runtimeUnits.Where(IsActive).OrderBy(item => item.UnitId, StringComparer.Ordinal).ToArray())
            foreach (var abilityState in caster.AbilityStates.OrderBy(item => item.Definition.AbilityId, StringComparer.Ordinal))
            {
                if (abilityState.Definition.ActivationKind != AbilityActivationKind.Timed) continue;
                if (abilityState.Definition.SkillPointGeneration != SkillPointGeneration.Automatic) continue;
                if (!abilityState.CanCast
                    || IsAttackAnimationLocked(caster)
                    || IsSkillAnimationLocked(caster))
                    continue;
                if (!caster.Definition.TryGetSkillAnimation(
                        abilityState.Definition.AnimationKey,
                        out var animation))
                    throw new InvalidOperationException(
                        "Validated skill animation is missing at runtime: "
                        + caster.TypeId
                        + "/"
                        + abilityState.Definition.AnimationKey);
                var castOrdinal = abilityState.ConsumeCast();
                caster.SkillAnimationLockUntilTick =
                    CurrentTick + animation.EffectiveDurationTicks;
                Emit(
                    BattleEventType.Skill,
                    caster.UnitId,
                    null,
                    null,
                    null,
                    null,
                    null,
                    0,
                    0,
                    0,
                    0,
                    animation.OriginalDurationTicks,
                    animation.EffectiveDurationTicks,
                    null,
                    BattleStopReason.None,
                    abilityState.Definition.AnimationKey);
                CastSummonAbility(caster, abilityState.Definition, castOrdinal);
            }
        }

        private void CastSummonAbility(RuntimeUnitState caster, AbilityDefinition ability, int castOrdinal)
        {
            var summonEffect = ability.SummonEffect;
            var summonDefinition = unitDefinitions[summonEffect.SummonTypeId];
            for (var spawnOrdinal = 1; spawnOrdinal <= summonEffect.Count; spawnOrdinal++)
            {
                var offsetX = StableSpawnOffset(
                    Input.BattleId,
                    caster.UnitId,
                    ability.AbilityId,
                    castOrdinal,
                    spawnOrdinal,
                    0,
                    summonEffect.SideLengthCentimetres);
                var offsetY = StableSpawnOffset(
                    Input.BattleId,
                    caster.UnitId,
                    ability.AbilityId,
                    castOrdinal,
                    spawnOrdinal,
                    1,
                    summonEffect.SideLengthCentimetres);
                var position = new FixedPosition(caster.Position.XUnits + offsetX, caster.Position.YUnits + offsetY);
                var summoned = new RuntimeUnitState(
                    dynamicUnitIdAllocator.Allocate(),
                    caster.PlayerId,
                    caster.Side,
                    summonDefinition,
                    position,
                    0,
                    Array.Empty<BuffPlaceholder>(),
                    CurrentTick + 1,
                    CreateAbilityStates(summonDefinition, abilityDefinitions));
                runtimeUnits.Add(summoned);
                var snapshot = CreateSpawnSnapshot(summoned, true);
                unitSnapshots.Add(summoned.UnitId, snapshot);
                EmitSpawn(summoned, snapshot);
            }
        }

        private static int StableSpawnOffset(
            string battleId,
            string casterId,
            string abilityId,
            int castOrdinal,
            int spawnOrdinal,
            int axis,
            int sideLengthCentimetres)
        {
            var hash = 14695981039346656037UL;
            hash = AppendStableHash(hash, battleId);
            hash = AppendStableHash(hash, casterId);
            hash = AppendStableHash(hash, abilityId);
            hash = AppendStableHash(hash, castOrdinal);
            hash = AppendStableHash(hash, spawnOrdinal);
            hash = AppendStableHash(hash, axis);
            var minimum = -(sideLengthCentimetres / 2);
            return minimum + (int)(hash % ((ulong)sideLengthCentimetres + 1UL));
        }

        private static ulong AppendStableHash(ulong hash, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            hash = AppendStableHash(hash, bytes.Length);
            foreach (var item in bytes)
            {
                hash ^= item;
                hash *= 1099511628211UL;
            }
            return hash;
        }

        private static ulong AppendStableHash(ulong hash, int value)
        {
            unchecked
            {
                for (var shift = 0; shift < 32; shift += 8)
                {
                    hash ^= (byte)((uint)value >> shift);
                    hash *= 1099511628211UL;
                }
            }
            return hash;
        }

        private void EndBattle(BattleStopReason reason, BattleSide? winner)
        {
            if (Status == BattleRunnerStatus.Stopped) return;
            Winner = winner;
            StopReason = reason;
            Status = BattleRunnerStatus.Stopped;
            Emit(BattleEventType.BattleEnded, null, null, null, null, null, null, 0, 0, 0, 0, 0, 0, winner, reason);
        }

        private void StartBlock(RuntimeUnitState actor, RuntimeUnitState target)
        {
            actor.AddBlock(target.UnitId);
            target.AddBlock(actor.UnitId);
            Emit(BattleEventType.BlockStarted, actor.UnitId, null, target.UnitId, null, null, null, 0, 0, 0, 0, 0, 0, null, BattleStopReason.None);
        }

        private void EndBlock(RuntimeUnitState unit, RuntimeUnitState other)
        {
            if (unit == null) return;
            if (other == null)
            {
                foreach (var orphanId in unit.BlockedUnitIds.Where(id => FindUnit(id) == null).ToArray()) unit.RemoveBlock(orphanId);
                return;
            }

            if (!unit.RemoveBlock(other.UnitId) && !other.HasBlockWith(unit.UnitId)) return;
            other.RemoveBlock(unit.UnitId);
            var first = string.CompareOrdinal(unit.UnitId, other.UnitId) <= 0 ? unit : other;
            var second = ReferenceEquals(first, unit) ? other : unit;
            Emit(BattleEventType.BlockEnded, first.UnitId, null, second.UnitId, null, null, null, 0, 0, 0, 0, 0, 0, null, BattleStopReason.None);
        }

        private void SetTarget(RuntimeUnitState unit, string targetUnitId)
        {
            if (string.Equals(unit.TargetUnitId, targetUnitId, StringComparison.Ordinal)) return;
            unit.TargetUnitId = targetUnitId;
            Emit(BattleEventType.TargetChanged, unit.UnitId, null, targetUnitId, null, null, null, 0, 0, 0, 0, 0, 0, null, BattleStopReason.None);
        }

        private bool IsActive(RuntimeUnitState unit) => unit.IsAlive && unit.ActivationTick <= CurrentTick;
        private bool HasLiveTarget(RuntimeUnitState unit) => unit.TargetUnitId != null && FindUnit(unit.TargetUnitId) != null && FindUnit(unit.TargetUnitId).IsAlive;
        private RuntimeUnitState GetAttackTarget(RuntimeUnitState unit)
        {
            var normalTarget = HasLiveTarget(unit) ? FindUnit(unit.TargetUnitId) : null;
            // A blocker only changes attack priority after the current target has left range.
            // It never rewrites the unit's normal acquisition target.
            if (normalTarget != null && IsInAttackRange(unit.Position, normalTarget.Position)) return normalTarget;
            if (unit.IsBlocked)
            {
                foreach (var blockedUnitId in unit.BlockedUnitIds)
                {
                    var blocker = FindUnit(blockedUnitId);
                    if (blocker != null && blocker.IsAlive) return blocker;
                }
            }
            return normalTarget;
        }
        private bool HasTargetDeathAnimationLock(RuntimeUnitState unit) => pendingAttacks.Any(item => string.Equals(item.AttackerUnitId, unit.UnitId, StringComparison.Ordinal) && item.DamageTick >= CurrentTick && !FindUnit(item.TargetUnitId).IsAlive);
        private RuntimeUnitState FindUnit(string unitId) => runtimeUnits.FirstOrDefault(item => string.Equals(item.UnitId, unitId, StringComparison.Ordinal));
        private bool IsAttackAnimationLocked(RuntimeUnitState unit) => CurrentTick <= unit.AttackAnimationLockUntilTick;
        private bool IsSkillAnimationLocked(RuntimeUnitState unit) => CurrentTick <= unit.SkillAnimationLockUntilTick;
        private static FixedPosition GatePosition(BattleSide side) => FixedPosition.FromCell(side == BattleSide.Home ? BattlefieldRules.BlueGate : BattlefieldRules.RedGate);
        private static FixedPosition OpposingGatePosition(BattleSide side) => FixedPosition.FromCell(side == BattleSide.Home ? BattlefieldRules.RedGate : BattlefieldRules.BlueGate);
        private static long DistanceSquared(FixedPosition first, FixedPosition second) { var x = (long)first.XUnits - second.XUnits; var y = (long)first.YUnits - second.YUnits; return x * x + y * y; }
        private static bool IsInAttackRange(FixedPosition first, FixedPosition second) => DistanceSquared(first, second) < FixedPosition.QuarterMetre * FixedPosition.QuarterMetre;
        private static int CalculateDamage(PendingAttack attack, RuntimeUnitState target)
        {
            return DamageCalculator.Calculate(attack.DamageType, attack.Attack, target.Definition.Defense, target.Definition.MagicResistance);
        }

        private static int IntegerSquareRootCeiling(long value)
        {
            if (value <= 0) return 0;
            long low = 0;
            long high = 1;
            while (high * high < value) high *= 2;
            while (low + 1 < high)
            {
                var middle = low + (high - low) / 2;
                if (middle * middle < value) low = middle;
                else high = middle;
            }
            return (int)high;
        }

        private static FixedPosition MoveTowards(RuntimeUnitState unit, RuntimeUnitState targetUnit)
        {
            return MoveTowards(unit, targetUnit.Position, FixedPosition.QuarterMetre - 1);
        }

        private static FixedPosition MoveTowards(RuntimeUnitState unit, FixedPosition target, int stopDistanceUnits)
        {
            var dx = target.XUnits - unit.Position.XUnits;
            var dy = target.YUnits - unit.Position.YUnits;
            var distance = IntegerSquareRootCeiling((long)dx * dx + (long)dy * dy);
            if (distance == 0) return unit.Position;
            unit.MoveRemainder += unit.Definition.MoveSpeedCentimetresPerSecond;
            var budget = unit.MoveRemainder / BattleInput.TicksPerSecond;
            unit.MoveRemainder %= BattleInput.TicksPerSecond;
            if (budget <= 0) return unit.Position;
            // Attack/block pursuit stops inside the shared strict range. Gate
            // movement instead passes zero and reaches the exact gate position.
            var maximumEntryBudget = Math.Max(0, distance - stopDistanceUnits);
            budget = Math.Min(budget, maximumEntryBudget);
            if (budget <= 0) return unit.Position;
            if (budget >= distance) { unit.MoveXNumeratorRemainder = 0; unit.MoveYNumeratorRemainder = 0; return target; }
            var xNumerator = (long)dx * budget + unit.MoveXNumeratorRemainder;
            var yNumerator = (long)dy * budget + unit.MoveYNumeratorRemainder;
            var moveX = (int)(xNumerator / distance);
            var moveY = (int)(yNumerator / distance);
            unit.MoveXNumeratorRemainder = (int)(xNumerator % distance);
            unit.MoveYNumeratorRemainder = (int)(yNumerator % distance);
            return new FixedPosition(unit.Position.XUnits + moveX, unit.Position.YUnits + moveY);
        }

        private void Emit(BattleEventType type, string unitId, string unitTypeId, string relatedUnitId, FixedPosition? from, FixedPosition? to, DamageType? damageType, int amount, int hpBefore, int hpAfter, int damageTick, int originalTicks, int effectiveTicks, BattleSide? winner, BattleStopReason reason, string animationKey = null)
        {
            if (eventTick != CurrentTick) { eventTick = CurrentTick; eventSequence = 0; }
            events.Add(new BattleEvent(type, CurrentTick, ++eventSequence, unitId, unitTypeId, null, relatedUnitId, from, to, damageType, amount, hpBefore, hpAfter, damageTick, originalTicks, effectiveTicks, winner, reason, null, animationKey));
        }

        private void EmitSpawn(RuntimeUnitState unit, BattleUnitInstanceSnapshot snapshot)
        {
            if (eventTick != CurrentTick) { eventTick = CurrentTick; eventSequence = 0; }
            events.Add(new BattleEvent(BattleEventType.Spawn, CurrentTick, ++eventSequence, unit.UnitId, unit.TypeId, unit.Side, null, null, unit.Position, null, 0, unit.CurrentHitPoints, unit.CurrentHitPoints, 0, 0, 0, null, BattleStopReason.None, snapshot));
        }

        private readonly struct MoveIntent { public MoveIntent(RuntimeUnitState unit, FixedPosition from, FixedPosition to, string relatedUnitId) { Unit = unit; From = from; To = to; RelatedUnitId = relatedUnitId; } public RuntimeUnitState Unit { get; } public FixedPosition From { get; } public FixedPosition To { get; } public string RelatedUnitId { get; } }
        private readonly struct BlockProposal { public BlockProposal(RuntimeUnitState actor, RuntimeUnitState target) { Actor = actor; Target = target; } public RuntimeUnitState Actor { get; } public RuntimeUnitState Target { get; } }
        private readonly struct PendingAttack { public PendingAttack(string attackerUnitId, string targetUnitId, int damageTick, DamageType damageType, int attack, int originalTicks, int effectiveTicks) { AttackerUnitId = attackerUnitId; TargetUnitId = targetUnitId; DamageTick = damageTick; DamageType = damageType; Attack = attack; OriginalTicks = originalTicks; EffectiveTicks = effectiveTicks; } public string AttackerUnitId { get; } public string TargetUnitId { get; } public int DamageTick { get; } public DamageType DamageType { get; } public int Attack { get; } public int OriginalTicks { get; } public int EffectiveTicks { get; } }
        private readonly struct DamageHit { public DamageHit(PendingAttack attack, int amount) { Attack = attack; Amount = amount; } public PendingAttack Attack { get; } public int Amount { get; } }

        private static List<RuntimeUnitState> BuildInitialUnits(
            BattleInput input,
            IReadOnlyDictionary<string, UnitDefinition> definitions,
            IReadOnlyDictionary<string, AbilityDefinition> abilities)
        {
            var result = new List<RuntimeUnitState>();
            foreach (var player in input.Players)
            foreach (var unit in player.Units)
            {
                if (unit.Zone != UnitZone.Deployed) continue;
                var coordinate = player.Side == BattleSide.Home ? BattlefieldRules.MapHome(unit.Formation.Value) : BattlefieldRules.MapAway(unit.Formation.Value);
                var definition = definitions[unit.TypeId];
                result.Add(new RuntimeUnitState(
                    unit.UnitId,
                    player.PlayerId,
                    player.Side,
                    definition,
                    unit,
                    coordinate,
                    CreateAbilityStates(definition, abilities)));
            }
            return result.OrderBy(item => item.UnitId, StringComparer.Ordinal).ToList();
        }

        private static IEnumerable<RuntimeAbilityState> CreateAbilityStates(
            UnitDefinition definition,
            IReadOnlyDictionary<string, AbilityDefinition> abilities)
        {
            return definition.InnateAbilityIds
                .OrderBy(item => item, StringComparer.Ordinal)
                .Select(item => new RuntimeAbilityState(abilities[item]))
                .ToArray();
        }

        private static BattleUnitInstanceSnapshot CreateSpawnSnapshot(RuntimeUnitState unit, bool isDynamicallyGenerated)
        {
            var definition = unit.Definition;
            return new BattleUnitInstanceSnapshot(
                unit.UnitId,
                unit.TypeId,
                unit.PlayerId,
                unit.Side,
                isDynamicallyGenerated,
                unit.Position,
                unit.EliteLevel,
                definition.MaxHitPoints,
                unit.CurrentHitPoints,
                0,
                definition.Attack,
                definition.Defense,
                definition.MagicResistance,
                definition.MoveSpeedCentimetresPerSecond,
                definition.AttackIntervalTicks,
                definition.AttackAnimationDurationTicks,
                definition.DamageType,
                definition.AttackMethod,
                definition.BlockCapacity,
                definition.TauntLevel,
                unit.Buffs,
                unit.ActivationTick);
        }

        private string BuildStableSummary()
        {
            var builder = new StringBuilder(Input.CanonicalSummary);
            builder.Append("|runner:").Append(CurrentTick).Append(',').Append((int)Status).Append(',').Append((int)StopReason);
            foreach (var unit in runtimeUnits.OrderBy(item => item.UnitId, StringComparer.Ordinal))
            {
                builder.Append("|R:").Append(unit.UnitId).Append(',').Append(unit.PlayerId).Append(',').Append((int)unit.Side).Append(',').Append(unit.TypeId).Append(',').Append(unit.CurrentHitPoints).Append(',').Append(unit.Position.XUnits).Append(',').Append(unit.Position.YUnits).Append(',').Append(unit.ActivationTick);
                foreach (var ability in unit.AbilityStates.OrderBy(item => item.Definition.AbilityId, StringComparer.Ordinal)) ability.AppendStableSummary(builder);
            }
            foreach (var item in trace) builder.Append("|S:").Append(item.Tick).Append(',').Append((int)item.Status).Append(',').Append((int)item.StopReason);
            return builder.ToString();
        }
    }
}
