# Unit Elite Variants v2 Source Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the split `unit-source-v1` plus `unit-elite-variants-v1` authoring model with one validated, self-contained `unit-elite-variants-v2` file per TypeId, initially migrating 1000, 5503, and 5504 without changing either generated Player catalog.

**Architecture:** Move the resolver into the runtime-visible predefined assembly so Editor generators and the legacy `UnitFactory` consume the same v2 parser and fully resolved elite variant. Keep `Assets/GameData/Units/EliteVariants/Json` as the only authored unit source directory, project v2 elite 0 into existing legacy consumers at explicit compatibility boundaries, and fail whenever the frozen `unit-catalog-v1` cannot represent a source. Model animation facts live in one keyed registry; playback state, speed, and special behavior remain outside the source schema.

**Tech Stack:** Unity 2022.3.62f1c1, C# predefined assemblies, Unity `JsonUtility`, Spine Unity runtime APIs, NUnit EditMode/PlayMode tests, PowerShell verification scripts, Git.

## Global Constraints

- Work directly in `G:\ARKnoNIGHTS_beta`; do not create a worktree.
- Preserve all pre-existing dirty-worktree changes and stage only files named by the current task.
- Do not modify Unity, Package, render-pipeline, or Input System versions.
- Do not add packages, binaries, services, global tools, or external network dependencies.
- Preserve every moved Unity asset GUID by moving its paired `.meta` file with it.
- Do not modify scenes, Prefabs, ScriptableObjects, `ProjectSettings`, or generated Unity directories.
- `Assets/GameData/Units/EliteVariants/Json` is the only authored unit source directory after this migration.
- The source schema is exactly `unit-elite-variants-v2`; there is no v1 fallback.
- Phase 1 migrates only TypeIds `1000`, `5503`, and `5504`.
- Every migrated unit has `deploymentCost = 2`, `attackRadiusMetres = 0`, and `blockRadiusMetres = 0`.
- Do not add `unitSkeletonType`, `hitAnimation`, `attackAnimationDurationSeconds`, separate move/attack/death animation fields, `resourceFolderName`, `initialEliteLevel`, `Default` bindings, `animationBehavior`, playback speed, or target playback duration to v2.
- Keep only the existing `SUMMON_JELLY_MINIONS` innate ability on 5503; all other migrated variants use empty skill text and empty ability arrays unless inherited from a lower variant.
- Use the confirmed rarity sources for authored v2 data: 1000=`1`, 5503=`6`, and 5504=`3`. The frozen Player catalog intentionally retains its current 5503 rarity until a later catalog migration.
- Include every actually used non-idle/move/death animation in `model.animations` with a positive source duration; idle/move/death store names without duration.
- Do not implement the special playback rules in `docs/bonds/UnitAnimation.md`.
- Do not delete the legacy Hit interfaces in this phase; retain their destruction inventory in `docs/bonds/UnitAnimation.md`.
- Do not execute the menu command that writes the real Player catalogs during phase 1.
- The SHA-256 of `Assets/Resources/BattleData/unit-catalog-v1.json` must remain `359C81D56AB89EA735FAFCD0F2A6CA243076DE7C72A9086B7E4097B6B728B0AA`.
- The SHA-256 of `Assets/Resources/BattleData/ability-catalog-v1.json` must remain `BA76A69BFC5AFB186863ECF28AB36EBD504F14CD503347BE09CEEC652ECB3466`.
- Unity tests and builds run serially; confirm no Unity Editor or batchmode process owns this project before starting one.
- A skipped test, missing result XML, zero-test result, timeout, or incomplete log is not a pass.

---

## File and Interface Map

### Runtime-visible source contract

- Modify `Assets/GameData/Units/UnitJson.cs`: replace the flat v1 DTO with the resolved v2 value object and serializable v2 DTOs.
- Move `Assets/Game/Editor/Battle/UnitEliteVariantResolver.cs` and its `.meta` to `Assets/GameData/Units/UnitEliteVariantResolver.cs` and `.meta`: make one parser/resolver available to Editor code and `UnitFactory`.
- Create `Assets/GameData/Units/UnitEliteVariantJsonShape.cs` and `.meta`: perform dependency-free structural/property-presence validation before `JsonUtility` materialization.

### Authored data

- Modify `Assets/GameData/Units/EliteVariants/Json/1000_gopro.json`.
- Move and rewrite `Assets/GameData/Units/Json/5503_arcslma.json` plus `.meta` to `Assets/GameData/Units/EliteVariants/Json/5503_arcslma.json` plus `.meta`.
- Move and rewrite `Assets/GameData/Units/Json/5504_arcslmi.json` plus `.meta` to `Assets/GameData/Units/EliteVariants/Json/5504_arcslmi.json` plus `.meta`.
- Delete `Assets/GameData/Units/Json/1000_gopro.json` plus `.meta`.
- Delete the now-empty `Assets/GameData/Units/Json` directory and its `.meta` if one exists.

### Direct consumers

- Modify `Assets/Game/Editor/Battle/UnitCatalogGenerator.cs`: scan v2, resolve elite 0, verify real resources, and expose a path-scoped test output without writing the frozen catalog.
- Modify `Assets/Game/Editor/Battle/AbilityCatalogGenerator.cs`: load known TypeIds from v2 documents.
- Modify `Assets/Game/Editor/UnitJsonAbilityBakeTool.cs`: gather explicitly declared variant ability IDs from v2.
- Modify `Assets/Game/Runtime/Initial/UnitFactory.cs`: resolve v2 elite 0 and keep only the legacy/debug template-and-object adapter.
- Modify `Assets/Game/Runtime/Data/Unit/UnitSkelBase.cs`: accept explicit standard animation names for the legacy adapter.
- Modify `Assets/Game/Runtime/Data/Unit/UnitSkelType1.cs` and `UnitSkelType2.cs`: use the configured standard attack name rather than a string literal.
- Modify `Assets/Game/Debug/UITest.cs`: remove the unused old source-directory constant.

### Tests and documentation

- Rewrite `Assets/Game/Tests/EditMode/Battle/UnitEliteVariantSourceEditModeTests.cs`.
- Modify `Assets/Game/Tests/EditMode/Battle/BattleCoreEditModeTests.cs`.
- Create `Assets/Game/Tests/EditMode/Battle/UnitSourceConsumerEditModeTests.cs` and `.meta`.
- Create `Assets/Game/Tests/PlayMode/Battle/UnitFactoryV2SourcePlayModeTests.cs` and `.meta`.
- Modify `docs/SPEC.md`, `docs/ARCHITECTURE.md`, `docs/TEST_PLAN.md`, and `docs/UNIT-DATA-001.md`.
- Create `docs/decisions/2026-07-28-unit-elite-variants-v2-source.md`.
- Modify the status line in `docs/superpowers/specs/2026-07-28-unit-elite-variants-v2-source-design.md` after implementation passes.

### Interfaces shared between tasks

The runtime-visible contract must expose these exact members:

```csharp
internal sealed class UnitEliteVariantSource
{
    internal int TypeId { get; }
    internal string Path { get; }
    internal string Json { get; }
}

internal static class UnitEliteVariantResolver
{
    internal const string SchemaVersion = "unit-elite-variants-v2";
    internal static UnitEliteVariantSource LoadFile(string path);
    internal static IReadOnlyDictionary<int, UnitEliteVariantSource> LoadDirectory(string directory);
    internal static ResolvedUnitVariant Resolve(string json, int eliteLevel, string context);
    internal static ResolvedUnitVariant Resolve(UnitEliteVariantSource source, int eliteLevel);
    internal static IReadOnlyList<string> GetDeclaredInnateAbilityIds(UnitEliteVariantSource source);
}

[Serializable]
public sealed class ResolvedUnitVariant
{
    public const float BaseAttackIntervalMultiplier = 0.5f;

    public int typeId;
    public int minEliteLevel;
    public string sourceVariant;
    public int statsLevel;
    public string displayNameZhHans;
    public string skillDescriptionZhHans;

    public int rarity;
    public int deploymentCost;
    public int attackMethod;
    public int actionMethod;
    public float attackRadiusMetres;
    public float blockRadiusMetres;
    public bool canBlock;
    public int blockCapacity;
    public int tauntLevel;
    public string damageType;

    public int maxHitPoints;
    public int attack;
    public int defense;
    public int magicResistance;
    public float moveSpeedMetresPerSecond;
    public float attackIntervalSeconds;
    public int lifeDeduct;

    public string resourceKey;
    public string skeletonDataResourceName;
    public string profilePictureResourceName;
    public List<UnitAnimationBinding> animations;
    public List<string> innateAbilityIds;

    public float BaseAttackIntervalSeconds =>
        attackIntervalSeconds * BaseAttackIntervalMultiplier;

    public UnitAnimationBinding FindAnimation(string key);
    public UnitAnimationBinding RequireAnimation(string key, string context);
}

[Serializable]
public sealed class UnitAnimationBinding
{
    public string key;
    public string name;
    public float durationSeconds;
}
```

