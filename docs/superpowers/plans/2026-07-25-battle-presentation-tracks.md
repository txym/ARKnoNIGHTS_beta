# Battle Presentation Tracks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace forward-only battle-event presentation with deterministic, seekable unit Tracks, preserve the existing single-battle Demo, and add a reusable two-battle shared-clock session that can later be bound to the four-player HUD.

**Architecture:** Core remains the only authority and emits immutable Spawn instance snapshots together with the complete event stream. Presentation compiles each completed result once into read-only unit Tracks, compresses only presentation position keys, and renders one selected Track at an arbitrary shared Tick. The existing single-battle controller becomes a compatibility client of the Track path; a separate multi-battle session computes two inputs once and switches observation without rerunning Core.

**Tech Stack:** Unity 2022.3.62f1c1, C# assemblies, NUnit/EditMode tests, Unity PlayMode tests, existing Spine runtime, existing Built-in Render Pipeline, existing `scripts/Invoke-UnityTests.ps1`.

## Global Constraints

- Read `AGENTS.md`, `docs/SPEC.md`, `docs/ARCHITECTURE.md`, `docs/TEST_PLAN.md`, `docs/UI_TASK_TABLE.md`, and `docs/superpowers/specs/2026-07-25-battle-presentation-tracks-design.md` before editing.
- Execute in an isolated worktree. If only the current shared dirty worktree is available, stop and ask before creating commits or touching files that already contain unrelated changes.
- Do not upgrade Unity, packages, Spine, the render pipeline, or the input system.
- `ARKnoNIGHTS.Battle.Core` must remain free of UnityEngine references.
- Core remains fixed at `20 TPS`; combat calculation must not read Unity Physics, `Time.deltaTime`, render-frame timing, or Spine state.
- Preserve every original `BattleRunResult.Events` item and its Tick/Sequence. Position compression is presentation-only.
- Position compression must produce at most `1 Unity world unit = 1 cm` Euclidean error at every original Move Tick.
- Spawn, Attack, BlockStarted, BlockEnded, Death, BattleEnded, movement start, movement stop, and final position must remain exact.
- Actions are `Death > Attack > Move > Idle`; Damage updates HP only and the Track playback path must never call `PlayHit()`.
- Dynamic units reference an existing Type ID, carry an immutable instance snapshot, and use per-battle canonical IDs `-1`, `-2`, `-3`, and so on.
- Initial player units must not use IDs that parse as negative integers.
- Dynamic IDs are unique only inside one battle; complete identity is `(BattleId, UnitId)` and `-1` may be reused in another battle.
- Track and presentation objects must never write combat HP, death, position, or winner back to `PlayerState`.
- Do not implement summon skills, Buff calculation, shield calculation, healing, remote units, networking, player-life settlement, shop behavior, or four-player HUD widgets in this plan.
- Do not modify `SampleScene`, Prefabs, ScriptableObjects, packages, or project settings. The existing scene must keep working through compatible script APIs.
- New `.cs` resources must retain Unity-generated `.meta` files once imported; never regenerate or replace existing GUIDs.
- Every verification result must be reported as passed, failed, or unverified from actual output. Zero tests is not a pass.

---

## File Structure and Responsibilities

### Core contracts

- `Assets/Game/Battle/Core/Events/BattleUnitInstanceSnapshot.cs`
  - Immutable Spawn-time instance attributes used by Track compilation.
- `Assets/Game/Battle/Core/Events/BattleEvents.cs`
  - Keeps the existing event fields and adds the optional Spawn snapshot.
- `Assets/Game/Battle/Core/Simulation/DynamicUnitIdAllocator.cs`
  - Allocates canonical per-battle negative IDs without defining any summon behavior.
- `Assets/Game/Battle/Core/Simulation/BattleRunner.cs`
  - Emits complete initial Spawn snapshots and self-contained battle/player IDs in `BattleRunResult`.
- `Assets/Game/Battle/Core/Input/BattleInput.cs`
  - Rejects negative numeric IDs in initial player snapshots.
- `Assets/Game/Battle/Core/Properties/AssemblyInfo.cs`
  - Exposes Core internals only to the two existing battle test assemblies so synthetic dynamic-Spawn results can be tested without making event constructors public.

### Presentation Tracks

- `Assets/Game/Battle/Presentation/Tracks/BattlePresentationTrack.cs`
  - Read-only battle/unit Track models, keys, samples, summaries, and compression metrics.
- `Assets/Game/Battle/Presentation/Tracks/BattlePresentationTrackCompiler.cs`
  - Validates one completed result and compiles lifecycle, action, facing, HP, shield, and uncompressed position data.
- `Assets/Game/Battle/Presentation/Tracks/PositionTrackCompressor.cs`
  - Deterministic time-parameterized position simplification and error measurement.
- `Assets/Game/Battle/Presentation/Playback/BattleTrackPlaybackController.cs`
  - Binds one selected Track to Unity views and renders arbitrary presentation Ticks.
- `Assets/Game/Battle/Presentation/Playback/BattleEventPlaybackController.cs`
  - Compatibility façade preserving the current single-battle Play/Pause/Replay API while delegating to Tracks.
- `Assets/Game/Battle/Presentation/Playback/BattlePresentationContracts.cs`
  - Adds Idle and instance MaxHP to the view contract, and Battle/Unit identity to diagnostics.
- `Assets/Game/Battle/Presentation/Projection/BattlefieldWorldProjection.cs`
  - Projects continuous Track positions and logical horizontal facing for Home/Away observers.

### Unity view bridge

- `Assets/Game/Runtime/Data/Unit/UnitSkelBase.cs`
  - Exposes the existing serialized default animation as a presentation-only Idle command.
- `Assets/Game/Runtime/Data/Unit/UnitSkelPresentationView.cs`
  - Implements Idle, accepts instance MaxHP, and retains `PlayHit()` only as a legacy interface method.
- `Assets/Game/Runtime/Initial/MappedBattlePresentationViewFactory.cs`
  - Continues resolving Prefab/Skeleton/animation mappings from Type ID; no longer treats catalog MaxHP as authoritative for Track status bars.

### Demo and multi-battle session

- `Assets/Game/Battle/Demo/BattleDemoCoordinator.cs`
  - Keeps the existing single-battle public behavior on the Track-backed compatibility façade.
- `Assets/Game/Battle/Demo/MultiBattlePresentationCoordinator.cs`
  - Computes multiple immutable inputs once, owns one shared clock, maps player IDs to `(BattleId, Observer)`, and binds only the selected Track.

### Tests

- `Assets/Game/Tests/EditMode/Battle/BattleCoreEditModeTests.cs`
  - Core Spawn snapshot and ID reservation/allocator tests.
- `Assets/Game/Tests/EditMode/Battle/BattlePresentationTrackEditModeTests.cs`
  - Track contract, lifecycle, actions, diagnostics, dynamic Spawn, and final-state tests.
- `Assets/Game/Tests/EditMode/Battle/PositionTrackCompressorEditModeTests.cs`
  - Compression error, forced-key, gap, and determinism tests.
- `Assets/Game/Tests/EditMode/Battle/BattleDemoCoordinatorEditModeTests.cs`
  - Existing single-demo API regression after migration.
- `Assets/Game/Tests/EditMode/Battle/MultiBattlePresentationCoordinatorEditModeTests.cs`
  - Two-result computation, shared clock, observation mapping, and completion tests.
- `Assets/Game/Tests/PlayMode/Battle/BattlePresentationPlaybackPlayModeTests.cs`
  - Real `DefaultUnit`/Spine Track playback, Idle/Attack/Death, no Hit, status bar, pause, and cleanup.
- `Assets/Game/Tests/PlayMode/Battle/BattleDemoCoordinatorPlayModeTests.cs`
  - Existing `SampleScene` single-demo regression.
- `Assets/Game/Tests/PlayMode/Battle/MultiBattlePresentationCoordinatorPlayModeTests.cs`
  - Cross-frame shared-clock switching with one active set of view objects.

