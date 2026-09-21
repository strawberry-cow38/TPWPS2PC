"""`.ssh` -- EA's SHPS image container, as the PS2 build ships it.

Pixels are now decoded by core/TPW.PS2.Data/Ssh.cs using FFmpeg's existing IPU codec.
See findings/ssh.md and tools/TPW.PS2.SshScore for the scored results and limitations.
This Python module remains the lightweight container inspector.

    0x00  'SHPS'
    0x04  u32 fileSize          (matches the archive's declared size)
    0x08  u32 entryCount
    0x0C  char[4] platform      'GIMX' on every file here
    0x10  entryCount x { char[4] name, u32 offset }
          ...then padding to the first entry. EA left "Buy ERTS" in it.

    entry, 16-byte header at its offset:
    +0x00 u8  type              ⚠ BIT 0x80 IS A COMPRESSION FLAG; the real type is the low 7 bits
    +0x01 u24 blockSize         bytes to the NEXT block; 0 means "to the end of the file"
    +0x04 u16 width
    +0x06 u16 height
    +0x08 u16 centreX, u16 centreY
    +0x0C u16 posX,    u16 posY
    +0x10 the payload

The fixtures have 331 type 0x84 (RGB) and 69 type 0x85 (RGB + alpha) images, out of 400.
The GM payload is an IPU macroblock stream, not an ordinary MPEG elementary stream or RefPack.
The earlier 'every entry is 0x84' observation did not cover the alpha population.

⭐ When it IS attacked, the test set is already there: **5,493 of the disc's 6,007 material
references ship BOTH a `.ssh` and a `.tga` of the same stem**, so a candidate decoder can be scored
against thousands of known-correct images rather than eyeballed.

⚠ And the reason it matters: the other **514 references have NO `.tga`**, and **473 of those do have
a `.ssh`**. `LOBBY.WAD` alone is 448 of the 514. The claim in findings/formats.md that "a `.tga` of
the same stem sits beside each `.ssh`, so SHPS never needs decoding" is true for 92% of materials
and false for the rest -- it was checked on JUNGLE and generalised.
"""
import struct

def entries(d):
    """(name, type, compressed, width, height, payloadStart, payloadEnd)."""
    if d[:4] != b'SHPS': raise ValueError('not SHPS: %r' % d[:4])
    n = struct.unpack_from('<I', d, 8)[0]
    for i in range(n):
        o = 0x10 + i*8
        name = d[o:o+4].rstrip(b'\0').decode('latin-1')
        off = struct.unpack_from('<I', d, o+4)[0]
        typ = d[off]
        blk = int.from_bytes(d[off+1:off+4], 'little')
        w, h = struct.unpack_from('<2H', d, off+4)
        end = off + blk if blk else len(d)
        yield dict(name=name, type=typ & 0x7F, compressed=bool(typ & 0x80),
                   width=w, height=h, data=(off+0x10, end))
