# Ride sound: from a script's cue to a clip by name

2026-09-23. What a ride script's sound cue means on this disc, resolved to a clip by name and
checked against predictions made before the lookup. Two mechanisms carry ride sound and they
DIVIDE the park rather than overlap: scripted cues (`EVENT`/`ADDOBJ` with a sound group), which are
solved here, and the track rides' `SPAWNSOUND` child, which is a table for the ride engine and is
NOT resolved -- see the last section, which says so plainly. Playback is wired
(`game/RideSounds.cs`) and built; whether a voice actually starts is measured by a census that has
not yet been run on a render box, and this note does not claim it has.

Readers: `core/TPW.PS2.Data/SoundIndex.cs` (`SfxMap`, `SoundCatalogue`), `tools/sfxmap.py`.
Census: `tools/TPW.PS2.ParkSimAudit` (per ride, per second, clip by name) and the viewer's
`--sound-census=<seconds>`.

## What a script fires

A sound cue is `EVENT <group> <node> <event>` or `ADDOBJ <group> <node> <event> <tag>` -- 793 of
them across the four worlds. The first operand is a KIND, exactly as it is for particles
(`ParticleLibrary`: kinds 1 and 2), and the sources spell the sound kinds as `OBJ_SOUND_*`. Their
values come from aligning every `.rss` statement with its `.rse` instruction over all 351 pairs
(0 mismatches), which is also where every `EVT_*` number below comes from:

| constant | value | map it names |
|---|---:|---|
| `OBJ_SOUND_LOC_RID` | 3 | `/AUDIO/<WORLD>/PARK<n>/RIDESFX.MAP` |
| `OBJ_SOUND_LOC_AMB` | 4 | `/AUDIO/<WORLD>/PARK<n>/AMBSFX.MAP` |
| `OBJ_SOUND_GLO_RID` | 5 | `/AUDIO/GLOBAL/RIDESFX.MAP` |
| `OBJ_SOUND_GLO_KID` | 6 | `/AUDIO/GLOBAL/KIDSSFX.MAP` |
| `OBJ_SOUND_GLO_STA` | 7 | `/AUDIO/GLOBAL/STAFSFX.MAP` |
| `OBJ_SOUND_GLO_AMB` | 8 | `/AUDIO/GLOBAL/AMBSFX.MAP` |
| `OBJ_SOUND_GLO_UI` | 9 | `/AUDIO/GLOBAL/UISFX.MAP` |
| (no name in any source) | 10 | -- |
| `OBJ_SOUND_GLO_BMP` | 11 | `/AUDIO/RIDES/BUMPSFX.MAP` |

The second operand is a node: `-1` on 716 of the 793, and `1`, `2`, `3`, `9`, `10` on the rest
(fireworks at nodes 9 and 10, gates at 1, the seaplane). ⭐ `-1` means THE RIDE ITSELF: the node
resolver `0x1b9388` takes a negative node straight to `0x1b9220(instance+0xc8, …)`, the
instance's own position, and returns 1. A non-negative node is a fitting search (findings/rse-vm.md)
and a miss returns 0, so the instruction does nothing.

The third operand is the EVENT ID. ⚠ It is not a bank index, and the obvious elimination test
would have discarded the right answer: kind-3 ids run 8..260 in the jungle (61 distinct) against
park ride banks of 88 and 79 sounds -- 45 of the 61 lie past the bank count. Ids past the bank
count is what an event-keyed index looks like; the join is one hop longer than a bank lookup.

`EVENT` passes 1000 where `ADDOBJ` passes its fourth operand (`0x1bbf28`, both reach `0x1bbf28`'s
common tail), and the scripts use that operand as a TAG: `ADDOBJ OBJ_SOUND_LOC_RID -1 EVT_GRAVE1
10` … `FADEOBJ 10`; `KILLOBJ 1` (x126), `FADEOBJ 10` (x54), `KILLOBJ 1000` when a ride breaks.
⚠ That EVENT is a one-shot and ADDOBJ a loop is a reading of the scripts -- an object worth killing
is one that would otherwise go on -- not of the object list at instance `+0xb0`, which has not been
walked.

