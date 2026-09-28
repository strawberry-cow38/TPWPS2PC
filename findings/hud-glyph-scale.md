<!-- Research notes (2026-09-28), copied from ~/ghidra_tpw/notes. Paths under ~/ghidra_tpw refer to the research box; the retail captures are not in the repo. -->

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

## 8. The in-park LAPTOP: same 512-unit map, and the chrome is a full-frame background (follow-up, 2026-09-28)

**Answer.** The laptop has no projection of its own. Its widgets and text use the same calls as the HUD,
through the same `0x2F0400` 2D matrix. The chrome `laptop_<world>.ssh` is not a quad. It is IPU-decoded
into a background buffer, and each frame draws that buffer as a full-frame sprite. So **the 512×512 chrome
spans the whole frame, edge to edge**, and the laptop's 512 units cover the full width, as the HUD's do. On
PAL 640×512 that is 1.25 px across per unit and 1 px down. **The port's centred square
(`Scale = Min(view.X, view.Y)/512` on both axes, `game/LaptopShopScreen.cs:64`) is 1.25× too narrow on
PAL, and 4/3 too narrow on a 4:3 screen.** The console laptop fills the whole frame. This settles the
open question in `findings/shop-info-ui.md` ("whether the console itself stretched 512x512 … is NOT
settled"): it does.

### 8.1 The chrome's draw chain (READ)

| step | where | what |
|---|---|---|
| screen draw | every laptop screen inherits root `0x1648A8` (vtable `0x361A60`); the slot at `+0x14` is `0x1649C8`, reached e.g. from `0x1D98C8` | `0x212838(this+0x4C)`, then vt `+0x5C` |
| container | `this+0x4C` is built by `0x1434F0`: vtable `0x35F688`, `+0x30 = 1` (chrome on), `+0x34 = 1` (children on). `0x212838` calls vt `+0x2C` = **`0x143598`** | if `+0x30`: `0x2156A0(mgr, 0x3B)`, then `0x2135D8(spr)`. If `+0x34`: each child gets `0x212870(child, x+cx, y+cy, z+cz)` |
| sprite 0x3B | `0x2156A0` (`0x2156A4..0x2156EC`) | id 0x3B is special-cased: returns `[0x2EF758 + 4·0x14E170()]`, the per-world chrome loaded by `0x214C20` (`0x2158D8("Data/Ui/Laptop/", "laptop_<world>.ssh", 1)` → `0x2EF758..0x2EF764`). **The only `jal 0x2156A0` with 0x3B is `0x1435D4`** |
| `0x2135D8` | `0x2135D8..0x213634` | not a quad. It calls the texture's vt `+0x34` once and sets sprite `+0x24 \|= 4` |
| SSH vt `+0x34` | class vtable `0x36FEB0` (installed `0x235CFC`) → **`0x236578`** | returns 0 unless **`[0x2EFA24] & 0x100`**. Then it calls `0x223FE0(data+0x10, buf, DBP = [0x2EF9FC]<<5, w = tex+4, h = tex+6, DBW·64 = [0x2EFA30], PSM = [0x2EF9AC], DSAX 0, DSAY 0, flags 3)` |
| `0x223FE0` | IPU | checks `"GM"` (0x4D47), IPU-decodes, and GIF-IMAGE-transfers into VRAM at DBP, x 0, y 0, in 16-px columns. **The chrome entry is type 0x84, 0x200×0x200, and its data opens with `47 4D` ("GM")** (read off `UI.WAD/laptop/LAPTOP_JUNGLE.ssh` at `+0x70`/`+0x80`) |

### 8.2 The display side: where the UI pass puts the 512×512 (READ, one step INFERRED)

- **UI pass begin, `0x1C55C8`**, outermost level only (`[0x2E9904] == 0`): `0x21CA48(disp 0x2EF970)` at
  `0x1C5604`, then `0x21EE00` (`0x1C5644`), then the shadow default `0x20B258(ctx,2,2)`.
- **`0x21CA48`**, only if `disp+0xB4` bit 0 is set and 0x100 is clear:
  - it saves W/H (`+0xC8/+0xCC`), sets **0x100**, and sets **`+0xC0 = +0xC4 = 0x200`**: the UI renders
    512×512;
  - it sends a packet that draws the current frame (TBP = FBP·32, the saved width) as a sprite into buffer
    `[+0x90]` at 512×512, then draws `[+0x90]` into **`[+0x8C]`**, the background buffer.
- **`0x21EE00` rebuilds `0x2F0400` via `0x21F0B0`** from `[0x2EFA30]/[0x2EFA34]`, which are now
  512/512. So NDC ±1 = the 512-px UI buffer, and **1 unit = 1 UI-buffer pixel** in both axes.
- **Per-frame environment `0x21BDC8`** with 0x100 set: before anything else it emits a textured SPRITE
  (PRIM 0x16) with TEX0 = `[+0x8C]<<5`, TBW `[+0xC0]>>6`, TW/TH = log2(512). The ST run is (0,0)→(1,1)
  and XYZ2 runs (0x800 ∓ W/2, 0x800 ∓ H/2)·16, so it covers the whole buffer. **This background is
  what `0x236578` overwrote with the chrome: 512 texels onto the 512-px buffer, 1:1, full frame.**
- UI pass end `0x1C5680` → `0x21D028`: restores `+0xC0/+0xC4` from `+0xC8/+0xCC` and clears 0x100.
- INFERRED: the 512-wide UI result reaches the 640-wide display through the present (`0x21C288`: sprite
  from the render buffer, TW = log2(`+0xC0`), onto a display of width `disp+0x0` = 640). Its exact
  ordering with `0x21D028` was not read. The capture (8.4) shows the outcome.
- INFERRED: retail runs with `+0xB4` bit 0 set. The chrome is uploaded only under 0x100, 0x100 is set
  only by `0x21CA48` (no other writer found: Ghidra refs to `0x2EFA24` are all reads, and the struct
  stores are in `0x21CA48` / `0x21D028`), and the retail capture shows the chrome. A side effect: the
  **HUD is also drawn in this 512×512 UI buffer**. That changes nothing in §1–§6: text and sprites still
  share one map.

### 8.3 One laptop text row to the primitives (READ)

Shop screen draw `0x1D70C8` (vtable `0x368AE0`, fn word at `0x368BB4`), MIPS `0x1D71F8..0x1D7240`:
`0x1388E8(ctx, colour)`, `0x1389C0`, then **`0x138798(ctx, text, x = [0x2E9CC4], y = [0x2E9CC8], z, 1)`**.
The globals are the `.sce`-bound layout (`findings/shop-info-ui.md`: filled by the binder). `0x138798`
is a short forwarder to `0x20ACF8` (`jal` at `0x1387B8`), and from there it is §2's path
(`0x2137C0 x/256−1` … `0x213980 → 0x2329C8`, matrix `0x2F0400`). The bars and sliders are sprites on
`0x2137C0/0x2131B8/0x213980` too. **Nothing gives the laptop its own matrix.** Ghidra's 6 refs to `0x2F0400` are 2 in `0x21EE00` (the builder,
`0x21F0B0`) and 4 reads that copy it (`0x213980`, `0x2346E8`, `0x161110` at `0x161268`, `0x2282E0`).
`0x21EE00` runs at every UI pass begin (`0x1C5644`) and end (`0x1C56E4`). The render size `+0xC0/+0xC4`
is stored by `0x21AA18`, `0x21CA48` and `0x21D028`; a full census of other stores was not done.

