# The laptop's "tabs": there is no tab strip, and the four pages are two cursor rows plus a state field

Research for the info screens' sub-pages, 2026-09-28. Question asked: how does the console select and
draw a tab, which screens really have which tabs, and what is on the ride's Upgrades / Options /
Addons pages. Everything below is read from `SLES_500.32` (vaddr = file offset + 0xFF000), the
`.sce` layouts in `MENUS.WAD`, and `Text/translations/eur/{eng,id}.dat`. Nothing here is inferred
from the string table alone; every "this exists" claim has a code address, and every "this does not"
has a control that the same search hits.

**The answer in one paragraph.** The Single-item screens have a **state field** `this+0x2f4`
(0 Details, 1 Options, 2 Upgrade, 3 Addons) and the base per-frame method dispatches the draw on it.
But nothing ever *selects a tab*: the only writes to the state are in the base's per-frame hook,
which moves 0 → 2 or 0 → 3 when the Details cursor sits on the **"Upgrades" / "Addons" rows** and
Confirm is pressed, and 2 → 0 on Back. State 1 ("Options") is set only by a `SetState` method that
**no code calls**. The shop, sideshow and toilet never leave state 0 at all — their "Details /
Options" strings are pushed into a menu widget that is constructed and never drawn. So: the ride has
two real sub-pages (Upgrades, Addons) reached from two Details rows; nobody has an Options page;
the toilet's "Upgrades" is a shipped string with no code behind it.

---

## 1. How the pages work mechanically

### 1.1 The driver runs a screen through five devirtualised calls

Each Single-item screen has its own runner in the laptop driver; the ride's is `FUN_001c5fb0`:

```c
FUN_001d4670(&s);                 // ctor (screen object lives on the driver's stack, 0x1930 bytes)
s.yoff /*+0xc*/ = 100;            // the vertical offset every text draw adds (see 1.6)
FUN_001d4a38(&s, driver->0x78);   // SetObject(selected ride)          -- vtable slot 16, called directly
while (driver->4) {
    if (FUN_00153930() == 0) FUN_001d4fb0(&s);   // slot 3: the per-frame HOOK (input + state)
    if (s.exit /*+0x90*/) { s.+0x94 ? (DAT_00397744 = 1, FUN_001c5748()) : FUN_001c5680(0); dtor; return 1; }
    FUN_00196288(); FUN_001d51e8(&s, 0, 0, 0);   // slot 2: the per-frame DRAW
    FUN_00194628(1);
    if (s.back /*+0x88*/) break;                 // Back leaves the screen
}
FUN_001c5680(0); FUN_001d48d8(&s, 2); return 0;
```

Shop `FUN_001c6278` (ctor `FUN_001d6bc8`, hook `FUN_001d6f08`, draw `FUN_001d70a0`; it stores the
object straight into `s+0x2fc` instead of calling SetObject), sideshow `FUN_001c6540` (SetObject =
base `FUN_001d95b0` at `0x1c6578`), toilet `FUN_001c6808` (`FUN_001d95b0` at `0x1c6854`, hook
`FUN_001d9fb8`, draw `FUN_001d9fe0`). **Every call is direct.** The driver never touches any other
method of the screen, which is what makes the "who calls SetState" question answerable by census.

### 1.2 The Single-item base: fields and vtable (`FUN_001d9500`, vtable `0x368f50`)

| field | meaning | set by |
|---|---|---|
| `+0x2f4` | **state**: 0 Details, 1 Options, 2 Upgrade, 3 Addons | ctor = 0 (`0x1d9598`); hook (`0x1d97c8`, `0x1d9820`); SetState (`0x1d9610`, dead) |
| `+0x2f8` | previous state | SetState only |
| `+0x2fc` | the selected world object | SetObject / driver |
| `+0x300` | a text-options menu widget (`FUN_001c8d80`, vt `0x367870`) | ctor; see §2 |
| `+0x578` | a listbox (`FUN_0015d338`, rect x 280 y 80 w 180 h 110 hardcoded at `0x1d95xx`) | the dead Options state |
| `+0x950` | the spinning-model widget (`FUN_0017e0a8`) | all pages |
| `+0x88 / +0x8c / +0x90` | root flags: Back pressed / Confirm pressed / exit-laptop | `FUN_00164d00` via slots 14/13/15 |

