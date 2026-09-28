# The finance laptop screens: menu, balance sheet, new loan

Researched 2026-09-28. Source: `SLES_500.32` (PAL; `vaddr = file offset + 0xFF000`), Ghidra
decompiles with hand-read MIPS where a leaf was two instructions, `MENUS.WAD/main_financialinfo.sce`,
`main_fi_balancesheet.sce`, `main_fi_newloan.sce`, and `Text/translations/eur/{eng,id}.dat`.
Every address below is native. Text ids were resolved to BOTH their string and their `STR_` key.

## ⭐⭐ All three screens resolve their elements BY NAME from the `.sce`

`laptop-screens.md` reports `numericoptions` as absent from the executable and concludes the
balance sheet must be hardcoded. **That was a search defect.** A case-folded byte search finds the
whole finance string pool in one block:

| address | string | bound by |
|---:|---|---|
| `0x35E390` | `TextOptions` | `FUN_00133960` at `0x1339b8` (id 4) and `0x133e18` (id 5) |
| `0x35E410` | `main_fi_balancesheet` | menu-block select at `0x133dfc` |
| **`0x35E428`** | **`NumericOptions`** | **`FUN_00133960` at `0x133e68`** |
| `0x35E438` | `main_fi_newloan` | menu-block select at `0x133eec` |
| `0x35E448` | `LoanText` | `0x133f08` |
| `0x35E458` | `LoanInformation` | `0x133f60` |
| `0x35E468` | `LenderName` | `0x133fb0` |
| `0x35E478` | `LenderNameArrows` | `0x134000` |

Controls for the search: the same pass finds `TextOptions` at 11 sites, `SatisfactionBar` at 4,
`CostItem` at 2. Control for the xref (`tools/re/xref.py`): `LoanText`, which the survey already
scored 4/4, hits inside the same function. (`hardcoded-screens.md` reached the same conclusion for
`main_gameoptions` and `main_research` independently.)

**`FUN_00133960` is one binder for the whole family.** It loads scene ids 4, 0x15, 0x16, 5 and 6 in
turn -- `FUN_001fc4e8(mgr, id) == 0 && FUN_001fc508(mgr, id)` (load), `FUN_001fc610` (the scene),
`FUN_00177768(scene, "<menu>")` (select the menu block; ⚠ not called for id 4, which goes straight
to the element), `FUN_00177978(scene, "Elem")` (find, case-insensitive), `FUN_00177ac8` → `int[4]
{row, col, width, height}` at element+0x20, `FUN_00177b08` → justify at element+0x30, then
`FUN_001fc598` (unload). It is called once, from the family constructor `FUN_00134188`.

⭐ **Frame and justify encodings, read from the parser rather than assumed.** `FUN_001c8510`
fills `f[0..3]` from the keys `ROW` (`0x367738`), `COL` (`0x367740`), `WIDTH` (`0x367748`),
`HEIGHT` (`0x367750`). `FUN_001c87c8` (`0x1c8828..0x1c88b0`) maps `LEFT → 0`, `RIGHT → 2`,
`CENTER → 1`; any other word is a parse error (`FUN_001c82a0`). An element that authors no
`width`/`height` keeps the allocator's defaults: `FUN_00177430` (kind 2, 0x5C bytes) sets the frame
to **`{0, 0, 1, 1}`** and justify to **0** at `0x177430+`. So an unauthored width is 1, not 0 and
not the image's stale 200.

⭐⭐ **`justify=right` DOES NOTHING ON THE PS2.** Every finance label and value goes through
`FUN_00138580`/`FUN_00138798` → `FUN_0020ACF8(dev, x, y, z, str, draw)`. The justify mode is
`dev+0x10`, set by `FUN_001389C0(ctx, mode)` → `FUN_0020B248(dev, mode)` (`sw a1, 0x10(a0)`), and
the ONLY branch that moves the pen is `if (dev[4] == 1) x -= width >> 1` (`0x20ae60` region). Mode
0 and mode 2 both draw from the pen. Only two `.sce` files on the disc author `right` -- these two --
so the port's `DrawRun` right-align branch (`LaptopShopScreen.cs:1339`) has never been exercised
by a screen that was compared to a capture, and for these screens it would put the numbers
somewhere the console never puts them.

