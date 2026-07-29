# Staging and Shop Affinity UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add region/category presentation to staging headers and add deployment-cost, region, and category rows to shop portraits.

**Architecture:** A versioned Resources JSON mirrors the shop-unit region/category assignments confirmed in `docs/bonds/BONDS_SPEC.md`. A shared UI-only catalog resolves labels, icon paths, and the staging priority rule; existing staging and shop controllers consume this read-only projection without changing gameplay ownership.

**Tech Stack:** Unity 2022.3.62f1c1, C# 9-compatible Unity scripts, UGUI, `JsonUtility`, NUnit/EditMode/PlayMode tests, repository PowerShell tooling.

## Global Constraints

- `docs/bonds/BONDS_SPEC.md` is authoritative for unit region/category membership.
- Do not upgrade Unity, packages, render pipeline, or Input System.
- Preserve all existing user changes, especially the uncommitted Ready artwork changes in `ShopReadyHudController.cs` and `ShopReadyHudStateEditModeTests.cs`.
- Do not modify scene, Prefab, ScriptableObject, Animator, package, or project-setting assets.
- Do not change shop purchase price, deployment cost, economy, affinity counts, buffs, or commands.
- All icons preserve aspect ratio; numeric text uses `FormalNumericFont`, and Chinese shop text uses `FangZhengHeiTiJianTi-1`.
- Unity batchmode processes must use the repository's single-process guard in `scripts/Invoke-UnityTests.ps1`.
- Because relevant source files already contain user-owned changes, implementation checkpoints use exact diff review rather than commits that could accidentally capture unrelated work.

---

### Task 1: Shared affinity presentation catalog and deterministic runtime data

**Files:**
- Create: `Assets/Game/UI/FormalHud/UnitAffinityPresentationCatalog.cs`
- Create: `Assets/Game/Tests/EditMode/Battle/UnitAffinityPresentationEditModeTests.cs`
- Create: `scripts/Export-UnitAffinityPresentation.ps1`
- Generate: `Assets/Resources/UI/Data/unit-affinity-presentation-v1.json`
- Generate by Unity import: new `.meta` files for the files and folders above, plus the two user-provided occupation textures that currently lack `.meta`

**Interfaces:**
- Produces: `UnitAffinityPresentationCatalog.LoadFromResources()`
- Produces: `UnitAffinityPresentationCatalog.TryGet(string typeId, out UnitAffinityPresentation value)`
- Produces: `UnitAffinityPresentationCatalog.Entries`
- Produces: `UnitAffinityPresentation.RegionName`, `RegionIconResourcePath`, `OccupationName`, `OccupationIconResourcePath`, and `PreferredHeaderIconResourcePath`
- Consumes: current `docs/bonds/BONDS_SPEC.md` headings and numeric unit lines

- [ ] **Step 1: Write a compile-safe failing existence test**

Add this test before creating the production type:

```csharp
[Test]
public void AffinityPresentationCatalog_IsAvailableToTheUiAssembly()
{
    var type = Type.GetType(
        "ArknoNights.UI.UnitAffinityPresentationCatalog, ARKnoNIGHTS.UI");
    Assert.NotNull(type);
}
```

- [ ] **Step 2: Run the targeted EditMode test and verify RED**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests.UnitAffinityPresentationEditModeTests' `
  -OutputDirectory 'Artifacts\AffinityUi\Task1-Red-Type' `
  -NoGraphics
```

Expected: one failed assertion because the UI catalog type is absent; the XML must report a nonzero test count.

- [ ] **Step 3: Implement the minimal catalog API**

Create serializable JSON DTOs and immutable presentation values. The public surface must be:

