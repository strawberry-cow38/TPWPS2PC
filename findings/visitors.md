# Visitors on the park grid

2026-09-23. **Ada (guest 101) walks to Orbiter, Bugs TV or Hocus Pocus, queues, is accepted by the ride's real RSSE
script, comes back through that script's unload mailbox, and walks out.** Both visitor audits
cover SPACE, FANTASY and HALLOW `terrain_1.mps` and `terrain_2.mps`. The scene renders the disc's character meshes
and the script-selected ride APS. Characters disappear while on the ride; seat/head attachments
are not implemented. Their skeletal animation is (see [Skinned characters](#skinned-characters-2026-09-23)):
`AnimatedModel` poses a character from any skeletal record of its `.aps`, so a guest walks the
moment its caller hands it one -- at the time of that note, `Viewer.cs` and `VisitorParkView.cs`
still constructed their actors with no record and so still showed the bind pose.

This is a bounded managed simulation of one ride and a finite cohort, not a reconstruction of
the original guest AI. It uses the VM from `rse-runtime` commit `ba028d7`, cherry-picked into
`visitor-ai`. No changes were made to `main`, and no game data is committed.

## Run and audit

Requires the owner's disc and .NET 8. Simulation and pathfinding live entirely in `core/`,
with no Godot reference, subprocesses or native dependencies.

```sh
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
dotnet run --project tools/TPW.PS2.VisitorAudit -- /path/to/disc.bin
(cd game && dotnet build -m:1 -p:UseSharedCompilation=false)
TPW_PS2_DISC=/path/to/disc.bin /path/to/godot-4.6.2-mono --path game res://VisitorDemo.tscn
# Or choose “Visit the park” in the existing viewer.
TPW_PS2_DISC=/path/to/disc.bin /path/to/godot-4.6.2-mono --headless --path game res://tests/VisitorAudit.tscn
tools/TPW.PS2.VisitorAudit/teeth.sh /path/to/disc.bin /path/to/godot-4.6.2-mono
```

The scene has SPACE park 1/2, pause, restart, speed, orbit and zoom. It opens at 5 seconds.
Ada/101, Ben/202, Cy/303 and Dee/404 request arrival at 0/1500/3000/4500ms. Actual spawn
waits for a free entrance cell. Capacity **10** comes from Orbiter's `Upgrades[0].InitCapacity`
in its own `.sam`, ID **3104**. Duration **1** is an explicit demo input. The last guest exits
at 57 seconds for Orbiter, 44.8 seconds for Bugs TV (SAM ID **4102**, capacity **4**). The
interactive demo still offers SPACE 1/2; FANTASY is exercised by both audits.

Reproducible graphical capture (requires a graphical session):

```sh
TPW_PS2_DISC=/path/to/disc.bin /path/to/godot-4.6.2-mono --path game res://VisitorDemo.tscn -- --shot /tmp/visitors.png --at-ms 9000 --terrain 1
```

## Paths: assets are not an existing network

The four inspected grids — SPACE and FANTASY, both terrains — contain **ground material
references**, not placed path or queue references. For example, SPACE terrain 1 names
`sfl_bas2` at slot 2 and `sfl_bas1` at 19; the path palette starts with `jpa_squ1` at 47,
`jpa_str1` at 49, and has `jpa_que1` at 65. FANTASY terrain 1 instead places `jpa_squ1`
at 45 and `jpa_que1` at 63. Palette availability does not establish placed paths.

`ParkPaths` copies `Model.Field`; laid path cells replace **byte1** with a material index from
that terrain's own table. Every byte0 is preserved, and unlaid byte1 values are preserved.
`Park.SetPaths` gives the existing ground renderer this **same field instance**. Routing
classifies its live material references case-insensitively (`pa_str/cnr/ctr/edg/end/tju/xrd/
que/squ/icn`); it cannot walk over arbitrary grass. The demo lays `jpa_squ1` for the public
path and `jpa_que1` for the queue. Tile orientation and original automatic junction selection
are not reconstructed. Index zero remains a sentinel, even when its palette name is ground.

The grid's evidence comes from the existing reader and [heightfield investigation](heightfield.md):
M3D2 `+0x44`, row-major `(z*NX+x)*2`, skip test on byte0 bit 0, and byte1's material-table
lookup in `0x222fe8`. That consumer was revisited with `tools/r5900dis.py`; its R5900 SQ/LQ
prologue is not valid input for an ordinary MIPS32-only linear walk. Construction uses byte0
**bit 0 only**, as the runtime tile-map fill at `0x14E700` sets the no-build flags only for
that bit (see `Model.HeightField.Buildable`). Occupancy and
fixed scenery further restrict construction; walking additionally needs an actual path material.
This remains a demo navigation policy, not a recovered guest navigation service.

### 2026-09-22: correcting the whole-byte predicate

The earlier claim that FANTASY was unsupported was wrong. `CanBuild` tested `Raw0 == 0`,
which eliminated **all** FANTASY terrain-1 and terrain-2 cells. The bit-0 fix in `91fc774`
is retained. The site search, its ranking and its scenery constraint are unchanged; no
constraint was added to recover either old SPACE origin.

`tools/TPW.PS2.VisitorAudit/VisitorExpectations.cs` is audit-only code linked into the Godot
audit. Before constructing a scenario, it reads the original terrain and SAM, checks every
cell against `(byte0 & 1) == 0` plus the existing scenery projection, and exhaustively fits
the complete footprint/queue/public-path stencil. It ranks valid origins by integer squared
distance of the footprint centre to the grid centre, then row and column. It never runs the
scenario, routing or VM to get an expected answer. The material classifier and scenery
projection are shared dependencies; this is an independent oracle for the changed predicate,
layout and visitor timing, not another reconstruction of scenery collision.

Every verdict includes the pre-placement bit-0 buildable count, actual/expected eligible count
(after scenery), and complete-layout count. Empty eligibility fails before running any cycle.
The production `CanBuild` mutation to `Raw0 == 0` fails first on FANTASY/1: **3928 buildable,
0 actual eligible, 2487 expected eligible**. It cannot pass by iterating an empty set.

| Terrain | Buildable (bit 0 clear) | Eligible after scenery | Complete layouts | Raw0 == 0 |
|---|---:|---:|---:|---:|
| SPACE 1 | 4068 | 2587 | 1302 | 2334 |
| SPACE 2 | 3176 | 2084 | 915 | 1639 |
| FANTASY 1 | 3928 | 2487 | 1207 | 0 |
| FANTASY 2 | 3604 | 2498 | 725 | 0 |

A graphical check found a real omission in the first version: grid-only placement put part
of the public path through the fixed entrance scenery. The old terrain-1 site was `(41,19)`;
public cell `(44,28)` has byte0 zero but intersects the scenery. `ParkPaths` now projects the
terrain's actual triangles, through their complete parent transforms and the viewer's Z mirror,
onto the grid. Triangle/square separating-axis tests also catch thin walls that a centre-only
sample misses. Any intersection excludes new construction, including authored roads and tree
canopies. This is a conservative collision policy, not recovered PS2 walkability.

The revised placement and the full visible path were captured and inspected. It also exposed
why a rendering check belongs alongside the managed route checks. The existing terrain still
has holes and incomplete height rendering; this work does not close those gaps.

| Terrain | Grid | Ride footprint origin | Spawn/departure | Queue front → tail | Exit portal |
|---|---|---|---|---|---|
| SPACE 1 | 96×54 | (37,19) | (35,28) | (39,22) → (39,25) | (40,22) |
| SPACE 2 | 72×62 | (33,22) | (31,31) | (35,25) → (35,28) | (36,25) |
| FANTASY 1 | 80×60 | (30,26) | (27,36) | (31,30) → (31,33) | (32,30) |
| FANTASY 2 | 76×62 | (36,28) | (33,38) | (37,32) → (37,35) | (38,32) |

The ride uses its own `Info.Shape`, with `2` on the last row. The queue front is immediately
outside that row. The exit is an **explicit demo portal** one cell to its right. No claim is
made that the `N` symbol or engine exit offsets have been decoded by this work. Spawn is a
chosen endpoint of the constructed public path, not the original park-gate spawning service.

Bugs TV's SAM footprint is 4×4, with entrance `2` at local `(1,3)`; Orbiter is 6×3,
with `2` at `(2,2)`. FANTASY now completes real script-driven cycles on both terrains in
both audits. Its nonzero flags are preserved, including on laid public and queue cells.

### HALLOW feasibility

HALLOW also has usable ground. Terrain 1 has **3906 buildable / 3298 eligible** cells
(`Raw0 == 0`: **0**); terrain 2 has **3924 / 2426** (`Raw0 == 0`: **2518**).
Hocus Pocus (`/rides/candle/Candle`, SAM **2100**, capacity **20**) has a 5×5 footprint,
south entrance `(2,4)` and the supported HUSH/HOP mailbox protocol. The same complete
layout has **1891** sites on terrain 1 (best origin `(45,23)`, queue front `(47,28)`,
spawn `(43,34)`) and **1059** on terrain 2 (origin `(33,19)`, front `(35,24)`, spawn `(31,30)`).
An external managed feasibility probe completed all four guests on both, without a script
fault; this is not a committed rendered HALLOW audit.

Supporting that ride in `VisitorScenario` needs a HALLOW stem mapping, an independent
RSS/APS timeline derivation (its Start/Main/End waits and repeated sound-event waits differ),
and the same rendered footprint/queue/visibility checks on both terrains. A demo world
selector would make it accessible interactively. No broader terrain predicate is needed.
This is ride-specific: Brain Buster requires the currently unsupported BOUNCE/BOUNCING/
UNBOUNCE service and Pumpkin Castle uses WALKON/WALKOFF/WALKGET, so merely adding an
arbitrary HALLOW ride name is insufficient.

### HALLOW integration (2026-09-23)

Hocus Pocus now completes the four visitors on **both HALLOW terrains** in the managed and
rendered visitor audits. `RseAnimationAudit` also covers all six visitor parks, retaining its
JUNGLE/Crazy Ape regression. The interactive demo's selector remains SPACE-only.

The independent oracle reads `/rides/candle/Candle.sam`, `.rss`, `.aps` and each original
terrain before constructing the scenario. SAM supplies ID **2100**, capacity **20**, shape
`**S** / ***** / ***** / ***** / **2**`, and entrance `(2,4)`. Placement is still the exhaustive
complete-layout search. These are the fresh census and positions on this branch's `origin/main`
base; they supersede the older feasibility counts above. The existing scenery projection has
changed since that probe; this integration does not change it or `CanBuild`.

| Terrain | Bit-0 buildable | Eligible (actual = expected) | Complete layouts | Ride origin | Spawn | Queue front → tail |
|---|---:|---:|---:|---|---|---|
| SPACE 1 | 4068 | 2935 | 1481 | (45,30) | (43,39) | (47,33) → (47,36) |
| SPACE 2 | 3176 | 2287 | 1016 | (33,30) | (31,39) | (35,33) → (35,36) |
| FANTASY 1 | 3928 | 3026 | 1432 | (38,32) | (35,42) | (39,36) → (39,39) |
| FANTASY 2 | 3604 | 3313 | 1679 | (36,29) | (33,39) | (37,33) → (37,36) |
| HALLOW 1 | 3906 | 3218 | 1678 | (45,23) | (43,34) | (47,28) → (47,31) |
| HALLOW 2 | 3924 | 2867 | 1411 | (41,30) | (39,41) | (43,35) → (43,38) |

**The HALLOW t1 yaw was measured, not applied as an extra visitor flip.** Its heightfield
marker at MPS **0x2300** has composed X `(-0.1,0,0)`, Z `(0,0,-0.1)` and translation
`(0,0,-10)` (the node's 180° yaw and -100 translation through the 0.1 bind scale). Current
`Park.AuthoredPlot` deliberately uses that scale with the marker's **local** bounds, dropping
its yaw and translation for the model-base grid. The ground triangles confirm this convention:

| Terrain / observation | Actual X, Z | Drawn ground X, Z |
|---|---|---|
| HALLOW 1, Ada on queue tail `(47,31)`, 7000ms | (47.49971, -31.50020) | (47.5, -31.50004) |
| HALLOW 1, ride bind centre on 5×5 footprint | (47.49971, -25.50020) | (47.5, -25.49999) |
| HALLOW 2, Ada on queue tail `(43,38)`, 7000ms | (43.49973, -38.50021) | (43.5, -38.50008) |
| HALLOW 2, ride bind centre on 5×5 footprint | (43.49973, -32.50021) | (43.5, -32.50003) |

The rendered audit locates floor triangles from independently derived disc bounds, checks
the actual claimed cell identities against SAM, and compares the ride's transformed bind
centre to those drawn cells. It checks Ada's interpolated position, facing, queue texture and
visibility. Node identity tolerance remains **1e-5**; disc/ground comparisons retain **0.001**
for exporter rounding. No additional X handling is needed. `Viewer.cs`, `Park.cs`, the placement
sign and the SPACE/FANTASY row convention are unchanged.

The conservative `ParkPaths` scenery projection still uses the rotated marker's transformed
AABB. The oracle separately checks its asymmetric far corner, giving origin
**(-96.19229, -62.10420)**. This prevents the shared scenery dependency from concealing an
ignored yaw behind agreeing eligible counts. It establishes that transform's position, not
retail scenery collision parity or a reason to apply the marker's yaw to the drawn grid.

**Timeline from RSS/APS.** The four boardings are 10000/12000/14000/16000ms. Candle resets
`STARTNOW` by 10000 on each admission and tests a strict negative difference, so RUNNING rises
at **26100ms**. All three animation calls are `TRIGWAITANIM ... 0 0` followed by `WAIT4ANIM`.
The oracle checks the complete explicit WAIT sequences between them, rounding each wait to
100ms independently. Start's waits total **4400ms** after rounding; End's total **4500ms**.
Sound/event services are neither changed nor used as timing observations.

| APS slot (variant 0) | Frames at 30fps | Script call | Actual queued start | WAIT4ANIM resumes |
|---|---:|---:|---:|---:|
| Start | 200 | 26100 | 26100 | 32500 |
| Main | 100 | 32500 | 32766 | 35800 |
| End | 240 | 35800 | 36099 | 43800 |

RUNNING falls at **43800ms**. HOP returns Dee/Cy/Ben/Ada at
**43800/44800/46800/48800ms**; Ada clears the held exit mailbox at **50800ms** and departs
at **66800ms**. Candle returns directly to loading without Orbiter's empty-ride guard, so
another **empty** run starts at **60900ms** while Ada is walking out. The oracle checks that
third RUNNING edge too. Reopening the closed control at 19000ms boards at 19600ms, after the
next ENDSLICE tick and Candle's explicit loading `WAIT 500`.

The baseline audits also needed updates for existing main changes: head operations now report
through `HeadChanged`, so the scenario supplies the disc model's `0x80` fitting count and the
managed audit verifies the same ADDHEAD/DELHEAD guest identities through seat callbacks.
Placement now keeps model Y=0 and no longer draws debug baseplates, so the rendered audit
measures claimed floor cells. `RseAnimationAudit` advances the presenter every tick and gives
its independent APS reference the preceding records' visibility state; recreating a wholly
visible Main model incorrectly resurrected Crazy Ape's destroyed crate. It requires visibility
to be stable across each prior record's final ten frames before using that end state.

`teeth.sh` retains the whole-byte, frozen-motion, wrong-HUSH-ID, displaced-node and frozen-APS
controls. It adds a production mutation dropping only HALLOW t1's marker yaw, and a rendered
HALLOW-only X reversal. The former must fail on the independent marker position even though
the shared eligible totals agree; the latter must fail on Ada's position with unchanged counts.
All verdicts include buildable/eligible counts. No ride sound, `SPAWNSOUND`, `EVENT`, or
`TRIGWAITANIM` implementation is changed; Thrill Grill is not part of these scenarios.

Validation: managed visitor, rendered visitor and `RseAnimationAudit` all pass on all six parks.
Whole-byte, ignored-yaw, frozen-motion and wrong-HUSH-ID mutations exit **1**; displaced Ada,
HALLOW X reversal and frozen APS exit **2**. The ignored-yaw mutation reports **3906 buildable,
2754 actual / 2754 expected eligible** but fails the marker-position identity: actual origin
approximately **(0,-10)** versus **(-96.19229,-62.10420)**. Baseline and restored visitor runs
pass. All builds used `MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0`; the game was
built before scenes using the supplied Godot 4.6 mono binary. Complete logs:
`/tmp/tpw-visitor-teeth.YxSKrG/` (including `rse-animation.log` and all failure controls).

## Movement and queue ownership

`VisitorSimulation` owns registered guest identities and the states Outside, Walking, Queuing,
Riding, Alighting, Leaving and Departed. It uses cardinal BFS over the live path grid. Public
routes cannot cut through the queue; queue routes remain in that ride's queue cells. Queue
order is arrival at its tail. Another control registers guest 808 before 909, with 909 arriving
first, and requires boarding order **909,808**.

Movement is exact integer edge progress: **1000 units per cell**, **100 units per 100ms**
step. No guest moves twice when it enters the queue during a step. Current and destination
cells are reserved, preventing cell overlap, passing and opposing edge swaps. Blocked routes
remain blocked explicitly; there is no teleport or direct-line fallback. Outside guests wait
for an unoccupied spawn cell. One guest completes its visit before reaching Departed; its
identity remains available afterward.

The audit reads `Cell`, `NextCell` and `EdgeProgress`, not a proxy velocity or a rounded screen
position. Closed-queue Ada has exactly the front cell, no next edge and integer progress zero
through the checked interval. The simulation's position resolution is **0.001 cell**, with
100ms scheduling; rendered position checks allow **0.00001 model unit** float error. These
are scoped quantities, not an assertion that the original game's motion converges or stops.

## The script, not a host timer, boards and unloads Ada

The matched sources are SPACE `/Rides/orbiter/orbiter.rss` and FANTASY
`/Rides/bugstv/bugstv.rss`. Their relevant instruction sequences are checked before either
audit runs. While loading, Orbiter reads `LETMEON`, resets `STARTNOW` to the
current time plus 10000, executes `HUSH VAR_LETMEON` and `ADDHEAD`, clears `LETMEON`, increments
`ONRIDE` and decrements `SPACELEFT`. The host offers only a guest standing at the queue front;
a consumed mailbox is accepted only if that exact ID is present on the VM's guest stack.

The stack consumer was re-read from the owner's ELF with `tools/r5900dis.py` at
`0x1be28c..0x1be370`: HUSH writes the evaluated value through instance `+0x20` indexed by
`+0x44` and increments that index; HOP decrements the same index and loads that entry into
its destination variable. `RseMachine.GuestIds` exposes only this guest region, excluding
the call-stack region. This is why unloading is **LIFO**, even though queue admission is FIFO.
See [VM evidence](rse-vm.md) for scheduling, critical sections, timers and animation semantics.

Since four riders do not fill the SAM's ten seats, Orbiter's own timeout starts the run.
`SUB TEMP STARTNOW TEMP; BRANCH_NV run` makes equality still wait. The VM selects its real
Start/Main/End APS records. The simulation never writes `ONRIDE`, `SPACELEFT`, `RUNNING`,
`STARTNOW`, `COUNT`, or the guest stack.

On unload, `HOP VAR_LETMEOFF; DELHEAD; CRIT_UNLOCK` produces the actual guest ID. The script
then loops on `LETMEOFF` and decrements `ONRIDE` only after the AI clears it. The simulation
holds that mailbox while the guest occupies or waits at the exit portal, then clears it only
when the guest finishes the first outgoing edge. This exercises real backpressure.

The path derivation is the same on all four grids, translated from each SAM entrance.
Ada walks four cells east from spawn, then six north to the queue front: **10 edges at
1s/edge**, with the queue tail reached after seven. Reservations space followers by two
seconds despite arrival requests every 1.5s. Boarding is therefore **10000,12000,14000,16000ms**.
Orbiter remains below capacity and starts at `16000 + 10000 + 100 = 26100ms` (strict negative
timeout). Bugs TV fills all four seats; its final `CRIT_UNLOCK` resumes at **16100ms**,
sets RUNNING, then its explicit `WAIT 1000` puts Start at **17100ms**.

APS variant-0 Start/Main/End lengths are **45/100/100** frames for Orbiter and **40/100/10**
for Bugs TV, at 30fps. Each full animation end is `max(call, previous full end) +
trunc(frames*1000/30)`. Script waits resume `max(300, remaining-300)` ms later, rounded up
to the next 100ms tick. Start/Main/End resume at **27300/30700/34000ms** for Orbiter and
**18200/21500/21800ms** for Bugs TV. Orbiter's Main uses TRIGWAITANIM + WAIT4ANIM; Bugs TV
uses WAITANIM. This arithmetic derives RUNNING's falling edge, without reading a VM trace.

| Event | SPACE (both terrains) | FANTASY (both terrains) |
|---|---:|---:|
| Ada spawn, visible, first edge progress 100 | 100ms | 100ms |
| Ada stands on the queue tail | 7000ms | 7000ms |
| Ada boards at front, hidden | 10000ms | 10000ms |
| Last guest boards | 16000ms | 16000ms |
| RUNNING rises | 26100ms | 16100ms |
| RUNNING falls; Dee returned, mailbox held | 34000ms | 21800ms |
| Cy returned | 35000ms | 23100ms |
| Ben returned | 37000ms | 25100ms |
| Ada returned at exit portal, visible | 39000ms | 27100ms |
| Ada clears first exit edge, mailbox acknowledged | 41000ms | 28800ms |
| Ada reaches spawn/departure, hidden | 57000ms | 44800ms |

The first unloaded guest clears the portal after 1s; followers clear after 3/5/7s because
both current and next cells are reserved. Bugs TV's `WAIT 300` after decrementing ONRIDE
delays subsequent HOPs by 300ms, while reservations still determine the same clearance
schedule. The exit loop is 17 edges: three east, six south, eight west. Ada departs 16s
after her first-edge acknowledgement. The managed audit compares her expected state,
cell, next cell and integer progress **at every 100ms tick**, as well as every waypoint
and transition. The rendered audit uses that independent pose and checks both sides of
boarding, alighting and departure visibility changes.
All of Ada's intermediate waypoint identities are compared with the explicit inward L and
outward loop, not merely the endpoints or number of steps. `ADDHEAD`/`DELHEAD` request IDs
are compared with **101,202,303,404 / 404,303,202,101**. The host still records those head
effects without attaching character geometry to seats.

## Rendering and audit teeth

2026-09-22, `visitor-rows`: the visitor fork predates main's row reversal in `9481dfc7`.
The view now supplies `Park.PlotSpace` and derives its origin from the transformed plot
corners, matching main's tiles. At the sim → world boundary, guest Z is
`Origin.Y + (Height - p.Z) * CellSize`; facing reverses its Z delta too. The simulation
coordinates and shared `Paths.Field` instance stay unchanged. The measured subtraction in
`Park.TryPlace` from `c05af10` is unchanged, as are main's tile convention and `Viewer.cs`.

The geometry audit checks positions on all four terrains: Ada's world queue-tail centres
at 7000ms are approximately **(39.5,-25.5)** / **(35.5,-28.5)** for SPACE and
**(31.5,-33.5)** / **(37.5,-35.5)** for FANTASY. Ride footprint centres are approximately
**(40,-20.5)** / **(36,-23.5)** and **(32,-28)** / **(38,-30)** respectively.
It reads floor triangles and footprint vertices, checks queue texture bytes, and exercises
interpolated movement and facing. Node-position identity retains **1e-5** tolerance;
comparisons to authored tiles allow **0.001** for exporter/bind-scale rounding.

Bugs TV exposed another Orbiter-only assumption in the audit: `Park.Bounds` measures tight
vertex bounds, while placement measures the union of transformed per-material surface AABBs.
Rotated decorations make these centres differ (about **0.005 X / 0.0592 Z**). The audit now
derives the placement envelope from the disc's indexed triangles grouped by material and
transforms each local box corner through its full bind chain. It does not query the placed
node for its expected centre, change placement, or widen tolerances. That independently
derived bind centre must still land on the actual drawn footprint, which must land on the
SAM cells. The protected placement sign and reversed-row presentation remain unchanged.

`VisitorParkView` uses the existing `Park` ground builder and `RseModelPresenter`. Ride
placement now measures bounds including the root transform, so both a mirrored model root
and an unmirrored holder have correct offsets. The holder keeps its position when the VM
changes APS records. Character nodes are keyed by guest ID and follow the simulation position;
Riding/Outside/Departed guests are hidden, and Alighting guests are visible again.

The Godot audit selects `/Chars/Girl1a/girl1a.mps` independently of the visitor registry and
compares Ada's actual geometry against it. It checks her real node position and visibility at
spawn, walking, queue, boarding, ride running, unloading and departure. It locates the drawn
queue floor and compares its bound texture bytes with the ordinary resolver's `jpa_que1`.
It also checks the presenter's live APS record/frame against the VM host. These use the existing
mesh/APS evaluators, not an independent PS2 framebuffer oracle.

`teeth.sh` runs the unchanged managed audit, temporarily mutates production code, rebuilds,
requires exit **1** and the specific failure, restores the files with an EXIT trap, then
reruns the original audit. No fault switches live in simulation code. Optional Godot checks
require exit **2** for a displaced rendered node. The current harness does not modify
`VisitorParkView.cs`; the historical row-conversion mutation is recorded below. All builds
disable MSBuild node reuse and the MSBuild server.

| Deliberate mutation | Required failure |
|---|---|
| `CanBuild` bit-0 predicate → `Raw0 == 0` | `FANTASY/1 bit-0 eligibility is empty`; buildable **3928**, eligible **0**, expected **2487**, exit **1** |
| Movement increment `100` → `0` in `VisitorSimulation.Move` | `Ada movement at 100ms: expected 100, got 0` |
| HUSH stores offered ID **+1**, preserving stack occupancy | `Boarding identity lost: Ada (101) is absent from RSSE HUSH stack` |
| Audit moves Ada's actual rendered node to the origin | `Ada rendered position identity at 100ms`, Godot exit **2** |
| Historical check: view reverts guest Z from `Height - p.Z` to `p.Z`, rebuilt against the unchanged audit | `Ada rendered position identity at 100ms`, Godot exit **2** |

For `canbuild-bit0`, both visitor audits pass on all four terrains, before and after the
mutations above. The whole-byte, frozen-movement and wrong-guest controls exit **1**;
the displaced-node control exits **2**. `RseAnimationAudit` passes its exact Main:1 vertex
checks at frames **4.02** and **19.02** and completes; `--mutate-freeze` exits **2**.
All requested builds used both server-disabling environment variables, and the game was
built before running scenes. Logs: `/tmp/tpw-visitor-teeth.nOIWCX/` and
`/tmp/canbuild-probe/{rse-animation,rse-animation-freeze,hallow-sites}.log`.
`Viewer.cs`, `Park.cs`, `VisitorParkView.cs`, the scenario's placement search and simulation
were not changed. No original disc assets or generated binaries are committed.

For `visitor-rows`, the entire `c05af10` version of `VisitorParkView.cs` was also restored
temporarily and rebuilt against the updated audit: exit **2**, with the Orbiter bind centre
**21 units** from its drawn footprint. Restoring the fix passes SPACE t1/t2; all existing
mutation controls still fail. `RseAnimationAudit` passes, and `--mutate-freeze` still exits
**2**. Logs: `/tmp/tpw-visitor-teeth.x7oVGt/` and `/tmp/tpw-visitor-rows-proof.ivpweoak/`.

Unmodified and restored managed/geometry audits pass. Controls also remove a real connecting
path cell, hold a closed queue and reopen it, preserve terrain flag/material identities, reject
construction through the fixed entrance scenery, enforce guest cell reservations, and compare
identical final VM/guest state under different caller frame cadences.

The earlier visitor integration also passed `TPW.PS2.RseAudit`, including its source/binary
alignment, ride guest handshakes, timers and failure guards. Final mutation logs are outside
the repository at `/tmp/tpw-visitor-teeth.NLTrFQ/`; trace logs and inspected captures are under
`/tmp/visitor-probe/`. They contain no new repository fixtures. The graphical SPACE-2 capture
at 39000ms shows Ada at the exit and Ben, Cy and Dee already walking away.

## What could not be established / remains absent

* Original guest navigation, spawn gates, decision-making, route costs, speeds, crowd rules,
  queue abandonment, needs, happiness, spending and ride selection. BFS, FIFO admission,
  cell reservations and clearance timing are explicit new simulation policies.
* Raised paths, slopes, stairs, bridges, corner heights and original terrain-cell-to-world orientation are not independently validated
  against PS2 execution. The scene uses the existing viewer's mirrored grid and flat Y=0 floor.
  Scenery projection is deliberately conservative; a tree canopy excludes the ground below it.
* Seat transforms and guest head attachments. Skinning itself is done and audited (below), but
  a guest only animates where its caller passes a skeletal record, and the guest actors did not
  yet. ⚠ `tools/skin.py` documents the descriptor correctly and its bone-index space and
  `bind_positions()` check wrongly -- see below. Guests visibly translate along paths; that is
  not evidence of seated riding geometry.
* A fully decoded original park-gate/ride-exit link. The demo lays paths explicitly from a
  chosen spawn endpoint to the SAM entrance edge and supplies its own nearby exit portal.
* Advisor quantities: `advisor:findings/advisor.md` was read, but it explicitly leaves the
  meanings of the visitor-related state producers unproven. Rule 0 tests `v4>3`, `v0==0`,
  `v14!=0` for OPEN_PARK; that does **not** establish `v14` as our waiting-guest value.
  Calendar state and event-counter ranges are established there, individual event meanings
  are not. This work does not fabricate an advisor state mapping or claim advisor integration.
* Original RSSE scheduler cadence, complete animation blending/visibility carryover, sound,
  particles and breakdown/repair paths. Existing VM boundaries in `rse-vm.md` remain in force.
* Arbitrary ride scripts, dynamic ride/path editing, multi-ride scheduling, save/load and
  continuous population replenishment. Removing a path under a moving guest faults explicitly;
  a disconnected route before walking remains blocked. Finite Orbiter and Bugs TV cohorts are audited
  end to end; HALLOW remains a feasibility result pending scenario and rendered audit integration.

## Skinned characters (2026-09-23)

The 20-byte skeletal path is now executed, not just decoded: `core/TPW.PS2.Data/Model.Skin.cs`
reads the skin at `mesh+0x90`, `core/TPW.PS2.Data/SkeletalPose.cs` samples a skeletal record
the way the game's sampler does, and `game/AnimatedModel.cs` re-skins every skinned part from
that pose each frame. `tools/TPW.PS2.SkinAudit` checks it against the disc without Godot:

```sh
dotnet run --project tools/TPW.PS2.SkinAudit -- /path/to/disc.bin      # or a bare DATA.WAD
```

### Read out of the executable

* **`FUN_001a8da8`, skeletal half.** One 4x4 per track into a 35-slot stack array indexed by the
  track's node u16; then every mesh with a non-zero `+0x90` is skinned as `Σ weight × (position ×
  M[bone])`, the whole affine result scaled by the weight, and written through the `mesh+0x98` run
  list. **No hierarchy is composed at any point**: a track's rotation and position are the bone's
  whole transform, and a bone the record does not key keeps whatever its slot held.
* **Key search**: the first key pair whose later time is at or after `floor(frame)+1`;
  `t = (frame−t0)/(t1−t0)`; past the last key the last pair at `t = 1`. A channel with fewer than
  two keys is read from key 0 with no search.
* **`FUN_00167a48`** (rotation, SLERP over the dot product's `acos`; linear within 0.001 of
  parallel; an antipodal branch that blends against a perpendicular over π/2 and leaves `w`
  unblended; normalised on the way out. No short-path sign flip) and **`FUN_00167d18`** (position,
  plain lerp of the int16 fields).
* **`FUN_0016f220`** fills the 4x4 sequentially: eight register args into words 0–2, 4–6, 8–9;
  the ninth (`2yz+2xw`) from the stack into word 10; the position from the stack into words 12,
  13, 14, and the call site (`0x1a90b8`–`0x1a90d0`) stores it as **(key.x, key.z, key.y)**. ⭐ The
  Z-up-to-Y-up swap is the game's, for rotation columns and translation alike. The quaternion
  is (x, y, z, w): the formula's three diagonal terms are `1−2(y²+z²)`, `1−2(x²+z²)`, `1−2(x²+y²)`
  and the fourth component appears only in cross terms.
* **`FUN_001a8c30`**, the skeletal record's `small` array: one 8-byte entry per MESH holding an
  int16 show/hide timeline with the same sign convention as the 48-byte visibility channel.
  Three records on the disc carry one (FatMechanic 2, hunter 1); none of the kids do. Applied by
  the viewer since the note under "Not done" below.

### ⭐⭐ The bone index is a HELPER index

Both the skin's bone byte and the track's node u16 index the same matrix array and neither is
resolved against the model. On the data it is `node = meshCount + bone`, not `skin.py`'s "meshes
then helpers": under it the tracks of every kid cover exactly every helper except `Bip01
Footsteps` and the `Dummy` nodes, the head mesh's two bones become Neck and Head rather than
Pelvis and Spine, and the bind-pose control below closes to a hundredth of a unit where the other
reading misses by thousands. `skin.py`'s `bind_positions()` returns world-unit points against
vertices in the tens of thousands and never reproduced a vertex.

### The bind-pose control, and what it can and cannot hold

The game stores no bind pose. What the audit skins the bind with is a rule **measured** on the
data (`Model.SkinBindRotation`): a bone's bind rotation in vertex space is `D1 · R · D2` with `R`
the bone's world rotation relative to `Bip01`'s (basis rows normalised), `D1` the y/z swap and
`D2` a quarter turn about Y -- exact to 1e-3 on every well-determined bone of 23 of 24 rigs. The
bind **translations** are not derivable (the hierarchy's translations scale 9,345 : 4,566 :
2,907 : 33,827 vertex units per unit along one spine), so each (mesh, bone) translation is solved
by least squares. Under test therefore: the reader's offsets and byte-wide counts, the index
space, the weights, the run list and the row-vector arithmetic; **not** the translations.

Measured on the disc (24 characters, 81 skinned meshes, threshold 1.0 vertex unit on rigs
14,000–60,000 units across): **19 rigs close at 0.012–0.053**; five do not, named with their
errors: boy2a 220.7, handyman 59.3, guard 5.8, girl2a 3.9 -- on all four the single-bone vertices
close (≤ 0.03, boy2a 21.8) and only blended vertices miss, i.e. those skins are not one rigid
transform per bone, a mesh edited after it was weighted -- and Researcher 499.7, whose every bone
is a consistent ~1° (0.02 per element) from the rule, amplified by a placard 13,000 units out on
the hand. The audit exits FAIL on those five rather than excluding them.

⭐ **The control with nothing solved**: skinning a record's frame 0 straight through the game's
matrices (keys → `BoneMatrix` → `Deform`) and comparing with the authored vertices. FatMechanic's
`Start` frame 0 returns its authored mesh to within **1.3 units** with no fitted quantity anywhere
-- the whole runtime pipeline end to end. Every kid's records start mid-gait, so for them it
reports only the nearest (3,500 units, an idle).

### The animation, proved the way the walking was

Slot 1 record 0 (16 frames at 30 fps) is the walk on every kid: both feet travel ~6,300 vertex
units along the forward axis in anti-phase (corr −0.95) with an 800-unit pelvis bob; slot 2's six
records are idles. Sampled through the game's own search and interpolators, boy1a's `Bip01 L
Foot` is at (1620, −9512, 5364) at frame 0 and (860, −7464, 11698) at frame 8 -- 6,700 units,
model (0.049, 0.042, −0.072) → (0.029, 0.095, 0.092) through the mesh's world matrix -- and the
body's skinned centroid moves with it. 21 of 24 rigs show a bone moving; the other three (Boy2a,
Boy3a, Boy4a) carry only flag-0x80 records whose tracks live in Boy1a's file and must be given it.

### Not done / not established

* The guest actors in `Viewer.cs` and Ada in `VisitorParkView.cs` are still built with no
  record; wiring them is the callers' change (`new AnimatedModel(model, aps, walkRecord, tex)` and
  `SetFrame` on the park clock).
* ~~The per-mesh show/hide lists (`FUN_001a8c30`) are read but not applied~~ -- applied
  (`SkeletalPose.MeshVisibility` / `MeshShown`, `AnimatedModel.SetFrame`): three records carry
  them, and they read as intent -- FatMechanic's `Start` moves the toolbox from `toolboxinhand`
  `[-16, -33]` to `toolboxfree` `[0, 16, 33]` at frame 16 and `End` moves it back (`[0, 16, 41]` /
  `[-16, -41]`); hunter's `Main` juggles `gun1 [-5, 41, -50, 71, 75]`, `gun2 [0, -5, -41, 50, -71,
  -75]`, `gun3 [0, 5, -41, -50, -71, -75]`. The game's rule is kept verbatim (before the first
  magnitude: shown; the first pair still ahead decides by the sign of its earlier entry; past the
  last entry the state is left) rather than reusing `VisibleAt`, which would differ on a
  one-entry list -- none exists. Every list ascends in magnitude and ends one frame past the
  record's duration. Prop bones some records leave unkeyed (Box01–03 on dino/flower/gnome, the
  toolboxes, the guns) still sit at the identity here where the PS2 has stack garbage; with the
  lists applied, those props are hidden when their record says so.
* The bind translations are solved, never read; the five rigs above are reported, not explained.
* Rotation keys never take the short path across the hemisphere, as in the game; no character
  track on the disc starts after frame 0, so the sampler's lack of a clamp is never exercised.

## Which record is a guest SITTING?

Each kid in `/Chars` ships 16 skeletal records. Composing every one at frame 0 through the bone
hierarchy and measuring the left leg gives a clean split:

| record | knee bend | thigh, from straight down |
|--------|-----------|---------------------------|
| `Idle` v0, v1, v2, v4, v5; `Start`; `Main`; `End`; `Unused1`, `Unused8`; 12; 14 | 74° | 46° |
| `Idle` v3 | 91° | 43° |
| **`Load` v0** | 62° | **140°** |
| **`Load` v1** | 50° | **143°** |

⭐ Fourteen of sixteen hold the leg hanging down at about 46°. Exactly two raise the thigh past
horizontal, and both are variants of **slot 3**. That is a seated pose, and it is what a rider in
a ride should be playing rather than the bind pose. The slot being named `Load` -- a guest being
LOADED onto a ride -- agrees, but the angles are the evidence: the slot-name table is the ride's
and need not mean the same thing for a character.

⚠ THE ABSOLUTE ANGLES ARE NOT THE POINT, the split is. The "from straight down" reference assumes
the character's up axis; what is load-bearing is that fourteen records agree with each other to a
tenth of a degree and two disagree by ninety-odd.

⚠⚠ This rests on `node = meshCount + bone` above. Under `skin.py`'s old "meshes then helpers"
reading the same measurement is noise, so it is one inference standing on another -- both with
controls under them, and neither read out of a consumer.

## ⭐⭐ A seat helper's ORIENTATION is authored per seat — there is no constant to flip

Every 0x80 fitting on **Crazy Ape** (`monkey.mps`) carries the *same* basis, and it is exactly a
180° yaw: X `(-1,0,0)`, Y `(0,1,0)`, Z `(0,0,-1)`, det `+1` (a proper rotation, not a mirror), with
only a ±5° pitch that tracks the row of the banana. Sixteen seats, no exception. Read against the
ape's `m_body`, all sixteen come out at ±180°.

That reads as a constant bug and it is not. Across **all 21 models that have 0x80 fittings**, the
seat-vs-body angles are:

```
Bird       9 seats  [0]                cart      6 seats  [0]
ape        2 seats  [90]               croccar   6 seats  [0]
bumper     5 seats  [-120,-20,55,120,180]        dizzyd    4 seats  [-90,90]
gk_*       1 seat   [-180]             incagod  32 seats  [90,94]
king       9 seats  [-90]              manic     8 seats  [-180,180]
monkey    16 seats  [-180,180]         mumbo     5 seats  [-125,-45,-2,101,180]
porkpie    8 seats  [-162,-75,14,103]  spider   40 seats  [-155,-154,-108,-69,-68,-16,23,65,112,162]
totem     13 seats  [-180,0]           tvsim    27 seats  [0]
volcano   16 seats  [-170,-150,-120,-100,-75,-55,-30,-10,10,30,60,80,105,125,150,170]
wr_ring    5 seats  [-165,-84,-18,65,141]
```

**Dodgems point five different ways because they are PARKED at angles. Volcano's sixteen are a
ring, every 20-25°. `tvsim`'s twenty-seven all face one way because it is an auditorium.** The
helper basis is real authored per-seat facing, so nothing in the seat path may apply a blanket
180° keyed off the ride data — it would be right on the ape and wrong on every ride whose seats are
not parallel.

⚠ THE "BODY" REFERENCE IN THAT TABLE IS A HEURISTIC — last of `m_body`/`body`/`m_base`, else mesh 0
— so the per-ride *offsets* are soft and `gk_*`'s `-180` may only mean its body mesh was picked
differently from `cart`'s. What does not depend on the heuristic, and is the load-bearing fact, is
that **seats within a single ride differ from each other**.

So a rider that looks 180° out is 180° out *relative to its own seat*: a convention in the path
that puts a character into a seat basis, not a number in the disc. The control for that is the
**walk** path, which is known-good by inspection (a walker faces the way it is going), and the
comparison is `B_walk(s).Inverse() * B_seat` for a seat whose world forward is `s` — identity means
the two paths agree, a 180° yaw means one flip in the seat path, and anything else means one of the
two readings is wrong. Script: `tools`-adjacent `yaw.py` (scratchpad), fittings via `Model.Fittings`.

## RSE-adapter visibility census — not the complete native service path

**September24 correction:** this section observes the managed RSE host, not every layer of
native guest visibility. Guest arrival/completion code also hides/reveals through virtual+2C.
In particular, accepted relief service hides unconditionally at20D784..790 even when its RSE
contains no LIMBO. See [native-shop-flow.md](native-shop-flow.md). The earlier exclusive
"only if LIMBO" interpretation is withdrawn; opcode presence is still a valid script fact.

Sixteen jungle shops and sideshows, three guests queued at each, 120 s. **Five take a guest inside
and eleven take nobody**, and the old check (`at least one shop took somebody in`) passed anyway —
the five that worked carried it and the eleven were never questioned.

In this RSE-only run, LIMBO (`0x1bbb30` -> `0x1fa2c8`) is the host's hide request.
The observed correlation is exact within that adapter; it does not exclude a hide from
native guest state-machine code outside the interpreter:

```
LIMBO in script: yes -> went inside 3/3   Balloon Shop, Costume Shop, Gift Shop,
                                          Steak Restaurant, Arcade
LIMBO in script: no  -> went inside 0/3   Burger, Drinks, Fries, Ice Cream,
                                          Laughing Hyenas, Jungle Spray, Busta Block,
                                          Giant Puzzle, Dino Race, Strength Bird, Gopher Whack
```

**The five you enter are rooms; the eleven you do not are food counters and standing sideshows.**
This is the scripts' observed behavior, not proof of the complete console service path.
All sixteen still hand guests back in the adapter. Whether native guest code also hides them
must be answered from its consumer, not inferred from missing opcodes.

The audit now checks the two directions that cannot be timing artefacts: **nobody is hidden by a
shop whose script never asks for LIMBO** (a statement about this RSE host, not other native visibility owners) and **every shop that took somebody in is one that asks**, plus a non-vacuity
guard that some shop carries the opcode at all. "Carries LIMBO but hid nobody in 120 s" is printed
as a note and not a failure, because an untaken branch is not a bug.

⚠ `AsksForLimbo` scans the chain as it stands after the run, not every program the shop could
reach; a shop that spawns a LIMBO-carrying child only on a later branch would read "no". That is
exactly why the hard failure is the *hid-without-asking* direction.

**⭐⭐ THE SAME FIVE IN EVERY WORLD.** The audit passes on all four, and the walk-in set is not a
jungle quirk — the same five scripted archetypes repeat per park:

| world | shops + sideshows | carry LIMBO | the five |
|---|---|---|---|
| JUNGLE | 16 | 5 | Balloon, Costume, Gift, **Steak Restaurant**, Arcade |
| SPACE | 15 | 5 | Balloon, Costume, Gift, **Moonrock Cafe**, Arcade |
| HALLOW | 19 | 5 | Balloon, Costume, Gift, **Restaurant**, Arcade |
| FANTASY | 17 | 5 | Balloon, Costume, Gift, **Restaurant**, Arcade |

Sixty-seven shops and sideshows, twenty of them walk-in, and the LIMBO correlation is exact in
every world with no exceptions in either direction. The only thing that varies is what the
restaurant is called. Four independent parks agreeing is worth more than the jungle result alone:
a bug in the limbo path would have had to reproduce this split four times.

## ⭐⭐ Some rides WALK their riders to their seats; most TELEPORT them there

Thirteen ride models on this disc carry **no park-space (`0x800`) fitting at all** — `monkey`
(Crazy Ape), `spider`, `volcano`, `bumper`, `cart`, `croccar`, `Bird`, `ape`, `wr_ring` and the
four go-karts. `WalkMilliseconds` resolves the guest-side node of a walk in space `0x800`, so on
those rides the lookup cannot succeed and every leg takes the 100 ms floor. Crazy Ape being in
that list reads as an alarming defect.

It is not one. **A ride only needs a park-space node if its script actually walks somebody.** A
script that seats its riders with `ADDHEAD` alone never calls `WALKON`; Inca Totem, which passes
`VAR_ONRIDE` as the destination so the Nth rider walks to the Nth seat, does. Asking the bytecode
which rides call `WALKON` and requiring exactly those to be timed passes in all four worlds. The
park owner, who knows the game independently of any of this, says the same thing unprompted: *"yes
i believe a few teleport them on."*

So "walks at the floor" on Crazy Ape is the correct behaviour of a ride that does not walk.

⚠⚠ **TWO WAYS THIS CHECK LIED BEFORE IT WORKED**, both worth keeping:

1. **It was first written in the script-only census, which runs without any model.** That host has
   no `NodeSource`, so *every* ride floors, and the check reported eight broken rides that were
   nothing of the kind. A walk-timing check is meaningless wherever the geometry is absent. It now
   lives with the rides that were given models.
2. **"Asked to walk but not timed" fails on its own control ride.** `WalksAreTimed == false` means
   either "a node would not resolve" or "nobody ever walked", and those are opposite verdicts. The
   CONTROL ride is placed off the path precisely so nobody reaches it, so it carries `WALKON` in
   its bytecode, never runs one, and got reported as broken. `RseMachine.WalksWereAttempted`, set
   inside `WalkMilliseconds` itself, separates the two without a proxy such as boardings.

## ⭐⭐ The rides that board nobody are the track rides — with exactly one exception

A third of the park takes no guests: 6 of 21 rides in JUNGLE, 6 of 23 in SPACE (⚠ every run since
2026-09-24 03:45 reads 6 of **22**; the 23 has no surviving log), 9 of 23 in HALLOW,
6 of 19 in FANTASY. The audit used to say why in a *comment* — "they are the coasters, the karts
and the tour bus, the rides whose scripts poll TOUR/BUMP/COAST" — which is the weakest kind of
finding: it reads like a conclusion, and nothing would have noticed a flat ride quietly joining
them.

It is now a check, in both directions, and it holds in three worlds out of four:

```
JUNGLE    6 of 21 poll a track subsystem;  6 took nobody   PASS
SPACE     6 of 23 poll a track subsystem;  6 took nobody   PASS
FANTASY   6 of 19 poll a track subsystem;  6 took nobody   PASS
HALLOW    8 of 23 poll a track subsystem;  9 took nobody   FAIL
```

The three subsystem opcodes are dead in this build by design — every branch of `0x1c1260`,
`0x1c1370` and `0x1c14e0` writes zero or discards its argument — so a script that polls one waits
forever, and those rides sit at `VAR_RUNNING 0` with a guest parked in `VAR_LETMEON` that they
never take. That is this executable's behaviour, not a gap in the port.

### ⚠ THE EXCEPTION: Thrill Grill (HALLOW) — OPEN

**`Thrill Grill` boards nobody and does not poll a track subsystem at all.** It is the only such
ride on the disc, and it is unlike the eight around it in exactly the way that matters:

```
Ghosta Coasta  polls track: yes  RUNNING 0  LETMEON 1020  CAPACITY 10  reads LETMEON: yes  WALKON: no
Thrill Grill   polls track: NO   RUNNING 1  LETMEON 1040  CAPACITY 18  reads LETMEON: yes  WALKON: yes
```

**`VAR_RUNNING 1`** — every track ride is stalled at 0, never having started. Thrill Grill's script
is alive and cycling. It reads `VAR_LETMEON`, it calls `WALKON`, a guest id is sitting in the
handshake slot waiting to be accepted, and it never accepts one. Not a fault, not closed, not
broken, capacity 18.

**FOUND. It is stuck on an animation that never starts.** The saturating-walk-table lead is dead:
its table reads `0 of 60` and `WalksWereAttempted` is false, so it never even tried to walk
anybody. Printing the PC and the yield reason names the instruction outright:

```
Thrill Grill is parked at pc 85 (yield Animation), host slot -1:-1, around it:
       75: COPY VAR_RUNNING=1 1
       78: WAIT 1000
       80: COPY VAR_COUNT=1 VAR_DURATION=1
       83: JSR @251
  ->   85: TRIGWAITANIM 4 0 0
       89: STARTSCREAM VAR_ONRIDE=0 20
```

`VAR_RUNNING` is set to 1 at pc 75 — which is why the ride *looks* alive while doing nothing —
and then it hangs one instruction later. **`host slot -1:-1` means no animation has ever started
on this ride at all.**

The chain is exact:

1. Thrill Grill's model has no slot 4, so `PlayAnimation(4, 0)` finds no record.
2. The host correctly returns 1000 ms and starts nothing — "nothing is started and nothing
   already playing is disturbed" is `0x1abc80`'s own behaviour and is well sourced.
3. `TRIGWAITANIM` then sets `_triggerSlot = 4` and re-visits itself until
   `Host().AnimationSlot == _triggerSlot`. Since step 2 started nothing, `AnimationSlot` stays
   `-1` and the condition can never become true.

**⭐⭐ STEP 3 IS SOURCED, NOT A RECONSTRUCTION — AND THE CONSOLE HANGS TOO.** This paragraph used to
say the spin was invented here because its comment cited no address. It is the console's, read two
independent ways that agree: Ghidra decompiles of `0x1bcfa8`/`0x1abc80`/`0x1ab780`/`0x1ab518`/
`0x1acaf8`/`0x1ac8a0`/`0x1f8c18`, and a raw R5900 disassembly by a hand-written decoder (neither
Ghidra nor capstone), validated first on two controls whose decompiles were already in hand.

`TRIGWAITANIM` is `0x366ec0[0x13] = 0x1bd78c`, a **distinct** handler from `TRIGANIM`
(`[0x10] = 0x1bd4e0`), `WAITANIM` (`[0x11] = 0x1bd5cc`) and `WAIT4ANIM` (`[0x2e] = 0x1be01c`).
`TRIGANIM`'s body is `TRIGWAITANIM`'s **minus the gate** — no `+0xbc` load, no PC−4, no `+0xbc`
store. (Our VM sharing one C# case for those two is therefore harmless, because it branches on the
opcode inside; calling that "a confirmed defect on our side" was wrong.)

The gate, at `0x1bd7d0..0x1bd81c`: on re-visit it reads channel 0's **current slot** via
`0x1acaf8`, compares `cur + 1` against the pending `+0xbc`, and on a mismatch sets PC := PC−4 and
yields. **There is no timeout** — the `+0xa4` deadline is armed once before the wait and the gate
never looks at the clock. Nothing can put a missing slot into that channel: `0x1ab780` writes
either a slot that HAS a record or the sentinel `0xc`, and `0x1ac8a0` resets every channel to
`0xc` at construction. A writer census of all 24 `×0x38` channel accessors in the ELF finds no
other store into word [0]. The 173 `TRIGWAITANIM` sites on the disc request slots 2..11 only,
never `0xc`, so a "nothing" channel cannot accidentally match.

⭐ **And this is the ONLY opcode that can wait forever.** `WAITANIM` (its own deadline `+0xa0`),
`TRIGANIM` and `WAIT4ANIM` (the shared `+0xa4`) all finish on the clock with a 300 ms floor, so a
missing record costs them time and nothing else. That is exactly why WhirliGig — which uses only
`WAITANIM` — never hangs.

**So the real Thrill Grill hangs at pc 85 exactly as ours does.** HALLOW stays red as the console's
own behaviour, not as a port defect.

⚠ Residual ambiguity, stated: this is the ELF's semantics plus the constructor/writer census, not
an observation of a live instance. A PCSX2 savestate with Thrill Grill built would close it —
channel 0 of its context should read `0xc`.
### ⚠ "Two rides ship no animation file" was WRONG, both halves — and one half was an audit bug

**WhirliGig ships a full animation.** `/Rides/whirli/whirli.aps` (8,312 B) and `whirli.mps`
(46,512 B) are in `SPACE.WAD`. The audit said otherwise because SPACE ships a **stub that shadows
the real ride**: `/Rides/whirli.RSE` and `/Rides/whirli.sam` sit at the top level with no model or
animation beside them, and ordered by path the stub sorts FIRST (`.` is 0x2E, `/` is 0x2F). The
census described a stub and published it as a fact about the disc.

`whirli` is the only such pair — every other repeated basename under `/Rides/` is
`EventMap`/`Worn`/`effects`, which carry no `.sam` and were skipped anyway. Fixed by preferring a
candidate whose stem has a `.mps` and keeping what it has when none does, which leaves firepit
alone. SPACE now reports **0 of 22** rides with no animation slots.

**Thrill Grill ships one too — in a folder we do not search.** `/rides/firepit/` holds only
`firepit.RSE/.rss/.sam` and signs, but **`/upgrades/firepit/` holds `firepit.sam`, `firepit.aps`
and `firepit.mps`**. The loader looks for an `.aps` beside the script, so it finds nothing.

⚠⚠ **And that does not rescue the ride.** `/upgrades/firepit/firepit.aps` contains exactly **one
record: slot 5, 25 frames**. The script's `TRIGWAITANIM 4 0 0` at pc 85 asks for **slot 4**, which
is in no firepit animation on the disc. Its other requests are `WAITANIM` 0, 3, 6, 7, 9, 10 and
`TRIGANIM 5` — slot 5 is the only one that exists. Given the gate above, the console hangs there
too.

Corrected statement: **one ride's animation lives in a folder we do not search, and its script
waits forever on a slot that folder does not contain either.** Fixing the search is a real lookup
gap, and it will not un-stick Thrill Grill.

⭐ **And it is a quirk, not a systematic gap — censused before anyone chases it.** Across all four
worlds there are 86 ride scripts that carry a `.sam`, and exactly **two** have no sibling `.mps`:

```
SPACE    /Rides/whirli.RSE            -> /Rides/whirli/whirli.mps      (the stub, now fixed)
HALLOW   /rides/firepit/firepit.RSE   -> /upgrades/firepit/firepit.mps (the real case)
```

JUNGLE and FANTASY have none. So after the stub fix, **firepit is the only ride on the disc whose
model is not beside its script** — worth a note and not worth a search-path redesign. ⚠ How the
console resolves it is still unread: whether `/upgrades/` is a second search root, or the `.sam`
points at it, or the upgrade is a separate entity the ride references. Read it before generalising
from one ride's folder name.
## ⭐⭐ The five failing rigs are TWO bugs, and the bone indices are not one of them

`SkinAudit` fails five of twenty-four characters on "skinning the bind pose returns the authored
vertices". Printed side by side with three that pass, the five are not one fault:

```
                                       worst     single-bone    blended
boy1a    3 meshes 26 helpers 21 bones    0.016       0.014        0.016   ok
girl1a   3 meshes 32 helpers 25 bones    0.013       0.009        0.013   ok
girl3a   3 meshes 34 helpers 28 bones    0.014       0.010        0.014   ok
girl2a   3 meshes 26 helpers 22 bones    3.909       0.015        3.909   FAIL
guard    2 meshes 27 helpers 22 bones    5.775       0.029        5.775   FAIL
handyman 3 meshes 31 helpers 26 bones   59.322       2.690       59.322   FAIL
boy2a    3 meshes 26 helpers 22 bones  220.697      21.812      220.697   FAIL
Researcher 3 meshes 30 helpers 24 bones 499.672     499.672      159.904  FAIL
```

**⭐ BUG ONE: the blend, not the bone mapping.** Four of the five fail on BLENDED vertices while
their SINGLE-BONE vertices are essentially exact — girl2a 0.015 and guard 0.029 against thresholds
of 1. **If the bone indices were wrong, the single-bone vertices would be wrong too.** They are
not. So `node = meshCount + bone` survives this, and the defect is in how several influences are
combined, not in which bones they name. The audit already checks weights sum to 1 (worst
`1.0E-006`), so it is not normalisation either — the next suspect is which *second and third* bone
indices a multi-influence vertex is read as naming.

⚠ Bone COUNT does not explain it: guard fails on a two-bone blend (Head+Neck) while boy1a, girl1a
and girl3a all pass on two-bone blends. Do not chase "three or more influences".

**⭐ BUG TWO: Researcher's `plackard` is a different fault.** It is the only one whose SINGLE-bone
error is the worst (499.672, with its blended error a third of that), and it is a prop mesh
weighted to a single bone, `Bip01 R Hand`. ⭐ The control is `vampire`, which has its own
`plackard` mesh on the same `Bip01 R Hand` and comes out at **0.023**. So a placard in a hand works
in general and Researcher's specifically does not — which makes this a per-character data question,
not a rule question, and it should not be lumped in with the four above.

⚠ Separately and already fixed (`27460fa`): `UseRecord` threw "Index was out of range" on girl1a's
and girl4a's `Load` v0 by asking for 48-byte texture tracks on a 20-byte skeletal table. That is a
different failure from these five and is no longer in the tree.

### ⭐⭐ Bug one, measured per INFLUENCE: not the second and third bone bytes — the blend itself

The suspect above ("which second and third bone indices a multi-influence vertex is read as
naming") was tested directly, with the bone bytes used only as group labels — no helper-index
reading, no bind rotation rule involved — and it is not that. `SkinAudit` now prints the test
under every failing rig.

**The property that a consistent skin has.** On boy1a every influence of every vertex, taken
ALONE through its own bone's transform, lands on that vertex: 0.01 units over all 265
influences, blended vertices included. The exporter built each influence from the same authored
point, so each one reproduces it by itself.

**What the four blend-failing rigs have instead.** Fit each bone from its SINGLE-influence
vertices only (bone byte beyond doubt, threshold-exact on these rigs), then score the influences
of BLENDED vertices with that fit, first influence and later ones apart. The audit now prints this
for EVERY rig, named, directly under that rig's bind verdict, so one run shows the discrimination:

| rig | bind | first influence of a blended vertex vs its own bone | later influences | later influences ANY fitted bone maps within 5 units |
|---|---:|---:|---:|---:|
| boy1a, girl1a, girl3a, girl4a, HallowKid, JungleKid, SpaceKid, FantasyKid, dino, gnome, franky, flower, FatMechanic, spaceman, Alien | pass | 0.00–0.01 | 0.00–0.03 | every scored one |
| boy3a / hunter | pass | — | 0.34 / 2.56 | all |
| vampire | pass (0.023) | 13.05 over 2 | 9.57 over 31 | 28 of 154 |
| girl2a | **3.9** | **315.5** over 7 | 350.9 over 8 | **0 of 92** |
| guard | **5.8** | **171.5** over 18 | 407.5 over 26 | 24 of 100 (its Spine and Hand blends, all exact) |
| boy2a | **220.7** | (no blend led by a fitted bone) | 314.7 over 8 | **0 of 78** |
| handyman / Researcher | 59.3 / 499.7 | — | 0.01 / 0.01 | all — their misses are on UNSCORED bones: the coat tails (no single vertices) and the hands' ~1° rotation (single-bone) |

⭐ **The FIRST influence misses too** on girl2a and guard, by hundreds of units, so no misreading
of the later bone bytes can be the cause; and no re-mapping of a later influence to any other bone
brings it home (0 of 92, 0 of 78). A free per-bone affine fit over all of a bone's influences
cannot close either (boy2a Spine 700, girl2a R Calf 901 units) — no rigid transform of the bone
maps its own influences onto their vertices. Yet the WEIGHTED blend lands: 3.9 (girl2a), 5.8
(guard), 220 (boy2a). These skins carry per-influence positions whose offsets cancel only in the
blend; vampire shows the same shape at a size that still cancels (offsets of 13, bind 0.023), so
the offsets by themselves are not the FAIL — the residual they leave is. On guard it is
per-VERTEX: the Spine and Hand blends are consistent at 0.01 while the Head+Neck and Foot+Calf
blends are not, which an indexing error could not produce. **Hypothesis, not a finding:** 3ds Max
Physique's "deformable" vertices export exactly this shape (per-link offsets that sum out); the
rigid ones are the consistent vertices.

⚠ **Attribution note.** An earlier form of this line printed only for a failing rig and ABOVE that
rig's own `ok/FAIL <name>` lines, so read in sequence it looked like the tail of the previous
rig's block: the 314.72 / 350.88 / 407.47 once quoted as boy1a / girl1a / gnome are boy2a's,
girl2a's and guard's own numbers, and "it never runs on a failing character" was the same slip
inverted — it ran only on them. Now named and universal.

For the runtime nothing changes: `Skin.Deform` is the same weighted sum the PS2 does, so a posed
girl2a/guard is as consistent as its authored mesh (a 1/4,000-of-height residual), boy2a within
1% of its height. Researcher's blends are consistent (0.01 over 15); its fault is a single-bone
one — both hands' bind rotation sit a consistent ~1° (0.02 per element) off the rule, amplified
to 196 (clipboard, 13,000 units out on the left hand) and 500 (placard, right hand) — a
per-character question, as above.

⚠ Correction to the paragraph before this one: the "Index was out of range" thrower was not the
texture-track call (`TextureTracks` already returns early on a skeletal record); it was
`AnimatedModel.MorphFor` walking the 20-byte skeletal table at the 48-byte stride and handing
`Animation.Morph` garbage headers — 93 of the disc's 177 skeletal records throw, 84 are clean, and
none produces a silent garbage morph (every garbage pointer lands outside the file). Fixed in
`92375ff` on `rec.Skeletal` (flag 0x20), independent of the bone-index reading.

## ⚠ Moon Buggies (SPACE): a ride that WALKS but has no park-space node — OPEN

Widening the whole-loop audit from three placed rides to eight **doubled** how much of each world
the new checks touch (walk timing went from 8 of the 35 rides that call `WALKON` across the four
worlds, to 16 of 35) and immediately caught a ride the narrow test could never reach:

```
FLOORED: Moon Buggies calls WALKON but no node resolved -- its legs ran at 100 ms
```

`WalkMilliseconds` resolves the **guest-side** node of a walk in park space `0x800` against the
**ride model**. But that node is where the guest stands in the PARK, at the queue — and the rides
that pass this check (`dizzyd`, `incagod`) each carry exactly **four** `0x800` fittings, which is
what an entrance/exit/queue-start/queue-end set looks like sitting on the ride's own footprint.
Moon Buggies carries none and calls `WALKON` anyway.

So one of two things is true and this does not yet say which:

- the console resolves the guest-side node somewhere **other than the ride model** — in which case
  our `0x800`-against-the-model lookup is too narrow and several rides are floored that should not
  be; or
- the disc is simply like that for this ride, as it is for Thrill Grill's missing `.aps`.

⚠ Note this is NOT the Crazy Ape situation, which is settled: Crazy Ape also has no `0x800`
fittings and that is fine **because its script never calls `WALKON`** — it seats riders with
`ADDHEAD`. Moon Buggies is the case that combination rules out.

### CLOSED: its model is missing the four park fittings every other WALKON ride has

Two rides, the **identical** instruction:

```
/Rides/mbuggy/mBUGGY.RSE     72: WALKON VAR_LETMEON  1  2  3  4  kind 1  extra 1
/Rides/spawheel/Spawheel.RSE 61: WALKON VAR_LETMEON  1  2  3  4  kind 1  extra 1
```

Spawheel's model answers it — `id1/0x811  id2/0x811  id3/0x811  id4/0x811`, four fittings with ids
1..4 carrying bit 11. **Moon Buggies' model has ids 1..8 and not one `0x800` among them**: seven
`0xb1` seats, one `0x111` emitter, one `0x10b1`. Nodes 1..4 exist on that model — as SEATS, not as
park nodes — so the lookup finds nothing and every leg floors.

Every other SPACE ride that calls `WALKON` has them: bumper 4, hoverbot 4, scitour 5, spawheel 4,
tv_ride 2, whirli 4, zerog 10. **`mbuggy` is the only one with zero.** There is no companion model
in its folder to carry them.

So this is a **disc-data gap, not a port defect** — the same shape as Thrill Grill asking for a
slot 4 that is in no firepit animation. The check is correct to fail and stays red.

⭐ Worth noting the pattern across all three of today's OPEN items — Thrill Grill, WhirliGig and
Moon Buggies. **Not one turned out to be a VM bug.** Two were data the disc simply does not carry,
and one was the audit censusing a stub. The VM and the walk table were right every time; what was
wrong was twice the disc and once the instrument.

### ⭐⭐ The walker, photographed and then FILMED (2026-09-23, `a4d1377`, `e9cd996`, `7a2d85b`)

Guests now walk: each actor takes slot 1 of the `.aps` its model was built against (its own file,
or Boy1a's for the boys whose records are Shared -- every kid logged "slot 1 v0, 16 frames" from
the right file, none refused), and `Gait()` plays it while the guest is `Walking`, at the park's
25 ticks a second against the animation's 30, from the tick the walk began.

**The shot, by condition and not by clock.** A stage W winds the park one tick at a time until a
guest is Walking with 0.3-0.7 of a cell behind it, three cells out from the mouth, half a cell clear
of every other guest, then looks at it side-on from three units out. The expectation was written
before the render: feet ~0.16 model units apart at gait frames 0/8, crossing at 4/12, one arm
forward; feet together and arms down if the gait were not playing. Result (`skin_walk-w.png`):
walker #1 at (29,21) -> (29,22), fraction 0.32, 1.40 cells from the nearest of 5 guests, legs
scissored and one arm forward -- **feet apart 0.180 model units** at gait frame 1.2, measured off
the two foot bones through the game's own matrices, the bind control reading "together".

**The clip (`--walk-film=150`, `skin_walk.mp4`, 2.5 s of park time at 60 frames a second, 654 KB).**
The orbit re-aims at the followed guest every frame; every fifteen frames the census prints the
body's speed and the feet's separation off the pose drawn. Walker #1, followed for all 150 frames:
body **0.88-1.04 units/s** (GuestWalk's 1.0 cell/s), gait frames cycling (8.4, 15.6, 6.8, 14.0,
6.4, 13.6, 4.8, 12.0, 4.4 at 0.24 s spacing), feet apart **0.075-0.189** units -- widest near
frames 0/8 (0.180, 0.189), narrowest near the crossings (0.075 at frame 4.8).

**⭐ The prediction, made before the film and now measured: the feet slide about 3x.** One
16-frame cycle takes 0.53 s and carries the feet 0.16-0.19 units, while the body covers 0.53 units
in the same time -- a ratio of 2.8-3.3. The skinning is not what is wrong: the stride is the game's
own animation at the game's own 30 fps clock (`FUN_001acfc0`), and the pace is ours --
`GuestWalk.UnitsPerTick = 40` of 1,000 per cell, chosen so the demo moves. Either the console's
guests walk at about a third of a cell a second, or its gait plays faster than 30 fps; the
console's guest speed is unread, and that is the next thing to read before touching either number.
Visual confirmation of the slide is the clip's job; a still cannot carry it.


## ⭐⭐ The visitors' needs, and a correction (2026-09-23)

Eight bytes on the guest, each clamped 0..100, identified by the arithmetic that touches them
rather than by plausible names:

| offset | need | what pins it |
|---|---|---|
| `+0x75` | happiness | spawns at exactly 50, the only need seeded to a constant; a shop's DBA happiness effect is added here |
| `+0x76` | sick | a shop's DBA "vomit increase" (key `0x36`) is added here; above 92, with one roll in four, the guest vomits |
| `+0x77` | hunger | a shop's "hunger reduction" (key `0x32`) is subtracted |
| `+0x79` | toilet | ⭐ the SAME hunger reduction is also ADDED here, and it is one of only two needs seeded `rand(100)*rand(100)/100`, which piles up near zero |
| `+0x7A` | thirst | a shop's "thirst reduction" (key `0x33`) is subtracted; seeded biased-low like the toilet |
| `+0x60` | cash | `(rand(300) + 200) * 10`, so 2000..4990; below 100 the guest goes home |
| `+0x74`, `+0x78`, `+0x7B` | ⚠ **not identified** | `+0x74 > 89` triggers `FUN_0020D010(guest, 0)` and `+0x7B < 99` gates the leave check; named by their offsets |

Thresholds, from `FUN_0020C930`: hunger AND thirst both above 90 is checked **before** either single
want — and the bar for wanting a facility at all is in `FUN_0020F888`, which opens
`if (need < 0x5b) return 0`: **one shared threshold of 91**, for hunger, thirst and the toilet
alike, passed in as the need. Three constants that happen to agree would be a coincidence; one
constant read once is not; sick above 92 vomits on one roll in four; happiness below 3 is angry (id 10) and below 5 goes
home. The thought ids `+0x40` takes are the sixteen the UI table at `FUN_00216028` names
`bubbles\tb*.ssh`, and five of them agree with what `0x20C930` writes.

### ⚠⚠ The 26-byte record is a SPAWN TEMPLATE, not a rate table

I reported it as the per-tick rise rates. It is not. `FUN_001603B0` reads **one** record off the
level blob, **outside** its loop, and gives the same one to every visitor it creates:

```c
record = FUN_0015fd48(0x1a, 1);
for (i = 0; i < park->visitorCount; i++)
    FUN_00211a00(FUN_0014ac48(), record);   // base + roll(spread + 1) per need
```

So it is this park's visitors' starting personality, and there are two spawn paths -- this one and
`FUN_0020BCD0`'s hardcoded distributions.

### ⚠ The rise over time has not been found

The need setters at `0x212330..` have **no `jal` callers at all**, and neither does `FUN_0020BCD0`:
both are reached through a vtable built at runtime. ⭐ That claim has a control -- the identical
scan finds the single caller of `FUN_00211A00` (`0x160410`) and of the selection-box drawer
(`0x225F7C`), both independently known to be called. A plain word-search for those addresses finds
nothing either, and that search is worthless: it finds nothing for the known-called functions too,
because `jal` encodes its target in 26 bits and not as a literal word.


## ⭐⭐ Toilets: the authored service data, and the two paths it splits into (2026-09-24)

Historical pre-implementation investigation with astraclaw. The later managed service/body
adapter was implemented, but **its visible-Small-Toilet justification is now retracted**:
compiled kind2 relief walks to its inside entrance, hides at guest arrival, waits522 updates
with a strict deadline test, and reveals at the same position. See native-shop-flow.md for
instruction addresses. The old census below records script properties, not native visibility.

**The disc says which buildings satisfy the toilet need.** `UsageInfo.ProvidesRelief 1` appears on
exactly **two `.sam` per world, all four worlds** — "Small Toilet" and "Super Toilet", eight in
total, and on nothing else anywhere. So the discriminator is authored, not a name match or an id
list. `/Features/Toilet/Toilet.sam` also carries `Info.IsChoosable 1` ("People CAN use this", where
the gate's is 0), a model, an `.rse`, and authored stand positions:

```
UsageInfo.EntryCellStandPosX 0.5   Y 0.8
UsageInfo.ExitCellAppearPosX 0.5   Y 0.8
Info.Shape  ->  a single `2`
```

⭐ `Park.Footprint.From` already reads `'2'` as the ENTRY cell, so the toilet parses to a 1x1
footprint with `EntryX >= 0` — the predicate `IsEntranceCapable` tests — and the 1x1 case is the
one its "every small square shop" tie-break was written for.

### Historical script partition, not a proof of two native visibility paths

The early census used the RSE-only correlation above. Its inference about native service
visibility was invalid even if every opcode-presence observation was correct:

| | LIMBO | WALKON | `.aps` |
|---|---|---|---|
| Small Toilet, all four worlds | **no** | no | yes |
| Super Toilet · JUNGLE, HALLOW, SPACE | yes | no | yes |
| Super Toilet · FANTASY | yes | **yes** | **no** |

The previous conclusion "Small Toilet is serviced standing outside" does NOT follow.
Native common walking supplies its approach without WALKON, and native relief arrival
supplies its hide without LIMBO. The FANTASY script/animation outlier is a separate asset
observation, not evidence against that guest-state consumer.

⚠ **METHOD, because the first attempt was worthless.** Grepping the `.rse` bytes for the string
"LIMBO" returns "no" for every file on the disc — `.rse` is compiled bytecode and that search can
never say yes. Redone through `rse.disassemble` with BOTH controls: positive **17/17** (every shop
this file lists as LIMBO-bearing comes back yes) and negative **284/308** (the detector can say no).
A "no" on the Small Toilets means something only after both.

### Historical managed body gap — native visibility must be phase-correct

`ParkVisitors.Deliver` ends its boarding branch with `Walk.Remove(g.Id)`, and `Viewer.PlaceActors`
draws a guest only if they are in one of three sets: the walking layer, `_seated` (a `0x80` seat
fitting), or `_walking` (a scripted WALK pose). A Small Toilet's script provides **none** of the
three — no script-requested hide, WALK pose or seat — so the managed adapter dropped the
body on handover. That diagnosis located the adapter's ownership gap, not the console's
intended accepted-service visibility. Missing RSE LIMBO is not a license to draw during
native relief service.

The corrected task is phase-specific: walk visibly into the compiled entrance, hide only
when native relief service accepts the customer, and reveal at the reached position on
completion. The old standing-body patch kept the guest visible but did not implement this
native service state machine. Preserve that correction rather than calling the old patch
fully faithful because its managed lifecycle checks passed.

## ⭐⭐ The wants are AUTHORED: the game ships its own effect table, commented (2026-09-24)

Master asked for visitor wants "looking at the actual game's code". The code turned out to be the
least of it — **the data files carry the numbers with the developers' own comments beside them.**
Census of every `UsageInfo.*` integer over all **251 `.sam`** on the disc:

| key | files | range | the comment IN THE DATA |
|---|---|---|---|
| `HappinessEffect` | 23 | 5..20 | `//How much happiness to add` |
| `ThirstEffect` | 23 | 0..40 | `//How much thirst to deduct` |
| `HungerEffect` | 23 | 0..25 | `//How much hunger to deduct` |
| `VomitEffect` | 23 | 0..15 | `//How much vomit to add` |
| `LitterEffect` | 23 | 0..50 | `//How much litter to add` |
| `FatigueEffect` | 1 | 5 | `reduce fatigue by this amount` |
| `ProvidesRelief` | 7 | 1 | — the lavatory flag |
| `HoldsLitter` | 4 | 1 | — a bin |
| `ProvidesSecurity` | 4 | 1 | `The camera has a security effect` |
| `ChillsYouOut` | 3 | 1 | — |
| `SpecialIngredient` | 23 | 0..4 | `SALT=2`, and `FAT=1` on the Burger Shop |
| `Info.WhichUIType` | 7 | 1..4 | `0=rides, 1=shops, 2=sideshows, 3=features` |

So a Burger Shop deducting 25 hunger is not a number anyone here chose. ⭐ This is the line between
these and `ParkVisitors.RideIntensity` and friends: those stay invented **only** because the
globals behind them (`DAT_002EEB30/34/44`) have not been decoded. These never needed inventing.

⚠ **7 vs the 8 counted in the section above, and both are right.** 8 is per-world instances; 7 is
distinct paths. `/Features/loo/loo.sam` (FANTASY) and `/features/loo/loo.sam` (SPACE) differ only
in case and collapse under a case-insensitive key. Say which you are counting.

### ⭐⭐ `VAR_WORNON` is an OCCUPANCY LATCH, not a mess counter — and the correlation is what misled me

Exactly **7 of 277** scripts declare `VAR_WORNON`, and they are **precisely** the 7 `.sam` with
`ProvidesRelief` — nothing on either side of the difference. A correlation that clean over 277
samples looked conclusive, and the conclusion drawn from it ("this is where the mess goes") was
wrong. Resolving the references says what it actually does:

```
Toilet.rse   12  TEST  VAR_WORNON        SupBog.rse  16  TEST  VAR_WORNON
             26  COPY  VAR_WORNON 1                  30  COPY  VAR_WORNON 1
             31  TEST  VAR_WORNON                    35  TEST  VAR_WORNON
             37  COPY  VAR_WORNON 0                  43  COPY  VAR_WORNON 0
```

Test, claim with 1, release with 0: a **single-occupancy latch the script owns**. Writing soil into
it would have fought the script for the cubicle. ⭐ The correlation was real and the reading of it
was not — a variable only toilets have is a variable about *being a toilet*, which is not the same
as being about *dirt*.

And the toilet's whole guest protocol is two more lines, which is worth having:

```
Toilet.rse   48  COPY  VAR_PEEPID  VAR_LETMEON      stash who came in
            108  COPY  VAR_LETMEOFF VAR_PEEPID      hand that same one back
```

⚠⚠ **METHOD, and it took three goes.** (1) `symbols()` returned `[]` for both toilets — I was one
step from reporting "the toilets declare no `VAR_LETMEON`, so they need a service path of their
own". The control killed it: `Monkey.rse` came back with all 16, and re-run properly **both
toilets declare `VAR_LETMEON`**. A toilet IS a ride to the engine. (2) Searching the disassembly
text for `VAR_WORNON` found nothing — but the control found nothing for `VAR_ONRIDE` in Crazy Ape
either, and that ride certainly uses it. The disassembler prints variables as `v<N>`; the search
was blind. (3) Resolving `v<N>` by position was ambiguous until `RseProgram` settled it:
`VariableNames` holds exactly `VariableCount` entries and the ride name lives in a separate
`_strings` block, so python's list carries one extra leading element and **`v0` is the first
variable**. Confirmed numerically per file (header count == list length − 1) and behaviourally —
under that mapping Crazy Ape reads `ADD VAR_ONRIDE 1` / `ADD VAR_ONRIDE -1`, which is exactly what
`ParkRide`'s existing note records `king.RSE` doing.

### What this bought, and what it did not

Wired (`ParkVisitors`): a guest whose hunger, thirst or toilet crosses **91** — `FUN_0020F888`'s
single shared bar, the same one that raises the bubble — walks to the nearest facility whose
authored data answers it, in the console's own hunger→thirst→toilet tie order, instead of picking
a ride at random. On the way out, `Serve` applies the effect for **what the place is**: a lavatory
runs `UseToilet`, a shop runs the decoded purchase path with its OWN `.sam` numbers, anything else
is a ride.

⚠ **`LitterEffect` is authored and is NOT applied.** 23 shops declare it and the guest has a
`Litter` field, but the decoded purchase path (`0x20E380..0x20E45C`, findings/dba.md) does exactly
four things and littering is not one of them. Applying it anyway would be inventing a cadence and
calling it a decode. Same for `FatigueEffect`, which has no field at all yet.

⚠ **Where the console puts the mess is still not found**, and it is not `VAR_WORNON` (above). The
amount `(need-60)*2/3` is decoded; the sink is the port's own accumulator and drives nothing.

⭐ **Teeth** (`tools/TPW.PS2.ParkSimAudit/ServiceChecks.cs`, 15 checks, green in all four worlds
against four different lavatories). Both mutations were run and each reddens exactly the line
written for it: delete the `Errand` call and *only* "rode nothing on the way" fails (8 facilities
used instead of 1); dispatch on "did they complete something" instead of on what the place is and
*only* the two RIDE-control lines fail. ⚠ The single-toilet case cannot see routing at all — with
one facility in the park the random fallback reaches it anyway — which is why the routing case
puts 7 closer rides between the guest and the lavatory and asserts they used exactly one.

### ⭐⭐ The authored answers to "where does the body stand, and who draws it" (2026-09-24)

astraclaw reproduced the missing-body problem in a real scene — the guest actor is removed the
moment service ownership transfers — and went looking for positions, handback and hiding rules.
The disc authors all three. Every lavatory, with Crazy Ape as the control:

| `.sam` | `RideHandlesSprite` | Entry stand X/Y | Exit appear X/Y | other |
|---|---|---|---|---|
| `toilet` (Small) | **1** | 0.5 / 0.8 | 0.5 / 0.8 | |
| `loo` (Small) | **1** | 0.5 / 0.6 | 0.5 / 0.6 | |
| `royaloo` | **1** | 0.5 / 0.5 | 0.5 / 0.5 | |
| `horloo` (Small) | **0** | 0.5 / 0.7 | 0.5 / 0.7 | |
| `supbog` (Super) | absent | 0.5 / 0.9 | 0.5 / 0.9 | |
| `horsuloo` | absent | 0.5 / 0.9 | 0.5 / 0.9 | |
| `loo_big` | absent | 0.5 / 0.9 | 0.5 / 0.9 | `RequiresTeleport 1` |
| *Crazy Ape (control)* | *absent* | *0.5 / 0.9* | *0.5 / **0.1*** | |

⭐ **A lavatory hands you back where you went in.** Entry and exit are the same point on all seven,
and that is NOT the general case — Crazy Ape puts you out at 0.5/**0.1**, the far side of the cell.
So a toilet needs no separate exit placement and a ride does.

⭐⭐ **`RideHandlesSprite` records authored ownership**, in the data's own words: `If the script
handles the person sprite`.

⚠⚠ **AND IT IS NOT A HIDING RULE FOR THIS PORT — I claimed it was, and that was wrong.** It states
what the CONSOLE's script renderer owns. Ours owns nothing: `RseMachine` routes `ADDOBJ` to
`Host().TryEffect` and the comment beside it says *"ADDOBJ is a presentation request this host
records and does not act on"*. A script with the flag set therefore draws **nothing** here, and
suppressing our own body on it would leave the guest invisible at precisely the three Small
Toilets that set it — the bug being fixed. ⭐ The general lesson is the one this repo keeps
relearning: the game's rule and our predicate answer different questions, and an authored comment
licenses a statement about the console, never about our code. astraclaw declined to take the
shortcut on my say-so and was right to.

⚠⚠ **It is three-state and must stay so.** Three lavatories say 1, `horloo` says **0** explicitly,
and three omit it — and the control omits it too, so *absent is the ordinary case for a ride*, not
a quiet "no". Folding absent into false cannot tell the Haunted Loo's deliberate 0 from a ride
that never mentioned the question, and those are different instructions to a renderer. Exposed as
`bool?` for exactly that reason.

⚠ The stand positions differ per lavatory (0.5/0.5 through 0.5/0.9), so a constant would be wrong
for six of the seven.

## ⭐⭐ Guests could never leave: `WantsToGoHome` was decoded and called from nowhere (2026-09-24)

`VisitorNeeds.WantsToGoHome` was read off `FUN_0020C930`, documented down to the half of the gate a
"leaves when unhappy" reading would miss, and given its own arithmetic checks — and **nothing in
the port ever called it.** Grepped: zero call sites outside its own file. So every park filled up
monotonically and no visitor had ever gone home.

⚠ **This is the failure mode worth naming: dead code is indistinguishable from a working feature
from the outside.** The decode was right, the tests were right, the documentation was right, and
the park it described could not lose a guest. Nothing catches that except asking the PARK for the
outcome rather than asking the function for its answer.

Now wired: a guest who has had enough walks to the gate and is retired there — asked BEFORE any
errand or ride, since having had enough outranks both. Dropping their plan is what retires them,
because `Needs.Reconcile(_plans.Keys)` reaps any record with no plan behind it.

⚠ **The id-reuse trap is the reason that matters.** A needs row outliving its guest is inherited
by whoever is handed that id next, and reads as a visitor who arrived already miserable. There is
a check for exactly that (`leaving no needs record behind`).

⚠ **No gate, nobody leaves** — and that is deliberate: with no entrance registered a guest stays
in the park rather than being deleted where they stand. ⭐⭐ This also set the trap in the CHECKS:
a copied `ParkPaths` carries `Field.Cells` but NOT the entrance registration, so the fixture had
to call `SetEntrance` or `Gate` would be null and all three departure cases would have passed
while testing nothing. Same shape as the bug they were written to catch.

⭐ Teeth, with a control and a mutation. Broke → goes home; miserable → goes home; **solvent and
happy → stays, still walking, record intact**. Without the control, "everyone leaves immediately"
passes every positive case. Mutation run: disable the call and exactly the four positive lines
redden while the control stays green.

⚠ `Unknown7B` (the had-enough counter) still only ever FALLS — `Spawn` seeds `rand(50)` and the
ride path subtracts `rand(20)` — so its `>= 99` clause cannot currently fire, and departures come
from the cash and happiness clauses alone. The rise for it is the same unfound rise as the other
needs.

## ⭐⭐ Queueing was free, and a tool found it in one run (2026-09-24)

Straight after `WantsToGoHome`, I ported `tools/dead_port_audit.py` from the PSX port — whose own
docstring already records this exact class of bug there, including *"the idle pass was ported and
never called ... so NOBODY EVER LEFT and the park filled with maximally miserable people"*. Both
ports lost the ability to go home, independently, and neither build nor audit noticed.

It reported **8 candidates** for the PS2 port (the PSX one had 87), and the gameplay one was
`VisitorNeeds.Queue`: decoded off `FUN_0020C6A8` (happiness down, `+0x78` up), **18 references
from the checks, zero call sites anywhere.** Standing in a queue cost a guest nothing.

⚠⚠ **And the obvious wiring was wrong in a way only the existing audit caught.** Charging every
guest whose plan reads `Queued` bills *riders* a waiting cost: that intent covers both waiting at
the stub AND being aboard, because the ride owns them from WALKON onward without the plan
changing. Two `needs lifecycle` checks went red — "seated guest retains its entire side-table
state". The honest test is the ride's OWN queue, which the script empties when it takes somebody.

⚠⚠ **Then my check read 0 and would have read 0 however right the code was.** One guest and an
idle ride hand over within a tick or two, so nobody is ever in a queue at a rise boundary. **A
queue is not a queue until there are more people than seats** — the fixture now sets capacity to 1
and puts eight guests in. Peak boredom 100, happiness floor 0, against a control (no ride to
queue for) that stays 0/100. Mutation: disable the charge and exactly the two positive lines
redden.

⭐ The emergent behaviour is the game working: a long queue wears patience down, and a guest under
5 happiness then trips the go-home path and leaves. Two features that were each inert a commit ago
now compose into "badly-run park loses its visitors".

⚠ The cadence is CHOSEN, the effect is not. How often the console charges waiting has not been
read, so it rides the needs' existing `SecondsPerRise` rather than introducing a second invented
constant — one chosen number instead of two, and a guest who waits twice as long still pays twice.

### The other seven candidates, unexamined
`KanjiTable.CodeForSlot` / `TryGetImageOrdinal`, `AssetResourceDatabase.Tier`,
`BitmapFont.TryGetGlyph`, `Lighting.Modulate`, `Model.Skin.SkinBindRotation`,
`ParkPaths.SceneryBlocks`. ⚠ Candidates, not verdicts — the tool matches on identifier and cannot
see a call through a delegate or interface. Listed so the next person starts from a shortlist.

### ⚠⚠ The stranded-recovery was keyed on INTENT, and that kept it broken through two fixes

astraclaw reproduced it against the new go-home flow: a `Leaving` guest whose path was dug up under
them stayed stuck even after it was repaired and a route existed again.

⭐ **It is the one-way door** — nothing stale, nothing wrong in the data; the guest left a state
with no path back, so every snapshot of them looks individually fine and only a DURATION shows it.

**Fix one (wrong):** extend the clause from `Heading` to `Heading or Leaving`. Still stuck, and the
reason is the lesson: by the time the guest is wedged their plan says **neither**, because the
first failed attempt already reset them to `Wandering` — and a Wandering guest who is `NoRoute`
matched nothing and was skipped forever by the `!= Arrived` guard.

⭐⭐ **The intent says what they were TRYING to do, which is exactly what has been lost by the time
they are stuck. Being unable to move is a fact about the STATE.** Keyed on state now.

⚠ And resetting the plan was never enough on its own: `Wander` rewrites the plan but leaves the
walk state `Stranded`, so the original `Heading` fix swapped one stuck state for another. The
guest is now also sent to the cell they already stand on, which `GuestWalk.Send` answers with
`Arrived` — and on ground that is still gone it fails and they are retried next tick, which is the
retry that was missing.

Check: break the corridor under a departing guest, confirm they CANNOT reach the gate, mend it,
confirm they do. Mutation with the shipped clause restored reddens the second line only.

## ⭐⭐ Four invented constants replaced by read ones, and the mess sink found (2026-09-24)

Chasing "what READS `+0x78`" turned into the best return of the night. Method: census every
`lb`/`lbu` at offset `0x78` in the executable (22 across the whole binary, 20 inside the guest
code range), then decompile the functions containing them. Most were read-modify-write — the need
rising or falling. **One was a branch**, and that was the consumer.

### The rule: an unmet need docks your mood — `FUN_0020FB88`

```c
if (DAT_002eeb4c <= guest[0x78]) guest[0x75]--;   // boredom, 95
if (DAT_002eeb50 <= guest[0x76]) guest[0x75]--;   // sickness, 85
if (DAT_002eeb54 <= guest[0x79]) guest[0x75]--;   // toilet,   90
```

Three separate globals, three **independent** tests, each clamped at zero. ⚠ Not an else-if chain:
somebody bored AND sick AND bursting loses three. The check that distinguishes them asserts the
triple costs ~3x the single (18 vs 6 measured; an else-if mutation reads 6 vs 6).

⭐ This is what makes needs MATTER. Before it, ignoring a need cost a guest nothing, so a park with
no lavatory was indistinguishable from a good one until they left for an unrelated reason.

⭐ `+0x76` is confirmed **Sick** by `FUN_0020EDD8` adding the ride's sickness term to it.

### `FUN_0020EDD8`'s three globals, read

| global | image | what it is | the port had |
|---|---|---|---|
| `DAT_002eeb44` | **15** | flat happiness gain from a ride | 8, invented |
| `DAT_002eeb30` | **1212** | `sick += 1212*(intensity-30)*0x1000>>0x18` = `(i-30) * 1212/4096` = 0.2959 | 0.25, invented |
| `DAT_002eeb34` | **4096** | `boredom -= 4096*intensity*0x1000>>0x18`; 4096*4096 is exactly 2^24 so the shift cancels — **boredom falls by the intensity itself**, scale 1.0 | 0.5, invented |

⭐ The `0x1e` = **30** pivot in the sickness term is the same 30 this file already recorded from a
separate reading. Two routes to one number is corroboration; one route twice would not be.

⚠ **READ FROM THE IMAGE**, which is the weaker reading — this port's own rule is that an image is
not authority for a runtime global. Unlike the classic case these are non-zero, sit in an ordered
run (95/85/90, then 85/90/95 following), and land exactly where thresholds belong on a 0..100
need. A savestate would settle it. `RideIntensity` remains a port invention and probably should
not be a global at all: it is the ride's own `UsageInfo.ExcitementLevel`.

### ⭐⭐ And the mess sink, which this file recorded as NOT FOUND

`FUN_0020EDD8` lines 133-137, in the same function as the ride effects:

```c
if (guest[0x79] < 0x3d) guest[0x79] = 0;                      // under 61: nothing to leave
else { FUN_00130948(facility, (guest[0x79] - 0x3c) * 2 / 3);  // (toilet-60)*2/3 -> THE FACILITY
       guest[0x79] = 0; }
```

So the amount was already decoded and **the destination is `FUN_00130948(facility, soil)`** — the
mess is handed to the building, exactly as the port's accumulator guessed, through a call that
had simply never been looked for. ⚠ `FUN_00130948` itself is NOT yet read: what the facility does
with it (a dirtiness counter, a handyman job) is still open, and it is a long way from the visitor
code, so it is probably the building/scenery layer rather than the guest layer.

⚠ Worth noting the shape: relief lives in the SAME function as the ride effects, branching on the
kind of facility — which is the console doing what `ParkVisitors.Serve` now does.

### ⭐⭐ The mess sink, read — and it runs the OTHER WAY (2026-09-24)

`FUN_00130948(facility, amount)`, the call the relief path makes, is four lines:

```c
facility[0xb4] -= amount;   // clamped at 0
```

⚠⚠ **It SUBTRACTS.** The earlier note here said the mess is "handed to the building", which is the
right call and the wrong direction: the console keeps a **condition** on the facility that use
depletes toward a floor of zero, rather than a mess that accumulates upward. Arithmetically the
same; structurally the opposite, and the difference shows the moment you want to clean one.

Every site of `+0xb4` accounts for itself:

| site | what it does |
|---|---|
| `FUN_001302d8` | constructor: `+0xb4 = 100`, `+0xb0/a8/ac = 0` |
| `FUN_00130678` | placement: `+0xb4` from the template's byte `0xe`, clamped 0..100 |
| `FUN_00130948` | **use wears it down**, floored at 0 — sole caller `0x20ef48`, the relief path |
| `FUN_00130978` | `+0xb4 = 100` **and stamps a time at `+0xa8`** — so this is a SERVICING, not an init |

⭐ That last one is the shape of the whole feature: a toilet gets dirty in proportion to how
desperate its customer was, and something comes along and resets it. The cleaner's hook already
exists and is named.

⚠ **NOTHING READS IT BACK**, as far as a census of `lb`/`lbu` at `+0xb4` can see — every site is
the constructor, the placer, the depletion or the reset. So a filthy lavatory currently costs
nobody anything on the console either, as far as this reading goes. ⚠ A getter reached through a
vtable would be invisible to that census, so this is **not found**, not **not there** — the same
distinction that cost this repo a wrong "paths are free" finding once already.

Ported as `ParkRide.Condition` (0..100, `Wear`, `Service`), with `ParkVisitors.Soil` kept as a
`100 - Condition` VIEW because two audits read it. ⭐ Keeping the console's direction means a
cleaner is `= 100` rather than `-= something`.

### ⚠⚠ A ride only turns a stomach above 55 — the port was CURING sickness (2026-09-24)

astraclaw read the consumer rather than the constant and found the gate at `0x20F248`:

```c
if (0x37 < lVar9) {                       // ride value > 55
    guest[0x76] += DAT_002eeb30 * (iVar10 - 0x1e) * 0x1000 >> 0x18;
}                                          // ...and NO other arm
```

There is no lower branch. Below 56 sickness is left **exactly alone**. This file previously said
"sickness is measured against 30, so an intensity below that settles the stomach" — the 30 is
real, but it is the pivot INSIDE the term, not a threshold around it, and the port was therefore
**subtracting** sickness on every gentle ride, its own default of 45 included. ⭐ Knowing a
constant is not knowing what it does; only the consumer says that.

### ⭐⭐ And happiness is BANDED BY TASTE, not flat

The same function, just above the gate:

```
lVar9/iVar10 = the RIDE's value        iVar5 = FUN_0020C078(guest) = what this guest LIKES
uVar7        = |iVar5 - iVar10|        the mismatch
  |diff| <= 20  -> happiness += DAT_002eeb44 = 15
  |diff| <  51  -> happiness += DAT_002eeb40 = 10
  otherwise     -> happiness += DAT_002eeb3c = 5
```

So the flat **15** recorded earlier today is only the BEST band. ⚠ A number read correctly can
still be the wrong number, if you read it out of one arm of three.

`FUN_0020C078` is four instructions: `*(u16*)(&DAT_002eebd8 + guest[0x7d] * 8)`. So `+0x7d` is a
**personality index** and the preference is a per-type constant. The table is 8 entries of stride
8 and its first u16s are read off the image:

| idx | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---|---|---|---|---|---|---|---|
| prefers | 90 | 30 | 50 | 75 | 100 | 45 | 60 | 70 |
| (2nd u16) | 15 | 20 | 25 | 18 | 16 | 14 | 20 | 10 |

⭐⭐ **RESOLVED, AND AGAINST ME: THE TABLE IS LONGER THAN EIGHT.** The purchase path's
shop-kind arm 2 sets `guest[0x7d] = 8` outright at `0x20e6b8`, and five `sb` writes to `+0x7d`
exist in the guest range. So the row I dismissed as "other data" is one the game can select. The
original note below stands as written because the reasoning in it is the point.

⚠⚠ **EIGHT RECORDS ARE VERIFIED; THE LENGTH IS NOT.** This first said that index 10 holding two
`1.0f` proved the table ends at eight. It does not: the getter has **no bounds check**, so a
larger `+0x7d` would read that float data as an intensity. What the neighbouring bytes look like
is a fact about LAYOUT, not about extent — the length can only come from whatever constrains
`+0x7d`, which is the very thing that is unread. astraclaw caught it, and it is the exact mirror
of the "not found is not not-there" correction made one section earlier, in the other direction.

⚠ **WHAT SETS `+0x7d` IS NOT READ.** `FUN_0020BCD0` spawns every other need byte and never touches
it, so the personality is assigned somewhere else. The port picks one of the eight uniformly:
**the values are the game's, the choice is not**, and the second u16 has no consumer yet either.

### ⚠⚠ The `.sam` is the authored source; the compiled DBA is what ships — and they disagree

astraclaw found it on sideshows (`InitChanceOfLoosing` is a uniform **75** across all 23 .sam,
while ARC2X3's compiled record says win percentage **33** in all three regional DBAs). That is a
field this port never used, so the question was whether the same split touches the shop effects
tonight's purchase path DOES use. Measured, rather than assumed:

**8 of 9 distinct shop effect tuples appear verbatim in the compiled data. One does not.**

| world | file | id | `.sam` HappinessEffect |
|---|---|---|---|
| FANTASY | `fatfairy` | 4205 | 10 |
| HALLOW | `vampshop` | 2205 | **15** |
| JUNGLE | `Balloon` | 1209 | **15** |
| SPACE | `droid` | 3202 | **15** |

Every compiled row at price 45 / cost 30 with that profile reads **10**, in `arsdb`, `arsusdb` and
`arsjapdb` alike. So three worlds' authored text claims 15 and the shipped data says 10.

⚠ `ParkVisitors.Serve` passes the `.sam` value, so balloon-type shops currently pay 15 where the
console pays 10. Small in itself; the POINT is that "the data files are ground truth" — the
premise most of today's work rests on — is true of what was AUTHORED and not always of what RUNS.

⭐ **Agreement has to be checked per field.** One file matching proves one field in one file.
`AssetResourceDatabase` already decodes the compiled shop block (`HungerReduction`,
`ThirstReduction`, `HappinessEffect`, `VomitIncrease`); joining it to `RideDefinition` is the fix
and is NOT done — flagged in the accessor rather than left to be discovered by someone wondering
why a balloon costs the wrong happiness.

⚠ Method note: the first pass of this comparison hard-coded the 17 DBA rows that survived a
`head -30` and reported two mismatches. Both were artefacts of the truncated set. Re-run against
all 96 shop rows across the three regional files, exactly one survives.

### ⭐⭐ The authored→compiled join, done on IDENTITY (2026-09-24)

astraclaw handed me the runtime wiring with one constraint that decided the whole design: **join
by verified identity, not by matching effect tuples.** A value join could only ever match rows
that already agree, and would silently drop exactly the disagreements the join exists to surface.

The identity is already established in `findings/advisor.md` from a traced consumer: DBA payload
`+4` is a localised asset-name row, and every payload's row resolves to a
`STR_GRAPHICS_<WORLD>_<PATH>` key — which is what `TextDatabase.GraphicsKey` builds from a wad
path. `CompiledAssets` maps those to entries, **first match wins** because `0x10f248` does (the
directory carries duplicate `FFFFFFFF` keys).

Result, per world: **8 of 8 shops joined**, 273 identities resolved, and the disagreements fall
out rather than being hunted:

| world | shop | `.sam` | compiled |
|---|---|---|---|
| JUNGLE | `Balloon` | 15 | **10** |
| HALLOW | `vampshop` | 15 | **10** |
| SPACE | `droid` | 15 | **10** |
| SPACE | `burger` | 5 | **10** |
| FANTASY | — | — | (none disagree) |

⭐ **The method is corroborated by a different one.** astraclaw looked the same shops up by name
and key — Balloon 239, VampShop 153, FatFairy 66, Droid 388, all happiness 10 across EUR/USA/JAP.
And Droid settles why identity beats profile: its compiled price/cost is **50/35** where the other
three are 45/30, so a tuple match would have mis-grouped it. Two methods, one answer.

⚠⚠ **10 IS A BASE, NOT THE PAYOUT.** `0x20E450` scales the compiled happiness by the **shop's
quality** before adding it, and quality is not modelled here at all — so a guest currently
receives the unscaled base. Recorded at the line that spends it (`ParkVisitors.Serve`) rather than
only in a findings file. astraclaw read that consumer.

⚠ `LitterEffect` has no counterpart in the decoded compiled block, so it stays authored-only —
and is still applied nowhere. Region is passed in, never chosen: `arsdb`/`arsjapdb` are
byte-identical and `arsusdb` differs in eight bytes across four Ice Cream records.

### ⚠⚠ The join shipped DEAD, and my own checks could not see it (2026-09-24)

astraclaw: `IndexRides` called `AttachCompiledRecords()` **between** `new RideCatalogue()` and
`AddWad(...)`, so the join walked an empty list and attached nothing. The checks stayed green
because they exercised `CompiledAssets` directly instead of the wiring.

⭐ **That is the exact failure this port spent the night deleting** — correct code, passing tests,
never reached — committed by the person who had spent the night deleting it, one commit after
writing a tool to find it. Nothing in the audit could catch it, because the audit and the viewer
were running *different implementations of the same loop*.

**Three fixes, of which only the first is the bug:**

1. Attach after populate.
2. **One implementation.** The loop moved into `CompiledAssets.Attach`, which the viewer and the
   audit both call. A check that tests a different implementation than the one that ships is not
   a check of anything.
3. **An empty input must say so.** `0 joined, 0 missed` is what an empty catalogue looks like and
   it reads as success; it now reports `NOTHING TO JOIN`, and a control asserts that.

⚠ **And running through the real path immediately found a SECOND bug.** `AddWad` stamps whatever
`wadPath` it is handed: the viewer passes `/DATA/JUNGLE.WAD` and gets
`/DATA/JUNGLE.WAD/Shops/...`, while a bare world name yields `JUNGLE/Shops/...`. The splitter
looked only for an element ending `.WAD` and silently missed every definition of the second
shape — `0 of 8 joined`. Both shapes are handled now, and the audit passes the path the viewer
passes rather than a tidied one.

⚠ Method note: the run that "confirmed" the fix printed `FAILs: 0` from a log containing a
**compile error** — the grep counted failures in a file with no audit output at all. A green from
a run that never happened. The loop now refuses a zero-check run explicitly.


### ⭐⭐ The purchase path switches on SHOP KIND, and the arms are mirrored (2026-09-24)

Chasing astraclaw's quality note into `FUN_0020E1A0` turned up more than the scale. The whole
effect block is a `switch` on `FUN_001D1D08(shop)` — a shop-kind getter, ⚠ **not verified to be
`UsageInfo.ShopType`** — and the arms are not variations on one behaviour:

| arm | what it does to the guest |
|---|---|
| 0, 4, 5 | `+0x77 -= A`; `+0x7a += A`; `+0x79 += A`; `+0x76 += A`; `+0x75 += (C*(q1-q2/15))/100`; `+0x74 += …` |
| 1 | **the mirror**: `+0x7a -= A`; `+0x77 += A`; the rest as above |
| 2 | `+0x7d = 8`; `+0x34 |= 0x80`; calls `FUN_0020BC70`; happiness `+= (C*q)/100` |
| 3, 6 | happiness only, `+= (C*q)/100` — no `/15` term |
| 7 | `+0x7a += n/15` |

⭐ **So `+0x77` and `+0x7a` are hunger and thirst, and the SHOP KIND decides which one it fills.**
The port's `+0x77 = Hunger` holds for the default group (0/4/5); arm 1 works the other way.

⚠⚠ **CORRECTION, WITHIN THE HOUR: "raising the other by the same amount" WAS WRONG.** I read the
decompiler's reused `cVar3` as one value and collapsed four distinct getters into it. astraclaw
warned about exactly this before I had checked. Tracked assignment by assignment, arm 1 is:

```
cVar3 = vtable 0x1e4   ->  +0x7a -= cVar3      getter A
cVar3 = vtable 0x1e4   ->  +0x79 += cVar3      SAME getter A  -- the toilet DOES mirror it
cVar3 = FUN_001D1CC8   ->  +0x76 += cVar3      getter B
        FUN_001D1B88 / 1F50 / 1FB8 -> +0x75 += (C*(q1-q2/15))/100
cVar3 = vtable 0x1ec   ->  +0x77 += cVar3      getter C -- DIFFERENT from A
        rand(25)       ->  +0x74 += ...        litter, randomised
```

⭐ So the opposite need is raised by **its own getter**, not by the reduction — which means the
port's two-amount `Buy(hungerReduction, thirstReduction, …)` is closer to the console's shape
than I claimed an hour ago, and the "hunger reduction is ALSO added to the toilet" decode this
file has carried all along is **confirmed** by A being re-read for `+0x79`.

⚠ What IS still unverified: which getter supplies which authored field, and the **sign** — the
console ADDS getter C to the opposite need where `Buy` SUBTRACTS a thirst reduction from it. That
is a real difference in direction and it is not resolved here. `+0x74` litter is also still
unapplied. ⚠⚠ No purchase expectation should be pinned until the getters are identified;
astraclaw is doing that independently and is holding theirs off main.

⚠ The happiness scale is `(effect * (q1 - q2/15)) / 100` with `q1 = FUN_001D1F50(shop)` and
`q2 = FUN_001D1FB8(shop)`; neither getter is read yet, so "quality" is still a name for two
numbers rather than a decoded quantity.

### ⭐⭐ Two silent purchase bugs, found by verifying the getters (2026-09-24)

astraclaw verified the shop slots — **`+1E4` is the compiled THIRST getter, `+1EC` the HUNGER
one** — and that ice cream raises thirst by its own 5 rather than by any function of its hunger
15. Reading the default arm against that mapping makes the whole block plain:

```
+0x77 -= hunger    (0x1ec)     Hunger down
+0x79 += hunger    (0x1ec)     Toilet up by the SAME amount, same getter re-read
+0x76 += vomit     (1d1cc8)    Sick up
+0x75 += (happy * (q1 - q2/15)) / 100
+0x7a += thirst    (0x1e4)     THIRST UP, by the shop's own thirst value
+0x74 += base + rand(25)       Litter
```

Two things the port had wrong, both silent:

⚠⚠ **A purchase charged the bare price.** `0x20E1A0` does `cash += price * -10`, and cash is kept
in the same ×10 units the spawn seeds it in (`(rand(300)+200) * 10`). Every purchase undercharged
by an order of magnitude — a park where nothing costs anything looks exactly like a park that is
balanced.

⚠⚠ **Eating QUENCHED thirst.** The console ADDS the shop's thirst value; the port subtracted it.
So a burger was slaking a thirst the game intends to create, which quietly removes the reason
drink shops exist. ⭐ The bug is invisible in isolation: both directions "work", and only the
consumer says which.

⭐ The toilet rise is now traced rather than asserted — the default arm re-reads the same `+0x1ec`
getter for `+0x79` that it subtracted from `+0x77`, which is why "the hunger reduction is ALSO
added to the toilet" has been right in this file all along.

⚠ `+0x74` litter is still NOT applied. The base is not the `.sam`'s `LitterEffect` (50 for a
burger against an observed 30), so its source is unidentified, and inventing one is how a wrong
number acquires a confident comment.

### ⭐ Litter's base, the quality defaults, and the affordability guard (2026-09-24)

astraclaw closed all three of the gaps left open above.

**Litter's base is read, and it is not the authored field.** `DAT_002EEB60` is **30** in the
image (confirmed independently here), loaded at `0x20E504` and added to `rand(25)`. ⚠ The `.sam`
carries `LitterEffect 50` for a burger, so the authored number is *not* the one that runs — the
third time tonight that has been true, after the balloon's happiness and the sideshow win
percentage. Range is therefore 30..54 per purchase, and the check asserts the RANGE rather than
this seed's draw.

**Quality defaults explain why the compiled base IS the payout.** Shop setup writes **q1 = 100,
q2 = 0** at `0x1D1870/7C`, so `(q1 - q2/15) / 100` is exactly 1 and a new shop pays its compiled
happiness unscaled. ⚠ That is a DEFAULT, not a constant: whatever moves q1 and q2 later is
unread, so this port has **no** shop quality rather than a wrong one — which is the honest state
to be in.

**⚠⚠ And the affordability guard belongs WITH the tenfold debit.** `0x20E1A0` gates the entire
block on `price * 10 <= cash`, so a guest who cannot pay does not eat either. Shipping the ×10
debit without the guard would have let cash go negative and fed the park for free — which reads
as generosity rather than as a missing check. They are one change, not two, and astraclaw said so
before it could ship half-done.

⭐ `Buy` now returns whether the purchase happened, and `Serve` only counts one that did: a
counter that ticks on a refused trade reports commerce that never occurred.


### ⚠ Costume shops: one third of a feature, said out loud (2026-09-24)

The purchase path's costume arm (compiled `Product` 2) does four things. The port models one:

| the console | ported? |
|---|---|
| `guest[0x7d] = 8` — the personality index, so they leave wanting different rides | ✅ as `CostumePreference = 14`, row 8 of `DAT_002EEBD8` |
| `+0x75 += (effect * q1) / 100` — happiness, scaled by **q1 alone**, not the food arm's `(q1 - q2/15)` | ✅ unscaled, which is correct while q1 defaults to 100 |
| `+0x34 \|= 0x80` — a flag on the guest | ❌ unread |
| `FUN_0020BC70(guest)` — actor-side, presumably the visible change of dress | ❌ |
| `FUN_001073C0(…, 6, 1)` — a global call, kind unidentified | ❌ |

⭐ So a guest who buys a costume here leaves **wanting different rides and looking exactly as they
did**. That is a third of the feature, and calling it "costumes work" would be the same mistake
as every other one this file records: a thing that runs, passes, and is not what the console does.
astraclaw asked for the limitation to be explicit in the code rather than implied by a commit
message nobody reads twice.

⭐ Independently confirmed by astraclaw at instruction level: product 7's table entry reaches
`0x20E36C`, adds `q2/15`, and falls through into the complete food arm at `0x20E3AC`; row 8's
intensity is exactly **14**.


## ⭐⭐ A shop keeps a RATING, not takings — and quality has addresses now (2026-09-24)

Chasing "the guest pays 10x price and the park receives nothing" into the last unread call of the
purchase path, `FUN_001D1E68`:

```c
shop[0xb0] += amount;    // amount = (happiness gained) * 5, NOT the price
shop[0xb4] += 1;         // customers served
FUN_001073C0(global, kind, …)   // a statistic, switched by product kind
```

⚠ **It is called with `(guest[0x75] - happinessBefore) * 5`** — so `+0xb0` accumulates the
SATISFACTION a shop has delivered and `+0xb4` counts who it delivered to. `0xb0 / 0xb4` is an
average rating. **This is not the park's till**, and the search for where a guest's money goes is
still open: the debit at `0x20E1A0` is followed immediately by the effect switch, with no credit
between.

### ⚠⚠ And `+0xb4` means TWO different things

On a shop it is that unbounded customer count. On a lavatory it is the 0..100 condition this file
decoded earlier — clamped, depleted by use, reset to 100 by `FUN_00130978`. **Two classes, one
offset, two meanings.** `ParkRide.Condition` is therefore meaningful only where `ProvidesRelief`
holds; its note previously said "the console's own facility record" without that qualifier, which
would have invited someone to read a shop's condition and get a customer count.

⭐ This is the same trap as the sideshow `+A8/+CC/+CE` that astraclaw corrected earlier: an offset
is not a meaning until you know which class you are standing in.

### ⭐ Quality: q1 is `+0xba`, q2 is `+0xac`

Both getters are one instruction each — `FUN_001D1F50` returns `*(u16*)(shop + 0xba)`,
`FUN_001D1FB8` returns `*(u32*)(shop + 0xac)`. So the happiness scale `(q1 - q2/15) / 100` reads
those two fields, and setup writes 100 and 0 into them (astraclaw, `0x1D1870/7C`).

⚠ I had guessed quality would read the `0xb0 / 0xb4` rating, since a satisfaction average is
exactly what "quality" ought to mean. **It does not.** Read before believing: the guess was
plausible, tidy, and wrong, and one decompile of an eight-byte function settled it.

⚠ What MOVES `+0xba` and `+0xac` after setup is still unread, so the port still has no shop
quality rather than a wrong one.

### ⭐⭐ `FUN_0020FB88` read to the END — and the first read stopped a third of the way in (2026-09-24)

Master said "look at the damn code", so the whole function got read rather than the part that
answered the question already being asked. It is five phases, each staggered per guest by
`tick % N == guest[0x14] % N`:

| phase | period | what it does |
|---|---|---|
| zones | 8 | `FUN_0014D0A0(pos)` → flag mask; bit 0 `happy += 6`, bit 2 costs happiness and adds sickness |
| shelter | 8 | if mask bit 1 and `guest[0x6c] < now` and stack depth < 2: push activity, walk to nearest |
| litter | 64 | every litter within **Manhattan 2**: `happy -= 3`, and `+3` sick if `node[9]` |
| bars | 64 | **five** `-1` docks |
| appetite | 50 / 40 | `guest[0x77] += rand(2)` / `guest[0x7a] += rand(2)` |
| thoughts | 4 (+128) | the bubble chain |

**⚠⚠ THE MOOD DRAIN HAS FIVE TESTS, AND THIS FILE RECORDED THREE.** `DAT_002eeb58` (95) gates
`+0x77` and `DAT_002eeb5c` (85) gates `+0x7a` — hunger and thirst, the two needs the whole
feature is about. Three consecutive identical blocks looked like the whole run; they were the
whole run of what had been scrolled to.

### ⭐⭐ `+0x7B` IS BOREDOM. `+0x78` IS NOT — and this file said the opposite all day

The thought chain calls `FUN_0020F888(guest, guest[0x7B], 0x40, 2, allowed)` and on a hit writes
bubble id **4**, which is `tbbored` in `FUN_00216028`'s own table. **The engine points the bored
picture at `+0x7B`.** Three behaviours agree: the bubble; `FUN_0020C930` only *asks* whether to go
home while `+0x7B` < 99 and leaves unconditionally at 99; and finishing **any** facility use takes
`rand(20)` off it (`FUN_0020EDD8`, before the per-kind switch).

⚠⚠ So the commit "boredom rises over time" put a timed rise on **`+0x78`**, justified by that
byte's 95 bar — and `+0x78` is not boredom, and **nothing in `FUN_0020FB88` raises it on a clock**.
Retracted; only queueing (`+5`, `FUN_0020C6A8`) is a found riser. `+0x78` keeps its offset for a
name: a meter queueing raises and an exciting ride lowers *by its own intensity*. "Craving
excitement" fits and is not read.

⭐ The lesson is the one already in this file one level up: **an offset is not a meaning until
something names it.** Knowing `+0x78`'s arithmetic did not make it boredom, and a threshold on it
was not evidence about boredom.

### ⭐⭐ The appetite clock, read: 50 and 40 ticks of `rand(2)`

```c
if (now % DAT_002eeb6c == guest[0x14] % DAT_002eeb6c) guest[0x77] += rand(2);  // 50, hunger
if (now % DAT_002eeb70 == guest[0x14] % DAT_002eeb70) guest[0x7a] += rand(2);  // 40, thirst
```

Two invented rates replaced by two read periods. ⚠ Seconds-per-tick is still invented — one
number instead of five — and **the ratio (thirst 1.25× hunger) survives whatever it turns out to
be**, which is what the audit asserts.

**Identity, settled from the product switch** (`FUN_001D1D08` selects, and `case 1` is `Drink`):
that arm does `+0x7a -= ThirstEffect` and `+0x79 += ThirstEffect`, so **`+0x7a` is thirst** and
the food arm's `+0x77 -= …` makes **`+0x77` hunger**. ⭐ Drinking fills a bladder by exactly what
it empties a thirst.

### ⭐⭐ The purchase is a WANT SCORE against the price, not just affordability

```c
score = (base*125/100) * ((thirst*ThirstEff + hunger*HungerEff + 100 - sick*VomitEff
                          + (100-happiness)*HappyEff) / 100) / 100 * (happiness+100) / 100;
if (shop[0xb8] < score && shop[0xb8]*10 <= guest[0x60]) { ...buy... }
```

⭐ So a guest refuses something they can afford but do not want, and the `×10` on cash this port
already found is right there. ⚠ NOT YET PORTED — recorded here so it is not re-derived.

### ⭐⭐ THE FILTHY-LAVATORY PENALTY EXISTS, and this file called it "not found"

`FUN_0020EDD8`'s lavatory arm, after wearing the facility down, reads the condition **back**:

```c
sick -= 0x28;                              // 40 — the only cure for sickness found anywhere
if (FUN_00130938(facility) < 0x32) {       // return facility[0xb4]  —  below 50
    bubble = 10 (tbangry);  happiness -= 10;  sick += 10;
}
```

⚠⚠ The earlier census of `lb`/`lbu` at `+0xB4` could not see it **because the read is inside a
one-line accessor**. The note at the time said exactly that — *"a getter reached through a vtable
would be invisible to that census, so this is **not found**, not **not there**"* — and the hedge
turned out to be load-bearing. ⭐ A negative search needs a control it MUST hit; that one had none.

⭐ It also gives `Condition` its first consumer: `Wear` had no reader, so a cleaner was a feature
with no effect. Ported with the console's ordering — wear, then read — so the guest who made the
mess can be the one disgusted by it.

### The thought chain, and a bubble budget of 25

`DAT_002E28D0` is a **global count of bubbles on screen, capped at 0x19 = 25**, with bit 3 of
`guest[0x34]` meaning "I hold one". Every bubble-setting site takes a slot the same way. The chain
(first hit wins, `FUN_0020F888` fires at ≥ 91, `FUN_0020F968` at < 10):

| test | bubble | face |
|---|---|---|
| toilet `+0x79` ≥ 91 | 7 Toilet | 0 |
| sick `+0x76` ≥ 91 | 5 Sick | 2 |
| happiness `+0x75` ≥ 91 | 1 | 1 |
| happiness < 10 | 3 Sad | 0 |
| boredom `+0x7B` ≥ 91 | 4 Bored | 0 |
| happiness 26..74, 1-in-10 | 1 | 1 |
| happiness ≥ 81 | 0 Happy | 1 |

⭐⭐ **Bubble id 1 is the STRONGER form of id 0, not its opposite** — same field, same face id,
thresholds 91 over 81, with the sad end taken by 3. This port's enum calls id 1 `VeryUnhappy`;
the engine's own ladder says it is *very happy*. ⚠ Not renamed yet: the art has not been looked
at, and the behavioural argument and the picture should agree before the name changes.

⚠ NOT PORTED, and deliberately: `FUN_0014D0A0` walks a list of circular zones (centre shorts at
`node+8`/`node+12`, **radius²** at `node+16`, flag mask at `node+20`) hung off `DAT_003952A8+8`.
The mechanism is read; **nothing is yet known to register a zone**, so porting the effects today
would be a loop over an empty list. The litter list (`DAT_003952C0+8`) and the shelter list
(`DAT_003952BC+8`) are the same shape and the same open question.

⚠⚠ **RETRACTED THE SAME DAY, AND THE RETRACTION IS THE INTERESTING PART.** This paragraph said
`guest[0x6c]` was "a decision cooldown written on completion", that astraclaw had found "the same
mechanism from the selector side", and that this was **"two independent routes to one mechanism,
which is corroboration"**.

`FUN_0020EDD8` does write **both** `guest[0x6c] = now + rand(60) + 60` and
`guest[0x2c] = now + rand(300) + 300` as a use ends — that part holds. But astraclaw traced the
consumers: **`+0x6C` gates watching an ENTERTAINER** (activity **28** = `0x1C`, the `PoolOfEntertainers`
list at `DAT_003952BC`, which this file guessed was "shelter"), and the destination re-target gate
is **`+0x2C`**. Two timers written in one breath, and I matched mine to theirs by which was
*nearest* rather than by reading either consumer.

⭐⭐ So the "corroboration" was **two different mechanisms asserted to be one**. That is the exact
failure this repo keeps a note about — agreement is only corroboration when the two routes reach
the *same* thing — and it is worse than a plain wrong reading, because it borrows someone else's
correct work to certify a wrong one. ⚠ A timer written beside the timer you want is not the timer
you want.

⭐ The ONE genuine corroboration from this exchange: astraclaw read the native counter as
"gameticks/rendertick" behind a default **two-VBLANK** throttle, and on a PAL disc that is
**25 Hz** — which is the rate `ParkSim.TickMilliseconds` already carried from a different route.
`VisitorNeeds.SecondsPerTick` now derives from it instead of the 60 Hz it was guessed at.

### ⭐⭐ The want score, PORTED — and shop quality starts at **100**, which nearly shipped as 0

Every accessor in `0x20E1A0`'s purchase gate resolved, and each one lands on a field this port's
own compiled-record parser already names — two independent routes to one layout:

| call | reads | this port calls it |
|---|---|---|
| `FUN_001D1B08` | `record[0x2e]` × `((quality>>2) + 75 - (customers>>2))` / 100 | `BaseCostOfGoods` |
| `FUN_001D1D08` | `record[0x30]` | `Product` |
| `FUN_001D1B88` | `record[0x34]` | `HappinessEffect` |
| `FUN_001D1CC8` | `record[0x36]` | `VomitIncrease` |
| `FUN_001D1F50` / `FUN_001D1F58` | `shop[0xba]` get / set | **quality** |
| `FUN_001D1FB8` | `shop[0xac]` | **customer count**, not a second quality |

⚠ `[0x2e]` is parsed here as `BaseCostOfGoods`; what the executable does with it is scale a
**desire**, not a cost.

**⚠⚠ AND THE PORT ALMOST SHIPPED QUALITY AS 0.** The reasoning written into the code was that 0
is "the console's own arithmetic on an unset field". It is not unset: `FUN_001D16C8`, which
builds a shop from its compiled record, calls `FUN_001D1F58(shop, 100)` two lines after writing
the price. At 0 the base falls to three quarters and **no guest ever buys anything** — measured,
a guest at hunger 80 scores 25 against a price of 30.

⭐ The audit caught it as **nine failing purchase cases**, which is the only reason the reasoning
was re-examined rather than believed. A plausible argument for a default is not evidence about
the default, and "it's the natural value of an unset field" is the shape that argument takes
every time it is wrong.

**How the real value was found**, and the method is worth keeping: a census of `sb`/`sh`/`sw` at
`+0xBA` over the whole image found exactly **one** store in game code (plus one in a serializer),
**with `+0xB4` as a control that had to hit its three known sites and did**. Then a census of
encoded `jal` to that setter gave three call sites — construction, savegame restore, and one
unread. ⭐ Two censuses, each with a control, beat reading around the neighbourhood hoping to
trip over it.

⚠ **The customer term is still NOT passed.** `(customers >> 2)` is subtracted from the base, so
at 300 lifetime customers a shop's base reaches zero and it never sells again. Nothing read so
far resets it for a shop (the lavatory arm zeroes `+0xAC`; shops have no equivalent yet). A term
that only ever falls would hand this port a slow silent shop death and call it fidelity.

⚠ **The happiness payout scaling is also NOT ported**, for the same reason in reverse: the drink
arm pays `HappinessEffect * (quality - customers/15) / 100`, which at quality 100 is
`HappinessEffect` exactly — so the unscaled value this port already pays is right *today* and
becomes wrong the moment quality moves. Recorded so it is wired the day quality has a mover.

### The thought ladder and the 25-bubble budget, ported (2026-09-24)

`FUN_0020FB88`'s chain, first hit wins, `FUN_0020F888` firing at **≥ 91** and `FUN_0020F968` at
**< 10**, every `+0x40` write gated on the 128-tick refresh AND the budget:

| test | bubble | face (`FUN_0020FA78`) |
|---|---|---|
| toilet ≥ 91 | 7 Toilet | 0 |
| sick ≥ 91 | 5 Sick | 2 |
| happiness ≥ 91 | 1 | 1 |
| happiness < 10 | 3 Sad | 0 |
| happiness ≥ 81 | 0 Happy | 1 |
| *(below 81)* boredom ≥ 91 | 4 Bored | 0 |
| *(below 81)* happiness 26..74, 1-in-10 | 1 | 1 |
| otherwise | none — **the slot is given back** | — |

⭐ This supplies the rules `Decide` (`FUN_0020C930`) could not: **Bored and Sad had no source at
all**, and the happy end had a guessed 75 where the console uses 81 and 91.

⭐⭐ `DAT_002E28D0` caps bubbles at **25 park-wide**, with bit 3 of `guest[0x34]` marking a holder.
The per-guest stagger (`tick & 0x7f == guest & 0x7f`) is load-bearing: without it the first 25
guests would hold every slot forever.

⚠ NOT PORTED: `FUN_0020FA78(guest, 0|1|2)` is a FACE, unconditional, separate from the bubble.
This port has no guest expression to hang it on.

### ⚠⚠ Two defects in the CHECKS, both of which read as passes

Worth recording because both are shapes that recur:

1. **`VisitorWants` is a struct and the arrange helper took `Action<VisitorWants>`** — mutating a
   by-value parameter. No case arranged anything. Fixed with `Func<VisitorWants, VisitorWants>`.
2. ⭐⭐ **`Thought.Happy` is enum value 0**, so "the ladder never wrote anything" and "the guest is
   happy" are the SAME VALUE. Five rows went red and the one row expecting `Happy` went green —
   **and the green one was the vacuous one**. Every case now stamps a sentinel (`Thought.Litter`)
   first, with a control asserting the sentinel survives a guest the ladder declines to bubble.

⚠ And a third, in an assertion: the retracted `>= 20` boredom check was first replaced with
`== 5`, **which was the JUNGLE number rather than an invariant** — HALLOW and FANTASY read 0,
because their fixture geometry never queues. Now asserts what is actually invariant (only the
queue moves `+0x78`, in steps of 5) and leaves "it moves at all" to the case that owns it.

### ⭐⭐ THE PARK'S TILL, FOUND — and this file said the search was "still open" (2026-09-24)

The earlier note was right that `0x20E1A0` debits the guest with no credit beside it. The credit is
one level down, in the shop's own till, and it books a sale in three places:

```c
FUN_001D18E8(shop):
    margin = shop[0xb8] - FUN_001D1B08(shop);        // price - cost of goods
    if (margin < 1)  FUN_00100698(park, -margin * 10);        // a LOSS: debit the park
    else             FUN_001007D8(park, kind, margin * 10);   // credit, FILED BY KIND
    shop[0xbc] += price;    shop[0xc0] += margin;
```

**The park object** (`FUN_001005D8` returns the singleton, `0x12F4` bytes):

| call | what it does |
|---|---|
| `FUN_00100750(park, n)` | `park[4] += n`; income totals `+0x12D8`/`+0x12DC`; income graph `+0x2FC + (period%0x90)*4`; clears the overdrawn warning when the balance comes back positive |
| `FUN_00100698(park, n)` | **refuses** if `park[8] == 0 && park[4] < n`; else `park[4] -= n`; spend totals `+0x12E4`/`+0x12D4`; spending graph `+0xBFC + (period%0x90)*4` |
| `FUN_001007D8(park, kind, n)` | the plain credit, **then** files it in a per-kind ring and total (switches on 4 and 5) |

⭐⭐ **Why the plain credit had no shop caller**, which is what stalled the earlier search: the shop
path calls the **categorised** wrapper, not the credit itself. A `jal` census of `FUN_00100750`
finds twelve sites and none of them is a shop. The control (`FUN_00100698`, which
`findings/dba.md` already placed on the upgrade path at `0x1162FC`) hit, so the census was sound —
it was looking for the wrong function.

⭐⭐ **And `BaseCostOfGoods` does two jobs, both real.** It scales how badly a guest wants the thing
(`FUN_001D1B08` feeding the want score) *and* it is the shop's cost per sale. A field named for one
job and used for two is exactly what makes a value-based join look wrong.

⭐ The `×10` is the same `×10` the guest is charged, which is the cross-check that the two halves
were read in the same units: the park earns strictly less than the guest paid, and more than
nothing. The audit asserts that as a control rather than just asserting the number.

⚠ NOT PORTED, and said rather than silently dropped:
- **the category**, because nothing read says which facility kind is 4 and which is 5, and filing
  income under a guessed heading is worse than filing it under none. The balance is unaffected —
  the categorised wrapper credits *first*, then files.
- **the two 144-slot graphs**, because the period counter at `park[0x12BC]` has no traced advance,
  and 144 slots of zero advanced by a clock nobody found is not a feature.
- **the starting balance**, because park setup is untraced. Opens at zero; the caller sets it.

⚠ An unjoined shop has no cost of goods, so it books its takings and **no** margin and the park is
not paid — a visible zero rather than invented income.

### ⚠⚠ And the dirt penalty was a one-way ratchet (same day, master's playtest)

`DirtyLavatory` shipped without a cleaner, so three uses soiled a lavatory permanently and every
guest afterwards was angry until the park emptied. `FUN_00130978` (condition back to 100) has
**exactly one caller**, `0x1456D8` — a **staff member** finishing a clean, bumping their own
`+0x53`/`+0x54` and then clearing their goal stack exactly as a guest's ride exit does.

⭐ Porting a punishment whose only cure is an entity the port does not have is worse than porting
neither. The stand-in reproduces the console's OUTCOME on a timer and is labelled to be deleted
when staff arrive, with an off switch and a check whose control proves the wear is real.

### Guests idle instead of standing in bind pose (2026-09-24)

`Viewer.Gait` played slot 1 when walking and `UseRecord(null)` otherwise, so a standing guest was
the **bind pose** — arms out, dead still. This file's own note had said *"slot 2's six records are
idles"* since the walk landed; the code never used it. ⭐ A fact recorded and not wired is the
same shape as the dead-port problem `tools/dead_port_audit.py` exists to catch, one level up: the
knowledge was in a comment instead of a call.

Now one record per state, with the record's **own** duration for the loop (an idle is 16 or 40
frames against the walk's 16, and looping one at the other's length either cuts it short or holds
its last pose), and the clock restarts on a change of record rather than carrying the walk's
frame into the idle. A rig whose file has no record with tracks for a state still falls back to
bind and says which rig and which slot.

⚠ **WHICH idle is a port choice.** The guest carries a 5-bit state at `guest[0x38] & 0x1f` —
censused at **4, 11, 12, 13, 14**, with 13 set at spawn (`FUN_00211A00`) and on every facility
exit (`FUN_0020EDD8`), and 11 set by the walking path (`FUN_0020D628`). Those values run **past
the end of the slot table**, so the field is a STATE and something maps state to record. That
table is not found, so the viewer spreads the six variants across guests by id and says so.

⚠ The check covers SELECTION, not playback: it resolves each kid's idle through the viewer's own
Shared-record rule, with the walk as a control that the resolver works at all. Whether the pose
visibly moves still needs a render.

### ⭐⭐ The guest's idle STATE machine, found — `FUN_002106E8`

A census of `andi rt, rs, 0x1f` preceded by a load of `+0x38` finds **two** sites in the whole
image, both in one function:

```c
FUN_002106E8(guest):
    if (guest[0x2c] + 0x78 < now) { if ((guest[0x38] & 0x1f) == 0xb) goto pick; }   // 120 ticks
    else if ((guest[0x38] & 0x1f) == 0xb) return;                                    // still waiting
    if (FUN_001448E0(100) > 9) return;                                               // 1 in 10
pick:
    guest[0x38] = (guest[0x38] & ~0x1f) | (DAT_002EEC18[FUN_001448E0(4) * 4] & 0x1f);
```

⭐ **Four idle states: `14, 5, 6, 13`.** The count is proved by `FUN_001448E0(4)` in the CODE; the
data agrees (entry 4 is `0x3F800000`, a float 1.0 — plainly something else) but that is only
corroboration. ⚠ Adjacency has bounded a table wrongly in this repo before; the code's bound is
what settles it.

⭐ **State 11 (`0xB`) is the resting/walking default** — set at spawn (`FUN_0020BCD0`) and by the
walking path (`FUN_0020D628`) — and while a guest is in it the picker waits out the **120-tick**
timer (4.8 s at the park's 25 Hz) before choosing a fidget. Once fidgeting, each call has a
1-in-10 chance to choose again.

**Ported:** the count (four) and the interval (120 ticks).
**Not ported, and this is the interesting refusal:** the **1-in-10 re-roll**, because it fires per
call of a function whose cadence has not been read. A 10% roll on the wrong clock is a guest
flickering between poses every few frames — ⭐ an invented cadence is the one thing that could
make this look *worse* than the frozen pose it replaces, so the read interval carries it instead.

⚠ **STILL UNREAD: state → record.** The state values in use are `4, 5, 6, 11, 12, 13, 14` and the
animation slot table runs 0..11, so `+0x38` is not a slot and something maps it. Until that turns
up the viewer cycles the first four of the six slot-2 variants and says so rather than claiming a
correspondence — 5 and 6 being valid slot numbers is exactly the coincidence that would make a
wrong mapping look right.

### ⭐⭐ The money UI: the units are READ, and `+0xAC` was not what this file said (2026-09-24)

**The display divides by ten**, and two independent routes say so:

1. The shop management screen (`0x1D6D28`) builds its price control with **min 1, max 500** and
   seeds it from `shop[0xb8]` **unscaled** — so `shop[0xb8]` is the number the PLAYER sees. The
   purchase path charges `price * 10` and the till credits `margin * 10` into `park[4]`, so the
   balance is in **tenths of the player's unit**.
2. The statistics dispatcher `FUN_0010DE38` case `0x30` graphs the balance as `balance / 1000`
   clamped to ±30000 — a finance graph in **thousands of the displayed unit**.

⚠ **NO CURRENCY SYMBOL EXISTS TO COPY.** Every `%d` format string in the image and every `STR_`
key on the disc was searched: the only money text is advisor messages (`STR_ADVMES_CASH_LOW`).
No HUD label, no format string, no symbol — which fits a console HUD drawing digits as sprites.
An invented £ or $ would be the only untraced thing on screen, so the readout is the number.

⭐ Every caller of the balance getter `FUN_00100688` (16 of them) is an affordability test or the
overdraft warning; none formats it. That is why the search for "the HUD readout" kept landing on
`balance - cost*10 < 0`.

⭐ `FUN_00100470`, the park constructor, sets **`park[8] = 1`** — the very flag the debit tests —
so a park nothing has loaded into **spends freely**. `ParkFinances.Unlimited` defaults to that
rather than to a cautious `false` that nothing in the executable asks for. It never writes
`park[4]`, so the opening balance comes with the scenario: still untraced.

### ⚠⚠ `+0xAC` IS A PLAYER SLIDER, NOT A CUSTOMER COUNT — and that reversed a refusal

This file said `+0xAC` was "the shop's running customer count" and the want score therefore
**left its term out**, on the reasoning that a term which only ever falls would kill every shop by
its 300th sale. Both halves were wrong:

```c
FUN_001D1FC0(shop, v):  shop[0xac] = v;          // a plain SETTER
0x1D7070 (shop screen): shop[0xb8]      = ui[0xd2c];     // price, clamped 1..500
                        FUN_001D1F58(shop, ui[0xa82]);   // quality
                        FUN_001D1FC0(shop, ui[0xbba]);   // this, clamped 0..100
```

⭐ **Three controls on one screen**, all the player's. A counter nobody increments and the player
types into is not a counter. The real customer count is `+0xB4`: `FUN_001D1E00` returns
`shop[0xb0] / shop[0xb4]` clamped to 100, the average-satisfaction rating already decoded here.

⭐⭐ The mistake was **carrying a meaning ACROSS OFFSETS by analogy** — `+0xB4` really is a
customer count, so `+0xAC` "must be one too". One line of the setter settles what an hour of
inference had backwards, which is this repo's own rule about reading the parser rather than the
bytes, applied to a field instead of a file.

⭐ So the term is now ported, and the same expression is **also the cost of goods**: turning the
slider up makes a sale cheaper to supply *and* less attractive to buy, while quality does the
opposite. That is the whole shop economy, and half of it was missing.

### ⭐⭐ A shop rings a till — and the control caught a wrong reading first (2026-09-24)

Master: *"make sure shops are playing a sound when a purchase is made (follow the real game)."*

`FUN_0020E1A0` ends with `FUN_00111428(audio, 7, 0xD0, &guestPos, guest+0x90, 0)` — the same
positional one-shot the lavatory arm uses with `0x35`, and the mood paths with `0xCD` / `0x81`.

**`0xD0` = 208, and `GLOBAL/KIDSSFX.MAP` event 208 is `cashD2b.vag`.** A cash register. Four ids
resolved together and every clip matches its context, which is the corroboration:

| id | where it is played | clip |
|---|---|---|
| 53 | `FUN_0020EDD8` relief arm | `dooropen1.mp2` |
| **208** | **`FUN_0020E1A0` tail** | **`cashD2b.vag`** |
| 205 | very unhappy (`FUN_0020F968`) | `scared1 / cry1 / kidsad1 / kidsad2` |
| 129 | very happy (`FUN_0020FB88`) | `huh1.vag` |

⚠⚠ **THE FIRST READING WAS WRONG AND ITS CONTROL SAID SO.** `FUN_00111428`'s second argument is
**7**, and this port already maps 3..11 to the `OBJ_SOUND_*` groups, so 7 was read as the group.
Under group 7 (`GlobalStaff`) **the lavatory's own id did not resolve either** — and 53 is known
to live in the kids map. That argument selects a table *inside* `FUN_00111428`, in some other
numbering; the group these ids actually live in is **6**, found by sweeping every group.

⭐ A lookup whose control fails tells you nothing about its target. Without 53 in that sweep the
result for 208 would have read "no such event", and the honest-looking conclusion would have been
"the console plays nothing here" — the exact opposite of the truth.

⚠⚠ **AND IT IS NOT "ON A PURCHASE".** The call sits at the function's **common exit**, after the
purchase block closes, so it is reached whether the guest bought anything or walked away. The
instruction was to follow the game, so the port fires it on a completed shop **visit** and says so
rather than quietly matching the words of the request instead of its source.

⚠ `guest+0x90`, `+0x94`, `+0x98` are three effect handles the guest keeps; only the fact that the
shop call stores into `+0x90` is used here.

### ⭐⭐ All eight guest sounds, and the one-per-tag stacking bug (2026-09-24)

A `jal` census of the two effect entry points over the guest code (`0x209000..0x213000`) finds
**eight** call sites, and the `a2` immediate at each gives its id — **8 of 8 recovered**, so this
is a complete list of that range rather than a sample:

| id | site | clip in `GLOBAL/KIDSSFX.MAP` |
|---|---|---|
| 51 | `0x211710` | `flush.vag` |
| **53** | `0x20F0A0` | `dooropen1.mp2` — **the control** |
| 126 | `0x2105FC` | `yawn2a / yawn3a` |
| 129 | `0x2102D4` | `huh1.vag` |
| 204 | `0x20CFA4` | `puke3 / puke4 / sick1a` |
| 205 | `0x20FA14` | `scared1 / cry1 / kidsad1 / kidsad2` |
| 208 | `0x20EAD8` | `cashD2b.vag` |
| 307 | `0x20CA60` | `angry4 / kidsad1 / huh1` |

⭐ Every clip matches its site, which is what makes the group-6 reading evidence rather than a
lookup that happened to return something: `puke` at a sickness site, `flush` at a toilet one, a
till at the shop. Eight independent agreements.

⚠ Wired: the till, the two lavatory ones, and the mood pair, all raised from the arms that set
the corresponding bubble. **Not yet attributed**: 126, 204 and 307 — their containing functions
(`FUN_00210428`, `FUN_0020C930`, `FUN_0020C930`) are identified but the moment inside them is
not, and a sound played at the wrong moment is worse than one not played.

### ⚠⚠ A looping sound object stacked on every re-issue

Master: *"loadspeakers and bins are spamming sounds forever ... crazy ape spams a snort sound mid
cycle."* Reproduced from this repo's own cue census before changing anything — over one 60-second
JUNGLE run there are **46 `ADDOBJ` against 14 `KILLOBJ`**, and Crazy Ape's `boil000/boil002` is
re-issued at **7.1s, 20.2s, 33.2s, 46.3s and 59.4s**. Each started another looping voice on top of
the last.

⚠⚠ **AND THE FIRST FIX WAS AIMED AT THE WRONG THING.** Deduping by tag stopped voices STACKING,
which was real, but master's actual complaint survived it: *"the sound is looping"*, on bins and
loudspeakers, with no guest anywhere near them.

⭐⭐ **"`ADDOBJ` MEANS LOOP" WAS AN INFERENCE, AND `findings/sound.md` SAID SO IN ITS OWN WORDS** --
"a reading of the scripts ... not of the object list at instance `+0xb0`, which has not been
walked". The data disproves it and names itself doing so: `End.RSE` does `ADDOBJ group 9 evt 186`,
and that event resolves to ONE set holding **`WinOneShot.mp2`**. A clip the authors called a
one-shot, played endlessly because of the opcode it arrived on. Its neighbours are the same
shape -- a firework burst, a `woooosh` -- one set each. **8 such scenery events in JUNGLE alone.**

⭐ Against that, `bus.RSE`'s `ADDOBJ` resolves to **four** sets: `mk_bus_1 | nl_bus_stop |
nl_bus_idle | pullaway3b` -- an approach, an idle to sit on, a pull-away. That is what something
that genuinely loops looks like here, and **6** scenery events have it.

⭐⭐ So looping is a property of the **event**, not the instruction: an event with the
start/loop/end structure has a middle to sustain and a single-set event does not. `ADDOBJ` still
means "add an object" -- something persistent that `KILLOBJ tag` can stop.
⚠⚠ **AND THAT RULE WAS WRONG TOO -- THE THIRD IN A ROW.** Master: loudspeakers "are meant to play
a sound from their respective banks on a timer/random", and ours "just cycl[ed] at the end of each
sfx". The scenery data says why:

```
Speaker1  3 sets:  TP BEAST 1 | TP BEAST 4 | TP BEAST 7
Speaker3  3 sets:  TP CRICKETS 2 | TP FROG 1 | frog3
Staff     6 sets:  cough | crackle | newspaper | slurp | sniff | tapspoon
```

⭐⭐ **A SET IS AN ALTERNATIVE, NOT A STAGE.** Those are peers -- a bank to choose from. Treating
three sets as start/loop/end chained them: play the first, sustain the second forever, then the
third, which is exactly the cycling reported.

⭐ So every sound event is now a **weighted random pick of one set, played once**, and the
repetition belongs to the SCRIPT -- which is what a loudspeaker's timer is, and what `ADDOBJ`
plus a killable tag is for.

⚠⚠ **NOTHING SUSTAINS NOW, INCLUDING THE BUS**, whose four sets really do read as approach /
stop / idle / pull-away. That is a real loss, taken deliberately: a silent bus is a smaller wrong
than four loudspeakers screeching without end, and structure alone cannot tell them apart.
⭐ WHAT WOULD SETTLE IT is already recorded as unread in `findings/sound.md` -- the L2 record's
`+0x10` flags (0, 4, 6, 8, 0x406, 0xc06) and its `+0xC` word (3300, 4600, 5700, 2300, 1000,
3200, 4000, 5999), which look very like a repeat interval in milliseconds.

⭐⭐⭐ **AND THE FOURTH RULE IS THE FIELD ITSELF.** Master again, after the one-shot change: "now
they play 1 sound and then never again." So the repetition is not the script's either -- the
object is persistent and the SOUND SYSTEM owes the timer. That is exactly what the L2 record's
`+0x10` flags word says, and it was parsed and carried in `SoundIndex` the whole time:

```
0x0404  Speaker1..4, Staff        0x0406  PelBin, bus       -> bit 0x400: REPEAT
0x0008  Fountain, MamFount fallz2                           -> continuous
0x0006  fireworks, gulp    0x0200  WinOneShot    0x0000  woooosh, mortar, Toilet x6   -> once
```

`+0xC` is the interval in **milliseconds**: 4300 for the speakers and staff, 3200 for the bin,
4000 for the bus, 3000 for the fountain. ⭐ Every case matches what the object IS.

⚠ TWO of the checks written for this were fitted to JUNGLE and failed the moment the four-world
gate ran them: first JUNGLE's speaker event ids (236..239) asserted in every world, then "no
feature has slot 0" (SPACE has one) and "some feature has slot 1" (only JUNGLE). ⭐⭐ That is the
second and third time in one evening a number watched going by was promoted to an invariant. The
fix each time was to assert the PROPERTY -- the flag partitions the events; the scripts ask for a
slot almost nothing carries -- which holds anywhere without naming an id.

⚠ This is a **correlation over ~20 events read off a field**, not a consumer walked in the
executable -- the bit may carry more than "repeats". But it is the first of the four rules with
any data under it at all, and it is asserted both ways: the six things that should repeat carry
it, the four that should not do not.

⭐ It also retires an invention made an hour earlier: the bus is `0x0406` like the bin, so its
four sets are ALTERNATIVES on a four-second timer and not the approach/stop/idle/pull-away
sequence this file claimed to be sacrificing. There was nothing to sacrifice.

⭐⭐ **Four rules for looping in one evening -- the opcode, the set count, neither, then the
flag.** The first three were inferences about STRUCTURE standing in for a FIELD, each survived
until a person listened to it, and the field was sitting parsed in the codebase throughout.

### ⚠⚠ "Features have no Create animation" -- WRONG, and wrong in a way this file warned about

Master asked whether features even had create animations. This said **no**: slot 0 is `Create`,
and 0 of 18 feature `.aps` in JUNGLE carry one. Master corrected it in a line: *"slot 1 for toilet
and s_plant are the create animations."*

⭐ The mistake is one already recorded two sections up, about characters: **the slot-name table is
the RIDE's** and "need not mean the same thing for a character". It does not mean the same thing
for a **feature** either. Asking "does slot 0 exist" answered a question about rides.

**What the data actually says**, once you look at every world instead of one:

| world | feature `.aps` | carry slot 0 | carry slot 1 | typical |
|---|---|---|---|---|
| JUNGLE | 18 | 0 | 2 (`s_plant`, the toilet) | 5 |
| FANTASY | 15 | 0 | **0** | 5 |
| SPACE | 24 | 1 | **0** | 5 |
| HALLOW | 18 | 0 | **0** | 5 |

⚠⚠ So "features keep their create in slot 1" is **not a rule** -- it is true of the two assets
master named and of nothing else on the disc. The first fix generalised it to every feature, which
would have put a silent fallback on 57 assets that do not want one. ⭐ Two names are not a rule;
this repo has a note about taking master's spec literally and this is what it is for.

⭐ What IS invariant, and what the port's fallback exists for: **30 feature scripts open
`WAITANIM 0 0` while almost nothing carries a slot 0.** The console must resolve that request some
other way; this port falls back for the two named assets and says so.

⚠ And master's other instruction -- "the small toilet and small tree's animations are baked
backwards ... reverse em when playing em" -- is applied to the same two, matched by name. Nothing
in a record says which way round it was authored.

⭐ The tag semantics still settle the stacking half: `ADDOBJ` names an object and `KILLOBJ tag` /
`FADEOBJ tag` act on *the* object with that tag — singular, and `RideSounds.Kill` already looks it
up that way. Re-adding a live tag restarts that object; it does not create a second one.
⚠⚠ NOT a decoded rule: the console's object list at instance `+0xb0` has never been walked. What
is certain is that five overlapping snorts is not something the game does.
⚠ Loops only — one-shots must still overlap, or two guests cannot be sick at once.

⚠ And master's framing ("assuming fps = tps") was tested and is **not** the cause: equal simulated
time at two different step sizes produces an identical number of trigger periods. The spam is one
event firing repeatedly, not a clock running fast.

### The ride's value, read — `FUN_001B82D0`

```c
base = record[0x18];  if (base == 0) return 0;      // the compiled BaseExcitement
s = clamp((speed    << 12) / 100, 0xC00, 0x1400);   // 0.75 .. 1.25
d = clamp((duration << 12) / 5,   0xC00, 0x1400);
return min(base * (s * d >> 12) >> 12, 100);
```

⭐ Verified against the image independently of the trace that named it: `0x366330 + 0x1D4` is
`0x001B82D0` with the `-8` this-adjustment, and the coaster/tour/track/feature rows of the same
table reproduce exactly. `record[0x18]` is offset 24, which this port's parser already calls
`BaseExcitement`.

⚠ The SETTINGS' defaults are not read — the getters go through a second vtable at object `+0x18`
which is not in hand. Measured on the disc, speed is `1..100` on every ride (so it is a percentage
whose maximum gives exactly 1.0 and can only pull the value down) while duration ranges vary from
`1..1` to `10..60`, and anything from 7 up saturates the 1.25 cap. Replaces the invented flat 45;
that constant now applies only to a ride with no compiled record at all.

### ⭐⭐ Construction particles: they exist, nothing asks for them (2026-09-24)

Master: *"we are missing a lot of particle effects. mainly the ones produced when something is
built."* `Tp2.plb` names them without ambiguity:

```
75 Destroy1  76 Destroy2  77 Destroy3  78 Destroy4
79 Create1   80 Create2   81 Create3   82 Create4        89 Upgrade
```

⭐ A census of every `EVENT`/`ADDOBJ` particle request in **every script on the disc** finds 33
distinct (kind, id) pairs, all 33 resolving — and **none of them is any of the above**. The
library holds 105 effects and 72 named ones have no script caller at all. So the construction
puff is the game's to spawn, which is precisely why this port never showed one.

⚠⚠ **THE SPAWNER IS NOT FOUND.** None of those ids appears as a literal near either effect entry
point anywhere in the image, so the call computes its id or reads it from a table. The port now
emits one at placement as a **labelled port choice**, with the variant chosen by footprint --
1 cell, under 4, under 8, larger -- because that is the shape the console uses for its other
size-varied effect family (below). ⚠ That is a reading of a DIFFERENT function, not this one.

⭐ **Found on the way: the ride scream.** `FUN_001B94B8` picks an effect on exactly those
thresholds and spawns it at the object -- and ids `0x47..0x4A` resolve to `scr2l*`, `scr4l*` and
`scr8l*` plus the `kid-l*` voice lines.

⚠⚠ **CORRECTED 2026-09-24: "by footprint" was wrong -- the thresholds are the RIDER COUNT.** The
function tests `param_2`, which the opcode dispatcher `FUN_001BCFA8` case 0x56 takes straight from
`STARTSCREAM`'s first operand, and the scripts write `STARTSCREAM VAR_ONRIDE 20`. Its guard is
`handle == 0 && param_2 != 0`: a placed ride's footprint is never zero, a rider count is. Screams
sized to the CROWD, not to the ride. Implemented in `RideScreams`; see findings/sound.md.

⚠ And a reading retired: `FUN_00111428`'s second argument is **not** a sound group or a particle
kind. Its values across the image are 7, 8, 1, 12, 2, 3, 6, 13, 4, 9, 11, and id 175 -- a SOUND --
appears under both 1 and 2. Whatever it selects, it is not the `OBJ_SOUND_*` numbering, which the
failed control this evening had already shown.

## ⚠⚠ RETRACTION: `FUN_0020C6A8` is the destination picker, not queueing (2026-09-25)

Flagged by tinyclaw while landing `tinyclaw/native-ride-queues`: "20C6A8 is the destination picker,
not queueing. its +5 to +0x78 fires when nothing scores, so the `VisitorNeeds.Queue` comment is off."

Checked, and they are right -- **and this port's own files already said so.**
`findings/native-destination-score.md` describes `20C6A8` returning early if `G+28` holds a target,
otherwise enumerating candidates through `1E5AF0/1E5BA0/1E5CA8`, scoring them, and storing the
winner at `G+28`; the happiness penalty in it is for a poor CHOICE -- "a chosen score<8 costs
happiness 5" -- not for waiting. `GuestDestinationScore.cs` calls it "20C6A8's sequential choice".

⭐⭐ **Two of my own files contradicted each other for days and I did not notice**, because each
read consistently on its own. The destination write-up is the one that actually read the function;
`VisitorNeeds`' comments inherited the wrong name and then propagated it to `ParkVisitors`, to
`ServiceChecks` twice, and to this file twice. A claim repeated in five places is not five
corroborations of it -- it is one claim, copied.

**What survives:** `+0x78` is still not boredom. That rests on `FUN_0020FB88` pointing the
`tbbored` bubble at `guest[0x7B]`, which does not depend on this attribution.
**What is lost:** the claim that QUEUEING raises `+0x78`. It had no other support, so the byte's
risers are unread again and what it is a need FOR is once more unknown.
**What is NOT changed:** the port still charges the wait. Removing a charge is as much a behaviour
claim as adding one, and the console's queue cost has not been read. The comments now say the
number is the port's rather than the console's.

## ⚠⚠ `VisitorNeeds.Decide` is authoritative and destructive: no external thought can survive

The same report surfaced a bigger defect, and it is mine rather than the queue's.

`Decide` ends with an unconditional `w.Thought = t`, and `Viewer.PlaceThoughts` calls it for every
VISIBLE guest EVERY FRAME. So any thought set anywhere else lives at most until the next frame that
guest is on screen, and a thought `Decide` cannot produce can never be seen:

| writer | fate |
|---|---|
| ride queue's `BadQueue` (thought 9) | erased always -- `Decide` has no BadQueue arm |
| the 128-tick ladder's `Thought.Bored` | erased always -- `Decide` never produces Bored |
| `DirtyLavatory`'s `Thought.Angry` | erased unless happiness is under 3 |

⚠ The middle row is the one that stings: the `FUN_0020FB88` bubble ladder was decoded, ported, given
its sounds -- and then silently overwritten every frame for anyone on screen. It was never visible.

⭐ The fix is ARBITRATION and its shape is a decode question, not a preference: does the console
re-decide destructively too, or does an event thought latch for a while? Until that is read, adding
a `BadQueue` arm to `Decide` would make the bubble appear without establishing that it should.

### ⭐⭐ The fix, and why every check stayed green through the bug

`Viewer.PlaceThoughts` now calls `VisitorNeeds.ThoughtOf` -- a pure read -- instead of `Decide`.

That is not a preference. `Decide` is `FUN_0020C930`, the arm that runs when a guest CHOOSES WHAT
TO DO, and tinyclaw's census of `+0x40` writers found every one to be a one-shot EVENT write with
no per-frame writer anywhere: `20C6A8` writes 4 when nothing scores or 2 on a sub-8 pick, `210428`
writes 9 once as the guest leaves. The per-frame poll was this port's invention. ⭐ And the
ladder's own comment in `VisitorNeeds` already stated the correct model -- that the two sources
coexist and the ladder "refreshes slowly rather than stamping over the decision bubble every
frame" -- while the renderer did exactly what that sentence forbids.

⚠ `Decide` now has NO call site. It is an event handler waiting for its event: nothing in this port
yet marks the moment a guest picks a destination. Kept rather than deleted, because its rules are
read off the console. ⚠⚠ `tools/dead_port_audit.py` will flag it -- that is expected now, not a
finding. What the port loses meanwhile is nothing real: the renderer was passing `foodNearby: true`
for every guest on every frame, asserting that every amenity is always adjacent to everyone.

⭐⭐ **AND THE CHECKS COULD NOT HAVE CAUGHT THIS, WHICH IS THE LESSON.** `MoodChecks` exercises the
ladder directly and asserts `Thought.Bored` at boredom 91 -- it has been GREEN throughout, while
the bubble was never once visible in the running game. The check tests `VisitorNeeds` in isolation;
the defect was `Viewer` calling it wrongly. A defect that lives in the SEAM between two components
is invisible to every test that instantiates only one of them, and both of mine did.

### ⚠ The decision-time thought write, and a regression it did not fully fix (2026-09-25)

tinyclaw, against `949333e`: three runtime checks in the standing-service scene went red --
`[89] satisfied guest clears visible toilet thought`, `[141] BEFORE service real toilet bubble is
visible`, `[155] completion ... removes toilet thought` -- because "those checks leaned on the
per-frame Decide to raise the toilet bubble, and now nothing raises it".

⭐ **Half right, and the half matters.** The ladder DOES raise Toilet -- it is the chain's FIRST arm,
`w.Toilet >= Urgent -> Thought.Toilet`. What it does not do is raise it *immediately*: `Moods` runs
on the console's own per-guest stagger, `tick % 128 == guest % 128`. So the bubble was **delayed,
not absent**, and the checks were measuring an immediacy the per-frame poll had invented.

⭐⭐ **Verified rather than argued.** Changing the scene's `Tick(1)` to `Tick(4)` before the bubble
assertion turns `[141]` green. (It reds three others, because the extra ticks shift the service
timeline -- which is exactly why "add ticks" is not the fix.) The experiment was reverted.

**What was fixed here:** `Decide` now has its real call site. `ParkVisitors` calls it where a guest
takes `GuestIdleAction.SelectDestination` -- `FUN_0020C930`'s own moment, a guest CHOOSING WHAT TO
DO. That restores promptness on an EVENT rather than on every frame, so it does not restore the
clobber. ⭐ And its three "nearby" flags are real now: the old caller passed `true` for all three,
asserting a burger van, a drinks stall and a lavatory are permanently adjacent to every guest;
these ask the park through `ProvidesRelief` / `HungerEffect` / `ThirstEffect`.

⚠ **It does NOT fix the standing-service scene**, and that is worth saying plainly rather than
leaving it looking fixed. That guest is already `Servicing` when the scene starts, so it never
takes a decision action inside the test window. The remaining failures need the scene to reach the
guest's mood slot before asserting -- a change to the scene's timing, which belongs with whoever
knows what the scene is proving.

### ⭐⭐ THE BUBBLE SLOT IS WHAT IS DRAWN -- a stale-thought bug, and where clearing belongs

tinyclaw, testing `VisitorNeeds` alone: toilet 95 raises `Thought.Toilet` at the guest's mood slot;
then toilet 0 and 400 more ticks (three slots) left the thought as **Toilet** with `BubblesHeld`
**0**. So a satisfied guest kept showing a stale toilet bubble.

⚠ **My own comment described the fix and the code did half of it.** The give-back arm says "the
console GIVES THE SLOT BACK here rather than leaving a stale bubble up" -- and it released the slot
and left `w.Thought` standing, while `ThoughtOf` never looked at slots at all.

⭐ **The rule, from the console's own arms:** both `20C6A8` and `210428` check `2E28D0 < 25`, take a
slot, set `+0x34 |= 8`, and only then write `+0x40`. tinyclaw: "which reads like the slot is what's
drawn". So holding a SLOT, not holding a thought, is what puts a bubble over a guest. `ThoughtOf`
now returns `Normal` without one, and every writer goes through `TakeBubble`/`ReleaseBubble` --
the ladder, `Decide`, and `DirtyLavatory` -- so no arm can set a thought without claiming its place
in the budget.

⭐⭐ **AND CLEARING IS THE LADDER'S JOB, NOT THE SERVICE'S -- read, not assumed.**
`FUN_0020EDD8`, the relief arm, only ever SETS a bubble: at its lines
`if (lVar9 == 0 && DAT_002e28d0 < 0x19) { DAT_002e28d0++; *(u16*)(g+0x34) |= 8; } *(u32*)(g+0x40) = 10;`
-- take a slot, mark the holder, write thought **10** (Angry, for a filthy lavatory). It never
clears the toilet thought. So on the console a satisfied guest's bubble persists until the ladder's
next 128-tick slot gives it back.

⚠ Which means the standing-service checks `[89]` and `[155]` are the same shape as `[141]`: they
assert immediately and the console does not clear immediately. The fix there is the mood-slot wait,
not more code here. ⭐ The gating IS still required -- without it the bubble never cleared at all,
which is what tinyclaw's 400-tick run showed.

⭐ Pinned in `MoodChecks`: toilet 95 -> Toilet, then toilet 0 and three slots -> `Normal` with 0
slots held. ⚠ Isolated on **happiness 78**, deliberately: at 50 the guest legitimately picks up a
different bubble through the ladder's `26..74, one roll in ten` arm, so the check fails on correct
code. 78 is above the 74 bar and below the 81 one, so the toilet is the only bubble in play.
