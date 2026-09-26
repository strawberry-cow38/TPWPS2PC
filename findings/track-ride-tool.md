# Track rides: the player's track tool, the validity rule, placement and cost

Researched 2026-09-26. Per-piece prices are read from `DATA.WAD:/arsdb.dba`.
Source: `SLES_500.32` (PAL) decompiled in Ghidra 12.1.2 with the ghidra-emotionengine-reloaded
extension. Each load-bearing function was checked against raw R5900 MIPS (`tools/r5900dis.py`), and
tables were read straight from the ELF. For the overview, and for where the four track-ride files
were reconciled, see `track-rides.md`.
**READ** means seen in the decompile or the MIPS. **INFERRED** means reasoned from what was read.

## 0. Conventions used below

- **Vtables are 8-byte entries** `{s16 this_adjust, u16 0, u32 fn}`. A call `vt+0xNN` uses the fn
  word at `+0xNN` and the adjust at `+0xNN-4`. So "slot 49 / +0xc4" of the task is entry 24.
  The track-ride object ("CTrkRide", name from debug strings `0x36bb08` "  CTrkRide::Load" and
  `0x36baf0` "  CTrkRide::Save") keeps its vtable at `ride+0x18`; virtual calls pass `ride+8`
  (the base-ride subobject), and entries with adjust −8 get `ride` back. **Offsets below are from
  the object base `ride`** unless marked `obj8+` (relative to `ride+8`).
- **Cells**: park grid cell coordinates, signed 16-bit. **World units**: 256 per cell horizontally.
  **Heights**: grid cell byte 1 × 4 (`0x14e250`, `0x152760`) in the same world units.
- Park grid: `cell(x,z) = *0x3952ec + (z*W + x)*8` (`0x14e138`), `W = *0x3952f0`, `H = *0x3952f4`.
  Cell byte 0 = type, byte 1 = height/4, byte 6 = overlay bits, byte 7 = flags.
- **Money**: the park money word is `fin+4`, `fin = 0x1005d8()`. Every debit/credit in the track
  tool is `amount_in_DBA_units × 10` (READ). The on-screen "Cost: $N" shows the DBA units (READ,
  `0x1b3210`), so park money is stored in tenths of the displayed dollar (INFERRED).
- Pad bits (game mask built by `0x230278` from libpad): `0x40` Cross, `0x80` Square, `0x100`
  Circle, `0x200` Triangle, `1/2/4/8` Up/Down/Left/Right (READ the remap; face-button names assume
  the standard libpad `PAD_*` bit order, which the D-pad mapping confirms — INFERRED).

## 1. The tool framework (who calls what)

### 1.1 Tool manager (`0x14e180()` → `0x3951b0`)

| field | meaning | read/written by |
|---|---|---|
| `+0x0c` | current tool object | `0x125460` writes; `0x125ae8`, `0x125158`, `0x1b3210` call it |
| `+0x10` | previous tool object | `0x125460` |
| `+0x20` / `+0x24` | current / previous **mode** | `0x125460` |
| `+0x2c,+0x30` | smoothed cursor x/z, 1/256 cell | `0x125c98` (moves half-way to `+0x34/+0x38` each frame, clamps to `[0,(W-2)*256]`,`[0,(H-2)*256]`) |
| `+0x34,+0x38` | target cursor x/z, 1/256 cell | `0x125680` (pad), `0x125408`/`0x125440` (warp) |
| `+0x3c,+0x3e,+0x40` | cursor cell x, height (byte1×4), cell z | `0x125c98` |

**Set mode** `0x125460(mgr, mode, arg, arg2)`: `tool = *(0x2b2a10 + mode*4)`; for modes 3 and 8
it passes the previous tool's ride (`prev.vt+0x74`) to the new tool (`new.vt+0x64`); for modes 4,
9, 10, 13 it passes the selected object `*(0x1497b0()+0x88)`; then calls `tool.vt+0xc(tool,
cursorPos, arg, arg2)` ("enter"). If enter returns 0 → error sound `0xaf` and mode 0.

Mode table `0x2b2a10` (READ) — the four track-ride modes:

| mode | tool object | vtable | role |
|---|---|---|---|
| 3 | `0x388f90` | `0x35baa0` | queue path for the ride just built (enter `0x127d38`) |
| 7 | `0x389570` | `0x35c098` | place the track-ride **station** |
| 8 | `0x389590` | `0x35c008` | **draw a new track** |
| 9 | `0x3895d0` | `0x35bf78` | **edit the track** (also "Build Track") |
| 10 | `0x389610` | `0x35bef0` | place a track **add-on** (upgrade) |

**Per frame** (`0x125158` loop): `0x125ae8` dispatches buttons (edge-triggered mask
`0x181700(0)`): action 6 → `tool.vt+0x2c`, 7 → `+0x34`, 8 → `+0x3c`, 9 → `+0x44`; action masks
from `0x181250`: table `0x363f40` = {6: `0x40` Cross, 7: `0x200` Triangle, 8: `0x100` Circle,
9: `0x80` Square}; alternative table `0x363fa8` when `*0x395430 == 8` = {6: Circle, 7: Cross,
8: Triangle, 9: Square} (what setting `8` is: not established — INFERRED to be a Japanese-style
button layout). Then `0x125c98` updates the cursor, then `tool.vt+0x14(tool, *(mgr+0x3c))`
("cursor moved": recompute cost). **Render** `0x1b3210`: `tool.vt+0x1c(tool)` (preview draw),
then if `tool+8 != 0` prints `"%s $%ld"` (`0x365928`) with string `0x23` **"Cost:"** at the cursor.

