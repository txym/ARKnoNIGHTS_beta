using System;
using System.Collections.Generic;
using System.Linq;

namespace ArknoNights.Match
{
    public sealed partial class MatchAuthority
    {
        public const long PreparationDurationMs = 30000;

        public MatchTransactionResult AdvancePreparationClock(long hostMonotonicNowMs)
        {
            if (state.Phase != MatchPhase.Preparation || !state.Flow.HasPreparationClock)
            {
                return TransactionRejected(
                    MatchCommandCode.InvalidTransition,
                    "match.clock.phase.rejected");
            }
            if (hostMonotonicNowMs < state.Flow.LastHostMonotonicMs)
            {
                return TransactionRejected(
                    MatchCommandCode.ClockRegressed,
                    "match.clock.regressed");
            }
            if (hostMonotonicNowMs >= state.Flow.PreparationDeadlineHostMonotonicMs)
            {
                var deadlineState = state.WithFlow(
                    state.Flow.With(lastHostMonotonicMs: hostMonotonicNowMs),
                    false);
                if (!TryBuildSealedState(
                    deadlineState,
                    MatchSealTrigger.PreparationDeadlineReached,
                    checked(state.StateRevision + 1),
                    out var sealedState,
                    out var code,
                    out var diagnosticCode))
                {
                    return code == MatchCommandCode.FatalMatchError
                        ? CommitHost(sealedState, diagnosticCode, code)
                        : TransactionRejected(code, diagnosticCode);
                }
                return CommitHost(sealedState, "match.seal.timeout.accepted");
            }
            if (hostMonotonicNowMs == state.Flow.LastHostMonotonicMs)
            {
                return TransactionNoChange("match.clock.noChange");
            }

            var revisionBeforeAdvance = state.StateRevision;
            var botHost = CreateLiveBotHost();
            if (!preparationEntryParticipant.TryAdvance(
                botHost,
                hostMonotonicNowMs,
                out var botDiagnosticCode))
            {
                return TransactionRejected(
                    MatchCommandCode.InternalInvariantViolation,
                    string.IsNullOrWhiteSpace(botDiagnosticCode)
                        ? "match.ai.advance.invalid"
                        : botDiagnosticCode);
            }
            if (state.Phase != MatchPhase.Preparation
                || !state.Flow.HasPreparationClock)
            {
                return TransactionRejected(
                    MatchCommandCode.InvalidTransition,
                    "match.clock.phase.changedDuringBotAdvance");
            }
            state = state.WithFlow(
                state.Flow.With(lastHostMonotonicMs: hostMonotonicNowMs),
                false);
            if (state.StateRevision == revisionBeforeAdvance)
            {
                return TransactionNoChange(
                    "match.clock.advanced.noStateChange");
            }
            return new MatchTransactionResult(
                MatchCommandCode.Accepted,
                state.StateRevision,
                state.StateRevision,
                true,
                "match.ai.advance.accepted");
        }

        public MatchTransactionResult TryExecuteBotFormation(
            string playerId,
            MatchCommandPayload command)
        {
            var draft = new MatchEconomyTransactionDraft(state);
            var result = MatchFormationService.TryApply(
                state,
                draft,
                playerId,
                command,
                MatchFormationOperationOrigin.BotAuthority);
            if (!result.Accepted)
            {
                return TransactionRejected(result.Code, result.DiagnosticCode);
            }
            if (!result.Changed)
            {
                return TransactionNoChange(result.DiagnosticCode);
            }
            return CommitHost(
                draft.BuildState(true),
                result.DiagnosticCode,
                result.Code);
        }

