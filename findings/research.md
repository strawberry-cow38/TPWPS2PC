# Research: the database, the catalogue, the rules and the screen (port-ready spec)

Source: `SLES_500.32` (PAL), Ghidra seat `agent_research`, MIPS via `mipsdis.py` wherever the
decompile was murky, raw `jal`/pointer searches of the ELF (`re-research/jal.py`), the catalogue
builder `0x158CF0` emulated to its return (796 steps, `re-research/emu_cat2.py`), and the DBA
read from `DATA.WAD:/arsdb.dba` (`re-research/dbacat.py`). Every address is native.

Tags: **READ** = read in code (decompile checked against MIPS where it mattered); **DATA** =
read out of the ELF image or the DBA file; **DERIVED** = computed by a Python model of READ code
over DATA (`re-research/sim.py`), not observed on a console; **INFERRED** = reasoning, says so.

Raw outputs: `re-research/b1.c` (db, manager, screen, list class), `b2.c` (save/load, advisor
helpers, test park), `b3.c` (build menu tabs), `b4.c`/`b5.c` (xrefs, jingle), `catalogue.json`,
`dbainfo.json`, `catalogue_dba.txt`.

---------------------------------------------------------------------------------------------------

## 0. The answers, short

1. **Database**: 60 records of `{u8 cat, u8 item, u8 percent, u8 level}` at `0x389650`, wiped to
   `{FF, FF, (untouched), 0}` whenever the asset-db singleton `0x2B2A78` is (re)created
   (`0x12A550`), which happens on first use in each park because park teardown frees it
   (`0x150E80 -> 0x12A5B8`). Records are created lazily by any query. **Nothing seeds it per
   park**: what starts researched is exactly the items whose **tier-0 research group is 0** -- the
   first query auto-promotes them (`0x12B928`/`0x12BA08` -> `0x12BAD8`). **Level = how many tiers
   are researched**: availability of tier *t* is `level > t`; a ride kind runs 0 (locked) -> 1 (base
   ride) -> 2 (upgrade 1) -> 3 (upgrade 2); a simple kind is available at level >= 1. (READ)
2. **Catalogue**: `PTR_DAT_00360850[world]` -> a per-world table indexed by park slot, holding for
   each kind a list of **DBA keys**; category 1..8 **is** `AssetKind` and item = ordinal in that
   list. Lists are compiled into the ELF (built by `0x158CF0`), not on the disc -- the port must
   carry them as a table, the way `TrackUpgrades` already does for kind 8 (table in §1.1). Group
   and work come from the DBA the port already parses: `RideTier(t).ResearchGroup/ResearchWork`
   (`+0x48/+0x4C + 0x34*t`) and `SimpleEconomy.ResearchGroup/ResearchWork` (`+0x28/+0x24`). (READ + DATA)
3. **Starting**: **the player picks**, on the Research screen; nothing else ever starts a project
   (the only two `jal 0x1B6880` in the image are the screen `0x1B5644` and the save loader
   `0x1B6764`). The screen offers, per row, the catalogue items passing `0x1B7208`; the list
   always ends with "Nothing", which **stops** that row's project. No weights exist in the choice;
   the slot `+0` "weight" is written (100 for the cursor row every frame) and never read. A row with
   no candidates shows only "Nothing". (READ)
4. **Completion**: the unlock is the `level++` that `0x12BAF8` does when the filed percent reaches
   100, in the same quantum as `0x1B74A0`; the slot goes idle (`active 0, complete 1, item -1`),
   thresholds are marked dirty, the advisor message posts, and **nothing starts the next item** --
   the row reads "Nothing" until the player picks again. If nothing is left, jingle 0xBA with a
   music duck. Build menu = `0x12B6D0(db, kind, i, tier 0)`; ride upgrade = `tier < level`. (READ)
5. **Screen**: row *i* = slot *i* (Rides, Shops, Sideshows, Features, Upgrades); name + 72x22 bar,
   green with percent while active, red at 0 with "Nothing" while idle. `OverallBar`/`OverallText`
   show **nothing** (never authored, written only by the binder, never read). Input: choose a row,
   choose a candidate or "Nothing". The budget cannot be changed (forced to 100 on open). (READ)
6. **Researcher**: the port's model is right: each quantum adds
   `(R[L] * budget << 12) divu (n * 100)` to **every** active slot (`R = 20,30,35,40,43`,
   `0x366150`), so the work is shared. Research **costs no money** anywhere in its own code; the
   only cost is the researcher's wage. (READ)

---------------------------------------------------------------------------------------------------

## 1. The data

### 1.1 The catalogue -- `PTR_DAT_00360850[world]` (READ `0x12B1B8`, `0x12AFE0..0x12B130`, `0x12AD78`; DATA by emulating `0x158CF0`)

The asset-db singleton `0x2B2A78` latches `+4 = park slot = 0x14E160() = [0x3952E8]` and
`+8 = world = 0x14E170() = [0x3952E4]` at creation (`0x12A4B0`); `0x150E20` copies them from
`P+0x2C` / `P+0x30` (READ). `[0x360850 + 4*world]` points at one of four BSS tables `0x395488`,
`0x395580`, `0x395678`, `0x395750` (Jungle, Hallow, Fantasy, Space), filled at boot by `0x158CF0`
from compiled key arrays (READ, `findings/bus-capacity-tables.md`). For park slot `s`:

| kind (= `AssetKind`) | key-array pointer | count | entry stride |
|---|---|---|---|
| 3 Ride | `+0x08 + 4s` | `+0x14 + 4s` (`0x12AFE0`) | 4 |
| 7 TourRide | `+0x20 + 4s` | `+0x2C + 4s` (`0x12B010`) | 4 |
| 6 TrackRide | `+0x38 + 4s` | `+0x44 + 4s` (`0x12B040`) | 4 |
| 8 TrackUpgrade | `+0x50 + 4s` | `+0x5C + 4s` (`0x12B070`) | 16 `{key, 0, handle{key, loaded}}` |
| 1 Coaster | `+0x68 + 4s` | `+0x74 + 4s` (`0x12B0A0`) | 4 |
| 2 Feature | `+0x80 + 4s` | `+0x8C + 4s` (`0x12B0D0`) | 4 |
| 4 Shop | `+0x98 + 4s` | `+0xA4 + 4s` (`0x12B100`) | 4 |
| 5 Sideshow | `+0xB0 + 4s` | `+0xBC + 4s` (`0x12B130`) | 4 |

**Item -> record** (`0x12AE78(db, cat, i)`, READ): cats 1..7 -> `0x12B1B8` reads `key = list[i]` ->
`0x10F248(key)` (first match in the loaded DBA). Cat 8 -> `0x12AD78` -> `0x10F0B0(entry+8)` =
`0x10F248(handle.key)`, where the handle's key was copied from `entry+0` by `0x12A698` ->
`0x10F0A8` at singleton creation (READ MIPS `0x10F0A8: sw a1,(a0)`). So **every category resolves a
DBA key through the same lookup**. (`0x12B1B8`'s own case 8 returns the first TRACK-ride key -- a
fallback nobody uses for cat 8 records; do not port it.)

**Checks** (DATA): all 347 catalogue keys resolve in `/arsdb.dba` and every record's kind equals its
list's category (0 mismatches); the research fields of all of them are identical in
`arsdb`, `arsusdb` and `arsjapdb`. The Jungle-0 counts (8/0/1/1/17/8/4/2 = 41) equal the live
savestate count in `findings/hardcoded-screens.md`, and the kind-3/6/7/8/1 lists equal
`findings/bus-capacity-tables.md` -- three routes agree.

⚠ Correction: `hardcoded-screens.md` says the kind-8 keys (237, 236) "are NOT in the loaded DBA
directory". That came from its decoder, `tools/research_db.py`, which for kind 8 sets
`rec = None` **without looking the key up**. They are ordinary kind-8 records in `arsdb.dba`
(Mammoth Tunnel, Lava Jump), and the code resolves them by the same `0x10F248`. (READ tool + DATA)

The table to port (DBA keys, in list order; position = item index). Park slot 2 is the
Rollercoaster Test Park (`findings/coaster-operation.md` §8); the other worlds' slot 2 are empty.

| world/park | 3 Ride | 7 TourRide | 6 TrackRide | 1 Coaster | 2 Feature | 4 Shop | 5 Sideshow | 8 TrackUpgrade | records |
|---|---|---|---|---|---|---|---|---|--:|
| JUNGLE 0 | 221 222 223 224 226 228 230 234 | -- | 220 | 225 | 179 182 184 186 188 190 192 193 194 197 198 200 202 204 205 207 208 | 239 240 241 242 243 244 245 246 | 247 249 251 253 | 237 236 | 41 |
| JUNGLE 1 | 216 215 227 231 233 219 229 | 232 | 235 | 217 218 | 178 180 183 185 187 189 191 193 195 196 198 199 201 203 204 206 207 208 | 239 240 241 242 243 244 245 246 | 252 250 248 606 | 238 | 42 |
| JUNGLE 2 | -- | -- | -- | 225 217 218 132 169 133 135 170 47 52 51 371 370 376 | 179 190 203 206 197 195 207 | 239 | -- | -- | 22 |
| HALLOW 0 | 136 139 140 130 144 128 168 | 145 | 146 | 132 169 | 100 102 103 105 106 108 109 110 111 113 114 115 116 117 120 121 122 462 | 147 148 149 150 151 152 153 154 | 155 160 158 156 | 167 | 42 |
| HALLOW 1 | 131 134 137 141 142 143 129 | -- | 138 | 133 135 170 | 101 104 105 107 110 112 113 115 118 119 120 121 458 459 460 461 463 464 | 147 148 149 150 151 152 153 154 | 159 157 161 607 | 165 166 | 43 |
| FANTASY 0 | 46 48 55 57 58 60 83 | -- | 62 | 47 52 | 26 27 29 31 32 34 36 37 40 465 467 469 470 85 471 472 475 477 | 64 65 66 67 68 69 70 71 | 74 77 73 | -- | 39 |
| FANTASY 1 | 49 63 50 53 54 56 | 61 | 59 | 51 | 26 28 30 84 33 35 39 465 466 468 469 470 473 474 475 476 478 | 64 65 66 67 68 69 70 71 | 76 72 75 605 | 81 82 | 40 |
| SPACE 0 | 365 366 372 374 379 380 403 383 | -- | 381 | 371 | 338 340 342 344 346 347 348 350 353 355 356 357 358 479 481 483 484 | 385 386 387 388 389 390 391 392 | 395 397 396 | 402 | 39 |
| SPACE 1 | 364 368 369 373 377 378 405 382 | 375 | 367 | 370 376 | 339 341 343 345 346 347 352 354 356 357 358 480 482 483 485 | 385 386 387 388 389 390 391 392 | 393 394 398 608 | -- | 39 |

Max records per park = 43 (Hallow 1), so the 60-record cap of §1.3 is never reached by retail data
(DATA). The test park's 22 do not matter: research is not saved there and every item is available.

### 1.2 Research fields in the DBA (READ `0x12B758`, `0x12B840`, jump tables `0x35C340`, `0x35C390`)

