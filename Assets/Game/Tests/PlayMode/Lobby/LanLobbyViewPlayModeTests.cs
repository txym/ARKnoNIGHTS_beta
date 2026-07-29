using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ArknoNights.Lobby;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ArknoNights.Lobby.Tests
{
    public sealed class LanLobbyViewPlayModeTests
    {
        private GameObject root;
        private global::LanLobbyView view;
        private int joinRequests;
        private readonly List<Canvas> canvasesDisabledForIsolation = new List<Canvas>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            joinRequests = 0;
            foreach (var existingView in Object.FindObjectsOfType<global::LanLobbyView>())
            {
                var existingCanvas = existingView.GetComponent<Canvas>();
                if (existingCanvas == null || !existingCanvas.enabled) continue;
                existingCanvas.enabled = false;
                canvasesDisabledForIsolation.Add(existingCanvas);
            }
            root = new GameObject("LanLobbyViewTests");
            view = root.AddComponent<global::LanLobbyView>();
            view.JoinRequested += OnJoinRequested;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (root != null) Object.Destroy(root);
            foreach (var eventSystem in Object.FindObjectsOfType<UnityEngine.EventSystems.EventSystem>())
            {
                if (eventSystem.name == "LanLobbyEventSystem") Object.Destroy(eventSystem.gameObject);
            }
            foreach (var canvas in canvasesDisabledForIsolation)
            {
                if (canvas != null) canvas.enabled = true;
            }
            canvasesDisabledForIsolation.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ClickingDiscoveredRoom_PrefillsButDoesNotJoin()
        {
            view.BindDiscoveredRooms(new[] { Discovery("654321") });

            view.ClickDiscoveredRoomForTests("654321");

            Assert.That(view.RoomCodeTextForTests, Is.EqualTo("654321"));
            Assert.That(joinRequests, Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HomeRoomSelect_ComposesMappedSpritesAndCombinedAvatar()
        {
            var home = view.transform.Find("LanLobbyRoot/Home");
            var create = home.Find("RoomSelect/Create");
            var join = home.Find("RoomSelect/Join");
            var layout = global::LanLobbyLayout.ForSize(1920, 1080, 4);

            Assert.That(create, Is.Not.Null);
            Assert.That(join, Is.Not.Null);
            Assert.That(create.GetComponent<RectTransform>().anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(join.GetComponent<RectTransform>().anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(create.GetComponent<RectTransform>().pivot, Is.EqualTo(Vector2.zero));
            Assert.That(join.GetComponent<RectTransform>().pivot, Is.EqualTo(Vector2.zero));
            Assert.That(create.GetComponent<RectTransform>().anchoredPosition, Is.EqualTo(new Vector2(layout.RoomSelectCreate.Left, layout.RoomSelectCreate.Bottom)));
            Assert.That(join.GetComponent<RectTransform>().anchoredPosition, Is.EqualTo(new Vector2(layout.RoomSelectJoin.Left, layout.RoomSelectJoin.Bottom)));
            Assert.That(create.GetComponent<RectTransform>().sizeDelta, Is.EqualTo(new Vector2(layout.RoomSelectCreate.Width, layout.RoomSelectCreate.Height)));
            Assert.That(join.GetComponent<RectTransform>().sizeDelta, Is.EqualTo(new Vector2(layout.RoomSelectJoin.Width, layout.RoomSelectJoin.Height)));
            Assert.That(home.Find("IdentityPanel/AvatarSelector/AvatarImage").GetComponent<UnityEngine.UI.Image>().sprite.name,
                Is.EqualTo("icon_amiy"));
            Assert.That(view.transform.Find("LanLobbyRoot/GridForeground").gameObject.activeSelf, Is.False,
                "The legacy shallow_main HUD overlay must not be stretched over the room-select Home.");
            AssertMappedRoomSelectSprites(home);
            Assert.That(join.Find("MiddleBlockMask"), Is.Null, "The source mask is a compositor mask, not a visible Home Image.");
            Assert.That(ChildrenWithPrefix(create, "Line").Count, Is.EqualTo(2));
            Assert.That(ChildrenWithPrefix(join, "LeftBlock_").Count, Is.EqualTo(2));
            Assert.That(ChildrenWithPrefix(join, "MiddleBlock_").Count, Is.EqualTo(4));
            Assert.That(ChildrenWithPrefix(join, "RightBlock_").Count, Is.EqualTo(2));
            Assert.That(join.Find("MiddleMask"), Is.Not.Null);
            Assert.That(join.Find("Blank"), Is.Not.Null);
            Assert.That(ChildrenWithPrefix(join, "Ban_").Count, Is.EqualTo(4));
            for (var index = 0; index < LobbyRoomCode.Length; index++)
                Assert.That(join.Find("Blank_" + index), Is.Null);

            view.BindDiscoveredRooms(new[] { Discovery("654321") });
            view.ClickDiscoveredRoomForTests("654321");
            Assert.That(view.RoomCodeTextForTests, Is.EqualTo("654321"));
            Assert.That(joinRequests, Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RoomPage_DoesNotActivateTheStretchDistortedLegacyGridForeground()
        {
            view.ShowRoom(HostOnlyRoom("654321"), "host");

            Assert.That(view.transform.Find("LanLobbyRoot/GridForeground").gameObject.activeSelf, Is.False,
                "shallow_main is a 49x65 legacy overlay and must remain inactive on the Room page.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator RoomView_KeepsLegacyForegroundInactiveAndHomeAddsCompleteRoomSelectAffordances()
        {
            var home = view.transform.Find("LanLobbyRoot/Home");
            var room = view.transform.Find("LanLobbyRoot/Room");
            var grid = view.transform.Find("LanLobbyRoot/GridForeground");

            Assert.That(grid.gameObject.activeSelf, Is.False);
            view.ShowRoom();
            Assert.That(room.gameObject.activeSelf, Is.True);
            Assert.That(grid.gameObject.activeSelf, Is.False);
            view.ShowHome();
            Assert.That(grid.gameObject.activeSelf, Is.False);

            Assert.That(home.Find("RoomSelect/PanelFrame"), Is.Null);
            Assert.That(home.Find("RoomSelect/Create/CreateFrame/Top_0"), Is.Not.Null);
            Assert.That(home.Find("RoomSelect/Create/CreateFrame/Bottom_2"), Is.Null);
            Assert.That(home.Find("RoomSelect/Create/CreateFrame/TopRightChamfer"), Is.Null);
            Assert.That(home.Find("RoomSelect/Join/SimulationInvite"), Is.Null);
            Assert.That(home.Find("RoomSelect/Create/CreateAction/ActionIcon").GetComponent<UnityEngine.UI.Image>().sprite.name,
                Is.EqualTo("create_icon"));
            Assert.That(home.Find("RoomSelect/Join/JoinAction/ActionIcon").GetComponent<UnityEngine.UI.Image>().sprite.name,
                Is.EqualTo("join_icon"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator HomeRoomSelect_CreateDecorationUsesMeasuredRepeatedSpritesBehindFrozenAction()
        {
            var home = view.transform.Find("LanLobbyRoot/Home");
            var layout = global::LanLobbyLayout.ForSize(1920, 1080, 4);
            var create = home.Find("RoomSelect/Create").GetComponent<RectTransform>();
            var join = home.Find("RoomSelect/Join").GetComponent<RectTransform>();
            var background = home.Find("RoomSelect/RightBackground").GetComponent<UnityEngine.UI.Image>();
            var createAction = create.Find("CreateAction");
            var joinAction = join.Find("JoinAction");
            var createActionRect = createAction.GetComponent<RectTransform>();
            var joinActionRect = joinAction.GetComponent<RectTransform>();
            var createActionImage = createAction.GetComponent<UnityEngine.UI.Image>();
            var joinActionImage = joinAction.GetComponent<UnityEngine.UI.Image>();
            var expected = new[]
            {
                new OrientedDecorationExpectation("Wings/WingLeftUpper", "img_pointer", 330f, 86f, 108f, 108f * 23f / 324f, 162f, true),
                new OrientedDecorationExpectation("Wings/WingLeftLower", "img_pointer", 330f, 147f, 108f, 108f * 23f / 324f, 198f, true),
                new OrientedDecorationExpectation("Wings/WingRightUpper", "img_pointer", 603f, 86f, 108f, 108f * 23f / 324f, 18f, true),
                new OrientedDecorationExpectation("Wings/WingRightLower", "img_pointer", 603f, 147f, 108f, 108f * 23f / 324f, 342f, true),
                new OrientedDecorationExpectation("DotTopLeft", "room_select_dot", 390.5f, 30.5f, 17f, 17f, 0f, true),
                new OrientedDecorationExpectation("DotTopRight", "room_select_dot", 525.5f, 31.5f, 17f, 17f, 0f, true),
                new OrientedDecorationExpectation("DotBottomLeft", "room_select_dot", 390f, 167f, 16f, 16f, 0f, true),
                new OrientedDecorationExpectation("DotBottomRight", "room_select_dot", 525.5f, 167.5f, 17f, 17f, 0f, true),
                new OrientedDecorationExpectation("LineLeft", "room_select_create_left_line", 402.1f, 89f, 16.2f, 54f, 0f, true),
                new OrientedDecorationExpectation("LineRight", "room_select_create_left_line", 515.1f, 89f, 16.2f, 54f, 180f, true),
                new OrientedDecorationExpectation("MiddleIcon", "room_select_create_middleicon", 459.5f, 87.81f, 87f, 87f * 62f / 63f, 0f, true),
                new OrientedDecorationExpectation("Text01", "room_select_create_text_01", 460f, 144.29f, 88f, 88f * 9f / 63f, 0f, true),
                new OrientedDecorationExpectation("Text02", "room_select_create_text_02", 461f, 154.59f, 66f, 66f * 5f / 46f, 0f, true),
                new OrientedDecorationExpectation("StartRoomDecoration", "room_select_img_startroom", 459f, 22.25f, 84f, 84f * 8f / 64f, 0f, true)
            };
            var frameExpected = new[]
            {
                new OrientedDecorationExpectation("CreateFrame/Top_0", "doc_frame_line", 263.667f, -23f, 237.171f, 12f, 0f, false),
                new OrientedDecorationExpectation("CreateFrame/Top_1", "doc_frame_line", 480f, -23f, 237.171f, 12f, 0f, false),
                new OrientedDecorationExpectation("CreateFrame/Top_2", "doc_frame_line", 696.333f, -23f, 237.171f, 12f, 0f, false),
                new OrientedDecorationExpectation("CreateFrame/LeftUpper", "doc_frame_line", 147f, 40f, 128.053f, 12f, 90f, false),
                new OrientedDecorationExpectation("CreateFrame/LeftLower", "doc_frame_line", 147f, 149f, 128.053f, 12f, 90f, false),
                new OrientedDecorationExpectation("CreateFrame/RightUpper", "doc_frame_line", 813f, 40f, 128.053f, 12f, 270f, false),
                new OrientedDecorationExpectation("CreateFrame/RightLower", "doc_frame_line", 813f, 149f, 128.053f, 12f, 270f, false)
            };

            Assert.That(home.Find("RoomSelect/PanelFrame"), Is.Null);
            Assert.That(create.Find("LogoLeft"), Is.Null);
            Assert.That(create.Find("LogoRight"), Is.Null);
            var backingNode = create.Find("InteriorBacking");
            Assert.That(backingNode, Is.Not.Null);
            var backing = backingNode.GetComponent<UnityEngine.UI.Image>();
            var backingRect = backing.rectTransform;
            Assert.That(backing.sprite, Is.Null);
            var materialField = typeof(Graphic).GetField("m_Material", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(materialField, Is.Not.Null, "Unity Graphic must expose its serialized custom-material field.");
            Assert.IsNull(materialField.GetValue(backing), "InteriorBacking must not use a custom serialized material.");
            Assert.That(backing.color, Is.EqualTo(new Color(0f, 0f, 0f, .78f)));
            Assert.That(backing.raycastTarget, Is.False);
            AssertTopLeftRect(backingRect, 147f, -12f, 666f, 224f, .05f);
            Assert.That(backingRect.GetSiblingIndex(), Is.LessThan(create.Find("CreateFrame").GetSiblingIndex()));
            var createFrame = create.Find("CreateFrame");
            Assert.That(createFrame.GetComponent<Graphic>(), Is.Null);
            Assert.That(create.Find("Wings").GetComponent<Graphic>(), Is.Null);
            var frameImages = createFrame.GetComponentsInChildren<UnityEngine.UI.Image>(false);
            Assert.That(frameImages, Has.Length.EqualTo(7));
            Assert.That(createFrame.Find("Bottom_0"), Is.Null);
            Assert.That(createFrame.Find("Bottom_1"), Is.Null);
            Assert.That(createFrame.Find("Bottom_2"), Is.Null);
            Assert.That(createFrame.Find("TopRightChamfer"), Is.Null);
            Assert.That(frameImages.All(image => image.gameObject.activeInHierarchy), Is.True);
            Assert.That(frameImages.All(image => image.color == new Color(.55f, .95f, .88f, 1f)), Is.True);
            var expectedWingTint = new Color(.35f, .65f, .58f, .45f);
            var wingImages = create.Find("Wings").GetComponentsInChildren<UnityEngine.UI.Image>(false);
            Assert.That(wingImages, Has.Length.EqualTo(4));
            Assert.That(wingImages.All(wing => wing.sprite != null && wing.sprite.name == "img_pointer"), Is.True);
            foreach (var wing in wingImages)
            {
                Assert.That(wing.color, Is.EqualTo(expectedWingTint), wing.name);
            }
            Assert.That(create.anchoredPosition.x, Is.GreaterThanOrEqualTo(960f));
            Assert.That(join.anchoredPosition.x, Is.GreaterThanOrEqualTo(960f));
            Assert.That(create.anchoredPosition.x + create.sizeDelta.x, Is.LessThanOrEqualTo(1920f));
            Assert.That(join.anchoredPosition.x + join.sizeDelta.x, Is.LessThanOrEqualTo(1920f));
            Assert.That(create.anchoredPosition.x, Is.EqualTo(layout.RoomSelectCreate.Left));
            Assert.That(join.anchoredPosition.x, Is.EqualTo(layout.RoomSelectJoin.Left));
            Assert.That(background.preserveAspect, Is.True);
            Assert.That(Aspect(background.rectTransform), Is.EqualTo(Aspect(background.sprite)).Within(.01f));
            AssertActionRect(createActionRect, create, layout.RoomSelectCreateAction, .05f);
            AssertActionRect(joinActionRect, join, layout.RoomSelectJoinAction, 2f);
            Assert.That(createActionImage.preserveAspect, Is.False);
            Assert.That(joinActionImage.preserveAspect, Is.False);
            Assert.That(createActionRect.sizeDelta, Is.EqualTo(joinActionRect.sizeDelta));
            Assert.That(createAction.Find("ActionIcon").GetComponent<UnityEngine.UI.Image>().sprite.name, Is.EqualTo("create_icon"));
            Assert.That(joinAction.Find("ActionIcon").GetComponent<UnityEngine.UI.Image>().sprite.name, Is.EqualTo("join_icon"));
            AssertTopLeftRect(createAction.Find("ActionIcon").GetComponent<RectTransform>(), 47f, 25f, 38f, 38f, .05f);
            AssertTopLeftRect(joinAction.Find("ActionIcon").GetComponent<RectTransform>(), 47f, 19f, 47f, 47f * 41f / 36f, .05f);
            var createLabel = createAction.Find("Label").GetComponent<UnityEngine.UI.Text>();
            var joinLabel = joinAction.Find("Label").GetComponent<UnityEngine.UI.Text>();
            Assert.That(createLabel.text, Is.EqualTo("创建同盟"));
            Assert.That(joinLabel.text, Is.EqualTo("加入同盟"));
            Assert.That(createLabel.rectTransform.offsetMin, Is.EqualTo(new Vector2(108f, 5f)));
            Assert.That(createLabel.rectTransform.offsetMax, Is.EqualTo(new Vector2(-220f, 5f)));
            Assert.That(joinLabel.rectTransform.offsetMin.x, Is.EqualTo(103f).Within(.05f));
            Assert.That(joinLabel.rectTransform.offsetMin.y, Is.EqualTo(2f).Within(.05f));
            Assert.That(joinLabel.rectTransform.offsetMax.x, Is.EqualTo(-220f).Within(.05f));
            Assert.That(joinLabel.rectTransform.offsetMax.y, Is.EqualTo(2f).Within(.05f));
            Assert.That(createLabel.fontSize, Is.EqualTo(38));
            Assert.That(joinLabel.fontSize, Is.EqualTo(38));
            Assert.That(create.GetComponent<UnityEngine.UI.Button>(), Is.Null, "The whole decoration container must not replace the action-bar hit area.");
            Assert.That(createActionRect.GetSiblingIndex(), Is.EqualTo(create.childCount - 1));
            Assert.That(joinActionRect.GetSiblingIndex(), Is.EqualTo(join.childCount - 1));

            var createRequests = 0;
            view.CreateRequested += () => createRequests++;
            createAction.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            Assert.That(createRequests, Is.EqualTo(1));

            foreach (var item in expected)
            {
                AssertOrientedDecoration(create, item);
                AssertDecorationBehindAction(create, item.Path, createAction);
            }
            foreach (var item in frameExpected)
            {
                AssertOrientedDecoration(create, item);
                AssertDecorationBehindAction(create, item.Path, createAction);
            }

            const float visibleLongAxisRatio = 304f / 309f;
            var horizontalPairs = new[]
            {
                new[] { "Top_0", "Top_1" },
                new[] { "Top_1", "Top_2" }
            };
            foreach (var pair in horizontalPairs)
            {
                var first = createFrame.Find(pair[0]).GetComponent<RectTransform>();
                var second = createFrame.Find(pair[1]).GetComponent<RectTransform>();
                var overlap = first.sizeDelta.x * visibleLongAxisRatio
                    - Mathf.Abs(second.anchoredPosition.x - first.anchoredPosition.x);
                Assert.That(overlap, Is.EqualTo(17f).Within(.1f), string.Join("/", pair));
            }
            var verticalPairs = new[]
            {
                new[] { "LeftUpper", "LeftLower" },
                new[] { "RightUpper", "RightLower" }
            };
            foreach (var pair in verticalPairs)
            {
                var first = createFrame.Find(pair[0]).GetComponent<RectTransform>();
                var second = createFrame.Find(pair[1]).GetComponent<RectTransform>();
                var overlap = first.sizeDelta.x * visibleLongAxisRatio
                    - Mathf.Abs(second.anchoredPosition.y - first.anchoredPosition.y);
                Assert.That(overlap, Is.EqualTo(17f).Within(.1f), string.Join("/", pair));
            }
            var topLeft = createFrame.Find("Top_0").GetComponent<RectTransform>();
            var topRight = createFrame.Find("Top_2").GetComponent<RectTransform>();
            Assert.That(topLeft.anchoredPosition.x - topLeft.sizeDelta.x * visibleLongAxisRatio / 2f, Is.EqualTo(147f).Within(.1f));
            Assert.That(topRight.anchoredPosition.x + topRight.sizeDelta.x * visibleLongAxisRatio / 2f, Is.EqualTo(813f).Within(.1f));
            var leftTop = createFrame.Find("LeftUpper").GetComponent<RectTransform>();
            var leftBottom = createFrame.Find("LeftLower").GetComponent<RectTransform>();
            Assert.That(-leftTop.anchoredPosition.y - leftTop.sizeDelta.x * visibleLongAxisRatio / 2f, Is.EqualTo(-23f).Within(.1f));
            Assert.That(-leftBottom.anchoredPosition.y + leftBottom.sizeDelta.x * visibleLongAxisRatio / 2f, Is.EqualTo(212f).Within(.1f));
            foreach (var frameImage in frameImages)
            {
                Assert.That(frameImage.rectTransform.sizeDelta.y, Is.InRange(8f, 14f), frameImage.name);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator HomeRoomSelect_JoinPlaceholderUsesMeasuredPresentation()
        {
            var input = view.transform.Find("LanLobbyRoot/Home/RoomSelect/Join/RoomCodeInput").GetComponent<InputField>();
            var placeholder = input.placeholder as Text;

            Assert.That(placeholder, Is.Not.Null);
            Assert.That(placeholder.text, Is.EqualTo("输入同盟密钥"));
            Assert.That(placeholder.alignment, Is.EqualTo(TextAnchor.MiddleCenter));
            Assert.That(placeholder.fontSize, Is.EqualTo(28));
            Assert.That(placeholder.color, Is.EqualTo(new Color(214f / 255f, 214f / 255f, 214f / 255f, 1f)));
            yield return null;
        }

        [UnityTest]
        public IEnumerator HomeRoomSelect_JoinDecorationUsesMeasuredOverlapAndKeepsInputFunctional()
        {
            var roomSelect = view.transform.Find("LanLobbyRoot/Home/RoomSelect").GetComponent<RectTransform>();
            var join = roomSelect.Find("Join").GetComponent<RectTransform>();
            var input = join.Find("RoomCodeInput").GetComponent<InputField>();
            var joinAction = join.Find("JoinAction").GetComponent<RectTransform>();
            var joinButton = joinAction.GetComponent<Button>();
            var materialField = typeof(Graphic).GetField("m_Material", BindingFlags.Instance | BindingFlags.NonPublic);
            var expectedBitmaps = new Dictionary<string, string>
            {
                { "LeftBlock_0", "room_select_join_left_block" },
                { "LeftBlock_1", "room_select_join_left_block" },
                { "MiddleBlock_0", "room_select_join_middle_block" },
                { "MiddleBlock_1", "room_select_join_middle_block" },
                { "MiddleBlock_2", "room_select_join_middle_block" },
                { "MiddleBlock_3", "room_select_join_middle_block" },
                { "RightBlock_0", "room_select_join_right_block" },
                { "RightBlock_1", "room_select_join_right_block" },
                { "MiddleMask", "room_select_join_middle_block_mask" },
                { "Blank", "room_select_join_blank" },
                { "Ban_0", "room_select_join_ban" },
                { "Ban_1", "room_select_join_ban" },
                { "Ban_2", "room_select_join_ban" },
                { "Ban_3", "room_select_join_ban" },
                { "Triangle", "room_select_join_triangle" },
                { "Logo", "room_select_join_logo" },
                { "Text01", "room_select_join_text_01" },
                { "Text02", "room_select_join_text_02" }
            };
            var expectedGeometry = new[]
            {
                new JoinGeometryExpectation("InteriorBacking", 122f, 11f, 717f, 280f),
                new JoinGeometryExpectation("OutlineTop", 122f, 11f, 717f, 2f),
                new JoinGeometryExpectation("OutlineLeft", 122f, 11f, 2f, 280f),
                new JoinGeometryExpectation("OutlineRight", 837f, 11f, 2f, 280f),
                new JoinGeometryExpectation("GuideHorizontal", 122f, 110f, 717f, 2f),
                new JoinGeometryExpectation("GuideVertical", 474f, 11f, 2f, 196f)
            };

            Canvas.ForceUpdateCanvases();
            Assert.That(join.Find("SimulationInvite"), Is.Null);
            Assert.That(materialField, Is.Not.Null, "Unity Graphic must expose its serialized custom-material field.");
            Assert.That(ChildrenWithPrefix(join, "LeftBlock_").Count, Is.EqualTo(2));
            Assert.That(ChildrenWithPrefix(join, "MiddleBlock_").Count, Is.EqualTo(4));
            Assert.That(ChildrenWithPrefix(join, "RightBlock_").Count, Is.EqualTo(2));
            Assert.That(join.Find("MiddleMask"), Is.Not.Null);
            Assert.That(join.Find("Blank"), Is.Not.Null);
            Assert.That(ChildrenWithPrefix(join, "Ban_").Count, Is.EqualTo(4));
            Assert.That(join.Find("Triangle"), Is.Not.Null);
            Assert.That(join.Find("Logo"), Is.Not.Null);
            Assert.That(join.Find("Text01"), Is.Not.Null);
            Assert.That(join.Find("Text02"), Is.Not.Null);
            for (var index = 0; index < LobbyRoomCode.Length; index++)
                Assert.That(join.Find("Blank_" + index), Is.Null);

            foreach (var bitmap in expectedBitmaps)
            {
                var image = join.Find(bitmap.Key).GetComponent<Image>();
                Assert.That(image.sprite, Is.Not.Null, bitmap.Key);
                Assert.That(image.sprite.name, Is.EqualTo(bitmap.Value), bitmap.Key);
                Assert.That(image.raycastTarget, Is.False, bitmap.Key);
            }

            foreach (var geometry in expectedGeometry)
                AssertJoinCodeNativeGeometry(join, geometry, materialField);
            Assert.That(join.Find("OutlineBottom"), Is.Null);

            var blockNamesInSiblingOrder = new[]
            {
                "LeftBlock_0", "LeftBlock_1",
                "MiddleBlock_0", "MiddleBlock_1", "MiddleBlock_2", "MiddleBlock_3",
                "RightBlock_0", "RightBlock_1"
            };
            for (var index = 1; index < blockNamesInSiblingOrder.Length; index++)
            {
                Assert.That(join.Find(blockNamesInSiblingOrder[index - 1]).GetSiblingIndex(),
                    Is.LessThan(join.Find(blockNamesInSiblingOrder[index]).GetSiblingIndex()),
                    "Block sibling order must remain left, middle, then right.");
            }

            // The end sprites intentionally extend past the approved visible-pixel union to compensate for
            // transparent/near-background source pixels measured by the real-Player detector.
            AssertTopLeftRect(join.Find("LeftBlock_0").GetComponent<RectTransform>(), 163f, 118f, 125f, 125f * 71f / 100f, .1f);
            AssertTopLeftRect(join.Find("LeftBlock_1").GetComponent<RectTransform>(), 251f, 118f, 125f, 125f * 71f / 100f, .1f);
            AssertTopLeftRect(join.Find("MiddleBlock_0").GetComponent<RectTransform>(), 271f, 118f, 74f, 74f * 71f / 86f, .1f);
            AssertTopLeftRect(join.Find("MiddleBlock_1").GetComponent<RectTransform>(), 343f, 118f, 106f, 106f * 71f / 86f, .1f);
            AssertTopLeftRect(join.Find("MiddleBlock_2").GetComponent<RectTransform>(), 504f, 118f, 108f, 108f * 71f / 86f, .1f);
            AssertTopLeftRect(join.Find("MiddleBlock_3").GetComponent<RectTransform>(), 606f, 118f, 108f, 108f * 71f / 86f, .1f);
            AssertTopLeftRect(join.Find("RightBlock_0").GetComponent<RectTransform>(), 603f, 118f, 121f, 121f * 71f / 97f, .1f);
            AssertTopLeftRect(join.Find("RightBlock_1").GetComponent<RectTransform>(), 701f, 118f, 121f, 121f * 71f / 97f, .1f);

            var blockRectsByName = blockNamesInSiblingOrder.ToDictionary(
                name => name,
                name => VisibleSpriteScreenTopLeftRect(join.Find(name).GetComponent<Image>(), roomSelect));
            var spatialOverlapOrder = new[]
            {
                "LeftBlock_0", "LeftBlock_1", "MiddleBlock_0", "MiddleBlock_1",
                "MiddleBlock_2", "RightBlock_0", "MiddleBlock_3", "RightBlock_1"
            };
            for (var index = 1; index < spatialOverlapOrder.Length; index++)
            {
                if (spatialOverlapOrder[index - 1] == "MiddleBlock_1") continue;
                Assert.That(blockRectsByName[spatialOverlapOrder[index - 1]].xMax,
                    Is.GreaterThan(blockRectsByName[spatialOverlapOrder[index]].x),
                    "Spatial block pair " + spatialOverlapOrder[index - 1] + "/" + spatialOverlapOrder[index] + " must overlap.");
            }
            var measuredBlankRect = VisibleSpriteScreenTopLeftRect(join.Find("Blank").GetComponent<Image>(), roomSelect);
            Assert.That(blockRectsByName["MiddleBlock_1"].xMax, Is.GreaterThan(measuredBlankRect.x),
                "The central Blank must overlap the left side of the intentional middle-bank gap.");
            Assert.That(measuredBlankRect.xMax, Is.GreaterThan(blockRectsByName["MiddleBlock_2"].x),
                "The central Blank must overlap the right side of the intentional middle-bank gap.");
            var blockRects = blockRectsByName.Values.ToArray();
            Assert.That(Union(blockRects), Is.EqualTo(new Rect(1195f, 703f, 659f, 108f * 71f / 86f)).Using(RectComparer.Within(.1f)));

            AssertVisibleSpriteScreenTopLeftRect(join.Find("Logo").GetComponent<Image>(), roomSelect, 1245f, 660f, 118f, 20f, 2f);
            AssertVisibleSpriteScreenTopLeftRect(join.Find("Text01").GetComponent<Image>(), roomSelect, 1545f, 652f, 65f, 8f, 2f);
            AssertVisibleSpriteScreenTopLeftRect(join.Find("Text02").GetComponent<Image>(), roomSelect, 1680f, 658f, 89f, 11f, 2f);
            // The triangle Rect likewise compensates for transparent/antialiased edge pixels; its decoded
            // visible bounds, rather than this RectTransform, remain the approved 30x17 target.
            AssertTopLeftRect(join.Find("Triangle").GetComponent<RectTransform>(), 462f, 60f, 28f, 13f, .1f);
            AssertVisibleSpriteScreenTopLeftRect(join.Find("Blank").GetComponent<Image>(), roomSelect, 1477f, 664f, 60f, 61f, 2f);
            AssertVisibleSpriteScreenTopLeftRect(input.GetComponent<Image>(), roomSelect, 1269f, 800f, 482f, 60f, 2f);
            Assert.That(join.Find("Triangle").GetComponent<Image>().preserveAspect, Is.False);
            Assert.That(join.Find("Blank").GetComponent<Image>().preserveAspect, Is.False);
            Assert.That(input.GetComponent<Image>().preserveAspect, Is.False);

            var blankRect = VisibleSpriteScreenTopLeftRect(join.Find("Blank").GetComponent<Image>(), roomSelect);
            var bans = ChildrenWithPrefix(join, "Ban_").Select(item => VisibleSpriteScreenTopLeftRect(item.GetComponent<Image>(), roomSelect)).ToArray();
            var banCoordinates = bans.Select(rect => new Vector2(rect.x, rect.y)).ToArray();
            Assert.That(banCoordinates.Distinct().Count(), Is.EqualTo(4), "Each ban must occupy one distinct grid cell.");
            Assert.That(bans.GroupBy(rect => rect.x).Select(group => group.Count()), Is.EquivalentTo(new[] { 2, 2 }),
                "The ban grid must have exactly two bans in each column.");
            Assert.That(bans.GroupBy(rect => rect.y).Select(group => group.Count()), Is.EquivalentTo(new[] { 2, 2 }),
                "The ban grid must have exactly two bans in each row.");
            Assert.That(bans.All(rect => blankRect.Contains(rect.min) && blankRect.Contains(rect.max)), Is.True,
                "Every ban must remain inside the central blank.");

            var inputTransform = input.GetComponent<RectTransform>();
            foreach (var graphic in join.GetComponentsInChildren<Graphic>(true))
            {
                if (!graphic.isActiveAndEnabled || graphic.transform.IsChildOf(input.transform) || graphic.transform.IsChildOf(joinAction)) continue;
                var directChild = graphic.transform;
                while (directChild.parent != join) directChild = directChild.parent;
                Assert.That(directChild.GetSiblingIndex(), Is.LessThan(inputTransform.GetSiblingIndex()), graphic.name);
                Assert.That(directChild.GetSiblingIndex(), Is.LessThan(joinAction.GetSiblingIndex()), graphic.name);
                Assert.That(DesignRect(graphic.rectTransform, roomSelect).Overlaps(DesignRect(joinAction, roomSelect)), Is.False,
                    graphic.name + " must not overlap the accepted Join action.");
            }
            Assert.That(DesignRect(inputTransform, roomSelect).Overlaps(DesignRect(joinAction, roomSelect)), Is.False,
                "The room-code input must not overlap the accepted Join action.");

            Assert.That(ScreenTopLeftRect(DesignRect(joinAction, roomSelect)), Is.EqualTo(new Rect(1154f, 876f, 717f, 99f)).Using(RectComparer.Within(2f)));
            AssertTopLeftRect(joinAction.Find("ActionIcon").GetComponent<RectTransform>(), 47f, 19f, 47f, 47f * 41f / 36f, .05f);
            var joinLabel = joinAction.Find("Label").GetComponent<Text>();
            Assert.That(joinLabel.rectTransform.offsetMin, Is.EqualTo(new Vector2(103f, 2f)));
            Assert.That(joinLabel.rectTransform.offsetMax, Is.EqualTo(new Vector2(-220f, 2f)));
            Assert.That(joinLabel.fontSize, Is.EqualTo(38));

            Assert.That(input.contentType, Is.EqualTo(InputField.ContentType.IntegerNumber));
            Assert.That(input.characterLimit, Is.EqualTo(LobbyRoomCode.Length));
            Assert.That(input.textComponent.alignment, Is.EqualTo(TextAnchor.MiddleCenter));
            Assert.That(input.GetComponent<Image>().raycastTarget, Is.True);
            view.BindDiscoveredRooms(new[] { Discovery("654321") });
            view.ClickDiscoveredRoomForTests("654321");
            Assert.That(view.RoomCodeTextForTests, Is.EqualTo("654321"));
            Assert.That(joinRequests, Is.Zero);

            var inputPointer = PointerAt(inputTransform);
            var inputHits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(inputPointer, inputHits);
            Assert.That(inputHits, Is.Not.Empty, "The input center must have a raycast hit.");
            var inputClickTarget = ExecuteEvents.GetEventHandler<IPointerClickHandler>(inputHits[0].gameObject);
            Assert.That(inputClickTarget, Is.EqualTo(input.gameObject),
                "The first raycast hit at the input center must resolve the real InputField click handler.");

            var actionPointer = PointerAt(joinAction);
            var actionHits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(actionPointer, actionHits);
            Assert.That(actionHits, Is.Not.Empty, "The Join action center must have a raycast hit.");
            var joinClickTarget = ExecuteEvents.GetEventHandler<IPointerClickHandler>(actionHits[0].gameObject);
            Assert.That(joinClickTarget, Is.EqualTo(joinButton.gameObject),
                "The first raycast hit at the Join action center must resolve the accepted Join Button.");
            ExecuteEvents.Execute(joinClickTarget, actionPointer, ExecuteEvents.pointerClickHandler);
            Assert.That(joinRequests, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator HomeRoomSelect_JoinActionIsUnobstructedBeforeAndAfterDiscoveryPrefill()
        {
            var roomSelect = view.transform.Find("LanLobbyRoot/Home/RoomSelect").GetComponent<RectTransform>();
            var joinAction = roomSelect.Find("Join/JoinAction").GetComponent<RectTransform>();
            var status = roomSelect.Find("Status").GetComponent<RectTransform>();
            var layout = global::LanLobbyLayout.ForSize(1920, 1080, 4);
            var expectedJoinRect = new Rect(
                layout.RoomSelectJoinAction.Left,
                layout.RoomSelectJoinAction.Bottom,
                layout.RoomSelectJoinAction.Width,
                layout.RoomSelectJoinAction.Height);

            Canvas.ForceUpdateCanvases();
            Assert.That(DesignRect(joinAction, roomSelect), Is.EqualTo(expectedJoinRect).Using(RectComparer.Within(2f)));
            Assert.That(DesignRect(status, roomSelect).Overlaps(expectedJoinRect), Is.False,
                "Status must not visually cover the Join action bar.");

            view.BindDiscoveredRooms(new[] { Discovery("654321") });
            view.ClickDiscoveredRoomForTests("654321");
            yield return null;
            Canvas.ForceUpdateCanvases();

            var discoveredRoom = roomSelect.Find("DiscoveredRooms/Items/Room_654321").GetComponent<RectTransform>();
            Assert.That(DesignRect(discoveredRoom, roomSelect).Overlaps(expectedJoinRect), Is.False,
                "A discovered-room Button must not cover or intercept the Join action bar.");
            AssertLaterRoomSelectGraphicsDoNotOverlapJoin(roomSelect, expectedJoinRect);

            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, joinAction.TransformPoint(joinAction.rect.center))
            };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            var firstButton = FirstInteractableButton(hits);
            Assert.That(firstButton, Is.EqualTo(joinAction.GetComponent<Button>()),
                "The Join action must be the first interactable Button at its screen-space center.");

            ExecuteEvents.Execute(firstButton.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Assert.That(joinRequests, Is.EqualTo(1), "The unobstructed Join action must retain its request behavior.");
        }

        [UnityTest]
        public IEnumerator UnknownFullAndStartedRooms_DisableJoinAndExplainWhy()
        {
            view.BindDiscoveredRooms(new[]
            {
                Discovery("111111", memberCount: 4, capacity: 4),
                Discovery("222222", joinable: false)
            });

            view.SetRoomCodeForTests("999999");
            Assert.That(view.JoinInteractableForTests, Is.False);
            Assert.That(view.StatusTextForTests, Does.Contain("not found"));

            view.SetRoomCodeForTests("111111");
            Assert.That(view.JoinInteractableForTests, Is.False);
            Assert.That(view.StatusTextForTests, Does.Contain("full"));

            view.SetRoomCodeForTests("222222");
            Assert.That(view.JoinInteractableForTests, Is.False);
            Assert.That(view.StatusTextForTests, Does.Contain("started"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator RoomSnapshot_RendersFourCardsReadyStateAndTopLeftLatency()
        {
            view.BindRoom(Room("654321"));
            view.SetLocalLatency(42);

            Assert.That(view.RoomCardCountForTests, Is.EqualTo(4));
            Assert.That(view.ReadyCardCountForTests, Is.EqualTo(2));
            Assert.That(view.LocalLatencyTextForTests, Is.EqualTo("42 ms"));
            Assert.That(view.CanvasSortOrderForTests, Is.EqualTo(1000));
            yield return null;
        }

        [UnityTest]
        public IEnumerator RoomLayout_At1920By1200_RendersLetterboxedRoomRects()
        {
            yield return AssertRenderedRoomLayoutAtResolution(1920, 1200);
        }

        [UnityTest]
        public IEnumerator RoomLayout_At2560By1080_RendersLetterboxedRoomRects()
        {
            yield return AssertRenderedRoomLayoutAtResolution(2560, 1080);
        }

        [UnityTest]
        public IEnumerator DestroyingView_RemovesItsOwnedEventSystem()
        {
            Assert.That(Object.FindObjectsOfType<UnityEngine.EventSystems.EventSystem>(), Has.Some.Matches<UnityEngine.EventSystems.EventSystem>(item => item.name == "LanLobbyEventSystem"));

            Object.Destroy(root);
            root = null;
            yield return null;

            Assert.That(Object.FindObjectsOfType<UnityEngine.EventSystems.EventSystem>(), Has.None.Matches<UnityEngine.EventSystems.EventSystem>(item => item.name == "LanLobbyEventSystem"));
        }

        [UnityTest]
        public IEnumerator ShowingProfile_PrefillsIdentityWithoutSavingIt()
        {
            var saves = 0;
            view.ProfileSaved += _ => saves++;

            view.ShowHome(new LobbyProfile("local", "Doctor", 0));

            Assert.That(saves, Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SavingProfile_UsesSelectedAvatarIndex()
        {
            LobbyProfile saved = null;
            view.ProfileSaved += profile => saved = profile;
            view.ShowHome(new LobbyProfile("local", "Doctor", 3));

            view.transform.Find("LanLobbyRoot/Home/IdentityPanel/SaveProfile").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();

            Assert.That(saved, Is.Not.Null);
            Assert.That(saved.AvatarIndex, Is.EqualTo(3));
            yield return null;
        }

        [UnityTest]
        public IEnumerator RoomPermissions_RequireLocalMemberAndNeverAllowStartedRoomActions()
        {
            view.ShowRoom(Room("654321", everyoneReady: true), "missing");
            var primary = RequireOnlyVisibleRoomPrimaryAction(view);
            var leave = RequireChild(view.transform, "LanLobbyRoot/Room/LeaveAction").GetComponent<Button>();
            Assert.That(view.RoomPrimaryActionInteractableForTests, Is.False);
            Assert.That(view.RoomLeaveInteractableForTests, Is.False);

            view.ShowRoom(Room("654321", everyoneReady: true), "host");
            Assert.That(view.RoomPrimaryActionInteractableForTests, Is.True);
            Assert.That(view.RoomLeaveInteractableForTests, Is.True);

            view.ShowRoom(Room("654321", hasStarted: true, everyoneReady: true), "host");
            Assert.That(view.RoomPrimaryActionInteractableForTests, Is.False);
            Assert.That(view.RoomLeaveInteractableForTests, Is.False);
            Assert.That(primary, Is.SameAs(RequireOnlyVisibleRoomPrimaryAction(view)));
            Assert.That(leave, Is.SameAs(RequireChild(view.transform, "LanLobbyRoot/Room/LeaveAction").GetComponent<Button>()));
            yield return null;
        }

        [UnityTest]
        public IEnumerator RoomPrimaryAction_HostAllPresentReady_UsesCyanStartAndEmitsOnlyStart()
        {
            var readyRequests = new List<bool>();
            var startRequests = 0;
            view.ReadyRequested += value => readyRequests.Add(value);
            view.StartRequested += () => startRequests++;

            view.ShowRoom(Room("654321", everyoneReady: true), "host");
            var primary = RequireOnlyVisibleRoomPrimaryAction(view);

            AssertResourceSprite(primary.GetComponent<Image>(), "btn_match_normal");
            AssertResourceSprite(RequireChild(primary.transform, "ActionIcon").GetComponent<Image>(), "btn_match_host_normal");
            Assert.That(primary.GetComponentInChildren<Text>().color,
                Is.EqualTo(new Color(33f / 255f, 33f / 255f, 33f / 255f, 1f)));
            Assert.That(primary.GetComponentInChildren<Text>().horizontalOverflow, Is.EqualTo(HorizontalWrapMode.Overflow));
            Assert.That(primary.GetComponentInChildren<Text>().text, Is.EqualTo("协议启动"));
            Assert.That(primary.interactable, Is.True);
            Click(primary);

            Assert.That(startRequests, Is.EqualTo(1));
            Assert.That(readyRequests, Is.Empty);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RoomPrimaryAction_HostWithUnreadyGuest_UsesDisabledGrayStartAndEmitsNothing()
        {
            var readyRequests = new List<bool>();
            var startRequests = 0;
            view.ReadyRequested += value => readyRequests.Add(value);
            view.StartRequested += () => startRequests++;

            view.ShowRoom(RoomWithGuest("654321", guestReady: false), "host");
            var primary = RequireOnlyVisibleRoomPrimaryAction(view);

            AssertResourceSprite(primary.GetComponent<Image>(), "btn_match_grey");
            AssertResourceSprite(RequireChild(primary.transform, "ActionIcon").GetComponent<Image>(), "btn_match_host_grey");
            Assert.That(primary.GetComponentInChildren<Text>().color,
                Is.EqualTo(new Color(157f / 255f, 157f / 255f, 157f / 255f, 1f)));
            Assert.That(primary.GetComponentInChildren<Text>().horizontalOverflow, Is.EqualTo(HorizontalWrapMode.Overflow));
            Assert.That(primary.GetComponentInChildren<Text>().text, Is.EqualTo("协议启动"));
            Assert.That(primary.interactable, Is.False);
            Assert.That(primary.GetComponent<CanvasRenderer>().GetColor(), Is.EqualTo(Color.white),
                "The disabled host action must render the approved gray Sprite without an additional tint.");
            Click(primary);

            Assert.That(startRequests, Is.Zero);
            Assert.That(readyRequests, Is.Empty);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RoomPrimaryAction_UnreadyGuest_UsesInteractableGrayReadyAndEmitsOnlyReadyTrue()
        {
            var readyRequests = new List<bool>();
            var startRequests = 0;
            view.ReadyRequested += value => readyRequests.Add(value);
            view.StartRequested += () => startRequests++;

            view.ShowRoom(RoomWithGuest("654321", guestReady: false), "guest-1");
            var primary = RequireOnlyVisibleRoomPrimaryAction(view);
            view.ShowRoom(RoomWithGuest("654321", guestReady: true), "guest-1");
            view.BindRoom(RoomWithGuest("654321", guestReady: false), "guest-1");

            Assert.That(RequireOnlyVisibleRoomPrimaryAction(view), Is.SameAs(primary));
            AssertResourceSprite(primary.GetComponent<Image>(), "btn_match_grey");
            Assert.That(primary.GetComponentInChildren<Text>().text, Is.EqualTo("准备就绪"));
            Assert.That(primary.interactable, Is.True);
            Click(primary);

            Assert.That(readyRequests, Is.EqualTo(new[] { true }));
            Assert.That(startRequests, Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RoomPrimaryAction_ReadyGuest_UsesCyanCancelAndEmitsOnlyReadyFalse()
        {
            var readyRequests = new List<bool>();
            var startRequests = 0;
            view.ReadyRequested += value => readyRequests.Add(value);
            view.StartRequested += () => startRequests++;

            view.ShowRoom(RoomWithGuest("654321", guestReady: true), "guest-1");
            var primary = RequireOnlyVisibleRoomPrimaryAction(view);

            AssertResourceSprite(primary.GetComponent<Image>(), "btn_match_normal");
            Assert.That(primary.GetComponentInChildren<Text>().text, Is.EqualTo("取消准备"));
            Assert.That(primary.interactable, Is.True);
            Click(primary);

            Assert.That(readyRequests, Is.EqualTo(new[] { false }));
            Assert.That(startRequests, Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RoomLeave_IsTopLeft_UsesApprovedSprite_AndHasUnobstructedHitTarget()
        {
            var leaveRequests = 0;
            view.LeaveRequested += () => leaveRequests++;
            view.BindRoom(HostOnlyRoom("654321"), "host");
            view.ShowRoom();

            var room = RequireChild(view.transform, "LanLobbyRoot/Room");
            var leaveButtons = room.GetComponentsInChildren<Button>(true)
                .Where(button => button.name == "LeaveAction" && button.gameObject.activeInHierarchy)
                .ToArray();
            Assert.That(leaveButtons, Has.Length.EqualTo(1));
            var leave = leaveButtons[0];
            AssertResourceSprite(leave.GetComponent<Image>(), "img_return");
            AssertBottomLeftRect(
                leave.GetComponent<RectTransform>(),
                global::LanLobbyRoomLayout.ForSize(1920, 1080).LeaveAction);
            Assert.That(leave.interactable, Is.True);
            AssertButtonChildrenDoNotReceiveRaycasts(leave);

            yield return null;
            var hits = RaycastAt(leave.GetComponent<RectTransform>(), new Vector2(.5f, .5f));
            Assert.That(hits, Is.Not.Empty);
            Assert.That(hits[0].gameObject, Is.SameAs(leave.gameObject));
            Click(leave);
            Assert.That(leaveRequests, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator RoomPrimaryAction_HitTargetWinsOverEveryDecoration()
        {
            var scaler = view.GetComponent<CanvasScaler>();
            scaler.matchWidthOrHeight = 0f;
            yield return null;
            Assert.That(view.GetComponent<Canvas>().scaleFactor, Is.EqualTo(Screen.width / 1920f).Within(.001f));

            view.BindRoom(HostOnlyRoom("654321"), "host");
            view.ShowRoom();
            var primary = RequireOnlyVisibleRoomPrimaryAction(view);
            var primaryRect = primary.GetComponent<RectTransform>();
            var label = primary.GetComponentInChildren<Text>();

            AssertBottomLeftRect(
                primaryRect,
                global::LanLobbyRoomLayout.ForSize(1920, 1080).PrimaryAction);
            AssertButtonChildrenDoNotReceiveRaycasts(primary);

            yield return null;
            var embeddedIconPoint = ScreenPointAt(primaryRect, new Vector2(.88f, .5f));
            var labelPoint = ScreenPointAt(label.rectTransform, new Vector2(.5f, .5f));
            AssertScreenPointIsVisible(embeddedIconPoint);
            AssertScreenPointIsVisible(labelPoint);
            var embeddedIconHits = RaycastAt(primaryRect, new Vector2(.88f, .5f));
            var labelHits = RaycastAt(label.rectTransform, new Vector2(.5f, .5f));
            Assert.That(embeddedIconHits, Is.Not.Empty);
            Assert.That(labelHits, Is.Not.Empty);
            Assert.That(embeddedIconHits[0].gameObject, Is.SameAs(primary.gameObject));
            Assert.That(labelHits[0].gameObject, Is.SameAs(primary.gameObject));
            yield return null;
        }

        [UnityTest]
        public IEnumerator RoomSnapshot_EmptySlotUsesCompleteInviteComposition()
        {
            view.BindRoom(HostOnlyRoom("654321"), "host");
            var room = view.transform.Find("LanLobbyRoot/Room");
            var layout = global::LanLobbyRoomLayout.ForSize(1920, 1080);

            Assert.That(view.RoomCardCountForTests, Is.EqualTo(4));
            for (var index = 0; index < 4; index++)
            {
                var slot = RequireChild(room, "RoomCard_" + index);
                AssertBottomLeftRect(slot.GetComponent<RectTransform>(), layout.Slots[index].Root);
            }

            for (var index = 1; index < 4; index++)
            {
                var slot = RequireChild(room, "RoomCard_" + index);
                AssertDirectChildren(slot,
                    "CardBody", "ReadyOverlay", "EmptyContent", "OccupiedContent",
                    "TopBar", "LowerDecoration", "CreatorTag");
                AssertResourceSprite(RequireChild(slot, "CardBody").GetComponent<Image>(), "card_bg");
                AssertResourceSprite(RequireChild(slot, "TopBar").GetComponent<Image>(), "bg_top_normal");

                var emptyContent = RequireChild(slot, "EmptyContent");
                Assert.That(emptyContent.gameObject.activeSelf, Is.True);
                AssertResourceSprite(emptyContent.GetComponent<Image>(), "card_empty");
                AssertDirectChildren(emptyContent, "EmptyInviteIcon", "EmptyInviteLabel", "EmptyInviteHint");
                var emptyInviteIcon = RequireChild(emptyContent, "EmptyInviteIcon").GetComponent<Image>();
                AssertResourceSprite(emptyInviteIcon, "bg_plus");
                Assert.That(emptyInviteIcon.preserveAspect, Is.True);
                Assert.That(Aspect(emptyInviteIcon.rectTransform),
                    Is.EqualTo(Aspect(emptyInviteIcon.sprite)).Within(.0001f));
                AssertBottomLeftAnchoring(emptyInviteIcon.rectTransform);
                Assert.That(RequireChild(emptyContent, "EmptyInviteLabel").GetComponent<Text>().text, Is.EqualTo("邀请"));
                Assert.That(RequireChild(emptyContent, "EmptyInviteHint").GetComponent<Text>().text, Is.Not.Empty);
                Assert.That(RequireChild(slot, "ReadyOverlay").gameObject.activeSelf, Is.False);
                Assert.That(RequireChild(slot, "OccupiedContent").gameObject.activeSelf, Is.False);
                Assert.That(RequireChild(slot, "LowerDecoration").gameObject.activeSelf, Is.True);
                Assert.That(RequireChild(slot, "CreatorTag").gameObject.activeSelf, Is.False);
                Assert.That(slot.GetComponentsInChildren<Text>(true).Select(text => text.text),
                    Has.None.EqualTo("OPEN SLOT").And.None.EqualTo("WAITING"));
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator RoomSnapshot_UnreadyGuestUsesNeutralFrameWithoutWaitingText()
        {
            view.BindRoom(RoomWithGuest("654321", guestReady: false), "guest-1");
            var slot = RequireChild(view.transform, "LanLobbyRoot/Room/RoomCard_1");

            AssertResourceSprite(RequireChild(slot, "CardBody").GetComponent<Image>(), "card_bg");
            AssertResourceSprite(RequireChild(slot, "TopBar").GetComponent<Image>(), "bg_top_normal");
            Assert.That(RequireChild(slot, "ReadyOverlay").gameObject.activeSelf, Is.False);
            Assert.That(RequireChild(slot, "EmptyContent").gameObject.activeSelf, Is.False);
            Assert.That(RequireChild(slot, "OccupiedContent").gameObject.activeSelf, Is.False);
            Assert.That(RequireChild(slot, "LowerDecoration").gameObject.activeSelf, Is.True);
            Assert.That(RequireChild(slot, "CreatorTag").gameObject.activeSelf, Is.False);
            Assert.That(slot.GetComponentsInChildren<Text>(true).Select(text => text.text),
                Has.None.EqualTo("OPEN SLOT").And.None.EqualTo("WAITING"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator RoomSnapshot_ReadyMemberUsesCyanLayersAndRestoresEveryLayerAfterRebind()
        {
            var ready = RoomWithGuest("654321", guestReady: true);
            var waiting = RoomWithGuest("654321", guestReady: false);
            var empty = HostOnlyRoom("654321");
            var room = RequireChild(view.transform, "LanLobbyRoot/Room");
            var originalSlots = ChildrenWithPrefix(room, "RoomCard_").ToArray();

            view.BindRoom(ready, "guest-1");
            AssertReadyGuestSlot(RequireChild(room, "RoomCard_1"));
            view.BindRoom(waiting, "guest-1");
            AssertWaitingGuestSlot(RequireChild(room, "RoomCard_1"));
            view.BindRoom(empty, "host");
            AssertEmptyGuestSlot(RequireChild(room, "RoomCard_1"));
            view.BindRoom(ready, "guest-1");

            var reboundSlot = RequireChild(room, "RoomCard_1");
            AssertReadyGuestSlot(reboundSlot);
            Assert.That(ChildrenWithPrefix(room, "RoomCard_"), Has.Count.EqualTo(4));
            for (var index = 0; index < originalSlots.Length; index++)
            {
                Assert.That(RequireChild(room, "RoomCard_" + index), Is.SameAs(originalSlots[index]));
            }

            var readyLabel = RequireChild(RequireChild(reboundSlot, "OccupiedContent"), "ReadyLabel").GetComponent<Text>();
            Assert.That(readyLabel.text, Is.EqualTo("已就绪"));
            Assert.That(readyLabel.rectTransform.sizeDelta.x, Is.EqualTo(readyLabel.preferredWidth).Within(.05f));
            Assert.That(readyLabel.rectTransform.sizeDelta.y, Is.EqualTo(readyLabel.preferredHeight).Within(.05f));
            AssertBottomLeftAnchor(
                readyLabel.rectTransform,
                global::LanLobbyRoomLayout.ForSize(1920, 1080).Slots[1].ReadyLabel);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RoomSnapshot_HostReadySlotHasNoProfileOrPortraitContent()
        {
            view.BindRoom(HostOnlyRoom("654321"), "host");
            var hostSlot = RequireChild(view.transform, "LanLobbyRoot/Room/RoomCard_0");

            AssertResourceSprite(RequireChild(hostSlot, "TopBar").GetComponent<Image>(), "bg_top_ready");
            AssertResourceSprite(RequireChild(hostSlot, "ReadyOverlay").GetComponent<Image>(), "player_card_self_frame");
            AssertResourceSprite(RequireChild(hostSlot, "CreatorTag").GetComponent<Image>(), "host_top_tag");
            Assert.That(RequireChild(hostSlot, "CreatorTag").gameObject.activeSelf, Is.True);
            var hostLayout = global::LanLobbyRoomLayout.ForSize(1920, 1080).Slots[0];
            Assert.That(RequireChild(hostSlot, "CardBody").GetComponent<Image>().color,
                Is.EqualTo(new Color(0f, 220f / 255f, 220f / 255f, 1f)));
            AssertBottomLeftRect(RequireChild(hostSlot, "TopBar").GetComponent<RectTransform>(), hostLayout.ReadyTopBar);
            AssertBottomLeftRect(RequireChild(hostSlot, "CreatorTag").GetComponent<RectTransform>(), hostLayout.CreatorTag);
            Assert.That(RequireChild(RequireChild(hostSlot, "OccupiedContent"), "ReadyLabel").GetComponent<Text>().text, Is.EqualTo("已就绪"));
            var hostNodeNames = hostSlot.GetComponentsInChildren<Transform>(true)
                .Select(item => item.name.ToLowerInvariant())
                .ToArray();
            var forbiddenProfileNames = new[]
            {
                "portrait", "avatar", "profilename", "playername", "playerid", "profilecard"
            };
            Assert.That(hostNodeNames.Any(name => forbiddenProfileNames.Any(name.Contains)), Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RoomSlot_AllStatesKeepOnePortraitFrameFootprint()
        {
            var room = RequireChild(view.transform, "LanLobbyRoot/Room");

            view.BindRoom(HostOnlyRoom("654321"), "host");
            Canvas.ForceUpdateCanvases();
            var emptySlot = RequireChild(room, "RoomCard_1").GetComponent<RectTransform>();
            var emptyFrame = WorldRect(RequireChild(emptySlot, "CardBody").GetComponent<RectTransform>());

            view.BindRoom(RoomWithGuest("654321", guestReady: false), "guest-1");
            Canvas.ForceUpdateCanvases();
            var waitingSlot = RequireChild(room, "RoomCard_1").GetComponent<RectTransform>();
            var waitingFrame = WorldRect(RequireChild(waitingSlot, "CardBody").GetComponent<RectTransform>());

            view.BindRoom(RoomWithGuest("654321", guestReady: true), "guest-1");
            Canvas.ForceUpdateCanvases();
            var readySlot = RequireChild(room, "RoomCard_1").GetComponent<RectTransform>();
            var readyFrame = WorldRect(RequireChild(readySlot, "CardBody").GetComponent<RectTransform>());
            var hostSlot = RequireChild(room, "RoomCard_0").GetComponent<RectTransform>();
            var hostFrame = WorldRect(RequireChild(hostSlot, "CardBody").GetComponent<RectTransform>());

            AssertWorldRect(waitingFrame, emptyFrame);
            AssertWorldRect(readyFrame, emptyFrame);

            var relativeFrames = new[]
            {
                RelativeToRoot(emptyFrame, WorldRect(emptySlot)),
                RelativeToRoot(waitingFrame, WorldRect(waitingSlot)),
                RelativeToRoot(readyFrame, WorldRect(readySlot)),
                RelativeToRoot(hostFrame, WorldRect(hostSlot))
            };
            for (var index = 1; index < relativeFrames.Length; index++)
            {
                AssertWorldRect(relativeFrames[index], relativeFrames[0]);
                Assert.That(relativeFrames[index].width, Is.EqualTo(relativeFrames[0].width).Within(.05f));
                Assert.That(relativeFrames[index].height, Is.EqualTo(relativeFrames[0].height).Within(.05f));
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator RoomSlot_LayersFrameBehindContentAndBars()
        {
            var layout = global::LanLobbyRoomLayout.ForSize(1920, 1080).Slots[1];
            var room = RequireChild(view.transform, "LanLobbyRoot/Room");

            view.BindRoom(RoomWithGuest("654321", guestReady: false), "guest-1");
            Canvas.ForceUpdateCanvases();
            var slot = RequireChild(room, "RoomCard_1");
            AssertDirectChildren(slot,
                "CardBody", "ReadyOverlay", "EmptyContent", "OccupiedContent",
                "TopBar", "LowerDecoration", "CreatorTag");
            AssertBottomLeftRect(RequireChild(slot, "TopBar").GetComponent<RectTransform>(), layout.TopBar);
            AssertBottomLeftRect(RequireChild(slot, "ReadyOverlay").GetComponent<RectTransform>(), layout.StateOverlay);
            AssertBottomLeftRect(RequireChild(slot, "EmptyContent").GetComponent<RectTransform>(), layout.EmptyInvite);
            AssertBottomLeftRect(RequireChild(RequireChild(slot, "OccupiedContent"), "ReadyIcon").GetComponent<RectTransform>(), layout.ReadyIcon);
            AssertBottomLeftAnchor(RequireChild(RequireChild(slot, "OccupiedContent"), "ReadyLabel").GetComponent<RectTransform>(), layout.ReadyLabel);
            AssertBottomLeftRect(RequireChild(slot, "LowerDecoration").GetComponent<RectTransform>(), layout.LowerDecoration);
            AssertBottomLeftRect(RequireChild(slot, "CreatorTag").GetComponent<RectTransform>(), layout.CreatorTag);

            view.BindRoom(RoomWithGuest("654321", guestReady: true), "guest-1");
            Canvas.ForceUpdateCanvases();
            AssertBottomLeftRect(RequireChild(slot, "TopBar").GetComponent<RectTransform>(), layout.ReadyTopBar);

            var cardBody = RequireChild(slot, "CardBody");
            var readyOverlay = RequireChild(slot, "ReadyOverlay");
            var emptyContent = RequireChild(slot, "EmptyContent");
            var occupiedContent = RequireChild(slot, "OccupiedContent");
            var topBar = RequireChild(slot, "TopBar");
            var lowerDecoration = RequireChild(slot, "LowerDecoration");
            Assert.That(cardBody.GetSiblingIndex(), Is.LessThan(readyOverlay.GetSiblingIndex()));
            Assert.That(cardBody.GetSiblingIndex(), Is.LessThan(emptyContent.GetSiblingIndex()));
            Assert.That(cardBody.GetSiblingIndex(), Is.LessThan(occupiedContent.GetSiblingIndex()));
            Assert.That(topBar.GetSiblingIndex(), Is.GreaterThan(readyOverlay.GetSiblingIndex()));
            Assert.That(topBar.GetSiblingIndex(), Is.GreaterThan(emptyContent.GetSiblingIndex()));
            Assert.That(topBar.GetSiblingIndex(), Is.GreaterThan(occupiedContent.GetSiblingIndex()));
            Assert.That(lowerDecoration.GetSiblingIndex(), Is.GreaterThan(topBar.GetSiblingIndex()));

            yield return null;
        }

        [UnityTest]
        public IEnumerator RoomSlot_DecorativeLayersDoNotReceiveRaycasts()
        {
            view.BindRoom(RoomWithGuest("654321", guestReady: true), "guest-1");
            var room = RequireChild(view.transform, "LanLobbyRoot/Room");

            foreach (var slot in ChildrenWithPrefix(room, "RoomCard_"))
            {
                Assert.That(slot.GetComponentsInChildren<Button>(true), Is.Empty, slot.name);
                foreach (var graphic in slot.GetComponentsInChildren<Graphic>(true))
                {
                    Assert.That(graphic.raycastTarget, Is.False, graphic.transform.name);
                }
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator DiscoveredRooms_CapsVisibleItemsAndRetainsPrefillForVisibleRoom()
        {
            view.BindDiscoveredRooms(new[]
            {
                Discovery("100001"), Discovery("100002"), Discovery("100003"),
                Discovery("100004"), Discovery("100005"), Discovery("100006")
            });

            Assert.That(view.DiscoveryRenderedItemCountForTests, Is.EqualTo(4));
            Assert.That(view.DiscoveryOverflowCountForTests, Is.EqualTo(2));
            view.ClickDiscoveredRoomForTests("100004");
            Assert.That(view.RoomCodeTextForTests, Is.EqualTo("100004"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator LobbyRoot_HasOpaqueFullCanvasBlockerBehindTransparentTerrain()
        {
            var lobbyRoot = view.transform.Find("LanLobbyRoot");
            var blockerTransform = lobbyRoot.Find("OpaqueBlocker");
            Assert.That(blockerTransform, Is.Not.Null);
            if (blockerTransform == null) yield break;

            var blocker = blockerTransform.GetComponent<UnityEngine.UI.Image>();
            var terrain = lobbyRoot.Find("Terrain");
            var blockerRect = blocker.rectTransform;

            Assert.That(blocker.sprite, Is.Null);
            Assert.That(blocker.color.a, Is.EqualTo(1f));
            Assert.That(blockerRect.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(blockerRect.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(blockerRect.offsetMin, Is.EqualTo(Vector2.zero));
            Assert.That(blockerRect.offsetMax, Is.EqualTo(Vector2.zero));
            Assert.That(blocker.transform.GetSiblingIndex(), Is.LessThan(terrain.GetSiblingIndex()));
            yield return null;
        }

        private void OnJoinRequested(string roomCode)
        {
            joinRequests++;
        }

        private sealed class OrientedDecorationExpectation
        {
            public OrientedDecorationExpectation(
                string path,
                string spriteName,
                float centerX,
                float centerTop,
                float width,
                float height,
                float rotation,
                bool preserveAspect)
            {
                Path = path;
                SpriteName = spriteName;
                CenterX = centerX;
                CenterTop = centerTop;
                Width = width;
                Height = height;
                Rotation = rotation;
                PreserveAspect = preserveAspect;
            }

            public string Path { get; }
            public string SpriteName { get; }
            public float CenterX { get; }
            public float CenterTop { get; }
            public float Width { get; }
            public float Height { get; }
            public float Rotation { get; }
            public bool PreserveAspect { get; }
        }

        private sealed class JoinGeometryExpectation
        {
            public JoinGeometryExpectation(string name, float left, float top, float width, float height)
            {
                Name = name;
                Left = left;
                Top = top;
                Width = width;
                Height = height;
            }

            public string Name { get; }
            public float Left { get; }
            public float Top { get; }
            public float Width { get; }
            public float Height { get; }
        }

        private static void AssertMappedRoomSelectSprites(Transform home)
        {
            var expected = new Dictionary<string, string>
            {
                { "RoomSelect/RightBackground", "room_select_right_bg" },
                { "RoomSelect/TitleIcon", "room_select_title_icon" },
                { "RoomSelect/TitleDot", "room_select_dot" },
                { "RoomSelect/Create/CreateFrame/Top_0", "doc_frame_line" },
                { "RoomSelect/Create/CreateFrame/Top_1", "doc_frame_line" },
                { "RoomSelect/Create/CreateFrame/Top_2", "doc_frame_line" },
                { "RoomSelect/Create/CreateFrame/LeftUpper", "doc_frame_line" },
                { "RoomSelect/Create/CreateFrame/LeftLower", "doc_frame_line" },
                { "RoomSelect/Create/CreateFrame/RightUpper", "doc_frame_line" },
                { "RoomSelect/Create/CreateFrame/RightLower", "doc_frame_line" },
                { "RoomSelect/Create/Wings/WingLeftUpper", "img_pointer" },
                { "RoomSelect/Create/Wings/WingLeftLower", "img_pointer" },
                { "RoomSelect/Create/Wings/WingRightUpper", "img_pointer" },
                { "RoomSelect/Create/Wings/WingRightLower", "img_pointer" },
                { "RoomSelect/Create/DotTopLeft", "room_select_dot" },
                { "RoomSelect/Create/DotTopRight", "room_select_dot" },
                { "RoomSelect/Create/DotBottomLeft", "room_select_dot" },
                { "RoomSelect/Create/DotBottomRight", "room_select_dot" },
                { "RoomSelect/Create/LineLeft", "room_select_create_left_line" },
                { "RoomSelect/Create/LineRight", "room_select_create_left_line" },
                { "RoomSelect/Create/MiddleIcon", "room_select_create_middleicon" },
                { "RoomSelect/Create/Text01", "room_select_create_text_01" },
                { "RoomSelect/Create/Text02", "room_select_create_text_02" },
                { "RoomSelect/Create/StartRoomDecoration", "room_select_img_startroom" },
                { "RoomSelect/Create/CreateAction", "room_select_create_btn_bg_down" },
                { "RoomSelect/Join/LeftBlock_0", "room_select_join_left_block" },
                { "RoomSelect/Join/LeftBlock_1", "room_select_join_left_block" },
                { "RoomSelect/Join/MiddleBlock_0", "room_select_join_middle_block" },
                { "RoomSelect/Join/MiddleBlock_1", "room_select_join_middle_block" },
                { "RoomSelect/Join/MiddleBlock_2", "room_select_join_middle_block" },
                { "RoomSelect/Join/MiddleBlock_3", "room_select_join_middle_block" },
                { "RoomSelect/Join/RightBlock_0", "room_select_join_right_block" },
                { "RoomSelect/Join/RightBlock_1", "room_select_join_right_block" },
                { "RoomSelect/Join/MiddleMask", "room_select_join_middle_block_mask" },
                { "RoomSelect/Join/Logo", "room_select_join_logo" },
                { "RoomSelect/Join/Text01", "room_select_join_text_01" },
                { "RoomSelect/Join/Text02", "room_select_join_text_02" },
                { "RoomSelect/Join/Triangle", "room_select_join_triangle" },
                { "RoomSelect/Join/Blank", "room_select_join_blank" },
                { "RoomSelect/Join/Ban_0", "room_select_join_ban" },
                { "RoomSelect/Join/Ban_1", "room_select_join_ban" },
                { "RoomSelect/Join/Ban_2", "room_select_join_ban" },
                { "RoomSelect/Join/Ban_3", "room_select_join_ban" },
                { "RoomSelect/Join/RoomCodeInput", "room_select_join_text_bg" },
                { "RoomSelect/Join/JoinAction", "room_select_join_btn_bg_down" }
            };

            foreach (var item in expected)
            {
                var transform = home.Find(item.Key);
                Assert.That(transform, Is.Not.Null, item.Key);
                var image = transform.GetComponent<UnityEngine.UI.Image>();
                Assert.That(image, Is.Not.Null, item.Key);
                Assert.That(image.sprite, Is.Not.Null, item.Key);
                Assert.That(image.sprite.name, Is.EqualTo(item.Value), item.Key);
                var usesExactJoinRectangle = item.Key == "RoomSelect/Join/Triangle"
                    || item.Key == "RoomSelect/Join/Blank"
                    || item.Key == "RoomSelect/Join/RoomCodeInput";
                if (item.Key != "RoomSelect/Create/CreateAction"
                    && item.Key != "RoomSelect/Join/JoinAction"
                    && !item.Key.StartsWith("RoomSelect/Create/CreateFrame/"))
                {
                    Assert.That(image.preserveAspect, Is.EqualTo(!usesExactJoinRectangle), item.Key);
                }
            }
        }

        private static void AssertOrientedDecoration(
            RectTransform create,
            OrientedDecorationExpectation expected,
            float tolerance = .05f)
        {
            var node = create.Find(expected.Path);
            Assert.That(node, Is.Not.Null, expected.Path);
            var image = node.GetComponent<UnityEngine.UI.Image>();
            var rect = node.GetComponent<RectTransform>();
            Assert.That(image, Is.Not.Null, expected.Path);
            Assert.That(image.sprite, Is.Not.Null, expected.Path);
            Assert.That(image.sprite.name, Is.EqualTo(expected.SpriteName));
            Assert.That(rect.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(rect.anchorMax, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(rect.pivot, Is.EqualTo(new Vector2(.5f, .5f)));
            Assert.That(rect.anchoredPosition.x, Is.EqualTo(expected.CenterX).Within(tolerance));
            Assert.That(rect.anchoredPosition.y, Is.EqualTo(-expected.CenterTop).Within(tolerance));
            Assert.That(rect.sizeDelta.x, Is.EqualTo(expected.Width).Within(tolerance));
            Assert.That(rect.sizeDelta.y, Is.EqualTo(expected.Height).Within(tolerance));
            Assert.That(Mathf.DeltaAngle(rect.localEulerAngles.z, expected.Rotation), Is.EqualTo(0f).Within(tolerance));
            Assert.That(image.preserveAspect, Is.EqualTo(expected.PreserveAspect));
            Assert.That(image.raycastTarget, Is.False);
        }

        private static void AssertDecorationBehindAction(RectTransform create, string path, Transform action)
        {
            var directChild = create.Find(path);
            while (directChild.parent != create) directChild = directChild.parent;
            Assert.That(directChild.GetSiblingIndex(), Is.LessThan(action.GetSiblingIndex()), path);
        }

        private static void AssertJoinCodeNativeGeometry(RectTransform join, JoinGeometryExpectation expected, FieldInfo materialField)
        {
            var image = join.Find(expected.Name).GetComponent<Image>();
            Assert.That(image.sprite, Is.Null, expected.Name + " must remain code-native geometry.");
            Assert.IsNull(materialField.GetValue(image), expected.Name + " must not use a custom serialized material.");
            Assert.That(image.raycastTarget, Is.False, expected.Name + " must not intercept input.");
            AssertTopLeftRect(image.rectTransform, expected.Left, expected.Top, expected.Width, expected.Height, .05f);
        }

        private static void AssertVisibleSpriteScreenTopLeftRect(
            Image image,
            RectTransform designRoot,
            float left,
            float top,
            float width,
            float height,
            float tolerance)
        {
            Assert.That(VisibleSpriteScreenTopLeftRect(image, designRoot),
                Is.EqualTo(new Rect(left, top, width, height)).Using(RectComparer.Within(tolerance)), image.name);
        }

        private static Rect VisibleSpriteScreenTopLeftRect(Image image, RectTransform designRoot)
        {
            return ScreenTopLeftRect(VisibleSpriteDesignRect(image, designRoot));
        }

        private static Rect VisibleSpriteDesignRect(Image image, RectTransform designRoot)
        {
            var rect = DesignRect(image.rectTransform, designRoot);
            if (!image.preserveAspect || image.sprite == null) return rect;

            var spriteAspect = Aspect(image.sprite);
            var rectAspect = rect.width / rect.height;
            if (rectAspect > spriteAspect)
            {
                var width = rect.height * spriteAspect;
                rect.x += (rect.width - width) / 2f;
                rect.width = width;
            }
            else
            {
                var height = rect.width / spriteAspect;
                rect.y += (rect.height - height) / 2f;
                rect.height = height;
            }
            return rect;
        }

        private static Rect ScreenTopLeftRect(Rect designRect)
        {
            return new Rect(designRect.x, 1080f - designRect.yMax, designRect.width, designRect.height);
        }

        private static Rect Union(IEnumerable<Rect> rects)
        {
            var result = rects.First();
            foreach (var rect in rects.Skip(1)) result = Rect.MinMaxRect(
                Mathf.Min(result.xMin, rect.xMin), Mathf.Min(result.yMin, rect.yMin),
                Mathf.Max(result.xMax, rect.xMax), Mathf.Max(result.yMax, rect.yMax));
            return result;
        }

        private static void AssertActionRect(RectTransform action, RectTransform container, global::LanLobbyRect expected, float tolerance)
        {
            Assert.That(container.anchoredPosition.x + action.anchoredPosition.x, Is.EqualTo(expected.Left).Within(tolerance));
            Assert.That(container.anchoredPosition.y + action.anchoredPosition.y, Is.EqualTo(expected.Bottom).Within(tolerance));
            Assert.That(action.sizeDelta.x, Is.EqualTo(expected.Width).Within(tolerance));
            Assert.That(action.sizeDelta.y, Is.EqualTo(expected.Height).Within(tolerance));
        }

        private static void AssertTopLeftRect(
            RectTransform rect,
            float left,
            float top,
            float width,
            float height,
            float tolerance)
        {
            Assert.That(rect.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(rect.anchorMax, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(rect.pivot, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(rect.anchoredPosition.x, Is.EqualTo(left).Within(tolerance));
            Assert.That(rect.anchoredPosition.y, Is.EqualTo(-top).Within(tolerance));
            Assert.That(rect.sizeDelta.x, Is.EqualTo(width).Within(tolerance));
            Assert.That(rect.sizeDelta.y, Is.EqualTo(height).Within(tolerance));
        }

        private static Rect DesignRect(RectTransform target, RectTransform designRoot)
        {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            var bottomLeft = designRoot.InverseTransformPoint(corners[0]);
            var topRight = designRoot.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(
                bottomLeft.x - designRoot.rect.xMin,
                bottomLeft.y - designRoot.rect.yMin,
                topRight.x - designRoot.rect.xMin,
                topRight.y - designRoot.rect.yMin);
        }

        private static void AssertLaterRoomSelectGraphicsDoNotOverlapJoin(RectTransform roomSelect, Rect joinRect)
        {
            var join = roomSelect.Find("Join");
            var joinSiblingIndex = join.GetSiblingIndex();
            foreach (var graphic in roomSelect.GetComponentsInChildren<Graphic>(true))
            {
                if (!graphic.isActiveAndEnabled || graphic.transform.IsChildOf(join)) continue;
                var directChild = graphic.transform;
                while (directChild.parent != roomSelect) directChild = directChild.parent;
                if (directChild.GetSiblingIndex() <= joinSiblingIndex) continue;
                Assert.That(DesignRect(graphic.rectTransform, roomSelect).Overlaps(joinRect), Is.False,
                    graphic.transform.name + " is later in render/raycast order and must not overlap the Join action.");
            }
        }

        private static Button FirstInteractableButton(IEnumerable<RaycastResult> hits)
        {
            foreach (var hit in hits)
            {
                var button = hit.gameObject.GetComponentInParent<Button>();
                if (button != null && button.isActiveAndEnabled && button.interactable) return button;
            }
            return null;
        }

        private static Button RequireOnlyVisibleRoomPrimaryAction(global::LanLobbyView targetView)
        {
            var room = RequireChild(targetView.transform, "LanLobbyRoot/Room");
            var actions = room.GetComponentsInChildren<Button>(true)
                .Where(button => button.name == "PrimaryAction" && button.gameObject.activeInHierarchy)
                .ToArray();
            Assert.That(actions, Has.Length.EqualTo(1));
            Assert.That(room.Find("Ready"), Is.Null);
            Assert.That(room.Find("Start"), Is.Null);
            return actions[0];
        }

        private static void AssertButtonChildrenDoNotReceiveRaycasts(Button button)
        {
            Assert.That(button.targetGraphic, Is.SameAs(button.GetComponent<Image>()));
            Assert.That(button.targetGraphic.raycastTarget, Is.True);
            foreach (var graphic in button.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic.gameObject == button.gameObject) continue;
                Assert.That(graphic.raycastTarget, Is.False, graphic.transform.name);
            }
        }

        private static void Click(Button button)
        {
            ExecuteEvents.Execute(
                button.gameObject,
                new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left },
                ExecuteEvents.pointerClickHandler);
        }

        private static List<RaycastResult> RaycastAt(RectTransform target, Vector2 normalizedPosition)
        {
            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = ScreenPointAt(target, normalizedPosition)
            };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            return hits;
        }

        private static Vector2 ScreenPointAt(RectTransform target, Vector2 normalizedPosition)
        {
            var localPoint = new Vector2(
                Mathf.Lerp(target.rect.xMin, target.rect.xMax, normalizedPosition.x),
                Mathf.Lerp(target.rect.yMin, target.rect.yMax, normalizedPosition.y));
            return RectTransformUtility.WorldToScreenPoint(null, target.TransformPoint(localPoint));
        }

        private static void AssertScreenPointIsVisible(Vector2 point)
        {
            Assert.That(point.x, Is.InRange(0f, (float)Screen.width));
            Assert.That(point.y, Is.InRange(0f, (float)Screen.height));
        }

        private IEnumerator AssertRenderedRoomLayoutAtResolution(int width, int height)
        {
            var scaler = view.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            var lobbyRoot = RequireChild(view.transform, "LanLobbyRoot").GetComponent<RectTransform>();
            lobbyRoot.anchorMin = Vector2.zero;
            lobbyRoot.anchorMax = Vector2.zero;
            lobbyRoot.pivot = Vector2.zero;
            lobbyRoot.anchoredPosition = Vector2.zero;
            lobbyRoot.sizeDelta = new Vector2(width, height);
            Canvas.ForceUpdateCanvases();
            yield return null;
            view.ShowRoom(HostOnlyRoom("654321"), "host");
            Canvas.ForceUpdateCanvases();
            yield return null;

            var room = RequireChild(view.transform, "LanLobbyRoot/Room");
            var expected = global::LanLobbyRoomLayout.ForSize(width, height);
            AssertRenderedScreenRect(
                RequireChild(room, "RoomCard_0").GetComponent<RectTransform>(),
                expected.Slots[0].Root);
            AssertRenderedScreenRect(
                RequireChild(room, "LeaveAction").GetComponent<RectTransform>(),
                expected.LeaveAction);
            AssertRenderedScreenRect(
                RequireChild(room, "LocalLatency").GetComponent<RectTransform>(),
                expected.Latency);
            AssertRenderedScreenRect(
                RequireChild(room, "PrimaryAction").GetComponent<RectTransform>(),
                expected.PrimaryAction);
        }

        private static void AssertRenderedScreenRect(
            RectTransform actual,
            global::LanLobbyRect expected,
            float tolerance = .75f)
        {
            var corners = new Vector3[4];
            actual.GetWorldCorners(corners);
            var bottomLeft = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
            var topRight = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
            Assert.That(bottomLeft.x, Is.EqualTo(expected.Left).Within(tolerance), actual.name + " left");
            Assert.That(bottomLeft.y, Is.EqualTo(expected.Bottom).Within(tolerance), actual.name + " bottom");
            Assert.That(topRight.x - bottomLeft.x, Is.EqualTo(expected.Width).Within(tolerance), actual.name + " width");
            Assert.That(topRight.y - bottomLeft.y, Is.EqualTo(expected.Height).Within(tolerance), actual.name + " height");
        }

        private static PointerEventData PointerAt(RectTransform target)
        {
            return new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, target.TransformPoint(target.rect.center))
            };
        }

        private sealed class RectComparer : IEqualityComparer<Rect>
        {
            private readonly float tolerance;

            private RectComparer(float tolerance)
            {
                this.tolerance = tolerance;
            }

            public static RectComparer Within(float tolerance)
            {
                return new RectComparer(tolerance);
            }

            public bool Equals(Rect left, Rect right)
            {
                return Mathf.Abs(left.x - right.x) <= tolerance
                    && Mathf.Abs(left.y - right.y) <= tolerance
                    && Mathf.Abs(left.width - right.width) <= tolerance
                    && Mathf.Abs(left.height - right.height) <= tolerance;
            }

            public int GetHashCode(Rect value)
            {
                return value.GetHashCode();
            }
        }

        private static float Aspect(RectTransform rect)
        {
            return rect.sizeDelta.x / rect.sizeDelta.y;
        }

        private static float Aspect(Sprite sprite)
        {
            return sprite.rect.width / sprite.rect.height;
        }

        private static Transform RequireChild(Transform parent, string path)
        {
            var child = parent.Find(path);
            Assert.That(child, Is.Not.Null, path);
            return child;
        }

        private static void AssertDirectChildren(Transform parent, params string[] expectedNames)
        {
            Assert.That(parent.childCount, Is.EqualTo(expectedNames.Length), parent.name);
            for (var index = 0; index < expectedNames.Length; index++)
            {
                Assert.That(parent.GetChild(index).name, Is.EqualTo(expectedNames[index]), parent.name + "[" + index + "]");
            }
        }

        private static void AssertResourceSprite(Image image, string resourceName)
        {
            Assert.That(image, Is.Not.Null, resourceName);
            var expected = Resources.Load<Sprite>("UI/Lobby/" + resourceName);
            Assert.That(expected, Is.Not.Null, resourceName);
            Assert.That(image.sprite, Is.SameAs(expected), resourceName);
            Assert.That(image.sprite.name, Is.EqualTo(resourceName));
        }

        private static void AssertBottomLeftRect(RectTransform actual, global::LanLobbyRect expected, float tolerance = .05f)
        {
            AssertBottomLeftAnchoring(actual);
            Assert.That(actual.anchoredPosition.x, Is.EqualTo(expected.Left).Within(tolerance));
            Assert.That(actual.anchoredPosition.y, Is.EqualTo(expected.Bottom).Within(tolerance));
            Assert.That(actual.sizeDelta.x, Is.EqualTo(expected.Width).Within(tolerance));
            Assert.That(actual.sizeDelta.y, Is.EqualTo(expected.Height).Within(tolerance));
        }

        private static void AssertBottomLeftAnchor(
            RectTransform actual,
            global::LanLobbyRect expected,
            float tolerance = .05f)
        {
            AssertBottomLeftAnchoring(actual);
            Assert.That(actual.anchoredPosition.x, Is.EqualTo(expected.Left).Within(tolerance));
            Assert.That(actual.anchoredPosition.y, Is.EqualTo(expected.Bottom).Within(tolerance));
        }

        private static void AssertBottomLeftAnchoring(RectTransform actual)
        {
            Assert.That(actual.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(actual.anchorMax, Is.EqualTo(Vector2.zero));
            Assert.That(actual.pivot, Is.EqualTo(Vector2.zero));
        }

        private static Rect WorldRect(RectTransform transform)
        {
            var corners = new Vector3[4];
            transform.GetWorldCorners(corners);
            var minX = corners.Min(point => point.x);
            var minY = corners.Min(point => point.y);
            var maxX = corners.Max(point => point.x);
            var maxY = corners.Max(point => point.y);
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private static void AssertWorldRect(Rect actual, Rect expected, float tolerance = .05f)
        {
            Assert.That(actual.xMin, Is.EqualTo(expected.xMin).Within(tolerance));
            Assert.That(actual.yMin, Is.EqualTo(expected.yMin).Within(tolerance));
            Assert.That(actual.xMax, Is.EqualTo(expected.xMax).Within(tolerance));
            Assert.That(actual.yMax, Is.EqualTo(expected.yMax).Within(tolerance));
        }

        private static Rect RelativeToRoot(Rect child, Rect root)
        {
            return new Rect(
                child.xMin - root.xMin,
                child.yMin - root.yMin,
                child.width,
                child.height);
        }

        private static void AssertWaitingGuestSlot(Transform slot)
        {
            var slotLayout = RoomSlotLayout(slot);
            AssertDirectChildren(slot,
                "CardBody", "ReadyOverlay", "EmptyContent", "OccupiedContent",
                "TopBar", "LowerDecoration", "CreatorTag");
            AssertActiveResourceSprite(RequireChild(slot, "CardBody"), "card_bg", true);
            AssertActiveResourceSprite(RequireChild(slot, "TopBar"), "bg_top_normal", true);
            Assert.That(RequireChild(slot, "CardBody").GetComponent<Image>().color, Is.EqualTo(Color.white));
            Assert.That(RequireChild(slot, "CardBody").localScale, Is.EqualTo(Vector3.one));
            AssertBottomLeftRect(RequireChild(slot, "TopBar").GetComponent<RectTransform>(), slotLayout.TopBar);
            Assert.That(RequireChild(slot, "TopBar").GetComponent<Image>().preserveAspect, Is.True);
            AssertActiveResourceSprite(RequireChild(slot, "ReadyOverlay"), "player_card_self_frame", false);

            var emptyContent = RequireChild(slot, "EmptyContent");
            Assert.That(emptyContent.gameObject.activeSelf, Is.False);
            AssertResourceSprite(emptyContent.GetComponent<Image>(), "card_empty");
            AssertDirectChildren(emptyContent, "EmptyInviteIcon", "EmptyInviteLabel", "EmptyInviteHint");
            AssertResourceSprite(RequireChild(emptyContent, "EmptyInviteIcon").GetComponent<Image>(), "bg_plus");
            Assert.That(RequireChild(emptyContent, "EmptyInviteLabel").GetComponent<Text>().text, Is.EqualTo("邀请"));
            Assert.That(RequireChild(emptyContent, "EmptyInviteHint").GetComponent<Text>().text,
                Is.EqualTo("复制同盟密钥以邀请队友"));

            var occupiedContent = RequireChild(slot, "OccupiedContent");
            Assert.That(occupiedContent.gameObject.activeSelf, Is.False);
            AssertDirectChildren(occupiedContent, "ReadyIcon", "ReadyLabel");
            AssertActiveResourceSprite(RequireChild(occupiedContent, "ReadyIcon"), "player_card_ready", false);
            Assert.That(RequireChild(occupiedContent, "ReadyLabel").gameObject.activeSelf, Is.False);
            Assert.That(RequireChild(occupiedContent, "ReadyLabel").GetComponent<Text>().text, Is.EqualTo("已就绪"));
            AssertActiveResourceSprite(RequireChild(slot, "LowerDecoration"), "card_deco_bg", true);
            Assert.That(RequireChild(slot, "LowerDecoration").GetComponent<Image>().color,
                Is.EqualTo(new Color(.5f, .5f, .5f, 1f)));
            AssertActiveResourceSprite(RequireChild(slot, "CreatorTag"), "host_top_tag", false);
            Assert.That(slot.GetComponentsInChildren<Text>(true).Select(text => text.text),
                Has.None.EqualTo("OPEN SLOT").And.None.EqualTo("WAITING"));
        }

        private static void AssertEmptyGuestSlot(Transform slot)
        {
            var slotLayout = RoomSlotLayout(slot);
            AssertDirectChildren(slot,
                "CardBody", "ReadyOverlay", "EmptyContent", "OccupiedContent",
                "TopBar", "LowerDecoration", "CreatorTag");
            AssertActiveResourceSprite(RequireChild(slot, "CardBody"), "card_bg", true);
            AssertActiveResourceSprite(RequireChild(slot, "TopBar"), "bg_top_normal", true);
            Assert.That(RequireChild(slot, "CardBody").GetComponent<Image>().color, Is.EqualTo(Color.white));
            Assert.That(RequireChild(slot, "CardBody").localScale, Is.EqualTo(Vector3.one));
            AssertBottomLeftRect(RequireChild(slot, "TopBar").GetComponent<RectTransform>(), slotLayout.TopBar);
            Assert.That(RequireChild(slot, "TopBar").GetComponent<Image>().preserveAspect, Is.True);
            AssertActiveResourceSprite(RequireChild(slot, "ReadyOverlay"), "player_card_self_frame", false);

            var emptyContent = RequireChild(slot, "EmptyContent");
            Assert.That(emptyContent.gameObject.activeSelf, Is.True);
            AssertResourceSprite(emptyContent.GetComponent<Image>(), "card_empty");
            AssertDirectChildren(emptyContent, "EmptyInviteIcon", "EmptyInviteLabel", "EmptyInviteHint");
            AssertActiveResourceSprite(RequireChild(emptyContent, "EmptyInviteIcon"), "bg_plus", true);
            Assert.That(RequireChild(emptyContent, "EmptyInviteLabel").gameObject.activeSelf, Is.True);
            Assert.That(RequireChild(emptyContent, "EmptyInviteLabel").GetComponent<Text>().text, Is.EqualTo("邀请"));
            Assert.That(RequireChild(emptyContent, "EmptyInviteHint").gameObject.activeSelf, Is.True);
            Assert.That(RequireChild(emptyContent, "EmptyInviteHint").GetComponent<Text>().text,
                Is.EqualTo("复制同盟密钥以邀请队友"));

            var occupiedContent = RequireChild(slot, "OccupiedContent");
            Assert.That(occupiedContent.gameObject.activeSelf, Is.False);
            AssertDirectChildren(occupiedContent, "ReadyIcon", "ReadyLabel");
            AssertActiveResourceSprite(RequireChild(occupiedContent, "ReadyIcon"), "player_card_ready", false);
            Assert.That(RequireChild(occupiedContent, "ReadyLabel").gameObject.activeSelf, Is.False);
            Assert.That(RequireChild(occupiedContent, "ReadyLabel").GetComponent<Text>().text, Is.EqualTo("已就绪"));
            AssertActiveResourceSprite(RequireChild(slot, "LowerDecoration"), "card_deco_bg", true);
            Assert.That(RequireChild(slot, "LowerDecoration").GetComponent<Image>().color,
                Is.EqualTo(new Color(.5f, .5f, .5f, 1f)));
            AssertActiveResourceSprite(RequireChild(slot, "CreatorTag"), "host_top_tag", false);
            Assert.That(slot.GetComponentsInChildren<Text>(true).Select(text => text.text),
                Has.None.EqualTo("OPEN SLOT").And.None.EqualTo("WAITING"));
        }

        private static void AssertActiveResourceSprite(Transform node, string resourceName, bool expectedActive)
        {
            Assert.That(node.gameObject.activeSelf, Is.EqualTo(expectedActive), node.name);
            AssertResourceSprite(node.GetComponent<Image>(), resourceName);
        }

        private static void AssertReadyGuestSlot(Transform slot)
        {
            var slotLayout = RoomSlotLayout(slot);
            var cardBody = RequireChild(slot, "CardBody").GetComponent<Image>();
            var topBar = RequireChild(slot, "TopBar").GetComponent<Image>();
            AssertResourceSprite(cardBody, "card_bg");
            Assert.That(cardBody.color, Is.EqualTo(new Color(0f, 220f / 255f, 220f / 255f, 1f)));
            Assert.That(cardBody.rectTransform.localScale, Is.EqualTo(new Vector3(1f, -1f, 1f)),
                "Ready cards flip the source gradient so its light cyan region sits behind the ready status.");
            AssertResourceSprite(topBar, "bg_top_ready");
            AssertBottomLeftRect(topBar.rectTransform, slotLayout.ReadyTopBar);
            Assert.That(topBar.preserveAspect, Is.False,
                "The measured ready top bar uses an intentional 8 px vertical overhang instead of source-aspect fitting.");

            var readyOverlay = RequireChild(slot, "ReadyOverlay");
            Assert.That(readyOverlay.gameObject.activeSelf, Is.True);
            AssertResourceSprite(readyOverlay.GetComponent<Image>(), "player_card_self_frame");
            Assert.That(readyOverlay.GetComponent<Image>().preserveAspect, Is.True);
            Assert.That(Aspect(readyOverlay.GetComponent<RectTransform>()),
                Is.EqualTo(Aspect(readyOverlay.GetComponent<Image>().sprite)).Within(.0001f));

            Assert.That(RequireChild(slot, "EmptyContent").gameObject.activeSelf, Is.False);
            var occupiedContent = RequireChild(slot, "OccupiedContent");
            Assert.That(occupiedContent.gameObject.activeSelf, Is.True);
            AssertDirectChildren(occupiedContent, "ReadyIcon", "ReadyLabel");
            var readyIcon = RequireChild(occupiedContent, "ReadyIcon").GetComponent<Image>();
            AssertResourceSprite(readyIcon, "player_card_ready");
            Assert.That(readyIcon.preserveAspect, Is.True);
            Assert.That(Aspect(readyIcon.rectTransform), Is.EqualTo(Aspect(readyIcon.sprite)).Within(.0001f));
            AssertBottomLeftRect(readyIcon.rectTransform, slotLayout.ReadyIcon);
            AssertBottomLeftAnchor(
                RequireChild(occupiedContent, "ReadyLabel").GetComponent<RectTransform>(),
                slotLayout.ReadyLabel);
            Assert.That(RequireChild(occupiedContent, "ReadyLabel").GetComponent<Text>().text, Is.EqualTo("已就绪"));

            var lowerDecoration = RequireChild(slot, "LowerDecoration");
            Assert.That(lowerDecoration.gameObject.activeSelf, Is.True);
            AssertResourceSprite(lowerDecoration.GetComponent<Image>(), "card_deco_self");
            Assert.That(lowerDecoration.GetComponent<Image>().color, Is.EqualTo(Color.white));
            Assert.That(lowerDecoration.GetComponent<Image>().preserveAspect, Is.True);
            Assert.That(RequireChild(slot, "CreatorTag").gameObject.activeSelf, Is.False);
        }

        private static global::LanLobbyRoomSlotLayout RoomSlotLayout(Transform slot)
        {
            const string prefix = "RoomCard_";
            Assert.That(slot.name.StartsWith(prefix, System.StringComparison.Ordinal), Is.True, slot.name);
            var index = int.Parse(slot.name.Substring(prefix.Length));
            return global::LanLobbyRoomLayout.ForSize(1920, 1080).Slots[index];
        }

        private static List<Transform> ChildrenWithPrefix(Transform parent, string prefix)
        {
            var result = new List<Transform>();
            for (var index = 0; index < parent.childCount; index++)
            {
                var child = parent.GetChild(index);
                if (child.name.StartsWith(prefix, System.StringComparison.Ordinal)) result.Add(child);
            }
            return result;
        }

        private static LobbyDiscoveryEntry Discovery(string roomCode, int memberCount = 1, int capacity = 4, bool joinable = true)
        {
            return new LobbyDiscoveryEntry(roomCode, "Host", memberCount, capacity, joinable, 12345, 1);
        }

        private static LobbyRoomSnapshot HostOnlyRoom(string roomCode)
        {
            return new LobbyRoomSnapshot(roomCode, "host", new[]
            {
                new LobbyMemberSnapshot(new LobbyProfile("host", "Host", 0), true, 42)
            }, false, 1);
        }

        private static LobbyRoomSnapshot RoomWithGuest(string roomCode, bool guestReady)
        {
            return new LobbyRoomSnapshot(roomCode, "host", new[]
            {
                new LobbyMemberSnapshot(new LobbyProfile("host", "Host", 0), true, 42),
                new LobbyMemberSnapshot(new LobbyProfile("guest-1", "Guest One", 1), guestReady, 60)
            }, false, 1);
        }

        private static LobbyRoomSnapshot Room(string roomCode, bool hasStarted = false, bool everyoneReady = false)
        {
            return new LobbyRoomSnapshot(roomCode, "host", new[]
            {
                new LobbyMemberSnapshot(new LobbyProfile("host", "Host", 0), true, 42),
                new LobbyMemberSnapshot(new LobbyProfile("guest-1", "Guest One", 1), everyoneReady, 60),
                new LobbyMemberSnapshot(new LobbyProfile("guest-2", "Guest Two", 2), true, 80),
                new LobbyMemberSnapshot(new LobbyProfile("guest-3", "Guest Three", 3), everyoneReady, 100)
            }, hasStarted, 1);
        }
    }
}