```csharp
public sealed class UnitAffinityPresentation
{
    public string TypeId { get; }
    public string RegionName { get; }
    public string RegionIconResourcePath { get; }
    public string OccupationName { get; }
    public string OccupationIconResourcePath { get; }
    public string PreferredHeaderIconResourcePath =>
        !string.IsNullOrEmpty(RegionIconResourcePath)
            ? RegionIconResourcePath
            : OccupationIconResourcePath;
}

public sealed class UnitAffinityPresentationCatalog
{
    public const string ResourcePath =
        "UI/Data/unit-affinity-presentation-v1";

    public IReadOnlyList<UnitAffinityPresentation> Entries { get; }

    public bool TryGet(
        string typeId,
        out UnitAffinityPresentation value);

    public static UnitAffinityPresentationCatalog LoadFromResources();
    public static UnitAffinityPresentationCatalog Parse(string json);
}
```

`Parse` must reject a wrong schema version, blank or duplicate TypeIds, unknown definition IDs, missing occupation IDs, duplicate definition IDs, and records with more than one resolved region. `LoadFromResources` must return an empty catalog and log a structured error if the resource is absent or invalid; it must not throw through HUD initialization.

- [ ] **Step 4: Replace the smoke assertion with behavioral parsing tests**

Tests must construct small JSON documents and assert:

```csharp
Assert.AreEqual(
    "UI/Texture/region/logo_reunionMovement",
    catalogEntry.PreferredHeaderIconResourcePath);

Assert.AreEqual(
    "UI/Texture/occupation/logo_sami",
    noRegionCollapsal.PreferredHeaderIconResourcePath);

Assert.AreEqual(string.Empty, other.OccupationIconResourcePath);
Assert.AreEqual(string.Empty, other.PreferredHeaderIconResourcePath);
```

Also assert every invalid document listed in Step 3 produces a deterministic `FormatException` from `Parse`.

- [ ] **Step 5: Write the failing BONDS/resource parity test**

The test reads the repository document, extracts the 94 canonical IDs from `## 商店单位（94）`, then extracts numeric entries under the six category headings and eight region headings. It loads the runtime catalog, requires all canonical records plus the one approved Demo compatibility record `1000 = 整合运动 + 感染生物`, and compares the canonical maps:

```csharp
CollectionAssert.IsSubsetOf(shopTypeIds, runtimeByTypeId.Keys);
Assert.AreEqual(95, runtimeByTypeId.Count);
Assert.AreEqual(81, canonicalEntries.Count(item =>
    !string.IsNullOrEmpty(item.RegionName)));
Assert.AreEqual(13, canonicalEntries.Count(item =>
    string.IsNullOrEmpty(item.RegionName)));
Assert.AreEqual(86, canonicalEntries.Count(item =>
    !string.IsNullOrEmpty(item.OccupationName)));
Assert.AreEqual(8, canonicalEntries.Count(item =>
    string.IsNullOrEmpty(item.OccupationName)));
```

For each TypeId, assert exact Chinese category and optional region equality. Assert the eight region icons and five non-empty occupation icons load through `FormalHudSpriteLoader.Load`; assert “其他” intentionally has no icon.

- [ ] **Step 6: Run the parity test and verify RED**

Use the Task 1 command with output directory `Artifacts\AffinityUi\Task1-Red-Resource`.

Expected: failure because `unit-affinity-presentation-v1` does not exist.

- [ ] **Step 7: Create the deterministic exporter and generate JSON**

`Export-UnitAffinityPresentation.ps1` must:

1. resolve the repository root from `$PSScriptRoot`;
2. parse UTF-8 `docs/bonds/BONDS_SPEC.md`;
3. require exactly 94 unique canonical shop IDs;
4. allow zero or one of the six categories per shop ID, and require exactly 86 categorized IDs;
5. allow zero or one of the eight regions per shop ID and reject multiple regions;
6. require every shop ID to have at least one dimension, with canonical counts `94 total`, `81 with region`, `13 without region`, `86 with occupation`, and `8 without occupation`;
7. emit stable definitions in the order documented by the design;
8. append the approved compatibility record `1000 = reunion + infected`, then emit all unit records sorted by numeric TypeId;
9. write UTF-8 without BOM to `Assets/Resources/UI/Data/unit-affinity-presentation-v1.json`;
10. return nonzero without writing partial output if any assertion fails.

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Export-UnitAffinityPresentation.ps1
```

Expected summary:

```text
AFFINITY_UI_EXPORTED schema=unit-affinity-presentation-v1 canonicalUnits=94 compatibilityUnits=1 totalUnits=95 canonicalRegions=81 canonicalNoRegion=13 canonicalOccupations=86 canonicalNoOccupation=8 occupationDefinitions=6
```

- [ ] **Step 8: Run Task 1 tests and verify GREEN**

Run the targeted command with output directory `Artifacts\AffinityUi\Task1-Green`.

Expected: all `UnitAffinityPresentationEditModeTests` pass, zero skipped, no missing-resource or import errors.

- [ ] **Step 9: Review the Task 1 diff**

Confirm the generated JSON contains no descriptions or combat values, no source file outside the listed files changed, and Unity-generated `.meta` files point only at new project assets.

---

### Task 2: Project deployment cost into the shop's read-only state

**Files:**
- Modify: `Assets/Game/Runtime/Data/Player/LocalMatchState.cs`
- Modify: `Assets/Game/UI/FormalHud/ShopReady/ShopReadyHudState.cs`
- Modify: `Assets/Game/Tests/EditMode/Battle/LocalMatchStateEditModeTests.cs`
- Modify: `Assets/Game/Tests/EditMode/Battle/ShopReadyHudStateEditModeTests.cs`

**Interfaces:**
- Produces: `LocalMatchShopSlotSnapshot.DeploymentCost`
- Produces: `ShopReadySlotViewState.DeploymentCost`
- Consumes: `UnitCatalogEntry.DeploymentCost`

- [ ] **Step 1: Write failing reflection-based snapshot assertions**

Avoid a compile error while the properties are absent:

```csharp
var shopSlot = match.Snapshot.LocalPlayer.ShopSlots[0];
var domainProperty = shopSlot.GetType().GetProperty("DeploymentCost");
Assert.NotNull(domainProperty);
Assert.AreEqual(2, domainProperty.GetValue(shopSlot));

var viewState = ShopReadyHudState.Project(
    match.Snapshot, true, ShopReadyConfirmation.None).Slots[0];
var uiProperty = viewState.GetType().GetProperty("DeploymentCost");
Assert.NotNull(uiProperty);
Assert.AreEqual(2, uiProperty.GetValue(viewState));
Assert.AreEqual(1, viewState.Price);
```

This explicitly distinguishes `1000`'s deployment cost `2` from purchase price `1`.

- [ ] **Step 2: Run targeted EditMode tests and verify RED**

Run `LocalMatchStateEditModeTests` and `ShopReadyHudStateEditModeTests` separately through `Invoke-UnityTests.ps1`, writing to `Artifacts\AffinityUi\Task2-Red-*`.

Expected: the new property-existence assertions fail while existing assertions still compile.

- [ ] **Step 3: Add the two minimal read-only properties**

In `LocalMatchShopSlotSnapshot`, assign only when the catalog entry exists:

```csharp
DeploymentCost = type.DeploymentCost;
```

Expose:

```csharp
public int DeploymentCost { get; }
```

Copy it without recalculation in `ShopReadySlotViewState`:

```csharp
DeploymentCost = source.DeploymentCost;
public int DeploymentCost { get; }
```

Do not add deployment cost to shop configuration or canonical command state.

- [ ] **Step 4: Run targeted EditMode tests and verify GREEN**

Expected: both fixtures pass with nonzero test counts and zero skipped.

- [ ] **Step 5: Review the Task 2 diff**

Confirm price remains `Rarity`, purchase affordability still compares `Gold >= Price`, and no serialized fixture changes.

---

### Task 3: Add the priority icon to staging HeaderLeft

**Files:**
- Modify: `Assets/Game/UI/FormalHud/StagingHudController.cs`
- Modify: `Assets/Game/Tests/PlayMode/Battle/StagingHudScenePlayModeTests.cs`

**Interfaces:**
- Consumes: `UnitAffinityPresentationCatalog`
- Produces hierarchy: `StagingSlot/Header/HeaderLeft/AffinityIcon`

- [ ] **Step 1: Write a failing scene assertion**

In the existing real `SampleScene` test, locate the `1000` staging slot and assert:

```csharp
var icon = firstSlot.Find(
    "Header/HeaderLeft/AffinityIcon").GetComponent<Image>();
