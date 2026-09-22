# The park grid — where the plot's extent is authored, and where it isn't

**2026-09-22.** Worked jointly with `cow tools`, who found the `heightfield` marker mesh; this file
records the executable side, the independent verification of that mesh, and two corrections.

## The `.lnd` path is dead code on the PS2

The engine's module map is in the binary as assert paths — `TPHField.cpp`, `TPHMap.cpp`,
`TPMesh.cpp`, `TPInstance.cpp`, `TPHoarding.cpp`, `TPEmbed.cpp`, `TPRide.cpp`, `Land.cpp`,
`M3D2Manip.cpp` — so there is a heightfield subsystem, named as such. Its strings sit in one
cluster at `0x36a850`:

    "%sbase.lnd"  "base.lnd"  "%s\Terrain"  "base.md2"  "Base"  "TestBase"
    "Could not load heightfield"   Source/Core/Code/TPHField.cpp

All seven are referenced from **one function, `0x001f3248`**, and nothing else references them.
Disassembled (R5900, so `LQ`/`SQ` hand-decoded — a stock MIPS32 disassembler stops dead at the
first quadword spill in the prologue):

```
0x1f3248  prologue
0x1f325c  beqz  $a0 -> 0x1f3288        two path builders, 0x146f80 / 0x147090
0x1f326c  addiu $s0, "%s\Terrain"
0x1f32c4  addiu $a0, "base.md2"   ; jal 0x1f79d0    load the model
0x1f32e0  addiu $a0, "Base"       ; jal 0x1f7ed8    look up a mesh by name
0x1f3300  bnez  -> success
0x1f3310  addiu $a0, "TestBase"   ; jal 0x1f7ed8    retry with the fallback name
0x1f3330  bnez  -> success
0x1f3340  both globals = 0, return 0
0x1f33e0  addiu $a0, "Could not load heightfield"   ; jal 0x1f3720
```

So the intended mechanism is: build `<world>\Terrain`, load `base.md2`, and pull a mesh named
**`Base`**, or failing that **`TestBase`**, out of it.

No `.lnd` ships: a raw scan of the whole 621,734,736-byte image finds the string `base.lnd` exactly
once, as the format string inside `SLES_500.32` itself. And the call graph agrees — **the `.lnd`
path-builder at `0x1f31f8` has zero callers.** That half is dead, proven two ways.

### ⚠ Correction 4 — but the LOADER is not dead, and I said it was

An earlier version of this file called the whole thing "the PC-era path, left in the PS2 build".
**That overstated it.** `0x1f3248` has one caller, `0x1f66f4`, so it is reached. And a `base` model
does ship: **`LOBBY.WAD/base.mps`**, whose 13 meshes include a `heightfield` marker — `base.md2` and
`base.mps` are the same family under two extensions, which the engine's own dispatch strings
(`sam / mps / aps / md2 / hmp`, adjacent in `.rodata`) already say.

I reached "dead" from the absence of files named exactly `base.md2` / `Base` / `TestBase`, which is
a search for spellings rather than for the thing. What remains genuinely unexplained is the mesh
lookup: the loader asks for `Base` and then `TestBase`, and `LOBBY.WAD/base.mps` contains neither —
its marker is named `heightfield`. Both lookups pass a constant `8` in `$a1`, which is not the
length of `"Base"`, so that argument is a type or a cap rather than a string length and the lookup
at `0x1f7ed8` has not been read yet.

**The `heightfield` marker is a deliberate convention, not an artefact: it appears in exactly 10 of
the disc's 496 `.mps` files** — the eight `terrain_*.mps`, plus `LOBBY.WAD/base.mps` and its backup.

## What IS authored: the `heightfield` marker mesh

Every world's `terrain_1.mps` and `terrain_2.mps` carries a mesh named **`heightfield`** with
**zero vertices and zero faces** — no geometry at all — but with its bounding box filled in. The
box is the plot.

Verified independently against all eight files (`+0x70` min, `+0x80` max, each a `float[4]`):

| world | `terrain_1.mps` | `terrain_2.mps` | |
|---|---:|---:|---|
| JUNGLE | **64 × 76** | **64 × 76** | agree |
| FANTASY | **80 × 60** | **76 × 62** | differ |
| HALLOW | **96 × 52** | **88 × 56** | differ |
| SPACE | **96 × 54** | **72 × 62** | differ |

