# The shop's config screen: the Laptop

Master: "can u research the info UI? lets do the shop config info ui", then "keep searching and
figure it out", then — on the first attempt — **"this isnt the right ui at all … u have the
correct ui CONTENT but its the wrong ui panel."**

Evidence is this disc: `SLES_500.32` (vaddr = file offset + `0xFF000`), `MENUS.WAD`, `UI.WAD` and
`DATA.WAD/Text/translations/`. Static analysis, not a console trace.

## ⚠⚠ The correction that matters

The first version of this screen was a **260x180 message box** built from `/messages/Mess*` —
the same nine-slice the little object menu uses — with the shop's numbers typed into it. The rows
were right. The furniture was not.

The real screen is what the executable itself calls the **Laptop**: `'Laptop is %s'` /
`hidden` / `visible` at `0x363950`, loading `Data/Ui/Laptop/` + `laptop_jungle.ssh`,
`laptop_hallow.ssh`, `laptop_fantasy.ssh`, `laptop_space.ssh` (`0x36d218..0x36d270`). All four are
preloaded by `FUN_00214c20` and picked per world. It is a full 512-wide screen with a photographic
backdrop and the shop's model spinning in a window on the right.

**How the wrong answer survived:** the layout globals near `0x2e9e00` read **0** in the executable
image, which was written up as "written at init, unknowable". They read 0 because a **file** fills
them — see below. Nothing in the executable was ever going to answer the question, so a
panel-shaped pair of numbers found nearby got adopted instead.

## ⭐⭐ The layout is DATA, in MENUS.WAD

`MENUS.WAD` is 29 **plain-text** `.sce` files, one per menu screen — `main_i_ride`,
`main_i_shop`, `main_i_bathroom`, `main_i_sideshow`, `main_fi_*`, `main_ps_*`, `main_gameoptions`,
`main_research`, … The shop's configuration screen is **`main_i_shop_data.sce`**:

| uielem | row | col | size | justify |
|---|---|---|---|---|
| `ItemText` (shop name) | 115 | 45 | — | left |
| `textoptions` (labels) | 175 | 45 | — | left |
| `NumericItems` (values) | 175 | 250 | — | center |
| `satisfactionbar` | 305 | 215 | 72x22 | |
| `qualityslider` | 338 | 215 | 72x22 | |
| `additiveslider` | 370 | 215 | 72x22 | |
| `CostItem` (sale price) | 400 | 250 | — | center |
| `CostItemArrows` | 412 | 190 | — | |
| `Model` (3D spinning model) | 208 | 315 | 147x240 | |

Its own comment names the rows: *"customers/CostOfGoods/takings/profit/satisfaction/drinkquality/
(additive)ice/SalePrice"*, and for the value column *"Customers/CostofGoods/Takings/Profit -
int/$/$/$"*.

The chain, all of it verified:

```
MENUS.WAD/main_i_shop_data.sce
  -> FUN_001d68b0                     the binder, menu id 0x11
       FUN_001fc508(mgr, 0x11)        load the scene
       FUN_00177978(scene, name)      find a uielem BY NAME, case-insensitively
                                      (the file says `textoptions`, the exe `TextOptions`)
       FUN_00177ac8(scene)            -> the frame, as [row, col, width, height]
       FUN_00177b08(scene)            -> the justification
  -> globals 0x2e9c48 .. 0x2e9cec
  -> FUN_001d70c8                     the draw
```

⭐ **The frame accessor's element order is established, not assumed.** The binder stores each
element into globals at ascending addresses, and for all nine the result is a consistent
`{x = col, y = row, w, h}` rect — e.g. `SatisfactionBar` puts `f[1]` at `0x2e9c50` and `f[0]` at
`0x2e9c54`, so `f[1]` is the x. Nine independent elements agreeing on one interpretation is the
evidence; one element would not have been.

The sibling screens bind the same way with their own element lists, at
`0x3590c8` (`main_i_ride`: `InfoText, ItemSelect, ItemSelectArrow, InfoValues, satisfactionbar,`
**`ExcitementBar`**`, Model`), `0x3690a0` (bathroom: … **`CleanlinessBar`** …) and `0x364dc0`
(`main_bh_items`: … **`ExcitementSlider`, `ReliabilitySlider`** …).

## ⭐⭐ The row step, corroborated across files

`DAT_002e9ca8` = **32**, added after every label and every value in `FUN_001d70c8`.

It is a **constant in the image**, unlike its neighbours: `0x2e9c48`, `0x2e9c4c`, `0x2e9c60` and
`0x2e9c64` all read 0 because the binder writes them, and a cross-reference sweep finds **no store
to `0x2e9ca8` anywhere**. That negative was given a control — the same sweep *does* find the store
to `0x2e9c48` — so it is a real negative and not a limitation of the tool.