| | ride kinds 1, 3, 6, 7 (tier t = 0..2) | simple kinds 2, 4, 5, 8 (tier ignored) | port accessor |
|---|---|---|---|
| research group | `+0x48 + 0x34*t` | `+0x28` | `Tier(t).ResearchGroup` / `SimpleEconomy.ResearchGroup` |
| research work | `+0x4C + 0x34*t` | `+0x24` | `Tier(t).ResearchWork` / `SimpleEconomy.ResearchWork` |

- `group(cat, item, t)` = `0x12B758`; categories outside 1..8 (jump table has 17 entries) answer 0.
- `work(cat, item, t)` = `0x12B840`: **0 when tier t is already available** (it calls `0x12B6D0`
  first, MIPS `0x12B86C..0x12B878`), else the DBA word.
- ⚠ `group(cat, item, 3)` on a ride kind reads `+0xEC`, past the third tier -- footprint cells on
  kind 3/1/7, type data on kind 6. Only reached for a level-3 ride by the save (§5); every retail
  ride has it non-zero (DATA), so it has no effect. Port: treat tier >= 3 as "group non-zero".
- Retail tier-0 groups are 0..3 and upgrade-tier groups 1..5 (DATA); no tier 1/2 has group 0.

### 1.3 The state database `0x389650` (READ `0x12A550`, `0x12BEE8`, `0x12BF38`, `0x12B928`, `0x12BA08`, `0x12BAD8`, `0x12BAF8`, `0x12B6D0`; MIPS checked for all of these)

```
record = { u8 cat, u8 item, u8 percent, u8 level }   x 60, BSS 0x389650

create:  0x12A550: if singleton [0x2B2A78] == 0: 0x12A4B0 (latch park/world, catalogue handles),
         then every record = {0xFF, 0xFF, (percent untouched), level 0}
teardown: 0x150E80 (park exit) -> 0x12A5B8: singleton = 0  -> the next use wipes the list again
find   (0x12BEE8): linear over 60 for cat==c && item==i, else null
alloc  (0x12BF38): first record with cat==0xFF && item==0xFF, else NULL (no guard: a 61st key
                   would be written through a null pointer -- unreachable with retail data, 1.1)
get(c,i):  r = find ?? (alloc; r.cat=c; r.item=i; r.percent=0)       // level is NOT written: 0 from the wipe

File(c, i, level, pct)                          // 0x12BAF8; 0x12BAD8(c,i,t) = File(c,i,t,100)
    r = get(c,i)
    if pct >= 100: level += 1; pct = 0          // slti 0x64 -- THE UNLOCK
    if level >= r.level: r.percent = pct; r.level = level      // slt s0, level: never lowers a level

Percent(c, i, tier)                             // 0x12B928
    g = group(c, i, tier); r = get(c,i)
    if tier < r.level: return 100               // that tier is researched
    if g != 0:        return r.percent          // in progress (or 0)
    File(c, i, tier, 100); return 100           // GROUP 0 = AUTO-MARK: level becomes tier+1

Available(c, i, tier)                           // 0x12B6D0 -- what every consumer asks
    return [0x2B3070] != 0                      // debug config key "AllResearched" (0x12BE30, 0x35C400)
        || [0x2B72A8] != 0                      // Rollercoaster Test Park flag (0x153410)
        || Percent(c, i, tier) == 100           // i.e. level > tier, or group(tier) == 0

Level(c, i)                                     // 0x12BA08
    loop: l = get(c,i).level
          if l >= 3 or group(c, i, l) != 0: return l
          File(c, i, l, 100)                    // promote across group-0 tiers
```

`0x12BBA0` (set level directly) has **no caller** (raw `jal` search, READ): dead.

**Writers of the list, complete** (Ghidra xrefs to `0x389650` + raw `jal` search for `0x12BAF8`,
`0x12BAD8`, `0x12BF38`, `0x12BBA0`; READ): the wipe in `0x12A550`, `get`'s allocation,
`File` from `0x12BAD8` (auto-mark), from `0x1B7388` (every research quantum) and from the save
loader `0x160AC0`. **There is no per-scenario seeding.**

### 1.4 What "level" means (READ `0x12B928`, `0x12BA08`, `0x1B74A0`, `0x1B7208`, `0x1D4A38`)

- Level = number of tiers researched; `Available(c, i, t) == level > t` (plus group-0 auto-mark).
- **Ride kinds 1/3/6/7**: 0 = not buildable; 1 = base ride buildable; 2 = upgrade 1 researched;
  3 = upgrade 2 researched (the cap: `Level` stops at 3, slot 4 requires `< 3`).
  - The completion message reads it after the increment: `< 2` -> "ride researched" (0x4B),
    else "add-on/upgrade researched" (0x4C, or 0x7E when no mechanic is hired).
  - The ride panel (`0x1D4A38`) offers upgrade to tier `T = ride.tier(+0x126) + 1` only when
    `T < 3`, the ride passes its `vt+0x2CC` check, and **`T < Level(kind, ride.catalogueIndex(+0x97))`**.
- **Simple kinds 2/4/5/8**: available once level >= 1. Group-0 ones climb to 3 on the first
  `Level` call (tier is ignored, so every "tier" has group 0) and to 4 at the first save (§5);
  harmless, nothing reads a simple kind's level beyond `> 0`.
- Percent is the in-progress percent **of the current tier** and resets to 0 on each level++.

### 1.5 What starts researched (DERIVED from 1.3 over the 1.1 catalogue and the DBA; corroborated)

