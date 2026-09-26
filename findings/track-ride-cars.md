# Track rides: the cars (how karts and boats move)

Researched 2026-09-26. Scope: the 0x98-byte car object and its two classes, the per-tick car
scheduler, and the kart-race minigame.
Source: `SLES_500.32` (PAL) decompiled in Ghidra 12.1.2 with the ghidra-emotionengine-reloaded
extension. Each load-bearing function was checked against raw R5900 MIPS (`tools/r5900dis.py`), and
tables were read straight from the ELF. For the overview, and for where the four track-ride files
were reconciled, see `track-rides.md`.

Tags:
- **READ** means I saw it in the MIPS or the decompile.
- **INFERRED** means I reasoned to it. Where the tag is missing, the line is READ.
- `rand(n)` is the game RNG `0x1448e0`, described in §8.

## 0. Units and conventions

| quantity | unit |
|---|---|
| distance along track `+0x50` | 256 per piece. `piece = (d % len) >> 8`, sample slot `= (d & 0xff) >> 6` (4 samples per piece, 64 apart) |
| track length `ride+0x2854` | pieces × 256 |
| heading `+0x4a` | 4096 per turn, kept in 0..0xfff. The model gets `−h·2π/4096` rad (0x2039c8, 0x204c38) |
| lateral `+0x5a` | 0..256 across the track. 0 is the piece-local x=160 edge (0xa0), 256 is the x=352 edge (0x160), and 128 is the centre (0x1fdba8). A piece is 512 world units (2 cells), so the lane band is 192 units wide |
| world position `+4/+6/+8` | s16. Sound uses `(s16 >> 8)` as a float, which is cells (0x203280, 0x2039c8) |
| tick | one call of the ride update (`vt+0x3c` = 0x200410 → 0x2023b0). Pose runs per frame, from 0x200728 → car `vt+0x2c` |

Car vtable pointer is at **`car+0x94`**. Entries are GCC-2 style `{s16 delta; s16 idx; fnptr}`, so a call
is `(*(vt+off))(car + *(s16*)(vt+off-4))`.

## 1. Classes and vtables

All three vtables are 0x60 bytes. Dumped from the ELF:

| slot | base `0x36c150` | boat `0x36bfb8` | kart `0x36c0a0` | role |
|---|---|---|---|---|
| +0x0c | 0x205338 | 0x203dc0 (→0x205338) | 0x2052d0 (→0x205338) | destructor. It deletes the model `+0x90` via model vt+0x34, then frees if `flag&1` |
| +0x14 | 0x2053a0 | 0x203360 | 0x203ec8 | init(ride, index) |
| +0x1c | 0x2057b0 (empty) | 0x203488 | 0x204088 | neighbour hook(behind, ahead) |
| +0x24 | 0x2057b8 (empty) | 0x203540 | 0x204240 | step(behind, ahead) |
| +0x2c | 0x2057c0 (empty) | 0x2039c8 | 0x204c38 | pose / draw, per frame |
| +0x34 | 0x205808 (empty) | 0x203280 | 0x203de0 | start the engine sound into `+0x44` |
| +0x3c | 0x205810 (empty) | 0x203318 | 0x203e78 | stop the sound. The kart version also prints `---- STOPPING KART ENGINE` |
| +0x44 | 0x205818 (empty) | 0x205818 (empty) | 0x203f10 | kart: reset to grid(index) |
| +0x4c | 0x205820 (empty) | 0x205820 (empty) | 0x2049d0 | kart: setState(s) |
| +0x54 | 0x205828 (empty) | 0x203bd8 | 0x205828 (empty) | boat: re-roll the tilt rates |

Non-virtual accessors sit at 0x2057c8–0x205800 (READ):
- get `+0x50`, get `+0x54`, get `+0x49`, get `+0x48`;
- set `+0x48`, set `+0x84`;
- get `+0x4a`, get `+0x84`.

**Construction.** `0x17a370(0x98)` (a malloc wrapper that goes to 0x29b640; it does NOT zero-fill), then
`0x205300`:
- sets the base vtable;
- `+0x90 = 0x230a98()`, a new 0x4c-byte scene object.

The creator then stores the class vtable at `+0x94`.

**Which class** (0x201ac0, READ): table `0x2ee528[world*8 + park*4]`, with world from `0x14e170` and
park from `0x14e160`. A nonzero entry gives a kart, zero gives a boat. Values:
- JUNGLE: park 0 kart, park 1 boat;
- HALLOW, FANTASY, SPACE: park 0 boat, park 1 kart.

Race mode (0x201c30) always makes karts.

**Boarding creates one car per guest** (READ, 0x201ac0).
- The "has room?" test `0x205638` is `return 0`, so every `PEEPON` allocates a new car at index
  `count` and calls `init(ride, count)`, with `a2 = count` checked in the MIPS at 0x201bb4.
- `0x205568` then pushes the guest (guest pointer into `+0x10+4*n`, `n = +0x34`, and seats it on model
  node `n+1` via 0x17d3a0).
