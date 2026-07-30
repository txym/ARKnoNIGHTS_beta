using System;
using System.Collections.Generic;
using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Demo;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Battle.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace ArknoNights.Battle.Tests
{
    public sealed class BattleDemoCoordinatorEditModeTests
    {
        private const string CatalogPath = "BattleData/unit-catalog-v1";
        private const string BattlePath = "BattleData/task004a-real-1v1";

        [Test]
        public void RealBattle_StartPauseContinueReplayAndViewKeepCompletedResultImmutable()
        {
            var factory = new FakeFactory();
            var expectedViewCount = 0;
            using (var demo = new BattleDemoCoordinator())
            {
                Assert.IsTrue(demo.StartOrContinue(factory, CatalogPath, BattlePath));
                Assert.AreEqual(BattleDemoState.Playing, demo.State);
                var inputDigest = demo.InputDigest;
                var eventDigest = demo.EventDigest;
                var resultDigest = demo.ResultDigest;

                demo.Advance(0.05f);
                var pausedTick = demo.PresentationTick;
                var pausedEvents = demo.ConsumedEventCount;
                Assert.IsTrue(demo.Pause());
                demo.Advance(10f);
                Assert.AreEqual(pausedTick, demo.PresentationTick);
                Assert.AreEqual(pausedEvents, demo.ConsumedEventCount);

                Assert.IsTrue(demo.StartOrContinue(factory, CatalogPath, BattlePath));
                demo.SetSpeed(2f);
                demo.SetObserver(BattleObserverView.Away);
                demo.Advance(120f);
                Assert.AreEqual(BattleDemoState.Completed, demo.State);
                // TASK-004A's real-data regression establishes Away as the winner for this fixed input.
                Assert.AreEqual("Away", demo.WinnerOrReason);
                Assert.AreEqual(eventDigest, demo.EventDigest);
                Assert.AreEqual(resultDigest, demo.ResultDigest);
                var authoredSpawns = demo.Result.Events
                    .Where(item => item.Type == BattleEventType.Spawn && item.Tick == 0 && !item.SpawnSnapshot.IsDynamicallyGenerated)
                    .ToArray();
                var dynamicJellySpawns = demo.Result.Events
                    .Where(item => item.Type == BattleEventType.Spawn && item.UnitTypeId == "5504" && item.SpawnSnapshot.IsDynamicallyGenerated)
                    .ToArray();
                Assert.AreEqual(7, authoredSpawns.Length);
                Assert.AreEqual(24, dynamicJellySpawns.Length);
                Assert.That(dynamicJellySpawns, Has.All.Matches<BattleEvent>(item => item.Tick >= 100));
                Assert.AreEqual(authoredSpawns.Length + dynamicJellySpawns.Length, factory.Created);

                Assert.IsTrue(demo.Replay());
                Assert.AreEqual(BattleDemoState.Playing, demo.State);
                Assert.AreEqual(inputDigest, demo.InputDigest);
                Assert.AreEqual(eventDigest, demo.EventDigest);
                Assert.AreEqual(resultDigest, demo.ResultDigest);
                Assert.AreEqual(BattleObserverView.Away, demo.Observer);
                expectedViewCount = authoredSpawns.Length * 2 + dynamicJellySpawns.Length;
            }
            Assert.AreEqual(expectedViewCount, factory.Created);
            Assert.AreEqual(expectedViewCount, factory.Disposed);
        }

        [Test]
        public void RealBattle_JellySummonTimelineMatchesFormalAttackAndSkillLocks()
        {
            var loaded = LocalBattleLoader.LoadFromResources(
                CatalogPath,
                BattlePath);
            Assert.That(
                loaded.Success,
                Is.True,
                string.Join("; ", loaded.Errors.Select(item => item.ToString())));

            var result = new BattleRunner(loaded.Input).RunToCompletion();
            var skills = result.Events
                .Where(item => item.Type == BattleEventType.Skill)
                .ToArray();
            Assert.That(
                skills
                    .Where(item => item.UnitId == "away-5503-alpha")
                    .Select(item => item.Tick),
                Is.EqualTo(new[] { 100, 250, 400 }));
            Assert.That(
                skills
                    .Where(item => item.UnitId == "away-5503-bravo")
                    .Select(item => item.Tick),
                Is.EqualTo(new[] { 100, 250, 400 }));
            Assert.That(
                skills
                    .Where(item => item.UnitId == "home-5503-alpha")
                    .Select(item => item.Tick),
                Is.EqualTo(new[] { 100, 279 }));

            var dynamicJellySpawns = result.Events
                .Where(item =>
                    item.Type == BattleEventType.Spawn
                    && item.UnitTypeId == "5504"
                    && item.SpawnSnapshot.IsDynamicallyGenerated)
                .ToArray();
            Assert.That(
                dynamicJellySpawns
                    .GroupBy(item => item.Tick)
                    .Select(group => group.Count()),
                Is.EqualTo(new[] { 9, 6, 3, 6 }));
            Assert.That(
                dynamicJellySpawns.Select(item => item.Tick).Distinct(),
                Is.EqualTo(new[] { 100, 250, 279, 400 }));

            var homeDeath = result.Events.Single(item =>
                item.Type == BattleEventType.Death
                && item.UnitId == "home-5503-alpha");
            Assert.That(homeDeath.Tick, Is.EqualTo(390));
            Assert.That(result.Events, Has.None.Matches<BattleEvent>(item =>
                item.Type == BattleEventType.Skill
                && item.UnitId == "home-5503-alpha"
                && item.Tick > homeDeath.Tick));
            Assert.That(
                result.Events.Single(item =>
                    item.Type == BattleEventType.BattleEnded).Tick,
                Is.GreaterThan(homeDeath.Tick));
        }

        [Test]
        public void MissingRealInput_EntersStructuredErrorWithoutCreatingViews()
        {
            var factory = new FakeFactory();
            using (var demo = new BattleDemoCoordinator())
            {
                Assert.IsFalse(demo.Recalculate(factory, CatalogPath, "BattleData/not-present", true));
                Assert.AreEqual(BattleDemoState.Error, demo.State);
                StringAssert.Contains("load.failed", demo.LastError);
                StringAssert.Contains("localBattle.resource.missing", demo.LastError);
                Assert.AreEqual(0, factory.Created);
            }
        }

        private sealed class FakeFactory : IBattlePresentationViewFactory
        {
            public int Created { get; private set; }
            public int Disposed { get; private set; }
            public bool TryCreate(string unitId, string typeId, out IBattlePresentationView view, out BattlePresentationDiagnostic diagnostic)
            {
                Created++;
                view = new FakeView(() => Disposed++);
                diagnostic = null;
                return true;
            }
        }

        private sealed class FakeView : IBattlePresentationView
        {
            private readonly Action onDispose;
            public FakeView(Action onDispose) { this.onDispose = onDispose; }
            public void SetWorldPosition(Vector3 position) { }
            public void SetFacing(Vector3 direction) { }
            public void SetPlaybackSpeed(float playbackSpeed) { }
            public void PlayMove() { }
            public void PlayAttack(float animationSpeedMultiplier) { }
            public void PlayHit() { }
            public void PlayDeath() { }
            public void SetStatusBarState(string unitId, bool isEnemy, int currentHitPoints, int currentShield) { }
            public void Dispose() => onDispose();
        }
    }
}
