using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ArknoNights.Battle.Tests
{
    public sealed class BondsUnitAnimationAuditEditModeTests
    {
        private const string CharacterAssetRoot = "Assets/Resources/Characters";
        private const string ExpectedSchemaVersion = "bonds-unit-animation-audit-v1";

        [Test]
        public void BuildDocument_EnumeratesAllCanonicalBondsVariantsWithKnownSampleDurations()
        {
            var document = BuildDocument();

            Assert.That(GetField<string>(document, "schemaVersion"), Is.EqualTo(ExpectedSchemaVersion));
            Assert.That(GetField<int>(document, "typeIdCount"), Is.EqualTo(99));
            Assert.That(GetField<int>(document, "variantCount"), Is.EqualTo(182));

            var variants = GetArrayField(document, "variants");
            Assert.That(variants.Length, Is.EqualTo(182));
            Assert.That(
                variants
                    .Cast<object>()
                    .Select(variant => GetField<int>(variant, "typeId"))
                    .Distinct()
                    .Count(),
                Is.EqualTo(99));

            var unitKeys = variants
                .Cast<object>()
                .Select(variant => GetField<string>(variant, "unitKey"))
                .ToArray();
            Assert.That(unitKeys, Is.Ordered.Using<string>(StringComparer.Ordinal));
            Assert.That(unitKeys.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(182));

            foreach (var variant in variants)
            {
                var unitKey = GetField<string>(variant, "unitKey");
                Assert.That(
                    GetField<string>(variant, "skeletonDataResourcePath"),
                    Is.EqualTo("Characters/" + unitKey + "/enemy_" + unitKey + "_SkeletonData"));
                Assert.That(GetArrayField(variant, "animations").Length, Is.GreaterThan(0), unitKey);
            }

            Assert.That(
                GetField<string>(FindVariant(variants, "1322_wdgyht"), "sourceUnitKey"),
                Is.EqualTo("1322_wdgyht_2"));
            Assert.That(
                GetField<string>(FindVariant(variants, "1322_wdgyht_2"), "sourceUnitKey"),
                Is.EqualTo("1322_wdgyht"));

            AssertAnimationDuration(FindVariant(variants, "1014_rogue"), "Attack", 1.1f);
            AssertAnimationDuration(FindVariant(variants, "5503_arcslma"), "Attack", 2.666667f);
        }

        [Test]
        public void Serialize_RoundTripsAllDurationsAndStableOrdering()
        {
            var document = BuildDocument();
            var independentlyBuiltDocument = BuildDocument();
            SetField(document, "generatedAtUtc", "2026-07-28T00:00:00.0000000Z");
            SetField(
                independentlyBuiltDocument,
                "generatedAtUtc",
                "2026-07-28T00:00:00.0000000Z");

            var first = Serialize(document);
            var second = Serialize(independentlyBuiltDocument);
            Assert.That(second, Is.EqualTo(first));

            var roundTripped = JsonUtility.FromJson(first, document.GetType());
            Assert.That(roundTripped, Is.Not.Null);

            var expectedVariants = GetArrayField(document, "variants");
            var actualVariants = GetArrayField(roundTripped, "variants");
            Assert.That(actualVariants.Length, Is.EqualTo(expectedVariants.Length));

            for (var variantIndex = 0; variantIndex < expectedVariants.Length; variantIndex++)
            {
                var expectedVariant = expectedVariants.GetValue(variantIndex);
                var actualVariant = actualVariants.GetValue(variantIndex);
                Assert.That(
                    GetField<string>(actualVariant, "unitKey"),
                    Is.EqualTo(GetField<string>(expectedVariant, "unitKey")));

                var expectedAnimations = GetArrayField(expectedVariant, "animations");
                var actualAnimations = GetArrayField(actualVariant, "animations");
                Assert.That(actualAnimations.Length, Is.EqualTo(expectedAnimations.Length));

                for (var animationIndex = 0; animationIndex < expectedAnimations.Length; animationIndex++)
                {
                    var expectedAnimation = expectedAnimations.GetValue(animationIndex);
                    var actualAnimation = actualAnimations.GetValue(animationIndex);
                    Assert.That(
                        GetField<string>(actualAnimation, "name"),
                        Is.EqualTo(GetField<string>(expectedAnimation, "name")));
                    Assert.That(
                        FloatBits(GetField<float>(actualAnimation, "durationSeconds")),
                        Is.EqualTo(FloatBits(GetField<float>(expectedAnimation, "durationSeconds"))));
                }
            }
        }

        [Test]
        public void BuildDocument_DoesNotModifyImportedAssets()
        {
            var assetPaths = AssetDatabase
                .FindAssets("t:SkeletonDataAsset", new[] { CharacterAssetRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.That(assetPaths.Length, Is.GreaterThanOrEqualTo(182));

            var hashesBefore = assetPaths.ToDictionary(
                path => path,
                AssetDatabase.GetAssetDependencyHash,
                StringComparer.Ordinal);

            BuildDocument();

            foreach (var assetPath in assetPaths)
            {
                Assert.That(
                    AssetDatabase.GetAssetDependencyHash(assetPath),
                    Is.EqualTo(hashesBefore[assetPath]),
                    assetPath);
            }
        }

        [Test]
        public void BuildDocument_TokenizesLeadingAndTrailingStateSegments()
        {
            var variants = GetArrayField(BuildDocument(), "variants");

            var leadingStateTokens = GetField<string[]>(
                FindVariant(variants, "10126_rkbomb"),
                "caseFoldedTokenSummary");
            Assert.That(leadingStateTokens, Does.Contain("a"));
            Assert.That(leadingStateTokens, Does.Contain("b"));
            Assert.That(leadingStateTokens, Does.Contain("attack"));
            Assert.That(leadingStateTokens, Does.Contain("move"));

            var trailingStateTokens = GetField<string[]>(
                FindVariant(variants, "10001_trslim"),
                "caseFoldedTokenSummary");
            Assert.That(trailingStateTokens, Does.Contain("a"));
            Assert.That(trailingStateTokens, Does.Contain("b"));
        }

        [Test]
        public void ValidateVariantTypeIdCoverage_RejectsMissingExpectedTypeId()
        {
            var auditType = Type.GetType("BondsUnitAnimationAudit, Assembly-CSharp-Editor");
            Assert.That(auditType, Is.Not.Null);
            var method = auditType.GetMethod(
                "ValidateVariantTypeIdCoverage",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "TypeId coverage validation must exist.");

            var exception = Assert.Throws<TargetInvocationException>(() =>
                method.Invoke(
                    null,
                    new object[]
                    {
                        new HashSet<int> { 1000, 1001 },
                        new[] { "1000_alpha", "1000_beta" }
                    }));
            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(
                exception.InnerException.Message,
                Does.StartWith("BONDS_ANIMATION_AUDIT_TYPE_ID_COVERAGE"));
        }

        private static object BuildDocument()
        {
            var auditType = Type.GetType("BondsUnitAnimationAudit, Assembly-CSharp-Editor");
            Assert.That(auditType, Is.Not.Null, "Editor animation audit type must exist.");
            var method = auditType.GetMethod(
                "BuildDocument",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "BuildDocument must exist.");
            return method.Invoke(null, null);
        }

        private static string Serialize(object document)
        {
            var method = document
                .GetType()
                .Assembly
                .GetType("BondsUnitAnimationAudit")
                ?.GetMethod("Serialize", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Serialize must exist.");
            return (string)method.Invoke(null, new[] { document });
        }

        private static object FindVariant(Array variants, string unitKey)
        {
            var matches = variants
                .Cast<object>()
                .Where(variant => GetField<string>(variant, "unitKey") == unitKey)
                .ToArray();
            Assert.That(matches.Length, Is.EqualTo(1), unitKey);
            return matches[0];
        }

        private static void AssertAnimationDuration(
            object variant,
            string animationName,
            float expectedDuration)
        {
            var unitKey = GetField<string>(variant, "unitKey");
            var matches = GetArrayField(variant, "animations")
                .Cast<object>()
                .Where(animation => GetField<string>(animation, "name") == animationName)
                .ToArray();
            Assert.That(matches.Length, Is.EqualTo(1), unitKey + "/" + animationName);
            Assert.That(
                GetField<float>(matches[0], "durationSeconds"),
                Is.EqualTo(expectedDuration).Within(0.000001f),
                unitKey + "/" + animationName);
        }

        private static T GetField<T>(object instance, string fieldName)
        {
            var field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, instance.GetType().Name + "." + fieldName);
            return (T)field.GetValue(instance);
        }

        private static Array GetArrayField(object instance, string fieldName)
        {
            return GetField<Array>(instance, fieldName);
        }

        private static void SetField(object instance, string fieldName, object value)
        {
            var field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, instance.GetType().Name + "." + fieldName);
            field.SetValue(instance, value);
        }

        private static int FloatBits(float value)
        {
            return BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        }
    }
}
