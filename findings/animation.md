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


## ⭐⭐⭐ The evaluator — and a second node array nobody had seen

`FUN_001accc0` starts an animation and `FUN_001a8d08` applies it. Both are short and both state
things that inference had no way to reach.

```c
FUN_001accc0(obj, ride, sectionIdx, recordIdx, flags):
    anim  = *(int*)(ride + 0x18);   model = *(int*)(*(int*)(ride+8) + 4);
    if (sectionIdx < *(byte*)(anim+0x1C))                       // pick a SECTION
        sec = *(int*)(anim+0x20) + sectionIdx*8
        if (recordIdx < sec[0])                                 // pick a RECORD
            rec = sec[1] + recordIdx*0x1C
            length = *(float*)(rec + 0x04)                      // ⭐ rec+0x04 is a FLOAT: duration
            FUN_001a8d08(rec, model)
            tracks = *(u16**)(rec + 0x10)
            for (n = *(u16*)(rec + 0x08); n; n--)               // rec+0x08 = track count
                node = *(int*)(model+0x48) + tracks[0] * 0xA0   // ⭐ track+0x00 is a MESH INDEX
                if (*(u32*)(track + 0x04) & 0x1000)
                    *node &= 0xFF7FFFFF                          // ⭐⭐ clears bit 0x800000
                tracks += 0x18 (u16) = 48 bytes                  // the 48-byte track stride

FUN_001a8d08(rec, model):
    for every mesh:  *flags &= ~0x10                             // clear "animated" on all
    list = *(u16**)(rec + 0x18);                                 // the u16 INDEX LIST
    for (n = *(u16*)(rec + 0x0C); n; n--) {                      // its length
        id = *list++;
        node = (id < meshCount) ? model[0x48] + id*0xA0          // a MESH, 160 bytes
                                : model[0x4C] + (id-meshCount)*0x60;   // ⭐ a HELPER, 96 bytes
        *node |= 0x10;                                           // mark "animated"
    }
```

### What this settles

- **`rec+0x0C` is the index-list length and `rec+0x18` is the list** — exactly what the statistical
  pass had guessed, now confirmed from code rather than from a score.
- **The index list is the set of nodes this animation drives.** Bit `0x10` on a node means "animated
  by the current animation"; it is cleared on every mesh first, then set on the listed nodes.
- **⭐ A model has TWO node arrays.** Meshes at `+0x48`, 160 bytes, count at `+0x30` — and
  **helpers at `+0x4C`, 96 bytes each**, addressed by ids at or above the mesh count. Verified in
  `monkey.mps`: `+0x4C` is `0x9F0`, exactly where the helper nodes sit, and they read out as
  `Head1, Head02, Head06, Head03, Head07, Head04, Head08, Head05` — all children of mesh 5, `m_arm`.
  This file had found those nodes by following parent pointers and called them "outside the mesh
  table"; they are a first-class array with its own header field and its own stride.
- **`monkey.aps`'s index list `[9 … 32]` is 24 helper ids and not one mesh.** The animation drives
  the helper rig; the meshes follow through the scene graph — which is precisely the `Dummy01` →
  `m_arm`/`m_arm1` behaviour already rendered.
- **⭐⭐ Visibility is a runtime flag, set by the animation.** A track with bit `0x1000` clears bit
  `0x800000` on its mesh. That closes the `m_crate` / `m_shards` question from `formats.md`: nothing
  in the `.mps` says which pieces to draw because **the animation says it**, by clearing a bit on the
  mesh as it plays.
- `rec+0x04` is a **float**, the animation's length — the first field in this format known to be a
  float rather than guessed to be one.


## ⭐⭐⭐ THE KEYFRAME ENCODING, from the interpolators

`FUN_001a8da8` samples a track each frame and calls one of two interpolators. Both are short enough
to read whole, and between them they define the key formats exactly.

**Position / scale — `FUN_00167d18`, linear:**

```c
FUN_00167d18(float t, keyA, keyB, out):
    out[i] = (float)(short)keyA[2 + 2i] * (1-t) + (float)(short)keyB[2 + 2i] * t   // i = 0,1,2
```

→ **an 8-byte key: `u16 time, int16 x, int16 y, int16 z`**, linearly interpolated.

**Rotation — `FUN_00167a48`, a SLERP:**

```c
q = (float)(short)key[n] * 3.051851e-05        // = 1/32768, so int16 normalised to [-1, 1]
... acos / sin, with a 1.5707964 (pi/2) fallback for the degenerate case ...
out[1..3] = sin((1-t)*w)/sin(w) * qA + sin(t*w)/sin(w) * qB
```

→ **a 10-byte key: `u16 time, int16 x, int16 y, int16 z, int16 w`** — a quaternion at 1/32768,
**spherically** interpolated. Called as `FUN_00167a48(t, rec+2, rec+0xC, out)`: the two keys are ten
bytes apart, which is the stride agreeing with the field list.