The v2 source DTOs in `UnitJson.cs` must be named:

```csharp
UnitEliteVariantsV2Document
UnitCommonSource
UnitVariantSource
UnitVariantStatsSource
UnitCombatStatsSource
UnitSharedStatsSource
UnitModelSource
UnitAnimationBinding
```

The shape reader must expose:

```csharp
internal readonly struct UnitJsonSlice
{
    internal int Start { get; }
    internal int Length { get; }
}

internal static class UnitEliteVariantJsonShape
{
    internal static UnitJsonSlice RootObject(string json, string context);
    internal static IReadOnlyDictionary<string, UnitJsonSlice> ReadObject(
        string json,
        UnitJsonSlice slice,
        string context);
    internal static IReadOnlyList<UnitJsonSlice> ReadArray(
        string json,
        UnitJsonSlice slice,
        string context);
    internal static void RequireExactProperties(
        IReadOnlyDictionary<string, UnitJsonSlice> properties,
        string context,
        params string[] expected);
}
```

`ReadObject` must reject a duplicate property in the same JSON object. `RequireExactProperties` must report missing and unexpected property names deterministically in ordinal order. This shape layer is what distinguishes an omitted numeric field from an explicitly authored zero without adding a JSON package.

---

### Task 1: Introduce the shared v2 contract and strict resolver

**Files:**

- Modify: `Assets/GameData/Units/UnitJson.cs`
- Move: `Assets/Game/Editor/Battle/UnitEliteVariantResolver.cs`
- Move: `Assets/Game/Editor/Battle/UnitEliteVariantResolver.cs.meta`
- Create: `Assets/GameData/Units/UnitEliteVariantJsonShape.cs`
- Create: `Assets/GameData/Units/UnitEliteVariantJsonShape.cs.meta`
- Test: `Assets/Game/Tests/EditMode/Battle/UnitEliteVariantSourceEditModeTests.cs`

**Interfaces:**

- Consumes: `UnitResourcePaths.BuildCharacterFolderName(int, string)` and `UnitResourcePaths.BuildProfilePictureResourceName(int, string)`.
- Produces: all interfaces in “Interfaces shared between tasks”.

- [ ] **Step 1: Replace the old resolver tests with v2 contract tests**

Use reflection against `Assembly-CSharp`, because an asmdef test assembly cannot directly reference a predefined assembly. Add these concrete tests:

```csharp
[TestCase(0, "猎狗", 820, 190, "gopro")]
[TestCase(1, "猎狗", 820, 190, "gopro")]
[TestCase(2, "猎狗pro", 1700, 260, "gopro_2")]
[TestCase(3, "狂暴的猎狗pro", 3000, 370, "gopro_3")]
public void Resolve_AppliesNearestLowerAtomicInheritance(
    int eliteLevel,
    string expectedName,
    int expectedHp,
    int expectedAttack,
    string expectedResourceKey)
{
    var resolved = Resolve(ValidThreeVariantFixture, eliteLevel, "1000_gopro.json");
    Assert.That(Field<string>(resolved, "displayNameZhHans"), Is.EqualTo(expectedName));
    Assert.That(Field<int>(resolved, "maxHitPoints"), Is.EqualTo(expectedHp));
    Assert.That(Field<int>(resolved, "attack"), Is.EqualTo(expectedAttack));
    Assert.That(Field<float>(resolved, "moveSpeedMetresPerSecond"), Is.EqualTo(1.9f));
    Assert.That(Field<string>(resolved, "resourceKey"), Is.EqualTo(expectedResourceKey));
}

[Test]
public void Resolve_InheritsTextAndAbilitiesButHonoursExplicitEmptyArray()
{
    var eliteTwo = Resolve(InheritanceFixture, 2, "9000_fixture.json");
    CollectionAssert.AreEqual(
        new[] { "ABILITY_A" },
        Field<System.Collections.IList>(eliteTwo, "innateAbilityIds").Cast<string>());

    var eliteThree = Resolve(InheritanceFixture, 3, "9000_fixture.json");
    Assert.That(Field<System.Collections.IList>(eliteThree, "innateAbilityIds"), Is.Empty);
}
```

Add individual rejection cases with these expected codes:

```csharp
[TestCase("unit-elite-variants-v1", "UNIT_ELITE_VARIANT_SCHEMA_INVALID")]
[TestCase("partial-common", "UNIT_ELITE_VARIANT_COMMON_SHAPE_INVALID")]
[TestCase("missing-elite-zero", "UNIT_ELITE_VARIANT_REQUIRED_MISSING")]
[TestCase("duplicate-level", "UNIT_ELITE_VARIANT_LEVEL_DUPLICATE")]
[TestCase("partial-combat", "UNIT_ELITE_VARIANT_COMBAT_SHAPE_INVALID")]
[TestCase("partial-shared", "UNIT_ELITE_VARIANT_SHARED_SHAPE_INVALID")]
[TestCase("partial-model", "UNIT_ELITE_VARIANT_MODEL_SHAPE_INVALID")]
[TestCase("duplicate-animation-key", "UNIT_ELITE_VARIANT_ANIMATION_KEY_DUPLICATE")]
[TestCase("missing-idle", "UNIT_ELITE_VARIANT_ANIMATION_REQUIRED_MISSING")]
[TestCase("timed-animation-without-duration", "UNIT_ELITE_VARIANT_ANIMATION_DURATION_INVALID")]
[TestCase("legacy-hit-field", "UNIT_ELITE_VARIANT_LEGACY_FIELD_FORBIDDEN")]
[TestCase("filename-mismatch", "UNIT_ELITE_VARIANT_FILE_NAME_INVALID")]
public void Resolve_RejectsInvalidV2Shape(string fixture, string expectedCode)
{
    var exception = Assert.Throws<TargetInvocationException>(
        () => Resolve(InvalidFixture(fixture), 0, "9000_fixture.json"));
    StringAssert.Contains(expectedCode, exception.InnerException.Message);
}
```

Also add valid non-attacker coverage:

```csharp
[Test]
public void Resolve_AllowsStationaryNonAttackerWithoutAttackAnimation()
{
    var resolved = Resolve(ValidStationaryNonAttackerFixture, 0, "10002_trtrsl.json");
    Assert.That(Field<int>(resolved, "attackMethod"), Is.Zero);
    Assert.That(Field<int>(resolved, "attack"), Is.Zero);
    Assert.That(Field<float>(resolved, "attackIntervalSeconds"), Is.Zero);
    Assert.That(Field<bool>(resolved, "canBlock"), Is.False);
    Assert.That(Field<int>(resolved, "blockCapacity"), Is.Zero);
    Assert.That(Field<string>(resolved, "damageType"), Is.EqualTo("None"));
}
```

Add a directory test that creates two differently named v2 files with the same
top-level TypeId and asserts:

```csharp
var exception = Assert.Throws<TargetInvocationException>(
    () => LoadDirectory(duplicateDirectory));
StringAssert.Contains(
    "UNIT_ELITE_VARIANT_TYPEID_DUPLICATE",
    exception.InnerException.Message);
```

- [ ] **Step 2: Run the resolver test and verify the v2 tests fail**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests.UnitEliteVariantSourceEditModeTests' `
  -OutputDirectory 'Temp\UnitEliteVariantsV2\Task1-Red' `
  -TimeoutSeconds 900 `
  -NoGraphics
