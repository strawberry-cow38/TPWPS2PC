# Roller coasters (PS2): operation, statistics, value, upgrades and the Rollercoaster Test Park

Researched 2026-09-26 for strawberry (the "how" pass on roller coasters). Source: `SLES_500.32` (PAL)
decompiled in Ghidra 12.1.2 with the ghidra-emotionengine-reloaded extension. Every load-bearing constant
and branch was checked in raw R5900 MIPS (`tools/r5900dis.py`). Tables were read from the ELF, and disc data
through the port's own `TPW.PS2.Data` readers; the scratch tools stay local and only numbers are recorded here.
Overview and cross-file notes: `coasters.md`. The "what" survey: `coaster-survey.md`.

**READ** means seen in the decompile, the MIPS or the data. **INFERRED** means reasoned to.

Object offsets are from the coaster object, where C = P: the vtable is at `+0x10`, and there is no `+8`
adjustment (unlike track rides). The status byte is `+0x9a`. Status numbers match `track-ride-operation.md`:
- 1 construction
- 2 running/open
- 3 closed
- 4 broken
- 5 broken with reliability 0
- 6 being repaired
- 7 repaired
- 10 loading
- 11 unloading

Reliability `+0xe4` is 20.12 fixed point: `0x64000` = 100.0, `0xa000` = 10.0.

---------------------------------------------------------------------------------------------------

## 0. Short version

1. **Status 2 and 10 are the same state for a coaster.** The status-2 tick `0x122ab0` just calls the
   status-10 tick `0x122af8` and then the empty status-11 tick.
   - The status-10 tick spawns trains if the ring is closed and none exist, advances the station
     animation, and applies wear when riders are aboard.
   - Boarding sets status 10. Nothing else in the coaster sets 2; status 11 never occurs.

   (READ, MIPS)
2. **Trains run in every status except 5,** and board or unload without looking at the status.
   - Closing the ride (3) does not stop them. A guest boarding a closed coaster **sets status 10 again**,
     which reopens it (`0x1b18d0..0x1b18f4`, MIPS).
   - At reliability 0 (status 5) every train **freezes in place with its riders** (`0x1238c0`, MIPS).
   - On repair (6) the trains move again. The track ride instead removes every car on entering 6.

   (READ)
3. **Wear rate** `0x1225f8`:
   ```
   rate = ((speedTerm + capTerm) / 2) × WearRate
   ```
   The track ride divides by 3 and adds a track-length term; the coaster has **no length term** (MIPS).
   It is applied per 4 ticks by the shared `0x117b88`. Breakdown happens deterministically when
   reliability is below 10.0, and only from status 2 or 10 (`0x1228d0`, MIPS).
4. **The Speed setting never reaches the trains.** Neither does Capacity or Duration: no train, car,
   node or coaster function reads them apart from those listed here (scan, READ).
   - Speed feeds only the ride value and the wear rate.
   - The panel applies only the Speed slider for kind 1 (`0x1d4fd0`).
   - Capacity = cars/train × seats/car × 6 (`0x1204d0`) feeds only the wear formulas.
5. **The test lap measures 8 things, from car 0 of one train only** (READ, MIPS):
   - duration, as steps/30;
   - length, the sum of the pylon segments' arc lengths in cells, shown as "meters";
   - max speed, ×175, shown as "kph";
   - number of drops, counted from pylon stack heights with a 256-unit threshold;
   - steepest drop, in degrees;
   - max +vertical g;
   - max −vertical g, scaled by 0.25;
   - max lateral g.

   There is **no inversion or airtime count**, and loop segments are skipped by the per-segment pass.
   The lap's first loop comparison reads an **uninitialised `$f21`**.
6. **Rating** `0x122ed0`:
   - "Ultimate Rollercoaster" when lateral < 0.5, 55 ≤ speed ≤ 70 and drops ∈ {2, 3}. This sets
     award bit `world·8 + park·4 + ord` in `0x2b72ac`, and only in parks 0 and 1.
   - Otherwise one of 27 texts from `0x2acda0[lat][speed][drops]`.
   - The stats are used **only** for the screen, the rating and the award. Their 16 accessors
     (`0x123f40..0x123fdc`) have no callers. No guest effect reads them.
7. **Ride value**:
   ```
   value = min(100, B × Q(Speed·4096/100) × Q(Duration·4096) >> 24)
   ```
   Duration is fixed at 1, so the duration factor is 1.0. This gives 67 (B = 90) or 71 (B = 95) at
   Speed 50, and B at Speed 100.
   - **The track's shape reaches guests nowhere**: not through the value, the wear, or any per-ride
     effect.
   - After-ride scoring treats kind 1 like any other ride.

   (READ)
8. **Rollercoaster Test Park** = world 0 (JUNGLE), park index 2. (READ)
   - It is selected by the "test park mode" flag `0x2b72a8`. The launcher `0x13b780` then loads park
     (0, 2).
   - The front-end offers "Rollercoaster Test Park" when at least one award bit is set.
   - The award mask is saved in the memory-card save at `+0x108`.
   - The same flag makes the wear rate 0 for the coaster (`0x1225f8`) and the track ride (`0x201f78`).
     The ordinary (`0x1b8108`) and tour-class (`0x1e9e38`) sites also read it; I did not decompile
     those.
   - `park == 2` makes debits free.
   - Its build list shows item *i* of **every** category only if **coaster *i* holds the Ultimate
     award**.
   - Registry entries with flag bit `0x8` are visible in park 2 (`0x17d7e8`, MIPS). The only
     terrain carrying it is JUNGLE `terrain_1`.
   - Rides, pylons, track pieces and cars take textures from `data\ultimate\<folder>`.
9. **Test-park seat defect:** the coaster init reads seats-per-car from `0x2e7220` with the
   **un-remapped** (world, 2, ord) triple (`0x11fcb8..0x11fce8`, MIPS), while the car init remaps.
   - Only ordinals 3 and 4 read their own entry. Ordinal 7 reads the wrong entry but it happens to have
     the same count.
   - Four ordinals (6, 8, 12, 13) read another coaster's car entry.
   - Seven (0, 1, 2, 5, 9, 10, 11) read model id 0.

   Consequence (INFERRED, not seen in play): boarding limits and "Capacity" are wrong in the test park.
   A division by zero is avoided only because wear is 0 there.
10. **Upgrades** (`0x116268`) are generic. They:
    - increment the tier;
    - re-run the defaults (reliability back to 100.0, Speed 50, Capacity setting and Duration reset);
    - debit the new tier's cost × 10.

    For a coaster the only tier-dependent inputs are:
    - WearRate 5/3/2 (MinSpeedDamage and MinCapacityDamage are 4 at every tier);
    - the queue head count 7 + 4·tier (port);
    - the park sum `0x16b7b8` (E·tier).

    Capacity, cars, trains, models and the ride value ignore the tier. No raiser of
    `STR_ADVMES_COASTER_UPGRADED` was found.

---------------------------------------------------------------------------------------------------

## 1. Update order (READ, `p1.c` and MIPS)

**Per update, `vt+0x3c` = `0x122a48`,** in this order:
1. `0x123500`, node neighbour windows.
2. `0x123598`, pylon models.
3. `0x123618`, pylon animation.
4. `0x123698`:
   - mesh rebuild;
   - texture phase `+0xe388` −= 0.08, wrapped;
   - `+0x144 = 0x1229d0()` (track valid).
5. `0x1237d0`, the style per-frame function.
6. `0x123840`, node visibility.
7. **`0x1238c0` trains**:
   - if not (`+0x148` ring closed and `+0x144` valid), call `0x1224c8`, which removes every train;
   - then, **unless status == 5**, run `0x1b0480` on each train (MIPS `0x1238d8..0x123940`).
