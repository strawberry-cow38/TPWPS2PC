"""`.rse` -- RSSE, the compiled form of the `.rss` script sources that ship beside it. Solved
2026-09-21 against the 270 stems that carry BOTH on the disc, which is a known-answer corpus: the
opcode table below was not guessed, it was derived by aligning each source's mnemonics with each
binary's opcode words and requiring every file to agree.

    0x00  'RSSE'
    0x04  u32 0x00010F51      version; identical in all 270
    0x08  u32 variable count  matches the source's `variable` declarations in 269 of 270
    0x0C  u32 ?               zero in 211 of 270
    0x10  u32 50              identical in all 270
    0x14  u32 ?               zero in 250 of 270
    0x18  u32 ?               zero in 267 of 270
    0x1C  u32 ?               zero in 223 of 270
    0x20  "Pad Pad Pad Pad "  literal ASCII padding, all 270
    0x30  the code: a flat array of u32 words

⭐ **Each code word is TAGGED IN ITS TOP BYTE.** That is the whole format:

    0x80xxxxxx   opcode          (the table below)
    0x40xxxxxx   variable index  (into the script's own `variable` list, 0-based)
    0x20xxxxxx   code address    (a word index from 0x30; the assembler's `.labels`)
    0x10xxxxxx   symbol index    (into the trailing table; see below -- NOT fully pinned)
    0x00xxxxxx   immediate constant

⚠ **The code does NOT run to EOF.** After the last instruction sits a table of `u32 length` +
that many bytes of NUL-terminated ASCII, ending exactly at end of file: the script's NAME first,
then variable names. `Toilet.rse` is 780 bytes and its code stops at word 124 of 183 -- the rest
is `13 "Small Toilet\0" 12 "VAR_LETMEON\0" ...`. **EA shipped the debug symbols**, so a port gets
the original identifier names rather than bare indices. An earlier version of this file read the
whole file as code and emitted 58 words of string bytes as instructions.

698 of 718 carry that table; 20 do not. Where it exists it holds `1 + variableCount` entries in
560 of 698 and FEWER in the rest (by 1 to 5), never more -- so unnamed variables are dropped and
the exact rule is not pinned. Consequently the `0x10` tag is called a SYMBOL index rather than a
string index: 758 operands carry it across 17 distinct values, but **652 of them are `NAME` with
value 0**, and `SPAWNCHILD`/`SPAWNSOUND` carry values up to 22 that exceed some tables. The tag is
real and located; its indexing is not established.

⚠ **The extension is `.RSE` in UPPERCASE on 710 of the 718 entries, and `.rse` on 8.** Globbing
case-sensitively finds those 8, which is 1.1% of the set -- and the analysis it produces looks
entirely coherent, because 8 real files are still 8 real files. Match case-insensitively.

Teeth-check, because 81 of 81 mnemonics mapping 1:1 proves CONSISTENCY, not truth: shuffling the
mnemonic order within each file and rebuilding the same table leaves **71 of 81 ambiguous and 10 of
10,606 instruction slots intact**, on three separate seeds. The real alignment holds 10,606 of
10,606. A mapping that survives its own shuffled control is a mapping.

Counts: 718 `.rse` across 8 WADs (FANTASY/FRSE/HALLOW/HRSE/JUNGLE/JRSE/SPACE/SRSE -- each world WAD
and its source twin), 354 `.rss`, 279 distinct inner paths, 270 carrying both.
"""
import struct

TAG_OP, TAG_VAR, TAG_ADDR, TAG_STR, TAG_IMM = 0x80, 0x40, 0x20, 0x10, 0x00

