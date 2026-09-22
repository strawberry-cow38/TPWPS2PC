# Advisor rules, messages, speech and lips

Decoded against the PAL `SLES_500.32` executable on 2026-09-22. The `.ass` pair is an advisor
rule VM, separate from RSSE. The speech/text/lip join is a **275-record table in the executable**,
at virtual address `0x2a6ac8`. The `.dba` files instead contain indexed park asset definitions.
Their directory and common text-name field are decoded; most of their payload is still unknown.

Managed readers: [AdvisorRules.cs](../core/TPW.PS2.Data/AdvisorRules.cs),
[AdvisorCatalogue.cs](../core/TPW.PS2.Data/AdvisorCatalogue.cs),
[LipTrack.cs](../core/TPW.PS2.Data/LipTrack.cs), and
[AssetResourceDatabase.cs](../core/TPW.PS2.Data/AssetResourceDatabase.cs).
The sound browser now displays the selected sound's actual message text and lip association.
This does **not** implement a running park simulation or an animated advisor head.

## Evidence scope and reproduction

All addresses below are **ELF virtual addresses**, not file offsets. Disassembly uses the repo's
R5900 helper, which handles `LQ`/`SQ`; no external disassembler is used by the managed readers.

```sh
python3 tools/r5900dis.py /path/to/ps2.elf 0x10da78 0x10de38
python3 tools/r5900dis.py /path/to/ps2.elf 0x10e4b8 0x10e6a0
python3 tools/r5900dis.py /path/to/ps2.elf 0x106338 0x106440
python3 tools/r5900dis.py /path/to/ps2.elf 0x1078d0 0x107bd0
python3 tools/r5900dis.py /path/to/ps2.elf 0x2636a4 0x263a50
python3 tools/r5900dis.py /path/to/ps2.elf 0x10f148 0x10f2a0
dotnet run --project tools/TPW.PS2.AdvisorAudit -- /path/to/disc.bin --list-rules
```

