# The park grid — where the plot's extent is authored, and where it isn't

**2026-09-22.** Worked jointly with `cow tools`, who found the `heightfield` marker mesh; this file
records the executable side, the independent verification of that mesh, and two corrections.

## The `.lnd` path is dead code on the PS2

The engine's module map is in the binary as assert paths — `TPHField.cpp`, `TPHMap.cpp`,
`TPMesh.cpp`, `TPInstance.cpp`, `TPHoarding.cpp`, `TPEmbed.cpp`, `TPRide.cpp`, `Land.cpp`,
`M3D2Manip.cpp` — so there is a heightfield subsystem, named as such. Its strings sit in one
cluster at `0x36a850`:

    "%sbase.lnd"  "base.lnd"  "%s\Terrain"  "base.md2"  "Base"  "TestBase"
    "Could not load heightfield"   Source/Core/Code/TPHField.cpp

All seven are referenced from **one function, `0x001f3248`**, and nothing else references them.
Disassembled (R5900, so `LQ`/`SQ` hand-decoded — a stock MIPS32 disassembler stops dead at the
first quadword spill in the prologue):

```
0x1f3248  prologue
0x1f325c  beqz  $a0 -> 0x1f3288        two path builders, 0x146f80 / 0x147090
0x1f326c  addiu $s0, "%s\Terrain"
0x1f32c4  addiu $a0, "base.md2"   ; jal 0x1f79d0    load the model
0x1f32e0  addiu $a0, "Base"       ; jal 0x1f7ed8    look up a mesh by name
0x1f3300  bnez  -> success
0x1f3310  addiu $a0, "TestBase"   ; jal 0x1f7ed8    retry with the fallback name
0x1f3330  bnez  -> success
0x1f3340  both globals = 0, return 0
0x1f33e0  addiu $a0, "Could not load heightfield"   ; jal 0x1f3720
```

So the intended mechanism is: build `<world>\Terrain`, load `base.md2`, and pull a mesh named
**`Base`**, or failing that **`TestBase`**, out of it.

⚠ **None of `base.md2`, `Base` or `TestBase` exists anywhere on this disc.** No `.lnd` either — a
raw scan of the whole 621,734,736-byte image finds the string `base.lnd` exactly once, as the
format string inside `SLES_500.32` itself. Every mesh name in all eight `terrain_*.mps` files was
checked: no `Base`, no `TestBase`. **This is the PC-era path, left in the PS2 build.** Anyone
hunting for a `.lnd` heightfield file is hunting for something that was never shipped here.

## What IS authored: the `heightfield` marker mesh

Every world's `terrain_1.mps` and `terrain_2.mps` carries a mesh named **`heightfield`** with
**zero vertices and zero faces** — no geometry at all — but with its bounding box filled in. The
box is the plot.

Verified independently against all eight files (`+0x70` min, `+0x80` max, each a `float[4]`):

| world | X span | Z span | cells | Y span |
|---|---:|---:|---|---:|
| JUNGLE | −0.64 … 641.92 | −0.76 … 762.28 | **64 × 76** | −0.021042 … 20.061041 |
| FANTASY | −0.80 … 802.40 | −0.60 … 601.80 | **80 × 60** | −0.021042 … 20.061041 |
| HALLOW | −0.96 … 962.88 | −0.52 … 521.56 | **96 × 52** | −0.021042 … 20.061041 |
| SPACE | −0.96 … 962.88 | −0.54 … 541.62 | **96 × 54** | −0.021042 … 20.061041 |

At the root scale of `0.100` established in `findings/grid.md`, and 1 model unit = 1 footprint cell,
those are the grids. `terrain_1` and `terrain_2` agree exactly within each world.

### ⚠ Correction 1 — the box carries an exact 0.4% pad, so divide, don't round

The minimum is **not** zero and the maximum is **not** the extent. The exporter inflates the box by
a fixed ratio: `min = −0.001 × extent`, `max = 1.003 × extent`. So

    cells = (max − min) / 1.004 / 10

and that lands on **exact integers on every axis of every world** — 640.0000, 760.0000, 800.0000,
600.0000, 960.0000, 520.0000, 960.0000, 540.0000, to four decimal places. Rounding or flooring the
raw numbers happens to work on the maxima and **fails on the minima**, where `−0.064` floors to −1
rather than 0.

This same inflation shows elsewhere in the file and is what it looks like when you have geometry to
check against: `EMBANKMENT` stores ±200.30 where its own vertices reach ±199.50.

### ⚠ Correction 2 — the Y range is a hardcoded constant, not the elevation

It is tempting to read `Y 0 … 2.01` as the park's elevation envelope. It is not data.

**The Y minimum and maximum are bit-identical in all eight terrain files across all four worlds** —
`4660acbc` and `037da041`, `−0.021042000502347946` and `20.0610408782959`. Applying the same
`/1.004` gives exactly **2.0**. It is a fixed two-cell ceiling the exporter writes into every world,
and it carries **zero per-world information**. Nothing about how high JUNGLE's volcano is can be
recovered from it.

A number that comes out identical everywhere is a constant, not a measurement — and the check that
separates the two is cheap: compare the raw bits across every instance before reading meaning into
the value.

## What is still open

The marker gives the **extent and the resolution**. It does not give the **per-cell heights**, and
no file on the disc obviously does. Two candidates remain, neither confirmed:

* the field is rasterised from the terrain mesh at load, in which case `EMBANKMENT`, `VOLCANO` and
  `newcliff12` — which are terrain, not props — are the source and the marker is what they are
  rasterised *into*;
* the field starts flat and every elevation in a fresh park is authored some other way.

`Load_ReadLand` / `Save_WriteLand()` confirm the field round-trips through saves, which settles how
it persists and not where it starts.
