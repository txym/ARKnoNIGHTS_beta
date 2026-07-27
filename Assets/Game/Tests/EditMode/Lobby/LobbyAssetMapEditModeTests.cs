using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ArknoNights.Lobby.Tests
{
    public sealed class LobbyAssetMapEditModeTests
    {
        private static readonly string UnpackedRootDirectory = Path.Combine("G:\\", "素材", "11.14", "Unpacked_1763129662", "Android", "ui", "autochess");

        private static readonly string[] ExpectedAssetNames =
        {
            "bg_terrain", "shallow_main", "room_create_btn_bg", "room_join_btn_bg", "create_icon", "join_icon",
            "img_player_bkg", "img_player_confirmed", "player_card_waiting", "player_card_ready", "player_card_self_frame",
            "team_icon_frame", "team_hp_back", "btn_match_host_normal", "btn_match_host_grey", "btn_match_grey", "btn_match_cancel",
            "card_bg", "bg_top_normal", "bg_top_ready", "card_empty", "card_deco_self", "bg_plus", "btn_match_normal", "btn_topmenu_back", "host_top_tag"
        };

        [Test]
        public void LobbyAssetMap_MapsEveryImportedPngToApprovedSource()
        {
            var importedFiles = Directory.GetFiles(ProjectPath("Assets/Resources/UI/Lobby"), "*.png");
            var mapRows = ReadMapRows();

            Assert.That(importedFiles, Has.Length.EqualTo(ExpectedAssetNames.Length));
            Assert.That(importedFiles.Select(Path.GetFileNameWithoutExtension), Is.EquivalentTo(ExpectedAssetNames));
            foreach (var assetName in ExpectedAssetNames)
            {
                var matchingRows = mapRows.Where(row => row.AssetName == assetName).ToArray();
                Assert.That(matchingRows, Has.Length.EqualTo(1), assetName + " must appear exactly once in ASSET_MAP.md.");
                Assert.That(matchingRows[0].SourceRelativePath, Is.Not.Empty);
                Assert.That(matchingRows[0].Sha256, Is.Not.Empty);
                Assert.That(matchingRows[0].SourceKind, Is.EqualTo("Unpacked direct").Or.EqualTo("Combined atlas sprite"));
            }
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

        [TestCaseSource(nameof(ExpectedAssetNames))]
        public void ApprovedLobbyAsset_BytesMatchMappedSource(string assetName)
        {
            var row = ReadMapRow(assetName);
            Assert.That(row.SourceRelativePath, Does.Not.Contain("$0").And.Not.Contain("#0"));
            Assert.That(row.Sha256, Does.Match("^[0-9A-F]{64}$"));
            Assert.That(row.SourceKind, Is.EqualTo("Unpacked direct"));
            AssertSourceHashMatches(assetName, row.SourceRelativePath, row.Sha256);
        }

        private static AssetMapRow ReadMapRow(string assetName)
        {
            var matchingRows = ReadMapRows().Where(row => row.AssetName == assetName).ToArray();
            Assert.That(matchingRows, Has.Length.EqualTo(1), assetName + " must appear exactly once in ASSET_MAP.md.");
            return matchingRows[0];
        }

        private static AssetMapRow[] ReadMapRows()
        {
            return File.ReadAllLines(ProjectPath("docs/references/ui/lobby/ASSET_MAP.md"))
                .Where(line => line.StartsWith("| ", StringComparison.Ordinal) && line.Contains(".png |"))
                .Select(line => line.Split('|').Select(column => column.Trim()).ToArray())
                .Where(columns => columns.Length >= 8 && columns[1].EndsWith(".png", StringComparison.Ordinal))
                .Select(columns => new AssetMapRow(
                    Path.GetFileNameWithoutExtension(columns[1]),
                    columns[2],
                    columns[6],
                    columns[7]))
                .ToArray();
        }

        private static void AssertSourceHashMatches(string assetName, string sourceRelativePath, string expectedSha256)
        {
            var sourcePath = Path.Combine(UnpackedRootDirectory, sourceRelativePath);
            var importedPath = ProjectPath("Assets/Resources/UI/Lobby/" + assetName + ".png");

            Assert.That(File.Exists(sourcePath), Is.True, "Approved source must be available for provenance audit: " + sourcePath);
            Assert.That(File.Exists(importedPath), Is.True, "Imported lobby asset must exist: " + importedPath);
            Assert.That(ComputeSha256(sourcePath), Is.EqualTo(expectedSha256), assetName + " source bytes must match ASSET_MAP.md.");
            Assert.That(ComputeSha256(importedPath), Is.EqualTo(expectedSha256), assetName + " must byte-match its approved source.");
        }

        private static string ComputeSha256(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
        }

        private sealed class AssetMapRow
        {
            public AssetMapRow(string assetName, string sourceRelativePath, string sha256, string sourceKind)
            {
                AssetName = assetName;
                SourceRelativePath = sourceRelativePath;
                Sha256 = sha256;
                SourceKind = sourceKind;
            }

            public string AssetName { get; }
            public string SourceRelativePath { get; }
            public string Sha256 { get; }
            public string SourceKind { get; }
        }

        private static string ProjectPath(string path)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
        }
    }
}
