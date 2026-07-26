using System.Linq;
using NUnit.Framework;

namespace ArknoNights.Lobby.Tests
{
    public sealed class LanLobbyLayoutEditModeTests
    {
        [Test]
        public void RoomCards_FourPlayersStayWithin1920Width()
        {
            var layout = global::LanLobbyLayout.ForSize(1920, 1080, 4);

            Assert.That(layout.Cards.Count, Is.EqualTo(4));
            Assert.That(layout.Cards.All(card => card.Left >= 0f && card.Right <= 1920f), Is.True);
        }

        [TestCase(1920, 1080)]
        [TestCase(1280, 720)]
        public void HomeLayout_StaysWithinViewportAtSupported16By9Sizes(int width, int height)
        {
            var layout = global::LanLobbyLayout.ForSize(width, height, 4);

            Assert.That(layout.HomePanels.All(panel => panel.Left >= 0f && panel.Right <= width && panel.Bottom >= 0f && panel.Top <= height), Is.True);
        }

        [TestCase(1920, 1080)]
        [TestCase(1280, 720)]
        public void RoomSelect_CreateAndJoinStayInRightHalfAtSupported16By9Sizes(int width, int height)
        {
            var layout = global::LanLobbyLayout.ForSize(width, height, 4);

            Assert.That(layout.RoomSelectCreate.Left, Is.GreaterThanOrEqualTo(width * .5f));
            Assert.That(layout.RoomSelectJoin.Left, Is.GreaterThanOrEqualTo(width * .5f));
            Assert.That(layout.RoomSelectCreate.Right, Is.LessThanOrEqualTo(width));
            Assert.That(layout.RoomSelectJoin.Right, Is.LessThanOrEqualTo(width));
            Assert.That(layout.RoomSelectCreate.Bottom, Is.GreaterThanOrEqualTo(0f));
            Assert.That(layout.RoomSelectJoin.Bottom, Is.GreaterThanOrEqualTo(0f));
        }

        [Test]
        public void RoomSelect_ActionBarsUseMeasuredFigure9Rects()
        {
            var full = global::LanLobbyLayout.ForSize(1920, 1080, 4);
            AssertRect(full.RoomSelectCreateAction, 1154f, 528f, 717f, 99f, 0.01f);
            AssertRect(full.RoomSelectJoinAction, 1154f, 105f, 717f, 99f, 0.01f);

            var small = global::LanLobbyLayout.ForSize(1280, 720, 4);
            AssertRect(small.RoomSelectCreateAction, 1154f * 2f / 3f, 528f * 2f / 3f, 717f * 2f / 3f, 66f, 0.02f);
            AssertRect(small.RoomSelectJoinAction, 1154f * 2f / 3f, 70f, 717f * 2f / 3f, 66f, 0.02f);
        }

        private static void AssertRect(global::LanLobbyRect actual, float left, float bottom, float width, float height, float tolerance)
        {
            Assert.That(actual.Left, Is.EqualTo(left).Within(tolerance));
            Assert.That(actual.Bottom, Is.EqualTo(bottom).Within(tolerance));
            Assert.That(actual.Width, Is.EqualTo(width).Within(tolerance));
            Assert.That(actual.Height, Is.EqualTo(height).Within(tolerance));
        }
    }
}
