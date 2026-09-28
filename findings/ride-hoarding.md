# Ride hoarding: the construction fence round a broken ride (PS2)

The research is agent `hoard`'s (2026-09-28, `~/ghidra_tpw/notes/hoarding.md`, SLES_500.32); this file is its
mechanism as the port now builds it, plus what implementing it re-read and corrected. strawberry, who plays the
console: "broken down rides dont get the construction fences build around them". Now they do.

**READ** = seen in MIPS, or a decompile checked against MIPS, or ELF data. **INFERRED** = reasoned to. Code:
`core/TPW.PS2.Data/RideHoarding.cs` (grid, geometry, state), `RideService.cs` (the raise/lower calls),
`game/Viewer.Hoarding.cs` (the view). Checks: `ParkSimAudit --hoarding-only` (`HoardingChecks.cs`), and
`game/tests/MechanicSmoke.cs`.

## 1. What it is

TPHoarding.cpp, code `0x1f3c70..0x1f5f28`: a **procedural mesh** of upright, double-sided textured quads, one per
fence edge, built once per ride model from the `.sam`'s `Info.Hoarding` block. Not a sprite, not a model on the
disc, and **not the "floating status icon"** that `staff-mechanics-guards.md` §1.3 once inferred for
`0x1f5948`/`0x1f5638` -- those two functions are this fence's texture switch and animation. (READ)

## 2. When it rises and drops

| event | native path | fence |
|---|---|---|
| broken (status 4/5), every update | breakdown checks `0x116d68` / `0x1228d0` / `0x1ea1c0` / `0x200358` → `0x118568(ride, 1)` | raise, bits 2, **Hoarding.ssh** |
| mechanic arrives to repair | `0x1785f8` → `0x118568(ride, 1)` | raise, bits 2 |
| mechanic arrives to install an upgrade | `0x178a38` → `0x118568(ride, 2)` | raise, bits 8, **Upgrade.ssh** |
| condemned (Life 0, flag clear) | `0x1169c0` → `0x118568(ride, 4)` | raise, bits 4, **Condemn.ssh**; nothing ever lowers it |
| job finished | `0x1786d0` → `0x118678(ride)` | lower |
| lowering reaches 0 | `0x1f5c10` → `0x1f56b0(inst, 0)` | hidden, texture back to **Closed.ssh** |

`0x118568` maps kind → bits in precedence order: `kind&2` → 8, else `kind&4` → 4, else `kind&1` → 2, then
`0x1fa690(h, bits)` = `0x1f5948(inst, bits)` and VAR_BREAKSTAT := 1; `0x118678` → `0x1fa700(h)` = `0x1f5ab0(inst)`
and VAR_BREAKSTAT := 0. Both are gated on the model instance `h = (ride+8)->+0x14 ≠ 0`, not on a script (READ,
MIPS `0x1185c0..0x118644`, `0x1186a0..0x1186c0`). Nothing else reaches the fence: not construction, placement,
closing, or REPAIREFFECT (research census). Shops, sideshows and features build one and never raise it, because
only rides reach the service. Nothing of it is saved.

## 3. `Info.Hoarding`

Parsed by `0x112fa0` against the table `0x2ac268` (`{u32 char, u32 value, u32 mask}`, 16 entries, value 0 in
all): spaces skipped, an unknown character fails the map ("Illegal map character"), more than 20 cells or rows
fails ("Map too wide"/"Map too high"), the width is the longest row, and **the rows are reversed** after parsing,
so the text's top line is z = h−1 (READ). Each character is an edge mask:

`.` 0x00 · `^` 0x01 (+z) · `]` 0x04 (+x) · `_` 0x10 (−z) · `[` 0x40 (−x) · `F` 0x41 · `7` 0x05 · `L` 0x50 ·
`J` 0x14 · `n` 0x45 · `=` 0x11 · `H` 0x44 · `C` 0x51 · `U` 0x54 · `3` 0x15 · `O` 0x55 (READ, ELF)

