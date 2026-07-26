# Battle HUD Visual Corrections Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Correct the player list, shop, Ready control, fonts, colors, bulk freeze behavior, and screenshot evidence at `1920 × 1080`.

**Architecture:** `LocalMatchState` remains authoritative for shop contents and receives one atomic bulk-freeze command. `ShopReadyHudController` and `PlayerListHudController` remain presentation adapters; pure layout objects expose testable geometry, while actual color/font/texture fitting is verified in a visible Windows Player. No scene or prefab serialization is added.

**Tech Stack:** Unity 2022.3.62f1c1, C#, uGUI, Unity Test Framework/NUnit, existing Resources fonts and textures, Windows Standalone screenshot capture.

## Global Constraints

- Preserve the user-added `Assets/Resources/UI/Texture/player_list/bg_player_list.png` and its `.meta`.
- Do not modify scenes, prefabs, packages, or project settings.
- Do not modify battle calculation or Presentation Track authority.
- New code and evidence directories use semantic names, never task numbers.
- Chinese text compares existing HanYi and FangZheng fonts before considering a downloaded Source Han Sans font.
- Numeric-only values use `StagingHudController.FormalNumericFont`.
- Refresh executes once on the first click; purchase and upgrade retain two-click confirmation.
- Every implementation change follows Red → Green verification.

---

### Task 1: Atomic bulk freeze and immediate refresh

**Files:**
- Modify: `Assets/Game/Runtime/Data/Player/LocalMatchState.cs`
- Modify: `Assets/Game/UI/FormalHud/ShopReady/ShopReadyHudState.cs`
- Modify: `Assets/Game/UI/FormalHud/ShopReady/ShopReadyHudController.cs`
- Modify: `Assets/Game/Runtime/Initial/BattleHudCaptureRunner.cs`
- Test: `Assets/Game/Tests/EditMode/Battle/LocalMatchStateEditModeTests.cs`
- Test: `Assets/Game/Tests/EditMode/Battle/ShopReadyHudStateEditModeTests.cs`
- Modify: `docs/SPEC.md`
- Modify: `docs/references/ui/battle_hud/UI_SPEC.md`

**Interfaces:**
- Consumes: `LocalMatchState.Snapshot`, existing `LocalMatchShopSlotSnapshot.IsEmpty/IsFrozen`.
- Produces: `LocalMatchOperationResult LocalMatchState.TrySetOccupiedShopSlotsFrozen(bool frozen)` and `void ShopReadyHudController.ToggleAllFrozen()`.

- [ ] **Step 1: Write the failing state tests**

Add tests equivalent to:

```csharp
[Test]
public void BulkFreeze_MixedSlotsBecomeFrozenWithOneNotificationAndEmptySlotsStayUnfrozen()
{
    var state = Load();
    Assert.IsTrue(state.TryToggleFrozen(0).Success);
    Assert.IsTrue(state.TryPurchase(4).Success);
    var changes = 0;
    state.Changed += _ => changes++;

    var result = state.TrySetOccupiedShopSlotsFrozen(true);

    Assert.IsTrue(result.Success);
    Assert.AreEqual(1, changes);
    Assert.That(result.Snapshot.LocalPlayer.ShopSlots.Take(4), Is.All.Matches<LocalMatchShopSlotSnapshot>(slot => slot.IsFrozen));
    Assert.IsTrue(result.Snapshot.LocalPlayer.ShopSlots[4].IsEmpty);
    Assert.IsFalse(result.Snapshot.LocalPlayer.ShopSlots[4].IsFrozen);
}

[Test]
public void BulkFreeze_AllFrozenSlotsBecomeUnfrozenWithOneNotification()
{
    var state = Load();
    Assert.IsTrue(state.TrySetOccupiedShopSlotsFrozen(true).Success);
    var changes = 0;
    state.Changed += _ => changes++;

    var result = state.TrySetOccupiedShopSlotsFrozen(false);

    Assert.AreEqual(1, changes);
    Assert.That(result.Snapshot.LocalPlayer.ShopSlots, Is.All.Matches<LocalMatchShopSlotSnapshot>(slot => !slot.IsFrozen));
}
```

- [ ] **Step 2: Write the failing controller test**

