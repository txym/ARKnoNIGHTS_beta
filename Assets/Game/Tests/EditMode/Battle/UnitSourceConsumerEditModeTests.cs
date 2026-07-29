using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
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
                    "BLOCKED_BLINK_FORWARD",
                    "CHARGED_DRINK_AREA_ATTACK",
                    "GREY_HAT_THIRD_ATTACK_DASH",
                    "SUMMON_JELLY_MINIONS",
                    "SUMMON_REPAIR_HELPER"
                }));
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
        public void UnitJsonBake_CollectsOnlyExplicitV2AbilityIds()
        {
            var ids = InvokeDeclaredAbilityCollector(
                Path.Combine(
                    Application.dataPath,
                    "GameData/Units/EliteVariants/Json"));

            Assert.That(ids, Is.EqualTo(new[]
            {
                "BLOCKED_BLINK_FORWARD",
                "CHARGED_DRINK_AREA_ATTACK",
                "FORTIFIED_CATERING_VEHICLE",
                "GREY_HAT_THIRD_ATTACK_DASH",
                "STACKING_DEFENSE_REDUCTION_ON_HIT",
                "SUMMON_JELLY_MINIONS",
                "SUMMON_REPAIR_HELPER",
                "UNTARGETABLE_BY_MELEE"
            }));
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
            using (var sha256 = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha256.ComputeHash(stream))
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
        }
    }
}
