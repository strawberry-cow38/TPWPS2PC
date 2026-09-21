"""`.plb` reader — Theme Park World (PS2) particle effect library.

`/DATA/PARTICLE.WAD` holds `Tp2.plb` plus 100 particle textures in animated sequences
(`PA1a0000..0015` is a 16-frame loop, `Pa1b0000..0007` an 8-frame one).

`FUN_001f6938` loads it: `FUN_00220800(0x2f0790, "Data\\Particle\\Tp2.plb", 400, 0x400)`.

Layout, confirmed by the name spacing matching the header's own record size:

    +0x00 u32 record count        (105 in Tp2.plb)
    +0x04 u32 record size         (320)
    records start at 0x120; each begins with a NUL-terminated name

    0x120 + 105*320 = 0x8460, and the file is 35,704 bytes.

⚠ The record's parameter fields (from about +0x20) are integers, not floats, and at least some are
**16.16 fixed point** -- `65536` appears as a value and `0xffffffff` as a sentinel. Their meanings
are NOT decoded; that needs the particle system's own parser, which is not located.
"""
import struct


def effects(d):
    """[(index, name)] for every record in the library."""
    count, size = struct.unpack_from('<2I', d, 0)
    out = []
    for i in range(count):
        o = 0x120 + i*size
        if o + size > len(d): break
        out.append((i, d[o:o+32].split(b'\0')[0].decode('latin-1')))
    return out


def record(d, i):
    """One raw record."""
    count, size = struct.unpack_from('<2I', d, 0)
    o = 0x120 + i*size
    return d[o:o+size]


if __name__ == '__main__':
    import sys
    d = open(sys.argv[1], 'rb').read()
    count, size = struct.unpack_from('<2I', d, 0)
    e = effects(d)
    print('%s: %d records of %d bytes, %d named' % (sys.argv[1], count, size,
                                                    sum(1 for _, n in e if n)))
    for i, n in e:
        if n: print('  %3d  %s' % (i, n))
