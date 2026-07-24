# PREP-DEPLOY-001 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow a selected deployed unit to move to an empty formation cell or atomically exchange cells with another deployed unit during Preparation.

**Architecture:** `PlayerState.TryRelocateDeployed` remains the sole authority for formation changes and returns the existing `PlayerOperationResult`. `StateDrivenDeploymentController` adds a source-aware drag session for selected deployed views; previews do not mutate state and snapshots reposition views after a one-time commit. Existing selection events keep the HUD bound to the original stable unit ID.

**Tech Stack:** Unity 2022.3.62f1c1, C#, NUnit/Unity Test Framework, existing `scripts/Invoke-UnityTests.ps1`.

## Global Constraints

- Do not modify Unity Editor, Packages, ProjectSettings, Battle Core, unit JSON, `DefaultUnit.prefab`, or `SampleScene.unity`.
- Coordinates are one-based `x=1..9`, `y=1..4`; blue gate `(5,1)` is invalid.
- A no-op succeeds without changing version, firing `Changed`, changing `CanonicalSummary`, or changing deployment cost.
- Real relocation and swap each increment version and raise `Changed` exactly once; failure changes neither state nor cost.
- First world click selects a deployed unit only. A later drag may start only when its view ID equals the selected ID.
- State commands are never issued during preview or cancellation. The existing phase lock, disable, destruction, and reload paths must cancel a preview.

---

### Task 1: Specify and characterize authoritative relocation

**Files:**
- Modify: `docs/SPEC.md` (section 5.2.3)
- Modify: `Assets/Game/Tests/EditMode/Battle/LocalPlayerStateEditModeTests.cs` (after `DeployAndRetreat_AreAtomicUseOneBasedCoordinatesAndTrackCost`)

**Interfaces:**
- Consumes: `PlayerState.TryDeploy(string, int, int)`, `PlayerState.Changed`, `PlayerStateSnapshot`.
- Produces: failing calls to `PlayerState.TryRelocateDeployed(string unitId, int x, int y)` and an exact expected atomic contract.

- [ ] **Step 1: Add the scoped SPEC rules.**

Add a paragraph under `5.2.3` saying that a selected deployed unit may move to an empty legal cell or exchange positions with another own deployed unit; release on its original cell is a successful no-op; out-of-bounds and gate targets fail; all results preserve the original selection and leave deployment cost unchanged. State that this applies only in Preparation and does not change deployment/retreat rules.

- [ ] **Step 2: Write failing EditMode tests.**

Add these tests, with the existing Resources fixture and two deployments to `(4,2)` and `(6,2)`:

```csharp
[Test]
public void RelocateDeployed_MovesToEmptyCell_ChangesOnceAndPreservesCost()
{
    var state = LocalPlayerStateLoader.LoadFromResources(CatalogPath, PlayerStatePath).State;
    Assert.IsTrue(state.TryDeploy("local-1000-alpha", 4, 2).Success);
    var before = state.Snapshot;
    var notifications = 0;
    state.Changed += _ => notifications++;

    var result = state.TryRelocateDeployed("local-1000-alpha", 6, 2);

    Assert.IsTrue(result.Success);
    Assert.AreEqual(before.DeploymentCost, result.Snapshot.DeploymentCost);
    Assert.AreEqual(before.Version + 1, result.Snapshot.Version);
    Assert.AreEqual(1, notifications);
    Assert.AreEqual(new LocalFormationCoordinate(6, 2), result.Snapshot.Units.Single(unit => unit.UnitId == "local-1000-alpha").Formation.Value);
}

[Test]
public void RelocateDeployed_SwapsOccupiedFriendlyCellAtomically()
{
    var state = LocalPlayerStateLoader.LoadFromResources(CatalogPath, PlayerStatePath).State;
    Assert.IsTrue(state.TryDeploy("local-1000-alpha", 4, 2).Success);
    Assert.IsTrue(state.TryDeploy("local-1000-bravo", 6, 2).Success);
    var before = state.Snapshot;
    var notifications = 0;
    state.Changed += _ => notifications++;

    var result = state.TryRelocateDeployed("local-1000-alpha", 6, 2);

    Assert.IsTrue(result.Success);
    Assert.AreEqual(1, notifications);
    Assert.AreEqual(before.Version + 1, result.Snapshot.Version);
    Assert.AreEqual(before.DeploymentCost, result.Snapshot.DeploymentCost);
    Assert.AreEqual(new LocalFormationCoordinate(6, 2), result.Snapshot.Units.Single(unit => unit.UnitId == "local-1000-alpha").Formation.Value);
    Assert.AreEqual(new LocalFormationCoordinate(4, 2), result.Snapshot.Units.Single(unit => unit.UnitId == "local-1000-bravo").Formation.Value);
}
```

