# Room Select Create Frame Completion Design

## Status

Approved in conversation on 2026-07-27.

This design supersedes
`2026-07-27-room-select-create-decoration-overlap-recovery-design.md` before
that design reached implementation.

## Goal

Complete the Figure 9 Create UI by making the upper-region outline continuous,
clearly visible, and recognizably constructed from the approved
`doc_frame_line` Sprite.

The four `img_pointer` wings and the existing central decoration are frozen in
their current state and are no longer visual acceptance gates. The already
accepted Create/Join action bars and LAN room behavior remain immutable.

## Confirmed scope

### In scope

- A dark, textureless interior backing behind the Create upper decoration.
- The stitched Create outline built only from repeated `doc_frame_line`
  Sprite Images.
- Frame continuity, brightness, right-top chamfer visibility, and overlap
  quality.
- Frame-only automated difference evidence and at most three new real Player
  calibration cycles.
- Provenance, hierarchy, raycast, action-bar, and LAN regression checks.

### Frozen

- The current four `img_pointer` instances, including their transforms, tint,
  and source-preserving aspect ratio.
- The current central Create Sprite transforms and tints.
- Create and Join action Rects, icons, labels, backgrounds, content placement,
  sibling order, and click behavior.
- All room-number, LAN discovery, host/join, room-state, and disconnect logic.

### Out of scope

- Further wing matching or wing connected-component measurement.
- Further central-decoration calibration.
- Join upper-decoration work.
- Avatar/ID work, Figure 10 work, and unrelated UI cleanup.
- Unity Editor, Package, render-pipeline, or Input System changes.

## Approaches considered

### Selected: rebuild from visible-alpha spans

Keep the approved `doc_frame_line`-only construction, but allow 9 to 16
instances. Position and overlap each segment according to the Sprite's
measured visible-alpha span rather than assuming that its RectTransform width
equals visible line coverage. Add a dark backing and raise the common frame
tint enough to remain visible in the full 1920x1080 Player screenshot.

This addresses both measured failures: the top edge's `0.4231884058` coverage
and the frame's weak visual contrast.

### Rejected: brighten the existing nine transforms

Increasing alpha alone cannot close the measured `335 px` top-edge gap. It
would make the fragments brighter while preserving the broken outline.

### Rejected: code-drawn or preset frame

A procedural line, `Outline`, mesh, border shader, or preset rectangular frame
would violate the confirmed requirement that the visible outline be assembled
from real `doc_frame_line` Sprites.

## Material and provenance rules

1. Every visible outline pixel comes from
   `Assets/Resources/UI/Lobby/Home/doc_frame_line.png`.
2. That Sprite remains byte-identical to the approved non-`$0`, non-`#0`
   autochess source recorded in the Lobby asset map.
3. Repetition, rotation, and overlap of `doc_frame_line` are allowed.
4. The visible outline uses between 9 and 16 `doc_frame_line` Image instances.
5. No other Sprite, code-native line, procedural mesh, `Outline`, border
   material, or shader contributes to the outline.
6. `room_select_create_logo` retains zero active runtime occurrences.
7. The dark backing is a plain uGUI `Image` with `sprite == null`; it is not a
   texture and does not draw a border.

## Runtime hierarchy

The relevant Create children are ordered back-to-front:

1. `InteriorBacking`
2. `CreateFrame`
3. frozen `Wings`
4. frozen central decorations
5. `CreateAction`

`CreateAction` remains the final Create child. `InteriorBacking`, every frame
segment, every wing, and every central decoration has
`raycastTarget == false`.

### Interior backing

`InteriorBacking` is a uniform semi-transparent black Image covering only the
framed Create upper region. It has:

- no Sprite;
- no material, shader effect, border, or outline;
- initial color `RGBA(0, 0, 0, 0.78)`;
- no overlap with the accepted Create action bar;
- no raycast participation.

The backing suppresses the map contours inside the panel and creates a stable
contrast field for the frame. Its alpha may be calibrated within
`0.68..0.84`, but its Rect and final alpha must be frozen in the View test.

### Stitched `doc_frame_line` outline

The frame has four straight semantic edges plus a right-top chamfer:

- `Top`
- `Bottom`
- `Left`
- `Right`
- `TopRightChamfer`

Each straight edge contains enough `doc_frame_line` segments to cover its
Figure 9 measurement span. A segment may be translated, rotated, resized along
its long axis, and assigned the shared cross-axis thickness of `8..14` design
pixels. Adjacent visible-alpha spans overlap by `10..24` design pixels.

