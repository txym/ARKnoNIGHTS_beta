using System.Globalization;
using NUnit.Framework;

namespace ArknoNights.Match.Tests
{
    public sealed class MatchCompatibilityEditModeTests
    {
        [Test]
        public void Manifest_ValidatesEveryFieldAndRequiresStrictFieldEquality()
        {
            var manifest = MatchTestData.Manifest();
            var same = MatchTestData.Manifest();
            var differentAbilityHash = MatchTestData.Manifest(abilityHashCharacter: 'c');

            Assert.That(manifest.IsValid, Is.True);
            Assert.That(manifest.IsCompatibleWith(same), Is.True);
            Assert.That(manifest.IsCompatibleWith(differentAbilityHash), Is.False);
            Assert.That(new MatchCompatibilityManifest(
                "protocol 1",
                "rules-1",
                "battle-1",
                new string('a', 64),
                new string('b', 64)).IsValid, Is.False);
            Assert.That(new MatchCompatibilityManifest(
                "protocol-1",
                "rules-1",
                "battle-1",
                new string('g', 64),
                new string('b', 64)).IsValid, Is.False);
        }

        [Test]
        public void ManifestAndCommand_CanonicalSummariesAreUnambiguousAndCultureInvariant()
        {
            var first = MatchTestData.Manifest(protocolVersion: "a:bc");
            var second = MatchTestData.Manifest(protocolVersion: "a", rulesVersion: "bc:rules-1");
            Assert.That(first.CanonicalSummary, Is.Not.EqualTo(second.CanonicalSummary));

            var envelope = new MatchCommandEnvelope(
                "session-1",
                "player-1",
                "command-1",
                1234,
                new SetPreparationReadyCommand(true));
            var previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
                var arabic = envelope.CanonicalSummary;
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                Assert.That(envelope.CanonicalSummary, Is.EqualTo(arabic));
                Assert.That(new SetPreparationReadyCommand(true).CanonicalSummary,
                    Is.EqualTo(envelope.Payload.CanonicalSummary));
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        }
    }
}
