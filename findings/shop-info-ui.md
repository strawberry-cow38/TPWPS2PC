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

## The art, `UI.WAD/laptop/` (62 files)

| file | what |
|---|---|
| `LAPTOP_{JUNGLE,HALLOW,FANTASY,SPACE}`, `LAPTOP_512` | 512x512 chrome, per world |
| `BARSLIDE` 128x32 + `BARKNOB` 32x32 | yellow track + gold knob — the sliders |
| `PROG_BAR` 128x32 (ticked) + `PROG_CBIT`/`PROG_VBIT`/`PROG_WBIT` 16x32 | the satisfaction bar and its cyan segments |
| `L_corner1-3`, `L_edge1-4`, `L_fill`, `L_Panel1-9`, `L_Panelfill` | the laptop's own inner nine-slice |
| `BUTTX`, `BUTTCIRCLE`, `BUTTSQUARE`, `BUTTTRIANGLE` | the face-button glyphs |
| `AWARD_*` | medals and stars |

⭐ Most of these ship as **`.tga` as well as `.ssh`**, so the art can be read without the SSH
decoder — but the `L_*` nine-slice does **not**, and see the gap below.

⭐ **The face is `Large.bff`**, corroborated rather than chosen: its line advance is **30** against
the 32 row step. Small (21) and Console (14) would leave holes.

## ⚠ Known gaps

* **Eight `.ssh` images do not decode**: `L_edge1-4`, `L_Panel2`, `L_Panel5`, `L_Panel6c`,
  `L_Panel7` — every one with a dimension of 8 that is not 8x8. `Ssh.cs` refuses them with
  *"Only the measured 8x8 sub-macroblock layout is supported"*, an **exclusion** rather than a
  decode failure. A sweep of all 11 archives finds exactly these 8 such images on the whole disc
  and **none has a `.tga` sibling**, so there is no shipped reference to validate a layout against.
  Three *8x8* files do (`AWARD_T_8`, `SPACE/PUD_5`, `SPACE/Sploo2X3d`) and are the available
  control. Until this is cracked the laptop's inner nine-slice cannot be drawn.
* **Satisfaction is not tracked.** `shop[0xb0]`/`[0xb4]` is decoded but the port does not yet
  accumulate it, so the bar renders empty rather than showing a number that was never computed.
* **The `Model` window** (147x240 at col 315) is a 3D viewport, not a sprite, and is not yet drawn.
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
