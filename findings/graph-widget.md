# The graph widget, and the three screens that use it

Research only (2026-09-28). Target: `main_fi_financestats.sce` (menu 21), `main_fi_overallstats.sce`
(menu 22), `main_ps_statistics.sce` (menu 24) and the `graph` / `YearSelect*` / `Items` /
`GraphText` / `GraphValue` / `GraphLegend` elements nothing else in the laptop authors. Every
address is a native EE vaddr in `SLES_500.32` (vaddr = file offset + 0xFF000). Decompiles are in
the shared Ghidra cache on the 4080; the plotter and the two page preps were also read from raw
disassembly because the Ghidra project was locked by a teammate at the time, and the two agree.

## TL;DR

- **The graph is a stepped-area drawn with axis-aligned rectangles, not a polyline.** For every
  pair of neighbouring points the plotter (`FUN_0013f560`) emits ONE filled rectangle from the
  baseline up to the LOWER of the two values. The only primitive it uses (`FUN_00213838` →
  `FUN_00213980`) writes a left/top/right/bottom rect and emits a flat-colour sprite — it has no
  way to draw a slope.
- **It is drawn twice**: once in the series colour at `Z−1`, once 3 px lower in `(0,56,51)` at
  `Z`. ⭐⭐ **The colour pass is in front — read, not inferred (§1.8)**: the GS depth test is
  `ALWAYS` with Z writes masked in every draw environment the game builds, and the 2D sprites
  are ordered by a CPU sort that emits larger z first. So a series reads as a **filled stepped
  area in the series colour**, with a 3-px dark strip showing only below the baseline. (My first
  draft called the dark pass an eraser and the result a 3-px line; that was the inference the
  read overturned.)
- **24 points, always.** A series is 24 buckets over the selected span; the x step is `w/24`
  (16.16 fixed point), the last 1/24 of the box stays empty. Young parks fill from the LEFT
  (oldest at x=0, newest right) and plot only `min(24, 24·months/span)` points.
- **Y scaling**: `y = h − h·(v − min)/range − 8`, `range = max − min − 8`, clamped to `[0,h]`,
  where `max` is the series' own headroom max (peak × 1.1, park stats: ×1.15 / fixed 110/120/120).
  `min` is always 0 on these three screens.
- **The widget itself draws no axes and no gridlines.** It sizes each series to `(w, h−10)`, calls
  its draw, then prints the year numbers `1..years−1` in **blue (0,0,255)** along the bottom —
  only when the span is more than one year. A nine-slice panel (fill + corners + edges,
  `FUN_00142090`) is issued over the plot rect by the series draw.
- **`YearSelect` selects the span**: a discrete selector over **{1, 2, 6, 12} years**, wrapping,
  initial 1; the label is `STR_FINANCE_YEARS` "Years", the value is the bare number (not the
  `STR_SELECT_*_YEARS` strings, which these screens never use). Changing it rebuilds the buckets.
- **`GraphLegend` is bound and never read.** Nothing draws a colour legend. The blue year ticks
  and the `Items` list are all the "legend" there is.
- **Only one series shows at a time** on all three screens: selecting an item clears every
  toggle then flips that one. The first item is on by default.
- ⚠ **Retail quirk, verified at instruction level**: the park-stats bucket builder
  (`FUN_00186d38`) OVERWRITES the bucket with each month's value and then divides by the month
  count; the finance builder (`FUN_001363a8`) accumulates properly. Port it bugs-and-all or not —
  it is a decision, and this is the evidence.

## 1. The widget

### 1.1 Classes and where they live

