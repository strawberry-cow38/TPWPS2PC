# DBA park asset payloads

The directory and text-name join were established in [advisor.md](advisor.md). This extends
that work to the **eight payload layouts**, common placement data, three ride tiers, research
and purchase parameters, shop effects, sideshow pricing, minigame selection, and footprint cells.
The [managed reader](../core/TPW.PS2.Data/AssetResourceDatabase.cs) now checks and exposes those fields. **The three files remain partly
decoded**, because several track/tour settings and flags still lack a complete interpretation.
The unresolved byte ranges are listed below; a passing audit does not erase them.

All offsets below are **relative to the start of a payload**, unless labelled ELF addresses.
Integers are little endian. Evidence comes from this disc's `SLES_500.32`, SHA256
`231771d7f39cc29e7573ce62f437af15d97737a64c1d1c49df8404ead0b7577a`.
Disassembly used `tools/r5900dis.py`, including R5900 `LQ`, `SQ`, and three-operand `MULT`.
These are static consumer traces, not console execution tests.

## The pointer is really a DBA payload

The previously established lookup is `0x10f248`, reading the loaded DBA global `0x2aadfc`.
The additional connection to placed objects is:

```
0x10f0b0: resource handle's key -> 0x10f248 (call at 0x10f0c0)
0x10fa30: handle+0x18 -> 0x10f0b0; caches returned pointer at handle+0x14
0x1e1420: placed object+0x20 -> 0x10fa30 (call at 0x1e1428)
```

`0x1e1420` is the method at vtable slot `+0x5c` in the ride (`0x35a560`), coaster
(`0x35b060`), shop (`0x368080`), tour (`0x369f10`), and track ride (`0x36bbf0`) tables.
Shop methods account for their base subobject at `+8`: their resource handle is at `+0x28`.
The consumers below either call that verified method or `0x10fa30` directly. Merely seeing
some unrelated class call vtable slot `+0x5c` was **not** treated as a DBA reference.

A second useful path is the existing asset-category lookup `0x12ae78 -> 0x10f248`.
Research and purchase-cost accessors call it directly. The adjacent release methods do not
invalidate the cached payload. This explains the compiler's loads in release-call delay slots.

## Common header: every byte of `+0x00..1f`

| Offset | Storage | Interpretation and evidence |
|---|---|---|
| `00` | `u16` | Kind, getter `0x12b540`, load `0x12b544`. The eight retail variants are below. |
| `02` | `u16` | **Runtime category-local index**, zero on disc. Getter `0x12b520`; setter `0x12b528` preserves the kind and replaces the high halfword. Category enumeration `0x12a698` passes its loop index to that setter at `0x12a710`. This is not a second persistent asset key. |
| `04` | `u32` | Localised name row, already established by `0x12b548 -> 0x1dfa58`. |
| `08` | `u8` | Unrotated footprint width. Getter `0x12b578`; used by placement loop `0x1e2b80`. |
| `09` | `u8` | Third dimension/height candidate. Separate getter `0x12b580` and setter `0x12b598` establish its width. **No downstream height use established**; API calls it `UnknownHeightByte`. |
| `0a` | `u8` | Unrotated footprint depth. Getter `0x12b588`; placement loop `0x1e2b8c`. |
| `0b` | byte | `FF` in every record; purpose unknown, preserved. Not combined with depth. |
| `0c,0e` | two `s16` | Connection A's local X/Z tile coordinates. `0x1e1760` obtains the payload, copies `+0c`, extracts signed halfwords, and rotates/translates through `0x1e2288`. Negative coordinates mean absent. |
| `10,12` | two `s16` | Connection B's coordinates, same operations in `0x1e1a48`; presence test `0x1e19f0`. The reader leaves A/B names: the complete entrance/exit convention is not established. |
| `14` | `u8` | Connection A direction, getter `0x1e1998`, load `0x1e19c4`; added to object rotation and reduced modulo four in `0x117474..`. |
| `15` | `u8` | Connection B direction, getter `0x1e1c80`, load `0x1e1cac`. Absolute compass naming is not assigned. |
| `16` | `u16` | **Minigame selector**. Loaded at `0x124420`, passed through `0x152668` to the constructor dispatcher `0x1debc8`. Also tested at `0x13773c..88`, including the special case 7. Identity examples: Jungle GoKarts key 220 = 7; SGPUZZLE 250 = 5; PONG 606 = 10. Zero means no minigame in the selection path. |
| `18` | word | Base excitement parameter. Verified payload loads include `0x122804`, `0x2021bc`, and `0x1d2a98`, feeding attraction calculations. Naming is corroborated by the matched ride SAMs' `UsageInfo.ExcitementLevel`: Belly Bounce 40, Chac Atak 90, Dino Karts 80, Jungle tour 70. Not a complete excitement formula. |
| `1c` | `u32` | Type-data byte extent. **Consumer proof**: `0x12b5a8` returns `payload + word(1c) + 0x20`; `0x1e2b74` uses this as the footprint pointer. It is relative to `+0x20`, not the file or payload base. |

