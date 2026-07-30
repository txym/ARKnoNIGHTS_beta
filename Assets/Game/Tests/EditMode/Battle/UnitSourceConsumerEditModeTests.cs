using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ArknoNights.Battle.Infrastructure;
using NUnit.Framework;
using UnityEngine;

namespace ArknoNights.Battle.Tests
{
    public sealed class UnitSourceConsumerEditModeTests
    {
        private const string ExpectedRuntimeCatalogHash =
            "359C81D56AB89EA735FAFCD0F2A6CA243076DE7C72A9086B7E4097B6B728B0AA";
        private static readonly string[] LegacyProjectionFileNames =
        {
            "1000_gopro.json",
            "5503_arcslma.json",
            "5504_arcslmi.json"
        };

        [Test]
        public void Generate_ProjectsRepresentableLegacySourcesToIsolatedV1Catalog()
        {
            var outputPath = NewIsolatedPath("projection", "unit-catalog-v1.json");
            var sourceDirectory = CopyRealSources(
                "projection-sources",
                null,
                json => json);
            var hashBefore = RuntimeCatalogHash();
            Assert.That(hashBefore, Is.EqualTo(ExpectedRuntimeCatalogHash));

            try
            {
                InvokeGenerator(sourceDirectory, outputPath);
            }
            finally
            {
                Assert.That(RuntimeCatalogHash(), Is.EqualTo(hashBefore));
            }

            var document = JsonUtility.FromJson<CatalogProjectionDocument>(
                File.ReadAllText(outputPath));
            Assert.That(document, Is.Not.Null);
            Assert.That(document.units, Is.Not.Null);
            Assert.That(
                document.units.Select(unit => unit.typeId),
                Is.EqualTo(new[] { "1000", "5503", "5504" }));
            Assert.That(
                document.units.Single(unit => unit.typeId == "1000").unitSkelType,
                Is.EqualTo(2));
            Assert.That(
                document.units.Single(unit => unit.typeId == "1000").moveAnimation,
                Is.EqualTo("Run_Loop"));
            Assert.That(
                document.units.Single(unit => unit.typeId == "1000")
                    .attackAnimationDurationTicks,
                Is.EqualTo(20));
            Assert.That(
                document.units.Single(unit => unit.typeId == "5503").deploymentCost,
                Is.EqualTo(2));
            Assert.That(
                document.units.Single(unit => unit.typeId == "5503").rarity,
                Is.EqualTo(6));
            Assert.That(
                document.units.Single(unit => unit.typeId == "5503")
                    .attackAnimationDurationTicks,
                Is.EqualTo(54));
            Assert.That(
                document.units.Single(unit => unit.typeId == "5503").hitAnimation,
                Is.Empty);
            Assert.That(
                document.units.Single(unit => unit.typeId == "5504")
                    .attackAnimationDurationTicks,
                Is.EqualTo(24));
        }

        [Test]
        public void Generate_RejectsUnrepresentableV2SourceWithoutTouchingOutput()
        {
            var sourceDirectory = CopyRealSources(
                "unrepresentable",
                "5504_arcslmi.json",
                MakeNonAttacker);
            AssertAtomicFailure(
                sourceDirectory,
                "UNIT_CATALOG_V1_SOURCE_UNREPRESENTABLE");
        }

