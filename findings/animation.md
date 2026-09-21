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

## It is NOT compressed, and the payload is integers not floats

The section at `0x88` holds no plausible float data — a float-plausibility sweep across all 27 KB
comes back almost empty. Its own sub-header reads `0x41, 0xD7, 9, 0x18` then `0xD4, 0, 0xA4`,
followed at `0xA4` by an ascending u16 list `9, 10, 11 … 32` — **24 entries, and `0x18` = 24 sits in
that sub-header**. After it come 48-byte records carrying an incrementing counter (2, 3, 4 …). So
this section is a sub-header, an index list, and a record array: ordinary nested structure.

⚠ **AND I ALMOST FILED IT AS COMPRESSED.** `monkey.aps`'s sections measure 7.1–7.6 bits/byte against
the model's 6.09, which is close to a RefPacked blob (7.51) — a tidy story for "no floats anywhere".
Checked it two ways instead:

- **No nested RefPack.** Zero `10 FB` occurrences in the whole file, where chance alone predicts ~1.
- **Entropy over 25 files of each kind**: `.aps` mean **5.41**, `.mps` mean **5.27**. They are the
  same. `monkey.aps` is simply a large dense one, not a representative one.

So the data is **packed integers/quantised keyframes, not a compressed blob** — readable, just not as
plain float matrices.

## The track table — reads perfectly on one file, does NOT generalise

Following the sub-header at `0x88` gives a clean, self-checking structure in `monkey.aps`:

```
sub-header  +0x08  u32  9      track count
            +0x0C  u32  0x18   index-list length (24)
            +0x10  u32  0xD4   offset of the track records
            +0x18  u32  0xA4   offset of the index list
index list @0xA4: u16 9, 10, 11 … 32   — 24 entries, and 0xA4 + 24*2 = 0xD4 exactly
track record, 48 bytes:  +0x00 node id | +0x04 flags | +0x20 data start | +0x28 data end
```

Nine records, ids `2, 3, 4, 33, 5, 6, 7, 8, 1`, and their data blocks **chain and tile the section**:
`0x284→0x370`, `0x378→0x253C`, `0x2544→0x2FEC`, `0x2FF0→0x3E24`, `0x3E28→0x52AC`, `0x52B4→0x58F0`,
`0x58F8→0x6C90` — ending 4 bytes before the next section at `0x6C94`. Nine tracks against
`monkey.mps`'s nine meshes.

⚠⚠ **AND IT DOES NOT HOLD ACROSS THE ARCHIVE.** Applied to all 89 `.aps`:

| test | files passing |
|---|---:|
| blocks ascend and fit, table assumed in section 0 | 28 / 89 |
| plus "index list abuts the records", table searched for in any section | **10 / 89** |

The stricter, more obviously-correct test passes **fewer** files. That is the signature of a layout
inferred from one example: every constraint that is true of `monkey.aps` in particular removes more
files than it keeps.

### Reading four files side by side found the real reason

`monkey`, `bouncy`, `bumper` and `mumbo` together show what is fixed and what is not:

| file | `0x1C` | sub-header `[2] [3] [4] [6]` | model meshes |
|---|---|---|---:|
| monkey | (12, 45) | 9, 0x18, 0xD4, 0xA4 | 9 |
| bouncy | (12, 29) | 5, 0x13, 0xCC, 0xA4 | 8 |
| bumper | (12, 31) | 21, 0x09, 0xB8, 0xA4 | 17 |
| mumbo  | (12, 36) | 5, 0x0E, 0xC0, 0xA4 | 7 |

- **`0x1C`'s first u16 is 12 in every file** — so it is a constant, not the node count this file
  previously guessed. The second (45, 29, 31, 36) varies per ride and is plausibly frame count.
- Section 0 is always `0x88` and the index list always `0xA4`.
- **The index list is 4-BYTE ALIGNED before the records.** `0xA4 + 0x13*2 = 0xCA`, but bouncy's
  records start at `0xCC`; bumper's `0xB6 → 0xB8`. My "must abut exactly" constraint was true of
  monkey and mumbo by luck, because their lengths happened to land on 4.