## Shared plumbing (what every finding below leans on)

- **Class**: vtable `0x35E540`, ctor `FUN_00134188`, title `FUN_00165948(this+0xa0, 0x39d)` = 925
  `STR_FINANCE_FINANCE` "Finance". Slot 2 draw `FUN_001349B8`, slot 3 update `FUN_00134828`.
- **Modal loop** `FUN_001C78D0` (no `jal` caller; reached through the menu-id table): builds the
  screen on its stack, writes **`this+0xC = 100`** (`uStack_1204`), then per frame `FUN_00134828`
  (update) and `FUN_001349B8(this, 0, 0, 0)` (draw), exiting when the root "back" flag `this+0x88` or
  the "exit" flag `this+0x90` is set.
- **Mode**: `this+0x560` is the menu list's accept flag (`list+0x260`, the list lives at
  `this+0x300`). 0 → the menu is shown. Non-zero → page `this+0x578`, which `FUN_00134760` copies
  from the highlighted row every frame: 0 balance sheet (`FUN_00134B98` / `FUN_00134B20`),
  1 overall stats (`FUN_00135190` / `FUN_00134EC0`), 2 finance stats (`FUN_00135620` /
  `FUN_00134EC0`), **3 new loan (`FUN_00136018` / `FUN_00135D98`)**, 4 existing loans
  (`FUN_001359F0` / `FUN_00135970`). Page 3 is proven, not inferred from menu order: its draw is
  the only reader of the four `main_fi_newloan` globals (`0x1360d0..0x136354`).
- **Text**: font 1 = `Large.bff` (`FUN_00141CE8` → `FUN_0020A958(dev, 1)`); the page draws push/pop
  it with `FUN_0020A9C0`/`FUN_0020A9D8`. Coordinates are the 512×512 text space already
  established for `FUN_002137C0` (`Viewer.cs`); `x = col`, `y = row`. The third numeric argument is
  a depth: text `z = param_4 + this+0xC = 100`, the menu list `z = 50`, the arrows sprite `z = 0`.
- **Colour** `FUN_001388E8(ctx, r, g, b)` → `FUN_0020A900(dev, r, g, b, 0xFF)`: **amber (200,130,0)**
  = `DAT_0035F550`, **yellow (255,255,0)** = `DAT_0035F560`.
- **Line advance** `FUN_0020AA08` = font record `+0x50`, which `FUN_0020A520` fills from the BFF's
  third header metric (`*(iVar3 + 0x2ee968) = uStack_bc`) -- the line advance, **30** for
  European Large (`bff-font.md`). Where a step is not an image constant it is `30 + 2 = 32`.
- **Formatters**: `FUN_00142908` money (`-` then `$` then `FUN_00142B68` thousands groups) --
  the port's `Money.Display`; `FUN_00142A10` percent (digits + `%`); `FUN_00142A50` years (digits +
  624 `STR_GUI_YR` "yr" when 1, else 794 `STR_GUI_YRS` "yrs", no space); `FUN_00142AC0` months
  (10 `STR_GUI_MNTH` "mnth" / 349 `STR_GUI_MNTHS` "mnths").
- **Pad**: `FUN_00181700(0)` = buttons pressed this frame; bits 1/2 move a list up/down, bits 4/8
  step a spinner down/up. `FUN_00181250(n)` = logical button → mask from table `0x363F40`:
  0 → `0x40` Cross (accept), 1 → `0x200` Triangle (back), 0xC → `0x80`, 0xD → `0x100` (both leave the
  laptop). Sounds `FUN_00111150(FUN_00110A68(), 0, id, 0)`: `0xD6` step, `0x12F` accept, `0x130`
  back, `0xAF` refused / clamp hit (`blnl_error1.vag`, per `Viewer.cs`).
