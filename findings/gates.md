# The park entrance gate — authored position, READ

## ⭐ RESOLVED 2026-09-23. Start at "The gate's z is authored, and the .sam states its rectangle".

The `.sam` footprint is the gate's own authored bounding box: the x is exact in all four parks,
the z is contained in all four, and the gate takes this park's **x shift and nothing in z**. The
shift is `ticket_booths` centre minus 48, and two of the four parks shift by zero, which is a free
control.

## ⚠⚠ THE TWO SECTIONS BELOW ARE SUPERSEDED, AND THEY COST A READER AN HOUR ON 2026-09-24

"UNFINISHED — calibrated by eye" and "The open question" describe the state BEFORE that section
and are kept only so the reasoning is followable. ⭐ A stale header outranks a correct section
further down, because the header is what gets quoted: this file's opening line was repeated to
master as current fact while the answer sat sixty lines below it. If a finding is superseded, say
so where the reader lands, not only where the work happened.

## ⚠ SUPERSEDED — the state before the .sam rectangle was read

The gate renders in all four parks, at a position **calibrated by eye, not read from the disc**.
The real coordinate has been found and is written down below; what is missing is the frame it is
expressed in.

## What ships

`Features/Gates/Gates.mps` — the themed arch, three parts (`gateway`, `door01`, `door02`) over 50
frames of animation. Placed by keeping the model's own authored transform, shifting x by this
park's entrance offset from 48, and shifting z by Fantasy's `gatebase01` pad reading **plus 0.25
toward the road**, which is master's own correction with `[` / `]` in the viewer.

⚠ That 0.25 is a judgement by the only pair of eyes on the real thing. It is not an anchor, and
the pad path and the constant path agreeing afterwards is **not** corroboration — the nudge moves
both by the same amount.

## ⭐ The coordinate, which IS on the disc

In each park's own `Gates.sam`, beside the model:

| park | `EngineMapOffsetOverrideX` | `…OverrideY` | `EngineFootprintWidthOverride` | `…HeightOverride` | model z |
|---|---:|---:|---:|---:|---|
| JUNGLE | 45 | 16 | 6 | 3 | −18.50 .. −16.22 |
| FANTASY | 45 | 16 | 6 | 5 | −20.71 .. −16.70 |
| HALLOW | 45 | 16 | 6 | 3 | −18.60 .. −16.21 |
| SPACE | 45 | 15 | 6 | 4 | −19.00 .. −14.91 |

**The footprint is per-park data** — depth 3/5/3/4, offset Y 16/16/16/15 — so the four gates are
not interchangeable, and no single shared offset can place all four.

**And the model is authored exactly inside its own footprint**, four for four on both axes:

- x: offset 45 + width 6 = 45..51, and every model measures x 45.00..51.00 (Fantasy 45.36, its
  arch being asymmetric).
- z: the near edge is model z = −`MapOffsetY`, and each model's own maximum z — −16.22, −16.70,
  −16.21, −14.91 — sits just inside its offset of 16, 16, 16, 15. The depth is always less than
  H and more than H−1.

Also in the file: `Info.DontApplyOffset = 1`, `Info.Id` 1601/3601, `Info.HasQueue = 0`, and
`UsageInfo.ConstrainCamera = 1` — the string found while reverse-engineering the camera, which
turns out to live on the gate.

## ⚠ SUPERSEDED — the open question, answered below by the .sam rectangle

**What map are those offsets into?** They are not plot cells: Jungle's plot runs z −76..0, so cell
16 would be world −60, nowhere near the entrance. Until that frame is identified the coordinate is
named but not placed, and the shipped position stays the calibrated one.

## Anchors measured along the way, identical in all four parks

`A_ROAD` z −18.90..−5.50 · `ticket_booths` z −16.12..−14.88 · `Stumps` z −17.56..−13.93. Per-park
extras: Jungle's `BASIC_HOARDING_ENT` z −19.10..−14.90, Fantasy's `gatebase01` z −23.00..−19.00.
The entrance is one prefab translated in x. ⚠ The authored SKIP map does not record the gate —
it stands at the plot edge, where skipped only means outside the park.

## ⭐⭐ The gate's z is authored, and the .sam states its rectangle (2026-09-23)

`Gates.sam` ships once per world with `Info.EngineMapOffsetOverrideX/Y` and
`Info.EngineFootprintWidthOverride/HeightOverride`. Master, on what the footprint means: *"the
footprint height/width is the width of the no-build zone the gate creates around it"*. Measured
against the four meshes, that rectangle **is the gate's own authored bounding box**:

