using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ArknoNights.Lobby;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Opt-in Player visual evidence for the LAN lobby. It drives the production view through stable
/// fixture states and never writes under Assets.
/// </summary>
public sealed class LanLobbyCaptureSuite : MonoBehaviour
{
    private const string SuiteFlag = "-lanLobbyCaptureSuite";
    private const string OutputFlag = "-lanLobbyCaptureOutput";
    private const string AssetSourcePrefix = "[uc]autochessouter/";
    private const string CombinedAvatarSourcePrefix = "Combined/[uc]autochesscommon/";
    private readonly List<CaptureRecord> captures = new List<CaptureRecord>();
    private string outputDirectory;
    private global::LanLobbyView view;

    private void Awake()
    {
        if (!Environment.GetCommandLineArgs().Any(arg => string.Equals(arg, SuiteFlag, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (!TryGetPlayerOutputDirectory(CommandLineValue(OutputFlag), out outputDirectory, out var error))
        {
            Debug.LogError("[LanLobby][capture.output.rejected] " + error, this);
            Application.Quit(1);
            return;
        }
        StartCoroutine(Capture(outputDirectory, true, true));
    }

    /// <summary>PlayMode seam that produces the exact Player manifest without quitting the test runner.</summary>
    public static IEnumerator CaptureForTests(string directory)
    {
        var root = new GameObject("LanLobbyCaptureSuiteTests");
        var suite = root.AddComponent<LanLobbyCaptureSuite>();
        yield return suite.Capture(Path.GetFullPath(directory), false, false);
        Destroy(root);
    }

    /// <summary>Test seam for the Player-only ignored-output restriction.</summary>
    public static bool TryGetPlayerOutputDirectoryForTests(string requestedDirectory, out string normalizedDirectory, out string error)
    {
        return TryGetPlayerOutputDirectory(requestedDirectory, out normalizedDirectory, out error);
    }

    /// <summary>Test seam for Player data-directory and current-directory root discovery.</summary>
    public static bool TryGetPlayerOutputDirectoryForTests(string requestedDirectory, string simulatedDataPath, string simulatedCurrentDirectory, out string normalizedDirectory, out string error)
    {
        return TryGetPlayerOutputDirectory(requestedDirectory, simulatedDataPath, simulatedCurrentDirectory, out normalizedDirectory, out error);
    }

    private IEnumerator Capture(string directory, bool quitWhenComplete, bool captureScreen)
    {
        if (captureScreen && !TryGetPlayerOutputDirectory(directory, out outputDirectory, out var error))
        {
            FailCapture(error, true);
            yield break;
        }
        if (!captureScreen) outputDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(outputDirectory);
        var disabledViews = DisableExistingLobbyViews();
        var viewRoot = new GameObject("LanLobbyCaptureFixture");
        view = viewRoot.AddComponent<global::LanLobbyView>();
        yield return null;

        yield return CaptureHome(captureScreen);
        yield return CaptureDiscoveredPrefill(captureScreen);
        yield return CaptureRoom("room-host", HostRoom(), 18, captureScreen);
        yield return CaptureRoom("room-ready", ReadyRoom(), 42, captureScreen);
        yield return CaptureRoom("room-full", FullRoom(), 87, captureScreen);

        File.WriteAllText(Path.Combine(outputDirectory, "manifest.json"), JsonUtility.ToJson(new CaptureManifest { captures = captures.ToArray() }, true));
        Debug.Log("[LanLobby][capture.completed] count=" + captures.Count + "; output=" + outputDirectory, this);
        Destroy(viewRoot);
        if (!quitWhenComplete) RestoreLobbyViews(disabledViews);
        if (quitWhenComplete) Application.Quit(0);
    }

    private IEnumerator CaptureHome(bool captureScreen)
    {
        view.ShowHome(new LobbyProfile("capture-host", "Doctor", 0));
        view.SetStatus("DISCOVERING LOCAL ROOMS");
        yield return CaptureOne("home", null, 0, captureScreen);
    }

    private IEnumerator CaptureDiscoveredPrefill(bool captureScreen)
    {
        view.ShowHome(new LobbyProfile("capture-host", "Doctor", 0));
        view.BindDiscoveredRooms(new[] { new LobbyDiscoveryEntry("654321", "Doctor", 1, 4, true, 48765, 1) });
        view.ClickDiscoveredRoomForTests("654321");
        yield return CaptureOne("discovered-prefill", null, 0, captureScreen);
    }

    private IEnumerator CaptureRoom(string name, LobbyRoomSnapshot room, long latency, bool captureScreen)
    {
        view.ShowRoom(room, "capture-host");
        view.SetLocalLatency(latency);
        yield return CaptureOne(name, room, latency, captureScreen);
    }

    private IEnumerator CaptureOne(string name, LobbyRoomSnapshot room, long latency, bool captureScreen)
    {
        Canvas.ForceUpdateCanvases();
        if (captureScreen) yield return new WaitForEndOfFrame();
        else yield return null;
        var path = Path.Combine(outputDirectory, name + ".png");
        if (File.Exists(path)) File.Delete(path);
        if (captureScreen) ScreenCapture.CaptureScreenshot(path, 1);
        else WriteTestProbe(path);

        byte[] bytes = null;
        for (var frame = 0; frame < 600 && bytes == null; frame++)
        {
            if (File.Exists(path))
            {
                try { bytes = File.ReadAllBytes(path); }
                catch (IOException) { }
            }
            if (bytes == null) yield return null;
        }

        if (bytes == null) FailCapture("Timed out waiting for screenshot: " + name, captureScreen);
        var probe = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        var valid = ImageConversion.LoadImage(probe, bytes, false);
        if (valid && captureScreen)
        {
            var pixels = probe.GetPixels32();
            valid = pixels.Count(pixel => pixel.r > 4 || pixel.g > 4 || pixel.b > 4) > pixels.Length / 200;
        }
        if (!valid || probe.width <= 1 || probe.height <= 1)
        {
            Destroy(probe);
            FailCapture("Invalid screenshot: " + name, captureScreen);
        }

        var width = probe.width;
        var height = probe.height;
        Destroy(probe);
        captures.Add(new CaptureRecord
        {
            name = name,
            path = path,
            width = width,
            height = height,
            canvasScale = view.GetComponent<Canvas>().scaleFactor,
            roomCode = room == null ? view.RoomCodeTextForTests : room.RoomCode,
            members = ToMembers(room),
            localLatencyMilliseconds = latency,
            rects = KeyRects(),
            spriteSources = SpriteSources(),
            codeNativeGeometry = CodeNativeGeometries()
        });
    }

    private CaptureRect[] KeyRects()
    {
        var names = new[] { "LanLobbyRoot", "LanLobbyRoot/Home", "LanLobbyRoot/Room", "LanLobbyRoot/Room/RoomCard_0", "LanLobbyRoot/Home/CreateRoomCard", "LanLobbyRoot/Home/JoinRoomCard" };
        var values = new List<CaptureRect>();
        foreach (var name in names)
        {
            var rect = view.transform.Find(name) as RectTransform;
            if (rect == null) continue;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            values.Add(new CaptureRect { name = name, x = corners[0].x, y = corners[0].y, width = corners[2].x - corners[0].x, height = corners[2].y - corners[0].y });
        }
        return values.ToArray();
    }

    private SpriteSource[] SpriteSources()
    {
        var result = new Dictionary<string, SpriteSource>(StringComparer.Ordinal);
        // Provenance is evidence of actual rendering in this capture state, not of dormant page objects.
        foreach (var image in view.GetComponentsInChildren<Image>(false))
        {
            if (image.sprite == null) continue;
            var spriteName = image.sprite.name;
            if (!TryGetApprovedSource(spriteName, out var source))
                throw new InvalidOperationException("Lobby capture uses an unmapped sprite: " + spriteName);
            result[spriteName] = new SpriteSource { spriteName = spriteName, sourcePath = source };
        }
        return result.Values.OrderBy(value => value.spriteName, StringComparer.Ordinal).ToArray();
    }

    private CodeNativeGeometry[] CodeNativeGeometries()
    {
        return view.GetComponentsInChildren<Image>(false)
            // Sprite-backed images are audited in spriteSources. Fully transparent Images are hit targets,
            // not rendered geometry, so they must not inflate this actual-visual-provenance table.
            .Where(image => image.isActiveAndEnabled && image.sprite == null && image.color.a > 0f)
            .Select(image =>
            {
                var corners = new Vector3[4];
                image.rectTransform.GetWorldCorners(corners);
                return new CodeNativeGeometry
                {
                    name = HierarchyPath(image.transform, view.transform),
                    kind = "code-native-geometry",
                    isBitmap = false,
                    color = "#" + ColorUtility.ToHtmlStringRGBA(image.color),
                    x = corners[0].x,
                    y = corners[0].y,
                    width = corners[2].x - corners[0].x,
                    height = corners[2].y - corners[0].y
                };
            })
            .OrderBy(value => value.name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string HierarchyPath(Transform value, Transform root)
    {
        var names = new List<string>();
        for (var current = value; current != null && current != root; current = current.parent)
            names.Add(current.name);
        names.Reverse();
        return string.Join("/", names);
    }

    private static bool TryGetApprovedSource(string spriteName, out string source)
    {
        switch (spriteName)
        {
            case "bg_terrain": case "shallow_main": case "room_create_btn_bg": case "room_join_btn_bg":
            case "create_icon": case "join_icon": case "img_player_bkg": case "img_player_confirmed":
            case "player_card_waiting": case "player_card_ready": case "player_card_self_frame": case "team_icon_frame":
            case "team_hp_back": case "btn_match_host_normal": case "btn_match_host_grey": case "btn_match_grey": case "btn_match_cancel":
            case "room_select_right_bg": case "room_select_title_icon": case "room_select_dot": case "room_select_img_startroom":
            case "room_select_create_btn_bg_down": case "room_select_create_left_line": case "room_select_create_logo":
            case "room_select_create_middleicon": case "room_select_create_text_01": case "room_select_create_text_02":
            case "room_select_join_ban": case "room_select_join_blank": case "room_select_join_btn_bg_down":
            case "room_select_join_left_block": case "room_select_join_logo": case "room_select_join_middle_block":
            case "room_select_join_middle_block_mask": case "room_select_join_right_block": case "room_select_join_text_01":
            case "room_select_join_text_02": case "room_select_join_text_bg": case "room_select_join_triangle":
                source = AssetSourcePrefix + spriteName + ".png";
                return true;
            case "icon_amiy": case "icon_clementi": case "icon_kirar": case "icon_zumam":
                source = CombinedAvatarSourcePrefix + spriteName + ".png";
                return true;
            default:
                source = string.Empty;
                return false;
        }
    }

    // Batchmode PlayMode has no graphics backbuffer, so its test seam writes a decodeable probe.
    // The Player-only command path above remains the only path that captures rendered pixels.
    private static void WriteTestProbe(string path)
    {
        var probe = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        probe.SetPixels(new[] { Color.cyan, Color.cyan, Color.cyan, Color.cyan });
        File.WriteAllBytes(path, probe.EncodeToPNG());
        Destroy(probe);
    }

    private static void FailCapture(string message, bool captureScreen)
    {
        Debug.LogError("[LanLobby][capture.failed] " + message);
        if (captureScreen) Application.Quit(1);
        throw new InvalidOperationException(message);
    }

    private static CaptureMember[] ToMembers(LobbyRoomSnapshot room)
    {
        if (room == null) return Array.Empty<CaptureMember>();
        return room.Members.Select(member => new CaptureMember
        {
            playerId = member.PlayerId,
            displayName = member.Profile.DisplayName,
            avatarIndex = member.Profile.AvatarIndex,
            isReady = member.IsReady,
            latencyMilliseconds = member.LatencyMilliseconds
        }).ToArray();
    }

    private static LobbyRoomSnapshot HostRoom()
    {
        return new LobbyRoomSnapshot("654321", "capture-host", new[]
        {
            new LobbyMemberSnapshot(new LobbyProfile("capture-host", "Doctor", 0), false, 18)
        }, false, 1);
    }

    private static LobbyRoomSnapshot ReadyRoom()
    {
        return new LobbyRoomSnapshot("654321", "capture-host", new[]
        {
            new LobbyMemberSnapshot(new LobbyProfile("capture-host", "Doctor", 0), true, 42),
            new LobbyMemberSnapshot(new LobbyProfile("capture-guest", "Amiya", 1), true, 56)
        }, false, 2);
    }

    private static LobbyRoomSnapshot FullRoom()
    {
        return new LobbyRoomSnapshot("654321", "capture-host", new[]
        {
            new LobbyMemberSnapshot(new LobbyProfile("capture-host", "Doctor", 0), true, 87),
            new LobbyMemberSnapshot(new LobbyProfile("capture-guest-1", "Amiya", 1), true, 64),
            new LobbyMemberSnapshot(new LobbyProfile("capture-guest-2", "Chen", 2), false, 71),
            new LobbyMemberSnapshot(new LobbyProfile("capture-guest-3", "Kal'tsit", 3), false, 95)
        }, false, 3);
    }

    private static List<GameObject> DisableExistingLobbyViews()
    {
        var disabled = new List<GameObject>();
        foreach (var existing in Resources.FindObjectsOfTypeAll<global::LanLobbyView>())
        {
            if (existing == null || !existing.gameObject.activeSelf) continue;
            disabled.Add(existing.gameObject);
            existing.gameObject.SetActive(false);
        }
        return disabled;
    }

    private static void RestoreLobbyViews(IEnumerable<GameObject> disabledViews)
    {
        foreach (var disabled in disabledViews)
            if (disabled != null) disabled.SetActive(true);
    }

    private static string CommandLineValue(string flag)
    {
        var args = Environment.GetCommandLineArgs();
        for (var index = 0; index + 1 < args.Length; index++)
            if (string.Equals(args[index], flag, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
        return null;
    }

    private static bool TryGetPlayerOutputDirectory(string requestedDirectory, out string normalizedDirectory, out string error)
    {
        return TryGetPlayerOutputDirectory(requestedDirectory, Application.dataPath, Directory.GetCurrentDirectory(), out normalizedDirectory, out error);
    }

    private static bool TryGetPlayerOutputDirectory(string requestedDirectory, string applicationDataPath, string currentDirectory, out string normalizedDirectory, out string error)
    {
        normalizedDirectory = string.Empty;
        error = string.Empty;
        try
        {
            if (!TryFindUnityProjectRoot(applicationDataPath, currentDirectory, out var projectRoot))
            {
                error = "Unity project root with Assets/, Packages/, and ProjectSettings/ was not found; capture output is rejected.";
                return false;
            }

            var candidate = Path.GetFullPath(Path.IsPathRooted(requestedDirectory ?? string.Empty)
                ? requestedDirectory
                : Path.Combine(projectRoot, requestedDirectory ?? Path.Combine("Artifacts", "LAN-LOBBY", "Captures")));
            var temporaryRoot = Path.Combine(projectRoot, "Temp");
            var artifactsRoot = Path.Combine(projectRoot, "Artifacts");
            if (!IsWithinDirectory(candidate, temporaryRoot) && !IsWithinDirectory(candidate, artifactsRoot))
            {
                error = "Capture output must be inside the ignored project Temp/ or Artifacts/ directory; rejected: " + candidate;
                return false;
            }

            normalizedDirectory = candidate;
            return true;
        }
        catch (Exception exception)
        {
            error = "Capture output path is invalid: " + exception.Message;
            return false;
        }
    }

    private static bool TryFindUnityProjectRoot(string applicationDataPath, string currentDirectory, out string projectRoot)
    {
        foreach (var startPath in new[] { currentDirectory, applicationDataPath })
        {
            if (string.IsNullOrWhiteSpace(startPath)) continue;
            var directory = new DirectoryInfo(Path.GetFullPath(startPath));
            for (; directory != null; directory = directory.Parent)
            {
                if (!Directory.Exists(Path.Combine(directory.FullName, "Assets"))) continue;
                if (!Directory.Exists(Path.Combine(directory.FullName, "Packages"))) continue;
                if (!Directory.Exists(Path.Combine(directory.FullName, "ProjectSettings"))) continue;
                projectRoot = directory.FullName;
                return true;
            }
        }

        projectRoot = string.Empty;
        return false;
    }

    private static bool IsWithinDirectory(string candidate, string root)
    {
        var normalizedCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(normalizedCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase)) return true;
        return normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    [Serializable] private sealed class CaptureManifest { public CaptureRecord[] captures; }
    [Serializable] private sealed class CaptureRecord { public string name; public string path; public int width; public int height; public float canvasScale; public string roomCode; public CaptureMember[] members; public long localLatencyMilliseconds; public CaptureRect[] rects; public SpriteSource[] spriteSources; public CodeNativeGeometry[] codeNativeGeometry; }
    [Serializable] private sealed class CaptureMember { public string playerId; public string displayName; public int avatarIndex; public bool isReady; public long latencyMilliseconds; }
    [Serializable] private sealed class CaptureRect { public string name; public float x; public float y; public float width; public float height; }
    [Serializable] private sealed class SpriteSource { public string spriteName; public string sourcePath; }
    [Serializable] private sealed class CodeNativeGeometry { public string name; public string kind; public bool isBitmap; public string color; public float x; public float y; public float width; public float height; }
}

internal static class LanLobbyCaptureSuiteBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Attach()
    {
        if (!Environment.GetCommandLineArgs().Any(arg => string.Equals(arg, "-lanLobbyCaptureSuite", StringComparison.OrdinalIgnoreCase))) return;
        if (UnityEngine.Object.FindObjectOfType<LanLobbyCaptureSuite>() != null) return;
        var holder = new GameObject("LanLobbyCaptureSuite");
        UnityEngine.Object.DontDestroyOnLoad(holder);
        holder.AddComponent<LanLobbyCaptureSuite>();
    }
}
