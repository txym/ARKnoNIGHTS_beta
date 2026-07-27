using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace ArknoNights.Lobby.Tests
{
    public sealed class LanLobbyRoomLayoutEditModeTests
    {
        [Test]
        public void RoomLayout_TypeIsAvailableToLobbyConsumers()
        {
            var roomLayoutType = typeof(global::LanLobbyLayout).Assembly.GetType("LanLobbyRoomLayout");

            Assert.That(roomLayoutType, Is.Not.Null);
        }

        [Test]
        public void RoomLayout_ProvidesFourMeasuredSlotRoots()
        {
            var layout = CreateLayout(1920, 1080);
            var slots = ReadSlots(layout);

            Assert.That(slots.Count, Is.EqualTo(4));
            AssertRect(ReadRect(slots[0], "Root"), 199.5f, 237.75f, 363.75f, 664.5f);
            AssertRect(ReadRect(slots[1], "Root"), 588.75f, 237.75f, 363.75f, 664.5f);
            AssertRect(ReadRect(slots[2], "Root"), 976.5f, 237.75f, 363.75f, 664.5f);
            AssertRect(ReadRect(slots[3], "Root"), 1365f, 237.75f, 363.75f, 664.5f);
            Assert.That(ReadRect(slots[1], "Root").Left - ReadRect(slots[0], "Root").Left, Is.EqualTo(389.25f).Within(0.01f));
            Assert.That(ReadRect(slots[2], "Root").Left - ReadRect(slots[1], "Root").Left, Is.EqualTo(387.75f).Within(0.01f));
            Assert.That(ReadRect(slots[3], "Root").Left - ReadRect(slots[2], "Root").Left, Is.EqualTo(388.5f).Within(0.01f));
        }

        [Test]
        public void RoomLayout_KeepsCardBodyAndLowerDecorationContainedWithSeamOverlap()
        {
            var slots = ReadSlots(CreateLayout(1920, 1080));

            foreach (var slot in slots)
            {
                var root = ReadRect(slot, "Root");
                var cardBody = ReadRect(slot, "CardBody");
                var lowerDecoration = ReadRect(slot, "LowerDecoration");

                Assert.That(cardBody.Left, Is.GreaterThanOrEqualTo(0f));
                Assert.That(cardBody.Bottom, Is.GreaterThanOrEqualTo(0f));
                Assert.That(cardBody.Right, Is.LessThanOrEqualTo(root.Width));
                Assert.That(cardBody.Top, Is.LessThanOrEqualTo(root.Height));
                Assert.That(lowerDecoration.Left, Is.GreaterThanOrEqualTo(0f));
                Assert.That(lowerDecoration.Bottom, Is.GreaterThanOrEqualTo(0f));
                Assert.That(lowerDecoration.Right, Is.LessThanOrEqualTo(root.Width));
                Assert.That(lowerDecoration.Top, Is.LessThanOrEqualTo(root.Height));
                Assert.That(lowerDecoration.Top - cardBody.Bottom, Is.EqualTo(0.75f).Within(0.01f));
            }
        }

        [Test]
        public void RoomLayout_PlacesMeasuredActionsAndSeparatesLatencyFromLeave()
        {
            var layout = CreateLayout(1920, 1080);

            AssertRect(ReadRect(layout, "PrimaryAction"), 1487.25f, 42.75f, 432f, 94.5f);
            AssertRect(ReadRect(layout, "LeaveAction"), 43.5f, 997.5f, 118f, 52.5f);
            var leave = ReadRect(layout, "LeaveAction");
            var latency = ReadRect(layout, "Latency");
            Assert.That(leave.Right <= latency.Left || latency.Right <= leave.Left || leave.Top <= latency.Bottom || latency.Top <= leave.Bottom, Is.True);
        }

        [Test]
        public void RoomLayout_ScalesCanonicalGeometryUniformlyAtFourThirds()
        {
            var canonical = CreateLayout(1920, 1080);
            var scaled = CreateLayout(2560, 1440);

            AssertScaled(ReadRect(ReadSlots(canonical)[0], "Root"), ReadRect(ReadSlots(scaled)[0], "Root"), 4f / 3f);
            AssertScaled(ReadRect(canonical, "LeaveAction"), ReadRect(scaled, "LeaveAction"), 4f / 3f);
            AssertScaled(ReadRect(canonical, "Latency"), ReadRect(scaled, "Latency"), 4f / 3f);
            AssertScaled(ReadRect(canonical, "PrimaryAction"), ReadRect(scaled, "PrimaryAction"), 4f / 3f);
        }

        [Test]
        public void RoomLayout_LetterboxesNon16By9CanvasWithoutChangingInternalAspect()
        {
            var canonical = CreateLayout(1920, 1080);
            var letterboxed = CreateLayout(1920, 1200);
            var canonicalRoot = ReadRect(ReadSlots(canonical)[0], "Root");
            var letterboxedRoot = ReadRect(ReadSlots(letterboxed)[0], "Root");

            Assert.That(letterboxedRoot.Left, Is.EqualTo(canonicalRoot.Left).Within(0.01f));
            Assert.That(letterboxedRoot.Bottom, Is.EqualTo(canonicalRoot.Bottom + 60f).Within(0.01f));
            Assert.That(letterboxedRoot.Width, Is.EqualTo(canonicalRoot.Width).Within(0.01f));
            Assert.That(letterboxedRoot.Height, Is.EqualTo(canonicalRoot.Height).Within(0.01f));
            Assert.That(letterboxedRoot.Width / letterboxedRoot.Height, Is.EqualTo(canonicalRoot.Width / canonicalRoot.Height).Within(0.0001f));
        }

        private static object CreateLayout(int width, int height)
        {
            var roomLayoutType = typeof(global::LanLobbyLayout).Assembly.GetType("LanLobbyRoomLayout");
            Assert.That(roomLayoutType, Is.Not.Null, "LanLobbyRoomLayout must be public to lobby consumers.");

            var forSize = roomLayoutType.GetMethod("ForSize", BindingFlags.Public | BindingFlags.Static);
            Assert.That(forSize, Is.Not.Null, "LanLobbyRoomLayout must expose ForSize(int, int).");
            return forSize.Invoke(null, new object[] { width, height });
        }

        private static List<object> ReadSlots(object layout)
        {
            var slots = ReadProperty(layout, "Slots") as IEnumerable;
            Assert.That(slots, Is.Not.Null, "LanLobbyRoomLayout.Slots must be enumerable.");

            var values = new List<object>();
            foreach (var slot in slots)
            {
                values.Add(slot);
            }

            return values;
        }

        private static global::LanLobbyRect ReadRect(object instance, string propertyName)
        {
            var value = ReadProperty(instance, propertyName);
            Assert.That(value, Is.TypeOf<global::LanLobbyRect>(), propertyName + " must be a LanLobbyRect.");
            return (global::LanLobbyRect)value;
        }

        private static object ReadProperty(object instance, string propertyName)
        {
            Assert.That(instance, Is.Not.Null, propertyName + " owner must not be null.");
            var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            Assert.That(property, Is.Not.Null, instance.GetType().Name + " must expose " + propertyName + ".");
            return property.GetValue(instance, null);
        }

        private static void AssertRect(global::LanLobbyRect actual, float left, float bottom, float width, float height)
        {
            Assert.That(actual.Left, Is.EqualTo(left).Within(0.01f));
            Assert.That(actual.Bottom, Is.EqualTo(bottom).Within(0.01f));
            Assert.That(actual.Width, Is.EqualTo(width).Within(0.01f));
            Assert.That(actual.Height, Is.EqualTo(height).Within(0.01f));
        }

        private static void AssertScaled(global::LanLobbyRect canonical, global::LanLobbyRect scaled, float scale)
        {
            AssertRect(scaled, canonical.Left * scale, canonical.Bottom * scale, canonical.Width * scale, canonical.Height * scale);
        }
    }
}
