# The park's procedural water layer (2026-10-02)

## Result and scope

The PS2 executable contains a **separately registered, animated water drawable**. It is created
by the **park** initialization chain, not the lobby room animation. It generates changing vertex
heights and scrolling UVs on the EE, loads `DATA.WAD/Generic/extra/justwater.ssh`, and requests
alpha-blended triangle strips. Its footprint overlaps the stationary `A_SEA` geometry, above
that geometry in composed world coordinates.

This is a positive code/data/placement join matching the reported two-layer sea. It supersedes
the old inference in `sea.md` that absence of an APS track or an extra shipped mesh meant no
moving park water. **No emulator screenshot/packet or rendered port implementation has been
compared yet.** This note establishes the mechanism, not final pixel parity or exact coverage
of every part of the ocean.

Research used the authorised disc read-only and the shared whole-program decompile. Selected
MIPS and raw file-backed ELF words were checked independently of the decompile. No savestate,
binary extraction, or renderer change was needed.

Executable: PAL `SLES_500.32`, SHA-256
`231771d7f39cc29e7573ce62f437af15d97737a64c1d1c49df8404ead0b7577a`.
Addresses below are EE virtual addresses; file mapping is obtained from PT_LOAD.

## Creation, registration, actual render callback

```text
park initialization 0x151498
    -> 0x149958(world, terrain variant)       also loads terrain and world sky
        -> 0x220FA0(x, 2.2, 5.875, logo variant)
            -> allocate 0x50-byte water object
            -> constructor 0x22C738
            -> initializer 0x22C858
            -> renderer registration 0x226040(renderer 0x310D48, object, layer 0, ...)
```

`0x220FA0` stores the object at global `0x2F07D8`; constructor `0x22C738` sets its vtable to
`0x36F6E8`. **Reading the actual ELF vtable word**, not guessing from neighboring functions,
gives `vtable + 0x14 == 0x22CB90`. The ordinary registered-renderable layer loop in `0x225BF8`
dispatches precisely that slot each frame.

Native calls to `0x220FA0` are at `0x149B8C` and `0x149BBC`; all four valid world branches reach
one of them. The neighboring logo objects and generic asset directory do **not** make this a
front-end-only drawable. `0x151498` is the park loader, also independently traced in
`native-gates-controller.md`.

## Actual texture join and blending evidence

- `0x2210B4/B8` materialize path/name pointers `0x36E7E8` / `0x36E8B0`; `0x2210C0` calls the
  texture loader `0x2351A8`, storing its result at `0x2F07C8`.
- The **actual strings** at those exact starting addresses are `data/generic/extra/` and
  `justwater.ssh`. Older notes called the directory `weather/`; that was wrong.
- The archive entry is `/Generic/extra/justwater.ssh` in **DATA.WAD**, a 64×64 compressed type-5
  SHPS entry (RGB + alpha). Its shipped 32bpp TGA counterpart has alpha min94, max154, mean
  approximately120; none of those source-art texels is fully opaque. The matching
  `justwater2` files are also present, but this loader requests **justwater**, not justwater2.
- `0x22CB90` passes object `+0x24` (that texture) to queue function `0x22A068` at `0x22ED34`.
- Its packet-builder call `0x22CCF0` supplies PRIM `0x5C`, including alpha-blending enable.
  `0x22B478` places this argument into the GIFtag PRIM field with PRE set.

This establishes alpha-bearing art and a blend-enabled primitive, **not the complete effective
GS ALPHA equation or measured framebuffer opacity**. The texture flag OR `0x0C` in the loader
is not that proof; those bits participate in clamp state.

The four unbound world texture stems (`jri_sur1`, `jri_sur4`, `jri_lak1`, `gby_sur1`) are a
separate lead. This path does not need them, and it exists in Fantasy too.

## The wave is real vertex motion, but not a sine lookup

`0x22CC0C` calls animation advance `0x22CB30` with the renderer's frame delta `0x2F07B0`:

```text
UV accumulator (+0x30, 64-bit) += trunc(delta * 3 / 2)
phase (+0x28, float) += float(delta) / object[+0x40]
if UV accumulator > 0x2000: subtract 0x1000
```

Initializer `0x22C858` supplies observed constants, not fitted port settings:

| field | native value | use |
|---|---:|---|
| +0x28 | `(rand() & 0x3fff) / 100` | initial deformation phase |
| +0x30 | 0x1000 | initial UV accumulator |
| +0x3C | 12.0 | spatial sampling span |
| +0x40 | 317.0 | phase advance divisor |
| +0x44 | 0.2 | combined X/Y sample height contribution |
| +0x48 | 0.6 | Z sample height contribution |