The header is followed by type data, then `width * depth` four-byte footprint cells. In **all
three** files, `payload length == 0x20 + type-data extent + 4*width*depth`, with no trailing
unclassified bytes. This equality is a bounds/coverage check, not evidence for a gameplay name.

| Kind | Identity family | Type-data extent | Footprint starts | Named anchor (key) |
|---:|---|---:|---:|---|
| 1 | Coaster | `b4` | `d4` | Jungle COASTER1 / Chac Atak (217) |
| 2 | Feature, including service buildings | `10` | `30` | Jungle BUSHIII (178), PELBIN (198) |
| 3 | Ordinary ride | `a0` | `c0` | Belly Bounce (215) |
| 4 | Shop | `18` | `38` | Jungle Ice Cream Shop (245) |
| 5 | Sideshow | `14` | `34` | Jungle ARC2X3 (247) |
| 6 | Track ride, including karts and water rides | `e8` | `108` | Dino Karts (220) |
| 7 | Tour ride | `b8` | `d8` | Jungle TOURRIDE (232) |
| 8 | Track upgrade | `10` | `30` | Jungle LAVAJUMP (236) |

These are names for the observed families, not recovered C++ enum identifiers. The cost/research
switches distinguish the shared tier layouts from the simple economy layouts. The table gives
exact retail extents; the reader deliberately rejects other kinds or extents as unsupported.

## Ride tiers: kinds 1, 3, 6, 7

Three consecutive **52-byte** tiers occupy `+20..bb`. Tier `i` begins at `20 + 34*i` (hex).
The `+34` stride is explicit in `0x1178b0..c8`; the current tier is the object's byte `+126`.
The table uses absolute offsets for tier zero; add `34*i` to each.

