# TPWPS2PC completion plan

Updated: 2026-09-24 UTC. Current purchase integration: peer branch `3583a06` plus independent consumer checks; exact landing/evidence is in progress.md. The original `0db3678` starting point is historical.
This plan is revised at the checkpoints below, not executed as an immutable queue.
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
* Confirm current file ownership with cow tools at each work-package review; the established split reserves its active viewer/needs/effects wiring and our independent lifecycle/validation work until explicitly renegotiated. Do not assume a historical file list is still current. Announce a narrow split before editing or pushing, and exchange implementation and validation for mutual review.
* Retain the documented HALLOW Thrill Grill missing-animation and SPACE Moon Buggies missing-fitting failures. Keep raw audit failures visible; do not weaken assertions, invent records or broaden expected-failure exceptions merely to obtain green output.
* Inventory coverage, interpreted byte coverage, consumer evidence, correctness tests and live integration are separate measures. Maintain READ / CANDIDATE / UNKNOWN distinctions; data-shape guesses are not consumer proofs.
* Every milestone includes the change, a regression or reproducible audit, relevant cross-world checks, and a progress entry with actual results. Record failed attempts and blockers, not only successes.
* Do not claim screenshots were visually reviewed when they were only generated or pixel-checked. Runtime logs, automated scene assertions, rendered captures and human/visual inspection are distinct evidence.

## Review checkpoints — evaluate, then change the plan

Milestones below describe outcomes, not a mandatory order or an invitation to keep adding tests forever.
At a checkpoint, make and record a decision: **continue**, **narrow/split**, **reorder**, **defer with a trigger**, or **stop the work package**.
Change this plan and the next-action handoff when the decision changes priorities; do not merely append another status report.

| Checkpoint | When it fires | Questions and required decision |
|---|---|---|
| CP0: reset the baseline | Now; after major upstream integration or resumed work with a stale handoff | What actually works at which revision? Which failures are retail data, production bugs, fixture bugs or environment limits? Replace stale assumptions and choose the next useful outcome. |
| CP1: select a bounded package | Before starting implementation, a new audit family or significant reverse engineering | What user-visible gap, concrete production risk or blocked consumer does this unlock? What evidence already exists? Agree scope/files with cow tools, minimum validation, and a stopping condition. Defer duplicate or unmotivated work. |
| CP2: review the result | After implementation plus independent review/regression evidence, before landing | Did we fix the actual failure, or only our instrument/fixture? Did scope or the data contract change? Does the test reject a plausible wrong implementation? Land, revise, split or discard based on evidence. |
| CP3: stop audit expansion | At M1/M3/M4 validation gates, and after two consecutive audit-only packages without a newly reproduced production defect | Is another test materially reducing a named risk, or postponing missing functionality? Maintain existing gates and move to an end-to-end feature unless a concrete risk justifies more validation. Record the exception, not an open-ended audit backlog. |
| CP4: revisit evidence or dependencies | A consumer contradicts a field guess; a fixture contradicts its caller contract; ownership changes; required data/platform is unavailable | Correct READ/CANDIDATE/UNKNOWN and compatibility claims, preserve the failed evidence, and reconsider dependent tasks. Do not invent retail semantics, silently weaken a gate or keep waiting without a bounded alternative. |
| CP5: assess integration value | After each playable M5/M6 slice or a cross-system M3/M4 change | Can the intended player action complete through the real consumer, not just a reader or helper? What remains visibly missing? Review the flow together, then choose the next feature or address a discovered integration defect. Missing visual/listening evidence stays missing. |
| CP6: release readiness | Before a platform/release claim, or after toolchain/dependency changes | Recheck chosen supported features and targets against actual clean-source, runtime and manual evidence. Qualify only tested targets; reopen failed gates and keep explicit limitations. |

Each checkpoint entry in `progress.md` must contain:

1. Baseline/revision and the question being decided.
2. What we learned, with tests/source/runtime evidence and its limits.
3. What assumption, priority or acceptance criterion changed (or why it did not).
4. Decision and rationale, including work we are **not** doing now.
5. Next bounded action, file owners, minimum proof/stopping condition, and what triggers the next review.

