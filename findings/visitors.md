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
  Three records on the disc carry one (FatMechanic 2, hunter 1); none of the kids do. ⚠ Not yet
  applied by the viewer.

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
* The per-mesh show/hide lists (`FUN_001a8c30`) are read but not applied; prop bones some
  records leave unkeyed (Box01–03 on dino/flower/gnome, the toolboxes, the guns) sit at the
  identity here where the PS2 has stack garbage -- and, presumably, the list to hide them.
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

## ⭐⭐ A shop takes you INSIDE only if its script contains LIMBO — and the split is exact

Sixteen jungle shops and sideshows, three guests queued at each, 120 s. **Five take a guest inside
and eleven take nobody**, and the old check (`at least one shop took somebody in`) passed anyway —
the five that worked carried it and the eleven were never questioned.

Asking the bytecode settles it. LIMBO is what a shop IS (`0x1bbb30` -> `0x1fa2c8` hides the guest),
so a script that does not contain the opcode was never going to hide anybody. The correlation is
perfect, both directions, with no exceptions:

```
LIMBO in script: yes -> went inside 3/3   Balloon Shop, Costume Shop, Gift Shop,
                                          Steak Restaurant, Arcade
LIMBO in script: no  -> went inside 0/3   Burger, Drinks, Fries, Ice Cream,
                                          Laughing Hyenas, Jungle Spray, Busta Block,
                                          Giant Puzzle, Dino Race, Strength Bird, Gopher Whack
```

**The five you enter are rooms; the eleven you do not are food counters and standing sideshows.**
That is authored behaviour, not a defect — the thing the weak check could not tell you. All
sixteen still hand every guest back out, so a food stall serves you and returns you without ever
taking you off the map, which is what a food stall does.

The audit now checks the two directions that cannot be timing artefacts: **nobody is hidden by a
shop whose script never asks for LIMBO** (nothing else can make a guest vanish, so this fails hard
either way round) and **every shop that took somebody in is one that asks**, plus a non-vacuity
guard that some shop carries the opcode at all. "Carries LIMBO but hid nobody in 120 s" is printed
as a note and not a failure, because an untaken branch is not a bug.

⚠ `AsksForLimbo` scans the chain as it stands after the run, not every program the shop could
reach; a shop that spawns a LIMBO-carrying child only on a later branch would read "no". That is
exactly why the hard failure is the *hid-without-asking* direction.

**⭐⭐ THE SAME FIVE IN EVERY WORLD.** The audit passes on all four, and the walk-in set is not a
jungle quirk — it is the game's design, five archetypes repeated per park:

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

A third of the park takes no guests: 6 of 21 rides in JUNGLE, 6 of 23 in SPACE, 9 of 23 in HALLOW,
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

**⚠⚠ STEP 3 IS A RECONSTRUCTION, NOT A READING.** Its comment ("revisit the instruction to wait
for the requested slot to start") cites no address, unlike the code either side of it, which names
`0x1abc80` and `0x1bcfa8`. So the defect is in the spin condition, and there are two readings the
executable can tell apart and I cannot:

- the console's `TRIGWAITANIM` also waits for the slot to start, and the real Thrill Grill hangs
  too — faithful, and implausible for a shipped ride;
- the console does not gate on the slot having started, and the spin is invented here.

**The fix must come from `0x1bcfa8`'s handler for this opcode, not from intuition.** The obvious
patch — only wait when the animation actually started — would un-stick this ride, and would also
change behaviour for the 59 rides that currently work, so it is not to be applied blind.

The check is deliberately left FAILING on HALLOW rather than excluded by name. A "by design" filter
is exactly where a defect would hide, and one red world is a better record of this than a green
suite with a note in it.


### Two rides ship no animation file at all — only one of them hangs

`wad.Find(stem + ".aps")` comes back null for exactly two rides on the disc: **Thrill Grill**
(HALLOW, folder `firepit`) and **WhirliGig** (SPACE). Neither is a parse failure — the audit's
`catch { }` used to swallow those indistinguishably from a missing file, and now keeps the reason.

**WhirliGig does not hang.** SPACE passes; only HALLOW fails. So "carries no animation records" is
not by itself what stalls a ride — the stall needs a script that *waits* on one, and the
discriminator is `TRIGWAITANIM`. That is a useful narrowing: it rules out "the port cannot cope
with an unanimated ride" and points squarely at the one opcode whose spin condition is a
reconstruction.

⚠ A first cut of this diagnostic also printed the ride's whole folder, to ask whether an `.aps`
lived under another name. **Its answer could not be told from its question**: the directory prefix
it derived (`stem` up to the last `/`) collapses to the WAD root for the flat paths in some
archives, so it listed every model in SPACE for WhirliGig and nothing at all for Thrill Grill. It
was removed rather than tuned. `wad.Find` returning null is measured directly and is the fact.

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

Left FAILING on SPACE rather than excluded by name, same reasoning as Thrill Grill on HALLOW.
