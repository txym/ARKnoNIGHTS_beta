# UI-INFO-001 Design

## Goal

Complete the upper half of `UnitInformationPanel` with a single read-only unit-detail projection for staging, deployed, and battle-enemy selections. The implementation also replaces the UI-004 fixed Away extraction with an independent temporary opponent PlayerState input and preserves instance elite level through battle presentation.

## Scope and boundaries

- Keep `PlayerState` authoritative for persistent local/opponent unit ownership, formation, elite level, buffs, and deployment cost.
- Keep `BattleInput` authoritative for a sealed battle's definitions and per-instance metadata; keep Presentation authoritative for live battle HP/alive state.
- Add no Buff calculation, multiplayer model, shop/life rules, lower-panel content, Core combat behavior, scene/prefab rewrite, or art/meta changes.
- Empty Chinese display names remain explicit placeholders; technical resource keys are never shown as player-visible names.

## Data flow

```text
local PlayerState + temporary opponent PlayerState + UnitCatalog
  -> pre-seal duplicate unit-ID validation
  -> sealed BattleInput (Home/Away UnitSnapshot including eliteLevel)
  -> Battle Presentation live HP/alive state
  -> immutable UnitDetailSnapshot
  -> FormalBattleHudUi005 binding only
```

The temporary opponent is loaded from its own Player-safe Resources JSON and has a stable, distinct player ID and unit IDs. It is never modified by deployment commands or combat playback. The existing fixed battle fixture remains a regression input only.

## Detail projection

Create a focused immutable detail type and a resolver owned by the existing coordinator/state boundary. The projection contains identity and ownership, player-visible/catalog fields, portrait/rarity/elite metadata, current/max HP, six runtime attributes, block/cost/life-deduct configuration values, availability flags, and structured diagnostics.

- Preparation resolves selected local Staging/Deployed instances from `PlayerStateSnapshot` plus `UnitCatalog`; current HP equals max HP.
- Battle resolves by exact `unitId` from the sealed `BattleInput` and current Presentation state. It does not reopen source JSON or match only by `typeId`.
- For a non-empty Buff without an authoritative computed-attributes result, the six dynamic attributes are unavailable and bind as `--`; static confirmed configuration values remain visible.
- The resolver is read-only and never writes PlayerState, runtime combat state, BattleRunResult, or events.

## UI binding and selection

Split the current monolithic information-panel construction into focused builders for the shell, identity/portrait overlays, stat entries, and detailed HP bar. A single binder consumes `UnitDetailSnapshot` and presentation sprites.

The binder implements the UI_SPEC 9.3 reference-region scale, exact stat order, name placeholder/diagnostic rule, enum mappings, 45x45 rarity overlays, elite overlay, numeric formatting without `%` or `s`, and the existing HP fill/number-anchor formula. The lower tabs remain placeholders.

The shared selected unit ID stays authoritative across staging, deployed, and enemy selection. Enemy selection only refreshes the projection; it does not surface retreat, drag, or PlayerState commands. Selection and event subscriptions are cleared symmetrically on battle cleanup, replay rebuild, phase change, disable, and destroy.

`StagingHudController` adds a 45x45 rarity overlay anchored to the visible portrait's top-right. Invalid rarity hides the overlay and emits a type-ID diagnostic.

## Battle and compatibility changes

`UnitSnapshot` receives an explicit `eliteLevel` with validation. Repository fixtures receive explicit values where required; the existing local battle fixture stays supported and keeps its battle behavior unchanged. Canonical summaries are updated deliberately to include this metadata, and tests distinguish metadata digest changes from combat result changes.

The preparation sealer loads the catalog, local PlayerState, and temporary opponent PlayerState, checks cross-player ID collisions with player IDs and collision IDs in the stable error, then maps them to Home/Away and still relies on `BattleInputFactory` for final validation.

## Verification

Implement tests first for opponent loading/ID collisions, elite propagation and combat invariance, detail field authority, unavailable Buff attributes, HP isolation, formatting, selection permissions/lifecycle, and staging rarity layout. Extend the existing UI-005 capture suite and manifest for the required selection sources, 50% HP, and natural/compressed rarity states. Run applicable EditMode and PlayMode tests, compile, build Windows Player when available, and visually inspect generated images before reporting success.

Update SPEC, ARCHITECTURE, and TEST_PLAN only with implemented behavior and actual verification evidence.
