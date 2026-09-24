# Native park entrance lifecycle — causal trace

2026-09-24, PAL owner-disc MIPS and authorized partial C. Addresses are VAs;
N is the guest allocation and O=N+8 its embedded object. Hexadecimal state/offset
numbers are distinct from decimal movement modes. No runtime gameplay change is
claimed by these findings. Complements/corrects `bus-entrance-groups.md` and
`bus-admission-pressure.md`; quarter-cell motion is in `native-guest-motion.md`.

## The two counted groups are BOTH incoming

The earlier partial note observed mode15 writing N+28=0 and mode14 writing1, but
these writes occur BEFORE actual entrance-list registration. Calling them entry
and departure list membership would be wrong. `20DC70` consumes this discriminator:

* old N+28==0: draw RNG(2), store the new group, read its global membership count
  through1527B0, and flip once if count>=11. Set state2A for registration.
* old N+28!=0: clear word N+28, set state30 for departure. No group registration.

The threshold is literal instruction2842000B (`slti v0,v0,11`) at20DC94, not a DBA
MaxCapacity field or a capacity measured on an attraction. Both counted groups can
receive the incoming branch. Authored shop entry/exit coordinates are a DIFFERENT
system and cannot label these park lists. This reuse of N+28 is central to the join.

## Incoming state chain, requests and fee timing

Bus batch16B5C0 calls14AC48. Guest virtual activation20BCD0 runs before pool return;
only afterward the batch sets personality index withRNG(8) and point0 coordinates.
Activation establishes state24. The ordinary state table36CB90 then gives:

* state24 ->210AB0: request route to1532D8's point, movement mode15. Request
  acceptance sets state0B; rejection leaves24 for retry. State0B waits for the
  asynchronous result, not physical travel. Success event1 changes0B->3;
  failure event2 for mode15 restores24.
* route exhaustion mode15 ->20DC28: N+28=0, state2E, call153298.
* coordinator event9 ->20F588: state2E->2F.
* state2F ->211160: free old slot, acquire direct slot. On success set mode16,
  target current x plus1532D8's z, state3. Failed allocation stays2F.
* mode16 exhaustion ->20DC70: incoming branch chooses group and state2A.
* state2A ->210E80: register1527C8 BEFORE requesting travel to its assigned
  quarter-cell point, flag N+34 bit4, mode11. Accepted request sets speed15,
  timestamp and state0B. Request refusal does not undo membership.
* mode11 completion ->20DB8C: idempotently re-register/recalculate target;
  equal coordinates ->state2C, otherwise2A to move again.
* eligible queue head release152A18: state2C->25, decrement count, unlink O
  and insert O at the active-list front. Entrance evaluation follows afterward.
* state25 ->210C98: clear the group marker/flag, restore baseline speed,
  initially set state2D. Compare guest cash to the current entrance fee and
  evaluate acceptance. Rejection instead selects state26.
* accepted state2D ->210FE8: marks N+64=1, searches a suitable inward point
  and assigns mode13. Retry stays2D and does NOT re-charge the fee. Mode13
  completion20DC1C changes state to0, normal decisions.

Important target control:1532D8 ignores its first argument. It reads row+2/+3,
adds RNG(256) to x AFTER sampling height, and returns the same z for selectors0/1.
211160 really calls that helper (JAL at2111B8) but uses CURRENT x and returned z.
Do not invent separate crossing endpoints from the selector values. Immediately
following mode15 completion, mode16 can have an identical quantized target; it
still has its slot/state lifecycle. Row+4/+5 supplies entrance-list position,
not this staging point and not bus point0. `153380` separately uses row+10/+11.

## Queue ownership and scheduler

1527C8 searches the selected list for O=N+8. Existing membership returns the
recomputed point without incrementing. Otherwise it unlinks O's embedded links,
appends and increments count[3953D0+4*group]. There is NO direct decrement of a
previous group's count in this registration path. Do not invent native repair.

Each group sentinel is20bytes at2B7370+20*g. 152A18 tests group0/1 only on update
counter phases `(1C4930()&31)==g`, and releases only a state2C HEAD. Every remaining
member is updated with saved-next traversal. After a release, descriptor35A530
is event6 (getter118AC0), payload target state2B. Waiting guests release their slot
and enter2B before that group's update, so210F20 can immediately allocate/reposition
via mode12. Failed allocation remains2B. Mode12 completion repeats the target check.

