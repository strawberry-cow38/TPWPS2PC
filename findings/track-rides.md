# Track rides (karts and water rides) — how they work

Researched 2026-09-26 for strawberry ("research how track rides (dino karts, splish splash etc)
work"). Sources: the rides' own `.sam`/`.rss` on the PS2 disc, and `SLES_500.32` decompiled in
Ghidra 12.1.2 with the ghidra-emotionengine-reloaded extension (R5900). Addresses are the
executable's. Where something is inferred rather than read, it says so.

## The short version

- A track ride is a **station** the player places, plus a **track the player draws** from it.
  The engine turns the drawn line into 2×2-cell track pieces, and cars run laps along it.
- Each ride **ships a script that would drive it** (`BUMP` sub-opcodes), but in this PS2 build
  every `BUMP` handler is a stub that answers zero. The rides are driven by **native code**: a
  track-ride class with its own pieces, cars, boarding and lap logic.
- **Each PS2 park offers exactly one track ride**: the karts in one park of each world and the water
  ride in the other. The two use **different car classes**: karts race (grid, lanes, overtaking,
  spin-outs, finishing order), boats drift (surging speed, bobbing, rocking).
- The port currently places the station only. The script waits forever on `BUMP_ISTRACKVALID`
  (findings/rse-vm.md), so nobody boards (findings/visitors.md).

## The rides and their files

Per world, one kart ride and one water ride, `AssetKind` 6 (TrackRide) in the compiled database:

| world | karts | water |
|---|---|---|
| JUNGLE | Dino Karts (park 1) | Splish Splash (park 2) |
| HALLOW | CryptKarts (park 2) | Ooze Crooz (park 1) |
| FANTASY | Bumble Buggies (park 2) | Taptastic Rapids (park 1) |
| SPACE | Space Racers (park 2) | The Blobulator (park 1) |

(Park = which of the world's two terrains offers it, from each park's native attraction list,
`NativeBusCatalogue.Keys(6)`.)

`/Rides/GoKarts/` (JUNGLE) holds the station `gokarts.mps` (+ `PGOKARTS.mps`, a preview), ten track
pieces `gk_trckb, b2, h, h_a, h_d, h_u, q, s, x, v` (`.mps`, most with an `.aps`), four kart colours
`gk_blue/green/orange/purple`, `GoKarts.rse`/`.rss` and `EventMap.rse`/`.rss`. `/Rides/Wateride/` has
the same piece set as `wr_trck*` (plus `x2`, `s1`) and a `wr_ring` boat.

### `.sam` (Dino Karts / Splish Splash)

- Station footprint: karts `****/****/*2N*` (4×3), water `****/****/****/*2N*` (4×4).
- `Bumper.WhichTrackType` 1 = car track, 2 = water track ("0=no track"). `Bumper.BumperType` −4
  `RIDETYPE_LOSTWORLD_GOCARTS`, −5 `RIDETYPE_LOSTWORLD_WATERTRACK`.
- `Bumper.{North,East,South,West}{X,Y}Adjust`: "constants to line up the Track sections with the
  rest of the co-ordinate system" (an older commented-out set is still in the file).
- Capacity by upgrade: karts 4/6/8 (one guest per kart, `MaxCapacity` 8); water 2/3/4 per boat.
- `Info.RunsContinuously 1` on the water ride ("always runs, so don't wait for the ride to stop
  before letting people on"); `UsageInfo.ISIndoors 1` ("more attractive with the rain").
- `SupplementalMeshes`: the kart colours / the boat ring.

### Event map (sound)

`EventMap.rss`: `EVT_KARTSTART`, `EVT_KARTSTOP`, `EVT_TOOT`, and parameter `JUNGLE_ENGINE_REVS`.
The water script also loops `EVT_RIVER` while the water flows.

## The script layer (what the ride *would* do)

`GoKarts.rss`, in order: wait for `BUMP_ISTRACKVALID`; `BUMP_OPENRIDE`; board one guest at a time
(`BUMP_PEEPON VAR_LETMEON` then `BUMP_LAUNCHCAR`), resetting a **10 s** timeout on each boarding;
start when full or timed out: `WAIT 2000`, `BUMP_SETLAPS VAR_DURATION`, `BUMP_STARTRACE`; unload
with `BUMP_PEEPOFF`; an invalid track or a closed ride `BUMP_HALTRIDE`s ("abort the race - everyone
off - NOW !"). Breakdown: `BUMP_SETBROKEN` and smoke on nodes 3 and 4.

`Wateride.rss` runs continuously: each loop `BUMP_WATERCLOSED 0` (water flowing), counts
`BUMP_CARSONRIDE` (max **64** boats), fills a boat to `VAR_CAPACITY` then `BUMP_LAUNCHCAR`s it (or
after 10 s), unloads with `BUMP_PEEPOFF`; closed/broken/invalid stops the water
(`BUMP_WATERCLOSED 1`) and fades the river sound.

⚠ **None of this runs on PS2.** `BUMP` is handled at `0x1c1370`; every selector either discards its
argument or writes 0 (selector 0x11 stores a rate at `+0xe6`). Confirmed in the R5900 decompile.

## The native track ride

**Pool:** `0x147eb0` allocates **2** track-ride objects of **0x28d8** bytes (ctor `0x154da0`,
vtable `0x36bbf0`), beside 15 rides of 0x140 and 3 tour rides of 0x1b0. The PSX had the same shape
(`PathedRide`, 2 per park, 34 pieces).

**Overrides** of the queued-ride base: `0x203130` destructor, `0x203220` returns 0, `0x200410`
update (`0x2023b0` car step, `0x200358` wear), `0x200728` per-frame (every car, then every piece).

| offset | what |
|---|---|
| `+0x13c` / `+0x13e` | count and list of the player's **waypoints** (x,z pairs) |
| `+0x1d8` | pieces: up to **35** × 0x108 (piece vtable `0x36b670`), count at `+0x26f8` |
| `+0x2808..0x280b` | the track's bounding box in cells |
| `+0x280c` / `+0x2810` | car count / car pointers |
| `+0x2854` | track length = pieces × **256** |
| `+0x2894` | cars set aside during the race mode (up to 17) |
| `+0x138` | the player's own car, in race mode |
| `+0xa2`, `+0xec` | status and reliability (`<0xa000` → breakdown, status 4) |

### Building the track

`0x2009c0` rebuilds from scratch: removes every car, clears the pieces (`0x201640`), lays the
**station** (table `0x2ee54a` by rotation: station type 0–3, a connector of shape 99, and — when no
legs are drawn yet — one straight), then for each consecutive pair of waypoints lays a leg
(`0x200cd0`). A leg must be axis-aligned; it is filled in **2-cell steps** by `0x200fb8`, which
chooses each piece's type. Length becomes pieces × 256; a failed validity check (vtable `+0xc4`)
sets status 2 or 3.

**Piece types** (`0x2ee1e0`, 48 × 8 bytes, four rotations each): types 0–3 the 4×3 station
(shape 15); 4–39 the 2×2 pieces (shapes 0, 1, 2, 3, 4, 9, 10, 11 and 99); 40–47 two more 4×3 shapes
(12, 13), handled separately in `0x201410` — plausibly the add-ons (jump, tunnel), **not confirmed**.
Which mesh each shape draws (`b`, `q`, `h_u`…) is chosen through the compiled record's resource
words at `+0xdc` (`0x204c38`), and the model registry at `0x2c2438` lists the piece resources
(karts 497–506, boats 511–520, kart colours 507–510; category 10 = track piece, 11 = vehicle).

### Cars

- **Boarding** `0x201ac0` (the native PEEPON): the guest joins the last car if it has room, else a
  new 0x98-byte car is created. So cars exist only while carrying guests. `0x2022a8` unloads a
  car's guests and deletes it.
- **Position** is one number along the track (`car+0x50`, 256 per piece), wrapping at the length;
  each wrap is a **lap** (`+0x58`). At the ride's lap target (vtable `+0x304`) the car is done.
- **Ordering** `0x2023b0`, each tick: finds each car's piece, sorts cars by distance, sets race
  rank (`+0xc`), and gives every car its neighbours ahead and behind.
- **Which car class** is a per-park table `0x2ee528` indexed by world (`0x3952e4`) and park
  (`0x3952e8`, both set in `0x150e20`). It matches the ride exactly: karts parks → class B,
  water parks → class A.

**Class A, boats** (vtable `0x36bfb8`, step `0x203540`): speed/4 per tick, accelerating by 1 toward
a target; on reaching it a new target **60 − 2·rand(11)** and speed = target − rand(11), so boats
surge and fall back. Height eases toward the track's height plus a random **±10 bob**; heading eases
toward the track's yaw by 0x14 per tick (of 4096); two tilt axes clamp at **±150** (±25 in status 2).
Sound parameter 4 = speed×100/target.

**Class B, karts** (vtable `0x36c0a0`, step `0x204240`): a state machine at `+0x39` — grid countdown,
racing states with a lane (`+0x5a`, values 0x32/0xcd), **overtaking** the car ahead on a 1-in-11
roll when blocked, a **spin-out** (heading +0x200 per tick while speed halves), a slow-down, and a
finishing order from total distance (`+0x54`). The player's car (`ride+0x138`) steers its own lane.

### Race mode

`0x201c30` sets the guests' cars aside (`+0x2894`) and creates exactly **5** class-B cars; `0x201e00`
restores them. With the player car at `+0x138`, this is the drive-it-yourself kart race. The
compiled record's minigame selector (GoKarts = 7, findings/dba.md) goes through the constructor
dispatcher `0x1debc8`. Not traced further.

## What is not read yet

- The piece choice in `0x200fb8`: when a leg gets a straight, a corner, a hill (`h_u`/`h_d`), a
  crossing (`x`) or `v`; the validity rule (vtable `+0xc4`) — presumably a closed loop back to the
  station.
- The player's track tool: how waypoints are drawn, removed and priced.
- The native status machine and boarding cadence (who calls `0x201ac0`, how often), and the lap
  target's source (vtable `+0x304`, likely the duration slider).
- Water flow as such (the boats' speed rules are read; any current is not), and the `Bumper.*Adjust`
  constants' consumer.

## To build it in the port

Roughly, in dependency order: a track tool that records waypoints and generates pieces with the
decoded footprints; a track model (piece chain, length = 256 × pieces, pose and height lookups
along it); native boarding and unloading, not the stubbed script; the two car classes on the
park's fixed tick; laps and finishing; sounds from the event map. The race mode is separate and
optional.
