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

## Crowd conservation and fixed-input replay

Audit-only `GuestConservationChecks.cs` extends each removal run with eight guests
split across two real script instances. Both must actually seat guests; one is then
removed while the other retains its riders and subsequently reports completion.
Final Clear must recover all eight original identities. At every sampled step the
census checks the expected identity set, walking-body multiplicity, live ride intent,
and incompatible cross-layer/cross-ride ownership.

Queue/offer/seated/exit representations are unioned within each ride because a
script handover can transiently expose more than one of those. They are never
merged across different rides. Queued plans can bridge script transitions without
a visible seat/mailbox/queue entry; this is not a proof that every possible live
script will eventually hand every guest back. The fixture additionally requires
actual seated bodies and a real completion so a perpetually idle script cannot pass.

Five deliberately corrupted fixture controls must be rejected: duplicate walker,
missing walker, unregistered identity, simultaneous walking/ride ownership, and
ownership by two rides. Two independent identical-input runs then hash all sampled
plans, walking states, public ride variables, queues, exits, and seat identities.
Their traces must match. This establishes deterministic replay of that fixed-input
sequence, **not different-frame-rate equivalence or a full VM-state snapshot**.

On `9da7a2a` plus the audit extension, all 20 additional assertions pass per world
in isolated and integrated modes. Each replay samples 112 steps in JUNGLE, 91 in
FANTASY, 133 in HALLOW, and 170 in SPACE; each mode executes two independent runs
per world. Integrated and isolated hashes differ because their corridor coordinates
differ; only same-fixture replay pairs are compared. The full matrix still reports
only the two unchanged retail findings, runner exit 2. No production core change
was needed for this additional coverage.