| thing | ctor | draw | size | notes |
|---|---|---|---|---|
| graph widget | `FUN_0013f168` (vtable `0x35f0a8`: slot1 dtor `FUN_0013fa88`, slot2 draw `FUN_0013f1c8`) | `FUN_0013f1c8` | 0x74 | base `FUN_00141ea0`; fields below |
| series | `FUN_0013f370` (vtable `0x35f0c8`: slot1 dtor `FUN_0013f9e8`, slot2 draw `FUN_0013f3b8`) | `FUN_0013f3b8` → `FUN_0013f560` ×2 | 0x68 | base `FUN_00141d48`; sub-object at `+0x14` (`FUN_00144510`) |
| series setup (finance) | `FUN_00134df8(screen, series, data, colour*, skip)` | | | 5 args |
| series setup (park stats) | `FUN_001858a8(screen, series, data, colour*, skip, signed)` | | | 6 args; `signed` is always 0 from `FUN_00185b58` |
| rect primitive | `FUN_00213838` + `FUN_002138e0` (colour) + `FUN_00213980` (emit) | | | prim handle `FUN_002156a0(FUN_00214b28(), 0x39)` = sprite 57, the flat-colour sprite |
| integer sqrt | `FUN_00206b38` | | | 16-iteration shift-subtract |

⚠ The widget inventory is in the CONSTRUCTOR (as the laptop-screens doc warned). The finance
family ctor `FUN_00134188` builds **one widget at `+0x58c`** and **7 series** (2 at `+0x600`,
`+0x668` for the overall page; 5 at `+0x6d0..+0x870` for the finance page); the park-stats ctor
`FUN_00184618` builds one widget at `+0x6e0` and **5 series** at `+0x7d4..+0x974`. Every page draw
copies the `.sce` graph frame into the widget and calls `FUN_0013f1c8` on it directly (callers:
`0x1354e0`, `0x135920`, `0x1860f4`).

**Widget fields** (`this` = widget):

| off | set by | meaning |
|---|---|---|
| `+0x08/+0x0a` (s16) | page draw, from the `.sce` `graph` frame | x = col, y = row |
| `+0x0c` (s16) | base | z offset (0) |
| `+0x14/+0x16` (s16) | page draw | w, h (147 × 200 on all three) |
| `+0x18` | ctor = 2 | element kind (unused: nobody calls `FUN_00141f68` on the widget) |
| `+0x3c` | ctor = `&DAT_002b6718` | a resource pointer; unread by the draw |
| `+0x4c` | page draw = the selected span (ctor default 3) | **years** |
| `+0x50` | prep = 0 then ++ | number of registered series |
| `+0x54..` | prep | series pointers (the prep appends) |

**Series fields** (`this` = series), all written by the setup function unless noted:

| off | value | meaning |
|---|---|---|
| `+0x44` | `&arr[1]` (`data + skip + 1` words) | the 24 samples; `arr[0]` is the max and is skipped |
| `+0x48` | 24 | x-step divisor: `xstep = (w << 16) / 24` |
| `+0x4c` | `n = min(24, (min(months, span) · 24) / span)`, `span = years·12`, `months = stats→+0x1c` | points actually plotted |
| `+0x50/+0x54` | prep: widget w/h; **overwritten every frame by `FUN_0013f1c8` to `(w, h − 10)`** | plot width / height |
| `+0x58` | 0 (park stats `signed` mode would set `−arr[0]`, never used here) | min |
| `+0x5c` | `arr[0]` | max |
| `+0x60` | −8 (`0xfffffff8`) | offset added to the range AND to y |
| `+0x64` | `colour*` → 4 bytes `r,g,b,0` | series colour (0 → default `0x35f0e8` = `(0,56,51)`) |

### 1.2 `FUN_0013f1c8` — the widget draw, in order

Args `(this, px, py, z)`; `X = px + this→x`, `Y = py + this→y`, `Z = z + this→+0xc`.

1. `ctx = FUN_00141d18()` (the text context); `FUN_00138998(ctx)` resets it (scale 1, justify 1);
   `FUN_001388e8(ctx, 0, 0, 255)` — **text colour blue**.
2. `fp = (w << 16) / years` — the x distance of one year, 16.16.
3. For each registered series `s` (count `+0x50`): `s→w = w; s→h = h − 10;` then
   `s→vtable[2](s, X, Y, Z)` — the series draw at the graph origin.
4. **Only if `years > 1`**: for `k = 1 .. years−1`: `sprintf(buf, "%d", k)` (format at `0x35f0a0`),
   `FUN_00138798(ctx, buf, X + ((k·fp) >> 16) − 1, Y + h + 8, Z, 1)`.
   So the bottom is divided into `years` equal parts and the boundaries are numbered; the
   numbers sit 8 px below the graph rect. With 12 years that is 11 numbers 12 px apart.

