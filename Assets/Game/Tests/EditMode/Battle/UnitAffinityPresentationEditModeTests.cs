using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ArknoNights.UI;
using ArknoNights.UI.FormalHud.ShopReady;
using NUnit.Framework;
using UnityEngine;

namespace ArknoNights.Battle.Tests
{
    public sealed class UnitAffinityPresentationEditModeTests
    {
        private static readonly string[] OccupationNames =
        {
            "感染生物", "无人机", "造物", "机械", "坍缩体", "其他"
        };

        private static readonly string[] RegionNames =
        {
            "乌萨斯", "莱塔尼亚", "维多利亚", "哥伦比亚",
            "萨尔贡", "阿戈尔", "叙拉古", "整合运动"
        };

        [Test]
        public void Catalog_PrefersRegionIconAndFallsBackToOccupation()
        {
            var catalog = UnitAffinityPresentationCatalog.Parse(ValidJson);

            Assert.IsTrue(catalog.TryGet("1000", out var regionMember));
            Assert.AreEqual("整合运动", regionMember.RegionName);
            Assert.AreEqual(
                "UI/Texture/region/logo_reunionMovement",
                regionMember.PreferredHeaderIconResourcePath);

            Assert.IsTrue(catalog.TryGet("2043", out var collapsal));
            Assert.AreEqual(string.Empty, collapsal.RegionName);
            Assert.AreEqual(
                "UI/Texture/occupation/logo_sami",
                collapsal.PreferredHeaderIconResourcePath);

            Assert.IsTrue(catalog.TryGet("5503", out var other));
            Assert.AreEqual("其他", other.OccupationName);
            Assert.AreEqual(string.Empty, other.OccupationIconResourcePath);
            Assert.AreEqual(string.Empty, other.PreferredHeaderIconResourcePath);

            Assert.IsTrue(catalog.TryGet("1014", out var regionOnly));
            Assert.AreEqual("整合运动", regionOnly.RegionName);
            Assert.AreEqual(string.Empty, regionOnly.OccupationName);
            Assert.AreEqual(
                "UI/Texture/region/logo_reunionMovement",
                regionOnly.PreferredHeaderIconResourcePath);
        }

        [Test]
        public void Catalog_RejectsInvalidSchemaDefinitionsAndUnitReferences()
        {
            var invalidDocuments = new[]
            {
                ValidJson.Replace(
                    UnitAffinityPresentationCatalog.SchemaVersion,
                    "unit-affinity-presentation-v2"),
                ValidJson.Replace(
                    "\"regions\":[",
                    "\"regions\":[{\"id\":\"reunion\",\"displayName\":\"重复\",\"iconResourcePath\":\"x\"},"),
                ValidJson.Replace(
                    "\"occupations\":[",
                    "\"occupations\":[{\"id\":\"infected\",\"displayName\":\"重复\",\"iconResourcePath\":\"x\"},"),
                ValidJson.Replace("\"typeId\":\"1000\"", "\"typeId\":\"\""),
                ValidJson.Replace(
                    "\"units\":[",
                    "\"units\":[{\"typeId\":\"1000\",\"regionId\":\"reunion\",\"occupationId\":\"infected\"},"),
                ValidJson.Replace("\"occupationId\":\"infected\"", "\"occupationId\":\"unknown\""),
                ValidJson.Replace("\"regionId\":\"reunion\"", "\"regionId\":\"unknown\""),
                ValidJson.Replace(
                    "\"typeId\":\"1014\",\"regionId\":\"reunion\",\"occupationId\":\"\"",
                    "\"typeId\":\"1014\",\"regionId\":\"\",\"occupationId\":\"\"")
            };

            foreach (var json in invalidDocuments)
                Assert.Throws<System.FormatException>(
                    () => UnitAffinityPresentationCatalog.Parse(json));
        }

        [Test]
        public void RuntimeCatalog_MatchesEveryShopUnitInBondsAndLoadsConfiguredIcons()
        {
            var specificationPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "../docs/bonds/BONDS_SPEC.md"));
            var lines = File.ReadAllLines(specificationPath);
            var shopTypeIds = ReadShopTypeIds(lines);
            var occupationsByTypeId = ReadMembership(
                lines,
                "# 按种类组织的单位",
                "# 种类与地区覆盖审计",
                OccupationNames,
                shopTypeIds);
            var regionsByTypeId = ReadMembership(
                lines,
                "# 按地区组织的单位",
                null,
                RegionNames,
                shopTypeIds);
            var catalog = UnitAffinityPresentationCatalog.LoadFromResources();
            var runtimeByTypeId = catalog.Entries.ToDictionary(item => item.TypeId);
            var canonicalEntries = shopTypeIds
                .Select(typeId => runtimeByTypeId[typeId])
                .ToArray();