Assert.NotNull(icon);
Assert.AreEqual("logo_reunionMovement", icon.sprite.name);
Assert.IsTrue(icon.preserveAspect);
Assert.AreEqual(new Vector2(.5f, .5f), icon.rectTransform.anchorMin);
Assert.AreEqual(new Vector2(.5f, .5f), icon.rectTransform.anchorMax);
Assert.AreEqual(new Vector2(20f, 20f), icon.rectTransform.sizeDelta);
```

Locate the `5503` staging slot and assert its icon is disabled because it has no region and “其他” has no icon.

- [ ] **Step 2: Run the staging PlayMode fixture and verify RED**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform PlayMode `
  -TestFilter 'ArknoNights.Battle.Tests.StagingHudScenePlayModeTests' `
  -OutputDirectory 'Artifacts\AffinityUi\Task3-Red' `
  -NoGraphics
```

Expected: failure because `AffinityIcon` is absent.

- [ ] **Step 3: Implement the minimal staging binding**

Load the shared catalog once per `StagingHudController`. Extend `StagingSlotView.Bind` to receive the resolved affinity. Create `AffinityIcon` as a child of `HeaderLeft`, set `raycastTarget = false`, `preserveAspect = true`, center anchors/pivot, zero anchored position, and `20 x 20` size.

Because `HeaderLeft` mirrors its background with local X scale `-1`, give the child icon a local X scale `-1` so the final artwork is not mirrored.

Every bind must clear the old Sprite before assigning the new one:

```csharp
headerAffinityIcon.sprite = null;
headerAffinityIcon.enabled = false;
if (affinity != null &&
    !string.IsNullOrEmpty(affinity.PreferredHeaderIconResourcePath))
{
    headerAffinityIcon.sprite = FormalHudSpriteLoader.Load(
        affinity.PreferredHeaderIconResourcePath);
    headerAffinityIcon.enabled = headerAffinityIcon.sprite != null;
}
```

- [ ] **Step 4: Run the staging PlayMode fixture and verify GREEN**

Expected: all staging scene tests pass; no header icon is stretched or mirrored.

- [ ] **Step 5: Review the Task 3 diff**

Confirm HeaderRight cost, click/drag targets, rarity icon, elite icon, and slot layout calculations are unchanged.

---

### Task 4: Add cost, region, and category rows to shop portraits

**Files:**
- Modify: `Assets/Game/UI/FormalHud/ShopReady/ShopReadyHudController.cs`
- Modify: `Assets/Game/Tests/EditMode/Battle/ShopReadyHudStateEditModeTests.cs`
- Modify: `Assets/Game/Tests/PlayMode/Battle/BattleHudSceneIntegrationPlayModeTests.cs`

**Interfaces:**
- Consumes: `ShopReadySlotViewState.DeploymentCost`
- Consumes: `UnitAffinityPresentationCatalog`
- Produces hierarchy:
  - `ShopSlot_<id>/PortraitClip/CostInfo/Icon`
  - `ShopSlot_<id>/PortraitClip/CostInfo/Value`
  - `ShopSlot_<id>/PortraitClip/RegionInfo/Icon`
  - `ShopSlot_<id>/PortraitClip/RegionInfo/Value`
  - `ShopSlot_<id>/PortraitClip/OccupationInfo/Icon`
  - `ShopSlot_<id>/PortraitClip/OccupationInfo/Value`
- Produces: `FormalHudSpriteLoader.LoadAtlasSprite(string atlasResourcePath, string spriteName)`

- [ ] **Step 1: Write failing controller hierarchy and binding assertions**

Initialize the real match fixture, open the shop, and assert for slot 0:

```csharp
Assert.AreEqual("2", costValue.text);
Assert.AreSame(StagingHudController.FormalNumericFont, costValue.font);
Assert.AreEqual("DeploymentCostPanelIcon", costIcon.sprite.name);
Assert.IsTrue(costIcon.preserveAspect);

