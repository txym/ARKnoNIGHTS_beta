using ArknoNights.Lobby;
using NUnit.Framework;

namespace ArknoNights.Lobby.Tests
{
    public sealed class LobbyRoomStateEditModeTests
    {
        [Test]
        public void OnlyHostCanStartAndOnlyWhenEveryMemberIsReady()
        {
            var state = LobbyRoomState.CreateHost(Profile("host"), "123456");
            state.TryJoin(Profile("guest"), out _);

            Assert.That(state.TryStart("guest", out var notHost), Is.False);
            Assert.That(notHost, Is.EqualTo(LobbyJoinFailure.NotHost));
            Assert.That(state.TryStart("host", out var notReady), Is.False);
            Assert.That(notReady, Is.EqualTo(LobbyJoinFailure.NotReady));

            state.TrySetReady("host", "host", true, out _);
            state.TrySetReady("guest", "guest", true, out _);

            Assert.That(state.TryStart("host", out _), Is.True);
            Assert.That(state.Snapshot.HasStarted, Is.True);
        }

        [Test]
        public void Join_RejectsDuplicateAndFifthMemberWithoutChangingSnapshot()
        {
            var state = LobbyRoomState.CreateHost(Profile("host"), "123456");
            state.TryJoin(Profile("guest-1"), out _);
            state.TryJoin(Profile("guest-2"), out _);
            state.TryJoin(Profile("guest-3"), out _);
            var beforeRejectedJoins = state.Snapshot;

            Assert.That(state.TryJoin(Profile("guest-3"), out var duplicate), Is.False);
            Assert.That(duplicate, Is.EqualTo(LobbyJoinFailure.DuplicatePlayer));
            Assert.That(state.TryJoin(Profile("guest-4"), out var full), Is.False);
            Assert.That(full, Is.EqualTo(LobbyJoinFailure.RoomFull));
            Assert.That(state.Snapshot, Is.SameAs(beforeRejectedJoins));
            Assert.That(state.Snapshot.Members.Count, Is.EqualTo(LobbyRoomSnapshot.MaximumMembers));
        }

        [Test]
        public void ReadyMutation_PublishesDefensiveSnapshotAndRejectsUnknownMember()
        {
            var state = LobbyRoomState.CreateHost(Profile("host"), "123456");
            var initial = state.Snapshot;

            Assert.That(state.TrySetReady("host", "host", true, out var accepted), Is.True);
            Assert.That(accepted, Is.EqualTo(LobbyJoinFailure.None));
            Assert.That(state.Snapshot, Is.Not.SameAs(initial));
            Assert.That(initial.Members[0].IsReady, Is.False);
            Assert.That(state.Snapshot.Members[0].IsReady, Is.True);

            var beforeUnknownMember = state.Snapshot;
            Assert.That(state.TrySetReady("missing", "missing", true, out var unknown), Is.False);
            Assert.That(unknown, Is.EqualTo(LobbyJoinFailure.UnknownPlayer));
            Assert.That(state.Snapshot, Is.SameAs(beforeUnknownMember));
        }

        [Test]
        public void RemovePlayer_PromotesNextMemberToHostAndPublishesSnapshot()
        {
            var state = LobbyRoomState.CreateHost(Profile("host"), "123456");
            state.TryJoin(Profile("guest"), out _);
            var beforeRemoval = state.Snapshot;

            Assert.That(state.RemovePlayer("host"), Is.True);
            Assert.That(state.Snapshot, Is.Not.SameAs(beforeRemoval));
            Assert.That(state.Snapshot.HostPlayerId, Is.EqualTo("guest"));
            Assert.That(state.Snapshot.Members[0].PlayerId, Is.EqualTo("guest"));
            Assert.That(state.RemovePlayer("missing"), Is.False);
        }

        [Test]
        public void RemovePlayer_AfterStartPreservesStartedSnapshot()
        {
            var state = LobbyRoomState.CreateHost(Profile("host"), "123456");
            state.TryJoin(Profile("guest"), out _);
            state.TrySetReady("host", "host", true, out _);
            state.TrySetReady("guest", "guest", true, out _);
            state.TryStart("host", out _);

            Assert.That(state.RemovePlayer("guest"), Is.True);

            Assert.That(state.Snapshot.HasStarted, Is.True);
            Assert.That(state.Snapshot.Members.Count, Is.EqualTo(1));
        }

        [Test]
        public void PruneExpiredMembers_RemovesOnlyTimedOutMembersInMemberOrder()
        {
            var state = LobbyRoomState.CreateHost(Profile("host"), "123456");
            state.TryJoin(Profile("guest-1"), out _);
            state.TryJoin(Profile("guest-2"), out _);

            var removed = state.PruneExpiredMembers(new[] { "guest-1" });

            Assert.That(removed, Is.EqualTo(1));
            Assert.That(state.Snapshot.Members.Count, Is.EqualTo(2));
            Assert.That(state.Snapshot.Members[0].PlayerId, Is.EqualTo("host"));
            Assert.That(state.Snapshot.Members[1].PlayerId, Is.EqualTo("guest-2"));
        }

        [Test]
        public void TryStart_RejectsRoomAfterPruningItsOnlyHost()
        {
            var state = LobbyRoomState.CreateHost(Profile("host"), "123456");
            state.PruneExpiredMembers(new[] { "host" });

            Assert.That(state.TryStart(null, out var failure), Is.False);
            Assert.That(failure, Is.EqualTo(LobbyJoinFailure.NotHost));
            Assert.That(state.Snapshot.HasStarted, Is.False);
        }

        [Test]
        public void TrySetReady_RejectsGuestAttemptToModifyHost()
        {
            var state = LobbyRoomState.CreateHost(Profile("host"), "123456");
            state.TryJoin(Profile("guest"), out _);
            var beforeUnauthorizedMutation = state.Snapshot;

            Assert.That(state.TrySetReady("guest", "host", true, out var failure), Is.False);
            Assert.That(failure, Is.EqualTo(LobbyJoinFailure.UnknownPlayer));
            Assert.That(state.Snapshot, Is.SameAs(beforeUnauthorizedMutation));
            Assert.That(state.Snapshot.Members[0].IsReady, Is.False);
        }

        [Test]
        public void PruneExpiredMembers_PublishesNewSnapshotWithoutMutatingPreviousSnapshot()
        {
            var state = LobbyRoomState.CreateHost(Profile("host"), "123456");
            state.TryJoin(Profile("guest"), out _);
            var beforePrune = state.Snapshot;

            Assert.That(state.PruneExpiredMembers(new[] { "guest" }), Is.EqualTo(1));
            Assert.That(state.Snapshot, Is.Not.SameAs(beforePrune));
            Assert.That(beforePrune.Members.Count, Is.EqualTo(2));
            Assert.That(beforePrune.Members[1].PlayerId, Is.EqualTo("guest"));
            Assert.That(state.Snapshot.Members.Count, Is.EqualTo(1));
        }

        private static LobbyProfile Profile(string playerId)
        {
            return new LobbyProfile(playerId, "Doctor " + playerId, 0);
        }
    }
}
