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

## ⭐⭐ Checked against the real screens (2026-09-25)

Master supplied screenshots of three live laptop screens -- the JUNGLE **Drinks Shop**
(`main_i_shop_data`, the one this file describes), a **Crazy Ape** ride
(`main_i_ride_data`) and an **Arcade** sideshow (`main_i_sideshow_data`). They are the only
ground truth in this document; everything else is static analysis.

**Confirmed, byte for byte:**

| claim | the real screen |
| --- | --- |
| row order, and the ingredient row | Customers / Cost of Goods / Takings / Profit / Satisfaction / Quality / **Ice** / Sale Price |
| labels orange, the selected one yellow | "Quality" yellow against the rest orange |
| satisfaction is a bar: orange frame, cyan fill, FLAT leading edge | frame **(255,162,4)** against `BARPROG`'s (255,157,0); fill **(21,227,212)** against `PROG_VBIT`'s (5,223,211) |
| `BARPROG`, not the notched `PROG_BAR` | no notches on any bar, on any of the three screens |
| an empty bar is normal | the Arcade's "Excitement" bar is drawn completely empty |

**Refuted -- both were inferences presented as decodes:**

* **The slider track is NOT tinted.** The brightest pixels in a slider row are **(242,246,26)**,
  the track's own yellow outline, and `BARSLIDE`'s art already carries it (most saturated yellow
  **(255,254,0)**). A green-tinted track cannot produce that.
* **The knob does NOT change with selection.** On the Drinks Shop the Quality knob -- the row
  being adjusted -- averages **(47,166,14)** over 2145 green pixels and the Ice knob **(49,167,14)**
  over 2149. The Arcade repeats it: its selected row, "Winning Chance", also has a green knob.
  ⚠ So `FUN_001DA938`'s branch on bit 0 (`+0xA8` against `+0xA4`) is NOT the selected/unselected
  split it was read as. Left undecoded rather than guessed at twice.

⭐ The pattern worth keeping: every claim that came from MEASURING held, and every claim inferred
from recognising a branch was wrong.

**Still missing from this port, visible in the references:** the `◀▶` arrows beside every
adjustable numeric row (the Arcade has two), the face-button legend in the chrome's notch
(triangle Back, square Mainmenu, circle Close, with a blank cross), and the model.

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

  ⭐⭐ AND THE `5` IS THE ANIMATION. tinyclaw had walked this chain already; each link below was
  re-checked here against the executable rather than relayed:

  * `FUN_00230A98` is a **factory** -- `new 0x4C`, constructor `0x227158` installs vtable
    `0x36F290`, and it returns `obj + 0x0C`.
  * That vtable's slot at `+0x58`/`+0x5C` is `{adjust -12, fn 0x228958}` (verified: the word at
    `0x36F290 + 0x5C` is `0x228958`), and `0x228958` reaches `0x17C5D8`, the play-animation router.
  * `0x17C5D8` saves its arguments (`s1 = a0` the object, `s2 = a1` the `5`, `s6` the trailing
    flag) and switches on the visual's **type** at `+0x18` through the jump table `0x362AC0`
    (materialised at `0x17C630..38`, bounds-checked to 16 arms).
  * **Types 6 and 7** land at `0x17C6F0`, which calls `0x10E910(a0 = s2, a1 = obj[0x14],
    a2 = s6)`. Against that function's signature `(logical, handle, flags)` the **logical is `a0`,
    i.e. `s2`, i.e. the `5`** -- so the `5` is **logical 5**. ⚠ Not `a1`: `a1` is reloaded from
    `obj + 0x14` and is the model handle. The conclusion is unaffected but the register is not.
  * And logical 5 in the `0x2AAD48` table is descriptor `0x2AA9C8`, count 1, main pair
    **slot 6 / variant 0**, with first and last both the inactive sentinel (verified here).
  * ⚠⚠ THE `1.0` IS NOT CONSUMED ON THIS PATH. `0x17C5D8` copies `f12` into `f20`
    (`mov.s f20, f12` at `0x17C61C`) and the types-6/7 arm never reads it -- there is no COP1
    instruction at all in `0x17C6F0..0x17C740`. So it is neither a scale (my guess) nor a speed
    (the first shortcut I was given, which tinyclaw withdrew and I had already propagated). What
    it does in the OTHER type arms is unread.

  So the shop's model plays **APS section 6, variant 0** -- which is the answer to master's
  "playing an animation (cant remember which)". No speed is set on this path.

  ⚠ CONDITIONAL ON THE TYPE, and that matters: only types 6 and 7 take the `0x10E910` arm. Other
  types take arms nobody has read, so a port must check the visual's type at `+0x18` before
  assuming this path.

  ⚠⚠ A STALE PARAGRAPH LIVED HERE and is worth recording rather than quietly deleting. It said the
  element was a sprite, that "what fills that sprite" was unread, and that the port needed a
  render-to-texture. All three were wrong and had already been corrected ABOVE in this same file --
  the correction was written and the superseded claim was left standing underneath it. A document
  that contradicts itself is worse than one that is merely out of date, because both halves look
  equally authoritative.

  ⭐⭐ WHAT THE ID IS, AND HOW IT BECOMES A MODEL. The value at `shop + 0x78` is an **asset id**:

  ```
  FUN_00144068(widget, id)
    -> obj = FUN_00230A98()                 ; new 0x4C, ctor 0x227158, vtable 0x36F290
    -> vtable slot +0x08, fn at +0x0C = FUN_00228868
         -> FUN_0017BFF8(obj + 0x0C, id, 0x10, -1)
              renderable[4]    = id
              index            = FUN_0017D7E8(id)    ; -> resource index, or -1
              renderable[0x10] = record[0x1C]        ; the handle
              renderable[0x18] = 0x10                ; the caller's flag overrides record[0x04]
  ```

  ⚠ The vtable's slots are EIGHT bytes, `{short adjust, fn}`, so `vtable + 0xC` is the FUNCTION of
  the slot at `+0x08`, not a slot of its own. Reading `+0x24` as a slot index lands on `0x227940`,
  which is a function prologue rather than a table.

  ⭐ `FUN_0017D7E8` is a linear search over **36-byte records** based at `0x2BF2BC`, count in
  `DAT_002C3300`:

  | offset | meaning |
  | --- | --- |
  | `+0x00` | world filter -- matches `4` (any) or the current world, from `FUN_0014E170` |
  | `+0x04` | type, which the consumer compares against `0xC` |
  | `+0x08` | the asset id searched for |
  | `+0x0C` | flags; bit `8` is special-cased and the low bits are matched to `FUN_0014E160() + 1` |
  | `+0x1C` | the handle the renderable keeps |

  ⭐⭐ SO THE LOOKUP IS FILTERED BY WORLD **AND** REGION -- the same seam the three shipped
  databases sit behind. An id alone does not identify an asset here; an id plus a world plus a
  region does. And the handle indexes `DAT_002EAAD0`, the SAME model table the guest animation
  dispatcher reads. One handle space, two consumers.

  ⚠ STILL UNREAD: who WRITES `shop + 0x78`, and the `parts[0x54]` branch in the fit, which
  overrides the model's own width with an authored value when it is present.
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