- **Root flags** (vtable `0x361A60`, slots 6..15 hand-read at `0x165df8..0x165e88`): `+0x88` back,
  `+0x8C` accept, `+0x90`/`+0x94` exit. `FUN_00164D00(this, 0)` sets them from the pad in page
  mode; each page's updater polls slot 7 (`+0x88`) and returns to the menu.
- **Gizmo legend**: `FUN_00164B70(this, 1)` → `FUN_0013E288(FUN_001497B0(), 2, 0x1DD)` = 477
  `STR_GIZMO_CPP_SELECT` "Select"; `(this, 0)` → 310 `STR_GIZMO_CPP_BLANK` " ".
- **Tutorial events** `FUN_00107390(DAT_002AA720, ev)` (the hook documented in
  `coaster-building.md`): `0x12` on open; per page `0x16` balance sheet, `0x17`/`0x18` the graphs,
  `0x1A` new loan, `0x19` existing loans.
- **Panels -- dead code**: `FUN_00164E78` arms a 280×152 frame widget at (16, 64) every page and
  `FUN_00164F08` a 180×110 one at (280, 80) on pages 1..3, both through `FUN_00141F68`, which draws
  only when the widget's `+0x1C` is set. It reads 0 in the image (`0x2B95C4`, `0x2B9614`), neither
  helper writes it, and an xref sweep finds **no reference to either word anywhere** (control:
  `0x2B5CE4` in the same sweep hits its known store at `0x133ea8`). Nothing draws those rectangles.

## 1. `main_financialinfo.sce` -- the finance menu (id 4)

### Layout

| element | col | row | size | justify |
|---|---:|---:|---|---|
| `TextOptions` | 45 | 115 | none authored → `{w 1, h 1}` | left (0) |

Binder stores: row → `0x2B6094`, col → `0x2B6090`, width → `0x2B5CD8`, height → `0x2B5CDC`,
justify → `0x2B608C` (`0x133a08..0x133a18`). ⚠ `0x2B5CD8`/`0x2B5CDC` read 200/110 in the image;
that is a stale default, overwritten with 1/1 at bind. The constructor copies them into the list
widget at `this+0x300` (`0x1346d8..0x1346fc`): `+8 = col` (x), `+0xA = row` (y), `+0x14`/`+0x16` =
w/h, **`+0x270 = (justify == 1)`, `+0x274 = justify`**.

### Rows

`FUN_001340C0` clears the list (slot 4, `FUN_001C8E58`) and appends with `FUN_001C8EA0(list, id,
enabled=1)` from the u16 table at **`0x35E630`**:

| order | y | id | `STR_` key | English | opens page |
|---:|---:|---:|---|---|---|
| 0 | 115 | 34 | `STR_FINANCE_INFORMATION` | Balance Sheet | 0 |
| 1 | 147 | 859 | `STR_FINANCE_GRAPH_1` | Overall Statistics | 1 |
| 2 | 179 | 861 | `STR_FINANCE_GRAPH_2` | Finance Statistics | 2 |
| 3 | 211 | 741 | `STR_FINANCE_NEW_LOAN` | New Loan | 3 |
| 4 | 243 | 472 | `STR_FINANCE_EXISTING_LOANS` | Existing Loans | 4 -- **only when a loan is taken** |

The fifth row is appended iff some loan slot has `park+0xC+i*0x2C+0x28 == 0` (the loop walks
`park+0x34` at stride 0x2C). The menu is rebuilt on every return from a page (`FUN_00135D98`,
`FUN_00134B20`, `FUN_00135970` all call `FUN_001340C0` on back), so the row appears the moment a
loan is accepted. Appending resets the highlight to row 0 (`FUN_001C8E68` writes `+0x58 = 0`).

### Draw (`FUN_001C92C8`, called as `(list, 0, 0, 0x32)`)

