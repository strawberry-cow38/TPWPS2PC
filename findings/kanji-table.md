# `kanji.table`: Shift-JIS to exported image ordinal

The three 11,062-byte files under `DATA.WAD/Text/translations/{eur,jap,usa}/` have a
managed reader in [KanjiTable.cs](../core/TPW.PS2.Data/KanjiTable.cs). **They do not
map to Unicode.** They map a packed Shift-JIS code to a small signed image ordinal;
the executable adds `0x23b` (571) to that ordinal. This is also **not a BFF glyph
index**. The numbered `kanji.lbmlist` exports independently identify the ordinal
namespace. The file-loading connection and the ultimate image lookup remain open.

Measured and traced 2026-09-22. EUR and JAP tables are byte-identical; USA is a
separate, different mapping. Their `.lbmlist` files have regional path prefixes,
so the two identical binary tables do not imply identical list bytes.

## Consumer evidence, and its limit

ELF SHA256: `231771d7f39cc29e7573ce62f437af15d97737a64c1d1c49df8404ead0b7577a`.
All addresses are virtual addresses. Reproduce with the R5900-aware helper:

```sh
python3 tools/r5900dis.py /path/to/ps2.elf 0x12fe78 0x1300d8
python3 tools/r5900dis.py /path/to/ps2.elf 0x1553d8 0x155424
python3 tools/r5900dis.py /path/to/ps2.elf 0x1dfaa8 0x1dfbbc
```