And it is corroborated **across two different files**. Stepping from the scene's own first label
row by 32 predicts where the widgets go; the scene independently places them:

| row | predicted (exe) | `.sce` | delta |
|---|---|---|---|
| Satisfaction (5th) | 303 | 305 | +2 |
| Quality (6th) | 335 | 338 | +3 |
| additive (7th) | 367 | 370 | +3 |
| Sale Price (8th) | 399 | 400 | +1 |

The small positive deltas are a 22-high widget being centred against a text baseline. A wrong step
misses by far more — 28 by 29px, 30 by 15px, 34 by 13px, 36 by 27px — which is the control that
makes the agreement mean something.

## The draw, `FUN_001d70c8`

**Labels**, by text-table row, in draw order:

| # | id | key | English |
|---|---|---|---|
| 1 | 82 | `STR_SINGLESHOP_CUSTOMERS` | Customers |
| 2 | 17 | `STR_SINGLESHOP_COST_OF_GOODS` | Cost of Goods |
| 3 | 106 | `STR_SINGLESHOP_MONTHLY_TAKINGS` | Takings |
| 4 | 986 | `STR_SINGLESHOP_MONTHLY_PROFIT` | Profit |
| 5 | 1074 | `STR_SINGLESHOP_CUSTOMER_SATISFACTION` | Satisfaction |
| 6 | 312 | `STR_SINGLESHOP_QUALITY_OF_GOODS` | Quality |
| 7 | *per shop* | `STR_SINGLESHOP_{FAT,ICE,SUGAR,SALT}` | Fat / Ice / Sugar / Salt |
| 8 | 594 | `STR_SINGLESHOP_SALE_PRICE` | Sale Price |

Every label is a `STR_SINGLESHOP_*` row — the game's own name for this screen.

⭐⭐ **Row 7 keeps its slot when the shop has no ingredient.** The label is inside
`if (FUN_001d1f60(shop) != 0)` but the row step that follows it is **outside** — so a shop with no
additive leaves a **gap** and Sale Price stays on row 8. Dropping the row instead would slide Sale
Price up a whole 32 units. `FUN_001d1f60` is the ingredient chain already written up below.

**Colours**, from `FUN_001388e8(ctx, r, g, b)` — there are exactly two, and the title shares the
selected row's:

| | rgb |
|---|---|
| title, selected label, all values | **255, 255, 0** |
| unselected label | **200, 130, 0** |

**Values**, at `NumericItems`, four only: `FUN_00142b68` formats the first as an **integer** and
`FUN_00142908` the other three as **money** — the int/$/$/$ the scene file states. They come from
the shop's own accessors: `FUN_001d1b08` cost of goods, `FUN_001d1da0` takings, `FUN_001d1d98`
profit, `FUN_001d1e00` satisfaction.

**Widgets**: the satisfaction bar's full scale is `*(screen + 0x9fc)` = **100**. The quality
slider is drawn unconditionally; the additive slider **only when the shop has an additive**.
`*(screen + 0x9cc)` is the selected row and drives both the yellow label and which slider is live
(0 quality, 1 additive, 2 sale price).

## The interaction, `FUN_001d6f68`

Up/down (`FUN_00181700` bits 0 and 1) move `screen[0x9cc]`, each playing sound `0xd6`, and it
**wraps**: below 0 goes to the limit, above the limit goes to 0.

⭐⭐ The limit is `FUN_001d1f60(shop) == 0 ? 1 : 2` — **2 rows when the shop has an additive, 1 when
it does not**. So the additive is skipped by the CURSOR as well as by the drawing; this is the
third place the same ingredient test appears, after the label and the slider.

Write-back, each frame: `FUN_001d1f58(shop, screen[0xa82])` sets quality, `FUN_001d1fc0(shop,
screen[0xbba])` sets the additive, and `shop[0xb8] = screen[0xd2c]` sets the sale price. Those
`+0x2e` offsets inside each widget are its value, which is also what `FUN_001daa88` copies.

⭐ The bar and the sliders are **one widget class**: `FUN_001daa88` (slider) copies its geometry
into a sub-object and calls `FUN_00115590`, the same painter the bar uses. Their field layout,
confirmed against both the draw and `FUN_00115530`'s fraction, is `+8` x, `+0xa` y, `+0x14` w,
`+0x16` h, `+0x18` value (<<16, so `+0x1a` is its integer part), `+0x1c` min, `+0x20` max,
`+0x2c` the sprite.

## Satisfaction, `FUN_001d1e00`

```
if (<vtable call> == 0) return 0;
if (shop[0xb4] == 0) trap(7);
return min(100, shop[0xb0] / shop[0xb4]);
```