This plan intentionally does not bind `MultiBattlePresentationCoordinator` to a concrete player-list component. `UI-006` and `UI-008` do not exist in the current repository, so their concrete roster/selection interfaces must be integrated by a later scene task after those prerequisites land.

---

### Task 1: Make Spawn Results Self-Contained and Reserve Dynamic IDs

**Files:**

- Create: `Assets/Game/Battle/Core/Events/BattleUnitInstanceSnapshot.cs`
- Create: `Assets/Game/Battle/Core/Simulation/DynamicUnitIdAllocator.cs`
- Create: `Assets/Game/Battle/Core/Properties/AssemblyInfo.cs`
- Modify: `Assets/Game/Battle/Core/Events/BattleEvents.cs`
- Modify: `Assets/Game/Battle/Core/Input/BattleInput.cs`
- Modify: `Assets/Game/Battle/Core/Simulation/BattleRunner.cs`
- Test: `Assets/Game/Tests/EditMode/Battle/BattleCoreEditModeTests.cs`

**Interfaces:**

- Consumes: existing `UnitDefinition`, `UnitSnapshot`, `RuntimeUnitState`, `BattleEvent`, and `BattleRunResult`.
- Produces:

```csharp
public sealed class BattleUnitInstanceSnapshot
{
    public string UnitId { get; }
    public string TypeId { get; }
    public string PlayerId { get; }
    public BattleSide Side { get; }
    public bool IsDynamicallyGenerated { get; }
    public FixedPosition Position { get; }
    public int EliteLevel { get; }
    public int MaxHitPoints { get; }
    public int CurrentHitPoints { get; }
    public int CurrentShield { get; }
    public int Attack { get; }
    public int Defense { get; }
    public int MagicResistance { get; }
    public int MoveSpeedCentimetresPerSecond { get; }
    public int AttackIntervalTicks { get; }
    public int AttackAnimationDurationTicks { get; }
    public DamageType DamageType { get; }
    public AttackMethod AttackMethod { get; }
    public int BlockCapacity { get; }
    public int TauntLevel { get; }
    public IReadOnlyList<BuffPlaceholder> Buffs { get; }
}

public sealed class DynamicUnitIdAllocator
{
    public string Allocate();
}

public sealed class BattleRunResult
{
    // Existing result properties remain.
    public string BattleId { get; }
    public string HomePlayerId { get; }
    public string AwayPlayerId { get; }
    public string InputCanonicalSummary { get; }
    public IReadOnlyList<string> KnownUnitTypeIds { get; }
}
```

- Adds `BattleEvent.SpawnSnapshot`.
- Adds `BattleRunResult.BattleId`, `HomePlayerId`, `AwayPlayerId`, `InputCanonicalSummary`, and the read-only stable list `KnownUnitTypeIds`.
- Does not add any API that creates a summoned unit or mutates a completed result.

- [ ] **Step 1: Add failing Core tests for the complete Spawn contract**

Add tests with these assertions:

```csharp
[Test]
public void SpawnEvents_CarryImmutableInstanceSnapshotsAndResultIdentity()
{
    var loaded = LocalBattleLoader.LoadFromResources(
        "BattleData/unit-catalog-v1",
        "BattleData/task004a-real-1v1");
    Assert.IsTrue(loaded.Success, string.Join(" | ", loaded.Errors));

    var result = new BattleRunner(loaded.Input).RunToCompletion();
    Assert.AreEqual(loaded.Input.BattleId, result.BattleId);
    Assert.AreEqual(
        loaded.Input.Players.Single(player => player.Side == BattleSide.Home).PlayerId,
        result.HomePlayerId);
    Assert.AreEqual(
        loaded.Input.Players.Single(player => player.Side == BattleSide.Away).PlayerId,
        result.AwayPlayerId);

    foreach (var spawn in result.Events.Where(item => item.Type == BattleEventType.Spawn))
    {
        Assert.NotNull(spawn.SpawnSnapshot);
        Assert.AreEqual(spawn.UnitId, spawn.SpawnSnapshot.UnitId);
        Assert.AreEqual(spawn.UnitTypeId, spawn.SpawnSnapshot.TypeId);
        Assert.AreEqual(spawn.UnitSide, spawn.SpawnSnapshot.Side);
        Assert.AreEqual(spawn.ToPosition, spawn.SpawnSnapshot.Position);
        Assert.Greater(spawn.SpawnSnapshot.MaxHitPoints, 0);
        Assert.AreEqual(spawn.SpawnSnapshot.MaxHitPoints, spawn.SpawnSnapshot.CurrentHitPoints);
        Assert.AreEqual(0, spawn.SpawnSnapshot.CurrentShield);
    }
}
```

Add two more tests:

```csharp
[Test]
public void InitialInput_RejectsAnyNegativeNumericUnitId()
{
    var definition = new UnitDefinition(
        "unit", 100, 10, 0, 0, 100, 20, 20,
        DamageType.Physical, AttackMethod.Melee, 1, false);
    var specification = new BattleInputSpecification(
        BattleInput.LocalBattleSchemaVersion,
        "negative-initial-id",
        20,
        new[] { definition },
        new[]
        {
            new PlayerSnapshot("home", BattleSide.Home, new[]
            {
                new UnitSnapshot("-01", "unit", UnitZone.Deployed,
                    new FormationCoordinate(4, 2), Array.Empty<BuffPlaceholder>())
            }),
            new PlayerSnapshot("away", BattleSide.Away, new[]
            {
                new UnitSnapshot("away-1", "unit", UnitZone.Deployed,
                    new FormationCoordinate(4, 2), Array.Empty<BuffPlaceholder>())
            })
        });

    Assert.IsFalse(BattleInputFactory.TryCreate(
        specification, out _, out var errors));
    Assert.That(errors, Has.Some.Matches<ValidationError>(
        error => error.Code == "unitId.reserved.dynamic"));
}

[Test]
public void DynamicUnitIdAllocator_IsPerBattleCanonicalAndDeterministic()
{
    var firstBattle = new DynamicUnitIdAllocator();
    CollectionAssert.AreEqual(new[] { "-1", "-2", "-3" },
        new[] { firstBattle.Allocate(), firstBattle.Allocate(), firstBattle.Allocate() });

    var secondBattle = new DynamicUnitIdAllocator();
    Assert.AreEqual("-1", secondBattle.Allocate());
}
```

For the negative-ID test, construct `BattleInputSpecification` directly and assert:

```csharp
Assert.IsFalse(BattleInputFactory.TryCreate(specification, out _, out var errors));
Assert.That(errors, Has.Some.Matches<ValidationError>(
    error => error.Code == "unitId.reserved.dynamic"));
```

- [ ] **Step 2: Run the focused Core tests and confirm the intended failures**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests.BattleCoreEditModeTests' `
  -OutputDirectory 'Temp/TRACKS/Task1-Red' `
  -NoGraphics
```

Expected: non-zero exit; failures identify missing `SpawnSnapshot`, missing result identity, missing reserved-ID validation, and missing allocator. If Unity reports compile errors before tests, record those exact diagnostics as the red result.

- [ ] **Step 3: Add the immutable instance snapshot and event/result identity**

Implement `BattleUnitInstanceSnapshot` with a constructor that copies Buffs into a `ReadOnlyCollection<BuffPlaceholder>`. Modify `RuntimeUnitState` to retain `EliteLevel` and Buffs from the source `UnitSnapshot`. `EmitSpawn` must construct exactly one snapshot from the runtime instance, set `IsDynamicallyGenerated=false` for the existing input-built units, and attach it to the Spawn event. The future generated-unit path will be required to pass `true`; this task does not add that path.

Use this event compatibility shape:

```csharp
public sealed class BattleEvent
{
    // Existing properties remain unchanged.
    public BattleUnitInstanceSnapshot SpawnSnapshot { get; }
}
```

