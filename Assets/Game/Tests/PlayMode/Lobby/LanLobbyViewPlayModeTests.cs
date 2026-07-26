using System.Collections;
using System.Collections.Generic;
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

        [UnitySetUp]
        public IEnumerator SetUp()
        {
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
            Assert.That(ChildrenWithPrefix(join, "Blank_").Count, Is.EqualTo(LobbyRoomCode.Length));

            view.BindDiscoveredRooms(new[] { Discovery("654321") });
            view.ClickDiscoveredRoomForTests("654321");
            Assert.That(view.RoomCodeTextForTests, Is.EqualTo("654321"));
            Assert.That(joinRequests, Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RoomView_RestoresLegacyForegroundAndHomeAddsCompleteRoomSelectAffordances()
        {
            var home = view.transform.Find("LanLobbyRoot/Home");
            var room = view.transform.Find("LanLobbyRoot/Room");
            var grid = view.transform.Find("LanLobbyRoot/GridForeground");

            Assert.That(grid.gameObject.activeSelf, Is.False);
            view.ShowRoom();
            Assert.That(room.gameObject.activeSelf, Is.True);
            Assert.That(grid.gameObject.activeSelf, Is.True);
            view.ShowHome();
            Assert.That(grid.gameObject.activeSelf, Is.False);

            Assert.That(home.Find("RoomSelect/PanelFrame/Top"), Is.Not.Null);
            Assert.That(home.Find("RoomSelect/PanelFrame/Bottom"), Is.Not.Null);
            Assert.That(home.Find("RoomSelect/PanelFrame/Divider"), Is.Not.Null);
            Assert.That(home.Find("RoomSelect/Join/SimulationInvite"), Is.Not.Null);
            Assert.That(home.Find("RoomSelect/Join/SimulationInvite/ActionIcon").GetComponent<UnityEngine.UI.Image>().sprite.name,
                Is.EqualTo("join_icon"));
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
                new DecorationExpectation("LogoLeft", "room_select_create_logo", 272f, 30f, 107f, 135f, 180f, false),
                new DecorationExpectation("LogoRight", "room_select_create_logo", 539f, 30f, 109f, 133f, 0f, false),
                new DecorationExpectation("DotTopLeft", "room_select_dot", 381f, 21f, 19f, 19f, 0f, true),
                new DecorationExpectation("DotTopRight", "room_select_dot", 516f, 21f, 19f, 19f, 0f, true),
                new DecorationExpectation("DotBottomLeft", "room_select_dot", 381f, 157f, 19f, 19f, 0f, true),
                new DecorationExpectation("DotBottomRight", "room_select_dot", 516f, 157f, 19f, 19f, 0f, true),
                new DecorationExpectation("LineLeft", "room_select_create_left_line", 393f, 54f, 20f, 20f * 40f / 12f, 0f, true),
                new DecorationExpectation("LineRight", "room_select_create_left_line", 506f, 54f, 20f, 20f * 40f / 12f, 180f, true),
                new DecorationExpectation("MiddleIcon", "room_select_create_middleicon", 415f, 44f, 89f, 89f * 62f / 63f, 0f, true),
                new DecorationExpectation("Text01", "room_select_create_text_01", 415f, 137f, 89f, 89f * 9f / 63f, 0f, true),
                new DecorationExpectation("Text02", "room_select_create_text_02", 428f, 151f, 66f, 66f * 5f / 46f, 0f, true),
                new DecorationExpectation("StartRoomDecoration", "room_select_img_startroom", 416f, 16f, 87f, 87f * 8f / 64f, 0f, true)
            };

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
                var decoration = create.Find(item.Name);
                Assert.That(decoration, Is.Not.Null, item.Name);
                var image = decoration.GetComponent<Image>();
                Assert.That(image, Is.Not.Null, item.Name);
                AssertTopLeftRect(image.rectTransform, item.Left, item.Top, item.Width, item.Height, .05f);
                Assert.That(image.sprite.name, Is.EqualTo(item.SpriteName));
                Assert.That(Mathf.DeltaAngle(image.rectTransform.localEulerAngles.z, item.Rotation), Is.EqualTo(0f).Within(.05f));
                Assert.That(image.preserveAspect, Is.EqualTo(item.PreserveAspect));
                Assert.That(image.raycastTarget, Is.False);
                Assert.That(image.transform.GetSiblingIndex(), Is.LessThan(createAction.GetSiblingIndex()));
            }
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
            view.BindRoom(Room("654321", everyoneReady: true), "missing");
            Assert.That(view.ReadyInteractableForTests, Is.False);
            Assert.That(view.StartInteractableForTests, Is.False);

            view.BindRoom(Room("654321", everyoneReady: true), "host");
            Assert.That(view.ReadyInteractableForTests, Is.True);
            Assert.That(view.StartInteractableForTests, Is.True);

            view.BindRoom(Room("654321", hasStarted: true, everyoneReady: true), "host");
            Assert.That(view.ReadyInteractableForTests, Is.False);
            Assert.That(view.StartInteractableForTests, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RoomSnapshot_RendersEachMemberAvatarIndexInItsCard()
        {
            view.BindRoom(Room("654321"), "host");

            Assert.That(view.RoomCardAvatarTextForTests(0), Is.EqualTo("A1"));
            Assert.That(view.RoomCardAvatarTextForTests(1), Is.EqualTo("A2"));
            Assert.That(view.RoomCardAvatarTextForTests(2), Is.EqualTo("A3"));
            Assert.That(view.RoomCardAvatarTextForTests(3), Is.EqualTo("A4"));
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

        private sealed class DecorationExpectation
        {
            public DecorationExpectation(
                string name,
                string spriteName,
                float left,
                float top,
                float width,
                float height,
                float rotation,
                bool preserveAspect)
            {
                Name = name;
                SpriteName = spriteName;
                Left = left;
                Top = top;
                Width = width;
                Height = height;
                Rotation = rotation;
                PreserveAspect = preserveAspect;
            }

            public string Name { get; }
            public string SpriteName { get; }
            public float Left { get; }
            public float Top { get; }
            public float Width { get; }
            public float Height { get; }
            public float Rotation { get; }
            public bool PreserveAspect { get; }
        }

        private static void AssertMappedRoomSelectSprites(Transform home)
        {
            var expected = new Dictionary<string, string>
            {
                { "RoomSelect/RightBackground", "room_select_right_bg" },
                { "RoomSelect/TitleIcon", "room_select_title_icon" },
                { "RoomSelect/TitleDot", "room_select_dot" },
                { "RoomSelect/Create/LogoLeft", "room_select_create_logo" },
                { "RoomSelect/Create/LogoRight", "room_select_create_logo" },
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
                { "RoomSelect/Join/Logo", "room_select_join_logo" },
                { "RoomSelect/Join/Text01", "room_select_join_text_01" },
                { "RoomSelect/Join/Text02", "room_select_join_text_02" },
                { "RoomSelect/Join/Triangle", "room_select_join_triangle" },
                { "RoomSelect/Join/Blank_0", "room_select_join_blank" },
                { "RoomSelect/Join/Blank_1", "room_select_join_blank" },
                { "RoomSelect/Join/Blank_2", "room_select_join_blank" },
                { "RoomSelect/Join/Blank_3", "room_select_join_blank" },
                { "RoomSelect/Join/Blank_4", "room_select_join_blank" },
                { "RoomSelect/Join/Blank_5", "room_select_join_blank" },
                { "RoomSelect/Join/RoomCodeInput", "room_select_join_text_bg" },
                { "RoomSelect/Join/Ban", "room_select_join_ban" },
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
                if (item.Key != "RoomSelect/Create/CreateAction"
                    && item.Key != "RoomSelect/Join/JoinAction"
                    && item.Key != "RoomSelect/Create/LogoLeft"
                    && item.Key != "RoomSelect/Create/LogoRight")
                {
                    Assert.That(image.preserveAspect, Is.True, item.Key);
                }
            }
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