**And the clock**: `FUN_001acfc0` computes `(time * 1000.0) / 30.0 / speed` — the animation runs at
**30 fps**, and `rec+0x04` (the float established earlier) is its length in those units.

⚠ **What is still missing is WHERE, not WHAT.** Scanning each track's eight pointers for 8- or
10-byte keys with ascending times starting at zero finds none — the streams sit deeper, behind the
`+0x20` stream's 12-byte records, and the byte runs this file got excited about earlier (`0, 34, 37,
39 …` ending in `0xD7`) are at single-byte spacing and so cannot be 8- or 10-byte keys. They are a
lookup or index of some kind, not the keys.

So: **the encoding is known exactly and the addressing is not.** That is a much smaller gap than
this file had an hour ago, and it is the *opposite* shape of the usual one — normally you can find
the data and not read it.


## The addressing: the sampler reads a RUNTIME structure, not the file one

`FUN_001a8da8` takes its track array from `record[4]` (i.e. `rec+0x10`) and steps it by 48 bytes,
then reads:

```
track +0x08  u8   rotation key count      +0x0C  u32 -> rotation keys (10 bytes each)
      +0x09  u8   position key count      +0x10  u32 -> position keys (8 bytes each)
count < 2  =>  constant: read the single key directly at +2, +4, +6, +8
otherwise  =>  walk keys comparing the u16 time at +0 against the current frame,
               then FUN_00167a48(t, key, key+10, out)   // the SLERP
```

⚠ **Those fields do not hold that in the file.** Reading `monkey.aps`'s 48-byte records at
`rec+0x10` gives `+0x08 = 0`, `+0x0C = 2`, `+0x10 = 0` — while the only non-zero words are at
`+0x20` and `+0x28`, which are the stream pointers this file mapped earlier.

So the structure the sampler walks is **not** the structure on the disc: something between load and
playback builds it — most likely the `+0x20` stream's 12-byte records being expanded into per-node
tracks, which is exactly the indirection `FUN_001675f0` and `FUN_00167708` exist to relocate.

**State: the key encoding is exact (from the interpolators), the runtime track layout is exact (from
the sampler), and the mapping between them and the on-disc layout is the one remaining link.** The
next move is the function that consumes the `+0x20` stream at playback rather than at load — the
same approach that has produced every correct thing in this file.


---

# ⭐⭐⭐ SOLVED — and the "runtime structure" above was WRONG

**Correction first.** The section immediately above concludes that the sampler walks a structure
built at load from the `+0x20` stream, because the fields it reads held `0, 2, 0` in the file. That
is not what is happening. There is no runtime expansion. **There are two track formats in this
file, chosen by a flag**, and I was reading one format's field list against the other format's
bytes.

`FUN_00167358` picks them, and it says so in one line:

```c
if ((*param_1 & 0x20) == 0)  FUN_001674b0(tracks + i*0x30, base);   // 48-byte tracks
else                         FUN_00167758(tracks + i*0x14, base);   // 20-byte tracks
```

`FUN_001a8da8` branches on **the same bit** to sample them. The `+0x08/+0x09` counts and
`+0x0C/+0x10` key pointers I documented belong to the **20-byte** track. `monkey.aps` uses the
**48-byte** one. Both readings were of real code; I joined them to the wrong file.

⭐ The tell was available and ignored: `FUN_001674b0` relocates `+0x10 … +0x2C` and **not `+0x0C`**,
so `+0x0C` could never have been a file pointer in a 48-byte track. A pointer field that the
relocator does not touch is not a pointer. See [[feedback_read_the_parser_not_the_bytes]] — this is
exactly its "layout-switching flags" case.

## The 20-byte track — keys inline (`FUN_00167758`, `FUN_001a8da8`)

```
+0x00 u16  node index          +0x08 u8 rotation key count   +0x09 u8 position key count
+0x0C u32 -> rotation keys     10 B: u16 time, int16 x,y,z,w   (/32768, SLERP)
+0x10 u32 -> position keys      8 B: u16 time, int16 x,y,z     (linear)
```

`FUN_00167758` relocates `+0x0C` and `+0x10` and nothing else, which is the whole confirmation.
The sampler's other half skins meshes with these, so this is the **skeletal** path.
⚠ **Zero of JUNGLE.WAD's 89 `.aps` files use it** — all 1,229 tracks are the 48-byte kind.

## The 48-byte track — VERTEX MORPH ANIMATION (`FUN_001674b0`, `FUN_001a6d68`)

```
+0x00 u16  node index (mesh when < mesh count, else helper)
+0x04 u32  flags   0x1000 = has a vertex stream    0x40000 = alternate player (FUN_001a7e18)
+0x10 +0x14 +0x18 +0x1C +0x20 +0x24 +0x28 +0x2C    eight relocated pointers; +0x20 is the stream
```

### The vertex stream (`FUN_001675f0` relocates it, `FUN_001a6d68` plays it)

```
+0x00 u16 flags      ⭐ bit 0x08 = key times are 1 BYTE; clear = u16
+0x02 u16 group-record count       +0x04 u16 group count      +0x06 u16 total vertices
+0x08 u32 -> group records, 12 B each
+0x0C f32 offset.x   +0x10 offset.y   +0x14 offset.z
+0x18 f32 scale.x    +0x1C scale.y    +0x20 scale.z
+0x24 u32 -> group list, u16 each (group -> group record)
+0x28 u32 -> packed vertex data

