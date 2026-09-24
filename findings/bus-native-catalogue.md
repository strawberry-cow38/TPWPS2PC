# Narrow native bus catalogue trace (partial)

Read directly from PAL ELF in memory, disc LBA 262773, Mode2/2352 +24,
size 0x2b1160; ELF file 0x1000 maps to VA 0x100000. No assets extracted.
All addresses below are VAs. Timeboxed primary-code investigation.

## Important correction: record +0x0c = 0x61 is a lookup ID, not demonstrated flags

`0x17d7e8` scans 36-byte records at `0x2bf2b8`; at
`0x17d848/858/860` it compares record +0x0c with its input ID for equality.
World and +0x10 select the eligible variant. `0x17d8e8` is a simpler
first-match lookup also comparing +0x0c directly. Thus Bus1 and Bus2 share
logical catalogue ID **0x61 (97)**. +0x10 supplies variant-selection data;
it is not an animation count. Bus1 has 1 (Jungle 9), Bus2 2.
The special bit 8 in +0x10 is stripped for the ordinary comparison and
permits a special case when `0x14e160()+1 == 3`.

Manual decoding critical to this reading:
- 17d84c `000c200b`: movn a0,zero,t4 (clear unless the special-case comparison is equal).
- 17d85c `0003200a`: movz a0,zero,v1 (clear if +0x10 bit 8 is absent).
- 17b6e0 `0003100a`: movz v0,zero,v1.
- 17b6e4 `0048200b`: movn a0,v0,t0.
R5900 `mult rs,rt,rd` writes rd as well as LO; helper display omits rd.

## Proven type-15 path mapping

`0x17b240` indexes category table `0x2bf218 + type*8`.
Type 15 entry at **0x2bf290** is **{0x25, 0x362c58}**, with string
`features`. These **category flags 0x25** are separate from catalogue ID 0x61.
Bit 0x40 is clear, so world table `0x2bf2a0` selects:
0 = `Data\\Jungle`, 1 = `Data\\Hallow`, 2 = `Data\\Fantasy`, 3 = `Data\\Space`.
Bit 1 (mask 0x1) is set, so `0x17b344..358` formats `%s\\%s\\%s`
(world path, category, catalogue name).
`17b350 0273400b` is **movn t0,s3,s3**: a nonzero record +0x14 overrides
name in the path; every supplied bus record has +0x14 zero, so no override.
(The alternative-format instruction at 17b368 is movn a3,s3,s3.)

Consequently the exact generated stems are `Data\\<World>\\features\\Bus1`
or `...\\Bus2` (native names capitalized). Category flag 0x20 produces
load flags **0x10000000** at `0x17b3b4..3e8`. Type 15 is not type 12,
so `0x17b764..77c` takes ordinary loader `0x1f8678`, with name and path
from the builder. Companion MPS/APS loading is the previously traced chain
in mtr.md, not a newly established Bus.RSE binding here.

## Positive callable creation path found

`0x1476e0`:
1. Calls `0x17bb48(0x61)` at 1476ec. That resolves through `0x17d7e8`
   and tests selected record +0x20 for a nonzero loaded handle.
2. If available, calls **0x230a98** at 1476fc. This allocates 0x4c bytes,
   invokes `0x227158`, and returns the embedded object at allocation+0x0c.
   `230ab8 0002800a` is movz s0,zero,v0 (null preservation).
3. Stores object in **0x37e0d4** and invokes its +0x24 vtable slot +0x0c,
   passing `(this, 0x61, 0, -1)` at 147720.
   Constructor 227158 installs vtable **0x36f290** at allocation+0x30,
   i.e. embedded object+0x24. Slot +0x0c points to **0x228868**.
4. Calls 17d280, 17ce10, then **1479a0(0)** at 147760.

A direct call to creation routine **1476e0 exists at 149c90**.
This is a real ID-linked native creation path, not an asset-presence inference.
The broader initialization conditions around that call remain untraced.

## Native state/update consumer and next targets

**0x1479a0** takes a state argument, checks global object 37e0d4,
and suppresses repeat state by comparison with **37e0d8**. It dispatches
states 0/1/2/3. The main caller at **14c258** supplies **[0x395280]**.
Nearby **14c22c -> 147828** invokes object vtable +0x6c (table target
**228938**) and does additional work not fully classified here.

For state 2, 147c38..6c calls vtable +0x5c, target **228958**, with
`a1=5, a2=1, a3=0, t0=0, f12=1.0`.
For state 3, 147ca0..d4 calls the same target with `a1=5, a2=2` and
otherwise the same arguments. `44816000` is mtc1 at,f12, not an unknown
branch/state operation. These calls are a concrete candidate connection
to animation slot 5, but the callee's argument semantics were not traced;
do not label a2 as animation index/variant/trigger yet.

Nearby poll paths: **14c298 -> 1477c8 -> 17c880(object,0)** and
**14c2a8 -> 1477f8 -> 17c8c8(object,0)**. These are concrete next
completion/state targets; return semantics remain unproved.

## Unresolved boundary

No demonstrated connection yet from **228868 / 228958 / 228938** to
`/Features/Bus/bus.RSE`, its NAME Bus, WAITTRIGGERs, or Traffic Lights
remote variable. Native states 0..3 above must NOT be equated to the
script's status sequence 1,2,3,4,0,5,6. No claim of absence or of
unconditional initialization. Highest-value next trace: **228868**
(ID-to-instance setup), **228958** (state-2/3 call), and callers/writers
of **395280**; creation is anchored by **149c90 -> 1476e0**.