- **This corrects track-rides.md**, which says a guest joins the last car if it has room. That path is
  dead code.

**Removal** (0x2005c8, READ):
- Every 10th global tick (`0x1c4930() % 10 == 0`), every car with `+0x88` (finished) set is unloaded
  and deleted by 0x2022a8.
- 0x2022a8 does nothing while `ride+0x138 != 0` (race mode).
- The loop does `i++` after deleting and shifting the array, so the car right after a removed one waits
  for the next 10-tick pass.

## 2. The car object (0x98 bytes)

W = writers, R = readers (function addresses). B = boat, K = kart, all = both.

| off | type | name | notes / W / R |
|---|---|---|---|
| +0x00 | ptr | next car on same piece | Link of the piece's car list `piece+0xa4`. Rebuilt every tick by 0x2023b0. R: 0x203488, 0x204088 |
| +0x04,+06,+08 | s16×3 | world X,Y,Z | K: 0x204c38 writes the real position. **B: 0x2039c8 writes uninitialised stack** (`sp+0x18/0x20/0x28`, never set in that function; READ at 0x203af0), so boats' values are junk. R: sound start 0x203280/0x203de0, kart crossing test 0x204088 |
| +0x0c | u8 | rank, 1 = leader | W 0x2023b0 (`count − i`), K reset sets idx |
| +0x10..+0x33 | ptr[] | guests | 0x205568 push / 0x2055d0 pop |
| +0x34 | u8 | guest count | zeroed by base init and kart reset |
| +0x35 | u8 | K colour 0..3 | reset `rand(4)`. Pose draws compiled-record resource word `+0xf0+4·c` |
| +0x36 | u8 | K base max speed | `12 + 2·rand(6)` = 12..22 |
| +0x37 | u8 | K current accel | set by setState |
| +0x38 | u8 | K base accel | `5 + rand(11)` = 5..15 |
| +0x39 | u8 | K state | §6 |
| +0x3a | u8 | K state timer | ticks |
| +0x3b | u8 | K "aggressive" flag | `rand(5)==0` (1 in 5) |
| +0x3c | s16 | B push-back | added to `+0x50` at the start of the next step, then cleared |
| +0x3e | s16 | B lateral wobble | −10..+10 |
| +0x40 | s16 | B wobble velocity | init `rand(11)+1`. K: written, never read |
| +0x44 | handle | engine sound instance | 0 at init. Restarted by the step if dead (0x111cc8) |
| +0x48 | s8 | speed | K: distance units per tick. B: quarter-units per tick |
| +0x49 | u8 | K current max speed / B target speed | |
| +0x4a | s16 | heading | 0..0xfff |
| +0x4c | s16 | tilt A | B: rocking, computed but **never drawn**. K: always 0, added to the track pitch |
| +0x4e | s16 | tilt B | B: rocking, never drawn. K: always 0, and its rotation call `0x1951d0` is `return 0` |
| +0x50 | u16 | distance | 0..len−1 |
| +0x54 | s32 | total distance, the sort key | **K only**: reset (0x204060) and step (0x20443c, 0x2044dc) are its only writers in the module. **Boats never write it** (§4) |
| +0x58 | s8 | lap | starts −1 (0xff) |
| +0x5a | s16 | lateral | init 0x7f (base, boat). K reset `0x40 + 0x80·(idx&1)` |
| +0x5c..+0x69 | rec | track sample A | 14 bytes, at `d` |
| +0x6a..+0x77 | rec | track sample B | at `d+0x40` |
| +0x78,+0x7a | s16 | B tilt A/B rates | 0x203bd8 |
| +0x7c | ptr | ride | base init |
| +0x84 | ptr | current piece | 0x2023b0 |
| +0x88 | u8 | finished | B/K step. R: 0x2005c8, minigame 0x1cb808 |
| +0x89 | u8 | index / grid slot | base init, K reset |
| +0x8a | u8 | `0x10f7f8(...)`, always 1 | purpose unknown |
| +0x8c | u8 | B cached sound param | 0x2039c8 |
| +0x90 | ptr | vehicle model instance | Mesh = table `0x2ecad0[(r+5)*4 + park*0x18c + world*0x4a4]`, where `r = libc rand()%4` (0x29cf08, a separate LCG). JUNGLE karts give 507–510, JUNGLE boats give 521 ×4 |
| +0x94 | ptr | vtable | |

**Track sample record** (14 bytes, piece `+0xb0 + 14·slot`, copied by 0x202898/0x1fdb20):

| off | field |
|---|---|
| +0 | xL |
| +2 | zL |
| +4 | xR |
| +6 | zR |
| +8 | y |
| +0xa | pitch |
| +0xc | yaw (4096/turn; may exceed 0xfff) |

Written by 0x1fdba8 (all but pitch) and 0x1fe4e8 (a whole-record copy at 0x1fe83c). That is geometry's
area.

The piece helpers:
- `0x2028f0(ride,d)` returns byte 7 of the piece-type entry (0x1fdb68 → 0x1fd6c0). It is **0x80 for
  every valid piece type** (dumped 0x2ee1e0 and all per-park tables).
