# Procedural park water implementation — WIP, not ready to merge

2026-10-02, requested by strawberry at Discord message1555509148982968411.
Based on `tinyclaw/particle-schedule`8363918 plus research trace0051c42; Cow guest atlas2acd035 was merged explicitly before the full gates.

## What exists

- `core/TPW.PS2.Data/ProceduralParkWater.cs`: guarded native profile/placement, explicit native
  libc RNG, immutable 128-segment cubic vector noise, phase/UV state, N4..16 geometry plus the
  exact eight appended vertices, signed16 UV narrowing and native strip diagonals. Caller-owned
  buffers are reused. It has no Godot or asset-writing dependency.
- `ProceduralParkWaterView`: the separate alpha-bearing mesh, preserving the opaque terrain
  below it. It reflects native Z, supplies constant raw normal(0,112,0) via CUSTOM0, and updates
  existing vertex/attribute regions rather than rebuilding the mesh every render. Actual
  uploaded bounds are updated along with geometry. It uses the existing PS2 lighting/material
  translation, not the obsolete travelling-sine uniform.
- `Viewer.ParkWater`: creates the drawable on terrain load, hides/queues the old one on map
  switch, and drives it after the camera update. Its art is explicitly loaded from
  DATA.WAD `/Generic/extra/justwater.ssh`; the ordinary `TextureNear` API searches the active
  world and correctly could not find this global asset. That failed first component run was
  corrected in the production loader as well as the fixture, not hidden by fixture substitution.

## Important translation limits / choices

The native water consumer uses the gated real-time clock `0x2F07B0`, sampled in10ms steps
(`0x220C78`), **not** fixed simulation D. The Viewer samples monotonic `Time.GetTicksUsec`, independently of engine process delta.
The view retains sub-10ms time and discards elapsed paused/hidden time. Load initialization
is not replayed. An earlier implementation mistakenly passed engine delta and was corrected
with an actual zero-engine-delta compiled control. Rendering/projection/clip-Z are translated through Godot; full native VU/GS
rasterization and camera parity are not claimed.

The core receives an explicit RNG seed. The viewer currently uses a reproducible separate
stream beginning at1 with native LCG arithmetic and consumes the second table's384 calls
after generating the water table. The actual game's startup seed and intervening global RNG
consumers have not been replayed. Consequently **exact emulator phase/noise history is not
claimed**. The motion family, cubic generation, native constants and placement are decoded,
not fitted wave parameters.

EE `cvt.w.s` bounded conversion is toward zero; checked against PCSX2's primary interpreter
implementation (`pcsx2/FPU.cpp`, `CVT_W`), alongside the actual conversion instructions in
the owner's executable. Exceptional/saturating EE float behavior is outside the core's bounded
finite-input contract.

The global curve is reused between map loads; each drawable gets fresh phase/UV state. The
current A/B switch `TPW_NATIVE_PARK_WATER=0` omits only this new drawable; it does not replace
terrain art or enable a guessed shader wave.

## Checks and controls completed at the current WIP checkpoint

- Targeted data audit: **76 assertions PASS**, integrated in the default audit and required
  by `audit_matrix.py` with literal numerical and semantic witnesses. All eight native placements,
  explicit LCG/cubic oracles, signed UV narrowing, skirts/topology, and actual ELF guard are covered.
- Initial shipping Viewer sweep: **eight parks at both640x360 and1152x648, 32 each**, with
  ordinary CLI startup and actual Pause/filter/Close Park input. The high-resolution driver's
  shell was interrupted after six completed cases; **both SPACE cases were rerun** to completion
  with identical source/assembly snapshots before combining the eight results. No interrupted
  case is counted. These are pre-strengthening results, not a substitute for the final rerun.
- Read-only review identified weak evidence: clock/CPU/GPU agreement alone did not establish
  actual shipping motion; owner destruction alone did not establish water destruction; reset
  receipts did not require both maps' motion. Those checks were strengthened rather than waived.
- Current registered component: **28 PASS at both sizes**, zero map witnesses. Actual uploaded
  Y/V move, X/Z stay fixed, mesh/buffers and surface-build count stay stable, CUSTOM0 is preserved,
  clock remainder/pausing/long-hitch zero-delta recovery and final node retirement are checked.