```

Expected: non-zero result with test failures showing the current resolver still expects `unit-elite-variants-v1` and the current reflected signature takes both base and sidecar JSON.

- [ ] **Step 3: Move the resolver into the runtime-visible source area**

Move the `.cs` and `.meta` as one GUID-preserving pair. The destination must be:

```text
Assets/GameData/Units/UnitEliteVariantResolver.cs
Assets/GameData/Units/UnitEliteVariantResolver.cs.meta
```

Confirm the GUID before and after:

```powershell
Select-String '^guid:' 'Assets\GameData\Units\UnitEliteVariantResolver.cs.meta'
```

- [ ] **Step 4: Implement the dependency-free JSON shape reader**

Implement a character scanner, not a second semantic deserializer. It must:

```csharp
// Object algorithm:
// 1. Require '{' at slice start and '}' at slice end.
// 2. Read a quoted JSON property name, decode \" and \\ for comparison.
// 3. Require ':'.
// 4. Record the complete value slice by skipping a string, number/literal,
//    nested object, or nested array while respecting quoted strings.
// 5. Reject duplicate names in the same object.
// 6. Require either ',' or the closing brace.
//
// Array algorithm:
// 1. Require '[' and ']'.
// 2. Record each complete element slice with the same value skipper.
// 3. Require either ',' or the closing bracket.
//
// Exact-property algorithm:
// 1. Compare ordinal property-name sets.
// 2. Emit missing names and unexpected names in ordinal order.
// 3. Throw InvalidOperationException with the caller's stable error code.
```

Reject malformed strings, unclosed nesting, trailing tokens, and empty property names with `UNIT_ELITE_VARIANT_JSON_INVALID`.

- [ ] **Step 5: Replace the flat v1 DTO with v2 DTOs and a resolved value**

Use the exact field layout below:

```csharp
[Serializable]
internal sealed class UnitEliteVariantsV2Document
{
    public string schemaVersion;
    public int typeId;
    public UnitCommonSource common;
    public UnitVariantSource[] variants;
}

[Serializable]
internal sealed class UnitCommonSource
{
    public int rarity;
    public int deploymentCost;
    public int attackMethod;
    public int actionMethod;
    public float attackRadiusMetres;
    public float blockRadiusMetres;
    public bool canBlock;
    public int blockCapacity;
    public int tauntLevel;
    public string damageType;
}

[Serializable]
internal sealed class UnitVariantSource
{
    public int minEliteLevel;
    public string sourceVariant;
    public int statsLevel;
    public string displayNameZhHans;
    public string skillDescriptionZhHans;
    public UnitVariantStatsSource stats;
    public UnitModelSource model;
    public string[] innateAbilityIds;
}

[Serializable]
internal sealed class UnitVariantStatsSource
{
    public UnitCombatStatsSource combat;
    public UnitSharedStatsSource shared;
}

[Serializable]
internal sealed class UnitCombatStatsSource
{
    public int maxHitPoints;
    public int attack;
    public int defense;
    public int magicResistance;
}

[Serializable]
internal sealed class UnitSharedStatsSource
{
    public float moveSpeedMetresPerSecond;
    public float attackIntervalSeconds;
    public int lifeDeduct;
}

[Serializable]
internal sealed class UnitModelSource
{
    public string resourceKey;
    public string skeletonDataResourceName;
    public string profilePictureResourceName;
    public UnitAnimationBinding[] animations;
}
```

`ResolvedUnitVariant.FindAnimation` compares keys with `StringComparison.Ordinal`. `RequireAnimation` throws `UNIT_ELITE_VARIANT_ANIMATION_REQUIRED_MISSING` and includes key and context.

- [ ] **Step 6: Implement strict parse, validation, and inheritance**

The resolver must apply these rules in this order:

```csharp
private static readonly string[] ForbiddenLegacyFields =
{
    "unitSkeletonType",
    "hitAnimation",
    "attackAnimationDurationSeconds",
    "moveAnimation",
    "attackAnimation",
    "deathAnimation",
    "resourceFolderName",
    "initialEliteLevel",
    "animationBehavior"
};
```

1. Parse and validate the root shape exactly as `schemaVersion`, `typeId`, `common`, `variants`.
2. Reject any forbidden legacy property anywhere in the document.
3. Require schema `unit-elite-variants-v2` and positive TypeId.
4. Validate the common object has exactly the ten approved fields.
5. Require rarity `1..6`, deployment cost `2`, attack method `0..1`, action method `1..4`, both radii `0`, taunt `0`, and one of `Physical`, `Magic`, `None`.
6. Require `(canBlock, blockCapacity)` to be either `(true, 1)` or `(false, 0)`.
7. Sort variants by `minEliteLevel`; reject null entries, duplicate levels, and values outside `0..3`; require level 0.
8. Allow only `minEliteLevel`, `sourceVariant`, `statsLevel`, `displayNameZhHans`, `skillDescriptionZhHans`, `stats`, `model`, and `innateAbilityIds` in a variant. Require every variant to contain the first three and require `statsLevel = 0`.
9. Require elite 0 to explicitly contain `displayNameZhHans`, `skillDescriptionZhHans`, `stats`, `model`, and `innateAbilityIds`.
10. When `stats` is present, allow only complete `combat` and/or `shared` atomic blocks; elite 0 requires both.
11. When `model` is present, require all four model properties and validate every animation entry.
12. Require model keys `idle`, `move`, and `death`, with no `durationSeconds` property.
13. Require attackers to have the `attack` key and every non-base animation key to have a positive `durationSeconds`.
14. Require non-attackers to have damage `None`, attack `0`, attack interval `0`, and no key equal to `attack` or beginning with `attack.`.
15. Require attackers to have damage `Physical` or `Magic`, positive attack interval, and positive attack-animation duration.
16. Require action method 4 to have move speed `0`; other action methods require positive move speed.
17. Validate each model's `sourceVariant`, `resourceKey`, Skeleton name, and portrait name using `UnitResourcePaths`.
18. Validate the file name is `<typeId>_<elite-zero-resourceKey>.json` and elite-zero `sourceVariant` equals the file stem.
19. Resolve a requested elite level by starting from common, then applying every variant at or below the requested level. Replace whole `combat`, `shared`, and `model` blocks; inherit omitted text and ability arrays; copy all arrays/lists.
20. Return a new resolved object every call; never share mutable animation or ability lists between results.
21. `LoadDirectory` reads only top-level `*.json`, sorts file names ordinally, rejects duplicate TypeIds, and never returns a partial dictionary after an invalid file.

- [ ] **Step 7: Run the resolver tests and verify they pass**

Run the Task 1 command again with output `Temp\UnitEliteVariantsV2\Task1-Green`.

Expected: all `UnitEliteVariantSourceEditModeTests` pass, the XML contains a non-zero test count, and the Unity log has no C# compile errors.

- [ ] **Step 8: Commit the shared v2 contract**

```powershell
git add -- `
  Assets/GameData/Units/UnitJson.cs `
  Assets/GameData/Units/UnitEliteVariantResolver.cs `
  Assets/GameData/Units/UnitEliteVariantResolver.cs.meta `
  Assets/GameData/Units/UnitEliteVariantJsonShape.cs `
  Assets/GameData/Units/UnitEliteVariantJsonShape.cs.meta `
  Assets/Game/Editor/Battle/UnitEliteVariantResolver.cs `
  Assets/Game/Editor/Battle/UnitEliteVariantResolver.cs.meta `
  Assets/Game/Tests/EditMode/Battle/UnitEliteVariantSourceEditModeTests.cs
