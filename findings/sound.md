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

⭐ **STATUS: plays, measured.** `--sound-census`/`--ride-film` run the guest test's park in REAL
frames rather than winding it -- a wound park fires every cue in one frame and no voice can
advance -- and log every cue twice: the resolution, and the verdict once the voice's playback
position has moved (or the mixer reports it finished), with the elapsed seconds beside the frame
count. On the render box under `--audio-driver Dummy`, a 90 s film of Crazy Ape gave **36 cues, 36
resolved, 36 voices started** (crate thumps, the crunch, the grunt, the roar, the swings, the
ADDOBJ grunt loop ended by its `KILLOBJ 10` at 46.4 s). ⚠ The first census read the position once,
eight frames after `Play()`, and reported two 314 ms clips as "DID NOT START" while a 1065 ms clip
read 0.842 s: at ~105 ms a frame the short clips had finished and stopped before the read. That
was the instrument, fixed as above; the mixed verdict it produced is also what proved the Dummy
driver ticks playback at all.

The video handed to the owner (`crazy_ape_park.mp4`, 90 s, 960x540, 12 fps) carries an audio track
**assembled from what the bytecode requested, at the times it requested it** -- each logged clip
decoded from the bank's own bytes and laid at its cue's park time, the loop repeated until its
kill -- **not recorded off the engine's mixer**, which every render mutes. `tools/` does not ship
the assembler; it is a scratch script over the census lines.

## Not established

* **What is left of the engine layer.** The vtable path;
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

## Regression evidence: playback observation and reset lifetime

See `audio-lifecycle.md` for the synthetic actual-Godot 37-check audit, seven rejected
mutations, and separate real JUNGLE 22/22 cue/voice census. Completion or position
advancement is sufficient evidence of playback even if the first Playing poll was
false. No evidence remains pending until the 0.5-second diagnostic window; that window
is port instrumentation policy, not a decoded console constant. World reset stops old
voices, releases the world-specific catalogue manager and clears old particle instances
while retaining their global library holder. Dummy observations do not replace listening.

## ⭐⭐ The sound-parameter call contract (`FUN_00111D40`), and why "parameter 20" has no engine-side answer

Traced 2026-09-24 after astraclaw found the bus state handler setting a parameter on a live sound
handle. **The parameter is still unnamed, deliberately** — see §"what this does NOT establish".

### The chain, end to end (READ)

```
FUN_00111D40(audio, handle, selector, value)          a thin forwarder
    audio  = *DAT_002ABE18 -> word 0 of a 5-word struct = the sound object
    -> vtable 0x3706C8, slot +0x44 = FUN_002413E0(audio, handle, selector, value)
         if handle == 0            -> 0        (nothing)
         if selector == 0          -> handle   (no-op; selector 0 is not a parameter)
         if (handle & DAT_00342EDC) == 0   -> FUN_00244568  (a SINGLE voice)
              -> FUN_00247920 -> vtable slot +0x64 of the voice, called (selector, value)
         else                              (a COMPOSITE/GROUP handle)
              index  = handle & ~DAT_00342EDC, bounds-checked against audio[0xD4]
              member = audio[0xD0][index]
              slot   = FUN_00243840(paramTable, selector)
              member[0x39][slot] = value ;  FUN_00242138(paramTable, member)
```

⚠⚠ **Ghidra's signature for `FUN_00111D40` is WRONG and will mislead anyone who trusts it.** It
renders as `(int *, undefined8, undefined1)` — three parameters, no selector — because it types the
handle as 64-bit and swallows `a2`/`a3`. The call sites disagree, and so does a wrapper:

```
0x147BD4  lw    a1,0x73A0(s1)     ; handle          FUN_00111E08(x, v)
0x147BDC  addiu a2,zero,20        ; selector          -> FUN_00111D40(x, DAT_002AC178, 2, v)
0x147BE0  jal   0x111D40
0x147BE4  daddu a3,zero,zero      ; value
```

Two independent witnesses to four arguments. The same collapse affects `FUN_00244568` and
`FUN_00247920` further down the chain.

### ⭐⭐ A selector is an id in the SOUND'S OWN table, not an engine property

`FUN_00243840(table, selector)` is a **linear search**: a count at `table+0x1C`, an array at
`table+0x20` of **five-word records**, matching `record[0] == selector`, returning the record's
index. The value is then written into the instance's own array (`member[0x39]`) at that index.

So there is no engine-side `switch (selector)` anywhere, and looking for one is the wrong search.
**What a selector means is defined by the parameter table belonging to the sound being played** —
for the bus, the sound created as group 1, event 6. That is also why the selector space is sparse
and uneven rather than a dense enum.

### The selector space, censused (READ)

All 49 call sites of `FUN_00111D40`, grouped by the `a2` selector, with the values each passes:

| selector | sites | values seen |
|---|---|---|
| 2 | 1 | not immediate |
| 4 | 3 | not immediate |
| 6 | 7 | 0 |
| 7 | 13 | 0, 20, 60 |
| 8 | 5 | 0 |
| 9 | 1 | 0 |
| 10 | 1 | 100 |
| **20** | **4** | **0, 51** |
| 22 | 4 | not immediate |
| 23 | 2 | 100 |

