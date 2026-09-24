# Paths — how a park path chooses its tile, and where the game keeps the answer

Every address is in the PAL `SLES_500.32` (PT_LOAD at EE 0x100000). READ means read out of the
executable's own code or data; PREDICTED means stated before it was checked.

## 1. A path is a ground tile

The authored terrain grid gives each cell two bytes (`Model.HeightField`). `byte1` indexes the
terrain model's **own material table**, and the path art is a block of twenty entries at the end of
it — sixteen path tiles then four queue tiles:

```
squ1 end1 str1 cnr2 tju1 xrd1 tju2 tju3 xrd2 cnr1 edg1 icn1 icn2 ctr1 ctr1 tju4 | que4 que3 que1 que2
```

The same twenty in all four worlds, all under the jungle's `jpa_` prefix, at a different base index
per file (FANTASY 45, HALLOW 45, SPACE 47, JUNGLE 61). So the tile is found by **what its name says
it is**, never by an index and never by the world's letter.

**No shipped park has a path already laid**: 0 path-tiled cells in all four worlds (measured over
every cell of every terrain file). A park starts from bare ground.

## 2. The piece tables (READ)

A path cell's art is not stored — it is **derived from its neighbours every time anything next to it
changes**. The game keeps two tables of 12-byte records:

| field | width | meaning |
|---|---|---|
| +0 | u32 | high half = which list (0 grass, 1 path, 2 queue), low half = the sprite in it |
| +4 | s32 | the angle in degrees; /90 is the quarter turns |
| +8 | u8 | the eight neighbour bits this record answers |

| table | address | count at | records |
|---|---|---|---|
| path | 0x2B8128 | 0x3611DC | 49 |
| queue | 0x2B80A0 | 0x3611D8 | 11 |

Found by the table's own shape — a 12-byte stride whose middle word is a quarter-turn angle — and
then **pinned by which pair the code loads**: the chooser at 0x1E6950 takes the path table for tile
kind 13 (and kind 2 in the branch above) and the queue table otherwise, `lui v0,0x2c` +
`addiu s1,-0x7ed8` = 0x2B8128 at 0x1E6968, `-0x7f60` = 0x2B80A0 at 0x1E697C. That region is the
tile code: it calls the tile accessor 0x14E138 and the height-to-world helper 0x152760.

⚠ A second pair with the same shape and **different masks** sits at 0x2E23F0 / 0x2E2368. **Nothing
references it** (whole-image scan of every `lui`+`addiu`/`lw`/`sw` pair), so it is not used here.
Picking it by eye would have given every piece the wrong turn.

## 3. The bits, and the rule

The four one-arm records carry 0x01, 0x04, 0x10 and 0x40, so:

```
North 0x01  NorthEast 0x02  East 0x04  SouthEast 0x08
South 0x10  SouthWest 0x20  West 0x40  NorthWest 0x80      (z grows southwards)
```

**Linking** (PSX 0x8004E20C, the same tables): an orthogonal neighbour of the same kind is always
joined; a **diagonal one only when both cells between it and this one are also path**. Without that
second half a path laid round the outside of a corner reads as a solid block and wears the centre
tile.

**Choosing**: the **last** record whose mask is exactly the links, or failing that the **first**
whose mask the links contain; the catch-all (mask 0) always answers. Last-then-first matters —
sprite 12 appears twice with the same mask at two different angles, so "first exact match" would
give a different turn from the game's.

**A queue laid onto a path** makes the one cell that is both (kind 13). That is the *only* join
between a path network and a queue run, and from outside it is invisible: the two are drawn
touching either way, and without it nobody can walk between them.

## 4. What makes this more than a plausible reading

Four independent lines, from two sources and three methods:

1. the counts are **49 and 11** — what the PSX build's tables hold, read from a different
   executable by different means (`findings/paths.md` in the PSX port);
2. the bit assignment **falls out of the data** (the four one-arm masks) and lands on the PSX's;
3. the sprite indices land on the right **art**, PREDICTED from the names before it was run:
   sprite 0 answers mask 0 (alone) → `squ1`; 1 the four single arms → `end1`; 2 masks 0x11/0x44 →
   `str1`; 3 the four right angles → `cnr2`; 4 the three-arms → `tju1`; 5 the four-arm 0x55 →
   `xrd1`; 13 answers 0xFF, every neighbour joined → `ctr1`, the **centre**. Seven named, seven hit;
