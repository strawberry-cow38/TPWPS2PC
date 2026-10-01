# Laptop "Users", "Satisfaction" and "Winners": console trace and port spec

SLES_500.32, traced 2026-10-01 with Ghidra seat `agent_users` and raw MIPS (`mipsdis.py`). Decompiles are under
`scratchpad/re-users/d*.c`. Tags: **READ** means seen in code or MIPS at the cited address. **INFERRED** means
reasoned from READ facts and not observed directly.

---------------------------------------------------------------------------------------------------

## 0. How the objects are laid out (needed to read every offset below)

- Every placed attraction has a **map-object base**. Call its pointer `obj`. Its vtable is at `obj+0x10`, and each
  slot is 8 bytes: an s16 this-adjust, then the function pointer (READ, e.g. `0x1d5290/0x1d5294`:
  `lh a0,0xd0(vt); lw v0,0xd4(vt)`).
- **Shop, sideshow, feature/toilet, ordinary, tour and track classes start 8 bytes before `obj`**. The class
  pointer is `cls = obj-8`, so the vtable is at `cls+0x18` and a field at `obj+N` is at `cls+N+8`. Shop and
  sideshow code always calls `vt(cls+8)` (READ: `FUN_001d1e00`, `FUN_001d2b48`, and the `obj-8` fix-ups in the
  draws `0x1d82d0 addiu s6,s2,-8` and `0x10a7d0`). The coaster and ride base use `cls == obj`
  (`FUN_001188b0` stores the vtable at `+0x10`). **Every shop or sideshow offset below is from `cls`.**
- Kind: `obj+0x96`, read by `0x1e1d68` (`lbu v0,0x96(a0)`). 1 = coaster, 2 = feature, 3 = ordinary,
  4 = shop, 5 = sideshow, 6 = track, 7 = tour (READ: the switch in `0x20edd8`).
- Class vtables (READ, `vt+0xa4` = `0x1e1d68` on all of them): base `0x35a560`, coaster `0x35b060`,
  ordinary `0x366330`, tour `0x369f10`, track `0x36bbf0`, feature `0x35dc70`, **shop `0x368080`**
  (`vt+0x1e4 = 0x1d1c08`, the shop getters) and **sideshow `0x3683b8`** (`vt+0x1d4 = 0x1d2a68`, the sideshow
  excitement). ⚠ staff-mechanics-guards.md §1.2 lists these two in the other order. This is the binding.

---------------------------------------------------------------------------------------------------

## 1. What each row reads (Q1)

| row (text id) | screen draw | read | formatter / widget |
|---|---|---|---|
| Single Ride **Users** (415 `STR_SINGLER_USERS`) | `FUN_001d5210` | `vt+0xd4` at `0x1d5294`, then `jalr` at `0x1d5298` -> `0x1e1ce8` = **`lw v0,0x18(a0)`** (`0x1e1cec`) | `0x142b68` digits, at `0x1d5918` |
| Single Toilet **Users** (168 `STR_SINGLEBOG_USERS`) | `FUN_001da008` | `vt+0xd4` at `0x1da064` -> `obj+0x18` | `0x142b68` at `0x1da1fc` |
| All Rides **Users** (771 `STR_ALLRIDES_USERS`) | `FUN_00109d40` (vtable `0x358eb8` entry at `+0x14`, i.e. slot 2) | `vt+0xd4` at `0x109fd8` -> `obj+0x18` | `0x142b68` at `0x109ff0` |
| Single Shop **Satisfaction** (1074) | `FUN_001d70c8` | `jal 0x1d1e00` at `0x1d7150` | bar: value<<16 into `bar+0x18` at `0x1d76fc`, max 100 at `0x1d76f8`, min 0 at `0x1d7704`, painted by `0x115620` at `0x1d7700` |
| All Shops **Satisfaction** (451) | `FUN_0010a7d0` (vtable `0x358ff0` `+0x14`) | `jal 0x1d1e00` at `0x10a88c` | bar `+0x648` via `0x115620` at `0x10ab78`; the bar constructor `0x115468` defaults to 0..100 |
| Single Sideshow **Winners** (637 `STR_SINGLESHOW_WINNERS`) | `FUN_001d8288` | `jal 0x1d2b38` at `0x1d82fc` = `return cls[0xd0]` | `0x142b68` at `0x1d86e8`, **only if `cls[0xa8]` (prize value) != 0**: gate `0x1d86d4/0x1d86dc` |
| Single Sideshow **Satisfaction** (743) | `FUN_001d8288` | `jal 0x1d2b48` at `0x1d8338` | bar at screen `+0xa48`, max 100 / min 0, `0x115590` at `0x1d8940` |
| All Sideshows **Satisfaction** (537) | `FUN_0010b288` (vtable `0x359130` `+0x14`) | `jal 0x1d2b48` at `0x10b350` | second bar `+0x6c0` via `0x115620` at `0x10b654` |

