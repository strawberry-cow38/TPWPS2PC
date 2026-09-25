# TPWPS2PC completion plan

Updated 2026-09-25 by tinyclaw after VoX's review round (1 fable + 3 Opus reviewers, reconciled below).

**Where the port is.** On main, all 8 parks load. Rides run their scripts. Visitors spawn from the bus
and queue, ride, buy, use toilets and leave. Sound, particles, weather and water work. Cow tools is
active on UI and presentation.

The research branch `tinyclaw/native-entrance-flow` (pushed as `origin/astraclaw/native-entrance-flow`)
reproduces the console's guest-entrance lifecycle behind opt-in flags. That covers the booth queues,
the entrance fee, rejected guests walking back to the bus, and guests waiting for their animation
before they walk. None of it has reached default play yet.

The next work moves that branch onto main in slices, then turns to what players see.

Execution log and handoff: [progress.md](progress.md). The checkpoint history that used to live here
(2026-09-23..24) is archived verbatim at the end of progress.md.

## 1. What "complete" means

Deliver a reproducibly buildable, usable PS2-derived PC port which reads a user-supplied disc in place,
exposes the shipped worlds/assets, and runs the supported park/ride/visitor/audio/UI systems with
documented provenance. An asset reader existing is not sufficient: its important consumers, lifecycle,
controls, failure behavior and integration must be exercised.

There are three kinds of outcome. Never conflate them silently:

* **Faithful baseline.** Reproduce supported retail behavior and preserve evidence of retail defects
  and stubs. An unavailable asset, or a handler that returns zero in the executable, is not permission
  to invent data and call it decoded.
* **Labelled adapter.** A port mechanism that stands in for a native one. It may ship as the default
  only when all four of these hold:
  - it names its native counterpart;
  - its divergence from native is bounded and listed;
  - it cannot lose, duplicate or strand a guest;
  - the result is strictly closer to native than what it replaces.
  The labels are adapters such as a BFS in place of the A* planner, or NewlibRand in place of the
  shared console random stream. A labelled adapter is not a release blocker just because it is not
  parity.
* **Explicit extension.** Behavior absent from or stubbed in this build (for example, playable track
  systems) that needs an independently designed implementation. Document it separately from measured
  PS2 parity. Unsupported faithful behavior stays visible until evidence or an identified extension
  supplies it.

Completion requires:
- reproducible reader and runtime gates;
- a usable launcher and error paths;
- stable state and lifecycle handling;
- documented platform requirements;
- manual rendered smoke tests;
- a handoff ledger listing the remaining evidence gaps.

No raw disc, extracted game assets, credentials or proprietary source goes into Git.

## 2. Working agreement and evidence standards

**Branches and commits**
* Work in isolated worktrees. The reference is **origin/main**. The shared checkout `~/tpwps2` was 251
  commits behind it on 2026-09-25, so never read main's state from it.
* Fetch before each package and inspect the actual diff against origin/main. Integrate upstream without
  replacing another agent's work. Push normally, never force; a rejected push means reconcile and
  retest.
* Inspect status immediately before committing and stage explicit paths. Preserve unknown or
  uncommitted work.

**Ownership** (dated 2026-09-25; confirm with cow tools at each package)

| Owner | Area |
|---|---|
| cow tools | The body of Viewer.cs (build/placement UI, gait, HUD); money, economy and ParkFinances; ParkVisitors, VisitorNeeds and GuestWalk; audio; particles; gates and protected paths |
| tinyclaw (astraclaw after Oct 1) | The native entrance research: `core/TPW.PS2.Data/Native*.cs`, the `Viewer.Entrance.cs` and `Viewer.NativeAnimation.cs` partials, `Viewer.Bus.cs`, the tools runners, and findings |

* Touch cow's files only through partial-class seams or with its recorded sign-off.

**Evidence**
* Keep the HALLOW Thrill Grill and SPACE Moon Buggies retail reds visible. Do not weaken assertions,
  invent records or broaden expected failures to get green output.
* READ, CANDIDATE and UNKNOWN stay distinct. Inventory, consumer evidence, tests and live integration
  are separate measures.
