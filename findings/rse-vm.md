# Running RSSE bytecode

2026-09-22. The managed VM runs the actual disc binaries. Crazy Ape, Spider, Bugs TV,
Phantom and Orbiter complete a two-guest loading/running/unloading cycle in the preview,
using their own APS records. This is a bounded ride-script runtime and an explicit demo host,
not the complete park simulation or a claim that every script runs.

## Run it

Requires the owner's disc and .NET 8. No extracted fixtures, executable or game sources are committed.

```sh
dotnet run --project tools/TPW.PS2.RseAudit -- /path/to/disc.bin
# Also print the animation/variable transitions:
dotnet run --project tools/TPW.PS2.RseAudit -- /path/to/disc.bin --trace

dotnet build game/TPWPS2Viewer.csproj
TPW_PS2_DISC=/path/to/disc.bin /path/to/godot-4.6.2-mono --path game res://RideScriptDemo.tscn
```

The normal viewer also has a **Run ride scripts** button. The preview offers all five rides,
pause, restart, speed, orbit and zoom. It opens the ride, offers guest IDs 101 and 102,
acknowledges their unloading, then closes. Capacity is 2 and duration is 1 for this scenario;
those are host inputs, not substitutes for script execution. The script chooses the animations,
counts the riders, resets the loading deadline and changes RUNNING. Sound, particles and guest
models are identified in requests but not rendered. The live display shows script variables,
animation slot/variant and frame.

Optional reproducible capture, in a graphical session:

```sh
TPW_PS2_DISC=/path/to/disc.bin /path/to/godot-4.6.2-mono --path game res://RideScriptDemo.tscn -- --shot /tmp/ape.png --at-ms 19000
```

## Files and boundaries

* `core/TPW.PS2.Data/RseProgram.cs`: strict loader, typed operands, string offsets, variable names,
  instruction boundaries, target validation. Invalid inputs fail rather than executing name bytes.
* `RseMachine.cs`: state and bounded execution. Caller-supplied monotonic milliseconds; no wall
  clock, native dependencies, processes, threads or Godot references.
* `RsePreviewHost.cs`: single-channel APS clock/queue and explicit presentation-effect callbacks.
  `RseRidePreview` contains only the guest/open/close scenario. It never assigns ONRIDE or RUNNING.
* `game/RseModelPresenter.cs`: binds the host's selected record and frame to `AnimatedModel`.
  Both the visible scene and geometry audit use this binding.
* `tools/TPW.PS2.RseAudit`: reads the WADs directly from the disc, aligns sources, executes fixtures,
  and checks values. It returns nonzero on an assertion, decode failure or VM fault.

## Consumer evidence: first prove which structure it reads

All addresses below are virtual addresses in the owner's PAL `ps2.elf`, inspected with
`tools/r5900dis.py`, including the R5900 LQ/SQ instructions. No PC executable is used for
the implemented semantics.

The chain is concrete: loader `0x1bfdf8` checks `0x45535352` (RSSE) at `0x1bfea0`, allocates
the instance at `0x1bfd88`, copies code into instance `+0x18` and variables into `+0x1c`.
The fetcher `0x1bceb8` reads a word from that same `+0x18` array using PC `+0x3c`, checks
against code-word count `+0x50`, then increments PC. Interpreter `0x1bcfa8` calls that fetcher,
requires top byte `0x80` at `0x1bd00c`, bounds the opcode by `0x6a`, and dispatches through
`0x366ec0`. Its arithmetic handlers write the same `+0x1c` variables. This is the bytecode
consumer, not a function identified only by proximity or strings.

## Corrections to the previous format notes

The old opcode-name alignment was useful, but its code boundaries were wrong.
`tools/rse.py` is corrected along with the managed reader.

