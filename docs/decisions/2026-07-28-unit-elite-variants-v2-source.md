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

Formal Player runtime continues to read only generated Resources catalogs; it
never reads authored Editor sources. On 2026-07-30 the deferred catalog
migration was completed: `unit-catalog-v1` now projects all 100 elite-zero
TypeIds, `ability-catalog-v1` projects all 67 authored abilities, and the skill
animation catalog contains all 13 required bindings.

The flat transport now represents non-attacking units explicitly with
`AttackMethod=None`, `DamageType=None`, zero attack/timings, and an empty attack
animation. Legacy skeleton type 2 and the empty Hit name remain presentation
transport rather than authored facts.

Authored BONDS rarity comes from `docs/bonds/BONDS_SPEC.md`. The LAN Match shop
catalog is a filtered view of the complete battle catalog: 94 entries are shop
eligible, while legacy/demo `1000` and non-shop `1137`, `1138`, `2033`, `5504`,
and `10002` remain loadable for demos and summon chains but never enter the
shared shop pool.

## Destruction

Remove the source-reading UnitFactory adapter after formal runtime data no
longer depends on it. Remove the legacy Hit chain after the old presentation
catalog and playback interfaces are retired. The complete Hit destruction list
remains in `docs/bonds/UnitAnimation.md`.
