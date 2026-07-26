# Room Select Create Decoration Overlap Recovery Design

## Status

Approved in conversation on 2026-07-27.

This design replaces the failed assumptions in
`2026-07-26-room-select-create-decoration-correction-design.md`. It applies
only to the Figure 9 Create upper-decoration region and its visual evidence.

## Goal

Reproduce the Figure 9 Create upper-decoration region with:

- four broad, tapered cyan wing silhouettes built by overlapping real
  `img_pointer` Sprite Images;
- a dark interior backing that prevents the Home map contours from showing
  through the Create decoration;
- a continuous low-brightness outline built only from overlapping real
  `doc_frame_line` Sprite Images;
- the existing central `room_select_create_*` and `room_select_dot` Sprites;
- automated evidence that measures the four wings independently and always
  publishes a complete failure report.

The already accepted Create/Join action bars and the LAN room workflow remain
frozen.

## Root-cause evidence

Three real 1920x1080 Player cycles under
`Artifacts/LAN-LOBBY/CreateDecorationCorrection/` established that the prior
architecture was wrong:

1. One aspect-preserving `img_pointer` per visible wing renders as a thin
   diagonal line. Figure 9 uses broad, filled, tapered wings.
2. The prior left/right whole-side connected-component ROIs include the Home
   map contours. Those contours connect to the thin wings and produce one
   large component instead of two isolated wings.
3. The Create upper region has no dark backing, unlike Figure 9. This makes
   both the visual mismatch and the detector contamination worse.
4. The fixed nine-segment frame layout achieves only `0.4231884058` top-edge
   coverage with a `335 px` gap. Segment count and RectTransform extent were
   mistaken for visible-alpha coverage.
5. The exporter throws when the requested component count is missing, so
   `VisualDiff-1`, `VisualDiff-2`, and `VisualDiff-3` are not published. A
   visual mismatch must be data in the report, not an exception that destroys
   the report.

The cycle-2 symmetric wing centers, widths, and mirrored rotations are a useful
starting point, but not an accepted final result. The cycle-3 alpha increase
was reverted because it did not improve the component gate and made the thin
line mismatch more conspicuous.

## Scope

### In scope

- The Create upper decoration above the accepted `创建同盟` action bar.
- Runtime hierarchy and Sprite instance composition for the dark backing,
  wings, central decoration, and `doc_frame_line` outline.
- Capture manifest provenance/counts for the revised repeated instances.
- Four-wing and frame visual measurement.
- At most three new, materially different real Player calibration cycles.
- Documentation and evidence for the final accepted result.

### Frozen and out of scope

- Create and Join action Rects, icons, labels, backgrounds, sibling order, and
  click behavior.
- Join upper decoration and join-code behavior.
- LAN discovery, room number, host/join transport, room state, and disconnect
  behavior.
- Avatar/ID UI, Room page, and Figure 10.
- Unity Editor, package, render-pipeline, and Input System versions.
- Any texture outside the approved autochess material roots.

## Material and provenance rules

1. Every texture-backed element must use an approved Sprite already imported
   from the specified autochess material library. No `$0` or `#0` source is
   allowed.
2. Every wing layer uses `Home/img_pointer`.
3. Every visible Create outline segment uses `Home/doc_frame_line`.
4. `room_select_create_logo` has zero active runtime occurrences.
5. No code-drawn line, `Outline`, procedural mesh, preset frame texture, or
   code-native border geometry may contribute to the visible outline.
6. The dark backing is a plain uGUI `Image` with `sprite == null`,
   `raycastTarget == false`, and a uniform semi-transparent black color. It is
   a fill only: it has no border, outline, material texture, or shader effect.
7. Repeated and overlapping Sprite instances are explicitly allowed. Instance
   counts are determined by the accepted visual result, not by the previous
   four-wing/nine-frame limits.

## Approaches considered

### Selected: repeated Sprites, dark backing, and isolated measurement

Overlap several aspect-preserving `img_pointer` instances per semantic wing,
add the approved plain dark backing, stitch the frame from measured visible
`doc_frame_line` spans, and measure four independent wing ROIs. This uses only
the approved visual sources, matches the reference construction, and removes
the known map-contour coupling.

### Rejected: one non-uniformly stretched Sprite per wing

Increasing the height of one `img_pointer` could make its bounding box look
larger, but it would distort the source and preserve a bar-like silhouette.
The previous UI review also identified unexplained texture enlargement as a
failure mode.

### Rejected: keep the transparent panel and subtract the background in tests

A background-only capture could make the detector more stable, but the player
would still see map contours through a region that is dark in Figure 9. That
would improve measurement while leaving the actual UI wrong.

## Runtime composition

The Create hierarchy is ordered back-to-front:

1. `InteriorBacking`
2. `CreateFrame`
3. `Wings`
4. existing central decorations
5. `CreateAction`

`CreateAction` remains the final Create child. Every backing, frame, wing, and
central decoration Graphic has `raycastTarget == false`.

### Interior backing

`InteriorBacking` is a plain black uGUI Image covering only the framed Create
upper region. Its initial alpha is `0.78`. Its Rect is calibrated against
Figure 9 so the map contours are suppressed inside the panel without extending
over either accepted action bar.

The backing is tested by rendered behavior:

- it has no Sprite;
- it does not receive raycasts;
- it is behind all Create decoration Sprites;
- the map contours do not produce qualifying cyan components in the four wing
  measurement ROIs outside the wing masks.

### Four visible wing composites

There are four semantic wing groups:

- `WingLeftUpper`
- `WingLeftLower`
- `WingRightUpper`
- `WingRightLower`

