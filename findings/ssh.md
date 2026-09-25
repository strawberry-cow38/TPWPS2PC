> Historical FFmpeg-adapter research. The shipping path is now managed; see
> [the IPU differential report](ipu.md) for current API, requirements, and whole-disc results.
> The flat-colour losslessness claim below was rejected by the
> [source-based CSC audit](ssh-colour.md): the existing conversion matches PCSX2
> on all 16,777,216 of 16,777,216 byte-input triples, and 26 of 117 compressed flat
> reference colours are unrepresentable by that fixed conversion at any input.

# SHPS / GM texture decoder — 2026-09-21

Implemented in `core/TPW.PS2.Data/Ssh.cs`, with no Godot dependency: SHPS directory validation,
compressed types **0x84 and 0x85**, an adapter to **FFmpeg's existing IPU decoder**, column-major
macroblock placement, the packed 8x8 RGB layout, IPU integer colour conversion, and separate alpha.
No coefficient decoder was written. No images, extracted buffers, or fixture bytes are in the repo.

**Score: 254 of 306 lowercase partners; 322 of 400 total partners within tolerance.**
Tolerance is **per-image mean absolute RGB error <=5 and alpha error <=1**, on 0..255 channels.
This includes RGB under transparent pixels. **400 of 400 decode; 2 of 400 are RGBA byte-exact.**
Image-weighted mean RGB error is **3.741870 over 400 of 400**. This is a working lossy decoder,
not an assertion that every decoded texture equals its source TGA. See the remaining work below.

| Population | Within RGB MAE 5 / alpha MAE 1 | Mean RGB error | Mean alpha error |
| --- | ---: | ---: | ---: |
| All pairs | 322 of 400 | 3.741870 over 400 | 0.028618 over 400 |
| Lowercase `.tga` partners | 254 of 306 | 3.575180 over 306 | 0.031844 over 306 |
| Uppercase `.TGA` partners | 68 of 94 | 4.284498 over 94 | 0.018118 over 94 |

All **400 of 400** have alpha MAE <=1. At RGB MAE <=10, **376 of 400** pass; at RGB MAE <=20,
**400 of 400** pass. These additional thresholds do not change the default 322-of-400 result.

Failed approaches, with numbers: the earlier **504 ordinary MPEG elementary-stream wrappers**
had best mean error **79/channel** (recorded in `formats.md`, not rerun). Feeding the correctly
adapted IPU output into raster-order placement gave **214 of 400** at RGB MAE <=5, mean **9.640308**.
8x8-block column placement gave **108 of 400**, mean **18.762959**. Vertical flip gave **113 of 400**,
mean **22.840262**. Whole-image transpose gave **103 of 400**, mean **22.017157**.

## Step zero: what already decodes it?

The library/tool search happened before decoder code was written.

