# `.LIP` — the advisor lip-sync tracks

**Structure and unit measured 2026-09-21; runtime consumer confirmed 2026-09-22.**
`tools/lip.py` and the managed `core/TPW.PS2.Data/LipTrack.cs` reader.

A flat `u32` mark list terminated by `FFFFFFFF`. 516 files, all in `LIPS.WAD`, and every one of the
516 satisfies all four of: length a multiple of 4, `FFFFFFFF` terminator, strictly increasing marks,
and an **odd** mark count. They alternate an active/silent lip-animation gate, initially active.
The earlier claim that odd counts ruled out alternating states was wrong: an odd number of
toggles from active ends inactive. The executable consumer, not the histogram, settles this.

Loader `0x105e48` takes the lip stem from a variant in the advisor message table at ELF address
`0x2a6ac8`. Playback sets the lip gate to true and the start time at `0x107a58..64`, attaching
the loaded track at advisor-object `+0x244`. Consumer `0x105f30` reads that same pointer, clears
the gate at `FFFFFFFF`, otherwise divides the mark by **1,000** and compares it strictly below
elapsed **milliseconds**. It advances at most one mark per call and toggles the flag at
`0x105f98..ac`. `LipTrack.Playback` preserves those boundaries and polling behavior.
Its caller at `0x106b54..0x106be8` reads this flag: inactive selects shape 0, a transition to
active selects shape 1, and an unchanged active gate permits random changes among five shapes.
Thus a mark does not encode a phoneme or a particular mouth pose. The shape setter `0x105fc8`
uses the same advisor object's model pointer and five part indices at `+0x24c`, clearing flag
`0x8000` on the selected part and setting it on the others. The renderer's consumption of that
flag remains untraced here. The managed gate does not reproduce random shape selection or rendering.

See [advisor.md](advisor.md) for the complete consumer chain and a specific identity:
`STR_ADVMES_OPEN_PARK`, text row 1030 → message 0 → sound ID 48 → bank index 47, `sp_001.mp2`
→ `English/sp_001.LIP` (and the corresponding French/German tracks). The catalogue also exposes
a shipped defect: message 268 selects `PS2_1.mp2` but requests missing `PS2_.lip` in all three
languages. A same-stem survey alone does not detect that defect.

**Three languages, not nine.** `English/`, `French/`, `German/`, 172 files each, against the text
database's nine locales. 243 distinct stems and only 128 present in all three, so the lip tracks
cover a subset of the speech and the subset differs per language.

They are per-clip timelines rather than offsets into one bank: several files begin at `0`, and 151
of the 172 English files overlap another file's value range.

## The unit is MICROSECONDS, and it is measured

⚠ **An earlier version of this document argued for microseconds from plausibility and said so.
It no longer has to.** The advisor speech banks exist; I had simply searched the wrong place.

They are not in any WAD — they sit at **ISO level**, which is why a search over WAD entries for a
same-stem partner returned zero and why the earlier note here wrongly concluded the clips were
"indexed some other way":

    /AUDIO/ADVISOR/ENGLISH/SPCHHD.SDT      7,776,992 bytes
    /AUDIO/ADVISOR/FRENCH/SPCHHD.SDT       5,054,593
    /AUDIO/ADVISOR/GERMAN/SPCHHD.SDT       9,003,599

Exactly the three languages that have `.lip` tracks. Each is an SDT variant 12345 bank of **170
sounds**, and **170 of the 172 `.lip` stems per language** match a sound name in their own
language's bank — `adds_01.lip` to `Adds_01.mp2`. Duration is the `+0x20` word of the sound's
40-byte header over 44,100 (see `tools/sdt.py`).

Dividing each track's last mark by its clip's duration, over **509 pairs**:

| read as | median ratio | pairs landing in 0.5 .. 1.0 |
|---|---:|---:|
| **microseconds** | **0.9759** | **469 of 509** |
| 100 ns ticks | 0.0976 | 0 of 509 |
| milliseconds | 975.90 | 0 of 509 |
| 44.1 kHz samples | 22.13 | 0 of 509 |

This is not a preference among readings that all roughly work: **the three rivals land zero pairs
between them.** 469 of 509 last marks fall inside their own clip at a median **97.6%** of its
length, which is where a mouth stops moving relative to where a file ends.

### English overruns and the timing distribution

40 pairs have a last mark past the end of the clip, and the split is lopsided — **37 English, 1
French, 2 German**. The language-specific distributions are:

| language | n | p10 | p25 | median | p75 | p90 | over 1.0 |
|---|---:|---:|---:|---:|---:|---:|---:|
| English | 169 | 0.9161 | 0.9577 | **0.9845** | 0.9941 | **1.0002** | 37 |
| French | 170 | 0.9560 | 0.9655 | 0.9736 | 0.9827 | 0.9879 | 1 |
| German | 170 | 0.9441 | 0.9635 | 0.9748 | 0.9828 | 0.9919 | 2 |

**English has higher median, p75 and p90, and a lower p10.** On the 169 stems measurable in all
three languages, English exceeds French on 105 — a real majority but only 62%, so it is not a
uniform offset either.

⚠ That identifies the SHAPE; the distribution alone cannot rule out mis-pairing or identify the
cause. The runtime associations are now checked independently in the advisor audit. The reading
it fits is the English audio being re-trimmed after its tracks were authored, and that is a
hypothesis with a distribution behind it, not a measurement.

## The mistake worth keeping

The earlier version said the audio was "not on the disc under these names — of 172 stems, **zero**
have a non-`.lip` file of the same stem in any WAD." Every word of that was true, and the
conclusion drawn from it was wrong, because the search covered WAD entries and the sentence was
about the disc. **The scope of the search became an assertion about the world.** Same shape as the
`.RSE` case-sensitivity miss in `rse.md`: the analysis was coherent, the population was chosen by
my own filter, and nothing inside it could show that.
