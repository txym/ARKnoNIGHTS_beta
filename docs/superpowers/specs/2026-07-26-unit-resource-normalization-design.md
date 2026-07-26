# Unit Resource Normalization Design

## Goal

Normalize the three confirmed unit source files and their Resources assets without treating downloaded model suffixes such as `_2` and `_3` as part of the unit directory or source JSON filename. Remove all other current unit resource directories.

## Confirmed Scope

The only retained units are `gopro`, `arcslma`, and `arcslmi`.

| Unit | Type ID | `resourceKey` | Source JSON | Characters directory | Avatar |
|---|---:|---|---|---|---|
| gopro | 1000 | `gopro` | `1000_gopro.json` | `1000_gopro` | `UIImage_1000_gopro.png` |
| arcslma | 5503 | `arcslma` | `5503_arcslma.json` | `5503_arcslma` | `UIImage_5503_arcslma.png` |
| arcslmi | 5504 | `arcslmi` | `5504_arcslmi.json` | `5504_arcslmi` | `UIImage_5504_arcslmi.png` |

The underlying Spine asset names remain unchanged. In particular, the gopro skeleton stays `enemy_1000_gopro_3_SkeletonData`; `_3` is not included in its directory, JSON filename, avatar filename, or `resourceKey` during this migration.

The obsolete `Assets/Resources/Characters/go` and `Assets/Resources/Characters/mdgint` directories, including each directory `.meta`, are deleted. No other Resources category, Spine runtime file, scene, prefab, or package is in scope.

## Naming and Path Contract

`resourceFolderName` is not added to `UnitJson`.

The resource folder is deterministically derived in both editor and runtime code as:

```text
<typeId>_<resourceKey>
```

For example, type ID `1000` and `resourceKey` `gopro` resolve the skeleton directory to `Characters/1000_gopro/`. The avatar remains a flat `ProfilePicture/` asset and is referenced by its exact normalized filename.

The JSON filename is validated against the same full key. The generated Player-safe catalog preserves the short `resourceKey`, uses the renamed source filename, and emits the derived skeleton and normalized portrait resource paths.

## Migration Steps

1. Move each retained source JSON and its `.meta` to the full-key filename, then update `profilePictureResourceName` only.
2. Move each retained Characters directory and its directory `.meta` to the full key; do not rename any contained Spine asset.
3. Move each retained avatar PNG and its `.meta` to the `UIImage_<full-key>.png` name.
4. Delete `go`, `mdgint`, and their directory `.meta` files.
5. Replace all editor/runtime path composition that assumes `Characters/<resourceKey>/` with one shared, explicit `<typeId>_<resourceKey>` composition rule. Update the catalog generator, UnitFactory, spine probe, hard-coded HUD portrait paths, generated catalog, and directly affected tests.
6. Regenerate the Player-safe catalog through the existing Unity editor generator; do not hand-edit a catalog that claims to be generated.

## Safety and Validation

- Every move preserves the original `.meta` alongside the renamed/moved asset, preserving Unity GUIDs.
- Before deletion, verify `go` and `mdgint` have no retained source JSON and no repository references outside their own asset paths.
- Add or update tests for source filename/full-key agreement and derived resource paths.
- Run the relevant Unity EditMode and PlayMode tests serially, followed by the unit catalog regeneration and resource smoke checks. If Unity cannot run, report those steps as unverified rather than inventing generated output.
- Inspect the final diff for unexpected `.meta` changes, scene/prefab edits, and any remaining obsolete resource path.

## Deliberate Deferrals

- No imported PRTS batch assets are added in this migration.
- No variants such as `_2` or `_3` are represented in folder names or JSON filenames.
- No new `UnitJson` field is introduced.
- Future short-key sidecar files inside unit directories are out of scope.
