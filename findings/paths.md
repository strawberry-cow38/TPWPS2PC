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

⚠⚠ **AND NOTHING IS WALKABLE AT LOAD.** The fill writes kind `0` or `1` and never `2` or `4`, so
the entrance plaza is not walkable by anything this function does. Something else must write the
bus stop and the turnstiles into the tile map, and **that writer has not been found.** Until it
is, `ParkPaths.EntranceParts` — a list of three mesh names — is a STAND-IN chosen by me, not a
recovered rule, and it should be replaced by whatever that writer actually does.