Searching ASCII strings, case-insensitively, finds **no `kanji` or `.table`**. The
localisation loader at `0x1dfaa8` formats the `Data\Text\translations\eur\` path
and a language `.dat` name, then relocates its `u32` string offsets. It does not
load this table. Searching code for the Shift-JIS boundary `0x8140` instead finds
`0x12fee8`, with three exact code ranges. Following its callers reaches a real
halfword-table consumer, rather than fitting a curve to the file:

```text
0x12ff60..70   code = (u8(source[0]) << 8) | u8(source[1]); call 0x12fee8
0x130080..8c   call that decoder; negative result takes a single-byte fallback
0x1300b4       slot *= 2
0x1300b8       base = *(u32*)0x3898b0
0x1300bc       address = base + slot
0x1300c0       result = *(s16*)(address + 2)       # skips the first word
0x1300c4       result += 0x23b
```

The constructor `0x12fe78` installs vtable `0x35cf98`; its slot at `0x35cfb4`
points to `0x130058`, the function above. `0x1553d8..0x155410` loads the selected
localisation and enables this object's multibyte mode exactly for language index
8 (`jap.dat` in the language-name array). `0x12fff0` advances the source pointer
by two when the range decoder accepts the code. These are character operations.

**The global pointer's loader was not found.** A scan of direct address pairs,
literal pointers and GP-relative accesses found the read above, but no assignment
of the file's buffer to `0x3898b0`. This address is in BSS, initially zero. References
using the same low immediate at `0x1c32cc` and nearby are to **`0x2e98b0`**, a different
object; they are not evidence of a table loader. No embedded copy of the table's
leading nontrivial bytes was found. Thus the ELF proves the addressing/consumer
contract, and the files independently fit it; it does **not** prove this path runs
successfully with `kanji.table` loaded on the European retail build. A leftover
export/rendering path is a possibility, not an established history.

## Every field

Little-endian storage. There are no interleaved `(multibyte, Unicode)` pairs.

| offset | field | evidence |
|---|---|---|
| `0` | `u16 ImageCount`: EUR/JAP 623, USA 657 | Exact ordinal domain `0..count-1` of the remaining words and the numbered `.lbmlist` names. Every ordinal occurs once. The traced consumer skips this word; **no runtime read of its count was found**. Naming it the export count is an inference supported by both files, not by a count alone |
| `2 + 2*i` | `s16` image ordinal at compact code slot `i` | `lh +2(base + 2*slot)` at `0x1300c0`; code-range function below fixes the slot. Nonnegative values select the corresponding four-digit LBM basename |
| same array, value `ffff` | signed `-1`, an unassigned export slot | Every other value belongs to the export ordinal domain. No LBM corresponds to `-1`. **The consumer does not branch on this sentinel**: it adds 571, producing 570. The meaning of that resulting ID is unknown |
| EOF `0x2b36` | end of 5,530 stored slots | Last stored slot `0x1599` corresponds to `0x9862`. There is no explicit slot-count or end-code field; extent comes from the file size |

The code range conversion at `0x12fee8..0x12ff50` is:

| packed Shift-JIS interval, inclusive | slot formula | exact evidence |
|---|---|---|
| `8140..84be` | `code - 8140` | subtract `8140`, unsigned `< 037f`, `0x12fef0..0x12ff08` |
| `8540..8796` | `code - 8540 + 037f` | subtract `8540`, unsigned `< 0257`, `0x12ff10..0x12ff28` |
| `889f..9872` | `code - 889f + 05d6` | subtract `889f`, unsigned `< 0fd4`, `0x12ff30..0x12ff40` |
| everything else | `-1` | `0x12ff44..48` |

These intervals include holes and are **not a general Shift-JIS validity test**.
The complete consumer domain has 5,546 slots. The shipped file omits its last 16
slots (`9863..9872`); a direct retail lookup there would exceed this buffer if no
extra allocation/padding exists. The reader bounds-checks and returns absent.
The entire second interval is unassigned in all three files; the implementation
still includes it, and synthetic boundary checks exercise it.

Ordering is a useful independent identity check: sorting each assigned packed code
by **trail byte, then lead byte** yields exactly ordinal `0,1,2,...` in both distinct
regional tables. For example, EUR starts `8140→0`, `8340→1`, `8b40→2`, `9240→3`,
`9640→4`, `8141→5`. The layout is addressed by the consumer's compact code slot;
the ordinal values reflect this different export ordering.

## Identity checks, not population checks

Unicode below is the identity of the source Shift-JIS character, independently
converted with code page 932. あ and 本 were also checked against recognizable BFF glyphs. **It is
not a value stored or returned by `kanji.table`.** The audited characters avoid
CP932/JIS mapping ambiguities.

| source bytes | Unicode / glyph | EUR/JAP ordinal | USA ordinal | Japanese BFF descriptor |
|---|---|---:|---:|---:|
| `82 a0` | `U+3042` あ | 335 (`0335.lbm`) | 349 (`0349.lbm`) | 118 |
| `93 fa` | `U+65E5` 日 | 611 (`0611.lbm`) | 645 (`0645.lbm`) | 589 |
| `96 7b` | `U+672C` 本 | 219 (`0219.lbm`) | 226 (`0226.lbm`) | 651 |
| `81 44` | `U+FF0E` ． | **hole** | 15 (`0015.lbm`) | **absent** in shipped Japanese Large/Small |

For あ, the slot is `0x160`, at file offset **`0x2c2`**. The words there are `014f`
(EUR/JAP, 335) and `015d` (USA, 349). The consumer results are **906 / 920**. Treating
335 as Unicode would produce `U+014F` (ŏ), and treating it as the BFF descriptor would
select the wrong glyph. BFF lookup instead uses the original **`82a0`**, yielding
index 118, Large 21×22 (left 1, top 2, advance 23), Small 15×16 (0,1,16).

The audit pins the ordered four-bit pixels of all three examples in both Japanese
fonts, in addition to the code/ordinal relationship. See [font shape inspection](bff-font.md)
for the earlier font work. This run inspected these actual BFF descriptors together: あ
and 本 are recognizable. **The `93fa` bitmap is anomalous**: both sizes show a rounded
outline with a short left inner stroke, not a clearly recognizable 日. That anchor checks
the code and ordered pixels, not linguistic correctness; the earlier font finding has
been corrected. No claim is made that every mapped character has been visually identified. In fact **38 EUR/JAP mapped codes and 32 USA mapped codes are absent from
these BFFs**. This is recorded as a mismatch, not silently substituted with another
character. The export table and the BFF repertoire must not be assumed interchangeable.

## Reader and audit

```sh
dotnet run --project tools/TPW.PS2.LastFiveAudit -c Release -- /path/to/disc.bin
dotnet run --project tools/TPW.PS2.LastFiveAudit -c Release -- --self-test
```

The managed reader exposes the count, signed slots, exact forward/inverse code-range
conversion and safe code-to-export-ordinal lookup. It does not convert to Unicode,
add a guessed font base, or claim that an export ordinal indexes BFF descriptors.
It rejects odd arrays, arrays beyond the consumer domain, invalid negative
values, out-of-domain ordinals, duplicates and missing ordinals. The ordinal bijection
is an observed export invariant, stricter than the unchecked consumer.

The audit reads all three exact regional paths case-insensitively. It pins a SHA256
of `u16 ImageCount` followed by **all 65,536** `(u16 code, s16 decoded ordinal)` pairs
in code order (absent = -1). Pins came from a separate Python byte walk after the ELF
trace, not from this reader; the reference walk is preserved in
[tools/last-five-reference.py](../tools/last-five-reference.py). EUR/JAP pin:
`2630322281ed528fffcd35b5ffb6bad12f7d91fcc02ebe1105c6ff02f2fe8aee`;
USA pin: `c2d40c3ebc36621e089f4b182afe01f014210f33a9abbead0c0e356ef7492fd3`.
It also checks each exact LBM filename, byte-sorted export ordering, the identities
above, all range boundaries and every truncated prefix of each disc table. Swapping
ordinals 0 and 5 preserves the entire population and passes structural parsing but
fails the identity digest. Exit 0 means pass, 2 means assertion failure, 1 means
usage/load exception. Pins cannot prove a live runtime connection that was not traced.

**Executed decoder negative control:** temporarily changed production lookup to swap
returned ordinals 0 and 5. All three regional digests and code identities failed; the audit
exited **2**. Restoring the decoder returns exit **0**. The swapped population is unchanged.

## What could not be established

* The file loader / assignment to the consumer's global pointer, and whether this
  older-looking image-ID path is used in this retail build. No live Japanese capture
  or memory trace was obtained. Absence of a filename string alone does not prove disuse.
* A runtime use of the leading count. Export-count meaning is supported by the exact
  numbered list and permutation, not a traced instruction reading that word.
* What namespace ultimately consumes `571 + ordinal`, why 571 is the base, and what
  ID 570 means after a hole. It is not established as a replacement glyph.
* The actual `report/.../NNNN.lbm` image contents. Those referenced exports are not in
  DATA.WAD. Their relationship to the later BFF export, including repertoire mismatches,
  is unknown; the table cannot decode missing images.
* Whether the last 16 consumer slots are intentionally omitted, a stale export boundary,
  or padded at load time. The managed reader deliberately avoids the potential overread.
* Unicode conversion as an alleged table purpose: **refuted by the consumer and identity
  examples**. PC BFMU/BFUM is not the PS2 table's mechanism.
