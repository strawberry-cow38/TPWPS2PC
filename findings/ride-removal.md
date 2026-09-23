# Managed guest ownership on ride removal

2026-09-23. This is **port lifecycle policy**, not decoded console evacuation behavior.
`ParkSim.Remove`/`Clear` remove ride instances; `ParkVisitors.Step` must reconcile the
identities formerly owned by those instances. No production viewer deletion caller
was established by this work, and no viewer/UI deletion feature is claimed.

## Failure and boundary

Before the fix, queued/riding guests were absent from `GuestWalk` and retained a
Queued plan after their ride vanished. Collection visited only live rides, so those
identities could never return. The regression suite produced 12 failing assertions
in JUNGLE before the core change. Removing an instance and adding another with the
same numeric ID must not transfer its people to the replacement.

The coordinator now retains actual ride-instance ownership. On its next step:

* Heading guests lose their ride intent but keep the existing walking object and
  physical route/position. This is not an immediate route-cancellation/teleport rule.
* Queued, offered, and seated guests become recoverable identities. Queue/offer
  states prefer the old entrance; other states prefer the old exit. The other
  endpoint and last recorded handoff cell are alternatives. Valid endpoints require
  grid membership and walkability.
* If endpoints are gone, recovery picks the nearest public open cell by Manhattan
  distance, then Z/X for deterministic ties. If none exists, an explicit Recovering
  plan retains the identity and retries on subsequent steps.
* Readmission happens once with the same ID. An already-walking identity is not
  duplicated. Removed ride objects held by external callers may still contain their
  old seat data, but no live simulation ride owns the recovered guest.
* Evacuation does not invent completed rides or boardings. A completion already
  reported in `Left` or `VAR_LETMEOFF` survives removal; it is counted only after
  successful readmission, once. Ordinary completion uses the same safe recovery
  path, including delayed recovery when ground is unavailable.
* Collection ignores unowned/stale/repeated handbacks instead of inventing people.
  `SendTo` rejects removed instances and stale walking-object references.

One coordinator owns this walking/simulation pair. Whole-park teardown should
replace/discard the coordinator together with its walk; `Sim.Clear` alone preserves
its visitor identities. This does not add guest needs, faithful AI, track services,
queue rendering, or retail evacuation animation. Recovery coordinates are policy,
not executable-consumer findings.

## Evidence and reproduction

With the owner's original disc read in place:

```sh
dotnet build tools/TPW.PS2.ParkSimAudit -c Release
for world in JUNGLE FANTASY HALLOW SPACE; do
  dotnet tools/TPW.PS2.ParkSimAudit/bin/Release/net8.0/TPW.PS2.ParkSimAudit.dll "$DISC" "$world" --removal-only
done
python3 tools/audit_matrix.py --disc "$DISC" --out "$FRESH_EVIDENCE_DIR"
```

`--removal-only` is explicitly isolated: real terrain, model, animation, and ride
scripts on a synthetic three-cell corridor, **not retail entrance integration**.
The same 57 removal assertions run within the ordinary integrated ParkSimAudit.
Fixtures own separate sim/path/script instances and do not mutate later audit data.
Cases include queue, offer mailbox, real seated rider, both script-completion
boundaries, normal/delayed completion, heading mid-edge, numeric-ID reuse,
missing endpoints, no-ground/restoration, repeated steps/removal, and Clear.

Integrated over upstream `19e2b0f`: 57 removal assertions per world pass in both
modes (228 per mode); all 120 availability checks pass. Whole-park JUNGLE/FANTASY
pass. HALLOW Thrill Grill and SPACE Moon Buggies retain their original single
known retail failures (raw exit 1, matrix exit 2); neither is waived or repaired.
GuestAudit passes all four worlds. Viewer Debug build passes with 15 warnings and
zero errors; this is compile compatibility, not rendered removal verification.
The matrix runner's 15 Python tests pass.

An upstream entrance regression at `f2ef76e` initially crashed HALLOW/SPACE before
this patch was applied. It was reported to the entrance maintainer and fixed upstream
in `19e2b0f`, then integrated and the full matrix rerun. The isolated mode was added
while that integration was blocked, not as a replacement for failing retail checks.
Source review also caught a lost-completion boundary and weak identity fixtures;
those were corrected and rerun before landing. Final scoped review found no concrete
remaining issue, which is not proof of absence of bugs.
