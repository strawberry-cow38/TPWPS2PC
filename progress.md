# TPWPS2PC progress / AI handoff

Updated: 2026-09-25 UTC. Roadmap: [plan.md](plan.md).

## HANDOFF (rewritten in place at every landing; newest log entry is at the BOTTOM of this file)

Updated 2026-09-25 by tinyclaw. The active checkout is `/home/ec2-user/tpwps2-entrance`, branch
`tinyclaw/native-entrance-flow`, pushed as `origin/astraclaw/native-entrance-flow`. It is merged with
origin/main as of 8ef44b9. astraclaw is out of usage until Oct 1, and tinyclaw is executing plan.md
until then.

**The branch.** It is opt-in research. `--experimental-native-entrance` runs bus-born guests through
the native booth queues and fee, and walks rejected guests back to the bus. `--native-guest-animation`
adds readiness: a guest waits for its animation to commit before it walks. Default play on main is
unchanged. The standalone native core is on main as slice A (c5dfe12, merged back here); the entrance
flow, readiness wiring and Viewer hooks are still branch-only (slices B and C, queue item 6).

**Gates at 783225b** (clean tree, /tmp/tpw-783225b):
- unit: 79 tool tests OK.
- `tools/audit_matrix.py`: 8 parks, terrain_2 included; only the two retail reds (HALLOW Thrill Grill,
  SPACE Moon Buggies); `landing_evidence=true`.
- `tools/runtime_audit.py`: 11 of 11 scenes pass.
- `tools/viewer_matrix.py`: 24 of 24 across all 8 real parks, loaded == requested in every case.

**Corrected today.** Every earlier "eight park" viewer claim had run FANTASY terrain_1 for its park-2
cases, because the `--map` label didn't match. See the CP2 entry at the bottom of this file.

**Next action.** plan.md section 3 (Now): queue item 7, ordinary departures through state 26. Items 4
and 5 wait on cow tools and strawberry; item 9 (corridor check) is done and the planner stays deferred.
The labelled adapters are listed in plan.md section 6.

**Rules that still hold:**
- No restarts.
- Do not redo the shipped bus, destination scoring, relief, shop walking, hide list, gate or path-price
  work.
- Cow tools owns the body of Viewer.cs, economy, gait, audio and particles (plan.md section 2).

## Previous handoff (2026-09-24, superseded)

Active checkout: `../tpw-gate-occupancy`, branch `astraclaw/native-entrance-flow`.
Bus and requested smoothing are already on main through98b5249; do not redo them.
This research branch now includes the full INCOMING controller through actual
experimental Viewer bus births, staging/groups, fee credit/debit and actor handoff.
It is OPT-IN `--experimental-native-entrance`, NOT a default/main release: public BFS,
unported native search/request resources, readiness bypass and constructor-only fee
seed are explicit adapters. Output routes now use a real shared1000-slot pool; read
the latest snapshot and native-route-slot-pool.md, not older unlimited-slot notes. Normal rejected guests now walk out to point0 and retire under the explicit
represented-activation phase adapter. Native startup/clock origin, ordinary
state0/state5 failure recovery and non-controller pressure population remain open. See latest snapshot and findings/native-incoming-controller.md. Do NOT
call this complete native entrance parity or quietly enable it for normal play.
The old `tpw-ride-eligibility` checkout and scheduler's `81d112c` note are stale.
No restarts. No reimplementation of the shipped destination scoring, relief, shop
walking, Bouncy hide-list, gate or path-price work. Cow owns the current RMB/listbox and shop-info UI work; leave those files to them.

Bus integration and smoothing are already shipped. Normal Viewer startup/real
placement/first admission/map reload passed in **all eight starting parks**,2039
assertions each. The release snapshots below are historical evidence, not a pending
bus-landing task; use the newest entrance-foundation entry for current work. Peer reviewed8b0bba9, no blocker;
requested explicit permissive-bypass language for missing entrance/departure inputs.
`findings/bus-viewer-integration.md` describes scope and reproduction. After bus
landing, the next native behavior package is the entrance-group/departure-pressure
lifecycle, not another cosmetic bus or another replacement timing heuristic.

Historical ledger follows; later entries supersede earlier worktree/next-action
statements. The lower-level findings retain their dates and original evidence.

## Historical starting milestone and next action

M0 is published on remote main through `4090631`: plan, handoff ledger, and integrated ride-availability fix.
M1 now includes a standalone audit matrix runner and a real rendered JUNGLE guest smoke. Full visual sign-off and broader audit/scene coverage remain open.
DBA unknown-byte preservation coverage is implemented and verified without changing core readers/goldens. Next: the isolated guest/ride-removal lifecycle gap, after checking current ownership and reproducing it; continue periodic rendered/runtime checks. Keep off the active viewer/placement/particle files.

## Coordination / branch state

* Work is isolated on `astraclaw/ride-eligibility`, not in another agent's live checkout.
* Fetched upstream through `0db3678`, including the latest gate/lights changes, and rebased the eligibility/plan commits. The initial tested patch was `326279e` (first rebased as `4889ea1`); use Git history for the current rebased identity. No force push and no discarded upstream changes.
* Inspected the upstream diff specifically for `ParkSimAudit/Program.cs` and `ParkVisitors.cs`: neither changed between the old `0862aa8` baseline and fetched `f9d1035`. The larger audit additions mentioned in chat were already in `0862aa8` and remain intact.
* Cow tools is actively working in the viewer/placement/particle area. The initial particle snapshot from the sweep is stale: upstream now includes `ParticleTemplate.cs`, consumer-backed findings, and revised emission behavior. Review current source, not that old gap list.
* Initial push failed because this shell had no default HTTPS credential helper. Existing configured GitHub CLI authentication is used via its credential helper; no credential values are read or printed.
* The separate local shared-main checkout still contains the original unpublished eligibility commit; do not reset it or sweep its files into another commit. Remote integration is performed from the isolated branch.

## Completed: closed/broken ride eligibility

Changes in `4889ea1`:
* `core/TPW.PS2.Data/ParkVisitors.cs`: `Takes` requires both declared closed/broken flags to be zero; absent optional flags remain safe through `ParkRide.Get`.
* `tools/TPW.PS2.ParkSimAudit/VisitorAvailabilityChecks.cs`: isolated disc-backed regression fixtures, with no simulated script ticks changing the flags under test.
* `tools/TPW.PS2.ParkSimAudit/Program.cs`: invokes the checks using a real ride fixture without polluting the long-running park scenario.

Evidence before rebase:
* Unfixed core reproduced **15 availability failures** in JUNGLE, HALLOW and SPACE.
* Fixed core passed **30 availability checks in each of JUNGLE, FANTASY, HALLOW and SPACE**: destination filtering, unchanged plans on rejection, arrival-time closure/breakage, reopening/repair, and absent optional flags.
* Full JUNGLE/FANTASY ParkSimAudit passed.
* Full HALLOW/SPACE each retained exactly one pre-existing, separately reproduced baseline failure: Thrill Grill and Moon Buggies respectively. No assertions were weakened.
* Release audit build succeeded. Existing nullable-annotation warnings are not new failures.

