# Staff (PS2): what they are made of

> Research notes from the staff "what" and "how" passes (2026-09-27), copied from the research
> agents' notes. Areas: A `staff-person.md`, B `staff-handymen-entertainers.md`,
> C `staff-mechanics-guards.md`, D `staff-management.md`. Paths under `~/ghidra_tpw` are the
> research scratch on the build box and are not part of this repo.

Researched 2026-09-27 (agent `staff`), the "what" pass. The "how" pass should start from the grouped
list in the last section.

Sources:
- the staff's files on the PS2 disc (`DATA.WAD:/Chars/*`, the four world WADs' staff-room features,
  `MENUS/main_*staff*.sce`, `/AUDIO/GLOBAL/STAF*`), read with the port's `Disc`/`WadArchive`/
  `AssetResourceDatabase`/`TextDatabase`/`AdvisorCatalogue`/`Animation`/`NativeLogicalAnimationTable`/
  `SoundCatalogue` through a scratch project `~/ghidra_tpw/agent_staff/out/staffscan` (references a
  copy of the prebuilt `TPW.PS2.Data.dll`; nothing built in the repo). Extracted scripts and scenes:
  `~/ghidra_tpw/agent_staff/out/disc/` (stay on this box). Full WAD listing `out/allwad.txt`.
- `SLES_500.32` decompiled in Ghidra 12.1.2 (own copy `~/ghidra_tpw/agent_staff`), outputs in
  `~/ghidra_tpw/agent_staff/out/`: `s1.c` (the five staff classes, the staff base, CStaffDatabase),
  split into `staffbase.c`, `handy.c`, `mech.c`, `guard.c`, `ent.c`, `res.c`; `s2.c` (pools, lists,
  feature getters, calendar/strike manager `mgr.c`, DB factory, ride job lists, save/load);
  `s3.c` (hire tool, wages, UI screens); `s4.c` (hand/hover, guest crime, ride service hooks).
- vtables and tables read straight from the ELF (`out/py/elf.py`, dump in `out/vt_dump.txt`);
  load-bearing constants checked in raw MIPS with `r5900dis.py` (addresses given where used).

**READ** = seen in the decompile/MIPS/data. **INFERRED** = reasoned to. Designer names (debug strings,
text keys, `.sam`/`.sce` comments) are marked as such; the rest are mine.

**Offsets.** A staff pool slot is `P`. The object the vtable methods receive is `C = P + 8` (every
staff vtable entry has delta −8, exactly like the guest `N`/`B = N + 8`). Tables below give `C+` offsets
unless they say `P+`. The subclass functions (`0x144xxx`, `0x178xxx`, …) take `P`.

---------------------------------------------------------------------------------------------------

## 0. Short version

- **Five staff types, five of each, fixed.** Mechanics, entertainers, handymen ("Cleaners" in the
  text), guards, researchers. Each has a pool of **5** objects (`0x147eb0`), and each has **5 named
  candidates** (e.g. mechanics Bob, Wayne, Darren, Henrik, Gary). Hiring takes one candidate; the
  advisor text confirms 5 is the park maximum. Staff are **not** DBA assets. (READ)
- **Staff are persons, like guests.** Same base class (`0x1912f8` ctor, `0x191360` activation), same
  goal stack at `C+0x14/+0x18`, same route planner (`0x18da78`, route-slot pool `0x3ae1b8`), same
  walk step (`0x191e98`), same state dispatcher (`0x1920d0`), same logical-animation plumbing (low 5
  bits of `C+0x30`). A staff base class (`0x1db5b8`, vtable `0x3694b0`) adds tiredness, morale, skill
  level, patrol rectangle, hire date and the rest/strike behaviour; five subclasses add the job. (READ)
- **Jobs.** Handymen clean **litter and vomit** and **toilets** (nearest, park-wide). Mechanics
  **repair broken rides** and **install ride upgrades** (the only other mechanic job). Guards **chase
  pranksters and entertainer-hecklers, carry them to the park entrance and throw them out**.
  Entertainers **perform** when a guest is adjacent (guests come to watch). Researchers spend 30 % of
  idle decisions "researching", adding work points to the research manager. (READ)
- **Management.** Wage = `[50,55,65,80,100][level] × [3,1,1,2,3][kind]` per month, paid pro rata;
  training cost = `[250,275,325,400,500][level] × [3,1,1,2,6][kind]`, training also resets tiredness
  to 0 and morale to 100. Tiredness drives staff to the nearest **Staff Room** (DBA `+0x2e` bit 1)
  at ≥ 81; a monthly check per type starts a **strike** ladder when average energy or morale falls
  under 15. Patrol areas only bound the idle wander. (READ)
- **Dead ends on PS2 (READ):** list-box "Call Cleaner" is `jr ra` (`0x124298`); list-box "Grab" sets
  tool mode 18, whose table slot is null (the grab tool object `0x388f10` is built but never put in
  the mode table); `FUN_00153d08` (called when a repair ends) returns 0; most "very unhappy /
  striking / strike end / coverage" advisor messages have blank text (row 310).
- **Nothing on the disc scripts staff.** Staff characters ship only `.mps`/`.aps`. The four staff-room
  features have a tiny `.rss` that runs a particle and a sound while `VAR_STAFFIN` is set. (READ)

---------------------------------------------------------------------------------------------------

## 1. The staff types

### 1.1 Identity table (READ)

Four different numberings exist; all four are used, so all four are given.

| | mechanic | entertainer | handyman | guard | researcher | source |
|---|---|---|---|---|---|---|
| DB kind (`0x12ad28(fac, kind, slot)`, hire tool arg) | 0 | 1 | 2 | 3 | 4 | activations `0x1783a8/0x12e0a8/0x144c90/0x140600/0x1b5fc0` |
| type code (`vt+0xac`) | 2 | 1 | 5 | 3 | 4 | `0x1796c8, 0x12e750, 0x145db8, 0x141cd8, 0x1b65b0`; used by free `0x14b608`, strikes, All Staff |
| advisor-rule bit | 1 | 8 | 2 | 4 | 0x10 | `0x1053a8/0x105538/0x105688` |
| pool (name, global) | PoolOfMechanics `0x3952b0` | PoolOfEntertainers `0x3952bc` | PoolOfHandymen `0x3952b4` | PoolOfGuards `0x3952ac` | PoolOfResearchers `0x3952b8` | `0x147eb0` |
| slot size / count | 0x64 × 5 | 0x68 × 5 | 0x60 × 5 | 0x6c × 5 | 0x60 × 5 | pool alloc sizes 0x204/0x218/0x1f0/0x22c/0x1f0 |
| slot ctor / vtable | `0x178310` / `0x3625c8` | `0x12da48` / `0x35c708` | `0x144bf8` / `0x35f9d8` | `0x140540` / `0x35f2e0` | `0x1b5f28` / `0x365f50` | ctors |
| allocate / free | `0x14b008` / `0x14b790` | `0x14b290` / `0x14b940` | `0x14b0e0` / `0x14b8b0` | `0x14af30` / `0x14b700` | `0x14b1b8` / `0x14b820` | |
| active list / count | `0x14d650` / `0x14d6b0` | `0x14d670` / `0x14d6d0` | `0x14d640` / `0x14d6a0` | `0x14d228` / `0x14d680` | `0x14d660` / `0x14d6c0` | pool `+8` / `+0xc` |
| model id (registry `0x2bf2b8`) | 427 `FatMechanic` | 424 (8 costumes, below) | 426 `Handyman` | 425 `Guard` | 423 `Researcher` | activation requests `0x1ab/0x1a8/0x1aa/0x1a9/0x1a7` |
| wage multiplier `0x35c428[kind]` | 3 | 1 | 1 | 2 | 3 | `0x12b630` |
| training multiplier `0x35c458[kind]` | 3 | 1 | 1 | 2 | 6 | `0x12b680` |
| candidate name rows `0x35c220..` | 255..259 Bob, Wayne, Darren, Henrik, Gary | 460,459,463,465,467 Steve, Andy, Simon, Alex, Brian | 778,779,782,783,784 Leon, Frank, Stuart, James, Nick | 1016..1020 Matt, Martin, Brett, Richard, Ian | 979,980,982,985,988 Jarl, Mark, Morten, Paul, Simon | tables `0x35c220/238/268/250/280` |
| DB record `+0xc` constant | 9 | 0xd | 0xb | 0xa | 0xc | `0x12a758`; meaning not traced |
| purchase text | `STR_PURCHASE_MECHANICS` 622 | `…ENTERTAINERS` 518 | `…HANDYMEN` 658 "Cleaners" | `…GUARDS` 576 | `…RESEARCHERS` 63 | text DB |

- **Entertainer costumes.** Registry id 424 has eight entries `{name, world, category 7, id, variant}`:
  JUNGLE `dino`(1) `hunter`(2); HALLOW `franky`(1) `vampire`(2); FANTASY `flower`(1) `gnome`(2); SPACE
  `spaceman`(1) `alien`(2) (entries `0x2c1988..0x2c1a84`). The other four staff have world 4 (all).
  Which variant an entertainer wears is not traced (the activation passes 0 after the id); text
  `STR_ADVMES_CONGRATS_COSTUMES` and a SPACE "Costume Shop" exist. (READ entries, variant choice open)
- `GetNumberOfStaff(kind)` `0x12b160` jumps through `0x35c300`, whose five targets are all `0x12b188`:
  **`return 5`** (MIPS `0x12b160..0x12b18c`). The 25 candidate records are made once per park by
  `0x12a878 → 0x12a758` (§2.4).