## The map: `*SFX.MAP` is keyed by event id

The record sizes and the depth-first layout are the loader's (`0x249d38` → `0x24b770` → `0x24b810`
→ `0x24b8d0` → `0x24a030`, findings/formats.md). What the fields MEAN is from the data:

```
0x00  GUID 00 2c 61 e9 …    0x10 u32 1    0x14 u32 0 (advisor: 6684110)    0x18 u32 roots (always 1)
L1, 24 B:  +0 u32 event count   +4 ptr   +8 u16 flags (0x0008, engine maps 0x0001)   +0xC three floats 1.0 2.0 0.5
L2, 20 B:  +0 u16 EVENT ID  ⭐⭐   +4 u32 set count   +8 ptr   +0xC u16 (3300/4600/5700/2300/1000/3200/4000/5999)
           +0x10 u16 flags (0, 4, 6, 8, 0x406, 0xc06)   +0x12 u16 (0/6/7/8/19/22)      -- the three unread
L3, 42 B:  +0 u16 clip count   +4 u32 link count   +8 ptr→clips   +0xC..+0x25 unread (byte pairs 64 64 / 55 55 / 3f 3f,
           32 32; the loader rewrites +0x1e in place)   +0x26 ptr→links
clip, 16 B: +0 u32 sound (ONE-BASED into the bank)   +4 u16 threshold   +6 u16 0   +8 u32 ms   +0xC u16 bank (ONE-BASED)   +0xE 0
link,  8 B: +0 u32 target L3 (one-based, this event's)   +4 u16 0   +6 u8 low   +7 u8 high
```

⭐⭐ **The u16 at the start of an L2 record is the script's third operand.** `JUNGLE/PARK1/RIDESFX.MAP`'s
events are numbered 8, 62, 73, 82, 83, … 260, and 8 is `EVT_RIDE_APE`, 62 `EVT_RIDE_APEGRUNT`, 73
`EVT_TOTEM1`, 82 `EVT_APESWING1` in the jungle scripts. `GLOBAL/KIDSSFX.MAP` holds 48..53 in a row:
`EVT_BOG1`..`EVT_BOG5`, `EVT_DOORSKWEEK1`.

⚠ `+0xC` of a clip is the BANK, not the sound. `tools/sfxmap.py` said "sound id" there; the sound
is `+0`, one-based, and the `ms` at `+8` equals the bank header's own length on 939 of 939 entries
(formats.md). All 42 maps are consumed exactly by this walk.

The threshold is cumulative and read from its shape, not from a consumer: a set of four carries
`0x3fff, 0x7ffe, 0xbffd, 0xfffc`, a set of three `0x5555, 0xaaaa, 0xffff`, and `apegrunt6.mp2`,
`apegrunt7.mp2`, `blank.mp2` carry `0x4ccc, 0x9998, 0xfffe` -- thirty, thirty and forty per cent
of the time the ape says nothing. The 8-byte links appear only in the `/AUDIO/RIDES/` maps:
`GRCSFX.MAP` chains 32 go-kart engine states, each link a target state and a `low..high` band of a
0..100 scalar. No script ever reaches them (below).

### `*BANK.MAP`

```
0x00 GUID 01 2c 61 e9 …   0x10 u32 1   0x14 u32 0 (advisor 0xffffffff)   0x18 u32 count
0x1C  count x 11 bytes:  ff ff ff ff | u32 id (0x66f310, 0x66efa8 …) | 9b | 01 or 02 | 5b/59/af/b1/16   -- kept verbatim by 0x249758
then  count x (u32 len, chars, NUL)
```