| File offset | Meaning | Consumer |
|---|---|---|
| `+0x08` | variable count, initially zeroed 32-bit values | `0x1bfebc..0x1bfeec` |
| `+0x0c` | shared stack capacity in words | `0x1bfef0..0x1bff08` |
| `+0x10` | instruction budget per scheduler visit | `0x1bff0c..0x1bff14`, `0x1bfbb4` |
| `+0x14` | limbo capacity, 8-byte entries | `0x1bff18..0x1bff44`; `LIMBOSPACE` calls `0x1bbd08` |
| `+0x18` | bounce capacity, 16-byte entries | `0x1bff48..0x1bff74`; bounce uses `+0x28/+0x64` at `0x1bb5b8` |
| `+0x1c` | walking capacity; allocates twice as many 32-byte entries | `0x1bff78..0x1bffa8`; matching `#setwalk` declarations |
| `+0x20` | 16 literal padding bytes | skipped at `0x1bffac` |
| `+0x30` | **code-word count** | `0x1bffb0..0x1bffbc` |
| `+0x34` | **first instruction; address zero** | copy at `0x1bffc8..0x1bffe8` |

After code is `u32 poolBytes`, then that many bytes of NUL-separated strings. The loader
copies this pool to instance `+0x34` (`0x1bffec..0x1c0014`). A `0x10` operand is a **byte offset**
into that pool: `SPAWNCHILD` adds the operand to `instance+0x34` at `0x1be958..0x1be960`,
then appends that string to the script directory. `SPAWNSOUND` does the same at `0x1be9f0`.
It is not an ordinal into the variable names. NAME stores its pool offset at instance `+0x74`.

Following the pool is one `u32 byteLength + name bytes` record per variable. Length zero
preserves an unnamed slot; nonempty names end in NUL. The PS2 runtime loader does not use
these names. Treating each printable run as a symbol both dropped empty entries and mixed
two different tables. Every managed variable-name list is compared against its own source.
The previous variable-count exception, Hallow Devil, declares `VAR_TEMP` twice; the binary
has one slot for that identifier.

The complete world/path corpus also corrects the **unused opcode** claim: Space Gates uses
`0x19 LOOPANIM_CH`, and Hallow Bug / Space Hoverbot use `0x43 GETVARINCHILD`. Both names and
operands appear in their own `.rss` files. There are 83 disc-observed slots, leaving 23 absent.
Neither of these two newly recognized instructions is implemented by this VM yet.

The audit enumerates both cases of `.rse` across all eight archives, including the mirrors.
It aligns **each mnemonic, operand tag, variable identifier, label target, literal number and
quoted string** in all 352 matched source/binary pairs. Labels are accumulated from source
operand lengths starting at zero, independently of the binary's targets. It strips both `;`
and `//` comments, preserving quoted text. All pairs agree; the earlier instruction-line drift
does not reproduce with this parser. Absent include-file constants are not reconstructed by
guessing. Corpus sizes describe scope; these identities, not the sizes, are the checks.

## Implemented execution semantics

