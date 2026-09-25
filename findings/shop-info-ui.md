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

## The laptop MAIN MENU, decoded and wired (2026-09-25)

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
