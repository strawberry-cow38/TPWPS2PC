# Theme Park World (PS2, PAL) — first look

⚠ **NO GAME FILES IN THIS REPO.** Master's instruction 2026-09-21, superseding the earlier
"keep the findings local": findings go in this private repo, game data never does. Nothing here goes
in `strawberry-cow38/TPW-PSXPC` either — that repo is the PSX port.

Source: the owner's own disc, their machine, `D:\ROM\ps2\Theme Park World (Europe) (En,Fr,De).bin`
(+.cue). 621,734,736 bytes = 264,343 sectors, single `TRACK 01 MODE2/2352`. Sector user data is
2048 bytes at offset 24. (Their "P:\" is this box's `D:`, volume "piner" — the letter changed.)

## Filesystem (209 entries, ISO9660 read clean)

| what | notes |
|---|---|
| `SLES_500.32` | 2,822,496 B, the EE executable |
| `DATA/*.WAD` | 13 archives: DATA, FANTASY, HALLOW, JUNGLE, SPACE, LOBBY, FRONTEND, UI, ICONS, MENUS, PARTICLE, LIPS, and four `?RSE.WAD` |
| `MOVIES/*.MPC` | 11 movies, 6–36 MB each, ~285 MB total |
| `AUDIO/<WORLD>/<PARK>/` | banks per world: ADVISOR, FANTASY, GLOBAL, HALLOW, JUNGLE, RIDES, SPACE; `.MAP` + `.SDT` pairs |
| `*.IRX` | IOP modules, incl. **ILINK.IRX / ILSOCK.IRX** (the online features) |
| `PADDING.` | 31 MB of nothing, to push data outward on the disc |

⭐ **IT IS A DIFFERENT GAME, NOT A BETTER-LOOKING ONE.** Four themed worlds (Fantasy, Halloween,
Jungle, Space) reached through a Lobby. The PSX build's catalogue on map 203 is 19 rides, all
Jurassic/Aztec (Inca Pot, Sun God, Mayan Spinner, Tom Tom Twister). None of that shares a name with
a PS2 world. So PS2 is not "the same content at higher fidelity" and its data will not drop into the
PSX port's shapes.

## The executable

Standard PS2 ELF: 32-bit LE MIPS (machine 8), entry `0x100008`, **one** PT_LOAD at `0x00100000`,
filesz 2,613,256, memsz 2,814,804.

⭐ **NO BASE-ADDRESS HUNT.** The load address is in the header. The PSX side cost a whole
investigation to establish that `TPW.BIN` sits at `0x80010000` (its own first word invites
`0x800C0000`, which scores 15%). Here it is stated.

Sections: `.text` 0x1A420C @0x100000 · `.vutext` 0x1BC0 @0x2A4210 · `.data` 0xADC58 · `.rodata`
0x2A248 · `.bss` 0x311D4 · `.gcc_except_table` (so C++ with exceptions somewhere).

## ⭐ The VU1 question, answered: it is ~7 KB

`.DVP.ovlytab` (0x3C bytes, 0x0C stride) = **five** VU overlays, with `.DVP.ovlystrtab` naming them:

| # | EE addr | size | VU load addr |
|---|---|---|---|
| 0 | 0x2A4240 | 0x1A0 | 0x0 |
| 1 | 0x2A4420 | 0x800 | 0x0 |
| 2 | 0x2A4C28 | 0x800 | 0x800 |
| 3 | 0x2A5430 | 0x800 | 0x1000 |
| 4 | 0x2A5C38 | 0x180 | 0x1800 |

6,944 bytes total. Overlays 2–4 share one source hash (2596800939) and load at consecutive 2 KB
banks, so they are **one program in three banks**, not three programs. Names are compiler-generated
(`.DVP.overlay..unknvma.<hash>.<line>.<n>`), not human ones.

VU1 micro is 64-bit instruction pairs, so 0x800 bytes is 256 pairs. The whole microcode budget is
five programs of at most 256 instructions. **Bounded, and small.** It is not the open-ended problem
it looks like from outside; it is about as much code as one busy C function each.

## The WAD container — READ, format confirmed

Magic `FKNL` (LE u32 0x4C4E4B46). Verified on ICONS.WAD (57,904 B): last entry ends at 57,899, five
bytes of pad. Header:

```
0x00 'FKNL'   0x04 0        0x08 data start   0x0C version<<16 | count
0x10 string table off       0x18 record table off        0x20 count
```
Record, 16 bytes: `u32 nameOff (absolute), u32 dataOff, u32 storedSize, u32 secondSize`.

`secondSize > storedSize` on every entry (2.14x on the icons, 1.36x on a 59-byte file), and the
payload does not begin with any file's own magic — so **the entries are compressed** and the second
field is the decompressed length. Scheme not yet identified; first bytes `10 fb 00 a0 d6 03 01 00`.

😼 ICONS.WAD ships `copy.ico`, `del.ico`, `list.ico` and **`vssver.scc`** — a Visual SourceSafe
status file. DATA.WAD's head carries `GCC\BIN;E:\BIN;` and `PS2_CPATH=E:\usr/local/sce/ee/gcc/`.
Same family as the PSX build's `" BOYS ROCK OUT - TPW BOOT "` padding: the devs' tree leaked onto
the disc.

## Tools

`scratchpad/iso2.py` (list) and `isox.py` (extract by extent+size) read Mode 2/2352 directly; no
disc image is copied anywhere. Extracted on the box for later: `C:\claude-workspace\SLES_500.32`.

## ⭐⭐ The compression: EA RefPack (QFS) — SOLVED, 2026-09-21

`10 FB` is not a mystery, it is **EA RefPack**, and this is an EA-published game. Confirmed three
ways before any decoding:

1. Magic bytes `10 FB` at the head of every payload.
2. The header's 3-byte **big-endian** decompressed size reads 41,174 for ICONS.WAD's first entry —
   **exactly** the directory record's second field. Two independent sources, same number.
3. The publisher matches the format.

Then decoded, which is the test that matters:

| archive | streams | decoded to declared length | rejected |
|---|---|---|---|
| ICONS.WAD (whole) | 4 | **4** | 0 |
| JUNGLE.WAD (first 6 MB) | 1,293 found by scan | **1,168** | 125 |

⚠ **THE 125 ARE ACCOUNTED FOR, NOT WAVED AWAY**: 123 of them lie INSIDE another stream's compressed
bytes — coincidental `10 FB` pairs in compressed data, and 6 MB should contain about 92 of any given
byte pair by chance. The remaining 2 are at the 6 MB truncation boundary. **Zero size mismatches.**

Header: `10 FB`, then (only if `byte0 & 0x01`) a 3-byte BE compressed size, then a 3-byte BE
decompressed size. Command stream is standard RefPack: `<0x80` 2-byte, `<0xC0` 3-byte, `<0xE0`
4-byte, `<0xFC` literal run, `>=0xFC` terminator.

Decoder: `scratchpad/refpack.py` → `decompress(data, offset) -> (bytes, declared_size, end_offset)`.
Staged on the box at `C:\claude-workspace\refpack.py` with `iso2.py`, `isox.py`, `wadls.py`.

⚠ STILL OPEN: the WAD directory is a **tree**, not a flat list. Header 0x20 and 0x24 are two counts;
ICONS has (4, 0) and is flat, JUNGLE has (2, 10) — two loose files then ten directories, each record
pointing at a sub-table. `wadls.py` only reads the flat case.

## ⭐⭐ The WAD directory: a TREE, solved 2026-09-21

```
header : 'FKNL', u32 0, u32 dataStart, u32 hash, u32 stringTable, u32 rootBlockOff
block  : u32 fileTableOff, u32 dirTableOff, u32 fileCount, u32 dirCount
file   : u32 nameOff (absolute), u32 dataOff, u32 storedSize, u32 decompressedSize   [16 bytes]
dir    : u32 nameOff, u32 blockOff                                                   [8 bytes]
```

The header's sixth word points at a **block**, and every directory entry points at another block.
That is the whole recursion. ICONS.WAD's root block is (4 files, 0 dirs) and looked flat, which is
why the first reader "worked" and hid the structure.

**⚠ AN ENTRY IS ONLY REFPACKED IF IT SHRANK.** When RefPack could not beat the original the archive
stores the file raw: those entries have `storedSize == decompressedSize` and no `10 FB` magic.
83 of JUNGLE.WAD's 2,545 files are like that (81 of them small 64×64 TGAs, plus two `vssver.scc`).
Treating a failed decompress as a broken file loses exactly the files that compress worst.

**JUNGLE.WAD, whole archive: 2,545 / 2,545 files read to their exact declared size, 0 failures**
(2,462 RefPacked + 83 raw), across 170 directories. Independent cross-check: the walker yields 2,715
entries and the string table holds exactly 2,715 names, so every name is accounted for by one entry.

Proof it is really files and not just lengths — `/GetDataFromS.bat` decompresses to the developers'
own build script, naming their internal project:

    xcopy s:\tpwpsx2\worksigned\Jungle\*.?ps . /s /y /c /d
    xcopy s:\tpwpsx2\worksigned\Jungle\*.tga . /s /y /c /d

and `/Rides/Bouncy/textures/Bd_42.tga` is a valid TGA (type 2, 64×64, 24 bpp, BGR, bottom-left
origin) that renders as a real texture. 984 TGAs in this archive alone.

`/Rides/` holds 21 folders: Bouncy, Bumper, Coaster1, Coaster3, GoKarts, IncaGod, Lookout, MineCart,
Monkey, Mumbo, PorkPie, Spider, TVSim, Totem, TourRide, Volcano, Wateride, dizzyd, king, manic, snake.

Reader: `scratchpad/wadtree.py` → `walk(data)` for the tree, `read(data, off, stored, decomp)` for
one entry's bytes (handles both the RefPacked and the raw case).

Per-file formats still unknown: `.RSE`, `.rss`, `.sam`, `.mps`, `.aps` (models/animation?), `.ssh`
(EA's SHPS image container, probably the converted-for-PS2 form of the neighbouring `.tga`).
