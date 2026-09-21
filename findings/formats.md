# What is inside a world archive

Measured on `DATA/JUNGLE.WAD` (2,545 files, 170 directories, all read). Counts are that archive's.

| ext | n | bytes out | what it is |
|---|---:|---:|---|
| `.tga` | 984 | 12,811,784 | source textures, ordinary Targa (type 2, 24 bpp, BGR, bottom-left origin) |
| `.gin` | 14 | 3,740,116 | unknown, and the largest per-file — 267 KB average |
| `.ssh` | 998 | 3,450,288 | **EA SHPS** image container, magic `SHPS`, first entry tagged `GIMX` (GIMEX). One per `.tga`, so: the converted-for-PS2 form |
| `.mps` | 113 | 2,986,292 | **model**, magic `0x183076E4`, 0x140-byte header |
| `.aps` | 89 | 1,414,064 | same magic family, 0x148-byte header — animation, probably |
| `.rss` | 90 | 155,681 | **ride behaviour script, PLAIN TEXT SOURCE** |
| `.sam` | 77 | 81,876 | **ride description, PLAIN TEXT** |
| `.md2` | 4 | 63,912 | Quake-2-style model |
| `.mtr` | 4 | 60,932 | unknown |
| `.rse` | 91 | 52,265 | magic `RSSEQ` — the assembled form of the `.rss` beside it |
| `.scc` | 78 | 7,024 | Visual SourceSafe status files, shipped by accident |

## ⭐⭐ `.rss` — the rides are SCRIPTED, and the scripts shipped as source

Every ride's behaviour is a program in an in-house assembly-like language, with the authors' own
comments and labels intact. 90 of them. They are not compiled-and-stripped: the `.rse` beside each
one is the assembled form, and **both** are on the disc.

**71 opcodes** appear across the corpus:

```
ADD ADDHEAD ADDOBJ BOUNCE BOUNCESETBASE BOUNCING BRANCH BRANCH_NV BRANCH_NZ BRANCH_PV BRANCH_Z
BUMP CMP COAST COPY CRIT_LOCK CRIT_UNLOCK DELHEAD DIPMUSIC DIV ENDSLICE EVENT FADEOBJ
FINDSCRIPTRAND FLUSHANIM FORCEUNBOUNCE FORCEUNLIMBO GETANIM_CH GETTIME GETTIMER GETVARINPARENT
HOP HUSH INLIMBO JSR KILLOBJ LIMBO LIMBOSPACE LOOPANIM MOD NAME RAND REMOVECHILD REPAIREFFECT
RETURN SCREAMLEVEL SETOBJPARAM SETREMOTEVAR SETREVERB SETTIMER SINGLESCREAM SPAWNCHILD SPAWNSOUND
STARTSCREAM STOPSCREAM SUB TEST TOUR TRIGANIM TRIGANIMSPEED TRIGANIM_CH TRIGWAITANIM TURBO
UNBOUNCE UNLIMBO WAIT WAIT4ANIM WAITANIM WALKGET WALKOFF WALKON
```

It is a real VM: a declared stack size (`#setstack`), named variables, labels, conditional branches
on zero / non-zero / overflow, subroutines (`JSR`/`RETURN`), critical sections (`CRIT_LOCK` /
`CRIT_UNLOCK`) and cooperative yields (`ENDSLICE`).

⭐ **The first twelve variables are a fixed ABI**, identical across scripts and clearly the host's
view of a ride: `LETMEON`, `LETMEOFF`, `CAPACITY`, `DURATION`, `BREAKSTAT`, `ONRIDE`, `RIDECLOSED`,
`BROKEN`, `WORN`, `RUNNING`, `PAD` ("unused at present"), `PARAM`. Those are the same quantities the
PSX port's ride lifecycle was reverse-engineered into from the binary — here they are named by the
people who wrote them.

The scripts `#include` headers that name the source tree:

```
\source\game\rsse\code\rsse_scriptdefs.h      the script language's own definitions
\source\game\soundint\{Events,Params,ThemedEvents}\...
\source\game\particle\code\par_lib.h
\..\tpwport\source\rsse\code\rsse_scriptdefs.h
\..\tpwport\source\audiosys\{GlobalEvent,GlobalRideEvent,JungleEvent}.h
```

⭐ **`tpwport`** — their own name for the port, sitting beside the original `game` tree. Some scripts
include from one and some from the other, so the disc carries both generations.