- `0x202938(ride,d)` returns the sample yaw.

## 3. Pose (per frame): position from distance + lateral

READ in 0x2039c8 and 0x204c38 (identical code). With `A`/`B` the two samples, `f = +0x5a`, `s = d & 63`:

```
xa = A.xL + ((A.xR-A.xL)*f >> 8);  xb = B.xL + ((B.xR-B.xL)*f >> 8);  X = xa + ((xb-xa)*s >> 6)
za = A.zL + ((A.zR-A.zL)*f >> 8);  zb = B.zL + ((B.zR-B.zL)*f >> 8);  Z = za + ((zb-za)*s >> 6)
Y  = A.y + ((B.y-A.y)*s >> 6)
model(+0x90).vt54(yaw = -h*2π/4096);  model.vt74(X,Y,Z)
```

- **Boat:** only yaw and position reach the model, so the boat tilts are dead state.
- **Kart:** it also builds a matrix and draws through the ride's own render instance (`ride+0x28`,
  0x10f7b8), with the matrix at `ride+0x38..0x67`. The rotation order is `rot(+0x4e)` (a stub),
  `rotP(+0x4c + A.pitch)` (0x194fc0), then `rotY(+0x4a)` (0x1950c8), plus the translation.

The sample pair is refreshed only when `d >> 6` changes (every step does this).

## 4. The per-tick scheduler `0x2023b0` (READ)

1. `0x2027e0`: lazily builds one piece's samples per tick until all are built. This is geometry's area.
2. **Skip everything** if `(count == 0 || ride+0xa2 == 10) && ride+0x138 == 0`. Status 10 freezes all
   cars except in race mode.