| Operation | Semantics and evidence |
|---|---|
| Numeric operands | tag `0x40` dereferences a variable. Immediates sign-extend **16 bits**, not 24: `0x1bbd18..0x1bbd48`. `0x0000ffff` is -1. |
| COPY / ADD | destination first; ADD is in-place. Both update last-value register `+0x48`: `0x1bd068`, `0x1be374`. |
| SUB | `destination = operand1 - operand2`, updates last value even with a non-variable destination: `0x1bd0d8..0x1bd134`. |
| DIV / MOD | three operands; signed quotient/remainder; zero divisor produces zero. `0x1be448..0x1be51c`. |
| TEST / CMP | TEST loads a variable into last value; CMP computes variable minus operand without writing the variable. `0x1be520..0x1be5b4`. |
| Conditional branches | Z means zero, NZ nonzero, NV strictly negative, PV strictly positive; zero is not positive. `0x1be13c..0x1be208`. |
| JSR / RETURN | return PC after operand is pushed from the high end of the shared stack; RETURN restores it. `0x1be0b0`, `0x1bcf00`, `0x1bcf50`. |
| HUSH / HOP | HUSH pushes a guest ID from the low end; HOP pops it LIFO into the destination and last value. They are stack operations, not guessed guest-animation commands. `0x1be28c..0x1be370`. |
| Scheduling | scheduler loads `+0x94` into remaining budget `+0x98`, decrements once per opcode unless critical, and resets its lock at each visit: `0x1bfbb4..0x1bfbfc`. ENDSLICE clears budget. CRIT_UNLOCK clears **both lock and budget** at `0x1bd058..0x1bd064`. |
| GETTIME / WAIT | supplied clock in milliseconds; WAIT establishes a deadline and revisits itself, yielding until it expires. It does not change last value. `0x1bd138`, `0x1bdf14..0x1bdf9c`. |
| SETTIMER / GETTIMER | separate deadline `+0xc4`: set to now + operand, get `max(0, deadline-now)`. GETTIMER always changes last value, optionally writes a variable: `0x1bf5b8..0x1bf60c`. Thus `GETTIMER 10000` is a flags-only query, not a delay. |
| RAND | inclusive upper bound, signed low-16-bit bound operand; PS2 remainder/absolute-value path at `0x1be050`. Preview uses a seeded injectable nonnegative generator, not the original global RNG stream. |

Malformed targets, unsupported instructions, unavailable host services, stack under/overflow
and runaway critical sections fault with PC and time. Faults are sticky. The managed stack
also prevents guest/call regions colliding instead of reproducing unchecked memory corruption.
The hard safety limit is separate from the authored slice budget. NAME and NOP are supported.

## Animation semantics and the preview approximation

The interpreter calls `0x1abc80` for channel-zero animation requests. That consumer reads
APS section `slot*8`, record `variant*0x1c`, and duration at record `+4`
(`0x1abdc0..0x1abe38`). Milliseconds are `frames * 1000 / 30`; its conversion helper
`0x297b68..0x297bf0` shifts away fractional bits: **truncation**, not rounding.

* TRIGANIM starts/queues playback, stores `max(300, returnedMilliseconds - 300)` in last value
  and an optional third-operand destination, and establishes WAIT4ANIM's deadline.
* WAITANIM starts/queues once, then yields at its own PC until that adjusted deadline.
  It preserves last value. Handler `0x1bd5cc..0x1bd704`.
* TRIGWAITANIM waits for the requested **slot to become current**, not for completion;
  the current-slot check is `0x1bd7c4..0x1bd81c`. WAIT4ANIM subsequently waits for the duration
  deadline (`0x1be01c..0x1be04c`). Conflating these would delay the ride's sound/effect cues.
* LOOPANIM caches slot/variant and does not retrigger an identical request (`0x1bd738`).
  FLUSHANIM clears the pending slot, not current playback (`0x1abc08..0x1abc34`).

The preview implements one pending animation after an unfinished one-shot, and replaces
loops immediately, following `0x1abd04..0x1abd60`. It samples actual APS records at 30 fps.
The host time is deliberately simple: one VM visit every 100ms, playback speed 1. The PS2
scheduler can stagger non-TURBO instances across eight visits (`0x1bfb50..0x1bfb78`); this
preview does not claim the original engine's wall-clock cadence, fractional transition
overshoot or complete animation flag behavior. Host callbacks make that boundary explicit.

There is an awkward edge left intact as a failure: WAITANIM's PS2 handler treats a negative
`returnedMilliseconds - 300` through an unsigned float-conversion path. For a duration under
300ms, this implementation raises an unsupported-path error, instead of silently treating it
as a normal 300ms wait. None of the audited ride paths needs that case.

## Predictions checked against the sources

The fixtures are loaded from their own WAD and source path, never selected only by basename.
Source statements supporting every scenario are checked before execution.

