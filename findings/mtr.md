# MTR: node transforms and topology remapping

2026-09-22, PAL `SLES_500.32`. **These four files are not a texture-material table.**
Their named records are MD2 scene nodes, including cameras and helpers. All their payload bytes
are accounted for by node names, ordinals, nine matrices per node, and three index arrays per
mesh. The materials and texture filenames are inside the companion MD2 itself.

The reader now exposes that structure, validates it against the actual named MD2 nodes and
geometry, and loads the four legacy models through the viewer. This is a structural decode with
strong cross-file identity evidence, **not a claim to have found an MTR consumer in the PS2 ELF**.
The format word `6`, one header count, and seven matrices still have unknown roles.

## Start with the consumer: what the ELF actually loads

ELF SHA-256: `231771d7f39cc29e7573ce62f437af15d97737a64c1d1c49df8404ead0b7577a`.
Addresses below are virtual addresses. Reproduce the inspection with, for example:

```sh
python3 tools/r5900dis.py /path/to/ps2.elf 0x17b240 0x17b488
python3 tools/r5900dis.py /path/to/ps2.elf 0x17b6f0 0x17b794
python3 tools/r5900dis.py /path/to/ps2.elf 0x1f8678 0x1f88ac
python3 tools/r5900dis.py /path/to/ps2.elf 0x1f7ed8 0x1f8098
python3 tools/r5900dis.py /path/to/ps2.elf 0x169d48 0x169e90
```

The positive trace is:

1. The 36-byte catalog records for jungle `sgrace`, `sgsquark`, `sgwhack` are at
   `0x2c0eb4`, `0x2c0ed8`, `0x2c0efc`. They contain those specific string pointers and
   type `4` at `+8`. This anchors the trace to the sideshows, not a similarly shaped object.
2. `0x17b240` reads that catalog's name/world/type fields. Type 4 selects the entry at
   `0x2bf238`: flags `0x101`, pointer `0x362c48` to `sideshow`. The path builder at
   `0x17b344` uses `%s\%s\%s` to assemble world, category, and model name.
3. The catalog loader calls that builder at `0x17b700`, then the ordinary ride loader
   `0x1f8678` at `0x17b778`. The latter constructs an `.md2`-spelled setup name and calls
   `0x1f79d0` at `0x1f8870`. **That function does not load the MD2 bytes:** it establishes
   search paths via `0x166760`. A `.md2` string alone is not evidence of an MD2 parser.
4. The actual model load is `0x1f889c -> 0x1f7ed8`. For a stem it appends **`.mps`**
   (`0x36ab30`, call at `0x1f7fc4`), then calls `0x166088` at `0x1f8030`.
   That calls `0x169d48`, the loader which checks `0x183076E4` at `0x169dd0..dc` and
   versions `0x13e..0x140` immediately afterwards. Neither legacy MD2 nor MTR passes it.
5. This is corroborated by the disc entries: `sgrace.mps`, `sgsquark.mps`, and `sgwhack.mps`
   exist beside the legacy files and already contain texture descriptors. `glove` has no MPS.

A whole-ELF byte/string scan found no `mtr` extension, `MTR`, or literal MTR magic. A scan
using the correct `word >> 26 == 0x0f` LUI test found no construction of either legacy magic;
the same test finds all three known MPS checks. A linear R5900 scan also found no `0x358`
record increment. These are **negative search results, not a proof of global unreachability**.

Candidate loads of `+0x348/+0x34c/+0x354` were checked before assigning meanings: for example
`0x12262c..64` loads a vtable from object `+0x10` and calls its function pointers with `jalr`.
It does not follow the MTR's header `+0x14` or its index-array pointers. The similarly tempting
`0x225fc8` path uses a rectangle/grid object with dimensions at `+0x340/+0x344`; it was not
accepted as an MTR consumer either. **No function was confirmed to consume this structure.**

Consequently it would be false to describe this change as repairing PS2 minigame rendering
by adding a missing MTR material lookup. It adds support for the legacy models present on
the disc. Their exact use in the original asset pipeline remains unproved.

## Complete on-disc MTR layout

Little endian, absolute file offsets, no relocation applied by this reader. Only format word
6 is supported. The packing rules below hold for every byte of all four files.

| File in `JUNGLE.WAD/Sideshow/` | Bytes | Nodes | Meshes | Unknown +0x10 | Node table |
|---|---:|---:|---:|---:|---:|
| `sgrace/sgrace.mtr` | 22,428 | 15 | 14 | 0 | `0x2574` |
| `sgsquark/glove.mtr` | 6,144 | 4 | 1 | 0 | `0x0aa0` |
| `sgsquark/sgsquark.mtr` | 15,648 | 13 | 7 | 6 | `0x11a8` |
| `sgwhack/sgwhack.mtr` | 16,712 | 14 | 5 | 0 | `0x1278` |

