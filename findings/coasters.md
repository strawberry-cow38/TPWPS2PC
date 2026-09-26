# Roller coasters: how they work

Researched 2026-09-26 for strawberry, as a "what" pass and then a "how" pass in four areas. Sources:
the coasters' own files on the PS2 disc, and `SLES_500.32` decompiled in Ghidra 12.1.2 with the
ghidra-emotionengine-reloaded extension, every load-bearing constant checked in raw MIPS. Addresses
are the executable's. **READ** means seen in the code or data, **INFERRED** means reasoned to. The
code says what the game does, not why its designers chose it.

| file | covers |
|---|---|
| `coaster-survey.md` | the "what": the 14 coasters, their files, the script layer, `coaster.sam`, the class, the vtable, the fields |
| `coaster-geometry.md` | nodes, the spline and its frame, loops, the 7 mesh styles, the lift flag, pylon posing, units |
| `coaster-building.md` | the tool modes, the placement rules, closing, validity, the station link, the save record, the `coaster.sam` search |
| `coaster-trains.md` | the train and car objects, the station state machine, the physics step, spawning, boarding and unloading, sounds, the ride camera |
| `coaster-operation.md` | the status machine, wear, the test lap, the rating and the Ultimate award, the ride value, the Rollercoaster Test Park, upgrades |

## The short version

**A different machine from track rides.**
- A coaster is a **ring of up to 32 player-placed pylons** between two fixed station nodes. There
  are no piece meshes and no piece table.
- The track between pylons is a **uniform Catmull-Rom spline** (tension ½) through a 4-node window,
  sampled at 17 points per segment. One of **7 hard-coded cross-sections** extrudes it: flat ribbon,
  trough, rope, triangular beam, big tube, twin rails or mine trough.
- Invalid segments swap every texture for `red.ssh`.

**Real elevation, player banking.**
- Each pylon has a height from 0 to 1280 in steps of 20, and a bank of ±45°.
- Pylons stand on the terrain and can stack two to a cell.
- The track frame comes from each node's side vector, built from the bank the player set. There is
  **no auto-banking**.
- A **loop** is a node kind: a lead-in plus a loop node one cell to the side. It is an explicit
  formula, a true circle of radius 3 cells, and 5 of the 14 coasters can't have one.

**Building** (`coaster-building.md`).
- Mode 11 places the station, 12 lays pylons, and 13 edits a pylon's height and bank. While laying,
  every pylon inherits the start node's height and bank.
- Each pylon must be 3–8 cells from its neighbours, with a turn under 90° (under 45° off the
  station), on empty land or stacked on its own pylon.
- There is no slope limit.
- Each pylon costs 100 (×10 in park money), and demolishing the coaster refunds nothing for pylons.

**Trains run on gravity** (`coaster-trains.md`).
- A coaster runs 2–6 trains of 1–4 cars; the count is `clamp((pylons/3 + 2) / cars, 2, 6)`.
- Each car gains **0.04 × the height it drops** per step, and the train takes the mean of its cars.
- Friction is 0.1 % per step, or 4 % on the approach and station segments. There is no speed cap.
- **Lift:** below 0.02 the train is held at exactly 0.04 until gravity takes over. So the lift is
  found by physics, not placed; a test train marks it for the mesh to draw a chain.
- A loop holds the speed the train entered with.
- A train waits while it is less than 0.75 segment behind the one ahead.
- At the station, the train stops by position, unloads one rider every 11 ticks, then boards one per
  11 ticks, and leaves when the next train is waiting behind it.

**As a ride** (`coaster-operation.md`).
- Status 2 and 10 are the same state, and 11 never occurs.
- Trains keep running through every status except 5 (reliability 0), where they freeze with their
  riders.
- Wear has no track term.
- **The Speed setting never reaches the trains.**
- The ride value is `min(100, base × speed factor)`. **The track's shape reaches guests nowhere.**

**The test lap and the Ultimate award.**
- Finishing the track runs one train. From its first car it records duration, length, max speed,
  drops, steepest drop, and +g, −g and lateral g.
- There is **no inversion or airtime count**.
- **"Ultimate Rollercoaster"** needs lateral g < 0.5, speed 55–70 and 2 or 3 drops. Otherwise the
  ride gets one of 27 rating texts.
- The stats feed only the stats screen, the rating and the award.

**The Rollercoaster Test Park.**
- It is **JUNGLE `terrain_1`**, at park index 2. Its shared textures come from DATA.WAD's
  `/Ultimate/Sharetex` (488 textures covering every coaster), because the code asks for
  `data\ultimate\sharetex` when `park == 2`.
- The registry loader `0x17d7e8` shows park 2 only the entries flagged `0x8`: all 14 coasters with
  their pylons and cars, JUNGLE `terrain_1` (the only terrain with the flag), a small jungle scenery
  set, and the Super Bog, which nothing else loads.
