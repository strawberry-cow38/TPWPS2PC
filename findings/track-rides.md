# Track rides (karts and water rides): how they work

Researched 2026-09-26 for strawberry, in two passes:
- **the "what"**: which rides exist, their files, the objects;
- **the "how"**: the mechanism in the code, at a level you can reimplement from.

Sources:
- the rides' own `.sam`/`.rss` on the PS2 disc;
- `SLES_500.32`, decompiled in Ghidra 12.1.2 with the ghidra-emotionengine-reloaded extension (R5900);
- every load-bearing function checked against raw MIPS with `tools/r5900dis.py`.

Addresses are the executable's. **READ** means seen in the code. **INFERRED** means reasoned to.
The code says what the game does, not why its designers chose it, so nothing here claims intent
unless the shipped files state it.

The detail is in four files:

| file | covers |
|---|---|
| `track-ride-geometry.md` | waypoints to pieces, the piece chooser, bridges, crossings, add-ons, meshes, the per-piece sample table, pose along the track |
| `track-ride-cars.md` | the car object, the per-tick car scheduler, the boat and kart steps, the kart state machine, the RNG, the drive-it-yourself kart race |
| `track-ride-operation.md` | the ride's status machine, boarding, launch, laps, unloading, wear, breakdown, repair, Excitement, after-ride scoring |
| `track-ride-tool.md` | the player's track tool: modes and buttons, the leg preview, the per-cell rule, the validity rule, prices and refunds |

## The short version

- **Two parts, one player-built.** A track ride is a **station** the player places, plus a loop the
  player **draws** from the station's exit back to its entry.
  - The loop is drawn as axis-aligned legs, in 2-cell steps.
  - On every edit the game throws the track away and re-lays it as 2×2 **pieces**: straights,
    bends, crossings where the loop crosses itself, and bridge pieces where it crosses a path.
  - A track has at most 36 pieces and 34 waypoints.
- **Valid means closed.** The last waypoint must be exactly the station's entry cell. There is no
  other validity check: everything else is enforced cell by cell while drawing, so an invalid shape
  cannot be committed. An open loop leaves the ride in status 3 (closed); a closed one puts it in 2
  (running).
- **The script is dead code.** Each ride ships a script (`GoKarts.rss`, `Wateride.rss`), but on PS2
  its `BUMP` handler is a stub. A native C++ class (`CTrkRide`, from its debug strings) runs the
  whole ride instead.
- **Boarding.**
  - The ride takes **one guest every 20 ticks**, and each guest gets **their own car**.
  - When the riders reach the Capacity setting (default 4, slider 1–8), all cars leave together.
  - There is **no timeout**: a ride that never fills never leaves.
- **Laps and unloading.** Each car drives from its grid slot to the line, then **Duration** laps
  (default 5, slider 1–10). It is then unloaded within 10 ticks, and the ride loads again once the
  last rider is off.
- **Two car classes by park.**
  - **Boats** surge (random target speed), wobble sideways around the centre line, queue behind
    each other, and give way at crossings.
  - **Karts** race: a staggered grid, overtaking into the other lane, aggressive karts blocking,
    spin-outs and stalls on contact, and a finishing order.
- **Breakdowns are deterministic.** Wear accrues while cars are aboard. The ride breaks the moment
  reliability falls below 10.0 of 100, and a mechanic's arrival dumps every rider at once.
- **The only way the track's shape reaches guests** is a weight sum in Excitement: crossings and
  add-ons count 4, humps and ramps 2, everything else 1.
- **Pose is a baked table.** A car's 3D pose comes from 4 samples per piece, computed once when the
  piece is built. The car interpolates linearly between samples and across the lane.
- **Height is flat.** The track does not follow the ground: laying it flattens the terrain.
  Heights change only on bridges and add-ons.

## The rides and their files

Per world, one kart ride and one water ride, `AssetKind` 6 (TrackRide) in the compiled database.
Each park offers exactly one:

