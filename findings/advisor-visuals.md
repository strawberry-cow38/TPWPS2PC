<!-- Copied from ghidra_tpw/notes/advisor-V-visuals.md (agent advV, 2026-09-28) for the port's record; no game
text is quoted. §0..§3 are the research as written: its §1.6 "port" column is the port BEFORE the
advisor-visuals step. §4 is what the port does now, with what that step READ, MEASURED and INFERRED. -->

# Advisor visuals: the read box and the head's screen placement (agent advV, 2026-09-28)

PAL `SLES_500.32`, owner's disc. Every address is an ELF virtual address. READ = seen in MIPS, a
decompile or data; INFERRED = reasoned from READ facts. Decompiles: `~/ghidra_tpw/agent_advV/out/v1.c..v7.c`.
Disc reads: scratch project `~/ghidra_tpw/agent_advV/out/boxscan` (modes `ui`, `fonts`, `model`, `anim`,
`chars`), run against the port's Release `TPW.PS2.Data.dll`.

## 0. Three premises in the brief that are wrong

1. **`0x20A120` is not the widget constructor and takes no arguments.** It is three instructions
   (`0x20A120..0x20A128`): `return [0x3989CC]`, the screen WIDTH. Its sibling `0x20A130` returns the
   height `[0x3989D0]`. Both are written only by `0x20A0E0(w, h)` (`0x20A104`, `0x20A10C`), which has one
   caller: boot `0x231500`, `0x20A0E0(0x280, 0x200)`. So the width is **640** and the height **512**.
   (Ghidra xrefs of `0x3989CC`: 1 write, 2 reads. The ELF holds no aligned data word `0x0020A0E0`.) The
   constants `0x104, 0xB4, 0x10, 0xE` are loaded into `a0..a3` AFTER the `jal` (`0x1084C8..0x1084DC`). They
   are only stored as widget fields (§1.1).
2. **The `0xFF` stores into `0x2Fxxxx` belong to the next functions**, which Ghidra ran together with
   `0x20A120`. `0x20A1A0` sets `[0x2EE908]=0xFF, [0x2EE904]=1, [0x2EE90C]=0` and calls `0x1817C0(1)`.
   `0x20A1E0` sets `[0x2EE904]=1, [0x2EE90C]=0xFF, [0x2EE908]=0` and calls the same. `0x20A218` steps
   `[0x2EE908]` toward `[0x2EE90C]` by `[0x2EE900]·D >> 12`, where D = `0x1C4920()`. So these are a
   current/target/active/speed quad: INFERRED a screen fade out and in. None of it concerns the box.
3. **"640×448 (PAL)"**: the PAL executable runs **640×512**. `0x230C88` takes the 448 (`0x1C0`) branch only
   for NTSC, and boot passes 512. Placements are given below for both heights.

Also: **(48,48,48) is the TEXT colour, not the background.** `findings/advisor-messages.md` §5.3 has it
wrong ("background (48,48,48)"), and so does the port (§1.6).

## 1. The read box

### 1.1 What `0x108458` sets up, every frame the stack is open (READ, MIPS `0x108458..0x108560`)

`0x108348` (the stack draw) calls `0x108458(stack, stack+0x144+cursor·0x120)` every frame while `+4`
(open) is set. So the widget at `stack+0x2544` is rewritten each frame. The stack is the global
`0x3928C8`, so the widget is at `0x394E0C`.

