using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
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
            var binding = document.bindings.Single();
            Assert.That(binding.typeId, Is.EqualTo("5503"));
            Assert.That(
                binding.abilityId,
                Is.EqualTo("SUMMON_JELLY_MINIONS"));
            Assert.That(binding.animationKey, Is.EqualTo("skill"));
            Assert.That(binding.animationName, Is.EqualTo("Skill"));
            Assert.That(binding.originalAnimationTicks, Is.EqualTo(30));
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
        public void UnitJsonBake_CollectsOnlyExplicitV2AbilityIds()
        {
            var ids = InvokeDeclaredAbilityCollector(
                Path.Combine(
                    Application.dataPath,
                    "GameData/Units/EliteVariants/Json"));

            Assert.That(ids, Is.EqualTo(new[] { "SUMMON_JELLY_MINIONS" }));
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
            return Assert.Throws<TargetInvocationException>(
                () => generate.Invoke(
                    null,
                    new object[] { sourceDirectory, outputPath }));
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
        }
    }
}