| world | karts | water |
|---|---|---|
| JUNGLE | Dino Karts (park 1) | Splish Splash (park 2) |
| HALLOW | CryptKarts (park 2) | Ooze Crooz (park 1) |
| FANTASY | Bumble Buggies (park 2) | Taptastic Rapids (park 1) |
| SPACE | Space Racers (park 2) | The Blobulator (park 1) |

Park = which of the world's two terrains offers it, from each park's native attraction list
(`NativeBusCatalogue.Keys(6)`). The executable indexes world by `0x3952e4` and park by `0x3952e8`
(0 = park 1).

**Files.**
- `/Rides/GoKarts/` (JUNGLE) holds:
  - the station `gokarts.mps` (+ `PGOKARTS.mps`, a preview);
  - track pieces `gk_trcks, b, b2, h, h_u, h_a, h_d, x, q, v` (`.mps`, most with an `.aps`);
  - four kart colours `gk_blue/green/orange/purple`;
  - `GoKarts.rse`/`.rss` and `EventMap.rse`/`.rss`.
- `/Rides/Wateride/` has the same set as `wr_trck*` (plus `x2`, `s1`) and a `wr_ring` boat.
- The other worlds reuse the names at other resource ids (`track-ride-geometry.md` §6).
- **Never drawn:** `q` and `v` are registered, but no piece shape selects them. `x2` and `s1` are
  not registered at all.

**`.sam` (Dino Karts / Splish Splash).**
- Station footprint: karts `****/****/*2N*` (4×3), water `****/****/****/*2N*` (4×4).
- `Bumper.WhichTrackType` 1 = car track, 2 = water track. `Bumper.BumperType` −4 is
  `RIDETYPE_LOSTWORLD_GOCARTS`, −5 is `RIDETYPE_LOSTWORLD_WATERTRACK`.
- `Bumper.{North,East,South,West}{X,Y}Adjust` are the station's return-point offsets. The
  executable has them as **hard-coded immediates** in `0x2001a8`, and they are not in the compiled
  record.
- Capacity "by upgrade" 4/6/8 (karts) and 2/3/4 per boat (water), and `Info.RunsContinuously 1` on
  the water ride. **Neither is honoured natively** (below).

**Event map (sound).**
- `EVT_KARTSTART`, `EVT_KARTSTOP` and `EVT_TOOT`, with parameter `JUNGLE_ENGINE_REVS`. The water
  script loops `EVT_RIVER`.
- The native cars play a looping event from bank 6 (event 4). Its parameter slot 4 is set to the
  car's speed as a percentage of its target, and a one-shot (6, 0xf) plays on overtaking and on a
  spin.
- The event names behind those ids are resolved at run time and not mapped yet.

## The script layer, and why it doesn't matter

**`GoKarts.rss`, in order:**
1. Wait for `BUMP_ISTRACKVALID`.
2. `BUMP_OPENRIDE`.
3. Board one guest at a time with `PEEPON` and `LAUNCHCAR`, resetting a 10 s timeout on each
   boarding.
4. `WAIT 2000`, then `SETLAPS VAR_DURATION` and `STARTRACE`.
5. `PEEPOFF` to unload.
6. `HALTRIDE` on an invalid track or a closed ride.

**`Wateride.rss`:** runs continuously with up to 64 boats. It fills each boat to `VAR_CAPACITY` and
launches it on its own.

**`BUMP` is a stub on PS2.** It is handled at `0x1c1370`, where every selector discards its argument
or writes 0. That alone doesn't explain why the rides still work. The rest of the reason is that:
- the ordinary ride's boarding and unloading routine `0x1166a8` talks to the script through
  VAR_LETMEON, VAR_ONRIDE and friends;
- the track ride **overrides every status tick that would call it** (`track-ride-operation.md` §9);
- so the script is never consulted.

The port currently places the station only, and its script VM waits forever on `BUMP_ISTRACKVALID`.

**Where native departs from the script** (`track-ride-operation.md` §8):
- no 10 s timeout;
- no `WAIT 2000`;
- one guest per boat, not 2–4;
- boats run in batches like karts, not continuously;
- no 64-boat cap;
- a closed ride keeps driving with nobody getting off until it reopens.

## How it works, piece by piece

### 1. The player draws a loop (`track-ride-tool.md`)