SHA-256 of the measured inputs (identification, not the audit's correctness argument):

| Input | SHA-256 |
|---|---|
| `SLES_500.32` / `ps2.elf` | `231771d7f39cc29e7573ce62f437af15d97737a64c1d1c49df8404ead0b7577a` |
| `Generic/Advisor/headers.ass` | `29cdf18b69425ebdf70d664e5b77d5870238d6a6bc902ab8f64f4019b63172c4` |
| `Generic/Advisor/opcodes.ass` | `55ce35bada915a5dd12b068dfb02a36920b7b80d2cf30b8b9a36caef13a4daca` |
| `arsdb.dba` and `arsjapdb.dba` | `ddda8a68e8dc1a28e875ce2f0a81a1006f52a4bdad1cc626cddc016053d01fd4` |
| `arsusdb.dba` | `9a6b04eb4b23b325ac9e9fb47650e4767e226640f528e7afe78d691f50edfb6a` |

The executable reader maps ELF32 `PT_LOAD` segments and checks instructions in the actual
consumers at `0x106420`, `0x107918`, `0x107928`, and `0x263934` before using this table profile.
Only this executable profile is supported. Testing all regional *assets* on this disc does not
establish the addresses or behavior of a separate US/Japanese executable.

## `.ass`: first establish the pointers

Loader `0x10da78` references the filename strings at `0x3597f0` and `0x359818`, then stores the
loaded headers at rule-object `+0xd0` (`0x10db0c`) and opcodes at `+0xd8` (`0x10db64`). It divides
the header size by 12 and stores the rule count in byte `+0xd4` (`0x10db7c..0x10dba4`).

The consumer is `0x10dc50`: **it loads these same two members**, at `0x10dd2c` and `0x10dd4c`.
It takes the header's first halfword, doubles it, adds the opcode pointer, and calls the
interpreter at `0x10e4b8` (`0x10dd58..0x10dd64`). This connects the interpreter to these files
before assigning meanings to instructions.

### `headers.ass`: 106 records of 12 bytes

All numbers are little-endian. These are runtime structures dumped with their scratch members
uninitialized; the `CC` bytes are not additional script instructions or authored timestamps.

| Offset | Type | Meaning | Consumer evidence |
|---|---|---|---|
| `+0` | `s16` | Starting **word** offset in `opcodes.ass` | `lh`, multiply by two, add loaded opcode pointer at `0x10dd58..64` |
| `+2` | `u16` | Delay in **game calendar days** after successful evaluation | Load and add to current day at `0x10dd88..94`; day getter and calendar below |
| `+4` | `u32` | Runtime next eligible day; disk bytes ignored | Unsigned strict `< currentDay` at `0x10dd38..40`; initialized to zero at `0x10dbc0` |
| `+8` | `u32` | Runtime day of last ordinary condition failure; disk bytes ignored | Written on interpreter result 1 at `0x10dd98`; read at `0x10dd48` for elapsed guard; initialized to current day at `0x10dbc4..d4` |

There is a width quirk: the last field is written with `sw` but read with `lhu`. Elapsed state is
`(short)(currentDay - (ushort)lastFailureDay)`, not an unrestricted 32-bit difference.

The clock is not seconds or milliseconds. `0x10dd10..1c` calls `0x16ae90` then `0x16b218`.
The latter returns calendar-object `+0x10`; `0x16b240..0x16b2d4` increments that counter alongside
day-of-month, month, and year, using the month-length table at `0x361d88` and a 12-month rollover.
Thus the first two delays of 60 are 60 game days. The exact wall-clock rate of a game day is not
established here.

### `opcodes.ass`: signed halfword instructions

The 2,816 bytes hold 1,408 words, split into 106 contiguous, END-terminated programs. Header
offsets are `0, 14, 28, 47, 60, 73, 77, 81, ...`. The nine-entry dispatch table at `0x3599a0`
handles opcodes 1 through 9; opcode 0 is handled at the loop boundary. `v` is an index into the
rule object's 79 signed halfword state variables. Immediates and comparison operands are signed.

| Code | Operands (words after opcode) | Operation | Body VA |
|---:|---|---|---|
| 0 | none | END, return 0 (`Completed`) | `0x10e678..84` |
| 1 | `v, value` | Continue if `state[v] == value`, otherwise return 1 | `0x10e50c` |
| 2 | `v, value` | Continue if `state[v] != value`, otherwise return 1 | `0x10e534` |
| 3 | `v, value` | Continue if `state[v] < value`, otherwise return 1 | `0x10e55c` |
| 4 | `v, value` | Continue if `state[v] > value`, otherwise return 1 | `0x10e580` |
| 5 | `v, value` | Add, storing only the low 16 bits | `0x10e5b0` |
| 6 | `v, value` | Set; indices 56..77 also update event-counter storage | `0x10e5d8` |
| 7 | `message` | Submit a message through the advisor's message path | `0x10e610` |
| 8 | `message` | Invoke the message's text/UI action | `0x10e63c` |
| 9 | `days` | Continue if `state[78] > days`, otherwise return 2 | `0x10e65c` |

There are no jumps or embedded strings. Ordinary failed comparisons return 1
(`ConditionFailed`); opcode 9 instead returns 2 (`ElapsedBlocked`). Execution is sequential:
stores and message effects before a failed guard are not rolled back.

SET's second store is to object `+0x9e + 2*(v-56)` at `0x10e5fc..0c`. The separately exposed
event-counter routine at `0x10ddd8` accepts 22 counters. ADD does not perform this mirror store.
The reader rejects SET to variable 78, which would run past those counters; it is absent from
the authored programs. The original interpreter skips unknown opcode words; the reader rejects
them instead of silently accepting corruption. Contiguous programs consuming the whole stream
are a checked property of this disc, not a claim about all possible versions of this VM.

Opcode 7 constructs an 8-byte message (`0x107c90`), sets its ID (`0x107ca8`), then submits it
through `0x107cc0 -> 0x1075f0`. That path can route immediately (`0x107640`) or queue
(`0x107760`); the queue deduplicates message IDs and uses a 20-slot ring, advancing the oldest
entry when full. Opcode 8 calls `0x1073f0`, which indexes the same message table, reads its text
row, skips row 310, and calls `0x108808`. The exact UI presentation/lifetime of this second action
is not fully traced; the API therefore calls it `TextUi`, not a guessed “play sound” opcode.

### Scheduling and what the rule variables mean

`0x10dc50` initially refreshes up to five state slots per invocation until all 79 have been
visited, then one slot per invocation (`0x10dc6c..0x10dcf0`). The producer dispatch is
`0x10de38`, with table `0x359860`. Once initialization completes, evaluation is allowed only when
the advisor ring's head and tail (`+0xa8`, `+0xa9` of the object at global `0x2aa720`) agree.
One rule is considered per invocation, round-robin through rule-object `+0xcc`.

For that rule, if `nextDay < currentDay`, it sets variable 78 as above and runs the interpreter.
Result 0 sets `nextDay = currentDay + delay`; result 1 sets `lastFailureDay = currentDay`;
result 2 changes neither timestamp. Equality at a delay or elapsed threshold still blocks.
The producer update and queue gate mean this is not “evaluate every condition every frame.”

Only these variable identities are established here:

| Variables | Established interpretation | Evidence / limit |
|---|---|---|
| 4 | Calendar month plus 12 times calendar year, capped at 30,000 | `0x10dfd0..0x10e00c`, getters `0x16b228`, `0x16b230`; not necessarily months since this park opened |
| 56..77 | Snapshots of the 22 event counters | Producer dispatch and the counter storage at object `+0x9e`; individual event meanings not decoded |
| 78 | Days since the rule's last ordinary failed condition, with the width quirk above | `0x10dd48..54` and opcode 9 consumer |

For rule 0, variable 0's producer calls `0x14e538`, which reads global `0x2b72a4`; variable 14's
producer calls `0x103970(0x0f)` at `0x10e0ec..0x10e120`. Their complete gameplay meanings have
**not** been proven. The OPEN_PARK text makes “park closed” and “waiting visitors” tempting
labels, but those labels are not used in the reader or audit.

Rule 0 can nevertheless be decoded and tested exactly:

```text
word  0: GREATER v[4], 3
word  3: EQUAL v[0], 0
word  6: NOT_EQUAL v[14], 0
word  9: TEXT_UI message 0
word 11: MESSAGE message 0
word 13: END
header delay: 60 game days
```

With `v[4]=4, v[0]=0, v[14]=1`, it emits the two actions for `STR_ADVMES_OPEN_PARK` in order.
Changing `v[0]` to 1 blocks it. The audit checks these identities and effects, not just that a
rule has six instructions.

## The actual speech/text/lip table is in the ELF

The loop at `0x106338..0x106438` indexes base `0x2a6ac8`, stride `0x38`, up to message 274;
it loads lip data into these records. More importantly, the playback consumer at
`0x1078d0..0x107bd0` uses **the same base and stride**, reads the text row, selects a variant,
and passes that variant's numeric sound ID to audio playback. This is the join, independently
of the number of sounds or the appearance of symbolic strings nearby.

Each record is 56 bytes:

| Offset | Type | Meaning | Evidence |
|---|---|---|---|
| `+0x00` | `u32` pointer | Symbolic diagnostic key, e.g. `STR_ADVMES_OPEN_PARK` | Pointed-to ELF strings agree with `id.dat` at every nonblank runtime text row; playback uses `+4`, not this pointer |
| `+0x04` | `u16` | Localisation row; **310 means no text action** | Reads/checks at `0x107420` and `0x107930`; 310 is `STR_GIZMO_CPP_BLANK` |
| `+0x06` | `u8` | Number of active variants | Initialization loop `0x106338..0x106418`, playback wrap `0x107b50..70` |
| `+0x07` | `u8` | Mutable selected variant | Playback selects this slot then increments and wraps it at `0x107b50..70`; initialization can randomize it |
| `+0x08` | 4 × 12 bytes | Variant slots (layout below) | Slot multiply by 12 at playback and lip initialization |

Each variant slot:

| Offset | Type | Meaning | Evidence |
|---|---|---|---|
| `+0` | `u16` | Sound ID, **one-based**; zero suppresses audio | Zero check `0x107a24..28`, call `0x111150` at `0x107a50`, bank consumer below |
| `+2` | `u8` | Animation selector | Read `0x107b90..c4`, copied to advisor `+0x1f8`, consumed by switch at `0x107160`; individual categories unknown |
| `+3` | `u8` | Unknown, possibly alignment | Zero in the measured table; no semantic consumer established |
| `+4` | `u32` pointer | Lip filename stem, without extension; may be null | Passed to `0x105e48` at `0x1063f0` |
| `+8` | `u32` pointer | Runtime loaded lip-track pointer, initially zero in ELF | Filled by lip loader, then read by playback at `0x107a78` and attached to advisor `+0x244` |

There are 96 nonblank runtime text rows. The other records use row 310 even when their symbolic
key resembles another row in `id.dat`. Substituting a lookup by symbolic key would invent text
the runtime deliberately does not select. All initial variant bytes are zero; initialization
uses random selection for multiple variants and overrides variant counts for messages 14, 54,
and 69 according to the world (`0x106338..0x106418`). The catalogue exposes the original table;
it does not emulate those world-dependent changes or the later rotation.

### Proving which SDT member a sound ID selects

Playback calls `0x111150` with sound-bank class **11**. Its class-11 branch uses bank descriptor
`0x2abf40` (`0x1111b0`); registration at `0x111c30` obtains the selected language through
`0x155438` and constructs `AUDIO/ADVISOR/%s/` with bank basename `SPCH` (string at `0x359d18`).
The audio virtual dispatch uses vtable `0x3706c8`, installed at `0x23fc68`; its `+0x24` entry
is `0x240970`. Descriptor getters `0x1124e8` / `0x1124f0` expose bank handle and sound ID;
the stream path proceeds through `0x243f50` and `0x24d6f8` to the bank consumer `0x263900`.

Before reading its subtraction as an index conversion, verify its structure: loader
`0x2636a4` checks SDT version `0x3039` (12345), stores the loaded offset-table pointer at
bank-object `+8`, sound count at `+0x0c`, and optional cached records at `+0x20`
(`0x2636bc..0x263818`). Consumer `0x263900` reads **these same members**. At `0x263934` it
subtracts one from the requested sound ID, then indexes the actual file offset table at
`0x263938..48` and cached sound records at `0x263954..68`.

Therefore ID 48 selects SDT array index 47. This is not inferred from “170 looks right” or from
matching a lip name. The audit independently reads the numeric SDT index and checks that the
selected sound's name matches the embedded lip stem, in every speech language.

148 active variant slots have nonzero sounds; they reference 147 distinct sound IDs. Of those
slots, 147 have a matching lip filename; the exception is the retail defect below. The 23 bank
members unreferenced by this table are printed by the audit. That does not prove they are dead
audio: a different caller may select them, and the table is not the only possible audio API user.

### A particular line, all the way through

```text
id.dat[1030] = STR_ADVMES_OPEN_PARK
    <- ELF message 0, text row 1030, variant 0
    <- .ass rule 0's TEXT_UI 0 and MESSAGE 0
    -> sound ID 48 -> SPCHHD.SDT[47] = sp_001.mp2
    -> embedded lip stem sp_001 -> LIPS.WAD/<language>/sp_001.LIP
```

English text, with original line breaks:

```text
People want to come in but
your park is closed. You
should think about opening
up.
```

The same message's other two variants select sound IDs 49 and 50, indices 48 and 49,
`sp_002.mp2` and `sp_003.mp2`, with corresponding embedded lip stems. Text remains row 1030.

| Audio bank / lip directory | Exact marks in `sp_001.LIP` (microseconds) | Row 1030 text, line breaks flattened |
|---|---|---|
| `ENGLISH` / `English` | 2,226,893; 2,812,380; 4,058,820 | People want to come in but your park is closed. You should think about opening up. |
| `FRENCH` / `French` | 2,322,267; 2,899,682; 4,640,090 | Des visiteurs veulent entrer mais le parc est fermé. Vous devriez l'ouvrir. |
| `GERMAN` / `German` | 3,272,743; 3,767,619; 6,915,192 | Die Leute wollen rein, aber dein Vergnügungspark ist geschlossen. Vielleicht solltest du mal drüber nachdenken, ob du ihn nicht öffnen willst. |

This verifies the **authored asset association**. No speech recognition or human transcription
was performed; the audit cannot certify that the waveform actually speaks the displayed words.

### Region and language are separate

All three `id.dat` files have identical identities and order. The audit reads every language
table in every region, including the differences below, then binds every nonzero voice against
all three audio banks and all three text regions. The reader does not invent a language fallback.

| Region | Compiled `.dat` tables, including `id` | Absent language relative to `eur` |
|---|---|---|
| `eur` | `ame,dut,eng,fre,ger,id,ita,jap,spa,swe` | none |
| `usa` | `ame,dut,eng,ger,id,ita,jap,spa,swe` | `fre` |
| `jap` | `dut,eng,fre,ger,id,ita,jap,spa,swe` | `ame` |

The eight plain-text `final*.dat` build masters also present in these directories are not
compiled language tables; the audit checks their filenames separately from the binary readers.

Thus French audio with the `usa` text region is a valid explicit test combination with no French
subtitle table. The sound/lip identity still holds. These directories do not establish how the
retail executable chooses fallback audio for other text languages; that policy remains unknown.

### A real broken association is preserved

Message 268, `STR_ADVMES_WELCOME_MAIN`, variant 0, has text row 310, sound ID 28, and lip stem
literally **`PS2_`**. The sound consumer selects index 27, `PS2_1.mp2`, in all three banks.
`English/PS2_.lip`, `French/PS2_.lip`, and `German/PS2_.lip` are absent. The audit permits only
this exact known discrepancy and prints it; a new missing track or mismatch fails. The viewer
reports the missing file/mismatch rather than replacing it with a guessed stem.

## `.lip`: an active/silent lip-animation gate

Loader `0x105e48` constructs `data\\audio\\advisor\\%s\\%s.lip` (format at `0x359888`), using the
language returned by `0x155438` and the **embedded variant's lip stem**. The equivalent archive
entry is under `/English`, `/French`, or `/German` in `LIPS.WAD`.

Playback initializes advisor lip gate `+0x234` to true and start time `+0x240` at
`0x107a58..64`, then attaches the loaded mark pointer at `+0x244`. Consumer `0x105f30` reads that
same pointer. It clears the gate on `FFFFFFFF`; otherwise it divides the mark by 1,000 and
compares it, **strictly less than**, against elapsed milliseconds from `0x147158 - startTime`.
That getter reads `0x2f07a8`; `0x220c78..0x220ccc` advances this clock by ten times the change
in the underlying tick counter `0x310ca8`, subject to the clock-enable flag at `0x2f07b8`.
At most one mark is consumed per invocation, toggling `+0x234` at `0x105f98..ac`.

Crucially, the caller at `0x106b54..0x106be8` **reads that flag**: inactive selects shape 0;
a transition to active selects shape 1; while active without a transition, a random test can
change the shape to `(previousShape + (random & 3) + 1) % 5`. Setter `0x105fc8` uses the same
advisor object's model pointer at `+0x238` and five part indices at `+0x24c` (resolved by
`0x106d88`), clearing part flag `0x8000` for the selected one and setting it for the other four.
The renderer's consumption of that part flag is outside this investigation; these stores alone
do not prove its rendering semantics. A true gate does not mean a single held
“open mouth” pose. The exact physical shapes have not been rendered/identified in this work.

