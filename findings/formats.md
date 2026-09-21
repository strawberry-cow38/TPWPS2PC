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
   +0x00  u32       flags
   +0x04  u32       PARENT node        \
   +0x08  u32       NEXT SIBLING node   > a scene graph, all three are file offsets
   +0x0C  u32       FIRST CHILD node   /
   +0x10  f32[16]   4x4 transform, column-major — ⚠ RELATIVE TO THE PARENT
   +0x50  u32       the mesh's own ordinal (NOT a texture index)
   +0x54  u32       offset of this mesh's name (NUL-terminated, near end of file)
```

⚠⚠ **THE TRANSFORM IS RELATIVE TO THE PARENT, AND APPLYING IT AS ABSOLUTE DETACHES LIMBS.** That is
what "it's still broken" was: `m_arm` sits at (-12.92, 2.45, -0.10) *relative to its parent*, and
drawn absolutely it floats away from a body that is at (19.67, 25.28, 20.32). World transform is the
product down the chain from the root.

⭐ **The hierarchy contains nodes that are not meshes.** `monkey.mps`'s parent/child pointers reach
`0x9F0`, `0xDB0`, `0x11D0` and `0x12F0`, which lie past the end of the mesh table — and they carry
names at `+0x54` like any node: **`Head1`, `Head09`, `nose03`, `Dummy01`.** Those are 3ds Max helper
objects, kept as animation pivots. `m_body`'s first child is `Dummy01`, and both arms hang off it.

So the scene graph is: `m_base` → { `m_boxes`, `m_sign` → `m_sign2`, `m_body` → `Dummy01` →
{ `m_arm`, `m_arm1` }, `m_crate`, `m_shards` }.

**Composing the chain fixes mumbo's sign**: it renders as `MUMBO`, left to right, on a signboard —
where flat transforms had put it at the wrong place and angle.

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
mesh entry +0x68   ->  a 32-byte-per-MATERIAL-GROUP table, immediately before the batch list at +0x6C
   group record +0x00  u32  pointer into an 8-byte-per-material table
                +0x04  u32  pointer to this group's FIRST batch in the batch list
                +0x08  u16  HOW MANY BATCHES the group covers
                +0x0A  u16  total vertex count across them
the 8-byte table ends exactly where the 16-byte material table (header 0x40) begins, so it starts
at  matTab - 8*materialCount,  and  materialIndex = (pointer - that base) / 8
material record, 16 B: +0x0C is the offset of a `.ssh` name
```

**539 of 539 batches resolve through it.** The names confirm themselves semantically: `jm_head` uses
`mb_fang`, `mb_pl4a` and `mb_tong`; `jm_body` uses `mb_stem2b`; `jm_leaf` uses `mb_leaf`. Mumbo is a
carnivorous plant, which is why it renders purple and fleshy rather than mossy.

A `.tga` of the same stem sits beside each `.ssh`, so the source art is usable without decoding SHPS.