**Tool modes.** 7 places the station, 8 draws, 9 edits ("Edit Track" in the ride's list box), and
10 drops an add-on onto a straight.

**Buttons.**
- **Cross** adds a waypoint.
- **Circle** undoes the last one.
- **Square** clears back to the exit.
- **Triangle** finishes in mode 8, and restores the saved track in mode 9.

**The leg preview.**
- A leg runs from the last waypoint along the cursor's dominant axis, snapped to 2-cell steps.
- The preview paints each 2×2 block blue (OK) or red (blocked).
- The loop-closing block gets its own tile, and an arrow marks the station entry.
- "Track Stock" shows 36 − pieces, and "Cost:" shows the leg's price.

**The per-block rule `0x14a248`.** Every 2×2 block of a leg must:
- be inside the grid;
- be within ±85 height units of the station's level;
- be empty land, a footpath, a queue, or one of this ride's own **plain straights** (which makes a
  crossing).

A leg's **end** block must be empty land, the station entry, or its own start. There is no message
for an invalid track: the feedback is the tile colours and the sounds.

**Prices** are per piece, from compiled record `+0xcc`:
- 35 for Dino Karts, Splish Splash and Space Racers;
- 40 for CryptKarts, Bumble Buggies, Taptastic Rapids and The Blobulator;
- 45 for Ooze Crooz.

Every money change is price × 10. Undoing refunds in full, and demolishing refunds half the station
price and nothing for the track.

### 2. The game lays pieces (`track-ride-geometry.md`)

**The rebuild `0x2009c0`**, run on every edit:
1. Unload every car.
2. Clear the pieces.
3. Lay 2 or 3 fixed station pieces from `0x2ee54a` (the station, a hidden connector and, while
   there is only one waypoint, an exit straight).
4. Lay one leg per waypoint pair (`0x200cd0`). A leg is 2×2 pieces every 2 cells, starting **on**
   its first waypoint.
5. Build the sample tables lazily, one piece per ride update.

**Per piece, `0x200fb8` picks in priority order:**
1. **An add-on** (types 40–47): the ride's up to 3 bought upgrades, such as the mammoth tunnel or the
   lava jump.
2. **A crossing:** the new piece lands exactly on an existing piece's anchor. Both are retyped; one
   draws the `x` mesh and the other is invisible.
3. **A bridge:** a path, queue or kind-7 tile is under the block. One obstacle gives `h`; a run gives
   `h_u`, then `h_a`…, then `h_d`.
4. **A straight or bend** from the 16-entry corner table `0x2ee518`, indexed by the previous piece's
   exit direction and the new direction. `b` and `b2` are the two bend hands.

**Pose.** When a piece is built, `0x1fdba8` bakes **4 samples** (one per 64 distance units). Each
sample holds:
- two lane points (lanes 96 units either side of the piece centre line);
- a height: 80 flat, 256 on bridge decks, per-park offsets on add-ons;
- a yaw.

Bends are quarter circles of centre radius 256, and their sin/cos is evaluated **only at bake
time**.

**A car's position is `car+0x50`** (256 per piece) plus a lateral 0–256. Its world position is two
lerps: across the lane, then between the two samples 64 units apart.

### 3. Boarding, running, unloading (`track-ride-operation.md`)

**Status byte `ride+0xa2`:**
- 2 running, 3 closed;
- 4 broken, 5 broken with reliability 0;
- 6 being repaired, 7 repaired;
- 10 loading, 11 unloading.

**The cycle.**

| status | what happens | exit |
|---|---|---|
| **10 loading** | Every 20 ticks, if riders < Capacity and the head of the queue is standing at the front, `0x201ac0` boards them into a **new** car. The "room in the last car" test `0x205638` is `return 0`. Cars wait at their grid slots, unstepped. | riders ≥ Capacity → **2** |
| **2 running** | Cars step every tick. The run timer counts up. | 2 × Duration ticks → **11** |
| **11 unloading** | Cars keep driving. Every 10 ticks, cars that finished their laps are unloaded to the exit. | last rider off → **10** |

Guests may choose the ride in 2, 10 and 11.

