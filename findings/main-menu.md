# The main menu and the intro / attract movies

What they are, then how they work and why, read out of `SLES_500.32`, `FRONTEND.WAD` and the disc.
Every address is in the executable's own space (vaddr = file offset + 0xFF000). The lobby itself
(`WorldMapSelector`) is deliberately out of scope here — see `findings/lobby-menu.md`; this file
covers everything the player sees **before** it, and the movies.

## WHAT

### ⭐ There are no `.pss` / `.m1v` / `.ipu` streams. The movies are the eleven `MOVIES/*.MPC`

The ISO tree (`tools/iso2.py`, 209 entries) has exactly one movie directory, `/MOVIES/`, holding
eleven `.MPC` files and nothing else. The container is already read (`findings/formats.md`,
"MOVIES/*.MPC"): an EA chunk stream, `MPCh` = one MPEG-2 picture, `SCHl/SCDl` = EA-ADPCM audio,
640×352, and ffmpeg's `ea` demuxer opens it natively. What each one **is**, from the executable's
own tables and from frames pulled with ffmpeg:

| file | frames | fps | what it is | who plays it |
|---|---:|---:|---|---|
| `BFLOGO.MPC` | 286 | 30 | the Bullfrog logo (clouds, the red frog rising) | boot, and the head of every attract cycle |
| `DINO.MPC` | 1203 | 30 | the Jungle world's story intro | first entry into a Jungle park; attract slot 0 |
| `FRANK.MPC` | 1622 | 30 | the Halloween world's story intro | first entry into a Halloween park; attract slot 1 |
| `FLOWER.MPC` | 1424 | 30 | the Fantasy world's story intro | first entry into a Fantasy park; attract slot 2 |
| `SPACEMAN.MPC` | 1491 | 30 | the Space world's story intro | first entry into a Space park; attract slot 3 |
| `ALIEN.MPC` | 1218 | 30 | a fifth story short (the alien stand-up act) | **attract slot 4 only** — no world plays it on entry |
| `FE125.MPC` | 896 | **25** | in-engine gameplay reel with the camcorder viewfinder brackets | attract, second half of slots 0 and 3 |
| `FE225.MPC` | 667 | **25** | same, another park | attract, second half of slots 1 and 4 |
| `FE325.MPC` | 659 | **25** | same, another park | attract, second half of slot 2 |
| `END_E.MPC` | 319 | 30 | the ending, English | game-state 9, every language except French |
| `END_F.MPC` | 316 | 30 | the ending, French | game-state 9, language 1 |

Frame counts and fps are the measured ones from `findings/formats.md`. "FE" is the front end's own
prefix (`FRONT END` is the menu scene's name, below) and the 25 in the name is the PAL frame rate
those three were captured at; the eight rendered shorts are 30 fps.

⭐ **The world→movie pairing is a table, not a naming rule.** The executable keeps the attract
reel as five pointer PAIRS at `0x35ed50` (`{story, reel}`, 8 bytes each), plus a sixth lone pair
for the logo at `0x35ed88` (`{BFLOGO, NULL}`):

| pair | @ | story | reel |
|---|---|---|---|
| 0 | `0x35ed50` | `DINO.MPC` | `FE125.MPC` |
| 1 | `0x35ed58` | `FRANK.MPC` | `FE225.MPC` |
| 2 | `0x35ed60` | `FLOWER.MPC` | `FE325.MPC` |
| 3 | `0x35ed68` | `SPACEMAN.MPC` | `FE125.MPC` |
| 4 | `0x35ed70` | `ALIEN.MPC` | `FE225.MPC` |
| logo | `0x35ed88` | `BFLOGO.MPC` | — |

The pair index equals the lobby's world index (0 Jungle, 1 Halloween, 2 Fantasy, 3 Space — the
same order `FUN_0013b3c8` uses to open the world archives), which is why entering a park can index
this table by world. Pair 4 has no world and is only ever reached by the attract counter.

The ending movies live in `.data`, not `.rodata`, as a per-language table at `0x2b62c0`
(`{name, 0}` pairs): `0x2b62c0` `END_E.MPC`, `0x2b62c8` `END_E.MPC`, `0x2b62d0` `END_F.MPC`,
`0x2b62d8` **`SPANISH.STR`** — a name that exists nowhere on the disc. `FUN_0013bb18` selects by
language through a 9-entry jump table at `0x35ec00`: language 1 → `END_F`, language 5 → the
phantom `SPANISH.STR`, 8 → `0x2b62c0`, everything else → `0x2b62c8` (`END_E`). The Spanish entry is
a leftover from a locale this PAL disc does not ship (its text tables do include `spa`).

### The EA logo is shipped three times, byte-identical

`/DATA/EAGAMES.SSH` (33,872 B, a loose file at ISO level), `FRONTEND.WAD/EAGames/EAGAMES.ssh`
and a `SHPS` blob **embedded in the executable's `.data` at `0x2f0c80`** are the same 33,872 bytes
(md5 `fe8eec2c…` for all three): one `GIMX` entry named `EAGA`, type `0x84` (the IPU macroblock
stream of `findings/ssh.md`), 512×512, 33,744 payload bytes starting at `0x2f0d00`; the header
carries EA's `Buy ERTS` easter egg. The embedded copy is the one the code uses: **`FUN_0012d268`**
waits for the renderer, sets the clear colour from `DAT_002b3088`, and hands `0x2f0d00` to
`FUN_00223fe0(src, 0x1400000, DAT_002ef9fc << 5, 0x200, 0x200, …)` — an IPU decode straight into
GS memory — then kicks one GS packet (`0x70000014 … 0x5000001307000000`) and a DMA send. That is
the whole EA GAMES screen: no scene, no WAD, no font, one decode into the frame buffer. Its caller
is the program's own start-up narrative, `FUN_002314e8` (from `main`'s `0x12e7b8`):

```
print "----------------\nTPW PS2 now loading...\n"          (0x36fa88)
FUN_0020a0e0(0x280, 0x200)                                    display 640 x 512
FUN_0012d268()                                                <- the logo
print "Initializing PS2; modules, graphics, pad etc...\n"     (0x36fab8)   FUN_00231120
print "Initializing game components...\n"                     (0x36faf0)   FUN_0010ee68
print "Running game...\n"                                     (0x36fb18)   FUN_0010eec0 unless
                                                              the string at 0x331538 is non-empty
```

