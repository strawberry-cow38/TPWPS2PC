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
