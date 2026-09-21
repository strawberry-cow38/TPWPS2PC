"""`.gin` -- GIN4, the fairground SIDESHOW scene format. Structure solved 2026-09-21.

All 14 live under `/Sideshow/` in JUNGLE.WAD, and the names say what they are: `sgrace` has
`racer0..4` (five files of identical size), `sgsquark` has `birdwait`/`birdwin`/`birdlose`,
`hamstart`/`hamhit`, `egg` and `scorebar`. They are the minigames, as full 3D scenes with bones and
animation -- not the 2D sprite sets the extension might suggest.

CHUNKS -- the same shape as the rest of EA's formats on this disc:

    4cc, u32 0, u32 payloadSize, payload

and every payload of a counted chunk opens `u32 ?, u32 count` before its records:

    GIN4  magic          VERS  version (120 on all 14)   TREE  named node tree, "Scene Root" first
    OBJ4  object, 36 B, carries an UPPERCASE name        MOD4  model, 64 B, carries a mixed-case name
    MESH  mesh header
    POLY  count x 3 u32   triangle vertex indices
    MAP4  count x 6 f32   UVs, three pairs per triangle
    FNRM  count x 9 f32   three normals per triangle
    PTS4  count x 3 f32   vertex positions
    VECT  count x 3 f32   a second per-vertex vector, meaning not established
    NORM  count x 3 f32   per-vertex unit normals
    TEX4  texture paths, e.g. `..\..\sharedtx\+nest1.bmp`  -- the artists' own tree again, and BMP
    MAT4  material floats      ANIM / KEY4  animation      BONE  bones      PART  ?

⭐ The strides are not guessed: `payloadSize - 8 == count * stride` holds on **every counted chunk
in every file** -- POLY 78/78, MAP4 78/78, FNRM 78/78, PTS4 29/29, VECT 29/29, NORM 29/29 -- and the
counts corroborate each other, 805 triangles across the three per-face chunks and 622 vertices
across the three per-vertex ones.

⚠ Each file's chunk walk stops 8 bytes short of the end, consistently on all 14. There is a trailer
and it is not read.
"""
import struct

U32 = lambda d, o: struct.unpack_from('<I', d, o)[0]
STRIDE = {b'POLY': 12, b'MAP4': 24, b'FNRM': 36, b'PTS4': 12, b'VECT': 12, b'NORM': 12}

def chunks(d):
    """(tag, payloadOffset, payloadSize). Stops at the 8-byte trailer."""
    p = 0
    while p + 12 <= len(d):
        tag = d[p:p+4]; size = U32(d, p+8)
        if p + 12 + size > len(d): break
        yield tag, p + 12, size
        p += 12 + size

def records(d, tag, off, size):
    """A counted chunk's records as tuples of the right width, or None for the others."""
    st = STRIDE.get(tag)
    if st is None or size < 8: return None
    n = U32(d, off + 4)
    if size - 8 != n * st: raise ValueError('%s: %d != %d*%d' % (tag, size-8, n, st))
    base = off + 8
    if tag == b'POLY':
        return [struct.unpack_from('<3I', d, base + i*12) for i in range(n)]
    w = st // 4
    return [struct.unpack_from('<%df' % w, d, base + i*st) for i in range(n)]

def strings(d, off, size):
    """The NUL-separated names inside a chunk like TEX4, MOD4 or OBJ4."""
    out, cur = [], bytearray()
    for b in d[off:off+size]:
        if 32 <= b < 127: cur += bytes([b])
        else:
            if len(cur) >= 3: out.append(cur.decode('latin-1'))
            cur = bytearray()
    if len(cur) >= 3: out.append(cur.decode('latin-1'))
    return out
