using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public sealed class LanLobbyRoomSlotLayout
{
    internal LanLobbyRoomSlotLayout(
        LanLobbyRect root,
        LanLobbyRect cardBody,
        LanLobbyRect topBar,
        LanLobbyRect readyTopBar,
        LanLobbyRect stateOverlay,
        LanLobbyRect emptyInvite,
        LanLobbyRect readyIcon,
        LanLobbyRect readyLabel,
        LanLobbyRect lowerDecoration,
        LanLobbyRect creatorTag)
    {
        Root = root;
        CardBody = cardBody;
        TopBar = topBar;
        ReadyTopBar = readyTopBar;
        StateOverlay = stateOverlay;
        EmptyInvite = emptyInvite;
        ReadyIcon = readyIcon;
        ReadyLabel = readyLabel;
        LowerDecoration = lowerDecoration;
        CreatorTag = creatorTag;
    }

    public LanLobbyRect Root { get; }
    public LanLobbyRect CardBody { get; }
    public LanLobbyRect TopBar { get; }
    public LanLobbyRect ReadyTopBar { get; }
    public LanLobbyRect StateOverlay { get; }
    public LanLobbyRect EmptyInvite { get; }
    public LanLobbyRect ReadyIcon { get; }
    public LanLobbyRect ReadyLabel { get; }
    public LanLobbyRect LowerDecoration { get; }
    public LanLobbyRect CreatorTag { get; }
}

public sealed class LanLobbyRoomLayout
{
    private const float ReferenceWidth = 1920f;
    private const float ReferenceHeight = 1080f;
    private const float SlotRootHeight = 664.5f;
    private const float CardBodyLeft = 26.25f;
    private const float CardBodyWidth = 320.25f;
    private const float CardBodyHeight = 545.25f;
    private const float TopBarSourceWidth = 234f;
    private const float TopBarSourceHeight = 31f;
    private const float EmptyInviteSourceWidth = 275f;
    private const float EmptyInviteSourceHeight = 104f;
    private const float ReadyContourSourceWidth = 256f;
    private const float ReadyContourSourceHeight = 103f;

    private static readonly LanLobbyRect[] CanonicalRoots =
    {
        FromTopLeft(199.5f, 177.75f, 363.75f, SlotRootHeight, ReferenceHeight),
        FromTopLeft(588.75f, 177.75f, 363.75f, SlotRootHeight, ReferenceHeight),
        FromTopLeft(976.5f, 177.75f, 363.75f, SlotRootHeight, ReferenceHeight),
        FromTopLeft(1365f, 177.75f, 363.75f, SlotRootHeight, ReferenceHeight)
    };

    private static readonly LanLobbyRect CanonicalCardBody = FromTopLeft(CardBodyLeft, 0f, CardBodyWidth, CardBodyHeight, SlotRootHeight);
    private static readonly LanLobbyRect CanonicalTopBar = FromTopLeft(CardBodyLeft, 0f, CardBodyWidth, CardBodyWidth * TopBarSourceHeight / TopBarSourceWidth, SlotRootHeight);
    private static readonly LanLobbyRect CanonicalReadyTopBar = FromTopLeft(CardBodyLeft, -8f, CardBodyWidth, 51f, SlotRootHeight);
    private static readonly LanLobbyRect CanonicalStateOverlay = new LanLobbyRect(
        CanonicalCardBody.Left,
        CanonicalCardBody.Bottom,
        CanonicalCardBody.Width,
        CanonicalCardBody.Width * ReadyContourSourceHeight / ReadyContourSourceWidth);
    private static readonly LanLobbyRect CanonicalEmptyInvite = CenteredIn(
        CanonicalCardBody,
        CardBodyWidth,
        CardBodyWidth * EmptyInviteSourceHeight / EmptyInviteSourceWidth);
    private static readonly LanLobbyRect CanonicalReadyIcon = new LanLobbyRect(114.5f, 148.25f, 38f, 38f);
    private static readonly LanLobbyRect CanonicalReadyLabel = new LanLobbyRect(139.5f, 148.25f, 0f, 0f);
    private static readonly LanLobbyRect CanonicalLowerDecoration = FromTopLeft(0f, 544.5f, 363.75f, 120f, SlotRootHeight);
    private static readonly LanLobbyRect CanonicalCreatorTag = new LanLobbyRect(123.5f, 576.25f, 124f, 35f);

    private static readonly LanLobbyRect CanonicalLeaveAction = FromTopLeft(37f, 29f, 75f, 80f, ReferenceHeight);
    private static readonly LanLobbyRect CanonicalLatency = FromTopLeft(176f, 30f, 250f, 52.5f, ReferenceHeight);
    private static readonly LanLobbyRect CanonicalPrimaryAction = FromTopLeft(1487.25f, 941.75f, 436f, 95.5f, ReferenceHeight);
    private static readonly LanLobbyRect CanonicalDisabledPrimaryAction = FromTopLeft(1487.25f, 933.75f, 435f, 105f, ReferenceHeight);
    private static readonly LanLobbyRect CanonicalPrimaryIcon = new LanLobbyRect(1573f, 60f, 62f, 56f);
    private static readonly LanLobbyRect CanonicalDisabledPrimaryIcon = new LanLobbyRect(1571f, 64f, 63f, 52f);
    private static readonly LanLobbyRect CanonicalPrimaryLabel = new LanLobbyRect(1645f, 71f, 150f, 38f);
    private static readonly LanLobbyRect CanonicalDisabledPrimaryLabel = new LanLobbyRect(1645f, 69f, 150f, 38f);