Cursor motion in modes 8/9/10 is the **free** mode (`0x1252a8` returns 0 for them): held D-pad
bits move the target by the camera-relative vector (`0x14dfc8`) × frame time (`0x125680`); the
analog branch there is dead because `0x230270` returns 0. The cell is `pos >> 8`. There is **no
snapping of the cursor itself**; snapping happens when the leg is derived (§3).

### 1.2 Slot maps (READ from the vtables)

| slot | station tool `0x35c098` | new track `0x35c008` | edit track `0x35bf78` | add-on `0x35bef0` |
|---|---|---|---|---|
| `+0x0c` enter | `0x129070` | `0x1291d8` | `0x129aa8` | `0x129cf0` |
| `+0x14` cursor | `0x126558` | `0x1292d8` | `0x1292d8` | `0x129d78` |
| `+0x1c` draw | `0x1268f0` | `0x1293c8` | `0x1293c8` | `0x129dc8` |
| `+0x24` abort | `0x126898` | `0x126460` | `0x126460` | `0x126460` |
| `+0x2c` **Cross** | `0x129100` place | `0x129840` add waypoint | `0x129840` add waypoint | `0x129e38` place |
| `+0x34` **Triangle** | `0x1271f0` → abort | `0x129918` finish (mode 3) | `0x129c18` cancel+restore | `0x129f88` cancel |
| `+0x3c` **Circle** | `0x126778` rotate | `0x129a00` undo last | `0x129a00` undo last | `0x127218` (empty) |
| `+0x44` **Square** | `0x126808` rotate (alt layout) | `0x129968` clear all | `0x129968` clear all | `0x127220` (empty) |
| `+0x4c` | `0x126338` set mode | same | same | same |
| `+0x64/+0x74` | set/get ride | `0x129a98`/`0x129a88` | same | `0x12a008`/`0x129ff8` |
| `+0x84` | — | `0x129810` → mode 3 | `0x129bd8` → discard backup, mode 0 | — |

### 1.3 Track-tool object layout (modes 8 and 9; 0x40 bytes)

| off | type | meaning | written by |
|---|---|---|---|
| `+0x00,+0x02,+0x04` | s16 | cursor cell x, height, z | `0x1292d8` (`+2` zeroed by `0x129840`) |
| `+0x08` | s32 | pending cost/refund, DBA units | `0x1292d8`, `0x129a00`, `0x129968`, `0x129aa8`, `0x129c18` |
| `+0x0c` | s32 | affordable | `0x126390` |
| `+0x10` | ptr | vtable | static |
| `+0x14` | s32 | **price per piece** = DBA payload `+0xcc` | `0x1291d8`, `0x129aa8` |
| `+0x18` | s32 | previewed leg valid | `0x1293c8`; cleared on enter |
| `+0x1c` | s32 | leg ends on station entry or on the last waypoint | `0x1293c8` |
| `+0x20` | 8 B | station **entry** cell `0x2001a8` | enter |
| `+0x28` | 8 B | leg start = last waypoint (x `+0x28`, z `+0x2c`) | enter, `0x129840`, `0x2017d0` |
| `+0x30` | 8 B | previewed leg end (x `+0x30`, z `+0x34`) | `0x1293c8` @`0x129730` |
| `+0x38` | ptr | the ride (object base) | `0x129a98` |
| `+0x3c` | s32 | pieces refunded on entering edit | `0x129aa8` |

## 2. UI flow (state machine)

| state | entered by | what it does | exits |
|---|---|---|---|
| Build menu | player picks a track ride (catalogue kind 6) in `0x197f48` | refuses (sound `0xaf`) if `money − price×10 < 0` | → mode 7 with the catalogue index; tutorial events `0x1b`,`0x3d` |
| **7 station** | `0x129070`: creates a CTrkRide from the 2-slot pool (`0x14a180`) | preview `0x1268f0` → ride `vt+0x1c4` = `0x1e3978` (footprint + entry/exit checks, §5.3); Circle rotates `+0x14=(r+1)&3` → ride `vt+0xb4` | Cross `0x129100`: needs placeable bit (`0x1e1e08`, bit 25 of `obj8+0x98`); else sound `0xaf`. OK → ride `vt+0x164(0)` = `0x1fff80` (**first rebuild**: station, connector, exit straight), sound `0x8e`, **mode 8**, debit station price×10 (`0x126408`). If this was the last of the 2 pool slots (`0x14cc68()==0`) advisor message **199** `STR_ADVMES_TRACK_RIDES_STOCK_OUT` ("All of the available track rides have been used…"). Triangle/abort `0x126898`: return ride to pool (`0x14a7b0(ride,0)`), mode 0, sound `0x131` |
| **8 new track** | from 7 | `0x1291d8`: `+0x20`=entry, `+0x28`=exit (`0x200078`), **add the exit cell as waypoint 0** (`0x2016e0`), price from payload `+0xcc`; no rebuild | Cross on a leg that closes the loop, or on a zero-length leg → `vt+0x84` = mode 3. Triangle `0x129918`: sound `0x130`, mode 3 **without closing** |
| **9 edit track** | ride list box "Edit Track"/"Build Track" (`0x124088`, table `0x360f48/0x360f50`) | `0x129aa8`: back up the waypoint list (`0x201980`), **remove the last waypoint and rebuild** (`0x2018c8`), refund its leg | Cross closing/zero-length → `0x129bd8` (free backup, mode 0). Triangle `0x129c18`: restore the backup (§7.4) |
| **10 add-on** | build menu kind 8 (`0x197f48`) or ride panel (`0x1d5dc8`, needs `3 − addonCount > 0`, `0x202f88`) | preview piece `ride+0x2700` of type `idx*4+0x28` follows the cursor (`0x129dc8`) | Cross `0x129e38` (§5.6), Triangle `0x129f88` (delete preview, mode 0, sound `0xd8`) |
| **3 queue** | from 8 | queue path from the station (`0x127d38`) | normal queue tool |

