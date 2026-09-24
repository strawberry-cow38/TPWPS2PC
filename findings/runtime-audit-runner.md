# Reproducible headless runtime audit gate

`tools/runtime_audit.py` makes the seven headless-safe Godot scenes a fresh-build
runtime gate. It does not replace `tools/lighting-check.py`, the whole-park matrix,
physical UI testing, listening, or manual visual review.

```sh
python3 tools/runtime_audit.py --disc "$DISC" --godot "$GODOT_MONO" --out "$NEW_OUTSIDE_GIT_DIR"
# Optional diagnostic subset; successful subsets are NOT a full-gate pass:
python3 tools/runtime_audit.py --disc "$DISC" --godot "$GODOT_MONO" \
  --out "$OTHER_NEW_OUTSIDE_GIT_DIR" --scenes advisor audio
python3 -m unittest discover -s tools -p 'test_*audit*.py'
```

## Contract

The default gate runs VisitorAudit, RseAnimationAudit, TextureAnimationAudit, MtrAudit,
AdvisorBrowserAudit, RideSoundLifecycleAudit and StandingServiceAudit. It probes the supplied engine for a
mono version, records .NET SDK version, and always runs `dotnet build -c Debug -t:Rebuild`
for the viewer. Debug is the assembly configuration these Godot scene launches load.
There is no skip-build switch. It does not invoke editor import or capture screenshots.

Each result records its actual exit, command, elapsed time, timeout/launch/truncation
state, log path/hash, warnings and coverage verdict. Zero exit alone is insufficient:
required named PASS/coverage witnesses must be present, with no FAIL, Godot ERROR,
SCRIPT ERROR or native resource-leak diagnostics. Ordinary warnings are retained but
not automatically fatal. Visitor and RSE require all six FANTASY/SPACE/HALLOW terrain
witnesses; textures require at least146 surface checks plus the viewer clock witness.
Advisor45/audio37 minimums also require named important lifecycle/locale witnesses.
Standing74 requires matching totals, visibility/ownership witnesses, and real placed
service completion at all four rotations; see standing-service.md for its boundaries.

Advisor/audio/standing checks print sequential assertion IDs. Repeated log lines cannot
replace omitted assertions while inflating totals. This is a logging-only change to
those scenes, not altered test behavior or a proof against deliberately forged logs.
MTR retains its existing all-four-witness summary contract.

Exit0 means every **selected** scene passed. Only `status=all_scenes_passed` with
`full_gate_passed=true` certifies this runner's complete seven-scene set. A successful
subset explicitly records `selected_scenes_passed` and `full_gate_passed=false`.
Exit1 preserves failure evidence. Argument errors exit2 before starting the gate.

## Provenance and failure handling

The output directory must be new and outside every `.git` ancestor; previous evidence
is not overwritten. The manifest is saved atomically before work, after the build and
after every scene, so a later failure does not discard completed evidence.

Source fingerprints include relevant source/project/scene/shader/Python files under
game, core and tools, including ignored-but-compilable C# and the linked visitor
expectations helper. Standard generated directories (`bin`, `obj`, `.godot`, etc.)
are excluded; relevant root build props/targets/global.json are included. Source-directory
symlinks and source-file links leaving the repo require explicit support rather than
being silently followed. Git revision and dirty status are recorded, not confused
with source-file identity. A dirty checkout is allowed and its observed files are hashed.

All Debug output DLLs and dependency/runtimeconfig manifests are hashed, not just the
viewer DLL. Source/assembly changes during the build/run invalidate success. The actual
executing runner/helper paths and hashes are recorded separately from `--repo`, so a
runner launched from checkout A does not pretend its executing code came from checkout B.
Disc and engine hashes are checked again at the end. No environment contents or credentials
are recorded; inherited TPW_* capture/test switches are cleared and only the explicit disc
path is restored. These are observed provenance checks, not a complete MSBuild import-graph,
publisher-authenticity, race-free filesystem snapshot or external native-library attestation.

The existing matrix subprocess helper gains an optional explicit environment, preserving
its previous default behavior. Its per-process timeout and POSIX process-group cleanup
remain in use. Its 8MiB cap bounds log **reading**, not disk writes; a noisy subprocess can
write more before exiting/timing out. Such truncated evidence cannot pass. This runner
has been exercised on Linux aarch64; it is not native Windows qualification.

## Review, controls and measured result

Read-only subagent review caught and prompted fixes for runner-vs-repo provenance,
ignored source visibility, duplicate assertion coverage and partial-suite distinction.
47 Python tests pass:20 existing park-matrix tests and27 runtime-runner controls.
They cover classifiers, engine capability, sanitized environment, evidence-directory
safety, actual filesystem source/assembly fingerprints, fresh build enforcement,
source/dependency drift, timeout/truncation and error-output orchestration.

On September23 US Eastern / September24 UTC2026, a real fresh Debug rebuild and all
six scenes pass with Godot `4.6.stable.mono.official.89cea1439` and Dummy audio. Visitor
and RSE each cover six terrain/world cases, texture reports146 surface checks plus
clock, MTR its four witnesses, browser45 and audio37. The local evidence directory is
recorded in project scratch `tpw-runtime-evidence-path.txt`; its manifest contains the
actual source/output fingerprints and raw per-scene results, not just these counts.

The known HALLOW Thrill Grill/SPACE Moon Buggies reds belong to the separate whole-park
matrix and remain explicit. These successful geometry/runtime cases neither erase them
nor establish playable track services, full advisor integration or native audio quality.

## Standing-service extension, September24 UTC2026

The seven-scene gate passes after integrating peer c7eb67f with the standing-service
patch. StandingServiceAudit adds70 checks; all51 Python classifier/orchestration tests
pass. The original six-scene evidence above remains tied to its original revision.
New result status is `all_scenes_passed`, not the historical `all_six_passed`.

Peer review adds four actual-stub distance/phase witnesses, raising the current standing
minimum to74. The restored selected-scene gate passes and a missing-row-mirror mutation
fails all four new witnesses; the previous all-seven run remains at70 standing checks.

After integrating peer0618434, the full seven-scene gate also passes with74 standing checks;
52 Python classifier/orchestration tests pass. The separate park matrix additionally requires
six interrupted-departure checks per world and retains its exact known retail failures.