**One field is shared (READ).** Every "Users" and "Customers" row on these screens calls the same getter `vt+0xd4`.
On all ten object vtables that slot is `0x1e1ce8` (vtable dump: `0x35a634 0x35b134 0x35dd44 0x366404 0x368154
0x36848c 0x369b74 0x369fe4 0x36b744 0x36bcc4`). So Single Ride 415 and All Rides 771 read **the same field**, and so
do Toilet 168, Single Shop Customers 82 (`0x1d7128`), All Shops 691 (`0x10a864`), Single Sideshow 707 (`0x1d82ec`)
and All Sideshows 488 (`0x10b31c`). What differs is **who increments it**, and that depends on the class (§2).

The two Satisfaction rows of each class share one function: shop 1074 and 451 both call `0x1d1e00`; sideshow 743
and 537 both call `0x1d2b48` (READ).

---------------------------------------------------------------------------------------------------

## 2. `obj+0x18`, the use counter ("Users" / "Customers") (Q2)

u32, unbounded, with no clamp anywhere.

| writer | what it does | when |
|---|---|---|
| `0x1e0fe4` in `FUN_001e0f30` | `= 0` | object activation, `vt+0x15c` (`0x1e0f30` on every object vtable). Called from each class's build: shop `0x1d18b4`, sideshow `0x1d23d8`, feature `0x130464`, coaster `0x11fca4`, ordinary `0x1b7c00`, tour `0x1e9090`, track `0x1fff24` (READ: `lw ...,0x15c(vt)`+`jalr` sites). The shop and sideshow ones are READ in decompile. That the others are those classes' builds is INFERRED from their address ranges. |
| `0x1e15a4` in `FUN_001e1508` | `= save[0]` | savegame load (base). Called by ride `0x116bdc`, feature `0x130698`, shop `0x1d1a78`, sideshow `0x1d24e0`. Saved by `FUN_001e1460` (`save[0] = obj[0x18]`), called by `0x116ad4 / 0x130630 / 0x1d19d8 / 0x1d2430`. **Persists across save/load** (READ). |
| `0x1e1ce4` in `FUN_001e1cd8` | `+= 1` | four direct callers and no vtable slot (jal scan; `findword` finds no table entry): |

The four increments, all inside the guest's use handlers:

| call | function | class | trigger (READ) |
|---|---|---|---|
| `0x20f2b0` | `FUN_0020edd8`, kinds 1/3/6/7 | **rides** | after the ride's happiness, sickness and boredom effects. Target `s2 = guest[0x28]` (`0x20f1ac`). |
| `0x20ef84` | `FUN_0020edd8`, kind 2 | **toilet** | only when `vt+0x1dc` (relief check, `0x130780` on feature vtable `0x35dc70`) != 0. Counts on every use **regardless of bladder level**: the `+0x79 < 61` branch only skips the wear. |
| `0x20e940` | `FUN_0020e1a0` | **shop** | only inside the purchase test `price < want && price*10 <= cash` (branches `0x20e2f4`, `0x20e30c`). One per **successful sale**. |
| `0x20ecdc` | `FUN_0020ec00` | **sideshow** | only when the game is **played and paid** (the second want/cash gate at `0x20ec44` and `0x20ec68` passes). |

