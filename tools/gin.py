"""`.gin` -- GIN4, the fairground SIDESHOW scene format. Solved 2026-09-21.

All 14 live under `/Sideshow/` in JUNGLE.WAD: `sgrace/racer0..4`, `sgsquark/birdwait`, `birdwin`,
`birdlose`, `hamstart`, `hamhit`, `egg`, `scorebar`, plus a `base` per game. The fairground
minigames, as full 3D scenes with bones and animation. See findings/formats.md for the proofs.

    chunk:  4cc, u32 0, u32 payloadSize, payload
    counted payload:  u32 key, u32 count, count x record

    POLY  12 B  3 x u32   triangle vertex indices
    MAP4  24 B  6 x f32   UVs, three pairs per triangle
    FNRM  36 B  9 x f32   three normals per triangle
    PTS4  12 B  3 x f32   WORLD-space vertex position
    VECT  12 B  3 x f32   OBJECT-space vertex position
    NORM  12 B  3 x f32   per-vertex unit normal

    GIN4 magic   VERS 120   MESH   MAT4   ANIM / KEY4   BONE   PART (open)
    TREE  the scene graph: N x 92 B nodes, NO count header. name at +0, pos +32, rot quat +44,
          scale +60, scale-axis quat +72 -- a 3ds Max node TRS. Both quats unit on every node.
    MOD4  64 B, name at +4, WORLD TRANSLATION at +52 (3 x f32)
    OBJ4  36 B, the UPPERCASE name of a TREE node = the DRAW LIST. Zero orphans across all 14;
          only `Scene Root`, `Dummy01` and `KID01` never get one.
    TEX4  texture paths, .bmp

⭐ `PTS4 = M * VECT + t` to 3e-5 on all 14 files. `t` is the float triple at `MOD4+52` (28 of 28
models); `M` is the TREE node's rotation x scale. Draw straight from PTS4 for a static scene, or
drive VECT through the transform to animate.

⚠ A node record holds `0x00a5xxxx` / `0x1108e6fe` words -- PS2 main-RAM addresses left in the file,
runtime fixup slots. Not floats, not data.

⚠ The leading u32 of a counted geometry chunk is a KEY, not flags: `low16` = model index (into the
MOD4 / PTS4 / VECT / NORM records in order), `high16` = submesh within that model. Reading it as a
flag word and `>> 16` as the submesh index fails on 18 of 42 chunk sequences.

⚠ Every file ends in 8 zero bytes after the last chunk -- a terminator, not unread data.
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

def key(d, off):
    """A counted geometry chunk's leading word, unpacked: (modelIndex, submeshIndex)."""
    v = U32(d, off)
    return v & 0xFFFF, v >> 16

def translation(d, off):
    """The world translation stored in a MOD4 record."""
    return struct.unpack_from('<3f', d, off + 52)

def name(d, off):
    """A MOD4 record's model name."""
    return bytes(d[off+4:off+52]).split(b'\0')[0].decode('latin-1')

def nodes(d, off, size):
    """TREE's scene graph: one dict per node. No count header -- size is always a multiple of 92."""
    out = []
    for i in range(size // 92):
        b = off + i * 92
        out.append({
            'name':  bytes(d[b:b+24]).split(b'\0')[0].decode('latin-1'),
            'pos':   struct.unpack_from('<3f', d, b + 32),
            'rot':   struct.unpack_from('<4f', d, b + 44),
            'scale': struct.unpack_from('<3f', d, b + 60),
            'axis':  struct.unpack_from('<4f', d, b + 72),
        })
    return out
