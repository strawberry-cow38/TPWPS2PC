# `.rse` — RSSE, the compiled game scripts

**Solved 2026-09-21.** `tools/rse.py` reads and disassembles them; the format and the full opcode
table live in its docstring rather than being restated here.

The disc ships **718 `.rse`** across 8 WADs — each world WAD and a source twin (`FANTASY`/`FRSE`,
`HALLOW`/`HRSE`, `JUNGLE`/`JRSE`, `SPACE`/`SRSE`) — and **354 `.rss`** sources. 279 distinct inner
paths, **270 carrying both**. That pairing is the whole reason this was tractable: the answer ships
next to the question.

## What was derived, and how it was checked

**81 opcodes, 0x00..0x69, every one unambiguous.** Built by aligning each source's mnemonic sequence
with each binary's `0x80`-tagged words: 262 of 270 files have equal counts, giving **10,606
instruction slots**, and no mnemonic ever aligned with two different opcodes in any file.

⚠ That is a statement about CONSISTENCY, so it was teeth-checked. Shuffling the mnemonic order
within each file and rebuilding the identical table leaves **71 of 81 mnemonics ambiguous and 10 of
10,606 slots intact**, on three separate seeds. The real alignment holds 10,606 of 10,606.

Verified end to end against `JUNGLE/Features/Toilet/Toilet.rse`, line for line with its source:
`TEST VAR_WORN` → `TEST v8` (WORN is the 9th `variable`), `ADDOBJ OBJ_PTCL 1 P_EFFECT_Flies 1` →
`ADDOBJ 1 1 9 1`, `BRANCH_Z ok` → `@31` where `.ok` is word 31. Variable indices, string indices,
branch targets and operand counts all land.

## ⚠ The mistake worth keeping

**710 of the 718 are spelled `.RSE` and 8 are `.rse`.** My first pass globbed `*.rse`
case-sensitively, found **8 files — 1.1% of the corpus — and produced a completely coherent
analysis**: 3 pairs, a clean "header+8 == variable count, 3 of 3", plausible constants. Nothing
looked wrong, because 8 real files are still 8 real files and every number computed off them was
true. It is the same trap this repo's `.ssh` work already documents for `.tga`/`.TGA` at 19.9% of
the disc, made worse by a smaller survivor set. **The tell was not in the analysis; it was that a
count taken one way (718) disagreed with a count taken another (8), and only because both existed.**

## Open

* `hdr+0x0C`, `+0x14`, `+0x18`, `+0x1C` — mostly zero, meaning not yet established.
* `hdr+0x08` matches the source variable count in 269 of 270; the one exception is unexamined.
* **25 values inside 0x00..0x69 appear in no script on the disc** (listed as `UNSEEN` in the tool).
  They are gaps in the corpus, not evidence that the opcodes do not exist.
* 8 of the 270 pairs emit fewer opcodes than their source has instruction lines, by 1 to 7, all in
  the same direction — so the source-side line counter is over-counting something the assembler
  does not emit, rather than the binary being short. Unresolved, and named rather than rounded away.
* ~~The string table itself is not located.~~ **Found, and it was inside the file all along.**
  See below.


## The symbol table — and the bug this corrected

`NAME s0` looked like an index into something external, and the first version of this document said
so. It is not: **the code does not run to end of file.** After the last instruction sits a table of
`u32 length` + that many bytes of NUL-terminated ASCII, ending exactly at EOF.

`Toilet.rse` is 780 bytes; its code stops at word 122 with `BRANCH @8` — which is the source's
closing `BRANCH load`, and `.load` is word 8, so the code terminates exactly where it should. The
remaining 232 bytes are:

    13 "Small Toilet\0"  12 "VAR_LETMEON\0"  13 "VAR_LETMEOFF\0"  ...

⭐ **EA shipped the debug symbols.** The table is the script's NAME followed by the variable names
in declaration order, so a port reads `VAR_PEEPID` rather than `v13`. All 14 of Toilet's come back
in source order.

⚠ **The first version of `tools/rse.py` read the whole file as code**, so it emitted 58 words of
string bytes as instructions — `tag 0x6C val 6384979` is the ASCII of `loo` being disassembled. It
did not error and the first hundred instructions were right, which is why it was publishable and
wrong at the same time. The reader now locates the table by parsing length-prefixed printable
strings from each word boundary and requiring the parse to land **exactly** on EOF; that exactness
is the check, and it is why the earliest passing offset is the real one rather than a coincidence.

**698 of 718** carry the table; 20 do not. Where it exists it holds `1 + variableCount` entries in
560 of 698 and *fewer* in the rest, by 1 to 5, never more — so unnamed variables are dropped and
the exact rule is not pinned.

Accordingly the `0x10` tag is now called a **symbol** index, not a string index. 758 operands carry
it across 17 distinct values, but **652 are `NAME` with value 0**, and `SPAWNCHILD` / `SPAWNSOUND`
carry values up to 22 that exceed some tables outright. The tag is real and the table is located;
how the two index each other is not established, and saying otherwise was the original error.

## Where the user-visible text lives

Searching the disc for `"Small Toilet"` and `"Crazy Ape"` also turned up
**`/DATA.WAD/Text/translations/usa/finalame.dat`**, 118,738 bytes, containing both — a localisation
table under a `Text/translations/<locale>/` tree. That is the UI text corpus, and it is not the
script symbol table: the scripts carry developer identifiers, this carries the player-facing strings.