At the root scale of `0.100` established in `findings/grid.md`, and 1 model unit = 1 footprint cell,
those are the grids.

### ⚠ Correction 3 — the two terrain files are DIFFERENT PARKS, and I claimed otherwise

An earlier version of this file said "`terrain_1` and `terrain_2` agree exactly within each world".
**That is false in three worlds out of four**, and `cow tools` caught it.

The mechanism is worth more than the fact. I compared the two files and got agreement — but the
run that produced that comparison printed **only the Y fields**, because I had written it to test
whether Y was constant. Y *is* constant, so the two files agreed trivially, and I read that
agreement as a property of the files rather than of the field I happened to be looking at. The X
and Z were never compared at all. **I generalised a constant's agreement into a claim about the
data, in the same breath as correcting someone else for reading meaning into that same constant.**

JUNGLE genuinely does agree, which is what made it look like a rule — and JUNGLE is the world both
of us test on. **Plot size is a property of the terrain FILE, not of the world.** Two terrain files
per world means two different parks, presumably the two difficulty or campaign variants.

### ⚠ Correction 1 — the box carries an exact 0.4% pad, so divide, don't round

The minimum is **not** zero and the maximum is **not** the extent. The exporter inflates the box by
a fixed ratio: `min = −0.001 × extent`, `max = 1.003 × extent`. So

    cells = (max − min) / 1.004 / 10

and that lands on **exact integers on every axis of every world** — 640.0000, 760.0000, 800.0000,
600.0000, 960.0000, 520.0000, 960.0000, 540.0000, to four decimal places. Rounding or flooring the
raw numbers happens to work on the maxima and **fails on the minima**, where `−0.064` floors to −1
rather than 0.

This same inflation shows elsewhere in the file and is what it looks like when you have geometry to
check against: `EMBANKMENT` stores ±200.30 where its own vertices reach ±199.50.

### ⚠ Correction 2 — the Y range is a hardcoded constant, not the elevation

It is tempting to read `Y 0 … 2.01` as the park's elevation envelope. It is not data.

**The Y minimum and maximum are bit-identical in all eight terrain files across all four worlds** —
`4660acbc` and `037da041`, `−0.021042000502347946` and `20.0610408782959`. Applying the same
`/1.004` gives exactly **2.0**. It is a fixed two-cell ceiling the exporter writes into every world,
and it carries **zero per-world information**. Nothing about how high JUNGLE's volcano is can be
recovered from it.

A number that comes out identical everywhere is a constant, not a measurement — and the check that
separates the two is cheap: compare the raw bits across every instance before reading meaning into
the value.

## The call graph, and what `Base` / `TestBase` actually are

Traced 2026-09-22, after the two retractions above. Everything here is from the call graph and the
disassembly, so it is checkable without running anything.

* **`0x1f31f8`, the `.lnd` path-builder — 0 callers.** Dead, and that is now proven by the call
  graph as well as by the file's absence.
* **`0x1f3248`, the loader — exactly 1 caller, `0x1f66f4`, and it passes `$a0 = 0`.** That zero
  matters: the loader branches on it at `0x1f325c` and the zero path takes the *second* of two path
  builders (`0x147090`, not `0x146f80`) to fill `"%s\Terrain"`.
* **`0x1f7ed8` is not a mesh lookup.** I called it one; it is a **load-by-name**. It builds
  `"%s%s"` from a prefix and the name, then calls `strchr(path, '.')` (`$a1 = 0x2e`) to see whether
  the name already carries an extension. So `"Base"` and `"TestBase"` are **model stems**, not the
  names of meshes inside `base.md2` — and `"base.md2"` having the stem `base` is why the lookup
  asks for `Base`. The constant `8` in `$a1` is a flags word, tested elsewhere as `& 0x200`.
* The result lands in `hf_obj` (`0x2ea848`); `hf_a` (`0x2ea840`) holds the live heightfield pointer,
  written by the setter at `0x1f6858` (called from `0x149a10`) and zeroed by `0x1f6868` (from
  `0x14ee00`).

### Both path builders, read rather than guessed

* **`0x147090`** — the one actually used. Calls `0x14e170` for the current world index and returns
  one of `"data\jungle\"`, `"data\hallow\"`, `"data\fantasy\"`, `"data\space\"`. So `%s`
  **is** the live world directory, and the target is `data\<world>\Terrain\base.md2`.