### 1.2 Per-level tables (READ from the ELF; level = `C+0x48 & 7`, 0..4, shown "Level 1..5")

| table | L0 | L1 | L2 | L3 | L4 | reader |
|---|---|---|---|---|---|---|
| wage per grade `0x35c410` (× kind mult) | 50 | 55 | 65 | 80 | 100 | `0x12b630` (index 5 = 0, reachable from the training screen, §4.4) |
| training cost `0x35c440` (× training mult) | 250 | 275 | 325 | 400 | 500 | `0x12b680` |
| handyman litter time, ticks `0x35fbd8+12L` | 120 | 60 | 30 | 20 | 10 | `0x144ec8` |
| handyman toilet time, ticks `0x35fbdc+12L` | 180 | 120 | 60 | 30 | 15 | `0x144ec8` |
| handyman walk speed `0x35fbe0+12L` | 10 | 15 | 20 | 20 | 18 | `vt+0x18c` = `0x144d38` |
| mechanic repair/install time, ticks `u16 0x3627c8+4L` | 240 | 180 | 120 | 60 | 60 | `0x1785f8`, `0x178a38` |
| mechanic walk speed `u16 0x3627ca+4L` | 9 | 12 | 14 | 16 | 18 | `vt+0x18c` = `0x179308` |
| researcher work per quantum `0x366150` | 20 | 30 | 35 | 40 | 43 | `0x1b61c0` |
| entertainer: watchers stay `300 + 60·L` ticks | 300 | 360 | 420 | 480 | 540 | guest `0x20fb88` reads `P+0x50 & 7` |

- Other speeds: the staff base speed is the 5-bit field `C+0x48` bits 3–7 (`vt+0x18c` = `0x1dc928`),
  set to **15** on activation (`0x1db618`, `| 0x78`) and by the guard idle (`0x1413e0`); a guard starting
  a chase sets **30** (`0x1417d0`, `| 0xf0`). Guests use `15 + rand(15)` (native-guest-motion.md), and
  step = `speed × D >> 14` in 1/256 cell with `D` the frame delta.
- Guards and entertainers have **no other per-level table found**; their level only feeds wage, the
  entertainer watch time and the advisor's training average.

### 1.3 Text (READ, EUR text DB)

- Labels: `STR_PURCHASE_STAFF` "Staff", `STR_PURCHASE_PAY_GRADE` "Pay Grade", `STR_PURCHASE_MOTIVATION`,
  `STR_PURCHASE_SKILL_LEVEL`, `STR_PURCHASE_STAFF_COSTS` "-Staff Costs", `STR_SINGLESTAFF_*` (Single
  Staff, Tiredness, Time Employed, Motivation, Skill Level, Monthly Wage, Training, Training Cost),
  `STR_ALLSTAFF_*` (All Staff, Status, Motivation, Overall Motivation, Skill Level, per-type plurals,
  Unknowns, Empty), `STR_TRAINING_LEVEL_1..5`, `STR_FINANCE_STAFF_WAGES`, list boxes
  `STR_LISTBOX_{SET_PATROL_AREA, GRAB, FIRE, ZOOM_TO, CALL_MECHANIC, CALL_HANDYMAN "Call Cleaner",
  KICKOUT_{MECHANICS,RESEARCHERS,HANDYMEN,ENTERTAINERS,GUARDS}}`, `STR_GIZMO_CPP_{HIRE,GRAB}`.
- `STR_STAFF_PAY_MESSAGE` "Your staff have been awarded their annual pay rise." (row 281): the only
  `li 0x119` immediates in the ELF are at `0x1af6c0` (coaster train code) and `0x217794` (audio); **no
  consumer found** — INFERRED unused on PS2.
- Advisor messages (id, `AdvisorCatalogue`; "blank" = text row 310):

  | ids | key | text |
  |---|---|---|
  | 3–7 | `STAFF_HIRE_{MECH,HANDY,GUARDS,ENT,RES}_1` | mechanics blank, others present |
  | 8,10,13,15 / 9,11,14,16 | `SET_*_AREA` / `*_COVERAGE` | areas present / coverage blank |
  | 12 | `STAFF_GUARD_BUILD_CAMERA` | present |
  | 0x11–0x15 | `STAFF_TRAIN_*` | present |
  | 0x16–0x1a, 0x1b–0x1f, 0x20–0x24, 0x25–0x29, 0x2a–0x2e | `UNHAPPY_*`, `VERY_UNHAPPY_*`, `STRIKING_*`, `HAPPIER_*`, `STRIKE_END_BAD_*` (order MECH, HANDY, GUARD, ENT, RES) | only UNHAPPY and HAPPIER have text |
  | 0x37/0x38/0x39 | `RIDES_BREAKDOWN_{NO,BUSY}_MECHANICS`, `…_MECHANIC_ON_IT` | present; chooser `0x103658` |
  | 0x48 | `FEATURES_DIRTY_TOILETS_1` | present |
  | 0x52 | `ADD_WAGES_HIGH` | present |
  | 0x58–0x61 | `ADD_MAX_*` (+`_V2`) | "5 … is the maximum", V2 blank |
  | 0x64 | `ADD_PRANK_LITTER` | present |
  | 0x7c/0x7d/0x7e, 0xcf | `ADD_UPGRADE_HIRE_MECHANIC`, `…STRIKING_MECHANICS`, …, `UPGRADE_NO_MECHANICS` | blank |
  | 0x80 | `ADD_NEED_STAFF_ROOM` | present |
  | 0x83 | `ADD_TRAIN_STAFF_GENERAL` | present |
  | 0xa0 | `GOLD_TICKET_BROTHER` (Security Award) | present; weekly `0x16bc70` when `0x104ce0(0x40) > 80` |
  | 0xe4 | `TUT_STAFF_HIRE` "Staff Types" | tutorial |

### 1.4 Models and animations (READ)

`DATA.WAD:/Chars/<name>/<name>.mps + .aps` for FatMechanic, Guard, Handyman, Researcher and the eight
costumes (Alien, Dino (+ `dino.tga/.ssh`), Flower, Franky, Gnome, Hunter, Spaceman, Vampire); body
textures in `/Chars/Textures/` (`fatmechbody`, `guardbody`, `handybody`, `boffinbody`, `broom`,
`clipboard`, `plackard` (a strike placard, INFERRED from the name), `raygun`, `spear`,
`thinmechbody` (no user found), costume bodies). No `.sam`/`.rss` in any `/Chars/` folder.

Every staff `.aps` has 8 sections (record counts, frames):

| model | s0 | s1 | s2 | s3 | s4 | s5 | s6 | s7 |
|---|---|---|---|---|---|---|---|---|
| Handyman | 1 (32, walk-layer) | 0 | 32 | 32 | 0 | **80** | 0 | **96** |
| FatMechanic | 32 | 0 | 32 | 32 | 32 | 32 | 40 | 32 |
| Guard | 32 | 32 | 32 | 32 | 41 | 32 | 36 | 32 |
| Researcher | 32 | 0 | **150** | 32 | 0 | 60 | 0 | 32 |
| Flower / Dino / Alien | 32 | 0 | 64/40/60 | 32 | 0 | 32/104/32 | 0 | 64/64/40 |

Logical animation ids (`C+0x30 & 0x1f`, dispatched through table `0x2aad48`, `NativeLogicalAnimationTable`)
that staff code writes, and what they play:

| logical | table row | staff writers |
|---|---|---|
| 18 (0x12) | main s3 | activation `0x1db618` |
| 13 (0xd) | main s0 (the walk layer) | walking/idle: `0x1db750`, `0x1db900`, `0x178714`, `0x140990`, `0x191d78` |
| 16 (0x10) | first s4 / main s5 / last s6 | working: sweep `0x145598`, clean-toilet, repair `0x178880`/`0x178b10`, perform `0x12df40`, research `0x1b61c0`, guard carry `0x140880`, guard at gate `0x140e08` |
| 15 (0xf) | main s7 | striking (`0x1db970`, load `0x1dc178`) |
| 11 (0xb) | 93 % nothing, else s2 variants | entertainer after a show (`0x12df40`) |
| 17 | s3 variants 0/1/2 | requested on the **attached copy of a caught guest's model** carried by a guard (`0x140880`) |

⚠ findings/native-guest-animation-readiness.md lists `140880`/`140990` (and their callers `140C70`,
`141020`) as guest code: they are **guard** methods (§3.3). The guest itself is hidden; the guard
carries a copy of its model.

### 1.5 Sounds (READ)

Staff sounds are native category 8 = `AUDIO/GLOBAL/staf` (`/AUDIO/GLOBAL/STAFSFX.MAP`, `STAFFHD.SDT`,
`STAFBANK.MAP`), played with `0x111428(audio, 8, event, pos, &handle, 0)`:

