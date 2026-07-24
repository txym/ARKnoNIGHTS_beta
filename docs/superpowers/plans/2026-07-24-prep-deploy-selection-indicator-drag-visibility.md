# PREP-DEPLOY Selection Indicator Drag Visibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Hide `DeployedUnitSelectionIndicator` while a selected deployed unit is being relocated, then restore and rebind it when the original unit remains selected after the drag ends.

**Architecture:** `StateDrivenDeploymentController` owns the selection indicator and the transient relocation session. No player state, grid, scene, prefab, or deployment rule changes are needed.

**Tech Stack:** Unity 2022.3.62f1c1, C#, Unity Test Framework, NUnit, `scripts/Invoke-UnityTests.ps1`.

## Scope and constraints

- Keep the indicator hidden from successful relocation drag start until the operation has completed and the selected view has been restored from the authoritative snapshot.
- Restore it for successful move/swap, no-op, invalid/gate result, UI-release cancellation, focus cancellation, and ordinary cancellation, provided interaction is still enabled and the original unit remains selected.
- Do not revive it during phase lock, retreat, controller disable, view hiding, or destruction; those paths retain their existing clear-selection behavior.
- Add no serialized assets or packages, and leave `PlayerState` unchanged.

### Task 1: Characterize the visual lifecycle in PlayMode

**Files:**
- Modify: `Assets/Game/Tests/PlayMode/Battle/StagingHudScenePlayModeTests.cs`

1. Add a focused scene test that deploys and selects alpha, verifies `DeployedUnitSelectionIndicator` is active, begins relocation, and verifies it becomes inactive before commit.
2. Complete a successful relocation/swap and assert the indicator is active again and remains bound to alpha.
3. Start subsequent drags that end in an invalid gate result and UI release cancellation; assert the indicator is restored and alpha remains selected each time.
4. Run the focused scene test before production code. The new indicator assertion must fail with the current controller.

### Task 2: Implement minimal controller visibility handling

**Files:**
- Modify: `Assets/Game/Runtime/Deployment/StateDrivenDeploymentController.cs`

1. Hide the existing indicator when `BeginRelocateSelected` successfully creates a deployed-relocation session.
2. Add one private restore-and-rebind helper that activates the indicator only when the existing selected ID resolves to a current deployed view.
3. Invoke that helper after successful or failed relocation completion and ordinary cancellation; guard it so disabled and phase-cleanup paths cannot recreate visible selection state.
4. Preserve the existing staging drag behavior, snapshot-driven view positions, selection events, and selection clearing lifecycle.

### Task 3: Document and verify

**Files:**
- Modify: `docs/SPEC.md`
- Modify: `docs/TEST_PLAN.md`

1. Add the scoped player-visible rule to the existing Preparation relocation behavior.
2. Record exact focused PlayMode result, fresh Unity compile result, changed-file diff audit, and any unavailable manual mouse visual check.
3. Run `git diff --check`, the focused `StagingHudScenePlayModeTests`, and a batchmode Unity compile. Inspect the generated test summary before reporting success.

## Verification commands

```powershell
./scripts/Invoke-UnityTests.ps1 -UnityPath D:\2022.3.62f1c1\Editor\Unity.exe -TestPlatform PlayMode -TestFilter 'ArknoNights.Battle.Tests.StagingHudScenePlayModeTests' -ProjectPath G:\ARKnoNIGHTS_beta -OutputDirectory G:\ARKnoNIGHTS_beta\Temp\PREP-DEPLOY-001-indicator -NoGraphics
D:\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath G:\ARKnoNIGHTS_beta -logFile G:\ARKnoNIGHTS_beta\Temp\PREP-DEPLOY-001-indicator-compile.log
git diff --check
git status --short
```

## Plan self-review

- The plan changes only the controller-owned UI visibility, with explicit lifecycle guards.
- The PlayMode test observes the requested behavior through the real scene controller.
- Existing phase-lock, retreat, disable, and destruction paths are explicitly preserved.