That is the whole widget. No axes, no ticks other than those numbers, no box.

### 1.3 `FUN_0013f3b8` — the series draw

Args `(this, X, Y, Z)`.

1. `FUN_0013f560(this, X, Y,     Z, colour = this→+0x64 ? this→+0x64 : 0x35f0e8)`
2. `FUN_0013f560(this, X, Y + 3, Z, colour = 0x35f0f0)` — the same plot 3 px lower in `(0,56,51)`.
   Inside the plotter, `z' = (colour == 0x35f0f0) ? Z : Z − 1` — the colour pass is at `Z−1`, the
   dark pass at `Z`.
3. Builds a temporary element on the stack (`FUN_00141ea0`): rect `(X, Y, this→w, this→h)`
   (the series' own x/y are 0), kind `+0x18 = 1`, `+0x1c = 1`, `+0x38 |= 1`, `+0x48 = 1`, and calls
   `FUN_00141f68(tmp, 0, 0, Z)` → kind 1 → **`FUN_00142090`**: the nine-slice panel — sprite
   `0x30` tiled in 16-px rows over the rect (white tint), 8×8 corner pieces (sprite `0x2f`) at
   `(x−8,y−8) (x+w,y−8) (x−8,y+h) (x+w,y+h)` with flips, 8-px edge strips (sprite `0x32`, rotated
   for the sides). Then `FUN_00141f30(tmp, 2)` destroys it.
   `game/LobbyMessageBox.cs:153` already identifies `0x2F/0x30` as `UI.WAD/messages/Messcorner|
   Messedge|Messfill` registered by `FUN_00216028` (16×16). ⚠ `0x32` was not checked against
   that table; it may be a different edge piece. Note kind 2 (`FUN_00142610`) is the stub path
   the message-box note warns about; kind 1 is not stubs, every call above emits a sprite.

### 1.4 `FUN_0013f560` — the plotter, precisely

Args `(s, X, Y, Z, colour*)`. Returns at once unless `s→+0x48 > 1` and `s→+0x4c ≥ 2`.

```
xstep  = (s.w << 16) / 24                       // 16.16
range  = (s.max - s.min) + s.off                // off = -8  →  max - 8
if range < 1: range = 1
absX   = X + s.x ; absY = Y + s.y               // s.x = s.y = 0
zdraw  = (colour == 0x35f0f0) ? Z : Z - 1
prim   = sprite 57
for i in 0 .. n-2:                              // n = s.+0x4c
    x0 = absX + ((i     * xstep) >> 16)
    x1 = absX + (((i+1) * xstep) >> 16)
    v0 = data[i] ; v1 = data[i+1]               // data = s.+0x44 (= &arr[1])
    y0 = s.h - (s.h * (v0 - s.min)) / range + s.off      // integer division
    y1 = s.h - (s.h * (v1 - s.min)) / range + s.off
    y0 = min(y0, s.h) ; y1 = min(y1, s.h)
    y0 = absY + max(y0, 0) ; y1 = absY + max(y1, 0)
    dx = |x1 - x0| ; dy = |y1 - y0| ; len = isqrt(dx*dx + dy*dy)
    if len != 0:
        s1 = ((dx << 12) / len + 2048) >> 12    // round(dx/len)        ∈ {0,1}
        s0 = ((dy * 7000) / len + 2048) >> 12   // round(1.709·dy/len)  ∈ {0,1,2}
    else: s1 = dx ; s0 = dy                     // (both 0)
    bottom = absY + s.h
    if y1 < y0:   rect(x0 - s0, bottom, x1 + s0, y0 + s1)   // rising: top = the LOWER point (i)
    else:         rect(x0 + s0, bottom, x1 - s0, y1 + s1)   // flat/falling: top = point i+1
    colour(r, g, b) ; emit
```

`rect(a, b, c, d)` is `FUN_00213838(prim, a, b, _, _, _, _, c, d, zdraw)` — it reads only its 1st,
2nd, 8th, 9th and 10th arguments (`a1, a2, t3, sp[0], sp[8]`), writes
`left = a/256 − 1, top = 1 − b/256, right = c/256 − 1, bottom = 1 − d/256, z = zdraw/128` into the
GS packet, and `FUN_00213980` sorts left/right and top/bottom before emitting. The four ignored
arguments are the "other two corners" of what was evidently once a quad; they never reach the GS.

Consequences worth stating plainly:

- Each column spans `[x_i, x_{i+1}]` and rises to the SMALLER of the two neighbouring values —
  a staircase whose steps sit under the line, never a slope.
- A flat segment gets `s1 = 1` (top 1 px lower); a steep rise is widened by up to 2 px each side,
  a steep fall narrowed by the same. Replicate literally or not; it is ±2 px.
- Value 0 does not sit on the bottom edge: `y = h − 0 − 8`, so a zero series is still an 8-px
  filled bar above the baseline, with the dark pass's 3-px strip under it. The plot area itself
  is `h − 10` tall, so the bottom 10 px of the `.sce` rect are never painted by the plotter.
- The peak value plots at `y = h − h·max/(1.1·max − 8) − 8 < 0` → clamped to the top edge.

### 1.8 Draw order — READ from the GS environment and the render list (2026-09-28, follow-up)

Master asked for the z rule to be read rather than argued from the panel. It is, and the
mechanism is not a depth test at all:

1. **The GS never rejects on depth.** Every draw environment the game builds writes
   `TEST_1 = 0x30000` — `ZTE=1, ZTST=1 (ALWAYS)`: `FUN_0021ca48` (`DAT_202efae0/e8`),
   `FUN_0021bdc8` (`param_2[6..7]`, and again `[10..11]` for the second context),
   `FUN_0021c288` (`param_2[0x10..0x11]`); `FUN_0021b960` writes `0x20000` (`ZTE=0`). Every
   `ZBUF_1` they write carries `0x100000000` (`ZMSK=1`): the Z buffer is never updated. The
   library's `FUN_00269df8` writes `0x21202` (ZTST ALWAYS too). Control: the scan of every
   `0x47` immediate in the image found nothing else that is a GS register (the last candidate,
   `0x234fb0`, builds the string "TGA"). No static A+D packet in data sets `TEST` either.
2. **Ordering is a CPU sort.** `FUN_00213838`/`FUN_002137c0` store the sprite's z (`z/128`,
   float) into the packet; `FUN_002329c8` copies it to `ctx+0x200f8` (`DAT_00331258`) and
   submits through `FUN_0022a068(0x311160, verts, flags|0x3010000, tex)`. There, if the
   TEXTURE's flag word has `0x40`, the entry goes to a sorted region with
   `key = z + 262144.0 (+4096/8192 by class)`; otherwise it is pushed on the texture's own
   LIFO chain. `FUN_0022ae80` emits: untextured chain, then each texture's chain (head first,
   i.e. last-submitted first), then the sorted region after **`FUN_0022a400` — a quicksort whose
   partition puts keys GREATER than the pivot on the left**, i.e. descending — walked in
   ascending index. **Larger key → emitted earlier → behind. Smaller z is nearer.**
