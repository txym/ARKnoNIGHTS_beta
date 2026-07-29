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
        private bool healthThresholdTriggered;
        private bool healthThresholdActive;
        private int healthThresholdActiveUntilTick =
            int.MinValue;
        private int unblockedAttackChargeStacks;
        private bool attackCountStateForceUnlocked;
        private int damageReceivedCount;
        private bool healthThresholdAdjacentSpawnTriggered;
        private bool healthThresholdFullHealQueued;
        private bool healthThresholdFullHealAnimationStarted;
        private bool healthThresholdFullHealCompleted;
        private int healthThresholdFullHealDueTick =
            int.MinValue;
        private readonly HashSet<string>
            proximityEntryInsideTargetIds =
                new HashSet<string>(StringComparer.Ordinal);

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

        internal bool IsHealthThresholdActive =>
            healthThresholdActive;
        internal bool IsHealthThresholdUnblockable =>
            healthThresholdActive
            && Definition.HealthThresholdCombatModifier != null
            && Definition.HealthThresholdCombatModifier
                .MakesUnblockable;
        internal bool IsAttackCountStateUnlocked(
            int startedAttackCount)
        {
            var effect = Definition.AttackCountStateModifier;
            return effect != null
                && (attackCountStateForceUnlocked
                    || startedAttackCount
                    >= effect.TransitionBeforeAttackOrdinal);
        }

        internal bool WillReleaseAlliesOnAttack(
            int nextAttackOrdinal)
        {
            var effect = Definition.AttackCountStateModifier;
            return effect != null
                && effect.ReleasesAlliedAttackCountStates
                && !attackCountStateForceUnlocked
                && nextAttackOrdinal
                == effect.TransitionBeforeAttackOrdinal;
        }

        internal bool ForceUnlockAttackCountState()
        {
            if (Definition.AttackCountStateModifier == null
                || attackCountStateForceUnlocked)
                return false;
            attackCountStateForceUnlocked = true;
            return true;
        }
        internal int UnblockedAttackChargeAdditive
        {
            get
            {
                var effect = Definition.UnblockedAttackCharge;
                if (effect == null)
                    return 0;
                return (int)Math.Min(
                    int.MaxValue,
                    (long)unblockedAttackChargeStacks
                    * effect.AttackAdditivePerStack);
            }
        }

        internal void UpdateUnblockedAttackCharge(
            bool isBlocked,
            int currentTick,
            int activationTick)
        {
            var effect = Definition.UnblockedAttackCharge;
            if (effect == null
                || isBlocked
                || currentTick <= activationTick
                || (currentTick - activationTick)
                % effect.CheckIntervalTicks != 0
                || unblockedAttackChargeStacks
                >= effect.MaxStacks)
                return;
            unblockedAttackChargeStacks++;
        }

        internal void CompleteAttack()
        {
            if (Definition.UnblockedAttackCharge != null)
                unblockedAttackChargeStacks = 0;
        }

        internal bool RegisterDamageReceived(
            out int receivedOrdinal)
        {
            receivedOrdinal = 0;
            var effect = Definition.TriggeredSpawnEffect;
            if (effect == null
                || effect.TriggerKind
                != TriggeredSpawnKind.DamageReceived)
                return false;
            damageReceivedCount++;
            receivedOrdinal = damageReceivedCount;
            return effect.IsTriggered(damageReceivedCount);
        }

        internal IReadOnlyList<string>
            UpdateProximityEntryTargets(
                IEnumerable<string> targetUnitIds)
        {
            var next = new HashSet<string>(
                targetUnitIds ?? Enumerable.Empty<string>(),
                StringComparer.Ordinal);
            var entered = next
                .Where(item =>
                    !proximityEntryInsideTargetIds.Contains(item))
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray();
            proximityEntryInsideTargetIds.Clear();
            proximityEntryInsideTargetIds.UnionWith(next);
            return entered;
        }

        internal void UpdateHealthThresholdState(
            int currentHitPoints,
            int maxHitPoints,
            int currentTick)
        {
            var effect =
                Definition.HealthThresholdCombatModifier;
            if (effect == null)
                return;
            if (healthThresholdActive
                && effect.TriggerOnce
                && effect.DurationTicks > 0
                && currentTick > healthThresholdActiveUntilTick)
                healthThresholdActive = false;
            var scaledCurrent = (long)currentHitPoints * 1000;
            var scaledThreshold =
                (long)maxHitPoints
                * effect.ThresholdHitPointsPermille;
            var condition = effect.InclusiveThreshold
                ? scaledCurrent <= scaledThreshold
                : scaledCurrent < scaledThreshold;
            if (!effect.TriggerOnce)
            {
                healthThresholdActive = condition;
                return;
            }

            if (healthThresholdTriggered || !condition)
                return;
            healthThresholdTriggered = true;
            healthThresholdActive = true;
            healthThresholdActiveUntilTick =
                effect.DurationTicks > 0
                    ? currentTick + effect.DurationTicks - 1
                    : int.MaxValue;
        }

        internal bool TryTriggerHealthThresholdAdjacentSpawn(
            int currentHitPoints,
            int maxHitPoints)
        {
            var effect =
                Definition.HealthThresholdAdjacentSpawnEffect;
            if (effect == null
                || healthThresholdAdjacentSpawnTriggered)
                return false;
            var scaledCurrent =
                (long)currentHitPoints * 1000;
            var scaledThreshold =
                (long)maxHitPoints
                * effect.ThresholdHitPointsPermille;
            var condition = effect.InclusiveThreshold
                ? scaledCurrent <= scaledThreshold
                : scaledCurrent < scaledThreshold;
            if (!condition)
                return false;
            healthThresholdAdjacentSpawnTriggered = true;
            return true;
        }

        internal void QueueHealthThresholdFullHeal(
            int currentHitPoints,
            int maxHitPoints)
        {
            var effect =
                Definition.HealthThresholdFullHealEffect;
            if (effect == null || healthThresholdFullHealQueued)
                return;
            var scaledCurrent =
                (long)currentHitPoints * 1000;
            var scaledThreshold =
                (long)maxHitPoints
                * effect.ThresholdHitPointsPermille;
            var condition = effect.InclusiveThreshold
                ? scaledCurrent <= scaledThreshold
                : scaledCurrent < scaledThreshold;
            if (condition)
                healthThresholdFullHealQueued = true;
        }

        internal bool TryStartHealthThresholdFullHeal(
            int currentTick,
            out HealthThresholdFullHealEffectDefinition effect)
        {
            effect = Definition.HealthThresholdFullHealEffect;
            if (effect == null
                || !healthThresholdFullHealQueued
                || healthThresholdFullHealAnimationStarted
                || healthThresholdFullHealCompleted)
                return false;
            healthThresholdFullHealAnimationStarted = true;
            healthThresholdFullHealDueTick =
                currentTick
                + effect.AnimationEffectiveDurationTicks;
            return true;
        }

        internal bool TryCompleteHealthThresholdFullHeal(
            int currentTick)
        {
            if (!healthThresholdFullHealAnimationStarted
                || healthThresholdFullHealCompleted
                || currentTick < healthThresholdFullHealDueTick)
                return false;
            healthThresholdFullHealCompleted = true;
            return true;
        }

        internal void AppendStableSummary(StringBuilder builder)
        {
            builder.Append(',')
                .Append(Definition.AbilityId)
                .Append(':')
                .Append(currentSkillPoints)
                .Append(':')
                .Append(castCount);
            if (Definition.HealthThresholdCombatModifier != null)
                builder.Append(':')
                    .Append(healthThresholdTriggered ? 1 : 0)
                    .Append(':')
                    .Append(healthThresholdActive ? 1 : 0)
                    .Append(':')
                    .Append(healthThresholdActiveUntilTick);
            if (Definition.UnblockedAttackCharge != null)
                builder.Append(":charge:")
                    .Append(unblockedAttackChargeStacks);
            if (Definition.AttackCountStateModifier != null
                && attackCountStateForceUnlocked)
                builder.Append(":forced-unlock");
            if (Definition.TriggeredSpawnEffect != null
                && Definition.TriggeredSpawnEffect.TriggerKind
                == TriggeredSpawnKind.DamageReceived)
                builder.Append(":received:")
                    .Append(damageReceivedCount);
            if (Definition.HealthThresholdAdjacentSpawnEffect
                != null)
                builder.Append(":adjacent-spawn:")
                    .Append(
                        healthThresholdAdjacentSpawnTriggered
                            ? 1
                            : 0);
            if (Definition.HealthThresholdFullHealEffect != null)
                builder.Append(":full-heal:")
                    .Append(
                        healthThresholdFullHealQueued ? 1 : 0)
                    .Append(':')
                    .Append(
                        healthThresholdFullHealAnimationStarted
                            ? 1
                            : 0)
                    .Append(':')
                    .Append(
                        healthThresholdFullHealCompleted ? 1 : 0)
                    .Append(':')
                    .Append(healthThresholdFullHealDueTick);
            if (Definition.ProximityEntryDamageEffect != null)
                builder.Append(":proximity:")
                    .Append(string.Join(
                        ",",
                        proximityEntryInsideTargetIds
                            .OrderBy(
                                item => item,
                                StringComparer.Ordinal)));
        }
    }

    internal sealed class RuntimeDamageOverTimeState
    {
        internal RuntimeDamageOverTimeState(
            string abilityId,
            int damagePerSecond,
            int expiresAtTick)
        {
            AbilityId = abilityId;
            DamagePerSecond = damagePerSecond;
            ExpiresAtTick = expiresAtTick;
        }

        internal string AbilityId { get; }
        internal int DamagePerSecond { get; private set; }
        internal int ExpiresAtTick { get; private set; }

        internal void Refresh(
            int damagePerSecond,
            int expiresAtTick)
        {
            DamagePerSecond = damagePerSecond;
            ExpiresAtTick = expiresAtTick;
        }
    }

    public sealed class RuntimeUnitState
    {
        private readonly List<string> blockedUnitIds = new List<string>();
        private readonly List<RuntimeAbilityState> abilityStates;
        private readonly List<IExternalCombatModifierDefinition>
            auraCombatModifiers =
                new List<IExternalCombatModifierDefinition>();
        private readonly List<RuntimeDamageOverTimeState>
            damageOverTimeStates =
                new List<RuntimeDamageOverTimeState>();
        private long accumulatedDefenseReduction;
        private bool temporaryUnblockable;
        private int temporaryUnblockableUntilTick =
            int.MinValue;

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
            : this(
                unitId,
                playerId,
                side,
                definition,
                position,
                eliteLevel,
                buffs,
                activationTick,
                abilities,
                DeathSpawnEffectDefinition
                    .NeutralMoveSpeedMultiplierPermille)
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
            IEnumerable<RuntimeAbilityState> abilities,
            int instanceMoveSpeedMultiplierPermille)
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
            InstanceMoveSpeedMultiplierPermille =
                instanceMoveSpeedMultiplierPermille;
            abilityStates = (abilities ?? Enumerable.Empty<RuntimeAbilityState>()).OrderBy(item => item.Definition.AbilityId, StringComparer.Ordinal).ToList();
            AbilityStates = new ReadOnlyCollection<RuntimeAbilityState>(abilityStates);
            BlockedUnitIds = new ReadOnlyCollection<string>(blockedUnitIds);
            if (activationTick == 0
                && abilityStates.Any(item =>
                    item.Definition.UnitTraitEffect != null
                    && item.Definition.UnitTraitEffect.Kind
                    == UnitTraitEffectKind
                        .MoveFromOwnGateToDeploymentPosition))
            {
                DeploymentApproachDestination = position;
                Position = FixedPosition.FromCell(
                    side == BattleSide.Home
                        ? BattlefieldRules.BlueGate
                        : BattlefieldRules.RedGate);
            }
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
        public bool HasExitedBattle { get; internal set; }
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
        internal int PassiveHealthRemainder { get; set; }
        internal int StartedAttackCount { get; private set; }
        internal bool ReleasedAlliesOnLastAttack
        {
            get;
            private set;
        }
        internal int InstanceMoveSpeedMultiplierPermille
        {
            get;
        }
        internal UnitDefinition Definition { get; }
        internal IReadOnlyList<RuntimeAbilityState> AbilityStates { get; }
        internal FixedPosition? DeploymentApproachDestination
        {
            get;
        }
        internal bool HasDeploymentApproach =>
            DeploymentApproachDestination.HasValue;
        internal bool IsDeploymentApproachInProgress =>
            HasDeploymentApproach
            && !Position.Equals(
                DeploymentApproachDestination.Value);
        internal bool IsTargetableBy(AttackMethod attackMethod) =>
            abilityStates.All(item =>
                item.Definition.UnitTraitEffect == null
                || (item.Definition.UnitTraitEffect.Kind
                        != UnitTraitEffectKind.Untargetable
                    && (item.Definition.UnitTraitEffect.Kind
                            != UnitTraitEffectKind.UntargetableByMelee
                        || attackMethod != AttackMethod.Melee)));
        public int EffectiveBlockCapacity
        {
            get
            {
                if (abilityStates.Any(item =>
                        item.IsHealthThresholdUnblockable)
                    || temporaryUnblockable)
                    return 0;
                var value = (long)Definition.BlockCapacity
                    + PassiveCombatModifiers.Sum(item =>
                        (long)item.BlockCapacityAdditive)
                    + ActiveHealthThresholdModifiers.Sum(item =>
                        (long)item.BlockCapacityAdditive);
                return value <= 0
                    ? 0
                    : value >= int.MaxValue
                        ? int.MaxValue
                        : (int)value;
            }
        }
        public int EffectiveMagicResistance => Math.Max(
            0,
            Math.Min(
                100,
                Definition.MagicResistance
                + PassiveCombatModifiers.Sum(item =>
                    item.MagicResistanceAdditive)
                + ActiveUnlockedAttackCountStateModifiers.Sum(item =>
                    item.UnlockedMagicResistanceAdditive)
                + auraCombatModifiers.Sum(item =>
                    item.MagicResistanceAdditive)));
        public int EffectiveAttackIntervalTicks
        {
            get
            {
                var finalAttackSpeed = 100
                    + PassiveCombatModifiers.Sum(item =>
                        item.AttackSpeedAdditive)
                    + ActiveHealthThresholdModifiers.Sum(item =>
                        item.AttackSpeedAdditive)
                    + ActiveLockedAttackCountStateModifiers.Sum(item =>
                        item.LockedAttackSpeedAdditive);
                if (finalAttackSpeed <= 0)
                    return 0;
                foreach (var modifier in auraCombatModifiers)
                    finalAttackSpeed = ApplyMultiplier(
                        finalAttackSpeed,
                        modifier.AttackSpeedMultiplierPermille);
                if (finalAttackSpeed <= 0)
                    return 0;
                var numerator =
                    (long)Definition.AttackIntervalTicks * 100;
                return Math.Max(
                    1,
                    (int)Math.Min(
                        int.MaxValue,
                        (numerator + finalAttackSpeed - 1)
                        / finalAttackSpeed));
            }
        }
        internal bool HasBlockingCapacity =>
            blockedUnitIds.Count < EffectiveBlockCapacity;
        public int EffectiveAttack
        {
            get
            {
                var attack = ApplyThresholdMultiplier(
                    Definition.Attack,
                    item => item.AttackMultiplierPermille);
                foreach (var modifier in
                         ActiveUnlockedAttackCountStateModifiers)
                    attack = ApplyMultiplier(
                        attack,
                        modifier.UnlockedAttackMultiplierPermille);
                foreach (var modifier in auraCombatModifiers)
                    attack = ApplyMultiplier(
                        attack,
                        modifier.AttackMultiplierPermille);
                var charge = abilityStates.Sum(item =>
                    (long)item.UnblockedAttackChargeAdditive);
                return (int)Math.Min(
                    int.MaxValue,
                    attack + charge);
            }
        }
        internal void UpdateUnblockedAttackCharges(int currentTick)
        {
            foreach (var ability in abilityStates)
                ability.UpdateUnblockedAttackCharge(
                    IsBlocked,
                    currentTick,
                    ActivationTick);
        }
        internal void CompleteAttack()
        {
            foreach (var ability in abilityStates)
                ability.CompleteAttack();
        }
        public int AccumulatedDefenseReduction =>
            accumulatedDefenseReduction >= int.MaxValue
                ? int.MaxValue
                : (int)accumulatedDefenseReduction;
        public int EffectiveDefense => Math.Max(
            0,
            ApplyThresholdMultiplier(
                Definition.Defense
                + ActiveLockedAttackCountStateModifiers.Sum(item =>
                    item.LockedDefenseAdditive)
                + auraCombatModifiers.Sum(item =>
                    item.DefenseAdditive),
                item => item.DefenseMultiplierPermille)
            - AccumulatedDefenseReduction);
        public int EffectiveMoveSpeedCentimetresPerSecond
        {
            get
            {
                var speed = ApplyThresholdMultiplier(
                ApplyMultiplier(
                    Definition.MoveSpeedCentimetresPerSecond,
                    InstanceMoveSpeedMultiplierPermille),
                item => item.MoveSpeedMultiplierPermille);
                foreach (var modifier in auraCombatModifiers)
                    speed = ApplyMultiplier(
                        speed,
                        modifier.MoveSpeedMultiplierPermille);
                return speed;
            }
        }
        internal int BeginAttackAndGetEffectiveAttack()
        {
            ReleasedAlliesOnLastAttack = abilityStates.Any(item =>
                item.WillReleaseAlliesOnAttack(
                    StartedAttackCount + 1));
            StartedAttackCount++;
            var attack = EffectiveAttack;
            foreach (var modifier in abilityStates
                         .Select(item =>
                             item.Definition.AttackSequenceModifier)
                         .Where(item =>
                             item != null
                             && item.IsEnhancedAttack(
                                 StartedAttackCount)))
                attack = (int)Math.Min(
                    int.MaxValue,
                    (long)attack
                    * modifier.AttackMultiplierPermille
                    / 1000);
            return attack;
        }
        internal void ForceUnlockAttackCountStates()
        {
            foreach (var ability in abilityStates)
                ability.ForceUnlockAttackCountState();
        }
        internal TriggeredSpawnEffectDefinition
            GetTriggeredAttackSkillAnimation(int attackOrdinal)
        {
            return abilityStates
                .Select(item =>
                    item.Definition.TriggeredSpawnEffect)
                .Where(item =>
                    item != null
                    && item.TriggerKind
                    == TriggeredSpawnKind.SuccessfulAttack
                    && item.UsesSkillAttackAnimation
                    && item.IsTriggered(attackOrdinal))
                .FirstOrDefault();
        }
        internal int EffectiveTargetDefenseMultiplierPermille
        {
            get
            {
                var multiplier =
                    AttackCountStateModifierDefinition
                        .NeutralMultiplierPermille;
                foreach (var modifier in
                         ActiveUnlockedAttackCountStateModifiers)
                    multiplier = ApplyMultiplier(
                        multiplier,
                        modifier
                            .UnlockedTargetDefenseMultiplierPermille);
                return multiplier;
            }
        }
        internal bool IsBlocked => blockedUnitIds.Count != 0;
        internal bool HasBlockWith(string unitId) => blockedUnitIds.Contains(unitId);
        internal void AddBlock(string unitId)
        {
            if (HasBlockWith(unitId)) return;
            blockedUnitIds.Add(unitId);
            blockedUnitIds.Sort(StringComparer.Ordinal);
        }
        internal bool RemoveBlock(string unitId) => blockedUnitIds.Remove(unitId);
        internal void StartTemporaryUnblockable(
            int currentTick,
            int durationTicks)
        {
            temporaryUnblockable = true;
            temporaryUnblockableUntilTick =
                currentTick + durationTicks - 1;
        }
        internal void UpdateTemporaryUnblockable(int currentTick)
        {
            if (temporaryUnblockable
                && currentTick > temporaryUnblockableUntilTick)
                temporaryUnblockable = false;
        }
        internal void ResetMoveDirectionRemainders()
        {
            MoveXNumeratorRemainder = 0;
            MoveYNumeratorRemainder = 0;
        }
        internal AttackDashEffectDefinition GetAttackDashEffect(
            int attackOrdinal)
        {
            return abilityStates
                .Select(item =>
                    item.Definition.AttackDashEffect)
                .Where(item =>
                    item != null
                    && item.IsTriggered(attackOrdinal))
                .FirstOrDefault();
        }
        internal void AppendTemporaryUnblockableSummary(
            StringBuilder builder)
        {
            if (!abilityStates.Any(item =>
                    item.Definition.AttackDashEffect != null
                    || item.Definition.TimedBlinkEffect != null))
                return;
            builder.Append(",unblockable:")
                .Append(temporaryUnblockable ? 1 : 0)
                .Append(':')
                .Append(temporaryUnblockableUntilTick);
        }

        internal int ApplyDamageTakenModifiers(
            DamageType damageType,
            int amount)
        {
            if (damageType != DamageType.Physical
                && damageType != DamageType.Magic)
                return amount;
            foreach (var abilityState in abilityStates)
            {
                var passive =
                    abilityState.Definition.PassiveCombatModifier;
                var conditional =
                    abilityState.Definition
                        .UnblockedDamageTakenModifier;
                var permille = passive != null
                    ? damageType == DamageType.Physical
                        ? passive.PhysicalDamageTakenPermille
                        : passive.MagicDamageTakenPermille
                    : conditional != null && !IsBlocked
                        ? damageType == DamageType.Physical
                            ? conditional.PhysicalDamageTakenPermille
                            : conditional.MagicDamageTakenPermille
                        : PassiveCombatModifierDefinition
                            .NeutralDamageTakenPermille;
                amount = (int)((long)amount * permille / 1000);
            }

            return amount;
        }

        private IEnumerable<PassiveCombatModifierDefinition>
            PassiveCombatModifiers =>
            abilityStates
                .Select(item => item.Definition.PassiveCombatModifier)
                .Where(item => item != null);
        private IEnumerable<HealthThresholdCombatModifierDefinition>
            ActiveHealthThresholdModifiers =>
            abilityStates
                .Where(item => item.IsHealthThresholdActive)
                .Select(item =>
                    item.Definition.HealthThresholdCombatModifier)
                .Where(item => item != null);
        internal int PassiveHitPointsPerSecond =>
            abilityStates
                .Where(item =>
                    item.Definition.PassiveLifecycleEffect != null)
                .Sum(item =>
                    item.Definition.PassiveLifecycleEffect
                        .HitPointsPerSecond)
            + ActiveUnlockedAttackCountStateModifiers.Sum(item =>
                item.UnlockedHitPointsPerSecond)
            + auraCombatModifiers.Sum(item =>
                item.HitPointsPerSecond);
        internal int PassiveLifetimeTicks
        {
            get
            {
                var lifetimes = abilityStates
                    .Where(item =>
                        item.Definition.PassiveLifecycleEffect != null
                        && item.Definition.PassiveLifecycleEffect
                            .LifetimeTicks > 0)
                    .Select(item =>
                        item.Definition.PassiveLifecycleEffect
                            .LifetimeTicks)
                    .ToArray();
                return lifetimes.Length == 0
                    ? 0
                    : lifetimes.Min();
            }
        }
        internal IEnumerable<OnDamageReactionEffectDefinition>
            OnDamageReactions =>
            abilityStates
                .Select(item =>
                    item.Definition.OnDamageReactionEffect)
                .Where(item => item != null);
        internal IEnumerable<KeyValuePair<string, DeathSpawnEffectDefinition>>
            DeathSpawnEffects =>
            abilityStates
                .Where(item =>
                    item.Definition.DeathSpawnEffect != null)
                .Select(item =>
                    new KeyValuePair<string, DeathSpawnEffectDefinition>(
                        item.Definition.AbilityId,
                        item.Definition.DeathSpawnEffect));
        internal IEnumerable<KeyValuePair<string, AuraCombatModifierDefinition>>
            AuraCombatModifiers =>
            abilityStates
                .Where(item =>
                    !IsDeploymentApproachInProgress
                    &&
                    item.Definition.AuraCombatModifier != null)
                .Select(item =>
                    new KeyValuePair<string, AuraCombatModifierDefinition>(
                        item.Definition.AbilityId,
                        item.Definition.AuraCombatModifier));
        internal IEnumerable<KeyValuePair<string, BlockedCounterpartCombatModifierDefinition>>
            BlockedCounterpartCombatModifiers =>
            abilityStates
                .Where(item =>
                    item.Definition
                        .BlockedCounterpartCombatModifier != null)
                .Select(item =>
                    new KeyValuePair<string, BlockedCounterpartCombatModifierDefinition>(
                        item.Definition.AbilityId,
                        item.Definition
                            .BlockedCounterpartCombatModifier));
        internal IEnumerable<KeyValuePair<string, NearbySameTypeSelfModifierDefinition>>
            NearbySameTypeSelfModifiers =>
            abilityStates
                .Where(item =>
                    item.Definition
                        .NearbySameTypeSelfModifier != null)
                .Select(item =>
                    new KeyValuePair<string, NearbySameTypeSelfModifierDefinition>(
                        item.Definition.AbilityId,
                        item.Definition
                            .NearbySameTypeSelfModifier));
        internal IEnumerable<KeyValuePair<string, EvasionModifierDefinition>>
            EvasionModifiers =>
            abilityStates
                .Where(item =>
                    item.Definition.EvasionModifier != null)
                .Select(item =>
                    new KeyValuePair<string, EvasionModifierDefinition>(
                        item.Definition.AbilityId,
                        item.Definition.EvasionModifier));
        internal IEnumerable<KeyValuePair<string, DeathAreaDamageEffectDefinition>>
            DeathAreaDamageEffects =>
            abilityStates
                .Where(item =>
                    item.Definition.DeathAreaDamageEffect != null)
                .Select(item =>
                    new KeyValuePair<string, DeathAreaDamageEffectDefinition>(
                        item.Definition.AbilityId,
                        item.Definition.DeathAreaDamageEffect));
        internal IEnumerable<KeyValuePair<string, AttackAreaDamageModifierDefinition>>
            AttackAreaDamageModifiers =>
            abilityStates
                .Where(item =>
                    item.Definition.AttackAreaDamageModifier != null)
                .Select(item =>
                    new KeyValuePair<string, AttackAreaDamageModifierDefinition>(
                        item.Definition.AbilityId,
                        item.Definition.AttackAreaDamageModifier));
        internal IEnumerable<KeyValuePair<string, OnHitDamageOverTimeEffectDefinition>>
            OnHitDamageOverTimeEffects =>
            abilityStates
                .Where(item =>
                    item.Definition.OnHitDamageOverTimeEffect != null)
                .Select(item =>
                    new KeyValuePair<string, OnHitDamageOverTimeEffectDefinition>(
                        item.Definition.AbilityId,
                        item.Definition.OnHitDamageOverTimeEffect));
        internal IEnumerable<KeyValuePair<string, OnHitDefenseDebuffEffectDefinition>>
            OnHitDefenseDebuffEffects =>
            abilityStates
                .Where(item =>
                    item.Definition.OnHitDefenseDebuffEffect != null)
                .Select(item =>
                    new KeyValuePair<string, OnHitDefenseDebuffEffectDefinition>(
                        item.Definition.AbilityId,
                        item.Definition.OnHitDefenseDebuffEffect));
        internal void ApplyDefenseReduction(int amount)
        {
            accumulatedDefenseReduction = Math.Min(
                int.MaxValue,
                accumulatedDefenseReduction + amount);
        }
        internal void ApplyOrRefreshDamageOverTime(
            string abilityId,
            int damagePerSecond,
            int expiresAtTick)
        {
            var existing = damageOverTimeStates.FirstOrDefault(item =>
                string.Equals(
                    item.AbilityId,
                    abilityId,
                    StringComparison.Ordinal));
            if (existing != null)
            {
                existing.Refresh(
                    damagePerSecond,
                    expiresAtTick);
                return;
            }
            damageOverTimeStates.Add(
                new RuntimeDamageOverTimeState(
                    abilityId,
                    damagePerSecond,
                    expiresAtTick));
            damageOverTimeStates.Sort((left, right) =>
                StringComparer.Ordinal.Compare(
                    left.AbilityId,
                    right.AbilityId));
        }
        internal void ExpireDamageOverTime(int currentTick)
        {
            damageOverTimeStates.RemoveAll(item =>
                currentTick >= item.ExpiresAtTick);
        }
        internal int ActiveDamageOverTimePerSecond
        {
            get
            {
                var total = damageOverTimeStates.Sum(item =>
                    (long)item.DamagePerSecond);
                return total >= int.MaxValue
                    ? int.MaxValue
                    : (int)total;
            }
        }
        internal void AppendDamageOverTimeSummary(
            StringBuilder builder)
        {
            foreach (var item in damageOverTimeStates)
                builder.Append(",dot:")
                    .Append(item.AbilityId).Append(':')
                    .Append(item.DamagePerSecond).Append(':')
                    .Append(item.ExpiresAtTick);
        }
        internal void SetAuraCombatModifiers(
            IEnumerable<IExternalCombatModifierDefinition> modifiers)
        {
            auraCombatModifiers.Clear();
            auraCombatModifiers.AddRange(
                modifiers
                ?? Enumerable.Empty<IExternalCombatModifierDefinition>());
        }

        private int ApplyThresholdMultiplier(
            int value,
            Func<HealthThresholdCombatModifierDefinition, int>
                selector)
        {
            foreach (var modifier in
                     ActiveHealthThresholdModifiers)
                value = ApplyMultiplier(value, selector(modifier));
            return value;
        }

        private IEnumerable<AttackCountStateModifierDefinition>
            ActiveLockedAttackCountStateModifiers =>
            abilityStates
                .Where(item =>
                    item.Definition.AttackCountStateModifier != null
                    && !item.IsAttackCountStateUnlocked(
                        StartedAttackCount))
                .Select(item =>
                    item.Definition.AttackCountStateModifier);

        private IEnumerable<AttackCountStateModifierDefinition>
            ActiveUnlockedAttackCountStateModifiers =>
            abilityStates
                .Where(item =>
                    item.IsAttackCountStateUnlocked(
                        StartedAttackCount))
                .Select(item =>
                    item.Definition.AttackCountStateModifier);

        private static int ApplyMultiplier(
            int value,
            int multiplierPermille)
        {
            return (int)Math.Min(
                int.MaxValue,
                (long)value * multiplierPermille / 1000);
        }
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
            HasExitedBattle = state.HasExitedBattle;
        }

        public string UnitId { get; }
        public string TypeId { get; }
        public BattleSide Side { get; }
        public FixedPosition Position { get; }
        public int HitPoints { get; }
        public bool IsAlive { get; }
        public bool HasExitedBattle { get; }
    }

    public sealed class BattleRunResult
    {
        internal BattleRunResult(string battleId, string homePlayerId, string awayPlayerId, string inputCanonicalSummary, IReadOnlyList<string> knownUnitTypeIds, int completedTicks, BattleStopReason stopReason, BattleSide? winner, IReadOnlyList<BattleStepTrace> trace, IReadOnlyList<BattleEvent> events, IReadOnlyList<BattleUnitFinalState> finalUnits, string stableSummary, int homeLifeLoss = 0, int awayLifeLoss = 0)
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
                stableSummary,
                homeLifeLoss,
                awayLifeLoss)
        {
        }

        internal BattleRunResult(string battleId, string homePlayerId, string awayPlayerId, string inputCanonicalSummary, IReadOnlyList<string> knownUnitTypeIds, int completedTicks, BattleStopReason stopReason, BattleSide? winner, IReadOnlyList<BattleStepTrace> trace, IReadOnlyList<BattleEvent> events, IReadOnlyList<BattleUnitFinalState> finalUnits, IReadOnlyDictionary<string, BattleUnitInstanceSnapshot> unitSnapshots, string stableSummary, int homeLifeLoss = 0, int awayLifeLoss = 0)
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
            HomeLifeLoss = homeLifeLoss;
            AwayLifeLoss = awayLifeLoss;
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
        public int HomeLifeLoss { get; }
        public int AwayLifeLoss { get; }

        public bool TryGetUnitSnapshot(string unitId, out BattleUnitInstanceSnapshot snapshot)
        {
            return UnitSnapshots.TryGetValue(unitId, out snapshot);
        }
    }

    /// <summary>Explicit, deterministic tick driver. TASK-003 will add combat stages inside RunAuthoritativeTick.</summary>
    public sealed class BattleRunner
    {
        private const int AutomaticSkillPointGainIntervalTicks = BattleInput.TicksPerSecond / BattleInput.AutomaticSkillPointsPerSecond;
        private const int OpposingGateHalfExtentUnits =
            FixedPosition.UnitsPerMetre * 4 / 10;
        private readonly List<RuntimeUnitState> runtimeUnits;
        private readonly List<BattleStepTrace> trace = new List<BattleStepTrace>();
        private readonly List<BattleEvent> events = new List<BattleEvent>();
        private readonly List<PendingAttack> pendingAttacks = new List<PendingAttack>();
        private readonly List<PendingDeathSpawn>
            pendingDeathSpawns = new List<PendingDeathSpawn>();
        private readonly List<PendingDeathAreaDamage>
            pendingDeathAreaDamages =
                new List<PendingDeathAreaDamage>();
        private readonly List<PendingTimedTargetAreaDamage>
            pendingTimedTargetAreaDamages =
                new List<PendingTimedTargetAreaDamage>();
        private readonly List<PendingRelocation>
            pendingRelocations =
                new List<PendingRelocation>();
        private readonly Dictionary<string, UnitDefinition> unitDefinitions;
        private readonly Dictionary<string, AbilityDefinition> abilityDefinitions;
        private readonly Dictionary<string, BattleUnitInstanceSnapshot> unitSnapshots = new Dictionary<string, BattleUnitInstanceSnapshot>(StringComparer.Ordinal);
        private readonly DynamicUnitIdAllocator dynamicUnitIdAllocator = new DynamicUnitIdAllocator();
        private int eventTick = int.MinValue;
        private int eventSequence;
        private int homeGateLifeLoss;
        private int awayGateLifeLoss;

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
                immutableUnitSnapshots, BuildStableSummary(), HomeLifeLoss, AwayLifeLoss);
        }

        public BattleSide? Winner { get; private set; }
        public int HomeLifeLoss { get; private set; }
        public int AwayLifeLoss { get; private set; }

        private void RunAuthoritativeTick()
        {
            foreach (var unit in runtimeUnits)
                unit.UpdateTemporaryUnblockable(CurrentTick);
            ResolveDueRelocations();
            ResolveDueTimedTargetAreaDamage();
            ResolveDueDeathSpawns();
            ResolveDueDeathAreaDamage();
            ResolveDueHealthThresholdFullHeals();
            UpdateHealthThresholdStates();
            ResolveDeathsAndCleanup();
            ResolveDueDeathSpawns();
            RefreshAuraCombatModifiers();
            RemoveInvalidPendingAttacks();
            AcquireTargets();
            ApplyMovement();
            ResolveOpposingGateArrivals();
            RefreshAuraCombatModifiers();
            ResolveProximityEntryDamage();
            ResolveDeathsAndCleanup();
            RefreshAuraCombatModifiers();
            EvaluateBlocking();
            RefreshAuraCombatModifiers();
            UpdateUnblockedAttackCharges();
            CastReadyAbilities();
            StartAttacks();
            ResolveDueDamage();
            UpdateHealthThresholdStates();
            ResolveDeathsAndCleanup();
            ResolveDueDeathSpawns();
            RefreshAuraCombatModifiers();
            ApplyPassiveLifecycleEffects();
            UpdateHealthThresholdStates();
            ResolveDeathsAndCleanup();
            ResolveDueDeathSpawns();
            RefreshAuraCombatModifiers();
            EvaluateBattleEnd();
            if (Status == BattleRunnerStatus.Stopped) return;
            RecoverAutomaticSkillPointsAndCast();
        }

        private void RemoveInvalidPendingAttacks()
        {
            // A dead or exited target cancels damage at its due Tick, but the
            // attacker's already-started animation lock remains intact.
            pendingAttacks.RemoveAll(item =>
                !IsInBattle(FindUnit(item.AttackerUnitId)));
        }

        private void AcquireTargets()
        {
            foreach (var unit in runtimeUnits.Where(IsActive).OrderBy(item => item.UnitId, StringComparer.Ordinal))
            {
                if (unit.IsDeploymentApproachInProgress
                    || !unit.Definition.CanAttack)
                {
                    SetTarget(unit, null);
                    continue;
                }
                if (HasLiveTarget(unit)) continue;
                var selected = runtimeUnits.Where(candidate =>
                        IsActive(candidate)
                        && candidate.IsTargetableBy(
                            unit.Definition.AttackMethod)
                        && candidate.Side != unit.Side)
                    .OrderBy(candidate => DistanceSquared(unit.Position, candidate.Position))
                    .ThenBy(candidate => DistanceSquared(candidate.Position, GatePosition(unit.Side)))
                    .ThenBy(candidate => candidate.UnitId, StringComparer.Ordinal).FirstOrDefault();
                SetTarget(unit, selected == null ? null : selected.UnitId);
            }
        }

        private void ApplyMovement()
        {
            var intents = new List<MoveIntent>();
            foreach (var unit in runtimeUnits.Where(item => IsActive(item) && (item.HasDeploymentApproach || item.Definition.ActionMethod != 4) && !item.IsBlocked && !IsSkillAnimationLocked(item) && !HasTargetDeathAnimationLock(item)).OrderBy(item => item.UnitId, StringComparer.Ordinal))
            {
                if (unit.HasDeploymentApproach)
                {
                    if (unit.IsDeploymentApproachInProgress)
                    {
                        var destination =
                            unit.DeploymentApproachDestination.Value;
                        var nextDestinationPosition =
                            MoveTowards(unit, destination, 0);
                        if (!nextDestinationPosition.Equals(
                                unit.Position))
                            intents.Add(new MoveIntent(
                                unit,
                                unit.Position,
                                nextDestinationPosition,
                                null));
                    }
                    continue;
                }

                if (!unit.Definition.CanAttack)
                {
                    var gate = OpposingGatePosition(unit.Side);
                    var nextGatePosition = MoveTowards(unit, gate, 0);
                    if (!nextGatePosition.Equals(unit.Position))
                        intents.Add(new MoveIntent(unit, unit.Position, nextGatePosition, null));
                    continue;
                }

                if (!HasLiveTarget(unit))
                {
                    if (runtimeUnits.Any(candidate =>
                            IsActive(candidate)
                            && candidate.Side != unit.Side))
                    {
                        var gate = OpposingGatePosition(unit.Side);
                        var nextGatePosition = MoveTowards(
                            unit,
                            gate,
                            0);
                        if (!nextGatePosition.Equals(unit.Position))
                            intents.Add(new MoveIntent(
                                unit,
                                unit.Position,
                                nextGatePosition,
                                null));
                    }
                    continue;
                }
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

        private void ResolveOpposingGateArrivals()
        {
            foreach (var unit in runtimeUnits
                         .Where(item =>
                             IsActive(item)
                             && IsInsideOpposingGateArea(item))
                         .OrderBy(item => item.UnitId, StringComparer.Ordinal)
                         .ToArray())
            {
                foreach (var otherId in unit.BlockedUnitIds.ToArray())
                    EndBlock(unit, FindUnit(otherId));
                foreach (var other in runtimeUnits
                             .Where(item =>
                                 IsActive(item)
                                 && string.Equals(
                                     item.TargetUnitId,
                                     unit.UnitId,
                                     StringComparison.Ordinal))
                             .OrderBy(
                                 item => item.UnitId,
                                 StringComparer.Ordinal))
                    SetTarget(other, null);
                SetTarget(unit, null);
                unit.HasExitedBattle = true;
                pendingAttacks.RemoveAll(item =>
                    string.Equals(
                        item.AttackerUnitId,
                        unit.UnitId,
                        StringComparison.Ordinal));
                if (unit.Side == BattleSide.Home)
                    awayGateLifeLoss = SaturatingAdd(
                        awayGateLifeLoss,
                        unit.Definition.LifeDeduct);
                else
                    homeGateLifeLoss = SaturatingAdd(
                        homeGateLifeLoss,
                        unit.Definition.LifeDeduct);
                Emit(
                    BattleEventType.GateReached,
                    unit.UnitId,
                    null,
                    null,
                    null,
                    unit.Position,
                    null,
                    unit.Definition.LifeDeduct,
                    unit.CurrentHitPoints,
                    unit.CurrentHitPoints,
                    0,
                    0,
                    0,
                    null,
                    BattleStopReason.None);
            }
        }

        private void EvaluateBlocking()
        {
            foreach (var unit in runtimeUnits.Where(item => item.IsBlocked).OrderBy(item => item.UnitId, StringComparer.Ordinal).ToArray())
            {
                foreach (var otherId in unit.BlockedUnitIds.ToArray())
                {
                    var other = FindUnit(otherId);
                    if (!IsInBattle(unit) || other == null || !IsInBattle(other) || DistanceSquared(unit.Position, other.Position) >= FixedPosition.QuarterMetre * FixedPosition.QuarterMetre) EndBlock(unit, other);
                }
            }
            ReleaseExcessBlockRelations();

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
            foreach (var unit in runtimeUnits.Where(item => IsActive(item) && item.Definition.CanAttack && item.EffectiveAttackIntervalTicks > 0 && !IsAttackAnimationLocked(item) && !IsSkillAnimationLocked(item) && item.NextAttackAllowedTick <= CurrentTick).OrderBy(item => item.UnitId, StringComparer.Ordinal))
            {
                var nextAttackDash = unit.GetAttackDashEffect(
                    unit.StartedAttackCount + 1);
                var target = nextAttackDash != null
                    && unit.IsBlocked
                        ? unit.BlockedUnitIds
                            .Select(FindUnit)
                            .FirstOrDefault(IsActive)
                        : GetAttackTarget(unit);
                if (target == null) continue;
                if (DistanceSquared(unit.Position, target.Position) >= FixedPosition.QuarterMetre * FixedPosition.QuarterMetre) continue;
                var attack = unit.BeginAttackAndGetEffectiveAttack();
                if (unit.ReleasedAlliesOnLastAttack)
                {
                    foreach (var ally in runtimeUnits
                                 .Where(item =>
                                     IsActive(item)
                                     && item.Side == unit.Side)
                                 .OrderBy(
                                     item => item.UnitId,
                                     StringComparer.Ordinal))
                        ally.ForceUnlockAttackCountStates();
                }
                var attackIntervalTicks =
                    unit.EffectiveAttackIntervalTicks;
                var skillAttackAnimation =
                    unit.GetTriggeredAttackSkillAnimation(
                        unit.StartedAttackCount);
                var attackDash = unit.GetAttackDashEffect(
                    unit.StartedAttackCount);
                var originalAnimationTicks =
                    attackDash != null
                        ? attackDash
                            .AnimationOriginalDurationTicks
                        : skillAttackAnimation == null
                        ? unit.Definition
                            .AttackAnimationDurationTicks
                        : skillAttackAnimation
                            .SkillAttackAnimationOriginalDurationTicks;
                var effectiveTicks =
                    attackDash != null
                        ? attackDash
                            .AnimationEffectiveDurationTicks
                        : skillAttackAnimation == null
                        ? Math.Min(
                            originalAnimationTicks,
                            attackIntervalTicks)
                        : skillAttackAnimation
                            .SkillAttackAnimationEffectiveDurationTicks;
                var damageTick = CurrentTick
                    + (attackDash == null
                        ? effectiveTicks
                        : attackDash
                            .MovementDelayEffectiveTicks);
                unit.NextAttackAllowedTick =
                    attackIntervalTicks >= int.MaxValue - CurrentTick
                        ? int.MaxValue
                        : CurrentTick + attackIntervalTicks;
                pendingAttacks.Add(new PendingAttack(unit.UnitId, target.UnitId, damageTick, unit.Definition.DamageType, attack, unit.EffectiveTargetDefenseMultiplierPermille, unit.StartedAttackCount, originalAnimationTicks, effectiveTicks));
                if (attackDash != null)
                {
                    unit.StartTemporaryUnblockable(
                        CurrentTick,
                        attackDash.UnblockableDurationTicks);
                    foreach (var blockedUnitId in unit
                                 .BlockedUnitIds.ToArray())
                        EndBlock(
                            unit,
                            FindUnit(blockedUnitId));
                    pendingRelocations.Add(
                        new PendingRelocation(
                            damageTick,
                            unit.UnitId,
                            target.UnitId,
                            attackDash
                                .DashDistanceCentimetres));
                    unit.SkillAnimationLockUntilTick = Math.Max(
                        unit.SkillAnimationLockUntilTick,
                        CurrentTick + effectiveTicks);
                    Emit(
                        BattleEventType.Skill,
                        unit.UnitId,
                        null,
                        target.UnitId,
                        null,
                        null,
                        unit.Definition.DamageType,
                        0,
                        0,
                        0,
                        damageTick,
                        originalAnimationTicks,
                        effectiveTicks,
                        null,
                        BattleStopReason.None,
                        attackDash.AnimationSequenceKey);
                }
                else if (skillAttackAnimation == null)
                {
                    unit.AttackAnimationLockUntilTick = Math.Max(
                        unit.AttackAnimationLockUntilTick,
                        damageTick);
                    Emit(BattleEventType.Attack, unit.UnitId, null, target.UnitId, null, null, unit.Definition.DamageType, 0, 0, 0, damageTick, originalAnimationTicks, effectiveTicks, null, BattleStopReason.None);
                }
                else
                {
                    unit.SkillAnimationLockUntilTick = Math.Max(
                        unit.SkillAnimationLockUntilTick,
                        damageTick);
                    Emit(BattleEventType.Skill, unit.UnitId, null, target.UnitId, null, null, unit.Definition.DamageType, 0, 0, 0, damageTick, originalAnimationTicks, effectiveTicks, null, BattleStopReason.None, skillAttackAnimation.SkillAttackAnimationKey);
                }
            }
        }

        private void ResolveDueRelocations()
        {
            var due = pendingRelocations
                .Where(item => item.DueTick <= CurrentTick)
                .OrderBy(item => item.DueTick)
                .ThenBy(
                    item => item.UnitId,
                    StringComparer.Ordinal)
                .ToArray();
            pendingRelocations.RemoveAll(item =>
                item.DueTick <= CurrentTick);
            foreach (var pending in due)
            {
                var unit = FindUnit(pending.UnitId);
                if (!IsActive(unit))
                    continue;
                var from = unit.Position;
                var to = DashTowards(
                    from,
                    OpposingGatePosition(unit.Side),
                    pending.DistanceCentimetres);
                if (to.Equals(from))
                    continue;
                unit.Position = to;
                unit.ResetMoveDirectionRemainders();
                Emit(
                    BattleEventType.Move,
                    unit.UnitId,
                    null,
                    pending.TargetUnitId,
                    from,
                    to,
                    null,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    null,
                    BattleStopReason.None);
            }
        }

        private void ResolveDueDamage()
        {
            var due = pendingAttacks.Where(item => item.DamageTick == CurrentTick).OrderBy(item => item.TargetUnitId, StringComparer.Ordinal).ThenBy(item => item.AttackerUnitId, StringComparer.Ordinal).ToArray();
            pendingAttacks.RemoveAll(item => item.DamageTick == CurrentTick);
            foreach (var attackerId in due
                         .Select(item => item.AttackerUnitId)
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(item => item, StringComparer.Ordinal))
            {
                var attacker = FindUnit(attackerId);
                if (attacker != null && IsInBattle(attacker))
                    attacker.CompleteAttack();
            }
            var valid = due
                .Where(item =>
                    IsInBattle(FindUnit(item.AttackerUnitId))
                    && IsInBattle(FindUnit(item.TargetUnitId)))
                .SelectMany(ExpandAttackAreaDamage)
                .ToArray();
            var reactions = new List<DamageReaction>();
            var successfulAttacks =
                new Dictionary<string, PendingAttack>(
                    StringComparer.Ordinal);
            foreach (var targetGroup in valid
                         .GroupBy(
                             item => item.TargetUnitId,
                             StringComparer.Ordinal)
                         .OrderBy(
                             item => item.Key,
                             StringComparer.Ordinal))
            {
                var target = FindUnit(targetGroup.Key);
                var hits = targetGroup.Select(item => new DamageHit(item, CalculateDamage(item, target))).ToArray();
                var before = target.CurrentHitPoints;
                var total = hits.Sum(item => item.Amount);
                target.CurrentHitPoints = Math.Max(0, before - total);
                foreach (var hit in hits) Emit(BattleEventType.Damage, hit.Attack.AttackerUnitId, null, hit.Attack.TargetUnitId, null, null, hit.Attack.DamageType, hit.Amount, before, target.CurrentHitPoints, 0, 0, 0, null, BattleStopReason.None);
                foreach (var hit in hits.Where(item => item.Amount > 0))
                {
                    var attacker =
                        FindUnit(hit.Attack.AttackerUnitId);
                    var attackKey =
                        hit.Attack.AttackerUnitId
                        + "\0"
                        + hit.Attack.AttackOrdinal;
                    if (!successfulAttacks.ContainsKey(attackKey))
                        successfulAttacks.Add(
                            attackKey,
                            hit.Attack);
                    if (target.CurrentHitPoints > 0)
                    {
                        foreach (var abilityState in target.AbilityStates
                                     .OrderBy(
                                         item => item.Definition.AbilityId,
                                         StringComparer.Ordinal))
                        {
                            if (abilityState.RegisterDamageReceived(
                                    out var receivedOrdinal))
                                TrySpawnTriggeredUnit(
                                    target,
                                    abilityState.Definition.AbilityId,
                                    abilityState.Definition
                                        .TriggeredSpawnEffect,
                                    receivedOrdinal);
                        }
                        foreach (var effect in attacker
                                     .OnHitDamageOverTimeEffects
                                     .OrderBy(
                                         item => item.Key,
                                         StringComparer.Ordinal))
                            target.ApplyOrRefreshDamageOverTime(
                                effect.Key,
                                effect.Value.DamagePerSecond,
                                CurrentTick
                                + effect.Value.DurationTicks);
                    }
                    foreach (var effect in attacker
                                 .OnHitDefenseDebuffEffects
                                 .OrderBy(
                                     item => item.Key,
                                     StringComparer.Ordinal))
                        target.ApplyDefenseReduction(
                            effect.Value
                                .DefenseReductionPerStack);
                    foreach (var reaction in target.OnDamageReactions)
                        reactions.Add(new DamageReaction(
                            target.UnitId,
                            hit.Attack.AttackerUnitId,
                            reaction));
                }
            }

            foreach (var attack in successfulAttacks.Values
                         .OrderBy(
                             item => item.AttackerUnitId,
                             StringComparer.Ordinal)
                         .ThenBy(item => item.AttackOrdinal))
            {
                var attacker = FindUnit(attack.AttackerUnitId);
                foreach (var abilityState in attacker.AbilityStates
                             .OrderBy(
                                 item => item.Definition.AbilityId,
                                 StringComparer.Ordinal))
                {
                    var effect =
                        abilityState.Definition.TriggeredSpawnEffect;
                    if (effect == null
                        || effect.TriggerKind
                        != TriggeredSpawnKind.SuccessfulAttack
                        || !effect.IsTriggered(
                            attack.AttackOrdinal))
                        continue;
                    TrySpawnTriggeredUnit(
                        attacker,
                        abilityState.Definition.AbilityId,
                        effect,
                        attack.AttackOrdinal);
                }
            }

            foreach (var reactionGroup in reactions
                         .GroupBy(
                             item => item.TargetUnitId,
                             StringComparer.Ordinal)
                         .OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var target = FindUnit(reactionGroup.Key);
                if (target == null || target.CurrentHitPoints <= 0)
                    continue;
                var resolved = reactionGroup
                    .Select(item => new ResolvedDamageReaction(
                        item,
                        CalculateReactionDamage(
                            item.Effect,
                            target)))
                    .ToArray();
                var before = target.CurrentHitPoints;
                var total = resolved.Sum(item => item.Amount);
                target.CurrentHitPoints = Math.Max(0, before - total);
                foreach (var reaction in resolved)
                    Emit(
                        BattleEventType.Damage,
                        reaction.Reaction.OwnerUnitId,
                        null,
                        reaction.Reaction.TargetUnitId,
                        null,
                        null,
                        reaction.Reaction.Effect.DamageType,
                        reaction.Amount,
                        before,
                        target.CurrentHitPoints,
                        0,
                        0,
                        0,
                        null,
                        BattleStopReason.None);
            }
        }

        private void TrySpawnTriggeredUnit(
            RuntimeUnitState owner,
            string abilityId,
            TriggeredSpawnEffectDefinition effect,
            int triggerOrdinal)
        {
            if (effect.MaxActiveSameType > 0
                && runtimeUnits.Count(item =>
                    IsInBattle(item)
                    && item.Side == owner.Side
                    && string.Equals(
                        item.TypeId,
                        effect.SummonTypeId,
                        StringComparison.Ordinal))
                >= effect.MaxActiveSameType)
                return;
            var definition =
                unitDefinitions[effect.SummonTypeId];
            var offsetX = StableSpawnOffset(
                Input.BattleId,
                owner.UnitId,
                abilityId,
                triggerOrdinal,
                1,
                0,
                effect.SideLengthCentimetres);
            var offsetY = StableSpawnOffset(
                Input.BattleId,
                owner.UnitId,
                abilityId,
                triggerOrdinal,
                1,
                1,
                effect.SideLengthCentimetres);
            var summoned = new RuntimeUnitState(
                dynamicUnitIdAllocator.Allocate(),
                owner.PlayerId,
                owner.Side,
                definition,
                new FixedPosition(
                    owner.Position.XUnits + offsetX,
                    owner.Position.YUnits + offsetY),
                0,
                Array.Empty<BuffPlaceholder>(),
                CurrentTick + 1,
                CreateAbilityStates(
                    definition,
                    abilityDefinitions));
            runtimeUnits.Add(summoned);
            var snapshot = CreateSpawnSnapshot(summoned, true);
            unitSnapshots.Add(summoned.UnitId, snapshot);
            EmitSpawn(summoned, snapshot);
        }

        private void SpawnHealthThresholdAdjacentUnits(
            RuntimeUnitState owner,
            HealthThresholdAdjacentSpawnEffectDefinition effect)
        {
            var centre =
                NearestBattlefieldCoordinate(owner.Position);
            var candidates = new[]
            {
                new[] { centre.X - 1, centre.Y },
                new[] { centre.X + 1, centre.Y },
                new[] { centre.X, centre.Y - 1 },
                new[] { centre.X, centre.Y + 1 }
            };
            var definition =
                unitDefinitions[effect.SummonTypeId];
            foreach (var candidate in candidates)
            {
                if (!BattlefieldCoordinate.TryCreate(
                        candidate[0],
                        candidate[1],
                        out var coordinate)
                    || !BattlefieldRules.IsDeployable(coordinate))
                    continue;
                var summoned = new RuntimeUnitState(
                    dynamicUnitIdAllocator.Allocate(),
                    owner.PlayerId,
                    owner.Side,
                    definition,
                    FixedPosition.FromCell(coordinate),
                    0,
                    Array.Empty<BuffPlaceholder>(),
                    CurrentTick + 1,
                    CreateAbilityStates(
                        definition,
                        abilityDefinitions));
                runtimeUnits.Add(summoned);
                var snapshot =
                    CreateSpawnSnapshot(summoned, true);
                unitSnapshots.Add(summoned.UnitId, snapshot);
                EmitSpawn(summoned, snapshot);
            }
        }

        private IEnumerable<PendingAttack> ExpandAttackAreaDamage(
            PendingAttack attack)
        {
            var attacker = FindUnit(attack.AttackerUnitId);
            var primaryTarget = FindUnit(attack.TargetUnitId);
            var modifiers = attacker.AttackAreaDamageModifiers
                .Where(item =>
                    item.Value.IsAreaAttack(
                        attack.AttackOrdinal))
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToArray();
            if (modifiers.Length == 0)
                return new[] { attack };

            var expanded = new List<PendingAttack>();
            foreach (var ability in modifiers)
            {
                var targets = runtimeUnits
                    .Where(target =>
                        IsActive(target)
                        && target.Side != attacker.Side
                        && IsInAttackArea(
                            primaryTarget.Position,
                            target.Position,
                            ability.Value))
                    .OrderBy(
                        target => target.UnitId,
                        StringComparer.Ordinal);
                foreach (var target in targets)
                {
                    var scaledAttack = (int)Math.Min(
                        int.MaxValue,
                        (long)attack.Attack
                        * ability.Value.AttackMultiplierPermille
                        / 1000);
                    expanded.Add(new PendingAttack(
                        attack.AttackerUnitId,
                        target.UnitId,
                        attack.DamageTick,
                        ability.Value.DamageType,
                        scaledAttack,
                        attack.TargetDefenseMultiplierPermille,
                        attack.AttackOrdinal,
                        attack.OriginalTicks,
                        attack.EffectiveTicks));
                }
            }
            return expanded;
        }

        private static bool IsInAttackArea(
            FixedPosition centre,
            FixedPosition candidate,
            AttackAreaDamageModifierDefinition modifier)
        {
            if (modifier.Shape == AttackAreaShape.Radius)
                return DistanceSquared(centre, candidate)
                    <= (long)modifier.RadiusCentimetres
                       * modifier.RadiusCentimetres;
            var centreCell =
                NearestBattlefieldCoordinate(centre);
            var candidateCell =
                NearestBattlefieldCoordinate(candidate);
            return Math.Abs(centreCell.X - candidateCell.X)
                   + Math.Abs(centreCell.Y - candidateCell.Y)
                   <= 1;
        }

        private void ResolveDeathsAndCleanup()
        {
            foreach (var unit in runtimeUnits.Where(item => item.IsAlive && item.CurrentHitPoints <= 0).OrderBy(item => item.UnitId, StringComparer.Ordinal).ToArray())
            {
                unit.IsAlive = false;
                Emit(BattleEventType.Death, unit.UnitId, null, null, null, null, null, 0, 0, 0, 0, 0, 0, null, BattleStopReason.None);
                foreach (var effect in unit.DeathSpawnEffects)
                    pendingDeathSpawns.Add(new PendingDeathSpawn(
                        CurrentTick + effect.Value.DelayTicks,
                        unit.UnitId,
                        unit.PlayerId,
                        unit.Side,
                        unit.Position,
                        effect.Key,
                        effect.Value));
                foreach (var effect in unit.DeathAreaDamageEffects)
                    pendingDeathAreaDamages.Add(
                        new PendingDeathAreaDamage(
                            CurrentTick + effect.Value.DelayTicks,
                            unit.UnitId,
                            unit.Side,
                            unit.Position,
                            unit.EffectiveAttack,
                            effect.Key,
                            effect.Value));
            }
            foreach (var unit in runtimeUnits.Where(item => !item.IsAlive).OrderBy(item => item.UnitId, StringComparer.Ordinal))
            {
                foreach (var otherId in unit.BlockedUnitIds.ToArray()) EndBlock(unit, FindUnit(otherId));
                foreach (var other in runtimeUnits.Where(item => IsInBattle(item) && item.TargetUnitId == unit.UnitId).OrderBy(item => item.UnitId, StringComparer.Ordinal)) SetTarget(other, null);
            }
            pendingAttacks.RemoveAll(item =>
                !IsInBattle(FindUnit(item.AttackerUnitId)));
        }

        private void ResolveDueDeathSpawns()
        {
            var due = pendingDeathSpawns
                .Where(item => item.DueTick <= CurrentTick)
                .OrderBy(item => item.DueTick)
                .ThenBy(item => item.OwnerUnitId, StringComparer.Ordinal)
                .ThenBy(item => item.AbilityId, StringComparer.Ordinal)
                .ToArray();
            pendingDeathSpawns.RemoveAll(item =>
                item.DueTick <= CurrentTick);
            foreach (var pending in due)
            {
                var origin = pending.Effect.SnapToNearestPassableCell
                    ? NearestPassableCellCentre(pending.Position)
                    : pending.Position;
                for (var spawnOrdinal = 1;
                     spawnOrdinal <= pending.Effect.Count;
                     spawnOrdinal++)
                {
                    var typeId = SelectDeathSpawnType(
                        pending,
                        spawnOrdinal);
                    var definition = unitDefinitions[typeId];
                    var offsetX = StableSpawnOffset(
                        Input.BattleId,
                        pending.OwnerUnitId,
                        pending.AbilityId,
                        pending.DueTick,
                        spawnOrdinal,
                        0,
                        pending.Effect.SideLengthCentimetres);
                    var offsetY = StableSpawnOffset(
                        Input.BattleId,
                        pending.OwnerUnitId,
                        pending.AbilityId,
                        pending.DueTick,
                        spawnOrdinal,
                        1,
                        pending.Effect.SideLengthCentimetres);
                    var summoned = new RuntimeUnitState(
                        dynamicUnitIdAllocator.Allocate(),
                        pending.PlayerId,
                        pending.Side,
                        definition,
                        new FixedPosition(
                            origin.XUnits + offsetX,
                            origin.YUnits + offsetY),
                        0,
                        Array.Empty<BuffPlaceholder>(),
                        CurrentTick + 1,
                        CreateAbilityStates(
                            definition,
                            abilityDefinitions),
                        pending.Effect
                            .SummonedMoveSpeedMultiplierPermille);
                    runtimeUnits.Add(summoned);
                    var snapshot =
                        CreateSpawnSnapshot(summoned, true);
                    unitSnapshots.Add(summoned.UnitId, snapshot);
                    EmitSpawn(summoned, snapshot);
                }
            }
        }

        private void ResolveDueDeathAreaDamage()
        {
            var due = pendingDeathAreaDamages
                .Where(item => item.DueTick <= CurrentTick)
                .OrderBy(item => item.DueTick)
                .ThenBy(
                    item => item.OwnerUnitId,
                    StringComparer.Ordinal)
                .ThenBy(
                    item => item.AbilityId,
                    StringComparer.Ordinal)
                .ToArray();
            pendingDeathAreaDamages.RemoveAll(item =>
                item.DueTick <= CurrentTick);
            var hits = due
                .SelectMany(pending => runtimeUnits
                    .Where(target =>
                        IsActive(target)
                        && target.Side != pending.Side
                        && DistanceSquared(
                            pending.Position,
                            target.Position)
                        <= (long)pending.Effect.RadiusCentimetres
                           * pending.Effect.RadiusCentimetres)
                    .OrderBy(
                        target => target.UnitId,
                        StringComparer.Ordinal)
                    .Select(target =>
                        new DeathAreaDamageHit(
                            pending,
                            target.UnitId,
                            CalculateDeathAreaDamage(
                                pending,
                                target))))
                .ToArray();
            foreach (var group in hits
                         .GroupBy(
                             item => item.TargetUnitId,
                             StringComparer.Ordinal)
                         .OrderBy(
                             item => item.Key,
                             StringComparer.Ordinal))
            {
                var target = FindUnit(group.Key);
                if (target == null || !IsInBattle(target))
                    continue;
                var resolved = group.ToArray();
                var before = target.CurrentHitPoints;
                var total = resolved.Sum(item =>
                    (long)item.Amount);
                target.CurrentHitPoints = (int)Math.Max(
                    0,
                    before - Math.Min(int.MaxValue, total));
                foreach (var hit in resolved)
                    Emit(
                        BattleEventType.Damage,
                        hit.Pending.OwnerUnitId,
                        null,
                        hit.TargetUnitId,
                        null,
                        null,
                        hit.Pending.Effect.DamageType,
                        hit.Amount,
                        before,
                        target.CurrentHitPoints,
                        0,
                        0,
                        0,
                        null,
                        BattleStopReason.None);
            }
        }

        private static int CalculateDeathAreaDamage(
            PendingDeathAreaDamage pending,
            RuntimeUnitState target)
        {
            var scaledAttack = (int)Math.Min(
                int.MaxValue,
                (long)pending.Attack
                * pending.Effect.AttackMultiplierPermille
                / 1000);
            var damage = DamageCalculator.Calculate(
                pending.Effect.DamageType,
                scaledAttack,
                target.EffectiveDefense,
                target.EffectiveMagicResistance);
            return target.ApplyDamageTakenModifiers(
                pending.Effect.DamageType,
                damage);
        }

        private void RefreshAuraCombatModifiers()
        {
            var contributions = new List<AuraContribution>();
            foreach (var source in runtimeUnits
                         .Where(IsActive)
                         .OrderBy(item => item.UnitId, StringComparer.Ordinal))
            foreach (var ability in source.AuraCombatModifiers
                         .OrderBy(item => item.Key, StringComparer.Ordinal))
            foreach (var target in runtimeUnits
                         .Where(IsActive)
                         .OrderBy(item => item.UnitId, StringComparer.Ordinal))
            {
                var modifier = ability.Value;
                if (modifier.ExcludeSource
                    && ReferenceEquals(source, target))
                    continue;
                var sideMatches =
                    modifier.TargetSide == AuraTargetSide.Allies
                        ? source.Side == target.Side
                        : source.Side != target.Side;
                if (!sideMatches)
                    continue;
                if (!modifier.IsGlobal
                    && DistanceSquared(source.Position, target.Position)
                    > (long)modifier.RadiusCentimetres
                    * modifier.RadiusCentimetres)
                    continue;
                contributions.Add(new AuraContribution(
                    source.UnitId,
                    target.UnitId,
                    ability.Key,
                    modifier));
            }
            foreach (var source in runtimeUnits
                         .Where(IsActive)
                         .OrderBy(item => item.UnitId, StringComparer.Ordinal))
            foreach (var ability in source
                         .BlockedCounterpartCombatModifiers
                         .OrderBy(item => item.Key, StringComparer.Ordinal))
            foreach (var counterpartId in source.BlockedUnitIds
                         .OrderBy(item => item, StringComparer.Ordinal))
            {
                var counterpart = FindUnit(counterpartId);
                if (counterpart == null
                    || !IsActive(counterpart)
                    || counterpart.Side == source.Side)
                    continue;
                contributions.Add(new AuraContribution(
                    source.UnitId,
                    counterpart.UnitId,
                    ability.Key,
                    ability.Value));
            }
            foreach (var source in runtimeUnits
                         .Where(IsActive)
                         .OrderBy(item => item.UnitId, StringComparer.Ordinal))
            foreach (var ability in source
                         .NearbySameTypeSelfModifiers
                         .OrderBy(item => item.Key, StringComparer.Ordinal))
            foreach (var neighbour in runtimeUnits
                         .Where(item =>
                             IsActive(item)
                             && !ReferenceEquals(item, source)
                             && item.Side == source.Side
                             && string.Equals(
                                 item.TypeId,
                                 source.TypeId,
                                 StringComparison.Ordinal))
                         .OrderBy(item => item.UnitId, StringComparer.Ordinal))
            {
                if (DistanceSquared(source.Position, neighbour.Position)
                    > (long)ability.Value.RadiusCentimetres
                    * ability.Value.RadiusCentimetres)
                    continue;
                contributions.Add(new AuraContribution(
                    source.UnitId,
                    source.UnitId,
                    ability.Key,
                    ability.Value));
            }

            foreach (var target in runtimeUnits)
            {
                var accepted =
                    new List<IExternalCombatModifierDefinition>();
                var nonStackingAbilityIds =
                    new HashSet<string>(StringComparer.Ordinal);
                foreach (var contribution in contributions
                             .Where(item =>
                                 string.Equals(
                                     item.TargetUnitId,
                                     target.UnitId,
                                     StringComparison.Ordinal))
                             .OrderBy(item =>
                                 item.SourceUnitId,
                                 StringComparer.Ordinal)
                             .ThenBy(item =>
                                 item.AbilityId,
                                 StringComparer.Ordinal))
                {
                    if (contribution.Modifier
                            .NonStackingByAbilityId
                        && !nonStackingAbilityIds.Add(
                            contribution.AbilityId))
                        continue;
                    accepted.Add(contribution.Modifier);
                }
                target.SetAuraCombatModifiers(accepted);
            }
        }

        private void UpdateHealthThresholdStates()
        {
            foreach (var unit in runtimeUnits
                         .Where(IsActive)
                         .OrderBy(item => item.UnitId, StringComparer.Ordinal)
                         .ToArray())
            foreach (var ability in unit.AbilityStates
                         .OrderBy(
                             item => item.Definition.AbilityId,
                             StringComparer.Ordinal))
            {
                ability.UpdateHealthThresholdState(
                    unit.CurrentHitPoints,
                    unit.Definition.MaxHitPoints,
                    CurrentTick);
                if (ability.TryTriggerHealthThresholdAdjacentSpawn(
                        unit.CurrentHitPoints,
                        unit.Definition.MaxHitPoints))
                    SpawnHealthThresholdAdjacentUnits(
                        unit,
                        ability.Definition
                            .HealthThresholdAdjacentSpawnEffect);
                ability.QueueHealthThresholdFullHeal(
                    unit.CurrentHitPoints,
                    unit.Definition.MaxHitPoints);
            }
            ReleaseExcessBlockRelations();
            StartReadyHealthThresholdFullHeals();
        }

        private void ResolveDueHealthThresholdFullHeals()
        {
            foreach (var unit in runtimeUnits
                         .Where(IsActive)
                         .OrderBy(
                             item => item.UnitId,
                             StringComparer.Ordinal))
            foreach (var ability in unit.AbilityStates
                         .OrderBy(
                             item => item.Definition.AbilityId,
                             StringComparer.Ordinal))
            {
                if (!ability.TryCompleteHealthThresholdFullHeal(
                        CurrentTick))
                    continue;
                var before = unit.CurrentHitPoints;
                unit.CurrentHitPoints =
                    unit.Definition.MaxHitPoints;
                var amount = unit.CurrentHitPoints - before;
                if (amount <= 0)
                    continue;
                Emit(
                    BattleEventType.HealthChanged,
                    unit.UnitId,
                    null,
                    null,
                    null,
                    null,
                    null,
                    amount,
                    before,
                    unit.CurrentHitPoints,
                    0,
                    0,
                    0,
                    null,
                    BattleStopReason.None);
            }
        }

        private void StartReadyHealthThresholdFullHeals()
        {
            foreach (var caster in runtimeUnits
                         .Where(IsActive)
                         .OrderBy(
                             item => item.UnitId,
                             StringComparer.Ordinal))
            foreach (var ability in caster.AbilityStates
                         .OrderBy(
                             item => item.Definition.AbilityId,
                             StringComparer.Ordinal))
            {
                if (IsAttackAnimationLocked(caster)
                    || IsSkillAnimationLocked(caster)
                    || !ability.TryStartHealthThresholdFullHeal(
                        CurrentTick,
                        out var effect))
                    continue;
                caster.SkillAnimationLockUntilTick =
                    CurrentTick
                    + effect.AnimationEffectiveDurationTicks;
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
                    effect.AnimationOriginalDurationTicks,
                    effect.AnimationEffectiveDurationTicks,
                    null,
                    BattleStopReason.None,
                    effect.AnimationKey);
            }
        }

        private void ReleaseExcessBlockRelations()
        {
            foreach (var unit in runtimeUnits
                         .Where(item =>
                             item.BlockedUnitIds.Count
                             > item.EffectiveBlockCapacity)
                         .OrderBy(
                             item => item.UnitId,
                             StringComparer.Ordinal)
                         .ToArray())
            foreach (var releasedUnitId in unit.BlockedUnitIds
                         .Skip(unit.EffectiveBlockCapacity)
                         .ToArray())
                EndBlock(unit, FindUnit(releasedUnitId));
        }

        private void UpdateUnblockedAttackCharges()
        {
            foreach (var unit in runtimeUnits
                         .Where(IsActive)
                         .OrderBy(
                             item => item.UnitId,
                             StringComparer.Ordinal))
                unit.UpdateUnblockedAttackCharges(CurrentTick);
        }

        private void ApplyPassiveLifecycleEffects()
        {
            foreach (var unit in runtimeUnits
                         .Where(IsActive)
                         .OrderBy(item => item.UnitId, StringComparer.Ordinal))
            {
                unit.ExpireDamageOverTime(CurrentTick);
                var lifetimeTicks = unit.PassiveLifetimeTicks;
                if (lifetimeTicks > 0
                    && CurrentTick - unit.ActivationTick >= lifetimeTicks)
                {
                    var before = unit.CurrentHitPoints;
                    unit.CurrentHitPoints = 0;
                    Emit(
                        BattleEventType.HealthChanged,
                        unit.UnitId,
                        null,
                        null,
                        null,
                        null,
                        null,
                        before,
                        before,
                        0,
                        0,
                        0,
                        0,
                        null,
                        BattleStopReason.None);
                    continue;
                }

                var rate = (int)Math.Max(
                    int.MinValue + 1L,
                    (long)unit.PassiveHitPointsPerSecond
                    - unit.ActiveDamageOverTimePerSecond);
                if (rate == 0)
                    continue;
                unit.PassiveHealthRemainder += Math.Abs(rate);
                var magnitude =
                    unit.PassiveHealthRemainder
                    / BattleInput.TicksPerSecond;
                unit.PassiveHealthRemainder %=
                    BattleInput.TicksPerSecond;
                if (magnitude == 0)
                    continue;
                var beforeHitPoints = unit.CurrentHitPoints;
                unit.CurrentHitPoints = rate > 0
                    ? Math.Min(
                        unit.Definition.MaxHitPoints,
                        unit.CurrentHitPoints + magnitude)
                    : Math.Max(0, unit.CurrentHitPoints - magnitude);
                if (unit.CurrentHitPoints == beforeHitPoints)
                    continue;
                Emit(
                    BattleEventType.HealthChanged,
                    unit.UnitId,
                    null,
                    null,
                    null,
                    null,
                    rate < 0 ? DamageType.True : (DamageType?)null,
                    magnitude,
                    beforeHitPoints,
                    unit.CurrentHitPoints,
                    0,
                    0,
                    0,
                    null,
                    BattleStopReason.None);
            }
        }

        private void EvaluateBattleEnd()
        {
            var homeAlive = runtimeUnits.Any(item =>
                                IsInBattle(item)
                                && item.Side == BattleSide.Home)
                            || pendingDeathSpawns.Any(item =>
                                item.Side == BattleSide.Home)
                            || pendingDeathAreaDamages.Any(item =>
                                item.Side == BattleSide.Home);
            var awayAlive = runtimeUnits.Any(item =>
                                IsInBattle(item)
                                && item.Side == BattleSide.Away)
                            || pendingDeathSpawns.Any(item =>
                                item.Side == BattleSide.Away)
                            || pendingDeathAreaDamages.Any(item =>
                                item.Side == BattleSide.Away);
            if (homeAlive && awayAlive) return;

            var homeLoss = CalculateLifeLoss(BattleSide.Home);
            var awayLoss = CalculateLifeLoss(BattleSide.Away);
            if (homeLoss == awayLoss)
            {
                EndBattle(BattleStopReason.MutualAnnihilation, null);
                return;
            }
            EndBattle(
                BattleStopReason.Victory,
                homeLoss < awayLoss
                    ? BattleSide.Home
                    : BattleSide.Away);
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
            StartReadyHealthThresholdFullHeals();
            foreach (var caster in runtimeUnits.Where(IsActive).OrderBy(item => item.UnitId, StringComparer.Ordinal).ToArray())
            foreach (var abilityState in caster.AbilityStates.OrderBy(item => item.Definition.AbilityId, StringComparer.Ordinal))
            {
                if (abilityState.Definition.ActivationKind != AbilityActivationKind.Timed) continue;
                if (abilityState.Definition.SkillPointGeneration != SkillPointGeneration.Automatic) continue;
                if (!abilityState.CanCast
                    || IsAttackAnimationLocked(caster)
                    || IsSkillAnimationLocked(caster))
                    continue;
                var blinkEffect =
                    abilityState.Definition.TimedBlinkEffect;
                if (blinkEffect != null && !caster.IsBlocked)
                    continue;
                var targetAreaEffect =
                    abilityState.Definition.TimedTargetAreaDamageEffect;
                var target = targetAreaEffect == null
                    ? null
                    : SelectTimedTargetAreaDamageTarget(
                        caster,
                        targetAreaEffect);
                if (targetAreaEffect != null && target == null)
                    continue;
                var blinkTarget = blinkEffect == null
                    ? null
                    : caster.BlockedUnitIds
                        .Select(FindUnit)
                        .FirstOrDefault(IsActive);
                if (blinkEffect != null && blinkTarget == null)
                    continue;
                var castOrdinal = abilityState.ConsumeCast();
                caster.SkillAnimationLockUntilTick =
                    CurrentTick
                    + abilityState.Definition.SkillAnimationEffectiveDurationTicks;
                if (blinkEffect != null)
                {
                    caster.StartTemporaryUnblockable(
                        CurrentTick,
                        abilityState.Definition
                            .SkillAnimationEffectiveDurationTicks);
                    foreach (var blockedUnitId in caster
                                 .BlockedUnitIds.ToArray())
                        EndBlock(
                            caster,
                            FindUnit(blockedUnitId));
                    pendingRelocations.Add(
                        new PendingRelocation(
                            CurrentTick
                            + blinkEffect
                                .RelocationDelayEffectiveTicks,
                            caster.UnitId,
                            blinkTarget.UnitId,
                            blinkEffect.DistanceCentimetres));
                }
                Emit(
                    BattleEventType.Skill,
                    caster.UnitId,
                    null,
                    blinkTarget != null
                        ? blinkTarget.UnitId
                        : target == null
                            ? null
                            : target.UnitId,
                    null,
                    null,
                    targetAreaEffect == null
                        ? (DamageType?)null
                        : targetAreaEffect.DamageType,
                    0,
                    0,
                    0,
                    0,
                    abilityState.Definition.SkillAnimationOriginalDurationTicks,
                    abilityState.Definition.SkillAnimationEffectiveDurationTicks,
                    null,
                    BattleStopReason.None,
                    abilityState.Definition.AnimationKey);
                if (abilityState.Definition.SummonEffect != null)
                    CastSummonAbility(
                        caster,
                        abilityState.Definition,
                        castOrdinal);
                else if (targetAreaEffect != null)
                    pendingTimedTargetAreaDamages.Add(
                        new PendingTimedTargetAreaDamage(
                            CurrentTick
                            + abilityState.Definition
                                .SkillAnimationEffectiveDurationTicks,
                            caster.UnitId,
                            caster.Side,
                            target.UnitId,
                            caster.EffectiveAttack,
                            abilityState.Definition.AbilityId,
                            targetAreaEffect));
            }
        }

        private RuntimeUnitState SelectTimedTargetAreaDamageTarget(
            RuntimeUnitState caster,
            TimedTargetAreaDamageEffectDefinition effect)
        {
            var maximumDistanceSquared =
                (long)effect.TargetRangeCentimetres
                * effect.TargetRangeCentimetres;
            return runtimeUnits
                .Where(candidate =>
                    IsActive(candidate)
                    && candidate.Side != caster.Side
                    && (!effect.GroundTargetsOnly
                        || candidate.IsTargetableBy(
                            AttackMethod.Melee))
                    && DistanceSquared(
                        caster.Position,
                        candidate.Position)
                    <= maximumDistanceSquared)
                .OrderBy(candidate =>
                    caster.HasBlockWith(candidate.UnitId)
                        ? 0
                        : 1)
                .ThenBy(candidate =>
                    DistanceSquared(
                        caster.Position,
                        candidate.Position))
                .ThenBy(candidate =>
                    DistanceSquared(
                        candidate.Position,
                        GatePosition(caster.Side)))
                .ThenBy(
                    candidate => candidate.UnitId,
                    StringComparer.Ordinal)
                .FirstOrDefault();
        }

        private void ResolveDueTimedTargetAreaDamage()
        {
            var due = pendingTimedTargetAreaDamages
                .Where(item => item.DueTick <= CurrentTick)
                .OrderBy(item => item.DueTick)
                .ThenBy(
                    item => item.OwnerUnitId,
                    StringComparer.Ordinal)
                .ThenBy(
                    item => item.AbilityId,
                    StringComparer.Ordinal)
                .ToArray();
            pendingTimedTargetAreaDamages.RemoveAll(item =>
                item.DueTick <= CurrentTick);
            var hits = due
                .Where(pending =>
                    IsInBattle(FindUnit(pending.OwnerUnitId))
                    && IsActive(FindUnit(pending.TargetUnitId)))
                .SelectMany(pending =>
                {
                    var centre =
                        FindUnit(pending.TargetUnitId).Position;
                    return runtimeUnits
                        .Where(target =>
                            IsActive(target)
                            && target.Side != pending.Side
                            && (!pending.Effect.GroundTargetsOnly
                                || target.IsTargetableBy(
                                    AttackMethod.Melee))
                            && DistanceSquared(
                                centre,
                                target.Position)
                            <= (long)pending.Effect
                                   .RadiusCentimetres
                               * pending.Effect
                                   .RadiusCentimetres)
                        .OrderBy(
                            target => target.UnitId,
                            StringComparer.Ordinal)
                        .Select(target =>
                            new TimedTargetAreaDamageHit(
                                pending,
                                target.UnitId,
                                CalculateTimedTargetAreaDamage(
                                    pending,
                                    target)));
                })
                .ToArray();
            foreach (var group in hits
                         .GroupBy(
                             item => item.TargetUnitId,
                             StringComparer.Ordinal)
                         .OrderBy(
                             item => item.Key,
                             StringComparer.Ordinal))
            {
                var target = FindUnit(group.Key);
                if (target == null || !IsInBattle(target))
                    continue;
                var resolved = group.ToArray();
                var before = target.CurrentHitPoints;
                var total = resolved.Sum(item =>
                    (long)item.Amount);
                target.CurrentHitPoints = (int)Math.Max(
                    0,
                    before - Math.Min(int.MaxValue, total));
                foreach (var hit in resolved)
                    Emit(
                        BattleEventType.Damage,
                        hit.Pending.OwnerUnitId,
                        null,
                        hit.TargetUnitId,
                        null,
                        null,
                        hit.Pending.Effect.DamageType,
                        hit.Amount,
                        before,
                        target.CurrentHitPoints,
                        0,
                        0,
                        0,
                        null,
                        BattleStopReason.None);
            }
        }

        private void ResolveProximityEntryDamage()
        {
            var hits = new List<ProximityEntryDamageHit>();
            foreach (var source in runtimeUnits
                         .Where(IsActive)
                         .OrderBy(
                             item => item.UnitId,
                             StringComparer.Ordinal))
            foreach (var abilityState in source.AbilityStates
                         .Where(item =>
                             item.Definition
                                 .ProximityEntryDamageEffect != null)
                         .OrderBy(
                             item => item.Definition.AbilityId,
                             StringComparer.Ordinal))
            {
                var effect =
                    abilityState.Definition
                        .ProximityEntryDamageEffect;
                var radiusSquared =
                    (long)effect.RadiusCentimetres
                    * effect.RadiusCentimetres;
                var inside = runtimeUnits
                    .Where(target =>
                        IsActive(target)
                        && target.Side != source.Side
                        && (!effect.GroundTargetsOnly
                            || target.IsTargetableBy(
                                AttackMethod.Melee))
                        && DistanceSquared(
                            source.Position,
                            target.Position)
                           <= radiusSquared)
                    .OrderBy(
                        target => target.UnitId,
                        StringComparer.Ordinal)
                    .Select(target => target.UnitId)
                    .ToArray();
                foreach (var targetUnitId in abilityState
                             .UpdateProximityEntryTargets(inside))
                {
                    var target = FindUnit(targetUnitId);
                    if (!IsActive(target))
                        continue;
                    hits.Add(new ProximityEntryDamageHit(
                        source.UnitId,
                        targetUnitId,
                        effect,
                        CalculateProximityEntryDamage(
                            source,
                            target,
                            effect)));
                }
            }

            foreach (var group in hits
                         .GroupBy(
                             item => item.TargetUnitId,
                             StringComparer.Ordinal)
                         .OrderBy(
                             item => item.Key,
                             StringComparer.Ordinal))
            {
                var target = FindUnit(group.Key);
                if (target == null || !IsInBattle(target))
                    continue;
                var resolved = group
                    .OrderBy(
                        item => item.SourceUnitId,
                        StringComparer.Ordinal)
                    .ToArray();
                var before = target.CurrentHitPoints;
                var total = resolved.Sum(item =>
                    (long)item.Amount);
                target.CurrentHitPoints = (int)Math.Max(
                    0,
                    before - Math.Min(int.MaxValue, total));
                foreach (var hit in resolved)
                    Emit(
                        BattleEventType.Damage,
                        hit.SourceUnitId,
                        null,
                        hit.TargetUnitId,
                        null,
                        null,
                        hit.Effect.DamageType,
                        hit.Amount,
                        before,
                        target.CurrentHitPoints,
                        0,
                        0,
                        0,
                        null,
                        BattleStopReason.None);
            }
        }

        private static int CalculateProximityEntryDamage(
            RuntimeUnitState source,
            RuntimeUnitState target,
            ProximityEntryDamageEffectDefinition effect)
        {
            var scaledAttack = (int)Math.Min(
                int.MaxValue,
                (long)source.EffectiveAttack
                * effect.AttackMultiplierPermille
                / 1000);
            var damage = DamageCalculator.Calculate(
                effect.DamageType,
                scaledAttack,
                target.EffectiveDefense,
                target.EffectiveMagicResistance);
            return target.ApplyDamageTakenModifiers(
                effect.DamageType,
                damage);
        }

        private static int CalculateTimedTargetAreaDamage(
            PendingTimedTargetAreaDamage pending,
            RuntimeUnitState target)
        {
            var scaledAttack = (int)Math.Min(
                int.MaxValue,
                (long)pending.Attack
                * pending.Effect.AttackMultiplierPermille
                / 1000);
            var damage = DamageCalculator.Calculate(
                pending.Effect.DamageType,
                scaledAttack,
                target.EffectiveDefense,
                target.EffectiveMagicResistance);
            return target.ApplyDamageTakenModifiers(
                pending.Effect.DamageType,
                damage);
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

        private string SelectDeathSpawnType(
            PendingDeathSpawn pending,
            int spawnOrdinal)
        {
            var totalWeight = pending.Effect.Options.Sum(item =>
                item.Weight);
            var hash = 14695981039346656037UL;
            hash = AppendStableHash(hash, Input.BattleId);
            hash = AppendStableHash(hash, pending.OwnerUnitId);
            hash = AppendStableHash(hash, pending.AbilityId);
            hash = AppendStableHash(hash, pending.DueTick);
            hash = AppendStableHash(hash, spawnOrdinal);
            hash = AppendStableHash(hash, 2);
            var selected = (int)(hash % (ulong)totalWeight);
            foreach (var option in pending.Effect.Options)
            {
                if (selected < option.Weight)
                    return option.SummonTypeId;
                selected -= option.Weight;
            }
            throw new InvalidOperationException(
                "Death-spawn weighted selection exhausted options.");
        }

        private static FixedPosition NearestPassableCellCentre(
            FixedPosition position)
        {
            return Enumerable
                .Range(1, BattlefieldCoordinate.Width)
                .SelectMany(x => Enumerable
                    .Range(1, BattlefieldCoordinate.Height)
                    .Select(y =>
                        new BattlefieldCoordinate(x, y)))
                .Where(item => BattlefieldRules.IsDeployable(item))
                .Select(item => FixedPosition.FromCell(item))
                .OrderBy(item => DistanceSquared(item, position))
                .ThenBy(item => item.XUnits)
                .ThenBy(item => item.YUnits)
                .First();
        }

        private static BattlefieldCoordinate
            NearestBattlefieldCoordinate(FixedPosition position)
        {
            return Enumerable
                .Range(1, BattlefieldCoordinate.Width)
                .SelectMany(x => Enumerable
                    .Range(1, BattlefieldCoordinate.Height)
                    .Select(y =>
                        new BattlefieldCoordinate(x, y)))
                .OrderBy(item => DistanceSquared(
                    FixedPosition.FromCell(item),
                    position))
                .ThenBy(item => item.X)
                .ThenBy(item => item.Y)
                .First();
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
            HomeLifeLoss = CalculateLifeLoss(BattleSide.Home);
            AwayLifeLoss = CalculateLifeLoss(BattleSide.Away);
            Winner = winner;
            StopReason = reason;
            Status = BattleRunnerStatus.Stopped;
            Emit(BattleEventType.BattleEnded, null, null, null, null, null, null, 0, 0, 0, 0, 0, 0, winner, reason);
        }

        private int CalculateLifeLoss(BattleSide damagedSide)
        {
            var result = damagedSide == BattleSide.Home
                ? homeGateLifeLoss
                : awayGateLifeLoss;
            var attackingSide = damagedSide == BattleSide.Home
                ? BattleSide.Away
                : BattleSide.Home;
            foreach (var unit in runtimeUnits
                         .Where(item =>
                             IsInBattle(item)
                             && item.Side == attackingSide)
                         .OrderBy(item => item.UnitId, StringComparer.Ordinal))
                result = SaturatingAdd(
                    result,
                    unit.Definition.LifeDeduct);
            return result;
        }

        private static int SaturatingAdd(int first, int second)
        {
            var sum = (long)first + second;
            return sum >= int.MaxValue
                ? int.MaxValue
                : (int)sum;
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

        private static bool IsInBattle(RuntimeUnitState unit) =>
            unit != null
            && unit.IsAlive
            && !unit.HasExitedBattle;
        private bool IsActive(RuntimeUnitState unit) =>
            IsInBattle(unit)
            && unit.ActivationTick <= CurrentTick;
        private bool HasLiveTarget(RuntimeUnitState unit) =>
            unit.TargetUnitId != null
            && IsActive(FindUnit(unit.TargetUnitId));
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
                    if (IsActive(blocker)) return blocker;
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
        private static bool IsInsideOpposingGateArea(
            RuntimeUnitState unit)
        {
            var gate = OpposingGatePosition(unit.Side);
            return Math.Abs(unit.Position.XUnits - gate.XUnits)
                       <= OpposingGateHalfExtentUnits
                   && Math.Abs(unit.Position.YUnits - gate.YUnits)
                       <= OpposingGateHalfExtentUnits;
        }
        private static long DistanceSquared(FixedPosition first, FixedPosition second) { var x = (long)first.XUnits - second.XUnits; var y = (long)first.YUnits - second.YUnits; return x * x + y * y; }
        private static bool IsInAttackRange(FixedPosition first, FixedPosition second) => DistanceSquared(first, second) < FixedPosition.QuarterMetre * FixedPosition.QuarterMetre;
        private int CalculateDamage(PendingAttack attack, RuntimeUnitState target)
        {
            if (IsAttackEvaded(attack, target))
                return 0;
            var damage = DamageCalculator.Calculate(
                attack.DamageType,
                attack.Attack,
                ApplyDefenseMultiplier(
                    target.EffectiveDefense,
                    attack.TargetDefenseMultiplierPermille),
                target.EffectiveMagicResistance);
            return target.ApplyDamageTakenModifiers(
                attack.DamageType,
                damage);
        }

        private bool IsAttackEvaded(
            PendingAttack attack,
            RuntimeUnitState target)
        {
            if (attack.DamageType != DamageType.Physical
                && attack.DamageType != DamageType.Magic)
                return false;
            foreach (var ability in target.EvasionModifiers
                         .OrderBy(
                             item => item.Key,
                             StringComparer.Ordinal))
            {
                var chance = attack.DamageType
                    == DamageType.Physical
                        ? ability.Value.PhysicalChancePermille
                        : ability.Value.MagicChancePermille;
                if (chance <= 0)
                    continue;
                if (chance >= 1000)
                    return true;
                var hash = 14695981039346656037UL;
                hash = AppendStableHash(hash, Input.BattleId);
                hash = AppendStableHash(
                    hash,
                    attack.AttackerUnitId);
                hash = AppendStableHash(
                    hash,
                    attack.TargetUnitId);
                hash = AppendStableHash(hash, ability.Key);
                hash = AppendStableHash(hash, attack.DamageTick);
                hash = AppendStableHash(
                    hash,
                    attack.AttackOrdinal);
                if ((int)(hash % 1000UL) < chance)
                    return true;
            }
            return false;
        }

        private static int CalculateReactionDamage(
            OnDamageReactionEffectDefinition reaction,
            RuntimeUnitState target)
        {
            var damage = DamageCalculator.Calculate(
                reaction.DamageType,
                reaction.DamageAmount,
                target.EffectiveDefense,
                target.EffectiveMagicResistance);
            return target.ApplyDamageTakenModifiers(
                reaction.DamageType,
                damage);
        }

        private static int ApplyDefenseMultiplier(
            int defense,
            int multiplierPermille)
        {
            return (int)((long)defense * multiplierPermille / 1000);
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
            unit.MoveRemainder +=
                unit.EffectiveMoveSpeedCentimetresPerSecond;
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

        private static FixedPosition DashTowards(
            FixedPosition from,
            FixedPosition target,
            int distanceCentimetres)
        {
            var dx = target.XUnits - from.XUnits;
            var dy = target.YUnits - from.YUnits;
            var distance = IntegerSquareRootCeiling(
                (long)dx * dx + (long)dy * dy);
            if (distance == 0)
                return from;
            if (distanceCentimetres >= distance)
                return target;
            return new FixedPosition(
                from.XUnits
                + (int)((long)dx
                    * distanceCentimetres
                    / distance),
                from.YUnits
                + (int)((long)dy
                    * distanceCentimetres
                    / distance));
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
        private readonly struct PendingAttack { public PendingAttack(string attackerUnitId, string targetUnitId, int damageTick, DamageType damageType, int attack, int targetDefenseMultiplierPermille, int attackOrdinal, int originalTicks, int effectiveTicks) { AttackerUnitId = attackerUnitId; TargetUnitId = targetUnitId; DamageTick = damageTick; DamageType = damageType; Attack = attack; TargetDefenseMultiplierPermille = targetDefenseMultiplierPermille; AttackOrdinal = attackOrdinal; OriginalTicks = originalTicks; EffectiveTicks = effectiveTicks; } public string AttackerUnitId { get; } public string TargetUnitId { get; } public int DamageTick { get; } public DamageType DamageType { get; } public int Attack { get; } public int TargetDefenseMultiplierPermille { get; } public int AttackOrdinal { get; } public int OriginalTicks { get; } public int EffectiveTicks { get; } }
        private readonly struct DamageHit { public DamageHit(PendingAttack attack, int amount) { Attack = attack; Amount = amount; } public PendingAttack Attack { get; } public int Amount { get; } }
        private readonly struct DamageReaction { public DamageReaction(string ownerUnitId, string targetUnitId, OnDamageReactionEffectDefinition effect) { OwnerUnitId = ownerUnitId; TargetUnitId = targetUnitId; Effect = effect; } public string OwnerUnitId { get; } public string TargetUnitId { get; } public OnDamageReactionEffectDefinition Effect { get; } }
        private readonly struct ResolvedDamageReaction { public ResolvedDamageReaction(DamageReaction reaction, int amount) { Reaction = reaction; Amount = amount; } public DamageReaction Reaction { get; } public int Amount { get; } }
        private readonly struct DeathAreaDamageHit { public DeathAreaDamageHit(PendingDeathAreaDamage pending, string targetUnitId, int amount) { Pending = pending; TargetUnitId = targetUnitId; Amount = amount; } public PendingDeathAreaDamage Pending { get; } public string TargetUnitId { get; } public int Amount { get; } }
        private readonly struct TimedTargetAreaDamageHit { public TimedTargetAreaDamageHit(PendingTimedTargetAreaDamage pending, string targetUnitId, int amount) { Pending = pending; TargetUnitId = targetUnitId; Amount = amount; } public PendingTimedTargetAreaDamage Pending { get; } public string TargetUnitId { get; } public int Amount { get; } }
        private readonly struct ProximityEntryDamageHit { public ProximityEntryDamageHit(string sourceUnitId, string targetUnitId, ProximityEntryDamageEffectDefinition effect, int amount) { SourceUnitId = sourceUnitId; TargetUnitId = targetUnitId; Effect = effect; Amount = amount; } public string SourceUnitId { get; } public string TargetUnitId { get; } public ProximityEntryDamageEffectDefinition Effect { get; } public int Amount { get; } }
        private readonly struct PendingTimedTargetAreaDamage { public PendingTimedTargetAreaDamage(int dueTick, string ownerUnitId, BattleSide side, string targetUnitId, int attack, string abilityId, TimedTargetAreaDamageEffectDefinition effect) { DueTick = dueTick; OwnerUnitId = ownerUnitId; Side = side; TargetUnitId = targetUnitId; Attack = attack; AbilityId = abilityId; Effect = effect; } public int DueTick { get; } public string OwnerUnitId { get; } public BattleSide Side { get; } public string TargetUnitId { get; } public int Attack { get; } public string AbilityId { get; } public TimedTargetAreaDamageEffectDefinition Effect { get; } }
        private readonly struct PendingRelocation { public PendingRelocation(int dueTick, string unitId, string targetUnitId, int distanceCentimetres) { DueTick = dueTick; UnitId = unitId; TargetUnitId = targetUnitId; DistanceCentimetres = distanceCentimetres; } public int DueTick { get; } public string UnitId { get; } public string TargetUnitId { get; } public int DistanceCentimetres { get; } }
        private readonly struct PendingDeathAreaDamage { public PendingDeathAreaDamage(int dueTick, string ownerUnitId, BattleSide side, FixedPosition position, int attack, string abilityId, DeathAreaDamageEffectDefinition effect) { DueTick = dueTick; OwnerUnitId = ownerUnitId; Side = side; Position = position; Attack = attack; AbilityId = abilityId; Effect = effect; } public int DueTick { get; } public string OwnerUnitId { get; } public BattleSide Side { get; } public FixedPosition Position { get; } public int Attack { get; } public string AbilityId { get; } public DeathAreaDamageEffectDefinition Effect { get; } }
        private readonly struct PendingDeathSpawn
        {
            public PendingDeathSpawn(
                int dueTick,
                string ownerUnitId,
                string playerId,
                BattleSide side,
                FixedPosition position,
                string abilityId,
                DeathSpawnEffectDefinition effect)
            {
                DueTick = dueTick;
                OwnerUnitId = ownerUnitId;
                PlayerId = playerId;
                Side = side;
                Position = position;
                AbilityId = abilityId;
                Effect = effect;
            }

            public int DueTick { get; }
            public string OwnerUnitId { get; }
            public string PlayerId { get; }
            public BattleSide Side { get; }
            public FixedPosition Position { get; }
            public string AbilityId { get; }
            public DeathSpawnEffectDefinition Effect { get; }
        }
        private readonly struct AuraContribution
        {
            public AuraContribution(
                string sourceUnitId,
                string targetUnitId,
                string abilityId,
                IExternalCombatModifierDefinition modifier)
            {
                SourceUnitId = sourceUnitId;
                TargetUnitId = targetUnitId;
                AbilityId = abilityId;
                Modifier = modifier;
            }

            public string SourceUnitId { get; }
            public string TargetUnitId { get; }
            public string AbilityId { get; }
            public IExternalCombatModifierDefinition Modifier { get; }
        }

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
                unit.EffectiveMoveSpeedCentimetresPerSecond,
                definition.AttackIntervalTicks,
                definition.AttackAnimationDurationTicks,
                definition.DamageType,
                definition.AttackMethod,
                definition.BlockCapacity,
                definition.TauntLevel,
                unit.Buffs,
                unit.ActivationTick,
                definition.LifeDeduct);
        }

        private string BuildStableSummary()
        {
            var builder = new StringBuilder(Input.CanonicalSummary);
            builder.Append("|runner:")
                .Append(CurrentTick).Append(',')
                .Append((int)Status).Append(',')
                .Append((int)StopReason).Append(',')
                .Append(HomeLifeLoss).Append(',')
                .Append(AwayLifeLoss);
            foreach (var unit in runtimeUnits.OrderBy(item => item.UnitId, StringComparer.Ordinal))
            {
                builder.Append("|R:").Append(unit.UnitId).Append(',').Append(unit.PlayerId).Append(',').Append((int)unit.Side).Append(',').Append(unit.TypeId).Append(',').Append(unit.CurrentHitPoints).Append(',').Append(unit.Position.XUnits).Append(',').Append(unit.Position.YUnits).Append(',').Append(unit.ActivationTick);
                if (unit.HasExitedBattle)
                    builder.Append(",exited");
                if (unit.InstanceMoveSpeedMultiplierPermille
                    != DeathSpawnEffectDefinition
                        .NeutralMoveSpeedMultiplierPermille)
                    builder.Append(",move:")
                        .Append(
                            unit.InstanceMoveSpeedMultiplierPermille);
                if (unit.DeploymentApproachDestination.HasValue)
                    builder.Append(",approach:")
                        .Append(
                            unit.DeploymentApproachDestination
                                .Value.XUnits)
                        .Append(':')
                        .Append(
                            unit.DeploymentApproachDestination
                                .Value.YUnits);
                if (unit.AbilityStates.Any(item =>
                        item.Definition.AttackSequenceModifier != null
                        || item.Definition.AttackCountStateModifier != null
                        || item.Definition.AttackDashEffect != null))
                    builder.Append(",attacks:")
                        .Append(unit.StartedAttackCount);
                foreach (var ability in unit.AbilityStates.OrderBy(item => item.Definition.AbilityId, StringComparer.Ordinal)) ability.AppendStableSummary(builder);
                unit.AppendTemporaryUnblockableSummary(builder);
                unit.AppendDamageOverTimeSummary(builder);
            }
            foreach (var pending in pendingDeathSpawns
                         .OrderBy(item => item.DueTick)
                         .ThenBy(item => item.OwnerUnitId, StringComparer.Ordinal)
                         .ThenBy(item => item.AbilityId, StringComparer.Ordinal))
                builder.Append("|X:")
                    .Append(pending.DueTick).Append(',')
                    .Append(pending.OwnerUnitId).Append(',')
                    .Append(pending.AbilityId);
            foreach (var pending in pendingDeathAreaDamages
                         .OrderBy(item => item.DueTick)
                         .ThenBy(
                             item => item.OwnerUnitId,
                             StringComparer.Ordinal)
                         .ThenBy(
                             item => item.AbilityId,
                             StringComparer.Ordinal))
                builder.Append("|Z:")
                    .Append(pending.DueTick).Append(',')
                    .Append(pending.OwnerUnitId).Append(',')
                    .Append(pending.AbilityId);
            foreach (var pending in pendingTimedTargetAreaDamages
                         .OrderBy(item => item.DueTick)
                         .ThenBy(
                             item => item.OwnerUnitId,
                             StringComparer.Ordinal)
                         .ThenBy(
                             item => item.AbilityId,
                             StringComparer.Ordinal))
                builder.Append("|AA:")
                    .Append(pending.DueTick).Append(',')
                    .Append(pending.OwnerUnitId).Append(',')
                    .Append(pending.TargetUnitId).Append(',')
                    .Append(pending.AbilityId);
            foreach (var pending in pendingRelocations
                         .OrderBy(item => item.DueTick)
                         .ThenBy(
                             item => item.UnitId,
                             StringComparer.Ordinal))
                builder.Append("|R:")
                    .Append(pending.DueTick).Append(',')
                    .Append(pending.UnitId).Append(',')
                    .Append(pending.TargetUnitId).Append(',')
                    .Append(pending.DistanceCentimetres);
            foreach (var item in trace) builder.Append("|S:").Append(item.Tick).Append(',').Append((int)item.Status).Append(',').Append((int)item.StopReason);
            return builder.ToString();
        }
    }
}
