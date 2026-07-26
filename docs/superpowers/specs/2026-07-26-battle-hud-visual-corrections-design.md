# Battle HUD visual corrections design

## Goal

Correct the currently visible player-list, shop, and ready-button layout without changing battle authority, shop pricing, player ownership, scene assets, prefabs, packages, or project settings. The finished layout must be adjusted through repeated `1920 × 1080` screenshots from a visible Windows Player.

## Confirmed behavior

- The shop freeze button targets every non-empty shop slot.
- If any non-empty slot is not frozen, one click freezes all non-empty slots.
- If every non-empty slot is frozen, one click unfreezes all non-empty slots.
- Empty slots are never frozen.
- Frozen products remain purchasable.
- Refresh executes on the first click and has no confirmation state.
- Purchase and upgrade retain their existing two-click confirmation.
- The latest Ready icon mapping overrides the earlier mapping:
  - not ready: `ready_icon.png`;
  - ready: `icon_ready.png`.
- The five shop slots form one right-aligned group. The upgrade button sits immediately to the left of that group with the same visual gap; it is not anchored to the shop panel's left edge.

## Player list

- Add one `bg_player_list.png` image behind all four player entries.
- The background and rows use one shared height-scaled geometry source so the background encloses the complete list and does not become an unrelated full-screen decoration.
- Keep the player list on the left and iteratively fit its top offset, width, height, row spacing, and internal margins against figure 8.
- Keep each player's health background, health icon, and life value completely inside the avatar-frame region.
- Align the left edge of `icon_self.png` with the left edge of the avatar region. The icon may still extend vertically beyond the avatar frame as allowed by the UI specification.
- Disconnection remains represented by `icon_lost_connect.png`; it does not select `bg_lose_hp.png`.

## Fonts and colors

- Reuse the existing bundled `NotoSansSC-VF.ttf` for Chinese text and request `FontStyle.Bold`. No external font is downloaded or added. If the resulting weight is still visually too light in the Player screenshot, stop and request a static Source Han Sans Medium/Bold asset from the user.
- Use `Novecento wide Normal Regular` for every numeric-only HUD value in this surface:
  - player life;
  - level;
  - shop price;
  - upgrade price;
  - refresh price.
- Chinese unit names and button labels use the bold Source Han/Noto font.
- Initial color targets, subject to screenshot fitting:
  - player life and level numbers: white;
  - shop unit name: light neutral gray;
  - shop, upgrade, and refresh price numbers: pale warm white;
  - Freeze label: dark teal on the cyan button;
  - Refresh label: dark brown on the yellow button;
  - Ready label: dark teal on the bright ready background.
- Text rectangles must be positioned using their icon and button geometry. Button labels are visually centered in the free horizontal space to the right of their icon, rather than centered across the whole button.

## Shop geometry

- At the reference resolution, calculate the five-card group from the shop panel's right edge:
  - card size remains `158 × 175`;
  - four equal gaps separate the five cards;
  - the fifth card's right edge aligns with the shop panel's right edge.
- Place the `111 × 175` upgrade button immediately left of the first card using the same gap.
- Add `cost_bg_1.png` or `cost_bg_2.png` behind the upgrade price. Hide the upgrade cost background at maximum level when the text is `MAX`.
- For every product card, align the horizontal center of `cost_bg_1/2` with the card center and align its vertical center exactly to the top edge of the rarity frame.
- Keep Freeze and Refresh below the shop group and adjust their label/icon/cost positions by screenshot rather than altering the texture aspect ratios.

## State ownership

- Add one atomic state-layer operation for setting the frozen state of all non-empty slots. It performs at most one state version increment and one `Changed` notification.
- The HUD derives the next bulk state from the current snapshot:
  - `freeze = occupiedSlots.Any(slot => !slot.IsFrozen)`.
- The HUD does not own a duplicate frozen-state collection.
- Refresh no longer enters or renders `ShopReadyConfirmation.Refresh`; obsolete refresh-confirmation paths may be removed if no compatibility caller requires them.

## Validation

1. EditMode Red/Green tests:
   - mixed occupied slots become all frozen in one command;
   - all occupied frozen slots become all unfrozen;
   - empty slots remain unfrozen;
   - bulk operation produces one state change;
   - refresh spends one gold and advances the page on its first request.
2. PlayMode Red/Green layout tests:
   - player-list background exists behind all rows;
   - health geometry is inside the avatar region;
   - self icon left edge equals avatar left edge;
   - numeric texts use the numeric font and Chinese labels use bold UI font;
   - Ready icons use the latest mapping;
   - upgrade/card group is right-aligned;
   - upgrade and card cost backgrounds have the required centers.
3. Build a Windows Standalone Player.
4. Run the semantic `BattleHudCaptureRunner` in a visible window and capture the full state matrix.
5. Compare the player list to figure 8 and the shop/Ready states to figures 7 and 8. Repeat until the inspected screenshots no longer show the reported misalignment.

## Scope limits

- Do not change battle calculation, track playback, formation rules, unit JSON, scene files, prefabs, packages, or project settings.
- Do not introduce a new font asset without user approval.
- Do not rename files after task numbers; all newly introduced code, evidence directories, and documentation use semantic names.