| Offset | Storage | Meaning and consumer |
|---|---|---|
| `20` | raw `u32` | Unknown flags/parameter, usually 0, also 4. No established consumer; not discarded. |
| `24` | word | `MinSpeedDamage`: getter `0x117a08`, load `0x117a50`. |
| `28` | word | `MinCapacityDamage`: getter `0x117a68`, load `0x117ab0`. |
| `2c` | word | `WearRate`: getter `0x117ac8`, load `0x117b10`. |
| `30` | word | Capacity parameter: getter `0x117b28`, load `0x117b70`. Initialisation `0x116120` takes half, clamps to at least one, and calls the capacity setter. `0x1183a0 -> 0x1182a8 -> 0x1fa7d0` supplies RSSE variable 2, CAPACITY. The general getter is overridden for coasters; do not describe this as every ride's final maximum capacity. |
| `34` | word | Initial condition/lifetime parameter. `0x1160d8` reads **tier zero**, stores its low halfword in object `+94`. Setter `0x1e1cf0` queues advisor message 135, `STR_ADVMES_RIDE_CONDEMNED`, on crossing to nonpositive. Applying this word from later tiers was not established. |
| `38` | word | `MinSpeed`: getter `0x117888`, load `0x1178d0`. |
| `3c` | word | `MaxSpeed`: getter `0x1178e8`, load `0x117930`. |
| `40` | word | `MinDuration`: getter `0x117948`, load `0x117990`. |
| `44` | word | `MaxDuration`: getter `0x1179a8`, load `0x1179f0`. |
| `48` | word | **Research group**, getter `0x12b758`, load `0x12b7e4`. Zero starts available (`0x12b928`). Research eligibility compares the group at `0x1b6958`; `0x1b6be0` groups already researched assets by this value, advancing when at least two thirds of a group are researched (`0x1b6cd0..d34`). Corroborates the SAM name `Research.Group`. |
| `4c` | word | **Research work requirement**, getter `0x12b840`, load `0x12b8d0`. `0x1b6918 -> 0x1b7378` stores it shifted left 12; `0x1b7388` divides accumulated work by it to obtain completion percentage. SAM calls it `CostOfResearch`, but this traced use is a progress denominator, not a purchase debit. Time/staff conversion not established. |
| `50` | word | Purchase/upgrade cost. `0x12bc10` reads tier-zero cost; upgrade path `0x116268` reads `50 + 34*tier` at `0x1162d8`, multiplies by 10, then uses the park-money debit `0x100698`. Demolition uses the base cost, halves it, and multiplies by 10 before refunding. Stored units are left unchanged by the reader. |

The speed, duration and damage names are **in the executable**, not inferred from ranges.
Diagnostic `0x1b7c38` calls the corresponding virtual getters and prints strings at
`0x366260`, `0x366278`, `0x366290`, `0x3662a8`, `0x3662c0`, `0x3662d8`, `0x3662f8`:
`getMinSpeed`, `getMaxSpeed`, `getMinDuration`, `getMaxDuration`, `getMinSpeedDamage`,
`getMinCapacityDamage`, `getWearRate`. The audit pins these strings and getter loads.
The two constant damage parameters are both **4**, including all three Belly Bounce tiers;
`0x1225f8` uses them in the speed/capacity-dependent wear calculation. Their constancy is
not a reason to drop them. Physical speed/duration units are not established.

The capacity/duration script destinations are independently named in
`JUNGLE.WAD/Rides/Bouncy/Bouncy.rss`: `VAR_CAPACITY ; 2` and `VAR_DURATION ; 3`, with the same
symbol order in `Bouncy.RSE`. Thus the capacity setter's variable number is tied to the named
asset's actual script, rather than an assumed global numbering scheme.

### Named prediction: Belly Bounce

Key **215**, offset **`cdc`**, name row **197** identifies
`STR_GRAPHICS_JUNGLE_RIDES_BOUNCY_BOUNCY`, English **Belly Bounce**. The consumer layout predicts:

* Kind 3; runtime index 0; dimensions bytes `(3,3,4)`; byte `0b = FF`.
* Connection A `(1,0)`, direction 0; connection B `(1,3)`, direction 2; minigame 0;
  base excitement 40; body length `a0`; footprint at `c0`; whole payload 240 bytes.
* Tier fields, in the order of the table above:

```
flags damageSpeed damageCapacity wear capacity condition speedMin speedMax durationMin durationMax group research purchase
0     4           4              5    5        100       1        100      10          60          0     0        500
0     4           4              3    7        100       1        100      10          60          1     150      150
0     4           4              2    9        100       1        100      10          60          1     150      250
```

Its extra bytes are `01 47 41 59`. Its footprint is three columns by four rows; all tile IDs are
`-1`, and the raw flag rows are `0,2,1 / 3,2,1 / 3,0,1 / 3,0,2`. These precise values, not a
record total, are checked against each regional file. The first tier initialisation predicts
capacity 2, speed 50, duration 30 under the base class methods (`0x116120`).

The shipped `JUNGLE.WAD/Rides/Bouncy/Bouncy.sam` corroborates identity, excitement 40, capacity
sequence 5/7/9, and 3-by-4 shape, but **is not the compiled DBA source of truth**. Its later
upgrade prices are 400/500, versus DBA 150/250; research differs too. Other SAMs even have old
footprint sizes. The audit does not substitute those stale numbers for the compiled payload.