Add a third test covering same cell, `(0,2)`, `(5,1)`, a missing ID, and a staging ID. For each, retain pre-call summary/cost/version, assert `notifications == 0`, and assert `Success` only for the same-cell result.

- [ ] **Step 3: Run the focused test to verify it fails.**

Run:

```powershell
./scripts/Invoke-UnityTests.ps1 -Mode EditMode -TestFilter 'ArknoNights.Battle.Tests.LocalPlayerStateEditModeTests'
```

Expected: compilation failure because `TryRelocateDeployed` does not yet exist. Record the terminal output and do not weaken assertions.

- [ ] **Step 4: Commit the specification and failing characterization.**

```powershell
git add docs/SPEC.md Assets/Game/Tests/EditMode/Battle/LocalPlayerStateEditModeTests.cs
git commit -m "test: characterize deployed relocation"
```

### Task 2: Implement the atomic PlayerState command

**Files:**
- Modify: `Assets/Game/Runtime/Data/Player/LocalPlayerState.cs` (after `TryDeploy`)
- Test: `Assets/Game/Tests/EditMode/Battle/LocalPlayerStateEditModeTests.cs`

**Interfaces:**
- Consumes: `unitsById`, `LocalFormationCoordinate`, `NotifyChanged()`, existing `PlayerOperationCode` values.
- Produces: `public PlayerOperationResult TryRelocateDeployed(string unitId, int x, int y)`.

- [ ] **Step 1: Add the minimal command.**

Insert this method after `TryDeploy`; no new result code is required because current codes precisely identify all failures:

```csharp
public PlayerOperationResult TryRelocateDeployed(string unitId, int x, int y)
{
    if (!unitsById.TryGetValue(unitId ?? string.Empty, out var unit)) return Result(PlayerOperationCode.UnitNotFound);
    if (unit.Zone != PlayerUnitZone.Deployed) return Result(PlayerOperationCode.UnitNotDeployed);
    if (!LocalFormationCoordinate.TryCreate(x, y, out var target)) return Result(PlayerOperationCode.CoordinateOutOfBounds);
    if (!LocalFormationCoordinate.IsDeployable(x, y)) return Result(PlayerOperationCode.CoordinateIsGate);
    if (!unit.Formation.HasValue) return Result(PlayerOperationCode.UnitNotDeployed);
    if (unit.Formation.Value.Equals(target)) return Result(PlayerOperationCode.Success);

    var occupant = unitsById.Values.FirstOrDefault(candidate =>
        candidate.Zone == PlayerUnitZone.Deployed &&
        candidate.Formation.HasValue &&
        candidate.Formation.Value.Equals(target));
    if (occupant == null)
    {
        unit.Formation = target;
    }
    else
    {
        var original = unit.Formation.Value;
        unit.Formation = target;
        occupant.Formation = original;
    }
    NotifyChanged();
    return Result(PlayerOperationCode.Success);
}
```

- [ ] **Step 2: Run the focused EditMode test.**

Run the command from Task 1. Expected: the focused `LocalPlayerStateEditModeTests` run has nonzero test count, zero failures, and confirms all added result branches.

- [ ] **Step 3: Inspect the state-only diff.**

Run:

```powershell
git diff --check
git diff -- Assets/Game/Runtime/Data/Player/LocalPlayerState.cs Assets/Game/Tests/EditMode/Battle/LocalPlayerStateEditModeTests.cs
```

Confirm the method performs no deployment-cost, zone, or catalog mutation and calls `NotifyChanged()` exactly once on real movement/swap.

- [ ] **Step 4: Commit the state command.**

```powershell
git add Assets/Game/Runtime/Data/Player/LocalPlayerState.cs Assets/Game/Tests/EditMode/Battle/LocalPlayerStateEditModeTests.cs
git commit -m "feat: relocate deployed player units"
```

### Task 3: Add source-aware selected-unit relocation gestures

**Files:**
- Modify: `Assets/Game/Runtime/Deployment/StateDrivenDeploymentController.cs`
- Modify: `Assets/Game/Tests/PlayMode/Battle/StagingHudScenePlayModeTests.cs`