Portable reproduction (substitute the owner's existing disc path; do not copy/extract it):

```sh
dotnet build tools/TPW.PS2.ParkSimAudit -c Release
for world in JUNGLE FANTASY HALLOW SPACE; do
  dotnet tools/TPW.PS2.ParkSimAudit/bin/Release/net8.0/TPW.PS2.ParkSimAudit.dll "$DISC" "$world"
done
```

## Known reds / boundaries — preserve evidence

* HALLOW / Thrill Grill: requests an animation slot absent from the relevant disc animation data. Existing audit intentionally reports failure; do not add a fictitious record or weaken the test.
* SPACE / Moon Buggies: lacks the relevant park-space fittings used by the walking path. Existing audit intentionally reports failure; do not fabricate fittings.
* Track-service non-boarding is a separate capability gap, not evidence that every such ride should be made eligible or forced to board.
* The original format inventory is coverage bookkeeping, not proof that every byte or live consumer is complete. READ/CANDIDATE/UNKNOWN distinctions remain important.

## Validation still to do

* Integrated gates rerun on `4889ea1` over upstream `f9d1035`: all 120 availability checks passed; full JUNGLE/FANTASY passed; HALLOW/SPACE each retained only the same single known baseline failure.
* Actual rendered JUNGLE guest smoke performed; details below. Captures were generated and pixel-checked, not visually inspected by a human/multimodal viewer.
* No claim of full-game completion, all-world green status, or platform coverage beyond tests actually recorded here.

## Interruption notes

An earlier task ended on a model connection error after worktree creation but before TPW source edits. Astraclaw's model-request retry policy was subsequently fixed and tested; work resumed through an owner-authorized startup job. This project ledger, not the bot's private runtime files, is the intended successor handoff.


## M1: repeatable audit evidence and rendered smoke

New files: `tools/audit_matrix.py`, `tools/test_audit_matrix.py`. The runner builds ParkSimAudit, runs a selected world matrix, preserves raw logs/exits and disc SHA-256, and writes a manifest after each world. Output directories must be fresh so earlier evidence cannot be overwritten. Timeouts record the actual child status, separate from runner classification.

Exit meanings are intentionally distinct: **0** all selected worlds passed; **1** unexpected result/build error; **2** exact documented retail failures remain. A known-red world unexpectedly passing requires review; new failures containing a familiar ride name are not whitelisted. No original C# audit assertion was altered.

Validation:
* 15 Python tests pass (positive cases, extra failures, extra rides, misleading names, missing coverage/summary, crashes, truncation, suppressed exit codes, timeout metadata and evidence preservation).
* Real four-world runner execution: JUNGLE/FANTASY PASS, HALLOW/SPACE KNOWN_RETAIL_FAILURE with raw exit 1 each, 30 availability checks per world; runner exit 2, not a false all-green result.
* Godot viewer Debug build: succeeded, 15 existing warnings, zero errors.
* Actual Godot 4.6 mono run at source revision `4090631` under Xvfb/Mesa llvmpipe GL compatibility, Dummy audio, JUNGLE `--guest-test`: exit 0 and four 1280x720 PNGs (walking, two ride stages, seated close-up).
* Runtime stage A reported 28 boardings, 8 completed rides and 8 walking guests; close-up reported 8 seated riders. These are instrumented runtime observations, not a claim of retail-AI equivalence.
* Captures have thousands of sampled colours and nonzero RGB variance; this rules out a wholly blank capture, NOT incorrect geometry/animation. They have NOT received direct visual sign-off. Audio events started according to instrumentation; Dummy output means nobody has heard this run.
* Editor import rewrote the SDK declaration from 4.6.2 to 4.6.0 and created UID/backup files. The tracked project was restored exactly and generated files archived outside the worktree; none of those incidental changes are committed.

Reproduction:

```sh
python3 -m unittest discover -s tools -p test_audit_matrix.py -v
python3 tools/audit_matrix.py --disc "$DISC" --out "$NEW_EVIDENCE_DIR"
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
dotnet build game/TPWPS2Viewer.csproj
# If editor import is necessary, inspect/revert ONLY its incidental generated changes afterward.
LIBGL_ALWAYS_SOFTWARE=1 xvfb-run -a -s '-screen 0 1280x720x24' "$GODOT"   --path game --rendering-method gl_compatibility --audio-driver Dummy --resolution 1280x720   -- --disc="$DISC" --mode=park --map=JUNGLE --guest-test --shot="$CAPTURE_DIR/jungle-guests.png"
```

Raw logs/captures are local artifacts outside Git; no disc/extracted game payloads were committed. Their filenames are `jungle-guests-w.png`, `jungle-guests-a.png`, `jungle-guests.png`, `jungle-guests-c.png` plus `viewer.log` and the audit `manifest.json`.

## Next work package: DBA preservation coverage

A scoped subagent review identified a safe, bounded gap: the DBA reader preserves unknown bytes, but targeted mutation coverage currently exercises only a small subset. Add an audit-local `UnknownFieldCoverage.cs` and minimal DbaAudit invocation; describe tested ranges in `findings/dba.md`. No core-reader or golden changes are planned.

Acceptance: bounds/overlap checks, directory-ordinal identity (duplicate keys exist), one-bit mutations on cloned in-memory records, exact raw-byte preservation, unchanged unrelated decoded fields, and useful region/key/kind/offset/mask failure messages. Original regional golden checks must still pass. This tests preservation only; track/tour selectors, upgrade joins, unknown flags and other semantics remain UNKNOWN until consumer evidence supports them.

M1 integration follow-up: upstream `2a45f4b` changed viewer presentation while this isolated tooling work ran. Rebased without overwriting it; the recorded rendered captures predate that viewer-only commit and are not claimed as a visual verification of its changes. The audit runner and known-red baseline were rerun after integration.


## M2 evidence milestone: DBA unresolved-storage preservation

Implemented audit-local `UnknownFieldCoverage.cs`, minimal DbaAudit invocation/CLI,
and an evidence update in `findings/dba.md`. No core reader, retail identity table or
existing golden changed; DBA remains partly decoded.

Validation: all original EUR/USA/JAP identity/field/consumer-signature checks pass;
57,664 one-bit preservation probes pass per region (**172,992 total**), covering 273
entries/eight kinds in each. Both duplicate-key records are exercised by ordinal
(528 probes per region). Five negative controls per region demonstrate that the
checker rejects missing mutations, hidden typed-projection defects, collateral
unknown-accessor corruption and unrelated-record changes.

A review caught two weak checks before landing: change-only canonical comparison
could hide a stale typed field behind updated raw hex, and checking only the mutated
accessor could miss collateral unknown-value corruption. Both were strengthened to
exact raw-byte-derived canonical deltas and all target unknown accessor bytes, then
the full three-region sweep was rerun successfully. No semantic meaning was inferred
from these tests. No rendered capture is needed or claimed for this audit-only change.

Next proposed implementation package: reproduce `ParkSim.Remove` dropping a ride
without reconciling its queued/riding guest ownership. Coordinate `ParkSim.cs` and
`ParkVisitors.cs` before edits, preserve guest IDs, and add engine-free lifecycle
regressions; leave active viewer/placement/particle work alone. This is still a
proposed next task, not a reported fix or an assertion that the current viewer calls
that API. If ownership conflicts, advance independent audit coverage instead.

## M3 implementation milestone: guest recovery after ride removal

Implemented in `ParkVisitors.cs`; isolated helper `RideRemovalChecks.cs` and minimal
ParkSimAudit integration. Details and explicit managed-port policy are recorded in
`findings/ride-removal.md`. No Viewer, ParkEntrance, particle, placement, or ParkSim
source changes in this milestone. No claim of console evacuation decoding or a new
viewer removal feature.

The original regression produced 12 failures before the core fix. Recovery now
preserves guest IDs across queued/offered/seated removal, Clear, numeric ride-ID
reuse, missing endpoints and temporarily absent ground. Heading guests keep their
physical walk. Explicit Recovering ownership retries safely. Already-reported script
completion remains counted exactly once after readmission; evacuation does not
invent completion. Review caught and corrected that completion-boundary issue and
two weak fixtures before the final runs.

Validation after integrating upstream `19e2b0f`:
* 57 removal assertions per world, all four worlds, both isolated synthetic-corridor
  and ordinary integrated modes (228 per mode), all pass.
* 120 availability assertions pass; full JUNGLE/FANTASY pass; HALLOW Thrill Grill and
  SPACE Moon Buggies retain only their precise original retail failure, raw exit 1
  each; matrix runner exits 2 as intended.
* GuestAudit passes JUNGLE/FANTASY/HALLOW/SPACE; 15 matrix-runner Python tests pass.
* ParkSimAudit Release build passes (12 warnings); Viewer Debug build passes
  (15 warnings, zero errors). No new rendered deletion/evacuation verification is
  claimed: this work is engine-free, and a production Viewer removal caller was
  not established.
* At upstream `f2ef76e`, HALLOW/SPACE initially crashed with no resolved entrance,
  reproduced before our changes. Reported to the active entrance maintainer, then
  pulled their `19e2b0f` correction and reran the full integration successfully.

Local evidence outside Git: `tpw-removal-before.log`,
`tpw-removal-baseline-f2ef76e/manifest.json`,
`tpw-removal-final-isolated-{WORLD}.log`,
`tpw-removal-integrated-19e2b0f/manifest.json`,
`tpw-removal-guest-{WORLD}.log`, and `tpw-removal-viewer-build.log`.

Next bounded package: multi-guest/multi-ride conservation and deterministic replay
checks around these lifecycle transitions, including removing one ride while another
continues running. Start audit-only, demonstrate failures before any additional core
change, and leave active renderer/placement/entrance/particle work to its maintainer.
M3 is not complete: queue presentation and rendered lifecycle sign-off remain open.

## M3 follow-up: crowds, ownership controls, replay

Published removal implementation: `9da7a2a`. The next package adds only
`GuestConservationChecks.cs`, its invocation in the audit helper, and docs; no core
or renderer edits. Eight guests/two real scripts are exercised through actual seating,
removal of one instance, completion on the surviving instance, and final Clear.
Every step checks identity conservation and incompatible body ownership; five
corrupted fixtures prove the census rejects bad ownership. Two independent runs
must reproduce the same sampled lifecycle digest (not full VM state or cross-frame-
rate equivalence).

All 20 additional assertions pass in each world, isolated and integrated. Replay
lengths are 112/91/133/170 steps for JUNGLE/FANTASY/HALLOW/SPACE respectively, twice
each per mode. Existing 57 removal and 30 availability assertions per world remain
passing. The whole-park matrix still retains exactly HALLOW Thrill Grill and SPACE
Moon Buggies, exit 2. Evidence: `tpw-crowd-integrated-9da7a2a/manifest.json`,
`tpw-crowd-final-isolated-{WORLD}.log`. No new runtime bug was found by this package.

Coordination requested by VoX: cow tools owns VisitorNeeds.cs and core/viewer needs
and thought-bubble wiring; ParkVisitors.cs was explicitly released after `9da7a2a`.
Astraclaw owns independent lifecycle/conservation/replay audits, and has requested
mutual review of removal handoffs plus the needs commit and initialization/update
contract. Next: review that implementation and add independent tests for need-state
continuity through boarding, ride removal and readmission. Do not race its core edits
or assume a Guest object survives readmission (identity does; the walking object is
recreated). Needs semantics must retain evidence/placeholder labels. If not ready,
advance independent audit coverage, not duplicate implementation.

## Audit-runner coverage gate follow-up

The matrix previously required availability coverage but could still accept a PASS
or known-red result if the new removal/crowd helpers were accidentally omitted.
Added minimum per-world coverage gates (30 availability, 57 removal, 20 conservation)
and explicit fixture/replay witnesses; manifests and console summaries now retain
all three counts. Missing/partial lifecycle coverage fails even on known-red worlds.

Regression first: five added test methods exposed seven failed subcases and one
missing-evidence-field error before the runner change. Afterward all 20 Python tests
pass. The original-disc matrix reports 30/57/20 for every world, full JUNGLE/FANTASY
pass and only the two unchanged retail failures elsewhere (runner exit 2). Local
artifacts: `tpw-coverage-before.log`, `tpw-lifecycle-coverage-final/manifest.json`.
No C# or renderer change in this follow-up.

Mutual review with cow tools: needs will live in a guest-ID side table, not on the
transient walking Guest object. Its retirement concern is valid integration work:
`Plans.Keys` is the existing coordinator identity census, includes Recovering, and
excludes retired entries after Step. Reconcile needs against it and initialize on
fresh arrival, not readmission. Recovery already tries any public open cell; when
no ground exists, indefinite explicit ownership is deliberate, not permission to
spawn on invalid ground. The audit counts those identities. Add retirement/ID-reuse
checks with the needs wiring when ready; no speculative core API change is needed
just to expose a second copy of that same identity list.

## Independent review: needs lifecycle integration (in progress)

Pulled cow tools' `5395ae3`. New local-only NeedsLifecycleChecks verifies 21 storage/
identity conditions: queued and seated continuity, ordinary completion, delayed
recovery, retirement and ID reuse, including stale-entry overwrite before another
step. Those pass with frozen chosen rates. Two independent clock controls FAIL on
that upstream revision: Step(0) ages needs, and one simulated second produces hunger
35 at 25 Hz versus 60 at 50 Hz from 10 with a controlled +1 increment.

Reported and shared the exact regression helper with cow tools; they own the core
clock/wiring correction. No competing ParkVisitors/VisitorNeeds edits by astraclaw.
The helper/invocation remain uncommitted pending that fix; they are not folded into
this launcher milestone. Evidence: `tpw-needs-before-JUNGLE.log`. Do not treat current
needs time integration as validated or ship the red helper to main without resolving
and rerunning it. This is a clock-contract failure, not a claim about retail rates.

## M6/M7 independent package: launcher retry and stale-build protection

While needs correction is in progress, implemented launcher-local failure recovery.
A persistent successful-build receipt records revision plus artifact hash and is
invalidated before checkout mutation; failed/partial builds cannot later become
launchable simply because a DLL exists. Retry dispatch now repeats the failed action,
launch errors remain visible, and busy state blocks duplicate entry/disc selection.
Malformed published hashes and too-short shape-check buffers reject without throwing.

26 synthetic LauncherAudit checks pass; the original hash/bounds regression had four
failures. Launcher compiles with zero errors. Two scoped source reviews covered the
failure scenarios; final review found no concrete issue. No real updater/Git reset
was executed. UI dispatch is source-reviewed/compiled, not UI-driver-tested, and
Windows update/relaunch is still a manual target-platform gate. Full details,
commands and limitations: `findings/launcher-recovery.md`.

Next: integrate the peer's needs clock correction and run the 23 independent lifecycle/
clock assertions in every world before publishing that helper. Continue independent
work if the correction is not ready; launcher follow-ups include bounded process
execution and interrupted clone handling, but do not silently broaden the current
receipt fix into a release-complete claim.

## Launcher follow-up: bounded process failures

Receipt/Retry implementation published as `18a90c6`. Continued independent work
rather than waiting idle for the needs clock correction. Added shared shell-free
`LauncherProcess` with bounded concurrent output draining, command deadlines, failed
launch/exit/timeout evidence, and best-effort timeout cleanup. MainWindow build/Git
paths use it; repository metadata rejects truncated output. Removed `.cmd` selection
from direct Windows executable lookup.

LauncherAudit now passes 32 checks, including actual harmless child-process fixtures
for quoting, dual-stream output pressure, exit failure, timeout, missing executable
and exceptional cleanup. Source review caught exceptional tree-termination handling
and unsupported batch-wrapper selection; both corrected. Launcher builds. See
`findings/launcher-recovery.md` for exact timeouts, cleanup limits and remaining
Windows/UI manual gates. Local evidence: `tpw-launcher-process-reviewed.log` and
`tpw-launcher-process-reviewed-build.log`.

Needs clock reproduction was also run in FANTASY/HALLOW/SPACE: each passes the same
21 storage checks and fails exactly the same two clock controls as JUNGLE. Local
helper still uncommitted; no core collision. Keep integrating the peer correction
when it arrives and proceed with independent plan items in the meantime.

## Needs peer integration completed for this package

Integrated cow tools' `07893c0` clock correction and thought-bubble implementation
without modifying its core/viewer files. The formerly local helper is now ready to
publish: 24 independent assertions pass per world, isolated and integrated (96 each).
The strengthened clock case uses a controlled .25-second period and 1.2 total seconds,
requiring four actual rises and exact equality at 25/50 Hz away from a period boundary.
The original one-second check alone would have been vacuous under the new 2.56-second
default, so it was deliberately strengthened rather than merely accepting green.

Matrix coverage now requires 30 availability, 57 removal, 20 conservation and 24
needs-lifecycle assertions per world. All pass; full JUNGLE/FANTASY pass, HALLOW/SPACE
retain only the exact historical retail findings, runner exit 2. All 20 Python matrix
tests pass with new missing-needs/partial-needs controls. Evidence:
`tpw-needs-final-07893c0/manifest.json`, `tpw-needs-final-isolated-{WORLD}.log`.
This closes the pending helper noted above, not all guest/needs/rendering work.

Additional M7 evidence: a separate fresh detached checkout of `34b050b` built all
22 tracked projects in Release successfully, using the existing NuGet cache. It
remained Git-clean. This is clean-checkout Linux aarch64 build evidence, not a clean
machine install or Windows runtime validation; subsequent `07893c0` was not included
in that pinned build sweep. Manifest: `tpw-clean-build-evidence-34b050b/manifest.json`.

Peer review of launcher receipt: a nonzero build already cannot record success, but
the original receipt hashes only the main DLL and would miss later changes to sibling
runtime dependencies. Next narrow package: reproduce that missing-dependency gate,
then record the managed output manifest rather than only the viewer assembly. Keep
Windows/manual launch gates explicit and preserve the peer's ongoing render work.

## Peer-review follow-up: launcher dependency manifest

Needs integration tests published as `6455861`, after a normal push rejection exposed
concurrent peer render commit `69e7eab`; rebased, reran the full matrix and 20 Python
tests, then pushed normally. No peer edits overwritten.

Cow tools' receipt review identified a coverage gap beyond the main viewer DLL.
Five added dependency-mutation controls reproduced it. Schema 2 now hashes the sorted
managed-output manifest (recursive DLLs plus dependency/runtime configuration JSON),
rejecting changed, missing and added runtime files. Legacy single-DLL receipts require
rebuilding; debug-symbol-only changes do not invalidate runtime output. LauncherAudit
passes 40 assertions and launcher builds. Evidence: `tpw-launcher-dependencies-before.log`,
`tpw-launcher-manifest-final.log`, `tpw-launcher-dependencies-build.log`.

No full Windows/runtime-attestation claim: engine installations, external plugins,
native non-DLL dependencies and concurrent installers remain outside this receipt.
Next independent package: make the README/current release checklist reflect the
verified source/build state instead of its early research-only snapshot, then resume
lifecycle long-stall/catch-up checks and renderer smoke in coordination with cow tools.
Need-clock tests currently validate normal frame rates/zero time, not large stalled
frames; do not silently claim the latter. Rendering/thought bubbles remain peer-owned.

## Current README and release-validation ledger

Updated README's research-only introduction and explicitly marked retained early format/open-
question/priorities sections historical. Added `findings/release-checklist.md` with exact tested
revisions, reproducible commands, expected retail reds, and outstanding Windows/manual/rendered
release gates. It distinguishes compile success from runtime/visual sign-off and storage
preservation from semantic decoding. No new game or launcher runtime behavior is claimed.

This documentation package follows the restored continuation path; recurring TPW work is now
bound to the original Discord request's actual poster/source, with assistant handoff context
separate. Project code was not changed by that harness repair.

Next: independent long-stall/catch-up measurements around the needs clock versus the capped
park/guest clocks. Establish observed behavior first; agree policy with the needs maintainer
before edits to its files. Continue coordinating rendered current-revision smoke and never
substitute earlier generated captures for present visual sign-off.

## Long-stall clock measurement — decision pending with needs maintainer

A scratch-only real-disc JUNGLE probe now demonstrates a timing-policy mismatch,
using a controlled .25-second rise period and deterministic +1 hunger from 10:

* One coordinator Step(10): offered 10s; ParkSim.Time=320ms, GuestWalk.Time=320ms,
  hunger=50.
* Eight Step(.04): offered .32s; both clocks=320ms, hunger=11.
* 250 Step(.04): offered 10s; both clocks=10000ms, hunger=50.

The existing park/walk catch-up cap deliberately discards excess time; needs currently
receives raw deltaSeconds. This establishes the difference, not a retail timing rule.
Shared the exact results with cow tools and proposed driving needs from actual park
clock advancement. Await that policy decision before changing its active core files.
No new red assertion was put on main and no existing clock check was weakened.

Reproduction artifact outside Git: `tpw-needs-clock-probe/probe.csproj` and Program.cs;
log `tpw-needs-clock-stall-evidence.log`. Uses the user's disc in place, no payload files.
The ordinary 24 needs assertions still cover storage, zero-time and normal frame rates,
not stalled-frame policy. While that decision is pending, continue independent release/
launcher validation rather than idle. Check recent channel messages before choosing
scope, and keep rendered needs-bubble work with its current maintainer.

## Long-stall clock correction verified with the peer

Cow tools confirmed the measured discrepancy was unintended and pushed `69faca2`.
Integrated it without competing core edits. The same probe now reports hunger 11 for
both a clipped ten-second frame and eight ordinary .04-second frames, with both park
and walking clocks at 320ms. Added three permanent assertions: the actual eight-tick
ceiling is exercised, needs follow consumed time, and longer ordinary runs still age.
All 27 needs assertions per world pass in the full matrix; runner and its 20 Python
tests updated for the new minimum. Known HALLOW/SPACE reds remain exact, runner exit 2.
Evidence: `tpw-needs-clock-stall-fixed.log`, `tpw-needs-stall-final-69faca2/manifest.json`.

Evidence correction from the maintainer: the 26-byte record is a per-level spawn
template, not temporal rise data. Rates/cadence and the needs-growth policy are port
inventions; these tests validate continuity/clock consistency, not retail growth values.

Independent launcher follow-up also reproduced four false-positive disc recognition
cases (suffix-only match, wrong folder, root-level name, directory named as an archive).
That separate narrow fix/test package is being validated; no disc-parser or viewer
changes are needed. Continue release/manual validation while respecting active renderer
ownership and the distinction between synthetic fixtures and real-disc verification.

## Launcher disc recognition corrected

Four synthetic false-positive controls failed before the fix: a misleading filename
suffix, correct basename in the wrong folder, correct basename at root, and a directory
named as the expected archive. DiscLocator now requires an exact case-insensitive
`/DATA/JUNGLE.WAD` non-directory entry, matching its documented predicate.

LauncherAudit passes 50 offline assertions and 51 with the optional real-disc-in-place
check; the existing owner image remains accepted. Launcher builds. Fixtures are
constructed directory metadata only, not copied disc payload or a claim of complete
ISO/archive validation. Evidence: `tpw-disc-recognition-before.log`,
`tpw-disc-recognition-final-offline.log`, `tpw-disc-recognition-final-real.log`.

A concurrent upstream needs-threshold commit (`c0946c4`) arrived before landing these
packages. Integrate it without overwriting the peer's source/findings, rerun the park
matrix and launcher audit, and only then push normally. Next independent release
validation should target actual launcher/UI error paths or a bounded scope agreed with
the renderer maintainer, not infer visual correctness from these data-layer gates.

## Actual launcher window event-path validation

Added `TPW.PS2.LauncherUiAudit` using the matching Avalonia.Headless 11.3.18 package.
It invokes the real MainWindow routed button handler under a headless dispatcher,
with controlled commands/processes and a unique temporary installation directory.
The production constructor retains real defaults; internal seams allow isolation.
Environment startup/discovery are explicitly blocked in the fixture so even an
accidental Retry-to-startup cannot touch real update/discovery paths.

22 assertions pass: build failure with emitted output, preserved failure/Retry,
refresh readiness, successful retry, throwing/null process start, artifact mutation,
actual window visibility/closure, exact launch arguments/disc environment, busy
controls, duplicate events, and awaited asynchronous completion/disposal.

Four deliberate broken variants were rejected (7/3/5/3 assertions respectively):
Retry dispatch removed, launch error overwritten, close on error, final receipt gate
bypassed. Original source restored; final green run completed. Two review passes
strengthened isolation, visibility and task-lifetime coverage. Evidence:
`tpw-launcher-ui-final.log`, `tpw-launcher-ui-mutations/manifest.json`.

This closes the previous source-only UI-event gap, not native Windows/visual/input/
self-update gates. No real Git, engine launch, HTTP update or disc lookup is executed
by the UI fixture. No renderer/needs core files touched. Update release matrix and
publish explicit paths after final builds and upstream reconciliation.

UI event audit published as `cab9bab`; final 22 headless window checks and 51 core/
real-disc checks passed, launcher built, and the worktree was clean at publication.
No source mutants remain. Next independent candidate: audit engine discovery, which
currently selects a Godot-named executable without checking whether it can run this
C# project. Confirm the intended version/mono compatibility policy from source and
primary engine evidence before tightening it; do not silently invent a patch-version
restriction. Continue sharing relevant findings with the renderer maintainer. Actual
Windows/manual gates remain blocked on that platform, not on more fake green tests.

## Engine discovery capability gate

Replaced filename-only engine selection with asynchronous bounded `--version` probing.
The old implementation's standard-build acceptance and console-directory-name bug
were reproduced against a copied original selector; the new one rejects/selects those
fixtures correctly. Discovery reports actual .NET/mono capability, preserves console
preference/fallback metadata, deduplicates candidates, caps probes at 16 and reports
when that cap prevents checking later candidates. Project target remains 4.6.2; no
new exact-patch compatibility gate was invented. Full project/engine compatibility
remains a separate real-launch check.

74 offline LauncherAudit assertions pass; 76 with the owner's disc and explicitly
selected installed engine. Its actual version output is 4.6.stable.mono.official.89cea1439.
22 headless window-event checks still pass. Primary engine documentation/source and
limits are in `findings/launcher-recovery.md`. Local evidence:
`tpw-engine-baseline-proof.log`, `tpw-engine-discovery-final.log`, `tpw-engine-ui-final.log`.

Integrated peer `01de816` (Litter field rename and semantic corrections) and reran all
27 needs assertions per world; full matrix retains only the two known retail findings.
This validates lifecycle regressions, not every newly decoded decision rule.

Next coordinated work: cow tools proposed legitimate ride-completion effects, and
correctly observed that a no-reseed invariant must allow specified effects. Waiting
for its hook/input contract before adjusting that suite. Expected shape agreed: effect
applies once, non-clamping sentinels catch doubles, cash/unaffected fields catch reseed,
random reductions are bounded, aborted removal differs from already-reported completion.
Do not use Queued as a waiting-only state: it includes seated riders. No core effect
wiring was pushed by the peer yet; don't race its files or guess the unknown scale factors.
Advance independent release validation while the contract is pending.

## Joint ride-effect consumer and no-reseed contract

Peer branch `needs-ride-effects` (`be4a40b`, locally cherry-picked as `0c0981c`) wires
Needs.Ride beside Rides++ in successful recovery. Main was left untouched while the
old byte-equality check was revised: real completion is allowed to change specified
fields, but not reseed a person. The old assertion reproduced exactly one expected red.

New controls pin non-clamping effect inputs and require the exact known deltas, preserve
all unaffected fields/Cash1234, bound the random reduction, and verify the walking ID/
Wandering plan. Completed-handback deletion, delayed ground recovery, repeat steps,
and aborted queued/seated removal are distinguished. 45 needs assertions/world pass
isolated and integrated. Five broken source variants were rejected (5/5/5/5/3 assertions):
missing effect, doubled effect, reseed, unconditional completion, Collect-only placement.
Original source restored; no mutants remain. Review strengthened explicit readmission
checks and noted that random bounds do not prove distribution/draw count.

The full park matrix retains only the exact two retail findings (exit 2); 20 runner
Python tests pass, viewer builds, 22 headless UI checks and 76 launcher/disc/engine
checks pass on the integrated tree. The engine capability package was previously
published as `131ae51`. Evidence: `tpw-ride-effects-old-invariant.log`,
`tpw-ride-effects-{WORLD}.log`, `tpw-ride-effect-mutations/manifest.json`,
`tpw-ride-effects-integrated/manifest.json`.

Pending final publication: fetch/reconcile, run the strengthened final helper, then
push the peer consumer plus audit commit together so main's new tip is green. Do not
mistake Queued for waiting or claim the chosen effect scales are decoded retail values.
A source-summary wording mismatch (Ride mentions toilet emptying while the agreed
contract leaves Toilet unchanged) was flagged for the needs maintainer, not changed
silently in its active file.

Joint consumer/audit publication completed: main tip `d02510b` includes peer consumer
`0c0981c` and the revised no-reseed/effect checks, pushed together after green integration.
No mutants or local core modifications remain. Next useful independent gate is an
actual current-revision Godot scene audit/rendered smoke using the installed engine,
recording reported engine version, compile target, exit/counts and any compatibility
failure. Do not infer project compatibility from the new --version capability check,
and do not claim screenshot visual inspection unless it actually occurs. Coordinate
with the renderer maintainer and leave its active source files untouched.

## Current Godot runtime gate and lighting audit correction

Ran the actual current project on installed Godot4.6.stable.mono.official.89cea1439,
compiled with SDK4.6.2. Visitor/RSE-animation/texture-animation/MTR scene audits passed
headlessly (including six visitor world/terrain cases and146 texture surface checks).
Lighting initially failed on CLIFFS raw normals in both headless and actual OpenGL;
the same failure existed at34b050b. Diagnosis was an audit selector error, not a renderer
normal change: #0 was treated as surface ordinal though names encode material indices.

Peer supplied SurfaceFor/Surfaces APIs in c32cb74. Integrated that change, selected the
already-verified triangle material via SurfaceFor, retained exact normal and pixel checks,
and added missing-material/diagnostic checks. The full lighting harness passes data,
Compatibility12pixels, Forward+12pixels, cull-off12, required directional-mutation
exit2 at the correct framebuffer mismatch, and restored12pixels. First artifact-path
attempt was refused because it was under Git ancestry; reran properly under /tmp,
without bypassing that guard.

Actual main JUNGLE viewer smoke also exited0: at60s 28boardings/8returns/20ride-owned/
8walking; at180s 68boardings/40returns/28ride-owned/0walking. Needs/bubble resource and
visibility instrumentation ran, and four1280x720 captures were generated. They are NOT
visually reviewed; Dummy audio is NOT an audible check. The walking-only “nobody in
park” log does not mean ride-owned guests vanished. Shared that distinction with peer.

Evidence/commands/limits: findings/runtime-smoke.md and appended findings/lighting.md;
local tpw-scene-regression-c32cb74/manifest.json, tpw-lighting-artifact-path.txt,
tpw-current-guest-smoke-path.txt. No images or extracted disc data go into Git.
Only LightingAudit source and docs changed here; renderer modifications remained with
its owner. Next: publish the verified gate, then choose an independent remaining audit/
release item while facility routing/want satisfaction stays coordinated with cow tools.

Peer follow-up `2802335` corrects the needs census label to distinguish WALKING guests
from identities off the walk. Integrated before publishing this audit/docs package;
recorded capture counts remain those of our c32cb74 run, not an assertion that separate
live captures have identical arrivals. No population-conservation assertion was changed.

## M4: audio observation and world-effect lifetime regression gate

Integrated peer fixes 1a5d2ab (Finished/position evidence), 4ca1a41 (elapsed diagnostic
window), 9acf578 (world sound reset) and 8889f2d (clear particle instances, retain library).
Our synthetic actual-Godot scene now passes37 checks. Before fixes, short/late playback
produced3 reds, frame-count deadline6, and missing particle reset1. Subagent review
strengthened actual voice identity, intermediate fading, different-tag isolation and
exception cleanup rather than accepting count-only assertions.

Seven temporary broken source variants were rejected with3/6/3/4/2/1/1 assertion
failures; restored production source rebuilt and37pass. Real JUNGLE60s census at4ca1a41
exits0 with22cues/22resolved/22started and no unresolved/silent voices. Dummy is not
an audible check. No disc assets extracted, no mutants retained, no core/renderer
production edits in this audit package. Full commands/limits: findings/audio-lifecycle.md.

Next independent scope: inventory advisor/UI integration against plan M6 and current
source, establish a narrow producer/scheduling/locale check if feasible; coordinate
with cow tools before touching its active Viewer/needs/effects files. Do not expand
this 37-check gate into a claim of full initialized world swaps or long-run memory
stability. Facility satisfaction, track services, manual/native-Windows gates and
known HALLOW/SPACE retail findings remain unfinished/explicit.

## M6: permanent advisor-browser callback/locale/playback gate

At production6aacf63, new AdvisorBrowserAudit builds actual browser controls, enters
them in a Godot scene and drives their real signals without Viewer._Ready/_Process.
45checks pass across EUR-English/French, USA-missingFrench, JAP-German and return to
English; exact text, lip metadata, selected-bank/slot PCM, live stream replacement,
retail missinglip defect, ordinary-bank state clearing and active teardown are covered.
Six mutation controls fail3/1/1/1/5/4; original Viewer restored/rebuilt,45pass. Existing
AdvisorAudit passes all regional identity/negative checks. No production viewer changes.
Read-only subagent review strengthened exact text and lifecycle controls. Full evidence
and explicit limitations: findings/advisor-browser-audit.md. No assets extracted.

Source census confirms AdvisorRules/LipTrack.Playback/BitmapFont/KanjiTable still lack
game consumers. Do not disguise catalogue browsing as VM scheduling, Japanese glyph
rendering or animated lips; producers/day-rate integration remain unresolved.

Next bounded independent item: malformed-input/whole-table validation for
TextDatabase.ParseTable. Current parser reads count/offsets before checking bounds
and treats a missing terminating NUL as EOF; reproduce with small synthetic inputs,
then make a narrow fail-explicitly reader fix if confirmed. Existing AdvisorAudit
already independently checks exact contiguous strings and full-byte consumption for
all regional real tables, so retain that real-disc gate. Coordinate scope with peer;
leave its Viewer/needs/effects files untouched. No guessed gameplay semantics or
new advisor scheduling implementation is authorized by merely calling the VM decoded.

## Compiled-text parser: malformed inputs now fail explicitly

Reproduced21 failures in61 initial synthetic checks:12 malformed inputs silently
accepted,9 incidental exception types. ParseTable now bounds count before allocation,
requires contiguous valid offsets/NULs and exact whole-buffer consumption, preserving
Latin1 bytes, empty rows and whitespace. No Load missing-file policy change.
Final73 checks pass including literal multibyte-offset fixtures and bounded-allocation
controls. Six mutations fail4/2/3/1/3/13; originals restored. Subagent review found no
parser correctness issues and contributed boundary fixtures. Existing28 regional
compiled-table checks and full AdvisorAudit stay green; rebuilt browser45/audio37 pass.
Evidence/reproduction: findings/text-table-validation.md. No disc extraction.

Also applied peer feedback to AdvisorBrowserAudit reflection diagnostics: renamed
members now identify themselves. A deliberately renamed lookup exits2 with the named
MissingMemberException, and the original audit source/build is restored. Its UI-parent
boundary caveat is documented rather than hidden.

Next bounded independent M7 item: consolidate the existing headless-safe Godot scene
audits into a reproducible runtime runner/manifest with unit-tested failure handling,
fresh build by default, explicit engine/disc paths, named PASS/exit/error checks and
source/build provenance. Leave LightingAudit's real framebuffer gate separate: never
label a headless run a pixel pass. No scene behavior/Viewer/needs/particle changes,
no screenshots or proprietary asset output required. This makes the new permanent
runtime gates actually easy to run together rather than relying on remembered commands.

## M7: reproducible six-scene headless runtime runner

Added tools/runtime_audit.py and27 runtime unit controls (47 combined with20 matrix
controls). Always fresh Debug Rebuild, explicit mono engine/disc paths, sanitized TPW
environment, named PASS/minimum coverage, zero exit plus clean error/leak output.
Default six-scene gate passes actual Godot4.6/Dummy: visitor6cases,RSE6cases,texture146+
clock,MTR4witnesses,browser45,audio37. Lighting framebuffer remains a separate real-render gate.

Review fixed provenance gaps: ignored source files can compile and must be hashed;
linked tools sources are included; actual executing runner/helper recorded separately
from --repo; all output DLL/deps/runtimeconfig files hashed; before/after drift invalidates
success. Advisor/audio logging gains assertion IDs so repeated lines cannot inflate
coverage. Successful subsets have full_gate_passed=false, distinct from all_six_passed.
No production Viewer/needs/effects changes. Existing matrix helper's optional explicit
environment is backward-compatible; its log cap still bounds reads, not disk writes.

Peer text-table feedback also applied: offset errors name shared/reordered/padded
layouts outside the checked profile rather than implying every rejection is corruption.
Evidence/limits/reproduction: findings/runtime-audit-runner.md; project scratch pointer
tpw-runtime-evidence-path.txt, runtime unit log. Retain exact known park-matrix reds.

Next bounded M7 item: test the current published source from a NEW detached clean
worktree with this runtime runner, no copied disc/assets or inherited Godot build cache.
Use installed toolchain and existing NuGet cache, and disclose that cache reuse rather
than calling it a cold/offline install. Inspect before creating/removing anything;
do not delete worktrees or artifacts destructively without confirmation. If clean
source cannot build/run, reproduce and fix only the packaging/build issue, coordinating
before touching peer-owned files. No bot restarts or harness work.

## M7: genuinely clean published-source checkout gate

Created detached tpw-release-check-6004151 at exact published6004151. Initial Git status
clean and no bin/obj/.godot directories. No project/Godot cache or disc assets copied.
Actual installed SDK is9.0.119 targetingnet8; Godot4.6mono and projectSDK4.6.2. Existing
NuGet cache reused, so do not call this a cold-machine or offline installation.

Fresh Debug runner: all six scenes pass. All23 tracked projects then build Release.
Clean checkout also passes47 Python tests, launcher76, headless launcher UI22 and full
AdvisorAudit including73textchecks. Four-world matrix has30availability/57removal/
20conservation/45needs per world: JUNGLE/FANTASY pass, exact HALLOW/SPACE retail reds
remain raw1/runner2. Final tree Git-clean. No packaging/build bug found; no production
fix or cleanup/restart performed. Detached worktree/evidence retained.

Updated release checklist with exact6004151 evidence and retained earlier lighting/
rendered/DBA baselines rather than pretending they were rerun. See findings/clean-checkout-
6004151.md and scratch tpw-clean-6004151-result.json plus its referenced manifests/logs.

Next independent M3 validation: inspect existing ConservationChecks/ride-removal helpers
and extend deterministic long-run disruption/replay coverage where a concrete invariant
is missing. Prefer audit-local fixtures and real scripts, preserving exact guest identity,
plans/owners/walking/recovering accounting, with replay controls. Do not invent console
queue/needs semantics or edit peer-owned Viewer/needs/effects wiring. If coverage already
exists, choose a different genuine gap rather than duplicating tests. Coordinate any
production finding before modifying shared core files. Current manual/native-Windows/
listening and unsupported gameplay gates remain explicit, not declared complete.

## M3: repeated disruption/identity/needs replay

Added audit-local GuestDisruptionChecks, reusing the existing ownership census and
observed-state snapshot. Three independent instances of one real script,16 identities,
12 cycles of close/break/occupied removal/immediate-ID replacement/Clear/no-ground/
restore/redispatch. Review strengthened phase-local recovery/occupancy/handback checks,
monotonic counters, and late corruption failing exactly at the injected sample.

21new checks/world pass. Unaltered replay samples: JUNGLE4968,FANTASY5848,HALLOW5468,
SPACE5072; two identical replays match, altered-delta digest differs (clocks included,
not claimed as full discrete-outcome sensitivity). Each phase restores all16 identities.
Four temporary ParkVisitors mutants (numeric-only owner, aborted completion, late
counter reset, late needs reseed) all fail the new gate; original source restored.
No production bug found or core change landed. A fixed-wait fixture assumption failed
on longer routes; corrected it to wait for actual arrival per GuestWalk.Send's intended
mid-edge refusal, independently confirmed by peer. No production rule weakened.

Final matrix:30availability/57removal/20conservation/45needs/21disruption per world;
JUNGLE/FANTASY pass, exact HALLOW/SPACE reds remain raw1/runner2. Matrix now requires
new coverage+witnesses;49 combined Python tests pass. Evidence and limitations:
findings/guest-disruption-replay.md and scratch tpw-disruption-matrix-path.txt/
tpw-disruption-mutations/manifest.json. Audit files only; no Viewer/needs/effects edits.

Next independent scope: inspect existing GuestAudit route/topology checks before
expanding them; focus on explicit movement contracts (mid-edge retarget refusal,
NoRoute versus Stranded, destination changes and rerouting) using independent small
graph controls if gaps exist. Do not duplicate covered tests or invent retail walking
speed/AI. Coordinate any actual core finding before changes. Facility/track/advisor
integration and native/manual release gates remain separate unfinished work.

## CP0/CP3 review — 2026-09-24 UTC: adjust the plan, not just the test count

Owner explicitly requested review/adjustment checkpoints and continued coordination
with cow tools. Updated plan.md with CP0–CP6 triggers, decision outcomes and a required
review record. Reviews now happen before package selection, after implementation,
after two audit-only/no-new-defect packages, when evidence/dependencies change, after
player-visible integration, and before release/platform claims.

Baseline:699a9b1. Current existing GuestAudit was built and run on JUNGLE/FANTASY/
HALLOW/SPACE: all exit0, before any proposed new walking-contract code. The recent
extended conservation pass found a fixture timing assumption, not a core lifecycle
bug. Core movement policy was clarified with the maintainer: committed edges finish;
a deleted final destination can still be Arrived, and explicit retargeting can leave
missing ground. This is port policy, not decoded console behavior. No new movement
test/helper or production edit was started in this turn.

Decision: DEFER the previously proposed standalone walking-contract audit expansion.
Maintain the existing gates and redirect toward a missing end-to-end gameplay/UI
consumer. The fresh direct-call census still finds no game/core callers of needs
Buy/UseToilet/WantsToGoHome/Queue, nor game consumers of AdvisorRules/LipTrack.Playback/
BitmapFont/KanjiTable. Tests around those readers do not implement those features.

Next action: coordinate active feature/file ownership with cow tools (asked in channel),
then inspect actual service/placement/needs interfaces read-only and agree a CP1 package.
Candidate is one needs-satisfaction flow, with implementation and independent regression/
consumer validation split between us; not yet claimed as accepted or implemented.
If blocked, record the specific dependency and choose an already-supported integration
outcome together. Do not fill the wait with another unmotivated audit family or guess
missing console semantics. Next review triggers when scope/ownership/evidence is known,
and again at that package's implementation/integration gate. Existing manual, native-
Windows, audible and unknown-format gates remain open.

Walking baseline evidence: task scratch tpw-walk-baseline/manifest.json and world logs.
This entry supersedes the earlier next-action instruction to expand GuestAudit.

### CP1 draft update: peer scope confirmed, placement approval still pending

Cow tools confirms needs-satisfaction consumers are its scope and proposes one toilet
as the smallest feature (no purchase/economy dependency). Agreed provisional file split:
peer owns ParkVisitors/VisitorNeeds/viewer implementation; astraclaw independent validation.
Requested strawberry's confirmation for placement/routing scope, respecting the peer's
stated dependency; no facility code or approval is claimed. Added the draft acceptance
contract and explicit no-scope-creep boundary to plan.md.

Read existing UseToilet: it clears Toilet and returns max(0,(oldToilet-60)*2/3) by the
current integer expression. Current Decide still bool-gates single-need thoughts.
Peer's new threshold/errand-code executable correction must be recorded/verified with
consumer provenance; it is not a reason to pretend current source already changed.
Next bounded action is interface/evidence preparation with peer, not placement edits
or speculative integration tests against an invented API. Freeze other needs/rates in
validation, preserve identity/cash in transit, require actual arrival and exact once-only
facility soil accounting, retain unreachable/cancellation behavior, and separate model
thought state from actual rendered bubble inspection. CP1 completes on scope/interface
agreement; CP2/CP5 follow implementation and the real player flow.

### CP1 preflight: authored small-toilet data confirmed; consumer seam identified

Read-only probe at58fbd88 independently found exactly two ProvidesRelief=1 definitions
per world. All four small toilets are shape2, choosable, with model/APS/script and authored
fractional entry/exit positions. Their parsed scripts have no LIMBO/WALK; supers use LIMBO,
and FANTASY royaloo additionally has WALK operations and no own/folder APS. Checked the
current resolver's own-stem/unique-folder fallback and StartScript's animation requirement.
This is a code/data compatibility question, not a newly declared retail defect.

Peer took its placement check: code-side footprint/category/entrance support confirmed,
actual in-park placement still unconfirmed. Read findings/visitors.md's outside-service
versus LIMBO evidence and limits. Found a concrete integration seam: Deliver removes
all handed-over walkers, but PlaceActors retains only walking/seated/scripted-WALK bodies.
A standing small-toilet user therefore needs an intentional visible service-body path;
shared this code-side risk with peer, without claiming a runtime disappearance was seen.

Updated plan preflight and findings/toilet-slice-preflight.md. No production edits or
permanent audit project. Temporary in-memory metadata probe emitted no raw assets.
Scope confirmation and concrete registration/completion/soil/presentation API remain
pending; do not invent them or land placement/routing code. Next useful step is peer's
runtime-placement evidence and CP1 scope/interface agreement, then independent tests
for that agreed slice. This bounded preflight is complete: do not repeat its census or
fill a dependency wait with generic audits. Check new messages; absent new evidence or
approval, keep the dependency explicit without periodic duplicate progress messages.

## CP1 correction / CP2 active: resume under standing authorization, no courtesy hold

VoX's unmentioned02:33 message challenged the unnecessary stop; the later explicit
restart/resume requests reaffirm execution. The approval hold was our over-deferral,
not a real product blocker. Do NOT keep waiting on that stale note. Listen-all is now
the bot default (own-code435f989,345tests2skipped), restart completed and verified; no
further bot restart needed. Recent history was checked to recover the missed context.

TPW pulled753a4f1 then73be234. Peer implemented service definitions/routing/Serve, fixed
unreachable-nearest fallback and thought-gating after review, and exposed authoritative
QueuedOwner(guest). Peer explicitly handed Viewer.cs to astraclaw; core remains theirs.
Current worktree WIP (NOT landed): game/StandingServicePose.cs, game/Viewer.cs narrow
standing-body integration, game/tests/StandingServiceAudit.cs/.tscn. A real Godot actor
probe reproduced disappearance at handover (one assertion red); the initial fix retains
the same actor (four assertions green). It is a real PlaceActors/coordinator scene probe,
not yet a physical placement or full visual sign-off. Production placement registers
immutable authored small-toilet geometry/turns against the actual ride instance.

Continue this WIP: complete four-rotation authored-point, active service, visibility/
seat/WALK precedence, handback/needs/cash and removal/reused-ID checks; include the
standing guest in thought/bubble presentation without fabricating limbo visibility.
Check actual placement path using the existing cursor override if practical. No main
push until relevant gates/mutations/peer review pass. Need a permanent unreachable-
nearest routing control in a separate audit helper (peer supplied discriminating shape:
closer disconnected toilet + farther reachable toilet + distracting rides; assert no
wrong ride first, not merely eventual relief). Leave peer ServiceChecks/core untouched.
Local logs tpw-standing-before.log and tpw-standing-after.log record red/initial green.

## CP2/CP5: standing small-toilet flow implemented and tested

Supersedes the WIP/approval-pending notes above. Integrated upstream through c7eb67f,
including peer departure and queue effects; preserved their files. Viewer now registers
immutable authored small-relief geometry against actual ParkRide instances, keeps waiting
and accepted customers drawn, respects host hide/seat/WALK ownership, and anchors thoughts
after actual transforms. RideHandlesSprite is metadata, NOT a port visibility switch.

StandingServiceAudit:70 checks green with real JUNGLE assets/coordinator/renderer methods.
Actual PlaceHeld (cursor override) creates model/script/stub and completes a real customer
service at all4 rotations. Tests cover body/legs, orientation, needs/cash, soil, bubbles,
hiding, handback, removal/ID reuse. Unready Viewer/audit stage and Dummy audio are explicit;
no normal-startup, screenshot inspection, listening or native-platform sign-off claimed.
Five production mutations rejected against earlier62-check suite; later8 checks add actual
body/legs and inward-facing witnesses. Read-only subagent found no concrete production bug.

Separate ServiceRoutingChecks:5/world, with a truly nearer disconnected toilet, reachable
farther toilet and distracting attraction. Nearest-only mutation causes3 failures; restored
all4worlds pass. Existing known HALLOW/SPACE retail failures remain raw1/matrix2, not green.
Runner now requires routing coverage; seven-scene runtime gate requires70 numbered standing
checks and named real-placement/visibility/ownership witnesses.51 Python tests pass.
Evidence: /tmp/tpw-standing-runtime-20260924b and /tmp/tpw-standing-parks-20260924; these runs
precede the final matrix minimum/docs edits, so final landing will rerun current runners.
Mutation logs in project scratch tpw-standing-mutant-*.log and tpw-service-routing-mutant.log.

Next: finish peer review / final current-tree gates, explicit-path commit and normal push.
Then normal-viewer rendered/manual smoke when inspectable and review peer departure/queue
integration. No restart, no permission hold, no repeated metadata census. Active recurring
owner continuation already exists; do not add another. Core/ServiceChecks remain peer-owned.

Final current-tree landing gates: /tmp/tpw-standing-runtime-final-20260924 has all7
scenes passing (standing70); /tmp/tpw-standing-parks-final-20260924 records5 routing
checks/world with only the two exact known retail reds;51 Python tests pass. A focused
production/test patch was sent to cow tools for peer review. No unrelated source changed.

## Landed6fedf61; peer phase review and departure-recovery regression WIP

6fedf61 is pushed to main. Cow independently reviewed the 1x1/no-LIMBO partition and
inward-facing derivation, requested a phase control against actual placement stubs.
Added4 checks (standing total74): stand point must be nearer its real stub than its
reflection through footprint centre. All4 fail with row mirroring removed; restored
selected-standing gate passes at /tmp/tpw-standing-phase-restored-20260924. Runner74
minimum/named witnesses and docs are local WIP; no new all-seven claim for this addition.

A narrow read-only review of peer dc67e2b/c7eb67f found a candidate Leaving/Stranded
recovery bug. Reproduced it independently in ALL4 worlds:6 checks, first4 green and last2
red. Departing guest is conserved while a route is broken; after restoration a real route
exists but WentHome remains0 even after6000 ticks, needs/ownership remain. Idle only
retries departure for Arrived guests, not Stranded Leaving guests. Peer has been asked to
own the core fix; do NOT edit their ParkVisitors file concurrently.

Local WIP: new tools/TPW.PS2.ParkSimAudit/DepartureRecoveryChecks.cs plus ONE Program.cs
call before ServiceRoutingChecks. This intentionally makes current ParkSimAudit RED;
not pushed. Logs tpw-departure-recovery-before.log and -FANTASY/-HALLOW/-SPACE.log.
Standing phase additions and runner tests are green separately (51 Python tests).
Next: peer core retry fix, verify6 recovery checks/world + existing retail reds, then
land regression/phase followup with explicit paths. If peer is unavailable do independent
normal-viewer smoke preparation, not a fake-green waiver or a courtesy-permission pause.
No restarts. Existing owner continuation remains active; do not duplicate it.

## Departure recovery resolved / peer phase follow-up ready to land

Peer0618434 arrived and was fast-forwarded without touching their core edits. The first
fixture over-pinned the transient walk state: its Stranded assertion failed precisely
because the fix retries within Step. Corrected the observable contract instead: after
actual movement, no route/no WentHome, one guest+needs retained; after repair, reach home
and retire together. Restoring the actual6fedf61 pre-fix ParkVisitors source still fails
exactly the two post-repair assertions (tpw-departure-contract-mutant.log); restored source
passes6/world. No fake-green waiver and no requirement to remain in the stuck state.

Final gates: /tmp/tpw-phase-departure-runtime-20260924 all7 pass, standing74;
/tmp/tpw-phase-departure-matrix-20260924 six recovery + five routing/world, only exact
known retail reds remain;52 Python tests. Updated matrix requires departure coverage.
See findings/departure-recovery.md and standing-service.md peer-phase follow-up.

Next useful player-flow work: normal Viewer._Ready/rendered service smoke, not more generic
headless census. Existing --guest-test deliberately places Crazy Ape via the Rides category,
so --guest-ride=Small Toilet cannot select a feature and is NOT a valid service smoke.
A separate test driver can create a real Viewer, let normal initialization run, use the
existing Features build callbacks/cursor override, and frame the actual standing customer.
Do not call an uninspected PNG visual proof; direct inspection/manual sign-off is still open.
Core needs/visitor files remain peer-owned; no restart or duplicate continuation needed.

## Normal-startup rendered service flow + direct crop inspection

Added game/tests/StandingServiceSmoke.cs/.tscn (capture driver, not part of the headless
seven-scene default). It invokes normal Viewer._Ready with its real tree, Features
arm/place and path callbacks; controlled production ticks route and serve a seeded
customer. Four worlds passed this flow and wrote serving/after1280x720 captures.
JUNGLE subsequently also passed actual texture-byte binding to tbtoilet and default-pose
projection/capture. Viewer._Process is intentionally stopped after startup; this is not
an unmodified real-time/input-latency test. Dummy audio; no extraction/raw asset files.

Important visual-access correction: tool-posted images appear to this model as paths,
but another sender's reattached images reach the image input. Cow reattached crops and
I directly inspected them: full body in doorway during service; out on path after,
doorway empty. Small icon ambiguous/crowded hair; revised crop after547fba2 clearly WC
figures with clearance. VoX also recognized it when zoomed in. Only those crops reviewed,
not all full frames/worlds. Cow corrected its mask: opaque16x13 before,34x31 after, not23px.
Independent disc review confirmed real id7->tbtoilet entry and alpha layout; final live
sprite pixels equal the managed decode. Do not claim independent full RGB validation.

New real measurement corrects a second mistaken comment: GameCamera.Build divides by
TileUnits256. Default Above2576->10.0625 Godot units, Behind1760->6.875; not2576 tiles up.
Actual normal pose, panned onto serving guest, FOV75/dolly0 projects the full sprite quad
11.46px. Crop tuning doesn't solve this. Owner explicitly said no approval waits; human
testing goes at the end. Agreed provisional port choice48–64px readable range with head
clearance; cow owns bubble code, I own projection/capture checks. No guessed retail claim.

Upstream integrated through be8e0cb, including1121263 unmet-need effects/read constants,
547fba2 bubble tuning and subsequent comment corrections. Gates at this integrated tree:
/tmp/tpw-smoke-integration-runtime-20260924 all7pass/standing74;
/tmp/tpw-smoke-integration-matrix-20260924 only the two exact known retail reds, routing5
and recovery6/world. Smoke evidence: /tmp/tpw-service-smoke-20260924 (first JUNGLE),
/tmp/tpw-service-smoke-worlds-20260924 (other3), /tmp/tpw-service-smoke-bubble-20260924,
/tmp/tpw-service-smoke-final-20260924 (texture binding), /tmp/tpw-service-camera-20260924
(default pose). All remain outside Git. PNGs posted only as generated scene captures.

Next: land capture driver/docs with explicit paths, then integrate peer readability policy
on a tested branch if it changes position contracts; update StandingServiceAudit's actual
bubble expectations rather than weakening them. Capture close-up/default pose with source,
camera and texture-bound measurements; use reattached crops for direct inspection. Keep
working while human review waits, no owner pings for already-authorized choices, no restart.

## CP5 re-evaluation: reject the provisional screen clamp, retain world scaling

Peer6b0ff1f inspected the default-pose capture: reports opaque cloud10x8px and hut roughly
32x45px. A52px cloud would be wider than the building. Our provisional48–64px target is
SUPERSEDED, not pending implementation. Agreed to retain world scaling with indicator-far /
readable-near behavior, explicitly port policy. Other UI designs remain possible; geometry
does not uniquely prove this one or decode retail rendering. Peer removed its unshipped
solver rather than leave a dormant feature. No approval stall and no more sizing loop.

Updated plan/findings accordingly. Next independent bounded task after landing the smoke:
review recently changed needs-effect constants and consumer arithmetic against executable
instructions (especially signed/rounding/clamping edges); do not edit peer core concurrently
or assume a bug from a formula comment. Preserve current all-seven and known-red matrix
results; keep crowding/DPI/zoom/platform questions for the final human checklist.

Capture-driver landing gate rerun at6b0ff1f plus this driver: all7 runtime scenes pass
(standing74) in /tmp/tpw-smoke-landing-runtime-20260924;52 Python tests pass. The earlier
/tmp/tpw-smoke-integration-matrix-20260924 covers the same core1121263 integration with
only the exact known retail reds; intervening bubble commits are comment-only. No images
or asset bytes staged. Smoke methods stay outside the headless default scene set.

## Ride-effect consumer gate mismatch reproduced (WIP, not pushed)

13f6980 integrated cleanly: peer now stores ParkRide.Condition and exposes Soil as
100-Condition for live relief facilities. Do not treat that view as unbounded cumulative
soil or invent a cleaning consumer; core remains peer-owned.

Read-only delegate checked FUN_0020EDD8 directly in original SLES_500.32, in memory from
the disc. Initial concern was negative fixed-point rounding; the real finding is a
missing gate. 0x20F248: slti v0,s0,56; 0x20F24C: bne v0,zero,0x20F28C skips the entire
sickness block when ride value<56. Thus gentle rides DO NOT reduce sickness through this
consumer. Negative-rounding concern is unreachable; do NOT fix it with floor alone.
0x20F258 subtracts30; 0x20F25C loads1212; 0x20F264 mult, 0x20F268 sll12, 0x20F26C sra24;
positive result added/clamped at100. Boredom4096 uses its own block outside sickness gate.
Image constants1212/4096 independently verified; callback value sourcing is still separate.

New local RideEffectConsumerChecks.cs has24 checks, invoked beside service helpers in
ParkSimAudit Program.cs. Literal cases cover0,26,29,30,45,55,56,57,58,100 and upper clamp;
other effects must still happen below56. JUNGLE baseline at13f6980 has EXACTLY4 reds:
value0 Sick20->12 instead20;26 Sick1->0 instead1;45 Sick20->24 instead20;55 Sick20->27
instead20. Others pass. Log tpw-ride-effect-consumer-before.log. No core changes made.

Local NeedsLifecycleChecks fixture moved from intensity40 to60 (above the real gate),
Sick sentinel71->61 and expected boredom42->32. Final sickness stays76, happiness90,
cash1234. A double effect remains non-clamping (Sick91, happiness97, boredom2), preserving
exactly-once discrimination. After that fixture correction only the4 new consumer reds
remain; lifecycle45 checks still pass. Do not weaken existing continuity expectations.

Peer was asked to own the conditional sickness guard and correct the gentle-ride comment.
If existing main's below-gate lifecycle expectation goes red, stage peer change on a branch
until this test update lands with it; never push a known-red integration. Next resume:
check peer response/upstream, verify24 checks and existing45/world, run mutation controls
for missing/off-by-one gate and accidentally gated whole effect, then current seven-scene
and four-world gates with known retail reds unchanged. Add runner required coverage/docs,
explicit-path commit/push only once green. Existing owner recurrence remains; no restart.

## Ride-effect branch integrated and verified; pending landing

Fetched/fast-forwarded peer ride-sickness-gate2027a5b then0ce3bf7 in the isolated branch;
main remains13f6980 until our green integration lands. A second independent disc review
confirmed absolute mismatch and happiness bands0..20=>15,21..50=>10,51+=>5, plus the
sickness>=56 gate. It rejected a doc overclaim: first8 preference records verified does
NOT prove original table length; getter has no bounds check. Peer corrected that in
0ce3bf7. I corrected remaining stale comments (globals are read; default45 added4 sickness,
not a cure; RideHappiness is fallback, not always flat15). No extra production logic edits.

RideEffectConsumerChecks now33: original24 plus9 explicit nonzero-preference/band-edge
cases with fallback7 deliberately unequal to15/10/5. NeedsLifecycleChecks explicitly
sets preference0 for legacy configurable-effect fixtures; a second genuine completed /
readmitted case sets preference90 and verifies middle-band happy93, Sick76, boredom32,
cash1234, preference continuity and no double payout. Total needs lifecycle50. Disruption
ledger includes PreferredIntensity. Matrix requires33 consumer/50 lifecycle and named
witnesses;53 Python tests pass.

8 production mutations rejected: missing gate(4 targeted failures), gate55(1), gate57(2),
whole-effect gate(11), flat happiness(10), lower-band edge(2), upper-band edge(2), missing
absolute mismatch(3). All source restored. Logs tpw-ride-effect-mutant-*.log and JSON
summaries in scratch; the local source backup is tpw-ride-effect-mutations-original.cs.
TPW.PS2.Check on real disc exits0. Full final functional gates:
/tmp/tpw-ride-consumer-matrix-20260924 has all4worlds'33/50 checks and only exact known
retail reds; /tmp/tpw-ride-consumer-runtime-20260924 all7pass, standing74.

Next: verify branch/main haven't moved, commit explicit audit/doc paths (core edits are
comment-only atop peer logic), push normal main and notify peer. Then choose the next
bounded consumer gap from the updated plan—not another generic census. Per-ride value
sourcing and original personality assignment remain genuine gaps; do not pretend constants
alone implement them. No restart, no duplicate recurring task, no permission hold.

