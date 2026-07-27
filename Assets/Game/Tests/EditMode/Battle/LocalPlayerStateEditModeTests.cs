using System;
using System.Linq;
using System.Text;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Player;
using NUnit.Framework;
using UnityEngine;

namespace ArknoNights.Battle.Tests
{
    public sealed class LocalPlayerStateEditModeTests
    {
        private const string CatalogPath = "BattleData/unit-catalog-v1";
        private const string PlayerStatePath = "PlayerData/local-player-state-v1";
        private const string TemporaryOpponentPath = "PlayerData/temporary-opponent-player-state-v1";

        [Test]
        public void RealCatalog_ProvidesPlayerSafeUiFieldsFromConfirmedSourceValues()
        {
            var result = UnitCatalogLoader.LoadFromResources(CatalogPath);
            Assert.IsTrue(result.Success, Errors(result.Errors));

            Assert.IsTrue(result.Catalog.TryGet("1000", out var gopro));
            Assert.AreEqual(2, gopro.DeploymentCost);
            Assert.AreEqual("ProfilePicture/UIImage_1000_gopro", gopro.PortraitResourcePath);
            Assert.AreEqual(1, gopro.Rarity);
            Assert.AreEqual(0, gopro.InitialEliteLevel);
            Assert.AreEqual("gopro", gopro.ResourceKey);
            Assert.AreEqual("猎狗", gopro.DisplayNameZhHans);
            Assert.AreEqual(string.Empty, gopro.SkillDescriptionZhHans);
            Assert.AreEqual(1, gopro.LifeDeduct);

            Assert.IsTrue(result.Catalog.TryGet("5503", out var arcslma));
            Assert.AreEqual(12, arcslma.DeploymentCost);
            Assert.AreEqual("ProfilePicture/UIImage_5503_arcslma", arcslma.PortraitResourcePath);
            Assert.AreEqual(4, arcslma.Rarity);
            Assert.AreEqual(0, arcslma.InitialEliteLevel);
            Assert.AreEqual("arcslma", arcslma.ResourceKey);
            Assert.AreEqual("果冻小子", arcslma.DisplayNameZhHans);
            Assert.AreEqual(string.Empty, arcslma.SkillDescriptionZhHans);
            Assert.AreEqual(1, arcslma.LifeDeduct);
        }

        [Test]
        public void LocalPlayerState_LoadsRealFixtureWithDeterministicReadOnlyStagingSlots()
        {
            var first = LocalPlayerStateLoader.LoadFromResources(CatalogPath, PlayerStatePath);
            var second = LocalPlayerStateLoader.LoadFromResources(CatalogPath, PlayerStatePath);
            Assert.IsTrue(first.Success, Errors(first.Errors));
            Assert.IsTrue(second.Success, Errors(second.Errors));
            Assert.AreEqual(99, first.State.DeploymentCost);
            Assert.AreEqual(first.State.Snapshot.CanonicalSummary, second.State.Snapshot.CanonicalSummary);

            var snapshot = first.State.Snapshot;
            Assert.AreEqual(4, snapshot.Units.Count);
            Assert.AreEqual(2, snapshot.StagingSlots.Count);
            CollectionAssert.AreEqual(new[] { "1000", "5503" }, snapshot.StagingSlots.Select(slot => slot.TypeId).ToArray());
            CollectionAssert.AreEqual(new[] { 2, 12 }, snapshot.StagingSlots.Select(slot => slot.DeploymentCost).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 4 }, snapshot.StagingSlots.Select(slot => slot.Rarity).ToArray(), "Rarity must come from the Player-safe catalog, not elite level.");
            CollectionAssert.AreEqual(new[] { 2, 1 }, snapshot.StagingSlots.Select(slot => slot.Count).ToArray());
            CollectionAssert.AreEqual(new[] { "local-1000-alpha", "local-1000-bravo" }, snapshot.StagingSlots[0].UnitIds);
            Assert.AreEqual(1, first.State.GetUnits(PlayerUnitZone.Overflow).Count);
        }

        [Test]
        public void TemporaryOpponent_LoadsAsIndependentDeployedPlayer()
        {
            var result = LocalPlayerStateLoader.LoadFromResources(CatalogPath, TemporaryOpponentPath);

            Assert.IsTrue(result.Success, Errors(result.Errors));
            Assert.AreEqual("temporary-opponent-player", result.State.PlayerId);
            var unit = result.State.GetUnits(PlayerUnitZone.Deployed).Single();
            Assert.AreEqual("opponent-5503-alpha", unit.UnitId);
            Assert.AreEqual(new LocalFormationCoordinate(5, 2), unit.Formation.Value);
            Assert.AreEqual(0, unit.EliteLevel);
            Assert.AreEqual(0, unit.Buffs.Count);
        }