All non-Spawn events pass `null`. Spawn keeps the existing `UnitTypeId`, `UnitSide`, `ToPosition`, and HP fields so existing diagnostics and summaries do not break abruptly. Track compilation will later validate that duplicated fields agree with the snapshot.

At result construction, derive player IDs from the already validated input:

```csharp
var homePlayerId = Input.Players.Single(player => player.Side == BattleSide.Home).PlayerId;
var awayPlayerId = Input.Players.Single(player => player.Side == BattleSide.Away).PlayerId;
var knownUnitTypeIds = Input.UnitDefinitions
    .Select(definition => definition.TypeId)
    .OrderBy(typeId => typeId, StringComparer.Ordinal)
    .ToArray();
return new BattleRunResult(
    Input.BattleId,
    homePlayerId,
    awayPlayerId,
    knownUnitTypeIds,
    CurrentTick,
    StopReason,
    Winner,
    immutableTrace,
    immutableEvents,
    finalUnits,
    BuildStableSummary());
```

- [ ] **Step 4: Implement the reserved-ID validation and allocator**

Reject an initial ID whenever invariant integer parsing succeeds and the value is negative:

```csharp
private static bool IsReservedDynamicUnitId(string value)
{
    return long.TryParse(
        value,
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out var parsed) && parsed < 0;
}
```

Use diagnostic code `unitId.reserved.dynamic`. Implement the allocator with a private `long next = -1`; `Allocate()` returns invariant text and decrements. Throw `InvalidOperationException` before decrementing `long.MinValue`.

`AssemblyInfo.cs` must contain only:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ARKnoNIGHTS.Battle.EditModeTests")]
[assembly: InternalsVisibleTo("ARKnoNIGHTS.Battle.PlayModeTests")]
```

- [ ] **Step 5: Run focused and full Core regressions**

Run the focused command from Step 2 with output directory `Temp/TRACKS/Task1-Green`, then run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests' `
  -OutputDirectory 'Temp/TRACKS/Task1-AllEditMode' `
  -NoGraphics
```

Expected: both commands report `result=Passed`, total greater than zero, failed `0`; `CoreAssembly_HasNoUnityEngineAssemblyReference` remains green.

- [ ] **Step 6: Review and commit Task 1**

Check:

```powershell
git diff --check
git status --short
```

Confirm no JSON, scene, Prefab, package, project setting, or existing `.meta` was altered. Commit only Task 1 files:

```powershell
git add `
  Assets/Game/Battle/Core/Events/BattleUnitInstanceSnapshot.cs `
  Assets/Game/Battle/Core/Events/BattleUnitInstanceSnapshot.cs.meta `
  Assets/Game/Battle/Core/Events/BattleEvents.cs `
  Assets/Game/Battle/Core/Input/BattleInput.cs `
  Assets/Game/Battle/Core/Properties/AssemblyInfo.cs `
  Assets/Game/Battle/Core/Properties/AssemblyInfo.cs.meta `
  Assets/Game/Battle/Core/Properties.meta `
  Assets/Game/Battle/Core/Simulation/DynamicUnitIdAllocator.cs `
  Assets/Game/Battle/Core/Simulation/DynamicUnitIdAllocator.cs.meta `
  Assets/Game/Battle/Core/Simulation/BattleRunner.cs `
  Assets/Game/Tests/EditMode/Battle/BattleCoreEditModeTests.cs
git commit -m "feat: add battle spawn instance snapshots"
```

---

### Task 2: Compile Completed Results into Seekable Unit Tracks

**Files:**

- Create: `Assets/Game/Battle/Presentation/Tracks/BattlePresentationTrack.cs`
- Create: `Assets/Game/Battle/Presentation/Tracks/BattlePresentationTrackCompiler.cs`
- Create: `Assets/Game/Tests/EditMode/Battle/BattlePresentationTrackEditModeTests.cs`
- Modify: `Assets/Game/Battle/Presentation/Playback/BattlePresentationContracts.cs`

**Interfaces:**

- Consumes: Task 1 `BattleRunResult` and `BattleEvent.SpawnSnapshot`.
- Produces:

```csharp
public enum UnitPresentationAction { Idle, Move, Attack, Death }

public readonly struct PresentationPosition
{
    public double XUnits { get; }
    public double YUnits { get; }
}

public readonly struct UnitPresentationSample
{
    public bool HasSpawned { get; }
    public bool IsAlive { get; }
    public bool ShouldDisplay { get; }
    public PresentationPosition Position { get; }
    public int HorizontalFacing { get; } // -1 or +1 in logical Home coordinates
    public UnitPresentationAction Action { get; }
    public int ActionStartTick { get; }
    public int ActionSequence { get; }
    public float AttackAnimationSpeedMultiplier { get; }
    public int MaxHitPoints { get; }
    public int CurrentHitPoints { get; }
    public int CurrentShield { get; }
}

public sealed class UnitPresentationTrack
{
    public string UnitId { get; }
    public string TypeId { get; }
    public BattleSide Side { get; }
    public int EliteLevel { get; }
    public int SpawnTick { get; }
    public int? DeathTick { get; }
    public int MaxHitPoints { get; }
    public UnitPresentationSample Sample(double tick);
}

public sealed class BattlePresentationTrack
{
    public string BattleId { get; }
    public string HomePlayerId { get; }
    public string AwayPlayerId { get; }
    public int EndTick { get; }
    public BattleSide? Winner { get; }
    public BattleStopReason StopReason { get; }
    public IReadOnlyList<UnitPresentationTrack> Units { get; }
    public string SourceInputDigest { get; }
    public string SourceEventDigest { get; }
    public string SourceResultDigest { get; }
    public string StableSummary { get; }
    public bool TryGetUnit(string unitId, out UnitPresentationTrack track);
}

public sealed class BattlePresentationTrackCompiler
{
    public bool TryCompile(
        BattleRunResult result,
        out BattlePresentationTrack track,
        out IReadOnlyList<BattlePresentationDiagnostic> diagnostics);
}
```

- Extend `BattlePresentationDiagnostic` with read-only `BattleId` and `UnitId` while keeping the existing constructor overload source-compatible:

```csharp
public BattlePresentationDiagnostic(
    string code,
    string message,
    int tick = -1,
    int sequence = -1)
    : this(code, message, string.Empty, string.Empty, tick, sequence)
{
}

public BattlePresentationDiagnostic(
    string code,
    string message,
    string battleId,
    string unitId,
    int tick = -1,
    int sequence = -1)
{
    Code = code ?? string.Empty;
    Message = message ?? string.Empty;
    BattleId = battleId ?? string.Empty;
    UnitId = unitId ?? string.Empty;
    Tick = tick;
    Sequence = sequence;
}
```

- [ ] **Step 1: Write failing lifecycle, action, and diagnostic tests**

Create `BattlePresentationTrackEditModeTests` with at least these named tests:

```csharp
[Test] public void Compile_ProducesStableSeekableLifecycleHpAndFinalState();
[Test] public void Compile_ActionPriorityIsDeathAttackMoveIdleAndDamageCreatesNoAction();
[Test] public void Compile_AttackUsesOriginalOverEffectiveAnimationMultiplier();
[Test] public void Compile_HomeAndAwayShareTheSameLogicalTrack();
[Test] public void Compile_AcceptsSpawnAtANonZeroTickWhenSnapshotIsComplete();
[Test] public void Compile_RejectsPreSpawnReferenceDuplicateSpawnAndUnknownType();
[Test] public void Compile_RejectsSpawnSnapshotMismatchAndFinalStateMismatch();
[Test] public void Compile_RepeatedRunsProduceIdenticalTrackSummary();
```

The action test must make these checks against the existing combat fixture:

```csharp
var attack = result.Events.First(item => item.Type == BattleEventType.Attack);
var unit = track.Units.Single(item => item.UnitId == attack.UnitId);
var during = unit.Sample(attack.Tick + 0.25);
Assert.AreEqual(UnitPresentationAction.Attack, during.Action);
Assert.AreEqual(
    (float)attack.OriginalAnimationTicks / attack.EffectiveAnimationTicks,
    during.AttackAnimationSpeedMultiplier,
    0.0001f);

