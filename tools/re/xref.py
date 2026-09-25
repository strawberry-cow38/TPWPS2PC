#!/usr/bin/env python3
"""Cross-reference sweep for the PS2 executable.

⭐ WHY THIS EXISTS. Ghidra's own xrefs miss cases that matter here, and the two obvious ways to
write this yourself are both WRONG in ways that produce confident false negatives. Both defects
were hit for real on 2026-09-24 and both are fixed below; the comments are the point of the file.

Usage:
    python3 tools/re/xref.py <elf> 0x2abe18 [0x2abe1c ...]        # who touches these addresses
    python3 tools/re/xref.py <elf> --range 0x2abe00 0x2abe48      # ... anywhere in a block

⚠⚠ DEFECT ONE, and it is the expensive one: a `jal` clobbers only CALLER-saved registers. A sweep
that clears every register on a call throws away `lui s0, hi` held across it -- which is exactly
the shape a constructor uses -- and then reports the global it writes as "never written". That
happened: DAT_002ABE18 read as unwritten when it is written three times.

⚠⚠ DEFECT TWO: a pointer passed as an ARGUMENT never touches memory. If you only report loads and
stores, every `lui`+`addiu` that materialises an address into a0 for a call reads as "unreferenced".
That happened too: COutput_SPU2's own error strings looked like dead data.

⚠⚠ DEFECT THREE, and it took a third person to find: MIPS HAS A BRANCH DELAY SLOT. The
instruction after `jr`/`jal` runs BEFORE the transfer, so killing registers at the transfer throws
away what its own delay slot needs. A counter written back on the way out --
`lw; addiu; jr ra; sw` -- had its STORE vanish, and 0x2AA73C read as "loaded, never stored".
Fixing it also added a hit to this file's own documented control at 0x2abe18, so the tool was
under-reporting even on the example it shipped with.

⚠ AND THE RULE THAT OUTRANKS ALL THREE: a negative from this tool is a claim about this tool. Give every
search a control that MUST hit -- a nearby address you already know is referenced -- and believe
the negative only when the control fires.

⚠⚠ AND A CONTROL FIRING IS STILL NOT ENOUGH IF THE QUERY IS WRONG. On 2026-09-24 a sweep for
`justwater.ssh` at 0x36e8b4 -- where a regex happened to match -- returned nothing while its
control fired, and "no name test in code" went into a findings doc. The string's SLOT starts at
0x36e8b0, four bytes earlier, and the reference is real (0x2210b8). Take a string's address by
scanning back to the preceding NUL, never from where a pattern matched. See findings/ and the memory on negative searches.
"""
import struct
import sys

BASE = 0xFF000          # PS2 ELF: vaddr = file offset + 0xFF000

R = ['zero','at','v0','v1','a0','a1','a2','a3','t0','t1','t2','t3','t4','t5','t6','t7',
     's0','s1','s2','s3','s4','s5','s6','s7','t8','t9','k0','k1','gp','sp','fp','ra']
MEM = {0x23:'lw',0x2b:'sw',0x24:'lbu',0x20:'lb',0x28:'sb',0x25:'lhu',0x21:'lh',0x29:'sh',
       0x27:'lwu',0x37:'ld',0x3f:'sd'}
LOADS = {0x23,0x24,0x20,0x25,0x21,0x27,0x37}
# a jal clobbers these and leaves s0-s7/gp/sp/fp alone -- see DEFECT ONE
CALLER_SAVED = (1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,24,25)