## a78ada8 landed; producer trace corrected and next priority is compiled shop data

Main a78ada8 includes peer0ce3bf7 plus our green consumer/lifecycle integration; clean
worktree after push. Ordinary per-ride producer trace then exposed a classification error:
first delegate labeled table3683B8/target1D2A68 ordinary and shifted its positive square8.
Second narrow trace + my direct raw-word check disproved both. It is sideshow-backed;
initializer loads payload+2E prize into complete+A8, +2C price into+CC, +30 win% into+CE.
Instruction00028103 at1D2AD0 shifts4. I explicitly corrected the peer in-channel.
Repeated+A8 offsets in facility/service structs do not establish service-decay here.

Details now findings/ride-value-producer.md. Table35A560's value callback returns0 and
constructor1188B0 installs it, but a base-constructor install is NOT final ordinary-instance
dispatch proof. Do not wire raw SAM excitement or sideshow math into ordinary rides.
Next narrow trace, if resumed: one known kind3 asset creation to final vtable, then its
1D0/1D4 callback; initializer slot150/154 for35A560 is0/295160. Avoid guessing from nearby
string constants or absent direct JAL references. No producer code changed.

Direct all3regional DbaAudit --list run passed original goldens. ARC2X3 key247 has
price10/prize30/win33/base0 in all3, despite old SAM omissions/75; resource initialization
is proven without guessing SAM defaults. Metadata-only output, no binary asset extraction.
Further named rows saved in scratch tpw-compiled-shop-identity-records.tsv (12 rows):
Balloon239, VampShop153, FatFairy66, Droid388, happiness10 all3. Droid price/cost50/35;
others45/30. Full identity matters; identical effect tuple is not a key.

Peer87bc690 reports real shop mismatch: Balloon/VampShop/Droid SAM happiness15 vs compiled10.
I am NOT implementing the join; peer was asked to own runtime wiring, I own independent
identity/region/consumer tests. Do not write against an invented API before reading theirs.
Known icecream regional controls: keys245/151/69/391, EUR/JAP hunger25/vomit10 vs USA15/15.
Warning sent: 20E450 also scales happiness by shop quality; raw compiled10 isn't proof of
final retail payout10. Join fidelity and chosen neutral/default quality are separate gates.

Next resume: fetch current main/peer branch and latest messages, inspect compiled-shop API
and identity/region/fallback contract, establish red-before/green-after tests on named assets,
preserve peer edits, then matrix/runtime gates and normal pushes. Ordinary producer stays
explicitly unresolved, not a reason to idle. No approval waits, no restart, no duplicate job.

## Compiled shop join f96dde8 integrated; actual viewer regression is RED

Peer wired core/TPW.PS2.Data/CompiledAssets.cs: constructor(database,text), For(world,path)
returns full Entry, Report reports coverage, Ambiguous exposes duplicate symbolic identities.
RideDefinition.Compiled is ShopSettings; price/cost/hunger/thirst/vomit/happiness getters
prefer it, otherwise authored values. Viewer.AttachCompiledRecords loads /arsdb.dba and
uses _text; caller currently explicitly selects EUR, not a viewer-configurable region.
Peer CompiledJoinChecks covers helper lookup only; its filtered-disagreeing.All assertion
cannot establish that actual viewer definitions got attached.

Found concrete ordering defect: Viewer.IndexRides calls AttachCompiledRecords before
_cat.AddWad. Only one caller exists. Notified peer to move it after population; keep
Viewer/core edits with peer. New LOCAL game/tests/CompiledShopViewerAudit.cs/.tscn calls
actual IndexRides then DefinitionFor for Balloon/VampShop/FatFairy/Droid, plus world reindex.
It has18 numbered checks and reproduces10 failures atf96dde8: logs0shopsjoined in every
world; correct names/authored controls pass, compiled attachment/getters fail. Log
../tpw-compiled-viewer-before.log, build ../tpw-compiled-viewer-build.log. No production
fix by me. This scene is NOT yet in runtime_audit.py and must not be called green.

Next resume: latest channel/upstream ordering fix, rerun18checks before/after; add core
named-record checks across EUR/USA/JAP incl four icecream counterexamples and missing/
ambiguous lookup policy. Distinguish duplicate numeric DBA keys from duplicate symbolic
identities—the original numeric first-match rule alone doesn't prove symbolic-collision
semantics. Existing map keeps first and reports Ambiguous, but runtime caller does not
currently surface that property. Verify/report before changing peer core. Consider actual
purchase consumer controls, not just equivalent getters, and retain explicit unscaled
quality default. Register new viewer scene in the default runner once green, with named
coverage/mutation controls; run full gates and land explicit paths.

## Viewer ordering fix verified; new default eighth scene ready to land

Peer d6f4e75 arrived during a normal-push race. Fetched and rebased our docs commit safely
(no force push; original3b5955c became6bde6b4). Producer-trace docs remain intact. d6 moves
Attach after AddWad and shares CompiledAssets.Attach between runtime/helper; empty input
reports NOTHING TO JOIN and both Source path shapes are handled.

Our actual IndexRides/DefinitionFor scene now passes18/18 after failing10/18 atf96.
3 restored-source mutation controls reject ordering(10 actual failed checks), authored-
first happiness(4), and retaining the old catalogue(1). Logs tpw-compiled-viewer-mutant-*.log;
source restored by finally. New shops scene registered as the DEFAULT eighth runtime
scene with numbered coverage, named4world/reindex witnesses and matching summary.54 Python
tests pass. /tmp/tpw-compiled-viewer-runtime-20260924: all8pass; shops18, standing74.
/tmp/tpw-compiled-viewer-matrix-20260924: only exact known retail reds, existing33 consumer/
50 needs lifecycle/6 departure/5 routing coverage retained. No production edits by me here.

Next after landing this green scene: independent core identity/region and actual purchase
controls for CompiledAssets/Definition getters, with named expected keys (239/153/66/388)
and real icecream region differences (245/151/69/391). Inspect current Attach/For ambiguity
and fallback semantics rather than assume the API; do not conflate duplicate numeric keys
with duplicate symbolic identities. Coordinate any core change with peer. No restart,
no human-approval wait, and existing recurring owner task remains the only continuation.

## CP4/CP2 — product-aware compiled purchases, joint candidate ready for review

Integrated peer purchase-arms through371e56d (latest changes are comment corrections).
The compiled join's real consumer exposed tenfold undercharging and wrong transfer signs.
A first universal thirst/litter correction broke drinks/trinkets; staging on a branch let
independent controls catch that before joint landing. Costume's array-length guard was a
no-op; product7 was misread as a standalone arm but actually prefixes q2/15 then falls through
into food. Original instructions independently confirm row8 intensity14 and the fall-through.
Original setup explicitly writes q1=100/q2=0, so base payouts at those INITIAL settings are
verified; mutable quality, costume actors, repeated balloon ownership, willingness and park
accounting remain incomplete. Findings are now compiled-shop-consumer.md, not only chat.

Our CompiledShopPurchaseChecks has65 assertions/world:21 per EUR/USA/JAP plus2 identical-input
selector controls. Named IDs/full identities and actual RSE/APS handbacks cover balloon,
icecream, drink, fries and costume; affordability299/300 is exercised after routing and stops
at physical handback, not a Purchases counter that could wait for repeated visits. All4worlds
pass these65. Nine restored-source mutants are rejected by this helper itself: bare price18,
food thirst sign7, drink-as-food4, balloon litter3, costume no-op3, fries omission3, missing
affordability3, strict affordability3, ignored product9. All65 assertions completed in each
mutant run; source/restored build clean. Local logs ../tpw-purchase-mutations/.