3. **Who is sorted.** `FUN_00237678` (the create virtual, vtable `0x36ffa8` slot 9, reached from
   `FUN_00235300` for every texture made by size) sets the flag word to **`0x40`** — so the
   nameless 8×8 flat sprite 57 (record `0x2ef548`, filled by `FUN_00214c20` with `0x60ffffff`)
   and the font pages are sorted. The `.ssh` loader `FUN_00235d68` sets `0x40` only when the
   entry's GM word (`+0x14`) has bit 27, the alpha plane `ssh.md` documents. Read off `UI.WAD`:
   `messfill/messcorner/messedge.ssh` are `0x85`, GM `0x0880012x`, bit 27 set → sorted;
   `laptop_<world>.ssh` are `0x84`, bit 27 clear → the opaque per-texture chain, emitted before
   the sorted region. That is the intended split: backdrop first, everything with alpha by z.
4. **Applied to the widget** (all in the sorted region): colour pass `Z−1`, dark pass `Z`,
   panel `Z`, page text `Z…`. The colour pass has the smallest z of the three, is emitted last,
   and is in front. The panel and the dark pass tie at `Z`; the quicksort is unstable, so their
   mutual order is unspecified — both are behind the coloured area, and `messfill` is alpha.
5. **Second control, independent of the graph**: the text renderer `FUN_0020acf8` submits each
   glyph at `z` and then its black drop shadow at `(x+dx, y+dy, z+8)` — same font texture, same
   path. A shadow must land behind its glyph; with descending-key emission it does. Under the
   opposite rule every laptop label would wear its shadow on top.

