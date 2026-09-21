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

## ⭐⭐ Fixed: the community R5900 extension

[`chaoticgd/ghidra-emotionengine-reloaded`](https://github.com/chaoticgd/ghidra-emotionengine-reloaded)
v2.1.37 ships a build for exactly Ghidra 12.1.2. Installed into
`Ghidra/Extensions/`, it adds the language `r5900:LE:32:default`. Same binary, same machine, one
import later:

| | stock `MIPS:LE:64:64-32addr` + aggressive | `r5900:LE:32:default` |
|---|---:|---:|
| Pcode errors | 1,692 | **0** |
| instructions | 145,879 | **408,031** |
| functions | 6,496 | **7,298** |
| named functions | 1 | **54** |
| `.text` decoded | 33.8% | **94.8%** |

**Zero decode errors and 94.8% of `.text`.** The diagnosis above was right: it was the R5900
instructions and nothing else.

⚠ **AND THE OLD COVERAGE FIGURE STOPPED MEANING WHAT IT SAID.** The extension maps the PS2's real
address space — `vu0.code`, `vu1.code`, a 32 MB `iop_ram` — and marks those blocks executable, so a
single "% of executable bytes" number collapsed to 4.6% while the disassembly got three times
better. The figures here are now **per block**, because one number over a denominator that changes
between imports is not a measurement.

| block | size | instructions | decoded |
|---|---:|---:|---:|
| `.text` | 1,720,844 | 408,031 | **94.8%** |
| `.vutext` | 7,104 | 0 | 0% |
| `vu0.code` | 4,096 | 0 | 0% |
| `vu1.code` | 16,384 | 0 | 0% |
| `iop_ram` | 33,554,432 | 0 | 0% |

**The VU microcode is still undisassembled**, but no longer unreachable: the extension has VU0/VU1
support and creates the blocks for it. `.vutext`'s 7,104 bytes have to be mapped into `vu1.code`
and disassembled there. That is the route to the `.mps` vertex layout — the VU1 program is the code
that consumes those buffers.

## What that means

It was **blocked on tooling, not on difficulty**, and the community extension unblocked it in one
import. Any function count or coverage figure taken from a *stock* Ghidra import of a PS2 game
understates it by roughly threefold, and which functions are missing is not random — it is the ones
doing vector maths, which is to say the interesting ones. Never quote a stock number.

⭐ **This is the real difference in difficulty between the two versions**, and it is not the one
anyone expects. The PSX overlay needed a base address established by experiment and then read
cleanly. The PS2 executable states its load address in its own header — and then does not disassemble.
