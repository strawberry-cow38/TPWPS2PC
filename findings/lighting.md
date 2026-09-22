# Lighting: the ordinary PS2 mesh path

Established from PAL `SLES_500.32`: one ambient RGB plus one directional RGB, evaluated **per
vertex on signed-byte normals**, followed by GS byte-domain texture modulation. Clear gameplay
uses ambient `(0.5, 0.5, 0.5)`, directional `(0.6, 0.6, 0.6)`, and a ray approximately
`(0.3, -0.7, 0.6)` in game coordinates. These are executable constants, not fitted colours.
Weather darkens the two colours. No separate HALLOW light palette was established.

The port reads these constants from the owner's disc. It replaces the viewer's invented ambient
and Godot directional light with a vertex shader implementing the traced path. This establishes
an ordinary-light translation, **not complete PS2 framebuffer parity**; the unresolved paths and
rasterization limits are listed below.

## Addresses and reproduction

Work began at `592705c`, which contains `0118c53`; back-face culling was already the default.
All before/after comparisons use that culling policy and the existing mirrored root `(1,1,-1)`.
No measurements from an older, double-sided checkout were used.

ELF SHA-256: `231771d7f39cc29e7573ce62f437af15d97737a64c1d1c49df8404ead0b7577a`.
Except where marked **file**, addresses below are EE virtual addresses. This ELF's PT_LOAD has
file offset `0x1000`, virtual address `0x100000`: `file = EE - 0xff000`. In particular, the
renderer VU upload at EE `0x2a4420` is at **file `0x1a5420`**. Subtracting `0x100000` instead
starts disassembly 4 KB too early. The sky table described as `0x260f60` in the texture lookup
findings is a file offset (EE `0x35ff60`).

```sh
python3 tools/r5900dis.py /path/to/ps2.elf 0x229ab0 0x229dc0
python3 tools/r5900dis.py /path/to/ps2.elf 0x23ec58 0x23f3f4
python3 tools/r5900dis.py /path/to/ps2.elf 0x227c60 0x227ef4
python3 tools/r5900dis.py /path/to/ps2.elf 0x22a5a0 0x22aa00
python3 tools/r5900dis.py /path/to/ps2.elf 0x21a214 0x21a2bc
python3 tools/vu1dis.py /path/to/ps2.elf 0x1a5420 0x800
python3 tools/vu1dis.py /path/to/ps2.elf 0x1a5c28 0x800 0x800
```

The R5900 helper preserves LQ/SQ and raw unsupported words; its stock Capstone fallback does
not correctly name every COP2 instruction. VU instruction labels here come from `vu1dis.py`.
No emulator or native disassembler is called by the managed reader.

## Producers: renderer initialization and weather

The constructor at `0x229ab0` initializes the renderer global at `0x311160` (static construction
calls it at `0x22b2e4`). Its first light occupies four vectors:

| Renderer offset | Constructor value | Use traced below |
|---|---|---|
| `+0x00` | `(0.6,0.6,0.6,1)` | ambient colour |
| `+0x10` | `(0.6,0.6,0.6,0)` | directional colour |
| `+0x20` | transformed ray | used for each mesh |
| `+0x30` | `(0.3,-0.7,0.6,0)` | source ray |

The ray assignments preserve bit 0 of X/Y from the preceding vector. Those source bits are
zero; stored Y is therefore `-0.6999999284744263`, not the nearest float to decimal `-0.7`.
The reader preserves that operation. This tiny difference was not used as a tuning parameter.

**Stopping at this constructor would give the wrong gameplay ambient.** The weather object at
`0x342e28`, constructed by `0x23ec58`, contains:

| Weather offset | Value | Immediate starts at |
|---|---|---|
| `+0x00` | clear ambient `(0.5,0.5,0.5,1)` | `0x23ec64` |
| `+0x10` | ambient decrement `(0.13,0.13,0.13,0)` | `0x23ecd0` |
| `+0x20` | clear directional `(0.6,0.6,0.6,0)` | `0x23ecb4` |
| `+0x30` | directional decrement `(0.1,0.1,0.1,0)` | `0x23ecec` |
| `+0x64` | current weather amount, initially zero | `0x23ec9c` store |

Weather update `0x23efe8` is called from `0x226000`. At `0x23f218` it skips colour updates if
the transition countdown is zero. Otherwise it advances the current amount, then executes:

```text
t = 0.8 * weather_amount             // 0.8 immediate at 0x23f23c
f = 2*t - t*t                       // 0x23f290..0x23f2a4
ambient     = clear_ambient     - ambient_decrement     * f
directional = clear_directional - directional_decrement * f
```

The resulting vectors are written to renderer `+0x00` at `0x23f2fc..0x23f308` and `+0x10`
at `0x23f37c..0x23f388`. At amount 1 they are `(0.3752,0.3752,0.3752)` and
`(0.504,0.504,0.504)`. Transition code at `0x13c1c8..0x13c1d4` and `0x13c27c..0x13c28c`
requests target zero over 20 time units through `0x23ef68`, making the clear-weather write
reachable even though the constructor's countdown starts at zero. Gameplay calls at
`0x14f058`, `0x14f0ec`, `0x14f1a8`, `0x14f230`, `0x14f2ac` set clear/random targets with
5000-unit transitions alongside precipitation changes.

No direct calls to the colour setters at `0x23ed10`/`0x23ee00` were found in the linear EE
listing. This is evidence for using the traced global clear state, not proof against every
indirect call or world-specific runtime mutation.

## Consumers: DMA, VU and GS

Frame setup `0x22a5a0` emits `UNPACK V4_32`, 26 qwords at VU address `0x56`
(`0x22a788`: command `0x6c1a0056`). The copies at `0x22a934..0x22a960` place renderer
ambient/directional at VU qwords `0x68`/`0x69`; the alternative pair goes to `0x6a`/`0x6b`.
The VU dispatcher at `L001d..L0029` selects a pair using render flags.

Before a mesh, EE `0x227d98..0x227e5c` computes three dot products of the ray with the
mesh basis. `0x227e78..0x227ef0` normalizes that resulting **local ray**. The packet beginning
with VIF `0x6c050007` uploads it at VU qword 7 along with the matrix. This translates to
`normalize(transpose(model_basis) * world_ray)`, after the renderer's view-coordinate
transforms cancel: `0x21ebec..0x21ec00` builds the inverse view at `0x2f0580`, and
`0x21ec40..0x21ed54` transforms renderer `+0x30` through it into `+0x20`. The mesh path
multiplies its basis by the corresponding view at `0x227ccc` before those dot products.
It is not an inverse-transpose normalized vertex normal. In particular,
nonuniform model scaling affects the ray before normalization.

The ordinary strip loop starts at `L0080`; its clipped sibling starts at `L00c5`.

| VU instructions | Operation |
|---|---|
| `L0091..L0093` | load qword-7 ray, negate it; load directional and ambient pair |
| `L0096..L0097` | multiply ambient by **128** |
| `L009e` | `ITOF0` signed normal bytes, with no normalization or division by 127 |
| `L009f..L00a6` | dot normal with negative ray; clamp at zero |
| `L00a7..L00a8` | directional times dot plus ambient |
| `L00ad`, `L00b9` | cap at 255, then truncate with `FTOI0` into the GS vertex colour |

Thus, componentwise, for raw normal bytes `N`:

```text
L = -normalize(transpose(model_basis) * ray)
Cvertex = trunc(min(255, 128*A + D*max(dot(N,L),0)))
```

The texture state built at `0x21a214..0x21a2b8` fills TEX0's address/size/format fields,
TCC at bit 34, and CLUT fields starting at bit 37, leaving TFX bits 35..36 zero: MODULATE.
At a constant-colour patch, GS RGB is therefore:

```text
Cpixel = min(255, trunc(texture_byte * Cvertex / 128))
```

