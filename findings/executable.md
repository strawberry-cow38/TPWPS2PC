# `SLES_500.32` — and why stock Ghidra cannot read it

PS2 ELF, 32-bit LE MIPS, one PT_LOAD at `0x00100000`, entry `0x100008`, `.text` 0x1A420C
(1,720,844 B = **430,211 instructions**), `.vutext` 0x1BC0, `.data` 0xADC58, `.rodata` 0x2A248,
`.bss` 0x311D4, plus `.gcc_except_table` (so C++ with exceptions somewhere).

## ⚠⚠ Ghidra 12.1.2 has no R5900 processor, and it costs two thirds of the binary

The PS2's EE is an **R5900**: a MIPS III core with 128-bit quadword loads/stores and the MMI
multimedia set. Ghidra 12.1.2 ships no R5900 language — the only `R5900` strings in `mips.ldefs` are
IDA-PRO name mappings, not a definition. Measured across the language variants:

| `-processor` | functions | instructions | `.text` decoded |
|---|---:|---:|---:|
| `MIPS:LE:32:default` | 6,169 | 48,362 | 11.2% |
| `MIPS:LE:64:64-32R6addr` (Ghidra's own guess) | 6,386 | 103,715 | 24.0% |
| `MIPS:LE:64:default` | 6,385 | 106,551 | 24.7% |
| `MIPS:LE:64:64-32addr` | 6,386 | 103,715 | 24.0% |
| `MIPS:LE:64:64-32addr` + aggressive instruction finder | **6,496** | **145,879** | **33.8%** |

⚠ **Ghidra's own auto-detection picks `64-32R6addr`** — MIPS64 *release 6*, a later and incompatible
revision. Nobody would choose it deliberately.

## The cause, counted

Primary opcodes across all 430,211 words of `.text`:

| opcode | instruction | count | share |
|---|---|---:|---:|
| `0x1E` | `LQ` load quadword | 18,382 | 4.27% |
| `0x1F` | `SQ` store quadword | 16,443 | 3.82% |
| `0x1C` | MMI (multimedia) | 1,464 | 0.34% |
| | **R5900-only total** | **36,289** | **8.44%** |

8.44% sounds survivable. It is not, because an undecodable instruction does not cost you one
instruction — it costs the **rest of its function**, and everything only reachable through it. At
that density:

| function length | chance it contains at least one R5900-only instruction |
|---:|---:|
| 10 instructions | 58.6% |
| 20 | 82.9% |
| 50 | 98.8% |
| 100 | ~100% |

**Nearly every real function in the binary contains a quadword load or store**, so nearly every one
dies part-way through. 8.4% of the instructions blocking 66% of the binary is exactly the shape the
measurements show.

## What that means

Reverse-engineering this executable is **blocked on tooling, not on difficulty** — and the fix is
bounded and known: extend the MIPS SLEIGH spec with `LQ`, `SQ` and the MMI set, or apply one of the
community R5900 definitions. Until then any function count or coverage figure taken from a stock
Ghidra import of a PS2 game understates it by roughly threefold, and *which* functions are missing is
not random — it is the ones doing vector maths, which is to say the interesting ones.

⭐ **This is the real difference in difficulty between the two versions**, and it is not the one
anyone expects. The PSX overlay needed a base address established by experiment and then read
cleanly. The PS2 executable states its load address in its own header — and then does not disassemble.
