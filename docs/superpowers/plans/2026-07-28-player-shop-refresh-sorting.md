# 玩家商店刷新排序 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为四名玩家维护独立商店，并让主动刷新和整轮战斗结束后的被动刷新按“稀有度、数值 `typeId`”从左到右升序写入可刷新槽位。

**Architecture:** `LocalMatchState` 继续作为商店权威状态，`LocalMatchPlayerData` 各自持有六个稳定 `ShopSlotId`；新增无场景依赖的 `ShopOfferOrdering` 只负责稳定复合排序。主动刷新只替换本地玩家全部商品并扣费，被动刷新在 `PreparationBattleLoopController.ReturnToPreparation` 中一次性更新四名玩家，冻结槽原位保留。

**Tech Stack:** Unity 2022.3.62f1、C#、NUnit、Unity Test Framework（EditMode / PlayMode）、现有 PowerShell 测试脚本、Windows x64 StrictMode 构建入口。

## Global Constraints

- 从左到右先按 `UnitCatalogEntry.Rarity` 升序，再按十进制数值 `typeId` 升序；高稀有度和大 ID 位于右侧。
- 初始加载不额外排序；排序只发生在主动刷新和战斗结束后的被动刷新。
- 主动刷新只作用于本地玩家，消耗 `1` 赤金，替换包括冻结商品在内的全部六槽并清除全部冻结状态。
- 被动刷新只在两场战斗演出全部完成、整轮从 Battle 返回 Preparation 时触发一次；不扣赤金，并在一次领域变更中刷新四名玩家。
- 被动刷新保留冻结商品的 `UnitTypeId`、`ShopSlotId` 和冻结状态；空槽与未冻结槽均参与刷新，新商品只在这些可刷新槽之间排序。
- 四名玩家可以使用相同的临时固定页面作为测试商品来源，但商店数组必须彼此独立。
- 固定页面和 `currentShopPage` 只是当前确定性 fixture 的私有实现，不新增公开页面游标接口；未来按玩家等级概率随机生成时替换商品来源，不改变排序器和槽位写入规则。
- 观察远端玩家时，正式商店 UI 仍读取 `snapshot.LocalPlayer`，不显示远端商店，也不新增远端购买、冻结或主动刷新命令。
- 不修改当前工作区中已有用户改动的 `ShopReadyHudController.cs`、`ShopReadyHudStateEditModeTests.cs`、Spine/头像/准备按钮资源、Bonds 文档或批量资源导入文件。
- 不升级 Unity、Package、渲染管线或 Input System；不修改场景、Prefab、ScriptableObject、Package 或 ProjectSettings。
- Unity 测试和构建必须串行执行，同一项目路径不能同时由两个 Unity 进程打开。

---

## File Map

- Create: `Assets/Game/Runtime/Data/Player/ShopOfferOrdering.cs`
  - 领域内稳定排序器；输入生成顺序中的 `typeId` 和稀有度解析函数，输出排序后的新数组。
- Modify: `Assets/Game/Runtime/Data/Player/LocalMatchState.cs`
  - 把六槽商店从全局本地字段迁移到每个 `LocalMatchPlayerData`；接入主动/被动刷新和全玩家快照。
- Modify: `Assets/Game/Tests/EditMode/Battle/LocalMatchStateEditModeTests.cs`
  - 覆盖数值 ID 排序、主动刷新、四玩家独立商店、被动刷新、冻结槽和原子通知。
- Modify: `Assets/Game/Runtime/Initial/PreparationBattleLoopController.cs`
  - 在合法的整轮完成边界调用一次被动刷新。
- Modify: `Assets/Game/Tests/PlayMode/Battle/PreparationBattleLoopPlayModeTests.cs`
  - 在真实 `SampleScene` 循环中验证演出完成前不刷新、返回准备后四店刷新且不扣费。
- Modify: `docs/SPEC.md`
  - 写入已确认的玩家可见刷新与排序规则。
- Modify: `docs/ARCHITECTURE.md`
  - 记录每玩家商店所有权、排序边界和回合触发点。
- Modify: `docs/TEST_PLAN.md`
  - 记录 Red/Green、全量测试、构建结果和未验证项。
- Unity-generated and committed with their source files:
  - `Assets/Game/Runtime/Data/Player/ShopOfferOrdering.cs.meta`

---

### Task 1: 每玩家商店与主动刷新排序

**Files:**
- Create: `Assets/Game/Runtime/Data/Player/ShopOfferOrdering.cs`
- Modify: `Assets/Game/Runtime/Data/Player/LocalMatchState.cs`
- Test: `Assets/Game/Tests/EditMode/Battle/LocalMatchStateEditModeTests.cs`

**Interfaces:**
- Consumes: `UnitCatalog.TryGet(string typeId, out UnitCatalogEntry entry)`、现有三页 `string[][] shopPages`、稳定的 `ShopSlotData.ShopSlotId`。
- Produces: `internal static string[] ShopOfferOrdering.Sort(IEnumerable<string> generatedTypeIds, Func<string, int> rarityByTypeId)`；每个 `LocalMatchPlayerData.ShopSlots`；保持签名不变的 `LocalMatchState.TryRefresh()`。

- [ ] **Step 1: 记录修改前基线并确认 Unity 未占用项目**

