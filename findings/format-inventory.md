# Every file format on the disc, and whether it is done

**Measured 2026-09-22** by walking the ISO9660 tree of `tpw_ps2.bin` and then the `FKNL` tree of
all 16 `.WAD`s. **180 files at ISO level, 14,678 inside the archives, 14,858 total, 36 distinct
extensions.** Byte counts are uncompressed sizes; the disc itself is 621,734,736 bytes.

Sizes here are what the files decompress to, so the totals exceed the disc. `findings/formats.md`
is the deep dive into one archive (JUNGLE.WAD); this is the whole-disc ledger.

| bucket | files | bytes |
|---|---:|---:|
| **we have it RE'd** — a reader exists and is checked | 8,400 | 557,581,753 |
| **common standard** — ordinary format or plain text | 6,440 | 133,911,488 |
| **not done** | **18** | **537,955** |
| total | 14,858 | 692,031,196 |

The three buckets sum to the census exactly, with nothing unclassified: that is the check, not a
presentational choice. **What is left is 18 files and half a megabyte** — 0.12% of the files and
0.08% of the bytes.

⚠ **Counts are case-insensitive.** The disc mixes spellings within a single extension and the split
is not small: `.tga`×4,563 / `.TGA`×1,132 (19.9%), `.RSE`×710 / `.rse`×8 (98.9% uppercase),
`.rss`×353 / `.RSS`×1. A case-sensitive glob on this disc silently analyses a biased sample and
produces a coherent, wrong answer — that is a mistake already made here once, recorded in
`findings/rse.md`.

---

## Done — a reader exists

| ext | n | bytes | what it is | reader |
|---|---:|---:|---|---|
| `.ssh` | 5,765 | 22,062,592 | EA **SHPS**/GIMEX textures. Types 0x84 and 0x85 are an IPU macroblock stream (MPEG-1 coefficient syntax, stored down columns, alpha a separate 0..128 plane); type 0x02 is uncompressed paletted with a PS2 **CSM1-swizzled** palette in a trailing 0x21 entry | `core/Ssh.cs`, `core/IpuDecoder.cs`, `tools/ssh.py` |
| `.rse` | 718 | 417,976 | **RSSE** ride/feature bytecode. 81 opcodes, `0x00..0x69`; word tags `0x80` opcode / `0x40` variable / `0x20` code address / `0x10` symbol / `0x00` immediate; trailing symbol table | `tools/rse.py` |
| `.lip` | 516 | 12,456 | lipsync: `u32` active/silent gate transition times in **microseconds**, terminated `FFFFFFFF`; initially active | `core/TPW.PS2.Data/LipTrack.cs`, `tools/lip.py` |
| `.mps` | 496 | 14,434,300 | **M3D2** mesh, magic `0x183076E4`. 160-byte mesh entries, batched vertex streams, skinning descriptor at `mesh+0x90` | `core/Model.cs`, `tools/m3d2.py`, `tools/skin.py` |
| `.aps` | 374 | 8,070,938 | animation. **Two track formats** (48-byte and 20-byte) selected by flag bit `0x20`; three signed 10-bit fields dequantised out of one 32-bit word | `core/Animation.cs`, `tools/aps.py` |
| `.sam` | 321 | 320,633 | ride description — the game's own design data, plain text | `core/RideCatalogue.cs` |
| `.map` | 84 | 157,595 | sound index beside each bank: 42 `*BANK.MAP` + 42 `*SFX.MAP` pairs, told apart by a 16-byte type GUID. Records are 24/20/42 bytes, so nothing is 4-byte aligned | `tools/sfxmap.py` |
| `.sdt` | 41 | 158,615,391 | sound banks. Codec tags `0x24`/`0x25` MPEG Layer II, `0x80` PS-ADPCM | `core/SoundBank.cs`, `core/Mpeg.cs`, `core/Vag.cs`, `tools/sdt.py` |
| `.dat` | 37 | 2,742,844 | localisation: `u32 count`, `count × u32` absolute offset, NUL-terminated strings; 1,087 entries; plus the plain-text `final*.dat` masters and `version.dat` | `core/TextDatabase.cs`, `tools/textdb.py` |
| `.wad` | 16 | 88,111,280 | **FKNL** archive — *not* the PC release's `.WAD`. Payloads are EA **RefPack**, and an entry is stored raw when RefPack could not beat it | `core/WadArchive.cs`, `core/RefPack.cs`, `tools/wadtree.py` |
| `.gin` | 14 | 3,740,116 | **GIN4** sideshow scenes — the fairground minigames as full 3D scenes with bones and animation | `tools/gin.py` |
| `.mpc` | 11 | 258,791,928 | EA movie container: a flat `4cc + u32 size` chunk stream, `MPCh` carrying one frame of **MPEG-2 video elementary stream** each, `SCHl`/`SCDl` the audio. Container is ours; the video inside is standard MPEG-2 | `tools/mpc.py` |
| `.md2` | 4 | 63,912 | same M3D2 family as `.mps` under another stamp (`0x1CD15D46`). ⚠ **not** Quake 2 — the engine's own dispatch lists `sam / mps / aps / md2 / hmp` together | `core/Model.cs` |
| `.ass` | 2 | 4,088 | advisor rule VM: 12-byte headers, game-day delays, signed-halfword instructions, ten opcodes; [consumer evidence and speech/text/lip join](advisor.md) | `core/TPW.PS2.Data/AdvisorRules.cs`, `tools/TPW.PS2.AdvisorAudit` |
| `.plb` | 1 | 35,704 | particle library, 105 records of 320 bytes. Layout confirmed against the loader call `FUN_00220800(…, "Data\Particle\Tp2.plb", 400, 0x400)` | `tools/plb.py` |