This proves microseconds and alternating active/silent intervals. The earlier argument that an
odd number of marks ruled out alternating states was wrong: the gate starts active, so an odd
number of toggles ends inactive. The terminator clears it as well. `LipTrack.Playback` preserves
the original polling behavior, including a zero-time mark waiting until elapsed time is greater
than zero and late updates consuming only one mark. It also preserves the signed `DIV` followed
by unsigned comparison (all shipped marks are positive signed integers). It does not implement
the caller's random shape variation. See [lip.md](lip.md) for timing measurements and limits.

## `.dba`: indexed park asset records, not speech

The filename string `Data\\arsdb.dba` at `0x3599c8` leads to loader `0x10f148`. It saves the
loaded buffer to global **`0x2aadfc`** at `0x10f1dc`, with byte length at `0x2aae00`.
Lookup function **`0x10f248` reads that exact global at `0x10f24c`**, scans the directory,
compares the requested key at `0x10f270..74`, and returns `base + directory.offset` at
`0x10f27c..84`. This is a file directory, not parallel columns of speech IDs.

| File position | Type | Meaning | Evidence |
|---|---|---|---|
| `0` | `u32` | Directory entry count, 273 here | Loop bound in `0x10f248` |
| `4` | 273 × 12 bytes | Directory records | Lookup advances by 12 |
| directory `+0` | `u32` | Lookup key | Compared with requested key, `0x10f270..74` |
| directory `+4` | `u32` | Absolute file offset of payload | Added to loaded file base, `0x10f27c..84` |
| directory `+8` | `u32` | Payload byte extent | **Structural evidence**, not read by this lookup: all spans tile exactly to EOF in all three files |
| `0xcd0..0xcdb` | 12 bytes | Zero gap between directory and payloads | Observed and preserved; purpose unknown |
| payload `+0` | raw `u32` | Kind/type word, enum not decoded | Accessor `0x12b540` returns only its low `u16`; the reader retains the full raw word |
| payload `+4` | `u32` | Localised asset-name row | Consumer `0x12b548` loads it at `0x12b554` and calls translation lookup `0x1dfa58` |
| payload `+8..end` | bytes | Undecoded asset-specific data | Retained verbatim, not assigned speech meanings |

