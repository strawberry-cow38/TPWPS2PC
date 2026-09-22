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
MODEL_PTR = 0x2EA840   # the loaded terrain model (M3D2). tinyclaw's handle -- it is NOT the field.
FIELD_PTR = 0x2EA83C   # ⭐ THE PARK FIELD OBJECT, one word EARLIER. Found by dumping the globals
                       # around the model handle rather than trusting the label on it.

# ⭐⭐ The field object describes itself, which is what makes this a confirmation and not a guess:
#   +0x0c u32  NX        64 for jungle terrain_1 -- the size predicted from the disc's heightfield
#   +0x10 u32  NZ        76          "
#   +0x18 f32  2.0       the Y envelope max, matching the authored AABB
#   +0x20 f32  10.0      model units per cell (10 units = 1 world cell)
#   +0x24 ptr  cells     -> OBJ+0x30, NX*NZ entries of 2 bytes
#   +0x28 u32  2
# Cell encoding: byte0 & 0x3F is the HEIGHT and is only ever 0, 1 or 2 across all 4,864 cells,
# exactly the authored envelope. byte0 & 0x40 is a flag set on 67 CONTIGUOUS cells.
# ⚠ byte1 is per-cell and varies (24 / 0 / 57 / 55-60). Surface type or grass variant are both
# plausible -- we ship jgr_bas2..6 -- but it renders more repetitively than a hand-built park
# should, so it stays UNIDENTIFIED rather than named wrongly.
FIELD = dict(NX=0x0c, NZ=0x10, YMAX=0x18, CELL=0x20, CELLS=0x24)

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


def _member(path, want):
    """One entry's bytes. ⚠ PCSX2 v2 compresses savestates with ZSTANDARD (zip method 93), which
    python's zipfile refuses -- so read the raw member and pipe it through the zstd CLI. Using
    zipfile.read() raises NotImplementedError and reads as a corrupt state rather than a missing
    codec."""
    import subprocess
    with zipfile.ZipFile(path) as z:
        info = next((i for i in z.infolist() if i.filename.lower() == want.lower()), None)
        if info is None:
            raise SystemExit(f"no {want} in {path}; entries: {[i.filename for i in z.infolist()]}")
        if info.compress_type != 93:
            return z.read(info)
        with open(path, 'rb') as f:
            f.seek(info.header_offset)
            hdr = f.read(30)
            namelen, extralen = struct.unpack_from('<HH', hdr, 26)
            f.seek(info.header_offset + 30 + namelen + extralen)
            raw = f.read(info.compress_size)
    out = subprocess.run(['zstd', '-d', '-c'], input=raw, capture_output=True)
    if len(out.stdout) != info.file_size:
        raise SystemExit(f"{want}: expected {info.file_size} bytes, got {len(out.stdout)}")
    return out.stdout


def ee_ram(path):
    with zipfile.ZipFile(path) as z:
        names = z.namelist()
    return _member(path, 'eeMemory.bin'), names


def u32(d, o):
    return struct.unpack_from('<I', d, o)[0] if o + 4 <= len(d) else None


def probe(path):
    ram, names = ee_ram(path)
    print(f"{path}: EE RAM {len(ram):,} bytes; state entries: {len(names)}")
    fld = u32(ram, FIELD_PTR)
    mdl = u32(ram, MODEL_PTR)
    print(f"  [{MODEL_PTR:#x}] model = {mdl:#010x}")
    print(f"  [{FIELD_PTR:#x}] field = {fld:#010x}" + ("  (NULL -- no park loaded)" if not fld else ""))
    if fld:
        o = fld & EE_MASK
        nx, nz = u32(ram, o + FIELD['NX']), u32(ram, o + FIELD['NZ'])
        ymax = struct.unpack_from('<f', ram, o + FIELD['YMAX'])[0]
        cs = struct.unpack_from('<f', ram, o + FIELD['CELL'])[0]
        cells = u32(ram, o + FIELD['CELLS']) & EE_MASK
        print(f"    grid {nx} x {nz} = {nx*nz} cells, ymax {ymax}, {cs} units/cell, data {cells:#x}")
        want = expected()
        if nx * nz in want:
            print(f"    ⭐ {nx*nz} matches our prediction: {', '.join(want[nx*nz])}")
        hs = collections.Counter(ram[cells + i*2] & 0x3F for i in range(nx*nz))
        fl = sum(1 for i in range(nx*nz) if ram[cells + i*2] & 0x40)
        print(f"    heights: {sorted(hs.items())}   flag 0x40 on {fl} cells")
        for z in range(0, nz, max(1, nz // 24)):
            print("      " + "".join(".12345"[ram[cells + (z*nx + x)*2] & 0x3F] for x in range(nx)))
    p = mdl
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
