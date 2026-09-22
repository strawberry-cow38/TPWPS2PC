"""M3D2 (.mps) reader — the PS2 Theme Park World mesh format.
header 0x00 u32 magic 0x183076E4 | 0x04 u32 version | 0x30 u16 meshCount | 0x48 u32 meshTable
mesh entry, 160 B: +0x10 mat4 | +0x50 texIdx | +0x54 nameOff | +0x60 u16 vertCount, u16 faceCount
                   +0x6C u32 batchTable | +0x94 u32 batchTableLen
batch, 16 B: u32 posOff, u32 uvOff, u32 normalOff, u16 vertCount (+u16 ceil(n/3))
             positions = vertCount xyz float32 at posOff; uvs int16/4096 pairs; normals 3 x int8/127
             ⭐ bit 0 of X = ADC (triangle ending here not drawn); bit 0 of Y = FACING of that
             triangle (1: faces its right-hand normal, 0: faces the other way) -- see `triangles`
"""
import struct, re
NAME = re.compile(rb'[ -~]{1,31}\0')

def meshes(m):
    assert struct.unpack_from('<I', m, 0)[0] == 0x183076E4, "not M3D2"
    tbl = struct.unpack_from('<I', m, 0x48)[0]
    for i in range(struct.unpack_from('<H', m, 0x30)[0]):
        o = tbl + i*160
        mat = struct.unpack_from('<16f', m, o+0x10)
        tex, noff = struct.unpack_from('<2I', m, o+0x50)
        nverts, nfaces = struct.unpack_from('<2H', m, o+0x60)
        bt, btlen = struct.unpack_from('<I', m, o+0x6c)[0], struct.unpack_from('<I', m, o+0x94)[0]
        nbatches = struct.unpack_from('<H', m, o+0x66)[0]
        groups_p = struct.unpack_from('<I', m, o+0x68)[0]
        yield dict(name=NAME.match(m, noff).group()[:-1].decode(), tex=tex, mat=mat,
                   nverts=nverts, nfaces=nfaces, nbatches=nbatches, groups=groups_p,
                   batchTable=bt, batchTableEnd=bt+btlen)

def batches(m, mesh):
    """The mesh's batches, using the COUNT AT mesh+0x66 rather than walking until a record stops
    looking valid. The heuristic walk stopped early on 13 meshes and over-ran on others.

    ⚠⚠ THE FOURTH WORD IS `u16 vertexCount, u16 ceil(vertexCount/3)`, NOT A PLAIN u32. 563 batch
    records on the disc carry a non-zero high half, and the ratio holds on every one of them. An
    earlier "0 < a < b < c and n <= 4096" sanity filter read those as counts near a million,
    rejected the FIRST batch and `break`ed -- so the whole mesh came back with no geometry at all
    and looked like missing artwork. A silent reject reads exactly like missing data; that filter
    is where the bug was, which is the one thing I had already written down.
    ⚠ 483 more records, all in the older file versions 0x13b/0x13c, carry a high half that is NOT
    ceil/3 and are still unexplained -- they are reported, not filtered."""
    out = []
    for j in range(mesh['nbatches']):
        o = mesh['batchTable'] + j*16
        if o + 16 > len(m): break
        a, b, c, n = struct.unpack_from('<4I', m, o)
        n &= 0xFFFF
        if n == 0 or max(a, b, c) >= len(m): break
        out.append((a, b, c, n))
    return out


def strips(m, mesh):
    """Each batch's positions, in strip order. ⚠ Use `triangles`, not this: a batch is NOT one
    strip, and reading it as one bridges unrelated pieces (see `triangles`)."""
    return [[struct.unpack_from('<3f', m, a + k*12) for k in range(n)]
            for a, b, c, n in batches(m, mesh)]


def _strip_triangles(m, a, n, base=0):
    """The drawn triangles of one batch as index triples, in OUTWARD (counter-clockwise-front)
    order, from the two flags the VU1 microcode reads off each vertex -- see `triangles`."""
    out = []
    words = [struct.unpack_from('<2I', m, a + k*12) for k in range(n)]
    for k in range(2, n):
        if words[k][0] & 1: continue                # ADC: the triangle ending here is not drawn
        i0, i1, i2 = k-2, k-1, k
        if not words[k][1] & 1: i1, i2 = i2, i1     # faces AWAY from its right-hand normal
        out.append((base+i0, base+i1, base+i2))
    return out


