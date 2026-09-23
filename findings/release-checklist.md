# Current validation and release checklist

Updated 2026-09-23. Evidence baseline through `81d112c`; later changes require reruns.
This is an engineering checklist, **not a release or retail-parity declaration**.
The authoritative running handoff is `progress.md`; implementation scope is `plan.md`.

## Verified in this development environment

| Gate | Actual evidence | Limit |
|---|---|---|
| Fresh-checkout builds | All 22 tracked projects built in Release at pinned `34b050b`; resulting tree Git-clean | Linux aarch64 with existing NuGet cache; not a clean-machine install; later `07893c0` needs changes were not in that sweep |
| Integrated park audits | At `6455861`, 30 availability + 57 removal + 20 conservation + 24 needs assertions per world; JUNGLE/FANTASY full pass | HALLOW/SPACE retain exact findings below; no implication that every system is complete |
| Matrix classifier | 20 Python tests, including missing/partial coverage and misleading known-failure names | Classifies the recorded audit contract, not arbitrary runtime correctness |
| Launcher failure paths | 40 offline assertions through `81d112c`, including real harmless process fixtures | UI dispatch source-reviewed/compiled; Windows real update/relaunch not exercised |
| Needs continuity | Guest-ID state preserved across queue, actual seated ride, completion, deletion, delayed readmission, retirement and ID reuse | Normal frame-rate/zero-time checks, not all long-stall timing; rise rates/cadence remain chosen |
| DBA unknown storage | 172,992 one-bit probes across EUR/USA/JAP, original golden checks unchanged | Preservation evidence only; specialised semantics remain partial |
| Earlier rendered guest smoke | Actual Godot/Xvfb run at `4090631`; runtime boarding/seating counters and four generated captures | Captures were pixel-checked, not visually signed off; predate current renderer/needs bubbles |

Recorded logs/manifests are local build artifacts outside Git. Reproduction commands and
revision-specific outcomes are recorded in `progress.md`, `ride-removal.md` and
`launcher-recovery.md`. Do not substitute a newer untested commit name for these baselines.

## Expected retail findings — keep them visible

* **HALLOW / Thrill Grill:** its script waits for an animation slot missing from the
  relevant disc animation. Do not invent that slot or waive additional failures.
* **SPACE / Moon Buggies:** missing park-space fittings required by the walking path.
  Do not fabricate fittings or label unrelated SPACE failures expected.

The ordinary C# audits exit 1 for those findings; the matrix distinguishes exact known
findings with exit 2. Extra failures, crashes, missing coverage, and unexpected disappearance
of a known finding all require investigation. “Expected” is not “passed.”

## Still required before declaring the chosen release target ready

- [ ] Fresh-checkout build and dependencies on the actual Windows target/toolchain.
- [ ] Real launcher installation with a user-owned disc path containing spaces/brackets.
- [ ] Actual update success, failed build with emitted output, interrupted download/clone,
      offline startup, missing Git/.NET/engine, Retry clicks, and failed process start.
- [ ] Windows file-lock/self-replacement/relaunch checks. Receipt schema 2 should refuse
      altered/missing dependencies and require a rebuild for a legacy/missing receipt.
- [ ] Real rendered current-revision guest lifecycle, recovery and needs-bubble inspection;
      a generated screenshot or a nonblank pixel census is not visual sign-off.
- [ ] Representative world changes, repeated load/unload, long runs, and resource checks.
- [ ] Audio heard on a real output device; Dummy audio instrumentation does not establish
      playback quality or timing correctness.
- [ ] Documented controls, cancellation/focus/scaling, informative errors, locale checks,
      and a precise supported-feature list.
- [ ] Dependency/license review and confirmation that distribution contains no disc,
      extracted proprietary assets, credentials or proprietary source.

The launcher currently searches Windows engine locations and a matching mono engine is
required by its design. A Linux build of the launcher is compilation evidence, not a claim
that its Windows discovery/self-update workflow works on Linux.

## Scope that must not be disguised as finished

Track/tour services, management/economy/saving, advisor state producers, format evidence debt,
and rendered/live integration have independent exit gates in `plan.md`. A reader that accepts
a file, a byte-preservation test, an inferred transform, and a decoded executable consumer are
not interchangeable evidence. Keep READ/CANDIDATE/UNKNOWN labels where applicable.

The launcher receipt is local successful-build provenance: revision plus a managed-output
manifest. It does not attest external engine/plugins, native non-DLL dependencies, arbitrary
source-tree edits, concurrent installers, publisher authenticity or retail correctness.

## Minimal reproduction

```sh
# POSIX shell examples. DISC names the owner's existing image; read it in place.
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
dotnet build game/TPWPS2Viewer.csproj -c Release
dotnet build launcher/TPWPS2Launcher.csproj
dotnet run --project tools/TPW.PS2.LauncherAudit -c Release
python3 -m unittest discover -s tools -p test_audit_matrix.py
python3 tools/audit_matrix.py --disc "$DISC" --out "$NEW_EVIDENCE_DIR"
```

Do not use `--no-build` unless intentionally reusing an artifact and retaining that fact in
the manifest. Isolated `--removal-only` tests use synthetic corridors with real scripts;
they do not replace retail entrance integration or the ordinary whole-park matrix.