def sweep(image, want, lo=0x100000, hi=None):
    """Yield (pc, mnemonic, address, dest_reg, base_reg) for every reference into `want`."""
    hi = hi if hi is not None else BASE + len(image)
    val, hits = {}, []
    # ⚠⚠ DEFECT THREE (found 2026-09-24, by astraclaw, while tracing guest activation IDs):
    # MIPS HAS A BRANCH DELAY SLOT, so the instruction AFTER a `jr`/`jal` executes BEFORE the
    # transfer takes effect. Applying the register kill AT the transfer therefore throws away the
    # state its own delay slot still needs. The real shape this hid:
    #
    #     1093b4  lw    v0, -0x58c4(v1)    <- the sweep saw this
    #     1093bc  addiu v0, v0, 1
    #     1093c0  jr    ra                  <- old code cleared every register HERE
    #     1093c4  sw    v0, -0x58c4(v1)    <- ...so this STORE read as nothing at all
    #
    # A counter incremented and written back on the way out of a function is a completely ordinary
    # thing for a compiler to emit, and it made 0x2AA73C read as "loaded, never stored".
    # ⭐ The kill is DEFERRED by one instruction instead.
    pending = None
    for off in range(lo - BASE, min(hi, BASE + len(image)) - BASE - 3, 4):
        w = struct.unpack_from('<I', image, off)[0]
        pc = off + BASE
        op, rs, rt, rd = w >> 26, (w >> 21) & 0x1f, (w >> 16) & 0x1f, (w >> 11) & 0x1f
        imm = w & 0xffff
        simm = imm - 0x10000 if imm >= 0x8000 else imm
        if op == 0x0f:                                   # lui
            val[rt] = imm << 16
        elif op in (0x09, 0x19):                         # addiu / daddiu
            if rs in val:
                val[rt] = (val[rs] + simm) & 0xffffffff
                if val[rt] in want:                      # DEFECT TWO: report the materialisation
                    hits.append((pc, 'addr', val[rt], R[rt], R[rs]))
            else:
                val.pop(rt, None)
        elif op == 0x0d:                                 # ori
            if rs in val: val[rt] = (val[rs] | imm) & 0xffffffff
            else: val.pop(rt, None)
        elif op in MEM:
            if rs in val:
                a = (val[rs] + simm) & 0xffffffff
                if a in want: hits.append((pc, MEM[op], a, R[rt], R[rs]))
            if op in LOADS: val.pop(rt, None)
        elif op == 0x00:
            f = w & 0x3f
            if f in (0x08, 0x09): pending = 'all'        # jr / jalr -- AFTER its delay slot
            elif f == 0x2d and rt == 0 and rs in val: val[rd] = val[rs]   # daddu rd,rs,zero = move
            else: val.pop(rd, None)
        elif op == 0x03:                                 # DEFECT ONE: caller-saved only
            pending = 'caller'                           # ...and AFTER its delay slot, as above
        elif op not in (0x02,0x04,0x05,0x06,0x07,0x14,0x15,0x16,0x17):
            val.pop(rt, None)
        # Apply a transfer's register kill only once its delay slot has been read.
        if pending is not None and not (op == 0x00 and (w & 0x3f) in (0x08, 0x09)) and op != 0x03:
            if pending == 'all': val.clear()
            else:
                for r in CALLER_SAVED: val.pop(r, None)
            pending = None
    return hits


def callers(image, target):
    """Every `jal target`, by its encoded word -- the census that says N-of-M call sites."""
    jal = (0x03 << 26) | ((target >> 2) & 0x3ffffff)
    return [i + BASE for i in range(0, len(image) - 3, 4)
            if struct.unpack_from('<I', image, i)[0] == jal]


if __name__ == '__main__':
    if len(sys.argv) < 3:
        print(__doc__); sys.exit(2)
    img = open(sys.argv[1], 'rb').read()
    args = sys.argv[2:]
    if args[0] == '--range':
        want = set(range(int(args[1], 0), int(args[2], 0), 4))
    elif args[0] == '--callers':
        for pc in callers(img, int(args[1], 0)): print(hex(pc))
        sys.exit(0)
    else:
        want = {int(a, 0) for a in args}
    found = sweep(img, want)
    for pc, mn, a, rt, rs in found:
        print(f"{hex(pc)}  {mn:5} {rt:4} {hex(a)}")

    # ⚠⚠ AN EMPTY SWEEP OVER A FUNCTION ADDRESS IS NOT A FINDING, AND IT LOOKS EXACTLY LIKE ONE.
    # This mode reports address MATERIALISATION -- lui/addiu, loads, stores. A function that is
    # only ever `jal`ed is never materialised, so it comes back silent. That silence read as
    # "nothing references this" on 2026-09-25 for a function with THIRTEEN call sites, and the
    # only reason it was caught is that the check had a control.
    #
    # ⭐ So the tool now answers the question the silence invites, rather than leaving it to be
    # misread: if nothing matched and the address is in the code range, count the call sites too.
    if not found:
        for a in sorted(want):
            if 0x100000 <= a < 0x2b0000:
                n = len(callers(img, a))
                print(f"# {hex(a)}: no address materialisation."
                      + (f" BUT {n} `jal` call site(s) -- it is CALLED, not referenced."
                         f" Use --callers {hex(a)}." if n else
                         " And no `jal` call sites either: not called by name anywhere."
                         " It may be virtual (look for its pointer in a vtable) or unreachable."))
