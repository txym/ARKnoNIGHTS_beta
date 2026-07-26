# Unit Death Presentation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 连续播放跨过 Death 时完整播放死亡动画，动画完成后用 `0.5` 秒把 Spine 身体变黑并隐藏；从任意 Track 节点切入时不创建已经死亡的单位。

**Architecture:** `UnitPresentationTrack.Sample` 负责提供切入 Tick 的 `ShouldDisplay`，`BattleTrackPlaybackController` 只在创建新视图时应用该值，保证连续播放中的现存单位仍能收到 Death。真实视图从 `UnitSkelBase` 取得本次 Spine `TrackEntry`，监听其 `Complete` 后用现实时间驱动 Skeleton 顶点色，最终只停用 GameObject；播放器仍统一负责销毁。

**Tech Stack:** Unity 2022.3.62f1c1、C#、Spine Unity runtime、Built-in Render Pipeline、Unity Test Framework/NUnit、仓库 `scripts/Invoke-UnityTests.ps1`

## Global Constraints

- 直接在 `G:\ARKnoNIGHTS_beta` 主工作区和当前 `txym` 分支实施，不创建 worktree。
- 保留用户未跟踪的 `.superpowers/` 与 `docs/bonds/`，不得加入提交。
- 不修改 Core、Death 事件格式、目录 JSON、场景、Prefab、Shader、Package 或 Unity 版本。
- `0.5` 秒使用 `Time.unscaledDeltaTime`，属于显示层现实时间，不写入 Core/Track。
- 状态条不参与变黑，随单位 GameObject 一起隐藏。
- 缺少死亡动画时继续记录现有警告并立即隐藏。
- 同一路径不得同时运行多个 Unity Editor/batchmode 进程；所有 Unity 测试串行执行。
- 每个代码任务严格执行 Red → Green → Refactor，并在提交前检查相关测试与 diff。

---

### Task 1: Track 切入过滤已死亡单位

**Files:**
- Modify: `Assets/Game/Battle/Presentation/Tracks/BattlePresentationTrack.cs`
- Modify: `Assets/Game/Battle/Presentation/Playback/BattleTrackPlaybackController.cs`
- Test: `Assets/Game/Tests/EditMode/Battle/BattlePresentationTrackEditModeTests.cs`
- Test: `Assets/Game/Tests/EditMode/Battle/BattlePresentationEditModeTests.cs`

**Interfaces:**
- Consumes: `UnitPresentationTrack.DeathTick`、`UnitPresentationSample.HasSpawned`、`BattleTrackPlaybackController.Bind(...)`
- Produces: 死亡后点采样的 `UnitPresentationSample.ShouldDisplay == false`；创建新视图时遵守 `ShouldDisplay`

- [ ] **Step 1: 写入死亡后点采样的失败测试**

在 `Compile_ActionPriorityIsDeathAttackMoveIdleAndDamageCreatesNoAction` 中补充死亡前后可见性断言：

```csharp
Assert.IsTrue(dead.Sample(death.Tick - 0.01d).ShouldDisplay);
Assert.IsFalse(dead.Sample(death.Tick).IsAlive);
Assert.IsFalse(dead.Sample(death.Tick).ShouldDisplay);
Assert.IsFalse(dead.Sample(track.EndTick).ShouldDisplay);
```

- [ ] **Step 2: 写入切入与连续死亡的失败测试**

在 `BattlePresentationEditModeTests` 新增：

```csharp
[Test]
public void TrackPlayback_CutInSkipsDeadUnitsButContinuousPlaybackStillPlaysDeath()
{
    var result = RunFixture();
    var compiler = new BattlePresentationTrackCompiler();
    Assert.That(compiler.TryCompile(result, out var track, out var diagnostics), Is.True, string.Join(";", diagnostics));
    var death = result.Events.First(item => item.Type == BattleEventType.Death);

    var cutInFactory = new FakeFactory();
    using (var cutIn = new BattleTrackPlaybackController())
    {
        Assert.That(cutIn.Bind(track, cutInFactory, BattleObserverView.Home, death.Tick, out var bindDiagnostics),
            Is.True, string.Join(";", bindDiagnostics));
        Assert.That(cutInFactory.Contains(death.UnitId), Is.False);
    }

    var continuousFactory = new FakeFactory();
    using (var continuous = new BattleTrackPlaybackController())
    {
        Assert.That(continuous.Bind(track, continuousFactory, BattleObserverView.Home, death.Tick - 0.01d, out var bindDiagnostics),
            Is.True, string.Join(";", bindDiagnostics));
        Assert.That(continuousFactory.Contains(death.UnitId), Is.True);
        Assert.That(continuous.RenderAt(death.Tick, out var renderDiagnostics), Is.True, string.Join(";", renderDiagnostics));
        Assert.That(continuousFactory.Get(death.UnitId).Commands.Count(command => command == "death"), Is.EqualTo(1));
    }
}
```