4. the queue list is `lone, end, straight, corner` in order — what the PSX report says the queue
   sprite list is — although the PS2 names them que4, que3, que1, que2.

## 5. What is NOT read

- **Where a PS2 tile keeps its chosen piece.** The PSX packs `sprite | turns << 12` into the tile's
  ground word (0x8004D250). No such pack appears anywhere in the PS2 chooser's region, so the turn
  is kept beside the grid in this port rather than claimed to be in the tile.
- The PS2's own linking code. The link rule above is the PSX's, carried over because both builds
  feed the **same tables**; the tables are what was read here.
- The starting path the shipped maps lay just inside the gate (PSX 0x800541B8). The PS2 grids carry
  no path at all, so whatever lays it runs at load and has not been located.

## 6. Checked

`PathTool` is exercised against all four discs' terrains, fourteen checks each, every expectation
written before the run and derived from the table and the geometry, never from the output:

lone → `squ1`; a pair → `end1` facing each way; a three-long run → `str1` turned once in the middle
and `end1` at the ends; a crossroads → `xrd1`; a corner joined east and south → `cnr2` unturned;
**the same corner with its fourth cell filled → `cnr1`** (the diagonal rule); a 3×3 block's middle →
`ctr1`; a T joined north/east/south → `tju1` unturned; a queue run → `que1` and `que3`; and a
no-build cell **refuses**. All pass in FANTASY, HALLOW, JUNGLE and SPACE.

## The runtime tile, and what the engine calls walkable

2026-09-23. Read out of `SLES_500.32` with Ghidra, not inferred.

The park's runtime tile map is **8 bytes per tile**, allocated and filled at `0x14E5B0` from the
authored grid (the fill loop the earlier note placed at `0x14E700` is inside this function):

| offset | filled at load with |
|---|---|
| `+0` | **kind** — `1` if the authored `byte0` bit 0 is set (a skipped cell), `0` otherwise |
| `+1` | height — `2` if authored `byte0 & 0x40`, else `0` |
| `+2` `+3` `+6` | zero |
| `+4..5` | `s16` block id = `(x / 10) * 10 + (y / 10) + 1` — a **10×10 zone number** |
| `+7` | **flags** — `0x23` (bits 0, 1, 5) for a skipped cell, `0` otherwise |

`FUN_0018E710(x, y, mask)` is the whole flags test: `(tile[+7] & mask) != 0`. The predicates
built on it, with the kind byte, are where walkability actually lives:

| function | test | meaning |
|---|---|---|
| `0x18E020` | `tile[0] == 2` and `!(flags & 1)` | a usable **path** is here |
| `0x18E0C8` | `tile[0] == 4` | a **queue** is here |
| `0x18E1E8` | `tile[0] == 2` and `flags & 8` | path, plus something bit 3 marks |
| `0x18E158` | `tile[0] == 4` and `flags & 8` | queue, same |
| `0x18E278` | `flags & 2` | **nothing may be built here** |

⭐⭐ **So the engine's walkable set is the KIND BYTE: 2 is path, 4 is queue.** Those are the same
numbers `PathTool.Kind` already carries. Flags bit 1 is the no-build bit, which is why a skipped
cell's `0x23` refuses a placement; bit 0 additionally makes a path tile unusable.

## The entrance is a table in the executable

⭐⭐ **FOUND, and it is the same function.** `0x14E5B0` runs the fill loop above and then, still in
the same call, draws the park's own entrance into the tile map from a per-park table at
**`0x2B71B0`** — `0x12` bytes per entry, `0x36` per world, so three parks each and twelve entries.

