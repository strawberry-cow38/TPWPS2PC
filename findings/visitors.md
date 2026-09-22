# Visitors on the park grid

2026-09-22. **Ada (guest 101) walks to the Orbiter, queues, is accepted by its real RSSE
script, comes back through that script's unload mailbox, and walks out.** This works on
SPACE `terrain_1.mps` and `terrain_2.mps`. The scene renders the disc's character meshes
and the script-selected ride APS. Characters currently move in their bind pose and disappear
while on the ride; seat/head attachments and walking gait are not implemented.

This is a bounded managed simulation of one ride and a finite cohort, not a reconstruction of
the original guest AI. It uses the VM from `rse-runtime` commit `ba028d7`, cherry-picked into
`visitor-ai`. No changes were made to `main`, and no game data is committed.

## Run and audit

Requires the owner's disc and .NET 8. Simulation and pathfinding live entirely in `core/`,
with no Godot reference, subprocesses or native dependencies.

```sh
dotnet run --project tools/TPW.PS2.VisitorAudit -- /path/to/disc.bin
dotnet build game/TPWPS2Viewer.csproj
TPW_PS2_DISC=/path/to/disc.bin /path/to/godot-4.6.2-mono --path game res://VisitorDemo.tscn
# Or choose “Visit the park” in the existing viewer.
TPW_PS2_DISC=/path/to/disc.bin /path/to/godot-4.6.2-mono --headless --path game res://tests/VisitorAudit.tscn
tools/TPW.PS2.VisitorAudit/teeth.sh /path/to/disc.bin /path/to/godot-4.6.2-mono
```

The scene has SPACE park 1/2, pause, restart, speed, orbit and zoom. It opens at 5 seconds.
Ada/101, Ben/202, Cy/303 and Dee/404 request arrival at 0/1500/3000/4500ms. Actual spawn
waits for a free entrance cell. Capacity **10** comes from Orbiter's `Upgrades[0].InitCapacity`
in its own `.sam`, ID **3104**. Duration **1** is an explicit demo input. The last guest exits
at 57 seconds; restart repeats the visit.

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
prologue is not valid input for an ordinary MIPS32-only linear walk. **A drawing skip test is
not a full guest navigation consumer.** The new placement policy is deliberately narrower:
only cells whose entire byte0 is zero, with no ride occupancy or fixed scenery, can be built
on or walked through. This excludes raised/skip cells and every undecoded nonzero flag class.

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
| SPACE 1 | 96×54 | (83,15) | (81,24) | (85,18) → (85,21) | (86,18) |
| SPACE 2 | 72×62 | (40,22) | (38,31) | (42,25) → (42,28) | (43,25) |

The ride uses its own `Info.Shape`, with `2` on the last row. The queue front is immediately
outside that row. The exit is an **explicit demo portal** one cell to its right. No claim is
made that the `N` symbol or engine exit offsets have been decoded by this work. Spawn is a
chosen endpoint of the constructed public path, not the original park-gate spawning service.

FANTASY was attempted and **rejected**: its otherwise ordinary ground uses nonzero classes
including `0x20` and `0x22`, so there is no eligible site under this policy. The audit requires
that explicit failure. It does not relabel those flags as zero or silently change worlds to
make a run pass. The visible scene offers only the two SPACE parks.

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

The matched source is `/DATA/SPACE.WAD/Rides/orbiter/orbiter.rss`. Its actual statements are
checked before the audit runs. While loading it reads `LETMEON`, resets `STARTNOW` to the
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

| Time | Ada / script observation (SPACE 1; same times on SPACE 2) |
|---|---|
| 100ms | Ada/101 spawns at (81,24), targets (82,24), progress **100** |
| 7000ms | Ada reaches queue tail (85,21) |
| 10000ms | Ada reaches front (85,18), offered ID **101** is consumed; HUSH stack contains **101**; `ONRIDE=1`, `SPACELEFT=9`, `STARTNOW=20000` |
| 12000/14000/16000ms | Ben/202, Cy/303, Dee/404 board in order; final `ONRIDE=4`, `SPACELEFT=6`, `STARTNOW=26000` |
| 26000ms | `TEMP=0`, `RUNNING=0`: timeout equality does not run |
| 26100ms | `RUNNING=1`, `COUNT=1` |
| 34000ms | `RUNNING=0`, HOP returns Dee/**404**; `ONRIDE=4` while her mailbox is held |
| 35000/37000/39000ms | Subsequent HOP identities **303,202,101**; at 39000 Ada reappears at exit (86,18), `LETMEOFF=101`, `ONRIDE=1` |
| 41000ms | Ada clears exit to (87,18); AI clears `LETMEOFF`; script decrements `ONRIDE` to **0** |
| 57000ms | Ada finishes the public exit loop at (81,24), becomes Departed |

All of Ada's intermediate waypoint identities are compared with the explicit inward L and
outward loop, not merely the endpoints or number of steps. `ADDHEAD`/`DELHEAD` request IDs
are compared with **101,202,303,404 / 404,303,202,101**. The host still records those head
effects without attaching character geometry to seats.

## Rendering and audit teeth

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
requires both exit **1** and the specific failure, restores both files with an EXIT trap, then
reruns the original audit. No fault switches live in simulation code. Optional Godot checks
mutate only the audit's actual rendered node.

| Deliberate mutation | Required failure |
|---|---|
| Movement increment `100` → `0` in `VisitorSimulation.Move` | `Ada movement at 100ms: expected 100, got 0` |
| HUSH stores offered ID **+1**, preserving stack occupancy | `Boarding identity lost: Ada (101) is absent from RSSE HUSH stack` |
| Audit moves Ada's actual rendered node to the origin | `Ada rendered position identity at 100ms`, Godot exit **2** |

Unmodified and restored managed/geometry audits pass. Controls also remove a real connecting
path cell, hold a closed queue and reopen it, preserve terrain flag/material identities, reject
construction through the fixed entrance scenery, enforce guest cell reservations, and compare
identical final VM/guest state under different caller frame cadences.

The existing `TPW.PS2.RseAudit` also passes after the integration, including its source/binary
alignment, ride guest handshakes, timers and failure guards. Final mutation logs are outside
the repository at `/tmp/tpw-visitor-teeth.NLTrFQ/`; trace logs and inspected captures are under
`/tmp/visitor-probe/`. They contain no new repository fixtures. The graphical SPACE-2 capture
at 39000ms shows Ada at the exit and Ben, Cy and Dee already walking away.

## What could not be established / remains absent

* Original guest navigation, spawn gates, decision-making, route costs, speeds, crowd rules,
  queue abandonment, needs, happiness, spending and ride selection. BFS, FIFO admission,
  cell reservations and clearance timing are explicit new simulation policies.
* FANTASY navigation under its nonzero terrain flags. Raised paths, slopes, stairs, bridges,
  corner heights and original terrain-cell-to-world orientation are not independently validated
  against PS2 execution. The scene uses the existing viewer's mirrored grid and flat Y=0 floor.
  Scenery projection is deliberately conservative; a tree canopy excludes the ground below it.
* Walking gait/skinning, seat transforms and guest head attachments. `tools/skin.py` documents
  skin data, but the existing renderer still presents character bind poses. Guests visibly
  translate along paths; this is not evidence of animated legs or seated riding geometry.
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
  a disconnected route before walking remains blocked. Only the finite Orbiter cohort is shown
  end to end. FANTASY rejection is a limitation check, not another successful visitor cycle.