        [Test]
        public void SkillAnimationCatalogGenerator_ProjectsSourceDurationWithoutChangingFrozenCatalog()
        {
            var outputPath = NewIsolatedPath(
                "skill-animation-projection",
                "skill-animation-catalog-v1.json");
            var hashBefore = RuntimeCatalogHash();

            InvokeSkillAnimationGenerator(
                RealSourceDirectory(),
                Path.Combine(
                    Application.dataPath,
                    "GameData/Abilities/Json"),
                outputPath);

            Assert.That(RuntimeCatalogHash(), Is.EqualTo(hashBefore));
            var document =
                JsonUtility.FromJson<SkillAnimationCatalogDocument>(
                    File.ReadAllText(outputPath));
            Assert.That(
                document.bindings.Select(item => item.abilityId),
                Is.EqualTo(new[]
                {
                    "BERRY_CRAB_HALF_HEALTH_FULL_HEAL",
                    "BLOCKED_BLINK_FORWARD",
                    "CHARGED_DRINK_AREA_ATTACK",
                    "FEED_ORIGINIUM_BUG_HALF_HEALTH_TRANSFORM",
                    "GREY_HAT_THIRD_ATTACK_DASH",
                    "PRISONER_RELEASE_BOXER",
                    "PRISONER_RELEASE_LEADER",
                    "PRISONER_RELEASE_STANDARD",
                    "PRISONER_RELEASE_STRONG",
                    "ROADBUILDER_FRAGMENT_THIRD_ATTACK_SPAWN",
                    "ROADBUILDER_THIRD_ATTACK_SPAWN",
                    "SUMMON_JELLY_MINIONS",
                    "SUMMON_REPAIR_HELPER"
                }));
            var berryCrabBinding = document.bindings.Single(item =>
                item.abilityId == "BERRY_CRAB_HALF_HEALTH_FULL_HEAL");
            Assert.That(berryCrabBinding.typeId, Is.EqualTo("10004"));
            Assert.That(berryCrabBinding.animationKey, Is.EqualTo("skill"));
            Assert.That(berryCrabBinding.originalAnimationTicks, Is.EqualTo(80));
            Assert.That(berryCrabBinding.presentationStateTag, Is.EqualTo("b"));
            Assert.That(berryCrabBinding.stateIdleAnimation, Is.EqualTo("Idle_B"));
            Assert.That(berryCrabBinding.stateMoveAnimation, Is.EqualTo("Move_B"));
            Assert.That(berryCrabBinding.stateAttackAnimation, Is.EqualTo("Attack_B"));
            Assert.That(berryCrabBinding.stateDeathAnimation, Is.EqualTo("Die_B"));
            var feederBinding = document.bindings.Single(item =>
                item.abilityId == "FEED_ORIGINIUM_BUG_HALF_HEALTH_TRANSFORM");
            Assert.That(feederBinding.typeId, Is.EqualTo("10001"));
            Assert.That(feederBinding.animationKey, Is.EqualTo("skill.begin"));
            Assert.That(feederBinding.animationName, Is.EqualTo("Skill_Begin"));
            Assert.That(feederBinding.originalAnimationTicks, Is.EqualTo(20));
            Assert.That(feederBinding.presentationStateTag, Is.EqualTo("b"));
            Assert.That(feederBinding.stateIdleAnimation, Is.EqualTo("Idle_B"));
            Assert.That(feederBinding.stateMoveAnimation, Is.EqualTo("Move_B"));
            Assert.That(feederBinding.stateAttackAnimation, Is.EqualTo("Attack_B"));
            Assert.That(feederBinding.stateDeathAnimation, Is.EqualTo("Die_B"));
            var standardPrisonerBinding = document.bindings.Single(item =>
                item.abilityId == "PRISONER_RELEASE_STANDARD");
            Assert.That(standardPrisonerBinding.typeId, Is.EqualTo("1116"));
            Assert.That(standardPrisonerBinding.animationKey, Is.Empty);
            Assert.That(
                standardPrisonerBinding.presentationStateTag,
                Is.EqualTo("released"));
            Assert.That(
                standardPrisonerBinding.stateIdleAnimation,
                Is.EqualTo("Idle"));
            Assert.That(
                standardPrisonerBinding.stateMoveAnimation,
                Is.EqualTo("Move"));
            Assert.That(
                standardPrisonerBinding.stateAttackAnimation,
                Is.EqualTo("Attack"));
            Assert.That(
                standardPrisonerBinding.stateDeathAnimation,
                Is.EqualTo("Die"));
            var boxerBinding = document.bindings.Single(item =>
                item.abilityId == "PRISONER_RELEASE_BOXER");
            Assert.That(boxerBinding.typeId, Is.EqualTo("1118"));
            Assert.That(boxerBinding.presentationStateTag, Is.EqualTo("red"));
            Assert.That(boxerBinding.stateIdleAnimation, Is.EqualTo("Idle_red"));
            Assert.That(boxerBinding.stateMoveAnimation, Is.EqualTo("Move_red"));
            Assert.That(boxerBinding.stateAttackAnimation, Is.EqualTo("Attack_red"));
            Assert.That(boxerBinding.stateDeathAnimation, Is.EqualTo("Die_red"));
            var blinkBinding = document.bindings.Single(item =>
                item.abilityId == "BLOCKED_BLINK_FORWARD");
            Assert.That(blinkBinding.typeId, Is.EqualTo("1502"));
            Assert.That(
                blinkBinding.animationKey,
                Is.EqualTo(
                    "blink.disappear|blink.appear"));
            Assert.That(
                blinkBinding.animationName,
                Is.EqualTo("Disappear|Appear"));
            Assert.That(
                blinkBinding.originalAnimationTicks,
                Is.EqualTo(20));
            Assert.That(
                blinkBinding.segmentOriginalAnimationTicks,
                Is.EqualTo(new[] { 10, 10 }));
            var loaded =
                SkillAnimationCatalogLoader.LoadFromJson(
                    File.ReadAllText(outputPath));
            Assert.That(
                loaded.Success,
                Is.True,
                string.Join(
                    "; ",
                    loaded.Errors.Select(item => item.ToString())));
            Assert.That(
                loaded.Catalog.TryGetAbility(
                    "BLOCKED_BLINK_FORWARD",
                    out var loadedBlink),
                Is.True);
            Assert.That(
                loadedBlink.SegmentOriginalAnimationTicks,
                Is.EqualTo(new[] { 10, 10 }));
            Assert.That(
                loaded.Catalog.TryGetAbility(
                    "PRISONER_RELEASE_STANDARD",
                    out var loadedPrisoner),
                Is.True);
            Assert.That(loadedPrisoner.HasSkillAnimation, Is.False);
            Assert.That(loadedPrisoner.HasPresentationState, Is.True);
            Assert.That(
                loadedPrisoner.PresentationStateTag,
                Is.EqualTo("released"));
            var chargedBinding = document.bindings.Single(item =>
                item.abilityId == "CHARGED_DRINK_AREA_ATTACK");
            Assert.That(chargedBinding.typeId, Is.EqualTo("10039"));
            Assert.That(chargedBinding.animationKey, Is.EqualTo("skill"));
            Assert.That(chargedBinding.animationName, Is.EqualTo("Skill"));
            Assert.That(
                chargedBinding.originalAnimationTicks,
                Is.EqualTo(57));
            var greyHatBinding = document.bindings.Single(item =>
                item.abilityId == "GREY_HAT_THIRD_ATTACK_DASH");
            Assert.That(greyHatBinding.typeId, Is.EqualTo("1322"));
            Assert.That(
                greyHatBinding.animationKey,
                Is.EqualTo(
                    "skill.begin|skill.loop|skill.end"));
            Assert.That(
                greyHatBinding.animationName,
                Is.EqualTo(
                    "Skill_Begin|Skill_Loop|Skill_End"));
            Assert.That(
                greyHatBinding.originalAnimationTicks,
                Is.EqualTo(40));
            Assert.That(
                greyHatBinding.segmentOriginalAnimationTicks,
                Is.EqualTo(new[] { 7, 4, 30 }));
            var roadbuilderBinding = document.bindings.Single(item =>
                item.abilityId
                == "ROADBUILDER_THIRD_ATTACK_SPAWN");
            Assert.That(roadbuilderBinding.typeId, Is.EqualTo("2031"));
            Assert.That(
                roadbuilderBinding.animationKey,
                Is.EqualTo("attack.skill"));
            Assert.That(
                roadbuilderBinding.animationName,
                Is.EqualTo("Skill"));
            Assert.That(
                roadbuilderBinding.originalAnimationTicks,
                Is.EqualTo(40));
            Assert.That(
                roadbuilderBinding.segmentOriginalAnimationTicks,
                Is.EqualTo(new[] { 40 }));
            var fragmentBinding = document.bindings.Single(item =>
                item.abilityId
                == "ROADBUILDER_FRAGMENT_THIRD_ATTACK_SPAWN");
            Assert.That(fragmentBinding.typeId, Is.EqualTo("2033"));
            Assert.That(
                fragmentBinding.animationKey,
                Is.EqualTo("attack.skill"));
            Assert.That(
                fragmentBinding.animationName,
                Is.EqualTo("Skill"));
            Assert.That(
                fragmentBinding.originalAnimationTicks,
                Is.EqualTo(40));
            Assert.That(
                fragmentBinding.segmentOriginalAnimationTicks,
                Is.EqualTo(new[] { 40 }));
            var binding = document.bindings.Single(item =>
                item.abilityId == "SUMMON_JELLY_MINIONS");
            Assert.That(binding.typeId, Is.EqualTo("5503"));
            Assert.That(
                binding.abilityId,
                Is.EqualTo("SUMMON_JELLY_MINIONS"));
            Assert.That(binding.animationKey, Is.EqualTo("skill"));
            Assert.That(binding.animationName, Is.EqualTo("Skill"));
            Assert.That(binding.originalAnimationTicks, Is.EqualTo(30));
            var repairBinding = document.bindings.Single(item =>
                item.abilityId == "SUMMON_REPAIR_HELPER");
            Assert.That(repairBinding.typeId, Is.EqualTo("10077"));
            Assert.That(repairBinding.animationKey, Is.EqualTo("skill"));
            Assert.That(repairBinding.animationName, Is.EqualTo("Skill"));
            Assert.That(
                repairBinding.originalAnimationTicks,
                Is.EqualTo(50));
        }

        [Test]
        public void Generate_RejectsUnknownAbilityWithoutTouchingOutput()
        {
            var sourceDirectory = CopyRealSources(
                "unknown-ability",
                "5504_arcslmi.json",
                json => ReplaceRequired(
                    json,
                    "\"innateAbilityIds\": []",
                    "\"innateAbilityIds\": [\"ABILITY_DOES_NOT_EXIST\"]"));
            AssertAtomicFailure(
                sourceDirectory,
                "UNIT_CATALOG_SOURCE_ABILITY_UNKNOWN");
        }

        [Test]
        public void AbilityCatalogGenerator_UsesV2TypeIdsAndPreservesUnknownSummonFailure()
        {
            var output = TempFileWith("must-not-change");
            var exception = InvokeAbilityGenerator(
                UnknownSummonSourceDirectory,
                output);

            StringAssert.Contains(
                "ABILITY_CATALOG_SOURCE_SUMMON_TYPE_UNKNOWN",
                exception.InnerException.Message);
            Assert.That(File.ReadAllText(output), Is.EqualTo("must-not-change"));
        }

        [Test]
        public void AbilityCatalogGenerator_ProjectsPassiveMeleeTargetingTrait()
        {
            var output = NewIsolatedPath(
                "ability-trait-projection",
                "ability-catalog-v1.json");

            RunAbilityGenerator(
                Path.Combine(
                    Application.dataPath,
                    "GameData/Abilities/Json"),
                output);

            var document = JsonUtility.FromJson<AbilityCatalogDocument>(
                File.ReadAllText(output));
            var trait = document.abilities.Single(item =>
                item.abilityId == "UNTARGETABLE_BY_MELEE");
            Assert.That(trait.activationKind, Is.EqualTo("Passive"));
            Assert.That(trait.skillPointGeneration, Is.EqualTo("None"));
            Assert.That(
                trait.unitTrait,
                Is.EqualTo("UntargetableByMelee"));
        }

