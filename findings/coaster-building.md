# Roller coasters (PS2): building — the tool, the track rules, the station link, save/load

Researched 2026-09-26 for strawberry (the "how" pass on roller coasters). Source: `SLES_500.32` (PAL)
decompiled in Ghidra 12.1.2 with the ghidra-emotionengine-reloaded extension. Every load-bearing constant
and branch was checked in raw R5900 MIPS (`tools/r5900dis.py`). Tables were read from the ELF, and disc data
through the port's own `TPW.PS2.Data` readers; the scratch tools stay local and only numbers are recorded here.
Overview and cross-file notes: `coasters.md`. The "what" survey: `coaster-survey.md`.

**READ** means I saw it in the decompile, the MIPS or the data. **INFERRED** means I reasoned to it.
Offsets are from the object named in each section.

Units:
- cells: park grid;
- node units: `+0x3c/+0x40` are 1/256 cell with cell centre `+0x80`;
- heights: pylon `+0x44` is in the same raw units as the station table `0x2acb60`;
- headings: 4096 per turn;
- money: park money is ×10 the displayed "$". The ×10 is READ; the display meaning comes from
  `track-ride-tool.md`.

---------------------------------------------------------------------------------------------------

## 0. Short version

- **Three tool modes.**
  - **11** places the station. It uses the shared ride-placement code, plus a coaster-only check that
    both track cells are empty land.
  - **12** lays pylons. A cursor "ghost" node is validated every frame, but it is **never drawn**.
    The preview is only ground overlays.
  - **13** edits pylons: select one, move it, change its height and bank.
- **Rules** (`0x1216d8`, all READ in MIPS):
  - The distance to the previous node is **3..8 cells** (horizontal, `0x300..0x800`). So is the
    distance to the next node.
  - The turn at the previous node is **< 90°**, or **< 45°** when the previous node is the station
    exit.
  - A pylon stands on **empty land**, or stacks on this coaster's own pylon, with at most 2 per stack
    and a stack height of at most `0x600`.
  - No other non-loop coaster node may be in the **8 neighbouring cells**.
  - The pylon may not stand on a **loop node's cell**.
  - The **segment clearance** test (`0x121000`) samples the spline 10 times. It must stay above the
    model height of any map object under it, and above 2.0 over tiles with flag bit 0. It must not
    cross another segment at the same height (`0x1209b0`).
  - A node on the **entry cell** (closing the ring) needs the last pylon's incoming heading within 90°
    of the station direction.
  - There is **no slope limit, no height-difference rule and no terrain-intersection test.**
- **Heights while building are inherited, not chosen.**
  - Every pylon laid in the build state gets the height and bank of the node the session started from.
    On a fresh track that is the **station exit node's table height** (`0x2acb60`, 175..1470).
  - Height (0..1280, step 20) and bank (±512, step 20) are changed only in the pylon sub-mode. The
    D-pad does it there, one step per frame while held.
- **Cost:**
  - Each node costs `DBA +0xd0` (100 on all 14 coasters) × 10. There is no length or height cost.
  - A loop costs 2 nodes.
  - Undo refunds 100 %.
  - Demolishing the coaster refunds half the station price and **nothing for pylons**.
- **Closing and validity.**
  - A Cross on the station entry cell with at least 1 pylon **closes the ring** (`+0x148 = 1`). No
    pylon is added there.
  - **Valid** (`+0x144`) is recomputed every update as the AND of every pylon's `+0x54` and both
    station nodes' `+0x54`.
  - Those per-node flags are written only incrementally. The tool writes them live for the edited
    node and its next node. Placing or removing a ride or shop rechecks segment clearance within
    ±8 cells.
  - The only full revalidation, `0x149908 → 0x121c80`, is **dead code** (no reference of any kind).
- **Invalid segments draw red.** All 7 mesh styles swap every texture for `red.ssh` when node
  `+0x54 == 0`.
- **An invalid or open track:**
  - loses all its trains every update (`0x1238c0`);
  - loses the "Ride" button and the ride-along camera (vt `+0x13c`);
  - raises the voice-only advisor 203 or 204 when you finish.
- **Station link.**
  - The exit and entry cells are the DBA local cells `+0xbc`/`+0xc0`, rotated into the footprint and
    stepped one cell out in direction `(dir + rotation) & 3`, where `dir` is DBA `+0xd3` bits 4–5 or
    6–7.
  - Direction encoding: 0 = z−1, 1 = x−1, 2 = z+1, 3 = x+1.
  - A worked example for all 14 coasters is in §6.4.
- **Save.** One fixed `0x418`-byte record per coaster. `+0x94` holds the ordinal, the count (bits 8–13)
  and closed (bit 14). Each pylon takes 14 bytes from `+0x96`.
- **`coaster.sam` is never read.**
  - There is no filename string for it.
  - There is no aligned pointer into the schema.
  - There is no `lui`-built address into it. The scanner propagates through `addu`, handles calls and
    jumps, and on its positive controls finds the ride-schema accessor and three indexed tables.
  - The only four raw hits were each disproved in MIPS.

---------------------------------------------------------------------------------------------------

## 1. Objects, globals and slots

### 1.1 Tool objects (READ: mode table `0x2b2a10`, vtables dumped, field uses in the handlers)

| mode | object | size | vtable |
|---|---|---|---|
| 11 station | `0x3890a0` | 0x20 | `0x35ad58` |
| 12 build track | `0x3890c0` | 0x258 | `0x35acd0` |
| 13 edit pylons | `0x389318` | 0x258 | `0x35ac48` |

The 0x258 size is INFERRED from `0x389318 − 0x3890c0`; the last field used is `+0x254`.

**Mode 11 object**:

| off | meaning |
|---|---|
| `+0x00` | cursor cell `{s16 x, s16 y, s16 z, pad}`; y = tile byte1×4 |
| `+0x08` | cost = the ride's price `0x12bc10(db, kind, index)` |
| `+0x0c` | affordable (`0x126390`) |
| `+0x10` | vtable |
| `+0x14` | rotation 0..3 |
| `+0x18` | the coaster |

**Mode 12/13 object** (same layout for both):

| off | type | meaning | written by |
|---|---|---|---|
| `+0x00..+0x07` | 8 B | cursor cell `{x, y, z, pad}`; set to (−1,−1,−1) on mode-13 enter | `0x11aef0`, `0x11c288`, `0x11cac8`, `0x11c668` |
| `+0x08` | s32 | **displayed cost** ("Cost: $%ld"): `= +0x3c` when not in the sub-mode, else 0 | `0x11aef0` @`0x11b2d4`/`0x11b2c0` |
| `+0x0c` | s32 | affordable: sandbox, or the Test Park, or `money − (+8)×10 ≥ 0` | `0x126390` |
| `+0x10` | ptr | vtable | static |
| `+0x14` | ptr | the coaster (`vt+0x64 = 0x11aa68` set, `vt+0x74 = 0x11aa60` get) | mode setter `0x125460` |
| `+0x18` | ptr | **current node**: the ghost `0x2ac438` or a selected pylon (`vt+0x7c = 0x11d1f8` returns it) | `0x11c288`, `0x11c9f8` (=0) |
| `+0x1c..+0x23` | 8 B | station entry cell (stepped), = coaster `vt+0x1b4` | `0x11c9f8` |
| `+0x24` | s32 | entry-arrow rotation `(rot + entryDir + 1) & 3` | `0x11c9f8` |
| `+0x28` | s32 | coaster closed, captured on Triangle (used by restore) | `0x11ba00` @`0x11bb48` |
| `+0x2c` | s32 | current node `+0x52`; becomes the new pylon's `+0x52` (always 0 in practice, INFERRED) | `0x11c288` |
| `+0x30` | s32 | current node `+0x53`; copied into the ghost's `+0x53` every frame | `0x11c288`, 0 on enter |
| `+0x34` | s32 | **height** for new or edited pylons | `0x11c288`, `0x11cac8` |
| `+0x38` | s32 | **bank** | `0x11c288`, `0x11cac8`; 0 if `+0x2c == 1` |
| `+0x3c` | s32 | price per pylon = DBA `+0xd0` (u16) | enter `0x11ad60` / `0x11ceb0` |
| `+0x40` | s32 | current placement valid | `0x11aef0`, `0x11b550` |
| `+0x44` | s32 | number of cells in the valid-cell field | `0x11aa70`; 0 on reset |
| `+0x48..+0x247` | u8 pairs | valid-cell field: x at `+0x48+2i`, z at `+0x49+2i`. Room for 256 cells; no bound check (INFERRED fits: ≤ ~180 cells can pass the 3..8 rule) | `0x11aa70` |
| `+0x248` | s32 | backup pylon count (mode 13) | `0x11cbf0`, `0x11ce70` |
| `+0x24c`, `+0x250` | ptr | backup allocation handle and data, 0x18 bytes per pylon | `0x11cbf0` |
| `+0x254` | s32 | **1 = the ghost is selected (build state), 0 = a real pylon is selected** | `0x11c288` (mode 12 only), `0x11ceb0` (0) |

Backup entry (0x18 bytes, `0x11cbf0`):
- `+0` 8-byte cell struct (`0x19a2b0`);
- `+8` height;
- `+0xc` bank;
- `+0x10` `+0x52`, `+0x11` `+0x53`;
- `+0x14` valid.

### 1.2 Globals (READ, x-refs in `b1.c`)

