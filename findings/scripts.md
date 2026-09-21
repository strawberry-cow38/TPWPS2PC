# The RSSE ride scripts

## Across all six archives read

| archive | files | `.rss` | `.rse` | compiled-only |
|---|---:|---:|---:|---:|
| JUNGLE | 2,545 | 90 | 91 | 1 |
| HALLOW | 2,627 | 95 | 96 | 3 |
| SPACE | 2,858 | 87 | 88 | 1 |
| FANTASY | 2,184 | 82 | 84 | 2 |
| LOBBY | 1,034 | 0 | 0 | — |
| DATA | 1,737 | 0 | 0 | — |

**354 sources, 359 compiled, 7 compiled-only.** The source shipped for 98% of every script in the
game. Cracking the bytecode is easy — a known-plaintext attack with 354 matched pairs — and buys
those 7 files; the better argument for doing it is that 354 pairs would catch any misreading of the
language immediately. ⚠ The `#include` headers defining the symbolic constants (`ANIM_Create`,
`EVT_APE_THUMP`) are **not on the disc**, so constants would decode as bare numbers either way.

LOBBY and DATA carry no scripts at all: the hub and the shared data are not script-driven, it is a
per-world mechanism.

⭐ Reading those five archives also put **12,985 further files** through the `FKNL` reader, which was
built against JUNGLE alone. All five parsed clean.

---

90 `.rss` files in `JUNGLE.WAD`, shipped as source. **39 of them are rides** — those declare the
full twelve-variable ride ABI; the other 51 are scenery, shops and sideshows with simpler programs.

## ⭐⭐ The PSX version has no script VM at all

Measured, not assumed. Searched the whole PSX side for any trace of this system:

| searched | `TPW.BIN` (1,065,308 B) | the PSX disc image (515,998,224 B) |
|---|---|---|
| `RSSEQ` | 0 | 0 |
| `rsse` | 0 | 0 |
| `script` / `SCRIPT` / `Script` | 0 | — |
| `.sam` | — | 0 |
| `Ride Description` | — | 0 |

**Zero, everywhere.** PSX ride behaviour is compiled into `TPW.BIN`; PS2 ride behaviour is a program
in a bytecode VM, interpreted at runtime, with the source on the disc beside it.

⚠ **THIS QUALIFIES THE "THE SIMULATION TRANSFERS" CLAIM.** The *ride behaviour layer* is not a port,
it is a rewrite: on PSX it is C in an overlay, on PS2 it is a scripted state machine. Anyone
assuming the PSX reverse-engineering carries straight across for rides will be wrong.

**But not as wrong as it first looks**, because of who owns what — see the ABI below. The host still
decides wear, breakdown, capacity and closure and hands them to the script; the script *sequences*
the ride and reports back. So the park economics and the wear/breakdown rules can still be shared
knowledge. It is the per-ride choreography that is differently built.

## The ABI: twelve variables, in a fixed order, in all 39

```
0 LETMEON     1 LETMEOFF    2 CAPACITY    3 DURATION
4 BREAKSTAT   5 ONRIDE      6 RIDECLOSED  7 BROKEN
8 WORN        9 RUNNING    10 PAD ("unused at present")  11 PARAM
```

Direction, from how the scripts use them:

| host → script | script → host |
|---|---|
| `LETMEON` / `LETMEOFF` (a guest wants on/off), `CAPACITY`, `DURATION`, `BREAKSTAT` (you should break now), `RIDECLOSED`, `WORN`, `PARAM` | `ONRIDE` (rider count), `BROKEN`, `RUNNING` |

The script never writes `CAPACITY`, `BREAKSTAT` or `RIDECLOSED`; it only `TEST`s them. It writes
`ONRIDE`, `BROKEN` and `RUNNING`, which is how the host learns what happened.

⭐ Those are the same quantities the PSX port's ride lifecycle was reverse-engineered into out of the
binary — here with the authors' own names on them. Independent confirmation of that reading.

## The state machine

Labels shared across the corpus, and what they answer to:

| label | in | PSX `AttractionStatus` |
|---|---:|---|
| `.init` | 42 | 1 UnderConstruction |
| `.waitc` | 27 | waiting while closed |
| `.load` | 19 | 10 Loading |
| `.run` | 18 | 2 Running |
| `.unload` | 17 | 11 Unloading |
| `.broke` | 19 | 5 BrokenDown |
| `.closed` | 29 | 3 ClosedByPlayer |

The correspondence is exact, which is the second independent confirmation.

## Concrete numbers

- **Loading timeout: 10,000 ms**, in 31 of the 32 places one is set (`ADD VAR_STARTNOW 10000`,
  commented "Set the timeout" / "reset the timeout"). It is reset every time a guest boards, so it
  measures idleness at the gate rather than total loading time. One script uses 5,000 for its first
  wait and 10,000 thereafter.
