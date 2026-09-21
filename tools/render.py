import sys, struct, re, math
sys.path.insert(0, "/tmp/claude-1000/-home-cowtools-claude-discord-catboy/8969d9f6-1995-40aa-be55-408a25ebc462/scratchpad")
from wadtree import walk, read
from m3d2 import meshes, xform
NAME = re.compile(rb'[ -~]{1,63}\0')

def batches(m, me):
    p, out = me['batchTable'], []
    while p + 16 <= me['batchTableEnd']:
        a, b, c, n = struct.unpack_from('<4I', m, p)
        if not (0 < a < b < c <= len(m)) or n == 0 or n > 4096: break
        pos = [struct.unpack_from('<3f', m, a + k*12) for k in range(n)]
        uv  = [struct.unpack_from('<2h', m, b + k*4)  for k in range(n)]
        out.append((pos, [(u/4096.0, v/4096.0) for u, v in uv]))
        p += 16
    return out

def textures(m):
    ntex = struct.unpack_from('<H', m, 0x22)[0]
    mat  = struct.unpack_from('<I', m, 0x40)[0]
    names = []
    for i in range(ntex):
        no = struct.unpack_from('<I', m, mat + i*16 + 12)[0]
        names.append(NAME.match(m, no).group()[:-1].decode() if 0 < no < len(m) else None)
    return names

def load_tga(buf):
    """Targa -> (w, h, rows of (r,g,b)), top row first.

    ⚠ 32-BIT IS NOT OPTIONAL HERE. 261 of JUNGLE.WAD's 984 TGAs are 32-bit BGRA, and a loader that
    tests `bpp != 24` returns None for every one of them -- silently, so the model renders untextured
    and it looks like a missing file or missing geometry rather than a rejected format. That is
    exactly what it did look like, for hours."""
    idlen, cmap, kind = buf[0], buf[1], buf[2]
    w, h = struct.unpack_from('<HH', buf, 12)
    bpp, desc = buf[16], buf[17]
    px = buf[18 + idlen:]
    if kind != 2 or bpp not in (24, 32): return None
    n = bpp // 8
    img = [[(0, 0, 0)] * w for _ in range(h)]
    for y in range(h):
        row = h - 1 - y if not (desc & 0x20) else y
        for x in range(w):
            o = (y * w + x) * n
            img[row][x] = (px[o+2], px[o+1], px[o])
    return w, h, img