The caller chain matters: `0x12ae78` uses the asset-category key mapping at `0x12b1b8`, calls
`0x10f248`, and its returned payload is consumed by `0x1b5b98 -> 0x12b548`. Translation lookup
`0x1dfa58` indexes the loaded language's row pointers (`row * 4` at `0x1dfa80`). Thus the text
field is established by a downstream consumer, not by a pleasing range of integers.

Specific identities in **every region**:

| Directory key | Payload offset | Bytes | Text row | `id.dat` identity / English text |
|---:|---:|---:|---:|---|
| 215 | `0xcdc` | 240 | 197 | `STR_GRAPHICS_JUNGLE_RIDES_BOUNCY_BOUNCY` / Belly Bounce |
| `FFFFFFFF` (first) | `0x8dd0` | 240 | 476 | `STR_GRAPHICS_SPACE_RIDES_ZOB_ZOB` / Tubes of Zob |
| `FFFFFFFF` (second) | `0x9668` | 112 | 288 | `STR_GRAPHICS_SPACE_UPGRADES_ZOB2_ZOB2` |

The duplicate `FFFFFFFF` keys are significant for a reader: the retail lookup returns the
**first** match. A dictionary that keeps the last value changes the asset identity while
preserving the number of distinct keys. The managed reader keeps ordered entries and first-match
lookup. Every payload's `+4` maps to a `STR_GRAPHICS_...` identity, across all three regions.