- The front end offers it once any Ultimate award is saved (memory-card save `+0x108`).
- Building there is free, there is no wear, no award can be earned, and item *i* of every build
  category shows only when coaster *i* holds its award.

**The script is dead code again.** Every coaster's `.rss` uses `COAST` sub-opcodes, and the PS2
handler `0x1c14e0` is a stub like `BUMP`. `coaster.sam` is the PC's track and physics description;
nothing on PS2 reads its schema, and the native per-coaster tables disagree with it.

## The coasters

14 compiled records of `AssetKind` 1. A normal park allows 2 coasters, and the test park all 14.

| world | park 1 | park 2 |
|---|---|---|
| JUNGLE | Temple of Gloom | Chak Atak, Gorilla Thrilla |
| HALLOW | Hades, Dare Devil | Scatty Batty, Ghosta Coasta, Bone Shaker |
| FANTASY | Big Dripper, Caterpillar Coaster | Candy Coaster |
| SPACE | Moonshot | Escape Velocity, The Shocker |

`coaster-survey.md` §1 has, per coaster: key, folder, base excitement, footprint, mesh style, cars
per train, car spacing, whether it can loop, the station heights, and the sound family.

## Corrections across the files

The four "how" files correct the survey in these places:
- **Car vectors:** `+0x14` is the side vector, `+0x20` up and `+0x2c` forward (trains §1, operation §9).
- **Node fields:** the pylon model is at node `+8`; `+0x5c` is the track mesh (geometry §1).
- **`+0x148`** means "ring closed", not the ride's open/closed status (operation §9). The survey's
  "closure flag" wording stands.
- **The `coaster.sam` schema** starts at `0x2acee0`, one record earlier than the survey says
  (building §8).

## Findings for the rest of the port

These came out of the coaster work but reach beyond it:
- **Fitting → node.** The coaster code resolves a model fitting to a node as `fitting index + header
  u16 @+0x34` (MIPS `0x19a4dc`). Below the mesh count that is a mesh record; above it, a helper.
  - The port's `Model.Fittings` uses `mesh count + index`.
  - The two agree on `monkey.mps` (9 and 9) but differ on 107 of the 360 models with fittings, for
    example gokarts.mps and wateride.mps.
  - The general accessor `0x1f2978` reads a prebuilt table whose builder is not read yet, so whether
    every model follows the same rule is open. (geometry §4.4)
- **Additive animation.** `0x1a7f48` adds an animated path translation to the bind pose, rather
  than replacing it, for models with header flag `+0x1c & 4`, and the morph player adds its keys to
  the vertices. The port implements it as `AnimatedModel.Additive` (see "The pylons" below). (geometry §4)
- **`0x2b72a8` is the test-park flag.** `track-ride-operation.md` lists it as an untraced "global
  switch" that zeroes wear; it is the same flag. (operation §8.1)
- Bone Shaker draws Ghosta Coasta's pylon, because registry id 435 names the `coasta` folder.
- Caterpillar's fourth seat has no attach point, so that rider is probably not drawn.

## Still unknown

- **How pylon height maps onto track height.** The mechanism is read (the additive loft animation,
  0.9 units of rise per unit of height), but the numbers rest on one inference. They fit 10 of the
  station models' rail heights and miss 3. One node read from a live savestate would settle it.
- The wall-clock rate of a tick (as for track rides). The stats screen counts 1/30 s a step.
- What ground markers 171 and 174 (the pylon field) look like. The earlier ghost-cursor research
  found the game never loads them.
- The test lap's first loop comparison reads an uninitialised FPU register (`$f21`) left by its
  caller.
- The sound event names; the unused sound parameters.
- DBA `+0xc4..+0xcf` and `+0xd2`.

## To build it in the port

In dependency order:
1. The node ring with its Catmull-Rom window and the side-vector frame.
2. The loop formula.
3. The 7 cross-section extruders, with the `red.ssh` invalid swap.
4. The tool: the three modes, the placement rules, closing, and costs.
5. The trains: the physics step exactly as written, including the look-ahead quirk at segment joins;
   station boarding on the 11-tick cadence; spawning.
6. The test lap and the rating.
7. The test park, which needs the registry's `0x8` flag and the `/Ultimate` textures.

The coaster's status machine and value are small once those exist.

## In the port (2026-09-26)

Built, in `core/TPW.PS2.Data/Coaster{Track,Sim,Mesh}.cs` and `game/Viewer.Coasters.cs`, held by 37
ParkSimAudit checks (`coaster:`) and the `CoasterSmoke` viewer scene:
- **The station and its queue.** The station's doors are DBA `+0xc`/`+0x10` (`0x1e1760`, `0x1e1a48`,
  the same record every ride's doors come from), which is the station's own `<name>.sam`
  `Info.Shape` with z flipped. The port used to resolve a coaster station to the PC-only
  `coaster.sam` (first `.sam` in the folder), which has no shape and no compiled record, so a coaster
  had no doors, no queue, and ParkSim never knew it was a coaster. `DefinitionFor` now prefers the
  `.sam` named after the model, then the one named after the folder.
