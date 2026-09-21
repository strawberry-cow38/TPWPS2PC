# The RSSE ride scripts

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
