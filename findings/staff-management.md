# Staff, area D: management, economy, strikes, researchers and the laptop (the "how" pass)

Researched 2026-09-27 by agent `sD`. Starting map: `staff.md` §4 and "Areas for the how pass → D".
Everything below was re-checked against the ELF; corrections to the what-pass are collected in §14.

Sources:
- `SLES_500.32` in Ghidra 12.1.2, own copy `~/ghidra_tpw/agent_sD`; decompiles `agent_sD/out/d1.c`
  (finance, advisor producers, training, UI draws, research, staff room screen) and `d2.c` (screen
  methods, research manager, rest/strike/save); the what-pass files `agent_staff/out/*.c`; the tool
  agent's `agent_tool/out/r2..r6.c` (tool framework, hire/patrol/grab tool objects).
- Raw MIPS from the full listing (scratchpad `full.s`), used for every constant, every signedness
  question (`div` vs `divu`) and every place the decompiler dropped an argument.
- ELF data read with `agent_staff/out/py/elf.py` (tables, vtables, strings).
- Disc: the port's own `TPW.PS2.Data.dll` from a scratch project `agent_sD/out/dscan` (text rows,
  advisor catalogue incl. voices, the advisor rule VM dump `agent_sD/out/rules.txt`, DBA feature flags).

**READ** = seen in decompile/MIPS/data. **INFERRED** = reasoned. Offsets are `C+` (the object the
staff vtable methods receive) unless marked `P+` (pool slot, `P = C − 8`), as in staff.md §2.3.

**Money.** Wage and cost tables hold display dollars. Every debit/credit multiplies by **10**; the park
purse `park+4` is in tenths (the port's `ParkFinances` unit; `Money.Format` divides by 10).

---------------------------------------------------------------------------------------------------

## 0. Short version

- **Candidates** are made once per park session (first `0x12a550`, freed at park teardown
  `0x150e80 → 0x12a5b8`), 5 per kind: pay grade `rand & 1`, motivation `rand % 60 + 20`. Firing returns
  the same person unchanged; trained levels are lost. Candidate records are **not saved** (INFERRED
  re-roll on load). **Motivation (`+0x18` → `C+0x4a`) is display-only**: no reader in the whole ELF.
- **Hiring is free.** Tool mode 1's debit is `tool+8 × 10`, and `tool+8` is only ever written 0
  (the hire cursor handler zeroes it every frame, `0x128794`). The drop needs a tile that passes
  `0x1e65b8`; the staff's hire date is the moment the laptop hands the person to the cursor.
- **Wages**: `wage[L] × kindMult × pct / 100`, `pct = min(100, daysEmployed × 100 / lengthOfPreviousMonth)`,
  0 for a staff member standing in state 0xf (striking). Paid at every month rollover **after** the
  strike check, and again (pro rata by the same formula) when fired, so a long-serving employee fired
  on day 1 costs a full extra month. WAGES_HIGH = wages above income in both of the last two months.
- **Training** costs `train[L] × mult` (next level only, offered while `L < 4`), resets tiredness to 0
  and morale to 100. Free in the free-build modes.
- **Staff Room**: go at tiredness ≥ 81 (only tested in state 0); nearest room by 3-D Manhattan, **no
  capacity limit**; rest = every 4th tick morale +1, tiredness −2, leave at 0. Without a room a tired
  staff member never works again (patrols forever; INFERRED from the READ loop). Kick-out does not
  reset tiredness, so the kicked staff walk straight back (INFERRED). `VAR_STAFFIN` has **no
  writer**: the room's particle/sound script never fires; the native side plays ambience 0xbc
  whenever the room's status is 2. The SPACE Laser Show carries the staff-room DBA bit and is
  treated as a staff room everywhere.
- **Strikes**: at each month change, per type with ≥ 2 staff: unhappy = average energy < 15 or
  average morale ≤ 14. Ladder 0→1 (UNHAPPY, spoken) →2 (strike) …→5 (strike) →0; a strike always
  ends at the next month change. Only UNHAPPY and HAPPIER have text or voice; VERY_UNHAPPY,
  STRIKING and STRIKE_END_BAD are silent on PS2.
- **Research budget** starts at 80 % and is **forced to 100 % whenever the Research screen is opened**
  (`0x1b54cc..0x1b54d4`); at 100 each research quantum also adds 6 tiredness.
- **Grab is dead twice**: the list-box builder for Grab (`0x15d718`) has no caller, and mode 18's
  table slot is 0. **Patrol areas** only bound idle wandering, and the stored rectangle is shifted
  by one cell whenever the first corner is the smaller one (§4.3).
- **Security Award**: weekly (days 1, 8, 15, 22, 29), once per park, when camera coverage
  `0x104ce0(0x40) > 80` — a figure that is 8× a percentage (§11.4).
- **All Staff can be wired**: the three bars step 32 rows from the one `InfoBars` element and
  `InfoValues` contributes only its column (§12.1).

---------------------------------------------------------------------------------------------------

## 1. The candidate database

### 1.1 Lifetime (READ)

| event | code | effect |
|---|---|---|
| first use in a park session | `0x12a550` (lazy singleton at `0x2b2a78`) → `0x12a4b0` → `0x12a878` | for kind 0..4, slot 0..`GetNumberOfStaff(kind)`−1 (= 5, `0x12b160`→`0x12b188`): `0x12a758(fac, kind, slot)` appends a 0x20-byte record at `fac+0xf4 + n·0x20`, `fac+0xf0 = n+1` |
| park teardown | `0x150e80` → `0x12a5b8` (only caller, `0x150fe8`) | frees the factory; next use rolls a new set |
| hire (activation) | subclass activations, e.g. `0x1783a8`: `0x12ad28(fac, kind, slot)` then `rec+0x1c = 0` | candidate taken |
| fire | `0x1dc6f0`: `vt+0x5c(C)` (candidate) `+0x1c = 1` | candidate back, **unchanged** |
| hire cancelled (Triangle in mode 1) | `0x128a90` → `0x12ae68`: `+0x1c = 1` | candidate back |
| save | `0x1c2968` writes DB categories 1..8 (research state) only | **candidate records not saved**; INFERRED that a loaded park re-rolls pay grades and motivations of the unhired and keeps hired staff's levels from the staff save record |

There is **no periodic refresh**: within a session the 25 candidates never change.

### 1.2 Record layout (READ, `0x12a758`, `0x12b5b8`, MIPS `0x12b5b8..0x12b628`)

| off | meaning | value |
|---|---|---|
| `+0` u16 | (upper half of word 0) | `0x12b528` ORs the slot index into bits 16–31; `0x12b520` (`lhu +2`) reads it back |
| `+2` u16 | slot index 0..4 | build menu passes it as the tool mode 1 argument |
| `+4` | name text row | tables `0x35c220` mech, `0x35c238` ent, `0x35c268` handy, `0x35c250` guard, `0x35c280` res (names in staff.md §1.1); read by `0x12b548 → 0x1dfa58` and `vt+0x7c` = `0x1dc240` |
| `+8,+9,+0xa` | bytes = 1 | shared DB accessors (`0x12b590..0x12b5a0`) |
| `+0xc` | 9 / 0xd / 0xb / 0xa / 0xc (mech / ent / handy / guard / res) | no staff reader found |
| `+0x10` | kind 0 mech, 1 ent, 2 handy, 3 guard, 4 res | tool mode 1 argument, wage/training multiplier index |
| `+0x14` | **pay grade = starting level**: `rand_u32 & 1` → 0 or 1 | activation copies `& 7` into `C+0x48` bits 0–2; hire screen shows `+1` |
| `+0x18` | **motivation**: `rand_u32 % 60 + 20` (`divu`, so 20..79) | hire screen bar; activation copies (clamped 0..100) into `C+0x4a` |
| `+0x1c` | 1 available / 0 hired | `0x12ad08` reads it |

`rand_u32` is `0x144870` (lagged add-with-carry generator, state at `0x2b68d0`); `rand(n)` is
`0x1448e0` = `rand_u32 % n` with **`divu`** (MIPS `0x1448f4`), so always 0..n−1.

### 1.3 Motivation `C+0x4a` (READ absence)

The only access in the ELF is the activation's own clamp (`0x1db6e4`). Scan: every `lb/lbu …, 0x4a(`
(1 hit) and `…, 0x52(` (21 hits, P-relative) whose function also touches the neighbouring staff fields
(`0x48/0x4b/0x4c`, P `0x50/0x53/0x54`) — the 7 survivors are ride/UI structures, not staff. Word loads
at `+0x48` (389 in the ELF) were not all checked for a bits-16..23 extract; the ones in staff code
(training `0x1ff610`, save `0x1dc0e8`) only use bits 0–2. **Conclusion: motivation only paints the
Hire screen bar.** The All Staff "Motivation" is a different number (§12.1).

---------------------------------------------------------------------------------------------------

## 2. Hiring

### 2.1 The Hire panel of Build & Hire (READ)

- Tab list (`0x197c40`, `+0x838 = 1` staff): for selector `s = 0..4` a tab is added **only if
  `0x14ca20(10, key[s]) > 0`** = `5 − hired count of that type`. Order, text (`0x365020`) and key
  (`0x365050`, the type code): **Guards 576 (3), Mechanics 622 (2), Cleaners 658 (5), Researchers
  63 (4), Entertainers 518 (1)**. The selector per shown tab goes to runtime table `0x397440`.
- Choosing a tab (`0x197f00`): candidate list at screen `+0x5e0` cleared, then `0x15d238(list, s)` →
  jump table `0x360c10` → kind (s 0→3, 1→0, 2→2, 3→4, 4→1) → `0x15d2a0(list, kind)`: slots 0..4 in
  order, **only candidates with `+0x1c != 0`**.
- Draw `0x199008` (§12.5): name, Pay Grade, Monthly Wage, Motivation bar.
- Confirm (`0x1982a0 → 0x197f48`): first a price check common to both tabs: `price =
  0x12bc10(DB, cat, idx)` of the list at `+0x2f4`; if `money − price×10 < 0` → sound 0xaf, refuse.
  On the staff tab `+0x2f4` is not rebuilt (only the tab list `+0x83c` is), so this compares against
  the build list's leftover selection; with no selection `0x12bc10` prints "Unknown Object Type,
  Cost = 0" (`0x35c3c0`) and returns 0 (INFERRED: normally no refusal; a stale build selection could
  refuse a hire — not tested). Then: `cand = +0x6c0[+0x62c]`, **`0x125460(toolmgr, 1, cand+2 (slot),
  cand+0x10 (kind))`**, tutorial event 0x1d, laptop closes.

