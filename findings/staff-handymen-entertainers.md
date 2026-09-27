# Staff area B: handymen, litter, toilets, entertainers, and the guests' side of them (PS2)

Researched 2026-09-27 by agent `sB`, the "how" pass for area B of `staff.md`. Starting map was
staff.md §3.1 and §3.4; every claim there that this file relies on was re-read, and the ones that
changed are listed in §8.

Sources:
- `SLES_500.32` in Ghidra 12.1.2, own copy `~/ghidra_tpw/agent_sB`. My decompiles:
  `~/ghidra_tpw/agent_sB/out/b1.c` (litter class, litter allocator, rand, tick, xrefs),
  `b2.c` (litter vtable methods, vomit/load producers, pool model setup, stink table, gate, tick writer),
  `b3.c` (load path, debug menu, tick increment, particle spawn, entry cell, advisor evaluator, save),
  `b4.c` (dirty-toilet metric, freeze, tick caller), `b5.c` (pool range check, guest load, reader xrefs),
  `b6.c` (Single Bog screen). The what-pass files `~/ghidra_tpw/agent_staff/out/handy.c`, `ent.c`,
  `s1.c`, `s2.c`, `s4.c`, and the corpus `…/ghidra-corpus/FUN_0020{fb88,c930,d010,d628,edd8,bcd0}`,
  `FUN_002113a8`, `FUN_0010de38` were re-read, not trusted.
- Raw MIPS (`scratchpad/full.s`) for every constant marked "MIPS".
- ELF data read with `~/ghidra_tpw/agent_sB/out/py/elf.py` (vtables 8-byte entries, fn word at `+N`).
- `DATA.WAD` text/advisor rows through the prebuilt `agent_staff/out/staffscan/bin/staffscan.dll`
  (run, not rebuilt); `Tp2.plb` names from `/tmp/tpw-anim/Tp2.plb` (SHA-256 `6564736a…3210`, the same
  file findings/particles.md hashes).

**READ** = seen in the decompile, MIPS or data. **INFERRED** = reasoned to. "Another agent's reading"
is marked where I cite and did not re-derive.

**Offsets.** Staff and guest pool slots are `P`; the map-object base the vtable methods receive is
`C = P + 8`. The handyman/entertainer functions (`0x144xxx`, `0x12dxxx`) take `P`; the staff base
(`0x1dbxxx`) takes `C`; guest functions (`0x20xxxx`) take guest `P` (written `G+`). Litter slots are
also `P` (0x30 bytes). Cross-reference for the brief's "`+0x53/+0x54`": **`P+0x53` = `C+0x4b`
tiredness, `P+0x54` = `C+0x4c` morale** (activation `0x1db618`: `C+0x4b = rand(30)`,
`C+0x4c = 70 + rand(30)`, READ).

**Units.** Positions are 1/256 cell (`s16`); every distance below is in **cells**, computed on the
cell coordinates that `vt+0x74` returns (`x >> 8`, `z >> 8` as signed bytes: persons `0x1925f8`, litter
`0x15e890`, MIPS). Time is the tick counter `DAT_00397644` (read by `0x1c4930`), incremented once per
call of `0x1c4a58` (MIPS), which `0x13add0` calls once per scene update. **25 ticks/s is another
agent's reading** (visitors.md "two-VBLANK on PAL"); seconds below use it and are labelled.
`rand(n)` = `0x1448e0` = lagged-Fibonacci `0x144870` **`divu`** n, so 0..n−1 (MIPS `0x1448f4`).

---------------------------------------------------------------------------------------------------

## 0. Short version

- **Litter is a pool object** (PoolOfLitter `0x3952c0`, **40 × 0x30**, vtable `0x361038`) with a
  vomit flag and a claimant. It has **no per-tick behaviour at all** (update `0x15e350` is `jr ra`):
  it never decays, and **the only thing that ever removes one is a handyman finishing a sweep**
  (`0x14b460` has one caller, `0x145598`). Pool full ⇒ new litter is silently not made. (READ)
- **Four producers**: a guest with a full rubbish meter and no bin within 5 cells (drop), a guest
  finishing being sick (vomit), an unhappy guest's prank (an **unshown** litter object plus a
  **`YellowStink`** particle — the advisor calls it a stink bomb), and the loader re-scattering
  saved counts at random path/queue cells (also unshown). Test-park mode (`0x2b72a8`) makes none. (READ)
- **Toilet wear has one producer**, the guest's relief exit `0x20edd8`: `wear = (toilet−60)·2/3` when
  the guest's toilet need is ≥ 61, else 0. It is undone only by a handyman (`0x130978`, one caller). (READ)
- **Handyman**: in state 0, a coin flip picks **nearest unclaimed litter** (park-wide Manhattan) or
  **the best toilet under 60 %** by the score **`|dx| + |dz|·(c+1)`** (MIPS `0x14534c..0x14536c`, only
  the z term is weighted), falling back to litter only if no toilet qualifies. Sweep takes
  120/60/30/20/10 ticks and a toilet 180/120/60/30/15 by level; a sweep gives morale +1 (vomit −6) and
  tiredness +5; a toilet sets condition 100, stamps the day, gives morale +5 (or −10 and an extra
  tiredness +5 if it was under 40) and tiredness +5. Patrol rectangles bound **only** idle wandering.
- **Entertainer**: from state 0, **1 in 3** and a guest within **1 cell** (2-D in practice, §4.3)
  ⇒ show: registers an **effector** (radius² 1 cell², flags 2) and performs until **600 ticks** pass or
  nobody is within 1 cell. Tiredness +2 and morale +1 every 4 ticks while performing. (READ)
- **Guests**: every 8 ticks, a guest inside a flag-2 effector (and off cooldown, stack depth < 2, not
  inside a building) stops and **watches** for `300 + 60·level` ticks, then gains **happiness +5** and
  a 900-tick cooldown. Every 64 ticks each litter within **Manhattan ≤ 1** costs happiness 3 (+3
  sickness if it is vomit). While idle, **1 %/decision** of a guest near an entertainer (≤ 4 cells)
  **heckles**: the entertainer is Shocked (morale −5, then **−10 every tick** for 0–4 s), then calls
  the nearest free guard within 6 cells to chase the heckler.
- **Effector flags 1 and 4 have no producer on PS2** (census §4.4): their guest effects are dead code.
- **Port**: it has lavatory `Condition`/`Wear`/`Service` and the guest `Litter` meter, but **no
  litter objects, no vomit object, no bins behaviour, no effectors, no entertainers, no handymen**;
  hooks and gaps in §7.

---------------------------------------------------------------------------------------------------

## 1. The litter object