| Source | Host inputs and prediction | Assertion |
|---|---|---|
| Jungle `Rides/Monkey/child.rss` | NAME followed by `ADD VAR_TEMP 1; BRANCH loop`, budget 50. First slice is NAME, 24 ADD/BRANCH pairs, ADD; later slices have 25 pairs. | TEMP after four visits = **25, 50, 75, 100**. |
| Space `Rides/scentro/Jets.rss` | Enable at 100ms; initialize jet=0, increment, wrap 5 to 1, emit at that node, WAIT 600. | At 100/699/700/1300/1900/2500ms: variable **and effect node** = **1/1/2/3/4/1**. Disable/re-enable resets to 1. |
| Jungle `Rides/Spider/Spider.rss` | Fixed 1000ms animation service; loading begins at 800ms, timeout=10800. SUB is deadline minus now; NV is strictly negative. | At 800/10799/10800/10801ms TEMP = **10000/1/0/-1**, RUNNING = **0/0/0/1**. |
| Hallow `features/firework/Firework.rss` | Fixed 1000ms animation service; 700ms Create wait, two JSR subroutines each with 30000ms timer, then WAIT 2000 and WAIT 20000. | STATUS remains **0** through 82600ms and becomes **1** at 82700ms; both returns and flags-only GETTIMER must work. |

Crazy Ape uses real APS: Create is 215 frames, so TRIGANIM/WAIT4ANIM's deadline is 6866ms.
With 100ms visits it resumes at 6900ms, ENDSLICE, then enters WAIT 500 at 7000ms. The script
therefore first boards at 7500ms. These snapshots are asserted as literal expected values:

| Time | ONRIDE | SPACELEFT | STARTNOW | TEMP | COUNT | RUNNING |
|---|---:|---:|---:|---:|---:|---:|
| 7500ms | 1 | 1 | 17500 | 5000 | 0 | 0 |
| 7600ms | 2 | 0 | 17600 | 9900 | 0 | 0 |
| 7700ms | 2 | 0 | 17600 | 10866 | 1 | 1 |

The same two-guest cycle runs on Jungle Spider, Fantasy Bugs TV, Hallow Phantom and Space
Orbiter. For each, assertions require ONRIDE **0→1→2→1→0**, SPACELEFT **2→1→0**, COUNT
**1→0**, clearing LETMEON, exact deadline resets where authored, and HOP returning **102 then
101** before each host acknowledgment. Each ride's complete ordered animation slot/variant
sequence is compared with the sequence read from its source. With this host, cycles complete
at 35900/31000/13300/17500/15200ms respectively (Ape first). These are preview observations,
not independently measured PS2 timings.

`game/tests/RseAnimationAudit.tscn` checks the actual presenter. Ape's Start is 335 frames,
so Main variant 1 starts at **18866ms** in this host. At 19000 and 19500ms the presenter must
select Main:1 at **4.02 and 19.02 frames**. Its visible, transformed surface vertices must equal
an explicitly selected APS reference at those frames and must differ between the two times.
This checks that execution reaches rendered geometry, not just a logged animation request.
It uses the existing APS evaluator as the reference and does not independently validate that
evaluator against PS2 framebuffer pixels. The graphical scene was also captured and inspected.

## Teeth: deliberate execution failures

```sh
# Managed tests, then optional Godot binding tests, with expected-failure controls:
tools/TPW.PS2.RseAudit/teeth.sh /path/to/disc.bin /path/to/godot-4.6.2-mono
```

The script requires both the expected exit code and the specific failure message. Actual results:

| Mutation | Failure | Exit |
|---|---|---:|
| In-memory child ADD → COPY, identical instruction size/budget | tick 1 TEMP: expected **25**, got **1** | 1 |
| Swap Spider SUB's two inputs, preserving arity/indices/instruction totals | at 800ms TEMP: expected **10000**, got **-10000** | 1 |
| Force the presenter's rendered geometry to frame zero | at 19000ms vertices differ from Main:1 at **4.02** | 2 |

