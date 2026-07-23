using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Player;
using ArknoNights.Round;
using NUnit.Framework;

namespace ArknoNights.Battle.Tests
{
    public sealed class PreparationBattlePhaseEditModeTests
    {
        private const string CatalogPath = "BattleData/unit-catalog-v1";
        private const string PlayerPath = "PlayerData/local-player-state-v1";
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
            var fixedBattle = LoadFixed();
            var player = LoadPlayer();
            Assert.IsTrue(PreparationBattleSealer.TrySeal(player, fixedBattle.Catalog, Away(fixedBattle), "test-round-1", fixedBattle.Input.MaxTicks, out var seal, out var error), error);

            CollectionAssert.AreEqual(new[] { "local-1000-overflow" }, seal.OverflowRemovedUnitIds);
            Assert.AreEqual("local-5503-alpha", seal.AutoDeployedUnitId);
            Assert.AreEqual(99, seal.Before.DeploymentCost);
            Assert.AreEqual(87, seal.After.DeploymentCost);
            var home = seal.Input.Players.Single(item => item.Side == BattleSide.Home);
            var deployed = home.Units.Single(item => item.Zone == UnitZone.Deployed);
            Assert.AreEqual("local-5503-alpha", deployed.UnitId);
            Assert.AreEqual(new FormationCoordinate(5, 2), deployed.Formation.Value);
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
            var fixedBattle = LoadFixed();
            const string playerJson = "{\"schemaVersion\":\"local-player-state-v1\",\"playerId\":\"limited-home\",\"deploymentCost\":2,\"units\":[{\"unitId\":\"costly\",\"typeId\":\"5503\",\"zone\":\"Staging\",\"eliteLevel\":0,\"buffs\":[],\"formationX\":0,\"formationY\":0},{\"unitId\":\"affordable\",\"typeId\":\"1000\",\"zone\":\"Staging\",\"eliteLevel\":0,\"buffs\":[],\"formationX\":0,\"formationY\":0}]}";
            var playerLoad = LocalPlayerStateLoader.LoadFromJson(fixedBattle.Catalog, playerJson);
            Assert.IsTrue(playerLoad.Success);
            Assert.IsTrue(PreparationBattleSealer.TrySeal(playerLoad.State, fixedBattle.Catalog, Away(fixedBattle), "test-round-limited", fixedBattle.Input.MaxTicks, out var seal, out var error), error);
            Assert.AreEqual("affordable", seal.AutoDeployedUnitId);
            Assert.AreEqual(0, seal.After.DeploymentCost);
        }

        private static LocalBattleLoadResult LoadFixed()
        {
            var result = LocalBattleLoader.LoadFromResources(CatalogPath, FixedBattlePath);
            Assert.IsTrue(result.Success, string.Join(" | ", result.Errors.Select(item => item.ToString()).ToArray()));
            return result;
        }

        private static PlayerState LoadPlayer()
        {
            var result = LocalPlayerStateLoader.LoadFromResources(CatalogPath, PlayerPath);
            Assert.IsTrue(result.Success, string.Join(" | ", result.Errors.Select(item => item.ToString()).ToArray()));
            return result.State;
        }

        private static PlayerSnapshot Away(LocalBattleLoadResult fixedBattle) => fixedBattle.Input.Players.Single(item => item.Side == BattleSide.Away);
    }
}