`arsdb.dba` and `arsjapdb.dba` are byte-identical. `arsusdb.dba` differs in exactly eight bytes:
four records have byte `+0x32` changed from 25 to 15 and byte `+0x36` from 10 to 15. Their keys
are 245 (Jungle `SHOPS_ICECREAM_ICECREAM`), 151 (Hallow `SHOPS_ICES_ICES`), 69 (Fantasy
`SHOPS_ICECREAM_ICECREAM`), and 391 (Space `SHOPS_ICES_ICES`). All four name the Ice Cream Shop.
The audit pins these full identities and exact changes. These are byte observations; neither
the logical field widths nor their gameplay meanings are established here.

There are further consumers (for example `0x12b758` reads `+0x48 + 0x34*tier`, `0x12b840` reads
`+0x4c + 0x34*tier`, and `0x12bc10` reads `+0x50`), but assigning them prices, stock, or timing
from context alone would overstate the result. Those fields remain raw. The PAL executable
references `arsdb.dba`; the selection policy for separate regional executables is not established.

## Port integration and the audit's contract

```sh
dotnet run --project tools/TPW.PS2.AdvisorAudit -- /path/to/disc.bin
dotnet build game/TPWPS2Viewer.csproj
TPW_PS2_DISC=/path/to/disc.bin TPW_PS2_MODE=sounds \
  TPW_PS2_SOUND=ADVISOR/ENGLISH TPW_PS2_PLAY=sp_001 \
  /path/to/godot --path game
```