Routine evidence-based resequencing does not require repeatedly asking for permission already granted.
Keep strawberry informed of project outcomes and coordinate directly with cow tools. Escalate a real product-scope
choice—such as presenting an invented track system as the target experience—or an unavailable manual/platform gate
to the humans rather than claiming that an autonomous technical review settled it. Existing pause/stop instructions
still take priority; checkpoints are not authority to ignore them.

## Priority checkpoint — compiled shop inputs before further producer research

The per-ride value trace resolved a sideshow calculation, not the ordinary family: table
3683B8 uses price/prize/win parameters and a shift4, not the initially reported generic
ride modifier/shift8. Corrected evidence is in findings/ride-value-producer.md. No speculative
producer was wired; a raw SAM field is not automatically its compiled or computed value.

A more immediate proven defect is the shop join: Balloon/VampShop/Droid authored happiness15
versus compiled base10. Cow tools owns runtime compiled-data wiring; astraclaw owns named-asset
and region controls. Use full symbolic identity (not effect-profile similarity), distinguish
missing/ambiguous cases, and preserve the actual regional ice-cream differences. Compiled
base effects still need the documented runtime quality modifier or an explicit port default;
do not confuse a correct lookup with full retail outcome parity. Keep any unresolved ordinary
producer work bounded to an independently identified concrete class/dispatch chain.

## CP2/CP6 adjustment — coordinate-less policy and integration checkpoint

The six shops without stand keys now have a labelled arrival-stub policy: preserve the actual
position reached by the guest, without writing guessed authored values. Both scalar Fields
and fenced Blocks count as supplied data, so malformed keys are not silently treated as absent.
Authored positions retain priority; real host ownership/hiding still wins.132 standing checks,
actual fallback-consumer witnesses and six normal-startup runs cover this bounded behavior.

Original parser/default semantics remain unknown. Peer code/images have been shared; absent
image feedback is not invented sign-off. Human input/camera/platform testing remains at the
end as the owner requested. Stop expanding this finished policy's audit family. Next take a
CP6 clean-source integration/build checkpoint (the prior all-project evidence predates many
changes), then select the next playable gap together with cow tools. Do not substitute that
Linux/cache-backed checkpoint for native Windows or real-audio qualification.

## CP5/CP4 adjustment — visible shop customers and the actual park frame

Normal viewer placement exposed a missing customer that headless purchase arithmetic could
not see. The bounded 2x2 authored-shop standing body fixes that handover gap, but peer image
inspection then exposed a second defect: the legacy GuestWorld/overlay frame displaced
HALLOW by114.5 units and SPACE by10 relative to the built floor. A shared bad expected frame
made the first smoke overstate success. Use the floor's CellCorner transform, not per-world
offsets, and make actual-floor alignment an independent gate alongside body ownership.

Cow tools reviews code/images and traces defaults for the six coordinate-less shops;
astraclaw owns the narrow renderer/frame fix and independent normal-startup checks.
The current bounded exit gates pass:93 standing/frame checks, fresh8scene runtime,
normal-startup rendered service, and peer marked-image review in JUNGLE/HALLOW/SPACE.
Coordinate-less bodies remain explicitly open while that evidence is investigated. Do not
conflate presence, alignment, visual readability and retail behavior. Land this bounded integration and resolve those six customers next. If original defaults
remain unread, explicitly label a conservative port fallback rather than inventing provenance.
Do not restart a finished arithmetic census or an unrelated subsystem by default.

## CP4/CP2 adjustment — compiled purchases need product-aware consumers

The join exposed more than wrong authored happiness: the consumer was charging one tenth
of the price, quenching thirst for food, and treating distinct product arms alike. A first
correction adding thirst/litter universally would instead break drinks and balloons. Stop
broad “purchase path complete” claims: retain the verified initial q1=100/q2=0 baseline,
full symbolic identity and explicit regional values, then validate real handbacks per product.

