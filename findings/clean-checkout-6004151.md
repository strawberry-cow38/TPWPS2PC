# Clean-checkout release-engineering gate at 6004151

Measured September 23 US Eastern / September 24 UTC 2026. Pinned revision:
`600415132d3f353865a8a2db55fc5a2694582250`.
This is source/build/runtime evidence on Linux, **not a release declaration**.

## Isolation and toolchain

Created a new detached Git worktree from the published commit, not a copy of the
working directory. Before the first command it was Git-clean and had no `bin`, `obj`
or `.godot` directories under game/core/tools/launcher. No existing project output,
Godot import/build cache or extracted disc asset was copied into it.

The existing host toolchain and NuGet package cache were reused: .NET SDK **9.0.119**,
net8.0 project targets, Godot **4.6.stable.mono.official.89cea1439**, project Godot SDK
4.6.2, Linux aarch64. This is not a cold-machine install, an empty-package-cache restore,
an offline-install guarantee or native Windows qualification. The owner's existing
BIN was read in place throughout.

## Results

| Gate | Actual result |
|---|---|
| Tracked project enumeration/build | All 23 tracked csproj projects build in Release, each exits 0 with Build succeeded |
| Fresh Debug runtime gate | All six scenes pass through the committed runtime runner; full_gate_passed=true |
| Visitor/RSE runtime coverage | Six world/terrain cases each: FANTASY/SPACE/HALLOW × terrains 1/2 |
| Texture/MTR runtime coverage | 146 texture-surface checks plus viewer clock; four named MTR witnesses |
| Browser/audio runtime coverage | 45 advisor-browser and 37 audio-lifecycle checks |
| Python runner/classifier tests | 47 pass (20 matrix + 27 runtime) |
| Four-world park matrix | JUNGLE/FANTASY pass; HALLOW/SPACE only the exact documented retail findings |
| Per-world lifecycle assertions | 30 availability, 57 removal, 20 conservation, 45 needs |
| Launcher audit | 76 checks pass including the actual selected disc/engine probes |
| Launcher window-event audit | 22 headless actual-window checks pass with fake external operations |
| Advisor data audit | 73 text structural checks plus full 106-rule/275-message regional identity/negative audit pass |
| Final source status | Git-clean; generated outputs remain ignored |

The runtime runner performed its own fresh Debug Rebuild first. The subsequent Release
sweep built each of the 23 projects once; project references could reuse outputs made
earlier in that sweep. This is not 23 fully independent empty-cache installations.
Compilation does not imply that every tool was executed; executed gates are listed above.
Build warnings were not reclassified as absent.

HALLOW/Thrill Grill still has the known missing animation slot; SPACE/Moon Buggies still
has the known missing park-space fittings. Their raw exits remain 1 and the matrix exit
remains 2. These are **known failures, not green worlds**. No allowances were broadened.

## Reproduction and retained evidence

Start from a separate clean worktree at the pinned commit; do not copy assets or
existing build directories. Use a new evidence directory outside Git ancestry:

```sh
python3 tools/runtime_audit.py --disc "$DISC" --godot "$GODOT_MONO" --out "$RUNTIME_OUT"
# Enumerate tracked csproj paths and build each with:
dotnet build "$PROJECT" -c Release --nologo
python3 -m unittest discover -s tools -p 'test_*audit*.py'
python3 tools/audit_matrix.py --disc "$DISC" --out "$PARK_OUT"
dotnet run --project tools/TPW.PS2.LauncherAudit -c Release -- --disc "$DISC" --engine "$GODOT_MONO"
dotnet run --project tools/TPW.PS2.LauncherUiAudit -c Release
dotnet run --project tools/TPW.PS2.AdvisorAudit -c Release -- "$DISC"
```

Actual launcher/advisor runs used their just-built Release DLLs directly, avoiding a
second implicit build; the run commands above reproduce equivalent project entry points.
Local task evidence: `tpw-clean-6004151-baseline.json`, `tpw-clean-6004151-result.json`,
`tpw-clean-build-6004151/manifest.json`, `tpw-clean-runtime-evidence-path.txt`,
`tpw-clean-park-evidence-path.txt`, and the corresponding clean-audit logs. The runtime
manifest hashes source and Debug outputs; the Release build manifest records each
project/exit/log. It is not a signed release artifact manifest.

The detached worktree and evidence are retained. No cleanup/deletion or service restart
was performed. No packaging/build defect was found in this sweep, so there is no invented
production fix attached to it. Manual visuals, native Windows update/relaunch, audible
output, unsupported gameplay and format-semantic gaps remain in the release checklist.
