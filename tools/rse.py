"""RSSE disassembler, corrected against the PS2 loader (0x1bfdf8).

See findings/rse-vm.md for executable semantics and audit evidence. The old decoder
started code at +0x30 and searched for printable trailing strings. The consumer
shows +0x30 is the code WORD COUNT and code starts at +0x34. Branches are relative
to +0x34. After code: a u32 byte size, that many bytes of NUL-separated strings,
then one length-prefixed name per variable (including zero-length names).

0x10 operands are BYTE OFFSETS in that first string pool, not symbol ordinals.
0x40 operands index variables; 0x20 operands address code words; 0x80 is opcode.
Numeric immediates are sign-extended from the low 16 bits by 0x1bbd18.

Header +8 variables, +0xc shared stack words, +0x10 instructions/slice,
+0x14 limbo slots, +0x18 bounce slots, +0x1c walking capacity.
Match .rse case-insensitively: the disc has both .rse and .RSE.

The original 81 source-derived names below are retained with their historical
alignment observations. Full world/path matching adds 0x19 and 0x43, both present
in shipped sources and binaries. Other 23 slots remain absent from this disc.
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
    0x19: ('LOOPANIM_CH', 1),
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
    0x43: ('GETVARINCHILD', 9),
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

# Other slots are not executed by the disc corpus.
UNSEEN = tuple(i for i in range(0x6a) if i not in OPCODES)

def header(d):
    """(version, variableCount, w0C, w10, w14, w18, w1C). Raises if the magic or padding is wrong."""
    if d[:4] != b'RSSE': raise ValueError('not RSSE: %r' % d[:4])
    if d[0x20:0x30] != b'Pad Pad Pad Pad ': raise ValueError('padding is not the literal Pad run')
    return struct.unpack_from('<7I', d, 4)

def layout(d):
    """(code end, string pool, variable names). Lengths follow the loader."""
    _, count, *_ = header(d)
    nwords = struct.unpack_from('<I', d, 0x30)[0]
    end = 0x34 + 4 * nwords
    def block(p):
        if p + 4 > len(d): raise ValueError('truncated string length')
        n = struct.unpack_from('<I', d, p)[0]; p += 4
        if p + n > len(d): raise ValueError('truncated string block')
        return p + n, d[p:p+n]
    p, pool = block(end)
    names = []
    for _ in range(count):
        p, name = block(p)
        if name and (name[-1] != 0 or b'\0' in name[:-1]): raise ValueError('bad variable name')
        names.append(name[:-1].decode('ascii') if name else '')
    if p != len(d): raise ValueError('unconsumed RSSE bytes')
    return end, pool, names

def symbols(d):
    """Compatibility listing: code end, pooled strings followed by variable names.
    Do not use this list's indices to resolve a 0x10 operand; use string_at."""
    end, pool, names = layout(d)
    return end, [s.decode('ascii') for s in pool.split(b'\0') if s] + names

def string_at(d, offset):
    pool = layout(d)[1]
    if not 0 <= offset < len(pool) or (offset and pool[offset-1] != 0):
        raise ValueError('not a string boundary')
    end = pool.find(b'\0', offset)
    if end < 0: raise ValueError('unterminated string')
    return pool[offset:end].decode('ascii')

def words(d):
    """Tagged code words only, with indices based at +0x34."""
    end = layout(d)[0]
    return [(w >> 24, w & 0xFFFFFF) for (w,) in struct.iter_unpack('<I', d[0x34:end])]

def disassemble(d):
    """Lines of `index  MNEMONIC  operands`. An operand keeps its tag so nothing is silently
    reinterpreted: v12 is a variable, @34 an address, s+2 a pool byte offset, a bare number a signed immediate."""
    out, pending = [], None
    for i, (tag, val) in enumerate(words(d)):
        if tag == TAG_OP:
            if pending: out.append(pending)
            nm = OPCODES.get(val, ('OP_%02X' % val, 0))[0]
            pending = '%4d  %-14s' % (i, nm)
        elif pending is not None:
            pending += {TAG_VAR: ' v%d', TAG_ADDR: ' @%d', TAG_STR: ' s+%d', TAG_IMM: ' %d'}.get(tag, ' ?%d') % ((val & 0x7fff) - (val & 0x8000) if tag == TAG_IMM else val)
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