| widget off | value | meaning | reader |
|---|---|---|---|
| `+0x1C` | 1 | visible | `0x141F68`, `0x142090` |
| `+0x18` | 1 | element kind 1 = nine-slice panel | `0x141F68` switch |
| `+0x38` | `\|= 1` | draw enabled | `0x141F68` |
| `+0x58,+0x5A,+0x59` | 0x30,0x30,0x30 | text colour, via `0x1DD2E0` (`sb a1→+0x58, a2→+0x5A, a3→+0x59`) | `0x1DD0E0` |
| `+0x44`, `+0x40` | → `0x358C38` (128,100,64), `0x358C30` (222,171,82) | two colour pointers | **none on this path.** Kind 1 and the text draw never read them, and kind 0's eight painters `0x1424A0..0x1424D8` are all `jr ra` |
| `+0x4C` | 0x72 | text flags; only bit 2 (0x2, centre) is tested by `0x1DD0E0` (it tests 0x2, 0x4, 0x8) | `0x1DD0E0` |
| `+8` (s16) | `(0x20A120() − 0x104) >> 1` = (640−260)/2 = **190** | x | |
| `+0xA` (s16) | 0xD2 = **210** | y | |
| `+0x14`, `+0x16` | 0x104 = **260**, 0xB4 = **180** | w, h | |
| `+0x5C`, `+0x60` | 0x10 = **16**, 0xE = **14** | margins, applied OUTWARD by the draw (§1.3) | `0x1DD1E8` |
| `+0x34` | 1 | read only by kind 0 (all stubs) | |
| `+0x50` | `record+8` (own text) or `0x1DFA58(row)` if `record+0x108 ≠ −1` | the string | `0x1DD0E0` |
| `+0x48` | **0**, never written (base ctor `0x141F14`; no other store of stack offset `0x258C`) | fill selector | `0x142090` |

Then it calls `0x1DD1E8(widget, 0, 0, 10)` directly (`0x108530`). Afterwards it stores
`[0x2AA72C] = widget+0x54 + 0x80` (`0x108558`). A `lui 0x2b`/`−0x58D4` scan finds no reader of
`0x2AA72C`. (`0x1F383C` reads `lui 0x2f −0x58D4`, which is `0x2EA72C`, a different address.)

`0x1DFA58(row)` returns `[[0x397B9C] + 4 + row·4]` when `[0x397BA0]` is set, else `[[0x397B94] + 4 +
row·4]` (READ). It does no wrapping.

**The class.** The stack ctor (`0x107F0C..0x107F5C`) builds the widget with `0x141EA0`. It then stores
vtable `0x3697D0`, whose `+0x10` slot is `0x1DD0E0` (text only), and then vtable `0x3697A8`. The `+0x10`
slot of `0x3697A8` is **`0x1DD1E8`**: the box's draw is that vtable slot, and `0x108458` calls it directly.

### 1.2 `0x1DD0E0`, the text (READ, MIPS `0x1DD0E0..0x1DD1E4`)

```
0x141CE8()                              selects font 1 and returns ctx 0x2B6810 (ignores a1..a3); result unused
ctx = [0x2EEA58]
0x20A9C0(ctx)                           save: +8 ← +0xC (font id), +0x1C ← +0x14 (shadow on/off)
0x20A958(ctx, 0)                        font id 0
0x20A900(ctx, [+0x58],[+0x59],[+0x5A], 0xFF)   colour word 0x80RRGGBB = 0x80303030 → font rec +0x5C → 0x1B43C0 → obj+0x4C
if flags&4: 0x20B248(ctx,0); if flags&8: 0x20B248(ctx,2)
if flags&2: 0x20B248(ctx,1) (justify 1); px += w/2
0x20ACF8(ctx, x = [+8]+px, y = [+0xA], z = a3+[+0xC], text, 1)
0x20A9D8(ctx)                           restore the font id and the shadow flag
```

- The `a2` that `0x1DD1E8` passes (`py + 0xD`) is **dead**. `0x1DD0E0` overwrites `$a2` at `0x1DD134`
  before any read, and `0x141CE8` does not read it. So y is the widget's 210 exactly.
- **Font id 0 = `Small.bff`** (READ). `0x20BA28` (the font-table init) sets record 0 at `0x2EE918` to name
  `0x36C8B0` "Small.bff" with id `+0x58 = 0`. It sets record 1 to "Large.bff" (`0x36C8C0`), id 1, and
  record 2 to "Console.bff" (`0x36C8D0`), id 2. The records are 0x64 bytes apart. `0x20A958` picks the
  record whose `+0x58` equals the id. The loader path is `"Data\Fonts\%s\%s"` (`0x36C848`) with
  `"EUROPEAN"` (`0x36C860`). The count's font 1 = Large, as already documented, agrees.