Rule: **an item is available at park start iff its tier-0 research group is 0** (or the debug /
test-park flags). Nothing else is pre-researched. The thresholds `+0x98` at start are `[1,1,1,1,x]`
in every retail park. Corroboration by a different route: for JUNGLE 0 this model gives exactly
the 13 items the live savestate in `hardcoded-screens.md` shows at level >= 1 without having been
researched, and the same thresholds `[1,1,1,1,1]`.

Initial state and the candidate list each row would offer on a fresh park (keys; "row 4" assumes no
ride is built yet, since its ride entries need one built -- §2.2):

| park | thresholds `+0x98[0..4]` | available at start (key label) | row 0 Rides | row 1 Shops | row 2 Sideshows | row 3 Features | row 4 Upgrades |
|---|---|---|---|---|---|---|---|
| JUNGLE 0 | [1, 1, 1, 1, '(dead)'] | 222 King of the Swingers, 226 Crazy Ape, 228 Rocky Racers, 225 Temple of Gloom, 193 Security Camera, 198 Litter Bin, 204 Staff Room, 208 Toilet, 240 Burger Shop, 241 Drinks Shop, 243 Fries Shop, 247 Arcade, 249 Jungle Spray | 221, 223, 224, 230, 220 | 242, 245, 246 | 251, 253 | 179, 182, 184, 186, 188, 190, 192, 194, 197, 200, 202, 205, 207 | Nothing only |
| JUNGLE 1 | [1, 1, 1, 1, '(dead)'] | 215 Belly Bounce, 227 Mumbo, 219 Dizzy Dinos, 229 Slither, 183 Tiny Rock, 193 Security Camera, 198 Litter Bin, 204 Staff Room, 208 Toilet, 240 Burger Shop, 241 Drinks Shop, 243 Fries Shop, 252 Strength Test, 250 Giant Puzzle | 216, 231, 233, 217, 218 | 242, 245, 246 | 248, 606 | 178, 180, 185, 187, 189, 191, 195, 196, 199, 201, 203, 206, 207 | Nothing only |
| HALLOW 0 | [1, 1, 1, 1, '(dead)'] | 139 Tentacle Terror, 140 Devil's Disc, 130 Insecticide, 100 Tiny Rock, 105 Litter Bin, 106 Small Bush, 113 Security Camera, 115 Small Toilet, 120 Staff Room, 462 Tentacle, 147 Burger Shop, 151 Ice Cream Shop, 152 Drinks Shop, 155 Arcade, 160 Devil Bash | 136, 144, 146, 132, 169 | 148, 149, 154 | 158, 156 | 102, 103, 108, 109, 110, 116 | Nothing only |
| HALLOW 1 | [1, 1, 1, 1, '(dead)'] | 142 Putrid Pumpkins, 143 Rat Race, 129 Brain Buster, 105 Litter Bin, 112 Gravestone, 113 Security Camera, 115 Small Toilet, 120 Staff Room, 461 Small Tree, 147 Burger Shop, 151 Ice Cream Shop, 152 Drinks Shop, 159 Strength Test, 161 Shooter | 131, 134, 138, 133, 170 | 148, 149, 154 | 157, 607 | 101, 107, 110, 118, 458, 459, 464 | Nothing only |
| FANTASY 0 | [1, 1, 1, 1, '(dead)'] | 46 The Dizzy Tree, 57 Flying Fountain, 83 Rubber Ringos, 26 Litter Bin, 29 Card Statue, 465 Small Toilet, 470 Security Camera, 85 Sun Flower, 475 Staff Room, 65 Drinks Shop, 66 Balloon Shop, 67 Fries Shop, 74 Fruit Shy, 77 Aqua Spray | 48, 60, 62, 47 | 64, 68, 69, 70, 71 | 73 | 27, 31, 34, 37, 467, 469, 471, 477 | Nothing only |
| FANTASY 1 | [1, 1, 1, 1, '(dead)'] | 50 Bugs TV, 53 Woodland Racers, 26 Litter Bin, 33 Large Flower, 35 Small Flower, 465 Small Toilet, 470 Security Camera, 475 Staff Room, 478 Trowel, 65 Drinks Shop, 66 Balloon Shop, 67 Fries Shop, 72 Arcade | 54, 56, 59, 61, 51 | 64, 68, 69, 70, 71 | 76, 75, 605 | 28, 84, 466, 469, 473, 476 | Nothing only |
| SPACE 0 | [1, 1, 1, 1, '(dead)'] | 366 Crater Creature, 403 Whirligig, 340 Small Bush, 346 Small Toilet, 350 Obelisk, 353 Giant Robot, 357 Security Cameras, 358 Litter Bin, 479 Comet Comms, 481 Space Dust, 483 Staff Room, 484 Tower, 387 Drinks Shop, 391 Ice Cream Shop, 395 Martian Mooners | 365, 372, 379, 380, 371 | 385, 389 | 397 | 342, 344, 348, 355, 356 | Nothing only |
| SPACE 1 | [1, 1, 1, 1, '(dead)'] | 364 Bounce on Iggy, 373 Missile Madness, 405 G-Force, 382 Wormhole, 339 Antenna, 346 Small Toilet, 352 Pulsar, 354 Golden Rocks, 357 Security Cameras, 358 Litter Bin, 480 Ground Control, 482 UFOs, 483 Staff Room, 485 Medium Tree, 387 Drinks Shop, 391 Ice Cream Shop | 368, 377, 378, 370 | 385, 389 | 393, 394, 398, 608 | 341, 343, 356 | Nothing only |

"Nothing only" in row 4 at start: no ride is built, and no park's track ride 0 is available at start
(every track ride has tier-0 group >= 1), so no add-on qualifies either.

---------------------------------------------------------------------------------------------------

## 2. The manager: what to add to `ResearchManager` (READ)