* **`0x146f80`** — the `$a0 != 0` branch, and unreachable, since the only caller passes 0. It
  returns a hardcoded `"Data\Levels\Jungle\"`, with `"Jungle"` and `"Data\"` as its neighbours.
  A development stub, and its path shape does not match this disc's layout at all.

**So the loader's target resolves to `data\<world>\Terrain\base.md2`, and no world has one.** All
four `/terrain/` directories hold `terrain_1.mps` and `terrain_2.mps` and nothing named `base`; the
disc's only `base.mps` is at the root of `LOBBY.WAD`, which is not one of the four world dirs and
has no `Terrain` subdirectory either. The stem fallbacks `Base` and `TestBase` resolve into the same
missing directory.

The precise statement, then — replacing both the "dead code" overstatement and the retraction that
followed it — is: **the loader is reached, its target does not exist for any world, and so this path
cannot be what populates a park's heightfield.** What does exist is the `heightfield` marker mesh
inside `terrain_1.mps` / `terrain_2.mps`, which something else must be reading, because the loader
above never opens those files.

### For a live-RAM session

The cheapest handle is not a breakpoint on the loader: **read the pointer at `0x2ea840`** and follow
it. The grids are already known (64×76, 80×60, 96×52, 96×54 depending on the terrain *file*), so the
buffer can be confirmed by its shape rather than hunted for blind, and a raise-one-tile diff then
gives stride, element size and encoding against an input you chose.

## What is still open

The marker gives the **extent and the resolution**. It does not give the **per-cell heights**, and
no file on the disc obviously does. Two candidates remain, neither confirmed:

* the field is rasterised from the terrain mesh at load, in which case `EMBANKMENT`, `VOLCANO` and
  `newcliff12` — which are terrain, not props — are the source and the marker is what they are
  rasterised *into*;
* the field starts flat and every elevation in a fresh park is authored some other way.

`Load_ReadLand` / `Save_WriteLand()` confirm the field round-trips through saves, which settles how
it persists and not where it starts.

## The runtime structure is a NODE POOL, not a flat grid

Found 2026-09-22 after the loader trace above, and it **supersedes my own advice to scan a savestate
for a contiguous grid-shaped array.**

The real heightfield module is not at `0x1f3xxx` at all — that was the model loader. It lives at
**`0x222xxx`–`0x225xxx`**, and its own error strings describe the shape:

```
 *** FATAL ERROR heightfield::initnodespace - couldnt allocate memory %08x-%08x
 *** FATAL ERROR heightfield:allocbuffer - out of nodebuffers
 *** FATAL ERROR heightfield::updatenodes, too much data used by node %d
 *** WARNING heightfield:init - more heightfield quadbuffers potentially needed (%d)
 *** ERROR mesh passed to mapwho lacks a heightfield***
 *** mapwho::renderheightfield failed
```

`heightfield::initnodespace` is **`0x222c90`**, and it is short enough to read whole:

```
lui/ori $a1 = 0x000B3C40          736,320 bytes
jal 0x17a0e0                      one allocation of that size
sw  $v0, 0x184($s0)               -> the pool pointer
jal 0x298d88, $a0 = 0xf0          a second allocation, 240 bytes
sw  $v0, 0x188($s0)               -> the node table
loop i = 0..29:                   sltiu $a1, $a2, 0x1e
    table[i] = pool + i*0x5FE0    8 bytes per table slot
```

**30 node buffers of `0x5FE0` = 24,544 bytes**, and 30 × 24,544 = 736,320 = `0x000B3C40` exactly,
with the 240-byte table being 30 × 8. The arithmetic closes on itself, so the read is right.

### What that means for a RAM search

* The heightfield object carries **the pool at `+0x184` and the node table at `+0x188`**. Those are
  better handles than any shape search.
* 24,544 does not divide by any of the grid sizes (24,544 / 4,864 = 5.046), so **the buffers are a
  generic pool, not one-buffer-per-row or one-per-park**. A contiguous run of exactly 4,864 values
  may not exist anywhere.
* ⚠ I told `cow tools` to scan a savestate for a buffer of 4,864 / 4,800 / … entries. **That advice
  was given before reading this and should not be the primary plan.** It is still worth doing as a
  cheap secondary — a grid may well sit inside one of the 30 buffers — but the first move is the
  pointer at `0x2ea840`, then `+0x184` / `+0x188` off the object it points at.