* **A multi-case gate must show, from each case's own output, what that case actually loaded.**
  Loop-variable names and constant check counts are not coverage. On 2026-09-25 a runner's
  `--map=WORLD 2` matched nothing, the viewer silently loaded FANTASY-1, and four published
  "eight-park" claims were false for a day.
* A gate is cited only for changes it can observe.
* Landing evidence comes from a committed runner on a committed tree, never a `/tmp`-only script or a
  dirty build.
* Generated captures are not inspected captures. Say which one you have.
* Every new check family gets teeth: plausible wrong implementations must fail **by name**.

## 3. Now

**Package:** none in progress for the agent. Items 2, 7, 8, 9 and 10 are done on the research branch,
and item 13 is half done: the CI workflow is on the branch. See progress.md, "CP2: departures,
stranded guests, soak and CI".

Cow tools acked slice A's dispatcher and slice B at 05:09 UTC (progress.md). They also built
`--idle-scene` on main, and the item 5 film has been made and sent.

Everything left waits on a human:
- cow tools: land slice B (tinyclaw/native-core-slice-b, 71bfbdc, gates clean), then review slice C
  (item 6).
- strawberry: the scope matrix (section 7); the idle choice from the film (item 5); and whether CI
  goes to main (item 13). The default flip (item 11) is decided: no.

**Owner:** tinyclaw, for the evidence and the next package once any of those answers arrive.

**Next review:** when the first answer arrives. Replace this section in place then; do not append.

## 4. Priority queue

Each item lists its stopping condition. The queue is ordered but not rigid: a report from strawberry
preempts it, and a CP4 contradiction reorders it. Human testing is collected at the end (owner request).

1. **Land the readiness package and correct the record.** DONE 2026-09-25. The 24/24 matrix is in
   /tmp/tpw-cp1-final. The false park-2 claims are annotated in place.
2. **Evidence infrastructure.** DONE 2026-09-25 at 783225b. All four gates ran clean in /tmp/tpw-783225b,
   the viewer matrix 24/24 with loaded == requested, and the film at /tmp/tpw-film4 was inspected.
   - Commit `tools/viewer_matrix.py` with unit tests. Its per-case requirements are:
     - PASS matched by an exact scene-prefix regex (a BYPASS witness must not count);
     - a minimum check count;
     - a fresh output directory;
     - a loaded-map assertion read from a canonical `[map] loaded` log line.
   - An unmatched or ambiguous `--map` exits non-zero. This is Viewer.cs, so it needs cow's sign-off.
   - ParkSimAudit takes a terrain argument, and the native families also run on terrain_2.
   - Add `REQUIRED_CHECKS` entries for logical animation.
   - `audit_matrix.py` refuses to call a dirty-tree run landing evidence.
   - Opt-in flags are read once at `_Ready`, not per tick or per frame.
   - The bus boundary witness line prints only when the native flow is off.
   - The readiness film is re-shot with the UI hidden and the guest framed, then inspected.
   - *Stop when:* two regression tests fail with the fixes reverted (the old `--map=JUNGLE 2`, and a
     log whose only PASS is BYPASS), the 8-park viewer matrix passes from one committed command, and
     the film is inspected.
3. **Plan and handoff hygiene.** This rewrite; progress.md keeps a prose HANDOFF block at its top.
   - Send strawberry the scope matrix (section 7) once. Do not wait for the answer.
   - *Stop when:* one Now section exists, every blocker is a gate, a trigger or won't-fix, and the
     question has been sent.
4. **Slice A to main: the standalone native core.**
   - The files: NativeLogicalAnimation, NativeGuestMotion, NativeRoutePool, NativeRouteOutput,
     NativeGuestRoute, NativeActivationSequence and NativeEntranceAcceptance.
   - Their ParkSimAudit categories.
   - No behavior change.
   - *Stop when:* main's audit matrix shows only the two retail reds, the runtime scenes pass, and cow
     tools acknowledges the dispatcher is available.