**Wear, breakdown, repair.**
- **Wear** is applied every 4 ticks while cars are aboard and the ride isn't loading. The rate
  combines Speed, load, pieces/30 and a per-tier factor of 5/3/2.
- **Breakdown:** below reliability 10.0 the ride is forced to status 4 every update. That empties
  the queue, while the riders already aboard keep driving and get off as they finish.
- **Repair:** a mechanic's arrival (status 6) throws every rider off. After 240/180/120/60/60 ticks
  by mechanic level, reliability goes back to 100 and the ride returns to loading.
- Life (condition) is lost with wear and is never restored.

**Excitement** is `(base + Σweights/2) × speed factor × duration factor`. Each factor is clamped to
0.75–1.25, and the result is capped at 100.
- The base is 75 or 80 by ride.
- After the ride, a guest's happiness rises by 15/10/5 depending on how close Excitement is to their
  taste.
- Sickness rises only above Excitement 55.

### 4. The cars (`track-ride-cars.md`)

**The car scheduler `0x2023b0`**, every tick:
- assigns each car its piece;
- sorts cars by total distance `+0x54` and sets their rank;
- runs each car's neighbour hook, then each car's step, with its sorted neighbours behind and ahead
  (cyclic).

**Boats** (vtable `0x36bfb8`).
- **Speed** is in quarter units, and a boat moves speed/4 per tick. Speed climbs 1 per tick to a
  target; on reaching it, the target re-rolls to `60 − 2·rand(11)` and speed drops to
  `target − rand(11)`.
- **Lateral wobble:** the boat wanders ±10 around the centre line, at 2 per tick.
- **Queueing:** within 200 of the boat ahead it loses 5 per tick, and within 25 it stops and backs
  off 10.
- **Crossings:** only the boat furthest in may move.
- There is no water current.
- **Rocking is never drawn.** Two tilt angles are computed, but only yaw and position reach the
  model.

**Karts** (vtable `0x36c0a0`) run a 7-state machine: grid, yield, race, overtake, block, spin and
stall.
- **Grid:** 100 units per slot behind the line, lanes at 64 and 192.
- **Overtaking is deterministic.** 100–299 behind the kart ahead, a kart swings to the other lane
  (50 or 205) with double acceleration.
- **Contact:** under 100 behind and within 20 sideways, a kart spins (at speed ≥ 21, one full turn,
  frozen) or stalls.
- **Blocking:** an "aggressive" kart (1 in 5) whose follower is overtaking blocks it on a 1-in-11
  roll.
- **Finishing:** finished karts park `(5 − rank)·100` past the line.

**The drive-it-yourself race** (compiled minigame selector 7).
- It swaps the guests' cars for 5 karts, and the player drives the last one on the grid.
- Up and down change speed by ±4; left and right step through 5 lanes.
- AI karts get +2 speed per tick while the player leads.
- The first win per save pays one unit of a counter that is probably golden tickets.

## Corrections to the first pass

The first version of this file got these wrong:
1. **`vt+0xc4` is not a validity check.** Vtable entries are 8 bytes (`{s16 delta, s16, fn}`), and
   `+0xc4` is the base `0x1e2830`, "is broken down (status 4/5)". Statuses 2/3 are running/closed,
   picked by the loop-closed flag `+0x1c6`.
2. **A guest never joins an existing car.** Every boarding creates a car.
3. **Boats have no height bob.** The ±10 is lateral wobble, and the tilts are never drawn.
4. **Kart overtaking is not a 1-in-11 roll.** The roll is an aggressive kart *blocking* an
   overtaker.
5. **There are 36 piece slots, not 35.** The constructor loop runs 36 times.
6. **Types 40–47 are the bought track add-ons**, not stations. The Excitement weights also cover
   types 48–51.
7. **`0x204c38` is the car's draw, not the piece-mesh selector.** Piece meshes come from the type
   table's shape byte, through `0x2ecad0[world][park][shape]`.

## Where the four detail files were reconciled