git diff --cached --check
git commit -m "feat: add self-contained unit variant v2 resolver"
```

The removed source paths in the `git add` command are intentional; Git records the GUID-preserving move.

---

### Task 2: Migrate the three authoritative unit source files

**Files:**

- Modify: `Assets/GameData/Units/EliteVariants/Json/1000_gopro.json`
- Move and modify: `Assets/GameData/Units/Json/5503_arcslma.json`
- Move: `Assets/GameData/Units/Json/5503_arcslma.json.meta`
- Move and modify: `Assets/GameData/Units/Json/5504_arcslmi.json`
- Move: `Assets/GameData/Units/Json/5504_arcslmi.json.meta`
- Delete: `Assets/GameData/Units/Json/1000_gopro.json`
- Delete: `Assets/GameData/Units/Json/1000_gopro.json.meta`
- Test: `Assets/Game/Tests/EditMode/Battle/UnitEliteVariantSourceEditModeTests.cs`

**Interfaces:**

- Consumes: `UnitEliteVariantResolver.LoadDirectory` and `Resolve`.
- Produces: three v2 files that all later consumers read.

- [ ] **Step 1: Add real-file and real-Spine failing tests**

Add:

```csharp
[Test]
public void RealSources_AreOnlyTheThreeSelfContainedV2Documents()
{
    var v2Directory = Path.Combine(
        Application.dataPath,
        "GameData/Units/EliteVariants/Json");
    var sources = LoadDirectory(v2Directory);

    Assert.That(DictionaryKeys(sources), Is.EqualTo(new[] { 1000, 5503, 5504 }));
    Assert.That(Directory.Exists(
        Path.Combine(Application.dataPath, "GameData/Units/Json")), Is.False);
}

[TestCase(1000, 0, "猎狗", "gopro", 820, 190)]
[TestCase(1000, 2, "猎狗pro", "gopro_2", 1700, 260)]
[TestCase(1000, 3, "狂暴的猎狗pro", "gopro_3", 3000, 370)]
[TestCase(5503, 0, "果冻小子", "arcslma", 18000, 1100)]
[TestCase(5504, 0, "果冻丁", "arcslmi", 2500, 290)]
public void RealSources_ResolveExpectedVariantFacts(
    int typeId,
    int eliteLevel,
    string name,
    string resourceKey,
    int hp,
    int attack)
{
    var resolved = ResolveReal(typeId, eliteLevel);
    Assert.That(Field<string>(resolved, "displayNameZhHans"), Is.EqualTo(name));
    Assert.That(Field<string>(resolved, "resourceKey"), Is.EqualTo(resourceKey));
    Assert.That(Field<int>(resolved, "maxHitPoints"), Is.EqualTo(hp));
    Assert.That(Field<int>(resolved, "attack"), Is.EqualTo(attack));
    Assert.That(Field<int>(resolved, "deploymentCost"), Is.EqualTo(2));
    Assert.That(Field<float>(resolved, "attackRadiusMetres"), Is.Zero);
    Assert.That(Field<float>(resolved, "blockRadiusMetres"), Is.Zero);
}
```

Add a Spine assertion helper that loads each resolved `SkeletonDataAsset`, finds every declared animation, and compares non-base durations with a tolerance of `0.000001f`.

Expected animation facts:

```text
1000_gopro, 1000_gopro_2, 1000_gopro_3:
  idle=Idle
  move=Run_Loop
  attack=Attack duration 1.0
  death=Die

5503_arcslma:
  idle=Idle
  move=Move
  attack=Attack duration 2.666667
  death=Die
  skill=Skill duration 1.5

5504_arcslmi:
  idle=Idle
  move=Move
  attack=Attack duration 1.166667
  death=Die
```

- [ ] **Step 2: Run the real-file tests and verify they fail**

Run the Task 1 test command with output `Temp\UnitEliteVariantsV2\Task2-Red`.

Expected: failures because only 1000 exists in the v2 directory, it is schema v1, and the old source directory still exists.

- [ ] **Step 3: Rewrite 1000 as the confirmed v2 example**

Use the complete JSON in section 8 of:

```text
docs/superpowers/specs/2026-07-28-unit-elite-variants-v2-source-design.md
```

Keep these exact variant facts:

```json
{
  "common": {
    "rarity": 1,
    "deploymentCost": 2,
    "attackMethod": 1,
    "actionMethod": 1,
    "attackRadiusMetres": 0,
    "blockRadiusMetres": 0,
    "canBlock": true,
    "blockCapacity": 1,
    "tauntLevel": 0,
    "damageType": "Physical"
  }
}
```

Each 1000 model registry contains exactly `idle`, `move`, `attack`, and `death`. Do not carry `Default`, `Move_Begin`, `Move_End`, `Run_Begin`, or `Run_End` into the authored registry because `UnitAnimation.md` confirms only `Run_Loop` is the move binding.

- [ ] **Step 4: Move 5503 and 5504 with their GUIDs and rewrite them as v2**

Move each JSON and `.meta` pair into the v2 directory. Use these exact common
values. The 5503 rarity intentionally follows the user-confirmed BONDS and
numerical reclassification sources (`docs/bonds/BONDS_SPEC.md` and
`docs/numerical_architecture/2026-07-27_unit_rarity_reclassification_analysis.md`),
which both say rarity 6. This supersedes the stale authored v1 value 4 while the
runtime catalog remains frozen:

```json
{
  "typeId": 5503,
  "common": {
    "rarity": 6,
    "deploymentCost": 2,
    "attackMethod": 1,
    "actionMethod": 1,
    "attackRadiusMetres": 0,
    "blockRadiusMetres": 0,
    "canBlock": true,
    "blockCapacity": 1,
    "tauntLevel": 0,
    "damageType": "Physical"
  }
}
```

```json
{
  "typeId": 5504,
  "common": {
    "rarity": 3,
    "deploymentCost": 2,
    "attackMethod": 1,
    "actionMethod": 1,
    "attackRadiusMetres": 0,
    "blockRadiusMetres": 0,
    "canBlock": true,
    "blockCapacity": 1,
    "tauntLevel": 0,
    "damageType": "Physical"
  }
}
```

5503 elite 0 must contain:

```json
{
  "minEliteLevel": 0,
  "sourceVariant": "5503_arcslma",
  "statsLevel": 0,
  "displayNameZhHans": "果冻小子",
  "skillDescriptionZhHans": "",
  "stats": {
    "combat": {
      "maxHitPoints": 18000,
      "attack": 1100,
      "defense": 0,
      "magicResistance": 0
    },
    "shared": {
      "moveSpeedMetresPerSecond": 0.2,
      "attackIntervalSeconds": 4.0,
      "lifeDeduct": 1
    }
  },
  "innateAbilityIds": [
    "SUMMON_JELLY_MINIONS"
  ]
}
```

5504 elite 0 keeps `2500/290/100/20`, move speed `1.9`, attack interval `1.5`, life deduct `1`, empty skill text, and an empty ability array.

- [ ] **Step 5: Delete the old 1000 base source and empty old directory**

Delete only:

```text
Assets/GameData/Units/Json/1000_gopro.json
Assets/GameData/Units/Json/1000_gopro.json.meta
Assets/GameData/Units/Json
Assets/GameData/Units/Json.meta
```

The directory deletion is allowed only after a read-only listing proves no other files remain.

- [ ] **Step 6: Run the real-file and Spine tests**

Run the Task 1 command with output `Temp\UnitEliteVariantsV2\Task2-Green`.

Expected: all resolver tests pass; five real resolved variants are checked; every declared animation exists; Attack and Skill durations match within `0.000001f`.

- [ ] **Step 7: Verify GUID preservation and commit the three sources**

Compare the original 5503/5504 GUIDs from Git history with the destination `.meta` files:

```powershell
git show HEAD:Assets/GameData/Units/Json/5503_arcslma.json.meta
git show HEAD:Assets/GameData/Units/Json/5504_arcslmi.json.meta
Select-String '^guid:' `
  'Assets\GameData\Units\EliteVariants\Json\5503_arcslma.json.meta', `
  'Assets\GameData\Units\EliteVariants\Json\5504_arcslmi.json.meta'
```

Then:

```powershell
git add -- `
  Assets/GameData/Units/EliteVariants/Json/1000_gopro.json `
  Assets/GameData/Units/EliteVariants/Json/5503_arcslma.json `
  Assets/GameData/Units/EliteVariants/Json/5503_arcslma.json.meta `
  Assets/GameData/Units/EliteVariants/Json/5504_arcslmi.json `
  Assets/GameData/Units/EliteVariants/Json/5504_arcslmi.json.meta `
  Assets/GameData/Units/Json `
  Assets/GameData/Units/Json.meta `
  Assets/Game/Tests/EditMode/Battle/UnitEliteVariantSourceEditModeTests.cs
