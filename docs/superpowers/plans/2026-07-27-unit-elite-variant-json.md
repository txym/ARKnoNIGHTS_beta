# Unit Elite Variant JSON and Elite-Zero Loading Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Store sparse elite variants for unit `1000_gopro` and make the existing catalog generator resolve elite level 0 without changing the Player-side catalog schema.

**Architecture:** Keep `unit-source-v1` as the legacy/common source and add an optional `unit-elite-variants-v1` sidecar keyed by `typeId`. An Editor-only resolver applies sparse patches with atomic `combat`, `shared`, and `model` blocks; `UnitCatalogGenerator` currently requests only elite level 0 and then continues through its existing validation, attack-interval conversion, Spine verification, and flat `unit-catalog-v1` output. Unit portraits remain raw `Texture2D` resources; a shared cached `UnitPortraitLoader` adapts them to Sprite only at UGUI `Image` consumers.

**Tech Stack:** Unity 2022.3.62f1, C#, Unity `JsonUtility`, Unity Editor APIs, Spine-Unity, NUnit EditMode/PlayMode tests, PowerShell test runner.

## Global Constraints

- Work directly in `G:\ARKnoNIGHTS_beta`; do not create a worktree.
- Preserve and do not stage the user's existing changes to `Assets/GameData/Units/Json/1000_gopro.json`, `Assets/Resources/Characters/1000_gopro*`, `Assets/Resources/ProfilePicture/UIImage_1000_gopro*`, `.superpowers/`, or `docs/bonds/`.
- Use `unit-elite-variants-v1` only for the new sidecar contract; do not change `unit-source-v1` or Player-side `unit-catalog-v1` schema versions.
- Every variant uses its source `unit-levels.json` level 0 values; no synthesis coefficient is stored or calculated.
- `stats.combat` and `stats.shared` are independent atomic override blocks. An omitted block inherits the nearest lower elite result.
- `model` is an atomic block: omit the entire block to inherit, or provide every model field.
- An omitted `innateAbilityIds` inherits; an explicit empty array clears the list.
- Missing elite 2 or elite 3 entries inherit the nearest lower entry and never borrow a higher entry.
- Current generation resolves elite level 0 only. Do not connect elite 2/3 selection to battle, UI, presentation, or Player runtime data in this task.
- All unit portraits under `Assets/Resources/ProfilePicture` are raw `Texture2D`; portrait code must not try `Resources.Load<Sprite>` first.
- Keep UGUI `Image` consumers and convert each portrait Texture2D to one cached runtime Sprite through `UnitPortraitLoader`.
- Do not modify portrait `.meta` files, Spine textures, general UI textures, atlases, or mixed-format non-portrait artwork.
- Preserve the existing base attack interval rule: `ceil(attackIntervalSeconds × 0.5 × 20)`.
- Do not add or upgrade Unity packages, Editor versions, render pipelines, input systems, or external dependencies.
- If Unity is already using this project, stop the batch invocation rather than opening the same project in a second Editor process.

## File Structure

- Create `Assets/GameData/Units/EliteVariants/Json/1000_gopro.json`: authoritative sparse variant sidecar for type 1000.
- Create `Assets/Game/Editor/Battle/UnitEliteVariantResolver.cs`: Editor-only DTO parsing, validation, inheritance, and patch application.
- Create `Assets/Game/UI/FormalHud/UnitPortraitLoader.cs`: Texture2D-only unit portrait loading and Sprite caching for UGUI.
- Create `Assets/Game/Tests/EditMode/Battle/UnitEliteVariantSourceEditModeTests.cs`: reflection-based resolver tests that do not require an assembly-definition migration.
- Modify `Assets/Game/Editor/Battle/UnitCatalogGenerator.cs`: discover sidecars by `typeId`, resolve elite 0, and feed the resolved `UnitJson` into the existing conversion.
- Modify `Assets/Game/Battle/Infrastructure/RealBattleDataLoader.cs`: validate portrait paths as Texture2D resources.
- Modify `Assets/Game/UI/FormalHud/StagingHudController.cs`: load portraits through `UnitPortraitLoader`.
- Modify `Assets/Game/UI/FormalHud/ShopReady/ShopReadyHudController.cs`: use the portrait-only loader for shop unit portraits while retaining `FormalHudSpriteLoader` for mixed UI art.
- Modify `Assets/Game/Runtime/Initial/FormalBattleHudController.cs`: load all unit portraits through `UnitPortraitLoader`.
- Modify `Assets/Game/Debug/ButtonDebug.cs`: keep the legacy portrait debug entry compatible with Texture2D portraits.
- Modify `Assets/Game/Tests/EditMode/Battle/BattleCoreEditModeTests.cs`: assert the generated type 1000 entry uses elite-zero data.
- Modify `Assets/Game/Tests/EditMode/Battle/StagingHudLayoutEditModeTests.cs`: assert Texture2D conversion and cache identity through the real portrait loader.
- Regenerate `Assets/Resources/BattleData/unit-catalog-v1.json`: keep the schema flat while changing type 1000 to the resolved elite-zero values and resources.
- Modify `docs/SPEC.md`: record the confirmed source, inheritance, level-zero, and current-runtime rules.
- Modify `docs/ARCHITECTURE.md`: document the Editor-side resolver between source JSON and catalog generation.
- Modify `docs/TEST_PLAN.md`: record commands, result counts, logs, and unverified items.
- Include Unity-generated `.meta` files for new project assets, but never hand-author or reuse GUIDs.

---

### Task 1: Add the elite-variant source contract and resolver

**Files:**
- Create: `Assets/GameData/Units/EliteVariants/Json/1000_gopro.json`
- Create: `Assets/Game/Editor/Battle/UnitEliteVariantResolver.cs`
- Test: `Assets/Game/Tests/EditMode/Battle/UnitEliteVariantSourceEditModeTests.cs`
- Create through Unity import: corresponding `.meta` files and new directory `.meta` files

**Interfaces:**
- Consumes: existing `UnitJson` fields from `Assets/GameData/Units/UnitJson.cs`
- Produces: `UnitEliteVariantResolver.Resolve(string unitSourceJson, string variantSourceJson, int eliteLevel, string context) : UnitJson`
- Produces: `UnitEliteVariantResolver.LoadDirectory(string directory) : IReadOnlyDictionary<int, UnitEliteVariantSource>`
- Produces: `UnitEliteVariantSource.TypeId`, `.Path`, and `.Json`