        public MatchTransactionResult TrySubmitBattleResolution(
            MatchBattleResolution resolution)
        {
            if (state.Phase != MatchPhase.Battle || state.Flow.SealedRoundPlan == null)
            {
                return TransactionRejected(
                    MatchCommandCode.InvalidTransition,
                    "match.battleResolution.phase.rejected");
            }
            if (resolution == null
                || string.IsNullOrWhiteSpace(resolution.BattleId)
                || resolution.HomeLifeDamage < 0
                || resolution.AwayLifeDamage < 0
                || resolution.EndTick < 0
                || !Enum.IsDefined(typeof(MatchBattleOutcome), resolution.Outcome)
                || !Enum.IsDefined(typeof(MatchBattleTerminalReason), resolution.TerminalReason))
            {
                return TransactionRejected(
                    MatchCommandCode.InvalidPayload,
                    "match.battleResolution.payload.invalid");
            }
            var pairing = state.Flow.SealedRoundPlan.Pairings.FirstOrDefault(item =>
                string.Equals(item.BattleId, resolution.BattleId, StringComparison.Ordinal));
            if (pairing == null)
            {
                return TransactionRejected(
                    MatchCommandCode.BattleIdUnexpected,
                    "match.battleResolution.battleId.unexpected");
            }
            if (!string.Equals(
                pairing.SealedInputHash,
                resolution.SealedInputHash,
                StringComparison.Ordinal))
            {
                return TransactionRejected(
                    MatchCommandCode.SealedInputHashMismatch,
                    "match.battleResolution.hash.mismatch");
            }
            if (resolution.Outcome != MatchBattleResolution.DeriveOutcome(
                resolution.HomeLifeDamage,
                resolution.AwayLifeDamage))
            {
                return TransactionRejected(
                    MatchCommandCode.BattleOutcomeMismatch,
                    "match.battleResolution.outcome.mismatch");
            }
            var existing = state.Flow.BattleResolutions.FirstOrDefault(item =>
                string.Equals(item.BattleId, resolution.BattleId, StringComparison.Ordinal));
            if (existing != null)
            {
                return string.Equals(
                    existing.CanonicalSummary,
                    resolution.CanonicalSummary,
                    StringComparison.Ordinal)
                    ? TransactionNoChange("match.battleResolution.duplicate")
                    : TransactionRejected(
                        MatchCommandCode.BattleResolutionConflict,
                        "match.battleResolution.conflict");
            }

            var nextFlow = state.Flow.With(
                battleResolutions: state.Flow.BattleResolutions.Concat(new[] { resolution }));
            return CommitHost(
                state.WithFlow(nextFlow, true),
                "match.battleResolution.accepted");
        }

        public MatchTransactionResult TryMarkBattlePlaybackCompleted()
        {
            if (state.Phase != MatchPhase.Battle || state.Flow.SealedRoundPlan == null)
            {
                return TransactionRejected(
                    MatchCommandCode.InvalidTransition,
                    "match.playback.phase.rejected");
            }
            if (state.Flow.BattlePlaybackCompleted)
            {
                return TransactionNoChange("match.playback.alreadyCompleted");
            }
            if (!HasCompleteBattleResults())
            {
                return TransactionRejected(
                    MatchCommandCode.BattleResultsIncomplete,
                    "match.playback.results.incomplete");
            }
            return CommitHost(
                state.WithFlow(
                    state.Flow.With(battlePlaybackCompleted: true),
                    true),
                "match.playback.completed");
        }

        public MatchTransactionResult TryCommitRoundSettlement(
            long nextPreparationHostMonotonicNowMs)
        {
            return TryCommitRoundSettlement(
                state.RoundNumber,
                nextPreparationHostMonotonicNowMs);
        }

