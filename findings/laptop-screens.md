# The laptop's screens: what is left, and how each one is driven

**8 of 29 `.sce` screens are implemented. 21 are not.** This is the survey master asked for on
2026-09-27: what each remaining screen is, how the console drives it, and why.

## ⭐⭐ Every screen's menu id, from the registry rather than by guessing

`FUN_001fc778` is one long init that writes **27 scene records at a 0x10 stride from `0x2ec718`**
-- `{ .sce path, menu name, -1, 0 }` each. The slot order below is that table's order, and

> **menu id = slot + 1**

**Three controls, and all three pass.** The port already knew two ids from other work -- Shop
`0x11` and Ride `0x0E` -- and this rule reproduces both (slots 16 and 13). It also reproduces
`main_goldtickets` = 3, which had been derived separately. A rule that agrees with three ids
obtained by different routes is the evidence; one would not have been.

⚠ **`main_bh_items.sce` and `main_bh_staff.sce` are NOT in the registry.** 27 records, 29 files.
They are not screens at all -- they are sub-panels of Build & Hire, which is why the same binder
(`FUN_00197100`) reaches all three. Any design that gives them their own menu id is wrong.

## The 27 registered screens

| id | scene | state | binder | evidence |
|---:|---|---|---|---|
| 1 | `main_gameoptions.sce` | TODO | **none in the exe** | see below |
| 2 | `main.sce` | done | `--` | -- |
| 3 | `main_goldtickets.sce` | done | `FUN_00183d50` | 2/4 |
| 4 | `main_financialinfo.sce` | TODO | `--` | -- |
| 5 | `main_fi_balancesheet.sce` | TODO | **none in the exe** | see below |
| 6 | `main_fi_newloan.sce` | TODO | `FUN_00133960` | 4/4 |
| 7 | `main_buildhire.sce` | TODO | `FUN_00197100` | 3/3 |
| 8 | `main_parkstats.sce` | TODO | `--` | -- |
| 9 | `main_research.sce` | TODO | **none in the exe** | see below |
| 10 | `main_ps_visitorinfo.sce` | TODO | `FUN_00183d50` | 9/9 |
| 11 | `main_ps_parkfinance.sce` | TODO | `FUN_00183d50` | 2/2 |
| 12 | `main_info.sce` | done | `--` | -- |
| 13 | `main_i_ride.sce` | TODO | `FUN_001098b0` | 5/5 |
| 14 | `main_i_ride_data.sce` | done | `FUN_001d3f98` | 3/10 |
| 15 | `main_i_staff.sce` | TODO | `FUN_001ff248` | 1/3 |
| 16 | `main_i_shop.sce` | TODO | `FUN_0010a468` | 1/1 |
| 17 | `main_i_shop_data.sce` | done | `FUN_001d68b0` | 3/5 |
| 18 | `main_i_staff_opts.sce` | TODO | `FUN_001d8dd0` | 1/1 |
| 19 | `main_i_sideshow_data.sce` | done | `FUN_001d7a30` | 4/6 |
| 20 | `main_i_sideshow.sce` | TODO | `--` | -- |
| 21 | `main_fi_financestats.sce` | TODO | `--` | -- |
| 22 | `main_fi_overallstats.sce` | TODO | `--` | -- |
| 23 | `main_i_bathroom_data.sce` | TODO | `--` | -- |
| 24 | `main_ps_statistics.sce` | TODO | `FUN_00183d50` | 3/3 |
| 25 | `main_i_ride_data_upgrd.sce` | TODO | `FUN_001d3f98` | 3/7 |
| 26 | `main_i_bathroom.sce` | TODO | `FUN_0010cf18` | 2/2 |
| 27 | `main_i_staff_opts_training.sce` | TODO | `FUN_001ff248` | 4/4 |

Two sub-panels sit outside that table: `main_bh_items.sce` and `main_bh_staff.sce`, both bound by
`FUN_00197100` (2/2 and 3/3).

## How the binders were found, and what that census can and cannot say

Every binder resolves its elements by NAME: `FUN_00177978(scene, "SatisfactionBar")`. So the
element names authored in the `.sce` are string constants in the executable, and whichever
function materialises them is that screen's binder. The sweep finds each name's address, scans for
the `lui`+`addiu`/`ori` pair that builds it, and attributes the hit to the enclosing function by
back-scanning for `addiu sp, sp, -imm`.

