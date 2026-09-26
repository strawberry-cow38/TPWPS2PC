# Coaster trains, cars and physics (PS2): how they work

Researched 2026-09-26 for strawberry (the "how" pass on roller coasters). Source: `SLES_500.32` (PAL)
decompiled in Ghidra 12.1.2 with the ghidra-emotionengine-reloaded extension. Every load-bearing constant
and branch was checked in raw R5900 MIPS (`tools/r5900dis.py`). Tables were read from the ELF, and disc data
through the port's own `TPW.PS2.Data` readers; the scratch tools stay local and only numbers are recorded here.
Overview and cross-file notes: `coasters.md`. The "what" survey: `coaster-survey.md`.

**READ** means seen in the decompile, MIPS or data. **INFERRED** means reasoned to. Offsets are from
the object named. "Tick" means one call of the coaster update `vt+0x3c` (§2).

---------------------------------------------------------------------------------------------------

## 0. Short version

- **One physics step per tick, no interpolation.** The coaster update 0x122a48 steps every train
  once per tick (0x1238c0 → 0x1b0480). Car poses are written only inside that step, and the
  per-frame slot touches no train. Timers subtract the tick delta `D` (0x397640), which is
  `min(Δclock×128, 0x4000)` and is always 0x4000 in normal play. So the station timer of 0x27100 is
  **10 ticks**. (READ; D = 0x4000 comes from the bus clock notes.)
- **Speed is in cells per tick; position is in segments.** The train position is a float in
  `[0, n+2)`: the integer part counts segments after the station exit node, and the fraction is the
  spline parameter t. The position advances by `speed / arcLength(current segment)`. (READ)
- **Gravity is linear in height, not energy.** Each car looks ahead by its own speed along the
  curve and gains `0.04 × (y_now − y_ahead)`. The train speed is the mean over its cars ×
  (1 − f), with f = 0.04 on the approach and station segments and 0.001 elsewhere. There is **no
  maximum speed**. (READ, constants in MIPS)
- **The lift is a mode, not a track piece.** When the speed falls to ≤ 0.02 the train enters
  "chain mode" at exactly 0.04 (+0x254 = 1), and it leaves as soon as gravity gives > 0.04. A newly
  spawned train starts in chain mode. The chain *mesh* is found by simulating one train around the
  track (0x1239d8) and marking every sample the lead car crosses while in chain mode, plus the
  station and departure segments. (READ)
- **Loops:** on a loop segment (`node+0x53 == 2`), if the new speed is ≥ 0.08, the previous
  speed is kept. (READ)
- **Blocking:** a train holds, with its car speeds zeroed, whenever it is less than 0.75 segment
  behind the train ahead. The train ahead is index i−1, taken cyclically. (READ)
- **The station stops the train by position, not by braking.** When the lead car reaches t = 0.75 of
  the station segment (from the entry node to the exit node), the train simply stops (state 1). Its
  car speeds are **not** cleared, so it departs at its arrival speed minus friction. (READ; the
  consequence is INFERRED)
- **Station cycle:**
  1. Dwell 10 ticks.
  2. Unload one rider per 11 ticks (FIFO, car by car).
  3. Try to board: each attempt is 10 ticks of waiting then one queue-head guest (state 0x12), for
     at most 40 attempts.
  4. Leave when all seats are full, when the attempts run out, or **as soon as the next train is
     being held behind this one**.

  The ride-camera train never boards. (READ)
- **Trains:** `clamp((pylons/3 + 2) / carsPerTrain, 2, 6)` (integer division). They are placed at
  positions n+2, n+1, n, … (one segment apart, the first on the station segment), in chain mode
  with speed 0. There is no release-rate parameter: dispatch is the station state machine. (READ)
- **Seats per car** = the car model's 0x80 fittings: 6/6/2/6/4/3/4/4/2/4/4/3/4/6, in coaster order
  (§1.5). I counted them from the disc models with the port's parser. Two routes agree:
  `.sam MaxCapacity ÷ seats = coaster.sam uiMaxCars` for 11 of 12 coasters.
- **Sounds:**
  - The rumble loop (native category 4 `RIDES/grc`, event 17) has 32 sets, selected by parameter 7
    in bands of ten. The train writes 10/20/30/40/50/60/70 there for depart / first chain /
    after-first-crest / after-later-crest / running / mid-track chain or crawl / arrive.
  - The three trough coasters and Dare Devil use category 5, **whose map has no event 17** (INFERRED
    to be silent).
  - Scream rows are picked **once, at spawn, from an empty car 0**, so the one-rider row is always
    used. (READ; the consequence is INFERRED)
- **Ride-along camera:** it rides car 0 of train 0 (+0x140 is never written except to 0). The eye is
  at a per-coaster offset in the car's side/up/forward basis. The view rolls with the car. The pad
  turns the look ±90° yaw and ±60° pitch, at 0.05 rad per frame. The shake code is dead, because
  its amplitude is never raised. (READ)

---------------------------------------------------------------------------------------------------

## 1. The objects

### 1.1 Ownership (READ)

- The coaster (0xe390 bytes) owns a train sub-pool:
  - pool header `+0xd1b4` (`0x1df0b0`, registered by `0x15f9f8(hdr, 0x11b8)`, 0x11b8 = 6 × 0x2f4);
  - **6 trains** at `+0xd1c4 + i×0x2f4` (ctor `0x1af050`);
  - live count `+0xe37c` (0..6);
  - trains are used from slot 0 up and never compacted. `0x1224c8` removes them all and sets count
    = 0.
- Each train owns a car sub-pool:
  - header `train+0x0c` (`0x1df0b0`, `0x15f9f8(hdr, 0x230)`, 0x230 = 4 × 0x8c);
  - **4 cars** at `train+0x1c + k×0x8c`;
  - the ctor links them onto the free list at `+0x10` through each car's `+0x00/+0x04`;
  - how many are used = cars/train `0x1233a8()` = table `0x2acc80`.
- **Neither the train nor the car has a vtable.** Their ctors `0x1af050`/`0x1ae898` store none,
  and every call on them is direct.
- A car's model instance (`car+0x40`, from `0x230a98`) has vtable **0x36f290** (§1.4).
- "Train ahead" and "train behind" are **indices, not pointers**:
  - ahead = `(i − 1) mod count` (`0x1232f8`);
  - behind = `(i + 1) mod count` (`0x1b25c0` → `0x1234b8`).

  Spawn order puts train 0 in front. Trains never pass each other, because a train that closes to
  0.75 segment holds, so the order is permanent. (INFERRED from the blocking rule)

### 1.2 Train, 0x2f4 bytes

