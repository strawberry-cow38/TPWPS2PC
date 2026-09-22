"""Independent struct-based golden projection, developed alongside the ELF investigation.

Usage: python3 tools/TPW.PS2.DbaAudit/reference.py arsdb.dba id.dat [--list]
Reads extracted inputs; never overwrites goldens. Canonical columns: findings/dba.md.
This is a regression reference, not an independent proof of field semantics.
"""
import hashlib
import pathlib
import struct
import sys


def project(data, ids):
    def word(blob, p):
        return struct.unpack_from('<I', blob, p)[0]

    names = [ids[p:ids.index(0, p)].decode('latin1').strip()
             for p in struct.unpack_from('<%dI' % word(ids, 0), ids, 4)]
    lines = []
    for key, offset, size in struct.iter_unpack('<III', data[4:4 + 12 * word(data, 0)]):
        p = data[offset:offset + size]
        kind, index, row, w, h, d, b, ax, az, bx, bz, ad, bd, mini, ex, length = struct.unpack_from(
            '<HHIBBBBhhhhBBHiI', p)
        common = [kind, index, row, w, h, d, b, ax, az, ad, bx, bz, bd, mini, ex, length]
        ride = kind in (1, 3, 6, 7)
        if ride:
            body = []
            for tier in range(3):
                body.extend(struct.unpack_from('<I12i', p, 32 + 52 * tier))
        else:
            body = list(struct.unpack_from('<3i', p, 32))
            if kind == 4:
                body.extend(struct.unpack_from('<HH8B', p, 44))
            elif kind == 5:
                body.extend(struct.unpack_from('<HHB', p, 44))
            elif kind == 2:
                body.append(p[46])
        cells = ['%d:%d:%d:%d:%d:%d' % (t, f, f & 3, bool(f & 4), bool(f & 8), t < 0)
                 for t, f in struct.iter_unpack('<hH', p[32 + length:])]
        lines.append('\t'.join([str(key), str(offset), names[row], ','.join(map(str, common)),
                                ','.join(map(str, body)), p[188 if ride else 44:32 + length].hex().upper(),
                                ','.join(cells)]))
    return lines


if __name__ == '__main__':
    if len(sys.argv) not in (3, 4) or (len(sys.argv) == 4 and sys.argv[3] != '--list'):
        sys.exit('usage: reference.py DBA id.dat [--list]')
    lines = project(pathlib.Path(sys.argv[1]).read_bytes(), pathlib.Path(sys.argv[2]).read_bytes())
    if '--list' in sys.argv:
        print('\n'.join(lines))
    else:
        print(hashlib.sha256(('\n'.join(lines) + '\n').encode('ascii')).hexdigest())