### 2.1 Thresholds `mgr + 0x98 + 4*slot` (`0x1B7130`, `0x1B6D68`, `0x1B6BE0`, `0x1B6FB8`; MIPS read for all three loops)

`Refresh()` = `0x1B7130`: only when `mgr+0 != 0` (the dirty flag: 1 from `0x1B6640` and from every
completion `0x1B74A0`); recompute, then `mgr+0 = 0`. Called only from `0x1B6880` (start) and
`0x1B7208` (eligibility) -- raw `jal` search.

| slot | function | pool (kinds, in this order) |
|---|---|---|
| 0 | `0x1B6D68` | 6, 7, 1, 3 (table `0x3661D8`, DATA) |
| 1 | `0x1B6BE0(mgr, 4, nShops, 1)` | 4 |
| 2 | `0x1B6BE0(mgr, 5, nSideshows, 2)` | 5 |
| 3 | `0x1B6BE0(mgr, 2, nFeatures, 3)` | 2 |
| 4 | `0x1B6FB8` | 8 -- **dead**: nothing reads `+0xA8` (§2.2) |

```
total[0..4] = done[0..4] = 0               // two 8-word stack arrays, only 5 words zeroed
for each item i of each pool kind k:
    g = group(k, i, 0); total[g]++; if Available(k, i, 0): done[g]++
if done[0]*3 < total[0]*2 (unsigned): return          // threshold NOT written (keeps old value)
g = 1; while done[g]*3 >= total[g]*2 (unsigned): g++  // empty group: 0 >= 0, advances
threshold[slot] = g                                   // first group under two-thirds researched
```

- Group 0 is always 100 % done (auto-mark), so the "not written" branch is unreachable (INFERRED
  from 1.3).
- ⚠ If groups 1..4 are all >= 2/3 done the loop reads uninitialised stack words 5..7 and beyond
  (MIPS `0x1B6D08..0x1B6D30`). Any threshold >= 4 already admits every retail group (max tier-0
  group is 3), so the port can stop at "max group + 1" with no visible difference (INFERRED).

### 2.2 Eligibility `0x1B7208(mgr, slot, cat, item)` -- exact (READ, MIPS `0x1B7208..0x1B732C`)

```
Refresh()
a = Available(cat, item, 0)
if slot == 4 and cat != 8:  return a && BuiltCount(cat, item) > 0 && Level(cat, item) < 3
if slot != 4 and cat != 8:  return group(cat, item, 0) <= threshold[slot] && !a     // signed slt
/* cat == 8, any slot */    return Available(6, 0, 0) && !a      // the park's track ride 0 researched
```

`BuiltCount` = `0x12AC78`: walks the world-object list `0x14DA50()` counting objects whose kind
(`vt+0xA4`) == cat and catalogue index (`0x1E1E98` = byte `+0x97`) == item. **An upgrade is offered
only for a ride type with at least one built instance**; add-ons only need the track ride researched.

### 2.3 Start `0x1B6880(mgr, slot, cat, item)` (READ, MIPS `0x1B6880..0x1B69E8`)

```
L   = Level(cat, item)
pct = Percent(cat, item, L)          // resume point: the filed percent of the tier being researched
g   = group(cat, item, L)
w   = work(cat, item, L)             // 0 if already available
if slot.active: return 0
Refresh()
if slot != 4 && threshold[slot] < g: return 0          // slot 4 never checks its threshold
slot.active = 1; slot.complete = 0; slot.progress = 0; slot.weight = 0; slot.item = -1   // 0x1B7498, 0x1B7600
slot.item = item; slot.category = cat
slot.required = w << 12                                 // 0x1B7378
slot.progress = (pct * required) divu 100               // 0x1B7650 (MIPS: divu)
return 1
```
The port's `Start(slot, cat, item, requiredWork, percentDone)` matches once the DB supplies `w` and
`pct` and the threshold re-check is added. Note the tier researched is always the item's current
level: slot 0 researches tier 0 of a locked ride; slot 4 researches tier 1 or 2 of an available one.

### 2.4 The quantum `0x1B6A38` + `0x1B7388` (READ, MIPS `0x1B6A38..0x1B6B24`, `0x1B7388..0x1B748C`)

The port's `Contribute` is right except that it never files the percent. Per active slot:
```
slot.progress += share                                   // share = (R[L]*budget << 12) divu (n*100)
L   = Level(slot.category, slot.item)                    // promoted, current
pct = required == 0 ? 100 : (progress*100) divu required
File(slot.category, slot.item, L, pct)                   // EVERY quantum; at pct >= 100 this IS the unlock
(debug print 0x16D4C8, empty)
if progress >= required (sltu): Complete(slot)           // 0x1B74A0(slot, 1)
```
then, if that slot completed in this call: debug print `0x107E48` (empty) and
`if !AnythingLeft(0xFFFF): AllResearched()` (§4.3). `n` is counted once before the loop.
Only caller of `0x1B7388` is `0x1B6A38`; only caller of `0x1B6A38` is the researcher `0x1B620C`.
**No researcher, no progress.** (READ, raw `jal`)

### 2.5 Completion `0x1B74A0(slot, 1)` (READ, MIPS `0x1B74A0..0x1B75FC`, jump table `0x3661F0`)

```
slot.complete = 1; slot.active = 0; mgr+0 = 1            // thresholds dirty
name = record(cat, item).name (0x12AE78 -> 0x12B548), then release 0x12AED8   -- unused (debug)
message by category: 1,3,6,7 -> Level < 2 ? 0x4B : (mechanics 0x14D6B0() != 0 ? 0x4C : 0x7E)
                     2 -> 0x4F; 4 -> 0x4D; 5 -> 0x4E; 8 -> 0x4C; posted via 0x107C90/0x107CA8/0x107CC0
slot.item = -1                                           // category is left as it was
```
No sound here (no `0x111150` call in the function). **No restart**: `0x1B6880` has exactly two
callers, the screen and the loader (raw `jal` search, no pointer to it). The port's
`CompletionMessage` is already right; its `ItemLevel` hook becomes `Level`.

