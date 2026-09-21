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

⚠ **NOT verified**: the audio `codec` field reads **7** and what 7 means has not been proved.
Stereo and 48 kHz are certain; the codec is not.

Reader: `tools/mpc.py` — `disc_file(bin, extent, size)`, `chunks(data)`, `audio_header(d, off, n)`.