| event | clips | played by |
|---|---|---|
| 0x87 | `TADA` | entertainer show ends `0x12df40` |
| 0x88 | (no sets) | guard starts a chase `0x1417d0` |
| 0x89 | blank | guard carrying `0x1409e8` (debug " EVT_GUARD") |
| 0x8a | blank | researcher quantum `0x1b61c0` |
| 0xa0 / 0xa1 | `cl_wk03/04` / `cl_st01/04` | handyman nothing-to-do / 1-in-16 idle (" EVT_CLEANER_WALK"/"_STAND") |
| 0xa2 / 0xa3 | `mc_wk02/04` | mechanic chatter `0x1781b8` |
| 0xa4 / 0xa5 | `jj_wk05/07` | entertainer patrol / 1-in-16 idle |
| 0xa6 / 0xa7 | `gd_wk04` | guard patrol / 1-in-16 idle |
| 0xa8 | blank | researcher patrol (" EVT_SCIENTIST_WALK") |
| 0xbc | 6 sets: cough, crackle, newspaper, slurp+sniff, spoon/tapspoon | staff-room feature, status 2 (`0x130510`, "START Staff event") |

Also bank 2 (`GLOBAL/ride`) event **0x6f**, looped with a handle at `P+0x60`, is the mechanic's
repair noise (`0x178880`); upgrade completion plays bank 2 `0xb8` (tiers < 3) or `0xe1` (`0x116268`).

### 1.6 The Staff Room feature (READ)

DBA kind-2 records with `+0x2e` bit 1: JUNGLE key 204 `Features/Staff`, HALLOW 120 `features/horstaff`,
FANTASY 475 and SPACE 483 `features/staffrm`: all "Staff Room", 2×2, **cost 500**, research 0. SPACE
key 356 "Laser Show" also has bit 1 (already flagged in dba.md) and would be accepted by the staff
code's filter `0x1308c8` (INFERRED: a data bug that makes the laser show a staff room).

`.sam` (e.g. JUNGLE `Staff.sam`): `Info.Id 1411`, `Info.IsChoosable 0` ("People CANNOT use this"),
`Info.Shape` 2×2 with the entry cell marked `2`, `UsageInfo.RideHandlesSprite 1`,
`EntryCellStandPos`/`ExitCellAppearPos`, `CostOfUpgrade 500`, **`UsageInfo.ChillsYouOut 1`**. `.rss`:
waits for `VAR_STAFFIN` (variable 0) to become non-zero, adds particle `P_EFFECT_BrewUp` on a node and
sound `EVT_STAFF_ROOM` (`OBJ_SOUND_GLO_STA`), removes them when it drops to 0. The **writer of
`VAR_STAFFIN` was not found** (the native side starts event 0xbc from the feature's own status
instead, §1.5).

Feature fields the staff read (P-relative feature object, getters `0x130718..0x130978`): `+0xb4`
cleanliness 0..100 (`0x130938`; `0x130948` subtracts; `0x130978` sets 100 and stamps `+0xa8` with the
calendar day), `+0xa2` "usable" byte (staff-room search), DBA `+0x2e` bits: 0 toilet (`0x130718/0x130780`),
1 staff room (`0x1308c8`), 2 bin (`0x1307e8`), 3 camera (`0x130858`).

---------------------------------------------------------------------------------------------------

## 2. The native classes

### 2.1 Hierarchy, pools and ownership (READ)

```
CPerson   ctor 0x1912f8, dtor 0x191338, activate 0x191360 (serial via 0x1093b0)      (shared with guests)
 └ CStaff ctor 0x1db5b8 (vt 0x3694b0, C+0x3c = 0), dtor 0x1db5f0, activate 0x1db618
    ├ mechanic    P ctor 0x178310, P dtor 0x178350, activate 0x1783a8
    ├ entertainer        0x12da48,          0x12da88,          0x12e0a8
    ├ handyman           0x144bf8,          0x144c38,          0x144c90
    ├ guard              0x140540 (+P+0x58=0), 0x140588 (frees the carried model), 0x140600
    └ researcher         0x1b5f28,          0x1b5f68,          0x1b5fc0
 guest: ctor 0x20bbd0, vt 0x36cce0, activate 0x20bcd0, 100 × 0xa8 in PoolOfPeople 0x3952cc
```

- Pools are built in `0x147eb0` right after **PoolOfEffectors** (`0x3952a8`, 20 × 0x1c, §3.4): guards
  (write `0x148744`), mechanics (`0x1487f4`), handymen (`0x1488a4`), researchers (`0x148954`),
  entertainers (`0x148a04`), then PoolOfLitter (40 × 0x30). Pool header 0x10 bytes: `+4` free list,
  `+8` active list, `+0xc` active count; registered with `0x15fa38(pool, name, size, stride, 5)`.
  Freed in `0x149038`.
- **Allocate** (`0x14af30` etc.): pop free → push active, count+1, `vt+0x1c4(slot)` (stores the
  candidate slot into `C+0x49`), `vt+0x34` (activation), `0x14da60` (register as a map object). Callers:
  the hire tool `0x128690` and the loaders `0x160448..0x1606c8`. **Free**: `0x14b608(C)` calls
  `vt+0x194` (release) and dispatches on `vt+0xac`; unknown types print
  "*** DONT KNOW HOW TO FREE THIS STAFF TYPE ***". Callers: Fire `0x124300`, hire-cancel `0x128a90`.
- **Per-tick update**: staff are map objects, so they are updated by the global object loop in
  `0x14be60` through `vt+0x3c` (INFERRED that this loop walks the list `0x14da60` adds to; the loop
  calls `vt+0x3c` on every element, READ).
- The five "guard slots" of native-activation-serial.md are just PoolOfGuards's 5 slots; plan.md's
  "guard staging (second family, `0x3952AC`)" is this pool (consistent with progress.md).

### 2.2 Vtables (READ; 63 entries each, `{s16 delta, s16, u32 fn}`, call `vt+N` uses the fn at `+N`)

Staff base `0x3694b0`; `**` = the subclass overrides it (all overrides have delta −8). Full dump:
`out/vt_dump.txt`.

| slot | base `0x3694b0` | mech `0x3625c8` | ent `0x35c708` | handy `0x35f9d8` | guard `0x35f2e0` | res `0x365f50` | role |
|---|---|---|---|---|---|---|---|
| `+0x00c` | `0x1db5f0` | `0x178350`** | `0x12da88`** | `0x144c38`** | `0x140588`** | `0x1b5f68`** | destructor |
| `+0x02c` | `0x192c10` | | | | | | show/hide model (0 hides) |
| `+0x034` | `0x1db618` | `0x1783a8`** | `0x12e0a8`** | `0x144c90`** | `0x140600`** | `0x1b5fc0`** | activation |
| `+0x03c` | `0x1dc018` | `0x1791a8`** | `0x12e160`** | `0x1459b0`** | `0x141580`** | `0x1b6308`** | **per-tick update** |
| `+0x04c` | `0x1dc900` (1) | | | | | | selectable |
| `+0x054` / `+0x05c` | `0x1dc910` / `0x1dc908` | | | | | | release / get candidate record (`C+0x3c`) |
| `+0x06c` / `+0x074` | `0x192ff0` / `0x1925f8` | | | | | | position raw / **in cells** (`sra 8`, MIPS `0x1926c4`) |
| `+0x07c` | `0x1dc240` | | | | | | name text (candidate `+4`) |
| `+0x0a4` | `0x1dc2a0` (returns 10) | | | | | | object class for the hover test |
| `+0x0ac` | `0x1096e8` | `0x1796c8` 2 | `0x12e750` 1 | `0x145db8` 5 | `0x141cd8` 3 | `0x1b65b0` 4 | staff type code |
| `+0x0dc` | `0x192dc8` | | | | | | returns state `C+0x2f` |
| `+0x144` | `0x1db970` | `0x178be8`** | `0x12dc88`** | `0x144ec8`** | `0x140cd8`** | `0x1b6068`** | person state 2: segment end / arrival by mode |
| `+0x14c` | `0x191e98` | | | | `0x140b48`** | | person state 3: walk step (guard adds catch test) |
| `+0x154 +0x15c +0x164` | `0x1920b8 0x1913b8 0x1920c8` | | | | | | person states 1, 5 (local wander), 0xb (wait for route) |
| `+0x16c` | `0x1db768` | `0x178458`** | | `0x144d60`** | `0x1406d8`** | | event handler (route found/failed, gate events) |
| `+0x174` | `0x1dbb78` (empty) | `0x1791a0` | `0x12e158` | `0x1459a8` | `0x141578` | `0x1b6300` | "think" — **empty in every staff class** (guests: `0x20fb88`) |
| `+0x17c/+0x184` | `0x192468/0x192480` | | | | | | save/load person part |
| `+0x18c` | `0x1dc928` | `0x179308`** | | `0x144d38`** | | | walk speed |
| `+0x194` | `0x1925a8` | `0x1794c8`** | `0x12e5b0`** | `0x145b48`** | | | release job, then person remove |
| `+0x19c` | `0x1928f8` | | | | | | "an object was removed" notice (`0x14b9d0`) |
| `+0x1a4` | `0x1db800` | `0x178dd8`** | `0x12de40`** | `0x1457e0`** | `0x1413e0`** | `0x1b60a8`** | **state 0: find work** |
| `+0x1ac` | `0x1db900` | | | | | | state 0xf (striking): end when the strike ends |
| `+0x1b4` | `0x1db808` | | | | | | state 0x1a: walk to the strike point |
| `+0x1bc` | `0x1dba90` | | | | | | tired / on-strike check (1 = busy with that) |
| `+0x1c4/+0x1cc` | `0x1dc918/0x1dc920` | | | | | | set/get `C+0x49` (candidate slot) |
| `+0x1d4` | `0x1db750` | | | | | | placed: logical 13 |
| `+0x1dc` | `0x1dc9c8` | | | | | | drop target and route |
| `+0x1e4` | `0x1dca60` (1) | `0x179430`** | | | | | interruptible (mech: not while repairing/installing) |
| `+0x1ec` | `0x1dca68` | `0x179470`** | | | | | available (state ≠ 0x32) |
| `+0x1f4` | `0x1dc6f0` | | `0x12e768`** | | | | fire (pay off, free the candidate) |

