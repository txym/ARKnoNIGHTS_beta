using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Spine.Unity;
using UnityEngine;

internal static class BondsUnitAnimationAudit
{
    internal const string SchemaVersion = "bonds-unit-animation-audit-v1";
    private const int ExpectedTypeIdCount = 93;
    private const int ExpectedVariantCount = 172;
    private const string BondSpecRelativePath = "docs/bonds/BONDS_SPEC.md";
    private const string CharacterAssetRoot = "Assets/Resources/Characters";
    private const string CharacterResourceRoot = "Characters";
    private const string DefaultOutputRelativePath =
        "Temp/bonds-unit-animation-audit-v1.json";

    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private static readonly Regex TypeIdSectionPattern =
        new Regex(@"(?ms)^## [^\r\n]+\s*```text\s*(?<ids>.*?)\s*```");
    private static readonly Regex DecimalPattern = new Regex(@"\d+");
    private static readonly Regex UnitKeyPattern =
        new Regex(@"^(?<typeId>\d+)_", RegexOptions.CultureInvariant);
    private static readonly Regex AnimationTokenPattern =
        new Regex(
            @"[A-Z]+(?=[A-Z][a-z]|\d|$)|[A-Z]?[a-z]+|[A-Z]+|\d+",
            RegexOptions.CultureInvariant);

    internal static BondsUnitAnimationAuditDocument BuildDocument()
    {
        var repositoryRoot = RepositoryRoot();
        var typeIds = ReadTypeIds(Path.Combine(repositoryRoot, BondSpecRelativePath));
        var variants = ReadVariants(repositoryRoot, typeIds);
        var diagnostics = variants
            .SelectMany(variant => variant.animations
                .Where(animation => animation.durationSeconds == 0f)
                .Select(animation =>
                    "ZERO_DURATION unitKey=" + variant.unitKey
                    + " animation=" + animation.name))
            .OrderBy(message => message, StringComparer.Ordinal)
            .ToArray();

        return new BondsUnitAnimationAuditDocument
        {
            schemaVersion = SchemaVersion,
            generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            typeIdCount = typeIds.Count,
            variantCount = variants.Length,
            variants = variants,
            signatures = BuildSignatures(variants),
            tokenSummary = BuildTokenSummary(variants),
            diagnostics = diagnostics
        };
    }

