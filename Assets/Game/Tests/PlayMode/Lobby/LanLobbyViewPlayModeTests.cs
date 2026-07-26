using System.Collections;
using System.Collections.Generic;
using ArknoNights.Lobby;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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
            AssertMappedRoomSelectSprites(home);

            view.BindDiscoveredRooms(new[] { Discovery("654321") });
            view.ClickDiscoveredRoomForTests("654321");
            Assert.That(view.RoomCodeTextForTests, Is.EqualTo("654321"));
            Assert.That(joinRequests, Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HomeRoomSelect_UsesPreservedAssetProportionsWithinRightSideBounds()
        {
            var home = view.transform.Find("LanLobbyRoot/Home");
            var layout = global::LanLobbyLayout.ForSize(1920, 1080, 4);
            var create = home.Find("RoomSelect/Create").GetComponent<RectTransform>();
            var join = home.Find("RoomSelect/Join").GetComponent<RectTransform>();
            var background = home.Find("RoomSelect/RightBackground").GetComponent<UnityEngine.UI.Image>();
            var createAction = home.Find("RoomSelect/Create/CreateAction").GetComponent<UnityEngine.UI.Image>();
            var joinAction = home.Find("RoomSelect/Join/JoinAction").GetComponent<UnityEngine.UI.Image>();

            Assert.That(create.anchoredPosition.x, Is.GreaterThanOrEqualTo(960f));
            Assert.That(join.anchoredPosition.x, Is.GreaterThanOrEqualTo(960f));
            Assert.That(create.anchoredPosition.x + create.sizeDelta.x, Is.LessThanOrEqualTo(1920f));
            Assert.That(join.anchoredPosition.x + join.sizeDelta.x, Is.LessThanOrEqualTo(1920f));
            Assert.That(create.anchoredPosition.x, Is.EqualTo(layout.RoomSelectCreate.Left));
            Assert.That(join.anchoredPosition.x, Is.EqualTo(layout.RoomSelectJoin.Left));
            Assert.That(background.preserveAspect, Is.True);
            Assert.That(createAction.preserveAspect, Is.True);
            Assert.That(joinAction.preserveAspect, Is.True);
            Assert.That(Aspect(createAction.rectTransform), Is.EqualTo(Aspect(createAction.sprite)).Within(.01f));
            Assert.That(Aspect(joinAction.rectTransform), Is.EqualTo(Aspect(joinAction.sprite)).Within(.01f));
            Assert.That(Aspect(background.rectTransform), Is.EqualTo(Aspect(background.sprite)).Within(.01f));
            yield return null;
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

        private static void AssertMappedRoomSelectSprites(Transform home)
        {
            var expected = new Dictionary<string, string>
            {
                { "RoomSelect/RightBackground", "room_select_right_bg" },
                { "RoomSelect/TitleIcon", "room_select_title_icon" },
                { "RoomSelect/TitleDot", "room_select_dot" },
                { "RoomSelect/StartRoomDecoration", "room_select_img_startroom" },
                { "RoomSelect/DiscoveredRooms", "room_select_join_text_bg" },
                { "RoomSelect/Create/LeftLine", "room_select_create_left_line" },
                { "RoomSelect/Create/Logo", "room_select_create_logo" },
                { "RoomSelect/Create/MiddleIcon", "room_select_create_middleicon" },
                { "RoomSelect/Create/Text01", "room_select_create_text_01" },
                { "RoomSelect/Create/Text02", "room_select_create_text_02" },
                { "RoomSelect/Create/CreateAction", "room_select_create_btn_bg_down" },
                { "RoomSelect/Join/LeftBlock", "room_select_join_left_block" },
                { "RoomSelect/Join/MiddleBlock", "room_select_join_middle_block" },
                { "RoomSelect/Join/MiddleBlockMask", "room_select_join_middle_block_mask" },
                { "RoomSelect/Join/RightBlock", "room_select_join_right_block" },
                { "RoomSelect/Join/Logo", "room_select_join_logo" },
                { "RoomSelect/Join/TextBackground", "room_select_join_text_bg" },
                { "RoomSelect/Join/Text01", "room_select_join_text_01" },
                { "RoomSelect/Join/Text02", "room_select_join_text_02" },
                { "RoomSelect/Join/Triangle", "room_select_join_triangle" },
                { "RoomSelect/Join/Blank_0", "room_select_join_blank" },
                { "RoomSelect/Join/Blank_1", "room_select_join_blank" },
                { "RoomSelect/Join/Blank_2", "room_select_join_blank" },
                { "RoomSelect/Join/Blank_3", "room_select_join_blank" },
                { "RoomSelect/Join/Blank_4", "room_select_join_blank" },
                { "RoomSelect/Join/Blank_5", "room_select_join_blank" },
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
                Assert.That(image.preserveAspect, Is.True, item.Key);
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
