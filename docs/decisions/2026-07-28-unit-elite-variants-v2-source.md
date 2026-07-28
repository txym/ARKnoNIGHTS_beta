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
authority for the physical resource folder. Importing the remaining units and
implementing the independent animation layer are later work.

## Compatibility

The existing Player catalogs are frozen during the first three-unit migration.
Formal Player runtime continues to read only the generated Resources catalogs;
it never reads authored Editor sources. Legacy v1 projection uses mapped
skeleton type 2 and an empty Hit name. Sources that v1 cannot represent, such
as a valid v2 non-attacker/non-blocker, fail instead of being coerced.

Authored v2 rarity is `1000=1`, `5503=6`, and `5504=3`; the frozen catalog
continues to expose its pre-migration values until a later catalog migration.
The generator's mapped Type 2 is temporary presentation transport and is not
an authored unit fact.

## Destruction

Remove the source-reading UnitFactory adapter after formal runtime data no
longer depends on it. Remove the legacy Hit chain after the old presentation
catalog and playback interfaces are retired. The complete Hit destruction list
remains in `docs/bonds/UnitAnimation.md`.