Fresh matrix /tmp/tpw-purchase-matrix-20260924: JUNGLE/FANTASY pass, only exact HALLOW/SPACE
retail reds remain (raw1, runner2), with new65 check minimum and named witnesses. Fresh Debug
runtime /tmp/tpw-purchase-runtime-20260924: all8scenes pass. Whole-disc TPW.PS2.Check exits0
(log ../tpw-purchase-full-check.log).56 Python runner tests pass. Gates ran on3583a06 plus our
patch;371e56d is comment-only. No new rendered shop proof claimed.

Peer review requested; files were initially attached because peer is on a different host.
Owner now asks code sharing by commit/push: publish this candidate on a Git review branch,
then incorporate peer findings and land normally once green. Never assume another agent can
read this worktree. Main remainsa5e0e83 until actual landing; do not claim published-to-main.

Next bounded action after landing: reuse normal Viewer startup/placement/tick smoke for a
named shop; inspect actual guest presentation/ownership as well as its purchase. Current
StandingServicePose is intentionally 1x1 ProvidesRelief-only; this fact alone does NOT prove
shops invisible (LIMBO/scripted poses may legitimately own them). Inspect/reproduce before
changing renderer or widening the pose helper. Coordinate core/viewer scope with cow tools,
keep our work in tests/validation until agreed. No more generic purchase-audit expansion;
no restart, human-approval wait or duplicate continuation job.

## CP2 complete — purchase integration published to main47281a6

Published candidate9de13d6 on astraclaw/purchase-consumers per owner's request to share code
through Git, not attachments. Peer independently merged/tested all4worlds and cross-mutated
its implementation: our checks reject selector10, bare debit18 and food sign7 failures.
Review identified source-path coverage coupled to region selection. Definitions now use a
consistent source shape; two separate bare/archive attachment checks run outside the region
loop. Final helper count67/world. A bare-world-split mutation fails exactly its one check
while all67 run; source restored. This is tenth mutation in the package, after nine on the
initial65. Product7 default tests cannot distinguish its zero q2 prefix from food; doc now
says explicitly that original instruction evidence establishes the prefix/fall-through.

Final matrix /tmp/tpw-purchase-final-matrix-20260924: all67/world, only exact known retail reds.
56 runner tests pass; earlier fresh eight-scene runtime and whole-disc gates remain valid
(peer follow-up comment-only; our review edits helper/runner/docs, not production). Debug
ParkSimAudit rebuilt after restoring the final source-path mutation, so no mutant DLL is left
as its normal output. Joint landing includes peer purchase-arms through371e56d. Normal push
succeeded; git ls-remote confirmed BOTH main and astraclaw/purchase-consumers at
47281a6b044c1a27b49b767d47618fdbd8bd8de4. Peer review and actual tests, not local commit alone.

Process correction: the first attempt to append this final documentation had a Python quoting
SyntaxError; its surrounding shell did not stop, so the reviewed code/tests still committed
and pushed successfully but the doc append did not. Inspected status/output, then repaired this
documentation separately with a fail-fast shell. Do not claim that failed append was published.

Next bounded package announced to peer: reuse normal Viewer startup/placement/tick smoke for
a named SHOP and verify guest presentation/ownership plus real purchase. Current standing
pose helper is intentionally1x1 ProvidesRelief-only; that alone does NOT prove shops invisible
because LIMBO/scripted poses may legitimately own them. Inspect/reproduce before widening it.
Cow tools is on a DIFFERENT HOST; share code via pushed review branches, not local paths or
code attachments. Coordinate production/viewer ownership; our initial work is tests/validation.
Do not re-run generic purchase censuses or reopen ordinary-ride producer work without a reason.
No restart, no approval wait, existing recurring task only. Report project milestones to
strawberry1028383794362847362. Disc stays in place; no raw assets in Git.

## CP5 — external shop body reproduced; narrow fix on review branch

After purchase landing, baseline6d65a89 normal Viewer startup + actual Shops construction
reproduced IceCream body1→0→1 through accepted service/handback. No actual host-hide/seat/WALK
applied. Real purchase succeeded, so this is precisely a renderer gap headless arithmetic
cannot see. New optional ShopServiceSmoke scene was pushed atba7bdea on
astraclaw/shop-presentation (not main). First fixture also wrongly froze Thought; corrected
to require preserved seed plus actual Hungry presentation, leaving one genuine body failure.
Logs ../tpw-shop-smoke-baseline-v2.log; captures /tmp/tpw-shop-smoke-baseline-v2-20260924.

Production change only expands StandingServicePose to authored2x2Sells, alongside1x1relief;
Viewer edits are explanatory comments. Six coordinate-less shops stay unsupported, larger
LIMBO/ordinary rides excluded, real host ownership gates untouched. StandingServiceAudit now89
with15 literal geometry/negative controls; runtime runner names all new witnesses.56 Python
checks pass. First fixed rendered runs: JUNGLE all4turns plusHALLOW/SPACE firstfittingturn,
each one visible body during acceptance, correct transaction and exactly one returning walker.
Captures /tmp/tpw-shop-smoke-fixed-20260924; no visual review claimed yet.

Independent source review found two real test gaps, now tightened: a single non-head mesh
could count legs-only as full body, and projected visibility alone did not assert the live
shop actor's authored transform. The smoke now requires torso+legs, one actor, literal
per-world position, placement-derived height/facing and upright basis. Final JUNGLEturn0
passes (../tpw-shop-smoke-final.log); otherfinalruns/fullruntimegate pending at this entry.
Share source by pushed branch per owner; captures may be attached for peer visual inspection.
Next finish peer review/final gates and land normally; no restart or duplicate scheduler.

## CP4 — peer image review found shared bad frame; fixed on review branch

Peer approved guard/source and JUNGLE visibility, but rejected HALLOW's floating-in-void
capture. Its initial SPACE alignment approval was subsequently retracted. Independent
instrument: compare the guest-space expected point with actual park.CellCorner interpolation.
JUNGLEdelta.0003, HALLOW114.4984, SPACE10.0002. So the earlier successful H/S smoke was
body/transaction evidence, not correct placement. SPACE's visible paving was not enough
in-frame ground truth. Current smoke's actual-floor assertion fails on old GuestWorld:
../tpw-shop-frame-red.log, one genuine frame assertion failure, exit2.

Root cause: GuestWorld used raw ParkPaths.Origin/overlay bounds rather than the built
park's authored transform. The production fix now interpolates CellCorner; GuestHeading
maps directions through the same frame, keeping bodies upright/unscaled. No world constants.
Legacy fallback for contexts without a built authored plot is unchanged. New literal
synthetic transformed-plot/origin-change controls bring StandingServiceAudit to93;56 Python
checks pass. Final JUNGLE/HALLOW/SPACE rendered runs pass actual floor alignment, actor
ownership, body+legs, counter effects and handback; logs ../tpw-shop-frame-fixed-*.log.
Captures /tmp/tpw-shop-frame-fixed-20260924 include optional magenta actual-model-origin
landmarks (--shop-markers). These markers are diagnostic overlays, not gameplay artwork.

Still to do before landing this frame extension: peer reinspection/new source review,
fresh full8scene gate after GuestWorld change, rerun rotatedJUNGLE and FANTASY normal toilet
smoke (its icecream remains coordinate-less). Earlier8scene/four-turn results predate this
frame fix. Main untouched at6d65a89; work remains on astraclaw/shop-presentation. Do not claim
HALLOW/SPACE's old images were alignment evidence. No restart/duplicate scheduler/approval wait.

## CP2/CP5 complete — authored shop bodies and real guest frame ready to land

Production candidate c213a4c reviewed independently: no actionable regression; current retail
plot construction is canonical+X/-Z, while synthetic affine checks qualify guest mapping,
NOT arbitrary transformed placement. /tmp/tpw-frame-final-runtime-20260924 fresh8scene gate
passes, standing93. Final rotatedJUNGLE1/2/3 and normal-startup FANTASY toilet pass; J0/HALLOW/
SPACE already passed final actual-floor shop checks.56 Python tests pass. Two deliberate
mutations of the built-plot branches reject POSITION (2 point/origin assertions) and HEADING
(2 axes); summary line is not an extra assertion. Source restored and game Debug rebuilt.
Logs ../tpw-frame-mutation-*.log and ../tpw-frame-restored-build.log.

Peer confirmed JUNGLE/SPACE visually with actual-model-origin markers. HALLOW's corrected
front view was camera-occluded, not evidence against the transform. Added test-only
--shop-camera=front/high/left/right; high/left images at SAME shop/customer with the SAME
floor-alignment check show grounded counter service. Peer confirmed obstruction from a nearby
sign/stone structure; no outstanding visual doubt for this bounded patch. Original bad and
obstructed captures retained. These are peer image inspections; do not claim a native/input/
full-gameplay-camera test. FANTASY instrumented toilet smoke is not a coordinate-less shop fix.

Next after normal landing: six coordinate-less external shops remain the actual missing
consumer. Cow tools owns tracing their defaults; coordinate directly and share CODE by Git
branches, renders by attachments. Do not repeat finished purchase/body/coordinate censuses.
If defaults are not established, choose and label a conservative visible-service fallback
rather than invent an authored position or leave the project idle; validate actual ownership,
visibility and handback. Keep source tracing bounded to what unlocks those six customers.
No restart, no extra scheduler, no approval wait. Existing owner continuation remains active.

## CP1/CP2 — coordinate-less arrival-stub policy candidate

Main6fa5c5d confirmed. Peer's default trace did not establish a console default; numeric
pattern searches/key strings are not proof of compiled absence or a live parser call path.
Agreed bounded policy: keep customers at the actual arrival stub, explicitly port behavior,
without authoring fake SAM values. No continuing parser hunt blocks these six customers.

Before fix, new normal-startup --shop-target=coconut fixture reproduced body1→0→1 while its
real drink purchase succeeded; one missing-body assertion red, exit2. Log
../tpw-coordinate-less-before.log, captures /tmp/tpw-coordinate-less-before-20260924.
New TryCreateAtEntryStub/IsEntryStubFallback is separate from authored-only TryCreate. Viewer
registers authored first and holds fallback at actual plan.At under existing instance/hide/
seat/WALK guards. Partial/malformed values, larger shops and missing entry remain excluded.

Fixed Coconut passes real visibility/arrival/floor/purchase/handback; earlier all-six rendered
runs and authoredJUNGLEIceCream control pass in /tmp/tpw-coordinate-less-fixed-20260924.
Those six predate the tightened final live-facing assertion. Headless metadata31 + actual
Viewer/RSE fallback consumer8 bring defaultStandingServiceAudit to132, passing. Source review
caught fenced-block keys being mistaken for absence; now both Fields and Blocks are checked,
with parsed-input negatives. Runner now requires all8consumer witnesses;57 Python checks pass.
Initial classifier negative accidentally deleted assertion IDs; corrected to preserve IDs
and change only the witness, so it really tests coverage rather than ID validation.

Current candidate needs Git peer review, deliberate no-registration/block-guard mutations,
and final fresh8scene + six named rendered reruns after review-tightening. Only comment/name
cleanup followed the earlier6renderpass; no final-source six-run claim yet. Share code via
review branch, not paths/attachments; screenshots may be attached. No restart/new scheduler.

## CP2 complete — entry-stub automated gates and policy evidence

Candidate f956fe4 is pushed on astraclaw/entry-stub-service. Independent source review's
fenced-block malformed-input issue and missing-consumer coverage gate are resolved. The
review requested live facing checks; these now pass in all named normal-startup runs.
Final /tmp/tpw-entry-stub-final-runtime-20260924: all8scenespass, standing132.57 Python
runner tests pass. /tmp/tpw-coordinate-less-final-20260924: JUNGLECoconut, FANTASYburger/
fries/icecream, SPACEburger/fries all pass, plus still-authoredJUNGLEIceCream control.
Log summary ../tpw-entry-stub-final-gates.result is runtime=0 rendered=0. No raw assets copied.

Three deliberate restored-source failures: missingfallbackregistration3 actualconsumerchecks;
blocks-treated-as-absence2 parsed-inputchecks; inventedcounteroffset1 actualarrival-position
check. All132assertions completed each, exit2; source and Debug build restored. Logs
../tpw-entry-stub-mutant-*.log. Metadata-only coverage could NOT hide dead viewer wiring.
Representative images and Git branch shared with cow tools; image response still pending
at this entry, so don't fabricate peer visual approval. Owner requested final human testing
at the end; this labelled best-effort policy is not claimed as a decoded console default.

After normal landing, run a CP6 integration/build checkpoint rather than expand this policy
into an open-ended audit project: current all-project clean-source evidence is much older
than the integrated renderer/needs changes. Use a separate clean detached checkout, no disc
or extracted assets in Git, existing toolchain/cache (not a clean-machine claim). Rebuild
tracked projects and retain actual failures/known retail reds. Coordinate the next playable
feature with cow tools after that checkpoint; outstanding costume/carried-object/queue/track
limits are not closed by these service-body changes. No bot restart or new scheduler.

## Entry-stub peer approval and upstream integration

Peer reviewed f956fe4's guards/policy and Coconut/FANTASYIceCream images. SPACEFries' initial
camera was cyan-obstructed, not visual proof. Two same-placement alternative views retain
all floor/arrival/facing/handback checks; the LEFT view clearly shows the customer, shop and
marker. Peer approves; high remains obscured by a tube. Keep the observable distance from
some counters labelled as arrival-stub policy, not a newly decoded console default.
Captures /tmp/tpw-space-fries-views-20260924; logs ../tpw-space-fries-view-*.log.

Upstream main advanced to626058a (peer flag/water work). Rebased without code conflicts,
then preserved the already-published candidate ancestry with a non-force merge. The only
conflicts were two docs: verified our sides were exact prefixes plus final evidence, preserved
both, and verified the resulting tree equals the pre-merge tree. Integrated branch65b7370
is pushed; game build and alternative rendered SPACEFries runs pass on that integrated tree.
No force push, no overwriting peer Viewer changes.

CP6 clean-source checkpoint STARTED, do not duplicate: detached worktree ../tpw-clean-65b7370
at65b7370, initially Git-clean with no bin/obj/game/.godot. Script ../tpw-clean-build-65b7370.py
is building all23 tracked projects serially in Release, existing Linux toolchain/NuGet cache.
Background PID597398; output ../tpw-clean-build-65b7370.log and
/tmp/tpw-clean-build-65b7370/manifest.json. Check actual completion/results before claims.
This is NOT clean-machine/native-Windows proof. After build, run appropriate clean-tree gates
and record the checkpoint; preserve known HALLOW/SPACE retail reds. No restart/new scheduler.

## User-priority CP4 — repeated visits and missing window walk; native trace completed

Strawberry08:01 requested ACTUAL game-code investigation of apparent stub trapping/repeated
shop visits and absent walking-to-window; latest08:09 explicitly rejects speculative fixes.
This takes priority over finishing CP6 runtime sweep. Clean23projectRelease build at65b7370
has completed ALL_PASSED true, finalGitclean, no failures; retain that pinned result, don't
promote it to later main revisions without rerun. Manifest /tmp/tpw-clean-build-65b7370/.

Three bounded read-only executable traces produced findings/native-shop-flow.md. Concrete
original paths: shopapproach commonwalking→compiledINSIDEentrancecellcentre, notoutsidepath
stub and notRSEWALKON; unrotatedIceCream/Coconut native(0,0) vsportoutside(0,-1). Native service
uses arrival/state35→22 and purchase; handbackretainsposition. IndexedSAMschema/loaderfound,
butthisloaderparsesanddoesNOTretain fourstand/appearancefractions—do notcall our authored
renderpoints provenretailwindowcoordinates. Actualoriginalwalkpermissionintobuildingcell
remainsuntraced; never open entirefootprint asshortcut.

Nativecompletion writes60+rand60 and300+rand300 deadlines BEFOREshopBuy; refusalreturnignored.
State0selection randomarm0 compares now>stored+60+rand300; plusrealweightedselector/history
penalties exist. Recent-ID writer isconditional/oddforwardcopy, NOTgenericFIFO. Originalclock
is1C4930/u32[397644], +1perupdateproducer; wall-time cadence NOTproven, don'tpretend40msport
clockprovesPS2seconds. No affordabilityfilter found in actualresolvedSHOPselector, as opposed
tomere wantsetterabsence. Moodwork remainspeer's separate scope; it isn't the sole explanation.

PortprobeusingREALcompiledIceCream/RSE, frozenneeds andwanderoption demonstrates bothcases:
rich1234 completespurchase H91→66 cash934, broke299refuses unchanged; BOTH atWalk.Time4160
alreadyHeadingtosameshop. One Step(0) advancesBoardings1→2 andsameQueuedOwner withclockunchanged.
Artifacts ../tpw-native-shop-probe and ../tpw-native-shop-repeat-probe.log. Notneeds-rategrowth,
notrefusal-only, notwrongkindfixture. No production fix shipped yet for these newlytraced gaps.
Next: sharethisGittrace withpeer; implementbounded nativecounter/decision scheduling and
compiledinside-entrywalking with controlledterminal permission, or finishmissingpermission/
clockproducertrace first. Preserve explicitunknowns, don't replace with guessed cashfilters,
SAMwindowcoords or arbitrary cooldownseconds. Peer owns mood/zones; coordinate ParkVisitors
scope before edits. Main currentlya6f5828 locally; fetch before implementation/landing because
peer reports692f2e9 moodchanges. No bot restart/new scheduler.

## CP4 timing candidate — September24 09:28 UTC

0d25bf8 implements the post-completion gate; merged peer05b5bbc as4d053e8. Needs clock
accessor merged cleanly; peer retains mood/idles/money UI scope. DecisionSchedulingChecks
25/world use real compiled IceCream, successful1234/refused299 cash, same-counter calls,
strict boundary, eventual revisit and retirement. Four deliberate mutants rejected:
registration8 failures, missing consumer8, >=5, same-tick retry2. Sources restored/buildgood.
Matrix /tmp/tpw-decision-gate-matrix-20260924: JUNGLE/FANTASY pass; HALLOW/SPACE only exact
known retail reds; all25 new checks everyworld. Runtime /tmp/tpw-decision-runtime-20260924:
all8scenes pass. Python classifier58tests pass; gate count/witnesses required explicitly.

Further original traces now in findings/native-shop-flow.md: conditional2VBLANK/default
1gametick-render pacing (not absolute25Hz promise), +6C entertainer gate, and actual
kind7 directional destination-only entrance permission. Movement remains next: public
stub -> compiled INSIDE entrance centre through common GuestWalk, no globally opening
footprints and no visual-only lerp. Native handback retainsinsideposition. Preserve
liveowner, mid-edge, demolition/replacement and egress invariants. Do not call current
SAMfractions/fallbackstub positions decoded native window endpoints. They are superseded
only when actual movement consumer is wired and checked. No movement fix shipped yet.

Timing peer review resolved before landing: sole clock is now executed ParkSim.Time ticks,
NOT adjustable VisitorNeeds.UpdateTicks. Removed the unused accessor. Both successful/refused
fixtures change appetite cadence8x while waiting and preserve the same deadline. Clock-coupling
mutation rejects both rate controls. Full matrix /tmp/tpw-decision-clock-final-20260924 retains
only the exact two retail reds;25scheduling checks/world. Peer approved lifecycle/zero-time
boundary; requested this clock correction. Shared injected RNG and native unsigned comparison
retained, not split/replaced for test convenience. Movement candidate is temporarily in Git
stash 'compiled terminal movement candidate pending timing clock review'; restore after timing
landing. It is NOT discarded or shipped: real approach/egress tests pass, rendered JUNGLE smoke
passes, but review/mutations/otherworldrendered checks remain before movement landing.

Timing LANDED mainc0ee884 with peer moneyUIce87955 preserved. Final integrated4world matrix
/tmp/tpw-decision-money-merged-20260924 and8runtime /tmp/tpw-decision-money-runtime-20260924
pass except exacttwoexpectedretailreds. No arbitrary permanent shop blacklist added.

Movement candidate restored from stash and now wired end-to-end (not yet main): compiled
DBA entry geometry, GuestTerminal directed permission, actual GuestWalk final edge, held
inside-position during service, same-position handback, physical reverse egress. Source
permission only for matching live owner; demolition retains escape, same-ID replacement
cannot inherit. 90checks/world in /tmp/tpw-terminal-final-matrix-20260924; all expected outcomes.
Four core mutations fail: stub consumer8, permission45, handback teleport4, replacement1
(these counts predate15extra demolition assertions). Runtime8pass at
/tmp/tpw-terminal-runtime-v2-20260924; Python59pass. An earlier runtime attempt failed build
(missing Aps alias in a new smoke assertion), recorded, fixed; NOT a passing runtime run.

Rendered normalstartup earlier8runs pass acrossJUNGLE4rotations/HALLOW/SPACE/FANTASY/Coconut
in /tmp/tpw-terminal-smoke-20260924. New five-frame smoke also verifies actual walk record AND
changing leg vertices while moving through the last edge, not merely translating a bind pose.
JUNGLE pass in ../tpw-terminal-fiveframe-jungle-v2.log, images
/tmp/tpw-terminal-final-captures/jungle-v2*.png. Initial added animation assertion wrongly used
LastWorld (hierarchy track diagnostic absent for this skeletal path); it failed24times. Read
sampler, replaced with actual Record + mesh motion checks, no renderer workaround. Failed
captures preserved. Need peer review, remaining rendered controls/mutations, then land movement
only after fresh merge/tests. Tests/code shared by pushed branch, images may be attached.

Movement peer review at765138e: directed-entry guards and literal rotation oracle accepted.
Image review distinguishes visible displacement/bubble clearing from alignment; requested
numeric per-capture positions. Added logs: actorMinusPhysical=(0,0,0) throughout; relative to
independent placed/compiled entry, approach at480/1000 is+0.520004, service0, departure480 is
+0.480003 (worldZ; X drift<0.000002). Normalstartup log ../tpw-terminal-positions.log.
Render mutations /tmp/tpw-terminal-render-mutations: restoring SAM fractional jump fails only
service endpoint; freeze gait atframe0 fails actual leg-mesh animation. Both restored.
Final five-frame/actual-animation runs allpass: JUNGLE4rotations,HALLOW,SPACE,FANTASY,Coconut;
/tmp/tpw-terminal-reviewed-captures plus JUNGLE../tpw-terminal-fiveframe-jungle-v2.log.
Plan revised: prioritize traced movement/selection over release sweeps; next investigate actual
native weighted destination scorer/conditional history, not arbitrary blacklists or more generic
service tests. Other facility kinds' entrance consumers still need their own trace.

Movement LANDED main54a58af after explicit peer no-objections09:59. All90terminalchecks/world
on54a58af: /tmp/tpw-terminal-landing-matrix-20260924, onlyexacttwoexpectedretailfailures.
All8runtime scenes /tmp/tpw-terminal-landing-runtime-20260924,59Pythonchecks pass. Six deliberate
mutations total (fourcore,twoactualrendered) rejected; restored source clean before push.
No outstanding ownership collision, no further restarts, no duplicate scheduled task. Next
work is fidelity of destination selection / other facility-kind approach traces per revised
plan, NOT redoing the now-landed timing/SHOP entrance fixes. Coordinate scope with cow tools.

## CP4 native score / final producers / relief correction — September24 after10:00

No pause/completion request. Main946a89a code clean; three read-only native traces completed,
with parent independent raw-word/vtable/lookup verification against original image SHA256.
No disc/executable/asset extraction. This package changes findings/plan, NOT gameplay yet.

NEW findings/native-destination-score.md contains implementable20C138 equation, literal
121wordneedtable and22wordrelieftable, SHOPspecialization, signed/unsigned/truncation rules,
strict thresholds, weighteddenominator13+E+BW, actualequal-score chooser behavior and history.
History INITIALIZATION20BF38..50 resetsFFFFFFFF on every guestactivation/reuse. Writer20C8D8
really maps[a,b,c,d]->[new,a,a,a], only atselection when signedToilet>=99 andkind!=2; no FIFO
repair permitted. IDs are runtimeobjectserialsP+0C via1093B0, not reused numericrideIDs/DBAkeys.
Full candidate enumeration ordering remains tobe traced before claiming wholechooserparity.

Updated ride-value-producer.md: final ordinary366330->-8/1B82D0 proven through actualpool
constructor replacingbase35A560; feature35DC70->relief130780 compiledbyte2Ebit0; coaster35B060,
tour369F10,track36BBF0 concretefamilyallocatorsresolved. OperatingS=P+E8,D=P+F0; Q12speed/100,
duration/5 (coasterno/5), clips3072..5120, product>>12, baseproduct>>12 cap100. Trackadds cached
u8C+1D1/2 afterbasezeroearlyout. Defaultsetting116120 is midpointspeed, max(1,maxDuration/2).
Do NOTwirecurrentVAR_DURATION1 asnativeD: forwarding118310->1FA858->1C0E28 writesvariableindex3;
speed118240->1FA818->1C1068 writessignedhalfwordmachine+C0. Finalmanagedbinding/scalingunresolved.

CRITICAL correctionnative-shop-flow.md + visitors.md: kind2FEATURE takes SAMEinsideAcentre
approach/directedkind7terminal asSHOP. Acceptedrelief then unconditionallyhides throughguest
virtual2C at20D784..790, setsdeadline now+522 (raw2442020A), state35;20E160 strictnow>deadline
advancesto22;20EDD8reveals/relieves/clearsoccupancy and retainsinsideposition. NoRSELIMBO does
NOTimplyvisiblecustomer; priorstandingSmallToiletjustificationretracted, peeracknowledged.
130AA0->1FAD40 invokes1ABC80 channel0slot5variant1onentry/0exit forresourcekind2. Availability
1309F8 isconditionalresourcepredicate, NOTalwaysC+AC==0; portmustlabelanysimplification.
No native relief state integration landed yet; currentRSEadapterstillcontrolsservice/body.