So the argument from "the panel must end up behind the data" was right, and the reason is the
sort direction, not a depth compare.

### 1.5 Data: 24 buckets over the span

Both screens keep their samples in global arrays of **25 words**: `[0]` = the max used for
scaling, `[1..24]` = the buckets. They are rebuilt by a builder at construction and whenever the
year selector changes.

**Finance — `FUN_001363a8(screen)`**, seven arrays at `0x2b5dc8 + k·0x64`:

| k | array | getter (park, m) | ring in the park object | label |
|---|---|---|---|---|
| 0 | `0x2b5dc8` | `FUN_00100da8` | `+0xbc` (bank balance at month end; m=0 → live `park→+4`) | Bank Balance |
| 1 | `0x2b5e2c` | `FUN_00101288` | `+0x107c` (park value at month end; m=0 → live `FUN_001011c8`) | Park Value |
| 2 | `0x2b5e90` | `FUN_00100fb8` | `+0x2fc` | Money In |
| 3 | `0x2b5ef4` | `FUN_00101010` | `+0x53c` | Gate |
| 4 | `0x2b5f58` | `FUN_00101068` | `+0x77c` | Shop |
| 5 | `0x2b5fbc` | `FUN_001010c0` | `+0x9bc` | Sideshow |
| 6 | `0x2b6020` | `FUN_00101170` | `+0xe3c` (written at month end by `FUN_00100a18`) | Staff Wages |

The label column is not a guess: the overall page prep `FUN_001350a0` hands series 0/1 the
pointer at `screen+0x988 = &0x2b5dc8` stepped by 100 bytes, and the finance page prep
`FUN_00135530` hands series 0..4 `screen+0x98c = &0x2b5e90` stepped by 100 — in the same loop
that pairs them with the `Items` text ids. Every getter value is `/10` (money is in tenths).
A ring at `+0xbfc` exists (zeroed at month end with the others) but no graph reads it.

```
months = stats→+0x1c            // FUN_0016ae90()→+0x1c, the month counter
span   = min(months, years·12)
m      = span                   // months ago; the OLDEST month in the window
for b in 0 .. 23:
    lower = span - (b · years) / 2          // integer division
    bucket[b] = 0 ; count = 0
    while m >= lower and m >= 0:            // NOTE: the boundary month is in both buckets
        bucket[b] += getter(park, m) / 10 ; count++ ; m--
    m = lower                               // (re-includes the boundary month)
    if count: bucket[b] /= count
    max = max(max, bucket[b])
    if lower <= 0: break
arr[0] = max · 0x119a >> 12                 // × 1.1001
```

So bucket 0 is the oldest month, bucket 23 the newest; with 1 year selected each month lands in
two buckets (`years/2 = 0` steps), with 2 years each bucket is one month, with 6 / 12 years three
/ six months. ⚠ The first bucket's `m = span` is ONE PAST the ring for the accumulator rings
(`FUN_00100f68` needs `m < count`) and returns 0, so the leftmost bucket of Money In/Gate/Shop/
Sideshow/Wages is always 0 and the next is halved. Retail arithmetic; keep or fix knowingly.
⚠ Buckets beyond the last visited keep whatever a previous (wider) build left; only the
`n = 24·span'/span` points are plotted so they are normally invisible.

**Park stats — `FUN_00186d38(screen)`**, five arrays at `0x2c40c8 + k·0x64`, samples are BYTES
from the stats singleton `FUN_0016ae90()` (0x32c bytes, vtable `0x361cd0`, month counter `+0x1c`),
144-entry byte rings, index `FUN_0016b2e8` (same `m=0 → 1` aliasing):