var damage = result.Events.First(item => item.Type == BattleEventType.Damage);
var damaged = track.Units.Single(item => item.UnitId == damage.RelatedUnitId);
Assert.AreEqual(damage.HitPointsAfter, damaged.Sample(damage.Tick).CurrentHitPoints);
Assert.AreNotEqual("Hit", damaged.Sample(damage.Tick).Action.ToString());
```

Use Task 1 internals visibility to build a synthetic result containing a valid non-zero-Tick Spawn with Unit ID `-1`, followed by Move and BattleEnded. Do not add a summon method to `BattleRunner`.

- [ ] **Step 2: Run the Track test class and capture the red result**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests.BattlePresentationTrackEditModeTests' `
  -OutputDirectory 'Temp/TRACKS/Task2-Red' `
  -NoGraphics
```

Expected: non-zero exit because the Track types and compiler do not exist.

- [ ] **Step 3: Implement immutable Track models and sampling**

Keep model collections in `ReadOnlyCollection<T>`. Use double only for presentation interpolation; all stored source keys remain integer Tick and integer `FixedPosition`.

Sampling semantics:

```text
tick < SpawnTick                     => HasSpawned=false, ShouldDisplay=false
tick >= SpawnTick and before Death   => HasSpawned=true, IsAlive=true, ShouldDisplay=true
tick >= DeathTick                    => HasSpawned=true, IsAlive=false, ShouldDisplay=true, Action=Death
tick > EndTick                       => clamp to EndTick
```

HP keys use the final event sequence at a Tick. CurrentShield starts from the Spawn snapshot and remains available as its own key channel; current Core has no shield-change event, so no later shield key is synthesized.

Action segments:

```text
Attack: [Attack.Tick, Attack.Tick + EffectiveAnimationTicks)
Move:   each contiguous position-motion interval
Death:  [Death.Tick, +infinity)
Idle:   otherwise after Spawn
```

When action intervals overlap, resolve `Death > Attack > Move > Idle`. Preserve Attack Tick and Sequence in the sample so two adjacent attacks restart even though both have action type Attack.

- [ ] **Step 4: Implement compiler validation and raw uncompressed positions**

The compiler must:

1. Validate non-null result, Battle ID, player IDs, event order, and contiguous per-Tick Sequence.
2. Create a unit builder only from Spawn.
3. Validate Spawn duplicated fields against `SpawnSnapshot`.
4. Validate positive MaxHP, `0 <= CurrentHP <= MaxHP`, non-negative shield, known side/type, and unique Unit ID.
5. Validate Type ID membership against `BattleRunResult.KnownUnitTypeIds`.
6. Require `IsDynamicallyGenerated=true` snapshots to use a canonical negative integer ID; require `IsDynamicallyGenerated=false` snapshots not to use a negative integer ID.
7. Reject any non-Spawn reference before that unit's Spawn Tick/Sequence.
8. Apply Move, Attack, Damage, Death, and BattleEnded in event order.
9. Require exactly one BattleEnded event and validate its Tick, Winner, and StopReason against `BattleRunResult`.
10. Build one exact position key per original Move endpoint plus the movement-start position.
11. Validate every Move `FromPosition` equals the unit's current event-derived position.
12. Validate Track final position, HP, alive state, type, side, and winner against `BattleRunResult.FinalUnits`.
13. Sort unit Tracks by Spawn Tick, Spawn Sequence, then ordinal Unit ID.

Use stable diagnostic codes:

```text
track.result.missing
track.identity.invalid
track.event.order.invalid
track.event.sequence.invalid
track.spawn.contract.invalid
track.spawn.snapshot.missing
track.spawn.snapshot.mismatch
track.spawn.dynamicId.invalid
track.spawn.duplicate
track.unit.beforeSpawn
track.unit.unknown
track.move.contract.invalid
track.move.discontinuous
track.attack.contract.invalid
track.damage.contract.invalid
track.death.duplicate
track.battleEnded.invalid
track.finalState.mismatch
track.winner.mismatch
```

Every unit-specific diagnostic must populate Battle ID, Unit ID, Tick, and Sequence.

Compute `SourceInputDigest`, `SourceEventDigest`, `SourceResultDigest`, and `StableSummary` with an invariant 32-bit FNV-1a hex digest, matching the existing `BattleDemoCoordinator` fingerprint behavior. The input digest uses `BattleRunResult.InputCanonicalSummary`; the event digest includes every event field and every Spawn snapshot field in Tick/Sequence order.

- [ ] **Step 5: Run Track tests and all presentation EditMode regressions**

Run the Step 2 command with `Temp/TRACKS/Task2-Green`, then:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests.BattlePresentation' `
  -OutputDirectory 'Temp/TRACKS/Task2-PresentationRegression' `
  -NoGraphics
```

Expected: Track tests pass; existing event-player tests still compile and pass because no playback behavior has been migrated yet.

- [ ] **Step 6: Review and commit Task 2**

Run `git diff --check`. Confirm Track classes contain no GameObject, MonoBehaviour, Physics, Time, Spine, or PlayerState reference. Commit:

```powershell
git add `
  Assets/Game/Battle/Presentation/Tracks.meta `
  Assets/Game/Battle/Presentation/Tracks/BattlePresentationTrack.cs `
  Assets/Game/Battle/Presentation/Tracks/BattlePresentationTrack.cs.meta `
  Assets/Game/Battle/Presentation/Tracks/BattlePresentationTrackCompiler.cs `
  Assets/Game/Battle/Presentation/Tracks/BattlePresentationTrackCompiler.cs.meta `
  Assets/Game/Battle/Presentation/Playback/BattlePresentationContracts.cs `
  Assets/Game/Tests/EditMode/Battle/BattlePresentationTrackEditModeTests.cs `
  Assets/Game/Tests/EditMode/Battle/BattlePresentationTrackEditModeTests.cs.meta
git commit -m "feat: compile battle results into presentation tracks"
```

---

### Task 3: Compress Position Tracks with a Deterministic 1 cm Bound

**Files:**

- Create: `Assets/Game/Battle/Presentation/Tracks/PositionTrackCompressor.cs`
- Create: `Assets/Game/Tests/EditMode/Battle/PositionTrackCompressorEditModeTests.cs`
- Modify: `Assets/Game/Battle/Presentation/Tracks/BattlePresentationTrack.cs`
- Modify: `Assets/Game/Battle/Presentation/Tracks/BattlePresentationTrackCompiler.cs`
- Test: `Assets/Game/Tests/EditMode/Battle/BattlePresentationTrackEditModeTests.cs`

**Interfaces:**

- Consumes: Task 2 raw integer-Tick position keys and forced exact Tick set.
- Produces:

```csharp
public sealed class BattlePresentationCompressionMetrics
{
    public int OriginalMoveCount { get; }
    public int PositionKeyCount { get; }
    public double CompressionRatio { get; }
    public double MaximumErrorUnits { get; }
}

internal sealed class PositionTrackCompressor
{
    internal const int MaximumErrorUnits = 1;

    internal bool TryCompress(
        IReadOnlyList<RawPositionPoint> points,
        ISet<int> forcedExactTicks,
        out IReadOnlyList<PositionSegment> segments,
        out long maximumSquaredErrorNumerator,
        out BattlePresentationDiagnostic diagnostic);
}
```

- Adds `BattlePresentationTrack.CompressionMetrics`.
- Does not remove or rewrite any Core Move event.

- [ ] **Step 1: Write failing compression tests**

Create these tests:

```csharp
[Test] public void StraightRun_CollapsesToOneSegmentAndKeepsEveryOriginalTickWithinOneUnit();
[Test] public void OneUnitDeviationMayCollapseButTwoUnitDeviationCreatesAKey();
[Test] public void ForcedTicksRemainExactEvenWhenTheyAreCollinear();
[Test] public void StationaryGapAlwaysSplitsMovementRuns();
[Test] public void AttackBlockDeathBattleEndAndFinalPositionAreForcedExact();
[Test] public void Compression_IsDeterministicAcrossTenCompilations();
[Test] public void Compression_DoesNotChangeSourceEventsOrFinalResult();
```

For the straight-run assertion:

```csharp
Assert.That(track.CompressionMetrics.OriginalMoveCount, Is.GreaterThan(20));
Assert.That(track.CompressionMetrics.PositionKeyCount,
    Is.LessThan(track.CompressionMetrics.OriginalMoveCount));
Assert.That(track.CompressionMetrics.MaximumErrorUnits, Is.LessThanOrEqualTo(1d));
```

At every source Move Tick, sample the compressed unit Track and compare it with `Move.ToPosition`:

```csharp
var dx = sample.Position.XUnits - move.ToPosition.Value.XUnits;
var dy = sample.Position.YUnits - move.ToPosition.Value.YUnits;
Assert.That(dx * dx + dy * dy, Is.LessThanOrEqualTo(1d));
```

- [ ] **Step 2: Run the compressor tests and capture the red result**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests.PositionTrackCompressorEditModeTests' `
  -OutputDirectory 'Temp/TRACKS/Task3-Red' `
  -NoGraphics
```

Expected: non-zero exit because compression and metrics are absent.

- [ ] **Step 3: Implement contiguous movement-run extraction**

For a run of Move events with consecutive Ticks:

```text
first point = (firstMove.Tick - 1, firstMove.FromPosition)
next points = (eachMove.Tick, eachMove.ToPosition)
```

Start a new run if either condition is true:

```text
currentMove.Tick != previousMove.Tick + 1
currentMove.FromPosition != previousMove.ToPosition
```

The second condition is still a hard `track.move.discontinuous` error. The Tick gap is valid stationary time and must produce separate segments.

Build the forced exact Tick set from:

```text
Spawn Tick
Attack Tick
BlockStarted Tick
BlockEnded Tick
Death Tick
BattleEnded Tick
each run start Tick
each run end Tick
final-state Tick
```

When a forced Tick lies inside a movement run, insert the raw position at that Tick before simplification.

- [ ] **Step 4: Implement integer-rational error comparison**

For candidate endpoints `(t0, x0, y0)` and `(t1, x1, y1)`, evaluate raw point `(t, x, y)` without platform floating-point decisions:

```csharp
var duration = (long)t1 - t0;
var elapsed = (long)t - t0;
var expectedXNumerator = (long)x0 * duration + ((long)x1 - x0) * elapsed;
var expectedYNumerator = (long)y0 * duration + ((long)y1 - y0) * elapsed;
var dxNumerator = (long)x * duration - expectedXNumerator;
var dyNumerator = (long)y * duration - expectedYNumerator;
var squaredErrorNumerator =
    checked(dxNumerator * dxNumerator + dyNumerator * dyNumerator);
var allowedSquaredNumerator =
    checked(duration * duration * MaximumErrorUnits * MaximumErrorUnits);
```

If any point exceeds the bound, split at the point with the largest normalized error. For equal normalized error, choose the earliest Tick. Forced points split before error-driven recursion. Use `checked` arithmetic and return `track.position.overflow` instead of wrapping.

Sampling a retained segment may use double interpolation because it is presentation-only; all decisions about which keys survive must use the integer-rational comparison above.

- [ ] **Step 5: Add metrics, exact-key validation, and stable summary data**

After compression:

1. Resample every original Move Tick.
2. Assert maximum Euclidean error is at most `1`.
3. Assert every forced Tick has zero error.
4. Assert no segment spans a stationary Tick gap.
5. Add original Move count, retained key count, invariant compression ratio, and maximum error to Track stable summary.

If validation fails, return `track.position.error.exceeded` or `track.position.forcedKey.inexact` and do not return a partial Track.

- [ ] **Step 6: Run focused, Track, and full EditMode tests**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests.PositionTrackCompressorEditModeTests' `
  -OutputDirectory 'Temp/TRACKS/Task3-Compressor' `
  -NoGraphics

powershell -ExecutionPolicy Bypass -File scripts/Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests.BattlePresentationTrackEditModeTests' `
  -OutputDirectory 'Temp/TRACKS/Task3-Tracks' `
  -NoGraphics

powershell -ExecutionPolicy Bypass -File scripts/Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests' `
  -OutputDirectory 'Temp/TRACKS/Task3-AllEditMode' `
  -NoGraphics
```

Expected: all three runs pass with non-zero test counts and zero failures.

- [ ] **Step 7: Review and commit Task 3**

Confirm the diff contains no edit to `BattleRunner` Move emission or existing event lists. Commit:

```powershell
git add `
  Assets/Game/Battle/Presentation/Tracks/BattlePresentationTrack.cs `
  Assets/Game/Battle/Presentation/Tracks/BattlePresentationTrackCompiler.cs `
  Assets/Game/Battle/Presentation/Tracks/PositionTrackCompressor.cs `
  Assets/Game/Battle/Presentation/Tracks/PositionTrackCompressor.cs.meta `
  Assets/Game/Tests/EditMode/Battle/BattlePresentationTrackEditModeTests.cs `
  Assets/Game/Tests/EditMode/Battle/PositionTrackCompressorEditModeTests.cs `
  Assets/Game/Tests/EditMode/Battle/PositionTrackCompressorEditModeTests.cs.meta
git commit -m "feat: compress presentation position tracks"
```

---

### Task 4: Render Tracks and Migrate the Existing Single-Battle Demo

**Files:**

- Create: `Assets/Game/Battle/Presentation/Playback/BattleTrackPlaybackController.cs`
- Modify: `Assets/Game/Battle/Presentation/Playback/BattleEventPlaybackController.cs`
- Modify: `Assets/Game/Battle/Presentation/Playback/BattlePresentationContracts.cs`
- Modify: `Assets/Game/Battle/Presentation/Projection/BattlefieldWorldProjection.cs`
- Modify: `Assets/Game/Battle/Demo/BattleDemoCoordinator.cs`
- Modify: `Assets/Game/Runtime/Data/Unit/UnitSkelBase.cs`
- Modify: `Assets/Game/Runtime/Data/Unit/UnitSkelPresentationView.cs`
- Modify: `Assets/Game/Runtime/Initial/MappedBattlePresentationViewFactory.cs`
- Modify: all existing fake `IBattlePresentationView` implementations under `Assets/Game/Tests`
- Test: `Assets/Game/Tests/EditMode/Battle/BattlePresentationEditModeTests.cs`
- Test: `Assets/Game/Tests/EditMode/Battle/BattleDemoCoordinatorEditModeTests.cs`
- Test: `Assets/Game/Tests/PlayMode/Battle/BattlePresentationPlaybackPlayModeTests.cs`
- Test: `Assets/Game/Tests/PlayMode/Battle/BattleDemoCoordinatorPlayModeTests.cs`

**Interfaces:**

- Consumes: Task 3 `BattlePresentationTrack`.
- Produces:

```csharp
public interface IBattlePresentationView
{
    void SetWorldPosition(Vector3 position);
    void SetFacing(Vector3 direction);
    void SetPlaybackSpeed(float playbackSpeed);
    void PlayIdle();
    void PlayMove();
    void PlayAttack(float animationSpeedMultiplier);
    void PlayHit(); // compatibility only; Track path never calls it
    void PlayDeath();
    void SetStatusBarState(
        string unitId,
        bool isEnemy,
        int maxHitPoints,
        int currentHitPoints,
        int currentShield);
}