3. If `0x14dd68() != 2`, call ride `vt+0x364` (0x117b88, wear; lifecycle's area).
4. Clear every piece's list `+0xa4`. For each car:
   - `car+0x84 = &piece[(d % len) >> 8]`;
   - push the car at the list head (`car+0 = old head`).
5. **One car:** `hook(car, 0, 0)`, then `step(car, 0, 0)`.
6. **Two or more cars:**
   - Build the pairs `(key = s32 car+0x54, car)`.
   - Exchange-sort ascending: `for i, for j>i: if key[j] < key[i] swap`. It is signed and strict, so
     ties keep array order.
   - Set `rank(s[i]) = n − i`, which gives rank 1 to the largest total.
7. Neighbours are **cyclic in sorted order**: `behind = s[i-1]`, `ahead = s[i+1]`, with `s[-1] = s[n-1]`
   and `s[n] = s[0]`. With n = 2 both neighbours are the other car. Call **all** hooks
   `vt+0x1c(s[i], behind, ahead)` in order `i = 0..n-1` (last place first), **then** all steps
   `vt+0x24(s[i], behind, ahead)` in the same order.

Consequences:
- **Boats never write `+0x54`** (no store at that offset in the boat or base code; `mod.s` scan). They
  are sorted on whatever the allocator left there. The allocator does not zero-fill (0x29b640).
- Boat "rank" and "ahead" are therefore arbitrary but stable per boat. If the memory is zero, the order
  is array (creation) order, so a boat's "ahead" is the **next-created** boat.
- Port choice (INFERRED): zero it, which gives creation order.

## 5. Boats (class A)

### Init (0x203360 after base init 0x2053a0), READ
- **Base init**, in order:
  - `+0x7c = ride`, lap = −1, lateral = 0x7f;
  - `d = len − 64(i+1)` (overwritten below), samples, speed 0;
  - `yaw = trackYaw(d)`, `+0x44 = 0`;
  - `+0x40 = rand(11)+1`, tilts 0, guests 0, `+0x88 = 0`;
  - model mesh `rand()%4`, `+0x89 = i`, start the sound.
- **Boat:**
  - wobble = 0, push-back = 0;
  - **`target(+0x49) = ride_speed / 20`**, where `ride_speed` = ride `vt+0x2f4` = `*(s32*)(ride+0xf0)`
    (0x118398), with signed division;
  - re-roll the tilt rates, `lateral = 0x7f`;
  - **`d = len − 200·(i+1)`**, samples, `yaw = trackYaw(d)`.
- `ride+0xf0` looks like a percentage near 100: 0x202188 normalises it by /100 and clamps to
  0.75..1.25 (INFERRED). So the first target is about 5 and matters only for the first surge.
- Karts never read this setting (only 0x203394 calls `vt+0x2f4` among car code).

### Neighbour hook 0x203488 (READ): spacing and crossings
```
if ahead:
    g = |ahead.d - self.d|            # raw u16 difference, NO wrap handling
    if g < 25:   self.speed = 0; self.pushback = -10
    elif g < 200:                    self.pushback = -5      # speed kept
p = self.piece
if p.cross (piece+0xac: 0x1fd818 links two pieces that share a cell, for piece shapes 4 and 99 only,
            both ways; that is the crossing piece):
    m = max over cars c in p.list and p.cross.list of (c.d & 0xff)    # includes self
    if m != (self.d & 0xff): self.speed = 0; self.pushback = -5
```
- **Boats do not pass through each other.** Within 200 of the neighbour a boat loses 5 units per tick;
  within 25 it stops and backs off 10.
- At a crossing, only the boat furthest into its piece continues. The rest stop and are pushed back 5
  per tick until they are off the crossing piece, so they jitter at its entrance.
- Because the neighbour is the sort neighbour (§4), the pair checked is not necessarily physically
  adjacent.

### Step 0x203540 (READ, every line checked in MIPS)
```
oldBucket = d >> 6
if sound dead: vt34()
if pushback: d += pushback; pushback = 0         # u16; going below 0 wraps to ~65530
d += speed >> 2                                   # arithmetic shift of s8 (floor /4)
if d >= len:
    lap += 1; d %= len
    if status(ride+0xa2) != 2 and lap >= LAPS(ride vt+0x304): finished = 1
if d>>6 != oldBucket: A = sample(d); B = sample(d+0x40)
if speed < target: speed += 1
else:
    target = 60 - 2*rand(11)          # 40,42..60
    speed  = target - rand(11)        # target-10 .. target
sound.param[4] = (int)(speed*100.0f/target)                 # 0x111d40
lt = 0x80 (piece byte 7 at d+0x100) + wobble                # the lateral TARGET
lateral moves 2 toward lt, clamped; flag = 1 if it clamped onto lt
wobble += wvel
if wobble < -10: wobble = -10; wvel = rand(11)+1          # +1..+11
elif wobble > 10: wobble = 10; wvel = -rand(11)-1          # -1..-11
ty = trackYaw(d+0x100)
if |ty-yaw| >= 0x7ff: (ty < yaw) ? ty += 0x1000 : yaw += 0x1000
yaw moves 0x14 toward ty (clamped); yaw &= 0xfff
L = (status == 2) ? 25 : 150
tiltA += rateA; clamp to [-L, L], flag on clamp
tiltB += rateB; clamp to [-L, L], flag on clamp
if flag: vt54()  -> rateA = rand(11), rateB = rand(11);
                    rate = (status==2) ? rate*8-40 : rate*16-80    # -40..40 or -80..80
```

Answers to the brief:

- **Speed units.** `+0x48` is in quarter-units. Distance per tick is `floor(speed/4)`, so about 7 to 15
  units per tick in steady state (speed 30..60). Acceleration is +1 quarter-unit per tick², up to the
  target. The **surge** is a jump at each re-roll (up or down), then a ramp of 0 to 10 ticks.
- **The ±10 is a lateral wobble, not a height bob.**
  - `+0x5a` is the lateral coordinate (§3). Y comes only from the samples.
  - The wobble is a triangle wave between ±10 with a random slope of 1..11 per tick.
  - The boat's lateral chases `128 + wobble` at 2 per tick, so it wanders roughly 118..138 of 256.
  - **This corrects track-rides.md** ("height eases toward the track's height plus a random ±10 bob").
- **Yaw.** It eases 20/4096 per tick toward the yaw sample one piece ahead (`d+0x100`).
- **Tilt.** There are two independent axes, each a random walk between limits:
  - The rate is re-rolled whenever any limit clamps, or when the lateral lands on its target.
  - Limits and rates are ±25 and ±40 when `ride+0xa2 == 2`, otherwise ±150 and ±80.
  - **Neither angle is ever applied to the boat model** (§3; the only in-module readers of
    `+0x4c/+0x4e` are the kart pose). A port that wants rocking has to invent the mapping.
- **Status 2.** It is `ride+0xa2 == 2` (`0x1e1d70(ride+8)` reads `ride+8+0x9a`). The same value also
  disables finishing, so boats never set `+0x88` while in status 2.
  - INFERRED meaning: 2 is the normal running state. 0x200448 sets it when the boarded count
    `+0x128` reaches capacity (`vt+0x2fc`), and 0x1e26d8 toggles 2↔3.
  - Under that reading, a running water ride's boats circulate forever and rock gently. In any other
    status (closing, broken 4/5) they rock ±150 and finish at the lap target, and are then unloaded by
    0x2005c8. The status names are in `track-ride-operation.md` §3: 2 is running, 11 is unloading.
- **Water current or flow.** None. The only per-piece inputs are the lateral centre (constant 0x80)
  and the yaw sample. There is no slope or flow term (READ, by the absence of any other read in
  0x203540).
- **Sound.**
  - Instance: `0x111428(mgr, 6, 4, pos, &+0x44, 0)`, a looping event (bank/group 6, event 4) positioned
    at the car.
  - Parameter slot 4 = `speed·100/target` (0..100), set every step, and again in the pose (integer
    division, only on change, cached in `+0x8c`).
  - INFERRED: slot 4 is the engine-revs parameter of the event map (`JUNGLE_ENGINE_REVS`). Not mapped.
- **RNG.** `rand(11)` gives 0..10 (§8).

## 6. Karts (class B)

### Reset to grid 0x203f10(i), called by init 0x203ec8 and by the race reset. READ
In this order:
- lap = −1, `d = len − 100·(i+1)`, samples, `yaw = trackYaw(d)`, speed 0;
- `+0x40 = rand(11)+1`, tilts 0, guests 0, finished 0, `+0x89 = i`;
- `aggressive = (rand(5)==0)`, `colour = rand(4)`;
- `baseMax = 12 + 2·rand(6)`, `baseAccel = 5 + rand(11)`;
- `rank = i`, `lateral = 0x40 + 0x80·(i&1)` (a two-wide staggered grid at 64/192), `total = d`;
- `setState(0)`.

The grid is 100 units per slot **behind** the start line. The line is at `d = 0`, the station piece.

### setState 0x2049d0(s) (READ)
If race mode (`ride+0x138`) and this is the player's car (`+0x89 == minigame+0x179`), then
`s ∈ {1,3,4}` becomes 2.

| s | effect |
|---|---|
| 0 | `rand(11)` discarded; speed = 0; timer = 1 |
| 1 | accel = baseAccel>>1; max = baseMax>>1; timer = 80 + 2·rand(11) |
| 2 | accel = baseAccel; max = baseMax; timer = 0 |
| 3 | **only if current state == 2**, else nothing: accel = baseAccel<<1; max = baseMax + rand(6); timer = 10 + 2·rand(11); one-shot sound (6, 0xf) |
| 4 | timer = 20 (accel and max unchanged) |
| 5 | timer = 8 + trunc((s16)(trackYaw(d) − heading) / 512); one-shot sound (6, 0xf) |
| 6 | timer = 5 + 2·rand(11) |

Then, for the player's car only: if `s == 2`, set state = 3; timer = 1; `max = baseMax + rand(6)`.

### Neighbour hook 0x204088(behind, ahead) (READ)
```
if state in {0,5,6} or behind == 0: return
g = (ahead.d + ahead.speed) - (self.d + self.speed)       # predicted next positions
if ahead.d < self.d: g += len                             # wrap
if g < 100:
    if |self.lateral - ahead.lateral| < 20: setState(self.speed >= 21 ? 5 : 6)   # collision
elif g < 300: setState(3)                                 # overtake (only from state 2)
# crossings: cars on the crossing partner piece only (not the own piece)
if piece.cross:
    for c in piece.cross.list:
        if (self.d & 0xff) < (c.d & 0xff) and |self.x-c.x| < 80 and |self.z-c.z| < 80:
            setState(5); break
```

### Step 0x204240(behind, ahead) (READ, every branch checked in MIPS)
```
if sound dead: vt34()
if finished:                                              # parking
    lateral += trunc((11 - lateral)/4)
    step = min(max(+0x49), trunc(((5-rank)*100 - d + 1)/2))  # signed
    d += step; refresh samples on bucket change; return   # (no rubber band)
if state == 0:
    race ? (minigame+0x28 == 2 -> setState(2)) : (--timer == 0 -> setState(2))
elif state in 1..4:
    total += speed; d += speed
    if d >= len: lap += 1; d %= len
        if lap >= LAPS: finished = 1; total += (5-rank)*100 - d
    refresh samples on bucket change
    if player car: lt = minigame.slot*0x38 + 0x10          # skip AI speed and timers
    else:
        speed += accel; if speed > max: speed = max - rand(6)
        sound.param[4] = (int)(speed*100.0f/max)
        if state==1: if --timer==0: setState(2)
        elif state==3: if --timer==0: setState(2)
        if state==2 and aggressive and behind and behind.state==3 and rand(11)==0: setState(4)
        elif state==4: if --timer==0: setState(2)
        lt = state==3 && ahead ? (ahead.lateral  >= 100 ? 0x32 : 0xcd)
           : state==1 && ahead ? (behind.lateral >= 100 ? 0x32 : 0xcd)   # reads BEHIND
           : state==4 && behind && behind.state==3 ? behind.lateral
           : 0x80                                         # piece byte 7
    lateral += trunc((lt - lateral + 3) / 4)              # C division; asymmetric
    ty = trackYaw(d + 4*speed); wrap as boats (>= 0x7ff)
    heading moves 4*speed toward ty (clamped); heading &= 0xfff
elif state == 5:
    if not player: heading = (heading + 0x200) & 0xfff    # 1/8 turn per tick
    speed >>= 1 (arithmetic); if --timer==0: setState(6)
elif state == 6:
    speed = trunc(speed/4)*3; if --timer==0: setState(1)
# every non-finished path ends here:
if race and cars[4].rank == 1 (0x201dd8) and not player: speed += 2
```

### Kart state machine

| state | name (INFERRED) | per tick | exit |
|---|---|---|---|
| 0 | grid | no movement, speed 0 | Guest mode: next tick (timer 1) → 2. Race: when minigame `+0x28 == 2` → 2 |
| 1 | yield (half pace) | moves. accel ½, max ½. Lateral → the lane **opposite the car behind** | timer 80..100 → 2 |
| 2 | racing | moves. Base accel and max. Lateral → centre 128 | hook: gap 100..299 → 3; gap <100 and Δlat <20 → 5/6; crossing → 5. Aggressive and car behind in 3, 1/11 → 4 |
| 3 | overtaking | moves. accel ×2, max + rand(6). Lateral → the lane opposite the car ahead | timer 10..30 → 2 (re-entered next tick if still 100..299 behind) |
| 4 | blocking | moves. Lateral → the overtaker's lateral (if the car behind is still in 3), else 128 | timer 20 → 2 |
| 5 | spin-out | **no movement**. heading +0x200 per tick, speed >>1 | timer `8+Δyaw/512` (1..18) → 6. The spin ends within 512 of the track yaw |
| 6 | stall | **no movement**. speed ×¾ (trunc/4·3) | timer 5..25 → 1 |
| — | finished (`+0x88`) | parks: lateral → 11, d → (5−rank)·100 | guest mode: deleted by 0x2005c8 within 10 ticks |

Timers are only processed for non-player cars.

- **Lanes 0x32/0xcd.** They are 50 and 205 of 256 across, the two overtaking lines.
  - Grid lanes are 64/192, and the default line is 128.
  - The lateral moves by `(target − lat + 3)/4` per tick, so about ¼ of the gap. Rounding stalls it up
    to 6 above the target when moving down (READ: the `+3/+6` bias at 0x2047b8).
- **Overtaking is deterministic, not a 1-in-11 roll.**
  - A car in state 2 whose predicted gap to the car ahead is 100..299 goes to state 3.
  - It swings to the opposite lane with double acceleration and extra top speed.
  - "Blocked" (collision) means **predicted gap < 100 and lateral difference < 20**. That gives a spin
    (speed ≥ 21, only reachable via state 3's `+rand(6)` or rubber-banding) or a stall.
  - **The 1-in-11 roll is the block**: an aggressive kart (1 in 5) whose car *behind* is overtaking
    goes to state 4 and moves into the overtaker's lateral. **This corrects track-rides.md.**
- **Spin-out.**
  - Triggers: collision at speed ≥ 21, or a crossing conflict.
  - Heading +0x200 per tick. The timer is chosen so the kart does one full turn plus the correction to
    the track yaw.
  - Speed halves each tick, and the position is frozen. Then state 6 (frozen, speed ×¾ per tick), then
    state 1 (half pace for 80..100 ticks, moving aside for the car behind), then state 2.
  - The following karts can pile into a stationary kart (gap < 100, same lane).
- **Steady speeds.** Speed settles in [max−5, max]: each tick `speed + accel ≥ max+1` is re-rolled to
  `max − rand(6)`.

| kart speed quantity | value |
|---|---|
| base max | 12..22 (even) |
| accel | 5..15 |
| state 1 | max 6..11 |
| state 3 | max ≤ 27, accel 10..30 |

  Units are distance units per tick (256 = 1 piece).
- **Turn rate.** It is 4·speed per tick toward the yaw sampled `4·speed` ahead (a speed-scaled look-ahead).
- **Finishing and order.**
  - Lap starts at −1, so the first crossing of the line makes lap 0.
  - When `lap ≥ LAPS` after a wrap, the kart sets `+0x88` and `total += (5−rank)·100 − d`. Its total
    is frozen from then on, so the finishing order is preserved in the sort.
  - Finished karts drive to `(5−rank)·100` past the line and pull to lateral 11.
  - With 6 to 8 guest karts (capacity upgrades), ranks > 5 give a negative goal, and the u16 distance
    then jumps around the track. They are deleted within 10 ticks, so this is brief. READ; treat it as an
    original quirk.
- **LAPS.** `ride vt+0x304` → 0x1183e8 = `*(s32*)(ride+8+0xf0)` = **`ride+0xf8`**. It is the ride's **Duration** setting,
  default 5, range 1–10 (`track-ride-operation.md` §5). It is also used by 0x200518: the cycle ends at `2·LAPS` of `+0x12a` → status 11.
- **Sound.**
  - Loop: `0x111428(mgr, 6, 4, pos, &+0x44, 0)` in 0x203de0.
  - Parameter 4 = `speed·100/max`, set by the step (AI only).
  - One-shot `(6, 0xf)` on entering state 3 or 5.
  - The event names are not resolved.

### Kart pose extras (0x204c38)
- The mesh drawn through the ride instance is compiled-record word `+0xdc + (colour+5)·4`.
- For the player's car (race mode):
  - camera target `minigame+0x170..0x174 = (X,Y,Z)`;
  - eye `+0x168..0x16c = (X,Y,Z+0x100)`. A rotated forward vector is computed and then unused.
  - HUD: `"%s: %i/%i"` with string 0x265, `min(lap+1, LAPS)` and LAPS, drawn at (0x24,0xc4).

## 7. Car ordering summary (answers item 4)
- **Sort, rank and neighbours:** as in §4.
- **The two hooks by class:** `+0x1c` is spacing and crossings (boat §5) or collision/overtake detection
  (kart §6). `+0x24` is the step.
- **Laps:** increment on each wrap past `len` inside the step.
- **Spacing rules:** boats use 25/200 push-back plus crossing priority. Karts use collision (gap < 100
  and Δlat < 20), overtake (gap < 300) and the crossing spin (80×80 box).
- **No other separation.** Two karts in different lanes can overlap freely, and so can two karts on
  either side of a wrap except via the gap test.

## 8. RNG (READ)

`0x144870`: a lag-4 add-with-carry generator. State words are at 0x2b68d0 (last output), then
`s1..s5` at 0x2b68d4..0x2b68e4.

```
x = s1 + s4 (mod 2^32); if carry: x += 1; x += 1
s5 = s4; s4 = s3; s3 = s2; s2 = s1; s1 = x; out = x
```

- `rand(n)` (0x1448e0) = `x % n`, **unsigned** (`divu`), so 0..n−1.
- Seeded once at boot (0x1dee88 → 0x144818) from 0x2b68b8:
  `{abcd1234, ffde1534, ffde1534, 001a1010, f61a1890, 00000002}` → (out, s1..s5).
- The generator is shared by about 140 call sites across the game, so exact sequences are not
  reproducible in isolation.
- The car's model choice uses libc `rand()` (0x29cf08, LCG ×0x41c64e6d+0x3039) instead.

## 9. Race mode: the kart minigame (selector 7)

**Start path.**
- The walk-around mode's per-frame 0x137040 looks at the attraction under the player. It reads the
  compiled record's minigame selector (`record+0x16`).
- For selector 7 it also requires ride `vt+0x114` (0x1e2798), which is true iff status is one of
  {2, 4, 7, 8, 10, 11}. It then shows prompt 0xb.
- Pressing logical button 0x15 sets `global(0x395090)+0x88 = attraction` and calls
  `0x152668(7)` → `0x1debc8(7)` → `0x1cb1d0(new 0x1b8)`.
- The object is kept in global 0x3953cc. Its vtable is at `+0x158` = 0x367b18. Base: 0x1dd588 / 0x3698e8.

**Constructor 0x1cb1d0.**
- `ride = obj+0x20 − 8`, then `0x201c30(ride)`, `ride+0x138 = obj`, `0x1cb6b0(obj)`, `+0x179 = 0`.
- It saves the ride instance's matrix (48 bytes) to `obj+0x180`. The minigame update restores it every
  frame, because the kart draw uses the ride instance as scratch.

**0x201c30** (READ):
- Moves every existing car to `ride+0x2894[]` (the rest up to 17 are zeroed).
- Hides each car's model (model `vt+0xac(0)`) and its first guest (`(guest−8)` `vt+0x2c(0)`).
- Sets count 0, then creates **5 kart-class cars** with `init(ride, i)`, i = 0..4, then calls ride
  `vt+0x54` (0x1e1440).

**0x201e00** (teardown, from dtor 0x1cb650 after `ride+0x138 = 0`):
- Stops and deletes the 5 race karts.
- Moves the saved cars back and unhides their models and guests. They resume with their old state.

**Minigame object fields used.**

| field | meaning |
|---|---|
| `+4` | base state |
| `+0x10` / `+0x12` / `+0x14` | pad pressed / held / released |
| `+0x20` | ride+8 |
| `+0x28` | race state |
| `+0x178` | lane slot 0..4 |
| `+0x179` | player car index |
| `+0x1b0` | countdown |
| `+0x168..0x174` | camera eye and target |

**Base state machine `+4` (0x1de858, once per main-loop frame from 0x125190).**

| state | behaviour |
|---|---|
| 0 | instructions. Confirm (mask 0x11) → 2. Back (0x10) → `vt+0x34` pause. Help (0x12) → 1 |
| 2 | → 3, and calls `vt+0x1c` = **0x1cb6b0**: re-grid the 5 karts (reset i), `+0x28 = 0`, slot = 2, countdown = 0x1d |
| 3 | playing. `vt+0x24` = 0x1cb808 runs with live pads |
| 4 / 5 | won / lost. The update still runs with zeroed pads. Confirm → 2 (race again). Back → pause |
| 6 | pause. 0x11 resumes (0x1de800). 0x10 quits: `vt+0x14` → 7, the camera returns |
| 8 | the draw path (0x151268) sees 8 and calls 0x1526d0, which runs the minigame's destructor `vt+0xc(3)` (0x1cb650 → 0x201e00), clears 0x3953cc and sets the game mode `0x14daf8(1)` (READ) |

**Race state `+0x28` (0x1cb808, READ).**
- 0 → 1, with `+0x179 = 4`. **The player drives car 4, the last grid slot (d = len−500, lateral 64).**
- 1: the countdown `+0x1b0` runs from 29 to −1, **30 updates**, then → 2. The karts sit in state 0 until
  `+0x28 == 2`.
- 2: controls (below). When the player's kart `+0x88` is set: rank 1 → win 0x1de1f0, else lose
  0x1de2d8, and `+0x28 = 4`.

**Controls** (held word `+0x12`; bit meaning INFERRED from the pad mapper 0x230278).
- Bits 1/2/4/8 are **up/down/left/right**. They are set by the d-pad (libpad bits
  0x1000/0x4000/0x8000/0x2000), or by the **left stick** past ±0x40 outside a 0x60..0xa0 dead zone,
  which then overrides the d-pad.
- **Up (1):** speed +4 per update. **Down (2):** −4. Neither: −2. Clamped to 0..`+0x49`.
- **Left (4) / Right (8):** slot −1 / +1 (0..4) per update while held, **only if speed ≠ 0**. The
  player kart's lateral target is `slot·56 + 16` = 16, 72, 128, 184, 240.
- Which screen side "left" is depends on the piece frame. Not verified.

**Player car differences** (READ, 0x204240/0x2049d0/0x204088):
- It skips the AI speed update and all timers.
- States 1, 3 and 4 map to 2, and 2 maps to 3, so it sits in "3". AI blockers therefore react to it.
- It still gets collisions and crossing spins (5/6), but does not rotate while in 5.
- `+0x49` is re-rolled `baseMax + rand(6)` on each setState.

**Rubber band.** While the player's car (cars[4]) has rank 1, every AI kart gets +2 speed per tick
(0x201dd8, end of 0x204240).

**Camera** (READ, feeds 0x1dddc0's easing).

| function | race state | value |
|---|---|---|
| distance 0x1cb2b0 | 1 | 500 |
| | 2 | 600 (snapped) |
| | 4 | 0x200 |
| | other | 0x400 |
| yaw 0x1cb358 | 0 | kart yaw + 900 |
| | 2 | kart yaw (lags by ¼ per update while spinning) |
| height 0x1cb438 | 0 | 0x200 |
| | 1 | 600 |
| | 2 | 700 |
| | 4 | 0x400 |

Target = the kart position (0x1cb4e0).

**HUD.** Rank as `"%i"` at (0x26,0xaa) in state 2 (0x1cb9f0), plus the lap line (§6).

**Prize.**
- The win path 0x1de1f0 runs only if `0x153410()` (global 0x2b72a8) is 0 and profile bit `7+4 = 11`
  (`0x16ae90()+0x24`) is not yet set.
- It then sets the bit and calls `0x1c38c0(1)`: counters 0x3975bc (spendable; 0x1c3920 subtracts from
  it) and 0x3975c0 (lifetime) go up by 1, sound 0xc5, message 599, jingle 0xb3.
- Otherwise it shows message 0x2c8 and jingle 0xb1. Losing gives message 0x285 and jingle 0xb2.
- INFERRED: the counters are golden tickets. The 0x1c3290 module that owns them has a 4-world × 3
  structure, but no string names them. **So the first win per save pays one ticket and nothing more.**

## 10. Corrections to track-rides.md
1. Boats have no height bob. The ±10 is lateral wobble around the track centre line, and the rocking
   tilts are never drawn.
2. Kart overtaking is deterministic. The 1-in-11 roll is an aggressive kart *blocking* an overtaker.
3. A guest never joins an existing car: one car per guest (the `0x205638` room test returns 0).
4. The kart roll axis is a stub (0x1951d0), and the kart tilt fields are always 0.

## 11. Still unknown (what I tried)
- **Tick rates.**
  - The minigame update (0x1de858, once per main-loop iteration at 0x125158) versus the car step (ride
    `vt+0x3c` via the sim loop 0x151920..0x15196c → 0x152a18/0x14be60, which runs `s0` iterations per
    scene update).
  - I did not establish the ratio, so the countdown's 30 updates and the ±4 per update are in minigame
    frames, not car ticks.
- **Status names.** 2, 3, 10 and 11 are inferred only. Lifecycle owns this.
- **LAPS source.** The writer of `ride+0xf8`. Lifecycle.
- **Sound bank 6.** The events 4 and 0xf, and whether parameter slot 4 is `JUNGLE_ENGINE_REVS`. I tried
  reading 0x111428. The bank is looked up at run time (0x2abe3c table), so it is not resolvable
  statically.
- **The second kart mesh.** What the ride-instance draw in the kart pose shows (compiled-record words
  `+0xf0..+0xfc`) versus the car's own model `+0x90` (resources 507–510). The resource words need the
  DBA record (geometry/dba).
- **Face buttons.** The physical buttons behind logical masks 0x10/0x11/0x12/0x15. The tables at
  0x363f40/0x363fa8 map them to game bits 0x40–0x800, and the region switch is `0x155428()==8`. The
  d-pad/stick mapping above is solid; the face-button names are not.
- **Guests boarding during race mode.** Could 0x200448 add a 6th car mid-race (not guarded by
  `ride+0x138`)? Not traced.
- **Boat `+0x54`.** Whether the heap happens to be zero there in practice.