A name is the folder the bank was BUILT from -- `Sound1\Ride`, `sound\Coast`, `Global\Water`,
`spch\spch` -- and its last component names the file beside the map: `RIDEHD.SDT`, `COASTHD.SDT`,
`WATERHD.SDT`, `SPCHHD.SDT`. The loader (`0x249758` → `0x249210`) looks a bank up by that NAME
before opening a file, which is how `GLOBAL/AMBSFX.MAP`'s bank 2, `Sound\Ride`, is the same
`RIDEHD.SDT` the global ride map opens. Six maps carry more than one bank: the jungle park ride
maps (`Sound1\Ride`, `Sound1\Amb` -- one ride event plays from the ambient bank), `GLOBAL/RIDE`
(Ride, UI, Staff), `GLOBAL/AMB` (Amb, Ride), `GLOBAL/KIDS` (Kids, UI), `GLOBAL/UI` (UI, OSM).
⚠ Read as a single trailing string -- which `sfxmap.py`'s `bank_path()` did -- those six lose their
second and third banks silently.

## Which map a group names: measured, with a chance baseline

Every `(world, id)` a script uses under a group was tested for membership in every map's event
column. The assignment is the table above because each group's ids are events of ONE map while the
same ids hit the others at chance:

| group | ids | in its map | in the others |
|---|---:|---|---|
| `GLO_RID` | 31 distinct | `GLOBAL/RIDESFX` **31/31** | world ride maps 1/8 … 4/6 |
| `GLO_KID` | 22 | `GLOBAL/KIDSSFX` **22/22** | `GLOBAL/RIDESFX` 3/22 |
| `GLO_STA` | 4 | `GLOBAL/STAFSFX` **4/4** | -- |
| `GLO_BMP` | 8 | `RIDES/BUMPSFX` **8/8** | -- |
| `LOC_RID` | 234 | own world, PARK1 ∪ PARK2: **461 of 467 cues** (new headers), 26/26 and 45/45 (mixed, old) | `GLOBAL/KIDS` 83/234, `GLOBAL/UI` 20/234, `GLOBAL/RIDE` 19/234, `STAF` 10/234 |

⚠ The advisor's `SPCHSFX.MAP` numbers its 170 events 1..170 and therefore "contains" 87% of every
id on the disc; it is not a candidate and a map like it cannot be scored by membership.

⚠ A world ships TWO parks and their ride maps differ (jungle: 70 events against 54). Mumbo and
Inca Totem resolve only in `PARK2/RIDESFX.MAP`; resolved against park 1 alone, 43 of the jungle's
95 cues came back "no such event" and every one was those two rides. The game loads one park's
maps; a census that builds every ride in one park has to ask the other and say so.

## Controls, predicted before the lookup

| cue, and what predicted the answer | resolves to |
|---|---|
| Crazy Ape `EVENT 3 -1 8` (`EVT_RIDE_APE`) -- "an ape" | `JUNGLE/PARK1/RIDEHD.SDT[1]` **`apeoooooC.vag`** 868 ms |
| `EVT_RIDE_APEGRUNT` (62) | `GRUNTEXP.vag` |
| `EVT_APEGRUNT1` (89) | `apegrunt6.mp2` / `apegrunt7.mp2` / `blank.mp2` |
| horloo `EVT_BOG3`, commented `; PISS` in its own source | `GLOBAL/KIDSHD.SDT[11]` **`wee1.vag`** |
| `EVT_BOG1 ; STRAIN` | `strain2.vag` |
| `EVT_BOG2 ; CRAP` | `FART1.vag` |
| `EVT_DOORSKWEEK1` | `dooropen1.mp2` |
| `EVT_BREAKDOWN` (43) | `breakdown1.mp2` |
| `EVT_WITCH` (Hallow 84) | `WITCH2.vag` |
| `EVT_STRETCH` (69), three sets | `nl_creak_start`, `nl_creak_1..4`, `nl_creak_end` |
| `EVT_BUMPER_CAR_ENGINES` / `_FX` (20, 21) | `nl_bump_1..3` / `Crunch.mp2`, `electric1.mp2` |
| negative: the same id 8 under `GLO_RID` | no event 8 there |