- [ ] **Step 1: Add the authoritative type 1000 sidecar**

Create `Assets/GameData/Units/EliteVariants/Json/1000_gopro.json` with this exact logical content:

```json
{
  "schemaVersion": "unit-elite-variants-v1",
  "typeId": 1000,
  "variants": [
    {
      "minEliteLevel": 0,
      "sourceVariant": "1000_gopro",
      "statsLevel": 0,
      "displayNameZhHans": "猎狗",
      "stats": {
        "combat": {
          "maxHitPoints": 820,
          "attack": 190,
          "defense": 0,
          "magicResistance": 20
        },
        "shared": {
          "moveSpeedMetresPerSecond": 1.9,
          "attackIntervalSeconds": 1.4,
          "lifeDeduct": 1
        }
      },
      "model": {
        "resourceFolderName": "1000_gopro",
        "resourceKey": "gopro",
        "skeletonDataResourceName": "enemy_1000_gopro_SkeletonData",
        "profilePictureResourceName": "UIImage_1000_gopro",
        "unitSkeletonType": 1,
        "attackAnimationDurationSeconds": 1.0,
        "moveAnimation": "Run_Loop",
        "attackAnimation": "Attack",
        "hitAnimation": "",
        "deathAnimation": "Die"
      },
      "innateAbilityIds": []
    },
    {
      "minEliteLevel": 2,
      "sourceVariant": "1000_gopro_2",
      "statsLevel": 0,
      "displayNameZhHans": "猎狗pro",
      "stats": {
        "combat": {
          "maxHitPoints": 1700,
          "attack": 260,
          "defense": 0,
          "magicResistance": 20
        }
      },
      "model": {
        "resourceFolderName": "1000_gopro_2",
        "resourceKey": "gopro_2",
        "skeletonDataResourceName": "enemy_1000_gopro_2_SkeletonData",
        "profilePictureResourceName": "UIImage_1000_gopro_2",
        "unitSkeletonType": 1,
        "attackAnimationDurationSeconds": 1.0,
        "moveAnimation": "Run_Loop",
        "attackAnimation": "Attack",
        "hitAnimation": "",
        "deathAnimation": "Die"
      }
    },
    {
      "minEliteLevel": 3,
      "sourceVariant": "1000_gopro_3",
      "statsLevel": 0,
      "displayNameZhHans": "狂暴的猎狗pro",
      "stats": {
        "combat": {
          "maxHitPoints": 3000,
          "attack": 370,
          "defense": 0,
          "magicResistance": 20
        }
      },
      "model": {
        "resourceFolderName": "1000_gopro_3",
        "resourceKey": "gopro_3",
        "skeletonDataResourceName": "enemy_1000_gopro_3_SkeletonData",
        "profilePictureResourceName": "UIImage_1000_gopro_3",
        "unitSkeletonType": 1,
        "attackAnimationDurationSeconds": 1.0,
        "moveAnimation": "Run_Loop",
        "attackAnimation": "Attack",
        "hitAnimation": "",
        "deathAnimation": "Die"
      }
    }
  ]
}
```

Do not copy absolute staging paths, URLs, hashes, or external manifests into the runtime contract.

- [ ] **Step 2: Write resolver tests before the resolver exists**

Create `Assets/Game/Tests/EditMode/Battle/UnitEliteVariantSourceEditModeTests.cs`. The test uses reflection because the resolver compiles into `Assembly-CSharp-Editor`, while the existing tests live in an asmdef:

```csharp
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ArknoNights.Battle.Tests
{
    public sealed class UnitEliteVariantSourceEditModeTests
    {
        private static string SourcePath =>
            Path.Combine(Application.dataPath, "GameData/Units/Json/1000_gopro.json");

        private static string VariantPath =>
            Path.Combine(Application.dataPath, "GameData/Units/EliteVariants/Json/1000_gopro.json");

        [TestCase(0, "猎狗", 820, 190, "gopro", "enemy_1000_gopro_SkeletonData")]
        [TestCase(1, "猎狗", 820, 190, "gopro", "enemy_1000_gopro_SkeletonData")]
        [TestCase(2, "猎狗pro", 1700, 260, "gopro_2", "enemy_1000_gopro_2_SkeletonData")]
        [TestCase(3, "狂暴的猎狗pro", 3000, 370, "gopro_3", "enemy_1000_gopro_3_SkeletonData")]
        public void Resolve_AppliesSparseVariantsAndLowerLevelInheritance(
            int eliteLevel,
            string expectedName,
            int expectedHitPoints,
            int expectedAttack,
            string expectedResourceKey,
            string expectedSkeleton)
        {
            var resolved = Resolve(
                File.ReadAllText(SourcePath),
                File.ReadAllText(VariantPath),
                eliteLevel);

            Assert.That(Field<string>(resolved, "displayNameZhHans"), Is.EqualTo(expectedName));
            Assert.That(Field<int>(resolved, "maxHitPoints"), Is.EqualTo(expectedHitPoints));
            Assert.That(Field<int>(resolved, "attack"), Is.EqualTo(expectedAttack));
            Assert.That(Field<int>(resolved, "defense"), Is.EqualTo(0));
            Assert.That(Field<int>(resolved, "magicResistance"), Is.EqualTo(20));
            Assert.That(Field<float>(resolved, "moveSpeedMetresPerSecond"), Is.EqualTo(1.9f));
            Assert.That(Field<float>(resolved, "attackIntervalSeconds"), Is.EqualTo(1.4f));
            Assert.That(Field<int>(resolved, "lifeDeduct"), Is.EqualTo(1));
            Assert.That(Field<string>(resolved, "resourceKey"), Is.EqualTo(expectedResourceKey));
            Assert.That(Field<string>(resolved, "skeletonDataResourceName"), Is.EqualTo(expectedSkeleton));
        }

        [Test]
        public void Resolve_RejectsPartialModelBlocks()
        {
            var invalid = File.ReadAllText(VariantPath).Replace(
                "\"profilePictureResourceName\": \"UIImage_1000_gopro_2\"",
                "\"profilePictureResourceName\": \"\"");

            var exception = Assert.Throws<TargetInvocationException>(() =>
                Resolve(File.ReadAllText(SourcePath), invalid, 2));

            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
            StringAssert.Contains("UNIT_ELITE_VARIANT_MODEL_INCOMPLETE", exception.InnerException.Message);
        }

        private static object Resolve(string sourceJson, string variantsJson, int eliteLevel)
        {
            var resolver = Type.GetType("UnitEliteVariantResolver, Assembly-CSharp-Editor");
            Assert.That(resolver, Is.Not.Null, "Editor resolver type must exist.");
            var method = resolver.GetMethod("Resolve", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Resolve method must exist.");
            return method.Invoke(null, new object[] { sourceJson, variantsJson, eliteLevel, "test" });
        }

        private static T Field<T>(object source, string name)
        {
            var field = source.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, "Missing resolved field: " + name);
            return (T)field.GetValue(source);
        }
    }
}
```