        public MatchTransactionResult TryCommitRoundSettlement(
            int roundNumber,
            long nextPreparationHostMonotonicNowMs)
        {
            if (state.Flow.SettlementRecords.Any(
                record => record.RoundNumber == roundNumber))
            {
                return TransactionNoChange("match.settlement.alreadyApplied");
            }
            if (roundNumber != state.RoundNumber)
            {
                return TransactionRejected(
                    MatchCommandCode.InvalidTransition,
                    "match.settlement.round.rejected");
            }
            if (state.Phase != MatchPhase.Battle || state.Flow.SealedRoundPlan == null)
            {
                return TransactionRejected(
                    MatchCommandCode.InvalidTransition,
                    "match.settlement.phase.rejected");
            }
            if (!HasCompleteBattleResults())
            {
                return TransactionRejected(
                    MatchCommandCode.BattleResultsIncomplete,
                    "match.settlement.results.incomplete");
            }
            if (!state.Flow.BattlePlaybackCompleted)
            {
                return TransactionRejected(
                    MatchCommandCode.BattlePlaybackIncomplete,
                    "match.settlement.playback.incomplete");
            }
            if (nextPreparationHostMonotonicNowMs < state.Flow.LastHostMonotonicMs)
            {
                return TransactionRejected(
                    MatchCommandCode.ClockRegressed,
                    "match.settlement.clock.regressed");
            }
            if (!MatchSettlementRules.TryCalculate(
                state,
                out var calculation,
                out var diagnosticCode))
            {
                return TransactionRejected(
                    MatchCommandCode.InternalInvariantViolation,
                    diagnosticCode);
            }

            var targetRevision = checked(state.StateRevision + 1);
            var settlementFlow = state.Flow.With(
                battlePlaybackCompleted: true,
                externalEffects: AppendEffects(
                    state.Flow.ExternalEffects,
                    targetRevision,
                    state.RoundNumber,
                    MatchExternalEffectKind.RoundSettlementCommitted));
            var intermediate = state.Rebuild(
                state.StateRevision,
                MatchPhase.Settlement,
                state.RoundNumber,
                calculation.Seats,
                state.Pool,
                string.Empty,
                settlementFlow);
            var economyDraft = new MatchEconomyTransactionDraft(intermediate);
            var poolExhausted = economyDraft.ApplyPostBattleNaturalRefresh(state.RoundNumber);
            economyDraft.ResetRoundBehaviorFacts();
            var refreshed = economyDraft.BuildState(false);
            var settlementRecord = new MatchSettlementRecord(
                state.RoundNumber,
                calculation.AppliedResults,
                calculation.NewlyEliminatedPlayerIds,
                state.Flow.SealedRoundPlan.CanonicalSummary,
                state.Flow.BattleResolutions,
                BuildSettlementOutputCanonicalSummary(refreshed));
            refreshed = refreshed.WithFlow(
                refreshed.Flow.With(
                    settlementRecords: refreshed.Flow.SettlementRecords.Concat(
                        new[] { settlementRecord })),
                false);

            MatchState next;
            if (calculation.CompetitivelyEnded)
            {
                var standings = refreshed.Seats.Select(
                    seat => new MatchStanding(seat.PlayerId, seat.Life, seat.Placement));
                var endFlow = refreshed.Flow.With(
                    hasPreparationClock: false,
                    endReason: MatchEndReason.CompetitiveCompleted,
                    finalStandings: standings,
                    externalEffects: AppendEffects(
                        refreshed.Flow.ExternalEffects,
                        targetRevision,
                        state.RoundNumber,
                        MatchExternalEffectKind.MatchEnded,
                        MatchExternalEffectKind.ReturnAllToMainMenu));
                next = refreshed.Rebuild(
                    targetRevision,
                    MatchPhase.Ended,
                    state.RoundNumber,
                    refreshed.Seats,
                    refreshed.Pool,
                    MatchEndReason.CompetitiveCompleted.ToString(),
                    endFlow);
            }
            else
            {
                if (!TryBuildPreparationState(
                    refreshed,
                    checked(state.RoundNumber + 1),
                    nextPreparationHostMonotonicNowMs,
                    targetRevision,
                    out next,
                    out diagnosticCode))
                {
                    return TransactionRejected(
                        MatchCommandCode.InternalInvariantViolation,
                        diagnosticCode);
                }
                var botHost = CreatePreparationEntryBotHost(next);
                if (!preparationEntryParticipant.TryRun(
                    botHost,
                    out diagnosticCode)
                    || botHost.Current == null
                    || botHost.Current.StateRevision != targetRevision
                    || botHost.Current.Phase != MatchPhase.Preparation
                    || botHost.Current.RoundNumber != next.RoundNumber)
                {
                    return TransactionRejected(
                        MatchCommandCode.InternalInvariantViolation,
                        string.IsNullOrWhiteSpace(diagnosticCode)
                            ? "match.preparation.participant.invalid"
                            : diagnosticCode);
                }
                next = botHost.Current;
                if (!HasAnyRequiredHuman(next))
                {
                    if (!TryBuildSealedState(
                        next,
                        MatchSealTrigger.NoRequiredHumansAfterBotEntryCycle,
                        targetRevision,
                        out next,
                        out var sealCode,
                        out diagnosticCode))
                    {
                        return sealCode == MatchCommandCode.FatalMatchError
                            ? CommitHost(next, diagnosticCode, sealCode)
                            : TransactionRejected(sealCode, diagnosticCode);
                    }
                }
            }
            return CommitHost(
                next,
                poolExhausted
                    ? "match.settlement.accepted.poolExhausted"
                    : "match.settlement.accepted",
                poolExhausted
                    ? MatchCommandCode.PoolExhaustedDiagnostic
                    : MatchCommandCode.Accepted);
        }

