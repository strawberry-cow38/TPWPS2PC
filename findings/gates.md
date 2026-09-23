# The park entrance gate — placed, and its coordinate NAMED BUT NOT PLACED

## ⚠ UNFINISHED, at master's call

The gate renders in all four parks, at a position **calibrated by eye, not read from the disc**.
The real coordinate has been found and is written down below; what is missing is the frame it is
expressed in. Picked up again, start at "the open question".

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

## ⚠ The open question

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