运行：

```powershell
git status --short
Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" |
  Where-Object { $_.CommandLine -match [regex]::Escape('G:\ARKnoNIGHTS_beta') } |
  Select-Object ProcessId, CommandLine
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath D:\2022.3.62f1c1\Editor\Unity.exe `
  -TestPlatform EditMode `
  -TestFilter ArknoNights.Battle.Tests.LocalMatchStateEditModeTests `
  -OutputDirectory Artifacts\ShopRefreshSorting\Baseline-LocalMatch `
  -NoGraphics
```

预期：没有 Unity 进程占用项目；测试 XML 的测试数大于零且当前 `LocalMatchStateEditModeTests` 全部通过。若项目被占用，停止执行并让用户关闭该 Unity 实例。

- [ ] **Step 2: 写入主动刷新和纯排序器的失败测试**

在 `LocalMatchStateEditModeTests.cs` 增加 `using System;`、`using System.Collections.Generic;` 和 `using System.Reflection;`，并修改/新增以下断言：

```csharp
[Test]
public void FixedMatch_LoadsFourIndependentPlayersAndTheInitialSixSlotShopDeterministically()
{
    var first = LocalMatchStateLoader.LoadFromResources(CatalogPath, MatchPath);
    var second = LocalMatchStateLoader.LoadFromResources(CatalogPath, MatchPath);

    Assert.IsTrue(first.Success, Errors(first.Errors));
    Assert.IsTrue(second.Success, Errors(second.Errors));
    Assert.AreEqual("local-ui-player", first.State.LocalPlayerId);
    Assert.AreEqual("local-ui-player", first.State.ObservedPlayerId);
    Assert.AreEqual(4, first.State.Snapshot.Players.Count);
    CollectionAssert.AllItemsAreUnique(first.State.Snapshot.Players.Select(player => player.PlayerId));
    CollectionAssert.AllItemsAreUnique(first.State.Snapshot.Players
        .SelectMany(player => player.PlayerState.Units)
        .Select(unit => unit.UnitId));
    Assert.IsTrue(first.State.Snapshot.Players.All(player => player.ShopSlots.Count == LocalMatchState.ShopSlotCount));
    Assert.IsTrue(first.State.Snapshot.Players.All(player =>
        player.ShopSlots.Select(slot => slot.UnitTypeId).SequenceEqual(
            new[] { "1000", "1000", "1000", "1000", "1000", "1000" })));
    Assert.AreEqual(1, first.State.Snapshot.LocalPlayer.Level);
    Assert.AreEqual(7, first.State.Snapshot.LocalPlayer.Gold);
    Assert.AreEqual(400, first.State.Snapshot.LocalPlayer.Life);
    Assert.IsFalse(first.State.Snapshot.LocalPlayer.IsReady);
    Assert.AreEqual(first.State.Snapshot.CanonicalSummary, second.State.Snapshot.CanonicalSummary);
}

[Test]
public void InitialShop_KeepsConfiguredGenerationOrderForEveryPlayer()
{
    var catalog = UnitCatalogLoader.LoadFromResources(CatalogPath).Catalog;
    var source = Resources.Load<TextAsset>(MatchPath).text.Replace(
        "\"typeIds\": [\"1000\", \"1000\", \"1000\", \"1000\", \"1000\", \"1000\"]",
        "\"typeIds\": [\"5503\", \"1000\", \"5503\", \"1000\", \"5503\", \"1000\"]");
    var loaded = LocalMatchStateLoader.LoadFromJson(catalog, source);

    Assert.IsTrue(loaded.Success, Errors(loaded.Errors));
    foreach (var player in loaded.State.Snapshot.Players)
        CollectionAssert.AreEqual(
            new[] { "5503", "1000", "5503", "1000", "5503", "1000" },
            player.ShopSlots.Select(slot => slot.UnitTypeId));
}

[Test]
public void OfferOrdering_SortsByRarityThenNumericTypeIdAndKeepsGenerationOrderForEqualKeys()
{
    var orderingType = typeof(LocalMatchState).Assembly.GetType("ArknoNights.Player.ShopOfferOrdering");
    Assert.NotNull(orderingType, "The domain ordering helper is missing.");
    var sort = orderingType.GetMethod("Sort", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    Assert.NotNull(sort);
    var rarities = new Dictionary<string, int>
    {
        ["9"] = 2,
        ["09"] = 2,
        ["10"] = 2,
        ["11"] = 2,
        ["100"] = 1
    };

    var actual = (string[])sort.Invoke(
        null,
        new object[]
        {
            new[] { "10", "09", "100", "9", "11" },
            new Func<string, int>(typeId => rarities[typeId])
        });

    CollectionAssert.AreEqual(new[] { "100", "09", "9", "10", "11" }, actual);
}

[Test]
public void ActiveRefresh_ReplacesFrozenLocalOffersClearsFreezeAndSortsWithoutChangingRemoteShops()
{
    var state = Load();
    var remoteBefore = state.Snapshot.Players
        .Where(player => player.PlayerId != state.LocalPlayerId)
        .ToDictionary(
            player => player.PlayerId,
            player => player.ShopSlots.Select(slot => slot.UnitTypeId).ToArray());
    Assert.IsTrue(state.TryToggleFrozen(0).Success);

    var first = state.TryRefresh();

    Assert.IsTrue(first.Success);
    Assert.AreEqual(6, first.Snapshot.LocalPlayer.Gold);
    Assert.IsTrue(first.Snapshot.LocalPlayer.ShopSlots.All(slot => !slot.IsFrozen));
    CollectionAssert.AreEqual(
        new[] { "5503", "5503", "5503", "5503", "5503", "5503" },
        first.Snapshot.LocalPlayer.ShopSlots.Select(slot => slot.UnitTypeId));
    foreach (var remote in first.Snapshot.Players.Where(player => player.PlayerId != state.LocalPlayerId))
        CollectionAssert.AreEqual(remoteBefore[remote.PlayerId], remote.ShopSlots.Select(slot => slot.UnitTypeId));

    var second = state.TryRefresh();

    Assert.IsTrue(second.Success);
    CollectionAssert.AreEqual(
        new[] { "1000", "1000", "1000", "5503", "5503", "5503" },
        second.Snapshot.LocalPlayer.ShopSlots.Select(slot => slot.UnitTypeId));
}
```