- [ ] **Step 3: Run the focused test and verify the intended failure**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests.UnitEliteVariantSourceEditModeTests' `
  -OutputDirectory 'Temp/UnitEliteVariants/TDD-Resolver-Red' `
  -NoGraphics
```

Expected: non-zero exit with both tests failing because
`UnitEliteVariantResolver, Assembly-CSharp-Editor` does not exist. If Unity reports
zero tests, a compile error, project-in-use error, or missing XML, treat the run as
unverified rather than as the expected red test.

- [ ] **Step 4: Implement the Editor-only resolver**

Create `Assets/Game/Editor/Battle/UnitEliteVariantResolver.cs` with these exact
serialized boundaries and method signatures:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

internal sealed class UnitEliteVariantSource
{
    internal UnitEliteVariantSource(int typeId, string path, string json)
    {
        TypeId = typeId;
        Path = path;
        Json = json;
    }

    internal int TypeId { get; }
    internal string Path { get; }
    internal string Json { get; }
}

internal static class UnitEliteVariantResolver
{
    private const string SchemaVersion = "unit-elite-variants-v1";

    private static UnitEliteVariantsDocument ParseDocument(string json, string context)
    {
        UnitEliteVariantsDocument document;
        try { document = JsonUtility.FromJson<UnitEliteVariantsDocument>(json); }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_JSON_INVALID context=" + context,
                exception);
        }
        ValidateDocument(document, context);
        return document;
    }

    private static void ValidateDocument(
        UnitEliteVariantsDocument document,
        string context)
    {
        if (document == null
            || !string.Equals(document.schemaVersion, SchemaVersion, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_SCHEMA_INVALID context=" + context);
        if (document.typeId <= 0
            || document.variants == null
            || document.variants.Length == 0)
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_REQUIRED_MISSING context=" + context);

        var ordered = document.variants
            .OrderBy(item => item == null ? int.MinValue : item.minEliteLevel)
            .ToArray();
        if (ordered[0] == null || ordered[0].minEliteLevel != 0)
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_REQUIRED_MISSING context=" + context);

        var levels = new HashSet<int>();
        foreach (var variant in ordered)
        {
            if (variant == null
                || variant.minEliteLevel < 0
                || variant.minEliteLevel > 3)
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_LEVEL_INVALID context=" + context);
            if (!levels.Add(variant.minEliteLevel))
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_LEVEL_DUPLICATE context=" + context
                    + " eliteLevel=" + variant.minEliteLevel);
            if (variant.statsLevel != 0)
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_STATS_LEVEL_INVALID context=" + context
                    + " eliteLevel=" + variant.minEliteLevel);
            if (string.IsNullOrWhiteSpace(variant.sourceVariant))
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_REQUIRED_MISSING context=" + context
                    + " eliteLevel=" + variant.minEliteLevel);

            ValidateStats(variant.stats, context, variant.minEliteLevel);
            ValidateModel(
                document.typeId,
                variant.sourceVariant,
                variant.model,
                context,
                variant.minEliteLevel);

            if (variant.minEliteLevel == 0
                && (variant.displayNameZhHans == null
                    || variant.stats == null
                    || variant.stats.combat == null
                    || variant.stats.shared == null
                    || variant.model == null
                    || variant.innateAbilityIds == null))
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_BASE_INCOMPLETE context=" + context);
        }
    }

    private static void ValidateStats(
        UnitEliteVariantStats stats,
        string context,
        int eliteLevel)
    {
        if (stats == null) return;
        if (stats.combat != null
            && (stats.combat.maxHitPoints <= 0
                || stats.combat.attack < 0
                || stats.combat.defense < 0
                || stats.combat.magicResistance < 0
                || stats.combat.magicResistance > 100))
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_COMBAT_INVALID context=" + context
                + " eliteLevel=" + eliteLevel);
        if (stats.shared != null
            && (stats.shared.moveSpeedMetresPerSecond <= 0f
                || stats.shared.attackIntervalSeconds <= 0f
                || stats.shared.lifeDeduct < 0))
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_SHARED_INVALID context=" + context
                + " eliteLevel=" + eliteLevel);
    }

    private static void ValidateModel(
        int typeId,
        string sourceVariant,
        UnitEliteVariantModel model,
        string context,
        int eliteLevel)
    {
        if (model == null) return;
        if (string.IsNullOrWhiteSpace(model.resourceFolderName)
            || string.IsNullOrWhiteSpace(model.resourceKey)
            || string.IsNullOrWhiteSpace(model.skeletonDataResourceName)
            || string.IsNullOrWhiteSpace(model.profilePictureResourceName)
            || model.unitSkeletonType <= 0
            || model.attackAnimationDurationSeconds <= 0f
            || string.IsNullOrWhiteSpace(model.moveAnimation)
            || string.IsNullOrWhiteSpace(model.attackAnimation)
            || model.hitAnimation == null
            || string.IsNullOrWhiteSpace(model.deathAnimation))
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_MODEL_INCOMPLETE context=" + context
                + " eliteLevel=" + eliteLevel);

        var expectedFolder =
            UnitResourcePaths.BuildCharacterFolderName(typeId, model.resourceKey);
        var expectedPortrait =
            UnitResourcePaths.BuildProfilePictureResourceName(typeId, model.resourceKey);
        if (!string.Equals(
                model.resourceFolderName,
                expectedFolder,
                StringComparison.Ordinal)
            || !string.Equals(
                model.profilePictureResourceName,
                expectedPortrait,
                StringComparison.Ordinal)
            || !string.Equals(
                sourceVariant,
                model.resourceFolderName,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_MODEL_CANONICAL_NAME_INVALID context=" + context
                + " eliteLevel=" + eliteLevel);
    }

    internal static IReadOnlyDictionary<int, UnitEliteVariantSource> LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return new Dictionary<int, UnitEliteVariantSource>();

        var result = new Dictionary<int, UnitEliteVariantSource>();
        foreach (var path in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(item => Path.GetFileName(item), StringComparer.Ordinal))
        {
            var json = File.ReadAllText(path);
            var document = ParseDocument(json, path);
            if (result.ContainsKey(document.typeId))
                throw new InvalidOperationException(
                    "UNIT_ELITE_VARIANT_TYPEID_DUPLICATE typeId=" + document.typeId);
            result.Add(document.typeId, new UnitEliteVariantSource(document.typeId, path, json));
        }
        return result;
    }

    internal static UnitJson Resolve(
        string unitSourceJson,
        string variantSourceJson,
        int eliteLevel,
        string context)
    {
        if (eliteLevel < 0 || eliteLevel > 3)
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_TARGET_INVALID context=" + context + " eliteLevel=" + eliteLevel);

        var source = JsonUtility.FromJson<UnitJson>(unitSourceJson);
        if (source == null)
            throw new InvalidOperationException("UNIT_ELITE_VARIANT_BASE_INVALID context=" + context);

        var document = ParseDocument(variantSourceJson, context);
        if (document.typeId != source.typeId)
            throw new InvalidOperationException(
                "UNIT_ELITE_VARIANT_TYPEID_MISMATCH context=" + context);

        foreach (var variant in document.variants
                     .OrderBy(item => item.minEliteLevel)
                     .Where(item => item.minEliteLevel <= eliteLevel))
        {
            Apply(source, variant);
        }
        return source;
    }
}
```

Add `[Serializable]` DTOs for:

```csharp
[Serializable]
internal sealed class UnitEliteVariantsDocument
{
    public string schemaVersion;
    public int typeId;
    public UnitEliteVariantPatch[] variants;
}

