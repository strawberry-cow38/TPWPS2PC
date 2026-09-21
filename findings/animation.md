# `.aps` — the animation format (in progress)

89 `.aps` files in `JUNGLE.WAD`, one beside each `.mps` that needs one. **This is the door**: a
`.mps` is an asset bundle and the file itself does not say which pieces to draw or where they sit
once posed — see `formats.md`. That decision lives here or in the ride's game logic.

`monkey.aps` is **92,928 bytes** against `monkey.mps`'s 34,796 — nearly three times the model.

## Header

```
0x00  u32   magic 0x185AA030      (cf. .mps 0x183076E4 — sibling formats)
0x04  u32   version 0x148         (.mps accepts 0x13E..0x140; this is the anim line)
0x08  char[20]  the file's own name, e.g. "monkey\monkey.aps"
0x1C  u16   12       GUESS: animated node count
0x1E  u16   45       GUESS: frame count
0x20  u32   0x28     start of the section table
0x28..0x78  (u32 count, u32 offset) pairs — the section table
```

⚠ Both interpretations at `0x1C`/`0x1E` are **GUESSES**. `monkey.mps` has 9 drawable meshes plus 4
helper nodes = 13, and 12 is one short of that; nothing yet connects 45 to anything.

## The section table (monkey.aps)

| header | count | offset | bytes |
|---|---:|---:|---:|
| +0x28 | 1 | 0x00088 | 27,660 |
| +0x38 | 1 | 0x06C94 | 9,652 |
| +0x40 | 1 | 0x09248 | 5,448 |
| +0x48 | 1 | 0x0A790 | 11,788 |
| +0x50 | **7** | 0x0D59C | 26,696 |
| +0x58 | 1 | 0x13DE4 | 2,964 |
| +0x70 | 1 | 0x14978 | 7,104 |
| +0x78 | 1 | 0x16538 | 1,480 |

Offsets ascend and tile the file exactly, so these are the top-level sections. Two of the ten pair
slots are `(0, 0)` — the layout is fixed and some sections are simply absent for this ride.

⚠ **The count-7 section is NOT an array of equal records.** 26,696 / 7 = 3,813, and reading at that
stride gives a sane first item and noise after it. Its first item begins
`5, 0x3C, 3, 0x1A, 0xD694, 0` — and `0xD694` points just past the item's own start, so these are
**variable-length sub-records that carry their own internal offsets**. The 7 is tempting because
exactly 7 of `monkey.mps`'s 9 meshes are drawn at rest, but **that is a coincidence until shown
otherwise** and the section's shape does not obviously support it.

## What is established, and what is not

**Established**: magic, version, the self-name, the section table, that the file tiles cleanly, and
that the count-7 section holds variable-length records with internal offsets.

**Not established**: what any section contains; whether 12 and 45 are nodes and frames; how a node
here maps to a `.mps` mesh (there are no readable node-name strings — the ~129 printable runs in the
file are compressed-looking noise, so nodes are almost certainly referenced by index).

**Next**: the 27 KB section at `0x88` is the largest and the natural place for keyframe data; and
the PSX side's 88-byte descriptors with a logical phase map (`TPW-PSXPC` `findings/animation-phases.md`)
are the closest known relative, so comparing shapes against that is likely cheaper than reading this
cold.
