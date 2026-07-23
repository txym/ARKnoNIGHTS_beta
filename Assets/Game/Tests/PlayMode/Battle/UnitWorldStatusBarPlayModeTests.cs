using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ArknoNights.Battle.Tests
{
    public sealed class UnitWorldStatusBarPlayModeTests
    {
        [UnityTest]
        public IEnumerator DefaultUnitStatusBar_UsesPrefabRenderersAndKeepsLeftRightSemanticsWhenFacingChanges()
        {
            var prefab = Resources.Load<GameObject>("Prefabs/DefaultUnit");
            Assert.IsNotNull(prefab);
            var instance = Object.Instantiate(prefab);
            instance.transform.position = Vector3.zero;
            var bar = instance.GetComponent("UnitWorldStatusBar");
            Assert.IsNotNull(bar);
            var barRoot = GetProperty<Transform>(bar, "BarRoot");
            var background = GetProperty<Renderer>(bar, "BackgroundRenderer");
            var capacity = GetProperty<Transform>(bar, "MaxHitPointsCapacity");
            var current = GetProperty<Renderer>(bar, "CurrentHitPointsRenderer");
            var shield = GetProperty<Renderer>(bar, "ShieldRenderer");
            Assert.IsNotNull(background);
            Assert.IsNotNull(current);
            Assert.IsNotNull(shield);
            Assert.IsFalse(barRoot.gameObject.activeSelf);

            bar.GetType().GetMethod("SetState").Invoke(bar, new object[] { "smoke-unit", false, 100, 50, 50 });
            yield return null;

            Assert.IsTrue(barRoot.gameObject.activeSelf);
            Assert.That(GetLayoutWidth(bar, "MaxHitPointsWidth"), Is.EqualTo(60f));
            Assert.That(GetLayoutWidth(bar, "CurrentHitPointsWidth"), Is.EqualTo(30f));
            Assert.That(GetLayoutWidth(bar, "ShieldWidth"), Is.EqualTo(30f));
            Assert.That(capacity.localPosition.x, Is.EqualTo(-15f).Within(0.001f));
            Assert.That(capacity.localScale.x, Is.EqualTo(60f).Within(0.001f));
            Assert.That(current.bounds.size.x, Is.EqualTo(30f).Within(0.01f));
            Assert.That(shield.bounds.size.x, Is.EqualTo(30f).Within(0.01f));
            Assert.That(current.transform.position.x, Is.EqualTo(-30f).Within(0.01f));
            Assert.That(shield.transform.position.x, Is.EqualTo(30f).Within(0.01f));

            instance.transform.rotation = Quaternion.Euler(60f, 180f, 0f);
            yield return null;
            Assert.That(Quaternion.Angle(barRoot.rotation, instance.transform.rotation), Is.LessThan(0.01f));
            Assert.That(current.transform.position.x, Is.EqualTo(-30f).Within(0.01f));
            Assert.That(shield.transform.position.x, Is.EqualTo(30f).Within(0.01f));

            bar.GetType().GetMethod("SetState").Invoke(bar, new object[] { "smoke-unit", true, 100, 100, 0 });
            Assert.IsFalse(barRoot.gameObject.activeSelf);
            Object.Destroy(instance);
            yield return null;
            Assert.IsNull(GameObject.Find("DefaultUnit(Clone)"));
        }

        private static T GetProperty<T>(Component component, string name) where T : class => component.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public).GetValue(component, null) as T;

        private static float GetLayoutWidth(Component component, string name)
        {
            var layout = component.GetType().GetProperty("Layout", BindingFlags.Instance | BindingFlags.Public).GetValue(component, null);
            return (float)layout.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public).GetValue(layout, null);
        }
    }
}