Vtable `0x368f50`, read to its own end (31 slots, next word 0):

| slot | base | ride `0x368868` | shop `0x368ae0` | sideshow `0x368d00` | toilet `0x3690e0` | role |
|---:|---|---|---|---|---|---|
| 2 | `1d98c8` | `1d51e8`→base | `1d70a0`→base | `1d8260`→base | `1d9fe0`→base | per-frame draw dispatcher |
| 3 | `1d96b0` | `1d4fb0`→base | **`1d6f08` own** | **`1d80b8` own** | `1d9fb8`→base | per-frame hook (input, state changes) |
| 16 | `1d95b0` | `1d4a38` | `1d7a28` | base | base | SetObject |
| 17 | `1d95e0` | – | – | – | – | SetState — **never called** |
| 18 | `1d9c88` | – | – | – | – | GetState (`return this+0x2f4`) |
| 19 | stub | `1d4c80` | `1d6d28` | `1d7f48` | `1d9f70` | prepare Details (slider ranges/values) |
| 21 | stub | stub | `1d6f68` | `1d8118` | stub | own Details input (shop/sideshow only) |
| 22 | `1d9848` = **return 0** | `1d4fd0` | base | base | base | Details cursor; returns the *requested* state |
| 23 | `1d9850` | – | – | – | – | listbox activation (dead with state 1) |
| 24 | stub | `1d5c00` | base | base | base | state-2 (Upgrade page) input |
| 25 | stub | `1d5dc8` | base | base | base | state-3 (Addons page) input |
| 26 | stub | `1d5210` | `1d70c8` | `1d8288` | `1da008` | state-0 draw (Details) |
| 27 | `1d99e0` | – | – | – | – | listbox draw (dead) |
| 28 | stub | `1d6058` | base | base | base | state-2 draw (Upgrade page) |
| 29 | stub | `1d6338` | base | base | base | state-3 draw (Addons page) |
| 30 | stub | `1d5ec8` | base | base | base | pre-input for states 2/3 |

(Slots 4–15 are the root class's, identical everywhere: `164d00` input, `165e00` = Back flag,
`165e08` = Confirm flag, `165e30` set Confirm, `165e88` set Back, `165ee0` set exit.)

### 1.3 The draw dispatcher, read to the end (`FUN_001d98c8`, 42 lines)

```c
FUN_001649c8(this,0,0,0);                    // root per-frame (FUN_00212838(this+0x4c); slot 11 = this+0x84 = 1)
switch (this->state /*+0x2f4*/) {
  case 0:  slot26(this, a,b,c); break;                         // Details
  case 2:  (this+0x9d0 ? slot29 : slot28)(this, a,b,c); break; // Upgrade page (0x9d0 = "maxed", see 3.2)
  case 3:  slot29(this, a,b,c); break;                         // Addons page
  default: /* state 1 and anything else draws NOTHING */ ;
}
```

State 1 draws nothing here; the Options listbox would have been drawn by slot 27, which has no
caller in the laptop (§2.2).

### 1.4 The hook: the only place the state changes (`FUN_001d96b0`, read to the end)

```c
r = 0;
if (state == 2 || state == 3) { slot30(this); state == 2 ? slot24(this) : slot25(this); }
else { slot19(this); r = slot22(this); FUN_00164b70(this, r == 2 || r == 3); }   // gizmo slot 2 = "Select"(477) / " "(310)
FUN_00164d00(this, 0);           // root input: Back → sound 0x130 + Back flag; Confirm → sound 0x12f + Confirm flag;
                                 //             button 0xd → exit; button 0xc → exit + "close laptop" (mgr+8 = 1)
if ((r == 2 || r == 3) && slot8(this) /*Confirm flag*/) { slot13(this, 0); state = r; }     // 0x1d97c8
if (slot7(this) /*Back flag*/ && state == 2) { slot14(this, 0); slot13(this, 0); state = 0; } // 0x1d9820
FUN_0017e1e8(this + 0x950);      // model widget tick
```