git diff --cached --check
git commit -m "data: migrate initial units to variant v2 sources"
```

---

### Task 3: Migrate the unit catalog generator without touching the frozen catalog

**Files:**

- Modify: `Assets/Game/Editor/Battle/UnitCatalogGenerator.cs`
- Create: `Assets/Game/Tests/EditMode/Battle/UnitSourceConsumerEditModeTests.cs`
- Create: `Assets/Game/Tests/EditMode/Battle/UnitSourceConsumerEditModeTests.cs.meta`

**Interfaces:**

- Consumes: `UnitEliteVariantResolver.LoadDirectory`, `Resolve(source, 0)`, and `ResolvedUnitVariant.RequireAnimation`.
- Produces: `UnitCatalogGenerator.Generate(string sourceDirectory, string outputPath)` for isolated tests.

- [ ] **Step 1: Add a path-scoped catalog projection test**

Use reflection to invoke this exact private overload:

```csharp
private static void Generate(string sourceDirectory, string outputPath)
```

The test writes only beneath `Temp\UnitEliteVariantsV2\CatalogProjection` and asserts:

```csharp
var document = JsonUtility.FromJson<CatalogProjectionDocument>(
    File.ReadAllText(outputPath));
Assert.That(document.units.Select(unit => unit.typeId),
    Is.EqualTo(new[] { "1000", "5503", "5504" }));
Assert.That(document.units.Single(unit => unit.typeId == "1000").unitSkelType, Is.EqualTo(2));
Assert.That(document.units.Single(unit => unit.typeId == "1000").moveAnimation, Is.EqualTo("Run_Loop"));
Assert.That(document.units.Single(unit => unit.typeId == "1000").attackAnimationDurationTicks, Is.EqualTo(20));
Assert.That(document.units.Single(unit => unit.typeId == "5503").deploymentCost, Is.EqualTo(2));
Assert.That(document.units.Single(unit => unit.typeId == "5503").rarity, Is.EqualTo(6));
Assert.That(document.units.Single(unit => unit.typeId == "5503").attackAnimationDurationTicks, Is.EqualTo(54));
Assert.That(document.units.Single(unit => unit.typeId == "5503").hitAnimation, Is.Empty);
Assert.That(document.units.Single(unit => unit.typeId == "5504").attackAnimationDurationTicks, Is.EqualTo(24));
```

Declare test-local serializable `CatalogProjectionDocument` and
`CatalogProjectionEntry` DTOs containing exactly the fields read by these
assertions. Do not reflect the generator's private nested output types.

Before and after invocation, calculate the real catalog hash and assert it stays:

```text
359C81D56AB89EA735FAFCD0F2A6CA243076DE7C72A9086B7E4097B6B728B0AA
```

Add a negative fixture with `attackMethod = 0`; assert `UNIT_CATALOG_V1_SOURCE_UNREPRESENTABLE` and that the isolated output sentinel text is unchanged.

Add a second negative fixture that declares `innateAbilityIds:
["ABILITY_DOES_NOT_EXIST"]`; assert
`UNIT_CATALOG_SOURCE_ABILITY_UNKNOWN` and that the isolated output sentinel is
unchanged.

- [ ] **Step 2: Run the consumer test and verify it fails**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests.UnitSourceConsumerEditModeTests' `
  -OutputDirectory 'Temp\UnitEliteVariantsV2\Task3-Red' `
  -TimeoutSeconds 900 `
  -NoGraphics
```

Expected: failure because the overload does not exist and the generator still scans `Assets/GameData/Units/Json`.

- [ ] **Step 3: Convert the generator to direct v2 input**

Use:

```csharp
private const string SourceDirectory =
    "Assets/GameData/Units/EliteVariants/Json";
private const string AbilitySourceDirectory =
    "Assets/GameData/Abilities/Json";
private const int LegacyMappedSkeletonType = 2;

public static void Generate()
{
    Generate(SourceDirectory, OutputPath);
}
```

The overload must:

1. Load the directory once with `UnitEliteVariantResolver.LoadDirectory`.
2. Resolve elite 0 once per source.
3. Sort by numeric TypeId before conversion.
4. Build the full document in memory.
5. Validate duplicate output TypeIds.
6. Write `outputPath` only after every source validates.
7. Call `AssetDatabase.ImportAsset` only when `outputPath` begins with `Assets/`.

Do not call the parameterless `Generate()` during this phase.

Before converting any unit, load the complete set of authored
`ability-source-v1` IDs from `AbilitySourceDirectory`. Validate schema, non-empty
ID, and duplicate ID, then require every resolved `innateAbilityIds` entry to
exist in that set. Build and validate the entire source list before writing the
path-scoped output, so an unknown ability cannot leave a partial catalog.

- [ ] **Step 4: Project explicit v2 animation mappings into v1**

Use:

```csharp
var move = source.RequireAnimation("move", sourcePath);
var attack = source.RequireAnimation("attack", sourcePath);
var death = source.RequireAnimation("death", sourcePath);

unitSkelType = LegacyMappedSkeletonType;
moveAnimation = move.name;
attackAnimation = attack.name;
hitAnimation = string.Empty;
deathAnimation = death.name;
attackAnimationDurationTicks =
    ConvertSecondsToTicks(attack.durationSeconds, sourcePath + ":attack");
initialEliteLevel = 0;
```

The fixed Type 2 projection is a legacy transport choice, not an authored animation behavior. `UnitSkelPresentationView` already plays explicit catalog names and does not use Type 2's host-command name literals.

Before conversion, reject any resolved source that v1 cannot represent:

```csharp
if (source.attackMethod != 1
    || source.damageType == "None"
    || !source.canBlock
    || source.blockCapacity != 1
    || source.moveSpeedMetresPerSecond <= 0f
    || source.attackIntervalSeconds <= 0f)
{
    throw new InvalidOperationException(
        "UNIT_CATALOG_V1_SOURCE_UNREPRESENTABLE path=" + sourcePath
        + " typeId=" + source.typeId);
}
```

Do not map a non-attacker to melee/physical or a non-blocker to capacity 1.

- [ ] **Step 5: Verify every declared model fact against Unity/Spine**

For every resolved model:

1. Build the folder from `sourceVariant`.
2. Load the portrait by `profilePictureResourceName`.
3. Load the `SkeletonDataAsset`.
4. Find every declared animation by its real name.
5. For keys other than `idle`, `move`, and `death`, compare Spine duration with the source value to `0.000001f`.
6. Keep the existing exact metres-to-centimetres and seconds-to-Tick conversions.

The error for a duration mismatch must include source path, resource path, animation key, source seconds, and Spine seconds.

- [ ] **Step 6: Run the consumer test and verify it passes**

Run the Task 3 command with output `Temp\UnitEliteVariantsV2\Task3-Green`.

Expected: the isolated v1 projection contains three entries, the negative source does not overwrite its sentinel, and the real runtime catalog hash is unchanged.

- [ ] **Step 7: Commit the unit catalog consumer**

```powershell
git add -- `
  Assets/Game/Editor/Battle/UnitCatalogGenerator.cs `
  Assets/Game/Tests/EditMode/Battle/UnitSourceConsumerEditModeTests.cs `
  Assets/Game/Tests/EditMode/Battle/UnitSourceConsumerEditModeTests.cs.meta
git diff --cached --check
git commit -m "refactor: project unit catalog from variant v2"
```

---

### Task 4: Migrate ability validation and innate-ability baking

**Files:**

- Modify: `Assets/Game/Editor/Battle/AbilityCatalogGenerator.cs`
- Modify: `Assets/Game/Editor/UnitJsonAbilityBakeTool.cs`
- Modify: `Assets/Game/Tests/EditMode/Battle/UnitSourceConsumerEditModeTests.cs`
- Modify: `Assets/Game/Tests/EditMode/Battle/BattleCoreEditModeTests.cs`

**Interfaces:**

- Consumes: `UnitEliteVariantResolver.LoadDirectory` and `GetDeclaredInnateAbilityIds`.
- Produces: v2-backed known TypeIds and a pure ability-ID collection seam for tests.

- [ ] **Step 1: Add failing known-TypeId and ability-collection tests**

