using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Battle.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ArknoNights.Battle.Tests
{
    public sealed class BattleStreamingPlaybackPlayModeTests
    {
        [UnityTest]
        public IEnumerator NonZeroBind_SamplesRawStreamLikeFullTrackAndSkipsHiddenUnits()
        {
            var loaded = BattleFixtureLoader.LoadFromResources(
                "BattleFixtures/task003-minimal-v1");
            Assert.That(loaded.Success, Is.True,
                string.Join(";", loaded.Errors));
            var producer = new BattleSimulationProducer(loaded.Input);
            var buffer = new BattlePresentationStreamBuffer(
                loaded.Input.BattleId,
                producer.SealedInputHash);
            foreach (var chunk in producer.Advance(
                         BattleSimulationProducer
                             .AuthoritativeTicksPerFullChunk))
                Assert.That(buffer.TryAppendChunk(
                    chunk,
                    out var appendError), Is.True, appendError);
            Assert.That(buffer.HasFirstChunk, Is.True);

            var result = new BattleRunner(loaded.Input)
                .RunToCompletion();
            var compiler = new BattlePresentationTrackCompiler();
            Assert.That(compiler.TryCompile(
                result,
                out var fullTrack,
                out var compileDiagnostics), Is.True,
                string.Join(";", compileDiagnostics));
            var tick = Math.Max(
                0.5d,
                buffer.AvailableThroughTick / 2d);
            tick = Math.Min(tick, buffer.AvailableThroughTick);
            var factory = new RecordingFactory();

            using (var playback =
                   new BattleTrackPlaybackController())
            {
                Assert.That(playback.Bind(
                    buffer,
                    factory,
                    BattleObserverView.Home,
                    tick,
                    out var bindDiagnostics), Is.True,
                    string.Join(";", bindDiagnostics));
                foreach (var actual in playback.ViewStates)
                {
                    Assert.That(fullTrack.TryGetUnit(
                        actual.UnitId,
                        out var fullUnit), Is.True);
                    var expected = fullUnit.Sample(tick);
                    Assert.That(actual.HitPoints,
                        Is.EqualTo(expected.CurrentHitPoints));
                    Assert.That(actual.IsAlive,
                        Is.EqualTo(expected.IsAlive));
                    Assert.That(actual.Action,
                        Is.EqualTo(expected.Action));
                    Assert.That(Math.Abs(
                        actual.Position.XUnits
                        - expected.Position.XUnits
                          * FixedPosition.UnitsPerMetre),
                        Is.LessThanOrEqualTo(1d));
                    Assert.That(Math.Abs(
                        actual.Position.YUnits
                        - expected.Position.YUnits
                          * FixedPosition.UnitsPerMetre),
                        Is.LessThanOrEqualTo(1d));
                }
                var expectedVisible = fullTrack.Units
                    .Where(item => item.Sample(tick).ShouldDisplay)
                    .Select(item => item.UnitId)
                    .OrderBy(item => item, StringComparer.Ordinal)
                    .ToArray();
                CollectionAssert.AreEqual(
                    expectedVisible,
                    factory.CreatedUnitIds
                        .OrderBy(
                            item => item,
                            StringComparer.Ordinal)
                        .ToArray());
            }

            yield return null;
        }

        private sealed class RecordingFactory :
            IBattlePresentationViewFactory
        {
            internal readonly List<string> CreatedUnitIds =
                new List<string>();

            public bool TryCreate(
                string unitId,
                string typeId,
                out IBattlePresentationView view,
                out BattlePresentationDiagnostic diagnostic)
            {
                CreatedUnitIds.Add(unitId);
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