给测试内 `FakeFactory` 增加只读查询：

```csharp
public bool Contains(string unitId) => views.ContainsKey(unitId);
```

- [ ] **Step 3: 运行定向 EditMode 测试确认 Red**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath D:\2022.3.62f1c1\Editor\Unity.exe `
  -TestPlatform EditMode `
  -TestFilter "ArknoNights.Battle.Tests.BattlePresentationTrackEditModeTests|ArknoNights.Battle.Tests.BattlePresentationEditModeTests" `
  -OutputDirectory Temp\UnitDeathPresentation\Task1-Red `
  -NoGraphics
```

Expected: 非零测试数且至少两项失败；失败分别表明死亡后的 `ShouldDisplay` 仍为 `true`、切入死亡 Tick 仍创建死亡单位。

- [ ] **Step 4: 实现死亡后点采样契约**

在 `UnitPresentationTrack.Sample(double tick)` 构造最终 sample 时，把显示值从无条件 Spawn 改为 Spawn 且存活：

```csharp
return new UnitPresentationSample(
    true,
    alive,
    pos,
    facing,
    action,
    start,
    seq,
    mult,
    MaxHitPoints,
    current,
    CurrentShield);
```

并将 `UnitPresentationSample` 构造器中的赋值改为：

```csharp
HasSpawned = s;
IsAlive = a;
ShouldDisplay = s && a;
```

保持 `Action = Death` 和最终 HP/位置采样不变。

- [ ] **Step 5: 仅在创建新视图时应用 ShouldDisplay**

在 `BattleTrackPlaybackController.RenderAt` 的单位循环中保持 `HasSpawned` 过滤，并在 `TryCreate` 前增加：

```csharp
if (!sample.HasSpawned) continue;
if (!views.TryGetValue(unit.UnitId, out var record))
{
    if (!sample.ShouldDisplay) continue;
    // existing factory.TryCreate and ViewRecord setup
}
Apply(record, sample);
```

不得对已有 `record` 使用 `ShouldDisplay` 提前返回，否则连续跨过 Death 会跳过 `PlayDeath`。

- [ ] **Step 6: 增加回退重建回归**

在同一 EditMode 测试中追加一个独立播放器：

```csharp
var rewindFactory = new FakeFactory();
using (var rewind = new BattleTrackPlaybackController())
{
    Assert.That(rewind.Bind(track, rewindFactory, BattleObserverView.Home, death.Tick, out _), Is.True);
    Assert.That(rewindFactory.Contains(death.UnitId), Is.False);
    Assert.That(rewind.RenderAt(death.Tick - 0.01d, out var rewindDiagnostics), Is.True, string.Join(";", rewindDiagnostics));
    Assert.That(rewindFactory.Contains(death.UnitId), Is.True);
    Assert.That(rewindFactory.Get(death.UnitId).Commands, Does.Not.Contain("death"));
}
```

- [ ] **Step 7: 运行定向 EditMode 测试确认 Green**

重复 Step 3，输出目录改为 `Temp\UnitDeathPresentation\Task1-Green`。

Expected: 相关测试全部通过、0 失败、0 跳过。

- [ ] **Step 8: 审查并提交 Track 改动**

```powershell
git diff --check
git diff -- Assets/Game/Battle/Presentation/Tracks/BattlePresentationTrack.cs Assets/Game/Battle/Presentation/Playback/BattleTrackPlaybackController.cs Assets/Game/Tests/EditMode/Battle/BattlePresentationTrackEditModeTests.cs Assets/Game/Tests/EditMode/Battle/BattlePresentationEditModeTests.cs
git add -- Assets/Game/Battle/Presentation/Tracks/BattlePresentationTrack.cs Assets/Game/Battle/Presentation/Playback/BattleTrackPlaybackController.cs Assets/Game/Tests/EditMode/Battle/BattlePresentationTrackEditModeTests.cs Assets/Game/Tests/EditMode/Battle/BattlePresentationEditModeTests.cs
git commit -m "feat: skip dead units when entering presentation tracks"
```

---

### Task 2: Spine 死亡完成、变黑和隐藏生命周期

**Files:**
- Modify: `Assets/Game/Runtime/Data/Unit/UnitSkelBase.cs`
- Modify: `Assets/Game/Runtime/Data/Unit/UnitSkelPresentationView.cs`
- Test: `Assets/Game/Tests/PlayMode/Battle/BattlePresentationPlaybackPlayModeTests.cs`

**Interfaces:**
- Consumes: `UnitSkelBase.PlayAnimation(...)`、Spine `TrackEntry.Complete`/`Dispose`、`SkeletonAnimation.Skeleton`
- Produces: `UnitSkelBase.TryPlayPresentationAnimation(string, bool, float, out TrackEntry)`；幂等的 `UnitSkelPresentationView.PlayDeath()`

- [ ] **Step 1: 写入真实 Spine 死亡生命周期失败测试**

在 `BattlePresentationPlaybackPlayModeTests` 新增测试，通过现有反射边界访问 Assembly-CSharp：

```csharp
[UnityTest]
public IEnumerator RealUnitView_DeathCompletesThenBlackensForHalfASecondAndHides()
{
    var factoryType = Type.GetType("MappedBattlePresentationViewFactory, Assembly-CSharp");
    Assert.IsNotNull(factoryType);
    var factoryObject = new GameObject("DeathPresentationFactory");
    var factory = factoryObject.AddComponent(factoryType) as IBattlePresentationViewFactory;
    Assert.IsNotNull(factory);

    Assert.That(factory.TryCreate("death-probe", "5504", out var view, out var diagnostic),
        Is.True, diagnostic == null ? string.Empty : diagnostic.ToString());
    var viewObject = FindChild(factoryObject.transform, "BattleView_death-probe");
    var skeleton = viewObject.GetComponent("SkeletonAnimation");
    Assert.IsNotNull(skeleton);
    var initialColor = GetSkeletonColor(skeleton);

    view.SetPlaybackSpeed(10f);
    view.PlayDeath();
    view.PlayDeath();
    yield return null;
    Assert.That(viewObject.activeSelf, Is.True);
    Assert.That(GetSkeletonColor(skeleton), Is.EqualTo(initialColor));

    var fadeDeadline = Time.realtimeSinceStartup + 3f;
    while (viewObject.activeSelf &&
           !HasAnyRgbDecreased(initialColor, GetSkeletonColor(skeleton)) &&
           Time.realtimeSinceStartup < fadeDeadline)
        yield return null;

    Assert.That(viewObject.activeSelf, Is.True, "The view must remain visible when blackening begins.");
    var earlyFadeColor = GetSkeletonColor(skeleton);
    Assert.That(HasAnyRgbDecreased(initialColor, earlyFadeColor), Is.True);

    yield return new WaitForSecondsRealtime(0.15f);
    var laterFadeColor = GetSkeletonColor(skeleton);
    Assert.That(laterFadeColor.r, Is.LessThan(earlyFadeColor.r));
    Assert.That(laterFadeColor.g, Is.LessThan(earlyFadeColor.g));
    Assert.That(laterFadeColor.b, Is.LessThan(earlyFadeColor.b));
    Assert.That(laterFadeColor.a, Is.EqualTo(initialColor.a).Within(0.0001f));
    Assert.That(viewObject.activeSelf, Is.True);

    yield return new WaitForSecondsRealtime(0.45f);
    Assert.That(viewObject.activeSelf, Is.False);

    view.Dispose();
    UnityEngine.Object.Destroy(factoryObject);
    yield return null;
}
```

增加不直接引用 Spine 程序集的反射 helper：

```csharp
private static Color GetSkeletonColor(Component skeletonAnimation)
{
    var skeleton = skeletonAnimation.GetType().GetProperty("Skeleton").GetValue(skeletonAnimation, null);
    var type = skeleton.GetType();
    return new Color(
        (float)type.GetProperty("R").GetValue(skeleton, null),
        (float)type.GetProperty("G").GetValue(skeleton, null),
        (float)type.GetProperty("B").GetValue(skeleton, null),
        (float)type.GetProperty("A").GetValue(skeleton, null));
}

