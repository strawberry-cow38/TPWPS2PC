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