## The authored canvas is 512x512 SQUARE, measured on all four sides

Master asked how the laptop looks "filling at 1080p/1440p". That is a question about the canvas'
proportions, so the proportions had to be measured rather than assumed.

⚠ **The first measurement was contaminated and said 480.** It compared the lowest *text row*
(`UsersVal`, row 436 in `main_i_ride_data`) against the rightmost *frame edge* (col 315 + width
147 = 462). A text row is a baseline with glyphs hanging below it; a frame edge is a true edge.
Comparing the two is comparing unlike things, and it produced a confident wrong answer.

⭐ **Redone on true frame edges only** -- elements that declare both `width` and `height`, so each
axis is measured the same way -- across all 29 `.sce` files in MENUS.WAD:

| | extreme | against a 512 canvas |
|---|---|---|
| lowest frame BOTTOM | 448 (`main_bh_items:Model`, row 208 + h 240) | bottom margin **64** |
| rightmost frame RIGHT | 472 (`main_goldtickets:StarRow`, col 45 + w 427) | right margin **40** |
| topmost row | 65 | top margin **65** |
| leftmost col | 45 | left margin **45** |

Top 65 against bottom 64, left 45 against right 40. A canvas that is not 512 tall breaks that
symmetry: at 448 the bottom margin is 12 against a top of 65. So the canvas is **512x512**, which
is independently the size of the chrome art (`laptop_{jungle,hallow,fantasy,space}.ssh`), and the
margins are the chrome's own border.

⭐ **What that means on a widescreen.** The port scales by `min(w,h)/512` and centres, so the panel
comes out exactly square -- verified on the rendered pixels, `panel/height = 1.000` at both 1920x1080
and 2560x1440 -- leaving 420px bars each side at 1080p and 560px at 1440p, **43.8% of the width**.
That is the art's own shape, not a layout fault. Filling a 16:9 screen would mean stretching a
square canvas by 1.78, and whether the console itself stretched 512x512 into its 640x448 framebuffer
is NOT settled here; the `.sce` cannot answer it.

⚠ **`--resolution` does not work on the build box.** Its display is 1024x768, Godot clamps the
window, and `--resolution 1920x1080` silently yields 1028x749. `--ui-size=WxH` puts the panel in a
SubViewport of the asked-for size so it *lays out* for that size, and `SaveShot` grabs that
viewport. The model's own SubViewport is 4x oversampled (588x960), so it downscales -- and stays
sharp -- through 1440p, and only begins to upscale past ~2160p.

## The chrome's surround: measured, after a magic eraser was rejected

Master, on the first cut: *"id do a measured version. magic eraser just eats too much. try again"*.
Right on both counts -- the first version flooded inward over anything below luma 24 and cleared
**13.9%** of the image against a true surround of **12.0%**, eating ~4,900 pixels of the bevel's
antialiasing and leaving stray specks behind.

⭐⭐ **The art is not a gradient to be thresholded; the surround is one EXACT flat colour.** On the
lossless TGAs it is **(0,0,100)**, and all four world chromes agree on the colour *and* the area:

| chrome | key | exact-colour pixels | reachable from the border | stranded inside |
|---|---|---|---|---|
| `LAPTOP_JUNGLE` | (0,0,100) | 31,522 | 31,522 | 0 |
| `LAPTOP_HALLOW` | (0,0,100) | 31,524 | 31,522 | **2** |
| `LAPTOP_FANTASY` | (0,0,100) | 31,522 | 31,522 | 0 |
| `LAPTOP_SPACE` | (0,0,100) | 31,522 | 31,522 | 0 |
| `LAPTOP_512` | (0,0,150) | 29,250 | 29,248 | **2** |

Four independently-skinned images agreeing to within two pixels is a measurement, not a fit, and it
leaves no threshold to choose. ⚠ But the colour alone is not the test: two key-coloured pixels sit
stranded INSIDE the panel on HALLOW and on 512, so a plain colour match punches pinholes in them.
The flood from the border is what excludes those.

⚠⚠ **The mask cannot be taken from the `.ssh`, and that was measured too.** Over the pixels the TGA
says are exactly the key, the decoded SSH drifts by up to **66**, while the nearest NON-surround
pixel is **1** away -- the two populations overlap in colour space, so no runtime tolerance can
separate them. Nor is the silhouette a rounded rectangle: the best circular fit leaves a **4px**
residual and the four corners are not identical, so a radius would have been the same fitting
mistake in different clothes.

⭐ **UI.WAD ships both formats** -- 79 `.tga` against 103 `.ssh` -- so the lossless art is simply
there at runtime. The chrome is still DRAWN from the `.ssh`, which is what the console displays;
only the mask is read off the `.tga`, where it is exact. Runtime reports 31,522 / 12.0%, matching
the offline measurement to the pixel.

⚠ Worth stating plainly: **this is an improvement, not fidelity.** All five chromes are SSH type 4
and 24bpp TGA -- no alpha channel anywhere, against every other laptop sprite being type 5 / 32bpp.
On the console the laptop is a full-screen opaque image and the surround IS the background.

## Consistency across the three screens (2026-09-25)

Master: *"is everything between rides -> shop -> sideshows consistent?"* Checked three ways -- the
scene files field by field, every declared element against its scene, and a render of each.

**Shared, and identical rather than merely similar:**

| | RIDE | SHOP | SIDESHOW |
|---|---|---|---|
| model window | row 208, col 315, 147x240 | same | same |
| title | col 45, left | col 45, left | col 45, left |
| label column | col 45, left | col 45, left | col 45, left |
| value column | col 250, centre | col 250, centre | col 250, centre |

**Two differences that are the GAME'S, authored in the `.sce`:**
- The **shop sits 50 units lower**: title row 115 against 65 on the other two, labels 175 against
  115. Fewer rows, so the block is placed lower. Not drift.
- The **ride has no stepped value column**. Its values are per-row elements (`UpgradeVal` 338,
  `AgeVal` 400, `UsersVal` 436) while shop and sideshow step a shared column.

**⚠⚠ One real port defect, found by cross-checking declared elements against the scenes.** Of every
element the three scenes define, exactly one was provided by the scene and never used by the port:
**`UpgradeVal`**. `LaptopScreens.Ride` left the Upgrades row unbound with the comment "blank on the
real screen" -- an inference the scene file contradicts. Unbound, the value fell through to the
shared-column fallback and rendered **one full 32-unit row too low**, on the Addons line.

⭐ Measured on the render before and after, reading the value column's text bands back into
authored units: **370 -> 336**, against the scene's authored **338**. `AgeVal` (404 vs 400) and
`UsersVal` (437 vs 436) were already correct and did not move.

⭐ And Addons genuinely has NO value element -- the scene stops at those three -- so that row is
label-only. The asymmetry is the game's.

**⚠ Gaps that are the SCREENSHOT HARNESS, not the port**, recorded so they are not mistaken for
port bugs when someone next looks at a render:
- `LaptopFilmFrame` picks its model out of `_lib.Rides` by substring ("monkey" / "arcade" /
  "balloon"). Shops and sideshows are not rides, so the **shop renders no model at all** and the
  **sideshow renders the wrong one**. Only the ride screen's model is the right asset.
- The shop's ingredient row (Fat/Ice/Sugar/Salt) draws with a **blank label**, because the harness
  supplies no per-shop ingredient text id. `ShopScreen.LabelKeys` already holds null there by
  design; it is the caller that has nothing to put in it.

## ⚠⚠ SUPERSEDED -- see "The laptop menus, corrected" below. The laptop MAIN MENU, decoded and wired (2026-09-25)

Master: *"can you wire the laptop main menu"*.

**The chain, all read:**
1. `MENUS.WAD/main.sce` has exactly ONE element -- `textoptions`, row 115, col 45, left. No title
   element, no value column, no model window: the options ARE the screen.
2. `FUN_001fc778` builds the menu registry at **`0x2ec718`**, 16 bytes per entry
   (`"<name>.sce"`, `"<name>"`, -1, 0) -- 27 menus, `main` second.
   ⭐ The registry index is one BELOW the menu id, corroborated three ways: this port already had
   ride_data 0x0E, shop_data 0x11 and sideshow_data 0x13 decoded from their binders, and those sit
   at indices 13, 16 and 18. Three for three at index+1, so `main` is **menu id 2**.
3. `FUN_0016ef68` fills the option table at **`0x2b97c0`**, 8 bytes per entry `{u32 text id,
   handler}`.