### 1.1 Pool and layout (READ)

Pool `PoolOfLitter` global `0x3952c0`, built in `0x147eb0` (write `0x148ab4`) as **40 slots of 0x30**
(the pool-range check at `0x150b10` uses `+0x10..+0x790` = 0x10 + 40·0x30, READ). Header `+4` free
list, `+8` active list, `+0xc` active count. Active list is **LIFO**: `0x14ad08` inserts at the head
(so iteration order is newest first).

| `P+` | type | meaning | writers / readers |
|---|---|---|---|
| `+0x00/+0x04` | ptr | next / prev in the pool list | alloc `0x14ad08`, free `0x14b460` |
| `+0x08` | | `C`: map-object base (ctor `0x109308`, dtor `0x109340`) | |
| `+0x10` | ptr | model instance (`C+8`) | pool build `0x148dd0` |
| `+0x14` | u32 | activation serial (`0x1093b0`) | activation `0x15e280` |
| `+0x18` | ptr | vtable `0x361038` | ctor `0x15e1c8` |
| `+0x1c/+0x1e` | s16 | x / z, 1/256 cell | place `0x15e2b8` |
| `+0x20` | u32 | model variant `rand(6)`, rolled **once per slot at pool construction** (`0x15e1c8`) | model `0x15e468` |
| `+0x24` | u32 | **vomit flag** | `0x15e368` sets 1 (only if 0); `0x15e638` clears on free; activation 0 |
| `+0x28` | u32 | "model needs refresh" flag | `0x15e368` sets 1; `0x15e638` sets 1 when clearing vomit; `0x15e5b8` consumes; activation 0 |
| `+0x2c` | ptr | **claimant** (a handyman's `C`), 0 = free | handyman `0x144fd8` claims; releases §3.9; activation 0 |

Vtable `0x361038` (40 entries): `+0x0c` dtor `0x15e220`, `+0x34` activation `0x15e280`, **`+0x3c`
update `0x15e350` = `jr ra`**, `+0x44` `0x15e358` = `jr ra`, `+0x74` cell position `0x15e890` (same
code as the person's `0x1925f8`), `+0x84/+0x8c/+0x94` return 1, **`+0xa4` class = `0xd`**
(`0x15e9f0`); everything else is the map-object base. No other vtable in the ELF has class `0xd`
among the 23 map-object vtables found by scanning for `+0x14 == 0x109528` (READ).

### 1.2 Models (READ)

`0x15e468` sets the model through the instance (`inst->vt+0x24->+0xc(inst, id, 0, DAT_002b7f10)`),
positions it at `(x, 0, z)`, then **hides it** (`vt+0xac(0)`). Registry ids (table `0x2bf2b8`, 0x24-byte
entries, world 4 = all, category 12):

| condition | id | registry name |
|---|---|---|
| vomit flag set | 487 (`0x1e7`) | `puke` (`0x2c2318`) |
| variant 0, 1 | 598 (`0x256`) | `litter1` (`0x2c233c`) |
| variant 2, 3 | 599 | `litter2` (`0x2c2360`) |
| variant 4, 5 | 600 | `litter3` (`0x2c2384`) |

`DAT_002b7f10` is set once by `0x15e3c0` to whichever of the four ids has the largest
`0x17d558(id)` (what that measures is not read; INFERRED a shared allocation hint). `0x148dd0` (pool
model setup, from `0x149958`) calls `0x15e3c0` then `0x15e468` on all 40 slots, so **every slot starts
with its model built and hidden**. `vt+0xac` on the instance is show/hide: the person show/hide
`0x192c10` uses the same slot (READ).

Show/hide in the litter code (READ, whole class): `0x15e5f0` = refresh-if-dirty then **show**;
`0x15e638` (on free) = clear vomit → dirty, **hide**, refresh; `0x15e468` hides. **`0x15e5f0` has two
callers: the drop path `0x20d4cc` and the vomit path `0x210a54`.**

### 1.3 Every producer of litter (READ: `0x14ad08` has exactly 4 call sites)

`0x14ad08` = allocate: returns 0 if **test-park mode** (`0x153410` → `DAT_002b72a8`, named in
coaster-operation.md §8.1) or if the free list is empty; else pops, pushes on the active list, count+1,
`vt+0x34` activation (serial; `+0x24 = +0x28 = +0x2c = 0`), registers the map object (`0x14da60`).
Place `0x15e2b8(litter, pos)`: `x = pos.x + rand(200) − 100`, `z = pos.z + rand(200) − 100` (1/256
cell, so −100..+99), and moves the instance there.

| # | producer | where it lands | vomit | shown | other effects |
|---|---|---|---|---|---|
| 1 | guest **drop**: idle arm 3 (`+0x74` > 89) → `0x20d010(g, 0)` when no bin is within Manhattan ≤ 5 (MIPS `0x20d2e0` `sltiu 6`) | guest's 1/256 position ± 100 | no | **yes** (`0x15e5f0`) | guest `+0x74 = 0` (even if the pool was full); advisor counter 0x15 += 1 (§1.6) |
| 2 | guest **prank**: idle arm 5 → `0x20d010(g, 1)` | guest's position ± 100 | no | **no** | `0x1822d0(cellx, cellz)` stink effect (§1.5); advisor counters 2 and 0x14 += 1; guard dispatch `0x14d3e0(g)`; guest `+0x74` **not** reset |
| 3 | guest **vomit**: state 0x1d end (`0x210950`) | guest's position ± 100 | **yes** (`0x15e368`) | **yes** | guest sickness `+0x76 = 0`, depth 0, logical 11, state 0 |
| 4 | **load** `0x160880` → `0x14d8d8(counts)` | random cell `(rand(W), rand(H))` (map size `0x3952f0/0x3952f4`) retried until its kind is 2 (path, `0x1e6338`) or 4/7 (`0x1e6348`; 4 = queue tile per native-ride-queue.md, 7 not identified); cell centre ± 100 | first `counts[0]` plain, then `counts[1]` vomit | **no** | `0x14d8d8` does not null-check the allocation (MIPS `0x14d938..0x14d9b8`) |

- MIPS `0x20d13c..0x20d228` (prank) and `0x14d908..0x14d9cc` (load) contain no show call. So prank
  litter and reloaded litter stay hidden, and reloaded vomit keeps the non-vomit model (dirty flag set,
  never refreshed). **INFERRED: they are invisible but otherwise real**: handymen find, claim and sweep
  them, and guests next to them lose happiness (§5.4). Not checked on screen.
- **Save** is two counts only: `0x1c2900` → `0x14d868` counts plain vs vomit (`+0x24`) over the active
  list and writes 2 bytes (`0x1c1e10(buf, 2, 1)`); **positions and claims are not saved** (READ).
- **Not producers** (READ): no timer, no ride, no shop and no staff code allocates litter; the guest
  purchase only raises the guest's *carried* meter `+0x74` (§5.8).

### 1.4 Lifetime and removal (READ)

- Update `vt+0x3c` is empty; there is no age field. A litter item lives until **`0x14b460`**, whose only
  caller is the handyman's sweep completion `0x145598`, or until the pool is torn down (`0x149038`,
  models released by `0x148f48`).
- `0x14b460(litter)`: `0x15e638` (clear vomit, hide, refresh model), unregister the map object
  (`0x14dac0`), move from the active to the free list, count−1. It sends **no** "object removed" notice
  (`0x14b9d0` is not called), which is safe only because only the claimant can remove it.

### 1.5 The prank stink table `0x3ae140` (READ)

What the what-pass asked about (`0x1824a8`, `0x3ae140`, `0x37e114`):
- Table `0x3ae140`: up to **10** entries `{u32 particle handle, s32 cellx, s32 cellz}` (12 bytes),
  count `DAT_0037e114`.
- **Add** `0x1822d0(cellx, cellz)` (only caller: the prank, `0x20d1e8`): refuse if the count is 10 or
  that cell is already in the table; else spawn particle **50** with `0x18b5a8(50, (cellx + 0.1 +
  (r1 % 9)/10)·10240, 0, (cellz + 0.1 + (r2 % 9)/10)·10240)` (`r1, r2` from `0x29cf08`, INFERRED libc
  rand) and store it. Particle 50 in `Tp2.plb` is **`YellowStink`** (record 50's name at `+0x118`).
- **Remove** `0x1824a8(cellx, cellz)` (only caller: the sweep, `0x145698`, with the swept litter's own
  cell): find the entry by cell, stop its emitter (`0x18aae0`: emitter `+0x20 = −2` if the handle is
  live, `0x18bb88`), move the last entry into the hole, count−1.
- **Clear all** `0x182598` from `0x1f6bd0` (teardown with other effect systems).
- Consequences (INFERRED from the READ arithmetic): the stink is keyed by the **guest's** cell but
  removed by the **litter's** cell, and the litter is scattered up to 100/256 cell away, so a prank near
  a cell edge leaves a stink that sweeping never stops; an 11th concurrent prank makes litter with no
  stink. The advisor text for this is `STR_ADVMES_ADD_PRANK_SBOMB` (0x63): "Some prankster has left a
  stink bomb in your park. Get it cleaned up before too many people get ill." — but prank litter has
  **no** vomit flag, so it costs nearby guests happiness only, never sickness (§5.4).

### 1.6 Advisor side effects (READ counters; rules are area D's reading)

`0x1073c0(adv, n, d)` → `0x10ddd8`: advisor counter `n` (at `adv+0x9e+2n`, clamped ±30000) += d,
which the rule evaluator reads as variable `0x38 + n` (`0x10de38` cases `0x38..0x4d`). So: drop → n
0x15 = **v77**; prank → n 2 = v58 and n 0x14 = **v76**; heckle → n 2 = v58. Agent sD's rule dump
(`agent_sD/out/rules.txt`, not re-derived) has rule 46 `v76 > 0` → `ADD_PRANK_SBOMB` and rule 47
`v77 > 0` → **`ADD_PRANK_LITTER` "A prankster is littering your park"**, so that message is triggered by
**ordinary** drops, and v58 appears in no rule. Litter count (`0x14d208`) is advisor variable 13
(`0x10de38` case 0xd): rule 63 "no cleaners and the park is getting untidy" is `v10 == 0 && v13 > 10`.

---------------------------------------------------------------------------------------------------

## 2. Toilets: condition, wear, and who reads it

### 2.1 What counts as a toilet (READ)

The placed-object list `0x14cd30` (`*(0x3952a0 + 8)`; its users are all placed buildings: bins,
cameras, staff rooms, advisors) holds objects of many classes. **Only the feature class, vtable
`0x35dc70`, overrides `vt+0x134`**, with `0x130718` = DBA record `+0x2e` **bit 0** (the other 22
map-object vtables have the default `0x109878` = `return 0`; ELF scan). The guest's relief exit uses
`vt+0x1dc` = `0x130780`, the same bit. So "toilet" = feature with DBA `+0x2e` bit 0 (the port's
`ProvidesRelief`).

### 2.2 Feature fields used (READ; feature `P`, class `0x35dc70`)

| `P+` | meaning | writers | readers |
|---|---|---|---|
| `+0xb4` | **condition** 0..100 ("Cleanliness") | activation `0x1302d8` = 100; wear `0x130948` (floor 0); clean `0x130978` = 100; load `0x130678` (clamped 0..100) | `0x130938`: handyman search/clean, guest relief, advisor `0x104760`, `0x10d228`, Single Bog screen |
| `+0xa8` | **day last cleaned** (calendar day count `0x16b218`) | activation 0; `0x130978`; load | `0x130940`: only the Single Bog screen `0x1da008` ("Last Cleaned") |
| `+0xac` | occupied flag for scripted relief facilities | guest arrival/exit (`0x20edd8` sets 0) | `0x1309f8` (guest can use) — **handymen never touch it** |
| `C+0x18` | uses counter ("Users") | `0x1e1cd8` (+1 per use) | `vt+0xd4` = `0x1e1ce8`, Single Bog screen |
| `+0xb0` | effector pointer | activation writes 0 (the only `sw …,0xb0` in `0x130000..0x131000`) | freed in `0x130498` if non-zero — never set, dead |

Save `0x130600`: record `+0xe` = condition, `+8` = day stamp. **Condition is saved**, unlike litter.

### 2.3 Every producer of toilet wear (READ: `0x130948` has exactly one caller, `0x20ef48`)

`0x20edd8` (guest state 0x16, leaving a facility), feature arm (class 2) when `vt+0x1dc` (toilet):
```
if (toilet >= 61) wear(feature, ((toilet - 60) * 2) / 3);   // 0..26; toilet = guest +0x79
toilet = 0;
sick = max(0, sick - 40);
uses++;                                                        // 0x1e1cd8
if (condition < 50) { bubble 10; happiness -= 10; sick += 10; } // read AFTER the wear
feature+0xac = 0; relief sound (bank 7, 0x35)
```
Nothing else lowers `+0xb4`: no timer, no weather, no vomit, no litter. (READ census of `0x130948`.)

### 2.4 Every consumer of condition (READ: 8 call sites of `0x130938`)

| consumer | rule |
|---|---|
| guest relief `0x20ef8c` | `< 50` → happiness −10, sickness +10, angry bubble |
| handyman search `0x1452e4/0x145328` | candidate if `< 60`; weight `c + 1` (§3.5) |
| handyman clean `0x145720` | `< 40` at completion → morale −10 and extra tiredness +5 |
| advisor `0x104760` (variable 31) | `100 − average condition` over all toilets (0 if none); rule 101 (sD): `v10 == 0 && v31 > 25` → `FEATURES_DIRTY_TOILETS_1` |
| `0x10d228` | average condition over an object's own list at `+0x3d0` (count `+0x348`); owner not traced |
| Single Bog screen `0x1d9f70/0x1da008` | "Users" (`vt+0xd4`), "Last Cleaned" (`0x142948(stamp)`), "Cleanliness" bar (`c/100`); text rows 168/1077/132 |

---------------------------------------------------------------------------------------------------

## 3. The handyman

### 3.1 Constants (READ from the ELF; level `L = C+0x48 & 7`, 0..4; seconds at 25 ticks/s)

| table (12-byte rows at `0x35fbd8`) | L0 | L1 | L2 | L3 | L4 | reader |
|---|---|---|---|---|---|---|
| sweep time, ticks `+0` | 120 (4.8 s) | 60 | 30 | 20 | 10 (0.4 s) | arrival `0x144ec8` (MIPS `0x144f14..0x144f40`) |
| toilet time, ticks `+4` | 180 (7.2 s) | 120 | 60 | 30 | 15 (0.6 s) | arrival (MIPS `0x144f54..0x144f84`) |
| walk speed `+8` | 10 | 15 | 20 | 20 | **18** | `vt+0x18c` = `0x144d38` |

The handyman overrides `vt+0x18c`, so the staff base 5-bit speed (15) is not used for him. Walk step
arithmetic is area A's.

### 3.2 State machine (READ)

Update `vt+0x3c` = `0x1459b0`: skip if frozen (`C+0x2c & 0x40`) or focus object (`0x14e1a0`); call
`vt+0x174` (empty); state **0x1b → `0x145598`**, **0 → `vt+0x1a4` `0x1457e0`**, **0x33 → `0x1456d8`**,
else staff base `0x1dc018` (0xd patrol, 0xf, 0x1a, 0x31, 0x32, person states 1/2/3/5/0xb).

| state | what | exit |
|---|---|---|
| 0 | find work `0x1457e0` (§3.3) | 0xb (route requested, litter mode 7 / toilet mode 0x12), 0xd (nothing), 0x1a (strike), 0x31 (tired) |
| 0xb | waiting for the route planner (person) | event 1 → 3 (walk); event 2 → §3.8 |
| 2/3 | walking; state 2 = segment end → `vt+0x144` = `0x144ec8` | route slot `C+0x28 == −1` and mode 7 → **0x1b**; mode 0x12 → **0x33**; else base `0x1db970` (tiredness +1 on the 4-tick serial phase, advance segment; route end of other modes → state 0) |
| 0x1b | sweeping: logical anim 16 every tick; done when `now > deadline` | → 0 (§3.6) |
| 0x33 | cleaning toilet: model hidden; done when `now > deadline` | → 0, model shown (§3.7) |
| 0xd | patrol (base `0x1dc6b0`) | route (mode 1) → walk → route end → 0; or 5 (local wander) |

`deadline = now + time[L]` is written at arrival and the test is strict (`deadline < now`, MIPS
`sltu`), so a job lasts `time[L] + 1` updates after the arrival tick.

### 3.3 Find work `0x1457e0` (READ, MIPS `0x1457f0..0x1458e8`)

```
if ((rand() & 0xf) == 0) sound(bank 8, 0xa1, pos, handle P+0x58);        // 1 in 16
if (vt+0x1bc(C)) return;                  // 0x1dba90: strike -> 0x1a; tiredness >= 81 -> 0x31 (area A/D)
found = (rand(2) == 0) ? findLitter() : findToilet();   // findToilet falls back to findLitter
if (!found) { sound(8, 0xa0, pos, P+0x5c); depth = 0; state = 0xd; }
```

### 3.4 Litter search `0x144fd8` (READ)

- **Candidates**: every item on the active litter list (`0x14d1f8`) with claimant `+0x2c == 0`. No
  distance limit (park-wide), no patrol-rectangle test, no vomit preference, no reachability test.
- **Score**: `|litter.cellx − my.cellx| + |litter.cellz − my.cellz|` (Manhattan, cells).
- **Ties**: strict `<` against the best so far, list order = newest first ⇒ **the newest of equally
  near items wins**.
- **Claim and route**: `mode (C+0x2e) = 7`, `target C+0x20 = litter C`, `litter+0x2c = my C`; route
  request `0x18da78(C, myX, myZ, litter.x, litter.z, 0x11, 0)` to the litter's raw 1/256 position.
  Route refused → debug print, `target = 0`, **unclaim**, return 0. Accepted → `C+0x24 = now`, push the
  current state on the goal stack, **state 0xb**, return 1.

### 3.5 Toilet search `0x145250` (READ, MIPS `0x14530c..0x14539c`)

- **Candidates**: every object on the placed list `0x14cd30` whose `vt+0x134` is true (toilet, §2.1)
  and whose condition `c < 60`. No claim is taken or checked: **several handymen can be sent to the
  same toilet** (nothing is written to the toilet by the search).
- **Score** (unsigned, lowest wins):
  ```
  score = |t.cellx - my.cellx| + |t.cellz - my.cellz| * (c + 1)
  ```
  MIPS: `v1 = |t.z − my.z|; v1 *= (c+1)` (`mult $v1,$v1,$v0` at `0x14535c`), `a1 = |t.x − my.x|`,
  `score = a1 + v1` (`0x14536c`). Toilet position is `vt+0x74` = `0x1e1fa8`, the feature's stored
  origin cell at `C+0x84..0x89` (not its entry cell). INFERRED: the intended score was
  `(|dx|+|dz|)·(c+1)`; as shipped, **a toilet in the handyman's own row (`dz = 0`) is scored by `|dx|`
  alone, however clean it is (c ≤ 59)**, and a toilet one row away at c = 59 costs `|dx| + 60`.
- **Ties**: strict `<`, first in the placed list wins (the list's order is not traced).
- **Fallback**: only if no toilet qualifies does it call the litter search. If the chosen toilet's
  route is refused it returns 0 **without** trying litter.
- **Route**: target cell = origin + entry offset (`0x1e1760`: DBA record `+0xc` rotated by `0x1e2288`,
  default `DAT_00369a30` if negative), to the cell centre `(cell·256 + 0x80)`; `0x18da78(…, 0x11, 0)`;
  `mode = 0x12`, `target = toilet C`; accepted → `C+0x24 = now`, depth 0, **state 0xb**.

### 3.6 Sweep completion `0x145598` (READ, MIPS `0x145598..0x1456d0`)

Every update in state 0x1b sets logical animation 16. When `now > deadline`:
```
litter = target - 8
morale    = clamp(morale + (litter.vomit ? -6 : +1), 0, 100)
tiredness = clamp(tiredness + 5, 0, 100)
litter.claimant = 0
0x1824a8(litter.cellx, litter.cellz)       // stop a prank stink in that cell, if any (§1.5)
0x14b460(litter)                           // free it (the ONLY remover of litter)
state = 0; target = 0; depth = 0
```

### 3.7 Toilet completion `0x1456d8` (READ, MIPS `0x1456e8..0x1457a4`)

On arrival (`0x144ec8` mode 0x12): deadline `now + toiletTime[L]`, **state 0x33, model hidden**
(`vt+0x2c(0)`); no logical animation is set while hidden. When `now > deadline`:
```
toilet = target - 8
if (condition(toilet) < 40) { morale = max(0, morale - 10); tiredness = min(100, tiredness + 5); }
else                          morale = min(100, morale + 5);
tiredness = min(100, tiredness + 5)
0x130978(toilet)            // condition = 100, +0xa8 = calendar day (0x16ae90 -> 0x16b218)
depth = 0; state = 0; show model (vt+0x2c(1))
```
The condition is read **at completion**, so wear added by guests during the clean counts. Guests may
use the toilet while it is being cleaned (`+0xac` untouched; `0x1309f8` does not look at staff).

### 3.8 Events and releases (READ)

| path | litter claim | other |
|---|---|---|
| event 2 (route failed) `0x144d60`, mode 7 and target set | if target class (`vt+0xa4`) == 0xd: **unclaim** (else debug print) | target 0, **state 0xd**, depth 0 |
| event 2, any other mode | — | base `0x1db768`: mode 5 → 0, mode 1 → 5, else 0xd |
| event 1 (route found) | — | depth 0, state 3 |
| event 3 | — | ignored |
| **event 5** `0x144d60` | **not released** | free route, target 0, depth 0, state 5 — a claimed litter item stays claimed forever. Sender of code 5 not found (§9) |
| route-system reset `0x145ac8` (`0x18c340/0x18c4b8`) | unclaim if target is litter, **unless** state is 0x1b or 0x33 | `0x1dc780` → `0x1928b0`: target 0, route freed, depth 0, frozen, state 0 |
| release `vt+0x194` `0x145b48` (fire → `0x14b608`) | unclaim if target is litter | `0x1925a8` person remove |
| placed object removed `0x14b9d0` → `vt+0x19c` `0x1928f8` | n/a (litter removal sends no notice) | if target == the removed object: target 0, route freed, depth 0, state 0, model shown (covers a toilet demolished mid-clean) |

Tiredness/strike checks run only in state 0, so a handyman never abandons a job to rest or strike;
he finishes it first.

### 3.9 Patrol confinement (READ)

The rectangle `C+0x44..+0x47` is read only by the patrol `0x1dc558` (state 0xd): up to 10 tries of a
random cell in `[x0, x1) × [z0, z1)` that is in bounds (`0x149d20`), a path cell (kind 2) and routable
(flags 0x11, mode 1). No rectangle (`x1−x0 ≤ 0`) or 10 failures → state 5 (local wander). **Neither job
search reads it.** INFERRED: the rectangle only changes where an idle handyman stands, hence which
litter is "nearest" at his next state-0 search; it never stops him leaving it. The advisor still says
"Your cleaners could do a better job if you set their patrol areas" (advisor 0xa).

### 3.10 Sounds (READ)

Bank 8 (`AUDIO/GLOBAL/staf`): `0xa1` 1-in-16 per find-work (handle `P+0x58`), `0xa0` when nothing
was found (handle `P+0x5c`). No sound on sweep or toilet completion.

---------------------------------------------------------------------------------------------------

## 4. The entertainer

### 4.1 Fields (READ; `P+`)

`P+0x58` effector record (or 0); sound handles `P+0x5c` (0xa4), `P+0x60` (0xa5), `P+0x64` (0x87);
`P+0x2c` (`C+0x24`) = show start tick, **reused** as the Shocked deadline; `P+0x28` (`C+0x20`) = the
heckler guest's `C` while Shocked. Level `C+0x48 & 7` only feeds the guests' watch time (and wage).
Walk speed: no `vt+0x18c` override, so the staff base 15.

### 4.2 State machine (READ; update `0x12e160`: 0 → `vt+0x1a4` `0x12de40`, 0xc → `0x12df40`,
0x20 → `0x12e320`, else base)

| state | what | exit |
|---|---|---|
| 0 | find work `0x12de40` | 0xc (show), 0xd (patrol), 0x1a / 0x31 (strike / tired) |
| 0xc | performing `0x12df40` | `now > start + 600` or nobody within 1 cell → 0 |
| 0x20 | Shocked `0x12e320` (pushed over whatever state it interrupted) | `now > deadline` → pop |
| 0xd, 1/2/3/5/0xb, 0xf, 0x1a, 0x31, 0x32 | base / person | arrival handler `0x12dc88` is the base `0x1db970` for every mode |

### 4.3 Find work and the show trigger (READ, `0x12de40`, `0x12dcc8`, `0x12dae0`)

```
0x12de40:  if ((rand() & 0xf) == 0) sound(8, 0xa5, pos, P+0x60);
           if (effector) { free(effector); effector = 0; }        // 0x14d038
           if (vt+0x1bc(C)) return;                               // strike / tired
           0x12dcc8:
             if (rand(3) == 0 && guestAdjacent() && state != 0xc) {
                 start = now;
                 effector = 0x14cfb8();                           // may be 0 if 20 are in use
                 if (effector) { eff.pos = my cell (8-byte copy); eff.radius2 = 1; eff.flags = 2; }
                 depth = 0; state = 0xc; return;
             }
             depth = 0; state = 0xd; sound(8, 0xa4, pos, P+0x5c);
```
`guestAdjacent` = `0x12dae0`: any guest on the **active** PoolOfPeople list (`0x14d218`) with
`dx² + dy² + dz² < 2` over `vt+0x74` cell coordinates; no guest-state test (READ), so INFERRED a
guest hidden inside a ride or shop counts if its stored position is adjacent.

⚠ **The `y` in that test is not a height.** `0x1925f8` builds its 3-short result from a stack struct
whose middle short is never written (MIPS `0x1925f8..0x1926f0`: only `sp+0x10` (x) and `sp+0x14` (z) are
stored before the 8-byte `ldl/ldr`), and it stores the same garbage back to the same slot. In
`0x12dae0` the entertainer's and every guest's position come from back-to-back calls of the same
function at the same stack depth with nothing in between, so both read the same short and **dy = 0**
(INFERRED from the MIPS). Reimplement as **2-D: `dx² + dz² ≤ 1`** (own cell or the 4 orthogonal
neighbours).

### 4.4 Effectors (READ)

Pool `PoolOfEffectors` `0x3952a8`: 20 × 0x1c (range check `0x150b10`: `+0x10..+0x240`). Record:
`+0/+4` links, `+8` x (s16, cells), `+0xa` junk y, `+0xc` z, `+0x10` **radius² (u32)**, `+0x14`
**flags**, `+0x18` unused. Alloc `0x14cfb8` (pop free → push active), free `0x14d038`, query
`0x14d0a0(pos)` = OR of `flags` over active records with `dx² + dz² ≤ radius²`.

**Census**: `jal 0x14cfb8` occurs **once** in the ELF (`0x12dd14`, the entertainer); the pool global
`0x3952a8` is referenced at `0x148694` (build), `0x1492e4` (teardown), `0x14cfbc/0x14d03c/0x14d0a8` (the
three functions) and `0x150b10` (the pointer range check — **missed by Ghidra's xref list**, found by
searching the listing for `0x52a8(`). So **every effector on PS2 is an entertainer's flags-2,
radius²-1 zone**; the guest code for flags 1 and 4 (§5.1) never fires. The feature's `+0xb0` effector
slot is only ever written 0.

Lifetime: registered when a show starts; freed on the next find-work (the tick after the show ends),
on a heckle (`0x12e278`), on release (`0x12e5b0`) and on fire (`0x12e768`). Not moved during a show.

### 4.5 Performing `0x12df40` (READ, MIPS `0x12df78..0x12dfc8`)

```
logical = 16 (every update)
if ((now & 3) == (serial & 3)) { tiredness = min(100, tiredness + 2); morale = min(100, morale + 1); }
if (now > start + 600 || !guestAdjacent()) {
    depth = 0; state = 0; sound(8, 0x87 "TADA", pos, P+0x64); logical = 11;
}
```
600 ticks = 24 s. Tiredness +2 per 4 ticks reaches 100 in 200 ticks from 0 (INFERRED: after nearly
every full show the next state-0 check sends him to the staff room at ≥ 81).

### 4.6 Heckled: `0x12e278` and state 0x20 `0x12e320` (READ, MIPS `0x12e34c..0x12e368`)

```
0x12e278(ent, guest):                      // only caller: guest idle arm 2 (0x20cecc)
  if (state != 0x20) { push(state); state = 0x20; }
  morale = max(0, morale - 5)
  heckler = guest C
  deadline = now + rand(5) * 60            // 0, 60, 120, 180 or 240 ticks  (overwrites show start)
  if (effector) { free(effector); effector = 0; }

0x12e320 (every update in 0x20):
  morale = max(0, morale - 10)             // unconditional, before the deadline test
  if (now > deadline) {
      g = nearest guard with !busy(0x1416f0), Manhattan cells, strict <, initial 0xffff
      if (g && dist <= 6) 0x1417d0(g, heckler P)          // guard chase: area C
      else morale = max(0, morale - 5)
      state = pop()
  }
```
INFERRED consequences: one heckle all but zeroes morale (−5, then −10 per update for 1..241 updates),
which feeds the monthly strike test (average morale ≤ 14, staff.md §4.5). A heckle during a show
pops back into state 0xc **without** an effector (no new watchers) and with `start` = the Shocked
deadline, so the show can run up to 600 more ticks; guests already watching stop at once (§5.3).
Any entertainer can be heckled, including one resting hidden in a staff room or striking.

---------------------------------------------------------------------------------------------------

## 5. The guests' side

### 5.1 Effector phase of `0x20fb88` (guest think, `vt+0x174`; READ)

Every 8 ticks per guest (`(now & 7) == (serial & 7)`): `f = 0x14d0a0(my cell)`.
- `f & 1` → happiness +6. `f & 4` → if walking (state 2 or 3): happiness −3, sickness +5; else
  happiness −1, sickness +2. **Both dead on PS2** (§4.4).
- `f & 2` → the watch trigger, §5.2.

### 5.2 Starting to watch (READ, MIPS `0x20fcbc..0x20fe28`)

All of: guest flags `C+0x2c` bit 2 clear (not inside a facility; `0x20edd8` clears it on exit), goal
depth `< 2`, `now > G+0x6c` (unsigned), state `!= 0x1c`, `f & 2`. Then:
```
ent = nearest entertainer on the active list 0x14d670 (Manhattan cells, strict <, first wins; no
      state or range test beyond the effector having matched)
G+0x68 = ent C; push(state); state = 0x1c
G+0x2c = now + 300 + 60 * (ent C+0x48 & 7)       // 300..540 ticks = 12..21.6 s
```
The guest is **not routed**: state 0x1c has no movement, so the guest stops where it is (a queueing
or walking guest is paused and resumes its pushed state afterwards; INFERRED from the state machine).
`G+0x6c` writers (census of `sw …,0x6c(` in `0x20a000..0x212000`): spawn `0x20bf10` = spawn tick +
`rand(300)`; facility exit `0x20ee40` = `now + 60 + rand(60)`; watch end `0x210934` = `now + 900`.

### 5.3 Watching, guest state 0x1c `0x2107a0` (READ)

Every update: logical 2; face the entertainer (same x: ent.z ≤ my.z → π, else 0; ent.x < my.x →
3π/2; else π/2). If the entertainer is in state 0xc and `now ≤ G+0x2c` → keep watching. Otherwise:
`state = pop()`, logical 13, **happiness = min(100, happiness + 5)**, `G+0x68 = 0`,
`G+0x6c = now + 900` (36 s). The +5 is paid whether the show ran its course or was cut short.

### 5.4 Litter proximity (READ, MIPS `0x20ff00` `slti $a0,$a0,2`)

Every 64 ticks per guest (`(now & 0x3f) == (serial & 0x3f)`), for **each** active litter item with
`|dx| + |dz| < 2` (cells; own cell or 4 neighbours):
```
if (litter.vomit) sick = min(100, sick + 3);
happiness = max(0, happiness - 3);
```
No visibility test: hidden prank and reloaded litter count the same.

### 5.5 Idle decision arms 2–5 of `0x20c930` (READ; guest state 0, called every update in state 0 by
`0x2113a8`; arm = `rand(6)`)

| arm | condition | effect | per state-0 update |
|---|---|---|---|
| 2 heckle | `rand(1000) < DAT_002eeb64` (**10**) and the nearest entertainer (Manhattan, any state) is `< 5` cells (MIPS `0x20cde4` `sltiu 5`) | guest logical 4; counter 2 += 1; `0x12e278(ent, guest)` | 1/600 |
| 3 litter | `+0x74 > 89` | `0x20d010(g, 0)`: bin or drop (§5.6) | 1/6 |
| 4 vomit | `+0x76 > 92` and `rand(4) == 0` | depth 0, state **0x1d**, logical 12, `G+0x2c = now + 15`, sound (7, 0xcc) | 1/24 |
| 5 prank | happiness `< DAT_002eeb98` (**25**) and `rand(1000) < DAT_002eeb68` (**10**) | `0x20d010(g, 1)` (§1.3 row 2) | 1/600 |

Arms 2, 3 (drop branch) and 5 leave the guest in state 0, so it re-rolls on the next update.
`DAT_002eeb68` and `DAT_002eeb98` have no writer (xrefs: one read each). **`DAT_002eeb64` is written by
`0x12c348`**, the apply function of the object named **"Debug Menu"** (`0x12bfa8`, 0 direct callers),
together with 0x2eeb30/3c/40/44/4c/50/54/60; the shipped image value is 10.

### 5.6 Bins (READ, `0x20d010(g, 0)` and arrival `0x20d628` mode 0x13)

Nearest placed object with DBA `+0x2e` bit 2 (`0x1307e8`), Manhattan cells, strict `<`, first wins. If
none or `dist > 5`: drop litter (§1.3 row 1). Else route to the bin's raw position adjusted by its
facing `0x1e1de8` (`C+0x98`), jump table `0x36c980` (MIPS `0x20d328..0x20d38c`): 0/4 → z −0x80;
1 → x −0x80; 2 → z +0x180; 3 → x −0x180; ≥ 5 → no offset (1/256 cell; the asymmetry is as read), `mode = 0x13`, `target = bin`, `0x18da78(…, 1, 1)`, state 0xb. On arrival
(mode 0x13): `+0x74 = 0`, depth 0, state 0. **Bins have no capacity and are never emptied** (nothing is
written to the bin; READ absence in both paths).

### 5.7 Vomiting, guest state 0x1d `0x210950` (READ)

When `now > G+0x2c` (15 ticks after arm 4): allocate litter at the guest's position ± 100, set vomit,
show (puke model); `+0x76 = 0`; depth 0; logical 11; state 0. Pool full ⇒ no puke, sickness still reset.

### 5.8 The carried-rubbish meter `+0x74` has no timed riser (READ census)

Every `sb …,0x74(` in the ELF: spawn `0x20bd4c..0x20bd68` (`rand(40)`); purchase arms `0x20e528` and
`0x20e6a0/ac` (`+DAT_002eeb60` (30) `+ rand(25)`); drop `0x20d4d4/ec` and bin arrival `0x20dce8/0x20dd08`
(= 0); guest load `0x211a00` (from `0x1603b0`); an unreferenced setter `0x212330`. `0x20fb88` does
not touch it.

---------------------------------------------------------------------------------------------------

## 6. Reimplementation sketch (one tick = one update of every object)

```
Litter { x, z (1/256 cell); bool vomit; Handyman claimant; int variant = rand(6) per slot; bool shown; }
pool 40, LIFO list; alloc fails when full or in test-park mode

Handyman.FindWork():                       // state 0
  if rand16 == 0: sfx 0xa1
  if TiredOrStriking(): return
  ok = rand(2) == 0 ? FindLitter() : (FindToilet() ?: FindLitter-only-if-no-candidate)
  if !ok: sfx 0xa0; state = Patrol
FindLitter: min over unclaimed litter of manhattan(cell); tie -> newest; claim; route(mode 7)
FindToilet: min over toilets with c<60 of |dx| + |dz|*(c+1); tie -> list order; route to entry cell (mode 0x12)
Arrive(mode 7):  job = Sweep,  deadline = now + [120,60,30,20,10][L]
Arrive(mode 12): job = Toilet, deadline = now + [180,120,60,30,15][L]; hide
Sweep done:  morale += vomit?-6:+1; tired += 5; stopStinkAt(litter.cell); free litter; state 0
Toilet done: if c<40 {morale-=10; tired+=5} else morale+=5; tired+=5; c=100; lastCleaned=today; show

Entertainer.FindWork():                    // state 0
  if rand16 == 0: sfx 0xa5
  free effector
  if TiredOrStriking(): return
  if rand(3)==0 && AnyGuestWithin1(): start=now; effector={myCell, r2=1, flags=2}; state=Perform
  else: sfx 0xa4; state = Patrol
Perform (each tick): anim 16; on serial&3: tired+=2, morale+=1
  if now > start+600 || !AnyGuestWithin1(): state 0; sfx 0x87; anim 11
Heckled(guest): push unless already 0x20; morale-=5; deadline = now + rand(5)*60; free effector
Shocked (each tick): morale -= 10; if now > deadline: guard(<=6) chases guest else morale -= 5; pop

Guest, every 8 ticks: if inEffector(flags 2) && !inside && depth<2 && now>watchCooldown && state!=0x1c:
    push; state = Watch(ent = nearest entertainer); until = now + 300 + 60*ent.L
Watch (each tick): face ent; if ent.state != Perform || now > until:
    pop; happiness += 5; watchCooldown = now + 900
Guest, every 64 ticks: for each litter within manhattan<=1: happiness -= 3; if vomit: sick += 3
Guest idle arms (per update in state 0, arm = rand(6)): 2 heckle(1%, ent<5), 3 bin-or-drop(+0x74>89),
    4 vomit (sick>92, 1 in 4), 5 prank (happiness<25, 1%)
```

---------------------------------------------------------------------------------------------------

## 7. The port (`~/tpw-coasters`, read-only): hooks and gaps

**What exists and maps directly (READ in the port):**

| console | port | status |
|---|---|---|
| toilet test DBA `+0x2e` bit 0 | `ParkRide.ProvidesRelief` (`ParkSim.cs:33`) | same bit |
| condition `+0xb4`, activation 100 | `ParkRide.Condition` (`ParkSim.cs:75`) | ✓ |
| `0x130948` wear, floor 0 | `ParkRide.Wear` (`ParkSim.cs:264`), called from `ParkVisitors.Serve` (`:1022`) with `Needs.UseToilet` | ✓ formula `(toilet−60)·2/3` per its comment |
| `< 50` penalty after wear | `VisitorNeeds.DirtyLavatory`, `FilthyBelow = 50` | ✓ |
| `0x130978` | `ParkRide.Service()` (`ParkSim.cs:267`) | sets 100 only — **no last-cleaned day** (`+0xa8`, read by the Single Bog "Last Cleaned") |
| guest `+0x74` carried rubbish | `VisitorNeeds.Litter`, purchase `LitterBase 30 + Rand(25)` | ✓ purchase; ✗ **`Rise()` adds an invented timed litter rate (`Rates["litter"] = Rate(0,1)`) that the console does not have** (§5.8) |
| guest `+0x6c` | noted in `ParkVisitors.cs:702` as the entertainer timer | not modelled |
| idle arms | `GuestDecisionSchedule.NextAction`: arms 0 and 1 only; 2..5 return `None` | arms 2–5 are the hooks for heckle, bin/drop, vomit, prank |
| `HoldsLitter` (bin) | `RideCatalogue.HoldsLitter` (`:231`) | parsed, no behaviour |

**The stand-in to delete**: `ParkVisitors.Maintain` (`ParkVisitors.cs:587`), with `SecondsPerService`
(45 s), `AutoService`, `Serviced` and the doc block at `:474..:498`. It calls `Service()` on every dirty
toilet on a fixed timer. Replace with the handyman's state 0x33 completion (§3.7), which also stamps
the day and depends on handymen existing, on the 60 % candidate threshold and on the score of §3.5.
With no handyman hired, the console's toilets **stay dirty** and the advisor raises rule 101 at average
condition < 75.

**Missing entirely (grep of `core/` for litter objects, vomit, bins, effectors, entertainers,
handymen: none):**
1. A litter object pool (40, LIFO, vomit flag, claimant, variant model `litter1..3` / `puke`), with
   the four producers of §1.3, no decay, and the two-count save with random re-scatter on load.
2. Guest bin-seeking and dropping (arm 3), vomiting (arm 4 → state 0x1d → puke), pranks (arm 5 →
   hidden litter + `YellowStink` stink table of 10 + guard call).
3. The every-64-tick litter proximity penalty (§5.4) — note visitors.md's "Manhattan 2" is `< 2`.
4. Effectors (only flag 2 needs implementing; flags 1/4 are dead on PS2) and the guest watch state
   0x1c with its cooldown.
5. The heckle arm and the entertainer's Shocked state (needs area C's guard chase).
6. Handymen and entertainers themselves (area A's staff person, then §3 and §4).
7. The Single Bog screen fields (Users, Last Cleaned, Cleanliness) and the advisor variables 13
   (litter count), 31 (100 − average condition), 76/77 (prank / drop events).

---------------------------------------------------------------------------------------------------

## 8. Corrections to earlier notes

- **staff.md §3.4 "a guest within 3-D distance² < 2"**: the middle coordinate is an unassigned
  stack short; in practice the test is 2-D (§4.3).
- **staff.md §3.4 "go to the nearest entertainer"**: the guest is not routed; it stops and faces
  the entertainer (§5.2–5.3).
- **staff.md §3.1 "event 5 → drop everything"**: it does **not** release the litter claim (§3.8).
- **staff.md §3.1 "falls back to litter"**: only when no toilet qualifies; a refused toilet route
  does not fall back (§3.5).
- **staff.md area B "litter objects `0x15e1c8..0x15e3c0`"**: the class spans `0x15e1c8..0x15ea20`
  (vtable methods at `0x15e350..0x15e9f0`), and its update is empty.
- **staff.md §3.4 "producers of flags 1/4 not traced"**: there are none (census §4.4).
- **staff.md §3.3 "Pranksters … drop litter"**: the prank litter is never shown; the visible part
  is a `YellowStink` particle (§1.3, §1.5).
- **visitors.md** (`FUN_0020FB88` table): "every litter within **Manhattan 2**" → `< 2`, i.e. ≤ 1
  (MIPS `0x20ff00`). "nothing is yet known to register a zone" → the entertainer registers the only
  zones (§4.4).
- **ParkVisitors.cs:484..487** ("bumps the staff's own `+0x53`/`+0x54`"): those are `P`-relative
  **tiredness +5 (+10 if under 40)** and **morale +5 / −10**.
- **ParkSim/VisitorNeeds "Rates[litter]"**: no console counterpart (§5.8).
- **This file's §1.3 row 2 and §1.5** (found porting the prank, 2026-09-27): the stink is spawned ONLY when
  the prank's litter was allocated -- `beqz $a0, 0x20d1f0` at `0x20d148` skips both the place `0x15e2b8`
  and `0x1822d0`; the counters and `0x14d3e0` follow regardless (MIPS `0x20d13c..0x20d21c`).
- **§5.3 facing**: with the entertainer on the guest's own cell (dx = dz = 0) the rule gives **π**
  (`dz > 0` fails, MIPS `0x210890..0x2108b0`) -- the case the list above does not spell out.

---------------------------------------------------------------------------------------------------

## 9. Still unknown (tried)

- **Who sends event code 5** (the handyman's "drop everything" that leaks a litter claim): the 13
  constructions of event-record vtable `0x35a548` store codes 1, 2, 3, 4, 7, 9, 0xa (MIPS at
  `0x14bd38`, `0x18d7b8`, `0x18cf78`, `0x18d08c/0x18d0a4`, `0x18d164`, `0x18cfc0`, `0x1177cc`,
  `0x118044`, `0x140c90`, `0x14103c`) and `0x1180b0` stores 7; none stores 5. A subclassed record whose
  `vt+0xc` returns 5 was not searched for.
- **Order of the placed-object list `0x3952a0`** (toilet ties): its inserter was not read.
- **Tick rate**: 25 Hz is visitors.md's reading of the frame throttle; I only confirmed the counter
  increments once per `0x13add0` call.
- **Whether hidden litter is really invisible on screen**: inferred from the absence of the show call and
  from `vt+0xac` being the person show/hide slot; not observed in an emulator.
- **How often a handyman/entertainer returns to state 0** while idle: depends on the patrol leg and
  local wander (`0x1913b8`, area A); every patrol leg ends in state 0 (`0x1db970`, READ), local wander
  not read.
- **`0x17d558`** (what the litter model's fourth argument `DAT_002b7f10` selects): reads the model
  registry and `0x16a7e8`; not followed.
- **Map cell kind 7** (accepted by the litter loader with 2 = path and 4 = queue): not identified.
- **`0x10d228`**: averages the condition of an object's own list (`+0x348` count, `+0x3d0` entries);
  the owning object was not identified.
- **The entertainer costume variant** and the watch/perform animations on each costume (area A).
