"""`.SDT` -- the PS2 Theme Park World sound bank. Two variants, told apart by the u16 at +0x02.

VARIANT A (version 0) -- banks of named sounds:
    u16 count, u16 0
    u32 offset[count]                        absolute; offset[0] == 4 + 4*count
    at each offset, a 40-byte header:
       +0x00 u32 headerSize (always 40)      the payload starts at offset + headerSize
       +0x04 u32 dataSize                    headerSize + dataSize == the next offset
       +0x08 char[16] name                   "Crunch.mp2", "smTree1.vag"
       +0x18 u32 tag                          TOP BYTE IS THE CODEC: 0x24 mp2, 0x80 vag, 0x00 empty
       +0x1C u32 0
       +0x20 u32 ?                            large, rises with length
       +0x24 u32 0

VARIANT B (version 12345 = 0x3039) -- the same headers, moved out of the data:
    u16 count, u16 12345
    u32 offset[count]                        absolute, into the data
    40-byte header[count]                    the SAME layout as above
    the audio, from offset[0] == 4 + 4*count + 40*count

The payloads are MPEG-2 Layer II (the names say `.mp2` and the bitstream agrees) and Sony
PS-ADPCM (`.vag`, raw, no RIFF header, opening on the customary silent 16-byte block).
"""
import struct

VERSION_STREAMS = 12345

def variant(d): return struct.unpack_from('<H', d, 2)[0]

def offsets(d):
    n, ver = struct.unpack_from('<2H', d, 0)
    return [struct.unpack_from('<I', d, 4 + i*4)[0] for i in range(n)], ver

def sounds(d):
    """(name, codec, dataStart, dataEnd). Variant B has no names, so they come back empty."""
    offs, ver = offsets(d)
    # Variant B keeps the headers in their own table after the offsets; variant A puts each one
    # immediately before its own data. Same 40 bytes either way.
    htab = 4 + 4*len(offs) if ver == VERSION_STREAMS else None
    for i, (o, e) in enumerate(zip(offs, offs[1:] + [len(d)])):
        h = htab + i*40 if htab is not None else o
        hs, ds = struct.unpack_from('<2I', d, h)
        nm = d[h+8:h+24].split(b'\0')[0].decode('latin-1', 'replace')
        tag = d[h + 0x1B]
        a = o if htab is not None else o + hs
        yield nm, {0x24: 'mp2', 0x25: 'mp2', 0x80: 'vag', 0x00: 'empty'}.get(tag, 'tag%02x' % tag), a, a + ds, tag

MPEG1 = {1:32,2:48,3:56,4:64,5:80,6:96,7:112,8:128,9:160,10:192,11:224,12:256,13:320,14:384}
MPEG2 = {1:8,2:16,3:24,4:32,5:40,6:48,7:56,8:64,9:80,10:96,11:112,12:128,13:144,14:160}
SR    = {0:44100, 1:48000, 2:32000}

def frame_header(d, o):
    if o + 4 > len(d) or d[o] != 0xFF or (d[o+1] & 0xE0) != 0xE0: return None
    ver, layer = (d[o+1] >> 3) & 3, (d[o+1] >> 1) & 3
    bri, sri = (d[o+2] >> 4) & 15, (d[o+2] >> 2) & 3
    if ver == 1 or layer == 0 or bri in (0, 15) or sri == 3: return None
    rate = SR[sri] // (1 if ver == 3 else (2 if ver == 2 else 4))
    kbps = (MPEG1 if ver == 3 else MPEG2)[bri]
    pad = (d[o+2] >> 1) & 1
    size = (12000*kbps//rate + pad)*4 if layer == 3 else 144000*kbps//rate + pad
    return dict(ver={3:'MPEG-1',2:'MPEG-2',0:'MPEG-2.5'}[ver], layer={3:'I',2:'II',1:'III'}[layer],
                kbps=kbps, rate=rate, mono=((d[o+3] >> 6) & 3) == 3, size=size,
                samples=384 if layer == 3 else (1152 if ver == 3 else 576 if layer == 1 else 1152))
