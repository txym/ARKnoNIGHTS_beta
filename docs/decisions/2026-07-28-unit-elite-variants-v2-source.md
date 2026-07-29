# ADR: Self-contained unit elite variant v2 sources

- Status: Accepted
- Date: 2026-07-28

## Decision

`Assets/GameData/Units/EliteVariants/Json` is the only authored unit source.
Each TypeId owns one `unit-elite-variants-v2` document containing common rules
and all imported elite variants. Model animation facts use a keyed registry;
playback behavior stays in the animation layer.

The first implemented migration covers TypeIds `1000`, `5503`, and `5504`.
Elite 0 is complete for those documents. Higher elite entries inherit omitted
atomic blocks from the nearest lower entry, while `sourceVariant` remains the
authority for the physical resource folder.

The 2026-07-29 authored-data import extends this authority to the 99 current
BONDS TypeIds while retaining legacy/demo TypeId `1000`: 100 documents and 185
model variants in total. TypeId `1021` is outside the current scope. The
independent animation layer and runtime elite 2/3 selection remain later work.

## Compatibility

The existing Player catalogs remain frozen during the subsequent bulk import.
Formal Player runtime continues to read only the generated Resources catalogs;
it never reads authored Editor sources. Legacy v1 projection uses mapped
skeleton type 2 and an empty Hit name. Sources that v1 cannot represent, such
as a valid v2 non-attacker/non-blocker, fail instead of being coerced.

Authored BONDS rarity comes from `docs/bonds/BONDS_SPEC.md`, while retained
legacy/demo TypeId `1000` uses rarity `1`; the frozen catalog continues to
expose its pre-migration three-TypeId values until a later catalog migration.
The generator's mapped Type 2 is temporary presentation transport and is not
an authored unit fact.

## Destruction

Remove the source-reading UnitFactory adapter after formal runtime data no
longer depends on it. Remove the legacy Hit chain after the old presentation
catalog and playback interfaces are retired. The complete Hit destruction list
remains in `docs/bonds/UnitAnimation.md`.
