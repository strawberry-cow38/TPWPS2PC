# TPWPS2PC progress / AI handoff

Updated: 2026-09-23. Roadmap: [plan.md](plan.md).

## Current milestone and next action

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