| off | type | meaning | written by | read by |
|---|---|---|---|---|
| +0x00/+0x04 | ptr | coaster train-pool links (INFERRED; zeroed in ctor) | `0x1af050` | — |
| **+0x08** | f32 | **position** in segments (§1.6); lead car | `0x1b19e0`, lap wrap `0x1b15f8` | step, gap `0x1232f8`, chain marker, test run |
| +0x0c..+0x1b | | car sub-pool header; `+0x10` free list | ctor | |
| +0x1c + k×0x8c | car[4] | cars (§1.3) | | |
| **+0x24c** | ptr | owning coaster | init `0x1af198` | everywhere |
| **+0x250** | s32 | train index 0..5 | init | gap, camera test `0x1b03e8`, `0x1b25c0` |
| **+0x254** | s32 | **chain (lift) mode**: 1 = held at 0.04 | init (=1), `0x1b14b4` (=1), `0x1b0844` (=0) | step, chain marker `0x1239d8` (as `coaster+0xd418`) |
| **+0x258** | s32 | **state** 0..4 (§3) | `0x1b1b08` only | `0x1b0480`, sound tails |
| +0x25c | handle | pass-by sound | `0x1af858` | |
| +0x260 | handle | **rumble loop** (§10) | `0x1af330`, `0x1af858` | many |
| +0x264 | handle | far loop | same | |
| +0x268 | handle | rider-ambience loop | same | |
| +0x26c | s32 | cached rumble param 7 ("state code" 10..70) | step, states 3/4 | |
| +0x270 | s32 | cached rumble param 6 (0/10) | step | |
| +0x274 | s32 | cached rumble param 8 (speed code 0..999) | `0x1af858`, wrap | |
| +0x278 | s32 | "first climb after the station" flag: 1 at init and at each arrival, 0 after the first chain exit | init, `0x1b1680`, `0x1b08d8` | step |
| +0x27c | | **unused** (no load or store in 0x1ae700..0x1b2700 or 0x11f990..0x124200, scan) | | |
| +0x280/+0x284/+0x288/+0x28c/+0x290 | s32 | scream event ids (dive base, crest base, speed, climb, chain), chosen at spawn | `0x1af330` | step |
| +0x294 | u32 | next time (ms clock `0x147158`) the climb scream may fire | step | step |
| +0x298/+0x29c/+0x2a0 | u32 | next times for the three speed screams | step | step |
| **+0x2a4** | s32 | **state timer** (0x27100 on entering states 1 and 3) | `0x1b1b34`, states 1/3 | states 1/3 |
| **+0x2a8** | s32 | **car cursor** for unload and board | init, wrap, states 2/4 | states 2/4 |
| **+0x2ac** | s32 | **boarding attempts left** (40) | state 2 | state 3 |
| **+0x2b0** | f32 | **previous speed** (the train speed of the last committed step) | init (0), step | loop hold |
| **+0x2b4** | ptr | **lead segment node** (the node whose segment holds car 0) | `0x1b19e0` | step (friction, loop, advance, sounds) |
| +0x2b8 | ptr | last node seen, for the trough splash test | init (0), step | step |
| **+0x2bc** | s32 | **held (blocked) flag** | step `0x1b05f0`/`0x1b05fc` only | states 3/4 of the train *ahead* |
| +0x2c0 | u32 | time of the last chain-exit rumble change (cleared after 2600 ms) | step, `0x1af858` | step |
| +0x2c4 | u32 | time param 6 was set to 10 (held ≥ 1900 ms) | step | step |
| +0x2c8 | u32 | slewed speed code (±40 per tick) | `0x1af858` | `0x1af858` |
| +0x2cc/+0x2d0/+0x2d4/+0x2d8/+0x2dc/+0x2e0 | handle | scream voices: chain, climb, speed, dive, speed-high, speed-top | step | step |
| +0x2e4..+0x2f0 | u32[4] | distance history to the camera, newest first (pass-by) | `0x1af858` | `0x1af858` |

`+0x2a4`, `+0x2ac` and `+0x2bc` are **not** initialised by `0x1af198`. The timers are always set by
their state's entry before use. `+0x2bc` can hold a stale value until the train's first state-0
step (READ; harmless in practice, INFERRED).

### 1.3 Car, 0x8c bytes

| off | type | meaning | writer |
|---|---|---|---|
| +0x00/+0x04 | ptr | train car-pool links | `0x1af050` |
| **+0x08..+0x10** | f32[3] | **position** (cells) | pose `0x1aeb00` |
| **+0x14..+0x1c** | f32[3] | **side** unit vector (the spline's bank/frame vector, re-orthogonalised) | pose |
| **+0x20..+0x28** | f32[3] | **up** = fwd × side | pose |
| **+0x2c..+0x34** | f32[3] | **forward** = unit tangent | pose |
| +0x38..+0x3c | | not touched by the car code read here | |
| +0x40 | ptr | model instance (vtable 0x36f290) | ctor `0x1ae898` |
| +0x44 | ptr | coaster | init `0x1ae9e0` |
| +0x48 + 4j | ptr[10] | riders (guest link nodes, guest = node − 8), FIFO | board `0x1aec68`, unload `0x1aed00` |
| **+0x70** | s32 | rider count (≤ coaster `+0x138`) | init (0), board, unload |
| **+0x74** | f32 | spline t on `+0x7c` | pose |
| **+0x78** | f32 | **speed**, cells per tick | init (0), car step `0x1aeeac`, train step (via train+0x94+k×0x8c) |
| **+0x7c** | ptr | segment node | pose; car step (look-ahead, then overwritten by the pose) |
| +0x80..+0x88 | f32[3] | predicted position `pos + speed × fwd` (for the ride statistics) | car step |

This **corrects coaster-survey.md §6.1**, which had `+0x14` forward and `+0x2c` side. The evidence
(READ):
- the spline's 5th output (the derivative basis) goes to `+0x2c`;
- `0x1aed90` moves along `+0x2c`;
- `0x1aeec0` measures vertical g along `+0x20` and lateral g along `+0x14`;
- the camera puts its "up" offset on `+0x20`.

### 1.4 Vtables involved (8-byte entries `{s16 delta, s16, u32 fn}`, call `vt+X` uses the fn at +X)

**Train and car: none** (§1.1).

**Coaster `0x35b060`**: the full dump is in coaster-survey.md §3.2. The slots the trains use:

| slot | fn | use here |
|---|---|---|
| `+0x03c` | `0x122a48` | the per-tick update; calls `0x1238c0` (step all trains) |
| `+0x044` | `0x120560` → `0x116a70` | per frame: queued guests `0x116e70` and station pose `0x1e52e0`. **No train or car code.** |
| `+0x05c` / `+0x054` | `0x1e1420` / `0x1e1440` | lock and unlock the DBA record (used by `0x122ce8` to read the sound family `+0xd3 & 3`) |
| `+0x0a4` | (base) | object kind; 1 = coaster (`0x1503a0`) |
| `+0x13c` | `0x122c80` | "may ride-cam": closed `+0x148` && valid `+0x144` (`0x152c48`) |
| `+0x1f4` | `0x1e4d70` | set status (board sets 10) |
| `+0x2e4` | `0x122c98` | ride-along camera (§11) |
| `+0x344` | `0x1204d0` | capacity = cars × seats × 6 (not used by boarding) |

**Car model instance `0x36f290`** (READ, ELF; built in `0x227158`, which stores `0x36f290` at
instance+0x30, the returned pointer being instance+0xc, so all deltas are −12):
```
+004 {-12,0,000000} | +00c {-12,0,228868} | +014 {-12,0,228898} | +01c {-12,0,2288c8} | +024 {-12,0,227940} | +02c {0,0,17c4d8}
+034 {-12,0,2288f8} | +03c {0,0,17c550}   | +044 {-12,0,228a08} | +04c {-12,0,228998} | +054 {-12,0,228a88} | +05c {-12,0,228958}
+064 {-12,0,228978} | +06c {-12,0,228938} | +074 {-12,0,228af0} | +07c {-12,0,228b78} | +084 {-12,0,228ba0} | +08c {-12,0,228bc0}
+094 {-12,0,228cf0} | +09c {-12,0,2296a8} | +0a4 {-12,0,2297c0} | +0ac {0,0,17c598}   | +0b4 {0,0,17d7a8}   | +0bc {-12,0,228860}
+0c4 {-12,0,227230} | +0cc {0,0,000000}
```

The slots the car uses:
- `+0x0c` load model (`0x1ae9e0`, model id `0x2e7220[w][p][o]`);
- `+0xac(1)` (after the load; meaning not traced);
- `+0x24` (car teardown `0x1aeab8`);
- `+0x34` destroy (`0x1ae8d0`);
- **`+0x74` set position** `0x228af0`: three s16 values, each × 1/256, into the matrix translation;
- **`+0x8c` set orientation** `0x228bc0`: rows X = side, Z = fwd, Y = fwd × side, then X =
  Y × Z, normalised. So the car model's local +X is side, +Y is up and +Z is forward. The row
  roles are READ; that `0x208af8`/`0x208a88` are normalise and cross is INFERRED from the call
  pattern.

**Guest (queue head)**: link node N, guest = N − 8, and the guest's vtable is at N + 0x10 (the +8
sub-object). The slots used are `+0xdc` (the state byte; 0x12 = standing at the front of the queue,
`native-ride-queue.md`), `+0x2c`, `+0x3c`, `+0x44` and `+0x16c` (event), all via `0x117c90`.

