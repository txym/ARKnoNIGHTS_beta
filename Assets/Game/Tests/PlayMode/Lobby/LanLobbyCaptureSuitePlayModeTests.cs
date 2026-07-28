using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ArknoNights.Lobby.Tests
{
    public sealed class LanLobbyCaptureSuitePlayModeTests
    {
        private string outputDirectory;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            outputDirectory = Path.Combine(Application.temporaryCachePath, "LanLobbyCaptureSuiteTests", Guid.NewGuid().ToString("N"));
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, true);
            var fixture = GameObject.Find("LanLobbyCaptureSuiteTests");
            if (fixture != null) UnityEngine.Object.Destroy(fixture);
            foreach (var eventSystem in Resources.FindObjectsOfTypeAll<UnityEngine.EventSystems.EventSystem>())
                if (eventSystem != null && eventSystem.name == "LanLobbyEventSystem") UnityEngine.Object.Destroy(eventSystem.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CaptureSuite_EmitsHomeAndRoomRecords()
        {
            var suiteType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("LanLobbyCaptureSuite"))
                .FirstOrDefault(type => type != null);
            Assert.That(suiteType, Is.Not.Null, "The Player capture suite must be available from the Initial presentation assembly.");
            var capture = suiteType.GetMethod("CaptureForTests", BindingFlags.Public | BindingFlags.Static);
            Assert.That(capture, Is.Not.Null, "The suite needs a test-only capture entry that uses the production view.");

            var routine = capture.Invoke(null, new object[] { outputDirectory }) as IEnumerator;
            Assert.That(routine, Is.Not.Null);
            yield return routine;

            var manifestPath = Path.Combine(outputDirectory, "manifest.json");
            Assert.That(File.Exists(manifestPath), Is.True);
            var manifest = File.ReadAllText(manifestPath);
            var expectedNames = new[] { "home", "discovered-prefill", "room-host", "room-ready", "room-full" };
            foreach (var name in expectedNames)
            {
                StringAssert.Contains("\"name\": \"" + name + "\"", manifest);
                Assert.That(File.Exists(Path.Combine(outputDirectory, name + ".png")), Is.True);
            }

            StringAssert.Contains("\"canvasScale\"", manifest);
            StringAssert.Contains("\"roomCode\"", manifest);
            StringAssert.Contains("\"members\"", manifest);
            StringAssert.Contains("\"localLatencyMilliseconds\"", manifest);
            StringAssert.Contains("\"rects\"", manifest);
            StringAssert.Contains("\"spriteSources\"", manifest);
            StringAssert.Contains("\"unityText\"", manifest);
            StringAssert.Contains("\"codeNativeGeometry\"", manifest);
            StringAssert.Contains("[uc]autochessouter/", manifest);

            var parsed = JsonUtility.FromJson<CaptureManifestProbe>(manifest);
            Assert.That(parsed.captures, Has.Length.EqualTo(expectedNames.Length));
            foreach (var captureRecord in parsed.captures)
            {
                Assert.That(captureRecord.canvasScale, Is.GreaterThan(0f), captureRecord.name + " must report a positive canvas scale.");
                Assert.That(captureRecord.spriteSources, Is.Not.Null.And.Not.Empty, captureRecord.name + " must include sprite provenance.");
                foreach (var spriteSource in captureRecord.spriteSources)
                {
                    Assert.That(spriteSource.node, Is.Not.Null.And.Not.Empty);
                    Assert.That(spriteSource.spriteName, Is.Not.Null.And.Not.Empty);
                    Assert.That(spriteSource.sourcePath, Is.Not.Null.And.Not.Empty);
                    Assert.That(spriteSource.coordinateOrigin, Is.EqualTo("screen-bottom-left"));
                    Assert.That(spriteSource.unit, Is.EqualTo("px"));
                    Assert.That(spriteSource.width, Is.GreaterThan(0f));
                    Assert.That(spriteSource.height, Is.GreaterThan(0f));
                }
                Assert.That(captureRecord.unityText, Is.Not.Null.And.Not.Empty,
                    captureRecord.name + " must enumerate current active rendered Unity Text.");
                foreach (var text in captureRecord.unityText)
                {
                    Assert.That(text.node, Does.StartWith("LanLobbyRoot/"));
                    Assert.That(text.text, Is.Not.Null.And.Not.Empty);
                    Assert.That(text.fontName, Is.Not.Null.And.Not.Empty);
                    Assert.That(text.fontResourcePath, Is.Empty,
                        "The runtime capture cannot prove a Resources path and must not invent one.");
                    Assert.That(text.hasBitmapSource, Is.False);
                    Assert.That(text.bitmapSourcePath, Is.Empty,
                        "Unity Text must explicitly have no material-library bitmap source.");
                }
                Assert.That(captureRecord.codeNativeGeometry.Any(geometry => geometry.name == "LanLobbyRoot/OpaqueBlocker"), Is.True,
                    captureRecord.name + " must report the visible non-bitmap OpaqueBlocker.");
                Assert.That(captureRecord.spriteSources.Any(sprite => sprite.node.Contains("SimulationInvite")), Is.False,
                    captureRecord.name + " must not retain a SimulationInvite Sprite source.");
                Assert.That(captureRecord.unityText.Any(text => text.node.Contains("SimulationInvite")), Is.False,
                    captureRecord.name + " must not retain a SimulationInvite text row.");
                Assert.That(captureRecord.codeNativeGeometry.Any(geometry => geometry.name.Contains("SimulationInvite")), Is.False,
                    captureRecord.name + " must not retain SimulationInvite geometry.");
            }

            var discovered = parsed.captures.Single(record => record.name == "discovered-prefill");
            Assert.That(discovered.roomCode, Is.EqualTo("654321"));
            Assert.That(discovered.members, Is.Empty);

            var home = parsed.captures.Single(record => record.name == "home");
            var homeStates = new[] { home, discovered };
            foreach (var homeState in homeStates)
            {
                Assert.That(homeState.canvasScale, Is.GreaterThan(0f), homeState.name + " must report a positive canvas scale.");
                Assert.That(homeState.spriteSources.Count(sprite => sprite.spriteName == "doc_frame_line"), Is.EqualTo(7));
                Assert.That(homeState.spriteSources.Count(sprite => sprite.spriteName == "img_pointer"), Is.EqualTo(4));
                Assert.That(homeState.spriteSources.Count(sprite => sprite.spriteName == "room_select_create_logo"), Is.Zero);
                Assert.That(homeState.spriteSources.Any(sprite =>
                    sprite.node == "LanLobbyRoot/Home/RoomSelect/Create/CreateAction"), Is.True,
                    homeState.name + " must include the frozen CreateAction sprite-source row; "
                    + "last-sibling order is verified by the View hierarchy assertion.");

                const string interiorBackingPath = "LanLobbyRoot/Home/RoomSelect/Create/InteriorBacking";
                Assert.That(homeState.codeNativeGeometry.Any(geometry => geometry.name == interiorBackingPath), Is.True,
                    homeState.name + " must report the visible sprite-null InteriorBacking.");
                var interiorBacking = homeState.codeNativeGeometry.Single(geometry => geometry.name == interiorBackingPath);
                Assert.That(interiorBacking.kind, Is.EqualTo("code-native-geometry"));
                Assert.That(interiorBacking.isBitmap, Is.False);
                Assert.That(interiorBacking.color, Is.EqualTo("#000000C7"));
                Assert.That(interiorBacking.width, Is.GreaterThan(0f));
                Assert.That(interiorBacking.height, Is.GreaterThan(0f));
                Assert.That(homeState.spriteSources.Count(sprite => sprite.spriteName == "join_icon"), Is.EqualTo(1),
                    homeState.name + " must contain only the JoinAction icon after removing SimulationInvite.");
                AssertJoinBitmapInventory(homeState);
                AssertHomeJoinGeometry(homeState);
            }
            Assert.That(homeStates.Sum(record =>
                record.spriteSources.Count(sprite => sprite.spriteName == "doc_frame_line")), Is.EqualTo(14));
            Assert.That(homeStates.Sum(record =>
                record.spriteSources.Count(sprite => sprite.spriteName == "img_pointer")), Is.EqualTo(8));
            Assert.That(homeStates.Sum(record =>
                record.spriteSources.Count(sprite => sprite.spriteName == "room_select_create_logo")), Is.Zero);

            AssertActionRect(home, "LanLobbyRoot/Home/RoomSelect/Create/CreateAction");
            AssertActionRect(home, "LanLobbyRoot/Home/RoomSelect/Join/JoinAction");
            Assert.That(home.unityText.Any(text => text.text == "LOCAL IDENTITY"), Is.True,
                "Home identity title must be captured dynamically.");
            Assert.That(home.unityText.Any(text => text.text == "Doctor"), Is.True,
                "Current profile input text must be captured dynamically.");
            Assert.That(home.unityText.Any(text => text.text == "创建同盟"), Is.True,
                "Create action text must be captured dynamically.");
            Assert.That(home.unityText.Any(text => text.text == "加入同盟"), Is.True,
                "Join action text must be captured dynamically.");
            Assert.That(home.unityText.Any(text => text.text == "DISCOVERING LOCAL ROOMS"), Is.True,
                "Current Home status text must be captured dynamically.");
            Assert.That(discovered.unityText.Any(text => text.text.Contains("654321") && text.text.Contains("Doctor")), Is.True,
                "The rendered discovered-room row must be captured dynamically.");
            var roomHost = parsed.captures.Single(record => record.name == "room-host");
            Assert.That(roomHost.unityText.Any(text => text.text == "18 ms"), Is.True,
                "Room latency must be captured dynamically.");
            Assert.That(roomHost.unityText.Any(text => text.text == "654321"), Is.True,
                "Room code must be captured dynamically.");
            AssertAuthoritativeRoomCapture(
                roomHost,
                new[]
                {
                    new MemberExpectation("capture-host", "Doctor", true)
                },
                true,
                "btn_match_normal");
            AssertAuthoritativeRoomCapture(
                parsed.captures.Single(record => record.name == "room-full"),
                new[]
                {
                    new MemberExpectation("capture-host", "Doctor", true),
                    new MemberExpectation("capture-guest-1", "Amiya", false),
                    new MemberExpectation("capture-guest-2", "Chen", false),
                    new MemberExpectation("capture-guest-3", "Kal'tsit", false)
                },
                false,
                "btn_match_grey");
            AssertAuthoritativeRoomCapture(
                parsed.captures.Single(record => record.name == "room-ready"),
                new[]
                {
                    new MemberExpectation("capture-host", "Doctor", true),
                    new MemberExpectation("capture-guest-1", "Amiya", true),
                    new MemberExpectation("capture-guest-2", "Chen", true),
                    new MemberExpectation("capture-guest-3", "Kal'tsit", true)
                },
                true,
                "btn_match_normal");
            Assert.That(home.spriteSources.Any(sprite =>
                    sprite.spriteName.StartsWith("room_select_", StringComparison.Ordinal) &&
                    sprite.sourcePath.StartsWith("[uc]autochessouter/room_select_", StringComparison.Ordinal)),
                Is.True,
                "The Home capture manifest must prove it rendered an approved room_select_ source.");
            Assert.That(home.spriteSources.Any(sprite =>
                    sprite.spriteName.StartsWith("icon_", StringComparison.Ordinal) &&
                    sprite.sourcePath.StartsWith("Combined/[uc]autochesscommon/icon_", StringComparison.Ordinal)),
                Is.True,
                "The Home capture manifest must prove it rendered an approved Combined avatar source.");
            Assert.That(home.spriteSources.Any(sprite => sprite.spriteName == "shallow_main"), Is.False,
                "The Home provenance table must exclude inactive legacy foreground sprites.");
            Assert.That(home.spriteSources.Count(sprite => sprite.spriteName == "join_icon"), Is.EqualTo(1),
                "Home must count only the accepted JoinAction icon.");
            Assert.That(home.spriteSources.Count(sprite => sprite.spriteName == "room_select_create_logo"), Is.Zero);
            Assert.That(home.spriteSources.Count(sprite => sprite.spriteName == "img_pointer"), Is.EqualTo(4));
            Assert.That(home.spriteSources.Count(sprite => sprite.spriteName == "doc_frame_line"), Is.EqualTo(7));
            Assert.That(home.spriteSources.Count(sprite => sprite.spriteName == "room_select_create_left_line"), Is.EqualTo(2));
            Assert.That(home.spriteSources.Count(sprite => sprite.spriteName == "room_select_dot"), Is.EqualTo(5));
            Assert.That(home.spriteSources
                    .Where(sprite => sprite.spriteName.StartsWith("room_select_create_", StringComparison.Ordinal)
                        || sprite.spriteName == "room_select_dot"
                        || sprite.spriteName == "room_select_img_startroom"
                        || sprite.spriteName == "img_pointer"
                        || sprite.spriteName == "doc_frame_line")
                    .All(sprite => sprite.sourcePath.StartsWith("[uc]autochessouter/", StringComparison.Ordinal)
                        && !sprite.sourcePath.Contains("$0")
                        && !sprite.sourcePath.Contains("#0")), Is.True);
            Assert.That(discovered.spriteSources.Count(sprite => sprite.spriteName == "join_icon"), Is.EqualTo(1),
                "Discovered Home must count only the accepted JoinAction icon.");
            Assert.That(homeStates.Sum(record => record.spriteSources.Count(sprite => sprite.spriteName == "join_icon")), Is.EqualTo(2),
                "Rendered join_icon occurrences must sum to two across the two Home states.");
            Assert.That(home.spriteSources.Select(sprite => sprite.node), Is.Unique,
                "Each Sprite usage row must identify one stable rendered node.");
            Assert.That(roomHost.spriteSources.Any(sprite => sprite.spriteName == "shallow_main"), Is.False,
                "The Room provenance table must exclude the inactive stretch-distorted legacy foreground.");
            Assert.That(roomHost.codeNativeGeometry.Select(geometry => geometry.name), Is.EquivalentTo(new[] { "LanLobbyRoot/OpaqueBlocker" }),
                "Room must report the visible blocker but no Home-only frame geometry.");
            foreach (var room in parsed.captures.Where(record => record.name.StartsWith("room-", StringComparison.Ordinal)))
                Assert.That(room.codeNativeGeometry.Any(geometry => geometry.name.Contains("PanelFrame")), Is.False,
                    room.name + " must not report inactive Home-only frame geometry.");
        }

        private static void AssertAuthoritativeRoomCapture(
            CaptureRecordProbe capture,
            IReadOnlyList<MemberExpectation> expectedMembers,
            bool primaryActionInteractable,
            string primarySpriteName)
        {
            Assert.That(capture.width, Is.EqualTo(1920), capture.name + " must remain a 1920x1080 capture.");
            Assert.That(capture.height, Is.EqualTo(1080), capture.name + " must remain a 1920x1080 capture.");
            Assert.That(capture.localPlayerId, Is.EqualTo("capture-host"));
            Assert.That(capture.primaryActionInteractable, Is.EqualTo(primaryActionInteractable));
            Assert.That(capture.members, Has.Length.EqualTo(expectedMembers.Count));
            for (var index = 0; index < expectedMembers.Count; index++)
            {
                Assert.That(capture.members[index].playerId, Is.EqualTo(expectedMembers[index].PlayerId));
                Assert.That(capture.members[index].displayName, Is.EqualTo(expectedMembers[index].DisplayName));
                Assert.That(capture.members[index].isReady, Is.EqualTo(expectedMembers[index].IsReady));
            }

            Assert.That(capture.unityText.Count(text => text.text == "协议启动"), Is.EqualTo(1),
                capture.name + " must expose exactly one host primary-action label.");
            Assert.That(capture.spriteSources.Count(sprite =>
                sprite.node == "LanLobbyRoot/Room/PrimaryAction" &&
                sprite.spriteName == primarySpriteName), Is.EqualTo(1));
            AssertRoomKeyRects(capture, expectedMembers);
            AssertRoomSourceAudit(capture);

            Assert.That(capture.unityText.Any(text =>
                string.Equals(text.text, "OPEN SLOT", StringComparison.Ordinal) ||
                string.Equals(text.text, "WAITING", StringComparison.Ordinal)), Is.False,
                capture.name + " must not render legacy placeholder literals.");
            Assert.That(HasRenderedHostProfile(capture, "Doctor", "capture-host"), Is.False,
                capture.name + " must not render the host display name, player ID, or avatar bitmap.");
            var semanticNodes = capture.keyRects.Select(rect => rect.name)
                .Concat(capture.spriteSources.Select(sprite => sprite.node))
                .Concat(capture.unityText.Select(text => text.node))
                .Concat(capture.sourceAudit.Select(item => item.node))
                .ToArray();
            Assert.That(semanticNodes.Any(IsHostProfileNode), Is.False,
                capture.name + " must not contain host portrait/avatar/profile/name/id nodes.");
        }

        private static void AssertRoomKeyRects(
            CaptureRecordProbe capture,
            IReadOnlyList<MemberExpectation> expectedMembers)
        {
            Assert.That(capture.keyRects, Is.Not.Null.And.Not.Empty);
            Assert.That(capture.keyRects.Select(rect => rect.name), Is.Unique);
            foreach (var rect in capture.keyRects)
            {
                Assert.That(rect.coordinateOrigin, Is.EqualTo("screen-bottom-left"), rect.name);
                Assert.That(rect.unit, Is.EqualTo("px"), rect.name);
                Assert.That(rect.width, Is.GreaterThan(0f), rect.name);
                Assert.That(rect.height, Is.GreaterThan(0f), rect.name);
            }

            AssertKeyRect(capture, "LanLobbyRoot/Room/LeaveAction");
            AssertKeyRect(capture, "LanLobbyRoot/Room/LocalLatency");
            AssertKeyRect(capture, "LanLobbyRoot/Room/PrimaryAction");
            for (var index = 0; index < LobbyRoomSnapshot.MaximumMembers; index++)
            {
                var slot = "LanLobbyRoot/Room/RoomCard_" + index;
                AssertKeyRect(capture, slot);
                AssertKeyRect(capture, slot + "/CardBody");
                AssertKeyRect(capture, slot + "/TopBar");
                AssertKeyRect(capture, slot + "/ReadyOverlay");
                AssertKeyRect(capture, slot + "/LowerDecoration");
                if (index == 0) AssertKeyRect(capture, slot + "/CreatorTag");

                if (index >= expectedMembers.Count)
                {
                    AssertKeyRect(capture, slot + "/EmptyContent");
                    AssertKeyRect(capture, slot + "/EmptyContent/EmptyInviteIcon");
                    AssertKeyRect(capture, slot + "/EmptyContent/EmptyInviteLabel");
                    AssertKeyRect(capture, slot + "/EmptyContent/EmptyInviteHint");
                }
                else if (expectedMembers[index].IsReady)
                {
                    AssertKeyRect(capture, slot + "/OccupiedContent/ReadyIcon");
                    AssertKeyRect(capture, slot + "/OccupiedContent/ReadyLabel");
                }
            }
        }

        private static void AssertRoomSourceAudit(CaptureRecordProbe capture)
        {
            var expectedOccurrences = ExpectedRoomBitmapOccurrences(capture.name);
            Assert.That(capture.sourceAudit, Is.Not.Null.And.Not.Empty);
            var bitmapRows = capture.sourceAudit.Where(item => item.isBitmap).ToArray();
            var codeNativeRows = capture.sourceAudit.Where(item => !item.isBitmap).ToArray();
            Assert.That(bitmapRows, Has.Length.EqualTo(capture.spriteSources.Length),
                capture.name + " must audit every active rendered bitmap occurrence.");
            Assert.That(codeNativeRows, Has.Length.EqualTo(capture.codeNativeGeometry.Length),
                capture.name + " must audit every active rendered code-native occurrence.");
            Assert.That(bitmapRows.Select(item => item.node), Is.Unique,
                capture.name + " bitmap rows must remain per rendered instance.");

            foreach (var item in bitmapRows)
            {
                Assert.That(item.kind, Is.EqualTo("bitmap-sprite"));
                Assert.That(item.spriteName, Is.Not.Null.And.Not.Empty);
                Assert.That(item.materialName, Is.Empty);
                Assert.That(ExpectedRoomBitmapSources.ContainsKey(item.spriteName), Is.True,
                    capture.name + " rendered an unapproved room bitmap: " + item.spriteName);
                var expectedSource = ExpectedRoomBitmapSources[item.spriteName];
                Assert.That(item.resourcesPath, Is.EqualTo("UI/Lobby/" + item.spriteName));
                Assert.That(item.sourcePath, Is.EqualTo("[uc]autochessouter/" + item.spriteName + ".png"));
                Assert.That(item.sha256, Is.EqualTo(expectedSource.Sha256));
                Assert.That(item.sourcePath.Contains("$0") || item.sourcePath.Contains("#0"), Is.False);
                Assert.That(item.captures, Is.EqualTo(new[] { capture.name }));
                Assert.That(item.occurrenceCount, Is.EqualTo(1));
                var renderedOccurrence = capture.spriteSources.SingleOrDefault(sprite =>
                    sprite.node == item.node && sprite.spriteName == item.spriteName);
                Assert.That(renderedOccurrence, Is.Not.Null,
                    capture.name + " audit row must retain its rendered Image occurrence identity: " + item.node);
                Assert.That(renderedOccurrence.isBitmap, Is.True);
                Assert.That(item.raycastTarget, Is.EqualTo(renderedOccurrence.raycastTarget),
                    capture.name + " audit raycast must come from the same rendered Image occurrence: " + item.node);
            }

            foreach (var item in codeNativeRows)
            {
                Assert.That(item.kind, Is.EqualTo("code-native-geometry"));
                Assert.That(item.spriteName, Is.Empty);
                Assert.That(item.materialName, Is.Empty);
                Assert.That(item.resourcesPath, Is.Empty);
                Assert.That(item.sourcePath, Is.Empty);
                Assert.That(item.sha256, Is.Empty);
                Assert.That(item.raycastTarget, Is.False);
                Assert.That(item.captures, Is.EqualTo(new[] { capture.name }));
                Assert.That(item.occurrenceCount, Is.EqualTo(1));
            }

            CollectionAssert.AreEquivalent(
                expectedOccurrences.Select(pair => pair.Key + "|" + pair.Value),
                bitmapRows.GroupBy(item => item.spriteName)
                    .Select(group => group.Key + "|" + group.Sum(item => item.occurrenceCount)),
                capture.name + " must report the exact rendered bitmap occurrence inventory.");
            Assert.That(expectedOccurrences["card_bg"], Is.EqualTo(4));

            AssertBitmapOccurrenceRaycast(capture, "LanLobbyRoot/Room/LeaveAction", true);
            AssertBitmapOccurrenceRaycast(capture, "LanLobbyRoot/Room/PrimaryAction", true);
            AssertBitmapOccurrenceRaycast(capture, "LanLobbyRoot/Room/RoomCard_0/CardBody", false);
            AssertBitmapOccurrenceRaycast(capture, "LanLobbyRoot/Room/RoomCard_0/TopBar", false);
            var blocker = capture.sourceAudit.Single(item => item.node == "LanLobbyRoot/OpaqueBlocker");
            Assert.That(blocker.isBitmap, Is.False);
            Assert.That(blocker.raycastTarget, Is.False);
        }

        private static void AssertBitmapOccurrenceRaycast(
            CaptureRecordProbe capture,
            string node,
            bool expectedRaycastTarget)
        {
            var rendered = capture.spriteSources.Where(item => item.node == node).ToArray();
            var audited = capture.sourceAudit.Where(item => item.node == node).ToArray();
            Assert.That(rendered, Has.Length.EqualTo(1), capture.name + " rendered occurrence: " + node);
            Assert.That(audited, Has.Length.EqualTo(1), capture.name + " audit occurrence: " + node);
            Assert.That(rendered[0].spriteName, Is.EqualTo(audited[0].spriteName));
            Assert.That(rendered[0].raycastTarget, Is.EqualTo(expectedRaycastTarget), node);
            Assert.That(audited[0].raycastTarget, Is.EqualTo(expectedRaycastTarget), node);
            Assert.That(audited[0].occurrenceCount, Is.EqualTo(1), node);
        }

        private static Dictionary<string, int> ExpectedRoomBitmapOccurrences(string captureName)
        {
            switch (captureName)
            {
                case "room-host":
                    return new Dictionary<string, int>
                    {
                        { "bg_terrain", 1 }, { "card_bg", 4 },
                        { "bg_top_ready", 1 }, { "bg_top_normal", 3 },
                        { "player_card_self_frame", 1 }, { "card_empty", 3 }, { "bg_plus", 3 },
                        { "player_card_ready", 1 }, { "card_deco_bg", 3 }, { "card_deco_self", 1 }, { "host_top_tag", 1 },
                        { "img_return", 1 }, { "btn_match_normal", 1 }, { "btn_match_host_normal", 1 }
                    };
                case "room-full":
                    return new Dictionary<string, int>
                    {
                        { "bg_terrain", 1 }, { "card_bg", 4 },
                        { "bg_top_ready", 1 }, { "bg_top_normal", 3 },
                        { "player_card_self_frame", 1 }, { "player_card_ready", 1 },
                        { "card_deco_bg", 3 }, { "card_deco_self", 1 }, { "host_top_tag", 1 },
                        { "img_return", 1 }, { "btn_match_grey", 1 }, { "btn_match_host_grey", 1 }
                    };
                case "room-ready":
                    return new Dictionary<string, int>
                    {
                        { "bg_terrain", 1 }, { "card_bg", 4 },
                        { "bg_top_ready", 4 }, { "player_card_self_frame", 4 },
                        { "player_card_ready", 4 }, { "card_deco_self", 4 }, { "host_top_tag", 1 },
                        { "img_return", 1 }, { "btn_match_normal", 1 }, { "btn_match_host_normal", 1 }
                    };
                default:
                    Assert.Fail("Unexpected room capture: " + captureName);
                    return null;
            }
        }

        private static void AssertKeyRect(CaptureRecordProbe capture, string name)
        {
            Assert.That(capture.keyRects.Count(rect => rect.name == name), Is.EqualTo(1),
                capture.name + " must export one diagnostic key rect for " + name);
        }

        private static bool IsHostProfileNode(string node)
        {
            if (string.IsNullOrEmpty(node)) return false;
            var forbiddenSegments = new[]
            {
                "/Portrait", "/Avatar", "/Profile", "/PlayerName", "/PlayerId", "/MemberName", "/MemberId"
            };
            return forbiddenSegments.Any(segment =>
                node.IndexOf(segment, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool HasRenderedHostProfile(
            CaptureRecordProbe capture,
            string hostDisplayName,
            string hostPlayerId)
        {
            var forbiddenTexts = new[] { hostDisplayName, hostPlayerId };
            if (capture.unityText.Any(text => forbiddenTexts.Any(forbidden =>
                !string.IsNullOrEmpty(forbidden) &&
                !string.IsNullOrEmpty(text.text) &&
                text.text.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                return true;
            }

            var approvedAvatarSprites = new HashSet<string>(StringComparer.Ordinal)
            {
                "icon_amiy", "icon_clementi", "icon_kirar", "icon_zumam"
            };
            return capture.spriteSources.Any(sprite => approvedAvatarSprites.Contains(sprite.spriteName));
        }

        [Test]
        public void HostProfileAbsenceGuard_RejectsGenericLabelAndIconMutations()
        {
            var probe = new CaptureRecordProbe
            {
                unityText = new[]
                {
                    new UnityTextProbe { node = "LanLobbyRoot/Room/PrimaryAction/Label", text = "协议启动" }
                },
                spriteSources = new[]
                {
                    new SpriteSourceProbe
                    {
                        node = "LanLobbyRoot/Room/RoomCard_0/CardBody",
                        spriteName = "card_bg"
                    }
                }
            };
            Assert.That(HasRenderedHostProfile(probe, "Doctor", "capture-host"), Is.False);

            probe.unityText = new[]
            {
                new UnityTextProbe { node = "LanLobbyRoot/Room/RoomCard_0/Label", text = "Doctor" }
            };
            Assert.That(HasRenderedHostProfile(probe, "Doctor", "capture-host"), Is.True,
                "A generic Label containing the host display name must trip the guard.");

            probe.unityText = Array.Empty<UnityTextProbe>();
            probe.spriteSources = new[]
            {
                new SpriteSourceProbe
                {
                    node = "LanLobbyRoot/Room/RoomCard_0/Icon",
                    spriteName = "icon_amiy"
                }
            };
            Assert.That(HasRenderedHostProfile(probe, "Doctor", "capture-host"), Is.True,
                "A generic Icon rendering the host avatar must trip the guard.");
        }

        private static void AssertJoinBitmapInventory(CaptureRecordProbe capture)
        {
            const string joinRoot = "LanLobbyRoot/Home/RoomSelect/Join/";
            var expected = new Dictionary<string, string>
            {
                { joinRoot + "LeftBlock_0", "room_select_join_left_block" },
                { joinRoot + "LeftBlock_1", "room_select_join_left_block" },
                { joinRoot + "MiddleBlock_0", "room_select_join_middle_block" },
                { joinRoot + "MiddleBlock_1", "room_select_join_middle_block" },
                { joinRoot + "MiddleBlock_2", "room_select_join_middle_block" },
                { joinRoot + "MiddleBlock_3", "room_select_join_middle_block" },
                { joinRoot + "RightBlock_0", "room_select_join_right_block" },
                { joinRoot + "RightBlock_1", "room_select_join_right_block" },
                { joinRoot + "MiddleMask", "room_select_join_middle_block_mask" },
                { joinRoot + "Blank", "room_select_join_blank" },
                { joinRoot + "Ban_0", "room_select_join_ban" },
                { joinRoot + "Ban_1", "room_select_join_ban" },
                { joinRoot + "Ban_2", "room_select_join_ban" },
                { joinRoot + "Ban_3", "room_select_join_ban" },
                { joinRoot + "Triangle", "room_select_join_triangle" },
                { joinRoot + "Logo", "room_select_join_logo" },
                { joinRoot + "Text01", "room_select_join_text_01" },
                { joinRoot + "Text02", "room_select_join_text_02" },
                { joinRoot + "RoomCodeInput", "room_select_join_text_bg" },
                { joinRoot + "JoinAction", "room_select_join_btn_bg_down" },
                { joinRoot + "JoinAction/ActionIcon", "join_icon" }
            };
            var actual = capture.spriteSources.Where(sprite => sprite.node.StartsWith(joinRoot, StringComparison.Ordinal)).ToArray();
            CollectionAssert.AreEquivalent(expected.Select(item => item.Key + "|" + item.Value),
                actual.Select(item => item.node + "|" + item.spriteName),
                capture.name + " must have no unexpected or missing Join bitmap nodes.");
            foreach (var item in expected)
            {
                var occurrence = actual.Single(sprite => sprite.node == item.Key && sprite.spriteName == item.Value);
                Assert.That(occurrence.sourcePath, Is.EqualTo("[uc]autochessouter/" + item.Value + ".png"));
                Assert.That(occurrence.sourcePath.Contains("$0") || occurrence.sourcePath.Contains("#0"), Is.False,
                    capture.name + ": " + item.Key + " must not use a forbidden source variant.");
                Assert.That(occurrence.coordinateOrigin, Is.EqualTo("screen-bottom-left"));
                Assert.That(occurrence.unit, Is.EqualTo("px"));
                Assert.That(occurrence.width, Is.GreaterThan(0f), item.Key + " must expose rendered Sprite width.");
                Assert.That(occurrence.height, Is.GreaterThan(0f), item.Key + " must expose rendered Sprite height.");
            }
        }

        private static void AssertHomeJoinGeometry(CaptureRecordProbe capture)
        {
            var expected = new[]
            {
                new GeometryExpectation("LanLobbyRoot/Home/RoomSelect/Join/InteriorBacking", 1154f, 204f, 717f, 280f),
                new GeometryExpectation("LanLobbyRoot/Home/RoomSelect/Join/OutlineTop", 1154f, 482f, 717f, 2f),
                new GeometryExpectation("LanLobbyRoot/Home/RoomSelect/Join/OutlineLeft", 1154f, 204f, 2f, 280f),
                new GeometryExpectation("LanLobbyRoot/Home/RoomSelect/Join/OutlineRight", 1869f, 204f, 2f, 280f),
                new GeometryExpectation("LanLobbyRoot/Home/RoomSelect/Join/GuideHorizontal", 1154f, 383f, 717f, 2f),
                new GeometryExpectation("LanLobbyRoot/Home/RoomSelect/Join/GuideVertical", 1506f, 288f, 2f, 196f)
            };
            foreach (var item in expected)
            {
                var geometry = capture.codeNativeGeometry.SingleOrDefault(value => value.name == item.Name);
                Assert.That(geometry, Is.Not.Null, capture.name + ": missing " + item.Name);
                Assert.That(geometry.kind, Is.EqualTo("code-native-geometry"));
                Assert.That(geometry.isBitmap, Is.False, item.Name + " must be sprite-null geometry.");
                Assert.That(geometry.coordinateOrigin, Is.EqualTo("screen-bottom-left"));
                Assert.That(geometry.unit, Is.EqualTo("px"));
                Assert.That(geometry.raycastTarget, Is.False, item.Name + " must remain non-interactive.");
                Assert.That(geometry.x / capture.canvasScale, Is.EqualTo(item.X).Within(.05f));
                Assert.That(geometry.y / capture.canvasScale, Is.EqualTo(item.Y).Within(.05f));
                Assert.That(geometry.width / capture.canvasScale, Is.EqualTo(item.Width).Within(.05f));
                Assert.That(geometry.height / capture.canvasScale, Is.EqualTo(item.Height).Within(.05f));
            }
            Assert.That(capture.codeNativeGeometry, Is.Not.Null.And.Length.EqualTo(8),
                capture.name + " must report OpaqueBlocker, Create backing, and exactly six Join geometry rows.");
            foreach (var geometry in capture.codeNativeGeometry)
            {
                Assert.That(geometry.name, Is.Not.Null.And.Not.Empty);
                Assert.That(geometry.kind, Is.EqualTo("code-native-geometry"));
                Assert.That(geometry.isBitmap, Is.False);
                Assert.That(geometry.color, Is.Not.Null.And.Not.Empty);
                Assert.That(geometry.coordinateOrigin, Is.EqualTo("screen-bottom-left"));
                Assert.That(geometry.unit, Is.EqualTo("px"));
                Assert.That(geometry.width, Is.GreaterThan(0f));
                Assert.That(geometry.height, Is.GreaterThan(0f));
            }
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "LanLobbyRoot/OpaqueBlocker",
                    "LanLobbyRoot/Home/RoomSelect/Create/InteriorBacking",
                    "LanLobbyRoot/Home/RoomSelect/Join/InteriorBacking",
                    "LanLobbyRoot/Home/RoomSelect/Join/OutlineTop",
                    "LanLobbyRoot/Home/RoomSelect/Join/OutlineLeft",
                    "LanLobbyRoot/Home/RoomSelect/Join/OutlineRight",
                    "LanLobbyRoot/Home/RoomSelect/Join/GuideHorizontal",
                    "LanLobbyRoot/Home/RoomSelect/Join/GuideVertical"
                },
                capture.codeNativeGeometry.Select(geometry => geometry.name).ToArray(),
                capture.name + " must have exactly the approved Home geometry inventory.");
            Assert.That(capture.codeNativeGeometry.Any(item =>
                item.name.StartsWith("LanLobbyRoot/Home/RoomSelect/PanelFrame", StringComparison.Ordinal)), Is.False);
            Assert.That(capture.codeNativeGeometry.Any(geometry => geometry.name.EndsWith("/OutlineBottom", StringComparison.Ordinal)), Is.False,
                capture.name + " must not report a bottom outline.");
        }

        private static void AssertActionRect(CaptureRecordProbe capture, string name)
        {
            var rect = capture.rects.Single(value => value.name == name);
            Assert.That(rect.coordinateOrigin, Is.EqualTo("screen-bottom-left"));
            Assert.That(rect.unit, Is.EqualTo("px"));
            Assert.That(rect.x, Is.GreaterThanOrEqualTo(0f));
            Assert.That(rect.y, Is.GreaterThanOrEqualTo(0f));
            Assert.That(rect.width, Is.GreaterThan(0f));
            Assert.That(rect.height, Is.GreaterThan(0f));
        }

        [Test]
        public void PlayerOutputValidation_RejectsProjectAssetsBeforeAnyCaptureWrite()
        {
            var suiteType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("LanLobbyCaptureSuite"))
                .FirstOrDefault(type => type != null);
            Assert.That(suiteType, Is.Not.Null);
            var validator = suiteType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .SingleOrDefault(method => method.Name == "TryGetPlayerOutputDirectoryForTests" && method.GetParameters().Length == 3);
            Assert.That(validator, Is.Not.Null, "Player capture output needs an explicit safe-directory validator.");

            var forbidden = Path.Combine(Application.dataPath, "LanLobbyCaptureForbidden");
            var arguments = new object[] { forbidden, null, null };
            Assert.That((bool)validator.Invoke(null, arguments), Is.False);
            Assert.That(arguments[1] as string, Is.Empty);
            StringAssert.Contains("Temp", arguments[2] as string);
            Assert.That(Directory.Exists(forbidden), Is.False);
        }

        [Test]
        public void PlayerOutputValidation_FindsActualProjectRootInsteadOfTreatingPlayerDataParentAsProject()
        {
            var suiteType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("LanLobbyCaptureSuite"))
                .FirstOrDefault(type => type != null);
            Assert.That(suiteType, Is.Not.Null);
            var validator = suiteType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .SingleOrDefault(method => method.Name == "TryGetPlayerOutputDirectoryForTests" && method.GetParameters().Length == 5);
            Assert.That(validator, Is.Not.Null, "The test seam must model Player data and current directories separately.");

            var projectRoot = Directory.GetCurrentDirectory();
            var simulatedPlayerData = Path.Combine(projectRoot, "Temp", "PlayerBuild", "ARKnoNIGHTS_Data");
            var externalDirectory = Path.Combine(Application.temporaryCachePath, "LanLobbyExternalPlayer", "ARKnoNIGHTS_Data");
            var relativeOutput = Path.Combine("Temp", "LAN-LOBBY", "Captures");

            var inProjectArguments = new object[] { relativeOutput, simulatedPlayerData, externalDirectory, null, null };
            Assert.That((bool)validator.Invoke(null, inProjectArguments), Is.True);
            Assert.That(inProjectArguments[3] as string, Is.EqualTo(Path.Combine(projectRoot, relativeOutput)));

            var copiedPlayerArguments = new object[] { relativeOutput, externalDirectory, externalDirectory, null, null };
            Assert.That((bool)validator.Invoke(null, copiedPlayerArguments), Is.False);
            StringAssert.Contains("project root", copiedPlayerArguments[4] as string);
        }

        private sealed class GeometryExpectation
        {
            public GeometryExpectation(string name, float x, float y, float width, float height)
            {
                Name = name;
                X = x;
                Y = y;
                Width = width;
                Height = height;
            }

            public string Name { get; }
            public float X { get; }
            public float Y { get; }
            public float Width { get; }
            public float Height { get; }
        }

        private sealed class MemberExpectation
        {
            public MemberExpectation(string playerId, string displayName, bool isReady)
            {
                PlayerId = playerId;
                DisplayName = displayName;
                IsReady = isReady;
            }

            public string PlayerId { get; }
            public string DisplayName { get; }
            public bool IsReady { get; }
        }

        private static readonly Dictionary<string, BitmapSourceExpectation> ExpectedRoomBitmapSources =
            new Dictionary<string, BitmapSourceExpectation>
            {
                { "bg_terrain", new BitmapSourceExpectation("ECE7B6159268276287C20E3B3A82A5165BCC1D344EDFA6DE3B88EE24A76F988C") },
                { "shallow_main", new BitmapSourceExpectation("054110DDEE56F1D19FAFA846D821E6CBD83D47BEB70D4C399A84DA4035A11945") },
                { "player_card_ready", new BitmapSourceExpectation("F34786A3E832E97121EB03614B6D584C871B78E4C5CDD1FA6C5F9CB7D191A4C0") },
                { "player_card_self_frame", new BitmapSourceExpectation("19F0D43B704F9CB92EE3BE11D9C64879D1BA9B542EDFF90E9DBDF7FCE381C3A3") },
                { "btn_match_grey", new BitmapSourceExpectation("E774CB0533EB67BD2FE45F50339E36D0A6221BAF594E6AEE5E5C256469A4BA78") },
                { "card_bg", new BitmapSourceExpectation("050B347451BBEBC74F5E3B09A2470931D9B2A85DEF707A4AAC42B5CE1B0BCEE2") },
                { "bg_top_normal", new BitmapSourceExpectation("5A9479B9AFDD4FC3F597CCBF4A1A0D92C1BB1B5053D716E8C20267E6B2C77C4F") },
                { "bg_top_ready", new BitmapSourceExpectation("EFAA99906A087AAF5AD631E4DF8CFCD7E90C4F463621779A13447675F221482D") },
                { "card_empty", new BitmapSourceExpectation("4DD34E0B5BE318770082B14F245591D80F6ABFF00451744C4BEF3459798DCE31") },
                { "card_deco_bg", new BitmapSourceExpectation("C907B3527747B947ECCA08757DD6601BCA46B8EC5AF835F61F226BBBF8E1EBF1") },
                { "card_deco_self", new BitmapSourceExpectation("A3217A0EE5C8B1D7325758162C9859BEC765C7B63331881C90CDA4804A93F661") },
                { "bg_plus", new BitmapSourceExpectation("E2CA5554B27862FE172E2D18D50092618B2E895C2AD63CDB57019BE593B7B66D") },
                { "btn_match_normal", new BitmapSourceExpectation("62B586274488AE3A7BF203829DDFE0C80955993AE22334F46EDE076215C3ADCD") },
                { "btn_topmenu_back", new BitmapSourceExpectation("BB78B1FCB84BA5F3A2FF8992809C8B0EFD4CAC5E1E960A8056BAA79A1A6E6303") },
                { "img_return", new BitmapSourceExpectation("3F20542913541EAF1F175225268FD3E1EC0343C45A18D0BFE3F7DFBDDEFBEC09") },
                { "btn_match_host_normal", new BitmapSourceExpectation("9D36CBDA42FC64CEB7590CBEDF5E49176BFA90E63A8B263FD3C87EE08C3BF3DF") },
                { "btn_match_host_grey", new BitmapSourceExpectation("C4CD3326EA4D04777AAA540525405DF8AA217D2E972443FDE95C202333F93614") },
                { "host_top_tag", new BitmapSourceExpectation("861754CAFABFEF6641129CAC439501EE3E3D964E0FA3C72BC32FDAC117131009") }
            };

        private sealed class BitmapSourceExpectation
        {
            public BitmapSourceExpectation(string sha256)
            {
                Sha256 = sha256;
            }

            public string Sha256 { get; }
        }

        [Serializable] private sealed class CaptureManifestProbe { public CaptureRecordProbe[] captures; }
        [Serializable] private sealed class CaptureRecordProbe { public string name; public int width; public int height; public float canvasScale; public string roomCode; public string localPlayerId; public bool primaryActionInteractable; public CaptureMemberProbe[] members; public CaptureRectProbe[] rects; public CaptureRectProbe[] keyRects; public SpriteSourceProbe[] spriteSources; public UnityTextProbe[] unityText; public CodeNativeGeometryProbe[] codeNativeGeometry; public SourceAuditProbe[] sourceAudit; }
        [Serializable] private sealed class CaptureMemberProbe { public string playerId; public string displayName; public bool isReady; }
        [Serializable] private sealed class CaptureRectProbe { public string name; public string coordinateOrigin; public string unit; public float x; public float y; public float width; public float height; }
        [Serializable] private sealed class SpriteSourceProbe { public string node; public bool isBitmap; public string spriteName; public string sourcePath; public bool raycastTarget; public string coordinateOrigin; public string unit; public float x; public float y; public float width; public float height; }
        [Serializable] private sealed class UnityTextProbe { public string node; public string text; public string fontName; public string fontResourcePath; public bool hasBitmapSource; public string bitmapSourcePath; }
        [Serializable] private sealed class CodeNativeGeometryProbe { public string name; public string kind; public bool isBitmap; public string color; public string coordinateOrigin; public string unit; public bool raycastTarget; public float x; public float y; public float width; public float height; }
        [Serializable] private sealed class SourceAuditProbe { public string node; public string kind; public bool isBitmap; public string spriteName; public string materialName; public string resourcesPath; public string sourcePath; public string sha256; public string[] captures; public int occurrenceCount; public bool raycastTarget; }
    }
}
