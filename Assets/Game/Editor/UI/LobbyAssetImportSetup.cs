using System;
using System.Collections.Generic;
using UnityEditor;

public sealed class LobbyAssetImportSetup : AssetPostprocessor
{
    private const string LobbyAssetDirectory = "Assets/Resources/UI/Lobby/";

    private static readonly HashSet<string> ApprovedAssetPaths = new HashSet<string>(StringComparer.Ordinal)
    {
        LobbyAssetDirectory + "bg_terrain.png",
        LobbyAssetDirectory + "shallow_main.png",
        LobbyAssetDirectory + "room_create_btn_bg.png",
        LobbyAssetDirectory + "room_join_btn_bg.png",
        LobbyAssetDirectory + "create_icon.png",
        LobbyAssetDirectory + "join_icon.png",
        LobbyAssetDirectory + "img_player_bkg.png",
        LobbyAssetDirectory + "img_player_confirmed.png",
        LobbyAssetDirectory + "player_card_waiting.png",
        LobbyAssetDirectory + "player_card_ready.png",
        LobbyAssetDirectory + "player_card_self_frame.png",
        LobbyAssetDirectory + "team_icon_frame.png",
        LobbyAssetDirectory + "team_hp_back.png",
        LobbyAssetDirectory + "btn_match_host_normal.png",
        LobbyAssetDirectory + "btn_match_host_grey.png",
        LobbyAssetDirectory + "btn_match_grey.png",
        LobbyAssetDirectory + "btn_match_cancel.png"
    };

    private void OnPreprocessTexture()
    {
        if (!ApprovedAssetPaths.Contains(assetPath)) return;

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;
    }

    [MenuItem("ARKnoNIGHTS/UI/Apply Lobby Asset Import Settings")]
    public static void ApplyLobbyAssetImportSettings()
    {
        foreach (var assetPath in ApprovedAssetPaths)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) continue;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
        }
    }
}