保留现有购买、批量冻结、升级和失败原子性测试。删除旧测试
`Refresh_PreservesFrozenItemsAndReplacesOtherSlotsFromTheNextFixedPage` 对“主动刷新保留冻结”的过期期望，由上面的主动刷新测试取代。

- [ ] **Step 3: 运行测试并确认 RED 原因准确**

运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath D:\2022.3.62f1c1\Editor\Unity.exe `
  -TestPlatform EditMode `
  -TestFilter ArknoNights.Battle.Tests.LocalMatchStateEditModeTests `
  -OutputDirectory Artifacts\ShopRefreshSorting\Task1-Red `
  -NoGraphics
```

预期：测试失败，且失败证据同时包含远端玩家商店为空、主动刷新仍保留冻结槽、排序辅助类型不存在中的相应项；不得出现 C# 编译错误或与这些断言无关的异常。

- [ ] **Step 4: 实现稳定复合排序器**

创建 `ShopOfferOrdering.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ArknoNights.Player
{
    internal static class ShopOfferOrdering
    {
        public static string[] Sort(IEnumerable<string> generatedTypeIds, Func<string, int> rarityByTypeId)
        {
            if (generatedTypeIds == null) throw new ArgumentNullException(nameof(generatedTypeIds));
            if (rarityByTypeId == null) throw new ArgumentNullException(nameof(rarityByTypeId));

            return generatedTypeIds
                .Select((typeId, generatedIndex) => new OrderedOffer(
                    typeId,
                    rarityByTypeId(typeId),
                    int.Parse(typeId, NumberStyles.None, CultureInfo.InvariantCulture),
                    generatedIndex))
                .OrderBy(offer => offer.Rarity)
                .ThenBy(offer => offer.NumericTypeId)
                .ThenBy(offer => offer.GeneratedIndex)
                .Select(offer => offer.TypeId)
                .ToArray();
        }

        private readonly struct OrderedOffer
        {
            public OrderedOffer(string typeId, int rarity, int numericTypeId, int generatedIndex)
            {
                TypeId = typeId;
                Rarity = rarity;
                NumericTypeId = numericTypeId;
                GeneratedIndex = generatedIndex;
            }

            public string TypeId { get; }
            public int Rarity { get; }
            public int NumericTypeId { get; }
            public int GeneratedIndex { get; }
        }
    }
}
```

`UnitCatalogLoader` 已要求目录 `typeId` 全部由数字组成、可解析为 `int`，并与 `legacyUnitTypeId` 相等；因此排序器使用 `NumberStyles.None` 和 `InvariantCulture`，不增加字符串字典序兜底。

- [ ] **Step 5: 把商店所有权迁移到每名玩家**

在 `LocalMatchPlayerData` 中加入独立商店：

```csharp
public ShopSlotData[] ShopSlots { get; private set; } = Array.Empty<ShopSlotData>();

public void InitializeShop(IEnumerable<string> initialTypeIds)
{
    ShopSlots = (initialTypeIds ?? Enumerable.Empty<string>())
        .Select((typeId, index) => new ShopSlotData(index, typeId, false))
        .ToArray();
}
```

在 `LocalMatchState` 构造器解析并复制页面后，为每个玩家初始化一份数组：

```csharp
this.shopPages = shopPages.Select(page => page.ToArray()).ToArray();
foreach (var player in orderedPlayers)
    player.InitializeShop(this.shopPages[0]);
```

删除原字段 `private readonly ShopSlotData[] shopSlots;`，增加只定位本地命令归属的私有属性：

```csharp
private ShopSlotData[] LocalShopSlots => playersById[localPlayerId].ShopSlots;
```

把 `TryPurchase`、`TryToggleFrozen`、`TrySetOccupiedShopSlotsFrozen` 和 `TryGetShopSlot` 中的 `shopSlots` 读取替换为 `LocalShopSlots`。不要改变这些命令的公开签名、错误码、扣费、单位 ID 或通知次数。

`CreateSnapshot()` 对四名玩家分别投影自己的商店，不再给远端玩家空集合：

```csharp
var slots = player.ShopSlots.Select(slot =>
    new LocalMatchShopSlotSnapshot(slot.ShopSlotId, slot.UnitTypeId, slot.IsFrozen, catalog));
