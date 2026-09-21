"""`.aps` reader — Theme Park World (PS2) animation.

Every offset is taken from the game's own code, never inferred:

  FUN_00167250  header: section count is a BYTE at +0x1C, section table POINTED TO by +0x20
  FUN_001672d8  section: sec[0] records of 0x1C bytes at sec[1]
  FUN_00167358  record: flags & 0x20 picks 20-byte tracks over 48-byte ones
  FUN_001674b0  48-byte track: eight relocated pointers, +0x10..+0x2C; +0x20 is the vertex stream
  FUN_00167758  20-byte track: pointers at +0x0C and +0x10 and NOWHERE ELSE
  FUN_001675f0  stream header: bit 0x08 of the flags picks 1-byte key times over u16
  FUN_001a6d68  playback: dequantises three SIGNED 10-bit fields out of one 32-bit word
  FUN_00167a48 / FUN_00167d18   the 20-byte track's SLERP and linear interpolators

⚠ There are TWO track formats and one flag bit chooses between them. Reading one format's field
list against the other's bytes produces fields that are "mysteriously zero" -- which is exactly the
wrong turn this decode took for an hour. See findings/animation.md.
"""
import struct

MAGIC, VERSION, FPS = 0x185AA030, 0x148, 30

_u8  = lambda d, o: d[o]
_u16 = lambda d, o: struct.unpack_from('<H', d, o)[0]
_i16 = lambda d, o: struct.unpack_from('<h', d, o)[0]
_u32 = lambda d, o: struct.unpack_from('<I', d, o)[0]
_f32 = lambda d, o: struct.unpack_from('<f', d, o)[0]


def sections(d):
    if _u32(d, 0) != MAGIC:   raise ValueError('not .aps: magic %08x' % _u32(d, 0))
    if _u32(d, 4) != VERSION: raise ValueError('unexpected version %08x' % _u32(d, 4))
    n, tab = _u8(d, 0x1C), _u32(d, 0x20)
    return [(_u32(d, tab + i*8), _u32(d, tab + i*8 + 4)) for i in range(n)]


def records(d, sec):
    count, off = sec
    return [off + i*0x1C for i in range(count)]


def record(d, r):
    return dict(off=r, flags=_u32(d, r), ntracks=_u16(d, r+8), nsmall=_u16(d, r+0x0A),
                nindex=_u32(d, r+0x0C), tracks=_u32(d, r+0x10),
                small=_u32(d, r+0x14), index=_u32(d, r+0x18))


def _sx10(v, shift):
    """(v << shift) >> 22 as the R5900 does it: sign-extend one 10-bit field."""
    x = (v << shift) & 0xFFFFFFFF
    if x & 0x80000000: x -= 1 << 32
    return x >> 22


def rotation_keys(d, off, n):
    """10 bytes: u16 time, int16 x, y, z, w -- a quaternion at 1/32768, SLERPed."""
    return [(_u16(d, off+i*10), [_i16(d, off+i*10+2+2*k)/32768.0 for k in range(4)])
            for i in range(n)]


def position_keys(d, off, n):
    """8 bytes: u16 time, int16 x, y, z -- linear."""
    return [(_u16(d, off+i*8), [_i16(d, off+i*8+2+2*k) for k in range(3)]) for i in range(n)]


def skeletal_tracks(d, rec):
    """The 20-byte form (record flags & 0x20). Keys are inline, and the sampler skins with them.
    ⚠ Unused by every one of JUNGLE.WAD's 89 files -- all 1,229 tracks there are the 48-byte kind."""
    out = []
    for i in range(rec['ntracks']):
        t = rec['tracks'] + i*0x14
        nr, np = _u8(d, t+8), _u8(d, t+9)
        kr, kp = _u32(d, t+0x0C), _u32(d, t+0x10)
        out.append(dict(node=_u16(d, t), off=t,
                        rot=rotation_keys(d, kr, nr) if kr and nr else [],
                        pos=position_keys(d, kp, np) if kp and np else []))
    return out


