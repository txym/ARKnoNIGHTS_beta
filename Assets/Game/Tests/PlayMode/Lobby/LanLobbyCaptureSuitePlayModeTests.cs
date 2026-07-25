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
            StringAssert.Contains("[uc]autochessouter/", manifest);

            var parsed = JsonUtility.FromJson<CaptureManifestProbe>(manifest);
            Assert.That(parsed.captures, Has.Length.EqualTo(expectedNames.Length));
            foreach (var captureRecord in parsed.captures)
            {
                Assert.That(captureRecord.spriteSources, Is.Not.Null.And.Not.Empty, captureRecord.name + " must include sprite provenance.");
                foreach (var spriteSource in captureRecord.spriteSources)
                {
                    Assert.That(spriteSource.spriteName, Is.Not.Null.And.Not.Empty);
                    Assert.That(spriteSource.sourcePath, Is.Not.Null.And.Not.Empty);
                }
            }

            var discovered = parsed.captures.Single(record => record.name == "discovered-prefill");
            Assert.That(discovered.roomCode, Is.EqualTo("654321"));
            Assert.That(discovered.members, Is.Empty);
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
        [Serializable] private sealed class CaptureRecordProbe { public string name; public string roomCode; public CaptureMemberProbe[] members; public SpriteSourceProbe[] spriteSources; }
        [Serializable] private sealed class CaptureMemberProbe { public string playerId; }
        [Serializable] private sealed class SpriteSourceProbe { public string spriteName; public string sourcePath; }
    }
}
