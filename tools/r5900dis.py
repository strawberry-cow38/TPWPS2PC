"""Small linear R5900 inspection aid. Requires Python capstone; never stops at LQ/SQ.

Usage: python3 tools/r5900dis.py /path/to/ps2.elf 0x1a6b08 0x1a6bf4
Addresses are virtual addresses mapped through ELF32 PT_LOAD, not file offsets.
This is not a full R5900 decoder: unimplemented MMI/COP2 words remain raw.
"""
import struct
import sys
from capstone import Cs, CS_ARCH_MIPS, CS_MODE_MIPS64, CS_MODE_LITTLE_ENDIAN


def disassemble(path, start, end):
    data = open(path, 'rb').read()
    if data[:6] != b'\x7fELF\x01\x01':
        raise ValueError('expected little-endian ELF32')
    phoff = struct.unpack_from('<I', data, 28)[0]
    stride, count = struct.unpack_from('<HH', data, 42)
    loads = [struct.unpack_from('<8I', data, phoff + i * stride) for i in range(count)]
    md = Cs(CS_ARCH_MIPS, CS_MODE_MIPS64 | CS_MODE_LITTLE_ENDIAN)
    regs = ('zero at v0 v1 a0 a1 a2 a3 t0 t1 t2 t3 t4 t5 t6 t7 '
            's0 s1 s2 s3 s4 s5 s6 s7 t8 t9 k0 k1 gp sp fp ra').split()
    for va in range(start, end, 4):
        seg = next(p for p in loads if p[0] == 1 and p[2] <= va < p[2] + p[4])
        word = struct.unpack_from('<I', data, seg[1] + va - seg[2])[0]
        op, rs, rt, rd = word >> 26, word >> 21 & 31, word >> 16 & 31, word >> 11 & 31
        imm = (word & 0x7fff) - (word & 0x8000)
        if op in (0x1e, 0x1f):
            text = f"{'lq' if op == 0x1e else 'sq'} ${regs[rt]}, {imm}(${regs[rs]})"
        elif op == 0 and word & 63 in (0x18, 0x19) and rd:
            text = f"{'mult' if word & 63 == 0x18 else 'multu'} ${regs[rd]}, ${regs[rs]}, ${regs[rt]}"
        elif op in (0x1c, 0x12):
            text = f'.word 0x{word:08x}  # MMI/COP2 not decoded'
        else:
            ins = next(md.disasm(struct.pack('<I', word), va, 1), None)
            text = f'{ins.mnemonic} {ins.op_str}' if ins else f'.word 0x{word:08x}'
        print(f'0x{va:08x}  {word:08x}  {text}')


if __name__ == '__main__':
    disassemble(sys.argv[1], int(sys.argv[2], 0), int(sys.argv[3], 0))