## Common standard, or plain text — nothing to reverse

| ext | n | bytes | what it is |
|---|---:|---:|---|
| `.tga` | 5,695 | 97,923,745 | Targa — **but read the reader before believing that**. 3,865 are 24bpp, 1,710 are 32bpp BGRA (alpha cutout), 111 are RLE, 8 are 8-bit paletted, and 4 of those 8 *lie in their header*: the sky cloud masks declare `cmapType 0 / kind 2` at 8bpp, which cannot exist. Believing the header renders every world's cloud layer as an opaque sheet that looks fine. `core/Targa.cs` |
| `.rss` | 354 | 611,154 | the `.rse` **source**, shipped: commented assembly with EA's own variable names and `#include` paths |
| `.scc` | 318 | 27,776 | `vssver.scc` — Visual SourceSafe status files, shipped by accident |
| `.sce` | 29 | 24,411 | plain-text **UI layout** for every in-game menu: `<menu>`, `<language>`, `<uielem>` with `row=`/`col=` frames and text justification. The whole menu system is declarative and on the disc |
| `.irx` | 21 | 627,856 | Sony PS2 IOP modules (IRX) |
| `.pl` | 4 | 4,601 | Perl build scripts, left in |
| `.dup` / `.h` / `.lbmlist` | 3 / 3 / 3 | 189,907 | localisation build leftovers — a C header of string enums, a duplicate report, a file list |
| `.ico` | 3 | 123,522 | PS2 **memory-card save icons** (`fileID 0x00010000`) — `copy` / `del` / `list`. Documented Sony format |
| `.gay` | 2 | 671 | plain-text sideshow descriptor, `[NAME]` / `[TYPE]` |
| `.bat` | 1 | 112 | a DOS batch file left in JUNGLE.WAD (`xcopy s:\tpwpsx2\worksigned\Jung…`) |
| `.cnf` | 1 | 56 | `SYSTEM.CNF`, plain text |
| `.32` | 1 | 2,822,496 | `SLES_500.32`, the boot ELF |
| `.img` | 1 | 98,901 | `IOPRP165.IMG`, Sony IOP reboot image |
| *(none)* | 1 | 31,457,280 | `/PADDING.` — 31.5 MB of filler to pad the disc |

## Not done — 18 files, 537,955 bytes

