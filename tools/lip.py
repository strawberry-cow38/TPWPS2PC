"""`.LIP` -- the advisor lip-sync mark tracks. Solved 2026-09-21. Marks are MICROSECONDS.

    u32 mark, u32 mark, ... , 0xFFFFFFFF

516 files, all of them in `LIPS.WAD`, under `English/`, `French/` and `German/` -- **172 each,
three languages, not the nine the text database ships.** 243 distinct stems; 128 appear in all
three. Every file: length a multiple of 4, terminated by `FFFFFFFF`, marks strictly increasing.
516 of 516 on each of those.

⭐ **Every mark count is ODD** -- 1, 3, 5, 7 ... 37, no exceptions in 516 files. So they are not
open/close pairs. 83 files carry a single mark and one of those is the single value `0`.

They are per-clip times, not offsets into a shared bank: several files start at **0**, and 151 of
the 172 English files have value ranges that overlap another file's.

⭐ **The unit is MICROSECONDS, measured.** The advisor banks are not in any WAD -- they are at ISO
level, `/AUDIO/ADVISOR/{ENGLISH,FRENCH,GERMAN}/SPCHHD.SDT`, exactly the three languages with tracks.
170 of each language's 172 stems match a sound name in its own bank. Dividing each last mark by its
clip's duration over 509 pairs gives a median ratio of **0.9759** with **469 of 509 inside the
clip**; read as 100ns, milliseconds or 44.1kHz samples, **zero** pairs land in 0.5..1.0. See
findings/lip.md, including why the 37 English overruns are a distribution shift and not bad joins.

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