⭐ **The control: `main_i_shop_data` must come out as `FUN_001d68b0`**, the one binder already
proven by hand. It does. That is what makes the other rows worth reading.

⚠ Names like `Model`, `textoptions` and `ItemSelect` are authored by a dozen screens and share one
string, so a hit on them attributes nothing. The table above scores **only names authored by at
most two screens**; the generic ones are discarded rather than counted. Before that filter a
shared helper (`FUN_001d9cb8`) looked like the binder for ten different screens.

⚠⚠ **A MISS HERE IS NOT A PROOF OF ABSENCE.** The scan sees `lui`+`addiu` only. PS2 code also
reaches data `$gp`-relative, and such a reference is invisible to it. Rows marked `--` mean *not
attributed*, never *unbound* -- with one exception, which has its own evidence below.

## ⭐⭐ Three screens' element names are ABSENT FROM THE EXECUTABLE -- but see the refinement

Not "not attributed" -- **absent**, as byte strings, searched case-folded across the whole image:

| screen | id | names that do not exist in the exe |
|---|---:|---|
| `main_fi_balancesheet.sce` | 5 | `numericoptions` |
| `main_gameoptions.sce` | 1 | `musicslider`, `musicslidertext`, `sfxslider`, `sfxslidertext` |
| `main_research.sce` | 9 | `researchbars`, `researchitem` |

The same search finds `CostItem`, `SatisfactionBar`, `TextOptions`, `YearSelect`, `Items`, `Model`
and `ItemSelect` immediately, so it reaches what it should.

⚠⚠ **REFINED, and this matters: those screens ARE implemented.** The vtable census below finds a
full screen class for `main_gameoptions` (vtable `0x35e9e0`, ctor `FUN_00139ba0`, registers
`STR_OPTIONS_TV_MODE` / `STR_FRONTEND_SPEECH_ON`) and for `main_research` (vtable `0x365e38`, ctor
`FUN_001b5410`, title `STR_RESEARCH_RESEARCH` = "Research"). So the first reading -- "the console
may not draw these" -- was too strong.

⭐ **What is actually true:** those screens exist and run, they just **do not resolve their
elements by name out of the `.sce`**. Their coordinates come from somewhere else -- almost
certainly hardcoded -- so for these three the `.sce` is NOT the authority the way it is for the
other eighteen. Port their layout from a render or from the draw code, not from the file.

## ⭐⭐⭐ Every screen is a C++ class, and that hands over all of them at once

`FUN_001d6bc8`, the caller of the shop's binder, is a **constructor**: it stores a vtable at
`this+0x10`, builds its sub-objects, calls the binder, and registers its text ids. So every screen
is a class, and the class is the thing to read.

**The vtables share an inherited tail** -- slots 4..8 are `164d00 1649c0 165df8 165e00 165e08` in
every one of them. Scanning the image for that five-pointer signature finds **25 screen vtables**.
⭐ Control: the 11 vtables already tied to a screen by hand all appear in that census.

⭐⭐ **And the title names the screen.** The constructor passes its title's text id to
`FUN_00165948(this+0xa0, id)`, and `Text/translations/eur/id.dat` turns that id into a symbolic
name. The shop's is `STR_SINGLESHOP_SINGLE_SHOP` = "Single Shop". That single call identifies
nearly every class with no guessing at all:

| vtable | ctor | title id | title | screen |
|---|---|---:|---|---|
| `0x358eb8` | `FUN_00109b90` | 432 | "All Rides" | `main_i_ride` |
| `0x358ff0` | `FUN_0010a6b8` | 419 | "All Shops" | `main_i_shop` |
| `0x359130` | `FUN_0010b130` | 184 | "All Sideshows" | **`main_i_sideshow`** |
| `0x359250` | `FUN_0010bb68` | 999 | "All Staff" | **`main_i_staff`** |
| `0x359710` | `FUN_0010d118` | 848 | "All Toilets" | `main_i_bathroom` |
| `0x35c530` | `FUN_0012bfa8` | -- | `STR_MAINMENU_MAIN_MENU` | `main` (the laptop menu) |
| `0x35e540` | `FUN_00134188` | 925 | "Finance" | **the finance family** |
| `0x35e9e0` | `FUN_00139ba0` | -- | `STR_OPTIONS_TV_MODE` | **`main_gameoptions`** |
| `0x35f018` | `FUN_0013e868` | 398 | "Gold Ticket" | `main_goldtickets` |
| `0x363bb0` | `FUN_0017f688` | 410 | **"Not Implemented"** | -- see below |
| `0x364318` | `FUN_00184618` | 530 | "Information" | `main_ps_visitorinfo` |
| `0x364f88` | `FUN_001976b0` | 178 | "Purchase" | `main_buildhire` |
| `0x365e38` | `FUN_001b5410` | 1013 | "Research" | **`main_research`** |
| `0x368868` | `FUN_001d4670` | 407 | "Single Ride" | `main_i_ride_data` |
| `0x368ae0` | `FUN_001d6bc8` | 315 | "Single Shop" | `main_i_shop_data` |
| `0x368d00` | `FUN_001d7dd8` | 23 | "Single Sideshow" | `main_i_sideshow_data` |
| `0x368e70` | `FUN_001d8ee8` | 655 | "Single Staff" | `main_i_staff_opts` |
| `0x3690e0` | `FUN_001d9ec8` | 568 | "Single Toilet" | `main_i_bathroom_data` |
| `0x369700` | `FUN_001dcaa0` | 589 | "Staff Room" | **a Staff Room screen** |
| `0x36b9f8` | `FUN_001ff4a8` | 655 | "Single Staff" | `main_i_staff_opts_training` |

⚠ `FUN_00134188` carries the title "Finance" yet binds `main_fi_newloan`'s four elements (4/4).
Both are true: it is the **finance family's** class, and the loan page is one of its pages. Do not
read it as "the new-loan screen".

### Three of the 25 are BASE CLASSES, not screens

Established by who calls them, not by shape:

- **`FUN_001648a8`** (vtable `0x361a60`) -- called by **all 15** screen constructors. The root.
- **`FUN_0010c928`** (vtable `0x3595f0`) -- called by exactly the five "All ..." screens
  (ride, shop, sideshow, staff, toilets). The **list** base.
- **`FUN_001d9500`** (vtable `0x368f50`) -- called by exactly the four "Single ..." data screens
  (ride_data, shop_data, sideshow_data, bathroom_data). The **item** base.

⭐⭐ That grouping is the port's shape: the console did not write these screens one at a time
either. Port the two bases and the five list screens and the four item screens mostly follow.

### ⭐⭐ "Not Implemented" is a real class -- and NOTHING CONSTRUCTS IT

`FUN_0017f688` builds a screen whose title is literally `STR_NOTIMP_NOT_IMPLEMENTED`
("Not Implemented"). A `jal` census over the whole image finds **zero callers**. So the PS2 does
not route any laptop entry to a stub: every menu row reaches a real screen. Worth knowing before
anyone concludes from a blank page that a screen was cut.

### What the three per-screen vtable slots are

Slots 4..8 are inherited and identical everywhere; only slots 1..3 are overridden. Reading the
shop's three:

- **slot 1** (`FUN_001d7960`) -- **destructor**: tears its sub-objects down and chains to the
  "Single item" base destructor `FUN_001d9b78`.
- **slot 2** (`FUN_001d70a0`) -- the **per-frame method**, and it is *eight lines*: it forwards
  straight to the base implementation, `FUN_001d98c8(this, 0, 0, 0)`.
- **slot 3** (`FUN_001d6f08`) -- a **lifecycle hook**: base `FUN_00164b70`, two further virtuals,
  then the inherited slot 4 `FUN_00164d00`.

⭐⭐⭐ **THE FINDING THAT SHOULD SHAPE THE PORT.** The per-screen draw is a *forwarder*. The
work lives in the shared base, and the subclass supplies constants. So this is not 21 screens to
write; it is **two or three base implementations plus a table of per-screen constants**. Anyone
starting from "implement main_i_sideshow" is about to write the base class for the fourth time.

⚠ **A limit of the sweep, stated so nobody trusts it too far.** The function-start heuristic
back-scans for `addiu sp, sp, -imm`, so a small leaf function with no stack frame is attributed to
whatever function precedes it. That is real and it bit here: several screen creators came back as
`FUN_001c5680`, which decompiles to a shutdown routine and calls none of them. The distinct
creator addresses (`0x1c5ce0` game options, `0x1c7108` research, `0x1c78d0` finance, `0x1c6e78`
staff room, and `0x1c7220`/`0x1c7650`/`0x1c7790`) are right; their *enclosing* names were not.

