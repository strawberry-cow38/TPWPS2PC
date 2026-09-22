# Animated textures: authored material choices, APS keys, and particle lifetimes

Measured against the owner's PAL `SLES_500.32`, 2026-09-22. Executable SHA-256:
`231771d7f39cc29e7573ce62f437af15d97737a64c1d1c49df8404ead0b7577a`.
All executable addresses below are **virtual addresses**. Its PT_LOAD maps file offset
`0x1000` to VA `0x100000`; for example the loading filename format is at VA `0x361518`
(file offset `0x262518`). `tools/r5900dis.py` maps the ELF segment and explicitly decodes
R5900 LQ/SQ and three-operand MULT, continuing across unsupported instructions.

**Model textures are selected by APS keyframes into explicit MPS filename arrays.** There is
no need to infer frame order or invent a rate from filenames: loader `0x227610–0x227664`,
player `0x1a6b08`, and renderer `0x227f1c–0x227f30` establish the complete chain. Particle
sprites and loading-screen children have different consumers, described separately below.

The port now reads and plays the model texture channels through its existing `AssetLibrary`
resolver, viewer cache, and surface shaders. The full data-coverage audit remains **failing**:
408 of 418 multi-texture choices resolve, with ten named failures below. The Godot binding
and clock audit passes. Particle simulation and the original loading-screen UI are not
implemented by this change.

## Why the filename census cannot define playback

The supplied 16-WAD census measures 108 numbered groups / 982 images among 11,459 SSH/TGA
entries, approximately 54 groups after pairing formats. Those remain useful candidates,
not an animation playback denominator. The MPS/APS audit below finds explicit two-choice
materials and nonuniform/repeated choices that a “three numbered files” rule cannot describe.
For example, HALLOW lights have two Main records selecting texture 0 and texture 1; SPACE
TV ride has a two-choice `TV_SHIP_C.ssh` / `TV_SHIP_C2.ssh` material (slot 15).

The 16-of-31 loading children are deliberate subsampling:

* `0x16166c` builds `data/frontend/loading/%skid/`.
* `0x1616f4` computes `a3 = frame << 1`; `0x161700` supplies `%skid%04d.ssh`.
* `0x16171c` bounds the loop at 16. The four worlds therefore request suffixes
  `0000, 0002, …, 0030`, storing handles at `0x2b84a8`.
* The **consumer**, `0x1611f4–0x161214`, reads that very table at byte offset
  `(counter >> 1) & 0x3c`, i.e. element `(counter >> 3) & 15`.
  `0x1610c0–0x1610d0` increments the counter at `0x2b848c`; `0x161380` registers the
  callback. Thus each stored image lasts eight counter callbacks. The callback frequency
  in wall-clock seconds was not established here; this is not a claimed FPS.

That loader does not request the contiguous 32-frame `Loading/4Steve/halkid` group or the
odd-index `4Steve/*blink` groups. This identifies the discovered path's actual assets; it
is not proof that every other code path ignores those directories. Sorting all 54 candidate
groups and applying one speed would erase these distinctions.

## Model material lists: confirm the structure, then follow the consumer

`0x2274ec–0x2274f8` loads **MPS +0x3c and +0x40** and `0x227508` reads **MPS +0x22**.
These are the same runtime material table, descriptor table and material count used by the
port's mesh/group decoder. This verifies the structure before interpreting the loop.

| Location | Meaning | Consumer evidence |
|---|---|---|
| MPS +0x22, u16 | material slot count | `0x227508`, `0x227668` |
| MPS +0x40, pointer | 16-byte material descriptors | `0x2274f8`, stride at `0x22760c` |
| descriptor +0x0a, u16 | texture choice count | `0x227610`, loop test `0x227650–0x227660` |
| descriptor +0x0c, pointer | ordered names, each 20 bytes | `0x227620–0x227630`, stride `0x227664` |
| MPS +0x3c, pointer | 8-byte runtime entries | `0x2274ec`, stride `0x227608` |
| runtime entry +2, u16 | selected texture index | renderer `0x227f1c` |
| runtime entry +4, pointer | loaded texture handles | loader `0x2275f8`, renderer `0x227f20` |