With alignment, 20 of 89 parsed — and the 69 failures were **my reader, not the format, for the
third time in this file's history.**

⚠ I was reading the section offset from `0x2C` unconditionally, i.e. always slot `0x28`. Simple
objects like `lights.aps` and `camera.aps` have **`(0, 0)` in that slot** and put their sections
elsewhere, so the reader took offset 0 and interpreted the file's own magic as a sub-header. I then
looked at that garbage, saw `2, 0x0A, 0x10000, …`, and concluded **"there is more than one kind of
section"** — an interesting structural claim invented entirely by my own indexing bug.

## Where it actually lands

Walking **every live (count, offset) slot**, and every 28-byte sub-header record within it:

```
section header record, 28 bytes:
   +0x08  u32  track count
   +0x0C  u32  index-list length
   +0x10  u32  offset of the 48-byte track records
   +0x18  u32  offset of the u16 index list   (records begin at align4(idxOff + len*2))

track record, 48 bytes:
   +0x00  node id      +0x04  flags
   +0x20  data start   +0x28  data end        (blocks ascend and tile the section)
```

**83 of 89 files consistent, 647 tracks.** The six that fail — `dizzyd`, `gokarts`, `spider`,
`wateride`, `pong`, `sgpuzzle` — all fail the same check, that data blocks ascend, which suggests
several track tables whose blocks restart rather than a different format. Not yet confirmed.

Also verified independently: **"a section is `count` records of 28 bytes, in bounds" holds for
89 of 89.**

**This is recorded as not general rather than tuned until it passes.** Loosening constraints until a
number looks acceptable would produce a reader that "works" on 89 files and is right about one. The
next step is a second and third file read cold — `bouncy.aps` (5 tracks), `bumper.aps` (21) — to see
which of the monkey-derived fields are real and which are coincidence, and the PSX side's 88-byte
descriptors and logical phase map (`TPW-PSXPC` `findings/animation-phases.md`) remain the closest
known relative to check shapes against.


## Inside a track block — suggestive, and NOT established

`monkey.aps` track 0 (node 2), block `0x284..0x370`, 236 bytes, read by eye:

```
+0x00  0x00010009            u16 9, u16 1
+0x04  0x00280004            u16 4, u16 40
+0x08  0x2B0                 an offset inside this block
+0x0C  f32  -4.1931,  2.4086,  -3.5531     ← the magnitude of a position
+0x18  f32   0.0148,  0.0128,   0.0031     ← small; scale or part of a rotation
+0x24  0x2C8, +0x28  0x2D0    two more in-block offsets
+0x2C  10
+0x30  0x2BC                 → bytes 00 22 25 27 28 29 2A 2D
                               = 0, 34, 37, 39, 40, 41, 42, 45
```

⭐ That byte run is **ascending, starts at 0, and ends at exactly 45 — the file's `0x1E`**, which is
the strongest hint yet that `0x1E` is a frame count and these are keyframe numbers. At `0x2D0` sit
repeated 32-bit values (`0x0981339E`, `0x07809BC2`, …) of the shape packed rotations take.

⚠⚠ **AND THE GENERALISATION FAILS: 0 of 204 track blocks.** Testing "the u32 at +0x2C is a keyframe
count and the u32 at +0x30 points at that many ascending bytes, starting at 0 and ending no later
than the frame count" passes **zero** blocks — including, on inspection, monkey's own: `+0x2C` is 10
but only 8 bytes ascend before the run breaks (`… 45, 49, 215`).

So the eight bytes ending exactly on 45 are **suggestive and unexplained**, the six floats are
**unassigned**, and the field offsets above are **one file read by eye, not a layout**. Written down
at that strength deliberately: the same "read one example, generalise, tune until it passes" loop has
already cost this format three wrong structural claims today.


## The track block header — established across 94 blocks

Classifying every word of every track block ≥64 bytes (is it an in-block offset? a plausible float?
a small int? zero?) rather than reading one file by eye:

```
track block:
  +0x00  u32              packed u16 pair
  +0x04  u32              packed u16 pair
  +0x08  u32  -> stream A   ALWAYS block+0x2C          94/94
  +0x0C  f32 x3           position magnitude (float in 67-82% of blocks)
  +0x18  f32 x3           float in 100% of blocks
  +0x24  u32  -> stream B   in-block offset            94/94
  +0x28  u32  -> stream C   in-block offset            94/94
  +0x2C  stream A, itself a sub-header:
           +0x00 u32 count   +0x04 u32 -> sub-stream   +0x08 u32 flag
```

Every check is 94 of 94: `+0x08`, `+0x24` and `+0x28` are in-block offsets in every block; `+0x18`,
`+0x1C` and `+0x20` are plausible floats in every block; stream A sits at `+0x2C` in every block;
and the offsets always order **A < A's sub-stream < B < C**, ascending and inside the block.

⚠ **This corrects the reading two sections above.** What looked like a fourth header offset at
`+0x30` is **stream A's own second word** — `+0x30` only reads as an offset because A begins at
`+0x2C`. The header holds **three** pointers, not four, and "the byte run at +0x30" from the
by-eye pass was A's sub-stream, not a header field.

⚠ **AND A's FIRST WORD IS NOT A COUNT.** Measuring it across all 94 blocks, most are values like
196,616 / 1,507,330 / 1,703,942 / 1,835,015 — which are packed u16 pairs (`0x0003_0008`,
`0x0017_0002`, `0x001A_0006`, `0x001C_0007`), not counts. Only a handful are the small numbers (7,
5, 2) that made "count" look right on the file I happened to open. Testing "stream length equals
A.count × some stride" scores **at most 2 of 94** for any stride, which is noise.

So the word is two u16s and calling it a count was, again, one example generalised.

**Still not decoded**: what any stream contains, what the six floats are, and what A's two u16s mean.
The keyframe run (`0, 34, 37, 39, 40, 41, 42, 45`, ending exactly on the frame count) is real and
lives in A's sub-stream, and nothing yet explains its length.


## ⭐⭐⭐ THE SPEC, READ OUT OF THE EXECUTABLE

Everything above was inferred from bytes. The parser states it. `SLES_500.32` validates the file at
`0x00167194` (magic `0x185AA030`, then `sltiu v0,v1,0x148` / `sltiu v0,v1,0x149` — **version must be
exactly 0x148**) and on success calls the loader at `0x00167250`. That loader is thirty lines:

```c
FUN_00167250(base):                             // the file loader
    if (base+0x20) *(int*)(base+0x20) += base;  // relocate
    if (base+0x24) *(int*)(base+0x24) += base;
    for (i = 0; i < *(char*)(base+0x1C); i++)   // ⚠ a BYTE count
        FUN_001672d8(*(int*)(base+0x20) + i*8, base);
    FUN_00168020(base);

FUN_001672d8(sec, base):                        // one 8-byte section entry
    if (sec[1]) sec[1] += base;
    for (i = 0; i < sec[0]; i++)
        FUN_00167358(sec[1] + i*0x1C, base);    // 28-byte records

FUN_00167358(rec, base):                        // one 28-byte record
    relocate rec[4], rec[5], rec[6]
    for (i = 0; i < u16 at rec+0x08; i++)
        if (rec[0] & 0x20) FUN_00167758(rec[4] + i*0x14)   // 20-byte tracks
        else               FUN_001674b0(rec[4] + i*0x30)   // 48-byte tracks
    for (i = 0; i < u16 at rec+0x0A; i++)
        FUN_00167780 / FUN_00167798 (rec[5] + i*8)         // 8-byte records

FUN_001674b0(track, base):                      // one 48-byte track
    relocate EIGHT pointers: +0x10 +0x14 +0x18 +0x1C +0x20 +0x24 +0x28 +0x2C
    if (+0x10) FUN_00168728(+0x10)
    if (+0x1C) FUN_001675d8(+0x1C)
    if (+0x20) (track[1] & 0x40000) ? FUN_001676d8(+0x20) : FUN_001675f0(+0x20)
    if (+0x24) FUN_00167720(+0x24)
```

### What this corrects

- **`+0x1C` is a BYTE**, not the u16 this file called a constant 12. It is the section count, and
  12 × 8 bytes from `0x28` ends at `0x88` — exactly where section 0 begins. **My scan of
  `0x28..0x80` only ever covered 10 of the 12 sections.**