Nobody chose these. King Of The Swingers asks for the same event 8 as Crazy Ape because both apes
ask the same event of the same park map.

## Two header generations

The sources include either `\..\tpwport\source\audiosys\<World>Event.h` (older) or
`\source\game\soundint\ThemedEvents\<World>Event.h` and `Events\SfxEvent.h` (newer), and the same
symbol has a different number under each and per world: `EVT_KARTSTART` is 134 (Fantasy), 165 and
old 4 (Halloween), 194 (Jungle), 167 (Space). Global events keep one number everywhere
(`EVT_BREAKDOWN` 43, `EVT_BOG1` 48, `EVT_STRETCH` 69). The shipped maps satisfy BOTH generations
for ride, kids, staff and bumper cues; the numbers in the `.rse` are what runs, and the census uses
those.

## The census

`ParkSimAudit` runs every ride of a world for sixty seconds and prints each sound cue with its
script time, opcode, group, id, the clip by name and length, and which park's map served:

```
   4.0s EVENT   LocalRide  node -1 evt   8 -> RIDEHD.SDT[1] apeoooooC.vag 868ms
   9.3s ADDOBJ  LocalRide  node -1 evt  90 -> RIDEHD.SDT[34] STONE.mp2 1871ms  (park 2 map)
```

| world | cues | resolved | not |
|---|---:|---:|---|
| JUNGLE | 95 | 95 | -- |
| HALLOW | 167 | 167 | -- |
| SPACE | 97 | 97 | -- |
| FANTASY | 58 | 54 | Flower Power `EVT_SPIN1` (97) x4 |

Cues whose id is in no shipped map, listed rather than given a neighbour's clip: the gates'
`EVT_UI_GATEOPEN` / `GATECLOSENORMAL` / `GATECLOSESLAM` (`LOC_AMB`, 126..192 by world) and
`EVT_SPEAKER1`; the seaplane's `EVT_PLANE` / `PLANE_START` / `PLANE2` and the ferry's `EVT_BOAT`
(`GLO_AMB`); `EVT_PARK_CLOSED` (`GLO_UI`, 185); `EVT_SPIN1`, Hyenas' `EVT_SPARE1`, the fountain's
`EVT_FOUNTAIN`. `SoundCatalogue.Resolve` returns null for them and the audit prints them.

The two mechanisms partition the rides: the six jungle scripts that `SPAWNSOUND` an `EventMap`
(Coaster1, Coaster3, MineCart, GoKarts, TourRide, Wateride) carry no `OBJ_SOUND_` cue at all except
Wateride (3), and the rides silent in the census are the audit's own dead-track pollers. Belly
Bounce, Mayan Spinner and The Hot Pot carry no sound cue of either kind.

## Playback: `game/RideSounds.cs`

Every scripted ride's `EffectRequested` is answered: a sound `EVENT`/`ADDOBJ` is resolved through
`SoundCatalogue` (park 1, then park 2, labelled), the clip is decoded to an `AudioStreamWav`, and a
voice stands at the ride root for node `-1` or at the named `0x200` fitting; `KILLOBJ tag` and
`FADEOBJ tag` stop the ride's voices with that tag, and a removed ride drops its voices.
`LOC_RID`, `GLO_RID`, `GLO_KID`, `GLO_STA`, `GLO_BMP` are positional; ambient and UI are not.

Decoders, checked against something other than themselves:

* **MP2** (NLayer 3.0.0) against the ffmpeg references on bigdisk for three real disc clips
  (`SPACE/PARK2/AMBHD` amb1, amb3, amb2-1): lag 0, gain 1.000, residual 0.01% of signal.
* **PS-ADPCM** (`Vag.Decode`) against an independent decoder written from the SPU description:
  sample-exact on `apeoooooC.vag`, `wee1.vag`, `WITCH2.vag` (88,732 samples, none differ).