[Serializable]
internal sealed class UnitEliteVariantPatch
{
    public int minEliteLevel;
    public string sourceVariant;
    public int statsLevel;
    public string displayNameZhHans;
    public UnitEliteVariantStats stats;
    public UnitEliteVariantModel model;
    public List<string> innateAbilityIds;
}

[Serializable]
internal sealed class UnitEliteVariantStats
{
    public UnitEliteVariantCombatStats combat;
    public UnitEliteVariantSharedStats shared;
}

[Serializable]
internal sealed class UnitEliteVariantCombatStats
{
    public int maxHitPoints;
    public int attack;
    public int defense;
    public int magicResistance;
}

[Serializable]
internal sealed class UnitEliteVariantSharedStats
{
    public float moveSpeedMetresPerSecond;
    public float attackIntervalSeconds;
    public int lifeDeduct;
}

[Serializable]
internal sealed class UnitEliteVariantModel
{
    public string resourceFolderName;
    public string resourceKey;
    public string skeletonDataResourceName;
    public string profilePictureResourceName;
    public int unitSkeletonType;
    public float attackAnimationDurationSeconds;
    public string moveAnimation;
    public string attackAnimation;
    public string hitAnimation;
    public string deathAnimation;
}
```

`ParseDocument` must reject these exact conditions:

- schema is not `unit-elite-variants-v1`:
  `UNIT_ELITE_VARIANT_SCHEMA_INVALID`
- non-positive `typeId`, null/empty variants, first sorted level not 0:
  `UNIT_ELITE_VARIANT_REQUIRED_MISSING`
- elite level outside 0..3 or duplicate elite level:
  `UNIT_ELITE_VARIANT_LEVEL_INVALID` /
  `UNIT_ELITE_VARIANT_LEVEL_DUPLICATE`
- `statsLevel != 0`:
  `UNIT_ELITE_VARIANT_STATS_LEVEL_INVALID`
- missing `sourceVariant`, or missing elite-zero name, combat, shared, model, or
  ability array:
  `UNIT_ELITE_VARIANT_BASE_INCOMPLETE`
- declared combat block with non-positive HP, negative attack/defense, or resistance
  outside 0..100:
  `UNIT_ELITE_VARIANT_COMBAT_INVALID`
- declared shared block with non-positive speed/interval or negative life deduction:
  `UNIT_ELITE_VARIANT_SHARED_INVALID`
- declared model block missing resource folder/key/skeleton/portrait/move/attack/death,
  non-positive skeleton type, or non-positive attack duration:
  `UNIT_ELITE_VARIANT_MODEL_INCOMPLETE`
- model folder not equal to
  `UnitResourcePaths.BuildCharacterFolderName(document.typeId, model.resourceKey)`,
  portrait not equal to
  `UnitResourcePaths.BuildProfilePictureResourceName(document.typeId, model.resourceKey)`,
  or `sourceVariant` not equal to the model folder:
  `UNIT_ELITE_VARIANT_MODEL_CANONICAL_NAME_INVALID`

`Apply` must perform only these assignments:

```csharp
private static void Apply(UnitJson source, UnitEliteVariantPatch variant)
{
    if (variant.displayNameZhHans != null)
        source.displayNameZhHans = variant.displayNameZhHans;

    if (variant.stats != null && variant.stats.combat != null)
    {
        source.maxHitPoints = variant.stats.combat.maxHitPoints;
        source.attack = variant.stats.combat.attack;
        source.defense = variant.stats.combat.defense;
        source.magicResistance = variant.stats.combat.magicResistance;
    }

    if (variant.stats != null && variant.stats.shared != null)
    {
        source.moveSpeedMetresPerSecond = variant.stats.shared.moveSpeedMetresPerSecond;
        source.attackIntervalSeconds = variant.stats.shared.attackIntervalSeconds;
        source.lifeDeduct = variant.stats.shared.lifeDeduct;
    }

    if (variant.model != null)
    {
        source.resourceKey = variant.model.resourceKey;
        source.skeletonDataResourceName = variant.model.skeletonDataResourceName;
        source.profilePictureResourceName = variant.model.profilePictureResourceName;
        source.unitSkeletonType = variant.model.unitSkeletonType;
        source.attackAnimationDurationSeconds = variant.model.attackAnimationDurationSeconds;
        source.moveAnimation = variant.model.moveAnimation;
        source.attackAnimation = variant.model.attackAnimation;
        source.hitAnimation = variant.model.hitAnimation;
        source.deathAnimation = variant.model.deathAnimation;
    }

    if (variant.innateAbilityIds != null)
        source.innateAbilityIds = new List<string>(variant.innateAbilityIds);
}
```

- [ ] **Step 5: Run the resolver tests and verify they pass**

Run the Step 3 command again with output directory
`Temp/UnitEliteVariants/TDD-Resolver-Green`.

Expected: result `Passed`, total `5`, failed `0`, skipped `0`. The four test cases
count as four NUnit cases, plus the invalid-model test.

- [ ] **Step 6: Inspect Unity-generated metadata**

Run:

```powershell
git status --short -- `
  Assets/GameData/Units/EliteVariants `
  Assets/Game/Editor/Battle/UnitEliteVariantResolver.cs `
  Assets/Game/Tests/EditMode/Battle/UnitEliteVariantSourceEditModeTests.cs
```