- Current reset fixture: **57 PASS at both sizes**, exactly JUNGLE/1 then FANTASY/2. Reset uses a
  **declared direct ordinary LoadMap invocation**, not fabricated player input. Fresh state/phase,
  immediate hide, actual old-node retirement, and both maps' uploaded motion are required.
- Shipping Viewer on production checkpoint d87a016: **35 PASS on all eight parks at both sizes**,
  plus all current component/reset and selected regressions. The newly corrected monotonic-clock
  build has35 PASS on JUNGLE/1; its final all-eight rerun is pending. Natural-frame GPU height/V changes, actual water retirement, real input and
  exact one-map/numbered/semantic receipts are enforced. A counters-moving-but-frozen-mesh mutant
  fails the new shipping-motion check.
- **12 compiled mutations rejected**, byte-for-byte restoration and rebuild verified: missing
  shipping load, missing shipping step, flat uploaded Y, frozen uploaded V, opaque shader, wrong
  placement, retained old water node, skipped zero-delta wrap, frozen CPU/GPU despite advancing
  counters, per-step surface rebuild, reversed cubic linear coefficient (five numerical fails), and feeding
  engine delta rather than monotonic elapsed time. The latter fails the actual clock-control scene.
  The independently constructed component **still passed with the shipping load omitted**;
  the actual Viewer failed. They are not interchangeable proofs.
- The **actual default audit helper was omitted and compiled**: full JUNGLE/1 still exited0/PASS,
  but the classifier rejected it as `missing_coverage`, procedural_water=0. Restored/rebuilt.
- Tool classifier tests: **162 PASS**, including independently pinned76/35/28/57/18/14 floors,
  one-below-floor controls, strict maps, ordinals, actual-motion/alpha/retirement semantics and
  two required receipts for each reset map. Counts alone are not a PASS.

The alpha control is actual raster output, not a shader-string assertion: visible blue water
changes when the artificial underlay changes red to green. This remains a **declared component**,
not player-path or emulator comparison. The shipping screenshot was separately reviewed by cow
and the lead: visible dark base through brighter ripple bands and a clean quay edge. One shot
cannot establish motion, all-tier coverage, or exact emulator rendering.

A concrete review bug was corrected: after a long hitch leaves UVacc>8192, a zero-delta draw
still performs the native one-step wrap. The view now advances state even for delta0 and uploads
if that changes UV; both the core's single-wrap oracle and actual component control cover it.

Evidence lives outside Git:
- `/tmp/tpw-water-component-registered`, `/tmp/tpw-water-reset-registered`,
  `/tmp/tpw-water-viewer-registered-clean`, `/tmp/tpw-water-viewer-1152-combined.json` (initial).
- `/tmp/tpw-water-strengthened-component`, `/tmp/tpw-water-strengthened-reset`,
  `/tmp/tpw-water-strengthened-viewer-j1` (current28/57/35).
- `/tmp/tpw-water-mutations`, `/tmp/tpw-water-strengthened-mutations`,
  `/tmp/tpw-water-core-controls-rerun` (actual compiled controls).
- `/tmp/tpw-water-strengthened-python.log`.

## Full gates completed before the final clock correction

On clean/pushed d87a016, the core8 matrix retained exactly4 PASS and4 known retail failures,
with76 procedural_water assertions in every case. Component28 and reset57 passed both sizes;
normal shipping water35 passed all8 parks at both sizes; pointer/mechanic/management/advisor/
coaster/vehicles passed12 selected JUNGLE/1 + HALLOW/2 cases; research24, particle-child62 and
particle-schedule18 passed both sizes. No exit error/leak warnings occurred in those gates.
These are recorded as pre-correction evidence, not silently relabeled as the final new build.

The first performance queue stopped on an **ObjectDB exit warning with water OFF** in the
existing production benchmark's immediate Quit path. The warning was not waived and those
results are not a clean A/B. A separate normal-Viewer performance fixture uses the smokes'
ResetNativeBus/StopMusic/RideSounds.Clear, QueueFree, two frames and0.1s real retirement.
This does not claim that the unrelated production benchmark teardown was repaired.

