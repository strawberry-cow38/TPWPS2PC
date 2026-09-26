# Track rides: operation (status machine, boarding, laps, wear, guest experience)

Researched 2026-09-26. Compiled ride records come from the EUR `arsdb.dba`, read through
`tools/TPW.PS2.DbaAudit/reference.py`.
Source: `SLES_500.32` (PAL) decompiled in Ghidra 12.1.2 with the ghidra-emotionengine-reloaded
extension. Each load-bearing function was checked against raw R5900 MIPS (`tools/r5900dis.py`), and
tables were read straight from the ELF. For the overview, and for where the four track-ride files
were reconciled, see `track-rides.md`.
**READ** means seen in the decompile and, where marked (MIPS), in the raw instructions.
**INFERRED** means reasoned to. Every address is the executable's.
Compiled ride records from the EUR `arsdb.dba` through the port's `tools/TPW.PS2.DbaAudit/reference.py`.
**READ** means I saw it in the decompile and, where marked (MIPS), in the raw instructions.
**INFERRED** means I reasoned to it. Every address is the executable's.

## 0. Corrections to `track-rides.md` and the brief (read these first)

1. **Vtable entries are 8 bytes, not 4.** Each entry is `{s16 this-delta, s16 0, u32 fn}`, and a
   call `(*(vt+N))(obj + *(s16*)(vt+N-4))` uses the **function half** of the entry at `N-4`. So
   `+0x304` is entry `0x300` (index 96), not slot 193, and `+0xc4` is entry `0xc0` (index 24). The
   track vtable `0x36bbf0` has **110 entries** (`0x000..0x368`), followed by a zero word and a string.
   READ (dump in §9).
2. **`vt+0xc4` is not a validity check.** It is the parent's `0x1e2830`,
   `return (u8)(status-4) < 2`, "is broken down (status 4 or 5)". READ (MIPS).
3. **Status 2/3 are not "invalid track".** 2 = **running**, 3 = **closed**. The rebuild `0x2009c0`
   picks 2 if the waypoint loop is closed (`+0x1c6`) and 3 if not, and only when the ride is not
   broken. READ (§3).
4. **PEEPON never joins an existing car.** Its "room in the last car?" test `0x205638` is
   `jr ra; move v0,zero`. Every boarding creates a new 0x98-byte car, **one guest per car for karts
   and boats alike**. READ (MIPS).
5. **The `+0x36c` override `0x201f78` is the WEAR RATE**, not an excitement formula. Excitement is
   `+0x1d4` = `0x202188`. READ (§6, §7).
6. **The lap target is the ride's Duration setting** (ride `+0xf8`), read through an *inherited*
   getter `0x1183e8`. READ (§5).
7. **None of the script's cadence exists natively:** no 10 s timeout, no 64-boat cap, no
   "runs continuously" water ride. Both classes run one batch at a time and launch only when full.
   READ (§4, §8).
8. The piece-type table `0x2ee1e0` has **at least 52 entries**, not 48: types 48–51 are a third
   4×3 shape, code `0x0e`. READ (dump in §7.2). This matters to the geometry and tool notes.

## 1. Object layout and conventions

`TrackRide` is 0x28d8 bytes. Its parent ("Ride", vtable `0x35a560`) is a subobject at **ride+8**,
whose vptr is ride `+0x18`.
- Parent methods are called with `this = ride+8`, so a **parent offset X is ride offset X+8**.
- Track overrides carry delta −8 and get `this = ride`.

Below, "ride" offsets are from the TrackRide start, with the parent offset in brackets.

