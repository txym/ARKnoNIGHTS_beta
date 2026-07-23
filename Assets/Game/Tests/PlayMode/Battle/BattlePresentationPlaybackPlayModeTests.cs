using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Battle.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ArknoNights.Battle.Tests
{
    public sealed class BattlePresentationPlaybackPlayModeTests
    {
        [UnityTest]
        public IEnumerator CompletedResult_ReplaysAcrossFramesWithoutChangingCoreOutcome()
        {
            var loaded = BattleFixtureLoader.LoadFromResources("BattleFixtures/task003-minimal-v1");
            Assert.IsTrue(loaded.Success, string.Join(";", loaded.Errors.Select(item => item.ToString())));
            var result = new BattleRunner(loaded.Input).RunToCompletion();
            var originalSummary = result.StableSummary;
            var factory = new FakeFactory();

            using (var playback = new BattleEventPlaybackController())
            {
                Assert.IsTrue(playback.Load(result, factory, out var diagnostics), string.Join(";", diagnostics));
                playback.Play();
                playback.Advance(0.025f);
                yield return null;
                playback.Advance(10f);

                Assert.IsTrue(playback.IsCompleted);
                Assert.AreEqual(result.Events.Count, playback.ConsumedEventCount);
                Assert.AreEqual(originalSummary, result.StableSummary);
                Assert.IsEmpty(playback.Diagnostics);
                Assert.That(playback.ViewStates.All(item => result.FinalUnits.Any(finalUnit => finalUnit.UnitId == item.UnitId && finalUnit.Position.Equals(item.Position) && finalUnit.HitPoints == item.HitPoints && finalUnit.IsAlive == item.IsAlive)), Is.True);
            }
        }

        [UnityTest]
        public IEnumerator RealCatalog_DefaultUnitViewsInitializeMoveAttackAndDispose()
        {
            var loaded = LocalBattleLoader.LoadFromResources("BattleData/unit-catalog-v1", "BattleData/task004a-real-1v1");
            Assert.IsTrue(loaded.Success, string.Join(";", loaded.Errors.Select(item => item.ToString())));
            var result = new BattleRunner(loaded.Input).RunToCompletion();
            var factoryType = Type.GetType("MappedBattlePresentationViewFactory, Assembly-CSharp");
            Assert.IsNotNull(factoryType, "Assembly-CSharp real presentation bridge is unavailable.");
            var factoryObject = new GameObject("TASK004A_RealFactory");
            var factory = factoryObject.AddComponent(factoryType) as IBattlePresentationViewFactory;
            Assert.IsNotNull(factory);

            using (var playback = new BattleEventPlaybackController())
            {
                Assert.IsTrue(playback.Load(result, factory, out var diagnostics), string.Join(";", diagnostics));
                playback.Play();
                playback.Advance(1000f);
                Assert.IsTrue(playback.IsCompleted);
                Assert.AreEqual(result.Events.Count, playback.ConsumedEventCount);
                Assert.IsEmpty(playback.Diagnostics);
            }

            UnityEngine.Object.Destroy(factoryObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RealCatalog_ChangingPlaybackSpeedDoesNotApplyTheSpeedTwiceToArcslmaAttack()
        {
            var loaded = LocalBattleLoader.LoadFromResources("BattleData/unit-catalog-v1", "BattleData/task004a-real-1v1");
            Assert.IsTrue(loaded.Success, string.Join(";", loaded.Errors.Select(item => item.ToString())));
            var result = new BattleRunner(loaded.Input).RunToCompletion();
            var arcslmaUnitIds = loaded.Input.Players.SelectMany(player => player.Units)
                .Where(unit => unit.TypeId == "5503")
                .Select(unit => unit.UnitId)
                .ToArray();
            var attack = result.Events.First(item => item.Type == BattleEventType.Attack && arcslmaUnitIds.Contains(item.UnitId));
            var factoryType = Type.GetType("MappedBattlePresentationViewFactory, Assembly-CSharp");
            Assert.IsNotNull(factoryType, "Assembly-CSharp real presentation bridge is unavailable.");
            var factoryObject = new GameObject("TASK003_AttackTimingFactory");
            var factory = factoryObject.AddComponent(factoryType) as IBattlePresentationViewFactory;
            Assert.IsNotNull(factory);

            using (var playback = new BattleEventPlaybackController())
            {
                Assert.IsTrue(playback.Load(result, factory, out var diagnostics), string.Join(";", diagnostics));
                playback.SetPlaybackSpeed(0.5f);
                playback.Play();
                playback.Advance((attack.Tick + 0.1f) / 10f);

                var attackView = GameObject.Find("BattleView_" + attack.UnitId);
                Assert.IsNotNull(attackView);
                var unitSkelType = Type.GetType("UnitSkelType2, Assembly-CSharp");
                var unitSkelBaseType = Type.GetType("UnitSkelBase, Assembly-CSharp");
                var unitSkel = attackView.GetComponent(unitSkelType);
                Assert.IsNotNull(unitSkel);
                var state = unitSkelBaseType.GetField("state", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(unitSkel);
                var entry = state.GetType().GetMethod("GetCurrent").Invoke(state, new object[] { 0 });
                Assert.IsNotNull(entry);
                var entryTimeScale = (float)entry.GetType().GetProperty("TimeScale").GetValue(entry, null);
                Assert.AreEqual(1f, entryTimeScale, 0.0001f, "Playback speed is already applied by SkeletonAnimation.timeScale and must not be multiplied into the attack entry again.");
            }

            UnityEngine.Object.Destroy(factoryObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RealCatalog_PauseFreezesSpineAnimationUntilResume()
        {
            var loaded = LocalBattleLoader.LoadFromResources("BattleData/unit-catalog-v1", "BattleData/task004a-real-1v1");
            Assert.IsTrue(loaded.Success, string.Join(";", loaded.Errors.Select(item => item.ToString())));
            var result = new BattleRunner(loaded.Input).RunToCompletion();
            var factoryType = Type.GetType("MappedBattlePresentationViewFactory, Assembly-CSharp");
            Assert.IsNotNull(factoryType, "Assembly-CSharp real presentation bridge is unavailable.");
            var factoryObject = new GameObject("TASK003_PauseFreezeFactory");
            var factory = factoryObject.AddComponent(factoryType) as IBattlePresentationViewFactory;
            Assert.IsNotNull(factory);

            using (var playback = new BattleEventPlaybackController())
            {
                Assert.IsTrue(playback.Load(result, factory, out var diagnostics), string.Join(";", diagnostics));
                playback.Play();
                playback.Advance(0.05f);

                var homeUnitId = loaded.Input.Players.Single(player => player.Side == BattleSide.Home).Units.First().UnitId;
                var awayUnitId = loaded.Input.Players.Single(player => player.Side == BattleSide.Away).Units.First().UnitId;
                var homeView = factoryObject.transform.Find("BattleView_" + homeUnitId);
                var awayView = factoryObject.transform.Find("BattleView_" + awayUnitId);
                Assert.IsNotNull(homeView);
                Assert.IsNotNull(awayView);
                var homeSkeleton = homeView.GetComponent("SkeletonAnimation");
                var awaySkeleton = awayView.GetComponent("SkeletonAnimation");
                Assert.IsNotNull(homeSkeleton);
                Assert.IsNotNull(awaySkeleton);

                playback.Pause();
                Assert.AreEqual(0f, GetSpineTimeScale(homeSkeleton));
                Assert.AreEqual(0f, GetSpineTimeScale(awaySkeleton));
                playback.SetPlaybackSpeed(2f);
                Assert.AreEqual(0f, GetSpineTimeScale(homeSkeleton));
                playback.Resume();
                Assert.AreEqual(2f, GetSpineTimeScale(homeSkeleton));
                Assert.AreEqual(2f, GetSpineTimeScale(awaySkeleton));
            }

            UnityEngine.Object.Destroy(factoryObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RealUnitView_FacesRightByDefaultAndOnlyFlipsForHorizontalMovement()
        {
            var viewType = Type.GetType("UnitSkelPresentationView, Assembly-CSharp");
            Assert.IsNotNull(viewType, "Assembly-CSharp facing bridge is unavailable.");
            var gameObject = new GameObject("TASK003_FacingView");
            var view = gameObject.AddComponent(viewType) as IBattlePresentationView;
            Assert.IsNotNull(view);

            var right = Quaternion.Euler(60f, 0f, 0f);
            var left = right * Quaternion.Euler(0f, 180f, 0f);
            Assert.That(Quaternion.Angle(gameObject.transform.rotation, right), Is.LessThan(0.5f));
            view.SetFacing(Vector3.forward);
            Assert.That(Quaternion.Angle(gameObject.transform.rotation, right), Is.LessThan(0.5f), "Pure Z movement must keep the default right-facing direction.");
            view.SetFacing(Vector3.left);
            Assert.That(Quaternion.Angle(gameObject.transform.rotation, left), Is.LessThan(0.5f));
            view.SetFacing(Vector3.forward);
            Assert.That(Quaternion.Angle(gameObject.transform.rotation, left), Is.LessThan(0.5f), "Pure Z movement must preserve the current left-facing direction.");
            view.SetFacing(Vector3.right);
            Assert.That(Quaternion.Angle(gameObject.transform.rotation, right), Is.LessThan(0.5f));

            UnityEngine.Object.Destroy(gameObject);
            yield return null;
        }

        private static float GetSpineTimeScale(Component skeleton)
        {
            var field = skeleton.GetType().GetField("timeScale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "SkeletonAnimation.timeScale is unavailable.");
            return (float)field.GetValue(skeleton);
        }

        private sealed class FakeFactory : IBattlePresentationViewFactory
        {
            public bool TryCreate(string unitId, string typeId, out IBattlePresentationView view, out BattlePresentationDiagnostic diagnostic)
            {
                view = new FakeView();
                diagnostic = null;
                return true;
            }
        }

        private sealed class FakeView : IBattlePresentationView
        {
            public void SetWorldPosition(Vector3 position) { }
            public void SetFacing(Vector3 direction) { }
            public void SetPlaybackSpeed(float playbackSpeed) { }
            public void PlayMove() { }
            public void PlayAttack(float animationSpeedMultiplier) { }
            public void PlayHit() { }
            public void PlayDeath() { }
            public void SetStatusBarState(string unitId, bool isEnemy, int currentHitPoints, int currentShield) { }
            public void Dispose() { }
        }
    }
}