Current bounded package combines cow tools' `purchase-arms` implementation with our independent
67 checks per world: named balloon/ice-cream/drink/fries/costume consumers across all3regions,
opposite-transfer controls, affordability299/300 and post-completion stability. Original
instruction evidence and limitations are in findings/compiled-shop-consumer.md. Product7's
q2/15 prefix falls through into food; costume index8 resolves intensity14 but does not yet
change the visible actor. Neither discovery licenses expanding random-spawn preferences.

CP2 stopping condition: these checks reject plausible price/sign/selector/no-op/guard defects,
then pass on the joint branch with the existing matrix/runtime gates unchanged. After landing,
review the playable shop flow and remaining ownership/presentation/accounting gaps rather than
expanding this audit family without a reproduced risk. Keep ordinary-ride producer research
separate; do not wire a sideshow formula into ordinary rides just to close a checklist.

## Latest CP3 adjustment — original ride-effect consumer, 2026-09-24 UTC

Reading the newly decoded constants led to a real correction: sickness is gated at ride
value56, and happiness is banded by absolute preference mismatch rather than always15.
Peer branch code plus independent33 consumer checks and50 lifecycle checks now exercise
both nonzero preferences and the deliberately unspecified fallback. Eight mutations reject
missing/wrong gates or bands; the full runtime gate and four-world matrix retain their
expected results. See findings/ride-effect-consumer.md for instruction evidence and limits.

The first eight preference records are verified; original table extent and assignment of
index+0x7D are not. Uniform choice among that verified set is a labelled port decision.
The per-ride callback value still defaults to45. Next bounded work should connect a real
per-ride producer only after tracing the existing callback/data path, rather than assuming
an authored field with a plausible name is the runtime value. Keep core edits coordinated
with cow tools; independent regression/consumer verification remains astraclaw's part.

## Latest CP5 adjustment — rendered service flow and readability, 2026-09-24 UTC

Normal Viewer startup + real build/service callbacks now produce successful rendered
small-toilet captures in all four worlds. Directly inspected JUNGLE crops confirm the
standing body/doorway/handback relationship and the correct WC icon after close-up tuning.
The default camera actually projects the full icon quad to11.46px at1280x720: its raw
2576 altitude is divided by256, not2576 Godot tiles. Do not reuse that unit error or the
peer's superseded23px opaque-mask measurement.

Owner explicitly requests best-effort autonomous choices with human testing collected
at the end, not permission waits. A proposed48–64px screen clamp was rejected after peer
measurement put the whole hut at approximately32×45px in that park-view capture. Retain
world scaling: indicator far away, readable artwork close up. This is chosen port policy,
not a decoded rule or the only possible UI design. No unused scaling solver is shipped.

Next bounded work: review newly decoded needs-effect constants and their consumer arithmetic
against the actual executable, while preserving peer ownership of core files. Avoid replacing
one speculative value with another or claiming image data equals runtime confirmation.
Keep crowding/zoom/DPI/interaction/native platform checks on the final human checklist.
See findings/service-smoke.md for scope, direct crop review and exact camera evidence.

## Earlier CP2/CP5 checkpoint — standing service, 2026-09-24 UTC

This supersedes the courtesy-approval hold in the older CP1 preflight below: owner
resume authorization was already sufficient. Peer shipped service routing/dispatch,
reachable-candidate fallback and authored geometry accessors, then departure and queue
consumers through c7eb67f. Astraclaw's bounded standing-service patch closes the real
small-toilet handover/body gap and now carries74 runtime checks plus five routing checks/world.
Peer0618434 also fixes interrupted departures; six recovery checks/world reject the old
source. A transient Stranded state was removed from the test contract because valid retries
need not preserve it—observable conservation/recovery, not implementation state, is the gate.
Actual placement and service callbacks pass at four JUNGLE rotations; headless scene-graph
proof is not manual visual approval. See findings/standing-service.md for exact scope.

Learning: authored script ownership does not prove this port draws a replacement body;
actual host visibility/pose ownership must decide. Eventual relief does not prove priority
routing; a disconnected nearer service and reachable distracting ride discriminate it.
Godot's deferred deletion must finish before a test calls a retired bubble a visible leak.