    private LanLobbyRoomLayout(
        IReadOnlyList<LanLobbyRoomSlotLayout> slots,
        LanLobbyRect leaveAction,
        LanLobbyRect latency,
        LanLobbyRect primaryAction,
        LanLobbyRect disabledPrimaryAction,
        LanLobbyRect primaryIcon,
        LanLobbyRect disabledPrimaryIcon,
        LanLobbyRect primaryLabel,
        LanLobbyRect disabledPrimaryLabel)
    {
        Slots = slots;
        LeaveAction = leaveAction;
        Latency = latency;
        PrimaryAction = primaryAction;
        DisabledPrimaryAction = disabledPrimaryAction;
        PrimaryIcon = primaryIcon;
        DisabledPrimaryIcon = disabledPrimaryIcon;
        PrimaryLabel = primaryLabel;
        DisabledPrimaryLabel = disabledPrimaryLabel;
    }

    public IReadOnlyList<LanLobbyRoomSlotLayout> Slots { get; }
    public LanLobbyRect LeaveAction { get; }
    public LanLobbyRect Latency { get; }
    public LanLobbyRect PrimaryAction { get; }
    public LanLobbyRect DisabledPrimaryAction { get; }
    public LanLobbyRect PrimaryIcon { get; }
    public LanLobbyRect DisabledPrimaryIcon { get; }
    public LanLobbyRect PrimaryLabel { get; }
    public LanLobbyRect DisabledPrimaryLabel { get; }

    public static LanLobbyRoomLayout ForSize(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        var scale = Math.Min(width / ReferenceWidth, height / ReferenceHeight);
        var contentWidth = ReferenceWidth * scale;
        var contentHeight = ReferenceHeight * scale;
        var leftInset = (width - contentWidth) * .5f;
        var bottomInset = (height - contentHeight) * .5f;
        var slots = new List<LanLobbyRoomSlotLayout>(CanonicalRoots.Length);

        foreach (var root in CanonicalRoots)
        {
            slots.Add(new LanLobbyRoomSlotLayout(
                ScaleAndOffset(root, scale, leftInset, bottomInset),
                Scale(CanonicalCardBody, scale),
                Scale(CanonicalTopBar, scale),
                Scale(CanonicalReadyTopBar, scale),
                Scale(CanonicalStateOverlay, scale),
                Scale(CanonicalEmptyInvite, scale),
                Scale(CanonicalReadyIcon, scale),
                Scale(CanonicalReadyLabel, scale),
                Scale(CanonicalLowerDecoration, scale),
                Scale(CanonicalCreatorTag, scale)));
        }

        return new LanLobbyRoomLayout(
            new ReadOnlyCollection<LanLobbyRoomSlotLayout>(slots),
            ScaleAndOffset(CanonicalLeaveAction, scale, leftInset, bottomInset),
            ScaleAndOffset(CanonicalLatency, scale, leftInset, bottomInset),
            ScaleAndOffset(CanonicalPrimaryAction, scale, leftInset, bottomInset),
            ScaleAndOffset(CanonicalDisabledPrimaryAction, scale, leftInset, bottomInset),
            ScaleAndOffset(CanonicalPrimaryIcon, scale, leftInset, bottomInset),
            ScaleAndOffset(CanonicalDisabledPrimaryIcon, scale, leftInset, bottomInset),
            ScaleAndOffset(CanonicalPrimaryLabel, scale, leftInset, bottomInset),
            ScaleAndOffset(CanonicalDisabledPrimaryLabel, scale, leftInset, bottomInset));
    }

    private static LanLobbyRect FromTopLeft(float left, float top, float width, float height, float canvasHeight)
    {
        return new LanLobbyRect(left, canvasHeight - top - height, width, height);
    }

    private static LanLobbyRect CenteredIn(LanLobbyRect container, float width, float height)
    {
        return new LanLobbyRect(
            container.Left + (container.Width - width) * .5f,
            container.Bottom + (container.Height - height) * .5f,
            width,
            height);
    }

    private static LanLobbyRect ScaleAndOffset(LanLobbyRect rect, float scale, float leftInset, float bottomInset)
    {
        return new LanLobbyRect(
            leftInset + rect.Left * scale,
            bottomInset + rect.Bottom * scale,
            rect.Width * scale,
            rect.Height * scale);
    }

    private static LanLobbyRect Scale(LanLobbyRect rect, float scale)
    {
        return new LanLobbyRect(rect.Left * scale, rect.Bottom * scale, rect.Width * scale, rect.Height * scale);
    }
}