private static bool HasAnyRgbDecreased(Color before, Color after)
    => after.r < before.r - 0.0001f || after.g < before.g - 0.0001f || after.b < before.b - 0.0001f;
```

- [ ] **Step 2: 运行定向 PlayMode 测试确认 Red**

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath D:\2022.3.62f1c1\Editor\Unity.exe `
  -TestPlatform PlayMode `
  -TestFilter ArknoNights.Battle.Tests.BattlePresentationPlaybackPlayModeTests `
  -OutputDirectory Temp\UnitDeathPresentation\Task2-Red `
  -NoGraphics
```

Expected: 新测试超时或断言失败，因为当前死亡动画完成后不会着色和隐藏。

- [ ] **Step 3: 为 UnitSkelBase 增加保留 TrackEntry 的兼容 API**

保留现有 bool 方法，新增：

```csharp
public bool PlayPresentationAnimation(string animName, bool loop, float animationSpeedMultiplier)
{
    return TryPlayPresentationAnimation(animName, loop, animationSpeedMultiplier, out _);
}

public bool TryPlayPresentationAnimation(
    string animName,
    bool loop,
    float animationSpeedMultiplier,
    out TrackEntry entry)
{
    entry = PlayAnimation(animName, loop);
    if (entry == null) return false;
    entry.TimeScale = Mathf.Max(0f, animationSpeedMultiplier);
    return true;
}
```