return new LocalMatchPlayerSnapshot(
    player.PlayerId,
    player.DisplayName,
    player.AvatarResourcePath,
    player.Life,
    player.IsConnected,
    player.HasExited,
    player.PlayerState.Snapshot,
    local ? level : 0,
    local ? gold : 0,
    local && isReady,
    slots);
```

这会让现有 `LocalMatchSnapshot.BuildCanonicalSummary` 自动包含四名玩家的商店，因为它已经遍历每个 `player.ShopSlots`。

- [ ] **Step 6: 改写主动刷新**

在 `LocalMatchState` 增加目录稀有度解析：

```csharp
private string[] SortShopOffers(IEnumerable<string> generatedTypeIds)
{
    return ShopOfferOrdering.Sort(generatedTypeIds, typeId =>
    {
        if (!catalog.TryGet(typeId, out var entry))
            throw new InvalidOperationException("Validated shop offer is missing from the unit catalog: " + typeId);
        return entry.Rarity;
    });
}
```

将 `TryRefresh()` 改为先校验赤金，再读取下一临时页面、排序、覆盖本地六槽：

```csharp
public LocalMatchOperationResult TryRefresh()
{
    if (gold < RefreshCost) return Result(LocalMatchOperationCode.InsufficientGold);

    var nextPage = (currentShopPage + 1) % shopPages.Length;
    var sortedOffers = SortShopOffers(shopPages[nextPage]);
    var localSlots = LocalShopSlots.OrderBy(slot => slot.ShopSlotId).ToArray();
    for (var index = 0; index < localSlots.Length; index++)
    {
        localSlots[index].UnitTypeId = sortedOffers[index];
        localSlots[index].IsFrozen = false;
    }

    currentShopPage = nextPage;
    gold -= RefreshCost;
    NotifyChanged();
    return Result(LocalMatchOperationCode.Success);
}
```

失败路径仍在读取/推进页面、改槽、清冻结、扣费和通知之前返回。

- [ ] **Step 7: 运行 Task 1 Green 并检查 Unity 生成的 meta**

运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath D:\2022.3.62f1c1\Editor\Unity.exe `
  -TestPlatform EditMode `
  -TestFilter ArknoNights.Battle.Tests.LocalMatchStateEditModeTests `
  -OutputDirectory Artifacts\ShopRefreshSorting\Task1-Green `
  -NoGraphics
git status --short -- Assets/Game/Runtime/Data/Player Assets/Game/Tests/EditMode/Battle/LocalMatchStateEditModeTests.cs
```

预期：测试数大于零、失败 `0`、跳过 `0`；`ShopOfferOrdering.cs.meta` 已由 Unity 生成且与 `.cs` 成对；没有修改商店 UI 文件。

- [ ] **Step 8: 审查并提交 Task 1**

运行：

```powershell
git diff --check
git diff -- Assets/Game/Runtime/Data/Player/ShopOfferOrdering.cs `
  Assets/Game/Runtime/Data/Player/LocalMatchState.cs `
  Assets/Game/Tests/EditMode/Battle/LocalMatchStateEditModeTests.cs
git add -- Assets/Game/Runtime/Data/Player/ShopOfferOrdering.cs `
  Assets/Game/Runtime/Data/Player/ShopOfferOrdering.cs.meta `
  Assets/Game/Runtime/Data/Player/LocalMatchState.cs `
  Assets/Game/Tests/EditMode/Battle/LocalMatchStateEditModeTests.cs
git diff --cached --check
git commit -m "feat: sort active shop refresh per player"
```

预期：暂存区只包含 Task 1 的四个路径；用户已有脏文件保持未暂存。

---

### Task 2: 四玩家战后被动刷新

**Files:**
- Modify: `Assets/Game/Runtime/Data/Player/LocalMatchState.cs`
- Test: `Assets/Game/Tests/EditMode/Battle/LocalMatchStateEditModeTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `LocalMatchPlayerData.ShopSlots`、`ShopOfferOrdering.Sort(...)`、私有固定页面来源。
- Produces: `public LocalMatchOperationResult LocalMatchState.RefreshAllShopsAfterBattle()`，供回合组合层在唯一完成边界调用。

- [ ] **Step 1: 写入被动刷新失败测试**

先使用反射寻找尚不存在的公开方法，使 RED 能生成结构化 NUnit 失败而不是 C# 编译失败：

