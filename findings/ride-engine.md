# `GRC.ENG` and `WTR.ENG`: ride sound layers and volume curves

The 265-byte files at ISO paths `/AUDIO/RIDES/GRC.ENG` and `WTR.ENG` describe two
sound layers, their scalar-input intervals, interpolated parameters, and **sampled
volume multipliers**. [RideEngine.cs](../core/TPW.PS2.Data/RideEngine.cs) reads them in
managed code. They are not language files. The amplitude interpretation is confirmed
by the executable's multiplication/division into an audio volume command, not the
appearance of the ramps. **The input's units were not established**, so these are not
described as time-based attack/release envelopes.

Measured/traced 2026-09-22. ELF SHA256:
`231771d7f39cc29e7573ce62f437af15d97737a64c1d1c49df8404ead0b7577a`.

## Filename to consumer

```sh
python3 tools/r5900dis.py /path/to/ps2.elf 0x240388 0x240570
python3 tools/r5900dis.py /path/to/ps2.elf 0x2419b8 0x241df0
python3 tools/r5900dis.py /path/to/ps2.elf 0x2426c0 0x242fc8
python3 tools/r5900dis.py /path/to/ps2.elf 0x241e10 0x24225c
python3 tools/r5900dis.py /path/to/ps2.elf 0x243540 0x24356c
python3 tools/r5900dis.py /path/to/ps2.elf 0x2438a8 0x243920
python3 tools/r5900dis.py /path/to/ps2.elf 0x24e488 0x24e540
python3 tools/r5900dis.py /path/to/ps2.elf 0x253890 0x253980
python3 tools/r5900dis.py /path/to/ps2.elf 0x25b390 0x25b624
```

`grc` / `wtr` strings at `0x359de0` / `0x359de8` are referenced in bank setup at
`0x112238..0x1122fc`. The `.eng` string at `0x3705e8` is passed to the path object's
extension operation at `0x241a64..0x241a80`. This function opens/checks that path.
Its caller `0x240388` then calls `0x2419b8` at `0x240524`, which allocates the object
and calls **`0x2426c0` at `0x241a08`** with the constructed filename. That loader
performs the exact sequence of reads below. No stock MIPS32 disassembly was used.

## Every stored field

Little-endian integers, **no alignment padding**. Layer records are **33 bytes**, not
36 or 40; their in-memory stride is 40. Curve records are 44 bytes in both forms.