so the logo goes up as the very first frame and stays for the whole of IOP-module loading and
game-component initialisation, until the state thread's first scene draws over it. There is no
timer on it; its length is the boot time. (`FUN_00236768`, the texture DMA-chain builder, also
names `0x2f0c80` as a source pointer; what it does with it is not read.) No `EAGAMES` path string
exists in the executable (control for that negative: the same scan finds every `Data\*.wad`
name), so the loose disc file and the WAD copy are build leftovers, not anything the EE opens by
name.

### `FRONTEND.WAD`: 385 files in 14 directories, 29.2 MB decompressed

| directory | files | what |
|---|---:|---|
| `/EAGames/` | 2 | `EAGAMES.tga/.ssh`, 512×512 24bpp — the logo above |
| `/Lang/` | 9 | `LangUK/GER/FRE.tga/.ssh` 512×512, the language-select backdrop with the map and the three language names **baked into the image**; `girl1_uk/ger/fre.mps` (3 × 14,164 B M3D2), a hostess model per language |
| `/Lang/Textures/` | 18 | the hostess's skin: `girlbody1_*`, `girllegs1_*` per language, `b1_arms`, `head03_h`, `headb03` |
| `/Loading/` | 24 | `{Fantasy,Hallow,Jungle,Space}1{,_Fr,_Ger}.tga/.ssh` 512×512 — the loading backdrop per world per language, "Loading…" and the bar frame baked in |
| `/Loading/{fantasy,hallow,jungle,space}kid/` | 4 × 32 | 16 frames each, 128×128 32bpp, numbered `0000..0030` **in steps of two** — the animated kid on the loading screen |
| `/Loading/4Steve/{halkid,junglekid}/` | 2 × 64 | 32 frames each, `0000..0031` — an earlier full-rate export of the same kids |
| `/Loading/4Steve/{fantasykidblink,junglekidblink}/` | 2 × 32 | 16-frame blink loops |
| `/Mainmenu/` | 12 | `Mainback1{,_US,_JAP}` and `Mainback2{,_US,_JAP}`, 512×512 24bpp |

`Mainback1` is the **title / legal screen**: the logo, the coaster, and the EA copyright paragraph
painted into the texture. `Mainback2` is the same picture with the paragraph removed — the **menu
background**, over which the options are drawn as text. The `_US` and `_JAP` variants carry the
regional legal text; only the plain (UK) pair is named by this executable (`mainback1.ssh` at
`0x3609c8`, `mainback2.ssh` at `0x35e890` and `0x363ca0`).

⚠ `/Loading/4Steve/` is not on the code path. The loader builds `data/frontend/loading/%skid/`
(`0x3614e0`) + `%skid%04d.ssh` (`0x361518`) from the world name, which only reaches the four
top-level `*kid/` folders. "4Steve" is a folder someone handed to Steve, shipped by accident.

### There is no `.sce` for any of this

All 29 layouts in `MENUS.WAD` are `main_*.sce` — the in-park laptop — as `findings/lobby-menu.md`
already found for the lobby. The front end, the language select, the title screen and the loading
screen are code-drawn: a background texture, text placed by literal coordinates, and (for the
menu) a table of text ids.

### The front end's strings

From `/Text/translations/eur/{id,eng}.dat` (read positionally, `tools/textdb.py`):

| id | key | English |
|---:|---|---|
| 922 | `STR_FRONTEND_NEW_GAME` | New Game |
| 509 | `STR_FRONTEND_LOAD_GAME` | Load Game |
| 203 | `STR_FRONTEND_LOAD_SAVE` | Load Game |
| 723 | `STR_FRONTEND_OPTIONS` | Options |
| 675 | `STR_FRONTEND_CPP_CREDITS` | Credits |
| 766 | `STR_FRONTEND_FULL_SIMULATION` | **Main Game** |
| 1080 | `STR_FRONTEND_INSTANT_ACTION` | **Practice Park** |
| 169 | `STR_FRONTEND_ROLLERCOASTER_TEST_PARK` | Rollercoaster Test Park |
| 880 | `STR_FRONTEND_PLAY_GAME` | Play Game |
| 210 / 843 | `STR_FRONTEND_SPEECH_ON` / `_OFF` | Tutorial On / Tutorial Off |
| 399 / 823 / 13 | `STR_FRONTEND_MUSIC` / `_SFX` / `_SCREEN` | Music / SFX / Screen Adjust |
| 212 / 881 | `STR_OPTIONS_VIBRATION_ON` / `_OFF` | Vibration ON / OFF |
| 107 | `STR_FRONTEND_EXIT` | Exit |
| 877 | `STR_FRONTEND_START` | Press START button to Continue |

⚠ `STR_MAINMENU_*` is **not** this menu. Those 20-odd keys (`Ride Information`, `Build & Hire`,
`Close Park`, …) are the in-park laptop's main page, `MENUS.WAD/main.sce`; `findings/shop-info-ui.md`
maps them. The executable's own name for the pre-game menu is `FRONT END`, and its strings are the
`STR_FRONTEND_*` family. ("GOING TO MAIN MENU" at `0x360740`, logged by `FUN_00155b00`, is the laptop
too — it loops on `FUN_001c5ba8`, the `MAIN MENU QUIT` laptop scene.)

### ⭐⭐ The scene family: every screen is a `Scene` with the same 8-slot vtable

`WorldMapSelector` was found by its constructor trace. **No other scene logs its class name** —
the 118 call sites of the trace printer `FUN_00107e48` were enumerated and `WorldMapSelector::` is
the only `Class::Class()` string among them. What every scene does share is the lobby's
**display-name leaf in vtable slot 8**: a frameless `lui v0 / jr ra / addiu v0` returning a string.
Searching `.rodata` for pointers to each such leaf finds the vtables, and the lobby's own
(`0x36dc70` → `"WORLD MAP"`) came out of the same search as a control.