### 1.5 Per-coaster constants

Every table is indexed `[world 0..3][park 0..2][ordinal 0..2]`. World order is JUNGLE, HALLOW,
FANTASY, SPACE (READ: the table values line up with coaster-survey.md §1). Park index 2, the Test
Park, is first remapped by `0x11f930` through `0x2ace10` to the home triple. "Park 1/2" in
coaster-survey.md are park indices 0/1.

| coaster (w,p,o) | cars/train `0x2acc80` (stride o·8, p·0x18, w·0x48) | car spacing `0x2acc84` (cells) | **seats/car** (model 0x80 fittings) | riders/train | seat fitting ids | ride-cam eye `0x2e72b0` (side, up, fwd; stride o·0xc, p·0x24, w·0x6c) | sound family `+0xd3&3` |
|---|---|---|---|---|---|---|---|
| Temple of Gloom (0,0,0) `cart.mps` | 4 | 1.0 | **6** | 24 | 2,4,3,5,1,6 | (0, 0.7, −0.2) | 2 |
| Chak Atak (0,1,0) `croccar.mps` | 1 | 1.0 | **6** | 6 | 2,1,3,4,5,6 | (0, 0.5, −0.2) | 1 |
| Gorilla Thrilla (0,1,1) `ape.mps` | 1 | 1.0 | **2** | 2 | 1,2 | (0, **−0.5**, 0.5) | 2 |
| Hades (1,0,0) `maggot.mps` | 1 | 1.0 | **6** | 6 | 5,4,3,2,1,6 | (0, 0.7, −0.2) | 1 |
| Dare Devil (1,0,1) `car.mps` | 2 | **1.25** | **4** | 8 | 3,2,1,4 | (0, 0.7, −0.2) | 1 |
| Scatty Batty (1,1,0) `bat.mps` | 2 | 1.0 | **3** | 6 | 2,3,1 | (0, 0.4, −0.2) | 0 |
| Ghosta Coasta (1,1,1) `cart.mps` | 2 | 1.0 | **4** | 8 | 3,1,2,4 | (0, 0.7, −0.2) | 2 |
| Bone Shaker (1,1,2) `car.mps` | 3 | 1.0 | **4** | 12 | 1,2,3,4 | (0, 0.7, −0.2) | 2 |
| Big Dripper (2,0,0) `car.mps` | 2 | 1.0 | **2** | 4 | 2,1 | (0, 0.5, −0.4) | 1 |
| Caterpillar (2,0,1) `caterbd.mps` | 3 | 1.0 | **4** ⚠ | 12 | 1,3,3,2 (**no id 4**) | (0, 0.9, −0.2) | 2 |
| Candy Coaster (2,1,0) `car.mps` | 4 | 1.0 | **4** | 16 | 3,1,2,4 | (0, 0.7, −0.2) | 0 |
| Moonshot (3,0,0) `car.mps` | 1 | 1.0 | **3** | 3 | 1,2,3 | (0, 0.2, 0) | 0 |
| Escape Velocity (3,1,0) `cart.mps` | 2 | 1.0 | **4** | 8 | 4,2,1,3 | (0, 0.7, −0.3) | 0 |
| The Shocker (3,1,1) `car.mps` | 1 | 1.0 | **6** | 6 | 3,5,1,6,2,4 | (0, **−0.4**, 0) | 0 |

**Seats: READ in code, counted from data.**
- The code (READ): `coaster+0x138` is set at init by `0x1ae928`, which loads the model and counts
  its fittings with `flags & 0x80` (`0x17d360 → 0x1f2070`: `u16` count at hdr+0x36, 0x14-byte
  records at hdr+0x74).
- The counts come from the disc car models (the folder paths above), read with the port's
  `Model.Fittings`, which parses that same table ((local scratch)).
- Check by a second route: every `<name>.sam UsageInfo.MaxCapacity` is an exact multiple of the
  count (all 14 are). For 11 of the 12 coasters that have a `coaster.sam`, the quotient equals its
  PC `sTrainType.uiMaxCars` (e.g. Scatty Batty 18/3 = 6, Gorilla 8/2 = 4, Shocker 18/6 = 3).
  Caterpillar (16/4 = 4 vs 6) is the exception.
- Not verified: that the runtime table equals the file's. The port validated the same parser on
  `monkey.mps`.
- ⚠ **Caterpillar:** the seat ids are {1,3,3,2}, so the 4th rider of a car looks for fitting id 4
  (`0x1f1f78(res, 0x80, 4)`), finds none, and `0x17d3a0` attaches nothing. INFERRED: that rider is
  not drawn.
- Negative "up" eye offsets are Gorilla Thrilla and The Shocker. Their `.sam` claims
  `bIsSuspendedCoaster`, so the camera hangs below the track. Only Gorilla has its own mesh style
  (coaster-survey §4.5). (READ values; "suspended" is the `.sam` word)

### 1.6 The ring and the position coordinate (READ)

- The ring is: station exit node `+0x794` → pylon 0 … pylon n−1 → station entry node `+0x174` →
  back to `+0x794`. n = pylons `+0x170`, and the ring has **L = n + 2 segments**.
- The segment "at" node N is the Catmull-Rom curve from `prev(N)` to N (P1..P2 of the window
  prev-prev, prev, self, next; `0x19bda0`).
- **Train position p ∈ [0, L)**: walk ⌊p⌋ times `+0x30` (next) from `+0x794` to get the node, and
  use `t = p − ⌊p⌋`. So:

  | p | segment | runs from → to |
  |---|---|---|
  | [0, 1) | the **station segment**, at `+0x794` | entry node → exit node |
  | [1, 2) | the **departure segment**, at pylon 0 | exit node → pylon 0 |
  | [n+1, n+2) | the **approach segment**, at `+0x174` | last pylon → entry node |

- t is the **spline parameter, not arc length**. Distances along a segment are `t × arcLen`, where
  `arcLen = node+0xd0` = the sum of the 17 sample chords (`0x19b208`, area A).
- `+0x7c0` = `+0x794+0x2c` is exit.prev, the entry node. `+0x7c4` = `+0x794+0x30` is exit.next,
  pylon 0. The step uses both.

---------------------------------------------------------------------------------------------------

## 2. Tick rate and call order

### 2.1 Chain (READ)