Next bounded action: exchange peer review of these integration changes, then normal-viewer
rendered/manual smoke with explicit inspection if available. Keep scope on the playable
service flow and inspect new departure/queue consumer interactions; do not broaden to
Super Toilets, track systems or another generic audit campaign without CP1 evidence.
Keep the two exact retail reds and manual/platform limitations explicit. The runtime gate
now has seven scenes; older clean-release evidence retains its original revision.

## Current review and priority decision — CP0/CP3, 2026-09-24 UTC

The original `699a9b1` review has been revised after actual service and compiled-shop integration. Earlier clean-build evidence keeps its own revision; it is not silently promoted to current parity:

| Area | What we now know | Plan adjustment |
|---|---|---|
| Baseline and automated gates (M0/M1) | Published matrix now includes67 compiled-purchase checks/world; the current fresh Debug eight-scene runner passes. The all23-project clean-checkout evidence remains pinned to `6004151`. | Maintain/reuse these gates. Do not repeatedly rebuild them as a substitute for feature delivery. Earlier framebuffer/capture evidence keeps its own revision. |
| Reader evidence (M2) | Unknown DBA storage is preserved, not fully interpreted; malformed compiled text is now rejected with real regional compatibility checks. | Follow a reader gap when it blocks a chosen consumer or a reproduced defect. Do not turn preservation coverage into a semantic-completion claim. |
| Guest lifecycle (M3) | Removal, needs continuity and disruption/replay are guarded; interrupted departure recovery was subsequently fixed and rejects the old stuck-guest implementation. Purchase handbacks retain their own counter and side-table controls. | Defer the proposed standalone expansion of walking-contract audits. Reopen it for a concrete movement bug or a chosen feature that depends on an uncovered boundary. |
| Effects/audio (M4) | Joint fixes have real voice/cue and synthetic lifecycle controls, including world-owned state cleanup. | Keep those regression gates; do not call Dummy playback an audible-quality or complete engine-layer result. |
| Gameplay consumers (M5) | Service, departure and queue consumers now run. Small toilets have actual viewer placement/standing/handback evidence; compiled shop product effects and transaction cash are tested at real script handback across all3regions. | Finish the joint purchase integration, then demonstrate a placed shop through normal viewer startup. Inspect presentation/ownership at that boundary before adding more arithmetic checks. Costume actor changes and balloon ownership are still incomplete. |
| UI/advisor (M6) | Browser callbacks work, but rules, lip playback and original font/Kanji readers still lack game consumers in the current census. | Treat these as genuine integration gaps. Choose one if the gameplay package is blocked; do not build speculative state producers or more browser tests by default. |
| Release (M7) | Clean Linux source/build/runtime evidence exists; selected small-toilet/WC crops were directly inspected. Native Windows, actual listening and general visual/platform sign-off remain absent. | Keep unsupported/manual claims open. Repeat release gates for relevant changes, not to imply those missing qualifications are solved. |

**Next selection:** after the purchase package lands, reuse the normal-startup service smoke
for a named placed shop and verify the actual purchase, guest ownership and visible presentation.
The headless arithmetic suite cannot establish that a shopper has a body during service. Inspect
existing script/renderer contracts first, coordinate core/viewer ownership with cow tools, and
reproduce any gap before choosing a fix. Preserve explicit limits on costume actors, repeated
balloon ownership, mutable quality and park accounting; no automatic broad rewrite is implied.

### Historical CP1 preflight — minimal toilet flow (superseded; not an active approval hold)

The following records the pre-implementation decision, not current blocking instructions. Owner
subsequently authorized continued autonomous execution and the slice shipped as described above.

Cow tools confirms that needs-satisfaction consumers are its scope and proposes the toilet as the smallest
end-to-end slice: one placeable facility, route an eligible guest to it, use it, retain the guest identity,
record the returned soil amount, and update the thought. It owns `ParkVisitors`, `VisitorNeeds` and viewer wiring;
astraclaw owns independent validation and must agree the observable API before writing integration checks.
Strawberry's confirmation for placement/routing scope has been requested; **do not call that approval or an
implemented feature**. Preparing the contract/evidence is safe while that dependency is open.