| scene name | name @ | vtable | 1 Enter | 2 Leave | 3 Draw | 4 Update | 6 OnSceneEnd | 7 dtor | object |
|---|---|---|---|---|---|---|---|---|---|
| `FMV SCENE` | `0x35e740` | `0x35e788` | `0x137e98` | base `0x1c4c58` | `0x137f18` | `0x137ed0` | `0x137e78` | `0x1383c0` | 0x40 B, stack |
| `FRONT END` | `0x35e868` | `0x35e8b8` | `0x138ea0` | `0x138fe8` | `0x139258` | `0x139098` | `0x139058` | `0x1394d8` | 0x58 B, stack |
| `LANGUAGE SELECT` | `0x360670` | `0x360698` | `0x1555f8` | `0x1556b8` | `0x155850` | `0x155750` | `0x155730` | `0x155a88` | 0x68 B, stack |
| `LEGALSCREEN` | `0x3609a0` | `0x3609d8` | `0x159a78` | `0x159b28` | `0x159ca8` | `0x159c08` | `0x159be8` | `0x159ea0` | 0x50 B, stack |
| `MEMCARDCHECKSCREEN` | `0x362000` | `0x362018` | `0x172338` | `0x172340` | `0x1725d0` | `0x1723b8` | `0x172398` | `0x172858` | 0x150 B, stack |
| `LOADING SCREEN` | `0x361450` | `0x361528` | `0x1618b0` | `0x1618b8` | `0x161050` | `0x161010` | `0x161098` | `0x161b00` | **runner `FUN_0013bc58` has no caller** |
| `LOAD SAVE` | `0x3615c8` | `0x361630` | `0x1622b8` | `0x162400` | `0x162978` | `0x1625a8` | `0x1623e0` | `0x162c48` | 0x580 B, stack, via `FUN_0013b850(mode)` |
| `LEVEL MENU` | `0x360a38` | `0x360a48` | `0x15a050` | base `0x1c4c58` | `0x15a060` | `0x15a140` | `0x15a210` | `0x15a3a0` | **never constructed** |
| `LAND` | `0x360270` | `0x3602f0` | `0x150e20` | `0x150e80` | `0x151268` | `0x151800` | `0x1524d8` | `0x154d38` | the park itself |
| `WORLD MAP` | `0x36dba0` | `0x36dc70` | `0x216f90` | `0x2178a0` | `0x2183a8` | `0x217e20` | `0x2179c8` | `0x217ae0` | 0x1c30 B, stack |

Slot 5 is `return 0` on every one of them. Slot meanings come from the callers, not the lobby's
guesses: the base scene tick calls slot 3 to draw and slot 4 to update; the scene manager's message
`0x10` calls slot 6 (`FUN_001c4858`, else it logs `"Unknown msg received by SceneMsgReceiver"`), and
the base slot 6 logs `"unhandled onRequestSceneEnd"` (`FUN_001c4cc8`); the delete helper
`FUN_001c4940(scene, 2)` runs slot 7. The base constructor `FUN_001c4908` stores the base vtable
`0x367580` at `this+0x28`; every derived constructor overwrites the same field. The scene manager
keeps the current scene in `DAT_00397648`, the per-frame delta in `DAT_00397640` (`FUN_001c4920`)
and its running total in `DAT_00397644`.

⚠ `LEVEL MENU` (`"Level Select"`, world names `Lost World / Horror / Fantasy / Space` at
`0x360ac0`, maps `Map 1 / Map 2` at `0x360ae0`) has a full vtable and methods but **no constructor
anywhere**: no code or data word holds `0x360a48`, and nothing calls its Enter. It is a developer
level-select that the linker kept. Note its world names are the *internal* ones (`Lost World`,
`Horror`) rather than the shipped `JUNGLE` / `HALLOWEEN`.

### The `FRONT END` object

Constructed inline by `FUN_0013b2a0`: base ctor, then vtable `0x35e8b8` at `+0x28`, then
`+0x2c = DAT_002b62a0` (the "returning from a game" flag). Fields written by Enter (`0x138ea0`),
Update (`0x139098`) and the timeout (`0x139168`):

| offset | meaning |
|---|---|
| `+0x28` | vtable |
| `+0x2c` | ctor arg: 0 on a cold start, 1 when coming back from a park; Enter opens page 3 instead of page 0 when set |
| `+0x30` | idle accumulator for the attract timeout |
| `+0x34` | "scene finished" latch (set by `FUN_001391e0`, which also starts the fade `FUN_0020a1e0`) |
| `+0x3c` | a layout offset, `0x40` (+`DAT_002b6198` when the language is 8, Japanese); probably the text x-origin, not verified |
| `+0x44` | result: timed out → attract (`FUN_00139110` reads it) |
| `+0x48` | result: Load Game chosen (`FUN_00139118` reads, `FUN_00139120` sets) |
| `+0x4c` | result: the fifth exit (`FUN_00139130` reads, `FUN_00139138` sets — it has no caller) |
| `+0x50` | the current page object |
| `+0x54` | the previous page object (set by the page switch `FUN_001392a8`) |

Enter also loads `data/frontend/mainmenu/` + `mainback2.ssh` through `FUN_002158d8`, sets the
clear colour `DAT_002efa18 = 0x80404040`, resets the clip player (`FUN_001059a0`) and starts clip
6 (`FUN_00105a50(6, 0)`); Leave (`0x138fe8`) unloads the texture and calls the current page's
slot 5.

### ⭐ The menu is four page objects, each with its own 6-slot vtable at `+0x30`