Expected: the three authored files plus their Unity-generated `.meta` files and
the two new directory `.meta` files. Verify every new GUID is unique with:

```powershell
rg -n "^guid:" Assets/GameData/Units/EliteVariants Assets/Game/Editor/Battle/UnitEliteVariantResolver.cs.meta Assets/Game/Tests/EditMode/Battle/UnitEliteVariantSourceEditModeTests.cs.meta
```

- [ ] **Step 7: Commit only the source contract, resolver, tests, and their metadata**

```powershell
git add -- `
  Assets/GameData/Units/EliteVariants `
  Assets/GameData/Units/EliteVariants.meta `
  Assets/Game/Editor/Battle/UnitEliteVariantResolver.cs `
  Assets/Game/Editor/Battle/UnitEliteVariantResolver.cs.meta `
  Assets/Game/Tests/EditMode/Battle/UnitEliteVariantSourceEditModeTests.cs `
  Assets/Game/Tests/EditMode/Battle/UnitEliteVariantSourceEditModeTests.cs.meta
git diff --cached --check
git commit -m "feat: add elite unit variant sources"
```

Before committing, `git diff --cached --name-only` must not list any existing
`1000_gopro` model, portrait, or base source files.

---

### Task 2: Resolve elite zero and load raw Texture2D portraits

**Files:**
- Modify: `Assets/Game/Editor/Battle/UnitCatalogGenerator.cs:15-67`
- Create: `Assets/Game/UI/FormalHud/UnitPortraitLoader.cs`
- Modify: `Assets/Game/Battle/Infrastructure/RealBattleDataLoader.cs:159-168`
- Modify: `Assets/Game/UI/FormalHud/StagingHudController.cs:235-245`
- Modify: `Assets/Game/UI/FormalHud/ShopReady/ShopReadyHudController.cs:387-391`
- Modify: `Assets/Game/Runtime/Initial/FormalBattleHudController.cs:369-420`
- Modify: `Assets/Game/Debug/ButtonDebug.cs:47`
- Modify: `Assets/Game/Tests/EditMode/Battle/BattleCoreEditModeTests.cs:540-605`
- Modify: `Assets/Game/Tests/EditMode/Battle/StagingHudLayoutEditModeTests.cs`
- Modify: `Assets/Resources/BattleData/unit-catalog-v1.json:5-36`

**Interfaces:**
- Consumes: `UnitEliteVariantResolver.LoadDirectory(...)`
- Consumes: `UnitEliteVariantResolver.Resolve(..., eliteLevel: 0, ...)`
- Produces: `ArknoNights.UI.UnitPortraitLoader.Load(string resourcePath) : Sprite`
- Produces: the unchanged `unit-catalog-v1` shape with type 1000 resolved from elite zero

- [ ] **Step 1: Write a failing Texture2D portrait adapter test**

Add these imports to `StagingHudLayoutEditModeTests.cs`:

```csharp
using System;
using System.Reflection;
using UnityEngine;
```

Add the test:

```csharp
[Test]
public void UnitPortraitLoader_LoadsTexture2DAsOneCachedSprite()
{
    const string path = "ProfilePicture/UIImage_1000_gopro";
    var texture = Resources.Load<Texture2D>(path);
    Assert.That(texture, Is.Not.Null, "Portrait must remain a raw Texture2D.");

    var loader = Type.GetType("ArknoNights.UI.UnitPortraitLoader, ARKnoNIGHTS.UI");
    Assert.That(loader, Is.Not.Null, "Texture2D-only portrait loader must exist.");
    var load = loader.GetMethod("Load", BindingFlags.Public | BindingFlags.Static);
    Assert.That(load, Is.Not.Null);

    var first = (Sprite)load.Invoke(null, new object[] { path });
    var second = (Sprite)load.Invoke(null, new object[] { path });
    Assert.That(first, Is.Not.Null);
    Assert.That(first.texture, Is.SameAs(texture));
    Assert.That(second, Is.SameAs(first));
}
```

This test catches a missing adapter, loading the wrong Resources type, copying the
portrait into a different texture, or recreating a Sprite on every UI refresh.

- [ ] **Step 2: Run the portrait test and verify the intended failure**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests.StagingHudLayoutEditModeTests.UnitPortraitLoader_LoadsTexture2DAsOneCachedSprite' `
  -OutputDirectory 'Temp/UnitEliteVariants/TDD-Portrait-Red' `
  -NoGraphics
```

Expected: one failed test with `Texture2D-only portrait loader must exist`; the raw
Texture2D assertion must already pass.

- [ ] **Step 3: Implement the portrait-only loader and route all unit portrait consumers**

Create `Assets/Game/UI/FormalHud/UnitPortraitLoader.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArknoNights.UI
{
    public static class UnitPortraitLoader
    {
        private static readonly Dictionary<string, Sprite> Cache =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);

        public static Sprite Load(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath)) return null;
            if (Cache.TryGetValue(resourcePath, out var cached)) return cached;

            var texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null)
            {
                Cache[resourcePath] = null;
                return null;
            }

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(.5f, .5f),
                100f);
            sprite.name = texture.name;
            Cache[resourcePath] = sprite;
            return sprite;
        }
    }
}
```

