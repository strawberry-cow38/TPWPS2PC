# The build ghost — what the PS2 draws under the cursor while you lay a path

Addresses are in the PAL `SLES_500.32` (PT_LOAD at EE 0x100000). READ means read out of the
executable's own code or data, through Ghidra and a disassembly of the same bytes. PREDICTED means
stated before it was checked. Nothing here is fitted to a screenshot.

## 1. The art is a set of numbered tile markers, and the numbers are the PSX's

`FUN_00220DC0` is the loader. It takes `Selectbox.ssh` from `data/generic/selection/`,
`cloudshadow.ssh` from `data/generic/extra/`, and **ten markers from `data/generic/tiles/`**:

```
24   165   166   167   168   169   170   172   173   175
```

into eleven consecutive words from `0x2F0A94`, leaving a gap at `0x2F0AAC` and `0x2F0AB8` — which
land exactly on **171** and **174**, the two numbers missing from the list. So the handles are an
array indexed by the marker's own number, and the two gaps are the two it does not load.

⭐⭐ **Those are the PSX build's marker sprite ids.** The PSX's verdict table (`0x800DBEFC`,
`findings/paths.md` §8 in the PSX port) holds 165, 175, 168, 169, 170, 173, and its queue ghost adds
167. Two different executables, read by different means, numbering the same markers the same way.

### 1.1 What each one is (byte-proven)

`/Generic/Tiles/<n>.tga` and `/Generic/Dynamic/Textures/<name>.tga` are the SAME FILE for nine of
the ten — identical SHA-256 over the decompressed bytes, with two deliberate mismatched controls
that correctly differ:

| id | same file as | what it draws |
|---|---|---|
| 24 | `green.tga` | flat green, and only **16x16** — the plain "will lay" fill |
| 165 | `blue.TGA` | flat teal — the neutral marker |
| 166 | `m_direct.tga` | a green chevron — a direction |
| 167 | `m_end.tga` | an orange arrow into a bar, in a green ring — an end |
| 168 | `m_enter.tga` | a green up arrow — an entrance |
| 169 | — | an orange down arrow, i.e. an exit. ⚠ **NOT** byte-equal to `m_exit.tga`, which is a separate image the loader never asks for |
| 170 | `m_front.tga` | a green bar along one edge — the front side |
| 172 | `m_inout.tga` | a green up arrow and an orange down arrow together |
| 173 | `m_link.tga` | **two interlocking rings in a green ring** — the join |
| 175 | `red.tga` | flat dark red — refused |

Beside them in the same folder, loaded by other tools: `m_nopath` (timber on red), `m_nocash` (a
dollar sign on red), `m_erase` (scissors), `m_break` (a bomb), `m_cross` (a small soft glow).

⭐ 173 settles a guess. The PSX report could only say "green ring = run ends on existing path",
determined by eye. The PS2 art says what the symbol IS: two links of a chain.

`Selectbox.tga` is different in kind — 64x64, pure white, and **3960 of its 4096 texels are
transparent**. What is left is an L in one corner: a bar along the top and a bar down the left. It is
one corner bracket, white so it can be tinted, not a box.

## 2. The verdict table (READ, `0x35B5E0`)

Eight words, verdict index to marker id:

```
0:165   1:175   2:168   3:169   4:170   5:173   6:173   7:175
```

The first six are the PSX's six, in the PSX's order. ⚠ **Index 6 differs**: 173 here, 167 on PSX.

Read at `0x11B40C` and at `0x127ACC`, both as `lui 0x36; addiu -0x4A20` — the same table, two
consumers.

## 3. The walk (READ, `FUN_001279C8`)

The walker takes a start and an end and covers them with an **L**: it compares `|dx|` with `|dy|`,
walks the longer axis first, then the other, then draws the corner tile last. Per tile:

```c
tile    = Tile(x, y);                                  // 0x14E138, the tile accessor
verdict = Validate(tile, kind, 1);                     // 0x1E81E0
if (refused_already) verdict = 1;                      // ⭐ latched
Draw(x, y, VerdictTable[verdict], 0, 1, 1);            // 0x1504F8
if (verdict == 1) refused_already = true;
```

⭐ **A refusal latches: after the first refused tile every later tile is drawn red**, whatever it
would have said on its own. That is the PSX's rule ("after a 1, every tile is 1") in the PS2's own
code — the same behaviour reached from two different disassemblies.

⚠ **The path tool itself lays STRAIGHT SEGMENTS**, one per press, the run carrying on from each
one's end — master, who plays it, says so, and the PSX report agrees from the other build, where
the tool "lays from the last corner to the cursor (axis-snapped)" and counts the run in CORNERS.
So the cursor is snapped to one axis before it ever reaches this walker, and the L is a shape the
walker can cover rather than one the path tool asks it for. What feeds it an off-axis end has not
been established.

It returns 0 when any tile refused, which is the gate on the press: the run lays only if no tile of
the ghost refuses. It also records the run's resolved end (`+0x24` x, `+0x28` y) and which leg ran
first (`+0x2C`: 1 along x, 2 along y), and sets `DAT_002B8398` only while the run has not moved off
its start tile — "this ghost is one tile".

## 4. The draw is a pair of quads draped on the terrain (READ, `FUN_001504F8`)