| ride off [parent] | type | meaning | written by | read by |
|---|---|---|---|---|
| `+0x20` [`+0x18`] | u32 | riders who finished (Users, INFERRED name) | `0x1e1cd8` (+1, from the guest's after-ride `0x20edd8`) | `0x1e1ce8` |
| `+0x88` [`+0x80`] | ptr | assigned mechanic | `0x1e1df8` | `0x1e1df0`, `0x1541d0` |
| `+0x94` [`+0x8c`] | u32 | status timer, 20.12 (construction animation) | enter handlers (=0), `0x1e4e88` | `0x1e5138` (`+0x74 = timer>>12` unless status 1) |
| `+0x98` [`+0x90`] | u16 | "status just changed" flag, passed to the model animation | enter handlers (=1) | `0x1e5138 → 0x1e5268` |
| `+0x9c` [`+0x94`] | s16 | **Life** (condition) | `0x116048` (= record tier-0 `+0x34`), setter `0x1e1cf0` (vt `+0x2c4`) | getter `0x1e1d58` (vt `+0x2cc`) |
| `+0xa2` [`+0x9a`] | u8 | **status** | only `0x1e4d70` (vt `+0x1f4`) | `0x1e1d70`, many |
| `+0xec` [`+0xe4`] | s32 20.12 | **reliability**, `0x64000` = 100.0 | `0x116120`/`0x116660` (=100.0), `0x117b88` (wear), load `0x116ba8` (= byte<<12) | `0x200358`, parent `0x116d68`, Repair row `0x118228` (>>12) |
| `+0xf0` [`+0xe8`] | s32 | **Speed** setting | setter `0x118378` (vt `+0x30c`) | getter `0x118398` (vt `+0x2f4`) |
| `+0xf4` [`+0xec`] | s32 | **Capacity** setting | setter `0x1183a0` (vt `+0x314`) | getter `0x1183c0` (vt `+0x2fc`) |
| `+0xf8` [`+0xf0`] | s32 | **Duration** setting = lap target | setter `0x1183c8` (vt `+0x31c`) | getter `0x1183e8` (vt `+0x304`) |
| `+0xfc` [`+0xf4`] | ptr | queue head: a guest's link node, guest = node−8 | queue code (port `native-ride-queue.md`) | `0x200448` |
| `+0x110` [`+0x108`] | list | riders list | `0x117c90` | `0x117e08`, `0x118018` |
| `+0x124` [`+0x11c`] | u32 | **service flag** ("needs / has a mechanic job") | `0x118568` (=1), `0x118678` (=0), `0x116048` (=0) | `0x115fa0` (≠0), `0x116020` (==0) |
| `+0x128` [`+0x120`] | s16 | **riders on the ride** | `0x117c90` (+1), `0x117e08` (−1) | `0x200448`, `0x2005c8`, `0x201f78` |
| `+0x12a` [`+0x122`] | s16 | **run timer** (status 2) | `0x200518` (+1); =0 in `0x200448`, `0x1164a8`, `0x116120` | `0x200518` |
| `+0x12c` [`+0x124`] | u16 | build date (calendar clock `0x16ae90→0x16b218`) | `0x116048`, load | Age `0x118470` |
| `+0x12e` [`+0x126`] | u8 | upgrade tier 0..2 | `0x116268` (+1), load | the tier getters (stride 0x34) |
| `+0x138` | ptr | race-mode player object (GoKarts minigame) | `0x1cb1d0` (set), `0x1cb650` (clear) | `0x2022a8`, `0x2023b0`, the kart step |
| `+0x13c` / `+0x13e` | u8 / (s16,s16)[34] | waypoints; at most **34** (`< 0x22` in `0x2016e0`) | `0x2016e0` add, `0x2017d0` remove | rebuild |
| `+0x1c6` | u8 | **loop closed** | `0x2016e0` (1 when the new waypoint equals the station point from `0x2001a8`, else 0), `0x2017d0` (0), `0x1ffda8` (0) | `0x2009c0`, `0x200518`, `0x202848` |
| `+0x1d1` | u8 | track excitement weight Σ | `0x200c20` (after every rebuild) | `0x202188` |
| `+0x26f8` | u8 | piece count | rebuild | wear, weight |
| `+0x280c` / `+0x2810` | u8 / ptr[17] | car count / cars. The bound of 17 is INFERRED from the next field at `+0x2854` and the 17-slot race array. | `0x201ac0`, `0x2022a8`, `0x201c30`, `0x201e00` | |
| `+0x2854` | u16 | track length = pieces × 256 | `0x2009c0` | cars |
| `+0x2888` / `+0x2889` | u8 | lazy piece build done / next index | `0x2009c0` (=0), `0x2027e0` | `0x2027e0` |

Car fields that the lifecycle uses (car = 0x98 bytes, from `0x17a370`). The motion fields are the
covered in `track-ride-cars.md`.

| car off | meaning | writer |
|---|---|---|
| `+0x0c` | rank, 1 = leader (sorted by `+0x54`) | `0x2023b0`; the kart also sets its index at reset |
| `+0x10..+0x33` | guest link nodes (9 slots) | `0x205568` |
| `+0x34` | guests in the car (always 1 in guest mode) | `0x205568` +1, `0x2055d0` −1 |
| `+0x50` | position, 256 per piece | steps |
| `+0x54` | total distance (karts) | kart step |
| `+0x58` | s8 **lap count**, starts at `0xff` (−1) | base init `0x2053a0`, kart reset `0x203f10`, step (+1 per wrap) |
| `+0x7c` | owning ride | base init |
| `+0x88` | **done** flag | 0 at init; 1 in the steps (§5) |
| `+0x89` | index at creation | base init / kart reset |
| `+0x90` / `+0x94` | model object / car vtable | `0x205300` |

Car vtable, same 8-byte format, fn at:
- `+0xc` destructor (arg 3 = delete);
- `+0x14` init(ride, index);
- `+0x1c` set neighbours;
- `+0x24` **step**(prev, next);
- `+0x2c` per-frame pose;
- `+0x34` sound start;
- `+0x3c` cleanup before delete;
- `+0x44` kart reset(index);
- `+0x4c` kart set-state;
- `+0x54` boat rock re-roll.

Boat `0x36bfb8`, kart `0x36c0a0`, base car `0x36c150`. READ.

## 2. Tick and update order

- **Tick** = u32 `0x397644`, getter `0x1c4930`, +1 per simulation pass (`0x1c4a70..7c`, MIPS).
  The chain, once per `0x10eec0` call:
  1. `0x13af68 → 0x1c4aa8 → scene vt+0x24 = 0x151800`, which runs **once**:
     `0x230260(0x360218)` is a stub returning 0, so the 30× branch is dead.
  2. That pass runs `0x152a18` (guests), then `0x14be60` (objects). `0x14be60` calls each object's
     update slot `vt+0x3c` once, and skips all objects while `0x395090 != 0`.
  3. Then `0x13add0 → 0x1c4a58` increments the tick.

  So **one ride update per tick** READ (MIPS). The tick keeps running while `0x395090` holds the
  objects, and wall-clock rate is **not established**, as in the port's `native-shop-flow.md`.
  All times below are in ticks = ride updates.
- **Update `0x200410`** (vt `+0x3c`), in this order, READ (MIPS):
  1. `0x2023b0`: lazy-builds one piece per update (`0x2027e0`). Then, **only if
     `(cars != 0 && status != 10) || ride+0x138 != 0`** (MIPS `0x2023d0..0x2023ec`):
     - applies wear `vt+0x364` (§6.2), unless camera mode `0x395288 == 2`;
     - assigns each car its piece;
     - sorts cars by `+0x54` and sets rank;
     - calls each car's `step(prev, next)`.
  2. `0x200358`: the breakdown check (§6.3).
  3. Parent `0x1169c0`:
     - the status tick `0x1e5138` (§3);
     - the queued-guest update `0x116e20` (each queued guest's `vt+0x3c`);
     - the Life/condemned check (§6.4).
- **Per-frame `0x200728`** (vt `+0x44`): parent `0x116a70` (queued guests' `vt+0x44`, station model
  pose `0x1e52e0`), then each car's `+0x2c`, then each piece's `vt+0x44`. **No state changes.** READ.

## 3. The status machine (ride `+0xa2`)

**Mechanism.** READ (decompile of both switches).
- **Set status `0x1e4d70`** (vt `+0x1f4`) stores the byte, then calls the **enter handler** at vtable
  entry `0x1f8 + 8·s`, for s = 0..11.
- **Status tick `0x1e5138`** calls the **tick handler** at entry `0x250 + 8·s`, for s = 1..11.
  Status 0 has no tick. After the call, `+0x74 = timer>>12` unless status is 1.
- Enter handlers run **every time** set-status is called, even if the status is unchanged.

Track-ride handler assignments (entry → function; "parent" means inherited from `0x35a560`):

| s | name | enter (entry `0x1f8+8s`) | tick (entry `0x250+8s`) |
|---|---|---|---|
| 0 | initial | parent `0x1e4c98`: timer=0, flag90=1 | — |
| 1 | construction | parent `0x1e4ca8`: `+0x74`=0, flag90=0, timer=0 | parent `0x1e4f58`: construction animation, then **→10** when `0x1e4e88` completes |
| 2 | **running** | parent `0x1164a8`: flag90=1, timer=0, **run timer `+0x12a`=0** | **track `0x200518`** |
| 3 | **closed** | parent `0x1e4cc8`: flag90=1 | parent `0x1e50f0`: empty |
| 4 | **broken down** | **track `0x2002d8`**: flag90=1, **empties the queue** `0x117798(ride+8,0)` (event 7 to every queued guest) | **track `0x2006f8`** → unload pass `vt+0x2ac` = `0x2005c8` |
| 5 | broken, reliability 0 | parent `0x116550`: flag90=1, sound `0x1073c0(snd,1,1)`, effect **0x70** at the vt `+0x6c` position | **track `0x200698`** → unload pass |
| 6 | **being repaired** | **track `0x200310`**: **removes every car now** (`0x2022a8` until none), flag90=1 | **track `0x2006c8`** → unload pass |
| 7 | repaired | parent `0x116660`: set 2 (via `0x1e4d08`), reliability=`0x64000`, **set 10** | parent `0x1e5110`: empty |
| 8, 9 | (unused here) | parent: flag90=1 | parent: empty / record lock-unlock |
| 10 | **loading** | parent `0x1e4d58`: flag90=1, timer=0 | **track `0x200448`** |
| 11 | **unloading** | parent `0x1e4d68`: empty | **track `0x2005c8`** |

The status names for 1, 3 and 10/11 match the port's `native-selection-eligibility.md`. Guests may
pick the ride only in **2, 10, 11** (`0x1e1e48`, port).

### Tick behaviour and exits (READ, MIPS for 0x200448, 0x200518 and 0x2005c8)

| status | what the update does | exit → next |
|---|---|---|
| 10 loading | Only on ticks with `tick % 20 == 0` (unsigned, `0x200464..70`): if `riders < Capacity`, take the queue head `ride+0xfc`. If that guest's `vt+0xdc` returns **0x12** (standing at the front), **PEEPON** it (§4). Cars are **not** stepped (the `0x2023b0` guard), so there is no wear. | `riders ≥ Capacity` → `+0x12a=0`, **→2**. **No timeout.** |
| 2 running | Every update: if `+0x1c6 == 0` → **3**. Else `+0x12a += 1`. Cars step and wear. | `(s16)+0x12a ≥ 2·Duration` → **11** (`sll v0,v0,1; slt` at `0x200588`) |
| 11 unloading | Only on ticks with `tick % 10 == 0`: if `riders != 0`, unload every car whose done flag `+0x88` is set (`0x2022a8(ride,i)`). Removal compacts the array and `i` still advances, so the car shifted into slot i waits one more pass. Cars keep stepping and wearing. | `riders == 0` and status still 11 → **10** |
| 3 closed | Nothing; cars (if any) keep stepping and wearing; **no unloading, no boarding**. | player Open (vt `+0xfc` → `0x1e2738` → 2); rebuild with the loop closed (→2); breakdown (→4) |
| 4 broken | The unload pass as in 11 (every 10 ticks, done cars only). Cars keep moving and wearing. | `0x200358`: reliability == 0 → **5**; mechanic arrives → **6**; mechanic done → **7** |
| 5 broken, 0 | the unload pass | mechanic → 6 / 7 |
| 6 repairing | the unload pass (nothing is left after the enter handler) | mechanic done → **7** (→10). ⚠ If 0 < reliability < 10.0, `0x200358` sets **4** again on the next update (§6.3); the repair still completes. |

Other producers (the set-status scan over the whole ELF, `lw …,0x1f4` with a constant `$a1`):
- **1**: `0x1e1238` (placement).
- **2**: rebuild `0x200bcc`, parent Open `0x1e2738`, `0x1e4d08` (inside 7).
- **3**: rebuild `0x200bec`, `0x20054c`, parent Close `0x1e2768`.
- **4**: `0x2003b8`, parent `0x116a54` (Life), load `0x116d40`.
- **5**: `0x2003ec`.
- **6**: mechanic `0x178634`/`0x178a74`.
- **7**: mechanic `0x17875c`, park teardown `0x154244`.
- **10**: `0x200670`, `0x11668c`.
- **11**: `0x2005a4`.

No constant 8 or 9 site exists. READ.

### Normal cycle

```
place (0x1fff80): set 1 → rebuild 0x2009c0 → 3 (loop open)   [status 1 is overwritten in the same call]
player closes the loop, rebuild → 2 → 2·Duration updates → 11 → riders==0 → 10
10: one guest / 20 ticks until riders == Capacity → 2 (cars start next update)
2: 2·Duration updates → 11
11: cars finish Duration laps → unloaded ≤ 10 ticks later → riders==0 → 10 …
```

READ, except the line "status 1 is overwritten". That follows from the order
`0x1fff90 jal 0x1186d8` (→ `0x1e1238`, set 1) then `0x1fffcc jal 0x2009c0` (→ 2/3), MIPS, so the
construction tick never runs for a track ride (INFERRED consequence).

**Rebuilds dump everyone.** `0x2009c0` starts with `while (0x2022a8(ride,0));`, so every rebuild
unloads all riders at the exit with full after-ride scoring. It then re-evaluates 2/3, which also
**reopens** a ride the player had closed, if the loop is closed. The rebuild is called from:
- placement `0x1fff80`;
- load `0x2008d0`;
- the tool (`0x2018c8`, …).

READ.

## 4. Boarding: native PEEPON `0x201ac0`

**Caller:** exactly one, `0x2004d8` in the status-10 tick. READ (whole-ELF `jal` scan; no data refs).

**Steps.** READ (decompile and MIPS `0x201ac0..0x201c28`):
1. `0x117c90(ride+8, node)`:
   - unlink the guest from the queue;
   - guest state **0x15**; guest `vt+0x3c`, `vt+0x44`;
   - link into the riders list, `riders += 1`;
   - the port's note adds event 0x13 to the rest of the queue (move-up ripple).
2. **Test** `0x205638(last car)`. It always returns 0 (MIPS), so the join branch at `0x201b10`
   (`bnel`) is never taken.
3. **New car**:
   - `0x17a370(0x98)`, then `0x205300` (vtable `0x36c150`, model `0x230a98`);
   - class from `0x2ee528[world·2 + park]` (world `0x14e170`, park `0x14e160`):
     0 → **boat** `0x36bfb8`, else → **kart** `0x36c0a0`;
   - store into `cars[count]`.
4. `car->vt+0x14(car, ride, a2 = count before increment)` (MIPS `0x201bb4`), then `count += 1`.
5. `0x205568(car, node, ride+8)`: attach the guest to seat `car+0x34` (`0x17d3a0` on the model), `+0x34 += 1`.
6. Return 1.

**Spawn positions** (index i = the car's slot at creation). READ:
- **boat** `0x203360`:
  - base init, then pos = `length − (i+1)·200`;
  - rocking speeds 0, lane `+0x5a = 0x7f`;
  - initial speed target `+0x49 = Speed/20`;
  - the boat step later replaces the target with `60 − 2·rand(11)`, so Speed matters for the
    first ~2 ticks only.
- **kart** `0x203ec8 → 0x203f10`:
  - lap = −1, pos = `length − (i+1)·100`, speed 0;
  - lane `+0x5a = 0x40` (even i) or `0xc0` (odd i), rank = i, distance `+0x54` = pos;
  - random max speed `12 + 2·rand(6)`, accel `5 + rand(11)`;
  - kart state **0** with countdown 1.
- The base init `0x2053a0` alone would use `−(i+1)·0x40`; both classes override it.

**Answers to the task questions:**

- **How often:** one attempt per **20 ticks**, one guest per attempt, and only if the head guest is
  in state 0x12. A head that is still walking up (0x13/0x29) costs the slot. READ.
- **How many per car:** **one**, for both classes. READ.
- **Capacity:** the **Capacity setting** (ride `+0xf4`).
  - Default `max(1, record_cap/2)` = **4** (`0x116120`).
  - Slider range 1 .. max over tiers of record `+0x30` (`0x1d4c80`), which is **8** for all eight
    track rides at every tier (§10).
  - The `.sam` 4/6/8 (karts) and 2/3/4 per boat (water) are **not** in the compiled record, and
    upgrading does not raise capacity. `0x116268` bumps the tier, **re-runs `0x116120`**
    (Capacity back to 4, Speed 50, Duration 5, reliability 100.0) and debits `record[tier]+0x50 × 10`.

  READ.
- **Launch:** when `riders ≥ Capacity`, checked on the 20-tick cadence. There is **no timeout**:
  the only way out of 10 in the track overrides is "full". The other exits are external: breakdown
  (4), mechanic (6/7), player close (3), rebuild (2/3). READ. A park with too few visitors
  therefore keeps seated riders waiting indefinitely (INFERRED consequence).
- **All cars launch together.** They sit at their spawn slots while status is 10 (not stepped). On
  the update after →2 they all step. A kart leaves state 0 on its first step: countdown 1 → 0 →
  `set_state(2)` (MIPS `0x2043c8..0x204404`). READ.
- **The kart grid in race mode:** state-0 karts wait until `player+0x28 == 2` (`0x2043f0`). READ.
- **64 boats:** **no such cap** natively. Guest mode holds ≤ Capacity ≤ 8 cars. The car array has
  17 slots (INFERRED bound). I searched the track module for comparisons with 64/65; the only
  hits, `slti …,0x41` at `0x1fedec..0x1fee24` in `0x1fea58`, compare terrain heights (`0x14e138`
  cell reads), not a car count. READ.
- **Where guests come from:** the parent's queue list at ride `+0xfc` (parent `+0xf4`), filled by
  the guest-side queue code.
  - Head count `7 + 4·tier`, physical spots at 4 per cell, the impatience clock: see the port's
    `findings/native-ride-queue.md` (tpwps2-queues). It is consistent with everything here: its
    `ride+F4/+108/+120` are the parent offsets.
  - The parent's `0x116e20` updates the queued guests each tick; `0x116e70` animates them each frame.
  - Breakdown (status 4 enter) empties the queue with event 7.
  - **The track ride differs from the ordinary ride here.** The ordinary ride (`0x1b8c28`) keeps
    its queue while Life > 0; the track ride always empties it. READ.

## 5. Laps, finishing and unloading

- **Lap target** = `vt+0x304` → entry `0x300` → **`0x1183e8`** (inherited): `lw v0, 0xf0(a0)` =
  parent `+0xf0` = **the Duration setting**. READ (MIPS). Its sources:
  - default `0x116120`: `max(1, MaxDuration/2)` = **5** (record MaxDuration 10, MinDuration 1, all
    eight rides, all tiers);
  - the ride panel's third slider (`0x1d4fd0 → vt+0x31c`), range min..max over tiers of record
    `+0x40/+0x44` = **1..10**;
  - save/load, byte at save `+0x8d` (`0x116ba8`).

  The setter also pushes the value to script var 3 = VAR_DURATION (`0x1183c8 → 0x118310 →
  0x1fa858`), which is dead for track rides because the script is a stub. The cars read the
  getter live, so changing Duration mid-run changes the target. READ.
  The `.sam` `Info.DurationUnit` has no consumer that I found here (not searched for globally).
- **Counting** (READ):
  - `car+0x58` (s8) starts at −1 and increments at each **wrap** (`pos ≥ length`, then
    `pos %= length`).
  - **Boat** `0x203540`: at a wrap, if ride status **== 2** nothing happens. Otherwise, if
    `lap ≥ Duration`, set done `+0x88 = 1`.
  - **Kart** `0x204240` (racing states 1–4): at a wrap, if `lap ≥ Duration`, set done `+0x88 = 1`
    and `+0x54 += (5 − rank)·100 − pos`, a finishing-order bonus. There is no status test.
  - So a car drives from its spawn point to the line, plus **Duration full laps**.
- **After done** (READ):
  - A kart skips its normal step: `pos += min(maxSpeed, ((5−rank)·100 − pos + 1)/2)`, and the lane
    eases toward 11 by a quarter of the gap per step. So it parks `(5−rank)·100` units past the
    line. For rank > 5 the target is behind the line and the formula moves it backwards; this is
    what the code does, not verified in play.
  - A boat keeps sailing (its step ignores `+0x88`) until the unload pass removes it.
- **Unloading happens only in statuses 4, 5, 6, 11** (the four tick handlers that call `0x2005c8`),
  every 10 ticks, done cars only. Status 6's enter handler removes all cars regardless. READ.
- **`0x2022a8(ride, i)` removes one car** (READ):
  1. It refuses when `ride+0x138 != 0` (race mode).
  2. It pops each guest (`0x2055d0`) into `0x117e08`, which:
     - unlinks the guest from the riders list;
     - places it on the exit cell (connection B, `0x1e1a48` + origin);
     - sets guest state **0x16**;
     - calls `0x1fb230` (empty) and `0x1fb3b8(g,0)` (clears the on-ride bit 0x100);
     - `riders −= 1`.
  3. It calls the car's `vt+0x3c` (cleanup) and `vt+0xc(3)` (destroy and free).
  4. It compacts the array, `count −= 1`, and returns `count != 0`.
- **Callers of `0x2022a8`** (whole-ELF scan; READ):
  - `0x2005c8` (the unload pass);
  - `0x200310` (status 6 enter);
  - `0x2009c0` (rebuild);
  - `0x1fffe8` (vt `+0x10c`, removal/demolition: also clears the pieces `0x201640` and tears
    down the queue `0x116458`);
  - `0x200038` (park teardown, called per track ride from `0x150e80`).
- **Guest afterwards:** state 0x16 → jump table `0x36cb90[0x16]` → `0x211854` → **`0x20edd8`**
  (after-ride scoring, §7.4), which sets state **0x17** (walk out). This is the "producer of
  state 0x17" the port's queue note lists as open. State 0x15 (riding) → `0x20e0e8` only sets
  flags and the walk animation, so there are **no per-tick rider effects**. READ.

## 6. Wear, breakdown and repair

### 6.1 Wear rate `0x201f78` (vt `+0x36c`, arg p)

Integer math, `/` truncates toward zero, `>>` is arithmetic. READ (MIPS `0x201f78..0x202180`).

```
if (u32[0x2b72a8] != 0) return 0;                 // global switch set by 0x153420; meaning not traced
msd = MinSpeedDamage   (tier +0x24, vt+0x34c)      // 4 on every track ride
mcd = MinCapacityDamage(tier +0x28, vt+0x354)      // 4
wr  = WearRate         (tier +0x2c, vt+0x35c)      // 5 / 3 / 2 for tiers 0/1/2
s   = (Speed << 12) / 100
t   = ((0x1000 - msd) * s) >> 12
speedTerm = Speed < 100 ? t + msd : (msd + t + 0x1000) / 2
frac = ((p ? Capacity : riders) << 12) / MaxCapacity(tier +0x30, vt+0x344 = 8)
capTerm  = (((0x1000 - mcd) * frac) >> 12) + mcd
if (frac >= 0xccc) { k = (frac - 0xccc) >> 6;  capTerm += k*k; }     // knee above 80% full
lenTerm  = (pieces << 12) / 30                     // pieces = u8 ride+0x26f8
rate = ((lenTerm + speedTerm + capTerm) / 3) * wr
```

This **scales with upgrades** (WearRate 5→3→2) **and with track length** (+1/30 of a unit per
piece), and with Speed and load.

### 6.2 Applying wear: parent `0x117b88` (vt `+0x364`)

READ (MIPS `0x117b88..`). It is called only from `0x2023b0`, under the car-step guard and
`camera mode 0x395288 != 2`.

```
if ((tick & 3) != 0) return;
old = rel;  rel = old - (rate(0) >> 5);
Life -= old/0xf000 - rel/0xf000;                  // one Life point per 15.0 of reliability lost (signed /)
if (Life < 0) Life = 0;  if (rel < 0) rel = 0;
```

- The Life setter `0x1e1cf0` queues advisor message 135 (`STR_ADVMES_RIDE_CONDEMNED`, name from the
  port's `dba.md`) on crossing to ≤ 0.
- Wear therefore accrues **only while cars exist and status ≠ 10**, i.e. in 2, 11, 3, 4, 5.
- It is **not** per lap or per ride: it is per 4 ticks of occupied running.
- Camera mode 2 is "not dispatched" in the port's `camera.md`. What sets it is not traced (possibly
  the drive-it-yourself mode, INFERRED).

### 6.3 Breakdown check `0x200358`

Every update, **any status**. READ (MIPS `0x20036c..0x2003f4`).

```
if (status == 4 || status == 5)  0x118568(ride+8, 1);   // service flag = 1 (+ pokes the stub script's var 4)
if (rel == 0) { if (status == 4) set 5; }
else if (rel <= 0x9fff) set 4;                            // 10.0 in 20.12; re-entered every update
```

- **This differs from the parent's `0x116d68`**, which the ordinary ride uses via `0x1b8038`: that
  one breaks down only `if (status == 2 && rel < 0xa000)`. The track override drops the status
  test.
- So once reliability is below 10.0, the track ride is forced to 4 **every update**, from any
  state including 6. Status 4's enter handler re-empties the queue each time.
- It becomes 5 only if wear drives reliability to exactly 0, which needs cars still aboard.
- The ride does not break at a random moment: **the threshold is deterministic**.

### 6.4 Life / condemned: parent update `0x1169c0`

`if (Life == 0 && flag == 0)`:
- `0x118568(ride+8, 4)`: flag=1 and effect 0x18;
- `0x153d70`: add the ride to the list `0x3953e8` (count `0x2b739c`);
- `if (!broken) set 4`.

READ. Repair restores reliability but not Life, so a Life-0 ride re-breaks on the update after
each repair (INFERRED from the paths). Life starts at record tier-0 `+0x34`: **100**, or **80** for
HALLOW karts and both SPACE rides. It loses 6 per full wear-down from 100.0 to below 10.0
(INFERRED arithmetic: floor(100/15) = 6).

### 6.5 What status 4 does to the cars

**Nothing directly.** No car step tests status 4. The boat tests only status 2, for finishing and
its tilt clamp (±25 in 2, ±150 otherwise); the kart tests nothing. READ.
- Cars **keep moving at normal speed** and are unloaded as they finish their laps.
- Riders are dumped all at once only when a mechanic starts work (status 6 enter). READ.
- The parent's status-4 enter `0x1164d0` plays `0x1073c0(snd,0,1)` and `0x111150(…,0xc,0xe2,0)`.
  The track override **does not call it**, so a track ride breaking down makes neither call. READ.
- The script's smoke on nodes 3/4 has no native equivalent in the track code. Status 5 inherits
  the parent's effect 0x70.

### 6.6 Repair by a mechanic

Mechanic methods; the ride pointer is at mechanic `+0x28`. READ.
- **`0x1785f8`:**
  - flag clear → route to the ride (`0x1781b8`, cell `vt+0x6c`) and set the flag
    (`0x118568(ride,1)`);
  - flag set → mechanic state 0xe, **ride → 6**, finish time `tick + T[mech+0x50 & 7]`, with
    `T = u16 table 0x3627c8, stride 4 = {240, 180, 120, 60, 60}` ticks for levels 0..4.
- **`0x178a38`:** the same with `0x118568(ride,2)` and mechanic state 0x36.
- **`0x1786d0`** (the job ends):
  - flag set → **ride → 7** (= reliability 100.0, then 10), clear the assigned mechanic
    `ride+0x88`, clear the flag (`0x118678`);
  - otherwise → mechanic state 0x3a, `mech+0x54 = min(100, +10)`.
- Because `0x1785f8` also takes unflagged rides, a mechanic who services a **working** track ride
  puts it in 6, which **ejects every rider at once** (INFERRED from the paths).
- Which mechanic is sent, and when, is staff AI and not traced.

## 7. Guest experience and the ride panel

### 7.1 Excitement `0x202188` (vt `+0x1d4`)

READ (MIPS `0x202188..0x2022a0`).

```
base = record +0x18 (base excitement: 80 JUNGLE karts/water, 80 HALLOW karts, 75 HALLOW water,
                     75 FANTASY both, 80 SPACE karts, 75 SPACE water)
if (base == 0) return 0;
e  = base + (u8[ride+0x1d1] >> 1)
sf = (Speed << 12) / 100;   df = (Duration << 12) / 5;
sf = clamp(sf, 0xc00, 0x1400);  df = clamp(df, 0xc00, 0x1400)     // 0.75 .. 1.25
return min(100, (e * ((sf * df) >> 12)) >> 12)
```

- The ordinary ride's `0x1b82d0` is identical **without** the `+0x1d1` term. **The track term is
  the only way the drawn track affects guests.**
- Speed below 75 always gives factor 0.75, and Speed tops out at 100 (1.0).
- Duration 1–3 gives 0.75, 4 gives 0.8, 5 gives 1.0, 6 gives 1.2, 7+ gives 1.25.

### 7.2 Track weight `+0x1d1`: `0x200c20`, after every rebuild

`Σ over pieces of w(type)`, type = `s8 piece+0xec` (`0x1fe9b0`). READ.

| types | shape code (`0x2ee1e0` byte 1) | w |
|---|---|---|
| 20–21, 22–23 | 4, 99 (`0x63`) | **4** |
| 40–43, 44–47, 48–51 | 12, 13, 14 (the 4×3 pieces) | **4** |
| 24–27, 28–31 | 3, 9 | **2** |
| 36–39 | 11 | **2** |
| 0–3 (station), 4–19, 32–35 | 15, 0, 99, 1, 2, 10 | 1 |

Which mesh (straight, curve, hill `h_u/h_d`, crossing `x`, `v`, jump, tunnel) each shape code draws
is in `track-ride-geometry.md` §6. The table rows (`size, shape, w, h, d, rot, ?, 0x80`)
are in `0x2ee1e0`, at least 52 × 8 bytes.

### 7.3 The ride info panel: `0x1d4c80` builds it, `0x1d5210` draws it, `0x1d4fd0` applies it

Labels come from the EUR `eng.dat` rows. READ.

| row (string id) | value |
|---|---|
| Excitement (`0x3d`) | vt `+0x1d4` → `0x202188` |
| Reliability (`0x424`) | vt `+0x2ec` → **`0x1183f0` = `100 − min(100, (rate(1) · Duration · 9) >> 15)`**, a *prediction* from the settings, not the current state |
| Repair (`0x284`) | `0x118228` = reliability >> 12, the current value 0..100 |
| Life (`0x431`) | `0x118238` = Life |
| Speed / Capacity / Duration sliders (`0x1b4`, `0x397`, `0x301`) | setters vt `+0x30c`, `+0x314`, `+0x31c`. The ranges are min..max over tiers of record Min/MaxSpeed (1..100), 1..max capacity (8), Min/MaxDuration (1..10). Capacity and Duration apply only when kind ≠ 1 and max cap ≥ 2. |
| Upgrades (`0x77`), Addons (`0x1c6`), Age (`0x1ed`, `0x118470`/365), Users (`0x19f`) | not needed for the lifecycle |

### 7.4 After-ride scoring: guest `0x20edd8`

Runs for ride kinds 1/3/6/7, so the track ride is scored like any other ride. READ, constants from
`0x2eeb30..44`. Guest field names are from the port's `visitors.md`.
- `E` = the ride's Excitement (vt `+0x1d4`).
- `P` = the guest's taste `u16[0x2eebd8 + 8·u8[g+0x7d]]` = {90, 30, 50, 75, 100, 45, 60, 70} for
  types 0..7.
- `d = |P − E|`: **happiness `g+0x75` += 15** if d ≤ 20, **+10** if 21..50, **+5** if ≥ 51, capped
  at 100.
- if `E > 55`: **sick `g+0x76` += (1212·(E−30)) >> 12**, capped at 100. This is the only nausea
  path, and it comes via E, not the track shape.
- **`g+0x78` −= E** (4096·E >> 12), clamped at 0.
- The ride's Users `+0x20` += 1 (`0x1e1cd8`).
- The guest walks out: state 0x17, `g+0x7b −= rand(20)`, deadlines `+0x6c = now+60+rand(60)` and
  `+0x2c = now+300+rand(300)`.

The same E also feeds:
- destination choice `0x20c138`: `F = 2·(50 − min(|P−E|, 50))`, see the port's
  `native-destination-score.md`;
- queue patience `0x210428`: roll `rand(100 − max(50,|P−E|)/2) < 2`, see the port's
  `native-ride-queue.md`.

Other readers of E, among the 12 `vt+0x1d4` call sites (READ, only where they touch E):
- **`0x16b7b8`** is a park-level sum. For each ride (kind 1/3/6/7) it adds
  `(20 + rand(10) + E·tier + 20) / 2`. **E counts for nothing here until the ride is upgraded**
  (tier 0). The result's consumer was not traced; the port's `bus-native-demand.md` may cover it.
- **`0x210b38`** divides the sum of E over attractions by `10000 + rand(5001)` (in 20.12) and
  compares it against thresholds at `0x2eeb84/88/8c`: a park-level rating.
- `0x109d40`, `0x10b288` and `0x1d8288` are attraction-list UIs (E, Repair, Life).
- `0x128888` belongs to a different class.

**No per-lap, per-hill or per-crossing experience exists at ride time.** The track acts only
through `+0x1d1` in E, and through piece count in wear. READ (`+0x1d1` has no other reader in the
module).

### 7.5 Worked values

These are **computed from the formulas as transcribed above**, not captured from the game. Dino
Karts tier 0: msd=mcd=4, cap 8, base E 80.

| pieces, Σw | Speed, Cap, riders, Dur, WR | rate | rel lost / 4 ticks | occupied ticks to < 10.0 | panel Reliability | Excitement |
|---|---|---|---|---|---|---|
| 20, 30 | 50, 4, 4, 5, 5 | 11380 | 355 | ~4156 | 85 | 71 |
| 20, 30 | 100, 8, 8, 10, 5 | 18440 | 576 | ~2564 | 50 | 100 |
| 20, 30 | 50, 4, 4, 5, 2 | 4552 | 142 | ~10388 | 94 | 71 |
| 12, 12 | 50, 4, 4, 5, 5 | 9560 | 298 | ~4952 | 87 | 64 |
| 35, 80 | 50, 4, 4, 5, 5 | 14795 | 462 | ~3192 | 80 | 90 |

## 8. Native against the ride scripts

| `GoKarts.rss` / `Wateride.rss` intent | native (track ride, both classes) | verdict |
|---|---|---|
| wait for `BUMP_ISTRACKVALID` | status 3 until the loop is closed (`+0x1c6`), then 2 | matches in effect |
| board one guest at a time, `PEEPON` + `LAUNCHCAR` per guest (karts) | one PEEPON per 20 ticks, one car per guest; cars wait unstepped in 10 | matches (one kart per guest) |
| water: fill a boat to `VAR_CAPACITY` (2/3/4), launch each boat on its own | **one guest per boat, and all boats launch together** as one batch | **departs** |
| 10 s timeout, reset per boarding; start when full *or* timed out | start **only** when full; no timer | **departs** |
| `WAIT 2000`, then `SETLAPS VAR_DURATION`, `STARTRACE` | launch on the next update; each car reads Duration live | laps match; no 2 s delay |
| running | status 2 for only 2·Duration updates, then 11 while the cars finish their laps | **departs** (the run shows as "unloading") |
| unload one guest per `PEEPOFF` when the exit slot is clear | every 10 ticks, whole cars that finished, straight to the exit cell | partial |
| track invalid → `HALTRIDE`, everyone off now | a rebuild removes all cars first, then 3 | matches |
| ride closed → `HALTRIDE` / `CLOSERIDE` | status 3: **nobody gets off**, cars keep driving, no unload until reopened | **departs** |
| breakdown: `SETBROKEN`, smoke nodes 3/4, repair effect, `WAIT 3000` | 4 below reliability 10.0, 5 at 0 (parent effect 0x70), 6 during repair (everyone off), 7 → 10 | partial |
| water "runs continuously", max 64 boats (`BUMP_CARSONRIDE`) | batch-based like the karts; ≤ Capacity ≤ 8 cars | **departs** |
| `BUMP_WATERCLOSED 0/1` (water flow, river sound) | no such state in the ride machine | not found natively (not searched outside the ride code) |

## 9. Overrides: the full diff against the parent and the ordinary ride

All three vtables have 110 entries. "fn" is at the entry offset + 4. Rows are listed only where
they differ; every other entry is identical in all three. READ (ELF dump).

| entry | parent `0x35a560` | track `0x36bbf0` | ordinary `0x366330` | what (track) |
|---|---|---|---|---|
| `0x008` | `0x118928` | **`0x203130`** | `0x1b8e58` | destructor, below |
| `0x020` | `0x109538` ret 0 | **`0x203220`** ret 0 | `0x109538` | no-op; see below |
| `0x038` | `0x1169c0` | **`0x200410`** | `0x1b7f80` | update |
| `0x040` | `0x116a70` | **`0x200728`** | `0x1b7db0` | per-frame |
| `0x108` | `0x116458` | **`0x1fffe8`** | `0x1b7d90` | remove/demolish: unload all, clear pieces, then the parent |
| `0x118` | `0x1e5a90` ret 0 | **`0x203230`** = length − 2 | `0x1e5a90` | see below |
| `0x138` | `0x109880` | `0x109880` | `0x1b83d8` | (ordinary only) |
| `0x150` | `0x295160` | **`0x1ffda8`** | `0x1b7a98` | init from record: model, reset `0x116048`, zero track and cars |
| `0x160` | `0x1186d8` | **`0x1fff80`** | `0x1b7c38` | place: parent, save transform to `+0x2858..`, rebuild |
| `0x1d0` | `0x1e5a98` | **`0x202188`** | `0x1b82d0` | Excitement |
| `0x218` | `0x1164d0` | **`0x2002d8`** | `0x1b8c28` | enter 4 |
| `0x220` | `0x116550` | `0x116550` | `0x1b8bf0` | enter 5 (ordinary only) |
| `0x228` | `0x1e4cf8` | **`0x200310`** | `0x1e4cf8` | enter 6 |
| `0x260` | `0x116970` | **`0x200518`** | `0x116970` | tick 2 |
| `0x270` / `0x278` / `0x280` | `0x116910` / `0x1e5100` / `0x1e5108` | **`0x2006f8` / `0x200698` / `0x2006c8`** | parent | ticks 4 / 5 / 6 → the unload pass |
| `0x2a0` | `0x116940` | **`0x200448`** | `0x1b8050` | tick 10 |
| `0x2a8` | `0x116990` | **`0x2005c8`** | `0x116990` | tick 11 |
| `0x2e0` | `0x118a70` | `0x118a70` | `0x1b8a98` | (ordinary only) |
| `0x368` | `0x118ab0` ret 0 | **`0x201f78`** | `0x1b80e0` | wear rate |

**What the track ride skips.** The parent's s2/s4/s10/s11 ticks all run `0x1166a8`, the
script-driven boarding and unloading. That routine:
- reads VAR_ONRIDE(5) against VAR_CAPACITY(2) via `0x1fa528`;
- offers LETMEON via `0x1fa368`;
- tests RUNNING(9) && ONRIDE via `0x1fa608`;
- takes LETMEOFF(1) via `0x1fa418`.

The track ride replaces all of it, which is exactly why the stubbed `BUMP` handler does not matter
to it. READ.

- **`0x203220` (entry `0x20`).** Byte-identical to the default `0x109538` (`jr ra; move v0,zero`),
  and all 23 object vtables in this family use `0x109538` there. It changes nothing.
  - 26 call sites use the slot. At `0x1653d8..0x165414`, when the selection changes, it is called
    on the *previous* object and `+0x1c` on the new one, with results ignored. That suggests a
    "deselect" hook (INFERRED).
  - The question it answers is **not resolved**.
- **`0x203230` (entry `0x118`).** Returns `u16 +0x2854 − 2`, never zero for a track ride; the
  parent returns 0. Its only placed-object consumer found is `0x15d5a0`, a case of a switch at
  `0x15d9dc`, in code near the Litter class (`0x361038`, class membership not verified). It picks
  record `0x360f50` `{12, 0x124088}` when the value is non-zero and `0x360f48` `{0, 0x124088}`
  otherwise, and passes it to its object's `vt+0x6c`.
  - So track rides always get the "12" variant.
  - **Meaning not resolved.**
- **Destructor `0x203130`** (READ):
  1. vptr = `0x36bbf0`; the embedded piece-like object at `+0x2700` (vptr `+0x2710 = 0x36b670`) is
     destroyed via `0x1e0ed0(…,2)`.
  2. The 35 pieces are destroyed from `+0x25f0` down to `+0x1d8` (piece `vt+0xc(0)`).
  3. vptr = parent `0x35a560`; `0x109340` runs on `+0x110` (riders list) and `+0xfc` (queue list);
     then the base destructor `0x1e0ed0(ride+8, 0)`.
  4. It frees the object if `flags & 1`.
  - **It does not delete cars.** Cars are removed earlier by `0x1fffe8`, `0x200038` or `0x2009c0`.

## 10. Compiled records (EUR `arsdb.dba`) for the eight track rides

Per-tier fields: `flags, dmgSpeed, dmgCap, wear, cap, condition(Life), speedMin, speedMax,
durMin, durMax, group, research, cost`.

| key | ride | base E | tier 0 / 1 / 2 (only the fields that differ between rides) |
|---|---|---|---|
| 220 | JUNGLE karts (Dino Karts) | 80 | wear 5/3/2, cap 8, Life 100, speed 1–100, dur 1–10 |
| 235 | JUNGLE water (Splish Splash) | 80 | same |
| 138 | HALLOW karts | 80 | Life **80** |
| 146 | HALLOW water | 75 | Life 100 |
| 59 | FANTASY karts | 75 | Life 100 |
| 62 | FANTASY water | 75 | Life 100 |
| 367 | SPACE karts | 80 | flags 4, Life **80** |
| 381 | SPACE water | 75 | flags 4, Life **80** |

- dmgSpeed = dmgCap = 4 everywhere.
- Minigame selector: 7 on all four kart rides, 0 on the water rides.
- Kind-8 track upgrades (e.g. LAVAJUMP 236) carry `{cost, research, group}` only; no lifecycle
  fields.

## 11. Still unknown (and what I tried)

- **Wall-clock rate of a tick.** I traced the tick to one increment per `0x10eec0` pass, but not
  the pass rate. The port has not established it either.
- **What sets camera mode 2** (it gates wear), and **what `0x2b72a8`** (the zero-wear switch, set
  via `0x153420` from `0x13b7b8`, `0x16e110`, `0x1c4070`) means. Neither was followed further.
- **Mechanic dispatch** (which mechanic goes to which ride, and when). Only the arrival and
  completion handlers were read.
- **Slot `+0x24` (`0x203220`) and slot `+0x11c` (`0x203230`) semantics.** Consumers were located
  (§9) but not resolved.
- **`0x153d70` / list `0x3953e8`.** Condemned rides are added to it. `0x1541d0` walks it only at
  park teardown (`0x150e80`), setting status 7 and calling `0x116268(ride,1)` when the assigned
  mechanic is in state 0x10/0x34/0x36. The purpose is only partly read.
- **The `.sam` `Info.DurationUnit` and `RunsContinuously`.** No consumer was found in the ride code.
  They are not in the compiled record layout per the port's `dba.md`. I did not search globally.
- **Shape code → mesh** for the excitement weights (see `track-ride-geometry.md` §6).
- **Kart parking for rank > 5** (§5): read, not checked for in-game effect.
- **Boat sort key.** `0x2023b0` sorts by `car+0x54`, which the boat code never writes. Whether it is
  zero after `0x17a370` is not checked (see `track-ride-cars.md` §4).