            CollectionAssert.IsSubsetOf(shopTypeIds, runtimeByTypeId.Keys);
            Assert.AreEqual(94, shopTypeIds.Count);
            Assert.AreEqual(95, runtimeByTypeId.Count);
            CollectionAssert.AreEquivalent(
                new[] { "1000" },
                runtimeByTypeId.Keys.Except(shopTypeIds).ToArray());
            Assert.AreEqual(81, canonicalEntries.Count(
                item => !string.IsNullOrEmpty(item.RegionName)));
            Assert.AreEqual(13, canonicalEntries.Count(
                item => string.IsNullOrEmpty(item.RegionName)));
            Assert.AreEqual(86, canonicalEntries.Count(
                item => !string.IsNullOrEmpty(item.OccupationName)));
            Assert.AreEqual(8, canonicalEntries.Count(
                item => string.IsNullOrEmpty(item.OccupationName)));

            foreach (var typeId in shopTypeIds)
            {
                Assert.AreEqual(
                    occupationsByTypeId.TryGetValue(typeId, out var occupation)
                        ? occupation
                        : string.Empty,
                    runtimeByTypeId[typeId].OccupationName,
                    "Occupation mismatch for TypeId " + typeId);
                Assert.AreEqual(
                    regionsByTypeId.TryGetValue(typeId, out var region)
                        ? region
                        : string.Empty,
                    runtimeByTypeId[typeId].RegionName,
                    "Region mismatch for TypeId " + typeId);
            }

            var iconPaths = catalog.Entries
                .SelectMany(item => new[]
                {
                    item.RegionIconResourcePath,
                    item.OccupationIconResourcePath
                })
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            Assert.AreEqual(13, iconPaths.Length);
            foreach (var path in iconPaths)
                Assert.NotNull(
                    FormalHudSpriteLoader.Load(path),
                    "Configured affinity icon must load: " + path);

            var other = runtimeByTypeId["5503"];
            Assert.AreEqual("其他", other.OccupationName);
            Assert.AreEqual(string.Empty, other.OccupationIconResourcePath);

            var legacyDemo = runtimeByTypeId["1000"];
            Assert.AreEqual("整合运动", legacyDemo.RegionName);
            Assert.AreEqual("感染生物", legacyDemo.OccupationName);
        }

        private static HashSet<string> ReadShopTypeIds(IReadOnlyList<string> lines)
        {
            var heading = FindLine(lines, "## 商店单位（94）");
            var inCodeBlock = false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var index = heading + 1; index < lines.Count; index++)
            {
                var line = lines[index].Trim();
                if (line == "```text")
                {
                    inCodeBlock = true;
                    continue;
                }

                if (inCodeBlock && line == "```") break;
                if (!inCodeBlock) continue;
                foreach (Match match in Regex.Matches(line, @"\d+"))
                    ids.Add(match.Value);
            }

            Assert.AreEqual(94, ids.Count, "BONDS shop list must contain 94 unique TypeIds.");
            return ids;
        }

        private static Dictionary<string, string> ReadMembership(
            IReadOnlyList<string> lines,
            string startHeading,
            string endHeading,
            IReadOnlyCollection<string> allowedHeadings,
            ISet<string> shopTypeIds)
        {
            var start = FindLine(lines, startHeading);
            var end = endHeading == null ? lines.Count : FindLine(lines, endHeading);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            string current = null;
            for (var index = start + 1; index < end; index++)
            {
                var line = lines[index].Trim();
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    var candidate = line.Substring(3).Trim();
                    current = allowedHeadings.Contains(candidate) ? candidate : null;
                    continue;
                }

                if (current == null) continue;
                var match = Regex.Match(line, @"^(\d+)\b");
                if (!match.Success || !shopTypeIds.Contains(match.Groups[1].Value)) continue;
                Assert.IsTrue(
                    result.TryAdd(match.Groups[1].Value, current),
                    "TypeId appears in more than one " + startHeading + " section: "
                    + match.Groups[1].Value);
            }

            return result;
        }

        private static int FindLine(IReadOnlyList<string> lines, string expected)
        {
            for (var index = 0; index < lines.Count; index++)
                if (string.Equals(lines[index].Trim(), expected, StringComparison.Ordinal))
                    return index;
            Assert.Fail("Required BONDS heading is missing: " + expected);
            return -1;
        }

        private const string ValidJson =
            "{\"schemaVersion\":\"unit-affinity-presentation-v1\","
            + "\"regions\":[{\"id\":\"reunion\",\"displayName\":\"整合运动\","
            + "\"iconResourcePath\":\"UI/Texture/region/logo_reunionMovement\"}],"
            + "\"occupations\":["
            + "{\"id\":\"infected\",\"displayName\":\"感染生物\","
            + "\"iconResourcePath\":\"UI/Texture/occupation/r_enemy_slime_repbsl_3\"},"
            + "{\"id\":\"collapsal\",\"displayName\":\"坍缩体\","
            + "\"iconResourcePath\":\"UI/Texture/occupation/logo_sami\"},"
            + "{\"id\":\"other\",\"displayName\":\"其他\",\"iconResourcePath\":\"\"}],"
            + "\"units\":["
            + "{\"typeId\":\"1000\",\"regionId\":\"reunion\",\"occupationId\":\"infected\"},"
            + "{\"typeId\":\"1014\",\"regionId\":\"reunion\",\"occupationId\":\"\"},"
            + "{\"typeId\":\"2043\",\"regionId\":\"\",\"occupationId\":\"collapsal\"},"
            + "{\"typeId\":\"5503\",\"regionId\":\"\",\"occupationId\":\"other\"}]}";
    }
}