| world | gate authored x | gate authored z | .sam offset | .sam footprint | zone |
|---|---|---|---|---|---|
| JUNGLE | 45.00..51.00 | 17.00..18.50 | 45, 16 | 6 x 3 | x 45..51, z 16..19 |
| HALLOW | 45.00..51.00 | 17.00..18.60 | 45, 16 | 6 x 3 | x 45..51, z 16..19 |
| SPACE | 45.00..51.00 | 17.00..19.00 | 45, 15 | 6 x 4 | x 45..51, z 15..19 |
| FANTASY | 45.36..51.00 | 16.70..20.71 | 45, 16 | 6 x 5 | x 45..51, z 16..21 |

⭐ The x is **exact in all four**. The z is contained in all four, with the zone's height
varying per world by exactly enough to hold that world's arch — 5 for Fantasy's 4.01-deep worm,
3 for Jungle's 1.50-deep arch. And the zone spans the gap between `ticket_booths` (z 14.88..16.12)
and the end of `A_ROAD` (z 18.90), both of which are identical in every park.

**So the gate takes this park's x shift and NOTHING in z.** The shift is `ticket_booths` centre
minus 48: −18 JUNGLE, −8 FANTASY, 0 HALLOW, 0 SPACE — whole cells, and the two zeroes are a
free control: if the authored position is the real one, those two parks must land correctly with
nothing moved at all.

⚠⚠ **`gatebase01` is not the gate's base.** Fantasy's pad is x 37..43, z 19..23. Its x matches
the shifted gate exactly, which is what made it look like one — but z 19 is `zEnd`, the park's
first row, and the entrance's own starting path runs x 39..40, z 19..24 straight over it (see
findings/paths.md). It is the paving under the path INSIDE the park. Centring the gate on it is
where the −2.29 bias came from, and it dragged every gate two tiles into the park.


## ⭐⭐ The bus stop's flags are built by the engine, not modelled (2026-09-23)

Master: *"there are flags on the poles. use a sine. check data again."* They are not a mesh, which
is why searching every model on the disc for one named like a flag found only the go-karts'
`Newflag` and the race sideshow's `startflag`. ⚠ That search had no control and a miss was
reported as an answer; it was wrong.

**`0x220FA0(obj, float x, float y, float z)`** allocates `528` bytes — `16 + 8 * 64` — writes a
count of `8`, and constructs eight 64-byte objects (`0x22EDB0`, stride 64). Each goes to
**`0x22EEB8(obj, verts, verts2, texture)`** at an offset from the base:

| | z + 0 | z + 1.2 |
|---|---|---|
| **x + 0** | flag 0 | flag 1 |
| **x + 4** | flag 2 | flag 3 |
| **x + 10** | flag 4 | flag 5 |
| **x + 14** | flag 6 | flag 7 |

then each is registered into the render list at `0x310D48` by `0x226040`.

The **texture** is loaded at `0x221108` into `DAT_002F07CC` from `data/generic/weather/` —
**`logo.ssh`**, or `logoam.ssh` when the region flag at `0x2210E8` is set (`logojp.ssh` ships too).
It is 128x64, the Theme Park World wordmark, and the same call loads `raindrop.ssh`,
`snowflake.ssh` and `justwater.ssh` — so a flag is an engine effect in the rain's bucket, not
scenery.

The **base** is hard-coded per park at `0x149A70..0x149BC0`: `y = 2.2`, `z = 5.875`, and
`x ∈ {23.125, 29.125, 31.125, 33.125, 37.125, 41.125}`.

### The mesh confirms every number

Clustering the tall vertices of `A_POLES & BOLLARDS` (`y > 0.6 * top`, grouped by x AND z) gives
**eight** poles per park, not four:

```
x 23.135  27.135  33.135  37.135     offsets +0, +4.00, +10.00, +14.00
z  5.872   7.077                     the code's z + 0 and z + 1.2 = 5.875, 7.075
top y 2.37                           the code anchors at 2.2, just under the finial
```

and across all eight parks the measured first-pole x is
`{23.13, 23.13, 33.13, 31.13, 41.13, 37.13, 41.13, 29.13}` — exactly the six values the
executable lists, with nothing left over on either side. A model in a WAD and float immediates in
an ELF are two sources that know nothing about each other, and they agree eight times.

⭐ So `EntranceFlags` MEASURES the anchors off the poles and keeps the executable's rule as a
check it prints when the two disagree — no six-value table to go stale.

⚠ **Not read:** which way a flag flies and how far. `0x22EEB8` is handed two corners,
`(0.05, 0, 0)` and `(1.08, 0, 0.72)`, and the setup turns by `-pi/2` (`0x16F2D0`) — about 1.03
long and 0.72 tall — but the axis of that turn is not established. And **the sine is master's
instruction, not a decode**: no `sinf` call reaches these objects on the EE, so the wave is on the
VU or inside the draw, and neither has been read.