## Screen families -- one binder, several screens

- **`FUN_00183d50` -- the whole Park Statistics family**: `main_ps_visitorinfo` (9/9),
  `main_ps_statistics` (3/3), `main_ps_parkfinance` (2/2) and `main_goldtickets` (2/4).
  ⭐ This *corroborates independently* what the gold-tickets work concluded from a different
  direction -- that its every string is `STR_PARKSTATS_*` and it opens from Park Statistics.
  Two routes, same answer.
- **`FUN_00197100` -- Build & Hire**: `main_buildhire` plus both unregistered sub-panels.
- **`FUN_001d3f98` -- a ride's data and its upgrade page**: `main_i_ride_data`,
  `main_i_ride_data_upgrd`.
- **`FUN_001ff248` -- staff**: `main_i_staff`, `main_i_staff_opts_training`.
- **`FUN_00133960` -- finance**: `main_fi_newloan`, and the year/graph screens share its elements.

⭐ **Port them by family, not one screen at a time.** The console shares the binder, so the
list/arrow/graph machinery is shared too; doing `main_i_ride` after `main_i_ride_data` is mostly
the same code twice.

## What each remaining screen actually is

- **`main_gameoptions.sce`** (id 1) -- musicslider, musicslidertext, sfxslider, sfxslidertext, textoptions
- **`main_financialinfo.sce`** (id 4) -- textoptions
- **`main_fi_balancesheet.sce`** (id 5) -- textoptions, numericoptions
- **`main_fi_newloan.sce`** (id 6) -- LoanText, LoanInformation, LenderNameArrows, LenderName
- **`main_buildhire.sce`** (id 7) -- TextOptions, ItemList, Stock, ItemCountList
- **`main_parkstats.sce`** (id 8) -- TextOptions
- **`main_research.sce`** (id 9) -- textoptions, researchbars, researchitem
- **`main_ps_visitorinfo.sce`** (id 10) -- FeelingsText, FeelingsClouds, ThoughtsText, ThoughtsClouds, PeopleVisitedText, PeopleVisitedVal, GatePriceText, GatePriceVal, GatePriceArrows
- **`main_ps_parkfinance.sce`** (id 11) -- TextItems, ThisYear, LastYear
- **`main_i_ride.sce`** (id 13) -- ItemSelect, ItemSelectArrows, InfoText, InfoVal, ExcitementBar, StateOfRepairBar, RemainingLifeBar, ThingModel
- **`main_i_staff.sce`** (id 15) -- item, itemarrows, textoptions, infobars, InfoValues, Model
- **`main_i_shop.sce`** (id 16) -- ItemSelect, ItemSelectArrows, InfoText, satisfactionbar, InfoValues, Model
- **`main_i_staff_opts.sce`** (id 18) -- Staffname, textoptions, Model
- **`main_i_sideshow.sce`** (id 20) -- ItemSelect, ItemSelectArrow, InfoText, InfoValues, excitementbar, satisfactionbar, NumericItems, Model
- **`main_fi_financestats.sce`** (id 21) -- YearSelect, YearSelectValue, YearSelectArrow, Items, graph
- **`main_fi_overallstats.sce`** (id 22) -- YearSelect, YearSelectArrow, YearSelectValue, Items, graph
- **`main_i_bathroom_data.sce`** (id 23) -- text, ItemSelect, textoptions, NumericItems, cleanlinessbar, Model
- **`main_ps_statistics.sce`** (id 24) -- GraphText, GraphValue, YearSelect, YearSelectValue, YearSelectArrow, Items, graph, GraphLegend
- **`main_i_ride_data_upgrd.sce`** (id 25) -- UpgradeName, UpgradeNameArrows, costtext, costvalue, stocktext, stockvalue, UpgradeModel
- **`main_i_bathroom.sce`** (id 26) -- ItemSelect, ItemSelectArrow, CleanlinessText, CleanlinessBar, Model
- **`main_i_staff_opts_training.sce`** (id 27) -- Item, InfoText, TrainingVal, SkillLevelBar, CostWageVal, Model

## What is NOT answered yet

