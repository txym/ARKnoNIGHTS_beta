# Room Select Create Open-Frame Correction Design

## Status

Approved in conversation on 2026-07-27.

This design supersedes
`2026-07-27-room-select-create-frame-completion-design.md`.

## Correction

Figure 9 does not place a separate cyan bottom line around the Create action.
The visible `创建同盟` bar is itself the lower boundary of the Create region.
The cyan outline surrounds only the dark upper decoration and meets the
visible top edge of that bar. It must not continue beside, below, or behind
the bar's visible pixels.

The previous eleven-segment frame treated the complete Create group as a
four-sided rectangle. Its three `Bottom_*` Sprites and the lower portions of
`LeftLower` and `RightLower` therefore produced a frame that was too large.
The old bottom contrast failure was measuring a line that Figure 9 does not
require.

## Goal

Replace the closed eleven-segment outline with a Figure-9-measured open frame:

- three repeated `doc_frame_line` Sprites across the top;
- two repeated `doc_frame_line` Sprites down each side;
- no independent cyan bottom edge;
- no cyan line beside the visible Create action bar;
- a textureless dark backing confined to the upper framed region;
- the accepted Create action bar supplying the lower boundary.

The Create/Join action bars, four wings, central decoration, and LAN behavior
remain frozen.

## Figure 9 measurements

Measurements use Figure 9 at its native `2102x1149` pixels and the accepted
1920x1080 Player capture.

In Figure 9:

- the upper frame centerline is near native `y=240`;
- the visible Create-bar top is near native `y=490`;
- the vertical centerline interval is therefore about `250` native pixels,
  or about `235` pixels after 1080-height normalization;
- the left and right upper-frame centerlines align with the visible left and
  right edges of the Create bar;
- there is no independent cyan line below the bar.

The accepted Player Create action Rect remains
`screen-top-left (1154,453,717,99)`. Its measured visible teal bounds begin
about `25 px` from the crop's left edge, end about `26 px` before the crop's
right edge, and begin about `7 px` below the action Rect top. Therefore the
open-frame target centerlines are:

```text
left x:               1179 screen px
right x:              1845 screen px
horizontal interval:   666 screen px
top y:                  225 screen px
bar-visible top y:      460 screen px
vertical interval:      235 screen px
```

The Create parent top-left at 1920x1080 is `(1032,248)`, so these become
Create-local top-left coordinates:

```text
left x:       147
right x:      813
top y:        -23
bar top y:    212
```

These measurements replace the previous `705 px` side-center interval and
the `374 px` closed-frame height.

## Sprite construction

### Source and material rules

1. Every visible cyan outline pixel comes from
   `Assets/Resources/UI/Lobby/Home/doc_frame_line.png`.
2. That Sprite remains byte-identical to the approved non-`$0`, non-`#0`
   autochess source in the Lobby asset map.
3. Repetition, rotation, and controlled overlap are allowed.
4. No procedural line, uGUI `Outline`, mesh, shader border, preset frame, or
   other Sprite may contribute to the outline.
5. All frame Images have `raycastTarget == false`.

### Seven-segment open frame

The corrected frame contains exactly seven active `doc_frame_line` Images:

```text
Top_0
Top_1
Top_2
LeftUpper
LeftLower
RightUpper
RightLower
```

There are no `Bottom_*`, `TopRightChamfer`, or other outline nodes. The
beveled ends already present in `doc_frame_line` form the two upper joints;
separate rotated corner pieces would add overlap hotspots not present in
Figure 9.

The source Sprite is `309x47`; its measured non-transparent long-axis span is
`304 px`, beginning at source `x=3`. Long-axis calculations use the
`304/309` visible-span ratio.

For the `666 px` top centerline interval, three equal visible spans overlap by
`17 px`:

```text
visible span per top segment = (666 + 2*17) / 3 = 233.333 px
RectTransform long width     = 233.333 * 309/304 = 237.171 px
top center pitch             = 233.333 - 17 = 216.333 px
top centers, Create-local    = (263.667,-23), (480,-23), (696.333,-23)
```

For each `235 px` vertical interval, two equal visible spans overlap by
`17 px`:

```text
visible span per side segment = (235 + 17) / 2 = 126 px
RectTransform long width      = 126 * 309/304 = 128.053 px
side center pitch             = 126 - 17 = 109 px
left centers, Create-local    = (147,40), (147,149)
right centers, Create-local   = (813,40), (813,149)
```

Top segments use rotation `0°`. Left segments use `90°`; right segments use
`270°`. All use a shared `12 px` cross-axis thickness and
`preserveAspect == false`.

