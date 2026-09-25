# Incoming controller: actual consumer, opt-in research only

September 24, 2026. **NOT main/release-ready.** `--experimental-native-entrance`
opts the research Viewer into automatic bus birth -> staging -> incoming groups
-> acceptance/charge -> mode13 -> ordinary visitor handoff. Without that flag,
the existing main behavior and its explicit backlog/departure bypasses remain.
This supersedes the earlier route-actuator-only checkpoint, not its scope limits.

## Causal chain now connected

`AdmitBusBatch` creates the actual visitor through `Arrive`, then immediately
`NativeEntranceFlow.Add` takes its route owner before ordinary AI can select a
destination. No test-created shadow population supplies bus counts. Baseline
speed is15+RNG(15), from ordinary activation, while the existing port RNG streams
and activation draw order are NOT claimed console-identical.

`GuestWalk.BeforeStep` runs once per EXECUTED walk tick, including catch-up ticks.
The flow pumps previously submitted route results, visits group0 then1, runs the
staging coordinator and visits its active list. Each explicit native lease is
manually stepped in its appropriate pass; the generic walker skips it entirely.
The rest of the port's global update order is not thereby claimed native.

Native state transitions and exact quarter-cell cursor are documented in
`native-entrance-lifecycle.md` and `native-route-consumer.md`. In particular:
- Registration precedes request submission and survives refusal/failure.
- Async submission is not path completion. Tokens and exact Guest references
  reject duplicate/stale results after removal, clear or reused numeric IDs.
- Both groups have the same base x; predecessor spacing is64/256cell in z.
- Only a ready HEAD releases on `(tick&31)==group`. Remaining members receive
  event6 and update individually. A released head enters the later active pass.
- P increases at mode15 completion; mode16 completion alone decrementsP/increasesR.
  An R<11 broadcast is not an eleven-recipient quota.
- Acceptance happens once after release; missing exit goals/slot failure retry
  state2D without another fee. Completed mode13 hands the SAME guest and needs
  row to ordinary selection at a cell centre, without teleporting or respawning.

The bus's entrance bound reads the two actual list lengths in this opt-in mode.
Its sticky departure input now includes the owned rejected cohort with native
before-dispatch timing. Normal rejected guests follow the outgoing journey and
retire at point0; exact startup phase and ordinary failure recovery remain bounded
adapters. See native-rejected-departure.md, not the older hold-only checkpoint.

## Explicit service boundaries; do not call this native pathfinder parity

`Viewer.Entrance.cs` is an experiment adapter, not a hidden default:

1. Route search uses existing public BFS and delivers its result at a later pump.
   The native planner's priority, graph, timed budget, cancellation and global
   search-node/request resources are NOT implemented. OUTPUT allocation now uses
   GuestWalk's shared1000-slot pool and the real packed cursor; see
   native-route-slot-pool.md. Unported legacy walkers/guards do not consume it.
2. Model-animation readiness is explicitly bypassed. Speed/delta arithmetic is
   native, but the model9/13 readiness join remains open.
3. The ordinary constructor entrance-fee seed150 is used, with saved scenario/UI
   overrides unjoined. The rendered fixture explicitly selects fee10; it does
   not claim fee10 is authored. `NativeEntranceAcceptance` consumes the actual
   placed-object NativeRideValue sum, strict fee<cash, randomized class, one
   admission count before finance, and the fee reread returned for guest debit. There are THREE fee reads on
   success: cash quote210CE8, class fee210BD4 AFTER value-sum/RNG, charged
   fee100D28. The implementation initially reused the quote for classification;
   final raw-instruction review caught that before the checkpoint.
4. The mode13 exit scan is restricted to the proven normal incoming-head point2,
   already in the native flag8 corridor. It accepts real path sprite kinds while
   excluding the port's bridge-as-Path policy. It does not substitute IsEntrance
   for the complete native tile flag array or support arbitrary loaded start
   coordinates. Out-of-grid stops are managed safety rather than raw native reads.
5. Guard staging remains unjoined. A shared represented-activation sequence now
   supplies rejected departures; native startup/restore history and absolute tick
   origin are not verified. Normal rejection routing/tally is connected, but
   ordinary departure/decision continuation and failed-route recovery remain open.
   Guest.Id is not substituted for the shared serial.
6. Unexpected disappeared guests and explicit map reset use labeled managed
   ownership cleanup. Native indirect destruction callbacks remain unread.

The controller is engine-free but NOT merely a spare helper: actual experimental
Viewer bus births, update ticks, counts, money and actors now consume it.

## Evidence and tests

`NativeEntranceFlowChecks` currently113 checks through real ParkVisitors and
GuestWalk, with explicit controlled services. Includes both groups, phase/head
blocking, same identity, refused/failed requests, stale callbacks, accepted
retry/no repeated fee, rejected ownership, teardown, batch12 broadcast, and
selection against actual pre-existing counts10 versus11. No private state edits
force success. `NativeEntranceAcceptanceChecks` adds23 checks for integer bounds,
strict cash equality, low32 overflow, RNG ordering and equal credit/debit of the
reread fee. Existing76 cursor and87 actuator controls remain required.

`NativeEntranceFlowSmoke` uses ordinary Viewer loading, actual build-menu placement
of a positive-value attraction, actual bus admission and the rendered actors.
It changes fee to10 explicitly and turns off FURTHER batches after the first
real drop-off to isolate money accounting. JUNGLE first run passed649 checks,
including same actual actor/guest through each update and exactly one fee credit
and debit, ending with no incoming membership or staging count. This one-birth
case proves the automatic hookup; the multi-member core tests cover pressure.
It does not prove native route-service or departure equivalence.

Mutations caught: generic walker also stepping manual leases (8 failures),
phase period16 instead of32 (4), group threshold12 instead of11 (2), missing
mode16 staging decrement (11), repeat acceptance while awaiting exit (8).
The initial period16 mutation PASSED: both first releases were after tick16 and
the test stopped before the next half-period. Extending observation through the
ready replacement head's half-period caught it. Record this as a test correction,
not evidence that the first passing test had protected the scheduler.

## Peer-review follow-up

Native released heads really prepend:14DA98 loads old head,14DAA4 writes it to
new.next,14DAB4 installs new head. The same-tick priority is not a managed guess.
Repeated liveness scans were removed via GuestWalk reference-identity HashSet and
separate ID multiplicities (ordinary Readmit historically permits duplicates).
Spawn, Readmit, ReadmitTerminal, Remove and Clear maintain both ordered view and
indices; public view is read-only. Native route owner lookups use the same index,
not another hidden linear Contains scan. Identity checks now87; controller113.

Mode13's head precondition now compares source fixed-point Points, never rendering
floats. Previously short/256 was exactly representable, so no observed float drift
bug was fixed; this change expresses the correct invariant and avoids depending
on the render conversion. No tolerance accepts a non-head position.

Explicit DiscardEntranceGuest increments DiscardedEntranceGuests separately from
WentHome and removes actual plan/needs/lease. This is a teardown census, not a
new native counter or proof of a global admission conservation equation; a guest
could already have paid before reset. Repeated Clear cannot count twice. Three
mutations (missing liveness Clear, terminal registration, discard count) each
produced two failures and were restored.
