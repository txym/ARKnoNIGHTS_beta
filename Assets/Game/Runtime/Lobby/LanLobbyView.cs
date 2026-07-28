using System;
using System.Collections.Generic;
using ArknoNights.Lobby;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class LanLobbyView : MonoBehaviour
{
    private const int CanvasOrder = 1000;
    private const int MaximumVisibleDiscoveryRooms = 4;
    private const string FontPath = "Fonts/Novecento wide Normal Regular.woff2";
    private const string SpriteRoot = "UI/Lobby/";
    private const float RoomReferenceWidth = 1920f;
    private const float RoomReferenceHeight = 1080f;

    private enum RoomSlotPresentationState
    {
        Empty,
        Waiting,
        Ready
    }

    private sealed class RoomSlotView
    {
        public RectTransform Root;
        public Image CardBody;
        public LanLobbyRect CardBodyLayout;
        public Image TopBar;
        public LanLobbyRect WaitingTopBarLayout;
        public LanLobbyRect ReadyTopBarLayout;
        public Image ReadyOverlay;
        public GameObject EmptyContent;
        public Image EmptyInviteIcon;
        public Text EmptyInviteLabel;
        public Text EmptyInviteHint;
        public GameObject OccupiedContent;
        public Image ReadyIcon;
        public Text ReadyLabel;
        public Image LowerDecoration;
        public Image CreatorTag;
    }

    private readonly Dictionary<string, LobbyDiscoveryEntry> discoveries = new Dictionary<string, LobbyDiscoveryEntry>(StringComparer.Ordinal);
    private readonly List<RoomSlotView> roomSlots = new List<RoomSlotView>();
    private Canvas canvas;
    private RectTransform homeRoot;
    private RectTransform roomRoot;
    private Image legacyGridForeground;
    private InputField profileNameInput;
    private Text avatarIndexText;
    private Image avatarImage;
    private InputField roomCodeInput;
    private Button joinButton;
    private Text statusText;
    private RectTransform discoveryItemsRoot;
    private RectTransform discoveredRoomsPanel;
    private Text discoveryOverflowText;
    private Text latencyText;
    private Text roomCodeText;
    private Button roomPrimaryActionButton;
    private Image roomPrimaryActionIcon;
    private Text roomPrimaryActionLabel;
    private Button roomLeaveButton;
    private LanLobbyRoomLayout roomLayout;
    private string localPlayerId;
    private bool boundLocalIsHost;
    private bool boundLocalMemberReady;
    private int avatarIndex;
    private int readyCardCount;
    private int discoveryOverflowCount;
    private float lastRoomPresentationWidth = -1f;
    private float lastRoomPresentationHeight = -1f;
    private float lastRoomCanvasScale = -1f;

    public event Action CreateRequested;
    public event Action<string> JoinRequested;
    public event Action<LobbyProfile> ProfileSaved;
    public event Action<bool> ReadyRequested;
    public event Action LeaveRequested;
    public event Action StartRequested;

    public string RoomCodeTextForTests => roomCodeInput == null ? string.Empty : roomCodeInput.text;
    public bool JoinInteractableForTests => joinButton != null && joinButton.interactable;
    public string StatusTextForTests => statusText == null ? string.Empty : statusText.text;
    public int RoomCardCountForTests => roomSlots.Count;
    public int ReadyCardCountForTests => readyCardCount;
    public string LocalLatencyTextForTests => latencyText == null ? string.Empty : latencyText.text;
    public int CanvasSortOrderForTests => canvas == null ? -1 : canvas.sortingOrder;
    public bool RoomPrimaryActionInteractableForTests => roomPrimaryActionButton != null && roomPrimaryActionButton.interactable;
    public bool RoomLeaveInteractableForTests => roomLeaveButton != null && roomLeaveButton.interactable;
    public int DiscoveryRenderedItemCountForTests => discoveryItemsRoot == null ? 0 : discoveryItemsRoot.childCount;
    public int DiscoveryOverflowCountForTests => discoveryOverflowCount;
    private void Awake()
    {
        Build();
        ShowHome();
    }

    private void LateUpdate()
    {
        RefreshRoomPresentation();
    }

    public void ShowHome()
    {
        if (homeRoot != null) homeRoot.gameObject.SetActive(true);
        if (roomRoot != null) roomRoot.gameObject.SetActive(false);
        if (legacyGridForeground != null) legacyGridForeground.gameObject.SetActive(false);
    }

    public void ShowHome(LobbyProfile profile)
    {
        ShowHome();
        if (profile == null) return;
        if (profileNameInput != null) profileNameInput.text = profile.DisplayName;
        avatarIndex = Mathf.Clamp(profile.AvatarIndex, LobbyProfile.MinimumAvatarIndex, LobbyProfile.MaximumAvatarIndex);
        RefreshAvatarIndex();
    }

    public void ShowRoom()
    {
        if (homeRoot != null) homeRoot.gameObject.SetActive(false);
        if (roomRoot != null) roomRoot.gameObject.SetActive(true);
        if (legacyGridForeground != null) legacyGridForeground.gameObject.SetActive(false);
    }

    public void ShowRoom(LobbyRoomSnapshot room, string localId)
    {
        BindRoom(room, localId);
        ShowRoom();
    }

    public void BindDiscoveredRooms(IEnumerable<LobbyDiscoveryEntry> rooms)
    {
        discoveries.Clear();
        if (rooms != null)
        {
            foreach (var room in rooms)
            {
                if (room == null || string.IsNullOrEmpty(room.RoomCode)) continue;
                discoveries[room.RoomCode] = room;
            }
        }

        RebuildDiscoveryItems();
        if (discoveredRoomsPanel != null) discoveredRoomsPanel.gameObject.SetActive(discoveries.Count > 0);
        EvaluateJoinAvailability();
    }

    public void BindRoom(LobbyRoomSnapshot room)
    {
        BindRoom(room, localPlayerId);
    }

    public void BindRoom(LobbyRoomSnapshot room, string localId)
    {
        localPlayerId = localId;
        roomCodeText.text = room == null ? "------" : room.RoomCode;
        readyCardCount = 0;
        for (var index = 0; index < roomSlots.Count; index++)
        {
            var member = room != null && index < room.Members.Count ? room.Members[index] : null;
            var state = member == null
                ? RoomSlotPresentationState.Empty
                : member.IsReady
                    ? RoomSlotPresentationState.Ready
                    : RoomSlotPresentationState.Waiting;
            BindSlot(roomSlots[index], state, member, index == 0);
            if (member != null && member.IsReady) readyCardCount++;
        }

        var localMemberReady = false;
        var hasLocalMember = false;
        var localIsHost = false;
        if (room != null)
        {
            foreach (var member in room.Members)
            {
                if (member == null || member.PlayerId != localPlayerId) continue;
                hasLocalMember = true;
                localMemberReady = member.IsReady;
                localIsHost = member.PlayerId == room.HostPlayerId;
                break;
            }
        }

        boundLocalIsHost = localIsHost;
        boundLocalMemberReady = localMemberReady;
        var canMutateRoom = room != null && !room.HasStarted && hasLocalMember;
        var allPresentMembersReady = room != null
            && room.Members.Count > 0
            && readyCardCount == room.Members.Count;
        var primaryUsesNormalSprite = localIsHost ? allPresentMembersReady : localMemberReady;

        roomPrimaryActionButton.GetComponent<Image>().sprite = Sprite(
            primaryUsesNormalSprite ? "btn_match_normal" : "btn_match_grey");
        var primaryActionLayout = primaryUsesNormalSprite
            ? roomLayout.PrimaryAction
            : roomLayout.DisabledPrimaryAction;
        var primaryIconLayout = primaryUsesNormalSprite
            ? roomLayout.PrimaryIcon
            : roomLayout.DisabledPrimaryIcon;
        var primaryLabelLayout = primaryUsesNormalSprite
            ? roomLayout.PrimaryLabel
            : roomLayout.DisabledPrimaryLabel;
        PositionBottomLeft(roomPrimaryActionButton.GetComponent<RectTransform>(), primaryActionLayout);
        roomPrimaryActionIcon.sprite = Sprite(
            primaryUsesNormalSprite ? "btn_match_host_normal" : "btn_match_host_grey");
        PositionBottomLeft(roomPrimaryActionIcon.rectTransform, RelativeTo(primaryIconLayout, primaryActionLayout));
        PositionBottomLeft(roomPrimaryActionLabel.rectTransform, RelativeTo(primaryLabelLayout, primaryActionLayout));
        roomPrimaryActionLabel.color = primaryUsesNormalSprite
            ? new Color(33f / 255f, 33f / 255f, 33f / 255f, 1f)
            : new Color(157f / 255f, 157f / 255f, 157f / 255f, 1f);
        roomPrimaryActionLabel.text = localIsHost
            ? "协议启动"
            : localMemberReady
                ? "取消准备"
                : "准备就绪";
        roomPrimaryActionButton.interactable = canMutateRoom && (!localIsHost || allPresentMembersReady);
        roomLeaveButton.interactable = canMutateRoom;
    }

    public void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message ?? string.Empty;
    }

    public void SetLocalLatency(long latencyMilliseconds)
    {
        if (latencyText != null) latencyText.text = Math.Max(0, latencyMilliseconds) + " ms";
    }

    public void ClickDiscoveredRoomForTests(string roomCode)
    {
        SetRoomCode(roomCode);
    }

    public void SetRoomCodeForTests(string roomCode)
    {
        SetRoomCode(roomCode);
    }

    private void Build()
    {
        canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CanvasOrder;

        var scaler = GetComponent<CanvasScaler>();
        if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = .5f;
        if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
        if (EventSystem.current == null && FindObjectOfType<EventSystem>() == null)
        {
            var eventSystem = new GameObject("LanLobbyEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            eventSystem.transform.SetParent(transform, false);
        }

        var root = Rect("LanLobbyRoot", transform);
        Stretch(root);
        var blockerRoot = new GameObject("OpaqueBlocker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        blockerRoot.transform.SetParent(root, false);
        var blocker = blockerRoot.GetComponent<Image>();
        blocker.color = new Color(.025f, .06f, .08f, 1f);
        blocker.raycastTarget = false;
        Stretch(blocker.rectTransform);
        var background = Image("Terrain", root, "bg_terrain");
        Stretch(background.rectTransform);
        background.preserveAspect = false;
        legacyGridForeground = Image("GridForeground", root, "shallow_main");
        Stretch(legacyGridForeground.rectTransform);
        legacyGridForeground.preserveAspect = false;
        legacyGridForeground.raycastTarget = false;
        legacyGridForeground.gameObject.SetActive(false);

        homeRoot = Rect("Home", root);
        Stretch(homeRoot);
        roomRoot = Rect("Room", root);
        roomRoot.anchorMin = Vector2.zero;
        roomRoot.anchorMax = Vector2.zero;
        roomRoot.pivot = Vector2.zero;
        roomRoot.anchoredPosition = Vector2.zero;
        roomRoot.sizeDelta = new Vector2(RoomReferenceWidth, RoomReferenceHeight);
        BuildHome(homeRoot);
        BuildRoom(roomRoot);
        RefreshRoomPresentation();
    }

    private void RefreshRoomPresentation()
    {
        if (roomRoot == null || canvas == null) return;
        var presentationRect = roomRoot.parent as RectTransform;
        if (presentationRect == null) return;

        var canvasScale = Mathf.Max(canvas.scaleFactor, .0001f);
        var presentationWidth = presentationRect.rect.width * canvasScale;
        var presentationHeight = presentationRect.rect.height * canvasScale;
        if (presentationWidth <= 0f || presentationHeight <= 0f) return;
        if (Mathf.Approximately(lastRoomPresentationWidth, presentationWidth)
            && Mathf.Approximately(lastRoomPresentationHeight, presentationHeight)
            && Mathf.Approximately(lastRoomCanvasScale, canvasScale))
        {
            return;
        }

        lastRoomPresentationWidth = presentationWidth;
        lastRoomPresentationHeight = presentationHeight;
        lastRoomCanvasScale = canvasScale;

        // Room children retain the calibrated 1920x1080 geometry. This root is
        // the explicit 16:9 letterbox container that maps it into the current
        // canvas pixel rect without applying CanvasScaler's aspect blend twice.
        var contentScalePixels = Mathf.Min(
            presentationWidth / RoomReferenceWidth,
            presentationHeight / RoomReferenceHeight);
        var leftInsetPixels = (presentationWidth - RoomReferenceWidth * contentScalePixels) * .5f;
        var bottomInsetPixels = (presentationHeight - RoomReferenceHeight * contentScalePixels) * .5f;
        var localScale = contentScalePixels / canvasScale;

        roomRoot.anchoredPosition = new Vector2(leftInsetPixels / canvasScale, bottomInsetPixels / canvasScale);
        roomRoot.localScale = new Vector3(localScale, localScale, 1f);
    }

    private void BuildHome(Transform parent)
    {
        var identity = Image("IdentityPanel", parent, "img_player_bkg");
        Position(identity.rectTransform, new Vector2(.19f, .5f), new Vector2(520f, 690f));
        var identityTitle = Text("Title", identity.transform, 34, TextAnchor.UpperLeft, new Color(.3f, .95f, .95f));
        Position(identityTitle.rectTransform, new Vector2(.08f, .9f), new Vector2(400f, 60f));
        identityTitle.text = "LOCAL IDENTITY";
        profileNameInput = Input("NameInput", identity.transform, "DISPLAY NAME", 28);
        Position(profileNameInput.GetComponent<RectTransform>(), new Vector2(.5f, .6f), new Vector2(410f, 64f));
        var avatarFrame = Image("AvatarSelector", identity.transform, "team_icon_frame");
        Position(avatarFrame.rectTransform, new Vector2(.5f, .77f), new Vector2(96f, 96f));
        avatarFrame.preserveAspect = true;
        avatarImage = Image("AvatarImage", avatarFrame.transform, "Home/icon_amiy");
        Stretch(avatarImage.rectTransform);
        avatarImage.rectTransform.offsetMin = new Vector2(10f, 10f);
        avatarImage.rectTransform.offsetMax = new Vector2(-10f, -10f);
        avatarImage.preserveAspect = true;
        avatarIndexText = Text("AvatarIndex", identity.transform, 18, TextAnchor.MiddleCenter, Color.white);
        Position(avatarIndexText.rectTransform, new Vector2(.5f, .68f), new Vector2(180f, 30f));
        var previousAvatar = Button("PreviousAvatar", identity.transform, "btn_match_grey", "<", 28);
        Position(previousAvatar.GetComponent<RectTransform>(), new Vector2(.33f, .77f), new Vector2(70f, 56f));
        previousAvatar.onClick.AddListener(() => ChangeAvatar(-1));
        var nextAvatar = Button("NextAvatar", identity.transform, "btn_match_grey", ">", 28);
        Position(nextAvatar.GetComponent<RectTransform>(), new Vector2(.67f, .77f), new Vector2(70f, 56f));
        nextAvatar.onClick.AddListener(() => ChangeAvatar(1));
        RefreshAvatarIndex();
        var save = Button("SaveProfile", identity.transform, "room_create_btn_bg", "SAVE", 24);
        Position(save.GetComponent<RectTransform>(), new Vector2(.5f, .42f), new Vector2(250f, 76f));
        save.onClick.AddListener(() => ProfileSaved?.Invoke(new LobbyProfile("local", profileNameInput.text, avatarIndex)));

        BuildRoomSelect(parent);
    }

    private void BuildRoomSelect(Transform parent)
    {
        var roomSelect = Rect("RoomSelect", parent);
        Stretch(roomSelect);
        var layout = LanLobbyLayout.ForSize(1920, 1080, LobbyRoomSnapshot.MaximumMembers);
        var rightBackground = Image("RightBackground", roomSelect, "Home/room_select_right_bg");
        PositionSprite(rightBackground, new Vector2(.751f, .79f), 820f);

        var titleIcon = Image("TitleIcon", roomSelect, "Home/room_select_title_icon");
        PositionSprite(titleIcon, new Vector2(.56f, .91f), 76f);
        var title = Text("Title", roomSelect, 34, TextAnchor.MiddleLeft, Color.white);
        Position(title.rectTransform, new Vector2(.71f, .91f), new Vector2(420f, 58f));
        title.text = "选择同盟方式";
        var titleDot = Image("TitleDot", roomSelect, "Home/room_select_dot");
        PositionSprite(titleDot, new Vector2(.88f, .91f), 26f);
        var create = Rect("Create", roomSelect);
        PositionBottomLeft(create, layout.RoomSelectCreate);
        BuildCreateSection(create, RelativeTo(layout.RoomSelectCreateAction, layout.RoomSelectCreate));

        var join = Rect("Join", roomSelect);
        PositionBottomLeft(join, layout.RoomSelectJoin);
        BuildJoinSection(join, RelativeTo(layout.RoomSelectJoinAction, layout.RoomSelectJoin));

        discoveredRoomsPanel = Rect("DiscoveredRooms", roomSelect);
        Position(discoveredRoomsPanel, new Vector2(.751f, .325f), new Vector2(770f, 240f));
        discoveredRoomsPanel.gameObject.SetActive(false);
        discoveryItemsRoot = Rect("Items", discoveredRoomsPanel);
        Stretch(discoveryItemsRoot);
        discoveryItemsRoot.offsetMin = new Vector2(28f, 15f);
        discoveryItemsRoot.offsetMax = new Vector2(-28f, -15f);
        var itemLayout = discoveryItemsRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        itemLayout.spacing = 8f;
        itemLayout.childAlignment = TextAnchor.UpperCenter;
        itemLayout.childControlWidth = true;
        itemLayout.childForceExpandHeight = false;
        discoveryOverflowText = Text("Overflow", discoveredRoomsPanel, 18, TextAnchor.LowerRight, new Color(.3f, .95f, .95f));
        discoveryOverflowText.rectTransform.anchorMin = new Vector2(0f, 0f);
        discoveryOverflowText.rectTransform.anchorMax = new Vector2(1f, 0f);
        discoveryOverflowText.rectTransform.pivot = new Vector2(.5f, 0f);
        discoveryOverflowText.rectTransform.anchoredPosition = new Vector2(0f, 25f);
        discoveryOverflowText.rectTransform.sizeDelta = new Vector2(-130f, 30f);

        statusText = Text("Status", roomSelect, 20, TextAnchor.MiddleCenter, Color.white);
        Position(statusText.rectTransform, new Vector2(.751f, .06f), new Vector2(700f, 36f));
        statusText.text = "DISCOVERING LOCAL ROOMS";
    }

    private void BuildCreateSection(RectTransform parent, LanLobbyRect actionRect)
    {
        CreateSolidDecorationPanel(
            "InteriorBacking", parent,
            147f, -12f, 666f, 224f,
            new Color(0f, 0f, 0f, .78f));

        var createFrame = Rect("CreateFrame", parent);
        Stretch(createFrame);
        var frameTint = new Color(.55f, .95f, .88f, 1f);
        CreateOrientedDecorationSprite("Top_0", createFrame, "Home/doc_frame_line", 263.667f, -23f, 237.171f, 12f, 0f, false, frameTint);
        CreateOrientedDecorationSprite("Top_1", createFrame, "Home/doc_frame_line", 480f, -23f, 237.171f, 12f, 0f, false, frameTint);
        CreateOrientedDecorationSprite("Top_2", createFrame, "Home/doc_frame_line", 696.333f, -23f, 237.171f, 12f, 0f, false, frameTint);
        CreateOrientedDecorationSprite("LeftUpper", createFrame, "Home/doc_frame_line", 147f, 40f, 128.053f, 12f, 90f, false, frameTint);
        CreateOrientedDecorationSprite("LeftLower", createFrame, "Home/doc_frame_line", 147f, 149f, 128.053f, 12f, 90f, false, frameTint);
        CreateOrientedDecorationSprite("RightUpper", createFrame, "Home/doc_frame_line", 813f, 40f, 128.053f, 12f, 270f, false, frameTint);
        CreateOrientedDecorationSprite("RightLower", createFrame, "Home/doc_frame_line", 813f, 149f, 128.053f, 12f, 270f, false, frameTint);

        var wings = Rect("Wings", parent);
        Stretch(wings);
        var wingTint = new Color(.35f, .65f, .58f, .45f);
        CreateOrientedDecorationSprite("WingLeftUpper", wings, "Home/img_pointer", 330f, 86f, 108f, 108f * 23f / 324f, 162f, true, wingTint);
        CreateOrientedDecorationSprite("WingLeftLower", wings, "Home/img_pointer", 330f, 147f, 108f, 108f * 23f / 324f, 198f, true, wingTint);
        CreateOrientedDecorationSprite("WingRightUpper", wings, "Home/img_pointer", 603f, 86f, 108f, 108f * 23f / 324f, 18f, true, wingTint);
        CreateOrientedDecorationSprite("WingRightLower", wings, "Home/img_pointer", 603f, 147f, 108f, 108f * 23f / 324f, 342f, true, wingTint);

        CreateOrientedDecorationSprite("DotTopLeft", parent, "Home/room_select_dot", 390.5f, 30.5f, 17f, 17f, 0f, true, Color.white);
        CreateOrientedDecorationSprite("DotTopRight", parent, "Home/room_select_dot", 525.5f, 31.5f, 17f, 17f, 0f, true, Color.white);
        CreateOrientedDecorationSprite("DotBottomLeft", parent, "Home/room_select_dot", 390f, 167f, 16f, 16f, 0f, true, Color.white);
        CreateOrientedDecorationSprite("DotBottomRight", parent, "Home/room_select_dot", 525.5f, 167.5f, 17f, 17f, 0f, true, Color.white);
        CreateOrientedDecorationSprite("LineLeft", parent, "Home/room_select_create_left_line", 402.1f, 89f, 16.2f, 54f, 0f, true, Color.white);
        CreateOrientedDecorationSprite("LineRight", parent, "Home/room_select_create_left_line", 515.1f, 89f, 16.2f, 54f, 180f, true, Color.white);
        CreateOrientedDecorationSprite("MiddleIcon", parent, "Home/room_select_create_middleicon", 459.5f, 87.81f, 87f, 87f * 62f / 63f, 0f, true, Color.white);
        CreateOrientedDecorationSprite("Text01", parent, "Home/room_select_create_text_01", 460f, 144.29f, 88f, 88f * 9f / 63f, 0f, true, Color.white);
        CreateOrientedDecorationSprite("Text02", parent, "Home/room_select_create_text_02", 461f, 154.59f, 66f, 66f * 5f / 46f, 0f, true, Color.white);
        CreateOrientedDecorationSprite("StartRoomDecoration", parent, "Home/room_select_img_startroom", 459f, 22.25f, 84f, 84f * 8f / 64f, 0f, true, Color.white);
        var create = Button("CreateAction", parent, "Home/room_select_create_btn_bg_down", "创建同盟", 32, true);
        PositionBottomLeft(create.GetComponent<RectTransform>(), actionRect);
        create.GetComponent<Image>().preserveAspect = false;
        var createIcon = Image("ActionIcon", create.transform, "create_icon");
        PositionSpriteTopLeft(createIcon, 47f, 25f, 38f);
        var createLabel = create.GetComponentInChildren<Text>();
        createLabel.color = new Color(.02f, .12f, .12f);
        createLabel.alignment = TextAnchor.MiddleLeft;
        createLabel.rectTransform.offsetMin = new Vector2(108f, 5f);
        createLabel.rectTransform.offsetMax = new Vector2(-220f, 5f);
        createLabel.fontSize = 38;
        create.onClick.AddListener(() => CreateRequested?.Invoke());
    }

    private void BuildJoinSection(RectTransform parent, LanLobbyRect actionRect)
    {
        CreateSolidDecorationPanel(
            "InteriorBacking", parent,
            122f, 11f, 717f, 280f,
            new Color(0f, 0f, 0f, .82f));
        var outlineColor = new Color(48f / 255f, 48f / 255f, 48f / 255f, .55f);
        CreateSolidDecorationPanel("OutlineTop", parent, 122f, 11f, 717f, 2f, outlineColor);
        CreateSolidDecorationPanel("OutlineLeft", parent, 122f, 11f, 2f, 280f, outlineColor);
        CreateSolidDecorationPanel("OutlineRight", parent, 837f, 11f, 2f, 280f, outlineColor);
        var guideColor = new Color(1f, 165f / 255f, 0f, .55f);
        CreateSolidDecorationPanel("GuideHorizontal", parent, 122f, 110f, 717f, 2f, guideColor);
        CreateSolidDecorationPanel("GuideVertical", parent, 474f, 11f, 2f, 196f, guideColor);

        for (var index = 0; index < 2; index++)
        {
            var leftBlock = Image("LeftBlock_" + index, parent, "Home/room_select_join_left_block");
            PositionSpriteTopLeft(leftBlock, index == 0 ? 163f : 251f, 118f, 125f);
        }
        for (var index = 0; index < 4; index++)
        {
            var middleBlock = Image("MiddleBlock_" + index, parent, "Home/room_select_join_middle_block");
            var middleBlockLeft = index == 0 ? 271f : index == 1 ? 343f : index == 2 ? 504f : 606f;
            var middleBlockWidth = index == 0 ? 74f : index == 1 ? 106f : 108f;
            PositionSpriteTopLeft(middleBlock, middleBlockLeft, 118f, middleBlockWidth);
        }
        for (var index = 0; index < 2; index++)
        {
            var rightBlock = Image("RightBlock_" + index, parent, "Home/room_select_join_right_block");
            PositionSpriteTopLeft(rightBlock, index == 0 ? 603f : 701f, 118f, 121f);
        }

        var middleMask = Image("MiddleMask", parent, "Home/room_select_join_middle_block_mask");
        PositionSpriteTopLeft(middleMask, 445f, 73f, 60f);
        var blank = Image("Blank", parent, "Home/room_select_join_blank");
        PositionSpriteTopLeftExact(blank, 445f, 79f, 60f, 60f);
        for (var index = 0; index < 4; index++)
        {
            var ban = Image("Ban_" + index, parent, "Home/room_select_join_ban");
            PositionSpriteTopLeft(ban, 456f + index % 2 * 22f, 94f + index / 2 * 19f, 13f);
        }
        var triangle = Image("Triangle", parent, "Home/room_select_join_triangle");
        PositionSpriteTopLeftExact(triangle, 462f, 60f, 28f, 13f);

        var logo = Image("Logo", parent, "Home/room_select_join_logo");
        PositionSpriteTopLeft(logo, 213f, 75f, 118f);
        var text01 = Image("Text01", parent, "Home/room_select_join_text_01");
        PositionSpriteTopLeft(text01, 513f, 67f, 65f);
        var text02 = Image("Text02", parent, "Home/room_select_join_text_02");
        PositionSpriteTopLeft(text02, 648f, 73f, 89f);

        roomCodeInput = Input("RoomCodeInput", parent, "输入同盟密钥", 30, "Home/room_select_join_text_bg");
        var roomCodePlaceholder = roomCodeInput.placeholder as Text;
        roomCodePlaceholder.alignment = TextAnchor.MiddleCenter;
        roomCodePlaceholder.fontSize = 28;
        roomCodePlaceholder.color = new Color(214f / 255f, 214f / 255f, 214f / 255f, 1f);
        roomCodeInput.characterLimit = LobbyRoomCode.Length;
        roomCodeInput.contentType = InputField.ContentType.IntegerNumber;
        roomCodeInput.textComponent.alignment = TextAnchor.MiddleCenter;
        PositionSpriteTopLeftExact(roomCodeInput.GetComponent<Image>(), 237f, 215f, 482f, 60f);
        roomCodeInput.onValueChanged.AddListener(_ => EvaluateJoinAvailability());

        joinButton = Button("JoinAction", parent, "Home/room_select_join_btn_bg_down", "加入同盟", 32, true);
        PositionBottomLeft(joinButton.GetComponent<RectTransform>(), actionRect);
        joinButton.GetComponent<Image>().preserveAspect = false;
        var joinLabel = joinButton.GetComponentInChildren<Text>();
        joinLabel.color = new Color(.12f, .06f, .01f);
        joinLabel.alignment = TextAnchor.MiddleLeft;
        joinLabel.rectTransform.offsetMin = new Vector2(103f, 2f);
        joinLabel.rectTransform.offsetMax = new Vector2(-220f, 2f);
        joinLabel.fontSize = 38;
        var joinIcon = Image("ActionIcon", joinButton.transform, "join_icon");
        PositionSpriteTopLeft(joinIcon, 47f, 19f, 47f);
        joinButton.onClick.AddListener(RequestJoin);
    }

    private void BuildRoom(Transform parent)
    {
        var layout = LanLobbyRoomLayout.ForSize(1920, 1080);
        roomLayout = layout;
        latencyText = Text("LocalLatency", parent, 28, TextAnchor.UpperLeft, new Color(.3f, .95f, .95f));
        PositionBottomLeft(latencyText.rectTransform, layout.Latency);
        latencyText.text = "0 ms";
        roomCodeText = Text("RoomCode", parent, 42, TextAnchor.UpperCenter, Color.white);
        Position(roomCodeText.rectTransform, new Vector2(.5f, .92f), new Vector2(500f, 70f));
        roomCodeText.text = "------";

        for (var index = 0; index < LobbyRoomSnapshot.MaximumMembers; index++)
        {
            var slot = BuildRoomSlot(parent, index, layout.Slots[index]);
            roomSlots.Add(slot);
            BindSlot(slot, RoomSlotPresentationState.Empty, null, false);
        }

        roomLeaveButton = Button("LeaveAction", parent, "img_return", string.Empty, 30, false);
        PositionBottomLeft(roomLeaveButton.GetComponent<RectTransform>(), layout.LeaveAction);
        roomLeaveButton.onClick.AddListener(RequestRoomLeave);

        roomPrimaryActionButton = Button("PrimaryAction", parent, "btn_match_grey", "协议启动", 30, true);
        PositionBottomLeft(roomPrimaryActionButton.GetComponent<RectTransform>(), layout.PrimaryAction);
        roomPrimaryActionButton.transition = Selectable.Transition.None;
        roomPrimaryActionLabel = roomPrimaryActionButton.GetComponentInChildren<Text>();
        roomPrimaryActionLabel.fontSize = 38;
        roomPrimaryActionLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        roomPrimaryActionIcon = Image("ActionIcon", roomPrimaryActionButton.transform, "btn_match_host_normal");
        roomPrimaryActionIcon.preserveAspect = false;
        PositionBottomLeft(roomPrimaryActionIcon.rectTransform, RelativeTo(layout.PrimaryIcon, layout.PrimaryAction));
        PositionBottomLeft(roomPrimaryActionLabel.rectTransform, RelativeTo(layout.PrimaryLabel, layout.PrimaryAction));
        roomPrimaryActionButton.onClick.AddListener(RequestRoomPrimaryAction);
    }

    private void RequestRoomLeave()
    {
        if (roomLeaveButton != null && roomLeaveButton.interactable) LeaveRequested?.Invoke();
    }

    private void RequestRoomPrimaryAction()
    {
        if (roomPrimaryActionButton == null || !roomPrimaryActionButton.interactable) return;
        if (boundLocalIsHost)
        {
            StartRequested?.Invoke();
            return;
        }

        ReadyRequested?.Invoke(!boundLocalMemberReady);
    }

    private static RoomSlotView BuildRoomSlot(
        Transform parent,
        int index,
        LanLobbyRoomSlotLayout layout)
    {
        var root = Rect("RoomCard_" + index, parent);
        PositionBottomLeft(root, layout.Root);

        var cardBody = Image("CardBody", root, "card_bg");
        PositionBottomLeft(cardBody.rectTransform, layout.CardBody);
        cardBody.preserveAspect = false;

        var topBar = Image("TopBar", root, "bg_top_normal");
        PositionBottomLeft(topBar.rectTransform, layout.TopBar);
        topBar.preserveAspect = true;

        var readyOverlay = Image("ReadyOverlay", root, "player_card_self_frame");
        PositionBottomLeft(readyOverlay.rectTransform, layout.StateOverlay);
        readyOverlay.preserveAspect = true;

        var emptyContentImage = Image("EmptyContent", root, "card_empty");
        PositionBottomLeft(emptyContentImage.rectTransform, layout.EmptyInvite);
        emptyContentImage.preserveAspect = true;

        var emptyInviteIcon = Image("EmptyInviteIcon", emptyContentImage.transform, "bg_plus");
        PositionSourceAspect(
            emptyInviteIcon,
            34f,
            (layout.EmptyInvite.Height - 72f * emptyInviteIcon.sprite.rect.height / emptyInviteIcon.sprite.rect.width) * .5f,
            72f);

        var emptyInviteLabel = Text("EmptyInviteLabel", emptyContentImage.transform, 25, TextAnchor.MiddleLeft, Color.white);
        PositionBottomLeft(emptyInviteLabel.rectTransform, new LanLobbyRect(122f, 61f, 110f, 34f));
        emptyInviteLabel.text = "邀请";

        var emptyInviteHint = Text(
            "EmptyInviteHint",
            emptyContentImage.transform,
            14,
            TextAnchor.MiddleLeft,
            new Color(.65f, .68f, .7f));
        PositionBottomLeft(emptyInviteHint.rectTransform, new LanLobbyRect(122f, 25f, 190f, 32f));
        emptyInviteHint.text = "复制同盟密钥以邀请队友";

        var occupiedContent = Rect("OccupiedContent", root);
        Stretch(occupiedContent);

        var readyIcon = Image("ReadyIcon", occupiedContent, "player_card_ready");
        PositionBottomLeft(readyIcon.rectTransform, layout.ReadyIcon);
        readyIcon.preserveAspect = true;

        var readyLabel = Text("ReadyLabel", occupiedContent, 28, TextAnchor.MiddleLeft, Color.black);
        readyLabel.text = "已就绪";
        PositionPreferredText(readyLabel, layout.ReadyLabel);

        var lowerDecoration = Image("LowerDecoration", root, "card_deco_self");
        PositionBottomLeft(lowerDecoration.rectTransform, layout.LowerDecoration);
        lowerDecoration.preserveAspect = true;

        var creatorTag = Image("CreatorTag", root, "host_top_tag");
        PositionBottomLeft(creatorTag.rectTransform, layout.CreatorTag);
        creatorTag.preserveAspect = false;

        return new RoomSlotView
        {
            Root = root,
            CardBody = cardBody,
            CardBodyLayout = layout.CardBody,
            TopBar = topBar,
            WaitingTopBarLayout = layout.TopBar,
            ReadyTopBarLayout = layout.ReadyTopBar,
            ReadyOverlay = readyOverlay,
            EmptyContent = emptyContentImage.gameObject,
            EmptyInviteIcon = emptyInviteIcon,
            EmptyInviteLabel = emptyInviteLabel,
            EmptyInviteHint = emptyInviteHint,
            OccupiedContent = occupiedContent.gameObject,
            ReadyIcon = readyIcon,
            ReadyLabel = readyLabel,
            LowerDecoration = lowerDecoration,
            CreatorTag = creatorTag
        };
    }

    private static void BindSlot(
        RoomSlotView slot,
        RoomSlotPresentationState state,
        LobbyMemberSnapshot member,
        bool isHostSlot)
    {
        var isEmpty = state == RoomSlotPresentationState.Empty;
        var isReady = state == RoomSlotPresentationState.Ready;
        slot.CardBody.sprite = Sprite("card_bg");
        slot.CardBody.color = isReady
            ? new Color(0f, 220f / 255f, 220f / 255f, 1f)
            : Color.white;
        PositionBottomLeft(slot.CardBody.rectTransform, slot.CardBodyLayout);
        slot.CardBody.rectTransform.localScale = isReady
            ? new Vector3(1f, -1f, 1f)
            : Vector3.one;
        if (isReady)
        {
            slot.CardBody.rectTransform.anchoredPosition += Vector2.up * slot.CardBodyLayout.Height;
        }
        slot.TopBar.sprite = Sprite(isReady ? "bg_top_ready" : "bg_top_normal");
        PositionBottomLeft(slot.TopBar.rectTransform, isReady ? slot.ReadyTopBarLayout : slot.WaitingTopBarLayout);
        slot.TopBar.preserveAspect = !isReady;
        slot.ReadyOverlay.sprite = Sprite("player_card_self_frame");
        slot.EmptyContent.GetComponent<Image>().sprite = Sprite("card_empty");
        slot.EmptyInviteIcon.sprite = Sprite("bg_plus");
        slot.ReadyIcon.sprite = Sprite("player_card_ready");
        slot.LowerDecoration.sprite = Sprite(isReady ? "card_deco_self" : "card_deco_bg");
        slot.LowerDecoration.color = isReady ? Color.white : new Color(.5f, .5f, .5f, 1f);
        slot.CreatorTag.sprite = Sprite("host_top_tag");
        slot.EmptyInviteLabel.text = "邀请";
        slot.EmptyInviteHint.text = "复制同盟密钥以邀请队友";
        slot.ReadyLabel.text = "已就绪";

        slot.CardBody.gameObject.SetActive(true);
        slot.TopBar.gameObject.SetActive(true);
        slot.ReadyOverlay.gameObject.SetActive(isReady);
        slot.EmptyContent.SetActive(isEmpty);
        slot.OccupiedContent.SetActive(isReady);
        slot.ReadyIcon.gameObject.SetActive(isReady);
        slot.ReadyLabel.gameObject.SetActive(isReady);
        slot.LowerDecoration.gameObject.SetActive(true);
        slot.CreatorTag.gameObject.SetActive(member != null && isHostSlot);
        ResizeToPreferredText(slot.ReadyLabel);
    }

    private void RebuildDiscoveryItems()
    {
        if (discoveryItemsRoot == null) return;
        for (var index = discoveryItemsRoot.childCount - 1; index >= 0; index--) Destroy(discoveryItemsRoot.GetChild(index).gameObject);
        var entries = new List<LobbyDiscoveryEntry>(discoveries.Values);
        entries.Sort((left, right) => string.CompareOrdinal(left.RoomCode, right.RoomCode));
        discoveryOverflowCount = Math.Max(0, entries.Count - MaximumVisibleDiscoveryRooms);
        discoveryOverflowText.text = discoveryOverflowCount > 0 ? "+" + discoveryOverflowCount + " MORE ROOMS" : string.Empty;
        var visibleCount = Math.Min(entries.Count, MaximumVisibleDiscoveryRooms);
        for (var index = 0; index < visibleCount; index++)
        {
            var entry = entries[index];
            var label = entry.RoomCode + "  " + entry.HostDisplayName + "  " + entry.MemberCount + "/" + entry.Capacity;
            var item = Button("Room_" + entry.RoomCode, discoveryItemsRoot, "player_card_waiting", label, 22);
            item.GetComponent<LayoutElement>().preferredHeight = 54f;
            var code = entry.RoomCode;
            item.onClick.AddListener(() => SetRoomCode(code));
        }
    }

    private void SetRoomCode(string roomCode)
    {
        roomCodeInput.text = roomCode ?? string.Empty;
        EvaluateJoinAvailability();
    }

    private void RequestJoin()
    {
        EvaluateJoinAvailability();
        if (joinButton != null && joinButton.interactable) JoinRequested?.Invoke(roomCodeInput.text);
    }

    private void EvaluateJoinAvailability()
    {
        if (joinButton == null || roomCodeInput == null) return;
        var code = roomCodeInput.text;
        if (!LobbyRoomCode.IsValid(code))
        {
            joinButton.interactable = false;
            SetStatus("Enter a six-digit room code.");
            return;
        }

        if (!discoveries.TryGetValue(code, out var entry))
        {
            joinButton.interactable = false;
            SetStatus("Room not found.");
            return;
        }

        if (entry.MemberCount >= entry.Capacity)
        {
            joinButton.interactable = false;
            SetStatus("Room is full.");
            return;
        }

        if (!entry.IsJoinable)
        {
            joinButton.interactable = false;
            SetStatus("Room has started.");
            return;
        }

        joinButton.interactable = true;
        SetStatus("Room ready to join.");
    }

    private void AddIcon(Transform parent, string spriteName)
    {
        var icon = Image("Icon", parent, spriteName);
        Position(icon.rectTransform, new Vector2(.15f, .5f), new Vector2(80f, 80f));
        icon.preserveAspect = true;
    }

    private void ChangeAvatar(int direction)
    {
        var count = LobbyProfile.MaximumAvatarIndex - LobbyProfile.MinimumAvatarIndex + 1;
        avatarIndex = (avatarIndex - LobbyProfile.MinimumAvatarIndex + direction + count) % count + LobbyProfile.MinimumAvatarIndex;
        RefreshAvatarIndex();
    }

    private void RefreshAvatarIndex()
    {
        if (avatarImage != null) avatarImage.sprite = Sprite("Home/" + AvatarSpriteName(avatarIndex));
        if (avatarIndexText != null) avatarIndexText.text = "AVATAR " + (avatarIndex + 1);
    }

    private static string AvatarSpriteName(int index)
    {
        switch (index)
        {
            case 0: return "icon_amiy";
            case 1: return "icon_clementi";
            case 2: return "icon_kirar";
            default: return "icon_zumam";
        }
    }

    private static Button Button(string name, Transform parent, string spriteName, string label, int fontSize, bool preserveAspect = false)
    {
        var value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        value.transform.SetParent(parent, false);
        var image = value.GetComponent<Image>();
        image.sprite = Sprite(spriteName);
        image.preserveAspect = preserveAspect;
        var button = value.GetComponent<Button>();
        button.targetGraphic = image;
        var text = Text("Label", value.transform, fontSize, TextAnchor.MiddleCenter, Color.white);
        Stretch(text.rectTransform);
        text.text = label;
        return button;
    }

    private static InputField Input(string name, Transform parent, string placeholder, int fontSize, string spriteName = "img_player_bkg")
    {
        var value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(InputField));
        value.transform.SetParent(parent, false);
        value.GetComponent<Image>().sprite = string.IsNullOrEmpty(spriteName) ? null : Sprite(spriteName);
        var text = Text("Text", value.transform, fontSize, TextAnchor.MiddleLeft, Color.white);
        Stretch(text.rectTransform);
        text.rectTransform.offsetMin = new Vector2(18f, 0f);
        text.rectTransform.offsetMax = new Vector2(-18f, 0f);
        var hint = Text("Placeholder", value.transform, fontSize - 6, TextAnchor.MiddleLeft, new Color(.6f, .7f, .7f, .8f));
        Stretch(hint.rectTransform);
        hint.rectTransform.offsetMin = new Vector2(18f, 0f);
        hint.rectTransform.offsetMax = new Vector2(-18f, 0f);
        hint.text = placeholder;
        var input = value.GetComponent<InputField>();
        input.textComponent = text;
        input.placeholder = hint;
        return input;
    }

    private static Image Image(string name, Transform parent, string spriteName)
    {
        var value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        value.transform.SetParent(parent, false);
        var image = value.GetComponent<Image>();
        image.sprite = Sprite(spriteName);
        image.raycastTarget = false;
        return image;
    }

    private static Text Text(string name, Transform parent, int fontSize, TextAnchor alignment, Color color)
    {
        var value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        value.transform.SetParent(parent, false);
        var text = value.GetComponent<Text>();
        text.font = Resources.Load<Font>(FontPath);
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static Sprite Sprite(string name)
    {
        return Resources.Load<Sprite>(SpriteRoot + name);
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var value = new GameObject(name, typeof(RectTransform));
        value.transform.SetParent(parent, false);
        return value.GetComponent<RectTransform>();
    }

    private static void Stretch(RectTransform value)
    {
        value.anchorMin = Vector2.zero;
        value.anchorMax = Vector2.one;
        value.offsetMin = Vector2.zero;
        value.offsetMax = Vector2.zero;
    }

    private static void Position(RectTransform value, Vector2 anchor, Vector2 size)
    {
        value.anchorMin = value.anchorMax = anchor;
        value.pivot = new Vector2(.5f, .5f);
        value.anchoredPosition = Vector2.zero;
        value.sizeDelta = size;
    }

    private static void PositionBottomLeft(RectTransform value, LanLobbyRect rect)
    {
        value.anchorMin = value.anchorMax = Vector2.zero;
        value.pivot = Vector2.zero;
        value.anchoredPosition = new Vector2(rect.Left, rect.Bottom);
        value.sizeDelta = new Vector2(rect.Width, rect.Height);
    }

    private static void PositionSourceAspect(Image image, float left, float bottom, float width)
    {
        if (image == null || image.sprite == null)
            throw new InvalidOperationException("Sprite must be assigned before source-aspect placement.");

        var height = width * image.sprite.rect.height / image.sprite.rect.width;
        PositionBottomLeft(image.rectTransform, new LanLobbyRect(left, bottom, width, height));
        image.preserveAspect = true;
    }

    private static void PositionPreferredText(Text text, LanLobbyRect anchor)
    {
        PositionBottomLeft(text.rectTransform, new LanLobbyRect(anchor.Left, anchor.Bottom, 0f, 0f));
        ResizeToPreferredText(text);
    }

    private static void ResizeToPreferredText(Text text)
    {
        text.rectTransform.sizeDelta = new Vector2(text.preferredWidth, text.preferredHeight);
    }

    private static LanLobbyRect RelativeTo(LanLobbyRect child, LanLobbyRect parent)
    {
        return new LanLobbyRect(child.Left - parent.Left, child.Bottom - parent.Bottom, child.Width, child.Height);
    }

    private static void PositionSprite(Image value, Vector2 anchor, float width)
    {
        var sprite = value == null ? null : value.sprite;
        if (sprite == null) throw new InvalidOperationException("Room-select sprite must be assigned before positioning.");
        var height = width * sprite.rect.height / sprite.rect.width;
        Position(value.rectTransform, anchor, new Vector2(width, height));
        value.preserveAspect = true;
    }

    private static void PositionSpriteTopLeft(Image value, float left, float top, float width)
    {
        var sprite = value == null ? null : value.sprite;
        if (sprite == null) throw new InvalidOperationException("Room-select sprite must be assigned before positioning.");
        var rect = value.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, width * sprite.rect.height / sprite.rect.width);
        value.preserveAspect = true;
    }

    private static void PositionSpriteTopLeftExact(
        Image value,
        float left,
        float top,
        float width,
        float height,
        bool preserveAspect = false)
    {
        if (value == null || value.sprite == null)
            throw new InvalidOperationException("Room-select sprite must be assigned before positioning.");

        var rect = value.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
        value.preserveAspect = preserveAspect;
    }

    private static Image CreateSolidDecorationPanel(
        string name,
        Transform parent,
        float left,
        float top,
        float width,
        float height,
        Color color)
    {
        var value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        value.transform.SetParent(parent, false);
        var image = value.GetComponent<Image>();
        image.sprite = null;
        image.material = null;
        image.color = color;
        image.raycastTarget = false;
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
        return image;
    }

    private static Image CreateOrientedDecorationSprite(
        string name,
        Transform parent,
        string spriteName,
        float centerX,
        float centerTop,
        float width,
        float height,
        float rotation,
        bool preserveAspect,
        Color color)
    {
        var image = Image(name, parent, spriteName);
        var rect = image.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = new Vector2(centerX, -centerTop);
        rect.sizeDelta = new Vector2(width, height);
        rect.localEulerAngles = new Vector3(0f, 0f, rotation);
        image.preserveAspect = preserveAspect;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }
}