- `x = col` (`+0x270` is 0 for a left-justified menu; a centred one would add `width/2`),
  `y = row + i × (30 + 2)`; justify mode = the `.sce`'s (left).
- Highlighted row yellow, every other row amber; a disabled row (none here) amber too.
- The list's own sprite draw `FUN_00141F68` is a no-op: `list+0x1C` is never set for this list.
- The "Finance" title and chrome are the base class's (`FUN_001649C8` and the title object at
  `this+0xA0`), not re-read here.

### Behaviour (`FUN_001C8EE8` + the hook `FUN_00134828`)

- Up/down (bits 1/2): highlight ±1 with wrap, sound `0xD6`.
- Cross (logical 0) on an enabled row: `list+0x260 = 1` (slot 13, `FUN_001C9710`), sound `0x12F`
  → `this+0x560 != 0` → page mode with `this+0x578` = the row. Entering row 4 also pre-fills the
  existing-loans list at `this+0x9C0` (`FUN_00134760`: `FUN_0015C680(list, 328/329/331/332, i)` =
  `STR_FINANCE_LOAN_1..4` "Loan 1".."Loan 4" for each taken slot).
- Triangle (logical 1): `list+0x264` → copied to `this+0x88` by the hook (`0x165e88`) → the modal
  loop exits: **back returns to the laptop's main menu**, not to a page.
- Logical 0xC/0xD: `list+0x268` → `this+0x90` (and 0xC also `+0x94` and `FUN_001497B0()+8 = 1`):
  closes the laptop.
- Gizmo legend "Select" while the menu is up; tutorial event `0x12` at construction.

## 2. `main_fi_balancesheet.sce` -- the balance sheet (id 5)

### Layout

| element | col | row | size | justify |
|---|---:|---:|---|---|
| `TextOptions` (labels) | 45 | 175 | none | left (0) |
| `NumericOptions` (values) | 250 | 175 | none | right (2) -- **read but never used, see below** |

Binder stores (`0x133e50..0x133e58`, `0x133ea0..0x133ea8`): TextOptions row → `0x2B5CF0`, col →
`0x2B5CEC`, justify → `0x2B5CE0`; NumericOptions row → `0x2B5CF8`, col → `0x2B5CF4`, justify →
`0x2B5CE4`. The only readers are in the draw (`0x134cfc..0x134d64`). Both justify globals read 0 in
the image because they are bound at load -- the trap the brief warns about, and here it is benign.

