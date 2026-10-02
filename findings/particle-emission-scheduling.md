# LaserRing63 emission scheduling: native-code-derived, not hardware-observed

## Scope / result (2026-10-01)

This slice reads the native birth/live-cap/lifetime ordering **before** treating the port's
`ExpectedTotal()` as an answer. No renderer, density, or production emission behavior changes
are included. `IsolatedParticleSchedule` lives in the audit project only; it is an explicit
parameterized translation, **not execution of the native binary or a full particle simulator**.

For an **admitted, unpaused, normal-mode isolated** LaserRing emitter with successful allocation,
no external lifetime edits, and **no applicable destructive attractor**, the examined native
code permits **one or two successful births**, depending on its first particle's randomized
lifetime and creation-phase alignment. The managed estimate currently returns **five**.
Neither five nor a blanket one-total clamp is a native observation.

The qualification matters: global optional-effect suppression can prevent admission; pool
failure can reduce births; a destructive attractor can shorten particle life and invalidate this
isolated lifetime bound. A runtime/hardware capture is still needed for a realized game sequence.

## Independently read raw record

`PARTICLE.WAD/Tp2.plb`, record63 (`LaserRing`):

| Field | Value |
|---|---:|
| emitter life +0x20 | 20 |
| particle base life +0x78 | 20 |
| burst +0x60 | 1 |
| MaxLive +0x64 | 1 |
| initial countdown +0x66 | 0 |
| rates +0x68..0x6B | -5, -5, -5, -5 |
| pause +0x02 / immortal +0x26 / emission mode +0x0E | 0 / 0 / 0 |
| density skip +0xC0 | 0 |
| die with emitter +0xAA | 0 |
| immediate child / expiry / particle-death effect | -1 / -1 / -1 |

The raw inventory is `/tmp/tpw-emission-raw.log`. No extracted asset is saved.

## Native ordering, checked against selected MIPS

### Spawn and density

`0x18B718..0x18B72C` bypasses density scaling only when template byte +0xC0 is nonzero.
LaserRing does not bypass it. The initial emitter lifetime is captured at +0xD4, the live count
is zeroed, and the particle list is initialized before the burst.

`0x18AFF0` scales **nonzero signed rate bytes**, not positive bytes only. At `0x18B004..0x18B02C`
the code uses `lb`, skips on **zero**, multiplies, arithmetic-shifts right10, clamps the upper
rate to127, replaces a zero result with1, then narrows to a byte. Thus at density400:

```text
(-5 * 400) >> 10 == -2
```

Burst1 and MaxLive1 each scale to zero and receive their minimum-one correction. The effective
isolated parameters are therefore burst1, cap1 and rates -2. The current managed helper returns
negative inputs unchanged; its claim that negatives are never scaled conflicts with this path.
**Bubbles58 is a control, not a collateral fix:** its +0xC0 is1, so its -5 interval really does
bypass the native scaler. Do not apply LaserRing's -2 interval indiscriminately to all records.

The burst loop at `0x18B948..0x18B964` decrements its counter in the call delay slot: burst1
makes exactly **one** call to `0x1888A8`. The allocator does not independently enforce MaxLive;
the initial burst is not clamped by that later scheduling gate.

### Particle lifetime is intrinsically randomized

`0x18893C..0x188998` computes:

```text
initialLife = baseLife + signed_remainder(ASR16(updatedSharedRng), baseLife >> 2)
```

It uses signed `div/mfhi`, without a 0x7FFF mask. Base20 gives a divisor5 and a possible lifetime
**16..24**, not fixed20 or only20..24. The range does not establish a probability distribution
or a specific RNG history. The audit supplies lifetimes explicitly; it does not pretend to
reproduce all the other consumers of the game's shared RNG.

### Executed tick phases

`0x18AF90` calls emitter update (`0x1893F8`), attractor update (`0x189948`), then particle update
(`0x189DA8 -> 0x189E78`) at `0x18AFC4/CC/D4`. A startup-counter guard skips the first enabled
invocation; "tick" below means an actual update, not every invocation of the master function.

The emitter lifetime decrements first; **zero is still emission-eligible**, negative is expired
(`0x18949C..0x1894D4`). The live-count gate is checked **before** countdown processing
(`0x189704..0x189714`), so countdown is **frozen while capped**.

For a negative rate, countdown is decremented and stored as16 bits; positive means no birth,
**zero or negative** attempts one birth and resets it to the positive interval
(`0x189748..0x189764`). Initial countdown0 therefore attempts immediately once capacity opens.

Particles also decrement first: **zero survives**, negative releases live count
(`0x189EC4..0x189F18`). That release occurs after the current emitter cap test, so replacement
cannot use the capacity until the following emitter tick. An expired emitter is freed only
after its live list is already empty (`0x1895A0..0x1895B0`).

The quarter getter `0x188428` selects Q1 at high remaining life, Q2 at phase2, Q3 at phase1,
Q4 at phase0; captured initial life0 selects Q1. Distinct-rate fixtures guard this mapping,
which equal-rate LaserRing alone could not distinguish.

