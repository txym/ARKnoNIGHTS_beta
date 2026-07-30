using System.Collections.Generic;
using System.Linq;
using ArknoNights.Match;
using ArknoNights.MatchAI;
using NUnit.Framework;

namespace ArknoNights.Lobby.Tests
{
    public sealed class LanMatchRoundIntegrationEditModeTests
    {
        private const string HostId =
            "lan-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        [Test]
        public void SingleHumanRuntime_RunsNativeBotsAndCommitsBattleIntoRoundTwo()
        {
            var baseline = LanMatchSessionConfiguration.CreateForTests();
            var bots = new BotController();
            var configuration = new LanMatchSessionConfiguration(
                baseline.CompatibilityManifest,
                baseline.ShopCatalog,
                4,
                null,
                true,
                bots);
            var lobby = new LobbyRoomSnapshot(
                "123456",
                HostId,
                new[]
                {
                    new LobbyMemberSnapshot(
                        new LobbyProfile(HostId, "host", 0),
                        true,
                        0)
                },
                false,
                1);
            var created = MatchSessionBuilder.Create(
                lobby,
                configuration,
                new Dictionary<string, MatchCompatibilityManifest>
                {
                    [HostId] = configuration.CompatibilityManifest
                },
                1000);

            Assert.That(created.Success, Is.True, created.DiagnosticCode);
            Assert.That(bots.RuntimeStates, Has.Count.EqualTo(3));
            Assert.That(created.Actor.BindFrozenConnection(
                HostId,
                "host-local",
                1), Is.True);
            created.Actor.TakeInitialization(HostId);
            created.Actor.MarkMatchRunning();

            created.Actor.Tick(31000);
            var battle = created.Actor.ProjectHostState();
            Assert.That(battle.Phase, Is.EqualTo(MatchPhase.Battle));
            Assert.That(battle.Flow.SealedRoundPlan, Is.Not.Null);
            Assert.That(
                battle.Flow.SealedRoundPlan.Pairings,
                Has.Count.EqualTo(2));

            var resolutions = battle.Flow.SealedRoundPlan.Pairings
                .Select(pairing => new MatchBattleResolution(
                    pairing.BattleId,
                    pairing.SealedInputHash,
                    0,
                    0,
                    MatchBattleOutcome.Draw,
                    MatchBattleTerminalReason.Normal,
                    100))
                .ToArray();
            var dispatches = created.Actor.CompleteBattleRound(
                resolutions,
                32000,
                out var diagnosticCode);

            Assert.That(diagnosticCode, Is.Empty);
            Assert.That(dispatches, Is.Not.Empty);
            var roundTwo = created.Actor.ProjectHostState();
            Assert.That(roundTwo.Phase, Is.EqualTo(MatchPhase.Preparation));
            Assert.That(roundTwo.RoundNumber, Is.EqualTo(2));
            Assert.That(roundTwo.Flow.BattleResolutions, Is.Empty);
            Assert.That(bots.RuntimeStates.All(item => item.RoundNumber == 2), Is.True);
        }
    }
}
