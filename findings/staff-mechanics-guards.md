# Staff (PS2), area C: mechanics, guards and their world hooks (the "how" pass)

Researched 2026-09-27 by agent `sC`. Starting map: `staff.md` §3.2, §3.3 and "Areas → C"; every lead
there was re-read, and the ones that turned out wrong are listed in §8.

Sources:
- `SLES_500.32` in Ghidra 12.1.2, own copy `~/ghidra_tpw/agent_sC`. Decompiles in `~/ghidra_tpw/agent_sC/out/`:
  `c1.c` (ride helpers, dispatch, guest decision), `c2.c` (iterator, ride-class handlers, park init/teardown),
  `c3.c` (script flag, ride load, staff base, person machine, object loop), `c4.c` (ordinary/tour status
  handlers, wear, staff room); xref lists `x1.c`, plus the `c*.c` `x:` blocks. `*s.c` are the same files with
  the stack noise stripped. The what-pass decompiles (`~/ghidra_tpw/agent_staff/out/mech.c`, `guard.c`,
  `s2.c`, `s4.c`) and the corpus (`FUN_0020f588`, `FUN_002113a8`, `FUN_0020d628`) were reused.
- Raw MIPS (`full.s`, helper `out/py/m.py`): every constant, compare and branch that a formula below depends on
  was checked there; the addresses are given where it mattered. Tables and vtables read with `out/py/elf.py`
  (`out/py/vts.py` dumps every ride-family vtable).
- The port, read-only (`~/tpw-coasters`): findings `coaster-operation.md`, `track-ride-operation.md`,
  `native-entrance-lifecycle.md`, `native-ride-queue.md`, `native-incoming-controller.md`,
  `native-booth-admission.md`, `visitors.md`; code `CoasterSim.cs`, `TrackRideSim.cs`, `NativeRideReliability.cs`,
  `ParkSim.cs`, `ParkEntrance.cs`, `NativeEntranceFlow.cs`, `NativeRideQueue.cs`, `game/Viewer.cs`.
- Disc text (EUR text DB, `AdvisorCatalogue`) through the what-pass scan tool; ride scripts in `~/ghidra_tpw/notes/*.rss`.

**READ** = seen in the decompile, MIPS or data. **INFERRED** = reasoned to. Designer names come from the debug
state table `0x10ca98`, text keys, and `.rss` comments, and are marked as such.

**Offsets.** A staff pool slot is `P`; the vtable object is `C = P + 8`. Subclass functions (`0x178xxx`,
`0x140xxx`) take `P`, base functions take `C`. Tables give `C+` offsets (`P+` = `C+` + 8): target `C+0x20`,
deadline/timestamp `C+0x24`, route slot `C+0x28`, flags `C+0x2c`, mode `C+0x2e`, state `C+0x2f`, logical
animation `C+0x30 & 0x1f`, facing `C+0x34`, level `C+0x48 & 7`, walk speed `C+0x48 >> 3`, tiredness `C+0x4b`,
morale `C+0x4c` (staff-what §2.3). Ride offsets are **parent offsets** (the object whose vptr is at `+0x10`; a
track ride's parent is `ride+8`, so parent `+X` = track-ride `+X+8`). Guests: `N` = pool slot, `N+8` = object.

**Units.** One tick = one pass of the object loop `0x14be60` (the ride update runs once per tick,
track-ride-operation.md §2). The port runs 40 ms ticks (`ParkSim.TickMilliseconds`, ParkSim.cs:342); the
console's wall-clock rate is not established. Positions: cells, or fine units of 1/256 cell. Walking moves
`max(5, speed) × D >> 14` fine units per tick (`0x191e98`), `D = 0x4000` per tick, so **speed = 1/256 cell per
tick**. Money: the game's raw unit ×10 as debited (`0x100698(fin, amount × 10)`).

---------------------------------------------------------------------------------------------------

## 0. Short version

1. **Only the four ride classes can need a mechanic** (ordinary, track, coaster, tour). They share one
   "broken" test `vt+0xc4 = 0x1e2830` (status 4 or 5) and one Life getter `vt+0x2cc = 0x1e1d58` (`+0x94`), but
   each has its own breakdown check, wear gate and enter-4/5/6 handlers (§1.2). Breakdown is deterministic:
   reliability `+0xe4` below 10.0 (`0xa000`) → status 4; 0 → status 5. No random failure exists. (READ)
2. **A mechanic looks for broken rides on only half of its idle decisions.** `0x178dd8` flips `rand(2)`: on 0
   it takes the nearest broken ride, else the nearest upgrade job; on 1 it takes only upgrade jobs. The
   duplicated `0x153d40` call (verified in MIPS `0x178e90..0x178ecc`) is **dead**: its repair dispatch runs only
   when the first `0x153d40` returned nothing, and the second call then returns nothing too. (READ; the "dead"
   conclusion is INFERRED from 0x1539c0 only ever returning unassigned rides, for which the install dispatch
   cannot fail.)
3. **No range, no patrol confinement.** Both searches are park-wide, nearest by Manhattan cells from the
   mechanic's current cell to the ride's origin cell, first-found on ties. The patrol rectangle is never read
   by them (READ absence in `0x178dd8/0x153b80/0x1539c0/0x178cf8/0x124158`).
4. **A condemned ride (Life 0) can never be repaired.** The repair dispatch `0x178cf8` requires broken **and**
   `Life ≠ 0`; the condemned check `0x1169c0` also removes the ride from the upgrade list and forces status 4.
   A condemned ride that is nearest to a mechanic makes that decision fall through to upgrades. (READ)
5. **Repair** = walk to the cell just outside the ride's entrance door (`vt+0x17c = 0x116ec0`), ride → status 6,
   wait `T[L] + 1` ticks with `T = {240,180,120,60,60}`, ride → status 7 (reliability = 100.0, then status 2
   then 10), walk to the ride's queue mouth (`vt+0xf4 = 0x117280`). **Life is not restored.** (READ)
6. **Upgrades are a mechanic job too.** The ride panel's "Apply upgrade" (`0x1d5c00`, a ride-screen vtable slot)
   needs ≥ 1 mechanic and no mechanic strike, then adds the ride to a 15-slot list and sets `+0x128 = 1`. There is
   **no money check at request time**; the new tier's cost × 10 is debited when the mechanic finishes
   (`0x116268`), which also resets reliability, speed, capacity and duration to the new tier's defaults. (READ)
7. **Guards never look for trouble.** Their idle `0x1413e0` only patrols. They are sent by two producers only:
   a prank (`0x20d010(g,1)` → `0x14d3e0`) and a heckled entertainer (`0x12e320`). (READ)
8. **The camera rule:** the nearest non-busy guard is sent if it is within **8 cells** (dist² < 64) of the
   prankster **and a security camera is within 8 cells** (dist² ≤ 64) of the prankster, or within **5 cells**
   (dist² < 25) otherwise. Euclidean, in whole cells. (READ, MIPS `0x14d578..0x14d5e4`)
9. **The chase is one route leg.** The 3600-tick deadline set at dispatch is **overwritten with "now"** by the
   first re-route (MIPS `0x1410e0`), so a guard that reaches the prankster's old position without having shared
   its cell gives up (morale −5). Guests in a ride queue (states 0x12/0x13/0x14) are immune. (READ)
10. **Capture deletes the guest at once.** The guard sends event 4; the guest's handler `0x20f588` frees the
    guest (`0x14b368`) and posts the removal notice `0x14b9d0`, which resets every other guard chasing it. The
    guard then carries a **copy of the guest's model** to the gate. Guest state 0x22 "In the bag" and 0x1e "Chase
    visitor" are debug names with **no writer and no case in the guest dispatcher `0x2113a8`**. (READ)
11. **Ejection uses the gate turnstile the guests use.** The guard walks to the staging point (entrance table
    `+2/+3`), counts itself in (`0x153298`), waits for the coordinator's event 9 (`0x14bcc0`, record READ:
    `{0x35a548, 9}`), crosses, walks out to the corridor start (`+0/+1`), drops the copy, comes back through the
    same staging wait, then walks to the park mouth (`+0x10/+0x11`). The port's `NativeEntranceFlow` coordinator
    is this code but has no guard members yet. (READ; port status from native-incoming-controller.md item 5)