- **The menu-id -> screen-class routing.** Each class's creator is known but the table that picks
  one from a menu id is not read yet; the creators cluster in `0x1c5000..0x1c8000`.
- **The data source behind each value.** Knowing `GatePriceVal` has a frame does not say which
  park field fills it. That is per-screen work and it is the larger half.
- **Why three screens' names are missing from the image** (above).
- **The `--` rows.** Not attributed, not disproven: try a `$gp`-relative sweep before concluding
  anything about them.


## ⭐⭐⭐ How much of this is actually CODE: eleven of the screens are configuration

The per-screen `slot 2` forwards into the base, and the base (`FUN_001d98c8`) turns out to be a
**state machine**: it reads the screen's state at `this+0x2f4` and dispatches to a handler at
vtable slot 26, 28 or 29 depending on it. ⭐ Control: that reading predicts the shop's state-0
handler is slot 26, and slot 26 of `0x368ae0` is **`FUN_001d70c8`** — the draw identified by hand,
from the other direction, before any of this. The state machine is right.

Reading each vtable to ITS OWN END (stop at the first word that is not a code pointer) then gives
the real measure of how much code each screen has:

| screen | vtable slots | own methods beyond the shared 0..8 |
|---|---:|---|
| `main_i_ride`, `main_i_shop`, `main_i_sideshow` | 17 | **none** |
| `main_i_staff`, `main_i_bathroom` | 18 | **none** |
| `main_gameoptions`, `main_research`, `main_goldtickets` | 17-18 | **none** |
| `main_ps_visitorinfo`, `main_buildhire` | 17 | **none** |
| "Not Implemented" | 17 | **none** |
| `main_i_staff_opts`, Staff Room, `main_i_staff_opts_training` | 18 | one (slot 16) |
| `main` (laptop menu) | 22 | 3 |
| finance family | 30 | 11 |
| `main_i_shop_data`, `main_i_sideshow_data`, `main_i_bathroom_data` | 32 | 15 |
| `main_i_ride_data` | 48 | 28 |

⚠ Every screen does override slots 1..3 (destructor, per-frame forwarder, lifecycle hook) — the
column above counts what it adds *beyond* that.

**So the remaining 21 screens are not 21 jobs.** Eleven of them add no behaviour at all: they are
the base class plus a constructor's worth of constants — a title text id, which list to walk, and
which `.sce` elements to bind. The code that must actually be written is:

1. the **All-list base** (`FUN_0010c928`, 17 slots) — and `main_i_ride`, `main_i_shop`,
   `main_i_sideshow`, `main_i_staff`, `main_i_bathroom` all fall out of it,
2. the **Single-item base** (`FUN_001d9500`, 32 slots, the state machine) — and `main_i_shop_data`,
   `main_i_sideshow_data`, `main_i_bathroom_data` are it plus one state-0 handler each
   (`1d70c8`, `1d8288`, `1da008`),
3. the **finance family** (`FUN_00134188`, 30 slots),
4. the **laptop menu** (`FUN_0012bfa8`, 22 slots).

⭐ `main_i_ride_data` is the outlier at 48 slots, and its slots 38..47 are byte-identical to the
finance family's 18..27 — it carries a finance sub-object. Expect it to be the most work of any
single screen, and do it AFTER the finance family rather than before.

⚠ This measures how much each screen OVERRIDES, which is not the same as how much behaviour it
has — a screen with no overrides still does whatever its constructor's constants make the base do,
and those constants are not decoded here. It bounds the work; it does not describe the screens.

## ⭐⭐⭐ What the eleven "configuration" screens are configured WITH

Reading a list screen's constructor (`FUN_00109b90`, All Rides) shows exactly what a subclass
supplies. Beyond the vtable and its sub-objects it is three things:

```c
FUN_0010c928();                      // the All-list base constructor
*(this + 0x10) = &DAT_00358eb8;      // its vtable
FUN_00165948(this + 0xa0, 0x1b0);    // TITLE text id -- 432, STR_ALLRIDES_ALLRIDES
*(this + 0x178) = 0x141;             // a second text id
FUN_0015c710(this + 0x2f4, 3);       // and a list of CATEGORY numbers
FUN_0015c710(this + 0x2f4, 7);
FUN_0015c710(this + 0x2f4, 6);
FUN_0015c710(this + 0x2f4, 1);
```