Each semantic wing is a composite of two to four `img_pointer` Images. The
initial implementation uses four layers per semantic wing, for sixteen
instances total. Layers share a mirrored base rotation and source-preserving
height, but use progressively shorter widths and small perpendicular offsets.
Their outer ends align while their inner ends step inward, producing a broad
taper rather than four parallel full-length bars.

Calibration may reduce a semantic wing to two or three layers if the reference
silhouette is already met. It may not exceed four layers. All four semantic
wings must use the same layer count and the same width/offset pattern. The
right pair mirrors the left pair; upper and lower pairs mirror around the
horizontal centerline. No layer may be non-uniformly stretched.

The final layer count, centers, widths, offsets, rotations, tint, and sibling
order are frozen in `LanLobbyViewPlayModeTests`.

### Stitched frame

The frame uses only `doc_frame_line` Images. The old exact-nine requirement is
removed.

- Each straight edge consists of enough overlapping segments to cover the
  measured visible-alpha span.
- A right-top chamfer is formed by one or more rotated
  `doc_frame_line` instances.
- Adjacent visible-alpha spans overlap by `10..24` design pixels.
- The implementation may use between 9 and 16 frame segments.
- All frame segments use the same low-brightness tint and remain behind the
  wings and central decoration.

The chosen final count and each segment transform are frozen in the View test
and reported by the capture manifest.

## Visual measurement

### Four independent wing rows

The old `wing-left` and `wing-right` whole-side rows are removed. They are
replaced by:

- `wing-left-upper`
- `wing-left-lower`
- `wing-right-upper`
- `wing-right-lower`

Each row has a narrow, non-overlapping ROI derived from Figure 9. The ROI may
contain only one semantic wing and its dark backing. Measurement selects the
largest qualifying cyan component in that ROI; missing components are reported
as a failed row with an error string, never thrown as a fatal exporter
exception.

Each wing row reports:

- expected/reference/actual bounds;
- center and size deviations;
- qualifying pixel count;
- reference-to-actual pixel-count ratio;
- aligned binary-mask intersection-over-union;
- thresholds and pass state.

Acceptance requires:

- center deviation within `±2 px`;
- width and height deviation within `±4 px`;
- actual/reference qualifying-pixel ratio in `[0.70, 1.30]`;
- aligned mask IoU at least `0.55`.

The pixel-ratio and IoU gates prevent a thin line from passing merely because
its rotated bounding box is large.

### Central decorations

The ten existing central component rows remain. Their narrow ROIs are measured
against the dark-backed panel. Acceptance remains center deviation within
`±1 px` and size deviation within `±2 px`.

### Frame continuity

Top, bottom, left, right, and right-top chamfer remain separate records.

- Straight edges require coverage `>= 0.90` and largest gap `<= 6 px`.
- The chamfer requires at least `80` qualifying pixels in its Figure 9 ROI.
- Segment count and transforms are reported alongside the continuity result.
- Manual overlay review must show a continuous low-brightness outline, a
  distinguishable right-top chamfer, no vertical-strip blocks, and no obvious
  overlap hotspot.

### Report behavior

The exporter always writes the complete JSON, Markdown, crops, overlays, and
heatmaps when the inputs are valid, even when visual acceptance fails.

A missing wing component or failed edge is represented by:

- `passed: false`;
- a stable failure reason;
- nullable/empty actual measurement fields as appropriate.

Input corruption, missing screenshots, invalid manifests, and unsafe output
paths remain fatal and transactional. A visual mismatch is not an input error.

The report retains the frozen Create/Join action-bar Rect and content rows and
must prove they are unchanged.

## Calibration workflow

1. Add tests for the new hierarchy, repeated-instance provenance, four wing
   measurements, non-fatal visual failures, and frame material-only rule.
2. Establish RED against the current thin-wing/nine-frame implementation.
3. Implement the dark backing, four repeated wing composites, and measured
   frame stitching.
4. Run focused Layout, View, Capture, and Controller fixtures.
5. Build a 1920x1080 Windows Player and capture the five existing real states.
6. Publish a complete visual report and inspect the full Home image, component
   crops, frame crops, overlays, and heatmaps.
7. Apply one measurement-driven correction per cycle.
8. Repeat for at most three new materially different cycles. Stop immediately
   on acceptance, or stop and report after cycle three. Never start a fourth
   cycle without a new architectural discussion and explicit approval.

Artifact roots for this pass use
`Artifacts/LAN-LOBBY/CreateDecorationOverlapRecovery/` so previous failed
evidence remains untouched.

## Acceptance

The revised slice is accepted only when all of the following are true:

1. Both accepted action bars retain zero Rect movement and all four accepted
   icon/label rows pass.
2. The four semantic wings independently satisfy bounds, pixel-ratio, and IoU
   gates and visually resemble the broad tapered Figure 9 silhouettes.
3. All ten central rows satisfy their tolerances.
4. All five frame records pass and the manual overlay shows a continuous,
   low-brightness `doc_frame_line` outline with visible right-top chamfer.
5. The runtime and capture evidence contain only approved non-`$0`/non-`#0`
   Sprite sources, with zero active `room_select_create_logo`.
6. No decoration Graphic intercepts clicks and `CreateAction` remains the last
   child.
7. Focused Unity tests, Player build/capture, visual-evidence smokes, and final
   repository-relevant verification pass with no new failure.
8. LAN behavior and the accepted Join/Create action layout remain unchanged.

Passing structural tests or producing screenshots is not sufficient if any
visual gate fails.
