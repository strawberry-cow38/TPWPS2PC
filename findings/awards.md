# Gold Tickets, awards and the park calendar

Master, 2026-09-27: *"wanna work on the gold ticket/reward / dd/mm/yyyy UI. do the what -> how/why
method."* This is the WHAT and the HOW. The calendar half shipped (`ParkClock`, the bottom-left
HUD); the awards screen is researched and not yet built. ⭐ 2026-10-02: the weekly pass and the world map's ticket
prices are ported -- see "The weekly pass, whole" and "What tickets buy" at the end.

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

⭐ **Earned by the coaster stats screen** (2026-10-02): an "Ultimate Rollercoaster" verdict sets bit
`w·8 + p·4 + o` of `0x2b72ac` (`0x1542b0`, not in the test park), and the star row reads its own
table `0x2c4040` -- JUNGLE 2's two stars are crossed there. coaster-operation.md §5.7-5.8.

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

## ⭐⭐ The weekly pass, whole (2026-10-02, tinyclaw; strawberry: "get on gold ticket 'quests' and rewards")

Only two routines pay a Gold Ticket at all (every caller of `0x1C38C0`): **the weekly pass `0x16BC70`** (through
`0x16BB38(cal, msg)`: a ticket, UI sound 0xC5, the advisor message) and **the mini-game win `0x1DE1F0`** (the first win
of each mini-game per park, goal bit `index + 4`). There are no ride tickets of their own: "Log Flume", "Roller
Coaster" and the like are advisor texts, not payers. Read whole (MIPS anchors in `GoldTicketChecks.cs`):

| | bit | test | message |
|---|---|---|---|
| goal 1 | park 1 | guests ever admitted (`stats+0x20`, `0x210C98`) ≥ record `+0xC` | 0x9D |
| goal 2 | park 2 | `0x100E40` (Cash In − loans x 10 − Cash Out) > record `+0x10` x 10 | 0x9E |
| goal 3 | park 3 | park open, and `(month + 12 x year − opened) / 12` ≥ record `+0x14` (unsigned) | 0x9F |
| goal 4 | park 4 | record `+0x30` (JUNGLE 1 only), a sideshow, a shop, a feature, and ordinary + tour + track + coaster rides > 1 | 0xAA |
| Security | award 0 | `0x104CE0(0x40)` > 0x50 (camera coverage) | 0xA0 |
| Upgrade | award 1 | rides > 7 and every ride's tier `+0x126` non-zero (`0x16BB80`) | 0xA1 |
| Aesthetic | award 2 | rides > 7 and the placed features' summed purchase cost (DBA `+0x20`, `0x16BBD0`) ≥ record `+0x18` | 0xA2 |
| Green | award 3 | `0x152FB0(5, 2)`: 5+ shops, every active shop with an active bin (DBA `+0x2E` bit 2) within 2 cells | 0xA3 |
| Path Economy | award 4 | rides > 9 and path cells (grid type 2) ≤ record `+0x1C` | 0xA4 |

"Rides" (`0x153248`) is five pools: ordinary, tour, track, coaster AND `0x3952C8 PoolOfTourTransports`. The goal bits are
the park's own (`cal+0x24` ↔ `0x3975C8 + world·8 + park·4`); the award bits the game's (`cal+0x28` ↔ `0x3975E8`), so
each hidden award is won once a GAME. The advisor's medal texts are loose: Upgrade is ALL rides, not "a large
percentage"; Aesthetic is money spent on features; Path Economy is a ceiling on laid path, not walking distance.

**The records** (`0x16C008`, re-read in the audit): visitors/profit/years as before, and every park has `+0x18` 2000
and `+0x1C` 100. `+0x31` is the park's ticket count -- J1 7, J2 6, H1 5, H2 7, F1 5, F2 7, S1 5, S2 6 -- the three
goals (four in JUNGLE 1) plus its mini-games. With the 5 hidden awards: **53 tickets in the game.**

## ⭐⭐ What tickets buy: islands

The save keeps twelve park slots (`0x1C3290`: world x 3 + park, park 2 the test park), 8 bytes each with the state at
`+4`: 0 open, 1 closed, 2 locked. A new game initialises all twelve locked (`0x1C4330`: `li v1,2; sb v1,4(a0)`) and
opens JUNGLE 1 and the test park (`0x1C3528(0, 0)`, `(0, 2)`). The world map copies the states into its islands
(`0x2184D0`).

⭐ The eleven kind-1 world-map records (8..18) are the LINKS between islands -- not spare, as `LobbySlots` used to say:
`0x216F90` copies their `+0x18/+0x19/+0x1A` (park record A, park record B, cost). On the disc: JUNGLE 1 to HALLOW 1
or FANTASY 1 cost **1**, either of those to JUNGLE 2 **4**, JUNGLE 2 to SPACE 1 or HALLOW 2 and SPACE 1 to HALLOW 2
**6**, then FANTASY 2 and SPACE 2 **8**.

Stepping onto a locked island (`0x2186B0`) does not move: it prices the island from where the map stands -- the link
joining them in either direction, 1 from JUNGLE 2 back to HALLOW 1 / FANTASY 1 (an explicit case), 100 where no link
does -- and opens prompt mode 1 (`176`/`666`, OK) when the tickets in hand fall short, else mode 2 (`962`/`628`, OK and
Cancel), both with the price `sprintf`ed in and sound 0x12F. OK on mode 2 (`0x219338` case 2) opens the slot, spends
the tickets (`0x1C3920`), stands the map on the island and plays 0x128. Logical button 2 shows mode 7 (`198`/`406`):
tickets earnt (`0x1C3810`, the set goal bits plus the hidden awards) / 53, this island's left / its `+0x31`, floating
(hidden awards) left / 5. A debug switch `[0x2E98C0]` makes `0x1C36D8` answer 255 tickets.

## In the port

- `ParkGoals` carries every field; `ParkAwards` holds the slots (`SlotState`, `OpenSlot`, `OpenParks`), spending and the
  map's counts; `ParkManagement.WeeklyPass` is the whole pass; `ParkAwardInputs` holds its hooks and Green's test, and
  `FromCensus` builds them from the viewer's placements (`Viewer.Management.cs`). `LobbySlots.Links`/`OpenCost`.
- The world map: a locked island raises mode 1 or 2; T shows mode 7. `--all-tickets` / `TPW_ALL_TICKETS=1` is the
  debug switch. A new game holds only JUNGLE 1 open, so a test that walks to another island now buys it first
  (`AdvisorResearchSmoke`, `MainMenuStartupSmoke`).
- ⚠ INFERRED: one tour transport per tour ride (so a tour ride counts twice toward "rides"); grid type 2 is the port's
  path; a scriptless ride has no tier and never counts as upgraded.
- ⚠ Not ported: the mini-game tickets (there are no player mini-games); closing and deleting islands (modes 4, 5); the
  save itself (slots and bits live for the session). JUNGLE 1's goal 4 and its first mini-game would both use bit 4 --
  unresolved; harmless while there are no mini-games.

Tests: `GoldTicketChecks` (33, family `gold_tickets`): the records and links re-read from the ELF, the weekly pass's
nine messages and bars as instruction words, every goal and award at its boundary with the one-short input as its
control, Green's reach and quirk, the census. `MainMenuStartupSmoke` (87): not enough tickets, Cancel, a purchase,
the count, and back to an open island without a prompt.