12. **The port has none of this.** No reliability field, wear, breakdown, Life, status 6/7, service flag,
    upgrade install, mechanics or guards exist; `CoasterSim` already freezes trains in 5 and `TrackRideStatus`
    lacks 4–7. §7 maps each console mechanism to the port hook it needs.

---------------------------------------------------------------------------------------------------

## 1. How a ride comes to need a mechanic

### 1.1 Ride fields used by area C (parent offsets; READ)

| off | type | meaning | writers / readers |
|---|---|---|---|
| `+0x80` | ptr | **assigned mechanic** (the mechanic's `C`) | set `0x1e1df8` (`0x178cf8`); cleared by `0x1786d0`, `0x178458`, `0x179328`, `0x1794c8`, `0x1541d0`, init `0x1e0f30`; read `0x1e1df0` (`0x153b80`, `0x1539c0`, `0x178cf8`, `0x124158`, `0x1541d0`) |
| `+0x84/+0x86/+0x88` | s16×3 | origin cell (x, y, z) | `vt+0x74 = 0x1e1fa8` returns them; `vt+0x6c = 0x1e1f00` returns them `<< 8` (fine) |
| `+0x94` | s16 | **Life** | setter `0x1e1cf0` (`vt+0x2c4`: posts advisor 0x87 when it drops from ≥ 1 to ≤ 0); getter `0x1e1d58` (`vt+0x2cc`) |
| `+0x96` | u8 | object class (`vt+0xa4 = 0x1e1d68`) | 1 coaster, 3 ordinary, 6 track, 7 tour (rides); 2 feature, 4 shop, 5 sideshow |
| `+0x98` | u8 | rotation 0..3 (`0x1e1de8`) | mechanic facing, work cell |
| `+0x9a` | u8 | **status** | set-status `vt+0x1f4 = 0x1e4d70`; broken test `vt+0xc4 = 0x1e2830`: `(u8)(status − 4) < 2` |
| `+0xa0` | struct | queue path `{s32 n; u8 x,z [32]}` (`vt+0x12c = 0x118a30` returns `this+0xa0`; add `0x1a3318`) | `vt+0xf4` reads the last cell |
| `+0xe4` | s32 20.12 | **reliability**, `0x64000` = 100.0 | wear `0x117b88`; 100.0 by `0x116120`/`0x116660`; load `0x116ba8` (byte << 12) |
| `+0x11c` | u32 | **service flag** | set 1 `0x118568`; clear `0x118678`; `0x115fa0` = flag ≠ 0; `0x116020` = flag == 0 |
| `+0x120` | s16 | riders aboard | |
| `+0x126` | u8 | **tier** | `0x116268` (+1), `0x116048`, load; getter `0x118aa8`, setter `0x118aa0` |
| `+0x128` | u32 | **upgrade pending** | 1 by `0x1d5c00`; 0 by `0x116268` and `0x116048`; read by the ride panel build `0x1d4c80` (copied to `ui+0x18e0`, `0x1d4de4`; use not traced); accessors `0x118a90/0x118a98` |

### 1.2 The four ride classes (READ: vtables dumped from the ELF; handlers decompiled)

All ride-family vtables (10 found: parent `0x35a560`, coaster `0x35b060`, ordinary `0x366330`, tour `0x369f10`,
track `0x36bbf0`, and the feature/shop/sideshow/piece vtables `0x35dc70`, `0x3683b8`, `0x368080`, `0x369aa0`,
`0x36b670`) carry the **same** `vt+0xc4 = 0x1e2830`, `vt+0x2cc = 0x1e1d58`, `vt+0x74 = 0x1e1fa8`,
`vt+0x6c = 0x1e1f00`, `vt+0x12c = 0x118a30`. The four ride classes also share `vt+0x17c = 0x116ec0` and
`vt+0xf4 = 0x117280`; the other five use the empty defaults `0x1e57b8`/`0x1097b0`. So **the task's four
"per class" slots resolve to one function each** for every object a mechanic can be sent to.

| class | pool (list getter) | vtable | update | breakdown check | wear rate `vt+0x36c` | wear applied (`0x117b88`, `tick & 3 == 0`) | enter 4 | enter 5 | enter 6 | ticks 4 / 5 / 6 |
|---|---|---|---|---|---|---|---|---|---|---|
| 3 ordinary | PoolOfRides `0x39528c` (`0x14cbe0`) | `0x366330` | `0x1b7f80` | `0x116d68`: flag if 4/5; `rel == 0 ∧ 4` → 5; **`status == 2` ∧ `rel < 0xa000`** → 4 | `0x1b80e0` = `((speedTerm + capTerm)/2)·wr` | from the script boarding pass `0x1166a8` whenever the script reports running (`0x1fa608`), in ticks 2, 4, 10, 11 | `0x1b8c28`: parent `0x1164d0` (advisor 0x36, counter 0, sound bank 0xc ev 0xe2), **queue emptied only if Life < 1** | `0x1b8bf0`: parent `0x116550` + queue emptied | `0x1e4cf8` (flag only) | `0x116910` (script cycle continues) / `0x1e5100` empty / `0x1e5108` empty |
| 6 track | PoolOfTrackRides `0x395294` (`0x14cc90`) | `0x36bbf0` | `0x200410` | `0x200358`: flag if 4/5; `rel == 0 ∧ 4` → 5; **any status** ∧ `rel < 0xa000` → 4 (every update) | `0x201f78` (adds a length term, ÷3) | `0x2023b0`: cars exist ∧ status ≠ 10 ∧ camera mode ≠ 2 | `0x2002d8`: queue emptied, no advisor | parent `0x116550` | `0x200310`: **removes every car** | unload passes |
| 1 coaster | PoolOfCoasters `0x395298` (`0x14cce8`) | `0x35b060` | `0x122a48` | `0x1228d0`: flag if 4/5; `rel == 0 ∧ 4` → 5; **status 2 or 10** ∧ `rel < 0xa000` → 4 | `0x1225f8` | status-10 tick with riders; status-4 tick always | `0x1229a0`: parent `0x1164d0` + queue emptied | parent `0x116550` | `0x1e4cf8` (trains keep running) | `0x122bb8` / `0x122b88` (trains frozen) / `0x122b90` |
| 7 tour | PoolOfTourRides `0x395290` (`0x14cc28`) | `0x369f10` | `0x1e92e8` | `0x1ea1c0`: flag if 4/5, then `0x116d68` (status 2 only) | `0x1e9e10` (same formula as ordinary) | `0x1ea000`: only when `(tick & 0x1e) == 0`, i.e. **every 32 ticks**, called every update while riders ≠ 0 | `0x1e94b8`: queue emptied, no advisor | parent `0x116550` | `0x1e94f8`: removes every tour transport | `0x1e96f0` / `0x1e9678` / `0x1e96c0` |

- Every class then runs the parent `0x1169c0`: status tick `0x1e5138`, queued-guest update, and the
  **Life/condemned check** (§1.4). (READ)
- Wear per call: `rel −= rate(0) >> 5`; `Life −= old/0xf000 − new/0xf000` (one Life point per 15.0 of
  reliability lost); clamps at 0 (`0x117b88`, READ; = coaster-operation.md §4.3). The rate formula for
  ordinary and tour rides (`0x1b80e0`, `0x1e9e10`) is exactly the coaster one, so the port's
  `NativeRideReliability.Wear` already implements it for `p = 1` (capacity); wear itself uses `p = 0`
  (riders `+0x120`, or `+0x128` of the tour object). (READ)
- "Empties the queue" is `0x117798(ride, 0)`: each queued guest is unlinked, sent `{0x35a548, 7}`, shown
  (`vt+0x2c(1)`) and re-registered (`0x14da60`). `0x117758(ride,0)` = `0x117798` + `0x118018(ride,0)`, and
  `0x118018` with 0 unloads nobody. (READ)
- **Load** `0x116ba8`: a ride saved in status 4, 5 or 6 comes back as **4** (`(status − 4) < 3`); the assigned
  mechanic, service flag and pending-upgrade flag are not in the record. (READ)
- Shops, sideshows and features have the same `vt+0xc4` but no producer of 4/5 and are outside the mechanics'
  iterator (§2.2). (READ)

### 1.3 The service flag `0x118568(ride, kind)` / `0x118678(ride)` (READ, MIPS `0x1185b0..0x118648`)

`0x118568` always sets `+0x11c = 1`. If the ride's model has a script host (`(ride+8)->+0x14 = h ≠ 0`,
controller `0x2eaad0[h]`) it also calls `0x1fa690(h, bits)`:

| kind | caller | `bits` | extra |
|---|---|---|---|
| 1 | breakdown checks (every update in 4/5), `0x1785f8` (repair arrival) | 2 | |
| 2 | `0x178a38` (install arrival) | 8 | |
| 4 | `0x1169c0` (condemned) | 4 | sound/effect bank 2 event **0x18**, non-positional (`0x111428(…,2,0x18,{0,0,0},0,1)`) |

`0x1fa690(h, bits)` → `0x1f5948(ctrl, bits)`: points `(ctrl+0x20)->+0x70->+0x68` at one of four 8-byte
entries `(ctrl+0x20)->+0x3c + {0, 8, 0x10, 0x18}` for bits 1/2/4/8 (`0x1f5638`) and starts a fade-in
(`+0x28 = 0.2`); then sets the ride **script's variable 4 to 1** (`0x1c0e28(inst, 4, 1)`, instance found by
`0x1c0de8(ctrl+0x34)`). `0x118678` clears the flag and, if the host exists, `0x1fa700`: fade-out
(`+0x28 = −0.3`) and script variable 4 := 0. (READ; that the four entries are the frames of the floating status
icon above the ride is INFERRED, from staff-what's "raises the ride's icon" and the fade pair.)

Variable 4 in every ride script is **`VAR_BREAKSTAT`** ("Common variable set - All ride scripts must have
these", `Rides_Wateride_Wateride.rss` lines 7–18). The scripts read it: GoKarts `.breaktest`: `TEST
VAR_BREAKSTAT … BUMP BUMP_SETBROKEN; COPY VAR_BROKEN 1 ; Call repair man; ADDOBJ … P_EFFECT_Smoke2` on nodes 3
and 4; `.fixed: … COPY VAR_BROKEN 0 ; Fixed ! … REPAIREFFECT 1; WAIT 3000; REPAIREFFECT 0`. So the smoke and the
repair effect are the script's response to the native flag; `VAR_BROKEN` is script-owned. (READ script text;
the PS2 may stub `BUMP_SETBROKEN`, not checked.) Kind-2 (upgrade) and kind-4 (condemned) also raise
`VAR_BREAKSTAT`, so an upgrading ride smokes like a broken one (INFERRED from the shared write).

### 1.4 Life and condemned rides (READ `0x1169c0`, `0x1e1cf0`)

```
if (Life(vt+0x2cc) == 0 && flag == 0) {
    0x118568(ride, 4);                 // flag := 1, condemned icon, VAR_BREAKSTAT := 1, event 0x18
    if (flag != 0) 0x153d70(ride);     // always true now: remove from the UPGRADE list
    if (!broken(vt+0xc4)) set 4;
}
```
Life starts at the tier-0 record `+0x34` (`0x116048`), is never raised by repair or upgrade, and is lost at one
point per 15.0 of worn reliability. A condemned ride: stays in 4 (flag stays 1, nobody clears it), is never
dispatched for repair (§2.3), leaves the upgrade list, and its setter posted advisor **0x87**
`RIDE_CONDEMNED` ("…too old and has been condemned. You should delete it…"). (READ; "stays in 4 forever" is
INFERRED from the absence of any other clearer of `+0x11c` than `0x1786d0`.)

### 1.5 The breakdown advisors (READ)

- **Enter 4, parent `0x1164d0`** (ordinary, coaster): message **0x36** `RIDES_BREAKDOWN_IMMINENT` "Your ride is
  about to break down!" with the ride attached, counter 0 (`0x1073c0(adv, 0, 1)`), sound bank 0xc event 0xe2.
  Track and tour enter-4 handlers do not call it.
- **Enter 5, parent `0x116550`** (all four classes): message chosen by **`0x103658`** (its only caller,
  `0x11656c`), counter 1, effect bank 2 event **0x70** at the ride position.

  `0x103658`:
  | condition | id | text |
  |---|---|---|
  | no mechanics (`0x14d6b0() == 0`) | 0x37 `BREAKDOWN_NO_MECHANICS` | "A ride has broken down, and you don't have any mechanics to fix it! You should hire some." |
  | some mechanic is **available** (predicate §2.5) | 0x39 `BREAKDOWN_MECHANIC_ON_IT` | "A ride has broken down, and a mechanic is on his way to fix it." |
  | otherwise | 0x38 `BREAKDOWN_BUSY_MECHANICS` | "…all your mechanics are busy. Maybe you should hire some more?" |

  So the "breakdown" messages fire only at reliability **0** (status 5), and "on his way" is claimed when an
  idle mechanic merely exists; nothing is dispatched by this code. (READ mechanism; the gap between message
  and behaviour is INFERRED.)
- Counter `0x1073c0(adv, i, d)` = `0x10ddd8(adv[0x264], i, d)`: s16 slot `i` (0..21) at `+0x9e + 2i`, clamped
  ±30000 (READ, MIPS `0x1073c0`). Slots seen here: 0 (enter 4), 1 (enter 5), 2 (heckle and prank), 0x14 (prank),
  0x15 (litter dropped for want of a bin); booth-admission.md adds 19. The consumer is not traced.

---------------------------------------------------------------------------------------------------

## 2. The mechanic: finding a job

### 2.1 Find work `0x178dd8` (state 0; READ, MIPS `0x178dd8..0x178ef4`)

```
if (vt+0x1bc(C)) return;                          // tired (≥81 → state 0x31) or type on strike (→ 0x1a); 0x1dba90
tired = min(100, tired + 6);  morale = min(100, morale + 1);      // every attempt that gets this far
if (rand(2) == 0) {                                // rand(n) = RNG 0x144870 % n, 0..n-1 (0x1448e0)
    r = 0x153b80(me);                              // nearest broken ride, unassigned or mine
    if (r && 0x178cf8(me, r, REPAIR)) return;
    r = 0x153d40(me);                              // nearest UNASSIGNED ride in the upgrade list
    if (!r) goto patrol;   if (0x178cf8(me, r, INSTALL)) return;  goto patrol;
} else {
    r = 0x153d40(me);
    if (r && 0x178cf8(me, r, INSTALL)) return;
    r = 0x153d40(me);                              // same call again (the "duplicated branch")
    if (!r) goto patrol;   if (0x178cf8(me, r, REPAIR)) return;
}
patrol: depth = 0; state = 0xd;
```
The second `0x153d40` is reached only when the first returned 0 (an unassigned ride always passes the install
dispatch), and nothing between the two calls changes the list, so it returns 0 again: **branch 1 is "upgrades
only"**. (READ code; dead-branch conclusion INFERRED.) The +6 tiredness per attempt means an idle mechanic
reaches the rest threshold 81 after about 14 fruitless searches from 0 (INFERRED arithmetic; rest is area A).

### 2.2 The two searches (READ)

- **Broken rides `0x153b80(mech)`**: iterator `0x1e5b20(it, 1)` visits ordinary → track → coaster → tour rides
  and stops before shops/sideshows/features (`0x1e5b58` starts at list `0x14cbe0`; `0x1e5ba8` chains 3 → 6 → 1
  → 7 and ends at 7 when `it[1] ≠ 0`). Keeps rides with `vt+0xc4` (status 4/5) whose `+0x80` is 0 or me.
  Score `|Δx| + |Δz|` between `vt+0x74` cells of mechanic and ride origin; `best` starts at `0xffffffff`,
  replaced only by a strictly smaller score (first-found wins ties). No range limit. There is **no list of
  broken rides**: every idle decision scans all rides. Iteration order within a pool is active-list order
  (newest allocation first, INFERRED from the pool pattern).
- **Upgrade jobs `0x153d40` → `0x1539c0(list 0x3953e8, n = [0x2b739c], mech)`**: over the list entries with
  `+0x80 == 0` (any assigned mechanic, even me, excludes), nearest by the same Manhattan score, the first entry
  taken unconditionally, later ones only if strictly nearer.

### 2.3 Dispatch `0x178cf8(mech, ride, repair)` (READ)

```
a = ride+0x80 (as P: a − 8); if (a && a != mech) return 0;
if (repair && (!vt+0xc4(ride) || vt+0x2cc(ride) == 0)) return 0;   // must be broken AND Life != 0
ride+0x80 = mech+8;  mech target = ride;  depth = 0;  state = repair ? 0x38 : 0x39;  return 1;
```
It does not free the current route; states 0x38/0x39 do. Install has no precondition on the ride.

### 2.4 The upgrade list `0x3953e8` (u32[15], count `0x2b739c`) (READ)

| op | fn | behaviour | callers |
|---|---|---|---|
| add | `0x153d10` → `0x153950(list, &n, 15, ride)` | returns 1 if already present; **returns 0 without adding when n ≥ 15** | `0x124270` only (reached from `0x1d5c00`) |
| nearest unassigned | `0x153d40` → `0x1539c0` | §2.2 | `0x178dd8` ×3 |
| remove | `0x153d70` → `0x153b00` | shifts the tail down | `0x1169c0` (condemned), `0x14b9d0` (object removed), `0x178b10` (install done) |
| clear | `0x1542a0` | n = 0 | `0x124300` when the last mechanic is fired |
| complete & clear | `0x1541d0` | for each entry whose assigned mechanic is in state 0x10/0x34/0x36 or walking (2/3) with mode 0x14 (`0x179508`): status 7, unassign, **`0x116268(ride, 1)`** (silent upgrade, still debited); then n = 0 | park teardown `0x150e80` only |
| reset | park init `0x151498` | n = 0 | |

The list is not saved (INFERRED: nothing in the ride record or the save writer found holds it; `+0x128` is
not in the load record `0x116ba8`), so a pending, not-yet-started upgrade is lost on reload.

### 2.5 Call Mechanic and the "available" predicate (READ, MIPS `0x124158..0x124248`)

List box row 179 "Call Mechanic" → `0x124250` → `0x124158(1)`:
```
ride = selection (0x1497b0() = 0x395090, +0x88);  if (!ride || ride+0x80) return;
for (m = mechanics head (0x14d650); m; m = m->next)
    if (available(m)) { 0x178cf8(m, ride, 1); return; }   // result ignored
available(m) = target(P+0x28) == 0
            && !(state == 0x1a || state == 0xf)
            && !(state ∈ {2,3} && mode ∈ {5, 0x11})
            && mode != 0x32
```
- **First** available mechanic in list order, no distance test; if the ride is not broken or is condemned the
  dispatch fails silently and nobody else is tried.
- **`mode != 0x32` is a slip for `state != 0x32`**: the MIPS compares the mode byte (`lbu v1, 0x2e(a1)` in
  the delay slot at `0x1241e4`, `bne v1, a3=0x32` at `0x1241fc`), and no mode 0x32 exists. So a mechanic
  **resting in a staff room (state 0x32, model hidden) counts as available** and can be called out; nothing
  shows its model until `0x1786d0` finishes the job. (READ compare; intent and visible consequence INFERRED.)
- The same predicate is inlined in `0x103658` (advisor) and `0x178458` (hand-off).

### 2.6 Route failure hand-off `0x178458` (mechanic `vt+0x16c`; READ)

| event | mode | action |
|---|---|---|
| 1 route found | any | depth 0, state 3 |
| 2 route failed | 6 or 0x14 | unassign the ride; walk the mechanic list **from my own `next` pointer** (`*P`), first available (§2.5) → `0x178cf8(it, ride, mode == 6)`; me: depth 0, state 0, target 0 |
| 2 | 0x16 | state 0, target 0, depth 0 |
| 2 | other | base `0x1db768` (mode 5 → 0, mode 1 → 5, else 0xd) |
| 3 | | ignored |

So a failed route passes the job only to mechanics **after** me in list order. (READ)

### 2.7 Other exits (READ)

- **Ride demolished**: `0x14a7c0` → `0x14b9d0(ride)` calls `vt+0x19c = 0x1928f8` on every guest and mechanic:
  if my target is that object → target 0, route freed, depth 0, state 0, model shown; then removes the ride
  from the upgrade list.
- **Route-system reset** `0x179328`: states 0xe/0x10/0x11/0x34/0x36 keep working; walking (2/3/0xb) with mode
  0x14 or 6 re-dispatch (`0x178cf8(me, ride, mode == 6)`), mode 0x16 → state 0x3a; everything else unassigns
  and freezes (`0x1dc780` → `0x1928b0`).
- **Fired** (`0x124300`): `vt+0x1f4` pays pro-rata (area D), `0x14b608` → `vt+0x194 = 0x1794c8`: unassign the
  ride, target 0, person release. **The ride keeps whatever status it had**: a ride fired on in status 6 stays 6
  for ordinary, coaster and tour rides (their breakdown checks only move 2/10 → 4, and `0x153b80` looks only at
  4/5), while a track ride below 10.0 is forced back to 4 and is found again; reload turns 6 into 4. (READ paths;
  the "stuck in 6" consequence INFERRED.)
- **Strike / tiredness** are checked only in state 0, so a mechanic always finishes a job it has started. (READ)

---------------------------------------------------------------------------------------------------

## 3. The mechanic: travel and work

### 3.1 Where he goes (READ)

- **Work cell `vt+0x17c = 0x116ec0`**: `o = origin cell (vt+0x74) + 0x1e1760(ride)`, where `0x1e1760` rotates the
  record's entry offset `(s16 +0xc, s16 +0xe)` by the ride rotation within the record footprint (`0x1e2288`), or
  uses `(−1,−1)` (`0x369a30`) when the offset is negative. Then one step along
  `d = (rotation + record+0x14) & 3`: **0 → z−1, 1 → x−1, 2 → z+1, 3 → x+1** (MIPS `0x116fb8..0x117010`). This is
  the cell just outside the entrance door, the queue's first spot in the port's `native-ride-queue.md`.
- **Leave cell `vt+0xf4 = 0x117280`**: bytes at `+0xa0 + 2 + 2n` = the **last** queue cell `(x, z)`, absolute
  (native-ride-queue.md: "the queue MOUTH"). With `n = 0` it reads the zero high half of the count, i.e. cell
  (0,0) (INFERRED edge).
- Routes: target = cell·256 + 0x80 (cell centre), request `0x18da78(C, x, z, tx, tz, 0x11, 0)`.

### 3.2 State table (READ; handlers in `mech.c`, dispatcher `0x1791a8`)

| state (debug name) | handler | does | exit |
|---|---|---|---|
| 0 Idle | `0x178dd8` | §2.1 | 0x38, 0x39, 0xd, 0x31, 0x1a |
| 0x38 | `0x178ef8` | free route; request route to the work cell; on acceptance `+0x24 = now`, mode 6, push 0x38, state 0xb | acceptance → 0xb (else retried every tick) |
| 0x39 | `0x178fe0` | same, mode 0x14 | → 0xb |
| 0xb → 3 ↔ 2 | person machine `0x1920d0` | wait for the route (event 1 → 3), walk (`0x191e98`, state 3 every tick), segment end (state 2, `vt+0x144 = 0x178be8`, once per waypoint) | arrival (route slot −1): mode 6 → **0x10**, mode 0x14 → **0x34** (both clear `+0x24`), mode 0x16 → target 0, state 0; route failed → §2.6 |
| 0x10 "Closing ride" | `0x1785f8` | flag clear → chatter, `0x118568(ride,1)`; flag set → **ride status 6**, `+0x24 = now + T[L]`, state 0xe | 0xe |
| 0xe "Repairing" | `0x178880` | logical 16; repair noise bank 2 **0x6f** with handle `P+0x60` (restarted whenever `0x111cc8` reports it idle); face the ride: rotation 0/1/2/3 → 0, π/2, π, 3π/2 (`0x1787d0`) | `+0x24 ≠ 0 ∧ +0x24 < now` (MIPS `0x1789f4..0x178a10`) → 0x11, `0x153d08(ride)` (returns 0) |
| 0x34 | `0x178a38` | flag clear → `0x118568(ride,2)` + chatter; flag set → **status 6**, deadline, state 0x36 | 0x36 |
| 0x36 | `0x178b10` | logical 16, chatter every tick, face ride; deadline passed → **`0x116268(ride, 0)`**, state 0x11, remove from upgrade list | 0x11 |
| 0x11 "Opening ride" | `0x1786d0` | pass A (flag set): **ride status 7**, unassign, chatter, clear flag (`0x118678`); pass B (flag clear): depth 0, logical 13, state 0x3a, show model, morale +10 | 0x3a |
| 0x3a | `0x1790c8` | request route to the leave cell, mode 0x16, push, state 0xb (retried every tick) | arrival → target 0, state 0 |

`T[L]` = u16 `0x3627c8 + 4L` = **240, 180, 120, 60, 60** ticks; walk speed u16 `0x3627ca + 4L` = **9, 12, 14,
16, 18** (`vt+0x18c = 0x179308`) (READ, table dumped). Chatter `0x1781b8`: on every tick with
`tick % [0x2bed10] ≠ 0`, `[0x2bed10] = 63` (only reader `0x1781d4`), plays bank 8 0xa2 (handle `P+0x58`) or 0xa3
(`P+0x5c`) by `rand & 1`; that a handle-based play does not restart a playing sound is INFERRED.

If the ride already has its flag set when he arrives (every broken ride does: its breakdown check sets it each
update), status 6 is set on the first 0x10 tick. A working ride (install) takes one extra tick.

### 3.3 Timing (worked from READ constants)

| level | walk, ticks per cell | status-6 duration | repair, s at 40 ms |
|---|---|---|---|
| 0 | 28.4 | 241 | 9.6 |
| 1 | 21.3 | 181 | 7.2 |
| 2 | 18.3 | 121 | 4.8 |
| 3 | 16.0 | 61 | 2.4 |
| 4 | 14.2 | 61 | 2.4 |

(status 6 is set at tick t0; the handler first runs at t0+1 and completes when `t0 + T < now`.)

### 3.4 What repair and installation restore (READ)

- **Status 7** (enter 7 is the parent `0x116660` for all four classes): set status **2** (`0x1e4d08`),
  reliability := `0x64000` (100.0), set status **10**. Life, tier, settings unchanged.
- **Status 6** on entry: track rides remove every car at once (riders unloaded at the exit with scoring), tour
  rides remove every transport, coasters and ordinary rides only raise `+0x90`. Guests pick only 2/10/11
  (`0x1e1e48`), so nobody joins during 6. A track ride below 10.0 is re-forced into **4** every update during
  the repair (`0x200358`), re-emptying the queue; the repair still completes.
- **Upgrade `0x116268(ride, silent)`** (MIPS head checked by coaster-operation.md §7): `+0x128 = 0`; if tier < 3:
  tier += 1; `0x116120`: reliability := 100.0, run timer `+0x122` := 0, **capacity := max(1, MaxCap >> 1)**,
  **speed := SpeedMin + (SpeedMax − SpeedMin) >> 1**, **duration := max(1, DurMax >> 1)**, all of the *new*
  tier (getters `0x117b28 +0x30`, `0x117888 +0x38`, `0x1178e8 +0x3c`, `0x1179a8 +0x44`, record + tier·0x34);
  debit **cost[new tier] (record + tier·0x34 + 0x50) × 10** via `0x100698`; unless silent, effect bank 2 **0xb8**
  (0xe1 when the new tier is 3) at the ride position. The panel presumably stops at tier 2 (not traced; the
  bound admits 3). Then state 0x11 sets status 7, so reliability is 100.0 twice over.
- **Installing on a broken ride repairs it** (install has no broken test; arrival finds the flag already set,
  and 0x116268 + status 7 restore 100.0). (INFERRED from the READ paths.)

### 3.5 Morale and tiredness ledger (READ; clamps 0..100)

| event | tiredness | morale | where |
|---|---|---|---|
| each find-work attempt | +6 | +1 | `0x178e08..0x178e3c` |
| each waypoint on a repair walk (mode 6 only) | −2 | −2 | `0x178cb4..0x178cd8` |
| each waypoint, any walk, when `tick & 3 == serial & 3` | +1 | | base `0x1db970` |
| job finished (0x11 pass B) | | +10 | `0x1786d0` |
| rest / strike / training | | | area A/D |

State 2 runs once per route segment: `0x191e98` sets state 2 at a segment end and `0x191d78` pops the next
segment and sets state 3 (READ), which settles staff-what's open "cadence" question for these handlers.

---------------------------------------------------------------------------------------------------

## 4. Requesting an upgrade: `0x1d5c00` and `0x124270` (READ, MIPS `0x1d5c00..0x1d5dc0`)

`0x1d5c00` is not called directly: it is a slot of the ride info screen's vtable (pointer at `0x36892c`, next to
`0x1d4c80` build, `0x1d4fd0` apply, `0x1d5210` draw). String `"Apply upgrade %d"` (`0x368850`).

```
sel = list(ui+0xf40).selected if its slot (ui+0x11ac+4·sel) is filled else -1;
ui+0x18d0 = ui+0x18cc + sel;                       // printed as "Apply upgrade %d"
if (!0x15c368(ui+0xf40)) return;                   // the list's accept
if (sel > 0) return;                               // only item 0 (or an empty list) acts (bgtz 0x1d5cb8)
if (mechanic count == 0)      { 0x1073f0(adv,0x7c); message 0xcf; sound 0xaf; return; }
if (0x16c988(cal, 2))         { 0x1073f0(adv,0x7d); message 0x7d; sound 0xaf; return; }   // mechanics on strike
ui->vt+0x7c(ui, 1);  0x124270();  selectedRide+0x128 = 1;
```
- `0x124270` = `0x153d10(selection+0x88)`: add to the list (§2.4); **a 16th request is dropped while `+0x128`
  is still set to 1**. (READ)
- No money, tier or Life test here; the cost is debited at completion and may overdraw (INFERRED).
- Messages 0x7c `ADD_UPGRADE_HIRE_MECHANIC`, 0x7d `…STRIKING_MECHANICS`, 0xcf `UPGRADE_NO_MECHANICS` all have the
  blank text row 310, and `0x1073f0` skips blank rows (`0x2a6acc[id·0x38] != 0x136`), so the player hears only
  the error sound 0xaf (READ).

---------------------------------------------------------------------------------------------------

## 5. Guards

### 5.1 Idle `0x1413e0` (READ)

1/16 sound bank 8 0xa7 (`P+0x60`); speed := 15 (`P+0x50 & ~0xf8 | 0x78`); if `vt+0x1bc` returns 0: sound 0xa6
(`P+0x64`), depth 0, state 0xd (patrol, area A). No search for crime.

### 5.2 Who sends a guard (READ)

| producer | trigger (guest decision `0x20c930`, state 0) | guard choice |
|---|---|---|
| **prank** | arm 5: `rand(6) == 5`, happiness `N+0x75 < [0x2eeb98] = 25` (slt), `rand(1000) < [0x2eeb68] = 10` (sltu) → `0x20d010(g, 1)` | `0x14d3e0(g)`: nearest non-busy guard by cell dist², accepted if **dist² < 64 and (camera within dist² ≤ 64 of the guest, or dist² < 25)** |
| **heckle** | arm 2: `rand(6) == 2`, `rand(1000) < [0x2eeb64] = 10`, nearest entertainer by Manhattan cells with distance **< 5** (sltiu 5 at `0x20cde4`) → guest logical 4, counter 2, `0x12e278(ent, g)` | the Shocked entertainer (state 0x20) waits `rand(5)·60` ticks (0–240), losing 10 morale per tick, then `0x12e320` sends the nearest non-busy guard with **Manhattan ≤ 6 from the entertainer**, else morale −5 |

- Rates per state-0 decision call: prank 1/6 × 1/100 = **1/600** for guests with happiness < 25; heckle 1/600
  for any guest near an entertainer (INFERRED arithmetic). `[0x2eeb68]`, `[0x2eeb98]` have one reader each
  (the decision); `[0x2eeb64]` is also written by `0x12c348` (not followed; the ELF value is 10).
- **Busy** `0x1416f0` (MIPS `0x1416f0..0x1417c8`): state ∈ {0x21, 0x3c, 0x3d, 0x27, 0xf, 0x2e, 0x2f}, or state
  ∈ {2, 3} with mode ∈ {8, 9, 5, 0xe, 0xf, 0x10, 0x30}. Not busy, hence dispatchable: resting in a staff room
  (0x32, model hidden; `0x1417d0` does not show it), setting off to rest (0x31) or walking there (mode 0x11),
  waiting for a chase route (0xb, mode 8), on the way back into the park (0x37, walking with mode 0x15). Mode
  0x30 exists nowhere (INFERRED slip).
- **Camera test `0x14d238(pos, 0x40)`**: over the features list (`0x14cd30`, PoolOfFeatures), objects with
  status `+0x9a ≠ 0` and DBA `+0x2e` bit 3 (`0x130858`), `dx² + dz² ≤ 64` in cells (`sltu` at `0x14d3a4`). Its
  only caller is `0x14d3e0`; the test uses the **guest's** position, so it is the same for every candidate.
- `0x14d3e0` returns whether a guard was sent; `0x20d010` ignores it.

### 5.3 Chase (READ, MIPS `0x140e08..0x141118`, `0x140b48..0x140cd4`)

`0x1417d0(guard, guest)`: free route; `+0x24 = now + 0xe10` (3600); target = `guest+8`; depth 0; state **0x21**;
speed := **30** (`| 0xf0`); sound bank 8 **0x88** (handle `P+0x68`, no clips).

State 0x21 `0x140e08`, every tick:
```
g = target − 8;
if (g < 2 /*null or the leg flag*/ || g.state ∈ {0x12,0x13,0x14} || deadline < now) give_up(−5);
else if (cell(me) != cell(g)) {                    // vt+0x74, x and z
    mode = 8;
    if (route(me.pos → g.fine pos (N+0x24/0x26), flags 0x11)) { +0x24 = now; push 0x21; state = 0xb; }
} else catch(+10 morale, +3 tiredness);
give_up(d): state 0, depth 0, target 0, morale −d.
```
- **`+0x24 = now` on the re-route (store at `0x1410e0`) overwrites the deadline.** Event 1 then resets the
  depth (the pushed 0x21 is discarded), the guard walks (state 3 = `0x140b48`), and on arrival
  (`0x140cd8`, mode 8) state := 0x21; the next 0x21 tick sees `deadline < now` and gives up. So the pursuit is
  **one route leg to where the guest was**; the 3600-tick window only matters if the very first tick finds the
  same cell. (READ; the reading that the overwrite is unintended is INFERRED.)
- Walk step `0x140b48` (mode 8): `0x191e98` step, then the same queue test (give up, morale **−2**) or the
  same-cell catch (no bonuses); otherwise reposition the carried copy (`0x1409e8`, a no-op without one).
- Guests queueing (0x12 "Waiting in queue", 0x13 "Shuffle forward", 0x14 "Shuffle queue", debug names
  `0x10ca98`) are safe; a guest on a ride is not excluded (edge not followed).
- Guard at 30 vs guests at `15 + rand(15)` (native-guest-motion.md): the guard is at least as fast as the
  fastest guest.

### 5.4 Capture (READ)

`catch`: logical 16 on the guard, `0x140880(guard, g)`, event **`{0x35a548, 4}`** to the guest's `vt+0x16c`
(`0x20f588`), depth 0, state **0x3c**.

- `0x140880`: destroys any previous copy, makes a model instance (`0x230a98`: 0x4c bytes, `0x227158`), loads the
  **guest's model id** (`guest vt+0x14 → +4`), plays logical **17** on it (`vt+0x5c(1.0, copy, 0x11, 0, 0, 2)`),
  registers it for drawing (`0x17ce10`), positions it (`0x1409e8`), sets the guard's logical 16 and
  `C+0x2c |= 0x200`.
- `0x1409e8` (called every walk tick, at each waypoint and each 0x3c/0x27 tick): copy at the guard's fine
  (x, 0, z), rotated to facing + 3π (i.e. facing + π); sound bank 8 **0x89** at the guard (handle `P+0x5c`,
  text blank), debug print " EVT_GUARD".
- **Guest side `0x20f588` case 4**: `0x14b368(N)` (release `vt+0x194`, unregister, back to the free list) and
  `0x14b9d0(N+8)`. For class 0xb (guests, `0x211c78`) the notice calls `vt+0x19c = 0x1928f8` on **every guard**:
  a guard whose target is that guest is reset (target 0, route freed, depth 0, state 0, shown). This includes
  the catching guard, whose route slot is freed here and whose state is then overwritten with 0x3c. The guest
  is gone for good; its stats, cash and happiness leave with it.
- **"In the bag" (0x22)** and **"Chase visitor" (0x1e)**: no `li`+`sb` writer (scan of the whole listing for
  `addiu r,0x22/0x1e` followed by `sb r,0x37/0x2f` within 10 instructions; the same scan finds the known 0x21,
  0x2e, 0x3c writers), no case in the guest dispatcher `0x2113a8`, and the four event-6 senders (which set the
  state from their payload) pass 0x2b (`0x152a18`) or 0x13 (`0x117da0`, `0x118168`, `0x1184d4`, found through
  the record vtable `0x35a530`). Unused on PS2. (READ null with that scan's limits.)
- `C+0x2c` bit 0x200 is **not "carrying"**: the person draw consumes it as a one-shot "play the requested
  animation with mode 2" and clears it (`0x1922f8..0x192348`). (READ)

### 5.5 Carrying out and coming back (READ; entrance table `0x2b71b0 + world·0x36 + park·0x12`)

| state | handler | does | exit |
|---|---|---|---|
| 0x3c | `0x141120` | reposition copy; read the guard's own animation (`0x10ec48`: logical `+0x4c`, phase `+0x4e`) | logical 16 at phase 1 → **0x27** |
| 0x27 "Chucking out" | `0x1411c0` | `0x14e288()` (always 1) → free route; target = **staging point** `0x1532d8`: entry `+2/+3` cell centre, x + `rand(256)` fine; route flags 0x21; mode 0xe | accepted → 0xb (else retried) |
| (walk) | | copy follows | arrival mode 0xe: leg flag (s16 `P+0x28`) = 1, state **0x2e**, **`0x153298` (P++)**, facing π |
| 0x2e | none | waits | coordinator event 9 → **0x2f** |
| 0x2f | `0x141900` | new direct route slot (`0x192768`); straight segment from (current x) to (current x, staging z); mode 0x10; state 3 | arrival: **`0x1532b0` (P−−, R++)**; leg 1 → **0x30**, leg 0 → **0x37** |
| 0x30 | `0x1419c0` | route flags 1 to the **exit point** `0x14e290(rand(1) = 0)`: entry `+0/+1` cell centre; mode 9 | arrival: target 0, state **0x3b**, `0x140990` destroys the copy, logical 13 |
| 0x3b | `0x141a70` | route flags 1 back to the staging point; mode 0xf | arrival: leg 0, state 0x2e, P++, facing 0 |
| 0x37 | `0x141298` | up to **5** requests (flags 0x80) to the **park mouth** `0x153380`: entry `+0x10/+0x11` cell centre, clamped to the map, no random offset (MIPS `0x153380..0x15340c`); mode 0x15 | arrival → **0** (speed back to 15 in state 0) |
| 0x3d | `0x141290` (empty) | — | no writer found (same scan) |

JUNGLE park 0 entry, bytes: `1a 06 | 1d 07 | 1d 0f | 1b 12 | 06 00 04 00 03 00 00 00 | 1d 13`: exit point (26,6)
= the corridor start (`ParkEntranceEntry.XStart/ZRow`), staging (29,7), park mouth (29,19) (`XCol/ZEnd`).
So the guard carries the copy out along the whole walkway, through the turnstile, to the outer end, and walks
back in the same way.

**Coordinator `0x14bcc0`** (called first in every object pass `0x14be60`; record `{0x35a548, 9}` READ in this
decompile, which settles staff-what's INFERRED): if `P ≠ 0 ∧ E == 0`: `R = 0, E = 1`; if `R < 11 ∧ P ≠ 0 ∧
E == 1`: event 9 to every member of **both** the guest list (`0x14d218`) and the guard list (`0x14d228`) whose
state is 0x2e; if `P == 0` or (`R ≥ 11` and bus step `[0x395280] == 2`): E 1 → 0. The bus writes E = 2 in its
step 2 and 0 otherwise (`0x14be60`). Guards therefore wait at the gate while the bus holds E = 2, and cross
in the same batches as guests. (READ; = native-entrance-lifecycle.md, whose "second family 140D64/80" is the
guard arrival handler `0x140cd8`, P++ at `0x140d70/0x140d90`, P−−/R++ at `0x140dc4`.)

### 5.6 Guard events `0x1406d8` (READ)

| event | mode | action |
|---|---|---|
| 1 | any | state 3, clear `C+0x2c & 0x20` |
| 2 | 9 (exit) | if `0x20` clear: re-route to the exit point with flags **0x23**, set `0x20`, state 0xb; else (or refused) state 0 |
| 2 | 0x15 | state 0x37 (retry) |
| 2 | 8 | depth 0, target 0, state 0, then base `0x1db768` → **0xd** |
| 2 | other (0xe, 0xf, 0x10 …) | base → 0xd |
| 9 | | state 0x2f |

A guard whose exit route fails twice, or whose gate legs fail, returns to patrol **with the copy still
attached** (it is destroyed only by `0x140990`, the next `0x140880`, or the destructor `0x140588`), and the
staging count P it added is never taken back. (INFERRED from the READ paths.)

### 5.7 Guard ledger (READ)

| event | tiredness | morale |
|---|---|---|
| catch in state 0x21 | +3 | +10 |
| catch mid-walk | — | — |
| give up in 0x21 (lost, timed out, queueing) | | −5 |
| give up mid-walk (guest queueing) | | −2 |
| walking, phase-matched waypoint | +1 | |

Speeds (base `vt+0x18c = 0x1dc928`, `C+0x48 >> 3`): 15 idle (17.1 ticks/cell), **30 from dispatch until the
next state 0** (8.5 ticks/cell), i.e. through the whole ejection. Guards have no level-dependent table; level
affects only wage and the advisor (staff-what §1.2).

### 5.8 Cameras and the Security Award (READ)

- Cameras are features (PoolOfFeatures) with DBA bit 3; their only gameplay reader is `0x14d238` (§5.2).
- Weekly `0x16bc70`: if award 0 is not yet held (`0x16b8e8(p, 0)`) and **`0x104ce0(0x40) > 80`**, message 0xa0
  `GOLD_TICKET_BROTHER` (Security Award + gold ticket), award 0 set.
- `0x104ce0(mask)`: a bitmap with one bit per **16×16-cell block**, `ceil(W/128)` bytes per row and `ceil(H/16)`
  rows (`W,H` = `[0x3952f0]`,`[0x3952f4]`); every feature with status ≠ 0 matching the mask (0x40 = camera)
  sets its block's bit; result = `100 × popcount / bytes` (`divu` at `0x104f04`). The divisor counts **bytes, not
  blocks**, so the figure is 8× the block coverage: > 80 is reached with cameras in 7 blocks of a 128×128 map
  (INFERRED arithmetic from the READ formula).
- Advisor 0xc `GUARD_BUILD_CAMERA` ("If you built some security cameras it would help your guards…"): no
  constant raiser through `0x107ca8`/`0x1073f0` (scan `out/py/advscan.py`); the advisor rule table is area D.

---------------------------------------------------------------------------------------------------

## 6. The guest side (READ)

- **Prank `0x20d010(g, 1)`**: allocate litter `0x14ad08` (PoolOfLitter, 40; **returns null in test-park mode**
  `0x153410`), place it at the guest's fine position ± `rand(200) − 100` fine (`0x15e2b8`), spawn particle
  template **50** at the guest's cell (`0x1822d0`, table `0x3ae140`, ≤ 10 cells, one per cell; the handyman's
  `0x1824a8` is its remover, area B); counters 2 and 0x14; `0x14d3e0(g)`. The guest's own state is not changed:
  it carries on deciding. Advisor 0x64 `ADD_PRANK_LITTER` ("A prankster is littering your park…") has no
  constant raiser (scan above); 0x63 `ADD_PRANK_SBOMB` (stink bomb) likewise.
- **Bin `0x20d010(g, 0)`** (arm 3, litter need `N+0x74 > 89`): nearest bin (DBA bit 2, `0x1307e8`) by Manhattan;
  none or > 5 cells → drop litter at the guest (`0x15e2b8` + `0x15e5f0`), `N+0x74 = 0`, counter 0x15; else walk
  (flags 1,1; mode 0x13) to the bin's fine origin offset by rotation 0/4: z−0x80, 1: x−0x80, 2: z+0x180, 3:
  **x−0x180** (jump table `0x36c980`; the rotation-3 sign looks like a slip, INFERRED); arrival mode 0x13
  (`0x20d628`): `N+0x74 = 0`, state 0. Area B owns bins; recorded here because it is the same function.
- **Event 4** (caught): §5.4. Event 7 (queue emptied by a breakdown): state 0x3a (walk out), port
  `NativeRideQueues`.
- Guest constants: `[0x2eeb64] = 10`, `[0x2eeb68] = 10`, `[0x2eeb98] = 25` (ELF values).

---------------------------------------------------------------------------------------------------

## 7. Port mapping (what exists, what each console mechanism needs)

| console mechanism | port hook today | status |
|---|---|---|
| reliability `+0xe4` (20.12), wear `0x117b88` per class (§1.2) | none; `NativeRideReliability.Wear` (NativeRideReliability.cs) is the `p = 1` rate for the panel | **missing**: a `Reliability` field per ride, the `p = 0` (riders) form, the per-class wear gates |
| panel rows "State of Repair" (`0x118228` = rel >> 12) and Life (`0x118238`) | Viewer.cs:7477 row 644 shows `ride.Condition`, which is the **lavatory** condition and never changes on a ride | **wrong source**: should read rel >> 12; Life row blank |
| Life `+0x94`, condemned `0x1169c0`, advisor 0x87 | none | missing |
| breakdown checks `0x1228d0` / `0x200358` / `0x116d68` / `0x1ea1c0` | `CoasterSim.Status` has 4 and 5 in its comment and freezes trains in 5 (CoasterSim.cs:88, 118), boarding keeps 4 (:484); `TrackRideStatus` has only 2, 3, 10, 11 (TrackRideSim.cs:4), and the class says "Not here yet: wear, breakdown and repair" (:63) | **no producer of 4/5/6/7** anywhere |
| enter 4/5/6 side effects (queue event 7, car/transport removal, advisors 0x36 and 0x37–0x39, effect 0x70) | `NativeRideQueues` empties a queue with event 7 when `Broken(ride)` (NativeRideQueue.cs:188, 295), where `Broken` = script `VAR_BROKEN` or a fault | exists as a consumer; needs the native status (4, 5, and ordinary-only-if-Life<1) as its input for native rides |
| service flag `+0x11c` → script `VAR_BREAKSTAT` (var 4) | `ParkSim.RideVariables` (ParkSim.cs:345) omits `VAR_BREAKSTAT` and `VAR_WORN`, so `ParkRide.Set("VAR_BREAKSTAT", …)` would be a no-op (`Has` false) | **add `VAR_BREAKSTAT`** so scripted rides smoke and play their repair effect |
| guests avoid broken rides (2/10/11 only) | `ParkRide.DestinationState` / `DestinationEligible` (ParkSim.cs:235, 239) | exists; feed it 4–7 |
| tier `+0x126`, upgrade `0x116268`, defaults `0x116120` | `ParkRide.CurrentTier` (ParkSim.cs:207, "nothing here tracks that byte yet"), `Speed`/`Duration`/`Capacity` (:161, :162, :210) | fields exist; **nothing raises the tier or resets the settings** |
| upgrade request `0x1d5c00`, list `0x3953e8`, pending `+0x128` | none | missing |
| mechanics (`0x178xxx`), guards (`0x140xxx`) | no staff objects at all (shop-info-ui.md, staff-what "Areas") | missing; needs area A's person base first |
| work cell `vt+0x17c`, leave cell `vt+0xf4` | the same cells are already derived for queues (findings/native-ride-queue.md table, "ride vtable +17C / +F4") | reuse |
| staging turnstile P/R/E, event 9 (`0x14bcc0`) | `NativeEntranceFlow.Tick` (NativeEntranceFlow.cs:276–367), `StagingPending`/`EpisodeProcessed` (:175–176), `StagingTarget` = `0x1532d8` (:46, :67) | exists for guests only ("Guard staging remains unjoined", native-incoming-controller.md item 5): guards must add P on reaching staging, P−−/R++ on the 0x10 leg, and receive event 9 in state 0x2e |
| exit point `+0/+1`, park mouth `+0x10/+0x11` | `ParkEntranceEntry.XStart/ZRow` and `XCol/ZEnd` (ParkEntrance.cs:141) | exists; bytes `+2/+3` (staging) are read only by the flow's `StagingTarget` |
| guest prank/heckle arms, event 4 | port guests have the decision partly (visitors.md); no event 4 | missing |

---------------------------------------------------------------------------------------------------

## 8. Corrections

To `staff.md`:
- §3.2 "`0x153b80` … nearest Manhattan" is right, but the repair dispatch also needs **Life ≠ 0**
  (`vt+0x2cc = 0x1e1d58`, not an unresolved check).
- §3.2 "`rand(2) == 1` … the upgrade list again → dispatch as repair (INFERRED a slip)": the second call is
  **unreachable in effect** (§2.1); whatever was meant, branch 1 only ever installs.
- §3.2 "Call Mechanic … not … resting": the resting test compares the **mode** with 0x32, so resting mechanics
  are called out (§2.5).
- §3.2 "`0x178458` … handed to the first available mechanic": to the first available **after this one** in
  list order.
- §3.3 "gives up … guest in state 0x12/0x13/0x14 (queueing / shuffling / walking to a bin)": 0x13 is the state
  "Shuffle forward"; walking to a bin is **mode** 0x13. All three states are queue states.
- §3.3 "0x37 … random point near the park mouth": `0x153380` has no random term; the point is the mouth cell's
  centre.
- §3.3 "that `0x14bcc0` sends code 9 is INFERRED": READ (`uStack_6c = 9`, `puStack_70 = 0x35a548`).
- §2.3 "`C+0x2c` 0x200 guard carrying": a one-shot animation-restart flag consumed by the person draw (§5.4).
- §2.3 feature "`+0xa2` usable byte": `C+0x9a` is the object status (read by `0x14d238`, `0x104ce0`).

To the port's findings:
- track-ride-operation.md §6.4 and coaster-operation.md §4.6 "add to the condemned list 0x153d70": it
  **removes** the ride from the **upgrade** list (staff-what already noted the remove; confirmed in `0x153b00`).
- track-ride-operation.md §11 "0x1541d0 … sets status 7 and calls 0x116268(ride,1) when the assigned mechanic is
  in state 0x10/0x34/0x36": also when he is walking with mode 0x14 (`0x179508`).
- coaster-operation.md §4.5 "`0x1785f8` flag clear → route to the ride": the route is laid by state 0x38; the
  flag-clear branch only chatters and sets the flag (staff-what §7 had this; repeated for completeness).
- native-booth-admission.md "second staging population positively identified as guards": confirmed, and its
  mode-16 completion is `0x140cd8` case 0x10 → 0x30 (outbound) or 0x37 (inbound).

---------------------------------------------------------------------------------------------------

## 9. Still unknown (tried)

- **Chase deadline overwrite, intent**: READ that `+0x24` is reused as the route-request stamp; whether the
  one-leg pursuit is what players saw is not checked in the emulator.
- **Money check for upgrades**: none in `0x1d5c00`; whether the panel build `0x1d4c80` hides item 0 when funds,
  tier or `+0x128` forbid it was not traced (it copies `+0x128` to `ui+0x18e0`; that field's reader not found).
- **Counters `adv[0x264]+0x9e+2i`** (slots 0, 1, 2, 0x14, 0x15): writer read; reader not searched.
- **Advisor raisers** for 0x64/0x63 (pranks) and 0xc (build cameras): no constant `a1` before `0x107ca8`,
  `0x1073f0` or `0x107390` in the whole listing; the rule-table path (area D) was not followed.
- **Particle template 50** spawned by pranks (`0x1822d0`): which effect it is (stink/flies) not looked up in the
  particle table.
- **`VAR_BREAKSTAT` consumers on PS2**: the script text reads it; whether the PS2 interpreter's
  `BUMP_SETBROKEN` is a stub (as `BUMP` is for track rides) was not checked.
- **A guard catching a guest who is on a ride** (states 4/0x15 are not excluded): the guest is freed by
  `0x14b368` while linked as a rider; whether `0x20bfd0` unlinks it was not read.
- **Entertainer's stale heckler pointer**: `0x14b9d0` notifies guards, not entertainers, so a heckler who leaves
  during the 0–240-tick shock leaves a dangling pointer that `0x12e320` hands to a guard (READ paths; runtime
  effect not checked).
- **`0x153db0` case 6** pairs the track-ride list with the coaster count `0x395298` (decompile only, not MIPS;
  outside this area).
- **PoolOfCameras** (name at `0x35ff28` region): not the security cameras (those are features); its role not
  traced.
- Wall-clock tick rate: as in the port, not established; seconds above assume 40 ms.