## ⭐ `.sam` — the ride balance table, in plain text

Header comment calls it a *"Theme Park 2 Ride Description File"*. Key/value with an ASCII-art
footprint. Fields observed: `Info.Id`, `Info.Name`, `Info.Shape`, `Info.Hoarding`,
`Info.RideTypeStringIndex`, `Info.DurationUnit`, `Info.AttractionValue`, `UsageInfo.RequiresTeleport`,
`UsageInfo.Min/MaxCapacity`, `UsageInfo.ExcitementLevel`, `Upgrades[n].{InitCapacity,
RedLineCapacity, QueueWaitTimeConstant, CostOfUpgrade, CostOfResearch}`, `Research.Group`,
`Attraction[n].NewBonus`, `Bumper.{WhichTrackType,BumperType}`.

`Info.Shape` and `Info.Hoarding` are the footprint and its fence drawn as characters, with the
entrance marked — the same footprint data the PSX port reads out of a binary table.

## `.mps` — the model (in progress)

```
0x00  u32   magic 0x183076E4          (identical in every file)
0x04  u32   0x140                      header size; .aps uses 0x148
0x08  char[32]  the file's own name
0x28  u16[] counts: materials, then two more that track file size
0x40  u32   0xC0 -> the material table
...   f32   a bounding box or base transform
0xC0  16-byte material records, last word = offset of that material's texture name
0xF0  the texture names as NUL-padded strings (".ssh", matching the folder beside the model)
0x140 f32   a 4x4 transform, 0.1 on the diagonal
```

Geometry is further in: two dense float regions holding values in ±4.14 for a 1×1-tile model, which
is the right magnitude for tile-space coordinates. **Vertex/face layout NOT established.**

Four things were tried and are recorded because each one saves the next person the same afternoon:

1. ~~**The first word is NOT a format magic.**~~ **⚠ RETRACTED 2026-09-21 — IT IS A MAGIC, AND IT IS
   CHECKED.** See "The validator" below. I reported a confident negative off a scan whose `lui` test
   was `(word & 0xFFFF0000) == 0x3C000000`, which masks the *register field* into the comparison: a
   real `lui v0,0x1830` is `0x3C021830`, so nothing ever matched and I concluded the loader never
   looks at it. The correct test is `(word >> 26) == 0x0F`, and it finds three sites. The claim that
   word 1 is a "header size" was wrong for the same reason — it is a **version**.
   `.MD2` files carry a different stamp (`0x1CD15D46`) because they are a different member of the
   same family, not Quake's `IDP2`.
2. **Header words that scale with file size**, over all 113 models: 0x40, 0x44, 0x48, 0x4C, 0x70,
   0x74 (r > 0.95 against size). Sorted, they form an ascending chain of in-file offsets, so they are
   section boundaries — but not all of them, because:
3. **No consistent record stride.** Testing every count field against every section length over all
   113 models, the best pairing (count@0x30, stride 160) divides exactly in only 64 of them and the
   rest scatter. A real layout would be 113 of 113. So at least one section boundary above is wrong
   or missing.
4. The counts at 0x22/0x28 do track complexity, and 0x22 equals the texture count on the models
   checked, but that is not enough to place the vertices.

## The validator (0x00169DD0), which settles the header

```
0x169dd0  lui   v0,0x1830
0x169dd4  ori   v0,v0,0x76e4     ; v0 = 0x183076E4
0x169dd8  lw    v1,0x0(s0)       ; header word 0
0x169ddc  bne   v1,v0,error      ; MAGIC, and it is enforced
0x169de4  lw    v1,0x4(s0)       ; header word 1
0x169de8  sltiu v0,v1,0x13e      ; version < 0x13E -> "Mesh %s version check failed"
0x169df4  sltiu v0,v1,0x141      ; version < 0x141 -> accepted
```

So `0x00` is a **magic**, enforced at three sites, and `0x04` is a **version** accepted in
**0x13E..0x140**. `.aps` carrying `0x148` is the *anim* format's version, checked separately —
"Anim %s version check failed" sits next to the mesh one in `.rodata`.

⭐ **The format has a name: M3D2.** `Source/M3d2/Code/M3D2Manip.cpp` is in the binary (an assert
path), alongside `Source/Core/Code/{TPMesh,TPRide,TPInstance,TPHoarding,TPHField,TPHMap,TPEmbed}.cpp`
and `Source/Land/Land.cpp`. `.mps`, `.aps` and `.md2` are three members of one family, and the
extension-dispatch strings `sam / mps / aps / md2 / hmp` sit together in `.rodata`.