Compared with the guest table `0x36cce0` (52 entries, ends at `+0x19c`), the staff base adds eleven
slots `+0x1a4..+0x1f4`; the guest overrides `+0x44`, `+0x4c..+0x5c`, `+0x7c`, `+0xa4`, `+0xac`, `+0x144`,
`+0x164..+0x174`, `+0x18c..+0x19c`, the staff base overrides a different subset (dump in `vt_dump.txt`).

### 2.3 Fields (READ; `C` offsets, `P = C − 8`)

| off | type | meaning | written / read by |
|---|---|---|---|
| `+0x08` | ptr | the visual (model instance); `(C+8)->vt+0x24->+0xc(model, id, 0, −1)` sets the model | activations |
| `+0x0c` | u32 | activation serial (low bits = the tick phase used everywhere below) | `0x1093b0` |
| `+0x10` | ptr | vtable | ctors |
| `+0x14` | s32 | goal-stack depth | everywhere (push: `C+0x18+depth = state; depth++`) |
| `+0x18..+0x1b` | u8[4] | goal stack (saved states) | |
| `+0x1c/+0x1e` | s16 | x / z, 1/256 cell | person base |
| `+0x20` | ptr | job target (litter, toilet, ride, guest, staff room); on the gate legs the guard stores a 0/1 leg flag in its low half (`0x140cd8`, `0x141900`) | subclasses |
| `+0x24` | u32 | timestamp / deadline (tick) | subclasses |
| `+0x28` | s16 | route slot (−1 none) | `0x192840` frees |
| `+0x2c` | u16 | flags: `0x40` frozen/held (skip update), `0x20` guard re-routed to exit, `0x200` guard carrying, `4` (guest-side) | tools, `0x1928b0`, `0x140880` |
| `+0x2e` | u8 | **mode** (what the current walk is for) | |
| `+0x2f` | u8 | **state** (execution state; names in §5) | |
| `+0x30` | u32 | low 5 bits: logical animation request | |
| `+0x34` | f32 | facing, radians (0, π/2, π, 3π/2) | mech `0x1787d0`, guard at the gate |
| `+0x3c` | ptr | candidate record (§2.4) | activations |
| `+0x40` | s32 | hire day (calendar day count `0x16b218`) | `0x1db618`; `0x1dc458` = days employed |
| `+0x44..+0x47` | s8×4 | patrol rectangle x0, z0, x1, z1 (cells); unset = (0,0,−1,−1) | `0x1dc540` reset, `0x1dc490` set, `0x1dc4f8` get, `0x1dc698` "set?" |
| `+0x48` | u8 bits | 0–2 **skill level**; 3–7 **walk speed** | activation, training `0x1ff610`, guard |
| `+0x49` | u8 | candidate slot 0..4 | allocator `vt+0x1c4` |
| `+0x4a` | s8 | copy of the candidate's "motivation" (0..100) | activation only; **no reader found** in the staff, UI, strike or wage code |
| `+0x4b` | s8 | **tiredness** 0..100 (activation `rand(30)`) | many |
| `+0x4c` | s8 | **morale** 0..100 (activation `70 + rand(30)`) | many |
| `P+0x58..` | | subclass: handyman `+0x58/+0x5c` sound handles; mechanic `+0x58/+0x5c/+0x60` sound handles; guard `+0x58` carried model, `+0x5c..+0x68` sound handles; entertainer `+0x58` effector record, `+0x5c..+0x64` handles; researcher `+0x58/+0x5c` handles | |

Displayed "Motivation" is `((100 − tiredness) + morale) / 2` (`0x1dc428`); "Overall Motivation" is its
average over the list (`0x10c0b0`).

### 2.4 CStaffDatabase: the candidate records (READ)

The DB factory singleton (`0x12a550`, at `0x2b2a78`) holds `+0xf0` count and `+0xf4 + i·0x20` records;
`0x12a878` creates, for kind 0..4 and slot 0..4, one record (`0x12a758`). `0x12ad28(fac, kind, slot)`
finds the slot-th record of that kind. A stale linker-map fragment in the ELF header gap
(file `0x100..0xfff`) names these functions (`CStaffDatabase::Init`, `getMonthlyWage(int)`,
`getTrainingCost(int)`, `CDatabaseFactory::GetNumberOfStaff(TYPE_STAFF)`, `ReleaseStaff(CStaffDatabase*)`)
at addresses **0x78 lower** than this build's (`Init` = `0x12b5b8`, wage `0x12b630`, training
`0x12b680`, checked on the three).

| off | meaning | writer / reader |
|---|---|---|
| `+0` / `+2` | u16 ? / u16 slot index | `0x12b528`; `0x12b520` read by the build menu |
| `+4` | name text row | `0x12b570`; `0x12b548` → `0x1dfa58` |
| `+8,+9,+0xa` | bytes = 1 | `0x12b5b8`; the ride code reads the same getters on ride records (`0x118568`) |
| `+0xc` | 9/0xd/0xb/0xa/0xc | `0x12a758` |
| `+0x10` | kind 0..4 | |
| `+0x14` | **pay grade** = starting skill level, `rand() & 1` (0 or 1) | `0x12b5b8`; hire screen shows `+1` |
| `+0x18` | **motivation** `rand() % 60 + 20` | hire screen bar; copied to `C+0x4a` |
| `+0x1c` | **available for hire** (1) / hired (0) | init 1; activation 0; fire `0x1dc6f0` and hire-cancel `0x12ae68` set 1; `0x12ad08` reads |

### 2.5 Shared with guests, and where staff differ (READ unless marked)

- Shared: person base fields `C+0x08..+0x34`, goal stack, activation serial, route requests
  (`0x18da78(C, x, z, tx, tz, flags, 0)`, flags used by staff: `0x11` ordinary, `0x23` strike point /
  exit re-route, `0x21` guard to the gate, `1` guard to exit/gate, `0x80` guard patrol point;
  native-route-planner.md), the route-slot pool `0x3ae1b8` (`0x192768` alloc, `0x192840` free,
  `0x192560` straight segment), the state dispatcher `0x1920d0`, walk step `0x191e98`, segment advance
  `0x191d78`, local wander `0x1913b8` (person state 5), event records (vtable `0x35a548`, code at
  `+4`), logical animations, hover/selection through `0x14be60`.
- Different: no needs, no "think" (`vt+0x174` empty), no money, no queues; speed from the level tables;
  five extra virtuals; staff are frozen (update skipped) while `C+0x2c & 0x40` (held by a tool,
  `0x1928b0`) **or while they are the focus object** `0x3953c0` (`0x14e1a0`). That global is cleared
  every frame by `0x14be60` and set there to the footprint object under the cursor — but that hit test
  skips objects of class 10 (staff, `vt+0xa4` = `0x1dc2a0`) and 0xb (MIPS `0x14c000..0x14c028`) — and
  set explicitly by `0x14bc28(obj)`, which the training screen calls (`0x1ff610`). INFERRED: a staff
  member stands still while the laptop is focused on them; how persons are picked with the cursor is
  not traced.

---------------------------------------------------------------------------------------------------

## 3. Jobs

### 3.1 Handymen ("Cleaners")

- **Find work** (`0x1457e0`, state 0): 1/16 chance of sound 0xa1; if `vt+0x1bc` (tired/strike) is 0:
  `rand(2) == 0` → nearest litter (`0x144fd8`), else → dirtiest-nearest toilet (`0x145250`, falls back
  to litter). Nothing found → sound 0xa0, state 0xd (patrol).
- **Litter** (`0x144fd8`): over the litter list (`0x14d1f8`, PoolOfLitter `0x3952c0`), nearest by
  Manhattan cells among items with no claimant (`litter P+0x2c == 0`); claim it (`litter+0x2c = C`),
  mode 7, route flags 0x11. Arrival (`0x144ec8`, mode 7) → state **0x1b** with deadline
  `now + litterTime[L]`. At the deadline (`0x145598`): morale `+1`, or `−6` if the item's `+0x24` is
  set; tiredness `+5`; unclaim; `0x1824a8(x, z)`; free the item (`0x14b460`); state 0.
  `litter+0x24` is the vomit flag (INFERRED: `0x15e368` sets it, and guests next to such an item gain
  nausea in `0x20fb88`).
- **Toilets** (`0x145250`): placed objects with `vt+0x134` true (toilet) and cleanliness `< 60`; score
  **`|dx| + |dz| × (cleanliness + 1)`** (MIPS `0x14534c..0x14536c`; only the z term is weighted —
  INFERRED a slip for `(|dx|+|dz|)·(c+1)`), lowest wins; mode 0x12, route to the entry cell
  (`0x1e1760`). Arrival → state **0x33**, deadline `now + toiletTime[L]`, model hidden. At the deadline
  (`0x1456d8`): if cleanliness `< 40`: morale `−10`, tiredness `+5`; else morale `+5`; then tiredness
  `+5`; **`0x130978`** (cleanliness 100, stamp); state 0; model shown.