* [FFmpeg IPU decoder](https://github.com/FFmpeg/FFmpeg/blob/n7.0.2/libavcodec/mpeg12dec.c)
  (`ipu_decode_frame`), [IPUM demuxer](https://github.com/FFmpeg/FFmpeg/blob/n7.0.2/libavformat/ipudec.c),
  and [parser](https://github.com/FFmpeg/FFmpeg/blob/n7.0.2/libavcodec/ipu_parser.c): **used**.
  The installed FFmpeg 7.0.2 advertises `ipu`; this is distinct from `mpeg1video` / `mpeg2video`.
* [Game Extractor's SHPS reader](https://github.com/wattostudios/GameExtractor/blob/1d3201e2fff3ba3a7fd0f72b12cb9d0738a89e6a/src/org/watto/ge/plugin/viewer/Viewer_BIG_BIGF_FSH_SHPI.java):
  inspected its dispatch. It does not accept 0x84/0x85; its compressed image branch is RefPack
  type 251. This was source inspection, not a 400-image runtime trial.
* [Rainbow](https://github.com/marco-calautti/Rainbow): its published supported-format list contains
  TIM2/TX48/DAT/NUT/TPL/EFX, not SHPS/GM.
* [ImageMagick format list](https://imagemagick.org/script/formats.php): no SHPS/GM decoder found.
* Noesis searches found game-specific SHPS palette/swizzle scripts, but no verified GM decoder.
  Noesis was not executed, so this is **not** a claim that every Noesis plugin was ruled out.
* Python SHPS implementations found during the search cover later paletted formats; none was
  established to support GM. [IPUenc](https://github.com/ravenDS/IPUenc) was another existing lead;
  FFmpeg already supplied the needed codec, so no port of IPUenc was attempted.

The [PCSX2 IPU implementation](https://github.com/PCSX2/pcsx2/blob/650d7048561756cdbfea60ee65282074a38eefa1/pcsx2/IPU/IPU_MultiISA.cpp)
and [register field documentation](https://psi-rockin.github.io/ps2tek/#eeipu) were checked as well.
PCSX2 starts IDEC at macroblock syntax, without parsing a picture or slice header. Its
[reference colour conversion](https://github.com/PCSX2/pcsx2/blob/650d7048561756cdbfea60ee65282074a38eefa1/pcsx2/IPU/yuv2rgb.cpp)
identifies the integer BT.601 arithmetic used here. No PCSX2 code or tables were vendored.

## Why the earlier MPEG experiment failed

**The rejected claim that GM contains ordinary MPEG intra pictures remains rejected.**
GM contains a raw IPU macroblock stream, with MPEG-1 coefficient syntax. FFmpeg's dedicated
IPU codec consumes that syntax directly. There are no picture headers or numbered slice headers
to locate. It is legitimate for one IDEC stream to contain multiple macroblock rows.

The distinguishing evidence is a deterministic adapter that decodes **400 of 400 images**,
with the measured source-image errors above. No parameter/offset sweep was needed:

1. Read the verified eight-byte GM header. Bits 0..19 of its final word are the GM length.
2. Use its byte-sized macroblock dimensions multiplied by 16.
3. Isolate the coefficient bytes between GM+8 and its `00 00 01 34` terminator.
4. Prefix one IPU flags byte, `0x80`: MPEG-1 coefficient syntax, other IPU codec flags zero.
5. Translate the terminator to IPUM's `00 00 01 b0`; remove GM alignment padding and alpha.
6. Prefix a 16-byte `ipum` transport header with byte length, u16 width/height, and frame count 1.
7. Decode as `-f ipu`, retaining YUV420P for the IPU colour conversion.

The compressed coefficients themselves are unchanged. FFmpeg verifies that it consumed the
requested macroblocks and that exactly four terminator bytes remain. Decode errors are failures,
not blank images. End-marker detection is bounded by the alpha plane and the final padding
quadword; embedded start-code-like bytes are not blindly treated as frame boundaries.

## Newly measured details

The original "every entry is 0x84" observation did not cover the alpha population:

| Entry / header property | Fixture count |
| --- | ---: |
| 0x84, GM flags `0x00800000`, RGB | 331 of 400 |
| 0x85, GM flags `0x08800000`, RGB + alpha | 69 of 400 |
| SHPS 8x8, GM coded 16x16 | 3 of 400 |
| Flat RGB source | 63 of 400 |

GM bit 27 enables an uncompressed alpha plane. It occupies the final `codedWidth * codedHeight`
bytes of the declared GM block. It starts after the coefficient terminator and alignment to 16
bytes **relative to GM**, and is in ordinary image-row order. Alpha values represent 0..128;
convert to RGBA8 with `min(255, alpha * 2)`.

RGB macroblocks traverse columns, while the pixels inside each 16x16 macroblock traverse rows.
This is confirmed from the game's own uploaders (`0x223fe0` immediate IDEC->VRAM, `0x224660` and
`0x236768` `texssh::generategifchain`): each IMAGE transfer is a **16-wide column strip** of the
entry's full height at `DSAX = 16 * strip`, which is exactly this placement.

## Sub-macroblock images: an ENCODER artefact, and the game never displays them

An entry with a dimension under 16 does not follow either rule above, and the two planes do not
even follow the *same* rule. There are **14** such entries on the disc: six 8x8, and the eight
`UI.WAD/laptop/` tiles at 8x32, 32x8 and 16x8.

⭐⭐ **THE GAME REFUSES ALL OF THEM.** `ctex_ssh::load` at `0x235d68` is the executable's only
SHPS parser — it holds the sole reference to the `"SHPS"` string at `0x36fd90`; the other `"SHPS"`
at `0x2f0c80` is an embedded splash fed straight to the IPU by the boot routine at `0x12d268`. It
walks every directory entry and rejects the **whole file** if any entry is under 16 in either
dimension:

```
if (entryWidth < 0x10 || entryHeight < 0x10) {
    printf("*** ERROR ctex::load - mipmap too small (%d,%d)\n", w, h);   ; 0x36fe20
    <destroy texture>; return 0;
}
```

So there is no in-game rendering to match, the engine **cannot** settle this layout, and the
encoder's bytes are the only authority — the source art it was given is the correct target. It
also means these are **dead assets**: eleven of the 62 laptop files are unloadable on this build,
so the laptop's inner nine-slice is not drawn from `L_edge*`/`L_fill` and a port should not
reinstate them.

**RGB** — the `W*H` source pixels sit **contiguously** in the coded raster (image pixel `p` =
coded pixel `p`), with stale encoder memory after them. Cropping a `W x H` rectangle is wrong.

**Alpha** — a *different* artefact: the encoder copied `codedWidth` bytes per coded row starting
at `src + y*W`, so the image is the **top-left crop at the coded stride** and the columns past it
are literally the next source row. The existing `alphaOffset + y*codedWidth + x` expression is
already this; the mechanism is what was previously unstated.

| claim | evidence | status |
| --- | --- | --- |
| 8x8 RGB contiguous | 3 TGA controls: MAE **10.0 / 4.1 / 5.7** against **50.6 / 36.5 / 34.9** for a crop, whose rows 4-7 score 60-129 (the stale memory) | measured |
| 8x8 alpha = crop at coded stride | `AWARD_T_8` alpha MAE **0.0**; contiguous scores **17.3** | measured |
| the alpha row rule, all 8-wide files | coded row `y` cols 8..15 equal row `y+1` cols 0..7 **exactly** — 248 of 248 pairs per 8x32 file, 120 of 120 per 8x8 — and the byte after `src[W*H]` is `0x44` in **all ten** sub-16 alpha files, at the coordinate the rule predicts | read from the bytes |
| 8x32 RGB contiguous | **no TGA exists.** The laptop edges are one bevel in two orientations: read contiguously, `L_edge1`/`L_edge3` match the **32x8** `L_edge2`/`L_edge4` profile (which have no layout choice) at luma MAE **0.72 / 3.31**; a crop scores 34 / 44; a control pairing of unrelated tiles scores 31 | **corroborated, NOT TGA-proven** |
| 32x8, 16x8 | single macroblock row, so contiguous and crop are the same expression | no choice exists |

⚠ The guard admits only a sub-16 dimension of **8**, because 8 is the only one on this disc.
Shapes like 8x16 or 8x64 are unmeasured and are refused rather than guessed at.

The supplied source set contains **399 Targa images and 1 PNG named `.tga`**. The scorer detects
that PNG signature and uses FFmpeg's existing PNG decoder. It is included in the 306 subset.

## Flat colours and precision: still report the failures

Only **2 of 63 flat-colour references are exact**, with RGB MAE **1.322751 over 63 of 63**.
All 63 are within RGB MAE 3, but a small error is not zero. `GEEN_BASE` is (28,142,27) in the
source; FFmpeg returns constant Y/Cb/Cr (99,96,87), and IPU integer conversion yields (31,142,32):
**MAE 2.666667, maximum channel error 5**. No per-texture correction or reference-image fallback
is present in the decoder. The residual RGB rounding/encoder behaviour has not been fully traced.

There is independently demonstrable information loss in alpha: in the same paired texture,
source values **123 and 124 both encode as 62**, and **121 and 122 both encode as 61**.
No inverse can recover both original values from that alpha byte alone. This does not justify
ignoring RGB errors; it establishes why byte-exact RGBA reconstruction is not a general promise.

## Running it

Install .NET 8 and an FFmpeg build with the `ipu` demuxer, parser and decoder. Put `ffmpeg` on PATH
or set `TPW_FFMPEG` to the executable path (including `.exe` on Windows). The scorer additionally
needs PNG decoding/Targa encoding for the one mislabeled reference. Native dependencies are
allowed; none is downloaded at runtime or committed. The adapter is portable C# and uses argument
lists and binary pipes, with no shell or temporary image files. Linux arm64 with FFmpeg 7.0.2 was
executed here; Windows and x86 runtime checks remain outstanding.

```sh
dotnet run --project tools/TPW.PS2.SshScore -c Release -- /path/to/pairs \
  --json /path/outside/the/repo/ssh-score.json
dotnet run --project tools/TPW.PS2.SshScore -c Release -- --self-test
```

Exit 0 means every image passed the specified tolerance. Exit 2 means at least one image did not;
exit 1 is a command/setup error. The default fixture run intentionally exits **2**. `--tolerance`
and `--alpha-tolerance` can be specified explicitly; the scorer prints their values.

Matching is recursive and case-insensitive for both stem and extension, scoped by relative folder.
Missing partners, ambiguous case-folded partners, malformed images, and dimensions mismatches stay
in the denominator. Per-image results contain RGB/alpha MAE, maximum errors, exact/flat status,
and errors. Summary means are image-weighted and always state the decoded population.

The core API is `new Ssh(bytes, entryIndex: 0, ffmpegPath: null)`, exposing `Width`, `Height`,
`Pixels` and `HasAlpha`; `Ssh.ReadEntries(bytes)` inspects the directory without starting FFmpeg.
Only the selected base image is decoded; trailing metadata/mipmap blocks are not exported.

Validation: the Release build completed with zero warnings/errors; **2,079 of 2,079 synthetic
assertions** passed. The checks exercise rectangular macroblock placement, linear alpha, 8x8 alpha
stride, reversed multi-entry directories, exact/error metric denominators, missing/ambiguous
case-insensitive pairs, malformed containers, and rejection of invalid coefficient streams.
Synthetic streams are generated from DC syntax in memory, not copied from the game.

## Remaining work

* **78 of 400** images exceed the default RGB tolerance. Characterise encoder quantisation,
  chroma loss and colour rounding against a captured hardware decode before claiming exactness.
* Fixtures cover only 8x8, 16x16, 16x32 and 32x32 images. Larger/multiple-entry files need real-disc
  validation; the available RAR could not be opened and no extracted disc was available here.
* No claim is made that the **473 SSH-only material references** now render. Viewer integration
  and runtime dependency distribution remain with the viewer work; `game/Viewer.cs` was untouched.
* Unseen GM flag combinations, other SHPS pixel types and mipmap export are explicitly unsupported.
* One FFmpeg process is started per image. Callers should cache decoded textures; batching or a
  libavcodec binding is a possible later optimisation.

Research sources and decoded buffers stayed under `/home/ec2-user/tpw-ps2/ssh-research/`.
Full per-image reports can be regenerated from the standalone scorer and the external fixtures.

## Real-disc validation: the 400-image fixture set was not representative

Run on 2026-09-21 against the retail disc (`Theme Park World (Europe) (En,Fr,De)`, 621,734,736 B),
every `.ssh` and `.tga` extracted from all 16 WADs by an independent Python walker, no exclusions.
This is the "larger/multiple-entry files need real-disc validation" item above, now done.

| population | within tolerance | decoded | mean RGB error |
|---|---|---|---|
| 400-image fixture set | **322 of 400  (80.5%)** | 400 of 400 | 3.741870 |
| whole disc | **3,538 of 5,695  (62.1%)** | 5,687 of 5,695 | 4.793167 |

**The fixture set flattered the decoder by 18 points.** Nothing about the decoder changed between
those two rows; only the population did. Quote the disc number, and state the denominator with it.

Denominators, since three different ones are in play: the disc holds **5,764 `.ssh`** and **5,695
`.tga`**. Every `.tga` has an `.ssh` partner -- **zero orphans in that direction** -- and **69 `.ssh`
have no `.tga`**, so they are unscoreable and are not the 5,695. Extension case: 4,563 `.tga` against
1,132 `.TGA`, so a case-SENSITIVE match silently drops **19.9%** of the disc, the same trap the
fixture set carried at 23.5%.

### The 8 decode failures are one thing, and it is not this decoder

| | |
|---|---|
| `FANTASY/Sky/Fantasy_back`, `Fantasy_front2` | SHPS type **0x02**, not 0x84/0x85 |
| `HALLOW/Sky/Hallow_back`, `Hallow_front2` | same |
| `JUNGLE/Sky/Jungle_back`, `jungle_front2` | same |
| `SPACE/Sky/Space_back`, `space_front2` | same |

All 8 are sky textures and all 8 are SHPS type 0x02 -- an uncompressed/paletted type this reader
correctly refuses rather than guessing at. **The `.tga` side agrees independently**: those same 8
stems are the only paletted TGAs on the disc. The four `*_back.tga` are correctly declared paletted
(cmaptype 1); the four `*_front2.tga` declare **cmaptype 0, imgtype 2 (true-colour) at 8bpp**, which
cannot exist, and then carry 1,024 bytes of 32-bit palette + 65,536 indices + 2 trailing bytes. A
standards-compliant reader (PIL 11.3) refuses all four; a reader that switches on bpp gets them right.

Also confirmed from the disc: `/DATA/UI.WAD/UltimateC/Star.tga` opens `89 50 4E 47 0D 0A 1A 0A`
`IHDR` -- it is a **PNG** under a `.tga` name, 32x32 RGBA. `ReferenceImage` already sniffs the
signature rather than the extension, which is why it stays in the scored population.

### Failure is systemic, not one world's art

Pass rate by WAD: FRONTEND 90%, PARTICLE 93%, HALLOW 73%, LOBBY 67%, DATA 60%, SPACE 59%,
JUNGLE 55%, UI 54%, FANTASY 50%. Spread but nowhere near clean anywhere, so the residual is the
codec's own colour/quantisation behaviour and not a per-world asset pipeline.

Flat-colour exactness on the disc: **19 of 117** RGB-exact (the fixture set's 2 of 63 was the
small-sample version of the same defect).

⚠ That denominator moved after this was written. Commit `15730ca` fixed the four cloud-mask TGAs,
and a pure-white reference IS flat, so the flat population became **121** and the line now reads
19 of 121. The numerator did not move -- no flat image became exact -- but a denominator that
changes because a DIFFERENT bug was fixed is exactly the kind of drift that makes an old number
look like a regression later. The 117-sample analysis below stands as measured: all 117 were
type 0x84/0x85 and went through the YUV path, and the four newcomers are type 0x02 and do not.
The earlier inference that a flat block must round-trip was incorrect: RGB-to-YCbCr sample
quantisation can lose information before DC encoding. See [the CSC audit](ssh-colour.md).

### Teeth-check on the scorer

The scorer was verified to be capable of failing before its pass was believed. Reverting the single
placement line to raster order -- `mb = (y/16)*(codedWidth/16) + x/16` -- and rebuilding drops the
fixture score from **322 to 215** and the mean error from **3.741870 to 9.384956**, independently
reproducing the **214 / 9.640308** this document already records for that hypothesis. The pass is
therefore a measurement and not a rubber stamp.

### Standing caveat on the dependency

`Ssh` starts one **`ffmpeg` child process per image** through `Process.Start`. That is an external
executable on PATH, not a shipped library: a player without FFmpeg, or with a build lacking the
`ipu` demuxer/parser/decoder, gets no textures at all. 5,695 textures is 5,695 process spawns. The
format finding (GM is an IPU macroblock stream in MPEG-1 coefficient syntax, macroblocks in column
order, alpha a separate linear 0..128 plane) is the durable part and is what a managed decoder would
be written from; the FFmpeg adapter is scaffolding that proves it, not a shipping path.

## Historical flat-colour trials: measurements retained, losslessness inference rejected

117 compressed flat-source pairs on the disc, 19 of 117 byte-exact. Every one decodes to a
**constant** Y/Cb/Cr -- checked, 117 of 117. These measurements used Python through the FFmpeg
IPU path, independently of the C# conversion. The original inference that this proves DC-only
encoding with no relevant information loss was wrong: spatial constancy proves neither lossless
RGB-to-YCbCr sample quantisation nor the absence of AC coefficients in the stream. These are
source-versus-decoded observations, not exact hardware input/output measurements. The subsequent
[CSC audit](ssh-colour.md) independently verifies the conversion and explains the limitation.
The historical trials below were not rerun for that audit.

**Rounding is not the cause, which was the obvious hypothesis and it is dead:**

| shift | final | exact of 117 |
|---|---|---|
| `>> 6` | `(v+1) >> 1` (current) | 19 |
| `(v+32) >> 6` | `(v+1) >> 1` | 19 |
| `>> 6` | `(v+1) / 2` | 19 |
| either | `v >> 1` | 1 |

Rounding the shifts changes nothing. Nor is it a flooring bias: G accumulates two floored shifts to
R and B's one, so a flooring cause predicts G biased low -- and the commonest signed error is
`(+1,+1,+1)` on **36 of 117**, uniform across all three channels, which is a LUMA offset and cannot
come from the chroma shifts at all.

**Exact float BT.601 nearly doubles it: 35 of 117** (studio-to-full swing; full-range scores
0 of 117). This is closer to the TGA targets on this population, but does not establish greater
hardware accuracy: the IPU has an explicitly specified integer approximation.

Least squares over the 117 samples, fitting reference RGB from the decoded YUV:

    R = 1.1702*(Y-16) + 1.6016*(Cr-128) + 0.0066*(Cb-128) - 1.801     max resid 2.46, mean 0.75
    G = 1.1704*(Y-16) - 0.8232*(Cr-128) - 0.4039*(Cb-128) - 1.164     max resid 1.01, mean 0.18
    B = 1.1709*(Y-16) - 0.0053*(Cr-128) + 2.0444*(Cb-128) - 1.906     max resid 3.07, mean 0.77

The cross terms come out at 0.006 and -0.005, i.e. zero, which says the model shape is right. The
luma coefficient is **1.170 on all three channels** against BT.601's 1.164, the chroma coefficients
are each a little larger than BT.601's, and there is a **constant of about -1.5 on every channel** --
which is precisely the `(+1,+1,+1)` mode seen directly. Equivalently the black level sits nearer
17.3 than 16.

⚠ **This is deliberately NOT implemented.** Fitting a coefficient to make a number go up is how you
patch the wrong constant: the right form has to come from what the PS2's IPU and this encoder
actually specify, and a fit that improves 19 to something higher on the data it was fitted to has
proven nothing. What the fit DOES establish is the shape of the remaining error and its size --
max 5 today, and no linear model can do better than about 3 -- so whoever takes this has a bound to
beat and a reason to look at the hardware documentation rather than at the residuals.

## SHPS type 0x02 solved: the skies decode, and the last 8 failures are gone

Eight entries on the whole disc are type **0x02**, uncompressed, and all eight are skies:
`{FANTASY,HALLOW,JUNGLE,SPACE}/Sky/*_{back,front2}.ssh`, every one 256x256. Layout:

    entry+0x00   16-byte header: type, 24-bit length, u16 W, u16 H   (as every SHPS entry)
    entry+0x10   W*H bytes of 8-bit palette indices, top row first, NOT swizzled
    entry+len    a SECOND entry: type 0x21, length 1,040, dimensions 256x1 -- the palette

⭐ Those 16 bytes are not padding to skip -- they are a real SHPS entry header, and every field in
it validates: type 0x21, 24-bit length 1,040 (16 + 1,024), width 256, height 1. A CLUT stored as a
256x1 image. The SHPS **directory lists only one entry**, so the palette is a trailing block the
directory never names -- which is why it reads as "16 mystery bytes" until you parse them as what
they are. Credit where due: this reading came from the managed-decoder run, and it is better than
the offset-arithmetic version it replaced, because it explains the 16 rather than stepping over it.

⚠ **The PALETTE is PS2 CSM1-swizzled** -- 8 blocks of 32 entries, entries 8..15 swapped with 16..23
within each block -- **and alpha is 0..128**, doubled on output. The index plane is NOT swizzled.

Both of those have a discriminator rather than an argument, measured against the four `*_back.tga`
partners, which are the disc's only *declared* paletted TGAs and therefore a known answer:

| variant | bytes matching the reference |
|---|---|
| swizzled CLUT, alpha x2 | **262,144 of 262,144 -- exact, all four files** |
| swizzled CLUT, alpha as stored | 196,608 of 262,144 = **exactly 75%** |
| CLUT read straight through | 146,115 - 197,110 (55-75%) |

The 75% row is the useful one: three bytes in four, RGB right and alpha wrong, which is what a
single-channel error looks like when you print the number instead of eyeballing the picture.

Through the real scorer, the four `*_back` pairs now read `RGB_MAE=0.000000 A_MAE=0.000000`, and
the disc total goes **decoded 5,687 -> 5,695 of 5,695, RGBA exact 2 -> 6, within tolerance
3,538 -> 3,542**. There are no undecodable images left on the disc.

### The four `*_front2` pairs disagree on purpose, and that is a finding not a bug

They now fail *enormously* -- RGB MAE 158 to 194 -- and that is correct. The `.ssh` palette is a
**pure grey ramp** (R==G==B on all 256 entries) with alpha constant 128, i.e. fully opaque; the
`.tga` palette is **pure white** (255,255,255) with 128 distinct alphas. They carry **the same cloud
mask in different channels**: luminance on the PS2, alpha on the PC. Checked from the palettes
alone, so it does not depend on either reader being right.

They are also not the same picture texel-for-texel (best agreement 23%), and the `*_back` pairs
matching at 100% through the identical code path is what rules out a layout error as the
explanation -- the index plane is demonstrably correct, so the two cloud masks were simply authored
differently. **A scorer will always fail these four**, and it should; they are not a decoder defect
and tuning anything to make them pass would be fitting to a difference that is really there.