group record (12 B):
  +0x00 u16 vertex count (the stride through the packed data is count*4 bytes)
  +0x02 u16 cursor index      +0x04 u32 -> key times      +0x08 f32 t (runtime scratch)
```

### ⭐ The packing, from the dequantiser

```c
X = (float)((v << 0x16) >> 0x16) * scale.x + offset.x    // bits  0..9,  SIGNED 10-bit
Y = (float)((v << 0x0c) >> 0x16) * scale.y + offset.y    // bits 10..19, SIGNED 10-bit
Z = (float)((v << 0x02) >> 0x16) * scale.z + offset.z    // bits 20..29, SIGNED 10-bit
```

**One 32-bit word is a whole vertex.** The game proves the field width itself: it builds the node's
bounds as `offset - 512*scale  ..  offset + 511*scale`, which is exactly the range of an int10.

### Playback

```c
while (times[cursor + 1] < now) cursor++;
t = (now - times[cursor]) / (times[cursor + 1] - times[cursor]);
vertex = lerp(dequant(keyA), dequant(keyB), t);      // 30 fps
```

## Validation — three independent checks, with controls

| check | result |
|---|---|
| All 89 `.aps` in JUNGLE.WAD parse to their own declared shape | **89 / 89**, 0 failures |
| Key-time lists ascending from 0, read at the width flag `0x08` selects | **1,699 / 1,699** |
| ⭐ the same lists read at the *wrong* width (the CONTROL) | **132 / 1,699** — and those 132 are exactly the bit-clear ones |
| `sum(groupRecord.count over the group list) == header+0x06` | **290 / 290** streams |

The control is the part that matters: a byte/u16 mix-up cannot hide, because reading 1,567 byte
lists as u16 produces garbage and reading 132 u16 lists as bytes produces garbage, and the flag
partitions them with **no overlap and no remainder**.

Animation lengths come out as authored round numbers — 100, 150, 120, 140, 200, 80, 125, 50 frames
(3.3 s … 6.7 s at 30 fps), which is the sanity check no format guess survives by accident.

⚠ And the byte runs this file dismissed earlier — *"`0, 34, 37, 39 …` ending in `0xD7`, at
single-byte spacing and so cannot be keys"* — **were the key times.** They were found, measured,
and argued away because they did not fit the stride of the format I had wrongly assumed.


## ⭐ The last link: which mesh vertex each animated one drives — `mesh+0x98`

An `.aps` stream says *how* vertices move but never *which* vertices. That mapping is in the
**model**, at `mesh+0x98` — a field inside the 160-byte mesh entry, so it ships in the file.

`FUN_001a6d68` walks it while emitting each animated vertex:

```c
do { a = *p++; write(gsPacket + (a & 0xfffc)); } while (a & 2);
```

* one **run** of entries — continuing while bit 1 is set — is one animated vertex;
* each entry's `a & 0xfffc` is a GS address;
* addresses step **12 bytes**, three words, one vertex, so an address's **rank** among the sorted
  unique addresses is its strip-vertex index.

| mesh | strip verts | unique GS slots | runs | animated verts |
|---|---:|---:|---:|---:|
| m_boxes | 258 | 258 | 91 | 91 |
| m_sign | 8 | 8 | 4 | 4 |
| m_body | 257 | 257 | 74 | 74 |
| m_arm | 103 | 103 | 37 | 37 |
| m_arm1 | 104 | 104 | 37 | 37 |
| m_crate | 186 | 186 | 56 | 56 |
| m_shards | 36 | 36 | 18 | 18 |

**7 of 7 on both columns.** `mesh+0x98` is **0** for exactly the meshes with no vertex stream
(`m_base`, `m_sign2`), which is the negative control.

⚠ **I nearly shipped a guess instead.** Before finding this I built the mapping by de-duplicating
strip vertices by position: 5 of 7 meshes matched and two were off by one, which reads as a
rounding problem rather than a wrong method. It was the wrong method — the ordering it produced was
scrambled, the centroids matched while every individual vertex was ~12 units out, and the model
rendered as a blob of the right size. **Guessing scored 71%. Reading the model scored 100%.**
[[feedback_read_the_parser_not_the_bytes]].

## What Crazy Ape actually does

`monkey.aps` holds **12 sections** — a ride has several animations (215, 100, … frames). Section 0,
the long one, is the ride's show, and the keyframes state the gag outright:

```
vertex 0 of m_body:  times [0, 62, 99, 100, 105, 117, 215]
  key0..key2  (0.206, -0.837,  1.797)      the ape squashed inside the crate
  key3        (0.169, -5.747,  9.950)      frame 100 -- it bursts out