Read-only preflight now independently confirms two ProvidesRelief definitions per world and the small-toilet
shape/offsets; see `findings/toilet-slice-preflight.md`. Start with one **small** toilet, not all eight assets:
super toilets take a different LIMBO path, and FANTASY's super variant has an animation-resolution question.
Placement is still runtime-unconfirmed. A newly identified consumer seam is outside-service visibility:
current handover removes the walking body, while the viewer retains only walking/seated/scripted-WALK bodies.
Require a correct visible standing-service path and agree the completion/soil interface before tests or wiring;
do not quietly expand this into general queue rendering or treat a data census as feature completion.

Proposed minimum acceptance, subject to the agreed interface:

* With other urgent needs controlled and growth rates frozen, Toilet 90 does not trigger this need-driven
  errand; Toilet 91 with a reachable, usable toilet routes through the actual walking/service consumer.
* No early satisfaction or reseeding: the same guest retains cash and unrelated needs while travelling.
  Only actual arrival/service may set Toilet to 0; an unreachable or absent facility does not satisfy it.
* Existing `UseToilet` arithmetic is observable at the facility: starting at 91 produces 20 soil,
 92 produces 21, and 100 produces 26. A completed use is accounted once; later idle updates do not duplicate it.
* The sole toilet thought is cleared/recomputed after use, not merely hidden while the need stays high.
  A missing route is distinct from a mid-edge `Send` refusal; neither licenses teleporting or losing the guest.
* Select at least one disruption boundary (facility removed/closed while travelling) and define its behavior
  before implementation. Preserve ownership and needs rather than inventing a completed service.
* Demonstrate the actual placed-facility flow in the viewer as well as the engine-free regression. Logs or
  headless checks alone do not establish bubble appearance or placement usability; visual limits stay explicit.

The peer has corrected its earlier executable interpretation: the threshold helper records an errand, rather
than searching for a facility. Record/verify that consumer evidence in the implementation findings before
promoting it to a decoded claim. Current `Decide` still exposes availability-gated single-need branches, so
source behavior, updated evidence and the intended contract must be reconciled—not silently assumed identical.
Facility selection/routing/service timing not established from the executable must remain labelled port policy.
No food purchases, economy, general facility framework, cleaning staff or advisor rewrite are implied by this slice.

Review trigger: scope confirmation and concrete interface/file split produce the final CP1 decision; then CP2
checks regression controls and CP5 checks the actual player flow. If placement needs a substantially larger
subsystem or missing data, narrow/defer it explicitly and select another supported consumer rather than hiding
scope growth inside “one toilet”.

## M0 — Establish the executable baseline and collaboration ledger

Deliverables:
1. Commit this plan and `progress.md`; incorporate the isolated ride-availability fix without losing current upstream work.
2. Enumerate existing audit commands and distinguish known retail findings from new regressions.
3. Make repeatable evidence capture straightforward: command, revision, world, raw exit status, exact failures, and local log/capture references.

Exit gate: the plan/progress are on remote main; the eligibility fix has before/after evidence; all four park audits are run against the integrated revision. JUNGLE/FANTASY are expected to pass; HALLOW/SPACE retain their precise known failures unless new independent evidence changes the findings.

Review point after M0: CP0 confirms the integrated baseline and separates known retail reds before choosing the first validation package.

## M1 — Repeatable validation and actual rendered smoke coverage