### 2.2 Tool mode 1 (object `0x388ef0` in .bss, vt `0x35bd48`) (READ)

| slot | fn | does |
|---|---|---|
| enter `vt+0xc` | `0x128690(tool, cursor, slot, kind)` | allocate from the kind's pool (`0x14b008` mech, `0x14b290` ent, `0x14b0e0` handy, `0x14af30` guard, `0x14b1b8` res); allocation runs **activation** (so hire day, random tiredness 0..29 and morale 70..99 are fixed now); `+0x14 = C`, `+0x18 = kind`; freeze (`0x1dc780 → 0x1928b0`, `C+0x2c |= 0x40`) |
| cursor `vt+0x14` | `0x128760` | `0x126390` (affordable flag `+0xc`), **`sw $zero, 8($s0)` — cost 0** (MIPS `0x128794`), tool pos = tool manager `+0x2c` (1/256 cell), staff x/z = that, keep frozen |
| draw `vt+0x1c` | `0x128880` | empty |
| Cross `vt+0x2c` | `0x128918` | tile = `0x14e138(x>>8, z>>8)`; `0x1e65b8(tile)` false → sound 0xaf, stay. Else `0x128888`: unfreeze, state 0, goal depth 0, `vt+0x1d4` (logical 13), sound 0x12f, `0x126408` (debit `tool+8 × 10` = 0, sound 0x1f), mode 0 via `vt+0x4c(…,0)`; then if the raw type count (`0x14d6b0` etc.) is now 5 → advisor ADD_MAX (0x5c mech, 0x5a ent, 0x58 handy, 0x5e guard, 0x60 res; text, no voice); tutorial event 0xd |
| Triangle `vt+0x34` | `0x128a90` | free (`0x14b608`), candidate `+0x1c = 1`, release, mode 0, sound 0x130 |

**Placement rule `0x1e65b8(tile)`** (READ): tile byte `+0` ∉ {4, 5, 7, 8, 10, 12} and flag byte `+7`
has none of `0x01, 0x02, 0x10`. (Tile type 2 = path; the others' names are unknown, tool.md §13;
flag 2 = no-build, tiles.md.) Path is not required.

**Hire fee: 0** (READ): `tool+8` of `0x388ef0` has exactly one writer, the `sw $zero` above; the object
is in .bss (PT_LOAD filesz ends at `0x37e008`). The debit call still plays the cash sound 0x1f.

---------------------------------------------------------------------------------------------------

## 3. Firing (READ)

- Offered in the staff list box (§12.2) when `vt+0x1e4` is true: base `0x1dca60` = 1; mechanic
  `0x179430` = not in state 0x0e, 0x10, 0x11, 0x34, 0x36 (repairing/closing/opening/installing).
- `0x124300`: `S = selection+0x88`; `vt+0x1f4(S)` = `0x1dc6f0` (entertainer `0x12e768` also frees its
  effector): candidate `+0x1c = 1`, free the route slot, **`0x100c78(park, 0x1dc338(S) × 10)`** =
  `park+0x12d0 += amount` then debit the same amount (MIPS `0x100c84..0x100c90` keeps `a1`); then
  `0x14b608` (free: `vt+0x194` release, back to the pool); if no mechanics remain `0x1542a0` clears
  the ride upgrade list. No confirmation.

---------------------------------------------------------------------------------------------------

## 4. Patrol areas: tool mode 17

### 4.1 Tool (object `0x388f38`, vt `0x35bc38`) (READ)

