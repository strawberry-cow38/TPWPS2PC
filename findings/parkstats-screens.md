# Park Statistics: the menu, Visitor Information and Park Finance

Scope: `main_parkstats.sce` (menu id 8), `main_ps_visitorinfo.sce` (id 10), `main_ps_parkfinance.sce`
(id 11). `main_ps_statistics.sce` (24) is another agent's; `main_goldtickets.sce` (3) is used here only
as the control. Every address is `SLES_500.32` (vaddr = file offset + 0xFF000). Text ids resolve
through `Text/translations/eur/{eng,id}.dat`.

## 1. One class draws all five screens

`FUN_00183d50` has exactly ONE caller, `0x184728`, inside the constructor `FUN_00184618` (vtable
`0x364318`, title `FUN_00165948(this+0xa0, 0x212)` = 530 `STR_PARKSTATS_INFORMATION` "Information").
So the "family" is one object: the Park Statistics MENU plus its four pages. The binder looks the
elements of all five scenes up by name and parks their frames in globals (§6); the pages are a
switch on `this+0x5c0`, the index of the menu row that was confirmed.

| vtable slot (gcc2 8-byte entries, pfn at +4) | function | what it is |
|---|---|---|
| 1 `0x184ec8` | destructor | -- |
| 2 `0x185228` | draw `(this, xOff, yOff, z)` | menu list, or the page by `this+0x5c0` |
| 3 `0x1850e8` | input/update | menu list input, or the page's update |
| 4..15 | `164d00 1649c0 165df8 165e00 165e08 ...` | the shared laptop-screen base |

The switch, from slot 2 (`0x185228`) and slot 3 (`0x1850e8`), with `this+0x5a8` = "a row has been
confirmed" and `this+0x5c0` = which row:

| `+0x5c0` | menu row (text id) | opens scene / id | draw | update |
|---:|---|---|---|---|
| 0 | 841 `STR_PARKSTATS_PARK_INFORMATION` "Visitor Information" | `main_ps_visitorinfo` 10 | `FUN_00185458` | `FUN_001853a8` |
| 1 | 1063 `STR_PARKSTATS_GRAPH` "Statistics" | `main_ps_statistics` 24 | `FUN_00185c48` | `FUN_00185998` |
| 2 | 164 `STR_PARKSTATS_DETAILS` "Park Finance" | `main_ps_parkfinance` 11 | `FUN_001867b8` | `FUN_00186138` |
| 3 | 101 `STR_GIZMO_CPP_AWARDS` "Awards" | `main_goldtickets` 3 | `FUN_00186238` | `FUN_001861b0` |

The four row ids are the u16 table `DAT_002c40c0` = `{841, 1063, 164, 101}`, appended in that order by
the ctor (`FUN_001c8ea0(this+0x348, id, 1)` x4, `0x184618`). ⚠ The Awards row borrows the gizmo
bar's "Awards" (101), not `STR_PARKSTATS_AWARDS` (475); 475 is the awards PAGE's heading.

Draw call convention (same as every laptop screen, see `findings/hardcoded-screens.md`):
`FUN_00138580(ctx, textId, col, row, z)` / `FUN_00138798(ctx, str, col, row, z, 1)` take ABSOLUTE
scene coordinates; the 5th argument is a DEPTH, `z = zOff + *(short*)(this+0xc)`, and the driver
`FUN_001c7a98` sets `this+0xc = 100`. `FUN_002137c0(sprite, x, y, z)` confirms it: `z * 1/128` goes
to the quad's depth, x/y to NDC. Lower z is nearer (bar fill at z+1 sits behind its outline trough;
the awards page draws medals at 24 in front of cutouts at 32). Colour is `FUN_001388e8(ctx, r, g, b)`;
justify is `FUN_001389c0(ctx, j)` and is STATE -- it stays in force for the next draws (§3.4).

## 2. `main_parkstats.sce` -- the menu (id 8)

### 2.1 Layout

| element | col | row | w x h | binder globals (`FUN_00183d50`, name `0x364180`) |
|---|---:|---:|---|---|
| `TextOptions` | 45 | 115 | -- (`justify=left`) | justify `DAT_002c4574`, row `DAT_002c44b0`, col `DAT_002c44ac`, w `DAT_002c44b4`, h `DAT_002c44b8` |

