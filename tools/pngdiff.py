"""Count the pixels that differ between two PNGs, with no imaging library.

    python3 tools/pngdiff.py a.png b.png [x0 y0 x1 y1]

Turns "looks better" into a number: how many pixels changed, and the share of the frame (or of
the given rectangle). Handles 8-bit RGB/RGBA non-interlaced PNGs, which is what Godot writes.
"""
import struct, sys, zlib


def read_png(path):
    d = open(path, 'rb').read()
    assert d[:8] == b'\x89PNG\r\n\x1a\n', path
    pos, idat, w, h, ct = 8, [], 0, 0, 0
    while pos < len(d):
        n, = struct.unpack_from('>I', d, pos); tag = d[pos+4:pos+8]; body = d[pos+8:pos+8+n]
        if tag == b'IHDR':
            w, h, depth, ct, _, _, il = struct.unpack('>IIBBBBB', body)
            assert depth == 8 and il == 0 and ct in (2, 6), (depth, ct, il)
        elif tag == b'IDAT': idat.append(body)
        pos += 12 + n
    bpp = 4 if ct == 6 else 3
    raw = zlib.decompress(b''.join(idat))
    stride = w * bpp
    out, prev = [], bytearray(stride)
    p = 0
    for y in range(h):
        f = raw[p]; line = bytearray(raw[p+1:p+1+stride]); p += 1 + stride
        if f == 1:
            for i in range(bpp, stride): line[i] = (line[i] + line[i-bpp]) & 255
        elif f == 2:
            for i in range(stride): line[i] = (line[i] + prev[i]) & 255
        elif f == 3:
            for i in range(stride): line[i] = (line[i] + ((line[i-bpp] if i >= bpp else 0) + prev[i]) // 2) & 255
        elif f == 4:
            for i in range(stride):
                a = line[i-bpp] if i >= bpp else 0; b = prev[i]; c = prev[i-bpp] if i >= bpp else 0
                pa, pb, pc = abs(b - c), abs(a - c), abs(a + b - 2*c)
                line[i] = (line[i] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        out.append(bytes(line)); prev = line
    return w, h, bpp, out


def main():
    wa, ha, ba, a = read_png(sys.argv[1]); wb, hb, bb, b = read_png(sys.argv[2])
    assert (wa, ha) == (wb, hb), 'sizes differ'
    x0, y0, x1, y1 = (int(v) for v in sys.argv[3:7]) if len(sys.argv) >= 7 else (0, 0, wa, ha)
    diff = 0
    for y in range(y0, y1):
        ra, rb = a[y], b[y]
        for x in range(x0, x1):
            if ra[x*ba:x*ba+3] != rb[x*bb:x*bb+3]: diff += 1
    n = (x1 - x0) * (y1 - y0)
    print(f'{diff} of {n} pixels differ ({100*diff/n:.1f}%) in [{x0},{y0})-[{x1},{y1})')


if __name__ == '__main__':
    main()