### 2.6 The slot weight `+0` (READ)

Written by `0x1B6850(mgr, cursorRow, 100)` at the end of every Research-screen input frame
(`0x1B58E8`) and zeroed by every start (`0x1B7600 -> 0x1B7370(slot, 0)`). The manager is reached
only through `0x1B6798`, whose 8 call sites (`0x10E3F4` advisor v51, `0x151618` park init,
`0x15C8A4` list ctor, `0x160D74` load, `0x1B5490` screen, `0x1B61E4` researcher, `0x1B74C4`
completion, `0x1C2CD8` save) and the manager's own methods never load slot `+0`. **Dead field: do
not port a weighting.**

---------------------------------------------------------------------------------------------------

## 3. The Research screen (READ: ctor `0x1B5410`, input `0x1B5668`, draw `0x1B5920`, list class `0x15C368..0x15D1D0`, runner `0x1C7108`)

### 3.1 Rows and bars

Draw loop i = 4 -> 0 over `mgr + 0xC + 0x1C*i`: **row i is slot i**; labels from `0x365EC0` =
531 Rides, 65 Shops, 185 Sideshows, 155 Features, 834 Upgrades (DATA). Per row:

| state | item column (col 260, row 220+32i) | bar (col 180, row 220+32i, 72x22, range 0..100) |
|---|---|---|
| slot active | the record's name (`0x12AE78 -> 0x12B548`) | `progress*100 divu required` (100 if required 0; MIPS `0x1B5BF4` divu), colour `0x365EE0` (64,240,64) |
| slot idle (incl. just completed) | 605 "Nothing" | 0, colour `0x365ED8` (240,64,64) |
| mode 2 and cursor row | the highlighted candidate (yellow), from `0x15CFE0` | value/colour not recomputed: the previous row's (decompile; unchanged from `hardcoded-screens.md`) |

Label colour: cursor row (255,255,0) `0x35F560`, others (200,130,0) `0x35F550` (DATA). The runner
`0x1C7108` sets the depth short `this+0xC = 100` before the loop (READ; closes that open item in
`hardcoded-screens.md`).

**`OverallBar` / `OverallText` show nothing**: their globals `0x2E74C0..0x2E74D4` have exactly one
reference each, a store in the binder `0x1B5218` (Ghidra xrefs, READ), the shipped `.sce` does not
author them, and the ctor's slider at `this+0x300` (`0x1DA630`) is never drawn or updated -- the
complete list of direct call targets in `0x1B5200..0x1B5F00` contains no slider draw/update (READ).
The PC budget slider was cut; `0x1B5410` forces `budget = 100` (`0x1B54D4`).

### 3.2 Input -- the player chooses (MIPS `0x1B5668..0x1B590C`)

`this+0x2FC` mode (0 rows, 2 candidates; 1 and 3 handled, never set), `this+0x2F4` cursor row.
The base handler `0x164D00(this, 0)` runs first: Cancel -> vtable slot 14 (`0x165E88`) with 1,
which sets `this+0x88`, the flag on which the runner `0x1C7108` leaves the screen.

- **Mode 0**: Up/Down move the row cursor (sound 0xD6, wrap 0..4). Accept -> build the row's list
  (`0x1B5528`), mode 2, `this+0x2F8 = 0`. Cancel -> the base's leave request stands: back to the
  laptop menu (INFERRED: the runner breaks on `+0x88` and returns 0). Gizmo: 545 Back, 967 Close,
  931 New, 39 Mainmenu.
- **Mode 2**: `0x15C368` runs the list: Up/Down cycle the entries (0xD6, wraps; only when there is
  more than one). Accept (0x12F) -> `item = 0x15D0E8`, `cat = 0x15D110`; if either is -1 ("Nothing")
  -> `0x1B71D8`: **stop the row's project** (`active = 0`, item/category kept, filed percent kept);
  else `0x1B55E8`: stop it, then `0x1B6880(mgr, row, cat, item)`. Mode 0 either way. Cancel ->
  mode 0 and slot 14 with 0, **cancelling** the base's leave request (`0x1B58A4..0x1B58BC`): you stay
  on the screen. Gizmo: Back, Close, 477 Select, Mainmenu.
- Every frame: `slot[cursor].weight = 100` (dead, §2.6).

### 3.3 The candidate list (`0x1B5528`, `0x15C8D8`, `0x15CA78`, `0x15C910`, `0x15CFE0`)

```
reset: count = 0; sel = -1; every entry.cat = -1                  // 0x15C8D8 (64 entries {cat, item} at +0xEC)
row 0: Ride(3), TrackRide(6), TourRide(7), Coaster(1) items with slot 0   // 0x15CBF8(list, 1)
row 1: Shop(4) slot 1;  row 2: Sideshow(5) slot 2;  row 3: Feature(2) slot 3
row 4: 3, 6, 7, 1 with slot 4, then TrackUpgrade(8) slot 4                 // 0x15CE90(list, 1)
   append {cat, i} for each catalogue index i (in order) passing 0x1B7208; first append sets sel = 0
append "Nothing" (0x15C910: count + 1, flag +0xE4)                         // ALWAYS
```
Side rules in `0x15CA78` (READ MIPS `0x15CAEC..0x15CB78`): a Feature whose record byte `+0x2E`
has **mask 2** set is skipped in the test park (`0x153410() != 0`; retail: the Staff Rooms,
`+0x2E = 2`) -- ⚠ `hardcoded-screens.md` says "bit 2", the mask is 2; and in park slot 2 the test is
`0x154328` (earned awards) instead of `0x1B7208`, research mode or not.

