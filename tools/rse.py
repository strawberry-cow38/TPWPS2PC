"""`.rse` reader — Theme Park World (PS2) COMPILED ride script.

⭐ The disc ships both forms: `.rss` is the un-stripped developer SOURCE (comments, `#include`
paths into EA's own tree) and `.rse` is the compiled bytecode. Having matched pairs makes this a
Rosetta Stone -- every opcode below was confirmed by decoding the compiled form beside its own
source, not by inference.

    +0x00  "RSSEQ"
    +0x08  u32, +0x0C u32, +0x10 u32     (16, 20, 50 in Monkey.rse)
    +0x20  "Pad Pad Pad Pad "            literal filler
    +0x30  code

Code is a stream of **u32 words**. A word with the **high bit set is an OPCODE** (its low 31 bits
are the opcode number); every other word is an operand of the opcode before it.

Confirmed against `Rides_Monkey_Monkey` (".init" of Crazy Ape), source line against bytecode:

    0x25  NAME       1 operand      NAME "Ape Ride"
    0x10  TRIGANIM   3 operands     TRIGANIM ANIM_Create 0 0
    0x2c  WAIT       1 operand      WAIT 1700 / 500 / 750  -> 0x6a4 / 0x1f4 / 0x2ee, all exact
    0x0d  EVENT      3 operands     EVENT OBJ_SOUND_LOC_RID -1 EVT_APE_THUMP -> (3, 0xffff, 0xdc)
    0x2e  WAIT4ANIM  0 operands
    0x06  ENDSLICE   0 operands

⭐ The WAIT literals are the proof: three different delays in one script, each appearing in the
bytecode as itself. And the two THUMP events share id `0xdc` while CRUNCH is `0xdb` -- the same
where it should be the same and different where it should differ.

⚠ Operand COUNTS beyond these are not yet established; the walker below infers them from the gap to
the next opcode word, which is right for a linear stream and would be wrong if any opcode can take
an operand with the high bit set.
"""
import struct

OPCODES = {0x25: 'NAME', 0x10: 'TRIGANIM', 0x2c: 'WAIT', 0x0d: 'EVENT',
           0x2e: 'WAIT4ANIM', 0x06: 'ENDSLICE'}

MAGIC = b'RSSEQ'


def header(d):
    if d[:5] != MAGIC: raise ValueError('not .rse: %r' % d[:5])
    return dict(w8=struct.unpack_from('<I', d, 8)[0],
                w12=struct.unpack_from('<I', d, 12)[0],
                w16=struct.unpack_from('<I', d, 16)[0],
                pad=d[0x20:0x30])


def walk(d, start=0x30):
    """[(address, opcode, name, [operands])] -- operands are the words up to the next opcode."""
    out, i = [], start
    cur = None
    while i + 4 <= len(d):
        w = struct.unpack_from('<I', d, i)[0]
        if w & 0x80000000:
            op = w & 0x7fffffff
            cur = (i, op, OPCODES.get(op), [])
            out.append(cur)
        elif cur is not None:
            cur[3].append(struct.unpack_from('<i', d, i)[0])
        i += 4
    return out


if __name__ == '__main__':
    import sys
    d = open(sys.argv[1], 'rb').read()
    h = header(d)
    print('%s: %d bytes, header %s pad=%r' % (sys.argv[1], len(d), (h['w8'], h['w12'], h['w16']), h['pad']))
    for addr, op, name, ops in walk(d)[:int(sys.argv[2]) if len(sys.argv) > 2 else 40]:
        print('  %04x  OP 0x%02x %-10s %s' % (addr, op, name or '', ops))
