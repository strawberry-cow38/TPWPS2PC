"""PCSX2 savestate reader, for finding the park heightfield in live EE RAM.

A .p2s savestate is a ZIP holding `eeMemory.bin` -- the full 32MB EE RAM. That is the only
place the per-cell heightfield can be: tinyclaw traced the disc loader (0x1f3248) to a target,
`data\\<world>\\Terrain\\base.md2`, that exists for NO world, so whatever fills the field does
it at runtime from something else. A disc image cannot show a structure that only exists in RAM.

⭐ The control is PREDICTION, not a diff. The grids are known from the disc BEFORE looking:
JUNGLE 64x76 = 4864 cells, FANTASY 80x60 = 4800, HALLOW 96x52 = 4992, SPACE 96x54 = 5184
(or +1 per axis if stored per CORNER: 65x77 = 5005, ...). A buffer that is 4864 in a jungle
state and 4800 in a fantasy state is the array, confirmed against a number we committed to in
advance -- which is stronger than the raise-a-tile diff this replaced (the park has no terrain
tool, so that control never existed).

⚠ 0x2ea840 INDEXES STRAIGHT INTO THE EE RAM IMAGE -- do not subtract the ELF load base. The
PT_LOAD has vaddr == paddr == 0x100000 covering 0x100000..0x3af354, so 0x2ea840 sits inside it,
and EE RAM is 32MB at physical 0: byte offset 0x2ea840 in the dump IS the pointer. Subtracting
0x100000 out of habit lands on 0x1ea840, which reads as a plausible wrong number rather than an
obvious error. (tinyclaw.)

⚠ The VALUE there is a pointer into the same 32MB; mask off the KSEG/uncached bits before using
it as an index. A masked offset past the end of RAM is reported, not wrapped -- a wrapped pointer
would look like a hit.
"""
import sys, zipfile, struct, collections

EE_MASK = 0x1FFFFFFF
HEIGHTFIELD_PTR = 0x2EA840          # tinyclaw: set by 0x1f6858, zeroed by 0x1f6868

# ⚠ PLOT SIZE IS A PROPERTY OF THE TERRAIN FILE, NOT THE WORLD. Two files per world, and they
# differ everywhere except JUNGLE -- so a fantasy park is 4800 cells OR 4712 depending which file
# it loaded, and scanning for only one reads as "no buffer found". Scan the whole set.
# (My own finding, handed back to me by tinyclaw after I wrote the four-number version.)
GRIDS = [
    ('JUNGLE',  1, 64, 76), ('JUNGLE',  2, 64, 76),     # the only world whose two files agree
    ('FANTASY', 1, 80, 60), ('FANTASY', 2, 76, 62),
    ('HALLOW',  1, 96, 52), ('HALLOW',  2, 88, 56),
    ('SPACE',   1, 96, 54), ('SPACE',   2, 72, 62),
]

def expected():
    """Every count worth looking for: per-cell n*m and per-corner (n+1)*(m+1), since which one
    the engine stores is not yet known. Kept as {count: [labels]} so a hit names itself."""
    out = {}
    for world, f, nx, nz in GRIDS:
        out.setdefault(nx * nz, []).append(f"{world} t{f} {nx}x{nz} cells")
        out.setdefault((nx + 1) * (nz + 1), []).append(f"{world} t{f} {nx}x{nz} corners")
    return out


def ee_ram(path):
    with zipfile.ZipFile(path) as z:
        names = z.namelist()
        hit = next((n for n in names if n.lower().endswith('eememory.bin')), None)
        if hit is None:
            raise SystemExit(f"no eeMemory.bin in {path}; entries: {names}")
        return z.read(hit), names


def u32(d, o):
    return struct.unpack_from('<I', d, o)[0] if o + 4 <= len(d) else None


def probe(path):
    ram, names = ee_ram(path)
    print(f"{path}: EE RAM {len(ram):,} bytes; state entries: {len(names)}")
    p = u32(ram, HEIGHTFIELD_PTR)
    print(f"  [{HEIGHTFIELD_PTR:#x}] = {p:#010x}" + ("  (NULL -- no park loaded?)" if not p else ""))
    if not p:
        return
    off = p & EE_MASK
    if off + 64 > len(ram):
        print(f"  points outside EE RAM (masked {off:#x})"); return
    print(f"  -> EE offset {off:#x}; first 64 bytes:")
    for r in range(0, 64, 16):
        print("     " + " ".join(f"{b:02x}" for b in ram[off + r:off + r + 16]))
    print(f"  as u32: {[hex(u32(ram, off + i * 4)) for i in range(8)]}")
    # If the struct starts with dimensions, they will be the grids we predicted.
    want = expected()
    dims = {}
    for world, f, nx, nz in GRIDS:
        dims.setdefault(nx, []).append(f"{world} t{f} X={nx}")
        dims.setdefault(nz, []).append(f"{world} t{f} Z={nz}")
        dims.setdefault(nx + 1, []).append(f"{world} t{f} X+1={nx+1}")
        dims.setdefault(nz + 1, []).append(f"{world} t{f} Z+1={nz+1}")
    head = [u32(ram, off + i * 4) for i in range(16)]
    for i, v in enumerate(head):
        if v in want:
            print(f"  ⭐ word {i} = {v} -> {', '.join(want[v])}")
        elif v in dims:
            print(f"  ⭐ word {i} = {v} -> {', '.join(dims[v])}")
    print(f"  (counts sought: {sorted(want)})")


def histogram(path, addr, count, stride=1):
    ram, _ = ee_ram(path)
    off = addr & EE_MASK
    vals = ram[off:off + count * stride:stride]
    h = collections.Counter(vals)
    print(f"{path} @{addr:#x} x{count} stride {stride}: {len(h)} distinct, top {h.most_common(8)}")


if __name__ == '__main__':
    if len(sys.argv) < 2:
        raise SystemExit(__doc__ + "\nusage: p2s.py <state.p2s> [more.p2s ...]\n"
                         "       p2s.py --hist <state.p2s> <addr> <count> [stride]")
    if sys.argv[1] == '--hist':
        histogram(sys.argv[2], int(sys.argv[3], 0), int(sys.argv[4]),
                  int(sys.argv[5]) if len(sys.argv) > 5 else 1)
    else:
        for a in sys.argv[1:]:
            probe(a)