        public MatchTransactionResult TryCommitRoundSettlement()
        {
            return TryCommitRoundSettlement(state.Flow.LastHostMonotonicMs);
        }

        public MatchTransactionResult AbortMatchNoContest(string reason)
        {
            if (state.Phase == MatchPhase.Ended)
            {
                return TransactionNoChange("match.abort.alreadyEnded");
            }
            if (string.IsNullOrWhiteSpace(reason))
            {
                return TransactionRejected(
                    MatchCommandCode.InvalidPayload,
                    "match.abort.reason.invalid");
            }
            var targetRevision = checked(state.StateRevision + 1);
            var standings = state.Seats.Select(
                seat => new MatchStanding(seat.PlayerId, seat.Life, seat.Placement));
            var flow = state.Flow.With(
                hasPreparationClock: false,
                endReason: MatchEndReason.NoContest,
                abortDiagnostic: reason,
                finalStandings: standings,
                externalEffects: AppendEffects(
                    state.Flow.ExternalEffects,
                    targetRevision,
                    state.RoundNumber,
                    MatchExternalEffectKind.MatchEnded,
                    MatchExternalEffectKind.ReturnAllToMainMenu));
            var next = state.Rebuild(
                targetRevision,
                MatchPhase.Ended,
                state.RoundNumber,
                state.Seats,
                state.Pool,
                MatchEndReason.NoContest.ToString(),
                flow);
            return CommitHost(next, "match.abort.noContest.accepted");
        }

        private MatchTransactionResult EnterPreparation(
            int roundNumber,
            long hostMonotonicNowMs)
        {
            if (state.Phase == MatchPhase.Ended)
            {
                return TransactionRejected(MatchCommandCode.InvalidTransition, "match.phase.ended");
            }
            var expectedRound = state.RoundNumber + 1;
            if ((state.Phase != MatchPhase.Initializing && state.Phase != MatchPhase.Settlement)
                || roundNumber != expectedRound)
            {
                return TransactionRejected(
                    MatchCommandCode.InvalidTransition,
                    "match.phase.preparation.invalid");
            }
            if (hostMonotonicNowMs < state.Flow.LastHostMonotonicMs)
            {
                return TransactionRejected(MatchCommandCode.ClockRegressed, "match.clock.regressed");
            }
            var targetRevision = checked(state.StateRevision + 1);
            if (!TryBuildPreparationState(
                state,
                roundNumber,
                hostMonotonicNowMs,
                targetRevision,
                out var prepared,
                out var diagnosticCode))
            {
                return TransactionRejected(
                    MatchCommandCode.InternalInvariantViolation,
                    diagnosticCode);
            }
            var botHost = CreatePreparationEntryBotHost(prepared);
            if (!preparationEntryParticipant.TryRun(
                botHost,
                out diagnosticCode)
                || botHost.Current == null
                || botHost.Current.StateRevision != prepared.StateRevision
                || botHost.Current.Phase != MatchPhase.Preparation
                || botHost.Current.RoundNumber != prepared.RoundNumber)
            {
                return TransactionRejected(
                    MatchCommandCode.InternalInvariantViolation,
                    string.IsNullOrWhiteSpace(diagnosticCode)
                        ? "match.preparation.participant.invalid"
                        : diagnosticCode);
            }
            prepared = botHost.Current;
            if (!HasAnyRequiredHuman(prepared))
            {
                if (!TryBuildSealedState(
                    prepared,
                    MatchSealTrigger.NoRequiredHumansAfterBotEntryCycle,
                    targetRevision,
                    out prepared,
                    out var code,
                    out diagnosticCode))
                {
                    return code == MatchCommandCode.FatalMatchError
                        ? CommitHost(prepared, diagnosticCode, code)
                        : TransactionRejected(code, diagnosticCode);
                }
            }
            return CommitHost(prepared, "match.phase.preparation.accepted");
        }