**When `FUN_0020edd8` runs (READ).** It is the guest-update handler for state `+0x37 == 0x16`, dispatched at
`0x211854` in `FUN_002113a8`. State 0x16 is set by three things:
- `FUN_00117e08`, the **per-rider unload**, at `0x117fc4`. It also does riders `+0x120 -= 1` and places the guest
  on the exit. Callers: ordinary script unload `0x1168c4` (`FUN_001166a8`), coaster car `0x1aed70`, tour
  `0x1e9a88` and `0x1ea2d0`, track car `0x2022e8`, and unload-all `0x118054`.
- the sideshow's normal per-player release `0x1d28c0` (`FUN_001d2880`), and its demolition ejection `0x1d29e8`
  (`FUN_001d2948`, see §8).
- `FUN_0020e160` (`0x20e188`): the in-building wait (state 0x23) timing out, which is the path for shops and
  toilets.

**So for rides, Users = guests who stepped off.** It counts one per rider when that rider is unloaded and then
processed. A cycle carrying N riders adds N. Boarding does not count (READ).

**Exception, evacuation (READ).** Selling a ride (`FUN_00116458` -> `0x117758(ride,1)` -> `0x118018`) unloads every
rider and then immediately sends each one message `{0x35a548, 10}` (`0x118044`, `0x118064`; `0x118ab8` returns
`this[1]` = 10). The guest's message handler `0x20f588`, case 10, sets state 0 and `guest[0x28] = 0` before the
state-0x16 handler can run. **Evacuees are not counted.** Breakdown queue-dumps pass 0 (`0x1b8c0c`, `0x1b8c68`) and
unload no riders.

**Resets: none except build and load.** Searched as follows:
- every `sw` at `+0x18` and `+0x20` in the ride-family code ranges (`0x116000-0x119800`, `0x11f000-0x124000`,
  `0x130000-0x131000`, `0x1b7000-0x1b9500`, `0x1d1000-0x1d3000`, `0x1e0000-0x1e6000`, `0x1e9000-0x1eb000`,
  `0x200000-0x204000`). The only hits are vtable stores, the two above, and unrelated classes (`0x118b08`,
  `0x119xxx`).
- a whole-binary read-modify-write census of `+0x18`. 42 sites, and the only one in object code is `0x1e1ce4`.
- the month rollover `0x16b060` and its callees `0x16c120` and `0x100a18`, plus the week handler `0x16bc70`.
  None of them writes an object field.

**Not monthly, and not reset on upgrade** (`0x116268` writes no `+0x18`).

---------------------------------------------------------------------------------------------------

## 3. Shop Satisfaction (Q3)

Getter `FUN_001d1e00(cls)` (READ):
```
if (vt+0xd4 (= obj[0x18], purchases) == 0) return 0;
return min(100, cls[0xb0] / cls[0xb4]);      // signed int div; console traps(7) if b4 == 0
```

Accumulator `FUN_001d1e68(cls, x)`. It has one caller, `0x20e95c`.
```
if (x < 0) x = 0;
cls[0xb0] += x;        // 0x1d1ea4
cls[0xb4] += 1;        // 0x1d1eac
advisor counter (product-dependent 14/15/16/17) += (x-50)/4 (C-style /4)
```

The call is at the **common exit** of `FUN_0020e1a0`, so it runs for **every shop visit, bought or not**. Both
failed gates branch to `0x20e948` (from `0x20e2f4` and `0x20e30c`). The argument is computed at
`0x20e94c..0x20e960`:
`x = (guest[0x75]_after - guest[0x75]_before) * 5`, where `guest+0x75` is happiness, a signed byte, and *before* is
read at function entry.

**So `+0xb4` counts visits, not customers.** It is the satisfaction divisor. `obj+0x18` counts purchases.