Replace only unit portrait loads:

```csharp
// StagingHudController.LoadPortrait
var portrait = UnitPortraitLoader.Load(stack.PortraitResourcePath);

// ShopReadyHudController.RefreshShop
widget.Portrait.sprite =
    slot.IsEmpty ? null : UnitPortraitLoader.Load(slot.PortraitResourcePath);

// FormalBattleHudController: selected entry, detail, and visual fixture
portrait.sprite = UnitPortraitLoader.Load(entry.PortraitResourcePath);
portrait.sprite = UnitPortraitLoader.Load(detail.PortraitResourcePath);

// ButtonDebug legacy portrait assignment
image.sprite = ArknoNights.UI.UnitPortraitLoader.Load(
    loadpath + UnitFactory.GetUnitBasicValueSO(
        units[i].GetComponent<UnitIdentity>().UnitTypeID).ProfilePicture);
```

Do not alter `FormalHudSpriteLoader`; it continues serving mixed-format non-portrait
HUD artwork.

Change portrait existence validation at both data boundaries:

```csharp
// UnitCatalogGenerator
if (Resources.Load<Texture2D>(portraitResourcePath) == null)
    throw new InvalidOperationException(
        "TASK004A_CATALOG_PORTRAIT_MISSING path=" + sourcePath
        + " resource=" + portraitResourcePath);

// RealBattleDataLoader
else if (Resources.Load<Texture2D>(dto.portraitResourcePath) == null)
{
    errors.Add(Error(
        "catalog.portrait.resource.missing",
        schemaVersion,
        catalogId,
        null,
        dto.typeId));
    valid = false;
}
```

Neither boundary may call `Resources.Load<Sprite>` for portraits.

- [ ] **Step 4: Run the portrait test and verify it passes**

Run the Step 2 command with output directory
`Temp/UnitEliteVariants/TDD-Portrait-Green`.

Expected: result `Passed`, total `1`, failed `0`, skipped `0`.

- [ ] **Step 5: Change the catalog regression test to require elite-zero output**

In `RealCatalog_ParsesSourceValuesAndLoadsDeterministicallyFromResources`, add
or update the type 1000 assertions:

```csharp
Assert.That(gopro.DisplayNameZhHans, Is.EqualTo("猎狗"));
Assert.That(gopro.Definition.MaxHitPoints, Is.EqualTo(820));
Assert.That(gopro.Definition.Attack, Is.EqualTo(190));
Assert.That(gopro.Definition.Defense, Is.EqualTo(0));
Assert.That(gopro.Definition.MagicResistance, Is.EqualTo(20));
Assert.That(gopro.Definition.MoveSpeedCentimetresPerSecond, Is.EqualTo(190));
Assert.That(gopro.Definition.AttackIntervalTicks, Is.EqualTo(14));
Assert.That(gopro.Definition.AttackAnimationDurationTicks, Is.EqualTo(20));
Assert.That(gopro.SkeletonDataResourcePath,
    Is.EqualTo("Characters/1000_gopro/enemy_1000_gopro_SkeletonData"));
Assert.That(gopro.PortraitResourcePath,
    Is.EqualTo("ProfilePicture/UIImage_1000_gopro"));
Assert.That(gopro.Definition.InnateAbilityIds, Is.Empty);
```

Replace the old raw-source display-name assertion with sidecar assertions:

```csharp
var eliteGopro = File.ReadAllText(Path.Combine(
    Application.dataPath,
    "GameData/Units/EliteVariants/Json/1000_gopro.json"));
StringAssert.Contains("\"schemaVersion\": \"unit-elite-variants-v1\"", eliteGopro);
StringAssert.Contains("\"minEliteLevel\": 0", eliteGopro);
StringAssert.Contains("\"statsLevel\": 0", eliteGopro);
StringAssert.Contains("\"displayNameZhHans\": \"猎狗\"", eliteGopro);
StringAssert.Contains("\"resourceFolderName\": \"1000_gopro_2\"", eliteGopro);
StringAssert.Contains("\"resourceFolderName\": \"1000_gopro_3\"", eliteGopro);
```

Keep the existing assertions for type 5503 and type 5504 unchanged.

- [ ] **Step 6: Run the catalog regression and verify it is red**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests.BattleCoreEditModeTests.RealCatalog_ParsesSourceValuesAndLoadsDeterministicallyFromResources' `
  -OutputDirectory 'Temp/UnitEliteVariants/TDD-Catalog-Red' `
  -NoGraphics
```

Expected: one failed test because the checked-in catalog still points at the previous
`enemy_1000_gopro_3_SkeletonData` location. With the imported resource rearrangement,
the runtime catalog validator may fail on that stale skeleton path before reaching
the HP 3000/attack 370 assertions; both failures are corrected only by regenerating
the elite-zero entry.

- [ ] **Step 7: Integrate the resolver into `UnitCatalogGenerator`**

Add the sidecar directory constant:

```csharp
private const string EliteVariantSourceDirectory =
    "Assets/GameData/Units/EliteVariants/Json";
```

At the start of `Generate`, load all sidecars exactly once:

```csharp
var eliteVariantsByTypeId =
    UnitEliteVariantResolver.LoadDirectory(EliteVariantSourceDirectory);
var consumedEliteVariantTypeIds = new System.Collections.Generic.HashSet<int>();
```

Change conversion to receive the map and consumed-ID set:

```csharp
units = sourceFiles
    .Select(path => Convert(path, eliteVariantsByTypeId, consumedEliteVariantTypeIds))
    .OrderBy(entry => entry.typeId, StringComparer.Ordinal)
    .ToArray()
```

After conversion, reject orphan sidecars:

```csharp
var orphanTypeIds = eliteVariantsByTypeId.Keys
    .Where(typeId => !consumedEliteVariantTypeIds.Contains(typeId))
    .OrderBy(typeId => typeId)
    .ToArray();
if (orphanTypeIds.Length > 0)
    throw new InvalidOperationException(
        "UNIT_ELITE_VARIANT_BASE_SOURCE_MISSING typeIds=" +
        string.Join(",", orphanTypeIds));
```