**No candidates**: the list is just "Nothing", `sel` stays -1, the row shows "Nothing", the cursor
cannot move, and Accept stops whatever the row was running. A row is never empty.

Entries are in catalogue order; the cursor starts on the first. The current project's own item is
still a candidate (it is not yet available); re-picking it restarts it from its filed percent,
truncating up to 1 % of progress (INFERRED from 2.3).

---------------------------------------------------------------------------------------------------

## 4. Who reads the database (READ)

### 4.1 Build menu (`0x197C40`, `0x197EA8`, `0x15C960`, `0x15CA78` with research = 0)

Tabs: for `s = 0..6`, kind `0x365030[s]` = 3, 6, 7, 1, 4, 5, 2 (DATA), text `0x365010[s]` =
1024, 362, 811, 457, 513, 522, 242; a tab exists when `0x14CA20(kind, 7) != 0` and the park's
catalogue count for that kind is non-zero (`0x12AF28`). Each tab lists catalogue index i iff
**`Available(kind, i, 0)`** (plus the test-park rules above). Kind 8 is not one of these tabs
(`s < 7`, and `0x197EE4` is the only caller of `0x15C960`, so its case 7 is unreachable); where the
build menu's kind-8 purchase path (`0x197F48`, `track-ride-tool.md`) takes its list from was not
traced here -- the ride panel's add-on list (§4.2) applies the same `Available(8, i, 0)`. So an item becomes buildable the instant its
`File` call bumps the level; the menu shows it the next time a tab list is built.

### 4.2 Ride panel (`0x1D4A38`)

Next tier `T = tier + 1` offered iff `T < 3`, the ride's `vt+0x2CC` passes, and `T < Level(kind,
index)`. For a track ride (kind 6) the add-on list holds every kind-8 index with `Available(8, i, 0)`.

### 4.3 "Anything left" and the jingle (`0x104358`, `0x1042D0`, `0x104BD0`, `0x104B28`, `0x151998`, `0x151C00`)

- `AnythingLeft(0xFFFF)` = some item of kinds 3, 7, 6, 1, 2, 4, 5 not `Available(.., 0)`, **or**
  `UpgradePercent() < 100`. **Kind 8 is not counted**: the "all researched" jingle can fire with
  add-ons still locked.
- `UpgradePercent` (`0x104BD0`, MIPS `0x104B60..0x104BA4`): over kinds 3, 6, 7, 1, for each item
  with `Level != 0`: `total += 2; done += Level - 1`; `done < total ? done*100/total : 100`.
- `AllResearched` = `0x151998`: if the handle `[0x2B72C0]` is free, UI sound 0xBA on it, and if the
  music setting `[0x2ABE20] > 25` the music target `[0x2ABE30] = 25` (a duck). The per-frame
  `0x151C00` restores `[0x2ABE30] = [0x2ABE20]` once the 0xBA voice ends and ramps the real music
  volume toward the target in steps of 8 (`findings/advisor-messages.md` §4.8 is the same
  mechanism). This closes the "untraced" note on `ResearchManager.AllResearched`.
- Advisor hooks already in the port: `0x1044B0` (v22/26/28/30) counts available at tier 0 over the
  masked kinds; ⚠ its mask bit 4 gates kinds 6 **and** 1, and no code tests bit 8 (MIPS: the masks
  read are 1, 2, 4, 0x100, 0x200, 0xF0) -- harmless for the masks passed (0xF, 0x100, 0x200, 0xF0).

---------------------------------------------------------------------------------------------------

## 5. Save and load (READ `0x1C2968`, `0x1B66C0`, `0x1B7678`; `0x160AC0`, `0x1B6720`; MIPS for byte order)

Both are skipped entirely in the test park (`0x153410() != 0`).

```
for cat in 3, 7, 6, 1, 2, 4, 5, 8:  for i in 0 .. count(cat)-1:
    L = Level(cat, i); P = Percent(cat, i, L)
    write u8 P; write u8 L                        // percent FIRST (0x1C29D8 / 0x1C29DC)
manager, 16 bytes: u8 budget; 5 x { u8 active, u8 category, byte item }    // 0x1B66C0 / 0x1B7678

load: for the same order: read P, read L; File(cat, i, L, P)          // 0x160B0C/0x160B10
      read 16 bytes: budget = b[0]; for slot: if active: 0x1B6880(mgr, slot, cat, item)
