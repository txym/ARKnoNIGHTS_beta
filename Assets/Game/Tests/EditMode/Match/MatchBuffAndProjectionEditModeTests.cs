using System;
using System.Linq;
using NUnit.Framework;

namespace ArknoNights.Match.Tests
{
    public sealed class MatchBuffAndProjectionEditModeTests
    {
        [Test]
        public void Fusion_RemapTargetedBuffsWithoutDedupingAndPreservesOtherPlayerState()
        {
            var catalog = MatchTestData.Catalog(MatchTestData.Entry("1001", 1));
            var authority = MatchFusionTestData.CreateAuthority(catalog);
            authority.TryEnterPreparation(1);
            var overflow = MatchFusionTestData.Unit(
                "external-overflow",
                "1001",
                MatchUnitZone.Overflow,
                0,
                0);
            var targeted = new[]
            {
                new PlayerTargetedUnitBuffState(
                    "buff-survivor",
                    "same-type",
                    "external-overflow",
                    "payload-a",
                    MatchTargetedBuffDiscardPolicy.RemoveWithTarget),
                new PlayerTargetedUnitBuffState(
                    "buff-consumed",
                    "same-type",
                    "external-overflow",
                    "payload-b",
                    MatchTargetedBuffDiscardPolicy.RemoveWithTarget)
            };
            var globals = new[]
            {
                new PlayerGlobalBuffState("global-1", "global-type", "global-payload")
            };
            var sources = new[]
            {
                new PlayerSourceEffectState("source-1", "bond-type", "source-payload")
            };
            authority = MatchFusionTestData.WithPlayerOneState(
                authority,
                new[] { overflow },
                targetedBuffs: targeted,
                globalBuffs: globals,
                sourceEffects: sources);

            var result = MatchFusionTestData.PurchaseNext(authority, "buff-fusion");

            var owner = authority.ProjectForPlayer("player-1").Owner;
            Assert.That(owner.TargetedUnitBuffs, Has.Count.EqualTo(2));
            Assert.That(owner.TargetedUnitBuffs.Select(buff => buff.BuffInstanceId),
                Is.EqualTo(new[] { "buff-consumed", "buff-survivor" }));
            Assert.That(owner.TargetedUnitBuffs,
                Has.All.Matches<PlayerTargetedUnitBuffState>(
                    buff => buff.TargetUnitId == result.Acquisition.FinalSurvivorUnitId));
            Assert.That(owner.GlobalBuffs.Select(buff => buff.CanonicalSummary),
                Is.EqualTo(globals.Select(buff => buff.CanonicalSummary)));
            Assert.That(owner.SourceEffects.Select(effect => effect.CanonicalSummary),
                Is.EqualTo(sources.Select(effect => effect.CanonicalSummary)));
        }

        [Test]
        public void Fusion_UnknownInlineBuffOnConsumedUnitRejectsWholePurchaseAtomically()
        {
            var catalog = MatchTestData.Catalog(MatchTestData.Entry("1001", 1));
            var authority = MatchFusionTestData.CreateAuthority(catalog);
            authority.TryEnterPreparation(1);
            authority = MatchFusionTestData.WithPlayerOneState(authority, new[]
            {
                MatchFusionTestData.Unit(
                    "external-overflow",
                    "1001",
                    MatchUnitZone.Overflow,
                    0,
                    0,
                    null,
                    new MatchBuffState("unknown-inline", "payload"))
            });
            var before = authority.ProjectForHostAuthority();

            var result = MatchFusionTestData.PurchaseNext(authority, "inline-rejected");

            var after = authority.ProjectForHostAuthority();
            Assert.That(result.Code, Is.EqualTo(MatchCommandCode.InternalInvariantViolation));
            Assert.That(result.ChangedState, Is.False);
            Assert.That(after.State.CanonicalSummary, Is.EqualTo(before.State.CanonicalSummary));
            Assert.That(after.StateRevision, Is.EqualTo(before.StateRevision));
            Assert.That(after.Pool.NextAcquisitionOrdinal, Is.EqualTo(before.Pool.NextAcquisitionOrdinal));
            Assert.That(after.Pool.RetiredUnits, Is.Empty);
        }

        [Test]
        public void ScopedProjectionsExposeOnlyAuthorizedBuffOverflowAndTombstoneData()
        {
            var catalog = MatchTestData.Catalog(MatchTestData.Entry("1001", 1));
            var authority = MatchFusionTestData.CreateAuthority(catalog);
            authority.TryEnterPreparation(1);
            var overflow = MatchFusionTestData.Unit(
                "external-overflow",
                "1001",
                MatchUnitZone.Overflow,
                0,
                0);
            authority = MatchFusionTestData.WithPlayerOneState(
                authority,
                new[] { overflow },
                targetedBuffs: new[]
                {
                    new PlayerTargetedUnitBuffState(
                        "private-buff",
                        "private-type",
                        overflow.UnitId,
                        "secret-payload",
                        MatchTargetedBuffDiscardPolicy.RemoveWithTarget)
                });

            var publicSnapshot = authority.ProjectPublic();
            var owner = authority.ProjectForPlayer("player-1").Owner;
            var other = authority.ProjectForPlayer("player-2").Owner;
            var host = authority.ProjectForHostAuthority();

            Assert.That(publicSnapshot.Seats.Single(seat => seat.PlayerId == "player-1").Units,
                Is.Empty);
            Assert.That(owner.OverflowUnits.Single().UnitId, Is.EqualTo(overflow.UnitId));
            Assert.That(owner.TargetedUnitBuffs.Single().CanonicalPayload, Is.EqualTo("secret-payload"));
            Assert.That(other.TargetedUnitBuffs, Is.Empty);
            Assert.That(typeof(PublicMatchSeatSnapshot).GetProperty("TargetedUnitBuffs"), Is.Null);
            Assert.That(typeof(PublicMatchSnapshot).GetProperty("RetiredUnits"), Is.Null);
            Assert.That(host.Seats.Single(seat => seat.PlayerId == "player-1")
                .TargetedUnitBuffs.Single().BuffInstanceId, Is.EqualTo("private-buff"));
        }
    }
}