The source Sprite is `309x47`, with a measured non-transparent bounding box of
`304x47` beginning at `(3,0)`. Placement calculations use that visible span.
The implementation must not infer continuity from GameObject count or
RectTransform width alone.

The initial common tint is `RGBA(0.42, 0.82, 0.76, 0.62)`. Calibration may
change only the common tint alpha within `0.50..0.75`; all segments retain the
same tint. The final segment count, names, transforms, overlap spans, and tint
are frozen in the View test and exported in the capture manifest.

## Automated evidence

### Frame geometry

The existing Create-frame crop remains:

```text
actual screen-top-left: x=1154, y=224, width=717, height=374
Figure 9 measurement-space reference: x=1257, y=239, width=763, height=397
```

The four straight-edge ROIs and right-top chamfer ROI remain separate. Each
straight edge passes only when:

- qualifying cyan coverage is at least `0.90`;
- the largest leading, internal, or trailing gap is at most `6 px`.

The right-top chamfer requires at least `80` qualifying cyan pixels and must
remain distinguishable in the full Home screenshot.

### Frame visibility

Continuity alone is insufficient. Each straight edge also reports a luma
contrast measurement:

1. Compute Rec.709 luma
   `Y = 0.2126R + 0.7152G + 0.0722B` for qualifying frame pixels.
2. Compare their median luma with the median luma of a parallel interior
   background band 8 to 16 pixels away from that edge.
3. Require `frame median - background median >= 18` on the `0..255` scale.

The chamfer uses the same minimum `18` luma contrast against its adjacent
interior background.

The report records the sample counts, both medians, contrast delta, threshold,
and pass state. A frame with continuous but effectively invisible pixels does
not pass.

### Wings and central decoration

The exporter does not run the previous wing connected-component gate. The
four wings are not included in the overall visual pass.

The existing ten central-decoration measurements remain as informational rows.
Their result does not block Create-frame completion and the exporter must not
throw because of them.

### Complete report on mismatch

Valid inputs always produce:

- JSON and Markdown;
- frame actual/reference crops;
- frame overlay and heatmap;
- per-edge continuity and contrast rows;
- material-usage evidence;
- frozen Create/Join action Rect and content rows.

A continuity, contrast, or chamfer failure is written as `passed: false`; it
does not abort publication. Missing/corrupt inputs, malformed manifests, and
unsafe output paths remain fatal and transactional.

## Testing and calibration

1. Add a smoke fixture with a deliberate `8 px` top gap and a separate
   continuous but low-contrast edge. Verify both failures appear in a complete
   published report.
2. Add View/Capture tests for the backing, frame-only material rule,
   `9..16` segment range, visible-span overlaps, common tint, raycast flags,
   hierarchy, and frozen wings/central/action values.
3. Establish RED against the current frame before modifying runtime geometry.
4. Implement the backing and visible-alpha-based frame layout.
5. Run Layout, View, Capture, and Controller fixtures sequentially.
6. Build the 1920x1080 Windows Player and capture the five existing real
   states.
7. Export and inspect the frame report, full Home screenshot, crops, overlay,
   and heatmap. Verify frozen action rows first.
8. Apply one measurement-driven frame correction per cycle.
9. Use at most three new materially different Player cycles under
   `Artifacts/LAN-LOBBY/CreateFrameCompletion/`. Stop immediately on
   acceptance or report failure after cycle three. Never start a fourth cycle
   without a new architectural discussion and approval.

## Acceptance

The Create UI is considered complete for this scope when:

1. Top, bottom, left, and right each have coverage `>= 0.90`, largest gap
   `<= 6 px`, and luma contrast `>= 18`.
2. The right-top chamfer has at least `80` qualifying pixels, luma contrast
   `>= 18`, and is visibly recognizable in the full Home screenshot.
3. Manual review at native 1920x1080 shows a continuous, obvious,
   low-to-medium brightness outline with no thick hotspot, accidental vertical
   strip, or missing corner.
4. Every outline Image uses `doc_frame_line`, the count is within `9..16`,
   every segment is non-raycast, and no code-native or preset outline exists.
5. The four wings and central decoration retain their current runtime values.
6. Both action bars retain zero Rect movement and all four accepted
   icon/label rows pass.
7. Focused Unity tests, Player build/capture, visual-evidence smokes, and final
   relevant verification pass with no new failure.
8. LAN behavior remains unchanged.

Passing structural tests or merely increasing frame alpha is not sufficient
when continuity, contrast, or manual visibility still fails.