Add:

```csharp
[Test]
public void AbilityCatalogGenerator_UsesV2TypeIdsAndPreservesUnknownSummonFailure()
{
    var output = TempFileWith("must-not-change");
    var exception = InvokeAbilityGenerator(UnknownSummonSourceDirectory, output);
    StringAssert.Contains(
        "ABILITY_CATALOG_SOURCE_SUMMON_TYPE_UNKNOWN",
        exception.InnerException.Message);
    Assert.That(File.ReadAllText(output), Is.EqualTo("must-not-change"));
}

[Test]
public void UnitJsonBake_CollectsOnlyExplicitV2AbilityIds()
{
    var ids = InvokeDeclaredAbilityCollector(
        Path.Combine(Application.dataPath, "GameData/Units/EliteVariants/Json"));
    Assert.That(ids, Is.EqualTo(new[] { "SUMMON_JELLY_MINIONS" }));
}
```

Update the existing unknown-summon generator test to use a task-local temp directory under `Temp\UnitEliteVariantsV2` rather than `.superpowers`.

- [ ] **Step 2: Run the tests and verify the old-directory readers fail**

Run the Task 3 command with output `Temp\UnitEliteVariantsV2\Task4-Red`.

Expected: failure because `AbilityCatalogGenerator` and `UnitJsonBake` still reference `Assets/GameData/Units/Json`.

- [ ] **Step 3: Change known unit loading to v2**

Use:

```csharp
private const string UnitSourceDirectory =
    "Assets/GameData/Units/EliteVariants/Json";

private static ISet<string> LoadKnownUnitTypeIds()
{
    var sources = UnitEliteVariantResolver.LoadDirectory(UnitSourceDirectory);
    if (sources.Count == 0)
        throw new InvalidOperationException(
            "ABILITY_CATALOG_UNIT_SOURCE_EMPTY path=" + UnitSourceDirectory);

    return new HashSet<string>(
        sources.Keys.Select(id =>
            id.ToString(CultureInfo.InvariantCulture)),
        StringComparer.Ordinal);
}
```

Keep the existing private `Generate(string sourceDirectory, string outputPath)` signature so the current negative test remains path-scoped.

- [ ] **Step 4: Replace `UnitJsonLite` with a pure v2 ability collector**

Set:

```csharp
public const string UnitsJsonDir =
    "Assets/GameData/Units/EliteVariants/Json";
```

Add this exact seam:

```csharp
private static IReadOnlyList<string> CollectDeclaredAbilityIds(
    string unitSourceDirectory)
{
    return UnitEliteVariantResolver
        .LoadDirectory(unitSourceDirectory)
        .Values
        .SelectMany(UnitEliteVariantResolver.GetDeclaredInnateAbilityIds)
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Select(id => id.Trim())
        .Distinct(StringComparer.Ordinal)
        .OrderBy(id => id, StringComparer.Ordinal)
        .ToArray();
}
```

`ScanAndBake` registers the returned IDs, then rebuilds existing `UnitTemplate` assets exactly as before. It must not infer inherited or BONDS-described abilities that are absent from source.

- [ ] **Step 5: Run the consumer and existing battle-core tests**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests.UnitSourceConsumerEditModeTests;ArknoNights.Battle.Tests.BattleCoreEditModeTests' `
  -OutputDirectory 'Temp\UnitEliteVariantsV2\Task4-Green' `
  -TimeoutSeconds 900 `
  -NoGraphics
```

Expected: non-zero test count; both fixtures pass; unknown summon TypeId remains a hard failure; real generated catalogs are not written.

- [ ] **Step 6: Commit the ability consumers**

```powershell
git add -- `
  Assets/Game/Editor/Battle/AbilityCatalogGenerator.cs `
  Assets/Game/Editor/UnitJsonAbilityBakeTool.cs `
  Assets/Game/Tests/EditMode/Battle/UnitSourceConsumerEditModeTests.cs `
  Assets/Game/Tests/EditMode/Battle/BattleCoreEditModeTests.cs
git diff --cached --check
git commit -m "refactor: read unit abilities from variant v2"
```

---

### Task 5: Migrate the legacy UnitFactory adapter

**Files:**

- Modify: `Assets/Game/Runtime/Initial/UnitFactory.cs`
- Modify: `Assets/Game/Runtime/Data/Unit/UnitSkelBase.cs`
- Modify: `Assets/Game/Runtime/Data/Unit/UnitSkelType1.cs`
- Modify: `Assets/Game/Runtime/Data/Unit/UnitSkelType2.cs`
- Create: `Assets/Game/Tests/PlayMode/Battle/UnitFactoryV2SourcePlayModeTests.cs`
- Create: `Assets/Game/Tests/PlayMode/Battle/UnitFactoryV2SourcePlayModeTests.cs.meta`

**Interfaces:**

- Consumes: `UnitEliteVariantResolver.LoadDirectory`, `Resolve(source, 0)`, and `ResolvedUnitVariant.FindAnimation`.
- Produces: unchanged public `UnitFactory.SpawnAll` and `GetUnitBasicValueSO` behavior for the legacy/debug caller.

- [ ] **Step 1: Add a PlayMode test for the real legacy factory**

Invoke `UnitFactory.SpawnAll` through reflection, using a task-created parent GameObject and `setInactive: true`. Assert:

```csharp
Assert.That(spawned.Count, Is.EqualTo(3));
Assert.That(idMap.Keys.OrderBy(id => id), Is.EqualTo(new[] { 1000, 5503, 5504 }));

AssertTemplate(1000, "gopro", 820, 190, 1, 2, 1.9f, 0.7f);
AssertTemplate(5503, "arcslma", 18000, 1100, 6, 2, 0.2f, 2.0f);
AssertTemplate(5504, "arcslmi", 2500, 290, 3, 2, 1.9f, 0.75f);

var type2 = Type.GetType("UnitSkelType2, Assembly-CSharp");
Assert.That(type2, Is.Not.Null);
Assert.That(idMap[1000].GetComponent(type2), Is.Not.Null);
Assert.That(idMap[5503].GetComponent(type2), Is.Not.Null);
Assert.That(idMap[5504].GetComponent(type2), Is.Not.Null);
Assert.That(idMap.Values, Has.All.Matches<GameObject>(
    go => go.GetComponent<SkeletonAnimation>().skeletonDataAsset != null));
```

Inspect the protected configured names through reflection:

```csharp
Assert.That(MoveName(idMap[1000]), Is.EqualTo("Run_Loop"));
Assert.That(MoveName(idMap[5503]), Is.EqualTo("Move"));
Assert.That(AttackName(idMap[5504]), Is.EqualTo("Attack"));
```

Destroy every created object in `finally` and leave no scene changes.

- [ ] **Step 2: Run the PlayMode test and verify it fails**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta' `
  -TestPlatform PlayMode `
  -TestFilter 'ArknoNights.Battle.Tests.UnitFactoryV2SourcePlayModeTests' `
  -OutputDirectory 'Temp\UnitEliteVariantsV2\Task5-Red' `
  -TimeoutSeconds 900 `
  -NoGraphics
```

Expected: failure because `UnitFactory` still scans the deleted old directory.

- [ ] **Step 3: Resolve elite 0 from the v2 directory**

Use:

```csharp
private const string JsonRootRel =
    "GameData/Units/EliteVariants/Json";
