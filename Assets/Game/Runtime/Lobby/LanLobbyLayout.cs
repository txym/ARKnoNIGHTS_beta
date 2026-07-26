using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public sealed class LanLobbyLayout
{
    private const float ReferenceWidth = 1920f;
    private const float ReferenceHeight = 1080f;

    private LanLobbyLayout(IReadOnlyList<LanLobbyRect> cards, IReadOnlyList<LanLobbyRect> homePanels, LanLobbyRect roomSelectCreate, LanLobbyRect roomSelectJoin)
    {
        Cards = cards;
        HomePanels = homePanels;
        RoomSelectCreate = roomSelectCreate;
        RoomSelectJoin = roomSelectJoin;
    }

    public IReadOnlyList<LanLobbyRect> Cards { get; }
    public IReadOnlyList<LanLobbyRect> HomePanels { get; }
    public LanLobbyRect RoomSelectCreate { get; }
    public LanLobbyRect RoomSelectJoin { get; }

    public static LanLobbyLayout ForSize(int width, int height, int playerCount)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        var scale = Math.Min(width / ReferenceWidth, height / ReferenceHeight);
        var contentWidth = ReferenceWidth * scale;
        var contentHeight = ReferenceHeight * scale;
        var leftInset = (width - contentWidth) * .5f;
        var bottomInset = (height - contentHeight) * .5f;

        var cards = new List<LanLobbyRect>();
        var cardCount = Math.Min(Math.Max(playerCount, 0), LobbyMemberCapacity);
        var cardWidth = 390f * scale;
        var cardHeight = 430f * scale;
        var gap = 30f * scale;
        var totalWidth = cardCount * cardWidth + Math.Max(cardCount - 1, 0) * gap;
        var start = leftInset + (contentWidth - totalWidth) * .5f;
        for (var index = 0; index < cardCount; index++)
        {
            cards.Add(new LanLobbyRect(start + index * (cardWidth + gap), bottomInset + 240f * scale, cardWidth, cardHeight));
        }

        var create = new LanLobbyRect(leftInset + 1032f * scale, bottomInset + 562f * scale, 820f * scale, 270f * scale);
        var join = new LanLobbyRect(leftInset + 1032f * scale, bottomInset + 225f * scale, 820f * scale, 270f * scale);
        var panels = new[]
        {
            new LanLobbyRect(leftInset + 80f * scale, bottomInset + 180f * scale, 540f * scale, 720f * scale),
            create,
            join,
            new LanLobbyRect(leftInset + 1032f * scale, bottomInset + 80f * scale, 820f * scale, 120f * scale)
        };

        return new LanLobbyLayout(new ReadOnlyCollection<LanLobbyRect>(cards), new ReadOnlyCollection<LanLobbyRect>(panels), create, join);
    }

    private const int LobbyMemberCapacity = 4;
}

public sealed class LanLobbyRect
{
    public LanLobbyRect(float left, float bottom, float width, float height)
    {
        Left = left;
        Bottom = bottom;
        Width = width;
        Height = height;
    }

    public float Left { get; }
    public float Bottom { get; }
    public float Width { get; }
    public float Height { get; }
    public float Right => Left + Width;
    public float Top => Bottom + Height;
}