The sound browser uses the numeric sound association to display message text and lip transition
information. Set `TPW_PS2_TEXT_REGION=eur`, `usa`, or `jap` to choose the text directory; the
selected advisor audio bank determines `eng`, `fre`, or `ger` for the displayed text. Missing
subtitle languages, missing tracks, and mismatched stems remain visible. Audio decoding remains
the existing managed MPEG Layer II path. All added `core/` code is managed C#, with no process
launch or native dependency.

The audit exits **0 on success, 1 on a failed check/exception, 2 for missing arguments**. It
checks structure, identities, and behavior:

* Every rule boundary, opcode, operand range, END, and byte of opcode-stream consumption;
  signed comparisons, strict thresholds, 16-bit wrap, SET's counter mirror, and effects before
  a failed guard. It pins rule 0's specific predicates and OPEN_PARK actions. Mutating header
  runtime scratch bytes must leave decoding unchanged.
* Every regional text table's offsets, NUL boundaries, complete consumption, exact language
  identities, identical `id.dat` order, and each nonblank message's text-row/key identity.
* Every regional DBA directory/payload extent, common asset-name row, duplicate-key behavior,
  Belly Bounce identity, and the exact four USA record differences.
* Every shipped lip track's alignment, final sentinel, and increasing marks; all nonzero
  active voice slots against all speech banks and text regions; the exact OPEN_PARK sound and
  language-specific lip marks. Only message 268's precise retail defect is allowlisted.
