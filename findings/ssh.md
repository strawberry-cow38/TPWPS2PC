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
An 8x8 texture uses the first 64 **consecutive RGB pixels** of a decoded 16x16 buffer; cropping
an 8x8 rectangle is wrong. For the three small images, cropping scored **36.536458, 34.911458,
50.578125** RGB MAE; packing consecutive pixels scores **4.098958, 5.744792, 10.041667**.
The one 8x8 alpha texture retains the **16-pixel alpha row stride**, independently of RGB.
Flattening its alpha gave **17.343750** mean alpha error; the coded stride gives **0**.

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