| ext | n | bytes | where | what is known |
|---|---:|---:|---|---|
| `.bff` | 6 | 322,719 | `DATA.WAD/Fonts/{European,Jap}/{Console,Large,Small}.bff` | **EA bitmap font**, magic `2FFB`. Nothing decoded. This is the one that matters for a port: no font, no text on screen |
| `.mtr` | 4 | 60,932 | `*/Sideshow/*/` | magic `0x2E5915AF`, exactly one per sideshow `.MD2`, so near-certainly its **material table**. Header counts look like (records, submeshes, offset) but nothing is confirmed |
| `.dba` | 3 | 120,588 | `DATA.WAD/ars{,us,jap}db.dba` | **Partially decoded park asset database.** Directory is `u32 count`, then `(key, offset, size)` records; payload `+4` selects an asset-name text row. Earlier advisor-speech interpretation was wrong: that join is embedded in the ELF. Managed directory/common-prefix reader exists; most payload fields remain unknown. [Evidence](advisor.md) |
| `.table` | 3 | 33,186 | `translations/*/kanji.table` | `u16` pairs, `FFFF` as a hole marker. Japanese glyph mapping. Only needed for a Japanese build |
| `.eng` | 2 | 530 | `/AUDIO/RIDES/{GRC,WTR}.ENG` | not a language file. Two mirrored amplitude ramps (`00 02 03 05 08 … 61 63 63` and its reverse) plus the build path `Q:\Theme Park II\TP-PS2\Export\Global\Sound\` and a bank name. An **envelope curve** for the `GRC`/`WTR` bank groups, which also have their own `*BANK.MAP`/`*SFX.MAP` pairs |

### What the remainder is actually worth

The advisor's `.ass` pair is now decoded, with a checked rule evaluator and the executable's
speech/text/lip catalogue. `.dba` belongs to park assets; grouping it with advisor speech was an
incorrect inference from filenames and numeric patterns. `.bff` concerns text rendering, `.mtr`
four minigame models, `.table` Japanese glyph mapping, and `.eng` two files of curve data.

## How this was measured

`tools/iso2.py` walks the ISO9660 tree (MODE2/2352, 2,048 user bytes per sector at offset 24);
`tools/wadtree.py` walks each `.WAD`'s FKNL tree and `wadtree.read` un-RefPacks each entry.
Extensions are folded to lowercase and the original spellings counted separately, so a case split
shows up instead of halving the corpus. Bucket membership was then summed back against the census
total — 8,400 + 6,440 + 18 = 14,858 — because a breakdown that does not add up to its own total is
hiding a path.

---

## Prior art: what the community already cracked (checked 2026-09-22)

Theme Park World has an active reverse-engineering community around **OpenTPW**
(`OpenTPW/OpenTPW`, `maexah/OpenTPW`, docs at `OpenTPW/opentpw-docs`), which targets the **PC**
release. Their coverage was checked against the six then-undecoded formats below, plus the ones
already done here; `.ass` has since moved to the decoded bucket.

⚠ **PC ≠ PS2, and their own docs say so.** The PC archive is `BFWD`/`DWFB`; the docs state outright
that the PS2's `FKNL` "is not compatible with DWFB". Treat every PC layout as a lead to test, never
as a spec — the case below where it half-transfers is the normal outcome, not the exception.

| ours | community status | use to us |
|---|---|---|
| `.bff` | **Cracked, and implemented well beyond their own docs.** `maexah/OpenTPW`'s `UI/BitmapFont.cs` carries a full `BF4` reader — glyph record, three pixel packings (raw nibbles / RLE / 1bpp), 4-bit coverage — derived from PC addresses `0x006b0680`, `0x006b4aa0`, `0x006b15f0`. The published doc page is only a partial header | **Large head start, not a drop-in.** PC magic is `"F4FB"`; ours is `"2FFB"` — a different version of the same family. See below |
| `.mtr` | **Not cracked by anyone.** `opentpw-docs/src/formats/mtr.md` is the word `TODO` and nothing else. `Render/Assets/Material.cs` is a Veldrid render material, not a file parser | nothing to borrow |
| `.table` | **Analogue documented**: `BFMU` / `BFUM` (Bullfrog Multibyte↔Unicode), the PC's `MBtoUni.dat`. Different file, same job — mapping multibyte to Unicode | a lead on what the table is *for*, not on its layout |
| `.dba` | absent from their format list | nothing |
| `.ass` | absent from their format list | nothing |
| `.eng` | absent from their format list | nothing |

### What `BF4` does and does not tell us about `BFF2`

Measured across **all six** fonts on the disc (`DATA.WAD/Fonts/{European,Jap}/{Console,Large,Small}.bff`):

```
+0x00  "2FFB"                   magic                       (PC: "F4FB")
+0x04  u8   1                   version, 1 in all six
+0x05  u8   line height         Console 9, EurLarge 22, EurSmall 15, JapLarge 23, JapSmall 16
+0x06  u8   ?                   3 / 8 / 6 / 2 / 1           scales with size, not monotonically
+0x07  u8   ?                   14 / 30 / 21 / 30 / 21
+0x08  cc f8 55 4c 47 55        SIX CONSTANT BYTES, identical in all six. No PC counterpart
+0x0e  u16  glyph count         132 / 294 / 294 / 2454 / 2454
+0x10  u16  256                 constant in all six
+0x12  u16  n                   22 / 49 / 49 / 409 / 409
+0x14  u16[n] character map     SORTED ASCENDING in all six
       then the glyph records, the first beginning 0x0020 in all six -- space is glyph 0
