# PS2 BFF bitmap fonts (`2FFB`)

The six files in `DATA.WAD/Fonts/{European,Jap}/{Console,Large,Small}.bff` decode to
legible pixels with the managed [BitmapFont reader](../core/TPW.PS2.Data/BitmapFont.cs).
The [FontAudit](../tools/TPW.PS2.FontAudit/Program.cs) checks ordered coverage, character
lookup and metrics, and writes PGM specimens and atlases. No extracted fonts or images are
committed. Findings and inspection below are from 2026-09-22.

The initial inventory's layout was wrong in several consequential places:

* `+0x0e` is **map-array byte length**, not glyph count. There is no explicit glyph count.
* The apparent `u16 256` at `+0x10` crosses the end of a 24-bit size and a separate high byte.
* The six constant bytes at `+8` split into two ignored bytes and the **`ULGU` chunk magic**.
* The `0020` after the first array is the first **range start**, not a glyph record.
* `+7`, not `+5`, is the **line advance** actually used between lines.
* `Jap/Console.bff` is byte-identical to `European/Console.bff`; it is not a Japanese font.

## Consumer, not just a byte pattern

Executable: `/home/ec2-user/tpw-ps2/ps2.elf`, SHA256
`231771d7f39cc29e7573ce62f437af15d97737a64c1d1c49df8404ead0b7577a`.
Addresses here are **virtual addresses**, inspected using this repository's R5900 helper:

```sh
python3 tools/r5900dis.py /path/to/ps2.elf 0x20a620 0x20a800
python3 tools/r5900dis.py /path/to/ps2.elf 0x118d38 0x119460
python3 tools/r5900dis.py /path/to/ps2.elf 0x119628 0x119680
python3 tools/r5900dis.py /path/to/ps2.elf 0x1d30e0 0x1d32b8
python3 tools/r5900dis.py /path/to/ps2.elf 0x1d34b0 0x1d34f0
```

The `.bff` search finds `Small.bff`, `Large.bff`, `Console.bff` at `0x36c8b0`,
`0x36c8c0`, `0x36c8d0`. The path format at `0x36c848` is `Data\Fonts\%s\%s`.
`0x20a620..0x20a64c` formats that path; after opening the resource it calls `0x118d38`
at `0x20a6cc`. That factory constructs the font and **calls `0x119270` at `0x118d94`**.
This connects the file names to the loader whose fields are described here.

`0x119270` reads ten bytes and checks `0x42464632` (`2FFB`). It then reads eight
bytes, checks `0x55474c55` (`ULGU`), reads a two-byte range count, allocates/reads
`count * 6`, reads another eight-byte header, checks `0x42464744` (`DGFB`), masks
its next word with `0x00ffffff`, and allocates/reads that many glyph-data bytes.

The factory installs the vtable at `0x35a918`, connecting these consumers:

| vtable slot | function | observed operation |
|---|---|---|
| `+0x24` | `0x118ea8` | returns the three header metrics as `u16`s |
| `+0x2c` | `0x118df0` | code-to-glyph lookup using all three range arrays |
| `+0x34` | `0x118ec8` | returns advance, left + width, top + height |
| secondary interface `0x35a8e8`, `+0x24` | `0x119068` | expands the glyph bitmap |

For example, the text layout at `0x1d3124..0x1d3190` calls the metric slot,
stores the current pen, computes the glyph bounds, and adds the returned advance to X.
At `0x1d329c..0x1d32b4` it calls `0x1d34b0` and adds the result to Y between lines.
That function calls the font's metric slot and returns the **third** metric (`lhu 4(sp)`
at `0x1d34dc`). The loader stored header `+7` as that third metric at `0x1192dc`.
This establishes line advance by use, rather than by how its values scale with font size.

## File layout and field evidence

All multibyte numbers are little-endian. Let `n = u16(+0x12)`,
`C = 0x14 + 6*n`, and `B = C + 8`.

