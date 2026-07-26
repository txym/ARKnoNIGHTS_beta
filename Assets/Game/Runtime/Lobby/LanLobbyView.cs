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

    private readonly Dictionary<string, LobbyDiscoveryEntry> discoveries = new Dictionary<string, LobbyDiscoveryEntry>(StringComparer.Ordinal);
    private readonly List<Image> roomCardImages = new List<Image>();
    private readonly List<Text> roomCardTexts = new List<Text>();
    private readonly List<Text> roomCardAvatarTexts = new List<Text>();
    private Canvas canvas;
    private RectTransform homeRoot;
    private RectTransform roomRoot;
    private InputField profileNameInput;
    private Text avatarIndexText;
    private Image avatarImage;
    private InputField roomCodeInput;
    private Button joinButton;
    private Text statusText;
    private RectTransform discoveryItemsRoot;
    private Text discoveryOverflowText;
    private Text latencyText;
    private Text roomCodeText;
    private Button readyButton;
    private Button startButton;
    private string localPlayerId;
    private int avatarIndex;
    private int readyCardCount;
    private int discoveryOverflowCount;

    public event Action CreateRequested;
    public event Action<string> JoinRequested;
    public event Action<LobbyProfile> ProfileSaved;
    public event Action<bool> ReadyRequested;
    public event Action LeaveRequested;
    public event Action StartRequested;

    public string RoomCodeTextForTests => roomCodeInput == null ? string.Empty : roomCodeInput.text;
    public bool JoinInteractableForTests => joinButton != null && joinButton.interactable;
    public string StatusTextForTests => statusText == null ? string.Empty : statusText.text;
    public int RoomCardCountForTests => roomCardImages.Count;
    public int ReadyCardCountForTests => readyCardCount;
    public string LocalLatencyTextForTests => latencyText == null ? string.Empty : latencyText.text;
    public int CanvasSortOrderForTests => canvas == null ? -1 : canvas.sortingOrder;
    public bool ReadyInteractableForTests => readyButton != null && readyButton.interactable;
    public bool StartInteractableForTests => startButton != null && startButton.interactable;
    public int DiscoveryRenderedItemCountForTests => discoveryItemsRoot == null ? 0 : discoveryItemsRoot.childCount;
    public int DiscoveryOverflowCountForTests => discoveryOverflowCount;
    public string RoomCardAvatarTextForTests(int index)
    {
        return index >= 0 && index < roomCardAvatarTexts.Count ? roomCardAvatarTexts[index].text : string.Empty;
    }

    private void Awake()
    {
        Build();
        ShowHome();
    }

    public void ShowHome()
    {
        if (homeRoot != null) homeRoot.gameObject.SetActive(true);
        if (roomRoot != null) roomRoot.gameObject.SetActive(false);
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
        for (var index = 0; index < roomCardImages.Count; index++)
        {
            var hasMember = room != null && index < room.Members.Count;
            var member = hasMember ? room.Members[index] : null;
            var ready = member != null && member.IsReady;
            roomCardImages[index].sprite = Sprite(ready ? "player_card_ready" : "player_card_waiting");
            roomCardImages[index].enabled = true;
            roomCardTexts[index].text = hasMember
                ? member.Profile.DisplayName + (member.PlayerId == room.HostPlayerId ? "  HOST" : string.Empty) + "\n" + (ready ? "READY" : "WAITING") + "  " + member.LatencyMilliseconds + " ms"
                : "OPEN SLOT";
            roomCardAvatarTexts[index].gameObject.SetActive(hasMember);
            if (hasMember)
            {
                roomCardAvatarTexts[index].text = "A" + (member.Profile.AvatarIndex + 1);
                roomCardAvatarTexts[index].color = AvatarColor(member.Profile.AvatarIndex);
            }
            if (ready) readyCardCount++;
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

        readyButton.interactable = room != null && !room.HasStarted && hasLocalMember;
        startButton.interactable = room != null && !room.HasStarted && localIsHost && room.Members.Count > 0 && readyCardCount == room.Members.Count;
        readyButton.GetComponentInChildren<Text>().text = localMemberReady ? "UNREADY" : "READY";
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
        var foreground = Image("GridForeground", root, "shallow_main");
        Stretch(foreground.rectTransform);
        foreground.preserveAspect = false;
        foreground.raycastTarget = false;

        homeRoot = Rect("Home", root);
        Stretch(homeRoot);
        roomRoot = Rect("Room", root);
        Stretch(roomRoot);
        BuildHome(homeRoot);
        BuildRoom(roomRoot);
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
        var startRoom = Image("StartRoomDecoration", roomSelect, "Home/room_select_img_startroom");
        PositionSprite(startRoom, new Vector2(.9f, .84f), 128f);

        var create = Rect("Create", roomSelect);
        PositionBottomLeft(create, layout.RoomSelectCreate);
        var createHitTarget = create.gameObject.AddComponent<Image>();
        createHitTarget.color = Color.clear;
        createHitTarget.raycastTarget = true;
        var createArea = create.gameObject.AddComponent<Button>();
        createArea.targetGraphic = createHitTarget;
        createArea.onClick.AddListener(() => CreateRequested?.Invoke());
        BuildCreateSection(create);

        var join = Rect("Join", roomSelect);
        PositionBottomLeft(join, layout.RoomSelectJoin);
        BuildJoinSection(join);

        var discovered = Image("DiscoveredRooms", roomSelect, "Home/room_select_join_text_bg");
        PositionSprite(discovered, new Vector2(.751f, .125f), 770f);
        discoveryItemsRoot = Rect("Items", discovered.transform);
        Stretch(discoveryItemsRoot);
        discoveryItemsRoot.offsetMin = new Vector2(28f, 15f);
        discoveryItemsRoot.offsetMax = new Vector2(-28f, -15f);
        var itemLayout = discoveryItemsRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        itemLayout.spacing = 8f;
        itemLayout.childAlignment = TextAnchor.UpperCenter;
        itemLayout.childControlWidth = true;
        itemLayout.childForceExpandHeight = false;
        discoveryOverflowText = Text("Overflow", roomSelect, 18, TextAnchor.LowerRight, new Color(.3f, .95f, .95f));
        discoveryOverflowText.rectTransform.anchorMin = new Vector2(0f, 0f);
        discoveryOverflowText.rectTransform.anchorMax = new Vector2(1f, 0f);
        discoveryOverflowText.rectTransform.pivot = new Vector2(.5f, 0f);
        discoveryOverflowText.rectTransform.anchoredPosition = new Vector2(0f, 25f);
        discoveryOverflowText.rectTransform.sizeDelta = new Vector2(-130f, 30f);

        statusText = Text("Status", roomSelect, 20, TextAnchor.MiddleCenter, Color.white);
        Position(statusText.rectTransform, new Vector2(.751f, .19f), new Vector2(700f, 36f));
        statusText.text = "DISCOVERING LOCAL ROOMS";
    }

    private void BuildCreateSection(RectTransform parent)
    {
        var line = Image("LeftLine", parent, "Home/room_select_create_left_line");
        PositionSprite(line, new Vector2(.08f, .5f), 24f);
        var logo = Image("Logo", parent, "Home/room_select_create_logo");
        PositionSprite(logo, new Vector2(.25f, .76f), 172f);
        var middle = Image("MiddleIcon", parent, "Home/room_select_create_middleicon");
        PositionSprite(middle, new Vector2(.29f, .46f), 126f);
        var text01 = Image("Text01", parent, "Home/room_select_create_text_01");
        PositionSprite(text01, new Vector2(.47f, .67f), 126f);
        var text02 = Image("Text02", parent, "Home/room_select_create_text_02");
        PositionSprite(text02, new Vector2(.47f, .33f), 92f);
        var create = Button("CreateAction", parent, "Home/room_select_create_btn_bg_down", "创建同盟", 32, true);
        PositionSprite(create.GetComponent<Image>(), new Vector2(.74f, .5f), 400f);
        create.onClick.AddListener(() => CreateRequested?.Invoke());
    }

    private void BuildJoinSection(RectTransform parent)
    {
        var leftBlock = Image("LeftBlock", parent, "Home/room_select_join_left_block");
        PositionSprite(leftBlock, new Vector2(.1f, .5f), 150f);
        var middleBlock = Image("MiddleBlock", parent, "Home/room_select_join_middle_block");
        PositionSprite(middleBlock, new Vector2(.29f, .5f), 129f);
        var middleMask = Image("MiddleBlockMask", parent, "Home/room_select_join_middle_block_mask");
        PositionSprite(middleMask, new Vector2(.365f, .5f), 96f);
        var rightBlock = Image("RightBlock", parent, "Home/room_select_join_right_block");
        PositionSprite(rightBlock, new Vector2(.52f, .5f), 146f);
        var logo = Image("Logo", parent, "Home/room_select_join_logo");
        PositionSprite(logo, new Vector2(.11f, .75f), 172f);
        var textBackground = Image("TextBackground", parent, "Home/room_select_join_text_bg");
        PositionSprite(textBackground, new Vector2(.39f, .74f), 278f);
        var text01 = Image("Text01", parent, "Home/room_select_join_text_01");
        PositionSprite(text01, new Vector2(.39f, .74f), 141f);
        var text02 = Image("Text02", parent, "Home/room_select_join_text_02");
        PositionSprite(text02, new Vector2(.39f, .26f), 132f);
        var triangle = Image("Triangle", parent, "Home/room_select_join_triangle");
        PositionSprite(triangle, new Vector2(.56f, .5f), 48f);
        for (var index = 0; index < LobbyRoomCode.Length; index++)
        {
            var blank = Image("Blank_" + index, parent, "Home/room_select_join_blank");
            PositionSprite(blank, new Vector2(.345f + index * .052f, .5f), 43f);
        }
        roomCodeInput = Input("RoomCodeInput", parent, "输入六位同盟密钥", 30, null);
        roomCodeInput.characterLimit = LobbyRoomCode.Length;
        roomCodeInput.contentType = InputField.ContentType.IntegerNumber;
        roomCodeInput.GetComponent<Image>().color = Color.clear;
        roomCodeInput.textComponent.alignment = TextAnchor.MiddleCenter;
        Position(roomCodeInput.GetComponent<RectTransform>(), new Vector2(.475f, .5f), new Vector2(258f, 50f));
        roomCodeInput.onValueChanged.AddListener(_ => EvaluateJoinAvailability());
        var ban = Image("Ban", parent, "Home/room_select_join_ban");
        PositionSprite(ban, new Vector2(.59f, .5f), 24f);
        joinButton = Button("JoinAction", parent, "Home/room_select_join_btn_bg_down", "加入同盟", 32, true);
        PositionSprite(joinButton.GetComponent<Image>(), new Vector2(.74f, .5f), 400f);
        joinButton.onClick.AddListener(RequestJoin);
    }

    private void BuildRoom(Transform parent)
    {
        latencyText = Text("LocalLatency", parent, 28, TextAnchor.UpperLeft, new Color(.3f, .95f, .95f));
        latencyText.rectTransform.anchorMin = latencyText.rectTransform.anchorMax = new Vector2(0f, 1f);
        latencyText.rectTransform.pivot = new Vector2(0f, 1f);
        latencyText.rectTransform.anchoredPosition = new Vector2(40f, -34f);
        latencyText.rectTransform.sizeDelta = new Vector2(260f, 50f);
        latencyText.text = "0 ms";
        roomCodeText = Text("RoomCode", parent, 42, TextAnchor.UpperCenter, Color.white);
        Position(roomCodeText.rectTransform, new Vector2(.5f, .92f), new Vector2(500f, 70f));
        roomCodeText.text = "------";

        for (var index = 0; index < LobbyRoomSnapshot.MaximumMembers; index++)
        {
            var card = Image("RoomCard_" + index, parent, "player_card_waiting");
            Position(card.rectTransform, new Vector2(.16f + index * .227f, .53f), new Vector2(390f, 430f));
            roomCardImages.Add(card);
            var text = Text("Member", card.transform, 25, TextAnchor.MiddleCenter, Color.white);
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(28f, 36f);
            text.rectTransform.offsetMax = new Vector2(-28f, -36f);
            text.text = "OPEN SLOT";
            roomCardTexts.Add(text);
            var avatarFrame = Image("AvatarFrame", card.transform, "team_icon_frame");
            Position(avatarFrame.rectTransform, new Vector2(.5f, .76f), new Vector2(86f, 86f));
            avatarFrame.preserveAspect = true;
            var avatarText = Text("Avatar", avatarFrame.transform, 20, TextAnchor.MiddleCenter, Color.white);
            Stretch(avatarText.rectTransform);
            avatarText.text = string.Empty;
            roomCardAvatarTexts.Add(avatarText);
        }

        readyButton = Button("Ready", parent, "btn_match_grey", "READY", 30);
        Position(readyButton.GetComponent<RectTransform>(), new Vector2(.38f, .14f), new Vector2(300f, 92f));
        readyButton.onClick.AddListener(() => ReadyRequested?.Invoke(readyButton.GetComponentInChildren<Text>().text == "READY"));
        var leave = Button("Leave", parent, "btn_match_cancel", "LEAVE", 30);
        Position(leave.GetComponent<RectTransform>(), new Vector2(.5f, .14f), new Vector2(300f, 92f));
        leave.onClick.AddListener(() => LeaveRequested?.Invoke());
        startButton = Button("Start", parent, "btn_match_host_grey", "START", 30);
        Position(startButton.GetComponent<RectTransform>(), new Vector2(.62f, .14f), new Vector2(300f, 92f));
        startButton.onClick.AddListener(() => StartRequested?.Invoke());
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

    private static Color AvatarColor(int value)
    {
        switch (value % 4)
        {
            case 0: return new Color(.3f, .95f, .95f);
            case 1: return new Color(1f, .72f, .25f);
            case 2: return new Color(.7f, .5f, 1f);
            default: return new Color(.45f, 1f, .55f);
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

    private static void PositionSprite(Image value, Vector2 anchor, float width)
    {
        var sprite = value == null ? null : value.sprite;
        if (sprite == null) throw new InvalidOperationException("Room-select sprite must be assigned before positioning.");
        var height = width * sprite.rect.height / sprite.rect.width;
        Position(value.rectTransform, anchor, new Vector2(width, height));
        value.preserveAspect = true;
    }
}