```csharp
[Test]
public void PassiveRefresh_RefreshesAllPlayersForFreeWhileFrozenSlotsStayAndOnlyOpenSlotsSort()
{
    var state = Load();
    Assert.IsTrue(state.TryRefresh().Success);
    Assert.IsTrue(state.TryPurchase(4).Success);
    Assert.IsTrue(state.TryToggleFrozen(0).Success);
    Assert.IsTrue(state.TryToggleFrozen(5).Success);
    var before = state.Snapshot;
    var changes = 0;
    state.Changed += _ => changes++;

    var method = typeof(LocalMatchState).GetMethod(
        "RefreshAllShopsAfterBattle",
        BindingFlags.Instance | BindingFlags.Public);
    Assert.NotNull(method, "The battle-completion shop refresh operation is missing.");
    var result = (LocalMatchOperationResult)method.Invoke(state, Array.Empty<object>());

    Assert.IsTrue(result.Success);
    Assert.AreEqual(before.LocalPlayer.Gold, result.Snapshot.LocalPlayer.Gold);
    Assert.AreEqual(before.Version + 1, result.Snapshot.Version);
    Assert.AreEqual(1, changes);
    CollectionAssert.AreEqual(
        new[] { "5503", "1000", "1000", "5503", "5503", "5503" },
        result.Snapshot.LocalPlayer.ShopSlots.Select(slot => slot.UnitTypeId));
    Assert.IsTrue(result.Snapshot.LocalPlayer.ShopSlots[0].IsFrozen);
    Assert.IsTrue(result.Snapshot.LocalPlayer.ShopSlots[5].IsFrozen);
    foreach (var remote in result.Snapshot.Players.Where(player => player.PlayerId != state.LocalPlayerId))
    {
        CollectionAssert.AreEqual(
            new[] { "1000", "1000", "1000", "5503", "5503", "5503" },
            remote.ShopSlots.Select(slot => slot.UnitTypeId));
        Assert.IsTrue(remote.ShopSlots.All(slot => !slot.IsFrozen));
    }
}
```

这里先执行一次主动刷新，把临时来源推进到全 `5503` 页，再购买槽 `4` 使它成为空槽；冻结本地物理槽 `0` 和 `5` 后，被动刷新读取混合页。混合页在可刷新槽 `1..4` 的候选顺序为 `5503,1000,5503,1000`，从而同时证明空槽会补货、候选只在可刷新槽之间重新排序，两个冻结槽不移动。

- [ ] **Step 2: 运行测试并确认 RED**

运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath D:\2022.3.62f1c1\Editor\Unity.exe `
  -TestPlatform EditMode `
  -TestFilter ArknoNights.Battle.Tests.LocalMatchStateEditModeTests `
  -OutputDirectory Artifacts\ShopRefreshSorting\Task2-Red `
  -NoGraphics
```

预期：只有新用例因 `RefreshAllShopsAfterBattle` 不存在而失败；Task 1 用例保持通过。

- [ ] **Step 3: 实现一次性全玩家被动刷新**

在 `LocalMatchState` 增加：

```csharp
public LocalMatchOperationResult RefreshAllShopsAfterBattle()
{
    var nextPage = (currentShopPage + 1) % shopPages.Length;
    foreach (var playerId in orderedPlayerIds)
    {
        var refreshableSlots = playersById[playerId].ShopSlots
            .Where(slot => slot.IsEmpty || !slot.IsFrozen)
            .OrderBy(slot => slot.ShopSlotId)
            .ToArray();
        var generatedOffers = refreshableSlots
            .Select(slot => shopPages[nextPage][slot.ShopSlotId])
            .ToArray();
        var sortedOffers = SortShopOffers(generatedOffers);

        for (var index = 0; index < refreshableSlots.Length; index++)
        {
            refreshableSlots[index].UnitTypeId = sortedOffers[index];
            refreshableSlots[index].IsFrozen = false;
        }
    }

    currentShopPage = nextPage;
    NotifyChanged();
    return Result(LocalMatchOperationCode.Success);
}
```

当前确定性 fixture 的同一 `nextPage` 在本次领域操作中供四名玩家使用；每名玩家根据自己的冻结槽集合，从该页相应 `ShopSlotId` 取得本次候选。页面读取、四店改写、游标推进和一次 `NotifyChanged()` 均在同一同步方法内完成，不暴露中间快照，也不读取或修改 `gold`。

- [ ] **Step 4: 把测试切换为编译期公开接口并运行 Green**

把测试中的反射调用替换为：

```csharp
var result = state.RefreshAllShopsAfterBattle();
```

运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath D:\2022.3.62f1c1\Editor\Unity.exe `
  -TestPlatform EditMode `
  -TestFilter ArknoNights.Battle.Tests.LocalMatchStateEditModeTests `
  -OutputDirectory Artifacts\ShopRefreshSorting\Task2-Green `
  -NoGraphics
```

预期：测试数大于零、失败 `0`、跳过 `0`；本地冻结槽保持原商品和原位置，远端三店也更新，本地赤金不变，`Changed` 恰好一次。

- [ ] **Step 5: 审查并提交 Task 2**

运行：

```powershell
git diff --check
git diff -- Assets/Game/Runtime/Data/Player/LocalMatchState.cs `
  Assets/Game/Tests/EditMode/Battle/LocalMatchStateEditModeTests.cs
git add -- Assets/Game/Runtime/Data/Player/LocalMatchState.cs `
  Assets/Game/Tests/EditMode/Battle/LocalMatchStateEditModeTests.cs
git diff --cached --check
git commit -m "feat: refresh all player shops after battle"
```

预期：提交不包含其他工作区文件。

---

### Task 3: 整轮完成边界接线

**Files:**
- Modify: `Assets/Game/Runtime/Initial/PreparationBattleLoopController.cs`
- Test: `Assets/Game/Tests/PlayMode/Battle/PreparationBattleLoopPlayModeTests.cs`