- **The station link**: DBA `+0xbc`/`+0xc0` and `+0xd3` through the port's own quarter turns.
- **The node ring**, window, Catmull-Rom, side-vector frame, loop, lead-in and lead-out, the 17
  samples, arc length, V/W texture coordinates, heights through the additive pylon pose.
- **The placement rules** of `0x1216d8`, with the segment-against-segment test reduced (see the
  comment on `SegmentClear`).
- **The tool**: station → build (place, undo, loop, close on the entry cell) → pylon edit (height and
  bank, next and prev) → the queue tool. Costs as §2.7.
- **The seven cross-sections**, the `red.ssh` swap, the scrolling chain and water bands.
- **The trains**: the physics step as written, blocking, the station state machine, spawning, and
  the lift marking.
- **The test lap** on finishing the tool: car 0's statistics, the per-segment length, drops and
  steepest drop, and the rating text (shown on the status line; the stats screen itself is not drawn).

**The pylons (fixed after strawberry saw them floating).**
- The loft keys are DELTAS. `stdpylon`'s section 3 moves 16 of the post's 20 animated vertices
  +0 → +90 in y and leaves 4 at +0. The morph player `0x1a6d68` takes `model header +0x1c & 4` as its
  last argument (from `0x1a7f48`), and with it set writes `lerp(keys) + the vertex's current value`.
  The port wrote the keys as positions, collapsing the post onto its origin: posts floated 0.37–0.5
  cells above the floor and stopped short of the track. `AnimatedModel.Additive` now adds morphs,
  adds path translations and composes rotations for the 18 of 496 models that set the bit: the 15
  pylons and three coaster cars (Ghosta Coasta's `cart`, Escape Velocity's `cart`, the Shocker's `car`).
- The loft's UV keys are deltas too: the post's top groups run V 0 → 7.493 at full loft, so the
  `mc_pylon` lattice (an X-braced bamboo panel with transparent holes) tiles up the post. The UV
  consumer `0x1ad378` takes `(hdr +0x1c >> 2) & 1` and ADDS its keys with it (`+=`, MIPS read in the
  decompile). Written as absolute UVs every U collapsed to 0 and the post drew as horizontal bands
  (strawberry: "should have crosses up the whole thing").
- **What a post meets is its track dummy, not the track.** Every post is authored to meet its posed
  `TrackDummyCentre` exactly (checked on all nine pylons of Temple of Gloom and of Caterpillar: +0.00).
  `0x19a420` then puts the track at the dummy's LOCAL y + 0x60, ignoring the parent's offset. So the
  rail sits 0.11 under Temple's post top and 0.39 over Caterpillar's (whose `PYLON_TOWER` stands at
  y 0, not 0.5). That is the console's arithmetic as read; Caterpillar's station check was already
  52 units high (geometry §4.5), so the reading of Caterpillar's numbers may still hide something.
- Node flag 0x8000 is a draw skip: `0x22810c` tests `flags & 0x8050` before drawing a node's own
  mesh (children still drawn), so the stacker really is hidden. On Caterpillar that is the tall
  `STACKER` trunk, which is why its visible support is only the short tower.
- Section 10's key at 25 % is +90° about +Y, taking the model's +Z to +X, so the yaw is +heading (the
  port had −heading, skewing every diagonal).
- **The stacker is hidden unless something is stacked on it** (`0x199c90`, run by the stack setters
  `0x199c50`/`0x199dd0`). The node is fitting (0x80000, id 1) by the engine's rule, index + header
  `u16 @0x34`: `mc_bridge`, `STACKER`, `sc_bridge`. It gets hide flag 0x8000 while the pylon has
  nothing above it. The same function hides the record at instance `+0xc` → `+0x70` while the pylon
  stands on another; that this is the post is INFERRED.

**The join quirk decides where the chain texture goes** (`0x1aee48..0x1aee68`, re-read in MIPS for
the port). Going into a longer segment on a climb, the look-ahead lands up to a cell behind the car,
the "drop" comes out positive, and the speed jumps past 0.04, so the chain lets go for a stretch.
On the audit's hill oval, the climb into a 7.05-cell segment after a 6.50-cell one is chained only in
patches. The console's lift texture is therefore patchy wherever consecutive segments differ in
length (INFERRED from the READ code; not seen on hardware).

Riders sit on their cars' seat fittings (id seat + 1, space 0x80; Caterpillar's missing seat 4 seats
nobody, as on the console).

Not built yet: the stats screen as a screen; the Ultimate award record; coaster sounds; the Test
Park; moving a pylon (mode 13's Move); breakdowns; stacked pylons use the pylon below's attach point
instead of its posed stack helper.
