using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ArknoNights.Battle.Tests
{
    public sealed class UnitEliteVariantSourceEditModeTests
    {
        private const string ValidThreeVariantFixture = @"
{
  ""schemaVersion"": ""unit-elite-variants-v2"",
  ""typeId"": 1000,
  ""common"": {
    ""rarity"": 1,
    ""deploymentCost"": 2,
    ""attackMethod"": 1,
    ""actionMethod"": 1,
    ""attackRadiusMetres"": 0,
    ""blockRadiusMetres"": 0,
    ""canBlock"": true,
    ""blockCapacity"": 1,
    ""tauntLevel"": 0,
    ""damageType"": ""Physical""
  },
  ""variants"": [
    {
      ""minEliteLevel"": 0,
      ""sourceVariant"": ""1000_gopro"",
      ""statsLevel"": 0,
      ""displayNameZhHans"": ""猎狗"",
      ""skillDescriptionZhHans"": ""基础技能"",
      ""stats"": {
        ""combat"": {
          ""maxHitPoints"": 820,
          ""attack"": 190,
          ""defense"": 0,
          ""magicResistance"": 20
        },
        ""shared"": {
          ""moveSpeedMetresPerSecond"": 1.9,
          ""attackIntervalSeconds"": 1.4,
          ""lifeDeduct"": 1
        }
      },
      ""model"": {
        ""resourceKey"": ""gopro"",
        ""skeletonDataResourceName"": ""enemy_1000_gopro_SkeletonData"",
        ""profilePictureResourceName"": ""UIImage_1000_gopro"",
        ""animations"": [
          { ""key"": ""idle"", ""name"": ""Idle"" },
          { ""key"": ""move"", ""name"": ""Move"" },
          { ""key"": ""attack"", ""name"": ""Attack"", ""durationSeconds"": 0.6 },
          { ""key"": ""death"", ""name"": ""Death"" }
        ]
      },
      ""innateAbilityIds"": [""ABILITY_A""]
    },
    {
      ""minEliteLevel"": 2,
      ""sourceVariant"": ""1000_gopro_2"",
      ""statsLevel"": 0,
      ""displayNameZhHans"": ""猎狗pro"",
      ""stats"": {
        ""combat"": {
          ""maxHitPoints"": 1700,
          ""attack"": 260,
          ""defense"": 0,
          ""magicResistance"": 20
        }
      },
      ""model"": {
        ""resourceKey"": ""gopro_2"",
        ""skeletonDataResourceName"": ""enemy_1000_gopro_2_SkeletonData"",
        ""profilePictureResourceName"": ""UIImage_1000_gopro_2"",
        ""animations"": [
          { ""key"": ""idle"", ""name"": ""Idle"" },
          { ""key"": ""move"", ""name"": ""Move"" },
          { ""key"": ""attack"", ""name"": ""Attack"", ""durationSeconds"": 0.6 },
          { ""key"": ""death"", ""name"": ""Death"" }
        ]
      }
    },
    {
      ""minEliteLevel"": 3,
      ""sourceVariant"": ""1000_gopro_3"",
      ""statsLevel"": 0,
      ""displayNameZhHans"": ""狂暴的猎狗pro"",
      ""stats"": {
        ""combat"": {
          ""maxHitPoints"": 3000,
          ""attack"": 370,
          ""defense"": 0,
          ""magicResistance"": 20
        }
      },
      ""model"": {
        ""resourceKey"": ""gopro_3"",
        ""skeletonDataResourceName"": ""enemy_1000_gopro_3_SkeletonData"",
        ""profilePictureResourceName"": ""UIImage_1000_gopro_3"",
        ""animations"": [
          { ""key"": ""idle"", ""name"": ""Idle"" },
          { ""key"": ""move"", ""name"": ""Move"" },
          { ""key"": ""attack"", ""name"": ""Attack"", ""durationSeconds"": 0.6 },
          { ""key"": ""death"", ""name"": ""Death"" }
        ]
      }
    }
  ]
}";

        private const string InheritanceFixture = @"
{
  ""schemaVersion"": ""unit-elite-variants-v2"",
  ""typeId"": 9000,
  ""common"": {
    ""rarity"": 1,
    ""deploymentCost"": 2,
    ""attackMethod"": 1,
    ""actionMethod"": 1,
    ""attackRadiusMetres"": 0,
    ""blockRadiusMetres"": 0,
    ""canBlock"": true,
    ""blockCapacity"": 1,
    ""tauntLevel"": 0,
    ""damageType"": ""Physical""
  },
  ""variants"": [
    {
      ""minEliteLevel"": 0,
      ""sourceVariant"": ""9000_fixture"",
      ""statsLevel"": 0,
      ""displayNameZhHans"": ""fixture"",
      ""skillDescriptionZhHans"": ""fixture skill"",
      ""stats"": {
        ""combat"": {
          ""maxHitPoints"": 100,
          ""attack"": 10,
          ""defense"": 0,
          ""magicResistance"": 0
        },
        ""shared"": {
          ""moveSpeedMetresPerSecond"": 1,
          ""attackIntervalSeconds"": 1,
          ""lifeDeduct"": 1
        }
      },
      ""model"": {
        ""resourceKey"": ""fixture"",
        ""skeletonDataResourceName"": ""enemy_9000_fixture_SkeletonData"",
        ""profilePictureResourceName"": ""UIImage_9000_fixture"",
        ""animations"": [
          { ""key"": ""idle"", ""name"": ""Idle"" },
          { ""key"": ""move"", ""name"": ""Move"" },
          { ""key"": ""attack"", ""name"": ""Attack"", ""durationSeconds"": 0.5 },
          { ""key"": ""death"", ""name"": ""Death"" }
        ]
      },
      ""innateAbilityIds"": [""ABILITY_A""]
    },
    {
      ""minEliteLevel"": 2,
      ""sourceVariant"": ""9000_fixture"",
      ""statsLevel"": 0
    },
    {
      ""minEliteLevel"": 3,
      ""sourceVariant"": ""9000_fixture"",
      ""statsLevel"": 0,
      ""innateAbilityIds"": []
    }
  ]
}";

        private const string ValidStationaryNonAttackerFixture = @"
{
  ""schemaVersion"": ""unit-elite-variants-v2"",
  ""typeId"": 10002,
  ""common"": {
    ""rarity"": 1,
    ""deploymentCost"": 2,
    ""attackMethod"": 0,
    ""actionMethod"": 4,
    ""attackRadiusMetres"": 0,
    ""blockRadiusMetres"": 0,
    ""canBlock"": false,
    ""blockCapacity"": 0,
    ""tauntLevel"": 0,
    ""damageType"": ""None""
  },
  ""variants"": [
    {
      ""minEliteLevel"": 0,
      ""sourceVariant"": ""10002_trtrsl"",
      ""statsLevel"": 0,
      ""displayNameZhHans"": ""训练装置"",
      ""skillDescriptionZhHans"": """",
      ""stats"": {
        ""combat"": {
          ""maxHitPoints"": 100,
          ""attack"": 0,
          ""defense"": 0,
          ""magicResistance"": 0
        },
        ""shared"": {
          ""moveSpeedMetresPerSecond"": 0,
          ""attackIntervalSeconds"": 0,
          ""lifeDeduct"": 0
        }
      },
      ""model"": {
        ""resourceKey"": ""trtrsl"",
        ""skeletonDataResourceName"": ""enemy_10002_trtrsl_SkeletonData"",
        ""profilePictureResourceName"": ""UIImage_10002_trtrsl"",
        ""animations"": [
          { ""key"": ""idle"", ""name"": ""Idle"" },
          { ""key"": ""move"", ""name"": ""Move"" },
          { ""key"": ""death"", ""name"": ""Death"" }
        ]
      },
      ""innateAbilityIds"": []
    }
  ]
}";

        [TestCase(0, "猎狗", 820, 190, "gopro")]
        [TestCase(1, "猎狗", 820, 190, "gopro")]
        [TestCase(2, "猎狗pro", 1700, 260, "gopro_2")]
        [TestCase(3, "狂暴的猎狗pro", 3000, 370, "gopro_3")]
        public void Resolve_AppliesNearestLowerAtomicInheritance(
            int eliteLevel,
            string expectedName,
            int expectedHp,
            int expectedAttack,
            string expectedResourceKey)
        {
            var resolved = Resolve(ValidThreeVariantFixture, eliteLevel, "1000_gopro.json");
            Assert.That(Field<string>(resolved, "displayNameZhHans"), Is.EqualTo(expectedName));
            Assert.That(Field<int>(resolved, "maxHitPoints"), Is.EqualTo(expectedHp));
            Assert.That(Field<int>(resolved, "attack"), Is.EqualTo(expectedAttack));
            Assert.That(Field<float>(resolved, "moveSpeedMetresPerSecond"), Is.EqualTo(1.9f));
            Assert.That(Field<string>(resolved, "resourceKey"), Is.EqualTo(expectedResourceKey));
        }

        [Test]
        public void Resolve_InheritsTextAndAbilitiesButHonoursExplicitEmptyArray()
        {
            var eliteTwo = Resolve(InheritanceFixture, 2, "9000_fixture.json");
            CollectionAssert.AreEqual(
                new[] { "ABILITY_A" },
                Field<IList>(eliteTwo, "innateAbilityIds").Cast<string>());

            var eliteThree = Resolve(InheritanceFixture, 3, "9000_fixture.json");
            Assert.That(Field<IList>(eliteThree, "innateAbilityIds"), Is.Empty);
        }

        [TestCase("unit-elite-variants-v1", "UNIT_ELITE_VARIANT_SCHEMA_INVALID")]
        [TestCase("partial-common", "UNIT_ELITE_VARIANT_COMMON_SHAPE_INVALID")]
        [TestCase("missing-elite-zero", "UNIT_ELITE_VARIANT_REQUIRED_MISSING")]
        [TestCase("duplicate-level", "UNIT_ELITE_VARIANT_LEVEL_DUPLICATE")]
        [TestCase("partial-combat", "UNIT_ELITE_VARIANT_COMBAT_SHAPE_INVALID")]
        [TestCase("partial-shared", "UNIT_ELITE_VARIANT_SHARED_SHAPE_INVALID")]
        [TestCase("partial-model", "UNIT_ELITE_VARIANT_MODEL_SHAPE_INVALID")]
        [TestCase("duplicate-animation-key", "UNIT_ELITE_VARIANT_ANIMATION_KEY_DUPLICATE")]
        [TestCase("missing-idle", "UNIT_ELITE_VARIANT_ANIMATION_REQUIRED_MISSING")]
        [TestCase("timed-animation-without-duration", "UNIT_ELITE_VARIANT_ANIMATION_DURATION_INVALID")]
        [TestCase("legacy-hit-field", "UNIT_ELITE_VARIANT_LEGACY_FIELD_FORBIDDEN")]
        [TestCase("filename-mismatch", "UNIT_ELITE_VARIANT_FILE_NAME_INVALID")]
        public void Resolve_RejectsInvalidV2Shape(string fixture, string expectedCode)
        {
            var exception = Assert.Throws<TargetInvocationException>(
                () => Resolve(InvalidFixture(fixture), 0, "9000_fixture.json"));
            StringAssert.Contains(expectedCode, exception.InnerException.Message);
        }

        [Test]
        public void Resolve_RejectsLegacyDefaultAnimationBinding()
        {
            var invalid = NormalizeLineEndings(ValidStationaryNonAttackerFixture).Replace(
                "{ \"key\": \"death\", \"name\": \"Death\" }",
                "{ \"key\": \"Default\", \"name\": \"Default\", \"durationSeconds\": 0.1 },\n"
                + "          { \"key\": \"death\", \"name\": \"Death\" }");

            var exception = Assert.Throws<TargetInvocationException>(
                () => Resolve(invalid, 0, "10002_trtrsl.json"));
            StringAssert.Contains(
                "UNIT_ELITE_VARIANT_LEGACY_FIELD_FORBIDDEN",
                exception.InnerException.Message);
            StringAssert.Contains("field=Default", exception.InnerException.Message);
        }

        [TestCase("stats-level-null")]
        [TestCase("defense-string")]
        [TestCase("display-name-null")]
        [TestCase("skill-description-null")]
        [TestCase("non-ascii-number")]
        public void Resolve_RejectsWrongJsonValueKinds(string fixture)
        {
            var exception = Assert.Throws<TargetInvocationException>(
                () => Resolve(WrongValueKindFixture(fixture), 2, "9000_fixture.json"));
            StringAssert.Contains(
                "UNIT_ELITE_VARIANT_JSON_INVALID",
                exception.InnerException.Message);
        }

        [Test]
        public void Resolve_RejectsSourceVariantThatDivergesFromInheritedModel()
        {
            var divergent = NormalizeLineEndings(InheritanceFixture).Replace(
                "\"sourceVariant\": \"9000_fixture\",\n      \"statsLevel\": 0\n    },",
                "\"sourceVariant\": \"9000_fixture_2\",\n      \"statsLevel\": 0\n    },");

            var exception = Assert.Throws<TargetInvocationException>(
                () => Resolve(divergent, 2, "9000_fixture.json"));
            StringAssert.Contains(
                "UNIT_ELITE_VARIANT_MODEL_CANONICAL_NAME_INVALID",
                exception.InnerException.Message);
        }

        [TestCase("trailing-token")]
        [TestCase("duplicate-property")]
        [TestCase("unterminated-string")]
        [TestCase("unclosed-object")]
        public void Resolve_RejectsMalformedJson(string fixture)
        {
            var malformed = MalformedFixture(fixture);
            var exception = Assert.Throws<TargetInvocationException>(
                () => Resolve(malformed, 0, "9000_fixture.json"));
            StringAssert.Contains(
                "UNIT_ELITE_VARIANT_JSON_INVALID",
                exception.InnerException.Message);
        }

        [Test]
        public void Resolve_ReturnsIndependentMutableCollectionsAndAnimationObjects()
        {
            var first = Resolve(InheritanceFixture, 0, "9000_fixture.json");
            var second = Resolve(InheritanceFixture, 0, "9000_fixture.json");
            var firstAbilities = Field<IList>(first, "innateAbilityIds");
            var secondAbilities = Field<IList>(second, "innateAbilityIds");
            var firstAnimations = Field<IList>(first, "animations");
            var secondAnimations = Field<IList>(second, "animations");

            Assert.That(firstAbilities, Is.Not.SameAs(secondAbilities));
            Assert.That(firstAnimations, Is.Not.SameAs(secondAnimations));
            Assert.That(firstAnimations[0], Is.Not.SameAs(secondAnimations[0]));

            firstAbilities[0] = "MUTATED";
            SetField(firstAnimations[0], "name", "Mutated");

            Assert.That(secondAbilities[0], Is.EqualTo("ABILITY_A"));
            Assert.That(
                Field<string>(secondAnimations[0], "name"),
                Is.EqualTo("Idle"));
        }

        [Test]
        public void Resolve_AllowsStationaryNonAttackerWithoutAttackAnimation()
        {
            var resolved = Resolve(
                ValidStationaryNonAttackerFixture,
                0,
                "10002_trtrsl.json");
            Assert.That(Field<int>(resolved, "attackMethod"), Is.Zero);
            Assert.That(Field<int>(resolved, "attack"), Is.Zero);
            Assert.That(Field<float>(resolved, "attackIntervalSeconds"), Is.Zero);
            Assert.That(Field<bool>(resolved, "canBlock"), Is.False);
            Assert.That(Field<int>(resolved, "blockCapacity"), Is.Zero);
            Assert.That(Field<string>(resolved, "damageType"), Is.EqualTo("None"));
        }

        [Test]
        public void LoadDirectory_RejectsDuplicateTypeIdsWithoutReturningPartialResults()
        {
            var duplicateDirectory = Path.GetFullPath(
                Path.Combine(Application.dataPath, "../Temp/UnitEliteVariantsV2/DuplicateTypeIds-"
                    + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(duplicateDirectory);
            var firstPath = Path.Combine(duplicateDirectory, "9000_fixture.json");
            var secondPath = Path.Combine(duplicateDirectory, "9000_other.json");
            try
            {
                File.WriteAllText(firstPath, InheritanceFixture);
                File.WriteAllText(secondPath, AlternateValidDocument());

                var exception = Assert.Throws<TargetInvocationException>(
                    () => LoadDirectory(duplicateDirectory));
                StringAssert.Contains(
                    "UNIT_ELITE_VARIANT_TYPEID_DUPLICATE",
                    exception.InnerException.Message);
            }
            finally
            {
                if (File.Exists(firstPath))
                {
                    File.Delete(firstPath);
                }

                if (File.Exists(secondPath))
                {
                    File.Delete(secondPath);
                }

                if (Directory.Exists(duplicateDirectory))
                {
                    Directory.Delete(duplicateDirectory, false);
                }
            }
        }

        private static string InvalidFixture(string fixture)
        {
            switch (fixture)
            {
                case "unit-elite-variants-v1":
                    return InheritanceFixture.Replace(
                        "unit-elite-variants-v2",
                        "unit-elite-variants-v1");
                case "partial-common":
                    return InheritanceFixture.Replace("    \"tauntLevel\": 0,\r\n", string.Empty)
                        .Replace("    \"tauntLevel\": 0,\n", string.Empty);
                case "missing-elite-zero":
                    return InheritanceFixture.Replace(
                        "\"minEliteLevel\": 0",
                        "\"minEliteLevel\": 1");
                case "duplicate-level":
                    return InheritanceFixture.Replace(
                        "\"minEliteLevel\": 2",
                        "\"minEliteLevel\": 0");
                case "partial-combat":
                    return InheritanceFixture.Replace(
                        ",\r\n          \"magicResistance\": 0",
                        string.Empty).Replace(
                        ",\n          \"magicResistance\": 0",
                        string.Empty);
                case "partial-shared":
                    return InheritanceFixture.Replace(
                        ",\r\n          \"lifeDeduct\": 1",
                        string.Empty).Replace(
                        ",\n          \"lifeDeduct\": 1",
                        string.Empty);
                case "partial-model":
                    return InheritanceFixture.Replace(
                        "        \"profilePictureResourceName\": \"UIImage_9000_fixture\",\r\n",
                        string.Empty).Replace(
                        "        \"profilePictureResourceName\": \"UIImage_9000_fixture\",\n",
                        string.Empty);
                case "duplicate-animation-key":
                    return InheritanceFixture.Replace(
                        "{ \"key\": \"move\", \"name\": \"Move\" }",
                        "{ \"key\": \"idle\", \"name\": \"Move\" }");
                case "missing-idle":
                    return InheritanceFixture.Replace(
                        "{ \"key\": \"idle\", \"name\": \"Idle\" }",
                        "{ \"key\": \"stand\", \"name\": \"Idle\" }");
                case "timed-animation-without-duration":
                    return InheritanceFixture.Replace(
                        "{ \"key\": \"attack\", \"name\": \"Attack\", \"durationSeconds\": 0.5 }",
                        "{ \"key\": \"attack\", \"name\": \"Attack\" }");
                case "legacy-hit-field":
                    return InheritanceFixture.Replace(
                        "\"profilePictureResourceName\": \"UIImage_9000_fixture\",",
                        "\"profilePictureResourceName\": \"UIImage_9000_fixture\","
                        + "\n        \"hitAnimation\": \"Hit\",");
                case "filename-mismatch":
                    return AlternateValidDocument();
                default:
                    throw new ArgumentOutOfRangeException(nameof(fixture), fixture, null);
            }
        }

        private static string AlternateValidDocument()
        {
            return InheritanceFixture
                .Replace("9000_fixture", "9000_other")
                .Replace("\"resourceKey\": \"fixture\"", "\"resourceKey\": \"other\"");
        }

        private static string WrongValueKindFixture(string fixture)
        {
            var normalized = NormalizeLineEndings(InheritanceFixture);
            switch (fixture)
            {
                case "stats-level-null":
                    return ReplaceFirst(
                        normalized,
                        "\"statsLevel\": 0",
                        "\"statsLevel\": null");
                case "defense-string":
                    return ReplaceFirst(
                        normalized,
                        "\"defense\": 0",
                        "\"defense\": \"0\"");
                case "display-name-null":
                    return normalized.Replace(
                        "\"sourceVariant\": \"9000_fixture\",\n      \"statsLevel\": 0\n    },",
                        "\"sourceVariant\": \"9000_fixture\",\n"
                        + "      \"statsLevel\": 0,\n"
                        + "      \"displayNameZhHans\": null\n    },");
                case "skill-description-null":
                    return normalized.Replace(
                        "\"sourceVariant\": \"9000_fixture\",\n      \"statsLevel\": 0\n    },",
                        "\"sourceVariant\": \"9000_fixture\",\n"
                        + "      \"statsLevel\": 0,\n"
                        + "      \"skillDescriptionZhHans\": null\n    },");
                case "non-ascii-number":
                    return normalized.Replace(
                        "\"tauntLevel\": 0",
                        "\"tauntLevel\": 1١");
                default:
                    throw new ArgumentOutOfRangeException(nameof(fixture), fixture, null);
            }
        }

        private static string MalformedFixture(string fixture)
        {
            var normalized = NormalizeLineEndings(InheritanceFixture);
            switch (fixture)
            {
                case "trailing-token":
                    return normalized + " true";
                case "duplicate-property":
                    return normalized.Replace(
                        "\"schemaVersion\": \"unit-elite-variants-v2\",",
                        "\"schemaVersion\": \"unit-elite-variants-v2\",\n"
                        + "  \"schemaVersion\": \"unit-elite-variants-v2\",");
                case "unterminated-string":
                    return normalized.Replace(
                        "\"displayNameZhHans\": \"fixture\",",
                        "\"displayNameZhHans\": \"fixture,");
                case "unclosed-object":
                    return normalized.Substring(0, normalized.LastIndexOf(
                        '}'));
                default:
                    throw new ArgumentOutOfRangeException(nameof(fixture), fixture, null);
            }
        }

        private static string ReplaceFirst(string source, string oldValue, string newValue)
        {
            var index = source.IndexOf(oldValue, StringComparison.Ordinal);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "Fixture token must exist.");
            return source.Substring(0, index)
                   + newValue
                   + source.Substring(index + oldValue.Length);
        }

        private static string NormalizeLineEndings(string value)
        {
            return value.Replace("\r\n", "\n");
        }

        private static object Resolve(string json, int eliteLevel, string context)
        {
            var resolver = Type.GetType("UnitEliteVariantResolver, Assembly-CSharp");
            Assert.That(resolver, Is.Not.Null, "Runtime resolver type must exist.");
            var method = resolver.GetMethod(
                "Resolve",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(int), typeof(string) },
                null);
            Assert.That(method, Is.Not.Null, "Resolve(string, int, string) must exist.");
            return method.Invoke(null, new object[] { json, eliteLevel, context });
        }

        private static object LoadDirectory(string directory)
        {
            var resolver = Type.GetType("UnitEliteVariantResolver, Assembly-CSharp");
            Assert.That(resolver, Is.Not.Null, "Runtime resolver type must exist.");
            var method = resolver.GetMethod(
                "LoadDirectory",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(string) },
                null);
            Assert.That(method, Is.Not.Null, "LoadDirectory(string) must exist.");
            return method.Invoke(null, new object[] { directory });
        }

        private static T Field<T>(object source, string name)
        {
            var field = source.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, "Missing resolved field: " + name);
            return (T)field.GetValue(source);
        }

        private static void SetField(object source, string name, object value)
        {
            var field = source.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, "Missing resolved field: " + name);
            field.SetValue(source, value);
        }
    }
}