        private bool TryBuildPreparationState(
            MatchState source,
            int roundNumber,
            long hostMonotonicNowMs,
            long targetRevision,
            out MatchState prepared,
            out string diagnosticCode)
        {
            prepared = null;
            long deadline;
            try
            {
                deadline = checked(hostMonotonicNowMs + PreparationDurationMs);
            }
            catch (OverflowException)
            {
                diagnosticCode = "match.clock.deadline.overflow";
                return false;
            }
            var seats = source.Seats.Select(seat =>
                !seat.Eliminated
                    ? seat.With(
                        ready: false,
                        preparationBehavior: MatchPreparationBehaviorState.Empty)
                    : seat).ToArray();
            var flow = source.Flow.With(
                hasPreparationClock: true,
                preparationStartedAtHostMonotonicMs: hostMonotonicNowMs,
                preparationDeadlineHostMonotonicMs: deadline,
                lastHostMonotonicMs: hostMonotonicNowMs,
                clearLastSealTrigger: true,
                clearSealedRoundPlan: true,
                battleResolutions: Array.Empty<MatchBattleResolution>(),
                battlePlaybackCompleted: false,
                externalEffects: AppendEffects(
                    source.Flow.ExternalEffects,
                    targetRevision,
                    roundNumber,
                    MatchExternalEffectKind.PreparationOpened));
            prepared = source.Rebuild(
                targetRevision,
                MatchPhase.Preparation,
                roundNumber,
                seats,
                source.Pool,
                string.Empty,
                flow);
            var economyDraft =
                new MatchEconomyTransactionDraft(prepared);
            if (!economyDraft.TryResolvePreparationEntryFusions(
                out _,
                out diagnosticCode))
            {
                prepared = null;
                return false;
            }
            prepared = economyDraft.BuildState(false);
            diagnosticCode = string.Empty;
            return true;
        }

        private bool TryBuildReadyState(
            MatchSeatState seat,
            bool desiredReady,
            out MatchState nextState,
            out MatchCommandCode code,
            out string diagnosticCode)
        {
            var targetRevision = checked(state.StateRevision + 1);
            var seats = state.Seats.Select(candidate =>
                candidate.SeatIndex == seat.SeatIndex
                    ? candidate.With(ready: desiredReady)
                    : candidate).ToArray();
            var readyState = state.Rebuild(
                state.StateRevision,
                state.Phase,
                state.RoundNumber,
                seats,
                state.Pool,
                state.EndReason,
                state.Flow);
            if (AllRequiredHumansReadyIn(readyState))
            {
                return TryBuildSealedState(
                    readyState,
                    MatchSealTrigger.AllRequiredHumansReady,
                    targetRevision,
                    out nextState,
                    out code,
                    out diagnosticCode);
            }
            nextState = readyState.Rebuild(
                targetRevision,
                readyState.Phase,
                readyState.RoundNumber,
                readyState.Seats,
                readyState.Pool,
                readyState.EndReason,
                readyState.Flow);
            code = MatchCommandCode.Accepted;
            diagnosticCode = "match.ready.accepted";
            return true;
        }