        [Test]
        public void Loader_RejectsSchemaIdentityTypeEliteZoneAndCoordinateErrors()
        {
            var catalog = UnitCatalogLoader.LoadFromResources(CatalogPath).Catalog;
            var source = Resources.Load<TextAsset>(PlayerStatePath).text;

            AssertError(catalog, source.Replace("local-player-state-v1", "unsupported-v1"), "playerState.schema.unsupported");
            AssertError(catalog, source.Replace("local-1000-bravo", "local-1000-alpha"), "playerState.unitId.duplicate");
            AssertError(catalog, source.Replace("\"typeId\": \"5503\"", "\"typeId\": \"missing\""), "playerState.unit.type.unknown");
            AssertError(catalog, source.Replace("\"eliteLevel\": 0", "\"eliteLevel\": 4"), "playerState.unit.elite.invalid");
            AssertError(catalog, source.Replace("\"zone\": \"Overflow\"", "\"zone\": \"Invalid\""), "playerState.unit.zone.invalid");
            AssertError(catalog, source.Replace("\"zone\": \"Overflow\"", "\"zone\": \"Deployed\"").Replace("\"formationX\": 0, \"formationY\": 0", "\"formationX\": 5, \"formationY\": 1"), "playerState.unit.coordinate.gate");
        }

        [Test]
        public void CatalogLoader_RejectsRarityOutsideOneToSix()
        {
            var source = Resources.Load<TextAsset>(CatalogPath).text;
            AssertCatalogError(source.Replace("\"rarity\": 1", "\"rarity\": 0"), "catalog.ui.values.invalid");
            AssertCatalogError(source.Replace("\"rarity\": 1", "\"rarity\": 7"), "catalog.ui.values.invalid");
        }

        [Test]
        public void Loader_RejectsMoreThanThirteenStrictStagingStacks()
        {
            var catalog = UnitCatalogLoader.LoadFromResources(CatalogPath).Catalog;
            var units = new StringBuilder();
            for (var index = 0; index < 14; index++)
            {
                if (index > 0) units.Append(',');
                units.Append("{\"unitId\":\"unit-").Append(index).Append("\",\"typeId\":\"1000\",\"zone\":\"Staging\",\"eliteLevel\":0,\"buffs\":[{\"id\":\"fixture-").Append(index).Append("\",\"rawPayload\":\"").Append(index).Append("\"}],\"formationX\":0,\"formationY\":0}");
            }
            var json = "{\"schemaVersion\":\"local-player-state-v1\",\"playerId\":\"capacity-test\",\"deploymentCost\":99,\"units\":[" + units + "]}";
            AssertError(catalog, json, "playerState.staging.capacity.exceeded");
        }

        [Test]
        public void DeployAndRetreat_AreAtomicUseOneBasedCoordinatesAndTrackCost()
        {
            var loaded = LocalPlayerStateLoader.LoadFromResources(CatalogPath, PlayerStatePath);
            Assert.IsTrue(loaded.Success, Errors(loaded.Errors));
            var state = loaded.State;
            var notifications = 0;
            state.Changed += _ => notifications++;

            var beforeFailure = state.Snapshot.CanonicalSummary;
            Assert.AreEqual(PlayerOperationCode.CoordinateOutOfBounds, state.TryDeploy("local-1000-alpha", 0, 2).Code);
            Assert.AreEqual(PlayerOperationCode.CoordinateIsGate, state.TryDeploy("local-1000-alpha", 5, 1).Code);
            Assert.AreEqual(beforeFailure, state.Snapshot.CanonicalSummary);
            Assert.AreEqual(0, notifications);

            var deployed = state.TryDeploy("local-1000-alpha", 5, 2);
            Assert.IsTrue(deployed.Success);
            Assert.AreEqual(97, state.DeploymentCost);
            Assert.AreEqual(1, notifications);
            var unit = state.GetUnits(PlayerUnitZone.Deployed).Single();
            Assert.AreEqual("local-1000-alpha", unit.UnitId);
            Assert.AreEqual(new LocalFormationCoordinate(5, 2), unit.Formation.Value);

            var beforeOccupied = state.Snapshot.CanonicalSummary;
            Assert.AreEqual(PlayerOperationCode.CoordinateOccupied, state.TryDeploy("local-1000-bravo", 5, 2).Code);
            Assert.AreEqual(beforeOccupied, state.Snapshot.CanonicalSummary);
            Assert.AreEqual(1, notifications);

            var retreated = state.TryRetreat("local-1000-alpha");
            Assert.IsTrue(retreated.Success);
            Assert.AreEqual(99, state.DeploymentCost);
            Assert.AreEqual(0, state.GetUnits(PlayerUnitZone.Deployed).Count);
            Assert.AreEqual(2, notifications);
        }

