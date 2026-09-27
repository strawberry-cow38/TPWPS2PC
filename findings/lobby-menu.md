# The lobby menu — `WorldMapSelector`

What it is, then how it works and why, read out of `SLES_500.32` and the archives. Every address
here is in the executable's own space (vaddr = file offset + 0xFF000).

## WHAT

### ⭐⭐ It is a 3D scene, not a 2D menu — and none of it is in `MENUS.WAD`

All **29** `.sce` layout files are named `main_*` and every one of them is an in-park **laptop**
screen. There is no lobby scene file. The lobby's UI is not authored as a menu layout at all; it
is a room with eight models standing in it.

### The archives

`FUN_00131ec8` registers the game's **16** archives through `FUN_00138400(slot, name)`, and
`Data\Lobby.wad` is **slot 0**:

| 0 Lobby | 1 Data | 2 Jungle | 3 Fantasy | 4 Hallow | 5 Space | 6 Lips | 7 Ui |
|---|---|---|---|---|---|---|---|
| **8 Menus** | **9 Particle** | **10 Frontend** | **11 Icons** | **12 jrse** | **13 frse** | **14 hrse** | **15 srse** |

Each archive's struct is `0x210` bytes and they are contiguous from `0x2b3b68`.

`LOBBY.WAD` holds **1034 files**: 10 `.mps` + 10 `.aps` + 507 `.ssh`/`.tga` texture pairs. The ten
models are `base` (the room) and one per park, plus a `/Backup` copy of `base`.

### ⭐⭐⭐ The eight parks, in the game's own order

`FUN_00216f90` loads `base` and then loops **exactly eight times** over the pointer table at
`0x2ef7d8`, calling `FUN_001f8678("data\lobby\", <name>, 0x10, id, 1, 0, 0)`:

| lobby slot | load id | model |
|---|---|---|
| 0 | 2 | `jungle1` |
| 1 | 3 | `hallow1` |
| 2 | 4 | `fantasy1` |
| 3 | 5 | `jungle2` |
| 4 | 6 | `space1` |
| 5 | 7 | `hallow2` |
| 6 | 8 | `fantasy2` |
| 7 | 9 | `space2` |

`base` itself is loaded first with id 1 and its handle kept at `this+0x2c`.

⚠⚠ **CORRECTION, same day: this is the MODEL LOAD order and nothing else.** I first wrote that
it "is the game's park progression". It is not -- it is only the order the eight `.mps` files are
read in. The lobby's own slots are ordered differently, grouped by world (see **The slot records**
below). Two orderings exist, the game converts between them with the table at `0x36dcc0`, and
treating either as the other is the easiest mistake to make here -- I made it.

### The class

`WorldMapSelector`, named by its own constructor trace `FUN_00107e48(0x36dbb0)` — the string
`'WorldMapSelector::WorldMapSelector()'` at `0x36dbb0`, with the destructor's at `0x36dbd8`. Its
display name is **"WORLD MAP"** (`0x36dba0`), returned by a frameless leaf at `0x2179b8`.

⚠ That leaf is invisible to the usual "scan back for `addiu sp, sp, -N`" function-start heuristic,
which attributes it to `0x2178a0` instead. Frameless leaves need the vtable to find them.

The four world names sit together at `0x36dc48`: `HALLOWEEN`, `JUNGLE`, `FANTASY`, `SPACE`.

### Vtable and object layout

Constructor `0x217a60` writes the vtable pointer **at `this+0x28`**, pointing at `0x36dc70`.
Entries are the usual `{short thisAdjust, u32 fnptr}` 8-byte pairs, so slot *i*'s pointer is at
`vt + 8i + 4`; slot 0 is null.

| slot | address | what it does |
|---|---|---|
| 1 | `0x216f90` | **Enter**: opens the archives, loads `base` + the 8 parks, seats and starts them |
| 2 | `0x2178a0` | **Leave**: frees all 8 park objects and handles |
| 3 | `0x2183a8` | — |
| 4 | `0x217e20` | **Update**: reads the pad and moves the selection |
| 5 | `0x2179b0` | — (frameless) |
| 6 | `0x2179c8` | — |
| 7 | `0x217ae0` | — |
| 8 | `0x2179b8` | **Name** → `"WORLD MAP"` |

Fields established by the Enter and Leave pair:

| offset | meaning |
|---|---|
| `+0x28` | vtable pointer |
| `+0x2c` | the `base` model handle |
| `+0x30 .. +0x4f` | the 8 park model handles |
| `+0x50` | the scene/actor context (`FUN_00230a98()`) |
| `+0x54 .. +0x73` | the 8 park objects |
| `+0x78`, `+0x82` | state flags that gate input |
| `+0x7b` | **the selected slot** (a byte, 0..7) |
| `+0x8c ..` | 8 records of `0x1C` bytes, one per slot |

The constructor also zeroes three runs of **four** words (`0x338`, `0x348`, `0x368`) — four being
the world count.

## HOW / WHY

### ⭐⭐ Navigation is an authored adjacency table, not geometry

`FUN_00217e20` takes the current slot's record and reads a **neighbour byte per direction**:

```c
rec = this + this->slot(0x7b) * 0x1c + 0x8c;
pad = FUN_00181700(0);
if      (pad & 1) next = rec[0x0e];
else if (pad & 2) next = rec[0x0f];
else if (pad & 4) next = rec[0x10];
else if (pad & 8) next = rec[0x11];
FUN_002186b0(this, next);
```

⭐ **Why it matters for a port:** moving between parks is a lookup, so the lobby's feel cannot be
reproduced by comparing model positions and picking the nearest in the pressed direction. Four
bytes per slot decide it, and they are free to be asymmetric or to skip a park entirely.

Buttons go through `FUN_00181250(n)`, which turns a logical button into its pad mask:

| logical | action |
|---|---|
| 0 | `FUN_002187f0(this)` |
| 1 | `FUN_00218880(this)` |
| 2 | `FUN_00218f78(this, 7)` |
| 0xE | `FUN_00218f78(this, 8)`, then `this+0x74 = 0` and `this+0x82 = 1` (latches "leaving") |

### ⭐⭐ Each park model is SEATED ON A NODE OF `base`, then scaled, then animated

Per slot, `FUN_00216f90`:

1. makes an object and binds the loaded model with kind `0xe`;
2. if the model carries a node with flag `0x40`, finds a node in the scene's table with
   **`FUN_001f1f78(table, 0x400, slot + 1)`** — key is the slot's own number plus one — and copies
   that node's matrix in as the park's transform (two components scaled by `0.1`);
3. scales it by `UNK_0036dce0[slot]`, an array of **eight floats that are all `0.7`**;
4. walks the model's meshes testing each name against `'ripples'` (`0x36db58`) and clears bit
   `0x08000000` on every mesh that does not match;
5. starts it with `(**(code **)(vt + 0x5c))(1.0f, obj, 5, 0, 0, 1)`.

⭐⭐ **Step 5 is the same call as the laptop's model window**, which independently decoded to
**APS section 5 at rate 1.0** (see `findings/laptop-screens.md`). Two unrelated call sites — a
menu widget and the lobby — asking for the identical thing is the strongest evidence yet that
section 5 is the port's default "show this thing doing its idle".

⭐ `FUN_001f1f78(obj, mask, key)` is a general **find-record-by-key-under-a-flag-mask**: it walks
`count` records of `0x14` bytes (count `u16` at `+0x36`, table pointer at `+0x74`), matching
`rec[1] == key && (rec[0] & mask)`, and returns the index. The lobby uses it twice with different
masks — `0x400` for the seating node above, and `0x1000` in `FUN_00217b48` keyed by the table at
`0x36dcc0`.

### ⭐⭐ The slot records -- `0x36dd00`, **nineteen** of them

`FUN_00216f90` copies this table into the object at `this+0x8c`, `0x1C` bytes a record, from
`0x36dd00` to `0x36df14`: **19 records**, not 8. The first eight are the parks (kind 0) and the
other eleven are empty kind-1 fillers -- and `base.mps` carries fitting ids 9..19 for exactly
those eleven, which confirms the count from the data side.

| offset | meaning |
|---|---|
| +0x00 | kind (0 = a park, 1 = empty filler) |
| +0x02, +0x04 | two `u16`, unread |
| +0x06 | **world**, 0..3 |
| +0x07 | **park within world**, 0..1 |
| +0x08 | `u16` id, 15..22 |
| +0x0A | `u16` **text id** -- the `STR_MAP_*` name |
| +0x0C..+0x0F | **the four neighbour records**; `20` means none |
| +0x10..+0x17 | four `u16` yaws, one per direction, zero where there is no neighbour |

⭐ **Record order is grouped BY WORLD**, and three independent sources say so: the `+0x06`/`+0x07`
pair, the `STR_MAP_*` text ids, and the key table below.