Measured by the audit over all 321 `.sam`: **163 blocks**, all parse, 163/163 the size of their `Info.Shape`,
**1940 edges**, every fence edge on the Shape's perimeter, and exactly one unfenced perimeter edge on each
block's entrance cell `2` (163) -- the queue's way in. Panels per block 3:4, 7:16, 8:2, 9:7, 10:20, 11:50,
12:4, 13:1, 14:27, 16:14, 18:18. All of these agree with the research's census, which was taken with a
different program (its scratch emulator) over the same decode.

## 4. Geometry (`0x1f3dd0`)

- **Frame.** The ride's model space: 1 unit = 1 cell, footprint corner at the origin, y 0 at the base. The fence
  model is chained to the ride's (`0x1f6230` links `+0x98`) with an identity root (`0x170380`), so it moves and
  turns with the ride's instance matrix and not with the root mesh's own 0.1-scale matrix (READ draw path).
- **Gate.** Built only if the model's root (header `+0x70`) exists, has node flag 0x40 clear and ≤ 512 vertices
  (READ). Over the disc: 155 of 155 resolvable models pass; 8 blocks have no model the port resolves.
- **Quads.** Cells row-major, bits 1, 4, 0x10, 0x40; corners `c0 (x,z+1)`, `c1 (x+1,z+1)`, `c2 (x+1,z)`,
  `c3 (x,z)` each pulled by an offset: the perpendicular offset always, the along-edge offset only where the
  adjacent edge is also set. Offsets come from `0x1f39f8`: the vertex whose xz distance to the corner is the
  second smallest, skipping any within 0.001 (doubles `0x36a940`/`0x36a948`) of the nearest; `o = 0.5 (v − c)`,
  0 if none (READ). Build UVs v0 (0,1), v1 (1,1), v2 (0,0), v3 (1,0); normals ±x/±z. Audit: 1940 panels,
  lengths median 0.950, p10 0.900, p90 1.000, 0.400..1.500, 790 exactly 1.0; 256 of 5280 corners take 0.
- ⚠ **Correction to the research: the 1×1 table.** `0x1f39b8` stores +0.3 at `+0x10` and `+0x14` (MIPS
  `0x1f39d4..0x1f39f4`), so `o2 = (+0.3, +0.3)`, not the "(−0.3, +0.3), 0.3 inward on both axes" of
  hoarding.md §4. Corner `c2` is pushed *out* in x and a 1×1's +x panel runs diagonally from (1.3, 0) to
  (0.7, 0.7). The port keeps the executable's value. Only the four 1×1 Small Toilets use the table and a
  feature never raises its fence, so no console player has seen it.
- **Order (`0x1f3c70`).** `key = fmod(atan2f(mz − h/2, mx − w/2) + π + π/4, 2π)` over the edge midpoints,
  stable merge sort (`0x1a6370`), slot = rank. Key 0 points at (−x, +z): panels run from the text's top-left
  down the left, along the bottom, up the right and back along the top.
- ⚠ **The order depends on the EE's rounding, for one panel in 33 blocks.** `atan2f` is newlib's (`0x28cef8` →
  `0x28f198`, `atanf` `0x292968`, tables at `0x37ce78`) and it and the two adds run on the EE's FPU, which
  rounds **toward zero** (INFERRED from the EE's documented FPU mode as emulators run it; not measured on this
  disc). An edge midpoint exactly on the up-left diagonal of the block's centre -- the top-left +z panel of a
  4×3, for instance -- gets `atan2f = 0x4016CBE4` either way, but `+ π + π/4` truncates to `0x40C90FDA`, one
  ulp below 2π: key 6.2831850, so it rises **last**. Rounded to nearest it would be exactly 2π, key 0, and
  first. The audit counts it: host rounding orders 33 of 163 blocks differently and in every one the only
  change is that one panel moving from last to first (2×3:3, 3×2:5, 3×4:3, 4×3:7, 4×5:5, 5×4:9, 6×3:1; Big
  Dripper is a 4×3). The port evaluates the key with newlib's code and round-toward-zero arithmetic. One
  console look at a 4×3 fence rising (does its top-left panel come up first or last?) settles it.

## 5. Animation (`0x1f5948`, `0x1f5ab0`, `0x1f5c10`, `0x1f5750`)

- Per model instance: flags `inst+4` (2 built, 0x20 visible, 0x40 raising, 0x80 lowering, kind 0x100/0x200/
  0x400/0x800), progress `+0x24` (0..1), rate `+0x28` (READ).