        [Test]
        public void AbilityCatalogGenerator_ProjectsRepairHelperCentreSummon()
        {
            var output = NewIsolatedPath(
                "ability-centre-summon-projection",
                "ability-catalog-v1.json");

            RunAbilityGenerator(
                Path.Combine(
                    Application.dataPath,
                    "GameData/Abilities/Json"),
                output);

            var document = JsonUtility.FromJson<AbilityCatalogDocument>(
                File.ReadAllText(output));
            var ability = document.abilities.Single(item =>
                item.abilityId == "SUMMON_REPAIR_HELPER");
            Assert.That(ability.activationKind, Is.EqualTo("Timed"));
            Assert.That(ability.initialSkillPoints, Is.EqualTo(3));
            Assert.That(ability.requiredSkillPoints, Is.EqualTo(5));
            Assert.That(
                ability.skillPointGeneration,
                Is.EqualTo("Automatic"));
            Assert.That(ability.summonTypeId, Is.EqualTo("10073"));
            Assert.That(ability.count, Is.EqualTo(1));
            Assert.That(ability.sideLengthCentimetres, Is.Zero);
            Assert.That(ability.inheritPathFromCaster, Is.False);
        }

        [Test]
        public void AbilityCatalogGenerator_ProjectsStackingDefenseReduction()
        {
            var output = NewIsolatedPath(
                "ability-defense-reduction-projection",
                "ability-catalog-v1.json");

            RunAbilityGenerator(
                Path.Combine(
                    Application.dataPath,
                    "GameData/Abilities/Json"),
                output);

            var document = JsonUtility.FromJson<AbilityCatalogDocument>(
                File.ReadAllText(output));
            var ability = document.abilities.Single(item =>
                item.abilityId
                == "STACKING_DEFENSE_REDUCTION_ON_HIT");
            Assert.That(ability.activationKind, Is.EqualTo("Passive"));
            Assert.That(
                ability.skillPointGeneration,
                Is.EqualTo("None"));
            Assert.That(
                ability.onHitDefenseReductionPerStack,
                Is.EqualTo(10));
        }

        [Test]
        public void AbilityCatalogGenerator_ProjectsCateringVehicleAbilities()
        {
            var output = NewIsolatedPath(
                "ability-catering-vehicle-projection",
                "ability-catalog-v1.json");

            RunAbilityGenerator(
                Path.Combine(
                    Application.dataPath,
                    "GameData/Abilities/Json"),
                output);

            var document = JsonUtility.FromJson<AbilityCatalogDocument>(
                File.ReadAllText(output));
            var fortified = document.abilities.Single(item =>
                item.abilityId == "FORTIFIED_CATERING_VEHICLE");
            Assert.That(fortified.activationKind, Is.EqualTo("Passive"));
            Assert.That(fortified.blockCapacityAdditive, Is.EqualTo(2));
            Assert.That(
                fortified.physicalDamageTakenPermille,
                Is.EqualTo(100));
            Assert.That(
                fortified.magicDamageTakenPermille,
                Is.EqualTo(100));
            var charged = document.abilities.Single(item =>
                item.abilityId == "CHARGED_DRINK_AREA_ATTACK");
            Assert.That(charged.activationKind, Is.EqualTo("Timed"));
            Assert.That(charged.initialSkillPoints, Is.EqualTo(20));
            Assert.That(charged.requiredSkillPoints, Is.EqualTo(30));
            Assert.That(charged.targetRangeCentimetres, Is.EqualTo(220));
            Assert.That(charged.areaRadiusCentimetres, Is.EqualTo(150));
            Assert.That(charged.areaDamageType, Is.EqualTo("Physical"));
            Assert.That(
                charged.areaAttackMultiplierPermille,
                Is.EqualTo(1000));
            Assert.That(charged.groundTargetsOnly, Is.True);
            var collision = document.abilities.Single(item =>
                item.abilityId
                == "GROUND_PROXIMITY_COLLISION_DAMAGE");
            Assert.That(
                collision.activationKind,
                Is.EqualTo("Passive"));
            Assert.That(
                collision.proximityEntryRadiusCentimetres,
                Is.EqualTo(50));
            Assert.That(
                collision.proximityEntryDamageType,
                Is.EqualTo("Physical"));
            Assert.That(
                collision.proximityEntryAttackMultiplierPermille,
                Is.EqualTo(1000));
            Assert.That(
                collision.proximityEntryGroundTargetsOnly,
                Is.True);
        }

        [Test]
        public void AbilityCatalogGenerator_ProjectsGreyHatAttackDash()
        {
            var output = NewIsolatedPath(
                "ability-grey-hat-dash-projection",
                "ability-catalog-v1.json");

            RunAbilityGenerator(
                Path.Combine(
                    Application.dataPath,
                    "GameData/Abilities/Json"),
                output);

            var document = JsonUtility.FromJson<AbilityCatalogDocument>(
                File.ReadAllText(output));
            var ability = document.abilities.Single(item =>
                item.abilityId
                == "GREY_HAT_THIRD_ATTACK_DASH");
            Assert.That(ability.activationKind, Is.EqualTo("Passive"));
            Assert.That(
                ability.attackDashFirstTriggerOrdinal,
                Is.EqualTo(3));
            Assert.That(
                ability.attackDashRepeatInterval,
                Is.EqualTo(3));
            Assert.That(
                ability.attackDashDistanceCentimetres,
                Is.EqualTo(150));
            Assert.That(
                ability.attackDashUnblockableDurationTicks,
                Is.EqualTo(20));
        }

        [Test]
        public void AbilityCatalogGenerator_ProjectsBlockedBlink()
        {
            var output = NewIsolatedPath(
                "ability-blocked-blink-projection",
                "ability-catalog-v1.json");

            RunAbilityGenerator(
                Path.Combine(
                    Application.dataPath,
                    "GameData/Abilities/Json"),
                output);

            var document = JsonUtility.FromJson<AbilityCatalogDocument>(
                File.ReadAllText(output));
            var ability = document.abilities.Single(item =>
                item.abilityId == "BLOCKED_BLINK_FORWARD");
            Assert.That(ability.activationKind, Is.EqualTo("Timed"));
            Assert.That(ability.initialSkillPoints, Is.EqualTo(15));
            Assert.That(ability.requiredSkillPoints, Is.EqualTo(15));
            Assert.That(
                ability.skillPointGeneration,
                Is.EqualTo("Automatic"));
            Assert.That(
                ability.timedBlinkDistanceCentimetres,
                Is.EqualTo(150));
        }

        [Test]
        public void AbilityCatalogGenerator_ProjectsRoadbuilderTriggeredSpawns()
        {
            var output = NewIsolatedPath(
                "ability-roadbuilder-spawn-projection",
                "ability-catalog-v1.json");

            RunAbilityGenerator(
                Path.Combine(
                    Application.dataPath,
                    "GameData/Abilities/Json"),
                output);

            var document = JsonUtility.FromJson<AbilityCatalogDocument>(
                File.ReadAllText(output));
            var roadbuilder = document.abilities.Single(item =>
                item.abilityId
                == "ROADBUILDER_THIRD_ATTACK_SPAWN");
            Assert.That(
                roadbuilder.triggeredSpawnKind,
                Is.EqualTo("SuccessfulAttack"));
            Assert.That(
                roadbuilder.triggeredSpawnFirstTriggerOrdinal,
                Is.EqualTo(3));
            Assert.That(
                roadbuilder.triggeredSpawnRepeatInterval,
                Is.EqualTo(3));
            Assert.That(
                roadbuilder.triggeredSpawnSummonTypeId,
                Is.EqualTo("2033"));
            Assert.That(
                roadbuilder.triggeredSpawnSideLengthCentimetres,
                Is.EqualTo(40));
            Assert.That(
                roadbuilder.triggeredSpawnMaxActiveSameType,
                Is.EqualTo(0));

            var received = document.abilities.Single(item =>
                item.abilityId
                == "ROADBUILDER_TENTH_HIT_SPAWN");
            Assert.That(
                received.triggeredSpawnKind,
                Is.EqualTo("DamageReceived"));
            Assert.That(
                received.triggeredSpawnFirstTriggerOrdinal,
                Is.EqualTo(10));
            Assert.That(
                received.triggeredSpawnRepeatInterval,
                Is.EqualTo(10));
            Assert.That(
                received.triggeredSpawnSummonTypeId,
                Is.EqualTo("2033"));
            Assert.That(
                received.triggeredSpawnSideLengthCentimetres,
                Is.EqualTo(40));
            Assert.That(
                received.triggeredSpawnMaxActiveSameType,
                Is.EqualTo(8));

            var fragment = document.abilities.Single(item =>
                item.abilityId
                == "ROADBUILDER_FRAGMENT_THIRD_ATTACK_SPAWN");
            Assert.That(
                fragment.triggeredSpawnKind,
                Is.EqualTo("SuccessfulAttack"));
            Assert.That(
                fragment.triggeredSpawnFirstTriggerOrdinal,
                Is.EqualTo(3));
            Assert.That(
                fragment.triggeredSpawnRepeatInterval,
                Is.EqualTo(3));
            Assert.That(
                fragment.triggeredSpawnSummonTypeId,
                Is.EqualTo("2033"));
            Assert.That(
                fragment.triggeredSpawnSideLengthCentimetres,
                Is.EqualTo(40));
            Assert.That(
                fragment.triggeredSpawnMaxActiveSameType,
                Is.EqualTo(12));
        }

