# Unit JSON Naming — Phase 2 Plan

## Goal

Rename the three authoritative unit source JSON files to
`<typeId>_<resourceKey>.json` while preserving their Unity `.meta` GUIDs and
keeping the generated runtime catalog consistent.

## Scope

- Rename only these source JSON and `.meta` pairs:
  - `gopro` → `1000_gopro`
  - `arcslma` → `5503_arcslma`
  - `arcslmi` → `5504_arcslmi`
- Update direct source-file consumers and current documentation.
- Regenerate `Assets/Resources/BattleData/unit-catalog-v1.json` through
  `UnitCatalogGenerator` so each `sourceFile` value is canonical.

## Out of scope

- Do not alter source JSON payloads, resource keys, resource folders, Spine
  assets, avatars, downloaded external data, or variant suffix policy.

## Risks and safeguards

- Move each JSON together with its existing `.meta` file using `git mv`, then
  compare GUIDs before and after.
- The catalog generator discovers JSON by glob, so a test will assert both the
  new disk paths and generated `sourceFile` names.
- Historical migration plans remain historical records; update maintained
  reference documentation only.

## Verification

1. Run the focused EditMode test after changing its expected paths; it must
   fail before the files move.
2. Run `UnitCatalogGenerator.Generate` after the moves.
3. Verify catalog source-file values, source JSON GUIDs, direct references,
   and changed-file scope.
4. Run the relevant full EditMode and PlayMode test suites in a single Unity
   process queue.

## Stop conditions

Stop if an existing source JSON lacks its `.meta`, GUID preservation cannot be
proved, the generator no longer finds exactly three source files, or Unity
reports a new compile/test failure attributable to this migration.
