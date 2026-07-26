using System;
using System.Collections;
using System.Linq;
using System.Reflection;
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
        public void PrepareAndSwitch_UsesTwoCompletedResultsAndKeepsTheSharedPresentationTick()
        {
            var first = BattleFixtureLoader.LoadFromResources("BattleFixtures/task003-minimal-v1");
            var second = LocalBattleLoader.LoadFromResources("BattleData/unit-catalog-v1", "BattleData/task004a-real-1v1");
            Assert.That(first.Success, Is.True, string.Join(";", first.Errors));
            Assert.That(second.Success, Is.True, string.Join(";", second.Errors));

            var coordinatorType = typeof(BattleDemoCoordinator).Assembly.GetType("ArknoNights.Battle.Demo.MultiBattlePresentationCoordinator");
            var requestType = typeof(BattleDemoCoordinator).Assembly.GetType("ArknoNights.Battle.Demo.BattleMatchRequest");
            var observationType = typeof(BattleDemoCoordinator).Assembly.GetType("ArknoNights.Battle.Demo.PlayerBattleObservation");
            Assert.That(coordinatorType, Is.Not.Null, "UI-009 requires a shared-clock multi-battle coordinator.");
            Assert.That(requestType, Is.Not.Null);
            Assert.That(observationType, Is.Not.Null);

            var firstPlayers = first.Input.Players.ToArray();
            var secondPlayers = second.Input.Players.ToArray();
            using (var coordinator = (IDisposable)Activator.CreateInstance(coordinatorType))
            {
                var factory = new Factory();
                var requests = CreateArray(requestType,
                    Activator.CreateInstance(requestType, "match-ab", first.Input),
                    Activator.CreateInstance(requestType, "match-cd", second.Input));
                var observations = CreateArray(observationType,
                    Activator.CreateInstance(observationType, firstPlayers[0].PlayerId, "match-ab", BattleObserverView.Home),
                    Activator.CreateInstance(observationType, firstPlayers[1].PlayerId, "match-ab", BattleObserverView.Away),
                    Activator.CreateInstance(observationType, secondPlayers[0].PlayerId, "match-cd", BattleObserverView.Home),
                    Activator.CreateInstance(observationType, secondPlayers[1].PlayerId, "match-cd", BattleObserverView.Away));

                Assert.That((bool)coordinatorType.GetMethod("Prepare").Invoke(coordinator, new object[] { requests, observations, factory, firstPlayers[0].PlayerId }), Is.True, ReadString(coordinatorType, coordinator, "LastError"));
                var before = ReadMatches(coordinatorType, coordinator).Select(match => match.GetType().GetProperty("Result").GetValue(match)).ToArray();
                Assert.That((bool)coordinatorType.GetMethod("Play").Invoke(coordinator, null), Is.True);
                coordinatorType.GetMethod("Advance").Invoke(coordinator, new object[] { 0.05f });
                Assert.That((bool)coordinatorType.GetMethod("SelectObservedPlayer").Invoke(coordinator, new object[] { secondPlayers[1].PlayerId }), Is.True);

                Assert.That(ReadMatches(coordinatorType, coordinator).Select(match => match.GetType().GetProperty("Result").GetValue(match)).ToArray(), Is.EqualTo(before));
                Assert.That((double)coordinatorType.GetProperty("PresentationTick").GetValue(coordinator), Is.GreaterThan(0d));
                Assert.That(ReadString(coordinatorType, coordinator, "SelectedMatchId"), Is.EqualTo("match-cd"));
                Assert.That(coordinatorType.GetProperty("Observer").GetValue(coordinator), Is.EqualTo(BattleObserverView.Away));

                factory.HoldTerminalPresentation = true;
                coordinatorType.GetMethod("Advance").Invoke(coordinator, new object[] { 1000f });
                Assert.That(coordinatorType.GetProperty("State").GetValue(coordinator).ToString(), Is.EqualTo("Playing"),
                    "Reaching the final Track tick must wait for existing terminal death views.");
                Assert.That(factory.PendingTerminalPresentationCount, Is.GreaterThan(0));

                Assert.That((bool)coordinatorType.GetMethod("Pause").Invoke(coordinator, null), Is.True);
                factory.CompleteTerminalPresentations();
                coordinatorType.GetMethod("Advance").Invoke(coordinator, new object[] { 0.05f });
                Assert.That(coordinatorType.GetProperty("State").GetValue(coordinator).ToString(), Is.EqualTo("Paused"),
                    "A paused coordinator must not transition to Completed.");
                Assert.That((bool)coordinatorType.GetMethod("Resume").Invoke(coordinator, null), Is.True);
                coordinatorType.GetMethod("Advance").Invoke(coordinator, new object[] { 0.05f });
                Assert.That(coordinatorType.GetProperty("State").GetValue(coordinator).ToString(), Is.EqualTo("Completed"));
                var createdBeforeReplay = factory.CreatedCount;
                Assert.That((bool)coordinatorType.GetMethod("Replay").Invoke(coordinator, null), Is.True, ReadString(coordinatorType, coordinator, "LastError"));
                Assert.That((double)coordinatorType.GetProperty("PresentationTick").GetValue(coordinator), Is.EqualTo(0d));
                Assert.That(factory.CreatedCount, Is.GreaterThan(createdBeforeReplay), "Replay must rebuild the selected scene views at tick 0 instead of only changing the summary tick.");
                Assert.That(ReadMatches(coordinatorType, coordinator).Select(match => match.GetType().GetProperty("Result").GetValue(match)).ToArray(), Is.EqualTo(before));
            }
        }

        private static Array CreateArray(Type elementType, params object[] values)
        {
            var array = Array.CreateInstance(elementType, values.Length);
            for (var index = 0; index < values.Length; index++) array.SetValue(values[index], index);
            return array;
        }

        private static object[] ReadMatches(Type coordinatorType, object coordinator)
        {
            return ((IEnumerable)coordinatorType.GetProperty("Matches").GetValue(coordinator)).Cast<object>().ToArray();
        }

        private static string ReadString(Type type, object source, string property) => (string)type.GetProperty(property).GetValue(source);

        private sealed class Factory : IBattlePresentationViewFactory
        {
            private readonly System.Collections.Generic.List<View> views = new System.Collections.Generic.List<View>();

            public int CreatedCount { get; private set; }
            public bool HoldTerminalPresentation { get; set; }
            public int PendingTerminalPresentationCount => views.Count(item => item.HasPendingTerminalPresentation);

            public bool TryCreate(string unitId, string typeId, out IBattlePresentationView view, out BattlePresentationDiagnostic diagnostic)
            {
                CreatedCount++;
                var created = new View(this);
                views.Add(created);
                view = created;
                diagnostic = null;
                return true;
            }

            public void CompleteTerminalPresentations()
            {
                foreach (var view in views) view.CompleteTerminalPresentation();
            }
        }

        private sealed class View : IBattlePresentationView
        {
            private readonly Factory owner;

            public View(Factory owner) { this.owner = owner; }
            public bool HasPendingTerminalPresentation { get; private set; }
            public void SetWorldPosition(Vector3 position) { }
            public void SetFacing(Vector3 direction) { }
            public void SetPlaybackSpeed(float playbackSpeed) { }
            public void PlayMove() { }
            public void PlayAttack(float animationSpeedMultiplier) { }
            public void PlayHit() { }
            public void PlayDeath() => HasPendingTerminalPresentation = owner.HoldTerminalPresentation;
            public void SetStatusBarState(string unitId, bool isEnemy, int currentHitPoints, int currentShield) { }
            public void Dispose() => HasPendingTerminalPresentation = false;
            public void CompleteTerminalPresentation() => HasPendingTerminalPresentation = false;
        }
    }
}
