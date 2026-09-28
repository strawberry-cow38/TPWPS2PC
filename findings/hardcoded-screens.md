# The two "hardcoded-layout" laptop screens: Research and Game Options

Researched 2026-09-28 for the port lead. Source: `SLES_500.32` (PAL), Ghidra decompiles plus the
MIPS where the decompile was noisy, `MENUS.WAD/main_research.sce` and `main_gameoptions.sce`,
`Text/translations/eur/{eng,id}.dat`. Every address is native (`vaddr = file offset + 0xFF000`).

## ⭐⭐⭐ Where the layout comes from: THE `.sce`, BY NAME, LIKE EVERY OTHER SCREEN

The research question was built on a false negative. `laptop-screens.md` says the element names
`musicslider`, `musicslidertext`, `sfxslider`, `sfxslidertext`, `researchbars`, `researchitem`
are **absent from the executable as byte strings**. They are not. A case-folded byte search of
the image this session finds every one of them at once, next to the element names of the other
screens:

| name in the `.sce` | in the image | address | used by |
|---|---|---:|---|
| `musicslider` | `MusicSlider` | `0x35e960` | `FUN_00139968` (`0x1399c4`) |
| `sfxslider` | `SfxSlider` | `0x35e970` | `FUN_00139968` |
| `musicslidertext` | `MusicSliderText` | `0x35e980` | `FUN_00139968` |
| `sfxslidertext` | `SfxSliderText` | `0x35e990` | `FUN_00139968` |
| `textoptions` | `TextOptions` | `0x35e9a0` | `FUN_00139968` |
| `textoptions` | `TextOptions` | `0x365dd0` | `FUN_001b5218` (`0x1b5274`) |
| -- | **`OverallBar`** | `0x365de0` | `FUN_001b5218` -- ⚠ NOT in the shipped `.sce` |
| -- | **`OverallText`** | `0x365df0` | `FUN_001b5218` -- ⚠ NOT in the shipped `.sce` |
| `researchbars` | `ResearchBars` | `0x365e00` | `FUN_001b5218` (`0x1b5360`) |
| `researchitem` | `ResearchItem` | `0x365e10` | `FUN_001b5218` (`0x1b53b4`) |

⭐ Controls: the same search finds `TextOptions` at 11 sites and `CostItem` at 2, so it reaches
what it should; and `tools/re/xref.py` on the five game-options strings hits `0x1399c4` inside
`FUN_00139968`, which is what a binder looks like. (`numericoptions`, the third "absent" name in
that document, is at `0x35e428` -- the balance sheet is a false negative of the same search. Not
pursued here.)

So **both screens have a normal binder**, called from their constructor, that loads their scene
by menu id and looks every element up by name with the same three calls every other screen uses
(`FUN_00177978` find-by-name, `FUN_00177ac8` frame as `[row, col, w, h]`, `FUN_00177b08` justify):

- **Game Options**: `FUN_00139968`, scene **id 1** (`FUN_001fc4e8(x, 1)` / `FUN_001fc508(x, 1)` /
  `FUN_001fc598(x, 1)`), called from the constructor `FUN_00139ba0` at `0x139c40`.
- **Research**: `FUN_001b5218`, scene **id 9**, called from the constructor `FUN_001b5410`.

The bound values land in per-screen globals that read **non-zero defaults** (game options) or
**zero** (research) in the disc image and are overwritten at bind time. The image defaults for game
options are NOT what the game shows -- see the table below -- which is the trap the memory note on
`.sce` files already warns about: do not take a UI number out of the executable.

⚠ **No savestate with the screen open is needed.** Everything the draw uses is either bound from
the `.sce` (and the `.sce` is on the disc) or a read-only constant in `.data` that this document
lists. The one exception is the `TextOptions` scale pair `0x2eec28`/`0x2eec2c` (see Game Options).

### What kind of class these two are -- and why the slot-26 rule does not apply

Both vtables are **16 slots long** (read to their own end): slot 1 destructor, slot 2 per-frame
draw, slot 3 input/update, slots 4..15 inherited unchanged from the **root** base `FUN_001648a8`
(vtable `0x361a60`: `164d00 1649c0 165df8 165e00 165e08 165e10 165e18 165e20 165e28 165e30
165e88 165ee0`). They do NOT derive from the Single-item base `FUN_001d9500`, so there is no
state machine and **no slot 26**: the draw IS slot 2.

| screen | vtable | slot 1 dtor | slot 2 draw | slot 3 input | ctor | binder |
|---|---|---|---|---|---|---|
| Game Options | `0x35e9e0` | `0x13a7d8` | **`0x13a258`** | **`0x139df0`** | `0x139ba0` | `0x139968` |
| Research | `0x365e38` | `0x1b5e40` | **`0x1b5920`** | **`0x1b5668`** | `0x1b5410` | `0x1b5218` |

Slot 2's signature is `(this, xOff, yOff, z)`: the base `FUN_001649c8(this, 0, 0, 0)` is called
first, and the text/bar/slider draws pass the fourth argument through as the fifth parameter of
`FUN_00138580` / `FUN_00138798` (-> `FUN_0020acf8(font, x, y, z, str)`). Research passes
`z + *(short*)(this+0xc)`; Game Options passes the constant `DAT_002b6218 = 100` (read-only in the
image, only readers `0x13a30c..0x13a4bc`). It is a depth, not a coordinate.