⭐ **`0x2B5CE4` (NumericOptions' `right`) has NO reader anywhere in the image** (`xref --range
0x2b5ce0 0x2b5d10`: one store at `0x133ea8`, nothing else; the range's other globals all show their
readers, which is the control). The draw sets the justify mode once, from **TextOptions**, and
draws both columns with it. So the values are **left-aligned starting at col 250**.

Row steps are image constants with no writer: **`0x2B5CE8 = 32`** and **`0x2B5D04 = 42`** (readers
`0x134d94`/`0x134d90` only).

### Draw (`FUN_00134B98`)

1. `FUN_0020A9C0`/`FUN_0020A958(dev, 1)`: push, Large.
2. Seven park figures through leaf accessors (`park = FUN_001005D8()`), **each `/10`** (the
   `Money.Format` division, confirmed here a second time).
3. `FUN_001388E8(ctx, 200, 0x82, 0)` -- **the whole page is amber**, labels and values alike; no
   highlight, no colour change per row.
4. `FUN_001389C0(ctx, 0x2B5CE0)` -- mode 0 (left) for everything that follows.
5. Eight rows: label `FUN_00138580(ctx, id, 45, y, 100)`, value `FUN_00142908` → `FUN_00138798(ctx,
   buf, 250, y, 100, 1)`; `y += 32`, except **`+= 42` after row 3** (the gap between the income
   block and the outgoing block).

### Rows (u16 table `0x35E648`; values from the leaf accessors at `0x100E30..0x100EF4`)

| i | y | id | `STR_` key | English | value (÷10 for display) |
|---:|---:|---:|---|---|---|
| 0 | 175 | 565 | `STR_FINANCE_GATE_TAKINGS` | Gate | `park+0x12C8` (`FUN_00100ED8`) |
| 1 | 207 | 954 | `STR_FINANCE_SHOP_TAKINGS` | Shop | `park+0x12CC` (`FUN_00100EE0`) -- income filed as category 4 by `FUN_001007D8` |
| 2 | 239 | 818 | `STR_FINANCE_SIDESHOW_TAKINGS` | Sideshow | `park+0x12C4` (`FUN_00100EE8`) -- category 5 |
| 3 | 271 | 774 | `STR_FINANCE_CASH_IN` | Cash In | `park+0x12D8` (`FUN_00100E30`) -- every credit, `FUN_00100750`; the port's `TotalIncome` |
| 4 | **313** | 504 | `STR_FINANCE_LOANS` | Loans | `Σ loan[i]+0x14 × 10` over slots with `+0x28 == 0` (`FUN_00100E88`, hand-read: `madd` of the outstanding by 10) -- **outstanding loan debt** |
| 5 | 345 | 371 | `STR_FINANCE_STAFF_WAGES` | Staff Wages | `park+0x12D0` (`FUN_00100EF0`) -- the port's `WageAccumulator` |
| 6 | 377 | 578 | `STR_FINANCE_PURCHASES` | Purchases | **computed in the draw**: `(park+0x12D4)/10 − (park+0x12D0)/10` = Cash Out − Staff Wages |
| 7 | 409 | 607 | `STR_FINANCE_CASH_OUT` | Cash Out | `park+0x12D4` (`FUN_00100E38`) -- every debit, `FUN_00100698` |

⚠ **`park+0x12D4` vs `park+0x12E4`.** The port's `ParkFinances` names `0x12E4` as `TotalSpending`.
`FUN_00100698` increments **both** (`+0x12E4` and `+0x12D4`) on every debit, and the balance sheet
reads `0x12D4`. They are equal unless some other path touches one of them, which is not read.

⚠ What files the Gate figure into `0x12C8` is not read here (`FUN_001007D8` handles only
categories 4 and 5); it is presumably the admission credit. Instrument: a caller census of
`FUN_00100750` grepped for a `0x12c8` store.

### Behaviour

Nothing is clickable. `FUN_00134B20` polls the back flag (`vt[7]` → `this+0x88`, set by
`FUN_00164D00` on Triangle with sound `0x130`), clears it (`vt[14](this, 0)`) and drops to the menu.
Gizmo legend blanked (`FUN_00164B70(this, 0)`); tutorial event `0x16`.

## 3. `main_fi_newloan.sce` -- New Loan (id 6)

### Layout

| element | col | row | size | justify | binder globals (row, col, justify) |
|---|---:|---:|---|---|---|
| `LenderName` | 45 | 115 | none | left (0) | `0x2B609C`, `0x2B6098`, `0x2B6084` |
| `LenderNameArrows` | 250 | 131 | none | -- | `0x2B60AC`, `0x2B60A8` |
| `LoanText` (labels) | 45 | 175 | none | left (0) | `0x2B5D98`, `0x2B5D94`, `0x2B6088` |
| `LoanInformation` (values) | 215 | 175 | none | right (2) → **drawn left-aligned** | `0x2B60A4`, `0x2B60A0`, `0x2B60B0` |

Stores at `0x133f48..0x13402c`; every one is read by `FUN_00136018` and nothing else. Row step
**`0x2B5D9C = 32`**, an image constant (seven reads in the draw, no writer). ⚠ `0x2B5D94`/`0x2B5D98`
read 85/30 in the image -- stale defaults, overwritten with 45/175 at bind.

### State: the lender spinner, and the two spinners that do nothing

The constructor builds four spinner widgets (`FUN_00207A50`, vtable `0x36C6B8`):

| widget | label (`FUN_00208350`) | min | max | initial | step `+0x6C` | display `+0x64` | flags `+0x60` |
|---|---|---:|---:|---:|---:|---|---|
| **lender** `this+0xF4C` | 648 "Loan" | 1 | 4 | 1 | 1 | digits | wrap |
| amount `this+0xFCC` | 478 "Amount" | 1000 | `loan+0x1C` | `loan+0x1C` | 1000 | money | auto-repeat |
| term `this+0x104C` | 1022 "Term" | 1 | `loan+0x20` | `loan+0x20` | 1 | years | -- |
| budget `this+0x10CC` | 442 "Budget" | 1000 | 1,000,000 | 500,500 | 1000 | money | auto-repeat |

The same four initialisers sit as data at `0x35E690..0x35E6EC` (`{min, max, value, textid |
display << 16, ...}`), which corroborates the field assignment.

⭐⭐ **Only the lender spinner takes input.** `FUN_00207B10` (spinner update) returns at once unless
`+0x18 & 1`; `FUN_00207A50` clears that bit at construction; the ONLY place in the family that sets
it is the New Loan draw, `this+0xF64 |= 1` at `0x136018+` -- the lender's. Every function in
`0x133960..0x137000` was enumerated by its prologue and read; none touches `0xFE4`/`0x1064` (the
amount's/term's bit). The graph pages set the year spinner's (`0x8F0`). So on the PS2 the amount is
always `Max.Loan` and the term always `Max.Term` -- which is why the page's own labels say
**Max.Loan / Max.Term**, and why the `.sce` comment ("number amount/term/…") describes the PC layout
rather than this one. The spinners are also never drawn: `FUN_00136018` draws no spinner and nothing
registers them for the base to draw.

The loan record is **`park + 0xC + (lender − 1) × 0x2C`** (four slots, 0x2C each):

| offset | meaning | evidence |
|---:|---|---|
| `+0` | lender index | `FUN_0017E4F0` |
| `+4` | amount borrowed ($) | written by `FUN_00135D98`; Existing Loans row "Borrowed" |
| `+8` | term, months | `FUN_00135D98` writes `years × 12`; Existing Loans shows `/12` as years |
| `+0xC` | rate, % | `FUN_00142A10` on it |
| `+0x10` | months remaining | Existing Loans "Remaining" via `FUN_00142AC0` |
| `+0x14` | outstanding ($) | Existing Loans "Outstanding"; summed by the balance sheet |
| `+0x18` | monthly repayment ($) | `FUN_0017E5D0` |
| `+0x1C` | max loan ($) | New Loan "Max.Loan" |
| `+0x20` | max term, years | New Loan "Max.Term" |
| `+0x24` | total repayable ($) | New Loan "Total" |
| `+0x28` | **1 = available, 0 = taken** | `FUN_00100470` sets 1; `FUN_00100CA8` clears |

⚠ **Loan money is in whole dollars, not tenths.** `FUN_00142908` is fed `loan+0x1C` etc. with no
division, and taking the loan credits `amount × 10` (`FUN_00100CA8` → `FUN_00100750(park,
loan+4 × 10)`). A port must format these with `Money.Display`, not `Money.Format`.

### The four lenders (`FUN_0017E4F0`, built by the park constructor `FUN_00100470` with `+0x28 = 1`)

| index | name (`FUN_0017E480` → `0x363B80[i]`) | max loan | rate | max term |
|---:|---|---:|---:|---:|
| 0 | Mr Byrne | $100,000 | 20% | 3 yrs |
| 1 | Ms Dabb | $50,000 | 20% | 2 yrs |
| 2 | Mr Howell | $25,000 | 20% | 2 yrs |
| 3 | Ms West | $10,000 | 15% | 2 yrs |

The names are plain C strings, not text ids -- they do not localise.

### The interest arithmetic (`FUN_0017E5D0`, 12.12 fixed point)

```
factor    = pow(0x1000 + (rate << 12) / 100, (months << 12) / 12)   // FUN_00206AA0 = exp(log(a) * b)
total     = amount * factor                                          // FUN_00195DB8, fixed multiply
repayment = total / months                                           // integer division   -> +0x18
outstanding = total = repayment * months                             // +0x14, +0x24
```

i.e. **annual compounding at the lender's rate over the term in years**, then a flat monthly
repayment. Nominal values: Mr Byrne 100,000 × 1.2³ = 172,800 → 4,800/month; Ms Dabb 72,000 →
3,000; Mr Howell 36,000 → 1,500; Ms West 10,000 × 1.15² = 13,225 → 551/month, total 13,224.
⚠ The exact console figures can differ by a unit: `pow` is `exp(log())` in fixed point through
floats (`FUN_00195A80`, `FUN_00206860`). Instrument: a PCSX2 savestate after taking a loan,
reading `park+0xC+i*0x2C` (`tools/p2s.py`).

### Draw (`FUN_00136018`)

1. Push font, Large.
2. **Arrows**: a UI sprite (`FUN_00143B18`, vtable `0x35F6F8`), colour yellow, `+0x30 = 1`,
   position (`LenderNameArrows` col 250, row 131), size `h × h` with `h = FUN_0020AA08` = 30.
   `FUN_00143BC0` blits UI texture **`#0x3C`** (the glyph the spinner class draws its own arrows
   with) at top-left `(250 + 15, 131 − 15)`, depth `z = 0`. Which arrow that texture shows is not
   read.
