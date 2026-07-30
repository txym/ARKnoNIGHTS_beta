using System;
using System.Collections.Generic;
using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Battle.Presentation;
using ArknoNights.Details;
using ArknoNights.Player;
using NUnit.Framework;
using UnityEngine;

namespace ArknoNights.Battle.Tests
{
    public sealed class UnitDetailProjectionEditModeTests
    {
        [Test]
        public void NumberFormatter_UsesValuesWithoutUnitSuffixes()
        {
            Assert.AreEqual("1.9", UnitDetailNumberFormatter.MoveSpeed(190));
            Assert.AreEqual("0.7", UnitDetailNumberFormatter.AttackInterval(14));
            Assert.AreEqual("2", UnitDetailNumberFormatter.AttackInterval(40));
            Assert.AreEqual("0.75", UnitDetailNumberFormatter.AttackInterval(15));
            Assert.AreEqual("20", UnitDetailNumberFormatter.Value(20));
        }

        [Test]
        public void Preparation_WithUnresolvedBuff_ExposesStaticFieldsButHidesDynamicAttributes()
        {
            var catalog = UnitCatalogLoader.LoadFromResources("BattleData/unit-catalog-v1").Catalog;
            var loaded = LocalPlayerStateLoader.LoadFromJson(catalog, "{\"schemaVersion\":\"local-player-state-v1\",\"playerId\":\"detail-test\",\"deploymentCost\":99,\"units\":[{\"unitId\":\"detail-unit\",\"typeId\":\"1000\",\"zone\":\"Staging\",\"eliteLevel\":2,\"buffs\":[{\"id\":\"future-buff\",\"rawPayload\":\"{}\"}],\"formationX\":0,\"formationY\":0}]}");
            Assert.IsTrue(loaded.Success, string.Join(";", loaded.Errors.Select(item => item.ToString())));

            Assert.IsTrue(UnitDetailResolver.TryResolvePreparation(loaded.State.Snapshot, catalog, "detail-unit", out var detail));
            Assert.IsFalse(detail.DynamicAttributesAvailable);
            Assert.IsNull(detail.Attack);
            Assert.IsNull(detail.MagicResistance);
            Assert.IsNotNull(detail.BlockCapacity);
            Assert.IsNotNull(detail.DeploymentCost);
            Assert.AreEqual(2, detail.EliteLevel);
            Assert.IsTrue(
                catalog.TryGet(
                    "1000",
                    2,
                    out var eliteEntry));
            Assert.AreEqual(
                eliteEntry.Definition.MaxHitPoints,
                detail.MaxHitPoints);
            Assert.AreEqual(
                eliteEntry.SkillDescriptionZhHans,
                detail.AbilityDescriptionZhHans);
            Assert.That(detail.Diagnostics, Does.Contain("detail.dynamicAttributes.unavailable.unresolvedBuff; unitId=detail-unit"));
        }

        [Test]
        public void Preparation_AppliesAlwaysOnPassiveModifierToEliteBase()
        {
            var catalog = UnitCatalogLoader
                .LoadFromResources(
                    "BattleData/unit-catalog-v1")
                .Catalog;
            var abilities = AbilityCatalogLoader
                .LoadFromResources(
                    "BattleData/ability-catalog-v1",
                    catalog);
            Assert.IsTrue(
                abilities.Success,
                string.Join(
                    ";",
                    abilities.Errors.Select(item =>
                        item.ToString())));
            var loaded = LocalPlayerStateLoader.LoadFromJson(
                catalog,
                "{\"schemaVersion\":\"local-player-state-v1\",\"playerId\":\"detail-test\",\"deploymentCost\":99,\"units\":[{\"unitId\":\"detail-unit\",\"typeId\":\"1240\",\"zone\":\"Staging\",\"eliteLevel\":2,\"buffs\":[],\"formationX\":0,\"formationY\":0}]}");
            Assert.IsTrue(
                loaded.Success,
                string.Join(
                    ";",
                    loaded.Errors.Select(item =>
                        item.ToString())));
            Assert.IsTrue(
                catalog.TryGet(
                    "1240",
                    2,
                    out var eliteEntry));

            Assert.IsTrue(
                UnitDetailResolver.TryResolvePreparation(
                    loaded.State.Snapshot,
                    catalog,
                    abilities.Catalog,
                    "detail-unit",
                    out var detail));

            Assert.IsTrue(detail.DynamicAttributesAvailable);
            Assert.AreEqual(
                eliteEntry.Definition.MaxHitPoints,
                detail.MaxHitPoints);
            Assert.AreEqual(
                eliteEntry.Definition.Attack,
                detail.Attack);
            Assert.AreEqual(
                eliteEntry.SkillDescriptionZhHans,
                detail.AbilityDescriptionZhHans);
            Assert.AreEqual(
                eliteEntry.Definition.BlockCapacity + 1,
                detail.BlockCapacity);
        }

        [Test]
        public void Battle_UsesSpawnSnapshotAndPresentationAttributeChanges()
        {
            var loaded = LocalBattleLoader.LoadFromResources("BattleData/unit-catalog-v1", "BattleData/task004a-real-1v1");
            Assert.IsTrue(loaded.Success, string.Join(";", loaded.Errors.Select(item => item.ToString())));
            var result = new BattleRunner(loaded.Input).RunToCompletion();
            var selected = loaded.Input.Players.SelectMany(player => player.Units).First();
            using (var playback = new BattleEventPlaybackController())
            {
                Assert.IsTrue(playback.Load(result, new NullFactory(), out var diagnostics), string.Join(";", diagnostics));
                Assert.IsTrue(UnitDetailResolver.TryResolveBattle(loaded.Input, playback.ViewStates, loaded.Catalog, selected.UnitId, out var detail));
                var state = playback.ViewStates.Single(
                    item => item.UnitId == selected.UnitId);
                Assert.AreEqual(
                    selected.EliteLevel,
                    detail.EliteLevel);
                Assert.AreEqual(
                    state.HitPoints,
                    detail.CurrentHitPoints);
                Assert.AreEqual(
                    state.MaxHitPoints,
                    detail.MaxHitPoints);
                Assert.AreEqual(state.Attack, detail.Attack);
                Assert.AreEqual(state.Defense, detail.Defense);
                Assert.AreEqual(
                    state.MagicResistance,
                    detail.MagicResistance);
                Assert.AreEqual(
                    state.AttackIntervalTicks,
                    detail.AttackIntervalTicks);
                Assert.IsTrue(
                    loaded.Catalog.TryGet(
                        selected.TypeId,
                        selected.EliteLevel,
                        out var selectedEntry));
                Assert.AreEqual(
                    selectedEntry.SkillDescriptionZhHans,
                    detail.AbilityDescriptionZhHans);
            }
        }

        private sealed class NullFactory : IBattlePresentationViewFactory
        {
            public bool TryCreate(string unitId, string typeId, out IBattlePresentationView view, out BattlePresentationDiagnostic diagnostic)
            {
                view = new NullView();
                diagnostic = null;
                return true;
            }
        }

        private sealed class NullView : IBattlePresentationView
        {
            public void Dispose() { }
            public void PlayAttack(float animationSpeedMultiplier) { }
            public void PlayDeath() { }
            public void PlayHit() { }
            public void PlayMove() { }
            public void SetFacing(Vector3 direction) { }
            public void SetPlaybackSpeed(float playbackSpeed) { }
            public void SetStatusBarState(string unitId, bool isEnemy, int currentHitPoints, int currentShield) { }
            public void SetWorldPosition(Vector3 position) { }
        }
    }
}
