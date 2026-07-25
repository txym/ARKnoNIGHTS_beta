# Unity Spine Resource Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate existing Unity Spine folders, portraits, JSON filenames, catalog paths, and tests to the `<download-ID>_<english-name>` convention without breaking asset GUID references.

**Architecture:** The project treats the parsed download ID and English model name as the authority for physical resource paths while retaining `resourceKey` only as a legacy logical identifier. A small pure resolver centralizes the canonical folder and portrait names; the catalog generator and old `UnitFactory` consume it, then regenerate `unit-catalog-v1.json` after resource moves preserve `.meta` files.

**Tech Stack:** Unity 2022.3.62f1c1, C#, Unity Test Framework, Spine Unity runtime already in `Assets/Spine`; no package or Unity-version changes.

## Global Constraints

- Do not change Unity, render pipeline, Input System, packages, scenes, Prefabs, or battle rules.
- Preserve every moved Unity asset's `.meta` file and GUID; use `git mv` for tracked asset and metadata pairs after verifying paths.
- Do not overwrite or reformat the current user's unrelated working-tree changes, including untracked `arcslmi` assets and UI-INFO files.
- Canonical current folders are `1000_gopro` and `5503_arcslma`; model files retain `enemy_1000_gopro_3` and `enemy_5503_arcslma` bases.
- `typeId` must match the download ID parsed from the model base; mismatch is an explicit generator error.
- Portrait names are `UIImage_<download-ID>_<english-name>` and original image size remains `158 × 158`.

---

## File Structure

- Create: `Assets/Game/Runtime/Data/Unit/UnitResourceNaming.cs` — canonical folder, portrait, and model-ID resolver independent of Unity objects.
- Modify: `Assets/GameData/Units/UnitJson.cs` — add `resourceFolderName` as the explicit canonical physical folder field.
- Modify: `Assets/Game/Editor/Battle/UnitCatalogGenerator.cs` — validate `resourceFolderName`, validate parsed model ID, and generate canonical Resources paths.
- Modify: `Assets/Game/Runtime/Initial/UnitFactory.cs` — load Spine assets through `resourceFolderName` rather than `resourceKey`.
- Modify: `Assets/Game/Editor/Battle/Task004aSpineProbe.cs` — probe canonical paths.
- Modify: `Assets/Game/Tests/EditMode/Battle/BattleCoreEditModeTests.cs` and `LocalPlayerStateEditModeTests.cs` — update explicit resource-path and source-file assertions.
- Modify: `Assets/GameData/Units/Json/1000_gopro.json` and `5503_arcslma.json` — renamed source JSONs with canonical folder and portrait names.
- Modify: `Assets/Resources/BattleData/unit-catalog-v1.json` — generator output only.
- Move: `Assets/Resources/Characters/gopro/` to `Assets/Resources/Characters/1000_gopro/` and `arcslma/` to `Assets/Resources/Characters/5503_arcslma/`, preserving children and `.meta` files.
- Move: `Assets/Resources/ProfilePicture/UIImage_gopro.png` to `UIImage_1000_gopro.png` and `UIImage_arcslma.png` to `UIImage_5503_arcslma.png`, preserving `.meta` files.
- Modify: `docs/ARCHITECTURE.md`, `docs/TEST_PLAN.md`, `docs/TASK-004.md`, and `docs/TASK-004A.md` — replace stale resource-path examples.

## Task 1: Add canonical resource naming and its tests

**Files:**
- Create: `Assets/Game/Runtime/Data/Unit/UnitResourceNaming.cs`
- Create: `Assets/Game/Tests/EditMode/Unit/UnitResourceNamingEditModeTests.cs`

**Interfaces:**
- Produces `UnitResourceNaming.TryCreate(int typeId, string resourceFolderName, string skeletonDataResourceName, out UnitResourceNames names, out string error)`.
- `UnitResourceNames` exposes `CharactersFolder`, `SkeletonDataResourcePath`, and `PortraitResourceName`.

- [ ] **Step 1: Write failing resolver tests**

```csharp
[Test]
public void TryCreate_UsesCanonicalFolderAndPortraitName() {
    Assert.That(UnitResourceNaming.TryCreate(1000, "1000_gopro", "enemy_1000_gopro_3_SkeletonData", out var names, out var error), Is.True, error);
    Assert.That(names.SkeletonDataResourcePath, Is.EqualTo("Characters/1000_gopro/enemy_1000_gopro_3_SkeletonData"));
    Assert.That(names.PortraitResourceName, Is.EqualTo("UIImage_1000_gopro"));
}

[Test]
public void TryCreate_RejectsFolderIdDifferentFromTypeId() {
    Assert.That(UnitResourceNaming.TryCreate(1000, "5503_gopro", "enemy_1000_gopro_3_SkeletonData", out _, out var error), Is.False);
    Assert.That(error, Does.Contain("folder.id.mismatch"));
}
```