5. **Guest animation for every guest on main.** Joint work; cow owns the gait.
   - Run hold-last-pose, the weighted logical 11 and the 2106E8 idle picker over all ordinary guests.
     This answers strawberry's "missing idle animations" (2026-09-24).
   - ~~The native rule holds about 93% of standing guests in their last pose.~~ **Wrong, and
     corrected 2026-09-25.** 93 is the weight of one pick. A playing idle clip keeps itself 3 times in
     4 at every loop end (flag 4), and the no-clip variant re-rolls fresh each time. Filmed on cow's
     `--idle-scene` (findings/native-idle-film.md), native plays an idle clip 79% of standing
     guest-ticks and holds the last pose 15%, at the port's 40 ms re-roll cadence. It is still a
     product choice: weighted idles plus the occasional freeze, against `id % 6`.
   - The side-by-side film was sent to strawberry on 2026-09-25. Porting it means taking
     hold-last-pose AND the retention roll together.
   - *Stop when:* strawberry has picked one and it is the default, with teeth.
6. **Slices B and C to main, flags off.**
   - B is the GuestWalk and ParkVisitors seams plus NativeEntranceFlow, reviewed by cow.
   - C is the Viewer partials and hooks, the Bus hooks, and the smokes, with an internal test seam so
     that about 30 reflected private members stop being a silent breakage risk.
   - *Stop when:* main's default gates are unchanged and the opt-in 24-scene matrix passes on main.
7. **Ordinary departures through state 26** (the real content of "pressure population"). DONE on
   the branch, 2026-09-25 at 4a98b64: 32/32 viewer matrix and findings/native-ordinary-departure.md.
   Needs cow's sign-off on the ParkVisitors seam before main.
   - 20C930 routes unhappy or broke guests (happiness < 5 or cash < 100) through 26 → 14 → 2E → 30 →
     9, so they walk out through the booths to the bus instead of vanishing at the gate. WentHome counts
     them and the sticky tally includes them.
   - This needs a Leaving handoff API in ParkVisitors, which is cow's.
   - *Stop when:* a Viewer smoke shows it, and mutations for "vanish at gate" and "skip phase gating"
     fail.
8. **No stranded guests.** A handback adapter for the state 0 and state 5 holds. DONE on the
   branch, 2026-09-25: NativeEntranceFlow.Resume and the disruption smoke. The booth-to-bus corridor
   cannot be bulldozed in this port, so the smoke digs up the park path instead; state 5 is covered
   headless.
   - *Stop when:* a disruption smoke (bulldoze booth→bus mid-departure, delete the staging path) ends
     every guest as WentHome or back in ordinary park state within a fixed tick budget, with no lease
     held at quiescence.
9. **Corridor passability check.** DONE 2026-09-25. findings/native-route-planner.md. Verdict: the sets
   differ in principle (direction links, flag 0x23 ground, queue flag 0x10) and nothing reproduces it
   yet. The planner stays deferred on its missing input (section 6). Read 18C928's kind arms (jump table 364690) for the tile kinds in
   the entrance corridor and request flags 0x21, 0x23 and 1, and compare them with ParkPaths.Open as
   the BFS adapter uses it.
   - *Stop when:* a findings table exists. A mismatch opens the planner package (section 6); a match
     records BFS as an equivalent labelled adapter for the corridor.
10. **Busy-park soak.** All 8 parks with the flow on, for a fixed number of ticks each. DONE on the
    branch, 2026-09-25: findings/native-entrance-soak.md. It uses the executable's LoadsOfKids batch
    rule as a fixture input.
    - *Stop when:* all of these hold: conservation (births = WentHome + discarded + live), the pool
      reads 1000 free at quiescence, the group flip (≥ 11) and the 30-guest veto each occur at least
      once, and there are no errors or leaks.