- **Justify 1 = each line centred on x** (READ). `0x20ACF8` lays the string out with `0x1D2FD0(..., mode 1)`.
  For mode 1 that shifts every line by `−lineWidth/2` (`(e0 − f0)/2 · −1`). Mode 2 = left, mode 4 = right.
  The blit then starts at `x − (bboxW >> 1)`. Lines break on `\n`, `\r`, `\r\n` or `\n\r`. There is **no
  width wrap**. For font id 0 only, `0x20ACF8` moves the pen −2 and the layout origin +2: net zero
  (headroom inside the 256×32 strip).
- The text is rasterised into 256×32 strip textures (`0x2151D8(…, 0x40, 0x100, 0x20, …)`). Each strip is
  drawn at 256×32 units (`0x2131B8(prim, 0x100, 0x20)`), stepping 256 across and 32 down. So one glyph
  texel = one HUD unit.
- **Shadow.** `0x1DD0E0` neither sets nor clears it; it inherits the context's `+0x14`. The outermost
  render-pass begin `0x1C55C8` turns it ON: `0x20B258(ctx, 2, 2)` at `0x1C5658`, when the nesting counter
  `[0x2E9904]` is 0. Every HUD draw before the box in the same frame restores what it found:
  `0x13DB90` (`0x13DBDC` save / `0x13DC8C` restore), `0x108D40` (`0x20A9C0`, then `0x20B250` + `0x20A9D8`
  at `0x108E60`). If the flag is on, `0x20ACF8` draws each strip a second time in black at (x+2, y+2),
  z+8. **INFERRED: the box text carries a black 2,2 drop shadow.** Eight other shadow sites clear it
  without restoring (`0x11B528`, `0x1297D8`, `0x159D24`, `0x16DACC`, `0x16E154`, `0x180334`, `0x1B3390`,
  `0x2050CC`), and `0x12D538` sets it and returns. Their order in the frame was not traced.
- **Colour.** The word `0x80303030` reaches the glyph rasteriser as `font.obj+0x4C` (`0x1B43C0`: `sw`). The
  strip sprite itself keeps a white sprite colour: `0x20ACF8` resets it to `0xFF,0xFF,0xFF` after each
  shadow pass, and the 2D path normalises vertex colour by 1/255 (`0x2329C8`). INFERRED on-screen text
  colour: **(48,48,48)**, opaque dark grey, with glyph coverage as alpha. How the rasteriser's virtual
  (`0x1D34F0 → surface vt+0x24`) applies `+0x4C` was not read.

### 1.3 `0x1DD1E8`, the panel, read to the end (READ)

`0x1DD1E8(w, px, py, z)` calls `0x1DD0E0(w, px, py+0xD, z)`. It then **widens the rect outward by the
margins**: x −= 16, y −= 14, w += 32, h += 28 (`0x1DD220..0x1DD274`). It calls
`0x141F68(w, px, py, z + [+0xC])` and restores the rect.

`0x141F68`: if `+0x1C`, it draws the children at `+0x20` (`0x1427E8 → 0x141E28`; this widget has none,
`0x141ED0/0x141ED8` zero them). Then if `+0x38 & 1` it switches on `+0x18`: 0 → `0x1424E0` (stubs),
**1 → `0x142090`**, 2 → `0x142610`, 3 → `0x142608` (`jr ra`).

`0x142090(w, px, py, z)`, with `x = [+8]+px`, `y = [+0xA]+py`, `z = z+[+0xC]`:
- **Fill**: sprite `[+0x48]==1 ? 0x30 : 0x31` (`0x1420E4..0x142110`). For the box `+0x48 = 0`, so
  **sprite 0x31 = `messages\wboxfill.ssh`**. The colour is `0x2138E0(0xFF,0xFF,0xFF)`: word `0xFFFFFFFF`,
  which is 1.0 after the 1/255 normalisation. The fill is drawn in `h>>4` rows (13 for h = 208) of
  **w × 16** units, each `(x, y+16i)`, plus a remainder row (none here). The whole 16×16 texture is
  stretched across the full width once per row. It is not tiled across.
- **Corners**, sprite 0x2F `messcorner`, 8×8, white. At `(x+w, y−8)` flip (0,0), `(x+w, y+h)` flip (0,1),
  `(x−8, y−8)` flip (1,0), `(x−8, y+h)` flip (1,1). `0x213668(prim, fx, fy)` sets the packet bits 0x10
  (U swap) and 0x20 (V swap) that `0x2329C8` honours.
