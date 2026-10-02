# Procedural park water implementation — WIP, not ready to merge

2026-10-02, requested by strawberry at Discord message1555509148982968411.
Based on `tinyclaw/particle-schedule`8363918 plus the research trace0051c42.

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
(`0x220C78`), **not** fixed simulation D. The view retains sub-10ms time and discards elapsed
paused time. Rendering/projection/clip-Z are translated through Godot; full native VU/GS
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
- Current normal shipping Viewer: **35 PASS on JUNGLE/1** after strengthening. The final all-eight
  rerun is pending. Natural-frame GPU height/V changes, actual water retirement, real input and
  exact one-map/numbered/semantic receipts are enforced. A counters-moving-but-frozen-mesh mutant
  fails the new shipping-motion check.
- **11 compiled mutations rejected**, byte-for-byte restoration and rebuild verified: missing
  shipping load, missing shipping step, flat uploaded Y, frozen uploaded V, opaque shader, wrong
  placement, retained old water node, skipped zero-delta wrap, frozen CPU/GPU despite advancing
  counters, per-step surface rebuild, and reversed cubic linear coefficient (five numerical fails).
  The independently constructed component **still passed with the shipping load omitted**;
  the actual Viewer failed. They are not interchangeable proofs.
- The **actual default audit helper was omitted and compiled**: full JUNGLE/1 still exited0/PASS,
  but the classifier rejected it as `missing_coverage`, procedural_water=0. Restored/rebuilt.
- Tool classifier tests: **152 PASS**, including independently pinned76/35/28/57 floors,
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

## Must finish before READY handoff

1. Sync the current guest-atlas staging change, then run the clean final all-eight core and
   shipping-water sweep with current35 assertions; both sizes for component/reset and shipping.
2. Existing pointer/mechanic/management/advisor/coaster/vehicles, research and particle regressions.
3. Bounded same-build water-on/off performance check; no other agents' services/renderers touched.
4. Resolve the A_SEA_04 review with actual Viewer-transformed bounds versus the bounded native
   grid, **not** by widening or raising the draw to fit model-frame coordinates. The native grid
   is not a promise of an overlay across the whole ocean. No invented wave/height replacement.
5. Final docs/push/handoff. No emulator pixel/phase parity claim or asset commits.