Not a sprite. The function calls the tile accessor `0x14E138` and the tile-height helper `0x152760`
**four times in a row**, submits through `0x221750`, then does it again — so **two quads per tile,
each with its four corners taken from that corner's own tile height**. The marker follows the
ground's slope rather than floating flat, and the two quads are the PS2's counterpart of the PSX's
underlay-plus-marker pair.

There is **not one COP1 instruction in the whole function**: the corner heights come through the
integer tile-height path, not floating-point. Whatever the PS2 does or does not do about the PSX's
`rsin` ripple, it is not in here.

## 5. The verdict rule (READ, `FUN_001E81E0(tile, kind, drawing)`)

`kind` is the tile kind being laid: **2 path, 4 queue, 0xD both**. In order:

1. tile flag 2 set → **1** (refused).
2. **Money**: `Cash() - Price()*10 < 1` → **1**. ⭐ The ghost goes red when you cannot afford the
   run — affordability is part of the preview, not only of the press.
3. A latched refusal (`DAT_002B837C`) refuses everything while not drawing.
4. An object standing on the tile refuses by its class (0xF, 1, 2, 4, 99) or when a property
   exceeds 0x27; a ride's own entrance and exit door tiles refuse by coordinate.
5. Flag 0x10 set and laying 2, 0xD or 4 → **0**.
6. Laying onto the same kind (2 on 2, 0xD on 0xD) → **0**. Tile type **0, grass → 0**.
7. Laying a **queue onto a path** (kind 4 over type 2 or 0xD): on the run's **last** tile → **5**,
   the link rings; anywhere else → refused. ⭐ That is the overlap tile, the only join between a
   queue and a path network, and the ghost marks it before you commit.
8. Laying a **path onto a queue** (kind 2 or 0xD over type 4): **5** when the queue end accepts it,
   else refused.
9. Same kind as the tile already is → **6, BUT ONLY ON THE RUN'S LAST TILE**. That arm is gated on
   `DAT_002B839C`, which `FUN_001279C8` sets once, just before it judges the corner tile; every
   earlier tile of the run falls through to the general branch and answers **0**. ⚠ An earlier
   draft of this file said plainly "same kind → 6", and the ghost built on it drew a connect symbol
   on every tile of a run laid along existing path. Master saw the string of them. The flag was
   already written down in §3 — it was read and then not used.

### 5.1 A run that connects closes the tool

**A run whose last tile joins something already there finishes the job, and the tool closes.**
Master says so for this build, and the PSX report has the same rule from the other one: its path
tool "CLOSES itself when a run finishes on existing path" (`0x8001C328` after the g7_3 sound), and
that case gets its own sound layered on the ordinary one and its own ghost marker — the link rings.
Three separate things in the game single out the same event, which is what makes it a rule rather
than a quirk.

⚠ Not read on PS2. The PS2 validator's two joining answers (5 and 6) are read, and so is the marker
they share, but the code that closes the tool after a press has not been followed here.

### 5.2 The tool's sounds

`/AUDIO/GLOBAL/UIHD.SDT` holds 26 sounds, and three of the four the path tool needs are named for
the job in their own filename. A scan of **all 41 sound banks on the disc** finds `connectPath` and
`rj_laypath_2` exactly once each, in this bank, so there is no second candidate:

| event | sound | length | basis |
|---|---|---|---|
| a run starts | `BUTTON01.vag` (5) | 75ms | master's ear — see below |
| a run is laid | `rj_laypath_2.vag` (11) | 101ms | named |
| the run connects | `connectPath.vag` (6) | 180ms | named |
| refused | `blnl_error1.vag` (3) | 325ms | named |
| taking it back | `tearup.vag` (13) | 192ms | ⚠ **a choice** — the bank's demolish sound |

⭐ **The connect sound is layered ON the lay sound, not instead of it** — the PSX report has them
on separate voices so neither cuts the other, because connecting is the success case of laying a
run rather than a different event. Each cue therefore gets its own player.

⚠ The PS2 code that plays them has not been located; the mapping is the names, the lengths and the
PSX's four events.

⚠⚠ **The first click is the one nothing names, and I got it wrong.** I picked `Select3` off its
43ms length and the word "select" in it; master, who can hear the console, says it is `BUTTON01`
(75ms). A name that merely SUITS a job is not evidence for it, and the length argument fit both
equally — the other three entries are named for the job itself, which is a different kind of claim
and the only reason they stand. An index is checked against the NAME at that index when the bank loads, so a
different disc build says the cue is missing instead of playing the wrong sound.

## 6. What is NOT established

- **`m_nocash`, `m_nopath`, `m_erase`, `m_break`, `m_cross` are not in the verdict table.** They are
  loaded from the same folder and clearly belong to the build tools, but which code draws them, and
  whether the money refusal above shows `m_nocash` rather than plain red, has not been read.
- ~~Whether the PS2 animates the markers.~~ **SETTLED: it does not.** Master, who has the game in
  front of them, says the PS2 markers do not ripple (2026-09-22). The PSX rolls its markers on a
  pair of `rsin` waves; this build draws them still. That agrees with the draw function having no
  float maths and no sine call — but the observation is what settles it, not the absence, because
  "the function I looked in has none" is not "the game has none".
- **`Selectbox`'s use.** It is loaded beside the markers and read at `0x2224D8` / `0x220F48`, but
  the code that places the bracket — and whether it is drawn four times for four corners — has not
  been followed.
- The meanings of kinds 5, 7, 0xA and 0x34, which the validator treats specially.
