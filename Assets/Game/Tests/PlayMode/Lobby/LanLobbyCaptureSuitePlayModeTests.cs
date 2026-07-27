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
            Assert.That(roomHost.spriteSources.Any(sprite => sprite.spriteName == "shallow_main"), Is.True,
                "The Room provenance table must include the foreground once that page restores it.");
            Assert.That(roomHost.codeNativeGeometry.Select(geometry => geometry.name), Is.EquivalentTo(new[] { "LanLobbyRoot/OpaqueBlocker" }),
                "Room must report the visible blocker but no Home-only frame geometry.");
            foreach (var room in parsed.captures.Where(record => record.name.StartsWith("room-", StringComparison.Ordinal)))
                Assert.That(room.codeNativeGeometry.Any(geometry => geometry.name.Contains("PanelFrame")), Is.False,
                    room.name + " must not report inactive Home-only frame geometry.");
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

        [Serializable] private sealed class CaptureManifestProbe { public CaptureRecordProbe[] captures; }
        [Serializable] private sealed class CaptureRecordProbe { public string name; public float canvasScale; public string roomCode; public CaptureMemberProbe[] members; public CaptureRectProbe[] rects; public SpriteSourceProbe[] spriteSources; public UnityTextProbe[] unityText; public CodeNativeGeometryProbe[] codeNativeGeometry; }
        [Serializable] private sealed class CaptureMemberProbe { public string playerId; }
        [Serializable] private sealed class CaptureRectProbe { public string name; public string coordinateOrigin; public string unit; public float x; public float y; public float width; public float height; }
        [Serializable] private sealed class SpriteSourceProbe { public string node; public string spriteName; public string sourcePath; public string coordinateOrigin; public string unit; public float x; public float y; public float width; public float height; }
        [Serializable] private sealed class UnityTextProbe { public string node; public string text; public string fontName; public string fontResourcePath; public bool hasBitmapSource; public string bitmapSourcePath; }
        [Serializable] private sealed class CodeNativeGeometryProbe { public string name; public string kind; public bool isBitmap; public string color; public string coordinateOrigin; public string unit; public bool raycastTarget; public float x; public float y; public float width; public float height; }
    }
}
