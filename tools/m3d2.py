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
        yield dict(name=NAME.match(m, noff).group()[:-1].decode(), tex=tex, mat=mat,
                   nverts=nverts, nfaces=nfaces, batchTable=bt, batchTableEnd=bt+btlen)

def strips(m, mesh):
    """Each batch's positions, as one triangle strip."""
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