## Corrected clock and bounded performance controls

`park-water-clock` is a **declared public-engine control**, not ordinary player proof: after
normal startup it temporarily sets Engine.TimeScale=0, observes actual zero process delta, then
uses real Pause input. **18 PASS at both sizes**. Actual uploaded Y/V and native phase continue
on wall time; real Pause still stops them. At640 the observation was705 real ms /660 native ms,
and at1152 it was638 /630 (frame-boundary sampling plus10ms granularity). The compiled engine-
delta version fails; sources restored byte-for-byte and rebuilt. This catches the actual earlier
clock bug, not merely a classifier-string mutation.

`tools/park_water_perf.py` records balanced off/on/on/off runs of the ordinary Viewer, the same
A_SEA_02 aim, and the same build. Natural frame intervals and the nine-second sample window use
monotonic ticks, not scaled/capped simulation delta or SceneTreeTimer duration. Sample arrays are
fixed/bounded; the camera transform/projection must remain fixed and have the same fingerprint
across all four runs. Exactly one successful aim, metric row, complete14-numbered receipt set,
one JUNGLE/1 witness, and clean teardown are required. Malformed extra metric rows, wrong aims,
missing retirement, short/unbounded windows, and old exit warnings are explicitly rejected.

Current local experiment (new clock; clean final replay still pending):

| water | median real frame ms | p95 ms | draw calls | samples |
|---|---:|---:|---:|---:|
| off |71.650|92.503|164|124|
| on |80.138|105.773|165|111|
| on |80.952|123.859|165|106|
| off |70.817|95.592|164|122|

All four pass14 with no error/leak warnings,9.0006..9.0881 seconds collected, and identical
camera fingerprint. This machine reports **Mesa llvmpipe**, not the peer's4080; an unrelated
headless server stayed untouched. The water adds one draw call and about9ms here. The base
scene is already about71ms without water; neither these absolute fps nor this overhead is a
claim about the player's GPU. Allocation figures include normal Viewer and instrumentation,
not an isolated pure-water allocation cost.

## Resolved footprint review and remaining capture limitation

Actual JUNGLE/1 Viewer bounds for A_SEA_04 are X[-43.221455,-3.968615], Z[16.999979,52.234583].
The moving main grid is X[17,42], Godot Z[-6.4,15], so it never covers that mesh. The exact eight
native appended vertices form lower, fixed-height skirts at Y=-.9, partly overlapping its edge.
No whole-ocean sheet or invented height adjustment was added. Cow independently reconciled the
model-frame spans by uniform /10 and the known translation/Z reflection; the lead accepted the
visible two-tier result. Logs contain `[water-footprint]` receipts.

**Offline film/still clock mapping remains a known gap.** Existing film capture advances park
simulation by fixed frame time, whereas the live water now correctly uses its own real clock.
There is no separately tested capture-time override in this patch. Do not treat those clips as
water timing or deterministic/repeatable pixel proof. The live clock is not bent to fit an offline
tool; capture-clock integration is a separate future slice. No emulator phase/RNG/pixel parity
claim is made. The rendered matrices enforce behavioral receipts, not golden-frame equality.

Evidence additions outside Git:
- `/tmp/tpw-water-final-{core,component,reset,viewer,highres,regression,research-persistence,particle-child,particle-schedule}`.
- `/tmp/tpw-water-clock-registered`, `/tmp/tpw-water-clock-mutant`, `/tmp/tpw-water-clock-viewer-j1`.
- `/tmp/tpw-water-wall-perf` (monotonic14-case A/B), `/tmp/tpw-water-clock-python.log`.
- `/tmp/tpw-water-final-perf` (baseline immediate-Quit warning, retained as failed evidence).

## Must finish before READY handoff

1. Run the clean final core8 and shipping-water8/both-size sweep on the corrected monotonic
   build, including component28, reset57 and clock18 both sizes.
2. Rerun the selected existing Viewer, research and particle regressions and the strict same-
   build14-case balanced performance experiment.
3. Append actual final results DOC-only, commit/push and coordinate handoff. Do not self-merge
   to main or quietly incorporate unrelated gold-ticket/coaster work. No assets committed.