The loader requests **every listed name** through `0x2351a8`. In the renderer, a mesh group's
material pointer is read at `0x227f00`; `0x227f1c–0x227f30` loads
`handles[selectedIndex]` and `0x227f88` passes that texture to `0x22a068`.
`0x227390–0x2273ac` independently performs the same index-to-handle lookup.
This is frame replacement, not UV scrolling or texture-atlas coordinates.

The arrays include nonnumeric names and separate start/countdown art. Phantom's descriptor
is at MPS file offset `0x1c0`: count 35 at `+0x0a`, names at `0x2d8`:
`START, STARTB, START1, START2, START3, TV_PM_00 … TV_PM_29` (all `.ssh`).
JUNGLE TVSim slot 7 instead lists `tvs1 … tvs10, start, startb, start1, start2, start3`.
The order is the data's order; it is not lexical or numeric sorting.

A full case-insensitive audit of **496 MPS files / 6,007 slots in all 16 WADs** measures
6,351 listed names, 74 multi-texture slots and 418 names in those slots. All 6,007 on-disc
runtime selected-index fields are initially zero. A multi-texture list alone does not imply
a repeating animation: DATA has 17 such slots but no APS records with the texture-channel
flag. The explicit setter at `0x1face0–0x1fad28` also accepts a material/index and validates
it against the same MPS count, so selection need not originate in an APS timeline.

## APS texture channels and timing

`0x1a87f8` receives the MPS in `a2` and APS record in `a3`; it uses the model's mesh/helper
pointers at `0x1a8870/0x1a8880`. At `0x1a88b8`, **record flag 2** gates the texture player;
global `0x2e71b4 & 8` suppresses it. At `0x1a88d4–0x1a88e0`, the call to `0x1a6b08`
passes that same model, record, and animation clock. Inside the callee, `0x1a6b50` loads
MPS +0x3c, confirming that the target is the runtime material table above.

| APS record / entry | Meaning | Reads |
|---|---|---|
| record +4, u32 | playback duration in APS frames | `0x1a8938`, `0x1a8a18` |
| record +0x0a, u16 | texture track count | `0x1a6b28` |
| record +0x14, pointer | 8-byte track entries | `0x1a6b34`, stride `0x1a6b44` |
| entry +0, u16 | MPS material slot | `0x1a6b40`, multiplied by 8 at `0x1a6b54` |
| entry +2, u16 | key count | `0x1a6b38` |
| entry +4, pointer | packed 4-byte keys | `0x1a6b48` |
| key low u16 | time in APS frames | `0x1a6b6c`, `0x1a6ba4` |
| key high u16 | texture choice index | `0x1a6b80`, `0x1a6bb8` |

`0x1a6b20` converts the clock frame through `0x297b68` (nonnegative float-to-unsigned
truncation; integer shifts at `0x297bd0–0x297bf0`). The player scans **backwards**, chooses
the last stored key whose time is <= that integer, stores its high halfword in runtime +2,
and marks flag `0x400` when the choice changes (`0x1a6b7c–0x1a6bd0`). Before the first key,
or with no keys, it retains the previous selection. Duplicate times therefore use the last
stored key. There is no interpolation or per-texture modulo operation in this consumer.

The animation clock at `0x1a8a54–0x1a8ab8` computes
`elapsedMilliseconds * 30 / 1000 / clockScale`. At scale 1, the existing port's 30 APS frames
per second is the appropriate clock. **The image rate is authored through key spacing**:

