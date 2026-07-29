using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ArknoNights.Battle.Tests
{
    public sealed class BondsUnitResourceImportEditModeTests
    {
        private const string BondSpecRelativePath = "docs/bonds/BONDS_SPEC.md";

        [Test]
        public void AllBondsVariants_HaveCanonicalRawFilesLoadableSpineAssetsAndPortraits()
        {
            var repositoryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var specificationPath = Path.Combine(repositoryRoot, BondSpecRelativePath);
            var typeIds = ReadTypeIds(specificationPath);
            Assert.That(typeIds.Count, Is.EqualTo(99));

            var characterRoot = Path.Combine(Application.dataPath, "Resources", "Characters");
            var projectUnitKeys = Directory.GetDirectories(characterRoot)
                .Select(Path.GetFileName)
                .Where(unitKey => TryReadTypeId(unitKey, out var typeId) && typeIds.Contains(typeId))
                .OrderBy(unitKey => unitKey, StringComparer.Ordinal)
                .ToArray();
            Assert.That(projectUnitKeys.Length, Is.EqualTo(182));
            Assert.That(
                projectUnitKeys.Select(ReadTypeId).Distinct().OrderBy(value => value),
                Is.EqualTo(typeIds.OrderBy(value => value)));

            foreach (var unitKey in projectUnitKeys)
            {
                var characterDirectory = Path.Combine(Application.dataPath, "Resources", "Characters", unitKey);
                var profilePicturePath = Path.Combine(Application.dataPath, "Resources", "ProfilePicture", "UIImage_" + unitKey + ".png");
                var rawFiles = new[]
                {
                    "enemy_" + unitKey + ".atlas.txt",
                    "enemy_" + unitKey + ".png",
                    "enemy_" + unitKey + ".skel.bytes"
                };

                Assert.That(Directory.Exists(characterDirectory), Is.True, "Character folder missing for " + unitKey);
                foreach (var fileName in rawFiles)
                {
                    var path = Path.Combine(characterDirectory, fileName);
                    Assert.That(File.Exists(path), Is.True, "Imported resource missing: " + path);
                }
                Assert.That(File.Exists(profilePicturePath), Is.True, "Imported portrait missing: " + profilePicturePath);
                Assert.That(
                    Directory.GetFiles(characterDirectory, "*.json", SearchOption.TopDirectoryOnly),
                    Is.Empty,
                    "External staging metadata must not be copied into Unity Resources: " + unitKey);

                var skeletonAssetPath = "Assets/Resources/Characters/" + unitKey + "/enemy_" + unitKey + "_SkeletonData.asset";
                var skeletonAsset = AssetDatabase.LoadMainAssetAtPath(skeletonAssetPath);
                Assert.That(skeletonAsset, Is.Not.Null, "SkeletonDataAsset missing: " + skeletonAssetPath);
                Assert.That(skeletonAsset.GetType().FullName, Is.EqualTo("Spine.Unity.SkeletonDataAsset"), "Unexpected skeleton asset type: " + skeletonAssetPath);
                var getSkeletonData = skeletonAsset.GetType().GetMethod("GetSkeletonData", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(bool) }, null);
                Assert.That(getSkeletonData, Is.Not.Null, "Spine API is unavailable: " + skeletonAssetPath);
                Assert.That(getSkeletonData.Invoke(skeletonAsset, new object[] { true }), Is.Not.Null, "Spine data cannot be loaded: " + skeletonAssetPath);

                var portrait = Resources.Load<Texture2D>("ProfilePicture/UIImage_" + unitKey);
                Assert.That(portrait, Is.Not.Null, "Portrait is not loadable from Resources: " + unitKey);
                Assert.That(portrait.width, Is.EqualTo(158), "Portrait width mismatch: " + unitKey);
                Assert.That(portrait.height, Is.EqualTo(158), "Portrait height mismatch: " + unitKey);
                var portraitImporter = AssetImporter.GetAtPath(
                    "Assets/Resources/ProfilePicture/UIImage_" + unitKey + ".png")
                    as TextureImporter;
                Assert.That(portraitImporter, Is.Not.Null, "Portrait importer missing: " + unitKey);
                Assert.That(
                    portraitImporter.textureType,
                    Is.EqualTo(TextureImporterType.Default),
                    "Portrait must remain a Default Texture: " + unitKey);
                Assert.That(
                    portraitImporter.npotScale,
                    Is.EqualTo(TextureImporterNPOTScale.None),
                    "Portrait must retain its exact 158x158 dimensions: " + unitKey);
            }

            Assert.That(projectUnitKeys, Does.Contain("1322_wdgyht"), "1322 default variant must use the unsuffixed resource key.");
            Assert.That(projectUnitKeys, Does.Contain("1322_wdgyht_2"), "1322 elite-two variant must use the _2 resource key.");
            Assert.That(projectUnitKeys.Any(unitKey => ReadTypeId(unitKey) == 1021), Is.False);
        }

        private static HashSet<int> ReadTypeIds(string specificationPath)
        {
            var specification = File.ReadAllText(specificationPath, new UTF8Encoding(false, true));
            var sections = Regex.Matches(specification, @"(?ms)^## [^\r\n]+\s*```text\s*(?<ids>.*?)\s*```");
            Assert.That(sections.Count, Is.EqualTo(2), "The BONDS specification must keep exactly two TypeId text sections.");
            return sections
                .Cast<Match>()
                .SelectMany(section => Regex.Matches(section.Groups["ids"].Value, @"\d+").Cast<Match>())
                .Select(match => int.Parse(match.Value, System.Globalization.CultureInfo.InvariantCulture))
                .ToHashSet();
        }

        private static bool TryReadTypeId(string unitKey, out int typeId)
        {
            typeId = default;
            var separator = unitKey.IndexOf('_');
            return separator > 0
                && int.TryParse(
                    unitKey.Substring(0, separator),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out typeId);
        }

        private static int ReadTypeId(string unitKey)
        {
            Assert.That(TryReadTypeId(unitKey, out var typeId), Is.True, "Invalid unit key: " + unitKey);
            return typeId;
        }
    }
}
