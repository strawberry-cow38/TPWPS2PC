# Managed IPU decoder: differential acceptance

The shipping decoder is pure managed net8.0 C#. `Ssh` passes the GM coefficient
stream directly to `IpuDecoder`; the IPUM wrapper and process implementation are
removed. Container parsing, GM validation/end-marker search, column macroblock
placement, integer colour conversion, and alpha-plane handling are unchanged.
The API is `new Ssh(bytes, entryIndex: 0)`; the executable-path parameter is gone.
The scorer's one PNG reference also uses managed code (`ZLibStream` plus PNG filters).
No new package, native binding, intrinsics, subprocess, or decoder fallback was added.

## Reference and arithmetic

The acceptance reference is the supplied **FFmpeg 7.0.2 AArch64 static executable**,
with the original `Ssh.cs` at **15730ca4f489873fa7029f0cb3c3a005bbc3aaca**.
That commit was the initial `ssh`/`ipu` base. `ssh` advanced independently during
this work; measurements here pin the original commit rather than the moving branch.

Read [ipu_decode_frame](https://github.com/FFmpeg/FFmpeg/blob/n7.0.2/libavcodec/mpeg12dec.c),
[ff_mpeg1_decode_block_intra](https://github.com/FFmpeg/FFmpeg/blob/n7.0.2/libavcodec/mpeg12.c),
the [VLC/quantisation tables](https://github.com/FFmpeg/FFmpeg/blob/n7.0.2/libavcodec/mpeg12data.c),
and the [AArch64 IDCT](https://github.com/FFmpeg/FFmpeg/blob/n7.0.2/libavcodec/aarch64/simple_idct_neon.S).
Only the GM configuration corresponding to IPUM flags `0x80` is supported:
MPEG-1 coefficients, other flags zero. IPU requires address increment one between
macroblocks, accepts intra/intra-with-quantiser, starts the three DC predictors at
128 and qscale at one, and doubles an explicit five-bit linear quantiser.

MPEG-1 oddifies each reconstructed AC magnitude. This reference path stores signed
16-bit coefficients without a separate 12-bit clamp; MPEG-2's block-parity toggle
does not apply. The scalar IDCT preserves 32-bit wrapping, row shift 11 with bias
1024, signed 16-bit row narrowing, signed 16-bit column-input bias 32, column shift
20, and byte saturation. **AArch64 does not use the x86/C DC-row shortcut.** These
operations are fixed on all target architectures, without CPU dispatch or floating
point. Linux arm64 was executed; Windows and x86 runtime execution remains untested.
The adapted files carry LGPL-2.1-or-later notices and the license text.

## Counts before adding type 0x02

Case-insensitive pairing found **5,695 of 5,764 SSH files** with partners:
**4,563 of 5,695** lowercase `.tga`, **1,132 of 5,695** uppercase `.TGA`.
The historical decoder produces RGBA for **5,687 of 5,695 scoreable pairs**;
**8 of 5,695** are unsupported type `0x02`. They are errors, not identical images.

Managed versus historical RGBA: **5,687 of 5,695 scoreable pairs byte-identical**,
or **5,687 of 5,687 reference-decodable pairs**. Different images: **0 of 5,687**.
Unchanged unsupported errors: **8 of 5,695**. Unexpected failures: **0 of 5,695**.
Every buffer byte was compared, not a similarity score or only a checksum.

Both real scorer runs produce the following, and their complete per-image JSON
objects are equal for **5,764 of 5,764 SSH records**:

| Population | Within tolerance | Decoded | RGBA exact | Image-weighted RGB mean |
|---|---|---|---|---|
| All SSH | 3,538 of 5,764 | 5,687 of 5,764 | 2 of 5,764 | 4.793167 over 5,687 of 5,764 |
| Lowercase `.tga` | 2,805 of 4,563 | 4,557 of 4,563 | 2 of 4,563 | 4.794980 over 4,557 of 4,563 |
| Uppercase `.TGA` | 733 of 1,132 | 1,130 of 1,132 | 0 of 1,132 | 4.785856 over 1,130 of 1,132 |

**Flat RGB exact is 19 of 121 flat references in both runs.** The requested
19 of 117 predates commit `15730ca`'s Targa cloud-mask correction. That correction
makes these **4 of 121 flat references** white RGB with varying alpha:
`FANTASY/Sky/Fantasy_front2.ssh`, `HALLOW/Sky/Hallow_front2.ssh`,
`JUNGLE/Sky/jungle_front2.ssh`, `SPACE/Sky/space_front2.ssh`.
The scorer marks `Flat` from the TGA before attempting SSH decoding, so those four
enter the flat denominator even while SSH type `0x02` is unsupported. The untouched
legacy scorer itself prints 19 of 121. No population adjustment was made to obtain 117.

## Rejected attempt and instrument check

The first scalar version reproduced x86's DC-row shortcut and signed-word saturation.
It decoded **5,687 of 5,695 scoreable pairs**, but only **3,970 of 5,695 scoreable
pairs** / **3,970 of 5,687 reference-decodable pairs** were RGBA-identical;
**1,717 of 5,687** differed. Scorer tolerance stayed **3,538 of 5,764**, while RGB
mean changed to **4.793190 over 5,687 of 5,764**. This is why tolerance is insufficient.
Worst measured difference, using zero-based RGBA byte offsets:

```
DATA/Generic/Advisor/textures/Ticket_Band.ssh
712 of 16,384 RGBA bytes differ
first offset 4096: reference 231, managed 234
maximum offset 4096: reference 231, managed 234; absolute difference 3
```

After fixing that arithmetic, `--perturb` deliberately flips output byte zero of
one image. Exactness drops to **5,686 of 5,695 scoreable pairs** /
**5,686 of 5,687 reference-decodable pairs**, and the comparison exits 2:

```
DATA/Chars/Dino/dino.ssh
1 of 65,536 RGBA bytes differs
first and maximum offset 0: reference 255, managed 254; absolute difference 1
```

The decoder tests include independently generated synthetic MPEG-1 AC vectors,
both extended escapes, negative levels, a last-position AC, quantiser changes,
all truncated vector prefixes, run overflow, malformed VLCs, trailing stream bytes,
all PNG filters and split IDAT, plus existing placement/alpha/container checks.
**2,139 of 2,139 synthetic assertions** pass without FFmpeg.

## Reproduction and proof of no FFmpeg execution

Run the complete two-build harness (Python orchestrates; no historical process
code is retained in the managed project):

```sh
python3 tools/ssh-differential.py /home/ec2-user/tpw-ps2/sshfull \
  /tmp/new-ipu-comparison --ffmpeg /home/ec2-user/.local/bin/ffmpeg
```

It archives the pinned historical source outside Git, compiles the same RGBA
instrument against each Data project, captures every reference decode/error, checks
input hashes and the complete pairing population, compares bytes, runs the deliberate
perturbation, then compares real scorer JSON records. New type `0x02` decodes are
reported separately. All reports and derived buffers stay outside the repository.

Actual managed scoring command, also used with `strace`:

```sh
env TPW_FFMPEG=/nonexistent/ffmpeg PATH=/usr/bin:/bin \
  dotnet run --project tools/TPW.PS2.SshScore -c Release -- \
  /home/ec2-user/tpw-ps2/sshfull

env TPW_FFMPEG=/nonexistent/ffmpeg PATH=/usr/bin:/bin \
  strace -f -e trace=execve -o /tmp/tpw-ipu-validation/managed-execve.txt \
  dotnet run --project tools/TPW.PS2.SshScore -c Release -- \
  /home/ec2-user/tpw-ps2/sshfull
```

The trace contains **0 FFmpeg executions of 2 successful execve calls**: `/usr/bin/dotnet`
and the scorer executable. `UI/UltimateC/Star.tga` remains in the score using the
managed PNG reader. External initial-run evidence is in `/tmp/tpw-ipu-validation/`.