Parentverified all121+22documentedtablewords againstdisc, thresholds50/55,9literal lookup
controls,6decisive instructionwords; separatelyreadfinalvtablepairs, hideforwarder192C10,
speed/durationbridges andresourceanimationnotification. No codebuild rerunclaimed fordocs-only.
Nextboundedproductionpackageperupdatedplan: actualnative relief lifecycle+insidewalk, notmore
script-basedvisibilityinference. Keep the remaining bridge/enumeration questions bounded. Coordinate peerwhoownsmoney/economy/idle.


## Urgent residence follow-up / bus queued — September24 10:55 onward

User explicitly queued bus research1552630912481624105 (strawberry1028383794362847362,
10:40:56UTC) for aftercurrentwork; acknowledged afterreliefintegration,beforefurtherscorer.
Subsequent10:55shop/toiletcycling report takespriority overnewreliefconsumer. No pause.
Reliefimplementation NOT started; priorresearchfindings remain, so do not claim native522
service/hide is shipped. CowownsnewvisitorSFX, looping/scenerySFX, bubbleanchor and backwards
featureanimations. Ourworkingbase pulled e574dbd; peerreportsdeee27e/edbf263later,fetchbeforeland.

First broad delegate timedout600s with no result; narrow read succeeded:35handler20E160
returns todispatcherepilogue afterstate22write;22completionnextdispatch, notsameinvocation.
Third traceplusparentwordread resolvesnewactualbug: state0arm1ordinarymovement is NOT gated
bydestinationdeadline. Ourbroad Idle earlycontinue was wrong. Native1->5->1913B8localplanner;
portstillusesexistingpublicWander+BFS,but nowonlyselectionarm0obeysdeadline.

Redfirst reproduction on e574dbd: realrelief,successfulIceCream1234,refusedIceCream299.
120executedticks,allzeroStepsatservicecell. Threegenuine assertionfailures, notbuild-failedgreen.
New PostServiceMovementChecks18/world plus3puregates(DecisionScheduling28) nowpass.
Controlledarm1walkstwopublicedges,thenarm0cannotbypassremainingdeadline or buy/playvisitagain.
One fixturecorrection: initiallycountedALLsoundsasvisits; legitimatehappysound129duringwalk
madeitred. Nowcounts51/53/208visitcues separately and logsallsoundIDs; no productionmute.

Normalstartup ShopServiceSmoke --shop-auto-depart keepsshopOPEN andissuesNOexplicitSend.
All4worlds pass /tmp/tpw-idle-auto-captures (plusframeposition andactualleganimationchecks).
Existing8runtime /tmp/tpw-idle-arm-runtime-20260924 pass. Corematrix
/tmp/tpw-idle-arm-matrix-20260924 onlyexacttwoexpectedretailreds;60Pythonpass.
Mutations /tmp/tpw-idle-arm-mutations: restoreglobalgate=>3corefailsANDactualrenderedauto-
departurefails; Forgetdeadlineonmovement=>7fails. Allrestored/rebuilt. Sourcesnotyetlanded.
Next:pushreviewbranch,mergepeerupdates,retest,landboundedgatefix. Thenactualnative relief
consumer, thenrequestedbusresearch. Do NOT claimallscenery/rideSFXrepeats sharethiscause.

Idle-movement peerreviewd3accb0: noobjections; checked stateArrived guardpreventsrepathjitter,
unusedrand300stillconsumed, occupiedterminalclearsatapproach, deadlinepersistswhilemoving.
Mergedpeer d79eac5 as30a1e24; corematrix /tmp/tpw-idle-merged-matrix-20260924 expectedonlyretailreds.
Runtime7scenespass, audioaudittestthrowsafter24checks becausepeerremovedVoice.OnEnd callback.
Updated AUDIT ONLY toassertactualplayerstop/owner-tagretirement/fade instead ofdeletedendclip
callback; namedreflectionerrorsnowidentifyremovedmember. SyntheticloopPCM isalifetimefixture,
notanassertionaboutretailADDOBJbanks. Adapted35checks pass /tmp/tpw-idle-audio-adapted-20260924;
removingKilltagfilterfails2checks /tmp/tpw-audio-adapted-scope-mutation. Source restored.
Peernew14b8bc2 repeatsoundflag/timer policy isseparate, correlationnotyetnativeconsumerproof;
warnedpeer thatrepeatdeadlines mustfollowEXECUTEDsimtime, notrenderdelta, while diagnostic
voiceobservation staysrealtime. Do not claimthatourresidencefixsolvesbin/speakerlooping.


Residence correction LANDED main0f7c689, peer reviewed, preserving23b2944 bubble/audio/feature
work. Final4worldmatrix /tmp/tpw-idle-final-matrix-20260924:28decision,18postservicemovement,
90terminalchecks/world;onlyexacttwoexpectedretailfailures. All8runtime scenes
/tmp/tpw-idle-final-runtime-20260924 pass;60Pythonchecks. Finalmergednormalstartupautomatic
JUNGLEdeparture (open shop, no explicit Send) /tmp/tpw-idle-landing-auto and
../tpw-idle-landing-auto.log passes. Earlierall4worldauto runs /tmp/tpw-idle-auto-captures.
Upstream14b8bc2 speaker-census usedJUNGLE236..239 everywhere, producing3NEWworldfailures;
heldlanding,reportedtopeer,mergedtheir23b2944correction; didNOTwaiveasretailfailure.
User-facingreport explicitlylimitsfix tooverbroadgate, not a permanent anti-revisit rule.

NEXT: currentnative relief phase remainsUNIMPLEMENTED (insidewalk,hide,strict522deadline,
next-dispatchcompletion,reveal,sameposition,resource slot5variant1/0). Busresearch queued by
strawberry1552630912481624105 immediately aftercurrentwork, beforefurtherscorer. Peer isactive
onsounds/bubbles/featurecreation; stayoffthoseexceptagreedservicevisibilityconsumer. Bus research
should include actualsoundeventconsumer/flags/timer rather thanrepeat any prior guessedstage
interpretations; peer'srepeatflag0x400/word0C policyislabelledcorrelation, notnativeconsumerread.
Do not repeat completedtiming/movement work orrestartbot; existingcroncontinuesoriginalplan.

## Native selector review slice — September24 12:27 UTC

Separate worktree ../tpw-gate-occupancy now branch astraclaw/native-destination-scoring,
base126dc80 (name is historical; NO gate changes). Owner handed gate task to cow.
Implemented GuestDestinationScore plus43 arithmetic/chooser checks, verified against
all121 need table words and legal relief bins read in memory from owner's ELF.
/tmp/tpw-score-restored.log PASS43, /tmp/tpw-score-mutations-20260924 all8 breaks caught;
restored source rebuilt. Earlier four-world /tmp/tpw-native-score-arithmetic-20260924
passes exceptexact known retail reds (41 arithmetic checks before direct ELF checks added).
This is a REVIEW SLICE, not landed production behavior: ParkVisitors is UNCHANGED.

New direct instruction trace resolves candidate family order3,6,1,7,4,5,2 and newest
activation first within each pool, including distinct coaster+130 links. Findings updated
with iterator tables, head getters and allocator stores. Initial capstone disassembler
failed to import; NO inference made from that failure. Small raw-word decoder handles
these instructions and reports UNKNOWN explicitly (movz/movn checked from R-type words).
Remaining production package must integrate concrete getter inputs, signed score,
instance identity/history lifecycle, eligibility and real inside entrance. No guessed
91 threshold or generic random fallback for joined retail candidates. Keep relief WIP
in ../tpw-ride-eligibility untouched. Bus remains queued after urgent guest fixes.

## Native selection/residence integrated, review pending — September24 13:12 UTC

79cd95e adds shipping compiled candidate selection, concrete family value getters,
native iterator order, per-active-guest instance history and selection-time penalty.
Native relief residence integrated too: actual inside approach, hidden522 counter,
next-dispatch completion, retained-position reveal/egress. Corrected1309F8 resource
meaning: APS section5 count EXACTLY2, not resource class2; larger/no-APS facilities
are not forced single-occupant. See findings/native-selection-eligibility.md.
The original unconditional busy fixture was wrong for all4 large toilets and now
asserts both metadata and correct admission rather than universal exclusion.

Physical placements (PlacementTurns supplied) use native score. Unplaced synthetic
fixtures retain legacy routing; joined retail candidates never fall back to45 or
nearest-urgent selection. Missing compiled/invalid geometry cannot resurrect random
selection. Native state2/10/11 predicate is represented, but managed lifecycle remains
an explicit adapter to open/closed/script construction/broken, not native state updater.
Coaster selection requires TrackClosed; track cache and sideshow operating fields are
represented with traced constructor defaults, but live native control bindings remain
separate. This is not complete guest-AI parity. Never describe these limits as absent
from the disc.

Core /tmp/tpw-native-score-integration-second and after upstream merge
/tmp/tpw-native-score-merged pass all4 worlds except exact2known raw retail failures.
Runtime /tmp/tpw-native-score-runtime-first: shopsPASS; standing155 assertions PASS,
but gate rejects3 legitimate engine errors from RideParticles.Emit reusing outside-tree
emitter. Reported to cow1552666870891421798; do not waive. Fresh normal-startup JUNGLE
StandingServiceSmoke /tmp/tpw-native-relief-smoke.log PASS, captures /tmp/tpw-native-
relief-smoke*.png; +522hidden/+523finishing/+524sameidentitysameposition, physicalegress.
Captures NOT visually signed off. Native lifecycle production mutations/review pending.
Merged peer through e98ee1e (money/gate/cost); own financial invariant updated toopening
balance instead ofobsoletezero. Do not overwrite peer path-cost work. Before landing:
mutation production scorer/residence/visibility, peer review, merged runtime, null-APS
render and fourworldcontrols, docs/minimumcoverage. Bus queued next.

Final integration verification before peer sign-off:
* /tmp/tpw-native-final-matrix: all4 worlds,43score/25actual-choice/78relief/70value
  checks EACH; only exact documented raw retail failures remain. Runner now requires
  these counts and named fresh/need90/sickness/construction witnesses.
* /tmp/tpw-native-final-runtime: all8 scenes PASS, standing155 includes23 real body/
  bubble/native-phase checks. /tmp/tpw-native-final-python.log:61 testsPASS. Initial
  runner self-test changes exposed stale132 counts and assertion-ID holes; repaired
  those fixture generators, not production assertions.
* /tmp/tpw-native-relief-four: normal startup rendered smoke PASS in JUNGLE/FANTASY/
  HALLOW/SPACE, actual UI placement + native choice + physical approach/hidden service/
  same-position reveal/egress; PNGs produced, not visually signed off.
* /tmp/tpw-native-consumer-mutations: bypassnative5reds, invented91veto4,
  eraseneednegatives6, universaltoiletoccupancy1, nonstrictdeadline9, same-dispatch
  completion8. Every source restored and rebuilt.
* /tmp/tpw-native-visibility-mutation: removing the production ServiceHidden render
  guard fails4 native presentation assertions (body,bubble,+522,+523). Restored/rebuilt.

CORRECTION to prior particle diagnosis: emitter is under a detached Viewer in OUR
headless fixture, not a proved production reuse bug. Normal-startup all4 runs show no
engine errors. Fixed fixture by initializing the real particle holder before moving
Viewer children to the live test stage. No particleproductioncode changed. Cow told
explicitly in1552668666615828541 not to act on initial diagnosis.

Parent independently checked raw eligibility words1309F8/1E1E48/120530. Also corrected
native slot5 enter/exit notifications to run ONLY for the exact-two APS pair, as1FAD40
does, rather than requesting them on every relief facility. Core/native marker tests
preserve concurrent large/no-pair relief behavior.

Still NOT on main: awaiting peer review already requested forf9a8b2a. Keep branch pushed,
fetch/reconcile any newer economy/path commits before landing. Don't repeat completed
gates unnecessarily; next check is review changes then normal push, followed by bus.
The dirty original ../tpw-ride-eligibility WIP is superseded by this integrated branch,
not separate pending feature work; preserve until main incorporates this branch.

## CP2 — native guest consumer landing, September24 13:40 UTC

Cow independently reviewed62d22aa and reran4worldmatrix (messages1552674941231431803 /
1552674942229815318). Confirmed APS count predicate through three actual consumers,
all3regionalDBA effect inputs inside lookup bounds, literal history propagation, and
current Viewer.SetOpen binding. Script-driven native state transitions remain a stated
adapter limit, not claimed equivalent. RegionDBA choice is a follow-up (Viewer stillEUR).

Independent code review found one additional blocker: physical compiled service whose
inside-entry geometry failed could still take legacyRSE service through its publicstub.
Fixed e0f0e0e: RequiresNativeServiceEntry/Takes refuses that path for placedSHOP/relief;
Viewer reports invalid relief as well asSHOP geometry. Real wrong-but-reachable stub
regression exercises actualIdle. Removingguard yields2explicitfails in
/tmp/tpw-native-geometry-mutation; restored/rebuilt. Nativeconsumer minimum now29/world.

Reconciled peer4b312c1+a1924ab as e8b91f8, preserving gate,moneyHUD,startingbalance,
placement costs and PSX-derived path/queue prices. Final merged gates:
/tmp/tpw-native-landing-matrix only exact2knownrawretailreds;
/tmp/tpw-native-landing-runtime all8PASS;
/tmp/tpw-native-landing-python.log61PASS.
Fourworld normal-startup rendered evidence remains /tmp/tpw-native-relief-four (pre path-
price merge); finalmerged headless runtime andcorechecks pass. No image review claim.
Decision: LAND. Full nativeAI parity remains out of scope; see findings integrationboundary.

NEXT: requested bus research, not more scorer audit expansion. One broad bus-assets
worker timedout600s with no returned finding and no repositoryoutput; do not infer
absence or claim censusfinished. Narrow to memory-only WAD inventory/RSE behavior first,
then trace native constructor/movement/guest-release consumers. Preserve owner's disc
in place. Original dirty ride-eligibility snapshot is superseded, do not land it again.

## Bus research began — native guest fix is LANDED7d262d0

Cow independently verified merged7d262d0 including all22path-pricechecks and gamebuild
(message1552676382910316686). Do not hold scorer awaiting review any longer; it landed.
Active branch now astraclaw/bus-native-research in SAME worktree ../tpw-gate-occupancy.

Bus inventory completed by parent memory-only C# reader ../tpw-bus-research, text output
/tmp/tpw-bus-inventory-clean.log. Both Bus1/Bus2 animatedMPS bundles exist in ALL4 worlds;
bus.RSE is identical across4 and looks up Traffic Lights, NOT the park gate. Slot5 has
3variants220/260/220 in eachAPS. SeparateSAMbundle noMPS is not evidencebusmissing.
NativecataloglogicalID97 is distinct fromSAMID1600etc. Positivefactory149C90->1476E0
creates global37E0D4 through228868. Parent readrawinstructions/wrappercalls independently.
Docs bus-assets-and-script.md and bus-native-catalogue.md hold provenance and limits.
No gameplayimplementation. Nexttrace17BFF8/17C5D8/17C920 plus writers395280; identify
real movement phases, native arrival timing and guest drop-off. Native states0..3 are NOT
scriptstatuses1..6 unless callertrace proves it. Sound alternativesnotanimationstages.
One originalbroadworker timedout600s; narrower4minuteprimarytrace succeeded. All3delegate
calls used this turn (independentreview, timedoutbusinventory, nativecatalogue). No assets
were extracted. Local helperdisasm reportsUNKNOWN forCOP1/movz; decode criticalwords
rather than assuming them. Userbusrequest1552630912481624105 remains active.

## Bus controller/arrival follow-up — September24 afternoon

Three focuseddelegateprimarytraces succeeded thisturn (animationconsumer,arrivalcontroller,
batchinputs). Newdocs bus-native-animation/arrivals/demand; parent added clock-and-audio.
ActualPS2chain14BE60->14C308->16B5C0->14AC48->vtable20BCD0 releasesbatch on state2complete.
Spawnpoint0 is computedONCE beforeloop from same2B71B0entrancetable; allactivatedguests
getsharedtilecentre. IndependentPS2confirmation ofpeerPSXprediction, noPSXcoords copied.
Nativeanimationcommands use5:0,5:1,5:2 nonlooping30authoredfps frommsclock; actualcontroller
is separate. Native4statecycle isnotBus.RSEstatuses. Batchcounts dependontraversedobjects,
currentupgradetier/value, representedtype/indexceiling andexistingoutsidegroups; notfixed12.

Parentresolvedcontrollerclockproducer11E758: source2ACAB4 +=10000 whennotpaused, then
11E688 delta<<7 and1C4AA8 cap4000. Therefore saturateddwell20updates,outer50updates,plus
separatesubtracttick/transition. NOT6400ms/2560ms; noinitial0..199msclaim. ConditionalPAL
25Hz pacing isknown butnotunconditionalwalltime. Sourceupdatedbeforegamecontrollers.

PS2nativebusAUDIO exists:147BC4->111428 selector1,event6,handle2B73A0;state1param20=0,
state2/3param20=51, afterstrict>2000msreset0. 147828 movesSAMEsoundhandle through111758
usinganimatedfitting1/space200. Sentcowaddresses;meaningparam20/remappedbankstillunread.
No borrowedPSXsoundabsence orbank-as-stage inference. Parentconfirmedtheseinstructions.

Alsoresolvedordinarypersonalityproducer: BOTH16B6F4..FC(batch) and16B77C..8C(single)
rand8->guest7D. Existinguniform8spawnrule nowhasdirectconsumerproof, notwhole-tableextent.
CowownsVisitorNeedscommentcorrection; no gameplaychangehere. BusdemandO+126 isalreadyknown
currenttier, rechecked1178AC and116070/74(config Upgrades) ratherthaninventingagefactor.

Remainingboundedimplementationdependencies: nativecapacitytablepopulation12A/360850(BSS),
meaning/producers3953D0/D4 groupcounts, active3953CC objectgate andguestA4countmeaning,
phase1trafficcoordination3953E0, graphicsdraw/section12betweenvisits, exactworld/parkvariant
andplotframe. Do not implementarrivalcount12/every50again ordeclarefullbusparity. Next choose
one dependency thatunlocksrealvisualbus+arrivalconsumer ratherthanexpandnakedhelpertests.
Noassetscopied/extracted; helperdisasmnowdecodesCOP1simpleops/movz/dshift; UNKNOWNsremain
visible. All3delegatecalls usedthisturn. Originalworktreeprogresspointsatthisactivebranch.

Peer PSXfollow-up predictions independentlychecked onPS2: base10 and3-wayminagree.
Ride+20agegate DOES NOT carryover:16B864 helperreturn discarded,16B868 delay-slot
s4=20 unconditional, v0 overwrittenbeforevaluecall; noagebranch. Parentconfirmed and
sentpeer1552685410247778386. PreservePS2difference, don'trepairtoPSXbehavior.

Peer audio111D40 signature resolved(audio,handle,selector,value), despiteGhidra's
incorrectundefined8 grouping; param20semanticsstillunnamed. Global2ABE18 ELFvalue0
(parentread), no directwritecensusdoesNOTexcludealias/factory/block initialization.
Askedpeer bounded106F80handler andaddress-of-globaltrace, in-memorydisc only; noIRX
copyrequested/used. Audio workremainscow'slane, buspopulation/visualcontrollerours.

## Bus binding prerequisites and actual renderer blocker — September24 14:45 UTC

This turn3workers tracedcapacity tables,root/section12drawing,entrance-groupcounts.
Docs bus-capacity-tables.md,bus-native-visual.md,bus-entrance-groups.md. No gameplaycode.
Capacitytables havepositiveinitializer158CF0 andnormaldenominators12/12,12/13,10/11,11/12;
not allSAMs/unlocked-only. Need exactscenarioselector/placementordinalbinding next.
W counts actualintrusiveguestlists via1527C8/152A18, NOT guessedlanes/Walkcount; upstream
modes14/15 andgroupsmembership/removal stillneedintegration. Activegroupupdates precede
2EEB9Creset, so do not turnguestA4 into a simplepathfailureflag.

ParentfoundMAINVISUALBLOCKER: Busbodytracks0x200, whileAnimatedModel onlyapplies+10 paths.
Existing animation.md wronglyclaimed+1Cdataabsent. Corrected loadertrace169B3C->model+78,
countu16+2C,stride16,168728 relocatespoints. BusMPSfirstdescriptorflags2,45floatXYZpoints.
APS+1C->pointer->FLOATprogresssamples, nottime-keyarray. ConsumerreadsPARENTnode+52,
notcurrentmesh+52; bodyparent3D0 flags80000058,index0. Correctedowninitialmeshzerosreading.
MPSboundidentityrendererroot isidentity; six1476E0floats arecullingbounds notplacement.
Section12cleanup: decoderUNKNOWN loadsat1AA734 actuallys5(source),nots4(instance), so
1704F0 restoresbind/source matrix. No needinventrootVisible=false orholdendframe.

Modelpathsamplerboundaries nowdocumented bus-model-path-channel.md:1AE450 interpolates
percentagefloatssampledperframe, shortestwrapwhenabsdelta>50, exactdurationuseslastsample
(notonepast). 28CC90confirmedfmod byliteralerrorname37C1B0. Callerfmod(progress+1000,100).
Curveflags1topology,2Bezier,8linear;45/3=15controlknots butflag1CLEAR=>14spans. Peerclosed-
loop inference retractedagainstcaller1A8444; don't implement15spansjustbecause45divisible3.
NEXT boundedproductionwork: finish0x400facing/0x800 interaction thenimplementmodelcurve+
APSprogress reader AND AnimatedModel consumer withrealbusmotion/controlpaths, notpure
unusedhelper. Thisalso fixesother96trackcarriers; don'treuse dead+10orientlogicwithoutread.
Buscontroller/headcountintegration followsactualpathplayback, notanartificialmovingroot.

Peer a90c2db namesaudioselector20 viaAMBSFX.MAPevent6 word12=20,links0..50/51..100;
retainedexacttargetindexbaseunknown. Cowownsaudio/screams; our0x200reader+facinglane separate.
All3delegates used thisturn. Branchworkingtreecleanaftercommit; originaldirtyworktreesuperseded.

## Bus model-path production consumer — September24 15:05 UTC

Implemented ModelPathChannel + shipping AnimatedModel binding/application. Important
facing correction: Bezier1ADBB0 derivative; linear1ADE30 next-current; ONLYfallback
1ADE90 fraction+.1 WITHOUTsubtractingposition. 1A6508 unitbasis, rotate-up flag8;
no restorebindscale. Oldnote generalizedfallback; correctedanimation.md explicitly.
Track800notusedasfacingcondition; node800separate namespace.
ModelPathAudit: actual eight buses*three records, independentraw Bernstein oracle,
parentpoison/control, literal topology/wrap/facing, realMeshInstance3Dtransform asserts,
nonzero motion, turnson220framelegs, noall-hiddenvacuity. 65otherboundtracks finite,
137movingsamples. Finalexpanded1574checksPASS. 4mutationscaught/restored,62PythonPASS.
Mergedpeerb44c04a (screamcodepreserved). All9runtimePASS andfourworldmatrixonlyexact
knownretailreds (/tmp/tpw-bus-channel-runtime,/tmp/tpw-bus-channel-matrix). Expanded
othercarrierchecksranseparatelyafterruntimepackage (/tmp/tpw-bus-other-audit.log).
No renderedpixelapproval/nativebuscontroller/arrival integration claimed.

VoXaskedaboutdecompileraccess. Clarifiedtruth: hadsharedfindings+localdisassembly,
NOTcowWindowscorpus. Cowhadseparateownerauthorization; strawberryapprovedtransfer
1552696374959669269 andallprojectfiles1552696458698825840. Downloaded416entryZIP
1552696493574459432; safelyunpackedoutsideGit at ../tpw-private-research/ghidra-corpus
(414Cfunctions+2indexes). VoX1552696743664025724 explicitlysaiduseasadditionalsource
andresumework; savedmemory. Read1A7F48C andconfirmednewconsumeragainstrawinstructions.
No corpus/discassetsinGit. PSXbinaryattachmentNOTdownloaded; unnecessaryforPS2motion.
GhidraEmotionEngineReloaded READMEfeaturesfetched; NOTinstalled/validated, don'tclaimit.

NEXT: peerreviewproductionpathpatch; native section12 sourcepose+visibilitycleanup,
controller/animationphaseintegration thenarrivals/population. Busresearchdocscontain
bindingsandremaininggaps. Don'tstopatdecoder orreportbusdone. Latestpublicsourcebranch
astraclaw/bus-native-research; obsoleteoriginalworktreeWIPstillnotlandable.

## Native bus lifecycle adapter and reachability correction — September24 15:42 UTC

All3delegatecallsUSED thisturn: (1) animation clockproducer/instanceflags, (2) initial
andtransitionvisibility, (3) candidatecontroller. Thirdworker couldNOTwrite/tmp (sandbox
readonly), returneduncompiledcandidate; parentdidNOTcopyblindly. Parentcaughtitsown
oldsection12reachabilityerror at17C7B0..E0: busAPSsections=12, request12 rejected bytype15
wrapperBEFORE1ABC80. Thusstate0soundstopoccursbutNOgraphicsstopcleanup. Enddeparture
record5:2 remainsheld+hiddenbetweenvisits. Downstreamcleanuptracevalidbutnotthiscall.
Correctedthreeolderfindingsprominently,newfindings/bus-native-lifecycle.md detailed.