| offset in the entry | field |
|---|---|
| `+0x00` | `xStart` — where the cross-corridor begins |
| `+0x01` | `zRow` — the row it runs along |
| `+0x06` | `apronX` — the left edge of the clearing at the mouth (`xStart + 1` in every entry) |
| `+0x07` | `apronZ` — where the clearing starts; `zEnd - 1` in ten entries, `zEnd - 2` in the two at `+0x2B7252` and `+0x2B7264` |
| `+0x08` | u16 `apronW` — the clearing's width, less two |
| `+0x0A` | u16 — **unread**; 4, 4, 3, 4 by world |
| `+0x0C` | u16 `pathRows` — how far the path the park STARTS WITH runs in past the mouth |
| `+0x0E` | u16 — zero in every entry |
| `+0x10` | `xCol` — the left column of the two-wide walkway |
| `+0x11` | `zEnd` — one past its last row |

and the shape it writes:

- row `zRow`, x from `xStart` to `xCol + 1` → kind `0x0C`, flags `8`
- columns `xCol` and `xCol + 1`, z from `zRow` to `zEnd - 2` → kind `0x0C`, flags `8`
- row `zEnd - 1`, both columns → kind **`0x0E`** — the mouth, where it meets the park

## ⭐⭐ The path a blank park is given

Master, playing it: *"a 4 long 2 wide path tile extends into the park from the entrance on a blank
park."* It is in the same function, and it is laid with the **path layer**, not written into the
tile map — `0x14E5B0` calls `0x15F4C0` / `0x15F4C8` on four cells:

```
(xCol,     zEnd + C)   (xCol,     zEnd)
(xCol + 1, zEnd)       (xCol + 1, zEnd + C)
```

the walkway's own two columns carried `C` rows further in, where
`C = (short)(*(u64 *)(entry + 8) >> 32)` — the u16 at `+0x0C`. Rows `zEnd .. zEnd + C`
inclusive is `C + 1` rows:

| entries | `xCol` | `+0x0C` | rows laid | world |
|---|---|---|---|---|
| 0, 1, 2 | 29 | 3 | **4** | JUNGLE — its three slots hold the same four numbers |
| 3, 4 | 47, 43 | 3 | **4** | |
| 6, 7 | 39, 37 | 5 | 6 | FANTASY — entry 6 predicts the savestate's walkway cell for cell |
| 9, 10 | 47, 35 | 4 | 5 | |

⭐ Four for JUNGLE, which is what master counted, and the table says the other worlds are not
four — so this is the game's number and not a constant fitted to one observation.

⚠ The two unnamed rows are HALLOW and SPACE, and WHICH IS WHICH IS NOT READ HERE. `Gates.sam`
gives a footprint height of 3 for HALLOW and 4 for SPACE, which would make entries 3–4 HALLOW
and 9–10 SPACE, and that correspondence holds on JUNGLE (3/3) and FANTASY (5/5) as well —
but it is a correspondence between two tables, not a reading of either, and nothing depends on it:
`ParkEntrance.Fit` matches an entry to the grid in front of it and never indexes by world.

**And the clearing around the mouth**, from the loop that follows:

```
for (i = 1; i <= C - 1; i++) {
    z = apronZ + i;
    if (z < zEnd) continue;
    for (x = apronX - 1; x <= apronX + apronW; x++)
        map[z][x] = (x == xCol || x == xCol + 1) ? {kind 2, flags 8}      // path
                                                 : {kind 1, flags 0x23};  // plain park ground
}
```

so JUNGLE gets rows 19 and 20 forced to ordinary buildable ground across x 26..33, with the two
walkway columns as path. ⚠ **READ, NOT APPLIED** — our grid takes buildability from the
authored bytes, and this overrides them for that block.

⚠ **And one case is read and cannot be applied.** The function ends with
`if (world == 0 && (park == 0 || park == 2))`, which lays a second run from (32,65) to (35,65) and
sets bit `0x80` then bit `0x02` on byte 7 of those four cells. It needs the loader's own world and
park NUMBERS; no file on the disc carries them, and `ParkEntrance` fits an entry against the grid
instead of indexing by them.

⭐ **`zRow` is 6 and `zEnd` is 19 in EVERY entry; only x moves.** findings/gates.md reached "the
entrance is one prefab translated in x" from mesh anchors, and this table says it again from the
code, which is two routes to the same sentence.

## Checked against a live park