| file offset | type / values | evidence and meaning |
|---|---|---|
| `0x00` | `u16 2` | number of layers. First `u32` is masked with `ffff` at `0x2427ac`; allocates count × 40 and loops |
| `0x02` | `u16 8` | preserved upper header word. Shifted out at `0x2427a4`, stored in the implicit first parameter-group record at `0x242d90..9c`. **Its semantic name is unknown** |
| `0x04`, `0x25` | two 33-byte layer records | eight separate 4-byte reads and one 1-byte read at `0x242820..0x2429a4`; details below |
| `0x46` | `u32 2` | curve count, read/allocated as count × 44 at `0x242a60..90` |
| `0x4a`, `0x76` | two 44-byte curves | three 4-byte reads and a 32-byte read at `0x242ad8..0x242b80`; details below |
| `0xa2` | `u32 10` | export metadata slot count. Loop bound read at `0x242c18..40` |
| `0xa6` | `u32 1` | first metadata slot present; any nonzero value causes two length-prefixed fields to be skipped (`0x242c70..0x242ce0`) |
| `0xaa` | `u32 44` | first field byte length; passed to relative seek at `0x242ca8` |
| `0xae..0xd9` | 44 bytes | `Q:\Theme Park II\TP-PS2\Export\Global\Sound\`, **without NUL**. Build directory by its literal contents; loader ignores it |
| `0xda` | `u32 3` | second field byte length, same skip mechanism at `0x242cdc` |
| `0xde..0xe0` | 3 bytes | `grc` / `wtr`, **without NUL**. Export name by literal correspondence; runtime loader skips it |
| `0xe1..0x104` | nine `u32 0`s | remaining nine metadata slots absent. These zeros are structural flags, not a padding blob |
| `0x105` | `u32 0` | additional parameter-group count. Read at `0x242cf8..0x242d38`, then one implicit group added. Nonzero groups have further data; no disc example is present |
| `0x109` | EOF | exact consumption, no trailing padding |

Layer-relative fields:

| relative offset | layer 0 GRC / WTR | layer 1 GRC / WTR | meaning / consumer |
|---|---|---|---|
| `+0x00` | 0 / 0 | 0 / 0 | inclusive scalar-input start; compared at `0x2421d0..f0` |
| `+0x04` | 1000 / 1000 | 1000 / 1000 | inclusive scalar-input end; same selection and interpolation denominator |
| `+0x08` | 100 / 100 | 30 / 30 | volume-percent start; interpolation `0x24207c..ac`, passed as `a3` to `0x241c28` |
| `+0x0c` | 100 / 100 | 100 / 100 | volume-percent end; same interpolation. **The constant layer-0 volume matters**: it still gets multiplied by its curve |
| `+0x10` | 0 / 0 | -6 / -6 | second interpolated audio parameter start, signed. `0x2420b4..e8` |
| `+0x14` | 24 / 24 | 12 / 12 | second parameter end. Sent as `t0`, then audio command `+0x0c`. **Units/semantic name not established** |
| `+0x18` | **17 / 19** | **1 / 18** | sound selector, copied into sound request at `0x2438b8..d8` and returned by accessor `0x1124f0`. Exact sample/bank resolution not traced here |
| `+0x1c` | 1 / 1 | 1 / 1 | stored bank selector. Request's other selector at `0x2438bc..d0`. **Overwritten** for every layer by bank binding `0x243540..60` called at `0x240560`; not evidence that both files use global bank group 1 |
| `+0x20` | 0 / 0 | 1 / 1 | channel slot. Serialized as one byte, stored at in-memory `+0x24` (`0x2429b0..bc`). Selects one of two sound-body slots at `0x241e54..70` and one of two curve multipliers at `0x241bcc..bf8` |

The in-memory `+0x20` word is the layer's ordinal, assigned at `0x242a24`; it is
**not in the file**. Misreading a 40-byte disk record invents fields and shifts the ramps.

Curve-relative fields:

| relative offset | curve 0 / curve 1 | meaning / consumer |
|---|---|---|
| `+0` | `s32 0 / 0` | inclusive start, comparison at `0x241b74..90` |
| `+4` | `s32 1000 / 1000` | inclusive end, comparison and divisor at `0x241b84..bb0` |
| `+8` | `s32 0 / 1` | **layer index**, multiplied by 40 at `0x241bc4`, then that layer's channel byte is read. It is not directly a channel number |
| `+12` | 32 sample bytes | read at `0x242b70..80`; selected by `0x241bec..bf4`, copied to a byte multiplier. Observed domain 0..99; valid percentage reader domain 0..100 |

The complete samples (decimal) are:

```text
curve 0: 99 99 99 97 96 94 91 88 85 81 77 73 69 64 59 54
         50 45 40 35 30 26 22 18 14 11  8  5  3  2  0  0
curve 1:  0  0  0  2  3  5  8 11 14 18 22 26 30 35 40 45
         49 54 59 64 69 73 77 81 85 88 91 94 96 97 99 99