So the transitions are exactly: **0 → 2 or 3** when slot 22 asks for it and Confirm is down, and
**2 → 0** on Back. ⚠ Back in state **3** is not intercepted: `FUN_00164d00` sets the Back flag,
the driver loop sees `s+0x88` and **leaves the Single Ride screen** — Back on the Addons page does
not return to Details. That is what the code does; whether the port keeps it is master's call.

The store census over the whole image for offset `0x2f4` in the item-base region finds exactly
these writers (`0x1d9598` ctor, `0x1d9610` SetState, `0x1d97c8` / `0x1d9820` hook); nothing else.

### 1.5 `SetState` (slot 17, `FUN_001d95e0`) exists and is dead

It would set the state, remember the previous one, and retitle the screen:
`0` → `FUN_00165948(this+0xa0, 972)` = `STR_SINGLETHING_INFORMATION` "Details" + slot 19;
`1` → 635 `STR_SINGLETHING_OPTIONS` "Options" + `FUN_0015d958(this+0x578, obj)` (fills the listbox, §2.2);
`2`/`3` → 705 `STR_SINGLETHING_UPGRADE` "Upgrade" + slot 30.

- **Direct callers**: a `jal` census over the entire file (0x1000..EOF, so no code was outside the
  scan): **zero**. Control: the same census finds the hook's two callers (`0x1d4fb8`, `0x1d9fc0`)
  and the dispatcher's four.
- **Virtual callers**: `lw rX, 0x8c(vt)` followed by `jalr rX` — none in the driver
  (`0x1c5000..0x1c9800`), the item screens (`0x1d3f00..0x1db000`), the root class
  (`0x164000..0x166000`) or the laptop menu (`0x12b000..0x12d800`). Control: the same scan finds
  slot-15 (`0x7c`) call sites at `0x1d5394`, `0x1d5d40`, `0x1d5ea4`, ... The 13 sites elsewhere in
  the image belong to other classes' vtables (world objects, tools).
- The three title ids 972/635/705 appear as immediates **only inside SetState**
  (`0x1d9654`, `0x1d966c`, `0x1d967c`). So the console never shows "Details" / "Options" /
  "Upgrade" as a page title: **the title stays "Single Ride" (407) on every page.**

### 1.6 Coordinates, colours, and the y offset

- All draws add `this+0xc` (= 100, set by the driver) as the 5th argument of
  `FUN_00138798(ctx, str, x, y, yoff)` → `FUN_0020acf8`. The port's Details pages already land in
  the right place with the `.sce` rows, so treat this as already handled, not as new information.
- Text attribute (the `.sce` `<text justify=...>`) is applied with `FUN_001389c0(ctx, attr)` and
  persists until the next call; colours with `FUN_001388e8(ctx, r, g, b)`:
  **(255,255,0)** for the selected row / the item name, **(200,130,0)** for everything else.
- The "arrows" on the sub-pages are the same stack-built sprite the list screens use
  (`FUN_00143b18` … `FUN_002127e8(sprite, col, row, yoff)` … `FUN_00212838`), drawn at the
  `UpgradeNameArrows` frame.

---

## 2. Which screen has which "tab" — with the evidence per string

Immediate-operand census (`addiu/ori rX, $zero, id`) over the driver, the item screens, the root
class and the laptop menu. **Controls that must hit and do:** 119 and 454 at `0x1d5650`/`0x1d5758`
(the Details draw), 297 and 918 at `0x1d61f8`/`0x1d627c` (the Upgrade page), 972/635/705 in SetState.