- **Raise** `0x1f5948(bits)`: Closed only when lowering or no kind; Hoarding only if the kind is neither Hoarding
  nor Condemn (so a broken, condemned ride keeps Condemn); Condemn unless Condemn; Upgrade unless Upgrade (so
  Upgrade replaces Condemn); then rate +0.2 (`0x3e4ccccd`), draw-enabled if hidden, flags `| 0x60 & ~0x80`.
  **Progress is not reset**: a fence caught half-way down rises again from there. **Lower** `0x1f5ab0`: rate
  −0.3; only a visible fence starts lowering (READ, MIPS).
- **Tick** `0x1f5c10(dt)` while visible and raising or lowering: `p = clamp(p + rate·dt, 0, 1)`; if p moved,
  reshape; if not, lowering at 0 hides (texture Closed, kind 0x100) and raising at 1 stops, each one tick after
  the end is reached. `dt` is pausable wall-clock seconds (`0x1a8ae8` over the gated clock `0x147158`).
  **5.0 s up, 3.33 s down.**
- **Reshape** `0x1f5750`: `f = clamp(1 − (i/N·0.8 − (p − 0.25))·4, 0, 1) × (i odd ? 0.8 : 1)`; the top
  vertices sit at y = f, the bottom UVs become v = 1 − f, the top UVs stay at 1 (READ, MIPS
  `0x1f57b0..0x1f58d8`). Panel i starts at p = 0.8 i/N and is up 0.25 later; even slots stand 1.0 cell high,
  odd ones 0.8; for N > 16 the last slot peaks below 1 (N = 18: 0.978, ×0.8 on its odd slot).
- Drawn this way the panels rise **out of the ground** carrying their top edge. With the port's model-UV
  convention the Upgrade texture's arrow points UP on screen (MechanicSmoke's `hoard_upgrade` shot), though it
  points down in the decoded image -- the research's prediction that the hoarding shares the `.mps` UV
  convention. Condemn's skull is stored upside down the same way, so it should stand upright; not seen on screen.

## 6. Textures

`data\generic\MiscMesh\textures\` (`0x36a918`), one global set built by the static ctor `0x1f5d20`: k 0
`Closed.ssh`, 1 `Hoarding.ssh`, 2 `Condemn.ssh`, 3 `Upgrade.ssh`, each 64×64 and opaque; the kind→k mapping is
READ (`0x1f5638`), "handle k is list entry k" INFERRED. Closed is never on screen: it becomes current only as
the fence hides. Don't confuse these with each world WAD's `/hoarding/textures/`, which are the park's boundary
fence.

## 7. What the port adapts

- ⚠ `ParkSim.HoardingRaise`/`HoardingLower` fire for every ride `Service`/`ClearService` reaches; the port's
  existing `r.Machine != null` test keeps gating only the script half (VAR_BREAKSTAT and the condemned sound),
  unchanged. The state is the view's, one per placed model, as the console keeps it on the instance.
- ⚠ The fence is built at placement for every placed thing with a block, under `AnimatedModel.Root` (the node
  `Park.TryPlace` turns and places), and freed with it; the MechanicSmoke measures it in the world on every park.
- ⚠ Drawn with the viewer's PS2 model shader, `cull_disabled` for the two windings; the model's render flags 6
  are untraced.
- ⚠ The mesh is always built at the current progress. Whether the console can draw the as-built full-height
  fence for one frame before its first tick (instance-tick order, research §9) is untraced, and the port
  never draws it.
- ⚠ One tick per rendered frame after the park step, while `ParkSimulationRunning` (the H pause holds it); the
  console's per-frame callback order is untraced. `p + rate·dt` uses the host's rounding.
- ⚠ The distances in `0x1f39f8` use the host's float sqrt; nothing on the disc sits on a 0.001 boundary for
  that to move.

## 8. Still open

- A console image of a fence: the corner offsets (Mole Whack's chamfer) and the 4×3 first-or-last panel.
- The loader that fills `model+0x3c` (texture handle order), render flags 6, destruction, and the order of the
  instance tick against the ride update (research §9).