`this + 0x2f4` is the same field the base's state machine reads. The numbers pushed into it are
**`AssetResourceDatabase.AssetKind` values** -- the enum this port already has:

> `Coaster = 1, Feature = 2, Ride = 3, Shop = 4, Sideshow = 5, TrackRide = 6, TourRide = 7,`
> `TrackUpgrade = 8`

| screen | registers | reads as |
|---|---|---|
| All Rides | 3, 7, 6, 1 | Ride, TourRide, TrackRide, Coaster -- **every ride kind** |
| All Shops | 4 | Shop |
| All Sideshows | 5 | Sideshow |
| All Toilets | 2 | Feature |
| All Staff | (none) | staff are not assets, so no kind applies |

⭐⭐ **Four of the five land on exactly the kind set their title promises, with no interpretation
required.** That is what makes the five list screens one class: they are the same code filtered to
different `AssetKind`s, and the filter values are an enum this port already decoded for the Build
menu (see `BuildCategoryNames`, which derived the same enum from the other end -- the
`STR_PURCHASE_*` names and master's requested ordering).

⭐⭐ **All Toilets did not simply read, and reading one more function said why.** `FUN_0015c710`
is not a "register" call at all: it walks the world's object list from `FUN_0014da50`, asks each
object its kind through a virtual at `vtable+0xa4`, and appends every match. So the number is a
kind and the call populates -- **and it carries exactly one special case, for kind 2 only.** When
the kind is `Feature` it asks the object a second question, a virtual at `vtable+0x134`, and skips
it when that returns zero.

That is the narrowing All Toilets needs, and it was found by reading the populate function instead
of guessing at the number.

⭐⭐ **AND THE PREDICATE IS NOW READ: it is `record[0x2E] & 1`.** Across the ten world-object
classes that share the kind getter at `vt+0xA4`, `vt+0x134` has exactly TWO implementations --
nine take the base and one class (vtable `0x35DC70`) overrides it with `FUN_00130718`, which
returns that bit.

⭐ **The control passes and is worth stating precisely:** on FANTASY the bit is set by **2 of 36
features -- `loo.mps` and `royaloo.mps`** -- and by no bin, bench or tree. A predicate that picked
half the features, or none, would have been the wrong one; this picks exactly the toilets.

⚠ It is carried as `Entry.FeatureFlag0` rather than `IsToilet`: the byte's other bits are unread
and nothing on the disc names it. What is established is what the console DOES with bit 0.

⚠ One tension left honest rather than resolved: the base's state machine reads `*(int*)(this+0x2f4)`
as a small mode (0, 2, 3), while this function treats `this+0x2f4` as a list object with its own
vtable. Both are observed. They are compatible only if word 0 of that member is a mode field, which
is plausible but not shown here.

⚠⚠ **`this + 0x178` IS NOT A TEXT ID, and calling it one was my error.** I named it "a second
text id" purely because the title id sits nearby. Passing the actual values through the table is
the control, and it fails: 321 -> "Pretty Good", 322 -> "Welcome message", 323 -> "deg", 325 ->
"Your stock of rides is running". They are also not unique per screen -- All Rides and All Toilets
both store 321, All Shops and Single Shop both store 322 -- which a per-screen label could not be.
So it is **an unidentified constant**, and the shared values hint at a kind or class tag rather
than a string. Recorded as unread.

### What this leaves

The research question is answered: the remaining screens are four implementations plus a table of
constants, and for the five list screens the table is **a title id and a set of `AssetKind`s**,
both of which this port can already produce. The honest gaps are the `this+0x178`
constant, the All Toilets filter, and the menu-id -> class routing.

## ⭐⭐ The widget inventory is in the CONSTRUCTOR, not the draw

Starting to build the list screens immediately hit a wall worth recording. `LaptopScreens` gets a
screen's rows by counting widget calls in its DRAW (`FUN_00115590` a bar, `FUN_001DAAE0` a
slider), and that method does not transfer to the list screens: `main_i_ride`'s `.sce` declares
three bars and its draw calls the bar function **zero** times. The bars are drawn by the base.

⚠ Those draws also decompile badly at their vtable entry points -- `unaff_s0`, `unaff_retaddr`,
register saves read as uninitialised -- so Ghidra's bounds there are not to be trusted either.