* The inference that this is a quadtree rather than a flat field rests on the words `nodespace`,
  `nodebuffers`, `quadbuffers` and `updatenodes` in the engine's own errors. The allocation numbers
  are read; the tree shape is inferred and should be confirmed against the live structure.

`mapwho` at `0x225b70` is the other half — "mesh passed to mapwho lacks a heightfield" says the
field hangs off a **mesh**, which is what ties it back to the `heightfield` marker inside
`terrain_1.mps` / `terrain_2.mps`. **Confirmed by `strawberry_cow` from domain knowledge: the
terrain files are one per park, two parks per theme** — which is exactly why their grids differ.

---

# ⭐⭐ SOLVED — the per-cell heightfield ships on the disc

**2026-09-22.** `cow tools` found the live field in a savestate at the global `0x2ea83c`. Reading the
code that fills it shows where it comes from: **it is a verbatim copy of a block inside
`terrain_N.mps`.** The data was authored, on the disc, in every park, the whole time.

## The builder — `0x1f3418(model, &globalSlot)`

Called from the loader at `0x1f3384` with `$a0` = the loaded terrain model (`0x2ea840`) and `$a1` =
`0x2ea83c`. It is short:

```
$s0 = model[0x44]                      the source heightfield struct
if (!model || !$s0) { *global = 0; return; }
$a0 = s0[0x0c]        NX
$v0 = s0[0x10]        NZ
mult $a0, $v0                          NX * NZ
$a0 = (NX*NZ) << 1                     TWO BYTES PER CELL
$a0 = $a0 + 0x30                       plus a 0x30 header
$s1 = alloc($a0)                       tagged "TPHField.cpp", line 0x134 = 308
   ldl/ldr + sdl/sdr x6                copy the 0x30 header s0 -> s1
   $a0 = $s1 + 0x30                    the cell array
   s1[0x24] = $a0                      header's own pointer to its cells
   $a1 = s0[0x24]                      the SOURCE cell array
   if ($a1) memcpy($a0, $a1, (NX*NZ)<<1)
*global = $s1
```

Nothing is computed, rasterised or derived. **The runtime heightfield is `memcpy` of a block that
ships in the model file**, with a 0x30-byte header copied in front of it.

## Where it is in the file

`M3D2 header +0x44` → a struct, laid out the same as the runtime header:

| offset | meaning |
|---|---|
| `+0x0c` | `u32 NX` |
| `+0x10` | `u32 NZ` |
| `+0x18` | `f32` — 2.0 in every file |
| `+0x24` | pointer to the cells, always `struct + 0x30` |
| `+0x30` | `NX * NZ` cells, **2 bytes each** |

Read out of all eight terrain files, and **every grid matches the one predicted independently from
the `heightfield` marker's AABB — 8 of 8**:

| world | file | `+0x44` | NX × NZ | cells | bytes |
|---|---|---:|---|---:|---:|
| JUNGLE | terrain_1 | `0xa9154` | 64 × 76 | 4,864 | 9,728 |
| JUNGLE | terrain_2 | `0xafce4` | 64 × 76 | 4,864 | 9,728 |
| FANTASY | terrain_1 | `0xb2a90` | 80 × 60 | 4,800 | 9,600 |
| FANTASY | terrain_2 | `0xa3464` | 76 × 62 | 4,712 | 9,424 |
| HALLOW | terrain_1 | `0x94924` | 96 × 52 | 4,992 | 9,984 |
| HALLOW | terrain_2 | `0x8f130` | 88 × 56 | 4,928 | 9,856 |
| SPACE | terrain_1 | `0x955f4` | 96 × 54 | 5,184 | 10,368 |
| SPACE | terrain_2 | `0xa4400` | 72 × 62 | 4,464 | 8,928 |

Two grids arrived at by different routes — an AABB on a zero-geometry marker mesh, and a `u32` pair
in an unrelated struct — agreeing on all eight files. That is the check.

## ⚠ The encoding is NOT settled, and JUNGLE is misleading about it