- **Add-on tables.** The per-park tables overlap: one park's kind-1 rows are another park's kind-0
  rows. The tool file reads three kinds per park straight through the overlap; the geometry file
  reads which rows have meshes. Which catalogue entry becomes which kind index is **unresolved**, so
  whether, say, Dino Karts could be offered the water tunnel is open. Space Racers' table pointer is
  NULL: placing an add-on there would crash.
- **Station footprint.** The station building is the ride's own 4×3 model. Its **piece** occupies
  2×2 tiles, but its samples sit in a 4×4 local frame. That is why the connector after a station
  shifts its lanes by −256.
- **Run length.** Status 2 lasts only 2 × Duration ticks, so a ride spends almost all of its run in
  11. Boats only finish outside status 2, which fits.

## Still unknown

- **The wall-clock rate of a tick.** The ride update runs once per simulation pass, but the pass rate
  is not established; the port has the same gap in `native-shop-flow.md`. Every time above is in
  ride updates.
- **Height units.** Terrain height is byte × 4, an object's y is `+0x86 << 8`, and a piece mesh's y
  is `+0x86` raw. These disagree. The tool's ±85 check compares terrain × 4 against station y × 256,
  so its "two build levels" reading is unconfirmed. This needs a live savestate.
- Which physical buttons drive the race's confirm/back masks. The sound event names.
- Mechanic dispatch.
- The add-on catalogue mapping (above).

## In the port

Built 2026-09-26:
- **Core.** `core/TPW.PS2.Data/TrackLayout.cs` holds the tables, the rebuild, the piece chooser (crossings, bridges, corners; add-ons not yet), the sample bake and the double-lerp pose. `TrackRideSim.cs` holds the status machine, boarding, laps, unloading and both car classes. `ParkSim.AttachTrack` hooks a track into a placed ride: the cars take guests straight from the ride's queue and hand them back through `Left`, and the stubbed script handshake is skipped.
- **Viewer.** `game/Viewer.TrackRides.cs`:
  - placing a track ride opens the track tool before the queue tool, as on the console;
  - legs are previewed with the console's own tiles (165 ok, 175 blocked, 173 closes, 166 entry arrow) and priced per piece from record `+0xCC`;
  - pieces and cars are drawn from the ride's own folder at the console's model origin and yaw;
  - "Edit Track" in the ride menu reopens the loop.
- **Checks.**
  - ParkSimAudit `--track-rides-only` runs 43 `track ride:` checks, with controls. Three deliberate mutations turn them red: the b2 sampling, the connector shift, and a boarding timeout.
  - The viewer matrix scene `trackride` (`TrackRideSmoke`) builds, draws, boards and unloads a loop on the real viewer.
- **Riders and sound** (2026-09-26, second pass):
  - Each rider's head sits on the car model's seat fitting 1 (`0x205568` seats guest *n* on fitting *n+1*), through the same `SeatPose` the scripted rides use.
  - Every car plays native category 6 (`AUDIO/RIDES/trck`) event 4, `Engine.mp2` from `TRACKHD.SDT`. The console restarts it whenever it has stopped (`0x111CC8`, then vtable `+0x34`), and so does the port.
  - Karts play event 0xF when they start an overtake or a spin.
  - ⚠ Parameter 4 (speed × 100 / target) is set every step, but what the audio object does with it is not read, so pitch doesn't follow speed.
- **Not yet.** Add-ons; wear, breakdown and repair (the port has no mechanics); the drive-it-yourself race; laying a path under an existing track.
- **Kept, because the console has them:**
  - the b2 bend's jump and stall;
  - one guest per car;
  - no boarding timeout;
  - Running lasting 2 × Duration updates.

## To build it in the port

In dependency order:
1. **The track tool:** waypoints on the 2-cell lattice, the per-block rule, prices.
2. **The rebuild:** the station table, legs, the piece chooser, crossings, bridges.
3. **The per-piece sample bake**, and pose by double lerp.
4. **Native boarding and unloading** on the status machine, replacing the script's `BUMP` path
   entirely.
5. **The two car classes** on the park tick, with the scheduler's sort and neighbours.
6. **Laps, Duration, wear and breakdown.**
7. **Excitement's track term.**

The kart race is separate and optional.
