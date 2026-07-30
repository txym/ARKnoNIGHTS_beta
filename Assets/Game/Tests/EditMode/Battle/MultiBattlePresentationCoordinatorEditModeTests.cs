using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Demo;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Battle.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace ArknoNights.Battle.Tests
{
    public sealed class MultiBattlePresentationCoordinatorEditModeTests
    {
        [Test]
        public void PrepareAndPump_StreamsRoundRobinBeforePlaybackAndBuffersAtTheComputedBound()
        {
            var first = BattleFixtureLoader.LoadFromResources(
                "BattleFixtures/task003-minimal-v1");
            var second = LocalBattleLoader.LoadFromResources(
                "BattleData/unit-catalog-v1",
                "BattleData/task004a-real-1v1");
            Assert.That(first.Success, Is.True,
                string.Join(";", first.Errors));
            Assert.That(second.Success, Is.True,
                string.Join(";", second.Errors));

            var firstPlayers = first.Input.Players.ToArray();
            var secondPlayers = second.Input.Players.ToArray();
            using (var coordinator =
                   new MultiBattlePresentationCoordinator())
            {
                var factory = new Factory();
                var requests = new[]
                {
                    new BattleMatchRequest("match-ab", first.Input),
                    new BattleMatchRequest("match-cd", second.Input)
                };
                var observations = new[]
                {
                    new PlayerBattleObservation(
                        firstPlayers[0].PlayerId,
                        "match-ab",
                        BattleObserverView.Home),
                    new PlayerBattleObservation(
                        firstPlayers[1].PlayerId,
                        "match-ab",
                        BattleObserverView.Away),
                    new PlayerBattleObservation(
                        secondPlayers[0].PlayerId,
                        "match-cd",
                        BattleObserverView.Home),
                    new PlayerBattleObservation(
                        secondPlayers[1].PlayerId,
                        "match-cd",
                        BattleObserverView.Away)
                };

                Assert.That(coordinator.Prepare(
                    requests,
                    observations,
                    factory,
                    firstPlayers[0].PlayerId), Is.True,
                    coordinator.LastError);
                Assert.That(coordinator.State,
                    Is.EqualTo(
                        MultiBattlePresentationState.Preparing));
                Assert.That(coordinator.Matches,
                    Has.All.Matches<BattleMatchPresentation>(
                        item => item.ProducedThroughTick == 0
                            && item.Result == null));

                Assert.That(coordinator.PumpComputation(2),
                    Is.True, coordinator.LastError);
                Assert.That(
                    coordinator.Matches.Select(item =>
                        item.ComputedThroughTick).ToArray(),
                    Is.EqualTo(new[] { 1, 1 }),
                    "A two-tick budget must advance both battles once.");

                var guard = 0;
                while (coordinator.State
                       == MultiBattlePresentationState.Preparing
                       && guard++ < 200)
                    Assert.That(coordinator.PumpComputation(2),
                        Is.True, coordinator.LastError);
                Assert.That(coordinator.State,
                    Is.EqualTo(MultiBattlePresentationState.Ready));
                Assert.That(coordinator.AllFirstChunksReady, Is.True);
                Assert.That(coordinator.Matches,
                    Has.All.Matches<BattleMatchPresentation>(
                        item => item.ProducedThroughTick <= 100),
                    "No battle may run beyond its first block before all first blocks exist.");

                Assert.That(coordinator.Play(), Is.True);
                coordinator.Advance(1000f, 0);
                Assert.That(coordinator.State,
                    Is.EqualTo(
                        MultiBattlePresentationState.Buffering));
                Assert.That(coordinator.PresentationTick,
                    Is.EqualTo(
                        coordinator.CommonAvailableThroughTick));

                Assert.That(coordinator.PumpComputation(200),
                    Is.True, coordinator.LastError);
                Assert.That(coordinator.State,
                    Is.EqualTo(
                        MultiBattlePresentationState.Playing));
                var switchTick = coordinator.PresentationTick;
                Assert.That(coordinator.SelectObservedPlayer(
                    secondPlayers[1].PlayerId), Is.True,
                    coordinator.LastError);
                Assert.That(coordinator.PresentationTick,
                    Is.EqualTo(switchTick));
                Assert.That(coordinator.SelectedMatchId,
                    Is.EqualTo("match-cd"));
                Assert.That(coordinator.Observer,
                    Is.EqualTo(BattleObserverView.Away));
            }
        }

        [Test]
        public void Prepare_AcceptsOneMatchAndTwoValidObservations()
        {
            var loaded = BattleFixtureLoader.LoadFromResources(
                "BattleFixtures/task003-minimal-v1");
            Assert.That(loaded.Success, Is.True,
                string.Join(";", loaded.Errors));
            var players = loaded.Input.Players.ToArray();
            using (var coordinator =
                   new MultiBattlePresentationCoordinator())
            {
                var prepared = coordinator.Prepare(
                    new[]
                    {
                        new BattleMatchRequest(
                            "single",
                            loaded.Input)
                    },
                    new[]
                    {
                        new PlayerBattleObservation(
                            players[0].PlayerId,
                            "single",
                            BattleObserverView.Home),
                        new PlayerBattleObservation(
                            players[1].PlayerId,
                            "single",
                            BattleObserverView.Away)
                    },
                    new Factory(),
                    players[0].PlayerId);

                Assert.That(prepared, Is.True,
                    coordinator.LastError);
                Assert.That(coordinator.Matches.Count,
                    Is.EqualTo(1));
                Assert.That(coordinator.State,
                    Is.EqualTo(
                        MultiBattlePresentationState.Preparing));
            }
        }

        [Test]
        public void PrepareCompletedPlayback_BindsTheImmutableTrackOnlyAfterAllComputationFinishes()
        {
            var loaded = BattleFixtureLoader.LoadFromResources(
                "BattleFixtures/task003-minimal-v1");
            Assert.That(loaded.Success, Is.True,
                string.Join(";", loaded.Errors));
            var players = loaded.Input.Players.ToArray();
            using (var coordinator =
                   new MultiBattlePresentationCoordinator())
            {
                Assert.That(coordinator.Prepare(
                    new[]
                    {
                        new BattleMatchRequest("single", loaded.Input)
                    },
                    new[]
                    {
                        new PlayerBattleObservation(
                            players[0].PlayerId,
                            "single",
                            BattleObserverView.Home),
                        new PlayerBattleObservation(
                            players[1].PlayerId,
                            "single",
                            BattleObserverView.Away)
                    },
                    new Factory(),
                    players[0].PlayerId), Is.True,
                    coordinator.LastError);

                var guard = 0;
                while (!coordinator.AllTracksReady && guard++ < 1000)
                    Assert.That(
                        coordinator.PumpComputation(200),
                        Is.True,
                        coordinator.LastError);

                Assert.That(coordinator.AllTracksReady, Is.True);
                Assert.That(
                    coordinator.Matches,
                    Has.All.Matches<BattleMatchPresentation>(
                        match => match.Track != null));
                Assert.That(
                    coordinator.PrepareCompletedPlayback(),
                    Is.True);
                Assert.That(
                    coordinator.State,
                    Is.EqualTo(MultiBattlePresentationState.Ready));
                Assert.That(coordinator.PresentationTick, Is.Zero);

                Assert.That(
                    coordinator.AdvanceToAuthoritativeTick(
                        coordinator.GlobalRoundEndTick,
                        0),
                    Is.True,
                    coordinator.LastError);
                Assert.That(
                    coordinator.PresentationTick,
                    Is.EqualTo(coordinator.GlobalRoundEndTick));
                Assert.That(coordinator.AllTracksReady, Is.True);
            }
        }

        [Test]
        public void SelectObservedPlayer_UnknownPlayerIsRejectedWithoutPoisoningPlayback()
        {
            var loaded = BattleFixtureLoader.LoadFromResources(
                "BattleFixtures/task003-minimal-v1");
            Assert.That(loaded.Success, Is.True,
                string.Join(";", loaded.Errors));
            var players = loaded.Input.Players.ToArray();
            using (var coordinator =
                   new MultiBattlePresentationCoordinator())
            {
                Assert.That(coordinator.Prepare(
                    new[]
                    {
                        new BattleMatchRequest("single", loaded.Input)
                    },
                    new[]
                    {
                        new PlayerBattleObservation(
                            players[0].PlayerId,
                            "single",
                            BattleObserverView.Home),
                        new PlayerBattleObservation(
                            players[1].PlayerId,
                            "single",
                            BattleObserverView.Away)
                    },
                    new Factory(),
                    players[0].PlayerId), Is.True,
                    coordinator.LastError);

                Assert.That(coordinator.SelectObservedPlayer(
                    "eliminated-or-unpaired-player"), Is.False);
                Assert.That(coordinator.State,
                    Is.Not.EqualTo(MultiBattlePresentationState.Error));
                Assert.That(coordinator.LastError, Is.Empty);
                Assert.That(coordinator.PumpComputation(2), Is.True,
                    coordinator.LastError);
            }
        }

        [Test]
        public void PumpComputation_DoesNotPublishAnEarlyTerminalBeforeEveryFirstChunkExists()
        {
            var shortSource = LocalBattleLoader.LoadFromResources(
                "BattleData/unit-catalog-v1",
                "BattleData/task004a-real-1v1");
            var longSource = BattleFixtureLoader.LoadFromResources(
                "BattleFixtures/task003-minimal-v1");
            Assert.That(shortSource.Success, Is.True,
                string.Join(";", shortSource.Errors));
            Assert.That(longSource.Success, Is.True,
                string.Join(";", longSource.Errors));
            var shortSpecification = new BattleInputSpecification(
                shortSource.Input.SchemaVersion,
                "multi-early-terminal",
                1,
                shortSource.Input.UnitDefinitions,
                shortSource.Input.AbilityDefinitions,
                shortSource.Input.Players);
            Assert.That(BattleInputFactory.TryCreate(
                    shortSpecification,
                    out var shortInput,
                    out var errors),
                Is.True,
                string.Join(";",
                    errors.Select(item => item.ToString())));
            var shortPlayers = shortInput.Players.ToArray();
            var longPlayers = longSource.Input.Players.ToArray();

            using (var coordinator =
                   new MultiBattlePresentationCoordinator())
            {
                Assert.That(coordinator.Prepare(
                    new[]
                    {
                        new BattleMatchRequest("short", shortInput),
                        new BattleMatchRequest(
                            "long",
                            longSource.Input)
                    },
                    new[]
                    {
                        new PlayerBattleObservation(
                            shortPlayers[0].PlayerId,
                            "short",
                            BattleObserverView.Home),
                        new PlayerBattleObservation(
                            shortPlayers[1].PlayerId,
                            "short",
                            BattleObserverView.Away),
                        new PlayerBattleObservation(
                            longPlayers[0].PlayerId,
                            "long",
                            BattleObserverView.Home),
                        new PlayerBattleObservation(
                            longPlayers[1].PlayerId,
                            "long",
                            BattleObserverView.Away)
                    },
                    new Factory(),
                    shortPlayers[0].PlayerId),
                    Is.True,
                    coordinator.LastError);

                Assert.That(coordinator.PumpComputation(1),
                    Is.True, coordinator.LastError);
                Assert.That(coordinator.Matches[0].ComputedThroughTick,
                    Is.EqualTo(1));
                Assert.That(coordinator.Matches[0].IsTerminal,
                    Is.False,
                    "The early terminal tail must remain unpublished until every producer has a first chunk.");
                Assert.That(coordinator.AllFirstChunksReady, Is.False);

                Assert.That(coordinator.PumpComputation(100),
                    Is.True, coordinator.LastError);
                Assert.That(coordinator.AllFirstChunksReady, Is.True);
                Assert.That(coordinator.Matches[0].IsTerminal,
                    Is.True);
                Assert.That(coordinator.Matches[0].Result,
                    Is.Not.Null);
            }
        }

        private sealed class Factory :
            IBattlePresentationViewFactory
        {
            public bool TryCreate(
                string unitId,
                string typeId,
                out IBattlePresentationView view,
                out BattlePresentationDiagnostic diagnostic)
            {
                view = new View();
                diagnostic = null;
                return true;
            }
        }

        private sealed class View : IBattlePresentationView
        {
            public void SetWorldPosition(Vector3 position) { }
            public void SetFacing(Vector3 direction) { }
            public void SetPlaybackSpeed(float playbackSpeed) { }
            public void PlayMove() { }
            public void PlayAttack(float animationSpeedMultiplier) { }
            public void PlayHit() { }
            public void PlayDeath() { }
            public void SetStatusBarState(
                string unitId,
                bool isEnemy,
                int currentHitPoints,
                int currentShield) { }
            public void Dispose() { }
        }
    }
}