Unmodified managed and Godot audits exit 0. Mutation switches are confined to audit tools;
no production VM fault-injection switches. Separate guards reject a branch into an operand,
a missing host and the real Space Gates LOOPANIM_CH instruction. No game data is modified.

## Not established / not implemented

* No PS2 execution trace or screenshot comparison establishes cycle-accurate engine parity.
  The caller controls scheduling and speed; eight-way engine staggering and TURBO are absent.
* The original global RNG stream, non-unit speed, clock wraparound, animation queue overshoot,
  channel flags and sub-300ms WAITANIM edge behavior are not reproduced.
* The renderer uses the existing per-record APS evaluator. Cross-record pose/visibility
  carryover and blending are not reconstructed here; for example, construction debris can
  remain visible in a later record. Successful geometry identity tests are scoped to this
  renderer, not a claim of complete visual fidelity.
* Guest IDs/stack handshakes work; guest AI, paths, seat/head attachment rendering, particles,
  sound, screams, reverb and repair effects are not implemented by the preview. Presentation
  requests are surfaced through `IRseHost.TryEffect`/`EffectRequested`, explicitly acknowledged
  by `RsePreviewHost`; a missing or rejecting host faults. Economic/wear/breakdown decisions
  remain host inputs. Breakdown/repair paths are not covered by the demonstrated cycles.
* **Every one of the 359 `.rse` scripts on this disc now executes**, across all four worlds.
  No observed instruction is left deliberately failing. The 23 opcode slots that appear in no
  shipped script are still refused -- their borrowed PC names alone do not validate PS2 semantics.

## The ones this build answers with nothing

Several implemented opcodes do nothing on the console, and are modelled that way rather than
being given behaviour that would look livelier:

| Opcode | What `SLES_500.32` does |
|---|---|
| `TOUR` `BUMP` `COAST` | A selector and one argument; every branch discards it or writes **zero**. `0x1c1260`, `0x1c1370`, `0x1c14e0` |
| `HOUR` `MIN` `SEC` | Share one case with `0x61`..`0x63`; the body is `LastValue = 0` and a store |
| `SPARK` | Stores two node ids, resolves their positions into locals and discards them |
| `SETOBJPARAM` | Walks the instance's own ADDOBJ list at `+0xb0`, which this host does not build |

⚠ Six jungle rides -- Chac Atak, Gorilla Thrilla, Temple Of Gloom (COAST), Dino Karts, Splish
Splash (BUMP) and Jurassic Tours (TOUR) -- take one guest into `VAR_LETMEON` and then wait
forever on one of these. That is this executable's behaviour, not a gap in the port.

## A missing animation is not an error

`0x1abc80` resolves a record through `0x1ab518` and, when that lookup returns nothing, adds
**1000** to the duration it reports and plays nothing; the other arm of the same function falls
back to the same 1000 when playback reports zero. So a script asking for an animation its model
does not carry costs one second and carries on.

⚠ This was the single biggest thing stopping scripts on the disc. Every `/features/` bin,
speaker, fountain, camera, tower and portaloo opens with the standard `WAITANIM 0 0` prologue on
a model with no Create record at all -- 38 across the four worlds -- and several rides ask for a
Load or Unload slot they do not carry (`whirli` asks 3:0, 7:0 and 7:1 of an APS holding 0, 2, 4,
5, 6, 9 and 10). A rock with no build animation is not a broken rock.

## Limbo, which is what a shop is

The limbo table is instance `+0x24`, `+0x58` entries of 8 bytes (the declared `#setlimbo`, not
doubled), with `+0x60` holding how many are in use:

| offset | field |
|--------|-------|
| `+0x00` | guest id, 0 when free |
| `+0x04` | when they are due out: `now + seconds * 1000` |