The ordering is route-task pump151928 -> group pass151954 -> reset departure tally
151960 -> coordinator14BF58 -> active guest virtual updates14BF7C -> bus. Shared
embedded links prevent normal group and active passes from double-updating a
transferred guest: a released head joins the later active pass, while registration
from that active pass occurs after the already-finished group pass. Pool allocation
links N+0/N+4 are distinct from these embedded N+8/N+12 links.

## Staging coordinator is a third input, not queue length

Aliases: P=[3953D8], R=[3953DC], E=[3953E0], bus step S=[395280].
153298 increments P;1532B0 decrements P and increments R, without guards. Both
ordinary guest mode15/14 completion and the separate140D64/80 family call them.
That second class is not yet assigned a gameplay name. P is not either queue count.

14BCC0, independently checked in raw instructions:

* P!=0 and E==0: R=0, E=1.
* R<11, P!=0, E==1: walk both allocated-object pool lists and broadcast event9
  to EVERY state2E member. This is not a release quota of eleven objects.
* If P==0, or R>=11 AND S==2, clear E only when E==1. E==2 is retained.

This executes before active guest updates, so P/R changes from that pass are not
seen until the coordinator's next invocation. The bus's state1 traffic guard
reads this E; bus state2 writesE=2, other advancing states write0. Thus missing
staging behavior is separate from missing counted queues and the+A4 tally.
The present Viewer supplies only the bus's own0/2 traffic updates; no native guest
staging producer currently writes1. Do not call this constraint fully implemented.

## Entrance acceptance/finance source, not an attraction price

210C98 gets finance object F via1005D8 (cached2A60B0). Getter100D20 returns F+0.
Acceptance first requires strict signed `fee < guestCash`; exact equality rejects.
Only then210B38 is called; its returned class must be greater than-2. The class
sums object virtual+1D4 values via1E5AF0/1E5BA0/1E5CA8 and compares a randomized
park-wide quotient against fee/10. These value producers need their own identity
join; no attraction-count/nearby-money approximation is justified.

On success, increment manager16AE90+20, call100D28(F), then subtract its RETURNED
charged fee from N+60.100D28 re-reads F+0, credits F through100750 and updates its
accounting fields. Rejection does not increment, credit or debit. Fee setter100D18
has constructor/UI/load callers; constructor100470 uses0 when14E160()==2, otherwise
150 raw units. The full fee-setting UI/save path remains outside this trace.

The named ForceKidsToEnter call at210C08 does not establish an enabled cheat:
230260 in this image returns0 in its return delay slot. Even its hypothetical
nonzero return would not bypass the earlier cash comparison.

## Departure ordering and cleanup limits

State26->210D70 is the phase-gated departure request, mode14, sticky+A4 on deferral
or request failure. Mode14 exhaustion sets N+28=1/state2E and contributes P; after
the coordinator/mode16 chain this selects state30.211220 then routes toward the
bus point with mode9; mode9 exhaustion reaches pool return14B368.

20C930's departure conditions DO NOT immediately return from state0 decision:
20CA70 writes26, then execution continues through clock/RNG(6)/RNG(300) and the
six-arm action switch. Parent checked20C950..20CAC0, including branch delays.
The current port's early WantsToGoHome/continue is therefore not sufficient to
produce the exact native departure lifecycle. Later arms may preserve/push/replace
that state; the complete decision must be joined, not reduced to a Boolean veto.

14B368 invokes20BFD0, unlinks embedded O through14DAC0, decrements allocation-pool
count and returns N to the free chain. The unlink can remove a grouped object, but
these direct bodies do not decrement a group count or P.20BFD0 has indirect callbacks
whose cleanup remains unread. Likewise registration does not repair cross-group
counts. A managed safety invariant may be prudent, but must not be attributed to
native code until those callbacks are resolved. Bulk initialization explicitly
zeros queue counts/sentinels, P,E,S; R resets operationally on an E0->1 episode.

## Next implementation boundary

The port still lacks explicit ID-owned entrance/staging states, asynchronous route
request/result handling, quarter-cell slot movement/retirement and fee acceptance.
`NativeGuestMotion` covers only the numeric coordinate portion (55 controlled checks).
Do not wire W to nearby walkers, leaving guests, occupied tiles or a guessed lane.
No runtime entrance reduction, traffic coordinator or departure tally is fixed by
these research notes. Full pathfinder resource capacity/readiness, cancellation,
second coordinator class, indirect cleanup and acceptance-value producers remain
bounded open work.