The list box calls ride `vt+0x11c` = `0x203230` = `*(u16*)(ride+0x2854) − 2`; nonzero →
"Edit Track" (`0x360f50`, string 12), zero → "Build Track" (`0x360f48`, string 0). Track length is
pieces×256, so for track rides the result is never 0 and **"Build Track" is unreachable** (READ the
arithmetic; unreachability INFERRED). Both entries call the same `0x124088` → mode 9.

## 3. Waypoints and the leg preview

### 3.1 The waypoint list (in the ride)

- `+0x13c` u8 count; `+0x13e + 4i` s16 x, `+0x140 + 4i` s16 z. **Maximum 34** (`0x2016e0`:
  `sltiu count, 0x22`); the array ends exactly at `+0x1c6`.
- `+0x1c6` u8 **loop-closed flag**. Writers: `0x2016e0` (cleared first, then set to 1 if the new
  waypoint equals the entry cell `0x2001a8`), `0x2017d0` (cleared), `0x201a18` (restored from
  backup `+0x1d0`), `0x1ffda8` (init 0). No reader outside the track module and the tool (full
  binary scan for `lbu/lb/sb …,0x1c6(` outside `0x1fc000–0x205800`/`0x129xxx`: none).
- **Add** `0x2016e0(ride, &pos)`: clears `+0x1c6`; if count ≥ 34 returns 0 (the caller ignores
  this and still rebuilds/charges — INFERRED unreachable, see budget below); appends; sets
  `+0x1c6=1` iff pos == entry; returns 1.
- **Remove last** `0x2017d0(ride, &out)`: clears `+0x1c6`; if count ≥ 2: count−−, `out` = new last
  waypoint, returns `max(|dx|,|dz|) >> 1` of the removed leg (its piece count). If count == 1:
  `out` = waypoint 0, returns 0 (the exit waypoint can never be removed).
- **Backup/restore/discard**: `0x201980` (copy count → `+0x1c7`, heap copy at `+0x1cc`, closed
  flag → `+0x1d0`), `0x201a18` (copy back, then free), `0x201a90` (free).

### 3.2 Preview algorithm `0x1293c8` (per frame; READ, branch-checked in MIPS)

```
stock   = 36 - pieceCount            // 0x202f78, displayed as "Track Stock %d"
valid   = 1; closes = 0              // +0x18, +0x1c
if any of cursor.x, cursor.z, prev.x, prev.z < 0: valid = 0; return
dx = cursor.x - prev.x; dz = cursor.z - prev.z
if |dz| < |dx|:  step = (sign(dx)*2, 0); n = |dx| >> 1     // ties go to the z axis
else:            step = (0, sign(dz)*2); n = |dz| >> 1
p0   = pieceAt(ride, prev)           // 0x202e00(ride, x, z, 0)
bad  = directionRule(type(p0), step) // below
budget = stock; drawArrow = 1
for i = 0 .. n:                      // n+1 cells: prev (i=0) .. end (i=n)
    c = prev + i*step; isEnd = (i == n)
    ok = budget >= 0 && tool.affordable && cellOK(c, isEnd) && !bad
    if ok and isEnd and (c == entry or c == prev):
         draw(c, tex 0xad); closes = 1; drawArrow = (c != entry)
    elif ok: draw(c, tex 0xa5)
    else:    draw(c, tex 0xaf); valid = 0
    budget -= 1
if drawArrow: draw(entry, tex 0xa6, rot = (ride.rot + 0 + 2) & 3)   // vt+0x1bc = 0x1e5a78 returns 0
end = prev + n*step  -> +0x30/+0x34
print "%s %d" (0x35be20) with string 0x151 "Track Stock", stock, at screen (36,196), white
```

- `cellOK(c, isEnd)` = `0x129378`: true if `c` is the leg start (`+0x28`) or the station entry
  (`+0x20`); otherwise `0x14a248(ride, c.x, c.z, stationY, isEnd)` (§5.1). Every `draw` is a 2×2
  ground overlay `0x1504f8(x, z, tex, rot, 2, 2)`.
- **Grid snapping**: legs are axis-aligned, run along the dominant axis only, and are truncated to
  **2-cell steps** (`n = |d| >> 1`). All waypoints therefore sit on a 2-cell lattice anchored at
  the exit cell; the entry cell is 6 cells from the exit along the station axis, so a closing leg
  always lands exactly (INFERRED from the offsets in §6).
- **Piece budget**: cell `i` needs `stock − i ≥ 0`, so a leg of `n` steps needs `pieceCount + n ≤
  36` (**36 piece slots**, not 35: the ctor loop `0x154e40..50` runs `s1 = 35 … −1`, 36 times;
  count byte at `+0x26f8` = `0x1d8 + 36×0x108`). With ≥1 piece per leg this also caps waypoints at
  34 = 3 base pieces + 33 legs (INFERRED; it is why the unchecked `0x2016e0` failure never fires).
- **Direction rule** from the piece already at the leg start (jump table `0x35be30`, `type−4`):

