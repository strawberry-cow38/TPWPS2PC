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

## ⭐⭐ Three screens' element names are ABSENT FROM THE EXECUTABLE

Not "not attributed" -- **absent**, as byte strings, searched case-folded across the whole image:

| screen | id | names that do not exist in the exe |
|---|---:|---|
| `main_fi_balancesheet.sce` | 5 | `numericoptions` |
| `main_gameoptions.sce` | 1 | `musicslider`, `musicslidertext`, `sfxslider`, `sfxslidertext` |
| `main_research.sce` | 9 | `researchbars`, `researchitem` |

The same search finds `CostItem`, `SatisfactionBar`, `TextOptions`, `YearSelect`, `Items`, `Model`
and `ItemSelect` immediately, so it reaches what it should.

**What this means, stated no more strongly than the evidence allows:** whatever draws those three
screens does **not** use the `.sce` frame lookup that every other screen uses. Their scenes *are*
registered, so the console knows them. Either they are drawn from hardcoded coordinates, or the
layouts shipped for a version that reads them and this one does not -- the same shape as the
seaplane and the ferry, which ship complete and which the PS2 never runs.

⚠ Do not port these three off the `.sce` until that is settled. For the other 18 the `.sce` IS the
authority (see `SceneLayout`); for these it is not established that anything reads it.

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

- **The draw function for each screen.** Only the binders are attributed here. The shop's pair is
  binder `FUN_001d68b0` / draw `FUN_001d70c8`; the other draws are unfound.
- **The data source behind each value.** Knowing `GatePriceVal` has a frame does not say which
  park field fills it. That is per-screen work and it is the larger half.
- **Why three screens' names are missing from the image** (above).
- **The `--` rows.** Not attributed, not disproven: try a `$gp`-relative sweep before concluding
  anything about them.