**Interfaces:**
- Consumes: Task 2 的 `LocalMatchState.RefreshAllShopsAfterBattle()`；现有 `MultiBattlePresentationState.Completed` 和 `ReturnToPreparation()` 唯一转换路径。
- Produces: 每次合法 Battle → Preparation 转换恰好一次的四玩家被动刷新。

- [ ] **Step 1: 扩展真实场景 PlayMode 用例**

在 `SampleScene_AutoLoopsPreparationToBattleAndBackWithoutWritingCombatResultToPlayerState` 获得 loop-owned match，并在进入战斗前准备可观察商店状态：

```csharp
var match = (LocalMatchState)loopType.GetProperty("MatchState").GetValue(loop);
Assert.NotNull(match);
Assert.IsTrue(match.TryRefresh().Success);
Assert.IsTrue(match.TryToggleFrozen(0).Success);
var goldBeforeBattle = match.Snapshot.LocalPlayer.Gold;
```

在现有 `AdvanceForTests(1200f)` 后、仍断言 `Phase == Battle` 的位置记录并核对商店尚未发生被动刷新：

```csharp
var shopsBeforeTerminalPresentation = match.Snapshot.Players.ToDictionary(
    player => player.PlayerId,
    player => string.Join(",", player.ShopSlots.Select(slot =>
        slot.ShopSlotId + ":" + slot.UnitTypeId + ":" + (slot.IsFrozen ? "1" : "0"))));

loopType.GetMethod("AdvanceForTests").Invoke(loop, new object[] { 1200f });

Assert.AreEqual("Battle", loopType.GetProperty("Phase").GetValue(loop).ToString());
Assert.AreEqual(goldBeforeBattle, match.Snapshot.LocalPlayer.Gold);
foreach (var player in match.Snapshot.Players)
    Assert.AreEqual(shopsBeforeTerminalPresentation[player.PlayerId],
        string.Join(",", player.ShopSlots.Select(slot =>
            slot.ShopSlotId + ":" + slot.UnitTypeId + ":" + (slot.IsFrozen ? "1" : "0"))));
```

在现有等待 `Phase == Preparation` 完成后加入：

```csharp
var afterRound = match.Snapshot;
Assert.AreEqual(goldBeforeBattle, afterRound.LocalPlayer.Gold);
CollectionAssert.AreEqual(
    new[] { "5503", "1000", "1000", "5503", "5503", "5503" },
    afterRound.LocalPlayer.ShopSlots.Select(slot => slot.UnitTypeId));
Assert.IsTrue(afterRound.LocalPlayer.ShopSlots[0].IsFrozen);
Assert.IsTrue(afterRound.LocalPlayer.ShopSlots.Skip(1).All(slot => !slot.IsFrozen));
foreach (var remote in afterRound.Players.Where(player => player.PlayerId != match.LocalPlayerId))
    CollectionAssert.AreEqual(
        new[] { "1000", "1000", "1000", "5503", "5503", "5503" },
        remote.ShopSlots.Select(slot => slot.UnitTypeId));
```

本用例先主动刷新一次，因此战后被动刷新读取混合页；本地槽 `0` 冻结在 `5503`，其余可刷新槽排序填入，远端全六槽排序填入。现有终局死亡演出等待断言继续证明刷新不能在死亡动画和变黑完成前提前发生。

- [ ] **Step 2: 运行 PlayMode 并确认 RED**

运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath D:\2022.3.62f1c1\Editor\Unity.exe `
  -TestPlatform PlayMode `
  -TestFilter ArknoNights.Battle.Tests.PreparationBattleLoopPlayModeTests `
  -OutputDirectory Artifacts\ShopRefreshSorting\Task3-Red `
  -NoGraphics
```

预期：场景成功走完演出，但返回 Preparation 后商店仍是战前状态，新增的战后商店断言失败；既有阶段、双战斗、死亡演出等待和 PlayerState 不回写断言不应失败。

- [ ] **Step 3: 在唯一完成路径接入被动刷新**

在 `PreparationBattleLoopController.ReturnToPreparation()` 中，把调用放在完成日志之后、`multiBattle.Reset()` 之前：

```csharp
Debug.Log("[UI-009][battle.completed] " + LastBattleSummary, this);

matchState.RefreshAllShopsAfterBattle();
multiBattle.Reset();
```

该位置只会在 `Advance()` 已观察到 `MultiBattlePresentationState.Completed` 后执行，所以两场终局表现已经完成。必须位于 `multiBattle.Reset()` 之前：刷新发出的 `Changed` 发生时阶段仍为 Battle，现有 `HandleMatchChanged` 仍可使用尚未清空的 observation map 确认当前观察目标；若先 Reset，处理器会对空 observation map 调用 `SelectObservedPlayer` 并把回合误置为 Error。

不要在 `BattleHudSceneCoordinator.HandlePhaseChanged`、UI 重绘、`ResetPreparationUiState()` 或每个单场 battle 的完成回调中再次调用。

- [ ] **Step 4: 运行 PlayMode Green**

运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath D:\2022.3.62f1c1\Editor\Unity.exe `
  -TestPlatform PlayMode `
  -TestFilter ArknoNights.Battle.Tests.PreparationBattleLoopPlayModeTests `
  -OutputDirectory Artifacts\ShopRefreshSorting\Task3-Green `
  -NoGraphics
```