⭐⭐ **And the PC release uses the same family**, where it was already reverse-engineered (2026-05-06,
from `demon.MD2`): a mesh table of **160-byte entries** holding a mat4 transform, texture index, name
offset, vertex/material/face counts. That is the corroboration the stride test needed — the best
pairing it found over the 113 PS2 models was **stride 160**, in 64 of them, and it was dismissed here
as noise. It was not noise.

## ⭐⭐ The mesh table — CRACKED, 113/113

```
header 0x00  u32  magic 0x183076E4        enforced at three sites
header 0x04  u32  version                 mesh accepts 0x13E..0x140
header 0x08  char[32]  the file's own name
header 0x30  u16  MESH COUNT
header 0x48  u32  MESH TABLE offset

mesh entry, 160 bytes:
   +0x00  16 bytes  scene-graph / hierarchy
   +0x10  f32[16]   4x4 transform, column-major (translation at +0x40..0x4B)
   +0x50  u32       texture index
   +0x54  u32       offset of this mesh's name (NUL-terminated, near end of file)
```

**Validated, not guessed: in all 113 `.mps` in JUNGLE.WAD, every entry `[0 .. header[0x30]-1]` has a
name offset landing on a printable NUL-terminated string.** 113/113. A wrong table base or stride
gives arbitrary offsets, and arbitrary offsets essentially never land on clean strings — so the test
fails loudly when it is wrong, which is the only kind worth running.

What it reads out, with the artists' own names:

```
/Rides/Mumbo/mumbo.mps      7 meshes   jm_floor jm_sign jm_sign01 jm_head jm_leaf jm_grass jm_body
/Rides/Monkey/monkey.mps    9 meshes   m_base m_boxes m_sign m_sign2 m_body m_arm m_arm1 m_crate m_shards
/Rides/GoKarts/gokarts.mps 11 meshes   wr_base gk_finish gk_pole1 Newflag gk_pole2 Newflag01
                                       gk_sign gk_sign1 pitsstopstuff gk_trcke gk_TRACK01
```

Texture indices run 0..n-1 and the transforms carry sensible positions — a gorilla ride with two
arms and breakable crates, a go-kart track with two flagpoles and a finish line.

## ⭐⭐⭐ Geometry — DECODED

```
mesh entry +0x60  u16 vertexCount, u16 faceCount
mesh entry +0x6C  u32 batch table offset
mesh entry +0x94  u32 batch table length

batch record, 16 bytes:
   +0x00 u32  positions offset
   +0x04 u32  second stream offset      (5 B/vertex: UV and/or packed normal)
   +0x08 u32  third stream offset       (8 B/vertex)
   +0x0C u32  vertex count for this batch
```

Positions are **plain xyz float32**, `count` of them, one batch per triangle strip. The stream is
padded out past the last vertex, which is why dividing a section length by a stride never worked:
there is no single array, it is a chain of strips.

**Proof, per mesh, not per file:** decoded vertex count equals the declared `vertexCount` for every
mesh of `mumbo.mps` — 175, 32, 8, 203, 65, 15, 507, all exact, 1,005 vertices in 33 strips. And
`sum(batch counts) == vertexCount` holds across the meshes checked.

The coordinates read as sense on sight: batch 0 of `jm_floor` is a 10×10 floor tile
(`10.000 0.000 0.000`, `0.000 0.000 10.000`, …) with vertices 3, 5, 7, 9 and 11 all the same point —
a fan hub.

**And it draws.** `tools/m3d2.py` decodes to triangles; a wireframe of `gokarts.mps` shows the
flagpole, its flag, the platform and the trackside barrier, and `1x1east.mps` is the faceted base of
a small Easter Island statue. That is the only test that matters for a vertex layout: a wrong stride
gives confetti.

## ⭐⭐⭐ UVs — DECODED, and the models texture

The batch's second stream is **UV, 2 × int16, fixed point ÷4096**, one pair per vertex.

The padding rule that proves it, checked on **560 of 560 batches, no exceptions**:

```
lenPositions == align16(n * 12) + 16
lenUV        == align16(n *  4) + 16
```

Both streams are the vertex data, padded to a 16-byte boundary, plus one trailing quadword (zeros
here). That trailing quadword is why dividing a stream length by a stride never gave an integer.