The draw selects grid dimension N between **4 and16** using projected bounds. Its main grid
uses `2*N*(N-1)` strip vertices, plus eight appended vertices. Each grid sample calls `0x239358`
at `0x22CDC8` using `phase + i/(N-1)*12`, then halves the returned three-component vector.

`0x239358` evaluates a **128-segment periodic cubic** table at `0x339C60`, stride0x30. It
selects `trunc(input) & 0x7F`, obtains the fractional component, and evaluates three cubics.
`0x238CC8` builds this table at renderer initialization from RNG-generated three-component
samples and finite differences. This is smooth procedurally generated deformation, **not a
reader of the previously censused sine tables**. A sine-only search cannot find it.

The grid's Y writes are explicit: `0x22CEE0..0x22CF14` combines reversed-X and forward-Y
samples with baseY+0.03 and factor0.2; `0x22CF48..0x22CF7C` supplies factor0.6 times reversed-Z
samples; the vertex writer adds those contributions. Its UV writer subtracts the accumulator's
low halfword from V (`0x22CE68..70`). The usual VU transform path can therefore remain unchanged:
the **input positions are already moving**.

This is not a reconstructed realized RNG history. Neither an assumed FPS nor an invented
amplitude/speed is needed to identify the mechanism. A faithful implementation still needs to
preserve the native table generation, update order, wrapping/narrowing and coordinate mapping.

## Placement: the native surface is above the opaque sea

`0x220FA0` builds identity-transformed bounds:

```text
X = suppliedX - 6.125 .. suppliedX + 18.875       width25
Z = suppliedZ - 20.875 .. suppliedZ + 0.525
suppliedZ = 5.875 -> Z = -15 .. 6.4             depth21.4
baseY = approximately -0.55
main grid = baseY + 0.03 + procedural displacement
```

World branch supplied X values (variant argument1 / other) are Jungle23.125/23.125, Hallow37.125/41.125,
Fantasy31.125/33.125, Space29.125/41.125. The source values are read from the caller's float
immediates; they are not centers fitted from the art.

Appending geometry also uses X margins−30/+31 and front Z margin−20, at **baseY−0.35 ≈−0.9**,
with the same scrolling UVs. This extends the drawable beyond the central animated grid; it
is not a claim that every ocean triangle is deformed.

The separate opaque sea must be measured through **the entire MPS parent chain**, not mesh-local
coordinates or AABB centers. For all eight terrains, composed `A_SEA` Y is approximately
**−1.1..−1.0**. For Jungle, front pieces `A_SEA_02/05` cover X≈14..46.147,Z≈−20..5: the generated
grid X17..42,Z−15..6.4 overlaps them and its baseline is above them. Other worlds translate the
static pieces; the eight-park probe records those bounds separately.

The earlier local two-height values −2.2666/−1.2666 are not world heights of the rendered sea.
Scale and parent translation matter here just as they do for rider attachment positions.

## Reproduction / limits / status

`tools/ocean_wave_probe.py --disc /path/to/tpw_ps2.bin` reads everything in memory and prints only
metadata, instruction/vtable witnesses, source alpha statistics and world bounds. It checks
the actual path/name/callback join and the eight terrain sea envelopes. It is **not an emulator
or native execution harness**, and no assets are written.

Selected evidence logs from this read: `/tmp/tpw-ocean-selected-native.log`,
`/tmp/tpw-ocean-join-native.log`, `/tmp/tpw-ocean-height-packet-native.log`,
`/tmp/tpw-ocean-identity-native.log`, `/tmp/tpw-ocean-disc-probe.log`.

No game/renderer behavior was changed in this slice. The mechanism is found; exact emulator
image matching and implementation/testing remain separate work. Lobby `base.aps` scrolling is
also separate, owned by cow tools, and is not this park drawable.

Final local checks: the registered read-only probe exits0 with all ten native word witnesses,
the actual path/name/alpha-bearing texture, and six composed sea meshes in each of eight
terrains (`/tmp/tpw-ocean-wave-registered-probe.json`). The Python tool suite passes **134 tests**,
including eight new asset-free probe guards (`/tmp/tpw-ocean-tools-tests.log`). These tests guard
evidence reading, not a rendered implementation. No rendered matrix or hardware parity claim
is made for this documentation/tool-only slice.