| Main record | Authored duration | Example keys / effective spacing at scale 1 |
|---|---:|---|
| HALLOW `phantom.aps`, record `0x1e34`, keys `0x1e58` | 119 | `(0,5), (4,6), … (116,34)`: TV image changes every 4 frames, 7.5/s |
| JUNGLE `tvsim.aps`, record `0x33b8`, keys `0x33dc` | 100 | `(0,0), (2,1), …`: 2 frames, 15/s |
| FANTASY `bugstv.aps`, record `0x6c0`, keys `0x700` | 100 | 110 keys at 2-frame intervals, ending at 218 |
| SPACE `tv_ride.aps`, record `0x2400` | 150 | six independent channels: 4-, 10-, 16-frame intervals and irregular holds |

Some stored keys extend beyond the selected record's duration (FANTASY above; SPACE slot 15
also has a key at 270). A player must not extend a record to the last texture key or demand
that all stored choices become visible during that record. The viewer now uses record +4
for playback duration and its animation-picker label. Its existing repeat-selected-record
policy remains a viewer preview policy, not an implementation of the ride script state machine.

`TPW.PS2.TextureAnimationAudit` reads all **374 APS files**, respects record flag 2 and the
skeletal format boundary, and measures:

| WAD | MPS multi-texture slots | APS texture records | Tracks | Keys (including repeats across records) |
|---|---:|---:|---:|---:|
| DATA | 17 | 0 | 0 | 0 |
| FANTASY | 7 | 26 | 30 | 1,059 |
| HALLOW | 11 | 20 | 24 | 760 |
| JUNGLE | 8 | 37 | 47 | 1,095 |
| LOBBY | 1 | 1 | 1 | 2 |
| SPACE | 30 | 101 | 185 | 10,749 |
| Other ten WADs | 0 | 0 | 0 | 0 |
| Total | 74 | 185 | 287 | 13,665 |

All 287 tracks pair with a model through the existing library's APS pairing, and every key's
material and texture indices fit that model's lists. All lists start at time zero and have
nondecreasing times. There are 13,588 distinct per-track key times; repeated timestamps
explain the difference from 13,665, and the audit expects the **last** key at each duplicate.

**Retraction of the earlier `findings/animation.md` interpretation:** the generic “small”
array is not automatically a per-mesh visibility timeline. That interpretation used
`0x1a8c30`, a different consumer, and produced an impossible mesh-count loop bound for TVSim.
For flag-2 records, the observed call is `0x1a88dc -> 0x1a6b08`, and the entries are
`material, keyCount, keyPointer`. Nonzero `SmallCount` without flag 2 is not decoded as a
texture channel. This removes the contradiction without inventing a different target model.

## Tp2.plb: correction, then the actual sprite consumer

PLB SHA-256: `6564736ad4d12d27634be0af1f23ef6f3ce4259b9d5b2f577e57fd8a68113210`.
The count 105, stride 320, and name spacing in `tools/plb.py` were correct. **The names do
not begin the records.** Loader `0x1467b8` reads the first two words using the advancing
memory-copy cursor `0x1461c0`, then reads 105 * 320 bytes into `0x2ce508`
(`0x146858–0x14687c`). Thus records start at file +8. Names at `0x120 + i*320` are
**record +0x118**. The old `record()` combined a name with the next record's parameters.

An independent file-boundary check agrees: `8 + 105*320 = 0x8348`, where the next header
is `(20,104)`; its records end at `0x8b70`, followed by two u32s, giving exactly 35,704 bytes.
There are 101 nonempty names. `tools/plb.py` now returns actual records and exposes only
the two sprite fields whose consumers were established.

Structure provenance: `0x18b1a0–0x18b270` copies the selected 320-byte definition from
`0x2ce508` into an emitter at `0x2c46cc + emitterIndex*320`. `0x1462c8–0x1462d8` derives
that same emitter address, and reads its signed halfwords +0x94 (sprite group) and +0x96
(logical frame count) at `0x14630c/0x146314`. It passes the group and a particle's logical
frame at +0x2c to `0x182680` at `0x14656c–0x146578`.