Sample values land in [0,1] as UVs should — (0.961, 0.934), (0.199, 0.999), (0.995, 0.199) — and a
repeated pair (0.399, 0.399) recurs at exactly the vertices whose positions repeat. Two independent
streams agreeing on which vertices are the same point.

**⚠ TEXTURES ARE PER BATCH, NOT PER MESH — I had this wrong and master caught it.** The giveaway is
one number: `mumbo.mps` has **7 meshes and 25 materials**. You cannot assign 25 textures to 7 meshes
one to one, and assigning `texture[i]` to `mesh[i]` gives every model its own correct *set* of
textures with each one in the wrong place — which is exactly what it looked like.

⚠ And `mesh entry +0x50`, which this file called `texIdx`, is **not a texture index**: it runs
0, 1, 2 … n-1 in every model. It is the mesh's own ordinal.

The real chain:

```
mesh entry +0x68   ->  a 32-byte-per-BATCH table, immediately before the batch list at +0x6C
   batch record +0x00  u32  pointer into an 8-byte-per-material table
                +0x04  u32  pointer to this batch's entry in the batch list
                +0x0C  u16  vertex count
the 8-byte table ends exactly where the 16-byte material table (header 0x40) begins, so it starts
at  matTab - 8*materialCount,  and  materialIndex = (pointer - that base) / 8
material record, 16 B: +0x0C is the offset of a `.ssh` name
```

**539 of 539 batches resolve through it.** The names confirm themselves semantically: `jm_head` uses
`mb_fang`, `mb_pl4a` and `mb_tong`; `jm_body` uses `mb_stem2b`; `jm_leaf` uses `mb_leaf`. Mumbo is a
carnivorous plant, which is why it renders purple and fleshy rather than mossy.

A `.tga` of the same stem sits beside each `.ssh`, so the source art is usable without decoding SHPS.

**And it renders.** `tools/render.py` rasterises the decoded triangles with the real textures:
`monkey.mps` comes out as a gold gorilla on a sand base with stacked wooden crates and grass edging,
`gokarts.mps` as a planked track platform. Wood grain runs along the planks and the gorilla's face
lands on the gorilla. A wrong UV interpretation smears; this does not.

⚠ **Grey patches are now batches the walker DROPS.** With per-batch materials all textures resolve
(25/25, 21/21, 20/20), but iterating batches through the new 32-byte table lost geometry: mumbo fell
from 902 triangles to 479. The indexing between the two tables is not fully right yet, and the
remaining grey is missing *triangles*, not missing textures.

⚠ **Remaining: strip restarts.** Each batch is treated as one continuous strip, which leaves a few
long spurious triangles spanning a model where a strip really restarts inside a batch. Degenerate
triangles are already dropped; the restart convention is not yet established. The UV stream is decoded (above). The THIRD
stream is still unread: its stride does not resolve cleanly, and the probe I tried — using a
repeated position as a control — turned out to be **vacuous**, because only one vertex actually
shares that position and the others merely print the same to three decimals. It compared a row with
itself and said "identical". No conclusion drawn from it.

⚠ ~~**STILL MISSING: the per-mesh vertex and face data.**~~ *(superseded — see above)* The PC member of the family puts
vertex/material/face counts at entry +0x58..+0x5E; on PS2 those words read zero, so the geometry is
reached some other way — most likely one of the other header offsets (0x40, 0x44, 0x4C, 0x70, 0x74),
which sort into an ascending chain of section boundaries. The structure is open; the geometry is not.

⚠ **AND THE PC LAYOUT IS NOT GROUND TRUTH.** Master: the PC `.MD2` work "was broken when rendering
... we never fixed/finished it". It was a useful hint for the 160-byte stride and nothing more —
every claim above is validated against the PS2 data itself.

**The two principled routes left**, either of which gives the layout outright rather than by
divination:
- the EE code that parses `.mps` — Ghidra on `SLES_500.32`, which needs no base-address work
- the **VU1 microcode**, which is the code that actually consumes the vertex buffers, and is only
  6,944 bytes across five overlays (`.DVP.ovlytab`). Its VIF unpack and addressing state the layout
  directly. Needs a VU disassembler.

## Not yet looked at

`MOVIES/*.MPC` (~285 MB), `AUDIO/**/*.MAP` + `*.SDT` bank pairs, `.gin`, `.mtr`, `ILINK.IRX` /
`ILSOCK.IRX`.
