# TPWPS2PC completion plan

Updated: 2026-09-23. Working baseline: upstream `0db3678` plus the tested ride-availability fix.
Execution/results/handoff: [progress.md](progress.md). This is a completion roadmap, not a claim that the port is finished.

## What “complete” means

Deliver a reproducibly buildable, usable PS2-derived PC port which reads a user-supplied disc in place, exposes the shipped worlds/assets, and runs the supported park/ride/visitor/audio/UI systems with documented provenance. An asset reader existing is not sufficient: its important consumers, lifecycle, controls, failure behavior and integration must be exercised.

There are two distinct outcomes, which must never be silently conflated:

* **Faithful baseline:** reproduce supported retail behavior and preserve evidence of retail defects/stubs. An unavailable asset or a handler returning zero in the executable is not permission to invent data and call it decoded.
* **Explicit extensions:** playable track systems or other behavior absent/stubbed in this build may require an independently designed implementation. Keep such work separately documented and distinguish it from measured PS2 parity. Unsupported faithful behavior remains visible until evidence or an explicitly identified extension supplies it.

Completion requires reproducible reader/runtime gates, a usable launcher and error paths, stable state/lifecycle handling, documented platform requirements, manual rendered smoke tests, and a handoff ledger listing remaining evidence gaps. No raw disc, extracted game assets, credentials or proprietary source goes into Git.

## Working agreement and evidence standards

* Work in isolated worktrees. Fetch before each milestone, inspect the actual diff against remote main, and integrate upstream without replacing another agent's work. Push normally, never force. A rejected push means reconcile and retest.
* Inspect status immediately before committing and stage explicit paths. Preserve unknown/uncommitted work. Never sweep up the active shared tree.
* Cow tools is actively changing `game/Viewer.cs`, placement/`RideCatalogue.cs`, and particles. Do not edit these concurrently. Announce narrow file ownership before a push; prefer separate audit/tool modules and isolated core changes until coordination says otherwise.
* Retain the documented HALLOW Thrill Grill missing-animation and SPACE Moon Buggies missing-fitting failures. Keep raw audit failures visible; do not weaken assertions, invent records or broaden expected-failure exceptions merely to obtain green output.
* Inventory coverage, interpreted byte coverage, consumer evidence, correctness tests and live integration are separate measures. Maintain READ / CANDIDATE / UNKNOWN distinctions; data-shape guesses are not consumer proofs.
* Every milestone includes the change, a regression or reproducible audit, relevant cross-world checks, and a progress entry with actual results. Record failed attempts and blockers, not only successes.
* Do not claim screenshots were visually reviewed when they were only generated or pixel-checked. Runtime logs, automated scene assertions, rendered captures and human/visual inspection are distinct evidence.

## M0 — Establish the executable baseline and collaboration ledger

Deliverables:
1. Commit this plan and `progress.md`; incorporate the isolated ride-availability fix without losing current upstream work.
2. Enumerate existing audit commands and distinguish known retail findings from new regressions.
3. Make repeatable evidence capture straightforward: command, revision, world, raw exit status, exact failures, and local log/capture references.

Exit gate: the plan/progress are on remote main; the eligibility fix has before/after evidence; all four park audits are run against the integrated revision. JUNGLE/FANTASY are expected to pass; HALLOW/SPACE retain their precise known failures unless new independent evidence changes the findings.

## M1 — Repeatable validation and actual rendered smoke coverage

First work package, deliberately outside the active viewer/core files:
* Add an audit runner which builds selected projects, runs reproducible world matrices, retains raw outputs and writes a structured result manifest.
* Start with ParkSimAudit, then expand to reader/VM/visitor audits after recording their real baselines. An expected retail finding is a distinct outcome, not a pass. Unknown failures, crashes, timeouts, missing checks, or extra failures must fail classification.
* Test the runner's own classification with negative controls: an extra error containing a familiar ride name must not be accepted; a truncated/crashed audit must not look successful.
* Build and run the actual Godot viewer in an isolated checkout. Use the existing `--guest-test`, `--shot`, `--walk-film`, `--ride-film`, `--place-test`, `--path-test` and `--ghost-test` controls where applicable. Record the renderer/engine, world, command, logs and output dimensions; inspect actual captures when image-viewing capability is available.
* Exercise normal launch, missing/invalid disc, a world change and an exit. Do not confuse a headless data-only run with rendered verification.

Exit gate: a successor can reproduce the baseline without guessing commands; at least one real rendered park smoke is recorded, and the distinction between inspected and merely generated captures is explicit.

## M2 — Reader completeness and evidence debt

* Reconcile `findings/format-inventory.md` with current source and upstream changes. ParticleTemplate/consumer findings recently superseded earlier particle-field guesses; do not repeat stale claims.
* For DBA, finish only semantics supported by executable consumers and cross-region evidence: specialised track/tour fields, upgrade selector, unnamed flags/ranges. Add identity/field mutation tests for every interpretation.
* Inventory uninterpreted sound-map records, fitting fields/pointers, GIN payloads and any retained raw ranges. Measure bytes accounted for, not merely tags recognised; validate every claim against the current readers before opening work.
* Revalidate model/APS/skinning uncertainties against independent controls. Separate reconstruction (e.g. inferred bind transforms) from direct decoding. If required source data is unavailable, record the boundary rather than fabricate a “fix”.