| # | id | key | English | handler | opens |
|---|---|---|---|---|---|
| 1 | 420 | `STR_MAINMENU_RIDES` | Ride Information | `FUN_001c5e08` | `main_i_ride` |
| 2 | 1041 | `STR_MAINMENU_SHOPS` | Shop Information | `FUN_001c60d0` | `main_i_shop` |
| 3 | 760 | `STR_MAINMENU_SIDE_SHOWS` | Side Show Information | `FUN_001c6398` | `main_i_sideshow` |
| 4 | 429 | `STR_MAINMENU_TOILETS` | Toilet Information | `FUN_001c6660` | `main_i_bathroom` |
| 5 | 995 | `STR_MAINMENU_STAFF` | Staff Information | `FUN_001c6980` | `main_i_staff` |
| 6 | 752 | `STR_MAINMENU_BUILD_AND_HIRE` | Build & Hire | `FUN_001c7220` | `main_buildhire` |
| 7 | 1042 | `STR_MAINMENU_RESEARCH` | Research | `FUN_001c7108` | `main_research` |
| 8 | 485 | `STR_MAINMENU_PARK_STATS` | Park Statistics | `FUN_001c7a98` | `main_parkstats` |
| 9 | 441 | `STR_MAINMENU_FINANCE` | Financial Information | `FUN_001c78d0` | `main_financialinfo` |
| 10 | 549 | `STR_MAINMENU_GAME_OPTIONS` | Game Options | `FUN_001c5ce0` | `main_gameoptions` |
| 11 | 617 / 844 | `OPEN_PARK` / `EXIT_TO_MAP_SCREEN` | Open Park / Close Park | `FUN_001c7c00` / `FUN_001c7bb0` | — |
| 12 | 801 | `STR_MAINMENU_BUILD` | Build | `FUN_001c7650` | — |

⭐ **HOW IT WAS FOUND, because the method is reusable.** Scan the image for
`addiu rt, zero, imm` with each `STR_MAINMENU_*` text id as the immediate, then cluster the hit
addresses. They land on a regular **0x18 stride from `0x16ef84` to `0x16f0b8`**.

⚠⚠ **The same scan without controls lied first.** Run across the whole corpus it pointed at
`FUN_00bfd9ac` -- a 7,586-line decompiler artefact *outside the code range* that contains most
3-digit integers by chance. Adding an address-range and a size limit removed it. The control that
made the result trustworthy: the SHOP's seven already-known label ids cluster at `0x1d7288`, inside
the already-decoded shop draw `FUN_001d70c8`.

⭐⭐ **Open Park and Close Park are mutually exclusive, predicted BEFORE the render.** The table has
thirteen entries but twelve rows from 115 at a 32 step end at 467, and thirteen would start at 499
-- past the panel's own bottom edge at 493. Measured on the render afterwards: twelve bands, each
within 2-4 units of prediction, the last starting at **469.3** and its text ending at **489.7**,
3.3 units inside the panel. Nothing else fits. ⚠ The *condition* that chooses between them is still
unread; the exclusion is geometric, not decoded.

⚠ **Unsettled:** the table is preceded at `0x2b97b8` by `{530, null}` -- text id 530 is
`STR_PARKSTATS_INFORMATION` ("Information") with a NULL handler. A null handler fits a heading, but
`main.sce` gives the screen nowhere to put one and thirteen rows do not fit. It may be the
terminator of the table before this one. Recorded with its address; nothing draws it.

## ⚠⚠ The laptop menus, CORRECTED -- the table is a pool, not a menu (2026-09-25)

Master, on the render above: *"the list continues off the bottom"*. Right, and the overflow was the
symptom of a worse error than a layout slip.

**What I got wrong.** I read `FUN_0016ef68`, saw thirteen `{text id, handler}` entries, and drew
them as one flat list. They are neither one menu nor all shown. **The table at `0x2b97b8` is a
POOL.** `FUN_0016e520(menu, n)` appends pool entry `n` to the live list at `0x3ae108`, and two
builders pick from it under guards:

* **`FUN_0016e558` -- the MAIN menu.** Appends 0, then 6 only if `FUN_0014c928 && FUN_0014c8b8`,
  then 7, 8, 9, then 10, then 11 only if `FUN_0014e538() == 0`, then 12. **At most eight rows.**
  ⚠ Its ELSE arm (`FUN_00153410()` non-zero, a mode not identified here) replaces the whole first
  group with entry 13, `Build`.
* **`FUN_0016e710` -- the INFORMATION submenu.** Appends 1..5, each behind its own predicate, so a
  row appears only when the park actually contains one of that thing.

| pool | text | English | menu | condition |
|---|---|---|---|---|
| 0 | 530 | Information | main | always -- opens the submenu |
| 1 | 420 | Ride Information | info | any of `0014cba8/cbf0/cca0/cc58` |
| 2 | 1041 | Shop Information | info | `0014cd40` |
| 3 | 760 | Side Show Information | info | `0014cd88` |
| 4 | 429 | Toilet Information | info | `0014cdd0` |
| 5 | 995 | Staff Information | info | `00153410==0` && any staff |
| 6 | 752 | Build & Hire | main | `0014c928 && 0014c8b8` |
| 7-10 | 1042/485/441/549 | Research, Park Statistics, Financial Information, Game Options | main | always |
| 11 | 617 | Open Park | main | `0014e538() == 0` |
| 12 | 844 | Close Park | main | always |
| 13 | 801 | Build | main, ELSE arm | `00153410 != 0` |