⭐ **The constructor answers it instead, and cleanly.** A screen builds its widgets as members:
`FUN_00115468` constructs a bar, `FUN_001da630` a slider. Counting those calls per constructor
gives an inventory that can be checked against the `.sce`, which declares the same widgets by name:

| screen | ctor bars | ctor sliders | `.sce` declares |
|---|---:|---:|---|
| `main_i_ride` | 3 | 0 | ExcitementBar, StateOfRepairBar, RemainingLifeBar |
| `main_i_shop` | 1 | 0 | satisfactionbar |
| `main_i_sideshow` | 2 | 0 | excitementbar, satisfactionbar |
| `main_i_bathroom` | 1 | 0 | CleanlinessBar |
| `main_i_ride_data` | 4 | 3 | 4 bars, 3 sliders |
| `main_i_shop_data` | 1 | 2 | satisfactionbar, qualityslider, additiveslider |
| `main_i_bathroom_data` | 1 | 0 | cleanlinessbar |
| `main_i_staff_opts_training` | 1 | 0 | SkillLevelBar |
| `main_gameoptions` | 0 | 2 | musicslider, sfxslider |

**Nine screens agree exactly**, including `main_i_ride_data` at 4 bars and 3 sliders and
`main_i_shop_data` at 1 and 2 -- and `main_i_shop_data`'s counts are the control, because
`LaptopScreens.Shop` already declared that shape from the draw.

⭐⭐ **`main_gameoptions` builds exactly two sliders**, matching the `musicslider` and `sfxslider`
its `.sce` declares -- even though those element names appear nowhere in the executable. That
closes the question this document opened: the screen is implemented, its layout does match the
authored file, and only the by-name lookup is missing. The first reading of that finding was wrong
and this is what settles it.

⚠ Three screens do NOT agree, and they are flagged rather than smoothed:
- `main_i_staff` builds 3 bars against one element named `infobars` -- a plural name covering three.
- `main_i_sideshow_data` builds 3 bars, its scene names 2.
- `main_i_staff_opts` builds 3 bars and its scene names none at all.
- `main_research` builds 1 slider against an element named `researchbars`.

So the scene file names a REGION in at least some cases, not one widget each, and a port that
assumes one element = one widget will be wrong on those four.

## ⭐⭐ The upgrade page is a TAB of the ride screen, not a screen

`main_i_ride_data_upgrd.sce` is a layout with no screen behind it. Four things say so together:

- **No class.** The vtable census found 25 screen classes and read a title out of 20 of them.
  None is the upgrade page. Every other `.sce` that is a screen has one.
- **It shares a binder** with `main_i_ride_data` (`FUN_001D3F98`), which is what a second layout
  belonging to the same screen would do.
- **The `STR_SINGLER_*` family carries tab strings**: Details (8), Upgrades (119), Options (218),
  Addons (454). "Upgrades" is a tab of the ride screen, named in the ride screen's own family.
- **The ride screen has the extra state handlers to serve them.** The Single-item base dispatches
  on state to vtable slots 26/28/29; `main_i_ride_data` overrides all three (`1d5210`, `1d6058`,
  `1d6338`) where shop, sideshow and toilet override only slot 26 and inherit the rest.

⚠ **What that does NOT establish** is a rule "own state slots = number of tabs" -- it fails:
`STR_SINGLEBOG_*` has three tab strings (Details, Options, Upgrades) and the toilet screen
overrides only one slot, while shop and sideshow have two tabs each and also override one. The
count is not the tab count; only the *direction* holds (the richest screen has the most).

⭐ So the earlier "four implementations" figure is one better than it looked: the upgrade page is
a mode of a screen already ported, not a fourteenth screen to write.

## ⚠ All Staff: a SECOND reason it is held back

Its three bars share one authored element, which was the first reason. Reading its scene gives
another, independent one: **its value column is not on its label column's row.**

```
textoptions   row 175 col  45      the labels
infobars      row 175 col 215      72x22, one element for three bars
InfoValues    row 220 col 250      the values -- FORTY-FIVE rows lower
```

Every other list screen authors the label and value elements on the SAME row (All Rides 108/108,
All Shops 175/175, All Sideshows 175/175). All Staff does not, so the stepping that serves the
other four cannot be assumed to serve it: stepping both columns by 32 from their own origins puts
its two value rows above their own labels. Whatever it does is its own arrangement, and it is
still unread. The screen waits.