        private bool TryBuildSealedState(
            MatchState source,
            MatchSealTrigger trigger,
            long targetRevision,
            out MatchState sealedState,
            out MatchCommandCode code,
            out string diagnosticCode)
        {
            var draft = new MatchEconomyTransactionDraft(source);
            if (source.RoundNumber == 1)
            {
                foreach (var originalSeat in source.Seats
                    .Where(seat =>
                        !seat.Eliminated
                        && seat.ControllerKind == MatchControllerKind.Human)
                    .OrderBy(seat => seat.SeatIndex))
                {
                    var seat = draft.GetSeat(originalSeat.PlayerId);
                    if (seat.PreparationBehavior.SuccessfulShopPurchaseCount != 0
                        || seat.PreparationBehavior.HasIssuedEffectiveFreezeThisRound)
                    {
                        continue;
                    }
                    var offer = seat.ShopOffers
                        .Where(item => !item.IsEmpty)
                        .OrderByDescending(item => item.SlotIndex)
                        .FirstOrDefault();
                    if (offer == null)
                    {
                        continue;
                    }
                    var childSource = draft.BuildState(false);
                    var child = new MatchEconomyTransactionDraft(childSource);
                    if (!child.TryPurchaseShopOffer(
                        seat.PlayerId,
                        offer.SlotIndex,
                        offer.UnitId,
                        stagingSlotPolicy,
                        out var acquisition,
                        out var purchaseCode,
                        out var purchaseDiagnostic))
                    {
                        if (purchaseCode == MatchCommandCode.InternalInvariantViolation)
                        {
                            return BuildFatalState(
                                source,
                                targetRevision,
                                purchaseDiagnostic,
                                out sealedState,
                                out code,
                                out diagnosticCode);
                        }
                        continue;
                    }
                    draft = child;
                    if (acquisition != null
                        && !string.IsNullOrWhiteSpace(acquisition.FinalSurvivorUnitId))
                    {
                        var survivor = draft.GetSeat(seat.PlayerId).Units.FirstOrDefault(unit =>
                            string.Equals(
                                unit.UnitId,
                                acquisition.FinalSurvivorUnitId,
                                StringComparison.Ordinal));
                        if (survivor != null && survivor.Zone == MatchUnitZone.Staging)
                        {
                            MatchFormationService.TryApply(
                                childSource,
                                draft,
                                seat.PlayerId,
                                new DeployUnitCommand(
                                    survivor.UnitId,
                                    new MatchFormationPosition(5, 2),
                                    draft.GetSeat(seat.PlayerId).AvailableDeploymentCost),
                                MatchFormationOperationOrigin.SealSafety);
                        }
                    }
                }
            }

            if (!draft.TryDiscardRemainingOverflow(
                out _,
                out var discardCode,
                out var discardDiagnostic))
            {
                return BuildFatalState(
                    source,
                    targetRevision,
                    discardDiagnostic,
                    out sealedState,
                    out code,
                    out diagnosticCode);
            }
            foreach (var originalSeat in source.Seats
                .Where(seat =>
                    !seat.Eliminated
                    && seat.ControllerKind == MatchControllerKind.Human)
                .OrderBy(seat => seat.SeatIndex))
            {
                var seat = draft.GetSeat(originalSeat.PlayerId);
                if (seat.Units.Any(unit => unit.Zone == MatchUnitZone.Deployed))
                {
                    continue;
                }
                var stacks = MatchStagingProjection.Project(seat, source.Pool.Catalog);
                if (stacks.Count == 0)
                {
                    continue;
                }
                var unitId = stacks[stacks.Count - 1].UnitIds[0];
                MatchFormationService.TryApply(
                    source,
                    draft,
                    seat.PlayerId,
                    new DeployUnitCommand(
                        unitId,
                        new MatchFormationPosition(5, 2),
                        seat.AvailableDeploymentCost),
                    MatchFormationOperationOrigin.SealSafety);
            }

            var postSafety = draft.BuildState(false);
            if (!MatchStateInvariant.TryValidate(
                postSafety,
                stagingSlotPolicy,
                out diagnosticCode))
            {
                return BuildFatalState(
                    source,
                    targetRevision,
                    diagnosticCode,
                    out sealedState,
                    out code,
                    out diagnosticCode);
            }
            if (!MatchPairingScheduler.TryBuild(
                postSafety,
                out var schedule,
                out diagnosticCode))
            {
                return BuildFatalState(
                    source,
                    targetRevision,
                    diagnosticCode,
                    out sealedState,
                    out code,
                    out diagnosticCode);
            }
            var plan = MatchPairingScheduler.BuildPlan(postSafety, schedule);
            var history = postSafety.Flow.PairingHistory.Concat(
                plan.Pairings.Select(
                    pairing => new MatchPairingHistoryEntry(
                        postSafety.RoundNumber,
                        schedule.Generation,
                        pairing)));
            var effects = AppendEffects(
                postSafety.Flow.ExternalEffects,
                targetRevision,
                postSafety.RoundNumber,
                MatchExternalEffectKind.RoundSealed,
                MatchExternalEffectKind.BattlePlanReady);
            var systemActionIds = postSafety.Flow.SystemActionIds.Concat(
                source.Seats
                    .Where(seat =>
                        !seat.Eliminated
                        && seat.ControllerKind == MatchControllerKind.Human)
                    .SelectMany(seat => source.RoundNumber == 1
                        ? new[]
                        {
                            MatchSystemActionId.Derive(
                                source.SessionId,
                                source.RoundNumber,
                                seat.SeatIndex,
                                "round-one-safe-purchase"),
                            MatchSystemActionId.Derive(
                                source.SessionId,
                                source.RoundNumber,
                                seat.SeatIndex,
                                "empty-formation-safe-deploy")
                        }
                        : new[]
                        {
                            MatchSystemActionId.Derive(
                                source.SessionId,
                                source.RoundNumber,
                                seat.SeatIndex,
                                "empty-formation-safe-deploy")
                        }))
                .Concat(new[]
                {
                    MatchSystemActionId.Derive(
                        source.SessionId,
                        source.RoundNumber,
                        0,
                        "discard-remaining-overflow")
                });
            var flow = postSafety.Flow.With(
                hasPreparationClock: false,
                lastSealTrigger: trigger,
                pairingGeneration: schedule.Generation,
                pairingPopulation: schedule.Population,
                pairingOffset: schedule.Offset,
                pairingSeatOrder: schedule.SeatOrder,
                pairingHistory: history,
                pairingDiagnostics: schedule.Diagnostics,
                sealedRoundPlan: plan,
                battleResolutions: Array.Empty<MatchBattleResolution>(),
                battlePlaybackCompleted: false,
                systemActionIds: systemActionIds,
                externalEffects: effects);
            sealedState = postSafety.Rebuild(
                targetRevision,
                MatchPhase.Battle,
                postSafety.RoundNumber,
                postSafety.Seats,
                postSafety.Pool,
                string.Empty,
                flow);
            code = discardCode == MatchCommandCode.PoolExhaustedDiagnostic
                ? MatchCommandCode.PoolExhaustedDiagnostic
                : MatchCommandCode.Accepted;
            diagnosticCode = "match.seal.accepted";
            return true;
        }