public sealed class BattlePresentationViewState
{
    // Existing identity and rounded FixedPosition properties remain compatible.
    public string UnitId { get; }
    public string TypeId { get; }
    public BattleSide Side { get; }
    public FixedPosition Position { get; }
    public PresentationPosition ContinuousPosition { get; }
    public int MaxHitPoints { get; }
    public int HitPoints { get; }
    public int CurrentShield { get; }
    public bool HasSpawned { get; }
    public bool IsAlive { get; }
    public UnitPresentationAction Action { get; }
    public int EliteLevel { get; }
}

public sealed class BattleTrackPlaybackController : IDisposable
{
    public BattlePresentationTrack Track { get; }
    public BattleObserverView Observer { get; }
    public double PresentationTick { get; }
    public IReadOnlyList<BattlePresentationViewState> ViewStates { get; }
    public IReadOnlyList<BattlePresentationDiagnostic> Diagnostics { get; }

    public bool Bind(
        BattlePresentationTrack track,
        IBattlePresentationViewFactory factory,
        BattleObserverView observer,
        double presentationTick,
        out IReadOnlyList<BattlePresentationDiagnostic> diagnostics);

    public bool RenderAt(
        double presentationTick,
        out IReadOnlyList<BattlePresentationDiagnostic> diagnostics);

    public bool SetObserver(
        BattleObserverView observer,
        double presentationTick,
        out IReadOnlyList<BattlePresentationDiagnostic> diagnostics);

    public void SetPlaybackSpeed(float playbackSpeed);
    public void Clear();
}
```

- `BattleEventPlaybackController` keeps its existing public `Load`, `Play`, `Pause`, `Resume`, `Advance`, `SetObserver`, `SetPlaybackSpeed`, `Replay`, `StopAndClear`, and observable properties.
- `BattleDemoCoordinator` keeps its existing public controller-facing API.

- [ ] **Step 1: Update tests first for Idle, instance MaxHP, no Hit, and arbitrary binding**

Change fake views to record commands and the five status-bar inputs. Add or update tests so they assert:

```csharp
CollectionAssert.DoesNotContain(fake.Commands, "hit");
Assert.That(fake.Commands, Has.Some.EqualTo("idle"));
Assert.That(fake.StatusStates, Has.Some.Matches<string>(
    value => value.Contains("|" + spawn.SpawnSnapshot.MaxHitPoints + "|")));
```

Add these Track renderer cases:

```csharp
[Test] public void TrackPlayback_BindAtMiddleCreatesOnlySpawnedUnitsAndRestoresCurrentAction();
[Test] public void TrackPlayback_DynamicSpawnCreatesViewOnlyWhenSharedTickReachesSpawn();
[Test] public void TrackPlayback_ObserverSwitchReprojectsWithoutChangingTrackOrCore();
[Test] public void TrackPlayback_DamageUpdatesHpWithoutHitAndDoesNotInterruptAttack();
[Test] public void TrackPlayback_BackwardRenderRebindsRatherThanLeavingFutureUnitsVisible();
```

In PlayMode, use the real catalog to assert that switching away and back restarts the current action type, but does not change result/event digests.

- [ ] **Step 2: Run focused EditMode and PlayMode tests for the red result**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests.BattlePresentationEditModeTests' `
  -OutputDirectory 'Temp/TRACKS/Task4-EditMode-Red' `
  -NoGraphics

powershell -ExecutionPolicy Bypass -File scripts/Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform PlayMode `
  -TestFilter 'ArknoNights.Battle.Tests.BattlePresentationPlaybackPlayModeTests' `
  -OutputDirectory 'Temp/TRACKS/Task4-PlayMode-Red' `
  -NoGraphics
```

Expected: failures identify the missing Track renderer and changed view contract.

- [ ] **Step 3: Implement continuous Home/Away projection**

Add overloads that accept `PresentationPosition` and logical horizontal facing. Preserve the existing `FixedPosition` APIs.

The continuous mapping must be:

```text
Home: (x, y)
Away: (1000 - x, 900 - y)
World: (projectedX, 0, projectedY)
```

The constants follow the existing one-based `9 × 8` center transform. Away horizontal facing is the negation of Home horizontal facing. Pure logical vertical movement does not change the stored horizontal facing.

- [ ] **Step 4: Implement the one-selected-Track renderer**

`Bind` disposes all old views, stores the new Track/factory/observer, and calls `RenderAt(presentationTick)`.

`RenderAt` must:

1. Reject negative or NaN Tick with `track.playback.tick.invalid`.
2. If Tick moved backward, clear views and reconstruct from the Track at that Tick.
3. Create a view only when `sample.HasSpawned` is true.
4. Fail the entire binding on duplicate identity, missing Type ID mapping, or null view.
5. Apply projected position every render call.
6. Apply facing only when the sampled logical horizontal facing changes or observer changes.
7. Apply the full status state, including instance MaxHP, when any value or observer enemy relation changes.
8. Invoke an animation command when `(Action, ActionStartTick, ActionSequence)` changes.
9. Call `PlayIdle`, `PlayMove`, `PlayAttack(multiplier)`, or `PlayDeath` according to the sample.
10. Never call `PlayHit`.

Keep dead unit views bound so Death can remain visible until the battle presentation is cleared. On a missing Death animation, retain the existing local hide fallback.

- [ ] **Step 5: Adapt the real unit bridge**

Add to `UnitSkelBase`:

```csharp
public bool PlayDefaultPresentationAnimation()
{
    return !string.IsNullOrEmpty(defaultAnimation) &&
           PlayPresentationAnimation(defaultAnimation, defaultLoop, 1f);
}
```

Add `UnitSkelPresentationView.PlayIdle()` delegating to that method. Change `SetStatusBarState` to pass the provided MaxHP directly to `UnitWorldStatusBar.SetState`.

Keep `ConfigureStatusBarMaximumHitPoints` temporarily source-compatible for any non-Track caller, but the Track path must not depend on its catalog value. `MappedBattlePresentationViewFactory` continues to resolve Type ID, Prefab, SkeletonData, `unitSkeletonType`, and move/attack/hit/death names. It must not overwrite Track instance MaxHP after view creation.

- [ ] **Step 6: Rewrite the old event player as a Track-backed compatibility façade**

On `Load`:

1. Compile the result once.
2. Bind the Track at Tick `0`.
3. Preserve old load diagnostics and observable state.

On `Advance`:

```csharp
displayTicks += unscaledDeltaSeconds * PlaybackSpeed * BattleInput.TicksPerSecond;
displayTicks = Math.Min(displayTicks, track.EndTick);
renderer.RenderAt(displayTicks, out diagnostics);
```

Derive `ConsumedEventCount` from the immutable source event list:

```csharp
events.Count(item => item.Tick <= Math.Floor(displayTicks))
```

Completion occurs when `displayTicks >= track.EndTick`. Pause sets real view playback speed to `0`; resume restores the configured speed. Replay clears/rebinds the same already-compiled Track at Tick `0`; it must not run Core or compile a different result.

Keep `BattleDemoCoordinator` signatures stable. Update its event digest to include Spawn snapshot identity and instance HP so two differing instance snapshots cannot produce the same digest.

- [ ] **Step 7: Run focused, full EditMode, and full PlayMode verification**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests' `
  -OutputDirectory 'Temp/TRACKS/Task4-AllEditMode' `
  -NoGraphics

powershell -ExecutionPolicy Bypass -File scripts/Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform PlayMode `
  -TestFilter 'ArknoNights.Battle.Tests' `
  -OutputDirectory 'Temp/TRACKS/Task4-AllPlayMode' `
  -NoGraphics
```

Then compile without tests:

```powershell
& 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -batchmode -nographics -quit `
  -projectPath 'G:\ARKnoNIGHTS_beta' `
  -logFile 'G:\ARKnoNIGHTS_beta\Temp\TRACKS\Task4-Compile.log'
