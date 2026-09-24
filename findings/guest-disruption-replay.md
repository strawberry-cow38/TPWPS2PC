# Repeated guest disruption and replay gate

Measured September 23 US Eastern / September 24 UTC 2026 on the existing lifecycle
implementation, with audit-only additions. No production guest/needs/viewer changes
were needed. This extends the one-removal/two-ride census, rather than treating that
short scenario as long-run coverage.

## Fixture and invariants

`GuestDisruptionChecks` keeps sixteen guest identities and their needs across twelve
cycles of closure, break/repair, occupied ride removal, immediate numeric-ID reuse,
missing ground, Clear, restoration and redispatch. It uses **three independent instances
of one selected real ride script**, not three distinct ride types. The whole-park matrix
supplies a suitable real script/terrain fixture in each of JUNGLE/FANTASY/HALLOW/SPACE.

Every normal replay includes thousands of samples, variable frame deltas including
zero/sub-tick/capped-stall calls, actual seating and actual completion. Needs growth
rates are frozen to isolate identity persistence; the chosen rates are not asserted
as console values. Cash uses a per-ID sentinel outside spawn values and must remain
unchanged by ride-only lifecycle operations.

The shared audit-only ownership census checks plans, walking bodies, live ride bodies
(queue/offer/seat/handback union per ride), recovering identities, and cross-ride ownership.
The extended census also checks the exact needs key set, cash continuity, nonnegative
completion bounded by boarding, monotonic cumulative counters, and absence of script faults.

Phase-local requirements close holes that aggregate counts would miss:

* Each removal target actually has seated guests and queued/ride-owned plans.
* After a replacement with the same numeric ID is created **before** reconciliation,
  the removed instance's affected guests must enter Recovering, not belong to the new
  instance. Its live bodies must not inherit them.
* The fixture proves there is no walkable ground. Every affected identity must remain
  recovering throughout that phase's blackout, not merely somewhere in the overall run.
* Actual Left/VAR_LETMEOFF handbacks are collected before/during the blackout and before
  Clear. No readmission/completion can occur with no ground; restoration must count
  exactly those reported completions, never aborted rides or new boardings.
* Every restoration returns all sixteen original identities exactly once, with no ride
  intent. Every redispatch and restored route assignment must succeed at its valid boundary.

Grid erasure/restoration is explicitly synthetic: every material byte is zeroed and
later restored, with no-ground checks. This is not a test of the viewer's path tool,
a decoded console evacuation algorithm, automatic route recovery, or every map/ride type.
Walkers are explicitly resent after the fixture restores its grid.

## Replay and meaningful negative controls

Two identical schedules must produce equal reports and SHA256 of the sampled walk,
plan, ride-body/variables and needs state. A changed first delta must change the digest.
The hash contains observed state only—not the input seed or operation labels. It does
include clock values and sample order, so changed-delta sensitivity is deliberately
not claimed as an independent proof that every discrete lifecycle outcome changed.
This is same-process/version replay evidence, not serialized saves or cross-runtime RNG parity.

Direct negative controls reject changed cash and an orphan needs record. Two additional
runs inject those defects only after seven repeated cycles and more than 1000 samples.
They use the ordinary Step/Sample path and must fail **at the injection sample**, so an
earlier unrelated failure cannot satisfy the control. This proves late sampling remains
active rather than only validating initialization.

Read-only subagent review prompted the phase-local ownership, occupancy, recovery and
handback accounting requirements. It also caught the distinction between numeric and
instance ownership in the broader census and the weakness of clock-inclusive hash sensitivity.

## A fixture error, not a production bug

The short isolated corridor initially passed, but all four full-world runs reported
redispatch failure. The fixture waited a fixed 80 ticks after sending walkers back,
which can leave them mid-edge in the longer corridor. GuestWalk.Send intentionally
returns false while Next is non-null. The fixture now waits for actual Arrived/no-edge
state, with a bounded failure if arrival never happens. The maintainer confirmed this
existing API contract; no production refusal was loosened to green the test.

## Measured full-world results

Each world passes all 21 new disruption checks alongside 30 availability, 57 removal,
20 earlier conservation and 45 needs checks. Representative unaltered replay:

| World | State samples | Boarding/completion totals | Recovery samples |
|---|---:|---:|---:|
| JUNGLE | 4,968 | 192 / 25 | 408 |
| FANTASY | 5,848 | 208 / 16 | 408 |
| HALLOW | 5,468 | 192 / 6 | 408 |
| SPACE | 5,072 | 192 / 13 | 408 |

The two identical runs match; the changed-input replay differs. Final state has all
sixteen identities walking once and no ride intent. The ordinary world audit remains
JUNGLE/FANTASY pass, HALLOW Thrill Grill and SPACE Moon Buggies exact known failures
(raw 1, matrix 2). Expected failures were neither waived nor turned green.

Four deliberate production mutations in the isolated worktree all fail the new suite:

| Mutant | First new-suite diagnosis |
|---|---|
| Reconcile ownership by numeric ID instead of ride instance | Replaced ride's guests fail to enter recovery |
| Count every evacuation as a completed ride | Restoration differs from real script handbacks |
| Reset the boarding counter after 40 boardings | Cumulative counters go backwards |
| Reseed needs on readmission only after 40 boardings | Per-ID cash sentinel changes |

Each yields three failing normal-replay checks plus two late controls that refuse an
already-corrupt prefix. These are four intentionally broken variants, not five separate
production bugs per variant. Original ParkVisitors source was restored and rebuilt;
no mutants remain, and the final four-world matrix has only the two known retail reds.

The matrix now requires 21 disruption checks plus the replay and both late-control
witnesses. Two new classifier controls prevent omission of this gate; combined Python
runner tests pass 49 (22 matrix + 27 runtime). A stale binary without the helper cannot pass.

## Reproduction and evidence

```sh
python3 tools/audit_matrix.py --disc "$DISC" --out "$NEW_OUT"
# Isolated synthetic corridor, not the full retail entrance gate:
dotnet run --project tools/TPW.PS2.ParkSimAudit -c Release -- "$DISC" JUNGLE --removal-only
python3 -m unittest discover -s tools -p 'test_*audit*.py'
```

Local task evidence: `tpw-disruption-matrix-path.txt` points at final full-world logs/
manifest; `tpw-disruption-mutations/manifest.json` records broken controls and restoration;
`tpw-disruption-python-final.log` records classifier tests. The isolated mutation fixture
has a shorter route and therefore different sample counts from the full-world table.
No disc data was copied/extracted, no renderer files modified, and no service restarted.