The update consumer `0x189e78` finds that same emitter through particle +0x26 and the same
`0x2c46a0 +0x2c` base. At `0x189ebc–0x189ed0` it decrements particle +0x20; a negative
counter takes the removal path. At `0x189fdc–0x18a020`, for count N > 0, it selects:

```
logicalFrame = (N - 1) - trunc((N - 1) * remainingLifetime / initialLifetime)
```

The counters are initialized together by `0x18893c–0x1889a8`, using emitter +0x78 with a
random remainder based on a quarter of that value; a zero initial divisor is replaced by 1.
This initializer explicitly derives the particle pool at `0x2c46a0 +0x28` and the emitter
base at +0x2c (`0x1888dc–0x188920`). Therefore a particle's sprite progression is tied to
its own lifetime, including birth-time variation, not a universal texture FPS or a loop.

Finally, `0x182680` validates group 1..32, reads a base index from `0x364058 + group*4`,
and adds **logicalFrame / 2**, before fetching a handle from `0x2c4038`. The handles were
loaded by `0x182618–0x182638` from 74 descriptors at `0x2c3b98`; initializer `0x1826d8`
constructs those descriptors from explicit 20-byte names at `0x2c35d0`.
For group 9 / logical count 16, the table starts at index 21 and names
`PA1a0000, 0002, …, 0014.ssh` (eight images). Group 4 / count 8 starts at index 7 and
names `PA1b0000, 0002, 0004, 0006.ssh`. The odd files present on disc are not selected by
this consumer. Among the 105 PLB definitions, `(group,count)` includes `(12,16)` 35 times,
`(3,8)` 18 times, `(9,16)` three times and `(4,8)` twice.

The previously documented “PA1a is a 16-frame loop” is withdrawn. It is a 16-logical-frame
lifetime sequence using eight stored images in this PS2 path. No global particle texture
rate is manufactured for the port. The full emitter simulator is outside this implementation.

## Implementation and acceptance

A perfect result for the **data audit** is: the complete measured MPS/APS populations are
read; all 418 explicit choices in multi-texture slots decode; every sampled boundary selects
the authored index; every track's indices fit its paired model. The required failure count
is zero. This does **not** require all 13,665 stored keys to be visible during a selected
record, nor require the filename candidates to behave as 54 uniform loops.

A perfect result for the **Godot integration audit** is: every expected surface binds the
specified texture at each test time, including before boundaries, after backwards seeking,
in texture-only records, and after viewer advance/pause/wrap. The required failure count is
zero; a static model's zero-length timeline must also keep a finite clock. Its finite
fixtures are not a percentage estimate of all gameplay.

Changes:

* `Model.MaterialTextures` reads each explicit list, validating bounded 20-byte names.
  `Materials` retains the first name for existing static callers.
* `Animation.TextureTracks` reads flag-2 tracks with pointer bounds checks and samples with
  the engine's last-applicable-key rule. `Record.DurationFrames` reads the authored duration.
* `AnimatedModel.SetFrame` applies those choices to shared per-slot `ShaderMaterial`s.
  Frame resolution goes through the existing viewer callback, `TextureNear`, and its cache;
  different model slots keep separate material state even when they share a filename.
  Alpha classification/shader choice is refreshed when the selected image changes.
* The existing TGA-first / SSH fallback policy remains in `AssetLibrary`. The viewer cache
  now also compares owner/name keys case-insensitively. No cross-WAD aliases or naming loops
  were added. The existing clock drives both geometry and textures; wrap preserves excess
  elapsed time instead of discarding it.

