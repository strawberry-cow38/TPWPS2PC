"""`.plb` reader — Theme Park World (PS2) particle effect library.

`/DATA/PARTICLE.WAD` holds `Tp2.plb` and particle textures. Numbered files are NOT
automatically loops: 0x189fdc selects a logical sprite by particle lifetime, and
0x182680 halves that index into an executable-authored table of even-numbered SSHs.

`FUN_001f6938` loads it: `FUN_00220800(0x2f0790, "Data\\Particle\\Tp2.plb", 400, 0x400)`.

Layout, confirmed by loader 0x1467b8 and cursor reader 0x1461c0:

    +0x00 u32 record count        (105 in Tp2.plb)
    +0x04 u32 record size         (320)
    records start at 0x08; the NUL-terminated name is at record +0x118
    +0x94 i16 sprite group, +0x96 i16 logical sprite count

    0x08 + 105*320 = 0x8348: next header is (20 records, 104 bytes).
    0x8350 + 20*104 = 0x8b70: two trailing u32s; file length 35,704.

The former 0x120 record origin was the first NAME, not the first record. It mixed one
effect's name with the next effect's parameters. The two sprite fields are consumed
by 0x14630c/0x146314, 0x14656c and 0x189fdc; other parameters remain undecoded here.
See findings/animated-textures.md for the structure-to-consumer chain.
"""
import struct


def effects(d):
    """[(index, name)] for every record in the library."""
    count, size = struct.unpack_from('<2I', d, 0)
    out = []
    for i in range(count):
        o = 8 + i*size
        if o + size > len(d): break
        out.append((i, d[o+0x118:o+size].split(b'\0')[0].decode('latin-1')))
    return out


def record(d, i):
    """One raw record."""
    count, size = struct.unpack_from('<2I', d, 0)
    if not 0 <= i < count: raise IndexError(i)
    o = 8 + i*size
    return d[o:o+size]


def sprite(d, i):
    """(group, logical frame count), consumed by 0x146290 and 0x189e78."""
    return struct.unpack_from('<hh', record(d, i), 0x94)


if __name__ == '__main__':
    import sys
    d = open(sys.argv[1], 'rb').read()
    count, size = struct.unpack_from('<2I', d, 0)
    e = effects(d)
    print('%s: %d records of %d bytes, %d named' % (sys.argv[1], count, size,
                                                    sum(1 for _, n in e if n)))
    for i, n in e:
        if n: print('  %3d  %s' % (i, n))