11. **Default flip (CP5 with strawberry).**
    - Add a `--legacy-entrance` escape. Film legacy against native on the same park.
    - List the residual adapters. Get cow's sign-off for economy, gait and the gate area.
    - This also brings entrance fees into default play: main currently admits every guest free.
    - *Stop when:* strawberry decides. A no is recorded with its trigger.
    - **Decided 2026-09-25 09:49 UTC: NO.** strawberry, in #cowbot: "dont run the native code. its
      built for 25hz, probably poorly written lol, and not built with kb&m support and for modern
      machines." The native controllers stay opt-in research flags and are not flipped on.
      - What the no rests on: tick-shaped rules (per-update clocks) and console-era input. It is
        not about running the binary, which none of this does; that was clarified at the time.
      - Open with strawberry: whether decoded behaviour (for example the ride queues' spots, the
        cap of 7, impatience and close-up) gets folded into the normal port logic, written
        frame-rate independent, or stays reference only.
    - **Revisited 2026-09-25 ~15:57 UTC: YES, for the entrance only.** Asked whether guests arrive by
      bus, buy tickets and enter, then offered "make tickets part of normal play", strawberry: "yes go".
      The entrance flow (booth queues, the fee into park income, turn-back and ride home) is now the
      default; `--legacy-entrance` restores the walk-in. It runs on the park's fixed 40 ms tick like
      the rest of the sim, so the 25 Hz objection above does not add a new frame-rate dependence.
      Ride queues, native guest animation and idle-all stay opt-in. Residual adapters are printed at
      startup (`[entrance] NON-PARITY ADAPTERS`).
12. **Scope items, in strawberry's order** (section 7): the M5 census of non-boarding rides first
    (read-only), then each in-scope feature (save/load, coasters and track rides, staff, research,
    advisor). Each gets a CP1 naming the player action it enables.
    - *Stop, per feature:* a default-path smoke in 8 parks, plus a demo strawberry has seen.
13. **Release.**
    - CI: build plus the disc-free tests on every push. Main has none, and the launcher rebuilds main
      for the owner.
    - Rebuild from a clean checkout at the candidate.
    - The human release checklist.
    - *Stop when:* section 8 holds.

## 5. Milestone status

| Milestone | State | Evidence | What remains |
|---|---|---|---|
| M0 baseline and ledger | done | progress.md "CP0" entries; audit_matrix in tools | nothing |
| M1 validation and rendered smoke | done | audit_matrix.py, runtime_audit.py, viewer_matrix.py (79 Python tests); `--map` fails loudly | CI (queue 13) |
| M2 reader completeness | open by design | findings/format-inventory.md | only when a consumer needs it (CP1) |
| M3 park and visitor lifecycle | mostly done on main | availability, removal, replay, departure recovery | fixed-tick replay across the native flow (queue 8 and 10) |
| M4 effects and audio | done for current scope | the audio-lifecycle and particle findings | listening and visual sign-off are human |
| M5 gameplay and track boundary | not started | "M5" appears nowhere in progress.md | the scope matrix, then queue 12 |
| M6 UI, advisor and text | partial on main (cow) | the shop-info UI, the laptop screens | the fee UI and other controls go to strawberry as scope |
| M7 release | stale | clean checkout at 6004151 only | CI and the queue 13 checklist |
| M8 native guest lifecycle (new) | research branch | progress.md 2148 onward; the native-* findings | queue 4–11, and section 6 |

## 6. Native guest lifecycle: blocker disposition

| Item | State | Gate for default? | If not, trigger |
|---|---|---|---|
| Readiness (191E10 over 2AAD48) | done, opt-in | yes, already met | n/a |
| Ordinary departures via 26 (pressure population) | done on the branch, opt-in | **yes** (queue 7), met there | n/a; the arms after the 26 write are a labelled adapter |
| No stranded guests (state 0/5 holds) | done on the branch as an adapter | **yes** (queue 8), met there | n/a |
| Native A* planner 18C4B8–18DC74 (A*, Manhattan heuristic, random 1-of-4 direction order, two-ended open-list insert, ~99 nodes per call) | decoded in part; BFS adapter in use | no | the queue 9 check found a mismatch IN PRINCIPLE (findings/native-route-planner.md), so the package opens only once its input exists. Trigger: a native tile-kind producer (placement 1E2AD0, link byte 1E70F0) is built, or a routing defect is reproduced at the entrance, or ordinary walkers move onto native routes. Then rerun the item 9 comparison on real tiles |
| Activation-serial origin | unknowable statically | no | **won't fix**: a labelled adapter (≤ 64-tick phase offset) |
| RNG stream and global order | generator readable, order not | no | **won't fix**: NewlibRand / guest stream, labelled |
| Guard staging (second family, 3952AC) | producer readable | no | staff get ported |
| Fee overrides | data rule read: 150 for every ordinary park | no | the UI is a scope question (section 7). There is no save/load, so no save override |
| "Idle ordering" | undefined | no | dropped unless someone defines it with an address |

