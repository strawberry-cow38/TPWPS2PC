# Native shop approach, completion and reselection — instruction evidence

2026-09-24, SLES_500.32. Read from the supplied disc in memory; no assets/executable exported.
PT_LOAD file0x1000→VA0x100000, size0x27E008; .reginfo GP0x385E70. Existing ELF hash:
231771d7f39cc29e7573ce62f437af15d97737a64c1d1c49df8404ead0b7577a.
This is static control/dataflow evidence, not a console execution capture.

This CP4 result supersedes claims that no repeated-visit limiter exists, that mood alone
explains the symptom, or that SAM stand fractions prove the original window-approach target.
It does not make a complete native AI port out of the current coordinator.

## Reproduced port symptom, including successful purchases

A real JUNGLE IceCream script + compiled kind4 definition, frozen needs growth/queue cadence,
and an available wander destination reproduces immediate retargeting. At Walk.Time4160ms:

* cash1234: one actual purchase, hunger91→66, cash934; plan already Heading to the same shop.
* cash299: purchase refused, hunger91/cash299 unchanged; plan also Heading to that shop.
* In BOTH cases, one subsequent Step(0) leaves the clock at4160 but advances Boardings1→2
  and QueuedOwner becomes that same shop instance.

The probe checks physical handback separately from Purchases, so refusal is not an empty
"no purchases" control. This disproves the hypothesis that rapid need growth or refusal
alone is necessary for the immediate re-entry. Idle's random Open selection still includes
shops even when the need was satisfied; there is no post-completion decision timing here.
Local probe/code: ../tpw-native-shop-probe, ../tpw-native-shop-repeat-probe.log.

## Native approach: inside entrance cell, not the outside path stub

Guest vtable36CCE0. Native approach20DD70 calls1E1760 at20DE98, adds placed-object origin
via virtual+74→1E1FA8, then for shop kind4 forms `(cell << 8) + 0x80` for BOTH coordinates
at20E024..3C. It submits that centre to common route request18DA78 at20E064.

1E1760 obtains the compiled payload through virtual+5C and reads signed connection-A X/Z
at+0C/+0E. It rotates the connection cell through1E2288; there is NO direction step.
The rotation uses width at+8 and depth at+A, with rotation from placed-object+98:

| Native rotation | Result |
|---|---|
| 0 | x,z |
| 1 | z,W−1−x |
| 2 | W−1−x,D−1−z |
| 3 | D−1−z,x |

The separate helper1E23F8 DOES return the outside cell: it calls1E1760, reads direction
through1E1998 (payload+14), adds rotation, then steps z−1/x−1/z+1/x+1. Shop approach calls
the inside-cell helper, not this outside-cell helper. Native and port turn numbering have
opposite senses; do not equate their numeric rotation values blindly.

Named witness: JUNGLE IceCream key245, DBA payload offset9748 length72, kind4/textrow1069,
width2/depth2, A=(0,0), direction0, B=(-1,-1). Coconut241/offset9420 has the same connections.
IceCream SAM `**;2*` marks(0,1); the port mirrors rows to inside(0,0), direction(0,−1), then
Placement.OutsideOf creates an ordinary PATH stub(0,−1). Viewer supplies that outside stub
as ParkRide.Entrance and ParkVisitors routes to it. At rotation0, the port stops one cell
short of the native requested destination. Calling the variable "queueStub" does not make
ordinary shop stubs queue terrain.

Native route-task initialization18C698 stores the destination at task+1C/+20; route build
18D4B0 passes it to encoder192560 and18D650 installs the chain. Guest notification+16C→
20F588 changes waiting state11 to walking state3. State3 calls+14C→191E98, which decodes
waypoints through1924D0 and writes guest+24/+26 coordinates. Equality advances to state2.
This is common guest walking, NOT RSE WALKON. Missing WALKON therefore does not imply a
stationary approach. Native permission for entering the occupied cell was not established
in this bounded trace; do not make the whole footprint globally walkable as a shortcut.

## Arrival/service and position retention

State2→20D628 first checks remaining route index G+30. If not−1 it advances through191D78;
only route exhaustion enters the arrival-purpose switch. Purpose0/kind4 enters20D87C.
The non-indoor branch checks/sets bit32 of complete SHOP+ C0, sets animation/state, and
enters state35. State35 dispatches through20E160; strict clock-deadline expiry enters22.
State22 calls20EDD8; its kind4 arm calls purchase20E1A0 at20F170.