- **Edges**, sprite 0x32 `messedge`. Top: `(x, y−8)` w×8, after `0x213760`. Bottom: `(x, y+h)` w×8, flip
  (0,1). Right: `(x+w, y)` 8×h, after `0x213760`, `0x2136D0(2)`. Left: `(x−8, y)` 8×h, after `0x213760`,
  `0x2136D0(0)`. No colour call. The sprite record keeps white from the record frames (`0x108B40`,
  `0x2138E0(0xFF,0xFF,0xFF)` on 0x32).

So: **a nine-slice** with **`wboxfill` as its fill** and the stack's own blue `messcorner`/`messedge` as its
frame. This is the same painter as the graph panel (`findings/graph-widget.md` §1.3) with the other fill.

**`wboxcorner`/`wboxedge`: not used.** They are on the disc as `.tga` and `.ssh`, but have no sprite id, and
cow tools found no string for them in the image.

### 1.4 The art (disc, READ through the port's `Ssh`)

| file | header | pixels |
|---|---|---|
| `messages/wboxfill.ssh` (0x31) | type **0x84**, 16×16, no alpha | 5 colours, rows uniform across. Pale cyan (≈189,239,241, A 255) with a darker band on rows 7–8 (176,228,229). Tiled every 16 units down, this reads as faint ruled lines |
| `messages/Messcorner.ssh` (0x2F) | 0x85, 16×16, alpha | blue ≈(22,34,253), A 0–248 |
| `messages/Messedge.ssh` (0x32) | 0x85, 16×16, alpha | (17,30,255), A 110–248 |
| `messages/Messfill.ssh` (0x30, not used here) | 0x85, 16×16 | (17,30,255) A 110 flat |
| `wboxcorner.ssh` / `wboxedge.ssh` | 0x84 | unreferenced |

Per `findings/graph-widget.md` §1.8 (READ there): a type-0x84 texture has no GM bit 27, so it never gets
the 0x40 "sorted" flag. **wboxfill therefore goes on the opaque per-texture chain, emitted before the whole
z-sorted region, and is behind the text and the frame whatever the z.**

### 1.5 Where it sits, and depth

The HUD's 2D space is **512 × 512 units covering the whole frame** (§2.2: NDC = x/256−1, 1−y/256, and
NDC ±1 = the full frame). On PAL 640×512 one unit is 1.25 px across and 1 px down. Supporting evidence
that 512 units is the full width: `0x1FBCD0` draws sprite 0x3A at x 0, 512 wide; many centred texts sit at
x = 256 (e.g. `0x1513E0`, row 973 at (256, 128), justify 1).

**The box x comes from the 640-PIXEL width in this 512-UNIT space.** So it sits right of centre (READ
arithmetic; nothing rescales it):

| piece | units x | units y | size |
|---|---|---|---|
| text rect (widget) | 190..450 | 210..390 | 260×180 |
| fill (wboxfill) | 174..466 | 196..404 | 292×208, 13 rows |
| frame outer edge | 166..474 | 188..412 | 308×224 |
| text lines | centred on **x = 320** (62.5 % across; 400 px on PAL) | first line top **y = 210** | Small.bff, advance 21 |

It is positioned relative to the screen only (width global and constant 210). Nothing ties it to the
envelope or the records.

**Depth** (smaller z = nearer, graph-widget §1.8). Text z = 10 + `[+0xC]` = 10 (`0x141D60` zeroes `+0xC`).
Corners and edges are at z 10. The shadow, if on, is at z 18. The fill is on the opaque chain. The records
are in front: letter z 0, frame z 4 (existing findings). The envelope and count are at `[0x2AA728]` = 10
(data init; no writer found), but they do not overlap the box.