| addr | meaning | writers → readers |
|---|---|---|
| `0x2ac428` | **pylon sub-mode** (a pylon is selected for Next/Move/Prev and height/bank) | `0x11c118` (1), `0x11c180`/`0x11c9f8` (0) |
| `0x2ac42c` | **moving** (the selected pylon follows the cursor) | `0x11b550`, `0x11c288`, `0x11a7c0`, `0x11ceb0` |
| `0x2ac430` | **stats screen** shown | `0x11bbd8` (1), `0x11ba00`/`0x11bca8` (0) |
| `0x2ac434` | the build started in mode 11 (manager's previous mode == 11 at Triangle) | `0x11ba00` → `0x11bca8` |
| `0x2ac438` | **the ghost node** (0x620 bytes), shared by every coaster; owner reset by `0x11fd28` | many |
| `0x37e23c` | valid-cell scan complete | `0x11aa70` (1), `0x11aca8` (0) |
| `0x37e240` | next scan row | `0x11aa70`, `0x11aca8` |
| `0x2aca58` | 8 B: cursor cell when the current node was selected (the revert position) | `0x11c288` → `0x11ba00` |
| `0x2aca60` | the selected node's `+0x38` at selection. **Written only; no reader** (x-ref) | `0x11c288` |

Manager (`0x14e180()`, see `track-ride-tool.md` §1.1):
- `+0x20`/`+0x24` hold the current and previous mode.
- `+0x50`: while 1, `0x125ae8` skips the cursor-motion call `0x125680`, so the **cursor is frozen**.
  It is set by `0x11c118` and `0x11bbd8`, cleared by `0x11c180` and `0x11bca8`.

### 1.3 Slots and buttons (READ, vtables dumped here)

| slot | 11 `0x35ad58` | 12 `0x35acd0` | 13 `0x35ac48` |
|---|---|---|---|
| `+0x0c` enter | `0x11a7c0` | `0x11ad60` | `0x11ceb0` |
| `+0x14` cursor (per frame) | `0x126558` | `0x11aef0` | `0x11aef0` |
| `+0x1c` draw | `0x1268f0` | `0x11b2f8` | `0x11b2f8` |
| `+0x24` abort | `0x126898` | `0x126460` (set mode 0) | `0x126460` |
| `+0x2c` action 6 | `0x11a858` place | `0x11b550` | `0x11b550` |
| `+0x34` action 7 | `0x1271f0` → abort | `0x11ba00` | `0x11ba00` |
| `+0x3c` action 8 | `0x126778` rotate (standard layout) | `0x11b898` | `0x11cf88` |
| `+0x44` action 9 | `0x126808` rotate (alternate layout) | `0x11b828` | `0x11b828` |
| `+0x4c` / `+0x54` | `0x126338` set mode / `0x126370` get mode | same | same |
| `+0x64` / `+0x74` | `0x127318` / `0x127310` | `0x11aa68` / `0x11aa60` | same |
| `+0x7c` | `0x1272d8` (returns 0) | `0x11d1f8` (returns `+0x18`) | same |

Action to button mapping, from `track-ride-tool.md` §1.1:
- standard layout: 6 = Cross, 7 = Triangle, 8 = Circle, 9 = Square;
- when `*0x395430 == 8`: 6 = Circle, 7 = Cross, 8 = Triangle, 9 = Square.

The button bar `0x13e340(ui, a, b, c, d)` fills slots 0..3 in the order **Triangle, Circle, Cross,
Square** for the standard layout. That order is INFERRED: mode 11's default bar at `0x2b28e0+11·8`
is (Cancel, Rotate, Place, blank), and Rotate is Circle there (`0x126778`); the alternate table
`0x2b2978` moves Rotate to slot 3 = Square (`0x126808`).

### 1.4 The cursor in modes 11–13 (READ)

- `0x1252a8` returns 1 for modes 2–7 and 11–16, so all three coaster modes use the **snapped**
  branch of `0x125680`.
  - On each pad-repeat pulse (`0x1817e0`), the target moves by the vector `0x395380 >> 5`.
  - That vector is `(0, 0, 0x1000)` from `0x2b73f8`, rotated by the camera yaw quantised to 90°
    (`& 0xfc00`, `0x14fbd4`, through the 4.12 fixed-point `0x194a00`). **One pulse is 128 units =
    half a cell.** What the resulting step feels like per press was not traced (`0x1812f8`).
- The smoothed cursor (`0x125c98`) closes half the remaining distance per frame. It is clamped to
  `[0, (W−2)·256] × [0, (H−2)·256]`. The tool receives `{x = pos>>8, y = byte1·4 of that tile,
  z = pos>>8}`.

---------------------------------------------------------------------------------------------------

## 2. The tool flow

### 2.1 State table

States:
- **S11**: mode 11.
- **B**: build (mode 12, ghost selected, sub-mode 0, not moving).
- **E**: pylon selected, sub-mode 1.
- **M**: moving (sub-mode 0, moving 1, `+0x254 == 0`).
- **ST**: stats screen.

In E and ST, the manager `+0x50` freezes the cursor.

| state | input | effect (READ; addresses in §2.2–2.6) | next |
|---|---|---|---|
| — | build menu, catalogue kind 1 (`0x197f48`, jump table `0x364ec0` → `0x19804c`) | set mode 11 | S11 |
| S11 | enter `0x11a7c0` | take a coaster from the pool (`0x14a4a0(ordinal)`; `0x195e20` always returns 1); moving = 0; `0x126490`: tutorial event 0x43, rotation 0, station at cursor − footprint/2 | S11 |
| S11 | per frame `0x126558` | cost = the station price; position the station (`vt+0xbc = 0x1e1ed8`); preview `0x1e3978` (§6.3) | S11 |
| S11 | Cross `0x11a858` | if placeable bit 25 of `+0x98` is clear (`0x1e1e08`): sound 0xaf, stay. Else: tutorial 0x45; if `0x14ccb0() == 0` advisor **201** (stock = (park 2 ? 14 : 2) − in-use count `*(0x395298)+0xc`, which already includes this coaster: "that used the last slot" is INFERRED); place (`vt+0x164 = 0x11fd28`: map object, station nodes, ghost owner); set mode 12; sound 0x8e; debit price×10 | B |
| S11 | Circle (Square in the alternate layout) | rotation = (r+1)&3 → `vt+0xb4`; sound 0x1f | S11 |
| S11 | Triangle `0x1271f0` → `0x126898` | return the coaster to the pool (`0x14a7b0(ride, 0)`), set mode 0, sound 0x131 | mode 0 |
| — | ride list box "Build Track"/"Edit Track" (`0x360f50/58`, fn `0x1240e8`) | set mode 12 (the ride is passed from the UI selection) | B |
| — | ride list box "Edit Pylons" (`0x360f68`, fn `0x124118`) | tutorial 0x3a, set mode 13 | E |
| B | enter `0x11ad60` | §2.2 | B |
| B | per frame `0x11aef0` | ghost follows the cursor; validate (§4); scan the valid-cell field; `+8` = price | B |
| B | Cross `0x11b550` | if the ghost is invalid or 32 pylons exist: sound 0xaf. Else add a pylon (`0x121d68`). If that **closed** the ring: tutorial 0x46, then: previous mode ∈ {5,6,7,8,11,12,13} → select the first pylon, **no charge** → E; else set mode 0, **charge price×10**, sound 0x8e → mode 0. Not closed: re-link the ghost after the new pylon, sound 0x8e, charge price×10 | B / E / mode 0 |
| B | Circle `0x11b898` | **undo** (§2.4) | B |
| B | Square `0x11b828` | if the coaster can loop (`0x122e48`, table `0x2acad0`) and not moving: **loop** `0x11c668` (§4.10) | B |
| B | Triangle `0x11ba00` | if not closed: last.next = 0, advisor **204**; test run and stats (`0x11bbd8`) | ST |
| E | enter mode 13 `0x11ceb0` | backup all pylons; select the first editable pylon after the exit node; reset (trains removed); sub-mode on; cursor = (−1,−1,−1), re-synced next frame | E |
| E | per frame | cursor pinned to the pylon (`0x11cac8` warps it); D-pad (held mask `0x181860`) changes height/bank (§2.5); the pylon and its next are revalidated | E |
| E | Circle | **Next** `0x11c468` (mode 12 or 13) | E |
| E | Square | **Prev** `0x11c540` | E |
| E | Cross | moving = 1; `0x11c180` (sub-mode 0, cursor free, bar "Exit, blank, Place, blank") | M |
| E | Triangle | as in M: an invalid selected pylon (e.g. after a height change) is moved back to `0x2aca58` with sound 0xaf; then the footprint is marked, the backup freed, `0x11c180`, finish → stats | ST |
| M | per frame | the selected pylon is moved to the cursor, its stack above follows (`0x11a940`); it and its next are validated live | M |
| M | Cross | if invalid: sound 0xaf, stay. Else moving = 0, `0x11c118` (**no charge**) | E |
| M | Triangle | if invalid: sound 0xaf and move back to `0x2aca58` (position only; height/bank are not reverted). Mark the footprint, free the backup, `0x11c180`, finish | ST |
| M | Circle / Square | ignored (Circle returns because sub-mode == 0; Square because moving) | M |
| ST | Cross (OK) `0x11bca8` | if `0x2ac434` (came from the station): remove the trains, set **mode 3** (queue path tool); else mode 0 | 3 / 0 |
| ST | Triangle (Back) | stats = 0. If the ghost is selected: `0x11c180` → B. Else if moving: `0x11c180` → M. Else `0x11c118` → E | B/M/E |
| ST | Circle / Square | ignored | ST |

Reachability notes (INFERRED from the transitions above):
- **Mode 13's Circle "restore backup"** (`0x11cf88` → `0x11cd38`) runs only in (sub-mode 0, moving 0,
  stats 0). No transition in mode 13 reaches that state, and Triangle frees the backup
  (`0x11ce70`). The restore is unreachable.
