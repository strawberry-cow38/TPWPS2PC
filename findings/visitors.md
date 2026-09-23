# Visitors on the park grid

2026-09-22. **Ada (guest 101) walks to Orbiter or Bugs TV, queues, is accepted by the ride's real RSSE
script, comes back through that script's unload mailbox, and walks out.** Both visitor audits
cover SPACE and FANTASY `terrain_1.mps` and `terrain_2.mps`. The scene renders the disc's character meshes
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