Assert.AreEqual("整合运动", regionValue.text);
Assert.AreEqual("感染生物", occupationValue.text);
Assert.AreNotSame(StagingHudController.FormalNumericFont, regionValue.font);
Assert.AreSame(regionValue.font, occupationValue.font);
Assert.AreEqual("1", slot.Find("Price").GetComponent<Text>().text);
```

Assert row bottoms are `4`, `26`, and `48` multiplied by `ShopVisualScale`, row heights are `20 * ShopVisualScale`, icons are `18 * ShopVisualScale`, and each icon/text pair shares the same vertical center.

Refresh to the all-`5503` page and assert `RegionInfo` is inactive, `OccupationInfo/Value` displays `其他`, and its Image is inactive. Purchase one slot through the existing two-click API and assert all three row roots become inactive in the empty slot.

- [ ] **Step 2: Run ShopReady EditMode tests and verify RED**

Run the fixture through `Invoke-UnityTests.ps1` with output `Artifacts\AffinityUi\Task4-Red-EditMode`.

Expected: failure because the three row hierarchies do not exist.

- [ ] **Step 3: Implement atlas Sprite lookup**

Add a cache keyed by `atlasResourcePath + "|" + spriteName` and load via:

```csharp
foreach (var sprite in Resources.LoadAll<Sprite>(atlasResourcePath))
{
    if (sprite != null &&
        string.Equals(sprite.name, spriteName, StringComparison.Ordinal))
        return sprite;
}
```

Return `null` and log a structured missing-Sprite error if no match exists. Do not alter the atlas texture or `.meta`.

- [ ] **Step 4: Build the three row widgets**

Extend `SlotWidgets` with `PortraitClip` and three small row records containing Root, Icon, and Value. Create rows after `Portrait` so they draw above the portrait while remaining inside the clip.

Use `NumberLabel` for cost and `Label` for region/category. Set all icons to `preserveAspect = true` and `raycastTarget = false`.

- [ ] **Step 5: Bind and clear every row**

For nonempty slots:

```csharp
costRow.Value.text = slot.DeploymentCost.ToString();
BindAffinityRow(regionRow, affinity?.RegionName,
    affinity?.RegionIconResourcePath);
BindAffinityRow(occupationRow, affinity?.OccupationName,
    affinity?.OccupationIconResourcePath);
```

For empty slots, deactivate all three roots. `BindAffinityRow` hides a blank-name row, shows nonblank text, and independently hides a blank/missing icon without hiding its text.

- [ ] **Step 6: Apply exact row geometry**

Use existing `PositionBottomLeft` with natural coordinates multiplied by `ShopVisualScale`:

```csharp
LayoutInfoRow(costRow, 4f, scale);
LayoutInfoRow(regionRow, 26f, scale);
LayoutInfoRow(occupationRow, 48f, scale);
```

Each row uses left `6`, width `132`, height `20`; its icon uses left `0`, bottom `1`, size `18`; its text uses left `22`, bottom `0`, width `110`, height `20`. Apply reference font size `13` through `ScaleShopText`.

- [ ] **Step 7: Run ShopReady EditMode tests and verify GREEN**

Expected: all ShopReady tests pass, including the user's pre-existing Ready sliced-image test.

- [ ] **Step 8: Add and run real-scene PlayMode assertions**

In `BattleHudSceneIntegrationPlayModeTests`, open the shop through the existing coordinator path and assert the same first-slot text, icon, fonts, order, and deployment-cost/purchase-price distinction.

Run with output `Artifacts\AffinityUi\Task4-Green-PlayMode`.

Expected: fixture passes with nonzero tests and zero skipped.

- [ ] **Step 9: Review the Task 4 diff**

Compare against the pre-task diff and confirm the Ready `Image.Type.Sliced` changes remain intact, existing six-card geometry and action buttons are unchanged, and no row Image intercepts pointer input.

---

### Task 5: Synchronize behavior documentation and run final verification

**Files:**
- Modify: `docs/SPEC.md`
- Modify: `docs/references/ui/battle_hud/UI_SPEC.md`
- Modify: `docs/TEST_PLAN.md`

**Interfaces:**
- Consumes: all implemented UI behavior and test evidence
- Produces: documented acceptance rules and actual result locations/counts

- [ ] **Step 1: Update the authoritative behavior summaries**

Add the confirmed staging priority, shop row order, deployment-cost distinction, fallback behavior, resource mapping location, aspect-ratio rule, and font rule. Replace UI_SPEC's deferred shop-affinity placement note with the exact implemented geometry.

- [ ] **Step 2: Run fresh targeted EditMode verification**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests.UnitAffinityPresentationEditModeTests' `
  -OutputDirectory 'Artifacts\AffinityUi\Final-Affinity-EditMode' `
  -NoGraphics
```

