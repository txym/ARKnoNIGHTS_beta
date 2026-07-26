using System;
using System.Collections;
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
                Assert.That(captureRecord.spriteSources, Is.Not.Null.And.Not.Empty, captureRecord.name + " must include sprite provenance.");
                foreach (var spriteSource in captureRecord.spriteSources)
                {
                    Assert.That(spriteSource.node, Is.Not.Null.And.Not.Empty);
                    Assert.That(spriteSource.spriteName, Is.Not.Null.And.Not.Empty);
                    Assert.That(spriteSource.sourcePath, Is.Not.Null.And.Not.Empty);
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
            }

            var discovered = parsed.captures.Single(record => record.name == "discovered-prefill");
            Assert.That(discovered.roomCode, Is.EqualTo("654321"));
            Assert.That(discovered.members, Is.Empty);

            var home = parsed.captures.Single(record => record.name == "home");
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
            Assert.That(home.spriteSources.Count(sprite => sprite.spriteName == "join_icon"), Is.EqualTo(2),
                "Home must count both rendered join_icon instances.");
            Assert.That(discovered.spriteSources.Count(sprite => sprite.spriteName == "join_icon"), Is.EqualTo(2),
                "Discovered Home must count both rendered join_icon instances.");
            Assert.That(parsed.captures.Sum(record => record.spriteSources.Count(sprite => sprite.spriteName == "join_icon")), Is.EqualTo(4),
                "Rendered join_icon occurrences must sum to four across the two Home states.");
            Assert.That(home.spriteSources.Select(sprite => sprite.node), Is.Unique,
                "Each Sprite usage row must identify one stable rendered node.");
            Assert.That(home.codeNativeGeometry, Is.Not.Null.And.Length.EqualTo(6),
                "The Home manifest must report every active non-bitmap Image: OpaqueBlocker plus five frame lines.");
            foreach (var geometry in home.codeNativeGeometry)
            {
                Assert.That(geometry.name, Is.Not.Null.And.Not.Empty);
                Assert.That(geometry.kind, Is.EqualTo("code-native-geometry"));
                Assert.That(geometry.isBitmap, Is.False);
                Assert.That(geometry.color, Is.Not.Null.And.Not.Empty);
                Assert.That(geometry.width, Is.GreaterThan(0f));
                Assert.That(geometry.height, Is.GreaterThan(0f));
            }
            CollectionAssert.AreEquivalent(new[]
                {
                    "LanLobbyRoot/OpaqueBlocker",
                    "LanLobbyRoot/Home/RoomSelect/PanelFrame/Top",
                    "LanLobbyRoot/Home/RoomSelect/PanelFrame/Bottom",
                    "LanLobbyRoot/Home/RoomSelect/PanelFrame/Left",
                    "LanLobbyRoot/Home/RoomSelect/PanelFrame/Right",
                    "LanLobbyRoot/Home/RoomSelect/PanelFrame/Divider"
                },
                home.codeNativeGeometry.Select(geometry => geometry.name).ToArray());
            Assert.That(roomHost.spriteSources.Any(sprite => sprite.spriteName == "shallow_main"), Is.True,
                "The Room provenance table must include the foreground once that page restores it.");
            Assert.That(roomHost.codeNativeGeometry.Select(geometry => geometry.name), Is.EquivalentTo(new[] { "LanLobbyRoot/OpaqueBlocker" }),
                "Room must report the visible blocker but no Home-only frame geometry.");
            foreach (var room in parsed.captures.Where(record => record.name.StartsWith("room-", StringComparison.Ordinal)))
                Assert.That(room.codeNativeGeometry.Any(geometry => geometry.name.Contains("PanelFrame")), Is.False,
                    room.name + " must not report inactive Home-only frame geometry.");
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

        [Serializable] private sealed class CaptureManifestProbe { public CaptureRecordProbe[] captures; }
        [Serializable] private sealed class CaptureRecordProbe { public string name; public string roomCode; public CaptureMemberProbe[] members; public CaptureRectProbe[] rects; public SpriteSourceProbe[] spriteSources; public UnityTextProbe[] unityText; public CodeNativeGeometryProbe[] codeNativeGeometry; }
        [Serializable] private sealed class CaptureMemberProbe { public string playerId; }
        [Serializable] private sealed class CaptureRectProbe { public string name; public string coordinateOrigin; public string unit; public float x; public float y; public float width; public float height; }
        [Serializable] private sealed class SpriteSourceProbe { public string node; public string spriteName; public string sourcePath; }
        [Serializable] private sealed class UnityTextProbe { public string node; public string text; public string fontName; public string fontResourcePath; public bool hasBitmapSource; public string bitmapSourcePath; }
        [Serializable] private sealed class CodeNativeGeometryProbe { public string name; public string kind; public bool isBitmap; public string color; public float x; public float y; public float width; public float height; }
    }
}