Resource flag1000 chooses the indoor branch, where20D918..92C hides the guest. The traced
SAM loader connects that flag to ISIndoors. Completion20EDF8..EE08 reveals the guest through
virtual+2C→192C10, which updates guest visibility and forwards to model virtual+AC. RSE's
hide helper reaches the same model slot; that does not make the native path an RSE handshake.

Shop completion20F0B8..184 clears the non-indoor reservation, calls purchase, clears G+28,
clears state-stack G+1C and sets state G+37=0. It does NOT inspect purchase's return value,
assign a new position or consume an ExitCellAppearPos fraction in this traced branch.

## Parsed SAM fractions are not established native approach coordinates

The keys ARE referenced, via an indexed schema, not the previously unsuccessful direct
string scan. Accessor1F98C8 returns2E7548+60*index (vtable36AD90,+0C). Records54..57 contain
EntryCellStandPosX/Y and ExitCellAppearPosX/Y; names at record+4 are2E81F4/2E8230/2E826C/
2E82A8. Loader1F7CB0 builds a temporary parser, appends .sam and calls114048 at1F7D18.
Initializer112680..112844 maps the four values to parser+D0/D4/D8/DC, this loader's
stack+1E0/1E4/1E8/1EC. This concrete loader does NOT read/export those destinations before
its parser destruction at1F7E60. It retains other shape/hoarding/flag data.

That proves parsed-but-not-retained in THIS path, not unused everywhere. The prior authored
standing renderer was a port interpretation of those fractions, not an independently proven
original window-position consumer. Correct its provenance rather than use plausible field
names as a substitute for this native route trace.

## Refusal still follows completion scheduling

Let G be guest, P=G+28 placed base, S=P−8 complete SHOP. Purchase20E1A0 initializes result0
at20E1FC. Price>=willingness or cash<10*price branches at20E2F0..30C reach20E948. Success
sets1 at20E938..944; the return loads that result at20EAEC and returns20EB18.

Refusal has a common tail, not a side-effect-free return. It reports zero happiness delta
through1D1E68, increments the shop's +B4 count, and can write thought12/13 based on signed
(willingness−price)/2. The completion caller ignores v0 and resets the same native state
regardless of success. Do not infer a denial blacklist from those thought/tally writes.

20EDD8's COMMON prologue writes:

```
G+6C = now + 60 + rand(60)       // 60..119
G+2C = now + 300 + rand(300)     // 300..599
```

It also resets animation/state-related values and reduces G+7B. Shop completion does not
undo those deadline writes. State0 calls20C930; its random arm0 enters destination selection
only when unsigned `now > G+2C + 60 + rand(300)` OR G+28 is already nonzero. Handback cleared
G+28. The additional random draw occurs during decision processing, not as one permanently
saved delay. Other decisions/states can rewrite this reused timestamp. This is a concrete
ordinary-path delay, not a blanket universal minimum across every possible state transition.

### Clock units: do not invent seconds

These deadlines use1C4930, reading u32[397644], NOT16AE90→16B218. Initialization clears it;
1C4A70..7C increments it by ONE. Caller chain10EEE8→13ADD0→1C4A58 runs the producer when
an active object exists;2312E8 invokes10EEC0 in a loop controlled by331510 (initial1).
The established unit is one producer execution. Its wall-time cadence is not yet proven;
the port's40ms ticks do not establish a PS2 conversion. The other calendar-like clock has
a distinct accumulator/getter. Preserve this distinction in code and tests.

## Real selector, not just the want-setter

State6→20DD70 first phase-gates `(now & 7) == (guestID & 7)`, then calls20C6A8. This chooser
enumerates candidates, calls virtual+2DC eligibility, scores via20C138, and picks the highest
score. Equal scores use sequential rand(2) replacement. For the concretely constructed
SHOP vtable368080, +2DC→1E1E48 permits placed-state byte+9A values2/10/11. It receives no
guest; it is not a cash check.

The resolved shop score path checks the entrance/bounds, adds distance and hunger/thirst/
subtype contributions, then divides for candidate-ID matches against G+44/48/4C/50 by
5/4/3/2. These are penalties, not hard exclusions. A score<8 also costs happiness5 but does
not reject the chosen destination. No price-versus-cash/willingness veto exists in this
resolved shop selector; that conclusion follows the actual calls/virtual targets, not merely
absence of cash tests in20C930 or20F888.