The happiness change on a purchase (READ, `0x20e1a0` switch on product `FUN_001d1d08` = record `+0x30`):

| product | Δh added to `guest+0x75` (each sum capped at 100, with no lower clamp) |
|---|---|
| 0, 1, 4, 5, 7 | `(s8)((HappinessEffect * (Quality - Additive/15)) / 100)` |
| 2, 6 | `(s8)((HappinessEffect * Quality) / 100)` |
| 3 (balloon) | same as 2/6, **only if the guest had no balloon** (`+0x34` bit 4 clear), otherwise 0 |
| other | 0 (the default arm only logs) |

`HappinessEffect` = record `+0x34` (`FUN_001d1b88`). `Quality` = `cls+0xba` (`0x1d1f50`). `Additive` = `cls+0xac`
(`0x1d1fb8`). A failed visit leaves happiness unchanged, so it records x = 0.

**Range** 0..100. **Updates** once per visit processed by the shop (state-0x16 handler). It is a **running mean
over every visit since build**: `Σ max(0, 5·Δh) / visits`, integer-truncated, shown as 0 until the first sale.

Typical value (INFERRED from the formula): at the default Quality 100 and Additive 0, a sale scores
`5·HappinessEffect` (less if the guest was near 100 happiness). Satisfaction is that times the fraction of visits
that bought. A shop whose record has HappinessEffect 0 always reads 0.

---------------------------------------------------------------------------------------------------

## 4. Sideshow: Satisfaction and Winners (Q3, Q4)

Sideshow fields, offsets from `cls` (READ: build `FUN_001d21f0`, accessors `0x1d2a28..0x1d2b48`):

| off | meaning | writers |
|---|---|---|
| `+0xa8` u32 | prize value (record `+0x2e`) | build `0x1d2a50`, load |
| `+0xac` u32 | **satisfaction divisor** (attempts) | `+= 1` at `0x1d2bf0`, load `0x1d2508`. **⚠ No zeroing writer found**: build does not write it, and there is no zero store anywhere (full-binary `sw` census at `+0xac`). |
| `+0xb0` s32 | satisfaction accumulator | `+= max(0,x)` at `0x1d2bf8`, `= 0` in build `0x1d23a8` |
| `+0xb4` | current occupants (not displayed) | build, load, `0x1d26f8` +1, `0x1d27d0` -1 |
| `+0xcc` u16 | game price (record `+0x2c`) | |
| `+0xce` u16 | win percentage (record `+0x30`) | slider |
| **`+0xd0`** u32 | **Winners** | `+= 1` at `0x1d2608` (`FUN_001d2560`), `= 0` in build `0x1d23ac`. Whole-binary census found no other sideshow-context writer. |
| `+0xd4` u32 | prize value paid out (sum) | `+= prize` at `0x1d2610`, `= 0` in build |
| `+0xd8` u32 | takings (sum of prices) | `+= price` at `0x1d263c`, `= 0` in build. Single Sideshow Takings 949 = `0x1d2b40` = `+0xd8`. Profit 238 and All Sideshows Total Profit 365 = `0x1d2a58` = `+0xd8 - +0xd4`. |

**Satisfaction getter `FUN_001d2b48`** (READ): `obj[0x18]==0 ? 0 : min(100, cls[0xb0] / cls[0xac])`. This is the shop
formula with `+0xac` as the divisor.

**The guest's path, end to end (READ):**

1. **Arrival**, `FUN_0020d628` case 5 (`0x20d7xx`):
   - Compute `W = FUN_0020eb20(guest, ss)`:
     `W = ((win% == 0 ? 100 : win%·prize/100) · (DAT_002eeb74+100)/100) · (happy+100)/100`, where
     `DAT_002eeb74` = 100 in the image (no lui/addiu store to it found).
   - If `W < price` or `cash < price·10`, the guest goes elsewhere and **nothing is recorded**.
   - Otherwise `r = rand(100)`, where `0x1448e0` is `raw % n`, so 0..99. If there is no free slot
     (`FUN_001d26a8`), the guest goes elsewhere.
   - Otherwise join via `FUN_001d26f8(ss, guest, r < win%)`, and set **guest flag `+0x34` bit 0x400 =
     `r < win%`**. This is the ONLY setter of bit 0x400 (ori/sh census: `0x20d854` -> `0x20d860`).