        private bool BuildFatalState(
            MatchState source,
            long targetRevision,
            string fatalDiagnostic,
            out MatchState fatalState,
            out MatchCommandCode code,
            out string diagnosticCode)
        {
            var flow = source.Flow.With(
                hasPreparationClock: false,
                endReason: MatchEndReason.FatalMatchError,
                abortDiagnostic: fatalDiagnostic,
                finalStandings: source.Seats.Select(
                    seat => new MatchStanding(seat.PlayerId, seat.Life, seat.Placement)),
                externalEffects: AppendEffects(
                    source.Flow.ExternalEffects,
                    targetRevision,
                    source.RoundNumber,
                    MatchExternalEffectKind.MatchEnded,
                    MatchExternalEffectKind.ReturnAllToMainMenu));
            fatalState = source.Rebuild(
                targetRevision,
                MatchPhase.Ended,
                source.RoundNumber,
                source.Seats,
                source.Pool,
                MatchEndReason.FatalMatchError.ToString(),
                flow);
            code = MatchCommandCode.FatalMatchError;
            diagnosticCode = fatalDiagnostic;
            return false;
        }

        private bool HasCompleteBattleResults()
        {
            var plan = state.Flow.SealedRoundPlan;
            if (plan == null || state.Flow.BattleResolutions.Count != plan.Pairings.Count)
            {
                return false;
            }
            var expected = new HashSet<string>(
                plan.Pairings.Select(item => item.BattleId),
                StringComparer.Ordinal);
            return expected.SetEquals(
                state.Flow.BattleResolutions.Select(item => item.BattleId));
        }