- [ ] **Step 2: Run the resolver test to verify it fails**

Run the project EditMode command from `docs/TEST_PLAN.md`, filtered to `UnitResourceNamingEditModeTests`.

Expected: compilation failure because `UnitResourceNaming` does not exist.

- [ ] **Step 3: Implement the pure naming resolver**

Parse `resourceFolderName` with `^(?<id>[0-9]+)_(?<name>[a-z0-9]+(?:_[a-z0-9]+)*)$`; parse `skeletonDataResourceName` with `^enemy_(?<id>[0-9]+)_(?<name>[a-z0-9]+(?:_[a-z0-9]+)*?)(?:_[0-9]+)?_SkeletonData$`. Require both parsed IDs to equal `typeId` and both English names to equal the folder English name. Return `Characters/<folder>/<skeletonDataResourceName>` and `UIImage_<folder>` only after all checks pass.

- [ ] **Step 4: Run the resolver test to verify it passes**

Run the same filtered EditMode command.

Expected: PASS; valid current model paths resolve and ID/name mismatches produce stable diagnostics.

- [ ] **Step 5: Commit the resolver**

```powershell
git add Assets/Game/Runtime/Data/Unit/UnitResourceNaming.cs Assets/Game/Tests/EditMode/Unit/UnitResourceNamingEditModeTests.cs
git commit -m "feat: centralize unit resource naming"
```

## Task 2: Migrate catalog and legacy loader to the resolver

**Files:**
- Modify: `Assets/GameData/Units/UnitJson.cs`
- Modify: `Assets/Game/Editor/Battle/UnitCatalogGenerator.cs`
- Modify: `Assets/Game/Runtime/Initial/UnitFactory.cs`
- Modify: `Assets/Game/Editor/Battle/Task004aSpineProbe.cs`
- Modify: `Assets/Game/Tests/EditMode/Battle/BattleCoreEditModeTests.cs`
- Modify: `Assets/Game/Tests/EditMode/Battle/LocalPlayerStateEditModeTests.cs`

**Interfaces:**
- Consumes `UnitJson.resourceFolderName` and `UnitResourceNaming.TryCreate`.
- Produces generated `skeletonDataResourcePath = "Characters/<folder>/<skeletonDataResourceName>"` and `portraitResourcePath = "ProfilePicture/UIImage_<folder>"`.

- [ ] **Step 1: Change existing path assertions to their required canonical values**

Replace test expectations with `Characters/1000_gopro/enemy_1000_gopro_3_SkeletonData`, `Characters/5503_arcslma/enemy_5503_arcslma_SkeletonData`, `ProfilePicture/UIImage_1000_gopro`, and `ProfilePicture/UIImage_5503_arcslma`. Add a generator fixture where `typeId` is `1000` and `resourceFolderName` is `5503_gopro`; assert that generation fails before any catalog output is written.

- [ ] **Step 2: Run affected EditMode tests to verify they fail**

Run the `BattleCoreEditModeTests` and `LocalPlayerStateEditModeTests` filters.

Expected: FAIL because existing loaders still use `resourceKey` and the new JSON field is absent.

- [ ] **Step 3: Implement resolver-backed loading**

Add `public string resourceFolderName;` to `UnitJson`. In `UnitCatalogGenerator.Convert`, require it, call `UnitResourceNaming.TryCreate`, and use the returned skeleton/portrait paths. In `UnitFactory`, replace `BuildResPath(j.resourceKey, j.skeletonDataResourceName)` with the resolver and log its diagnostic on failure. Change the probe constants to the two canonical `Characters/...` paths. Do not change `resourceKey` values or animation names.

- [ ] **Step 4: Run affected EditMode tests to verify they pass**

Run the same two filtered test groups.

Expected: PASS; generated paths use the canonical folder and portrait names, and ID mismatch fails deterministically.

- [ ] **Step 5: Commit loader and generator changes**

```powershell
git add Assets/GameData/Units/UnitJson.cs Assets/Game/Editor/Battle/UnitCatalogGenerator.cs Assets/Game/Runtime/Initial/UnitFactory.cs Assets/Game/Editor/Battle/Task004aSpineProbe.cs Assets/Game/Tests/EditMode/Battle/BattleCoreEditModeTests.cs Assets/Game/Tests/EditMode/Battle/LocalPlayerStateEditModeTests.cs
git commit -m "feat: load unit resources by canonical folder"
```

## Task 3: Move existing resources and regenerate the catalog