**Interfaces:**
- Consumes: `TryRelocateDeployed`, `PreparationUnitViewCoordinator.TryGetView`, `PreparationUnitView`, `DeployedSelectionChanged`.
- Produces: selected-only deployed drag start, a restore-on-cancel preview, and a test-only commit path using the relocation command.

- [ ] **Step 1: Add failing PlayMode coverage.**

Add a test that deploys alpha at `(4,2)` and bravo at `(6,2)`, calls `SelectDeployedForTests("local-1000-alpha")`, starts a relocation drag through a new public test helper, moves it to `PreparationGridProjection.ToWorld(new LocalFormationCoordinate(6, 2))`, and commits. Assert alpha and bravo views match the swapped snapshot positions, `SelectedUnitId` remains alpha, cost is unchanged, and `PreparationViewCount` stays two.

Add a second test that begins alpha's selected drag, targets `(5,1)`, commits, and asserts the pre-drag summary and alpha's view position remain unchanged while alpha remains selected. Begin another drag, call `SetInteractionEnabled(false)`, and assert the same invariant and no remaining preview/session.

- [ ] **Step 2: Run the focused PlayMode test to verify it fails.**

Run:

```powershell
./scripts/Invoke-UnityTests.ps1 -Mode PlayMode -TestFilter 'ArknoNights.Battle.Tests.StagingHudScenePlayModeTests'
```

Expected: compilation failure because the selected-relocation helper and gesture path are absent.

- [ ] **Step 3: Model the source and original view position in `PreparationDragSession`.**

Add a `PreparationDragSource` enum with `Staging` and `DeployedRelocation`, then replace the session constructor with:

```csharp
public PreparationDragSession(string unitId, string typeId, PreparationDragSource source, LocalFormationCoordinate? origin)
{
    UnitId = unitId ?? string.Empty;
    TypeId = typeId ?? string.Empty;
    Source = source;
    Origin = origin;
}

public PreparationDragSource Source { get; }
public LocalFormationCoordinate? Origin { get; }
```

Keep `Candidate` and `SetCandidate` unchanged. Update `BeginDragFromSlot` to construct `PreparationDragSource.Staging, null`.

- [ ] **Step 4: Implement the selected-only drag lifecycle.**

Add `BeginRelocateSelectedForTests(string unitId)` that resolves the view and delegates to a private `BeginRelocateSelected(PreparationUnitView view)`. The private method must return false unless initialized, interaction enabled, `State == SelectedDeployed`, `view.PlayerUnitId == selectedUnitId`, its snapshot unit is deployed with a Formation, and no session is active. It creates a deployed-source session and stores the selected view plus its world position, then sets `State = Dragging` without clearing selection.

In `Update`, when `State == SelectedDeployed`, use mouse-down on a raycasted deployed view matching `selectedUnitId` to capture potential drag start; on later movement beyond `EventSystem.current.pixelDragThreshold`, call `BeginRelocateSelected`. Keep mouse-up click processing so the first click still selects only.

Extend `UpdateDragPreview` to move `dragPreview` for `Staging`, but move the saved deployed view transform for `DeployedRelocation`. In `CommitCurrentDrag`, branch exactly as follows:

```csharp
var result = dragSession.Source == PreparationDragSource.DeployedRelocation
    ? (candidate.HasValue
        ? hud.PlayerState.TryRelocateDeployed(dragSession.UnitId, candidate.Value.X, candidate.Value.Y)
        : hud.PlayerState.TryRelocateDeployed(dragSession.UnitId, 0, 0))
    : (candidate.HasValue
        ? hud.PlayerState.TryDeploy(dragSession.UnitId, candidate.Value.X, candidate.Value.Y)
        : hud.PlayerState.TryDeploy(dragSession.UnitId, 0, 0));
```

On any deployed-source failure/no-op and in `CancelDrag`, restore the saved view to `PreparationGridProjection.ToWorld(Origin.Value)`. After every deployed-source completion, resolve the original `selectedUnitId` through the coordinator and re-bind `selectionIndicator`; do not emit a different selection ID. Emit one structured log per result with `relocate.success`, `relocate.swap`, `relocate.noop`, `relocate.invalid`, `relocate.gate`, or `relocate.cancel` and unit/origin/target fields.

- [ ] **Step 5: Preserve phase cleanup.**