- **The M-state Cross branch at `0x11b6fc`** (mark the footprint, free the backup, **set mode 0 and
  charge `+8`×10**) needs `+0x254 == 0`, sub-mode 0 and moving 0. That state is also not reached.
- **Before the ring closes, mode 12 never enters E**, because nothing calls `0x11c118`. So while
  laying track there is no way to change the height.

### 2.2 Entering mode 12 (`0x11ad60`, READ in MIPS)

1. `+0x44 = 0`, `+0x30 = 0`. Let `start` = the last node (`0x120508`: `coaster + 0x794 + count·0x620`,
   which is the exit node when count is 0).
2. **If the ring is closed**: reopen it (`0x1234d8`: `+0x148 = 0`, entry.prev = 0), and set `start` =
   the **entry node** (`+0x174`).
3. Unlink the ghost (prev, next, above, below = 0). Configure it with `0x11a940`: cell = start's cell,
   **height = start's height**, **bank = start's bank**, `+0x52 = 0`, `+0x53 = 0`.
4. Reset (`0x11c9f8`):
   - sub-mode 0, `+0x44 = 0`, `+0x18 = 0`;
   - `+0x1c` = entry cell, `+0x24` = entry-arrow rotation;
   - **remove all trains** (`0x1224c8`).
5. Select the ghost (`0x11c288`):
   - tool `+0x2c/+0x30/+0/+0x34/+0x38` are read from the ghost;
   - warp the cursor;
   - ghost.prev = last, ghost.next = 0, last.next = ghost;
   - `+0x254 = 1`, moving = 0;
   - restart the scan; save `0x2aca58`.
6. Button bar `0x13e340(Exit 0x1ff, Undo 0x395, Place 0x1b, Loop 0x414 or blank 0x136)`.
7. `+0x3c` = DBA `+0xd0`.

**Consequence (INFERRED from READ steps).** In the build state `+0x34`/`+0x38` are never changed:
- Cross resets the ghost height to `+0x34` and re-selects the ghost, which reads it back.
- So **every pylon laid in one session has the start node's height and bank**.
- On a fresh track that is the exit node's table value (§6.2): for example 375 for Temple of Gloom,
  or 1470 for The Shocker, which is above the tool's own 0x500 clamp.
- After an Undo, the ghost takes the new last node's height and bank.
- Heights are then edited per pylon in E. Closing the ring from a station session enters E on the
  first pylon for this reason.

### 2.3 The per-frame cursor (`0x11aef0`, READ in MIPS `0x11afb0..0x11b2f0`)

1. `0x126390` (affordable). If on the stats screen, return. If the cursor is (−1, −1), take it from
   the current node and warp; else take it from the manager.
2. If the scan isn't complete: `0x11aa70` scans 4 more rows (§4.11).
3. **Stacking preview.**
   - `s2` = current.below, or 0 for the ghost.
   - `s0` = `0x14c688(cursor, 9)`, the bottom coaster node at the cursor.
   - If `s0` is ours (`0x122c18`), is not the exit or entry node, is not current, and is not a loop
     node: climb to the top of its stack, stopping under current.
   - If that top is not current, current.prev, current.next or current.above: set current.below =
     top, and (if `s2`) `s2.above = current`. Otherwise current.below = 0 and `s2.above = 0`.
4. If `+0x2c == 1`, bank = 0. Ratchet sounds `0x199ae8`/`0x1999e8`, bank 2 event 0x45, play while
   the height or bank differs from the node's.
5. **Ghost selected** (`+0x254 = 1`):
   - move the ghost to the cursor;
   - `valid = 0x1216d8(coaster, ghost, clearance = 1)`; if not affordable, valid = 0;
   - `+0x40 = valid`, ghost `+0x54 = valid` (`0x19aec0`);
   - `0x11a940(ghost, kind = +0x30, cursor, height, bank)`, `0x19ad28`.

   **Pylon selected** (`+0x254 = 0`):
   - `0x11a940(pylon, …)` moves it and its stack above; each stacked node is revalidated without
     clearance (`0x199bf0`);
   - `0x19ad28`;
   - `valid = 0x1216d8(pylon, 1)` → `+0x54`, `+0x40`;
   - its next (if any and not the ghost) is recomputed and validated the same way. If the next is
     invalid, `+0x40 = 0`.
6. In the sub-mode: `+8 = 0` and pad handling `0x11cac8` (§2.5). Otherwise `+8 = +0x3c` (price).

`0x11a940` (READ) is: node `vt+0x3c` (window); owner = coaster; **`+0x52 = 0`**; `+0x53` = its 4th
argument; position; bank (`0x19c818`, **forced 0 for Moonshot**, world 3 park 0 ord 0); height;
`0x19ad28`.

### 2.4 The action handlers (READ in MIPS)

**Cross `0x11b550`**:
- stats → `0x11bca8`.
- sub-mode → toggle moving: if moving, clear it and return; else set it and `0x11c180`.
- Otherwise:
  - valid = the loop node's cached `+0x40`, or `0x1216d8` at the cursor;
  - if invalid → sound 0xaf;
  - if moving → commit (§2.1);
  - if a pylon is selected → `0x11b6fc` (unreached);
  - otherwise build:
    - count ≥ 32 → sound 0xaf;
    - `r = 0x121d68(coaster, cursor, +0x34, +0x38, +0x2c, kind 0)`;
    - if closed → tutorial 0x46;
    - `r == 0` means the ring closed: branch on the previous mode (`0x1252e0`: previous mode − 5 < 4,
      or 11, 12, 13) as in the state table.

  The Cross that closes the ring **and leaves the tool** falls through to sound 0x8e and **debits
  `+8`×10 = 1000**, with no pylon added. That is READ: path `0x11b6b4 → 0x11b6cc → 0x11b740`. That it
  is unintended is INFERRED.

**Circle `0x11b898`**:
- stats → nothing.
- A pylon is selected: only in the sub-mode → **Next** `0x11c468`.
- Ghost selected and not in the sub-mode → **Undo**:
  1. `pos` = the last node's cell;
  2. `0x122460` removes the last pylon; if one was removed, **refund `+8`×10**;
  3. `0x11b7b0`: if the new last node is a loop lead-in (`+0x53 == 1`), remove it too and refund;
  4. `0x11a940(ghost, kind = new last +0x53, pos, new last height, new last bank)`;
  5. select the ghost; ghost.next = 0; restart the scan.
- Undo on a loop therefore removes both loop nodes. Undo on an extended loop removes one loop node per
  press. After an undo that leaves a loop node (kind 2) last, the ghost carries kind 2. Its heading
  then gets +0x400 in `0x19aa48`, which changes the turn test (INFERRED side effect).

**Square `0x11b828`**:
- stats → nothing;
- sub-mode → **Prev** `0x11c540`;
- else if not moving and the coaster loops → `0x11c668`.

**Triangle `0x11ba00`**: see the state table. When the ring is not closed it raises advisor 204
(`0x11bb70`), then `0x11bbd8` raises 203 if invalid, **204 again** if valid but open, or tutorial
0x49 if valid and closed. It sets stats = 1 and runs the **test run** `0x122d48` (area D). The bar
becomes (Back 0x221, blank, OK 0x243, blank) and the manager `+0x50 = 1`.

**Next `0x11c468` / Prev `0x11c540`**:
- Stop the ratchet sounds and re-mark the current pylon's footprint.
- Walk `+0x30` (Next) or `+0x2c` (Prev) until `0x121cf8` accepts a node: not a station node and
  `+0x53 == 0`.
- Wrap: Next restarts at the exit node's next; Prev at the last node.
- Mode 12 only: a null link starts from the ghost.
- **Loop nodes and station nodes can never be selected, moved or re-heighted.**

**`0x11c288` select** (used by all of the above):
- If the previous current node was a pylon, re-mark its footprint (`0x19a828`).
- Take `+0x2c/+0x30/cell/height/bank` from the node. Clamp: height < 0 → 0; `+0x2c > 1` → 0; loop
  → bank 0.
- Warp the cursor.
- In mode 12, selecting the ghost re-links it after the last node and sets `+0x254 = 1`; selecting a
  pylon sets `+0x254 = 0`.
- If a pylon with nothing below is selected, **unmark its footprint** (`0x19a930`), so it does not
  block itself.
- Restart the scan; save the cursor to `0x2aca58`.

### 2.5 Height and bank (`0x11cac8`, sub-mode only, READ)

Per frame, using the **held** mask `0x181860(0)` (the current buttons, not the edge):

| D-pad | effect |
|---|---|
| Up (1) | height +0x14 |
| Down (2) | height −0x14 |
| Left (4) | bank −0x14 |
| Right (8) | bank +0x14 |

Then clamp height to **[0, 0x500]** and bank to **[−0x200, +0x200]** (±45°). Set cursor = the node's
cell and warp. Holding a direction therefore changes the value 20 units per frame (INFERRED from the
held mask).

### 2.6 Drawing and on-screen text (`0x11b2f8`, READ)