    internal static string Serialize(BondsUnitAnimationAuditDocument document)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        var json = JsonUtility.ToJson(document, true) + "\n";
        var roundTripped = JsonUtility.FromJson<BondsUnitAnimationAuditDocument>(json);
        ValidateRoundTrip(document, roundTripped);
        return json;
    }

    public static void Run()
    {
        var repositoryRoot = RepositoryRoot();
        var outputPath = ReadOutputPath(repositoryRoot);
        var document = BuildDocument();
        var json = Serialize(document);

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrEmpty(outputDirectory))
        {
            throw new InvalidOperationException(
                "BONDS_ANIMATION_AUDIT_OUTPUT_DIRECTORY_MISSING path=" + outputPath);
        }

        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(outputPath, json, StrictUtf8);
        var persisted = File.ReadAllText(outputPath, StrictUtf8);
        if (!string.Equals(persisted, json, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "BONDS_ANIMATION_AUDIT_OUTPUT_VERIFY_FAILED path=" + outputPath);
        }

        Debug.Log(
            "BONDS_ANIMATION_AUDIT_COMPLETE typeIds=" + document.typeIdCount
            + " variants=" + document.variantCount
            + " signatures=" + document.signatures.Length
            + " diagnostics=" + document.diagnostics.Length
            + " output=" + outputPath);
    }

    private static HashSet<int> ReadTypeIds(string specificationPath)
    {
        if (!File.Exists(specificationPath))
        {
            throw new InvalidOperationException(
                "BONDS_ANIMATION_AUDIT_SPEC_MISSING path=" + specificationPath);
        }

        var specification = File.ReadAllText(specificationPath, StrictUtf8);
        var sections = TypeIdSectionPattern.Matches(specification);
        if (sections.Count != 2)
        {
            throw new InvalidOperationException(
                "BONDS_ANIMATION_AUDIT_SPEC_SECTION_COUNT expected=2 actual="
                + sections.Count);
        }

        var parsedTypeIds = sections
            .Cast<Match>()
            .SelectMany(section => DecimalPattern
                .Matches(section.Groups["ids"].Value)
                .Cast<Match>())
            .Select(match => int.Parse(match.Value, CultureInfo.InvariantCulture))
            .ToArray();
        var uniqueTypeIds = new HashSet<int>(parsedTypeIds);
        if (parsedTypeIds.Length != ExpectedTypeIdCount
            || uniqueTypeIds.Count != ExpectedTypeIdCount)
        {
            throw new InvalidOperationException(
                "BONDS_ANIMATION_AUDIT_TYPE_ID_COUNT expected="
                + ExpectedTypeIdCount
                + " parsed=" + parsedTypeIds.Length
                + " unique=" + uniqueTypeIds.Count);
        }

        return uniqueTypeIds;
    }

    private static BondsUnitAnimationAuditVariant[] ReadVariants(
        string repositoryRoot,
        HashSet<int> typeIds)
    {
        var characterRoot = Path.Combine(
            repositoryRoot,
            CharacterAssetRoot.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(characterRoot))
        {
            throw new InvalidOperationException(
                "BONDS_ANIMATION_AUDIT_CHARACTER_ROOT_MISSING path=" + characterRoot);
        }

        var unitKeys = Directory
            .GetDirectories(characterRoot, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(unitKey => TryReadTypeId(unitKey, out var typeId)
                && typeIds.Contains(typeId))
            .OrderBy(unitKey => unitKey, StringComparer.Ordinal)
            .ToArray();
        var uniqueUnitKeys = new HashSet<string>(unitKeys, StringComparer.Ordinal);
        if (unitKeys.Length != ExpectedVariantCount
            || uniqueUnitKeys.Count != ExpectedVariantCount)
        {
            throw new InvalidOperationException(
                "BONDS_ANIMATION_AUDIT_VARIANT_COUNT expected="
                + ExpectedVariantCount
                + " discovered=" + unitKeys.Length
                + " unique=" + uniqueUnitKeys.Count);
        }

        return unitKeys.Select(ReadVariant).ToArray();
    }

    private static BondsUnitAnimationAuditVariant ReadVariant(string unitKey)
    {
        if (!TryReadTypeId(unitKey, out var typeId))
        {
            throw new InvalidOperationException(
                "BONDS_ANIMATION_AUDIT_UNIT_KEY_INVALID unitKey=" + unitKey);
        }

        var resourcePath = CharacterResourceRoot + "/" + unitKey
            + "/enemy_" + unitKey + "_SkeletonData";
        var asset = Resources.Load<SkeletonDataAsset>(resourcePath);
        if (asset == null)
        {
            throw new InvalidOperationException(
                "BONDS_ANIMATION_AUDIT_SKELETON_MISSING unitKey="
                + unitKey + " resource=" + resourcePath);
        }

        Spine.SkeletonData skeletonData;
        try
        {
            skeletonData = asset.GetSkeletonData(true);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "BONDS_ANIMATION_AUDIT_SPINE_LOAD_FAILED unitKey="
                + unitKey + " resource=" + resourcePath,
                exception);
        }

        if (skeletonData == null)
        {
            throw new InvalidOperationException(
                "BONDS_ANIMATION_AUDIT_SKELETON_INVALID unitKey="
                + unitKey + " resource=" + resourcePath);
        }

        var animations = new List<BondsUnitAnimationAuditAnimation>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < skeletonData.Animations.Count; index++)
        {
            var animation = skeletonData.Animations.Items[index];
            if (animation == null)
            {
                continue;
            }

            var animationName = animation.Name;
            if (string.IsNullOrEmpty(animationName))
            {
                throw new InvalidOperationException(
                    "BONDS_ANIMATION_AUDIT_ANIMATION_NAME_EMPTY unitKey=" + unitKey);
            }

            if (!names.Add(animationName))
            {
                throw new InvalidOperationException(
                    "BONDS_ANIMATION_AUDIT_ANIMATION_NAME_DUPLICATE unitKey="
                    + unitKey + " animation=" + animationName);
            }

            var duration = animation.Duration;
            if (duration < 0f || float.IsNaN(duration) || float.IsInfinity(duration))
            {
                throw new InvalidOperationException(
                    "BONDS_ANIMATION_AUDIT_DURATION_INVALID unitKey="
                    + unitKey + " animation=" + animationName
                    + " duration=" + duration.ToString("R", CultureInfo.InvariantCulture));
            }

            animations.Add(new BondsUnitAnimationAuditAnimation
            {
                name = animationName,
                durationSeconds = duration
            });
        }

        animations.Sort((left, right) =>
            StringComparer.Ordinal.Compare(left.name, right.name));
        if (animations.Count == 0)
        {
            throw new InvalidOperationException(
                "BONDS_ANIMATION_AUDIT_ANIMATIONS_EMPTY unitKey=" + unitKey);
        }

        var tokenSummary = animations
            .SelectMany(animation => Tokens(animation.name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(token => token, StringComparer.Ordinal)
            .ToArray();

        return new BondsUnitAnimationAuditVariant
        {
            typeId = typeId,
            unitKey = unitKey,
            sourceUnitKey = SourceUnitKey(unitKey),
            skeletonDataResourcePath = resourcePath,
            animationCount = animations.Count,
            animations = animations.ToArray(),
            exactNameSignature = string.Join(
                "\u001f",
                animations.Select(animation => animation.name)),
            caseFoldedTokenSummary = tokenSummary
        };
    }

    private static BondsUnitAnimationAuditSignature[] BuildSignatures(
        BondsUnitAnimationAuditVariant[] variants)
    {
        return variants
            .GroupBy(variant => variant.exactNameSignature, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new BondsUnitAnimationAuditSignature
            {
                exactNameSignature = group.Key,
                unitKeys = group
                    .Select(variant => variant.unitKey)
                    .OrderBy(unitKey => unitKey, StringComparer.Ordinal)
                    .ToArray()
            })
            .ToArray();
    }

    private static BondsUnitAnimationAuditToken[] BuildTokenSummary(
        BondsUnitAnimationAuditVariant[] variants)
    {
        var variantCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var animationCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var variant in variants)
        {
            foreach (var token in variant.caseFoldedTokenSummary)
            {
                Increment(variantCounts, token);
            }

            foreach (var animation in variant.animations)
            {
                foreach (var token in Tokens(animation.name)
                             .Distinct(StringComparer.Ordinal))
                {
                    Increment(animationCounts, token);
                }
            }
        }

        return variantCounts.Keys
            .Union(animationCounts.Keys, StringComparer.Ordinal)
            .OrderBy(token => token, StringComparer.Ordinal)
            .Select(token => new BondsUnitAnimationAuditToken
            {
                token = token,
                variantCount = variantCounts.TryGetValue(token, out var variantCount)
                    ? variantCount
                    : 0,
                animationCount = animationCounts.TryGetValue(token, out var animationCount)
                    ? animationCount
                    : 0
            })
            .ToArray();
    }

    private static IEnumerable<string> Tokens(string animationName)
    {
        return AnimationTokenPattern
            .Matches(animationName)
            .Cast<Match>()
            .Select(match => match.Value.ToLower(CultureInfo.InvariantCulture));
    }

    private static void Increment(Dictionary<string, int> counts, string key)
    {
        counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
    }

    private static bool TryReadTypeId(string unitKey, out int typeId)
    {
        var match = UnitKeyPattern.Match(unitKey ?? string.Empty);
        if (!match.Success)
        {
            typeId = default;
            return false;
        }

        return int.TryParse(
            match.Groups["typeId"].Value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out typeId);
    }

    private static string SourceUnitKey(string unitKey)
    {
        return unitKey == "1322_wdgyht"
            ? "1322_wdgyht_2"
            : unitKey == "1322_wdgyht_2"
                ? "1322_wdgyht"
                : unitKey;
    }

    private static void ValidateRoundTrip(
        BondsUnitAnimationAuditDocument expected,
        BondsUnitAnimationAuditDocument actual)
    {
        if (actual == null
            || actual.schemaVersion != SchemaVersion
            || actual.typeIdCount != expected.typeIdCount
            || actual.variantCount != expected.variantCount
            || actual.variants == null
            || actual.signatures == null
            || actual.tokenSummary == null
            || actual.diagnostics == null
            || expected.variants == null
            || actual.variants.Length != expected.variants.Length)
        {
            throw new InvalidOperationException(
                "BONDS_ANIMATION_AUDIT_JSON_ROUNDTRIP_FAILED");
        }

        for (var variantIndex = 0; variantIndex < expected.variants.Length; variantIndex++)
        {
            var expectedVariant = expected.variants[variantIndex];
            var actualVariant = actual.variants[variantIndex];
            if (actualVariant == null
                || actualVariant.unitKey != expectedVariant.unitKey
                || actualVariant.animations == null
                || expectedVariant.animations == null
                || actualVariant.animations.Length != expectedVariant.animations.Length)
            {
                throw new InvalidOperationException(
                    "BONDS_ANIMATION_AUDIT_VARIANT_ROUNDTRIP_FAILED unitKey="
                    + expectedVariant.unitKey);
            }

            for (var animationIndex = 0;
                 animationIndex < expectedVariant.animations.Length;
                 animationIndex++)
            {
                var expectedAnimation = expectedVariant.animations[animationIndex];
                var actualAnimation = actualVariant.animations[animationIndex];
                if (actualAnimation == null
                    || actualAnimation.name != expectedAnimation.name
                    || FloatBits(expectedAnimation.durationSeconds)
                    != FloatBits(actualAnimation.durationSeconds))
                {
                    throw new InvalidOperationException(
                        "BONDS_ANIMATION_AUDIT_DURATION_ROUNDTRIP_FAILED unitKey="
                        + expectedVariant.unitKey
                        + " animation=" + expectedAnimation.name);
                }
            }
        }
    }

    private static int FloatBits(float value)
    {
        return BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
    }

    private static string ReadOutputPath(string repositoryRoot)
    {
        var arguments = Environment.GetCommandLineArgs();
        string requestedPath = null;
        for (var index = 0; index < arguments.Length; index++)
        {
            if (!string.Equals(
                    arguments[index],
                    "-bondsAnimationAuditOutput",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (requestedPath != null
                || index + 1 >= arguments.Length
                || string.IsNullOrWhiteSpace(arguments[index + 1]))
            {
                throw new InvalidOperationException(
                    "BONDS_ANIMATION_AUDIT_OUTPUT_ARGUMENT_INVALID");
            }

            requestedPath = arguments[++index];
        }

        if (requestedPath != null && !Path.IsPathRooted(requestedPath))
        {
            throw new InvalidOperationException(
                "BONDS_ANIMATION_AUDIT_OUTPUT_MUST_BE_ABSOLUTE path="
                + requestedPath);
        }

        var outputPath = Path.GetFullPath(
            requestedPath
            ?? Path.Combine(repositoryRoot, DefaultOutputRelativePath));
        var tempRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "Temp"));
        var tempPrefix = tempRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!outputPath.StartsWith(tempPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "BONDS_ANIMATION_AUDIT_OUTPUT_OUTSIDE_TEMP path=" + outputPath);
        }

        return outputPath;
    }

    private static string RepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }
}

[Serializable]
internal sealed class BondsUnitAnimationAuditDocument
{
    public string schemaVersion;
    public string generatedAtUtc;
    public int typeIdCount;
    public int variantCount;
    public BondsUnitAnimationAuditVariant[] variants;
    public BondsUnitAnimationAuditSignature[] signatures;
    public BondsUnitAnimationAuditToken[] tokenSummary;
    public string[] diagnostics;
}

[Serializable]
internal sealed class BondsUnitAnimationAuditVariant
{
    public int typeId;
    public string unitKey;
    public string sourceUnitKey;
    public string skeletonDataResourcePath;
    public int animationCount;
    public BondsUnitAnimationAuditAnimation[] animations;
    public string exactNameSignature;
    public string[] caseFoldedTokenSummary;
}

[Serializable]
internal sealed class BondsUnitAnimationAuditAnimation
{
    public string name;
    public float durationSeconds;
}

[Serializable]
internal sealed class BondsUnitAnimationAuditSignature
{
    public string exactNameSignature;
    public string[] unitKeys;
}

[Serializable]
internal sealed class BondsUnitAnimationAuditToken
{
    public string token;
    public int variantCount;
    public int animationCount;
}