不得改变 Attack/Move 对现有 `PlayPresentationAnimation` 的调用语义。

- [ ] **Step 4: 在真实视图中实现死亡状态机**

在 `UnitSkelPresentationView` 增加状态：

```csharp
private const float DeathBlackeningSeconds = 0.5f;

private enum DeathPresentationState
{
    Alive,
    Animation,
    Blackening,
    Hidden
}

private DeathPresentationState deathState;
private Spine.TrackEntry deathTrackEntry;
private float deathBlackeningElapsed;
private Color deathBlackeningStartColor = Color.white;
```

把 `PlayOrReport` 改为可选返回 TrackEntry：

```csharp
private bool PlayOrReport(
    string animationName,
    bool loop,
    float localSpeed,
    string action,
    out Spine.TrackEntry entry)
{
    entry = null;
    if (deathFallbackApplied || !unitSkel)
    {
        Debug.LogWarning("[BattlePresentation][animation.adapter.missing] Cannot play " + action + " because UnitSkelBase is unavailable.", this);
        return false;
    }

    var success = unitSkel.TryPlayPresentationAnimation(
        animationName,
        loop,
        Mathf.Max(0f, localSpeed),
        out entry);
    if (!success)
        Debug.LogWarning("[BattlePresentation][animation.missing] action=" + action + "; animation=" + animationName, this);
    return success;
}
```

为非死亡调用保留一个丢弃 entry 的重载，避免修改公开接口：

```csharp
private bool PlayOrReport(string animationName, bool loop, float localSpeed, string action)
    => PlayOrReport(animationName, loop, localSpeed, action, out _);
```

- [ ] **Step 5: 订阅完成回调并以现实时间变黑**

实现幂等死亡入口和每帧着色：

```csharp
public void PlayDeath()
{
    if (deathState != DeathPresentationState.Alive) return;
    if (!PlayOrReport(deathAnimation, false, 1f, "death", out var entry))
    {
        ApplyDeathFallback();
        return;
    }

    deathState = DeathPresentationState.Animation;
    deathTrackEntry = entry;
    deathTrackEntry.Complete += HandleDeathAnimationComplete;
    deathTrackEntry.Dispose += HandleDeathTrackDisposed;
}

private void Update()
{
    if (deathState != DeathPresentationState.Blackening) return;
    deathBlackeningElapsed += Time.unscaledDeltaTime;
    var progress = Mathf.Clamp01(deathBlackeningElapsed / DeathBlackeningSeconds);
    var target = new Color(0f, 0f, 0f, deathBlackeningStartColor.a);
    if (skeletonAnimation && skeletonAnimation.Skeleton != null)
        skeletonAnimation.Skeleton.SetColor(Color.Lerp(deathBlackeningStartColor, target, progress));
    if (progress >= 1f) HideDeathView();
}
```

完成回调必须先解除 TrackEntry 订阅，再读取 Skeleton 当前色并进入 `Blackening`。若 Skeleton 不可用，则记录 `[BattlePresentation][death.fade.skeleton.missing]` 并立即隐藏。

