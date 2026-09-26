# Track rides: geometry (how the drawn line becomes pieces, and how a distance becomes a pose)

Researched 2026-09-26.
Source: `SLES_500.32` (PAL) decompiled in Ghidra 12.1.2 with the ghidra-emotionengine-reloaded
extension. Each load-bearing function was checked against raw R5900 MIPS (`tools/r5900dis.py`), and
tables were read straight from the ELF. For the overview, and for where the four track-ride files
were reconciled, see `track-rides.md`.
A Python re-implementation of the rules (kept locally, not in the repo) served as a consistency check.

Every claim is tagged **READ** (seen in the decompile and checked against the MIPS where it
matters), or **INFERRED** (reasoned to). **SIM** means the consequence was reproduced by running
the Python re-implementation of the READ rules.

---------------------------------------------------------------------------------------------------

## 0. Short version

- A track is **a list of waypoints** (`ride+0x13e`, up to 34). `0x2009c0` throws every piece away and
  re-lays the whole track from scratch on every edit: 2 or 3 fixed **station pieces**, then one
  **leg** per consecutive waypoint pair.
- A leg (`0x200cd0`) is axis-aligned and is filled with **2×2-cell pieces at 2-cell steps**, starting
  ON the leg's first waypoint. The first piece of every leg is the **turn piece**. It comes from a
  16-entry table indexed by *(previous piece's exit direction, new leg direction)* (`0x2ee518`).
- Per piece, `0x200fb8` picks, in priority order: a **track upgrade** (the "add-on" types 40–47),
  then a **crossing** (the new piece lands on the exact anchor of an existing piece, so both become
  `x`), then a **bridge** (any of the 2×2 cells is a path, queue or kind-7 tile gives `h`, `h_u`,
  `h_a` or `h_d`), then the ordinary straight or corner.
- **The track does not follow the ground and hills never come from slope.** Car height is a
  per-ride constant (the station's level + 80, or +144, or +170 depending on the park). The only
  elevation changes are bridges (absolute height 256) and the upgrades' built-in offsets. Placing a
  piece *flattens* the terrain under it.
- **Pose along the track is a lookup table, not a curve evaluation.** When a piece is built,
  `0x1fdba8` bakes 4 samples per piece (one every 64 distance units). Each sample holds 2 lane
  endpoints (x, z), a height and a yaw. The bends' arcs are evaluated with sin/cos **once, at bake
  time**. A car's pose is then bilinear: across the lane, and between two samples 64 units apart.
- Types 40–47 are **track upgrades** (DBA kind 8, e.g. `mammtunn`, `lavajump`, `watertun`): at most 3
  per ride, 4×4 cells, laid on two consecutive straights. They are **not** station variants.
- The `Bumper.{N,E,S,W}{X,Y}Adjust` values in the `.sam` are **the return-point offsets hard-coded as
  immediates in `0x2001a8`**, exactly: N = rotation 0 … W = rotation 3.
- Of the ten piece meshes, **`q` and `v` are never drawn**: no shape number maps to them.

---------------------------------------------------------------------------------------------------

## 1. Conventions (all READ unless noted)

| thing | value |
|---|---|
| cell | 256 world units in x and z (`0x1e1f00` returns `cell<<8`; `0x152760` does `idx%W<<8`) |
| distance along track | `car+0x50`, u16; **256 per piece**; length `ride+0x2854 = pieces<<8` (`0x2009c0`) |
| samples | 4 per piece, at distance `(d & 0xff) >> 6` = 0..3, i.e. **every 64 units** |
| direction code (leg / table byte 5) | **0 = +z, 1 = −z, 2 = +x, 3 = −x** (`0x200cd0`: dx>0→2, dx<0→3, dz>0→0, else 1) |
| model rotation `r` (piece `+0x98`, table byte 6) | quarter turns. The sample transform (below) maps local +z to world +z / +x / −z / −x for r = 0/1/2/3 |
| yaw (sample field, `car+0x4a`) | 4096 per turn. **0 = heading +z, 1024 = +x, 2048 = −z, 3072 = −x** (stored as `r*1024` for a straight of rotation r). Stored values are **not wrapped**: bends store up to 7168 |
| straight dir → r | 0→0, 1→2, 2→1, 3→3 (types 4–11, 20–27, 32–47) |
| world index `0x3952e4` (getter `0x14e170`) | 0 JUNGLE, 1 HALLOW, 2 FANTASY, 3 SPACE |
| park index `0x3952e8` (getter `0x14e160`) | 0 = the world's "park 1", 1 = "park 2" (joined through the mesh table in §6: W0P0 is karts = Dino Karts) |
| tile | `0x14e138(x,z) = *0x3952ec + (z*W + x)*8`. `+0` kind (2 path, 4 queue, 5 attraction footprint, 7/8 attraction connection A/B, set by `0x1e2ad0`); `+1` height; `+7` flags |

---------------------------------------------------------------------------------------------------

## 2. The rebuild, `0x2009c0` (READ, MIPS `0x2009f8..0x200b90`)

Runs from the tool on every waypoint add or remove, when an upgrade is added or removed, when a
path is laid under the track (§12), and on load (`0x1fff80`).

| step | what |
|---|---|
| 1 | Unload every car (`0x2022a8` until none left). |
| 2 | `0x201640`: for each piece call vtable `+0x10c` (remove, §9). Count `+0x26f8` = 0, length = 0, bounding box `+0x2808..0x280b` = (W−1, 0, H−1, 0). |
| 3 | n = **3** if waypoint count `+0x13c` ≤ 1, else **2**. For k < n: entry `0x2ee54a + rot*8 + k*2` = {offset byte, type}; the anchor is the station origin (`ride+0x8c` x, `+0x90` z) + (low nibble, high nibble), both signed 4-bit. Place with `0x201410`. |
| 4 | If waypoints > 1: for i in 0..count−2, leg(`wp[i]`, `wp[i+1]`, last = (i == count−2)). The leg start carries y = the ride's `+0x6c` y (= station y<<8). |
| 5 | `0x202c00`: upgrade validation (§5). It may remove an upgrade and recurse into `0x2009c0`. |
| 6 | `+0x2888 = +0x2889 = 0`: the sample tables are rebuilt lazily, **one piece per ride update** (`0x2027e0`, called first thing in `0x2023b0`). Length = count<<8. |
| 7 | If ride vtable `+0xc4` (`0x1e2830`: object state `+0xa2` ∈ {4,5}) is false, set state 2 if `+0x1c6` (closed) else 3. **This is a state test, not a geometry validity check.** |
| 8 | `0x200c20`: piece points → `+0x1d1` (§13). |

### Station layout, table `0x2ee54a` (8 bytes per rotation; only the first 3 entries are read)

| station rot `ride+0xa0` | entry 0 (station piece) | entry 1 (connector) | entry 2 (straight, only when ≤1 waypoint) | 4th entry (never read) |
|---|---|---|---|---|
| 0 | (0,0) type 2 | (+2,0) type 10 | (+4,+1) type 6 | (1,4) type 5 |
| 1 | (0,+2) type 1 | (0,0) type 9 | (+1,−2) type 5 | (4,1) type 7 |
| 2 | (+2,+2) type 3 | (0,+2) type 11 | (−2,+1) type 7 | (1,−2) type 4 |
| 3 | (+2,0) type 0 | (+2,+2) type 8 | (+1,+4) type 4 | (0,2) type 0 |

Travel direction out of the station: rot 0 → +x, 1 → −z, 2 → −x, 3 → +z. The station piece and the
connector are each 2×2 in the tile system, but their **samples** live in a 4×4 local frame
(table bytes 2 and 4 = 4) and sit on the centre line of the station's track row. That is why the
connector after a station shifts its lanes by −256 (§10). The car path through the station is
pieces 0–1, distance 0–511 (SIM).

### Exit point and return point

| rot | `0x200078` exit point = waypoint 0 | `0x2001a8` return point = the closing waypoint |
|---|---|---|
| 0 | (x+4, z+1) | (x−2, z+1) |
| 1 | (x+1, z−2) | (x+1, z+4) |
| 2 | (x−2, z+1) | (x+4, z+1) |
| 3 | (x+1, z+4) | (x+1, z−2) |

- The tool seeds waypoint 0 with the exit point (`0x1291d8` → `0x2016e0(ride, exitpoint)`; READ in
  `track-ride-tool.md`).
- `0x2016e0` (add waypoint, max 0x22 = 34) sets **`+0x1c6 = 1` ("closed")** when the added point
  equals the return point.

**Bumper.*Adjust consumer (READ, numbers).** The `.sam` has N(−2,1) E(1,4) S(4,1) W(1,−2). These
are exactly `0x2001a8`'s immediates for rot 0/1/2/3, with X = cell x and Y = cell z. `0x200078`
uses the opposite rotation's pair. The values are **not** in the compiled DBA record (kind-6 record
dumped from the EUR `arsdb.dba`: no 0xfe bytes in `+bc..+d7`). The ELF still carries the
SAM field schema with those names: a table at `0x2e7548` of 0x3c-byte {type, name} entries, with
`Bumper`, `NorthXAdjust`… at `0x2e7e30..0x2e808c`, accessed by `0x1f98c8` (vtable `0x36ad90`,
constructed at `0x1f7cc0`). I did not trace whether any runtime path reads parsed Bumper values.
INFERRED: the code was written from these constants and they are dead data on PS2.

---------------------------------------------------------------------------------------------------

## 3. The leg layer, `0x200cd0(ride, A, B, last)` (READ, full MIPS)

```
if A == B:            n = 0,        step = (0,0),  dir = exitdir(last piece)
elif A.x < B.x:       n = B.x-A.x,  step = (+2,0), dir = 2
elif B.x < A.x:       n = A.x-B.x,  step = (-2,0), dir = 3
elif A.z < B.z:       n = B.z-A.z,  step = (0,+2), dir = 0
else:                 n = A.z-B.z,  step = (0,-2), dir = 1
if !last: n -= 2
pos = A
while n >= 0:
    choose(pos, dir, isLastPiece = (n <= 1) ? last : 0)     # 0x200fb8
    n -= 2; pos += step
```

- The x test comes first, so a diagonal leg is treated as an x leg of length |dx| and dz is ignored.
  Legs are assumed axis-aligned; nothing here checks that.
- **"2-cell steps".** A non-final leg of length L lays pieces at offsets 0, 2, …, ≤ L−2, i.e.
  ⌊L/2⌋ pieces. The next leg starts at B, so **an even L is contiguous and an odd L leaves a 1-cell
  hole** at L−1. The final leg lays pieces at 0, 2, …, ≤ L; for even L its last piece sits **on B**
  (the return point). Nothing in the build rejects odd legs. The tool's preview (`0x1293c8`) steps
  the cursor from the previous waypoint in ±2 along the dominant axis, so tool-made legs are even
  (READ in `track-ride-tool.md`; not otherwise verified).
- **The turn is made at the waypoint.** A waypoint is the anchor (min-x, min-z cell) of the turn
  piece's 2×2 block, and that piece is the first piece of the outgoing leg.
- A zero-length leg (A == B) lays nothing unless it is the last leg; then it lays one piece that
  continues the last exit direction.

---------------------------------------------------------------------------------------------------

## 4. The per-piece chooser, `0x200fb8(ride, pos, dir, isLast)` (READ, full MIPS `0x200fb8..0x20140c`)

Priority order. The first rule that matches places the piece and returns.

| # | test | result |
|---|---|---|
| 1 | For each upgrade entry j (`+0x288a + 2j` = {piece index, kind\|0x10}, count `+0x2890` ≤ 3): `count == index + (closed ? 0 : 1)` | place type **0x28 + (kind&0xf)·4 + dir** (upgrade, §5). It uses the requested `dir`, not the corner table. |
| 2 | Some existing piece k in 0 .. count−2 (all but the last) has anchor `(x,z) == pos` | **crossing**: that piece → type `exitdir(k) + 0x14` (`0x1fd818`), and the new piece is type `dir + 0x14`. Exact anchor equality only. |
| 3 | Otherwise idx = `exitdir(last) << 2`; then `idx \|= stationdir` if `isLast` and pos == return point (rot 0→2, 1→1, 2→3, 3→0), else `idx \|= dir` | (idx kept for rule 5) |
| 4 | Any of the 4 tiles (pos.x..+1, pos.z..+1) has kind **2 (path)**, **4 (queue)** or **7** (`0x1e6338`, `0x1e6348`) | **bridge**, below |
| 5 | none of the above | type = **`0x2ee518[idx]`** |

**Bridge rule** (READ). The ride's compiled record byte `+0xc0` (via vtable `+0x64` → `0x10fa30`) is
1 in all 8 retail track-ride records (READ from `arsdb.dba`). With it set:

- if the last piece is `h` (0x18–0x1b) → it becomes `h_u` (0x1c + its dir), and the new piece is `h_d` (0x24 + last dir);
- if the last piece is `h_d` (0x24–0x27) → it becomes `h_a` (0x20 + its dir), and the new piece is `h_d`;
- otherwise the new piece is `h` (0x18 + last dir).

With `+0xc0 = 0`, every bridge piece would be `h`. Result: a 1-piece obstacle gets `h`; 2 pieces
get `h_u h_d`; n pieces get `h_u h_a… h_d` (SIM). **A bridge piece always continues the previous
exit direction and ignores `dir`**, so a turn requested on a bridged cell is lost. INFERRED: the
tool prevents this, since `0x14a248` refuses a path or queue tile at the waypoint step (`track-ride-tool.md` §5.1).

**Corner table `0x2ee518`** (row = previous exit dir, column = new dir):

| prev \ new | 0 (+z) | 1 (−z) | 2 (+x) | 3 (−x) |
|---|---|---|---|---|
| 0 (+z) | 4 s | **0** | 17 b2 | 14 b |
| 1 (−z) | **0** | 5 s | 12 b | 19 b2 |
| 2 (+x) | 15 b | 18 b2 | 6 s | **0** |
| 3 (−x) | 16 b2 | 13 b | **0** | 7 s |

A U-turn yields type **0, a station piece** (shape 15: no mesh, station samples). That is garbage
geometry, and nothing here prevents it (INFERRED: the tool must).

**Entry and exit directions.** Only the exit direction is stored (table byte 5). Entry is implicit:
the corner table is the only place prev and next are matched. For a bend, entry = the row. For
everything else, entry = exit. A crossing keeps the existing piece's exit direction and the new
piece's requested direction.

**Crossings vs overlaps.** The chooser does not distinguish them. It fires on anchor equality. Only
types 20/21 (the dir-0/1 halves) have the `x` mesh; 22/23 are shape 99 (invisible). A perpendicular
crossing therefore draws exactly one `x` (SIM: existing straight → 20, new → 22). Two
parallel pieces on one anchor would draw two or zero meshes. The existing piece is converted
whatever it was (bend, hill…). INFERRED: the tool limits crossings to plain straights.
`0x14a248` rejects crossing any piece whose shape ≠ 0 and rejects crossing at a waypoint
(READ in `track-ride-tool.md`).

---------------------------------------------------------------------------------------------------

## 5. Placing a piece, and the upgrades

**`0x201410(ride, pos, type)`** (READ, MIPS):

1. For types 0x28–0x33 the anchor is shifted so the 4×4 upgrade straddles the 2-wide track with a
   one-cell margin. `type&3` = 0: x−1; 1: x−1, z−2; 2: z−1; 3: x−2, z−1.
2. `0x202d48`: take slot `count++` (debug print "track stock used = %i", **no bounds check**; the
   36-piece stock is enforced by the tool through `0x202f78` = 36 − count). Call vtable `+0x154`
   (`0x1fcbd0`, init with type).
3. `0x1fcc68`: `+0xa8` = ride, `+0x84` = the 8-byte position, `+0x86` = the ride's raw y,
   `0x1fd818(type)`, set flag bit 41 (committed).
4. vtable `+0x164(0)` (`0x1fcd08`, commit to the map, §9).
5. The previous piece's `+0xa0` = this piece (the chain).
6. Grow the bounding box `+0x2808` minx, `+0x2809` max(x+W−1), `+0x280a` minz, `+0x280b` max(z+D−1).

**Upgrades (types 40–47) = "track upgrades", DBA kind 8** (READ).

- Stored as up to **3** entries at `+0x288a` ({chain index, kind nibble \| 0x10}), count `+0x2890`.
  `0x202980` refuses a fourth with "Tried to add too many upgrades to track ride" (`0x36bba8`).
- **Placement** `0x202980(ride, kind, cell)`, called from the tool `0x129e38`:
  - the cell must satisfy x ≥ 0, z ≥ 0, x+4 < W−1, z+4 < H−1;
  - the piece at (x+1, z+1) must belong to this ride;
  - if it is not linked to the piece at (x+2, z+2) but that piece links back to it, take the second;
  - index = its position in the chain, −1 if not closed.
- The ghost validity `0x1fea58` needs the two cells to be two *different* straights of the *same*
  type (4–7). The ghost rotation comes from the straight: 4→0, 5→2, 6→1, 7→3.
- **Post-pass** `0x202c00` (READ, MIPS):
  1. drop entries whose index ≥ count − (closed ? 2 : 1);
  2. walk the chain; for each upgrade piece the next piece **must be a straight (4–7)**, and it is
     retyped to **type+4 (8–11, invisible connector)**;
  3. if it is not a straight, remove the upgrade entry whose array index = the count of upgrades
     seen so far in chain order, then rebuild.

  INFERRED: step 3 assumes the array is sorted in chain order.
- **Per-park upgrade table** (`0x1fd6c0`: `base + type*8`, for type ≥ 40). W, byte 3 and D are
  4, 3, 4; rot = straight mapping; byte 7 = 0x80. **Byte 1 = the upgrade's DBA `+0x2c` selector**
  (Jungle MammTunn 11, LavaJump 12, WaterTun 6 match dba.md). In `0x1fd818` its only effect is
  nonzero → `+0xef = 1` and flag bit 43.

| park | table (kind0 @) | kind 0 id / mesh (shape 12) | kind 1 id / mesh (shape 13) |
|---|---|---|---|
| JUNGLE p1, Dino Karts | 0x2ee380 | 11 / `mammtunn` (237) | 12 / `lavajump` (236) |
| JUNGLE p2, Splish Splash | 0x2ee3c0 | 6 / `watertun` (238) | (12, no mesh) |
| HALLOW p1, Ooze Crooz | 0x2ee420 | 9 / `ogre` (167) | (9, no mesh) |
| HALLOW p2, CryptKarts | 0x2ee3e0 | 12 / `chopper` (165) | 11 / `firepit` (166) |
| FANTASY p1, Taptastic Rapids | 0x2ee480 | 8 / **no mesh** (table entry 0) | (8, no mesh) |
| FANTASY p2, Bumble Buggies | 0x2ee440 | 9 / `beejump` (81) | 8 / `honeypot` (82) |
| SPACE p1, The Blobulator | 0x2ee4a0 | 8 / `meteor` (402) | **garbage**: 0x2ee4c0 is the global in §10 plus UI data |
| SPACE p2, Space Racers | **NULL** (`0x1fd6c0` returns 0) | would crash | would crash |

The tables overlap each other: W0P1's kind-1 rows are W1P1's kind-0 rows. The DBA holds 10 kind-8
records with selectors 12, 11, 6 / 12, 11, 9 / 9, 8 / 8, 8, which fits this table.
The mesh ids come from §6, and `+0xe8` = `0x12adc8(0x12a550(), kind)`, the park's upgrade asset.

---------------------------------------------------------------------------------------------------

## 6. Shapes → meshes (READ; the table is `0x2ecad0` in .data, **not** `0x204c38`)

`0x1fd818(piece, type)`: shape = **`0x2ee1e0[type].byte1`**. This always reads the *base* table,
even for upgrades, so upgrades get shape 12 or 13.

- shape **99**: hide the model (model vtable `+0x24(0)`), `+0x100 = −1`.
- any other shape: `id = *(u32*)(0x2ecad0 + world*0x4a4 + park*0x18c + shape*4)`. If `id ≠ 0` and
  `id ≠ +0x100`, set the model to it (model vtable `+0xc`, then `0x17ce10`, `+0xac(1)`).
  `id == 0` keeps whatever model is there (station pieces, shape 15).

`0x2ecad0` has only two readers, `0x1fd818` and `0x1fefc8` (xrefs + full lui/addiu scan). Shapes
5–8 are **car** meshes, read by `0x1fefc8(n)` = shape 5+n; the kart init `0x2053a0` uses
`rand()%4`.

| shape | mesh | Dino Karts (W0P0) | Splish Splash (W0P1) | used by types |
|---|---|---|---|---|
| 0 | `trcks` straight | 504 | 518 | 4–7 |
| 1 | `trckb` bend | 497 | 511 | 12–15 |
| 2 | `trckb2` other-hand bend | 498 | 512 | 16–19 |
| 3 | `trckh` hump | 499 | 513 | 24–27 |
| 4 | `trckx` crossing | 505 | 519 | 20–21 |
| 5–8 | kart colours `gk_blue/green/orange/purple` / boat `wr_ring` | 507–510 | 521 ×4 | cars |
| 9 | `trckh_u` ramp up | 502 | 516 | 28–31 |
| 10 | `trckh_a` raised | 500 | 514 | 32–35 |
| 11 | `trckh_d` ramp down | 501 | 515 | 36–39 |
| 12 / 13 | upgrade meshes (§5) | 237 / 236 | 238 / – | 40–43 / 44–47 |
| 15 | none (the station building is the ride's own model) | 0 | 0 | 0–3 |
| 99 | hidden | – | – | 8–11, 22, 23 |

The other parks use the same names at other ids:
- HALLOW: water 536–546, karts 522–535;
- FANTASY: water 561–571, karts 547–560 (all four kart colours → `gk_blue`);
- SPACE: water 586–596, karts 572–585.

**`gk_trckq`/`wr_trckq` (503/517…) and `gk_trckv`/`wr_trckv` (506/520…) are registered in
`0x2c2438` but no shape selects them.** `x2`/`s1` have no registry string at all.

`0x204c38` is the **car** draw: it picks the compiled record word `+0xdc + 4*(car[+0x35]+5)`, not a
piece mesh.

---------------------------------------------------------------------------------------------------

## 7. The piece type table `0x2ee1e0` (48 × 8 bytes, READ)

Bytes:
- b0 = size class (0 = 2×2, 1 = 4×4; **never read**, the only reader of the entry pointer is `0x1fd7f8`);
- b1 = shape;
- b2 = local width W (cells), b4 = local depth D; model-origin offsets, §8;
- b3 = height class (vtable `+0x8c`);
- b5 = **exit direction**;
- b6 = **model rotation r**;
- b7 = 0x80 everywhere (returned by `0x2028f0`).

The **tile footprint** is vtable `+0x84`/`+0x94` = 2×2, or 4×4 for type ≥ 40. So station pieces
occupy only 2×2 tiles although b2 and b4 are 4.

| types | shape / mesh | b2 b3 b4 | exit dirs | r | sample rule (§10) | tile flag | points |
|---|---|---|---|---|---|---|---|
| 0,1,2,3 | 15 station | 4 3 4 | 0,1,2,3 | 3,1,0,2 | station | 0x10 | 1 |
| 4–7 | 0 `s` | 2 2 2 | 0,1,2,3 | 0,2,1,3 | straight | 0x10 | 1 |
| 8–11 | 99 connector | 2 2 2 | 0,1,2,3 | 0,2,1,3 | connector | none (no commit) | 1 |
| 12,13,14,15 | 1 `b` | 2 2 2 | 2,1,3,0 | 0,1,2,3 | bend b | 0x10 | 1 |
| 16,17,18,19 | 2 `b2` | 2 2 2 | 0,2,1,3 | 0,1,2,3 | bend b2 | 0x10 | 1 |
| 20,21 / 22,23 | 4 `x` / 99 | 2 2 2 | 0,1 / 2,3 | 0,2 / 1,3 | straight | 0x10 | **4** |
| 24–27 | 3 `h` | 2 2 2 | 0..3 | 0,2,1,3 | hump | **0x50** | **2** |
| 28–31 | 9 `h_u` | 2 2 2 | 0..3 | **2,0,3,1** | ramp up (reversed) | 0x50 | **2** |
| 32–35 | 10 `h_a` | 2 2 2 | 0..3 | 0,2,1,3 | raised | 0x50 | 1 |
| 36–39 | 11 `h_d` | 2 2 2 | 0..3 | 0,2,1,3 | raised | 0x50 | **2** |
| 40–43 / 44–47 | 12 / 13 upgrade | 4 3 4 | 0..3 | 0,2,1,3 | upgrade | 0x10 + DBA footprint | **4** |

- Entering directions for the bends, from the corner table:
  - `b`: 12 = −z→+x, 13 = −x→−z, 14 = +z→−x, 15 = +x→+z;
  - `b2`: 16 = −x→+z, 17 = +z→+x, 18 = +x→−z, 19 = −z→−x.
- In engine terms, **`b` turns yaw −1024 and `b2` turns yaw +1024** across the piece. Whether that is
  left or right on screen depends on the renderer's handedness, which I did not establish.
- Tile flag = `tile+7` written by `0x1fd3e8` (it overwrites the whole byte). 0x40 marks an elevated
  deck. The corpus's `0x1e61e0` returns 1 for tiles with both 0x10 and 0x40, the same answer it
  gives path and queue tiles without 0x10. INFERRED: paths stay usable under bridges.

---------------------------------------------------------------------------------------------------

## 8. The piece object (0x108 bytes; 36 of them at `ride+0x1d8`, plus a spare at `+0x2700`)

The ctor loop `0x154e40` runs **36** times (`s1` = 35 down to −1), not 35. `0x2700` is the tool's
ghost piece (`0x202dc8`). Base-class ctor `0x1e0e60` (base vtable `0x369aa0`); piece vtable
`0x36b670` is stored at `+0x10`.

| off | type | meaning | written by / read by |
|---|---|---|---|
| +0x08 | ptr | render model instance (its vtable at `inst+0x24`) | base / 0x1fd818, 0x1fd0c0, 0x1fcdb8 |
| +0x10 | ptr | vtable 0x36b670 | ctor 0x1ff198, 0x154da0 |
| +0x20 | handle | base DBA resource handle (base `+0x5c`) | base |
| +0x84, +0x88 | s16 | anchor cell x, z | 0x1fcc68 / everything |
| +0x86 | s16 | **y level: 0 or 2** (2 if the mean tile height under the footprint ≠ 0) | 0x1fcc68 (ride y), then 0x1e2880 |
| +0x98 | u8 | model rotation r | vtable `+0xb4` = 0x1e1de0 (from b6); read by 0x1e1de8 |
| +0x98 (u64) bit 25 | | ghost-valid (tool) | 0x1fea58 / tool 0x129dc8 |
| +0x9a | u8 | object state | vtable `+0x1f4` = 0x1e4d70 (0x1fcd08 sets 1) |
| +0xa0 | ptr | **next piece in chain** | 0x201410 / 0x202c00, 0x202980 |
| +0xa4 | ptr | head of the list of cars on this piece (`car+0` = next, `car+0x84` = piece) | 0x2023b0 every update |
| +0xa8 | ptr | owning ride | 0x1fcc68 |
| +0xac | ptr | "crossing partner", set for shapes 4/99 | 0x1fd818 (see quirk Q3) |
| +0xb0 + 14i | 4×{s16 p1x, p1z, p2x, p2z, h, **pitch**, yaw} | the sample table | 0x1fdba8 / 0x1fdb20, 0x1fdb88 |
| +0xe8 | u32 | upgrade: DBA handle of the upgrade asset | 0x1fd818 / vtable `+0x5c` |
| +0xed | u8 flags (bits 40–43 of the u64 at +0xe8) | b0 = samples from 0x1fe4e8 (dead); b1 = **committed** (gates +0x44, +0x10c, upgrade stamping); b2 = cleared in 0x1fd818; b3 = "special" | see §9 |
| +0xec | u8 | **piece type** 0–47 | 0x1fd818 / 0x1fe9b0 (32 refs) |
| +0xee | u8 | 1 for types 12–15 and 28–31 when "special" ≠ 0 | 0x1fd818 only |
| +0xef | u8 | 1 for upgrade types (never cleared) | 0x1fd818 only |
| +0xf0/+0xf4/+0xf8 | f32 | model origin world x, y, z | 0x1fcdb8 |
| +0xfc | f32 | model angle r·π/2 | 0x1fcdb8 |
| +0x100 | s32 | current mesh resource id (−1 none) | 0x1fd818 |

- `+0xee`, `+0xef` and bits 42/43: **no reader found**. I scanned all of `.text` (0x100000–0x2a420c)
  for `lb`/`lbu` with imm 0xed/0xee/0xef (0 hits) and `ld` imm 0xe8 (15 hits; all in the piece
  code except one unrelated class).
- "special" in `0x1fd818` =
  - type ≥ 40: the per-park byte 1;
  - shape 15 or 99: 0;
  - **otherwise the caller's `$s3`, uninitialised** (READ, MIPS `0x1fd9f8..0x1fda14`).

**Model origin (`0x1fcdb8`, vtable `+0x2d4`, READ).** With W = b2<<8, D = b4<<8:
- r=0: (x·256, z·256), angle 0
- r=1: (x·256, z·256 + W), angle π/2
- r=2: (x·256 + W, z·256 + D), angle π
- r=3: (x·256 + D, z·256), angle 3π/2

`+0xf4` = (float)`+0x86`, **not** scaled by 256. The base version `0x1e4b10` does scale it.

**Mesh transform (`0x1fd0c0`, vtable `+0x44`, READ).** Only when committed. Corner offset
(dx, dz) = r0 (+W,+D), r1 (+W,−D), r2 (−W,−D), r3 (−W,+D).
- Types 12–19 and 28–31: model yaw = **2π − angle**, position = origin.
- All others: yaw = **π − angle**, position = origin + (dx, dz).

(Render-only. INFERRED: the meshes are authored with different pivots.)

---------------------------------------------------------------------------------------------------

## 9. Piece vtable `0x36b670`: overrides of the base `0x369aa0` (READ)

Slot = offset of the function pointer, as used in the calls `(vt+off)(this + *(s16*)(vt+off-4))`.
The table runs to +0x2dc (92 entries).

| fn slot | piece fn | base fn | does |
|---|---|---|---|
| +0x00c | 0x1ff1d0 | 0x1e0ed0 | destructor |
| +0x03c | 0x1fd0b8 | 0x1e5138 | per-frame state dispatch: **empty** for pieces |
| +0x044 | 0x1fd0c0 | 0x1e52e0 | place/rotate the mesh (above) |
| +0x054 | 0x1fd3a8 | 0x1e1440 | release DBA payload (only type < 40) |
| +0x05c | 0x1fd368 | 0x1e1420 | get DBA payload: type < 40 → base handle `+0x20`; type ≥ 40 → `+0xe8` |
| +0x084 | 0x1fe9b8 | 0x1e15d0 | footprint width: 2, or 4 if type ≥ 40 |
| +0x08c | 0x1fe9e8 | 0x1e1668 | height class = table b3 |
| +0x094 | 0x1fea08 | 0x1e1670 | footprint depth: 2 or 4 |
| +0x10c | 0x1fcf78 | 0x1e12e0 | remove: hide mesh, `+0x100=−1`; if shape ≠ 99 and committed, `0x1fd538` clears the tiles' `+7` flags (and resets them via `0x1e5f50` for upgrades), then clear committed; a still-committed shape-99 piece clears tile `+6` bits over (W+1)×(D+1) |
| +0x154 | 0x1fcbd0 | 0x295160 (pure virtual) | init(type): `+0x100=−1`, `+0xe8=+0xa8=+0xa0=0`, `0x1fd818(type)`, clear bits 40–41 |
| +0x164 | 0x1fcd08 | 0x1e1238 | commit: state 1; unless type 8–11: `0x1fd3e8` (tile flags 0x10/0x50), `0x1e2880` (**flatten terrain**, set `+0x86`), and for type > 39 + committed `0x1e2ad0` (stamp the upgrade's DBA footprint: tile kinds 5/7/8…) |
| +0x2b4 | 0x1ff238 | 0x1e1708 | has connection A → 0 |
| +0x2bc | 0x1ff240 | 0x1e19f0 | has connection B → 0 |
| +0x2d4 | 0x1fcdb8 | 0x1e4b10 | compute model origin/angle |

Everything else is inherited. The ones the track code uses:
- `+0x74` = 0x1e1fa8 returns the 8 bytes at `+0x84` (x, y, z);
- `+0x6c` = 0x1e1f00 returns (x<<8, y<<8, z<<8);
- `+0xb4` = 0x1e1de0 sets r;
- `+0x1f4` = 0x1e4d70 sets state.

`0x1ff1f8`/`0x1ff210`/`0x1ff228`/`0x1ff230` (bit-41 getter, bit-25 getter, `+0xa8` getter,
`+0xac` getter) have no `jal` callers.

---------------------------------------------------------------------------------------------------

## 10. The sample table, `0x1fdba8(piece)` (READ, full MIPS; jump table `0x36b5b0`)

For i = 0..3 the function builds two local points P1 and P2, a height h and a yaw in quarter turns
(`Y`). Then:
- world = R(r)·local + (`+0xf0`, `+0xf8`);
- store {P1.x, P1.z, P2.x, P2.z, h, —, yaw = trunc(Y·1024)} at `+0xb0 + 14i`.

`R(r)` is angle θ = −r·π/2, fixed point 4.12 with truncation, applied by `0x194b90`. The effect is
**x' = cosθ·x − sinθ·z, z' = sinθ·x + cosθ·z**, so r=1 maps local (x,z) to (z, −x). The world y
of the transformed point is discarded. The height is `h` only.

**Base height `B`** (station/ride y from ride vtable `+0x6c`, i.e. `ride+0x8e`<<8):

```
B = (stationY << 8) + 0x50                                  # 80
if world==1 && park==1: B = (stationY << 8) + 0x90          # CryptKarts: 144
if world==2 && park==1: B += 0x40                           # Bumble Buggies: 144
... after the per-type rule:  if world==3 && park==1: h += 0x5a   # Space Racers: +90 on EVERY sample
```

**Defaults.** P1 = (160, 128i), P2 = (352, 128i), h = B, Y = r. The travel is along local +z;
the lanes are 96 either side of the 2×2 centre x = 256.

| types | override |
|---|---|
| 0–3 station | P1 = (128i, 608), P2 = (128i, 416), Y = r+1; type 0: z += 512 (both); type 3: x += 512 and z += 512; type 1: x += 512. Travel is along local +x, centred on z = 512. |
| 8–11 connector | `G` = the global `0x2ee4c0` (below). G < 4 (after a station): **x −= 256 on both lanes**. G ∈ 40–43: h += `upg[w][p][k0][3].y` for i < 3, and also i = 3 only in JUNGLE p1. G ∈ 44–47: h += `upg[w][p][k1][3].y` for i < 3, and also i = 3 only in HALLOW p2. |
| 12–15 `b` | θ = i·π/8; P1 = (512 − ⌊160 cosθ⌋, 512 − ⌊160 sinθ⌋), P2 = (512 − ⌊352 cosθ⌋, 512 − ⌊352 sinθ⌋); Y = (r+2) − (i+1)/4 |
| 16–19 `b2` | θ = **(3−i)**·π/8; P1 = (512 − ⌊352 cosθ⌋, 512 − ⌊352 sinθ⌋), P2 = (512 − ⌊160 cosθ⌋, 512 − ⌊160 sinθ⌋); Y = (r+3) + (i+1)/4 |
| 24–27 `h` | h = **0x100** (absolute) for i ≠ 0 |
| 28–31 `h_u` | P1 = (352, (4−i)·128), P2 = (160, (4−i)·128): travel along local −z, which is why r is the straight's +2. Y = r+2; h = 0x100 for i ≠ 0 |
| 32–39 `h_a`, `h_d` | h = 0x100 for all i |
| 40–47 upgrade | e = `upg[w][p][kind][i]` = s16×4 at `0x2edd60 + w·0x120 + p·0x60 + kind·0x20 + i·8`; P1 += (e.x, e.y, e.z), P2 likewise, h += e.y. Every retail e.x = 256, which centres the lanes at 512 in the 4-wide frame. |
| 20–23, 4–7 | defaults |

- `⌊⌋` here is C truncation of float→int. The sequence is `cvt.w.s`, back to float, subtract,
  `cvt.w.s` again.
- **The global `0x2ee4c0`** is written with the piece type by every non-connector call (MIPS
  `0x1fdd70..7c`). So a connector reads "the previous non-connector piece built". It works because
  `0x2027e0` builds pieces in chain order.

**Upgrade height offsets** (e.y for i = 0..3; all e.x = 256, e.z = 0):

| park | kind 0 | kind 1 |
|---|---|---|
| JUNGLE p1 (mammtunn / lavajump) | 0, −128, −128, −128 | 0, +64, +64, +64 |
| JUNGLE p2 (watertun) | 0,0,0,0 | – |
| HALLOW p1 (ogre) | 0,0,0,0 | – |
| HALLOW p2 (chopper / firepit) | 0, 0, −64, −64 | 0, +128, +128, +128 |
| FANTASY p1 | 0,0,0,0 | – |
| FANTASY p2 (beejump / honeypot) | +64 ×4 | 0 ×4 |
| SPACE p1 (meteor) | 0,0,0,0 | – |

So, SIM for Dino Karts: the mammoth tunnel dips the cars to 80 − 128 = **−48** through the upgrade
and its connector. The lava jump lifts them to 144 and drops back on the connector's last sample.

**Bend geometry.** Both bends are quarter circles of centre-line radius **256** (one cell), with
lanes at radius 160 and 352. The centre is the local corner (512, 512).
- `b` samples at arc fractions 0, ¼, ½, ¾. Its yaw leads its position by one sample.
- `b2` samples at ¼, ½, ¾, **1**: the entry sample is missing and the exit sample duplicates the
  next piece's sample 0. SIM: the centre-line step into a `b2` is 226 units and the step out is
  0, where every other step is 128.

Pieces are built one per ride update: `0x2027e0`, state `+0x2888` = done, `+0x2889` = next index.

---------------------------------------------------------------------------------------------------

## 11. Pose along the track (READ)

| fn | returns |
|---|---|
| `0x202898(out, ride, d)` | copies the 14-byte sample of piece `((d & 0xffff) % L) >> 8`, index `(d & 0xff) >> 6` (`0x1fdb20`). `L == 0` traps. |
| `0x2028f0(ride, d)` | table byte 7 of that piece = **always 0x80** in every shipped table (base and per-park). Callers: boat step `0x203780`, kart step `0x204798`. |
| `0x202938(ride, d)` | that sample's yaw (`0x1fdb88`: `+0xbc + 14·idx`) |

The car keeps two samples: `car+0x5c..0x69` = S(d) and `car+0x6a..0x77` = S(d+0x40) (set in
`0x2053a0`, refreshed by the step functions). The draw `0x204c38` computes:

```
f    = d & 0x3f                       # car+0x50
lane = car+0x5a                       # 0..255, 0x7f = middle
Ax = A.p1x + ((A.p2x - A.p1x) * lane >> 8);   Az likewise;  Bx, Bz likewise
x  = Ax + ((Bx - Ax) * f >> 6);   z = Az + ((Bz - Az) * f >> 6)
y  = A.h + ((B.h - A.h) * f >> 6)
model yaw   = -(car+0x4a) * 2π/4096          # car+0x4a initialised from 0x202938
model pitch = car+0x4c + A.pitch             # 0x194fc0; A.pitch = sample field 5 (+0xba), NEVER written by live code
roll        = 0x1951d0(car+0x4e)             # stub: returns 0 and does nothing
```

- All shifts are arithmetic.
- **Lane 0 is P1**, which (SIM) is always on the side reached by rotating the heading by −1024 yaw.
  Heading +x puts P1 at larger z.
- So the answer to "linear, arcs or table": **a baked table of 4 samples per piece, with piecewise
  linear interpolation at 64-unit spacing.** Arcs exist only as the sin/cos evaluated at bake time.

---------------------------------------------------------------------------------------------------

## 12. Terrain and height

- **The chooser never looks at ground height** (READ, the whole of `0x200fb8`). Hills come only from
  path (2), queue (4) or kind-7 tiles (7 = an attraction's connection A, per `0x1e2ad0`).
- The car height is `B` (§10). It depends on the **station's** y, not on each piece.
- Placing a piece (`0x1e2880` via `+0x164`) **flattens** its footprint: every tile's `+1` =
  round(mean of `tile[+1]·4`, then /4). The piece's `+0x86` becomes 2 if that mean ≠ 0, else 0.
  Retail tiles hold 0 or 2 (tpwps2 `findings/paths.md`), so this can create height-1 tiles.
- The bridge deck is absolute **0x100**. Only when station y = 0 is it "above" the flat track
  (80/144/170).
- **Laying a path or queue under an existing track rebuilds it into a bridge** (READ):
  - `0x18e4b8` (tile link update) and `0x1e6cf0` (tile event 0x84, and 0x32) find the piece on the
    tile;
  - if its shape ≠ 3, `0x1fd818(piece, 8)` (invisible, so the rebuild's remove skips `0x1fd538`
    and keeps the new tile flags; INFERRED rationale), then `0x2009c0(piece.ride)`.
- ⚠ **Unit mismatch, unresolved.**
  - Tile height → world is `tile[+1]<<2` (`0x152760`, `0x14e250`).
  - An object's y is `+0x86<<8` (`0x1e1f00`).
  - The piece model's y is the raw `+0x86` as a float (`0x1fcdb8`).

  These three scales cannot all be right, and I did not settle it. Consistent reading for a port:
  station on level-0 ground → flat car height 80 (or 144 / 170), bridge 256, all in the same units
  as x/z (256 per cell), because the car's model position takes (x, h, z) in one call.

---------------------------------------------------------------------------------------------------

## 13. Piece points and excitement (READ)

`0x200c20` sums per piece into `ride+0x1d1`:
- 4 for types 20–23 and 40–51;
- 2 for 24–31 and 36–39;
- 1 for everything else, **including `h_a` 32–35**.

`0x202188` computes

```
a = clamp((slot2f4() << 12) / 100, 0xc00, 0x1400)
b = clamp((slot304() << 12) / 5,   0xc00, 0x1400)
excitement = min(100, ((rec+0x18 + (ride+0x1d1 >> 1)) * (a * b >> 12)) >> 12)
```

where `rec+0x18` is the DBA base excitement. Slots `+0x2f4` and `+0x304` are the Speed and Duration settings
(`track-ride-operation.md` §7.1).

---------------------------------------------------------------------------------------------------

## 14. Quirks and bugs a faithful port inherits (READ unless marked)

- **Q1, the b2 sample skew.** On every `b2` bend the cars jump ¼ arc in one 64-unit step, then sit
  still for 64 units at the exit (§10, SIM). `b` bends are uniform.
- **Q2, U-turns.** `0x2ee518` gives 0 (a station piece) for a U-turn.
- **Q3, the crossing partner.** `0x1fd818` looks up `+0xac` with `0x14a420(cellx, cellz)`, but that
  function takes world units (`>>8` inside), so it searches cell (x>>8, z>>8) ≈ (0,0). Effectively
  `+0xac` stays 0.
- **Q4, the connector global.** The connector reads the global `0x2ee4c0`. Two track rides building
  their tables in the same updates can clobber it for each other (INFERRED hazard). Pass the
  previous type explicitly.
- **Q5, SPACE upgrades.** SPACE p2's upgrade table pointer is NULL, and SPACE p1's kind-1 rows are
  live globals.
- **Q6, odd legs.** They leave 1-cell holes, and nothing checks for them.
- **Q7, bridges ignore the turn.** A bridge piece takes the previous exit direction.
- **Q8, the "special" flag.** In `0x1fd818` it depends on an uninitialised register for ordinary
  pieces. There are no readers, so it is harmless.
- **Q9, the pitch field.** Sample field 5 (pitch) is only written by `0x1fe4e8`, a 12-argument
  generic sample builder with **no callers**:
  - no `jal`/`j`;
  - no 4-byte pointer;
  - no `lui`/`addiu` pair anywhere.

  `0x200e48` is dead the same way.

---------------------------------------------------------------------------------------------------

## 15. How this was checked

- The key functions were read in MIPS, not only in the decompile: `0x200cd0`, `0x200fb8`,
  `0x201410`, `0x1fd818`, `0x1fdba8`, `0x1fcdb8`, `0x1fd0c0`, `0x202c00`, `0x2009c0` (loop),
  `0x1fd6c0`, `0x1e2880`.
- **SIM.** The local re-implementation re-implements §2–§5 and §10 from the reads and builds closed loops for all 4
  station rotations: right-turn loops, left-turn loops, bridges over 1-piece and 3-piece paths, a
  self-crossing, and two upgrades.
  - Centre-line steps are all exactly 128. The only exception is the `b2` 226/0 pair.
  - Lane P1 stays on one side of travel everywhere.
- **Controls.** Each mutation must break the result, and each does:
  - transposing R(r) → gaps up to 2176;
  - dropping the connector −256 shift → 286-unit gaps at every station;
  - `b2` θ = (4−i) instead of (3−i) → the 226/0 pair disappears, steps 100–128.

  So the check can see the things it vouches for. It **cannot** see heights, meshes or anything
  about runtime order; those rest on the reads.

---------------------------------------------------------------------------------------------------

## 16. Still unknown

- **The units of y** (§12): terrain `<<2`, object `<<8`, the piece mesh raw. Needs a live PS2
  savestate. None is on this box: `find` for `*.p2s` came up empty, and `gatelog/` and `tpw/` hold
  2 MB PSX dumps.
- **How the meshes sit relative to the samples.** I did not open `gk_trck*.mps`, so the hump
  profile and the deck heights against 80/256 are unverified. The render transform is in §8.
- The meaning of the upgrade id (per-park byte 1 = DBA kind-8 `+0x2c`) beyond "nonzero".
- Whether parsed `.sam` Bumper values are ever read at runtime. The schema accessor `0x1f98c8` is
  reachable only through vtable `0x36ad90` (built at `0x1f7cc0`), and I did not trace its users.
- The source of `ride+0xa0` (station rotation) against the `.sam` N/E/S/W naming. I mapped rot 0 ↔
  North by the numbers only.
- `bc..bf` and `c1..d7` of the kind-6 record (only `+0xc0` has a consumer here).
- Whether the ride pool zeroes memory, and so whether the never-written pitch field is 0.
