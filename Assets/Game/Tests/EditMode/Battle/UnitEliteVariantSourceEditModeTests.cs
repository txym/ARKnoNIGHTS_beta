using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ArknoNights.Battle.Tests
{
    public sealed class UnitEliteVariantSourceEditModeTests
    {
        private static string SourcePath =>
            Path.Combine(Application.dataPath, "GameData/Units/Json/1000_gopro.json");

        private static string VariantPath =>
            Path.Combine(Application.dataPath, "GameData/Units/EliteVariants/Json/1000_gopro.json");

        [TestCase(0, "猎狗", 820, 190, "gopro", "enemy_1000_gopro_SkeletonData")]
        [TestCase(1, "猎狗", 820, 190, "gopro", "enemy_1000_gopro_SkeletonData")]
        [TestCase(2, "猎狗pro", 1700, 260, "gopro_2", "enemy_1000_gopro_2_SkeletonData")]
        [TestCase(3, "狂暴的猎狗pro", 3000, 370, "gopro_3", "enemy_1000_gopro_3_SkeletonData")]
        public void Resolve_AppliesSparseVariantsAndLowerLevelInheritance(
            int eliteLevel,
            string expectedName,
            int expectedHitPoints,
            int expectedAttack,
            string expectedResourceKey,
            string expectedSkeleton)
        {
            var resolved = Resolve(
                File.ReadAllText(SourcePath),
                File.ReadAllText(VariantPath),
                eliteLevel);

            Assert.That(Field<string>(resolved, "displayNameZhHans"), Is.EqualTo(expectedName));
            Assert.That(Field<int>(resolved, "maxHitPoints"), Is.EqualTo(expectedHitPoints));
            Assert.That(Field<int>(resolved, "attack"), Is.EqualTo(expectedAttack));
            Assert.That(Field<int>(resolved, "defense"), Is.EqualTo(0));
            Assert.That(Field<int>(resolved, "magicResistance"), Is.EqualTo(20));
            Assert.That(Field<float>(resolved, "moveSpeedMetresPerSecond"), Is.EqualTo(1.9f));
            Assert.That(Field<float>(resolved, "attackIntervalSeconds"), Is.EqualTo(1.4f));
            Assert.That(Field<int>(resolved, "lifeDeduct"), Is.EqualTo(1));
            Assert.That(Field<string>(resolved, "resourceKey"), Is.EqualTo(expectedResourceKey));
            Assert.That(Field<string>(resolved, "skeletonDataResourceName"), Is.EqualTo(expectedSkeleton));
        }

        [Test]
        public void Resolve_RejectsPartialModelBlocks()
        {
            var invalid = File.ReadAllText(VariantPath).Replace(
                "\"profilePictureResourceName\": \"UIImage_1000_gopro_2\"",
                "\"profilePictureResourceName\": \"\"");

            var exception = Assert.Throws<TargetInvocationException>(() =>
                Resolve(File.ReadAllText(SourcePath), invalid, 2));

            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
            StringAssert.Contains("UNIT_ELITE_VARIANT_MODEL_INCOMPLETE", exception.InnerException.Message);
        }

        private static object Resolve(string sourceJson, string variantsJson, int eliteLevel)
        {
            var resolver = Type.GetType("UnitEliteVariantResolver, Assembly-CSharp-Editor");
            Assert.That(resolver, Is.Not.Null, "Editor resolver type must exist.");
            var method = resolver.GetMethod("Resolve", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Resolve method must exist.");
            return method.Invoke(null, new object[] { sourceJson, variantsJson, eliteLevel, "test" });
        }

        private static T Field<T>(object source, string name)
        {
            var field = source.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, "Missing resolved field: " + name);
            return (T)field.GetValue(source);
        }
    }
}