        [Test]
        public void AbilityCatalogGenerator_ProjectsAndWiresCorruptedGolemThresholdAbilities()
        {
            var output = NewIsolatedPath(
                "ability-corrupted-golem-threshold-projection",
                "ability-catalog-v1.json");

            RunAbilityGenerator(
                Path.Combine(
                    Application.dataPath,
                    "GameData/Abilities/Json"),
                output);

            var document = JsonUtility.FromJson<AbilityCatalogDocument>(
                File.ReadAllText(output));
            var spawn = document.abilities.Single(item =>
                item.abilityId
                == "CORRUPTED_GOLEM_THRESHOLD_ADJACENT_SPAWN");
            Assert.That(
                spawn.healthThresholdAdjacentSpawnHitPointsPermille,
                Is.EqualTo(500));
            Assert.That(
                spawn.healthThresholdAdjacentSpawnInclusive,
                Is.False);
            Assert.That(
                spawn.healthThresholdAdjacentSpawnTypeId,
                Is.EqualTo("10002"));

            var moveSpeed = document.abilities.Single(item =>
                item.abilityId
                == "CORRUPTED_GOLEM_THRESHOLD_MOVE_SPEED");
            Assert.That(
                moveSpeed.healthThresholdCombatHitPointsPermille,
                Is.EqualTo(500));
            Assert.That(
                moveSpeed.healthThresholdCombatInclusive,
                Is.False);
            Assert.That(
                moveSpeed.healthThresholdCombatTriggerOnce,
                Is.True);
            Assert.That(
                moveSpeed.healthThresholdCombatDurationTicks,
                Is.Zero);
            Assert.That(
                moveSpeed.healthThresholdCombatAttackMultiplierPermille,
                Is.EqualTo(1000));
            Assert.That(
                moveSpeed.healthThresholdCombatDefenseMultiplierPermille,
                Is.EqualTo(1000));
            Assert.That(
                moveSpeed.healthThresholdCombatMoveSpeedMultiplierPermille,
                Is.EqualTo(2500));

            var unit = JsonUtility.FromJson<UnitAbilitySourceDocument>(
                File.ReadAllText(Path.Combine(
                    RealSourceDirectory(),
                    "10006_trsmgi.json")));
            Assert.That(unit.typeId, Is.EqualTo(10006));
            Assert.That(unit.variants, Has.Length.EqualTo(2));
            foreach (var variant in unit.variants)
                Assert.That(
                    variant.innateAbilityIds,
                    Is.EqualTo(new[]
                    {
                        "CORRUPTED_GOLEM_THRESHOLD_ADJACENT_SPAWN",
                        "CORRUPTED_GOLEM_THRESHOLD_MOVE_SPEED"
                    }),
                    variant.sourceVariant);
        }

        [Test]
        public void AbilityCatalogGenerator_ProjectsAndWiresConstantSelfModifiers()
        {
            var output = NewIsolatedPath(
                "ability-constant-self-modifier-projection",
                "ability-catalog-v1.json");

            RunAbilityGenerator(
                Path.Combine(
                    Application.dataPath,
                    "GameData/Abilities/Json"),
                output);

            var document = JsonUtility.FromJson<AbilityCatalogDocument>(
                File.ReadAllText(output));
            var blockTwo = document.abilities.Single(item =>
                item.abilityId == "BLOCK_CAPACITY_PLUS_TWO");
            Assert.That(blockTwo.blockCapacityAdditive, Is.EqualTo(2));
            Assert.That(
                blockTwo.physicalDamageTakenPermille,
                Is.EqualTo(1000));
            Assert.That(
                blockTwo.magicDamageTakenPermille,
                Is.EqualTo(1000));

            var blockOne = document.abilities.Single(item =>
                item.abilityId == "BLOCK_CAPACITY_PLUS_ONE");
            Assert.That(blockOne.blockCapacityAdditive, Is.EqualTo(1));

            var resistanceSeventy = document.abilities.Single(item =>
                item.abilityId
                == "MAGIC_RESISTANCE_PLUS_SEVENTY");
            Assert.That(
                resistanceSeventy.magicResistanceAdditive,
                Is.EqualTo(70));
            var resistanceSixty = document.abilities.Single(item =>
                item.abilityId
                == "MAGIC_RESISTANCE_PLUS_SIXTY");
            Assert.That(
                resistanceSixty.magicResistanceAdditive,
                Is.EqualTo(60));

            var heterogeneous = document.abilities.Single(item =>
                item.abilityId
                == "HETEROGENEOUS_BEAST_FORTIFICATION");
            Assert.That(
                heterogeneous.attackSpeedAdditive,
                Is.EqualTo(100));
            Assert.That(
                heterogeneous.physicalDamageTakenPermille,
                Is.EqualTo(500));
            Assert.That(
                heterogeneous.magicDamageTakenPermille,
                Is.EqualTo(500));

            AssertUnitAbilityIds(
                "1058_traink.json",
                1058,
                "BLOCK_CAPACITY_PLUS_TWO");
            AssertUnitAbilityIds(
                "1081_sotisd.json",
                1081,
                "BLOCK_CAPACITY_PLUS_TWO");
            AssertUnitAbilityIds(
                "1240_ltgint.json",
                1240,
                "BLOCK_CAPACITY_PLUS_ONE");
            AssertUnitAbilityIds(
                "1165_duhond.json",
                1165,
                "MAGIC_RESISTANCE_PLUS_SEVENTY");
            AssertUnitAbilityIds(
                "1166_dusbr.json",
                1166,
                "MAGIC_RESISTANCE_PLUS_SEVENTY");
            AssertUnitAbilityIds(
                "1170_dushld.json",
                1170,
                "MAGIC_RESISTANCE_PLUS_SEVENTY");
            AssertUnitAbilityIds(
                "1230_dsbudr.json",
                1230,
                "MAGIC_RESISTANCE_PLUS_SIXTY");
            AssertUnitAbilityIds(
                "10127_rkmbst.json",
                10127,
                "HETEROGENEOUS_BEAST_FORTIFICATION");
        }