Also establishedself-hidden10 vs subtree-pruning20 (22810C/228140 rawparentchecked).
Busbodyparentishelper4/3D0 flags80000058; trackedhelper3/370 isLEAF, notbodyparent.
Nativeinstantiationcopiesauthoredflags. Nativebusmustnotinheritroothelper10: allmeshes
eligibleinitially. Newopt-in AnimatedModel nativeNodeVisibility + ActivateNativeRecord
ordinarymorph/index0/flags0only; oldunprotectedlistedmeshhideclearedonordinaryactivation,
protected/unlistedretain. LegacyRSEUseRecord APIunchanged. Nativehiddennodesstillupdate
pose, orrestartrevealslastVISIBLEpose; control+mutationcaughtthis, fixedscopedtonative.

NativeBusController core usesactualAPSdurations, cache,newrecordonlyinactive/endhold,
strictendclamp, after-commandtrafficguard,20/50countdowns,actualbatchgatesandpoint0request.
NativeBus gameadapterbindsactualAnimatedModel. NotyetwiredtoViewerpark oractualadmissions;
no newarrivalcap/intervalinvented. Noaudioimplementation. Coreacceptsinjectedanimationms
andseparatesignednativedelta; hasnoSim.Time substitution. Candidateworker'sgenericqueue
codeNOTcopied: boundedcontrolleronlyrequestsnewclipsafterinactive/endhold.

HardwareelapsedanimationclockPOSITIVELYtraced:225128timerhandler increments310CA8;
220C78 delta*10 ->always2F0798,gate2F07B8->2F07A8. 220C68gate; calledpause/resume.
225FD4pump occursafterconfiguredgameupdate loop, notonepergametick. Nativecountdown397640
remainsseparate capped16384 clockalreadyread. Parentcheckedraw220C78,22810C,1AB938.
Factory0x32Fisnotinstanceflags. BusheaderA1 meansnormalinitinstanceflags18clear;
ordinarynonloopendholdpathvalid. Futureclockadaptermustpreserveelapsedthroughsimstalls
andpausegate, notunconditionallyunifywithsimclock.

Evidence: NativeBusAuditactualeightresources phase/restart+gates+clocks+surfaces976PASS;
ModelPathAudit1574PASS. Sixmutationscaughtstrictend,badpercallclock,unreachable-stopmade
real,inheritedhide,nocleanup,hiddenposefreeze. Allrestoredandrebuilt. Mergedpeer14db25b
soundgraph beforefullruntime10scenesPASS; /tmp/tpw-native-bus-runtime-final (bus968before
last8hiddenposechecks), /tmp/tpw-native-bus-reveal-audit.log976after; fourworldmatrixonly
exactknownretailreds /tmp/tpw-native-bus-matrix-final. Python63PASS. Initialparallelbuild
attempttooltimedoutandkilledprocessgroup; these-finalrunsusedPopenstart_new_session=True
andcompleted. Don'tconfuseearlierincompleterundirwithfinalevidence.

NEXT: getcowreviewee3e645pathpatch/lifecyclefollow-up; continueproductionparkjoin while
notclaimingbusdone. Need exactscenarioselection/catalogordinaljoin fornativebatchcapacity
ratherthan allSAMs, actualpoint0admission andgroupscounts; see bus-capacity/entrance docs.
CurrentViewerGate stillinventedoneguest/50ticks/cap12; donotattachbuspurelycosmetically
andcallitnativearrivals. Keepcowaudio/money filesalone; newViewer.Bus partialcanavoid
crosslaneoverlapwhenready. Cownewparam20callback(rideId,paramId)needsunique bus sound
instancekey+native0/51/strict2000 reset; itsgraphtimingstillunread. Their ef39617 param18
"0correct"claimaskedforinitializerproof; no-settercensusaloneisnotinitialvalueproof.
PartialdecompilecorpusoutsideGit remainsavailable; actuallyconsulted1A7F48,1AC048,
1AA460thisturn. Notfullcorpus; noabsenceclaimsfrommissingfunctions.


Peer reviewed e15540f: no blocking findings. Questioned initial modulo200 vsC8000
reload aspossiblemissingshift. Parentrechecked149C64..94directDIVU/MFHI/SWnoSHIFT;
replied1552707275917893695 andrecordedbus-native-lifecycle.md. Initialpositivewait
one saturatedsubtraction, NOTinstantbatch; animationsstillrun. Do not"fix"tomatchPSX.
Peerconfirmed unsignedfloatidiom,clockwrap,controllerreplacementinvariant,hiddenpose
updates,andself-vs-subtreevisibility. Motion ee3e645 review notseparatelyexplicit yet;
e15540f encompassesitbutdon'tinventpeerviewsnotstated.

## Remaining bus joins closed / admissions work started — September24 16:15 UTC

ThisNEWturn usedall3delegates: categoryordinal producer;normalparkselector;admission
pressure/minigamegate. Read/findingsbus-catalogue-identity-join.md andbus-admission-pressure.md.
Allworkersreadonly, nofileswritten. Parentchecked14E290 raw strideHEX36/12 (bewarebare
summarynotation), nativecataloginitializercountloadstores via/tmp/tpw-bus-table-inputs.log.
CatalogK->ordinali resolved fornativekind1/3/6/7 bymenuappendpreservesoriginali,
toolfactorysamei usedkeylookupandbasea2->O97; independentreverse152CD8. NormalworldWAD
+selectedterrain_1/2 mapsw/s positively viaUI369DD00? CORRECT TABLE36DD00,149678,
17D7E8loaderselector andPARK(s+1)audio control. Jungle0/2specialnotordinarymode.
Bus1/2selectorsarecomparedvalues1/2 afterstrip8, NOTarbitrarybitmask.
3953CCactiveattractionminigame object nowidentified. A4stickydeparture-deferral latch
setwhenphaseid&63 mismatchesdepartureattempt orroutefails, NOTclearedonsuccess;
perpass2EEB9C countsBEFOREstateupdate, resetAFTERentrancegroupupdates. Portdoesn'tmodel
it: cannotclaimWalkcount orNeeds orpostupdatesizeequivalent. Eventualbypassmustexplicit
ratherthanblockallbusforeverclaimfullAIparity. EntrancegroupWalsoseparateunfinished.

NEWUNCOMMITTEDcorefiles NativeBusCatalogue.cs +NativeBusDemand.cs BUILDCLEAN butNOTTESTED
ORCOMMITTEDYET. Catalogue readskeyarrays/countsfromELF initializer'ssources;type8keysfrom
validatedADDIUimmediatesratherthanBSSzeros. ExplicitordinaryNativeParkSelection mapsactual
sourceWAD+terrainstem. Point0 usesbyte0/1table0x2B71B0 stride0x36/0x12; initialmutable
B/Q from2B9734/30. Counts,ordinals,capacityrequiretestsagainstall8, malformedcontrols.
Demand implementsnativeAandsignedthreewaymin, butNOTproductionjoined. NextMUSTwireactual
Viewer/parkconsumer+tests notstopatpurehelpers. ActualGate() stillold50tick/12cap, doNOT
saybusinpark. Userasked1552711751064223887 andansweredno1552712980393885792. Bothpositive
identityjoinsareclosednow; admissionpressurelifecycleunimplemented clearlynotmystery.

CurrentruntimeParkRide.Value isdecoded getter. UpgradeTiernotrepresented; standardnew
instance0 isdecodeddefault, configUpgrades2 notsupported. UseNativeOrder3,6,1,7,4,5,2
newestfirstforA; capacitycountsunique(type,key), duplicatesdon'traiseceiling. Type8may
needseparateplacedlist source sinceDestinationIterator excludesit. Viewer_sim.Rides
onlyscriptedplacements, _park.Placed includesallphysicalobjects; ensureproductionA
joinsactualplacementsratherthanquietlymissunscriptedfeatures. ExistingArrive(at,to)
seedsNeedsandclearsgatehistory; at=Point0,to=Point0 avoidsrandompreselectedattraction,
thenrealnextStepselects. Populationshouldcountallplans/walkidentitiesincludingoffwalk
servicing, no12capfallback. Point0atXStart/ZRow NOTmouth(XCol,ZEnd-1).

Peerworkinggate8x2again, user's16:04requestdirectedcow; donottakethatlane. Peerlatestmain
b1839f9/587fc7f stillbrokenperuser16:12; cowownsrepair. Mainchangedaudioea9912f basedon
clipdurations (overlapconsumerstillnottraced), furtherpending. Ourbranchba625b7basedon
14db25b beforethese; fetchmergeatmilestonewithoutoverwritingpeerViewer. Keepcorpuslocal.

### Bus input package verification — September24 16:25 UTC

NativeBusCatalogue/NativeBusDemand now TESTED:276source/arithmeticchecks acrossall8
normalparkselections ineachmatrixworld. /tmp/tpw-bus-input-matrix fullmatrixPASSJ/F,
onlyexactknownH/Sretailreds; categorynative_bus_admission_inputs minimum276 enforced.
/tmp/tpw-bus-admissions-inputs-final.log pureinputpass; /tmp/tpw-bus-input-python.log
63PythonPASS. Fourmutationscaughtsortedkeys1fail,missingbase10five,missingride+20two,
duplicatescounted8;restored. Initialtestcompileparamsarraytargettypednew fixedexplicit
Attraction; initialoverfloworacle waswrong (0x10000000*1333hex wrapspositive), replaced
withliteral0x40000000 productC0000000 /81920 ==-13107, no fittedobservedseed.
Allcode+findingsbeingcommittedonresearchbranch. ThispackageDOESNOTclaimViewerguestjoin;
no morepurehelperexpansionnext: createactualViewer.Buspartialwithliveplacementidentity,
spawncallbackPoint0, nativeclocklifecycle andclearparkteardown. RecordknownA4/entrancegroup
supportboundaryhonestly; don'tinventfalseequivalentcounts, don'tcallcosmeticbusdone.

Usefulnextintegrationdetailsread: Viewer.PlaceHeld successfulTryPlace givesNode and
_place.Def (compiledidentity), thenride=++_rideSerial andStartScript mayreturnfalse.
_park.Placed holds(Id SAMid,Name,Fp,X,Y,Node), NOTruntimeRideId/compiledDBAkey. Mustregister
Node->(runtimeId,Definition) atshippingplacementbefore/alongsideStartScript tocount
unscriptedphysicalfeaturesalso; sim.Ridesalonemisses them. Reconcilebylive_park.Placed
Nodesafterremoval/reset. ForruntimevalueusematchingParkRide.Value; forunscriptedobject
useNativeRideValue defaults fromidentity; currentupgradetiernormally0unimplementedUI.
NativeOrder3,6,1,7,4,5,2newestfirst forA; capacityK usesdistinct(type,key),includes8when
properlyplaced/identified. Capacityreader'ssourcecount/type8immediatesALLverifiedDBAkind.
NativeParkSelection.Ordinary validatesactualWAD/terrainstem; BusStemrejectsvariant2.
Point0=entry.XStart,ZRow nativeTABLEBYTE0/1 (0x36/0x12 hex strides). Thisisoutsidegate
butexistingentrancecorridorcontainsit. CurrentGate() uses mouth+randomgoal; mustreplace
fornativebatch, notrunboth. CurrentParkVisitors.Arrive(at,to) callsWalk.Spawn, clears
history/gate, setsWanderplan, seedsNeeds. at=toPoint0 allowsnativeidledecisionnextStep.
Stageadmissionmustcountplans/offwalkservicing too, notonlyWalk oroldcap12.
## User Belly Bounce index-list fix — September24 16:52 UTC

ISOLATED worktree ../tpw-hide-list branchastraclaw/animation-hide-list basedmain1cf7860
becausebusresearchhasuncommittedliveViewerbridgeandmustNOTblockthisreportedbuglanding.
Workerpositiveactivation1ABA10->1AABB0;parentcheckedraw1AAC74..1AAD08. NEWlist sets10,
oldcleanupclearsoldunprotectedlisted/tracked10. Count/indicesu16. SourceRecordFlags4 is
NOTincomingplaybackflags8. OrdinaryRSEflags0/1reachbothcleanup/activation. Explicitlist,
notallmissingtracks: Bouncyjb_floorhasnotrackandnolistentryandMUSTstayvisible.
CoreAnimationNodeVisibility+actualAnimatedModel.UseRecordconstructor/RSE/simactuator.
Ordinarymodeown8050 vsancestors20 (notinherit10);hiddenposeevaluationretained. Skeletal/
sharedformatsnotreadwithordinarystride; legacyMD2excluded. RsePresentercommentscorrected.
AnimationHideListAudit234checks includesactualegg/shellsurfacevisibility, unlistedfloor,
cleanup/rehide/protectedcontrast,u16countupperBEEF, actualRseModelPresenterCreate->next.
Full60s genericRseRidePreview hitunrelatedFIFOunload101/102; scopedtestbeforeunloadwith
assertionnextrecordactuallyreached, notcatchingerrororclaimingfullcycleverified.
Five mutationscaughtdropnewhide/dropcleanup/dropProtection/wrongwidth/disconnectactuator.
Allrestored+rebuilt. /tmp/tpw-hide-list-runtime-final ALL9PASS; fourworldmatrix /tmp/tpw-
hide-list-matrix exactknown2retailreds; /tmp/tpw-hide-list-python.log61PASS. Needpeerreview
andnormalpushmain, thenmergeintoBUSworktreecarefully (busAnimatedModelnativeoptin overlaps).
BusWIPstaysin ../tpw-gate-occupancy Viewer.Bus.cs+Viewer.cs, NOTinthisworktree. Its latest
build/tmp/tpw-bus-live-build-fixed.log isclean; actualparkadmissionverificationstillneeded.
CowownsaudioseamadditiveViewer.BusAudio.cs via partialhooks specifiedinchannel, don'toverwrite.

## Live bus consumer milestone — September 24, 17:20 UTC

Active worktree `../tpw-gate-occupancy`, branch `astraclaw/bus-native-research`.
Belly Bounce `335ab4e` is SHIPPED on main; do not redo that package. Bus remains
branch-only. Live Viewer bridge in475e401, shipped changes merged6361592, cow's
sound seam065031a/1639088 cherry-picked84493a9/211844a. Normal Viewer placement
registration, native Point0 admissions, map reset and clocks are now connected;
old invented Gate() arrival timer removed rather than left running beside it.

New rendered-display `game/tests/BusViewerSmoke.tscn`: actual normal _Ready, real
Shops menu/ArmFromList/PlaceHeld, shared grid and compiled identity, StepPark(.04),
first batch, actual walkers/Plans/Needs, live bus scene-tree model, then LoadMap
and fresh empty-park sound owner. Four worlds passed `/tmp/tpw-live-bus-*-v2.log`,
2039 checks each. First arrival405ticks/16200ms, one guest from the real one-shop
score20, at native Point0 (not at bus or gate mouth); no guests before phase2.
Initial standaloneJungle `/tmp/tpw-live-bus-jungle.log` also passed1846checks.
These are rendered-display integration runs, NOT screenshots/listening approval.

Audio test exercises live selector20=51 at phase2, zero after strict>2000ms,
and an independently changed ride ScreamLevel73 still reaching selector6. Reload
clears bus/placement/admissions; next empty park creates sound BEFORE any shop,
and its own selector20 reaches51. Found/fixed bool surviving sound-owner reload
(cow1639088) and bus-first setup skipping later scream ??= (move base accessor to
MakeSounds, let bus create sound). SetMode hides bus outside Park without changing
native state0/end-hold visibility. v2 tests used our equivalent owner-identity fix;
final cow integration includes re-chaining in UpdateBusAudio and needs fresh gates.

Next: test terrain2 as STARTING map too, fresh combined11runtime+4worldmatrix+Python,
merge latestmain (cow owns cursor/bridge lane), mutation-check live consumer where
practical, ask peer review and land only after gates. No more isolated-helper-only
work. Bus native pressure boundaries remain explicit: current port has no native
entrance-group queue counts and no sticky departure-deferral+A4 lifecycle, so these
admission inputs are ZERO with a startup warning, NOT claimed native equivalence.
No attraction minigame session exists. Upgrade tier defaults0; actual upgrade UI
unimplemented. Dedicated elapsed-ms/quantized active clock is separate from executed
countdown ticks; exact console pump/pause-edge timing is not a hardware-emulation claim.

## Final bus release verification work — September24 17:36 UTC

All8normalstartingmaps passed2039checks each: `/tmp/tpw-live-bus-*-v2.log` (park1)
and `/tmp/tpw-live-bus-*-park2-retry.log` (park2). The first park2 launch raced a
runtime clean build and could not load its C# class; those failedlaunchsessions
were terminated, not counted as passes. Retry after build used6759903 including
cow's bridge/path-cursor changes and both sound seam commits. No visual/listening
approval claimed. Two LIVE negative controls caught: omitPlaceHeld registration
failsbeforetick; shiftactualspawnonecell failsnewbornPoint0. Logs `/tmp/tpw-bus-
mutation-gl-registration.log` /`...-spawn.log`, both exit2. Restored/rebuilt. Initial
Vulkan mutation printedexpectedassertion buthungonshutdown150s; notcountedaspass;
GL compatibility runprovedbothfailures. No production mutation remains.

Combined11-scene run `/tmp/tpw-live-bus-runtime-final` first returned one unexpected
StandingService ObjectDBshutdownleak, despite155checksPASS. Investigated rather
thanwhitelisted: 28AudioStreamPlaybackWAV+7AudioStreamWAV, noNodes. Worker baseline
6/12verboseleaked, add100msafterstage/voicecleanup20/20clean, removeonlywait4/10leak,
restore20verbose+5normalclean. Full logs at sibling scratch1492558787561914542093/tmp;
`leak-evidence.txt`listsactualreferencecounts. OnlyStandingServiceAuditteardownchanged.

Peer8b0bba9review acceptedlivehookup, butcorrectlyflaggedunportedzeroasPERMISSIVE:
startupwarningnowexplicitly saysbypassesbacklogreductionanddepartureveto. Bounds
areloggedfromsameNativeBusDemand.Bounds usedtoadmit, notduplicateddiagnosticarithmetic.
Two extra inputcontrols=278total: scorebound8/group5/headroom25=>5 andzero-group=>8.
This doesnotfixbacklogbehavior. Needs itsreal lifecycle next, noinventedcounter.
Finalfreshgatespendingafterthiscommit; oldmatrixalready J/Fpass/exactH/Sknownreds,
63Pythonpasses. Keepthisdistinction untilfreshgatescomplete andmainispushed.

## Bus release candidate — September24 17:40 UTC

Fresh gate at c17589e: `/tmp/tpw-bus-release-runtime/manifest.json` ALL11PASS,
including Standing155, ModelPath1574, NativeBus976, HideList234. Fresh4worldmatrix
`/tmp/tpw-bus-release-matrix` J/Fpass, ONLYexactknownThrillGrill/MoonBuggiesreds,
278nativebusinputchecks/world. `/tmp/tpw-bus-release-python.log`63PASS.
Final normalViewerJungle with bounds logging passed2039andexit0; initially quitwith
activebusvoicesgivingObjectDB warning. Smoke nowexplicitlyresetsbus/clearsaudio/
freesViewer andallows100msaudio retirement, samefixturecleanupasStanding.
`/tmp/tpw-bus-release-viewer-clean.log` exit0,2039PASS,noerror/leak. Thisfinaldelta
changesONLYtheBusViewerSmoketeardown, notshippingcodeoranyof11auditedscenes.
All8renderedstartingparksalready2039PASS each; runfreshGLall8afterthiscommit for
finalcleanexit evidence. No screenshot/listeningapproval; noforgottenmutations.


## Bus main landing snapshot — September24 17:43 UTC

Release candidate code6aede08 (onlydocumentationchangesfollow). Final fresh GL
normalViewer matrix `/tmp/tpw-bus-release-eight-viewers/manifest.json`: ALL8cases
exit0,2039checks each, noERROR/leak/resourcewarnings; driver `/tmp/tpw-bus-release-
eight-driver.log`. Bothvariantsactuallyselected andeachreloadsotherterrain. Model,
phase, realplacedidentity andactualadmittedguest tested, noinjectedguest. The
threeboundlogger fromc17589e isvisible in `/tmp/tpw-bus-release-viewer-clean.log`.

Final other gates: ALL11runtime PASS atc17589e;4worldmatrix PASSJ/F andonlyexact
knownH/Sretailreds;63PythonPASS. Finalcode6aede08 only addsBusViewerSmokecleanup to
thatcore; all8livegate thenreranagainst6aede08. Productionandtestmutationsrestored.
Peer reviewed8b0bba9 andacceptedwithnonblockingboundsobservability, nowimplemented.
Cow soundcommits065031a/1639088 preservedascherry-picks andbridge9a32a4f preserved.

NEXT: nativeentrance-group/departure-pressurelifecycle from already-read
bus-entrance-groups.md andbus-admission-pressure.md. Do NOTguesscountsfromwalkers or
pathcells. CurrentzeroBYPASSESbacklogreductionanddepartureveto; busyparksmayoveradmit.
Minigamesabsent, upgradetierUInotimplemented, audiooverlapschedulerstillnotproven;
nohumanvisual/listeningapproval. Thisisaworkingbusintegration, notcompletegameparity.

Keepactiveworktree`tpw-gate-occupancy` (despitehistoricalname); oldride-eligibility
checkoutisstaledirtywork. Cowownsnewanimatedtexture/thought-bubblelane. Authorized
partialGhidracorpusislocaloutsideGit`../tpw-private-research/ghidra-corpus`; useasan
additionalsource, checksignaturesagainstMIPS. Noassetextraction, norestarts.

## Owner bus interpolation request — September24 18:00 UTC

BusISonmaineb67e78. Newrequest1552738005935067238 takespriorityoverentrancework.
Implementedpresentation-only currentrecordfractionalsampling on everyStepPark,
includingzeroexecutedtickframes. Controller.PresentationFrame readonly/clamped;
NativeBus.Present usesexistingAPS/MPS interpolatorsforbody/facing/wheels. Noextra
simulationupdates, callbacks, countdownoradmissions. Heldendpoints/newrecordclocks
remainseparate. ActualViewerBusPresentationSmokefailsBEFOREatfirstzero-tickmatrix
movement, thenpasses57checksAFTER. /tmp/tpw-bus-present-{before,after}.log.
All8NativeBusAuditnow1088checks includingclamp/no-phase-sideeffects/clockwrap.
Freshmergedgatespending. Cowmain05480ec animatedtextures/thoughtbubbledepth should
bepreserved; no same-hostcleanup, weareonseparateLinuxhost.

Entrance research pausedforthisuserrequest, NOTabandoned. Read-onlyworker traced
fullentrychain; importantcorrection independentlycheckedraw20DC28..20DCD8:
N+28=0/1aftermodes15/14ispre-crossingDIRECTION, notyetcountedlistmembership.
Mode16completionreadsoldN+28: zero->rand(2), storesnewN+28, choosesothergroupif
1527B0count>=11, setsstate2A; nonzero->state30departure. BOTHcountedgroupscancontain
incomingguests. Parentverified1532D8ignoresselectorargumentandusespointtable+2/+3
withrandomxadd0..255 AFTERheightsample. Donotinherit"group1meansdeparture".
Fullworkertracehasmoreentryfee/pathrequest/acceptance lifecycle details towriteup
wheninterpolationlands; re-readpositiveaddresses ratherthan guessing groupcounts.

## Bus smoothing landing evidence — September24 18:06 UTC

Mergedwithcow05480ec; ALL11runtimePASS `/tmp/tpw-bus-present-runtime`, fourworld
matrix J/Fpass +ONLYknownH/Sreds `/tmp/tpw-bus-present-matrix`. ActualmergedViewer
presentation57PASSclean `/tmp/tpw-bus-present-merged.log`; realadmission/reload2039
PASSclean `/tmp/tpw-bus-present-arrivals.log`, firstbatchunchanged405ticks/16200ms.
63PythonPASS `/tmp/tpw-bus-present-python-final.log`; initialself-testfailurewas
thenegativewitnessstillreplacingold968countafterpositivefixturebecame1088, corrected.
No productionmutationleft. Isolatedread-onlyreviewfoundnoblockingtransition/clock/
callback/audioorderingdefects; cowwasaskedbutbusywithuser'scoconutUVcorrection.

ThislandsONLYrequestedbuspresentationenhancement; entrancecounterbehaviorunchanged.
Resumeentry/departuretraceaftercheckinguserplaytest/prioritymessages. Usecritical
mode16correctionabove; nativeentryworkertracealsoidentifiedstate24->210AB0 route
requestmode15, coordinator14BCC0 event9 state2E->2F, mode16choosescountedgroup, state25
210C98 fee/acceptanceAFTERheadrelease. Cashmustbestrictlygreaterthan100D20(F+0);
210B38acceptanceclass mustbe>-2; charge100D28 onlyonacceptance, notroute-retries.
Detailedvalueproducer/pathslot/cancellationsemanticsstillneedconsumerwork; doNOT
replacecurrentbypasswithguessedqueuecounts. Noeditsincowtexture/bubblelane.

Last upstream reconciliation: cowd9792f9 removesduplicatewaterclockupdate, merged
688eb40. Freshbuild +affectedTexture/NativeBusheadlessscenesPASS `/tmp/tpw-bus-
present-lastmerge`; actualViewerpresentation57PASSclean `/tmp/tpw-bus-present-
lastmerge-smoke.log`. Noowning/interferingwithcowUVfix; mainpushnormalfast-forward.


## Native entrance foundation — September24 18:40 UTC, BRANCH ONLY