Text colours, both screens: the cursor row `(255, 255, 0)`, every other row `(200, 130, 0)`
(`FUN_001388e8(ctx, r, g, b)`; research reads them as the triplets at `0x35f560` / `0x35f550`,
game options as immediates). Game Options resets to `(128,128,128)` at the end.

---

## Game Options (`main_gameoptions.sce`, menu id 1)

### Layout: every element from the `.sce`, one constant step

| element | `.sce` authors | bound into (row / col / w / h / justify) | image defaults (WRONG, overwritten) |
|---|---|---|---|
| `MusicSlider` | row 120 col 214, 72×22 | `0x2b6230` / `0x2b622c` / `0x2b6234` / `0x2b6238` | 100 / 220 / 100 / 32 |
| `MusicSliderText` | row 116 col 45, left | `0x2b6244` / `0x2b6240` / -- / -- / `0x2b623c` | 100 / 160 |
| `SfxSlider` | row 157 col 214, 72×22 | `0x2b6258` / `0x2b6254` / `0x2b625c` / `0x2b6260` | 122 / 220 / 100 / 32 |
| `SfxSliderText` | row 150 col 45, left | `0x2b6250` / `0x2b624c` / -- / -- / `0x2b6248` | 122 / 160 |
| `TextOptions` | row 185 col 45, left | `0x2b6220` = y, `0x2b621c` = x (see ⚠), `0x2b6228` justify | 150 / 250 |

⚠ `TextOptions` alone goes through `FUN_00212588(&pt, col, row)`: `x = DAT_002eec28 * col`,
`y = DAT_002eec2c * row` (floats), then `(int)`. Both floats are **1.0f in the image** and
`xref.py` finds no writer (`$gp`-relative writes would be invisible to it). Treat the scale as 1.0
-- it is the only number on either screen that a savestate-with-screen-open could still refine.

Constants the draw adds, all read-only in `.data`:

| what | global | value |
|---|---|---:|
| row step of the text rows | `DAT_002b6224` (only reader `0x13a4dc`) | **32** |
| draw depth | `DAT_002b6218` | 100 |

So the screen, as shown: "Music" at (45, 116); music slider at (214, 120) 72×22; "SFX" at
(45, 150); SFX slider at (214, 157) 72×22; then four text rows at **x = 45, y = 185 + 32k**:
185 Tutorial, 217 Vibration, 249 Save Game, 281 Quit Current Game.

⭐ The `laptop-screens.md` inventory already counted **exactly two slider constructions**
(`FUN_001da630` at `this+0x2f4` and `this+0x42c`) in this constructor; that matches.

### Rows (`FUN_0013a258`, in draw order)

The cursor is `this+0x564`, 0..5 with wrap; row *i* is yellow when `i == cursor`. Rows 2..5 read
their text id from the table at `0x2b6288` (`{0x18f, 0x337, 0xd2|0x34b, 0xd4|0x371, 0xeb, 0x2fb}`;
the tutorial and vibration entries are rewritten at construction and on every toggle).

| # | y | text id | `STR_` key | English | value / source |
|--:|--:|--:|---|---|---|
| 0 | 116 | 399 | `STR_FRONTEND_MUSIC` | Music | slider `this+0x42c`, 0..128, from `DAT_002abe20` |
| 1 | 150 | 823 | `STR_FRONTEND_SFX` | SFX | slider `this+0x2f4`, 0..128, from `DAT_002abe1c` |
| 2 | 185 | 210 / 843 | `STR_FRONTEND_SPEECH_ON` / `_OFF` | Tutorial On / Tutorial Off | `*DAT_002aa720 & 0x40` (advisor flag byte) |
| 3 | 217 | 212 / 881 | `STR_OPTIONS_VIBRATION_ON` / `_OFF` | Vibration ON / Vibration OFF | `*[DAT_002e982c]` (pad object word) |
| 4 | 249 | 235 | `STR_MAINMENU_EXIT_TO_MAPSCREEN_AND_SAVE` | Save Game | posts `0x30008` |
| 5 | 281 | 763 | `STR_MAINMENU_QUIT_GAME` | Quit Current Game | posts `0xe0008` |

⚠ **"TV Mode" (808, `STR_OPTIONS_TV_MODE`) is NOT on this screen.** The earlier note that the
constructor "registers" it came from the immediate `0x328` at `0x139cc8`, which is
`addiu s0, s6, 0x328` -- a field address (`this+0x328`), not a text id. The constructor registers
no title either (no `FUN_00165948(this+0xa0, ...)` call): the screen has six rows and no heading
of its own.

### Behaviour (`FUN_00139df0`, slot 3)

- **Cursor**: pad bit 1 (up) / bit 2 (down) from `FUN_00181700(0)`, sound `0xd6` on each move,
  wraps 5 -> 0 -> 5. `FUN_00164b70(this, cursor >= 2)` tells the base whether the cursor is on a
  text row (1) or a slider row (0).