## Simple economy and shops: kinds 2, 4, 5, 8

All four simple layouts share `+20 purchase cost`, `+24 research work`, `+28 research group`,
three words. Their direct loads are respectively `0x12bc88`, `0x12b8ec`, and `0x12b808`.
They use the same cost/progress/availability consumers described above.

Shop-specific `+2c..37`:

| Offset | Storage | Meaning and evidence |
|---|---|---|
| `2c` | `u16` | Initial selling price. `0x1d1830` initializes shop state `+b8`; getter `0x1d1c88` reads the payload value. |
| `2e` | `u16` | Base cost of goods. `0x1d1b08` obtains the payload directly, loads at `0x1d1b2c`, and adjusts it by quality/ingredient state. The same shop SAM calls it `InitCostOfGoods`. |
| `30` | `u8` | Product selector, getter `0x1d1d08`. Purchase path `0x20e344` dispatches through `0x36ca60`. Observed identities: 0 burger/steak, 1 drink, 2 costume, 3 balloon, 4 ice cream, 5 cauldron, 6 gift, 7 fries. This numbering is not the old SAM `SpecialIngredient` numbering. |
| `31` | byte | Unknown, zero in retail. |
| `32` | `u8` | Hunger reduction, getter `0x1d1c48`, load `0x1d1c6c`. Purchase `0x20e3b0..420` subtracts it from visitor byte `+77`, clamps at zero, and also adds it to `+79`. The second state variable's meaning is not established here. |
| `33` | `u8` | Thirst reduction, getter `0x1d1c08`; purchase subtracts from visitor `+7a`. |
| `34` | `u8` | Happiness effect, getter `0x1d1b88`; `0x20e450..` scales by shop quality before adding to visitor `+75`. |
| `35` | `u8` | Unknown effect, accessor `0x1d1bc8` exists but no caller was found. **Not labelled litter** just because a SAM has `LitterEffect`: IceCream SAM says 25, while this byte is 0. |
| `36` | `u8` | Vomit increase, getter `0x1d1cc8`, load `0x1d1cec`; `0x20e424..44c` adds to visitor `+76`, clamping at 100. |
| `37` | byte | Unknown, zero in retail. |

Hunger/thirst/happiness/vomit names are corroborated by the matching shops' SAM effect names
and values, with the above consuming arithmetic establishing which field is added/subtracted.
For example Jungle IceCream has product 4, price 30, base goods 20, thirst 5 and happiness 5;
Coconut has hunger 0/thirst 40, whereas Burger has hunger 25/thirst 0. This supplies discriminating
identities, rather than assigning every small byte the next plausible label.

### The regional counterexample is gameplay data

The eight differing bytes are **four hunger bytes and four vomit bytes**, not two wider integers.
Each row below identifies the Ice Cream Shop in that world:

| Key | Full `id.dat` key | EUR/JAP hunger, vomit | USA hunger, vomit |
|---:|---|---|---|
| 245 | `STR_GRAPHICS_JUNGLE_SHOPS_ICECREAM_ICECREAM` | 25, 10 | 15, 15 |
| 151 | `STR_GRAPHICS_HALLOW_SHOPS_ICES_ICES` | 25, 10 | 15, 15 |
| 69 | `STR_GRAPHICS_FANTASY_SHOPS_ICECREAM_ICECREAM` | 25, 10 | 15, 15 |
| 391 | `STR_GRAPHICS_SPACE_SHOPS_ICES_ICES` | 25, 10 | 15, 15 |

The USA record for Jungle IceCream agrees with the SAM's hunger 15/vomit 15; EUR/JAP does not.
All other bytes of these records, and every other record, are identical across regions.
The audit loads **all three independently**, checks the four full identities and effects, and
compares USA with an EUR copy changed only at those eight named byte positions.

## Sideshows and the remaining type-specific bytes