Header, 24 bytes:

| Offset | Type | Meaning and evidence |
|---|---|---|
| `+00` | u32 | Stamp `0x2E5915AF`, all four files; no engine validator found |
| `+04` | u32 | Format word `6`. Possibly revision; its interpretation is **unknown**. It is not discarded, and unsupported values fail |
| `+08` | u32 | Node count; every record's name/ordinal/local matrix identifies an MD2 mesh or helper, including cameras |
| `+0c` | u32 | Mesh count; exactly the leading records with geometry arrays, and named MD2 meshes |
| `+10` | u32 | **Unknown count**, byte-value equivalent to MD2's u16 at `+0x48`. Only `sgsquark` has 6; no consumer establishes what is counted |
| `+14` | u32 | Node-table offset. `offset + nodeCount * 0x358 == fileLength` in each file; no trailer |

Node record, **856 / 0x358 bytes**:

| Offset | Type | Meaning and evidence |
|---|---|---|
| `+000` | char[256] | NUL-terminated, zero-padded node name; exact MD2 node-name identity for all 46 records |
| `+100` | u32 | One-based node ordinal, exactly MD2 node `+0x50` plus 1; not a material index |
| `+104` | f32[16] | World matrix: agrees with independently composing the named MD2 node's parent chain (audit tolerance 0.0003 per component) |
| `+144` | f32[16] | Local matrix: **byte-identical** to that MD2 node's `+0x10..+0x4f`, all 46, including nonidentity parents |
| `+184` | f32[16] | Matrix 2, role **unknown** |
| `+1c4` | f32[16] | Matrix 3, role **unknown** |
| `+204` | f32[16] | Matrix 4, role **unknown** |
| `+244` | f32[16] | Matrix 5, role **unknown** |
| `+284` | f32[16] | Matrix 6, role **unknown** |
| `+2c4` | f32[16] | Matrix 7, role **unknown**; e.g. root translations differ from the local matrix |
| `+304` | f32[16] | Matrix 8, role **unknown** |
| `+344` | u32 | Expanded/render vertex count V; each entry maps to a specific MD2 position index, see below |
| `+348` | u32 | Offset to V u32 **source face indices**, in the unduplicated face domain |
| `+34c` | u32 | Offset to V u32 **source corner indices**, each 0, 1, or 2 |
| `+350` | u32 | Expanded face count F (including backface copies) |
| `+354` | u32 | Offset to F triples of u32 source-position indices |

The arrays occupy `[0x18, nodeTable)`, packed in mesh order: V face indices, V corner
indices, F triples, then the next mesh. Helpers have all five trailing words zero. There
are no gaps or overlaps in these files. The reader rejects different packing rather than
claiming to understand it. The nine matrices are stored as 4x4 float arrays with translation
in elements 12..14. The seven unknown matrices are retained, checked for finite values and
covered by the decoded-field regression digest; they are not applied as renderer state.

### Vertex identity: the domain that initially looked wrong

The F triples include adjacent duplicate source triangles. The corresponding MD2 face normals
are **exact negatives**, component for component: 4 pairs in `sgrace`, 33 in `sgsquark`, 6 in
`sgwhack`, none in `glove`. MD2's face indices wind the copies oppositely. This is evidence
for backface duplication rather than a count-based guess.

Collapse each adjacent run of equal triples to obtain `UnduplicatedFaces`. Then, for every
expanded vertex v:

```
point = UnduplicatedFaces[VertexSourceFaces[v]][VertexSourceCorners[v]]
point == MD2.u16(mesh.vertexPositionRemap + 2*v)
```

That is an **exact position-index identity for all 1,103 vertices**. It is not a comparison of
vertex totals, approximate coordinates, or nearest neighbours. Do not apply the face indices
directly to the expanded F-triple list: that fails on eight meshes.

Concrete non-degenerate witness: `sgsquark`, node `sq_fence`, expanded vertex 6. The MTR
words at `0xe68` and `0xf08` are `(face=1, corner=1)`. The triples at `0xf90` start
`(0,1,6), (0,1,6), (6,5,0), (6,5,0)`. Unduplicated face 1 corner 1 is **point 5**,
exactly the MD2 u16 at `0x2852`. Indexing expanded face 1 would produce **point 1**.
`glove` cannot expose this error because it has no duplicated faces.

Separately, every MD2 face identifies a source triple with
`sourceFace = face.normalIndex - expandedVertexCount`. Mapping its three expanded indices
through the MD2 position-remap array yields that exact MTR triple with either `(A,C,B)` or
`(A,B,C)` order: **1,010 reversed, 43 same-order**. The audit accepts only these two orders
with A fixed, not arbitrary permutations or equality of triangle counts.

