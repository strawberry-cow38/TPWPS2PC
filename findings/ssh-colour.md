# IPU colour conversion audit — 2026-09-22

**Keep the current conversion. It is exactly correct against PCSX2's IPU reference:
16,777,216 of 16,777,216 possible byte-valued Y/Cb/Cr inputs agree.** No coefficient,
bias, shift or clipping behaviour was changed. The arithmetic was extracted into
an internal function so the production implementation can be tested directly.

Flat RGB exactness remains **19 of 121 flat references**, and flat RGBA exactness
remains **2 of 121 flat references**. The premise that a flat source must round-trip
exactly is false for a quantised colour space. **26 of 117 compressed flat references
have RGB colours that this fixed CSC cannot produce from ANY byte-valued Y/Cb/Cr
input**. These are **18 of 36 distinct target RGB colours**. This is enumeration
of the input domain of a fixed, independently sourced function, not a coefficient
search or a fitted decoder.

There is one source disagreement that prevents claiming new proof of silicon
behaviour: Sony's manual truncates green's positive chroma products before
subtracting them, while PCSX2 truncates negative products before adding them.
The literal-manual alternative was tested and rejected under this job's regression
bar. A better TGA score would not settle this hardware question either. No hardware
capture was available or performed.

Thus this audit closes the alleged mismatch with PCSX2's CSC, and disproves the
flat-lossless argument. It does **not** establish that every non-flat residual is
encoder loss: the exact original encoder, PC/PS2 asset differences, and hardware
IDCT parity have not been established here. FFmpeg parity is documented separately
in [ipu.md](ipu.md). Do not relabel that as a hardware capture.

## Derivation and source hierarchy

Read sources:

* [PCSX2 `yuv2rgb.cpp`, revision 81a20150f2b3d32dc25dbe5dfab0fac1af5d000d](https://github.com/PCSX2/pcsx2/blob/81a20150f2b3d32dc25dbe5dfab0fac1af5d000d/pcsx2/IPU/yuv2rgb.cpp):
  the reference and SIMD implementations specify the same integer operations.
  The [original conversion change](https://github.com/PCSX2/pcsx2/commit/fc84ade01df0bde5c546c996cf56345c32de955f)
  explicitly replaced generic MPEG conversion with IPU-specific arithmetic.
* [Sony EE User's Manual v6.0, section 8.6.1, printed pages 205–207](https://github.com/ninjadynamics/PS2Docs/blob/a8c2585cb13d7ed5cf4bd4a0e8047aeca23f9369/EE_Users_Manual.pdf):
  primary hardware documentation, archived on a third-party host. Gives the fixed
  coefficients, fractional precision, truncation, final rounding and clipping.
  Its green subtraction order differs from PCSX2; see below.
* [ps2tek IPU registers](https://psi-rockin.github.io/ps2tek/#eeipu):
  RGB32/RGB16 output selection, dithering only for RGB16, SGN's wrapped 128 offset,
  and alpha thresholds. It does not provide an alternative programmable RGB matrix.
  None of these controls specifies a small uniform RGB offset. The SHPS alpha plane
  is decoded separately; RGB16 dithering is inapplicable to this RGB32 path.
* [ITU-R BT.601-7, sections 2.5.1–2.5.4](https://www.itu.int/dms_pubrec/itu-r/rec/bt/R-REC-BT.601-7-201103-I!!PDF-E.pdf):
  luma weights, chroma normalisation, digital ranges and quantisation. This explains
  the underlying colour model, not the IPU's exact intermediate precision.
* [DobieStation CSC](https://github.com/PSI-Rockin/DobieStation/blob/master/src/core/ee/ipu/ipu.cpp)
  and [Play! CSC](https://github.com/jpd002/Play-/blob/master/Source/ee/IPU.cpp)
  use full-range floating arithmetic in the inspected implementations. That is
  inconsistent with the limited-range sources above. Inspected, not executed;
  the already rejected full-range trial was not rerun.

For full-range RGB, BT.601's unquantised inverse follows from
`Kr=0.299`, `Kb=0.114`, `Kg=0.587`, luma excursion 219 and chroma excursion 224:

```
R = (255/219)*(Y-16) + (255/224)*1.402*(Cr-128)
G = (255/219)*(Y-16) - (255/224)*(1.402*0.299/0.587)*(Cr-128)
                      - (255/224)*(1.772*0.114/0.587)*(Cb-128)
B = (255/219)*(Y-16) + (255/224)*1.772*(Cb-128)
```

The documented IPU magnitudes are fixed-point values with seven fractional bits:

| Term | BT.601 coefficient | IPU coefficient |
|---|---:|---:|
| Y | 1.164383562 | 149/128 = 1.1640625 |
| Cr → R | 1.596026786 | 204/128 = 1.59375 |
| Cr → G | -0.812967647 | -104/128 = -0.8125 |
| Cb → G | -0.391762290 | -50/128 = -0.390625 |
| Cb → B | 2.017232143 | 258/128 = 2.015625 |

PCSX2 subtracts 128 from each chroma byte, saturates `Y-16` at zero, multiplies,
and arithmetically shifts **each product** right by six. This retains half-unit
precision. It adds those terms, adds one, shifts right once more, and saturates to
0..255. That is exactly the existing `Ssh.cs` arithmetic, including negative shifts.
An ordinary floating BT.601 inverse is not a more faithful implementation of these
hardware approximations, regardless of its TGA score.

## Why flat does not mean lossless

A spatially constant image still undergoes RGB-to-YCbCr sample quantisation before
DC encoding. For a synthetic neutral grey `R=G=B=g`, BT.601's rounded eight-bit
luma is `floor(16 + 219*g/255 + 0.5)` and both chroma bytes are 128. Synthetic
greys **(3,3,3)** and **(4,4,4)** therefore both produce **(19,128,128)**. A
DC-only stream preserves that YCbCr triple, but no inverse of that triple can
recover both RGB sources. This is a mathematical counterexample using the standard,
not a claim to have identified the game's encoder implementation.

The local audit found:

* **121 of 5,695 paired references** have flat RGB: **117 of 121** use compressed
  SHPS types 0x84/0x85, and **4 of 121** are the paletted cloud representations.
* **117 of 117 decoded compressed flat references** have constant Y, Cb and Cr
  planes. This independently checks the managed path; it does not prove the
  original RGB-to-YCbCr conversion was lossless or that the stream contains no AC.
* **0 of 36 distinct constant Y/Cb/Cr groups** have conflicting RGB targets.
  The attempted real-data collision proof found no contradiction. It is not
  evidence that the transform is invertible.
* The exhaustive CSC output test finds **26 of 117 compressed flat targets**
  unrepresentable by the PCSX2 mapping, even with arbitrary alternative input
  triples. Those cannot be repaired while preserving that mapping. The other
  **91 of 117 compressed flat targets** are representable somewhere in its output
  set; this does not assert that the encoder stored those particular triples.

The fitted luma gain and common signed RGB offset in the historical notes are
observations about source-versus-encoded data. They are not hardware coefficient
measurements. No fitted constant was tried or shipped.

## The source-derived trial, and why it was rejected

The only new candidate was the literal green subtraction order in manual section
8.6.1 steps 2, 3 and 7, retaining PCSX2's luma clipping and final rounding:

```
G = clamp((lum - ((104*(Cr-128)) >> 6) - ((50*(Cb-128)) >> 6) + 1) >> 1)
```

It was tested in an external archive of the original checkout,
`c4cbad3529fb425d2d368ec124475f0fdbba4517`, with just that expression changed.
This is distinct from the previously rejected rounding sweeps.
It can alter green by one output byte; red and blue are unchanged. Synthetic input
`(Y,Cb,Cr)=(16,127,128)` distinguishes the two: PCSX2 returns RGB `(0,0,0)`, while
the literal-manual expression returns `(0,1,0)`. A direct RAW8→RGB32 CSC hardware
test with thresholds and SGN disabled would resolve that discrepancy. Neither the
manual's wording nor PCSX2's comment was silently treated as a hardware experiment.

The trial leaves flat exactness at **19 of 121 RGB** and **2 of 121 RGBA**, while
regressing the MPEG-1 compressed population from **3,538 of 5,687 paired images**
within tolerance to **3,461 of 5,687 paired images**. Among all paired images,
RGB MAE improves on **1,281 of 5,695**, worsens on **4,241 of 5,695**, and is
unchanged on **173 of 5,695**. Pass→fail occurs for **79 of 5,695 pairs** and
fail→pass for **2 of 5,695 pairs**. Rejected; not shipped. This score rejects the
patch under the job's gate, not the possibility that the manual describes silicon.

The shift-rounding, final-halving, full-range and floating BT.601 trials in
[ssh.md](ssh.md) were not rerun. Their recorded populations remain the historical
117 compressed flat references; they must not be relabelled as 121-image trials.

## Full score tables, before and after

Tolerance is per-image mean absolute RGB error <=5 and alpha <=1, including RGB
under transparent pixels. There are **5,695 of 5,764 SSH files with partners**;
**69 of 5,764 SSH files** have no partner and are unscoreable, not decoder failures.
Case-insensitive matching includes **4,563 of 5,695 partners** named `.tga` and
**1,132 of 5,695 partners** named `.TGA`. The compressed population below is the
supported MPEG-1-coefficient/IPUM-0x80 population, not the unrelated MP2 audio gate.

| Phase | Population | Within tolerance | Decoded | RGBA exact | RGB mean (image-weighted) |
|---|---|---:|---:|---:|---|
| Before | All SSH | 3,542 of 5,764 | 5,695 of 5,764 | 6 of 5,764 | 4.907867 over 5,695 of 5,764 |
| After | All SSH | 3,542 of 5,764 | 5,695 of 5,764 | 6 of 5,764 | 4.907867 over 5,695 of 5,764 |
| Before | Paired SSH | 3,542 of 5,695 | 5,695 of 5,695 | 6 of 5,695 | 4.907867 over 5,695 of 5,695 |
| After | Paired SSH | 3,542 of 5,695 | 5,695 of 5,695 | 6 of 5,695 | 4.907867 over 5,695 of 5,695 |
| Before | Lowercase `.tga` | 2,809 of 4,563 | 4,563 of 4,563 | 6 of 4,563 | 4.870594 over 4,563 of 4,563 |
| After | Lowercase `.tga` | 2,809 of 4,563 | 4,563 of 4,563 | 6 of 4,563 | 4.870594 over 4,563 of 4,563 |
| Before | Uppercase `.TGA` | 733 of 1,132 | 1,132 of 1,132 | 0 of 1,132 | 5.058112 over 1,132 of 1,132 |
| After | Uppercase `.TGA` | 733 of 1,132 | 1,132 of 1,132 | 0 of 1,132 | 5.058112 over 1,132 of 1,132 |
| Before | MPEG-1 compressed pairs | 3,538 of 5,687 | 5,687 of 5,687 | 2 of 5,687 | 4.793167 over 5,687 of 5,687 |
| After | MPEG-1 compressed pairs | 3,538 of 5,687 | 5,687 of 5,687 | 2 of 5,687 | 4.793167 over 5,687 of 5,687 |
| Before | Paletted pairs | 4 of 8 | 8 of 8 | 4 of 8 | 86.445311 over 8 of 8 |
| After | Paletted pairs | 4 of 8 | 8 of 8 | 4 of 8 | 86.445311 over 8 of 8 |

Before and after: flat RGB exact **19 of 121**, flat RGBA exact **2 of 121**.
Alpha means before and after are **0.197901 over 5,695 of 5,764 SSH files**,
**0.147497 over 4,563 of 4,563 lowercase pairs**, and **0.401078 over 1,132 of
1,132 uppercase pairs**. For compressed pairs the alpha mean is **0.046773 over
5,687 of 5,687**; for paletted pairs it is **107.631050 over 8 of 8**.

| Rejected literal-manual trial population | Within tolerance | Decoded | RGBA exact | RGB mean (image-weighted) |
|---|---:|---:|---:|---|
| All SSH | 3,465 of 5,764 | 5,695 of 5,764 | 6 of 5,764 | 4.953801 over 5,695 of 5,764 |
| Paired SSH | 3,465 of 5,695 | 5,695 of 5,695 | 6 of 5,695 | 4.953801 over 5,695 of 5,695 |
| Lowercase `.tga` | 2,742 of 4,563 | 4,563 of 4,563 | 6 of 4,563 | 4.918179 over 4,563 of 4,563 |
| Uppercase `.TGA` | 723 of 1,132 | 1,132 of 1,132 | 0 of 1,132 | 5.097392 over 1,132 of 1,132 |
| MPEG-1 compressed pairs | 3,461 of 5,687 | 5,687 of 5,687 | 2 of 5,687 | 4.839166 over 5,687 of 5,687 |
| Paletted pairs | 4 of 8 | 8 of 8 | 4 of 8 | 86.445311 over 8 of 8 |

All alpha means and flat counts in this rejected trial equal the before/after
values above. The four paletted cloud references use a different stored channel
representation, documented in [ipu.md](ipu.md); a CSC change does not affect them.

## Independent checks and reproduction

The original scorer command was run before any edit. Its complete output from
`Case-insensitive partners:` onward matches the final output, including **5,764
of 5,764 per-SSH records**. An external RGBA snapshot captured before edits also
matches **5,695 of 5,695 paired decodes byte-for-byte** after the helper extraction;
**0 of 5,695 paired images** differ. This protects the whole MPEG-1 population,
not just tolerance counts. The RGBA differential's deliberate byte flip reduces
exactness to **5,694 of 5,695 paired images** and exits 2 as required.
Existing codec vectors plus the new exhaustive colour
hash pass **2,164 of 2,164 synthetic assertions** on Linux arm64.

`tools/ssh-colour-check.py` downloads the pinned PCSX2 source, verifies its SHA-256,
and compiles its **unmodified `yuv2rgb_reference` function** with minimal data
structure stubs. A synthetic driver generates RGB for every byte-valued Y/Cb/Cr
input. C# compares each triple against the actual production helper:

* **16,777,216 of 16,777,216 synthetic input triples** match, maximum channel error 0.
* A deliberate output-byte flip makes **16,777,215 of 16,777,216 synthetic triples**
  match, maximum channel error 1; the comparator exits 2 and the harness requires it.
* The complete synthetic RGB table's SHA-256 is
  `65af830bd857fe6f4355a32a12358fab5e83393637f6656c71222d341c977c22`.
  The normal `--self-test` uses that independent hash without downloading anything.

Commands (choose a new external output directory for each reference build):

```sh
dotnet run --project tools/TPW.PS2.SshScore -c Release -- /home/ec2-user/tpw-ps2/sshfull
python3 tools/ssh-colour-check.py /home/ec2-user/colour-research/new-csc-reference
dotnet run --project tools/TPW.PS2.SshScore -c Release -- --self-test
dotnet run --project tools/TPW.PS2.SshScore -c Release -- --audit-flat /home/ec2-user/tpw-ps2/sshfull
```

The real scorer intentionally exits 2 because the tolerance gate fails for some
pairs. The flat audit exits 0 if its reads succeed; that is **not** a declaration
of exact textures. It prints the failures and representability counts separately.

This run's logs, JSON, reference builds and pre-edit snapshots are outside Git at
`/home/ec2-user/colour-research/`. An initial reference-table build and verification
were interrupted by a full `/tmp`; those incomplete runs were not counted. They
were rerun successfully on the larger filesystem. No images, disc buffers, PCSX2
source, or extracted colour tuples are committed. The only golden data in the
tests is a hash of the entirely synthetic reference output.

## Why 26 of them are unreachable: the conversion reaches 17.66% of the colour space

The 26 unreachable flat references are not a curiosity about those 26. Brute-forcing the whole input
space through `ConvertIpuRgb` — all 256³ (Y,Cb,Cr) triples, independently of the audit above — the
conversion emits only

    2,962,391 distinct RGB triples out of 16,777,216   =  17.66%

The integer form is why: each chroma product loses six fractional bits to a `>> 6`, the sum keeps one
and is rounded away by `+1 >> 1`, and all three channels share one luma term. The output is a sparse
lattice, and **82.34% of RGB space cannot be produced by this conversion at all, for any input.**

⚠ **That reframes "flat RGB exact 19 of 121" and it is worth stating plainly, because this document
originally proposed that count as the ungameable acceptance gate.** It is ungameable — no coefficient
fit can raise it dishonestly — but it is also **partly unattainable by construction**: a flat
reference whose colour lies off the lattice can never be matched exactly, no matter how correct the
decoder is. 26 of 117 measured here are in exactly that position. So the count is substantially a
measure of how many reference colours happen to land on the lattice, not of decoder quality.

A gate can be honest and still be measuring the wrong thing. The mistake was mine: I reasoned
"flat source -> constant YUV -> nothing lost -> must be exactly recoverable", and the middle step
does not follow. Constant YUV means the encoder kept DC only; it says nothing about whether the
*quantisation* of that DC was lossless. It was not, for 26 of them.

**Method, so it can be re-run:** enumerate Y in 0..255 and Cb, Cr in 0..255, push each through the
same integer arithmetic the decoder uses, and collect the distinct packed RGB values. Then test each
flat reference colour for membership. Independent of the PCSX2 harness above — it needs nothing but
the conversion itself — and it returned the same 26, which is why both are recorded.