The factor is **128, not 255**, and multiplication is in encoded byte space. The independent
GS reference inspected was PCSX2 revision `650d7048561756cdbfea60ee65282074a38eefa1`,
[`tfx_fs.glsl`, function `tfx`](https://github.com/PCSX2/pcsx2/blob/650d7048561756cdbfea60ee65282074a38eefa1/bin/resources/shaders/opengl/tfx_fs.glsl#L809).
This cross-checks GS modulation; it is not a capture of this game's live light state.

## Godot translation

`core/TPW.PS2.Data/Lighting.cs` maps ELF addresses through PT_LOAD, guards the interpreted
constructor/update, packet, TEX0 and VU code spans with SHA-256, and reads the float immediates.
An unsupported executable fails explicitly. It has no Godot, process-launch or native dependency.

`game/Ps2Materials.cs` supplies the shared opaque/soft-alpha shader. `AnimatedModel` uploads
the original normal bytes in `CUSTOM0`; Godot's compressed/normalized NORMAL would lose them.
The shader evaluates and quantizes colour in the vertex stage, then modulates the sampled
texture bytes. It is unshaded: Godot lights and ambient cannot add a second lighting pass.
There is no `FRONT_FACING` normal flip. The ray's Z is reflected once into Godot world space;
the mirrored model basis then transforms it into each mesh's local space.

The shader retains repeat UVs, nearest mip filtering for authored meshes, linear mip filtering
for generated floor, the 16/255 cutout and soft-alpha depth prepass. The texture sampler has no
`source_color` annotation. Forward+ receives the sRGB-decoded final colour so output encoding
returns the computed byte. Compatibility needs a separate output adapter: Godot 4.6.2 converts
ALBEDO through a cubic approximation, then applies a power output transform even for unshaded
materials. Simply passing encoded bytes made expected green 20 become 16. The shader inverts
that documented cubic with Newton iterations; no light value was adjusted. Sources:
[`scene.glsl`](https://github.com/godotengine/godot/blob/4.6.2-stable/drivers/gles3/shaders/scene.glsl),
[`tonemap_inc.glsl`](https://github.com/godotengine/godot/blob/4.6.2-stable/drivers/gles3/shaders/tonemap_inc.glsl).
Other Godot versions need the pixel audit, particularly for this adapter.

`Viewer.cs` only loads the disc light, removes its invented directional light/ambient, and
routes two generated-floor material factories through the shared shader. Sky contribution
stays zero. `Park.cs` routes its fallback floor through the same material.

Named defaults: `DefaultWeatherAmount = 0` until the actual weather state machine is ported;
`UnconfiguredNeutral` is only for standalone callers without a disc (the viewer always loads
the ELF). Missing-texture grey remains the viewer's `fallback_colour = 0.72`. Generated ground
uses its synthetic normal times 127; its exact PS2 terrain-normal generation is not established.

## Identity audit and mutation

These are named surfaces of `/terrain/terrain_1.mps`, first triangle's first vertex, with each
mesh's authored world transform. Textures below are the existing resolver's TGA choices for
the stated `.ssh` material names. Each light uses the clear A/D/ray above. GS colour is neutral
RGB, so the table lists one channel.

| World / mesh | Material | Raw N | GS colour | Resolved texel `(x,y): RGB` | Expected RGB |
|---|---|---|---:|---|---|
| SPACE / `CLIFFS` | `sfl_bas1.ssh` | `(66,108,-4)` | 100 | `(0,1): (101,26,144)` | **`(78,20,112)`** |
| HALLOW / `road_rhs01` | `grd_ctr1.ssh` | `(0,127,0)` | 119 | `(0,0): (59,58,69)` | **`(54,53,64)`** |
| HALLOW / `ticket_booths` | `gte_rof1.ssh` | `(83,-21,93)` | 64 | `(52,24): (89,86,78)` | **`(44,43,39)`** |
| JUNGLE / `A_ROAD` | `grd_ctr1.ssh` | `(0,127,0)` | 119 | `(0,0): (59,58,69)` | **`(54,53,64)`** |
| FANTASY / `gatebase01` | `jgr_bas1.ssh` | `(0,-127,0)` | 119 | `(0,0): (8,162,4)` | **`(7,150,3)`** |

FANTASY's negative local Y is transformed by its authored basis; treating it as a world-down
normal incorrectly gives ambient only. An identity-basis up normal gives 119; byte 126 instead
of 127 gives 118. A mirrored-root sign error would turn the 119 into 64: a **119/64 = 1.859375**
ratio under this recovered light. The older 2.95 ratio described the previous Godot light.

The managed console audit checks constants, weather, named mesh/material/normal/texel/output
identities, and rejects a deliberate `.5 -> .25` ambient-immediate mutation in a private copy
of the ELF. The Godot audit uses the **actual AnimatedModel surface material and uploaded
CUSTOM0**, carrying the named normal, transform and sampled texel onto a controlled patch.
Its deliberately green, energy-8 Godot ambient must have no effect. It checks opaque and soft
materials, transformed/mirrored bases, generated floor, and the 126-byte case. It allows one
framebuffer code of backend rounding; the measured results were exact in both renderers.
The patch isolates the lighting equation; it does not test the complete source triangle's
interpolation or texture filtering.

Run on a display, including Xvfb, with Godot 4.6.2 mono and .NET installed:

```sh
python3 tools/lighting-check.py /path/to/tpw_ps2.bin /tmp/new-lighting-audit \
    --godot /path/to/Godot_mono
```

This builds, runs the managed audit, checks Compatibility and Forward+ pixels, checks culling
off, removes the directional term from the actual shader in memory, **requires exit 2 with a
SPACE pixel mismatch**, then runs the unmodified shader again. The measured negative control
was expected `(78,20,112)`, actual `(50,13,72)`. A missing mutation target or unrelated crash
does not satisfy the gate. The mutation never changes source files. Logs and probe PNGs are
written outside Git. The existing `TextureAnimationAudit.tscn` also passed, including material
updates when animated texture frames switch.

## Before/after inspection

Matched full park images were rendered and opened for SPACE and HALLOW using Forward+,
Godot 4.6.2 / llvmpipe, 1152x648, frame zero, cull back. Baseline code came from a `git archive`
of `592705c` into `/tmp/tpw-lighting/baseline`; no checkout or main branch was changed.
The same comparison was made close to HALLOW's `ticket_booths` using `TPW_PARK_MESH`.
The broad ground colours move modestly, while the booth's previously bright roof becomes
grey and the gate stonework has stronger differences between faces. Geometry and the sky
remain visually aligned. These observations catch gross rendering mistakes; the table and
pixel gate, not this appearance, are the acceptance evidence.

Session artifacts (not committed, because they contain rendered game artwork):

- `/tmp/tpw-lighting/space-forward-before.png`, `space-after.png`
- `/tmp/tpw-lighting/hallow-forward-before.png`, `hallow-forward-after.png`
- `/tmp/tpw-lighting/hallow-roof-before.png`, `hallow-roof-after.png`
- `/tmp/tpw-lighting/final-gate/`: both renderers' named probes, mutation and restored logs

Reproduce each full scene with the same command against the baseline and modified game paths:

```sh
TPW_PS2_DISC=/path/to/tpw_ps2.bin TPW_PS2_MODE=park TPW_PS2_WAD=HALLOW \
TPW_PS2_CULL=back TPW_PS2_SHOT=/tmp/hallow.png \
    /path/to/Godot_mono --path game --rendering-method forward_plus --audio-driver Dummy
# Add TPW_PARK_MESH=ticket_booths for the close comparison; use SPACE for the other park.
```

Full-park Compatibility captures intermittently crashed with a native/coreclr error after
loading, also observed before the lighting change. Forward+ produced the matched full images;
the isolated pixel audit passed in both. The crash was not diagnosed or fixed in this work.

## Not established / not implemented

- **Other light selections.** Renderer `+0x40/+0x50/+0x70` initialize a second pair/ray
  `(0.5,0.5,0.5)`, `(0.6,0.6,0.6)`, `(0.1,-0.7,0.7)`. EE `0x228384..0x2283b4`
  selects its ray when `(flags & 0x00600000) == 0x00200000`; VU dispatch can also select
  other constant pairs. Which entities/materials activate these paths is unresolved.
  The port applies the ordinary path to meshes; this is a limitation for those selections.
- **Runtime completeness.** No exhaustive proof of world-specific indirect colour writes,
  live saved weather states, or every caller's render flags. HALLOW was checked by identity
  and rendered; a global clear light is not a claim that its live night scene always uses it.
- **Scripted lights.** The four opcode names also exist in this PS2 ELF, referenced by pairs
  at `0x2e9748..0x2e9760`. Their names alone do not establish handlers, arguments, point
  attenuation or placement semantics. No script on this disc exercises them.
- **Fog, shadows, local lights, specular, sky-to-ambient coupling.** No implemented model
  was established for these. The white cloud masks do not prove an ambient colour.
- **Weather timing.** The colour equation is implemented and tested; transition timing and
  precipitation-driven state changes are not connected to the viewer's rain/snow toggle.
- **Complete GS emulation.** Godot still controls perspective interpolation, mip/filtering,
  alpha blending, depth, output precision and other raster state. Soft-alpha probes isolate
  RGB with alpha 1. No complete GS alpha/fog/framebuffer-format pipeline was reconstructed.
  EE/VU float rounding is not emulated bit for bit.
- **Live-console comparison.** No PCSX2 game-frame or live GS packet capture independently
  confirms the chosen state for every park. The evidence is static executable dataflow plus
  disc surface identities and actual Godot pixels, not a claim of visual PS2 equivalence.
