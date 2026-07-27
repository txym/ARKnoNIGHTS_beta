using System.Collections.Generic;
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
            var layout = global::LanLobbyRoomLayout.ForSize(1920, 1080);

            Assert.That(layout.Slots.Count, Is.EqualTo(4));
            AssertRect(layout.Slots[0].Root, 199.5f, 237.75f, 363.75f, 664.5f);
            AssertRect(layout.Slots[1].Root, 588.75f, 237.75f, 363.75f, 664.5f);
            AssertRect(layout.Slots[2].Root, 976.5f, 237.75f, 363.75f, 664.5f);
            AssertRect(layout.Slots[3].Root, 1365f, 237.75f, 363.75f, 664.5f);
            Assert.That(layout.Slots[1].Root.Left - layout.Slots[0].Root.Left, Is.EqualTo(389.25f).Within(0.01f));
            Assert.That(layout.Slots[2].Root.Left - layout.Slots[1].Root.Left, Is.EqualTo(387.75f).Within(0.01f));
            Assert.That(layout.Slots[3].Root.Left - layout.Slots[2].Root.Left, Is.EqualTo(388.5f).Within(0.01f));
        }

        [Test]
        public void RoomLayout_KeepsCardBodyAndLowerDecorationContainedWithSeamOverlap()
        {
            var layout = global::LanLobbyRoomLayout.ForSize(1920, 1080);

            foreach (var slot in layout.Slots)
            {
                AssertContained(slot.CardBody, slot.Root);
                AssertContained(slot.LowerDecoration, slot.Root);
                Assert.That(slot.LowerDecoration.Top - slot.CardBody.Bottom, Is.EqualTo(0.75f).Within(0.01f));
            }
        }

        [Test]
        public void RoomLayout_FitsReadyContourToCardWidthWithItsSourceAspect()
        {
            var slot = global::LanLobbyRoomLayout.ForSize(1920, 1080).Slots[0];

            Assert.That(slot.StateOverlay.Left, Is.EqualTo(slot.CardBody.Left).Within(0.01f));
            Assert.That(slot.StateOverlay.Bottom, Is.EqualTo(slot.CardBody.Bottom).Within(0.01f));
            Assert.That(slot.StateOverlay.Width, Is.EqualTo(slot.CardBody.Width).Within(0.01f));
            Assert.That(slot.StateOverlay.Width / slot.StateOverlay.Height, Is.EqualTo(256f / 103f).Within(0.0001f));
            AssertContained(slot.StateOverlay, slot.Root);
        }

        [Test]
        public void RoomLayout_PreservesTopBarSourceAspect()
        {
            var slot = global::LanLobbyRoomLayout.ForSize(1920, 1080).Slots[0];

            Assert.That(slot.TopBar.Width / slot.TopBar.Height, Is.EqualTo(234f / 31f).Within(0.0001f));
        }

        [Test]
        public void RoomLayout_PreservesEmptyInviteSourceAspect()
        {
            var slot = global::LanLobbyRoomLayout.ForSize(1920, 1080).Slots[0];

            Assert.That(slot.EmptyInvite.Width / slot.EmptyInvite.Height, Is.EqualTo(275f / 104f).Within(0.0001f));
        }

        [Test]
        public void RoomLayout_UsesOnlyNativeOrDeferredGeometryForUnmeasuredReadyChildren()
        {
            var slot = global::LanLobbyRoomLayout.ForSize(1920, 1080).Slots[0];

            Assert.That(slot.ReadyIcon.Width, Is.EqualTo(34f).Within(0.01f));
            Assert.That(slot.ReadyIcon.Height, Is.EqualTo(34f).Within(0.01f));
            Assert.That(slot.ReadyIcon.Left, Is.EqualTo(slot.CardBody.Left + (slot.CardBody.Width - slot.ReadyIcon.Width) * .5f).Within(0.01f));
            Assert.That(slot.ReadyIcon.Bottom, Is.EqualTo(slot.CardBody.Bottom + (slot.CardBody.Height - slot.ReadyIcon.Height) * .5f).Within(0.01f));
            AssertRect(slot.ReadyLabel, slot.ReadyIcon.Left, slot.ReadyIcon.Bottom, 0f, 0f);
            AssertRect(slot.CreatorTag, slot.TopBar.Left, slot.TopBar.Top - 26f, 72f, 26f);
        }

        [Test]
        public void RoomLayout_KeepsEveryChildLocalAndUniformlyScaledWithoutLetterboxInsets()
        {
            var canonical = global::LanLobbyRoomLayout.ForSize(1920, 1080).Slots[0];
            var scaled = global::LanLobbyRoomLayout.ForSize(2560, 1440).Slots[0];

            foreach (var child in Children(canonical))
            {
                AssertContained(child, canonical.Root);
            }

            var canonicalChildren = Children(canonical);
            var scaledChildren = Children(scaled);
            Assert.That(scaledChildren.Count, Is.EqualTo(canonicalChildren.Count));
            for (var index = 0; index < canonicalChildren.Count; index++)
            {
                AssertScaled(canonicalChildren[index], scaledChildren[index], 4f / 3f);
            }
        }

        [Test]
        public void RoomLayout_PlacesMeasuredActionsAndSeparatesLatencyFromLeave()
        {
            var layout = global::LanLobbyRoomLayout.ForSize(1920, 1080);

            AssertRect(layout.PrimaryAction, 1487.25f, 42.75f, 432f, 94.5f);
            AssertRect(layout.LeaveAction, 43.5f, 997.5f, 118f, 52.5f);
            Assert.That(layout.LeaveAction.Right <= layout.Latency.Left || layout.Latency.Right <= layout.LeaveAction.Left || layout.LeaveAction.Top <= layout.Latency.Bottom || layout.Latency.Top <= layout.LeaveAction.Bottom, Is.True);
        }

        [Test]
        public void RoomLayout_ScalesCanonicalGeometryUniformlyAtFourThirds()
        {
            var canonical = global::LanLobbyRoomLayout.ForSize(1920, 1080);
            var scaled = global::LanLobbyRoomLayout.ForSize(2560, 1440);

            AssertScaled(canonical.Slots[0].Root, scaled.Slots[0].Root, 4f / 3f);
            AssertScaled(canonical.LeaveAction, scaled.LeaveAction, 4f / 3f);
            AssertScaled(canonical.Latency, scaled.Latency, 4f / 3f);
            AssertScaled(canonical.PrimaryAction, scaled.PrimaryAction, 4f / 3f);
        }

        [Test]
        public void RoomLayout_LetterboxesTallerNon16By9CanvasWithoutChangingInternalAspect()
        {
            var canonical = global::LanLobbyRoomLayout.ForSize(1920, 1080);
            var letterboxed = global::LanLobbyRoomLayout.ForSize(1920, 1200);
            var canonicalRoot = canonical.Slots[0].Root;
            var letterboxedRoot = letterboxed.Slots[0].Root;

            Assert.That(letterboxedRoot.Left, Is.EqualTo(canonicalRoot.Left).Within(0.01f));
            Assert.That(letterboxedRoot.Bottom, Is.EqualTo(canonicalRoot.Bottom + 60f).Within(0.01f));
            Assert.That(letterboxedRoot.Width, Is.EqualTo(canonicalRoot.Width).Within(0.01f));
            Assert.That(letterboxedRoot.Height, Is.EqualTo(canonicalRoot.Height).Within(0.01f));
            Assert.That(letterboxedRoot.Width / letterboxedRoot.Height, Is.EqualTo(canonicalRoot.Width / canonicalRoot.Height).Within(0.0001f));
        }

        [Test]
        public void RoomLayout_LetterboxesWideCanvasWithoutOffsettingLocalChildren()
        {
            var canonical = global::LanLobbyRoomLayout.ForSize(1920, 1080);
            var wide = global::LanLobbyRoomLayout.ForSize(2560, 1080);

            Assert.That(wide.Slots[0].Root.Left, Is.EqualTo(canonical.Slots[0].Root.Left + 320f).Within(0.01f));
            Assert.That(wide.Slots[0].Root.Bottom, Is.EqualTo(canonical.Slots[0].Root.Bottom).Within(0.01f));
            Assert.That(wide.PrimaryAction.Left, Is.EqualTo(canonical.PrimaryAction.Left + 320f).Within(0.01f));
            var canonicalChildren = Children(canonical.Slots[0]);
            var wideChildren = Children(wide.Slots[0]);
            for (var index = 0; index < canonicalChildren.Count; index++)
            {
                AssertRect(wideChildren[index], canonicalChildren[index].Left, canonicalChildren[index].Bottom, canonicalChildren[index].Width, canonicalChildren[index].Height);
            }
        }

        private static List<global::LanLobbyRect> Children(global::LanLobbyRoomSlotLayout slot)
        {
            return new List<global::LanLobbyRect>
            {
                slot.CardBody,
                slot.TopBar,
                slot.StateOverlay,
                slot.EmptyInvite,
                slot.ReadyIcon,
                slot.ReadyLabel,
                slot.LowerDecoration,
                slot.CreatorTag
            };
        }

        private static void AssertContained(global::LanLobbyRect child, global::LanLobbyRect root)
        {
            Assert.That(child.Left, Is.GreaterThanOrEqualTo(0f));
            Assert.That(child.Bottom, Is.GreaterThanOrEqualTo(0f));
            Assert.That(child.Right, Is.LessThanOrEqualTo(root.Width));
            Assert.That(child.Top, Is.LessThanOrEqualTo(root.Height));
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