Master's PCSX2 savestate, a running FANTASY park (30/03/2000, £21,671). Controls first: the field
object in RAM reads `NX=80 NZ=60`, 4800 cells, **872 skipped and 3928 drawn — exactly what our
disc read of FANTASY gives**, so the RAM grid and the disc grid are the same data in the same
frame before anything else is claimed.

Then the tile map, 81 x 61 at `0x2B73C0`:

| kind | count | where |
|---|---|---|
| `2` path | 43 | the player's network |
| `4` queue | 9 | beside it |
| `13` both | 2 | (40,24) and (39,25) — **exactly where a queue meets the path** |
| `12` | 27 | x 39..40, z 6..17, with a five-wide lip at z=6 |
| `14` | 2 | (39,18), (40,18) |

Table entry 6 reads `xStart=36 zRow=6 xCol=39 zEnd=19`, which **predicts those 27 and 2 cells
exactly**. And `2`, `4` and `13` are the numbers `PathTool.Kind` already carried, read back out of
live memory.

⚠⚠ **THE ENTRANCE IS NOT AUTHORED IN THE GRID.** Every corridor cell has authored `byte0 = 0x01`
and `byte1 = 0` — identical to the skipped cells either side of it at x=38 and x=41. There is
nothing in the terrain data that distinguishes the walkway; it exists only because this table
paints it.

⚠⚠ **AND `ParkPaths.EntranceParts` IS WRONG, not merely unproven.** Rasterising `A_ROAD`,
`A_BUS STOP` and `ticket_booths` gave **147 cells at z 33..47** for FANTASY. The game's answer is
**29 cells at x 39..40, z 6..18** — a two-tile walkway, not a fifteen-wide apron, and in a
different place. The mesh names were never the rule and the difference is not small.

## `jpa_que4` has no art, and it is the tile a queue never wears

2026-09-23. Jungle's terrain names twenty path materials at indices 61..80 and the resolver finds
nineteen of them. `jpa_que4.ssh` has no entry of any extension in any WAD on the disc
(findings/texture-fallback.md counts sixteen such references in total).

⭐ **Which SPRITE it is falls out of the list order, and the path list is the control.** The lists
are the material table's own order filtered by kind, and jungle's path block reads
`squ1 end1 str1 cnr2 tju1 xrd1 …` against the piece table's sprite 0 = alone, 1 = the four single
arms, 2 = straight, 3 = the four right angles — name for name, six for six. The same rule on the
queue block, which reads **`que4 que3 que1 que2`**, gives

| queue sprite | masks it answers | tile |
|---|---|---|
| 0 | `00` — no links at all | **`jpa_que4`, which does not exist** |
| 1 | `10 40 01 04` — one arm | `jpa_que3` |
| 2 | `11 44` — straight | `jpa_que1` |
| 3 | `14 50 41 05` — the four corners | `jpa_que2` |

So the missing tile is the one a queue cell wears when it is joined to **nothing** — not the
corner, which was the first guess and would have been every turn a queue makes. A queue cell
always carries at least its run link or its ride's door, so sprite 0 should never be chosen in
play. The reference is a real gap in the disc and a defect nobody can see.

⚠ Not established: whether the game falls back to something for it, or whether `jpa_que4` was
simply never authored. Only that no file of that stem ships.

## ⭐⭐ What a tile costs: path £10, queue £25

**READ, but out of the PSX executable, not this one.** Two builds were traced here; they agree on
the mechanism and only one of them gives up the numbers.

### The PS2 side (READ, and traced first)

The path tool is a global object. There are two of them, `0x389328` and `0x3890D0`, given their
vtables (`0x35AC48`, `0x35ACD0`) by the static initialiser at `0x11E2C0`; the two vtables are
identical but for one slot each, which is what makes them siblings rather than one class. Nothing
else in `0x100000..0x300000` touches either object, so the tool is only ever reached through a
pointer.

```
FUN_0011AD60 / FUN_0011CEB0   the two tools' activate handlers
    tool[0x3c] := *(u16 *)(P + 0xD0)        P = (tool[0x14])->vtable[slot 10](tool[0x14] + adj)
0x11B2D4                      tool[0x08] := tool[0x3c]      (and sw zero,8 on the other arm)
FUN_0011B898                  FUN_00100750(FUN_001005D8(), *(int *)(tool + 8) * 10)
```