Sideshow kind 5 has `+2c u16 initial price`, `+2e u16 prize value`, `+30 u8 win percentage`,
then **three unresolved bytes** `+31..33`. Initialisation `0x1d235c..2390` gets the verified
payload and passes these fields to setters `0x1d2a28`, `0x1d2a50`, `0x1d2a38` respectively.
`0x20d808..81c` compares a random value below 100 to the percentage; success sets visitor flag
`0x400` at `0x20d844..60`. `0x1d2560` charges the entry price and, if that flag is set, debits
the prize value and returns price minus prize. This establishes **win**, even though the stale
SAM source has the misspelled field `InitChanceOfLoosing`. Jungle ARC2X3 key 247 has price 10,
prize 30, win percentage 33, extra bytes `0a 00 1e 00 21 fa 43 00`. It is not a 32-bit
percentage containing `FA 43`.

The following completes byte coverage of the bodies. `Extra` preserves each entire interval;
where a logical field boundary is unproven, the table intentionally describes raw bytes.

| Kind | Offset/span | Established interpretation; remaining gap |
|---|---|---|
| 2 | `2c..2d` (2 bytes) | Always `30 FE`; no established consumer/meaning. Not assumed padding. |
| 2 | `2e` (byte) | Service classification flags. Verified payload getters `0x130718..1308fc` mask bits 0, 2, 3, 1; category lookup consumers at `0x103f54`/`0x10402c` also inspect them. Identity association: toilets bit 0, staff-type buildings bit 1, bins bit 2, cameras bit 3. Other masks/precise service policies remain unresolved; exposed as `RawFeatureFlags`. |
| 2 | `2f` (byte) | Unknown, zero here. |
| 3 | `bc` (`u8`) | Boolean control read at `0x1b7e2c`, gating a call to `0x1952c8`. Exact policy unresolved; Belly Bounce = 1, Bumper = 0. |
| 3 | `bd..bf` (3 bytes) | Unresolved, sometimes ASCII `GAY`; likely exporter residue/alignment, **not proven disposable**. Reader retains it and accepts changes to it. |
| 1 | `bc..bf`, `c0..c3` (two pairs of `s16`) | Additional local track connection coordinates. `0x11fdd0` and `0x1200e0` extract signed pairs, rotate through `0x1e2288`, and translate to park coordinates. Names of the two ends remain unresolved. |
| 1 | `c4..c7` (4 bytes) | Unknown; observed paired-looking halfwords are not enough to assign elevation or slope. |
| 1 | `c8..cb` (4 bytes) | Unknown; observed 0/16. |
| 1 | `cc..cf` (4 bytes) | Unknown; usually 128, Moonshot 0. |
| 1 | `d0..d3` (packed word) | Low `u16` copied to a child object's `+3c` at `0x11aeb4`/`0x11cf4c`; purpose unresolved. Bits 28–29 and 30–31 are connection directions (`0x120020`, `0x120080`); first is added to object rotation and used for an adjacent tile in `0x11fea8..ff44`. Bits 24–25 are exposed by `0x122ce8` but not named. Remaining bits, including byte `d2`, unresolved. Whole word retained. |
| 6 | `bc..bf` (4 bytes) | Unknown. |
| 6 | `c0` (`u8`) | Branch control in `0x2012cc..1364`, after **direct** `0x10fa30`. Selects alternate track-piece operations. Exact authored meaning unresolved. |
| 6 | `c1..d7` (23 bytes) | Unresolved settings; no complete consumer-derived field partition. The run of small words is not enough to label track width, height, speed, cars, etc. |
| 6 | `d8..107` (48 bytes) | Contains indexed resource selectors: `0x204e10` fetches the payload; `0x204e54..74` selects word `dc + 4*(signed child byte + 5)` and passes it to resource acquisition `0x10f7b8`. The child-byte domain and every element's identity are unresolved, so no fabricated twelve-entry semantic enum. |
| 7 | `bc` (`u8`), `bd..bf` (3 bytes) | Getter `0x1e9720` reads `bc`; meaning unknown. The other bytes are unestablished. |
| 7 | `c0..c3` (word) | Controls two different cyclic-index increments at `0x1eb95c..`; high-level meaning unresolved. |
| 7 | `c4..cf` (12 bytes) | Unknown; no established field partition/consumer. |
| 7 | `d0` (`u8`), `d1..d7` (7 bytes) | Getter `0x1e9a00` reads `d0`; exact purpose and remaining bytes unresolved. |
| 8 | `2c..2f` (4 bytes) | Unknown selector; Jungle LavaJump = 12, MammTunn = 11, WaterTun = 6. No consumer-backed target-ride join established. |

