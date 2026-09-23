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