| type(p0) | new leg may go |
|---|---|
| 4–7 (plain straight) | any direction |
| 20,24,36,40,44,48 (dir 0) | only +z (step (0,+2)) |
| 21,25,37,41,45,49 (dir 1) | only −z |
| 22,26,38,42,46,50 (dir 2) | only +x |
| 23,27,39,43,47,51 (dir 3) | only −x |
| anything else (corners 12–19, connectors 8–11, 28–35, station 0–3) | nowhere (whole leg red) |

  A U-turn off a straight passes this table; it is stopped by `cellOK`, because reversing runs
  back over the ride's own track and reaches a non-straight piece (a corner, crossing or the
  station connector), which `0x14a248` refuses (INFERRED; I found no explicit reverse test).

### 3.3 Cost of the previewed leg `0x1292d8` (vt+0x14, every frame)

`affordable = sandbox || park==2 || money − cost×10 ≥ 0` (`0x126390`; sandbox = `*0x2a60b8 != 0`,
park==2 = `0x154428`). Then `cost (+8) = price(+0x14) × (|Δ_dominant| >> 1)`, the dominant axis
chosen exactly as in the preview (`|dz| < |dx|` → x). Displayed as "Cost: $cost".

## 4. Committing and removing waypoints

| action | function | effect (READ) |
|---|---|---|
| Cross | `0x129840` | if `+0x18 == 0` → sound `0xaf`, nothing else. Else `0x2016e0(ride, +0x30)`, **rebuild**, `+2=0`; if `+0x1c == 1` or ride `+0x1c6` → `vt+0x84` (finish); else `+0x28 = +0x30`. Sound `0xce`. **Debit** `cost×10` (`0x126408`, which also plays `0x1f`). |
| Circle | `0x129a00` | `k = 0x2017d0(ride, +0x28)`; **rebuild**; sound `0xdc`; `+8 = k×price`; **credit** `k×price×10` (`0x100750`). |
| Square | `0x129968` | loop `0x2017d0` until it returns 0, crediting `k×price×10` per removed leg; **rebuild** once; sound `0xdc`. Leaves only waypoint 0 — except that a zero-length leg also returns 0 and stops the loop early (INFERRED edge case). |
| Triangle (mode 8) | `0x129918` | sound `0x130`, mode 3 (the track stays as drawn, open or closed). |

Zero-length legs: with the cursor on the last waypoint, `n = 0`, the only cell is the leg start
(always OK), `+0x1c = 1`, so Cross appends a duplicate waypoint (charging 0) and **finishes**. This
is how a player leaves the tool with an open track (INFERRED intent; mechanism READ).

## 5. Placement constraints

### 5.1 Per-2×2-block rule `0x14a248(ride, x, z, h, isEnd)` (READ in MIPS `0x14a248..0x14a41c`)

For each of the 4 cells `(x..x+1, z..z+1)`:
1. `0 ≤ x` and `x < W−2`, `0 ≤ z` and `z < H−2` (else fail).
2. `|4·byte1(cell) − h| ≤ 85` (`slti …, 0x56`), where `h = ride vt+0x6c` y = `256 × stationY`.
3. The cell must be one of: **empty land** (`0x1e64d0`), **type 2**, **type 4 or 7**, or
   **flag 0x10** (occupied by a ride). Otherwise fail.