⭐ **`Close Park` is not the opposite of `Open Park`.** Its key is
`STR_MAINMENU_EXIT_TO_MAP_SCREEN` -- "leave for the map screen" -- which is why the console appends
it unconditionally. My earlier "mutually exclusive pair" was a guess built to explain an overflow,
and it was wrong about which entry is conditional and about why.

⭐ **The row budget, measured rather than argued.** Rows start at 115 and step 32; the chrome's
inner content edge is row **469**, measured up the lossless TGA at col 150 where the bevel's bright
face gives way to the interior. Eleven rows end at 435 with text to ~465; a twelfth puts text at
~497, on the bevel. ⚠ My first check compared against the panel's OUTER silhouette (493) and so
called twelve rows a 3.3-unit fit. The ride screen settles it independently: it draws exactly
eleven label rows, 115 to 435.

⚠ The port now refuses loudly (`GD.PrintErr`) if a menu exceeds eleven rows, rather than drawing
onto the chrome. The two prior guesses both showed up as silent overflow.

## Build and Hire, split and wired (2026-09-25)

Master: *"split build & hire into separate build / hire options. then wire up the menus behind
those options"*.

**What was behind it.** `main_buildhire.sce` holds one `TextOptions` list and its own comment says
what it lists -- *"main build menu; items/staff text"* -- and it `<include>`s `main_bh_items.sce`
and `main_bh_staff.sce`. So the console's single `Build & Hire` opens a two-item menu. Splitting it
hoists that second level up and removes a step.

⚠⚠ **This is a deliberate departure from the console**, and it is the only one on this screen. Both
rows still carry pool entry 6's condition, because they are one console entry shown as two; nothing
about WHEN they appear has changed. `Build` takes text 801 (`STR_MAINMENU_BUILD`), a real main-menu
string. `Hire` takes 291 (`STR_GIZMO_CPP_HIRE`) -- there is **no `STR_MAINMENU_HIRE`** in the
table, so that label is borrowed from the gizmo bar and is the one string here the console never
shows in a menu.

**The two screens, read from their draws rather than their scene comments:**

| screen | draw | label ids | rows |
|---|---|---|---|
| `main_bh_items` (Build) | `FUN_00198b48` | 780, 375, 969, 61, 1060 | Purchase Cost $, Balance $, No. Owned int, Excitement slider, Reliability slider |
| `main_bh_staff` (Hire) | `FUN_00199008` | 773, 886, 833 | Pay Grade, Monthly Wage $, Motivation slider |

⭐⭐ **Excitement and Reliability reuse the RIDE screen's own ids** -- 61 and 1060, the same two
numbers already in `LaptopScreen.Ride`. That is corroboration across two independently decoded
screens rather than a coincidence. Both draws also carry the **32** row step ten times over and the
**(200,130)** label colour, so the whole laptop really is one widget family.

⭐ The items scene's comment ("PurchaseCost/Balance/NumberOwned/Excitment Reliability") turns out to
be right -- but only because the last two are SLIDERS with their own elements rather than text
rows. It was checked against the draw, not believed; the ride screen's comment omitted a row.

⭐ Nine main-menu rows now, still inside the eleven the panel holds, and the overflow guard stayed
silent.

## ⚠⚠ The scene element's NAME does not tell you the widget (2026-09-25)

Master, on the Build screen: *"those arent meant to be sliders, they're meant to be bars."*

`main_bh_items` names its two widgets **`ExcitementSlider`** and **`ReliabilitySlider`**, and
`main_bh_staff` names its one **`MotivationSlider`**. All three are drawn as **BARS**. I wrote all
three as sliders because I read the names -- the same trap as picking `PROG_BAR` over `BARPROG`
because of the word "BAR" in it.

⭐ **The widget is whichever function the draw calls.** `FUN_00115590` draws a bar, `FUN_001DAAE0`
a slider. Counting those calls per draw:

| screen | draw | bars | sliders |
|---|---|---|---|
| Ride | `FUN_001d5210` | **4** | **3** |
| Build | `FUN_00198b48` | 2 | 0 |
| Hire | `FUN_00199008` | 1 | 0 |

⭐⭐ **The ride row is the control and it is not vacuous.** Its 4 bars and 3 sliders were decoded
long before this count existed, and `LaptopScreen.Ride` already declared exactly that -- so the
method is validated against a screen whose answer was known, rather than fitted to the screens
being corrected.

⭐ **The check was proven to reject the bug, not just to pass.** `LaptopWidgetCounts.Decoded` is
now asserted against every spec's row kinds in `LaptopShopScreenAudit`. Reintroducing the fault on
purpose -- flipping Excitement back to `Slider` -- turned the audit red with
*"[70] Build: spec has 1 bars / 1 sliders, and FUN_00198b48 emits 2 / 0"*, exit code 2, while the
Ride control stayed green. Restored, it passes 71.

## The Build screen sells real rides at real prices (2026-09-25)