`0x125460` handles modes 0x11/0x12 by calling `tool.vt+0x6c(tool, selection+0x88)` **before** enter;
for this tool that is `0x129068`: `tool+0x24 = staff`.

| slot | fn | does |
|---|---|---|
| enter | `0x128c48` | `+0x14 = 1`, `+0x18 = 0` (no first corner) |
| cursor | `0x128c68` | affordable flag; `tool+0..+7 = mgr+0x3c` = **cursor cell** {x, height, z}; if no first corner, first corner = cursor; freeze the staff |
| draw | `0x128ce8` | `0x1504f8(minX, minZ, 0xa5, 0, maxX−minX+1, maxZ−minZ+1)` ground highlight between cursor and first corner, inclusive |
| Cross | `0x128d98` | `+0x14 == 0` → 0xaf (never, enter sets it). First press: `+0x18 = 1`, corner `+0x1c/+0x20` = cursor. Second press: **corner.x += 1, corner.z += 1**, `0x1dc490(staff, corner, cursor)`, unfreeze, `0x126408` (debit `tool+8`×10 = 0 — `+8` never written, .bss — plus sound 0x1f), mode 0. Sound 0xdb on either press |
| Triangle | `0x128e78` | unfreeze; if a first corner was placed, forget it and stay; else leave (mode 0) |

### 4.2 Rectangle (READ `0x1dc490..0x1dc6e0`)

`C+0x44..+0x47` = s8 `x0, z0, x1, z1` = `min(a.x,b.x), min(a.z,b.z), max(a.x,b.x), max(a.z,b.z)` (low byte
of the cell shorts; park grids are ≤ 96 wide, tiles.md, so no wrap). Unset = `(0,0,−1,−1)`
(`0x1dc540`, at activation), "set?" = `word(C+0x44) != 0xffff0000` (`0x1dc698`).

Patrol (state 0xd, `0x1dc6b0 → 0x1dc558`): `w = x1 − x0`, `h = z1 − z0`; if `w ≤ 0` or `h ≤ 0` → state 5
(guest-style local wander). Else up to 10 tries: cell = `(x0 + rand(w), z0 + rand(h))`; needs
`0x149d20` (in bounds) and tile kind 2 (path, `0x1e6338`); route to the cell centre (`cell·256 + 0x80`),
flags 0x11 → success: state 0xb, mode 1, `+0x24 = now`, goal depth 0. All tries fail → state 5.
So the patrol cells are the half-open `[x0, x1) × [z0, z1)`.

### 4.3 The off-by-one (READ arithmetic, INFERRED as unintended)

With first corner `c` and cursor `u` (one axis), the drawn highlight is `min..max` inclusive, the stored
rectangle is `min(c+1,u) .. max(c+1,u)`, the walked cells `[x0, x1)`:

| case | highlighted | walked |
|---|---|---|
| `u == c` | `c` | `c` |
| `u < c` (first corner is the larger) | `u..c` | `u..c` (correct) |
| `u > c` (first corner is the smaller) | `c..u` | `c+1..u−1` — both edges lost; `u == c+1` gives an empty range → no patrol at all, yet the advisor counts the area as "set" |

Nothing but the idle wander reads the rectangle (staff.md §3.6, checked). The advisor reads it
(§11.3).

---------------------------------------------------------------------------------------------------

## 5. "Grab" (mode 18): dead on PS2, twice (READ)

1. **The option is never offered.** The staff list-box path (class 10, jump table `0x360c30[9]` =
   `0x15da74`) adds Set Patrol Area (`0x15d6e8`), Fire (`0x15d7c8`), Training and Zoom To. The Grab
   builder `0x15d718` has **no `jal`, no data pointer and no `lui/addiu` pair** anywhere (its condition,
   for the record: not in states 0x1a/0x0f/0x32, not walking with mode 5/0x11, and `vt+0x1ec`).
2. **The mode has no tool.** Mode table `0x2b2a10` (only reference: `0x1254e8`) holds 0 at index 18
   (and 19); `0x125460` then sets mode 0 with a null current tool (camera call `0x13e340` for mode 18
   still runs). The grab tool object `0x388f10` (vt `0x35bcc0`: enter `0x128b40` freeze + remember
   the position, cursor `0x128760` carry, Cross `0x128b88` drop with the hire tile test then the hire
   finish `0x128888`, Triangle `0x128c08` put back) is referenced only by its constructor stores at
   `0x125f70`/`0x1260c8`. INFERRED design: pick a staff member up and drop them elsewhere, as on PC.

Nothing else moves staff by hand: the hire tool is the only live user of the carry/drop pair.

---------------------------------------------------------------------------------------------------

## 6. Wages

### 6.1 Tables (READ `0x12b630`, ELF data)

`wage(kind, L) = W[L] × M[kind]`, `W = 0x35c410 = [50, 55, 65, 80, 100, 0]`, `M = 0x35c428` by kind
`[3, 1, 1, 2, 3]`. Display dollars per month:

| level (shown) | mechanic | entertainer | cleaner | guard | researcher |
|---|---|---|---|---|---|
| 0 (1) | 150 | 50 | 50 | 100 | 150 |
| 1 (2) | 165 | 55 | 55 | 110 | 165 |
| 2 (3) | 195 | 65 | 65 | 130 | 195 |
| 3 (4) | 240 | 80 | 80 | 160 | 240 |
| 4 (5) | 300 | 100 | 100 | 200 | 300 |