- Screaming is driven by the rider count: `STARTSCREAM VAR_ONRIDE 20`, `SINGLESCREAM VAR_ONRIDE -1`.
- `VAR_ONRIDE` is incremented and decremented by the script as guests board and leave (43 decrements
  and 41 increments across the corpus).

⚠ **The PSX port's partial-load rule is a DIFFERENT MECHANISM, and this does not overrule it.** The
port sets a part-full ride going after the queue has been empty for five game DAYS (A+0xF8), read
from the PSX binary. The PS2 script uses a 10-second idle timer from the last boarder. These are two
versions doing the same job differently; nothing here says the PSX reading is wrong, and it should
not be "corrected" on this evidence.

## The language

71 opcodes. A real VM: `#setstack` declares a stack size, `variable` declares named slots, labels are
`.name`, branches are `BRANCH`/`BRANCH_Z`/`BRANCH_NZ`/`BRANCH_NV`/`BRANCH_PV`, calls are
`JSR`/`RETURN`, and `ENDSLICE` yields cooperatively. `CRIT_LOCK`/`CRIT_UNLOCK` bracket the
boarding handshake, which says the host can touch these variables between slices.

Verbs group into: flow, arithmetic (`ADD SUB DIV MOD CMP COPY RAND`), animation (`TRIGANIM`,
`LOOPANIM`, `WAIT4ANIM`, `TRIGANIMSPEED`, `GETANIM_CH`), audio (`EVENT`, `SPAWNSOUND`, `DIPMUSIC`,
`SETREVERB`, the `*SCREAM` family), object lifetime (`SPAWNCHILD`, `KILLOBJ`, `ADDOBJ`, `FADEOBJ`,
`LIMBO`/`UNLIMBO`, `REMOVECHILD`), guest handling (`WALKON`, `WALKOFF`, `WALKGET`, `ADDHEAD`,
`DELHEAD`, `HUSH`) and ride motion (`BOUNCE`, `COAST`, `HOP`, `BUMP`, `TURBO`, `TOUR`).

## `.rse` — the compiled form, and the one orphan

`.rse` beside each `.rss` is the assembled form, magic `RSSEQ`. **Verified rather than assumed from
the filenames**: 90 `.rss` and 91 `.rse`, all 90 pair, and pulling each source's `NAME "..."` string
and searching for it inside the compiled blob gives **82 carrying it verbatim and 0 mismatches**
(the remaining 8 declare no `NAME`).

⭐ **`/Features/gidol/gidol.RSE` has no source.** 90 of the 91 shipped with their `.rss`; that one
did not. It is the only script on the disc that actually requires the bytecode decoder to read.

The smallest pair, whole. Source, comments stripped:

```
.start	NAME		"Small Rock Feature"
	WAITANIM	ANIM_Create	0
.null	ENDSLICE
	BRANCH		null
```

Compiled, 107 bytes:

```
0x00  52 53 53 45 51 0f 01 00      "RSSEQ" + version
0x10  32 00 00 00                  a size or count (0x32)
0x20  "Pad Pad Pad Pad "           header padding, spelled out
0x30  08 00 00 00  25 00 00 80
      00 00 00 10  11 00 00 80     four words
```

**Four source statements, four 32-bit words**, with the strings appended after the code. Two of the
four have the top bit set. That is as far as it goes: the opcode mapping is **not decoded**, and one
sample is not enough to claim opcode 8 is `NAME`. With 90 matched pairs it would not hold out long
if anyone wanted it.


## ⭐⭐⭐ THE RIDE SCRIPTS SHIP AS SOURCE — and they are where particles come from

This file already noted that JUNGLE.WAD carries 90 `.rss` files "shipped as source". Measured
properly, that is an understatement:

| | |
|---|---:|
| `.rss` files with developer comments | **90 / 90** |
| `.rss` files with `#include` directives | **89 / 90** |
| `.rss` files naming a particle effect | 47 |
| `.rse` (the compiled form) | 91 |

They are un-stripped developer source with original comments and include paths like
`\source\game\particle\code\par_lib.h` and `\source\game\soundint\ThemedEvents\JungleEvent.h`.

### ⭐ The particle link is the SCRIPT, not the `.aps`

```
EVENT    OBJ_PTCL  VAR_RAND  P_EFFECT_MumboPuff      ; <Type><Node><Param>
ADDOBJ   OBJ_PTCL  1         P_EFFECT_IncaGodFlame  1
```

An effect is attached to a **node** with `OBJ_PTCL` and a `P_EFFECT_<name>` constant — and those
constants are exactly the names in `Tp2.plb`, supplied by `par_lib.h`. **32 distinct effects** are
referenced across the scripts:

```
AirLeak ApeSnot BigSmokePuff BigSparks BrewUp Bubbles CUSTOM1 CUSTOM2 Explode2 Firework1
Firework2 Flies GoldSparkles GreenFumes IncaGodFlame MudJet MumboPuff Notes SideShowWin
SmallAirLeak Smoke Smoke2 SmokeTrailB SmokeTrailR SmokeTrailW Sparks Spray Steam TorchSmoke
Volcano WaterJet Zzzz
```

⚠⚠ **This means my `+0x24` = particles hypothesis is probably WRONG.** The owner's instinct that
there is a *global particle emitter* was right; my attribution of the wiring to a `.aps` field was
the wrong half. The evidence that convinced me — the carrier list being all lava, water and
vehicles — is explained just as well by those being the rides with scripted effects. `+0x24`'s
meaning is open again, and this is the fifth time today a well-supported reading did not survive
contact with a better source. [[feedback_validate_before_claiming]]

### The script VM's vocabulary

```
COPY 289   BRANCH_NZ 271   TEST 225   BRANCH_Z 222   BRANCH 211   WAIT 206   EVENT 183
ADDOBJ 164 ADD 153         WAITANIM 128  KILLOBJ 72  GETTIME 45   REPAIREFFECT 42
FADEOBJ 41 LOOPANIM 37     CRIT_LOCK 34  COAST 33    TRIGWAITANIM 32  BRANCH_PV 32
CRIT_UNLOCK 29  BUMP 28    STOPSCREAM 24
```

Variables, comparisons, branches, RNG (`RAND`), timers, animation waits (`WAITANIM`, `LOOPANIM`,
`TRIGWAITANIM`), object spawn/kill and critical sections. **The rides are programs**, and for a port
this is the single most valuable thing on the disc — the ride logic in readable form rather than
recovered from a binary.

⚠ The scripts themselves are EA source and stay out of this repo, as everything else does.


## ⭐⭐⭐ `.rse` — the compiled script format, cracked against its own source

Because the disc ships **both** forms, the `.rss`/`.rse` pairs are a Rosetta Stone. `tools/rse.py`
reads it.

```
+0x00  "RSSEQ"
+0x08  u32 / +0x0C u32 / +0x10 u32      (16, 20, 50 in Monkey.rse)
+0x20  "Pad Pad Pad Pad "               literal filler
+0x30  code
```

Code is a stream of **u32 words**. ⭐ **A word with the high bit set is an OPCODE** (low 31 bits =
the opcode number); every other word is an operand of the opcode before it.

### Confirmed opcodes — bytecode decoded beside its own source

`Rides_Monkey_Monkey`, the `.init` of Crazy Ape:

| source line | bytecode |
|---|---|
| `NAME "Ape Ride"` | `OP 0x25` `[0x10000000]` |
| `TRIGANIM ANIM_Create 0 0` | `OP 0x10` `[0, 0, 0]` |
| `WAIT 1700` | `OP 0x2c` `[1700]` |
| `EVENT OBJ_SOUND_LOC_RID -1 EVT_APE_THUMP` | `OP 0x0d` `[3, 0xffff, 0xdc]` |
| `WAIT 500` | `OP 0x2c` `[500]` |
| `EVENT OBJ_SOUND_LOC_RID -1 EVT_APE_THUMP` | `OP 0x0d` `[3, 0xffff, 0xdc]` |
| `WAIT 750` | `OP 0x2c` `[750]` |
| `EVENT OBJ_SOUND_LOC_RID -1 EVT_APE_CRUNCH` | `OP 0x0d` `[3, 0xffff, 0xdb]` |
| `WAIT4ANIM` | `OP 0x2e` `[]` |
| `ENDSLICE` | `OP 0x06` `[]` |

⭐ **The `WAIT` literals are the proof.** Three different delays in one script — 1700, 500, 750 —
each appears in the bytecode as itself. And the two `THUMP` events share id `0xdc` while `CRUNCH`
is `0xdb`: **the same where it should be the same and different where it should differ.** No
alignment or stride guess survives that by accident.

`OBJ_SOUND_LOC_RID` = 3 and `-1` encodes as `0xffff`.

⚠ **Operand counts are inferred from the gap to the next opcode word.** That is correct for a
linear stream and would break if any opcode can take an operand with bit 31 set. Two opcodes
already seen carrying such operands (`OP 0x26 [0x40000006]`, `OP 0x21 [0x20000019]`) suggest
tagged operand *types* rather than raw ints, so this needs establishing before the walker is
trusted past the confirmed set.

### Why this matters for a port

There are **91 `.rse`** on the disc and **90 `.rss`** beside them. The ride behaviour does not have
to be reverse-engineered from a binary at all — it is readable, and now the compiled form can be
checked against it.