| screen | string | id | reached? | where it is used |
|---|---|---:|---|---|
| ride | `STR_SINGLER_INFORMATION` "Details" | 8 | **no** | never registered anywhere (the ride ctor pushes no menu entries); SetState dead |
| ride | `STR_SINGLER_UPGRADES` "Upgrades" | 119 | **YES — a Details ROW** | drawn at `0x1d5650`/`0x1d57dc` as row 3 of the label column; Confirm on it → state 2 |
| ride | `STR_SINGLER_OPTIONS` "Options" | 218 | **no** | **zero references in the image** |
| ride | `STR_SINGLER_TRACKUPGRADES` "Addons" | 454 | **YES — a Details ROW** | drawn at `0x1d5758`/`0x1d5800` as row 4; Confirm on it → state 3 |
| shop | `STR_SINGLESHOP_INFORMATION` 751 / `_OPTIONS` 417 | | **dead** | pushed into the `+0x300` menu widget by the ctor (`0x1d6ccc`, `0x1d6cdc` → `FUN_001c8ea0(w, id, 1)`); the widget is never drawn or polled (§2.1) |
| sideshow | `STR_SINGLESHOW_INFORMATION` 44 / `_OPTIONS` 290 | | **dead** | same, ctor `0x1d7ee0`/`0x1d7ef0` (the second hit of 44 at `0x1d8150` is the advisor-hint argument `0x2c`, a coincidence) |
| toilet | `STR_SINGLEBOG_INFORMATION` 302 / `_OPTIONS` 148 | | **dead** | same, ctor `0x1d9f18`/`0x1d9f28` |
| toilet | `STR_SINGLEBOG_UPGRADES` "Upgrades" | 659 | **no** | **zero references**; the toilet Details draw (`FUN_001da008`) draws only 168 Users, 1077 Last Cleaned, 132 Cleanliness |
| all | `STR_SINGLETHING_*` 972/635/705 | | dead | SetState only |
| ride | `STR_SINGLER_NO_UPGRADE_AVAILABLE` 616 | | **no** | zero references; the page shows 335 "Unavailable" instead |

**The sideshow family is `STR_SINGLESHOW_*`** (not `SINGLESIDE`): it has "Details" 44 and
"Options" 290, both dead like the shop's.

### 2.1 What the shop/sideshow/toilet "Details / Options" entries went into

`FUN_001c8d80` (vtable `0x367870`) is the **vertical text-menu widget** the All Staff, finance,
park-stats and Build & Hire screens draw as their `textoptions` column: `FUN_001c92c8` draws its
entries top-down at font-height + 2, selected entry in palette `0x35f560`, others `0x35f550`,
disabled ones (200,130,0); `FUN_001c8ee8` moves the cursor with pad bits 1/2 and confirms with
button 0. It is not a horizontal strip. The three item ctors reset it (`FUN_001c8e58`) and push two
entries. Callers of its draw: `0x10c654 0x134af8 0x185380 0x1978c4 0x1988bc`; of its input:
`0x10bfc0 0x134934 0x1851a0 0x1977dc` — **none in `0x1d3f00..0x1db000`**, and no `lw ..,0x14/0x1c(vt)`
+ `jalr` site there either. The item screens never draw it. (The ride's SetObject resets the widget at
`0x1d4bc0` and pushes nothing.)

### 2.2 The Options page that was never wired

State 1 would have populated the `+0x578` listbox with `FUN_0015d958(list, obj)` — the object's
**context menu**, built per kind from 8-byte `{text id, callback}` descriptors at `0x360f28..0x360fc8`:

| kind | entries (in order) |
|---|---|
| 1 Coaster | Build/Edit Queue (`0x3aa`/`0x3b1`, by `obj.vt[37]`), Build/Edit Track (`vt[36]`), Edit Pylons 0x416, Call Mechanic 0xb3 (if `vt[24]` and life `vt[89]`), Delete 0x273, Zoom To 0x187 |
| 3 Ride, 7 Tour | Build/Edit Queue, Call Mechanic, Delete, Zoom To |
| 6 Track ride | Build/Edit Queue, Build/Edit Track (`vt[35]`), Call Mechanic, Delete, Zoom To |
| 4 Shop, 5 Sideshow | Call Cleaner 0x38d (if `vt[24]`), Delete, Zoom To |
| 2 Feature | toilet (`vt[38]`): as shop; staff room (`FUN_001308c8`): Kick Out Mechanics/Researchers/Cleaners/Entertainers/Guards (`FUN_0014aa90(obj, kind)`); then Delete, Zoom To |
| 10 Staff | Set Patrol Area 0x4f, Fire 0x38a (`vt[60]`), Training 0x3a2 (if `(obj.byte[0x48]&7) < 4`), Zoom To |
| 18 | Open 0x114 (if `vt[24]==0 && vt[34]==0`), Zoom To |