        [Test]
        public void RelocateDeployed_MovesToEmptyCell_ChangesOnceAndPreservesCost()
        {
            var state = LocalPlayerStateLoader.LoadFromResources(CatalogPath, PlayerStatePath).State;
            Assert.IsTrue(state.TryDeploy("local-1000-alpha", 4, 2).Success);
            var before = state.Snapshot;
            var notifications = 0;
            state.Changed += _ => notifications++;

            var result = state.TryRelocateDeployed("local-1000-alpha", 6, 2);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(before.DeploymentCost, result.Snapshot.DeploymentCost);
            Assert.AreEqual(before.Version + 1, result.Snapshot.Version);
            Assert.AreNotEqual(before.CanonicalSummary, result.Snapshot.CanonicalSummary);
            Assert.AreEqual(1, notifications);
            Assert.AreEqual(new LocalFormationCoordinate(6, 2), result.Snapshot.Units.Single(unit => unit.UnitId == "local-1000-alpha").Formation.Value);
        }

        [Test]
        public void RelocateDeployed_SwapsOccupiedFriendlyCellAtomically()
        {
            var state = LocalPlayerStateLoader.LoadFromResources(CatalogPath, PlayerStatePath).State;
            Assert.IsTrue(state.TryDeploy("local-1000-alpha", 4, 2).Success);
            Assert.IsTrue(state.TryDeploy("local-1000-bravo", 6, 2).Success);
            var before = state.Snapshot;
            var notifications = 0;
            state.Changed += _ => notifications++;

            var result = state.TryRelocateDeployed("local-1000-alpha", 6, 2);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(before.DeploymentCost, result.Snapshot.DeploymentCost);
            Assert.AreEqual(before.Version + 1, result.Snapshot.Version);
            Assert.AreNotEqual(before.CanonicalSummary, result.Snapshot.CanonicalSummary);
            Assert.AreEqual(1, notifications);
            Assert.AreEqual(new LocalFormationCoordinate(6, 2), result.Snapshot.Units.Single(unit => unit.UnitId == "local-1000-alpha").Formation.Value);
            Assert.AreEqual(new LocalFormationCoordinate(4, 2), result.Snapshot.Units.Single(unit => unit.UnitId == "local-1000-bravo").Formation.Value);
        }

        [Test]
        public void RelocateDeployed_NoOpAndFailures_LeaveStateCostVersionAndNotificationsUnchanged()
        {
            var state = LocalPlayerStateLoader.LoadFromResources(CatalogPath, PlayerStatePath).State;
            Assert.IsTrue(state.TryDeploy("local-1000-alpha", 4, 2).Success);
            var before = state.Snapshot;
            var notifications = 0;
            state.Changed += _ => notifications++;

            Assert.IsTrue(state.TryRelocateDeployed("local-1000-alpha", 4, 2).Success);
            Assert.AreEqual(PlayerOperationCode.CoordinateOutOfBounds, state.TryRelocateDeployed("local-1000-alpha", 0, 2).Code);
            Assert.AreEqual(PlayerOperationCode.CoordinateIsGate, state.TryRelocateDeployed("local-1000-alpha", 5, 1).Code);
            Assert.AreEqual(PlayerOperationCode.UnitNotFound, state.TryRelocateDeployed("missing", 6, 2).Code);
            Assert.AreEqual(PlayerOperationCode.UnitNotDeployed, state.TryRelocateDeployed("local-1000-bravo", 6, 2).Code);

            Assert.AreEqual(before.CanonicalSummary, state.Snapshot.CanonicalSummary);
            Assert.AreEqual(before.DeploymentCost, state.DeploymentCost);
            Assert.AreEqual(before.Version, state.Version);
            Assert.AreEqual(0, notifications);
        }

