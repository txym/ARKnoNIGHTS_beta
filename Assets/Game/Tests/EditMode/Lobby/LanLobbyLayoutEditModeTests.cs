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
    }
}