Everything is a ground overlay (`0x1504f8`, two quads per tile that follow the ground). The ghost
node's pylon and track are **forced invisible**: `0x19d1e0` zeroes both flags when the node is
`0x2ac438` (MIPS `0x19d2a0..0x19d2b0`).

Placed pylons and track are real geometry. A pylon being moved or re-heighted moves live, and its
segment turns red live.

| what | where | texture id (`0x35b5e0` table / literal) |
|---|---|---|
| cursor tile, current placement invalid | cursor | idx 1 → **175** `red` |
| cursor tile, current node is a loop node | cursor | idx 8 → **174** (not loaded, see below). INFERRED unreachable: the current node is the ghost (`+0x52` forced 0) or a selectable pylon, and loop nodes are not selectable |
| cursor tile, otherwise | cursor | idx = `0x1e81e0(tile, 10, 1)`, the path tool's verdict: 0 → 165 teal; 1 → 175 red (flag 2, cash, …); 6 → 173 "link" on a tile already of kind 10 (a pylon cell) |
| valid-cell field (§4.11) | each listed cell, once the scan is complete | **171** when the current placement is valid, **175** when not (`0x11acc8`) |
| station entry cell | `+0x1c`, unless the cursor is on it | **166** chevron, rotation `+0x24` |
| selected pylon | 3D highlight slot `0x1b3110` (3 slots at `0x397470`, intensity 0x80) | — |
| "Pylon Stock %d" | screen (36, 196), `0x35b604..0x35b60c` = 36, 196, 10; white | text row **0xdf** `STR_PYLONS_LEFT`, value `32 − count` (loop nodes count) |
| "Cost: $%ld" | at the cursor (`0x1b3210`, when tool `+8 ≠ 0`) | row **0x23** `STR_COST`; shows 100 in B and M (moving is free), hidden in E |

⚠ `ghost-cursor.md` §1 found that the tile loader `0x220dc0` **does not load ids 171 and 174**; their
handle slots are gaps. So how the valid-cell field (171) and the loop-cursor tile (174) look on a PS2
is **not established**.

Button bars (`0x13e340` order Triangle, Circle, Cross, Square; rows resolved from the EUR text DB):

| situation | bar | rows |
|---|---|---|
| build, loops allowed | Exit / Undo / Place / Loop | 0x1ff `STR_GIZMO_CPP_EXIT`, 0x395 `_UNDO`, 0x1b `_PLACE`, 0x414 `STR_PYLON_TYPE_LOOP` |
| build, no loops | Exit / Undo / Place / (blank) | …, 0x136 `STR_GIZMO_CPP_BLANK` |
| pylon sub-mode | Exit / Next / Move / Prev | 0x1ff, 0x248 `_NEXT`, 0x118 `_MOVE`, 0x184 `_PREVIOUS` |
| moving | Exit / (blank) / Place / (blank) | 0x1ff, 0x136, 0x1b, 0x136 |
| stats | Back / (blank) / OK / (blank) | 0x221 `_BACK`, 0x136, 0x243 `_OK`, 0x136 |
| station (mode default `0x2b28e0+11·8`) | Cancel / Rotate / Place / (blank) | 0x1c `_CANCEL`, 0x231 `_ROTATE`, 0x1b, 0x136 |

`STR_CURSORCOASTER_HEIGHT` (row 0x55) and `STR_CURSORCOASTER_BANKING` (row 0x2b0) have **no user**:
- no instruction in `.text` loads 0x2b0 as an immediate;
- none loads 0x55 into `$a0` next to a call.

The tool shows no numeric height or bank readout that I could find.

Sounds (`0x111150(snd, bank, id, 0)`):
- 0xaf error (bank 1);
- 0x8e place;
- 0x1f debit (`0x126408`) and rotate;
- 0x131 station abort.

Tutorial events (`0x107390(0x2aa720, ev)`):
- 0x43 station tool opened;
- 0x45 station placed;
- 0x46 ring closed;
- 0x49 finished valid and closed;
- 0x3a "Edit Pylons".

Advisor records (`0x2a6ac8 + 56·id`, fields per `advisor.md`):

| id | key | text row | voice |
|---|---|---|---|
| **201** | `COASTER_STOCK_OUT` | 229 "All of the available roller coasters have been used. You can only build more if you delete existing ones" | sound 26, lip `ADDS_45` |
| **203** | `COASTER_COLLISION` | 310 (no text) | sound 42, lip `PS2_4` |
| **204** | `COASTER_TRACK_INCOMPLETE` | 310 (no text) | sound 44, lip `PS2_6` |

So 203 and 204 are **voice only**.

### 2.7 Costs and refunds (READ)

