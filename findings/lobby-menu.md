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

⭐ That slot order **is** the game's park progression. It is a table, not an arrangement derived
from the worlds: the worlds interleave (jungle, hallow, fantasy, jungle, space, hallow, fantasy,
space) and no rule over world names produces it.

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

### The slot → key table at `0x36dcc0`

Indexed by the selected slot (`this+0x7b`), used with mask `0x1000`:

| slot | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---|---|---|---|---|---|---|---|
| model | jungle1 | hallow1 | fantasy1 | jungle2 | space1 | hallow2 | fantasy2 | space2 |
| key | 1 | 4 | 2 | 6 | 3 | 7 | 5 | 8 |

⭐ Sorted by key, the **first** parks take 1–4 (jungle1, fantasy1, space1, hallow1) and the
**second** parks take 5–8 (fantasy2, jungle2, hallow2, space2). So the key is a park identity in a
different ordering from the lobby's own, and the table exists to convert between the two.

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