* **22,050 Hz**: decoded samples over the clip's own header duration gives 22,059..22,7xx Hz on 347
  of the disc's 356 PS-ADPCM clips. ⚠ Nine disagree with their own header -- `toyWindup1B.vag`
  implies 51 kHz, `servoUP.vag` 30 kHz, `harpB`, `dragonfly1b22`, `Spacehitcounter`, `Select3`,
  `pumpkbg` 24..25 kHz. They play at 22,050 like the rest, and the header-versus-decoded length is
  printed per clip so a wrong-speed one is visible in the log.

⚠ Chosen, and said in the code: `EVENT` one-shot with the default tag 1000, `ADDOBJ` a loop under
its tag; a three-set event plays start → loop → end (`nl_creak_start / 1..4 / end` name
themselves so); the clip within a set is a random draw against the cumulative thresholds; a fade is
half a second; positional voices reach 80 units with Godot's default attenuation. None of that is
walked from the console's object list or its SPU code.

⚠⚠ **STATUS: wired and built, not yet shown to play.** `--sound-census=<seconds>` on the park viewer
borrows `--guest-test`'s park (corridor, Crazy Ape, guests) and runs it in REAL frames -- a wound
park fires every cue in one frame and no voice can advance, which is precisely the "resolves but
never plays" hole -- printing two lines per cue: the resolution, and eight frames later whether the
voice was playing and how far its playback position had moved. That run needs a render box under
`--audio-driver Dummy` and had not been made when this was written. Until it has, the chain is shown
to reach a decoded stream handed to a player, not a voice consumed by the mixer.

## The engine layer, read from the executable

Every call into the audio entry `0x111428(sys, a1 = subsystem, a2 = event id, a3 = &position, t0 = &out,
t1 = 0)` was censused: **119 sites**, each classified by the subsystem loaded into `a1` and by where
its event id comes from. The subsystem ids are named by the registration block at `0x112108..0x1124c0`,
reconstructed by tracking its constant loads:

| id | bank | id | bank | id | bank |
|---:|---|---:|---|---:|---|
| 0 | `GLOBAL/UI` | 4 | `RIDES/grc` | 9 | `RIDES/fprc` |
| 1 | `GLOBAL/AMB` | 5 | `RIDES/wtr` | 10 | `RIDES/fpwt` |
| 2 | `GLOBAL/RIDE` | 6 | `RIDES/trck` | 11 | `ADVISOR/spch` |
| 3 | `RIDES/bump` | 7 | `GLOBAL/KIDS` | 12 / 13 | the park's `RIDE` / `AMB` |
| 8 | `GLOBAL/STAF` | | | 14 / 15 | `MUS`, `LOBM` / `LOBS` |

⭐ **The VM's dispatch confirms the group table above from the other side.** The `EVENT`/`ADDOBJ`
jump table at `0x366e90` sends each kind to its own case, and the cases load `a1`: kind 3 → 12,
4 → 13, 5 → 2, 6 → 7, 7 → 8, 8 → 1, 9 → 0, 11 → 3. That is `LOC_RID` → the park ride map,
`GLO_KID` → `KIDSSFX`, `GLO_BMP` → `BUMPSFX`, exactly as the membership matrix measured.

**The "unused" event ids are the engine's, and they are literals.** Every engine-side site either
passes a literal or reads a per-object slot that was filled from a per-world literal table,
selected on the world number (`0x147d00` returns `*0x3952e4`: 1 Halloween, 2 Fantasy, 3 Space):

