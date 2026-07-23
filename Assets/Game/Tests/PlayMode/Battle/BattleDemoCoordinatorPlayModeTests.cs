using System;
using System.Collections;
using System.Linq;
using ArknoNights.Battle.Demo;
using ArknoNights.Battle.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ArknoNights.Battle.Tests
{
    public sealed class BattleDemoCoordinatorPlayModeTests
    {
        [UnityTest]
        public IEnumerator RealBattle_CompletesAndDisposesEveryReplayView()
        {
            var factory = new FakeFactory();
            using (var demo = new BattleDemoCoordinator())
            {
                Assert.IsTrue(demo.StartOrContinue(factory, "BattleData/unit-catalog-v1", "BattleData/task004a-real-1v1"));
                demo.Advance(0.05f);
                Assert.IsTrue(demo.Pause());
                var pausedIndex = demo.ConsumedEventCount;
                yield return null;
                demo.Advance(1f);
                Assert.AreEqual(pausedIndex, demo.ConsumedEventCount);
                Assert.IsTrue(demo.StartOrContinue(factory, "BattleData/unit-catalog-v1", "BattleData/task004a-real-1v1"));
                demo.Advance(120f);
                Assert.AreEqual(BattleDemoState.Completed, demo.State);
                Assert.AreEqual(demo.EventCount, demo.ConsumedEventCount);
                Assert.IsTrue(demo.Replay());
            }
            Assert.AreEqual(factory.Created, factory.Disposed);
        }

        [UnityTest]
        public IEnumerator SampleScene_BattleDemoRootRunsTheRealCatalogToCompletion()
        {
            SceneManager.LoadScene("SampleScene");
            yield return null;

            var root = GameObject.Find("BattleDemoRoot");
            Assert.IsNotNull(root, "TASK-005 scene root is missing.");
            var controller = root.GetComponent("BattleDemoController");
            Assert.IsNotNull(controller, "TASK-005 controller component is missing.");
            var coordinatorProperty = controller.GetType().GetProperty("Coordinator");
            Assert.IsNotNull(coordinatorProperty, "Demo coordinator is not exposed for state inspection.");
            var demo = coordinatorProperty.GetValue(controller, null) as BattleDemoCoordinator;
            Assert.IsNotNull(demo);

            // UI-004 owns the automatic SampleScene loop. Disable that opt-in guard here so this legacy
            // regression continues to exercise the preserved fixed-Resources controller entry explicitly.
            controller.SendMessage("SetFormalRoundMode", false);
            controller.SendMessage("StartOrContinue");
            yield return null;
            Assert.AreEqual(BattleDemoState.Playing, demo.State, demo.LastError);
            Assert.AreEqual(7, demo.Input.Players.Sum(player => player.Units.Count));
            CollectionAssert.AreEquivalent(new[] { "1000", "5503" }, demo.Input.Players[0].Units.Select(unit => unit.TypeId).Distinct().ToArray());
            CollectionAssert.AreEquivalent(new[] { "1000", "5503" }, demo.Input.Players[1].Units.Select(unit => unit.TypeId).Distinct().ToArray());
            Assert.AreEqual("Away", demo.WinnerOrReason);
            var views = demo.Result.FinalUnits.Select(unit => GameObject.Find("BattleView_" + unit.UnitId)).ToArray();
            Assert.That(views, Has.All.Not.Null, "A real unit view was not created.");
            foreach (var view in views) AssertHorizontalFacing(view);
            var sourceEventDigest = demo.EventDigest;
            controller.SendMessage("SetAwayView");
            Assert.AreEqual(BattleObserverView.Away, demo.Observer);
            Assert.AreEqual(sourceEventDigest, demo.EventDigest);
            Assert.AreEqual("Away", demo.WinnerOrReason);
            foreach (var view in views) AssertHorizontalFacing(view);

            controller.SendMessage("Pause");
            Assert.AreEqual(BattleDemoState.Paused, demo.State);
            var pausedEvents = demo.ConsumedEventCount;
            demo.Advance(20f);
            Assert.AreEqual(pausedEvents, demo.ConsumedEventCount);

            controller.SendMessage("StartOrContinue");
            demo.Advance(1200f);
            Assert.AreEqual(BattleDemoState.Completed, demo.State, demo.LastError);
            Assert.AreEqual(demo.EventCount, demo.ConsumedEventCount);
            Assert.IsTrue(demo.Replay());
            Assert.IsTrue(demo.Replay());
            Assert.AreEqual(BattleDemoState.Playing, demo.State);
        }

        private static void AssertHorizontalFacing(GameObject view)
        {
            var right = Quaternion.Euler(60f, 0f, 0f);
            var left = right * Quaternion.Euler(0f, 180f, 0f);
            Assert.That(Mathf.Min(Quaternion.Angle(view.transform.rotation, right), Quaternion.Angle(view.transform.rotation, left)), Is.LessThan(0.5f));
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