`HandleDeathTrackDisposed` 只处理仍指向本次 entry 的状态；若动画在完成前被意外释放，记录 `[BattlePresentation][death.animation.interrupted]` 并立即隐藏，避免永久尸体。

```csharp
private void HandleDeathAnimationComplete(Spine.TrackEntry entry)
{
    if (!ReferenceEquals(entry, deathTrackEntry) ||
        deathState != DeathPresentationState.Animation)
        return;

    DetachDeathTrackEntry();
    if (!skeletonAnimation || skeletonAnimation.Skeleton == null)
    {
        Debug.LogWarning("[BattlePresentation][death.fade.skeleton.missing] Cannot blacken a death view without an initialized Skeleton.", this);
        HideDeathView();
        return;
    }

    deathBlackeningStartColor = skeletonAnimation.Skeleton.GetColor();
    deathBlackeningElapsed = 0f;
    deathState = DeathPresentationState.Blackening;
}

private void HandleDeathTrackDisposed(Spine.TrackEntry entry)
{
    if (!ReferenceEquals(entry, deathTrackEntry)) return;
    DetachDeathTrackEntry();
    if (deathState != DeathPresentationState.Animation) return;
    Debug.LogWarning("[BattlePresentation][death.animation.interrupted] Death animation ended before completion; hid the dead unit.", this);
    HideDeathView();
}
```

- [ ] **Step 6: 统一 fallback、隐藏、销毁和回调清理**

```csharp
private void ApplyDeathFallback()
{
    deathFallbackApplied = true;
    Debug.LogWarning("[BattlePresentation][death.fallback.hide] Missing death animation; hid the event-confirmed dead unit.", this);
    HideDeathView();
}

private void HideDeathView()
{
    deathState = DeathPresentationState.Hidden;
    DetachDeathTrackEntry();
    if (gameObject.activeSelf) gameObject.SetActive(false);
}

public void Dispose()
{
    if (!this || !gameObject) return;
    if (gameObject.activeSelf) gameObject.SetActive(false);
    Destroy(gameObject);
}

private void OnDestroy()
{
    DetachDeathTrackEntry();
}

private void DetachDeathTrackEntry()
{
    var entry = deathTrackEntry;
    deathTrackEntry = null;
    if (entry == null) return;
    entry.Complete -= HandleDeathAnimationComplete;
    entry.Dispose -= HandleDeathTrackDisposed;
}
```

`DetachDeathTrackEntry` 必须对 `Complete`、`Dispose` 同时退订并清空字段；在 `Dispose` 事件回调内清字段时不得在 TrackEntry 被池复用后继续持有引用。

- [ ] **Step 7: 增加 Replay/Dispose 不重叠回归**

在同一 PlayMode 测试类新增：

```csharp
[UnityTest]
public IEnumerator RealUnitView_DisposeDuringDeathImmediatelyHidesBeforeFrameEnd()
{
    var factoryType = Type.GetType("MappedBattlePresentationViewFactory, Assembly-CSharp");
    Assert.IsNotNull(factoryType);
    var factoryObject = new GameObject("DeathDisposeFactory");
    var factory = factoryObject.AddComponent(factoryType) as IBattlePresentationViewFactory;
    Assert.IsNotNull(factory);

    Assert.That(factory.TryCreate("dispose-probe", "5504", out var view, out var diagnostic),
        Is.True, diagnostic == null ? string.Empty : diagnostic.ToString());
    var viewObject = FindChild(factoryObject.transform, "BattleView_dispose-probe");
    Assert.IsNotNull(viewObject);

    view.PlayDeath();
    view.Dispose();

    Assert.That(viewObject.activeSelf, Is.False,
        "Dispose must hide the old death view before Unity destroys it at frame end.");
    yield return null;
    Assert.That(FindChild(factoryObject.transform, "BattleView_dispose-probe"), Is.Null);

    UnityEngine.Object.Destroy(factoryObject);
    yield return null;
}
```

测试实现必须复用 `FindChild`，不得通过清空父节点或销毁整个工厂掩盖单位视图未立即隐藏的问题。

- [ ] **Step 8: 运行定向 PlayMode 测试确认 Green**

重复 Step 2，输出目录改为 `Temp\UnitDeathPresentation\Task2-Green`。

Expected: `BattlePresentationPlaybackPlayModeTests` 全部通过、0 失败、0 跳过；日志无新异常。

- [ ] **Step 9: 审查并提交视图生命周期**