```

The map is not one entry per glyph (22 entries against 132 glyphs), and it is sorted, so it indexes
blocks rather than characters. What it does settle is **which encoding each font is in**:

* **European** — Unicode. `007e 00a5 00ac 00b1 00b8 00bb 00cf 00d6 00dc 00ef 00fc 00ff 0131 0153
  0178 0192 02c7 02c9 02dd 0394 03a9 03bc 03c0 2010 2014 201a 201e 2022 2026 2030 203a 2044 2122
  2126 2202 2206 220f 2211 2219 221a …` — cp1252's extensions plus Greek (Δ Ω μ π) and the maths
  set (∂ ∆ ∏ ∑ ∙ √), ending on `FFFF`.
* **Japanese** — Shift-JIS. `0021 0026 0029 002b 0039 005a 007a 007c 8142 8146 8149 815c …`, with
  **401 of 409** entries at or above `0x8140`, topping out at `0x9862`. Which is precisely what
  `kanji.table` here, and `BFMU`/`BFUM` on PC, exist to bridge.

So the PC work transfers as a **model** — glyphs carry width/height/bearings/advance, pixels are
4-bit coverage, packing is chosen per glyph — and not as a **layout**: different magic, different
header shape, a glyph count at a different offset, and six constant bytes at `+0x08` that `BF4` has
no slot for. The glyph records themselves are unread. It stays in the "not done" bucket until a
reader round-trips a real glyph to pixels.

⚠ **Two corrections I had to make to my own first pass here**, both from flattening a tree:

1. The first extraction wrote all six files into one directory **by basename**, so
   `Fonts/Jap/{Large,Small}.bff` silently overwrote `Fonts/European/{Large,Small}.bff`. Two of the
   three "European" fonts I first examined were Japanese. Same-named files in different directories
   eat each other without a word when you flatten.
2. I labelled European/Large as Shift-JIS off a one-line test — `any(v >= 0x8140)` — which fired on
   the `FFFF` sentinel at the end of its own map. **A heuristic that a single sentinel value can
   trip is not a classifier.** Counting how many entries land in the range (2 of 49 against 401 of
   409) separates them immediately; asking whether *any* does, does not.

### And one thing the community confirmed that we had already done

`maexah/OpenTPW` derives the RSSE instruction set from the PC executable's dispatch table. **All 81
opcodes derived here from the PS2's `.rss`/`.RSE` pairs agree with theirs by name at the same index,
with zero disagreements**, and their remaining 25 are exactly the 25 this disc never exercises. Two
derivations, two platforms, two methods, no shared upstream. See `findings/rse.md`.
