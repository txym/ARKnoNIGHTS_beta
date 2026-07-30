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
    public sealed class PreparationBattlePhaseEditModeTests
    {
        private const string CatalogPath = "BattleData/unit-catalog-v1";
        private const string PlayerPath = "PlayerData/local-player-state-v1";
        private const string TemporaryOpponentPath = "PlayerData/temporary-opponent-player-state-v1";
        private const string FixedBattlePath = "BattleData/task004a-real-1v1";

        [Test]
        public void Clock_EntersThirtySecondPreparationAndCrossesZeroOnlyOnce()
        {
            var phase = new PreparationBattlePhaseMachine();
            phase.EnterPreparation();
            Assert.AreEqual(LocalBattlePhase.Preparation, phase.Phase);
            Assert.AreEqual(30f, phase.RemainingPreparationSeconds);
            Assert.IsFalse(phase.Advance(29.9f));
            Assert.AreEqual(0.1f, phase.RemainingPreparationSeconds, 0.0001f);
            Assert.IsTrue(phase.Advance(1f));
            Assert.AreEqual(LocalBattlePhase.Battle, phase.Phase);
            Assert.AreEqual(0f, phase.RemainingPreparationSeconds);
            Assert.IsFalse(phase.Advance(100f));
            Assert.IsTrue(phase.CompleteBattle());
            Assert.AreEqual(LocalBattlePhase.Preparation, phase.Phase);
            Assert.AreEqual(30f, phase.RemainingPreparationSeconds);
        }

        [Test]
        public void Seal_RemovesOverflow_AutoDeploysUniqueHighestAffordableUnit_AndBuildsValidatedInput()
        {
            var catalog = LoadUniqueAutoDeployCatalog();
            var fixedBattle = LoadFixed(catalog);
            var player = LoadPlayer(catalog);
            Assert.IsTrue(PreparationBattleSealer.TrySeal(player, fixedBattle.Catalog, Away(fixedBattle), "test-round-1", fixedBattle.Input.MaxTicks, out var seal, out var error), error);

            CollectionAssert.AreEqual(new[] { "local-1000-overflow" }, seal.OverflowRemovedUnitIds);
            Assert.AreEqual("local-5503-alpha", seal.AutoDeployedUnitId);
            Assert.AreEqual(99, seal.Before.DeploymentCost);
            Assert.AreEqual(78, seal.After.DeploymentCost);
            var home = seal.Input.Players.Single(item => item.Side == BattleSide.Home);
            var deployed = home.Units.Single(item => item.Zone == UnitZone.Deployed);
            Assert.AreEqual("local-5503-alpha", deployed.UnitId);
            Assert.AreEqual(new FormationCoordinate(5, 2), deployed.Formation.Value);
            Assert.That(seal.Input.AbilityDefinitions.Select(item => item.AbilityId), Does.Contain("SUMMON_JELLY_MINIONS"),
                "The preparation sealer must forward the validated ability required by deployed 5503.");
            Assert.IsFalse(seal.After.Units.Any(unit => unit.Zone == PlayerUnitZone.Overflow));
        }

        [Test]
        public void Seal_UsesExistingDeploymentWithoutAutoDeploy_AndFrozenInputDoesNotTrackLaterState()
        {
            var fixedBattle = LoadFixed();
            var player = LoadPlayer();
            Assert.IsTrue(player.TryDeploy("local-1000-alpha", 5, 2).Success);
            Assert.IsTrue(PreparationBattleSealer.TrySeal(player, fixedBattle.Catalog, Away(fixedBattle), "test-round-2", fixedBattle.Input.MaxTicks, out var seal, out var error), error);
            Assert.AreEqual(string.Empty, seal.AutoDeployedUnitId);
            var digest = seal.Input.CanonicalSummary;
            Assert.IsTrue(player.TryRetreat("local-1000-alpha").Success);
            Assert.AreEqual(digest, seal.Input.CanonicalSummary);
            Assert.AreEqual(97, seal.After.DeploymentCost);
            Assert.AreEqual("local-1000-alpha", seal.Input.Players.Single(item => item.Side == BattleSide.Home).Units.Single(item => item.Zone == UnitZone.Deployed).UnitId);
        }

        [Test]
        public void Seal_WhenNoUnitIsAffordable_LeavesHomeEmptyAndCoreAwardsFixedAway()
        {
            var fixedBattle = LoadFixed();
            const string playerJson = "{\"schemaVersion\":\"local-player-state-v1\",\"playerId\":\"empty-home\",\"deploymentCost\":0,\"units\":[{\"unitId\":\"waiting\",\"typeId\":\"1000\",\"zone\":\"Staging\",\"eliteLevel\":0,\"buffs\":[],\"formationX\":0,\"formationY\":0}]}";
            var playerLoad = LocalPlayerStateLoader.LoadFromJson(fixedBattle.Catalog, playerJson);
            Assert.IsTrue(playerLoad.Success);
            Assert.IsTrue(PreparationBattleSealer.TrySeal(playerLoad.State, fixedBattle.Catalog, Away(fixedBattle), "test-round-empty", fixedBattle.Input.MaxTicks, out var seal, out var error), error);
            Assert.AreEqual(string.Empty, seal.AutoDeployedUnitId);
            Assert.AreEqual(0, seal.Input.Players.Single(item => item.Side == BattleSide.Home).Units.Count(item => item.Zone == UnitZone.Deployed));
            Assert.AreEqual(BattleSide.Away, new BattleRunner(seal.Input).RunToCompletion().Winner);
        }

        [Test]
        public void Seal_WhenHighestCostIsNotAffordable_DeploysNextAffordableUnit()
        {
            var catalog = LoadUniqueAutoDeployCatalog();
            var fixedBattle = LoadFixed(catalog);
            const string playerJson = "{\"schemaVersion\":\"local-player-state-v1\",\"playerId\":\"limited-home\",\"deploymentCost\":2,\"units\":[{\"unitId\":\"costly\",\"typeId\":\"5503\",\"zone\":\"Staging\",\"eliteLevel\":0,\"buffs\":[],\"formationX\":0,\"formationY\":0},{\"unitId\":\"affordable\",\"typeId\":\"1000\",\"zone\":\"Staging\",\"eliteLevel\":0,\"buffs\":[],\"formationX\":0,\"formationY\":0}]}";
            var playerLoad = LocalPlayerStateLoader.LoadFromJson(fixedBattle.Catalog, playerJson);
            Assert.IsTrue(playerLoad.Success);
            Assert.IsTrue(PreparationBattleSealer.TrySeal(playerLoad.State, fixedBattle.Catalog, Away(fixedBattle), "test-round-limited", fixedBattle.Input.MaxTicks, out var seal, out var error), error);
            Assert.AreEqual("affordable", seal.AutoDeployedUnitId);
            Assert.AreEqual(0, seal.After.DeploymentCost);
        }

        [Test]
        public void Seal_WhenStatesShareUnitId_FailsWithStableDiagnosticBeforeMutatingLocalState()
        {
            var fixedBattle = LoadFixed();
            var localJson = Resources.Load<TextAsset>(PlayerPath).text.Replace("local-1000-alpha", "opponent-5503-alpha");
            var local = LocalPlayerStateLoader.LoadFromJson(fixedBattle.Catalog, localJson).State;
            var opponent = LocalPlayerStateLoader.LoadFromResources(CatalogPath, TemporaryOpponentPath).State;
            var before = local.Snapshot.CanonicalSummary;
            var seal = typeof(PreparationBattleSealer).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .SingleOrDefault(method => method.Name == "TrySeal" && method.GetParameters().Length == 7 && method.GetParameters()[1].ParameterType == typeof(PlayerState));

            Assert.NotNull(seal, "The sealer must accept independent Home and Away PlayerState instances.");
            var arguments = new object[] { local, opponent, fixedBattle.Catalog, "duplicate-id", fixedBattle.Input.MaxTicks, null, string.Empty };
            Assert.IsFalse((bool)seal.Invoke(null, arguments));
            var error = (string)arguments[6];
            StringAssert.Contains("player.unitId.conflict", error);
            StringAssert.Contains(local.PlayerId, error);
            StringAssert.Contains(opponent.PlayerId, error);
            StringAssert.Contains("opponent-5503-alpha", error);
            Assert.AreEqual(before, local.Snapshot.CanonicalSummary);
        }

        private static LocalBattleLoadResult LoadFixed()
        {
            var result = LocalBattleLoader.LoadFromResources(CatalogPath, FixedBattlePath);
            Assert.IsTrue(result.Success, string.Join(" | ", result.Errors.Select(item => item.ToString()).ToArray()));
            return result;
        }

        private static LocalBattleLoadResult LoadFixed(UnitCatalog catalog)
        {
            var asset = Resources.Load<TextAsset>(FixedBattlePath);
            Assert.NotNull(asset);
            var result = LocalBattleLoader.LoadFromJson(catalog, asset.text);
            Assert.IsTrue(result.Success, string.Join(" | ", result.Errors.Select(item => item.ToString()).ToArray()));
            return result;
        }

        private static PlayerState LoadPlayer()
        {
            var result = LocalPlayerStateLoader.LoadFromResources(CatalogPath, PlayerPath);
            Assert.IsTrue(result.Success, string.Join(" | ", result.Errors.Select(item => item.ToString()).ToArray()));
            return result.State;
        }

        private static PlayerState LoadPlayer(UnitCatalog catalog)
        {
            var asset = Resources.Load<TextAsset>(PlayerPath);
            Assert.NotNull(asset);
            var result = LocalPlayerStateLoader.LoadFromJson(catalog, asset.text);
            Assert.IsTrue(result.Success, string.Join(" | ", result.Errors.Select(item => item.ToString()).ToArray()));
            return result.State;
        }

        private static UnitCatalog LoadUniqueAutoDeployCatalog()
        {
            var result =
                UnitCatalogLoader.LoadFromResources(CatalogPath);
            Assert.IsTrue(result.Success, string.Join(" | ", result.Errors.Select(item => item.ToString()).ToArray()));
            return result.Catalog;
        }

        private static PlayerState LoadOpponent()
        {
            var result = LocalPlayerStateLoader.LoadFromResources(CatalogPath, TemporaryOpponentPath);
            Assert.IsTrue(result.Success, string.Join(" | ", result.Errors.Select(item => item.ToString()).ToArray()));
            return result.State;
        }

        private static PlayerSnapshot Away(LocalBattleLoadResult fixedBattle) => fixedBattle.Input.Players.Single(item => item.Side == BattleSide.Away);
    }
}
