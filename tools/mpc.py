"""EA MPC movie container reader (PS2 Theme Park World).

Layout: a flat stream of EA chunks, `4cc + u32 LE size` where size INCLUDES the 8-byte header.
  SCHl  audio header  (platform 'PT\0\0', then a tag/len/value element stream)
  SCCl  audio chunk count
  SCDl  audio data
  MPCh  ONE VIDEO FRAME of MPEG-2 video elementary stream
  SCEl  end of stream
"""
import struct
SEC, OFF, USR = 2352, 24, 2048

def disc_file(path, extent, size):
    out, left, s = bytearray(), size, extent
    with open(path,'rb') as f:
        while left > 0:
            f.seek(s*SEC+OFF); out += f.read(min(USR,left)); left -= USR; s += 1
    return bytes(out)

def chunks(d):
    p = 0
    while p + 8 <= len(d):
        tag = d[p:p+4]; sz = struct.unpack_from('<I', d, p+4)[0]
        if sz < 8 or p + sz > len(d): raise ValueError('bad chunk at %d: %r %d' % (p, tag, sz))
        yield tag, p + 8, sz - 8
        p += sz
    if p != len(d): raise ValueError('trailing %d bytes' % (len(d)-p))

def _arb(d, p):
    """EA's length-prefixed big-endian integer: one length byte, then that many bytes."""
    n = d[p]; return int.from_bytes(d[p+1:p+1+n], 'big'), p + 1 + n

def audio_header(d, off, size):
    """The SCHl element stream. 0xFD opens a subheader, 0x8A closes it, 0xFF ends the header."""
    assert d[off:off+4] == b'PT\0\0', d[off:off+4]
    p, end, out = off + 4, off + size, {}
    NAMES = {0x80:'revision', 0x82:'channels', 0x83:'codec', 0x84:'sample_rate', 0x85:'num_samples'}
    while p < end:
        b = d[p]; p += 1
        if b == 0xFF: break
        if b != 0xFD: continue                     # top-level element, not needed here
        while p < end:                             # subheader
            s = d[p]; p += 1
            if s == 0x8A: _arb(d, p); break
            if s in NAMES: out[NAMES[s]], p = _arb(d, p)
            else: _, p = _arb(d, p)
        break
    return out