- **A 28-byte record holds TWO counts** — u16 at `+0x08` and u16 at `+0x0A` — and two arrays, not
  the "track count + index length" I inferred.
- **`rec[0] & 0x20` selects 20-byte tracks instead of 48-byte ones.** That is why one fixed stride
  could never read every file, and it is the real reason six files failed.
- **A track has EIGHT relocated pointers at `+0x10`…`+0x2C`**, not the "data start / data end" pair
  I inferred. Those two were simply two of the eight, and their "ascending and tiling" was an
  artefact of relocated offsets ascending.
- `track[1] & 0x40000` picks between two different decoders for the `+0x20` stream.

### The lesson, plainly

Hours of statistical inference produced a model that was **right about shape and wrong about
meaning**, and every wrong part was stated with a validation score attached. One decompile of a
30-line function gave the exact layout, including two things inference could never have found: a
flag that switches record size, and four sibling pointers that are simply never populated in the
files I sampled.

**I had the R5900 disassembler working before I started guessing.** Asked what was stopping me from
cracking it, the honest answer was: nothing — I was reading the data instead of the code that reads
the data.


## The streams, same way — and it is now mechanical

Each of a track's eight pointers is handled by a small function that states its own sub-structure.
Two read so far:

```c
FUN_001675d8(p, base):          // the +0x1C stream
    if (*p) *p += base;         // relocates one pointer. that is all it does.

FUN_001675f0(p, base):          // the +0x20 stream
    relocate pointers at +0x08, +0x24, +0x28
    if ((u16 at +0x00) & 8) ... else
        for (i = 0; i < u16 at +0x02; i++)
            FUN_00167708(*(int*)(p+0x08) + i*0x0C, base)    // ⭐ 12-BYTE records
```

So the `+0x20` stream is a **count at `+0x02` and an array of 12-byte records** — and 12 bytes is
three floats, the size of a position key. A flag bit (`& 8`) switches to a different arm, exactly as
`rec[0] & 0x20` and `track[1] & 0x40000` do higher up. **This format switches layout on flags at
every level**, which is precisely what byte-inference cannot see and why the statistical model
plateaued.

**What remains is no longer guesswork, it is a list**: `FUN_00167708` (the 12-byte record),
`FUN_00168728`, `FUN_001676d8`, `FUN_00167720`, and the 20-byte-track arm `FUN_00167758`. Each is
tens of lines and each states its own layout the way these did.


## The rig poses — driven by hand, not yet by the file

Rotating `Dummy01` and re-evaluating the scene graph swings **`m_arm` and `m_arm1` only**: the body,
platform, rope fence and signs stay exactly where they are, because they are not children of that
pivot. `m_crate` and `m_shards` are hidden, per master.

That is worth having on its own — it proves the parent-relative transforms and the child propagation
are right, and that a `.mps` bundle **can** be posed. It is the machinery the animation will drive.

⚠⚠ **IT IS NOT THE GAME'S ANIMATION.** The angle is a number I chose. Nothing here reads a keyframe.
Saying otherwise would be the easiest and worst lie available at this point in the work.

## Where the keyframes actually stand

The 12-byte record is `u16 count (+2 more), u32 -> data, u32 flag`, and the data is a run of bytes.
`monkey.aps` gives clean ascending runs that start at 0 and end with **`0xD7`** — which is why an
earlier "ascending" test here broke at 215: `0xD7` is a terminator, not a value, and the count
includes it.

⚠ Tested across the archive that pattern holds for only **45 of 1,731 runs (2.6%)**, so it is
`monkey`'s shape and not the format's. Two further observations, unexplained: the runs reach 213,
well past the `0x1E` value this file guessed was a frame count; and the byte pair `0x63 0x64`
(99, 100) recurs inside many runs, which looks more like an opcode than a timestamp.

**The honest state: the container is read from the parser and is exact; the keyframe payload is
not.** Finding the evaluator — the code that turns a track into a matrix, which is a different
function from the loader and not reachable from it — is the next step, and is the same move that
cracked the container.