2. **Release**, `FUN_001d2880` (state 0x16), then `0x20edd8` case 5, then `FUN_0020ec00`:
   - Recompute W and re-check `W >= price` and `cash >= price·10`.
   - If both pass, call `net = FUN_001d2560(ss, guest)`:
     - credit `price·10` to the park (`0x1007d8`).
     - **if bit 0x400: `+0xd0 += 1`, `+0xd4 += prize`, debit `prize·10` (`0x100698`), net = price - prize.**
     - `+0xd8 += price`.
   - Then `cash -= net·10`, `happiness += sign(net)·DAT_002eeb40` (s8, capped at 100; `0x20ec90..0x20ecd4`),
     **Users++** (`0x20ecdc`).
   - **Always** call `FUN_001d2bb0(ss, (h_after - h_before)·5)` at `0x20ecf4`. That does `+0xac += 1`,
     `+0xb0 += max(0,x)`, and advisor counter 0x12.

**Winners vs SideshowWinPercentage (Q4, READ):**
- Each game that is joined and then played counts as a win with probability exactly `win%/100` (`rand(100) <
  win%`).
- The roll uses the win% **at join time**, and the booking happens at release.
- So `Winners / Customers` tends to `win%/100`. They are not equal: a guest who joined but failed the
  release-time gate is in neither count.
- The Winners row and the Winning Chance slider are drawn only when **prize value != 0** (`0x1d8488`, `0x1d86d4`).
  With prize 0 the Winners row is **absent** and every later row moves up 32.

**Satisfaction range in practice (READ arithmetic; constant INFERRED):**
- `DAT_002eeb40` = 10 in the image (`0x2eeb40`).
- A paid game with `net > 0` (a loss, or a win worth less than the price) adds +10 happiness, so it scores
  **x = 50**, or `5·(100-h)` if the guest was above 90.
- A win worth more than the price gives net < 0, -10 happiness, so x = 0. An exactly even win also scores 0.
- **So sideshow satisfaction cannot exceed 50 with the image constant.** The bar never fills past half.
- The only store to `0x2eeb40` is `0x12c44c` in `FUN_0012c348`. That is the per-frame method of screen class
  `0x35c530` (ctor `0x12bfa8`, which names itself "Debug Menu"), and it writes back a slider that was initialised
  from the same global. Normal play therefore runs at 10 (INFERRED).

---------------------------------------------------------------------------------------------------

## 5. Save/load behaviour of these fields (READ; port may choose to replicate)

- **Shop** save `0x1d19b8` writes `+0xb4` (u32, `save+8`) and **satisfaction as a byte** (`0x1d1e00` -> `save+0x17`,
  `0x1d1a20`). Load `0x1d1a58` sets `+0xb4 = save+8` and **`+0xb0 = satByte · +0xb4`** (`0x1d1ae0`/`0x1d1ae4`).
  The mean survives the round trip, truncated to an integer. Users persists through the base save.
- **Sideshow** save `0x1d2410` writes `+0xd0/+0xd4/+0xd8/+0xdc` as u16 and satisfaction as a byte (`0x1d2488`).
  **The load `0x1d24c0` reads none of them back.** The load path is `0x160310` -> `FUN_0014a6e8` -> `vt+0x154` =
  build `0x1d21f0` (which zeroes `+0xb0/+0xd0/+0xd4/+0xd8`) -> `0x1d24c0`. It restores only price, prize,
  `+0xac`, win% and Users.
- **So after loading a save, a sideshow shows Winners 0, Takings 0, Profit 0 and Satisfaction 0, while Customers
  keeps its value.** That is a console asymmetry, READ statically. A savestate before and after a load would
  confirm the visible result.