| record | world | park | text id | name | model |
|---|---|---|---|---|---|
| 0 | 0 | 0 | 1000 | Lost Kingdom: Prehistoric World | `jungle1` |
| 1 | 0 | 1 | 1001 | Lost Kingdom: The Park That Time Forgot | `jungle2` |
| 2 | 1 | 0 | 392 | Halloween World: Realm of Terror | `hallow1` |
| 3 | 1 | 1 | 393 | Halloween World: Ghost World | `hallow2` |
| 4 | 2 | 0 | 796 | Wonder Land: Land of Dreams | `fantasy1` |
| 5 | 2 | 1 | 797 | Wonder Land: Enchanted Island | `fantasy2` |
| 6 | 3 | 0 | 426 | Space Zone: The Final Frontier | `space1` |
| 7 | 3 | 1 | 427 | Space Zone: Star Park | `space2` |

World 0 is Lost Kingdom (the jungle), 1 Halloween, 2 Wonder Land (fantasy), 3 Space Zone.

### The record → model table at `0x36dcc0`

Indexed by the selected slot (`this+0x7b`), used with mask `0x1000`:

| slot | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---|---|---|---|---|---|---|---|
| model | jungle1 | hallow1 | fantasy1 | jungle2 | space1 | hallow2 | fantasy2 | space2 |
| key | 1 | 4 | 2 | 6 | 3 | 7 | 5 | 8 |

⭐⭐ **SOLVED: `table[record] == modelIndex + 1`, checked on all eight.** It converts the record
order (by world) into the model load order -- and the same number is also the fitting id the park
is seated on, so one value answers both "which model" and "which seat".

⚠⚠ **These are NOT text ids, and it is worth saying so.** Read as indices into
`/Text/translations/eur/eng.dat` they resolve to `Salt`, `Welcome message`, `Bank Balance`,
`Medium Tree`, `3 Years`, `Details` — plausible-looking UI words, every one of them wrong. The
lookup they are actually passed to is a record search over `0x14`-byte entries, not the text
table. A string table will happily answer any index you give it.

## Still open

- Vtable slots 3, 5, 6, 7 are not yet read.
- Where the `0x1C`-byte slot records (and their neighbour bytes) are filled from.
- What `FUN_002187f0` / `FUN_00218880` / `FUN_00218f78` do — the confirm, back and
  leave actions.
- Whether the three 4-word runs zeroed by the constructor are per-world state.


## ⭐⭐ Implemented -- and the shared-code bug it exposed

`game/Viewer.Lobby.cs` + `core/TPW.PS2.Data/LobbySlots.cs` build the scene: `base` plus the eight
parks, each seated on the `0x400` fitting whose id is its model index + 1, scaled 0.7, playing APS
section 5, with the authored neighbour table on the arrows and the record's `STR_MAP_*` name for
the selection. `--lobby` opens it; `--lobby-overview` frames all eight.

⚠⚠ **`Model.Fitting.Node` was wrong, and the lobby is where it shows.** It read
`Meshes.Count + i`; the consumer reads **`u16 @0x34 + i`**, a separate header field. The two are
equal often and not always -- across JUNGLE's 88 models with fittings they agree on 61 and differ
on 27, and on `base.mps` it is 2 against a mesh count of 13. Under the old reading 3 of 8 parks
seated and 5 did not, and **the 3 that worked were right by coincidence**.

⭐ Why it survived: it was validated on `monkey.mps`, whose mesh count is 9 and whose `0x34` is
also 9 -- a case where both readings give the identical answer. A rule confirmed only where it
agrees with its rival has not been tested against it. Fixed centrally; the 61 agreeing models do
not move.

⚠ **Two scales, both load-bearing.** Every seat fitting carries a basis scale of exactly `0.100`
-- they are markers drawn small -- so a park must take the seat's POSITION and FACING but not its
size, or it draws at 0.07 and the overview shows eight specks. And `base.mps` is authored ten
times the size its own root draws it at: at 1x the island is 113 units across while
`GameCamera.MinBehind` is **384**, so no camera distance frames it and every render came back as
open water.

## Still open

- Vtable slots 3, 5, 6, 7, and `FUN_002187f0` / `FUN_00218880` / `FUN_00218f78` (confirm, back, leave).
- The two `u16` at record `+0x02` / `+0x04`.
- `base`'s bridges draw clustered rather than spanning between the islands; the park seating is
  right, that geometry's placement is not yet.
- Choosing a park does not load it -- the lobby is a scene and a selector, not yet a hand-off.