private const int LegacyMappedSkeletonType = 2;
```

Load once:

```csharp
var sources = UnitEliteVariantResolver.LoadDirectory(rootAbs);
foreach (var source in sources.Values.OrderBy(item => item.TypeId))
{
    var resolved = UnitEliteVariantResolver.Resolve(source, 0);
    var template = BuildTemplate(resolved);
}
```

Do not catch a source validation exception and continue. A malformed authority file must abort `SpawnAll` after logging one contextual error; it must not return a partial three-unit set.

- [ ] **Step 4: Map the resolved v2 value to the legacy template**

Change the method signature to:

```csharp
private static UnitTemplate BuildTemplate(ResolvedUnitVariant source)
```

Map:

```csharp
so.typeID = source.typeId;
so.uintName = source.resourceKey;
so.ProfilePicture = source.profilePictureResourceName;
so.Rarity = source.rarity;
so.cost = source.deploymentCost;
so.attackMethod = source.attackMethod;
so.actionMethod = source.actionMethod;
so.unitskeltype = LegacyMappedSkeletonType;
so.HP = source.maxHitPoints;
so.atk = source.attack;
so.def = source.defense;
so.res = source.magicResistance;
so.attackInterval = source.BaseAttackIntervalSeconds;
so.attackRadius = source.attackRadiusMetres;
so.BlockRadius = source.blockRadiusMetres;
so.moveSpeed = source.moveSpeedMetresPerSecond;
so.isBlock = source.canBlock;
so.FixedAbility = new List<string>(source.innateAbilityIds);
so.LifeDeduct = source.lifeDeduct;
so.narrowTitle = source.tauntLevel;
```

Do not add source-schema fields to `UnitTemplate`.

- [ ] **Step 5: Configure standard animation names without adding behavior data**

Add to `UnitSkelBase`:

```csharp
[SerializeField] protected string attackAnimationName = "Attack";

public void ConfigureLegacySourceAnimations(
    string idleAnimationName,
    string configuredMoveAnimationName,
    string configuredAttackAnimationName)
{
    defaultAnimation = idleAnimationName ?? string.Empty;
    moveAnimationName = configuredMoveAnimationName ?? string.Empty;
    attackAnimationName = configuredAttackAnimationName ?? string.Empty;
}
```

Replace the local `const string attackAnimName = "Attack"` in both skeleton types with:

```csharp
var attackAnimName = attackAnimationName;
```

In `UnitFactory`, always add `UnitSkelType2`, then configure it from semantic keys:

```csharp
var idle = resolved.RequireAnimation("idle", source.Path);
var move = resolved.RequireAnimation("move", source.Path);
var attack = resolved.attackMethod == 0
    ? null
    : resolved.RequireAnimation("attack", source.Path);

var unitSkel = go.AddComponent<UnitSkelType2>();
unitSkel.unitIdentity = unitIdentity;
unitSkel.ConfigureLegacySourceAnimations(
    idle.name,
    move.name,
    attack == null ? string.Empty : attack.name);
```

This is only a name adapter for the legacy host-command component. It does not implement Skill, state transitions, alternating attacks, interruption, speed policy, or any `UnitAnimation.md` rule.

- [ ] **Step 6: Run the factory test and existing presentation tests**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta' `
  -TestPlatform PlayMode `
  -TestFilter 'ArknoNights.Battle.Tests.UnitFactoryV2SourcePlayModeTests;ArknoNights.Battle.Tests.BattlePresentationPlaybackPlayModeTests' `
  -OutputDirectory 'Temp\UnitEliteVariantsV2\Task5-Green' `
  -TimeoutSeconds 900 `
  -NoGraphics
```

Expected: both fixtures pass; the formal runtime catalog still creates its existing types from the unchanged catalog; the legacy factory creates three Type 2 adapters from v2.

- [ ] **Step 7: Commit the legacy adapter migration**

```powershell
git add -- `
  Assets/Game/Runtime/Initial/UnitFactory.cs `
  Assets/Game/Runtime/Data/Unit/UnitSkelBase.cs `
  Assets/Game/Runtime/Data/Unit/UnitSkelType1.cs `
  Assets/Game/Runtime/Data/Unit/UnitSkelType2.cs `
  Assets/Game/Tests/PlayMode/Battle/UnitFactoryV2SourcePlayModeTests.cs `
  Assets/Game/Tests/PlayMode/Battle/UnitFactoryV2SourcePlayModeTests.cs.meta
git diff --cached --check
git commit -m "refactor: load legacy unit factory from variant v2"
```

---

### Task 6: Remove stale source references and update the architecture contract

**Files:**

- Modify: `Assets/Game/Debug/UITest.cs`
- Modify: `Assets/Game/Tests/EditMode/Battle/BattleCoreEditModeTests.cs`
- Modify: `docs/SPEC.md`
- Modify: `docs/ARCHITECTURE.md`
- Modify: `docs/TEST_PLAN.md`
- Modify: `docs/UNIT-DATA-001.md`
- Create: `docs/decisions/2026-07-28-unit-elite-variants-v2-source.md`
- Modify: `docs/superpowers/specs/2026-07-28-unit-elite-variants-v2-source-design.md`

**Interfaces:**

- Consumes: the completed v2 source chain.
- Produces: current documentation and a clean active-code reference scan.

- [ ] **Step 1: Update the real-catalog test to separate frozen runtime facts from authored source facts**

Keep all existing assertions that load `BattleData/unit-catalog-v1`; those prove
the runtime freeze. Replace old source-text assertions with a resolver call
through reflection:

```csharp
var sourceRoot = Path.Combine(
    UnityEngine.Application.dataPath,
    "GameData/Units/EliteVariants/Json");
var resolvedGopro = ResolveV2Source(sourceRoot, 1000, 0);
var resolvedArcslma = ResolveV2Source(sourceRoot, 5503, 0);
var resolvedArcslmi = ResolveV2Source(sourceRoot, 5504, 0);

Assert.That(Field<string>(resolvedGopro, "resourceKey"), Is.EqualTo("gopro"));
Assert.That(Field<int>(resolvedGopro, "maxHitPoints"), Is.EqualTo(820));
Assert.That(Field<string>(resolvedArcslma, "resourceKey"), Is.EqualTo("arcslma"));
Assert.That(
    Animation(resolvedArcslma, "skill", "name"),
    Is.EqualTo("Skill"));
Assert.That(
    Animation(resolvedArcslma, "skill", "durationSeconds"),
    Is.EqualTo(1.5f));
Assert.That(Field<string>(resolvedArcslmi, "resourceKey"), Is.EqualTo("arcslmi"));
Assert.That(
    Directory.Exists(Path.Combine(
        UnityEngine.Application.dataPath,
        "GameData/Units/Json")),
    Is.False);
```

Do not change the frozen catalog expectations from cost 12/4, old rarity, or unit skeleton types; source and generated runtime data intentionally differ until the later catalog migration.

- [ ] **Step 2: Remove active-code old-directory references**

Delete the unused `JsonRootRel` from `UITest.cs`.

Run:

```powershell
rg -n `
  'GameData/Units/Json|Assets/GameData/Units/Json|unit-source-v1|unit-elite-variants-v1' `
  Assets `
  --glob '*.cs' `
  --glob '*.json'
