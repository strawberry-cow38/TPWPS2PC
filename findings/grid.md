# One model unit is one footprint cell

**Measured 2026-09-21.** Not a fitted constant — an identity, to float precision.

## The result

Compose each mesh's transform up its **parent chain** (`Model.WorldTransforms`: parent offset at
node`+4`, helper nodes from `U32(0x4C)` in 0x60 strides while the top bit of `U32(o)` is set, local
applied before parent) and measure the XZ extent of every batch vertex. Divide by the footprint
cell count from the `.sam`'s `Info.Shape`:

| model | footprint | composed extent |
|---|---|---|
| `1x1east` | 1x1 | 1.000000 x 1.000000 |
| `2x2rck` | 2x2 | 2.000000 x 2.000000 |
| `4x4rock` | 4x4 | 4.000012 x 4.000004 |
| `5x5rck` | 5x5 | 5.000009 x 5.000575 |
| `monkey` (Crazy Ape) | 4x4 | 4.000000 x 4.000000 |
| `bigpalm` (Large Tree) | 2x2 | 2.000000 x 2.000000 |

Over **all 562 extents from 281 rides**: median exactly **1.0000** units per cell, p25 and p75 also
exactly 1.0000, and **479 of 562 within 1% of 1.000**.

So the grid cell size is not a property of the data to be recovered. **A ride's footprint in cells
IS its extent in model units**, and whatever a park scales by is a free choice made by the renderer.

The 83 extents that are not ~1.0 (p05 0.73, p95 2.0, max 7.4) are models that genuinely overhang or
under-fill their declared plot — coasters, signage, props — which is a real property of the art.

## ⚠ Why this needed a third measurement

Two of us measured this independently and **agreed on a wrong answer**, because we had made the
same shortcut: taking each mesh's OWN matrix instead of composing the chain. Own-matrix extents run
about 10x the composed ones, which produced:

* 14.42 units per cell from one reading (later found to be also missing the matrix entirely, giving
  ~1.3x on top of that)
* 9.82 from mine, "confirmed" by a containment test that then chose **10** — a round number, a
  plausible designer's choice, and reached through a shared fault

⭐⭐ **Agreement between two measurements is corroboration only if the two are independent.** Ours
were not: same tool, same convention, same omission. The number that broke it was not a better
statistic, it was a different transform. See the same trap in `rse.md` (a coherent analysis over
1.1% of a corpus) and `lip.md` (a true sentence about WADs read as a claim about the disc).

The tell was available before the fix, too: a park built on a 10-unit cell rendered a 2x2 tree at
**10% x 10%** of its plot. A viewer that frames each model on its own can never show that, because
the camera simply pulls in — the error only becomes visible once models stand on a shared thing.

## Where the factor of ten lives: a 0.1 root scale, on almost everything

The composed extents come out at exactly one tenth of the own-matrix ones because **the root node
of almost every model carries a uniform 0.1 scale**. Measured over every `.mps` on the disc:

| root basis scale | roots | what they are |
|---|---:|---|
| **exactly 0.100** | **464 of 496** | rides, props, scenery |
| ~0.00003 | 24 | **characters** — `/Chars/*`, roots named `Bip01`, `pickup`, `neck pivot box` |
| ~0.004 | 7 | |
| ~0.02 | 1 | |

`monkey.mps` root `m_base` is 0.1000/0.1000/0.1000; `4x4rock` and `bigpalm` roots likewise. So the
tenth is an authoring convention across the fleet, and it is the second, independent route to the
same constant as the units-per-cell identity above.

⚠ **Rides are authored at 0.1 and skinned characters at ~3e-5 — about 3,000x apart.** Any code that
assumes one root convention is wrong by three orders of magnitude the moment a guest stands next to
a ride, and at that size it does not render as "slightly off", it renders as nothing visible.

⚠ And a caution about how that was nearly mis-reported: a histogram rounded to three places printed
those 24 as **`0.000`**, which reads as degenerate matrices, and the first draft of this section
said so. At full precision they are a different convention, not a broken one. `0.000` in a rounded
table is not a measurement of zero.