### 8.4 Control: retail laptop captures (MEASURED)

These are strawberry's three retail laptop captures from cowbot, 2026-09-25 06:53–06:54 UTC (messages
`1552936143362854964`, `…184639135754`, `…224468242432`; the ones `shop-info-ui.md` calls "Master
supplied screenshots"). I downloaded them to `~/ghidra_tpw/agent_advV/out/laptopcaps/`. Only capture 1
(`1790610361502-…png`, Crazy Ape, 1600×892) shows the surround on all four sides. Captures 2 and 3 are
cropped by the user and cut into the bevel.

The panel boundary is where pixels differ from the surround (7,24,111) by more than 45, taken as medians
over the middle 40 % of rows and columns. It is fitted to the chrome art's own boundary: the flat
(0,0,100) key, READ off `LAPTOP_JUNGLE.tga`, is non-key at columns 12..498 and rows 17..493.

| | art (units) | capture 1 (px) | fit |
|---|---|---|---|
| panel x | 12 .. 499 | 38 .. 1565 | **x = 0.4 + 3.136·u → art 0..512 spans 0.4 .. 1605.8 of 1600** |
| panel y | 17 .. 494 | 22 .. 863 | **y = −8.0 + 1.763·v → art 0..512 spans −8 .. 895 of 892** |

**The chrome touches all four capture edges, within the capture's own crop.** Its x:y stretch is
**1.778**, the frame stretch the HUD capture showed (1.80). A square 512-px laptop inside a 640 frame
would cover 80 % of the width and give 1.44.

**Laptop elements on the same fit, using `main_i_ride_data.sce`'s own coordinates:**

| element (`.sce`) | predicted px | measured px |
|---|---|---|
| excitement bar, col 215 w 72, row 118 h 22 | x 675..900, y 200..239 | x 675..897, y 200..237 |
| reliability / repair / life bars, rows 150/182/214 | y 256/313/369 .. +39 | y 257/313/369 .. +37 |
| title `ItemSelect` row 65 col 45 (+ `C` bearing 1, top 4) | left 145, top 114 | left 145, top 112 |

The sprites (bars), the text (title) and the chrome all sit on one unit→px map spanning the full frame.

### 8.5 Side result, the date (READ)

`0x13DB90` draws the park date with Large.bff, white, shadow (2,2), at **`(0x26, 0x1D6)` = (38, 470)**
(`0x138630(…, 0x26, 0x1D6, 10, buf)`; `0x138630` (frame 0xC0) ends in `jal 0x138798` at `0x1386A4`). The HUD-capture fit in §6 predicted the date origin at ≈ 471.
That confirms that calibration, and the port's `DateBottomRows = 14` (a bottom anchor at 498) should
become y = 470.

### 8.6 Still unknown

- The final 512 → 640 present order (`0x21C288` against `0x21D028`) and the DISPLAY/MAGH settings
  (`0x21A0B8`) were not decoded. The capture shows full-frame coverage but cannot give the TV's pixel
  aspect.
- Who sets `disp+0xB4` bit 0: not found (it is 0 in the image; no store located). The inference that
  retail runs with it set rests on the chrome being visible.
- Captures 2 and 3 were not used (cropped).