- **Accept** (`FUN_00181250(0)` mask): row 2 -> `FUN_0013a178` (tutorial), row 3 ->
  `FUN_0013a1f0` (vibration), row 4 -> `FUN_001510f8(0x30008)`, row 5 ->
  `FUN_001510f8(0xe0008)`, each followed by vtable slot 15 (`vt+0x7c`, with this-adjust
  `vt+0x78`) which closes the screen. `FUN_001510f8(code)` stops the laptop's held sounds,
  clears the advisor (`FUN_001072c8`), and sets `DAT_002b7290 = 1`, `DAT_002b729c = code` for the
  state thread -- `main-menu.md` already reads those two codes as Save / back through the boot
  chain to a cold front end.
- **Sliders**: focus follows the cursor every frame -- `this+0x444 |= (cursor == 0)` focuses the
  music slider, `this+0x30c |= (cursor == 1)` the SFX one (bit 0 of `widget+0x18`). Then
  `FUN_001da740` runs each: with `widget+0x20 == 0` it reads the **held** pad bits
  (`FUN_00181860(0)`), bit 4 (left) -> `FUN_001da820`, bit 8 (right) -> `FUN_001da7c8`:
  `value16.16 -= / += step * 0x100`, clamped to `[min, max]`. Step is `widget+0x30 = 0x200`
  (the constructor overrides the base's `0x100`), so a held direction moves **2 units per frame**
  and the 0..128 range takes 64 frames end to end. The integer part is `(short)widget+0x2e`.
- **While the SFX slider is focused** a preview sample plays: if handle `this+0x310` is not
  playing, `FUN_00111150(audio, 0, 0xb0, &handle)` ("Start Sample"); when it loses focus the
  sample is stopped (`FUN_00111d78`). The music slider has no preview -- music is already
  playing.
- **Every frame, unconditionally**, the values are written out:
  `DAT_002abe1c = DAT_002abe28 = sfx` then `FUN_001118f0(audio, sfx)`;
  `DAT_002abe20 = DAT_002abe30 = music` then `FUN_001118a8(audio, music)`. `FUN_001118a8` posts
  one volume message to the audio object (`vt+0x4c`); `FUN_001118f0` posts two -- the SFX level,
  and a second channel at `min(3 * sfx, 100)` (which channel is not identified). ⭐ `main-menu.md`
  independently attributes the front end's Music slider to `FUN_001118a8` and its SFX slider (with
  the same `0xb0` preview) to `FUN_001118f0`; two screens agree on which is which.
- **Defaults**: `DAT_002abe1c` and `DAT_002abe20` are both `0x40 = 64` in the image -- half
  volume -- and the constructor clamps whatever they hold into 0..128 (`this+0x450/0x454`,
  `0x318/0x31c`) before seeding the sliders.
- **Tutorial** (`FUN_0013a178`): flips bit `0x40` of `*DAT_002aa720`, calls `FUN_001c3950(on)`
  (`DAT_002e98b8 = on`, image 1) and swaps the row label 210 <-> 843.
- **Vibration** (`FUN_0013a1f0`): pad object `FUN_001c17d8()` = `DAT_002e982c`; enabled is its
  first word (`FUN_001c1830`); `FUN_001c17e8` enables, `FUN_001c17f8` disables (and calls
  `FUN_001c1960`, presumably stopping a running rumble); label 212 <-> 881.

Option summary: **two sliders (0..128, default 64, step 2/frame held), two toggles, two actions.
No enum options.**

### The slider widget, for whoever draws it

From `FUN_001da630` (ctor), `FUN_001daae0` (draw), `FUN_001daa88`, `FUN_001da870`, `FUN_001da938`:

| offset | meaning |
|---|---|
| `+0x08` / `+0x0a` / `+0x0c` | x, y, z (shorts); the draw copies the bound frame here every frame |
| `+0x14` / `+0x16` | width, height |
| `+0x18` bit 0 | focused (yellow label, held-input active) |
| `+0x24` / `+0x28` | min / max (0 / 100 by default; game options sets max 0x80) |
| `+0x2c` | value, 16.16; `+0x2e` its integer halfword |
| `+0x30` | step (0x100 default, 0x200 here) |
| `+0x34` | an embedded **bar** (`FUN_00115468`), drawn filled to `value` over `[+0x50, +0x54]` = 0..max |
| `+0xb4` | label string, cleared (`FUN_001db2a0(w, 0)`) -- these sliders show no number |
| `+0x134` | colour pointer, `0x35f540` = `(200,130,0)` |

Knob x within the trough is `(value - min) * (width << 16 / (max - min)) >> 16`
(`FUN_001da870`). The game-options sliders use the subclass vtable `0x3691e8`, which overrides
slot 4 with `FUN_001dace0`: it draws the knob art at `(x + knobX - 5, y - 13)` in white instead of
the base `FUN_001da938`'s `(x + knobX, y)`. That is the whole difference.

---

## Research (`main_research.sce`, menu id 9)

### Layout: the `.sce` again -- with two elements the disc never authored