- Once per `0x10eec0` call:
  1. `0x13af68` → `0x1c4aa8`:
     - writes **D** = `0x397640` = `min(0x11e688(), 0x4000)`, where `0x11e688` returns
       `(counter − previous) × 0x80` of the counter `0x2acab4`;
     - then calls the scene `vt+0x24` = `0x151800`.
  2. `0x151800` runs its body **once**. The 30× branch needs `0x230260(0x360218) != 0`, a stub
     returning 0 (track-ride-operation.md §2).
  3. The body calls `0x14be60`, which calls every object's `vt+0x3c` once. For a coaster that is
     `0x122a48`.
- `0x122a48` runs, in order:
  1. `0x123500`, `0x123598`, `0x123618`, `0x123698`, `0x1237d0`, `0x123840` (node and mesh work,
     area A);
  2. **`0x1238c0`** (trains);
  3. `0x1228d0` (breakdown);
  4. `0x1169c0` (base: the status tick `0x1e5138`, which is where status 10/2 spawns trains, the
     queued-guest update, the Life check).
- **`0x1238c0`**:
  - if `!(closed +0x148 && valid +0x144)`, remove every train (`0x1224c8`);
  - then, **unless status `+0x9a` == 5**, for each train `0x1b0480`:
    1. `0x1af858` (sounds, every tick, every state);
    2. the state function `switch(+0x258)`.

  So in status 5 (broken, reliability 0) trains freeze in place. In every other status they keep
  running, including 3 (closed by the player) and 4 (broken).
- The coaster object is in the object list that `0x14be60` walks. This is INFERRED: `0x122a48`
  has no direct callers and is reached only through vtable `+0x3c`, exactly like the track rides.

### 2.2 The delta D

- The counter `0x2acab4` gains 10000 per `0x10eec0` call when not paused (`0x11e758`,
  bus-native-clock-and-audio.md). So D = min(1 280 000, 16384) = **16384 = 0x4000 every tick**.
- The first call returns 0 (`0x11e688` initialises the snapshot). `0x1c4970` also zeroes D; it is a
  scene-init path (INFERRED from its call of the scene's `vt+0xc`).
- **Train timer 0x27100 = 160000 = 9.77 × 16384**, so a timed state lasts **10 ticks**:

  | tick in the state | 1 | 2 | … | 9 | 10 |
  |---|---|---|---|---|---|
  | timer after the subtraction | 143616 | 127232 | … | 12544 | −3840 |

  The transition happens in the **same** call as the subtraction that reaches ≤ 0 (unlike the
  bus). (READ)
- **Physics is not scaled by D.** Speeds are cells per tick and positions advance once per tick.
  (READ)

### 2.3 Rate and interpolation

- **Wall-clock rate:** one tick per render tick at the default gamespeed. On PAL that is **25
  ticks/s** (native-shop-flow.md; conditional, not proven for every path).
- **The statistics assume 30.** The ride-stats car hook `0x1aeec0` adds 0.033333335 s
  (`0x3d088889`) per step to the Duration, and reports max speed as `speed × 175`. INFERRED: the
  stats were tuned for 30 steps/s, so on PAL the shown Duration is 25/30 of real time.
- **No interpolation.** Car poses are written only by `0x1b19e0` → `0x1aeb00` inside the step, and
  the per-frame slot `+0x44` touches no car.
- The ride camera (§11) runs per camera frame, from `0x14f758` (camera.md), and reads car 0's pose
  as last written. (READ)

---------------------------------------------------------------------------------------------------

## 3. The train state machine (`+0x258`)

**Set state** `0x1b1b08(train, s)` (READ, MIPS `0x1b1b08..0x1b1b44`):
- `+0x258 = s`;
- if `s < 5`, jump through `0x3658c0` = {0x1b1b40, 0x1b1b34, 0x1b1b40, 0x1b1b34, 0x1b1b40}.
  `0x1b1b34` sets **timer `+0x2a4 = 0x27100`**, so only states 1 and 3 have an entry action.
- The entry runs even if the state is unchanged.

| state | fn | per tick | exit → next |
|---|---|---|---|
| **0 RUN** | `0x1b0518` | the physics step (§4); also the train's screams and rumble codes | `pos ≥ L + 0.75` → cursor `+0x2a8 = 0`, **→ 1**, `pos −= L` (arrival) |
| **1 DWELL** | `0x1b16b0` | `timer −= D` | `timer ≤ 0` **→ 2** (10 ticks after entry) |
| **2 UNLOAD** | `0x1b16f0` | if `cursor < cars`: pop the **first** rider of `car[cursor]` (`0x1aed00`); if the car was already empty, `cursor += 1`; **→ 1** (so every pop, and every empty-car check, costs 1 + 10 ticks) | `cursor ≥ cars`: `cursor = 0`, attempts `+0x2ac = 40`, **→ 3** |
| **3 WAIT** | `0x1b1778` | `timer −= D` | if `timer ≤ 0`: `attempts −= 1`; `attempts < 0` **→ 0**, else **→ 4**. Else, if the train **behind** (`(i+1) mod count`) has held flag `+0x2bc` set **→ 0** |
| **4 BOARD** | `0x1b1858` | see below | **→ 3** or **→ 0** |

**State 4 in detail** (READ, MIPS `0x1b1858..0x1b19d8`):
```
head = coaster+0xf4            // queue head link node, guest = head-8, may be 0
if (i != coaster+0x13c  &&  cursor < cars) {        // not the ride-cam train, seats left
    if (head && guest.vt+0xdc() == 0x12) {          // front guest standing at the front spot
        if (coaster status != 4) coaster.vt+0x1f4(10);   // set status 10 (entry runs every time)
        if (Board(car[cursor], head)) cursor += 1;   // 0x1aec68 returns "car now full"
        setState(3); return;
    }
    if (!trains[(i+1) mod count].held) { setState(3); return; }
}
setState(0);                                        // depart
```

**Departure sound tail** (states 3 and 4, READ): if the new state is 0, the camera mode ≠ 1 and the
train is audible (`0x1b0410`), rumble param 7 = 10 ("depart"), when it changed.

**Resulting station cycle** (INFERRED arithmetic from the READ rules, D = 0x4000):

| phase | duration (ticks) |
|---|---|
| arrival tick | 1 |
| dwell | 10 |
| unload, for r riders over c cars | 11 × (r + c), then 1 tick to reach state 3 |
| each wait → board attempt | 10 + 1 |

- Boarding therefore takes ≤ 1 guest per 11 ticks, and at most 40 attempts (successful or not)
  before a forced departure.
- **The train leaves at the first state-3 or state-4 tick at which the train behind is held.** The
  one exception is the state-3 tick where the timer expires, which does not check. With several
  trains close together, each dispatch boards only the guests that fit in the gap before the next
  train reaches the block point (INFERRED).
- The **ride-cam train never boards** (READ). It departs after one 10-tick wait.
- **The coaster's Capacity setting `+0xec` is not read by boarding** (READ absence in `0x1b1858`,
  `0x1aec68`). Only the seat count limits a car.

---------------------------------------------------------------------------------------------------

## 4. The physics step (state 0, `0x1b0518`)

All constants are MIPS-checked at the addresses shown.

