"""PS2 VU (VU0/VU1) microcode disassembler, enough to read a game's whole microprogram.

Usage: python3 tools/vu1dis.py <elf-or-raw> <file-offset> <byte-length> [vu-base-addr]

Each 64-bit instruction is a pair: the UPPER (float/FMAC) word is the high 32 bits, i.e. the
second little-endian word in memory, the LOWER (integer, load/store, branch) word is the first.
Upper bits 31..27 are the I/E/M/D/T flags; I means the lower slot is a 32-bit float immediate
(shown as LOI). Branch targets are 64-bit-instruction addresses (VU code addresses divide by 8).

Field layout follows Sony's VU manual as tabled in PCSX2's VU opcode tables. Anything not decoded
is printed as raw hex so an unknown op is visible rather than silently wrong.
"""
import struct, sys

BC = 'xyzw'


def dest(w):
    d = (w >> 21) & 0xF
    return ''.join(c for c, b in zip('xyzw', (8, 4, 2, 1)) if d & b)


def vf(n): return f'vf{n:02d}'
def vi(n): return f'vi{n:02d}'


def upper(w):
    d, ft, fs, fd, op = dest(w), (w >> 16) & 31, (w >> 11) & 31, (w >> 6) & 31, w & 63
    bc = BC[op & 3]
    if op < 0x1C:
        name = ['ADD', 'SUB', 'MADD', 'MSUB', 'MAX', 'MINI', 'MUL'][op >> 2]
        return f'{name}{bc}.{d} {vf(fd)}, {vf(fs)}, {vf(ft)}{bc}'
    two = {0x1C: 'MULq', 0x1D: 'MAXi', 0x1E: 'MULi', 0x1F: 'MINIi', 0x20: 'ADDq', 0x21: 'MADDq',
           0x22: 'ADDi', 0x23: 'MADDi', 0x24: 'SUBq', 0x25: 'MSUBq', 0x26: 'SUBi', 0x27: 'MSUBi'}
    if op in two:
        n = two[op]
        return f'{n}.{d} {vf(fd)}, {vf(fs)}, {"Q" if n.endswith("q") else "I"}'
    three = {0x28: 'ADD', 0x29: 'MADD', 0x2A: 'MUL', 0x2B: 'MAX', 0x2C: 'SUB', 0x2D: 'MSUB',
             0x2E: 'OPMSUB', 0x2F: 'MINI'}
    if op in three:
        return f'{three[op]}.{d} {vf(fd)}, {vf(fs)}, {vf(ft)}'
    if op >= 0x3C:
        hi = fd
        if hi == 0: return f'ADDA{bc}.{d} ACC, {vf(fs)}, {vf(ft)}{bc}'
        if hi == 1: return f'SUBA{bc}.{d} ACC, {vf(fs)}, {vf(ft)}{bc}'
        if hi == 2: return f'MADDA{bc}.{d} ACC, {vf(fs)}, {vf(ft)}{bc}'
        if hi == 3: return f'MSUBA{bc}.{d} ACC, {vf(fs)}, {vf(ft)}{bc}'
        if hi == 4: return f'ITOF{["0","4","12","15"][op&3]}.{d} {vf(ft)}, {vf(fs)}'
        if hi == 5: return f'FTOI{["0","4","12","15"][op&3]}.{d} {vf(ft)}, {vf(fs)}'
        if hi == 6: return f'MULA{bc}.{d} ACC, {vf(fs)}, {vf(ft)}{bc}'
        if hi == 7: return [f'MULAq.{d} ACC, {vf(fs)}, Q', f'ABS.{d} {vf(ft)}, {vf(fs)}',
                            f'MULAi.{d} ACC, {vf(fs)}, I', f'CLIPw.xyz {vf(fs)}, {vf(ft)}w'][op & 3]
        if hi == 8: return [f'ADDAq.{d} ACC, {vf(fs)}, Q', f'MADDAq.{d} ACC, {vf(fs)}, Q',
                            f'ADDAi.{d} ACC, {vf(fs)}, I', f'MADDAi.{d} ACC, {vf(fs)}, I'][op & 3]
        if hi == 9: return [f'SUBAq.{d} ACC, {vf(fs)}, Q', f'MSUBAq.{d} ACC, {vf(fs)}, Q',
                            f'SUBAi.{d} ACC, {vf(fs)}, I', f'MSUBAi.{d} ACC, {vf(fs)}, I'][op & 3]
        if hi == 10: return [f'ADDA.{d} ACC, {vf(fs)}, {vf(ft)}', f'MADDA.{d} ACC, {vf(fs)}, {vf(ft)}',
                             f'MULA.{d} ACC, {vf(fs)}, {vf(ft)}', f'?upper.{w:08x}'][op & 3]
        if hi == 11: return [f'SUBA.{d} ACC, {vf(fs)}, {vf(ft)}', f'MSUBA.{d} ACC, {vf(fs)}, {vf(ft)}',
                             f'OPMULA.xyz ACC, {vf(fs)}, {vf(ft)}', 'NOP'][op & 3]
    return f'?upper.{w:08x}'