A **running mean**: an accumulator over a count, clamped to 100. So filling this bar needs the port
to accumulate a per-customer rating, which it does not yet do — hence the empty bar rather than an
invented number.

## Cost of goods, `FUN_001d1b08`

```
base = *(u16*)(dbaPayload + 0x2e)
return base * ((shop[0xba] >> 2) + 0x4b - (shop[0xac] >> 2)) / 100      ; 0x4b = 75
```

⭐ Quality is `shop[0xba]` (u16) and the additive `shop[0xac]`. ⚠ The additive's shift rounds
**toward zero** (`v < 0 ? v + 3 : v` before `>> 2`), where C#'s `>>` rounds toward negative
infinity — a one-off difference that cannot arise while the slider stays in 0..100, but is a real
difference in the expression.

## The art, `UI.WAD/laptop/` (62 files)

| file | what |
|---|---|
| `LAPTOP_{JUNGLE,HALLOW,FANTASY,SPACE}`, `LAPTOP_512` | 512x512 chrome, per world |
| `BARSLIDE` 128x32 + `BARKNOB` 32x32 | yellow track + gold knob — the sliders |
| `PROG_BAR` 128x32 (notched) **or** `BARPROG` 128x32 (smooth) | the satisfaction bar's frame — ⚠ **which one is UNRESOLVED**, see below |
| `PROG_CBIT`/`PROG_VBIT`/`PROG_WBIT` 16x32 | the cyan cap / cyan body / white segments that fill a bar |
| `L_corner1-3`, `L_edge1-4`, `L_fill`, `L_Panel1-9`, `L_Panelfill` | the laptop's own inner nine-slice |
| `BUTTX`, `BUTTCIRCLE`, `BUTTSQUARE`, `BUTTTRIANGLE` | the face-button glyphs |
| `AWARD_*` | medals and stars |

⭐ Most of these ship as **`.tga` as well as `.ssh`**, so the art can be read without the SSH
decoder — but the `L_*` nine-slice does **not**, and see the gap below.

⭐ **The face is `Large.bff`**, corroborated rather than chosen: its line advance is **30** against
the 32 row step. Small (21) and Console (14) would leave holes.

## ⚠ Known gaps

* ~~Eight `.ssh` images do not decode~~ — **fixed**, and the answer changed what the port should
  draw. All eight (`L_edge1-4`, `L_Panel2`, `L_Panel5`, `L_Panel6c`, `L_Panel7`) now decode, and
  they are blue bevel gradients in matched orientation pairs. Zero regression: the 400+ pair
  scorer returns an identical 3542 of 5764 within tolerance and an identical image-weighted RGB
  mean of 4.907867, before and after.

  ⭐⭐⭐ **BUT THE GAME NEVER LOADS THEM.** `ctex_ssh::load` at `0x235d68` — the sole reference to
  the executable's `"SHPS"` string — refuses the whole file if **any** entry is under 16 in either
  dimension:

  ```
  if (entryWidth < 0x10 || entryHeight < 0x10) {
      printf("*** ERROR ctex::load - mipmap too small (%d,%d)\n", w, h);   ; 0x36fe20
      <cleanup>; return 0;                                                 ; the LOAD FAILS
  }
  ```

  So **11 of the 62 laptop files are dead assets on this build**: the eight above plus `L_fill`,
  `L_Panelfill` and `AWARD_T_8`, all 8x8. The laptop's inner nine-slice therefore **cannot** be
  drawn from `L_edge*`/`L_fill` on the PS2 — whatever it draws uses only the 30 pieces that are
  16 or larger. The port should not reinstate them.

  ⭐ This also reframes the decode itself: since no shipped code path ever displays these bytes,
  the engine **cannot** settle their layout and the encoder's output is the only authority. The
  rule was established against controls rather than guessed — the three 8x8 files that do have
  `.tga` partners score RGB MAE 10.0 / 4.1 / 5.7 read contiguously against 50.6 / 36.5 / 34.9 for
  a top-left crop, and the 8x32 tiles are corroborated against their 32x8 siblings (which have no
  layout choice) at luma MAE 0.7 / 3.3 where a crop scores 34 / 44 and unrelated tiles score 31.
  Alpha is a *different* layout from RGB, verified byte-for-byte: coded row `y` columns 8..15
  equal row `y+1` columns 0..7 in all six 8-wide alpha files (248 of 248 pairs per 8x32).
* **⚠ Which frame the satisfaction bar uses is NOT established.** There are two 128x32 orange
  frames, `PROG_BAR` (notched) and `BARPROG` (smooth). The port currently draws `PROG_BAR`, chosen
  **by name** on the assumption that its notches were the fill-segment dividers — and that
  assumption is measurably false: the notches sit **11px** apart while the `PROG_*BIT` fill
  segments are **16px** wide, so they do not correspond. `BARPROG`'s interior measures completely
  empty. The frame is bound through the laptop **sprite registry** (`DAT_002eeff0`, 62 records of
  24 bytes — exactly the 62 files in `UI.WAD/laptop/`), which reads all zeros in the image because
  it is runtime-populated, so the index cannot be resolved statically. Master was asked to point.
