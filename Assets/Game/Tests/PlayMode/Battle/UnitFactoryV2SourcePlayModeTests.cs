using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ArknoNights.Battle.Tests
{
    public sealed class UnitFactoryV2SourcePlayModeTests
    {
        private static readonly BindingFlags InstanceFields =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [Test]
        public void SpawnAll_LoadsEliteZeroFromAuthoritativeV2Sources()
        {
            var parent = new GameObject("Task5_UnitFactoryV2Source_Parent");
            var spawned = new List<GameObject>();
            var templates = new List<UnityEngine.Object>();
            MethodInfo resetStatics = null;

            try
            {
                var factoryType = Type.GetType("UnitFactory, Assembly-CSharp");
                Assert.That(factoryType, Is.Not.Null);

                resetStatics = factoryType.GetMethod(
                    "ResetStatics",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(resetStatics, Is.Not.Null);
                resetStatics.Invoke(null, null);

                var spawnAll = factoryType.GetMethod(
                    "SpawnAll",
                    BindingFlags.Static | BindingFlags.Public);
                Assert.That(spawnAll, Is.Not.Null);

                var arguments = new object[] { parent.transform, true, null };
                var spawnedResult = spawnAll.Invoke(null, arguments) as IEnumerable;
                var idMap = arguments[2] as IDictionary;
                Assert.That(spawnedResult, Is.Not.Null);
                Assert.That(idMap, Is.Not.Null);

                spawned.AddRange(spawnedResult.Cast<GameObject>());
                Assert.That(spawned.Count, Is.EqualTo(3));
                Assert.That(
                    idMap.Keys.Cast<int>().OrderBy(id => id),
                    Is.EqualTo(new[] { 1000, 5503, 5504 }));

                AssertTemplate(factoryType, templates, 1000, "gopro", 820, 190, 1, 2, 1.9f, 0.7f);
                AssertTemplate(factoryType, templates, 5503, "arcslma", 18000, 1100, 6, 2, 0.2f, 2.0f);
                AssertTemplate(factoryType, templates, 5504, "arcslmi", 2500, 290, 3, 2, 1.9f, 0.75f);

                var type2 = Type.GetType("UnitSkelType2, Assembly-CSharp");
                Assert.That(type2, Is.Not.Null);
                Assert.That(GameObjectFor(idMap, 1000).GetComponent(type2), Is.Not.Null);
                Assert.That(GameObjectFor(idMap, 5503).GetComponent(type2), Is.Not.Null);
                Assert.That(GameObjectFor(idMap, 5504).GetComponent(type2), Is.Not.Null);
                Assert.That(
                    idMap.Values.Cast<GameObject>(),
                    Has.All.Matches<GameObject>(HasConfiguredSkeletonData));

                Assert.That(MoveName(GameObjectFor(idMap, 1000), type2), Is.EqualTo("Run_Loop"));
                Assert.That(MoveName(GameObjectFor(idMap, 5503), type2), Is.EqualTo("Move"));
                Assert.That(AttackName(GameObjectFor(idMap, 5504), type2), Is.EqualTo("Attack"));
            }
            finally
            {
                for (var index = spawned.Count - 1; index >= 0; index--)
                {
                    if (spawned[index] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(spawned[index]);
                    }
                }

                if (parent != null)
                {
                    UnityEngine.Object.DestroyImmediate(parent);
                }

                foreach (var template in templates)
                {
                    if (template != null)
                    {
                        UnityEngine.Object.DestroyImmediate(template);
                    }
                }

                resetStatics?.Invoke(null, null);
            }
        }

        [Test]
        public void LegacyFlatUnitJsonRoot_IsNotLoadable()
        {
            Assert.That(
                Type.GetType("UnitJson, Assembly-CSharp"),
                Is.Null,
                "The obsolete unit-source-v1 root DTO must be removed after the factory migrates to v2.");
        }

        private static void AssertTemplate(
            Type factoryType,
            ICollection<UnityEngine.Object> templates,
            int typeId,
            string resourceKey,
            int maxHitPoints,
            int attack,
            int rarity,
            int deploymentCost,
            float moveSpeed,
            float baseAttackInterval)
        {
            var getTemplate = factoryType.GetMethod(
                "GetUnitBasicValueSO",
                BindingFlags.Static | BindingFlags.Public);
            Assert.That(getTemplate, Is.Not.Null);

            var template = getTemplate.Invoke(null, new object[] { typeId }) as UnityEngine.Object;
            Assert.That(template, Is.Not.Null, "Missing legacy UnitTemplate for type " + typeId);
            templates.Add(template);

            Assert.That(Field<int>(template, "typeID"), Is.EqualTo(typeId));
            Assert.That(Field<string>(template, "uintName"), Is.EqualTo(resourceKey));
            Assert.That(Field<int>(template, "HP"), Is.EqualTo(maxHitPoints));
            Assert.That(Field<int>(template, "atk"), Is.EqualTo(attack));
            Assert.That(Field<int>(template, "Rarity"), Is.EqualTo(rarity));
            Assert.That(Field<int>(template, "cost"), Is.EqualTo(deploymentCost));
            Assert.That(Field<int>(template, "unitskeltype"), Is.EqualTo(2));
            Assert.That(Field<float>(template, "moveSpeed"), Is.EqualTo(moveSpeed).Within(0.0001f));
            Assert.That(
                Field<float>(template, "attackInterval"),
                Is.EqualTo(baseAttackInterval).Within(0.0001f));
        }

        private static GameObject GameObjectFor(IDictionary idMap, int typeId)
        {
            return idMap[typeId] as GameObject;
        }

        private static bool HasConfiguredSkeletonData(GameObject gameObject)
        {
            var skeleton = gameObject.GetComponent("SkeletonAnimation");
            return skeleton != null
                && Field<UnityEngine.Object>(skeleton, "skeletonDataAsset") != null;
        }

        private static string MoveName(GameObject gameObject, Type type2)
        {
            return Field<string>(gameObject.GetComponent(type2), "moveAnimationName");
        }

        private static string AttackName(GameObject gameObject, Type type2)
        {
            return Field<string>(gameObject.GetComponent(type2), "attackAnimationName");
        }

        private static T Field<T>(object instance, string name)
        {
            for (var type = instance.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, InstanceFields);
                if (field != null)
                {
                    return (T)field.GetValue(instance);
                }
            }

            Assert.Fail("Missing field " + name + " on " + instance.GetType().FullName);
            return default;
        }
    }
}