```csharp
[Test]
public void Controller_BulkFreezeTogglesAllOccupiedSlotsAndRefreshExecutesImmediately()
{
    var match = Load();
    var root = new GameObject("ShopBehaviorTests", typeof(RectTransform));
    try
    {
        var controller = root.AddComponent<ShopReadyHudController>();
        controller.Initialize(match);

        controller.ToggleAllFrozen();
        Assert.That(match.Snapshot.LocalPlayer.ShopSlots, Is.All.Matches<LocalMatchShopSlotSnapshot>(slot => slot.IsFrozen));

        controller.ToggleAllFrozen();
        Assert.That(match.Snapshot.LocalPlayer.ShopSlots, Is.All.Matches<LocalMatchShopSlotSnapshot>(slot => !slot.IsFrozen));

        var gold = match.Snapshot.LocalPlayer.Gold;
        controller.RequestRefresh();
        Assert.AreEqual(gold - LocalMatchState.RefreshCost, match.Snapshot.LocalPlayer.Gold);
        Assert.AreEqual(ShopReadyConfirmation.None, controller.State.PendingConfirmation);
    }
    finally { Object.DestroyImmediate(root); }
}
```

- [ ] **Step 3: Run the focused EditMode tests and verify Red**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-UnityTests.ps1 `
  -UnityPath D:\2022.3.62f1c1\Editor\Unity.exe `
  -TestPlatform EditMode `
  -TestFilter ArknoNights.Battle.Tests.ShopReadyHudStateEditModeTests `
  -OutputDirectory Artifacts/BattleHudVisualAudit/bulk-shop-red `
  -NoGraphics
```

Expected: failure because `TrySetOccupiedShopSlotsFrozen` and `ToggleAllFrozen` do not exist or because first-click refresh still waits for confirmation.

- [ ] **Step 4: Implement the atomic state command**

Add:

```csharp
public LocalMatchOperationResult TrySetOccupiedShopSlotsFrozen(bool frozen)
{
    var changed = false;
    foreach (var slot in shopSlots)
    {
        if (slot.IsEmpty || slot.IsFrozen == frozen) continue;
        slot.IsFrozen = frozen;
        changed = true;
    }
    if (changed) NotifyChanged();
    return Result(LocalMatchOperationCode.Success);
}
```

Keep `TryToggleFrozen(int)` only for existing per-slot state tests and non-global callers.

- [ ] **Step 5: Implement controller semantics**

Replace the focused-slot button behavior with:

```csharp
public void ToggleAllFrozen()
{
    if (!preparationPhase || match == null || state == null) return;
    var occupied = state.Slots.Where(slot => !slot.IsEmpty).ToArray();
    if (occupied.Length == 0) return;
    var freeze = occupied.Any(slot => !slot.IsFrozen);
    Complete(match.TrySetOccupiedShopSlotsFrozen(freeze));
}
```

Bind `FreezeButton` to `ToggleAllFrozen`. Derive button text/art from whether all occupied slots are frozen. Change `RequestRefresh` to call `Complete(match.TryRefresh())` on the first click and remove the refresh confirmation branch from `ShopReadyConfirmation` and its tests. Update the capture runner to call `ToggleAllFrozen`.

- [ ] **Step 6: Run focused EditMode tests and verify Green**

Run the Step 3 command plus the `LocalMatchStateEditModeTests` filter. Expected: all selected tests pass, with non-zero counts.

- [ ] **Step 7: Update confirmed rules**

In `docs/SPEC.md` and `docs/references/ui/battle_hud/UI_SPEC.md`, state the mixed/all-frozen toggle rule and that Refresh does not use secondary confirmation.

- [ ] **Step 8: Commit behavior**

```powershell
git add -- Assets/Game/Runtime/Data/Player/LocalMatchState.cs Assets/Game/UI/FormalHud/ShopReady/ShopReadyHudState.cs Assets/Game/UI/FormalHud/ShopReady/ShopReadyHudController.cs Assets/Game/Runtime/Initial/BattleHudCaptureRunner.cs Assets/Game/Tests/EditMode/Battle/LocalMatchStateEditModeTests.cs Assets/Game/Tests/EditMode/Battle/ShopReadyHudStateEditModeTests.cs docs/SPEC.md docs/references/ui/battle_hud/UI_SPEC.md
git commit -m "fix(ui): correct shop freeze and refresh behavior"
```