* **Satisfaction is not tracked.** `shop[0xb0]`/`[0xb4]` is decoded but the port does not yet
  accumulate it, so the bar renders empty rather than showing a number that was never computed.
* **The `Model` window** (147x240 at col 315) is not drawn yet. ⚠⚠ And it is **not a 3D viewport**:

  ```
  FUN_0017e150(screen + 0x950, obj)  ->  FUN_00144068(widget + 0x38, *(u32*)(obj + 0x58))
  FUN_0017e190(screen + 0x950)       ->  FUN_002127e8(widget + 0x38, x, y, 0x60)   ; place
                                         FUN_00212828(widget + 0x38, w, h)          ; size
                                         FUN_00212838(widget + 0x38)                ; draw
  ```

  ⚠⚠ THOSE THREE CALLS ARE A GENERIC "PLACE, SIZE, DRAW" TRIO, NOT PROOF OF A SPRITE. An earlier
  version of this section said the element was "a flat sprite blit", because `FUN_00115590` draws
  the satisfaction bar with the same three. That was reading the calls that answered the question
  and stopping. `FUN_00144068`, the fourth call, is where the answer actually is:

  ```
  if (widget[0x30] == 0 || widget[0x38] != value) {     ; cached on the VALUE
      FUN_00144168(widget);                             ; tear the old one down
      obj = FUN_00230a98();                             ; CREATE an object
      widget[0x30] = obj;
      obj->vtable[0x24]->[0x0C](obj, value, 0x10, -1);  ; initialise it FROM the value
      widget[0x38] = value;  widget[0x34] = obj[0x0C];
      FUN_0016FEA0(obj[0x0C][0x70] + 0x10, 0x2B68A8);
      FUN_0017D1D8(obj, 1);
      obj->vtable[0x24]->[0x5C](1.0f, obj, 5, 0, 0, 1); ; 0x3F800000 = 1.0, and a 5
      FUN_0017CCA8(obj);  DAT_002B68AC = 1;
  }
  ```

  ⭐ So `shop + 0x78` holds an **id**, not a picture: the widget instantiates a real object from it,
  configures it, and caches on the id so it only rebuilds when the shop changes. That fits master's
  description -- "just a front facing render of it. playing an animation" -- far better than a blit
  does, and it means the port needs a real model in a viewport rather than a texture.

  ⚠ NOT established, and deliberately not guessed: what `FUN_00230a98` creates, what the `0x10`
  and `-1` mean, and whether the `5` in that last virtual call is the animation master remembers.
  The float 1.0 beside it looks like a scale. Naming them would need those two vtables walked.

  ⚠ WHAT IS STILL UNREAD: what *fills* that sprite. A front-facing render of the model playing an
  animation has to be produced somewhere upstream and handed to `obj + 0x58`; that producer, and
  which animation it plays, are not traced. So the port needs a render-to-texture whose contents
  are not yet specified — knowing the UI blits a sprite does not tell you what is in it.
* The laptop **sprite registry** at `DAT_002eeff0` — 62 records of 24 bytes, exactly the file count
  in `UI.WAD/laptop/` — reads all zeros in the image, so it is runtime-populated and its
  index-to-file mapping is **not** established. `FUN_00214c20` sets sprite 57 to `0x60ffffff` and
  58 to `0x60808080`, i.e. alpha `0x60`, but which files those are is unread.

---

# The special ingredient

`0x1d1f60`:

```
id = shopIngredientId()                        ; 0x1d1d08, one byte at DBA payload +0x30
if (id == 2 || id == 3 || id == 6) return 0    ; no special ingredient
return *(u32*)(0x2E9A78 + id * 4)              ; -> text-table index
```

A flat `u32` array indexed by id, with three ids short-circuited **before** the load. The id is DBA
data, read through the payload accessor `0x10fa30`.

⚠ This table was nearly written off as "runtime-populated, nearly all zeros". It is **static and
always in the file** — the zeros are the array's unused slots, and the 12-byte stride that made it
look like a runtime structure was invented. Verified against the shipped `arsdb.dba`: key 241 (the
JUNGLE Coconut) reads 01 = Ice, 243 = 07 Salt, 245 = 04 Sugar, and 242/244 = 02/06, which are
exactly the guarded ids — so the guard is live code rather than something read into it.

⭐ Confirmed **in place** by the draw: `FUN_001d1f60` is what supplies row 7's text id, and the row
is skipped when it returns 0.