def triangles(m, mesh):
    """⭐ The mesh's real triangles, as (v0, v1, v2) positions in OUTWARD order: the right-hand
    normal of each triple is the side the game shows.

    A batch is a triangle strip and vertex k carries TWO flags in the low bit of its floats:
      X bit 0 -- ADC: the triangle (k-2, k-1, k) is not drawn. Restarts a strip inside a batch
                 and kills a fan's degenerate triangles.
      Y bit 0 -- FACING: 1 = the triangle (k-2, k-1, k) faces its own right-hand normal, so that
                 is the outward order; 0 = it faces the other way, outward order (k-2, k, k-1).

    ⚠⚠ THERE IS NO STRIP PARITY. This used to swap every odd triangle (`if k & 1`), which is what
    a strip means on hardware with a winding convention. The GS has none; the game culls in its
    VU1 microprogram (SLES_500.32 `.vutext`, strip loops at L00ab-L00c4 and L00e8-L0106 --
    `tools/vu1dis.py`): it takes the screen-space orientation of (k-2, k-1, k) from OPMULA/OPMSUB,
    reads the sign flag of its Z (FMAND 0x20), and forces ADC on when that sign differs from Y bit
    0. Per-triangle, authoritative. The parity guess pointed half of jungle's ground DOWN.

    Validated on the data (`tools/winding_check.py`): every ground triangle of jungle's terrain_1
    faces up under this rule (2,618 / 2,619), and it agrees with the stored vertex normals on
    60,084 / 60,537 triangles across all 112 jungle models (99.25%).

    Each flag costs one ulp, which is why the runtime writer `FUN_001a6d68` stores X AND Y as
    `(uint)value & 0xfffffffe | old & 1`.

    ⭐⭐ Validated against the mesh's own face count at +0x62: reading each batch as one plain strip
    matches it for **8.1%** of meshes; honouring ADC AND taking the batch count from +0x66 matches
    for **935 / 935 = 100.00%**."""
    out = []
    for a, b, c, n in batches(m, mesh):
        pos = [struct.unpack_from('<3f', m, a + k*12) for k in range(n)]
        out += [(pos[i0], pos[i1], pos[i2]) for i0, i1, i2 in _strip_triangles(m, a, n)]
    return out


def triangle_indices(m, mesh):
    """Same as `triangles`, but as indices into the concatenated per-batch vertex list -- what a
    renderer needs when an animation replaces vertex POSITIONS by index."""
    out, base = [], 0
    for a, b, c, n in batches(m, mesh):
        out += _strip_triangles(m, a, n, base)
        base += n
    return out


def _unused_old_strips(m, mesh):
    p, out = mesh['batchTable'], []
    while p + 16 <= mesh['batchTableEnd']:
        a, b, c, n = struct.unpack_from('<4I', m, p)
        if not (0 < a < b < c <= len(m)) or n == 0 or n > 4096: break
        out.append([struct.unpack_from('<3f', m, a + k*12) for k in range(n)])
        p += 16
    return out

def xform(mat, v):
    x, y, z = v
    return (mat[0]*x + mat[4]*y + mat[8]*z  + mat[12],
            mat[1]*x + mat[5]*y + mat[9]*z  + mat[13],
            mat[2]*x + mat[6]*y + mat[10]*z + mat[14])


def materials(m):
    """The model's material names, in index order."""
    ntex = struct.unpack_from('<H', m, 0x22)[0]
    tab  = struct.unpack_from('<I', m, 0x40)[0]
    out = []
    for i in range(ntex):
        no = struct.unpack_from('<I', m, tab + i*16 + 12)[0]
        out.append(NAME.match(m, no).group()[:-1].decode() if 0 < no < len(m) else None)
    return out


