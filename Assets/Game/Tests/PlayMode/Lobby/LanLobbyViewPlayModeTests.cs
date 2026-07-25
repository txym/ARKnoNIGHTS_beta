using System.Collections;
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

        private void OnJoinRequested(string roomCode)
        {
            joinRequests++;
        }

        private static LobbyDiscoveryEntry Discovery(string roomCode, int memberCount = 1, int capacity = 4, bool joinable = true)
        {
            return new LobbyDiscoveryEntry(roomCode, "Host", memberCount, capacity, joinable, 12345, 1);
        }

        private static LobbyRoomSnapshot Room(string roomCode)
        {
            return new LobbyRoomSnapshot(roomCode, "host", new[]
            {
                new LobbyMemberSnapshot(new LobbyProfile("host", "Host", 0), true, 42),
                new LobbyMemberSnapshot(new LobbyProfile("guest-1", "Guest One", 1), false, 60),
                new LobbyMemberSnapshot(new LobbyProfile("guest-2", "Guest Two", 2), true, 80),
                new LobbyMemberSnapshot(new LobbyProfile("guest-3", "Guest Three", 3), false, 100)
            }, false, 1);
        }
    }
}
