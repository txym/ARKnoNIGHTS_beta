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
        public void RoomLayout_UsesOneCompensatedPortraitFrameWithDeepLowerOverlap()
        {
            var layout = global::LanLobbyRoomLayout.ForSize(1920, 1080);

            foreach (var slot in layout.Slots)
            {
                AssertRect(slot.CardBody, 26f, 38.5f, 329f, 626f);
                AssertContained(slot.CardBody, slot.Root);
                AssertContained(slot.LowerDecoration, slot.Root);
                Assert.That(
                    slot.LowerDecoration.Top - slot.CardBody.Bottom,
                    Is.EqualTo(81.5f).Within(0.01f));
                Assert.That(
                    slot.LowerDecoration.Top - slot.CardBody.Bottom,
                    Is.GreaterThanOrEqualTo(60f));
            }
        }

        [Test]
        public void RoomLayout_EnlargesOnlyPortraitFrameAndPreservesExistingSlotChildren()
        {
            var slot = global::LanLobbyRoomLayout.ForSize(1920, 1080).Slots[0];

            AssertRect(slot.TopBar, 26.25f, 622.073718f, 320.25f, 42.426282f);
            AssertRect(slot.ReadyTopBar, 26.25f, 621.5f, 320.25f, 51f);
            AssertRect(slot.StateOverlay, 18.5f, 111.25f, 337f, 135.589844f);
            AssertRect(slot.EmptyInvite, 18.5f, 324.151364f, 337f, 127.447273f);
            AssertRect(slot.ReadyIcon, 114.5f, 144.25f, 38f, 38f);
            AssertRect(slot.ReadyLabel, 168.5f, 148.25f, 0f, 0f);
            AssertRect(slot.LowerDecoration, 0f, 0f, 363.75f, 120f);
            AssertRect(slot.CreatorTag, 123.5f, 576.25f, 124f, 35f);
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
        public void RoomLayout_PlacesMeasuredReadyLayersAtTheirReferenceVisibleTargets()
        {
            var slot = global::LanLobbyRoomLayout.ForSize(1920, 1080).Slots[0];

            AssertRect(slot.ReadyTopBar, 26.25f, 621.5f, 320.25f, 51f);
            AssertRect(slot.ReadyIcon, 114.5f, 144.25f, 38f, 38f);
            AssertRect(slot.ReadyLabel, 168.5f, 148.25f, 0f, 0f);
            AssertRect(slot.CreatorTag, 123.5f, 576.25f, 124f, 35f);
        }

        [Test]
        public void RoomLayout_KeepsChildrenLocalAndUniformlyScaledWithOnlyTheMeasuredReadyTopOverhang()
        {
            var canonical = global::LanLobbyRoomLayout.ForSize(1920, 1080).Slots[0];
            var scaled = global::LanLobbyRoomLayout.ForSize(2560, 1440).Slots[0];

            foreach (var child in Children(canonical))
            {
                if (ReferenceEquals(child, canonical.ReadyTopBar)) continue;
                AssertContained(child, canonical.Root);
            }
            Assert.That(canonical.ReadyTopBar.Top - canonical.Root.Height, Is.EqualTo(8f).Within(0.01f));

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

            AssertRect(layout.PrimaryAction, 1487.25f, 42.75f, 436f, 95.5f);
            AssertRect(layout.DisabledPrimaryAction, 1487.25f, 41.25f, 435f, 114f);
            // btn_match_host_normal's DarkOnCyan source-visible height is
            // 38/41 of its RectTransform, so 56 px produces the measured
            // 52 px Player foreground without shifting its visible top.
            AssertRect(layout.PrimaryIcon, 1573f, 60f, 62f, 56f);
            AssertRect(layout.DisabledPrimaryIcon, 1571f, 60f, 63f, 56f);
            AssertRect(layout.PrimaryLabel, 1627.5f, 71f, 190f, 38f);
            AssertRect(layout.DisabledPrimaryLabel, 1627.5f, 72f, 190f, 38f);
            // The 54x56 img_return source has a blended light core near
            // x=15,y=8,w=28,h=28; compensate the source-visible inset so that
            // the decoded Player core lands on reference x=58,y=40,w=39,h=40.
            AssertRect(layout.LeaveAction, 37f, 971f, 75f, 80f);
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
            AssertScaled(canonical.DisabledPrimaryAction, scaled.DisabledPrimaryAction, 4f / 3f);
            AssertScaled(canonical.PrimaryIcon, scaled.PrimaryIcon, 4f / 3f);
            AssertScaled(canonical.DisabledPrimaryIcon, scaled.DisabledPrimaryIcon, 4f / 3f);
            AssertScaled(canonical.PrimaryLabel, scaled.PrimaryLabel, 4f / 3f);
            AssertScaled(canonical.DisabledPrimaryLabel, scaled.DisabledPrimaryLabel, 4f / 3f);
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
                slot.ReadyTopBar,
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