```

Expected: non-zero test totals, failed `0`, compile process exit `0`, no `error CS`, `Compilation failed`, or unhandled exception. `SampleScene_BattleDemoRootRunsTheRealCatalogToCompletion` remains green without scene changes.

- [ ] **Step 8: Perform the minimal manual presentation check**

If Unity GUI is available:

1. Open `SampleScene`.
2. Enter Play Mode.
3. Let the fixed battle begin.
4. Pause and resume.
5. Switch Home/Away during Move and Attack.
6. Confirm the action restarts from its current type after switching.
7. Confirm Damage changes the bar without a Hit animation.
8. Confirm death and final winner remain consistent.
9. Replay twice and exit Play Mode.
10. Confirm no duplicate `BattleView_*` objects or errors remain.

If GUI is unavailable, report all ten items as unverified; do not infer them from unit tests.

- [ ] **Step 9: Review and commit Task 4**

Run `git diff --check` and verify no serialized asset changed. Commit:

```powershell
git add `
  Assets/Game/Battle/Presentation/Playback/BattleTrackPlaybackController.cs `
  Assets/Game/Battle/Presentation/Playback/BattleTrackPlaybackController.cs.meta `
  Assets/Game/Battle/Presentation/Playback/BattleEventPlaybackController.cs `
  Assets/Game/Battle/Presentation/Playback/BattlePresentationContracts.cs `
  Assets/Game/Battle/Presentation/Projection/BattlefieldWorldProjection.cs `
  Assets/Game/Battle/Demo/BattleDemoCoordinator.cs `
  Assets/Game/Runtime/Data/Unit/UnitSkelBase.cs `
  Assets/Game/Runtime/Data/Unit/UnitSkelPresentationView.cs `
  Assets/Game/Runtime/Initial/MappedBattlePresentationViewFactory.cs `
  Assets/Game/Tests/EditMode/Battle/BattlePresentationEditModeTests.cs `
  Assets/Game/Tests/EditMode/Battle/BattleDemoCoordinatorEditModeTests.cs `
  Assets/Game/Tests/PlayMode/Battle/BattlePresentationPlaybackPlayModeTests.cs `
  Assets/Game/Tests/PlayMode/Battle/BattleDemoCoordinatorPlayModeTests.cs
git commit -m "feat: play completed battles from presentation tracks"
```

---

### Task 5: Add the Two-Battle Shared-Clock Presentation Session

**Files:**

- Create: `Assets/Game/Battle/Demo/MultiBattlePresentationCoordinator.cs`
- Create: `Assets/Game/Tests/EditMode/Battle/MultiBattlePresentationCoordinatorEditModeTests.cs`
- Create: `Assets/Game/Tests/PlayMode/Battle/MultiBattlePresentationCoordinatorPlayModeTests.cs`
- Modify: `docs/ARCHITECTURE.md`
- Modify: `docs/TEST_PLAN.md`
- Modify: `docs/UI_TASK_TABLE.md`

**Interfaces:**

- Consumes: validated immutable `BattleInput` values, Task 4 Track compiler/renderer, and existing view factory.
- Produces:

```csharp
public sealed class BattleMatchRequest
{
    public string MatchId { get; }
    public BattleInput Input { get; }
}

public readonly struct PlayerBattleObservation
{
    public string PlayerId { get; }
    public string MatchId { get; }
    public BattleObserverView Observer { get; }
}

public sealed class BattleMatchPresentation
{
    public string MatchId { get; }
    public BattleInput Input { get; }
    public BattleRunResult Result { get; }
    public BattlePresentationTrack Track { get; }
    public string WinnerOrReason { get; }
}

public enum MultiBattlePresentationState
{
    Idle,
    Ready,
    Playing,
    Paused,
    Completed,
    Error
}

public sealed class MultiBattlePresentationCoordinator : IDisposable
{
    public MultiBattlePresentationState State { get; }
    public double PresentationTick { get; }
    public float Speed { get; }
    public string SelectedPlayerId { get; }
    public string SelectedMatchId { get; }
    public BattleObserverView Observer { get; }
    public IReadOnlyList<BattleMatchPresentation> Matches { get; }
    public string LastError { get; }
    public int CompletionTransitionCount { get; }

    public bool Prepare(
        IReadOnlyList<BattleMatchRequest> requests,
        IReadOnlyList<PlayerBattleObservation> observations,
        IBattlePresentationViewFactory factory,
        string initiallyObservedPlayerId);

    public bool Play();
    public bool Pause();
    public bool Resume();
    public bool Replay();
    public bool SetSpeed(float speed);
    public bool SelectObservedPlayer(string playerId);
    public void Advance(float unscaledDeltaSeconds);
    public void Reset();
}
```

- [ ] **Step 1: Write failing two-battle session tests**

Create EditMode tests:

```csharp
[Test] public void Prepare_ComputesEachInputOnceAndBuildsTwoDifferentTracks();
[Test] public void FixedFourPlayerMapSelectsMatchAndHomeAwayObserver();
[Test] public void SwitchingAtCurrentTickDoesNotRunCoreOrRestartTheTrack();
[Test] public void EarlierFinishedMatchHoldsItsFinalState();
[Test] public void AllTracksFinishedTransitionsCompletedExactlyOnce();
[Test] public void ReplayUsesExistingInputsAndResultsAndReturnsToTickZero();
[Test] public void InvalidMappingOrOneFailedTrackMakesWholeSessionError();
```

Use the confirmed map:

```csharp
var observations = new[]
{
    new PlayerBattleObservation("player-1", "match-ab", BattleObserverView.Home),
    new PlayerBattleObservation("player-2", "match-ab", BattleObserverView.Away),
    new PlayerBattleObservation("player-3", "match-cd", BattleObserverView.Home),
    new PlayerBattleObservation("player-4", "match-cd", BattleObserverView.Away)
};
```

Build two different `BattleInput` objects with different battle IDs and unit IDs. Assert switching never changes `Matches[index].Result` object identity, StableSummary, or event digest.

Create a PlayMode test that advances across frames, switches `player-1 → player-3 → player-2 → player-4`, and asserts factory-created minus disposed views equals only the currently bound Track's spawned unit count.

- [ ] **Step 2: Run focused tests and capture the red result**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests.MultiBattlePresentationCoordinatorEditModeTests' `
  -OutputDirectory 'Temp/TRACKS/Task5-EditMode-Red' `
  -NoGraphics
```

Expected: non-zero exit because the multi-battle session types do not exist.

- [ ] **Step 3: Implement one-time computation and mapping validation**

`Prepare` must:

1. Reset previous views/session state.
2. Require at least one request, unique non-empty Match IDs, unique non-null BattleInput objects, unique Battle IDs, one factory, and one observation per player ID.
3. Require every observation Match ID to exist.
4. Require each observation player ID to equal the Home or Away player ID of the selected result according to its observer.
5. Run `new BattleRunner(input).RunToCompletion()` exactly once per request.
6. Compile each result exactly once.
7. If any result or Track fails, clear all session data and enter Error with the failing Match/Battle diagnostic.
8. Bind only the initially observed player's Track at Tick `0`.
9. Enter Ready.

Store `BattleMatchPresentation` objects in request order and the observation lookup with ordinal string comparison.

- [ ] **Step 4: Implement the shared clock and observation switching**

The coordinator owns exactly one `double presentationTick`.

```csharp
presentationTick +=
    unscaledDeltaSeconds * Speed * BattleInput.TicksPerSecond;
presentationTick = Math.Min(presentationTick, maximumEndTick);
```

`SelectObservedPlayer`:

1. Looks up `(MatchId, Observer)`.
2. If selecting the already bound pair, updates only `SelectedPlayerId`.
3. Otherwise binds that Track at the current shared Tick.
4. Does not construct a runner, compile a Track, reset Tick, or modify any result.

When one Track ends before the maximum Tick, sampling clamps it to its final state. When the shared Tick reaches the maximum EndTick, set Completed once and increment `CompletionTransitionCount` only on that state transition.

Pause sets active view speed to `0` without changing Tick. Replay reuses the same immutable inputs/results/Tracks, resets shared Tick to `0`, rebinds the selected player, and enters Playing.