| offset | type / value | interpretation and evidence |
|---|---|---|
| `0x00` | four bytes `2FFB` | magic, checked at `0x1192b4..0x1192c0` |
| `0x04` | `u8`, always 1 | version by convention/inference; the traced loader does not test it. Reader supports only the observed value |
| `0x05` | `u8` | raw metric 05. Copied to font object `+0x0c` at `0x1192e4`, returned as metric 0. **Semantic name not established** |
| `0x06` | `u8` | raw metric 06. Copied to object `+0x0e` at `0x1192e8`, returned as metric 1. **Semantic name not established** |
| `0x07` | `u8` | line advance, copied to object `+0x10`; downstream Y increment above |
| `0x08` | two bytes `cc f8` | ignored bytes within the ten-byte first read. Padding is an **inference**, not a proven field name |
| `0x0a` | four bytes `ULGU` | map chunk magic, compared at `0x119320..0x119328` |
| `0x0e` | `u24`, `6*n` | measured byte size of the three arrays, excluding their two-byte count. Loader reads this word but sizes the arrays using `n` instead |
| `0x11` | `u8`, always 1 | upper byte of map size word; meaning unknown, not tested by loader |
| `0x12` | `u16 n` | range count, read at `0x119330..0x11934c`; allocation/read of `6*n` at `0x119354..0x1193a0` |
| `0x14` | `u16[n]` | inclusive range **ends**, binary searched by `0x119628` |
| `0x14+2*n` | `u16[n]` | inclusive range **starts**, compared to the code at `0x118e44..0x118e54` |
| `0x14+4*n` | `u16[n]` | index **subtractors**, subtracted from the code at `0x118e78..0x118e7c` |
| `C` | four bytes `DGFB` | glyph-data chunk magic, compared at `0x1193d8..0x1193e4` |
| `C+4` | `u24` | glyph-data byte length, masked with `0x00ffffff` at `0x1193f8` and `0x119418`. Exactly `file length - B` in every file |
| `C+7` | `u8`, always 1 | high byte discarded by the loader; meaning unknown |
| `B` | 14-byte descriptors, then bitmaps | descriptor address is `B + 14*index`, explicitly computed at `0x118e60..0x118e84` |

The map lookup is:

```text
i = first range whose end >= code                  # lower_bound, not exact match
if no such range or code < start[i]: absent
index = code - subtract[i]                         # integer subtraction, not u16 wrap
descriptor = glyph_data_base + 14 * index
```

All ranges are disjoint and increasing on this disc. Subtractors are **not offsets to
glyph blocks** and need not equal the start: the Console range `0020..007e` subtracts
`0020`; its `00a0..00a0` range subtracts `00a0`, aliasing nonbreaking space to space.
Console soft hyphen also aliases hyphen. European Large/Small include aliases such as
`03a9`/`2126` (omega/ohm) and `0394`/`2206` (delta/increment).

The European terminal `ffff..ffff` is handled like any other range: it maps to descriptor
216 in Console or 223 in Large/Small. The lookup does **not** special-case it as a
terminator or select it as a missing-character fallback. Its intended application-level
meaning remains unknown.

**Descriptor extent is inferred**, not stored: `1 + max(end[i] - subtract[i])`.
This yields contiguous, fully referenced descriptor tables in all six files. Independently,
the first nonempty bitmap begins exactly after those tables, and every subsequent bitmap
begins immediately after the previous bitmap. No holes, overlaps or trailing bytes remain.
These extent checks support the layout; glyph shapes and ordered pixels validate the image.

## Each 14-byte glyph descriptor

Offsets below are relative to **that descriptor**, not to `B`.