```

These are **not reverse copies**: comparing the reversed first array to the second
differs at indices 2 through 29, because the endpoint plateaux also differ. They are
**exactly complementary to 99 at every matching index**. The earlier 30-byte excerpt
hid endpoint samples. Readers preserve all 32 bytes, including the 50/49 midpoint.

## Evaluation proves amplitude scaling

`0x241b28` initializes both channel multipliers to **100**, scans matching curves in
file order and stops after **two matches**. For an in-range scalar `x`:

```text
sample = min(31, floor((x - start) * 32 / (end - start)))
channel = layers[curve.layer_index].channel
multiplier[channel] = curve.samples[sample]
```

There is no interpolation between curve samples. At the inclusive endpoint the
quotient is 32, then clamps to 31. Outside a curve interval the channel retains 100.
The caller at `0x24218c..0x242258` gets `x` from the sound body's state array and clamps
it to a **minimum of 5** before choosing layers/curves. No clock delta appears in this
selection. A speed-related crossfade is plausible, but the producer/units of `x` were
not traced, and `.ENG` by itself does not establish speed, RPM or milliseconds.

Layer endpoint interpolation uses signed integer division (toward zero):
`startValue + (endValue-startValue)*(x-start)/(end-start)`.
`0x241c28..0x241d20` scales interpolated volume by the body's float gain, converts it
to integer, then computes `min(127, value*127/100)`, multiplies by the selected curve
percentage, divides by **100** again and caps at 127. The result enters audio command
`+8` (vtable `0x371e78`, handler `0x24e488`). That handler passes it to `0x246ab0` and
`0x254e80`: the latter stores it at command parameter `+0x1c` and sets update flag
`2`. The downstream handler `0x25b390`, at `0x25b494..b0`, tests that flag, copies the
value to voice `+0x18`, and calls `0x253890`, which multiplies it by additional float
gains before driver dispatch. The second parameter follows a separate field
`+0x24` / flag `0x20` path. This traced gain chain is the operational evidence for
calling the samples volume multipliers; hardware output and audible loudness were
not measured.

At body gain 1, the shipped files evaluate identically except for sound selectors:

| input | curve percentages | interpolated volumes | second parameters | resulting 0..127 volumes |
|---:|---|---|---|---|
| 0 | 99, 0 | 100, 30 | 0, -6 | 125, 0 |
| 499 | 54, 45 | 100, 64 | 11, 2 | 68, 36 |
| 500 | **50, 49** | 100, 65 | 12, 3 | **63, 40** |
| 1000 | 0, 99 | 100, 100 | 24, 12 | 0, 125 |

Input 0 above is direct evaluator behavior; the caller normally raises it to 5.
The reader's `Evaluate` documents unity body gain and an already supplied scalar;
it does not reproduce state production, voice playback or the caller's clamp.

## Audit and unsupported cases

```sh
dotnet run --project tools/TPW.PS2.LastFiveAudit -c Release -- /path/to/disc.bin
dotnet run --project tools/TPW.PS2.LastFiveAudit -c Release -- --self-test
```

Every parsed field, including constant header/metadata fields, all samples in order,
and both evaluation methods for every integer input **-1 through 1001**, enter a
fixed decoded SHA256. A separate Python byte walk and the consumer arithmetic supplied
the pins ([preserved reference walk](../tools/last-five-reference.py)): GRC `e5936792cb777ad8fe5fa1ecf5198b9010f20c85c980b5d21c25901e9b685de2`;
WTR `c83c8f22e94c3467bcad8affd0b8937a7bd28c6bda9818fbd8dc68a20ba92bc8`.

Explicit checks identify sound selectors, export names and midpoint results. Synthetic
data swaps the two channel numbers so that confusing curve layer index with channel
fails, uses non-mirrored samples, exercises a third matching curve to verify the
consumer's two-match limit, and checks inclusive endpoints, absent intervals and
signed interpolation. Every truncated prefix is rejected, as are oversized counts,
strings, invalid references/channels/intervals and trailing bytes. Reversing a ramp
preserves its histogram and fails the decoded/evaluated digest. Audit exit codes:
0 pass, 2 assertion failure, 1 usage/load exception.

**Executed decoder negative control:** replaced the production sample lookup with
`Samples[31 - sample]`. Both per-file digests, midpoint results and synthetic endpoint/
channel checks failed; the audit exited **2**. Restored lookup exits **0**. Sample counts
and histograms were unchanged.

The reader supports the actual shipped variant and rejects nonzero additional
parameter-group counts. The ELF has a larger parameter-group path at `0x242dac..0x242f34`;
implementing it without a file example is left for future work. HeaderHigh and metadata
presence words are retained rather than guessed to be versions or Boolean bytes.

## What could not be established

* Scalar input's producer and units; a temporal ADSR envelope, vehicle speed or RPM is
  not proven. This trace confirms sampled volume modulation/crossfade behavior.
* Semantic name/units of the signed second parameter (0→24 and -6→12). A pitch-like
  interpretation is plausible; the implementation exposes `ParameterStart/End`.
* Meaning of upper header value 8, beyond its exact storage/use described above.
* Exact terminal sample identity selected by the sound/bank selector pair, or its
  relation to the sibling SFX.MAP graph. Sibling group names and loader binding are
  established; a shared name alone is not a sample-level join.
* Why export metadata has ten slots, why this build stores one present slot, or which
  editor generated the unequal ramps. The runtime skips the strings.
* Additional parameter-group variants, malformed-file behavior of the original loader,
  and final audible rendering. Reader validation is intentionally stricter; no audio
  capture comparison or full state/voice simulation is claimed.
