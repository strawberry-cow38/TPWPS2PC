<!-- Research notes (2026-09-28), copied from ~/ghidra_tpw/notes. Paths under ~/ghidra_tpw refer to the research box. -->

# HUD and laptop text: HUD units or framebuffer pixels? (agent advV, 2026-09-28)

PAL `SLES_500.32`. Addresses are ELF virtual addresses. READ means seen in MIPS, a decompile or data.
INFERRED means reasoned from READ facts. MEASURED means measured on the disc or on a retail capture.
Decompiles: `~/ghidra_tpw/agent_advV/out/g1.c`, `g2.c` (and `v1..v7.c` from the earlier pass). Tools:
`agent_advV/out/glyphscan` (Large.bff metrics and icon extents, run against the port's Release DLL) and
`agent_advV/out/capture_measure.py`.

## Answer

**Text is sized in HUD units, and so stretches with the frame exactly like sprites.** Each glyph texel is
drawn as one HUD unit square. On PAL 640×512 that is **1.25 framebuffer px across and 1 px down**.

- A `Large.bff` glyph of bitmap width N is **1.25·N px wide** on the PAL framebuffer and H px tall. Its pen
  advance A is 1.25·A px. Example: `'0'` (w 9, h 17, adv 12) is 11.25 × 17 px and advances 15 px.
- No 640/512 correction exists anywhere in the glyph path (§3). The one width-getter call on a text
  path, `0x1386D0`, moves POSITIONS only (§3.2).
- **Port.** `k = view.Y/512` on both axes is wrong. The console-faithful glyph scale is **the same
  per-axis factor the port already uses for positions: `(view.X/512, view.Y/512)`**. At 4:3 that is
  **(4/3·k, k), not (1.25·k, k)**. 1.25 is the framebuffer-PIXEL ratio 640/512. PAL pixels on a 4:3
  display are 16/15 wide, so a HUD unit is 1.25·16/15 = **4/3** as wide as it is tall on the glass.
  (1.25·k, k) is right only if the frame is shown with square 640×512 pixels, a 5:4 view. As things stand,
  port text is too narrow for its own layout by `view.X/view.Y`: 0.75× at 4:3, 0.5625× at 16:9.

## 1. Glyph bitmap → strip texture: 1 glyph texel = 1 strip texel (READ)

Every string is laid out, rasterised into 256×32 RGBA strip textures, and each strip is drawn as a sprite.

| step | where | what it does with sizes |
|---|---|---|
| metrics | `0x118EC8` (font vt `+0x34`) | returns u16 advance = desc `+12`, `left+width` (`+10`,`+8`), `top+height` (`+11`,`+9`). Integers in texels |
| layout | `0x1D2FD0` | pen x += advance per glyph. Each glyph entry stores the integer pen (x,y). The bbox comes from `left+width` and `top+height`. Line y += line advance (`0x1D34B0` → metric 2 = header `+7`; 30 for Large). No scaling |
| raster | `0x1D34F0` (sole caller `0x20AF18`) | per glyph: font vt `+0x34` metrics, then the blitter vt `+0x24` at integer (pen + layout offset) |
| blitter | renderer made by `0x1B47B8` (sole caller `0x20A6EC`, the font loader). Vtables `0x365A78`/`0x365A48`. `0x1B3700` = begin, `0x1B3878` = glyph blit | `0x1B3878`: dest = (x+left−clip.x, y+top−clip.y), size = glyph w×h (glyph-image getters vt `+0x3C`/`+0x34`), clipped. Source = pixels + dy·pitch + dx. Then `0x1B4390` → pixel writer `[+0x48]` |
| surface | `0x36C820` (36-byte descriptor copied to font record `+0x2C` at `0x20A744..0x20A788`) | {ptr, **w 0x100, h 0x20, pitch 0x400**, masks R `0xFF` G `0xFF00` B `0xFF0000` A `0xFF000000`, bpp 0x20}. So it is 256×32, 4 bytes per texel, 1024 bytes a row. `0x1B3FF0` matches it to format `0x365D60` and picks writer **`0x17F270`** |
| writer | `0x17F270` (MIPS `0x17F2D8..0x17F300`) | for each dest texel: `v = *src++`; if v ≠ 0, `*dst = colour \| (v>>1)<<24`; dst += 4. **One source byte to one dest texel.** No step other than 1 |
| coverage | `0x119068` (glyph expand) | nibble n → byte `n \| n<<4` = 17·n, row-major w×h, packed with no row reset |
| texture | `0x2151D8(…, 0x40, 0x100, 0x20, rec+0x54, 1)` at `0x20AEB0` → `0x213448` → `0x213050(spr, 0x100, 0x20)` → `0x235300(w,h)` | 64 strip records, each a **256×32** texture. Sprite `[0]=[2]=256, [1]=[3]=32`. `0x213F88` clears w·h·4 bytes |

Glyph alpha: coverage 15 → 255 → alpha byte `0x7F` (GS scale, where 0x80 = 1.0). The text RGB is the
font record's colour word (`0x1B43C0` → renderer `+0x4C` → byte-swapped into `+0x50` by `0x1B3FF0`),
baked into the strip.

## 2. Strip → screen: the same calls as every sprite (READ, MIPS `0x20AF20..0x20B02C`)

```
0x2137C0(prim, x, y, z)            packet +0xC = x·(1/256) − 1,  +0x10 = 1 − y·(1/256),  +0x1C = z·(1/128)
0x2131B8(prim, 0x100, 0x20)        spr +8 = 256, +0xC = 32, +0x10 = 256/256 = 1.0, +0x14 = 32/256 = 0.125
0x213938(prim, 0.0, 0.0, 1.0, 1.0) packet +0x20 = +0x22 = 4095: UV spans the whole 256×32 texture
0x213778 / 0x213798                packet bit 0 on/off (ctx[6] && [0x2EE910]); not a size
0x213980(prim)                     x1 = x0 + w/256, y1 = y0 − h/256; min/max; 0x1704F0 copies 0x2F0400 → 0x2F0300;
                                   0x2329C8(packet, 0x200000) emits the quad through matrix 0x2F0300
```

- The constants are checked in MIPS: `lui 0x3B80` = 1/256 (`0x2131C4`, `0x2137D8`), `lui 0x3C00` = 1/128
  (`0x213800`), `0x457FF000` = 4095.0 (`0x213944`). `f20 = 1.0` (`0x20AE48`), `f21 = 0` (`0x20AE44`).
- **The code equates texels and units three times.** (a) Strips wider than 256: x steps **+0x100 units**
  (`0x20AFEC`) while the layout window steps **0x100 texels** (`uStack_d0 −= 0x100`, `0x20AFF0`). (b) Strip
  rows step **+0x20 units** (`0x20B02C`) against **0x20 texels** (`0x20B014`). (c) Justify 1 subtracts the
  bbox half-width in texels, `(x1−x0)>>1`, from x in units. If a texel were not one unit, strips would
  gap or overlap.
- `0x2137C0`, `0x2131B8` and `0x213980` each have 47–48 callers; the HUD icons, panels and nine-slices use
  them. `0x213980` is the only caller of `0x2329C8` on this path (`0x213A44`). **Text and sprites share
  every step from the unit coordinate to the GS.**

## 3. No aspect correction in the glyph path (READ, searched)

### 3.1 Searched

- `0x20ACF8` has **no size parameter**: `(ctx, x, y, z, text, flag)`. It uses ctx `[0],[1]` (shadow
  offset), `[4]` (justify), `[5]` (shadow on) and `[6]`. The strip size is the immediate `0x100 × 0x20`.
- These MIPS ranges hold no `lui 0x3F4C/0x3FA0/0x3FAA` (0.8/1.25/1.333) and no `0x280`, `0x1C0` or `0x200`
  immediate: `0x20ACF8..0x20B080`, `0x1D2FD0..0x1D3700`, `0x1B3700..0x1B3AD0`, `0x17F270..0x17F600`,
  `0x2131B8`, `0x2137C0`, `0x213938..0x213A60`, `0x2329C8..0x232D00`, `0x118EC8`, `0x119068..0x119270`.
  None of them calls the size getters `0x20A120`/`0x20A130`.
- **There is no second text renderer.** `0x1D34F0` has one caller. `0x1D2FD0` is called only from
  `0x20ACF8` (`0x20ADD0`, `0x20ADEC`) and from four siblings at `0x20AA90..0x20ACA0`. The siblings
  call only `0x29DC40`, `0x29C370` and `0x1D2FD0`, with no draw (READ, jal list). INFERRED: they measure
  strings. The font factory `0x118D38` and
  the renderer factory `0x1B47B8` are each called once, by the loader `0x20A520` (`0x20A6CC`, `0x20A6EC`).
  No data word points at `0x1D34F0`, `0x20ACF8`, `0x1D2FD0`, `0x213980` or `0x2329C8`.

### 3.2 Width/height getters on text paths: positions only

- `0x1386D0(ctx, text, x, y, z)` passes `x·W >> 10` and `y·H / 0x300` to `0x138798`. That is a 1024×768
  layout scaled by the PIXEL size into HUD units (`0x138700..0x138754`). Its only callers are
  `0x13AEA0/0x13AF28/0x13AF48` in `0x13ADD0`, a debug overlay ("NO CURRENT SCENE"). **Position only.**
- The other getter callers (`0x13E170`, `0x153484`, `0x18056C`, `0x1961B4`, `0x209EB4`, `0x218DB8`) take
  `W>>1` / `H>>1` as a centre. Also position only. They are the same effect that puts the advisor box at
  320 units (`advisor-V-visuals.md` §1.5).

## 4. Controls

### 4.1 Sprite 0x3A by the same derivation (READ; weak as a control)

`0x1FBCD0`: `0x2137C0(spr, 0, 0, 1)` gives NDC x0 = −1. `0x2131B8(spr, 0x200, [+0x70]+0x10)` gives +0x10 =
2.0. `0x213980` gives x1 = +1. NDC −1..+1 through `0x2F0400` (m00 = W/3600, `0x21F0B0`) is the full frame
width, 640 px. That matches the premise. The same method gives the text strip NDC width 1.0 = **320 px for
256 texels = 1.25 px per texel**, and height 0.125 NDC = 32 px for 32 texels.

⚠ This control is **circular**: 0x3A's "known" 640 px comes from the same 512-units-per-frame premise.
Sprite 0x3A is a nameless 8×8 flat texture (registry `0x216028`, record `0x2EF560`, name `0x36D8C0` = "",
8×8). No `jal`, data word or `lui/addiu` pair reaches `0x1FBCD0`, so its on-screen use was not found. The
independent check is 4.2.

### 4.2 Retail capture (MEASURED; independent)

The image is strawberry's retail PS2 capture, posted in cowbot on 2026-09-27 at 00:07 UTC with message
`1553558551857987635`. It is 1600×885, a build-mode HUD, stored at
`~/claude-discord/tinyclaw/.claude/channels/discord/inbox/1790467671110-1553558550863683706.png`. The
positions are READ unit coordinates (`FUN_0013DCB8`, `FUN_001B3210`, `FUN_0011B2F8` as quoted in the port).
Each glyph's bearing was added from Large.bff (`glyphscan`). The measurements are ink-left edges:

| element | unit x (origin+bearing) | px | unit y (origin+top) | px |
|---|---|---|---|---|
| money `$` | 38+1 | 118 | 50+2 | 63 |
| ticket count `3` | 80+0 | 251 | 80+5 | 122 |
| star count `0` | 80+1 | 254 | 110+5 | 177 |
| `Cost:` `C` | 38+1 | 118 | 160+2 | 262 |
| `Pylon Stock` `P` | 36+1 | 112 | 196+3 | 328 |

Fits: **x = −8.1 + 3.237·u** (residuals ≤ 0.3 px) and **y = −31.0 + 1.806·u** (≤ 0.5 px). The capture is
the frame stretched to a 1.8:1 window and cropped (it shows about 494 × 491 units). This is why "1600 px =
512 units" is only about 4 % right.

Glyph pitch, a least-squares fit of each glyph's ink-left edge against its Large.bff pen+bearing:

| string | glyphs | px per glyph texel | max residual |
|---|---|---|---|
| `Pylon Stock 32` | 12 | **3.230** | 1.2 px |
| `04/08/2002` | 10 | **3.229** | 2.9 px |
| `Cost: $100` | 9 | **3.241** | 1.3 px |

- **Glyph texel / HUD unit across = 0.998–1.001.** The framebuffer-pixel hypothesis (texel = 0.8 unit)
  predicts 2.59 px per texel. On `Pylon Stock 32` it predicts a 353 px span where the capture shows 444.
  Rejected.
- Down: `Pylon Stock 32` ink is 26 texels tall and 47 px, which is 1.81 px per texel against 1.806 px per
  unit.
- **The sprite is in the same frame.** The ticket icon is `gticket.tga`, 32×32, drawn 32×32 units at
  (38,76). Its yellow texels span x 1..30 and y 7..25. In the capture they are 92 × 33 px. That is ≈ 3.07 ×
  1.74 px per texel; the threshold trims the filtered edges. At 0.8 unit per texel it would be 78 px wide.
  So sprite texels, glyph texels and unit positions carry **the same x:y stretch (1.76–1.80)**.

## 5. Drop shadow (READ; answers the coordinator's "palette slot `colour + 8`")

**There is no palette slot. The `+8` is DEPTH.** MIPS `0x20AF94..0x20AFE4`:

```
if ctx[5]:                                   ; shadow on (0x20B258(ctx, dx, dy) sets [0]=dx, [1]=dy, [5]=1)
    0x2138E0(prim, 0, 0, 0)                  ; colour word = 0xFF000000 | b<<16 | g<<8 | r  (lui 0xFF00 at 0x2138F8)
    0x2137C0(prim, x+ctx[0], y+ctx[1], z+8)  ; a3 = s6+8 (0x20AFB4); 0x2137C0's 4th arg is z (×1/128 → +0x1C)
    0x213980(prim)
    0x2138E0(prim, 0xFF, 0xFF, 0xFF)         ; restore white = 0xFFFFFFFF
```

- The shadow's sprite colour is **RGBA (0,0,0,255)**, normalised to (0,0,0,1.0) by `0x2329C8`'s 1/255.
  The strip texture supplies alpha: `coverage>>1`, i.e. 0x7F for solid European Large/Small texels.
  **INFERRED** (the VU1/GS modulate step was not read): the shadow is **opaque black at the glyph's own
  coverage**, not black at 0.55. The port's `MoneyShadowTint (0,0,0,0.55)` is a guess and should be
  opaque black.
- The money draw `0x108D40` and HUD `0x13DCB8` call `0x20B258(ctx, 2, 2)`, so the offset is **(2,2) HUD
  units**: 2.5 px right and 2 px down on PAL. The port uses `MoneyShadow·k` on both axes, which needs the
  same per-axis fix as the glyphs. z is 10 for the text and 18 for the shadow (`[0x2AA728]` = 10). Larger
  z is farther, so the shadow is behind (graph-widget §1.8).

## 6. For the port

- Text: `Scale = (view.X/512, view.Y/512)` wherever position is `x/512·view.X`. This covers the four
  `new Vector2(k, k)` sites in `game/Viewer.cs` (money 11355, cost/stock 11382, counts 11421, date 11477)
  and every other 0x20ACF8-backed text (laptop, advisor box, menus), since they all share one renderer.
- Shadow offset: `(2·view.X/512, 2·view.Y/512)`.
- Icons, the same issue: `Icon()` (≈11410) uses `size/512·view.Y` on both axes. The console draws 32×32
  units = 40×32 PAL px.
- A 4:3 presentation of the HUD means a 4:3 rect with 512 units each way. Glyphs are then (4/3)·k wide per
  texel, where k = rect height/512.
- Side note: the port's `DateBottomRows = 14` was measured assuming 885 px = 512 rows. On the fit above,
  the date's ink bottom (860 px) is unit ≈ 494, so `0x20ACF8`'s y for the date is about 471 (ink bottom 23
  texels below the origin). INFERRED from the capture; the date draw was not traced here.

## 7. Still unknown (what was tried)

- **The clip → GS pixel step** (2048 ± 1800·clip, making NDC ±1 = the frame) is still INFERRED
  (advisor-V §2.1; VU1 not read). The capture shows text and sprites share the stretch, which holds
  whatever that step is. The absolute 1.25 px per unit rests on W = 640 (`0x20A0E0(0x280, 0x200)`) and on
  512 units spanning W.
- **Who draws sprite 0x3A**: no caller of `0x1FBCD0` found (jal, 4-byte data word, `lui/addiu −0x4330`).
- **GS modulate/alpha for the shadow** (vertex 1.0 × texel alpha 0x7F): the VU/GS packing was not read.
- **Packet bit 0** (`0x213778`/`0x213798`, driven by ctx[6] and `[0x2EE910]`): → `0x4000` in
  `0x2329C8`. Its meaning (filter or blend) was not read. It does not touch size.