```
Run(train i):                                        // 0x1b0518
  C = coaster; L = C.pylons + 2; nc = carsPerTrain(C)          // 0x1233a8
  if (C.trainCount >= 2) {                                      // slti 2 @0x1b0584
     gap = pos[(i-1) mod count] - pos[i];                        // 0x1232f8
     while (gap < 0) gap += L;  while (gap > L) gap -= L;
     if (gap < 0.75) {                                          // 0x3f400000, c.olt @0x1b05a4
        for k in cars: car[k].speed = 0;
        PlaceCars(train, train.pos);                            // same position, re-posed
        train.held = 1;                                         // 0x1b05f0
        return;                                                 // no sounds, prevSpeed unchanged
     }
  }
  train.held = 0;
  sum = 0; for k in cars: sum += CarGravity(car[k]);            // 0x1aed90 returns new car speed
  m = sum / nc;
  f = (train.node == C+0x174 || train.node == C+0x794) ? 0.04   // 0x3d23d70a @0x1b0660
                                                       : 0.001; // 0x3a83126f @0x1b0674
  v = m - m*f;
  // ... screams and rumble codes here (§10) ...
  if (!train.chain) {
     if (v <= 0.02) { v = 0.04; train.chain = 1; }             // 0x3ca3d70a, c.ole @0x1b1494; 0x3d23d70a
  } else {
     if (v > 0.04) train.chain = 0;                            // c.olt 0.04,v @0x1b0830; keep v
     else          v = 0.04;
  }
  if (train.node.kind(+0x53) == 2 && v >= 0.08) v = train.prevSpeed;   // 0x3da3d70a, c.ole @0x1b1550
  for k in cars: car[k].speed = v;                              // via train+0x94 + k*0x8c
  train.prevSpeed = v;                                          // +0x2b0
  PlaceCars(train, train.pos + v / train.node.arcLen);          // 0x1b15a0: len of the node BEFORE moving
  if (train.pos >= L + 0.75) {                                  // 0x3f400000, c.ole @0x1b15d4
     train.cursor = 0; setState(train, 1);
     train.pos -= L;                                            // cars are not re-posed (same place mod L)
     // + arrival sound codes (§10)
  }
```

```
CarGravity(car):                                     // 0x1aed90 (MIPS 0x1aed90..0x1aeeb8)
  node = car.node; len0 = node.arcLen
  (p0, side, fwd) = Spline(node, car.t); normalize(side); normalize(fwd)
  d = car.t * len0 + car.speed
  car.pred = p0 + car.speed * fwd                    // +0x80..+0x88, only for statistics
  while (len0 <= d) {                                // c.ole f21,f20
     node = node.next; car.node = node;
     d -= node.arcLen;                               // ⚠ subtracts the NEXT segment's length
  }
  p1 = Spline(node, d / len0)                        // ⚠ divides by the ORIGINAL length
  car.speed += (p0.y - p1.y) * 0.04                  // 0x3d23d70a @0x1aee7c
  return car.speed
```

**Faithfulness notes** (READ):
- The look-ahead distance is the car's current speed. The height difference is taken between the
  car's own position and that point, per car, and the train uses the **mean of the updated car
  speeds**.
- **Boundary quirk.** When the look-ahead crosses into the next segment, it subtracts the *next*
  segment's length and then divides by the *current* one.
  - If the next segment is longer, d can go negative, and the spline is evaluated at t < 0
    (Catmull-Rom extrapolation, behind the boundary).
  - The effect is a one-step speed error at segment joins where the lengths differ (INFERRED). A
    faithful port reproduces it.
- The car's node written by the look-ahead is thrown away: `PlaceCars` re-poses every car from the
  train position in the same step.
- **Friction** is chosen by the lead node *before* moving, and so is the length used for the
  advance.
- **Nothing zeroes car speed at the station.** The only writers of `car+0x78` are init
  (`0x1aea9c`), `0x1aeeac`, and the train step's set-all / held-zero (whole-range store scan). So a
  train leaves the station with its arrival speed (× 0.96 per step on the station segment). If that
  is ≤ 0.02, it drops into chain mode at 0.04.
- **No speed cap** anywhere in 0x1ae700..0x1b2700.

**What the law amounts to** (INFERRED; ignoring friction and the quirk): `v_after = v_before + 0.04 ×
(height lost in cells)`. It is path-independent and linear in height, not √h. On a constant grade g
(Δy per cell of travel), each step multiplies v by `(1 + 0.04·g)·(1 − f)`.

**Chain (lift) mode** (+0x254) summary:

| event | effect |
|---|---|
| spawn | chain = 1, speed 0 |
| chain = 1 and computed v ≤ 0.04 | v = 0.04 (flat or uphill; this includes 0.04 × 0.999 on the flat) |
| chain = 1 and v > 0.04 | leave chain mode, keep v (over the crest) |
| chain = 0 and v ≤ 0.02 | v = 0.04, chain = 1 (the train would stall) |
| chain = 0 and 0.02 < v | free running |

**Loop hold:** on a loop segment the speed is frozen at the value from before the lead car entered
it, provided the physics speed is still ≥ 0.08. Below that, the physics value is used and becomes
the new "previous", so a slow train can fall into chain mode (0.04) inside a loop (INFERRED from the
order: chain logic, then loop hold).

---------------------------------------------------------------------------------------------------

## 5. Placing cars on the spline and the car pose

```
PlaceCars(train, p):                                  // 0x1b19e0 (MIPS 0x1b19e0..0x1b1ae0)
  train.pos = p
  node = C+0x794
  while (p >= 1.0) { p -= 1.0; node = node.next }     // c.ole 1.0,p; delay slot lw 0x30
  train.node = node                                   // +0x2b4
  d = p * node.arcLen
  for k in 0..nc-1:
     PoseCar(car[k], node, d / node.arcLen)           // 0x1aeb00
     d -= spacing(C)                                  // 0x123430 → 0x2acc84 (cells)
     while (d < 0) { node = node.prev (+0x2c); d += node.arcLen }

PoseCar(car, node, t):                                // 0x1aeb00
  (car.pos, car.side, car.fwd) = Spline(node, t)      // 0x19bda0(t,node,&pos,&side,&tangent)
  car.up   = cross(car.fwd, car.side)                 // 0x1ae830(a,b,out) = a×b
  car.side = cross(car.up,  car.fwd)
  normalize(side); normalize(up); normalize(fwd)      // 0x1ae7a8
  inst.vt+0x74((s16)(int)(pos.x*256), (s16)(pos.y*256), (s16)(pos.z*256))   // → /256 translation
  inst.vt+0x8c(&side, &fwd)                           // rows X=side, Y=fwd×side, Z=fwd
  car.node = node; car.t = t
```

**Car 0 is at the train position.** Car k trails by k × spacing, measured as `t × arcLen` on each
segment (not true arc length), stepping back across segment boundaries. Dare Devil's 1.25 is the only
non-1.0 spacing.

**The spline outputs** (`0x19bda0`; the curve itself is area A):
- position (cells, y up);
- a **side** vector: `Σ basis_i × frame_i`, where frame_i are the node frame vectors at
  `+0x5b8..` that carry heading and bank;
- the **tangent**, from the derivative basis `((−3t²+4t−1)/2, (9t²−10t)/2, (−9t²+8t+1)/2,
  (3t²−2t)/2)`.

On a loop segment (`+0x53 == 2`):
- the side vector is `D = (P1.x − P2.x, 0, P1.z − P2.z)` (the one-cell sideways step, not
  normalised);
- the tangent is `normalize(D × (sin(2πt)·D.z, cos(2πt), −sin(2πt)·D.x))`.

Lead-in and exit blends ease position, side and tangent by `0.5 − 0.5cos(πt)`.

**Heading, pitch and bank** are not stored as angles for cars. The port can use the basis directly:
model X = side, Y = up, Z = forward. If angles are wanted (INFERRED, standard): heading =
atan2(fwd.x, fwd.z), pitch = asin(fwd.y), bank from `side` against the horizontal. Any bank comes
only from the node frame vectors (area A).

**Render position is quantised to 1/256 cell** (truncated through s16) by `vt+0x74`. The
orientation stays float. (READ)