---

### Task 2: Player-list, shop, Ready, font, and color geometry

**Files:**
- Modify: `Assets/Game/UI/FormalHud/StagingHudController.cs`
- Modify: `Assets/Game/UI/FormalHud/PlayerListObserver/PlayerListObserverCoordinator.cs`
- Modify: `Assets/Game/Runtime/Initial/BattleHudSceneCoordinator.cs`
- Modify: `Assets/Game/UI/FormalHud/ShopReady/ShopReadyHudController.cs`
- Modify: `Assets/Game/Tests/EditMode/Battle/PlayerListObserverEditModeTests.cs`
- Test: `Assets/Game/Tests/PlayMode/Battle/BattleHudSceneIntegrationPlayModeTests.cs`
- Add existing user asset to Git: `Assets/Resources/UI/Texture/player_list/bg_player_list.png`
- Add existing user metadata to Git: `Assets/Resources/UI/Texture/player_list/bg_player_list.png.meta`

**Interfaces:**
- Consumes: `PlayerListLayout.Calculate`, `StagingHudController.FormalNumericFont`, `FormalHudSpriteLoader`.
- Produces: testable `PlayerListLayoutSnapshot.Background`, a selected heavier Chinese font property, and stable semantic child names (`Background`, `HealthBackground`, `Self`, `UpgradeCostBackground`, `CostBackground`).

- [ ] **Step 1: Write failing pure-layout assertions**

Extend `FourPlayerLayout_IsLeftAnchoredAndDoesNotOverlapRows`:

```csharp
Assert.AreEqual(layout.Rows[0].X, layout.Background.X);
Assert.AreEqual(layout.Rows[0].YMin, layout.Background.YMin);
Assert.AreEqual(layout.Rows.Last().YMax, layout.Background.YMax);
Assert.AreEqual(layout.Rows[0].Width, layout.Background.Width);
```

- [ ] **Step 2: Write failing PlayMode visual-structure assertions**

After opening the shop, assert:

```csharp
var listBackground = playerListRoot.Find("Background").GetComponent<Image>();
Assert.AreEqual("bg_player_list", listBackground.sprite.name);

var avatar = localPlayerRow.Find("Avatar").GetComponent<RectTransform>();
var health = localPlayerRow.Find("HealthBackground").GetComponent<RectTransform>();
var self = localPlayerRow.Find("Self").GetComponent<RectTransform>();
Assert.That(health.rect.xMin + health.anchoredPosition.x, Is.GreaterThanOrEqualTo(avatar.rect.xMin + avatar.anchoredPosition.x).Within(.01f));
Assert.That(health.rect.xMax + health.anchoredPosition.x, Is.LessThanOrEqualTo(avatar.rect.xMax + avatar.anchoredPosition.x).Within(.01f));
Assert.That(health.rect.yMin + health.anchoredPosition.y, Is.GreaterThanOrEqualTo(avatar.rect.yMin + avatar.anchoredPosition.y).Within(.01f));
Assert.That(self.rect.xMin + self.anchoredPosition.x, Is.EqualTo(avatar.rect.xMin + avatar.anchoredPosition.x).Within(.01f));
Assert.AreSame(StagingHudController.FormalNumericFont, localPlayerRow.Find("HealthBackground/Life").GetComponent<Text>().font);

var first = slotRects[0];
var last = slotRects[4];
Assert.That(last.anchoredPosition.x + last.sizeDelta.x, Is.EqualTo(shopPanel.GetComponent<RectTransform>().rect.width).Within(.01f));
var upgrade = shopPanel.Find("UpgradeButton").GetComponent<RectTransform>();
Assert.That(upgrade.anchoredPosition.x + upgrade.sizeDelta.x, Is.EqualTo(first.anchoredPosition.x - 8f).Within(.01f));

var frame = first.Find("RarityFrame").GetComponent<RectTransform>();
var cost = first.Find("CostBackground").GetComponent<RectTransform>();
Assert.That(cost.anchoredPosition.x + cost.sizeDelta.x * .5f, Is.EqualTo(frame.sizeDelta.x * .5f).Within(.01f));
Assert.That(cost.anchoredPosition.y + cost.sizeDelta.y * .5f, Is.EqualTo(frame.sizeDelta.y).Within(.01f));
Assert.NotNull(upgrade.Find("UpgradeCostBackground").GetComponent<Image>().sprite);
```

