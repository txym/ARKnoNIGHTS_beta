using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ArknoNights.Match.Tests
{
    public sealed class MatchFlowEditModeTests
    {
        [Test]
        public void PreparationClock_UsesHostMonotonicDeadline_AndSealsAtThirtySeconds()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());

            var entered = authority.TryEnterPreparation(1, 1000);

            Assert.That(entered.Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(authority.ProjectPublic().PreparationRemainingMs, Is.EqualTo(30000));
            var revisionBeforeClockOnlyAdvance = authority.StateRevision;
            Assert.That(authority.AdvancePreparationClock(30999).Accepted, Is.True);
            Assert.That(authority.ProjectPublic().Phase, Is.EqualTo(MatchPhase.Preparation));
            Assert.That(authority.ProjectPublic().PreparationRemainingMs, Is.EqualTo(1));
            Assert.That(
                authority.StateRevision,
                Is.EqualTo(revisionBeforeClockOnlyAdvance),
                "Clock-only observation must not create a gameplay revision.");

            var sealedResult = authority.AdvancePreparationClock(31000);

            Assert.That(sealedResult.Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(authority.ProjectPublic().Phase, Is.EqualTo(MatchPhase.Battle));
            Assert.That(authority.ProjectForHostAuthority().Flow.LastSealTrigger,
                Is.EqualTo(MatchSealTrigger.PreparationDeadlineReached));
            Assert.That(authority.ProjectForHostAuthority().Flow.SealedRoundPlan, Is.Not.Null);
        }

        [Test]
        public void PreparationClock_RejectsRegression_AndConnectionChangesDoNotMoveDeadline()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1, 1000);
            var deadline = authority.ProjectForHostAuthority()
                .Flow.PreparationDeadlineHostMonotonicMs;
            authority.AdvancePreparationClock(1200);
            var beforeRegression = authority.ProjectForHostAuthority().CanonicalSummary;

            var regressed = authority.AdvancePreparationClock(1199);

            Assert.That(regressed.Code, Is.EqualTo(MatchCommandCode.ClockRegressed));
            Assert.That(authority.ProjectForHostAuthority().CanonicalSummary,
                Is.EqualTo(beforeRegression));
            authority.TrySetConnectionState(
                "player-2",
                MatchConnectionState.DisconnectedGrace);
            authority.TrySetConnectionState("player-2", MatchConnectionState.Connected);
            Assert.That(authority.ProjectForHostAuthority()
                .Flow.PreparationDeadlineHostMonotonicMs, Is.EqualTo(deadline));
        }

        [Test]
        public void TimeoutSeal_PersistsObservedHostClockAcrossSettlement()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1, 0);
            authority.AdvancePreparationClock(30000);
            var plan = authority.ProjectForHostAuthority().Flow.SealedRoundPlan;
            foreach (var pairing in plan.Pairings)
            {
                authority.TrySubmitBattleResolution(Resolution(pairing, 0, 0, 100));
            }
            authority.TryMarkBattlePlaybackCompleted();
            var before = authority.ProjectForHostAuthority().CanonicalSummary;

            var regressed = authority.TryCommitRoundSettlement(1, 29999);

            Assert.That(regressed.Code, Is.EqualTo(MatchCommandCode.ClockRegressed));
            Assert.That(authority.ProjectForHostAuthority().CanonicalSummary,
                Is.EqualTo(before));
            Assert.That(authority.TryCommitRoundSettlement(1, 30000).Accepted, Is.True);
            Assert.That(authority.ProjectForHostAuthority().Flow.LastHostMonotonicMs,
                Is.EqualTo(30000));
        }

        [Test]
        public void FinalReady_SealsInTheSameRevision_AndRetryIsIdempotent()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1, 0);
            Execute(authority, "player-1", "ready-1", new SetPreparationReadyCommand(true));
            Execute(authority, "player-2", "ready-2", new SetPreparationReadyCommand(true));
            Execute(authority, "player-3", "ready-3", new SetPreparationReadyCommand(true));
            var final = new MatchCommandEnvelope(
                "session-1",
                "player-4",
                "ready-4",
                authority.StateRevision,
                new SetPreparationReadyCommand(true));
            var before = authority.StateRevision;

            var accepted = authority.Execute(final);

            Assert.That(accepted.Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(authority.StateRevision, Is.EqualTo(before + 1));
            Assert.That(authority.ProjectPublic().Phase, Is.EqualTo(MatchPhase.Battle));
            Assert.That(authority.ProjectForHostAuthority().Flow.LastSealTrigger,
                Is.EqualTo(MatchSealTrigger.AllRequiredHumansReady));
            Assert.That(authority.Execute(final), Is.SameAs(accepted));
            Assert.That(authority.StateRevision, Is.EqualTo(before + 1));
        }

        [Test]
        public void FinalReady_WhenSealFailsFatally_CommitsEndedStateAndRetryIsIdempotent()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1, 0);
            authority.TryMarkEliminated("player-2", 4);
            authority.TryMarkEliminated("player-3", 3);
            authority.TryMarkEliminated("player-4", 2);
            var final = new MatchCommandEnvelope(
                "session-1",
                "player-1",
                "ready-fatal",
                authority.StateRevision,
                new SetPreparationReadyCommand(true));
            var before = authority.StateRevision;

            var fatal = authority.Execute(final);

            Assert.That(fatal.Code, Is.EqualTo(MatchCommandCode.FatalMatchError));
            Assert.That(fatal.ChangedState, Is.True);
            Assert.That(authority.StateRevision, Is.EqualTo(before + 1));
            Assert.That(authority.ProjectPublic().Phase, Is.EqualTo(MatchPhase.Ended));
            Assert.That(authority.ProjectPublic().EndReason,
                Is.EqualTo(MatchEndReason.FatalMatchError));
            Assert.That(authority.Execute(final), Is.SameAs(fatal));
            Assert.That(authority.StateRevision, Is.EqualTo(before + 1));
        }

        [Test]
        public void DisconnectedGraceHuman_BlocksEarlySeal_ButNotTimeout()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1, 0);
            authority.TrySetConnectionState(
                "player-4",
                MatchConnectionState.DisconnectedGrace);
            Execute(authority, "player-1", "r1", new SetPreparationReadyCommand(true));
            Execute(authority, "player-2", "r2", new SetPreparationReadyCommand(true));
            Execute(authority, "player-3", "r3", new SetPreparationReadyCommand(true));

            Assert.That(authority.ProjectPublic().Phase, Is.EqualTo(MatchPhase.Preparation));
            Assert.That(authority.AllRequiredHumansReady, Is.False);

            Assert.That(authority.AdvancePreparationClock(30000).Accepted, Is.True);
            Assert.That(authority.ProjectPublic().Phase, Is.EqualTo(MatchPhase.Battle));
        }

        [Test]
        public void FormationCommands_DeploySwapAndRetreatAtomically()
        {
            var catalog = MatchFusionTestData.CatalogWithFillers();
            var authority = MatchFusionTestData.CreateAuthority(catalog);
            authority = MatchFusionTestData.WithPlayerOneState(
                authority,
                new[]
                {
                    MatchFusionTestData.Unit(
                        "unit-a", "1001", MatchUnitZone.Staging, 0, 100),
                    MatchFusionTestData.Unit(
                        "unit-b", "7000", MatchUnitZone.Staging, 0, 101)
                });
            authority.TryEnterPreparation(1, 0);

            var deployA = Execute(
                authority,
                "player-1",
                "deploy-a",
                new DeployUnitCommand(
                    "unit-a",
                    new MatchFormationPosition(1, 1),
                    16));
            var deployB = Execute(
                authority,
                "player-1",
                "deploy-b",
                new DeployUnitCommand(
                    "unit-b",
                    new MatchFormationPosition(2, 1),
                    14));

            Assert.That(deployA.Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(deployB.Code, Is.EqualTo(MatchCommandCode.Accepted));
            var beforeSwapCost = authority.ProjectForPlayer("player-1")
                .Owner.AvailableDeploymentCost;

            var swap = Execute(
                authority,
                "player-1",
                "swap",
                new RelocateOrSwapUnitCommand(
                    "unit-a",
                    new MatchFormationPosition(2, 1)));
            var retreat = Execute(
                authority,
                "player-1",
                "retreat",
                new RetreatUnitCommand("unit-b"));

            Assert.That(swap.Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(retreat.Code, Is.EqualTo(MatchCommandCode.Accepted));
            var owner = authority.ProjectForPlayer("player-1").Owner;
            Assert.That(owner.Units.Single(unit => unit.UnitId == "unit-a").Formation,
                Is.EqualTo(new MatchFormationPosition(2, 1)));
            Assert.That(owner.Units.Single(unit => unit.UnitId == "unit-b").Zone,
                Is.EqualTo(MatchUnitZone.Staging));
            Assert.That(owner.AvailableDeploymentCost, Is.EqualTo(beforeSwapCost + 3));
        }

        [Test]
        public void FormationCommands_RejectInvalidGateOccupiedCostAndReadyWithoutMutation()
        {
            var authority = MatchFusionTestData.CreateAuthority();
            authority = MatchFusionTestData.WithPlayerOneState(
                authority,
                new[]
                {
                    MatchFusionTestData.Unit(
                        "unit-a", "1001", MatchUnitZone.Staging, 0, 100)
                });
            authority.TryEnterPreparation(1, 0);
            var initialRevision = authority.StateRevision;

            Assert.That(Execute(
                authority,
                "player-1",
                "gate",
                new DeployUnitCommand(
                    "unit-a",
                    new MatchFormationPosition(5, 1),
                    16)).Code, Is.EqualTo(MatchCommandCode.FormationPositionInvalid));
            Assert.That(Execute(
                authority,
                "player-1",
                "stale-cost",
                new DeployUnitCommand(
                    "unit-a",
                    new MatchFormationPosition(1, 1),
                    15)).Code, Is.EqualTo(MatchCommandCode.DeploymentCostChanged));
            Assert.That(authority.StateRevision, Is.EqualTo(initialRevision));

            Execute(
                authority,
                "player-1",
                "ready",
                new SetPreparationReadyCommand(true));
            Assert.That(Execute(
                authority,
                "player-1",
                "locked",
                new DeployUnitCommand(
                    "unit-a",
                    new MatchFormationPosition(1, 1),
                    16)).Code, Is.EqualTo(MatchCommandCode.FormationLocked));
        }

        [Test]
        public void ReplaceCommand_ReturnsOldCostAndChargesNewCostInOneRevision()
        {
            var catalog = MatchFusionTestData.CatalogWithFillers();
            var authority = MatchFusionTestData.CreateAuthority(catalog);
            authority = MatchFusionTestData.WithPlayerOneState(
                authority,
                new[]
                {
                    MatchFusionTestData.Unit(
                        "deployed", "1001", MatchUnitZone.Deployed, 0, 100,
                        new MatchFormationPosition(2, 2)),
                    MatchFusionTestData.Unit(
                        "incoming", "7000", MatchUnitZone.Staging, 0, 101)
                });
            authority.TryEnterPreparation(1, 0);
            var before = authority.StateRevision;

            var replaced = Execute(
                authority,
                "player-1",
                "replace",
                new ReplaceDeployedUnitCommand(
                    "incoming",
                    "deployed",
                    new MatchFormationPosition(2, 2)));

            Assert.That(replaced.Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(authority.StateRevision, Is.EqualTo(before + 1));
            var owner = authority.ProjectForPlayer("player-1").Owner;
            Assert.That(owner.Units.Single(unit => unit.UnitId == "deployed").Zone,
                Is.EqualTo(MatchUnitZone.Staging));
            Assert.That(owner.Units.Single(unit => unit.UnitId == "incoming").Zone,
                Is.EqualTo(MatchUnitZone.Deployed));
            Assert.That(owner.AvailableDeploymentCost, Is.EqualTo(13));
        }

        [Test]
        public void RoundOneSeal_SafelyPurchasesMaximumSlotAndDeploysFinalSurvivor()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1, 0);
            var ownerBefore = authority.ProjectForPlayer("player-1").Owner;
            var expected = ownerBefore.ShopOffers
                .Where(offer => !offer.IsEmpty)
                .OrderByDescending(offer => offer.SlotIndex)
                .First();

            authority.AdvancePreparationClock(30000);

            var owner = authority.ProjectForPlayer("player-1").Owner;
            Assert.That(owner.Gold, Is.EqualTo(ownerBefore.Gold - expected.Rarity.Value));
            Assert.That(owner.ShopOffers.Single(offer =>
                offer.SlotIndex == expected.SlotIndex).IsEmpty, Is.True);
            Assert.That(owner.PreparationBehavior.SuccessfulShopPurchaseCount, Is.EqualTo(1));
            var purchased = owner.Units.Single(unit => unit.UnitId == expected.UnitId);
            Assert.That(purchased.Zone, Is.EqualTo(MatchUnitZone.Deployed));
            Assert.That(purchased.Formation, Is.EqualTo(new MatchFormationPosition(5, 2)));
            Assert.That(authority.ProjectForHostAuthority().Flow.SystemActionIds,
                Does.Contain(MatchSystemActionId.Derive(
                    "session-1",
                    1,
                    1,
                    "round-one-safe-purchase")));
        }

        [Test]
        public void RoundOneSeal_DoesNotSafetyPurchaseAfterHumanAlreadyPurchased()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1, 0);
            var offer = authority.ProjectForPlayer("player-1").Owner.ShopOffers
                .First(item => !item.IsEmpty);
            Assert.That(Execute(
                authority,
                "player-1",
                "manual-purchase",
                new PurchaseShopOfferCommand(offer.SlotIndex, offer.UnitId)).Accepted, Is.True);
            var afterPurchase = authority.ProjectForPlayer("player-1").Owner;

            authority.AdvancePreparationClock(30000);

            var afterSeal = authority.ProjectForPlayer("player-1").Owner;
            Assert.That(afterSeal.Gold, Is.EqualTo(afterPurchase.Gold));
            Assert.That(afterSeal.PreparationBehavior.SuccessfulShopPurchaseCount,
                Is.EqualTo(1));
        }

        [Test]
        public void FourPlayerPairing_GoldenTableRepeatsAfterSixRounds()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            var observed = new List<string>();
            authority.TryEnterPreparation(1, 0);
            for (var round = 1; round <= 7; round++)
            {
                ReadyAll(authority, round);
                observed.Add(PairingRoles(authority.ProjectForHostAuthority()
                    .Flow.SealedRoundPlan));
                CompleteDrawRound(authority, round * 1000L);
            }

            Assert.That(observed.Take(6), Is.EqualTo(new[]
            {
                "player-1H-player-2A|player-3H-player-4A",
                "player-1H-player-3A|player-2H-player-4A",
                "player-4H-player-1A|player-3H-player-2A",
                "player-2H-player-1A|player-4H-player-3A",
                "player-1H-player-4A|player-2H-player-3A",
                "player-3H-player-1A|player-4H-player-2A"
            }));
            Assert.That(observed[6], Is.EqualTo(observed[0]));
        }

        [Test]
        public void ThreePlayerPairing_UsesOfficialAndShadowGoldenRoles()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryMarkEliminated("player-4", 4);
            authority.TryEnterPreparation(1, 0);

            ReadyAlive(authority, 1);

            var plan = authority.ProjectForHostAuthority().Flow.SealedRoundPlan;
            Assert.That(plan.Pairings.Count, Is.EqualTo(2));
            Assert.That(plan.Pairings[0].Kind, Is.EqualTo(MatchPairingKind.Official));
            Assert.That(plan.Pairings[0].HomePlayerId, Is.EqualTo("player-1"));
            Assert.That(plan.Pairings[0].AwayPlayerId, Is.EqualTo("player-2"));
            Assert.That(plan.Pairings[1].Kind, Is.EqualTo(MatchPairingKind.Shadow));
            Assert.That(plan.Pairings[1].HomePlayerId, Is.EqualTo("player-3"));
            Assert.That(plan.Pairings[1].AwayPlayerId, Is.EqualTo("player-1"));
            Assert.That(plan.Pairings[1].ShadowOwnerPlayerId, Is.EqualTo("player-1"));
            Assert.That(plan.Pairings.SelectMany(pairing => pairing.SettlementRecipients)
                .GroupBy(playerId => playerId)
                .All(group => group.Count() == 1), Is.True);
        }

        [Test]
        public void ThreePlayerPairing_GoldenTableRunsSixRounds()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryMarkEliminated("player-4", 4);
            authority.TryEnterPreparation(1, 0);
            var observed = new List<string>();
            for (var round = 1; round <= 6; round++)
            {
                ReadyAlive(authority, round);
                var plan = authority.ProjectForHostAuthority().Flow.SealedRoundPlan;
                observed.Add(string.Join(
                    "|",
                    plan.Pairings.OrderBy(pairing => pairing.BattleIndex).Select(pairing =>
                        pairing.Kind + ":"
                        + pairing.HomePlayerId + "H-" + pairing.AwayPlayerId + "A"
                        + (pairing.Kind == MatchPairingKind.Shadow
                            ? ":owner=" + pairing.ShadowOwnerPlayerId
                            : string.Empty))));
                CompleteDrawRound(authority, round * 1000L);
            }

            Assert.That(observed, Is.EqualTo(new[]
            {
                "Official:player-1H-player-2A|Shadow:player-3H-player-1A:owner=player-1",
                "Official:player-2H-player-3A|Shadow:player-1H-player-2A:owner=player-2",
                "Official:player-3H-player-1A|Shadow:player-2H-player-3A:owner=player-3",
                "Official:player-2H-player-1A|Shadow:player-1H-player-3A:owner=player-1",
                "Official:player-3H-player-2A|Shadow:player-2H-player-1A:owner=player-2",
                "Official:player-1H-player-3A|Shadow:player-3H-player-2A:owner=player-3"
            }));
        }

        [Test]
        public void TwoPlayerPairing_AlternatesHomeAndAway()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryMarkEliminated("player-3", 4);
            authority.TryMarkEliminated("player-4", 3);
            authority.TryEnterPreparation(1, 0);
            ReadyAlive(authority, 1);
            var first = authority.ProjectForHostAuthority().Flow.SealedRoundPlan.Pairings.Single();
            CompleteDrawRound(authority, 1000);
            ReadyAlive(authority, 2);
            var second = authority.ProjectForHostAuthority().Flow.SealedRoundPlan.Pairings.Single();

            Assert.That(first.HomePlayerId, Is.EqualTo(second.AwayPlayerId));
            Assert.That(first.AwayPlayerId, Is.EqualTo(second.HomePlayerId));
            Assert.That(first.Kind, Is.EqualTo(MatchPairingKind.Official));
            Assert.That(second.Kind, Is.EqualTo(MatchPairingKind.Official));
        }

        [Test]
        public void FourToThreeTransition_AvoidsPreviousPairWhenPossible_AndPersistsDiagnostics()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1, 0);
            ReadyAll(authority, 1);
            CompleteDrawRound(authority, 1000);
            authority.TryMarkEliminated("player-4", 4);

            ReadyAlive(authority, 2);

            var flow = authority.ProjectForHostAuthority().Flow;
            Assert.That(flow.PairingGeneration, Is.EqualTo(2));
            Assert.That(flow.PairingDiagnostics, Is.Not.Null);
            Assert.That(flow.PairingDiagnostics.Population, Is.EqualTo(3));
            Assert.That(flow.PairingDiagnostics.SelectedSeatOrder, Is.EqualTo(
                flow.PairingSeatOrder));
            Assert.That(flow.SealedRoundPlan.Pairings.Any(pairing =>
                IsPair(pairing, "player-1", "player-2")), Is.False);
        }

        [Test]
        public void FourToThreeTransition_UsesSeatIndexRatherThanPlayerIdForFinalTieBreak()
        {
            var standard = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            standard.TryEnterPreparation(1, 0);
            ReadyAlive(standard, 1);
            CompleteDrawRound(standard, 1000);
            standard.TryMarkEliminated("player-4", 4);
            ReadyAlive(standard, 2);

            var renamedSeats = new[]
            {
                MatchTestData.Seat(1, MatchControllerKind.Human, "z-host"),
                MatchTestData.Seat(2, MatchControllerKind.Human, "a-two"),
                MatchTestData.Seat(3, MatchControllerKind.Human, "y-three"),
                MatchTestData.Seat(4, MatchControllerKind.Human, "b-four")
            };
            var renamedResult = MatchSessionFactory.Create(MatchTestData.Request(
                renamedSeats,
                hostPlayerId: "z-host"));
            Assert.That(renamedResult.Success, Is.True);
            var renamed = renamedResult.Authority;
            renamed.TryEnterPreparation(1, 0);
            ReadyAlive(renamed, 1);
            CompleteDrawRound(renamed, 1000);
            renamed.TryMarkEliminated("b-four", 4);
            ReadyAlive(renamed, 2);

            Assert.That(PairingSeatIndexes(renamed), Is.EqualTo(
                PairingSeatIndexes(standard)));
        }

        [Test]
        public void BattleResolution_ValidatesHashOutcomeAndConflictingDuplicates()
        {
            var authority = SealFourPlayerRound();
            var pairing = authority.ProjectForHostAuthority()
                .Flow.SealedRoundPlan.Pairings[0];

            Assert.That(authority.TrySubmitBattleResolution(new MatchBattleResolution(
                pairing.BattleId,
                new string('0', 64),
                1,
                2,
                MatchBattleOutcome.HomeWin,
                MatchBattleTerminalReason.Normal,
                10)).Code, Is.EqualTo(MatchCommandCode.SealedInputHashMismatch));
            Assert.That(authority.TrySubmitBattleResolution(new MatchBattleResolution(
                pairing.BattleId,
                pairing.SealedInputHash,
                1,
                2,
                MatchBattleOutcome.AwayWin,
                MatchBattleTerminalReason.Normal,
                10)).Code, Is.EqualTo(MatchCommandCode.BattleOutcomeMismatch));

            var acceptedResolution = Resolution(pairing, 1, 2, 10);
            var accepted = authority.TrySubmitBattleResolution(acceptedResolution);

            Assert.That(accepted.Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(authority.TrySubmitBattleResolution(acceptedResolution).Code,
                Is.EqualTo(MatchCommandCode.AcceptedNoChange));
            Assert.That(authority.TrySubmitBattleResolution(
                Resolution(pairing, 2, 1, 10)).Code,
                Is.EqualTo(MatchCommandCode.BattleResolutionConflict));
        }

        [Test]
        public void Settlement_RequiresCompleteResultsAndPlaybackGate()
        {
            var authority = SealFourPlayerRound();
            var plan = authority.ProjectForHostAuthority().Flow.SealedRoundPlan;
            authority.TrySubmitBattleResolution(Resolution(plan.Pairings[0], 0, 0, 100));

            Assert.That(authority.TryMarkBattlePlaybackCompleted().Code,
                Is.EqualTo(MatchCommandCode.BattleResultsIncomplete));
            Assert.That(authority.TryCommitRoundSettlement(1, 1000).Code,
                Is.EqualTo(MatchCommandCode.BattleResultsIncomplete));

            authority.TrySubmitBattleResolution(Resolution(plan.Pairings[1], 0, 0, 100));
            Assert.That(authority.TryCommitRoundSettlement(1, 1000).Code,
                Is.EqualTo(MatchCommandCode.BattlePlaybackIncomplete));
            Assert.That(authority.TryMarkBattlePlaybackCompleted().Accepted, Is.True);
            Assert.That(authority.TryCommitRoundSettlement(1, 1000).Accepted, Is.True);
        }

        [Test]
        public void ShadowOwner_IgnoresShadowDamageAndReceivesOnlyOfficialResult()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryMarkEliminated("player-4", 4);
            authority.TryEnterPreparation(1, 0);
            ReadyAlive(authority, 1);
            var plan = authority.ProjectForHostAuthority().Flow.SealedRoundPlan;
            var official = plan.Pairings.Single(item => item.Kind == MatchPairingKind.Official);
            var shadow = plan.Pairings.Single(item => item.Kind == MatchPairingKind.Shadow);
            authority.TrySubmitBattleResolution(Resolution(official, 20, 0, 100));
            authority.TrySubmitBattleResolution(Resolution(shadow, 30, 100, 100));
            authority.TryMarkBattlePlaybackCompleted();

            authority.TryCommitRoundSettlement(1, 1000);

            var host = authority.ProjectForHostAuthority();
            Assert.That(host.Seats.Single(seat => seat.PlayerId == "player-1").Life,
                Is.EqualTo(380));
            Assert.That(host.Seats.Single(seat => seat.PlayerId == "player-3").Life,
                Is.EqualTo(370));
            var record = host.Flow.SettlementRecords.Single();
            Assert.That(record.AppliedResults.Count, Is.EqualTo(3));
            Assert.That(record.AppliedResults.Count(item => item.PlayerId == "player-1"),
                Is.EqualTo(1));
        }

        [Test]
        public void Settlement_AppliesDamageIncomeEliminationAndRefreshExactlyOnce()
        {
            var authority = SealFourPlayerRound();
            var plan = authority.ProjectForHostAuthority().Flow.SealedRoundPlan;
            authority.TrySubmitBattleResolution(Resolution(plan.Pairings[0], 401, 10, 100));
            authority.TrySubmitBattleResolution(Resolution(plan.Pairings[1], 0, 0, 100));
            authority.TryMarkBattlePlaybackCompleted();

            var settled = authority.TryCommitRoundSettlement(1, 1000);

            Assert.That(settled.Accepted, Is.True);
            Assert.That(authority.ProjectPublic().Phase, Is.EqualTo(MatchPhase.Preparation));
            Assert.That(authority.ProjectPublic().RoundNumber, Is.EqualTo(2));
            var host = authority.ProjectForHostAuthority();
            var eliminated = host.Seats.Single(seat => seat.PlayerId == "player-1");
            Assert.That(eliminated.Life, Is.EqualTo(-1));
            Assert.That(eliminated.Eliminated, Is.True);
            Assert.That(eliminated.Placement, Is.EqualTo(4));
            Assert.That(host.Seats.Single(seat => seat.PlayerId == "player-2").Gold,
                Is.GreaterThan(7));
            Assert.That(host.Pool.AppliedPostBattleRefreshRounds, Does.Contain(1));
            var record = host.Flow.SettlementRecords.Single();
            Assert.That(record.SealedPlanCanonicalSummary, Is.Not.Empty);
            Assert.That(record.BattleResolutionCanonicalSummaries.Count, Is.EqualTo(2));
            Assert.That(record.SettlementOutputCanonicalSummary,
                Does.Contain(eliminated.CanonicalSummary));
            Assert.That(record.SettlementOutputCanonicalSummary,
                Does.Contain(host.Pool.CanonicalSummary));
            var revision = authority.StateRevision;
            Assert.That(authority.TryCommitRoundSettlement(1, 1000).Code,
                Is.EqualTo(MatchCommandCode.AcceptedNoChange));
            Assert.That(authority.StateRevision, Is.EqualTo(revision));
            Assert.That(authority.ProjectForPlayer("player-1").Owner, Is.Null);
            Assert.That(authority.ProjectPublic().Seats.Single(
                seat => seat.PlayerId == "player-1").ConnectionState,
                Is.EqualTo(PublicConnectionState.Spectating));
        }

        [Test]
        public void LegacyPhaseAndTerminationPrimitives_AreNotPublic()
        {
            Assert.That(typeof(MatchAuthority).GetMethod("TryAdvancePhase"), Is.Null);
            Assert.That(typeof(MatchAuthority).GetMethod("TryMarkEliminated"), Is.Null);
            Assert.That(typeof(MatchAuthority).GetMethod("TryEndMatch"), Is.Null);
        }

        [Test]
        public void Ranking_AllEliminatedUsesNegativeLifeAndSharedPlacements()
        {
            var authority = SealFourPlayerRound();
            var plan = authority.ProjectForHostAuthority().Flow.SealedRoundPlan;
            authority.TrySubmitBattleResolution(Resolution(plan.Pairings[0], 403, 408, 100));
            authority.TrySubmitBattleResolution(Resolution(plan.Pairings[1], 403, 403, 100));
            authority.TryMarkBattlePlaybackCompleted();

            authority.TryCommitRoundSettlement(1, 1000);

            var host = authority.ProjectForHostAuthority();
            Assert.That(host.Phase, Is.EqualTo(MatchPhase.Ended));
            Assert.That(host.Flow.EndReason, Is.EqualTo(MatchEndReason.CompetitiveCompleted));
            Assert.That(host.Seats.Single(seat => seat.PlayerId == "player-2").Placement,
                Is.EqualTo(4));
            Assert.That(host.Seats.Where(seat => seat.PlayerId != "player-2")
                .Select(seat => seat.Placement), Has.All.EqualTo(1));
            Assert.That(host.Flow.ExternalEffects.Select(effect => effect.Kind),
                Does.Contain(MatchExternalEffectKind.ReturnAllToMainMenu));
        }

        [Test]
        public void SettlementRules_UseAllIncomeAndStreakBands()
        {
            Assert.That(new[] { 1, 5, 9, 13, 17, 21 }
                .Select(round => MatchSettlementRules.GetBaseIncome(round).Gold),
                Is.EqualTo(new[] { 9, 11, 13, 15, 17, 19 }));
            Assert.That(new[] { 1, 5, 9, 13, 17, 21 }
                .Select(round => MatchSettlementRules.GetBaseIncome(round).Cost),
                Is.EqualTo(new[] { 20, 24, 28, 32, 36, 40 }));
            Assert.That(new[] { 1, 2, 4, 6 }
                .Select(count => MatchSettlementRules.GetStreakReward(
                    MatchStreakKind.Win,
                    count).Gold),
                Is.EqualTo(new[] { 0, 2, 4, 6 }));
            Assert.That(new[] { 1, 2, 4, 6 }
                .Select(count => MatchSettlementRules.GetStreakReward(
                    MatchStreakKind.Loss,
                    count).Cost),
                Is.EqualTo(new[] { 0, 4, 8, 12 }));
        }

        [Test]
        public void BattleEconomyChanges_DoNotMutateSealedPlan()
        {
            var authority = SealFourPlayerRound();
            var before = authority.ProjectForHostAuthority()
                .Flow.SealedRoundPlan.CanonicalSummary;

            var refresh = Execute(
                authority,
                "player-1",
                "battle-refresh",
                new RefreshShopCommand());

            Assert.That(refresh.Accepted, Is.True);
            Assert.That(authority.ProjectForHostAuthority()
                .Flow.SealedRoundPlan.CanonicalSummary, Is.EqualTo(before));
            Assert.That(typeof(MatchSealedRoundPlan).GetProperty("Gold"), Is.Null);
            Assert.That(typeof(MatchSealedRoundPlan).GetProperty("ShopOffers"), Is.Null);
            Assert.That(typeof(MatchSealedRoundPlan).GetProperty("OverflowUnits"), Is.Null);
        }

        [Test]
        public void AbortNoContest_DoesNotApplyBattleResultsOrInventWinner()
        {
            var authority = SealFourPlayerRound();
            var initialLife = authority.ProjectForHostAuthority().Seats
                .ToDictionary(seat => seat.PlayerId, seat => seat.Life);
            var pairing = authority.ProjectForHostAuthority()
                .Flow.SealedRoundPlan.Pairings[0];
            authority.TrySubmitBattleResolution(Resolution(pairing, 399, 0, 100));

            var aborted = authority.AbortMatchNoContest("host-process-stopped");

            Assert.That(aborted.Code, Is.EqualTo(MatchCommandCode.Accepted));
            var host = authority.ProjectForHostAuthority();
            Assert.That(host.Phase, Is.EqualTo(MatchPhase.Ended));
            Assert.That(host.Flow.EndReason, Is.EqualTo(MatchEndReason.NoContest));
            Assert.That(host.Flow.AbortDiagnostic, Is.EqualTo("host-process-stopped"));
            Assert.That(host.Seats.All(seat => seat.Life == initialLife[seat.PlayerId]), Is.True);
            Assert.That(host.Flow.FinalStandings, Has.All.Matches<MatchStanding>(
                standing => !standing.Placement.HasValue));
        }

        private static MatchAuthority SealFourPlayerRound()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1, 0);
            ReadyAll(authority, 1);
            return authority;
        }

        private static void ReadyAll(MatchAuthority authority, int round)
        {
            for (var seat = 1; seat <= 4; seat++)
            {
                Execute(
                    authority,
                    "player-" + seat,
                    "ready-" + round + "-" + seat,
                    new SetPreparationReadyCommand(true));
            }
        }

        private static void ReadyAlive(MatchAuthority authority, int round)
        {
            foreach (var playerId in authority.ProjectForHostAuthority().Seats
                .Where(seat => !seat.Eliminated)
                .Select(seat => seat.PlayerId))
            {
                Execute(
                    authority,
                    playerId,
                    "ready-" + round + "-" + playerId,
                    new SetPreparationReadyCommand(true));
            }
        }

        private static MatchCommandResult Execute(
            MatchAuthority authority,
            string playerId,
            string commandId,
            MatchCommandPayload payload)
        {
            return authority.Execute(new MatchCommandEnvelope(
                "session-1",
                playerId,
                commandId,
                authority.StateRevision,
                payload));
        }

        private static void CompleteDrawRound(
            MatchAuthority authority,
            long nextPreparationTime)
        {
            var round = authority.ProjectForHostAuthority().RoundNumber;
            foreach (var pairing in authority.ProjectForHostAuthority()
                .Flow.SealedRoundPlan.Pairings)
            {
                Assert.That(authority.TrySubmitBattleResolution(
                    Resolution(pairing, 0, 0, 100)).Accepted, Is.True);
            }
            Assert.That(authority.TryMarkBattlePlaybackCompleted().Accepted, Is.True);
            Assert.That(authority.TryCommitRoundSettlement(
                round,
                nextPreparationTime).Accepted, Is.True);
        }

        private static MatchBattleResolution Resolution(
            MatchSealedPairing pairing,
            int homeDamage,
            int awayDamage,
            int endTick)
        {
            return new MatchBattleResolution(
                pairing.BattleId,
                pairing.SealedInputHash,
                homeDamage,
                awayDamage,
                MatchBattleResolution.DeriveOutcome(homeDamage, awayDamage),
                homeDamage == awayDamage && homeDamage > 0
                    ? MatchBattleTerminalReason.MutualElimination
                    : MatchBattleTerminalReason.Normal,
                endTick);
        }

        private static string PairingRoles(MatchSealedRoundPlan plan)
        {
            return string.Join(
                "|",
                plan.Pairings
                    .OrderBy(pairing => pairing.BattleIndex)
                    .Select(pairing =>
                        pairing.HomePlayerId + "H-" + pairing.AwayPlayerId + "A"));
        }

        private static int[] PairingSeatIndexes(MatchAuthority authority)
        {
            var host = authority.ProjectForHostAuthority();
            return host.Flow.PairingSeatOrder
                .Select(playerId => host.Seats.Single(
                    seat => string.Equals(
                        seat.PlayerId,
                        playerId,
                        StringComparison.Ordinal)).SeatIndex)
                .ToArray();
        }

        private static bool IsPair(
            MatchSealedPairing pairing,
            string firstPlayerId,
            string secondPlayerId)
        {
            return string.Equals(
                pairing.HomePlayerId,
                firstPlayerId,
                StringComparison.Ordinal)
                    && string.Equals(
                        pairing.AwayPlayerId,
                        secondPlayerId,
                        StringComparison.Ordinal)
                || string.Equals(
                    pairing.HomePlayerId,
                    secondPlayerId,
                    StringComparison.Ordinal)
                    && string.Equals(
                        pairing.AwayPlayerId,
                        firstPlayerId,
                        StringComparison.Ordinal);
        }
    }
}