### Creation-phase alignment

The emitter traversal caches its next link before processing (`0x18947C/80`). A new emitter
inserted at the list head during that phase is skipped by that emitter pass, but the subsequent
particle pass reads the current list and can age its burst immediately. Let:

- A=0: no particle update before the first own-emitter tick;
- A=1: one particle update already occurred, as for creation during the preceding emitter phase.

This is not a blanket claim that spawns later during particle processing receive that same-pass
update; traversal caches and phase placement must be considered separately.

## Independent literal chronology

For first randomized particle life L, death is at own tick `L-A+1`; the earliest replacement
is **next** emitter tick `L-A+2`, and only if it is at most20.

| First life L | A=0 replacement tick | A=1 replacement tick |
|---:|---:|---:|
| 16 | 18 | 17 |
| 17 | 19 | 18 |
| 18 | 20 | 19 |
| 19 | none | 20 |
| 20..24 | none | none |

A replacement has minimum life16 and is aged in its birth tick's particle phase. It cannot
release its slot in time for a third birth within the 20 eligible emitter ticks.

For the particular L=20 outcome:

- A=0: at tick20 emitter=0 and particle=0; the cap still blocks. At21 emitter expires first,
  then the particle dies; the empty emitter is freed at22. One birth.
- A=1: the particle dies after the tick20 cap check; the emitter expires and frees at21. One birth.

The audit also contains an explicitly synthetic, deterministic base-life1 counterexample:
birth ticks `[0,3,6,9,12,15,18]` under a one-live gate. That is seven lifetime births, showing
why clamping a total to MaxLive is not generally valid. A positive-rate fixture shows that the
native gate is checked once before the birth loop; it is not a universal per-birth hard cap.

## Executable audit and limits

`ParticleEmissionChecks` currently passes **90 assertions**: raw parameters, literal signed
scaling, complete signed-high lifetime range, both alignment/lifetime endpoint tables,
counterexamples, and actual ELF instruction anchors. The family is integrated into the normal
audit with a90-check floor and semantic witnesses. Python tool suite: **127 PASS**.

```sh
dotnet run --project tools/TPW.PS2.ParkSimAudit -- "$DISC" --particle-emission-only
```

The managed comparison is printed as the **function under test**, not frozen into a regression
requiring the port to remain wrong. No renderer correction is shipped here. The final clean
full core matrix and targeted compiled controls below have completed.
No capture/savestate or actual native instruction execution was performed; code-derived ranges
must not be reported as a measured hardware emission count.

## Compiled controls, restored afterward

Ten compiled audit-reference mutations were rejected: negative rates left unscaled(3 failing
assertions), one-sided life RNG(2), zero-particle premature death(13), zero-emitter ineligibility(6),
reversed quarters(2), ignored creation pre-age(5), countdown advancing while capped(19), per-birth
cap clamp(1), burst clamp(1), and saturated rather than wrapping countdown(1).
Sources were restored byte-for-byte and the audit rebuilt. Logs/JSON:
`/tmp/tpw-emission-mutations-final-run.log`, `/tmp/tpw-emission-mutations.json`.

The first burst-clamp mutation exposed a TEST defect: the spawn receipt reported the requested
burst rather than the actual birth history, and its first oracle only checked that receipt.
The receipt now records actual births; the oracle also checks actual birth ticks and live count.
Rerunning all ten controls rejects the clamp. This was a reference/fixture correction, not a
production behavior change, and the initially surviving mutant is not hidden from the record.

Removing the actual default-family call, compiling, and running JUNGLE/1 leaves raw exit0/PASS;
the matrix rejects it as missing_coverage with particle_emission count0. The call was restored
exactly and Release rebuilt (`/tmp/tpw-emission-omitted-matrix`).

## Final clean gate (2026-10-01)

After all mutations and the omitted-family control were restored, the clean source revision
`e4f0c9f7825d1b7757661b955b142e54c8296168` was gated without further source edits:

- Full eight-park core matrix: all eight record **particle_emission90**, with the existing
  particle_child236 and particle_fitting109 families also intact. JUNGLE/1–2 and FANTASY/1–2
  pass; HALLOW/1–2 retain only the known Thrill Grill failure, and SPACE/1–2 retain only the
  known Moon Buggies failure. Runner exit2 is the explicit known-retail baseline, not eight
  green cases and not a waived new failure.
- Game project build: exit0, zero errors (28 existing warnings).
- Python tool suite: **127 tests, OK**, exit0.

Evidence: `/tmp/tpw-emission-final-core/manifest.json`,
`/tmp/tpw-emission-final-game-build-run.log`, `/tmp/tpw-emission-final-python-run.log`, and
`/tmp/tpw-emission-final-gates.json`. The final queue completed with exit0 after checking each
expected runner exit. This section is a documentation-only addition after that gate.

This is ready as an **audit/read handoff**, not a rendered scheduling fix. The immediate
LaserRing child rollout remains disabled; a production change still needs an explicit
implementation and runtime verification of admission, allocation, RNG and attractor effects.

## ⭐ In the game now (2026-10-02, tinyclaw)