| element | `.sce` authors | bound into (row / col / w / h / justify) | image | read by the draw? |
|---|---|---|---|---|
| `TextOptions` | row 215 col 45, left | `0x2e74b0` / `0x2e74ac` / -- / -- / `0x2e7530` | 0 | yes |
| `OverallBar` | **not authored** | `0x2e74cc` / `0x2e74c8` / `0x2e74d0` / `0x2e74d4` | 0 | **no** |
| `OverallText` | **not authored** | `0x2e74c4` / `0x2e74c0` | 0 | **no** |
| `ResearchBars` | row 220 col 180, 72×22 | `0x2e74b8` / `0x2e74b4` / `0x2e7538` / `0x2e753c` | 0 | yes |
| `ResearchItem` | row 215 col 260, left | col only -> `0x2e74bc`; justify `0x2e7534` | 0 | yes (col) |

Constant: the row step **`DAT_002e74e8 = 32`**, read-only (`0x1b59d0`, `0x1b5ad8`, no writer) --
the same 32 as game options' `0x2b6224` and the Staff Room's `0x2e9ca8`.

⭐⭐ **The `OverallBar`/`OverallText` globals are written by the binder only when the `.sce`
authors them, which it does not, and NOTHING READS THEM** (`xref.py` over `0x2e74c0..0x2e74d4`:
stores in the binder, no loads; neither the draw nor the input handler touches them). The
constructor also builds one slider at `this+0x300` (`FUN_001da630`) -- the one `laptop-screens.md`
counted against `researchbars` -- and **neither slot 2 nor slot 3 ever draws or updates it**: no
`FUN_001daae0`/`FUN_001da740` call anywhere in `0x1b5528..0x1b5e40`. Together with
`STR_RESEARCH_OVER_ALL_RESEARCH` "Overall Research", `STR_RESEARCH_ALTER_RESEARCH` "Alter" and
`STR_RESEARCH_APPLY` "Apply" having no reader in this class, the reading is: **the PC game's
overall-research budget slider was cut from the PS2 screen**. The budget is simply forced to 100
when the screen opens (`FUN_001b6848(mgr, 100)` in the constructor -- already in
`ResearchManager.OpenResearchScreen`). Port no budget control.