Also assert not-ready uses `ready_icon`, ready uses `icon_ready`, numeric shop values use the numeric font, and Chinese labels use the selected Chinese font.

- [ ] **Step 3: Run focused PlayMode/EditMode tests and verify Red**

Use `scripts/Invoke-UnityTests.ps1` with:

- EditMode filter: `ArknoNights.Battle.Tests.PlayerListObserverEditModeTests`
- PlayMode filter: `ArknoNights.Battle.Tests.BattleHudSceneIntegrationPlayModeTests`
- evidence root: `Artifacts/BattleHudVisualAudit/layout-red`

Expected failures: missing background property/object, wrong inner geometry, wrong fonts/icons, missing upgrade cost background, left-offset card group, and cost background center above the frame.

- [ ] **Step 4: Implement player-list geometry**

Add a background rectangle to the pure layout spanning the first row top through the last row bottom. Build `bg_player_list` before all rows. At the initial reference fit:

```csharp
// Player row local geometry
Position(avatar.rectTransform, 58f, 70f, 92f, 92f);
Position(border.rectTransform, 58f, 70f, 108f, 108f);
Position(hp.rectTransform, 58f, 36f, 92f, 24f);
Position(hpIcon.rectTransform, 14f, 12f, 12f, 18f);
Position(value.rectTransform, 55f, 12f, 60f, 20f);
Position(self.rectTransform, 28f, 110f, 32f, 32f);
```

Use `FormalNumericFont` for Life. Keep the disconnected health background as `bg_hp`.

- [ ] **Step 5: Add the heavier Chinese font boundary**

Expose one cached property in `StagingHudController`, initially pointing to the selected candidate Resources path:

```csharp
public static Font FormalBoldUiFont
{
    get
    {
        if (formalBoldUiFont == null) formalBoldUiFont = Resources.Load<Font>(formalBoldUiFontPath);
        return formalBoldUiFont != null ? formalBoldUiFont : FormalUiFont;
    }
}
```

Use this only for compact shop/Ready Chinese labels and unit names. Do not change the already-approved information-panel typography during the candidate comparison.

- [ ] **Step 6: Implement shop and Ready geometry**

Use a shared calculation:

```csharp
const float panelWidth = 1070f;
const float cardWidth = 158f;
const float gap = 8f;
const float upgradeWidth = 111f;
var firstCardX = panelWidth - (5f * cardWidth + 4f * gap); // 248
var upgradeX = firstCardX - gap - upgradeWidth;            // 129
```

Place the upgrade cost background at `(33.5, 157.5, 44, 35)` and each card cost background at `(57, 157.5, 44, 35)`. Swap Ready icon resources. Center labels in the icon-free button area and define explicit color constants for every created text element. Use a `NumberLabel` factory for numeric values and the heavier Chinese font for labels/names.

- [ ] **Step 7: Run focused tests and verify Green**

Repeat Step 3. Expected: both filtered suites pass with non-zero counts.

- [ ] **Step 8: Commit structural visual corrections**

```powershell
git add -- Assets/Resources/UI/Texture/player_list/bg_player_list.png Assets/Resources/UI/Texture/player_list/bg_player_list.png.meta Assets/Game/UI/FormalHud/StagingHudController.cs Assets/Game/UI/FormalHud/PlayerListObserver/PlayerListObserverCoordinator.cs Assets/Game/Runtime/Initial/BattleHudSceneCoordinator.cs Assets/Game/UI/FormalHud/ShopReady/ShopReadyHudController.cs Assets/Game/Tests/EditMode/Battle/PlayerListObserverEditModeTests.cs Assets/Game/Tests/PlayMode/Battle/BattleHudSceneIntegrationPlayModeTests.cs
git commit -m "fix(ui): align battle HUD layout and typography"
```

---