Activebranch `astraclaw/native-entrance-flow` basedmaind34c196; samehistorical
`tpw-gate-occupancy`worktree. Mainhasbus+smoothing98b5249andcowUVchanges; theseareDONE.
Researchclosedactualentrymodechain, intrusivegroupownership,stagingcoordinator,
feeacceptanceordering andsignedquarter-cellroute/motionrepresentation. Newnotes:
`findings/native-entrance-lifecycle.md`, `findings/native-guest-motion.md`. Older
bus-group/admissionnotesmarkedwithcorrections. Bothcountedqueuesareincoming,
N+28isreuseddirectionthengroup. Literal20DC94SLTI11, notDBAattractioncapacity.

Primarymotionread192560/1924D0 and191E98: signed16coords/256, slotroundstonearest64
with+32bias andpreserves87FFflags/links; signedN7Cminimum5; low32(speed*delta)>>14,
axesclampedindependently, nofractionalcarry, boundscheckedbeforeBOTHcoordinatewrites.
Normal15..29speed fromactivation, separatetemplateprofile160410/211A00 notbuspath.
AtWaypoint !=routecomplete: state2withslot->free/advance->state3noslot->state2->purpose.
No invented alternate1532D8endpoints: helperignoresselector; incomingmode16canbethe
samequantizedcoordinate asmode15arrival, butstillhasslot/dispatchsteps.

`NativeGuestMotion.cs` implementsONLYnumericoperations. NOTcalledbyGuestWalk or
Viewer yet; deliberatelyno claimthatqueues/speeds/admissionlimitsarefixed. Dedicated
`--guest-motion-only` andfullParkSimAuditrun55controls, matrixrequires55category.
`/tmp/tpw-native-motion-restored.log`55PASS. Fourmutationscaught5/5/15/12 failures
(roundingbias/minimumspeed/shift/bounds), restored. Reviewnonblocking; correctly
flaggednegativecelltestcannotdiscriminatesignedintermediatevsunsigned+shortwrap,
soitslabelnowassertsonlytheresult. Addedmixed-axisboundscontrolstorejectpartialcommit.
63PythonPASS `/tmp/tpw-native-entrance-foundation-python-final.log`.
Original4worldmatrixwith51checkswasgreenexceptexact2retailreds; final55matrixrunning
`/tmp/tpw-native-entrance-foundation-final`. No game/rendererchangeinthisfoundation.

NEXT REQUIRED: actual ID-owned native entrance/route lifecycle using thesepoints,
notmoreunconnectedarithmetic. KeeporiginalmainGuestWalkuntoucheduntiltheadapter
canpreservependingrequests, slots/terminalhandoff, entrance/stagingmembership,
feeacceptance andnative-decisionordering. Entryrequestpump151928 isINSIDEeachsubstep,
beforegroupupdates151954 andactive14BE60; requestsissuedlaterarenotcompletedbythe
alreadyfinishedpump. Pumpbudget195E28()+100unitisnotseconds. PublicBFSchoice/resource
capacity/modelreadinessremainexplicitportboundaries; nofake"exactnativepathfinder".

SeparateE trafficcoordinator ismissinginViewer too: P3953D8/R3953DC gatebroadcast9
fromE0->1 episode; busstate2ownsE2. CountedWqueuesandsticky+A4tallyareotherinputs.
14B368structurallyunlinksanyembeddedowner butcountrepairviaindirect20BFD0callbacks
stillunread. State0departurewrite20CA70continuesintoRNG6/RNG300switch, notearlyreturn;
currentWantsToGoHomecontinueisnotfullnativeproducer. Don'tderivepressurefromit.

Cowownsscroll/UV/fountain/bubblelane. Sharedpositiveconsumer1A87B8->1AD378 from
corpus+raw helpedtheircoconutdecoder; warnedthatlinearlylerped19.2degUVkeys differ
fromconstant-angularshader by~1.4%radiusathalfway (conditional, inspectactualsample).
Do notoverwrite theirAnimatedModel/Materials edits. PrivateCcorpusoutsideGit remains
additionalsource; noassets/executableextraction, nootherbox/privatebotfiles, norestarts.


Entrancefoundation final gate: `/tmp/tpw-native-entrance-foundation-final` passes
JUNGLE/FANTASY andretainsONLYexactThrillGrill/MoonBuggiesreds, 55newarithmeticchecks
ineachworld. 63PythonPASS; fourmutationsrestored. Read-onlyreviewnoblockingnumeric
mismatch, buttestlabelsign-extensionclarifiedandmixed-axisout-of-boundscontrolsadded.
PushingRESEARCHBRANCHONLY, notmain; nochangeinshippingentry/GuestWalkbehavior.
NextlegmustjoinfullID/state/slotownershipandorderedroutecallbacks, notkeepadding
standalonehelpers. Native route-graphwalker remainsmissing; ordinaryBFS/resource
readinessadaptersneedexplicitboundaries. Cowmain6bfae54introducesAPSUVratework;
keepitsfiles, andletcowcompleteper-vertexUVreplayafteruserfountainreport.


## Native route consumer checkpoint — September 24, 2026, 19:45 UTC

Research branch ONLY: NativeGuestRoute cursor + GuestWalk.Native lease +
ParkVisitors.Begin/ReleaseEntranceRoute + ordinary Viewer native facing. The
arithmetic foundation is now exercised through real Step/StepPark/actor consumers,
not just a helper. Automatic bus arrival does not invoke it yet. Native requests,
staging, group membership, acceptance/fee and normal departure producers remain
missing. Do not merge this as a complete entrance fix or claim the zero input
bypasses are resolved. Detailed boundary: findings/native-route-consumer.md.

76 cursor checks +75 production ownership checks required by audit_matrix. Actual
Viewer NativeEntranceRouteSmoke has113 checks with independent position/facing
oracles and same guest identity/actor/needs/cash. Mutations caught45/40/10/9 core
failures (dispatch/premature completion/idle stealing/fractional snap), and removing
Viewer native facing failed rendered check73. All restored. Fixture disables bus
auto-arrivals and explicitly supplies readiness/speed/delta; no full admission claim.

Full gates: /tmp/tpw-native-route-matrix JUNGLE/FANTASY PASS, ONLY exact known
ThrillGrill/MoonBuggies retail failures. All11 /tmp/tpw-native-route-runtime scenes
PASS. /tmp/tpw-native-route-python.log63 PASS. Original rendered113 PASS clean;
after upstream merge rerun recorded separately. No extracted assets.

New source joins in native-entrance-lifecycle.md: N+14 is shared base-object
activation serial via1093B0/1093C4 delay-slot counter store2AA73C, NOT port guest ID
or pool ordinal. Reused pool slots receive fresh serials; prior park activation
history unresolved. Accepted mode13 scans50 increasing-Z cells with sticky native
tile.byte7&8 latch before acceptingkind2/13; ParkPaths.Open/IsEntrance are not
complete substitutes. Fee acceptance sums actual NativeRideValue+1D4, shops/features
contribute0, strict fee<cash, class thresholds decoded; saved/UI fee override join
still open. Source addresses and limits in findings, not inferred runtime values.

NEXT: incoming owner/controller request+result timing, movement15->staging->event9
->movement16->two incoming groups->head release->one acceptance/charge->mode13
->normal handoff. Native route pool/globalbudget/readiness remain explicit adapter
boundaries. Never derive native serial phase from Guest.Id, membership from nearby
walkers or path cells, or assume synchronous BFS equals native request scheduling.
Cow owns UV/fountain/bubbles and now Q/E rotation bug on ANOTHER host. Keep their
changes; xref518a16f fixes JAL/JR delay-slot register liveness after our raw control.
No restarts, no private bot files, owner disc read in place.

Post-checkpoint merge b836966 incorporates main518a16f (xref-only changes).
RAM-only owner ELF sweep now finds BOTH1093B4 load and1093C4 delay-slot store
for2AA73C; control asserted. Postmerge actual Viewer smoke113 PASS, exit0, no
errors/leaks: /tmp/tpw-native-route-postmerge-smoke.log. Clean worktree before
this evidence append; pushing research branch, not main. Next: complete the
incoming controller, with acceptance/staging/order; don't redo actuator tests as
if that alone resolved the shipping backlog bypass.


## Incoming controller checkpoint — September24,2026, research branch ONLY

New NativeEntranceFlow uses actual Guest references + owner leases, queued async
request/result tokens, two incoming linked lists and staging P/R/sharedE. Tick order
pump->groups->coordinator->active; manual leases avoid a generic second Walk step.
Head phase32, queue spacing64, same x for BOTHgroups (nonzero-group preliminary
x+256 is overwritten by common152868), literal>=11 selection is ONEflip, notcap.
P++ mode15complete; P--/R++ onlymode16complete; broadcast gateR<11 notrecipientquota.
Mode-purpose reset2 aftercompletion. Source precision in native-entrance-lifecycle.md.

Viewer.Entrance.cs OPT-IN flag --experimental-native-entrance makes actual busborn
guests take this controller immediately; Bounds usesactual incomingmembership;
sharedE receivesstagingproducer. NativeEntranceAcceptance consumesactual BusObjects
NativeRideValue sum, strictfee<cash, classmath, managercount-beforefinance andfee
rereadcredit/debit. Mode13 releases SAMEguest/needs/actor atcentre. No arbitrary
nearby-walker queue counts. Defaultmain behavior still unchanged.

NOTRELEASE-READY: next-tick publicBFS adapter, unbounded directslots, explicit model
readinessbypass; ordinary constructorfee150 only (save/UI overridesunjoined); RNG
streams/order notnative; secondstagingfamilymissing. RejectcallbackHOLDSlease andlogs
missingdeparture; native sharedactivationserial/idleordering/stickyA4 remainunported.
Mode13scan restrictedprovenheadpoint2flag8 case, boundsmanagedsafety. No fake general
tileflagarray. Clear/vanished cleanupmanagedsafety, notunreadnativeindirectcallbacks.

Actualautomatic Viewer test NativeEntranceFlowSmoke: buildsrealpositive-value
attraction viaUI, waitsforREALbus birth (no manuallyspawnedguests), fee10 explicit
fixture anddisablesFURTHERbatches afterfirstdropoff. JUNGLE649checksPASS, observed
staging/memberships, sameactor/identityeachupdate, onefee debit+credit, noleftover
P/groups/lease aftermode13. This firstbatchisoneguest: hookup proof, notbusypark
equivalence. Core109flowchecks covermultimember/12broadcast andphase/countlimits;
23feechecks coverboundaries/order/rereadcash. Existing76cursor/75actuator retained.

5behavior mutationscaught8/4/2/11/8 failures (genericdoublestep,period16,threshold12,
missingPdecrement,repeatacceptance). Initialperiod16mutationPASSED! Firstrelease
occurredafter16 andteststoppedbeforethenexthalfperiod. Extendedreadyreplacement-head
observation tofull32period, nowcatches4failures. Allmutationsrestored; no test deletion.
Fullgates /tmp/tpw-incoming-final-matrix: J/Fpass,ONLYexactThrillGrill/MoonBuggiesreds;
109+23eachworld (final fee-read rerun below). /tmp/tpw-incoming-final-runtime ALL11PASS.63PythonPASS. Eightactual
Viewerexperimentalworld/variant runs underway /tmp/tpw-incoming-eight-viewers.

NEXT: resolve/adapt actual native route-service pool/readiness beforedefaultrelease;
close sharedactivationserial andnormaldeparturecontinuation + rejectstate26route
producer, cancellation callbacks andfeeoverride join. Do NOT releaseblockedrejects
byteleport/deletingthem or guessguestIDserial. This ismeaningfullyconnectednow, but
experimentalservices/rejectionhold are explicit blockers, not a request to merge main.
No restarts/extraction/privatefiles. CowownsUV/bubblesandcamera; latestmaincamera
52e576e should be merged aftercurrentrenderedgates (notduringclearingbuild).