# mnemonic and the number of instruction slots it was confirmed on. Every one of these was
# unambiguous: no mnemonic ever aligned with two different opcodes in any file.
OPCODES = {
    0x00: ('NOP', 2),
    0x01: ('CRIT_LOCK', 141),
    0x02: ('CRIT_UNLOCK', 232),
    0x03: ('COPY', 1009),
    0x05: ('SUB', 57),
    0x06: ('ENDSLICE', 327),
    0x07: ('GETTIME', 150),
    0x08: ('ADDOBJ', 517),
    0x0A: ('KILLOBJ', 223),
    0x0B: ('FADEOBJ', 133),
    0x0C: ('SETOBJPARAM', 5),
    0x0D: ('EVENT', 490),
    0x0F: ('FLUSHANIM', 13),
    0x10: ('TRIGANIM', 62),
    0x11: ('WAITANIM', 552),
    0x12: ('LOOPANIM', 183),
    0x13: ('TRIGWAITANIM', 120),
    0x15: ('TRIGANIMSPEED', 1),
    0x17: ('TRIGANIM_CH', 72),
    0x1B: ('GETANIM_CH', 18),
    0x1C: ('RAND', 31),
    0x1D: ('JSR', 37),
    0x1E: ('RETURN', 18),
    0x1F: ('BRANCH', 644),
    0x20: ('BRANCH_Z', 645),
    0x21: ('BRANCH_NZ', 909),
    0x22: ('BRANCH_NV', 51),
    0x23: ('BRANCH_PV', 73),
    0x25: ('NAME', 240),
    0x26: ('TEST', 1235),
    0x27: ('CMP', 21),
    0x2A: ('HUSH', 49),
    0x2B: ('HOP', 49),
    0x2C: ('WAIT', 394),
    0x2E: ('WAIT4ANIM', 148),
    0x2F: ('ADD', 514),
    0x31: ('DIV', 3),
    0x32: ('MOD', 4),
    0x33: ('TURBO', 14),
    0x35: ('TOUR', 57),
    0x36: ('BUMP', 37),
    0x37: ('COAST', 168),
    0x38: ('ADDHEAD', 40),
    0x39: ('DELHEAD', 40),
    0x3A: ('LIMBO', 19),
    0x3B: ('UNLIMBO', 19),
    0x3C: ('FORCEUNLIMBO', 18),
    0x3D: ('INLIMBO', 4),
    0x3E: ('LIMBOSPACE', 19),
    0x3F: ('SPAWNCHILD', 13),
    0x40: ('SPAWNSOUND', 19),
    0x41: ('REMOVECHILD', 3),
    0x42: ('SETVARINCHILD', 3),
    0x45: ('GETVARINPARENT', 4),
    0x46: ('BOUNCESETNODE', 1),
    0x47: ('BOUNCESETBASE', 3),
    0x48: ('BOUNCE', 3),
    0x49: ('UNBOUNCE', 3),
    0x4A: ('FORCEUNBOUNCE', 6),
    0x4B: ('BOUNCING', 19),
    0x4C: ('WALKON', 57),
    0x4D: ('WALKOFF', 62),
    0x4E: ('WALKGET', 50),
    0x4F: ('WALKST_FLOAT', 1),
    0x50: ('WALKFLOATSTAT', 1),
    0x51: ('WALKFLOATSTOP', 1),
    0x56: ('STARTSCREAM', 48),
    0x57: ('STOPSCREAM', 96),
    0x58: ('SINGLESCREAM', 53),
    0x59: ('SCREAMLEVEL', 90),
    0x5A: ('FINDSCRIPTRAND', 1),
    0x5C: ('SETREMOTEVAR', 2),
    0x5D: ('REPAIREFFECT', 142),
    0x5F: ('SETTIMER', 62),
    0x60: ('GETTIMER', 32),
    0x64: ('HOUR', 1),
    0x65: ('MIN', 1),
    0x66: ('SEC', 1),
    0x67: ('SETREVERB', 18),
    0x68: ('DIPMUSIC', 2),
    0x69: ('SPARK', 1),
}

# 25 values inside 0x00..0x69 are never used by any script on the disc:
UNSEEN = (0x04, 0x09, 0x0E, 0x14, 0x16, 0x18, 0x19, 0x1A, 0x24, 0x28, 0x29, 0x2D, 0x30,
          0x34, 0x43, 0x44, 0x52, 0x53, 0x54, 0x55, 0x5B, 0x5E, 0x61, 0x62, 0x63)

def header(d):
    """(version, variableCount, w0C, w10, w14, w18, w1C). Raises if the magic or padding is wrong."""
    if d[:4] != b'RSSE': raise ValueError('not RSSE: %r' % d[:4])
    if d[0x20:0x30] != b'Pad Pad Pad Pad ': raise ValueError('padding is not the literal Pad run')
    return struct.unpack_from('<7I', d, 4)

def symbols(d):
    """(tableOffset, [names]) for the trailing symbol table, or (len(d), []) when there is none.

    Found by parsing `u32 length` + that many bytes of printable NUL-terminated ASCII greedily from
    each word boundary and requiring it to land EXACTLY on end of file. That exactness is the whole
    check -- it is what makes the earliest passing offset the real start rather than a coincidence."""
    for off in range(0x30, len(d) - 3, 4):
        p, out = off, []
        while p < len(d):
            if p + 4 > len(d): break
            ln = struct.unpack_from('<I', d, p)[0]; p += 4
            if ln == 0 or ln > 256 or p + ln > len(d): break
            s = d[p:p + ln]; p += ln
            if s[-1] != 0 or any(b < 32 or b > 126 for b in s[:-1]): break
            out.append(s[:-1].decode('latin-1'))
        if p == len(d) and out: return off, out
    return len(d), []

def words(d):
    """(tag, value) for every CODE word, in order -- stopping before the symbol table."""
    end = symbols(d)[0]
    return [(struct.unpack_from('<I', d, o)[0] >> 24,
             struct.unpack_from('<I', d, o)[0] & 0xFFFFFF) for o in range(0x30, end - 3, 4)]

def disassemble(d):
    """Lines of `index  MNEMONIC  operands`. An operand keeps its tag so nothing is silently
    reinterpreted: v12 is a variable, @34 an address, s2 a string, a bare number an immediate."""
    out, pending = [], None
    for i, (tag, val) in enumerate(words(d)):
        if tag == TAG_OP:
            if pending: out.append(pending)
            nm = OPCODES.get(val, ('OP_%02X' % val, 0))[0]
            pending = '%4d  %-14s' % (i, nm)
        elif pending is not None:
            pending += {TAG_VAR: ' v%d', TAG_ADDR: ' @%d', TAG_STR: ' s%d', TAG_IMM: ' %d'}.get(tag, ' ?%d') % val
    if pending: out.append(pending)
    return out

if __name__ == '__main__':
    import sys
    d = open(sys.argv[1], 'rb').read()
    v, nvar, *rest = header(d)
    off, names = symbols(d)
    print('version 0x%08X  %d variables  rest %s' % (v, nvar, rest))
    print('symbols at 0x%X: %s' % (off, names))
    for l in disassemble(d): print(l)
