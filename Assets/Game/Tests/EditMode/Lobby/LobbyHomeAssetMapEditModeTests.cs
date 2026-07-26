using System;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ArknoNights.Lobby.Tests
{
    public sealed class LobbyHomeAssetMapEditModeTests
    {
        private static readonly string UnpackedOuterDirectory = Path.Combine("G:\\", "素材", "11.14", "Unpacked_1763129662", "Android", "ui", "autochess", "[uc]autochessouter");
        private static readonly string CombinedCommonDirectory = Path.Combine("G:\\", "素材", "11.14", "Combined_1763139377", "Android", "ui", "autochess", "[uc]autochesscommon");

        private static readonly string[] RoomSelectAssetNames =
        {
            "room_select_right_bg", "room_select_title_icon", "room_select_dot", "room_select_img_startroom",
            "room_select_create_btn_bg_down", "room_select_create_left_line", "room_select_create_logo", "room_select_create_middleicon", "room_select_create_text_01", "room_select_create_text_02",
            "room_select_join_ban", "room_select_join_blank", "room_select_join_btn_bg_down", "room_select_join_left_block", "room_select_join_logo", "room_select_join_middle_block", "room_select_join_middle_block_mask", "room_select_join_right_block", "room_select_join_text_01", "room_select_join_text_02", "room_select_join_text_bg", "room_select_join_triangle"
        };

        private static readonly string[] AvatarAssetNames =
        {
            "icon_amiy", "icon_clementi", "icon_kirar", "icon_zumam"
        };

        [Test]
        public void HomeAssets_AreAvailableAsSingleSpritesFromResources()
        {
            foreach (var assetName in RoomSelectAssetNames)
                AssertSprite("Assets/Resources/UI/Lobby/Home/" + assetName + ".png", "UI/Lobby/Home/" + assetName);

            foreach (var assetName in AvatarAssetNames)
                AssertSprite("Assets/Resources/UI/Lobby/Home/" + assetName + ".png", "UI/Lobby/Home/" + assetName);
        }

        [Test]
        public void HomeAssetMap_UsesNormalUnpackedRoomSelectAndCombinedCommonAvatars()
        {
            var map = File.ReadAllText(ProjectPath("docs/references/ui/lobby/ASSET_MAP.md"));

            foreach (var assetName in RoomSelectAssetNames)
            {
                var mapRow = assetName + ".png | [uc]autochessouter/" + assetName + ".png";
                StringAssert.Contains(mapRow, map);
                StringAssert.DoesNotContain(assetName + "$0.png", map);
            }

            foreach (var assetName in AvatarAssetNames)
                StringAssert.Contains(assetName + ".png | Combined/[uc]autochesscommon/" + assetName + ".png", map);
        }

        [Test]
        public void HomeAssetMap_DeclaresBothApprovedRootsAndImportedBytesMatchTheirDeclaredSourceType()
        {
            var map = File.ReadAllText(ProjectPath("docs/references/ui/lobby/ASSET_MAP.md"));
            StringAssert.Contains("Approved source roots", map);
            StringAssert.Contains("Unpacked_1763129662", map);
            StringAssert.Contains("Combined_1763139377", map);

            foreach (var assetName in RoomSelectAssetNames)
                AssertSourceHashMatches(assetName, UnpackedOuterDirectory);

            foreach (var assetName in AvatarAssetNames)
                AssertSourceHashMatches(assetName, CombinedCommonDirectory);
        }

        private static void AssertSprite(string assetPath, string resourcePath)
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<Sprite>(assetPath), Is.Not.Null, assetPath + " must import as a Sprite.");
            Assert.That(Resources.Load<Sprite>(resourcePath), Is.Not.Null, resourcePath + " must be loadable from Resources.");
        }

        private static void AssertSourceHashMatches(string assetName, string sourceDirectory)
        {
            var importedPath = ProjectPath("Assets/Resources/UI/Lobby/Home/" + assetName + ".png");
            var sourcePath = Path.Combine(sourceDirectory, assetName + ".png");

            Assert.That(File.Exists(sourcePath), Is.True, "Approved source must be available for provenance audit: " + sourcePath);
            Assert.That(File.Exists(importedPath), Is.True, "Imported Home asset must exist: " + importedPath);
            Assert.That(ComputeSha256(importedPath), Is.EqualTo(ComputeSha256(sourcePath)), assetName + " must byte-match its approved source.");
        }

        private static string ComputeSha256(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
        }

        private static string ProjectPath(string path)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
        }
    }
}