        [Test]
        public void RelocateDeployed_RepeatedFixedSequenceProducesTheSameSnapshot()
        {
            var first = LocalPlayerStateLoader.LoadFromResources(CatalogPath, PlayerStatePath).State;
            var second = LocalPlayerStateLoader.LoadFromResources(CatalogPath, PlayerStatePath).State;

            ApplyRelocationSequence(first);
            ApplyRelocationSequence(second);

            Assert.AreEqual(first.Snapshot.CanonicalSummary, second.Snapshot.CanonicalSummary);
        }

        [Test]
        public void Retreat_WhenReturnWouldExceedStagingCapacity_DoesNotChangeStateOrCost()
        {
            var catalog = UnitCatalogLoader.LoadFromResources(CatalogPath).Catalog;
            var staging = new StringBuilder();
            for (var index = 0; index < 13; index++)
            {
                if (index > 0) staging.Append(',');
                staging.Append("{\"unitId\":\"staging-").Append(index).Append("\",\"typeId\":\"1000\",\"zone\":\"Staging\",\"eliteLevel\":0,\"buffs\":[{\"id\":\"fixture-").Append(index).Append("\",\"rawPayload\":\"").Append(index).Append("\"}],\"formationX\":0,\"formationY\":0}");
            }
            staging.Append(",{\"unitId\":\"deployed\",\"typeId\":\"5503\",\"zone\":\"Deployed\",\"eliteLevel\":0,\"buffs\":[],\"formationX\":5,\"formationY\":2}");
            var json = "{\"schemaVersion\":\"local-player-state-v1\",\"playerId\":\"retreat-capacity\",\"deploymentCost\":87,\"units\":[" + staging + "]}";
            var loaded = LocalPlayerStateLoader.LoadFromJson(catalog, json);
            Assert.IsTrue(loaded.Success, Errors(loaded.Errors));

            var before = loaded.State.Snapshot.CanonicalSummary;
            var result = loaded.State.TryRetreat("deployed");
            Assert.AreEqual(PlayerOperationCode.StagingCapacityExceeded, result.Code);
            Assert.AreEqual(before, loaded.State.Snapshot.CanonicalSummary);
            Assert.AreEqual(87, loaded.State.DeploymentCost);
        }

        [Test]
        public void RemoveOverflow_OnlyRemovesOverflowAndReportsStableIds()
        {
            var loaded = LocalPlayerStateLoader.LoadFromResources(CatalogPath, PlayerStatePath);
            Assert.IsTrue(loaded.Success, Errors(loaded.Errors));
            var result = loaded.State.RemoveOverflowUnits();
            Assert.IsTrue(result.Success);
            CollectionAssert.AreEqual(new[] { "local-1000-overflow" }, result.RemovedUnitIds);
            Assert.AreEqual(0, loaded.State.GetUnits(PlayerUnitZone.Overflow).Count);
            CollectionAssert.AreEqual(new[] { "local-1000-alpha", "local-1000-bravo", "local-5503-alpha" }, loaded.State.Snapshot.Units.Select(unit => unit.UnitId).ToArray());
        }

        private static void AssertError(UnitCatalog catalog, string json, string expectedCode)
        {
            var result = LocalPlayerStateLoader.LoadFromJson(catalog, json);
            Assert.IsFalse(result.Success);
            Assert.That(result.Errors.Select(error => error.Code), Does.Contain(expectedCode));
        }

        private static void AssertCatalogError(string json, string expectedCode)
        {
            var result = UnitCatalogLoader.LoadFromJson(json);
            Assert.IsFalse(result.Success);
            Assert.That(result.Errors.Select(error => error.Code), Does.Contain(expectedCode));
        }

        private static string Errors(System.Collections.Generic.IReadOnlyList<PlayerStateValidationError> errors) => string.Join("; ", errors.Select(error => error.ToString()));
        private static string Errors(System.Collections.Generic.IReadOnlyList<ArknoNights.Battle.Core.ValidationError> errors) => string.Join("; ", errors.Select(error => error.ToString()));

        private static void ApplyRelocationSequence(PlayerState state)
        {
            Assert.IsTrue(state.TryDeploy("local-1000-alpha", 4, 2).Success);
            Assert.IsTrue(state.TryDeploy("local-1000-bravo", 6, 2).Success);
            Assert.IsTrue(state.TryRelocateDeployed("local-1000-alpha", 6, 2).Success);
            Assert.IsTrue(state.TryRelocateDeployed("local-1000-bravo", 7, 2).Success);
        }
    }
}
