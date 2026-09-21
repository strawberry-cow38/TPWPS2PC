# `.LIP` — the advisor lip-sync tracks

**Solved 2026-09-21 — structure and unit both.** `tools/lip.py`; the layout is in its docstring.

A flat `u32` mark list terminated by `FFFFFFFF`. 516 files, all in `LIPS.WAD`, and every one of the
516 satisfies all four of: length a multiple of 4, `FFFFFFFF` terminator, strictly increasing marks,
and an **odd** mark count. That last one rules out the obvious reading — they are not open/close
pairs.

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

### The English overrun is a SHIFT, not 37 bad pairs

40 pairs have a last mark past the end of the clip, and the split is lopsided — **37 English, 1
French, 2 German**. Mis-pairing would leave the bulk of English sitting on the French/German centre
and add outliers. It does not:

| language | n | p10 | p25 | median | p75 | p90 | over 1.0 |
|---|---:|---:|---:|---:|---:|---:|---:|
| English | 169 | 0.9161 | 0.9577 | **0.9845** | 0.9941 | **1.0002** | 37 |
| French | 170 | 0.9560 | 0.9655 | 0.9736 | 0.9827 | 0.9879 | 1 |
| German | 170 | 0.9441 | 0.9635 | 0.9748 | 0.9828 | 0.9919 | 2 |

**English's whole distribution is displaced upward and is wider at both ends** — higher median, p75
and p90, and a *lower* p10. Whatever produces the 37 is acting on all 169, so those 37 are the top
of a shifted distribution rather than a set of broken joins. On the 169 stems measurable in all
three languages, English exceeds French on 105 — a real majority but only 62%, so it is not a
uniform offset either.

⚠ That identifies the SHAPE and rules out mis-pairing. It does not identify the cause. The reading
it fits is the English audio being re-trimmed after its tracks were authored, and that is a
hypothesis with a distribution behind it, not a measurement.

## The mistake worth keeping

The earlier version said the audio was "not on the disc under these names — of 172 stems, **zero**
have a non-`.lip` file of the same stem in any WAD." Every word of that was true, and the
conclusion drawn from it was wrong, because the search covered WAD entries and the sentence was
about the disc. **The scope of the search became an assertion about the world.** Same shape as the
`.RSE` case-sensitivity miss in `rse.md`: the analysis was coherent, the population was chosen by
my own filter, and nothing inside it could show that.