This listbox IS live elsewhere — the Single Staff and Staff Room screens populate it
(`0x1d901c`, `0x1dcba0`) and the in-world popup does (`0x13d008`). For the item screens it is dead
data: the only populate/activate/draw calls on `this+0x578` are SetState's and slots 23/27.

### 2.3 Where a ride/shop/sideshow's "options" really live

On the **Details** page, as cursor rows (slot 21/22 + slot 19):

- **Ride** (`FUN_001d4c80` / `FUN_001d4fd0`): row 0 Speed slider, 1 Capacity, 2 Duration, 3 Upgrades,
  4 Addons. Slider ranges come from the record's three tiers: speed `[min(+0x38,+0x6c,+0xa0),
  max(+0x3c,+0x70,+0xa4)]`, duration `[min(+0x40,+0x74,+0xa8), max(+0x44,+0x78,+0xac)]`, capacity
  `[1, max(+0x30,+0x64,+0x98)]` (coasters: `vt[104]` = `FUN_00117b28`); values from `vt[94..96]`
  (`obj+0xe8/+0xec/+0xf0`), written back through `vt[97..99]` (`FUN_00118378/1183a0/1183c8`).
  Rows 1–2 exist only when `kind != 1 && capacityMax >= 2`; otherwise the cursor skips them — and
  moving DOWN from row 0 then lands on the LAST row (`iVar6 = maxRow` when pad bit 0 is clear),
  moving UP from row 3 lands on row 0. Up/down = `FUN_00181700(0)` bits 1/2, sound `0xd6`; wrap.
  (`findings/laptop-sliders.md` has the slider widget itself.)
- **Shop** (`FUN_001d6d28` / `FUN_001d6f68`): quality slider 50..100 (`FUN_001d1f50/58`), additive
  slider 0..100 (`FUN_001d1fb8/c0`), sale-price numeric 1..500 clamped by `shop.u16[0xb8]`
  (`FUN_00207b10`); rows 0..2, or 0..1 when `FUN_001d1f60(shop) == 0`. Advisor hint `0x2b`.
- **Sideshow** (`FUN_001d7f48` / `FUN_001d8118`): chance-of-winning slider 0..100
  (`FUN_001d2a40/38`), price-per-game numeric 1..1000 (`FUN_001d2a50`), prize-cost numeric 1..1000
  (`FUN_001d2a28`); rows 0..2, or only 0 when `FUN_001d2a48(ss) == 0`.
- **Toilet**: no input handler at all (slot 21 stub, slot 22 = 0); Details is read-only.

---

## 3. The ride's Upgrades page (state 2) — full spec

### 3.1 Layout (`main_i_ride_data_upgrd.sce`, scene id 0x19, bound by `FUN_001d3f98`)

| `.sce` element | frame | global(s) | drawn as |
|---|---|---|---|
| `UpgradeName` | row 65 col 45, `justify=left` | `2e9bd0`/`2e9bcc`/attr `2e9bdc` | the **ride's name** (`FUN_0012b548(record)` = text id at record+4), colour (255,255,0) |
| `UpgradeNameArrows` | row 84 col 270 | `2e9bd8`/`2e9bd4` | the arrow sprite, only when **list B** (addons) has > 1 entry — even on this page (gate `this+0x1390` at `0x1d60c8`) |
| `costtext` | row 388 col 45 | `2e9bf0`/`2e9bec` | 297 `STR_SINGLER_UPGRADE_COST` "Upgrade Cost" |
| `costvalue` | row 388 col 250 | `2e9bfc`/`2e9bf8` | `"$" + tier[idx].cost` via `FUN_00142908` (thousands commas; stored word as-is) |
| `stocktext` | row 200 col 45 | `2e9c18`/`2e9c14` | 918 `STR_PURCHASE_STOCK` "Stock" — **kind 6 (track ride) only** |
| `stockvalue` | row 200 col 250 | `2e9c24`/`2e9c20` | `3 − ride.addonsPlaced` (`FUN_00202f88`: `3 - byte[+0x2890]` of the track-ride object) |
| `UpgradeModel` | row 208 col 315, 147×240 | `2e9c08`/`2e9c04`/`2e9c0c`/`2e9c10` | the model widget, showing the ride's **current** model (`FUN_0017e150`'s third argument, the tier index, is not used — disassembly `0x17e150..0x17e16c`) |

⚠ Only `UpgradeName`'s text attribute is ever applied (`FUN_001389c0` at the top of `FUN_001d6058`);
the `justify=center` authored on `costvalue`/`stockvalue` is bound into `2e9c00`/`2e9c28` and never
used, so cost and stock are left-justified at col 250 by the console. ⚠ The list-A caption
("Upgrade 1"/"Upgrade 2", §3.2) is **never drawn** — the page names the ride, not the tier.
The cost/stock block and the model are drawn only when list A is non-empty (`this+0xf94 != 0`).

### 3.2 What is offered, and how availability is decided (`FUN_001d4a38`, SetObject)

```
kind      = obj.vt[20]()                         -> this+0x18c8
level     = obj.byte[0x126]  (the current tier, dba.md);  this+0x18cc = level + 1
tiersOK   = FUN_0012ba08(db, kind, obj.byte[0x97] /*asset index*/)   // s3 in the disassembly
maxed     = (level + 1 >= 3)                      -> this+0x9d0
listA     = (level + 1 < tiersOK) ? [ 0x3689f8[level+1] ] : []      // 595 "Upgrade 1" for level 0, 596 "Upgrade 2" for level 1
canUpgrade= !maxed && obj.u16[0x94] /*life, vt[89] FUN_001e1d58*/ != 0 && level + 1 < tiersOK   -> this+0x18d8
```

`FUN_0012ba08` returns how many tiers of this asset are researched, auto-completing tiers whose
research group (`record+0x48 + 0x34*tier`, `FUN_0012b758`) is 0 (`FUN_0012baf8` stores the count in
the cache at `0x389650`). So **exactly one upgrade is ever offered: the next tier**, and only when it
is researched, the ride is not condemned, and it is below tier 2. There is no paging on this page
(list A has ≤ 1 entry; `FUN_0015c368` on it is a no-op for up/down).

The displayed index is `this+0x18d0 = level + 1 + sel` (`sel` = 0 when list A has its entry, −1
when empty), recomputed every frame in slot 30 (`FUN_001d5ec8`) and slot 24. The cost shown is
`record + 0x50 + 0x34 * (level+1)` — tier `level+1`'s `CostOfUpgrade` in `findings/dba.md`'s tier
table (= `.sam` `Upgrades[level+1].CostOfUpgrade`, the port's `RideCatalogue.UpgradeCost(tier)`).
It is drawn as the raw word; the eventual debit is `word * 10` in park tenths, i.e. the same
figure the port's `Money.Display` would print.

### 3.3 Buying (slot 24, `FUN_001d5c00`, read to the end)

Each frame on the page: pad edge bits; `FUN_00107390(advisor, 0x2a)` (advisor tick); gizmo slot 2
reads "Select" (477) when list A is non-empty, " " (310) otherwise (`FUN_00164b70`). Then:

```
if (FUN_0015c368(listA))            // Confirm on the list (sound 0x12f); empty list + Confirm -> error sound 0xaf
  debug "Apply upgrade %d" (string 0x368850)
  if (FUN_0014d6b0() == 0)                       // *(DAT_003952b0 + 0xc): the mechanic head-count (the port's reading, MechanicSmoke.cs "NoMechanics")
       advisor message 124 (FUN_001073f0), popup box 207 (FUN_00107c90/ca8/cc0 -> FUN_001075f0), sound 0xaf
  else if (FUN_0016c988(FUN_0016ae90(), 2))      // byte[0x309 + 8*(2-1)] of the FUN_0016ae90 singleton: staff kind 2 = mechanics on strike (the port's IsStriking reading)
       advisor 125, popup 125, sound 0xaf
  else
       slot15(this, 1)                            // exit flag -> the driver CLOSES THE LAPTOP (§1.1)
       FUN_00124270() -> FUN_00153d10(ride) -> FUN_00153950(0x3953e8, 0x2b739c, 15, ride)   // append to the 15-slot upgrade list, no duplicates
       ride.+0x128 = 1                            // "Upgrading Now..." (920) on the Details row