- Event handler `0x144d60`: route failed while going for litter → release the claim; event 5 → drop
  everything, state 5. `0x145ac8` (route-system reset) and `0x145b48` (release) also unclaim.
- **Not found**: any bin emptying (bins are handled by guests, `0x20d010`), vomit as a separate job
  (it is litter), paths, or anything tied to the patrol area.

### 3.2 Mechanics

- **Find work** (`0x178dd8`, state 0): if not tired/striking: tiredness `+6`, morale `+1` (every
  attempt, MIPS `0x178e08..0x178e3c`); then (MIPS `0x178e40..0x178ecc`):
  - `rand(2) == 0`: nearest **broken** ride `0x153b80` → dispatch as repair; else nearest ride in the
    **upgrade list** `0x153d40` → dispatch as install;
  - `rand(2) == 1`: upgrade list → install; else **the upgrade list again** → dispatch as repair
    (`0x153d40` twice; INFERRED a slip for `0x153b80`: in this branch broken rides are never sought).
  - nothing → state 0xd.
- `0x153b80` iterates placed rides (`0x1e5b20(it, 1)`), keeps those with `vt+0xc4` true whose
  assigned mechanic (`ride+0x80`, `0x1e1df0`) is none or me, nearest Manhattan.
- **Dispatch** `0x178cf8(mech, ride, repair)`: fail if another mechanic holds the ride; for a repair
  also require `vt+0xc4` and `vt+0x2cc`; set `ride+0x80 = C`, target = ride, state **0x38** (repair)
  or **0x39** (install). 0x38/0x39 route to the ride's `vt+0x17c` cell (mode 6 / mode 0x14).
- **Repair**: arrival → state 0x10 (`0x1785f8`): service flag (`ride+0x11c`, `0x115fa0`) clear →
  chatter sound (`0x1781b8`) + request flag 1 (`0x118568(ride,1)`, which also raises the ride's icon);
  set → state **0xe**, ride status **6**, deadline `now + repairTime[L]`. State 0xe (`0x178880`): logical
  16, looping bank-2 0x6f, face the ride; at the deadline → state 0x11 and `0x153d08(ride)` (returns 0,
  a stub). State **0x11** (`0x1786d0`): flag set → ride status **7**, unassign, chatter, clear the flag
  (`0x118678`); next pass (flag clear) → state **0x3a**, logical 13, show, morale `+10`. State 0x3a
  (`0x1790c8`) walks to the ride's `vt+0xf4` cell (mode 0x16); arrival clears the target.
- **Install an upgrade**: arrival → state 0x34 (`0x178a38`): request flag 2, then state **0x36**, ride
  status 6, deadline `now + repairTime[L]`. State 0x36 (`0x178b10`): at the deadline **`0x116268(ride,0)`**
  (clears the ride's pending-upgrade flag `+0x128`, ride tier `+0x126 += 1` up to 3, debits the new
  tier's price × 10 from the ride's record `+0x50 + 0x34·tier`, sound 0xb8/0xe1), state 0x11, remove
  from the list `0x153d70`. (`0x1d5c00` sets `+0x128 = 1` when it queues the ride.)
- **The upgrade list** `0x3953e8` (u32[15], count `0x2b739c`): add `0x153d10` (only caller `0x124270`,
  reached from the ride panel's upgrade handler `0x1d5c00`, which instead posts advisor 0x7c (no
  mechanics) or 0x7d (mechanics striking)); remove `0x153d70` (callers: ride update `0x1169c0`,
  object removal `0x14b9d0`, `0x178b10`); cleared by `0x1542a0` when the last mechanic is fired
  (`0x124300`) and by `0x1541d0` at park teardown (which forces status 7 on rides a mechanic is
  working on, via `0x179508`). ⚠ coaster-operation.md/track-ride-operation.md call `0x153d70` "add to
  the condemned list"; it **removes** (`0x153b00`).
- **Manual dispatch**: ride list box "Call Mechanic" (`0x124250 → 0x124158(1)`): the **first** mechanic
  in list order that has no target and is not striking/resting/walking to strike or staff room → dispatch
  as repair (no distance test). Breakdown advisor chooser `0x103658` uses the same availability test.
- Walking on a repair errand (mode 6): morale `−2`, tiredness `−2` on each state-2 pass (`0x178be8`;
  frequency = once per route segment, INFERRED).
- Event handler `0x178458`: if the route to a ride fails, the ride is unassigned and handed to the
  first available mechanic (`0x178cf8`).
- ⚠ track-ride-operation.md §6.6 reads `0x1781b8` as "route to the ride": it only plays chatter
  (bank 8, 0xa2/0xa3) every `DAT_002bed10` ticks; the route is laid by state 0x38/0x39.

### 3.3 Guards

- **Find work** (`0x1413e0`, state 0): 1/16 sound 0xa7; speed back to 15; if free → sound 0xa6,
  state 0xd. Guards do not look for crime themselves; they are **sent**.
- **Who sends a guard** (`0x1417d0(guard, guest)`: target, deadline `now + 3600`, state **0x21**
  "Chase", speed 30, sound 0x88):
  1. **Pranksters**: guest decision `0x20c930` arm 5 (happiness below `[0x2eeb98]` and
     `rand(1000) < [0x2eeb68]`) → `0x20d010(g, 1)`: drop litter (`0x14ad08`, the litter allocator —
     native-activation-serial.md puts its activation call at `0x14adac`; "prank" from the advisor text
     `ADD_PRANK_LITTER`, INFERRED), then **`0x14d3e0(g)`**:
     among non-busy guards (`0x1416f0`) the nearest with dist² `< 64` cells², accepted only if a
     **security camera** (DBA bit 3, `0x14d238`) lies within dist² ≤ 64 of the guest or the guard is
     within dist² `< 25`.
  2. **Hecklers**: guest arm 2 (`rand(1000) < [0x2eeb64] = 10`) picks the nearest entertainer within
     Manhattan `< 5` and calls `0x12e278(ent, guest)`; the "Shocked" entertainer (§3.4) then calls the
     nearest non-busy guard within Manhattan ≤ 6.
- **Chase and catch.** State 0x21 (`0x140e08`): while `now ≤ deadline`, if the guest's cell differs,
  re-route (flags 0x11, mode 8) to the guest's current cell; if the cells are equal: attach a copy of the
  guest's model (`0x140880`: factory `0x230a98`, logical 17 on it, own logical 16, flag 0x200), send the
  guest an event with code 4 (record vtable `0x35a548`), state **0x3c**, morale `+10`, tiredness `+3`.
  Deadline passed → give up, morale `−5`. The walk step `0x140b48` (mode 8) makes the same equal-cell
  catch mid-walk (without the bonuses) and gives up with morale `−2` if the guest is in state
  0x12/0x13/0x14 (queueing / shuffling / walking to a bin).
- **Throw out** (states 0x3c → 0x27 → 0x2e → 0x2f → 0x30/0x37 → 0x3b → 0 …): 0x3c waits for the carry
  animation's main phase (`0x10ec48`) → 0x27 "Chucking out" (`0x1411c0`) routes (flags 0x21, mode 0xe)
  to the park-entrance table entry `+2/+3` (`0x1532d8`, x + rand(256)/256; its first argument is unused,
  MIPS) → arrival: state **0x2e** (wait), `0x153298` counts a waiting guard (`0x3953d8`). The gate
  controller `0x14bcc0` sends event 9 to guards and guests in 0x2e → 0x2f (`0x141900`: straight route
  segment, mode 0x10; the event code 9 is READ in the guard handler `0x1406d8`, that `0x14bcc0` sends
  code 9 is INFERRED — its record argument is lost in the decompile) → arrival 0x30 → `0x1419c0` routes
  to the exit point (entry `+0/+1`, the corridor
  start, `0x14e290`, flags 1, mode 9) → arrival **0x3b**: drop the carried model (`0x140990`), route back
  (`0x141a70`, mode 0xf) → 0x2e → 0x2f → 0x37 (`0x141298`: random point near the park mouth
  entry `+0x10/+0x11`, flags 0x80, mode 0x15) → state 0.
  The park-entrance table is `0x2b71b0 + world·0x36 + park·0x12` (findings/paths.md, `ParkEntrance.cs`).
- `0x1416f0` "busy" = states 0x21/0x3c/0x3d/0x27/0xf/0x2e/0x2f, or walking (2..3) with modes
  8/9/5/0xe/0xf/0x10/0x30.
- **Cameras** matter only through `0x14d238` (above) and the advisor (0xc). Security Award: weekly
  `0x16bc70` → gold ticket 0xa0 when `0x104ce0(0x40) > 80` (not read).

### 3.4 Entertainers

- **Find work** (`0x12de40`, state 0): 1/16 sound 0xa5; release any effector; if free → `0x12dcc8`:
  with `rand(3) == 0` and **a guest within 3-D distance² < 2** of me (`0x12dae0` over PoolOfPeople)
  → state **0xc** "Entertaining", register an **effector** (`0x14cfb8`: pos = mine, radius² 1,
  flags 2); otherwise state 0xd, sound 0xa4.
- **Performing** (`0x12df40`): logical 16; every 4 ticks tiredness `+2`, morale `+1`; stops after
  **600 ticks** or when no guest is adjacent → state 0, sound 0x87, logical 11.