---------------------------------------------------------------------------------------------------

## 6. The lift (chain) section and its marking

- **Physics:** the chain mode of §4. It is **not tied to marked track.** A train is in chain mode
  wherever its speed decays to ≤ 0.02, and for its first climb after spawning. (READ)
- **The marking** (`0x1239d8`, called only from spawn `0x1230b0`; READ):
  1. Only if closed and valid.
  2. Clear the winch flags of every node from `+0x794` up to, but **not including**, the entry node
     `+0x174` (`0x19b180`: 17 flags at `node + 0x11c + i×0x48`).
  3. `MarkRange(0.0, 2.0)` (`0x123b28`): the whole station and departure segments.
  4. Add a temporary train at position 1.0, in slot 0: in chain mode, speed 0, at the exit node.
  5. Loop:
     `prev = pos; Run-physics(train0) (0x1b0518 directly, no state machine); if train0.chain:
     MarkRange(prev, pos); until pos stops increasing` (the lap wrap).
  6. Remove the trains, then call `vt+0x144(1)` (mesh rebuild) on every node from `+0x794` up to,
     but not including, `+0x174`.
- **`MarkRange(a, b)`** walks segment by segment. On each segment it sets the flags of samples
  ⌊a·16⌋ .. ⌊b·16⌋ inclusive (`0x19b1a0`, `t × 16` → one of 17 samples).
- **INFERRED:** the entry node's flags are neither cleared nor rebuilt here. A chain mark on the
  approach segment could therefore go stale after an edit (not tested).
- How the flags change the mesh (the `chain.ssh` texture) is area A.

---------------------------------------------------------------------------------------------------

## 7. Blocking and the station

- **Gap** `0x1232f8(C, i)` = `pos[i−1] − pos[i]`, wrapped into [0, L] (MIPS `0x1232f8..0x1233a4`).
  i−1 wraps to count−1. The gap is **in segments**, so the physical distance of 0.75 segment
  varies with segment length (3..8 cells horizontally).
- **Held** = gap < 0.75, checked before moving. A train can therefore close to (0.75 − one step)
  and then hold.
- **The trailing cars of the train ahead are not considered.** A 4-car train with spacing 1.0
  extends 3 cells behind its position. INFERRED: on short segments the next train can visually
  overlap it.
- A held train restarts from speed 0, and usually from chain mode at 0.04.
- **Station geometry:**
  - The station segment runs from the entry node to the exit node (p ∈ [0, 1)).
  - The train **stops when its lead car reaches p ≥ 0.75** of the station segment. The stop point is
  t ≈ 0.75 (spline parameter), overshooting by up to one step.
  - The train behind is held at the station entry: its gap to a train at p ≈ 0.75 drops below 0.75
    as it passes p ≡ 0.
  - Friction is 4 % on the approach segment (`+0x174`) and on the station segment (`+0x794`).
  - **There is no separate brake**; the chain marking always covers the station and departure
    segments (§6).

---------------------------------------------------------------------------------------------------

## 8. Spawning (`0x1230b0`, `0x1231e8`, `0x1af198`)

- **Callers** (READ, xrefs):
  - `0x122af8`, the status-10 tick. It is also run by the status-2 tick `0x122ab0` and the status-4
    tick `0x122bb8`, all through vtable `+0x2a4`. It spawns only when closed `+0x148 != 0` and
    count == 0.
  - The test run `0x122d48`, after the stats lap.
- **Remove all** `0x1224c8`: every train runs `0x1af250`, which unloads every rider to the exit
  (`0x1aeab8` → `0x1aed00` → `0x117e08`), stops the three sound handles (`0x1af748`), and sets
  count = 0 and `+0xe384 = 1`. Callers:
  - `0x1238c0` (not closed or not valid, every tick);
  - `0x120868` (closing the ring);
  - `0x11c9f8`, `0x11bca8` (tool);
  - `0x11fd80` (remove);
  - the spawn, chain-mark and test-run functions.
- **Count** (MIPS `0x1230e4..0x123140`): `count = (pylons / 3 + 2) / nc` (integer division), then
  `if (!(1 < count)) count = 2`, then `if (!(count < 7)) count = 6`.

  | cars/train | pylons → trains |
  |---|---|
  | 1 | 0–2 → 2, 3–5 → 3, 6–8 → 4, 9–11 → 5, ≥ 12 → 6 |
  | 2 | 0–11 → 2, 12–17 → 3, 18–23 → 4, 24–29 → 5, 30–32 → 6 |
  | 3 | 0–20 → 2, 21–29 → 3, 30–32 → 4 |
  | 4 | 0–29 → 2, 30–32 → 3 |

- **Positions:** start at p = pylons + 2.0 (≡ 0: the start of the station segment, at the entry
  node). Each further train is at p −= 1.0, wrapped into ≥ 0 by adding L; this never happens,
  since count ≤ n/3 + 2 < L (INFERRED). So trains sit at n+2, n+1, n, …, one segment apart, with
  train 0 in front.
- **Train init** `0x1af198(train, C, index)` (MIPS):
  1. index `+0x250`, **chain = 1**, coaster, cursor = 0, `+0x2b8 = 0`;
  2. each car `0x1ae9e0`: model, `+0x78 = 0`, `+0x70 = 0`;
  3. state 0 (no entry action);
  4. sounds `0x1af330`;
  5. `+0x2b0 = 0`, `+0x278 = 1`, `+0x2c0 = +0x2c4 = +0x2c8 = 0`.

  Then `PlaceCars(p)`.
- **First moves** (INFERRED from the rules):
  - Train 0 creeps 0.75 segment in chain mode and stops (state 1), then does its empty unload pass
    (11 ticks per car).
  - Train 1 rolls to the station entry and is held there. Train 0 then departs at its first
    state-3 tick, **empty**.
  - Guests only board a train while no train is held behind it.
- **No release-rate parameter.** `.sam uiTrainReleaseRate` has no consumer (coaster-survey §2.5).
  Dispatch is entirely §3.

---------------------------------------------------------------------------------------------------

## 9. Boarding and unloading

- **Queue:** `coaster+0xf4` is the queue list head (base ride field; filled by the generic queue
  code, `native-ride-queue.md`). The guest is `node − 8`. Only a head whose `vt+0xdc` state == 0x12
  (standing at the front spot) boards. A head still walking up leaves the attempt empty. (READ)
- **Board** `0x1aec68(car, node)`, READ:
  1. `guest.vt+0x2c(1)`.
  2. `0x17d3a0(model, seat = car.riders, guest, coaster)` attaches the guest to the fitting whose
     **id = seat + 1** and flags & 0x80 (`0x1f1f78` → `0x1fa770` → `0x1f23b8`). A missing id does
     nothing.
  3. `riders[seat] = node`, `riders += 1`.
  4. `0x117c90(coaster, node)` (base PEEPON):
     - unlink from the queue;
     - guest state byte `+0x37 = 0x15`;
     - `guest.vt+0x3c`, `vt+0x44`;
     - link into the coaster rider list `+0x10c`;
     - **coaster riders `+0x120` += 1**;
     - event 0x13 to each remaining queued guest, with a `rand(3)` stagger.
  5. Return `seats(+0x138) ≤ riders`, i.e. whether the car is full.
- **Order:** cars are filled front to back (`cursor` 0..nc−1). Each car is filled to `+0x138`
  seats before moving on. One guest per board attempt.
- **Board also sets the coaster status to 10** (loading) unless it is 4. Nothing in the train code
  sets it back (area D).