def s11(v): return v - 0x800 if v & 0x400 else v
def s5(v): return v - 32 if v & 16 else v


def lower(w, pc):
    op = (w >> 25) & 0x7F
    d, it, is_, imm11 = dest(w), (w >> 16) & 31, (w >> 11) & 31, s11(w & 0x7FF)
    ftf, fsf = BC[(w >> 23) & 3], BC[(w >> 21) & 3]
    tgt = lambda: f'L{(pc + 1 + imm11) & 0xFFFF:04x}'
    if op == 0x00: return f'LQ.{d} {vf(it)}, {imm11}({vi(is_)})'
    if op == 0x01: return f'SQ.{d} {vf(is_)}, {imm11}({vi(it)})'
    if op == 0x04: return f'ILW.{d} {vi(it)}, {imm11}({vi(is_)})'
    if op == 0x05: return f'ISW.{d} {vi(it)}, {imm11}({vi(is_)})'
    if op == 0x08: return f'IADDIU {vi(it)}, {vi(is_)}, {(((w >> 21) & 0xF) << 11) | (w & 0x7FF):#x}'
    if op == 0x09: return f'ISUBIU {vi(it)}, {vi(is_)}, {(((w >> 21) & 0xF) << 11) | (w & 0x7FF):#x}'
    if op == 0x10: return f'FCEQ vi01, {w & 0xFFFFFF:#08x}'
    if op == 0x11: return f'FCSET {w & 0xFFFFFF:#08x}'
    if op == 0x12: return f'FCAND vi01, {w & 0xFFFFFF:#08x}'
    if op == 0x13: return f'FCOR vi01, {w & 0xFFFFFF:#08x}'
    imm12 = (((w >> 21) & 1) << 11) | (w & 0x7FF)
    if op == 0x14: return f'FSEQ {vi(it)}, {imm12:#05x}'
    if op == 0x15: return f'FSSET {imm12:#05x}'
    if op == 0x16: return f'FSAND {vi(it)}, {imm12:#05x}'
    if op == 0x17: return f'FSOR {vi(it)}, {imm12:#05x}'
    if op == 0x18: return f'FMEQ {vi(it)}, {vi(is_)}'
    if op == 0x1A: return f'FMAND {vi(it)}, {vi(is_)}'
    if op == 0x1B: return f'FMOR {vi(it)}, {vi(is_)}'
    if op == 0x1C: return f'FCGET {vi(it)}'
    if op == 0x20: return f'B {tgt()}'
    if op == 0x21: return f'BAL {vi(it)}, {tgt()}'
    if op == 0x24: return f'JR {vi(is_)}'
    if op == 0x25: return f'JALR {vi(it)}, {vi(is_)}'
    if op == 0x28: return f'IBEQ {vi(it)}, {vi(is_)}, {tgt()}'
    if op == 0x29: return f'IBNE {vi(it)}, {vi(is_)}, {tgt()}'
    if op == 0x2C: return f'IBLTZ {vi(is_)}, {tgt()}'
    if op == 0x2D: return f'IBGTZ {vi(is_)}, {tgt()}'
    if op == 0x2E: return f'IBLEZ {vi(is_)}, {tgt()}'
    if op == 0x2F: return f'IBGEZ {vi(is_)}, {tgt()}'
    if op == 0x40:
        sub, hi = w & 63, (w >> 6) & 31
        idr = hi
        if sub == 0x30: return f'IADD {vi(idr)}, {vi(is_)}, {vi(it)}'
        if sub == 0x31: return f'ISUB {vi(idr)}, {vi(is_)}, {vi(it)}'
        if sub == 0x32: return f'IADDI {vi(it)}, {vi(is_)}, {s5(hi)}'
        if sub == 0x34: return f'IAND {vi(idr)}, {vi(is_)}, {vi(it)}'
        if sub == 0x35: return f'IOR {vi(idr)}, {vi(is_)}, {vi(it)}'
        t = sub & 3
        if sub >= 0x3C:
            tbl = {
                (0, 0x0C): f'MOVE.{d} {vf(it)}, {vf(is_)}', (0, 0x0D): f'LQI.{d} {vf(it)}, ({vi(is_)}++)',
                (0, 0x0E): f'DIV Q, {vf(is_)}{fsf}, {vf(it)}{ftf}', (0, 0x0F): f'MTIR {vi(it)}, {vf(is_)}{fsf}',
                (0, 0x10): f'RNEXT.{d} {vf(it)}, R', (0, 0x19): f'MFP.{d} {vf(it)}, P',
                (0, 0x1A): f'XTOP {vi(it)}', (0, 0x1B): f'XGKICK {vi(is_)}',
                (0, 0x1C): f'ESADD P, {vf(is_)}', (0, 0x1D): f'EATANxy P, {vf(is_)}',
                (0, 0x1E): f'ESQRT P, {vf(is_)}{fsf}', (0, 0x1F): f'ESIN P, {vf(is_)}{fsf}',
                (1, 0x0C): f'MR32.{d} {vf(it)}, {vf(is_)}', (1, 0x0D): f'SQI.{d} {vf(is_)}, ({vi(it)}++)',
                (1, 0x0E): f'SQRT Q, {vf(it)}{ftf}', (1, 0x0F): f'MFIR.{d} {vf(it)}, {vi(is_)}',
                (1, 0x10): f'RGET.{d} {vf(it)}, R', (1, 0x1A): f'XITOP {vi(it)}',
                (1, 0x1C): f'ERSADD P, {vf(is_)}', (1, 0x1D): f'EATANxz P, {vf(is_)}',
                (1, 0x1E): f'ERSQRT P, {vf(is_)}{fsf}', (1, 0x1F): f'EATAN P, {vf(is_)}{fsf}',
                (2, 0x0D): f'LQD.{d} {vf(it)}, (--{vi(is_)})', (2, 0x0E): f'RSQRT Q, {vf(is_)}{fsf}, {vf(it)}{ftf}',
                (2, 0x0F): f'ILWR.{d} {vi(it)}, ({vi(is_)})', (2, 0x10): f'RINIT R, {vf(is_)}{fsf}',
                (2, 0x1C): f'ELENG P, {vf(is_)}', (2, 0x1D): f'ESUM P, {vf(is_)}',
                (2, 0x1E): f'ERCPR P, {vf(is_)}{fsf}', (2, 0x1F): f'EEXP P, {vf(is_)}{fsf}',
                (3, 0x0D): f'SQD.{d} {vf(is_)}, (--{vi(it)})', (3, 0x0E): 'WAITQ',
                (3, 0x0F): f'ISWR.{d} {vi(it)}, ({vi(is_)})', (3, 0x10): f'RXOR R, {vf(is_)}{fsf}',
                (3, 0x1C): f'ERLENG P, {vf(is_)}', (3, 0x1E): 'WAITP',
            }
            if (t, hi) in tbl: return tbl[(t, hi)]
    if w == 0: return 'NOP'
    return f'?lower.{w:08x}'


def disassemble(data, off, length, base=0):
    out = []
    for i in range(0, length, 8):
        lo, up = struct.unpack_from('<2I', data, off + i)
        pc = base + i // 8
        flags = ''.join(c for c, b in zip('IEMDT', (31, 30, 29, 28, 27)) if up >> b & 1)
        u = upper(up & 0x07FFFFFF)
        if up >> 31 & 1:
            l = f'LOI {struct.unpack("<f", struct.pack("<I", lo))[0]:g} ({lo:#010x})'
        else:
            l = lower(lo, pc)
        out.append(f'L{pc:04x}  {up:08x} {lo:08x}  {flags:<3} {u:<36} | {l}')
    return out


if __name__ == '__main__':
    data = open(sys.argv[1], 'rb').read()
    off, length = int(sys.argv[2], 0), int(sys.argv[3], 0)
    base = int(sys.argv[4], 0) // 8 if len(sys.argv) > 4 else 0
    print('\n'.join(disassemble(data, off, length, base)))