```

**No money is checked or taken here.** Installation is the mechanics' job — `FUN_00116268(ride,
silent)` (callers `0x154260`, `0x178b9c`): clears `+0x128`, `level++` if `< 3`, `FUN_00116120`
(capacity re-init), debits `tier[level].cost * 10` through `FUN_00100698`, plays `0xb8` (or `0xe1`
if the new level is 3, which the laptop never requests). ⚠ `FUN_00100698`'s "can't afford" return is ignored there: an unaffordable
upgrade is installed unpaid. The port's core already carries all of this
(`ParkStaff.RequestUpgrade` → `UpgradeRequest.{NoMechanics, MechanicsOnStrike, ListFull, Queued}`,
`Sim.InstallUpgrade`), so the missing piece is only the page.

Advisor entries 124/125/207 (table base `0x2a6ac8`, stride 0x38, `findings/advisor.md`) carry the
blank text 310; entry 207 names the speech sample `PS2_18` (`0x358010`). What the player hears is
the advisor system's business; the laptop only posts the ids.

### 3.4 Leaving

Back → state 0 (Details), Confirm/Back flags cleared. The `this+0x9d0` (maxed) branch of the
dispatcher, which would show the Addons draw on this page, is unreachable for the ride: the
Upgrades row is disabled when maxed, so state 2 is never entered maxed.

---

## 4. The Addons page (state 3) — track rides only

**What an addon is.** A kind-8 asset (`TrackUpgrade`, the DBA's "track upgrade" family) — a
special track piece placed onto an existing track ride: `findings/track-ride-tool.md` (mode 10,
tool `0x389610`, place `0x202980`, max 3 per ride at `+0x2890`, price = kind-8 record `+0x20`
×10 debited on placement) and `findings/track-ride-geometry.md`.

**How it is listed** (`FUN_001d4a38`, kind 6 only): for every record `i` of the current world's
track-upgrade table (`FUN_0012b070(db)` entries; `FUN_0012ad78(db,i)` is the kind-8 accessor —
`FUN_0012ae78` routes kind 8 there and kinds 1–7 elsewhere) that passes
`FUN_0012b6d0(db, 8, i, 0)` — true when `DAT_002b3070` or `DAT_002b72a8` is set (everything
available) or research progress of tier 0 is 100% — push `{name = text id at record+4, id = i}`
into list B (`this+0x133c`; ids at `this+0x1738 + 4*n`). `this+0x18dc = kind == 6 && count > 0`
enables the Details row.

**Draw** (`FUN_001d6338`, same frames as §3.1): name of `listB[sel]` (255,255,0) at (45,65); arrows
if count > 1 at (270,84); "Upgrade Cost" + `"$" + record.+0x20` at (45,388)/(250,388) (the kind-8
price, same word `FUN_0012bc10` returns for the Build menu); "Stock" + `3 − placed` at
(45,200)/(250,200); the **ride's** model in the window (the addon id passed to `FUN_0017e150` is
dropped). Records are acquired/released per frame (`FUN_0012ad78`/`FUN_0012ae18`).

**Input** (`FUN_001d5dc8`): up/down cycle list B with wrap (sound `0xd6`); Confirm (`0x12f`) and
`stock != 0` → `FUN_00125460(FUN_0014e180(), 10, listB.id[sel], 0)` = enter the add-on placement
tool with that record, then `slot15(this,1)` → the laptop closes and the tool is live. With stock 0
nothing happens (no message). Back → leaves the Single Ride screen (§1.4).

---

## 5. What this means for the port

1. **There is no tab widget to build.** Keep the Details page; make its "Upgrades" and "Addons"
   label rows cursor rows 3 and 4 (after Speed/Capacity/Duration); Confirm on them replaces the
   Details body with the Upgrade / Addons page under the unchanged "Single Ride" title.
2. The current shortcut (the Upgrades row calling request-upgrade directly) skips a page whose
   only content is: ride name, "Upgrade Cost $N" for the next tier, the ride's model, and for track
   rides "Stock 3−n". Confirm on that page is what fires the request the port already has — and
   then **closes the laptop**.
3. The Addons page is a picker over the researched kind-8 records of the current world; Confirm
   hands the chosen kind to the port's existing add-on tool (`game/Viewer.TrackAddons.cs`) and
   closes the laptop.
4. Nothing to build for Options on any screen. Nothing to build for the toilet's "Upgrades".
5. Two console quirks to decide on rather than copy blindly: Back on the Addons page exits the
   screen; on rides without capacity/duration rows, Down from the Speed row jumps to the last row.

## 6. Not determined, and what would settle it

- `DAT_002e9b04`, the Details label pitch, has no writer visible to `lui`+`addiu` xref
  (`tools/re/xref.py`); the authored bar frames sit 32 rows apart. A `$gp`-relative sweep or a
  savestate read of `0x2e9b04` settles it.
- `FUN_0014d6b0`'s field (`DAT_003952b0 + 0xc`) is taken as the mechanic count on the strength of
  the port's earlier reading (`game/tests/MechanicSmoke.cs`, which names `0x1D5C00`); the owning
  init `FUN_00147eb0` (3.8 KB) was not re-read here.
- `FUN_001092d0` (the "Apply upgrade %d" call) is assumed to be a debug print like the empty
  `FUN_0016d4c8`; not decompiled.
- The names "sandbox / all-available" for `DAT_002b3070` and `DAT_002b72a8` are from their effect
  in `FUN_0012b6d0` (every record passes), not from a read of who sets them.
- A PCSX2 savestate of a Single Ride at tier 0 with a researched tier 1 would confirm the two
  visible claims a port will care about most: the Upgrade page shows the ride's name (not
  "Upgrade 1"), and its values are left-justified at col 250.

## Appendix: addresses used

Driver `FUN_001c5fb0/1c6278/1c6540/1c6808`; base ctor `FUN_001d9500`, dispatcher `FUN_001d98c8`,
hook `FUN_001d96b0`, SetState `FUN_001d95e0`, SetObject base `FUN_001d95b0` / ride `FUN_001d4a38`;
ride ctor `FUN_001d4670`, binder `FUN_001d3f98`, Details draw `FUN_001d5210`, prepare `FUN_001d4c80`,
cursor `FUN_001d4fd0`, pre-input `FUN_001d5ec8`, Upgrade input `FUN_001d5c00`, Addons input
`FUN_001d5dc8`, Upgrade draw `FUN_001d6058`, Addons draw `FUN_001d6338`, model rect `FUN_001d5f50/5fc8`;
root input `FUN_00164d00`, flags `FUN_00165e00/e08/e30/e88/ee0`; listbox `FUN_0015bb58` (input
`FUN_0015c368`, add `FUN_0015c550/598/650`, clear `FUN_0015c5e0`, populate `FUN_0015d958`);
text-menu widget `FUN_001c8d80` (add `FUN_001c8ea0`, draw `FUN_001c92c8`, input `FUN_001c8ee8`);
asset db `FUN_0012a550/ad78/ae18/ae78/b070/b548/b6d0/b758/b928/ba08/baf8/bc10`; upgrade list
`FUN_00153950`, install `FUN_00116268`, debit `FUN_00100698`; world-object virtuals on vtable
`0x35a560` family: slot 11 `FUN_001e1420` record, 12 `FUN_001e1e00` model, 20 kind, 26 `FUN_001e1ce8`
users, 89 `FUN_001e1d58` life, 94–99 `FUN_00118398/1183c0/1183e8/118378/1183a0/1183c8`
speed/capacity/duration get/set; stock `FUN_00202f88`; tier-name table `0x3689f8`; option
descriptors `0x360f28..0x360fc8`; sounds bank 0: `0xd6` cursor, `0x12f` confirm, `0x130` back,
`0xaf` error.