4. If flag 0x10: fail if `isEnd`; else the track piece covering the cell (`0x14a420` → first
   piece of any track ride, via each ride's bbox + `0x202e00`) must belong to this ride
   (`piece+0xa8 == ride`) **and have shape 0** (plain straight, types 4–7). Anything else fails
   (a normal ride's tile has no piece → `0x14a420` returns 0 and the code reads `*(0+0xa8)`
   unguarded; it cannot equal the ride pointer — INFERRED).
5. If type 2 or type 4/7: fail if `isEnd`.

`0x1e64d0` empty land = type ∉ {2,4,5,7,8,10,12,13}, and flags `0x10`, `0x08`, `0x01`, `0x02` all
clear (READ). Tile-type names are not in the code. Evidence: the footpath tool calls
`0x1279c8(tool, 2)` and the queue tools `0x1279c8(tool, 4)` → **2 = footpath, 4 = queue path**
(INFERRED); 7 is grouped with 4 in `0x1e6348` (queue-related, INFERRED); 13 is accepted wherever 2 is
by the path tool (`0x1e6438`) but **not** by the track rule. The meanings of 5, 8, 10, 12 and flags
1, 2, 8 are not established.

So, in words: a leg may run over empty land, footpaths, queues and the ride's **own straights**,
on ground within ±85/256 of a cell of the station's level; its **end cell** must be empty land
(or the station entry, or the leg start). It may not touch other rides, corners, stations,
crossings, any tile type other than the listed ones, or the last grid row/column.

`stationY` (ride `+0x8e`) is written by `vt+0xbc` = `0x1e1ed8` as `(cursorHeight != 0) ? 2 : 0`
(READ), so `h ∈ {0, 512}`. Pieces do the same (`0x1e2880`: piece y = 0 if the averaged footprint
height is 0, else 2). The parks therefore behave as two build levels, 0 and 2 cells up, with cells
further than 85 units from the level refused (INFERRED reading of READ arithmetic).

### 5.2 Crossing its own track (READ `0x200fb8`)

When a leg step lands on a cell already holding one of pieces `0 … count−2` (not the immediately
previous one), that old piece is re-typed to `its_dir + 20` and a new piece `leg_dir + 20` is laid
on the same cell. Types 20/21 are shape 4 (the X mesh, dirs ±z), 22/23 are shape 99 (hidden,
dirs ±x): one of the pair draws the crossing. The new piece still costs one stock slot.

### 5.3 Station placement (the track-specific part of `0x1e3978`, `kind == 6`)

Besides the normal footprint rules (every footprint cell in-grid `0x149d20`, empty land
`0x1e64d0`, height difference to each +x/+z/+xz neighbour ≤ 64 units, affordable), the station
requires a **3×3 area at the exit and at the entry** to pass `0x14a248(…, isEnd=1)`: the code
calls it for `(exit.x+i, exit.z+j)`, `i,j ∈ {0,1}`, each covering a 2×2 (union 3×3 — INFERRED
union, READ loops). Failure clears the placeable bit (bit 25 of `obj8+0x98`) and paints `0xaf`;
success paints `0xa5` and arrow tiles `0xa6` oriented by `(rot+2)&3`.

### 5.4 Paths and the track (bridges/tunnels) (READ `0x200fb8`, `0x1fd3e8`, `0x1e6cf0`, `0x18e4b8`, `0x1e81e0`)

- A leg step whose 2×2 block contains a type-2 or type-4/7 cell becomes a **path-crossing piece**.
  With DBA payload byte `+0xc0 != 0` (all eight retail track rides have 1), consecutive crossings
  chain: first `dir+24` (shape 3) is laid; when the next step also crosses, the previous shape-3
  piece becomes `dir+28` (shape 9) and the new one `dir+36` (shape 11); a further crossing turns
  the previous shape-11 into `dir+32` (shape 10) and lays another shape 11. Result for a run of
  `n` crossings: n=1 → [3]; n≥2 → [9, 10×(n−2), 11]. Which meshes these are (a bridge/hill vs a
  tunnel) is chosen per ride by the payload resource words (`track-ride-geometry.md` §6: 3 = `h`, 9 = `h_u`, 10 = `h_a`, 11 = `h_d`) — the shape
  numbers are READ, "start/middle/end of a span" is INFERRED.
- Placing a piece writes the tile flags byte: `0x10` (occupied), or `0x50` for types 24–39
  (`0x10|0x40`, "track over path") (`0x1fd3e8`). Connector types 8–11 write nothing (`0x1fcd08`).
  Removal clears the byte to 0 (`0x1fd538`).
- Building a **path over existing track later** is allowed only onto shapes other than 15, 1, 2,
  4, 99 and non-add-ons, and not on the entry/exit 2×2 blocks (`0x1e81e0` returns 1 = blocked for
  those). The path-change hooks `0x1e6cf0` (events `0x32`, `0x84`) and `0x18e4b8` then set the
  piece under the tile to type 8 and **rebuild** its ride, which re-lays the leg with crossing
  pieces (the type-8 write is superseded by the rebuild — INFERRED).

### 5.5 Bounding box and tile ownership

`+0x2808` minX, `+0x2809` maxX, `+0x280a` minZ, `+0x280b` maxZ (s8, cells). Reset by `0x201640` to
`(W−1, 0, H−1, 0)`; grown by `0x201410` for each laid piece to include `pos … pos+size−1` (size =
piece `vt+0x84/+0x94` = `0x1fe9b8/0x1fea08` → 2, or 4 for types > 0x27). Used only as a quick reject
in `0x202e00` ("which of my pieces covers cell (x,z)?"), which `0x14a420` calls for every track ride
in the list `0x14cc90()`. Each placed piece also **flattens the terrain** under it (`0x1e2880`:
averages byte1 over its w×d cells and writes that average to all (w+1)×(d+1) vertices).

### 5.6 Add-ons (the 40–51 types) (READ `0x129cf0`, `0x1fea58`, `0x129e38`, `0x202980`, `0x202c00`)

- Not a path-crossing mechanism: add-ons are **bought separately** (catalogue category 8, the
  "Addons" upgrades) and dropped onto an existing straight. Type = `kind*4 + 0x28 + dir`; the
  shape byte comes from a **per-world/park table** (`0x1fd6c0`: world 0 park 0 `0x2ee240`, w0p1
  `0x2ee280`, w1p0 `0x2ee2e0`, w1p1 `0x2ee2a0`, w2p0 `0x2ee340`, w2p1 `0x2ee300`, w3p0 `0x2ee360`,
  w3p1 → none), not from `0x2ee1e0`. Their shape bytes (11/12/6, 12/11/9, 9/9/8, 9/8/8, 8/8, 8) are
  the kind-8 DBA selector `+0x2c` values (LavaJump 12, MammTunn 11, WaterTun 6, Chopper 12,
  Firepit 11, Ogre 9, BeeJump 9, HoneyPot 8, Meteor 8, ZOB2 8) (the match is INFERRED).
- Preview validity `0x1fea58(piece, ride)`: pieces at `(x+1,z+1)` and `(x+2,z+2)` must both belong
  to the ride, differ, be straights (4–7) of the **same type**; each of the 4×4 footprint cells must
  be in-grid, pass `0x1e66c8`, have neighbour height steps ≤ 64, and hold no other piece.
- Place `0x129e38`: for each track ride, `0x202980(ride, kind, pos)`: max **3** add-ons (`+0x2890`
  < 3, else debug "Tried to add too many upgrades to track ride" `0x36bba8`), fits in grid with a
  4-cell margin, finds the chain index of the covered straight (walking `piece+0xa0`), stores
  `{index − (open?1:0), kind | 0x10}` at `+0x288a + 2i`, rebuilds. Then debit price×10, sound
  `(bank 2, 0xb8)`, mode 0. Add-on price = kind-8 payload `+0x20` via `0x12bc10(mgr, 8, idx)`.
- After every rebuild, `0x202c00` drops add-ons with `index ≥ count − (closed?2:1)`, and requires
  each add-on piece to be followed by a straight, which it converts to the hidden connector
  (`type+4` → 8–11); otherwise it deletes that add-on and rebuilds again. **No refund** on drop.

## 6. How the first leg attaches and how the loop closes

Station cell `(X, Z)` = ride `+0x8c`/`+0x90`, rotation `r` = ride `+0xa0`.

| r | exit `0x200078` (waypoint 0) | entry `0x2001a8` (closing cell) | station piece | connector | exit straight |
|---|---|---|---|---|---|
| 0 | (X+4, Z+1) | (X−2, Z+1) | type 2 @(X,Z) | type 10 @(X+2,Z) | type 6 @(X+4,Z+1) |
| 1 | (X+1, Z−2) | (X+1, Z+4) | type 1 @(X,Z+2) | type 9 @(X,Z) | type 5 @(X+1,Z−2) |
| 2 | (X−2, Z+1) | (X+4, Z+1) | type 3 @(X+2,Z+2) | type 11 @(X,Z+2) | type 7 @(X−2,Z+1) |
| 3 | (X+1, Z+4) | (X+1, Z−2) | type 0 @(X+2,Z) | type 8 @(X+2,Z+2) | type 4 @(X+1,Z+4) |

(Exit READ in MIPS `0x200078..`; entry READ in decompile; pieces from table `0x2ee54a`, 8 bytes per
rotation of `{offset nibbles dz:dx, type}` × 3, read by `0x2009c0`.) The four exit/entry offsets are
exactly the `.sam` `Bumper.{North,East,South,West}{X,Y}Adjust` pairs (−2,1), (1,4), (4,1), (1,−2):
on PS2 they are **immediates in code**, not read from the file (the match is INFERRED; the
immediates are READ).

- **Attach**: with one waypoint the rebuild lays station + connector + exit straight. With two or
  more it lays only station + connector, and leg 0 starts on the exit cell; its first piece is
  `turn[connectorDir][legDir]` (table `0x2ee518`, index `prevDir*4+newDir`):
  `[4,0,17,14 | 0,5,12,19 | 15,18,6,0 | 16,13,0,7]` (0 = reversal). So leg 0 may go straight on or
  turn either way at the exit. Directions: 0 = +z, 1 = −z, 2 = +x, 3 = −x (`0x200cd0`).
- **Close**: the last leg must end **exactly on the entry cell** (checked by value in `0x2016e0`).
  The entry cell is exempt from the cell rule; the piece laid there is forced to face into the
  station (`0x200fb8`, `param_4`: rot 0→dir 2, 1→1, 2→3, 3→0), becoming a corner if the leg arrives
  from the side.

## 7. Validity

### 7.1 `vt+0xc4` is not a track check

`vt+0xc4` of `0x36bbf0` is the **base** `0x1e2830` (no override): `return (u8)(obj8[0x9a] − 4) < 2`,
i.e. **"status is 4 or 5"** (broken down / 5). The base update `0x1169c0` uses the same slot before
forcing status 4 ("if not already broken, break"). READ in MIPS.

### 7.2 The rule actually used

End of every rebuild (`0x2009c0`, MIPS `0x200b9c..0x200bf4`):

```
length(+0x2854) = pieceCount << 8
if !isBrokenDown(): setStatus(ride.closed(+0x1c6) ? 2 : 3)     // vt+0x1f4 = 0x1e4d70
```

**A track is valid iff its last waypoint equals the station entry cell.** Nothing else is checked
globally: no minimum length, no overlap test, no park-boundary or terrain test at this point — all
of those are enforced cell-by-cell while drawing (§3, §5), so an invalid shape cannot be committed.

### 7.3 Statuses 2 and 3 (READ the handlers; names INFERRED)

Status byte `ride+0xa2` (`obj8+0x9a`); per-status tick dispatch `0x1e5138` → `vt+0x25c + 8·(s−1)`.

| status | set by rebuild when | enter handler | tick handler | meaning |
|---|---|---|---|---|
| 2 | loop closed | `0x1164a8`: `obj8+0x8c=0`, `obj8+0x90=1` (via `0x1e4cb8`), **cycle timer `ride+0x12a` (`obj8+0x122`) = 0** | `0x200518`: if not closed → status 3; else `+0x12a++`, at `≥ 2 × vt+0x304` (`ride+0xf8`) → status 11 | running cycle ("valid, go") |
| 3 | loop open | `0x1e4cc8`: `obj8+0x90 = 1` | `0x1e50f0`: empty | track incomplete: idle |

Every rebuild overwrites **any status other than 4 and 5**, whatever the ride was doing (loading,
unloading, or a player-set state), including rebuilds triggered by path edits (READ condition;
what that does to a player-closed ride is INFERRED, the status names are in `track-ride-operation.md` §3). Each rebuild also ejects
every car and rider first (`0x2022a8` loop), so editing interrupts the ride.

### 7.4 Edit-mode arithmetic (READ in MIPS `0x129aa8`, `0x129c18`)

- Enter (`0x129aa8`): backup; remove the last waypoint + rebuild; credit `k×price×10` for its leg.
  The "`k−1` if closed" branch reads `+0x1c6` **after** `0x2017d0` has cleared it, so it is dead.
- Cancel (`0x129c18`): credit `(pieces − (closed?5:4)) × price × 10`; strip all waypoints; restore
  backup; rebuild; debit `(pieces' − (closed'?3:4)) × price × 10`; mode 0.
- Net for enter-then-cancel with no edits (INFERRED from the READ formulas, paid pieces = pieces−3):
  an open track nets 0; a closed track nets **−1 piece price**.

## 8. Cost

| ride | DBA key | per-piece `+0xcc` | station `+0x50` (tier 0) |
|---|---|---|---|
| Dino Karts | 220 | 35 | 2500 |
| Splish Splash | 235 | 35 | 2750 |
| CryptKarts | 138 | 40 | 4000 |
| Ooze Crooz | 146 | 45 | 5000 |
| Bumble Buggies | 59 | 40 | 4000 |
| Taptastic Rapids | 62 | 40 | 5000 |
| Space Racers | 367 | 35 | 6500 |
| The Blobulator | 381 | 40 | 5000 |

(Read from `DATA.WAD:/arsdb.dba`, extracted to my out dir. `+0xcc` is inside the range dba.md lists
as unresolved `c1..d7`; its consumer is now the two tool-enter loads `0x129298`/`0x129b54`, the only
`lw …,0xcc(v0)` after a `jalr` in the binary.) Add-ons (kind 8, `+0x20`): LavaJump 750, MammTunn 900,
WaterTun 900, Chopper 1000, Firepit 1200, Ogre 1000, BeeJump 750, HoneyPot 900, Meteor 750, ZOB2 750.

- **Priced per piece**: a leg of `k` two-cell steps costs `k × price`; the station straight, the
  connector and the station piece itself are covered by the station price.
- **Debit**: `0x126408` → `0x100698(fin, cost×10)` (skipped in sandbox or park 2). Callers: waypoint
  commit `0x129840`, station `0x129100`, add-on `0x129e38`, cancel re-charge `0x129c18`.
- **Refund**: 100 % via `0x100750(fin, k×price×10)` on undo/clear/edit-enter/cancel. `0x100750`
  has no sandbox check (INFERRED: refunds pay out even when building was free).
- **Demolition**: `vt+0x10c` = `0x1fffe8` → destroy pieces (no refund) → `0x116458` → `0x1e12e0`
  credits **half the station price** only; pieces and add-ons are not refunded.

## 9. Feedback to the player

- **No text message says a track is invalid.** Feedback is ground tiles, sounds and the counter.
- Overlay textures `data/generic/tiles/<n>.ssh` loaded into `0x2f0800[n]` by `0x220dc0`; colours
  from the disc's `DATA.WAD:/Generic/Tiles/<n>.tga` sources: `0xa5` (165) blue grid = cell OK;
  `0xaf` (175) solid dark red = blocked; `0xad` (173) blue with a green disc = leg end closes the
  loop / finishes; `0xa6` (166) blue with a green chevron = arrow at the station entry (and at
  exit/entry during station placement).
- Strings: `0x151` `STR_TRACK_PIECES_LEFT` "Track Stock" (`"%s %d"` `0x35be20`); `0x23` `STR_COST`
  "Cost:" (`"%s $%ld"` `0x365928`); list box `0` "Build Track", `12` "Edit Track". Advisor message
  199 (table `0x2a6ac8`, 56-byte records) `STR_ADVMES_TRACK_RIDES_STOCK_OUT`.
- Sounds (`0x111150(bank 0, id)`, names unresolved): `0xaf` refuse, `0x8e` station placed, `0xce`
  waypoint placed, `0xdc` waypoint removed, `0x130` finish, `0x1f` generic click after a debit,
  `0x131` station cancelled, `0xd8` add-on cancelled, `(2,0xb8)` add-on placed.
- Tutorial notifications via `0x107390(tut, n)` → `0x206358` (jump table `0x36c3a0`): `0x3c`
  (enter new track), `0x1b`/`0x1c` are no-ops (`0x206594`); `0x3d` → tutorial `vt+0xcc`, `0x3e` →
  `vt+0xd4`, `0x43` → `vt+0xe4` (targets not traced).

## 10. All 12 callers of the rebuild `0x2009c0`

| caller | trigger |
|---|---|
| `0x129840` | Cross: add waypoint (modes 8, 9) |
| `0x129a00` | Circle: undo last waypoint |
| `0x129968` | Square: clear to waypoint 0 |
| `0x2018c8` ← `0x129aa8` | entering edit mode |
| `0x129c18` | Triangle in edit mode: restore backup |
| `0x1fff80` (vt+0x164) | station placed (`0x129100`) |
| `0x2008d0` ← `0x15fff0` | savegame load (`Load_ReadTrackRides`, 0x130-byte records written by `0x2007e8` ← `0x1c20c0`: `+0x94` record index, `+0x95` waypoint count, `+0x96+4i` x/z, `+0x12c` add-on count, `+0x126+2i` add-ons) |
| `0x202b18` ← `0x202980` ← `0x129e38` | add-on placed |
| `0x202c00` | post-rebuild add-on validation, when an add-on is dropped |
| `0x1e6cf0` (events `0x32`, `0x84`) | path/tile change over a piece |
| `0x18e4b8` | path tile change over a piece |

## 11. Track-ride vtable `0x36bbf0` (110 entries, ends at `+0x36c`)

Overrides marked ★ (all others are base-ride functions). Identified meanings in brackets.

```
+004 0 (adj-8)       +00c 0x203130★ [dtor]   +014 0x109528         +01c 0x1e5728
+024 0x203220★ [ret 0] +02c 0x109540       +034 0x1093b0         +03c 0x200410★ [update]
+044 0x200728★ [frame] +04c 0x118a68       +054 0x1e1440 [release payload] +05c 0x1e1420 [get DBA payload]
+064 0x1e1e00 [&handle] +06c 0x1e1f00 [pos<<8] +074 0x1e1fa8 [pos cells] +07c 0x1e1d78
+084 0x1e15d0 [width] +08c 0x1e1668        +094 0x1e1670 [depth] +09c 0x1096d8
+0a4 0x1e1d68 [kind=6] +0ac 0x1e5ab8       +0b4 0x1e1de0 [set rot] +0bc 0x1e1ed8 [set pos, y∈{0,2}]
+0c4 0x1e2830 [isBrokenDown] +0cc 0x1e2030 +0d4 0x1e1ce8         +0dc 0x109798
+0e4 0x1097a0        +0ec 0x1097a8         +0f4 0x117280         +0fc 0x1e2738
+104 0x1e2768        +10c 0x1fffe8★ [demolish] +114 0x1e2798      +11c 0x203230★ [len-2, list box]
+124 0x109868        +12c 0x118a30         +134 0x109878         +13c 0x109880
+144 0x1e5730        +14c 0x1e5778         +154 0x1ffda8★ [init]  +15c 0x1e0f30
+164 0x1fff80★ [place→rebuild] +16c 0x1e1460 +174 0x1e1508       +17c 0x116ec0
+184 0x1e5840 +18c 0x1e5848 +194 0x1e58d0 +19c 0x1e5958 +1a4 0x1e5960 +1ac 0x1e5968 +1b4 0x1e59f0
+1bc 0x1e5a78 [ret 0] +1c4 0x1e3978 [placement preview] +1cc 0x118990 +1d4 0x202188★ +1dc 0x116030
+1e4 0x118a78 +1ec 0x118a80  +1f4 0x1e4d70 [setStatus]
status-enter 0..11: +1fc 0x1e4c98 +204 0x1e4ca8 +20c 0x1164a8 +214 0x1e4cc8 +21c 0x2002d8★
                    +224 0x116550 +22c 0x200310★ +234 0x116660 +23c 0x1e4d38 +244 0x1e4d48
                    +24c 0x1e4d58 +254 0x1e4d68
status-tick 1..11:  +25c 0x1e4f58 +264 0x200518★ +26c 0x1e50f0 +274 0x2006f8★ +27c 0x200698★
                    +284 0x2006c8★ +28c 0x1e5110 +294 0x1181d8 +29c 0x1181e0 +2a4 0x200448★ [load]
                    +2ac 0x2005c8★ [unload]
+2b4 0x1e1708 +2bc 0x1e19f0 +2c4 0x1e1cf0 +2cc 0x1e1d58 +2d4 0x1e4b10 +2dc 0x1e1e48 +2e4 0x118a70
+2ec 0x1183f0 +2f4 0x118398 +2fc 0x1183c0 +304 0x1183e8 [ride+0xf8] +30c 0x118378 +314 0x1183a0
+31c 0x1183c8 +324 0x117888 [MinSpeed] +32c 0x1178e8 [MaxSpeed] +334 0x117948 [MinDuration]
+33c 0x1179a8 [MaxDuration] +344 0x117b28 [capacity] +34c 0x117a08 +354 0x117a68 +35c 0x117ac8
+364 0x117b88 +36c 0x201f78★
```

## 12. Other facts a reimplementation needs

- Piece record `0x2ee1e0` (types 0–39) bytes: `[0]` 1 = big, `[1]` shape, `[2]`,`[3]` 4×3 or 2×2,
  `[4]`, `[5]` **direction** (read by `0x1fdb00`), `[6]` model quarter-turns (passed to `vt+0xb4`
  in `0x1fd818`), `[7]` `0x80`. Pieces are linked by `piece+0xa0` (next), owner `piece+0xa8`,
  type `piece+0xec`, cell `piece+0x84/+0x88`.
- `+0x1d1` = Σ per-piece weight after each rebuild (`0x200c20`): 4 for types 20–23 and 40–51,
  2 for 24–31 and 36–39, 1 otherwise (shape-10 middles count 1). Its consumer is Excitement, `0x202188`
  (`track-ride-operation.md` §7.1).
- Removing a piece clears tile overlay bits over `(w+1)×(d+1)` cells (`0x1fcf78`), one more row
  and column than its footprint (READ loop bounds; whether that touches neighbours' data is not
  checked).

## 13. Still unknown (and what I tried)

- **Tile type names** 5, 7, 8, 10, 12, 13 and flags 1, 2, 8: only predicate code exists
  (`0x1e6338..0x1e65b8`); the writers I found (`0x1e6138` callers with constants 2, 5, 7, 8, 10)
  were not traced to their tools.
- **Which add-on is which catalogue index per park**: the category-8 list lives in the runtime
  catalogue (`0x360850[world]` → bss `0x395488…`); no static copy found. The piece tables are READ.
- **Meaning of `*0x395430 == 8`** (alternative button layout / rotate button swap): not traced.
- **Sound names** for ids `0xaf, 0xce, 0xdc, 0x130, 0x8e`: runtime tables `0x2abe38…` only.
- **Tutorial handlers** behind `vt+0xcc/+0xd4/+0xe4` of the tutorial object: not traced.
- **Status 0/1/4–11 semantics**: see `track-ride-operation.md` §3.
- Whether anything besides `0x1e1ed8` rewrites the station y after placement: checked `0x1186d8`'s
  direct callees (`0x118240`, `0x1182a8`, `0x118310`, `0x1e1238`): none writes `obj8+0x86`.