The candidate form (`L < 0` → use `+0x14`) is what the Hire screen shows; employees use `C+0x48 & 7`.
Nothing changes wages over time: `STR_STAFF_PAY_MESSAGE` (281, "annual pay rise") and
`STR_SET_PAY_RISE` (324) have no consumer found (the what-pass's `li 0x119` search; 324 not searched).

### 6.2 Pro-rated wage `0x1dc338(C)` (READ, MIPS)

```
if C+0x2f == 0x0f: return 0                               # standing on strike
days = calendarTotalDays (cal+0x10, 0x16b218) − C+0x40    # 0x1dc458; C+0x40 = total days at activation
len  = daysInMonth[month − 1] (0x16d2e8; month 0 → 11), table 0x361d88 = 31,28,31,30,31,30,31,31,30,31,30,31
pct  = days >= len ? 100 : days*100/len                   # signed div
return wage(kind, L) * pct / 100                          # display dollars, truncated twice
```

Only state 0x0f counts as striking: a striker still walking to the strike point is paid.

### 6.3 Month end (READ `0x16b060`, `0x100a18`, `0x1008b8`, MIPS `0x100a18..0x100b30`)

`0x16b060` (every frame, via the calendar) advances the day (`0x16b240`, 0xF0000 units a day); on a
**month change**, in this order:
1. `0x16c120` strikes (§9) — gated by the park-running flag `0x2b7288`;
2. `0x100a18(park)` finances, not gated:
   - `balanceHistory[i] = park+4` (`+0xbc`, `i = park+0x12bc % 144`);
   - 4 loan slots (`park+0xc + 0x2c·k`): repayment `min(+0x14, +0x18)` ×10 → `loans`;
   - **`W = 0x1008b8() = Σ 10 × 0x1dc338(C)`** over mechanics, entertainers, guards, researchers, handymen;
   - `park+0x12d0 += W`, `wageHistory[i] (+0xe3c) = W`;
   - **one debit `0x100698(park, loans + W)`**; `0x100750(park, 0)`; value history `+0x107c`;
   - `park+0x12bc += 1`, clear the next slots of every history ring.
3. If the balance is negative, the bankruptcy counter `cal+0x18` chain (messages 0xce+0x79, 0x7a, 0x7b).

`0x100698` refuses (debits nothing) only when `park+8 == 0` and `balance < amount`; the park constructor
`0x100470` sets `park+8 = 1` and no other writer was found (searched `lw …,0x60b0` + `sw …,8(` and
`jal 0x1005d8` + `sw …,8($v0)`; the save loader not checked). So in practice wages always come off and
may drive the balance negative. Free-build (`DAT_002a60b8`, or `0x154428()` = park index 2) debits
nothing.

**Pay cadence summary:** monthly, in arrears, whole months for anyone employed at least one full
previous-month length, pro rata by days for new hires; plus the same formula once more at firing.

### 6.4 WAGES_HIGH (READ, advisor rule 103 and producer `0x10e32c..0x10e3d8`)

Rule 103 (delay 60 days): `v49 == 1` and more than 1 day since the rule last failed → message 0x52
(text row 525, voices sp_298/sp_299). Producer:

```
v49 = (income(1)/10 < wages(1)/10) && (income(2)/10 < wages(2)/10)
wages(k)  = 0x101170(park,k) = wageHistory[(park+0x12bc % 144) − max(k,1)]    (0 if k >= period count)
income(k) = 0x100fb8(park,k) = incomeHistory +0x2fc[…]   (every credit through 0x100750)
```
i.e. wages exceeded all income in each of the last two completed months. The `/10`s are integer.

---------------------------------------------------------------------------------------------------

## 7. Training

### 7.1 Offer and screen (READ)

- The list-box entry "Training" (row 930, `0x360fc8`) is added only if **`L < 4`** and
  `*(int*)0x1497b0() != 0` (MIPS `0x15da8c..0x15dac4`; what that flag means is not traced).
- Choosing it in the Single Staff screen (the loop in `0x1c6b60` matches text 0x3a2) builds the
  Training screen `0x1ff4a8` and calls `vt+0x84 = 0x1ff578(screen, S)`: selection = S, `+0x2f8 = L`,
  one list entry text `0x36ba88[L+1]` (rows 110, 111, 112, 114, 116 = "Level 1..5"; index 5 = row 0
  "Build Track", unreachable), **target `0x3988a8[0] = +0x2fc = L+1`**. Tutorial events 0x40/0x41.

### 7.2 Cost and effect (READ `0x1dc2a8`, `0x1ff6f8`, `0x1ff610`)

`cost = T[L] × N[kind]` (current level L), `T = 0x35c440 = [250, 275, 325, 400, 500, 0]`,
`N = 0x35c458 = [3, 1, 1, 2, 6]`; **0 in free-build** (`DAT_002a60b8 || park index 2`).

| from level | mechanic | entertainer | cleaner | guard | researcher |
|---|---|---|---|---|---|
| 0 → 1 | 750 | 250 | 250 | 500 | 1500 |
| 1 → 2 | 825 | 275 | 275 | 550 | 1650 |
| 2 → 3 | 975 | 325 | 325 | 650 | 1950 |
| 3 → 4 | 1200 | 400 | 400 | 800 | 2400 |

Cross (`0x1ff6f8`): if `balance (park+4) < cost × 10` → sound 0xaf; else `0x1ff610`: target =
`0x3988a8[sel]` (sel = `+0x3c0` if `+0x5e0[sel]` set, else −1), focus the staff (`0x14bc28`), debit
`cost × 10`, **if target > L: morale = 100, tiredness = 0**, `L = target & 7`; sound 0x12f.

Effects of the new level are the per-level tables of the other areas (handyman times and speed,
mechanic time and speed, researcher work, entertainer watch time) plus the wage. There is no limit
on how often or how soon; the level cap is the list-box `L < 4` test. The trained level lives only in
the staff object (and its save record); firing forgets it (§1.1).

---------------------------------------------------------------------------------------------------

## 8. Tiredness, morale and the Staff Room (the parts D owns)

### 8.1 Every writer of `C+0x4b` tiredness / `C+0x4c` morale outside the job code (READ)

| where | when | tiredness | morale |
|---|---|---|---|
| activation `0x1db618` | hire / load | `rand(30)` = 0..29 | `70 + rand(30)` = 70..99 |
| walking `0x1db970` (state-2 handler, not arrived) | every 4th tick (`tick & 3 == serial & 3`) | +1 (cap 100) | |
| `0x1dba90` tired check, tiredness ≥ 91 | every 4th tick | | −1 — **unreachable**: all five callers are the state-0 find-work functions (`0x1413e0, 0x1457e0, 0x178dd8, 0x1b60a8, 0x12de40`) and at state 0 a tiredness ≥ 81 has already sent them to rest |
| resting `0x1dbf50` (state 0x32) | every 4th tick | −2 (floor 0) | +1 (cap 100) |
| training `0x1ff610` | on purchase | = 0 | = 100 |
| researcher quantum `0x1b61c0` | each research tick | `+ (budget − 80) / 3` (`divu`, truncated to s8): 0 at 80, **+6 at 100** | |

Job-specific changes are areas B/C (staff.md §3). Tick = the global counter `0x1c4930` (area A).

### 8.2 Going to rest (READ `0x1dba90`, `0x1dbb80`, `0x1db970`, `0x1db768`)

- `vt+0x1bc` = `0x1dba90`, called by each type's find-work at state 0: striking → §9.4; else if
  tiredness ≥ **81** → goal depth 0, state **0x31**, return 1 (the job search is skipped).
- State 0x31 `0x1dbb80`: over all placed objects (`0x14cd30`), keep those with **DBA `+0x2e` bit 1**
  (`0x1308c8`) and **status byte `+0xa2 != 0`** (the placed-object status, lifecycle.md §3);
  distance = `|dx| + |dy| + |dz|` of the `vt+0x74` cell positions (height included); strict `<` keeps
  the first of equals. None → target 0, state 0xd (patrol). Found → mode 0x11, target = room, route
  (flags 0x11) to `(room cell + entry offset 0x1e1760)·256 + 0x80`; accepted → push 0x31, state 0xb,
  `+0x24 = now`; refused immediately → stays 0x31 and retries next tick; route fails later (event 2)
  → state 0xd.
- Arrival (`0x1db970`, mode 0x11) → state **0x32**, model hidden (`vt+0x2c(0)`). The room is not
  re-validated on arrival.
- **Capacity: none.** No count is taken anywhere in the search, the arrival or the rest.
- Resting until tiredness ≤ 0 takes `ceil(T/2)` quanta of 4 ticks (41 from 81, 50 from 100); then
  `0x1dbfd0`: show the model, target 0, state 0xd.
- **No Staff Room**: state 0x31 finds nothing → patrol (walking adds tiredness) → state 0 → ≥ 81 again
  → … The staff member never takes another job and drifts to 100 (INFERRED from the READ loop). This
  is also how the "average energy < 15" strike trigger (§9) is normally reached.

### 8.3 Kick-out (READ `0x124478..0x124678`, `0x14aa90`, `0x15d850`)

List box of a selected Staff Room (feature class, `0x15da18 → 0x15da48 → 0x15d850`): one entry per type
with resting staff in **this** room, order Mechanics (455, `0x124478`), Researchers (523, `0x1244f8`),
Cleaners (446, `0x124578`), Entertainers (520, `0x1245f8`), Guards (830, `0x124678`). Each walks its
type's active list and calls `0x1dbfd0` on those in state 0x32 whose target is the room. **Tiredness
is not touched**, so anyone still ≥ 81 heads straight back to the nearest room at their next find-work
(INFERRED consequence). `0x14aa90(room, type)` counts `state 0x32 && target == room`.

### 8.4 The room itself: sound, script, Laser Show (READ)

- Feature vtable `0x35dc70`: `vt+0x164 = 0x130510` (status change): after the base `0x1e1238`, if the
  new status (`vt+0xa4`) is **2** and the object is a staff room → start looping sound bank 8 event
  **0xbc** at the room (handle `+0xb8`), debug "START Staff event"; `vt+0x10c = 0x130498` stops it
  ("STOP Staff event"). So the ambience plays whenever the room is open, occupied or not.
- **`VAR_STAFFIN` has no writer.** Native writes to script variables go through `0x1c0e28(inst, var,
  value)` (9 call sites, all constant indices) and `0x1c1068` (1 site, rides); index 0 is written only
  by `0x1fa1d8`, reached from the ride queue's guest-offer path (`0x1fa368` ← ride update `0x116768`
  and `0x1d2738`; delayed form `0x1fa2e8` ← machine sweep `0x1f6a40`). The staff-room `.rss` has no
  SPAWNCHILD/parent link, so no script can write it either. The script's `P_EFFECT_BrewUp` particle
  and `EVT_STAFF_ROOM` sound therefore never start on PS2 (INFERRED from the complete writer set).
