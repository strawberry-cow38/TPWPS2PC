# The PS2 sea

> **2026-10-02 correction: a native moving park-water layer has now been found.**
> See `ocean-waves.md`: the park initializer registers procedural drawable `0x22CB90`,
> which generates changing heights/UVs and loads global alpha texture `justwater.ssh`.
> It overlaps the opaque `A_SEA` meshes. The absence of animation on those meshes did
> not exclude this separate draw path. The historical negative conclusions below
> (including "the only waves are a sound" and "could not animate in principle") are
> superseded, not implementation instructions. Composed sea world Y is approximately
> −1.1..−1.0; the local coordinates below are not its final rendered heights.

Master, 2026-10-01: *"kill all the sea stuff we have rn and reimplement it properly, assume
everything previous was wrong"*, then *"okay then research the sea from the ps2 version"*.

**Answer, for the layer this document measured: `A_SEA_01..06` is a static, opaque, un-animated
textured sheet. It does not morph and it does not scroll.**

⚠⚠ **BUT THAT IS NOT THE WHOLE SEA, AND THIS DOCUMENT'S FIRST HEADLINE WAS TOO BROAD.** On
2026-10-02 master, who has the PS2 running under an emulator, reported: *"theres a stationary opaque
layer (which we have rn) and a sine translucent layer above it"*, later softened to *"maybe not
specifically sine. but some kind of wave"*.

So there are **TWO** layers and this document only ever found one. The stationary opaque layer
below IS `A_SEA` and master confirms it matches what the port ships — that half stands. **A second,
translucent, moving layer exists and has not been found.** Everything below about `A_SEA` remains
measured and true; it simply does not describe the upper layer. The hunt for it is astraclaw's as
of 2026-10-02. What has been eliminated is in "The upper layer: what it is NOT" at the end.

Everything below is measured off the disc. Where something is inferred it says so.

## What the sea IS

`A_SEA_01..06`, six meshes, in **both** `terrain_1.mps` and `terrain_2.mps` of **all four worlds**
— 48 instances. Their world-space geometry is **byte-identical in all eight terrain files** (one
SHA per mesh name, 8 occurrences each), so it is one shared asset sitting in one place in every
world. Vertex counts 75 / 91 / 181 / 78 / 84 / 174.

One material on every instance: **`jri_lak2.ssh`**, 64x64, **24bpp with no alpha** — opaque.
Its material flag word is `0xa0`, *identical to plain ground* (`grd_pav1`). For contrast the river's
`wr_water3` is 32bpp **with** alpha and flag `0x3a0`. The sea is the opaque one; the river is not.

⚠ `mesh+0x50` is the mesh ORDINAL, not a material index — reading materials through it makes the
sea come out wearing `grd_top1`. Materials here are resolved with `m3d2.groups`.

## Its shape: one stepped surface, NOT two planes

421 triangles. **185 of them (43.9%) are exactly level** (dY < 1e-4); every remaining triangle is
within **4.2 degrees** of level, p90 = 0.64 degrees. There is **not one vertical triangle** — no
skirt, no side wall.

Two canonical heights, exactly 1.0 apart:

| Y | vertices (all 8 files) |
|---|---|
| **-1.2666** | 1752 |
| -1.7666 | 480 |
| **-2.2666** | 1752 |

⭐⭐ **They are not stacked.** The equal counts invite that reading and it is wrong: the upper sheet
occupies **95 distinct XZ sites**, the lower **90**, and they share **ZERO**. It is one continuous
surface that sits at -1.2666 over some ground and -2.2666 over other ground, joined by ramps so
gentle no triangle exceeds 4.2 degrees.

⚠ The port's old claim that the six meshes "tile at one height" came from their AABB **centres**,
which are equal because each mesh carries a similar mix of both heights. The vertices disagree with
the centroids. Measure the thing, not its average.

## Its animation: none, with a positive control

Three channels could move it. All three are absent on all 48 instances:

| channel | sea | control in the SAME file |
|---|---|---|
| APS track flag `0x10000` (per-vertex UV keys) | absent | **11 tracks** present |
| `mesh+0x9c` UV fan-out run list | `0` | **11 lists** present |
| `mesh+0x98` morph run list | `0` | — |

The control is what makes the negative mean anything. In `JUNGLE/terrain_1`, exactly eleven meshes
carry both a `0x10000` track and a `+0x9c` list: `surface13/15/22/23/25/27`, `Surface15b`,
`falls02`, `Object12`, `RIVERBED_03B/04B` — the river, the ponds and the waterfall, wearing
`wr_water3` / `dk_water3`. The instrument finds UV animation exactly where it exists. It finds none
on the sea. (`terrain_2` is the same picture: 8 tracks, none on the sea.)

⭐⭐ And **only JUNGLE ships a terrain `.aps` at all.** HALLOW, FANTASY and SPACE ship `terrain_*.mps`
with no animation file beside it. In three of the four worlds the sea could not animate even in
principle.

## `fScrollRate` is a coaster field, and never applied to terrain

This was the last candidate and it is dead two ways over.

**From the executable's reflection schema** (`SLES_500.32`, vaddr = file offset + `0xFF000`), the
0x3c-stride run reads:

```
0x2acf20  pcTextureFilename
0x2acf5c  bIsSelfIlluminating
0x2acf98  fScrollRate
0x2acfd4  asTextureData            <- the array those three describe
0x2ad04c  fX fY fU fNx fNy
0x2ad178  asCrossSectionPoints1
0x2ad31c  asCrossSectionPoints2    ... 
```

