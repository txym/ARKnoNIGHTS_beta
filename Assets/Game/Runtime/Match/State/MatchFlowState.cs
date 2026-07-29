using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ArknoNights.Match
{
    public sealed class MatchPlayerCombatSnapshot
    {
        internal MatchPlayerCombatSnapshot(
            string playerId,
            IEnumerable<MatchUnitState> deployedUnits,
            IEnumerable<PlayerTargetedUnitBuffState> targetedUnitBuffs,
            IEnumerable<PlayerGlobalBuffState> globalBuffs,
            IEnumerable<PlayerSourceEffectState> sourceEffects)
        {
            PlayerId = playerId;
            DeployedUnits = new ReadOnlyCollection<MatchUnitState>(
                (deployedUnits ?? Enumerable.Empty<MatchUnitState>())
                    .OrderBy(unit => unit.Formation.HasValue ? unit.Formation.Value.Y : int.MaxValue)
                    .ThenBy(unit => unit.Formation.HasValue ? unit.Formation.Value.X : int.MaxValue)
                    .ThenBy(unit => unit.UnitId, StringComparer.Ordinal)
                    .ToArray());
            GlobalBuffs = new ReadOnlyCollection<PlayerGlobalBuffState>(
                (globalBuffs ?? Enumerable.Empty<PlayerGlobalBuffState>())
                    .OrderBy(buff => buff.BuffInstanceId, StringComparer.Ordinal)
                    .ToArray());
            var deployedIds = new HashSet<string>(
                DeployedUnits.Select(unit => unit.UnitId),
                StringComparer.Ordinal);
            TargetedUnitBuffs = new ReadOnlyCollection<PlayerTargetedUnitBuffState>(
                (targetedUnitBuffs ?? Enumerable.Empty<PlayerTargetedUnitBuffState>())
                    .Where(buff => deployedIds.Contains(buff.TargetUnitId))
                    .OrderBy(buff => buff.BuffInstanceId, StringComparer.Ordinal)
                    .ToArray());
            SourceEffects = new ReadOnlyCollection<PlayerSourceEffectState>(
                (sourceEffects ?? Enumerable.Empty<PlayerSourceEffectState>())
                    .OrderBy(effect => effect.EffectInstanceId, StringComparer.Ordinal)
                    .ToArray());

            var writer = new CanonicalSummaryWriter(nameof(MatchPlayerCombatSnapshot));
            writer.String("playerId", PlayerId);
            foreach (var unit in DeployedUnits)
            {
                writer.Summary("unit", unit.CanonicalSummary);
            }
            foreach (var buff in GlobalBuffs)
            {
                writer.Summary("globalBuff", buff.CanonicalSummary);
            }
            foreach (var buff in TargetedUnitBuffs)
            {
                writer.Summary("targetedUnitBuff", buff.CanonicalSummary);
            }
            foreach (var effect in SourceEffects)
            {
                writer.Summary("sourceEffect", effect.CanonicalSummary);
            }
            CanonicalSummary = writer.ToString();
        }

        public string PlayerId { get; }
        public IReadOnlyList<MatchUnitState> DeployedUnits { get; }
        public IReadOnlyList<PlayerGlobalBuffState> GlobalBuffs { get; }
        public IReadOnlyList<PlayerTargetedUnitBuffState> TargetedUnitBuffs { get; }
        public IReadOnlyList<PlayerSourceEffectState> SourceEffects { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class MatchSealedPairing
    {
        internal MatchSealedPairing(
            string battleId,
            int battleIndex,
            MatchPairingKind kind,
            string homePlayerId,
            string awayPlayerId,
            string shadowOwnerPlayerId,
            IEnumerable<string> settlementRecipients,
            string battleSeed,
            string sealedInputHash)
        {
            BattleId = battleId;
            BattleIndex = battleIndex;
            Kind = kind;
            HomePlayerId = homePlayerId;
            AwayPlayerId = awayPlayerId;
            ShadowOwnerPlayerId = shadowOwnerPlayerId ?? string.Empty;
            SettlementRecipients = new ReadOnlyCollection<string>(
                (settlementRecipients ?? Enumerable.Empty<string>())
                    .OrderBy(item => item, StringComparer.Ordinal)
                    .ToArray());
            BattleSeed = battleSeed;
            SealedInputHash = sealedInputHash;
            CanonicalSummary = BuildCanonicalSummary(includeHash: true);
        }

        public string BattleId { get; }
        public int BattleIndex { get; }
        public MatchPairingKind Kind { get; }
        public string HomePlayerId { get; }
        public string AwayPlayerId { get; }
        public string ShadowOwnerPlayerId { get; }
        public IReadOnlyList<string> SettlementRecipients { get; }
        public string BattleSeed { get; }
        public string SealedInputHash { get; }
        public string CanonicalSummary { get; }

        internal string BuildCanonicalSummary(bool includeHash)
        {
            var writer = new CanonicalSummaryWriter(nameof(MatchSealedPairing));
            writer.String("battleId", BattleId);
            writer.Integer("battleIndex", BattleIndex);
            writer.EnumValue("kind", Kind);
            writer.String("homePlayerId", HomePlayerId);
            writer.String("awayPlayerId", AwayPlayerId);
            writer.String("shadowOwnerPlayerId", ShadowOwnerPlayerId);
            foreach (var recipient in SettlementRecipients)
            {
                writer.String("settlementRecipient", recipient);
            }
            writer.String("battleSeed", BattleSeed);
            if (includeHash)
            {
                writer.String("sealedInputHash", SealedInputHash);
            }
            return writer.ToString();
        }
    }

    public sealed class MatchSealedRoundPlan
    {
        internal MatchSealedRoundPlan(
            string sessionId,
            int roundNumber,
            int pairingGeneration,
            IEnumerable<MatchSealedPairing> pairings,
            IEnumerable<MatchPlayerCombatSnapshot> playerCombatSnapshots,
            string matchRulesVersion,
            string unitCatalogSha256,
            string abilityCatalogSha256,
            string canonicalInputHash)
        {
            SessionId = sessionId;
            RoundNumber = roundNumber;
            PairingGeneration = pairingGeneration;
            Pairings = new ReadOnlyCollection<MatchSealedPairing>(
                (pairings ?? Enumerable.Empty<MatchSealedPairing>())
                    .OrderBy(pairing => pairing.BattleIndex)
                    .ToArray());
            PlayerCombatSnapshots = new ReadOnlyCollection<MatchPlayerCombatSnapshot>(
                (playerCombatSnapshots ?? Enumerable.Empty<MatchPlayerCombatSnapshot>())
                    .OrderBy(snapshot => snapshot.PlayerId, StringComparer.Ordinal)
                    .ToArray());
            MatchRulesVersion = matchRulesVersion;
            UnitCatalogSha256 = unitCatalogSha256;
            AbilityCatalogSha256 = abilityCatalogSha256;
            CanonicalInputHash = canonicalInputHash;
            CanonicalSummary = BuildCanonicalSummary(includeHash: true);
        }

        public string SessionId { get; }
        public int RoundNumber { get; }
        public int PairingGeneration { get; }
        public IReadOnlyList<MatchSealedPairing> Pairings { get; }
        public IReadOnlyList<MatchPlayerCombatSnapshot> PlayerCombatSnapshots { get; }
        public string MatchRulesVersion { get; }
        public string UnitCatalogSha256 { get; }
        public string AbilityCatalogSha256 { get; }
        public string CanonicalInputHash { get; }
        public string CanonicalSummary { get; }

        internal string BuildCanonicalSummary(bool includeHash)
        {
            var writer = new CanonicalSummaryWriter(nameof(MatchSealedRoundPlan));
            writer.String("sessionId", SessionId);
            writer.Integer("round", RoundNumber);
            writer.Integer("pairingGeneration", PairingGeneration);
            writer.String("matchRulesVersion", MatchRulesVersion);
            writer.String("unitCatalogSha256", UnitCatalogSha256);
            writer.String("abilityCatalogSha256", AbilityCatalogSha256);
            foreach (var pairing in Pairings)
            {
                writer.Summary("pairing", pairing.CanonicalSummary);
            }
            foreach (var snapshot in PlayerCombatSnapshots)
            {
                writer.Summary("playerCombatSnapshot", snapshot.CanonicalSummary);
            }
            if (includeHash)
            {
                writer.String("canonicalInputHash", CanonicalInputHash);
            }
            return writer.ToString();
        }
    }

    public sealed class MatchPairingHistoryEntry
    {
        internal MatchPairingHistoryEntry(
            int roundNumber,
            int generation,
            MatchSealedPairing pairing)
        {
            RoundNumber = roundNumber;
            Generation = generation;
            BattleId = pairing.BattleId;
            BattleIndex = pairing.BattleIndex;
            Kind = pairing.Kind;
            HomePlayerId = pairing.HomePlayerId;
            AwayPlayerId = pairing.AwayPlayerId;
            ShadowOwnerPlayerId = pairing.ShadowOwnerPlayerId;
            var writer = new CanonicalSummaryWriter(nameof(MatchPairingHistoryEntry));
            writer.Integer("round", RoundNumber);
            writer.Integer("generation", Generation);
            writer.String("battleId", BattleId);
            writer.Integer("battleIndex", BattleIndex);
            writer.EnumValue("kind", Kind);
            writer.String("homePlayerId", HomePlayerId);
            writer.String("awayPlayerId", AwayPlayerId);
            writer.String("shadowOwnerPlayerId", ShadowOwnerPlayerId);
            CanonicalSummary = writer.ToString();
        }

        public int RoundNumber { get; }
        public int Generation { get; }
        public string BattleId { get; }
        public int BattleIndex { get; }
        public MatchPairingKind Kind { get; }
        public string HomePlayerId { get; }
        public string AwayPlayerId { get; }
        public string ShadowOwnerPlayerId { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class MatchPairingDiagnostics
    {
        internal MatchPairingDiagnostics(
            int generation,
            int population,
            int selectedOffset,
            IEnumerable<string> selectedSeatOrder,
            IEnumerable<string> relaxedConstraints)
        {
            Generation = generation;
            Population = population;
            SelectedOffset = selectedOffset;
            SelectedSeatOrder = new ReadOnlyCollection<string>(
                (selectedSeatOrder ?? Enumerable.Empty<string>()).ToArray());
            RelaxedConstraints = new ReadOnlyCollection<string>(
                (relaxedConstraints ?? Enumerable.Empty<string>())
                    .OrderBy(item => item, StringComparer.Ordinal)
                    .ToArray());
            var writer = new CanonicalSummaryWriter(nameof(MatchPairingDiagnostics));
            writer.Integer("generation", Generation);
            writer.Integer("population", Population);
            writer.Integer("selectedOffset", SelectedOffset);
            foreach (var playerId in SelectedSeatOrder)
            {
                writer.String("selectedSeat", playerId);
            }
            foreach (var constraint in RelaxedConstraints)
            {
                writer.String("relaxedConstraint", constraint);
            }
            CanonicalSummary = writer.ToString();
        }

        public int Generation { get; }
        public int Population { get; }
        public int SelectedOffset { get; }
        public IReadOnlyList<string> SelectedSeatOrder { get; }
        public IReadOnlyList<string> RelaxedConstraints { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class MatchFlowState
    {
        internal MatchFlowState(
            bool hasPreparationClock,
            long preparationStartedAtHostMonotonicMs,
            long preparationDeadlineHostMonotonicMs,
            long lastHostMonotonicMs,
            MatchSealTrigger? lastSealTrigger,
            int pairingGeneration,
            int pairingPopulation,
            int pairingOffset,
            IEnumerable<string> pairingSeatOrder,
            IEnumerable<MatchPairingHistoryEntry> pairingHistory,
            MatchPairingDiagnostics pairingDiagnostics,
            MatchSealedRoundPlan sealedRoundPlan,
            IEnumerable<MatchBattleResolution> battleResolutions,
            bool battlePlaybackCompleted,
            IEnumerable<MatchSettlementRecord> settlementRecords,
            MatchEndReason endReason,
            string abortDiagnostic,
            IEnumerable<MatchStanding> finalStandings,
            IEnumerable<string> systemActionIds,
            IEnumerable<MatchExternalEffect> externalEffects)
        {
            HasPreparationClock = hasPreparationClock;
            PreparationStartedAtHostMonotonicMs = preparationStartedAtHostMonotonicMs;
            PreparationDeadlineHostMonotonicMs = preparationDeadlineHostMonotonicMs;
            LastHostMonotonicMs = lastHostMonotonicMs;
            LastSealTrigger = lastSealTrigger;
            PairingGeneration = pairingGeneration;
            PairingPopulation = pairingPopulation;
            PairingOffset = pairingOffset;
            PairingSeatOrder = new ReadOnlyCollection<string>(
                (pairingSeatOrder ?? Enumerable.Empty<string>()).ToArray());
            PairingHistory = new ReadOnlyCollection<MatchPairingHistoryEntry>(
                (pairingHistory ?? Enumerable.Empty<MatchPairingHistoryEntry>())
                    .OrderBy(item => item.RoundNumber)
                    .ThenBy(item => item.BattleId, StringComparer.Ordinal)
                    .ToArray());
            PairingDiagnostics = pairingDiagnostics;
            SealedRoundPlan = sealedRoundPlan;
            BattleResolutions = new ReadOnlyCollection<MatchBattleResolution>(
                (battleResolutions ?? Enumerable.Empty<MatchBattleResolution>())
                    .OrderBy(item => item.BattleId, StringComparer.Ordinal)
                    .ToArray());
            BattlePlaybackCompleted = battlePlaybackCompleted;
            SettlementRecords = new ReadOnlyCollection<MatchSettlementRecord>(
                (settlementRecords ?? Enumerable.Empty<MatchSettlementRecord>())
                    .OrderBy(item => item.RoundNumber)
                    .ToArray());
            EndReason = endReason;
            AbortDiagnostic = abortDiagnostic ?? string.Empty;
            FinalStandings = new ReadOnlyCollection<MatchStanding>(
                (finalStandings ?? Enumerable.Empty<MatchStanding>())
                    .OrderBy(item => item.Placement ?? int.MaxValue)
                    .ThenBy(item => item.PlayerId, StringComparer.Ordinal)
                    .ToArray());
            SystemActionIds = new ReadOnlyCollection<string>(
                (systemActionIds ?? Enumerable.Empty<string>())
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(item => item, StringComparer.Ordinal)
                    .ToArray());
            ExternalEffects = new ReadOnlyCollection<MatchExternalEffect>(
                (externalEffects ?? Enumerable.Empty<MatchExternalEffect>())
                    .OrderBy(item => item.SourceRevision)
                    .ThenBy(item => item.EffectId, StringComparer.Ordinal)
                    .ToArray());
            CanonicalSummary = BuildCanonicalSummary();
        }

        internal static MatchFlowState Empty => new MatchFlowState(
            false,
            0,
            0,
            0,
            null,
            0,
            0,
            0,
            Array.Empty<string>(),
            Array.Empty<MatchPairingHistoryEntry>(),
            null,
            null,
            Array.Empty<MatchBattleResolution>(),
            false,
            Array.Empty<MatchSettlementRecord>(),
            MatchEndReason.None,
            string.Empty,
            Array.Empty<MatchStanding>(),
            Array.Empty<string>(),
            Array.Empty<MatchExternalEffect>());

        public bool HasPreparationClock { get; }
        public long PreparationStartedAtHostMonotonicMs { get; }
        public long PreparationDeadlineHostMonotonicMs { get; }
        public long LastHostMonotonicMs { get; }
        public MatchSealTrigger? LastSealTrigger { get; }
        public int PairingGeneration { get; }
        public int PairingPopulation { get; }
        public int PairingOffset { get; }
        public IReadOnlyList<string> PairingSeatOrder { get; }
        public IReadOnlyList<MatchPairingHistoryEntry> PairingHistory { get; }
        public MatchPairingDiagnostics PairingDiagnostics { get; }
        public MatchSealedRoundPlan SealedRoundPlan { get; }
        public IReadOnlyList<MatchBattleResolution> BattleResolutions { get; }
        public bool BattlePlaybackCompleted { get; }
        public IReadOnlyList<MatchSettlementRecord> SettlementRecords { get; }
        public MatchEndReason EndReason { get; }
        public string AbortDiagnostic { get; }
        public IReadOnlyList<MatchStanding> FinalStandings { get; }
        public IReadOnlyList<string> SystemActionIds { get; }
        public IReadOnlyList<MatchExternalEffect> ExternalEffects { get; }
        public string CanonicalSummary { get; }

        internal MatchFlowState With(
            bool? hasPreparationClock = null,
            long? preparationStartedAtHostMonotonicMs = null,
            long? preparationDeadlineHostMonotonicMs = null,
            long? lastHostMonotonicMs = null,
            MatchSealTrigger? lastSealTrigger = null,
            bool clearLastSealTrigger = false,
            int? pairingGeneration = null,
            int? pairingPopulation = null,
            int? pairingOffset = null,
            IEnumerable<string> pairingSeatOrder = null,
            IEnumerable<MatchPairingHistoryEntry> pairingHistory = null,
            MatchPairingDiagnostics pairingDiagnostics = null,
            MatchSealedRoundPlan sealedRoundPlan = null,
            bool clearSealedRoundPlan = false,
            IEnumerable<MatchBattleResolution> battleResolutions = null,
            bool? battlePlaybackCompleted = null,
            IEnumerable<MatchSettlementRecord> settlementRecords = null,
            MatchEndReason? endReason = null,
            string abortDiagnostic = null,
            IEnumerable<MatchStanding> finalStandings = null,
            IEnumerable<string> systemActionIds = null,
            IEnumerable<MatchExternalEffect> externalEffects = null)
        {
            return new MatchFlowState(
                hasPreparationClock ?? HasPreparationClock,
                preparationStartedAtHostMonotonicMs ?? PreparationStartedAtHostMonotonicMs,
                preparationDeadlineHostMonotonicMs ?? PreparationDeadlineHostMonotonicMs,
                lastHostMonotonicMs ?? LastHostMonotonicMs,
                clearLastSealTrigger ? (MatchSealTrigger?)null : (lastSealTrigger ?? LastSealTrigger),
                pairingGeneration ?? PairingGeneration,
                pairingPopulation ?? PairingPopulation,
                pairingOffset ?? PairingOffset,
                pairingSeatOrder ?? PairingSeatOrder,
                pairingHistory ?? PairingHistory,
                pairingDiagnostics ?? PairingDiagnostics,
                clearSealedRoundPlan ? null : (sealedRoundPlan ?? SealedRoundPlan),
                battleResolutions ?? BattleResolutions,
                battlePlaybackCompleted ?? BattlePlaybackCompleted,
                settlementRecords ?? SettlementRecords,
                endReason ?? EndReason,
                abortDiagnostic ?? AbortDiagnostic,
                finalStandings ?? FinalStandings,
                systemActionIds ?? SystemActionIds,
                externalEffects ?? ExternalEffects);
        }

        private string BuildCanonicalSummary()
        {
            var writer = new CanonicalSummaryWriter(nameof(MatchFlowState));
            writer.Boolean("hasPreparationClock", HasPreparationClock);
            writer.Integer("preparationStartedAt", PreparationStartedAtHostMonotonicMs);
            writer.Integer("preparationDeadline", PreparationDeadlineHostMonotonicMs);
            writer.Integer("lastHostMonotonic", LastHostMonotonicMs);
            writer.String(
                "lastSealTrigger",
                LastSealTrigger.HasValue ? LastSealTrigger.Value.ToString() : string.Empty);
            writer.Integer("pairingGeneration", PairingGeneration);
            writer.Integer("pairingPopulation", PairingPopulation);
            writer.Integer("pairingOffset", PairingOffset);
            foreach (var playerId in PairingSeatOrder)
            {
                writer.String("pairingSeat", playerId);
            }
            foreach (var entry in PairingHistory)
            {
                writer.Summary("pairingHistory", entry.CanonicalSummary);
            }
            writer.Summary(
                "pairingDiagnostics",
                PairingDiagnostics == null ? string.Empty : PairingDiagnostics.CanonicalSummary);
            writer.Summary(
                "sealedRoundPlan",
                SealedRoundPlan == null ? string.Empty : SealedRoundPlan.CanonicalSummary);
            foreach (var resolution in BattleResolutions)
            {
                writer.Summary("battleResolution", resolution.CanonicalSummary);
            }
            writer.Boolean("battlePlaybackCompleted", BattlePlaybackCompleted);
            foreach (var record in SettlementRecords)
            {
                writer.Summary("settlementRecord", record.CanonicalSummary);
            }
            writer.EnumValue("endReason", EndReason);
            writer.String("abortDiagnostic", AbortDiagnostic);
            foreach (var standing in FinalStandings)
            {
                writer.Summary("finalStanding", standing.CanonicalSummary);
            }
            foreach (var actionId in SystemActionIds)
            {
                writer.String("systemActionId", actionId);
            }
            foreach (var effect in ExternalEffects)
            {
                writer.Summary("externalEffect", effect.CanonicalSummary);
            }
            return writer.ToString();
        }
    }

    internal static class MatchFlowHash
    {
        internal static string Sha256(string value)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var builder = new StringBuilder(64);
                foreach (var item in bytes)
                {
                    builder.Append(item.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }
                return builder.ToString();
            }
        }
    }

    internal static class MatchFlowInvariant
    {
        internal static bool TryValidate(MatchState state, out string diagnosticCode)
        {
            var flow = state.Flow;
            if (flow == null)
            {
                diagnosticCode = "match.invariant.flow.null";
                return false;
            }
            if (flow.HasPreparationClock
                && (state.Phase != MatchPhase.Preparation
                    || flow.PreparationStartedAtHostMonotonicMs
                        > flow.PreparationDeadlineHostMonotonicMs
                    || flow.LastHostMonotonicMs
                        < flow.PreparationStartedAtHostMonotonicMs))
            {
                diagnosticCode = "match.invariant.flow.clock";
                return false;
            }
            if (state.Phase == MatchPhase.Preparation
                && flow.PairingGeneration > 0
                && !flow.HasPreparationClock)
            {
                diagnosticCode = "match.invariant.flow.preparationClock.missing";
                return false;
            }
            var effectIds = new HashSet<string>(StringComparer.Ordinal);
            if (flow.ExternalEffects.Any(effect =>
                effect == null
                || string.IsNullOrWhiteSpace(effect.EffectId)
                || !effectIds.Add(effect.EffectId)
                || effect.SourceRevision < 0
                || effect.SourceRevision > state.StateRevision))
            {
                diagnosticCode = "match.invariant.flow.effects";
                return false;
            }
            if (flow.SettlementRecords.Any(record =>
                    record == null
                    || record.RoundNumber <= 0
                    || string.IsNullOrWhiteSpace(record.SealedPlanCanonicalSummary)
                    || record.BattleResolutionCanonicalSummaries.Count == 0
                    || record.AppliedResults.Count == 0
                    || string.IsNullOrWhiteSpace(
                        record.SettlementOutputCanonicalSummary))
                || flow.SettlementRecords
                .GroupBy(record => record.RoundNumber)
                .Any(group => group.Count() != 1))
            {
                diagnosticCode = "match.invariant.flow.settlementRecords";
                return false;
            }
            if (flow.SystemActionIds.Any(actionId =>
                string.IsNullOrWhiteSpace(actionId) || actionId.Length != 64))
            {
                diagnosticCode = "match.invariant.flow.systemActions";
                return false;
            }
            var plan = flow.SealedRoundPlan;
            if (flow.PairingGeneration > 0
                && state.Phase == MatchPhase.Battle
                && plan == null)
            {
                diagnosticCode = "match.invariant.flow.plan.missing";
                return false;
            }
            if (plan != null)
            {
                if (!string.Equals(plan.SessionId, state.SessionId, StringComparison.Ordinal)
                    || plan.RoundNumber != state.RoundNumber
                    || plan.PairingGeneration != flow.PairingGeneration
                    || !string.Equals(
                        plan.CanonicalInputHash,
                        MatchFlowHash.Sha256(
                            plan.BuildCanonicalSummary(includeHash: false)),
                        StringComparison.Ordinal)
                    || plan.Pairings.Count < 1
                    || plan.Pairings.Count > 2
                    || plan.Pairings.Select(pairing => pairing.BattleId)
                        .Distinct(StringComparer.Ordinal).Count() != plan.Pairings.Count
                    || plan.Pairings.Select(pairing => pairing.BattleIndex)
                        .Distinct().Count() != plan.Pairings.Count
                    || plan.Pairings.Any(pairing =>
                        string.IsNullOrWhiteSpace(pairing.BattleId)
                        || string.IsNullOrWhiteSpace(pairing.BattleSeed)
                        || pairing.SealedInputHash == null
                        || pairing.SealedInputHash.Length != 64
                        || string.Equals(
                            pairing.HomePlayerId,
                            pairing.AwayPlayerId,
                            StringComparison.Ordinal)
                        || !Enum.IsDefined(typeof(MatchPairingKind), pairing.Kind)))
                {
                    diagnosticCode = "match.invariant.flow.plan";
                    return false;
                }
                var expectedBattleIds = new HashSet<string>(
                    plan.Pairings.Select(pairing => pairing.BattleId),
                    StringComparer.Ordinal);
                var resolutionIds = new HashSet<string>(StringComparer.Ordinal);
                if (flow.BattleResolutions.Any(result =>
                    result == null
                    || !expectedBattleIds.Contains(result.BattleId)
                    || !resolutionIds.Add(result.BattleId)
                    || result.HomeLifeDamage < 0
                    || result.AwayLifeDamage < 0
                    || result.EndTick < 0
                    || result.Outcome != MatchBattleResolution.DeriveOutcome(
                        result.HomeLifeDamage,
                        result.AwayLifeDamage)))
                {
                    diagnosticCode = "match.invariant.flow.resolutions";
                    return false;
                }
            }
            else if (flow.BattleResolutions.Count != 0
                || flow.BattlePlaybackCompleted)
            {
                diagnosticCode = "match.invariant.flow.resultWithoutPlan";
                return false;
            }
            if (flow.BattlePlaybackCompleted
                && (plan == null
                    || flow.BattleResolutions.Count != plan.Pairings.Count))
            {
                diagnosticCode = "match.invariant.flow.playback";
                return false;
            }
            if (flow.FinalStandings
                .GroupBy(standing => standing.PlayerId, StringComparer.Ordinal)
                .Any(group => group.Count() != 1)
                || flow.FinalStandings.Any(standing =>
                    state.FindSeat(standing.PlayerId) == null
                    || standing.Placement.HasValue
                        && (standing.Placement.Value < 1
                            || standing.Placement.Value > state.Seats.Count)))
            {
                diagnosticCode = "match.invariant.flow.standings";
                return false;
            }

            diagnosticCode = string.Empty;
            return true;
        }
    }
}