**Files:**
- Move: `Assets/Resources/Characters/gopro/` and paired `.meta`
- Move: `Assets/Resources/Characters/arcslma/` and paired `.meta`
- Move: `Assets/Resources/ProfilePicture/UIImage_gopro.png` and paired `.meta`
- Move: `Assets/Resources/ProfilePicture/UIImage_arcslma.png` and paired `.meta`
- Move: `Assets/GameData/Units/Json/gopro.json` and paired `.meta`
- Move: `Assets/GameData/Units/Json/arcslma.json` and paired `.meta`
- Modify: the two renamed JSON files and generated `Assets/Resources/BattleData/unit-catalog-v1.json`

**Interfaces:**
- Consumes the Task 2 resolver-backed generator.
- Produces actual Resources paths that match all generated catalog entries.

- [ ] **Step 1: Add a pre-move inventory check**

Run `git status --short` and list each source directory plus its `.meta` file. Stop if any source/destination overlaps the user-owned `arcslmi` directory or if a destination already exists. Record the source GUIDs from the directory and child `.meta` files for post-move comparison.

- [ ] **Step 2: Move assets and source JSONs with metadata**

Use `git mv` for each tracked directory or file and its paired `.meta`: `gopro` to `1000_gopro`, `arcslma` to `5503_arcslma`, `UIImage_gopro.png` to `UIImage_1000_gopro.png`, `UIImage_arcslma.png` to `UIImage_5503_arcslma.png`, and source JSON files to `1000_gopro.json` and `5503_arcslma.json`. Keep every Spine-generated child asset in its model directory.

Update both JSON documents with `resourceFolderName` and their canonical `profilePictureResourceName`; preserve all existing numeric data and animation strings.

- [ ] **Step 3: Verify the move before regenerating**

Compare the saved source GUID values with destination `.meta` files and verify no old path remains in `Assets/Resources/Characters`, `Assets/Resources/ProfilePicture`, or `Assets/GameData/Units/Json`. Check `git diff --check` and inspect the diff for unintended scene, Prefab, Package, or ProjectSettings changes.

- [ ] **Step 4: Regenerate and verify the catalog**

Run Unity with `-executeMethod UnitCatalogGenerator.Generate` using the Editor command template in `docs/TEST_PLAN.md`. Confirm the generated JSON has source files `1000_gopro.json` and `5503_arcslma.json`, canonical model paths, canonical portrait paths, and unchanged `typeId`, animation names, and numeric data.

- [ ] **Step 5: Commit the asset migration**

```powershell
git add Assets/Resources/Characters Assets/Resources/ProfilePicture Assets/GameData/Units/Json Assets/Resources/BattleData/unit-catalog-v1.json
git commit -m "refactor: normalize unit resource folders"
```

## Task 4: Update documentation and run Unity regression evidence

**Files:**
- Modify: `docs/ARCHITECTURE.md`
- Modify: `docs/TEST_PLAN.md`
- Modify: `docs/TASK-004.md`
- Modify: `docs/TASK-004A.md`

**Interfaces:**
- Documents the resolver-backed canonical paths and the separate external download workflow.

- [ ] **Step 1: Write the documentation assertions as a review checklist**

Add the canonical examples `Characters/1000_gopro/...`, `ProfilePicture/UIImage_1000_gopro`, and `Assets/GameData/Units/Json/1000_gopro.json` to the relevant architecture/task documents. State that the source downloader is external, model files use `.skel.bytes` and `.atlas.txt`, and `resourceKey` is not a physical-folder authority.

- [ ] **Step 2: Review stale references before edits**

Run `rg -n 'Characters/(gopro|arcslma)|UIImage_(gopro|arcslma)|Units/Json/(gopro|arcslma)\.json' Assets docs`.

Expected: every remaining occurrence is either a fixture intentionally unrelated to real units or a location identified for replacement.

- [ ] **Step 3: Apply minimal documentation updates**

Replace only the real-unit path examples and migration descriptions. Do not alter unrelated current UI specifications or historical test-result claims.

- [ ] **Step 4: Run regression commands and inspect the final diff**

Run Unity Editor compilation, the relevant EditMode tests, the relevant PlayMode tests, and the `Task004aSpineProbe` menu/execute method. Use the command templates in `docs/TEST_PLAN.md`; save XML/log paths, exit codes, test counts, and the first failure if any. Confirm no new resource-load errors mention the two migrated IDs.

- [ ] **Step 5: Commit docs and verification records**

```powershell
git add docs/ARCHITECTURE.md docs/TEST_PLAN.md docs/TASK-004.md docs/TASK-004A.md
git commit -m "docs: record canonical unit resource paths"
```