预期：测试数大于零、失败 `0`、跳过 `0`；阶段最终为 Preparation，未进入 Error；终局演出完成前商店不变，完成后四店刷新，本地赤金保持 `6`。

- [ ] **Step 5: 审查并提交 Task 3**

运行：

```powershell
git diff --check
git diff -- Assets/Game/Runtime/Initial/PreparationBattleLoopController.cs `
  Assets/Game/Tests/PlayMode/Battle/PreparationBattleLoopPlayModeTests.cs
git add -- Assets/Game/Runtime/Initial/PreparationBattleLoopController.cs `
  Assets/Game/Tests/PlayMode/Battle/PreparationBattleLoopPlayModeTests.cs
git diff --cached --check
git commit -m "feat: refresh shops on completed battle round"
```

预期：提交只包含回合接线和对应 PlayMode 测试。

---

### Task 4: 机制文档、全量验证与最终范围审查

**Files:**
- Modify: `docs/SPEC.md`
- Modify: `docs/ARCHITECTURE.md`
- Modify: `docs/TEST_PLAN.md`

**Interfaces:**
- Consumes: Tasks 1–3 已通过的公开行为和测试证据。
- Produces: 与代码一致的权威机制、架构边界、可复查验证记录。

- [ ] **Step 1: 更新 SPEC 的商店刷新规则**

在 `docs/SPEC.md` 的“当前本地 UI Demo 使用以下临时确定性配置”区域保留固定三页内容，并把旧句
“刷新时保留冻结且尚未购买的商品”替换为以下明确规则：

```markdown
- 四名玩家各自维护一份独立的六槽商店；当前确定性 Demo 允许四名玩家从相同初始页面开始，远端商店暂不显示，也没有远端主动商店命令；
- 初始加载不额外排序。每次刷新生成的商品从左到右先按单位稀有度升序、再按十进制数值 `typeId` 升序排列；因此稀有度更高的商品位于更右侧，稀有度相同时数值 ID 更大的商品位于更右侧；
- 本地玩家主动刷新消耗 `1` 赤金，替换包括冻结商品在内的全部六槽，并清除全部冻结状态；
- 两场战斗演出全部完成、整轮从 Battle 返回 Preparation 时执行一次免费被动刷新，四名玩家在同一次领域变更中一起刷新；
- 被动刷新时，冻结商品保留原商品、原物理槽位和冻结状态；空槽与未冻结槽为可刷新槽，新商品排序后只按 `ShopSlotId` 从小到大填入这些可刷新槽；
- 当前固定循环页面只是测试商品来源，未来将由按玩家等级概率随机生成替代；页面游标不属于长期公开机制。
```

保留冻结不禁止购买、批量冻结、即时主动刷新和 UI-only 免费费用表现接口的既有规则。

- [ ] **Step 2: 更新架构边界**

在 `docs/ARCHITECTURE.md` 的“正式 HUD 场景生命周期与证据入口”补充：

```markdown
- `LocalMatchState` 为每个 `LocalMatchPlayerData` 持有独立的六槽商店，快照为四名玩家分别投影商店；购买、冻结和付费主动刷新仍只定位 `LocalPlayerId`。`ShopOfferOrdering` 在领域层按目录稀有度、十进制数值 `typeId` 和生成顺序执行稳定排序，UI 继续只按稳定 `ShopSlotId` 显示 `snapshot.LocalPlayer`，不保存第二份排序。
- `PreparationBattleLoopController.ReturnToPreparation()` 在 `MultiBattlePresentationState.Completed` 后、清空 multi-battle observation map 前调用一次 `RefreshAllShopsAfterBattle()`；该同步操作保留各玩家冻结槽、刷新其余槽、推进临时固定商品源并只发布一次完整四玩家快照。`ResetPreparationUiState()` 仍只负责 ready 与观察目标，不负责商店刷新。
```

同时修正原架构文字中可能被理解为“战斗返回准备时商店绝不变化”的描述：保留 `ResetPreparationUiState()` 本身不改商店这一事实，并明确商店变化来自独立的战后领域操作。

- [ ] **Step 3: 运行全量 EditMode**

运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath D:\2022.3.62f1c1\Editor\Unity.exe `
  -TestPlatform EditMode `
  -OutputDirectory Artifacts\ShopRefreshSorting\Final-EditMode `
  -NoGraphics
```

预期：结果 XML 测试数大于零、失败 `0`、跳过 `0`。记录 `summary.txt` 的实际 total/failed/skipped、退出状态、XML 和日志路径；若失败，先定位根因，不把既有失败记为通过。

- [ ] **Step 4: 运行全量 PlayMode**

确认上一个 Unity 进程已经退出，再运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath D:\2022.3.62f1c1\Editor\Unity.exe `
  -TestPlatform PlayMode `
  -OutputDirectory Artifacts\ShopRefreshSorting\Final-PlayMode `
  -NoGraphics
```

预期：结果 XML 测试数大于零、失败 `0`、跳过 `0`。检查日志没有 `error CS`、`Compilation failed`、`Scripts have compiler errors`、未处理异常或 `round.observer.switch.failed`。

