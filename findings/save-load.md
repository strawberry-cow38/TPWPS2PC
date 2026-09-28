# Total park save/load — September 28, 2026

Request:1554033583797178469, strawberry. Restore the CURRENT RUNNING PARK through
Load Game on the main menu; normal launch must show Main Menu; Main Game -> lobby.
This is a port save format, not a claim of PS2 memory-card-format compatibility.

## Contract

A save is not complete if it only replaces scenery and restarts guests/rides.
Capture at a non-reentrant simulation boundary. Preserve identities, ordered queues,
private timers/carries, pending work and random continuation. On load: validate file,
version, assets and every owner first; construct an isolated replacement; resolve
reference IDs/rebind callbacks; only then swap the live park. No partial mutation of
the currently open park on load failure, no replay of charges or guest admission.

Explicit DTOs, schema versions and reference-ID tables, not reflection over CLR or
Godot objects. Assets are references to the player's disc, never extracted into saves.
Render/audio handles are rebuilt from logical state. Delegates, CLR type names,
Godot objects, asset blobs and process addresses are not serialized.

Game scope is distinct from park scope: ParkAwards.HiddenAwards outlives a park.
The advisor will add per-rule deadlines/failure days,22 event counters and20 queued
messages (tinyclaw's concurrent work); reserve a coordinator section for it, don't
silently reset it. Cow's finance/research UI may add data consumers or new owners.

## Coverage ledger (not a finished-save claim)

| Owner/category | Capture/restore state |
|---|---|
| Main Menu startup | implemented and rendered; bare launch -> New Game -> Main Game -> actual lobby; explicit map/mode preserved |
| Save-file transport | foundation: version/size/hash-checked JSON DTO envelope, flushed temp + replacement + previous .bak; no automatic corrupt-file repair |
| ParkClock | explicit DTO:partial accumulator,date,total days,countdown,calendar configuration |
| ParkFinances | schema2:balance/flags/totals,all six144-slot rings(income/wage/balance/gate/shop/sideshow),period,wage accumulator,category ledger |
| ParkAwards | explicit DTO:tickets/earned tickets,stars,medals,hidden flags,medal mapping; GAME scope |
| NativeRoutePool | explicit DTO:all1000words,hint,availability,generation; fresh pool factory,not beneath existing leases |
| NativeActivationSequence | explicit DTO:serial/origin/verified marker/total/per-kind counts |
| Terrain/placements/paths/queues/tracks | pending; asset IDs and edits, ownership/IDs, no raw resources |
| ParkSim/ParkRide/RSE/hosts | VM+host snapshots implemented/tested; ParkSim/ParkRide/time/carry/handles/asset+entity graph coordinator still pending |
| Guests/needs/visitors/routes/entrance/ride queues | pending; exact guest objects and memberships,leases,allocator cursors,request tokens/results,decision timers |
| Staff/litter/research/management | pending; candidates/pools,jobs/patrol/routes,month/weekly stamps,strikes,projects/progress,room occupancy |
| Track/coaster operation | pending; track topology + cars/trains/riders/progress/speed/timers/state, not respawn |
| RNG streams | SnapshotRandom compatible explicit-state generator implemented; owned RSE default integrated. Other owners/shared-provider registry pending |
| Presentation/runtime coordinator | pending; camera/pause/weather/controller clocks,script/animation/audio logical state; resource recreation |
| Main-menu Load Game/file selection + in-park Save | pending until complete coordinator exists; do not offer partial foundation files as park saves |

## Implementation order

1. Land normal startup separately; this is independently usable, not a save-complete claim.
2. Foundation owners + file protocol on astraclaw/save-load, disc-free round-trip and
   continuation checks. Keep them separate from UI until a complete save is possible.
3. Explicit asset/entity ID registry and no-side-effect reconstruction factories;
   RNG continuation, then ride/VM/host state. Add one actual runtime round trip.
4. Guests/native routes/queue ownership/pending services; staff/litter/research and
   management/advisor/game globals. Account for every populated live owner.
5. Actual Viewer build/snapshot/replace + main-menu Load Game and Save operation.
6. End-to-end: busy running park, mid-ride boarding/unloading, walking/queuing/rejected
   guests, staff jobs/rest/strikes, partial-day/month boundaries, research, particles,
   repeated save/load, malformed file, stale disc/version, all8parks. Compare continued
   simulation against an unsaved control, not merely matching load-time counters.

## Current validation and limits

SaveAudit is disc-free and tests explicit owners through JSON, continuation and
corrupt-schema/no-partial-owner mutation. File tests write/read actual files, retain
the previous save, and refuse bad magic/version/length/hash/trailing/missing JSON.
This does NOT test complete-world transactional loading yet. The mutable static
calendar/medal mappings require coordinator-level staging/rollback: creating fresh
owners alone does not isolate globals.

MainMenuStartupSmoke must be run without --menu/--map/--mode for its bare-launch
case; it confirms through MainMenu.Confirm twice, not a direct EnterLobby call.
An explicit map/mode case verifies the actual requested terrain still loads.

## Handoff checkpoint (foundation, NOT complete saves)

Main startup is shipped independently atc83a091: normal menu -> real lobby; normal
park/calendar ticks gated while menu/lobby is displayed; explicit direct park still
advances.9bare+4direct rendered checks; reverting old default or hidden-tick guard
fails. Do not redo this part. Main Game route existed and is now consumer-tested.

Snapshot foundation codeef587c1 and merge9634846 remain onastraclaw/save-load.
SaveAudit passes JSON/file round trips and continuation for its five implemented
owners. It is not a Viewer save/load test. No whole-park file is offered to players.
GameOptions now supplies Viewer.SaveGameRequested; _settings is its GAME-scoped
GameSettings owner with CaptureState/RestoreState. Its restore clamps volumes and
raises Changed; the coordinator must prevalidate and stage before committing globals.
MainMenu.LoadGame still explicitly reports not implemented pending the real load.

Next implementation: stateful RNG compatibility + RSE machine/preview-host snapshots
with reference IDs and no replayed init/actions, tested in actual running ParkSim.
An RSE file's script bytes are asset references, not snapshot payload. VM arrays,
call/guest tops, timers/walks/bounce/limbo, parent/child/sound-child and shared host
identity all matter; machine.Random delegates come from a sharedParkSim stream or
owned default. Reconstruct references only after all IDs are allocated.

RNG inventory: seeded System.Random at ParkSim(11), ParkVisitors(7), default
RseMachine(1), defaultParkStaff(0x5747), TrackRideSim(seed), VisitorNeeds(seed),
RideSounds(seed), Viewer._guestRng(1). Caller-provided delegates may share streams;
never reseed them independently. A seed alone is not continuation. Primary source
checked for a compatible explicit-state generator (not yet implemented):
https://raw.githubusercontent.com/dotnet/runtime/v8.0.0/src/libraries/System.Private.CoreLib/src/System/Random.Net5CompatImpl.cs
MIT license at the same tag's LICENSE.TXT. If adapting that algorithm include its
license/attribution; compare mixed Next/Next(max)/Next(min,max)/NextDouble/NextBytes
against current seeded System.Random BEFORE replacing any gameplay owner.
Alternative native RNG stream adapters remain explicit; do not conflate29CF08 with1448E0.

The full owner inventory from source includes ParkSim time/carry/handle allocator;
ParkRide queues/left/ejected + settings/accounting/service; preview host channels/
queued records/seats/head slots/walkers; GuestWalk live objects/cursors; Visitors
plans/owners/history/returns/terminals/decisions/vomit; Needs cadence/bubble state;
EntranceFlow ordered memberships/tokens+pending Viewer service results; NativeRideQueues;
staff/candidates/jobs/strikes/routes/litter; management stamps; research; track/coaster
train state and riders. See the coverage table above. No serializing delegates/Godot
objects/model/program graphs/assetbytes, no side effects during restore, no arbitrary
CLR type names in files. Phase2 resolves IDs, phase3 swaps only a validated wholeworld.

All three delegates were used in this foundation turn. Clean+pushed handoff; do not
repeat their inventories. Next package must exercise an actual runtime consumer,
not merely add another unused helper. Other agents own laptop/advisor and may add
new fields: fetch/merge, communicate, explicit commits. No restart or privatefiles.


## Running script checkpoint — September28 (~09:00), supersedes next-step notes above

c3e1826 implements SnapshotRandom (MIT .NET seeded compatibility; mixed-call tests),
RseMachine.State (all mutable VM fields, nullable lazy tables, owned RNG vs explicit
external-provider marker, child/parent/sound links by supplied IDs), and
RsePreviewHost.State (current/pending/channels/seat/walker/visibility/effect state).
RseMachine defaultowned RNG now uses SnapshotRandom; injected streams unchanged.
Restore does not rerun Create, tick/spawn, or emit effects. Assets resolved against
independently supplied expected keys. Allocateallmachines first thenhydrate; still
NOT a graph-wide transaction until the whole-world coordinator is implemented.

Parent fixed a real draft defect: hostcapture sortedchannels bykey, changing public
iteration order. Snapshot now preserves enumeration; an independent direct-Keys
check fails the sorted version. Host restores reserve dictionary storage before
committing logical state. Four mutations (lost call stack, reseeded RNG, lost pending
animation, sortedchannels) allfail thenrestoredPASS.

DiscRseSaveChecks: actual VisitorScenario boards a guest on BugTV/Candle/Orbiter at
10,000ms. VM+host state writes/reads a real ParkSaveFile, restores fresh owners,
then both copies run48,000ms with identical surrounding acknowledgements. PC/yield,
fullprivate state, boardedIDs and exactanimationframes agree. Three worldsPASS;
this intentionally isolates VM/host, NOT the whole VisitorSimulation/park save.
Assets stayRAM-only; saves containstate+assetkeys/hashes, notprogram/modelbytes.

9b47fb5 includes newly landedbalance/gate/shop/sideshow rings+totals in finance
schema2. Olddevelopment schema1 nowrejected(not a shippedsaveformat). All6rings
and getters checked throughmultiplewraps, activepartialperiod retained. Also merged
mainclock1c99a1b (2.4s/day) andFinance2032dc3; persistence didn't altertheirlogic.
SaveAudit226760 checksPASS with ownerdiscoptional.83PythonPASS.
Fullcorematrix /tmp/tpw-save-rse-matrix:4PASS+4exactretailreds,landing_evidence=True.
Runtime /tmp/tpw-save-rse-runtime:9/11; visitorFANTASYholderposition andStanding[89]
fail EXACTLYon unmodifiedmain50a7bdd also (/tmp/tpw-save-rse-baseline-runtime).
No newfailure, no test weakened/expectedfailureexpanded. Researchbranchonly.

NEXT: actual ParkSim/ParkRide graph snapshot+sideeffect-free reconstruction with
stable asset/entity IDs, sharedhost+VM links and directory handleallocator, times/
carry, orderedqueues/left/ejected, settings/services/upgrades/mechanicreferences.
Use the existing VM/host code; don't reimplementit. Need preserve externalasset
bindings/spawn/directory callbacks and shared RNG. Track/coaster state joins are
separate required owners; don't call a scripted-ride-only restore a total park.
Then Guests/Needs/NativeFlow queues+leases+pendingresults andstaff/litter/research/
advisor globals, Viewer transaction/filepicker/SaveGameRequested. All3delegatesused
thisturn; worker2 timedout but leftcompletefiles; parentcompiled/testedreviewedthem.
No pending worker writes. Fresh turn may splitnonoverlapping DTO owners. Keep
pushingboundedcommits; do NOT enable Save UI whileworldcoverageisincomplete.

## Scripted ParkSim graph checkpoint — September 28 (~09:35)

ParkRide.State now captures ride-owned placement metadata, all nullable operating
settings/lazy value cache, financial/condition/service/upgrade fields and ordered
queue/left/ejected collections. Staff/definition/VM/host/vehicle references use explicit
IDs, separate from display names. Hydration constructs a new ride, not a gameplay action.

ParkSim.CaptureScriptedState / FromScriptedState adds the ACTUAL scripted park consumer:
time plus fractional tick carry, ordered rides, complete VM parent/child/sound closure
(not depth-limited Chain), shared hosts, allocator/live handles/tombstones, directory RNG,
finance, upgrade order, advisor counters and instrumentation. Pure trusted asset bindings
identify exact program/animation/definition revisions; the restored spawn factory resolves
future children in the saved source scope, retains shared hosts, and binds the same park
directory. Never uses Add, Create, RunSlice, Book or service to hydrate. Callback rebinding
is explicit, after a staged graph validates. Source park is not mutated on a bad restore.

The name SCRIPTED is intentional: tracks/coasters and external VM RNG callbacks are
currently rejected, not dropped. Paths, staff and all visitor/Viewer owners remain separate
required snapshots. This is NOT a full park save, NOT player-ready, and no Save/Load button
has been enabled. Staged static factories do not yet constitute a Viewer world transaction.

Actual owner-disc fixtures: BugTV/FANTASY cut3080ms, Candle/HALLOW5280ms, Orbiter/SPACE1760ms,
each with a boarded guest, queue, nonzero carry, money and handle. Write/read a real savefile,
restore fresh ParkSim/ride/host/VM objects without Create/effects, run the ACTUAL ParkSim.Advance
for48000ms on both: exact time/PC/frame every pass; complete private-state snapshots every25.
Three worldsPASS. Paths are fresh external inputs, not evidence of saved terrain/visitors.
Synthetic graph adds future child and sound spawns, shared hosts/programs, cyclic parent
closure, dead handle nonreuse, ordered upgrades, corrupt references and staged failure.

Independent review found three real draft issues, fixed before commit:
- restored spawn metadata strongly retained replaced children; now weak-key metadata,
  tested by collecting detached, unhandled children while the park remains live;
- a shared host's lazy head count must be budgeted PER VM, not just once per host;
- capture needed live count preflight BEFORE copying tables/queues or resolving assets.
Tests cover prospective allocation rejection and preflight with an oversized live queue.

Five deliberate mutations now fail: discarded carry, reset directory RNG, reversed queue,
strong child metadata retention, and omitted lazy-head allocation budget. The queue mutant
initially SURVIVED because the draft queue fixture was a palindrome (9,2,9), and the graph
fixture had only one guest left. Fixed the TEST DATA: keep repeated identities but make
all ordered queues/left/ejected lists asymmetric; then reversal fails. No production bug
was hidden by simply declaring that mutant irrelevant. All mutations restored.
Final SaveAudit230633PASS with disc;83PythonPASS. Full core/runtime gates recorded below
when rerun against merged main. Tinyclaw fixed standing tests on mainb0ccdfe; visitor
FANTASY anchor failure remains independently bisected to00e5cd9, outside this save slice.

NEXT: native track/coaster owner snapshots (topology/train/car timing/riders/RNG), geometry
and paths, then visitor/needs/native flow/queues/lease/pending route results and staff/litter/
research/advisor/presentation; all required before transactional Viewer load. Three delegates
used this turn (ride DTO, graph DTO timedout with complete tested draft, independent review).
No pending workers. Main/fellow agents moving: fetch/merge; preserve new live fields. Push
research branch only, and do not claim a scripted graph is a total snapshot.

Post-merge gates on b4ac5f4 (main throughc9f7717):
- /tmp/tpw-save-graph-matrix-v2: 8parks,4PASS+4exactdocumentedretailfailures,
  exit2, landing_evidence=True. First /tmp/tpw-save-graph-matrix was INTERRUPTED by
  a short tool timeout and is not gate evidence; the v2 run completed normally.
- /tmp/tpw-save-graph-runtime-v2:10/11, standing nowPASS aftertinyclaw'sb0ccdfe.
  VisitorFANTASYholder remains exactbaseline: actual(37.99974,0,-32.00022), expected
  (37.994743,0,-31.941021). Its firstbad00e5cd9 is under separate investigation.
- postmergeownerdiscSaveAudit230633PASS; no snapshotscopeexpanded/no UIenabled.
Cow9ad3d59 subsequently exposed Viewer.Settings for coordinator; not yet merged here.
Settings and HiddenAwards remain GAME-scoped. UI selection/page are transient, not parkstate.

## Native vehicle graph checkpoint — September28 (~10:40)

TrackLayout.State saves exact ordered waypoints/pieces/four-sample caches/upgrades and
Closed (including incomplete edits), with an independently resolved ground key. The fresh
factory does NOT rebuild geometry or query bridge/ground callbacks. TrackRideSim.State saves
all kart/boat fields and car order, settings/status/timer and full SnapshotRandom state.
Only live behavior change is replacing seeded System.Random with the already compatibility-
proven explicit-state generator; constructor/Step behavior is otherwise unchanged.

CoasterTrack.State saves node IDs, station/pylon/ghost/stack links, exact spline/cache/sample/
winch state, validity and type key. CoasterSim.State saves train/car order, rider lists, poses,
node/sound cursors, status/timers/clock/sound deadlines/latches/winch version. No Spawn/Step/
TestLap/SetState/recompute on restore. CoasterStats is a RETURN VALUE, not owned by CoasterSim;
retained Viewer statistics still need the presentation/placement owner join. Unknown JSON
members rejected. Closed-ring validation must visit EVERY owned pylon then Entry then Exit;
merely allowing a cycle containing Exit accepted Exit.Next=Exit and could hang other walks.
Cold/open/ghost/invalid/loop caches and all14coaster type identities tested separately.

ParkSim.State / CaptureState / FromState is the new complete ParkSim envelope: script core
plus ID tables for layouts/native sims. Shared layouts remain shared, but one native sim
cannot silently serve two ride queues. Native factory callbacks bind TakeHead/Released/wear/
breakdown without invoking AttachTrack/AttachCoaster/Rebuilt/SetOpen/Sync/defaults. Parent
cached state remains verbatim. Old CaptureScriptedState remains explicitly script-only.
Native allocation preflight is bounded both per owner and across the envelope; node IDs,
asset bindings, unowned/duplicate records and callback publication all validated. Paths,
walking visitors, staff, external model/audio bindings and the Viewer are STILL NOT INCLUDED.

Evidence: real public standalone boat/kart Step continuation6000ticks each; multi-train,
four-car loaded coaster6000ticks with all state/cache/pose/cooldown and ordered event equality,
including boarding/release/wear/screams (non-inert controls). New integrated native ParkSim
fixture runs boat+kart+coaster together, deliberately shares one track layout, saves a FILE
at14280ms while all have riders, restores fresh owners and runs ParkSim.Advance for79960ms.
Exact complete graph sampled every20frames, event order everyframe, with actual queue,
release and wear consumers. No claims of saved terrain/visitor minds or full Viewer loading.

Five mutations fail then restored: track RNG reset, reversed car order, erased winch marks,
erased private sound cooldowns, and missing coaster Released→ride.Left binding (integrated
consumer detects that one at continuation frame1100). First mutation batch hit tooltimeout
mid-fourth case; inspected/restored the file explicitly, then reran remaining cases singly.
No mutant left in source. Standalone SaveAudit216264PASS incl14coldtypecases; earlier full
disc231007PASS before those14extra cases. Final postmerge gates recorded below.

NEXT: geometry/placement/path owners (ParkPaths PLUS Park/PathTool owners, including original
under-path tiles/queue owner IDs for delete/edit behavior), then walking/needs/visitors/native
leases/queues/pending work, staff/litter/research/advisor, presentation and Viewer transaction.
User requested TOTAL state, so advisor queue/rule deadlines will be captured even though the
console rebuilds them on load; this departure was explicitly coordinated with tinyclaw.
AdvisorScheduler needs79vars/22counters/bothcursors/warm/v53latch/perrule next+lastFail;
ParkAdvisor flags/state/countdown/ring+headtail/current speech progress; message-stack DTO
already exists on mainbb7b0cc. No Save/Load UI enablement, research branch only.
All3delegates used this turn (track owners, coaster draft timeout, coaster tests/review timeout);
all outputs integrated/tested, no workers pending. Fetch movingmain before next slice.

Postmerge native gates on d4c7a74 (includes main advisorbb7b0cc/deletebd0dab2):
/tmp/tpw-save-vehicle-matrix: 8parks,4PASS+4exactretailreds,exit2,landing_evidence=True.
/tmp/tpw-save-vehicle-runtime:10/11PASS,onlytheunchangedFANTASYanchorbaselinefailure.
/tmp/tpw-vehicle-postmerge.log: ownerdiscSaveAudit231021PASS (includes14coldtypechecks).
83PythonPASS. New RideService.AdvisorEvent is a CALLBACK, not new owned storage; later
wholeworld binder must reattach it to staged advisor alongside the other service sinks.

Geometry next inventory: ParkPaths is core/TPW.PS2.Data/ParkPaths.cs; the placement owner
is game/Park.cs (Godot), NOT core/Park.cs. Snapshot placement metadata/assets/footprints by
IDs, never Node3D. PathTool has separate kind/turn/owner/run arrays, bridge/walkway sets,
doors dictionary, original tile _before, and undo _legs/_leg alias plus Laid. Its _field is
shared with the live ground; preserve that identity, not two independently restored grids.
Tool UI mode/cursor are transient; committed path/queue topology and deletion restoration
are not. ParkPaths also owns occupied/scenery/entrance/protected/gatehold sets and entrance
kinds/entry, beyond Field.Cells. Incomplete tracks are now covered; ground/placement isn't.

## Ground + placement checkpoint — September28 (~11:40)

ParkPaths.State covers mutable tile bytes/height step and occupied/scenery/entrance/protected/
gatehold sets, entrance kinds/entry. PathTool.State covers kind/turn/owner/run arrays, bridge/
walkway/doors, ORIGINAL ground under every laid tile, Laid/report and the undo object graph
(including _leg alias/null/unlisted object, not an assumption that it means the final stack
entry). Fresh factories copy data, not Lay/Repaint/SetEntrance. Asset keys independently
resolved; DTO arrays detached. ParkCell coordinates now required JSON members, so a missing
X cannot silently turn into zero. Capture counts preflight before copying large histories.

Game Park.State owns placement order/IDs/names/ragged rotated footprint+door data, occupancy/
reservations/playable, plot frame/origin/height, LastX/Y and model-root transform/visibility/
TopLevel/asset keys. Validates whole logical DTO before invoking a trusted fresh-node factory.
No TryPlace/Build replay; no Node3D/material/delegate serialization. Failed factory validation
leaves caller nodes untouched/owned by caller. Staged Root remains outside SceneTree; ground
meshes regenerate AFTER logical staging and material/claimed/cutfloor callbacks rebind.
Descendant animation/material timelines and renderer caches are separate presentation owners.

IMPORTANT correction to earlier inventory: production Viewer has TWO HeightField wrappers
sharing ONE Cells byte array, not one wrapper. PathTool+Park bind terrainModel.Field; WalkGrid
constructs its own wrapper then assigns Cells=terrainModel.Field.Cells. The original draft
Park.State assumed a shared wrapper and would reject the real Viewer. Fixed before commit:
explicit SharedField binding, plus ParkGroundSnapshot preserves wrapper identity AND byte
aliasing. It keeps each wrapper's height Step (a control uses different .75/1.25 values).
RestoreIntoTerrainField takes a FRESH STAGED Model so its readonly Field wrapper remains the
actual render/tool owner; all validation completes before its Cells/Step are committed.
Generic Restore never mutates the supplied asset. Do not call the mutating variant on the old
live/cached terrain. Final Viewer coordinator must supply a fresh model, then publish atomically.

Evidence:8terrain ParkPaths audit312 checks; PathTool real edits105; --ground-only runs both
(417). Actual Godot ParkPlacementSaveSmoke659checks (rotated ragged footprints, file roundtrip,
publication/removal/replacement/rejections). ParkGroundSaveSmoke92checks across4worlds uses
real terrain+native path tables, FILE roundtrip, fresh staged model/ground/placement, then
actual selection, active-leg continuation/Undo, owner queue delete and path-to-grass. Walking,
rendering and editing all see the same restored bytes; source model unchanged. Rejected nested
tool state cannot partially mutate even the caller's staged model. These are owner consumers,
NOT a full Viewer Load Game path or proof that visitors/staff minds have been saved.

Five mutations fail/restored: aliased input grid (no clone), lost active-leg reference, lost
original-ground table, copied rather than shared render/walk bytes, lost placement occupancy.
One preliminary mutation invocation had a new runner return-type compile error, then one had
a30s timeout; neither counted as a test failure. Corrected runner and95s bounded retries all
fail on their intended controls; final restored source green. SaveAudit231438PASS withdisc;
Godot659+92PASS. No mutant left. Main ClearQueue defect discovered by continuation audit:
demoting Both deleted its original-ground backup; reported to cow, who fixed it independently.
After merge update that audit's old GroundUnknown expectation, do not undo the gameplay fix.

NEXT walking guests / VisitorNeeds / ParkVisitors / native route service+leases+queues+pending
work, staff/litter/research/management/advisor and final presentation/Viewer transaction. Geometry
now has owners, but Viewer allocator/asset registry and model/ride/world cross-links still need
the complete coordinator. Save/Load UI remains unavailable until ALL required owners stage.
All3delegates used this turn; no pending workers. Main perf changes moving fast; fetch/merge,
review newly added persistent vs derived fields, explicit paths, gates and normal research push.

Final geometry gates on a7275e8 (main6ee6c19): ownerdiscSaveAudit231438PASS;
Godot placement659+fourworldground92PASS;83PythonPASS. /tmp/tpw-save-geometry-matrix:
8parks,4PASS+4exactretailreds,exit2,landing_evidence=True. /tmp/tpw-save-geometry-runtime:
10/11PASS,unchangedknownFANTASYanchorbaseline only. Evidence logs /tmp/tpw-placement-final.log,
/tmp/tpw-ground-final.log,/tmp/tpw-geometry-final-saveaudit.log.
Cow's subsequent ClearQueue fix is12352fb and perf7f76494 oncatboy/frame-allocations,
NOT main yet at this checkpoint; tinyclaw reviewing/rebasing. Prefer reviewedmain when landed.
Update PathToolSaveChecks after merging: demotedBoth should keepbackup and NOT emitGroundUnknown;
exercise GroundUnknown explicitly with an authored/no-backup fixture instead of relying onbug.

## Visitor/walking graph checkpoint — September28

GuestWalk.State captures time/carry/allocator, ordered live membership and separately identified
Guest objects (GraphId != reusable numeric Guest.Id), including caller-supplied inactive bodies.
Two-phase AllocateState/Hydrate handles external terminal/controller/input callback cycles without
Spawn/AssignRoute/pathfinding/Step. ALL native cursors adopt the ONE restored NativeRoutePool;
position/state/slot/facing/generation/disposal/terminal flags persist. Reject overlapping owned
chains, unknown bindings and incorrect pool epochs. Input provider VALUES remain their owners'
responsibility; delegate IDs alone are not a saved controller or animation readiness state.

VisitorNeeds/VisitorWants.State captures every want byte/flag/cash/thought, RNG, timing/cadence,
rates/budget and occupied bubble slots. Preferences is a GAME/config fingerprint; restore validates
but does NOT mutate the static table. Root config staging still needs to account for such globals.
ParkVisitors.State owns plans, ride-instance/history references (including retired rides), returning/
service/vomit state, decision deadlines, relief clocks, settings/accounting and RNG. It exposes
referenced guest/ride/terminal inventories and stages callbacks after cycles resolve. A bounded
body registry retains the visitor's last body while aboard; readmission replaces it as before,
retirement removes it. SendTo adopts externally supplied walking bodies too. No spawning, decisions,
service or sound replay during restore. Shared external RNG providers require explicit bindings.

A continuation hole found during parent review: live dictionary entries alone lose future insertion
order after removals. Added IntMapLayout/SnapshotIntMap for the order-sensitive integer visitor maps:
tracks slot/free-stack history while retaining stock Dictionary values/enumerators; reconstructs
through public Add/Remove with temporary private construction keys, not runtime-private reflection.
Source behavior checked against independent stock Dictionary through350 mixed removal/addition
operations and repeated checkpoints. The layout is not inferred just from current enumeration.
The helper is internal; owner maps expose read-only interfaces. Mutation via an explicit cast to
the Dictionary base bypasses tracking and is unsupported; normal mutation interfaces are tracked.
Do not replace this with sorting guests: need/RNG/bubble/queue order is observable.

Actual consumer evidence: a FILE bundle joins ground + ParkSim + GuestWalk + Needs + ParkVisitors,
with a real disc ride occupied and another person walking, across FANTASY/HALLOW/SPACE. Restore
fresh owners in phases, bind Needs→visitor callbacks without setter replay, then run actual
ParkVisitors.Step for79960ms, with NEW arrivals too. Full state sampled every20frames and ordered
sounds each frame match. Saved riders unload/return/continue:5/3/5 completed rides respectively.
No native admission controller, staff, pending route-service requests or Viewer were claimed in
this bundle. Native leased walking/readiness/pool-exhaustion continuation is separately exercised
by GuestWalkSaveChecks, including duplicate numeric IDs and inactive objects.

Five deliberate mutations fail/restored: dropped map hole history, reset needs RNG, separate cursor
pool, lost bubble occupancy, zeroed decision deadlines. Full owner-disc SaveAudit233925PASS;
83PythonPASS. Added --visitors-only focused gate. No mutant left. Existing GroundSaveAudit updated
for main12352fb: demoted Both retains ground, tears up to grass WITHOUT GroundUnknown. Unknown-
ground callback now has an explicit no-backup fixture, not dependence on the previous defect.
Main perf7f76494/ClearQueue12352fb merged. Final broader gates recorded below after running.

NEXT: native entrance/ride queue/controller resources and pending service outputs, staff/litter/
research/management/advisor ownership, then presentation + complete Viewer load transaction.
GuestTerminal bindings and retired ride objects need the WORLD reference registry; current owner
factories deliberately demand those references rather than substituting reused numeric IDs.
Save/Load remains unavailable. All3delegates used/timedout with complete files; outputs integrated,
reviewed and consumer-tested; no pending workers. Fetch main before next ownership slice.

Visitor graph final gates9c5d475 (main12352fb): /tmp/tpw-save-visitors-matrix:
8parks4PASS+4exactretailreds,exit2,landing_evidence=True. /tmp/tpw-save-visitors-runtime:
10/11PASS,onlyknownFANTASYanchorbaseline. Re-ran realGodot ground92+placement659PASS after
mainperf merge. OwnerdiscSaveAudit233925PASS,83PythonPASS; /tmp/tpw-visitors-final-saveaudit.log.
Next native controller files: NativeEntranceFlow.cs871lines, NativeRideQueue.cs482,
NativeEntranceAcceptance.cs41, game/Viewer.Entrance.cs181 (actual pending service seam).
StaffRouteService.cs is another pending-work owner; do not conflate its request pool with
GuestWalk.NativeRoutes. Their references must adopt the restored ONE output pool where shared.
For future maps whose enumeration affects play, reuse IntMapLayout instead of losing removed
slots. Layout metadata tracks public Dictionary mutations, not private CLR internals.

## Native controller / deferred-result checkpoint — September 28

NativeEntranceFlow.Snapshot saves all allocated entries, independent active/group list orders,
next request token, tick, baseline/activation serial, speed/mode/state, pending token, acceptance/
rejection latches, staging population/pressure/counters, held/deferred/alternate/ordinary flags,
and admitted identities. AllocateState makes an unpublished owner and input bundles, THEN the
shared GuestGraph hydrates leases against those exact objects, THEN HydrateStateBindings verifies.
No Add/Tick/admission/path request/RNG/lease acquisition replay. Busy capture and shell Tick reject.
Services are external by explicit trusted identity: their RNG/fee/readiness/animation values,
bus traffic and guard state must still be saved by the world coordinator. Snapshot != service code.

NativeRideQueues.State saves ordered members/queues, movement states, routed latches, deadlines,
clock/counters and bounded boarding history (including detached guest bodies). Same two-phase
lease/owner/input hydration and same GuestWalk.NativeRoutes, never a second output pool. The
input-key registry is staging-only and cleared after hydration, avoiding a restored queue retaining
all its departed guests through input closures. Queue shapes and services remain external values.

A parent continuation found TWO additional requirements:
- native queue boarding Remove disposes/nulls the native lease but leaves the detached Guest's
  old coarse slot (-1 or a prior valid slot). GuestWalk validation had rejected this real state.
  Inactive bodies now preserve the bounded stale index; live bodies still require a matching lease
  or an ordinary route. No gameplay reset was added to make the snapshot look tidier.
- the reference-key ride->queue Dictionary ALSO reuses removed slots. SnapshotReferenceMap owns
  slot/free-stack history like the existing integer helper; serialized slots reference ORDINALS in
  the accompanying live queue array, not ride IDs. Restore uses private, side-effect-free ParkRide
  placeholders, then removes them in saved free-stack order. A real three-queue test deletes the
  first queue, loads, adds a fourth ride and checks it appears BEFORE the older two, plus150ticks.
  This is behavior, not aesthetic dictionary order: boarding/RNG evaluation follows that order.

NativeEntranceMailbox is now the Viewer's ACTUAL deferred result owner (Viewer.Entrance.cs), not
an unused snapshot helper. It saves result order, tokens, detached waypoint arrays, null vs empty,
failure strings and exact GuestGraph identities, including stale/duplicate/inactive results. The
next Pump drains the saved batch; it does not redo BFS against possibly changed paths. Capture at
quiescence; an in-progress drained batch is not a capture boundary. Diagnostics _entranceRequests,
fee/tick/counters/flags and the remaining Viewer provider values still need the final root DTO.

StaffRouteService.State stores its ten-record pending list newest-first, exact target/flags and
stored clamped cells, counters and owner-object keys. Restores only onto an unpublished service
with the SAME ParkStaff paths/tiles/output-pool references. Duplicate requests for one staff owner
are allowed (Submit permits them). StaffMember/job cursor state is NOT implemented in this slice;
its continuation test uses independently built same-state owners. NativeTileView cached building
values/provider also remain outside this DTO. No Submit/Pump/event replay at restore.

Evidence: file bundles join entrance+walk+visitors+mailbox and ridequeue+walk+visitors+needs+sim+
ground. Actual Tick continuation covers both entrance groups, pending/stale results, accepted and
rejected arrivals, blocked exits, ordinary departures, tokenless alternate failures, queue boarding,
move-up/impatience, broken/demolished release and future queue insertions. Service test providers'
values are explicitly copied separately; this does NOT claim production bus/fee/animation capture.
Acceptance test asserts exactly six acceptance calls despite repeated loads. Observation probes
call the queue's Shape service, so the test now samples BOTH branches before comparing call counts;
the first asymmetric probe was a test defect, not a restore defect.

Six deliberate mutations failed and were restored: reset next token, lose acceptance latch, reverse
queue membership, discard reference-map holes, drop first mailbox result, reverse staff requests.
A batched mutation tool timed out during the fourth; inspected files/processes and explicitly
restored it before rerunning each remaining mutation individually. No mutant left.

Code d1f47d9; merged advisor main884079b at2a2991b. Ownerdisc SaveAudit235766PASS;
83PythonPASS. /tmp/tpw-save-controllers-matrix:4PASS+4exactretailreds,exit2,landing_evidence=True.
/tmp/tpw-save-controllers-runtime:10/11PASS, ONLY existing FANTASY/1 anchor from00e5cd9
(actual37.99974,-32.00022 vs expected37.994743,-31.941021). Bus, advisor, standing all pass.
Full save log /tmp/tpw-controllers-merged-saveaudit.log. --controllers-only is focused gate.
No full-world save/LoadGame UI enabled. All3delegates finished (first2timedout with complete files),
reviewed/integrated; none pending. Explicitly do not present pending-request coverage as staff coverage.

NEXT: staff/job/manager graph. ParkStaff.cs399 + Management565 + Security637; StaffMember647;
ResearchManager194, StaffCandidates99, ParkLitter137, NativeTileView171. StaffRouteService is ready
for joining. ParkStaff constructor currently writes Walk.Paused and builds pools: restore needs a
no-side-effect staged construction path, retaining the ONE shared output pool/activation sequence.
Default staff RNG is still System.Random(0x5747), requiring owned SnapshotRandom or explicit shared
provider binding; no seed-only restore. Handyman/Mechanic subclasses and guard/entertainer job
objects all have state. Staff targets may refer to features/litter/ride instances/other staff.
Then native bus controller116lines + actual Viewer native bus/demand/guest animation providers;
advisor Scheduler/ParkAdvisor/MessageStack and NEW visible/audio state from884079b; all presentation
owners and final Viewer transaction last. ParkSim.ObjectRemoved is a NEW callback rebound through
ParkAdvisor.Attach, not serialized as a delegate. Settings/HiddenAwards remain GAME scope. Read
findings/advisor-viewer.md before capturing presentation. Full save requires all these joins.

## Staff / job / management graph checkpoint — September 28

ParkStaff.State now joins all 25 persistent slot objects (including stale fired slots), exact
per-kind free/active order and map update order, hire-held identity, tick/pool epoch, strikes/stamps/
stages, management flags, training count, lazy candidate DB and lazy research manager, litter,
cached NativeTileView, pending StaffRouteService records, room polling cache, security owners and
explicit provider/callback identities. AllocateState creates unpublished stable member shells;
HydrateState resolves links against the staged world. Neither invokes Hire/Drop/Step/Activate,
allocates output routes or replays litter/research/audio/advisor events. Constructor's Walk.Paused
write and litter variant rolls are skipped on this path. Default staff RNG is now owned
SnapshotRandom (same seeded .NET sequence); explicit external RNG delegates still require their
provider owner/state, not seed substitution. Named default callback methods make built-in bindings
reconstructible without compiler-generated closure names. StatePaused is exposed for GuestGraph.

Member DTOs cover goals, flags/modes/states, target/candidate object keys, serials, routes/epoch,
patrol/candidate/hire fields, tiredness/morale/settings and counters. Handyman has no extra mutable
fields; Mechanic adds dispatch counters, Researcher adds work quanta. Guard/Entertainer private job
fields are in SecuritySnapshot. Parent review removed the worker's UnsafeAccessor/backing-field
string approach: Guard/Entertainer partials now write their OWN fields through internal hydration
methods. NewlibRand exposes explicit current uint state; the owned PrankStinks generator persists
that, not its original seed. No runtime-private reflection is used by production snapshots.

Security includes all effector slots/free/active order (preserving SAME effector objects for job
links), stink records/counters/RNG/callbacks, carried guest copies, guard leg/counters, entertainer
show/effector/counters, watcher/cooldown/first-seen tables, live-target set, advisor counters and gate
binding. Watching/time dictionaries now preserve free-slot history with SnapshotIntMap. Room
polling uses the reference-map helper with its ORIGINAL default object equality (not a changed
reference comparer), plus slot/free history. Restoring gate event wiring requires a STAGED gate;
never resolve these keys to another live world's owners.

Leaf DTOs: candidate records preserve immutable data/availability with no RNG; litter captures
all slots/variants/claims, including inactive stale values, with two-phase claimant binding;
research preserves project object identities and all progress/completion fields/callbacks; tiles
retain the cached building cells without calling Refresh/provider. ParkManagement.State captures
calendar driver counters/red-month chain/last wages and binds the SAME clock, awards, finances and
staff. Clock/Awards themselves remain separately owned; Awards/HiddenAwards are GAME scope.

A real cross-owner cycle required extending ParkSim: ride.AssignedMechanic -> member.Target ride,
member -> ParkStaff -> ParkVisitors -> ParkSim. New AllocateScriptedState / AllocateState build the
sim with deferred staff keys; HydrateStaffState resolves ALL members before assigning reverse
links and binding callbacks. Advance/Capture on that unhydrated sim refuses. Existing one-phase
FromState APIs retain their behavior. This avoids creating fake staff owners, replaying dispatch
or substituting null permanently. The staff graph also validates output-chain disjointness across
ALL walking guest cursors AND staff cursors against the ONE restored NativeRoutePool.

Evidence: real FILE bundle ground/walk/visitors/needs/sim/staff/clock/activation/finance/management/
awards, all five kinds hired, a visitor moving, pending route work and a claimed litter item. Fresh
allocation continues420 actual ParkVisitors.Step calls with cleaning, new litter, firing/re-hiring,
researcher updates, month-end wages and weekly checks; exact state/events match, with repeated
loads during the run. Lazy never-created candidates stay absent and future hire rolls match.
A separate actual Mechanic.Dispatch creates the bidirectional ride/member link before save;
restored identities are asserted, then160 actual visitor updates match. Synthetic authored script/
terrain fixtures here (not a claim of newly measured retail behavior). Leaf tests also exercise
research completion and guard/entertainer jobs. Full guard catch/turnstile+native gate scene is NOT
claimed by these staff tests. Leaf litter sweep uses a direct internal Remove probe; the JOINED
parent test separately proves actual handyman cleaning, so that probe is not mistaken for a job.

Graph negative tests reject missing RNG/resource IDs, bad active/map/held membership, bad watcher
allocation history and two staff owning the same output chain, without changing the source world.
Six mutations fail/restored: reseed owned RNG, extra litter construction roll, missing claimant,
reverse map order, missing effectors, missing ride->mechanic backref. All3workers completed this
turn, files reviewed/integrated, none pending. No mutant left.

Code f8916b5; main advisor-visuals58dbbf6 merged at9cb0e1a. SaveAudit237412PASS with owner disc;
--staff-only1646PASS;83PythonPASS. /tmp/tpw-save-staff-matrix:4PASS+4exactretailreds,exit2,
landing_evidence=True. /tmp/tpw-save-staff-runtime:10/11PASS, only old FANTASY/1 anchor00e5cd9
(actual37.99974,-32.00022 vs expected37.994743,-31.941021); advisor/bus/standing PASS. Last final
save log /tmp/tpw-staff-merged-saveaudit.log. Final small follow-up expands external provider
inventory; it does not change runtime update behavior. Save/Load UI remains disabled.

NEXT native bus controller+demand/passenger/coarse animation-provider state and advisor core+
presentation state, then WORLD registry/coordinator and transactional Viewer/menu Load. Three
bounded workers can split bus/animation, advisor core, presentation inventory while parent joins.
Read latest advisor-visuals.md / advisor-viewer.md: UI changed on main58dbbf6. Staff animation/audio
readiness is EXTERNAL, not saved by StaffMember itself. Root must supply those live provider values.
Retired ride instances in visitor history/staff targets and GuestTarget objects remain WORLD
registry entries (explicit external keys); do not recreate them by reusable numeric IDs. Root
must also fingerprint/stage GAME config tables (StaffTables etc), GameSettings and HiddenAwards.
StaffPatrolTool and other active tool/presentation state still need an explicit root policy. Do
not call this a complete player save until all required roots stage and publish together.

## Advisor / native animation / bus-presentation checkpoint — September 28

AdvisorScheduler.State preserves variables/counters/cursors/warm-up/latches, every rule's next/
last-failure, report/instrumentation, asset fingerprint and explicit producer/day bindings.
ParkAdvisor.Snapshot retains all20ring slots INCLUDING stale object refs, pending/current/speech/
flags/countdowns/variants, default mouth RNG and lip playback. AdvisorMessageStack.CoreState is
separate from native CaptureState: all32slots, goals, deletion/sliding/input/selection and callbacks.
Parent removed the worker's duplicate SavedLipPlayback implementation: the existing LipTrack.Playback
now hydrates its OWN cursor/gate/Stepped state, so playback has one implementation. AdvisorProducers
has a snapshot for its topology/hooks; delegate closure values still belong to their real owners.
Capture freezes scheduler/advisor/UI between passes; in-update capture is guarded too.

NativeBusController and NativeGuestAnimation/LogicalControl snapshots preserve clock/applied-state
lag/timers/current record/frame/end-hold and dispatcher pending/phase/variant/held-pose/duration/
stamp/boundaries. Assets have APS/logical fingerprints. Guest duration/RNG bindings are explicit;
NewlibRand's current state is checked, NOT rewound by each guest restore. Root restores the shared
RNG once and supplies the same instance to every model. BusDemand is static/pure, not a new owner.

NativeBus GAME adapter restores a detached AnimatedModel/controller graph on eight real bus assets,
including retained root transform/visibility/TopLevel and displayed fractional frame. It refuses
unsampled-bind cuts and unsupported retained-texture/skeletal/index histories; final save safe-point
must be after ordinary Present, or extend its ownership (never sample as a Capture side effect).
Parent added explicit retained hidden-node state (including non-mesh helpers) via narrowly scoped
AnimatedModel methods. No guessed predecessor-record replay is now used. Important test correction:
removing the worker's proposed predecessor replay DID NOT fail its surface-only smoke. That was not
proof of retained state. Added direct hidden-state comparison and an independent injected retained-
flag probe; omitting the explicit hidden restore DOES fail. This is a state control, not a claim
that a particular predecessor sequence was read from the console.
GameAudioMix snapshots live ramp/targets/gains without touching global AudioServer; PublishGains is
explicit commit-time. It does not save physical voice/sound schedulers or GAME preferences.

Evidence: actual file graph with disc275-message catalogue +106rules, fresh real AdvisorProducers,
clock/sim/finance, timed head and logical lip playback;1600 updates plus future inputs/credits/
submissions match, with repeated loads and no voice/sound callback replay. Real head MODEL/Audio-
StreamPlayer are NOT in that core graph. Godot FILE bus cuts on all8real bus assets continue900
updates, exercise all3records, exact CPU matrices/hidden state, surface transforms/visibility and
future state/batch callbacks;142518checks including audio-ramp continuation. No extraction: assets
read in RAM from owner's disc. Six CORE mutations fail (warm/ring/speech/lip/busclock/pending), plus
explicit hidden-state omission fails in Godot; all restored.3delegates used, two completed, third
timed out with bus code/smoke complete but NO requested inventory; parent wrote inventory after
source review. No workers pending. See findings/save-viewer-roots.md for remaining ACTUAL Viewer
providers; that document explicitly is not a complete 11k-line-field census.

Code6dd47db; maindd474a7 merged29cf85d. SaveAudit243950PASS withdisc; presentation-only6538PASS;
83PythonPASS. Godot bus142518PASS /tmp/tpw-native-bus-save-merged.log. /tmp/tpw-save-presentation-matrix:
4PASS+4exactretailreds,exit2,landing_evidence=True. /tmp/tpw-save-presentation-runtime:10/11,ONLYold
FANTASYanchor00e5cd9; advisor/audio/bus/standingPASS. Save log /tmp/tpw-presentation-merged-saveaudit.log.

NEXT actual Viewer provider/renderer/audio roots and WORLD coordinator. `_guestRng` remains plain
Random in Viewer declaration/reset; ConsoleClock is distinct from ParkClock. Native bus outer
time/traffic/demand/counters/placements and entrance outervalues still need DTOs. Guest/staff native
animation dictionaries need GuestGraph/StaffMember identities and shared RNGs joined. Actual advisor
head node flags/channel/pose and audio seek cannot be inferred from CORE Costume/SpeechElapsed
alone. RideSounds/scheduled effects/park vehicle adapters remain to inventory. All logical owner
DTOs exist for the main sim now, but provider values, render state and graph registration are NOT
finished; do not enable or call it a total player save. Startup/menu task already shippedc83a091.

## Actual render/audio + Viewer scalar checkpoint (September 28, 2026)

STILL NOT TOTAL SAVE/LOAD. Startup/main-menu work remains done; player save/load stays unavailable.
AnimatedModel.State now copies actual retained CPU render state (including unsampled record binds,
forced surface transforms, UVs, layers, hidden flags, flags written to private assets and textures),
not only animation time. Fresh renderer restore never samples/replays frames. Explicit read-only
sharing preserves character asset sharing without writing cached dictionaries. Actual two skeletal
actors assert shared Model AND LastWorld as fixture preconditions; restoring one leaves the other
and shared caches/bytes unchanged. Writable advisor Model is private. Explicit UV provider binding
rebuilds the closure outside the DTO; staging does not invoke it. External children/mesh/material
edits and MD2 remain explicit refusals, not dropped fields.

Actual AdvisorHead and voice: selectors -1 included, channel/bound/pivot/viewport/camera/renderer,
plus stopped staged AudioStreamPlayer with explicit seek/pause publication. Godot paused players
have Playing=false AND HasStreamPlayback=true. Initial restore lost paused nonzero playback;
parent fixed BOTH advisor and RideSounds to preserve playback presence. Audio stop/free cleanup
fixed leaked paused mixer resources; scalar-only Viewer test explicitly frees constructor-owned
unattached Weather/Flags/Thoughts nodes. Dummy mixer queued 256-sample block can advance the
reported cursor once after pause: test preserves immediate seek bound, then checks stationarity
after .15s drain (three repeated passes), rather than claiming sample-perfect audio output.

RideSounds.State captures both RNGs, graph repeats/deadlines/sets/order, streams/nulls, moving and
parameter binding IDs, actual voices/fades/seek and census. Fresh stage no Cue/Start/provider calls;
publication starts players only after world accepted. CaptureBindings is an in-memory manifest,
NOT cold-load asset resolution; the world registry still must reconstruct it. Ordinary repeat
fixture was absent from bounded real catalogue search; smoke uses TWO real graph repeats and
claims no ordinary-repeat branch coverage. Voice test uses synthetic PCM, not retail voice decoding.

Viewer.RuntimeState now captures actual outer bus/entrance/gate/guest/staff/advisor latches and
three current RNG states; `_guestRng` declaration AND reset use proven seeded-compatible
SnapshotRandom. ConsoleClock carry/alpha is distinct from ParkClock calendar and now saved.
Restore requires unpublished Viewer and runs no Reset/Ensure/providers. Does NOT capture reference
dictionaries, root topology, all UI/tools, effects or assets. This is an owner join, not whole Viewer.

Godot FILE/continuation tests: AnimatedModel9021PASS (guest+staff+private head+ride+shop, exact
retained CPU inputs and future evaluation); AdvisorPresentation49518PASS (real head assets and
physical dummy audio seek/pause); RideSounds366PASS (real bank timers/graph transition +250ticks);
ViewerRuntime1002PASS (actual Viewer scalars,500mixed dt/RNG calls). Previous NativeBus142518PASS.
Final logs /tmp/tpw-render-final-*.log, no warnings/errors in four new final smokes. All assets read
from owner disc into RAM, no extraction. Three delegates completed; parent integrated/reviewed,
fixed actual runtime failures and ran all evidence. No pending worker.

Mutations: shared-cache write, default-mouth substitution, paused-playback loss, ConsoleClock
carry omission, UV provider execution during stage, graph-RNG reset and sound-deadline reset all
fail (7 distinct). Graph-RNG reset FIRST SURVIVED: initial fixture cut before any transition, so seed
1 == current state. Now forces a real transition and asserts nonseed before saving; mutation fails.
An unrelated worker morph-buffer resize change was NOT exercised (its reversal survived); parent
removed that production change entirely rather than ship/claim an untested fix. All mutants
restored. Logs /tmp/tpw-render-mutations*.{log,json}; final clean build afterwards.

Full SaveAudit243950PASS,83PythonPASS. /tmp/tpw-save-render-matrix:4PASS+4exactretailreds,
exit2,landing_evidence=False (dirty-tree local gate; corrected from previous mistaken True claim). /tmp/tpw-save-render-runtime:10/11 ONLYoldFANTASYanchor00e5cd9,
actual(37.99974,0,-32.00022) vs expected(37.994743,0,-31.941021). Main unchangeddd474a7.
NEXT: actual WORLD registry+Viewer guest/staff render dictionaries/provider/ref join, bus placement
order/assets, sound cold bindings, remaining effects/tools/camera/vehicle adapters. Then transactional
load publication/UI. See findings/save-viewer-roots.md; do not repeat completed owner snapshots.


## Actual Viewer logical + actor-world join checkpoint (September 28, 2026)

Code1bb17be, mainf38954c merged d11a4bc (HUD shadow correction and cache invariant comments).
STILL NOT TOTAL PLAYER SAVE. Three new actual Viewer partials and ActorWorld envelope now JOIN
core and render ownership rather than testing only standalone DTOs:
- WorldCoreRegistry assigns object IDs to live/inactive Guests, active/retained rides, pooled staff,
  litter, terminals/features and trusted provider delegates. Guest display IDs are not object IDs.
  Real Viewer sim/walk/needs/visitors/staff/calendar/management/awards/mailbox use existing DTOs.
  ParkSim allocate -> staff -> hydrate closes the bidirectional mechanic cycle.
- RuntimeState is restored BEFORE provider factories: a closure can hold the RNG object itself.
  Restoring RNG afterwards makes that closure point at the old seed while Viewer holds another
  object. A deliberately reversed order fails only at future hire tick40, not initial roundtrip.
- GuestPresentation captures actual actor/native/gait/idle/parts/previous pose/retained failure
  caches, shared asset bindings and actual record OBJECT relationships (not offsets alone).
  Native animation test retains two distinct Guest bodies with same display ID, and compares
  continued source against restored Viewer (parent changed worker's restored-vs-restored oracle).
- StaffPresentation captures actual member/serial/actor/native/record/Prev/Yaw/litter/hire linkage.
  Existing core _staff references from WorldCore are accepted only by identity. Parent retains the
  real litter AnimatedModel in `_litterDrawn` at creation and removes it at ordinary lifetime end:
  previously only the Node survived and a real load could not recover retained geometry without
  replay. No caller-made lookup is needed for normal litter anymore.
- Staff actor dictionary allocation holes are state: future hire/rebuild consumes shared animation
  RNG in dictionary enumeration order. SnapshotReferenceMap now supports explicit placeholder
  factories and nongeneric IDictionary tracking; staff/litter use unused pooled objects as temporary
  keys (never Hire/construct them). Queue/room callers supply plain ParkRide/object factories.
  Leading-hole fixture asserts the hole, then tests actual later insertion goes BEFORE the live actor.
- ActorWorld staging combines logical registry + both actual render joins. Guest capture recognizes
  staff/litter-owned child nodes rather than rejecting a normal staff root. Final sibling ordering
  preserves staff root BETWEEN guests and litter BEFORE staff, not constructor/default order.
  Failed later staff join disposes the entire unpublished stage and leaves live source untouched.

Tests: ViewerGuestSaveSmoke357PASS (real DATA assets + native/ordinary gait source continuation),
ViewerStaffSaveSmoke35PASS (real handyman/litter FILE +20native/PoseStaff updates),
ViewerWorldCoreSaveSmoke180PASS: actual Viewer seeded with real SPACE ride and saved rider aboard;
1000mixeddt visitor/staff/management updates, new arrivals/hire shared RNG, saved rider finishes;
actual Dispatch creates ride<->mechanic cycle then80updates; FILE ActorWorld join restores core,
guest/staff/litter graph and follows24sim/gait/staff-pose updates identically. Sibling order/free-slot
controls nonvacuous. Fixtures seed actual private Viewer fields without _Ready: NOT a player-loaded
park or proof that future lazy asset loading is wired. That distinction remains essential.

Seven mutations fail: staff-map holes, sibling order, guest Record identity, staff Prev, reverse
mechanic hydration, litter renderer registration, RNG/provider construction order. All restored.
Logs /tmp/tpw-world-mutations.log and /tmp/tpw-mut-provider-rng-order.log. Extra retained-source
controls changed after worker review; no failures hidden. Worker1 timed out after writing guest
files/smoke, worker2 delivered staff+tested, worker3 delivered core compilation only and NO smoke.
Parent ran/integrated all, wrote actual central/combined tests and fixes. All3 finished, NONEpending.

Gates: SaveAudit243950PASS,83PythonPASS. Actual render9021 and advisor49518 rerun green.
CLEAN code1bb17be gates: /tmp/tpw-save-world-matrix-clean4PASS+4exactretailreds,exit2,
landing_evidence=True; /tmp/tpw-save-world-runtime-clean10/11ONLYknownFANTASYanchor00e5cd9.
Earlier /tmp/tpw-save-world-matrix AND /tmp/tpw-save-render-matrix were dirty local gates with
landing_evidence=False. Corrected the previous render checkpoint's mistaken True metadata claim;
pass/failure results were accurate, but those runs were not clean-tree landing evidence.

NEXT (specific blockers, do not redo logical/actor owners): WorldCorePreflight STILL REFUSES active
native entrance/ride queues/advisor/bus. All their standalone DTOs exist; join them and provider
objects/callbacks into the registry, preserving controller lease/shared-pool identity and ordering.
Then actual advisor head/audio + bus presentation/placements + sound cold assets, remaining
security copies/stink/effects, camera/tools/scene roots, future asset libraries and _Ready bypass
for atomic publication. Guest/staff render joins currently require a trusted preloaded asset table;
source libraries are not a cold-load registry. Retired scalar/isolated VM rides supported in code,
but linked/spawning/native retired owners fail closed and were not exercised by this Viewer smoke.
Game settings remain GAME scope; current Award semantics need reconciliation at final game/park
root boundary. Save/Load stays disabled; no player session swap API yet.

## Native controllers + bus + advisor joined checkpoint (September 28, 2026)

Code48b835b, maine59fd64 mergedc91dded. TOTAL SAVE/LOAD STILL NOT PLAYER-READY. Native controller
blanket guards have been replaced by actual owner/hook checks and joins, not simply deleted.
WorldCoreState now includes entrance/ride-queue controller state, pending mailbox/request diagnostics,
shape cache, bus state and optional advisor state. Caller still must supply trusted asset/provider
bindings; these tests do not constitute a cold-start asset registry or a published player load.

Entrance/queue: capture exact StateOwner and NativeMotionInputs identities, including retained
bodies/rides. Allocate controllers and register their input objects BEFORE GuestGraph.Hydrate,
then hydrate leases/callbacks. Prior->entrance->queue hooks point only at fresh Viewer fields.
Normal EnsureNativeEntrance/EnsureNativeRideQueues and restore now use the SAME pure services and
tick-hook factories (not duplicate callback implementations). No Tick/route submission/Ensure is
called by restore. Worker initially used UnsafeAccessor to read private Services: parent REMOVED it,
added owner StateServices getters and uses those. No production private-layout access remains.

Bus: actual _nativeBus/_nativeBusMesh/catalogue/placement order and runtime cut witness, plus
fresh NativeBusState/AdmitBusBatch delegates. Early stage before entrance services; late node/ride
placement join after Park. No Bind/Present/startup RNG replay. Source bus must have been sampled
normally before cut. Physical RideSounds is NOT silently reset: default policy refuses sound-owner
presence unless caller supplies a separately captured/restored physical owner and fresh base
parameter provider. This registry is still missing from the total root. Bus Root stays detached
until final scene publication; StageActorWorld discard now frees it.

Advisor: actual scheduler producer/day getters, producer topology, core/ring/stack/head/lips,
physical voice state, cache nulls/failure latches and mixer fields. Parent added AdvisorStackView
owner: real smoothed scroll, allowed/visible/count cache and art/font bindings; Configure/providers
are NOT replayed. Last-draw diagnostic rects/blits are explicitly not future inputs and recompute
at next ordinary draw. HUD parent/layout is still outer-scene-owned. Source producer identity must
be the scheduler's ACTUAL producer, not a newly constructed imitation.
Stock sim/staff event/removal emitters are recorded and rebound only if present; ParkAdvisor.Attach
now uses named equivalent owner method delegates so identity is checkable. Restore does not call
Attach or emit anything. Non-stock emitter bundles fail closed. Settings subscription and physical
voice/global mixer publication remain commit-time operations; do not mistake saved bound flags
for a subscription already installed on the new world.

STAFF ORDER UPDATE (tinyclaw e59fd64): TickStaffAnimations/TickSecurity now consume ParkStaff.Members
newest-first, as the already-read console list specifies. Removed the now-unnecessary staff/litter
actor dictionary hole fields and their tracked-map wrappers; returned SnapshotReferenceMap to its
private core form. Core ParkStaff map-list order remains saved, as do scene sibling order and actor
identity. Do not resurrect the earlier dictionary-hole policy for staff RNG: that was a bounded
same-runtime preservation of the OLD port behavior, not claimed console fidelity or portability.

Parent tests:
- ViewerNativeWorldSaveSmoke: actual SPACE assets; FILE cut with pending entrance route/mailbox,
  live input lease AND occupied native ride queue, then118 actual bus/GuestWalk.BeforeStep chain
  updates match. First queue fixture failed because it never SendTo'd the ride; fixed the fixture's
  legitimate plan before Queue.Arrive. No bypass of the owner rule. Not a normal _Ready park run.
- ViewerAdvisorWorldSaveSmoke170PASS: central WorldCore FILE including real disc head,106rules,
  stock sim event/removal bindings, UI art/stack with nonzero scroll137,160updates+ordinary stack
  Pass. Active physical voice is NOT exercised through this join (standalone voice smoke covers
  it); no populated staff/visitor advisor producer scenario claimed.
- ViewerBusWorldSaveSmoke112PASS: eight real bus assets, FILE actual fields/new callbacks/placement
  node identities, safe-point refusal and explicit physical-audio refusal. Tests use inert old bus
  callbacks to prove restored callbacks target NEW Viewer. Constructor-owned Viewer node cleanup
  fixed by parent; final smokes have no leak warning.
- Actor/core176PASS, guest357PASS, staff35PASS after the ordering-policy change.
Six VALID mutations fail: prior hook, pending mailbox, bus callback, advisor removal emitter, stack
scroll, native input identity. The first input-clone mutant did not compile (invalid ctor), so it
was NOT counted; corrected to `with {}` (same values, different identity), which fails hydrated
lease identity. All mutants restored. /tmp/tpw-controller-mutations*.log; native/advisor/bus logs
/tmp/tpw-native-final-*.log. Three delegates done (bus worker timed out with code/test complete),
parent integrated/reviewed/extended tests and owner APIs; NO workers pending.

Gates: SaveAudit243950PASS;83PythonPASS. CLEAN48b835b /tmp/tpw-save-nativejoin-matrix-clean:
4PASS+4exactretailreds,exit2,landing_evidence=True. /tmp/tpw-save-nativejoin-runtime-clean:
10/11 ONLYoldFANTASYanchor00e5cd9. Normal entrance factory refactor covered by those runtime gates.

NEXT real blockers: cold trusted asset/provider manifest for actual Viewer captures (not fixture
factories); whole scene/placed ride/track/coaster render joins; physical RideSounds/other audio
roots; security copy/stink/effects, camera/editing tools/future library loading. Park placement,
PathTool and render/walk grid aliases must be one resource graph, not independent resolver clones.
Advisor ring references to retired rides still need registry closure supplied via retained objects;
spawning/linked/native retired owners still refuse. GameSettings/HiddenAwards GAME scope remains
separate. Then staged startup path (_Ready MUST NOT reinitialize), atomic publication, UI Save/
Load file flow and full normal-play scenario tests. Do not enable buttons from these fixture passes.

## Cold assets/audio + scripted placed scene checkpoint (September 28, 2026)

Code2f91100. Still NOT a total player load; root auto-census, native vehicle scenes and several
physical/UI/effect roots are incomplete. This checkpoint moves beyond preloaded source assets:
SaveAssetRegistry manifests full-disc hash/length plus exact app-approved WAD/member/kind/content
IDs. Cold Open uses the APPLICATION'S trusted disc path and origin policy, never file paths or
byte offsets from the save. Original bytes stay private; shared immutable objects and explicit
private Model copies are distinct. Verified complete sibling directories are opt-in: no silent
spawn enable/disable. Keep registry alive while loaded-world resolvers use it; not just until stage.

Parent extended worker's SAM-only loader with explicit CompiledDefinition kind: separate asset ID,
existing CompiledAssets.Attach using fixed DATA/arsdb.dba + EUR identity-key tree, full-disc hash
covering those inputs. Raw payload/Shop settings must match the actual source compiled definition.
Other regional/custom compiled joins are not inferred; mismatch fails. First test used language
"eng" where TextDatabase.Load expects region "eur"; nonvacuous compiled-entry check caught it,
fixed to the same region the Viewer uses. Uncompiled definitions stay uncompiled.

SaveSoundRegistry: cold verified MAP/SDT file closure, fresh catalogues/events/stream decode using
RideSounds' existing decoder helper (temporary detached decoder, no gameplay/audio playback).
Viewer.SoundState captures actual _sounds + explicit moving/base-parameter provider IDs, stages a
stopped shell, publishes voices only once. New registry survives for later catalogue lookups.
It is NOT YET joined into central WorldCore/ActorWorld. Explicit refusals remain for ToolSounds,
browser player, advisor voice handled elsewhere, coaster parameter hooks. Null decoded-stream
fingerprints validate, but a genuine null-decode fixture was not available; not claimed tested.
No source CaptureBindings assets are reused by cold Open. Parent fixed smoke's leaked constructor-
owned Weather/Flags/Thoughts nodes; final cold-sound smoke is warning-free.

Viewer.PlacedSceneState operates actual _scripted/_building/_rideMeshes/_current associations and
Park.Placed holder trees; distinct placed/runtime IDs, complete renderer subtrees via AnimatedModel
snapshots, ordered children/transforms/visibility. Parent added bounded represented_activation_serial
metadata (restored without Activate). Unknown metadata/static mesh inputs still refuse. Native
tracks/coasters and seaplane/ferry scenes are explicitly refused, NOT silently lost:
- TrackModel currently discards non-Flowing AnimatedModel owners.
- CoasterPylon discards its AnimatedModel owner.
- Vehicle RseModelPresenter retained renderer is not exposed.
Those lifetime owners must be retained before complete scene capture is possible. Active native
track/coaster tools, standalone preview _current, and unknown subtrees also refuse.

Tests (parent-run):
- SaveAssetRegistrySmoke36PASS: real ride/character/terrain/model/APS/SAM/RSE, compiled DBA payload,
  manifest FILE -> new registry, shared/private identity, missing/mislabelled/hash/policy refusal,
  unchanged disc length/timestamp. A SELF-CONSISTENT forged ID/hash pair tests actual content
  verification, not just metadata inconsistency; an existing raw asset outside the exact policy
  tests allowlist scope. No extraction.
- ViewerPlacedSceneSaveSmoke: ONE FILE containing cold manifest + ParkSim + Park geometry + actual
  scripted scene. Load uses ParkSim.FromState, not second Add/Create (worker's original fixture did
  that; parent replaced it). Real Crazy Ape, placed41/runtime709, activation123, six subsequent
  sim advances/render samples identical. No source asset resolver supplies the loaded scene.
  Textures intentionally null, no full terrain rendering/normal Viewer._Ready scenario claimed.
- ViewerSoundWorldSaveSmoke370PASS: real graph repeats/sustain/fade, fresh audio manifest/file,
  actual Viewer _sounds join, paused cursors/timers and250futureticks. ShellViewer deliberately
  suppresses _Ready; not the final player publication path. Integrated bus/audio chain not yet tested.
- WorldCore176PASS unchanged. Six valid mutants fail: actual-disc content verification, compiled
  join, private Model isolation, activation metadata, silent track omission, sound loop decoding.
  All restored; /tmp/tpw-cold-mutations.log. Three delegates finished, no pending worker.

Gates CLEAN2f91100: SaveAudit243950PASS,83PythonPASS; /tmp/tpw-save-cold-matrix-clean4PASS+4exact
retailreds,exit2,landing_evidenceTrue; /tmp/tpw-save-cold-runtime-clean10/11ONLYoldFANTASYanchor
00e5cd9. origin/main still e59fd64; cow perf3fd56e2 is on its branch, NOT merged here yet.

NEXT concrete work: retain TrackModel non-flowing and CoasterPylon/vehicle render owners, snapshot
native scene interpolation/procedural inputs; auto-map actual Viewer assets/origins/providers into
cold manifests; central physical sound join plus ToolSounds/browser/voice scope; security particles,
camera/editing tool state. Preserve geometry/render/walk grid aliases and actual compiled defs.
Then one staged normal-world bootstrap WITHOUT _Ready/Create replay, atomic publication and UI.
Do not redo cold registries or logical controller DTOs. No Save/Load buttons enabled.