- **The Laser Show is a staff room.** SPACE key 356 "Laser Show" (1×1, cost 200, research 200) has
  flags `0x02` in the DBA (dscan). Every staff-room test is that one bit (`0x1308c8`): rest search,
  kick-out list, `0x14aa90`, advisor count `0x103970(0x80)`, coverage `0x104ce0(0x80)`, and the 0xbc
  ambience. INFERRED data bug; a port should reproduce it deliberately or flag it.
- `STR_ADVMES_ADD_STAFFROOM_UNCONNECTED` (row 927) is used by no message record and no `li 0x39f`.

---------------------------------------------------------------------------------------------------

## 9. Strikes

### 9.1 State (READ, calendar singleton `0x16ae90`, 0x32c bytes)

Per type code `t` (1 ent, 2 mech, 3 guard, 4 res, 5 handy) at `cal + 0x304 + (t−1)·8`: `u32 stamp`
(month index 0..11 last processed), `u8 stage` (`+0x308`), `u8 striking` (`+0x309`). Getter
`0x16c988(cal,t)`, clear `0x16c9a0`. Saved by `0x16cb40` (stamps, stages at save `+0x21..+0x25`,
striking bitmask `+0x26`), loaded by `0x16d030`.

### 9.2 Monthly check `0x16c120` (READ)

Runs on the month change if `DAT_002b74b0 != 0 || 0x151258()` (`0x2b7288`, set to 1 during park start
`0x151748`, cleared `0x1512c4`; `0x2b74b0` is cleared at `0x1515e8` and its only setter `0x14e278` has
no caller or pointer — INFERRED a debug switch). For each type in order ent, mech, guard, res, handy
**that has at least one staff member**, and whose stamp ≠ current month:
- stamp = month;
- if striking: clear the flag and post `base + 0x2a` (STRIKE_END_BAD, silent); **the ladder is not
  run this month**;
- else `0x16c2a0(t)`.

`base = 0x361d98[t−1]` = ent 3, mech 0, guard 2, res 4, handy 1, so e.g. UNHAPPY = 0x16 + base gives
0x16 mech, 0x17 handy, 0x18 guard, 0x19 ent, 0x1a res.

### 9.3 Unhappy test `0x16c500` and ladder `0x16c2a0` (READ, MIPS `0x16c30c..0x16c4d8`)

```
if DAT_002b74b0: unhappy                                        # debug: everyone strikes
n = staff of the type
if n >= 2 and (Σ(100 − tiredness)) / n < 15: unhappy            # signed div; debug print "strikev, %d %d"
if n >= 2 and (Σ morale) / n <= 14:        unhappy               # "strikeh, %d %d"
```

| stage | unhappy | not unhappy |
|---|---|---|
| 0 | → 1, UNHAPPY (0x16+b, text + voice) | stays 0, strike flag cleared |
| 1 | → 2, **strike on**, VERY_UNHAPPY (0x1b+b, silent) | → 0, HAPPIER (0x25+b, text + voice), flag cleared |
| 2, 3, 4 | → 3, 4, 5, strike on, STRIKING (0x20+b, silent) | → 0 silently, flag cleared |
| 5 | → 0, strike off, STRIKE_END_BAD (0x2a+b, silent) | → 0, flag cleared |

A single employee of a type never triggers it (n ≥ 2). Text and voices from the catalogue: only
UNHAPPY (e.g. 0x16: row 70, sp_095..097) and HAPPIER (0x25: row 798, sp_140..142) carry either;
VERY_UNHAPPY, STRIKING and STRIKE_END_BAD have row 310 and no voice, so **a strike starts and ends
silently**.

Persistent grievance, month by month (INFERRED from the two READ paths): M1 UNHAPPY; M2 strike;
M3 back; M4 strike; M5 back; M6 strike; M7 back; M8 strike; M9 back; M10 silent reset; M11 UNHAPPY; …

### 9.4 What a striker does (READ `0x1dba90`, `0x1db808`, `0x1db970`, `0x1db900`)

Only at its next find-work (state 0), so a staff member mid-job or resting finishes first:
`vt+0x1dc` (drop the target, `0x1dc9c8`), state 0x1a → `0x1db808`: up to 10 tries, target =
`0x1497c0()` (random point near the park-entrance entry `+0x10/+0x11`), route flags 0x23 → mode 5,
state 0xb; all fail → retry next tick. Arrival (mode 5) → state **0x0f**, logical 15 (the strike
animation). `0x1db900` each tick: once the type's flag is clear → state 0, goal depth 0, logical 13.
While striking: no tiredness or morale change, no wage (§6.2), skipped by Call Mechanic and the
breakdown advisor (area C); saved with `level | 0x80`, reloaded straight into 0x0f.