The ctor copies them into the list widget at `this+0x348` (`+0x350` col, `+0x352` row, `+0x35c` w,
`+0x35e` h). Slot 2 copies the justify into `list+0x274` and sets `list+0x270 = (justify == 1)`
(the centre flag adds half the width to x, `FUN_001c92c8`); `left` = 0, so x = col.

### 2.2 Rows

The list draw `FUN_001c92c8(list, 0, 0, z + 0x32)` (z = 50 for the menu; pages use 100):

| order | text id | key | English | y |
|---:|---:|---|---|---:|
| 0 | 841 | `STR_PARKSTATS_PARK_INFORMATION` | Visitor Information | 115 |
| 1 | 1063 | `STR_PARKSTATS_GRAPH` | Statistics | 147 |
| 2 | 164 | `STR_PARKSTATS_DETAILS` | Park Finance | 179 |
| 3 | 101 | `STR_GIZMO_CPP_AWARDS` | Awards | 211 |

Row pitch = `FUN_00138a48()` + 2 = the laptop font's line height (`FUN_0020aa08` -> font+0x50) + 2 =
30 + 2 = **32** with `Large.bff` (findings/shop-info-ui.md), i.e. `y = 115 + 32*i`. Colours from the
list ctor `FUN_001c8d80`: cursor row `*0x35f560` = (255,255,0), other rows `*0x35f550` = (200,130,0);
a disabled row would also draw (200,130,0), but all four are appended enabled (third arg 1), so all
four are selectable. Text drawn at z 50 in the laptop font `DAT_002eea58`.

The laptop's title while on the menu AND on every page is 530 "Information" (set once in the ctor;
no page changes it). Gizmo bar slot 2: `FUN_00164b70(this, 1)` -> `FUN_0013e288(gizmo, 2, 0x1dd)` =
477 `STR_GIZMO_CPP_SELECT` "Select" on the menu; on a page `FUN_00164b70(this, 0)` -> 310
`STR_GIZMO_CPP_BLANK` (blank).

### 2.3 Behaviour (`FUN_001c8ee8`, list input; `FUN_001850e8`, screen)

- Raw pad bits from `FUN_00181700(0)` (buttons newly pressed): bit 1 = cursor up, bit 2 = cursor
  down, both wrap (`-1 -> count-1`, `count -> 0`), sound 0xd6.
- Logical button 0 (`FUN_00181250(0)`) = confirm: only if the row is enabled (`list+0x15c[sel]`);
  sets the list's activated flag (`list+0x260`, which the screen reads as `this+0x5a8`), sound 0x12f.
  The page shown is the HIGHLIGHTED row: `FUN_001850c8(this, sel)` (leaf at `0x1850c8`, no frame)
  writes `this+0x5c0 = this+0x5c4 = sel` every frame while the menu is up.
- Logical 1 = back (sound 0x130) -> screen slot 14 (`FUN_00165e88`) sets `this+0x88`; the driver
  loop in `FUN_001c7a98` exits on `this+0x88` and returns to the laptop main menu.
- Logical 0xd / 0xc = close the laptop (`this+0x90`, `FUN_00165ee0`; 0xc also slot 12 and
  `FUN_001497b0()+8 = 1`).

### 2.4 Leaving a page

Every page update (`FUN_001853a8`, `FUN_00186138`, `FUN_001861b0`) starts with: if slot 7
(`FUN_00165e00` = `this+0x88`, "back was pressed") is set -> clear it (slot 14 with 0), reset the
list (its slot 6), `this+0x5a8 = 0`. So back on a page returns to the menu; the base
`FUN_00164d00(this, 0)` in slot 3 is what turns the pad into those flags while a page is up.

## 3. `main_ps_visitorinfo.sce` -- Visitor Information (id 10)

### 3.1 Layout (binder names at `0x364190..0x364220`)

| element | col | row | justify | globals: row / col / justify |
|---|---:|---:|---|---|
| `FeelingsText` | 45 | 65 | left | `2c45a4` / `2c45a0` / `2c4578` |
| `FeelingsClouds` | 45 | 105 | -- | `2c45ac` / `2c45a8` |
| `ThoughtsText` | 45 | 243 | left | `2c4490` / `2c448c` / `2c4580` |
| `ThoughtsClouds` | 45 | 284 | -- | `2c4498` / `2c4494` |
| `PeopleVisitedText` | 45 | 350 | left | `2c44a0` / `2c449c` / (not stored -- draw uses `DAT_002c457c`, image 0 = left) |
| `PeopleVisitedVal` | 260 | 350 | -- | `2c44a8` / `2c44a4` |
| `GatePriceText` | 45 | 400 | left | `2c4594` / `2c4590` / `2c4584` |
| `GatePriceVal` | 260 | 400 | center (⚠ NOT stored, §3.4) | `2c458c` / `2c4588` |
| `GatePriceArrows` | 200 | 414 | -- | `2c459c` / `2c4598` |