The observed history writer is NOT a generic FIFO updated on every visit:20C76C..7A4 invokes
20C8D8 for selected non-kind2 candidates only under the traced G+79>=99 condition. Its forward
copy propagates the old first ID through the later slots. Do not port a conventional four-
visit queue and call it this code without further independent verification.

After route success,20E074..A8 writes now toG+2C and enters waiting11; failure adds360 and
pops state. Thus an unaffordable shop can be selected again after native scheduling/scoring;
this is not the port's zero-time immediate re-entry. Mood changes are an additional system,
not evidence that these timing/history mechanisms do not exist.

## Counter producer and conditional PAL pacing (follow-up)

The `331510` loop count defaults to1. Command0x10000002 in17DBF8 parses a decimal,
sets it at17DC64 (zero becomes1 at17DC70), and prints the diagnostic at363920:
“Gamespeed set to %d gameticks/rendertick”. Negative values skip the signed-positive
loop in2312B8. That loop calls10EEC0 N times, then225FC8 for rendering. Nested
pumps/loading paths also call it; this is not an unconditional wall-time clock.

21B014 registers VBLANK handler224C10. It increments310C98, and the pending render
submission is released against baseline310CB4 plus threshold310CBC (initial2).
Thus the normal PAL rendering path can imply25 updates/second at default gamespeed,
but neither this conditional throttle nor the default proves all guest updates run
at exactly25Hz. The port shares its existing ParkSim-derived update counter between
mood and selection; it does not create a second 60Hz clock.

`G+6C` is independently consumed by the entertainer interruption path in20FB88:
phase/flags/depth checks, strict deadline, then nearest member of PoolOfEntertainers
(35FEC8, pool3952BC), state28. Its handler2107A0 faces the entertainer and on exit
adds happiness5 and resets6C to now+900. This is NOT the shop destination gate at2C.

## Why the original can walk into a building entrance

Placement1E2AD0 first marks footprint cells as terrain kind5 via1E6138. For the shop,
virtual+2B4 validates the inside entry returned by1E1760. At1E2F14..2C that entry
cell alone becomes kind7. Its facing mask is written to terrain byte3 through1E8888
(native directions0/1/2/3 -> masks01/40/10/04). The outside helper1E23F8 is separate.

Route expansion18C928's kind7 arm at18CB60 requires BOTH the current cell's outgoing
direction bit and candidate terrain pointer == the task's destination pointer(+3C).
It is a **directional terminal cell**, not globally walkable building ground. The
path-link builder1E70F0 links a north public path to a south entry by checking kind7
and byte3 bit01, adding outgoing10 and reciprocal01. 18DA78 accepting the request
is not itself proof of route success: the queued task is later expanded/installed.

The common walking path installs the route through18D650; notification20F588 moves
waiting11 to walking3. Movement191E98 updates guest coordinates. Arrival20D628 checks
that the route is exhausted before entering the shop service path. Completion keeps
the current inside position; it does not read ExitAppearPos or teleport to a stub.
A port per-guest/live-owner terminal token would be an adapter for these permissions,
not a claim that the native kind7 arm itself checks an owner pointer.

## Bounded port integration: post-completion gate

GuestDecisionSchedule implements the traced `now+300+rand300` completion deadline
and strict `now > deadline+60+rand300` on decision arm0. ParkVisitors registers it
before Serve, so a refused purchase is delayed too. It shares VisitorNeeds.UpdateTicks
(or the walking counter when needs are absent), never advances on Step(0), preserves
the deadline when routing fails, and retires state with ownership. Successful explicit
SendTo remains an explicit override. Go-home handling remains ahead of this gate.

Scope: one attempt per *observed* coordinator counter value is port scheduling policy;
this does not reproduce every native state0 action, catch-up iteration, weighted
selector, or recency penalty. It fixes the reproduced immediate re-entry, not the
missing inside-entry walking leg. Guests may legitimately revisit after eligibility.

Independent checks use real named/compiled IceCream and both successful1234/refused299
cash fixtures. They require real handback, no zero-time reboarding, strict counter
boundaries, and eventual permitted revisit. Four mutations (missing registration,
missing consumer, non-strict comparison, repeated same-tick lottery) are rejected.
