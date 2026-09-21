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

## Type 0x02: derivation and final scorer

The smaller job follows the independently validated IPU replacement. During this
work, `ssh` acquired commit `ed581af5b6f9467e7cbba9ea865e8101027ff809` implementing
the same palette interpretation. It was reviewed alongside the independent byte
comparisons below. `ipu` keeps its original base and implements additional palette
header and entry-boundary checks; it does not merge the moving `ssh` branch.

All **8 of 8 type 0x02 entries** have a 16-byte image header, 256x256 linear
8-bit indices, and declared image length 65,552. Immediately following that extent
is a type `0x21` block, declared length 1,040, width 256, height 1: a 16-byte header
and 256 RGBA palette entries. Subsequent type `0x70` name metadata is not palette
data. Its length varies; a fixed trailing size is not assumed.

For the **4 of 8 type 0x02 pairs** named `*_back`, all **65,536 of 65,536 indices
per image** match the TGA after converting its bottom-origin rows to top-origin.
Thus the index plane is linear. Exchanging index bits 3 and 4 in the stored CLUT
matches **256 of 256 RGB palette entries per image**. Doubling and clamping palette
alpha gives **262,144 of 262,144 RGBA bytes per image**, or **1,048,576 of 1,048,576
RGBA bytes across the four back-sky controls**. Those observations determine the
layout; neither filenames nor matching a desired tolerance choose a pixel formula.

Rejected variants, measured on the four back-sky controls:

| World | Straight CLUT: exact RGBA bytes | Straight CLUT: RGB MAE | Wrong row origin: exact RGBA bytes | Wrong row origin: RGB MAE |
|---|---|---|---|---|
| FANTASY | 168,307 of 262,144 | 1.855916 | 131,640 of 262,144 | 2.613597 |
| HALLOW | 164,240 of 262,144 | 2.396383 | 119,396 of 262,144 | 2.920522 |
| JUNGLE | 197,110 of 262,144 | 4.546010 | 183,498 of 262,144 | 4.609253 |
| SPACE | 146,115 of 262,144 | 2.167491 | 119,832 of 262,144 | 2.732951 |

These two wrong variants each still pass tolerance on **4 of 4 controls**.
Leaving palette alpha unscaled gives **196,608 of 262,144 exact RGBA bytes per
control**, RGB MAE 0 and alpha MAE 127; **0 of 4 controls** pass tolerance.
At RGBA offset 3 of `FANTASY/Sky/Fantasy_back.ssh`, that rejected alpha variant
returns 128 against TGA 255. Straight CLUT first differs at offset 128, returning
46 against TGA 69. Wrong origin first differs at offset 16, returning 113 against
TGA 102. Offsets are zero-based.

For the **4 of 8 type 0x02 pairs** named `*_front2`, **256 of 256 SHPS palette
entries per image** are grayscale with stored alpha 128, while **256 of 256 TGA
palette entries per image** are white RGB with alpha 0..127. The stored channel
representations differ. Their index planes also differ after aligning origin.
The decoder preserves the SSH's opaque grayscale values; it does not invent an
alpha mask to match the TGA. The real scorer measures:

| SSH file (under world/Sky) | RGB MAE | Alpha MAE | Exact RGBA bytes |
|---|---|---|---|
| FANTASY/Fantasy_front2.ssh | 158.450317 | 207.895401 | 7,479 of 262,144 |
| HALLOW/Hallow_front2.ssh | 159.314346 | 208.440582 | 3,246 of 262,144 |
| JUNGLE/jungle_front2.ssh | 179.829987 | 218.709763 | 2,322 of 262,144 |
| SPACE/space_front2.ssh | 193.967834 | 226.002655 | 2,331 of 262,144 |

Thus **4 of 8 new pairs** are TGA-exact and **4 of 8** differ. Worst RGB-MAE file is
`SPACE/Sky/space_front2.ssh`: first differing RGBA offset 0 is TGA 255 / SSH 4;
maximum absolute byte error 255 occurs at offset 39, TGA 0 / SSH 255 (alpha).
For the tied maximum in `FANTASY/Sky/Fantasy_front2.ssh`, RGB offset 312 is
TGA 255 / SSH 0. This is a comparison with TGA art, separate from decoder parity.

After adding type 0x02, **5,756 of 5,764 scorer records** are unchanged from the
original baseline; the only changed records are the **8 of 5,764 SSH files** above.
The managed compressed output remains **5,687 of 5,687 reference-decodable pairs
byte-identical** (**5,687 of 5,695 original scoreable pairs**).

| Final population | Within tolerance | Decoded | RGBA exact | Image-weighted RGB mean |
|---|---|---|---|---|
| All SSH | 3,542 of 5,764 | 5,695 of 5,764 | 6 of 5,764 | 4.907867 over 5,695 of 5,764 |
| Lowercase `.tga` | 2,809 of 4,563 | 4,563 of 4,563 | 6 of 4,563 | 4.870594 over 4,563 of 4,563 |
| Uppercase `.TGA` | 733 of 1,132 | 1,132 of 1,132 | 0 of 1,132 | 5.058112 over 1,132 of 1,132 |

Flat RGB remains **19 of 121 flat references**, RGBA **2 of 121**. The higher mean
comes from including the four cloud pairs; no compressed image changed. Final
scoring used the same nonexistent `TPW_FFMPEG` and stripped PATH command above,
with `--json /tmp/tpw-ipu-validation/final-score.json`. The final trace at
`/tmp/tpw-ipu-validation/final-execve.txt` again contains **0 FFmpeg executions
of 2 successful execve calls**.

## Full-pair final differential

For a reference that can decode the smaller job too, the complete harness was run
again against the pinned newer historical commit. Its compressed path still uses
FFmpeg, and its type `0x02` path supplies the independently established palette
interpretation:

```sh
python3 tools/ssh-differential.py /home/ec2-user/tpw-ps2/sshfull \
  /tmp/tpw-ipu-all-pairs --ffmpeg /home/ec2-user/.local/bin/ffmpeg \
  --baseline ed581af5b6f9467e7cbba9ea865e8101027ff809
```

**5,695 of 5,695 scoreable pairs are RGBA byte-identical**, with **0 of 5,695**
different and **0 of 5,695** decode failures. All **5,764 of 5,764 real scorer JSON
records** are identical between that historical implementation and the final
managed implementation. Both produce the final score table above. Deliberate
mutation reduces exactness to **5,694 of 5,695 pairs**, reporting the same
`DATA/Chars/Dino/dino.ssh` offset 0 (reference 255, managed 254).

Final synthetic validation passes **2,163 of 2,163 assertions**. This adds a vertical
AC golden vector, linear palette/CSM1 boundary and alpha checks, malformed palette
headers and truncated data, and rejection of palettes overlapping the next SHPS
entry. All synthetic data is generated from source; no game fixture is committed.