Run the data audit (requires .NET 8 and the owner's disc, no extracted fixtures):

```sh
dotnet run --project tools/TPW.PS2.TextureAnimationAudit -- /path/to/disc.bin
```

Measured: 16 WADs, 496 MPS, 374 APS; all 13,588 distinct boundaries pass their before/at/after
checks; 287/287 track/model index checks pass. **408/418 multi-texture choices decode;
exit 2, ten failures.** The unresolved choices, without exclusions:

| WAD / model / slot | Names |
|---|---|
| DATA `/Chars/Researcher/Researcher.mps` / 4 | `shead03_nb.ssh`, `shead03_a.ssh`, `shead03_t.ssh` |
| FANTASY `/Rides/gokarts/PGOKARTS.mps` / 19 | `fire0.ssh`, `fire01.ssh`, `fire02.ssh`, `fire03.ssh`, `fire04.ssh`, `flame2.ssh` |
| HALLOW `/rides/gokarts/PGOKARTS.mps` / 11 | `flame2.ssh` |

This gate is intentionally left failing. Multi-texture lists include manually selected art,
so it would also be wrong to call all ten failures “ten broken APS sequences.”

Run the actual viewer/material audit with Godot **4.6.2 mono**:

```sh
dotnet build game/TPWPS2Viewer.csproj
TPW_PS2_DISC=/path/to/disc.bin /path/to/godot --headless --path game res://tests/TextureAnimationAudit.tscn
```

Measured: **146 surface checks pass, exit 0**. Fixtures cover all 30 Phantom TV images,
10 JUNGLE TVSim images, 10 FANTASY BugsTV images, 15 SPACE TV images, two HALLOW light
records with no geometry tracks, and the actual `Viewer._Process` chain through its scoped
cache and shader binding, including a static model without an APS. Expectations for the TV images are independently specified in
the test, rather than produced by the sampler under test.

The headless audit observes actual Godot material resource bindings. It does **not** inspect
rendered framebuffer pixels, GS output, original-console timing, particle emission, loading
screens, script-controlled texture setters, or ride-script phase transitions.

Deliberate fault tests, all restored in `finally` blocks and followed by a restored build:

| Mutation | Observed result |
|---|---|
| `TextureTrack.Sample` returns `previous` instead of the key index | Data audit exit 2; failures rise from 10 to 40,493 |
| `AnimatedModel.SetFrame` iterates `_textureTracks.Take(0)` | Godot audit exit 2: Phantom at frame 0 expects `TV_PM_00`, gets `start` |
| Viewer clock calls `SetFrame(0)` instead of `SetFrame(_time)` | Godot audit exit 2: viewer never resolves `TV_PM_01.ssh` |

The first mutation cannot cause a zero-to-nonzero exit transition because missing textures
already fail the coverage gate; the increased sampler failures are the evidence. The two
Godot mutations do turn a passing test into a failing test.

After restoring the mutations, the data audit reproduces the same ten missing choices and
the Godot audit passes again. `dotnet build game/TPWPS2Viewer.csproj` succeeds. The existing
`TPW.PS2.TextureAudit` reproduces its baseline: 496 models / 6,007 first-name references,
5,940 TGA resolutions, 24 SSH resolutions, 43 unresolved and zero decoder exceptions.

## Not established / not implemented

* No claim that every numbered group is an animation, used at runtime, or described by APS.
  The `4Steve` alternatives and unused odd particle art need separate usage evidence.
* The full particle system, its emission/physics/color parameters, and all birth/update paths
  are not ported. The lifetime-to-sprite consumer is established; a free-running particle
  texture preview would not reproduce it.
* Loading children's **counter cadence in seconds** and their original UI are not implemented.
* Script/manual calls to the texture setter, runtime clock-scale changes and the debug
  disable flag are not connected to a game simulation. APS preview uses the existing scale-1
  clock and user-selected record.
* The ten unresolved multi-texture choices above remain unresolved. Their intended aliases
  or external library sources have not been established.
* UV scrolling is a separate problem; frame replacement does not claim to implement it.
* Validation is static executable analysis plus port data/material audits, not a PS2 trace
  or side-by-side pixel/timing comparison with the original executable.