`LIMBO(guest, seconds)` takes the first free slot and returns 1, or 0 when full (`0x1bbb30`).
`UNLIMBO` (`0x1bbbe8`) returns the first guest whose time is up; `FORCEUNLIMBO` (`0x1bbc80`) the
first guest at all, and it does NOTHING without a destination variable because `0x1bea14` checks
the tag before calling the helper. `INLIMBO` is `+0x60` -- how many are inside, not whether a
particular guest is -- and `LIMBOSPACE` is `+0x58 - +0x60`.

⭐ Both ends call `0x1fa2c8` to hide the guest going in and show them coming out. That is the
whole illusion: somebody walks into a burger stand and stops existing for five seconds.

Every shop and sideshow on this disc -- balloon, costume, gift, steak, Super Bog, arcade -- was
blocked on this family and nothing else.

`GETANIM_CH(dest, channel)` fills the result from `0x1acaf8` and replaces it with **-1** when that
call reports bit 2. ⚠ Only the SIGN is established: every script using it tests `BRANCH_PV` and
`BRANCH_Z` and acts only on "finished", never on the magnitude.

## Animation channels

TRIGANIM_CH's fourth operand is a channel. `0x1bda84` is TRIGANIM's body with that channel passed
to `0x1abc80`, and the same `duration - 300` floored at 300. Inca Totem asks for `5 1 0 1`,
`5 4 0 2` and `5 7 0 3` -- three variants of one slot playing at once, one per totem head.

⚠ `RsePreviewHost` keeps every channel and reports honest durations, and exposes them through
`Channels`. It does NOT draw them: a renderer that follows only `Current` shows one third of that
ride and will look finished while it is not.

## Children, and the walk table

`SPAWNCHILD` (`0x1be91c`) is **not a presentation request**. Its single operand is tag `0x10`,
a string, and the handler copies the script's own directory from instance `+0x38`, appends that
string and loads the result as a second program. The twelve spawns in `JRSE.WAD` all name a real
sibling: `Coaster1.RSE` spawns **its own** `EventMap.rse`, one of seven files with that name in
the archive, which is why the lookup cannot be by name alone.

| instance | meaning | evidence |
|----------|---------|----------|
| `+0x08` | this instance's handle, copied into the child's `+0x10` | `0x1be968..0x1be988` |
| `+0x0c` | the child. **One slot**; SPAWNCHILD overwrites it | `0x1be964`, `REMOVECHILD 0x1bea14` |
| `+0x10` | the parent | `SETVARINPARENT 0x1bea64`, `GETVARINPARENT 0x1beaa4` |
| `+0x14` | the sound child; no parent link is written back | `0x1be9f8` |
| `+0x38` | the script's directory, used to build a child's path | `0x1be950` |
| `+0xc8` | animation context, **copied to the child** -- they drive one model | `0x1be98c` |

Opcode `0x44` has no name in any source list. Its handler shares `LAB_001bead4` with
SETVARINCHILD -- it **writes** -- and differs only in taking `+0x10` instead of `+0x0c`, so it is
recorded here as `SETVARINPARENT`. `0x45` is the matching read and already carried its name.

The walk table is instance `+0x2c`, `+0x7c` entries of 32 bytes (the loader at `0x1bff78`
allocates **twice** the declared `#setwalk` capacity):

| offset | field |
|--------|-------|
| `+0x00` `+0x02` | nodes A, B -- the route in |
| `+0x04` `+0x06` | nodes C, D -- the route out, stashed by WALKON for WALKOFF |
| `+0x08` `+0x0c` | start and end milliseconds |
| `+0x10` | guest id |
| `+0x14` | bearing, `0x1b9138`'s atan2(dz, dx) in twelfths of a circle |
| `+0x16` | kind; `4` puts the ride-side node in model space `0x80` instead of park space `0x800` |
| `+0x18` | state |
| `+0x1a` | WALKON's seventh operand |