Master: *"can u wire up every ride's purchase cost. and wire the balance to reflect our balance"*.

Both were already decoded; nothing here invents a number.

* **Purchase Cost** is `RideDefinition.PlacementCost` --
  `(HasRideTiers ? Tier(0) : SimpleEconomy).PurchaseCost * 10`, in the park's tenths -- and
  `Money.Format` divides by ten, the same `/10` the console's own finance screen
  (`FUN_00134B98`) applies to every figure it draws.
* **Balance** is `ParkSim.Finances.Balance`, the live park purse the placement path already debits.
  `OpeningBalance` is 300,000 tenths, which is why a fresh park reads **$30,000**.

**97 priced rides** on JUNGLE. Spot-checked across the list:

| ride | purchase cost |
|---|---|
| Mammoth Fountain | $100 |
| Arcade | $1,750 |
| Giant Puzzle | $1,750 |
| Dino Karts | $2,500 |
| Splish Splash | $2,750 |

⭐ A scenery fountain at $100 against a water ride at $2,750 is the sanity check that these are the
game's own figures rather than something fitted.

⚠ **A definition that never joined a compiled record has NO cost rather than a free one, so it is
LEFT OUT of the list instead of shown at $0.** Listing an unjoined asset as free is how a missing
join becomes a shopfront exploit that nobody traces back; the placement path already refuses it,
and now the shopfront does too. Coaster parts and terrain are filtered exactly as the build list
filters them, so a coaster's car and pylon are not offered as separate purchases.

⚠⚠ **RELIABILITY HAS NO DEFINITION FIELD.** Nothing in `RideCatalogue` carries it, so that bar is
the one value on this screen with no source. It reads **zero** and the run says so out loud rather
than being filled from the cosine sweep, which would have looked exactly like data.

## ⭐⭐⭐ Reliability is COMPUTED, not stored -- and Excitement was the wrong field (2026-09-25)

Master: *"go fetch everything u need from the game code"*. Both gaps on the Build screen closed.

**Why no field carried reliability.** `FUN_00198b48` feeds its two bars from different places: the
excitement bar takes the item record's **+0x18** directly, the reliability bar takes
**`FUN_00198ad8(item)`** -- a computation.

```
FUN_00198ad8(item):
    inner = FUN_00198a98(item+0x24, item+0x28, item+0x2c)
    v     = inner * ((item+0x40 + item+0x44) / 2) * 9 >> 15
    return 100 - (v < 101 ? v : 100)

FUN_00198a98(a,b,c):
    = ((((0x1000-a)*0x800>>12) + a + ((0x1000-b)*0x800>>12) + b) / 2) * c
```
Each half reduces to `0x800 + p/2` in 12-bit fixed point.

⭐⭐ **The offsets land exactly on this port's own tier layout**, which is the corroboration.
`RideTier` is 52 bytes at payload+32, so from the payload base +0x24/+0x28/+0x2c are
**MinSpeedDamage**, **MinCapacityDamage** and **WearRate**, and +0x40/+0x44 are **MinDuration** and
**MaxDuration**. Damage per unit speed and capacity, times a wear rate, times how long a ride runs
-- wear per ride, inverted into a percentage. Those names were chosen long before this function was
read.

⚠⚠ **AND IT CAUGHT A SECOND ERROR.** The excitement bar reads **+0x18**, and
`AssetResourceDatabase.Entry` independently names `I32(24)` -- the same offset -- `BaseExcitement`.
The first wiring used `UsageInfo.ExcitementLevel`, a different field. Two derivations that never
saw each other agreeing on +0x18 is what makes the correction a reading rather than a preference.

⚠ **Every tiered ride reads 86, and that is the DATA, not a bug.** All of them carry the same
tier-0 wear fields -- MinSpeedDamage 4, MinCapacityDamage 4, WearRate 5, MinDuration 1,
MaxDuration 10. ⭐ What proves the read is per-ride rather than one shared default is that
`InitialCondition` DOES vary across those same records (100 against 1000). Excitement varies too:
0 / 45 / 60 / 75 / 80 over the sample. So the purchase screen shows the reliability of a ride
*fresh*, and everything is equally fresh.

⭐ Pinned in the audit with a worked example AND a control: (4,4,5,1,10) must give 86, and eight
times the wear rate must give LESS than 86 -- otherwise the first check would pass on a function
that returned 86 regardless of its inputs.

⚠ The console's clamp is one-sided and the port keeps it: only the upper end is capped, so a
negative `v` returns above 100. `null` rather than 100 for anything with no ride tiers, because
"perfectly reliable" and "has no wear model" are different readings.

## "No. Owned" was wired, and wired to the wrong key (2026-09-25)

Master: *"do u wanna wire up the no. owned? if it isnt already"*. It was -- and it was wrong in a
way that could not be seen.

⚠⚠ **`ParkRide.Id` is the PLACEMENT's id, unique per placed thing. `RideDefinition.Id` is the ride
TYPE's `Info.Id`.** The first version compared the two, so it matched nothing and would have read
**0 in a park full of Dino Karts**. The harness park is empty, so the wrong answer and the right
answer were the same number on screen: **0**. A render could never have caught it.