A run of field descriptors is terminated by the array that owns them, and `asTextureData` shares a
field table with `asCrossSectionPoints1..12` — the **coaster / track** class.

**From the shipped data**, which settles it. The authored files are **plain text** and 327 of them
ship (`.sam` in the world WADs, `.dba` in DATA.WAD). `fScrollRate` occurs in **12 of 327**, and
every one of the twelve is a `coaster.sam` under `/Rides/`. The complete census of every scroll rate
on the disc — 13 of them:

| world | ride | texture | rate |
|---|---|---|---|
| FANTASY | b_drip (log flume) | Paint_flow.tga | 1.2 |
| FANTASY | b_drip | Ratchet.tga | 0.8 |
| FANTASY | candy_c | chain.tga | 1.0 |
| HALLOW | c_hade (log flume) | slime2.tga | 0.8 |
| HALLOW | c_hade | slime_up.tga | 0.8 |
| HALLOW | c_scat | chain.tga | 1.0 |
| HALLOW | coasta | chain.tga | 1.0 |
| JUNGLE | Coaster1 (water flume) | water2.tga | 1.2 |
| JUNGLE | Coaster1 | trak_sec2.tga | 0.8 |
| JUNGLE | Coaster1 | water2.tga | 0.5 |
| JUNGLE | MineCart | chain.tga | 1.0 |
| SPACE | megacost | ss_post.tga | 1.0 |
| SPACE | moonshot | rail_tube.tga | 1.0 |

Lift chains, flume water, slime and rails. **No terrain texture appears in any of them, and
`jri_lak2` appears in no `.sam` on the disc.**

## The executable has no sea at all

A sweep of every symbol in `SLES_500.32` matching scroll / wave / tide / sea / ocean / ripple /
water returns, in full: `fScrollRate`, `TurnOffScrollingTextures` (the switch for the above),
`wateride` / `watertun` (the water ride), `ripples` (a name sitting among the **lobby** strings
`data\lobby\`, `base`, `jungle2`… — and matching no asset in any WAD), and two **sound events**:

> ⭐ **`EVT_WAVES`** (`0x360226`) and **`EVT_SEAGULL`** (`0x360236`)

There is no sea level, no wave height, no water simulation symbol of any kind. The PS2's sea is
heard, not animated.

## What this means for the port

- The sea should be **static and opaque**, with no scroll and no vertex motion. Both the sine and
  the scroll this port has shipped were ours.
- The two heights are real; the "two stacked planes" were not.
- `EVT_WAVES` / `EVT_SEAGULL` are the sea's actual contribution and we do not play them.
- ⭐ The river is the opposite case: it carries **11 real UV tracks** we currently approximate with
  a guessed 90-degree scroll. Replaying them is a real improvement and is not done here.

⚠ **What is not measured:** nobody has watched a PS2 render its sea. This is the disc's data and the
executable's symbols, and they contain no mechanism by which the sea could move. The PSX is a
different build and *does* roll its sea texture one row per frame (VRAM diff of a running park) — so
if the PS2 sea ever does visibly move, that is a capture to bring back, not a number to guess.


# The upper layer: what it is NOT (2026-10-02)

Master observes a translucent moving layer above `A_SEA`. These are the places it is **not**, each
with the control that makes the negative mean something. None of this finds it; it narrows it.

- **Not shipped geometry.** Every `.mps` in JUNGLE.WAD *and* DATA.WAD was scanned for a mesh over
  the sea footprint. The only things there are land, bushes, trees and hoardings. `Sky/` is three
  textures and no mesh. So the layer is **built at runtime**.
- **Not the VU1 renderer.** All 816 instructions disassemble. Its entire float immediate pool is
  **{0.125, 128, 255}** (lighting scale and clamp), and **every load is a constant offset or a
  sequential post-increment — there is not one computed-index load**. A table lookup is therefore
  impossible in it, and position goes straight into the matrix multiply.
- **Not a second copy in EE RAM.** A PCSX2 savestate holds exactly **one** copy of the `A_SEA`
  vertices, byte-identical to the disc. Searching all 32 MB for a working copy with matching X/Z
  and a free Y finds nothing. ⚠ Controls were static meshes, so this test cannot see a displacement
  that is built straight into a DMA packet — and the river, which demonstrably animates, also reads
  byte-identical, so the instrument is partly blind. It is not strong evidence on its own.
- **Not any user of the engine's sine tables.** All **14** readers censused: cosine/sine pairs
  0x40 apart (rotation matrices), one SLERP, object bob and drift (`0x1ba7f0`, `0x1bb888`), and a
  2D sprite wave (`0x13ffa8`). **None builds a sheet.** The census found real wave users, so the
  instrument works.

⚠⚠ **THE TRAP THAT COST AN HOUR, WRITTEN DOWN SO NOBODY REPEATS IT: the sine tables are NOT IN THE
ELF IMAGE.** `FUN_001a6630` builds them into `.bss` at startup — 4096/turn at `0x2e31b0` and
256/turn at `0x33cc60`, with the radians->index scale at `0x2e31a8` and the mask at `0x2e31a4`.
Grepping the file for a sine table returns **zero** and reads exactly like "this engine has no sine".
Use the savestate RAM. See `feedback_image_is_not_authority_for_runtime_globals`.

⭐ **Untested idea, the one worth trying next:** it need not be a sine at all. A precomputed or
triangle wave, or the terrain path drawing `A_SEA` a **second time** with alpha and a UV offset,
would be invisible to every check above — the drawn-twice case especially, because it adds no
geometry, no new material and no table lookup.
