"""M3D2 skinning: the descriptor at mesh+0x90, and the bind-pose check that proves it.

  mesh+0x90 -> +0x00 u16 A   vertices (the ANIMATED vertices, not the strip slots)
               +0x02 u16 B   total influences
               +0x04 u32 -> A x { u16 first, u8 count, u8 ? }
               +0x08 u32 -> B x u8    bone index, into the node table (meshes then helpers)
               +0x0C u32 -> B x f32   weight; each vertex's weights sum to 1
               +0x10 u32 -> B x f32[3] the vertex's position in THAT BONE's space

⚠ The influence count is a BYTE. Read as a u16 it still tiles on most meshes and produces counts
of 65,282 on the rest -- the same trap the batch count at mesh+0x66 already carries a note about.
"""
import struct, re
NAME = re.compile(rb'[ -~]{1,31}\0')
u8  = lambda d,o: d[o]
u16 = lambda d,o: struct.unpack_from('<H',d,o)[0]
u32 = lambda d,o: struct.unpack_from('<I',d,o)[0]
f32 = lambda d,o: struct.unpack_from('<f',d,o)[0]

def mat(d, o):
    """The node's 4x4, flat. Row-vector convention: translation is words 12..14."""
    return list(struct.unpack_from('<16f', d, o + 0x10))

def mul(a, b):
    """a then b, row-vector convention."""
    return [sum(a[r*4+k]*b[k*4+c] for k in range(4)) for r in range(4) for c in range(4)]

def xform(m, v):
    x,y,z = v
    return (m[0]*x+m[4]*y+m[8]*z+m[12], m[1]*x+m[5]*y+m[9]*z+m[13], m[2]*x+m[6]*y+m[10]*z+m[14])

def nodes(d):
    """Every node in the scene graph: meshes first, then helpers, which is the order the
    animation's node indices use (`node < meshCount ? mesh[node] : helper[node - meshCount]`)."""
    nmesh, mt, ht = u16(d,0x30), u32(d,0x48), u32(d,0x4C)
    out = [mt + i*160 for i in range(nmesh)]
    o = ht
    while o + 0x60 <= len(d) and (u32(d,o) & 0x80000000):
        out.append(o); o += 0x60
    return out

def name(d, o): return NAME.match(d, u32(d, o+0x54)).group()[:-1].decode()

def world(d, locals_=None):
    """Each node's world matrix, composing parents. `locals_` overrides a node's own matrix."""
    offs = nodes(d)
    loc = {o: (locals_ or {}).get(o, mat(d, o)) for o in offs}
    par = {o: u32(d, o+4) for o in offs}
    W = {}
    def go(o, depth):
        if o in W: return W[o]
        p = par[o]
        W[o] = loc[o] if (p == 0 or p not in loc or depth > 32) else mul(loc[o], go(p, depth+1))
        return W[o]
    for o in offs: go(o, 0)
    return W

def skin(d, mesh_off):
    """The mesh's skin, as a list of A vertices, each [(bone, weight, (x,y,z)), ...]."""
    sk = u32(d, mesh_off + 0x90)
    if not sk: return None
    A, B = u16(d,sk), u16(d,sk+2)
    pg, pb, pw, pp = (u32(d, sk+4+4*k) for k in range(4))
    out = []
    for i in range(A):
        first, cnt = u16(d, pg+i*4), u8(d, pg+i*4+2)
        out.append([(u8(d, pb+first+j), f32(d, pw+(first+j)*4),
                     struct.unpack_from('<3f', d, pp+(first+j)*12))
                    for j in range(cnt)])
    return out

def anim_vertex_map(d, mesh_off):
    """Strip slot -> animated-vertex index, from mesh+0x98. One RUN of entries is one vertex."""
    lst, nv = u32(d, mesh_off+0x98), u16(d, mesh_off+0x60)
    if not lst: return None
    ent = [u16(d, lst + k*2) for k in range(nv)]
    addrs = sorted({e & 0xfffc for e in ent})
    if len(addrs) != nv: return None
    rank = {a:i for i,a in enumerate(addrs)}
    idx = [-1]*nv; g = 0
    for e in ent:
        idx[rank[e & 0xfffc]] = g
        if not (e & 2): g += 1
    return None if any(v < 0 for v in idx) else idx

def bind_positions(d, mesh_off):
    """Skin the bind pose: sum of weight * (bone-space position through that bone's world matrix)."""
    W = world(d); offs = nodes(d)
    out = []
    for infl in skin(d, mesh_off):
        x=y=z=0.0
        for bone, w, p in infl:
            if bone >= len(offs): return None
            q = xform(W[offs[bone]], p)
            x += w*q[0]; y += w*q[1]; z += w*q[2]
        out.append((x,y,z))
    return out
