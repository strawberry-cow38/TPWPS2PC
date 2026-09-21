"""`.LIP` -- the advisor lip-sync mark tracks. Structure solved 2026-09-21; the UNIT is not.

    u32 mark, u32 mark, ... , 0xFFFFFFFF

516 files, all of them in `LIPS.WAD`, under `English/`, `French/` and `German/` -- **172 each,
three languages, not the nine the text database ships.** 243 distinct stems; 128 appear in all
three. Every file: length a multiple of 4, terminated by `FFFFFFFF`, marks strictly increasing.
516 of 516 on each of those.

⭐ **Every mark count is ODD** -- 1, 3, 5, 7 ... 37, no exceptions in 516 files. So they are not
open/close pairs. 83 files carry a single mark and one of those is the single value `0`.

They are per-clip times, not offsets into a shared bank: several files start at **0**, and 151 of
the 172 English files have value ranges that overlap another file's.

⚠ **The unit is not established.** What can be said is what the readings imply:

| read as | clip length: min / median / max |
|---|---|
| microseconds | 2.5 s / 6.3 s / 46.4 s |
| 100 ns ticks | 0.25 s / 0.63 s / 4.6 s |
| 44.1 kHz samples | 57 s / 142 s / 1052 s |

The sample-rate readings are out by three orders of magnitude -- no advisor line is 17 minutes.
Microseconds gives spoken-line lengths and a median gap between marks of 0.78 s, which is a
mouth-movement cadence. 100 ns is not impossible but implies **no clip shorter than a quarter of a
second**, which no spoken line is. That is an argument from plausibility, not a measurement, and it
is written here as such.

**What would settle it:** the matching audio, which is NOT on the disc under these names -- of 172
stems, zero have a non-`.lip` file of the same stem anywhere in any WAD. The clips live in a sound
bank indexed some other way. Find the bank entry for one stem, read its duration, and the unit
falls out of one division.
"""
import struct

def marks(data):
    """The mark list, without the terminator. Raises if the file is not shaped as documented."""
    if len(data) % 4 or len(data) < 8: raise ValueError('not a whole number of u32 words')
    v = [struct.unpack_from('<I', data, o)[0] for o in range(0, len(data), 4)]
    if v[-1] != 0xFFFFFFFF: raise ValueError('missing FFFFFFFF terminator')
    v = v[:-1]
    if any(v[i] >= v[i + 1] for i in range(len(v) - 1)): raise ValueError('marks are not increasing')
    return v

if __name__ == '__main__':
    import sys
    for p in sys.argv[1:]:
        v = marks(open(p, 'rb').read())
        print('%-24s %2d marks%s  %s' % (p.split('/')[-1], len(v),
              '' if len(v) % 2 else '  <== EVEN, unexpected', v[:12]))
