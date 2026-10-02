# Procedural park water implementation — READY WITHDRAWN, correcting peer gate

2026-10-02, requested by strawberry at Discord message1555509148982968411.
Based on `tinyclaw/particle-schedule`8363918 plus research trace0051c42; Cow guest atlas2acd035 was merged explicitly before the full gates.


## 2026-10-02 peer gate correction — NOT READY

Tinyclaw's gate1555567532986077305 found that native park index0 is terrain_1 and1 is
terrain_2. I had treated the comparison against1 as external terrain_1, reversing HALLOW,
FANTASY and SPACE. Both old literal-test tables repeated that misinterpretation; their green
runs did not validate the mapping. **The earlier READY declaration is withdrawn.**

Confirmed against actual caller1515d8 and MIPS149958: corrected external minX sequence is
17,17,35,31,27,25,35,23. New core tests do NOT use that sequence as their oracle. They decode
A_SEA_02 vertices through the full authored parent chain and require suppliedX−seaLeft=9.125.
The old code fails six of those independent joins; the corrected code passes all eight.
Actual rendered joins are being added to the shipping smoke, not another copied table.

The laptop enter/exit pair1c55c8/1c5680 calls220c68(0/1); the view now discards native elapsed
water time while its laptop panel is open. The seeded Generate(ref uint) overload used by
Viewer now has an independent full128-point XYZ fingerprint plus seed/point checks, rather
than relying only on the Func overload. Targeted core currently passes80, up from76.

The initial reviewer note claiming plain view depth for LOD was retracted in1555570754614198295.
Native21ea20 composes projection into2f0380;21f3c0/21f5b0 yields pre-divide clip Z.21ef60 uses
sin(fov/2) scaling;2f0680 rescales onlyXY. Renderer boot21b090..a8 supplies near=.75, far=500,
fov=60, now guarded/read from ELF. The view uses that explicit native scalar projection instead
of Godot's unrelated near/far/reverse-Z. Native runtime mode-specific lens switches remain
untraced and are an explicit limitation; this is decoded boot-lens LOD, not full native camera
parity.22cc44 clamps depth with max.s; only22cc98 truncates after the half-depth/clamp expression.
SelectDimension(10.9) is11. The prior truncate-before-half comment/test was wrong.

Tests are being strengthened to check actual uploaded skirt extents in the CustomAabb, not
merely >20 dimensions which the main grid alone satisfies. Current new floors: core80,
shipping40, reset63, component31; clock18 and performance14 unchanged. Python162 passes and
both projects build; new rendered/mutation/full gate results are still pending. No new READY
claim or merge is authorized. Both earlier software/hardware performance tables below apply
only to the older LOD and do not license the corrected detail level; Cow explicitly requests
a fresh hardware A/B (1555570841952194651).

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

## Implementation checks and controls (including earlier checkpoints)

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
  build was subsequently rerun on all eight parks at both sizes; see the final clean gate below. Natural-frame GPU height/V changes, actual water retirement, real input and
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

Earlier local experiment on the new clock (final clean replay is recorded below):

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

## Earlier clean gate — superseded by the peer mapping/LOD correction

**Superseded evidence; not current readiness.** Every earlier final run used clean/pushed production
revision `c24c5316c35820fa92628928a5bcde132ab7b214`; the final READY update changes this Markdown
only. Source snapshot SHA256 was
`908e11a089ff4b02a81363580d738492567950a8caa080830601b0e28ff5868c`.
All rendered groups had identical source and assembly snapshots, also checked against the
actual worktree/binaries at completion. Stored log hashes were rechecked. The final queue
`/tmp/tpw-water-ready-gates.exit` is0; the core's documented exit2 is preserved rather than
misreported as all-pass. No compiled mutations remain in the shipping source.