| code | what it is, by the names its ids resolve to |
|---|---|
| `0x1ce090` | a throwing stall: Halloween {191 `misspumpkin`, 221 `throwatpumpkin`, 192 `hitpumpkin`}, Fantasy {162 `fruitsquidge`, 161 `fruitboing`, 159 `throwatfruit`, 160 `fruitslide1`, 170 `fruitupB`} |
| `0x1d0538` | a hammer stall: Jungle {224 `Hammove`, 222 `Molehit`, 225 `bell1b`, 221 `Moleup`, 227 `ahh1`, 226 `frog3`}, Space {202 `Sphammove`, 195 `Spacehit`, 197 `Spacehitcounter`, 194 `Spaceup`, 198/199 `Alianhit win/lose`}, Halloween {`devilup`, `winbell2b`, `losebell`}, Fantasy {`strength_target`, `strength_beeup`} |
| `0x193478` → `0x193708` | the pong stall: Jungle {250..252 `pong_gemdrops/gemhit/hitball`}, Fantasy/Halloween {191..193, 217..219}, Space {215..219}; fired from five slots at `+0x200..+0x210` |
| `0x1cd198` | Jungle {228, 229, 230} → `dino000..002.mp2` |
| `0x1cf7c8`, `0x1cf8c8` | walk a zero-terminated id list at `object+0x174` and fire each |
| `0x1af330` | a five-slot kids table at `+0x280..+0x290` from literals {83, 285, 269, 253, 237} or {87, 289, 273, 257, 241} |

**The track rides are literals as well:** `trck` 4 (`Engine.mp2`) at `0x203280`/`0x203de0` and 15
(`Toot.mp2`) at `0x2049d0` -- the OLDER header generation's `EVT_KARTSTART`/`EVT_KART_TOOT`
numbers; the lift-chain creak `EVT_STRETCH` 69 under the global ride map at `0x1999e8`/`0x199ae8`;
the track follower `0x1af330`/`0x1af858` firing 17 under a subsystem held in a register (the ride
type's `/AUDIO/RIDES/` bank -- `GRCSFX` 17 is `Whir01Flt`) and one `fprc` (9) call.

⭐⭐ **Not one of the 119 sites takes its id from the sound child.** No path loads instance `+0x14`
and then `+0x1c`; the only five-slot tables found (`+0x2e4`, `+0x1e8`, `+0x200`, `+0x280`) are
filled from literals. The `EventMap.rse` numbers (the newer `soundint` generation) match no map
and no literal, while the engine's numbers are the older generation's. Reading: on this build the
`SPAWNSOUND` child is loaded and scheduled and its table is not consumed. ⚠ That is a negative
from a static census of ONE entry point: a reader through the audio object's vtable (`0x3706c8`,
reached by `jalr`) is not excluded, and four non-VM sites have an id source not traced
(`0x1af650`, `0x1afa9c`, `0x1afd30` in the track follower; `0x15535c`).

For the port this means the engine-layer sounds are reproducible from data plus these literal
tables, and the `.ENG` layers from the `.ENG` selectors (`RideEngine.cs`); none of that is
implemented, and `RideSounds.cs` plays script cues only.

## Not established

* **What is left of the engine layer.** The four untraced id sources above; the vtable path;
  the `.ENG` parameter channel (who writes the speed the layers crossfade on, findings/ride-engine.md);
  the audio play path (`0x240970` → `0x243f50` → `0x24d6f8`), which would replace "read from shape"
  on the clip threshold with a consumer fact. `0x1fa8c0`, once credited as "the ride's own table",
  is `jr ra; move v0, zero`. A short decompile list for one box slot: `0x1af330`, `0x1af858`,
  `0x1ce090`, `0x1d0538`, `0x193478`, `0x1cf2d8`, `0x1cd198`, `0x203280`, `0x2049d0`, `0x1999e8`,
  `0x240970`, `0x243f50`, `0x24d6f8`, `0x2438a8`, `0x243540`.
* The L2 `+0xC`/`+0x12` words, the L3 middle bytes, the L1 floats and the exact random draw are
  carried raw.
* Group 10 has no name in any source and is not resolved.
* `SETREVERB`, `DIPMUSIC` and the scream family are recorded as effects, not map lookups.