No element carries a width/height; the widget sizes are constants in the ctor (§3.3).

### 3.2 Rows, in draw order (`FUN_00185458`, z = 100, colour (200,130,0) throughout)

| # | at (col,row) | text id | key | English | value drawn |
|---:|---|---:|---|---|---|
| 1 | 45,65 | 853 | `STR_PARKSTATS_PEOPLES_FEELINGS` | People's Feelings | heading |
| 2 | 45,243 | 308 | `STR_PARKSTATS_DOMINANT_THOUGHTS` | Dominant Thoughts | heading |
| 3 | 45,350 | 139 | `STR_PARKSTATS_PEOPLE_VISITED` | People Visited | -- |
| 4 | 260,350 | -- | -- | -- | **people visited**, `FUN_00142b68` = decimal with thousands commas ("12,345"), no `$` |
| 5 | 45,400 | 50 | `STR_PARKSTATS_TICKET_PRICE` | Ticket Price | -- |
| 6 | 260,400 (+ arrows 200,414) | -- | -- | -- | **gate price spinner**, `FUN_00142908` = `$` + commas, in whole dollars |
| 7 | 45,105 / 145 / 185 | -- | -- | -- | three feelings rows: icon + bar in a blue pill |
| 8 | 45,284 | -- | -- | -- | up to three dominant-thought icons in a blue pill |

Rows 3-6 are per-frame reads; rows 7-8 are computed ONCE in the constructor (§3.5) when the screen
object is created and never refreshed while it is open.

### 3.3 The feelings rows (`FeelingsClouds` is a REGION, three widgets step down it)

Three bar widgets (`0x2c4370`, `0x2c43b4`, `0x2c43f8`, class ctor `FUN_00143708`, stride 0x44) and
three icon widgets (`0x2c42d0`, `0x2c4304`, `0x2c4338`, ctor `FUN_00143e38`, stride 0x34), static
objects constructed by `FUN_001871b8`, are laid out by the row-layout object `0x2c4440`
(`FUN_002128b0`; draw `FUN_00212940`). Per frame (`0x185458`):

- bars: size 72x22 (ctor, vtable slot 3 with `0x48, 0x16`), style `FUN_001437c8(bar, 1)` =
  PROGRESS (`FUN_001437d0`: trough sprite 0x35 `laptop\barprog`, cap 0x37 `prog_cbit` at x+2 width
  w/6 = 12, body 0x38 `prog_vbit` at x+10 width `int(72 * f * 0.83)` (max 59) at z+1, a mirrored
  cap at x+58 when f > 0.92). The ctor also gives them colours (245,253,0) / (2,149,253) / (254,0,0)
  via vtable slot 4 -- ⚠ style 1 never applies a tint (`FUN_002138e0` is only in the slider branch),
  so the three bars draw in the art's own colour. Same widget class the shop's satisfaction bar uses.
- icons: 32x32 (slot 3 with `0x20, 0x20`), white, sprite ids **5, 6, 7** = `laptop\thoughts\
  tihappy / tinormal / tisad` (registry `FUN_00216028`, laptop table, id at entry+0x10).
- fill: `FUN_00143788(fraction, bar)` from `this+0x620 / +0x624 / +0x628` = happy / middling / unhappy
  fraction of the guests in the park (§3.5).
- layout `FUN_00212928(0x2c4440, 2, 1, 0x32, DAT_002c44e8=32)` = 2 items per row, 1 row, h-gap 50,
  v-gap 32. Items are pushed at the head (`FUN_002127a8`) so the ICON is left, the BAR right:
  icon at (45, y) 32x32; bar at (45+32+50 = **127**, y + (32-22)/2 = **y+5**) 72x22.
- rows at y = 105, then `y += 8 + 32` -> **105, 145, 185** (`DAT_002c44e8` = 32, another "32" that
  is a row step).