⚠ **AND THE 8-BYTE TABLE IS FLAGS, WHICH I USED AS AN INDEX WITHOUT EVER READING.** Entry `+0x00`
is a bitfield, and **bit 1 set ⟺ the texture is not in the model's own folder** — it lives in
`/Sharetex/` (309 entries, 152 TGAs, and the `%s\sharetex\` path in the binary). Perfect
correlation across all 21 materials of `monkey.mps`. The format says where to look; I was not
reading it.

⚠ **261 of the archive's TGAs are 32-bit BGRA**, not 24-bit. A loader that accepts only
`bpp == 24` silently returns nothing for them, which is what produced most of the untextured grey —
not missing files and not animation-only geometry.

## UV scale: confirmed right, mapping still wrong

The per-mesh UV ranges under ÷4096 are self-evidently sane, which rules the *scaling* out:

```
Newflag   / gk_flag3     u[0.00, 1.00]  v[0.00, 1.00]   exactly one texture
wr_base   / m_grass      u[0.00, 2.00]                  tiles twice — ground
gk_pole1  / GK_side2     u[0.00, 0.17]  v[-2.00, 1.00]  narrow strip tiling up — a pole
gk_finish / GK_top       u[0.17, 0.83]  v[0.17, 0.83]   centred cap — a pole top
```

You do not get "narrow strip that tiles vertically" on a pole by chance. Two rival scalings
(texel ÷16÷size, with and without a V flip) were rendered and both were visibly worse.

⭐ **AND THE SIGNS READ.** With the 32-bit loader fixed, `volcano.mps` renders its ride sign as
**"ErUPtIo…"** and `monkey.mps` renders **"CrAzY"** — the PSX catalogue names those rides *Eruption*
and *Crazy Ape*. Text is the most sensitive thing a texture mapping can carry: a V flip, an axis
swap or an offset all make it unreadable. Legible left-to-right ride names are the strongest
evidence available that the UV mapping is right.

Untextured triangles after the fix: **0 of 953 (monkey), 0 of 797 (gokarts), 0 of 903 (mumbo), 0 of
919 (volcano)**, with every material resolving (21/21, 20/20, 25/25, 19/19).

**Master, who can see the real game, confirmed the textures are right** once the 32-bit loader was
fixed. The earlier "still wrong" was against a render where most surfaces were grey, so the textured
minority was a biased sample of the formats the loader happened to support.

## Alpha: 32-bit means cutout, not just a wider pixel

**227 of the 261 32-bit TGAs have more than 2% fully transparent texels**, and their names say what
they are — `pl1_leaf`, `leaf1`, `leaf2`, `Lure_Leaf`. Foliage is a quad with the leaf shape cut out.

⚠ Discarding alpha does not give a slightly wrong colour, it gives an **opaque black wedge** where
the cut-out should be — which reads as stray geometry or bad winding, not as an ignored channel.
Those wedges appeared in the renders at exactly the moment 32-bit loading was switched on, and were
briefly mistaken for a geometry bug. With an alpha cutout at 16, monkey's rope fence resolves into
posts and swags and the sign backings disappear.

## Winding: front faces are CLOCKWISE

⚠⚠ **AND I REJECTED THIS HYPOTHESIS AFTER TESTING HALF OF IT.** Master suggested bad winding; I
implemented alternating strip winding with culling, saw the render get worse, and wrote it up as
"tested and rejected". I had tested **one cull parity**. The other is right.

With alternating winding and **clockwise** front faces:

| | kept of mumbo's 904 |
|---|---|
| no culling | 904 |
| cull clockwise (keep CCW) | 438 |
| **cull counter-clockwise (keep CW)** | **466** |

Keeping CW renders a complete-looking model *and* removes the artefacts: the stray triangle across
the leaves disappears, and so does the mirrored `MUMBO` sign — because that sign was a **back face**
all along. Its front points away from this camera, which is exactly master's "it feels inside out".

That also settles the last open oddity: mumbo's sign read backwards while volcano's and monkey's
read correctly because the first was being seen from behind and the others from the front. Nothing
was mirrored; the renderer simply had no notion of facing.

⚠ A global handedness flip was tested too and is **not** it — negating X or Z in world space pulls
the model apart, separating the plant from its base. The parts are assembled correctly as they are.

**And it renders.** `tools/render.py` rasterises the decoded triangles with the real textures:
`monkey.mps` comes out as a gold gorilla on a sand base with stacked wooden crates and grass edging,
`gokarts.mps` as a planked track platform. Wood grain runs along the planks and the gorilla's face
lands on the gorilla. A wrong UV interpretation smears; this does not.

⚠ **AND THE GROUP RECORD'S +0x08 IS A BATCH COUNT, NOT A CONSTANT.** I first read it as "always 1"
because the meshes I happened to print all had one batch per group, and iterating one batch per
group then lost two thirds of the geometry (mumbo 902 triangles down to 479). `jm_head` has a group
of **2** batches (69 + 64 = its 133 vertices) and `jm_body` a group of **7** (summing to its 507).
With the count honoured, mumbo is back to 904 triangles *and* correctly textured.

**Validated across the whole archive: 859 of 965 meshes** have every group's batch-vertex sum equal
its declared total, and the same 859 have their mesh totals match `vertexCount`. The remaining ~11%
are not yet explained and are the grey left in the renders — **missing triangles, not missing
textures**, since all textures now resolve (25/25, 21/21, 20/20).

## ⚠⚠ THE RENDERER'S DEPTH TEST WAS INVERTED, AND IT FAKED THREE FORMAT BUGS

Master, from the picture: *"it's like different pieces are rendering over other pieces. like the
render order is broken."* It was. The projection returned a depth and the rasteriser kept the
**smallest** value as nearest; after the yaw/pitch rotations the camera looks the other way down that
axis, so the smallest was the **furthest**. Every far surface painted over every near one.

**Three separate "findings" in this file were explanations of that one sign error**, and each was
plausible enough to write down:

1. **"Detached floating arms."** Never detached — the arms sit *behind* the body and drew on top of
   it. Reported to master as "probably the swinging arms at rest"; tinyclaw then supplied a second
   innocent explanation (an unposed animation bundle, true of the PSX side too). **Neither was
   needed.**
2. **"The mirrored MUMBO sign."** A back face winning the depth test over the front of the same
   board. Explained here as a facing artefact — nearly right, wrong mechanism: culling did not fix
   it, correct depth ordering does.
3. **"Winding is not the explanation."** Culling appeared to make things worse partly because the
   depth test was already scrambling which surface won.

⭐ **The M3D2 decode was right the whole time; the renderer was lying about it.** A decoder validated
only through a viewer inherits every bug the viewer has — and a wrong viewer does not look like a
wrong viewer, it looks like a wrong format, which is the direction you are already primed to suspect
when reverse-engineering. Master caught it four times from the picture while each internal check
kept passing.

## The bundle, labelled — and what the flags might mean

`monkey.mps` (Crazy Ape) in full, which is what a `.mps` actually is: **an asset bundle the game
picks from**, not one model.

| idx | name | flags | verts | tris | parent |
|---:|---|---|---:|---:|---|
| 0 | `m_base` | `0x000001` | 111 | 62 | root |
| 1 | `m_boxes` | `0x000f01` | 258 | 132 | `m_base` |
| 2 | `m_sign` | `0x000f01` | 8 | 4 | `m_base` |
| 3 | `m_sign2` | `0x000a01` | 16 | 8 | `m_sign` |
| 4 | `m_body` | `0x000301` | 257 | 148 | `m_base` |
| 5 | `m_arm` | `0x000321` | 103 | 70 | `Dummy01` |
| 6 | `m_arm1` | `0x000321` | 104 | 70 | `Dummy01` |
| 7 | `m_crate` | `0x000f01` | 186 | 108 | `m_base` |
| 8 | `m_shards` | `0x000f01` | 36 | 12 | `m_base` |

Helper nodes carry **bit 31** in their flags (`0x80000050`, `0x80000250`) and are not drawable:
`Head1`, `Head09`, `nose03`, `Dummy01`.

⚠ **Do not read the names as descriptions.** Rendering with pieces removed shows `m_shards` is the
**lightning-bolt sign topper**, not crate debris, and `m_boxes` is the **whole platform and fence
structure**. Both of those were guessed wrong here from the name alone.

### Which pieces are drawn at rest is NOT in the file

Master, who can see the ride: **`m_crate` and `m_shards` should not be on screen; everything else
can be.** With the answer in hand this became a diff rather than a guess — all 40 words of the
160-byte entry, hidden pair against visible set:

- **Flags do not separate them.** `m_crate` and `m_shards` are both `0x000f01` — the same value as
  `m_boxes` and `m_sign`, which *are* drawn. My earlier guess that the low bits select visibility is
  **wrong**.
- Every other apparent separator is trivially per-mesh: data offsets, bounding boxes, vertex counts.
  They differ because every mesh differs.
- The only real distinction is `+0x50`: they are meshes **7 and 8 — the last two**. And there is no
  "draw the first N" count anywhere in the header; nothing in it equals 7.

⭐ **So the visibility decision is not in the `.mps` at all**, which is exactly what master said the
format was: *"this is an asset BUNDLE. contains multiple pieces. game has to handle what to do w
them."* An importer should surface every piece and let the consumer choose; deciding it needs the
animation (`.aps`) or the game's own ride logic — the same shape as the PSX phase table in
`TPW-PSXPC`'s `findings/animation-phases.md`.

⚠ **Backface culling is wrong for these models** — master, comparing renders: no-cull is closest.
They are open single-sided pieces; there is nothing to cull correctly.

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


## M3D2: the third per-vertex stream is VERTEX NORMALS

The batch record's third pointer (`posOff, uvOff, **stream3Off**, vertexCount`) has been listed as
unknown here. It is **3 bytes per vertex, signed, over 127** — a unit normal.

`m_sign` is a flat sign of two quads, and its eight vertices read:

```
00 1d 85   x4      ->  (0,  29, -123)      29² + 123² = 15,970,  √ = 126.4 ≈ 127
00 e3 7b   x4      ->  (0, -29,  123)      the opposite face
```

Two opposed unit normals on a two-sided flat sign. The stream is `vertexCount * 3` bytes, padded
out to a 4-byte multiple.

## M3D2 mesh entry — fields confirmed while chasing the animation link

```
+0x00 u32 flags        +0x04 parent   +0x08 sibling   +0x0C child     ⚠ ABSOLUTE FILE OFFSETS
+0x10 mat4 (parent-relative)          +0x50 mesh ordinal   +0x54 name offset
+0x60 u16 vertCount, u16 faceCount    +0x68 group table    +0x6C batch table
+0x70 float4 bounds MIN               +0x80 float4 bounds MAX    (w = 1.0)
+0x94 u32 batch table length          +0x98 u32 -> ANIMATED-VERTEX LIST (0 when not animated)
```

⚠ **A parent link is an absolute offset and may point into EITHER table** — the mesh table at
`+0x48` (160-byte entries) or the helper table at `+0x4C` (96-byte entries), which share this
header layout. Crazy Ape's arms hang off `Dummy01`, **helper 24**, not off a mesh; and the animation
addresses that same node as **33** = 9 meshes + helper 24, exactly the evaluator's rule
(`node < meshCount ? mesh[node] : helper[node - meshCount]`).

A helper header is recognisable by bit `0x80000000` in its flags.


## ⚠⚠ M3D2 STRIP RESTARTS — a batch is NOT always one strip (measured 2026-09-21)

`tools/m3d2.py`'s `strips()` treats each batch as a single triangle strip. It is not, and the
renders have been carrying the damage. Swept 60 models for triangles with an edge much longer than
their own mesh's median:

```
239 stretched triangles across 60 models
worst 70x the mesh median   (Features_Sign1, mesh "Cylinder25")
and they arrive in CONSECUTIVE RUNS -- tris 14,15,16,17,18,19 of 30 in one batch
```

⭐ **A run of stretched triangles in the middle of a batch is the signature of two separate strips
concatenated into one.** The bad triangles are exactly the ones bridging the end of strip A to the
start of strip B. A single over-long triangle could be sloppy modelling; a consecutive run at one
place in the index order cannot be.

⚠ **`Gates.mps` and `monkey.mps` have ZERO stretched triangles**, which is why this was invisible
for days — the two models used for every render happen not to have the problem. Reported by the
owner looking at a *third* model.
[[feedback_exclusions_hide_the_defect]]

### ⭐⭐ SOLVED — it is the ADC bit, in bit 0 of the X position word

The hint was in the animation player all along: `FUN_001a6d68` writes a vertex's X as
`(uint)value & 0xfffffffe | old & 1`, deliberately **preserving bit 0**. A position write that goes
out of its way not to clobber one bit is carrying a flag in it.

```
A triangle spans vertices k, k+1, k+2 and is DRAWN only if bit 0 of vertex k+2's X word is CLEAR.
```

That single rule does both jobs: it restarts a strip inside a batch (the first two vertices of a new
piece are suppressed, so no triangle bridges the gap) and it kills the degenerate triangles of a
fan. The cost is one ulp on a float — invisible — which is why it can live there at all.

**And the second bug, found with it: the batch count is `mesh+0x66`.** `strips()` walked the batch
table until a record stopped looking valid, which under-ran on 13 meshes (`ENTRY_EXIT` yielded 39
triangles against a header count of 152).

### Validated against the mesh's own face count at `+0x62`

| reading | meshes matching `+0x62` |
|---|---:|
| each batch as one plain strip | 77 / 948 — **8.1%** |
| ADC-filtered, heuristic batch walk | 935 / 948 — 98.6% |
| **ADC-filtered + batch count from `+0x66`** | **935 / 935 — 100.00%** |

⭐ `+0x62` was never used for anything before this; it turns out to be the format's own checksum on
whether you are reading its geometry correctly. Every mesh in all 113 models agrees.

`tools/m3d2.py` now exposes `triangles()` and `triangle_indices()`; `strips()` is kept but
documented as not-for-rendering.


## ⭐⭐ MATERIALS ARE PER-GROUP, AND THE INDEX IS THE POINTER'S POSITION

A mesh is not one texture — it is split into **groups**, each covering a run of batches with one
material. Group records are 32 bytes starting at `mesh+0x68`:

```
+0x00 u32 -> material          +0x04 u32 -> batch table
+0x08 u8  BATCH COUNT          +0x09 u8 ?        +0x0A u16 vertex count
```

⚠ `+0x08` is a **byte**. Read as a u16 it makes `m_sign` claim 257 batches — the tell that it is
not one.

⭐ The material is never stored as an index. It is encoded by **where the group's pointer lands**
in an 8-byte-per-material table sitting immediately before the material table:

```
base     = materialTable - 8 * (materialCount + 1)
material = (group[0x00] - base) / 8 - 1
```

### It reads true on inspection

```
m_base   -> m_grass1, m_grass2, m_grass3, pp_grsph3
m_body   -> m_back, m_ear, m_foot, m_limb, m_tit, mface1
m_arm    -> m_hand, m_fing, m_banana, m_banseat, m_limb
m_crate  -> m_box1              m_shards -> m_box4
```

A monkey's arm textured with hand, fingers, a banana and a banana seat. On a second model the park
gate's `door01`/`door02` come out as `jgt_dor1`, and the gateway as walls, pillars and vines.

**Structural check: the groups' batch counts sum to exactly `mesh+0x66` on 963 meshes, 0
mismatched.**

⚠ This is what "one texture per mesh" got wrong months of renders ago — `mesh+0x50` is the mesh's
ORDINAL, not a material. The owner's report at the time was *"right texture for each model, just
wrong places entirely"*, which is exactly what a per-mesh guess produces.


## ⚠⚠ TEXTURE RESOLUTION: the ride's own folder FIRST, then `Sharetex/`

A material name is **not unique in the archive**. JUNGLE.WAD holds **22 files called
`sign_eng.tga`** — one per ride, each with that ride's name painted on it — plus `sign_ger`,
`sign_fre`, `sign_jpn` and `sign_jap` for the other languages.

A directory-wide search for `*_sign_eng.tga` returns whichever the filesystem hands back first.
That is how a textured Crazy Ape shipped **wearing Mumbo's sign**, spotted by the owner reading it.

```
resolve(material, ride):
    1. <ride>/textures/<name>.tga        the ride's own
    2. Sharetex/<name>.tga               the shared set
```

Both steps are needed: six of Crazy Ape's 21 materials (`m_grass1..3`, `m_fence`, `m_box4`,
`GK_trkunder2`) are not in its own folder at all, and `m_box4` exists in **both** `Sharetex/` and
`Sideshow/sgsquark/textures/` — so ride-first-then-shared also picks the right one of those.

⭐ The general trap: **a lookup by name across a flat extraction silently picks a wrong file rather
than failing.** It cost nothing to detect here only because the wrong texture had *words on it*.
Scope every asset lookup to the directory the referencing file came from.
[[feedback_exclusions_hide_the_defect]]


## ⭐⭐ `.plb` — the particle effect library, cracked

`/DATA/PARTICLE.WAD` (extent 36078, 301,952 B) holds **`Tp2.plb`** and **100 particle textures** in
animated sequences (`PA1a0000..0015` is a 16-frame loop, `Pa1b0000..0007` an 8-frame one).
`FUN_001f6938` loads it with
`FUN_00220800(0x2f0790, "Data\Particle\Tp2.plb", 400, 0x400)`.

```
+0x00 u32 record count   (105)
+0x04 u32 record size    (320)
records start at 0x120, each beginning with a NUL-terminated name
0x120 + 105*320 = 0x8460   (file is 35,704 bytes)
```

The record size is not inferred: the **names sit exactly 320 bytes apart**, matching the header's
own second word.

### 101 named effects

```
  0 NULL            1 Sparks          2 Smoke           3 Firework1       4 ExplodeFirey
  5 Explode2        6 Firework2       7 Explode3        8 FirePuff        9 Flies
 10 ExhaustPuff    11 Fire           12 Splash         13 SmallSmoke     14 Explode
 15 BeamUpCar      16 Smoke2         17 Steam          18 IncaGodFlame   19 BeamUp
 20 WaterFall      21 BigGreenPuff   22 ApeSnot        23 ApeSmoke       24 LargeExplosion
 25 TorchSmoke     26 AirLeak        27 SmallAirLeak   28 Volcano        29 CoasterSparks
 30 BigSparks      31 MumboPuff      32 Zzzz           33 Spray          34 DemonBreath
 35 Button         36 Notes          37 WaterJet       38 GreenSmokePuff 39 FlySmoke
 40 FlyFire        41 BloodSpurt     42 SlimeJet       43 Cannon         44 GoldenTicket
 45 DemonFire      46 PinkPop        47 BrewUp         48 PlasmaSphere   49 GoldenTicket2
 50 YellowStink    51 Repair         52 GreenPuke      53 Avatar         54 RepairData
 55 Flames         56 feathers       57 OtherAvatar    58 Bubbles        59 BuyLand
 60 FW1explosion   61 FireworkLaser  62 LaserFWexplode 63 LaserRing      64 LaserLaunch
 65 GocartFireballs 66 BigSteamJet   67 GoldSparkles   68 SmallExplosion 69 GreenFumes
 70 SteamJet       71 Engine         72 QuickSteamJet  73 MudJet         74 SideShowWin
 75 Destroy1       76 Destroy2       77 Destroy3       78 Destroy4       79 Create1
 80 Create2        81 Create3        82 Create4        83 Twinkle        84 Twinkle2
 85 TwinkleSm      86 Twinkle2Sm     87 Key            88 Key2           89 Upgrade
 90 MessageTag1    91 EndOfMessage   92 BigSmokePuff   93 SmokeTrailR    94 SmokeTrailB
 95 SmokeTrailW    96 SmallSplash    97 KeySparkle     98 KeyPuff        99 CongratSparkle
100 Test2D
```

⭐ The catalogue corroborates the `+0x24` carrier list independently: `ApeSnot`/`ApeSmoke` for Crazy
Ape, `IncaGodFlame` for Inca God, `MumboPuff` for Mumbo, `ExhaustPuff`/`GocartFireballs` for the
go-karts, `WaterFall`/`Splash`/`WaterJet` for the water ride, `Volcano`/`Flames`/`SteamJet` for the
volcano, and `CoasterSparks` for the coasters — the same rides that carry the pointer.

⚠ **Record fields are not decoded.** From about `+0x20` they are integers, not floats, and at least
some are **16.16 fixed point** (`65536` appears as a value, `0xffffffff` as a sentinel). Naming them
needs the particle system's own parser, which is not located.

## ⚠ Still missing: the link from `+0x24` to an effect index

The obvious candidate fails. `array1`'s entries are `(2k, 2)` and **that second field is a constant
`2` on every ride tested** — Fountain, LavSpurt, Volcano and GoKarts all give `2`. An effect index
would differ between a fountain and a volcano; a pair-count would not. So it is the count of
`array2` entries for that item, not an effect id.

Nothing yet read connects a `.aps` field to a `.plb` index. The library is cracked; the wiring is
not.


## ⭐ M3D2 FRONT FACES ARE CLOCKWISE — opposite to Godot's convention

Asked directly whether the winding was right, the honest first answer was **no idea**, because the
viewer had culling DISABLED. That does not answer the question, it hides it — and it is what let a
wrong parity survive for days on the PSX side.

Rendered all three parities of the same frame and compared:

| cull mode | result |
|---|---|
| BACK, unreversed winding | **hollow** — the ape's own back is visible through his chest, the plinth has no top |
| FRONT, unreversed winding | solid and correct |
| disabled | looks nearly right, with interior faces bleeding through |

So the file's front faces are **clockwise**. The fix is to emit the triangles reversed (`A, C, B`)
so ordinary back-face culling is correct, rather than to leave the engine culling the front:

⚠ **Do not "fix" winding by disabling culling.** It hides the question and it flips the normals a
generator derives from the triangle order, so the lighting is then wrong in a way that looks like a
material problem. `TPW_PS2_CULL=back|front|off` stays in the viewer to re-check this rather than
to trust it.

After reversing, the parities swap exactly as they should: cull-back renders solid, cull-front
renders hollow. That symmetry is the confirmation — a one-sided test would not have been one.


## ⭐⭐ SOLVED: the Godot viewer was lighting back faces with inverted normals

Rendering the same ride, animation and frame in both, the Python renderer shows banana clusters in
the crates and the CRAZY APE sign; the Godot viewer does not. **Unresolved.** Recorded with what
has been ELIMINATED, so the next attempt does not repeat it:

| ruled out | evidence |
|---|---|
| missing triangles | per-mesh counts are **identical** in both: 62/132/4/8/148/70/70/108/12 |
| missing textures | all **21 materials resolve**, 0 missing |
| texture tiling | `m_boxes` UVs run `u 0..4`; `TextureRepeat` was already on and setting it explicitly changed nothing |
| the morph maths | `tools/aps.py` and the older `apsrender.py` agree to **0.0000** on every animated vertex of every node at frame 134 |
| visibility gating | adding it to the Python renderer changed **0 pixels** — the only part it hides at 134 (`m_crate`) is already collapsed to a 0.03-unit sliver |
| camera framing | ⭐ was a REAL defect and is fixed (below), but the artwork is still absent after fixing it |
| the alpha cutout | disabling it makes things *worse* — the fence's cut-out quads become solid black rectangles — so `AlphaScissor` at 16/255 is right |

### ⭐ The answer, and it came from the owner describing the symptom precisely

> *"all the faces are there, its like they are textured on the wrong side half of the time"*

That is **two-sided lighting**. With culling disabled, Godot's `StandardMaterial3D` does **not**
flip the normal on a back face, so every surface seen from behind is shaded by a normal pointing
away from the light and comes out dark. Half the faces of any closed model are back faces from any
given angle — hence "half the time". The banana clusters and the fence detail were never missing;
they were unlit.

`StandardMaterial3D` has no toggle for this, so the viewer now uses a small shader:

```glsl
shader_type spatial;
render_mode cull_disabled, diffuse_lambert, specular_disabled;
uniform sampler2D albedo_tex : source_color, filter_nearest_mipmap, repeat_enable;
uniform float cutout = 0.0627;                 // 16/255, not the engine default 0.5
void fragment() {
    vec4 c = texture(albedo_tex, UV);
    if (c.a < cutout) discard;
    ALBEDO = c.rgb;
    if (!FRONT_FACING) { NORMAL = -NORMAL; }   // ⭐ the fix
}
```

It also carries the two settings this data needs and the engine defaults get wrong: `repeat_enable`
because `m_boxes` UVs run `u 0..4`, and the 16/255 cutout.

⚠ **Note which of my six eliminations was the near miss.** I had measured that disabling the alpha
cutout made things worse and concluded the material was therefore correct. It was correct *about
alpha* — and wrong about lighting. **Ruling out one property of a thing does not rule out the
thing.**

### ⭐ Fixed on the way: frame on real geometry, not the declared bounds

`FrameCamera` used `mesh+0x70`/`+0x80`. **Those bounds cover the whole MORPH RANGE** — every
position a vertex reaches across the entire animation — so the camera sat far enough back that the
model occupied ~212 px regardless of window size, even at 1600x1000. Thin geometry went sub-pixel.
Now framed on the actual transformed vertices.

⚠ That defect made the comparison itself dishonest for a while: a 158 px crop upscaled beside a
214 px one is not an A/B. Match the subject's on-screen SIZE before comparing two renderers.

### ⚠⚠ And the game's space is LEFT-HANDED

Spotted by the owner right after the lighting fix: the Godot render was a **mirror image** of the
Python one. Theme Park World's coordinate space is left-handed and Godot's is right-handed.

The conversion is applied as a **Z mirror on the scene root**, not per vertex:

```csharp
Root = new Node3D { Scale = new Vector3(1, 1, -1) };
```

⭐ At the root it conjugates the whole assembled scene at once — vertices, node transforms and the
hierarchy together — so nothing can end up half-converted, which is the usual way a handedness fix
goes wrong.

⚠ A mirror reverses triangle orientation, so front faces become back faces. That costs nothing
while the shader lights both sides via `FRONT_FACING`; it would matter immediately if culling were
re-enabled. Noted next to the code rather than left as a trap.

⚠ This was already known on the PSX side of the project ("left-handed → negate z") and it still
took the owner pointing at a mirrored picture to apply it here. **A fact recorded about one build
of a game is worth checking against every other build of it.**


## ⭐⭐ DATA.WAD: the characters — SKINNED TO A 3DS MAX BIPED (2026-09-21)

`DATA.WAD` (11.6 MB, extent 54) was the last unopened archive. 1,821 entries, 806 `.ssh`, 805
`.tga`, 34 `.mps`, 25 `.aps`. `/Chars/` holds **24 characters** — alien, Boy1a-4a, Girl1a-4a, Dino,
Fantasykid, FatMechanic, Flower, Franky, Gnome, Guard, hallowkid, Handyman, Hunter, junglekid,
Researcher, spacekid, Spaceman, Vampire — each one a `.mps` + `.aps` pair. **No new container and no
new file format**; the readers already written open them.

First run on never-seen data, triangles emitted by the ADC rule against the header's own face count:

```
Alien.mps    plackard  22v  12 faces ->  12 tris     boy1a.mps  boy1head 122v  74 ->  74
             alien1   567v 357 faces -> 357 tris                boy1legs 122v  80 ->  80
                                                                boy1body 181v 106 -> 106
```

### The rig is in the file, with the artists' own names

`/Chars/Boy1a` is 3 meshes and **26 helper nodes**, and the helpers are a 3ds Max biped:

```
Bip01 -> Bip01 Pelvis -> Bip01 Spine -> Bip01 Spine1 -> Bip01 Neck -> Bip01 Head
                                                     +- Bip01 L Clavicle -> L UpperArm -> L Forearm -> L Hand
                                                     +- Bip01 R Clavicle -> R UpperArm -> R Forearm -> R Hand
                         Bip01 Spine -> Bip01 L Thigh -> L Calf -> L Foot -> L Toe0
                                     -> Bip01 R Thigh -> R Calf -> R Foot -> R Toe0
```

The alien carries the same rig plus `Bip01 Ponytail1` / `Ponytail2`. The three meshes hang off
`Bip01` itself, not off individual bones — so they are skinned, not rigidly attached.

### ⭐⭐ The skin weights: `mesh+0x90`

Found by diffing all 40 words of the 160-byte mesh entry, character against ride: `+0x90` is the
only word that is **non-zero in every character mesh and zero in every ride mesh**.

```
mesh+0x90 -> +0x00 u16 A   vertices (the ANIMATED vertices, not the strip slots)
             +0x02 u16 B   total influences
             +0x04 u32 -> A x { u16 first, u8 count, u8 ? }
             +0x08 u32 -> B x u8     bone index, into the node table (meshes then helpers)
             +0x0C u32 -> B x f32    weight
             +0x10 u32 -> B x f32[3] the vertex, in THAT BONE's space
```

**⚠ THE INFLUENCE COUNT IS A BYTE.** Read as a u16 the run table still tiles on 5 of 7 meshes and
produces counts of 65,282 on the rest — the same trap `mesh+0x66` already carries a note about.

Measured over every `.mps` in DATA.WAD:

| test | result |
|---|---|
| run tables that tile `[0,B)` exactly | **81 / 81** |
| per-vertex weight sums | **4,382 / 4,382** within 1e-4 of 1.0 (min 0.999999, max 1.000001) |
| influences per vertex | 1 (1,874), 2 (1,979), 3 (328), 4 (201) — **never 5** |
| bone indices | 38 distinct, 0..37 |

A weight sum of exactly 1.0 on every one of 4,382 vertices is not something a wrong field offset
produces. `A` is the animated-vertex count, the same one `mesh+0x98` maps strip slots onto.

### What the bind transform is NOT (measured, so nobody re-tries these blind)

| tried | worst error / object span |
|---|---|
| bone's world matrix as composed, target = mesh-world vertices | 2.0 (median) |
| same, with the ROOT's own matrix replaced by identity (it carries a global 2.8305e-05 scale) | 1.30 (median) |
| the INVERSE of the bone's world matrix | 0.88 |
| bone matrices rebuilt from the animation's frame-0 quaternion + position keys | 0.82 |
| brute force over ALL 29 nodes, best one | 0.66 |

**No node's matrix, in any of those spaces, reproduces the bind pose.** The next step is the
evaluator itself -- the EE function that consumes `mesh+0x90` -- rather than another guess.

⚠ One more oddity to explain first: the guard's 22 animation tracks address nodes
`1,3,4,5,6,7,9,10,...,25`, skipping 0, 2, 8 and 26-28. Node 1 is the PLACKARD MESH, which the skin
says is bound to bone 16. So the `.aps` node numbering may not be the model's node numbering.

⚠ **The float3 is the vertex position and it is NOT yet placed correctly.** Solving the least
squares transform from those positions to the mesh's own vertices gives a **pure rotation** (basis
lengths 1.0000, 1.0000, 1.0000) with a residual of 0.004 against a 29,408-unit object — so the data
is right — but that transform is **not** any node's world matrix as composed here. The node matrices
are in world units (translations under 1) while vertices are in model units (tens of thousands), so
a scale factor is missing somewhere in the chain. Open.

### The 4th byte of the run record

Zero on 3,973 of 4,152 vertices; elsewhere 32, 48, 49, 56..64. Unexplained.


## ⭐ MOVIES/*.MPC — an EA chunk container around MPEG-2 (2026-09-21)

Eleven files, 285 MB. A flat stream of EA chunks, `4cc + u32 LE size`, size INCLUDING the header:

```
SCHl  audio header: platform 'PT\0\0', then a tag stream; 0xFD opens a subheader, 0x8A closes it,
      0xFF ends it. Each element is a tag byte, a length byte, then that many BIG-ENDIAN bytes.
      0x82 channels, 0x83 codec, 0x84 sample rate, 0x85 sample count.
SCCl  chunk count
SCDl  audio data
MPCh  ONE VIDEO FRAME of MPEG-2 video elementary stream
SCEl  end
```

The PS2 decodes MPEG-2 in hardware (the IPU), so EA shipped elementary stream and let the console
do the work.

| movie | bytes | MPCh | pictures | rate | audio s | fps |
|---|---|---|---|---|---|---|
| ALIEN | 27,011,556 | 1218 | 1218 | 48000 | 40.57 | 30.02 |
| BFLOGO | 6,346,344 | 286 | 286 | 48000 | 9.52 | 30.04 |
| DINO | 26,678,236 | 1203 | 1203 | 48000 | 40.07 | 30.02 |
| END_E | 7,027,700 | 319 | 319 | 48000 | 10.54 | 30.27 |
| END_F | 6,964,568 | 316 | 316 | 48000 | 10.51 | 30.08 |
| FLOWER | 31,590,596 | 1424 | 1424 | 48000 | 47.45 | 30.01 |
| FRANK | 35,989,692 | 1622 | 1622 | 48000 | 54.02 | 30.03 |
| SPACEMAN | 33,079,444 | 1491 | 1491 | 48000 | 49.68 | 30.01 |
| FE125 | 33,926,000 | 896 | 896 | 48000 | 35.84 | **25.00** |
| FE225 | 25,240,584 | 667 | 667 | 48000 | 26.68 | **25.00** |
| FE325 | 24,937,208 | 659 | 659 | 48000 | 26.36 | **25.00** |

* **All 11 tile to the last byte.** The walker raises on one trailing byte; none raised.
* **`pictures == MPCh` on every file** — 10,101 chunks, 10,101 MPEG-2 `picture_start_code`s. One
  chunk is one frame, measured, not assumed.
* **The fps column was never fed a frame rate.** It is the audio sample count against the video
  frame count, two independent numbers, and it separates the three `FE*` frontend movies at
  **exactly 25.00** from the eight story cutscenes at 30. Three files agreeing to four significant
  figures is not luck.
* Slice start codes run `0x01`..`0x16` — 22 macroblock rows, **352 px**, which independently
  confirms the `640x352` read out of the sequence header.
* `0xB5` extension codes are present, so this is MPEG-2 and not MPEG-1.

⭐⭐ **THE AUDIO CODEC IS `adpcm_ea` — EA ADPCM.** The `SCHl` header's compression field reads **7**,
and this file recorded that as unproven for a day. It took one command to settle:

```
$ ffprobe BFLOGO.MPC
Input #0, ea, from 'BFLOGO.MPC':
  Stream #0:0: Video: mpeg2video (Main), yuv420p, 640x352 [SAR 11:15 DAR 4:3], 30 fps
  Stream #0:1: Audio: adpcm_ea, 48000 Hz, 2 channels, s16, 384 kb/s
```

48000 Hz and 2 channels are exactly what the `SCHl` element stream declares, so the header reading
and the codec identification confirm each other.

⚠⚠ **AND FFMPEG OPENS THIS CONTAINER NATIVELY.** It has an `ea` demuxer that takes a raw `.MPC` and
finds both streams — no chunk walking, no wrapper, no elementary-stream extraction. Walking the
10,101 `MPCh` chunks by hand is what PROVED the format, and it is genuinely how the layout above was
established; it is not how the files get converted. **"Is this already handled somewhere?" is a
cheaper question than "what is this?", and it is worth asking first.**

Reader: `tools/mpc.py` — `disc_file(bin, extent, size)`, `chunks(data)`, `audio_header(d, off, n)`.


## ⭐⭐ AUDIO/**/*.SDT — the sound banks, SOLVED (2026-09-21)

154 files under `/AUDIO`, of which **41 are `.SDT`** and carry all the audio. Two variants, told
apart by the u16 at `+0x02`:

```
VARIANT A  (version 0)      29 files -- banks of named sounds
    u16 count, u16 0
    u32 offset[count]                  absolute; offset[0] == 4 + 4*count
    each offset points at a 40-byte HEADER followed immediately by that sound's data

VARIANT B  (version 12345)  12 files -- the same headers, moved out of the data
    u16 count, u16 12345
    u32 offset[count]                  absolute, straight at the AUDIO
    40-byte header[count]              the SAME layout
    the audio, from offset[0] == 4 + 4*count + 40*count

the 40-byte header:
    +0x00 u32 headerSize   always 40
    +0x04 u32 dataSize
    +0x08 char[16] name    NUL-padded: "Crunch.mp2", "smTree1.vag", "Level2a.mp2"
    +0x18 u32 tag          ⭐ THE TOP BYTE IS THE CODEC
    +0x1C u32 0
    +0x20 u32 ?            rises with length; not a sample count at any obvious rate
    +0x24 u32              0 in variant A; in variant B, a copy of the stream's first word
```

**⭐ The tag byte is proved, not assumed.** Cross-tabulating it against what each bitstream's own
header says, over every MPEG frame on the disc:

| tag | frames that decode as mono | as stereo |
|---|---|---|
| **0x24** | **97,667** | 0 |
| **0x25** | 0 | **169,211** |

Nothing on either off-diagonal. `0x80` is Sony PS-ADPCM, `0x00` an empty slot.

### The census — 41 files, 2,220 sounds, zero problems

| | |
|---|---|
| files whose tables end exactly where the data begins | **41 / 41** |
| MPEG streams | 1,849 |
| bytes left after walking every MPEG frame to the declared end | **exactly 1, on all 1,849** |
| `.vag` sounds | 356 |
| ...opening on the customary silent 16-byte block | **356 / 356** |
| ...whose length is a multiple of 16 | **356 / 356** |
| empty slots (name "Blank", size 0) | 15 |
| distinct sound names | 1,504 |
| **total audio** | **232 min 23 s** |

The one leftover byte per MPEG stream takes a single value across all 1,849 — it is structural
padding, not a decode error.

```
MPEG-2 Layer II  112 kbps  22050 Hz  stereo   169,211 frames   (music)
MPEG-2 Layer II   48 kbps  22050 Hz  mono      53,483 frames   (advisor speech)
MPEG-2 Layer II   32 kbps  22050 Hz  mono      24,150 frames
MPEG-2 Layer II   64 kbps  22050 Hz  mono      20,034 frames   (ride SFX)
```

So the PS2 build uses **MPEG audio for everything long and Sony ADPCM for short SPU one-shots** —
the split any PS2 title makes, here confirmed file by file rather than assumed.

Reader: `tools/sdt.py` — `offsets(d)`, `sounds(d)`, `frame_header(d, off)`.

⚠ **Still open**: the `.MAP` files beside each bank. `*BANK.MAP` is ~55 bytes, a Microsoft GUID
and a path string (`sound\Bumper`); `*SFX.MAP` is a table of u32s, a few hundred bytes to a few
kilobytes. Together they are the ID-to-sound index — what the game asks for when it wants a noise.


## ⭐⭐ `*SFX.MAP` / `*BANK.MAP` — the sound index, READ FROM THE LOADER (2026-09-21)

The `.MAP` files beside each bank are a **three-level tree with 24-, 20- and 42-byte records**, so
nothing in them is 4-byte aligned and reading them as a u32 array produces convincing nonsense —
which is exactly what a first pass produced. The layout below came out of the game's own loader
(`FUN_00249d38` → `FUN_0024b770` → `FUN_0024b810` → `FUN_0024b8d0` → `FUN_0024a030`), not out of
the bytes.

```
0x00  16-byte type GUID      00 2c61e9d0 31d211b4 0900b0c9 93f203   SFX.MAP
                             01 2c61e9d0 31d211b4 0900a0c9 93f203   BANK.MAP
      ⚠ the loader compares all four words and returns -1 on a mismatch; both constants sit in
      SLES_500.32 at 0x371360 and 0x371370, and finding THEM is what found the parser
0x10  u32 ?          0x14  u32 ?          0x18  u32 count
0x1C  the tree, DEPTH FIRST -- each level's whole array, then each element's children in turn

L1, 24 B:  +0x00 u32 childCount   +0x04 ptr->L2   +0x08 u16 flags (bit 2 cleared on load)
L2, 20 B:  +0x04 u32 childCount   +0x08 ptr->L3   +0x10 u16 flags
L3, 42 B:  +0x00 u16 n16          +0x04 u32 n8    +0x08 ptr->16B    +0x26 ptr->8B
  16-byte entry:  +0x00 u32 SOUND INDEX, 1-BASED   +0x04 0xFFFF   +0x08 u32 length in ms
                  +0x0C u16 bank, 1-based
  8-byte entry:   first u32 is a ONE-BASED index into this L2's own L3 array
                  (`*p = base + (*p-1)*0x2a`) -- the L3 records reference each other
```

### Verified

| test | result |
|---|---|
| `.MAP` files whose tree consumes the file **exactly** | **42 / 42** |
| sound indices inside their bank's range | **939 / 939** |
| `ms == round(SDT[+0x20] / (44.1 × channels))` | **939 / 939** |

The advisor's index is the cleanest single proof: `SPCHSFX.MAP` yields **170 entries whose sound
index runs 1..170, strictly increasing, with 170 distinct values**, against a `SPCHHD.SDT` of
**exactly 170 sounds**.

⭐ **And this closes the `.SDT` header's last unknown.** `+0x20` was "large, rises with length";
it is `milliseconds × 44.1 × channels`. Two files written by different tools agree to the
millisecond on all 939 entries, and the value also matches the duration obtained by walking the
MPEG frames (Level1-a: 41,730 ms declared against 41,848 ms decoded — the 0.28% is the encoder's
padding frames).

`*BANK.MAP` is a header and one length-prefixed path (`sound\Bumper`, `spch\spch`), the folder the
bank was built from.

Reader: `tools/sfxmap.py` — `parse(d)` returns the tree and the byte count it consumed.


## ⚠⚠ TWO BUGS THAT READ AS "MISSING GEOMETRY" (2026-09-21, found from the owner's screenshots)

### 1. The batch record's fourth word is `u16 count, u16 ceil(count/3)`

Not a plain u32. **563 batch records on the disc carry a non-zero high half and the ratio holds on
every one of them.** The reader carried a sanity filter — `0 < a < b < c <= len` and `n <= 4096` —
which read those counts as near a million, rejected the **first** batch and stopped, so the whole
mesh came back with no geometry. The go-karts' `TRACK` declares 692 faces and produced **0**.

Measured over every `.mps` in every WAD, against the face count each mesh declares at `+0x62`:

| | meshes correct | wrong |
|---|---|---|
| with the filter | 3,736 | **198** |
| with the low half as the count | **3,934** | **0** |

496 models, 3,934 meshes, 16 WADs, all five file versions (0x13b, 0x13c, 0x13d, 0x13e, 0x140).

⚠ **The earlier "935/935, 100%" in this file was measured on a SMALLER SET** — JUNGLE.WAD alone,
where the same filter had already emptied the meshes that would have failed. It counted 935 meshes
where the archive holds 965. The number above is the whole disc with nothing excluded.

⚠ 483 further records, all in versions 0x13b/0x13c, have a high half that is **not** `ceil(count/3)`
and is unexplained. They are reported here rather than filtered out; their meshes read correctly.

### 2. The viewer listed one model per FOLDER

33 folders hold more than one `.mps` — `/Rides/gokarts` has **twelve** (track pieces, karts,
pylons), `/Rides/wateride` thirteen, `/Generic/MiscMesh` nine. Keying the library on the folder kept
whichever came last and **hid the other 160 models on the disc**, which looks exactly like a ride
missing most of its geometry. One entry per model now; the animation beside it is matched by its own
stem first, then by the folder's single `.aps`.


## ⚠⚠ THE `.tga` DOES NOT ALWAYS EXIST — 473 MATERIALS NEED SHPS (2026-09-21)

This file has claimed since the model format was cracked that "a `.tga` of the same stem sits beside
each `.ssh`, so SHPS never needs decoding". **That was checked on JUNGLE.WAD and generalised.**
Counted over every model on the disc:

| | |
|---|---|
| material references | **6,007** |
| resolved to a `.tga` | 5,493 |
| **no `.tga` at all** | **514** |
| ...of which a `.ssh` of that name DOES exist | **473** |

`LOBBY.WAD` alone accounts for **448** of the 514 — the lobby is almost entirely `.ssh`-only, which
is why it looks the most broken in a viewer.

### The SHPS container — SOLVED. The pixels — NOT.

```
'SHPS' · u32 fileSize · u32 entryCount · char[4] platform ('GIMX')
entryCount x { char[4] name, u32 offset }          (EA left "Buy ERTS" in the padding)

entry header, 16 bytes:
  +0x00 u8  type        ⚠ BIT 0x80 IS A COMPRESSION FLAG; the real type is the low 7 bits
  +0x01 u24 blockSize   to the next block; 0 means to the end of the file
  +0x04 u16 width  +0x06 u16 height
  +0x08 centre (u16,u16)   +0x0C pos (u16,u16)
  +0x10 payload
```

Verified on three files: `sign` 64x64, `cont` 32x32, `door` 16x16, sizes agree with the archive and
the block chain tiles to the byte.

⚠⚠ Every entry is type `0x84` = compressed type 4. The payload opens `47 4d`, is high-entropy
throughout, and is **not RefPack** (`10 FB` would say so).

### The `GM` block, read out of the game's own loader (`FUN_00223fe0`)

```
+0x00 u16 'GM' (0x4D47)     -- the loader compares exactly this
+0x02 u8  width  / 16       -- 16x16 -> 01, 32x32 -> 02, 64x64 -> 04
+0x03 u8  height / 16
+0x04 u32 bits 0..19 = LENGTH of the whole GM block, header included
+0x08 the payload
```
The length matches every file exactly (32 / 320 / 480 / 2224 on four checked), and the loader masks
it with `0xFFFFF` before using it as a DMA length.

### ⚠⚠ "IT IS MPEG" — TESTED AND NOT SUPPORTED BY THE DATA

The same function ends by feeding the block to the **IPU**, the PS2's image-processing unit:

```c
REG_DMAC_4_IPU_TO_MADR = block & ~0xF;
REG_DMAC_4_IPU_TO_QWC  = len >> 4;
REG_IPU_CTRL = 0x1800000;
REG_IPU_CMD  = ofm<<27 | dte<<26 | 0x10000000 | 0x20000;
```

Reading `0x10000000` as command 1 (IDEC, intra decode), `0x1800000` as MP1 + I-picture and
`0x20000` as a quantiser scale of 2 gives "these are MPEG intra pictures". **That reading came from
a register field map recalled, not read, and the data does not bear it out.**

What was actually tried, and failed:

* **504 elementary streams** built around the payloads -- MPEG-1 and MPEG-2, 13 byte offsets, both
  slice alignments, every combination of intra DC precision, intra VLC format, alternate scan and
  q-scale type -- decoded with ffmpeg and scored against the matching `.tga`. **Best mean error 79
  per channel where random noise is ~85.** Every output was a single flat colour: the decoder found
  a valid picture header and then zero macroblocks in it.
* **The start-code census kills it outright.** Over all **5,756** GM payloads, `00 00 01 34` appears
  almost exactly once each -- but the other `00 00 01 xx` hits are **not sequential slice numbers**.
  4,366 payloads contain only the `0x34`, *whatever their height*: a 32-macroblock-row image cannot
  be one MPEG slice. The rest give sequences like `[52, 0, 3, 1]` and `[52, 2, 2, 1, 1, 1, ...]`,
  which is what `00 00 01` occurring by chance in compressed data looks like.

⭐ **The test set is ready for whoever comes back to this**: 5,493 materials ship both a `.ssh` and
a `.tga`, and several of the small ones are a single flat colour (`geen_base` is exactly
`(28,142,27)` in all 256 pixels), so a correct decode is unmistakable -- error 0, not error 20.

⚠ The next honest step is the IPU's own documentation for the IDEC command and the IPU_CTRL field
map, not another sweep.

⭐ **The test set already exists**: the 5,493 materials that ship BOTH forms mean any candidate
decoder can be scored against thousands of known-correct images instead of eyeballed.

Reader: `tools/ssh.py` — `entries(d)` walks the container and reports each entry's type, size and
payload extent.


## ⭐⭐ `.gin` = GIN4, the SIDESHOW minigames as 3D scenes (2026-09-21)

Listed in this file for months as *"14 files, 3,740,116 bytes, unknown, and the largest per-file --
267 KB average"*. Nobody had opened one.

**All 14 are under `/Sideshow/` in JUNGLE.WAD**, and the filenames do most of the work:
`sgrace/racer0..4`, `sgsquark/birdwait`, `birdwin`, `birdlose`, `hamstart`, `hamhit`, `egg`,
`scorebar`, plus a `base` per game. These are the fairground minigames — and they are **full 3D
scenes with bones and animation**, not the 2D sprite sheets the subject might suggest.

Chunked like the rest of EA's work on this disc:

```
4cc, u32 0, u32 payloadSize, payload
```

### Geometry — the six counted chunks

Each opens `u32 key, u32 count` and then `count` fixed-width records:

| chunk | stride | record |
|---|---|---|
| `POLY` | 12 | 3 × u32, triangle vertex indices |
| `MAP4` | 24 | 6 × f32, UVs, three pairs per triangle |
| `FNRM` | 36 | 9 × f32, three normals per triangle |
| `PTS4` | 12 | 3 × f32, **world-space** vertex position |
| `VECT` | 12 | 3 × f32, **object-space** vertex position |
| `NORM` | 12 | 3 × f32, per-vertex unit normal |

⭐ The strides are measured, not guessed: `payloadSize - 8 == count * stride` holds on **every
counted chunk in every file** — POLY/MAP4/FNRM 78/78 each, PTS4/VECT/NORM 29/29 each, **zero
mismatches** — and the counts corroborate each other, 805 across the three per-face chunks and 622
across the three per-vertex ones.

### ⭐ `PTS4` is `VECT` through the model's transform

`NORM` is unit-length (622/622); `PTS4` and `VECT` are not, and they are not equal. They are the
same vertices in two spaces: **`PTS4 = M · VECT + t` fits with a max residual of 3e-5 on all 14
files** — float32 exact. The exporter baked both, so a renderer can draw straight from `PTS4` with
no scene graph at all, or drive `VECT` through the node transform to animate.

⭐ **And `t` is stored.** The three floats at **`MOD4+52`** are that translation, on **28 of 28
models**, matching to every printed digit. Two independent sources: the fit never reads `MOD4`, and
`MOD4` never reads a vertex. `MOD4` also carries the model's name at `+4` (`New05`, `sq_hammer`,
`Cylinder04` — 3ds Max defaults and the artists' own tags).

The five racers make the same point from the other end: `racer0..2` are one mesh at a **pure Z
translation** apart (dx and dy exactly 0.0 on every vertex, dz a constant −36.75 then −36.45), and
`MOD4+52`'s z runs 537.46, 500.71, 464.27, 356.23, 243.41. Five lanes of a race.

### ⚠ The first u32 is a KEY, not flags

I first read `POLY`'s leading word as a flag field and called `>> 16` a submesh index. **A control
rejected that on 18 of 42 chunk sequences.** It is a packed pair:

```
low16  = model index   -> indexes the MOD4 / PTS4 / VECT / NORM records, in order
high16 = submesh index within that model
```

Verified on **14/14 files, zero failures**: the low words are non-decreasing and cover
`0..MOD4count-1` with no gaps, the high words restart at 0 for each model, `MESH`'s count equals the
total number of `POLY` groups, and the `PTS4` count equals the `MOD4` count. `sgrace/base` is 4
models with 1, 1, 4, 1 submeshes; `birdwait` is 4 with 5, 2, 2, 1.

### ⭐ `TREE` is the scene graph: N × 92-byte nodes, 3ds Max TRS

No count header — the payload is exactly `payloadSize / 92` node records, and **92 divides every
TREE in all 14 files**. Each record:

```
+0   name, NUL-terminated ASCII      ("Scene Root", "sq_hammer", "New05", "Box17")
+32  position   3 × f32
+44  rotation   4 × f32  quaternion
+60  scale      3 × f32
+72  scale-axis 4 × f32  quaternion     <- the 3ds Max ScaleValue, so this came out of Max
+88  u32
```

Both quaternions are unit-length on **every node of every file** — the check that makes the offsets
real rather than plausible.

⭐ **This is the rotation/scale half of `M`.** The fit gave racer0 `diag(0.5927)` with no rotation;
`TREE` lists `New05` with scale `(0.5927, 0.5927, 0.5927)` and an identity quaternion. So the whole
transform is now accounted for: **`M` from `TREE`'s rotation × scale, `t` from `MOD4+52`.**

`TREE`'s position is the node's own position and is *not* `MOD4+52` — `New05` sits at
`(375.692, 152.769, 418.256)` in `TREE` against `(329.142, 150.686, 537.460)` in `MOD4`. They are
different quantities, node placement versus the affine offset that lands object verts in world
space. **`MOD4+52` is the one a renderer wants**, and it is the one proven 28/28.

⚠ Some words in a node record are `0x00a5xxxx` and `0x1108e6fe` — PS2 main-RAM addresses left in the
file. Runtime fixup slots, not data. Do not read them as floats.

### `OBJ4` = the drawable nodes

Every `OBJ4` is the **UPPERCASE** form of a `TREE` node name, with **zero orphans across all 14
files**. The nodes that never get one are exactly the non-renderable kinds:

| node | files |
|---|---|
| `Scene Root` | 14 / 14 |
| `Dummy01` | 10 / 14 |
| `KID01` | 1 / 14 |

The root, a Max dummy helper, and a character placeholder. So `OBJ4` is the render list, which is
why its count (1..31) tracks neither the model count nor the submesh count.

### What the node names say the games are

`sgsquark` is not whack-a-mole. Its nodes are `sq_post`, `sq_hammer`, `sq_arrow`, `sq_fence`,
`Box01..Box22` and a bird built from `sq_body`, `sq_head`, `sq_neck`, `sq_feat` — a **high striker**,
with the 22 boxes as the score ladder and the bird at the top. `hamstart`/`hamhit` are the hammer
before and after the swing, `birdwait`/`birdwin`/`birdlose` the bird's three reactions, `egg` two
spheres. `sgrace` is five racers and a `Spray01`.

### ⭐⭐ `ANIM` is 95% of the bytes, and it is three streams

I first wrote this format up as "solved" with every chunk tag identified. **That was 3.5% of the
bytes.** `ANIM` alone is 95% and I had walked straight past it. Tag names recognised is not a
decode; bytes accounted for is.

`ANIM`'s first word is a kind. **One frame count `F` explains every `ANIM` chunk in a file — 14/14**:

```
kind 1   u32 1, u32 0, u32 F,  then F x nverts x 3 f32     per-vertex WORLD position cache
kind 6   u32 6, u32 0, u32 F,  then F x 56                 one node's TRS per frame
kind 7   u32 7,                then F x nnodes x 56        every node's TRS per frame
```

A TRS record is the same 56 bytes as a `TREE` node's transform block:
`pos +0 (3 f32) · rotation quat +12 · scale +28 (3 f32) · scale-axis quat +40`.

⭐ The control: **74,448 quaternions across all 14 files, zero non-unit, worst `|q|²-1` = 4.18e-07**,
and no record has a non-positive scale. The frame counts are corroborated independently — kinds 1
and 6 store `F` at `+8` and it matches the count the size implies, every time.

⚠ Note kind 7's header is **4 bytes**, not 12. It stores no frame count; the size and the node count
give it.

### ⭐⭐ `PTS4` IS frame 0 of the vertex cache

**On 29 of 29 models, `PTS4` equals frame 0 of that model's kind-1 `ANIM`, to an error of exactly
0.000000** — bit-identical, not merely close.

This is the format's real invariant and it is **stronger than the transform relation**, which holds
on only 28 of 29. The exception is `scorebar`'s `sq_arrow`, the marker that flies up the striker: it
misses `M · VECT + t` by 3.08 where every other model sits at 3e-5. Nothing is wrong with it —
`PTS4` is a baked animation frame, and for 28 models frame 0 happens to also be the rest pose, so
both relations hold. `sq_arrow`'s animation simply does not start at rest.

⚠ **I previously reported that transform test as "28/28".** It was 28 of 29: `sq_arrow` has 3
vertices, too few to fit a 12-parameter affine, and my harness skipped it and then printed the
filtered count as if it were the population. tinyclaw caught it off an independent extraction. The
skipped case was the only interesting one — as it usually is.

Playing the cache back confirms the decode end to end: `racer0` is 513 frames of a closed lap, `y`
pinned at 151.17 throughout, finishing 8.8 units from where it began.

### `PART` is a per-frame scalar track — 14/14

```
u32 count, u32 ?, u32 F, then F x f32        (count 0 -> the chunk is just that one word)
```

`size == 12 + F*4` on every non-empty one, **`F` equals the file's frame count every time**, and
every value is `1.0`. Present but empty in the 8 `sgsquark` files; real only in `sgrace`. A
visibility or opacity track — the same idea as `.aps`'s `track+0x28` appear-frame, which gates
drawing on the rides.

### `KEY4` is sparse TRS keyframes — 38 of 43

```
16-byte header (an empty KEY4 is exactly that header: u32 kind, 0, 0, 0)
then, repeatedly:  u32 nkeys, nkeys x (u32 frame, 56-byte TRS)
```

The TRS is the same record as everywhere else. **526 quaternions, zero non-unit**, and every frame
index lands inside the file's frame count. `0xFFFFFFFF` appears as a frame and is a sentinel — the
same `-1` that cost me the `.aps` bind-pose test when I read an `0xffff` ease field as a quaternion
component.

⚠ **Five chunks do not parse** and I am not going to keep tuning the walk until they do — the first
version of this hypothesis fitted 2 of 43 and happened to succeed on the one file I had read by eye,
which is what overfitting looks like. Four of the five stop dead within the first 44 bytes, so it is
a layout variant, not a bad stride:

| file | size | consumed |
|---|---|---|
| `sgsquark_base` | 4280 | 44 |
| `sgsquark_egg` | 560 | 44 |
| `sgsquark_hamhit` | 796 | 24 |
| `sgsquark_hamstart` | 6284 | 24 |
| `sgsquark_scorebar` | 6440 | 6440, frame out of range |

### `BONE` — shape only, not decoded

12 chunks, **only in the three bird files**, four each — one per model (`sq_body`, `sq_head`,
`sq_neck`, `sq_feat`). Each holds 43–86 strings of which 24–28 are `TREE` node names, interleaved
with floats around 0.99 and positions. It reads like a skin-weight table binding the bird's four
meshes to the striker's `Box01..Box22` node chain. **That is a description, not a decode.**

⚠ The right next step for both `BONE` and the five `KEY4` variants is **the PS2 executable's own
loader in Ghidra, not more inference.** Inference gets the shape; it does not get a
layout-switching flag, which is exactly what the five failures are.

### The rest

`GIN4` magic; `VERS` = 120 on all 14; `TEX4` texture paths — `..\..\sharedtx\+nest1.bmp`, the
artists' own tree again, and **BMP**, where the rides use TGA and SSH; `MAT4` material floats.

⚠ `TREE`, `OBJ4`, `MOD4` and `TEX4` do **not** share the `u32 key, u32 count` opening — read as a
count their second word decodes to ASCII.

⚠ Every file ends in **8 zero bytes** after the last chunk — a null terminator, not unread data.

**Bytes accounted for: 99.1%.** The remaining 0.9% is `BONE` and those five `KEY4` chunks.

Reader: `tools/gin.py` — `chunks`, `records`, `key`, `nodes`, `anim`, `part`, `keys`,
`translation`, `name`.


## ⚠⚠ TGA: two files on this disc lie about what they are (2026-09-21)

Both found by tinyclaw running an independent reader over their own extraction, against my
"5,694 of 5,695" headline. Both halves of that number were hiding something.

### `/DATA/UI.WAD/UltimateC/Star.tga` is a PNG

It opens `89 50 4E 47 0D 0A 1A 0A` and its IHDR reads 32x32, bitdepth 8, colourtype 6 (RGBA). It is
not a TGA the decoder failed on. **A perfect TGA decoder scores 5,694 of 5,694**, and reporting
5,694 of 5,695 invents a gap that does not exist. The checker now names it separately.

### ⭐⭐ Four `Sky/*_front2.tga` declare true-colour and are paletted

`FANTASY`, `HALLOW`, `JUNGLE`, `SPACE`. Each declares **cmapType 0 with kind 2 — true-colour, no
colour map — at 8bpp**, which cannot exist. The body is `1024 + 65536 + 2`: a 256-entry 32-bit
palette, then 256x256 indices.

**My decoder did not reject these. It silently produced garbage, which is worse.** With
`cmapType == 0` no palette is skipped, so the first 1,024 pixels *are the palette* and the stream
runs 1,024 bytes short; 8bpp then falls into the greyscale branch, which sets alpha to 255. Proof:
the decoder's first eight pixel values were byte-identical to the file's first eight palette bytes.

⭐ **And the alpha was the entire point of the file.** Every palette entry is `(255, 255, 255, a)` —
pure white, varying only in opacity. These four are the **cloud masks composited over
`Sky/*_back.tga`**. Rendering them opaque grey turns a transparent cloud layer into a solid sheet.
After the fix `jungle_front2` decodes as 65,536/65,536 pure white with **0 fully-opaque texels and
117 distinct alpha values** — 45,481 partial, 20,055 clear. Its honest sibling `Jungle_back.tga` is
unchanged at full colour, fully opaque, one alpha value, which is the control.

`Targa.cs` now derives the palette from the body size rather than believing the declared type.

⚠ **The lesson for `.ssh`.** A scorer that compares a candidate decode against one of these five
references is comparing against a bad reference and will blame the decoder. Check the reference
before trusting a score.

⚠ The counting lesson: the old summary counted paletted by *declared* kind and bpp as 24-or-32, so
its breakdown read `3897 + 1789 + 4 = 5690` against 5,694 decoded — **four files went down a path
the summary never named.** A breakdown that does not sum is telling you something. It now prints
the bpp split summing to the total and says how many of the paletted files declare otherwise.


## ⭐ Make every breakdown SUM — two more gaps it found (2026-09-21)

The four mislabelled sky TGAs were found because a breakdown read `3897 + 1789 + 4 = 5690` against
5,694 decoded. Applying that as a rule to the rest of `tpwps2check` found two more.

### `.aps` records: 1,180 of 1,451 were in no stated category

The summary said *"1451 records (226 skeletal, 45 whose tracks live in another file)"*. `Skeletal`
(0x20) and `Shared` (0x80) are independent bits, so counting each alone names 271 records and says
nothing about the other 1,180 — **and it cannot tell you whether the 45 are inside the 226 or
beside them.** Counting the four combinations partitions the population:

```
1225 plain + 181 skeletal-only + 0 shared-only + 45 both = 1451 of 1451
```

⭐ **Every `Shared` record is also `Skeletal`** — zero shared-only, and 226 = 181 + 45. A record
whose tracks live in another file is always a skeletal one. That is a fact about the format, and
the old pair of overlapping counts made it unreadable.

### The checker read 6,565 of 14,675 entries and never said so

It examines `.mps`, `.tga` and `.aps`. Everything else simply did not appear in any total. The
census now names every entry by extension:

| | | | |
|---|---|---|---|
| `.ssh` **5764** | `.tga` *5695* | `.rse` **718** | `.lip` **516** |
| `.mps` *496* | `.aps` *374* | `.rss` **354** | `.sam` **321** |
| `.scc` **318** | `.dat` 37 | `.sce` 29 | `.gin` *14* |
| `.bff` 6 | `.pl` 4 | `.md2` 4 | `.mtr` 4 |
| `.dba` 3 | `.lbmlist` 3 | `.table` 3 | `.h` 3 |
| `.ico` 3 | `.ass` 2 | `.gay` 2 | `.bat` 1, `.plb` 1 |

*italic* = read by the checker, **bold** = the large unexamined ones. **8,110 of 14,675 entries —
more than half the disc — are not touched by any reader**, and the biggest single extension on the
disc, `.ssh` at 5,764, is among them.

`.rse` (718), `.lip` (516), `.rss` (354), `.sam` (321) and `.scc` (318) are the unexplored bulk.


## ⭐⭐ The five "unopened formats" — three of them were never locked (2026-09-21)

The entry census turned up five extensions nobody had looked at: `.rse` 718, `.lip` 516, `.rss` 354,
`.sam` 321, `.scc` 318. A recon pass over all five took about three commands, and the headline is
that **three of them need no decoding at all**.

### ⭐⭐ `.sam` (321) — the ride design data, in plain text

`#\r\n#\tTheme Park 2 Ride Description File`. 100% printable, `key<TAB>value`, with ASCII-art blocks
fenced by `---`. The whole economic and layout model for every ride, readable as-is:

```
Info.Id                        5308
Info.Name                      "Mole Whack"
Info.Shape          ---        Info.Hoarding    ---
                    ***                         F^7
                    ***                         [.]
                    *2*                         L.J
                    ---                         ---
Info.RideTypeStringIndex       22
UsageInfo.InitPricePerUse      10      UsageInfo.InitCostOfGoods      25
UsageInfo.InitChanceOfLoosing  75      UsageInfo.ExcitementLevel      30
UsageInfo.EntryCellStandPosX   0.5     UsageInfo.EntryCellStandPosY   0.3
UsageInfo.ExitCellAppearPosX   0.5     UsageInfo.ExitCellAppearPosY   0.3
UsageInfo.MinCapacity          1       UsageInfo.MaxCapacity          1
Upgrades[0].CostOfUpgrade      2500    Upgrades[0].CostOfResearch     800
Research.Group                 3
```

⭐ `Info.Shape` is the ride's **footprint as ASCII art**, `*` a cell and `2` the entrance; `Info.Hoarding`
is its sign, also drawn. For a port this is the entire ride-tuning model handed over — prices, cost
of goods, excitement, capacity, upgrade and research costs, entry/exit stand positions — with no
reverse engineering required at all. (The developers' own typo, `InitChanceOfLoosing`, is in the
retail data.)

### ⭐ `.rss` (354) — the script SOURCE, and `.rse` (718) its compiled form

`.rss` is 100% printable and opens `#include "\source\game\rsse\code\rsse_scriptdefs.h"` — EA's own
build paths, shipped. `.rse` is the compiled output, magic `RSSE`, with `VAR_TRIGGER` / `VAR_STATUS`
readable inside.

⭐ **244 of 245 `.rss` stems have a compiled `.rse` partner, and only 5 `.rse` have no source.** That
is a known-answer corpus for a script decompiler of exactly the shape that made `.ssh` tractable —
a candidate disassembler can be scored against the source it should reproduce, not eyeballed.

### `.scc` (318) — Visual SourceSafe droppings

Every single one is named `vssver.scc` (318 of 318), magic `34 12 01 00`. EA shipped their
source-control metadata on a retail disc. **No game value; discard them from any count.**

### `.lip` (516) — lip sync, and small

8 to 152 bytes, median **16**. u32 records terminated by `FF FF FF FF`. Under `LIPS.WAD`, one per
language per line (`German/Tut_023.LIP`), so it pairs with the speech. Small enough to be a
sitting.

### What this does to the 8,110

`.sam` + `.rss` + `.scc` = **993 entries resolved or written off in three commands**, none of which
needed a decoder. `.rse` has its own answer key sitting beside it. The honest remaining unknowns in
this group are `.rse`'s opcodes and `.lip`'s record meaning.


## ⭐⭐ `.sam` cross-checked against the PSX port — and where the two versions diverge

`TPW-PSXPC` reads its ride capacities out of the PSX executable. The PS2 disc ships the same table
as text. Comparing the 20 rides the PSX port prints against `Upgrades[0..2].InitCapacity` in `.sam`:

**11 agree to the digit, 5 differ, 4 are absent from the PS2 disc.** Three independent numbers
matching exactly on 11 rides across two platforms and two extraction routes is not coincidence —
**`.sam` is the same ride table, and it is validated.**

⭐ **The split is perfectly clean on ride type.** The PSX port labels each ride with a type:

| | rides | PSX type |
|---|---|---|
| **agree** | Crazy Ape, Sun God, Mayan Spinner, Eruption, Tom Tom Twister, Rocky Racers, The Hot Pot, Belly Bounce, Mumbo, Inca Totem, Aztec Mayhem | **all 11 are type 3** |
| **differ** | Chac Atak, Gorilla Thrilla, Dino Karts, Splish Splash, Jurassic Tours | **none is type 3** (1, 1, 6, 6, 7) |

11 of 11 and 5 of 5. The flat rides carried their numbers between versions unchanged; the tracked
rides — coasters, karts, tours — were **rebalanced for PS2**. `Jurassic Tours` went 8/8/8 to 9/18/27,
`Chac Atak` 18/24/30 to 6/6/6.

⚠ So the data transfers, but **not blindly**: copying PS2 capacities onto a PSX-accurate port would
silently rebalance every tracked ride. The type-3 set is safe; the rest is a deliberate divergence
and a decision, not a lookup.

### What `.sam` has that a binary-extracted table does not

211 uniquely-named rides. Beyond capacity: `CostOfUpgrade` and `CostOfResearch` for **three upgrade
tiers**, `RedLineCapacity`, `ExcitementLevel`, `InitPricePerUse`, `InitCostOfGoods`,
`InitChanceOfLoosing`, `Research.Group`, `IsChoosable`, `OverwritePriority`, the entry/exit stand
positions, and the footprint and hoarding as ASCII art.


## `.sam` parser — the grammar, measured (2026-09-21)

`RideDefinition.Parse` in `core/TPW.PS2.Data/RideCatalogue.cs`. Four rules, each one found by a
control rather than assumed:

1. `#` opens a comment line.
2. `key` whitespace `value`, **and then optionally more text**. `UsageInfo.MinCapacity\t\t\t1\t\tper
   car` is a real line — the value is the first token and the rest is the author talking to
   themselves. Taking the whole remainder breaks 25 `InitCapacity` lines.
3. A quoted value may contain spaces: `Info.Name  "Mole Whack"`.
4. A bare key introduces a `---` fenced ASCII-art block — `Info.Shape` and `Info.Hoarding`.

⚠ **A key can repeat inside one file.** 14 of the 321 do it; `ices.sam` gives all four stand
positions twice. Last wins, which is what a line-ordered config means by an override — but nothing
in the file says so, so it is a decision and is documented as one.

⚠ A naive tab-split reports **463 distinct keys**; the whitespace grammar gives **434**. The
difference is phantom keys like `Info.RideTypeStringIndex 22`, where the separator was spaces.

⚠ There is no combined `MinCapacity / MaxCapacity` line. **Zero** lines on the disc carry a `/` in
a value. I invented that form prettifying a chat message and it was very nearly designed around.

### Known-answer controls

The self-test now carries seven rides by name with their id and three upgrade capacities, and
**returns non-zero if any of them stops reproducing**. Five agree to the digit with what TPW-PSXPC
reads out of the PSX executable; two are PS2 rebalances that deliberately disagree, so a parser
cannot pass by returning PSX numbers.

⚠ The table's first draft had five ids written from memory instead of read off the disc. The
capacities were measured and passed; the invented ids failed the instant the control ran.

```
rides: 321 .sam files, 321 fully printable, 309 named, 309 with a footprint,
       163 with a hoarding, 7568 fields
  upgrade tiers carrying a capacity: 151 / 86 / 86
  known-answer controls: 7 of 7 reproduce (id + 3 capacities each)
```


### ⭐ The id is the identity, and its leading digit is the world

309 `.sam` files carry a name. Those 309 hold **211 distinct names but 301 distinct ids**. 36 names
repeat across worlds and **32 of those 36 carry a different id in every copy**:

```
Litter Bin    FANTASY 4413   HALLOW 2411   JUNGLE 1406   SPACE 3415
Bus           FANTASY 4600   HALLOW 2600   JUNGLE 1600   SPACE 3600
End           FANTASY 4605   HALLOW 2605   JUNGLE 1605   SPACE 3605
```

Keying a catalogue by name merges four differently-priced rides into one. **`Info.Id` is the key.**

⭐ And the thousands digit is the world. Over all 305 rides with a numeric id, bands 1–4 are
**pure** — each appears in exactly one WAD and no other:

| band | world | rides |
|---|---|---|
| 1xxx | JUNGLE | 69 |
| 2xxx | HALLOW | 79 |
| 3xxx | SPACE | 72 |
| 4xxx | FANTASY | 70 |
| 5xxx | *(not a world)* | 15, spread across all four |

⚠ **Band 5 is a category, not a world.** Its 15 rides are the sideshows and every world ships its
own: `Gopher Whack` is 5303 in JUNGLE, `Mole Whack` is 5308 in FANTASY. A 5xxx ride's world is the
archive it came from, not its id.

`RideCatalogue.Load` accounts for every file rather than letting a dictionary eat the awkward ones:

```
catalogue: 321 rides, 300 distinct ids, 16 unnumbered, 5 id collisions
           id bands 1-4 pure in 4 of 4, band 5 (sideshows) spans 4 worlds
```

300 + 16 + 5 = 321.


### ⭐ A ride is a directory bundle

The `.sam` does not name its model. The ride's folder holds everything it needs:

```
/DATA/FANTASY.WAD/Features/bfbin/
    bfbin.sam      the definition
    bfbin.mps      the model
    bfbin.aps      its animation
    bfbin.RSE      the compiled script      BfBin.rss   its source
    vssver.scc     SourceSafe droppings
```

Resolution measured over all 321 definitions:

| | |
|---|---|
| `.mps` whose stem matches the `.sam` exactly | **287** |
| a single `.mps` under a different stem | 1 |
| several `.mps` and no stem match — **left ambiguous** | 12 |
| no model at all | 21 |

288 + 12 + 21 = 321. The 12 are not resolved by taking the first: a ride would get the wrong body
and nothing would say so.

⭐ The 21 without a model are **script-only entities** — `bus`, `end`, `sign1`, `firepit`,
`shooter` — carrying an `.rse` and no geometry. A missing model there is a *kind of ride*, not a
failure to find one.

⚠ **Directory case is not reliable.** The same bundle appears as `/Features/bus` and
`/features/bus`. Compare paths case-insensitively or you will find exactly one of the two — the
same trap that had 710 `.RSE` hiding behind 8 `.rse`.


## ⭐⭐ The localisation database, and the name a player actually sees (2026-09-21)

`DATA.WAD/Text/translations/`. Format found by tinyclaw; verified here independently off my own
extraction, which turned up one thing their writeup did not.

```
u32 count, count x u32 absolute offset, then NUL-terminated strings
```

On every table: first offset is exactly `4 + count*4`, offsets monotonic, last string ending
**exactly** at EOF, **1,087** rows in the same order. `id.dat` is not a language — it is the
symbolic key per row, and `include/trans.h` ships the same list a third time as a C enum.

⚠ **There are three regional trees, not one, and they hold different languages:**

| tree | languages |
|---|---|
| `eur/` | ame, dut, **fre**, ger, ita, jap, spa, swe, eng — **9** |
| `usa/` | ame, dut, ger, ita, jap, spa, swe, eng — 8, **no French** |
| `jap/` | dut, **fre**, ger, ita, jap, spa, swe, eng — 8, **no American** |

Reading "the" translations directory finds whichever one you happened to name.

⚠ Every one of the 1,087 rows carries a format field after the first space; **21 are non-empty**
and those are exactly the strings taking arguments. The other 1,066 end in a bare trailing space,
which IS the empty spec. `Formats` keeps `null` (no field) apart from `""` (empty field) for that
reason — collapsing them loses 1,066 rows' worth of "we looked and there is nothing".

### ⭐⭐ `Info.Name` is NOT the displayed name

The `STR_GRAPHICS_` keys encode the asset path, which joins the table to a ride:

```
STR_GRAPHICS_JUNGLE_RIDES_MONKEY_MONKEY -> JUNGLE.WAD/Rides/Monkey/Monkey.sam
```

**273 rides reach a row. 203 match that file's `Info.Name` and 70 DO NOT.**

```
sam 'Loudspeaker'    -> eng 'UFOs'          (SPACE/Features/spacspk4)
sam 'Loudspeaker'    -> eng 'Furry Fiends'  (HALLOW/Features/horspek1)
sam 'Garden Trowel'  -> eng 'Trowel'
sam 'Medium Bush'    -> eng 'Small Bush'
sam 'Chac Atak'      -> eng 'Chak Atak'
sam 'Gift Shop'      -> eng 'Gift Shop '
```

`Info.Name` is the **designer's internal label**; the table is what the player reads. A port that
displays `Info.Name` shows the wrong name on **roughly a quarter** of its rides.

⭐ The join is sound: matching on the full path instead of just the stem moves only **2 of 260**, so
the disagreements are a real difference between two fields rather than a mis-join.

This is the **third** independent reason not to key a ride on its name, after the 32-of-36 repeated
names carrying different ids and the 5 outright id collisions. `Info.Name` is also English-only by
construction, while the real name exists in nine languages.