| gate | final outcome |
|---|---|
| Python tool suite |162 PASS |
| Core8 |4 PASS /4 exact known retail failures;76 water assertions per park; no new failure |
| Normal shipping water |16/16 PASS: all8 parks at640x360 and1152x648;35 each |
| Declared clock control |18 PASS at both sizes; zero engine delta, real wall motion, real Pause |
| Dynamic-mesh/alpha component |28 PASS at both sizes; zero maps, actual raster underlay control |
| Declared ordinary-loader reset |57 PASS at both sizes; exact JUNGLE/1 → FANTASY/2 |
| Existing Viewer regressions |12/12 PASS: pointer, mechanic, management, advisor, coaster, vehicles on JUNGLE/1 and HALLOW/2 |
| Existing research persistence |24 PASS at both sizes |
| Existing particle child |62 PASS at both sizes |
| Existing particle schedule |18 PASS at both sizes |
| Balanced performance experiment |4/4 PASS;14 receipts each; clean retirement, same verified camera/build |

Thus40 rendered functional cases plus4 performance runs completed, separately scoped from the
core8 audit. This is not a claim of running every preexisting viewer-matrix scene on every park.
No error/leak warnings occurred in these final rendered runs. The earlier production benchmark
warning remains retained as separate failed evidence; neither its regex nor its teardown was
silently waived or patched by this work.

### Independent hardware gate — Cow's4080

Cow tools independently reported the following on branch3397778 in Discord message
1555563905839792261 (2026-10-02). This is the hardware performance/integration gate;
it is attributed peer evidence, **not a benchmark run on this agent's software-rendered host**.

| guests | water | median frame ms | draw calls | managed churn KB/frame |
|---:|---|---:|---:|---:|
|0|off|1.26|124|17|
|0|on|1.25|126|17|
|150|off|5.56|607|292|
|150|on|5.56|609|296|

On that4080 setup, the water adds **two draw calls with no measurable median frame-time
increase at the reported precision**. The .01ms empty-park reversal is not credited as a
speedup. Water-off reproduces the guest-performance baseline; the existing gait/atlas work
remains intact in this integration. This is not a guarantee for every GPU, camera, resolution
or scene. Cow's pass covers performance, build/integration and repository hygiene, **not**
the native decode review; Tinyclaw is separately gating that and the broader merged suite.

The hardware result supersedes any reading of the local68–80ms numbers as a player-GPU
regression. Those numbers remain below as valid software-renderer diagnostics, with their
original camera/host scope. Draw-call differences between the two setups are retained as
observations rather than forced into a single universal count. No zero-allocation claim is
inferred from value-type Vector3 temporaries; the measured whole-frame churn is recorded above.

### Retained local software-renderer diagnostic

Final same-build A/B on the **local Mesa llvmpipe** host:

| water | median real frame ms | p95 ms | draw calls | samples |
|---|---:|---:|---:|---:|
| off |68.320|95.170|164|126|
| on |76.701|102.458|165|112|
| on |79.676|105.977|165|109|
| off |69.719|93.828|164|126|

The bounded windows were9.0076..9.0606 seconds, all four camera fingerprints matched, and
all four error/leak classifications passed. The water adds one draw call and roughly9ms here;
this is software-rendering overhead on this host, **not** a forecast for the player's GPU.
Actual samples/counts are in the manifest; the table is not a synthetic performance model.

Final evidence:
- `/tmp/tpw-water-ready-{core,clock,component,reset,viewer,highres,regression,research-persistence,particle-child,particle-schedule,perf}/manifest.json`.
- `/tmp/tpw-water-ready-python.log`, `/tmp/tpw-water-ready-gates-run.log`.
- `/tmp/tpw-water-ready-verification.json`: rechecked source/assembly/log/count provenance.

The branch includes the already-tested particle-schedule8363918 base and Cow's atlas2acd035,
**not** the later gold-ticket/coaster-stats branch. No merge to main is performed by this handoff.
Offline film/still clock mapping, actual native global RNG history and exact VU/GS pixel parity
remain the explicit limitations above. No new implementation slice is started after this handoff.
