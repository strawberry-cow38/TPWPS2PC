#!/usr/bin/env python3
"""Decode the research database out of a PCSX2 savestate's EE RAM.

Reads (all addresses index straight into eeMemory.bin, no load base):
  0x389650            the research STATE list: 60 x {u8 cat, u8 item, u8 percent, u8 level}
  [0x2e7540]          the research manager (0xAC bytes): +0 dirty, +4 budget, +8, 5 slots at
                      +0xc + 0x1c*i {weight, required, progress, active, complete, item, category},
                      thresholds +0x98 + 4*slot
  0x2b2a78            the asset-db singleton: +4 park slot (FUN_0014e160), +8 world (FUN_0014e170)
  0x360850[world]     -> per-world catalogue table (+park*4 + kind offsets), FUN_0012b1b8
  [0x2aadfc]          the loaded DBA directory: u32 count; entries at +4, 12 bytes {key, offset, ?};
                      record = dir + offset (FUN_0010f248)
"""
import sys, struct, os
sys.path.insert(0, '/home/cowtools/tpw-ps2pc/tools')
import p2s

MASK = 0x1FFFFFFF
KINDS = {3: ('Ride', 0x08, 0x14), 7: ('TourRide', 0x20, 0x2c), 6: ('TrackRide', 0x38, 0x44),
         8: ('TrackUpgrade', 0x50, 0x5c), 1: ('Coaster', 0x68, 0x74), 2: ('Feature', 0x80, 0x8c),
         4: ('Shop', 0x98, 0xa4), 5: ('Sideshow', 0xb0, 0xbc)}
RIDE_KINDS = {1, 3, 6, 7}

text = {}
def _load_text():
    """Text ids -> (STR_ key, English) straight from the disc's DATA.WAD (never a checked-in dump:
    this repo carries no game data). TPW_DATA_WAD overrides the default disc path."""
    import wadtree, textdb
    d = open(os.environ.get('TPW_DATA_WAD', '/mnt/bigdisk/tpw-ps2/DATA.WAD'), 'rb').read()
    got = {}
    for path, off, st, dc in wadtree.walk(d):
        if path in ('/Text/translations/eur/eng.dat', '/Text/translations/eur/id.dat'):
            got[os.path.basename(path)] = wadtree.read(d, off, st, dc)
    eng = textdb.read(got['eng.dat']); ids = textdb.keys(got['id.dat'])
    for (i, k, _), (_, e) in zip(ids, eng): text[i] = (k, e)
_load_text()

def u32(ram, a): return struct.unpack_from('<I', ram, a & MASK)[0]
def u16(ram, a): return struct.unpack_from('<H', ram, a & MASK)[0]
def s32(ram, a): return struct.unpack_from('<i', ram, a & MASK)[0]

def dba_record(ram, key):
    d = u32(ram, 0x2aadfc)
    if not d: return None
    d &= MASK
    n = s32(ram, d)
    for i in range(n):
        k = u32(ram, d + 4 + 12 * i)
        if k == key:
            return d + u32(ram, d + 8 + 12 * i)
    return None

def research_fields(ram, rec, kind):
    if kind in RIDE_KINDS:
        return [(u32(ram, rec + 0x48 + 0x34 * t), u32(ram, rec + 0x4c + 0x34 * t)) for t in range(3)]
    return [(u32(ram, rec + 0x28), u32(ram, rec + 0x24))]

def main(path):
    ram, names = p2s.ee_ram(path)
    print(f"== {path}: {len(ram):,} bytes EE RAM; members {names}")
    try:
        png = p2s._member(path, 'Screenshot.png')
        out = os.path.splitext(path)[0] + '.png'
        open(out, 'wb').write(png); print(f"  screenshot -> {out} ({len(png)} bytes)")
    except SystemExit as e:
        print('  no screenshot:', e)

    # --- the state list
    print("\n-- research STATE list @0x389650 (60 x 4):")
    state = {}
    used = 0
    for i in range(60):
        cat, item, pct, lvl = ram[0x389650 + 4 * i: 0x389650 + 4 * i + 4]
        if cat == 0xff and item == 0xff: continue
        used += 1
        state[(cat, item)] = (pct, lvl)
        print(f"   [{i:2d}] cat {cat} item {item:3d}  percent {pct:3d}  level {lvl}")
    print(f"   {used} of 60 records in use")

    # --- the manager
    mgr = u32(ram, 0x2e7540) & MASK
    print(f"\n-- research manager [0x2e7540] = {mgr:#x}")
    if mgr:
        print(f"   dirty {u32(ram, mgr)}  budget {u32(ram, mgr+4)}  +8 {u32(ram, mgr+8)}")
        for s in range(5):
            b = mgr + 0xc + 0x1c * s
            w, req, prog, act, comp, item, cat = (u32(ram, b), u32(ram, b+4), u32(ram, b+8), u32(ram, b+0xc),
                                                  u32(ram, b+0x10), s32(ram, b+0x14), s32(ram, b+0x18))
            print(f"   slot {s}: weight {w} required {req:#x} progress {prog:#x} active {act} complete {comp} item {item} cat {cat}")
        print("   thresholds:", [u32(ram, mgr + 0x98 + 4 * s) for s in range(5)])

    # --- the db singleton and the catalogue
    init, park, world = u32(ram, 0x2b2a78), u32(ram, 0x2b2a7c), u32(ram, 0x2b2a80)
    print(f"\n-- asset db 0x2b2a78: init {init} park-slot {park} world {world}")
    tbl = u32(ram, 0x360850 + 4 * world) & MASK
    print(f"   catalogue table [0x360850+{world}*4] = {tbl:#x}")
    print(f"   debug AllResearched [0x2b3070] = {u32(ram, 0x2b3070)}   test-park [0x2b72a8] = {u32(ram, 0x2b72a8)}")
    for kind in (3, 7, 6, 1, 2, 4, 5, 8):
        name, roff, coff = KINDS[kind]
        cnt = u32(ram, tbl + park * 4 + coff)
        recs = u32(ram, tbl + park * 4 + roff) & MASK
        print(f"\n   kind {kind} {name}: {cnt} items, key array {recs:#x}")
        for i in range(min(cnt, 64)):
            if kind == 8:
                key = u32(ram, recs + 16 * i + 8)   # FUN_0012ad78: handle at +8 of a 16-byte entry
                rec = None
            else:
                key = u32(ram, recs + 4 * i)
                rec = dba_record(ram, key)
            st = state.get((kind, i))
            stt = f"state pct {st[0]} lvl {st[1]}" if st else "no state rec"
            if rec is None:
                print(f"     [{i:2d}] key {key:#x}  (no DBA record found)  {stt}")
                continue
            rk = u16(ram, rec); nid = u32(ram, rec + 4)
            nm = text.get(nid, ('?', '?'))
            rf = research_fields(ram, rec, kind)
            print(f"     [{i:2d}] key {key:5d} kind {rk} name {nid:4d} {nm[1]!r:28} group/work {rf}  {stt}")

if __name__ == '__main__':
    for p in sys.argv[1:]: main(p)