8. **`0x1228d0`**, the breakdown check (§2.3).
9. Parent **`0x1169c0`**:
   - the status tick `0x1e5138`, which calls the status's tick handler, then
     `0x1e5268(this, +0x90)` and `+0x74 = +0x8c >> 12` unless status is 1;
   - the queued-guest update `0x116e20`;
   - the Life check (§4.6).

**Per frame, `vt+0x44` = `0x120560`:** calls `0x116a70` (queued guests' `vt+0x44` and the station
pose). No state changes.

A coaster update runs once per simulation tick (the tick chain is in `track-ride-operation.md` §2). The
wall-clock rate of a tick is not established.

---------------------------------------------------------------------------------------------------

## 2. The status machine

### 2.1 Handler table (READ: vtable `0x35b060` dumped with `elf.py`; functions decompiled and checked in MIPS)

Set-status is `0x1e4d70` (vt `+0x1f4`). It stores `+0x9a`, then calls **enter** `vt+0x1fc+8s`, even if
the status is unchanged. The tick `0x1e5138` calls **tick** `vt+0x25c+8(s−1)` for s = 1..11.

| s | enter | tick | what the tick does | exits (producer) |
|---|---|---|---|---|
| 0 | `0x1e4c98` timer `+0x8c`=0, `+0x90`=1 | — | — | init `0x1e0f30` sets 0 |
| 1 | `0x1e4ca8` `+0x74`=0, `+0x90`=0, timer=0 | parent `0x1e4f58` | construction animation. When `0x1e4e88` completes, **→ 10** (`0x1e509c`) | placement: `0x11fd28 → 0x1186d8 → 0x1e1238` sets 1 (`0x1e1264`) |
| 2 | parent `0x1164a8`: `0x1e4cb8`, `+0x122`=0 | **`0x122ab0`** = tick 10, then tick 11 (empty) | same as 10 | → 10 on the next boarding (`0x1b18f0`); → 4 below 10.0; → 3 by player |
| 3 | `0x1e4cc8` `+0x90`=1 | `0x1e50f0` **empty** | nothing (no spawn, no wear, no station animation). Trains are still stepped by `0x1238c0` and still board and unload | player Open `vt+0xfc`=`0x1e2738` → 2; **any boarding → 10** |
| 4 | **`0x1229a0`** = parent `0x1164d0` + `0x117798(this, 0)` | **`0x122bb8`**: wear `vt+0x364`, then tick 10 (`0x122af8`), then tick 11 | wear at least once per 4 ticks, twice when riders > 0 and the ring is closed (§4.3); trains run | `0x1228d0`: rel == 0 → **5**; mechanic arrives → 6 |
| 5 | parent `0x116550` | **`0x122b88` empty** | nothing. **Trains are frozen** (`0x1238c0` skips stepping in 5) | mechanic → 6 |
| 6 | parent `0x1e4cf8` `+0x90`=1 (**does not touch trains**) | **`0x122b90`** = tick 11 (empty) | nothing; trains run, unload, no wear | mechanic done → 7 |
| 7 | parent `0x116660`: set **2** (`0x1e4d08`), `+0xe4 = 0x64000`, set **10** | `0x1e5110` empty | — | (transient: ends in 10) |
| 8, 9 | parent (`+0x90`=1) | parent `0x1181d8`/`0x1181e0` | — | no constant producer in the ELF |
| 10 | parent `0x1e4d58`: timer=0, `+0x90`=1 | **`0x122af8`** | (a) if `+0x148` and trains `+0xe37c == 0` → spawn `0x1230b0`; (b) `0x1e4e88` (station animation timer, result ignored); (c) if `+0x148` and riders `+0x120 != 0` → wear `vt+0x364`; (d) tick 11 (empty) (MIPS `0x122af8..0x122b78`) | → 4 below 10.0; → 3 by player |
| 11 | `0x1e4d68` empty | **`0x122b80` empty** | — | never entered by a coaster (INFERRED from the producer scan, §2.2) |