These observations explain the topology maps in this corpus. They do not prove a general
exporter algorithm for future files with arbitrary coincident faces.

## The legacy MD2 path and its actual materials

The old `Model` constructor rejected magic `0x1CD15D46`. Accepting that magic without changing
the parser would still read the wrong offsets. `Model.Md2.cs` handles the observed legacy
`+04=0xdd, +08=0xcb` layout separately. Their roles as revisions are not checked by a known
PS2 consumer. The following are the fields used by the reader; this is not a complete MD2 spec.

| Header field | Legacy MD2 interpretation |
|---|---|
| u16 `+36` | Material slots |
| u16 `+42`, `+44` | Total nodes, mesh nodes |
| u16 `+48` | Counter mirrored in MTR; meaning unknown |
| u32 `+50` | Eight-byte material-reference table; slot 0 begins **at the table**, no MPS sentinel |
| u32 `+54` | Sixteen-byte texture descriptors: u16 count at +10, u32 pointer at +12 to 20-byte names |
| u32 `+70`, `+74` | Mesh table (160-byte records), helper table (**88**, not MPS's 96-byte records) |

Nodes share flags at +0, parent/sibling/child links at +4/+8/+12, local matrix at +0x10,
zero-based ordinal at +0x50, and name pointer at +0x54. Only the parent link is interpreted
by the new renderer path; unsupported graph/geometry references fail rather than falling back.
Mesh-specific fields:

| Mesh field | Interpretation and independent checks |
|---|---|
| u16 `+58`, `+5a`, `+5c`, `+5e` | Source positions, material groups, faces, expanded vertices |
| u32 `+60` | Positions in four-wide blocks: XXXX YYYY ZZZZ, 48 bytes per block, rounded up to four positions |
| u32 `+64` | float3 normals: V vertex normals followed by F face normals; every geometric face has positive dot product with its own stored face normal |
| u32 `+68` | UVs in four-wide blocks: UUUU VVVV, 32 bytes per block, rounded up to four expanded vertices |
| u32 `+6c` | Sixteen-byte groups: material pointer +0, u16 first face +4, u16 face count +6; remaining eight bytes retained in raw model data, not interpreted |
| u32 `+70` | Eight-byte faces: u16 normal index, then three u16 expanded vertex indices |
| float3 `+78`, `+84` | Authored bounds min/max used for viewer framing |
| u32 `+94` | V u16 expanded-vertex -> source-position indices, corroborated independently by MTR's face/corner mapping |

Groups are not stored in face order. For `sgsquark.MD2` / `sq_body`, file order is:

| Group | Explicit faces (inclusive) | Material slot | First texture choice |
|---|---|---:|---|
| 0 | 0..27 | 6 | `sq_body.TGA` |
| 1 | 28..51 | 9 | `sq_head2.tga` |
| 2 | 52..53 | 11 | `sq_feather.TGA` |
| 3 | 82..85 | 10 | `sq_mouth.TGA` |
| 4 | 54..69 | 8 | `sq_neck.tga` |
| 5 | 70..81 | 7 | `sq_tail.TGA` |

**Identity witness, predicted from pointers before rendering:** `sq_body` is the MD2 node at
`0x3390`, matched to MTR node `0x1858` by name, ordinal, local matrix and geometry. Group 2 at
`0x2008` points to `0x110`. The material-reference base is `0xb8`, so `(0x110-0xb8)/8=11`.
Its explicit range starts at face 52. Descriptor `0x364` names `sq_feather.TGA` at `0x250`.
Face 52 at `0x2468` is `(normal=178, vertices=40,41,42)`.

The production resolver confirms the named pair:

```
sgsquark.MD2 / sq_body / face 52
 -> material 11: sq_feather.TGA
 -> AssetLibrary.TextureNear("/Sideshow/sgsquark/sgsquark.MD2", "sq_feather.TGA")
 -> /DATA/JUNGLE.WAD/Sideshow/sgsquark/Textures/sq_feather.TGA
 -> decoded 32x32 RGBA
```

The other fixed witnesses are `sgrace/new02/face 56 -> dinohead1.tga`,
`glove/ears01/face 0 -> gloves.tga`, and `sgwhack/hammer/face 16 -> gb_hammer2.tga`.
The headless Godot audit checks these particular surfaces' bound textures, geometry and
transform origins after the production `AnimatedModel.SetFrame(0)` path.

`AssetLibrary` now lists `.MD2` models and pairs only their own stem's `.mtr`. `LoadModel`
validates the companion, and `Viewer` uses that method for building and reframing. A broken
companion throws; it is not silently ignored. **No MTR value overrides a material slot.**
The nearby APS is paired only with MPS; applying it to MD2's different node/vertex tables
would be an unsupported animation conversion. Legacy models display their authored static pose
and first texture choice, including the four-choice `sq_head2/3/4/sq_eye` material.

### The GIN neighbours are a useful negative control

The existing `tools/gin.py` walk was used to inspect node names and geometry, not to assume
the neighbouring scenes were the same export. `racer3.gin` names `New02`, but its VECT has
39 positions with different coordinates, whereas MD2 `new02` has 47. `sgsquark/base.gin`
names `sq_post`, `sq_fence`, `Object02`; it is not the MD2's seven-mesh scene. These files do
not supply a byte-for-byte reference render for the MD2/MTR pair. A similar name alone was
not accepted as a material or topology match.

## Reproducible gates and their limits

No game files are committed; all readers in `core/` are managed and use no subprocesses.

```sh
dotnet run --project tools/TPW.PS2.MtrAudit -- /path/to/disc.bin
dotnet run --project tools/TPW.PS2.MtrAudit -- /path/to/disc.bin --inject-material-swap
dotnet run --project tools/TPW.PS2.MtrAudit -- /path/to/disc.bin --require-all-textures
dotnet build game/TPWPS2Viewer.csproj
TPW_PS2_DISC=/path/to/disc.bin /path/to/godot-4.6.2-mono --headless --path game res://tests/MtrAudit.tscn
```

The default data gate passes all four named pairs, 46 nodes/local and world matrices, 1,103
vertex identities, 1,053 triangle identities/material assignments/normal orientations, and
63 negative controls. It inventories every WAD so an omitted/extra MTR path fails. The
controls include truncation, illegal and overlapping pointers, bad ordinals, matrix/name
changes, source-index changes, UV component swaps, and **legal material swaps preserving every count**.
`--inject-material-swap` exercises the actual top-level failure exit: it exits **2** on
`sq_body` face 0 changing from material 6 `sq_body.TGA` to material 7 `sq_tail.TGA`.

`identities.json` records independently inspected material ranges and ordered texture names
for every mesh in every file. Its MTR digest was generated with an independent Python byte
walk: per node, name + LF, decimal ordinal + LF, nine lines of comma-separated uppercase
8-digit matrix word hex, then three lines of decimal face indices, corner indices, flattened
triangle indices. SHA-256 hashes the UTF-8 text including every LF. The C# audit regenerates
this from decoded objects. This catches a reader changing/swapping any matrix or index while
preserving counts; it is **regression evidence**, not independent proof of unknown semantics.
The independently generated MD2 geometry digest similarly serializes each mesh name, then
all vertices as eight float-bit hex words (XYZ, UV, normal XYZ), then each face's three indices
and material slot as decimal words, one comma-separated record per LF-terminated line. This
covers positions, UV orientation, normals, topology and material assignment beyond aggregate checks.

All 47 legacy texture choices go through the real resolver. **44 resolve; three do not:**
`sgrace`'s `dinobody.tga` and `dinohead.tga`, and `sgsquark`'s fourth head choice `sq_eye.tga`.
The default gate requires that exact missing set, so a newly missing/resolved asset fails for
review. It never invents `dinobody0` as an alias. `--require-all-textures` exits **2** on the
three unresolved choices. The default passing result must not be described as complete texture
coverage. The Godot gate passes the four explicit surface witnesses; no framebuffer pixels
or original-game screenshots are compared.

Regression checks also passed: the viewer build; the existing full-disc reader audit (496
MPS files, zero failed mesh-face comparisons); and the existing Godot texture-animation
audit's 146 surface checks across five models and four worlds.

Still **not established**:

- Any PS2 MTR consumer, or a PC engine consumer. The traced PS2 sideshow load reaches MPS.
- The expansion of the acronym MTR, whether it was an exporter intermediate, and when/how it
  was used. Node-transform/topology metadata is its measured content, not a recovered class name.
- Whether `+04=6` is a version, a mode, or something else. Constancy is not evidence of irrelevance.
- The meaning of `+10`, even though it matches MD2 +0x48 and is nonzero only in `sgsquark`.
- The roles of matrices 2..8. Their shapes/repetitions do not authorize names such as inverse
  bind, pivot, skin, or animated transform.
- A general duplicate-face/export rule beyond these four files, or compatibility with other revisions.
- Legacy animation, material lighting/blending flags, and restoration of the three missing texture
  choices. The viewer uses its existing texture-alpha shaders, not reconstructed original render state.
- Visual equivalence to the original game. The audits establish data and surface identities, not that.