- **Unload one** `0x1aed00(car)`, READ:
  1. If riders ≥ 1: take `riders[0]` and shift the array down (FIFO).
  2. `riders −= 1`.
  3. `0x17d428(model, riders)` detaches the fitting with **id = new count + 1**, i.e. the
     *highest occupied* seat, not the popped rider's seat.
  4. `0x117e08(coaster, node)`:
     - unlink from the rider list;
     - `0x1fb230`, `0x1fb3b8(g, 0)`;
     - guest state **0x16** (after-ride; see track-ride-operation.md §5 for what 0x16 leads to);
     - the guest's position becomes the **exit cell** (coaster `vt+0x74` anchor + `0x1e1a48`
       connection) × 256 + 0x80;
     - re-add the guest as a map object (`0x14da60`);
     - **coaster riders `+0x120` −= 1**.
  5. Return the node (0 if the car was empty).

  INFERRED: while unloading, the remaining riders' seat attachments do not follow the shift, so
  until the car is empty (≤ 11 ticks per rider) a departed guest's seat may still be bound. Not
  verified visually.
- **Where riders get off:** only in state 2 at the station, one per 11 ticks. When trains are
  removed (§8), every rider is dumped to the exit at once.
- **Per-car and per-train seats:** see §1.5. Coaster capacity (vt `+0x344`) = cars × seats × 6 is a
  different number (area D).

---------------------------------------------------------------------------------------------------

## 10. Sounds (`0x1af330` init, `0x1af858` every tick, screams in `0x1b0518`)

### 10.1 Camera modes and audibility

The camera mode is `0x395288` (`0x14dd68`; camera.md): 0 park, 1 first-person on a ride, 2 not
dispatched, 3 attached.

`audible(train)` = `0x1b0410`:
- false in modes 1 and 2;
- in mode 3, true only for the ride-cam train: `index == coaster+0x13c`, `0x1b03e8`;
- true otherwise.

**Family** = DBA `+0xd3 & 3` (`0x122ce8`). The code only ever tests **== 1** (READ), so families 0
and 2 behave identically. Family 1 = Chak Atak, Hades, Big Dripper and Dare Devil.

### 10.2 Native categories

The first argument of `0x111428(snd, cat, event, pos, &handle, flag)` is a category id
(sound.md). I resolved the event ids from the disc maps with the port's `SfxMap`
((local scratch)):

| cat | files | events used by trains (clip names) |
|---|---|---|
| 4 `RIDES/grc` | GRCSFX.MAP, bank `sound\Coast` = COASTHD.SDT | **17**: 32 sets, **selector param 7**. Links by param-7 band: 0–9 Whir01, 10–19 Grate01, 20–29 Winch01–04, 30–39 Roll01, 40–49 Accel01, 50–59 Slow01–03, 60–69 fastwinch01–08, 70–100 Brake01 |
| 5 `RIDES/wtr` | WTRSFX.MAP, bank `sound\Water` | 17 is **not in the map** (only 18/p6 and 19/p7). INFERRED: the family-1 rumble plays nothing |
| 9 `RIDES/fprc` | FPRCSFX.MAP, `sound\Coast` | **2** far loop ("Corkscrew Ext T", 3 sets, selector p5); **3** pass-by, no riders; **22** pass-by with riders (Peepass1–3) |
| 10 `RIDES/fpwt` | FPWTSFX.MAP, `Global\Water` | **23** far loop for family 1 (wr_flow_1–3, selector p5) |
| 7 `GLOBAL/kids` | KIDSSFX.MAP | **0xd5** ambkid01–06 (6 sets); 0xe5.. chain; 0xf5.. climb; 0x105.. speed; 0x115+band.. crest; 0x4b+3 dive (kid-l4) |

### 10.3 Handles

| handle | cat / event | started | parameters set | stopped |
|---|---|---|---|---|
| **+0x260 rumble** | 4 (5 if fam 1) / 0x11; 6th arg = (mode == 3) | init if audible; each tick in modes ≠ 1 if not playing and audible | p7 = `+0x26c` code, p6 = `+0x270`, p8 = `+0x274`; position updated each tick when mode ≠ 3 and audible (`0x111758`) | mode 1 (p8, p7, p6 set to 0 first); removal `0x1af748` |
| **+0x264 far loop** | 9 / 2 (fam 1: 10 / 0x17) | init if not audible; each tick in mode 1 if not playing | **p9 = 100 (fam 1: p11 = 100)**; position each tick | mode ≠ 1 (p9 = 0, stop); removal |
| **+0x268 rider ambience** | 7 / 0xd5, looped | init / tick, when mode 3, park open (`0x2b72a4` via `0x14e538`; bus-native-demand.md), riders on board, audible | p0x17 = trunc((1000 − `+0x2c8`)/10) when `+0x2c8` changes | mode 1; removal |
| **+0x25c pass-by** | 9 / 3, or 0x16 if riders | mode 1 only, fam ≠ 1, on ticks where `0x1498b8() & 3 == 0`, car 0 within 0xa00 (10 cells) in x and z of the point at `*(0x3952d4)` +0xa0/+0xa8/+0xb0 (the camera focus, INFERRED name): history `d3 > d2`, `d1 < d0` (the closest approach just passed) | p10 = 100 | restarted each time |

### 10.4 Rumble parameters

**Rumble state code, param 7 (`+0x26c`)**. It is written only when changed, in modes ≠ 1, when
audible (READ):

| code | band clip | when |
|---|---|---|
| 10 | Grate | departure: states 3/4 → 0 |
| 20 | Winch | entering chain mode (v ≤ 0.02) while `+0x278 == 1` (first climb after the station), or for family 1 |
| 60 | fastwinch | entering chain mode mid-track (`+0x278 == 0`, fam ≠ 1); **or** in free running with v < 0.04 (fam ≠ 1) once `+0x2c0` is clear |
| 30 | Roll | leaving chain mode with `+0x278 == 1` (then `+0x278 = 0`); stamps `+0x2c0` |
| 40 | Accel | leaving chain mode with `+0x278 == 0`; stamps `+0x2c0` |
| 50 | Slow | free running once `+0x2c0` is clear (2600 ms after a chain exit, `0x1af858`), except the 60 case |
| 70 | Brake | arrival (lap wrap); also p8 = 0, p6 = 0, `+0x278 = 1` |

- None of the chain-exit and running codes are written on the approach, station or departure
  segments (node == exit.prev, exit or exit.next).

**Param 6 (`+0x270`)**, on nodes other than the station exit and departure (READ):
- **Family 1 splash:** 10 when the train enters a node while the node it just left was a local
  minimum in pylon height (`0x19a1d0` = node `+0x44`: h(prev) < h(cur) and h(prev) <
  h(prev.prev)).
- **All families:** 10 while |fwd.z of car 0| > 0.99 (double `0x3658b0`; the component is `+0x34`,
  MIPS `0x1b13a0`). This tests the **world z axis**, which looks like a leftover; the code is
  exactly that.
- Back to 0 after ≥ 1900 ms (`0x76c`) once that condition is false.
- There is also a debug `printf("%f, %f, %f\n", |fwd|)` (`0x107e48`).

**Param 8 (`+0x274`)** = `min(999, trunc(speed / 0.4 × 1000))` (`0x1af800`, `0x3ecccccc` and
`0x447a0000`).
- `+0x2c8` slews toward it by at most 40 per tick.
- In mode 3 it also drives the pad motor: `0x1c18b8(pad, s × 255 / 999)` →
  `0x180db0(0x331280, 1, v)`.
- In mode 3 the sound **listener** is set to car 0's position each tick (`0x110e20`).