**Enter 4, `0x1164d0` (parent, called by the coaster's `0x1229a0`)**, READ:
- `0x1e4cd8`;
- advisor message **`0x36`** `STR_ADVMES_RIDES_BREAKDOWN_IMMINENT`, with the ride attached
  (`0x107ca8(msg, 0x36)`, `0x107cb0(msg, ride)`);
- `0x1073c0(0x2aa720, 0, 1)` and `0x111150(bank 0xc, event 0xe2, 0)`;
- then **`0x117798(this, 0)` empties the queue**. Each queued guest is unlinked and sent event
  `{0x35a548, 7}` through its `vt+0x16c`, then `vt+0x2c(1)` and `0x14da60`.

**Enter 5, `0x116550`**, READ:
- `0x1e4ce8`;
- an advisor message whose id comes from `0x103658()` (not traced);
- `0x1073c0(…, 1, 1)`;
- effect **0x70** at the `vt+0x6c` position (`0x111428(bank 2, 0x70, pos, 0, 0)`).

### 2.2 Who sets which status (READ: scan of every `lw …,0x1f4` + `jalr` site, a1 constant)

Sites that apply to a coaster:
- **1**: placement `0x1e1264`.
- **2**: parent Open `0x1e274c` (via `vt+0xfc`); enter-7 `0x1e4d1c`.
- **3**: parent Close `0x1e277c` (`vt+0x104`).
- **4**:
  - coaster breakdown `0x122938`;
  - parent Life check `0x116a54`;
  - load `0x116d40`.
- **5**: coaster `0x122984`.
- **6**: mechanic `0x178634`/`0x178a74`.
- **7**: mechanic `0x17875c`; park teardown `0x154244`.
- **10**:
  - construction end `0x1e509c`;
  - enter-7 `0x11668c`;
  - **train boarding `0x1b18f0`**.

The other constant sites belong to other classes or to parent routines the coaster overrides:
- `0x116830`/`0x1167a8`/`0x1168e8` are in the parent script boarding `0x1166a8`, which the coaster
  never calls, because it overrides ticks 2/4/10/11;
- `0x2004f4…` are track rides;
- `0x1b80bc` is the ordinary ride.

The toggle `0x1e26d8` (2 → 3, else → 2) has **no caller** (no `jal`, no pointer).

### 2.3 Breakdown check `0x1228d0` (every update, before the status tick)

READ (MIPS `0x1228d0..0x12299c`):

```
if (status == 4 || status == 5)  0x118568(this, 1);        // service flag +0x11c = 1, pokes the station script
if (rel == 0) {
    if (status == 4) {
        if (+0x13c >= 0) { +0x13c = -1 (0x122558); 0x14daf8(1); }   // kick the player out of ride-along camera
        set 5;
    }
} else if ((status == 2 || status == 10) && rel <= 0x9fff) set 4;
```

- This is the parent's `0x116d68` test (status 2 only), widened to include 10. It is **not** the track
  ride's `0x200358`, which forces 4 from any status every update.
- A coaster therefore breaks down once per wear-down, and is not re-forced into 4 while in 3, 6 or 7.
- There is no random component. The threshold is deterministic.
- The parent's `0x116d68` also calls `0x116038` (an empty function, READ) before setting 4. The
  coaster does not; this makes no difference.

### 2.4 What the trains do in each status (READ unless marked)

The trains are stepped by `0x1238c0` in **every status but 5**. Their station behaviour (train state
machine, `p2.c`/`r1.c`) does **not** read the coaster status except in boarding:

- **Train state 4, board (`0x1b1858`, MIPS):** it boards when all of these hold:
  - this train is not the ride-camera train (`+0x250 != coaster+0x13c`);
  - the car cursor `+0x2a8` < cars/train;
  - the queue head (`coaster+0xf4`) exists and its guest's `vt+0xdc` returns **0x12**.

  When they hold:
  - **if coaster status != 4, set status 10**;
  - `0x1aec68` boards the guest into car `[cursor]` (→ `0x117c90`: guest state 0x15, riders +1, the
    rest of the queue gets event 0x13);
  - the cursor advances when the car is full (`seats <= riders` **after** adding);
  - go to state 3 (wait).

  When the head exists but is not in state 0x12 (or the queue is empty): state 3 if the train behind is
  not blocked (`+0x2bc`), else depart (state 0).

  Camera train or all cars full: depart.
- **Train state 2, unload (`0x1b16f0`):**
  - `0x1aed00` removes one rider, calling `0x117e08`:
    - the guest is placed on the ride's exit cell;
    - guest state becomes 0x16, so after-ride scoring follows;
    - riders −1.
  - Then the train goes back to state 1, whose dwell timer is `+0x2a4 = 160000`, set by the entry
    function `0x1b1b34` (table `0x3658c0`: states 1 and 3 → `0x1b1b34`).
  - So **each rider costs one 160000-unit wait**. The timer is decremented by the frame delta
    `0x397640`; its units are not established (area C).
  - When all cars are empty: attempts `+0x2ac = 40`, state 3.
- **Train state 3, wait (`0x1b1778`):**
  - on timeout, attempts−1: if < 0 → depart, else → state 4;
  - if the train behind is blocked → depart at once.

Consequences per status:

| status | trains | unloading | boarding | wear | new trains |
|---|---|---|---|---|---|
| 2 / 10 | run | yes | yes (sets 10) | if riders > 0 | yes, if the ring is closed and none exist |
| 3 closed | **run** | yes | **yes, and it sets 10: a guest at the queue head reopens the coaster** (INFERRED consequence) | none | no |
| 4 broken | run | yes | queue emptied on entry; nobody new can join (guests pick only 2/10/11, `0x120530`) | ≥ once per 4 ticks even when empty | yes |
| 5 | **frozen with riders aboard** | no | no | none | no |
| 6 repairing | run (riders return to the station and get off) | yes | only if someone is somehow at the queue head, which would set 10 and end the repair state early (INFERRED edge case) | none | no |

**Removing trains dumps riders with scoring.** Train removal `0x1af250` calls `0x1aeab8` on every car.
That unloads each rider through `0x1aed00` → `0x117e08` (exit cell, state 0x16, after-ride scoring), then
stops the train's sounds (`0x1af748`). READ. `0x1224c8` (remove all) is called from:
- ring open or invalid, each update (`0x1238f0`);
- ring close `0x120868`;
- the test lap `0x122da0`/`0x122df0`;
- spawn `0x1230d4`, the lift-flagging sim `0x123ab8`, remove `0x11fd8c`, and the tool `0x11bcc4`/`0x11caac`.

**Entering Build/Edit Track** (tool enter `0x11ad60` → `0x1234d8`) clears `+0x148` and unlinks the ring.
On the next update every train is removed and its riders are unloaded. Guests cannot pick the coaster
until the ring is closed again (`0x120530`). READ.

**Ride-along camera.** `vt+0x13c` = `0x122c80` (`+0x148 && +0x144`) gates the ride-along camera
(`0x152c48`: camera mode 3, or mode 1 if false). The camera train skips boarding.

### 2.5 Against the track ride (`track-ride-operation.md`)

| aspect | track ride | coaster |
|---|---|---|
| states used | 10 → 2 → 11 → 10 cycle, launch only when full | 2 and 10 behave identically; 11 never used |
| boarding | one guest per 20 ticks in status 10 only; cars wait unstepped | per train at the station in any status, while trains keep running |
| closed (3) | nobody on or off; cars keep driving | trains run, **unload and board**, and a boarding sets 10 (reopens) |
| enter 4 | `0x2002d8`: empty queue (no sound or advisor) | parent `0x1164d0` (advisor 0x36 + sounds) + empty queue |
| breakdown test | `0x200358`: any status, re-forced every update below 10.0 | `0x1228d0`: only from 2/10 |
| status 5 | cars keep moving; unload pass | **all trains frozen** |
| enter 6 | removes every car at once (everyone off) | nothing; trains carry riders back to the station |
| wear gate | cars exist, status ≠ 10, camera mode ≠ 2 | status 2/10 with riders, or status 4 (twice when riders are aboard) |
| wear formula | `(len + speed + cap)/3 × wr` | `(speed + cap)/2 × wr` (no length) |

---------------------------------------------------------------------------------------------------

## 3. Capacity and settings

### 3.1 Capacity `0x1204d0` (vt `+0x344`, READ)

`MaxCap = cars/train (0x2acc80[w][p][o] via 0x1233a8, remapped) × seats/car (u8 +0x138) × 6`.

The 6 is the train pool size (`0x1231e8` caps at 6). The number of trains actually spawned is
`clamp((pylons/3 + 2) / cars, 2, 6)` (`0x1230b0`, area C). So the seats really available are
`trains × cars × seats`, and MaxCap is an upper bound. The DBA `CapacityParameter` (tier `+0x30`) is
**not used** for coasters: the parent getter `0x117b28` is overridden.

`+0x138` is set once, in init `0x11fae0` by `0x1ae928(world, park, ord)`:
1. it loads car model `0x2e7220[w·9 + p·3 + o]`;
2. it counts the fittings with flag 0x80 through `0x17d360 → 0x1f2070` (MIPS `0x11fcb8..0x11fce8`,
   `0x1ae928..0x1ae9d8`).

**No remap** is applied here (see §8.7).

| coaster | cars | seats/car (fittings & 0x80) | MaxCap | default Capacity setting |
|---|---|---|---|---|
| Temple of Gloom | 4 | 6 | 144 | 72 |
| Chak Atak | 1 | 6 | 36 | 18 |
| Gorilla Thrilla | 1 | 2 | 12 | 6 |
| Hades | 1 | 6 | 36 | 18 |
| Dare Devil | 2 | 4 | 48 | 24 |
| Scatty Batty | 2 | 3 | 36 | 18 |
| Ghosta Coasta | 2 | 4 | 48 | 24 |
| Bone Shaker | 3 | 4 | 72 | 36 |
| Big Dripper | 2 | 2 | 24 | 12 |
| Caterpillar Coaster | 3 | 4 | 72 | 36 |
| Candy Coaster | 4 | 4 | 96 | 48 |
| Moonshot | 1 | 3 | 18 | 9 |
| Escape Velocity | 2 | 4 | 48 | 24 |
| The Shocker | 1 | 6 | 36 | 18 |

- The seat counts are READ from the disc models through the port's `Model.Fittings`. That the game
  counts the same entries is INFERRED from the identical offsets and test in `0x1f2070`.
- Every car's model entry is used for every car of the train. Caterpillar uses `caterbd` only (the
  what-doc's §2.1 finding).

### 3.2 Settings: defaults, ranges, and who reads them (READ)

**Defaults** `0x116120`. It runs at init (`0x116048`) and on every upgrade. It sets:
- reliability = `0x64000`;
- run timer `+0x122` = 0;
- **Capacity** `+0xec` = max(1, MaxCap >> 1);
- **Speed** `+0xe8` = minSpeed + (maxSpeed − minSpeed) >> 1 = **50**;
- **Duration** `+0xf0` = max(1, maxDuration >> 1) = **1** (MaxDuration is 1 on all 14 coasters).

**Panel** `0x1d4c80` (build) and `0x1d4fd0` (apply):
- For kind 1 the capacity slider's maximum is `vt+0x344` (MaxCap).
- The apply step writes Capacity and Duration **only when kind ≠ 1** and max ≥ 2. The cursor cannot
  even reach those rows. So **the player can change only Speed (1..100)** on a coaster.
- The coaster's own Capacity setter `0x122570` does not forward to the script, unlike the base
  `0x1183a0`.

**Readers**, from a scan of `vt+0x2f4/0x2fc/0x304/0x344` calls and `+0xe8/+0xec/+0xf0` loads over the
coaster class `0x11a600..0x124100` and the node/car/train code `0x199000..0x1b2700`:

| setting | read by |
|---|---|
| Speed (`vt+0x2f4`) | wear rate `0x12267c`, `0x1226ac`; ride value `0x12282c` |
| Capacity (`vt+0x2fc`) | wear rate with p = 1 only, `0x1226f4` |
| Duration (`vt+0x304`) | Reliability readout `0x1225b0`; ride value `0x122844` |

**Nothing in the train, car or node code reads any of them.** The Speed setting therefore changes the
excitement value and the wear, and **not how fast the trains go**.

The Speed setter `0x118378` does forward the value to the station model's script machine
(`0x118240 → 0x1fa818 → … → 0x1c1068`, which stores it at machine `+0xc0`). The consumer of that
field was not traced; the port's `ride-value-producer.md` also leaves it open.

### 3.3 The Reliability readout `0x122578` (vt `+0x2ec`, the panel's "Reliability" row `0x424`)

READ (MIPS):

```
readout = 100 − min(100, (rate(1) × Duration × 9) >> 12)        // Duration = 1
```

- The parent `0x1183f0` uses `>> 15`. The coaster's `>> 12` makes the prediction 8× steeper.
- rate(1) uses the Capacity setting, which is always MaxCap/2 (frac = 0.5). So the readout depends
  only on Speed and tier, and is identical for all 14 coasters.

| Speed | tier 0 (wr 5) | tier 1 (wr 3) | tier 2 (wr 2) |
|---|---|---|---|
| 50 | 78 | 87 | 91 |
| 100 | 67 | — | — |

Computed (`scratchpad/wear.py` logic in §4.2), not captured in game.

The other panel rows are the same as for track rides:
- "Repair" `0x118228` = rel >> 12;
- "Life" `0x118238`;
- "Excitement" `vt+0x1d4`;
- Age and Users (`0x1d5210`).

---------------------------------------------------------------------------------------------------

## 4. Wear, breakdown, repair, Life

### 4.1 Tier data (READ, EUR DBA, all 14 coasters)

- MinSpeedDamage = MinCapacityDamage = 4 at every tier.
- **WearRate 5 / 3 / 2.**
- Life (`InitialCondition`) at every tier:
  - 100 for most;
  - **80** for Hades and Scatty Batty;
  - **65** for Ghosta Coasta.
- Speed 1..100, Duration 1..1.

### 4.2 Wear rate `0x1225f8` (vt `+0x36c`, argument p)

READ (MIPS `0x1225f8..0x1227d0`). `/` truncates toward zero, `>>` is arithmetic.

```
if (u32[0x2b72a8] != 0) return 0;                // TEST-PARK MODE (see §8.1): no wear anywhere in the test park
msd = vt+0x34c (tier +0x24) = 4;  mcd = vt+0x354 (tier +0x28) = 4;  wr = vt+0x35c (tier +0x2c) = 5/3/2
s  = (Speed << 12) / 100
t  = ((0x1000 − msd) · s) >> 12
speedTerm = Speed < 100 ? t + msd : (msd + t + 0x1000) / 2
num  = p ? Capacity(+0xec) : riders(s16 +0x120)
frac = (num << 12) / MaxCap(vt+0x344)            // divide-by-zero break if MaxCap == 0 (beql … break 7)
capTerm = (((0x1000 − mcd) · frac) >> 12) + mcd
if (frac >= 0xccc) { k = (frac − 0xccc) >> 6; capTerm += k·k; }
return ((speedTerm + capTerm) / 2) · wr
```

### 4.3 Applying wear: parent `0x117b88` (vt `+0x364`)

READ (p3.c; MIPS in `track-ride-operation.md` §6.2):

```
if ((tick & 3) != 0) return;
old = rel; rel −= rate(0) >> 5;
Life −= old/0xf000 − rel/0xf000;  clamp Life ≥ 0, rel ≥ 0
```

It is called for a coaster only from:
- the status-10 tick `0x122af8` (statuses 2, 10 and 4), when `+0x148` and riders ≠ 0;
- the status-4 tick `0x122bb8`, unconditionally.

In status 4 **with riders aboard both calls happen in the same tick**, so wear is doubled. There is no
camera-mode gate, unlike the track ride's `0x2023b0`. The rider count is incremented by `0x117c90` and
decremented by `0x117e08` (READ).

Worked values (computed from the formulas; tier 0, Chak Atak MaxCap 36; one wear event per 4 ticks):

| riders | Speed | rate(0) | rel lost per 4 ticks | ticks of occupied running from 100.0 to < 10.0 |
|---|---|---|---|---|
| 1 | 50 | 5415 | 169 | ~8728 |
| 18 | 50 | 10250 | 320 | ~4612 |
| 36 | 50 | 15725 | 491 | ~3004 |
| 36 | 100 | 20840 | 651 | ~2268 |

Status 4 with nobody aboard: rate 5135, 160 per 4 ticks, so 10.0 → 0 in ~1024 ticks. **A broken
coaster that no mechanic reaches in time always reaches status 5** and freezes its trains (INFERRED from
the paths).

The rate depends only on the load fraction, so Temple of Gloom (MaxCap 144) gives the same numbers at 4×
the riders. Wear time is a **duration of occupied operation**, not a count of laps or rides.

### 4.4 Breakdown

§2.3. Below 10.0 in status 2 or 10 → 4. At 0 in 4 → 5.

### 4.5 Repair (mechanic side; same code as track rides, READ in r1.c)

- **`0x1785f8` / `0x178a38`:**
  - service flag `+0x11c` clear → route to the ride and set the flag (`0x118568(ride, 1 or 2)`);
  - flag set (arrived) → mechanic state 0xe/0x36, **ride → 6**, finish tick = now +
    `T[mech+0x50 & 7]` with `T = u16 0x3627c8 stride 4 = {240, 180, 120, 60, 60}`.
- **`0x1786d0`:** flag set → **ride → 7** (rel 100.0, status 2 then 10), clear the assigned mechanic
  (`0x1e1df8`) and the flag (`0x118678`).
- `0x1228d0` keeps requesting service (`0x118568(this, 1)`) every update in 4 and 5.
- A mechanic who services a working coaster sets 6. For a coaster this is harmless: trains keep running
  and nobody is ejected (INFERRED, from enter-6 being the flag-only `0x1e4cf8`).

### 4.6 Life (parent update `0x1169c0`)

READ:

```
if (Life == 0 && service flag == 0) {
    0x118568(this, 4);
    if (flag) add to the condemned list 0x153d70;
    if (!broken(vt+0xc4 = 0x1e2830)) set 4;
}
```

Life starts at tier-0 `+0x34` in `0x116048`, and an upgrade does not restore it. One wear-down from 100.0
to below 10.0 costs 6 Life (floor(409600/61440)). So a coaster with Life 100 / 80 / 65 survives about
17 / 14 / 11 wear-downs (INFERRED arithmetic).

---------------------------------------------------------------------------------------------------

## 5. Test lap, statistics, rating, Ultimate award

### 5.1 When it runs

Tool Triangle `0x11ba00` → `0x11bbd8`. The advisor checks come first:
- `+0x144 == 0` → advisor **203** `COASTER_COLLISION`;
- ring open → **204** `COASTER_TRACK_INCOMPLETE`;
- otherwise tutorial event 0x49.

Then (READ, MIPS `0x11bbd8..`):
- `0x2ac430 = 1` (stats screen on);
- **`0x122d48(coaster)`**;
- button bar Back `0x221` / OK `0x243`.

`0x11b2f8` (tool draw) calls the stats screen `0x11bd28` **every frame** while `0x2ac430 != 0`.

### 5.2 The lap `0x122d48`

READ, MIPS `0x122d48..0x122e44`:

```
+0x14c..+0x168 = 0
if (!(+0x148 && +0x144)) return;                 // stats stay 0
0x1224c8();                                       // remove ALL trains (riders unloaded with scoring)
0x1231e8(1.0);                                    // one train at position 1.0 (start of the segment into pylon 0)
T = coaster+0xd1c4
f20 = f21(caller's value!);  loop:
    0x1b0518(T)                                   // the train's normal RUN step (speed, blocking skipped: 1 train)
    f21 = T+0x08 (position)
    0x1b1ae8(T) → 0x1aeec0(T+0x1c)                // stats from CAR 0 only
    if (f20 < f21) { f20 = f21; continue }        // stop at the first step whose position did not increase (the lap wrap)
0x1224c8(); 0x1230b0();                           // remove, then respawn the service trains
for (n = exit.next; n != +0x174; n = n.next) 0x19d5f0(n);   // pylon segments p0..p(n−1)
```

- ⚠ The first comparison uses `$f21` as the **caller** left it. The function saves `$f20`/`$f21`, sets
  `f20 = f21` before the first step and never initialises it. If the caller's `$f21` is ≥ the position
  after step 1, the lap ends after a single step. What the tool code leaves in `$f21` was not traced
  (READ bug, INFERRED impact).
- The lap runs to the run state's wrap (position ≥ n + 2 + 0.75, area C), so it includes the two station
  segments. It is the real gravity/friction/floor simulation (area C), not a separate model.

### 5.3 Per step, car 0: `0x1aeec0`

READ, MIPS `0x1aeec0..0x1af04c`. Car frame (see §9 for the correction to the what-doc):
- `+0x14` is the **side (lateral)** unit vector;
- `+0x20` is **up** = cross(`+0x2c`, `+0x14`);
- `+0x2c` is **forward** (tangent).

Before moving, the car step `0x1aed90` stores a prediction:
`+0x80 = p(t) + speed × tangent` (straight-line extrapolation). The train then moves and re-poses the
car (`0x1b19e0 → 0x1aeb00`), so

`Δ = pos(+0x08) − predicted(+0x80)` (the curvature deviation over one step, in cells).

```
duration  (+0x14c) += 1/30            (0x3d088889)
maxSpeed  (+0x154)  = max(., speed(+0x78) × 175)          (0x432f0000)   "kph"
vert       = (up · Δ) × 100                                (0x42c80000)
maxVertPos(+0x160)  = max(., vert)                         (starts 0, so ≥ 0)
maxVertNeg(+0x164)  = min(., vert × 0.25)                  (0x3e800000; starts 0, so ≤ 0)
lat        = |side · Δ| × 20                               (0x41a00000)
maxLat    (+0x168)  = max(., lat)
```

These are ad-hoc scalings of a per-step displacement:
- no gravity (a level straight reads 0 g);
- no division by the time step;
- the "−Gs" value is quartered relative to "+Gs".

(READ constants; "ad hoc" INFERRED.)

### 5.4 Per segment: `0x19d5f0(node)`

READ, MIPS `0x19d5f0..0x19d86c`. The node is skipped if it is the station exit node (`owner+0x794`) or
a **loop segment** (`+0x53 == 2`).

- **Steepest drop** `+0x15c`:
  - over the 16 intervals between the 17 samples (sample positions at `+0xd8 + i·0x48`, i = 0..16);
  - where `dy = y_i − y_{i−1} < 0`:
    `deg = trunc( atan2(−dy, sqrt(dx² + dz²)) × 360.0 × 0.5 / π )`;
  - then `+0x15c = max(., deg)`.
  - Doubles `360.0` at `0x3654d8` and `π` (float) at `0x3654e0`. `0x28cb78` is atan2 (INFERRED from
    the arguments and the degree conversion); `0x296778` converts double to int.
- **Length** `+0x150` += node `+0xd0` (the segment arc length, in cells; no scale). It is shown as
  "meters", so 1 cell reads as 1 m.
- **Drops** `+0x158` (float) += 1 when both hold:
  `h(self) − h(prev) < 256` **and** `h(prev) − h(prevprev) > 256`.
  - h = `0x19a1d8`, the **pylon stack height** (node `+0x44` plus the stack below). It is not the
    absolute track y: terrain height is ignored.
  - For station nodes h = the `0x2acb60/64` heights.
  - Window `+0x18/+0x1c/+0x20` = prevprev/prev/self (what-doc §4.2).
  - A "drop" is therefore counted at the node after a climb of more than 256 units, when the next rise
    is under 256: a crest (INFERRED reading).
  - Units: the tool steps heights by 0x14, so 256 ≈ 12.8 steps.

Only nodes p0..p(n−1) are visited. So the segment into the station entry node, the station segment and
every loop segment contribute to none of length, steepest drop or drops.

**Not measured:** inversions, loops, airtime, bank, time upside down. No other field is written. The
8 floats are the whole record (READ: every writer of `+0x14c..+0x168` in the coaster, car and node code
is listed above).

### 5.5 Stats screen `0x11bd28`

READ. Labels are at x = 0x32 and values at x = 300 (0x12c). Integers are drawn as `(int)` truncation
(EE `cvt.w.s`) with `"%4i"`; floats with `"%4.1f"` (`0x36c878`).

| y | label (row) | value | unit (row) |
|---|---|---|---|
| 0xd0 | `STR_COASTERSTATS_DURATION` 0xf4 "Duration" | int `+0x14c` | 0x2d "secs" |
| 0xe4 | 0x244 "Length" | int `+0x150` | 0x227 "meters" |
| 0xf8 | 0x335 "Maximum Speed" | int `+0x154` | 0x17d "kph" |
| 0x10c | 0x382 "Number of Drops" | int `+0x158` | — |
| 0x120 | 0xdb "Steepest Drop" | int `+0x15c` | 0x143 "deg" |
| 0x134 | 0xb4 "Max Vert +Gs" | `%4.1f` `+0x160` | 0x2d6 "g" |
| 0x148 | 0x53 "Max Vert -Gs" | `%4.1f` `+0x164` | 0x2d6 "g" |
| 0x15c | 0xc9 "Max Lat Gs" | `%4.1f` `+0x168` | 0x2d6 "g" |
| 0x184 | 0x228 "Coaster Rating:" | text row from `0x122ed0` | — |

- OK `0x11bca8` leaves to tool mode 3 (queue placement) when the build started from station
  placement (`0x2ac434`, after removing the trains), else to mode 0.
- `0x11bd28`'s only caller is the tool draw `0x11b2f8`.

### 5.6 Rating `0x122ed0`

READ, MIPS `0x122ed0..0x1230ac`:

```
if (lat < 0.5 && 55.0 <= speed && speed <= 70.0 && 1.0 < drops && drops < 4.0) {
    0x1542b0(world 0x14e170(), park 0x14e160(), ord +0x97);      // raw indices, NOT remapped
    return 0x256;                                                // STR_COASTER_RATING_ULTIMATE "Ultimate Rollercoaster"
}
L = lat < 0.5 ? 0 : lat < 1.0 ? 1 : 2            // BORING / CALM / VIOLENT
S = speed <= 50 ? 0 : speed <= 75 ? 1 : 2        // SLOW / MEDIUM / FAST
D = drops <= 1 ? 0 : drops <= 6 ? 1 : 2          // TAME / NORMAL / LOTS
return u32 0x2acda0[L·9 + S·3 + D]
```

In raw units the Ultimate window is:
- car-0 speed 0.3143..0.4 cells/step;
- |side·Δ| < 0.025 cells;
- 2 or 3 drops.

It is a sub-box of the "Average" cell.

The table `0x2acda0` (text row, EUR English):

| L \ S | SLOW: TAME / NORMAL / LOTS | MEDIUM: TAME / NORMAL / LOTS | FAST: TAME / NORMAL / LOTS |
|---|---|---|---|
| BORING (lat < 0.5) | 0x156 Too Slow / 0x3b Boring / 0x223 Up And Down | 0x5d Okay / 0x2bc Average / 0x107 Could Be Faster | 0x98 One Shot Ride / 0x290 Excellent / 0x141 Pretty Good |
| CALM (< 1.0) | 0x2ca Still Slow / 0xf8 Still Bored / 0x3a7 Like A Yo-Yo | 0x12a Low On Thrills / 0x329 Just Above Average / 0x1fa A Little More Speed? | 0x1f1 Just One Drop? / 0x388 Not Bad / 0x2bf A Bit Wild |
| VIOLENT | 0x10b Slow And Shaky / 0x41c Not Exciting / 0x1d7 Faster Please | 0x25 Not Thrilling / 0x278 Feeling Ill / 0xd5 Not Fast Enough | 0x56 Shaken Not Stirred / 0x24f Vomit Inducing / 0xfb Too Scary |

(Key names `STR_COASTER_RATING_{BORING,CALM,VIOLENT}_{SLOW,MEDIUM,FAST}_{TAME,NORMAL,LOTS}` confirm the
axis order.)

### 5.7 The award

READ, MIPS `0x1542b0..0x154374`.

- **`0x1542b0(w, p, o)`:** if `p < 2` and the bit is not already set, `0x2b72ac |= 1 << ((w·8 + p·4 + o)
  & 31)`. It is idempotent, which matters because `0x122ed0` runs every frame the stats screen is shown.
  In the test park (p = 2) **no award is recorded**.
- **`0x154328(w, p, o)`** (query): p > 1 → false; debug flag `0x2b3070 != 0` → true (the same flag
  makes everything available in `0x12b6d0`); else the bit.
- **`0x154378`** counts the set bits over w 0..3, p 0..1, o 0..3.

Readers:
- `0x154328`: the test-park build list `0x15cb4c` (§8.5) and the awards screen `0x186238`. The awards
  screen has title rows 0x1db and 0x43e "Ultimate Coaster Awards", and draws one icon per coaster with
  the bit from the table `0x2c4040`/`0x2c4048`. The `ultimatec\s_*.ssh` sprite names are at
  `0x36d640..`, referenced at `0x2162e8`.
- `0x154378`: the front-end menu `0x16db00`/`0x16dea8` (§8.2) and a HUD counter `0x13dcb8`, drawn next
  to icon 0x2e beside the `0x1c36d8` counter.

**Nothing in gameplay** (guests, park rating, money) reads the award. READ: the reader lists of
`0x154328` (4 `jal` sites) and `0x154378` (7 sites), plus the award-mask xrefs.

**No advisor message.** The text row 0xb1 `STR_ADVMES_ULTIMATE_COASTER` exists in the text DB, but its
key string is **absent from the ELF**, and no advisor record (275 records at `0x2a6ac8`) uses row 0xb1.
The only visible sign of the award at the time is the rating text (READ absence; INFERRED "unused").

---------------------------------------------------------------------------------------------------

## 6. Ride value and what guests experience

### 6.1 Value `0x1227d8` (vt `+0x1d4`)

READ (MIPS `0x1227d8..0x1228c8`; matches `ride-value-producer.md`):

```
B = DBA +0x18 (90 or 95);  if (B == 0) return 0
A = clamp((Speed << 12) / 100, 0xc00, 0x1400)
Z = clamp(Duration << 12, 0xc00, 0x1400)          // NO /5 (track/ordinary divide by 5); Duration = 1 → 0x1000
return min(100, (B · ((A · Z) >> 12)) >> 12)
```

- Speed ≤ 75 gives factor 0.75: **67** (B = 90) or **71** (B = 95).
- Speed 100 gives factor 1.0: **90 / 95**.
- There is **no track term** (the track ride adds `+0x1d1 >> 1`).

### 6.2 Guest effects

Readers of E (`vt+0x1d4`, 12 call sites, same list as the track note):
- the after-ride scoring `0x20edd8` (corpus decompile). Kind 1 shares the branch with kinds 3/6/7.
  Constants READ at `0x2eeb30..0x2eeb44`:
  - taste P = `u16[0x2eebd8 + 8·type]`;
  - happiness `g+0x75` +15 / +10 / +5 by |P − E| ≤ 20 / ≤ 50 / else, capped at 100;
  - if E > 55: sick `g+0x76` += (1212·(E − 30)·4096) >> 24;
  - `g+0x78` −= (4096·E·4096) >> 24 = E;
  - users +1.
- destination choice `0x20c3e8`/`0x20c418`;
- queue patience `0x210468`;
- park rating `0x210b74`;
- park sum `0x16b84c`/`0x16b874`, where E counts × tier;
- the lists `0x109dcc`, `0x10b340`, `0x1d8328` and the panel `0x1d52ac`.

**While riding:** state 0x15 (set by `0x117c90`). Per the track note, `0x20e0e8` only sets flags, so
there is no per-tick rider effect.

The trains' scream and whoosh sounds (area C) are audio only.

**Answer to "does the track shape matter anywhere guests see it":** no.
- E has no track term.
- The wear has no length term.
- The eight stats feed only the stats screen, the rating text and the award bit, and their accessor
  functions `0x123f40..0x123ff4` have **no callers** (no `jal`, `j` or pointer; READ). They are dead
  code.
- The only other `+0x14c..+0x168` float accesses in the ELF are in other classes' functions
  (`0x1b86fc` ordinary ride, `0x19b290`/`0x19bce4` node sample records, `0x1f6d3c`, `0x161140`,
  `0x193228`, `0x1cf584`, `0x21fea4`, `0x23c8fc`, `0x1bf054`). All were decompiled in r3.c; none takes
  a coaster pointer (INFERRED from their callers and types).

Guests experience the shape only through indirect physical facts:
- trains take longer on a longer track, which delays unloading;
- the train count grows with the pylon count (`0x1230b0`).

These are INFERRED consequences, not coded "experience".

This agrees with the port's `findings/ride-value-producer.md` (coaster row `0,1227D8`; "Z = Q(D << 12):
NO /5"; B from payload `+0x18`) and with the what-doc §7.

---------------------------------------------------------------------------------------------------

## 7. Upgrades `0x116268` (READ, MIPS head `0x116268..0x116300`)

```
+0x128 = 0
if (tier < 3) {                                   // sltiu …,3: the bound admits tier 3; the UI presumably stops at 2 (not traced)
    tier += 1;  0x116120();                       // rel=100.0, run timer 0, Capacity=max(1,MaxCap>>1), Speed=mid(=50), Duration=1
    debit(record[tier] +0x50 (PurchaseCost of the NEW tier) × 10)   // 0x100698, free in the test park
    if (!silent) effect 0xb8 (0xe1 when the new tier is 3) at the ride position
}
```

What a coaster tier changes:
- **WearRate** 5 → 3 → 2 in `0x1225f8`: wear and the readout (§3.3).
- MinSpeedDamage and MinCapacityDamage stay 4. Life is not restored.
- The queue head count **7 + 4·tier** (port `native-ride-queue.md`, `0x20D530`).
- The park-level sum `0x16b7b8`, which adds `E·tier`.
- The **upgrade itself repairs** (reliability back to 100.0) and resets Speed to 50. INFERRED
  side-effect: a Speed-100 coaster drops back to value 67/71 until the player raises Speed again.

What it does not change (READ in the respective tables and functions):
- MaxCap `0x1204d0` (tier-less `0x2acc80` and `+0x138`);
- the train count `0x1230b0`;
- car and pylon models `0x2e7220`/`0x2e2b30`;
- the style table;
- the ride value `0x1227d8` (no tier term);
- the DBA CapacityParameter 18/24/30 (unused by coasters).

`STR_ADVMES_COASTER_UPGRADED` (advisor 205 = 0xcd) has no constant raiser:
- no `li 0xcd` within 30 instructions of any `0x107ca8` call;
- none near the variable raisers `0x16bb38`, `0x16c120`, `0x10e4b8`, `0x107c18`.

The same is true of advisors 110/111 (`CONGRAT_BIG/FUN_COASTER`) and 166 (`GOLD_TICKET_ROLLER_COASTER`).

---------------------------------------------------------------------------------------------------

## 8. The Rollercoaster Test Park

### 8.1 The mode flag and the launch (READ)

- **`0x2b72a8`** is the test-park mode flag: getter `0x153410`, setter `0x153420`.
- `0x153420(v)` stores v, and if v ≠ 0 and `0x14e538() == 0` it calls `0x14e4c0`.
- Its writers:
  - the front-end menu `0x16dea8` (item 0 → 0, item 1 → 1);
  - the launcher `0x13b780(world, park, …, flag)`;
  - the save loader `0x1c3fc0`: `0x153420(record+0xce == 0)`.
- **Launch `0x13b780`:**
  ```
  0x153420(flag);
  if (!0x153410()) 0x149678(desc, world, park);
  else             0x149678(desc, 0, 2);          // world 0 = JUNGLE, park index 2
  ```
  `0x149678` stores world at desc `+0x30` and park at `+0x2c`. `0x150e20` copies them to the globals
  (world `0x3952e4` = desc `+0x30`, park `0x3952e8` = desc `+0x2c`). `0x150e20` has no direct `jal`,
  so that the launch descriptor reaches it is INFERRED; both globals have only this one writer
  (xrefs, READ).
- The main-menu result `0x13b2a0` is 0/1 in test-park mode and 2/3 otherwise. The front-end loop
  `0x13be08` sends 0/1 straight to launch with flag = 1, and 2/3 to park selection with flag = 0.
- `park == 2` is tested by **`0x154428`**, which has 7 callers, all read:

| caller | effect in park 2 |
|---|---|
| `0x100698` debit | always succeeds **without deducting** (also when `0x2a60b8` ≠ 0) |
| `0x126390` affordability | always affordable |
| `0x166638` shared textures | `"data\ultimate\sharetex"` instead of `"<world dir>\sharetex"` |
| `0x17b240` model path flags | categories 9, 1, 10, 11 (pylons, rides, track pieces, cars) get flag bit 31 |
| `0x1f79d0` texture dir | with that bit: `"data\ultimate\<last folder component>"` + `\textures` (e.g. `Rides\MineCart` → `data\ultimate\MineCart\textures`) |
| `0x1c7bb0` quit handler | back to the main menu (`0x10008`) **keeping the award mask** (`0x2b62a0 = 1`); from a normal park, to park selection `0x60008` |
| `0x1dc2a8` returns `0x12b680(res, +0x48 & 7)` (INFERRED: a cost) | 0 |

`0x2b72a8 != 0` (`0x153410`, 31 `jal` sites) is also tested:
- in the wear rates (§4.2, and the track ride's `0x201f78`): **no wear, so no breakdowns and no Life
  loss** in the test park. The ordinary `0x1b8108` and tour `0x1e9e38` sites were not decompiled.
- in the build-list filter (§8.5): feature items with DBA `+0x2e` bit 1 are hidden;
- in the gold-ticket handler `0x1de1f0`.

Other readers were not followed: `0x1067dc`, `0x12b704`, `0x13d078`…`0x13e4c4`, `0x14ad18`,
`0x160ad8`, `0x163984`, `0x164058`, `0x16ba68`, `0x16bca4`, `0x16e570`…`0x16e7dc`, `0x1c2980`,
`0x1c3b04`, `0x20cadc` and `0x2100e8` (the last two are in guest code).

### 8.2 The unlock

The front-end menu is built by `0x16db00` and drawn and handled by `0x16dea8`. Both branch on
`0x154378()` (the award count):
- **0 awards**: items from `0x2b9798` = `{0x2fe "Main Game"}, {0x6b "Exit"}`;
- **≥ 1 award**: `0x2b9770` = `{0x2fe "Main Game"}, {0xa9 "Rollercoaster Test Park"}, {0x6b "Exit"}`.

Selecting "Main Game" / "Rollercoaster Test Park" calls `0x153420(0 / 1)`.

So **the Test Park appears once any coaster in parks 0 or 1 has earned the Ultimate rating**, or when
the debug flag `0x2b3070` is set (READ). The "Choose your Rollercoasters" text (`STR_COASTER_TEST_PARK_CHOOSE`)
has no direct `li` reference; its consumer was not traced.

### 8.3 Where the award is stored

- In the **memory-card save**. The save writer `0x1c39b8` copies `0x2b72ac` into the record. The loader
  `0x1c3fc0` checks the header (magic `0x54505701` at `+0x88`, version `0xac` at `+0x8c`) and restores
  `0x2b72ac = u32 [+0x108]`.
- The save has per-(world, park) slots (`0x1c34b0`: slot = world·3 + park, 8 bytes each, 4 × 3 slots;
  park 2 is the test park's).
- Reaching the main menu clears the mask (`0x13b2a0` @ `0x13b304`, `0x13be08` @ `0x13bfe0`) **unless
  `0x2b62a0` is set**. `0x1c7bb0` sets it when leaving the test park.

So the unlock survives across sessions only through the save. That the mask is part of a global header
rather than of each park slot is INFERRED from the single `+0x108` field.

### 8.4 Map and registry

**Map:** world 0 (JUNGLE), park index 2 (READ, `0x13b780`).
- Heightfield: `0x1f66b8 → 0x1f3248(0, 0)`, with param 0 → `0x147090()` = `"data\jungle\"` →
  `"%s\Terrain"`. It loads `base.md2` and the object `Base` (or `TestBase` if `Base` is absent). This
  path is per world, not specific to the test park.
- **Registry loader that tests bit 0x8: `0x17d7e8(id)`**, READ (MIPS `0x17d7e8..0x17d8e4`). It walks the
  registry `0x2bf2b8` (0x24-byte entries, count `u32 0x2c3300`) and returns the first index whose
  entry satisfies all three:
  - `id == +0xc`;
  - world-ok: `+4 == 4` (shared) or `+4 == current world`, **or test-flag**;
  - park-ok: `(flags(+0x10) & ~8) == 0` or `== park + 1`, **or test-flag**.

  Here `test-flag = (park == 2) && (flags & 8)`. So `flags & 3` is the park mask (1 = park 0,
  2 = park 1), and **bit 3 makes an entry visible in the test park regardless of world and park**.
  Callers: `0x17b068` (preload loop over ids 0..0x261), `0x17b138`, `0x17b610`, `0x17b9d8`,
  `0x17bb50`, `0x17c03c`, `0x17c04c`, `0x17d560`, `0x196880`.
- Entries with bit 8 (READ):
  - the 14 coasters (cat 1);
  - their 14 pylons (cat 9, ids 428..441);
  - their 14 cars (cat 11, ids 442..455);
  - **JUNGLE `terrain_1`** (id 210, flags 0x9), **the only one of the 8 terrains** with bit 8;
  - JUNGLE `Bus1` (cat 15, 0x9);
  - JUNGLE features `bushIV` 179, `bigpalm` 190, `mamfount` 197 (0x9), `lavspurt` 195, `speaker4`
    203, `statue2` 206 (0xa), and `supbog` 207 (0x8, test park only).

  The test park therefore renders JUNGLE `terrain_1` (INFERRED: the terrain id request itself was not
  traced, but no other terrain id passes `0x17d7e8` in park 2).
- **Catalogue:** the JUNGLE park-list block `0x158cf0` (at `0x158f10`/`0x158f5c`) references a
  14-entry coaster list at `0x2b7808` = keys 225, 217, 218, 132, 169, 133, 135, 170, 47, 52, 51, 371,
  370, 376 (count 14 at `0x2b7840`), in exactly the remap order of `0x2ace10`. It also references the
  7-feature list at `0x2b77e8` = 179, 190, 203, 206, 197, 195, 207, which are exactly the bit-8
  features. (READ data and references; that these are park 2's lists is INFERRED.)

### 8.5 Remap, stock, build list

- **`0x11f930(&w, &p, &o)`:** if `p == 2`, replace (w, p, o) with `0x2ace10[o]` (12-byte
  `{world, park, ord}`) = the 14 home triples in list order. Entry 14 onward is unrelated data (4, 6, 512 …).
- It is applied by every per-coaster table getter: `0x122060`, `0x122e48`, `0x1233a8`, `0x123430`, the
  node and style code (`0x19a55c` … `0x1a1e30`), the car init `0x1aea18`, the camera `0x1b1ba8` and the
  build list `0x15cb3c`. It is **not** applied by the seat count `0x1ae928` (§8.7) or the award
  `0x122ed0`.
- **Stock `0x14ccb0`** = (park == 2 ? 14 : 2) − used coasters (pool `+0xc`).
- **Build list `0x15ca78`** (MIPS `0x15cab0..0x15cbc4`), for every category it is called with
  (kinds 3, 6, 7, 1, 4, 5, 2 from `0x15cc60..0x15cf28`) and every list index i:
  ```
  item = 0x12ae78(db, kind, i); if (!item) skip
  if (kind == 2 && (item+0x2e & 2) && 0x153410()) skip
  if (park == 2) show = 0x154328(remap(world, 2, i))
  else           show = 0x12b6d0(db, kind, i, 0) [research/availability]
  ```
  In the test park **coaster i is offered only if it holds its Ultimate award**. There is **no kind
  check**, so item i of any other category is gated by coaster i's award too (READ; INFERRED oddity).
  Items at i ≥ 14 read past the remap table, get p = 6 > 1, and are never shown.

### 8.6 What else differs in the test park (READ)

- No money is spent (debits succeed without deducting).
- No wear or breakdowns.
- The award cannot be earned there (`0x1542b0` needs p < 2).
- The rating text still says "Ultimate Rollercoaster".

### 8.7 Seat-count defect in the test park

READ indexing, INFERRED effect. `0x11fae0` → `0x1ae928(w, p, o)` indexes `0x2e7220[w·9 + p·3 + o]`
without the remap (MIPS `0x11fcb8..0x11fce8`, `0x1ae950..0x1ae990`). For JUNGLE park 2 that is flat
entry `6 + ord`:

| test ord | coaster | entry read | model | seats read | real seats |
|---|---|---|---|---|---|
| 0 | Temple of Gloom | 6 | 0 | 0 (INFERRED) | 6 |
| 1 | Chak Atak | 7 | 0 | 0 | 6 |
| 2 | Gorilla Thrilla | 8 | 0 | 0 | 2 |
| 3 | Hades | 9 | 445 maggot | 6 | 6 ✓ |
| 4 | Dare Devil | 10 | 446 devil car | 4 | 4 ✓ |
| 5 | Scatty Batty | 11 | 0 | 0 | 3 |
| 6 | Ghosta Coasta | 12 | 447 bat | 3 | 4 |
| 7 | Bone Shaker | 13 | 448 coasta cart | 4 | 4 (coincidence) |
| 8 | Big Dripper | 14 | 449 shake car | 4 | 2 |
| 9 | Caterpillar | 15 | 0 | 0 | 4 |
| 10 | Candy Coaster | 16 | 0 | 0 | 4 |
| 11 | Moonshot | 17 | 0 | 0 | 3 |
| 12 | Escape Velocity | 18 | 450 b_drip car | 2 | 4 |
| 13 | The Shocker | 19 | 451 caterbd | 4 | 6 |

The car *model* is still right, because the car init remaps.

With seats = 0:
- `0x1aec68` reports a car full after its first guest (`0 <= 1`), so each car takes one rider;
- MaxCap = 0.

`frac = num / MaxCap` would execute `break 7`. It is never reached, because both wear-rate callers
return 0 first in test-park mode.

Model id 0 is INFERRED to give 0 seats: `0x17d7e8(0)` finds only the `EOL` sentinel, and
`0x17d360` returns 0 when the model has no resource. Not observed in play.

---------------------------------------------------------------------------------------------------

## 9. Corrections to `coaster-survey.md`

1. **Car vectors** (§6.1): `+0x14` = **side/lateral**, `+0x20` = **up**, `+0x2c` = **forward
   (tangent)**, not forward/up/side.
   - Evidence: `0x1aed90` extrapolates `+0x80 = p + speed × (3rd output of 0x19bda0)`, and `0x1aeb00`
     writes that 3rd output to `+0x2c`.
   - `0x19bda0`'s 2nd output is the loop's sideways axis `(P1 − P2)`.
   - The stats screen calls the `+0x14` projection "Lat" and the `+0x20` projection "Vert".
   - The run state tests `train+0x4c` = car0 `+0x30` = tangent.y for climbs and drops.
2. `+0x120` riders: READ (`0x117c90` +1, `0x117e08` −1), no longer INFERRED.
3. `+0x148` is **"ring closed / track complete"**. It is not the ride's open/closed status (status 3).
4. `0x122578` is the **panel "Reliability" row** (label 0x424) and uses `>> 12`.
5. §6.4:
   - the stats come from car 0 only;
   - duration is steps/30;
   - "−Gs" is scaled by 0.25 and lateral by 20;
   - drops use pylon stack heights, not track y;
   - loop segments are excluded.
6. §3.3: statuses 2 and 10 are the same. 11 is never used. Nothing is "open 2 / closed 3" in the sense
   of trains stopping.
7. §1 table "park" column: those are park **indices 0/1** (terrain_1/terrain_2). The test park is
   index 2. The award bit uses indices 0/1.

For the track-ride notes (read-only for me): `0x2b72a8`, the "global switch set by `0x153420`" in
`track-ride-operation.md` §6.1 and §11, **is the Rollercoaster Test Park mode flag**. Track rides do not
wear there either.

---------------------------------------------------------------------------------------------------

## 10. Still unknown (and what I tried)

- **Tick and timer rates.** Stats duration assumes 30 steps/s. The station dwell uses 160000 units of the
  frame delta `0x397640`. The wall-clock rate was not traced (area C / shared gap).
- **The uninitialised `$f21` in the test lap.** I did not trace what `0x11bbd8`'s callers leave in
  `$f21`, so whether the lap can end after one step in practice is open.
- **Whether guests leave a closed coaster's queue.** This decides whether closing a coaster sticks,
  given that boarding sets 10. I read `0x117798` (used only on breakdown) and the board state, but not
  the guest-side queue logic (`vt+0x3c` of queued guests via `0x116e20`).
- **The terrain id the park loader requests.** I established it by the bit-8 filter (only
  `terrain_1` JUNGLE passes in park 2), not by reading the terrain load call.
- **The "Choose your Rollercoasters" screen** (`STR_COASTER_TEST_PARK_CHOOSE`, row 0xb6): no `li 0xb6`
  exists. The build list `0x15ca78` is the award filter; the title's consumer was not found.
- **Advisors 110/111/166/205** (coaster congratulations, gold ticket, coaster upgraded): no constant
  raiser found (scanned `li` near every `0x107ca8` call and near the 4 variable raisers). They may be
  unused on PS2, or raised through a computed id.
- **Main-menu item → flag mapping** in `0x16dea8`. With 0 awards the second item is "Exit", yet case 1
  still calls `0x153420(1)`. The menu id returned by `0x105a30` was not read, so the exact mapping of
  ids to cases is INFERRED.
- **The speed value forwarded to the station script machine** (`machine+0xc0`): consumer not traced.
- **Seat count for model id 0** (§8.7): INFERRED 0; not run.
- **Upgrade UI bound** (tier < 3 in `0x116268`; the DBA has 3 tiers): the UI side was not read.
