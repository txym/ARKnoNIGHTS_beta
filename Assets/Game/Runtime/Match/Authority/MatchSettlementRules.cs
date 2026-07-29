using System;
using System.Collections.Generic;
using System.Linq;

namespace ArknoNights.Match
{
    internal sealed class MatchSettlementCalculation
    {
        internal MatchSettlementCalculation(
            IReadOnlyList<MatchSeatState> seats,
            IReadOnlyList<MatchAppliedResult> appliedResults,
            IReadOnlyList<string> newlyEliminatedPlayerIds,
            bool competitivelyEnded)
        {
            Seats = seats;
            AppliedResults = appliedResults;
            NewlyEliminatedPlayerIds = newlyEliminatedPlayerIds;
            CompetitivelyEnded = competitivelyEnded;
        }

        internal IReadOnlyList<MatchSeatState> Seats { get; }
        internal IReadOnlyList<MatchAppliedResult> AppliedResults { get; }
        internal IReadOnlyList<string> NewlyEliminatedPlayerIds { get; }
        internal bool CompetitivelyEnded { get; }
    }

    internal static class MatchSettlementRules
    {
        internal static bool TryCalculate(
            MatchState state,
            out MatchSettlementCalculation calculation,
            out string diagnosticCode)
        {
            calculation = null;
            var plan = state.Flow.SealedRoundPlan;
            if (plan == null || plan.RoundNumber != state.RoundNumber)
            {
                diagnosticCode = "match.settlement.plan.missing";
                return false;
            }
            var resolutions = state.Flow.BattleResolutions.ToDictionary(
                result => result.BattleId,
                StringComparer.Ordinal);
            var aliveBefore = state.Seats.Where(seat => !seat.Eliminated).ToArray();
            var applied = new List<MatchAppliedResult>();
            foreach (var pairing in plan.Pairings.OrderBy(item => item.BattleIndex))
            {
                if (!resolutions.TryGetValue(pairing.BattleId, out var resolution))
                {
                    diagnosticCode = "match.settlement.result.missing";
                    return false;
                }
                foreach (var recipient in pairing.SettlementRecipients)
                {
                    var isHome = string.Equals(
                        recipient,
                        pairing.HomePlayerId,
                        StringComparison.Ordinal);
                    var isAway = string.Equals(
                        recipient,
                        pairing.AwayPlayerId,
                        StringComparison.Ordinal);
                    if (isHome == isAway)
                    {
                        diagnosticCode = "match.settlement.recipient.invalid";
                        return false;
                    }
                    applied.Add(new MatchAppliedResult(
                        recipient,
                        pairing.BattleId,
                        isHome ? resolution.HomeLifeDamage : resolution.AwayLifeDamage,
                        ToAppliedOutcome(resolution.Outcome, isHome),
                        isHome ? MatchBattleRole.Home : MatchBattleRole.Away,
                        pairing.Kind == MatchPairingKind.Shadow));
                }
            }
            var appliedCounts = applied
                .GroupBy(result => result.PlayerId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            if (aliveBefore.Any(seat =>
                !appliedCounts.TryGetValue(seat.PlayerId, out var count) || count != 1)
                || applied.Count != aliveBefore.Length)
            {
                diagnosticCode = "match.settlement.appliedResult.cardinality";
                return false;
            }

            try
            {
                var byPlayer = applied.ToDictionary(
                    result => result.PlayerId,
                    StringComparer.Ordinal);
                var afterDamage = state.Seats.Select(seat =>
                {
                    if (seat.Eliminated)
                    {
                        return seat;
                    }
                    var result = byPlayer[seat.PlayerId];
                    var nextLife = checked(seat.Life - result.LifeDamage);
                    var streak = UpdateStreak(seat, result.Outcome);
                    return seat.With(
                        life: nextLife,
                        streakKind: streak.Kind,
                        streakCount: streak.Count);
                }).ToArray();
                var newlyEliminated = afterDamage
                    .Where(seat => !state.FindSeat(seat.PlayerId).Eliminated && seat.Life <= 0)
                    .ToArray();
                var survivorCount = afterDamage.Count(seat => !seat.Eliminated && seat.Life > 0);
                var ranked = afterDamage.Select(seat =>
                {
                    if (seat.Eliminated)
                    {
                        return seat;
                    }
                    if (seat.Life <= 0)
                    {
                        var placement = survivorCount
                            + 1
                            + newlyEliminated.Count(other => other.Life > seat.Life);
                        return seat.With(
                            ready: false,
                            eliminated: true,
                            placement: placement,
                            preservePlacement: false,
                            connectionState: MatchConnectionState.Eliminated);
                    }
                    return seat;
                }).ToArray();

                var rewarded = ranked.Select(seat =>
                {
                    if (seat.Eliminated)
                    {
                        return seat;
                    }
                    var result = byPlayer[seat.PlayerId];
                    var income = GetBaseIncome(state.RoundNumber);
                    var streakReward = GetStreakReward(seat.StreakKind, seat.StreakCount);
                    var drawGold = state.RoundNumber >= 9
                        && result.Outcome == MatchAppliedOutcome.Draw
                            ? income.Gold / 2
                            : 0;
                    var drawCost = state.RoundNumber >= 9
                        && result.Outcome == MatchAppliedOutcome.Draw
                            ? income.Cost / 2
                            : 0;
                    var gold = checked(
                        seat.Gold + income.Gold + streakReward.Gold + drawGold);
                    var cost = checked(income.Cost + streakReward.Cost + drawCost);
                    return seat.With(
                        gold: gold,
                        totalDeploymentCost: checked(seat.TotalDeploymentCost + cost),
                        availableDeploymentCost: checked(seat.AvailableDeploymentCost + cost));
                }).ToArray();
                var survivors = rewarded.Where(seat => !seat.Eliminated).ToArray();
                if (survivors.Length == 1)
                {
                    rewarded = rewarded.Select(seat =>
                        string.Equals(
                            seat.PlayerId,
                            survivors[0].PlayerId,
                            StringComparison.Ordinal)
                            ? seat.With(placement: 1, preservePlacement: false)
                            : seat).ToArray();
                }

                calculation = new MatchSettlementCalculation(
                    rewarded,
                    applied.OrderBy(item => item.PlayerId, StringComparer.Ordinal).ToArray(),
                    newlyEliminated.Select(seat => seat.PlayerId).ToArray(),
                    survivors.Length <= 1);
                diagnosticCode = string.Empty;
                return true;
            }
            catch (OverflowException)
            {
                diagnosticCode = "match.settlement.numeric.overflow";
                return false;
            }
        }

        internal static MatchAppliedOutcome ToAppliedOutcome(
            MatchBattleOutcome outcome,
            bool isHome)
        {
            if (outcome == MatchBattleOutcome.Draw)
            {
                return MatchAppliedOutcome.Draw;
            }
            var homeWon = outcome == MatchBattleOutcome.HomeWin;
            return homeWon == isHome
                ? MatchAppliedOutcome.Win
                : MatchAppliedOutcome.Loss;
        }

        internal static MatchResourceReward GetBaseIncome(int roundNumber)
        {
            if (roundNumber <= 4) return new MatchResourceReward(9, 20);
            if (roundNumber <= 8) return new MatchResourceReward(11, 24);
            if (roundNumber <= 12) return new MatchResourceReward(13, 28);
            if (roundNumber <= 16) return new MatchResourceReward(15, 32);
            if (roundNumber <= 20) return new MatchResourceReward(17, 36);
            return new MatchResourceReward(19, 40);
        }

        internal static MatchResourceReward GetStreakReward(
            MatchStreakKind kind,
            int count)
        {
            if (kind == MatchStreakKind.None || count < 2)
            {
                return new MatchResourceReward(0, 0);
            }
            if (kind == MatchStreakKind.Win)
            {
                if (count <= 3) return new MatchResourceReward(2, 0);
                if (count <= 5) return new MatchResourceReward(4, 0);
                return new MatchResourceReward(6, 0);
            }
            if (count <= 3) return new MatchResourceReward(1, 4);
            if (count <= 5) return new MatchResourceReward(2, 8);
            return new MatchResourceReward(3, 12);
        }

        private static MatchStreak UpdateStreak(
            MatchSeatState seat,
            MatchAppliedOutcome outcome)
        {
            if (outcome == MatchAppliedOutcome.Draw)
            {
                return new MatchStreak(MatchStreakKind.None, 0);
            }
            var kind = outcome == MatchAppliedOutcome.Win
                ? MatchStreakKind.Win
                : MatchStreakKind.Loss;
            return new MatchStreak(
                kind,
                seat.StreakKind == kind ? checked(seat.StreakCount + 1) : 1);
        }

        internal struct MatchResourceReward
        {
            internal MatchResourceReward(int gold, int cost)
            {
                Gold = gold;
                Cost = cost;
            }

            internal int Gold { get; }
            internal int Cost { get; }
        }

        private struct MatchStreak
        {
            internal MatchStreak(MatchStreakKind kind, int count)
            {
                Kind = kind;
                Count = count;
            }

            internal MatchStreakKind Kind { get; }
            internal int Count { get; }
        }
    }
}