3. Yellow; `FUN_001389C0(ctx, LenderName.justify)`; the lender's name (`FUN_0017E670`) at
   (45, 115), `z = 100`.
4. `this+0xF64 |= 1` (lender spinner live).
5. Amber. **If `loan+0x28 == 0` (taken)**: one label, 910 `STR_FINANCE_LOAN_ALREADY_TAKEN`
   "Loan Taken", at (`LoanInformation` col 215, row 175 + 32 = **207**) -- and nothing else.
6. Otherwise `FUN_001389C0(ctx, LoanText.justify)` (0), six labels from **`0x35E680`** at
   (45, 175 + 32i); then `FUN_001389C0(ctx, LoanInformation.justify)` (2 → no shift) and six values
   at (215, 175 + 32i):

| i | y | id | `STR_` key | English | value |
|---:|---:|---:|---|---|---|
| 0 | 175 | 102 | `STR_FINANCE_LENDER` | Lender | lender name, `FUN_0017E670(loan)` |
| 1 | 207 | 854 | `STR_FINANCE_MAX_LOAN` | Max.Loan | money(`loan+0x1C`) |
| 2 | 239 | 387 | `STR_FINANCE_INTEREST` | Interest | percent(`loan+0xC`), e.g. "20%" |
| 3 | 271 | 133 | `STR_FINANCE_MAX_TERM` | Max.Term | years(`loan+0x20`), e.g. "3yrs" |
| 4 | 303 | 60 | `STR_FINANCE_REPAYMENT` | Repayment | money(`loan+0x18`) -- per month |
| 5 | 335 | 951 | `STR_FINANCE_TOTAL` | Total | money(`loan+0x24`) |