| k | array | getter | ring | label | max (`arr[0]`) |
|---|---|---|---|---|---|
| 0 | `0x2c40c8` | `FUN_0016b338` | `+0x2c` | People In Park | **110, fixed** (image value, no writer) |
| 1 | `0x2c412c` | `FUN_0016b378` | `+0xbc` | Arrival Rate | `max(5, peak) · 0x1262 >> 12` (×1.15) |
| 2 | `0x2c4190` | `FUN_0016b3b8` | `+0x14c` | Happiness | **120, fixed** |
| 3 | `0x2c41f4` | `FUN_0016b3f8` | `+0x1dc` | Time In Park | `max(|peak|) · 0x119a >> 12` (×1.1) |
| 4 | `0x2c4258` | `FUN_0016b438` | `+0x26c` | Overall Rating | **120, fixed** |

Same bucket walk, same `years/2` step, **but** the inner loop is `bucket = value` not
`bucket += value` — `0x186eac sw v0,0(t0)` straight after `jal 0x16b338`, and likewise for the
other four — followed by the same `/ count`. With 1 year selected every second point is the
oldest month of its pair halved; with 12 years every point is one-sixth of a single month. The
finance loop at `0x136560` is `lw; mflo; addu; sw`, i.e. a real sum. Both read from the raw words.

**Where the numbers come from** (so the port can feed the same thing):