⭐⭐ **State 1 does not become state 4.** The ticker `0x1bade8` runs 1 (walking in) → **2**
(aboard, re-placed every tick so a moving ride carries its riders); only `WALKOFF` (`0x1bb3f0`)
starts 3 (walking out), which finishes into 4, and `WALKGET` (`0x1bb338`) harvests a 4 and frees
the slot. Reading WALKGET on its own suggests a guest is collectable the moment they arrive; a
host built on that would hand every rider back without them ever riding.

Leg duration is `(int)distance * 1000` ms with a 100 ms floor -- **the cast runs before the
scale**, so a 2.7 unit walk is timed at 2000 ms. `RseMachine.WalksAreTimed` reports false when
the host could not place the nodes, in which case every leg ran at that floor.

`BOUNCING` reads instance `+0x6c` (`0x1bb880`) and `BOUNCESETBASE` writes `+0x6e`; the bounce
ticker `0x1bb888` uses `+0x6e` as a rest height under a sine table. `BOUNCING` is implemented and
`BOUNCE`/`UNBOUNCE` are not, so it answers truthfully only for a script that never bounced.
* The remaining 23 absent opcode slots are not accepted as executable instructions. Their
  borrowed PC names alone do not validate PS2 semantics. Version `0x10f51` is accepted; other
  RSSE versions are not established.
* DIV/MOD and some presentation opcodes have consumer evidence but are not independently
  exercised by a source-derived arithmetic scenario here. Corpus alignment validates their
  encoded identity, not their complete execution behavior.

## EVENT and ADDOBJ: what the scripts are asking the world for

`EVENT a b c` and `ADDOBJ a b c d` are the **same call** -- `0x1bd3e4`/`0x1bd16c` both reach
`0x1bbf28`, and EVENT simply passes 1000 where ADDOBJ passes its fourth operand. The first
operand is a **kind** and it selects between two unrelated subsystems:

| kind | what the handler does |
|------|-----------------------|
| 1 | node `b` resolved in space `0x100`, then `0x18b5a8(c, x*10240, y*10240, z*10240)` |
| 2 | node `b`'s position **and** direction, then `0x18b0f8(c, pos, dir)` |
| 3 | node `b` in space `0x200` scaled by 256, then `0x111428(sys, 0xc, c, pos, out, 0)` |

⭐ **Kinds 1 and 2 are particles, and the data proves it.** `0x18b5a8` bounds its id at `0x69`
(105) and copies a per-type template; `/DATA/PARTICLE.WAD` holds `Tp2.plb`, whose header declares
**105 records of 320 bytes** with a name at record `+0x118`. Reading those names against what the
jungle park actually asks for lands on itself: the ride named **Mumbo** asks for `EVENT 1 3 31`
and effect **31 is `MumboPuff`**. Nobody chose that mapping; the two files agree.

Kind 3 takes ids past 105 (131, 200, 203, 254), returns a handle the script keeps and later
KILLOBJs or FADEOBJs, and goes to a different manager entirely -- so it is not a particle.

⚠ If the id has bit `0x8000` set it is looked up per-instance first, through
`0x1fa8c0(animationContext, id & 0x7fff)` -- the ride's own table. That is what `EventMap.rse`
is for.

⚠⚠ The 105 templates live at `0x2ce508`, which reads as **all zeros in the executable image**:
they are filled from `Tp2.plb` at load. Read the file, never the image.

`tools/TPW.PS2.ParkSimAudit` prints every request the park makes and does not render: 387 EVENT,
46 ADDOBJ, 14 KILLOBJ, 11 FADEOBJ, 4 SETREVERB and 2 SINGLESCREAM in sixty seconds of jungle,
across just 44 distinct argument sets.

## A script's "node" is a named attachment point, found by id AND kind

`EVENT`, `ADDOBJ`, `WALKON` and `SPARK` all name a node, and none of them means an index into the
model's node table. `0x1b9388(vm, outPos, outDir, node, SPACE)` passes both the node number and
the space to `0x1f1f78`, which is a **search**:

```
table  = model + 0x74        count = u16 at model + 0x36        stride = 20
for each entry:  if (entry.id == node && (entry.flags & mask) != 0) return its INDEX
otherwise -1, and 0x1b9388 returns 0 -- the instruction does nothing at all
```

⚠ The mask defaults: `if ((space & 0x3da1f83) == 0) mask = 0x3da1f82`.

⭐⭐ **They are the ride's named fittings.** `monkey.mps` has 24 of them, and the strings beside
the table are `Head1`, `Head02`..`Head17`, `nose1`, `nose03`..`nose07`, `destroy` and `Dummy01`
-- the guest head slots that ADDHEAD and DELHEAD fill, and the ape's noses. Crazy Ape's
`EVENT 2 1 22` and `EVENT 2 2 22` are `ApeSnot` **out of its nose**, which is what the effect's
name said all along.

The four entries whose flags carry `0x100` are ids 1, 5, 9 and 13; every other entry is
`0x000400f1`. So the space argument picks a KIND of fitting and the node number picks which one.

⭐⭐ THE ENTRY'S FIRST POINTER SAYS WHICH NODE IT SITS ON. It opens on `u16, u16, float x, y, z`,
and the second `u16` is a node index. Reading Crazy Ape that way is coherent all the way through
its script: `EVENT 1 5 92` and `1 6 92` (BigSmokePuff) and `EVENT 2 5 1` / `2 6 30` (Sparks,
BigSparks) all land on `m_crate`, and `ADDOBJ 2 3 16` / `2 4 16` (Smoke2, as it breaks) land on
`m_arm`. Smoke and sparks off the crate the ape bursts out of; smoke off its arms when it breaks.
`Model.Fittings` and `Model.FindFitting` implement the search, and the ride preview places its
particles with them -- node 1's position MOVES between bursts, because the part it sits on is
animated.

⚠ The three floats are still not understood (see below) and are carried, not used. So a burst is
on the right PART and not at the right spot on it, and `IRseHost.TryNodePosition` still answers
"I do not know" rather than turning a node into a plausible number -- walk durations stay
honestly at their floor.

### What the fitting record holds, and where the reading stops

Each entry's first pointer opens on `u16, u16, float, float, float`, and the second `u16` reads
as a NODE INDEX that resolves to real names in `monkey.mps`: `m_arm1`, `m_arm`, `m_crate`,
`m_body`, `m_boxes`, `m_shards` and `Head09`.

⭐ So the Head and nose names are not a separate list at all -- they are **helper nodes in the
model's own node table**, which means a fitting can be found by name through the node chain that
is already read. That is a usable fact on its own.

⭐⭐ THE THREE FLOATS ARE A POSITION, NORMALISED INSIDE THE MESH'S OWN BOUNDS (`+0x70` min,
`+0x80` max). Crazy Ape's five `m_crate` seats then come out as a ROW across the crate -- x 1.67,
1.72, 2.00, 2.19, 2.55 -- at one depth; two seats on each arm; the rest up the body; all sixteen
distinct and all inside the ride's four-by-four plot.

⚠⚠ THIS NOTE PREVIOUSLY SAID THEY WERE NOT A POSITION, AND THAT WAS WRONG TWICE. The test behind
it read BoundsMax from `+0x7c` instead of `+0x80` and produced nonsense; and the reason given --
that the third component is 0.102..0.110 on every seat, so they would all lie on one plane -- is
exactly what a rank of seats at one height looks like. The objection was the evidence. It is left
written down because the wrong version was reasoned carefully and still wrong, and because what
broke the deadlock was not more staring: it was a renderer reporting that three riders were
sitting on one point.

⚠ It is a reading that fits, not a consumer that was walked. `0x1f2978` takes the position from a
runtime matrix and nobody has followed what builds it. Four independent properties hold -- the
crate seats are monotonic, nothing stacks, everything lands inside the footprint, the heights form
a narrow band -- and the wrong frame broke all four.
