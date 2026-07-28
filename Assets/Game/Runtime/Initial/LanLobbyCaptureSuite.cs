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
    private const int CaptureWidth = 1920;
    private const int CaptureHeight = 1080;
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

    /// <summary>Test seam for the fixed-resolution geometry export contract.</summary>
    public static bool IsSupportedCaptureSizeForTests(int width, int height)
    {
        return width == CaptureWidth && height == CaptureHeight;
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
        if (!IsSupportedCaptureSizeForTests(width, height))
        {
            FailCapture(
                "Capture dimensions must be exactly " + CaptureWidth + "x" + CaptureHeight +
                " before exporting screen-bottom-left geometry: " + width + "x" + height,
                captureScreen);
        }
        var rects = KeyRects(width, height);
        var spriteSources = SpriteSources(name, width, height);
        var codeNativeGeometry = CodeNativeGeometries(width, height);
        captures.Add(new CaptureRecord
        {
            name = name,
            path = path,
            width = width,
            height = height,
            canvasScale = view.GetComponent<Canvas>().scaleFactor,
            roomCode = room == null ? view.RoomCodeTextForTests : room.RoomCode,
            localPlayerId = room == null ? string.Empty : "capture-host",
            primaryActionInteractable = room != null && view.RoomPrimaryActionInteractableForTests,
            members = ToMembers(room),
            localLatencyMilliseconds = latency,
            rects = rects,
            keyRects = rects,
            spriteSources = spriteSources,
            unityText = UnityTexts(),
            codeNativeGeometry = codeNativeGeometry,
            sourceAudit = room == null
                ? Array.Empty<SourceAudit>()
                : SourceAuditFor(name, spriteSources, codeNativeGeometry)
        });
    }

    private CaptureRect[] KeyRects(int captureWidth, int captureHeight)
    {
        var names = new List<string>
        {
            "LanLobbyRoot",
            "LanLobbyRoot/Home",
            "LanLobbyRoot/Room",
            "LanLobbyRoot/Home/CreateRoomCard",
            "LanLobbyRoot/Home/JoinRoomCard",
            "LanLobbyRoot/Home/RoomSelect/Create/CreateAction",
            "LanLobbyRoot/Home/RoomSelect/Join/JoinAction",
            "LanLobbyRoot/Room/LeaveAction",
            "LanLobbyRoot/Room/LocalLatency",
            "LanLobbyRoot/Room/PrimaryAction"
        };
        for (var index = 0; index < LobbyRoomSnapshot.MaximumMembers; index++)
        {
            var slot = "LanLobbyRoot/Room/RoomCard_" + index;
            names.Add(slot);
            names.Add(slot + "/CardBody");
            names.Add(slot + "/TopBar");
            names.Add(slot + "/ReadyOverlay");
            names.Add(slot + "/LowerDecoration");
            AddIfActive(names, slot + "/EmptyContent");
            AddIfActive(names, slot + "/EmptyContent/EmptyInviteIcon");
            AddIfActive(names, slot + "/EmptyContent/EmptyInviteLabel");
            AddIfActive(names, slot + "/EmptyContent/EmptyInviteHint");
            AddIfActive(names, slot + "/OccupiedContent/ReadyIcon");
            AddIfActive(names, slot + "/OccupiedContent/ReadyLabel");
            AddIfActive(names, slot + "/CreatorTag");
        }

        var values = new List<CaptureRect>();
        foreach (var name in names)
        {
            var rect = view.transform.Find(name) as RectTransform;
            if (rect == null) continue;
            CaptureBounds(rect, captureWidth, captureHeight, out var minX, out var minY, out var maxX, out var maxY);
            values.Add(new CaptureRect
            {
                name = name,
                coordinateOrigin = "screen-bottom-left",
                unit = "px",
                x = minX,
                y = minY,
                width = maxX - minX,
                height = maxY - minY
            });
        }
        return values.ToArray();
    }

    private void AddIfActive(ICollection<string> names, string path)
    {
        var value = view.transform.Find(path);
        if (value != null && value.gameObject.activeInHierarchy) names.Add(path);
    }

    private SpriteSource[] SpriteSources(string captureName, int captureWidth, int captureHeight)
    {
        // Provenance is evidence of actual rendering in this capture state, not of dormant page objects.
        return view.GetComponentsInChildren<Image>(false)
            .Where(image => image.isActiveAndEnabled && image.sprite != null && image.color.a > 0f && image.canvasRenderer.GetAlpha() > 0f)
            .Select(image =>
            {
                var spriteName = image.sprite.name;
                if (!TryGetApprovedSource(spriteName, out var source))
                    throw new InvalidOperationException("Lobby capture uses an unmapped sprite: " + spriteName);
                CaptureBounds(image.rectTransform, captureWidth, captureHeight, out var minX, out var minY, out var maxX, out var maxY);
                return new SpriteSource
                {
                    node = HierarchyPath(image.transform, view.transform),
                    kind = "bitmap-sprite",
                    isBitmap = true,
                    spriteName = spriteName,
                    materialName = string.Empty,
                    resourcesPath = source.ResourcesPath,
                    sourcePath = source.SourcePath,
                    sha256 = source.Sha256,
                    captures = new[] { captureName },
                    occurrenceCount = 1,
                    raycastTarget = image.raycastTarget,
                    coordinateOrigin = "screen-bottom-left",
                    unit = "px",
                    x = minX,
                    y = minY,
                    width = maxX - minX,
                    height = maxY - minY
                };
            })
            .OrderBy(value => value.node, StringComparer.Ordinal)
            .ToArray();
    }

    private UnityText[] UnityTexts()
    {
        return view.GetComponentsInChildren<Text>(false)
            .Where(text => text.isActiveAndEnabled &&
                           text.font != null &&
                           !string.IsNullOrEmpty(text.text) &&
                           text.color.a > 0f &&
                           text.canvasRenderer.GetAlpha() > 0f &&
                           text.rectTransform.rect.width > 0f &&
                           text.rectTransform.rect.height > 0f)
            .Select(text => new UnityText
            {
                node = HierarchyPath(text.transform, view.transform),
                text = text.text,
                fontName = text.font.name,
                // A runtime Font object does not expose its original Resources path. Record the
                // observable name and explicitly avoid inventing a bitmap/material-library path.
                fontResourcePath = string.Empty,
                hasBitmapSource = false,
                bitmapSourcePath = string.Empty
            })
            .OrderBy(value => value.node, StringComparer.Ordinal)
            .ToArray();
    }

    private CodeNativeGeometry[] CodeNativeGeometries(int captureWidth, int captureHeight)
    {
        return view.GetComponentsInChildren<Image>(false)
            // Sprite-backed images are audited in spriteSources. Fully transparent Images are hit targets,
            // not rendered geometry, so they must not inflate this actual-visual-provenance table.
            .Where(image => image.isActiveAndEnabled && image.sprite == null && image.color.a > 0f)
            .Select(image =>
            {
                CaptureBounds(image.rectTransform, captureWidth, captureHeight, out var minX, out var minY, out var maxX, out var maxY);
                return new CodeNativeGeometry
                {
                    name = HierarchyPath(image.transform, view.transform),
                    kind = "code-native-geometry",
                    isBitmap = false,
                    spriteName = string.Empty,
                    materialName = string.Empty,
                    resourcesPath = string.Empty,
                    sourcePath = string.Empty,
                    sha256 = string.Empty,
                    color = "#" + ColorUtility.ToHtmlStringRGBA(image.color),
                    coordinateOrigin = "screen-bottom-left",
                    unit = "px",
                    raycastTarget = image.raycastTarget,
                    x = minX,
                    y = minY,
                    width = maxX - minX,
                    height = maxY - minY
                };
            })
            .OrderBy(value => value.name, StringComparer.Ordinal)
            .ToArray();
    }

    private void CaptureBounds(
        RectTransform rect,
        int captureWidth,
        int captureHeight,
        out float minX,
        out float minY,
        out float maxX,
        out float maxY)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        var roomRoot = view.transform.Find("LanLobbyRoot/Room") as RectTransform;
        if (roomRoot != null && (rect == roomRoot || rect.IsChildOf(roomRoot)))
        {
            var scaleX = (float)captureWidth / CaptureWidth;
            var scaleY = (float)captureHeight / CaptureHeight;
            minX = corners.Min(corner => roomRoot.InverseTransformPoint(corner).x) * scaleX;
            minY = corners.Min(corner => roomRoot.InverseTransformPoint(corner).y) * scaleY;
            maxX = corners.Max(corner => roomRoot.InverseTransformPoint(corner).x) * scaleX;
            maxY = corners.Max(corner => roomRoot.InverseTransformPoint(corner).y) * scaleY;
            return;
        }

        minX = corners.Min(corner => corner.x);
        minY = corners.Min(corner => corner.y);
        maxX = corners.Max(corner => corner.x);
        maxY = corners.Max(corner => corner.y);
    }

    private static SourceAudit[] SourceAuditFor(
        string captureName,
        IEnumerable<SpriteSource> spriteSources,
        IEnumerable<CodeNativeGeometry> codeNativeGeometry)
    {
        var values = new List<SourceAudit>();
        foreach (var sprite in spriteSources)
        {
            if (string.IsNullOrEmpty(sprite.resourcesPath) ||
                string.IsNullOrEmpty(sprite.sourcePath) ||
                string.IsNullOrEmpty(sprite.sha256))
            {
                throw new InvalidOperationException(
                    "Room capture bitmap lacks approved provenance: " + sprite.spriteName);
            }

            values.Add(new SourceAudit
            {
                node = sprite.node,
                kind = "bitmap-sprite",
                isBitmap = true,
                spriteName = sprite.spriteName,
                materialName = string.Empty,
                resourcesPath = sprite.resourcesPath,
                sourcePath = sprite.sourcePath,
                sha256 = sprite.sha256,
                captures = new[] { captureName },
                occurrenceCount = 1,
                raycastTarget = sprite.raycastTarget
            });
        }

        foreach (var geometry in codeNativeGeometry)
        {
            if (geometry.raycastTarget)
                throw new InvalidOperationException("Rendered code-native geometry must not receive raycasts: " + geometry.name);
            values.Add(new SourceAudit
            {
                node = geometry.name,
                kind = "code-native-geometry",
                isBitmap = false,
                spriteName = string.Empty,
                materialName = string.Empty,
                resourcesPath = string.Empty,
                sourcePath = string.Empty,
                sha256 = string.Empty,
                captures = new[] { captureName },
                occurrenceCount = 1,
                raycastTarget = false
            });
        }

        return values.OrderBy(value => value.node, StringComparer.Ordinal).ToArray();
    }

    private static string HierarchyPath(Transform value, Transform root)
    {
        var names = new List<string>();
        for (var current = value; current != null && current != root; current = current.parent)
            names.Add(current.name);
        names.Reverse();
        return string.Join("/", names);
    }

    private static bool TryGetApprovedSource(string spriteName, out ApprovedSource source)
    {
        switch (spriteName)
        {
            case "bg_terrain": source = RootSource(spriteName, "ECE7B6159268276287C20E3B3A82A5165BCC1D344EDFA6DE3B88EE24A76F988C"); return true;
            case "shallow_main": source = RootSource(spriteName, "054110DDEE56F1D19FAFA846D821E6CBD83D47BEB70D4C399A84DA4035A11945"); return true;
            case "room_create_btn_bg": source = RootSource(spriteName, "2782FDCBE671CDD760FF46FD2B3A83CEB3F6396BC44FF48673220C8B21521606"); return true;
            case "room_join_btn_bg": source = RootSource(spriteName, "38C41C0997D76056E3491E6E7D58F47F90A2CD907A3B359E26989838A7AD6A11"); return true;
            case "create_icon": source = RootSource(spriteName, "AE047958EE4E7D43F368D3205110307200D30F79BCF393B36CBBDE37A8A00BA7"); return true;
            case "join_icon": source = RootSource(spriteName, "6DE45E7D0AF9FFAE47D271E6A719FE008412FF62704154D277788EA094381C09"); return true;
            case "img_player_bkg": source = RootSource(spriteName, "CB940ECA5FE84527D9AD4546C617120A90F94CD88663F98C66261EE60D45185A"); return true;
            case "img_player_confirmed": source = RootSource(spriteName, "921FC9B33FFC7CBA6D90E6CA3CEF29145DC05792B8A1B3C2399DAAEA8192CFB5"); return true;
            case "player_card_waiting": source = RootSource(spriteName, "3B87EBB1DD62B7A8BD4D525F7F2E7E4358F2F1C0B04A65997BA34E79E97F0C8C"); return true;
            case "player_card_ready": source = RootSource(spriteName, "F34786A3E832E97121EB03614B6D584C871B78E4C5CDD1FA6C5F9CB7D191A4C0"); return true;
            case "player_card_self_frame": source = RootSource(spriteName, "19F0D43B704F9CB92EE3BE11D9C64879D1BA9B542EDFF90E9DBDF7FCE381C3A3"); return true;
            case "team_icon_frame": source = RootSource(spriteName, "B05BFEAE52C1E54E9382936F653E291D118A976DA0B1AA6DE14720FC9CCC4380"); return true;
            case "team_hp_back": source = RootSource(spriteName, "6987D8B1428A3F51C9F4697B014A76515CF51130E74991E44FE660E838602E74"); return true;
            case "btn_match_host_normal": source = RootSource(spriteName, "9D36CBDA42FC64CEB7590CBEDF5E49176BFA90E63A8B263FD3C87EE08C3BF3DF"); return true;
            case "btn_match_host_grey": source = RootSource(spriteName, "C4CD3326EA4D04777AAA540525405DF8AA217D2E972443FDE95C202333F93614"); return true;
            case "btn_match_grey": source = RootSource(spriteName, "E774CB0533EB67BD2FE45F50339E36D0A6221BAF594E6AEE5E5C256469A4BA78"); return true;
            case "btn_match_cancel": source = RootSource(spriteName, "6DA0D4FD99A7A3595F5FE3C79ABA99A1F41006A4114E7555D324E059419A97B3"); return true;
            case "card_bg": source = RootSource(spriteName, "050B347451BBEBC74F5E3B09A2470931D9B2A85DEF707A4AAC42B5CE1B0BCEE2"); return true;
            case "bg_top_normal": source = RootSource(spriteName, "5A9479B9AFDD4FC3F597CCBF4A1A0D92C1BB1B5053D716E8C20267E6B2C77C4F"); return true;
            case "bg_top_ready": source = RootSource(spriteName, "EFAA99906A087AAF5AD631E4DF8CFCD7E90C4F463621779A13447675F221482D"); return true;
            case "card_empty": source = RootSource(spriteName, "4DD34E0B5BE318770082B14F245591D80F6ABFF00451744C4BEF3459798DCE31"); return true;
            case "card_deco_bg": source = RootSource(spriteName, "C907B3527747B947ECCA08757DD6601BCA46B8EC5AF835F61F226BBBF8E1EBF1"); return true;
            case "card_deco_self": source = RootSource(spriteName, "A3217A0EE5C8B1D7325758162C9859BEC765C7B63331881C90CDA4804A93F661"); return true;
            case "bg_plus": source = RootSource(spriteName, "E2CA5554B27862FE172E2D18D50092618B2E895C2AD63CDB57019BE593B7B66D"); return true;
            case "btn_match_normal": source = RootSource(spriteName, "62B586274488AE3A7BF203829DDFE0C80955993AE22334F46EDE076215C3ADCD"); return true;
            case "btn_topmenu_back": source = RootSource(spriteName, "BB78B1FCB84BA5F3A2FF8992809C8B0EFD4CAC5E1E960A8056BAA79A1A6E6303"); return true;
            case "img_return": source = RootSource(spriteName, "3F20542913541EAF1F175225268FD3E1EC0343C45A18D0BFE3F7DFBDDEFBEC09"); return true;
            case "host_top_tag": source = RootSource(spriteName, "861754CAFABFEF6641129CAC439501EE3E3D964E0FA3C72BC32FDAC117131009"); return true;
            case "doc_frame_line": source = HomeSource(spriteName, "4E4D96093514340112A0799D61611A65DA41153ACBD21F271184E0C0BB311C97"); return true;
            case "img_pointer": source = HomeSource(spriteName, "3CD944DC7F0F3B7DE675E8BBE23EEA9640D95DE65B4B2E91F14648385284F697"); return true;
            case "room_select_create_btn_bg_down": source = HomeSource(spriteName, "8709B2C46A88AD6CDA15F0F7E78C02AD78CDB09D3FA2D99F045BC556028CD149"); return true;
            case "room_select_create_left_line": source = HomeSource(spriteName, "4893EDC89BF8D9DFE3914673B0446D764D0663327579DAE19744E17C74296CA4"); return true;
            case "room_select_create_logo": source = HomeSource(spriteName, "908CC693B473E82FB92B84B3A825A18A0530DC4BFFE6A2266139241939A91CB4"); return true;
            case "room_select_create_middleicon": source = HomeSource(spriteName, "F728D411AA11A67775AA2A3CBBB1CBED665B914E1BE645DCCDB6BD34BCE288C2"); return true;
            case "room_select_create_text_01": source = HomeSource(spriteName, "52DC9A7C8E48DEC53AAF69D91A6FD0E0AA60483C1EE1412E32A007B8DF2E2D72"); return true;
            case "room_select_create_text_02": source = HomeSource(spriteName, "9C87A8FE6DFB72362BA8A84B089B66BC80F01822E2E3C0713396499D93F2CE33"); return true;
            case "room_select_dot": source = HomeSource(spriteName, "056E14212EA8E02D175D03582C4726FABA09AD518E89DB318CD3EF793E996DB0"); return true;
            case "room_select_img_startroom": source = HomeSource(spriteName, "495AA8F2BD5CD97EE12192DACC2CFD8A15731F0E131F9CD74F936B92A49E7E02"); return true;
            case "room_select_join_ban": source = HomeSource(spriteName, "F1ACA192CCCD6399884810A52CDC15E733C415E83579325779E67EB700EB052E"); return true;
            case "room_select_join_blank": source = HomeSource(spriteName, "099A060B78BCA5E39CA82E9747C94BFBC011868DE4AC18CB11C9149BC99AA2CD"); return true;
            case "room_select_join_btn_bg_down": source = HomeSource(spriteName, "71AE8387746003F1BF0DA72A3E92A7AACDB8A908B63FC6B26C553FC779D77468"); return true;
            case "room_select_join_left_block": source = HomeSource(spriteName, "1D10B384025DCF05972D7AEAFDF438FD88DB9B3B9B829DFD541E15103F100D10"); return true;
            case "room_select_join_logo": source = HomeSource(spriteName, "85FB957F0BC4A5172B0F454F77F6195068484B6DEBBD6DFCEE2A2AD1D5D93B59"); return true;
            case "room_select_join_middle_block": source = HomeSource(spriteName, "997CF5A781848535654D21D9B97D6105DA07F959AECB134DF1FB2F570E9861CA"); return true;
            case "room_select_join_middle_block_mask": source = HomeSource(spriteName, "95D0FAAF36EEF6681486944D95AB3F453DB0D9B70F23CD2E0DE12F57E2609EC5"); return true;
            case "room_select_join_right_block": source = HomeSource(spriteName, "11C872C6DE561E4409085E958D1CDEFA7647E9EEC5883ED91E45162B2B9FE6B8"); return true;
            case "room_select_join_text_01": source = HomeSource(spriteName, "F09FD74598C6EEF1066FB53CDA294681FAEA469981B6F215B7E7DD674E63CC39"); return true;
            case "room_select_join_text_02": source = HomeSource(spriteName, "F9DCC617D9BB74218E1554939A0897F39516B7965D9400EAD6CCB2DE85E19DD0"); return true;
            case "room_select_join_text_bg": source = HomeSource(spriteName, "36260875697359E27930467D123D2B684A2DE51D2448A5B295885D06AA518472"); return true;
            case "room_select_join_triangle": source = HomeSource(spriteName, "BA585545BC5EF8F6CC126BBFDE59F63A1C75D4B761646A22CCFEA195CB9B7FF7"); return true;
            case "room_select_right_bg": source = HomeSource(spriteName, "F65BE15390749F0FA175B49C310E90E9F3A29C753F2D320068B0831A5EBFDE53"); return true;
            case "room_select_title_icon": source = HomeSource(spriteName, "7C0E9E67D349013FC49DBAF33E1F462C0DF4171B1C6681DB50517629B4BDFF6C"); return true;
            case "icon_amiy": source = CombinedAvatarSource(spriteName, "14D5F8D3A8026751B511942517B9815BA3E04438857FEA649EF8A8A02B64868B"); return true;
            case "icon_clementi": source = CombinedAvatarSource(spriteName, "D5195FFE5CCCC61EA49DBC1CF3CD0493DA4CE131D033DEC7F91B446EC77152F8"); return true;
            case "icon_kirar": source = CombinedAvatarSource(spriteName, "A9B279D39C74BD8EDD9CCDC8F8A7AA6157E445639F99800E95481DF6BE84CEFA"); return true;
            case "icon_zumam": source = CombinedAvatarSource(spriteName, "B656BF323746029AD66469F350DCCC5B52F903F68E1ACEB2E202068AF41A1302"); return true;
            default:
                source = default(ApprovedSource);
                return false;
        }
    }

    private static ApprovedSource RootSource(string spriteName, string sha256)
    {
        return new ApprovedSource(
            "UI/Lobby/" + spriteName,
            AssetSourcePrefix + spriteName + ".png",
            sha256);
    }

    private static ApprovedSource HomeSource(string spriteName, string sha256)
    {
        return new ApprovedSource(
            "UI/Lobby/Home/" + spriteName,
            AssetSourcePrefix + spriteName + ".png",
            sha256);
    }

    private static ApprovedSource CombinedAvatarSource(string spriteName, string sha256)
    {
        return new ApprovedSource(
            "UI/Lobby/Home/" + spriteName,
            CombinedAvatarSourcePrefix + spriteName + ".png",
            sha256);
    }

    // Batchmode PlayMode has no graphics backbuffer, so its test seam writes a decodeable probe.
    // The Player-only command path above remains the only path that captures rendered pixels.
    private static void WriteTestProbe(string path)
    {
        var probe = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGBA32, false);
        var pixels = new Color32[CaptureWidth * CaptureHeight];
        var cyan = (Color32)Color.cyan;
        for (var index = 0; index < pixels.Length; index++) pixels[index] = cyan;
        probe.SetPixels32(pixels);
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
            new LobbyMemberSnapshot(new LobbyProfile("capture-host", "Doctor", 0), true, 18)
        }, false, 1);
    }

    private static LobbyRoomSnapshot ReadyRoom()
    {
        return new LobbyRoomSnapshot("654321", "capture-host", new[]
        {
            new LobbyMemberSnapshot(new LobbyProfile("capture-host", "Doctor", 0), true, 42),
            new LobbyMemberSnapshot(new LobbyProfile("capture-guest-1", "Amiya", 1), true, 56),
            new LobbyMemberSnapshot(new LobbyProfile("capture-guest-2", "Chen", 2), true, 71),
            new LobbyMemberSnapshot(new LobbyProfile("capture-guest-3", "Kal'tsit", 3), true, 95)
        }, false, 2);
    }

    private static LobbyRoomSnapshot FullRoom()
    {
        return new LobbyRoomSnapshot("654321", "capture-host", new[]
        {
            new LobbyMemberSnapshot(new LobbyProfile("capture-host", "Doctor", 0), true, 87),
            new LobbyMemberSnapshot(new LobbyProfile("capture-guest-1", "Amiya", 1), false, 64),
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
    [Serializable] private sealed class CaptureRecord { public string name; public string path; public int width; public int height; public float canvasScale; public string roomCode; public string localPlayerId; public bool primaryActionInteractable; public CaptureMember[] members; public long localLatencyMilliseconds; public CaptureRect[] rects; public CaptureRect[] keyRects; public SpriteSource[] spriteSources; public UnityText[] unityText; public CodeNativeGeometry[] codeNativeGeometry; public SourceAudit[] sourceAudit; }
    [Serializable] private sealed class CaptureMember { public string playerId; public string displayName; public int avatarIndex; public bool isReady; public long latencyMilliseconds; }
    [Serializable] private sealed class CaptureRect { public string name; public string coordinateOrigin; public string unit; public float x; public float y; public float width; public float height; }
    [Serializable] private sealed class SpriteSource { public string node; public string kind; public bool isBitmap; public string spriteName; public string materialName; public string resourcesPath; public string sourcePath; public string sha256; public string[] captures; public int occurrenceCount; public bool raycastTarget; public string coordinateOrigin; public string unit; public float x; public float y; public float width; public float height; }
    [Serializable] private sealed class UnityText { public string node; public string text; public string fontName; public string fontResourcePath; public bool hasBitmapSource; public string bitmapSourcePath; }
    [Serializable] private sealed class CodeNativeGeometry { public string name; public string kind; public bool isBitmap; public string spriteName; public string materialName; public string resourcesPath; public string sourcePath; public string sha256; public string color; public string coordinateOrigin; public string unit; public bool raycastTarget; public float x; public float y; public float width; public float height; }
    [Serializable] private sealed class SourceAudit { public string node; public string kind; public bool isBitmap; public string spriteName; public string materialName; public string resourcesPath; public string sourcePath; public string sha256; public string[] captures; public int occurrenceCount; public bool raycastTarget; }

    private readonly struct ApprovedSource
    {
        public ApprovedSource(string resourcesPath, string sourcePath, string sha256)
        {
            ResourcesPath = resourcesPath;
            SourcePath = sourcePath;
            Sha256 = sha256;
        }

        public string ResourcesPath { get; }
        public string SourcePath { get; }
        public string Sha256 { get; }
    }
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