⭐ Note the item column: the binder keeps only `ResearchItem`'s **column** (`frame[1]` ->
`0x2e74bc`), and the draw puts the item names on the **bars'** row: MIPS `0x1b5a9c lw fp,
[0x2e74b8]` (ResearchBars row), `0x1b5ae8 mult s4, [0x2e74e8]`, `0x1b5b48 addu a3, fp, s1`,
`0x1b5b40 lw a2, [0x2e74bc]`. So names sit at (260, 220 + 32i), five pixels below their labels.

As shown, for i = 0..4: label at **(45, 215 + 32i)**; item name (or "Nothing") at
**(260, 220 + 32i)**; progress bar at **(180, 220 + 32i), 72×22**.

### Rows (`FUN_001b5920`, in draw order)

The five rows are the five research **slots** of the manager (`mgr + 0xC + 0x1C*i`, the
`ResearchProject` the port already has): row *i* shows slot *i*. The cursor is `this+0x2f4`
(0..4, wrap). Labels come from the table at `0x365ec0`, read backwards from `0x365ed0` as *i*
counts 4 -> 0:

| i | label id | `STR_` key | English | item column (`FUN_00138798` / `FUN_00138580`) | bar |
|--:|--:|---|---|---|---|
| 0 | 531 | `STR_RESEARCH_RIDES` | Rides | slot 0's item | slot 0 |
| 1 | 65 | `STR_RESEARCH_SHOPS` | Shops | slot 1's item | slot 1 |
| 2 | 185 | `STR_RESEARCH_SIDESHOWS` | Sideshows | slot 2's item | slot 2 |
| 3 | 155 | `STR_RESEARCH_FEATURES` | Features | slot 3's item | slot 3 |
| 4 | 834 | `STR_RESEARCH_UPGRADES` | Upgrades | slot 4's item | slot 4 |

Per row the item column is one of three things:
- **mode 2 and this is the cursor row**: the candidate list's current entry, yellow --
  `FUN_0015cfe0(list, list+0x4c)`: the DBA record's name (`FUN_0012b548` -> text id at
  record `+4`), or 605 `STR_RESEARCH_NOTHING` "Nothing" for the list's trailing empty entry;
- **slot idle** (`slot+0xC == 0`): 605 "Nothing", bar value 0, bar colour `0x365ed8` =
  `(240, 64, 64)` red;
- **slot active**: the project's item name (`FUN_0012ae78(db, slot.category, slot.item)` ->
  `FUN_0012b548`), bar value `progress*100/required` (100 when required is 0, the same
  expression as `ResearchProject.Percent`), bar colour `0x365ee0` = `(64, 240, 64)` green.

The bar is a stack-temporary `FUN_00115468` widget: rect `(x, y, w, h)` at `+8/+0xa/+0x14/+0x16`,
value at `+0x18` (`percent << 16`), range `+0x1c/+0x20` = 0/100, colour pointer at `+0x28`,
drawn by `FUN_00115590(bar, 0, 0, z)`. ⚠ Bug-for-bug detail: on the cursor row in mode 2 the
value/colour are not recomputed, so that bar shows the previous iteration's (row i+1's) value.

Title: 1013 `STR_RESEARCH_RESEARCH` "Research" (`FUN_00165948(this+0xa0, 0x3f5)` at `0x1b54a4`).
Gizmo bar (`FUN_0013e340(gizmo, a, b, c, d)` in slot 3): mode 0 = `545 Back, 967 Close,
931 New, 39 Mainmenu` (`STR_GIZMO_CPP_*`); mode 2 = `545 Back, 967 Close, 477 Select, 39 Mainmenu`.

### Behaviour (`FUN_001b5668`, slot 3): what starts a project

`this+0x2fc` is the mode: **0** = choosing a row, **2** = choosing a candidate for that row.
(Modes 1 and 3 are handled -- 1 behaves as 0 on accept, 3 closes on cancel -- but nothing in the
class sets them; unread.)

1. **Mode 0**: up/down move the cursor with sound `0xd6`, wrap 4 <-> 0. **Accept** ->
   `FUN_001b5528(this, row)` builds the candidate list for that row into `this+0x438`, mode = 2,
   `this+0x2f8 = 0`. **Cancel** -> vtable slot 14 (`vt+0x74`) closes the screen.
2. **Mode 2**: `FUN_0015c368(list)` runs the list: up/down cycle `list+0x4c` through
   `list+0x54` entries (wrapping; sound `0xd6`; an empty list answers accept with sound `0xaf`).
   **Accept** (sound `0x12f`): `FUN_0015d0e8`/`FUN_0015d110` give the entry's item and category
   (`list + 0xEC + 8*sel` = `{category, item}`); if either is -1 (the trailing "Nothing" entry)
   the slot is **cancelled** -- `FUN_001b71d8(mgr, row)` = `slot.Active = 0` -- else
   `FUN_001b55e8`: cancel the slot's current project the same way, then
   **`FUN_001b6880(mgr, row, category, item)` starts the new one**. Mode = 0 either way.
   **Cancel** -> mode 0, nothing changed.
3. Every frame, both modes: `FUN_001b6850(mgr, cursor, 100)` = `slot[cursor].Weight = 100`.
   The port already notes that field has no reader.

⚠ Cancelling loses nothing: the percentage is filed into the state database on every quantum
(`FUN_001b7388` -> `FUN_0012baf8`), and `FUN_001b6880` restarts from that percentage
(`FUN_001b7650`). The port's `ResearchManager.Start(..., percentDone)` already models that.

### ⭐⭐ The candidate list -- the eligibility rule, per row (`FUN_0015ca78`, `FUN_001b7208`)

`FUN_001b5528(this, row)` resets the list (`FUN_0015c8d8`, 64 entries of `{-1,-1}`) and fills it:

| row | fill | categories (`AssetKind`) walked, with slot index |
|--:|---|---|
| 0 Rides | `FUN_0015cbf8(list, 1)` | 3 Ride, 6 TrackRide, 7 TourRide, 1 Coaster -- slot 0 |
| 1 Shops | `FUN_0015cda0(list, 1)` | 4 Shop -- slot 1 |
| 2 Sideshows | `FUN_0015cdf0(list, 1)` | 5 Sideshow -- slot 2 |
| 3 Features | `FUN_0015ce40(list, 1)` | 2 Feature -- slot 3 |
| 4 Upgrades | `FUN_0015ce90(list, 1)` | 3, 6, 7, 1 (ride upgrades) and **8 TrackUpgrade** -- slot 4 |

then `FUN_0015c910` appends the trailing "Nothing" entry (`list+0xe4 = 1`, count + 1).

`FUN_0015ca78(list, kind, count, slot, research=1)` walks the world's catalogue for that kind,
item index 0..count-1 (`count` = `FUN_0012afe0` etc., see the database below), and appends
`{kind, item}` when **`FUN_001b7208(mgr, slot, kind, item)`** is true. (With `research = 0` the
same list class serves the build menus and appends what is *available* instead.) Two side
conditions: a Feature whose record byte `+0x2e` has bit 2 set is skipped in test-park mode
(`FUN_00153410() != 0`); and when `FUN_0014e160() == 2` -- the third park slot, the test park --
the filter is `FUN_00154328(world, park, item)` (the earned tally `main-menu.md` describes)
instead of the research rule.

**`FUN_001b7208(mgr, slot, cat, item)`, the rule**, after refreshing the thresholds
(`FUN_001b7130`):

- slots 0..3, `cat != 8`: `researchGroup(cat, item, tier 0) <= mgr.threshold[slot]`
  **and** not already available;
- slot 4, ride kinds: already available **and** `FUN_0012ac78(db, kind, item) != 0` -- **at
  least one is BUILT in the park** (it counts the world object list, kind via `vt+0xa4` and
  catalogue index via `FUN_001e1e98`) **and** `level(cat, item) < 3` (two upgrade tiers);
- `cat == 8` (any slot): track ride 0 available (`FUN_0012b6d0(db, 6, 0, 0)`) and the add-on
  not yet available.

`FUN_001b6880` then re-checks on start: slot not active, and (`slot == 4` or `group <=
threshold[slot]`); required work = `researchWork(cat, item, level) << 12`; progress = filed
percent of that. ⚠ Slot 4's threshold (`mgr+0xa8`, computed by `FUN_001b6fb8`) is therefore
never consulted -- it is dead.

**Thresholds** (`mgr + 0x98 + 4*slot`, recomputed by `FUN_001b7130` whenever `mgr+0` is set,
i.e. at init and after every completion): for the slot's categories, count per research group
`total[g]` and `done[g]` (available at tier 0); starting at g = 0, while `done[g]*3 >=
total[g]*2` advance; the threshold is the first group **less than two-thirds researched**
(`FUN_001b6be0`; rides pool kinds 6, 7, 1, 3 in `FUN_001b6d68`). `dba.md` already read the
two-thirds rule; this is where it is consumed. ⚠ Groups are indexed into an 8-word stack array
by their raw DBA value, so the data must keep groups in 0..4 (they do -- the loop advances past
an empty group because 0 >= 0).

### Progress, completion, unlock

Progress is the port's `ResearchManager.Contribute` (`FUN_001b6a38`, read here again, unchanged).
`FUN_001b7388` files the percentage into the state database **every quantum**
(`FUN_0012baf8(db, cat, item, level, percent)`), and that is where the unlock happens:

- `FUN_0012baf8`: find-or-add the `{cat, item}` record; **if `percent > 99` then
  `level += 1, percent = 0`**; store when the record's level is <= the requested level.
- So the moment a project's filed percentage reaches 100, the item's **level** goes up, and
  availability everywhere is `FUN_0012b6d0` -> `FUN_0012b928`: `record.level > tier`
  (or the tier's research group is 0, which auto-marks it), or the debug flag, or test park.
- `FUN_001b74a0(slot, 1)` (completion) then clears the slot, sets `mgr+0 = 1` (thresholds
  dirty), fetches the name (a debug print) and posts the advisor message the port already has.

⚠⚠ **`FUN_0012aed8` does NOT unlock anything** -- correcting `ResearchManager.cs`'s
`Researched` comment. It is the RELEASE paired with the record fetch `FUN_0012ae78`: for
categories 1..7 it calls `FUN_0010f2a0`, which is an empty function (`return;`), and for
category 8 `FUN_0012ae18` -> `FUN_0010f0e0` (the release of the handle `FUN_0012ad78` ->
`FUN_0010f0b0` acquired). Every reader in this area calls it straight after `FUN_0012ae78`
(`FUN_0012b758`, `FUN_0012b840`, the draw, `FUN_001b74a0`, `FUN_0015ca78`). `dba.md` says the
same: "the adjacent release methods do not invalidate the cached payload".

### ⭐⭐ The research database: shape and location

Four things, three of which the port already has a home for.

1. **Per-record research data, in the DBA** (`dba.md`): research group at `+0x28` for the simple
   layouts (Feature, Shop, Sideshow, TrackUpgrade) and `+0x48 + 0x34*tier` for the ride kinds;
   research work at `+0x24` / `+0x4c + 0x34*tier` (`FUN_0012b758` / `FUN_0012b840`). A ride's
   **tier is the record's level**: level 0 = the base ride, 1 and 2 = its two upgrades, hence
   "level < 3" above and the port's "level < 2 = a new ride" completion message.
2. **The per-world catalogue**, which defines what an item INDEX means: `FUN_0012ae78(db, cat, i)`
   -> `FUN_0012b1b8` reads `PTR_DAT_00360850[db+8]` (four world tables, `0x395488 0x395580
   0x395678 0x395750`, BSS) `+ db[+4]*4 + kindOffset`, a list of DBA record keys per kind, then
   `FUN_0010f248(key)` finds the loaded record. Kind offsets: `+8` Ride (count `+0x14`),
   `+0x20` TourRide (`+0x2c`), `+0x38` TrackRide (`+0x44`), `+0x50` TrackUpgrade (`+0x5c`,
   16-byte entries), `+0x68` Coaster (`+0x74`), `+0x80` Feature (`+0x8c`), `+0x98` Shop
   (`+0xa4`), `+0xb0` Sideshow (`+0xbc`). `db+4` is `FUN_0014e160()` and `db+8` is
   `FUN_0014e170()` (park slot and world, by the `== 2` test-park test above). The port's
   `AssetResourceDatabase` per-world lists are this; **item = its ordinal in that kind's list**.
3. **The research STATE, `0x389650`: 60 records × 4 bytes `{u8 cat, u8 item, u8 percent,
   u8 level}`**, BSS. Initialised by the database singleton `FUN_0012a550` on first use to
   `{0xff, 0xff, ?, 0}`; `FUN_0012bee8` finds by `(cat, item)` (linear, 60 max) and
   `FUN_0012bf38` allocates the first `{-1,-1}` slot. Records are created lazily by any query.
   ⚠ **60 is a hard cap**: a 61st distinct `(cat, item)` gets a NULL record and `FUN_0012ba08`
   would dereference it. `FUN_0012ba08` also caps level at 3 (auto-advancing over group-0
   tiers). Readers: `FUN_0012ba08` level, `FUN_0012b928` percent, `FUN_0012b6d0` available;
   writer `FUN_0012baf8`. The other reference, `0x12bbb0`, was not read -- it is the obvious
   candidate for the save/load of this list, which nothing here establishes.
4. **The five thresholds** `mgr+0x98..0xa8`, derived (above), not stored.

Debug/global switches that make everything available: `DAT_002b3070`, set by
`FUN_0012be30(x, 0xffff)` from the config key **`"AllResearched"`** (`0x35c400`), and the
test-park flag `DAT_002b72a8` (`FUN_00153410`, documented in `coaster-operation.md`).

**Is anything left to research** (`FUN_00104358(mask)`): per kind, count items not available at
tier 0 (`FUN_001042d0`), bits 1/2/4/8/0x10/0x100/0x200 = kinds 3/7/6/1/2/4/5; then
`FUN_00104bd0() < 100`, the percentage of ride upgrade tiers researched (`FUN_00104b28` adds 2
per ride with level > 0 and `level - 1` done).

### What the port has and lacks, from this reading

Has: the manager, slots, `Contribute`, completion messages, budget 80/100, DBA group/work,
per-world catalogues, `AssetKind`. Lacks: the 60-record state list (level/percent per item and
the `level > tier` availability test that every build menu asks), the two-thirds thresholds, the
per-row eligibility (`FUN_001b7208`, including the "must be built" rule for upgrades), the
candidate list, and the screen itself. ⚠ The port's `Researched` hook should be re-pointed at the
level increment in `FUN_0012baf8`, not at `FUN_0012aed8`.

---

## ⭐⭐ Corroborated in live EE RAM: three PCSX2 savestates, every number above

Read with `tools/p2s.py`'s `ee_ram` from `SLES-50032 (9CBA74B7).{01,resume,01.backup}.p2s`
(a live Jungle park, no laptop open -- the bundled `Screenshot.png` shows the radial
Build/Hire/Laptop/Path menu). The three states agree on every layout global; the shop screen's
bound globals (`0x2e9c48..` = 45 / 175 / 215 / 305) and the Staff Room's row step (`0x2e9ca8` = 32)
are the controls, read from the same dump.

| global | image | **live RAM** | `.sce` authors |
|---|---:|---:|---|
| research `TextOptions` row / col (`0x2e74b0` / `0x2e74ac`) | 0 / 0 | **215 / 45** | 215 / 45 |
| research `ResearchBars` row / col / w / h (`0x2e74b8` / `b4` / `0x2e7538` / `3c`) | 0 | **220 / 180 / 72 / 22** | 220 / 180 / 72 / 22 |
| research `ResearchItem` col (`0x2e74bc`) | 0 | **260** | 260 |
| research `OverallBar` / `OverallText` (`0x2e74c0..d4`) | 0 | **0** | not authored |
| research row step `0x2e74e8` | 32 | **32** | -- |
| options `MusicSlider` (`0x2b6230` / `2c` / `34` / `38`) | 100/220/100/32 | **120 / 214 / 72 / 22** | 120 / 214 / 72 / 22 |
| options `MusicSliderText` (`0x2b6244` / `40`) | 100 / 160 | **116 / 45** | 116 / 45 |
| options `SfxSlider` (`0x2b6258` / `54` / `5c` / `60`) | 122/220/100/32 | **157 / 214 / 72 / 22** | 157 / 214 / 72 / 22 |
| options `SfxSliderText` (`0x2b6250` / `4c`) | 122 / 160 | **150 / 45** | 150 / 45 |
| options `TextOptions` y / x (`0x2b6220` / `1c`) | 150 / 250 | **185 / 45** | 185 / 45 |
| options row step `0x2b6224`, depth `0x2b6218` | 32, 100 | **32, 100** | -- |
| options text table `0x2b6288..9c` | | **399 823 210 212 235 763** | |
| `0x2eec28` / `0x2eec2c` (the `TextOptions` scale) | 1.0 / 1.0 | **1.0 / 1.0** | -- (caveat closed) |
| justify globals (`0x2e7530`, `0x2e7534`, `0x2b623c`, `48`, `28`) | | **0** = left | left |
| volumes `0x2abe1c` / `0x2abe20` | 64 / 64 | 62 / 62 (backup 63 / 63) | -- |
| Staff Room `0x2e9e50` / `0x2e9e54` (for comparison) | 0 | **0** | -- |

So the image's game-options defaults are NOT what the game shows (100/220/100×32 became
120/214/72×22), the research globals are 0 in the image and authored at run time, and the two
elements the shipped `.sce` does not author stay 0 -- in the same dump where every `.sce`-bound
global on three screens holds its authored value.

### The research database, decoded live (two states, one park)

`0x2b2a78`: park slot 0, world 0 (Jungle); catalogue table `0x395488`; debug `AllResearched`
0, test park 0. The Jungle catalogue: 8 Rides, 0 TourRides, 1 TrackRide, 1 Coaster, 17 Features,
8 Shops, 4 Sideshows, 2 TrackUpgrades -- 41 items, and **41 of the 60 state records are in use**,
one per item (the thresholds query every item, so every item gets a record).

Sample records, key -> DBA record -> name, with the state list and the manager beside them:

| (cat, item) | key | name (text id) | tier groups / work | state `.01` | state `.resume` |
|---|---:|---|---|---|---|
| (3, 0) | 221 | Sun God (845) | (1, 750) (2, 1000) (3, 1000) | **21 %, level 0** -- slot 0 active | **62 %, level 0** -- in no slot |
| (3, 4) | 226 | Crazy Ape (1047) | (0, 0) (1, 200) (2, 200) | **80 %, level 1** -- slot 4 active | **0 %, level 2** -- slot 4 complete |
| (4, 6) | 245 | Ice Cream Shop (1069) | (1, 150) | 0 %, level 0 | **89 %, level 0** -- slot 1 item 6, active 0, complete 0 |
| (6, 0) | 220 | Dino Karts (194) | (1, 1250) (2, 200) (3, 300) | 0 %, level 0 | **level 1** -- slot 0 complete, required `0x4E2000` = 1250 << 12 |
| (2, 6) | 192 | Medium Bush (425) | (1, 100) | level 0 | level 1 -- slot 3 complete, required `0x64000` = 100 << 12 |
| (3, 1) | 222 | King of the Swingers (300) | (0, 200) (2, 300) (3, 300) | level 1 | level 1 -- group 0, never researched, auto-marked |

What those six say, each an arithmetic check of the reading above:

- `.01` has all five slots ACTIVE with the same progress `0xA0000` -- the equal share
  `FUN_001b6a38` hands out -- and the filed percentages are exactly `progress*100/required`:
  Sun God `0xA0000*100/0x2EE000` = 21, Costume Shop `/0xC8000` = 80, Dino Racing `/0x190000`
  = 40, Super Toilet `/0x12C000` = 53, Crazy Ape tier 1 `/0xC8000` = 80.
- **Crazy Ape went from level 1 at 80 % to level 2 at 0 %** between the states, and slot 4 holds
  `required 0xC8000` = its tier-1 work 200 << 12, complete: the level increment on reaching 100
  IS the unlock, exactly as `FUN_0012baf8` reads.
- Every item whose tier-0 group is 0 sits at level >= 1 without ever having been researched
  (King, Rocky Racers, Temple of Gloom, Burger/Drinks/Fries, Arcade, Jungle Spray, Security
  Camera, Litter Bin, Staff Room, Toilet): the group-0 auto-mark in `FUN_0012ba08`.
- Sun God's 62 % survives outside any slot, and Ice Cream Shop sits at 89 % with `active 0,
  complete 0, item 6` still in slot 1: cancelling (`FUN_001b7498(slot, 0)`) clears only the
  active flag and the filed percentage is kept for a restart.
- Thresholds `[1, 1, 1, 1, 1]` in both states reproduce from the two-thirds rule: rides' group 0
  is 4 of 4 done (advance), group 1 is 1 of 5 (`10 <= 3` fails) -> 1; shops 3/3 then 1/3 -> 1;
  sideshows 2/2 then 1/2 -> 1; features 4/4 then 2/13 -> 1.
- Budget 100 in both (the screen had been opened); `dirty` 1 in `.resume` after the completions.

⚠ The TrackUpgrade (cat 8) catalogue entries are 16 bytes `{key, 0, key, 0}` (`0x395438`: keys
237, 236) and those keys are NOT in the loaded DBA directory `[0x2aadfc]` -- they are reached
through the handle at `+8` (`FUN_0012ad78` -> `FUN_0010f0b0`), which this reading did not follow.
A reader for cat 8 needs that path; everything else resolves through the directory.

Decoder: `research_db.py` (scratchpad this session; it is 90 lines over `tools/p2s.ee_ram`) --
worth moving into `tools/` when the port grows a research reader to check against it.

## Not determined, and what would settle it

- ~~`DAT_002eec28/2c` (the `TextOptions` scale)~~ -- read 1.0 in all three savestates; closed.
- `FUN_001118f0`'s second volume channel (`min(3*sfx, 100)`): which output it drives.
- The `this+0xc` depth short both screens add, and what sets it (the laptop, presumably).
- Research modes 1 and 3 (handled, never set within the class).
- Save/load of the `0x389650` state list (`0x12bbb0` unread).
- Whether the front-end options page (`FUN_0017fc78`, `main-menu.md`) shares the slider
  subclass `0x3691e8`; not needed for the laptop.

## Corrections this reading makes to earlier documents

- `laptop-screens.md`: the "ABSENT FROM THE EXECUTABLE" section is wrong for all three screens
  (`numericoptions` is at `0x35e428`). Both screens here bind by name; their `.sce` IS the
  authority. Its "`main_research` builds 1 slider against `researchbars`" tension is resolved:
  the slider is the cut budget control, and `researchbars` is five bars drawn from one frame.
- `ResearchManager.cs`: `FUN_0012aed8` is a release, not the unlock; the unlock is the level
  increment in `FUN_0012baf8` when the filed percentage reaches 100.
- `laptop-screens.md`'s vtable table: these two screens have 16 slots and no slot 26; the draw
  is slot 2 (`0x13a258`, `0x1b5920`).
