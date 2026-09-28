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
| ParkFinances | explicit DTO:balance/flags/totals,144-slot income/wage rings,period,wage accumulator,category ledger |
| ParkAwards | explicit DTO:tickets/earned tickets,stars,medals,hidden flags,medal mapping; GAME scope |
| NativeRoutePool | explicit DTO:all1000words,hint,availability,generation; fresh pool factory,not beneath existing leases |
| NativeActivationSequence | explicit DTO:serial/origin/verified marker/total/per-kind counts |
| Terrain/placements/paths/queues/tracks | pending; asset IDs and edits, ownership/IDs, no raw resources |
| ParkSim/ParkRide/RSE/hosts | pending; time/carry,handle allocator,settings,queues,full VM stacks/PC/yields/deadlines/children and shared hosts |
| Guests/needs/visitors/routes/entrance/ride queues | pending; exact guest objects and memberships,leases,allocator cursors,request tokens/results,decision timers |
| Staff/litter/research/management | pending; candidates/pools,jobs/patrol/routes,month/weekly stamps,strikes,projects/progress,room occupancy |
| Track/coaster operation | pending; track topology + cars/trains/riders/progress/speed/timers/state, not respawn |
| RNG streams | pending; all streams and shared-provider identities; saving a seed alone is NOT continuation |
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
