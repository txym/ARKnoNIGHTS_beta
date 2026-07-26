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
        LobbyAssetDirectory + "btn_match_cancel.png",
        LobbyAssetDirectory + "Home/room_select_right_bg.png",
        LobbyAssetDirectory + "Home/room_select_title_icon.png",
        LobbyAssetDirectory + "Home/room_select_dot.png",
        LobbyAssetDirectory + "Home/room_select_img_startroom.png",
        LobbyAssetDirectory + "Home/room_select_create_btn_bg_down.png",
        LobbyAssetDirectory + "Home/room_select_create_left_line.png",
        LobbyAssetDirectory + "Home/room_select_create_logo.png",
        LobbyAssetDirectory + "Home/room_select_create_middleicon.png",
        LobbyAssetDirectory + "Home/room_select_create_text_01.png",
        LobbyAssetDirectory + "Home/room_select_create_text_02.png",
        LobbyAssetDirectory + "Home/room_select_join_ban.png",
        LobbyAssetDirectory + "Home/room_select_join_blank.png",
        LobbyAssetDirectory + "Home/room_select_join_btn_bg_down.png",
        LobbyAssetDirectory + "Home/room_select_join_left_block.png",
        LobbyAssetDirectory + "Home/room_select_join_logo.png",
        LobbyAssetDirectory + "Home/room_select_join_middle_block.png",
        LobbyAssetDirectory + "Home/room_select_join_middle_block_mask.png",
        LobbyAssetDirectory + "Home/room_select_join_right_block.png",
        LobbyAssetDirectory + "Home/room_select_join_text_01.png",
        LobbyAssetDirectory + "Home/room_select_join_text_02.png",
        LobbyAssetDirectory + "Home/room_select_join_text_bg.png",
        LobbyAssetDirectory + "Home/room_select_join_triangle.png",
        LobbyAssetDirectory + "Home/icon_amiy.png",
        LobbyAssetDirectory + "Home/icon_clementi.png",
        LobbyAssetDirectory + "Home/icon_kirar.png",
        LobbyAssetDirectory + "Home/icon_zumam.png"
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
