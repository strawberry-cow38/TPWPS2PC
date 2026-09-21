"""FKNL archive reader. Layout deduced 2026-09-21:

  header : 'FKNL', u32 0, u32 dataStart, u32 hash, u32 stringTable, u32 rootBlock
  block  : u32 fileTable, u32 dirTable, u32 fileCount, u32 dirCount
  file   : u32 nameOff (absolute), u32 dataOff, u32 storedSize, u32 decompressedSize   [16 bytes]
  dir    : u32 nameOff, u32 blockOff                                                   [8 bytes]

Names are NUL-terminated at an absolute offset. Payloads are EA RefPack.
"""
import struct, sys

def _u32(d, o): return struct.unpack_from('<I', d, o)[0]
def _name(d, o): return d[o:d.index(b'\0', o)].decode('latin-1')

def walk(d, block=None, path='', out=None, depth=0):
    if out is None: out = []
    if block is None:
        if d[:4] != b'FKNL': raise ValueError('not FKNL: %r' % d[:4])
        block = _u32(d, 0x14)
    ftab, dtab, nfiles, ndirs = struct.unpack_from('<4I', d, block)
    for i in range(nfiles):
        no, off, stored, decomp = struct.unpack_from('<4I', d, ftab + i*16)
        out.append((path + '/' + _name(d, no), off, stored, decomp))
    for i in range(ndirs):
        no, sub = struct.unpack_from('<2I', d, dtab + i*8)
        nm = path + '/' + _name(d, no)
        out.append((nm + '/', sub, 0, 0))
        if depth < 16: walk(d, sub, nm, out, depth + 1)
    return out

if __name__ == '__main__':
    d = open(sys.argv[1], 'rb').read()
    for p, off, a, b in walk(d):
        print(f"{a:>9} {b:>9}  {p}" if not p.endswith('/') else f"{'':>9} {'':>9}  {p}")


def read(d, off, stored, decompressed):
    """One entry's bytes. ⚠ AN ENTRY IS ONLY REFPACKED IF IT SHRANK: when RefPack could not beat
    the original the archive stores it raw, and those entries have stored == decompressed and no
    10 FB magic. 83 of JUNGLE.WAD's 2,545 files are like that -- mostly small 64x64 TGAs. Treating
    a failed decompress as a broken file would lose exactly the files that compress worst."""
    if stored == decompressed:
        return d[off:off + stored]
    from refpack import decompress
    out, size, _ = decompress(d, off)
    if len(out) != size:
        raise ValueError(f"refpack produced {len(out)}, header declares {size}")
    return out
