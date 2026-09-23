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