---------------------------------------------------------------------------------------------------

## 6. Port mapping and hooks (Q5)

| console | port today | needs |
|---|---|---|
| `obj+0x18` (all classes) | `ParkRide.Customers` (ParkSim.cs, incremented only in `Book`) | **Reuse `Customers` as this field for every class.** It already has the right meaning for shops (one per sale). Add the three missing increments below. ⚠ Fix its doc comment: it says `+0xb4`, which is the shop's **visit** count. |
| shop `+0xb0` / `+0xb4` | none (`LaptopShopScreen.cs` sets `_satisfaction = 0` "not yet tracked") | new `SatisfactionSum`, `SatisfactionVisits` |
| sideshow `+0xb0` / `+0xac` | none | same two fields; the divisor sits at a different console offset on this class, but one port pair is enough |
| sideshow `+0xd0` | none | new `Winners` |
| sideshow `+0xd4` / `+0xd8` | `Takings`/`Profit` exist but are only booked by `Book` (shop margin) | the sideshow arm books `Takings += price`; `Profit += price - (won ? prize : 0)` |

Hooks (ParkVisitors.cs; line numbers are from today's reading, and the file is moving):

1. **Rides**: `Serve(guest, used)`, ride arm, next to `Needs.Ride(...)` (≈ line 1255). Add `used.Customers++`
   (console `0x20f2b0`). This is the right trigger, because `Serve` is reached only through
   `returning.CompletedRide`, which is set from `ride.Left` in `Collect()` (≈ 682), the port's `0x117e08`. The
   port's removal path ("aborted removal is not a use") already matches the message-10 evacuation rule.
   `NativeRideQueue.Board` (`Boarded++`) is the **wrong** hook: the console counts unloads, not boardings.
2. **Toilets**: `Serve` relief arm, beside `Relieved++` (≈ 1209). Add `used.Customers++` (`0x20ef84`).
   Unconditional within the arm, matching the console's indifference to bladder level.
3. **Shops**: the increment already happens: `Take` -> `Book` -> `Customers++` on `bought`, matching `0x20e940`.
   For satisfaction, beside the `AdvisorShopEvents(guest, used, def, before, bought)` call (≈ 1239), for every
   visit, bought or not:
   `x = max(0, (Needs.Of(guest).Happiness - before.Happiness)*5); used.SatisfactionSum += x; used.SatisfactionVisits++;`
   (`0x1d1e68`). ⚠ Do it **outside** `AdvisorShopEvents`, which returns early for an unjoined record, because the
   console records unconditionally. The x arithmetic already exists inside that method and can be lifted.
4. **Sideshows**: **no arm exists.** `AssetKind.Sideshow` appears in core only in value, advisor and db code.
   `Serve` falls through to the ride arm, because `Sells` is `HungerEffect||ThirstEffect` (RideCatalogue.cs:303);
   that the fall-through happens for real sideshow records is INFERRED. Winners and sideshow satisfaction need a
   new arm that reproduces §4:
   - the join-time roll `rand(100) < SideshowWinPercentage` stored on the guest.
   - the release-time gate.
   - `FUN_001d2560` booking: `Customers++`, Winners on a win, Takings and prize cost.
   - `happiness += sign(net)*10`.
   - always: `SatisfactionSum += max(0,5Δh)`, `SatisfactionVisits++`.

Getters for the viewer (`game/Viewer.cs` `LaptopInfoCell`, `ShowRideDetails`, `ShowToiletDetails`,
`ShowSideshowDetails`; `LaptopShopScreen.cs:178`):
```
Satisfaction => Customers == 0 || SatisfactionVisits == 0 ? 0 : Math.Min(100, SatisfactionSum / SatisfactionVisits);
```
The second guard replaces the console's divide trap. Then:
- 415, 168 and 771 read `Customers`, as they already do. They become correct once hooks 1 and 2 land.
- 1074 and 451 read the shop Satisfaction.
- 743 and 537 read the sideshow Satisfaction.
- 637 reads `Winners`, shown **only when `SideshowPrizeValue != 0`**.

Resets: zero all of these at build (the port's construction). Never zero them monthly.

---------------------------------------------------------------------------------------------------

## 7. Side findings outside the brief (READ, flagged for whoever owns them)

- **The sideshow's Winners row is conditional** (§4). `LaptopScreens.Sideshow` lists 637 unconditionally.
  With prize 0 the console omits the row and the slider. The label gate is at `0x1d8488`/`0x1d8490`: it branches
  past the 637 label and leaves the row step at +0x20.
- **The sideshow's prize and price values go through `0x142b68` (digits)**, at `0x1d87bc` and `0x1d8854`, **not
  through `0x142908`**. The comment at `LaptopScreens.cs` ≈ line 238 says `FUN_00142908`. Takings and Profit on
  the same screen do use `0x142908` (`0x1d8714`, `0x1d8740`).
- **The Single Shop's "Takings" is the all-time `+0xbc`**, despite the string key `..._MONTHLY_TAKINGS`. The draw
  reads `0x1d1da0`, not the per-28-day rate `0x1d1da8`. The shop save truncates `+0xbc` and `+0xc0` to 16 bits
  (`0x1d19ec`, `0x1d19f4`).
- **`+0xac` means two different things.** On a shop it is the additive slider (`0x1d1fc0`). On a sideshow it is the
  satisfaction divisor. The offset does not carry its meaning across classes; ParkSim.cs already warns about this
  pattern for `+0xb4`.

---------------------------------------------------------------------------------------------------

## 8. Undetermined, and what would settle it

| question | why open | settle with |
|---|---|---|
| Is `DAT_002eeb40` 10 at runtime? (It caps sideshow satisfaction at 50.) | the image says 10; the only writer is the debug-screen writeback | `p2s.py` savestate: read the u32 at EE `0x2eeb40` |
| The initial value of sideshow `+0xac` on a reused pool slot | no zeroing writer found; pool init `FUN_00147eb0` doesn't write it; allocator zeroing not traced | savestate: sell a sideshow, build a new one in the same slot, read `cls+0xac` (pool `DAT_003952a4`, objects 0xe0 apart) |
| A sideshow demolished while occupied | `FUN_001d2948` sets state 0x16 **without** message 10, then removes the object, so the evacuees may run `0x20ec00` on a freed object | runtime only; edge case, recommend the port skip it |
| Whether every port sideshow definition really falls into the ride arm | depends on `.sam` data | print `Sells` for each sideshow definition |
| The visible post-load sideshow zeroes | static chain READ; not seen on screen | savestate pair across a load |

---------------------------------------------------------------------------------------------------

## 9. How the searches were done

- Function and vtable references: `jals.py` (jal/j target scan over the text PT_LOAD), `findword.py` (aligned
  32-bit data words), `matval.py` (lui + addiu/ori/lw/sw pairs within 24 instructions; ⚠ does not cover
  gp-relative addressing).
- Field stores: `stores.py` (`sw`/`sh`/`sb` at a given offset, excluding `sp`), and `classify18.py` (RMW versus
  zero store versus other).
- Writers outside the class code ranges at `+0xb0/+0xb4/+0xac/+0xd0`: `ctxcheck.py`. This is a function-level
  heuristic: a function that neither calls `0x1d1xxx/0x1d2xxx/0x1e1xxx` nor materialises a shop or sideshow pool
  or vtable is treated as not shop/sideshow. Only feature-class hits (`0x20d79c`, `0x20f038`, `0x130d24`) touched
  an attraction.
- State 0x16 setters: `sbconst2.py` (`sb` at `+0x37` or `+0x2f` after `li 0x16`). Hits: `0x20e188`, `0x117fc4`,
  `0x1d28c0`, `0x1d29e8`.
- Win flag: `orflag.py` (`ori 0x400` then `sh` at `+0x34` or `+0x2c`). One hit: `0x20d854`.