Order at the month change: strikes first, then wages. A strike that starts at this change is paid in
full (nobody is in 0x0f yet); the month a strike covered is unpaid for those who reached 0x0f
(they are still in 0x0f when the ending change pays).

---------------------------------------------------------------------------------------------------

## 10. Researchers and the research manager

### 10.1 Manager (READ `0x1b6640`, `0x1b6798`, `0x1b6a38`, `0x1b7388`; singleton `0x2e7540`, 0xac bytes)

| off | meaning |
|---|---|
| `+0` | 1 at init; set 1 when a project completes (`0x1b74a0`) |
| **`+4`** | **budget %**: 80 at init (`0x1b6640`); **set to 100 by the Research screen constructor** `0x1b5410` (`0x1b6848(mgr, 100)` at `0x1b54cc..0x1b54d4`); save/load as a byte (`0x1b66c0`/`0x1b6720`). Its getter `0x1b5910` has no caller |
| `+0xc + 0x1c·i` (i 0..4) | project slot: `+0` weight (set 100 by `0x1b6850`), `+4` required work × 4096 (`0x1b7378`, from DBA research work `0x12b840`), `+8` progress, `+0xc` active, `+0x10` complete, `+0x14` item index, `+0x18` category |
| `+0x98 + 4·i` | per-slot group threshold used when starting a project (`0x1b6880`), not traced further |

### 10.2 A researcher's contribution (READ `0x1b60a8`, `0x1b61c0`, `0x1b6a38`, `0x1b7388`)

- Find-work: if not tired/striking, `rand(10) < 3` → state 0x1f (one tick), else patrol (sound 0xa8).
- State 0x1f: logical 16; for **each active project** `progress += R[L] × budget × 4096 / (nActive × 100)`
  with `R = 0x366150 = [20, 30, 35, 40, 43]`; when `progress ≥ required` → `0x1b74a0` (complete, advisor
  0x4b..0x4f, or 0x7e "new upgrade, hire a mechanic" when there are none); tiredness per §8.1; state 0;
  sound bank 8 event 0x8a. Percent shown = `progress × 100 / required`.
- So a quantum yields `R[L] × budget/100 / nActive` work units per project, and the rate depends on
  how often the researcher returns to state 0 (70 % of decisions are patrol walks) — INFERRED.
- There is no money cost for research in this code (no debit near the manager); INFERRED that the PC
  budget slider was reduced to the fixed 80 → 100 switch.

---------------------------------------------------------------------------------------------------

## 11. Advisor conditions using staff

### 11.1 Rule-VM state variables fed by staff (READ, producer switch `0x10de38`, table `0x359860`)

| var | producer | meaning |
|---|---|---|
| v6, v7, v8, v9, v10 | `0x14d830, 0x14d7c0, 0x14d750, 0x14d7f8, 0x14d788` | count of ent, mech, guard, res, handy, **minus one if that type is the person held by the hire tool** (`0x14d6e0`: tool mode 1 and held `vt+0xac` == type) |
| v11 | sum of v6..v10 | all staff |
| v12 / v13 | `0x14d690` / `0x14d208` | guests in the park / litter items |
| v18, v19, v20 | `0x103970(0x20 / 0x80 / 0x40)` | placed features with status ≠ 0 that are toilets / **staff rooms** / **cameras** |
| v33..v36 | `0x1053a8(1,2,4,8)` (mech, handy, guard, ent) | % of that type **without** a patrol area (`100 − set×100/n`, 0 when all set or none employed) |
| v37..v40 | `0x104fb0(1,2,4,8)` | patrol **coverage** of path, §11.3 |
| v41..v45 | `0x105538(1,2,4,8,0x10)` (mech, handy, guard, ent, res) | training % = `ΣL × 100 / (n × 4)` |
| v46 | `0x16c988(cal, 2)` | mechanics striking (no rule reads it) |
| v49 | §6.4 | wages > income two months running |
| v55 | `0x105798` = max over types of `0x105688` | **highest tiredness of any staff member** |
| v56, v57, v58, v59, v76, v77 | event counters 0, 1, 2, 3, 20, 21 (`0x1073c0` → `0x10ddd8`) | 0/1 ride breakdown events (`0x116518`, `0x1165a0`); 2 and 20 bumped together in a guest prank (`0x20d1fc/0x20d20c`, SBOMB for 20); 21 litter prank (`0x20d4fc`); 3 += 8 at guest `0x21059c` (not identified) |

### 11.2 Staff rules in `headers.ass/opcodes.ass` (READ, dump `agent_sD/out/rules.txt`)

"Voice" = the catalogue has a nonzero sound for it; "silent" = text row 310 and no voice.

| rule | delay (days) | condition | message |
|---|---|---|---|
| 1 | 60 | v20 == 0, v8 != 0, v52 < 10 | 0x0c BUILD_CAMERA (text) |
| 48 | 30 | v19 == 0, **v55 > 15**, elapsed > 50 | 0x80 NEED_STAFF_ROOM (text + voice) |
| 49 | 100 | v12 > 20, all counts 0, elapsed > 60 | 0x81 (silent) |
| 51 | 720 | v4 > 35, v11 > 0, v41..v45 all < 1, elapsed > 10 | 0x83 TRAIN_STAFF_GENERAL (text + voice) |
| 52–56 | 400 | trained % > 90 and quiet counters | CONGRAT_*_TRAINED (silent) |
| 60, 65, 71, 77, 81 | 30000 | v4 > 24, count ≠ 0, training % < 1 (81 also every research % < 100) | TRAIN_MECH/HANDY/GUARDS/ENT/RES (text) |
| 61, 66, 72, 78 | 360 | v4 > 23/24, count ≠ 0, **no-area % > 99** | SET_*_AREA (text) |
| 63 | 30 | v10 == 0, v13 > 10 | 0x04 HIRE_HANDYMEN_1 (text) |
| 69 | 90 | v8 == 0, v58 > 5; then v58 = 0 | 0x05 HIRE_GUARDS_1 (text) |
| 73 | 300 | v8 ≠ 0, v20 == 0, v39 < 60, v58 > 4; v58 = 0 | 0x50 LAX_SECURITY (silent) |
| 75 | 180 | v6 == 0, v12 > 30 | 0x06 HIRE_ENTERTAINERS_1 (text) |
| 80 | 360 | v4 > 3, v9 == 0, v21 > 99 | 0x07 HIRE_RESEARCHERS_1 (text) |
| 101 | 60 | v10 == 0, v31 > 25 | 0x48 DIRTY_TOILETS_1 (text) |
| 103 | 60 | v49 == 1 | 0x52 WAGES_HIGH (text + voice) |
| 59/64/70/76/82, 62/67/74/79/83 | | at 5 staff / many staff | MAX_*_V2, LOTS_* (silent) |

Not reached by any rule: HIRE_MECHANICS_1 (0x03, also silent) and the four COVERAGE messages
(0x09/0x0b/0x0e/0x10, silent). Posted directly, outside the VM: ADD_MAX_* (§2.2), the strike ladder
(§9), the breakdown chooser `0x103658` (0x37/0x38/0x39, area C), upgrades 0x7c/0x7d (`0x1d5c00`, silent),
the Security Award (§11.4).