| offset | type | interpretation and evidence |
|---|---|---|
| `+0` | `u32` | flags, zero throughout the corpus. `0x11915c..0x119178` reads this word and **rejects bit 0 set**. Other bits have no established meaning |
| `+4` | `u32` | bitmap displacement from the descriptor's own address. Unaligned word read at `0x119188..0x11918c`, added to descriptor at `0x119198` |
| `+8` | `u8` | width, read at `0x1190fc`, multiplied by height to allocate/output pixels |
| `+9` | `u8` | height, read at `0x119104`; product at `0x119124` |
| `+10` | `s8` | left bearing, `lb` at `0x1190cc`, also added to width at `0x118ed8..0x118ee4` |
| `+11` | `s8` | top bearing, `lb` at `0x1190dc`, also added to height at `0x118ee8..0x118ef8` |
| `+12` | `u8` | X advance, `lbu` at `0x118ed0`, consumed by text pen addition at `0x1d3178..0x1d3190` |
| `+13` | `u8` | unknown, `57` for European/Console and `58` for Japanese Large/Small. No use found. Padding is an **inference** |

Left/top are relative to the pen/line origin: place a glyph at `(penX + Left, lineY + Top)`
and move the pen by `Advance`. Negative values are real: Console `$` has top `-1`,
Console `j` has left `-1`. Do not reinterpret top as an offset above a baseline or append
`+13` to advance to make a `u16`.

Space is an empty bitmap with nonzero advance. All empty descriptors have width/height
and relative pointer zero. Japanese Large/Small also have an empty fullwidth space
at packed Shift-JIS `8140`, with a different advance from ASCII space.

## Pixel order and packing

For the supported zero-flags records:

```text
start  = descriptor_address + u32(descriptor + 4)
pixels = width * height
stored = (pixels + 1) / 2
coverage[p] = (p even) ? data[start+p/2] >> 4 : data[start+p/2] & 15
```

The nibble cursor starts at zero (`0x119194`). `0x1191bc..0x1191d8` selects high
nibble first; `0x1191f0..0x119208` increments the pointer only after the low nibble
and toggles the nibble index. `0x11922c..0x119240` expands the sample to a byte with
`n | (n << 4)`, i.e. `17*n`. The loop emits **width × height samples**, with no
row-boundary reset. The reader retains 0..15; the PGM renderer multiplies by 17.

Rows are left-to-right, top-to-bottom: this is confirmed by rendering identifiable,
asymmetric glyphs, including `F`, `R`, `g` and Japanese kana. A transposed image or a
low-nibble-first image is visibly broken. Odd-width rows continue through the byte stream;
only the final unused low nibble is padding, and it is zero in every odd-area disc bitmap.
There is **no four-byte alignment** between bitmaps.

Concrete example: Console `F` is code `0046`, descriptor 38, at file `0x2b4`.
Its displacement is `0xcef`, so its pixels start at `0xfa3`. Width/height are 5×9,
bearings 0,0 and advance 6. The rendered shape has the vertical stroke on the left,
full top and middle bars, and an open lower right. The audit checks those positions,
not the number of lit pixels. Console `R` has an antialiased diagonal leg to the right;
`g` extends below the capitals. These detect orientation and coverage mistakes.