The classification names for feature flags are observed associations with named assets, not a
claim that each bit's complete behavior has been reconstructed. For example Space SLASER also
has bit 1; the reader therefore does not expose `IsStaffRoom` as a universal semantic test.

## Footprint cells, including their constant sentinel

The grid starts at `20 + u32(1c)` and advances by **four bytes** at `0x1e2d9c`:

| Cell offset | Storage | Meaning |
|---|---|---|
| `0` | `s16` | Terrain overlay tile selector. `0x1e2d04` uses a signed load and branches on **any negative** value. Negative calls `0x1e6150(cell, 1)` to select the default terrain path. Every shipped cell uses `FFFF = -1`; this is meaningful control data. |
| `2` | `u16` | Flags. Bits 0–1 are quarter turns; bits 2/3 become terrain-cell overlay bits 14/15. High bits are preserved but have no meaning established in this path. |

For a nonnegative tile, `0x1e2d10..30` extracts the flags and `0x1e6048` packs
`tile | (((flags&3)+rotation)&3)<<12 | (bit2<<14) | (bit3<<15)` into terrain cell `+4`.
The axis meanings of bits 14/15 are not established here; the reader avoids calling them X/Y
flips. The original placement loop handles rotated footprint traversal separately and adjusts
its odd orientations before the packing call. The reader exposes **unrotated stored cells**
as `Cell(x,z)` in row-major order; it does not emulate rotated placement or terrain writes.
All retail cells take the negative branch, so their flags are bypassed by this consumer even
though the flags are retained. Synthetic audit cases cover the other branch's field extraction.

## Reader and failing audit

```sh
dotnet run --project tools/TPW.PS2.DbaAudit -- /path/to/disc.bin
# Print each region, full symbolic identity, and decoded projection:
dotnet run --project tools/TPW.PS2.DbaAudit -- /path/to/disc.bin --list
# External mutation tests use the replacement bytes, never the original file silently:
dotnet run --project tools/TPW.PS2.DbaAudit -- /path/to/disc.bin --usa=/tmp/changed.dba
```

`AssetResourceDatabase` retains its ordered entries, first-match `Find`, full raw payload and
directory gap. It adds common fields, `Tier(0..2)`, `SimpleEconomy`, typed shop/sideshow settings,
raw type-specific `Extra`, and signed footprint cells. It owns a clone of the input. The reader
rejects invalid directory spans, unsupported layout extents, zero footprint dimensions,
truncation, and unconsumed bytes. It does **not** require unknown bytes to be zero or pretend
that retained raw bytes have been semantically decoded.

The audit exits **0 on success, 1 on a failed check/exception, 2 for missing arguments**. It:

* Pins the payload lookup/object/vtable chain, field load widths, grid stride, minigame
  dispatch, and ride diagnostic strings against ELF32 `PT_LOAD` addresses. `--elf=FILE`
  permits a different executable to fail those checks explicitly.
* Compares complete decoded projections for named examples of every kind, all four ice-cream
  records and a service feature against `retail-identities.tsv`. These goldens were produced
  with independent Python `struct` unpacking, not by dumping the C# implementation.
* Hashes **every ordered decoded record**, including its full symbolic identity, common fields,
  all tier/economy fields, shop/sideshow settings, unknown extra bytes and every footprint cell.
  This detects changes elsewhere in the corpus and reader regressions. A digest is a regression
  guard, not the evidence for the labels above. `reference.py DBA id.dat` reproduces it from
  extracted inputs without using the managed reader or rewriting any goldens.