### 11.3 Coverage `0x104fb0(mask)` (READ)

Grid of 4×4-cell blocks (`W/4 × H/4`, bytes at `*0x2a6a7c`): a block is "path" (=1) if any of its four
samples `(x, x+2) × (z, z+2)` is a path tile. For each staff of the type with an area set, OR 2 into
every block `x0/4..x1/4 × z0/4..z1/4` (inclusive). Result = `blocks==3 × 100 / blocks∈{1,3}`, 100 if
there are covered blocks and no more path blocks than covered, 0 if none covered. Only v39 (guards)
is used, by the silent rule 73.

### 11.4 Security Award (READ `0x16b060`, `0x16bc70`, `0x104ce0`)

- Weekly: when the day-of-month (0-based `cal+0xc`) changes to a multiple of 7 (days 1, 8, 15, 22, 29).
  `0x16bc70` returns early unless the park has a goals record (`0x16c0e8()`) and `0x153410() == 0`.
- Once per park: `cal+0x28` bit 0 clear and **`0x104ce0(0x40) > 80`** → `0x16bb38(cal, 0xa0)`
  (`0x1c38c0(1)`, INFERRED "award a gold ticket", then message 0xa0 GOLD_TICKET_BROTHER, text row 852,
  voice ADDS_04) and set the bit.
- `0x104ce0(mask)`: bit grid of **16×16-cell blocks**, `ceil(W/128)` bytes per row, `ceil(H/16)` rows,
  one bit per block (`bit = (x>>4)&7` of byte `x>>7`); each placed feature with status ≠ 0 matching
  the mask (0x20 toilet, 0x40 camera, 0x80 staff room, 0x10 any) sets its block's bit; result =
  `popcount × 100 / bytes` (low 16 bits). With 8 blocks per byte this is **8 × the covered fraction**
  (INFERRED unintended). All PS2 grids are ≤ 96 wide, so one byte per row: Jungle 64×76 → 5 rows,
  20 usable blocks, needs cameras in **≥ 5 distinct 16×16 blocks**; the 4-row parks (H 52..62) need
  **≥ 4** (grid sizes from findings/psx-vs-ps2-rides.md).

---------------------------------------------------------------------------------------------------

## 12. Laptop screens (READ; layouts from the scene files, coordinates `(col, row)`)

Values: `int` = `0x142b68`, `$` = `0x142908` (money), bars = `0x115590/0x115620` objects with the value
in 16.16 at bar `+0x18`, size 72×22 from the scene, 0..100 (INFERRED from the training bar's explicit
min 0 / max 100 at `+0x320/+0x324`).

### 12.1 All Staff `main_i_staff` (ctor `0x10bb68`, vt `0x359250`, draw `0x10c138`, input `0x10bf50`)

- Opened from Information → Staff Information (`0x1c6980`, tutorial 0x3f; menu condition
  `0x153410()==0` and any staff).
- **Type list** (`+0x8ac == 0`): rows for types that have staff, in order Entertainers 68, Mechanics
  864, Guards 421, Researchers 887, Cleaners 69 (`0x37e228[n]` = 0..4).
- **Per-staff view** (`+0x8ac != 0`): list filled by `0x15c7c8(list, class 10, sel)` = every map object
  (`0x14da50` list) whose class is 10 and type code (`vt+0xac`) is `sel + 1`; `Item`/`ItemArrows` step
  through it (the model window shows the selected staff member — INFERRED, not traced).

| row (y) | label | value (x) |
|---|---|---|
| 115 | — | name `vt+0x7c` at `Item` (45) |
| 175 | 683 Skill Level | bar at InfoBars (215, 175): `L × 25` |
| 207 | 827 Motivation | bar (215, 207): `((100 − tiredness) + morale) / 2` (`0x1dc428`) |
| 239 | 363 Tiredness | bar (215, 239): tiredness |
| 271 | 669 Time Employed | text at **InfoValues col 250**: `days/28 "." (days%28)×10/28` then "mnth" (row 10) if days == 28 else "mnths" (row 349) — 28-day months, no separator added |
| 303 | 886 Monthly Wage | `$ wage(kind, L)` at **InfoBars col 215** (full wage, not pro rata) |

Labels start at TextOptions (45, 175) and step `0x20` (`DAT_002aa89c`); the three bars step the same 32
from the single InfoBars frame; **InfoValues' authored row (220) is never read** — `0x10b980` stores
only its column (`DAT_002aa8d4`). "Overall Motivation" (`0x10c0b0`, the list average of `0x1dc428`) is
computed each draw and **discarded** (MIPS `0x10c240..0x10c248`); rows 555 and 676 (Status) are not
drawn.

### 12.2 Single Staff `main_i_staff_opts` (ctor `0x1d8ee8`, vt `0x368e70`, set `0x1d8fe0`, draw `0x1d9088`, input `0x1d9038`)

- Opened for one staff member (`0x1c6b60`, tutorial 0x40): selection+0x88 = S; list box
  `0x15d958(list, S)`. Draw: name at Staffname (45, 115); the option texts at TextOptions (45, 175),
  stepping `DAT_002e9da8`, highlighted = `+0x494`. **No statistics on this screen.**
- Options for a staff member (class 10), in order: **Set Patrol Area** (79, always) → mode 17;
  **Fire** (906, if `vt+0x1e4`) → §3; **Training** (930, if `L < 4` and `*0x1497b0() != 0`) → §7;
  **Zoom To** (391, if `*0x1497b0() != 0`) → camera to `vt+0xcc` (`0x124360`). Grab is never added (§5).
  The same builder serves the in-park list box (`0x13d008` also calls `0x15d958`).
- Action table `0x360f70`: `{text, fn}` pairs; "Training" `0x1246f8` only prints "STAFF TRAINING MENU",
  the screen loop opens the Training screen by comparing the chosen text with 930.

### 12.3 Training `main_i_staff_opts_training` (ctor `0x1ff4a8`, vt `0x36b9f8`, set `0x1ff578`, draw `0x1ff830`, input `0x1ff6f8`)

| (col, row) | shows |
|---|---|
| Item (45, 115) | name |
| InfoText (45, 175) | the one list entry "Level L+2" (text `0x36ba88[L+1]`) |
| InfoText + 32 / 64 / 96 | labels 118 Training Cost, 886 Monthly Wage, 709 Skill Level |
| CostWageVal (250, 207) | `$ trainingCost(L)` (§7.2) |
| (250, 239) | `$ wage(kind, min(L+1, 5))` — the wage after training |
| SkillLevelBar (215, 271) | bar `min(L+1, 5) × 25` |

`TrainingVal` (250, 125) is bound but not drawn by `0x1ff830`. Cross = buy (§7.2).

### 12.4 Staff Room (ctor `0x1dcaa0`, vt `0x369700`, set `0x1dcb68`, draw `0x1dcca0`, input `0x1dcbc0`)

