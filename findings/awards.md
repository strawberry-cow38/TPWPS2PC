# Gold Tickets, awards and the park calendar

Master, 2026-09-27: *"wanna work on the gold ticket/reward / dd/mm/yyyy UI. do the what -> how/why
method."* This is the WHAT and the HOW. The calendar half shipped (`ParkClock`, the bottom-left
HUD); the awards screen is researched and not yet built.

## WHAT — the screen already exists as a layout

`MENUS.WAD/main_goldtickets.sce`, four elements:

```
awardstext  row 65  col 45            ; the "Awards" heading
MedalRow    row 102 col 45  265x96    ; medal cutouts + icons
uctext      row 227 col 45            ; the "Ultimate Coasters" heading
StarRow     row 265 col 45  427x180   ; star cutouts + icons
```

`FUN_001fc778` is the scene registry and lists all 27 `.sce` screens, `main_goldtickets` among
them. ⚠ There is no Gold Tickets row in the laptop's main menu; every string on the screen is
`STR_PARKSTATS_*`, so it hangs off Park Statistics.

## WHAT — five medals, and they are five hidden awards

| art (`UI.WAD/awards/`) | text key | label | earned by (from the advisor message) |
| --- | --- | --- | --- |
| `M_AESTHETIC` | `STR_AWARDS_AESTHETIC` | Aesthetic | a large park full of beautiful features |
| `M_GREEN` | `STR_AWARDS_GREEN` | Green | enough shops have litter bins |
| `M_PATH` | `STR_AWARDS_PATH_ECONOMY` | Path Economy | visitors do not walk too far to reach rides |
| `M_SECURITY` | `STR_AWARDS_SECURITY` | Security | thorough security coverage |
| `M_upgrade` | `STR_AWARDS_UPGRADE` | Upgrade | a large percentage of rides upgraded |

Each pays a Gold Ticket. ⚠ There are about ten MORE ticket sources that are not medals and have no
row on this screen: Log Flume, Go Kart, Roller Coaster, Water Ride, Tutorial, Visitors, Business,
Profit, Placement, Mini Game.

⚠ `STR_MAINMENU_GOLDEN_TICKETS` displays **"Camcorder"** — the key and the string disagree, the same
shape as `Info.Name` not being the displayed name.

## WHAT — Ultimate Coasters is one star per coaster, and there are fourteen

`UI.WAD/UltimateC/` ships a generic `Star` plus 14 named ones. ⭐ **Fourteen is also exactly the
number of `.sam` shapes carrying the `<`/`>` station markers** — two censuses run for different
reasons landing on the same set, with the per-world split agreeing: JUNGLE 3, HALLOW 5, FANTASY 3,
SPACE 3.

## HOW — the texture registry gives the authored ORDER

`FUN_00216028`: name at entry+0, id at +0x10, stride 0x18.

```
0x15 laptop/award_medal_32     0x16 laptop/award_star_32
0x17 cart     0x18 apehead   0x19 croc      0x1a hades     0x1b devil
0x1c bat      0x1d ghost     0x1e shakey    0x1f bigdrip   0x20 catapillar
0x21 candy    0x22 moonshot  0x23 mega      0x24 shocker
0x25 m_upgrade  0x26 m_security  0x27 m_path  0x28 m_green  0x29 m_aesthetic
0x2d gticket/gticket           0x2e ultimatec/star
```

The 14 stars are one contiguous block, grouped by world in world order, and the 5 medals follow
immediately. That ordering is the rows' own and does not have to be invented.

⚠ `UI.WAD` also ships `AWARD_MEDAL_64`, which is **not** in this registry — the big `MedalRow` art
loads by a path not yet found.

## WHAT — tickets buy PARKS

`WorldMapGoldTickets`, and the economy is three pools:

> `Tickets earnt: %d/%d` · `Tickets available in this park: %d/%d` · `Floating tickets available: %d/%d`

`GoldenTicketCost` is a field in the attraction schema (neighbours `Length`, `Width`, `Height`,
`EntryCellStandPosX`).

## ⚠⚠ The date: a failed search is not a finding

I reported "there is no dd/mm/yyyy in this build" after finding no printf format and no month name
anywhere in the executable or the 1087 localised strings. Master had it on screen at the time.

**The HUD composes the date from digits and draws them with the bitmap font, exactly as the money
readout does** — so there is no format string to find and there never was. The search could not
have succeeded, and it was given no control that it must hit. See
`feedback_a_negative_search_proves_nothing`.

## HOW — the calendar, `FUN_0016b240`

Implemented in `core/TPW.PS2.Data/ParkClock.cs`; the full derivation is in that file's comment. In
short: a six-word struct, a day every `0xF0000` frame-time units (**240 ticks, 4.8 s at 50Hz, a year
in 29.2 minutes**), month 0-based against the table at `0x361d88`, and **no leap years**.

⭐ A park starts on **01/01/2000** — master: *"its meant to be 1/1/2000 as the start date"*, which
matches what the port rendered before he said so. ⚠ Still not read out of the image: the executable
holds no year literal at all, so the epoch arrives from a scenario or save that is not traced.

## ⭐ The calendar is the economy's spine

`FUN_0016b060` is the daily driver. It snapshots month, day and year, ticks the clock, and then:

- **month changed** → `FUN_0016c120`, the finance rollover, plus an escalating counter at `+0x18`
  that fires the in-the-red warning chain (`STR_ADVMES_ADD_IN_THE_RED_THREE_MONTHS`, `..._MONTH_LEFT`,
  `..._SIX_MONTHS`), and `+0x1c` counts months elapsed
- **day changed and `day % 7 == 0`** → `FUN_0016bc70`, a weekly pass

So monthly costs and the bankruptcy warnings hang off this clock; they should not be wired to a
separate timer.
