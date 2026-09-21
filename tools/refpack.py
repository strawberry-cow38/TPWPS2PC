"""EA RefPack (QFS) decompressor. Header: 0x10 0xFB, optional 3-byte BE compressed size
when byte0 & 0x01, then 3-byte BE decompressed size."""
def decompress(d, off=0):
    # returns (data, declared_size, end_offset)
    b0, b1 = d[off], d[off+1]
    if b1 != 0xFB: raise ValueError("not refpack: %02x %02x" % (b0, b1))
    p = off + 2
    if b0 & 0x01: p += 3                      # compressed size present
    size = int.from_bytes(d[p:p+3], 'big'); p += 3
    out = bytearray()
    while p < len(d):
        c = d[p]
        if c < 0x80:
            a = d[p+1]; p += 2
            proceed = c & 3
            run  = ((c & 0x1C) >> 2) + 3
            dist = ((c & 0x60) << 3) + a + 1
        elif c < 0xC0:
            a, b = d[p+1], d[p+2]; p += 3
            proceed = a >> 6
            run  = (c & 0x3F) + 4
            dist = ((a & 0x3F) << 8) + b + 1
        elif c < 0xE0:
            a, b, e = d[p+1], d[p+2], d[p+3]; p += 4
            proceed = c & 3
            run  = ((c & 0x0C) << 6) + e + 5
            dist = ((c & 0x10) << 12) + (a << 8) + b + 1
        elif c < 0xFC:
            p += 1
            proceed = ((c & 0x1F) << 2) + 4
            run = dist = 0
        else:
            p += 1
            proceed = c & 3
            run = dist = 0
            out += d[p:p+proceed]
            break
        out += d[p:p+proceed]; p += proceed
        for _ in range(run):
            out.append(out[-dist])
    return bytes(out), size, p
