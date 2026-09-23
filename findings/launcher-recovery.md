# Launcher recovery and successful-build evidence

2026-09-23. This work changes launcher error paths, not game/disc semantics.

## Findings and changes

Previously the launcher remembered readiness only through an existing viewer DLL
and matching checkout heads. A build can emit that DLL and fail a later target;
refresh/restart then offered Play despite the failed build. A failed DLL deletion
after updating the checkout had a similar stale-artifact path. The immediate build
call did check its exit code, but later state refresh forgot the result.

`ViewerBuildReceipt` now stores successful-build evidence beside the launcher,
outside its managed checkout: schema, full Git revision, and assembly SHA-256.
The receipt is invalidated **before** checkout/build mutation. Failure to invalidate
aborts the operation. Only a zero build exit, an existing artifact and unchanged
before/after HEAD permit recording success. Refresh and the final launch gate both
require matching revision and artifact hash. Missing, corrupt or oversized receipts
fail closed. Existing installations without a receipt require a successful rebuild
once. This is local build provenance, not publisher authentication, source-tree
attestation, a signature, or a transactional multi-process installer.

The window also remembers the failed action and explicitly dispatches Retry to it.
It no longer silently falls through to state refresh. Start errors retain their
message and Retry action rather than being overwritten by Ready. Busy dispatch is
set synchronously, and the disc picker is disabled while an operation is underway.
A failed `git clean` now aborts instead of being ignored.

Two malformed self-update inputs were reproduced with executable-free synthetic
fixtures: short hash strings threw while formatting their mismatch reason, and a
custom zero plausibility floor allowed zero-/one-byte arrays to index outside their
bounds. Hash validation now requires 64 hexadecimal characters if supplied; shape
checks always require the two signature bytes. The pre-existing explicit shape-only
policy when no hash is available remains unchanged. This patch does not redesign
release authenticity, the Windows update shim, clone recovery, process timeouts,
or engine-version discovery.

## Validation

```sh
dotnet run --project tools/TPW.PS2.LauncherAudit -c Release
dotnet build launcher/TPWPS2Launcher.csproj
```

No disc, network, real update, or actual Git reset is required by LauncherAudit.
Its temporary files are synthetic, unique per run, and removed afterward.

* Before the hash/bounds fix: four failed checks (two malformed-hash exceptions,
  two short-array exceptions).
* Afterward: 26 passing assertions, including matching/mismatching/malformed hashes,
  version rejection, missing/corrupt/oversized receipts, revision/hash mismatches,
  failed builds that leave output, restart persistence, and successful retry.
* Launcher build succeeds on the Linux aarch64 development box (12 existing core
  nullable warnings, zero errors in the recorded build).
* Two narrow review passes checked the failure paths and resulting integration.
  The receipt audit models failed deletion by retaining an old DLL after invalidation;
  it does not induce actual Windows file locking.

UI Retry dispatch, error persistence, and invalidation ordering were source-reviewed
and compiled, not exercised by an automated UI driver. Windows self-replacement,
locked-file behavior, and actual install/update/relaunch still need target-platform
manual validation. No Windows compatibility/release sign-off is claimed.

## Bounded command execution follow-up

Launcher commands now share `LauncherProcess`: argument-list execution without a
shell, concurrent draining of stdout/stderr, and at most 65,536 retained characters
per stream (diagnostic tails). Build execution has a ten-minute deadline, other
install Git commands three minutes, and repository-status queries thirty seconds.
These are launcher safety limits, not project delivery estimates. Timeout is failure
even if the associated process happened to exit zero while its pipes stayed open;
partial/truncated repository responses are not accepted as revision evidence.

Timeout attempts tree termination, waits at most two additional seconds for the
associated process, and closes redirected readers. Cleanup is best-effort, not an OS
job-object/process-supervisor guarantee: descendants of an already-exited parent
may outlive it. A cleanup failure remains visible and cannot become a successful
command. Windows `.cmd` wrappers are not selected as directly executable Git binaries.

The audit now has 32 passing assertions, including real synthetic subprocesses for
spaced arguments, exit 17, large dual-stream output, timeout with retained diagnostics,
missing executable, and an injected exceptional termination result. That injection
kills its real test process first, then reports an aggregate cleanup failure, proving
the remaining cleanup/result path without deliberately leaking a child. Two review
findings (exceptional tree cleanup and `.cmd` selection) were addressed before landing.
Launcher compiles; no actual Git update or self-update was executed by these tests.

## Peer-review correction: dependency manifest, receipt schema 2

Cow tools identified that a single viewer-DLL hash does not cover changed sibling
runtime dependencies. The existing build-exit gate already refused a nonzero build;
the reproduced gap is that changing/removing/adding a dependency after a successful
receipt left that receipt accepted. Five new mutation controls failed before the
correction (changed/missing/added dependency, changed dependency-resolution metadata,
and changed nested resource assembly).

Receipt schema 2 hashes a sorted, relative-path manifest of the output directory's
DLLs, `.deps.json` and `.runtimeconfig.json` files recursively. Each entry includes
its own SHA-256. This covers the managed output and Windows DLLs located there;
added/removed files change the manifest too. Debug symbols do not affect readiness.
The compact receipt stores the manifest digest rather than an unbounded file list.
Schema-1 receipts require rebuilding, rather than silently retaining the weaker gate.

LauncherAudit now passes 40 assertions, including all five mutations, unchanged
manifest restart, ignored debug-symbol changes, and schema migration. Launcher
compiles. This still does not attest external engine installations/plugins, native
non-DLL dependencies, arbitrary source working-tree modifications, or concurrent
installers. The manifest verifies current local output against the successful-build
record; it is not proof of retail correctness or cryptographic publisher identity.