Opened for a selected staff-room feature (`0x1c6e78`, object from its caller's `+0x78`). Title 589
"Staff Room"; the room's model; `0x1dcc18` counts resting staff per type 1..5 with `0x14aa90`; draw:
for each type with a count, label (68, 864, 421, 887, 69) and the count (`int`, label col + 0xa0),
rows stepping 20; if nobody → "Empty" (55). List box = the kick-outs of §8.3 plus the common feature
entries (`0x15d570`, not traced).

### 12.5 Hire `main_bh_staff` (draw `0x199008`)

| element | shows |
|---|---|
| StaffSelect (45, 125) | candidate name (`0x12b548`) |
| TextItems (45, 171) + step | 773 Pay Grade, 886 Monthly Wage, 833 Motivation |
| NumericItems | Pay Grade `int (+0x14) + 1` (1 or 2); Monthly Wage `$ W[+0x14] × M[kind]` |
| MotivationSlider (215, 240, 72×22) | a **bar**, value `+0x18` (20..79) |

Tabs and candidate list: §2.1. No hire fee is shown or charged.

---------------------------------------------------------------------------------------------------

## 13. Port hooks (read-only survey of `~/tpw-coasters` at `adeeeb5`)

| console mechanism | port hook today | missing |
|---|---|---|
| month / week / day rollover `0x16b060` | `ParkClock.Advance(…, out dayRolled, out monthRolled, out yearRolled)` (`core/TPW.PS2.Data/ParkClock.cs:88`), called from `game/Viewer.cs:10008` **with all three outs discarded**; the calendar lives in the Viewer (`Viewer.cs:80`), not in `ParkSim` | subscribe: on `monthRolled` run strikes then wages (that order); on `dayRolled && Day % 7 == 0` run the weekly awards |
| previous-month length `0x16d2e8`, total days `cal+0x10` | `ParkClock.DaysInPreviousMonth` (`ParkClock.cs:82`), `ParkClock.TotalDays` (`:71`) | nothing: hire day = `TotalDays` |
| debit / credit `0x100698` / `0x100750` | `ParkFinances.Debit` / `Credit` (`ParkFinances.cs:97/86`), `Unlimited` = `park+8` (`:68`) | wage accumulator `+0x12d0`, the 144-slot period rings (`+0xbc` balance, `+0x2fc` income, `+0xbfc` spending, `+0xe3c` wages), period counter `+0x12bc`, month end `0x100a18` incl. loans — WAGES_HIGH needs the income and wage rings |
| opening balance | `ParkFinances.OpeningBalance = 300_000` (`:56`), labelled "not a decoded constant" | it is READ: park start calls **`0x100690(park, 0x493e0)`** at `0x151720` (the constructor's `0x186a0` at `0x1004c8` is overwritten) |
| staff-room / camera / toilet feature bits | `AssetResourceDatabase.RawFeatureFlags` (`AssetResourceDatabase.cs:101`); bit 0 already used (`ParkSim.cs:34,44`) | bit 1 (staff room, incl. Laser Show), bit 3 (camera) tests on placed features |
| advisor rule VM | `AdvisorRules.Evaluate` (`AdvisorRules.cs:67`) | the scheduler and every producer of §11.1; the event counters |
| advisor text / voice | `AdvisorCatalogue` (`AdvisorCatalogue.cs:25`) | posting for the direct messages (§11.2 last line) |
| Hire screen | spec `LaptopScreen.Hire` (`LaptopScreens.cs:123`, rows 773/886/833 correct); `Viewer.cs:3612` prints "hire has no staff to list" | candidate DB (§1), tabs (§2.1), tool mode 1 (§2.2) |
| All Staff | `LaptopListScreens.Staff` `Complete: false` (`LaptopListScreens.cs:71`); `InfoScreenFor` row 4 returns null (`Viewer.cs:3538`); held-back notes `LaptopScreens.cs:273`, `findings/laptop-screens.md` "All Staff" | both held-back questions are answered in §12.1 (bar stepping, InfoValues column only, source list `0x15c7c8`) |
| Single Staff, Training, Staff Room | none | §12.2–12.4 |
| patrol tool, hire tool | no tool-mode framework (`core/TPW.PS2.Data/PathTool.cs:23` and `game/Viewer.TrackAddons.cs:29` are separate tools) | modes 1 and 17 |
| research manager | none (only DBA `ResearchWork`/`ResearchGroup`, `AssetResourceDatabase.cs:30`) | §10 |
| main-menu gating | `LaptopMainMenu` (`LaptopMainMenu.cs:70` Hire, `:93` Staff Information) | the "any staff" predicate and the Hire tab predicate `5 − count > 0` |

---------------------------------------------------------------------------------------------------

## 14. Corrections to staff.md (READ)

- §4.5 debug prints are swapped: `0x361d38` = "strikev" (the energy test), `0x361d48` = "strikeh" (morale).
- §4.5 "if striking → just clear the flag": it also posts STRIKE_END_BAD (`0x16c120`), and skips the ladder.
- §2.3 "`+0x4a` no reader found in the staff code": none in the **whole ELF** (§1.3).
- §3.6 "tiredness ≥ 91 every 4 ticks morale −1": unreachable (§8.1).
- §4.6/§5 "Grab sets tool mode 18": the Grab option is never put in any list box (§5).
- §4.1 "whether a hire fee is ever set is not traced": it is always 0 (§2.2); the build menu's
  pre-check is a different price (§2.1).
- §4.3 is right; add: the wage debit shares one call with loan repayments, and strikes are processed
  before wages at the same month change.
- §4.4 "index 5 reads the table's terminating 0": unreachable, training is offered only below level 4.
- §4.8 `0x105688` is max tiredness per type; the advisor uses the max over all types (`0x105798`, v55).
- §1.6 "VAR_STAFFIN writer not found": there is none (§8.4).

---------------------------------------------------------------------------------------------------

## 15. Still unknown (tried)

- What `*(int*)0x1497b0()` means (gates Training and Zoom To): read the builder only.
- The build menu price pre-check on the Hire tab (§2.1): `+0x2f4`'s selection when only the staff tab
  was used was not traced; no hands-on test.
- `0x153410()` (`0x2b72a8`) and the park goals record `0x16c0e8()` that gate the Security Award: not
  identified (laptop-screens.md has the same open question).
- `0x1c38c0(1)`: INFERRED gold-ticket award, not read.
- Advisor v52 (rule 1's `< 10`): producer is the counter-default path with a negative index (reads
  object `+0x96`); meaning not resolved. Counter 3 (v59) producer at guest `0x21059c` not identified.
- The research slot table `mgr+0x98` and the project-start rules (`0x1b6880`, `0x1b7130`): research
  area, not traced.
- `STR_SET_PAY_RISE` (324): no immediate search done.
- Whether the save/load of the park object restores `park+8` (the spend-anything flag): loader not read.
- Tick rate of the "every 4th tick" quanta: area A (`0x1c4930`).
- `0x397440` (hire tab → selector) is filled at runtime by `0x197c40`; nothing else writes it (not searched).
