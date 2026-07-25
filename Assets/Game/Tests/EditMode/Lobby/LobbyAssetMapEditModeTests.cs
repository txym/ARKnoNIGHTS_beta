using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ArknoNights.Lobby.Tests
{
    public sealed class LobbyAssetMapEditModeTests
    {
        private static readonly string[] ExpectedAssetNames =
        {
            "bg_terrain", "shallow_main", "room_create_btn_bg", "room_join_btn_bg", "create_icon", "join_icon",
            "img_player_bkg", "img_player_confirmed", "player_card_waiting", "player_card_ready", "player_card_self_frame",
            "team_icon_frame", "team_hp_back", "btn_match_host_normal", "btn_match_host_grey", "btn_match_grey", "btn_match_cancel"
        };

        [Test]
        public void LobbyAssetMap_MapsEveryImportedPngToApprovedSource()
        {
            var map = File.ReadAllText(ProjectPath("docs/references/ui/lobby/ASSET_MAP.md"));
            var importedFiles = Directory.GetFiles(ProjectPath("Assets/Resources/UI/Lobby"), "*.png");

            Assert.That(importedFiles, Has.Length.EqualTo(ExpectedAssetNames.Length));
            foreach (var file in importedFiles)
                StringAssert.Contains(Path.GetFileName(file) + " | [uc]autochessouter/", map);
        }

        [Test]
        public void LobbyAssets_AreAvailableAsSingleSpritesFromResources()
        {
            foreach (var assetName in ExpectedAssetNames)
            {
                var assetPath = "Assets/Resources/UI/Lobby/" + assetName + ".png";
                Assert.That(AssetDatabase.LoadAssetAtPath<Sprite>(assetPath), Is.Not.Null, assetPath + " must import as a Sprite.");
                Assert.That(Resources.Load<Sprite>("UI/Lobby/" + assetName), Is.Not.Null, assetName + " must be loadable from Resources.");
            }
        }

        private static string ProjectPath(string path)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
        }
    }
}