### Task 3: Font comparison, visible screenshot loop, and final verification

**Files:**
- Modify if screenshot evidence requires: files from Task 2 only
- Modify: `docs/references/ui/battle_hud/UI_SPEC.md`
- Modify: `docs/ARCHITECTURE.md`
- Modify: `docs/TEST_PLAN.md`
- Evidence only: `Artifacts/BattleHudVisualAudit/font-hanyi/`
- Evidence only: `Artifacts/BattleHudVisualAudit/font-fangzheng/`
- Evidence only: `Artifacts/BattleHudVisualAudit/final/`

**Interfaces:**
- Consumes: `BattleHudCaptureRunner` command-line entry and all Task 1/2 tests.
- Produces: selected Chinese font path, final screenshot manifest, test/build evidence, and updated documentation.

- [ ] **Step 1: Build and capture the HanYi candidate**

Set `FormalBoldUiFont` to `Fonts/HanYiCuHeiJian-1`, run the Windows build into `Artifacts/BattleHudVisualAudit/font-hanyi/WindowsStandalone/`, and launch the Player visibly with:

```powershell
ARKnoNIGHTS.exe -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 -battleHudCapture -uiCaptureOutput <font-hanyi-captures> -logFile <font-hanyi-player.log>
```

- [ ] **Step 2: Build and capture the FangZheng candidate**

Change only the candidate path to `Fonts/FangZhengHeiTiJianTi-1`, repeat the same build/capture into `font-fangzheng`, and inspect equivalent shop-open, frozen, Ready, player-list, and unit-information images.

- [ ] **Step 3: Select the font**

Choose the candidate that:

- remains readable at the Freeze/Refresh/Ready sizes;
- does not collide with icons;
- most closely matches figures 7 and 8 in visible weight;
- does not degrade Chinese unit-name legibility.

Record the comparison in `docs/TEST_PLAN.md`. If both fail these criteria, stop before downloading and document official Source Han Sans Medium/Bold source, license, version, file, and rollback.

- [ ] **Step 4: Iterate visible layout screenshots**

For each iteration:

1. build the current exact source;
2. launch the Player without hiding its window;
3. inspect `01_preparation_closed`, `02_shop_open`, `03_shop_upgrade_confirmation`, `05_shop_purchase_confirmation`, `06_shop_frozen`, `08_ready_shop_still_available`, `09_observe_remote_player`, and `11_player_disconnected_list`;
4. compare player-list geometry to figure 8 and shop/Ready geometry to figures 7/8;
5. change only measured constants or text colors;
6. repeat until the reported defects are absent.

- [ ] **Step 5: Validate the final manifest**

Parse `battle-hud-manifest.json` and assert:

- 17 unique PNGs, all `1920 × 1080`;
- every occupied slot in frozen capture 06 has `frozen=True`;
- screenshot 07 contains a real empty slot;
- battle screenshots hide the shop;
- same-Tick screenshots have equal presentation Tick;
- expected semantic rectangles are present.

- [ ] **Step 6: Run full verification**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-UnityTests.ps1 -UnityPath D:\2022.3.62f1c1\Editor\Unity.exe -TestPlatform EditMode -OutputDirectory Artifacts/BattleHudVisualAudit/final/EditMode -NoGraphics
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-UnityTests.ps1 -UnityPath D:\2022.3.62f1c1\Editor\Unity.exe -TestPlatform PlayMode -OutputDirectory Artifacts/BattleHudVisualAudit/final/PlayMode -NoGraphics
```

Run a strict Windows x64 build and record result, error count, warning count, and output path.

- [ ] **Step 7: Update documentation**

Update UI font choice, exact final rectangles, colors, Ready mapping, bulk-freeze rule, immediate Refresh behavior, screenshot paths, test counts, build result, and remaining manual mouse/animation checks.

- [ ] **Step 8: Review and commit**

Check:

```powershell
git diff --check
git status --short
git diff --name-only
```

Verify no scene, prefab, package, or project-setting changes and preserve unrelated `.superpowers/`. Commit:

```powershell
git add -- <only reviewed implementation, tests, resource, and documentation files>
git commit -m "fix(ui): finish battle HUD visual correction loop"
```