```powershell
git diff --check
git diff -- Assets/Game/Runtime/Data/Unit/UnitSkelBase.cs Assets/Game/Runtime/Data/Unit/UnitSkelPresentationView.cs Assets/Game/Tests/PlayMode/Battle/BattlePresentationPlaybackPlayModeTests.cs
git add -- Assets/Game/Runtime/Data/Unit/UnitSkelBase.cs Assets/Game/Runtime/Data/Unit/UnitSkelPresentationView.cs Assets/Game/Tests/PlayMode/Battle/BattlePresentationPlaybackPlayModeTests.cs
git commit -m "feat: fade dead unit views to black before hiding"
```

---

### Task 3: 文档、全量验证与最终审查

**Files:**
- Modify: `docs/ARCHITECTURE.md`
- Modify: `docs/TEST_PLAN.md`
- Verify: all files changed since `69f0444`

**Interfaces:**
- Consumes: Task 1 的 `ShouldDisplay` 切入语义、Task 2 的死亡视图状态机
- Produces: 可复查的架构说明、实际测试计数、未验证项和最终交付证据

- [ ] **Step 1: 更新架构说明**

在 `docs/ARCHITECTURE.md` 的 Battle Presentation 边界补充：

```markdown
- Track 点采样的 `ShouldDisplay` 表示切入创建资格：死亡单位保留权威终态采样，但新的绑定不会创建其视图；连续播放已有视图仍消费 Death。
- 真实 Spine 视图监听本次死亡 TrackEntry 完成，随后以 0.5 秒现实时间修改 Skeleton 顶点色并停用 GameObject；Replay/重绑仍由播放器统一 Dispose。
```

- [ ] **Step 2: 运行全量 EditMode**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath D:\2022.3.62f1c1\Editor\Unity.exe `
  -TestPlatform EditMode `
  -OutputDirectory Temp\UnitDeathPresentation\Full-EditMode `
  -NoGraphics
```

Expected: 非零测试数、0 失败、0 跳过；记录 XML 中实际 total。

- [ ] **Step 3: 运行全量 PlayMode**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath D:\2022.3.62f1c1\Editor\Unity.exe `
  -TestPlatform PlayMode `
  -OutputDirectory Temp\UnitDeathPresentation\Full-PlayMode `
  -NoGraphics
```

Expected: 非零测试数、0 失败、0 跳过；记录 XML 中实际 total。

- [ ] **Step 4: 检查编译与测试日志**

```powershell
rg -n "error CS|Compilation failed|Scripts have compiler errors|Unhandled|NullReferenceException" Temp/UnitDeathPresentation/Full-EditMode Temp/UnitDeathPresentation/Full-PlayMode
```

Expected: 没有与本次修改相关的编译错误、未处理异常或空测试结果。若命中测试故意记录的预期日志，逐条核对上下文后说明。

- [ ] **Step 5: 用实际结果更新 TEST_PLAN**

在 `docs/TEST_PLAN.md` 新增日期为 `2026-07-27` 的“单位死亡表现”验证记录，准确写入：

- Track 点采样、切入过滤、连续 Death 和回退重建的 EditMode 覆盖；
- 真实 Spine 动画完成、`0.5` 秒 RGB 变黑、隐藏和立即 Dispose 的 PlayMode 覆盖；
- 全量 EditMode/PlayMode 的实际 total/failed/skipped 与结果路径；
- 未执行 Windows Player 构建和人工连续观感检查，不得记为通过。

- [ ] **Step 6: 最终 diff 与工作树审查**

```powershell
git diff --check
git status --short
git diff 69f0444 -- Assets/Game docs/SPEC.md docs/ARCHITECTURE.md docs/TEST_PLAN.md docs/superpowers
git log --oneline -6
```

确认没有修改场景、Prefab、Shader、目录 JSON、Package、ProjectSettings，也没有加入 `.superpowers/` 或 `docs/bonds/`。

- [ ] **Step 7: 提交文档与验证记录**

```powershell
git add -- docs/ARCHITECTURE.md docs/TEST_PLAN.md
git commit -m "docs: verify unit death presentation lifecycle"
```

- [ ] **Step 8: 使用 verification-before-completion 做最终证据核对**

重新读取最终 XML 摘要、`git status --short` 和 `git diff 69f0444 --stat`。只有相关 SPEC 验收、编译、相关测试、全量测试和 diff 审查都有新鲜证据时才声明完成；人工视觉和 Windows Player 构建必须列为未验证。