- the pill: layout style `+0x3c = 1` draws behind each row (z+4, tint (0,0,255) = blue) a body
  (sprite 0x39, an entry registered with an EMPTY name -- a solid quad) from x=45 width 154 (icon
  to bar end = 199-45), y-8, height 32+16 = 48, and `laptop\prog_wbit` (0x33) end caps 12x48 at
  x = 36 and x = 196 (mirrored). Pills 97..145, 137..185, 177..225 -- they overlap by 8 px because
  the row advance (40) is less than the pill height (48). ⚠ That is what the code draws.

### 3.4 Gate price spinner (`this+0x754`, class `FUN_00207a50`)

- Value: `this+0x7bc = min(1000, price/10)` where price = `FUN_00100d20(park)` = `park[0]`, the gate
  price in tenths (`FUN_00210c98` compares `park[0]` against the guest's cash at the gate). Whole
  dollars, min 0 (`+0x7c4`), max 1000 (`+0x7c8`), step 1 (`+0x7c0`), format 1 = money (`+0x7b8`):
  `FUN_00208098` prints `FUN_00142908` -> "$N" with commas.
- Written back EVERY FRAME: `FUN_001853a8` -> `FUN_00100d18(park, value * 10)`.
- Input (`FUN_00207b10`): mode `+0x7b4 = 2` reads `FUN_00181860` = buttons HELD, so the value moves
  1 per frame while held; raw bit 4 = down, bit 8 = up; clamped, no wrap; sound 0xd6 on change,
  0xaf when it hits a limit. Enabled every frame by `this+0x76c |= 1`.
- Position: value at `GatePriceVal` (260,400) via `spinner+8/+0xa`; arrows at `GatePriceArrows`
  (200,414) via `FUN_00207e18`, drawn by `FUN_00207e30` as the standard arrow pair (spinner+0x24),
  a square of the font line height, variant 1 (`+0x7cc = 2`).
- Colours: enabled text `*0x35f560` = (255,255,0); after the spinner the colour resets to (200,130,0).
- ⚠ Justify: the binder stores NO justify for `GatePriceVal` (only row/col, `0x183d50`) and the
  spinner never sets one, so "$N" is drawn with whatever `FUN_001389c0` left in force -- the
  `GatePriceText` justify, LEFT -- at col 260, despite the scene saying `center`. Likewise the people
  visited number is drawn left at col 260 (`DAT_002c457c` = 0 in the image, never written).

### 3.5 Where the visitor numbers come from (`FUN_00184618`, once, at open)

Walk the guest list from `FUN_0014d218()` (head, next at `*guest`); per guest:

- happiness byte `guest+0x75` (findings/visitors.md): `>= 76` -> happy (`+0x614`), `25..75` ->
  middling (`+0x618`), `< 25` -> unhappy (`+0x61c`). Then `+0x620/624/628` = happy/middling/unhappy
  count / n as FLOATS (the bar fills), and the same three as integer percentages `*100/n` (not drawn
  by `0x185458`; nothing on this page uses them). n = 0 -> all three 0.
- dominant thought class `FUN_00211c80(guest)` -> 0..8, bucketed in `DAT_002c4540[9]`:

| test (first that holds) | class | icon (`DAT_003643b8[class]`) |
|---|---:|---|
| hunger `+0x77` > 80 AND thirst `+0x7a` > 80 | 7 | 0x0e `tihungthir` |
| hunger > 80 | 2 | 0x0c `tihungry` |
| thirst > 75 | 1 | 0x0d `tithirsty` |
| toilet `+0x79` >= 76 | 3 | 0x10 `titoilet` |
| sick `+0x76` > 75 | 4 | 0x09 `tisick` |
| happiness >= 76 | 5 | 0x05 `tihappy` |
| `+0x7b` >= 76 | 6 | 0x0b `tibored` |
| happiness < 25 | 8 | 0x07 `tisad` |
| otherwise | 0 | 0x07 `tisad` -- but class 0 is NEVER shown (below) |

⚠ The "bored" icon keys on `guest+0x7b`, which findings/visitors.md lists as unidentified, while its
boredom is `+0x78`. Reported as read; not resolved here.

- Top three: three passes over buckets **1..8** (index 0 is skipped: the scan starts at
  `DAT_002c4544`), each taking the largest non-zero bucket (earliest index on ties) and zeroing it;
  `DAT_002c4568/6c/70` = the icon ids. Fill-in when fewer than three: 2nd := 1st if unset, then if
  the 3rd is unset `3rd := old 2nd, 2nd := 1st`. All three stay -1 with no guests or no guest in
  classes 1..8, and `0x185458` then draws no thoughts row at all (`if (DAT_002c4568 != -1)`).
- Drawn: layout `(3, 1, 8, 8)`, icons 32x32 at x = 45, 85, 125, y = 284, most dominant first (pushed
  3,2,1 so the head is #1), in a blue pill: body 45..157 (width 112), caps at x = 36 and x = 154,
  y 276..324.
- People visited = `FUN_0016d2e0(FUN_0016ae90())` = `stats+0x20`, the visitor-statistics singleton
  (`DAT_002b9728`, 0x32c bytes). Incremented by `FUN_00210c98` once per guest admitted at the gate
  (price < cash and a route exists); zeroed by `FUN_0016af70`; loaded from a save by `FUN_0016d030`.

## 4. `main_ps_parkfinance.sce` -- Park Finance (id 11)

### 4.1 Layout (names `0x364230..0x364250`)

| element | col | row | justify | globals: justify / row / col |
|---|---:|---:|---|---|
| `TextItems` | 45 | 200 | left | `2c4480` / `2c4520` / `2c451c` |
| `ThisYear` | 250 | 200 | center | `2c4484` / `2c4530` / `2c452c` |
| `LastYear` | 393 | 200 | center | `2c4488` / `2c4538` / `2c4534` |

The scene's own comment "money balance slider thing?" has no element and nothing draws one.

### 4.2 Rows (`FUN_001867b8`; step `DAT_002c4528` = 32; z = 100)

Labels column, col 45, left, colour (200,130,0). ⚠ Row 200 itself is EMPTY in this column; the labels
start one step down:

| y | text id | key | English |
|---:|---:|---|---|
| 232 | 9 | `STR_PARKSTATS_MONEY_IN` | Money In |
| 264 | 664 | `STR_PARKSTATS_MONEY_OUT` | Money Out |
| 296 | 682 | `STR_PARKSTATS_BALANCE` | Balance |
| 328 | 115 | `STR_PARKSTATS_PARK_VALUE` | Park Value |
| 360 | 443 | `STR_PARKSTATS_PARK_RATING` | Park Rating |

"This Year" column, col 250, centre, colour **(255,255,0)**; "Last Year" column, col 393, centre,
colour (200,130,0). Money rows are `FUN_00142908(buf, v / 10)`: integer division of TENTHS, then
`$` + thousands commas, `-` before the `$` when negative ("-$1,234").

| y | This Year (col 250) | Last Year (col 393) |
|---:|---|---|
| 200 | 889 `STR_PARKSTATS_THIS_YEAR` "This Year" | 603 `STR_PARKSTATS_LAST_YEAR` "Last Year" |
| 232 | `FUN_00100f48` = `park+0x12dc` money in since the year roll | `FUN_00100f58` = `park+0x12e0` |
| 264 | `FUN_00100f50` = `park+0x12e4` money out since the year roll | `FUN_00100f60` = `park+0x12e8` |
| 296 | `FUN_00100688` = `park+4`, the CURRENT bank balance | `FUN_00101fa8` = `park+0x12f0` |
| 328 | `FUN_00101288(park, 0)` -> `FUN_001011c8()` park value, live | `FUN_00101fa0` = `park+0x12ec` |
| 360 | rating word for `FUN_0016b438(stats)` | rating word for `FUN_0016d328(stats)` = `stats+0x2fc` |

Rating word: `DAT_002c42c0[min(3, rating / 20)]` = 57 `STR_PARKSTATS_POOR` / 767 `_AVERAGE` /
113 `_GOOD` / 1004 `_EXCELLENT` -- so 0..19 Poor, 20..39 Average, 40..59 Good, 60+ Excellent. The
word is a text id, not a number; nothing prints the rating itself.

### 4.3 What the quantities are

- **Money in / out, this year**: `park+0x12dc` is credited by `FUN_00100750` and `park+0x12e4` debited
  by `FUN_00100698` (findings, `core/ParkFinances.cs`). Both are ZEROED by the year roll `0x100ef8`
  (a leaf with no frame: copies 0x12dc->0x12e0, 0x12e4->0x12e8, zeroes both), whose only caller is
  the calendar `FUN_0016b060` at `0x16b1bc`, taken when the calendar YEAR (`stats+8`) changes at a
  month end. ⚠ `ParkFinances.cs` calls `park+0x12e4` a lifetime total; this screen shows it is the
  this-year figure -- the lifetime spend is `park+0x12d4` (`FUN_00100e38`).
- **Balance**: `park+4`, live; "last year" is `park+0x12f0`.
- **Park value**: `FUN_001011c8()` = sum over every object in the world list (`FUN_001e5af0/ba0/ca8`)
  of `FUN_0012bc10(catalogue, kind, id)` -- the catalogue record's price (+0x50 for kinds 1,3,6,7;
  +0x20 for 2,4,5,8) -- `* 10 / 2`: HALF the purchase price of everything placed, in tenths.
- **Park value / balance, last year**: snapshotted by the month end `FUN_00100a18` at `0x100b80/0x100b8c`
  when `park+0x12bc % 12 == 0 && != 0` BEFORE the counter increments. `park+0x12bc` starts at 0
  (`sw zero,0x12bc(a0)` at `0x100500` in the ctor), so the snapshot lands at month ends 13, 25, 37 ...
  -- one month AFTER the money roll if the calendar starts in month 0. ⚠ A real quirk, not a
  misread; keep it if fidelity is the aim.
- **Park rating, this year**: the byte ring `stats+0x26c[144]`, written by `FUN_0016b478` at every
  month end (called from the calendar at `0x16b194`) with `FUN_00153650()`, the park rating; read
  back at index `FUN_0016b2e8(stats, 0)` = the LAST COMPLETED MONTH (k=0 is treated as 1; -1 -> 0
  before the first month end, which reads "Poor"). Not a yearly average.
- **Park rating, last year**: `stats+0x2fc`, written in the same `FUN_0016b478` only when
  `year != 0 && month == 0` (`FUN_0016b230` / `FUN_0016b228`) -- i.e. the December sample, taken as
  the calendar rolls into month 0. 0 ("Poor") until the first year turns.
- `FUN_00153650`'s composition (a sum of clamped category counts: guests/5 capped 20, ...) is not
  read to the end here; its scale is what makes the /20 banding 0..100.

### 4.4 Behaviour

Nothing interactive: `FUN_00186138` is only the leave-page check of §2.4. No arrows, no paging.

## 5. The control: `main_goldtickets` (id 3), drawn by `FUN_00186238`

Consistent with this reading: the same binder stores `AwardsText` (`2c4608/2c4604/2c460c`), `UCText`
(`2c4614/2c4610/2c4618`), `MedalRow` (row/col/w/h `2c4624/2c4620/2c4628/2c462c`), `StarRow`
(`2c4634/2c4630/2c4638/2c463c`); the page opens from menu row 3; five medal cutouts step across
`MedalRow` at width/5, stars 7 per row across `StarRow` at width/7 and height/2 -- the region-not-widget
pattern again. Two things in the ported `GoldTicketScreen.cs` disagree with the draw:

1. The second heading is **1086 `STR_AWARDS_ULTIMATE` "Ultimate Coaster Awards"** (`0x43e` at
   `UCText`), not 895 "Ulimate Coasters" as `UltimateTextId` says. findings/coaster-operation.md §5.7
   already recorded 0x43e.
2. Medal order across the row is by bit k of `stats+0x28` (`FUN_0016b8e8(stats, k)`, set by
   `FUN_0016b918` in the weekly check `FUN_0016bc70`): k = 0..4 -> sprites **0x26 m_security,
   0x25 m_upgrade, 0x29 m_aesthetic, 0x28 m_green, 0x27 m_path**, not registry order 0x25..0x29.

## 6. Handed to the statistics agent (id 24), not pursued here

The same ctor prepares the graph page: five series objects at `this+0x7d4` (stride 0x68,
`FUN_0013f370`), the graph data `DAT_002c40cc/4130/4194/41f8/425c` (5 x 24 columns) built by
`FUN_00186d38` from the per-month rings `stats+0x2c` (guests in park), `+0xbc` (arrivals = guests now
minus last month, floored), `+0x14c` (mean happiness), `+0x1dc` (mean time in park,
`FUN_00211d48` = now - `guest+0x70`), `+0x26c` (park rating); a second list at `this+0x9f4` with rows
620 / 164 / 475 at an image-constant frame (154, 96, 200x80, `DAT_002c44c0..cc`); a spinner at
`this+0x630` with presets 1, 2, 6, 12 (`FUN_00207f18`); scale maxima `DAT_002c412c / DAT_002c41f4`.
Binder globals for its eight elements: `Items 2c45fc/f8/2c4600`, `GraphLegend 2c45c4/c0/2c45e0`,
`YearSelect 2c45dc/d8/2c461c`, `YearSelectValue 2c45f0/ec/2c45f4`, `YearSelectArrow 2c45e8/e4`,
`GraphText 2c45cc/c8`, `GraphValue 2c45d4/d0`, `Graph 2c45b4/b0/b8/bc`.

## 7. Not determined, and what would settle it

- The calendar's starting month (`FUN_0016b240`, unread): decides whether the value/balance
  snapshot lags the money roll by exactly one month or by more.
- Sprite 0x39 (the pill body) is registered with an empty name at `0x36d8c0`; that it renders as a
  solid quad is inferred from its use, not read from the sprite loader (`FUN_002156a0`).
- The physical buttons behind raw bits 1/2/4/8 and logical 0/1/0xc/0xd: the port's existing menu
  code already maps them; nothing here is new.
- Whether the (200,130,0)/(255,255,0) text colours carry the shop screen's drop shadow: not in
  these draws; the port's laptop text path decides.
- `FUN_00100d28`, what the gate actually charges, is unrelated to what is displayed.

## 8. In the port (2026-09-30, strawberry: "flesh out a bunch of statistics, etc in the laptop uis")

Wired, each against the routine named above; the core half is `ParkStatsChecks.cs` (`--parkstats-only`, 12 checks):

- **Recording.** `ParkStatistics` is the stats singleton's five rings + People Visited + the December rating sample;
  `ParkManagement.MonthChanged` records it after the finances' month end, before the month counter moves
  (`0x16B478`). The guest walk is `Viewer.VisitorSample`; the rating is `AdvisorProducers.ParkRating` (`0x153650`).
  The arrival stamp `g+0x70` is set where the native entrance accepts a guest, the same place People Visited bumps.
- **Park Finance.** This year / last year Money In, Money Out (`0x12dc/e0`, `0x12e4/e8`, rolled by `0x100EF8` at the
  year change), balance and park value (the month-13 snapshots `0x12ec/f0`), and the rating word for `Rating(0)` and
  `+0x2FC`. The lifetime Cash In/Out (`0x12d8/d4`) are separate fields and do not roll.
- **Park Statistics.** All five series through `0x186D38`'s own walk, the per-series maxima, and the
  `GraphText`/`GraphValue` readout. ⚠ **The graph is drawn at roughly HALF the series' height, and that is retail.**
  The walk assigns (`0x186EAC sw v0,0(t0)`, pinned by the audit), so with the one-year span every second bucket is a
  single month divided by two, and the plotter's rising step takes the LOWER point -- so the steps read the halved
  values, and only the first step (buckets 0 and 1 are both one whole month) stands at full height. A tall first
  bar over a half-height staircase is the expected picture, not a bug.
- **Visitor Information.** People Visited, Ticket Price, and the dominant-thoughts row (§3.5's top three + fill-in).
- **Overall Statistics.** Park Value from the month-end ring `+0x107c`, the live value at k = 0.

**The control.** `--stats-demo --wind-months=N` with `--laptop-screen=push:<screen>` swaps the month end's inputs for
a feed predictable from the month index (people 10+5m, happiness 40+3m, time 2+m, rating 10+4m, value
$5,000+$1,000m, $200 in / $120 out a day, an admission every third day). At N = 14 every figure on Park Finance
matched the arithmetic (last year $73,000 / $43,800 = 365 days; this year $11,800 / $7,080 = 59; last-year balance at
the 13th month end; Good at m = 12 against Excellent at m = 13). The thoughts row has no guest to read under the demo,
so it is a drawing check only. ⚠ Use `--laptop-film=3` or more: the shot reads the PREVIOUS frame's texture, so a
one-frame film photographs the park from before the screen opened.

Not ported: the gate-price spinner (§3.4), the year selector and series toggles on the graph page, Existing Loans,
Research, ride/toilet Users, Satisfaction.