So **the price is a field, spent ×10** — the tenths the till holds. `FUN_00100750` is the debit
whose twelve call sites are the whole game's spending.

⚠ **What the PS2 image does NOT give is the number.** `P + 0xD0` is filled at load by a scenario
path nobody has traced; there is no `sw` of an immediate into it anywhere in the image, and with
no savestate there is nothing to read `0x3890D8` / `0x389330` out of at runtime. Three earlier
attempts to corner it went wrong in instructive ways: a scan for `sw rX, 8(rY)` that had not
excluded `sp` returned a stack message struct's command id `0x13` as "price 19"; a `lui` xref
scan with a 40-byte lookahead reported the tool globals as unreferenced, which they are not; and
a world-relative probe for offset `0x794 + 0xD0` returned nothing **and so did its control**,
which is the only reason it was not believed.

### The PSX side (READ, and it has the figures)

`TPW.BIN` carved from the disc image (ISO9660 lba 205194, 1,065,308 bytes, load `0x80010000`) and
disassembled:

```
0x8001b580  jal 0x8001b600 ; addiu a0,zero,10      path  price := 10     -> gp+192
0x8001b588  jal 0x8001b618 ; addiu a0,zero,25      queue price := 25     -> gp+196
            getters 0x8001b60c / 0x8001b624
            run accumulator gp+208: set 0x8001b630, add 0x8001b63c, read 0x8001b654
```

`0x8004F360`, the ghost, prices and gates each tile as the run is drawn:

| what | where | what it says |
|---|---|---|
| which price | `0x8004F3A4` | kind **2** or **13** → path; kind **4** → queue; anything else keeps the 0 of `0x8004F368` |
| the gate | `0x8004F3F4`–`0x8004F438` | `bank − total×10` (`0x8004FFBC`) against 0 via `0x80050014`, which is `*a <= *b`; non-zero **refuses** |
| the ×10 | `0x8005002C` | `((n << 2) + n) << 1` — a pounds figure becomes the money the till holds |
| what is charged | `0x8004F484`–`0x8004F4AC` | skipped when the cell is already that kind, when it is `13` and a path is being laid, or when the validator refused it |
| reset | `0x8001B27C` | `setRunTotal(0)` as the run is re-walked, so the total is per ghost, not per session |
| charged | `0x8001D448` / `0x8001E0E0` | on the press, after the lay sound, through the same spend helper the placement tools use |

Two things worth stating separately, because both are one step from being got wrong:

- **The gate is `≤ 0`, not `< 0`.** `0x80050014` is `lw; lw; slt v0,v0,v1; xori v0,v0,1` — it
  returns `*a <= *b`, and a non-zero answer branches to the refusal. You may spend down to a
  pound and never to nothing.
- **The total checked is the one BEFORE this tile.** The check is at `0x8004F3F4` and the
  accumulate at `0x8004F4AC`, in that order, so a run reaches exactly one tile further than a
  check-after ordering would let it.

### Why the two builds corroborate each other

The PSX tool "puts the total in its **+8**" and spends **total × 10**; the PS2 tool puts its price
in **`tool + 0x08`** and spends `tool[8] * 10`. Same field offset, same multiplier, different
compilers, and the PS2 half was read before this file was opened. And the PSX price chooser's tile
kinds — **2 and 13 path, 4 queue** — are the same three numbers §2 of this file already recorded
from the PS2 chooser at `0x1E6950`, which takes the path table for kinds 13 and 2 and the queue
table otherwise. Two independent readings of two binaries meeting on the same three constants is
what makes this more than one trace.

⚠ **So the figures are the PSX build's.** Ported as `PathPrices`, with the PS2's own mechanism
around them. What would replace this paragraph is a PS2 savestate: `0x3890D8` and `0x389330` hold
the two numbers while the game is running.

⚠ Not read: whether the ghost's money test also consults the spend-anything flag (`park[8]`,
`ParkFinances.Unlimited`) the way the debit does. The port honours the flag on the ground that a
ghost refusing a purchase the till would allow is the worse of the two errors.