Change `Convert` to read the base text once, parse its `typeId`, and resolve only
when a matching sidecar exists:

```csharp
private static UnitCatalogEntry Convert(
    string sourcePath,
    System.Collections.Generic.IReadOnlyDictionary<int, UnitEliteVariantSource>
        eliteVariantsByTypeId,
    System.Collections.Generic.ISet<int> consumedEliteVariantTypeIds)
{
    string sourceJson;
    UnitJson source;
    try
    {
        sourceJson = File.ReadAllText(sourcePath);
        source = JsonUtility.FromJson<UnitJson>(sourceJson);
        if (source != null &&
            eliteVariantsByTypeId.TryGetValue(source.typeId, out var eliteVariants))
        {
            source = UnitEliteVariantResolver.Resolve(
                sourceJson,
                eliteVariants.Json,
                0,
                eliteVariants.Path);
            consumedEliteVariantTypeIds.Add(source.typeId);
        }
    }
    catch (Exception exception)
    {
        throw new InvalidOperationException(
            "TASK004A_CATALOG_SOURCE_INVALID path=" + sourcePath,
            exception);
    }

    if (source == null)
        throw new InvalidOperationException(
            "TASK004A_CATALOG_SOURCE_EMPTY path=" + sourcePath);
    if (!string.Equals(source.schemaVersion, "unit-source-v1", StringComparison.Ordinal)
        || source.typeId <= 0
        || string.IsNullOrWhiteSpace(source.resourceKey)
        || string.IsNullOrWhiteSpace(source.skeletonDataResourceName))
        throw new InvalidOperationException(
            "UNIT_DATA_001_SOURCE_REQUIRED_MISSING path=" + sourcePath
            + " typeId=" + source.typeId);
```

Keep the existing value validation and the method body beginning with
`var moveSpeed = ConvertMetresPerSecondToCentimetres(...)` unchanged. Do not copy
the high-tier data into `UnitCatalogEntry`; only the resolved elite-zero snapshot
reaches the existing flat output.

- [ ] **Step 8: Regenerate the catalog through Unity**

Run:

```powershell
& 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -batchmode `
  -nographics `
  -quit `
  -projectPath 'G:\ARKnoNIGHTS_beta' `
  -executeMethod UnitCatalogGenerator.Generate `
  -logFile 'G:\ARKnoNIGHTS_beta\Temp\UnitEliteVariants\CatalogGenerate.log'
```

Expected: process exit code `0`, log contains `TASK004A_CATALOG_GENERATED`, and no
`error CS`, `Compilation failed`, `Scripts have compiler errors`, or unhandled
exception. The type 1000 output must contain:

```json
{
  "resourceKey": "gopro",
  "displayNameZhHans": "猎狗",
  "maxHitPoints": 820,
  "attack": 190,
  "defense": 0,
  "magicResistance": 20,
  "moveSpeedCentimetresPerSecond": 190,
  "attackIntervalTicks": 14,
  "attackAnimationDurationTicks": 20,
  "skeletonDataResourcePath": "Characters/1000_gopro/enemy_1000_gopro_SkeletonData"
}
```

- [ ] **Step 9: Run focused source and catalog tests**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests.UnitEliteVariantSourceEditModeTests' `
  -OutputDirectory 'Temp/UnitEliteVariants/Resolver-Final' `
  -NoGraphics
```

Then run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests.BattleCoreEditModeTests' `
  -OutputDirectory 'Temp/UnitEliteVariants/BattleCore-Final' `
  -NoGraphics
```

Expected: both summaries report `result=Passed`, positive test totals, failed `0`.
Do not change battle assertions solely to suppress a failure; investigate whether
the failure is a legitimate consequence of the confirmed elite-zero data.

- [ ] **Step 10: Verify deterministic generation**

Record the catalog hash:

```powershell
$firstHash = (Get-FileHash -LiteralPath `
  'Assets/Resources/BattleData/unit-catalog-v1.json' -Algorithm SHA256).Hash