`FUN_00138d40` (called first thing in Enter) allocates four pages and parks them in
`DAT_002b61f0 .. DAT_002b6200`; the tear-down loop in `FUN_00138e28` walks five slots and calls
page slot 1 with argument 3. Page vtables sit at `+0x30` (scenes: `+0x28`) and have slots
1 delete, 2 Enter, 3 Update (called from the scene's Update as `vt+0x1c`), 4 Draw, 5 Leave (called
from the scene's Leave as `vt+0x2c`), 6 base leaf `0x17a6f8`.

`FUN_001392a8(scene, n)` is the page switch: it calls the old page's Leave, stores it in `+0x54`,
takes **`DAT_002b61f0[n]`** as the new page, calls its Enter, and — when `n == 0` — resets the
game state first (`FUN_001c33e0`, `FUN_001c3290`). So page numbers are slots of that 5-word array,
and slot 2 (`DAT_002b61f8`) is never filled:

| slot | global | vtable | Enter | Update | Draw | what it is |
|---|---|---|---|---|---|---|
| 0 | `DAT_002b61f0` | `0x361e88` | `0x16d550` | `0x16d638` | `0x16d8a8` | **the main page**: New Game / Load Game / Options — the one the attract timeout watches |
| 1 | `DAT_002b61f4` | `0x363d30` | `0x17f998` | `0x17fc78` | `0x180098` | **options** — measures its four labels (`FUN_00138b20`), builds the Music and SFX sliders (`0x2c3468`, `0x2c33b0`; range 0..0x80, seeded from the sound system's current volumes), and re-uploads `mainback2.ssh` |
| 2 | `DAT_002b61f8` | — | — | — | — | never created |
| 3 | `DAT_002b61fc` | `0x361e48` | `0x16db00` | `0x16dc40` | `0x16dea8` | **the New Game sub-menu**: Main Game / [Rollercoaster Test Park] / Exit |
| 4 | `DAT_002b6200` | `0x363cf0` | `0x180370` | `0x1803c0` | `0x180550` | a **screen-position adjuster**: Enter snapshots the display offset (`FUN_0020a040/50`), Update nudges it with the d-pad (x −24..+24, y −8..+8, applied live through `FUN_0020a028`), Draw shows `Select` (477) / `Cancel` (28); confirm keeps it and cancel restores it, both returning to the options page. ⚠ **Unreachable**: the seven callers of `FUN_001392a8` switch to pages 0, 1 and 3 only, and the options table has no `Screen Adjust` (13) row |

The labels are a table of 12-byte `{text id, width, width}` records at `0x2b9748`, three
zero-terminated groups; page 0 draws the first group (`0x16d8a8`), page 3 draws the second or the
third depending on `FUN_00154378()` (`0x16db00`, `0x16dea8`). Each page's Enter measures its
group's labels with the wobble widget (`FUN_00138b20(0x2b61a0, id)`) and **overwrites words 1 and
2 with the measured width**, so the widths in the file are stale initialisers; Enter also sets the
page's y-origin to `DAT_002b9740 = 250`, the row pitch to `0x20`, the item table pointer and the
row range 0..2 (`FUN_0017a700/710/778/718`).

| @ | id | text | width in file |
|---|---:|---|---:|
| `0x2b9748` | 922 | New Game | 190 |
| `0x2b9754` | 203 | Load Game | 220 |
| `0x2b9760` | 723 | Options | 140 |
| `0x2b976c` | 0 | — | — |
| `0x2b9770` | 766 | Main Game | 250 |
| `0x2b977c` | 169 | Rollercoaster Test Park | 250 |
| `0x2b9788` | 107 | Exit | 80 |
| `0x2b9794` | 0 | — | — |
| `0x2b9798` | 766 | Main Game | 250 |
| `0x2b97a4` | 107 | Exit | 80 |

So the menu tree is **New Game / Load Game / Options**, then under New Game either **Main Game /
Rollercoaster Test Park / Exit** (when `FUN_00154378()` is non-zero) or just **Main Game / Exit**.
Both pages draw at x = 0x100 with the selected row in yellow (`0xff,0xff,0`) and the rest white,
row pitch from the page's `+0x1c`, through the shared wobble-text widget at `0x2b61a0`.
`Practice Park` (1080) is never drawn by either page — the practice mode's label on this menu is
`Rollercoaster Test Park`. `Tutorial On/Off` (210/843) belongs to the options page: its table at
`0x2c3520` is `{399 Music, 823 SFX, 210 Tutorial, 107 Exit}` at 12-byte stride, and `0x2b6288`
(`399, 823, 210, 212, 235, 763`) is the in-park variant of the same page shared with the laptop's
Game Options (`FUN_00139ba0`, called from laptop code at `0x1c5838` / `0x1c5d08`).

### ⭐⭐ The menu is driven by an animation clip player: nothing happens until the clip ends

Both menu pages have the same Update shape (`0x16d638`, `0x16dc40`): up/down move `+0x24` with
sound `0xd6`, wrapping between `+0x28` and `+0x2c`; hovering a new row starts clip 3, 4 or 5, run
forwards or backwards depending on the direction (`FUN_00105a50(clip, reverse)`); confirm
(`FUN_00181250(0)`) zeroes the scene's idle timer (`DAT_0037e110->+0x30`), latches `+0x8`, plays
sound `0x12f` and starts the chosen row's clip — but does not act on it. The action happens in
Draw, once `FUN_00105af0()` reports the clip finished, keyed on `FUN_00105a30()` (which clip):

| page | row | on confirm | when its clip ends |
|---|---|---|---|
| 0 | 0 New Game | switch to page 3 immediately | — |
| 0 | 1 Load Game | clip 1 | `FUN_001391e0` end + `FUN_00139120` (+0x48 → result 4) |
| 0 | 2 Options | clip 2 | `FUN_001392a8(scene, 1)` → the options page |
| 3 | 0 Main Game | clip 0 | `FUN_00153420(0)` (practice off) + end the scene |
| 3 | 1 Rollercoaster Test Park | clip 1 | `FUN_00153420(1)` (**practice on**) + end the scene |
| 3 | Exit (row 1 or 2) | back to page 0, clip 4 | — |
| any | transition clips 3/4/5 done | — | queue a hold clip 7/8/9 (`FUN_00105a10()`, the reverse flag, picks which) via `FUN_00105a70` |

The player itself is five small functions on one state block: `FUN_00105a70(clip, reverse)` sets
`DAT_0037e09c = 1` (playing), `DAT_0037e0a0 = clip`, `DAT_0037e0a4 = reverse` and the position
`DAT_0037e0a8` to 0, or to `(frames − 1) << 12` when reversed, with `frames =
FUN_0010f7f8(0x2a6a80, clip)` — a clip-length lookup in a table at `0x2a6a80`; `FUN_00105a50` is
the same with the "playing" latch cleared first (restart); `FUN_00105af0` advances the position by
`FUN_001c4920()` (the 4-per-update clock, negated when reversed) and returns true at the end;
`FUN_00105a30` / `FUN_00105a10` return the clip and the direction; `FUN_001059a0` resets all of it
(the scene's Enter calls it before starting **clip 6**, the intro, and page 0's Update refuses
input while `FUN_00105a30() == 6`). The position is in 20.12 frames, so with the clock's constant
4 the clips run at 4 frames per update.

⚠ What is animated is not read. The obvious candidate is the language-tagged hostess in `/Lang/`
(`girl1_uk/ger/fre.mps`, one body texture per language, and the sound side's "frontend" category
at `0x359c10`), with clips 3/4/5 as her turning between the rows and 7/8/9 as the holds — but
nothing decompiled here loads `girl1`, and the clip table at `0x2a6a80` has not been tied to an
`.aps`. Treat "hostess" as an inference from the assets.

The options page (`0x17fc78`) is the one page without a clip: row 0 is the Music slider
(adjusting plays sample `0xba`, the level goes to `FUN_001118a8`), row 1 the SFX slider (sample
`0xb0`, level to `FUN_001118f0`), row 2 toggles the tutorial (`FUN_001c3960`), row 3 `Exit` returns
to page 0; every input also zeroes the scene's idle timer.

## HOW / WHY

### ⭐⭐ One state thread drives everything: `FUN_0013be08`

`FUN_0013be08` has no caller by `jal`; its address is materialised at `0x13b0b0` (`FUN_0013b0a0`),
which spawns it. It is a message loop: `FUN_0013a988()` pops a message, `FUN_0017f488(msg, arg)`
posts one, and `FUN_0013a998` is the handler that turns a posted message into `DAT_0037e0b8`, the
next state. Messages are `state << 16 | 8`; `4` pushes a scene (`DAT_002b62a8` current,
`DAT_002b62ac` pending — the pair the debug overlay prints as `CURRENT SCENE: %s` /
`PENDING SCENE: %s` in `FUN_0013add0`), `0x10010` pops one, and `0x10` asks the current scene to
end. Anything else logs `"game state got unknown message %ld"` (`0x35eb00`). The thread's first act
is `FUN_0017f488(0xe0008, 0)`.

| state | what runs | then |
|---|---|---|
| **0xe** | `FUN_0013ba48`: open archive 10 (`Frontend.wad`); `FUN_0013ba78`: a sound-system call (`FUN_00110f00`) | post **0** with `DAT_00392744 = 8` |
| **0** | `FUN_0013b1c0`: **LANGUAGE SELECT** (skipped for good once `DAT_00392740` is set) | post **0xf** |
| **0xf** | `FUN_0013a998`: **MEMCARDCHECKSCREEN**, then `BFLOGO.MPC`, then **LEGALSCREEN** | title pressed START → **1**; title timed out → **0xb** with return-state 0xf, attract index reset |
| **1** | `FUN_0013b2a0`: the **FRONT END** | its result code, below |
| **0xb** | `FUN_0013bb98`: one **attract cycle** | post `DAT_00392744` (0xf from the title, 1 from the menu) |
| **0xa** | — | post 0xb |
| **4** | `FUN_0013b850(1)`: **LOAD SAVE** from the menu | 2 (loaded) → 7; 1 → 1; 0 → 1 with `DAT_002b62a0 = 1`; `SkipFrontEnd` → 6 |
| **0xd** | open all four world archives (2 Jungle, 4 Hallow, 3 Fantasy, 5 Space) | post 7 — this is **Practice Park** |
| **6** | open archive 10, start the **loading thread** (`FUN_00161490(800)`), close it, then `FUN_0013b3c8`: the **WORLD MAP** | 1 → 7 (a park was chosen); 0 → 5 |
| **5** | `FUN_0013b850(2)`: LOAD SAVE in mode 2 (the lobby's save/leave prompt) | 0 → back to 6 |
| **7** | `DAT_002b84a0 = world`; `FUN_0013baa0`: loading screen 2000 (Jungle) / 1600 (others); `FUN_0013b780`: the park (`LAND`) | fade 20 frames; next message 3 → `FUN_0013ba10`, else `FUN_0013b9f0` |
| **3** | `FUN_0013b850(0)`: LOAD SAVE from inside a park | 2 → 7 |
| **8** | `FUN_0013b270`: post 1 and clear `DAT_002b62a0` | a fresh front end |
| **9** | `FUN_0013bb18`: the **ending movie** for the language | post 1 |
| other | logs `"Unknown inner loop state"` (`0x35ec38`) | |

Two flags ride along: `uVar6` (practice park) and `uVar5` (tutorial on) are set by the menu
result and handed to the park as `FUN_0013b780(world, map, tutorial, practice)`; the dead
`FUN_00230260("DisableTutorial")` check sits in front of every park entry.

### The boot order, in full

1. `FUN_0012d268` (from `0x2314e8`) decodes the embedded EA GAMES `SHPS` straight into the frame
   buffer — before the state thread exists and before any archive is open (see "The EA logo is
   shipped three times").
2. State 0xe → state 0: **LANGUAGE SELECT**, once per boot. Its Enter (`0x1555f8`) loads
   `languk/langger/langfre.ssh` from the pointer table at `0x2b7520` into `+0x58/+0x5c/+0x60`;
   Update (`0x155750`) moves `+0x50` with pad bits 1 and 2, clamps it to 0..2, plays sound `0xd6` on a
   move and `0x128` on confirm (`FUN_00181250(0)`, the logical-button-0 mask), latches `+0x64` and ends.
   `FUN_00155908` turns the choice into a language id through the 3-entry table at `0x360630` =
   **`{0, 3, 1}`** — the screen lists `English / Deutsch / Français` (names at `0x2b7510`), so
   English is 0, German is 3, French is 1 — and `FUN_001553d8(lang)` applies it: `DAT_00395430 =
   lang`, reload the text tables, and switch the font set when `lang == 8`. (The dead `SkipFrontEnd`
   branch would apply `FUN_001553d8(0)`, English.) `DAT_00392740 = 1` makes message 8 skip straight
   to state 1 for the rest of the session.
3. State 0xf: **MEMCARDCHECKSCREEN** (object 0x150 B, `FUN_001722d0`), then **`BFLOGO.MPC`**
   (`FUN_0013b980(0x35ed88, 0)`), then **LEGALSCREEN**, unless `SkipFrontEnd`.
   The memory-card check (Update `0x1723b8`, Draw `0x1725d0`) waits `+0x130 = 25` updates, then
   probes slot 0 and slot 1 (`FUN_00172a40` present?, `FUN_001729e8`, `FUN_0011ef68` room?). If
   either slot passes, `+0x13c = 1` and the scene ends with nothing drawn. Otherwise it formats
   text **583** (`There is no memory card …`) or **642** (`insufficient space …`) with
   `FUN_0011f0a8() >> 10` — the save size in KB — and shows it at (40,180) with two rows,
   **677 `Retry`** at y = 350 and **579 `OK`** at y = 390, toggled by up/down (`+0x134 & 1`) and
   confirmed with the raw pad bit `0x40`; Retry re-probes, OK carries on. So the boot is blocked
   only for as long as the player leaves a missing card unanswered.
4. **LEGALSCREEN** = the title screen. Enter (`0x159a78`) loads `mainback1.ssh`. Update
   (`0x159c08`): after 160 frames (`+0x30 > 0xa0`) it turns on `+0x38`, and Draw (`0x159ca8`) then
   prints text **877** `Press START button to Continue` at (250, 250) at 2× scale. START (pad bit
   `0x10` from `FUN_00230278(0)`) ends the scene. `FUN_00159b88` is the idle clock: while no button
   is held it accumulates `FUN_001c4920() >> 12` into `+0x34`, and at **27000** it sets `+0x3c` and
   the scene ends. `FUN_00159be0` returns `+0x3c`, so **timed out → attract with return-state 0xf**,
   START → the front end. (The same construction exists once more in `FUN_0013b0d0`, a copy with no
   caller.)
5. State 1: the **FRONT END** on `mainback2`.

The menu's own timeout uses the same clock with a limit of **0x707 = 1799** (`FUN_00139168`,
logging `"FRONTEND TIMEOUT"` at `0x35e8a0`), counted only while the **main page** is showing
(`+0x50 == DAT_002b61f0`), and zeroed by the page whenever a row is confirmed or the hover moves.

### ⭐ The clock those timeouts run on always steps by 4

`FUN_001c4920` returns `DAT_00397640`, which the scene update tick (`0x1c4aa8`) sets every update
to `FUN_0011e688(&DAT_00397638)` **clamped to `0x4000`**. `FUN_0011e688` returns `(now − last) ×
0x80`, where `now` is `DAT_002acab4`, a counter that the interrupt callback at `0x11e758` bumps
by **`0x2710` = 10000** per tick unless paused (`DAT_002acabc`). One tick between updates is
already 1,280,000, so the clamp saturates every time and `DAT_00397640 >> 12` is **always 4**.
The design reads as "elapsed time in 20.12 centiseconds, capped at 4 cs" — the cap is exactly one
25 Hz frame — so the intended timeouts are **18 s** for the menu (1800 cs) and **4.5 min** for the
title (27000 cs), and the loading `duration` is not time at all (next section). What is certain
is the update count: **450 updates** on the menu and **6750** on the title. Whether an update is
one 50 Hz field or one 25 Hz frame (`FUN_0013b050` pumps `FUN_00194628(1)` per update; not read)
halves or keeps those seconds; the port should treat them as 18 s and 270 s and check against the
console.

### ⭐⭐ The attract cycle: `FUN_0013bb98`

Hand-read from the disassembly (`0x13bb98..0x13bc58`):

```
FUN_0013b980(0x35ed88, 0)                   // BFLOGO.MPC
if (DAT_00342ed8) return 1                  // a button interrupted it
i = DAT_002b62b8                            // the attract counter, 0..4
FUN_0013b980(0x35ed50 + 8*i, 0)             // pair[i].story
if (!DAT_00342ed8) {
    FUN_0013b980(0x35ed50 + 8*i + 4, 0)     // pair[i].reel
    if (!DAT_00342ed8) result = 0
}
DAT_002b62b8 = (i + 1 == 5) ? 0 : i + 1
```

So one attract cycle is **Bullfrog logo → story → gameplay reel**, and the counter advances one
pair per cycle and wraps at 5: the sequence over successive idles is DINO+FE1, FRANK+FE2,
FLOWER+FE3, SPACEMAN+FE1, ALIEN+FE2. The counter only moves once the story movie has been
attempted — a press during the logo returns without touching it (Ghidra's read of the same
function agrees: `DAT_002b62b8` is incremented inside the `DAT_00342ed8 == 0` branch). It is reset
to 0 whenever the title screen times out into attract (`DAT_002b62b8 = 0` in `FUN_0013a998`), and
is left alone when the menu times out, so the reel keeps rotating while the console sits on the
menu. `DAT_00342ed8` is the movie player's "interrupted" flag (zeroed by `FUN_0023f7a0` before
each play, set by its pad callback on START or the `0x40` button); one such press aborts the whole
cycle.

⚠ The return value is dead: `FUN_0013be08` ignores it and posts `DAT_00392744` regardless. From
the title screen that means the loop is *title → BFLOGO → story → reel → memcard check → BFLOGO
→ title*: the Bullfrog logo really does play twice per revolution, once as the attract bumper and
once as the boot bumper. That is what the code says, not a guess about what was intended.

### The world intro on entering a park: `FUN_0013b3c8`

This is the state-6 body around the lobby. Without `SkipFrontEnd` it builds a `WorldMapSelector`
on the stack (0x1c30 B), runs it with `FUN_0013b050`, then reads the chosen slot's record:
`FUN_00217a30` returns the record byte `+0x0b` (the **map**, `this+0x7b*0x1c+0x97`) and
`FUN_00217a48` returns `+0x0a` (the **world**). ⭐ That settles two of the lobby doc's open
`0x1c`-byte-record fields: bytes 0x0a and 0x0b of each slot record are the world and map that slot
stands for. It closes the other three world archives and opens the chosen one (`FUN_00131380(db,
{2,4,3,5}[world], 0)`), and then — **only if the lobby's `+0x74` flag is 1 and `SkipFrontEnd` is
off** — plays the world's story movie: `FUN_0013b980(0x35ed50 + 8*world, 0)`. So DINO / FRANK /
FLOWER / SPACEMAN play as a park's intro, and the `+0x74` flag (which the lobby doc saw button 0xE
clear) is what distinguishes "entered a park" from "left". The lobby's return value handed back to
the thread is that same `+0x74`.

The (dead — see "config switches") `SkipFrontEnd` branch would bypass the lobby entirely:
`FUN_00230238()` / `FUN_00230248()` supply a world and map by name, compared against `HALLOWEEN` /
`FANTASY` / `SPACE` (else Jungle) and `MAP1` (else map 2) at `0x35ebd8..0x35ebf8`, and no movie
plays. In this build those two functions return the constants `"JUNGLE"` and `"MAP0"`.

### The FMV scene and the player

`FUN_0013b980(pair, flag)` is the only movie entry point (10 call sites: boot, world intro, ending,
attract). It builds an `FMV SCENE` on the stack, stores the name at `+0x30`, and calls
`FUN_00137fc8(scene, *pair, flag ^ 1)` — the **first** pointer of the pair only; the second is
read separately by the attract loop. `FUN_00137fc8`:

1. `DAT_002b6184 = flag`, waits for `DAT_00310c9c == 0` (the renderer idle), takes the DMAC
   (`REG_DMAC_CTRL`), `FUN_002250c0()`;
2. builds the path `"MOVIES/"` + name into a stack buffer (`builtin_strncpy … "MOVIES/"`);
3. `FUN_0023f7a0(path, sound_ctx, flag != 0)` — the player: zeroes a 0x9c-byte context at
   `0x39cef8`, points it at the GS display buffers (`DAT_002ef970/974`), installs the callbacks
   `FUN_0023f718`, `FUN_0023f760`, `FUN_0023f780` and the tables at `0x342ea8` / `0x342ec0`, sets a
   1 MB stream buffer (`0x100000`) and a 0x40000 one, sizes the picture `0x280 × 0x1aa` (640 × 426),
   sets mode word `0x80118c`, or `0x84118c` when the flag is set, clears `DAT_00342ed8`, logs
   `"-> playing movie %s"` (`0x370360`) and calls `FUN_0026af80(path, ctx)`, which returns when the
   movie ends or is interrupted. ⭐ **Interruption is `FUN_0023f718`**, the per-frame callback: it
   polls pad 0 (`FUN_00230278(0)`) and stops the movie, setting `DAT_00342ed8 = 1`, when
   `buttons & 0x50` — START (`0x10`, the bit the title screen waits for) or the `0x40` button. Any
   other button leaves a movie running. `FUN_0023f760` is just the allocator (`FUN_00298d88`);
4. resets the IPU (`REG_IPU_CTRL = 0x40000000`), gives the DMAC back, and re-primes the render
   pipeline (`FUN_0022a378(0x311160)`, `FUN_0022b0a0`).

Because playback is synchronous inside step 3, the FMV scene's own vtable is nearly empty: Enter
clears `+0x2c` and the pad (`FUN_001817c0(0)`); Update ends the scene on any new press
(`FUN_00181860(0)`) and sets `+0x2c = 1`; Draw (`0x137f18`) prints `"Playing FMV %s"` and
`"Press Any Key to Continue..."` at (200,150) / (130,250) — a debug placeholder that is only visible
if the scene is ever ticked without a movie running. Every caller passes flag 0, `FUN_0013b980`
inverts it (`flag ^ 1`), so `FUN_00137fc8` always stores `DAT_002b6184 = 1` and the player always
gets the `0x84118c` mode word; what the `0x40000` bit changes is open.

### The front end's hand-off: `FUN_0013b2a0` → a result code → the thread

`FUN_0013b2a0` opens archive 10, constructs the scene, runs it, and then converts the scene's
fields into one code:

| code | condition | thread does |
|---|---|---|
| 4 | `LoadGame` config set, or `+0x48` (Load Game chosen) | state 4 |
| 3 | `SkipFrontEnd`, or Main Game with tutorial off | state 6 — the lobby |
| 2 | Main Game with tutorial on | state 6 |
| 1 | Practice Park with tutorial off | state 0xd — all worlds open, straight into a park |
| 0 | Practice Park with tutorial on | state 0xd |
| 5 | `+0x4c` | state 8 — a fresh front end (`+0x4c` has no setter with a caller, so this looks dead) |
| 6 | `+0x44` — the idle timeout | state 0xb, then back to state 1 |

Practice vs Main comes from `FUN_00153410()` (reads `DAT_002b72a8`) and the tutorial from
`FUN_001c3940()` (reads `DAT_002e98b8`). Both are plain globals with named setters, and the
setters say which page decides what:

* `FUN_00153420(v)` writes the **practice-park flag**. Its three callers are the park entry
  `FUN_0013b780` (re-asserting the thread's flag), the laptop (`0x1c3fc0`), and **the New Game
  sub-menu's Draw at `0x16e110`** (page slot 3, `FUN_0016dea8`), which writes 0 when the
  `Main Game` clip finishes and 1 after the `Rollercoaster Test Park` clip, then ends the scene at
  `0x16e11c`. Choosing there is what makes the menu's result 0/1 instead of 2/3. The row itself is
  offered only when `FUN_00154378()` is non-zero: a count over every world × map × 4 of
  `FUN_00154328(world, map, i)` — an earned-per-park tally (golden tickets or keys, not verified).
* `FUN_001c3950(v)` writes the **tutorial flag**, from `FUN_0013a178`, which flips bit `0x40` of
  the options byte `*DAT_002aa720`, writes the flag, and swaps the row's label between 210
  `Tutorial On` and 843 `Tutorial Off` (`DAT_002b6290`). Its one caller is `FUN_00139df0`, the
  **laptop's** Game Options page (six rows: Music, SFX, Tutorial, Vibration → `FUN_0013a1f0`,
  Save Game → posts `0x30008`, Quit Current Game → posts `0xe0008`, i.e. back through the boot
  chain to a cold front end). The front end's own options page reaches the same flag through
  `FUN_001c3960` (read-xor-write of `DAT_002e98b8`). `Tutorial On/Off` is an options toggle, not
  a menu branch.

So the assignment `(practice != 0) → codes 1/0 → state 0xd` and `(tutorial != 0) → codes 2/0 →
tutorial flag 1` is not only the one consistent with `FUN_0013be08`'s switch; it is what the
setters' call sites say. The practice park is then always `FUN_00149678(scene, 0, 2)` — **Jungle,
map 2** — inside `FUN_0013b780`, while a Main Game park takes `(world, map)` from the lobby.
Options is not a result at all: it is page 1 of the same scene and returns to page 0 without
leaving it.

### ⭐ The loading screen, and why only the even kid frames shipped

`FUN_00161490(duration)` sets the loading screen up (guarded by `DAT_002b8490`, so it runs once
per load). It picks the world name from `DAT_002b84a0` — 1 `hallow`, 2 `fantasy`, 3 `space`, else
`jungle` — and the language suffix from `FUN_00155428()`: **language 3 → `_Ger`, language 1 →
`_Fr`, anything else → none**, then formats `%s1%s.ssh` (`jungle1_Fr.ssh`) and loads it from
`data/frontend/loading/` (`FUN_002351a8(dir, name, 3)`). Then the kids:

```c
for (i = 0; i < 16; i++) {
    sprintf(buf, "%skid%04d.ssh", world, i << 1);      // 0x361518: frames 0, 2, 4 … 30
    DAT_002b84a8[i] = FUN_002351a8("data/frontend/loading/<world>kid/", buf, 3);
}
```

⭐ The frame index is **`i << 1`**, sixteen times. That is the whole reason `/Loading/*kid/`
holds `0000..0030` in steps of two while `/Loading/4Steve/` holds all 32: the loader was changed
to take every other frame and the shipped folders were re-exported to match; the full-rate
export stayed behind in "4Steve". A port that wants the kid at full rate has the frames, but the
game never asked for them.

It then sets `DAT_002b8498 = 0xc000ffff` (the bar colour), zeroes the file counter
`DAT_002b3b30` and the vsync counter `DAT_002b848c`, and calls `FUN_00161380(duration)`, which
registers a vsync hook (`FUN_00225208(0x1610b8, 0)`) and creates a thread on `FUN_00161110` with a
4 KB stack at priority 8 (`"*** ERROR loading screen thread could not be started"`, `0x361460`),
passing `duration` as its argument. The picture is drawn by that thread while the main thread is
busy inside the archive reader:

* the vsync hook `0x1610b8` increments `DAT_002b848c` and, every **8th** vsync, wakes the thread;
* the thread (`FUN_00161110`, cached decompile) loops forever: kid frame =
  `DAT_002b84a8[(DAT_002b848c >> 1) & 15]`, progress = `DAT_002b3b30 / duration` clamped to 1.0,
  draw the kid quad, draw the bar as a quad whose right edge is `progress × 1.2 − 0.6`, flip
  (`FUN_0022b0a0`, `FUN_0022a378`), `SleepThread()`;
* **`DAT_002b3b30` counts files**: the archive reader adds 1 after each file it reads
  (`0x13186c`, `daddiu v1, v1, 1`, and the other `sd` sites in `FUN_00131xxx`).

⭐ So `duration` is the expected **file count** of the load, not a time: **800** files for the
lobby (`LOBBY.WAD` holds 1034), **2000** for a Jungle park, **1600** for any other park
(`FUN_0013baa0`, `movz a0, v1, s0`), and the bar simply reports files-so-far over that. The kid
advances one frame every two vsyncs by index, but the thread only redraws on the 8-vsync wake-ups,
so unless something else wakes it the visible frames are every fourth of the sixteen.

⚠ The `LOADING SCREEN` *scene* class is not what shows any of this: its runner `FUN_0013bc58`
(construct with `FUN_00161048(scene, duration)`, run, delete) has no caller, and its Update
(`0x161010`) would only poll `FUN_00161008()`. The three real callers of `FUN_00161490` are the
park-load path (`FUN_0013baa0`), state 6 in the thread, and **the park scene's own Enter
`FUN_00150e20`**.

The same `FUN_00155428()` language id (it returns `DAT_00395430`) drives the ending movie (1 →
`END_F`) and the Japanese font/x-offset checks (`== 8`), so the id space this build touches is
0 English, 1 French, 3 German, 5 Spanish (phantom), 8 Japanese — consistent with the language
select's `{0, 3, 1}` table.

### ⭐⭐ The config switches are compiled out: every `SkipFrontEnd` branch is dead

`FUN_00230260(name)` is what every "skip this?" test calls (21 call sites; the names passed are
`SkipFrontEnd` ×9, `LoadGame` ×2, `DisableTutorial` ×2, `DisableSciptAsserts` ×2, `DisableSound`,
`Upgrades`, `OpenPark`, `SpeedUp`, `ForceKidsToEnter`, and `FUN_00230258("LoadsOfKids")` next
door). In this executable it is two instructions:

```
230260: jr ra
230264: daddu v0, zero, zero        ; return 0
```

and so are its neighbours `0x230258`, `0x230268`, `0x230270`. The world/map suppliers for the
skip path are constants too: `FUN_00230238` returns `"JUNGLE"` (`0x36f788`) and `FUN_00230248`
returns `"MAP0"` (`0x36f790`). The development build read these from somewhere (a command line or
an ini); the shipping build kept every call and every string and stubbed the lookup. So on this
disc **no screen or movie is ever skipped, `LoadGame` never short-cuts the menu, and the tutorial
flag is never forced off** — every `SkipFrontEnd` branch quoted in this file is dead code, kept
here because it documents what the developers could switch off, not because a port needs it.

### `LOAD SAVE`

`FUN_0013b850(mode)` builds the scene on the stack (0x580 bytes: the scene, plus a
`FUN_001434f0` object, a `0x3697d0`/`0x3697a8`-vtabled object made by `FUN_00141ea0` +
`FUN_001dd2e0`, a `0x360e60`-vtabled one from `FUN_0015bb58`, and a text-wobble widget from
`FUN_001384d8`), calls `FUN_00162078(scene, mode)`, runs it and returns the scene's `+0x2c`. The
modes are 0 from inside a park (state 3), 1 from the menu (state 4), 2 from the lobby (state 5);
the thread reads results 0/1/2 as listed in the state table. Its internals are not read here.

## Still open

- **The update rate.** The timeouts are 450 and 6750 scene updates; whether `FUN_00194628(1)`
  waits one field (50 Hz) or one frame (25 Hz) decides 9 s / 135 s versus 18 s / 270 s. The
  centisecond reading above says 25 Hz; it is not measured.
- Which button `0x40` is in this pad mask (the movie-skip button besides START), and the exact
  bit assignment of `FUN_00230278`'s word.
- What the string at `0x331538` is that `FUN_002314e8` tests before `Running game...`
  (`FUN_0010eec0` is skipped when it is non-empty — a leftover command-line hook?).
- **What the menu clips animate.** The player (`0x105a50` … `0x105af0`) is read; the clip table at
  `0x2a6a80` (`FUN_0010f7f8` returns a clip's frame count) is not tied to any `.aps`, and nothing
  read here draws the `girl1` hostess. Also whether the "frontend" sound category means the
  clips are voiced.
- What `FUN_00154328(world, map, i)` counts, i.e. what unlocks `Rollercoaster Test Park`.
- The meaning of mode bit `0x40000` in the movie context and of `FUN_0023f780`.
- Why the thread runs the loading assets through `FUN_00161490` three different ways (state 6,
  `FUN_0013baa0`, and the park's own Enter) while the `LOADING SCREEN` scene class sits unused.
- What the hostess model `girl1_*.mps` is for — nothing on the language-select path names it
  (the pointer table `0x2b7530` that lists `girl1_uk/ger`, `gir1_fre` has no code reference found).
