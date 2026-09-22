# The park heightfield, found in live RAM

**Status: per-cell heights FOUND.** Read out of a PCSX2 savestate of a live jungle park.

## Why RAM and not the disc

The plot's *extent* is authored on the disc (the `heightfield` marker's AABB — see
`reference`/the commit history). Its *fill* is not. tinyclaw traced the disc loader at
`0x1f3248` to a target, `data\<world>\Terrain\base.md2`, that exists for no world, so nothing
on the disc ever supplies per-cell data. A disc image cannot show a structure that only exists
at runtime; that is a boundary of static analysis, not a lack of effort.

## The globals

| address | holds |
|---|---|
| `0x2ea840` | the loaded terrain model (M3D2, `TERRAIN_1.mps`) — **not** the field |
| `0x2ea83c` | ⭐ **the park field object** |

`0x2ea840` was the handle we were given; it points at the model. The field is **one word
earlier**, found by dumping the globals either side of it instead of trusting the label.

## The field object

```
+0x0c  u32  NX      64      jungle terrain_1  (predicted from the disc BEFORE looking)
+0x10  u32  NZ      76           "
+0x18  f32  2.0             the Y envelope max, matching the authored AABB
+0x20  f32  10.0            model units per cell (10 units = 1 world cell)
+0x24  ptr  cells   -> OBJ+0x30, NX*NZ entries of 2 bytes
+0x28  u32  2
```

## Cell encoding (2 bytes)

- `byte0 & 0x3F` — **the height. Only ever 0, 1 or 2** across all 4,864 cells, exactly the
  authored `Y 0..2` envelope. Observed: 3,104 / 1,477 / 283.
- `byte0 & 0x40` — a flag, set on **67 contiguous cells**. A real spatial feature; meaning unknown.
- `byte1` — ⚠ **UNIDENTIFIED.** Varies per cell (24 / 0 / 57 / 55–60). Surface type and grass
  variant are both plausible (the disc ships `jgr_bas2..6`), but rendered out it repeats more
  than a hand-built park should. Not named until it is proven.

## Why this is a confirmation rather than a find

The grid sizes were derived from the disc and written down **first** — jungle 64×76 = 4,864,
fantasy 80×60 = 4,800 or 76×62 = 4,712, hallow 96×52 = 4,992 or 88×56 = 4,928, space 96×54 =
5,184 or 72×62 = 4,464. The object's own header then read back 64 and 76. The park has no
terrain tool, so the raise-a-tile diff never existed; predicting the size from the file is the
stronger control anyway, because the expected value comes from the data rather than from
whatever an edit happened to do.

## Reproducing

`tools/p2s.py <state.p2s>`. PCSX2 v2 compresses savestates with **zstandard** (zip method 93);
python's `zipfile` refuses it, so the reader pipes the raw member through the `zstd` CLI —
otherwise a perfectly good state reads as corrupt.

## Open

- `byte1`.
- Confirmation in a second world: a space or hallow park predicts 5,184 / 4,992 (or 4,464 /
  4,928 for the second park of each), which would turn this from one reading into a test.
