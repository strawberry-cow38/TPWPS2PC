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