        [Test]
        public void AbilityCatalogGenerator_ProjectsAndWiresHealthThresholdModifiers()
        {
            var output = NewIsolatedPath(
                "ability-health-threshold-modifier-projection",
                "ability-catalog-v1.json");

            RunAbilityGenerator(
                Path.Combine(
                    Application.dataPath,
                    "GameData/Abilities/Json"),
                output);

            var document = JsonUtility.FromJson<AbilityCatalogDocument>(
                File.ReadAllText(output));
            var revenger = document.abilities.Single(item =>
                item.abilityId
                == "REVENGER_HALF_HEALTH_ATTACK_BOOST");
            Assert.That(
                revenger.healthThresholdCombatHitPointsPermille,
                Is.EqualTo(500));
            Assert.That(
                revenger.healthThresholdCombatInclusive,
                Is.True);
            Assert.That(
                revenger.healthThresholdCombatTriggerOnce,
                Is.False);
            Assert.That(
                revenger.healthThresholdCombatAttackMultiplierPermille,
                Is.EqualTo(2000));
            var avenger = document.abilities.Single(item =>
                item.abilityId
                == "AVENGER_HALF_HEALTH_ATTACK_BOOST");
            Assert.That(
                avenger.healthThresholdCombatAttackMultiplierPermille,
                Is.EqualTo(2800));

            var trailmaker = document.abilities.Single(item =>
                item.abilityId
                == "TRAILMAKER_HALF_HEALTH_FORTIFICATION");
            Assert.That(
                trailmaker.healthThresholdCombatInclusive,
                Is.False);
            Assert.That(
                trailmaker
                    .healthThresholdCombatDefenseMultiplierPermille,
                Is.EqualTo(4000));
            Assert.That(
                trailmaker
                    .healthThresholdCombatBlockCapacityAdditive,
                Is.EqualTo(1));

            var sobering = document.abilities.Single(item =>
                item.abilityId
                == "SOBERING_ASSISTANT_FIRST_DAMAGE_ACCELERATION");
            Assert.That(
                sobering.healthThresholdCombatHitPointsPermille,
                Is.EqualTo(1000));
            Assert.That(
                sobering.healthThresholdCombatTriggerOnce,
                Is.True);
            Assert.That(
                sobering.healthThresholdCombatDurationTicks,
                Is.EqualTo(300));
            Assert.That(
                sobering.healthThresholdCombatAttackSpeedAdditive,
                Is.EqualTo(100));
            Assert.That(
                sobering
                    .healthThresholdCombatMoveSpeedMultiplierPermille,
                Is.EqualTo(2000));
            var eliteSobering = document.abilities.Single(item =>
                item.abilityId
                == "SOBERING_ASSISTANT_FIRST_DAMAGE_ACCELERATION_ELITE_TWO");
            Assert.That(
                eliteSobering.healthThresholdCombatAttackSpeedAdditive,
                Is.EqualTo(150));
            Assert.That(
                eliteSobering
                    .healthThresholdCombatMoveSpeedMultiplierPermille,
                Is.EqualTo(2500));

            var steamTank = document.abilities.Single(item =>
                item.abilityId
                == "STEAM_TANK_HALF_HEALTH_ACCELERATION");
            Assert.That(
                steamTank.healthThresholdCombatHitPointsPermille,
                Is.EqualTo(500));
            Assert.That(
                steamTank.healthThresholdCombatInclusive,
                Is.False);
            Assert.That(
                steamTank.healthThresholdCombatTriggerOnce,
                Is.True);
            Assert.That(
                steamTank.healthThresholdCombatDurationTicks,
                Is.Zero);

            var revengerUnit =
                JsonUtility.FromJson<UnitAbilitySourceDocument>(
                    File.ReadAllText(Path.Combine(
                        RealSourceDirectory(),
                        "1025_reveng.json")));
            Assert.That(
                revengerUnit.variants[0].innateAbilityIds,
                Is.EqualTo(new[]
                {
                    "REVENGER_HALF_HEALTH_ATTACK_BOOST"
                }));
            Assert.That(
                revengerUnit.variants[1].innateAbilityIds,
                Is.EqualTo(new[]
                {
                    "AVENGER_HALF_HEALTH_ATTACK_BOOST"
                }));
            AssertUnitAbilityIds(
                "1232_dssalr.json",
                1232,
                "TRAILMAKER_HALF_HEALTH_FORTIFICATION");
            var soberingUnit =
                JsonUtility.FromJson<UnitAbilitySourceDocument>(
                    File.ReadAllText(Path.Combine(
                        RealSourceDirectory(),
                        "1264_durgrd.json")));
            Assert.That(soberingUnit.typeId, Is.EqualTo(1264));
            Assert.That(
                soberingUnit.variants[0].innateAbilityIds,
                Is.EqualTo(new[]
                {
                    "SOBERING_ASSISTANT_FIRST_DAMAGE_ACCELERATION"
                }));
            Assert.That(
                soberingUnit.variants[1].innateAbilityIds,
                Is.EqualTo(new[]
                {
                    "SOBERING_ASSISTANT_FIRST_DAMAGE_ACCELERATION_ELITE_TWO"
                }));
            var regen160 = document.abilities.Single(item =>
                item.abilityId == "HOST_NATURAL_REGENERATION_160");
            Assert.That(regen160.hitPointsPerSecond, Is.EqualTo(160));
            Assert.That(regen160.lifetimeTicks, Is.Zero);
            var regen500 = document.abilities.Single(item =>
                item.abilityId == "HOST_NATURAL_REGENERATION_500");
            Assert.That(regen500.hitPointsPerSecond, Is.EqualTo(500));
            var lifeLoss330 = document.abilities.Single(item =>
                item.abilityId == "RAGING_HOST_LIFE_LOSS_330");
            Assert.That(lifeLoss330.hitPointsPerSecond, Is.EqualTo(-330));
            AssertUnitSingleAbilityIds(
                "1043_zomsabr.json",
                1043,
                "HOST_NATURAL_REGENERATION_160",
                "HOST_NATURAL_REGENERATION_300");
            AssertUnitSingleAbilityIds(
                "1044_zomstr.json",
                1044,
                "HOST_NATURAL_REGENERATION_400",
                "HOST_NATURAL_REGENERATION_500");
            AssertUnitSingleAbilityIds(
                "1061_zomshd.json",
                1061,
                "HOST_NATURAL_REGENERATION_400",
                "HOST_NATURAL_REGENERATION_500");
            AssertUnitSingleAbilityIds(
                "1062_rager.json",
                1062,
                "RAGING_HOST_LIFE_LOSS_330",
                "RAGING_HOST_LIFE_LOSS_500");
            AssertUnitAbilityIds(
                "1274_stmram.json",
                1274,
                "STEAM_TANK_HALF_HEALTH_ACCELERATION");
        }