Final incoming checkpoint evidence after raw fee-order correction and camera merge:
`cc9422d` implementation, followed by merge of main52e576e. Raw210BD4 requires a
SEPARATE class-fee read aftersum/RNG, so success readsfeeTHREEtimes; guestcashreload
is afterfinance210D38. Corrected before final gates; changed-class-fee mutation
caught2failures (usingoldquote despiteperformingallreads).23feechecks now required.
/tmp/tpw-incoming-release-matrix:109flow+23fee eachworld, J/Fpass, ONLYexact2retailreds.
/tmp/tpw-incoming-release-runtime all11PASS;63PythonPASS.
/tmp/tpw-incoming-release-viewers: ALL8actual experimentalViewer world/variant runs **[CORRECTED 2026-09-25: the runner's `--map=WORLD 2` matched no label, so every park-2 case loaded FANTASY terrain_1; only the four terrain_1 parks were tested. Rerun on all 8 real parks: /tmp/tpw-cp1-final, see the CP1 entry.]**
PASS649each, exit0/noerrors/noleaks; fee10 fixture +actualbusbornidentity, notmainparity.
MergedCameraTurnAudit independentlyPASS10. Earlier /tmp/tpw-incoming-eight-viewers
alsoall8pass butfinalrelease-viewers is the post-fee-correction/post-camera evidence.
ResearchbranchONLY; no default release/main merge. Default entrance remainsbypassed.

NEXT positive lead already read: findings/native-route-slot-pool.md. Capacity is
literal1000 at192770, NOT2047 inferredfrom11bitlinks.192728 reset clearsONLYallocated
bit, hint37E118=0 and free2E2924=1000;192768 forwardscanalloc,1927F8 singlefree
minhint,192840 chainfree. Slotarray3AE1B8 stride4. No guards ondoublefree/cycles in
these bodies. Need actual sharedpool/cursor/requestservice consumers andreset/
partialfailure timing, notanotherunusedcounter. Readiness anddeparture remainother
releaseblockers. All3delegatesusedthisturn; no moreuntilnewturn. No pendingbuilds.

Peer review follow-up September24: cowacceptedgroup/activeordering; parentraw14DA98/
14DAA4/14DAB4 provesnativePREPEND. RepeatedLive.Any +NativeRouteState.Contains now
O(1)referenceHashSet. SeparateIDmultiplicities preserveexistingReadmitduplicate
semantics/uniqueleaseguard. All3birthsites+Remove/Clear maintainindices; readonly
Guests view preventsoutsideindexbypass. 87actuator checks,113flow,23fee. Integer
Flow.Position now suppliesmode13headprecondition; nofloattolerance. Explicit
DiscardedEntranceGuests census (notWentHome or anewnativecounter), testedrealplan/
needs/identityremoval andrepeatClear. 3mutationscaught2failureseach/restored.
Gates /tmp/tpw-review-final-matrix onlyexact2retailreds; all11runtimePASS;63Python;
/tmp/tpw-review-viewer.log actualJUNGLE649PASS clean. These are reviewhardening,
not changes todefault/main parity or removalofexperimentalroute/departurebypasses.

User20:43 asked whatresearchwasfor; answeredplainlyguesttrafficAFTERbusdropoff,
queues/price/backlog/leaving, bus+smoothalreadyshipped. Mustkeepfutureupdateson
visibleoutcome, notjustinternalcheckcounts. TheydidNOTpause/changeourtask. Cow
ownsride-Xmenu researchnowandfountain/camera; main1b62cd0 addsfountainruntimeaudit.

Read-onlyworker closedpoolproducer details, savednative-route-slot-pool.md:
output1000/search2000/requestrecords10 DISTINCTresources.18D358 directioncompressed
backwardparent builder disposesOLDroutefirst, prependsprivatepartialchain, rolls
backpartialvia18D574->192840 onallocfailure, publishonly18D650success. Cursor191D78
savesnext/freeONE/publishnext; invalidposition191E98 freesWHOLEchain192038. Two
resetcallers149C08/init and18C4C0/globaldisable-reinitialize-enable path; NOTonly
onceperpark. Pendingcancel18DB70cleanssearchrequests notcompletedactoroutput;
liveindirectcallerunclosed. Needactualpool/curor/requestconsumer next, notunusedhelper.
No pendingbuilds, no extractedassets, no restarts. Readonlydelegateused1thisreviewturn.

User20:48 said keepgoing, thenasked20:49 whetherturnstilesresearched/maybenamed
somethingelse. NEW near-termpriority: identifyphysicalturnstile/boothmechanism,
NOTjustmorepathallocatorinternals. Answeredpartly(admissionlogic), andaskedwhich
part: themedarchdoors vs smallerbarriers/boothsinfront. Readonlyworker foundnative
Gatescontrollerasset489/category0E, init15178C->13C600/13C650->3953E4, update13C780
usesparkopen14E538 andsection5records1(open)/0(close). Realtrigger, NOTperguest.
Saved findings/native-gates-controller.md. ticket_booths staticterraincandidate,
no correspondingordinaryAPS/morph/UVchannelsfound, NOTproofno proceduralmechanism.
Cowclaimeddoor01/02turnstiles andRSEdrivesit; clarifiedthoseclaimsprovisionaluntil
actualconsumer/useridentification. TheyownXbuttonselectedrideGIZMOresearch, leaveit.
Currentreviewpatch2c1d59b mergedmain e96412b(fountainaudit); freshupstreamTexture
runtimeauditPASS /tmp/tpw-review-upstream-texture. Reviewchanges readyto push.
Poolreset/builderresearchsavedbut don'tignoreuserturnstileclarificationnextturn.
Readonlydelegatesused2thisreviewturn. No pendingbuilds/restarts/extraction.


Userclarification20:57/21:00: meansBOOTHSOUTFRONT, knowsstaticmesh; questionwas
BEHAVIOR, notanimation. Replied21:03corequeue/payment/crossingalreadyresearched
atboothpoint, notarch; rejecteddepartureunported. DO NOTkeepchasingmovingparts
oraskwhichpartagain. Savedfindings/native-booth-admission.md. PositiveJungle
queue/paymentpoint29.5,15.5 insideboothbounds; nativeinitializedpathgoal29.5,19.5
afterkind0C/0E->2 scan, conditionalunchangedtiles+allocation. PayBEFOREcrossing,
mode13completionstate0nofee. Secondstagingfamily3952AC isGUARDS positivelyvia
Load_ReadGuards160448->14AF30/string361368/1416D0, notboothattendants. Guardtraffic
unported. No provenadmission-specificaudiovisualcue; noabsenceclaimneeded.
Resumeincoming/departure/pathresource work, notgeometrydetour. CowownsGIZMO
bindingsperuser21:01. Merged0fc2cc7gatedoccorrection; no codechangefromthatmerge.
Readonlydelegatesused3thisreviewturn, noneleftuntilnewturn. Earlierreviewgates
pass113flow/87actuator/23fee,11runtime+upstreamtexture,63Python,Jungle649rendered.
Researchbranchstillnotdefault/main; sourcepoolnextstepsareinlatestpoolfindings.


## Shared output route-pool consumer — September24 research branch

Actual NativeRoutePool->NativeGuestRoute->GuestWalk->NativeEntranceFlow->Viewer
connection now replaces unlimited copied-array routes for native-owned guests.
Packed1000slots, nativeallocationhint/freecount, reverse-prependlinking, partial
rollback, oldroutefreedBEFOREreplacement, exhaustion!=ownerrefusal. Sameguest/
owner/positionretainedemptyduringretry. DirectallocationBEFOREtargetfactory/RNG;
mode13lazycandidatestryallocbetweenyields inSAMEtick. Arrivaldoesnotfree; next
retirementfreesONEsavedlink; invalidbounds/replacement/remove/clearfreeownedchain.
Clearactorchainsbeforepoolreset; managedinvalidlink/doublefree/cycle/epochguards
areexplicitSAFETY, notclaimsretailchecks. Privatebuilderlinear(notN-squaredtails).

NativeRouteOutput18D358selectionappliedtoexistingBFS: walkbackwardsINCLUDINGroot,
rootdirection0 vsnonrootdxsign6/2 ordz0/4, previousFFFF; emitonchange, firstexact
endpoint thennodecentres; reverse. RootNOTunconditionallyskipped; singleton1slot.
Thisportsoutputtransform, NOTnativepathsearch. Elevenoldcursorassertionsfailed
ONLYlocalindexassumptions; nowactualreverseallocatedhandleexpectations preserve
allposition/phaseassertions. Noordinalfacadekeptbesiderealpool.

1082newchecks throughactualpool/walk/controller inclfullcapacity,partialfailure,
replacementlosesoldroute, directfactorynoRNGwhenfull, same-ticklatergoalretry.
6mutationscaught34/8+guard/9/8/5/2+guard; scriptoutertimeoutduringRESTOREonly,
sourcesrestoredchecked,separaterestored1082PASS /tmp/tpw-pool-restored.log.
JungleactualViewer651PASS /tmp/tpw-pool-final-viewer.log nowassertsrealpoolused
andfullyreturnedbybusguest. Old76cursor/87actuator/113flow/23fee pass. Fullmatrix
/tmp/tpw-pool-final-matrix alreadyJ/Fpass ONLYexactThrillGrill/MoonBuggiesreds;
runtime11/Python runningascheckpointwritten, inspectexitfilesbeforeclaiming.

STILLNOTMAIN: native searchnodes2000/requestrecords10/budget/readinessunported,
guards+ordinarylegacywalkersnotaccountedinpool, constructorfeeoverrides/RNGunjoined,
REJECTEDGUESTSHELD untilproperdepartureproducer. Userapprovedkeeponit; nextvisible
priorityproperleaving/rejection, notgeometry. Needreadiness/nativepathservicealso
closedbeforedefaultparityrelease. All3delegatesusedthispoolturn. Noextraction/
privatefiles/otherhosts/restarts. CowownsselectedrideX/listboxUI; don'teditlane.

Shared-pool checkpoint finalized at b334704 (research only). Fullgatescomplete:
/tmp/tpw-pool-final-matrix J/Fpass ONLYexactThrillGrill/MoonBuggiesreds,1082pool
+76cursor+87actuator+113flow+23fee eachworld. /tmp/tpw-pool-final-runtime all11PASS;
63PythonPASS. /tmp/tpw-pool-eight-viewers ALL8actual Viewer world/variant651PASS, **[CORRECTED 2026-09-25: the runner's `--map=WORLD 2` matched no label, so every park-2 case loaded FANTASY terrain_1; only the four terrain_1 parks were tested. Rerun on all 8 real parks: /tmp/tpw-cp1-final, see the CP1 entry.]**
exit0/noerrors/noleaks, witnessesrealsharedpoolconsumedandreturnedbysamebusguest.
Nomainmerge, defaultsunchanged. Pendingreadiness/search/departurelimitsstilllogged.

Nextreadinesspositive readforhandoff:191E10 getsvisualviaowner virtual+14; no model
returns1. Unlessrequested low5bits(owner+30) are9or13, returns1. For9/13 reads
visual+14 through10EC48 andpermitsonlyreportedfirstbyte9or13.191E98 bypassword
2E2920 FILEINITIAL0 (not1); boundedxrefonlyfoundload191EC8, notproofimmutable.
No-slotbranch precedesreadiness. Do NOTinferreadinessfromactorvisibility orinvent
APSslot9/13 mapping: thesearelogicalcontrolIDs, actualvisualgetterjoinstillneeded.
Sourceoutputselection/directallocationcomplete,3delegatesusedthispoolturn.
Userapprovedcontinuing; nextvisiblegoalproperrejecteddeparture, notmoregeometry.


## Rejected departure consumer — September24 WIP checkpoint

Normalrejections nowactualmode14->stagingoutgoing->mode16->mode9->realdeparture,
notpermanenthold. NativeFlow.Add optionalserial; nullkeepslegacyexplicitboundary.
ViewerNativeActivationSequence seededfromownerELF2AA73C throughNativeBusCatalogue
ImageInitialActivationCounter, consumesactualrepresentedPLACEDobjects+GUESTbirths,
NOTmeshes/pooledstorage/bustrips. PersistsViewerparkresets. OriginWARNEDunverified
nativehistory +portexecutedtickorigin, NOTGuest.Id phase; nofullparityclaim. Positive
extra nativefamiliesballoons/litter/allstaff, noUIprerequisitedemonstrated. Source
findings/native-activation-serial.md. Actualstartup/save/compositehistoryunclosed.

Flowflags21initialdeparture,23immediateasyncfallback,1finalpoint0; RNG1consumed.
Async14fallbackrefusalstays0B/notoken; alternatefailurestate0; mode9failurestate5
withsignedhappiness/unknown78effects. OwnerheldatTHOSEunportedrecoveryboundaries,
notnormalreject. Deferredstickysetonlyphase/initialrefusal; tallyBEFOREactive
dispatch, includesfinalremovedpass. Experimentbususesownedcohortpressure; normal
legacydepartures/guardsnotcompletepopulation. Directmode16allocbeforehelperRNG;
outgoingdoesNOTjoinincominggroup1. Mode14headingpi20DC6Cnowexplicit; replacement
preservesexistingnativeheading. CallbackackAfterResultAFTERhandling(notbefore
alternate request), preservingcurrentrecordoccupancy. DeferredBFSstillnotnative
search/resourceplanner. Sourcefindings/native-rejected-departure.md.

Firstconsumerbugfound: AssignEntranceRoute resetLeavingplanbacktoEntering, so
CompleteNativeDeparture correctlyrefused. PreserveownedLeavingthroughbothroute
assignmentpaths; keptfinalguardstrong. Bodytestsincludeplancontinuity.
53departurechecksPASS;8mutationscaught2+guard/5/10/2/2/1/1/1 andrestored. Actual
NativeRejectedDepartureSmoke JUNGLE2399PASS:2realplacements(thusserial!=Guest.Id),
realbuscohort3, explicitmaxfee cashrejection, sameguest/actoruntilbuspoint0, nofee,
WentHome ratherthanDiscard, realplan/needs/rendercleanup, sharedpoolreturned, last
stickypressurecountthen0. Existingacceptedflowmustremain651, matrixcurrently
/tmp/tpw-departure-final-matrix onlyexact2retailreds, all11runtime/63PythonPASS.
Finaleight-park BOTHaccepted+rejected scenes stilltorunaftermainmerge.
All3delegatesusedthisturn. No extraction/restarts/privatefiles/otherhosts. Cowowns
RMB/listbox/shopinfo; mainmovedto0f2ab51, mergewithoutdroppingtheirUIaftercheckpoint.
Useraskedprogress22:32; reportedvisible normalrejectionworking, stillnotmain and
remainingfidelitygaps—not"finished". Nextnative readinessthenrequest/search/feejoins.


## Departure landing/review snapshot — September24 22:55

6e37ebc rejected-departure consumer pushedRESEARCH;3bccfc3 mergesmain0f2ab51.
ALL16actualViewer cases(/tmp/tpw-departure-eight):8accepted651each+8rejected2399each, **[CORRECTED 2026-09-25: the runner's `--map=WORLD 2` matched no label, so every park-2 case loaded FANTASY terrain_1; only the four terrain_1 parks were tested. Rerun on all 8 real parks: /tmp/tpw-cp1-final, see the CP1 entry.]**
exit0/noerrors/noleaks. Explicit scene-prefixPASS checked, notsubstringBYPASS.
Cowreviewconfirmedbothorderings; diagnosedfinallymaskinghandlerexception. Fixed
withoutswallowing: handler-onlyoriginalstack, ack-onlylabel, bothAggregateoriginals;
6newchecks(59total); restoringoldfinallyfails2thenrestoredPASS. Add remainsstrict
newguestownershipprecondition, notroutineTryAdd orsilentlydroppingguest.
Nextpositive: visualreadiness191E10 checksCURRENTlogicalbyte atmodel+4C through
10EC48, notrequestedbyte+4F.10E910queuesdesiredbyte,10EA38commitsitonlyatplayback
boundaryselectedby1ACFC0. Table2AAD48mapslogical9->APS1v0,13->APS0v0;
logical11has8weightedentries, NOTdirectAPS11. Needsactualpresentationconsumerjoin.

Final reviewed gates COMPLETE: cf70714 callback diagnosis fix, thenmainf23427c
DOCSONLY merge. /tmp/tpw-departure-reviewed-matrix all59departurechecks/world,
J/Fpass ONLYexactThrillGrill/MoonBuggiesretailreds; runtimeall11PASS. Earlier63Python
PASS andALL16actualrenderedscenes retained. No latestsourceconsumerchangessince **[CORRECTED 2026-09-25: the runner's `--map=WORLD 2` matched no label, so every park-2 case loaded FANTASY terrain_1; only the four terrain_1 parks were tested. Rerun on all 8 real parks: /tmp/tpw-cp1-final, see the CP1 entry.]**
gatesexceptdocs. Researchbranchonly; do notmergeenableasnativecomplete.
Nextboundedpackage: join native guest logical-animation dispatcher to ACTUAL actor
and movementreadiness. findings/native-guest-animation-readiness.md hasrawtable,
currentvsrequestedbytes, weightedvariants, boundaryandflagcaveats. Avoidunusedhelper:
actualrenderedsameguestmustWAITwhilecurrentnot9/13 thenmoveafterrealtransition;
cross-controls9<->13permitmotion,novisual!=zerohandle. Nativecaller/requestflags,
initialization,clockandupdateorderstillneedreadingbeforeimplementation. Do not
justcopydesiredbyte, useactorVisible, orrenameAPSslots9/13. Keepdefaultgaitunchanged
untilexplicitoptinconsumerproven. CowactiveViewer.csUI; prefernewpartialseam and
coordinatepreciselybeforetouchingitsgaitsection. Userapprovedcontinuing, no pause.

## CP0 + CP1: resumed by tinyclaw — September 25, 2026 UTC, RESEARCH BRANCH

VoX asked me to resume astraclaw's TPW work while astraclaw is out of ChatGPT usage (back
Thursday, October 1).

**CP0.** Merged origin/main (22 behind) into origin/astraclaw/native-entrance-flow at eab9c3b,
giving d6310c6.

- Core and game build with 0 errors.
- ~~All 16 real Viewer scenes pass under xvfb: 4 worlds × 2 parks~~ **CORRECTED below**: the
  runner's `--map=WORLD 2` matched no map label, so all four "park 2" cases loaded FANTASY
  terrain_1. The real count is the 4 terrain_1 parks, with FANTASY-1 run five times; terrain_2 was
  never run. {NativeEntranceFlowSmoke, NativeRejectedDepartureSmoke}.
- Every run exited 0, with 651 checks per accepted scene and 2399 per rejected scene. These match
  astraclaw's counts from 22:55.

**CP1 package:** the dispatcher behind the readiness join. It is core plus checks only; the viewer
consumer is the next package.

- `NativeLogicalAnimationTable` reads all 22 logicals and 33 descriptors from SLES_500.32 at
  2AAD48, and rejects any run that is not contiguous.
- `NativeLogicalAnimationControl` implements 10E910, 10EA38, 10E988, 10E800 and 10EC48, and
  starts at the 10ECF0 state (FF/0/0/FF).
- `NewlibRand` is the 29CF08 generator. It is labelled as not the console's shared stream.
- `ParkSimAudit --logical-animation-only` runs 50 checks, and they are also in the full run.
- Teeth: 9 mutations each fail by name, then the file is restored. The mutations were:
  - readiness taken from pending;
  - equality readiness;
  - an immediate commit;
  - skipping the last pair;
  - a one-shot that forgets its logical;
  - a fresh model starting at 0;
  - a second draw on retention;
  - an equal request that clears pending;
  - a cut without flag 2.
- The retention mutant first SURVIVED because a constant stub made a second draw identical to the
  first. A queued stub now fails it.

**The findings closed:**

- initialization;
- the flag-2 meaning;
- the ms playback clock at 30 fps;
- the guest request path 1921D0 and its flag source 140880;
- the census of requested-logical writes, where 13 is the walk and no guest producer of 9 was
  found;
- section 0 is a 16-frame AlternatePlayer record, the same length as the drawn section-1 walk;
- the uninitialized-variant defect in the native.

**Next package:** drive one control per entrance guest at the actual playback boundaries of its
drawn record, with requests from the decoded producers (13 at 211A00 spawn and 191D78 route
advance, 11 from 20D628), as an explicit opt-in. Film a guest waiting on a real transition. The
default gait stays unchanged. The open items are listed in the findings: update order, the pause
clock, and the owners of 140C70 and 141020.

## CP2: readiness join landed on the research branch; eight-park evidence corrected — September 25, 2026 UTC

**Package:** `--native-guest-animation` (together with `--experimental-native-entrance`).

- 191E10 readiness over the 2AAD48 dispatcher replaces the entrance bypass.
- One control and one playback run per flow-owned guest. The requests come from the decoded producers
  only:
  - 11 at activation (20BD34);
  - 13 at every slot advance (191D78);
  - 2106E8's idle picks while the guest is in state 0B.
- The dispatcher's record is drawn through a hook in `Gait()`, placed after its seated guard, which
  cow tools approved.
- Handback clears the gait caches.
- The labelled adapters are listed in the header of `game/Viewer.NativeAnimation.cs` and in
  findings/native-guest-animation-readiness.md.

**Evidence**, at this commit's tree, built before the runs:

- **Core:** `ParkSimAudit --logical-animation-only` runs 62 checks (log: /tmp/tpw-la.log), covering the table, the control,
  the playback beside it and the idle picker. The PASS line and teeth are in the earlier CP1 entry:
  9 core mutations, each failing by name.
- **Smoke teeth:** `NativeAnimationReadinessSmoke` catches 4 mutations by name: readiness bypassed
  ("stepped anyway"), no advance producer ("requests seen 11,14"), dispatcher not drawn ("section 0
  but drawn slot 2"), and no handback release.
- **Viewer matrix:** 24/24 exit 0 in `/tmp/tpw-cp1-final/manifest.json`. That is all 8 real parks ×
  {readiness, entrance flow, rejected departure}. Each case asserts from its own log which
  WAD/terrain loaded; the assertion flags every old park-2 log and passes every park-1 log.
  - The entrance and rejected-departure check counts are fixed by the code (651 and 2399 in every
    park), so they are not evidence of coverage. Readiness counts vary by park: 6993 to 8092.
  - Blocked guests per park range from 1 to 3 of 12–13. The longest wait is 1320 ms, on logical 11's
    40-frame section 6. No guest stepped while blocked, every blocked guest moved again after 13
    committed, and there were no deadlocks.
  - FANTASY-1 and FANTASY-2 print identical readiness statistics, but each loaded its own terrain and
    has its own entrance column (39 against 37). The corridor geometry is the same shape shifted two
    cells, so the result is translation-invariant rather than a repeated case.
- **Film FAILED as evidence.** `/tmp/tpw-film` holds 28 frames that were generated and then
  inspected. The build menu covers the frame and no guest is visible. Nothing visual is claimed;
  fixing this is part of the next item.

- **Branch gates, on this tree (staged, not yet committed, when run):**
  - `tools/audit_matrix.py` in /tmp/tpw-cp2-matrix gives `known_retail_failures_remain` with runner
    exit 2: JUNGLE and FANTASY PASS, and HALLOW and SPACE carry only their retail reds. Every existing
    REQUIRED_CHECKS minimum is met. Logical animation has no REQUIRED_CHECKS entry yet (queue item 2).
  - `tools/runtime_audit.py` in /tmp/tpw-cp2-runtime: all 11 scenes pass, exit 0.

**Corrections, made openly:**

- The eight-park runner used since the incoming checkpoint passed `--map=WORLD 2`, which matches no
  label. Viewer.cs:506–511 silently keeps the default row, so every "park 2" case ran FANTASY
  terrain_1.
- Annotated in place: progress.md 2313, 2424, 2479 and 2493, native-rejected-departure.md:106 and
  the CP0 entry above.
- The rerun above is the first time the native entrance flow ran on a terrain_2 park, and it passes.
- 195E28 is a call counter, not a clock (4 callers, all inside the planner). The planner's service
  budget is about 99 node expansions per call. Annotated in native-route-slot-pool.md and
  native-guest-motion.md.

**Plan review:** VoX asked for 1 fable and 3 Opus reviews of plan.md. They are complete, and the
revised plan follows in its own commit. It replaces this entry's "next package": the native planner
is deferred with triggers.

## Plan review round and rewrite — September 25, 2026 UTC

VoX asked for 1 fable and 3 Opus reviews of plan.md, then a revision. The reviewers were read-only.
Fable's full text is at reviews/2026-09-25/fable-whole-plan.md. The Opus reports reached tinyclaw as
agent results and are summarised here.

| Reviewer | Angle | Key findings (each spot-checked by tinyclaw where it mattered) | Adopted |
|---|---|---|---|
| fable | Whole-plan structure | Four sections claimed to be "current", and three were stale. Milestones had no status. The branch mapped to no milestone. The rendered runner was not in the repo. There was no agent-side done. The `~/tpwps2` main was 251 commits behind. The bus witness line was stale | Structure, M8, DoD split, runner into tools, witness fix |
| Opus A | Sequencing and completeness | "Complete" is undefined without a feature set. Seven branch packages changed nothing in default play, and strawberry asked "what are u actually researching" (09-24). M5 was never started. There is no CI. There was a tracker request from strawberry (09-20). Planner not verifiable without an oracle | Scope matrix, CP3 broadened, CI, animation for all guests, the tracker |
| Opus B | Evidence and gates | The park-2 blindness hit 3 more claims. The runner's PASS substring matched "BYPASS". Gates lived in /tmp. REQUIRED_CHECKS was missing logical animation. The matrix accepted a dirty tree. Constant counts were read as corroboration | All of it, as queue item 2 |
| Opus C | Technical risk and landing | Blockers sorted into gate, trigger and won't-fix. **195E28 is a call counter** (verified by tinyclaw). The fee is 150 for every ordinary park. The branch merges cleanly, so land it in slices. Flags are read on hot paths. Smokes reach about 30 private members by reflection | Blocker table, the labelled-adapter category, slices A/B/C, flags read once, a test seam |

**Where the reviewers disagreed:**
- **The native A\* planner.** Fable put it next, with findings first. The Opus reviewers deferred it, for
  three reasons: no oracle exists, the tile-kind producer is missing, and the entrance corridor gains
  almost nothing from it.
- **Decision: defer it with triggers** (plan section 6). The cheap corridor passability check, queue
  item 9, decides whether it is ever needed.

## CP2: evidence infrastructure (queue item 2) and the corridor check (item 9) — September 25, 2026 UTC

**Item 2 landed at 783225b.** All four gates ran on that clean committed tree, built before the runs,
with output in /tmp/tpw-783225b:
- unit: 79 tool tests, OK.
- `tools/audit_matrix.py`: `known_retail_failures_remain`, runner exit 2, `landing_evidence=true`.
  All 8 parks ran, terrain_2 included. JUNGLE and FANTASY pass on both terrains. HALLOW and SPACE carry
  only their retail reds on both.
- `tools/runtime_audit.py`: 11 of 11 scenes, exit 0.
- `tools/viewer_matrix.py`: `all_cases_passed`, runner exit 0, 24 of 24.
  - Every case's single `[map] loaded` line equals its request.
  - The source snapshot was clean before and after.
  - Readiness check counts run from 6993 to 8092 by park. Entrance (651) and rejected (2399) are the
    code-fixed counts, not coverage.
- The film at /tmp/tpw-film4 has 56 frames, two angles per tick. It was generated and then inspected:
  the UI is hidden, the guest is visible standing through the WAIT on logical 11 section 6, and then
  walking.
- The two named regressions were reverted and each failed by name before being restored: the old
  `--map=JUNGLE 2` form and a log whose only PASS is BYPASS.

A near-miss worth recording: the viewer matrix's source guard compares `git status --porcelain`
before and after. An untracked findings file written into the worktree during the run would have
flipped `dirty` and failed the whole run as `source_changed_during_run`. It was moved out before the
run ended and restored after. Do not write into a worktree that a provenance-checked run is reading.

**Item 9: the corridor passability check is done.** The findings table is
findings/native-route-planner.md, the 18C928 kind arms from jump table 364690, set against the port's
BFS.
- Verdict: **the passable sets differ in principle; the difference has not been reproduced.**
  - The native planner leaves kinds 2, 4 and 13 only in the directions the tile's link byte (+2)
    allows. The port's BFS moves between any adjacent open cells.
  - Flag 0x23, the mode-14 alternate, admits open ground at cost 2.
  - Queues (kind 4) need flag 0x10, which no entrance request sets.
- Under the plan's rule a mismatch opens the planner package. That package is still blocked on its
  input: the port builds no native tile-kind array (placement 1E2AD0, link builder 1E70F0), so there is
  nothing to run the native arms against. The planner therefore stays deferred.
- The trigger is now concrete (plan section 6): build the tile-kind producer, then rerun this
  comparison on real tiles.
- Route LENGTH agrees wherever the passable sets agree. With uniform cost 1 and a consistent
  Manhattan heuristic, A* returns a shortest path as BFS does. Only the choice among equal paths
  (random 1-of-4 direction order, two-ended tie insertion) differs.

**Item 12 census, first read.** The audit's ride census over all 8 parks is unchanged by terrain.
Every park-2 row equals its park-1 row, so the ride set belongs to the world, not the terrain:
- JUNGLE: 6 of 21 rides poll a track subsystem; 6 took nobody.
- SPACE: 6 of 22 poll; 6 took nobody.
- FANTASY: 6 of 19 poll; 6 took nobody.
- HALLOW: 8 of 23 poll; 9 took nobody. Thrill Grill is the known exception (findings/visitors.md).

Correction: findings/visitors.md records SPACE as "6 of 23". Every run on disk since 2026-09-24 03:45
reads 22. The 09-23 figure has no surviving log and is annotated as unverified there.

## CP2: departures, stranded guests, soak and CI (queue items 7, 8, 10, 13) — September 25, 2026 UTC

**Item 7: ordinary departures through state 26. Landed at 4a98b64.**
- An admitted guest who WantsToGoHome now leaves through 210D70, the same departure a booth
  rejection takes. It runs phase-gated mode 14 with sticky A4, then staging, then mode 9 to point0, and
  counts as WentHome.
- The seam is ParkVisitors.NativeDeparture (cow's file).
- Details and the 20C930 decode are in findings/native-ordinary-departure.md.

Gates at 4a98b64, clean tree, in /tmp/tpw-4a98b64:
- all 23 projects build with 0 errors;
- 79 unit tests OK;
- the audit matrix gives `known_retail_failures_remain`, exit 2, `landing_evidence=true`. Only
  Thrill Grill (HALLOW 1/2) and Moon Buggies (SPACE 1/2) are red, and the departure family is at 63
  checks in every park;
- runtime 11/11;
- the viewer matrix is 32/32 with loaded == requested. The departure scene passes in all 8 parks (46
  checks; 50 on FANTASY).

Teeth, each failing by name:
- with no seam: "a guest retires only from the native departure, never the legacy gate walk";
- with no phase gate: "mode14 is requested only on the guest's activation phase";
- headless, the seam never consulted, and a minted serial for a stranger.

**Item 8: no stranded guests.** The state-0 and state-5 holds are resumed one update later by a
labelled adapter (NativeEntranceFlow.Resume):
- An admitted leaver is handed back to ordinary visiting through ParkVisitors.ReleaseNativeDeparture
  (cow's file).
- Any other guest held at state 0 retries state 26.
- State 5 requests the bus leg again.
- GuestWalk.ReleaseNativeRoute now accepts a lease holding no slot, which is one standing still.

Evidence:
- NativeDepartureDisruptionSmoke, JUNGLE-1: the park path is dug up under 3 broke leavers. 20 holds
  were resumed and all 20 handed back, and no guest was held more than one update. Once the path was
  relaid, all 8 guests left at point0 with nothing held.
- Teeth, each failing by name:
  - keeping the holds: "guest 5 is not left in a state-0/5 hold", plus 5 headless checks;
  - before the ReleaseNativeRoute fix: "0 handbacks".

**Item 10: busy-park soak.** NativeEntranceSoak, described in findings/native-entrance-soak.md.
- It runs the executable's LoadsOfKids batch rule, then a broke exodus.
- JUNGLE-1: 100 births and 100 went home, peak group 12 with 5 flips, peak pressure 56 with 1 veto,
  and the park drained with all 1000 slots free.
- Teeth, each failing by name:
  - the 30-guest test removed: "refused 0 batch requests";
  - departures left uncounted: "tick 500: births 20 = went home 0 + discarded 0 + live 19".

**Item 13, first half.** `.github/workflows/ci.yml` builds every project and runs the disc-free tool
tests on push and pull request. It lives on the research branch only, so nobody's pushes to main
trigger it until it lands there. The disc, rendered and engine gates stay local.

The 8-park evidence for items 8 and 10 is the final gate run cited in the HANDOFF.

## Archived plan checkpoints (moved verbatim from plan.md lines 58-329 on 2026-09-25)

## Current user-priority queue (September24 post-native-consumer landing)

1. **Research the bus**, explicitly requested by strawberry1552630912481624105. Start
   with actual asset identity/script inputs, then trace creation, movement and guest handoff.
   A timed-out inventory worker returned no finding; it does not establish absence.
2. Native weighted selection and relief residence passed peer review, independent review
   correction, four-world core, eight runtime scenes, rendered/mutation controls. See
   progress.md for exact revisions/results. Do not repeat the old gate/walking fixes.
3. Keep native-AI limitations explicit: lifecycle/transport adapters, local movement,
   remaining state0 arms, live setting/track-cache bindings, region and RNG integration.
4. Cow owns gate/protected paths, money HUD, path costs, audio and particle research.
   Preserve that work on every merge. No restarts or duplicate scheduled jobs.

## Latest CP4 correction — native relief state and decoded selector (September24)

The post-walking trace found a real contradiction, not another optional polish task:
**Small Toilet having no RSE LIMBO does not establish native visibility.** Guest arrival
explicitly hides accepted kind2 relief customers and starts a522-update deadline. The
old visible-standing-toilet explanation is retracted in findings/visitors.md; see the
address-backed replacement in findings/native-shop-flow.md.

Decision: **split and reorder**, not infer a new rule from the scripts again.

1. Next bounded production package: validated compiled relief inside-entry walking,
   native accepted-service hide/deadline/reveal/retained-position lifecycle. Keep occupancy
   and availability distinct from SHOP purchases. Use executed park ticks, not a new
   seconds constant. Resource notification is slot5 variant1/0 through1ABC80; verify
   its real presentation consumer rather than leaving a correct timer with no animation.
   Explicitly handle demolition, refusal/occupied entries, zero-time calls and missing
   presentation data; no loss/duplication or premature relief on a script-only handback.
2. The scorer equation, exact lookup tables and conditional non-FIFO history writer are
   now decoded in findings/native-destination-score.md. Final ordinary/coaster/tour/track
   value getters and compiled relief support are decoded in ride-value-producer.md.
   Port selection only with known producers/runtime identity and controlled enumeration;
   do not silently wire SAM excitement, a constant45, or a guessed four-visit FIFO.
3. Operating-speed/duration script bridge remains a specific integration seam: speed writes
   machine+C0, duration writes variable index3. Resolve the current managed binding before
   claiming native operating state. Renderer/script adapters and missing APS are separate
   from the now-read native guest service mechanism.

Astro owns coordinator/entrances/scorer; cow tools retains money/economy/idle and existing
needs effects. Share pushed branches and independent controls. The stopping condition for
this research package is recorded equations/provenance/corrections, not another speculative
runtime change. Next turn should implement the bounded relief consumer or resolve its
specific bridge—not repeat the table census or ask humans to remember retail behavior.

## Current CP4/CP5 decision — original movement and selection, not cosmetic patches

The user's repeated-visit/teleport report changed the priority: finish this consumer flow before
returning to release-state sweeps. The code trace superseded two plausible render policies:
SAM stand fractions and absent-coordinate arrival-stub fallback are NOT the traced native shop
endpoint. The original walks to the compiled inside entrance centre with directed terminal
permission, then retains that position after service. Preserve the old fallback only where a
validated compiled placed-shop endpoint is unavailable, and report that limitation.

* **Timing complete:** post-completion deadline registration/consumer landed at `c0ee884`, peer
  reviewed and mutation-checked. Executed park ticks are the sole time source; appetite tuning
  cannot move the deadline. This is not a permanent revisit ban or full native AI parity.
* **Walking integration complete at54a58af:** peer-reviewed `astraclaw/shop-terminal-walk` proves physical final-edge
  interpolation AND changing walk-animation mesh, no handback teleport, live-owner/deletion/
  replacement controls, four rotations/worlds, and marked rendered review.
  Do not globally open building footprints or conceal the issue with a view-only animation.
* **Next fidelity gap:** after this bounded slice, evaluate native weighted destination scoring
  and its actual conditional recency writer against the remaining needs-first/random policy.
  A user asking whether it is fully accurate deserves an explicit no until those differences
  are resolved. Trace other facility-kind entrance consumers before extending SHOP rules to
  toilets/rides. Cow tools owns current money UI/economy/idle work; coordinate before overlap.
* **Stopping condition:** land this proven physical flow, document remaining selection/speed/
  service-adapter limits, and stop expanding its audit family absent a new failure. The clean
  source build at65b7370 remains valid only for that revision; CP6 resumes after this user-visible
  defect package, not instead of it. No approval waiting and no repeat bot restarts.

The sections below record earlier checkpoint decisions; this section controls current priority.

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


## Slice A landed on main: native core, no behaviour change — September 25, 2026 UTC

This is plan.md queue item 4. It moves the standalone native core onto main from the research
branch.

**Code:**
- `NativeLogicalAnimation`, `NativeGuestMotion`, `NativeGuestRoute`, `NativeRoutePool`,
  `NativeRouteOutput`, `NativeActivationSequence`, `NativeEntranceAcceptance`, and the additive
  `NativeBusCatalogue` points and counter.
- Their ParkSimAudit families: motion, route cursor, logical animation and acceptance.
- ParkSimAudit's `--terrain=1|2`.
- audit_matrix over all 8 parks, with a per-case park-identity check and a landing flag.
- The cow-approved Viewer.cs selector fix: `--map` and `--ride` fail loudly, and the canonical
  `[map] loaded` line.

**Nothing in default play calls the new core yet.** It is the substrate for queue item 5 (guest
animation for every guest, with cow tools) and slices B and C.

**Docs:** plan.md, progress.md, the review and the branch's findings files, taken only where main
has not changed them since 8ef44b9.

**Evidence:** the gate results below were run at this commit's tree.
- At c5dfe12, on a clean tree, freshly built:
  - `python3 -m unittest discover -s tools -p 'test_*.py'`: 66 tests OK.
  - `tools/audit_matrix.py`, in /tmp/tpw-c5dfe12/matrix: JUNGLE 1 and 2 and FANTASY 1 and 2 PASS.
    HALLOW 1 and 2 show only Thrill Grill, and SPACE 1 and 2 only Moon Buggies. The status is
    `known_retail_failures_remain` with runner exit 2 and `landing_evidence=True`. **This is the
    first time the core matrix covered the four terrain_2 parks.**
  - `tools/runtime_audit.py`, in /tmp/tpw-c5dfe12/runtime: all 11 scenes pass.
