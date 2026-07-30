using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Player;
using ArknoNights.Round;
using NUnit.Framework;
using UnityEngine;

namespace ArknoNights.Battle.Tests
{
    public sealed class FourPlayerBattleRoundSealerEditModeTests
    {
        private const string CatalogPath = "BattleData/unit-catalog-v1";
        private const string MatchPath = "PlayerData/local-match-state-v1";

        [Test]
        public void SealRound_PreparesAllFourPlayersAndBuildsTheTwoConfirmedPairs()
        {
            var catalog = LoadUniqueAutoDeployCatalog();
            var match = LocalMatchStateLoader.LoadFromResources(catalog, MatchPath).State;

            var result = InvokeSealRound(match, catalog, "ui009-round-1");
            var matches = ReadMatches(result);

            Assert.That(matches.Select(item => item.MatchId), Is.EqualTo(new[] { "match-ab", "match-cd" }));
            Assert.That(matches[0].Input.BattleId, Is.EqualTo("ui009-round-1-ab"));
            Assert.That(matches[1].Input.BattleId, Is.EqualTo("ui009-round-1-cd"));
            Assert.That(matches[0].Input.Players.Single(item => item.Side == BattleSide.Home).PlayerId, Is.EqualTo("local-ui-player"));
            Assert.That(matches[0].Input.Players.Single(item => item.Side == BattleSide.Away).PlayerId, Is.EqualTo("local-ui-player-2"));
            Assert.That(matches[1].Input.Players.Single(item => item.Side == BattleSide.Home).PlayerId, Is.EqualTo("local-ui-player-3"));
            Assert.That(matches[1].Input.Players.Single(item => item.Side == BattleSide.Away).PlayerId, Is.EqualTo("local-ui-player-4"));

            var players = match.Snapshot.Players;
            Assert.That(players.Single(item => item.PlayerId == "local-ui-player").PlayerState.Units.Any(item => item.UnitId == "local-1000-overflow"), Is.False);
            Assert.That(players.All(item => item.PlayerState.Units.Count(unit => unit.Zone == PlayerUnitZone.Deployed) == 1), Is.True);
            Assert.That(players.Single(item => item.PlayerId == "local-ui-player").PlayerState.DeploymentCost, Is.EqualTo(78));
        }

        [Test]
        public void SealRound_RepeatedFreshFixturesProduceStableIndependentInputs()
        {
            var catalog = LoadUniqueAutoDeployCatalog();
            var first = InvokeSealRound(LocalMatchStateLoader.LoadFromResources(catalog, MatchPath).State, catalog, "ui009-round-stable");
            var second = InvokeSealRound(LocalMatchStateLoader.LoadFromResources(catalog, MatchPath).State, catalog, "ui009-round-stable");

            var firstInputs = ReadMatches(first).Select(item => item.Input.CanonicalSummary).ToArray();
            var secondInputs = ReadMatches(second).Select(item => item.Input.CanonicalSummary).ToArray();

            Assert.That(firstInputs, Is.EqualTo(secondInputs));
            Assert.That(firstInputs[0], Is.Not.EqualTo(firstInputs[1]));
        }

        [Test]
        public void PreparePlayer_UsesTheLowestInstanceIdWhenTheHighestAffordableCandidatesAreOneStrictStack()
        {
            var catalog = UnitCatalogLoader.LoadFromResources(CatalogPath).Catalog;
            var match = LocalMatchStateLoader.LoadFromResources(CatalogPath, MatchPath).State;
            Assert.That(match.TryGetPlayerState("local-ui-player-2", out var state), Is.True);

            Assert.That(PreparationBattleSealer.TryPreparePlayer(state, catalog, out var seal, out var error), Is.True, error);

            Assert.That(seal.AutoDeployedUnitId, Is.EqualTo("local-2-1000-alpha"));
            Assert.That(seal.After.Units.Count(item => item.Zone == PlayerUnitZone.Deployed), Is.EqualTo(1));
        }

        [Test]
        public void MatchLoader_LocalOverrideIsTheSamePersistentInstanceUsedByTheHud()
        {
            var catalog = UnitCatalogLoader.LoadFromResources(CatalogPath).Catalog;
            var local = LocalPlayerStateLoader.LoadFromResources(CatalogPath, "PlayerData/local-player-state-v1").State;

            var match = LocalMatchStateLoader.LoadFromResources(catalog, MatchPath, local);

            Assert.That(match.Success, Is.True, string.Join(";", match.Errors.Select(error => error.ToString())));
            Assert.That(match.State.TryGetPlayerState(local.PlayerId, out var matchLocal), Is.True);
            Assert.That(matchLocal, Is.SameAs(local));
        }

        private static object InvokeSealRound(LocalMatchState match, UnitCatalog catalog, string roundId)
        {
            var sealerType = typeof(PreparationBattleSealer).Assembly.GetType("ArknoNights.Round.FourPlayerBattleRoundSealer");
            Assert.That(sealerType, Is.Not.Null, "UI-009 requires a pure four-player round sealer.");
            var method = sealerType.GetMethod("TrySealRound", BindingFlags.Public | BindingFlags.Static, null, new[]
            {
                typeof(LocalMatchState), typeof(UnitCatalog), typeof(int), typeof(string), typeof(FourPlayerBattleRoundSealResult).MakeByRefType(), typeof(string).MakeByRefType()
            }, null);
            Assert.That(method, Is.Not.Null, "UI-009 requires a public TrySealRound entry point.");
            var arguments = new object[] { match, catalog, 12000, roundId, null, string.Empty };

            Assert.That((bool)method.Invoke(null, arguments), Is.True, arguments[5] as string);
            Assert.That(arguments[4], Is.Not.Null);
            return arguments[4];
        }

        private static UnitCatalog LoadUniqueAutoDeployCatalog()
        {
            var result =
                UnitCatalogLoader.LoadFromResources(CatalogPath);
            Assert.IsTrue(result.Success, string.Join(" | ", result.Errors.Select(item => item.ToString()).ToArray()));
            return result.Catalog;
        }

        private static SealedMatch[] ReadMatches(object result)
        {
            var matches = result.GetType().GetProperty("Matches")?.GetValue(result) as IEnumerable;
            Assert.That(matches, Is.Not.Null, "Round seal result must expose Matches.");
            return matches.Cast<object>()
                .Select(item => new SealedMatch(
                    (string)item.GetType().GetProperty("MatchId").GetValue(item),
                    (BattleInput)item.GetType().GetProperty("Input").GetValue(item)))
                .ToArray();
        }

        private sealed class SealedMatch
        {
            public SealedMatch(string matchId, BattleInput input) { MatchId = matchId; Input = input; }
            public string MatchId { get; }
            public BattleInput Input { get; }
        }
    }
}
