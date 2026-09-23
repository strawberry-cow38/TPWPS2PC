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

## Needs continuity: independent integration checks

`NeedsLifecycleChecks.cs` exercises the peer's guest-ID side table through boarding,
actual scripted seating, removal, normal completion, no-ground recovery/restoration,
retirement, and reused IDs. Frozen chosen rates isolate preservation of all stored
fields from need arithmetic. Fresh arrivals overwrite stale entries even before a
later reconciliation step; recreated walking objects must not seed a second person.

The first run against `5395ae3` passed 21 identity/storage assertions but exposed two
clock defects in every world: a zero-time coordinator call aged needs, and equal
elapsed time at 25 Hz versus 50 Hz produced different hunger. This was shared with
the peer maintainer, who corrected the core in `07893c0`; no competing core edit was
made here.

The final independent suite has 24 assertions per world. Its rate-control case uses
a chosen .25-second period and deterministic +1 hunger, then checks 1.2 elapsed
seconds at both frame rates yields exactly four rises. That total avoids floating-
point boundary ambiguity and explicitly rejects a no-op updater; rates/cadence are
test controls, not retail measurements. All four worlds pass in both isolated and
integrated modes. Matrix manifests now require and report this coverage as well.
Only the two pre-existing retail failures remain in full ParkSimAudit.

These tests do not establish every rate/decision rule, long-stall catch-up behavior,
thought-bubble visibility, or retail AI parity. Rendering remains the peer's active
integration scope and requires its own evidence.

## Catch-up ceiling: second independent clock correction

A later controlled probe exposed a different defect from call-count aging: the
coordinator dropped most of a stalled frame for walking/park simulation, but passed
its full delta to needs. One Step(10) and eight Step(.04) both advanced park/walk
clocks to 320ms, yet hunger reached 50 versus 11 from 10 with controlled .25-second
+1 rises. The needs maintainer confirmed this was unintended and corrected it in
`69faca2` to use consumed walking ticks rather than raw frame delta.

Three permanent checks now cover the real eight-tick ceiling, identical needs for
equal consumed simulation time, and continued aging during an ordinary longer run.
The suite is now 27 needs assertions per world. All pass in the integrated four-world
matrix; the two original retail findings remain unchanged. The probe also reproduces
the corrected 11/11 results. This is managed clock consistency, not retail timing parity.

The maintainer also corrected the evidence label: the 26-byte record discussed in
VisitorNeeds is a **per-level spawn template**, not decoded temporal rise-rate data.
Need growth, its selected rates, and cadence remain port policy. This suite audits
storage/identity and clock consistency; it does not independently establish retail
need-growth rules. See the corrected source and `findings/visitors.md`.

## Completion effects: continuity does not mean immutability

The needs maintainer proposed wiring an existing Ride effect into completed rides.
The old completed-return check required byte-for-byte unchanged needs; that correctly
caught reseeding before any effects existed, but became an invalid invariant once a
specified ride effect was intentional. It was replaced, not merely disabled.

The jointly agreed contract applies the configured effect exactly once alongside
Rides++ in successful RecoverGuests readmission. That includes genuine handbacks
reported before deletion and excludes ordinary aborted removal. A completed guest
with no available ground retains the pending effect until readmission succeeds.
Queued is not a waiting-only state: it includes script-owned seated riders, so no
queue penalty was added to that intent.

The audit pins chosen inputs (intensity 40, happiness +7, sickness/boredom scales .5)
and non-clamping sentinels. One effect must take Happiness 83→90, Sick 71→76 and
Unknown78 62→42. Cash remains 1234, outside spawn cash values; Hunger, Thirst, Toilet,
Litter and Thought are unchanged. Unknown7B must drop by 0..19 with clamping. This
bounds the stochastic term but does not certify its distribution or RNG-call count.
Repeat-step equality detects a repeated whole effect or rerolled stored value. The
checks also require exactly one readmitted walking identity and a Wandering plan.

The suite now has 45 needs assertions per world: normal completion/repeat, both
reported-deletion boundaries, deferred completion/no-ground recovery, unchanged
queued/seated aborts and the prior identity/clock controls. All four worlds pass in
isolated and integrated modes; the exact retail findings remain visible.

Five deliberate source variants were rejected in JUNGLE: no effect, double effect,
reseed before effect, aborted removals treated as completed, and Collect-only effect
placement (5/5/5/5/3 failed assertions respectively). Original source was restored.
A review prompted explicit body/plan checks after successful completion. Peer branch
`needs-ride-effects` supplied the consumer wiring; it was integrated locally with
these tests before any green main-branch publication.

All four effect inputs remain chosen policy pending decoding of the ride's own value
and relevant scale globals. This verifies the agreed managed contract, not the retail
values or a complete guest-management model.
