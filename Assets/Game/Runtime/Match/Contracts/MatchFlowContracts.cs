using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace ArknoNights.Match
{
    public enum MatchStreakKind
    {
        None = 0,
        Win = 1,
        Loss = 2
    }

    public enum MatchSealTrigger
    {
        AllRequiredHumansReady = 0,
        PreparationDeadlineReached = 1,
        NoRequiredHumansAfterBotEntryCycle = 2
    }

    public enum MatchPairingKind
    {
        Official = 0,
        Shadow = 1
    }

    public enum MatchBattleOutcome
    {
        HomeWin = 0,
        AwayWin = 1,
        Draw = 2
    }

    public enum MatchAppliedOutcome
    {
        Win = 0,
        Loss = 1,
        Draw = 2
    }

    public enum MatchBattleRole
    {
        Home = 0,
        Away = 1
    }

    public enum MatchBattleTerminalReason
    {
        Normal = 0,
        MutualElimination = 1,
        MaximumTicksReached = 2
    }

    public enum MatchEndReason
    {
        None = 0,
        CompetitiveCompleted = 1,
        NoContest = 2,
        FatalMatchError = 3
    }

    public enum MatchExternalEffectKind
    {
        PreparationOpened = 0,
        RoundSealed = 1,
        BattlePlanReady = 2,
        RoundSettlementCommitted = 3,
        MatchEnded = 4,
        ReturnAllToMainMenu = 5
    }

    public static class MatchSystemActionId
    {
        public static string Derive(
            string sessionId,
            int roundNumber,
            int seatIndex,
            string actionKind)
        {
            var writer = new CanonicalSummaryWriter(nameof(MatchSystemActionId));
            writer.String("sessionId", sessionId);
            writer.Integer("round", roundNumber);
            writer.Integer("seat", seatIndex);
            writer.String("actionKind", actionKind);
            return MatchFlowHash.Sha256(writer.ToString());
        }
    }

    public sealed class MatchBattleResolution
    {
        public MatchBattleResolution(
            string battleId,
            string sealedInputHash,
            int homeLifeDamage,
            int awayLifeDamage,
            MatchBattleOutcome outcome,
            MatchBattleTerminalReason terminalReason,
            int endTick)
        {
            BattleId = battleId ?? string.Empty;
            SealedInputHash = sealedInputHash ?? string.Empty;
            HomeLifeDamage = homeLifeDamage;
            AwayLifeDamage = awayLifeDamage;
            Outcome = outcome;
            TerminalReason = terminalReason;
            EndTick = endTick;

            var writer = new CanonicalSummaryWriter(nameof(MatchBattleResolution));
            writer.String("battleId", BattleId);
            writer.String("sealedInputHash", SealedInputHash);
            writer.Integer("homeLifeDamage", HomeLifeDamage);
            writer.Integer("awayLifeDamage", AwayLifeDamage);
            writer.EnumValue("outcome", Outcome);
            writer.EnumValue("terminalReason", TerminalReason);
            writer.Integer("endTick", EndTick);
            CanonicalSummary = writer.ToString();
        }

        public string BattleId { get; }
        public string SealedInputHash { get; }
        public int HomeLifeDamage { get; }
        public int AwayLifeDamage { get; }
        public MatchBattleOutcome Outcome { get; }
        public MatchBattleTerminalReason TerminalReason { get; }
        public int EndTick { get; }
        public string CanonicalSummary { get; }

        public static MatchBattleOutcome DeriveOutcome(int homeLifeDamage, int awayLifeDamage)
        {
            if (homeLifeDamage < awayLifeDamage)
            {
                return MatchBattleOutcome.HomeWin;
            }
            if (homeLifeDamage > awayLifeDamage)
            {
                return MatchBattleOutcome.AwayWin;
            }
            return MatchBattleOutcome.Draw;
        }
    }

    public interface IMatchBattleInputAdapter<out TBattleInput>
    {
        TBattleInput CreateBattleInput(
            MatchSealedRoundPlan roundPlan,
            MatchSealedPairing pairing);
    }

    public interface IMatchBattleResolutionAdapter<in TBattleResult>
    {
        bool TryMapResolution(
            TBattleResult battleResult,
            MatchSealedPairing pairing,
            out MatchBattleResolution resolution,
            out string diagnosticCode);
    }

    public sealed class MatchAppliedResult
    {
        internal MatchAppliedResult(
            string playerId,
            string battleId,
            int lifeDamage,
            MatchAppliedOutcome outcome,
            MatchBattleRole role,
            bool isFromShadow)
        {
            PlayerId = playerId;
            BattleId = battleId;
            LifeDamage = lifeDamage;
            Outcome = outcome;
            Role = role;
            IsFromShadow = isFromShadow;

            var writer = new CanonicalSummaryWriter(nameof(MatchAppliedResult));
            writer.String("playerId", PlayerId);
            writer.String("battleId", BattleId);
            writer.Integer("lifeDamage", LifeDamage);
            writer.EnumValue("outcome", Outcome);
            writer.EnumValue("role", Role);
            writer.Boolean("isFromShadow", IsFromShadow);
            CanonicalSummary = writer.ToString();
        }

        public string PlayerId { get; }
        public string BattleId { get; }
        public int LifeDamage { get; }
        public MatchAppliedOutcome Outcome { get; }
        public MatchBattleRole Role { get; }
        public bool IsFromShadow { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class MatchStanding
    {
        internal MatchStanding(string playerId, int life, int? placement)
        {
            PlayerId = playerId;
            Life = life;
            Placement = placement;
            var writer = new CanonicalSummaryWriter(nameof(MatchStanding));
            writer.String("playerId", PlayerId);
            writer.Integer("life", Life);
            writer.NullableInteger("placement", Placement);
            CanonicalSummary = writer.ToString();
        }

        public string PlayerId { get; }
        public int Life { get; }
        public int? Placement { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class MatchExternalEffect
    {
        internal MatchExternalEffect(
            string effectId,
            MatchExternalEffectKind kind,
            long sourceRevision,
            int roundNumber)
        {
            EffectId = effectId;
            Kind = kind;
            SourceRevision = sourceRevision;
            RoundNumber = roundNumber;
            var writer = new CanonicalSummaryWriter(nameof(MatchExternalEffect));
            writer.String("effectId", EffectId);
            writer.EnumValue("kind", Kind);
            writer.Integer("sourceRevision", SourceRevision);
            writer.Integer("round", RoundNumber);
            CanonicalSummary = writer.ToString();
        }

        public string EffectId { get; }
        public MatchExternalEffectKind Kind { get; }
        public long SourceRevision { get; }
        public int RoundNumber { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class MatchSettlementRecord
    {
        internal MatchSettlementRecord(
            int roundNumber,
            IEnumerable<MatchAppliedResult> appliedResults,
            IEnumerable<string> newlyEliminatedPlayerIds,
            string sealedPlanCanonicalSummary,
            IEnumerable<MatchBattleResolution> battleResolutions,
            string settlementOutputCanonicalSummary)
        {
            RoundNumber = roundNumber;
            AppliedResults = new ReadOnlyCollection<MatchAppliedResult>(
                (appliedResults ?? Enumerable.Empty<MatchAppliedResult>())
                    .OrderBy(item => item.PlayerId, StringComparer.Ordinal)
                    .ToArray());
            NewlyEliminatedPlayerIds = new ReadOnlyCollection<string>(
                (newlyEliminatedPlayerIds ?? Enumerable.Empty<string>())
                    .OrderBy(item => item, StringComparer.Ordinal)
                    .ToArray());
            SealedPlanCanonicalSummary = sealedPlanCanonicalSummary ?? string.Empty;
            BattleResolutionCanonicalSummaries = new ReadOnlyCollection<string>(
                (battleResolutions ?? Enumerable.Empty<MatchBattleResolution>())
                    .OrderBy(item => item.BattleId, StringComparer.Ordinal)
                    .Select(item => item.CanonicalSummary)
                    .ToArray());
            SettlementOutputCanonicalSummary =
                settlementOutputCanonicalSummary ?? string.Empty;
            var writer = new CanonicalSummaryWriter(nameof(MatchSettlementRecord));
            writer.Integer("round", RoundNumber);
            writer.Summary("sealedPlan", SealedPlanCanonicalSummary);
            foreach (var resolution in BattleResolutionCanonicalSummaries)
            {
                writer.Summary("battleResolution", resolution);
            }
            foreach (var result in AppliedResults)
            {
                writer.Summary("appliedResult", result.CanonicalSummary);
            }
            foreach (var playerId in NewlyEliminatedPlayerIds)
            {
                writer.String("newlyEliminatedPlayerId", playerId);
            }
            writer.Summary(
                "settlementOutput",
                SettlementOutputCanonicalSummary);
            CanonicalSummary = writer.ToString();
        }

        public int RoundNumber { get; }
        public IReadOnlyList<MatchAppliedResult> AppliedResults { get; }
        public IReadOnlyList<string> NewlyEliminatedPlayerIds { get; }
        public string SealedPlanCanonicalSummary { get; }
        public IReadOnlyList<string> BattleResolutionCanonicalSummaries { get; }
        public string SettlementOutputCanonicalSummary { get; }
        public string CanonicalSummary { get; }
    }

    public interface IMatchPreparationEntryParticipant
    {
        bool TryRun(MatchState state, out MatchState preparedState, out string diagnosticCode);
    }

    internal sealed class NoOpMatchPreparationEntryParticipant : IMatchPreparationEntryParticipant
    {
        public bool TryRun(MatchState state, out MatchState preparedState, out string diagnosticCode)
        {
            preparedState = state;
            diagnosticCode = string.Empty;
            return true;
        }
    }
}
