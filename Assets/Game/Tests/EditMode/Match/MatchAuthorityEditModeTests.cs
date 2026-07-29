using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ArknoNights.Match.Tests
{
    public sealed class MatchAuthorityEditModeTests
    {
        [Test]
        public void ReadyCommand_ChangesOnce_NoOpsAndRetriesAreIdempotent_AndConflictIsRejected()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            Assert.That(authority.TryEnterPreparation(1).Code, Is.EqualTo(MatchCommandCode.Accepted));
            var changed = new List<MatchChangedEvent>();
            authority.Changed += changed.Add;

            var command = MatchTestData.Ready(authority, "player-1", "ready-1", true);
            var accepted = authority.Execute(command);

            Assert.That(accepted.Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(accepted.ChangedState, Is.True);
            Assert.That(accepted.CurrentStateRevision, Is.EqualTo(2));
            Assert.That(accepted.AcceptedStateRevision, Is.EqualTo(2));
            Assert.That(authority.StateRevision, Is.EqualTo(2));
            Assert.That(changed.Count, Is.EqualTo(1));
            Assert.That(changed[0].StateRevision, Is.EqualTo(2));
            Assert.That(changed[0].Snapshot.Seats.Single(seat => seat.PlayerId == "player-1").Ready, Is.True);

            var retry = authority.Execute(command);
            Assert.That(retry, Is.SameAs(accepted));
            Assert.That(authority.StateRevision, Is.EqualTo(2));
            Assert.That(changed.Count, Is.EqualTo(1));

            var conflict = authority.Execute(MatchTestData.Ready(authority, "player-1", "ready-1", false));
            Assert.That(conflict.Code, Is.EqualTo(MatchCommandCode.CommandIdConflict));
            Assert.That(conflict.ChangedState, Is.False);
            Assert.That(authority.StateRevision, Is.EqualTo(2));
            Assert.That(changed.Count, Is.EqualTo(1));

            var noChange = authority.Execute(MatchTestData.Ready(authority, "player-1", "ready-2", true));
            Assert.That(noChange.Code, Is.EqualTo(MatchCommandCode.AcceptedNoChange));
            Assert.That(noChange.AcceptedStateRevision, Is.EqualTo(2));
            Assert.That(authority.StateRevision, Is.EqualTo(2));
            Assert.That(changed.Count, Is.EqualTo(1));
        }

        [Test]
        public void ReadyCommand_UsesSemanticPreconditionsInsteadOfRejectingEveryOlderRevision()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1);
            var oldRevision = authority.StateRevision;
            Assert.That(authority.Execute(MatchTestData.Ready(authority, "player-2", "p2-ready", true)).Code,
                Is.EqualTo(MatchCommandCode.Accepted));

            var staleButValid = authority.Execute(
                MatchTestData.Ready(authority, "player-1", "p1-ready", true, oldRevision));

            Assert.That(staleButValid.Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(staleButValid.CurrentStateRevision, Is.EqualTo(3));

            var before = authority.ProjectPublic().CanonicalSummary;
            var future = authority.Execute(
                MatchTestData.Ready(authority, "player-1", "future", false, authority.StateRevision + 1));
            Assert.That(future.Code, Is.EqualTo(MatchCommandCode.FutureRevision));
            Assert.That(authority.ProjectPublic().CanonicalSummary, Is.EqualTo(before));
        }

        [Test]
        public void ReadyCommand_RejectsWrongSessionUnknownPlayerInvalidPayloadAndUnavailableControllers()
        {
            var seats = MatchTestData.OneHumanSeats();
            var authority = MatchTestData.CreateAuthority(seats);
            authority.TryEnterPreparation(1);

            Assert.That(authority.Execute(
                MatchTestData.Ready(authority, "player-1", "wrong-session", true, sessionId: "other")).Code,
                Is.EqualTo(MatchCommandCode.SessionMismatch));
            Assert.That(authority.Execute(
                MatchTestData.Ready(authority, "missing", "missing-player", true)).Code,
                Is.EqualTo(MatchCommandCode.UnknownPlayer));
            Assert.That(authority.Execute(new MatchCommandEnvelope(
                "session-1", "player-1", "invalid-payload", authority.StateRevision, null)).Code,
                Is.EqualTo(MatchCommandCode.InvalidPayload));
            Assert.That(authority.Execute(
                MatchTestData.Ready(authority, "player-2", "bot-ready", true)).Code,
                Is.EqualTo(MatchCommandCode.ControllerRejected));

            authority.TrySetConnectionState("player-1", MatchConnectionState.DisconnectedGrace);
            Assert.That(authority.Execute(
                MatchTestData.Ready(authority, "player-1", "disconnected-ready", true)).Code,
                Is.EqualTo(MatchCommandCode.ConnectionRejected));
        }

        [Test]
        public void ReadyCommand_RejectsWrongPhaseTakeoverEliminatedAndInvalidCommandIdWithoutMutation()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            var initialSummary = authority.ProjectPublic().CanonicalSummary;
            Assert.That(authority.Execute(
                MatchTestData.Ready(authority, "player-1", "wrong-phase", true)).Code,
                Is.EqualTo(MatchCommandCode.PhaseRejected));
            Assert.That(authority.ProjectPublic().CanonicalSummary, Is.EqualTo(initialSummary));

            authority.TryEnterPreparation(1);
            authority.TrySetControllerKind("player-2", MatchControllerKind.TakeoverBot);
            Assert.That(authority.Execute(
                MatchTestData.Ready(authority, "player-2", "takeover-ready", true)).Code,
                Is.EqualTo(MatchCommandCode.ControllerRejected));

            authority.TryMarkEliminated("player-3", 4);
            Assert.That(authority.Execute(
                MatchTestData.Ready(authority, "player-3", "eliminated-ready", true)).Code,
                Is.EqualTo(MatchCommandCode.Eliminated));

            var beforeInvalidId = authority.StateRevision;
            Assert.That(authority.Execute(
                MatchTestData.Ready(authority, "player-1", " ", true)).Code,
                Is.EqualTo(MatchCommandCode.CommandIdInvalid));
            Assert.That(authority.StateRevision, Is.EqualTo(beforeInvalidId));
        }

        [Test]
        public void ConnectionChange_ClearsReadyAtomically_AndReconnectDoesNotRestoreIt()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1);
            authority.Execute(MatchTestData.Ready(authority, "player-2", "ready-before-drop", true));
            var before = authority.ProjectForHostAuthority().Seats.Single(seat => seat.PlayerId == "player-2");
            var eventCount = 0;
            MatchChangedEvent observed = null;
            authority.Changed += change =>
            {
                eventCount++;
                observed = change;
            };

            var disconnected = authority.TrySetConnectionState(
                "player-2",
                MatchConnectionState.DisconnectedGrace);

            Assert.That(disconnected.Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(disconnected.ChangedState, Is.True);
            Assert.That(eventCount, Is.EqualTo(1));
            var changedSeat = observed.Snapshot.Seats.Single(seat => seat.PlayerId == "player-2");
            Assert.That(changedSeat.ConnectionState, Is.EqualTo(PublicConnectionState.LostConnection));
            Assert.That(changedSeat.Ready, Is.False);
            var changedHostSeat = authority.ProjectForHostAuthority().Seats.Single(
                seat => seat.PlayerId == "player-2");
            Assert.That(changedHostSeat.Gold, Is.EqualTo(before.Gold));
            Assert.That(changedHostSeat.TotalDeploymentCost, Is.EqualTo(before.TotalDeploymentCost));

            var reconnected = authority.TrySetConnectionState("player-2", MatchConnectionState.Connected);
            Assert.That(reconnected.Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(authority.ProjectForHostAuthority().Seats.Single(
                seat => seat.PlayerId == "player-2").Ready, Is.False);
        }

        [Test]
        public void PhaseAndHostTransactions_AreMonotonicAtomicAndRejectMutationAfterEnded()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            Assert.That(authority.TryEnterPreparation(1).Code, Is.EqualTo(MatchCommandCode.Accepted));
            authority.Execute(MatchTestData.Ready(authority, "player-1", "ready-round-1", true));
            var beforeInvalid = authority.ProjectForHostAuthority().CanonicalSummary;
            var beforeInvalidRevision = authority.StateRevision;

            var skipped = authority.TryAdvancePhase(MatchPhase.Battle);
            Assert.That(skipped.Code, Is.EqualTo(MatchCommandCode.InvalidTransition));
            Assert.That(authority.StateRevision, Is.EqualTo(beforeInvalidRevision));
            Assert.That(authority.ProjectForHostAuthority().CanonicalSummary, Is.EqualTo(beforeInvalid));

            Assert.That(authority.TryAdvancePhase(MatchPhase.Sealing).Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(authority.TryAdvancePhase(MatchPhase.Battle).Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(authority.TryAdvancePhase(MatchPhase.Settlement).Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(authority.TryEnterPreparation(2).Code, Is.EqualTo(MatchCommandCode.Accepted));
            Assert.That(authority.ProjectForHostAuthority().Seats.All(seat => !seat.Ready), Is.True);

            Assert.That(authority.TryMarkEliminated("player-4", 4).Code, Is.EqualTo(MatchCommandCode.Accepted));
            var eliminated = authority.ProjectForHostAuthority().Seats.Single(seat => seat.PlayerId == "player-4");
            Assert.That(eliminated.Eliminated, Is.True);
            Assert.That(eliminated.ConnectionState, Is.EqualTo(MatchConnectionState.Eliminated));
            Assert.That(eliminated.Ready, Is.False);

            Assert.That(authority.TrySetControllerKind(
                "player-1", MatchControllerKind.TakeoverBot).Code,
                Is.EqualTo(MatchCommandCode.ControllerRejected));

            Assert.That(authority.TryEndMatch("host-ended").Code, Is.EqualTo(MatchCommandCode.Accepted));
            var endedRevision = authority.StateRevision;
            Assert.That(authority.TrySetConnectionState(
                "player-2", MatchConnectionState.DisconnectedGrace).Code,
                Is.EqualTo(MatchCommandCode.InvalidTransition));
            Assert.That(authority.Execute(
                MatchTestData.Ready(authority, "player-2", "after-end", true)).Code,
                Is.EqualTo(MatchCommandCode.PhaseRejected));
            Assert.That(authority.StateRevision, Is.EqualTo(endedRevision));
        }

        [Test]
        public void AllRequiredHumansReady_ExcludesBotsAndTakeoverControllers()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1);
            authority.TrySetControllerKind("player-4", MatchControllerKind.TakeoverBot);
            authority.Execute(MatchTestData.Ready(authority, "player-1", "ready-1", true));
            authority.Execute(MatchTestData.Ready(authority, "player-2", "ready-2", true));
            authority.Execute(MatchTestData.Ready(authority, "player-3", "ready-3", true));

            Assert.That(authority.AllRequiredHumansReady, Is.True);
        }

        [Test]
        public void RejectedCommand_IsCachedAndRemainsRejectedAfterItsPreconditionLaterChanges()
        {
            var authority = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            authority.TryEnterPreparation(1);
            authority.TrySetConnectionState("player-1", MatchConnectionState.DisconnectedGrace);
            var command = MatchTestData.Ready(authority, "player-1", "rejected-retry", true);

            var first = authority.Execute(command);
            Assert.That(first.Code, Is.EqualTo(MatchCommandCode.ConnectionRejected));
            authority.TrySetConnectionState("player-1", MatchConnectionState.Connected);
            var revisionBeforeRetry = authority.StateRevision;

            var retry = authority.Execute(command);

            Assert.That(retry, Is.SameAs(first));
            Assert.That(retry.CurrentStateRevision, Is.LessThan(revisionBeforeRetry));
            Assert.That(authority.StateRevision, Is.EqualTo(revisionBeforeRetry));
            Assert.That(authority.ProjectForHostAuthority().Seats.Single(
                seat => seat.PlayerId == "player-1").Ready, Is.False);
        }

        [Test]
        public void IdenticalInitializationAndTransactionSequence_ProducesIdenticalCanonicalSnapshots()
        {
            var first = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());
            var second = MatchTestData.CreateAuthority(MatchTestData.FourHumanSeats());

            ApplyDeterministicSequence(first);
            ApplyDeterministicSequence(second);

            Assert.That(second.ProjectPublic().CanonicalSummary,
                Is.EqualTo(first.ProjectPublic().CanonicalSummary));
            Assert.That(second.ProjectForPlayer("player-2").CanonicalSummary,
                Is.EqualTo(first.ProjectForPlayer("player-2").CanonicalSummary));
            Assert.That(second.ProjectForHostAuthority().CanonicalSummary,
                Is.EqualTo(first.ProjectForHostAuthority().CanonicalSummary));
        }

        private static void ApplyDeterministicSequence(MatchAuthority authority)
        {
            authority.TryEnterPreparation(1);
            authority.Execute(MatchTestData.Ready(authority, "player-1", "sequence-ready-1", true));
            authority.Execute(MatchTestData.Ready(authority, "player-2", "sequence-ready-2", true));
            authority.TrySetConnectionState("player-3", MatchConnectionState.DisconnectedGrace);
            authority.TrySetControllerKind("player-4", MatchControllerKind.TakeoverBot);
        }
    }
}
