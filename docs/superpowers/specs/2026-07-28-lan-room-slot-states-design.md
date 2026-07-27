# LAN Room Slot States Design

**Status:** Approved on 2026-07-28

## Goal

Recreate the Figure 11–13 room-page slot states and primary room actions:

- empty player slot;
- occupied but not-ready player frame;
- ready player frame;
- the top-left Leave action;
- the role-dependent bottom-right Ready / Cancel Ready / Protocol Start action.

Preserve the existing LAN room-number discovery, join, snapshot broadcast,
latency display, ready synchronization, start broadcast, and transition into
the existing game loop.

## Authoritative references and exclusions

The authoritative references are:

- `G:\ARKnoNIGHTS_beta\docs\references\ui\battle_hud\图11.png`;
- `G:\ARKnoNIGHTS_beta\docs\references\ui\battle_hud\图12.png`;
- `G:\ARKnoNIGHTS_beta\docs\references\ui\battle_hud\图13.png`.

All three references are native `2560x1440` images. Reference coordinates map
to the production `1920x1080` canvas by multiplying X, Y, width, and height by
exactly `0.75`.

The following pixels are outside visual acceptance:

- the scrolling comments across the upper part of all three references;
- the right-side pop-up in Figure 12;
- the right-side pop-up in Figure 13;
- the entire fourth slot in Figures 12 and 13 because those pop-ups occlude
  it;
- all character illustrations;
- player profile-card artwork and profile text.

Figure 11 is the sole unobstructed reference for the fourth empty slot.

The host slot must not create or render a character illustration, avatar,
profile-card artwork, player name, or player ID. It still renders the ready
frame, ready check icon, `已就绪` label, and creator tag required to communicate
the host's role and readiness.

## Selected approach

Build each slot as a stable composition root with independent bitmap layers.
Switch layer visibility and Sprite assignments from an explicit
`Empty` / `Waiting` / `Ready` presentation state. Do not stretch
`player_card_waiting` or `player_card_ready` to the entire slot, and do not
create a precomposited replacement bitmap.

This approach is selected over:

1. continuing the existing one-small-Sprite-per-whole-card stretch, which is
   the direct cause of the enlarged and distorted current presentation;
2. creating derived full-card textures, which would violate the approved
   material policy and make state transitions and provenance opaque.

Overlapping and reusing approved Sprites is expected. Transparent padding,
Sprite pivots, and source texture bounds are treated as implementation
details, not as the visual target.

## Slot composition

Four slot roots use the same measured reference-space layout. Each root owns
the following layers, ordered back to front:

1. card backing and outer structural frame;
2. state-specific top bar;
3. state-specific border or ready overlay;
4. optional invite content for an empty slot;
5. optional portrait area for a non-host occupied slot;
6. ready check icon and ready label;
7. lower player-info or empty-info decoration.

Approved direct source Sprites include:

- `card_bg`;
- `bg_top_normal`;
- `bg_top_ready`;
- `player_card_self_frame`;
- `player_card_ready`;
- `card_empty`;
- `card_deco_self`;
- `bg_plus`;
- the additional existing `card_*`, `img_player_*`, or text-decoration
  Sprites only when visual evidence proves they are part of the reference
  composition.

### Empty

An empty slot uses the neutral `card_bg` structure, the normal gray top bar,
the centered invite/add icon treatment, the `邀请` label, its subordinate
invite explanation, and the empty lower information decoration. It contains
no avatar placeholder, `OPEN SLOT`, or `WAITING` text.

All three empty slots in Figure 11 are blocking references for the repeated
empty-state geometry. The fourth empty slot is not inferred from an occluded
Figure 12 or Figure 13 region.

### Waiting

An occupied not-ready slot uses the normal gray top bar and neutral frame. It
does not show a `WAITING` label or a large waiting-state bitmap. Character and
profile content may remain a non-blocking placeholder for non-host members,
but it must stay behind the frame and must not affect any frame, icon, or
button measurement.

Figure 12 slots two and three are the blocking not-ready references. The
fourth slot is excluded.

### Ready

A ready slot uses:

- `bg_top_ready` for the cyan top bar;
- `player_card_self_frame` for the cyan contour and lower cyan gradient;
- `player_card_ready` at its source aspect for the check icon;
- `已就绪` as the state label.

The host ready frame is checked in Figures 11–13. Ready guest frames are
checked only where unobstructed: slots two and three in Figure 13. The fourth
slot is excluded.

The creator tag is shown only for the host. The host portrait and lower
profile content remain empty as specified above.

## Visual placement authority

Acceptance is based on visible rendered content, not only on Unity layout
rectangles.

For every blocking Sprite or icon, evidence records both:

1. the Unity screen-space `RectTransform` rectangle;
2. the non-transparent visible-pixel bounds and visible-pixel center after
   rendering.

The second measurement is authoritative. A Sprite may need a RectTransform
offset or size compensation when its transparent padding, asymmetric artwork,
or pivot makes its texture center differ from its visual center.

The visual exporter compares:

- visible top, bottom, left, and right edges;
- visible-pixel center;
- visible width and height;
- repeated-slot spacing measured between visible contours;
- icon-to-label visual-center relationship;
- state-overlay contour continuity.

A matching RectTransform with displaced visible artwork fails. Conversely, a
compensated RectTransform passes when the visible artwork matches the
reference. Bitmap scaling must preserve the intended source aspect unless the
source is demonstrably a stretchable bar or backing.

At `1920x1080`, blocking icons and labels allow at most `2 px` visible-center
deviation per axis and `3 px` visible-size deviation. Long frame contours and
composed slot unions allow at most `4 px` edge deviation and require a
visible-contour Jaccard score of at least `0.95` within their named ROI.

## Role and primary-action behavior

### Host

- Creating a room initializes the host member as ready.
- The host has no cancel-ready action.
- The bottom-right action always reads `协议启动`.
- It uses gray `btn_match_grey` and is non-interactable while any present
  guest is not ready.
- It uses cyan `btn_match_normal` and is interactable when every member
  currently in the room is ready.
- Empty slots do not participate in the all-ready calculation.
- A host-only room is all-ready and may start immediately.
- Starting never requires four members.

### Guest

- A newly joined guest is not ready.
- While not ready, the bottom-right action uses gray `btn_match_grey`, reads
  `准备就绪`, remains interactable, and requests `ReadyRequested(true)`.
- While ready, it uses cyan `btn_match_normal`, reads `取消准备`, remains
  interactable, and requests `ReadyRequested(false)`.
- The gray ready action is a visual state, not a disabled control.

### Leave and room dissolution

`btn_topmenu_back` provides the room-only top-left Leave action. Local latency
remains visible to its right without overlapping the button.

- A guest leaving removes only that guest and restores the corresponding slot
  to `Empty`.
- A host leaving stops the room service and dissolves the room.
- Connected guests return to Home through the existing connection-loss /
  shutdown path.
- No remaining member is promoted to host.
- Domain tests and documentation must not continue to describe host
  promotion as supported behavior.

## Bottom-right button presentation

The room page has one role-dependent primary button, not separate visible
Ready, Leave, and Start buttons.

- host unavailable start: `btn_match_grey` + `协议启动`;
- host available start: `btn_match_normal` + `协议启动`;
- guest not ready: `btn_match_grey` + `准备就绪`;
- guest ready: `btn_match_normal` + `取消准备`.

The embedded source icon and the dynamic label are aligned by their visible
pixel centers. The texture rectangle itself is not treated as the icon
center. Figure 12 provides the gray host target and Figure 13 provides the
cyan host target. Guest state uses the same gray/cyan button composition with
the confirmed guest labels.

## Asset policy

- Every non-code-native bitmap must come from
  `G:\素材\11.14\Unpacked_1763129662\Android\ui\autochess` or, only for a
  proven atlas entry, the corresponding atlas under
  `G:\素材\11.14\Combined_1763139377\Android\ui\autochess`.
- Direct Unpacked `$0` or `#0` variants are forbidden.
- No reference screenshot crop, derived composite, generated replacement
  texture, or unrelated external texture may enter `Assets`.
