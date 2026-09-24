# Front ticket booths: guest admission behavior, not moving geometry

September24,2026. User clarified “turnstiles” means the front booths, and that
static geometry is already understood. The relevant question is what guests do
there. Do not keep researching arch doors or animation to answer that question.

## Spatial and state join

Native incoming queue helper1527C8 uses entrance row+4/+5 cell centre, with each
preceding member subtracting64/256 from z. Both lists return the same base x
(the nonzero-group preliminary x+1 is overwritten). In Jungle the head point is
(29.5,15.5), inside the measured ticket_booths terrain envelope x28.31..31.69,
z14.88..16.12. This places the payment operation at the front booths, not the
separate themed arch. It does not require a named booth-object callback.

Mode11/12 completion20DB8C/20DBD0 compares actual fixed-point position with the
recalculated list position before selecting waiting state2C.152A18 releases only
that state's HEAD on its group tick&31 phase, selects25 and transfers the SAME
guest to active updates without moving it.210C98 therefore evaluates the entrance
price while the guest is still at the booth-head point. Strict fee<cash and
class>-2 accept; success counts admission, credits finance and debits returned fee.
Rejection selects departure state26. No proven barrier-animation acknowledgement
is required by that release/acceptance chain.

Payment is BEFORE inward crossing. Accepted state2D runs210FE8, setsN+64=1 and
finds a direct mode13 goal after the native tile.flag8 latch and kind2/13 test.
With Jungle's initialized tiles unchanged:
- x29,z15..17: kind0C, flag8 (14E938..14E9BC);
- x29,z18: kind0E, flag8 (14E9D4..14EA80);
- x29,z19: kind2, flag8 (14EB54..14EBE0).

Thus, if its first eligible slot allocation succeeds, the normal accepted guest
moves from(29.5,15.5) to(29.5,19.5). Changed/loaded tiles or allocation failure can
alter the selected goal; this is not an unconditional saved-game destination.
Movement arrival and slot retirement stay separate. Mode13 completion20DC1C
selects ordinary state0 and resets movement mode2, with no second fee. Missing
goal/allocation retries stay2D, also without another fee.

The research incoming controller already exercises these queue/payment/crossing
states through actual bus guests. Its route-service/readiness/fee-override limits
are in native-incoming-controller.md; rejected guests remain held pending the
proper departure implementation. None of that is fixed by a scenery census.

## Audiovisual boundary

Guest drawing211D28->192438->1921D0 consumes the guest's own position, facing and
model-control field. Decimal movement mode13 atN+36 is NOT animation record13;
model-control is separateN+38. No booth-local moving mechanism or per-admission
sound was positively joined in this trace, and none is needed to explain the
established payment/guest-motion operation. This is not proof of their absence.
Acceptance1073C0->10DDD8 accumulates classification into statistic19 (clamped
±30000), not an immediate audiovisual cue. Normal mood/need effects are separate.

## Second staging population positively identified as guards

The previously unnamed pool3952AC allocates through14AF30. Load160448 calls it at
16048C, prints `Load_ReadGuards %d` through string361368, and loads the object via
1416D0. Constructor140540/vtable35F2E0 is the family containing140D64. This is a
positive guard load path, not evidence of booth attendants. Its incoming mode16
completion selects state37, not guest registration2A, while sharing staging P/R/E.
The experimental ordinary-guest-only coordinator consequently omits guard traffic;
that boundary now has an identified class rather than a guessed role.