| event | money (park units = displayed $ × 10) | where |
|---|---|---|
| station placed | −price × 10 (price = `0x12bc10(db, kind, index)`, the ride's purchase price) | `0x11a858` → `0x126408` |
| pylon placed (Cross, not closing) | −DBA `+0xd0` × 10 = −1000 | `0x11b740` |
| ring closed, staying in the tool (station session) | 0 | `0x11b6a4` returns before the debit |
| ring closed, leaving the tool | −1000 (no pylon added; INFERRED quirk) | `0x11b740` |
| loop | −1000 per node: 2 for a loop, 1 per extension | `0x11c738`, `0x11c834`, `0x11c974` |
| undo | +1000 per removed node (loop lead-in included) | `0x11b944`, `0x11b7b0` |
| move / height / bank | 0 | — |
| coaster demolished | +(station price >> 1) × 10; **pylons not refunded** (`0x11fd80 → 0x1223d0` clears without credit) | `0x1e12e0` |

- Sandbox (`*0x2a60b8`) and the Test Park (park index 2, `0x154428`): `0x100698` returns without
  debiting, and `0x126390` treats everything as affordable.
- `0x100698` also **refuses silently** (returns 0, no debit) when money < amount and `fin+8 == 0`.
  The pylon ghost is already invalid when unaffordable. The loop's second node and the
  leave-the-tool charge are not gated by affordability.
- **There is no per-length, per-height or per-2D-length cost.** The only multiplier anywhere is
  `+8 × 10` with `+8 = +0x3c`. The `.sam` `fCostPerUnit/2DLength/Height` are unused (§8).

---------------------------------------------------------------------------------------------------

## 3. Adding, removing, closing, reopening (READ)

```c
Node* AddPylon(C, cell{x,y,z}, height, bank, loopFlag, kind)          // 0x121d68
{
    if (C.count >= 32) { debug("TRACK PYLON OVERFLOW"); return 0; }    // slti 0x20 @0x121dac
    if (cell.xz == EntryCell(C) /*vt+0x1b4, 0x1207f0*/ && C.count != 0) {
        CloseRing(C); return 0;                                          // 0x120868
    }
    last = C + 0x794 + C.count*0x620;          // 0x120508: exit node when count == 0
    n    = C + 0xdb4 + C.count*0x620;          // array order == ring order (append only)
    n.owner = C; n[+0x52] = loopFlag; n[+0x53] = kind; n[+0x54] = 1; LoadPylonModel(n);
    n.pos = {cell.x<<8, cell.y, cell.z<<8}     // 0x199fb8 adds +0x80 to x,z; y re-derived (0x19cae0)
    n.height = height; n.bank = bank;          // bank forced 0 on Moonshot
    n.prev = last; n.next = 0; n.above = n.below = 0; last.next = n;
    b = BottomNodeAt(cell, 9);                 // 0x14c688
    if (IsOurs(C, b) && b != last) { top = TopOf(b); top.above = n; n.below = top; }   // stack
    Recompute(n);                              // 0x19ad28
    MarkFootprint(n);                          // 0x19a828: tile kind := 10 (1x1 cell)
    RegisterMapObject(n);                      // 0x14da60 (kind 9, found by 0x14c688)
    C.closed = 0; C.count++;
    return n;
}
CloseRing(C)    { e = C+0x174; last = LastNode(C); e.prev = last; last.next = e;
                  Recompute(e); Recompute(last); C.closed = 1; RemoveTrains(C); }   // 0x120868
RemoveLast(C)   { if (count) { count--; Unlink(C+0xdb4+count*0x620);  // 0x122320: vt+0x10c,
                                                         // relink prev/next and above/below,
                                                         // unregister
                               LastNode(C).next = 0; }
                  C.closed = 0; return wasNonZero; }                                 // 0x122460
Reopen(C)       { C.closed = 0; (C+0x174).prev = 0; }                                // 0x1234d8
ClearAll(C)     { unlink every pylon; closed = count = 0; hide ghost/station models;
                  exit.next = 0; }                                                   // 0x1223d0
```

- There is **no insertion**: new pylons are always appended after the last node, and only the last
  node can be removed. So the array order is the ring order.
- Closing adds no node. Nothing validates inside `AddPylon`. The Cross handler validated the ghost
  on the entry cell first, including the closing rule of §4.9.
- **Where the new node goes:** at the cursor cell's centre. Its base y comes from `0x19cae0`:
  - the terrain `0x149d90(cell.x<<8, cell.z<<8)` at the cell's corner (fraction 0) when nothing is
    below;
  - otherwise the y of marker 0x100000 on the model of the pylon below, × 256.

  The track point is base + the sum of heights down the stack (`0x19a368`). The units of y are area
  A's.

---------------------------------------------------------------------------------------------------

## 4. The placement rules

### 4.1 `Valid(C, N, doClearance)` = `0x1216d8` (READ in full MIPS `0x1216d8..0x121c7c`)

The 4th argument (`$a3`) is passed but never read.

```c
P = N.prev ? N.prev : N;   X = N.next ? N.next : N;   ok = 1
d1 = HDist(N, N.prev);  d2 = HDist(X, X.prev)          // 0x19adf0, see 4.2
if (N.loopFlag(+0x52) == 1) goto NEIGHBOURS;            // loop nodes: only the 8-neighbour test
if (!(0x300 <= d1 <= 0x800)) ok = 0;                    // 0x121784..0x1217bc
if (!(0x300 <= d2 <= 0x800)) ok = 0;
for (r = exit; r && r != entry; r = r.next)             // no pylon on a loop node's cell
    if (r != N && r.loopFlag == 1 && Cell(r) == Cell(N)) ok = 0;
lim = (P == exit) ? 0x200 : 0x400;                      // 45° after the station, else 90°
t   = abs(N.heading - P.heading)   (as s16)
if (!(P.loopFlag == 1 && N.next != 0))                  // turn test skipped after a loop node
    if (lim <= t && t < 0x1000 - lim) ok = 0;           // i.e. valid iff |turn| < lim (wrapped)
if (!ok) return 0;
if (InGrid(Cell(N))) {                                  // 0x149d20; off-grid skips to NEIGHBOURS
  tile = Tile(Cell(N));
  if (tile.kind == 10) {                                // a pylon footprint is here: stacking
    b = BottomNodeAt(Cell(N), 9);
    if (b && b != N && b != N.prev) {
      if (b.loopFlag == 1)       ok = 0;                // never on a loop node
      else if (!IsOurs(C, b))    ok = 0;                // another coaster's pylon
      else if (b == exit)        ok = 0;                // the exit node's cell
      else {                                            // 0x121d60() == 1 always; the
                                                        // "only on the entry node" branch
                                                        // is dead
        n = 0; for (s = b; s.above && s != N; s = s.above) n++;
        if (toolMode in {12,13} && tool.current.below == s) n++;  // vt+0x7c = 0x11d1f8
        if (n >= 2) ok = 0;                             // stack depth <= 2 incl. the new node
      }
    }
  } else if (!EmptyLand(tile)) ok = 0;                  // 0x1e64d0
}
NEIGHBOURS:
if (ok) for (d in {(1,0),(1,1),(0,1),(-1,1),(-1,0),(-1,-1),(0,-1),(1,-1)})   // table 0x2acec8
    if (InGrid(c = Cell(N)+d) && Tile(c).kind == 10 && (m = BottomNodeAt(c,9)) && m != N
        && m.loopFlag != 1) ok = 0;                     // any coaster, incl. station nodes
if (!ok) return 0;
if (doClearance && N.loopFlag != 1 && P != N) ok = SegmentClear(C, P, N);   // 0x121000
if (!ok) return 0;
if (StackHeight(N) > 0x600) return 0;                   // 0x19a6e8: sum of +0x44 over the stack
if (Cell(N) == EntryCell(C)) {                          // closing
    t = abs(exit.heading - P.heading)  (as s16)
    if ((u16)(t - 0x400) <= 0x7ff) return 0;            // valid iff t < 0x400 or t >= 0xc00
}
return 1;
```

### 4.2 The quantities used (READ unless marked)

| quantity | definition |
|---|---|
| `HDist(a, a.prev)` | `(int)sqrtf(dx² + dz²)` on `+0x3c/+0x40`, 1/256 cell (`0x19adf0 → 0x1958b8`). Nodes sit on cell centres, so **valid iff 9 ≤ Δx² + Δz² ≤ 64** in cells (INFERRED from the arithmetic; `(int)` truncates, and perfect squares are exact in float) |
| `heading` (`+0x4a`) | from `N − N.prev` in x,z (`0x19aa48`): 0 = +z, 0x400 = +x, 0x800 = −z, 0xc00 = −x; otherwise `0x400 − atan(dz/dx)` for dx > 0 and `0xc00 − atan(dz/dx)` for dx < 0 (`0x195b40`: `atan(v/4096)` in 4096/turn). Equal to `atan2(dx, dz)` (INFERRED). **+0x400 when the node's kind is 2 (loop)** |
| station exit heading | the exit node's prev is the entry node (`0x122060`), so it is the direction entry → exit, **the station's travel direction** (§6.4) |
| `EmptyLand(tile)` | `0x1e64d0`: kind ∉ {2 path, 4 queue, 5, 7, 8, 10 pylon, 12, 13, …}, flags `0x10/0x08/0x01/0x02` clear (`track-ride-tool.md` §5.1). Station and other ride tiles carry flag 0x10, so pylons can't stand on them |
| `BottomNodeAt(c, 9)` | `0x14c688`: walks the map-object list `0x395208` for an object whose `vt+0xa4` kind is 9 (a coaster node: `0x19a6e0` returns 9; footprint 1×1 from `0x19a6a8`/`0x19a6d8`) and whose footprint holds c. Requires `0x1e62b8(tile)` and kind 10; returns the **bottom** of the stack (walks `+0x38`) |
| `IsOurs(C, n)` | `0x122c18`: n is C's exit or entry node, or one of C's `count` pylons |
| `StackHeight` | `0x19a6e8`: from the top of N's stack, sum `+0x44` down through `+0x38`; valid iff sum ≤ 0x600 |

### 4.3 Segment clearance `SegmentClear(C, A = prev, B = node)` = `0x121000` (READ in MIPS)

```c
if (Cell(A) == Cell(B)) { if (A.loop || B.loop || A.above || B.above) return 0; }
if (B == exit) return 1;                         // the station segment entry->exit always passes
RebuildSpline(B);                                // 0x19b208: control points from the window
clear = 0.0f; last = (-1,-1)                     // f20; 0x35b040 = {0xffff,0xffff}
for (t = 0.0f; t < 1.0f; t += 0.1f) {            // 0x3dcccccd; 10 samples t = 0 .. 0.9
    p = Eval(B, t)                               // 0x19bda0: the curve arriving at B, in cells
    c = ((int)p.x, (int)p.z)
    if (c != last) {
        last = c
        if (Tile(c).flags & 1) clear = 2.0f;     // 0x121250: assignment, not max
        o = AnyObjectAt(c)  /*0x14c688(c,0)*/;  k = BottomNodeAt(c, 9)
        if (o && !k && o != A && o != B
            && !((A or B is exit/entry) && o == C)      // own station ignored for station segments
            && (m = o.vt+0x14 model))
            clear = max(clear, ModelHeight(m));  // 0x17c578: *(model+0xc)+0x64 (float)
    }
    if (p.y <= clear) return 0;                  // 0x121340: c.ole.s
}
// then segment-vs-segment, see 4.4
```

- **The clearance value carries over.** Once a segment has passed over something tall, every later
  sample of that segment must stay above it, even after leaving that object's cells. A flag-bit-0
  tile resets it to exactly 2.0, which can raise or lower it. It starts at 0.0 for each segment.
- **Terrain is not sampled.** `p.y` (an absolute spline height) is compared with object *model*
  heights. Whether those units agree is area A's question. This is the only height test for rides,
  shops and scenery under the track.
- Tile byte 7 bit 0 is set, with bits 1 and 5, on the cells the park loader `0x14e5b0` fills from the
  authored byte0 bit 0 (`paths.md`, "skipped cells"). **Paths and queues are tiles, not objects**, so
  they never raise the clearance.

### 4.4 Segment against other segments (READ)

After the samples, still in `0x121000`:

1. `boxA` = cells from `Cell(A)` to `Cell(A) + ((B − A) >> 8)` in x,z.
2. For every cell `c` in `Cell(A) + [−10, 10]²` (`0x121388`, `0x1214b8`, `slti 0xb`) whose tile kind
   is 10:
   - `n = BottomNodeAt(c, 9)`, `m = n.prev`;
   - skip if `n` is null, A, B, the exit or the entry; if `m` is null, A or B; or if A or B share a
     cell with `n` or `m` (`0x120f28`);
   - `boxB` = (c, `Cell(m)`);
   - if `AABB(boxA, boxB)` (`0x1208e0`: normalised min/max, inclusive), run the fine test
     `0x1209b0(A, B, n, m, hA, hA)` with `hA = (int)ModelHeight(A's pylon model)`.
3. Collision → segment invalid.

`0x1209b0`, the fine test:
- Segment A→B is stepped by `(B − A) / (A.+0x50 / 64)`. Segment m→n is stepped by
  `(m − n) / (n.+0x50 / 64)`. Points are `0x19a368` track points in node units.
- ⚠ The step count for A→B uses **`A.+0x50`, the length of the segment arriving at A**, not A→B
  (`lh s4, 0x50(s1)` with `s1 = a0` at `0x120af0`). The other segment uses its own length
  (`0x120c94`). READ. That it is a bug is INFERRED.
- Collision if either:
  - a sample of m→n lies in B's cell while that tile is **not** kind 10. For the uncommitted ghost
    the tile is not yet kind 10, so another track passing over the ghost's cell blocks it.
  - a sample `a` of A→B and a sample `b` of m→n share a cell and **|a.y − b.y| ≤ 2·hA**. The code
    tests whether `a.y − hA` or `a.y + hA` lies in `[b.y − hA, b.y + hA]`.
- `hA` is a model height truncated to int and compared with node-unit y's. In `0x121000` the same
  `0x17c578` value is compared with spline y in cells. One of the two uses mixes units (INFERRED).
  If the value is in cells, this vertical tolerance is a few node units, i.e. effectively "at the
  same height".

### 4.5 What is **not** checked (READ absence in `0x1216d8`, `0x121000`, `0x1209b0`)

- no maximum pylon height here: only the tool clamp (§2.5) and the stack sum;
- no slope or height-difference-per-distance limit (the `.sam` `fMaxHeightDiffPerGSquareDist` is
  unused);
- no minimum or maximum track length, and no pylon count minimum beyond "at least 1 before
  closing";
- no terrain-versus-track intersection;
- no path or queue test for the track in the air;
- no park-boundary test beyond the grid bounds and flag-bit-0 tiles.

### 4.6 Loops (`0x11c668`, READ in MIPS)

Allowed only in the build state (not sub-mode, not moving, not stats) and only when the coaster's
entry in the loops table `0x2acad0[w][p][o]` is set. After the Test Park remap `0x11f930`, that is
the ten coasters marked "yes" in `coaster-survey.md` §1.

- If `BottomNodeAt(cursor, 9)` is non-null and not the last node → sound 0xaf, return.
- **Extend** (the last node is a loop node, `+0x53 == 2`):
  - `cell = 2·Cell(last) − Cell(last.prev)`, which repeats the previous loop's sideways offset;
  - it must be in grid, hold no map object (`0x14c688(c, 0) == 0`) and be empty land; otherwise
    return silently;
  - `AddPylon(cell, h 0, bank 0, loop 1, kind 2)`; −1000.
- **New loop**:
  - requires ghost `+0x54` (valid and affordable at the cursor); otherwise return silently;
  - `AddPylon(cursor, h 0, bank 0, loop 1, kind 1)`, the **lead-in**; −1000;
  - let `(dx, dz) = cursor − Cell(pylon before it)`; step the cursor one cell sideways:

    | approach | sideways step |
    |---|---|
    | along z (`|dx| < |dz|`) | x −1 if dz ≥ 0, else x +1 |
    | along x | z +1 if dx ≥ 0, else z −1 |

    That is always the rotation `(dx,dz) → (−dz, dx)`;
  - `AddPylon(shifted, h 0, bank 0, loop 1, kind 2)`, the **loop**; −1000. **This cell is not
    validated at all.**
- Then warp the cursor, recompute, and re-select the ghost with height `+0x34`.
- The segment arriving at the kind-2 node is the vertical circle of radius 3 that drifts one cell
  sideways (`coaster-survey.md` §4.3). That drift is why the second node sits one cell to the side
  (INFERRED).
- Loop nodes:
  - have height 0 and bank 0 and are never selectable (§2.4);
  - skip every rule except the 8-neighbour test;
  - are exempt as neighbours;
  - forbid anything stacked on them or placed on their cell;
  - suspend the turn test for the node after them (when that node has a next).

### 4.7 The valid-cell field (`0x11aa70`, `0x11aca8`, `0x11acc8`, READ)

- The scan restarts on every selection (`0x11c288`) and every Undo. It does **not** restart when the
  cursor moves.
- Each frame it scans 4 rows. Rows run from `z0 = cursor.z − 8` at the restart. Columns are
  `cursor.x − 8 .. cursor.x + 8`, using the current cursor. It completes when the row counter passes
  the current `cursor.z + 8`: 5 frames, 20 rows × 17 columns when the cursor stays put.
- A cell `(x,z)` is listed when:
  1. `(x,z)` and `(x+1,z+1)` are in grid;
  2. `Valid(current moved to (x,z), doClearance = 0)`; and
  3. the current node's next is null, the ghost, or current is a loop node — or else
     `Valid(next, 0)`.
- This is the cheap rule set (distance, turn, occupancy, stacking, neighbours, stack height,
  closing angle) **without** the clearance or overlap tests.
- The current node is restored to the cursor afterwards.

---------------------------------------------------------------------------------------------------

## 5. Whole-track validity and its effects

### 5.1 Closed and valid (READ)

| flag | set | cleared | requires |
|---|---|---|---|
| `+0x148` closed | `0x120868`: an `AddPylon` on the entry cell with count ≥ 1 (Cross, or load); load bit 14 or 15 | `AddPylon` (non-closing), `0x122460`, `0x1223d0`, `0x1234d8` (**entering mode 12 on a closed ring reopens it**) | nothing beyond the ghost being valid on the entry cell at the Cross |
| `+0x144` valid | `0x123698` **every update** = `0x1229d0` | (same) | every pylon's `+0x54` (walk `+0xe08`, stride 0x620) **and** exit `+0x54` **and** entry `+0x54`. Does not require closed |

Writers of node `+0x54` (`0x19aec0`; the complete x-ref list):

| writer | what |
|---|---|
| `0x121d68` | new pylon := 1 |
| `0x122060` | both station nodes := 1 |
| `0x11aef0` | the edited or moved pylon and its next (full rules incl. clearance), or the ghost |
| `0x199bf0` (from `0x11a940`) | pylons stacked above a moved pylon: rules without clearance |
| `0x1206d8` | load: saved value |
| `0x11cd38` | restore backup: saved value (unreachable, §2.1) |
| `0x1e1078` | for each coaster node in the 17×17 cells around a map object being **placed** (`0x1e1238`, when not loading) or **removed** (`0x1e12e0`), walking each stack upward: `+0x54 := SegmentClear(C, n.prev, n)` (**clearance only**); also sets the exit node := 1 |
| `0x121c80` | revalidate all pylons with full rules. **Dead**: its only caller `0x149908` has no `jal`/`j`, no data word and no `lui`-built pointer (scanner §8) |

- `0x1e1238` is called by the ride base place `0x1186d8` (vtable `0x35a560` slot `+0x164`) and by
  the shop place `0x130510` (vtable `0x35dc70`).
- `0x1e12e0` is called by the ride base remove `0x116458`, by `0x130498` (shop remove) and by
  `0x1d2948` (vtable `0x3683b8`).
- So building a ride or shop under or near a coaster can turn segments red, and removing it can turn
  them valid again. Paths cannot (INFERRED from the callers).

### 5.2 Red track (READ)

- Every style's mesh-create function checks the node's `+0x54` and, when it is 0, uses `"red.ssh"`
  for **every** texture slot. The seven functions are A `0x19d870`, B `0x19e5a0`, C `0x19f728`,
  D `0x1a01b0`, E `0x1a0a98`, F `0x1a1600` and G `0x1a1d28`.
- A change of `+0x54` (`0x19aec0`) sets `+0xcc = −1`, `+0x58 = 0` and `+0x608 = +0x60c = 0`.
  `+0xcc = −1` makes `0x19aef8` reload the pylon model on the next update and re-register the mesh
  creator (`model5c vt+0x1c`). That this re-creates the mesh in red is INFERRED from the chain.
- The segment drawn red is the one **arriving** at the invalid node.

### 5.3 What an invalid or open track does (READ; per-update order `0x122a48`)

The per-update runs:
1. `0x123500` windows;
2. `0x123598` pylon models;
3. `0x123618` animation;
4. **`0x123698` meshes + `+0x144`**;
5. `0x1237d0` style;
6. `0x123840` visibility;
7. **`0x1238c0`**: if `!closed || !valid`, remove all trains; then step them (not in state 5);
8. `0x1228d0` breakdown;
9. base `0x1169c0`, whose status tick for states 2/10 (`0x122af8`) **spawns trains when closed and
   there are none, without checking valid**.

So a closed but invalid ring spawns trains in step 9 and deletes them in step 7 of the next update.
They never move (INFERRED).

| effect | condition | where |
|---|---|---|
| no trains | `!closed || !valid` | `0x1238c0` |
| "Ride" button (`STR_GIZMO_CPP_GET_ON_RIDE`, row 0x387) shown on the ride under the cursor | `vt+0x13c = 0x122c80` = closed && valid | `0x137040` @`0x1376e8` |
| ride-along camera (camera mode 3 vs 1) | same | `0x152c48` |
| guests may pick the ride | closed only (`0x120530`, `native-selection-eligibility.md`) | — |
| finish: advisor 203 (voice) | `!valid` | `0x11bbd8` |
| finish: advisor 204 (voice), twice when valid but open | `!closed` | `0x11ba00`, `0x11bbd8` |
| entering mode 12 or 13 removes the trains; mode 12 also reopens | always | `0x11c9f8`, `0x11ad60` |

---------------------------------------------------------------------------------------------------

## 6. The station link

### 6.1 The two fixed nodes (`0x122060`, called by place `0x11fd28` = vt `+0x164`, READ)

| | entry `+0x174` (end of ring) | exit `+0x794` (start of ring) |
|---|---|---|
| cell | `vt+0x1b4` = `0x120288` (DBA `+0xc0`, stepped) | `vt+0x194` = `0x11fdd0` (DBA `+0xbc`, stepped) |
| position | `{x<<8, 0xff00, z<<8}` → cell centre; base y **−0x100** (`0x19cae0` special case) | same |
| height `+0x44` | `(short)(int)` float **`0x2acb64`**`[w·0x48 + p·0x18 + o·8]` | float **`0x2acb60`**`[…]` |
| links | next = exit, prev = 0 (last pylon once closed) | prev = entry, next = pylon 0 |
| flags | `+0x52 = +0x53 = 0`, bank 0, `+0x54 = 1` | same |

- `(w, p, o)` is remapped for the Test Park by `0x11f930` (table `0x2ace10`), so Test Park coasters
  use their home values.
- Both nodes' cells are marked kind 10 (`0x19a828`) and registered as kind-9 map objects.
- They are **not editable** (`0x121cf8`) and are not saved. `0x1e1508` → vt `+0x164(obj, 1)`
  re-lays them on load.
- Their `+0x54` stays 1 except through `0x1e1078` (for the entry node, which the tool also validates
  as the next of the last pylon).

Heights (READ, `0x2acb60/64`):

| coaster | exit / entry |
|---|---|
| Temple of Gloom | 375 / 375 |
| Chak Atak | 575 / 575 |
| Gorilla Thrilla | 795 / 795 |
| Hades | 420 / 420 |
| Dare Devil | 265 / 265 |
| Scatty Batty | 175 / 175 |
| Ghosta Coasta | 250 / 250 |
| Bone Shaker | 355 / 355 |
| Big Dripper | 290 / 290 |
| Caterpillar Coaster | 455 / 455 |
| Candy Coaster | 285 / 285 |
| **Moonshot** | **1125 / 335** |
| Escape Velocity | 325 / 325 |
| The Shocker | 1470 / 1470 |

The track point is base + height (`0x19a368`) plus a pylon-marker offset in `0x19a420`. That offset
belongs to area A: `+0x60`, 0 for Gorilla Thrilla and The Shocker, 0x100 for Moonshot.

### 6.2 Deriving the cells (READ; `0x11fdd0` checked in MIPS)

```c
// L = DBA +0xbc (exit) or +0xc0 (entry): {s16 lx, s16 lz}; W = DBA byte +8, D = DBA byte +10
// r = rotation = byte +0x98 (0x1e1de8: lbu); anchor (X,Z) = +0x84/+0x88 (min corner, §6.3)
Rot(lx,lz): r=0 -> (lx, lz); r=1 -> (lz, W-1-lx); r=2 -> (W-1-lx, D-1-lz); r=3 -> (D-1-lz, lx)  // 0x1e2288
dirExit  = (u32(DBA+0xd0) >> 28) & 3    // = byte +0xd3 bits 4-5  (0x120020, vt+0x19c)
dirEntry =  u32(DBA+0xd0) >> 30         // = byte +0xd3 bits 6-7  (0x120080, vt+0x1bc)
d = (dir + r) & 3;   step = { 0:(0,-1), 1:(-1,0), 2:(0,+1), 3:(+1,0) }[d]   // 0x11fed8..0x11ff40
cell = Rot(L) + (X, Z) + step;   cell.y = InGrid ? byte1*4 of that tile : (unchanged)
// vt+0x1ac = 0x1200e0 gives the entry cell unstepped (Rot + anchor, same y rule).
// the track heading along direction d is (0x800 + d*0x400) & 0xfff (INFERRED from 4.2's
// convention)
```

The rotated footprint is W×D for r ∈ {0, 2} and D×W for r ∈ {1, 3} (`vt+0x84 = 0x1e15d0`,
`vt+0x94 = 0x1e1670`). Rotation by one step sends +x to −z.

### 6.3 Placing the station (mode 11, READ)

- **Anchor.** Station anchor = cursor − (rotated W >> 1, rotated D >> 1) (`0x126558`). `vt+0xbc =
  0x1e1ed8` stores x, z and the level `+0x86 = (cursor y != 0) ? 2 : 0`. The level plays no part in
  the coaster nodes.
- **Preview `0x1e3978`** (generic part). For every footprint cell:
  - `(x,z)`, `(x+1,z)` and `(x,z+1)` must be in grid;
  - the tile must be empty land;
  - `|4·byte1(x,z) − 4·byte1(n)| ≤ 64` for the +x, +z and +xz neighbours;
  - the player must be able to afford it (`money − price·10 ≥ 0`, `0x125130`).

  A failing cell draws 175 and clears the placeable bit 25 of `+0x98`. Otherwise it draws 165, or
  170 "front" with the rotation on the edge row facing the rotation.
- **Guest entrance and exit.** `vt+0x2b4`/`+0x2bc` and cells `0x1e23f8`/`0x1e2568` must be empty land.
  They draw 168 (kind 1 gets the "enter" marker) and 169.
- **Coaster-only part** (kind 1, or `vt+0x184`/`vt+0x1a4` non-zero):
  - the track exit cell (`vt+0x194`) and the track entry cell (`vt+0x1b4`) must be **empty land** and
    affordable;
  - they draw 166 with rotation `(r + dirExit + 3) & 3` and `(r + dirEntry + 1) & 3`;
  - ⚠ **if either cell is off the grid it is skipped, not refused** (`beql … 0x1e4848` at
    `0x1e4798`).
- **Cross (`0x11a858`)** needs bit 25. Placing (`0x11fd28`) runs:
  1. base place `0x1186d8`: map object, and the ±8-cell clearance recheck `0x1e1078`;
  2. `+0x144 = 0`;
  3. `0x122060` (station nodes);
  4. ghost window reset;
  5. ghost owner := this coaster, which resets the ghost's links.

### 6.4 Worked example (computed from the READ rules above; `+0xbc/+0xc0/+0xd3` from the EUR DBA)

**Temple of Gloom**: W=4, D=3, `+0xbc = (3,1)`, `+0xc0 = (0,1)`, `+0xd3 = 0x7E` (exit dir 3,
entry dir 1). The anchor is at `(X, Z)`. At **r = 0**:
- exit: Rot(3,1) = (3,1), plus (X,Z), plus step[3] = (+1,0) → **(X+4, Z+1)**;
- entry: Rot(0,1) = (0,1), plus step[1] = (−1,0) → **(X−1, Z+1)**;
- the station heading (entry → exit) is **0x400 (+x)**.

So the first pylon must be 3..8 cells from `(X+4, Z+1)` with a turn < 45° from +x. The last pylon's
incoming heading must be within 90° of +x for the Cross on `(X−1, Z+1)` to close.

All 14 coasters, cells relative to the anchor (rotated footprint in brackets):

| coaster | W×D | r0 exit / entry | r1 exit / entry | r2 exit / entry | r3 exit / entry |
|---|---|---|---|---|---|
| Chak Atak (`+0xd3 0xDD`) | 2×3 | (−1,1) / (2,1) [hdg 0xc00] | (1,2) / (1,−1) [0] | (2,1) / (−1,1) [0x400] | (1,−1) / (1,2) [0x800] |
| Gorilla Thrilla | 4×4 | (4,2) / (−1,2) [0x400] | (2,−1) / (2,4) [0x800] | (−1,1) / (4,1) [0xc00] | (1,4) / (1,−1) [0] |
| Temple of Gloom, Dare Devil, Bone Shaker, Big Dripper, Candy Coaster, Caterpillar, Escape Velocity | 4×3 | (4,1) / (−1,1) [0x400] | (1,−1) / (1,4) [0x800] | (−1,1) / (4,1) [0xc00] | (1,4) / (1,−1) [0] |
| Hades, Ghosta Coasta | 3×3 | (3,1) / (−1,1) | (1,−1) / (1,3) | (−1,1) / (3,1) | (1,3) / (1,−1) |
| Scatty Batty | 4×5 | (4,2) / (−1,2) | (2,−1) / (2,4) | (−1,2) / (4,2) | (2,4) / (2,−1) |
| Moonshot (`0xDC`) | 5×3 | (−1,1) / (5,1) [0xc00] | (1,5) / (1,−1) [0] | (5,1) / (−1,1) [0x400] | (1,−1) / (1,5) [0x800] |
| The Shocker | 5×3 | (5,1) / (−1,1) | (1,−1) / (1,5) | (−1,1) / (5,1) | (1,5) / (1,−1) |

- Twelve coasters leave from local `(W−1, row)` in +x (dir 3) and return at `(0, row)` coming from
  −x (dir 1).
- Chak Atak (`+0xbc = (0,1)`, `+0xc0 = (1,1)`, dirs 1/3) and Moonshot (`(0,1)`, `(4,1)`, dirs 1/3)
  run the other way.
- Scatty Batty's and Gorilla Thrilla's row index flips with rotation because it is D−1−lz at r 2.
- DBA `+0xc4..+0xcf` and `+0xd2` have **no reader** in any function read here (coaster class, tool,
  node class, train code, station preview `0x1e3978`).

---------------------------------------------------------------------------------------------------

## 7. Save and load (READ; `0x120580` in MIPS, `0x1206d8`, drivers `0x1c2180`/`0x160130`)

**Writer.** `0x1c2180` walks the in-use coaster list (head `*(0x395298)+8`, next `+0x130`). For each
coaster it fills a 1056-byte stack buffer with `0x120580` and writes **0x418 bytes**
(`0x1c1e10(buf, 0x418, 1)`). The buffer is not zeroed, so bytes past the last pylon hold stack
garbage (INFERRED).

**Reader.** `0x160130`:
1. reads the count `hdr+7`;
2. for each coaster, reads 0x418 bytes (`0x15fd48`);
3. `obj = 0x14a4a0(rec[+0x94])` (pool allocation with the ordinal);
4. `0x1206d8(obj, rec)`.

Record layout:

| off | size | content | source |
|---|---|---|---|
| `+0x00` | u32 | object `+0x18` | `0x1e1460` (map-object base) |
| `+0x04` | u8 | kind (`vt+0xa4`, 1) | ″ |
| `+0x05`, `+0x06` | u8, u8 | station anchor x, z (cells) | ″ |
| `+0x07` | u8 | rotation | ″ |
| `+0x08 + 4i` | u16, u16 | ride list entries (count at `+0x91`), bytes 0 and 1 of each `0x1a3368(+0xa0, i)` | `0x116aa0` (ride base) |
| `+0x88` | s16 | ride `+0x124` | ″ |
| `+0x8a` | s16 | speed setting `+0xe8` | ″ |
| `+0x8c` | u8 | capacity setting `+0xec` | ″ |
| `+0x8d` | u8 | duration `+0xf0` | ″ |
| `+0x8e` | u8 | `vt+0x2cc()` (loaded through `vt+0x2c4`) | ″ |
| `+0x8f` | u8 | reliability `+0xe4 >> 12` | ″ |
| `+0x90` | u8 | status `+0x9a` | ″ |
| `+0x91` | u8 | list count | ″ |
| `+0x92` | u8 | tier `+0x126` | ″ |
| `+0x94` | u32 | **byte 0 = ordinal** (`0x1e1e98`); **bits 8–13 = pylon count** (`& 0x3f`); **bit 14 = closed**; bit 15 cleared on save | `0x120580` |
| `+0x96 + 14i` | s16 ×3 | pylon i: cell x, **raw base y** (`+0x3e`, not shifted), cell z (node `vt+0x74 = 0x199e18`) | ″ |
| `+0x9c + 14i` | s16 | `+0x54` valid (overwrites the pad of the 8-byte store) | ″ |
| `+0x9e + 14i` | s16 | height `+0x44` | ″ |
| `+0xa0 + 14i` | s16 | bank `+0x4e` | ″ |
| `+0xa2 + 14i` | u8 | `+0x52` loop flag | ″ |
| `+0xa3 + 14i` | u8 | `+0x53` kind | ″ |

32 pylons end at `+0x256`. The rest of the 0x418 bytes is unused.

**Load** (`0x1206d8`):
1. Base ride load `0x116ba8` → `0x1e1508`. That restores the kind, the anchor (level forced 0), the
   rotation and `+0x18`, then calls **vt `+0x164(obj, 1)` = place**, which lays the station nodes.
   With argument 1 the clearance recheck `0x1e1078` is skipped.
2. For each saved pylon: `AddPylon(C, {x, y, z}, height, bank, loop, kind)`, then
   `+0x54 := (saved != 0)`. The saved y is overwritten from the terrain or stack by `0x19cae0`.
3. `vt+0x3c` (one per-update pass).
4. If bit 14 or 15 is set: `CloseRing`.

- A saved pylon that sat on the entry cell would close the ring early. That can't be saved from the
  tool, because closing adds no node (INFERRED).
- More than 32 pylons (the 6-bit field allows 63) would print "TRACK PYLON OVERFLOW" and be
  dropped.

---------------------------------------------------------------------------------------------------

## 8. `coaster.sam` schema: definitive search for a run-time reader

### 8.1 The target

- The schema is **384 records of 0x3c bytes from `0x2acee0` to `0x2b28e0`**. It starts one record
  earlier than `coaster-survey.md` said: `0x2acee0` is a type-2 record with an empty name.
- Record: `{u32 type, char name[0x30], u32 count @+0x34, u32 @+0x38}`. Type counts: 7×190, 6×85,
  2×46, 3×46, 5×6, 10×3, 8×3, 0×2, 1×2, and 12×1 (the end, at `0x2b28a4`).
- It sits between coaster-class data (`0x2acad0..0x2aced8`) and the per-mode button tables
  (`0x2b28e0`).

### 8.2 How a schema is reached when one is used (the positive control)

- The only SAM loader is `0x1f7cb0`. It is called by `0x1f7ed8`, the resource loader, when flag
  0x200000 is set. It appends `".sam"` (`0x36ab20`) to a caller-supplied name.
- It parses through a schema-provider object whose vtable is `0x36ad90`. Slot `+0x0c` is
  `0x1f98c8: return i*0x3c + 0x2e7548`, the ride `.sam` schema (Info, Bumper, …). This is a `lui 0x2e`
  / `addiu 0x7548` pair.
- **Ghidra reports 0 references to `0x2e7548`.** That is exactly why a Ghidra x-ref null is not a null
  here.

### 8.3 The searches ((local scratch), `schemascan2.py`, `schemascan3.py`; output in (local scratch))

| # | search | coaster schema `[0x2acee0, 0x2b28e0)` | positive controls |
|---|---|---|---|
| A | aligned 32-bit words anywhere in the loaded image equal to an address in range | 2 hits: `0x2f4aa4 = 0x2aed1e` (among random-looking words) and `0x373420 = 0x2b015b` (among `0x444444`, `0x222222`: colour data). Neither is aligned to a record; both are data, not pointers | — |
| B | unaligned words equal to a **record start** | 38. Of these, 34 are in `.text` straddling two instructions, and 4 are at odd `.rodata` addresses (`0x37c273…`). None is loadable as a pointer | — |
| C | `lui`-built addresses (`addiu`/`daddiu`/`ori`/loads/stores), **propagated through `addu`/`daddu`/`or`**, stopping at unconditional jumps and ending caller-saved registers at calls | 4 raw hits, all disproved in MIPS: `0x131c3c` is a `bgezl` delay slot, whose taken path loads `0x2b3b30`; `0x13a3b0` is a `b` delay slot, and the code it seemed to reach runs with `$v0 = 0x360000` from the `bne` delay slot at `0x13a38c`, so the colour read is at `0x35f550`; `0x150f24` is a `beql` delay slot, whose path uses `0x2aae08` | found: `0x1f98c8 → 0x2e7548` (the ride schema); `0x122194 → 0x2acb60` (station heights, indexed); `0x122ea0 → 0x2acad0` (loops, indexed); `0x19af70` and 10 more `→ 0x2e2b30` (style table, indexed) |
| D | `$gp`-relative access | impossible: `$gp = 0x385e70` (`.reginfo`), so ±32 KB cannot reach `0x2b…` | — |
| E | any `addiu rX, $zero, 0x3c` followed by `mult` and a `lui`-built constant (the accessor shape) | only `0x1f98c8` (+ `0x2e7548`) in the whole `.text` | — |
| F | strings | no `coaster.sam` and no standalone `coaster`/`Coaster` string in the ELF. The only case-insensitive "coaster" strings are `coaster1`/`coaster3` (model names), `STR_*` keys, debug strings and schema field names | — |

### 8.4 Result

**Nothing reads the `coaster.sam` schema at run time.** The four hits and the two data words were
each checked individually. The positive controls show the scan finds direct, indexed and accessor
references.

The schema-provider class of the one SAM parser serves only the ride schema. No function in the
coaster class (`0x11f990..0x124100`), the tool (`0x11a600..0x11d300`), the node class
(`0x199000..0x19e100`) or the train code (`0x1ae800..0x1b2100`) calls `0x1f7ed8` or `0x1f7cb0`
directly. `0x1f7ed8`'s callers are `0x1f8678` and `0x1f3248`, and `0x1f7cb0`'s only caller is
`0x1f7ed8`.

If the generic resource loader ever reaches `0x1f7cb0` while loading a coaster's models, it parses
`<name>.sam` with the **ride** schema `0x2e7548`, never the coaster schema. Whether it does was not
traced; it does not affect this conclusion.

The `.sam` `Bumper.*Adjust` values of the coaster ride `.sam`s (N(2,1) E(1,0) S(0,1) W(1,2)) are
not the DBA `+0xbc/+0xc0` values. The coaster code derives the station link only from the DBA
(§6.2).

- READ: the searches and their results.
- INFERRED: that no other mechanism exists, e.g. a pointer computed arithmetically from an unrelated
  base. No such pattern appeared in C's propagation.

---------------------------------------------------------------------------------------------------

## 9. Still unknown (what I tried)

- **What the valid-cell field and the loop-cursor tile look like on screen.** They draw marker ids 171
  and 174, which the tile loader does not load (`ghost-cursor.md`). Not run on hardware or in an
  emulator; no savestate is on this box.
- **Snapped cursor step size per press.** One repeat pulse is 128 units (half a cell), READ. How
  pulses map to presses is in `0x1812f8` (the pad repeat), not traced.
- **Units of the clearance and overlap heights.** Model height `0x17c578` (`*(model+0xc)+0x64`) is
  compared with spline y in cells in `0x121000`, and truncated to int against node-unit y in
  `0x1209b0`. One of these mixes units. Resolving it needs a model header value next to a track
  height (area A's y-unit question).
- **Meaning of tile flag bit 0** beyond "set with bits 1 and 5 on authored skipped cells"
  (`paths.md`). The coaster rules only use it as a 2.0 clearance floor.
- **Whether `0x19aef8`'s re-registration really re-creates the mesh in red.** The chain is read
  piecewise; the mesh-creator call inside the model object (`model5c vt+0x1c`) was not followed.
- **`0x1e81e0(tile, 10, 1)` verdicts for pylon cells.** This is the path tool's verdict function
  reused for the cursor colour. I read only the arms that matter here: flag 2 → 1, cash → 1,
  kind-10 tile → 6. It has path-tool globals (`0x2b837c` and others) that may leak state from the
  path tool.
- **The station-placement generic rules** (`0x1e3978`) are shared with every ride. I recorded only
  what bears on coasters. The entrance/exit door cells come from `0x1e23f8`/`0x1e2568`, not traced.
- **The test run and stats screen** (`0x11bbd8 → 0x122d48`, `0x11bd28`) are area D.