```
- Progress is **not** saved: a loaded project restarts at its filed whole percent.
- A stopped (inactive) project is not restored to its slot; its percent survives in the database.
- Correction 2026-10-01: group-0 simple items mutate during save. ONE research-save invocation
  can capture `{100,3}` and leave/load level4; the next captures `{100,4}` and leaves/loads5.
  The full save traverses twice (sizing and writing), so `{100,3}` is not its invariant.
  Manager ITEM loads signed (`lb`), unlike category/budget's `lbu`. File compares levels before
  final byte stores: L255/P100 wraps the stored level to0. See `research-persistence.md` for
  selected MIPS evidence, meaningful-section/alignment boundaries and component scope.
- Thresholds at load: the manager is created dirty (`0x1B6640`, also called for by a startup
  sequence at `0x151618`), and `Refresh` has only two callers, `0x1B6880` and `0x1B7208`, the latter
  reached only from the research-mode list fills of the screen (the build menu passes research = 0).
  So the loader's restarts compute thresholds from the already-loaded database (INFERRED: the
  relative order of `0x151618` and the loader `0x15FBF8` was not read, and does not matter while
  nothing refreshes in between).

---------------------------------------------------------------------------------------------------

## 6. Researchers and money (READ)

- Contribution confirmed (MIPS `0x1B6A94` `mult s1 = n*100`, `0x1B6AB8` `mult a1 = work*budget`,
  `sll 12`, `divu`): every active slot gets `(R[L]*budget << 12) divu (n*100)`; `R = [20, 30, 35, 40,
  43]` at `0x366150` (DATA). In work units: `R*budget/100/n` per slot per quantum. Example
  (DERIVED): one level-0 researcher, budget 100, one project: Sun God (750) takes 38 quanta.
- Budget is 80 from `0x1B6640` until the Research screen is first opened, then 100 (saved). At 100
  each quantum costs the researcher 6 tiredness (port has it).
- A quantum with no active slot still happens (state 0x1F, tiredness, sound 0x8A; `0x1B61C0`,
  `staff-management.md` §10.2), it just adds nothing (`0x1B6A88 beqz a1`).
- **Money**: none of the 12 `jal 0x100698` (the park debit) lies in the research code
  `0x1B5000..0x1B7FFF` (raw search, READ). The month end `0x100A18` makes one debit, loans + wages
  (`0x100B18`; `staff-management.md` §6.3), and calls nothing research-related. Known other debits:
  placement `0x12642C`, ride upgrade `0x1162FC`, add-on `0x129CCC`, training `0x1FF67C`; the rest
  (`0x100C90`, `0x11C738/834/974`, `0x1CD410`, `0x1D1974`, `0x1D261C`) were not identified but are
  outside the research code. The researcher's wage is the only cost of research (INFERRED from
  the absence plus the PC budget slider being cut, §3.1).
- ⚠ Not determined: how often a researcher reaches find-work (the staff tick rate; open in
  `staff-management.md` §15). That sets research speed in seconds.

---------------------------------------------------------------------------------------------------

## 7. Port plan (all in existing types where possible)

1. **`ResearchCatalogue`** (static, like `TrackUpgrades`): the §1.1 key table per (world, park);
   `Count(kind)`, `Key(kind, i)`, `IndexOf(kind, key)`. `TrackUpgrades.Lists` becomes its kind-8
   column (identical data). Records via `AssetResourceDatabase.Find(key)`.
2. **`ResearchDatabase`** (per park, reset at park entry): the 60 x `{cat, item, percent, level}`
   list with `get/File/Percent/Available/Level` exactly as §1.3, `Group/Work` from §1.2, flags
   `AllResearchedDebug` and `TestPark`. Keep the 60 cap with a hard failure (retail never hits it).
3. **`ResearchManager`**: add `Thresholds[5]` + `Dirty` (= `CompletedFlag`, which IS the dirty flag
   -- the port's "no reader traced" note is answered: `0x1B7130` reads it), `Refresh`,
   `Eligible(slot, cat, item)` (§2.2, with a `BuiltCount` delegate over the park's placed objects),
   `Candidates(row)` (§3.3), `Start(slot, cat, item)` computing work/percent from the DB with the
   threshold re-check (§2.3), `Stop(slot)` (`0x1B71D8`), and a `File` call in `Contribute` before the
   completion test (§2.4). Point `ItemLevel` at `Level`, `AnythingLeftToResearch` at §4.3, and
   `Researched` at the level++ inside `File` (it coincides with completion).
4. **Build menu**: replace `SoldHere`'s "everything in the world library" with "in this park's
   catalogue **and** `Available(kind, index, 0)`". ⚠ This also removes the other park's items, which
   the port shows today. Ride panel upgrade: `T < Level`. Add-ons list: `Available(8, i, 0)`.
5. **Research screen**: modes 0/2, the candidate list with its trailing "Nothing", Accept = start or
   stop, Cancel in mode 2 stays on the screen.
6. **Save**: §5 layout if the port ever writes console-shaped saves.
7. Tests with teeth: the §1.5 table (13 start items + thresholds `[1,1,1,1,1]` for JUNGLE 0 is
   independently corroborated by RAM); a researched item appears in the build tab and its
   completion message; a ride upgrade is offered only after slot 4 finishes tier 1; a row with no
   candidates offers only "Nothing" and Accept stops the slot.

---------------------------------------------------------------------------------------------------

## 8. Corrections to existing documents

- `ResearchManager.cs` "`+0` ⚠ no reader traced": `0x1B7130` reads it -- it is the thresholds'
  dirty flag.
- `ResearchManager.cs` `Complete` summary still says "`0x12AED8` (unlock)" (line 164): it is the
  release; the unlock is in `File`.
- `ResearchManager.cs` "`+0x98` ⚠ untraced" / `staff-management.md` §15 "mgr+0x98 ... not traced":
  traced here (§2.1-2.3).
- `hardcoded-screens.md`: kind-8 keys are in the DBA (decoder artefact, §1.1); the Feature skip is
  mask 2, not bit 2 (§3.3); `this+0xC` = 100 from the runner `0x1C7108` (§3.1).
- `ResearchManager.AllResearched` "(untraced)": the music duck, §4.3.

## 9. Not determined, and what would settle it

- The staff tick rate behind the researcher's find-work (research speed in real time): read
  `0x1C4930` (area A of the staff work).
- `0x14CA20(kind, 7)` -- the extra build-tab gate -- not read; irrelevant to research.
- Whether any scenario starts with pre-built rides (it would put rides into row 4 at once): read
  the park loader, or open a fresh park in an emulator.
- The mode-2 cursor-row bar value (stale previous row) is from the decompile only.
