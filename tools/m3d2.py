"""M3D2 (.mps) reader — the PS2 Theme Park World mesh format.
header 0x00 u32 magic 0x183076E4 | 0x04 u32 version | 0x30 u16 meshCount | 0x48 u32 meshTable
mesh entry, 160 B: +0x10 mat4 | +0x50 texIdx | +0x54 nameOff | +0x60 u16 vertCount, u16 faceCount
                   +0x6C u32 batchTable | +0x94 u32 batchTableLen
batch, 16 B: u32 posOff, u32 bOff, u32 cOff, u32 vertCount
             positions = vertCount xyz float32 at posOff (stream padded out to a vertex multiple)
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
    looking valid. The heuristic walk stopped early on 13 meshes and over-ran on others."""
    out = []
    for j in range(mesh['nbatches']):
        a, b, c, n = struct.unpack_from('<4I', m, mesh['batchTable'] + j*16)
        if not (0 < a < b < c <= len(m)) or n == 0 or n > 4096: break
        out.append((a, b, c, n))
    return out


def strips(m, mesh):
    """Each batch's positions, in strip order. ⚠ Use `triangles`, not this: a batch is NOT one
    strip, and reading it as one bridges unrelated pieces (see `triangles`)."""
    return [[struct.unpack_from('<3f', m, a + k*12) for k in range(n)]
            for a, b, c, n in batches(m, mesh)]


def triangles(m, mesh):
    """⭐ The mesh's real triangles, as (v0, v1, v2) positions.

    A batch is a triangle strip with the PS2's **ADC bit** in it: **bit 0 of the X position word**
    means "do not draw a triangle ending at this vertex". It is how the format restarts a strip
    inside a batch (the first two vertices of a new piece are suppressed) and how it kills the
    degenerate triangles of a fan.

    The position value is a float, so the flag costs one ulp -- invisible -- which is why the
    runtime writer is careful to preserve it: `FUN_001a6d68` stores X as
    `(uint)value & 0xfffffffe | old & 1`.

    ⭐⭐ Validated against the mesh's own face count at +0x62: reading each batch as one plain strip
    matches it for **8.1%** of meshes; filtering by the ADC bit AND taking the batch count from
    +0x66 matches for **935 / 935 = 100.00%**."""
    out = []
    for a, b, c, n in batches(m, mesh):
        pos = [struct.unpack_from('<3f', m, a + k*12) for k in range(n)]
        adc = [struct.unpack_from('<I', m, a + k*12)[0] & 1 for k in range(n)]
        for k in range(n - 2):
            if adc[k+2]: continue                   # ADC: this triangle is not drawn
            i0, i1, i2 = k, k+1, k+2
            if k & 1: i1, i2 = i2, i1
            out.append((pos[i0], pos[i1], pos[i2]))
    return out


def triangle_indices(m, mesh):
    """Same as `triangles`, but as indices into the concatenated per-batch vertex list -- what a
    renderer needs when an animation replaces vertex POSITIONS by index."""
    out, base = [], 0
    for a, b, c, n in batches(m, mesh):
        adc = [struct.unpack_from('<I', m, a + k*12)[0] & 1 for k in range(n)]
        for k in range(n - 2):
            if adc[k+2]: continue
            i0, i1, i2 = k, k+1, k+2
            if k & 1: i1, i2 = i2, i1
            out.append((base+i0, base+i1, base+i2))
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
        adc = [struct.unpack_from('<I', m, a + k*12)[0] & 1 for k in range(n)]
        for k in range(n):
            u, v = struct.unpack_from('<2h', m, b + k*4)
            uvs.append((u/4096.0, v/4096.0))
        for k in range(n - 2):
            if adc[k+2]: continue
            i0, i1, i2 = k, k+1, k+2
            if k & 1: i1, i2 = i2, i1
            tris.append((base+i0, base+i1, base+i2, batch_mat.get(j)))
        base += n
    return tris, uvs
