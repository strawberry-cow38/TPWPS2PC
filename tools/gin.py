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

    GIN4 magic   VERS 120   MESH   MAT4
    PART  u32 count, u32 ?, u32 F, then F x f32 -- one scalar per frame, F == the file's frame
          count on 14/14. count 0 means the chunk is that single word. Visibility/opacity.
    KEY4  16 B header, then repeatedly: u32 nkeys, nkeys x (u32 frame, 56 B TRS). Sparse
          keyframes. 38 of 43 chunks parse; 0xFFFFFFFF is a sentinel frame.
          ⚠ 5 chunks use a layout variant this does not cover -- read the PS2 loader, don't guess.
    BONE  NOT DECODED. 12 chunks, only the 3 bird files, 4 each = one per model. TREE node names
          interleaved with ~0.99 floats; reads like a skin-weight table. Shape only.
    ANIM  95% OF THE FILE. First word is a kind; ONE frame count F explains every ANIM in a file:
      kind 1  u32 1, u32 0, u32 F, then F x nverts x 3 f32   per-vertex WORLD position cache
      kind 6  u32 6, u32 0, u32 F, then F x 56               one node's TRS per frame
      kind 7  u32 7,               then F x nnodes x 56      every node's TRS per frame  (4 B hdr!)
    TRS record, 56 B: pos +0 (3 f32), rot quat +12, scale +28 (3 f32), scale-axis quat +40.
      74,448 quaternions checked, ZERO non-unit, worst |q|^2-1 = 4.18e-07.
    TREE  the scene graph: N x 92 B nodes, NO count header. name at +0, pos +32, rot quat +44,
          scale +60, scale-axis quat +72 -- a 3ds Max node TRS. Both quats unit on every node.
    MOD4  64 B, name at +4, WORLD TRANSLATION at +52 (3 x f32)
    OBJ4  36 B, the UPPERCASE name of a TREE node = the DRAW LIST. Zero orphans across all 14;
          only `Scene Root`, `Dummy01` and `KID01` never get one.
    TEX4  texture paths, .bmp

⭐⭐ `PTS4` IS frame 0 of the model's kind-1 ANIM cache -- 29 of 29 models, error exactly 0.000000.
That is the format's real invariant.

⭐ `PTS4 = M * VECT + t` also holds, with `t` = `MOD4+52` and `M` = the TREE node's rotation x scale
-- but on 28 of 29. `scorebar`'s `sq_arrow` misses by 3.08 because `PTS4` is a baked ANIMATION FRAME
and its animation does not start at rest. ⚠ Do not report that test as 28/28: the 29th has 3 verts,
too few to fit a 12-parameter affine, and a harness that skips it and prints the filtered count is
hiding the only interesting case.

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

def anim(d, off, size, nverts=None, nnodes=None):
    """An ANIM chunk decoded. Returns (kind, frames):

    kind 1 -> frames[f][vertex]   = (x, y, z)        world-space position cache
    kind 6 -> frames[f]           = one TRS dict
    kind 7 -> frames[f][node]     = a TRS dict
    """
    kind = U32(d, off)
    def trs(b):
        return {'pos':   struct.unpack_from('<3f', d, b),
                'rot':   struct.unpack_from('<4f', d, b + 12),
                'scale': struct.unpack_from('<3f', d, b + 28),
                'axis':  struct.unpack_from('<4f', d, b + 40)}
    if kind == 1:
        n = nverts or U32(d, off + 4)
        f = U32(d, off + 8)
        return kind, [[struct.unpack_from('<3f', d, off + 12 + (i * n + j) * 12)
                       for j in range(n)] for i in range(f)]
    if kind == 6:
        f = U32(d, off + 8)
        return kind, [trs(off + 12 + i * 56) for i in range(f)]
    if kind == 7:
        n = nnodes
        if not n: raise ValueError('kind 7 needs nnodes -- its header carries no count')
        f = (size - 4) // (n * 56)
        return kind, [[trs(off + 4 + (i * n + j) * 56) for j in range(n)] for i in range(f)]
    raise ValueError('unknown ANIM kind %d' % kind)

def part(d, off, size):
    """PART's per-frame scalar track. Empty -> []."""
    if size <= 4 or U32(d, off) == 0: return []
    n = U32(d, off + 8)
    return [struct.unpack_from('<f', d, off + 12 + i * 4)[0] for i in range(n)]

SENTINEL = 0xFFFFFFFF

def keys(d, off, size):
    """KEY4's sparse TRS keyframes, as a list of tracks, each a list of (frame, trs).

    Raises on the five chunks that use the layout variant this does not cover, rather than
    returning a half-walked result -- see findings/formats.md.
    """
    out, p, end = [], off + 16, off + size
    while p < end:
        if p + 4 > end: raise ValueError('KEY4: %d bytes left, need 4' % (end - p))
        n = U32(d, p); p += 4
        if n * 60 > end - p: raise ValueError('KEY4: track of %d keys overruns' % n)
        track = []
        for i in range(n):
            b = p + i * 60
            track.append((U32(d, b), {
                'pos':   struct.unpack_from('<3f', d, b + 4),
                'rot':   struct.unpack_from('<4f', d, b + 16),
                'scale': struct.unpack_from('<3f', d, b + 32),
                'axis':  struct.unpack_from('<4f', d, b + 44)}))
        out.append(track); p += n * 60
    if p != end: raise ValueError('KEY4: walk ended at %d, chunk ends at %d' % (p, end))
    return out
