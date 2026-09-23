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
    L2, 20 bytes:  +0x00 u16 EVENT ID    +0x04 u32 childCount   +0x08 ptr->L3   +0x10 u16 flags
    L3, 42 bytes:  +0x00 u16 n16          +0x04 u32 n8    +0x08 ptr->16B   +0x26 ptr->8B
                   16-byte entry: +0x00 u32 sound index (ONE-BASED into the bank)
                                  +0x04 u16 cumulative pick threshold
                                  +0x08 u32 milliseconds
                                  +0x0C u16 BANK, ONE-BASED into the sibling *BANK.MAP's list
                   8-byte entry:  first u32 is a ONE-BASED index into this L2's own L3 array
                                  (`*p = base + (*p-1)*0x2a`), so the L3 records reference each other

⚠⚠ TWO CORRECTIONS, both of which this docstring had wrong and which produced plausible output:

  1. `+0x0C` IS THE BANK, NOT THE SOUND ID. The one-based sound index is at `+0x00`; `+0x0C`
     selects which bank of the sibling `*BANK.MAP` to take it from (a ride event can legitimately
     play from the ambient bank). Reading `+0x0C` as the sound id returns small in-range integers
     that resolve to real clips -- the wrong ones.
  2. THE EVENT ID IS THE L2 RECORD'S LEADING u16, and it is what a ride script's third operand is
     matched against (`EVENT <OBJ_SOUND_group> <node> <EVT_id>`). This layout note previously had
     no id field at all, which is why the script-to-clip join looked impossible.

Controls that fix both, predicted BEFORE lookup: Crazy Ape's `EVENT 3 -1 8` (EVT_RIDE_APE) ->
JUNGLE/PARK1 RIDEHD.SDT[1] = `apeoooooC.vag`; the haunted toilet's `EVT_BOG3`, commented `; PISS`
in its own .rss source, -> KIDSHD.SDT[11] = `wee1.vag`; `BOG1 ; STRAIN` -> `strain2.vag`;
`BOG2 ; CRAP` -> `FART1.vag`. See core/TPW.PS2.Data/SoundIndex.cs and findings/rse-vm.md.
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

def bank_paths(d):
    """⭐⭐ A `*BANK.MAP` holds a LIST of banks, and a clip entry's `+0x0C` is a ONE-BASED index
    into it. Header, then `count` 11-byte records the loader (`0x249758`) keeps verbatim, then
    `count` length-prefixed names. A name is the folder the bank was built from -- `Sound1\\Ride`,
    `sound\\Coast` -- and its LAST COMPONENT names the file beside the map: `RIDEHD.SDT`,
    `COASTHD.SDT`.

    ⚠⚠ THIS REPLACES `bank_path`, WHICH RETURNED ONE NAME BY SCANNING FOR THE FIRST PLAUSIBLE
    LENGTH-PREFIXED STRING. That is right for 36 of the 42 maps and silently drops the second and
    third banks of the six that have them -- so a ride event that legitimately plays from the
    ambient bank resolved to a clip of the same index in the wrong bank. A confident wrong name,
    which is the worst kind. The walk below is bounded and asserts it consumed the whole file, so
    it fails loudly instead.
    """
    if d[:16] != GUID_BANK: raise ValueError('not a BANK.MAP: %s' % d[:16].hex())
    count = u32(d, 0x18)
    q, names = 0x1C + count * 11, []
    for _ in range(count):
        ln = u32(d, q)
        if q + 4 + ln > len(d): raise ValueError('BANK.MAP name runs off the file')
        names.append(d[q+4:q+4+ln].rstrip(b'\0').decode('latin-1'))
        q += 4 + ln
    if q != len(d): raise ValueError('BANK.MAP walk consumed %d of %d bytes' % (q, len(d)))
    return names


def bank_path(d):
    """⚠ DEPRECATED, kept so existing callers keep working: the FIRST bank only. Use
    `bank_paths(d)[entryBank - 1]` -- the index is one-based."""
    names = bank_paths(d)
    return names[0] if names else None
