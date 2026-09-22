"""`Text/translations/<locale>/<lang>.dat` -- the localisation string tables. Solved 2026-09-21.

    u32 count
    count x u32 absolute byte offset
    NUL-terminated strings

The header is exactly `4 + count*4` bytes and the first offset equals it, the offsets are
monotonic, and the last string ends exactly at EOF -- on all nine tables, which is the check.

⭐ **`id.dat` is not a language.** It is the SYMBOLIC KEY table, in the same format, the same count
and the same order as every language: `STR_LISTBOX_BUILD_TRACK`, `STR_SINGLESHOP_SALT`, ... So a
key resolves to a row index, and the row index reads across every locale.

⭐⭐ And `include/trans.h` ships the same list a THIRD time as a C `enum`, where the ordinal is the
index. Splitting each `id.dat` entry at its first space gives **1,087 of 1,087 identical to the
enum at the same position**. A shuffled control scores 1 of 1,087, so the ordering is real.

The part after that space is the entry's **printf format spec**: 21 of 1,087 carry one (`%d` x17,
`%i` x2, `%i%i%i%i`, `%d%d%d%d%d%d`), and they are exactly the strings whose text contains
substitutions -- `STR_SGRACE_BET` is `'Bet $%i on Racer %i\\n Odds %i-%i'`. So the table tells you
which strings take arguments and of what type, without parsing the text.

Locales: ame dut eng ger id ita jap spa swe, 1,087 entries each. `final.dat` / `finalame.dat` are
the plain-text masters in `[STR_KEY]` form, not this binary layout -- do not feed them to `read`.

⭐ Keys encode the asset path, which is the bridge from an asset to its display name in any
language: `STR_GRAPHICS_JUNGLE_RIDES_MONKEY_MONKEY` is `/JUNGLE/Rides/Monkey/Monkey`, whose
`Monkey.sam` carries `Info.Name "Crazy Ape"` -- and `eng.dat[1047]` is `Crazy Ape`.
"""
import struct

def read(data):
    """[(index, string)] for one table. Validates the header arithmetic rather than trusting it."""
    count = struct.unpack_from('<I', data, 0)[0]
    if count == 0 or 4 + count * 4 > len(data):
        raise ValueError('implausible count %d for %d bytes' % (count, len(data)))
    offsets = [struct.unpack_from('<I', data, 4 + i * 4)[0] for i in range(count)]
    if offsets[0] != 4 + count * 4:
        raise ValueError('first offset %d is not the header size %d' % (offsets[0], 4 + count * 4))
    out = []
    for i, o in enumerate(offsets):
        if o >= len(data): raise ValueError('offset %d past end' % o)
        out.append((i, data[o:data.index(b'\0', o)].decode('latin-1')))
    return out

def keys(id_dat):
    """[(index, key, formatSpec)] from `id.dat`; the spec is '' when the string takes no arguments."""
    out = []
    for i, s in read(id_dat):
        k, _, f = s.partition(' ')
        out.append((i, k, f))
    return out

if __name__ == '__main__':
    import sys
    rows = read(open(sys.argv[1], 'rb').read())
    if len(sys.argv) > 2:
        for (i, k, f), (_, s) in zip(keys(open(sys.argv[2], 'rb').read()), rows):
            print('%4d  %-40s %-12s %r' % (i, k, f, s))
    else:
        for i, s in rows: print('%4d  %r' % (i, s))