```

The whole body goes from ~5 units tall to ~30 **between frame 99 and frame 100**. Rendering the
first second and seeing a tiny ape is not a decoder bug; it is the ape hiding in the box.

⭐ Worth keeping as a habit: the first render looked wrong, and the right move was to *measure the
pose extent over time* rather than start adjusting the decoder. The data explained itself.


## A file holds MANY animations, and only one of them breaks the crate

`monkey.aps`'s 12 sections carry **14 records** — 14 separate animations, not one:

```
sec0   215f | m_sign, m_body, m_arm, m_arm1, m_crate, m_shards, m_boxes
sec2   100f | m_body, m_arm, m_arm1          sec3  125f | m_body, m_arm, m_arm1
sec4   335f | m_body                         sec5  60/40/60/60/40/60f | m_body  (x6)
sec5   112f | m_body, m_arm, m_arm1          sec6   80f | m_body
sec9   120f | m_body                         sec10  80f | m_body
```

⭐ **Only section 0 touches `m_crate` and `m_shards`** — it is the ride's show: the ape bursts out
at frame 100 *and the crate smashes*. The shards are its debris.

That answers a question left open days ago, when the static render showed stray shards and master
said *"shards and crate arent meant to be there"*. They are not stray geometry and they are not a
format bug — they are **debris that should not exist yet**. ⚠ **I first wrote that the game hides them with the track
flag `0x1000` / mesh bit `0x800000`. That is wrong** -- see the retraction below.

⚠ **Not implemented here.** The renderer draws every part for the whole clip, so 36 shards are on
screen from frame 0. Stated rather than filtered: a render that quietly drops the parts that do not
fit is the thing this repo keeps catching itself doing.
[[feedback_exclusions_hide_the_defect]]

**Still open on `.aps`:** the per-track visibility bit above; the other seven track pointers
(`+0x10`, `+0x14`, `+0x18`, `+0x1C`, `+0x24`, `+0x28`, `+0x2C` — `+0x28` is set on every monkey
track and is only ~4 bytes); the 20-byte skeletal path has no file in JUNGLE.WAD to test against,
so it is read but unexercised.


## ⚠ RETRACTION: `0x800000` is not the show/hide bit, and nothing in the file hides the shards

I claimed the shards are hidden by track flag `0x1000` clearing mesh bit `0x800000`. Reading the
**caller** kills it. `FUN_001acfc0` runs, every frame:

```c
FUN_001a8d08(record, model);                     // evaluator
if (track flags & 0x41000)
    for each track:  if (flags & 0x1000)  mesh[0] &= ~0x800000;    // CLEARED here
FUN_001a8da8(...);                               // sampler -> FUN_001a6d68 ... |= 0x800000  SET here
```

Cleared and set again on every frame of normal playback. That is **playback state, not an artistic
show/hide.** `0x800000` also has 28 load sites across the binary, at least 12 of them tests, and
`FUN_001aa460` uses it as one bit of a `0xf40000` clear-mask on a render path — a general-purpose
flag, not a per-mesh visible bit.

### What the data actually says

The shards are at **full size and parked in mid-air** for the whole first half of section 0:

| frame | m_shards world Y | m_crate size |
|---|---|---|
| 0 – 99 | **3.80 .. 5.93** | 0.24 x **0.03** x 0.24 |
| 60 | 3.76 .. 5.91 | 1.40 x 1.57 x 1.41 |
| 105 | 4.05 .. 6.32 | 0.24 x **0.03** x 0.24 |
| 160 | **-0.46 .. 2.27** | 0.24 x 0.03 x 0.24 |

The ape reaches Y 4.17 at full height, so the shards sit **above his head** until frame ~105, then
fall onto the ride.

⭐ **The crate hides itself geometrically** — squashed to a 0.03-unit-thin speck before it exists
and again after it smashes, the same trick the ape uses to hide inside it. **That is the artists'
convention for "not here yet", and the shards do not follow it.**

So: the shards are plainly debris, they are plainly not meant to be on screen at frame 0, and
**neither the animation data nor the flag I named hides them.** Either the in-game camera never
frames that high, or there is a draw gate still unfound. Recorded as unresolved rather than
papered over. [[feedback_validate_before_claiming]]