⭐ `ParkSim.Add` already carries the definition through to `ParkRide.Definition`, and the catalogue
holds one instance per definition, so reference equality is the exact test, with the `Info.Id`
comparison kept as a fallback for anything placed before that was wired.

⭐⭐ **Proven against rides built by hand, not against the park.** The audit now constructs three
`ParkRide`s -- two sharing one definition with different placement ids, one of another -- and
asserts the predicate counts 2 and 1. ⚠ Its control re-runs the ORIGINAL key on the same fixture
and asserts it finds **0**, which is exactly the shape the bug took: a wrong answer that reads as
an empty park. Without that control the first two checks would pass on the broken version too,
because in an empty park everything counts zero.

## The Hire screen's data sources -- decoded, but the port has no staff to show (2026-09-25)

Read from `FUN_00199008`, which draws labels `0x305`/`0x376`/`0x341` = 773 Pay Grade, 886 Monthly
Wage, 833 Motivation:

| row | source |
|---|---|
| Pay Grade | `staff[+0x14] + 1`, through the INTEGER formatter `FUN_00142b68` |
| Monthly Wage | `FUN_0012b630(staff, -1)`, through the MONEY formatter `FUN_00142908` |
| Motivation (bar) | `staff[+0x18]` |

⭐ **The wage is a product of two tables**, `FUN_0012b630`:
```
wage = DAT_0035c410[ staff[+0x14] ] * DAT_0035c428[ staff[+0x10] ]
```
so `staff[+0x14]` is the PAY GRADE index and `staff[+0x10]` the STAFF KIND index -- and the five
kinds are already visible in the text table as `STR_PURCHASE_{MECHANICS,HANDYMEN,ENTERTAINERS,
GUARDS,RESEARCHERS}`. ⚠ The two tables themselves are not yet dumped.

⚠⚠ **AND THE SCREEN CANNOT BE WIRED YET, because this port has no staff at all.** There is no
`Staff` type, no pay grade and no wage anywhere in `core/` or `game/` -- the only matches are the
laptop lines written today. Staff are also absent from the compiled database: `AssetKind` runs
Coaster / Feature / Ride / Shop / Sideshow / TrackRide / TourRide / TrackUpgrade, with no staff
kind, so unlike the Build screen there is no record to read a price off.

⭐ Recorded rather than faked. Filling these three rows from the demo sweep would have produced a
screen that looks finished and means nothing; the spec above is what an implementation needs.

## Mouse support, and the Back/Close buttons -- which fill the lump (2026-09-25)

Master: *"does the 'laptop' ui support mouse hover to highlight, click to open / select. oh also
add a back/close button to every laptop ui"*.

**It did not.** `LaptopShopScreen` was `MouseFilter.Ignore` with no input handler at all -- the
pointer passed straight through it. Now `MouseFilter.Stop` with `_GuiInput`: motion sets a hover
row or button, a left click activates. ⭐ Hover is kept SEPARATE from selection, because the
console has no pointer and highlight-under-cursor must not quietly move a selection a controller
would be driving.

⭐ **A menu row's hit box is the whole row**, 260 units wide, not the rendered glyphs -- hit-testing
the text would make "Build" harder to click than "Financial Information", which is a worse UI than
the console's.

**The buttons go in the lump** -- cols 321..499, rows 17..183, the space measured earlier and left
empty when master said "leave it blank for now". That is where the console drew its face-button
legend (triangle Back, square Mainmenu, circle Close), so it is the right home for the same idea.
⚠ A deliberate addition, not a restoration: the console drew glyphs for a pad it assumed you were
holding, and these are words because a mouse has no face buttons. ⭐ The words are the game's own,
`STR_GIZMO_CPP_BACK` (545) and `STR_GIZMO_CPP_CLOSE` (967) -- the pair the gizmo bar already uses
-- rather than invented English. Hover brightens them to the selection yellow, since the laptop has
exactly two text colours and a third invented for hover would not be this UI.

⚠ **A still cannot show a hover state**, so `ForceHoverForShot` drives the same two fields the
pointer sets. Without it a render could neither demonstrate the highlight nor catch it going
missing -- the screenshots proving this are evidence rather than assertion.

## Tab opens the laptop; the temporary build panel is gone (2026-09-25)

Master: *"can u wire up the laptop ui to tab, remove the old temporary build tab"*.

**Tab** was toggling a Godot `PanelContainer` down the right-hand side -- a label reading
`BUILD  (Tab)`, a wrapping row of category buttons and an `ItemList` -- scaffolding from before the
laptop existed. It now calls `ToggleLaptop`, which opens the laptop's own main menu. ⚠ Closing
clears any armed placement, exactly as closing the old panel did: a held ride with no menu behind
it is a cursor nobody can put down.

⚠⚠ **ONLY THE WIDGETS WENT, AND THAT WAS THE WHOLE DIFFICULTY.** `ShowBuildCategory`,
`ArmFromList` and `_buildRows` are not UI -- **four harnesses drive placement through them**: the
shop path, the ride path, `GuestTestRide` and `CheckBuildMenu`. Deleting the list along with the
panel would have broken all four to remove a widget. The selection API stays; the `ItemList` lines
inside it went.