        [Test]
        public void AbilityCatalogGenerator_ProjectsRemainingBondsEffects()
        {
            var output = NewIsolatedPath(
                "ability-expanded-bonds-projection",
                "ability-catalog-v1.json");
            RunAbilityGenerator(
                Path.Combine(
                    Application.dataPath,
                    "GameData/Abilities/Json"),
                output);
            var document = JsonUtility.FromJson<AbilityCatalogDocument>(
                File.ReadAllText(output));

            Assert.That(document.abilities, Has.Length.EqualTo(67));
            var tactical = document.abilities.Single(item =>
                item.abilityId == "TACTICAL_COMMAND_AURA");
            Assert.That(tactical.auraIsGlobal, Is.True);
            Assert.That(tactical.auraAttackMultiplierPermille, Is.EqualTo(1100));
            Assert.That(tactical.auraDefenseAdditive, Is.EqualTo(100));
            Assert.That(tactical.auraGrantedStatusTag, Is.EqualTo("TacticalCommand"));
            var tacticalAttack = document.abilities.Single(item =>
                item.abilityId == "TACTICAL_COMMAND_ATTACK");
            Assert.That(tacticalAttack.requiredStatusTag, Is.EqualTo("TacticalCommand"));
            Assert.That(
                tacticalAttack.requiredStatusTagAttackMultiplierPermille,
                Is.EqualTo(1500));

            var deathSpawn = document.abilities.Single(item =>
                item.abilityId == "CORE_FEEDER_DEATH_SPAWN");
            Assert.That(deathSpawn.deathSpawnOptions, Has.Length.EqualTo(2));
            Assert.That(deathSpawn.deathSpawnCount, Is.EqualTo(1));
            Assert.That(
                deathSpawn.deathSpawnSummonedMoveSpeedMultiplierPermille,
                Is.EqualTo(3000));
            var prisoner = document.abilities.Single(item =>
                item.abilityId == "PRISONER_RELEASE_LEADER");
            Assert.That(
                prisoner.attackCountTransitionBeforeAttackOrdinal,
                Is.EqualTo(4));
            Assert.That(prisoner.attackCountReleasesAlliedStates, Is.True);
            Assert.That(
                prisoner.persistentPresentationStateTag,
                Is.EqualTo("red"));
            var cross = document.abilities.Single(item =>
                item.abilityId == "TRUMPETER_THIRD_ATTACK_CROSS");
            Assert.That(
                cross.attackAreaShape,
                Is.EqualTo("OrthogonalAdjacentCells"));
            Assert.That(cross.attackAreaFirstAttackOrdinal, Is.EqualTo(3));
            var reaction = document.abilities.Single(item =>
                item.abilityId == "VEIN_GUARD_DAMAGE_REACTION");
            Assert.That(
                reaction.onDamageReactionDamageType,
                Is.EqualTo("Magic"));
            Assert.That(reaction.onDamageReactionDamageAmount, Is.EqualTo(200));
            var fullHeal = document.abilities.Single(item =>
                item.abilityId == "BERRY_CRAB_HALF_HEALTH_FULL_HEAL");
            Assert.That(
                fullHeal.healthThresholdFullHealHitPointsPermille,
                Is.EqualTo(500));
            Assert.That(
                fullHeal.persistentPresentationStateTag,
                Is.EqualTo("b"));
            var feeder = document.abilities.Single(item =>
                item.abilityId == "FEED_ORIGINIUM_BUG_HALF_HEALTH_TRANSFORM");
            Assert.That(
                feeder.persistentPresentationStateTag,
                Is.EqualTo("b"));
            var anvilAura = document.abilities.Single(item =>
                item.abilityId == "ANVIL_SUPPORT_AURA");
            Assert.That(anvilAura.auraTargetSide, Is.EqualTo("Allies"));
            Assert.That(anvilAura.auraIsGlobal, Is.False);
            Assert.That(anvilAura.auraRadiusCentimetres, Is.EqualTo(250));
            Assert.That(anvilAura.auraExcludeSource, Is.True);
            Assert.That(anvilAura.auraNonStackingByAbilityId, Is.True);
            Assert.That(anvilAura.auraDefenseAdditive, Is.EqualTo(200));
            Assert.That(anvilAura.auraHitPointsPerSecond, Is.EqualTo(400));
            var eliteTwoAnvilAura = document.abilities.Single(item =>
                item.abilityId == "ANVIL_SUPPORT_AURA_ELITE_TWO_BONUS");
            Assert.That(eliteTwoAnvilAura.auraTargetSide, Is.EqualTo("Allies"));
            Assert.That(eliteTwoAnvilAura.auraIsGlobal, Is.False);
            Assert.That(
                eliteTwoAnvilAura.auraRadiusCentimetres,
                Is.EqualTo(250));
            Assert.That(eliteTwoAnvilAura.auraExcludeSource, Is.True);
            Assert.That(
                eliteTwoAnvilAura.auraNonStackingByAbilityId,
                Is.True);
            Assert.That(eliteTwoAnvilAura.auraDefenseAdditive, Is.EqualTo(100));
            Assert.That(
                eliteTwoAnvilAura.auraHitPointsPerSecond,
                Is.EqualTo(100));

            AssertUnitAbilityIds(
                "1080_sotidp.json",
                1080,
                "TACTICAL_COMMAND_AURA");
            AssertUnitAbilityIds(
                "1169_duphx.json",
                1169,
                "MAGIC_RESISTANCE_PLUS_SEVENTY",
                "DEEP_POOL_PHALANX_NEARBY_DEFENSE");
            AssertUnitAbilityIds(
                "10126_rkbomb.json",
                10126,
                "HETEROGENEOUS_BUG_DAMAGE_REDUCTION",
                "HETEROGENEOUS_BUG_DAMAGE_REACTION");
            var anvil = JsonUtility.FromJson<UnitAbilitySourceDocument>(
                File.ReadAllText(Path.Combine(
                    RealSourceDirectory(),
                    "1146_defspd.json")));
            Assert.That(anvil.typeId, Is.EqualTo(1146));
            Assert.That(anvil.variants, Has.Length.EqualTo(2));
            Assert.That(
                anvil.variants[0].innateAbilityIds,
                Is.EqualTo(new[]
                {
                    "UNTARGETABLE_BY_MELEE",
                    "ANVIL_LIFETIME",
                    "ANVIL_SUPPORT_AURA"
                }));
            Assert.That(
                anvil.variants[1].innateAbilityIds,
                Is.EqualTo(new[]
                {
                    "UNTARGETABLE_BY_MELEE",
                    "ANVIL_LIFETIME",
                    "ANVIL_SUPPORT_AURA",
                    "ANVIL_SUPPORT_AURA_ELITE_TWO_BONUS"
                }));
        }

        [Test]
        public void UnitJsonBake_CollectsOnlyExplicitV2AbilityIds()
        {
            var ids = InvokeDeclaredAbilityCollector(
                Path.Combine(
                    Application.dataPath,
                    "GameData/Units/EliteVariants/Json"));

            Assert.That(ids, Is.EqualTo(new[]
            {
                "ANVIL_LIFETIME",
                "ANVIL_SUPPORT_AURA",
                "ANVIL_SUPPORT_AURA_ELITE_TWO_BONUS",
                "AVENGER_HALF_HEALTH_ATTACK_BOOST",
                "BERRY_BUG_LIFETIME",
                "BERRY_CRAB_HALF_HEALTH_FULL_HEAL",
                "BLOCKED_BLINK_FORWARD",
                "BLOCK_CAPACITY_PLUS_ONE",
                "BLOCK_CAPACITY_PLUS_TWO",
                "CHARGED_DRINK_AREA_ATTACK",
                "CORE_FEEDER_DEATH_SPAWN",
                "CORRUPTED_GOLEM_THRESHOLD_ADJACENT_SPAWN",
                "CORRUPTED_GOLEM_THRESHOLD_MOVE_SPEED",
                "DEEP_POOL_PHALANX_NEARBY_DEFENSE",
                "DEEP_SEA_PREDATOR_EVASION",
                "DRONE_DEFENSE_AURA_300",
                "DRONE_MAGIC_RESISTANCE_AURA_30",
                "FAMILY_CAR_DEATH_SPAWN",
                "FEED_ORIGINIUM_BUG_HALF_HEALTH_TRANSFORM",
                "FLAUTIST_THIRD_ATTACK_BOOST",
                "FORTIFIED_CATERING_VEHICLE",
                "FROST_ATTACK_SPEED_AURA",
                "GREY_HAT_THIRD_ATTACK_DASH",
                "GROUND_PROXIMITY_COLLISION_DAMAGE",
                "HETEROGENEOUS_BEAST_FORTIFICATION",
                "HETEROGENEOUS_BUG_DAMAGE_REACTION",
                "HETEROGENEOUS_BUG_DAMAGE_REDUCTION",
                "HOST_NATURAL_REGENERATION_160",
                "HOST_NATURAL_REGENERATION_300",
                "HOST_NATURAL_REGENERATION_400",
                "HOST_NATURAL_REGENERATION_500",
                "MAGIC_RESISTANCE_PLUS_SEVENTY",
                "MAGIC_RESISTANCE_PLUS_SIXTY",
                "MALIGNANT_TUMOR_BLOCKER_SLOW",
                "MUTANT_ROCKSPIDER_DEATH_SPAWN",
                "MUTANT_ROCKSPIDER_DEATH_SPAWN_ELITE_TWO",
                "MUTANT_SANDBEAST_DEATH_SPAWN",
                "MUTANT_SANDBEAST_DEATH_SPAWN_ELITE_TWO",
                "PATHFINDER_FIRST_ATTACK_BOOST",
                "PRISONER_RELEASE_BOXER",
                "PRISONER_RELEASE_LEADER",
                "PRISONER_RELEASE_STANDARD",
                "PRISONER_RELEASE_STRONG",
                "RAGING_HOST_LIFE_LOSS_330",
                "RAGING_HOST_LIFE_LOSS_500",
                "REVENGER_HALF_HEALTH_ATTACK_BOOST",
                "ROADBUILDER_FRAGMENT_THIRD_ATTACK_SPAWN",
                "ROADBUILDER_TENTH_HIT_SPAWN",
                "ROADBUILDER_THIRD_ATTACK_SPAWN",
                "SOBERING_ASSISTANT_FIRST_DAMAGE_ACCELERATION",
                "SOBERING_ASSISTANT_FIRST_DAMAGE_ACCELERATION_ELITE_TWO",
                "SOLIDIFIED_GOLEM_SPLASH_ATTACK",
                "STACKING_DEFENSE_REDUCTION_ON_HIT",
                "STEAM_TANK_HALF_HEALTH_ACCELERATION",
                "SUMMON_JELLY_MINIONS",
                "SUMMON_REPAIR_HELPER",
                "TACTICAL_COMMAND_ATTACK",
                "TACTICAL_COMMAND_AURA",
                "TACTICAL_COMMAND_MOVE_SPEED",
                "TRAILMAKER_HALF_HEALTH_FORTIFICATION",
                "TRUMPETER_THIRD_ATTACK_CROSS",
                "UNBLOCKED_DAMAGE_REDUCTION_HALF",
                "UNTARGETABLE_BY_MELEE",
                "VEIN_GUARD_DAMAGE_REACTION",
                "WIND_PLAYER_UNBLOCKED_ATTACK_CHARGE",
                "WINTERWISP_DEATH_BLAST",
                "WINTERWISP_LIFE_LOSS"
            }));
        }