The PC [OpenTPW BitmapFont.cs](https://github.com/maexah/OpenTPW/blob/main/source/OpenTPW/UI/BitmapFont.cs)
was useful prior art for metrics and four-bit coverage, but its offsets, descriptor layout
and packing dispatch do not apply here. An RLE helper exists at `0x118f08..0x119064`
(nonzero nibble literal, `0,count,value` run, `0,0` stop). **No caller/reference was
found in this ELF scan**, the traced bitmap method does not call it, and no disc descriptor
sets flags. This is not evidence for assigning the PC's packing modes to PS2 flags.
The managed reader rejects all nonzero flags rather than implementing an unverified mode.

## Corpus measurements and encodings

These are inventory measurements, **not correctness gates**:

| file(s) | bytes each | n | descriptors | mapped codes | +5 | +6 | line advance | B |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| European/Console, Jap/Console | 8,655 | 22 | 217 | 219 | 9 | 3 | 14 | `a0` |
| European/Large | 20,443 | 49 | 224 | 231 | 22 | 8 | 30 | `142` |
| European/Small | 11,951 | 49 | 224 | 231 | 15 | 6 | 21 | `142` |
| Jap/Large | 178,857 | 409 | 701 | 701 | 23 | 2 | 30 | `9b2` |
| Jap/Small | 94,158 | 409 | 701 | 701 | 16 | 1 | 21 | `9b2` |

European Large/Small use coverage 0 and 15. Console additionally uses 8, 9 and 11.
Japanese Large/Small use all sixteen levels. These distributions are observations,
not the derivation of pixel order or flag semantics.

**Encoding is inferred from the mapped codes and identified shapes**, not a header flag.
The European fonts use Unicode values, including accented Latin, ligatures, Greek and
math symbols. Console has a different repertoire: it includes `20ac` (euro), which the
European Large/Small maps omit. Japanese Large/Small use packed Shift-JIS: `82a0`
renders あ, `834a` renders カ, `967b` renders 本. The `93fa` code identifies 日 in Shift-JIS,
but its bitmap was later found anomalous; see the correction below. The reader
accepts raw `ushort` codes; it does not guess an encoding from the directory name or
convert a .NET Unicode string into Shift-JIS. In particular, `Jap/Console` must still be
used as the European Console font. No claim is made here about the game's `kanji.table`
conversion path.

**A shape anomaly found by looking:** European `0394` and `2206` both map to descriptor
189 (`0394 - 02d7`, `2206 - 2149`). Large's descriptor explicitly says width 2, height 6,
left 7, top 12, advance 7. Its three raw bitmap bytes are `ff 00 ff`: two short horizontal
marks, not a delta. Small's corresponding 6×4 bitmap is three isolated lit samples, also
not a delta. This is not a nibble-order explanation: swapping nibbles cannot make Large's
two-pixel-wide descriptor into the expected triangle. The executable's lookup and the
source descriptor both lead here. **Why these symbols were authored this way is unknown**;
an upstream source-font/export problem is only a hypothesis. The reader preserves the
disc bitmap, and the specimen deliberately leaves this visible. Do not describe the entire
Unicode repertoire as semantically verified just because the Latin text renders correctly.

## Audit and actual visual inspection

```sh
dotnet run --project tools/TPW.PS2.FontAudit -c Release -- /path/to/disc.bin /tmp/font-audit
dotnet run --project tools/TPW.PS2.FontAudit -c Release -- --self-test
```

The audit reads `/DATA/DATA.WAD` using the existing managed `Disc` and `WadArchive`
readers, matches filenames **case-insensitively**, and requires each known font path.
Exit 0 means pass; 2 means a failed check; 1 means usage or an uncaught load/I/O error.
It checks:

* A fixed SHA256 of **decoded semantic data for each font**: three metric bytes, then
  for every mapped code in ascending order a little-endian `u16 code`, six little-endian
  `i32`s (`index,width,height,left,top,advance`), then its ordered 0..15 coverage bytes.
  The pins were computed with a separate Python byte walk after tracing the executable
  and inspecting initial renders. They are not raw-file hashes and do not update themselves.
* Console `F`/`R`/`g` shape and bearing anchors, plus a hand-authored synthetic odd-width,
  odd-area bitmap with fractional coverage. A horizontal mirror preserves every population
  count and fails these checks.
* Lookup against an independent linear range walk across all 16-bit codes, including
  gaps, aliases and `ffff`. Record-relative pointers, descriptor reachability, sequential
  payload extents, final nibble padding and exact EOF are checked separately.
* Rejection of every truncated prefix of the synthetic font, wrong magic/size/version/tag,
  unordered/overlapping ranges, subtractor underflow, out-of-range indices, offset overflow,
  pointers into descriptors, unsupported flags and extra trailing bytes. Opaque padding is
  deliberately permitted to vary. Synthetic placement/clipping checks cover signed bearings.

Each font produces `*-specimen.pgm`, `*-atlas.pgm`, and `*-index.tsv`. Specimens are
nearest-neighbour enlarged 3×, use signed bearings/advances and the actual line advance,
and include `FRg jpq`, digits, `Theme Park World!`, both Latin alphabets, and either
Unicode extensions or `あいうえお カキクケコ 日本`. Atlases place glyphs in descriptor
order, 16 per row; the TSV associates each cell with its raw code(s) and metrics. Atlas
cells deliberately ignore bearings to expose the complete bitmap; specimens exercise placement.

**Observed:** inspected the audit's six specimens as images, through lossless PNG views
of the emitted PGM bytes. The `F` stems and `R` legs face correctly; `g/j/p/q` descend;
the text is legible; Japanese kana and 本 have recognizable, correctly oriented shapes.
The original assertion about 日 is withdrawn by the subsequent inspection below.
The differently styled Japanese Latin alphabet and fractional antialiasing survive decoding.
Inspected the European Large and Japanese Large full atlases as well. The extended European
specimens exposed the delta anomaly above. This does **not** assert that every symbol or
kanji has been individually identified.

**Negative control:** temporarily replaced the production decoder's output index with
`row*width + (width-1-column)`. All six decoded digests and the asymmetric Console
shape checks failed; the audit exited **2**. Glyph dimensions and pixel quantities were
unchanged. The mutation was removed; the normal audit exits **0** with no failures.

## What remains unestablished / outside the audit

* Semantic names of header `+5` and `+6`. They are exposed as `Metric05`/`Metric06`;
  neither is labelled ascent, descent, baseline, or line height. Their scale is not proof.
* Meaning of header byte `+4` beyond the version inference; meanings of both chunk high
  bytes; provenance of `cc f8` and descriptor `+13`. Padding/version labels above are
  explicitly inferences. The reader restricts versions/tags to the observed value 1,
  even though the game ignores them, and checks lengths more strictly than the loader.
* Any nonzero descriptor-flag mode, whether the unreferenced RLE helper belongs to an
  older variant, and whether any other disc/build uses a different table/payload layout.
* Whether an unobserved variant can contain unmapped descriptors beyond the maximum
  mapped index. The extent derivation fits the complete shipped corpus; it is not a
  universal format guarantee.
* Meaning of `ffff`, private-use glyphs and every individual Japanese code. Ordered
  digests catch regression from the reviewed decode; they cannot prove a previously
  unidentified glyph's linguistic identity or correct a wrong golden expectation.
  In particular, the reason for the malformed-looking `0394`/`2206` bitmap is unresolved.
* In-game GS blending, scaling, texture upload and final framebuffer output. PGM uses
  the consumer's nibble-to-byte expansion, not an emulator capture comparison. It does
  not establish gamma or final on-screen opacity.
* Full localisation-to-glyph conversion, including `kanji.table`, fallback policy, text
  wrapping and UI integration. This delivers the font reader and evidence; it does not
  wire fonts into the viewer or assert that the Japanese game text path is complete.

## Subsequent kanji-table identity inspection (2026-09-22)

[The kanji-table investigation](kanji-table.md) independently followed Shift-JIS `82a0` and
`967b` to あ and 本 and inspected the Large/Small pixels. It also re-examined `93fa`, which
maps to descriptor 589: both sizes show a rounded outline with a short left inner stroke,
rather than a clearly recognizable 日. The earlier claim that 日 was visually confirmed was
too strong. Its lookup and pixel digest remain established, but its linguistic shape does not.
The cause is unknown, like the European delta anomaly; no replacement bitmap is invented.

`kanji.table` is an export-image ordinal map, not the Unicode bridge hypothesized in the
initial inventory. It uses a different index namespace from BFF, has regional differences,
and contains mapped codes absent from these fonts. Do not use its ordinal as a descriptor index.