- Finance month end `FUN_00100a18(park)` (called from the stats tick `FUN_0016b060`): stores the
  cash balance into `+0xbc[count%144]`, computes the month's wages (`FUN_001008b8`) into
  `+0xe3c[count%144]`, deducts loan repayments + wages, stores `FUN_001011c8()` (park value =
  Σ catalogue price of every object × 10 / 2, i.e. half the purchase prices in tenths) into
  `+0x107c[count%144]`, then `count++` and zeroes the NEW month's `+0x2fc/+0xbfc/+0x53c/+0x77c/
  +0x9bc/+0xe3c` slots. Income functions add into the current slot during the month
  (`0x1016d4` and friends). So a month's Money In/Gate/Shop/Sideshow is the month's total.
- Stats recorder `FUN_0016b478(stats)` (same tick, before `+0x1c++`): walks the visitor list —
  `people = n`, `arrival = max(0, n − people[last month])`, `happiness = Σ visitor→+0x75 / n`,
  `time in park = Σ FUN_00211d48(visitor) / n`, `rating = FUN_00153650()` (the park rating).
  ⚠ Because the getters alias `m=0` to `m=1`, the "current" value the Statistics page prints is
  **last month-end's recording**, not the live count.

### 1.6 The year selector

The `YearSelect*` elements are NOT drawn by the numeric widget class; the page draws them itself
and uses the widget only as the value model and for input.

- Model: `FUN_00207a50` object (finance `+0x8d8`, park stats `+0x630`), vtable swapped to
  `0x36c690` (option-list variant). `FUN_00207f08` clears, `FUN_00207f18` appends **1, 2, 6, 12**;
  index `+0xac` starts at 0 → value `+0x68` = 1. Mode `+0x60 = 0x11`: bit 0 = wrap, bit 4 = the
  label-left layout of the widget's own (unused) draw.
- Input `FUN_00207f50` (called every frame from the page's input hook): returns unless
  `+0x18 & 1`, which the page sets to `(cursor row == 0)`; pad bit 8 → index+1, bit 4 → index−1,
  wrapping; value = `options[index]`. On change: finance `+0x584 = value; FUN_001363a8()`, park
  stats `+0xcbc = value; FUN_00186d38()`; the widget's `+0x4c` is refreshed from that every draw.
- Drawn by the page: `"Years"` (`0x3c5`, `STR_FINANCE_YEARS`) at the `YearSelect` frame with its
  `.sce` justify; the value through the integer formatter `FUN_00142b68` at `YearSelectValue`;
  the standard arrows sprite (`FUN_00143b18` object, variant 1) at `YearSelectArrow`, tinted
  **(255,255,0) when the cursor is on the Years row, else (200,130,0)**.
- ⚠ `STR_SELECT_1_YEAR / 3_YEARS / 6_YEARS / 12_YEARS` (970/5/847/166) are never referenced by
  these screens; the option set is 1/2/6/12 and the value prints as a number.

### 1.7 Cursor, toggles, hint bar (shared by all three)

- Cursor row: finance `+0x588`, park stats `+0x344`. Row 0 = the Years row, rows 1..N = items.
  Up (pad bit 1) / down (bit 2) with wrap (`< 0 → N`, `> N → 0`), sound `FUN_00111150(_, 0, 0xd6, 0)`.
- On an item row the select mask (`FUN_00181250(0)`) clears ALL toggles then `^= 1` the row's:
  exclusive, and selecting the shown item again hides it (zero series drawn).
  Toggles: overall `+0x990[2]`, finance `+0x998[5]`, park `+0x9e0[5]`; the first is 1 at ctor.
- Item text colour: `(200,130,0)` normally (`0x35f550`), `(255,255,0)` for the row under the
  cursor (`0x35f560`). Row step **32** (`DAT_002b5d28` / `DAT_002b5d80` / `DAT_002c4508`, image
  value 32, no writer — the same 32 the list screens use).
- Hint bar: `FUN_00164b70(this, row != 0)` → `FUN_0013e288(gizmo, 2, id)` with `0x1dd` "Select"
  on item rows and `0x136` " " on the Years row.
- Laptop model animation: finance ctor `0x12`; entering the overall page `0x17`, finance stats
  `0x18` (`FUN_00134828`); park-stats ctor `0x11`, the Statistics page `0x14` every frame
  (`FUN_00185998`).
- The root base also issues two kind-1 panels after every finance page: `FUN_00164e78` at
  `(16,64) 280×152` and `FUN_00164f08` at `(280,80) 180×110` — not graph-specific.

## 2. `main_fi_financestats.sce` — menu 21, finance page index 2

Binder `FUN_00133960` (elements at `0x133c48..0x133d74`), draw `FUN_00135620`, input
`FUN_00134ec0`, prep `FUN_00135530`. Frame accessor order is `[row, col, w, h]` (established in
`shop-info-ui.md`).

| element | row | col | w | h | justify | drawn as |
|---|---:|---:|---:|---:|---|---|
| `YearSelect` | 130 | 45 | | | left | "Years" |
| `YearSelectValue` | 130 | 120 | | | left | the span, itoa |
| `YearSelectArrow` | 143 | 150 | | | | arrows sprite |
| `Items` | 200 | 45 | | | left | 5 labels, rows 200,232,264,296,328 |
| `graph` | 208 | 315 | 147 | 200 | | the widget; plot area 147×190, year numbers at row 416 |

Rows (`DAT_0035e660`, draw order) with their series and colour (`0x35e4a0 + 4i`):

| i | text id | key | string | array | colour |
|---:|---:|---|---|---|---|
| 0 | 447 | `STR_FINANCE_MONEY_IN` | Money In | `0x2b5e90` | (254,1,1) red |
| 1 | 565 | `STR_FINANCE_GATE_TAKINGS` | Gate | `0x2b5ef4` | (231,102,27) orange |
| 2 | 954 | `STR_FINANCE_SHOP_TAKINGS` | Shop | `0x2b5f58` | (254,254,1) yellow |
| 3 | 818 | `STR_FINANCE_SIDESHOW_TAKINGS` | Sideshow | `0x2b5fbc` | (1,176,60) green |
| 4 | 371 | `STR_FINANCE_STAFF_WAGES` | Staff Wages | `0x2b6020` | (64,64,64) grey |

Fully specified.

## 3. `main_fi_overallstats.sce` — menu 22, finance page index 1

Binder elements at `0x133a78..0x133ba4`, draw `FUN_00135190`, prep `FUN_001350a0`.

| element | row | col | w | h | justify |
|---|---:|---:|---:|---:|---|
| `YearSelect` | 130 | 45 | | | left |
| `YearSelectArrow` | 140 | 200 | | | |
| `YearSelectValue` | 130 | 180 | | | center |
| `Items` | 200 | 45 | | | left (rows 200, 232) |
| `graph` | 208 | 315 | 147 | 200 | |

| i | text id | key | string | array | colour (`0x35e490`) |
|---:|---:|---|---|---|---|
| 0 | 3 | `STR_FINANCE_BANK_BALANCE` | Bank Balance | `0x2b5dc8` | (254,0,0) red |
| 1 | 813 | `STR_FINANCE_PARK_VALUE` | Park Value | `0x2b5e2c` | (231,102,27) orange |

Fully specified. ⚠ Bank Balance can go negative; `min` is still 0, so a negative month clamps to
the bottom edge (`y > h → h`).

## 4. `main_ps_statistics.sce` — menu 24, park-stats page index 1

Binder `FUN_00183d50` (`0x184228..0x18441c`), draw `FUN_00185c48`, input `FUN_00185998`, prep
`FUN_00185b58`. The item loop's per-row value fetch sits behind a jump table at `0x364300` that
Ghidra dropped; it was recovered from disassembly (`0x185e78..0x18607c`).

| element | row | col | w | h | justify | drawn as |
|---|---:|---:|---:|---:|---|---|
| `GraphText` | 115 | 45 | | | | the TOGGLED item's label (any of the five, not only people) |
| `GraphValue` | 115 | 250 | | | | that item's value for month 0 (= last recorded month) |
| `YearSelect` | 175 | 45 | | | left | "Years" |
| `YearSelectValue` | 175 | 210 | | | center | span |
| `YearSelectArrow` | 185 | 230 | | | | arrows |
| `Items` | 225 | 45 | | | left | 5 labels, rows 225,257,289,321,353 |
| `graph` | 208 | 315 | 147 | 200 | | widget |
| `GraphLegend` | 425 | 369 | | | center | **never read** (writes only, `0x1842b0..b8`; control `GraphText` col is read at `0x185fcc`) |

| i | text id | key | string | array | colour (`0x364150`) | value format |
|---:|---:|---|---|---|---|---|
| 0 | 512 | `STR_PARKSTATS_PEOPLE_IN_PARK` | People In Park | `0x2c40c8` | (254,1,1) | `FUN_00142b68` int |
| 1 | 618 | `STR_PARKSTATS_ARRIVAL_RATE` | Arrival Rate | `0x2c412c` | (231,102,27) | int |
| 2 | 926 | `STR_PARKSTATS_HAPPINESS` | Happiness | `0x2c4190` | (254,254,1) | `FUN_00142a10` = int + "%" |
| 3 | 539 | `STR_PARKSTATS_TIME_IN_PARK` | Time In Park | `0x2c41f4` | (1,176,60) | `FUN_00142b30` = int + text 221 `STR_GUI_D` "d" |
| 4 | 727 | `STR_PARKSTATS_OVERALL_RATING` | Overall Rating | `0x2c4258` | (1,178,235) | int + "%" |

The draw walks i = 4 → 0. Fully specified as far as the code goes; the one open point is the
appearance question below, shared with the other two.

## 5. What was NOT determined, and what settles it

1. ~~Which plot pass is in front~~ — **read** (§1.8): smaller z is nearer, the colour pass is
   in front, the series is a filled area.
2. ~~Whether the panel fill covers the plot~~ — **read** (§1.8): the panel sits at `Z`, behind
   the colour pass at `Z−1`; `messfill.ssh` is alpha (`0x85`, GM bit 27). What is NOT read is the
   blend equation (the `ALPHA_1` value in the environment packets and how the `0x60` alpha of
   sprite 57's fill colour combines with the `0xff` vertex alpha), so the exact translucency of
   the coloured area and of the panel over the dark strip is still a capture question.
3. **Sprite `0x32`** is `messages\messedge.ssh` (`FUN_00216028`, `DAT_002ef098..0ac`); `0x31` is
   `wboxfill.ssh` (opaque, `0x84`). Closed.
4. **The text origin** (`FUN_0020acf8`) is the open question `laptop-screens.md` already carries;
   the widget adds the per-frame `(px, py)` offsets to its frame the same way, so whatever the
   port does for text applies to the graph rect unchanged.
5. Units of `FUN_00211d48` (time in park) beyond "printed with a `d` suffix".