⭐ Verified by running the placement control afterwards: **6 categories read from the archive**
(Rides 47, Features 31, Sideshow 12, Shops 8, Coasters 3, Upgrades 3), every one listed, and a ride
armed and **placed** -- "placed Belly Bounce at (31,36) turned 0". The chain survives the panel.

⚠ **`CheckBuildMenu` now checks less than it did, and that is worth stating rather than hiding.**
Its stated point was that it went through the SAME calls the menu and the click do, so "a check
that armed a placement by hand would pass with the menu unwired". There is no longer a widget whose
wiring could rot, so that half retired with the menu. What remains still earns its place: the
categories come from the archive, the row from `ShowBuildCategory`, the placement from
`ArmFromList`.

⭐ `FillBuildCategories` became `BuildCategories`, returning names and counts instead of building
buttons. Its two hard-won warnings are kept verbatim -- terrain is excluded by PATH rather than by
"has a definition", because `DefinitionFor` matches a `.sam` by directory suffix and hands the
terrain one back anyway.

## Three bugs master hit in play (2026-09-25)

> *"the laptop menu doesnt block clicks behind it, so you cant interact with the menu itself. its
> also showing the halloween background graphic, not the park dependant one"* ... *"also dont close
> it when moving."*

**1. Clicks fell through.** I set `MouseFilter.Stop` and never gave the Control a RECT. A Control
stops the mouse over its rect; an empty rect stops nothing, `_GuiInput` never fired, and every click
went to the park. `SetAnchorsPreset(FullRect)` supplies the area.
⚠ And `Stop` must not be permanent, or a CLOSED laptop eats the whole park. `Open` is now a property
that moves the filter with it, so the two cannot drift apart.
⭐ **The audit caught a worse bug in that fix before it shipped**: the constructor still set `Stop`,
so a freshly built, closed laptop swallowed every click. Red on `[79] a closed laptop lets clicks
through (filter Stop)`; the constructor now starts `Ignore`.

**2. It was never showing halloween.** ⭐⭐ `LAPTOP_512` -- the FALLBACK -- carries the *same
ghoul-and-stone-wall art as `LAPTOP_HALLOW`*. So "no world matched at all" is indistinguishable, by
eye, from "picked the wrong world". That is why it read as a halloween bug.

⚠ It matched nothing because the screen is built from `LoadHudFont`, which can run BEFORE
`AssetLibrary.OpenWad` -- and `WadName` is set *only* by `OpenWad`. At construction there was no
world to name, and the old code froze that null forever.

⭐ Fixed by asking rather than remembering: `Create` takes a `Func<string> worldNow`, and
`RefreshChrome` re-picks each frame, guarded on the resolved FILENAME so an unchanged world costs
one comparison. This is a lesson this port already had written down -- a constructor freezes a
field, and call order is not file order.

**3. Moving closed it.** A line hid the laptop on any WASD input. Removed: you cannot read a screen
and pan at the same time, and a stray movement key dismissing a menu you are clicking is just a way
to lose your place.

⭐ All three are pinned in `LaptopShopScreenAudit` (now 88). The chrome checks include the ones that
matter: **a null world, an empty world, and a non-world WAD must all give the FALLBACK** -- those
are exactly the values the panel saw before `OpenWad` ran.

⚠ One honest limit: the audit adds the panel to a bare `Node`, so `FullRect` has no parent rect to
resolve against and the size reads 64x64. The check asserts only that it is non-zero. In the game
the panel is a child of `_uiRoot`, where FullRect resolves to the viewport -- that part is NOT
covered by a test.

## ⚠⚠ The click bug had ONE cause and TWO symptoms — a coordinate-space mismatch

Master, after the first fix: *"i cant click any options. and if it cant find my mouse, it just
selects a random option to highlight"*.

Those look like two bugs. They are one.

`_GuiInput` reports `InputEventMouse.Position` in the **Control's LOCAL space**. Every box this
screen hit-tests -- menu rows, the Back and Close buttons -- is built from `Origin` and `Scale`,
which are computed from **`GetViewportRect()`**, i.e. VIEWPORT space. The two are the same
coordinate system *only* when the Control sits at the origin and is exactly the viewport's size.

It was not. So:
* most clicks fell outside the rect and never reached `_GuiInput` at all → **"i cant click any
  options"**;
* the ones that did land were compared against boxes in the wrong frame and matched whichever row
  the arithmetic happened to produce → **"it just selects a random option to highlight"**.

⭐ Fixed by `FitToViewport()` -- Position to zero, Size to `GetViewportRect().Size` -- called on
show, on draw and before every hit-test. The rect is now *set*, not inferred from an anchor preset.

⚠⚠ **AND I HAD ALREADY WRITTEN THIS GAP DOWN.** The previous entry ends: *"the audit parents the
panel to a bare `Node`, so `FullRect` has no parent rect to resolve against and the size reads
64x64 ... In the game the panel is a child of `_uiRoot`, where FullRect resolves to the viewport --
that part is NOT covered by a test."* I identified the untested assumption, stated it, and then
relied on it anyway. Naming a gap is not the same as closing it.

⭐ The check that let it through asserted only `Size.X > 0 && Size.Y > 0` -- which 64x64 satisfies,
and which a wrong-but-nonzero rect satisfies too. It now asserts the **equality the code actually
depends on**: `Position == 0 && Size == viewport`.