**Does Small.bff fit?** (MEASURED, eng, 95 advisor rows, split on `\r`/`\n` as `0x1D2FD0` does.) The widest
line is **288** units (message 67). 7 rows are wider than the 260 rect and **none is wider than the 292
fill**. At most 7 lines × 21 = 147 of 180. The 16-unit outward margin is what makes 288 fit. That agrees
with the READ font choice. Console.bff (the port's choice) is 217 at most; Large.bff overflows 65 rows.

French and German have 4 rows over 292 in Small. Italian, Spanish, Dutch and Swedish advisor rows contain
**no line breaks at all** in the text database (0 control characters over all 95 rows). With no wrap in
`0x20ACF8`, they would be one long centred line (INFERRED; not checked on hardware).

### 1.6 The port today (`game/AdvisorStackView.cs`), versus the console

| | port | console |
|---|---|---|
| fill | `DrawRect` flat (48,48,48), 260×180 | **wboxfill.ssh** (pale cyan, opaque), 292×208 at (174,196): margins OUTSIDE the rect |
| border | none | **messcorner 8×8 ×4 + messedge 8-wide ×4** (blue, alpha): the record-frame nine-slice, outer edge (166,188)–(474,412) |
| x | centred in the window | **190** (text rect), computed as (640−260)/2 in 512-unit space; centre at 320 units |
| face | Console.bff (MEASURED guess) | **Small.bff**, font id 0 (READ) |
| colour | white | **(48,48,48)** |
| align | left, inset (16,14) | **each line centred on x 320**; top at y 210, no inset |
| shadow | none | INFERRED black (2,2) at z+8, inherited |
| layering | not stated | fill behind everything sorted; records (z 0/4) in front of the box (z 10) |

## 2. The head's screen placement

### 2.1 Layer 4 is camera-less and orthographic (READ)

- **Registration.** `0x105DB8 → 0x17C9D8` calls `vt+0x24` of the renderer `0x310D48` (`0x230A88` returns
  it) with `(renderable, 4, −1, −1, −1, −1)` (`0x17CA54`: `a2 = 4`). The register routine is `0x226040`.
  For a layer ≠ 0xFF it pushes onto the list at renderer `+0x2DC + 4·layer`.
- **Per-layer flags.** The renderer ctor `0x225838` sets the words at `+0x30C + 4·layer` to:
  layers 0,1 = 0; **2,3,4,5,6 = 0x200000**; 7,8,9 = 0; 10 = 0x200000; 11 = 0 (`puVar1[0xC3..0xCE]`). It
  sets the layer mask `+0x348 = 0x7FFFF61F`, so layers 0–4, 9 and 10 are on and layer 4 is on.
- **The layer loop** (`0x225BF8`, called each frame by `0x225FC8`). For each layer i < 12 whose mask bit is
  set, it calls each renderable's `vt+0x14` with the flags `+0x30C + 4i`. The model renderable class is
  `0x36F360` (ctor `0x227158`), and its `+0x14` slot is **`0x2282E0`**.
- **`0x2282E0`** chooses the object-to-clip matrix. With flag `0x200000` it copies **`0x2F0400`**
  (`0x21E8B0(0x3110D0, 0x2F0400)`). Without the flag it uses instance world `+0x38` × `0x2F0380`
  (view-projection). Layer 4 therefore uses **no camera and no perspective**. The advisor's look-at
  camera at `adv+0x118` plays no part in this path.
- **`0x2F0400`** is built by `0x21F0B0` (from `0x21EE00`): identity with
  `m00 = [0x2EFA30]/[0x2EFA28] = W/3600` and `m11 = −[0x2EFA34]/[0x2EFA2C] = −H/3600`.
  `0x21AA18` stores W and H at display `+0xC0/+0xC4`. `0x21A7D8` stores 3600.0 at `+0xB8/+0xBC`, 2048.0 at
  `+0x28/+0x2C` and 1800.0 at `+0x30/+0x34`.
- **The hierarchy is multiplied at draw time.** `0x2280E0` computes child = `local(+0x10) × parent` via
  `0x21E930(out, local, parent)` (row vectors, translation in the 4th row), starting from
  `*(submodel+0x70)`.

**INFERRED (strong): NDC ±1 spans the full frame.** Take the GS step as `2048 + 1800·clip`: 1800·W/3600 =
W/2 and 1800·H/3600 = H/2. The 3D projection `0x21EF60` carries the same W/3600 and H/3600 factors. The
512-unit sprite space fills the frame (§1.5). The VU1 microcode that applies 2048/1800 was not read. So:

`px = W/2 · (1 + X)`, `py = H/2 · (1 − Y)`, with world +Y up. HUD units: `u = 256(1+X)`, `v = 256(1−Y)`.

### 2.2 The root transform (READ)

`0x1066B0` runs every tick (MIPS `0x1066D4..0x106744`). `a1 = *(inst+0xC)` is the sub-model, and
`*(a1+0x70)` is its matrix-tree root. It writes translation `+0x40/44/48 = (0.6, −0.5, 0)`
(`0x2AA714/718`), then calls `0x16FD18(f12 = s, f13 = 4s/3, f14 = s, M+0x10)`, s = 0.013 (`0x2AA71C`).
Then it calls `vt+0x6C`.

`0x16FD18` **normalises each row of the 3×3 and rescales it to the given length.** Rotation is kept and the
scale is REPLACED. The port's inference "replaces the root's 0.02" is right.

The tree root is `Position Dummy`, the only parentless node in `advisor.mps`. Its bind rows are
(0.02,0,0), (0,0,−0.02), (0,0.02,0): local y → world −z, and local z → world up. **So the 4/3 on row 1
scales DEPTH**, which an orthographic layer never shows. On screen both axes get 0.013 of NDC. No record in
`advisor.aps` animates node 32 (Position Dummy); all 15 were listed. So the write sticks.

### 2.3 Numbers

Origin: NDC (0.6, −0.5).

| frame | origin px | fraction | HUD units |
|---|---|---|---|
| PAL 640×512 (this ELF) | **(512, 384)** | 80 % across, 75 % down | **(409.6, 384)** |
| NTSC 640×448 | (512, 336) | same | same |

Size, in the bind pose (= the "up" end of the enter record, and the talk pose):

| mesh set | PAL px x | PAL px y | HUD units |
|---|---|---|---|
| `Bug Head` disc (r = 10.75 local → 0.1398 NDC) | 467.3–556.7 (**89.4 wide**) | 348.2–419.8 (**71.6 tall**) | x 373.8–445.4, y 348.2–419.8 (71.5 × 71.6) |
| default dress (head, eyes, Body, both hands, plain antennae) | 416.7–607.1 | 311.6–454.5 | x 333–486, y 312–455 |
| NTSC 448: the disc is 89.4 × 62.6 px | | | |

The disc covers 14.0 % of the width and 14.0 % of the height. **On a 4:3 display it is an ellipse 4/3
wider than tall** (square in HUD units, stretched by the frame). The figure stands upright with +Y up and
faces the viewer. Its right eye lands on the screen left (x 486–509), and the face is on −z, which is
nearer (smaller z).

The frame's pieces overlap the head: x 333–474, y 312–412 units. Models and HUD sprites go into the same
render list `0x311160` (`0x22A068` from `0x227F88`/`0x228080` and from `0x2329C8`), emitted by `0x22AE80`
(§1.8 rules). Whether the box draws over the head when both are up was **not resolved**.

### 2.4 The port's inference, checked

- **Position: confirmed.** Console (410, 384) in HUD units = (512, 384) px PAL.
- **Scale: partly wrong.** Replacing 0.02 with 0.013 is right. **Putting 4/3 on the screen vertical is
  wrong.** In the port's frame (±1 vertical, ±aspect horizontal), the console is 0.013 vertically and
  0.013·aspect horizontally: 0.01733 at 4:3. The port applies 0.01733 on both axes, so **its head is the
  right width at 4:3 but 4/3 too tall**. The console head is a squat ellipse, not a circle.
- The enter rise (52 local units) = 0.676 NDC = 173 px on PAL. That starts the disc (top 348) at y ≈ 521,
  just below the 512 edge. INFERRED; the track values are the port's.

## 3. Still unknown (what was tried)

- **The GS step from clip to pixel** (2048 ± 1800) is INFERRED from the display floats and three
  consistency checks. The VU1 microcode was not read.
- **The box text's shadow** depends on unbalanced `0x20B250` callers that run earlier in the same pass;
  frame order was not traced. Its default is on.
- **How the glyph rasteriser uses the colour word** (`obj+0x4C`, surface `vt+0x24` under `0x1D34F0`) was
  not read. (48,48,48) opaque is INFERRED.
- **Head versus box draw order** where they overlap: both use list `0x311160`; the head textures' sort flag
  was not read.
- **Lighting for layer 4.** `0x2282E0` selects light block `0x3111C0` for `0x200000` without `0x400000`
  (else `0x311180`); both are built in `0x21EA20`. Not decoded.
- **Depth test for layer-4 models** against the park (layers 0–3) was not read.
- **`widget+0x54`**, whose value +0x80 goes to `0x2AA72C`, has no writer or reader found (`lui/offset`
  scans).
- **The non-English advisor rows without line breaks** (ita/spa/dut/swe): no wrap exists on this path; how
  the retail game shows them was not checked.

## 4. The port, after this step (2026-09-28)

§1.6's "port" column is the port before this step. Code: `game/AdvisorStackView.cs` (`ReadBox`),
`game/AdvisorHead.cs`, `game/UiPanel.cs`, `game/Viewer.Advisor.cs` (`AdvisorBoxFont`). Checks:
`game/tests/AdvisorSmoke.cs`, `tools/advisor_view_teeth.py`.

### 4.1 The read box

As §1.3 to §1.5: `wboxfill` (sprite 0x31, `UiPanel.Sheet`) in 13 rows of 292×16 from (174, 196), each row the
whole tile stretched across; `messcorner` 8×8 at the four corners with §1.3's flips; `messedge` 8 wide on the
four sides; outer edge (166, 188)–(474, 412). The text is `Small.bff` (font id 0) in (48,48,48). Each line is
centred on x 320, the first line's top is at y 210, and the lines are Small's own advance (21) apart. Lines
break on `\n`, `\r`, `\r\n` and `\n\r`, with no wrap. The draw order is the fill, the text's shadow, the
frame, the text, then the records. `wboxcorner`/`wboxedge` stay unused.

**READ in this step: the side edges' turn.** `0x2136D0(prim, mode)` (MIPS `0x2136D0..0x213758`) clears
packet flags `0x1030` and then sets mode 0 → `0x1020`, mode 1 → `0x30`, mode 2 → `0x1010`, any other → nothing.
`0x213760` zeroes the flag word. In `0x2329C8` (`0x232B84..0x232C40`), `0x10` swaps U, `0x20` swaps V, and
`0x1000` exchanges the UVs of corners 1 and 2. The unflipped UVs put (u0,v0) on corner 2, (u1,v0) on 3,
(u0,v1) on 0 and (u1,v1) on 1, so modes 0 and 2 are the two opposite quarter turns.

**INFERRED: which turn is which.** Take corner 2 as the top left, which is what an unflipped sprite drawn
upright means. The corner piece drawn raw at the top right then shows its measured cut
(`UiPanel`: the top-right quadrant) facing outward. With that, mode 2 is a clockwise turn: `messedge`'s band
(its top row) lands on the right. Mode 0 is counter-clockwise: the band lands on the left. So each side
edge's band faces outward. These are `UiPanel.EdgeRight`, which the record frames' mode-2 edge already used,
and `UiPanel.EdgeLeft`. The sprite record's v0/v1 values were not read.

**⚠ The mapping is the port's HUD rule** (`Viewer.ShowMoney`). A POSITION is a fraction of the window, and a
SIZE goes by one factor from the height, k = H/512. The box hangs off its centre line x 320, which is a
position. The console stretches its 512-unit space over the frame: on PAL 640×512 a unit is 1.25 px across,
so its fill is 365 px wide. The port does not stretch: the fill is 292 px wide at 640×512.

**MEASURED on the disc (2026-09-28):** the English table (`eur/eng.dat`) uses LF only. 242 of 1,087 rows
carry LF and none carries CR. So the smoke never exercises the CR paths of the line break.

**MEASURED on screen** (AdvisorSmoke, JUNGLE 1):

| window | frame outer edge | text centre | fill probe at (320, 390) units |
|---|---|---|---|
| 640×360 | (291.7, 132.2)–(508.3, 289.7) px | x 400 | (190,238,241) |
| 640×512 | (246, 188)–(554, 412) px | x 400 | (191,238,241) |

### 4.2 The head

The pivot now applies `0x16FD18(s, 4s/3, s)` to the ROOT's own local rows, as
Q = R·diag(s/|r0|, (4s/3)/|r1|, s/|r2|)·R⁻¹. R is the root's bind rotation. Q is carried through the model's
mirror root. The root's origin is set to NDC (0.6, −0.5), which is (0.6·aspect, −0.5) in the port's frame
(±aspect across, ±1 down). The root's bind origin is (0, 0, 0), so nothing is taken back out (logged in the
head's summary). The 4/3 therefore lands on the root's local y, which is depth.

Before this step the port scaled the whole model uniformly by 4s/3 = 0.01733 ("4/3 on the vertical, both
axes take the vertical factor"). **Its disc was a circle 4/3 too big on BOTH axes, not taller than wide.**

MEASURED off the drawn nodes: the `Bug Head` vertices through its surfaces' global transforms and the
overlay's camera, and the root's origin the same way.

| | window | disc w × h px | h/w | origin px | root axes x / y / z | local y on the view axis |
|---|---|---|---|---|---|---|
| before | 640×360 | 67.04 × 67.04 | 1.000 | (512, 270) | 0.01733 each | 1.000 |
| after | 640×360 | 50.28 × 50.28 | 1.000 | (512, 270) | 0.013 / 0.01733 / 0.013 | 1.000 |
| after | 640×512 | 71.51 × 71.51 | 1.000 | (512, 384) | 0.013 / 0.01733 / 0.013 | 1.000 |

The research gives a disc 71.6 units tall with its origin at (409.6, 384) units = (512, 384) px on PAL. At
640×512 the port's disc spans y 348.2–419.7 px (research: 348.2–419.8). Across it is 71.5 px, where the
console's framebuffer has 89.4, because the port does not stretch.

**Control.** The envelope, authored 40×40 at (32, 420), draws 28.13 × 28.13 px at 640×360 and 40 × 40 at
640×512: h/w 1.0000. So the HUD mapping carries no pixel-aspect factor. The old head's 4/3 was the head's own
transform.

### 4.3 Checks

`AdvisorSmoke` gains 11 checks, from 49 to 60 per park; the viewer matrix floor moves from 47 to 58. Each reads
the view's output. The envelope's drawn rect is the control. The head's `Bug Head` vertices and root node are
projected through the overlay's camera, and the root's axes are read in the overlay's world. The box's
recorded blits are checked for texture identity, rect and colour, and the drawn textures' own pixels against
§1.4's art. The text's line widths are checked against `Small.bff`'s advances, which the test reads off the
disc itself. One pixel of the rendered frame is sampled in the fill's bottom margin.

`tools/advisor_view_teeth.py` gains 19 mutations, from 20 to 39. **All 39 went red on JUNGLE 1.** The new ones:

| mutation | red at |
|---|---|
| `box-face-messfill`, `box-margins-inward`, `box-centred-in-window`, `box-fill-one-rect` | the face check |
| `box-no-frame`, `box-left-edge-unturned` | the frame check |
| `fill-drawn-last` | the depth-order check |
| `fill-recorded-not-drawn` (the blits recorded, the fill never drawn) | the framebuffer pixel, which read the park's grass (69,90,2) |
| `box-font-console`, `text-white`, `text-left-aligned`, `text-inset` | the text check |
| `text-no-shadow` | the ⚠ inferred shadow's check |
| `envelope-stretched` (a pixel-aspect factor in the mapping) | the control: 50 × 28.13 px |
| `head-4-3-uniform` (the old transform) | the disc: 67 × 67 px against 50.3 |
| `head-4-3-on-screen-y` | the disc: 50.3 × 67 px, h/w 1.333 |
| `head-no-depth-4-3` (no change on screen) | the root-axes check |
| `head-anchor-no-aspect` | the origin: (428, 270) px against (512, 270) |
| `head-anchor-y-on-depth` | the origin: (512, 180) px |

No mutation covers the line break's CR paths: the English rows carry none (§4.1), so none could go red.

### 4.4 Still unknown

- **Whether the box draws over the head where they overlap**: the port draws the box over it (the head is
  under the whole HUD). Both use list `0x311160`, and the head textures' sort flag was not read.
- **The step from clip to pixel** (2048 ± 1800), and the rest of §3.
