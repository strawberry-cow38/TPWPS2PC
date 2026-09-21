# `.LIP` — the advisor lip-sync tracks

**Structure solved 2026-09-21. The time unit is not.** `tools/lip.py`; the layout is in its docstring.

A flat `u32` mark list terminated by `FFFFFFFF`. 516 files, all in `LIPS.WAD`, and every one of the
516 satisfies all four of: length a multiple of 4, `FFFFFFFF` terminator, strictly increasing marks,
and an **odd** mark count. That last one rules out the obvious reading — they are not open/close
pairs.

**Three languages, not nine.** `English/`, `French/`, `German/`, 172 files each, against the text
database's nine locales. 243 distinct stems and only 128 present in all three, so the lip tracks
cover a subset of the speech and the subset differs per language.

They are per-clip timelines rather than offsets into one bank: several files begin at `0`, and 151
of the 172 English files overlap another file's value range.

## The unit, and why it is only an argument

| read as | clip length: min / median / max |
|---|---|
| microseconds | 2.5 s / 6.3 s / 46.4 s |
| 100 ns ticks | 0.25 s / 0.63 s / 4.6 s |
| 44.1 kHz samples | 57 s / 142 s / 1052 s |

Sample-rate readings are out by three orders of magnitude. Microseconds gives plausible spoken-line
lengths and a 0.78 s median gap between marks, which is a mouth cadence. 100 ns is not impossible
but implies no clip shorter than a quarter second, which no spoken line is.

⚠ **That is plausibility, not measurement**, and it is the weakest claim in this folder. It is
recorded as a lead precisely so that nobody later finds "microseconds" in a comment and takes it for
something that was checked.

**What would settle it in one division:** the matching audio. It is not on the disc under these
names — of the 172 stems, **zero** have a non-`.lip` file of the same stem in any WAD, so the clips
sit in a sound bank indexed another way. Find one stem's bank entry, read its duration, divide.