### 10.5 Screams (state 0 only)

All screams need a rider on any car (`0x1af2b8`) and the park open.

**The rider-count rows are chosen in `0x1af330` from car 0's riders.** `0x1af330` has one caller,
train init `0x1af198`, which runs it right after zeroing the cars (READ). So car 0 always has 0
riders at that moment, and the **"< 2" row is always the one used**: dive 0x4b, crest 0x115, speed
0x105, climb 0xf5, chain 0xe5. The ≥ 2 / ≥ 4 / ≥ 8 rows (0x4f/0x53/0x57, 0x119/0x11d/0x121,
0x109/0x10d/0x111, 0xf9/0xfd/0x101, 0xe9/0xed/0xf1) are never selected (INFERRED consequence).

| scream | condition (READ constants) | event (cat 7) | handle / cooldown |
|---|---|---|---|
| chain anticipation | chain mode, mode 3, fwd.y > 0.66 (`0x3f28f5c3`), now > global `0x2e747c` | `+0x290` | `+0x2cc`. While it plays and the global is 0, the global is set to now + (rand%3)·1000 + 2000 ms. The global is shared by all trains |
| crest | on leaving chain mode, not on the approach/station/departure segments; band from car-0 y×256 > 9850/8066/6283/4500 → 3/2/1/0, else none | `+0x284 + band` | one-shot |
| climb | not chain, fwd.y > 0.6 (`0x3f19999a`), v > 0.08 | `+0x28c` | `+0x2d0`; ≥ 3500 ms (`+0x294`) |
| speed | not chain, −0.5 < fwd.y ≤ 0.6, and v > 0.3 / 0.34 / 0.38 | `+0x288` (all three) | `+0x2d4` / `+0x2dc` / `+0x2e0`; each ≥ 3000 ms (`+0x298/+0x29c/+0x2a0`); debug strings "SCREAM SPEED", "SCREAM HIGH SPEED" |
| dive | not chain, v > 0.08, fwd.y < −0.66 | `+0x280 + 3` | `+0x2d8` if not playing |

- The time base for all cooldowns is `0x147158`, the millisecond clock (advisor.md).
- **Crest height units are not settled.** The bands 4500..9850 (car y × 256, i.e. node-y units) are
  above the pylon range 0..0x600 plus the station heights (§4.4 of coaster-survey). They may only be
  reached on high terrain. The unit question is area A's.

---------------------------------------------------------------------------------------------------

## 11. The ride-along camera (`0x1b1b48`), briefly

**Entry** (READ): `0x152c48(ride)`. Callers are `0x124450` (UI; the selected object at
`0x1497b0()+0x88`) and `0x137040`.
- If `ride.vt+0x13c` (coaster: closed && valid): target `0x2b72a0 = ride`, **camera mode 3**.
- Otherwise mode 1.

**Per camera frame:** `0x14f758` → `0x1503a0`, which calls `target.vt+0x2e4` = `0x122c98`.
- That sets `+0x13c = +0x140` if negative. `+0x140` is 0 from init and **no other store exists**
  (whole-.text scan: only `0x11fc7c`, `0x11fc84`, `0x122560`, `0x122cb4`).
- So the camera always rides **train 0**.

**Exit:**
- A button (`0x181700(0) & 0x181250(1)`): if the target kind `vt+0xa4 == 1`, `0x122558`
  (`+0x13c = −1`), then SetCameraMode(1).
- A breakdown to status 5 (`0x1228d0`) also forces the exit.

**The view** (READ, MIPS `0x1b1dc0..0x1b2060`):
- **Eye** = car0.pos + o.side·side + o.up·up + o.fwd·fwd, with o from `0x2e72b0` (§1.5; cells).
- **Look angles** are globals `0x2e7464` (yaw) and `0x2e7468` (pitch). Pad 0 held bits:
  - 0x10000 → yaw +0.05, 0x20000 → yaw −0.05;
  - 0x4000 → pitch +0.05, 0x8000 → pitch −0.05 (`0x3d4ccccd`, radians per frame);
  - clamped to yaw ±π/2 and pitch ±1.0471976 (π/3).
  - They are never reset (no other writer), so they persist between rides.
- **Shake:**
  - noise = amp × (rand%200/100 − 1), added to pitch and yaw;
  - the amplitudes `0x2e7474`/`0x2e7478` decay ×0.97 (`0x3f7851ec`) per frame;
  - **nothing ever raises them**: the only references are in this function, image value 0.0, and no
    data pointer. So the shake is always 0. (READ)
- **Orientation:**
  - the 3×3 from car 0's (side, up, fwd), rotated by pitch and then by yaw (sin/cos `0x28c910` /
    `0x28c828`), is set with the eye by `0x230758`;
  - the whole multiply-and-set is then repeated with angles 0, which is redundant.
  - Because the matrix **includes the car's up vector, the view rolls with bank and loops**. This is
    unlike the mode-1 ride camera, which never rolls (camera.md).
  - The exact order of the pitch/yaw multiplies relative to the basis is not transcribed (INFERRED:
    local pitch about side, then yaw about up).

---------------------------------------------------------------------------------------------------

## 12. Related, for the other areas

- **Test run** `0x122d48` (area D):
  1. zero the stats;
  2. remove the trains; add one at 1.0 (in chain mode);
  3. loop `0x1b0518` + `0x1b1ae8` (→ `0x1aeec0` on car 0 only) until the position stops increasing
     (the lap wrap);
  4. remove the trains, spawn normally, then run per-segment stats `0x19d5f0`.

  The test train uses exactly this physics. The g values are:
  - lateral = |side·(pos − pred)| × 20;
  - vertical = up·(pos − pred) × 100, with the maximum in `+0x160` and the minimum
    `min(+0x164, 0.25 × vertical)` in `+0x164`.
- **The PSX-derived draft** in `~/tpwport-coasterrun/core/TPW.Sim/CoasterSimulation.cs` cites
  0x800B.. PSX addresses (8 trains of 0x88, fixed-point gravity −4096, friction 32). **None of it
  is the PS2 mechanism above** (READ the file's constants; that it is the PSX game is INFERRED from
  the address range).

---------------------------------------------------------------------------------------------------

## 13. Still unknown (what I tried)

- **Wall-clock tick rate.** 25/s on PAL at the default gamespeed rests on native-shop-flow.md's
  pacing reading, which it calls conditional. I did not re-derive it. The pause path (D = 0) and
  whether objects update while paused were not traced.
- **Does a family-1 coaster really play no rumble?** Category 5 event 17 is absent from WTRSFX.MAP
  (read with the port's parser; the walk consumed the file exactly). What `0x111428` does with an
  unknown event (silence or a fallback) was not traced past the category lookup.
- **Parameters 6, 8, 9, 11 and 0x17** are set natively, but only param 7 (grc 17) and param 5 (the
  far loops) are the maps' selector ids. What the others do inside the voice was not followed.
- **The mode-3 pitch/yaw multiply order**, and whether `0x230758` transposes. The copy loop into
  `0x2f0480` suggests a transpose, but it is not settled.
- **Crest-scream height bands vs the y units.** They depend on area A's resolution of terrain and
  node y.
- **Rider-to-seat binding after an unload shift** (`0x1f23b8`/`0x1f24b0` flag the fitting and bind
  `0x1faef8(guest)`). I did not render it, so the "stale seat" consequence is unverified.
- **Seat counts** come from the file's fitting table, not the runtime copy (see §1.5 for the check
  that supports them).
- **`coaster+0xe384`** (1 after remove-all, 0 after spawn) still has no reader in the code I read.