* Negative controls swap sounds 47 and 48 **without changing counts**, substitute a different
  valid text row, truncate real files, and supply malformed instructions/tracks. These must be
  detected. `--headers=FILE`, `--opcodes=FILE`, and `--elf=FILE` permit external mutation tests.

Measured result: **PASS**. External runs with first opcode changed to 10 and with message 0's
text row changed from 1030 to valid-but-wrong row 158 both exited **1**, with the corresponding
opcode/identity error. The viewer built successfully; Godot 4.6.2 mono headless sound-browser
smokes decoded and started `sp_001.mp2` with its text/lip metadata in English, French, and German.
The German smoke selected the `jap` text region. A French/`usa` smoke reported the missing French
text while playing the correct sound and loading its lip data. Some forced early Godot exits
reported an ObjectDB leak warning; these checks establish startup/playback, not clean shutdown.
They are not listening tests or rendered-mouth comparisons.

### Explicit remaining unknowns and limits

* Most of the 79 state variables' gameplay meanings, the meanings/call sites of all 22 event
  counters, the day-to-wall-clock rate, and every caller's policy for immediate versus queued
  messages are not decoded. No complete park-driven advisor scheduler is implemented.
* The VM evaluator needs caller-supplied state. It returns ordered actions; it does not execute
  the queue, UI, random initial variant selection, world overrides, or subsequent rotation.
* `TextUi` presentation details, animation-selector categories, variant byte `+3`, and the
  full advisor facial/body rendering path remain unknown. Lip polling is decoded, but no mouth
  mesh is animated by this change.
* Most DBA payload semantics, kind enum, directory-gap purpose, and the two USA byte fields
  remain unknown. DBA stays in the inventory's partially decoded / not-done bucket.
* The 23 sounds absent from this message table may have other callers. The audit does not
  prove reachability, exhaust every possible simulation state, or verify all valid-but-altered
  programs against gameplay. It is not an original-console execution comparison.
* The audit confirms the asset linkage, not spoken content, subtitle translation quality, or
  perceptual lip timing. It reports the known missing lip instead of certifying a perfect disc.
* Only PAL executable addresses are supported; all three regional text/DBA sets and all three
  speech languages **on this disc** were checked. Other executable builds and audio fallback
  policy for the remaining text languages are not established.