- New direct imports use an explicit whitelist, retain their `.meta` files,
  and are byte-compared with the approved source.
- The capture manifest records Resources path, approved source-relative path,
  SHA-256, capture names, and rendered occurrence count for every bitmap.
- Code-native geometry must be declared separately with `isBitmap=false`, no
  Sprite, no custom material, and `raycastTarget=false`.

## Capture states and visual gates

The existing five-state capture suite retains the two Home captures and maps
the three room captures as follows:

- `room-host` targets Figure 11: ready blank-content host, three complete empty
  slots, and enabled cyan Protocol Start;
- `room-full` targets Figure 12: ready blank-content host, unready guests,
  disabled gray Protocol Start, right pop-up and fourth slot excluded;
- `room-ready` targets Figure 13: ready blank-content host, ready guests,
  enabled cyan Protocol Start, right pop-up and fourth slot excluded, and
  top-left Leave checked.

Named blocking gates cover:

- four-slot visible position, size, and repeated spacing where unobstructed;
- complete empty-state composition from Figure 11;
- normal gray top bars and waiting frame contours from Figure 12;
- cyan ready top bars, ready contours, lower gradients, check icons, and
  `已就绪` labels from Figures 11–13;
- gray/cyan bottom-right button visible bounds, embedded icon center, label
  center, and role-correct text;
- Figure 13 top-left Leave visible bounds;
- absence of host portrait/profile content;
- absence of `OPEN SLOT` and `WAITING`;
- approved material provenance.

Full-screen difference ratios remain informational. Character/profile masks,
upper scrolling-comment masks, and the Figure 12/13 right-side exclusion
cannot make a named frame, icon, button, or absence gate pass.

## Automated behavior verification

Tests must prove:

- a newly created host snapshot is ready;
- a newly joined guest snapshot is not ready;
- host-only start succeeds;
- two-, three-, and four-member rooms start exactly when every present member
  is ready;
- empty slots do not block start;
- guests can toggle ready and receive synchronized snapshots;
- host UI emits Start only when the Protocol Start button is enabled;
- guest UI emits Ready true/false from the role-dependent primary button;
- host UI never exposes a cancel-ready action;
- guest leave preserves the room and creates an empty slot;
- host leave dissolves the room and does not promote a member;
- room start still broadcasts before transport shutdown and enters the
  existing preparation flow;
- all named UI nodes use the expected Sprites, text, state, sibling order, and
  visible geometry;
- no blocking decoration intercepts the Leave or primary-action hit target.

## Calibration and stopping rule

Use at most three new visible Windows Player calibration cycles at
`1920x1080`.

1. Cycle 1 renders the measured multi-layer composition without manual
   aesthetic offsets.
2. Cycle 2 may change only room-slot layer geometry, visible-center
   compensation, room-button geometry, and the room-only evidence detector.
3. Cycle 3 may make one final room-only correction.

Each cycle retains screenshots, manifest, Player log, overlays, heatmaps, and
the structured report. Stop early when all named gates pass and manual review
confirms that the visible artwork—not merely its RectTransform—matches.
After Cycle 3, stop and report remaining measured deviations without claiming
visual completion.

## Acceptance

- Figure 11 empty slots read as complete invite slots rather than stretched
  lines or generic `OPEN SLOT` cards.
- Figure 12 unobstructed occupied guest slots use the correct neutral
  not-ready frame without `WAITING`.
- Figures 11–13 unobstructed ready slots use the correct cyan top bar,
  contour, lower gradient, check icon, and `已就绪`.
- The host slot has no portrait, avatar, name, ID, or profile-card content.
- The top-left Leave and bottom-right role action match their visible
  reference positions, sizes, icon centers, and label centers.
- Host starts with ready state, can start alone, and can start any non-full
  room once all present guests are ready.
- Host leave dissolves the room; no host migration occurs.
- Focused state, socket, View, Capture, and Controller tests pass with zero
  failures and zero skips.
- The Windows build and visible Player capture complete with approved,
  source-audited material evidence.
- Physical Windows-and-Android same-Wi-Fi behavior remains a separate manual
  acceptance item and is not inferred from Editor or single-machine capture.