        private static bool AllRequiredHumansReadyIn(MatchState value)
        {
            var connected = value.Seats.Where(seat =>
                !seat.Eliminated
                && seat.ControllerKind == MatchControllerKind.Human
                && seat.ConnectionState == MatchConnectionState.Connected).ToArray();
            return connected.Length > 0
                && connected.All(seat => seat.Ready)
                && !value.Seats.Any(seat =>
                    !seat.Eliminated
                    && seat.ControllerKind == MatchControllerKind.Human
                    && seat.ConnectionState == MatchConnectionState.DisconnectedGrace);
        }

        private static bool HasAnyRequiredHuman(MatchState value)
        {
            return value.Seats.Any(seat =>
                !seat.Eliminated
                && seat.ControllerKind == MatchControllerKind.Human
                && (seat.ConnectionState == MatchConnectionState.Connected
                    || seat.ConnectionState == MatchConnectionState.DisconnectedGrace));
        }

        private static string BuildSettlementOutputCanonicalSummary(MatchState value)
        {
            var writer = new CanonicalSummaryWriter("MatchSettlementOutput");
            writer.Integer("round", value.RoundNumber);
            foreach (var seat in value.Seats.OrderBy(seat => seat.SeatIndex))
            {
                writer.Summary("seat", seat.CanonicalSummary);
            }
            writer.Summary("pool", value.Pool.CanonicalSummary);
            return writer.ToString();
        }

        private MatchCommandResult ExecuteFormationCommand(
            CommandKey key,
            MatchCommandEnvelope envelope)
        {
            var draft = new MatchEconomyTransactionDraft(state);
            var operation = MatchFormationService.TryApply(
                state,
                draft,
                envelope.PlayerId,
                envelope.Payload,
                MatchFormationOperationOrigin.HumanCommand);
            if (!operation.Accepted)
            {
                return Cache(
                    key,
                    envelope,
                    CommandResult(
                        envelope.CommandId,
                        operation.Code,
                        false,
                        null,
                        operation.DiagnosticCode));
            }
            if (!operation.Changed)
            {
                return Cache(
                    key,
                    envelope,
                    CommandResult(
                        envelope.CommandId,
                        MatchCommandCode.AcceptedNoChange,
                        false,
                        state.StateRevision,
                        operation.DiagnosticCode));
            }
            return CommitCommand(
                key,
                envelope,
                draft.BuildState(true),
                operation.Code,
                operation.DiagnosticCode);
        }

        private static IReadOnlyList<MatchExternalEffect> AppendEffects(
            IEnumerable<MatchExternalEffect> existing,
            long sourceRevision,
            int roundNumber,
            params MatchExternalEffectKind[] kinds)
        {
            var result = new List<MatchExternalEffect>(
                existing ?? Enumerable.Empty<MatchExternalEffect>());
            foreach (var kind in kinds)
            {
                var writer = new CanonicalSummaryWriter("MatchEffectIdentity");
                writer.Integer("revision", sourceRevision);
                writer.Integer("round", roundNumber);
                writer.EnumValue("kind", kind);
                result.Add(new MatchExternalEffect(
                    MatchFlowHash.Sha256(writer.ToString()),
                    kind,
                    sourceRevision,
                    roundNumber));
            }
            return result;
        }
    }
}
