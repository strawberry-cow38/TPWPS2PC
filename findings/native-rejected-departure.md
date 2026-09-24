# Rejected guests: outbound booth journey and failure contracts

September24,2026. Research branch, opt-in experiment; normal rejection no longer
holds forever. Startup phase origin, search/readiness and ordinary failure
recovery remain explicit adapters/boundaries, NOT a finished native-game port.

## Baseline path, same identity throughout

210C98 rejection selects state26.14E288 literally returns1 at14E28C, so baseline
210D70 compares(N+14&63) with(1C4930()&63). Mismatch sets stickyN+A4=1 without
calling the target helper. Match calls1532D8, sets mode14 and submits18DA78 with
flags0x21, priority0. Immediate refusal stays26 and setsA4; acceptance timestamps,
selects pending0B and does NOT clearA4. Activation serial is shared, not Guest.Id;
see native-activation-serial.md for the implemented origin adapter.

Success event1 clears mask20, selects walking3 and logical model-control13.
Movement and slot retirement follow the shared pool consumer. Mode14 completion
20DC44 sets outgoing discriminatorN+28=1, staging2E andP++;20DC6C writes facingpi.
The port now preserves existing native facing during route replacement and makes
this explicit pi write; it does not guess it from the final route direction.

Coordinator event9 and direct mode16 reuse the same staging path. Mode16 allocates
BEFORE1532D8/RNG, retains currentx and helperz. The outgoing discriminator selects
state30 without RNG2 or incoming-group registration; only completion decrementsP
and incrementsR. State30/211220 consumes RNG(1), then14E290(index0) gives bus point0.
It submits mode9, flags1/priority0. Immediate refusal stays30 and does not setA4.

Mode9 completion20DB38 returns the actor to its native pool and cleans membership/
remaining route. The port consumer removes the SAME guest's walker, plan, needs
and visible actor through CompleteNativeDeparture/WentHome, not Discard or a
respawn. The guest must reach point0; no teleport from the booths. Rejection does
not credit/debit an entrance fee or increment paid admission.

## Async failure is not synchronous refusal

The callback join is positive:18CF30 descriptor35A548, event getter118AB8 reads
payload+4; owner virtual+16C adjustment-8 targets20F588 through36CE48/4C. Event1/2
require state0B. The mode14 event2 table entry36CB3C targets20F6B4:
- mask20 clear: immediately submit another mode14 request to a fresh1532D8 point,
  flags0x23/priority0, WITHOUT phase gating. Acceptance sets mask20 and0B.
- that alternate's synchronous refusal leaves0B with no accepted new request.
  It does not restore26 or auto-retry on the next phase.2106E8's pending handler
  does model/timing work, not the invented rescue transition.
- mask20 already set: select ordinary state0, retaining mask20 andA4.
- actual later output success clears mask20, notA4.

Mode9 event2 is generic20F790: signed happiness byte minusRNG15, floor0;
signed unknown78 byte plusRNG2, cap100 only; clearN+28 and select state5.
It does not retry30 or remove the guest. The prototype retains ownership at
state0/state5 and explicitly reports unported ordinary recovery. Null serial
callers likewise retain the old explicit boundary instead of inventing an ID.

Output-allocation failure follows event2 semantics too, not just a successful
search result. Services.AfterResult runs AFTER callback handling, including stale
results. In the native pump, a finishing request record is recycled only after
the callback returns; a flags23 callback request can therefore fail with all10
records occupied even though one will become free immediately afterward. The
controlled-service test discriminates that ordering. Actual native search/request
resource management is still not implemented by the Viewer's deferred BFS.

## Ordered sticky pressure

A4 is cleared at activation20BCF4, set on phase deferral/initial refusal at210E64,
and not cleared on successful travel.151960 resets the global tally AFTER group
updates;211724..34 counts an already-set flag BEFORE active state dispatch. A new
setter contributes next pass; a counted actor removed later still contributes
this pass. NativeEntranceFlow implements that ordering for its owned cohort and
the bus consumes that value in the experiment. Ordinary legacy departures and
unported guard traffic are not silently represented as complete native pressure.

## Consumer evidence and remaining limits

59 core checks cover actual ParkVisitors/GuestWalk with explicit serials, both
phase bits, request flags21/23/1, callback-before-record-recycle, sticky timing,
mode14 pi, native failure boundaries, actual departure cleanup and30 real owned
sticky contributors. Eight mutations caught: five-bit phase2failures then guard,
Guest.Id phase5, clearing sticky10, post-dispatch tally2, teardown instead of
departure2, mode9 retry1, skipped RNG1 draw1, omitted pi1. Restored sources pass.

The first integration run caught a real consumer bug: every route replacement
reset the plan to Entering, so final departure's owner/Leaving guard refused.
Assignments now preserve an already-owned Leaving plan; tests assert it stays
Leaving throughout, rather than weakening final removal's precondition.

NativeRejectedDepartureSmoke uses two ACTUAL placed rides followed by a real
bus cohort. Fee=int.MaxValue is an explicit rejection fixture, not an authored
price. JUNGLE passed2399 checks: all three actual guests and their same actors
walked to point0 before disappearing, no fee was collected, WentHome not teardown
was counted, sticky pressure included the last removal pass, and all slots freed.
The represented activation sequence is explicitly checked NOT to be Guest.Id or
claimed verified startup history. This is normal rejection behavior, not proof
of native search, readiness, exact phase origin or every recovery case.


## Peer review: preserve failures from both callback and acknowledgement

Cow reviewed6e37ebc and agreed with callback-before-recycle and tally-before-update.
The managed finally initially replaced a handler exception if acknowledgement also
threw. Now handler-only rethrows its original exception/stack; acknowledgement-only
is distinctly wrapped; simultaneous failures aggregate both original exceptions.
Acknowledgement still runs exactly once, after handling, including stale results.
Six assertions exercise the three actual callback fault combinations. Restoring the
old finally causes two diagnostic checks to fail; restored production passes59.
This is managed failure diagnosis, not an invented native recovery transition.

After merging main0f2ab51, all eight actual Viewer park variants passed BOTH paths:
accepted651 checks each, rejected2399 each. All16 exited0 with no engine errors or
resource-leak lines. Exact scene PASS prefixes were independently checked (a loose
substring PASS would incorrectly match the startup warning's word BYPASS).
Logs outsideGit: /tmp/tpw-departure-eight. Later acknowledgement-diagnostic change
has no successful-path behavior change; final consolidated gates recorded in progress.