First work package, deliberately outside the active viewer/core files:
* Add an audit runner which builds selected projects, runs reproducible world matrices, retains raw outputs and writes a structured result manifest.
* Start with ParkSimAudit, then expand to reader/VM/visitor audits after recording their real baselines. An expected retail finding is a distinct outcome, not a pass. Unknown failures, crashes, timeouts, missing checks, or extra failures must fail classification.
* Test the runner's own classification with negative controls: an extra error containing a familiar ride name must not be accepted; a truncated/crashed audit must not look successful.
* Build and run the actual Godot viewer in an isolated checkout. Use the existing `--guest-test`, `--shot`, `--walk-film`, `--ride-film`, `--place-test`, `--path-test` and `--ghost-test` controls where applicable. Record the renderer/engine, world, command, logs and output dimensions; inspect actual captures when image-viewing capability is available.
* Exercise normal launch, missing/invalid disc, a world change and an exit. Do not confuse a headless data-only run with rendered verification.

Exit gate: a successor can reproduce the baseline without guessing commands; at least one real rendered park smoke is recorded, and the distinction between inspected and merely generated captures is explicit.

Review point after M1: CP3 decides whether the validation infrastructure is sufficient for the next feature; uninspected captures do not make its manual gate complete.

## M2 — Reader completeness and evidence debt

* Reconcile `findings/format-inventory.md` with current source and upstream changes. ParticleTemplate/consumer findings recently superseded earlier particle-field guesses; do not repeat stale claims.
* For DBA, finish only semantics supported by executable consumers and cross-region evidence: specialised track/tour fields, upgrade selector, unnamed flags/ranges. Add identity/field mutation tests for every interpretation.
* Inventory uninterpreted sound-map records, fitting fields/pointers, GIN payloads and any retained raw ranges. Measure bytes accounted for, not merely tags recognised; validate every claim against the current readers before opening work.
* Revalidate model/APS/skinning uncertainties against independent controls. Separate reconstruction (e.g. inferred bind transforms) from direct decoding. If required source data is unavailable, record the boundary rather than fabricate a “fix”.

Exit gate: each format has an honest coverage/evidence table, mutation/round-trip or independent-reference checks appropriate to it, and explicit remaining ranges. A format is not declared complete because a constructor accepts the file.

Review point within M2: use CP4 whenever a consumer contradicts an interpretation; at CP1 tie further decoding to a concrete consumer/evidence gap.

## M3 — Park/visitor lifecycle correctness

* Finish ride availability checks (first patch already tested): closed/broken rides cannot be destinations or receive newly arriving guests; recovery works; optional flags remain safe.
* Audit ride removal, route invalidation, closure after queueing, cancellation and ownership transfer. Preserve guest identity and account for each guest in exactly one appropriate lifecycle state. Do not silently delete people.
* Represent waiting/queued guests deliberately rather than letting them disappear between the walking and seated sets; coordinate rendering changes with the viewer owner.
* Add tests for multiple rides/guests, blocked routes, removal during walking/queueing/riding, and deterministic fixed-tick replay. Keep the renderer a consumer of engine-free simulation state.

Exit gate: guest conservation and lifecycle invariants hold through disruptions; a manual/rendered sequence demonstrates arrival, waiting, boarding, riding, exit and recovery without duplicate or lost bodies.

Review point after M3: CP2/CP3 distinguish production fixes from fixture fixes and stop unmotivated census expansion; CP5 evaluates real gameplay integration.

## M4 — Effects and audio integration

* Coordinate with the active particle implementation before touching its files. Evaluate the current consumer-backed emitter implementation, not the older generic-burst code from the initial sweep.
* Check persistent object handles, ADDOBJ/EVENT distinctions, KILLOBJ/FADEOBJ/SETOBJPARAM, child effects and cleanup across world/ride removal.
* Connect or explicitly account for decoded `.eng` layers and sound event/clip selection. Distinguish proven clip lookup from inferred variant thresholds/attenuation.
* Investigate unresolved executable consumers/vtable paths only with provenance and an independent check. Do not make a static table “used” by guessing a mapping.

Exit gate: representative effects and sounds follow script lifecycle correctly, clean up on removal, and have recorded runtime/rendered/audio evidence; unresolved paths remain named.

Review point after M4: CP4 reassesses unresolved semantics; CP3/CP5 weigh another effects audit against a missing player-visible feature.

## M5 — Gameplay systems and track capability boundary