- [ ] **Step 5: Add stable structured summaries and errors**

Expose a deterministic session summary containing:

```text
matchId
battleId
input canonical digest
source event digest
result digest
track stable summary
winner or unresolved reason
selected player/match/observer
shared tick/speed/state
```

Use stable error prefixes:

```text
multi.requests.missing
multi.matchId.invalid
multi.matchId.duplicate
multi.battleId.duplicate
multi.observation.invalid
multi.observation.playerMismatch
multi.core.failed
multi.track.failed
multi.playback.bind.failed
multi.playback.render.failed
```

The failing Match ID and Battle ID must appear in `LastError`.

- [ ] **Step 6: Run focused and full automated verification**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests.MultiBattlePresentationCoordinatorEditModeTests' `
  -OutputDirectory 'Temp/TRACKS/Task5-MultiEditMode' `
  -NoGraphics

powershell -ExecutionPolicy Bypass -File scripts/Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform PlayMode `
  -TestFilter 'ArknoNights.Battle.Tests.MultiBattlePresentationCoordinatorPlayModeTests' `
  -OutputDirectory 'Temp/TRACKS/Task5-MultiPlayMode' `
  -NoGraphics

powershell -ExecutionPolicy Bypass -File scripts/Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -TestFilter 'ArknoNights.Battle.Tests' `
  -OutputDirectory 'Temp/TRACKS/Task5-AllEditMode' `
  -NoGraphics

powershell -ExecutionPolicy Bypass -File scripts/Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform PlayMode `
  -TestFilter 'ArknoNights.Battle.Tests' `
  -OutputDirectory 'Temp/TRACKS/Task5-AllPlayMode' `
  -NoGraphics
```

Then run the existing Windows build entry:

```powershell
& 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -batchmode -nographics -quit `
  -projectPath 'G:\ARKnoNIGHTS_beta' `
  -executeMethod Task006StandaloneBuild.BuildWindowsX64 `
  -logFile 'G:\ARKnoNIGHTS_beta\Temp\TRACKS\Task5-WindowsBuild.log'
```

Expected: four test commands pass with totals greater than zero and failures `0`; build log reports `Succeeded` and errors `0`. If a Unity test process remains only after a valid XML was written, use the existing wrapper's bounded shutdown behavior and report it.

- [ ] **Step 7: Update current-state and test documents**

Update `docs/ARCHITECTURE.md` only with behavior actually implemented and verified:

- Spawn snapshots and reserved dynamic IDs;
- read-only Track compiler/compressor;
- Track-backed single Demo;
- multi-battle shared-clock coordinator;
- current lack of concrete player-list/SampleScene multi-battle binding.

Update `docs/TEST_PLAN.md` with exact commands, test totals, failures, skipped counts, logs, build result, and any unverified GUI behavior.

Update `docs/UI_TASK_TABLE.md`:

- mark the library portion of UI-009 complete only if all corresponding tests passed;
- leave four-player roster sealing, player-list binding, stage-return wiring, and screenshot acceptance pending;
- do not mark UI-009 or UI-010 fully complete from library tests alone.

- [ ] **Step 8: Final self-review and commit Task 5**

Run:

```powershell
git diff --check
git status --short
```

Confirm:

- no scene, Prefab, JSON, ScriptableObject, package, or project setting changed;
- exactly one renderer owns views;
- switching does not rerun Core;
- no Track code calls `PlayHit`;
- Core events remain intact;
- test reports contain non-zero counts;
- any missing manual GUI result is labeled unverified.

Commit:

```powershell
git add `
  Assets/Game/Battle/Demo/MultiBattlePresentationCoordinator.cs `
  Assets/Game/Battle/Demo/MultiBattlePresentationCoordinator.cs.meta `
  Assets/Game/Tests/EditMode/Battle/MultiBattlePresentationCoordinatorEditModeTests.cs `
  Assets/Game/Tests/EditMode/Battle/MultiBattlePresentationCoordinatorEditModeTests.cs.meta `
  Assets/Game/Tests/PlayMode/Battle/MultiBattlePresentationCoordinatorPlayModeTests.cs `
  Assets/Game/Tests/PlayMode/Battle/MultiBattlePresentationCoordinatorPlayModeTests.cs.meta `
  docs/ARCHITECTURE.md `
  docs/TEST_PLAN.md `
  docs/UI_TASK_TABLE.md
git commit -m "feat: add shared-clock multi-battle presentation"
```

---

## Final Acceptance Checklist

- [ ] Every initial Spawn contains a complete immutable instance snapshot.
- [ ] Initial negative numeric Unit IDs are rejected with `unitId.reserved.dynamic`.
- [ ] Two new allocators each begin at `-1`; one allocator returns `-1`, `-2`, `-3` deterministically.
- [ ] A valid non-zero-Tick dynamic Spawn with an existing Type ID compiles into a Track.
- [ ] Pre-Spawn references, duplicate IDs, unknown Type IDs, missing snapshots, and final-state mismatches produce structured diagnostics containing Battle ID, Unit ID, Tick, and Sequence.
- [ ] Same completed input/result compiles to the same Track summary across ten runs.
- [ ] All Core Move events remain unchanged.
- [ ] Every original Move Tick is within `1 cm`; forced keys have zero error; stationary gaps are not bridged.
- [ ] Track samples expose Spawn, position, facing, `Idle/Move/Attack/Death`, MaxHP, CurrentHP, CurrentShield, alive, and visibility.
- [ ] Damage changes HP but never calls Hit or interrupts Attack.
- [ ] Existing single-battle Start/Pause/Resume/Replay/Home/Away behavior is Track-backed and remains compatible.
- [ ] Real `DefaultUnit` views still resolve `unitSkeletonType` and move/attack/death names from the catalog.
- [ ] Two battle inputs are each run once and compile to two independent Tracks.
- [ ] Player 1/2 map to MatchAB Home/Away and Player 3/4 map to MatchCD Home/Away.
- [ ] Switching observation samples the selected Track at the current shared Tick without rerunning Core.
- [ ] Earlier-finished Tracks remain in their final state; overall completion occurs once at the maximum EndTick.
- [ ] No combat result writes back to PlayerState.
- [ ] Full EditMode, full PlayMode, Editor compile, and Windows Standalone results are recorded from actual runs.
- [ ] Concrete four-player HUD/SampleScene binding remains explicitly pending until UI-006 and UI-008 exist.

## Stop Conditions

Stop and ask the project owner before proceeding if any of these occurs:

- `docs/SPEC.md` or the approved Track design contradicts an existing test expectation in player-visible behavior.
- Supporting dynamic Spawn requires implementing a concrete summon/skill rule rather than only its data contract.
- A dynamic unit needs a Type ID that is absent from the catalog.
- Instance attributes cannot be represented without changing unit JSON or introducing an unconfirmed Buff calculation.
- The `1 cm` bound can only be achieved by changing or deleting Core Move events.
- Idle playback requires adding a new unit JSON animation field instead of using the existing serialized `defaultAnimation`.
- Compatibility requires deleting `BattleDemoController`, changing `SampleScene`, or replacing existing Prefab GUIDs.
- A new package, Unity version, render pipeline, external tool, network service, account, key, or project-external persistent write is required.
- UI-006/UI-008 integration is requested before their concrete roster and observation interfaces exist.
- Three materially different attempts fail to produce a valid Unity test result or resolve the same implementation blocker.

## Completion Report Format

The implementing Agent must report:

1. Modified and created files.
2. Implemented interfaces and behavior.
3. Actual commands and Unity operations executed.
4. Test totals, passed/failed/skipped counts, exit status, and log/result paths.
5. Editor compile and Windows build result.
6. Manual checks actually performed.
7. Items not verified and why.
8. Assumptions made.
9. Remaining risks and compatibility notes.
10. Whether UI-006/UI-008 prerequisites are ready for the later concrete four-player HUD integration.