Master: "perform astra's fix on the actual game". The renderer now draws one-shot emitters from this
schedule rather than from `ExpectedTotal`.

- **The schedule moved into core.** `IsolatedParticleSchedule` is in `core/TPW.PS2.Data`, unchanged
  apart from its namespace, so the checks above exercise the code the game runs.
  `ParticleTemplate.Plan(rng)` feeds it the record's density-scaled burst, cap and rates, its own
  `+0x66` countdown (`0x18b5a8` copies the record and never writes it), and a random life per
  birth over the signed range (`RandomLife`: base ± (base>>2)−1, so 16..24 for base 20; uniform,
  because the console's RNG history is not reproduced). Creation alignment is 0, the script/event case.
- **Signed rate scaling.** `DensityScaled` scales a negative RATE byte (`-5 -> -2`); burst and max-live
  stay positive-only. That is the same function's other guard, which cow tools read independently
  in `0x18aff0`. `EffectiveDensity` honours `NoDensityScaling`, so Bubbles keeps its -5. The steady
  (looping) helpers use both, so a continuous effect with a negative rate and no skip flag now
  births 2 to 3x as often (-3 -> -1, -2 -> -1, -10 -> -4, -20 -> -8, -32 -> -13). Steady
  populations now vs before: SmallSmoke 20 (14), Steam 60 (40), Button 15 (8), GoldSparkles 10 (4),
  SmokeTrailR/B/W 100 (50), KeySparkle 8 (4), CongratSparkle 2 (1). LaserLaunch and
  GocartFireballs stay at 1 (capped), Bubbles at 12. Native, not chosen; most visible on steam
  and smoke trails.
- **Drawing it.** A one-shot CPU emitter births its `Amount` evenly over one particle life and cannot
  place a single birth, so `ParticleBirthRuns.For` cuts the plan into runs it can draw: the same
  number of births on each of an evenly spaced series of ticks, spanning at most one life.
  `RideParticles.DrawRuns` puts the first run on the effect's node and parents every later run
  under it, started by a scaled-time timer, so one emit is still one node to orbit, cull and count.
  A plan the cap gated is drawn birth by birth, each with ITS OWN planned life, so a replacement
  appears as its predecessor dies. Delays count from the plan's first birth, not the spawn, which
  draws a no-burst effect's tick-1 births 31 ms earlier than the console. Within a run, one
  tick's births are spread over that tick's gap.
- **What changed visibly** (census of all 67 non-continuous records, 50 plans each): LaserRing 1 or 2
  (was 5), Flames 14..21 (was 80), FirePuff 22 (28), ExhaustPuff 19 (26), MumboPuff 22 (28),
  PinkPop 22 (40), Explode3 7 (8). Uncapped records with positive rates mostly match the old total.

**Checks.** Core audit: `particle_emission` is now **97** (90 + 7). The managed scaler matches the
native byte scaler on all 256 rate bytes. Bubbles is unscaled and LaserRing scaled. Lives are
exactly 16..24. LaserRing's plan gives 1 or 2 over 400 seeds, both seen. Runs rebuild every
one-shot plan exactly (67 records x 40 plans, none longer than a life). Gated plans are one run
per tick. Rendered: `ParticleScheduleAudit`, a standalone matrix case `particle-schedule`, **18**
checks at 640x360 and 1152x648:
- LaserRing x40: 1 or 2 single-particle runs each, both seen. Each life is 16..24 ticks. The
  replacement is parked inside the first particle's life and starts after it.
- FirePuff draws its plan's 22, Flames draws single-particle cap-gated runs within a DERIVED 14..21
  (cap 7: every slot is reborn once at the longest life, none twice at the shortest), and Bubbles is
  unchanged. ⚠ The first version asserted 14..18, the range 50 census plans had shown; the merged
  rerun drew 19. A sampled range is not a bound.
- Clear frees parked runs before their timers fire, and a finished effect retires with its children.
- A plan with NO births (cloned one-tick Destroy75, whose last-quarter rate is 0) draws no parent but
  still spawns its Twinkle83 child, since `0x18b5a8` makes the child request after the burst whatever
  was born. The first version returned early and skipped it; astraclaw's child fixture caught that.
  It also needed one data change: its short-life clone of Destroy75 now has a last-quarter rate of 1.
  Under the native schedule a one-tick Destroy75 births nothing, and the fixture's "creates parent
  and child" relied on the old estimate drawing at least one. It passes 62 again at both sizes.

Six compiled mutations each fail: negative rates unscaled (2 audit checks), skip flag ignored (1),
cap gate off (audit 1 and rendered Flames), renderer back on the estimate (rendered LaserRing), and
parked runs never started (rendered replacement). The rendered fixture also caught a real bug before
commit: copying the first node after later runs had been parented under it duplicated them too,
doubling the tree per run (Flames: about 2^15 nodes, a frozen game).

**Limits.** Isolated schedule only: no destructive attractors, pool exhaustion or optional-effect
suppression. Continuous emitters keep their steady population and do not randomise lives. No
hardware capture. The LaserRing child link stays off as before.