```

Expected: zero matches. Generated runtime catalog files do not contain those strings.

- [ ] **Step 3: Update SPEC and architecture**

Document:

```text
Assets/GameData/Units/EliteVariants/Json/*.json
    -> UnitEliteVariantResolver(target elite 0)
    -> UnitCatalogGenerator
    -> frozen flat unit-catalog-v1
    -> existing Player loaders
```

State all of the following:

1. v2 is the only authored unit source.
2. Elite 0 is complete; higher entries inherit nearest lower atomic blocks.
3. `sourceVariant` is the physical folder authority.
4. `animations[]` stores keys, Spine names, and required source durations.
5. `Default`, Hit, skeleton-type, behavior, and playback-speed fields are absent.
6. Formal Player runtime still reads only generated Resources catalogs.
7. The source-reading `UnitFactory` is legacy/debug and is scheduled for destruction.
8. The v1 generator's Type 2 value is a temporary presentation transport, not an authored unit fact.
9. Authored v2 rarity is 1000=`1`, 5503=`6`, and 5504=`3`; the frozen catalog still exposes its pre-migration values.
10. A v2 non-attacker/non-blocker is valid source data but cannot yet be projected into `unit-catalog-v1`; projection fails explicitly.
11. The Hit destruction list remains in `docs/bonds/UnitAnimation.md`.

- [ ] **Step 4: Record the decision and mark the design implemented**

The decision document must include:

```markdown
# ADR: Self-contained unit elite variant v2 sources

- Status: Accepted
- Date: 2026-07-28

## Decision

`Assets/GameData/Units/EliteVariants/Json` is the only authored unit source.
Each TypeId owns one `unit-elite-variants-v2` document containing common rules
and all imported elite variants. Model animation facts use a keyed registry;
playback behavior stays in the animation layer.

## Compatibility

The existing Player catalogs are frozen during the first three-unit migration.
Legacy v1 projection uses mapped skeleton type 2 and an empty Hit name.
Sources that v1 cannot represent fail instead of being coerced.

## Destruction

Remove the source-reading UnitFactory adapter after formal runtime data no
longer depends on it. Remove the legacy Hit chain after the old presentation
catalog and playback interfaces are retired.
```

Change the design status from “已确认，尚未实施” to “已实施（首批 1000、5503、5504；其余单位导入未开始）”.

- [ ] **Step 5: Run documentation/reference and targeted EditMode checks**

Run:

```powershell
rg -n `
  'GameData/Units/Json|Assets/GameData/Units/Json|unit-source-v1|unit-elite-variants-v1' `
  Assets `
  --glob '*.cs' `
  --glob '*.json'

powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests.UnitEliteVariantSourceEditModeTests;ArknoNights.Battle.Tests.UnitSourceConsumerEditModeTests;ArknoNights.Battle.Tests.BattleCoreEditModeTests' `
  -OutputDirectory 'Temp\UnitEliteVariantsV2\Task6-Green' `
  -TimeoutSeconds 900 `
  -NoGraphics
```

Expected: the reference scan returns no active source/code match; all targeted tests pass with a non-zero count.

- [ ] **Step 6: Commit cleanup and documentation**

```powershell
git add -- `
  Assets/Game/Debug/UITest.cs `
  Assets/Game/Tests/EditMode/Battle/BattleCoreEditModeTests.cs `
  docs/SPEC.md `
  docs/ARCHITECTURE.md `
  docs/TEST_PLAN.md `
  docs/UNIT-DATA-001.md `
  docs/decisions/2026-07-28-unit-elite-variants-v2-source.md `
  docs/superpowers/specs/2026-07-28-unit-elite-variants-v2-source-design.md
git diff --cached --check
git commit -m "docs: adopt unit variant v2 source authority"
```

---

### Task 7: Final verification and diff audit

**Files:**

- Modify only if test evidence reveals a task-scoped defect in files already listed above.
- Do not stage any pre-existing HUD, imported character resource, profile picture, BONDS, rarity-analysis, `.superpowers`, scene, Prefab, Package, or ProjectSettings change.

**Interfaces:**

- Consumes: all prior tasks.
- Produces: completion evidence and a clean task-only commit range.

- [ ] **Step 1: Confirm the project is not open in Unity**

Run:

```powershell
Get-Process Unity,UnityHub -ErrorAction SilentlyContinue |
  Select-Object ProcessName,Id,Path
```

Expected: no `Unity` process has `G:\ARKnoNIGHTS_beta` open. `UnityHub` alone is harmless.

- [ ] **Step 2: Run full EditMode**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta' `
  -TestPlatform EditMode `
  -OutputDirectory 'Artifacts\UnitEliteVariantsV2\Full-EditMode' `
  -TimeoutSeconds 1200 `
  -NoGraphics
```

Expected: exit code 0, result XML exists, test count is greater than zero, failed count is zero, skipped count is recorded.

- [ ] **Step 3: Run full PlayMode**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -ProjectPath 'G:\ARKnoNIGHTS_beta' `
  -TestPlatform PlayMode `
  -OutputDirectory 'Artifacts\UnitEliteVariantsV2\Full-PlayMode' `
  -TimeoutSeconds 1200 `
  -NoGraphics
```

Expected: exit code 0, result XML exists, test count is greater than zero, failed count is zero, skipped count is recorded.

- [ ] **Step 4: Build Windows x64 with StrictMode**

```powershell
$env:ARKNIGHTS_BUILD_OUTPUT =
  'G:\ARKnoNIGHTS_beta\Artifacts\UnitEliteVariantsV2\WindowsStandalone\ARKnoNIGHTS.exe'
& 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -batchmode `
  -nographics `
  -quit `
  -projectPath 'G:\ARKnoNIGHTS_beta' `
  -executeMethod Task006StandaloneBuild.BuildWindowsX64 `
  -logFile 'G:\ARKnoNIGHTS_beta\Artifacts\UnitEliteVariantsV2\WindowsBuild.log'
if ($LASTEXITCODE -ne 0) {
    throw "Windows build failed with exit code $LASTEXITCODE"
}
```

Expected: exit 0; log contains `[TASK-006][build.succeeded]`, `result=Succeeded`, and `errors=0`; the EXE exists.

- [ ] **Step 5: Verify the generated Player catalogs are byte-identical**

```powershell
Get-FileHash `
  'Assets\Resources\BattleData\unit-catalog-v1.json', `
  'Assets\Resources\BattleData\ability-catalog-v1.json' `
  -Algorithm SHA256 |
  ForEach-Object { '{0} {1}' -f $_.Hash,$_.Path }
```

Expected exactly:

```text
359C81D56AB89EA735FAFCD0F2A6CA243076DE7C72A9086B7E4097B6B728B0AA unit-catalog-v1.json
BA76A69BFC5AFB186863ECF28AB36EBD504F14CD503347BE09CEEC652ECB3466 ability-catalog-v1.json
```

- [ ] **Step 6: Audit source layout, forbidden fields, and task diff**

Run:

```powershell
Get-ChildItem `
  'Assets\GameData\Units\EliteVariants\Json' `
  -Filter '*.json' |
  Sort-Object Name |
  Select-Object -ExpandProperty Name

Test-Path 'Assets\GameData\Units\Json'

rg -n `
  'unitSkeletonType|hitAnimation|attackAnimationDurationSeconds|resourceFolderName|initialEliteLevel|animationBehavior|\"Default' `
  Assets/GameData/Units/EliteVariants/Json

git diff --check
git status --short
git diff --stat 7e658b4..HEAD
git diff --name-status 7e658b4..HEAD
```

Expected:

- exactly `1000_gopro.json`, `5503_arcslma.json`, and `5504_arcslmi.json`;
- old directory result `False`;
- forbidden-field scan has zero matches;
- `git diff --check` has zero diagnostics;
- the commit range contains only files listed in this plan;
- unrelated dirty-worktree changes remain unstaged and unmodified by these commits.

- [ ] **Step 7: Inspect final serialization and runtime logs**

Check:

1. No `.meta` is missing for a created or moved Unity asset.
2. No scene, Prefab, ScriptableObject, Package, or ProjectSettings file is in the task commit range.
3. Full test and build logs contain no new compile error, unhandled exception, missing Skeleton, missing portrait, duration mismatch, or source-schema fallback.
4. Test XML counts and failure totals are recorded in `docs/TEST_PLAN.md`.
5. The Windows build result, error/warning counts, and log path are recorded in `docs/TEST_PLAN.md`.
6. The two catalog hashes are recorded in `docs/TEST_PLAN.md`.

- [ ] **Step 8: Commit evidence-only documentation if verification added results**

If Step 7 added only the actual verification results to `docs/TEST_PLAN.md`:

```powershell
git add -- docs/TEST_PLAN.md
git diff --cached --check
git commit -m "docs: record unit variant v2 verification"
```

If no tracked file changed, do not create an empty commit.

---

## Completion Gate

The phase is complete only when all of these statements are true:

- The sole authored directory contains exactly the three expected v2 files.
- 1000 resolves elite 0/1/2/3 with the confirmed nearest-lower inheritance.
- 5503 resolves its existing `SUMMON_JELLY_MINIONS` and includes the used `Skill` animation at 1.5 seconds.
- 5504 resolves with an empty ability array.
- All non-idle/move/death declared animations match real Spine duration within `0.000001f`.
- No forbidden v1/source-animation field remains in v2.
- Unit catalog generation can be tested to an isolated path but was not run against the real output.
- Ability generation and innate baking read v2.
- The legacy `UnitFactory` builds three elite-0 templates from v2 and remains explicitly marked for destruction.
- Both generated Player catalog hashes remain exact.
- Targeted and full Unity test suites pass with non-zero test counts.
- Windows x64 StrictMode build succeeds.
- Final diff is task-only and preserves all pre-existing user changes.

After this gate, stop. Importing the other 90 TypeIds and implementing the animation layer are separate phases.