```

Run the Step 8 generation command again, then compare:

```powershell
$secondHash = (Get-FileHash -LiteralPath `
  'Assets/Resources/BattleData/unit-catalog-v1.json' -Algorithm SHA256).Hash
if ($firstHash -ne $secondHash) { throw "Catalog generation is not deterministic." }
```

Expected: hashes are identical.

- [ ] **Step 11: Commit generator, Texture2D portrait routing, regressions, and output**

```powershell
git add -- `
  Assets/Game/Editor/Battle/UnitCatalogGenerator.cs `
  Assets/Game/Battle/Infrastructure/RealBattleDataLoader.cs `
  Assets/Game/UI/FormalHud/UnitPortraitLoader.cs `
  Assets/Game/UI/FormalHud/UnitPortraitLoader.cs.meta `
  Assets/Game/UI/FormalHud/StagingHudController.cs `
  Assets/Game/UI/FormalHud/ShopReady/ShopReadyHudController.cs `
  Assets/Game/Runtime/Initial/FormalBattleHudController.cs `
  Assets/Game/Debug/ButtonDebug.cs `
  Assets/Game/Tests/EditMode/Battle/BattleCoreEditModeTests.cs `
  Assets/Game/Tests/EditMode/Battle/StagingHudLayoutEditModeTests.cs `
  Assets/Resources/BattleData/unit-catalog-v1.json
git diff --cached --check
git commit -m "feat: resolve elite zero with Texture2D portraits"
```

Confirm with `git diff --cached --name-only` before commit that no user-owned model,
portrait, or `Assets/GameData/Units/Json/1000_gopro.json` change is staged.

---

### Task 3: Verify the complete data path and synchronize project documentation

**Files:**
- Modify: `docs/SPEC.md`
- Modify: `docs/ARCHITECTURE.md`
- Modify: `docs/TEST_PLAN.md`

**Interfaces:**
- Consumes: generated elite-zero `unit-catalog-v1`
- Produces: authoritative behavior text and reproducible verification evidence

- [ ] **Step 1: Run the complete EditMode suite**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -OutputDirectory 'Temp/UnitEliteVariants/EditMode-All' `
  -NoGraphics
```

Expected: `result=Passed`, total greater than zero, failed `0`. Record the exact
total, failed, skipped, XML path, and log path from `summary.txt`.

- [ ] **Step 2: Run the catalog-dependent PlayMode tests**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform PlayMode `
  -TestFilter 'ArknoNights.Battle.Tests.BattlePresentationPlaybackPlayModeTests' `
  -OutputDirectory 'Temp/UnitEliteVariants/Presentation-PlayMode' `
  -NoGraphics
```

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform PlayMode `
  -TestFilter 'ArknoNights.Battle.Tests.BattleDemoCoordinatorPlayModeTests' `
  -OutputDirectory 'Temp/UnitEliteVariants/Demo-PlayMode' `
  -NoGraphics
```

Expected: both runs report `result=Passed`, positive totals, failed `0`.

- [ ] **Step 3: Build the Windows x64 Player**

Run:

```powershell
& 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -batchmode `
  -nographics `
  -quit `
  -projectPath 'G:\ARKnoNIGHTS_beta' `
  -executeMethod Task006StandaloneBuild.BuildWindowsX64 `
  -logFile 'G:\ARKnoNIGHTS_beta\Temp\UnitEliteVariants\WindowsStandaloneBuild.log'
```

Expected: exit code `0`; the log records `result=Succeeded`,
`platform=StandaloneWindows64`, and `errors=0`. Keep all build products in ignored
output directories and do not stage them.

- [ ] **Step 4: Update the authoritative SPEC**

In `docs/SPEC.md`, extend the confirmed elite-data rules with these statements:

- `Assets/GameData/Units/EliteVariants/Json/*.json` uses
  `unit-elite-variants-v1` as an optional authoritative sidecar to
  `unit-source-v1`.
- All imported variants use `unit-levels.json` level 0; only a missing level 0
  falls back to that variant's `unit-source-v1` standard values.
- Elite 0 and 1 select the base variant; elite 2 selects `_2`; elite 3 selects
  `_3`; a missing entry inherits the nearest lower entry.
- `stats.combat` and `stats.shared` inherit by atomic block; name and abilities
  inherit when omitted; explicit empty abilities clear; a declared model block is
  complete and atomic.
- Model changes bind the display name, resource key/folder, skeleton, portrait,
  skeleton type, animation names, attack animation duration, and variant abilities.
- All unit portraits are raw Texture2D Resources. Catalog validation loads
  `Texture2D`, and UGUI `Image` consumers use cached `UnitPortraitLoader` Sprites
  without trying Sprite resources first.
- In the current milestone the Editor generator resolves only elite 0 into the flat
  catalog. Instance `eliteLevel` still does not change runtime battle results until
  a later integration task.

This wording resolves the apparent conflict with the existing statement that
`eliteLevel` is currently presentation metadata: source storage is now prepared,
but high-tier runtime selection remains explicitly deferred.

- [ ] **Step 5: Update architecture and verification records**

In `docs/ARCHITECTURE.md`, update the catalog data flow to:

```text
unit-source-v1 + optional unit-elite-variants-v1
    -> UnitEliteVariantResolver(target elite 0)
    -> UnitCatalogGenerator
    -> flat unit-catalog-v1
    -> existing Player loaders
```

State that `UnitEliteVariantResolver` is Editor-only, that higher variants remain
outside the Player catalog for now, and that no runtime reads the project-external
staging directory. Also record the portrait path:

```text
portraitResourcePath
    -> Resources.Load<Texture2D>
    -> UnitPortraitLoader cached Sprite
    -> existing UGUI Image
```

In `docs/TEST_PLAN.md`, append a dated verification section containing:

- the exact catalog generation command and deterministic SHA-256;
- focused resolver and BattleCore NUnit totals;
- focused Texture2D portrait-loader NUnit total;
- full EditMode total;
- both PlayMode totals;
- Windows build result, error count, warning count, and log path;
- explicit note that elite 2/3 runtime selection, synthesis coefficients, and
  in-game promotion are not implemented or verified;
- explicit note that the pre-existing model/portrait/source changes were not
  authored or staged by this implementation.

- [ ] **Step 6: Review the final diff and logs**

Run:

```powershell
git diff --check
git status --short
git diff -- `
  Assets/Game/Editor/Battle/UnitEliteVariantResolver.cs `
  Assets/Game/Editor/Battle/UnitCatalogGenerator.cs `
  Assets/GameData/Units/EliteVariants/Json/1000_gopro.json `
  Assets/Game/Tests/EditMode/Battle/UnitEliteVariantSourceEditModeTests.cs `
  Assets/Game/Tests/EditMode/Battle/BattleCoreEditModeTests.cs `
  Assets/Resources/BattleData/unit-catalog-v1.json `
  docs/SPEC.md `
  docs/ARCHITECTURE.md `
  docs/TEST_PLAN.md
```

Search the generated/test/build logs:

```powershell
rg -n -i `
  "error CS|Compilation failed|Scripts have compiler errors|Unhandled Exception|result=Failed" `
  Temp/UnitEliteVariants
```

Expected: no whitespace errors, no unrelated staged files, no relevant compile or
test failures, and all user-owned resource changes remain present and unstaged.

- [ ] **Step 7: Commit documentation and verification records**

```powershell
git add -- docs/SPEC.md docs/ARCHITECTURE.md docs/TEST_PLAN.md
git diff --cached --check
git commit -m "docs: specify elite unit variant loading"
```

- [ ] **Step 8: Final completion audit**

Confirm all of the following before reporting completion:

- the checked-in sidecar has elite 0, 2, and 3 entries with level-zero stats;
- elite 1 resolves to base, and absent `shared` fields inherit;
- model blocks are validated atomically;
- every formal unit portrait consumer loads through `UnitPortraitLoader`, and
  directory validation accepts raw Texture2D portraits;
- type 1000 in `unit-catalog-v1` is elite-zero `猎狗`, HP 820, attack 190, and
  uses `enemy_1000_gopro_SkeletonData`;
- type 5503 and 5504 catalog entries remain unchanged;
- attack interval is still 14 Tick for type 1000 after the existing half-interval
  conversion;
- focused tests, full EditMode, relevant PlayMode, and Windows x64 build have
  evidence, or any unavailable item is explicitly reported as unverified;
- final staged changes do not include the user's pre-existing resource imports,
  base source edit, `.superpowers/`, or `docs/bonds/`.