⭐ **All four selector-20 sites are `0x147980`, `0x147BE0`, `0x147C28`, `0x147C90` — every one inside
the bus handler.** Nothing else in the image uses it, so there is no sibling caller to learn from.

### Where the object comes from

`FUN_00110A78` builds it: `new(0x14)` → `FUN_00110A20` (which only zeroes five words — **no vtable**,
so `DAT_002ABE18` is a plain struct, not a polymorphic object) → stored in `DAT_002ABE18`. Word 0 is
then filled by `FUN_00110B60` → `FUN_0023FBA0` → `new(0xE0)` → `FUN_0023FC30`, and it is
`FUN_0023FC30` that writes `*obj = &DAT_003706C8`, the vtable above. `FUN_00230260("DisableSound")`
gates the whole of `FUN_00110B60`.

⚠ Two red herrings in this area, both `0x14`: the object's allocation size is `0x14` bytes and the
parameter records are `0x14` bytes. Neither has anything to do with selector 20.

### ⭐⭐⭐ ANSWERED: selector 20 is event 6's own link-branch variable

Read out of the data the prediction pointed at. Two steps.

**1. `a1` is the audio-category id, and the mapping is in the static initialiser `FUN_001120D8`.**
The registry at `0x2ABE38` is eleven `{dir, name, handle, id, used}` records of five words; `a1` is
matched against `record[3]`. ⚠ The registry lives in BSS, so the image's copy is all zeros -- the
values must be read from the initialiser's writes, not from the data.

| `a1` | category | | `a1` | category |
|---|---|---|---|---|
| 0 | `AUDIO/GLOBAL/ui` | | 6 | `AUDIO/RIDES/trck` |
| **1** | **`AUDIO/GLOBAL/amb`** | | 7 | `AUDIO/GLOBAL/kids` |
| 2 | `AUDIO/GLOBAL/ride` | | 8 | `AUDIO/GLOBAL/staf` |
| 3 | `AUDIO/RIDES/bump` | | 9 | `AUDIO/RIDES/fprc` |
| 4 | `AUDIO/RIDES/grc` | | 10 | `AUDIO/RIDES/fpwt` |
| 5 | `AUDIO/RIDES/wtr` | | | |

⭐ **The control that pins this table**: `a1 = 7` gives `kids`, and this port already recorded
`FUN_00111428(audio, 7, 0xD0, ...)` resolving event 208 to `cashD2b.vag` in `GLOBAL/KIDSSFX.MAP`
(findings/visitors.md). Two independent routes to the same category. ⚠ It is therefore the AUDIO
CATEGORY namespace and **not** the `SoundCatalogue`/RSE group namespace, which uses different
numbers -- a mismatch that has already caused one wrong reading in this port.

**2. The event names its own parameter.** `/AUDIO/GLOBAL/AMBSFX.MAP` event 6 -- the bus:

```
event 6: flags 0406  word0C 0FA0  word12 0014  sets 4
  set 0  sound 22  667 ms  bank 1   -> target 2 for 51..100,  target 1 for 0..50
  set 1  sound 20 1730 ms  bank 1   -> target 3 for 0..100
  set 2  sound 19  234 ms  bank 1   -> target 3 for 51..100,  target 4 for 0..50
  set 3  sound 17 2274 ms  bank 1   -> target 1 for 0..100
```

`word12 = 0x14 = **20**`, and the links split at exactly **0..50 / 51..100** -- which is why the bus
writes **0** and **51**: 51 is the first value of the high band, not a tuned number. Setting
parameter 20 to 51 steers the chain into the high-range targets and back to 0 into the low ones.

⭐⭐ **The control, and it is the strong kind.** Over 153 events in five categories: **141 have
`word12 == 0`, and not one of those has a range-limited link.** Of the twelve with a non-zero
`word12`, nine are values the call sites actually pass, and eight of those nine do have
range-limited links. A field that were noise would put branching links on some of the 141.

⚠ Three events carry `word12 = 19`, a value no call site passes as an immediate. That is
consistent rather than contradictory: eight of the 49 call sites compute `a2` instead of loading a
constant, and were not resolved.

⚠ What this does NOT say: that selector 20 means anything outside event 6. It is per-event by
construction, and 20 only because that is the id this event was authored with. The set-index base
of the link `target` field was not checked, so the exact cycle above is the parser's numbering.

### What this does NOT establish
- **That the table is absent from the executable.** A 20-byte-stride search for a static table whose
  word 0 carries those ids found nothing — ⚠ **but the same search with a decoy id set also found
  nothing, so it discriminates nothing and proves nothing.** The table is most likely built at
  runtime from the sound data; that is a expectation, not a reading.
- **The single-voice path.** Only the composite path was read to the parameter array. The single
  path ends at vtable slot `+0x64` of the voice object, which was not followed.
- ⚠ A previous version of this trace reported that `DAT_002ABE18` "is never written anywhere in the
  image". **That was false, and it was an instrument defect**: the cross-reference sweep cleared
  every register on `jal`, which throws away a `lui` held in a saved register across a call — the
  exact shape a constructor uses. With caller-saved registers alone cleared, three stores appear at
  `0x110AAC`, `0x110B14` and `0x110B20`. astraclaw called for a control before the negative was
  believed and was right to. A negative from a sweep is a claim about the sweep until it is.