Then run `LocalMatchStateEditModeTests` and `ShopReadyHudStateEditModeTests` into separate final directories.

- [ ] **Step 3: Run fresh targeted PlayMode verification**

Run `StagingHudScenePlayModeTests` and `BattleHudSceneIntegrationPlayModeTests` into separate final directories.

- [ ] **Step 4: Run full EditMode and PlayMode regressions**

Run without `-TestFilter`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -OutputDirectory 'Artifacts\AffinityUi\Final-Full-EditMode' `
  -NoGraphics
```

Repeat with `-TestPlatform PlayMode` and `Artifacts\AffinityUi\Final-Full-PlayMode`.

Expected: both XML files contain nonzero totals, zero failures, and zero skipped. Scan both logs for `error CS`, `Compilation failed`, `Scripts have compiler errors`, `Unhandled Exception`, and `NullReferenceException`.

- [ ] **Step 5: Build the visible Windows Player**

Run:

```powershell
$env:ARKNIGHTS_BUILD_OUTPUT = 'G:\ARKnoNIGHTS_beta\Artifacts\AffinityUi\Windows\ARKnoNIGHTS.exe'
& 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -batchmode -nographics -quit `
  -projectPath 'G:\ARKnoNIGHTS_beta' `
  -executeMethod Task006StandaloneBuild.BuildWindowsX64 `
  -logFile 'G:\ARKnoNIGHTS_beta\Artifacts\AffinityUi\Windows-Build.log'
Remove-Item Env:\ARKNIGHTS_BUILD_OUTPUT
```

Expected: process exit `0`, Player exists, and build log contains no compilation or build failure marker.

- [ ] **Step 6: Capture and inspect the formal HUD**

Launch the Player visibly at 1920×1080:

```powershell
& 'G:\ARKnoNIGHTS_beta\Artifacts\AffinityUi\Windows\ARKnoNIGHTS.exe' `
  -screen-width 1920 -screen-height 1080 `
  -battleHudCapture `
  -uiCaptureOutput 'G:\ARKnoNIGHTS_beta\Artifacts\AffinityUi\Captures' `
  -logFile 'G:\ARKnoNIGHTS_beta\Artifacts\AffinityUi\Player-Capture.log'
```

Require `17` PNG files, `battle-hud-manifest.json`, `[BattleHudCapture][completed] count=17`, and process exit `0`. Inspect at minimum `02_shop_open.png` and a Preparation screenshot containing staging slots for:

- HeaderLeft icon centering and undistorted aspect;
- shop cost/region/category order;
- cost icon and deployment-cost value;
- Chinese/numeric font distinction;
- no clipping into the name bar or purchase-price panel;
- no stale rows on an empty slot.

- [ ] **Step 7: Record actual results in TEST_PLAN**

Record exact commands, XML totals, failures, skipped counts, exit codes, log paths, screenshot count, inspected filenames, and any unverified manual interaction/DPI cases. Never record skipped, timed-out, missing, or zero-test output as passed.

- [ ] **Step 8: Final diff and external-data safety review**

Run:

```powershell
git diff --check
git status --short
git diff -- Assets/Game Assets/Resources/UI docs scripts
```

Confirm no existing `.meta` GUID changed, no scene/Prefab/package/project settings changed, no generated directory is staged, and all unrelated user changes remain present.
