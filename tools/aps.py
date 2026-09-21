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
    """10 bytes: u16 time, then a quaternion at 1/32768, SLERPed.

    ⚠⚠ The order is **x, y, z, w**. Reading it as w,x,y,z was tried on 2026-09-21 because one sign
    on one building stood up under it, and it broke almost every other ride -- see Animation.cs.
    ⚠ Every check this format has -- |q| == 32767, 49,839 unit quaternions -- is invariant under
    permuting the four fields, so NONE of them constrains the order either way."""
    return [(_u16(d, off+i*10), [_i16(d, off+i*10+2+2*k)/32768.0 for k in range(4)])
            for i in range(n)]


def position_keys(d, off, n):
    """8 bytes: u16 time, int16 x, y, z -- linear."""
    return [(_u16(d, off+i*8), [_i16(d, off+i*8+2+2*k) for k in range(3)]) for i in range(n)]


def skeletal_tracks(d, rec):
    """The 20-byte form (record flags & 0x20). Keys are inline, and the sampler skins with them.
    ⚠ Unused by every one of JUNGLE.WAD's 89 files -- all 1,229 tracks there are the 48-byte kind.
    DATA.WAD's characters are where it is used: 226 records, 4,268 tracks.

    ⚠⚠ FLAG 0x80 MEANS THE TRACKS ARE NOT IN THIS FILE. Boy2a/3a/4a carry 15 records each that
    declare 22 tracks and a NULL track pointer; they reuse Boy1a's animation and ship at 15 KB
    against its 68 KB. The split is exact over all 265 records: 0x80 set <=> tracks == 0, with no
    record on either off-diagonal. Reading one as data walks off the end of the file."""
    if not rec['tracks']: return []
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


def scale_track(d, off, count):
    """The track's `+0x18` array: a NODE SCALE track. 143 of 1,229 tracks carry one.

    Gate is **track flag 0x80**, count is the u16 at **track+0x0A** (`FUN_001a7f48`), and
    `FUN_001a6808` interpolates it:

        key  = base + index * 0x10          <- 16-byte stride
        out  = key[+0x04, +0x08, +0x0C] as float3, LINEAR between two keys

    `FUN_001a4410` then applies it by **renormalising each of the matrix's three basis vectors to
    the interpolated length** -- it sets the axis scales and leaves the rotation alone.

    Validated: times at key+0x00 ascend on **143/143** tracks (92.3% from zero), and the float3 at
    +0x04 has a **median of exactly 1.0000** -- which is what a scale track looks like and what the
    control offset (+0x00, 62.5% plausible vs 94.2%) does not."""
    return [(_u16(d, off + k*16), [_f32(d, off + k*16 + 4 + 4*j) for j in range(3)])
            for k in range(count)]


def spline_path(d, off):
    """The track's `+0x10` object: a CATMULL-ROM SPLINE PATH -- how a ride moves a thing along a
    curve. 333 of 1,229 tracks in JUNGLE.WAD carry one.

    Layout, from `FUN_001a7f48` and the interpolators it calls:

        +0x00 u32 flags     bit 0x02 -> index scaled (i*3+1)   bit 0x08 -> picks FUN_001adda0
        +0x04 u16 POINT COUNT   (the modulo the interpolator wraps on)
        +0x06 u16 key count     (passed to FUN_001a6878 with a 4-byte stride)
        +0x08 u32 -> control points, **float3, 12-byte stride**
        +0x0C u32 -> keys, 4-byte stride

    ⭐ `FUN_001ade90` is a textbook Catmull-Rom, verbatim:

        0.5 * ( (-p0 + 3*p1 - 3*p2 + p3)*t^3 + (2*p0 - 5*p1 + 4*p2 - p3)*t^2 + (-p0 + p2)*t + 2*p1 )

    and every index is taken **modulo the point count**, so the neighbours wrap -- the curve is
    treated as a loop.

    The 12-byte stride is not inferred: the decompiled code multiplies the index by `0xc` and reads
    `[0]`, `[1]`, `[2]` as floats. (A smoothness check on the data agrees -- mean step per extent
    0.22 at stride 12 against 1.23 at stride 8 -- but it is weak corroboration, not the evidence:
    many paths are only four points long, where that ratio is ~0.33 by construction.)"""
    flags = _u32(d, off)
    npts, nkeys = _u16(d, off+4), _u16(d, off+6)
    pts, keys = _u32(d, off+8), _u32(d, off+0x0C)
    points = [tuple(_f32(d, pts + k*12 + 4*j) for j in range(3)) for k in range(npts)] if pts else []
    return dict(flags=flags, npoints=npts, nkeys=nkeys, points=points, keys=keys)


def catmull_rom(points, i, t):
    """FUN_001ade90, with the same wrap."""
    n = len(points)
    p0, p1, p2, p3 = (points[(i-1) % n], points[i % n], points[(i+1) % n], points[(i+2) % n])
    out = []
    for j in range(3):
        a, b, c, e = p0[j], p1[j], p2[j], p3[j]
        out.append(0.5 * ((-a + 3*b - 3*c + e)*t*t*t + ((2*a - 5*b + 4*c) - e)*t*t + (-a + c)*t + 2*b))
    return tuple(out)