## 7. Scope matrix (for strawberry: in, out or extension)

The rows: save/load; coasters and track rides; staff (handymen, mechanics, guards); research;
advisor producers; an entrance-fee control; the master job/bug tracker strawberry asked about on
2026-09-20. The scope matrix itself can seed that tracker.

Until strawberry answers, rows stay "pending". Agent-done treats pending rows as open human decisions,
not as unfinished agent work.

## 8. Definition of done

**Agent-done** is where "work through it until done" terminates. All of these must hold at one
origin/main revision:
1. This plan has one Now section, and every blocker in section 6 is a met gate, a trigger or a
   won't-fix.
2. `git log origin/main..tinyclaw/native-entrance-flow` is empty, or the branch is recorded as
   archived.
3. The native flow is the default with `--legacy-entrance` working, or strawberry's "not default" is
   recorded with its trigger. The adapter list printed at startup matches one findings list word for
   word.
4. From a clean checkout, all of these pass:
   - every project builds with 0 errors;
   - `python3 -m unittest discover -s tools -p 'test_*.py'`;
   - `tools/audit_matrix.py` exits 2 with only the two retail reds, every REQUIRED_CHECKS minimum
     met (new families included), and the terrain_2 native families included;
   - `tools/runtime_audit.py` exits 0;
   - `tools/viewer_matrix.py` exits 0 over 8 parks, with loaded == requested for every case;
   - the soak passes.
5. Every "N parks/worlds" claim cites a manifest that records what loaded, and `--map` fails loudly.
6. Cow's sign-off is recorded for every file of theirs that the branch touched.
7. CI is green at that revision.
8. Every scope-matrix row is done with evidence, or out/extension by strawberry's recorded decision, or
   pending on a human, and listed as such in the handoff.
9. The progress.md handoff, in prose, names the revision, gates, reds, adapters and open human
   decisions, and `git status` is clean.

**Release-done** is for the humans: findings/release-checklist.md "Still required", executed on the
target platform through the launcher with the revision recorded. Each failure is fixed or waived by
name by strawberry.

## 9. Checkpoint protocol (slimmed)

* **CP1, bound a package:** five lines in the Now section (scope, owner, gate, stopping condition,
  next trigger). Name the player-visible change, or say that there is none.
* **CP2, review the result:** does a test reject a plausible wrong implementation? Teeth fail by name.
  This is the practice that actually caught defects (the period-16 mutant, the constant-stub retention
  mutant, the `--map` fallback).
* **CP3, stop audit expansion:** two packages in a row with no change visible in default play means
  stop and decide, recorded in Now.
* **CP4, contradictions:** correct READ/CANDIDATE/UNKNOWN openly and preserve the failed evidence.
* **CP5, integration value:** the player flow is reviewed with strawberry before a default changes.
* **CP6, release readiness:** at the end, or after a toolchain change.

A checkpoint replaces the Now section and adds one progress.md entry. The dated checkpoint sections do
not come back into this file.

Keep strawberry informed of outcomes and coordinate directly with cow tools. Escalate real product-scope
choices (the section 7 rows, default flips) to the humans instead of settling them autonomously.
Existing pause or stop instructions take priority.

## 10. Tooling and handoff

* **Managed data and runtime:** `core/TPW.PS2.Data` (.NET 8), separate from the Godot `game/` consumer.
* **Audits:** `tools/TPW.PS2.*Audit`, `tools/audit_matrix.py` (the core matrix), `tools/runtime_audit.py`
  (headless scenes) and `tools/viewer_matrix.py` (rendered normal-startup smokes, from queue 2).
  Read each tool's CLI before running it.
* **Viewer map selection:** `--map="<WORLD>  terrain_<n>"`. There are TWO spaces, and the argument
  matches the map label.
* **Reverse engineering:** `tools/r5900dis.py`, the findings, and the disc/ELF readers. Read the disc
  in place and never commit it or anything extracted from it.
* **Handoff:** the last pushed progress.md HANDOFF block names the branch, revision, gates, reds,
  blockers and next action. Local-only work and uninspected captures are marked as such.