        private static void AssertUnitAbilityIds(
            string fileName,
            int expectedTypeId,
            params string[] expectedAbilityIds)
        {
            var unit = JsonUtility.FromJson<UnitAbilitySourceDocument>(
                File.ReadAllText(Path.Combine(
                    RealSourceDirectory(),
                    fileName)));
            Assert.That(unit.typeId, Is.EqualTo(expectedTypeId), fileName);
            Assert.That(unit.variants, Is.Not.Empty, fileName);
            foreach (var variant in unit.variants)
                Assert.That(
                    variant.innateAbilityIds,
                    Is.EqualTo(expectedAbilityIds),
                    variant.sourceVariant);
        }

        private static void AssertUnitSingleAbilityIds(
            string fileName,
            int expectedTypeId,
            params string[] expectedAbilityIdsByVariant)
        {
            var unit = JsonUtility.FromJson<UnitAbilitySourceDocument>(
                File.ReadAllText(Path.Combine(
                    RealSourceDirectory(),
                    fileName)));
            Assert.That(unit.typeId, Is.EqualTo(expectedTypeId), fileName);
            Assert.That(
                unit.variants,
                Has.Length.EqualTo(expectedAbilityIdsByVariant.Length),
                fileName);
            for (var index = 0;
                 index < expectedAbilityIdsByVariant.Length;
                 index++)
                Assert.That(
                    unit.variants[index].innateAbilityIds,
                    Is.EqualTo(new[]
                    {
                        expectedAbilityIdsByVariant[index]
                    }),
                    unit.variants[index].sourceVariant);
        }

        private static void AssertAtomicFailure(
            string sourceDirectory,
            string expectedErrorCode)
        {
            const string sentinel = "sentinel-do-not-overwrite";
            var outputPath = Path.Combine(
                Path.GetDirectoryName(sourceDirectory),
                "unit-catalog-v1.json");
            File.WriteAllText(outputPath, sentinel);
            var hashBefore = RuntimeCatalogHash();
            Assert.That(hashBefore, Is.EqualTo(ExpectedRuntimeCatalogHash));

            var exception = Assert.Throws<TargetInvocationException>(
                () => InvokeGenerator(sourceDirectory, outputPath));

            Assert.That(
                FlattenMessages(exception),
                Does.Contain(expectedErrorCode));
            Assert.That(File.ReadAllText(outputPath), Is.EqualTo(sentinel));
            Assert.That(RuntimeCatalogHash(), Is.EqualTo(hashBefore));
        }

        private static string MakeNonAttacker(string json)
        {
            var normalized = json.Replace("\r\n", "\n");
            normalized = ReplaceRequired(
                normalized,
                "\"attackMethod\": 1",
                "\"attackMethod\": 0");
            normalized = ReplaceRequired(
                normalized,
                "\"damageType\": \"Physical\"",
                "\"damageType\": \"None\"");
            normalized = ReplaceRequired(
                normalized,
                "\"attack\": 290",
                "\"attack\": 0");
            normalized = ReplaceRequired(
                normalized,
                "\"attackIntervalSeconds\": 1.5",
                "\"attackIntervalSeconds\": 0");
            return ReplaceRequired(
                normalized,
                "          {\n"
                + "            \"key\": \"attack\",\n"
                + "            \"name\": \"Attack\",\n"
                + "            \"durationSeconds\": 1.166667\n"
                + "          },\n",
                string.Empty);
        }

        private static string CopyRealSources(
            string scenario,
            string transformedFileName,
            Func<string, string> transform)
        {
            var fixtureDirectory = NewIsolatedPath(scenario, "sources");
            Directory.CreateDirectory(fixtureDirectory);
            foreach (var fileName in LegacyProjectionFileNames)
            {
                var sourcePath = Path.Combine(RealSourceDirectory(), fileName);
                Assert.That(File.Exists(sourcePath), Is.True, "Missing legacy fixture source.");
                var json = File.ReadAllText(sourcePath);
                File.WriteAllText(
                    Path.Combine(fixtureDirectory, fileName),
                    string.Equals(
                        fileName,
                        transformedFileName,
                        StringComparison.Ordinal)
                        ? transform(json)
                        : json);
            }

            return fixtureDirectory;
        }

        private static string ReplaceRequired(
            string source,
            string oldValue,
            string newValue)
        {
            var index = source.IndexOf(oldValue, StringComparison.Ordinal);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "Fixture token must exist.");
            return source.Substring(0, index)
                   + newValue
                   + source.Substring(index + oldValue.Length);
        }

