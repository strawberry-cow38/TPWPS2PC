"""`*SFX.MAP` -- the sound INDEX beside each `.SDT` bank. Layout taken from the game's own loader
(`FUN_00249d38` -> `FUN_0024b770` -> `FUN_0024b810` -> `FUN_0024b8d0` -> `FUN_0024a030`), not
inferred from the bytes -- the records are 24, 20 and 42 bytes, so nothing here is 4-byte aligned
and reading it as a u32 array produces convincing nonsense.

    0x00  16-byte type GUID   00 2c 61 e9 d0 31 d2 11 b4 09 00 b0 c9 93 f2 03   (SFX.MAP)
                              01 2c 61 e9 d0 31 d2 11 b4 09 00 a0 c9 93 f2 03   (BANK.MAP)
          -- the loader compares all four words of it and returns -1 on a mismatch
    0x10  u32 ?               (the first read is 20 bytes, so this is part of the header)
    0x14  u32 ?
    0x18  u32 count           of level-1 records
    0x1C  the tree, DEPTH FIRST: each level's whole array, then each element's children in turn

    L1, 24 bytes:  +0x00 u32 childCount   +0x04 ptr->L2   +0x08 u16 flags (bit 2 cleared on load)
    L2, 20 bytes:  +0x04 u32 childCount   +0x08 ptr->L3   +0x10 u16 flags
    L3, 42 bytes:  +0x00 u16 n16          +0x04 u32 n8    +0x08 ptr->16B   +0x26 ptr->8B
                   16-byte entry: +0x0C u16 sound id, ONE-BASED, resolved through the bank
                   8-byte entry:  first u32 is a ONE-BASED index into this L2's own L3 array
                                  (`*p = base + (*p-1)*0x2a`), so the L3 records reference each other
"""
import struct
u16 = lambda d,o: struct.unpack_from('<H',d,o)[0]
u32 = lambda d,o: struct.unpack_from('<I',d,o)[0]

GUID_SFX  = bytes.fromhex('002c61e9d031d211b40900b0c993f203')
GUID_BANK = bytes.fromhex('012c61e9d031d211b40900a0c993f203')

def parse(d):
    """Walk the tree and return (tree, bytesConsumed). The caller checks consumed == len(d)."""
    if d[:16] != GUID_SFX: raise ValueError('not a SFX.MAP: %s' % d[:16].hex())
    p = 0x1C
    n1 = u32(d, 0x18)
    l1 = [dict(off=p + i*24, nchild=u32(d, p + i*24), flags=u16(d, p + i*24 + 8)) for i in range(n1)]
    p += n1*24
    for a in l1:
        a['children'] = c2 = [dict(off=p + j*20, nchild=u32(d, p + j*20 + 4),
                                   flags=u16(d, p + j*20 + 0x10)) for j in range(a['nchild'])]
        p += a['nchild']*20
        for b in c2:
            b['children'] = c3 = [dict(off=p + k*42, n16=u16(d, p + k*42), n8=u32(d, p + k*42 + 4))
                                  for k in range(b['nchild'])]
            p += b['nchild']*42
            for c in c3:
                c['ids'] = [u16(d, p + m*16 + 0x0C) for m in range(c['n16'])]
                p += c['n16']*16
                c['links'] = [u32(d, p + m*8) for m in range(c['n8'])]
                p += c['n8']*8
    return l1, p

def bank_path(d):
    """`*BANK.MAP` is a header and one length-prefixed path: `sound\\Bumper`."""
    if d[:16] != GUID_BANK: raise ValueError('not a BANK.MAP')
    n = u32(d, len(d) - 4 - 0)  # the length sits just before the string
    for o in range(16, len(d)-4):
        ln = u32(d, o)
        if 0 < ln <= len(d)-o-4 and d[o+4+ln-1] == 0 and all(32 <= c < 127 for c in d[o+4:o+4+ln-1]):
            return d[o+4:o+4+ln-1].decode('latin-1')
    return None