* Checks the two `FFFFFFFF` records by their distinct ZOB/ZOB2 names and first-match behavior;
  checks exact EUR/JAP equality and the **named** USA effect changes.
* Rejects malformed spans, body offsets and footprints; tests signed absent connections and
  negative terrain selectors beyond `-1`; tests runtime-index/kind separation and unknown-byte
  preservation. A legal change of constant `MinSpeedDamage` from 4 to 5, and a legal but wrong
  name row, must defeat the identity golden with every size and count unchanged.

The canonical projection is seven tab-separated columns: decimal key, decimal payload offset,
full symbolic name, comma-separated common fields in reader order (kind/index/name row,
width/middle byte/depth/unknown0b, A x/z/direction, B x/z/direction, minigame, excitement, body
extent), body values in the tier/economy table order (then shop/sideshow fields or feature flags),
uppercase raw `Extra` hex, and comma-separated cells. Each cell is
`tile:rawFlags:quarterTurns:bit14:bit15:usesDefault`, booleans as 0/1. Each record ends with LF;
SHA256 uses ASCII. Expected projection digests:

```
eur/jap b0b6fb641f96ab8af4ddfdc65a67d3e201d2602df0563e96b3bd72d086f02d51
usa     6d87e518ae3d17e4d04eff02fa024152cdc64eac81bf1381ecce84f4aa5914bf
```

Original file SHA256, for input identification only:

```
arsdb / arsjapdb ddda8a68e8dc1a28e875ce2f0a81a1006f52a4bdad1cc626cddc016053d01fd4
arsusdb         9a6b04eb4b23b325ac9e9fb47650e4767e226640f528e7afe78d691f50edfb6a
```

Measured verification on 2026-09-22:

| Check | Result |
|---|---|
| `dotnet build TPWPS2.sln` | Exit 0; existing nullable-annotation warnings |
| DBA audit, all original regional files | Exit 0 |
| Existing advisor audit, including speech/text/lip chain | Exit 0 |
| External EUR Belly Bounce `MinSpeedDamage` 4 → 5 | Exit 1; named payload and decoded projection differ |
| External EUR Belly Bounce first footprint flags 0 → 1 | Exit 1; named payload and decoded projection differ |
| External EUR kind 3 → 9 | Exit 1; unsupported kind |
| External USA Jungle IceCream hunger 15 → 25 | Exit 1; named payload, projection and regional effects differ |
| External ELF hunger load `lbu +32` → `lbu +36` | Exit 1; consumer signature at `0x1d1c6c` differs |
| Temporary C# tier-stride regression 52 → 48 | Exit 1; named ride payloads and projections differ. Source restored, rebuilt, original audit passes again. |
| Independent Python reference, EUR and USA | Both projection digests above reproduced |

## What is still not established

The unresolved body ranges above are substantive: coaster geometry/packed fields, track/tour
settings and resource-selector identities, the track-upgrade selector, ordinary-ride control,
and tier flags. Several have partial data flow but no resolved target enum or object-state
meaning. Others have no confirmed live read in the paths traced. A plausible value, a constant,
or an old SAM property is insufficient to close those gaps.

Also unresolved: the directory gap's purpose; common byte `09`'s actual height role/units and
byte `0b`; complete entrance/exit and compass conventions; physical speed/duration units and
research work's conversion to time; whether the later-tier condition fields are used; shop
bytes `31`, `35`, `37`; feature bytes `2c..2d`, `2f`; sideshow `31..33`; flat-ride tail residue;
and footprint high flags/axis naming. No gameplay integration or original-console comparison
is claimed. PAL executable selection of the other regional files is still unestablished.

Thus the ledger remains **3 partly undecoded files, 120,588 bytes**. The reader and audit make
the established portion usable and falsifiable; completing the remaining semantic joins still
requires tracing the specialised consumers and their runtime resource/state identities.