Exit gate: each format has an honest coverage/evidence table, mutation/round-trip or independent-reference checks appropriate to it, and explicit remaining ranges. A format is not declared complete because a constructor accepts the file.

## M3 — Park/visitor lifecycle correctness

* Finish ride availability checks (first patch already tested): closed/broken rides cannot be destinations or receive newly arriving guests; recovery works; optional flags remain safe.
* Audit ride removal, route invalidation, closure after queueing, cancellation and ownership transfer. Preserve guest identity and account for each guest in exactly one appropriate lifecycle state. Do not silently delete people.
* Represent waiting/queued guests deliberately rather than letting them disappear between the walking and seated sets; coordinate rendering changes with the viewer owner.
* Add tests for multiple rides/guests, blocked routes, removal during walking/queueing/riding, and deterministic fixed-tick replay. Keep the renderer a consumer of engine-free simulation state.

Exit gate: guest conservation and lifecycle invariants hold through disruptions; a manual/rendered sequence demonstrates arrival, waiting, boarding, riding, exit and recovery without duplicate or lost bodies.

## M4 — Effects and audio integration

* Coordinate with the active particle implementation before touching its files. Evaluate the current consumer-backed emitter implementation, not the older generic-burst code from the initial sweep.
* Check persistent object handles, ADDOBJ/EVENT distinctions, KILLOBJ/FADEOBJ/SETOBJPARAM, child effects and cleanup across world/ride removal.
* Connect or explicitly account for decoded `.eng` layers and sound event/clip selection. Distinguish proven clip lookup from inferred variant thresholds/attenuation.
* Investigate unresolved executable consumers/vtable paths only with provenance and an independent check. Do not make a static table “used” by guessing a mapping.

Exit gate: representative effects and sounds follow script lifecycle correctly, clean up on removal, and have recorded runtime/rendered/audio evidence; unresolved paths remain named.

## M5 — Gameplay systems and track capability boundary

* Establish what the shipped PS2 executable actually supplies for TOUR/BUMP/COAST and the ride classes they affect. Re-census the reported 27/86 non-boarding rides on the current tree rather than assuming all are eligibility bugs.
* Define the track/vehicle/path representation, construction constraints, movement/boarding lifecycle, and persistence tests before implementing it. Keep any behavior extending a retail stub explicitly identified as an extension, not reverse-engineered fact.
* Build persistent visitor needs/economy/happiness/queue-choice state and park-management services from validated game data where available. Distinguish original algorithms from port policy.
* Add save/load only with an explicit state schema, stable identities, round-trip tests, versioning and corruption handling; confirm existing support before designing a replacement.

Exit gate: the selected playable feature set is functional and reproducible; faithful unsupported cases and optional extensions are clearly separated. No invented implementation is reported as console parity.

## M6 — UI, advisor, text and accessibility

* Integrate original font/Kanji/text resources where appropriate; decoded tables require actual rendering/lookup consumers and non-Latin test cases.
* Connect advisor rules to game-state producers, scheduling, speech/subtitles and lip transitions. The existing sound-browser catalogue is not automatic park advice.
* Implement remaining menu/interaction paths from shipped layout evidence where available. Exercise keyboard/mouse focus, scaling, cancellation and informative error states.
* Verify every visible action has a real path (including launcher Retry/update failures), without silently launching stale binaries after failed builds.

Exit gate: controls and feedback form a usable workflow instead of disconnected asset demos, and representative locale/advisor/error-path scenarios are demonstrated.

## M7 — Release and long-run validation

* Build from a clean checkout on supported target platforms; record exact toolchain/engine requirements and dependency/license notices.
* Exercise launcher install/update/relaunch, offline and failure recovery, user-selected disc paths, and no bundled proprietary data.
* Run cross-world regression gates, long simulations, repeated load/unload, resource-leak checks and fixed-tick comparisons. Preserve known retail failures as evidence.
* Produce a release/handoff checklist with feature status, evidence links/commands, known limitations and the exact revision tested. Do not label an untested platform supported.

Exit gate: documented installation and gameplay workflows work on the declared targets, outstanding exceptions are explicit, and another developer can reproduce all release evidence.

## Tooling already available

* Managed data/runtime: `core/TPW.PS2.Data` (.NET 8), separate from the Godot `game/` consumer.
* Audits: `tools/TPW.PS2.*Audit`, plus `TPW.PS2.Check`, MP2/reference tooling, and negative-control `teeth.sh` checks. Read each tool's current CLI before running it.
* Scene audits: `game/tests/*Audit.tscn` for integration rather than decoder-only checks.
* Render harness: viewer command-line selectors and screenshot/film switches. A software GL/Xvfb run can exercise actual rendering on the Linux worker; it is not a substitute for declaring human visual inspection.
* Reverse-engineering evidence: `findings/`, disc/ELF readers and disassembly helpers. Read the owner's disc in place; never commit it or extracted payloads.

## Autonomous continuation and handoff

At each major milestone: update `progress.md`, commit explicit paths, fetch/reconcile, rerun affected gates, announce the push scope, and push without force. Continue with the next isolated work package rather than waiting for approval already granted. If another agent owns the relevant file or evidence is unavailable, record that dependency and advance independent tooling/research instead of colliding or inventing an answer.

If interrupted or out of model usage, the last pushed `progress.md` is the handoff: it must identify active branch/revision, completed changes, exact tests, known reds, blockers, and the next concrete action. Local-only work and unverified captures must be marked as such.