def _all_pointers(d):
    """Every address anything in the file points at, sorted. Arrays are laid out contiguously, so
    the next pointer above an array's start is where that array ENDS -- which is how you get a key
    count for the arrays that do not carry one (tinyclaw's trick, 2026-09-21).

    ⚠ It must be EVERY pointer, not just the ones of the kind you are bounding: a `+0x14` array can
    be followed by a vertex stream or an appear-frame object, and bounding only against other
    `+0x14` pointers would swallow them."""
    P = set()
    for sec in sections(d):
        if not sec[0]: continue
        P.add(sec[1])
        for r in records(d, sec):
            rec = record(d, r)
            for k in ('tracks', 'small', 'index'):
                if rec[k]: P.add(rec[k])
            if rec['flags'] & 0x20 or not rec['tracks']: continue
            for i in range(rec['ntracks']):
                t = rec['tracks'] + i*0x30
                if t + 0x30 > len(d): break
                for k in range(8):
                    q = _u32(d, t + 0x10 + 4*k)
                    if q: P.add(q)
                # ⚠⚠ `track+0x20` IS A UNION and the flags pick the meaning. 81 character tracks
                # carry a pointer there under flag 0x40000 (AlternatePlayer) and it is NOT a morph
                # header; walking one as if it were reads a record count out of unrelated bytes and
                # runs off the end of the file.
                if not (_u32(d, t + 4) & 0x1000): continue
                h = _u32(d, t + 0x20)
                if not h or h + 0x2C > len(d): continue
                for o in (8, 0x24, 0x28):
                    q = _u32(d, h + o)
                    if q: P.add(q)
                recs = _u32(d, h + 8)
                for k in range(_u16(d, h + 2)):
                    q = _u32(d, recs + k*12 + 4)
                    if q: P.add(q)
    return sorted(P)


def array_end(d, off, pointers=None):
    """Where the array starting at `off` stops: the next pointer in the file, or EOF."""
    import bisect
    P = pointers if pointers is not None else _all_pointers(d)
    i = bisect.bisect_right(P, off)
    return P[i] if i < len(P) else len(d)


def rotation_track(d, off, count):
    """The track's `+0x14` array: NODE ROTATION, the most-used animation in the format
    (548 of 1,229 tracks in JUNGLE.WAD -- more than the vertex morph stream's 290).

    From `FUN_001a9e50(t, base, index, out)`, the only consumer:

        key  = base + index * 0x0C                 <- 12-byte stride
        next = index + (index < count - 1)          <- clamped, so the last key holds
        components at key+4, +6, +8, +10, each * 3.051851e-05  (= 1/32768)
        SLERP via FUN_00167a48, or a cheap per-component LERP when a global flag is set

    So a key is 12 bytes: u16 time, u16 (unknown), then int16 x, y, z, w.

    ⭐ `count` IS in the file: the u16 at **track+0x08**, and the gate is **track flag 0x08**.
    Both from `FUN_001a7f48`, which primes the sampler's global from that same field. The count
    agrees with `array_end`'s pointer-contiguity bound on **548/548** tracks -- two independent
    derivations, no disagreement -- and every track with a `+0x14` has flag 0x08 (548/548).

    ⚠ The `u16` at key+0x02 is an INDEX into the track's `+0x2c` array (8-byte entries), with
    `0xffff` meaning none.

    Validated: reading the quaternion at +0x04 with a 12-byte stride gives |q| == 32767 for
    94.3% of 2,190 sampled keys, against 7.1% for the same stride read at +0x00."""
    out = []
    for k in range(count):
        b = off + k*12
        out.append((_u16(d, b), [_i16(d, b+4+2*j)/32768.0 for j in range(4)]))
    return out


def appear_frames(d, recoff):
    """node -> the frame it first appears. The track's `+0x28` object holds it as a u16 at +0x02.

    ⚠ FOUND BY CORRELATION AND CONFIRMED BY PREDICTION, NOT BY DISASSEMBLY. The object is 8 bytes
    for exactly `m_crate` and `m_shards` in monkey.aps section 0 and 4 for every other track, and
    the values are crate 28 / shards 100 / body 100 -- 100 being the frame the crate bursts.
    Gating drawing on them reproduces what the real game does (verified in-game by the owner:
    a closed crate and nothing else until the burst, then ape + flying debris).
    The consuming instruction has not been located; treat this as evidence, not proof."""
    rec = record(d, recoff)
    out = {}
    for i in range(rec['ntracks']):
        t = rec['tracks'] + i*0x30
        p28 = _u32(d, t + 0x28)
        if p28: out[_u16(d, t)] = _u16(d, p28 + 2)
    return out


def visibility(d, recoff, pointers=None):
    """node -> (appear frame, disappear frame or None).

    The `+0x28` object is 4 bytes for a part that only appears, and **8 for one that also goes
    away**; in the 8-byte form the int16 at `+0x04` is NEGATIVE and its magnitude is the frame it
    vanishes. Crazy Ape: the crate appears at 28 and disappears at 100 (the moment the ape bursts
    out and smashes it); the shards appear at 100 and vanish at 138.

    Validated on all 29 eight-byte objects in JUNGLE.WAD: **29/29** have that field negative and
    its magnitude strictly after the appear frame. The control field at `+0x06` manages 62%."""
    P = pointers if pointers is not None else _all_pointers(d)
    rec = record(d, recoff)
    out = {}
    for i in range(rec['ntracks']):
        t = rec['tracks'] + i*0x30
        p28 = _u32(d, t + 0x28)
        if not p28: continue
        appear = _u16(d, p28 + 2)
        gone = None
        if array_end(d, p28, P) - p28 >= 8:
            v = _i16(d, p28 + 4)
            if v < 0: gone = -v
        out[_u16(d, t)] = (appear, gone)
    return out


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