`cow tools` reported `byte0 & 0x3F` taking only the values 0, 1 and 2. **That is true of JUNGLE and
of no other world.** Histograms of `byte0 & 0x3F` across all eight:

    JUNGLE  t1   {0:3104, 1:1477, 2:283}
    JUNGLE  t2   {0:3090, 1:1550, 2:224}
    FANTASY t1   {1:871, 32:2374, 34:1546, 35:1, 40:2, 42:5, 50:1}
    HALLOW  t1   {1:1085, 2:2, 4:29, 6:9, 8:2441, 9:1, 10:1325, 16:16, 18:12, 32:18, ...}
    SPACE   t1   {0:2405, 1:1116, 2:1381, 4:5, 6:1, 10:2, 16:5, 32:178, 34:89, 48:2}

So `0x3F` is not a height field — bits `0x08`, `0x10`, `0x20` are in use, heavily in FANTASY and
HALLOW. The low bits look like height and the rest like flags, but **that is a guess and it is
exactly the guess JUNGLE invites**, because JUNGLE is the one park where every upper bit happens to
be clear. It is the third time today that JUNGLE, the world we both test on, has been the degenerate
case that made a wrong rule look right.

The honest state: **location, extent and element size are proven; the bit layout inside the two
bytes is not.** Nor is `byte1`, which `cow tools` also left open.

## What this retires

No emulator is needed to get the terrain. The savestate confirmed the runtime side and was worth
doing — it is what pointed at `0x2ea83c`, one word off the global I had handed over — but the port
can read all eight parks' heights straight off the disc with `m3d2` plus `+0x44`.

## The cell encoding, from the engine's own accessor

`0x166100` is the function that reads and repaints cells. It takes the same struct — `lw $t3,
0x44($a0)` — so it is unambiguously about this data, and it settles three things by mask rather
than by inference.

**Indexing is row-major, two bytes per cell.**

```
mult  $ac2, $t5, $v1        z * NX
addu  $a2,  $v0, $t6        + x
sll   $a2,  $a2, 1          * 2
lw    $a0,  0x24($t3)       + cells base
```

so `offset = (z * NX + x) * 2`, with the bounds checks right above it comparing `x` against
`[+0x0c]` and `z` against `[+0x10]`. No guessing required.

**`byte0`'s low two bits are the height; bits 2–5 are not.** The engine's own read-modify-write is:

```
andi  $v0, $v0, 0xc3        keep 0x80, 0x40, 0x02, 0x01 -- CLEAR bits 2..5
andi  $v0, $v0, 0xfe        clear bit 0x01
ori   $v0, $v0, 0x80        set   0x80
ori   $v0, $v0, 0x81        set   0x80 and 0x01
```

`0xC3` is `11000011`. **The four bits of `0x3C` are wiped and repainted by the engine**, which is
exactly why masking with `0x3F` produced those wild per-world histograms and why JUNGLE — where they
happen to be clear — looked like a clean 0/1/2. Measured on the disc, `byte0 & 0x03` is `{0,1,2}` in
seven parks and `{0,1,2,3}` in FANTASY `terrain_1`. `0x80` is a flag the engine sets in three
separate places; `0x40` survives the mask and is unexplained.

**`byte1` is written from a small table**, not read as one:

```
lw    $v1, 0x2c($t3)        table base
addu  $v1, $t0, $v1
lbu   $v0, -1($v1)          table[arg - 1]
addu  $v0, $v0, $t1         + arg
sb    $v0, 1($a0)           -> byte1
```

and `lbu $v0, 0x28($t3)` bounds that argument, so `+0x28` is the table's count. On disc `+0x28` is
**2** in all eight files and `+0x2c` points at a 2-byte array that ends **exactly at EOF** in all
eight — a clean structural check that the pointer is read right. `byte1`'s own meaning stays open;
its distinct values per park are small sets (6–8 values, e.g. JUNGLE t1 `{0,24,55,56,57,58,59,60}`).

### Status

| | |
|---|---|
| location, extent, element size | **proven** — `model+0x44`, `NX`/`NZ` at `+0x0c`/`+0x10`, 2 bytes per cell |
| indexing | **proven** — row-major, `(z*NX + x) * 2` |
| height | **proven** — `byte0 & 0x03`, confirmed by the engine's `andi 0xc3` and by the disc histograms |
| `byte0 & 0x3C` | engine-maintained, wiped and repainted; meaning open |
| `byte0 & 0x80` | set by the engine in three places; meaning open |
| `byte0 & 0x40` | survives the mask; unexplained |
| `byte1` | written from the `+0x2c` table; meaning open |