Before hiding the coordinator in `SetPreparationViewsVisible(false)`, call `CancelDrag("preparation.views.hidden")`. Retain the existing cancellation calls in `SetInteractionEnabled(false)`, `OnDisable`, `OnDestroy`, focus loss and escape; every cancellation must branch only through restore/cleanup and never call `TryRelocateDeployed`.

- [ ] **Step 6: Run focused and adjacent PlayMode regressions.**

Run the command from Step 2, then:

```powershell
./scripts/Invoke-UnityTests.ps1 -Mode PlayMode -TestFilter 'ArknoNights.Battle.Tests.PreparationBattleLoopPlayModeTests'
```

Expected: each generated XML has a nonzero test count and zero failures. The scene test confirms staged deploy, retreat, selection, phase lock and Preparation→Battle→Preparation continue to work.

- [ ] **Step 7: Commit the interaction implementation.**

```powershell
git add Assets/Game/Runtime/Deployment/StateDrivenDeploymentController.cs Assets/Game/Tests/PlayMode/Battle/StagingHudScenePlayModeTests.cs
git commit -m "feat: drag selected deployed units"
```

### Task 4: Record architecture and complete verification

**Files:**
- Modify: `docs/ARCHITECTURE.md`
- Modify: `docs/TEST_PLAN.md`

**Interfaces:**
- Consumes: final command, tests, Unity logs and test XML.
- Produces: accurate architecture ownership and a dated, evidence-backed validation record.

- [ ] **Step 1: Update architecture documentation.**

Document that `PlayerState.TryRelocateDeployed` owns movement/swap validation and atomic formation changes, while the deployment controller owns source-aware transient sessions and delegates final view placement to snapshots. State that `DeployedSelectionChanged` continues to use the dragged unit's stable ID.

- [ ] **Step 2: Run compilation and full affected test suites.**

Run:

```powershell
D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath G:\ARKnoNIGHTS_beta -logFile G:\ARKnoNIGHTS_beta\Temp\PREP-DEPLOY-001-compile.log
./scripts/Invoke-UnityTests.ps1 -Mode EditMode
./scripts/Invoke-UnityTests.ps1 -Mode PlayMode
```

Expected: compiler exit code `0`, no `error CS`, `Compilation failed`, or `Scripts have compiler errors`; both test runs have nonzero count with zero failed and zero skipped.

- [ ] **Step 3: Perform the required SampleScene manual smoke check if an interactive Unity Editor is available.**

Enter Preparation, select a deployed unit, move it to an empty cell, swap it with another unit, release on the gate, wait for Battle, and return to Preparation. Record each observed selection ID, formation, cost and Console exception state. If no interactive Editor is available, mark all visual gesture items as unverified rather than passed.

- [ ] **Step 4: Update the test plan with actual evidence only.**

Add a dated `PREP-DEPLOY-001` section recording exact commands, exit codes, test counts, XML/log paths, the compile result, manual smoke results, and any unverified visual/build items. Do not claim a test ran without its nonzero result XML or terminal parse.

- [ ] **Step 5: Final diff audit and commit.**

Run:

```powershell
git diff --check
git status --short
git diff -- docs/SPEC.md docs/ARCHITECTURE.md docs/TEST_PLAN.md Assets/Game/Runtime/Data/Player/LocalPlayerState.cs Assets/Game/Runtime/Deployment/StateDrivenDeploymentController.cs Assets/Game/Tests/EditMode/Battle/LocalPlayerStateEditModeTests.cs Assets/Game/Tests/PlayMode/Battle/StagingHudScenePlayModeTests.cs
```

Confirm no serialized scene/prefab, data, package, or ProjectSettings change exists. Then commit only the documentation files:

```powershell
git add docs/ARCHITECTURE.md docs/TEST_PLAN.md
git commit -m "docs: record deployed relocation verification"
```

## Plan self-review

- Spec coverage: Tasks 1–2 cover every PlayerState branch and invariants; Task 3 covers selected-only gestures, transient preview, selection preservation and lifecycle cancellation; Task 4 covers documentation and all required evidence.
- Placeholder scan: no deferred implementation terms or unspecified validations remain; unavailable manual GUI validation is explicitly an unverified outcome.
- Type consistency: `TryRelocateDeployed(string, int, int)` returns existing `PlayerOperationResult`; source enum and `PreparationDragSession` properties are introduced before controller use; no new public UI protocol is required.