def groups(m, mesh):
    """⭐ A mesh is split into GROUPS, each covering a run of batches with ONE material.

    The material is not stored as an index -- it is encoded by WHERE the group's pointer lands in
    an 8-byte-per-material table that sits immediately before the material table:

        base     = materialTable - 8 * (materialCount + 1)
        material = (group[0x00] - base) / 8 - 1

    Group records are 32 bytes, starting at mesh+0x68: +0x00 material pointer, +0x04 batch table,
    **+0x08 u8 batch count** (a BYTE -- read as a u16 it makes `m_sign` claim 257 batches),
    +0x09 u8 unknown, +0x0A u16 vertex count.

    ⚠ This is why "one texture per mesh" was wrong: `mesh+0x50` is the mesh's ORDINAL, not a
    material. Reads true on inspection -- Crazy Ape's arm comes out as hand, fingers, banana and
    banana-seat, and the park gate's doors as `jgt_dor1`.

    Yields (materialIndex, firstBatch, batchCount)."""
    ntex = struct.unpack_from('<H', m, 0x22)[0]
    base = struct.unpack_from('<I', m, 0x40)[0] - 8*(ntex + 1)
    g = mesh['groups']
    if not g: return
    first, k = 0, 0
    while first < mesh['nbatches'] and k < 64:
        o = g + k*32
        mptr = struct.unpack_from('<I', m, o)[0]
        nb   = m[o + 8]                      # a BYTE, not a u16
        if not nb: break
        idx = (mptr - base)//8 - 1
        yield (idx if 0 <= idx < ntex else None), first, nb
        first += nb; k += 1


def triangle_indices_mat(m, mesh):
    """`triangle_indices` plus the material index and UVs: (i0, i1, i2, materialIndex).
    UVs come back separately as a per-strip-slot list, int16 / 4096."""
    batch_mat = {}
    for mi, first, nb in groups(m, mesh):
        for j in range(first, first + nb): batch_mat[j] = mi
    tris, uvs, base = [], [], 0
    for j, (a, b, c, n) in enumerate(batches(m, mesh)):
        for k in range(n):
            u, v = struct.unpack_from('<2h', m, b + k*4)
            uvs.append((u/4096.0, v/4096.0))
        tris += [(i0, i1, i2, batch_mat.get(j)) for i0, i1, i2 in _strip_triangles(m, a, n, base)]
        base += n
    return tris, uvs


def world_transforms(m):
    """⭐⭐ Each mesh's WORLD matrix: its own matrix composed up the PARENT CHAIN.

    ⚠⚠ `mesh['mat']` alone is NOT where a mesh ends up. Every node carries a parent at `+0x04` (an
    absolute offset into the mesh table or the helper table at `header+0x4C`), and the renderer --
    `Model.WorldTransforms`, which `AnimatedModel` uses to build the geometry you actually see --
    walks that chain. Skipping it gave `monkey.mps` an extent of 53.6 where the drawn model is a
    tenth of that, because its root `m_base` carries a 0.1 scale that every other part inherits.

    Measuring a model without this does not look wrong: every ride comes out consistently too big,
    the camera frames whatever it is handed, and two people using the same shortcut agree with each
    other. Column-major, so the parent multiplies on the RIGHT.
    """
    HELPER = struct.unpack_from('<I', m, 0x4C)[0]
    local, parent = {}, {}

    def add(o):
        local[o] = struct.unpack_from('<16f', m, o + 0x10)
        parent[o] = struct.unpack_from('<I', m, o + 4)[0]

    offs = []
    tbl = struct.unpack_from('<I', m, 0x48)[0]
    for i in range(struct.unpack_from('<H', m, 0x30)[0]):
        o = tbl + i * 160
        offs.append(o); add(o)
    o = HELPER
    while o + 0x60 <= len(m) and struct.unpack_from('<I', m, o)[0] & 0x80000000:
        add(o); o += 0x60

    def mul(a, b):
        """a * b for ROW-MAJOR 4x4 stored as a flat 16-tuple, translation in elements 12..14.

        ⚠⚠ This was written with column-major indexing (`a[k*4+r] * b[c*4+k]`) over row-major data.
        The basis came out right, so every scale check passed -- `1x1east` composed to 0.1 in both
        implementations -- while the child's translation never picked up the parent's scale:
        `head` landed at (4.675, 2.000, 5.000) against the C# reader's (0.468, 0.200, 0.500),
        exactly 10x, because the 0.1 root was missing from it.

        That is why extents disagreed while scales agreed, and why the disagreement looked random
        across models: it only shows where a scaled parent has children offset from it. A model
        whose parts sit at the origin composes identically either way.
        """
        out = [0.0] * 16
        for r in range(4):
            for c in range(4):
                out[r * 4 + c] = sum(a[r * 4 + k] * b[k * 4 + c] for k in range(4))
        return tuple(out)

    world = {}

    def resolve(o, depth=0):
        if o in world: return world[o]
        p = parent.get(o, 0)
        w = local[o] if (p == 0 or p not in local or depth > 32) else mul(local[o], resolve(p, depth + 1))
        world[o] = w
        return w

    return {o: resolve(o) for o in offs}