        private static void InvokeGenerator(string sourceDirectory, string outputPath)
        {
            var generator = Type.GetType(
                "UnitCatalogGenerator, Assembly-CSharp-Editor");
            Assert.That(generator, Is.Not.Null, "Unit catalog generator type must exist.");
            var generate = generator.GetMethod(
                "Generate",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(string) },
                null);
            Assert.That(
                generate,
                Is.Not.Null,
                "Generate(string, string) must exist and remain private.");
            generate.Invoke(null, new object[] { sourceDirectory, outputPath });
        }

        private static TargetInvocationException InvokeAbilityGenerator(
            string sourceDirectory,
            string outputPath)
        {
            return Assert.Throws<TargetInvocationException>(
                () => RunAbilityGenerator(sourceDirectory, outputPath));
        }

        private static void RunAbilityGenerator(
            string sourceDirectory,
            string outputPath)
        {
            var generator = Type.GetType(
                "AbilityCatalogGenerator, Assembly-CSharp-Editor");
            Assert.That(
                generator,
                Is.Not.Null,
                "Ability catalog generator type must exist.");
            var generate = generator.GetMethod(
                "Generate",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(string) },
                null);
            Assert.That(
                generate,
                Is.Not.Null,
                "Generate(string, string) must exist and remain private.");
            generate.Invoke(
                null,
                new object[] { sourceDirectory, outputPath });
        }

        private static void InvokeSkillAnimationGenerator(
            string unitSourceDirectory,
            string abilitySourceDirectory,
            string outputPath)
        {
            var generator = Type.GetType(
                "SkillAnimationCatalogGenerator, Assembly-CSharp-Editor");
            Assert.That(
                generator,
                Is.Not.Null,
                "Skill animation catalog generator type must exist.");
            var generate = generator.GetMethod(
                "Generate",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(string), typeof(string) },
                null);
            Assert.That(
                generate,
                Is.Not.Null,
                "Generate(string, string, string) must exist.");
            generate.Invoke(
                null,
                new object[]
                {
                    unitSourceDirectory,
                    abilitySourceDirectory,
                    outputPath
                });
        }

        private static IReadOnlyList<string> InvokeDeclaredAbilityCollector(
            string sourceDirectory)
        {
            var bakeTool = Type.GetType(
                "UnitJsonBake, Assembly-CSharp-Editor");
            Assert.That(bakeTool, Is.Not.Null, "Unit JSON bake tool must exist.");
            var collect = bakeTool.GetMethod(
                "CollectDeclaredAbilityIds",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(string) },
                null);
            Assert.That(
                collect,
                Is.Not.Null,
                "CollectDeclaredAbilityIds(string) must exist and remain private.");
            return (IReadOnlyList<string>)collect.Invoke(
                null,
                new object[] { sourceDirectory });
        }

        private static string UnknownSummonSourceDirectory
        {
            get
            {
                var sourceDirectory = NewIsolatedPath(
                    "ability-unknown-summon",
                    "sources");
                Directory.CreateDirectory(sourceDirectory);
                File.WriteAllText(
                    Path.Combine(sourceDirectory, "unknown-summon.json"),
                    "{\"schemaVersion\":\"ability-source-v1\","
                    + "\"abilityId\":\"UNKNOWN_SUMMON\","
                    + "\"displayNameZhHans\":\"\","
                    + "\"descriptionZhHans\":\"\","
                    + "\"activationKind\":\"Timed\","
                    + "\"silencePolicy\":\"Unaffected\","
                    + "\"skillPoints\":{\"initial\":0,\"required\":1,"
                    + "\"generation\":\"Automatic\"},"
                    + "\"animationKey\":\"skill\","
                    + "\"effects\":[{\"kind\":\"Summon\","
                    + "\"summonTypeId\":\"does-not-exist\",\"count\":1,"
                    + "\"spawnArea\":{\"shape\":\"Square\","
                    + "\"center\":\"CasterPosition\","
                    + "\"sideLengthMetres\":1.0},"
                    + "\"inheritPathFromCaster\":false}]}");
                return sourceDirectory;
            }
        }

        private static string TempFileWith(string contents)
        {
            var path = NewIsolatedPath(
                "ability-output",
                "ability-catalog-v1.json");
            File.WriteAllText(path, contents);
            return path;
        }

        private static string RealSourceDirectory()
        {
            return Path.Combine(
                Application.dataPath,
                "GameData/Units/EliteVariants/Json");
        }

        private static string NewIsolatedPath(string scenario, string leafName)
        {
            var root = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "../Temp/UnitEliteVariantsV2/CatalogProjection"));
            var scenarioRoot = Path.Combine(
                root,
                scenario + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scenarioRoot);
            return Path.Combine(scenarioRoot, leafName);
        }

        private static string RuntimeCatalogHash()
        {
            var path = Path.Combine(
                Application.dataPath,
                "Resources/BattleData/unit-catalog-v1.json");
            var normalized = File.ReadAllText(path)
                .Replace("\r\n", "\n")
                .Replace("\r", "\n");
            var bytes = Encoding.UTF8.GetBytes(normalized);
            using (var sha256 = SHA256.Create())
            {
                return BitConverter.ToString(sha256.ComputeHash(bytes))
                    .Replace("-", string.Empty);
            }
        }

        private static string FlattenMessages(Exception exception)
        {
            var messages = new List<string>();
            for (var current = exception; current != null; current = current.InnerException)
            {
                messages.Add(current.Message);
            }

            return string.Join(" | ", messages);
        }

        [Serializable]
        private sealed class CatalogProjectionDocument
        {
            public CatalogProjectionEntry[] units;
        }

        [Serializable]
        private sealed class CatalogProjectionEntry
        {
            public string typeId;
            public int deploymentCost;
            public int rarity;
            public int attackAnimationDurationTicks;
            public int unitSkelType;
            public string moveAnimation;
            public string hitAnimation;
        }

        [Serializable]
        private sealed class SkillAnimationCatalogDocument
        {
            public SkillAnimationCatalogEntry[] bindings;
        }

        [Serializable]
        private sealed class SkillAnimationCatalogEntry
        {
            public string typeId;
            public string abilityId;
            public string animationKey;
            public string animationName;
            public int originalAnimationTicks;
            public int[] segmentOriginalAnimationTicks;
            public string presentationStateTag;
            public string stateIdleAnimation;
            public string stateMoveAnimation;
            public string stateAttackAnimation;
            public string stateDeathAnimation;
        }

        [Serializable]
        private sealed class AbilityCatalogDocument
        {
            public AbilityCatalogEntry[] abilities;
        }

        [Serializable]
        private sealed class AbilityCatalogEntry
        {
            public string abilityId;
            public string activationKind;
            public int initialSkillPoints;
            public int requiredSkillPoints;
            public string skillPointGeneration;
            public string summonTypeId;
            public int count;
            public int sideLengthCentimetres;
            public bool inheritPathFromCaster;
            public string unitTrait;
            public int onHitDefenseReductionPerStack;
            public int blockCapacityAdditive;
            public int magicResistanceAdditive;
            public int attackSpeedAdditive;
            public int physicalDamageTakenPermille;
            public int magicDamageTakenPermille;
            public int hitPointsPerSecond;
            public int lifetimeTicks;
            public string onDamageReactionDamageType;
            public int onDamageReactionDamageAmount;
            public int attackCountTransitionBeforeAttackOrdinal;
            public bool attackCountReleasesAlliedStates;
            public string persistentPresentationStateTag;
            public DeathSpawnOptionEntry[] deathSpawnOptions;
            public int deathSpawnCount;
            public int deathSpawnSummonedMoveSpeedMultiplierPermille;
            public string auraTargetSide;
            public bool auraIsGlobal;
            public int auraRadiusCentimetres;
            public bool auraExcludeSource;
            public bool auraNonStackingByAbilityId;
            public int auraAttackMultiplierPermille;
            public int auraDefenseAdditive;
            public int auraHitPointsPerSecond;
            public string auraGrantedStatusTag;
            public string attackAreaShape;
            public int attackAreaFirstAttackOrdinal;
            public int healthThresholdFullHealHitPointsPermille;
            public string requiredStatusTag;
            public int requiredStatusTagAttackMultiplierPermille;
            public int targetRangeCentimetres;
            public int areaRadiusCentimetres;
            public string areaDamageType;
            public int areaAttackMultiplierPermille;
            public bool groundTargetsOnly;
            public int attackDashFirstTriggerOrdinal;
            public int attackDashRepeatInterval;
            public int attackDashDistanceCentimetres;
            public int attackDashUnblockableDurationTicks;
            public int timedBlinkDistanceCentimetres;
            public int proximityEntryRadiusCentimetres;
            public string proximityEntryDamageType;
            public int proximityEntryAttackMultiplierPermille;
            public bool proximityEntryGroundTargetsOnly;
            public string triggeredSpawnKind;
            public int triggeredSpawnFirstTriggerOrdinal;
            public int triggeredSpawnRepeatInterval;
            public string triggeredSpawnSummonTypeId;
            public int triggeredSpawnSideLengthCentimetres;
            public int triggeredSpawnMaxActiveSameType;
            public int healthThresholdCombatHitPointsPermille;
            public bool healthThresholdCombatInclusive;
            public bool healthThresholdCombatTriggerOnce;
            public int healthThresholdCombatDurationTicks;
            public int healthThresholdCombatAttackMultiplierPermille;
            public int healthThresholdCombatDefenseMultiplierPermille;
            public int healthThresholdCombatBlockCapacityAdditive;
            public int healthThresholdCombatAttackSpeedAdditive;
            public int healthThresholdCombatMoveSpeedMultiplierPermille;
            public bool healthThresholdCombatMakesUnblockable;
            public int healthThresholdAdjacentSpawnHitPointsPermille;
            public bool healthThresholdAdjacentSpawnInclusive;
            public string healthThresholdAdjacentSpawnTypeId;
        }

        [Serializable]
        private sealed class DeathSpawnOptionEntry
        {
            public string summonTypeId;
            public int weight;
        }

        [Serializable]
        private sealed class UnitAbilitySourceDocument
        {
            public int typeId;
            public UnitAbilitySourceVariant[] variants;
        }

        [Serializable]
        private sealed class UnitAbilitySourceVariant
        {
            public string sourceVariant;
            public string[] innateAbilityIds;
        }
    }
}
