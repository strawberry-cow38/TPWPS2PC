# The runtime tile map, and why there is no "no-placement map" to draw

## The record: 8 bytes per tile

`0x14E138` is the accessor and returns `base + (y * width + x) * 8`, with the base at
`DAT_003952EC` and the dimensions at `DAT_003952F0` / `DAT_003952F4` (getters `0x14E0F8` and
`0x14E108`). Eight bytes per tile — the size the PSX port already carries as `ParkMap.TileBytes`.

**Read, not assumed.** Histogramming what the engine loads out of those eight across all 132 call
sites of the accessor gives `lbu +0`, `+1`, `+2`, `lhu +4`, `lbu +7`, which lands field for field
on the PSX `MapTile` (type, raw, links, a u16 ground, shade, flags). Two independent confirmations:

- `+2` is only ever masked with `0xFE 0xFD 0xFB 0xF7 0xEF 0xDF 0xBF 0x7F` — eight single-bit
  clears, so it is the eight path links, as that port has it.
- `0x152760` converts a tile pointer back to a world position and reads **`tile[+1] << 2`** as the
  Y. So `+1` is the height in quarter units, which is the PSX port's "raw height byte (+1)".

## The buildability test

`0x18E278(x, y)` is the predicate, and it is bounds-checked against `DAT_002E2354` /
`DAT_002E2358`:

    if (TestFlags(x, y, 2)) return 1;      // flags bit 1
    if (TestFlags(x, y, 3)) return 1;      // bits 0 or 1
    return 0;

24 call sites. `+7 & 0x02` is also tested inline at `0x191718`. The PSX port documents bit 1 of
the same byte as "nothing may be built here".

The flags helpers are `0x18E6A8` (OR a mask in), `0x18E6D8` (clear a mask), `0x18E710` (test a
mask), `0x18E740` (zero the byte).

## ⚠ Is there an authored no-placement map? NOT ESTABLISHED

Four sites OR the literal `0x2` into a byte at `+7` — `0x14ED20`, `0x14ED40`, `0x14ED60`,
`0x14ED80` — four consecutive tiles, immediately after a build-tool call at the literal coordinate
`(35, 65)`. Four tiles poked by name, which is a special case rather than a map.

⚠⚠ **AND THAT IS AS FAR AS IT GOES.** I said in a previous version of this file that bit 1 is
*only* set there, and that is not something my search can support:

- The scan was for `sb reg, 7(reg)` anywhere in `.text`. That is a store to offset 7 of **any
  structure**, not of a tile — and the 64 hits include literals like 12345, 99 and 255, which no
  flags byte would take. So the count never was 64 tile-flag writes; I never enumerated the real
  set.
- Two of the writers OR a mask held in a **register**: `0x18E6CC` (the shared `SetFlags(x,y,mask)`
  helper) and `0x14E5A4` (a second setter that computes the tile pointer inline). A register mask
  can carry bit 1, and I have not enumerated the callers of the second one — `jal` finds none, so
  it is entered some other way.

So the honest state is: bit 1 is real, tested in 24 places through `0x18E278` and inline at
`0x191718`, and the only *literal* writes of it are four hardcoded tiles. Whether an authored map
also supplies it is **open**, and the place to look is the fill of the tile array at park load.

## ⚠⚠ Two methodological misses in getting here, both worth keeping

1. **A bit of an index is not a flag.** I read `byte0` bit 1 of the *authored* cell as
   "unbuildable" and drew it. Master called it nonsense on sight and was right: `0x222230` splits
   that byte into bits 0-3 and bits 4-7 as two **indices**, so testing one bit selects the values
   {2,3,6,7,10,11,14,15} — an arbitrary subset of a lookup, which is why it looked structured.
   Same shape as the withdrawn "`0x40` is a raised flag".
2. **A search scoped to one caller is not a search.** I concluded "bit 1 is never set" from
   scanning only the call sites of the accessor — and the writer at `0x14E5A4` computes the tile
   pointer inline, so it was invisible to that scan. The full scan found it in one pass *because
   it carried a control that it had to hit*.
3. **And then I did it again, one rung up.** Having found that writer, I claimed from the wider
   scan that bit 1 is set in only four places. But that scan matched a store to offset 7 of ANY
   structure, and two of the writers take their mask in a register. The scan was broader than the
   first and still narrower than the claim. ⭐ The pattern to watch: every time, the search was
   sound and the SENTENCE built on it reached further than the search did.

## Open

Where the tile array is FILLED at park load. `0x151498` zeroes the base; the fill is not yet
traced, and if any of it comes from a resource rather than from code, that resource is where an
authored map would live.