- **Guests watch**: guest `0x20fb88` (every 8 ticks) ORs the flags of effectors within their radius
  (`0x14d0a0`); flag 2 and the guest's `+0x6c` cooldown passed → go to the nearest entertainer, state
  0x1c "Watch Entertainer" for `300 + 60·L` ticks (native-shop-flow.md: its end adds happiness 5 and
  resets `+0x6c`). Effector flag 1 → happiness `+6`; flag 4 → happiness down, nausea up (producers of
  flags 1/4 not traced).
- **Shocked** (`0x12e278(ent, guest)`, from the guest heckler arm): push state 0x20 "Shocked", morale
  `−5`, remember the guest, deadline `now + rand(5)·60`. State 0x20 (`0x12e320`): **morale `−10` every
  tick** (MIPS `0x12e34c..0x12e35c`) until the deadline, then send the nearest non-busy guard within
  Manhattan ≤ 6 after the guest, or morale `−5`; pop.
- Fire override `0x12e768` also frees the effector.

### 3.5 Researchers

- **Find work** (`0x1b60a8`): if free: `rand(10) < 3` → state **0x1f** "Research"; else state 0xd, sound
  0xa8.
- **Research** (`0x1b61c0`, one tick): logical 16; `0x1b6a38(mgr, R[L])` adds
  `R[L] × mgr+4 × 4096 / (activeProjects × 100)` to each active project (`0x1b7388`, the DBA `+0x4c`
  work requirement of dba.md; when a project's `+0x10` is set it calls `0x104358`/`0x151998`, not
  read); tiredness `+= (mgr+4 − 80) / 3`
  (can be negative); state 0; sound 0x8a. The research manager (`0x1b6798`, 0xac bytes at `0x2e7540`)
  is initialised by `0x1b6640` with `+0 = 1`, **`+4 = 80`** (the budget percentage, INFERRED from its
  use), five project slots at `+0xc` (0x1c each). The research system itself is its own area.

### 3.6 Everyone: rest, strike, wander

- **Tired/strike check** `vt+0x1bc` = `0x1dba90`: if my type is on strike (`0x16c988(cal, type)`) →
  `vt+0x1dc` (drop target), state **0x1a**, return 1. Else if tiredness ≥ **81** and state 0 → state
  **0x31**, return 1. Else if tiredness ≥ **91**, every 4 ticks morale `−1`. Return 0.
- **Go rest** (`0x1dbb80`, state 0x31): nearest (3-D Manhattan) placed object with DBA bit 1 and
  `+0xa2 ≠ 0`; route (0x11) to its entry cell, mode 0x11. None → state 0xd. Arrival (`0x1db970`,
  mode 0x11) → state **0x32**, model hidden. Resting (`0x1dbf50`): every 4 ticks morale `+1`,
  tiredness `−2`; at tiredness ≤ 0 → `0x1dbfd0`: show, state 0xd. The Staff Room list box "Kick Out
  <type>" (`0x124478..0x124678`) calls `0x1dbfd0` on every resting staff of that type in the selected
  room; `0x14aa90(room, type)` counts them.
- **Strike walk** (`0x1db808`, state 0x1a): route (flags 0x23, mode 5) to a random point near the park
  mouth (`0x1497c0`: entrance entry `+0x10/+0x11` plus a `rand(0x200)` offset; exact offset arithmetic
  not checked in MIPS), up to 10 tries; arrival →
  state **0xf** "Striking", logical 15 (section 7). `0x1db900` returns to work (state 0, logical 13)
  when the strike flag clears.
- **Walking** (`0x1db970`): every 4th tick (by serial phase) tiredness `+1`.
- **Patrol** (state 0xd, `0x1dc6b0` → `0x1dc558`): up to 10 random cells inside the rectangle that are
  path (kind 2, `0x1e6338`) and routable → mode 1, walk; with no rectangle (the default) it falls
  straight to state 5, the guest-style local wander `0x1913b8`. **No job search reads the rectangle**
  (litter, toilets, rides, guards, entertainers all search park-wide). (READ absence in the five
  find-work functions and their helpers)

---------------------------------------------------------------------------------------------------

## 4. Management

### 4.1 Hiring and placing (READ)

- Build & Hire → Hire (`main_bh_staff`, draw `0x199008`: name, "Pay Grade" = candidate `+0x14 + 1`,
  "Monthly Wage" = `0x12b630(cand, −1)`, "Motivation" bar = `+0x18`) → the build menu `0x197f48`
  sets **tool mode 1** (`0x19813c`) with `(slot = cand+2, kind = cand+0x10)`.
- Mode 1 (object `0x388ef0`, vt `0x35bd48`): enter `0x128690` allocates the staff member of that kind
  and slot (§2.1) and freezes it (`0x1dc780 → 0x1928b0`); cursor `0x128760` carries it; Cross
  `0x128918`: the cell must pass `0x1e65b8` (else error 0xaf) → `0x128888`: unfreeze, state 0, logical
  13, sound 0x12f, `0x126408` (debits tool `+8` × 10: whether a hire fee is ever set is not traced; the
  Hire screen shows none), mode 0; if the type's count is now 5 → advisor `ADD_MAX_*` (0x5c mech,
  0x5a ent, 0x58 handy, 0x5e guard, 0x60 res; MIPS `0x12898c..0x128a40` — the decompile drops the
  entertainer post); tutorial event 0xd. Triangle `0x128a90`: free it, candidate available again, sound 0x130.

### 4.2 Firing (READ)

List box "Fire" `0x124300`: `vt+0x1f4` = `0x1dc6f0`: candidate available again (`+0x1c = 1`), free the
route, **pay the pro-rata wage now** (`0x1dc338 × 10` through `0x100c78`, which adds it to the
staff-wages accumulator `park+0x12d0` and calls the debit `0x100698`; the decompile drops that call's
arguments, so "debit the same amount" is INFERRED); then `0x14b608`; last mechanic gone → upgrade
list cleared.

### 4.3 Wages (READ)

`0x1dc338(C)`: 0 while striking (state 0xf); else `wage(level) × pct / 100` with `pct = 100` if
days employed ≥ the length of the previous month (`0x16d2e8`, month table `0x361d88` = 31,28,31,…),
else `days × 100 / monthLength`. Month end (`0x100a18`): `0x1008b8` sums `10 × 0x1dc338` over all
five lists, adds to `park+0x12d0`, debits with the loan payments (`0x100698`), and files the month's
wages in the 144-slot history `park+0xe3c`. So an employee hired mid-month costs a partial month; a
striker costs nothing. Advisor 0x52 (`WAGES_HIGH`) exists; its producer is not traced.

### 4.4 Training (READ)

Single Staff → Training (`main_i_staff_opts_training`; screen `0x1ff4a8`, draw `0x1ff830`): shows the
next level `min(L+1, 5)`, "Training Cost" = `0x1dc2a8` = `trainingCost[L] × mult` (0 in a free-build
mode: `DAT_002a60b8` or `0x154428()`), "Monthly Wage" at the next level (index 5 reads the table's
terminating 0), a Skill bar. Cross `0x1ff6f8`: refuses with sound 0xaf if money < cost × 10; else
`0x1ff610`: target level from `0x3988a8[choice]`, debit cost × 10, and **if the target is higher,
morale = 100 and tiredness = 0**, then level = target; sound 0x12f.

### 4.5 Tiredness, morale, Staff Room, strikes (READ)

- Tiredness/morale changes are listed per job in §3; resting in §3.6.
- **Strike ladder** (state per type in the calendar singleton `0x16ae90`, `+0x304 + (type−1)·8`:
  `u32 month stamp, u8 stage (+0x308), u8 striking (+0x309)`; saved by `0x16cb40`, loaded by
  `0x16d030`). On each **month change** (`0x16b060 → 0x16c120`, gated by `DAT_002b74b0 || 0x151258()`),
  for each type with staff: if striking → just clear the flag; else `0x16c500(type)` = "unhappy" when
  there are **≥ 2** staff of the type and either **average energy `(100 − tiredness)` < 15** or
  **average morale ≤ 14** (debug prints "strikeh"/"strikev"). Then `0x16c2a0`:

  | stage | unhappy → next, message (`0x361d97[type]` + …) | not unhappy |
  |---|---|---|
  | 0 | → 1, UNHAPPY (+0x16) | stays 0 |
  | 1 | → 2, **strike on**, VERY_UNHAPPY (+0x1b) | → 0, HAPPIER (+0x25) |
  | 2, 3, 4 | → 3, 4, 5, strike on, STRIKING (+0x20) | → 0, strike off |
  | 5 | → 0, strike off, STRIKE_END_BAD (+0x2a) | → 0 |

  (MIPS `0x16c30c..0x16c4d8`.) Because a striking type is only un-flagged at the next month change
  and re-flagged the month after, a persisting grievance strikes every other month (INFERRED from the
  two paths).
- Striking staff walk to the park mouth and stand (§3.6); they are skipped by Call Mechanic and by the
  entertainer's guard search; wages are 0.

### 4.6 Patrol areas, grab, zoom, list boxes (READ)

List-box actions are `{text row, fn}` pairs at `0x360f70..0x360fc8`:

| row | text | fn | does |
|---|---|---|---|
| 179 | Call Mechanic | `0x124250` | `0x124158(1)`, §3.2 |
| 909 | Call Cleaner | `0x124298` | **`jr ra`** |
| 79 | Set Patrol Area | `0x1242a0` | tool mode 17 (`0x388f38`, vt `0x35bc38`): Cross `0x128d98` twice = two corners → `0x1dc490`; draw `0x128ce8` highlights the rectangle |
| 971 | Grab | `0x1242d0` | tool mode **18**: `0x2b2a10[18] = 0`, so `0x125460` falls back to mode 0 (the only reader of the table is `0x1254e8`). The grab tool object `0x388f10` (vt `0x35bcc0`: enter `0x128b40`, drop `0x128b88`, cancel `0x128c08`) is constructed in `0x125f60` but never registered |
| 906 | Fire | `0x124300` | §4.2 |
| 391 | Zoom To | `0x124360` | camera to `vt+0xcc` of the selection |
| 455, 523, 446, 520, 830 | Kick Out Mechanics / Researchers / Cleaners / Entertainers / Guards | `0x124478..0x124678` | §3.6 |
| 930 | Training | `0x1246f8` | debug print "STAFF TRAINING MENU" only (the screen is opened by the UI) |

### 4.7 Laptop screens (READ; laptop-screens.md has the registry)

| screen | fn | shows |
|---|---|---|
| All Staff `main_i_staff` | `0x10bb68` / draw `0x10c138` | types present (order ent, mech, guard, res, cleaners); per staff Skill Level (bar = L × 25 %), Motivation (`0x1dc428`), Tiredness, Time Employed (`0x1dc458` days), Monthly Wage, name (`vt+0x7c`) |
| Single Staff `main_i_staff_opts` | `0x1d8ee8` (layout `0x1d8dd0`) | name + options "setpatrolarea/grab/fire/training/zoomto" (scene comment) |
| Training `main_i_staff_opts_training` | `0x1ff4a8`, `0x1ff830`, `0x1ff6f8`, `0x1ff610` | §4.4 |
| Hire `main_bh_staff` | `0x199008` | §4.1 |
| Staff Room | `0x1dcaa0` | title row 589; content not read |

### 4.8 Advisor conditions using staff (READ the helpers, callers not traced)

`0x104fb0(mask)` builds a 4×4-cell path-presence grid and a coverage figure from the patrol rectangles
(COVERAGE, blank text); `0x1053a8(mask)` = % of staff of the masked types **without** a rectangle
(SET_*_AREA); `0x105538(mask)` = average level as % of 4 (TRAIN_*); `0x105688(mask)` = maximum tiredness
(NEED_STAFF_ROOM, INFERRED); `0x103658` breakdown chooser (0x37/0x38/0x39).

### 4.9 Save and load (READ)

Save `0x1c1b08` ("Save_WriteGuards();" … per type, e.g. `0x1c25c8`, `0x1c2688`); load `0x160448..
0x1606c8` ("Load_Read<type> %d"): header counts at `+0xb` guards, `+0xc` researchers, `+0xd` mechanics,
`+0xe` handymen, `+0xf` entertainers; per staff a 0x10-byte record (`0x1dc0e8` / `0x1dc178`): `+4` hire
day, `+8` candidate slot, `+9` level | 0x80 when striking, `+0xc..+0xf` patrol rectangle, plus the
person part (`vt+0x17c/+0x184`). **Tiredness, morale and jobs are not saved**: a loaded staff member
re-rolls them in activation and starts idle (or striking).

---------------------------------------------------------------------------------------------------

## 5. Per-tick update and state machines

Every staff update (`vt+0x3c`) returns at once if `C+0x2c & 0x40` or the staff member is the focus
object `0x3953c0` (§2.5); then calls `vt+0x174` (empty), then dispatches on `C+0x2f`. Unhandled states go to the staff
base `0x1dc018`, which handles 0 (`vt+0x1a4`), 0xd (patrol), 0xf (`vt+0x1ac`), 0x1a (`vt+0x1b4`), 0x31
(go rest), 0x32 (rest) and sends the rest to the person dispatcher `0x1920d0` (1, 2, 3, 5, 0xb).
Designer names are from the debug table `0x10ca98` (states 0..0x22 only).

| state | name (`0x10ca98`) | who | handler | exit |
|---|---|---|---|---|
| 0 | Idle | all | `vt+0x1a4` find work | a job state, 0xd, 0x1a, 0x31 |
| 1 / 2 / 3 / 5 / 0xb | Set random dest / Walk to dest / Walk to waypoint / Walking / Idle(wait) | all | person `0x1920d0` (`vt+0x154/+0x144/+0x14c/+0x15c/+0x164`) | arrival by mode in `vt+0x144` |
| 0xc | Entertaining | ent | `0x12df40` | 600 ticks or no adjacent guest → 0 |
| 0xd | Patrolling | all | `0x1dc6b0` | walk (mode 1) or 5 |
| 0xe | Repairing | mech | `0x178880` | deadline → 0x11 |
| 0xf | Striking | all | `0x1db900` | strike flag clear → 0 |
| 0x10 | Closing ride | mech | `0x1785f8` | flag set → 0xe |
| 0x11 | Opening ride | mech | `0x1786d0` | ride → 7, then 0x3a |
| 0x1a | Walk to Strike | all | `0x1db808` | route → walk (mode 5) → 0xf |
| 0x1b | Cleaning up litter | handy | `0x145598` | deadline → 0 |
| 0x1f | Research | res | `0x1b61c0` | one tick → 0 |
| 0x20 | Shocked | ent | `0x12e320` | deadline → pop |
| 0x21 | Chase | guard | `0x140e08` | caught → 0x3c; timeout → 0 |
| 0x27 | ("Chucking out" debug) | guard | `0x1411c0` | route to gate → walk (mode 0xe) |
| 0x2e | — (wait at gate) | guard | none | event 9 → 0x2f |
| 0x2f | — | guard | `0x141900` | straight segment, mode 0x10 |
| 0x30 | — | guard | `0x1419c0` | route to exit, mode 9 |
| 0x31 | — (go rest) | all | `0x1dbb80` | walk (mode 0x11) or 0xd |
| 0x32 | — (resting) | all | `0x1dbf50` | tiredness ≤ 0 → 0xd |
| 0x33 | — (cleaning toilet) | handy | `0x1456d8` | deadline → 0 |
| 0x34 | — (install: arrived) | mech | `0x178a38` | flag set → 0x36 |
| 0x36 | — (installing) | mech | `0x178b10` | deadline → upgrade, 0x11 |
| 0x37 | — | guard | `0x141298` | patrol point, mode 0x15 |
| 0x38 / 0x39 | — (go repair / go install) | mech | `0x178ef8` / `0x178fe0` | walk (mode 6 / 0x14) |
| 0x3a | — (leave ride) | mech | `0x1790c8` | walk (mode 0x16) |
| 0x3b | — (dropped the guest) | guard | `0x141a70` | walk back (mode 0xf) |
| 0x3c | — (caught) | guard | `0x141120` | carry pose playing → 0x27 |
| 0x3d | — | guard | `0x141290` (empty) | — |

Modes (`C+0x2e`) seen: 1 patrol, 5 strike, 6 repair, 7 litter, 8 chase, 9 exit, 0xe/0xf gate legs,
0x10 gate straight, 0x11 staff room / generic, 0x12 toilet, 0x14 install, 0x15 guard patrol point,
0x16 leave ride. Arrival handlers (`vt+0x144`): base `0x1db970` (1 → 2, 5 → 0xf, 0x11 → 0x32), handy
`0x144ec8` (7, 0x12), mech `0x178be8` (6, 0x14, 0x16), guard `0x140cd8` (8 → 0x21, 9 → 0x3b, 0xe/0xf →
0x2e, 0x10 → 0x30/0x37, 0x15 → 0), ent/res `0x12dc88`/`0x1b6068` (base only).
Events (`vt+0x16c`, code at record `+4`): 1 route found → state 3; 2 route failed → per-mode fallback
(base: mode 5 → 0, mode 1 → 5, else 0xd); 9 (guard) → 0x2f; 5 (handyman) → drop job.

Route-system reset `0x18c340`/`0x18c4b8` freezes every guest and staff member (`+0x2c |= 0x40`) and
calls each type's release (`0x179328`, `0x1418e0`, `0x12e590`, `0x145ac8`, `0x1b63f8`), then unfreezes.

---------------------------------------------------------------------------------------------------

## 6. Scripts on the disc (READ)

- **No staff character script exists**: `/Chars/*` hold only `.mps`/`.aps` (+ one `dino.tga/.ssh`).
- Staff rooms: `JUNGLE/Features/Staff/Staff.{rss,RSE,sam}`, `HALLOW/features/horstaff/Horstaff.{rss,
  RSE}` + `horstaff.sam`, `FANTASY|SPACE/features/staffrm/Staffrm.{rss,RSE,sam}`; the `.rss` has one
  variable `VAR_STAFFIN`, `WAITANIM ANIM_Create`, and the particle/sound loop of §1.6 (SPACE uses two
  particle nodes). Compiled copies also in `JRSE/HRSE/FRSE`.
- Menus: `MENUS/main_bh_staff.sce` (StaffSelect, StaffSelectArrows, TextItems, NumericItems,
  MotivationSlider, Model), `main_i_staff.sce` (item, itemarrows, textoptions "ent/mech/guard/
  researcher/janitor", infobars, InfoValues, Model), `main_i_staff_opts.sce` (Staffname, textoptions
  "setpatrolarea/grab/fire/training/zoomto", Model), `main_i_staff_opts_training.sce` (Item, InfoText,
  TrainingVal, SkillLevelBar, CostWageVal, Model).

---------------------------------------------------------------------------------------------------

## 7. Corrections to earlier notes (READ)