## Disc-recognition predicate follow-up

The launcher described requiring `DATA/JUNGLE.WAD`, but its code accepted any entry
whose path merely ended with `JUNGLE.WAD`, including directories. Four synthetic
reader-layout fixtures reproduced false acceptance: `NOTJUNGLE.WAD`, the same name
under another folder, the same name at disc root, and a directory with the exact name.
The predicate now requires the exact case-insensitive `/DATA/JUNGLE.WAD` path and a
non-directory entry. No core disc parser or game data changed.

The new fixtures contain only constructed directory records, not copied game payload.
They test the predicate, not full ISO conformance or archive integrity. Missing paths,
empty folders, truncated input and the two positive path/case controls are also checked.
LauncherAudit passes 50 offline assertions, or 51 with the optional owner's-disc check:

```sh
dotnet run --project tools/TPW.PS2.LauncherAudit -c Release -- --disc "$DISC"
```

The real disc is read in place and still recognized. Launcher builds. Broader structural
validation, alternate sector layouts and target-platform UI testing are not established
by tightening this filename predicate.

## Actual window event-path audit (headless, not Windows release sign-off)

`tools/TPW.PS2.LauncherUiAudit` now constructs the actual MainWindow in Avalonia's
headless dispatcher and raises its routed button events. Production uses the same
real command/process defaults as before; an internal constructor permits a fixture
root and fake external-effect services. Startup and disc discovery explicitly refuse
to run in that isolated host, including accidental Retry-to-startup dispatch.

The 22 assertions cover failed builds that emit a DLL, receipt rejection on refresh,
Retry rebuilding/launching only after success, throwing/null process starts, preserved
error messages, recovery-window visibility, close after successful handoff, changed
artifacts, disabled busy controls, duplicate-click suppression, and asynchronous
failure recovery. The fake start boundary also checks exact project arguments, a disc
path with spaces/brackets passed through the environment, and shell-free execution.
The actual action task is awaited; fixture disposal releases pending work and waits
before deleting its own files.

Four deliberate temporary source mutations were rejected by the audit: broken Retry
dispatch (7 failed assertions), error overwrite (3), closing the window on failure (5),
and bypassing the final receipt gate (3). Original source was restored and the green
audit rerun. Two source reviews checked fixture isolation and assertion strength;
the first review prompted environment guards, window-visibility checks, launch-contract
checks and awaited disposal before the final review found no concrete blocker.

```sh
dotnet run --project tools/TPW.PS2.LauncherUiAudit -c Release
```

No actual Git command, viewer process, HTTP update, disc lookup, or self-replacement
runs in this audit. Synthetic receipt/artifact files are confined to a unique temporary
folder. This improves the earlier source-only UI coverage, but does not test native
mouse input, visual layout, accessibility, Windows file locks, or a real installer/
self-update handoff. Those target-platform/manual gates remain open.

## Engine discovery: advertised .NET capability, not filename trust

The old selector accepted any Godot-named executable without running a capability
probe. It also searched the entire path for “console,” so that word in a parent
folder changed variant selection. A snapshot of the original selector was run beside
the replacement against synthetic non-executable fixture files: the old code accepted
a standard-build candidate and misclassified the directory-name case; the replacement
rejected the former and selected the latter correctly. No fixture executable was run.

Discovery now invokes the selected candidate's `--version` through bounded command
execution, accepts reported Godot 4 .NET/mono capability, records the response, and
skips failed/standard/malformed candidates. Console classification uses the executable
basename. It can prefer a validated requested variant in a later probe directory and
honestly report a validated fallback. Paths are deduplicated; at most 16 executable
probes are attempted. Exhaustion is logged as incomplete discovery, not an exhaustive
negative result. Each probe has a five-second command deadline plus possible cleanup;
there is deliberately no claim that the entire discovery finishes in five seconds.

Primary engine evidence: the official C# documentation distinguishes the .NET-enabled
engine from the standard build, and the engine's mono module adds `mono` to its version
string. Source pointers used for this check:

```text
https://docs.godotengine.org/en/4.6/tutorials/scripting/c_sharp/c_sharp_basics.html
https://raw.githubusercontent.com/godotengine/godot/4.6-stable/modules/mono/config.py
```

The project SDK target remains 4.6.2. This patch does **not** introduce an exact patch-
version rejection rule: advertised Godot 4/.NET capability is not proof that a given
engine version can load every project API. The UI logs the reported version and that
limitation. Full engine/project compatibility and Windows console-wrapper behavior
still need real launch validation. Default discovery locations remain Windows-specific.

LauncherAudit now has 74 offline assertions, including 24 discovery checks. With the
optional owner's disc and explicitly selected installed engine, 76 assertions pass;
the local engine reports `4.6.stable.mono.official.89cea1439`. This last observation is
only an actual CLI capability check, not current-renderer compatibility sign-off.
The 22 headless window-event assertions still pass.

```sh
dotnet run --project tools/TPW.PS2.LauncherAudit -c Release -- \
  --disc "$DISC" --engine "$EXPLICIT_ENGINE_PATH"
```

`--engine` executes only the explicitly supplied executable with `--version`; the
ordinary offline audit never executes its synthetic engine fixtures. A source review
prompted explicit probe-limit reporting and tighter malformed-prefix checks before
landing. No engine download, runtime-version upgrade or target change is part of this
package.