The first measured Player cycle preserves the current common tint
`RGBA(0.42,0.82,0.76,0.72)` so geometry is evaluated independently. Because
the source alpha peaks at only about `102/255`, one subsequent brightness-only
cycle may use `RGBA(0.55,0.95,0.88,1.0)` if the corrected outline remains
visually faint or has Rec.709 contrast below `18`. No intermediate
guess-and-check tint variants are authorized.

## Interior backing

`InteriorBacking` remains a plain black uGUI Image with:

- `sprite == null`;
- no custom material;
- no shader, border, or outline;
- `raycastTarget == false`;
- color `RGBA(0,0,0,0.78)`;
- sibling order before `CreateFrame`.

Its corrected Create-local top-left Rect is:

```text
x=147, y=-12, width=666, height=224
```

The backing begins inside the top line and ends at the measured visible top of
the Create action bar. It does not extend beneath any visible bar pixel. Its
left and right edges align with the corrected frame centerlines instead of
the action Rect's transparent bounds.

## Frozen hierarchy and behavior

The relevant Create children remain back-to-front:

1. `InteriorBacking`
2. `CreateFrame`
3. frozen `Wings`
4. frozen central decoration
5. frozen `CreateAction`

The following values remain unchanged:

- Create action Rect `(1154,453,717,99)` in screen-top-left pixels;
- Join action Rect `(1154,876,717,99)`;
- both action backgrounds, icons, labels, hit targets, and click wiring;
- four `img_pointer` transforms and tint;
- all ten central-decoration transforms and tints;
- all room-number, discovery, host/join, room-state, latency, and disconnect
  behavior.

## Automated evidence

### Semantic frame regions

The full Create comparison crop remains
`screen-top-left (1154,224,717,374)` so the report still shows the upper
decoration, Create bar, and area below it. Cyan continuity is no longer
measured as a four-sided rectangle.

The report contains these acceptance regions:

- `top`: the corrected `666 px` horizontal span;
- `left`: only from the top joint to the Create bar's visible top;
- `right`: only from the top joint to the Create bar's visible top;
- `top-left-joint`;
- `top-right-joint`.

Top/left/right each require:

- qualifying cyan coverage `>= 0.90`;
- largest gap `<= 6 px`;
- Rec.709 luma contrast `>= 18`.

Each upper joint requires:

- at least `80` qualifying cyan pixels;
- Rec.709 luma contrast `>= 18`;
- no visible thick hotspot in the native 1920x1080 screenshot.

There is no `bottom` cyan edge row.

### Lower-boundary proof

The report records that `home-create-action` supplies the bottom boundary and
retains:

- zero position deviation;
- zero size deviation;
- passing icon and label visual rows.

View and Capture tests prove:

- exactly seven active `doc_frame_line` Images per Home state;
- no frame node named `Bottom_*` or `TopRightChamfer`;
- no frame segment's calculated `304/309` visible long-axis span extends
  below Create-local top-left `y=212`;
- `InteriorBacking` ends at `y=212`;
- no backing or frame Graphic receives raycasts;
- `CreateAction` remains the final Create child.

The two Home states therefore contain exactly fourteen `doc_frame_line`
occurrences, eight frozen `img_pointer` occurrences, and zero active
`room_select_create_logo` occurrences.

### Negative evidence

The visual smoke fixture includes:

1. a deliberate `8 px` gap on the corrected top span;
2. a continuous but low-contrast side;
3. no synthetic bottom cyan edge.

It proves the first two failures are published without aborting and that the
report exposes only the three semantic straight edges plus two upper joints.

## Calibration

Use at most two new real Player cycles:

1. corrected geometry with the current tint;
2. only if needed, the single authorized brightness tint.

Each cycle must:

- run Layout, View, Capture, and Controller fixtures sequentially;
- build the Windows Player successfully;
- capture the existing five 1920x1080 states;
- export the complete visual-difference report;
- verify Create/Join action rows before frame rows;
- inspect the full Home screenshot and frame actual/reference/overlay/heatmap.

No third cycle is permitted without another architectural discussion.

## Acceptance

The corrected Create open frame passes only when:

1. the frame centerline interval is `666 px` horizontally and `235 px`
   vertically at 1920x1080;
2. exactly seven approved `doc_frame_line` Images form top/left/right;
3. no independent cyan bottom line or frame segment beside/below the visible
   Create bar exists;
4. the dark backing covers only the upper region and stops at the visible bar
   top;
5. top/left/right continuity and contrast pass;
6. both upper joints pass and have no hotspot;
7. the Create action bar visibly and structurally supplies the lower boundary;
8. frozen action, wing, central-decoration, asset-provenance, and LAN
   assertions remain green;
9. focused tests, build, Player capture, evidence smokes, and manual native
   screenshot review pass.

Passing the former closed-frame bottom measurement is neither required nor
desirable.