7. Pop font; sprite destructor.

`FUN_00164F08` also arms the (280, 80) 180×110 frame on this page (see Panels above).

### Behaviour (`FUN_00135D98`)

- `FUN_00207B10(lender)`: **left/right (bits 4/8) step the lender ±1 with wrap** (`+0x60` bit 0),
  sound `0xD6`. The arrows sprite is decoration for this; it is not hit-tested.
- On a lender change: the amount spinner is re-ranged to `[1000, loan+0x1C]` and **set to
  `loan+0x1C`**, the term spinner to `[1, loan+0x20]` and **set to `loan+0x20`** (the "value =
  min(max, X)" pattern with X = max).
- `FUN_00207B10(amount)`, `FUN_00207B10(term)`: no-ops (above).
- Up/down (bits 1/2): cycle `this+0x2FC` in 0..3 with sound `0xD6` -- **dead**: no function reads
  `+0x2FC` (checked across every finance decompile; the constructor zeroes it and only this updater
  touches it). A PC-era field-focus that lost its consumers.
- Every frame while the slot is available: `loan+4 = amount`, `FUN_0017E5D0(loan)`,
  `loan+0x10 = loan+8 = term × 12`, `FUN_0017E5D0(loan)` -- so Repayment/Total on screen are live
  for the current lender. If taken, `+0x2FC = 0` and nothing is written.
- Gizmo legend "Select" (`FUN_00164B70(this, 1)`).
- **Cross** (`FUN_00181250(0)` & pressed): `FUN_00100CA8(park, lender − 1)`: if `+0x28 != 0`, set
  it to 0 and credit `loan+4 × 10` tenths, return true; on success `FUN_00107E48("APPLIED FOR LOAN")`
  -- which is an **empty function** (`0x107e48`: `return`). So accepting a loan plays no sound and
  shows no message of its own; the page simply flips to "Loan Taken" next frame and the menu gains
  "Existing Loans" on return. **The only refusal is a slot already taken**, and it is silent: there
  is no balance, credit or count check anywhere in the path. 638 `STR_LOAN_APPROVED` is never
  referenced by this family.
- **Triangle**: `+0x88` → `FUN_001340C0` rebuilds the menu, `vt[14](this, 0)` clears the flag,
  `this+0x560 = 0`. Tutorial event `0x1A`.

Repayment over time (what decrements `+0x10`, debits `+0x18`, reduces `+0x14`) lives in the month
end, not in these screens, and is not read here.

## Existing Loans (page 4) for contrast -- the one page that IS hardcoded

There is no `main_fi_existingloans.sce` and the registry has no id for it. `FUN_001359F0` draws
"Lender" (102) at (101, 124), the name at (221, 124), then rows at `y = 124 + 32i` with labels from
`0x35E670` (77 Borrowed, 1022 Term, 387 Interest, 60 Repayment, 994 Remaining, 434 Outstanding) at
x 101 and values at x 221 (`FUN_00138630`), and 154 `STR_FINANCE_NO_LOAN_TAKEN` "No Loan Taken" at
(146, 148) when nothing is selected. Its lender list (`this+0x9C0`, `FUN_0015BB58` class) is not
read. Out of scope; recorded so nobody looks for its `.sce`.

## What was NOT determined, and what would settle it

1. **Exact loan figures** -- fixed-point `pow` rounding (above). PCSX2 savestate + `tools/p2s.py`.
2. **The finance title and chrome** are the base class's (`FUN_001649C8`, the title object at
   `this+0xA0`), inherited unchanged and not re-read; the port already draws them for the other
   screens. (The two panel helpers turned out to be dead -- see Panels above -- so no capture is
   needed for them.)
3. **UI texture `#0x3C`** (the arrows glyph): which way it points, and its atlas -- not read.
4. **The Gate accumulator's writer** (`park+0x12C8`).
5. **`0x12D4` vs `0x12E4`** -- both incremented by every debit; whether anything else separates them.

## Summary for the port

- All three screens: `.sce` is the authority for every coordinate; bind by name exactly as the shop
  screen does. Menu list: rows at `115 + 32i`, left at 45, yellow highlight, amber otherwise; 4 rows,
  5 once any loan is taken. Balance sheet: 8 amber rows, labels at 45 / values at 250, `y = 175 +
  32i` with an extra 10 after row 3, values `/10`. New Loan: lender name at (45,115) yellow, arrows
  glyph at (265,116) 30×30, six label/value rows at 45 / 215 from `y = 175`, values in whole dollars.
- **Treat `justify=right` as left**, on these screens and in `DrawRun` generally, until a capture
  contradicts the device's code.
- Model the loan as the 0x2C record above with the four hardcoded lenders and the compound formula;
  accept = take the whole offer; the only refusal is "already taken".