* Establish what the shipped PS2 executable actually supplies for TOUR/BUMP/COAST and the ride classes they affect. Re-census the reported 27/86 non-boarding rides on the current tree rather than assuming all are eligibility bugs.
* Define the track/vehicle/path representation, construction constraints, movement/boarding lifecycle, and persistence tests before implementing it. Keep any behavior extending a retail stub explicitly identified as an extension, not reverse-engineered fact.
* Build persistent visitor needs/economy/happiness/queue-choice state and park-management services from validated game data where available. Distinguish original algorithms from port policy.
* Add save/load only with an explicit state schema, stable identities, round-trip tests, versioning and corruption handling; confirm existing support before designing a replacement.

Exit gate: the selected playable feature set is functional and reproducible; faithful unsupported cases and optional extensions are clearly separated. No invented implementation is reported as console parity.

Review point within M5: CP1 bounds each feature slice and labels extensions; CP5 reviews the implemented player flow before expanding the subsystem.

## M6 — UI, advisor, text and accessibility

* Integrate original font/Kanji/text resources where appropriate; decoded tables require actual rendering/lookup consumers and non-Latin test cases.
* Connect advisor rules to game-state producers, scheduling, speech/subtitles and lip transitions. The existing sound-browser catalogue is not automatic park advice.
* Implement remaining menu/interaction paths from shipped layout evidence where available. Exercise keyboard/mouse focus, scaling, cancellation and informative error states.
* Verify every visible action has a real path (including launcher Retry/update failures), without silently launching stale binaries after failed builds.

Exit gate: controls and feedback form a usable workflow instead of disconnected asset demos, and representative locale/advisor/error-path scenarios are demonstrated.

Review point within M6: CP5 requires real producers/consumers and usable interaction, not just successful asset browsing; defer unknown semantics explicitly.

## M7 — Release and long-run validation

* Build from a clean checkout on supported target platforms; record exact toolchain/engine requirements and dependency/license notices.
* Exercise launcher install/update/relaunch, offline and failure recovery, user-selected disc paths, and no bundled proprietary data.
* Run cross-world regression gates, long simulations, repeated load/unload, resource-leak checks and fixed-tick comparisons. Preserve known retail failures as evidence.
* Produce a release/handoff checklist with feature status, evidence links/commands, known limitations and the exact revision tested. Do not label an untested platform supported.

Exit gate: documented installation and gameplay workflows work on the declared targets, outstanding exceptions are explicit, and another developer can reproduce all release evidence.

Review point after M7: CP6 checks exact tested revisions/platforms and outstanding manual gates before any release claim.

## Tooling already available

* Managed data/runtime: `core/TPW.PS2.Data` (.NET 8), separate from the Godot `game/` consumer.
* Audits: `tools/TPW.PS2.*Audit`, plus `TPW.PS2.Check`, MP2/reference tooling, and negative-control `teeth.sh` checks. Read each tool's current CLI before running it.
* Scene audits: `game/tests/*Audit.tscn` for integration rather than decoder-only checks.
* Render harness: viewer command-line selectors and screenshot/film switches. A software GL/Xvfb run can exercise actual rendering on the Linux worker; it is not a substitute for declaring human visual inspection.
* Reverse-engineering evidence: `findings/`, disc/ELF readers and disassembly helpers. Read the owner's disc in place; never commit it or extracted payloads.

## Autonomous continuation and handoff

At each major milestone: perform CP2 and any triggered priority review, update `progress.md`, commit explicit paths, fetch/reconcile, rerun affected gates, announce the push scope, and push without force. Continue with the next bounded package chosen by the latest checkpoint rather than waiting for approval already granted. A later checkpoint supersedes an older next-action note. If another agent owns the relevant file or evidence is unavailable, record that dependency and choose bounded work that advances an agreed outcome—not independent tooling merely because its files are free.

If interrupted or out of model usage, the last pushed `progress.md` is the handoff: it must identify active branch/revision, completed changes, exact tests, known reds, blockers, and the next concrete action. Local-only work and unverified captures must be marked as such.