- [ ] **Step 5: 运行 Windows x64 StrictMode 构建**

串行运行：

```powershell
$env:ARKNIGHTS_BUILD_OUTPUT = 'G:\ARKnoNIGHTS_beta\Artifacts\ShopRefreshSorting\WindowsStandalone\ARKnoNIGHTS.exe'
& D:\2022.3.62f1c1\Editor\Unity.exe `
  -batchmode `
  -nographics `
  -quit `
  -projectPath G:\ARKnoNIGHTS_beta `
  -executeMethod Task006StandaloneBuild.BuildWindowsX64 `
  -logFile G:\ARKnoNIGHTS_beta\Artifacts\ShopRefreshSorting\WindowsBuild.log
$buildExitCode = $LASTEXITCODE
Remove-Item Env:ARKNIGHTS_BUILD_OUTPUT
if ($buildExitCode -ne 0) { exit $buildExitCode }
```

预期：进程退出码 `0`；`Temp/TASK-006/windows-standalone-build-summary.txt` 记录 `result=Succeeded`、`errors=0`，目标 EXE 存在。把实际 warnings、产物路径和日志路径记入测试文档。

- [ ] **Step 6: 写入实际验证证据**

在 `docs/TEST_PLAN.md` 追加“玩家商店刷新排序与整轮被动刷新（2026-07-28）”一节，逐项写入：

```markdown
- Task 1/2 EditMode Red 与 Green：实际测试数、失败项、零跳过，以及 `Artifacts/ShopRefreshSorting/...` 的 XML/日志路径；
- Task 3 PlayMode Red 与 Green：演出完成前未刷新、返回 Preparation 后四店刷新且本地赤金不变的实际结果；
- 最终全量 EditMode / PlayMode：实际 total、passed、failed、skipped 和进程退出情况；
- Windows x64 StrictMode：实际 result、errors、warnings、输出路径和日志路径；
- 范围检查：未修改商店 UI、场景、Prefab、Package、ProjectSettings 或用户已有资源改动；
- 未验证：交互式 Editor/Windows Player 中用真实鼠标观察刷新后的卡片视觉顺序，以及未来按玩家等级概率随机生成商品的实现。
```

只记录命令实际产生的数字和状态，不预填推测结果。

- [ ] **Step 7: 最终 diff、资源和日志审查**

运行：

```powershell
git diff --check
git status --short
git diff --stat
git diff -- Assets/Game/Runtime/Data/Player/ShopOfferOrdering.cs `
  Assets/Game/Runtime/Data/Player/LocalMatchState.cs `
  Assets/Game/Runtime/Initial/PreparationBattleLoopController.cs `
  Assets/Game/Tests/EditMode/Battle/LocalMatchStateEditModeTests.cs `
  Assets/Game/Tests/PlayMode/Battle/PreparationBattleLoopPlayModeTests.cs `
  docs/SPEC.md `
  docs/ARCHITECTURE.md `
  docs/TEST_PLAN.md
Select-String -Path Artifacts\ShopRefreshSorting\Final-EditMode\EditMode.log,`
  Artifacts\ShopRefreshSorting\Final-PlayMode\PlayMode.log,`
  Artifacts\ShopRefreshSorting\WindowsBuild.log `
  -Pattern 'error CS|Compilation failed|Scripts have compiler errors|Unhandled Exception|NullReferenceException|round\.observer\.switch\.failed'
```

确认：

- `ShopOfferOrdering.cs` 与 `.meta` 成对且 GUID 未与仓库其他 meta 重复；
- 新快照的四个商店数组彼此独立，没有共享 `ShopSlotData` 实例；
- `TryRefresh()` 失败路径没有推进临时页面、扣费、清冻结或通知；
- `RefreshAllShopsAfterBattle()` 只通知一次且不修改赤金、单位池、阵型、部署费用、生命、观察目标、准备状态和已封存输入；
- UI 仍读取 `snapshot.LocalPlayer`，没有修改用户正在编辑的两个 ShopReady 文件；
- 暂存区不包含现有大量资源、Bonds 文档、`.superpowers/` 或其他用户改动。

- [ ] **Step 8: 提交文档并确认工作树归属**

运行：

```powershell
git add -- docs/SPEC.md docs/ARCHITECTURE.md docs/TEST_PLAN.md
git diff --cached --check
git diff --cached --stat
git commit -m "docs: specify player shop refresh ordering"
git status --short
```

预期：最后一次提交只有三份权威文档；`git status --short` 中剩余项均为任务开始前已经存在或用户后来加入的未提交改动。

---

## Completion Gate

只有同时满足以下条件才可报告实现完成：

- SPEC 的主动刷新、被动刷新、冻结槽、数值 ID 和四玩家规则均有对应测试；
- 定向 Red 能证明旧实现不满足要求，定向 Green 全部通过；
- 最终全量 EditMode 和 PlayMode 都有非零测试数、零失败、零跳过；
- Windows x64 StrictMode 构建成功且没有编译错误；
- 最终 diff 无空白错误、无意外资源变化、无 UI 重复排序、无用户改动丢失；
- `docs/TEST_PLAN.md` 只记录实际运行证据；
- 人工鼠标/视觉检查和未来等级概率随机生成若未执行，明确标记为未验证。