def morph_tracks(d, rec):
    """The 48-byte form: per-vertex morph animation. node -> [(times, [xyz per key]), ...],
    one entry per ANIMATED vertex, in the order the model's mesh+0x98 list expects them."""
    out = {}
    for i in range(rec['ntracks']):
        t = rec['tracks'] + i*0x30
        node, h = _u16(d, t), _u32(d, t + 0x20)
        if not h: continue
        byte_times = bool(_u16(d, h) & 8)
        nrec, ngroup = _u16(d, h+2), _u16(d, h+4)
        off = (_f32(d, h+0x0C), _f32(d, h+0x10), _f32(d, h+0x14))
        scale = (_f32(d, h+0x18), _f32(d, h+0x1C), _f32(d, h+0x20))
        recs, glist, vdata = _u32(d, h+8), _u32(d, h+0x24), _u32(d, h+0x28)
        sched = []
        for k in range(nrec):
            r = recs + k*12
            n, tl = _u16(d, r), _u32(d, r+4)
            sched.append((n, list(d[tl:tl+n]) if byte_times
                             else [_u16(d, tl+2*j) for j in range(n)]))
        verts, cur = [], vdata
        for g in range(ngroup):
            n, times = sched[_u16(d, glist + 2*g)]
            verts.append((times, [(_sx10(_u32(d, cur+4*k), 22)*scale[0] + off[0],
                                   _sx10(_u32(d, cur+4*k), 12)*scale[1] + off[1],
                                   _sx10(_u32(d, cur+4*k),  2)*scale[2] + off[2])
                                  for k in range(n)]))
            cur += n*4
        out[node] = verts
    return out


def sample(vertex, now):
    """One animated vertex at time `now` (frames, 30 fps), as FUN_001a6d68 blends it."""
    times, keys = vertex
    if len(keys) == 1: return keys[0]
    i = 0
    while i < len(times) - 2 and times[i+1] < now: i += 1
    t0, t1 = times[i], times[i+1]
    f = 0.0 if t1 == t0 else max(0.0, min(1.0, (now - t0) / (t1 - t0)))
    a, b = keys[i], keys[i+1]
    return (a[0] + (b[0]-a[0])*f, a[1] + (b[1]-a[1])*f, a[2] + (b[2]-a[2])*f)


def vertex_map(m, mesh_offset, nverts):
    """⭐ The link between an .aps stream and its .mps mesh, read from the MODEL, not guessed.

    `FUN_001a6d68` walks the u16 list at mesh+0x98 as it emits each animated vertex:

        do { a = *p++; write(gsPacket + (a & 0xfffc)); } while (a & 2);

    so one RUN (continuing while bit 1 is set) is one animated vertex, and each entry's
    `a & 0xfffc` is a GS address. Addresses step 12 bytes -- three words, one vertex -- so a
    slot's RANK among the sorted unique addresses is its strip-vertex index.

    Returns strip index -> animated vertex index, or None when the mesh is not animated.
    Verified on every animated mesh of monkey.aps: runs == animated vertices, 7 of 7."""
    p98 = _u32(m, mesh_offset + 0x98)
    if not p98: return None
    ent = [_u16(m, p98 + 2*k) for k in range(nverts)]
    rank = {s: i for i, s in enumerate(sorted({e & 0xfffc for e in ent}))}
    if len(rank) != nverts: return None
    idx, g = [None]*nverts, 0
    for e in ent:
        idx[rank[e & 0xfffc]] = g
        if not (e & 2): g += 1
    return None if any(v is None for v in idx) else idx


if __name__ == '__main__':
    import sys
    d = open(sys.argv[1], 'rb').read()
    secs = sections(d)
    print('%s: %d bytes, %d sections' % (sys.argv[1], len(d), len(secs)))
    for si, s in enumerate(secs):
        if not s[0]: continue
        for r in records(d, s):
            rec = record(d, r)
            kind = 20 if rec['flags'] & 0x20 else 48
            print('  sec%-2d rec@%06x flags=%08x tracks=%d (%dB)' % (si, r, rec['flags'],
                                                                     rec['ntracks'], kind))
            if kind == 48:
                for node, verts in morph_tracks(d, rec).items():
                    print('      node %-3d  %4d animated vertices, %3d frames'
                          % (node, len(verts), max(v[0][-1] for v in verts)))