- `0x1781b8` is mechanic chatter, not a route (track-ride-operation.md §6.6, coaster-operation.md §4.5).
- `0x153d70` removes from the ride **upgrade** list `0x3953e8`; `0x153d10` adds (coaster-operation.md
  §4.6, track-ride-operation.md §11 "condemned list").
- The mechanic's "flag 2" path is upgrade installation (`0x116268` raises the tier), not a service.
- `140880/140990/140C70/141020` are guard methods (native-guest-animation-readiness.md).
- shop-info-ui.md's "wage = `0x35c410[staff+0x14] × 0x35c428[staff+0x10]`" is right for the Hire
  screen's candidate record; for an employed staff member the index is the skill level `C+0x48 & 7`.

---------------------------------------------------------------------------------------------------

## Areas for the how pass

Four groups, each one agent. The port has **no staff at all** today (shop-info-ui.md), so group A is
the prerequisite for the rest; B–D can run in parallel once A's field map is agreed.

### A. The staff person: base class, lifecycle, movement, animation, save
- Person base as used by staff: `0x1912f8/0x191360`, `0x1920d0` (states 1/2/3/5/0xb), `0x191e98`,
  `0x191d78`, `0x1913b8`, `0x1920b8`, `0x1920c8`, `0x192768/0x192840/0x192560`, `0x1928b0`, `0x1925a8`,
  `0x1928f8`, `0x192c10`, `0x18da78` request flags 0x11/0x21/0x23/1/0x80 (join with
  native-route-planner.md), speed `vt+0x18c`.
- Staff base `0x1db5b8..0x1dca98`: activation `0x1db618` (random tiredness/morale, level from the
  candidate, logical 18), events `0x1db768`, arrival `0x1db970`, update `0x1dc018`, patrol
  `0x1dc558/0x1dc6b0`, the state-2 cadence (how often tiredness `+1` really fires).
- Pools/alloc/free/registration `0x147eb0`, `0x14af30..0x14b290`, `0x14b608..0x14b940`, `0x14da60`,
  the object loop and focus freeze `0x14be60`/`0x14bc28`/`0x3953c0` (and how the cursor picks a
  person at all, since the footprint hit test skips class 10), reset `0x18c340/0x18c4b8`.
- Animation: logical 18/13/16/15/11/17 against each staff `.aps` (sections table §1.4, missing
  sections 4/6 on handyman/researcher/costumes), the costume variant (registry `+0x10`), the model
  request `(C+8)->vt+0x24->+0xc(id, 0, −1)`.
- Save/load `0x1c1b08`, `0x1c25c8..0x1c2748`, `0x160448..0x1606c8`, `0x1dc0e8/0x1dc178`, `vt+0x17c/+0x184`.
- Port needs: a `Staff` object with the §2.3 fields, five pools of 5, the state table of §5, the focus
  freeze, save record.

### B. Handymen, litter, toilets, entertainers and the guests' side of them
- Handyman `0x144bf8..0x145dc0`: find work, `0x144fd8`, `0x145250` (score formula), `0x145598`,
  `0x1456d8`, `0x144ec8`, events `0x144d60`, sounds; litter objects `0x15e1c8..0x15e3c0` (`+0x24` vomit,
  `+0x2c` claimant), `0x14ad08`, `0x14b460`, `0x1824a8` (what the table `0x3ae140`/`0x37e114` is).
- Toilet cleanliness producers (`0x130948` callers) and the port's labelled stand-in to delete
  (visitors.md "DirtyLavatory").
- Entertainer `0x12da48..0x12e7d0`: `0x12dae0`, `0x12dcc8`, `0x12df40`, `0x12e278`, `0x12e320`;
  effectors `0x14cfb8/0x14d038/0x14d0a0` and the other effector producers (flags 1 and 4); guest side
  `0x20fb88` (watch, litter proximity), `0x2107a0` (watching), `0x20c930` arm 2 (heckling).
- Port needs: litter claiming and cleaning, toilet cleaning with the per-level times, entertainer
  shows and the guest watch hook.

### C. Mechanics, guards and their world hooks
- Mechanic `0x178310..0x179600`: `0x178dd8` (with the duplicated `0x153d40` branch), `0x178cf8`,
  states 0x38/0x39/0x10/0xe/0x11/0x34/0x36/0x3a, `0x178458`, `0x179328`, `0x179508`; ride side `vt+0xc4`,
  `vt+0x2cc`, `vt+0x17c`, `vt+0xf4`, `ride+0x80`, `ride+0x11c`, `0x118568/0x118678/0x115fa0/0x116020`,
  `0x116268`, status 6/7 (join coaster-operation.md §3–4 and track-ride-operation.md §6); lists
  `0x153b80`, `0x153d10/0x153d40/0x153d70`, `0x1541d0`, `0x1542a0`; ride upgrade entry `0x1d5c00`,
  `0x124270`; Call Mechanic `0x124158`; breakdown advisor `0x103658`.
- Guard `0x140540..0x141ea0`: `0x1417d0`, `0x140b48`, `0x140e08`, `0x140880/0x140990/0x1409e8`,
  `0x141120`, `0x1411c0`, `0x141900`, `0x1419c0`, `0x141a70`, `0x141298`, `0x1406d8`, `0x1416f0`;
  dispatch `0x14d3e0`, cameras `0x14d238`; gate cooperation `0x14bcc0`, `0x153298/0x1532b0`
  (`0x3953d8/0x3953dc`), the entrance table entries used (`0x1532d8`, `0x14e290`, `0x153380`,
  `0x1497c0`); the guest side: `0x20d010` (prank litter, bins), event code 4 in `0x20f588`, what the
  guest does while carried ("In the bag" 0x22).
- Port needs: breakdown repair and upgrade installation driven by mechanics (today rides break and
  are never fixed), prank/heckle response and ejection through the entrance.

### D. Management, economy, strikes, research staff and UI
- Candidates and DB factory `0x12a4b0..0x12b6d8` (`0x12a878`, `0x12a758`, `0x12b5b8`, `0x12ad28`,
  `0x12ad08`, `0x12ae68`), hire tool mode 1 (`0x128690..0x128a90`), patrol tool mode 17
  (`0x128c48..0x128e78`), the grab/mode-18 null slot and whether anything else grabs staff, the hire
  fee (tool `+8`), list boxes `0x360f70..0x360fc8`.
- Wages `0x1dc338`, `0x1008b8`, `0x100a18`, `0x100c78`, `0x16d2e8`, WAGES_HIGH producer; training
  `0x1ff4a8/0x1ff610/0x1ff6f8/0x1ff830` and `0x3988a8`; fire `0x1dc6f0`, `0x124300`.
- Calendar and strikes `0x16ae90..0x16d328` (`0x16b060`, `0x16b240` day = 0xf0000 delta units,
  `0x16c120`, `0x16c2a0`, `0x16c500`, `0x16c988/0x16c9a0`, save `0x16cb40/0x16d030`), the gate
  `DAT_002b74b0`/`0x151258`, the per-type advisor base `0x361d97`.
- Staff room: `0x1dbb80`, `0x1dbf50`, `0x1dbfd0`, kick-outs, `0x14aa90`, the feature's `+0xa2`, the
  `VAR_STAFFIN` writer, `0x130498/0x130510`, the Laser Show bit-1 question, the Staff Room screen
  `0x1dcaa0`.
- Researchers `0x1b5f28..0x1b66c0` and the research manager `0x1b6640/0x1b6798/0x1b6a38/0x1b7388`
  (the budget `+4`).
- Advisor helpers `0x104fb0`, `0x1053a8`, `0x105538`, `0x105688` and their callers; weekly `0x16bc70`
  (Security Award `0x104ce0`).
- UI: All Staff `0x10bb68/0x10c138/0x10c0b0`, Single Staff `0x1d8dd0/0x1d8ee8`, Hire `0x199008`
  (so the port's Hire screen can be wired, shop-info-ui.md).

## Still unknown (tried)

- **What writes `VAR_STAFFIN`** (the staff-room script's only input): not in any staff function read
  here; no search of the RSE VM variable writers was done.
- **Entertainer costume variant** (registry `+0x10` = 1/2 per world): the activation passes 0; no
  reader of the variant was traced.
- **`C+0x4a`** (the candidate's motivation, copied at hire): a scan of `lb/lbu …, 0x4a(` and
  `…, 0x52(` loads in the staff, UI, strike, wage and advisor ranges found only the activation's own
  clamp (`0x1db6e4`); the rest of the ELF was not scanned.
- **Hire fee**: tool mode 1 debits tool `+8` × 10 but `0x128690` does not write `+8`; `0x125460`'s
  argument passing was not followed.
- **The annual pay rise** text (row 281): no `li 0x119` in a text consumer (two unrelated hits).
- **`vt+0xc4` / `vt+0x2cc` on rides** (the repair-dispatch preconditions) and `vt+0x134` on features
  (the toilet test): not resolved per ride class.
- **Effector flags 1 and 4** producers (guest happiness/nausea): only the entertainer's flag 2 was found.
- **`0x104ce0(0x40)`** (Security Award coverage) and the callers of the four advisor helpers.
- Exact cadence of the state-2 handlers (walking tiredness, the mechanic's −2/−2): the handlers are
  read, how often the person machine enters state 2 per route is not.
- `thinmechbody.tga`, `raygun.tga`, `spear.tga`, `plackard.tga`: which model uses them was not checked.
