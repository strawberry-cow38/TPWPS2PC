# Native guest coordinates and route-slot stepping

2026-09-24, PAL owner-disc MIPS. Read in memory, not an extracted executable.
Descriptive names below are not recovered class symbols. `NativeGuestMotion` is a
checked arithmetic foundation on the entrance-flow branch, **not yet a replacement
for the shipping GuestWalk**. No claim that guest speed or entrance queues are fixed.

## Object frame and slot representation

Guest allocation N has signed halfword coordinates N+24/N+26: a cell is256 raw
units. Common movement functions receive B=N+8. Current slot is signed halfword
B+28=N+30; mode B+2E=N+36 is distinct from execution state B+2F=N+37. N+1C is the
state-stack depth, not a movement accumulator. Preserve the observed16-bit storage.

Each route slot at3AE1B8+4*index is four bytes:

* header bits0..10: next slot,7FF means terminal;
* bits11..12: x quarter-cell fraction;
* bits13..14: z quarter-cell fraction;
* bit15: allocated;
* byte2/byte3: signed cell coordinates on decode.

`192560` adds32 to each requested coordinate, then stores its cell byte and two
fraction bits. It retains header mask87FF (allocation and next link). Its x-cell
byte store at1925A0 is in the return delay slot. `1924D0` reads the bytes with LBU
but explicitly sign-extends them with SLL24/SRA16 before adding fractions*64.
Equivalent resulting coordinate: signed16(64*floor((input+32)/64)). This is nearest
quarter-cell, ties toward the larger coordinate, with storage wrap—not whole cells.

Literal instruction-control examples:

| input x,z | encoded/decoded x,z |
|---|---|
|31,32|0,64|
|95,96|64,128|
|-33,-32|-64,0|
|1280h,2340h|1280h,2340h|
|12A1h,22A0h|12C0h,22C0h|
|32767,-32768|-32768,-32768|

Entrance-list targets are at a cell center minus64z per preceding member. Those
are already exact quarter-cell points; replacing membership by occupied whole
cells would lose queue position and cannot produce the native W count.

`18C698` stores coarse search cells separately from original request coordinates:
task+1C/+20 retain target x/z; task+24/+28 retain start x/z. `18D358` sends the
original target to192560 at18D4B0, while intermediate waypoints use cell centers.
Thus the final route point retains quarter-cell fractions even though the search
is cell-based. Slot handoff and search/resource policy are separate from encoding.

## Walking arithmetic, readiness and facing

Guest vtable36CCE0: +148/+14C has adjustment0 and target191E98, receiving B.
Its speed callback +188/+18C has adjustment-8 and target2122B0, receiving N.
2122B0 returns signed byte N+7C in the return delay slot.

After a valid slot and animation-readiness check,191E98 decodes the waypoint,
subtracts current signed coordinates, then computes:

    speed = max_signed(5, signed8(N+7C))
    step = unsigned32(low32(speed * D)) >> 14
    nextX = low16(currentX + clamp(targetX-currentX, -step, step))
    nextZ = low16(currentZ + clamp(targetZ-currentZ, -step, step))

D is the native1C4920 delta, not1C4930's update counter and not seconds. Its writer
1C4AA8 caps positive values at4000h; negative inputs are not repaired. The arithmetic
helper accepts D explicitly. At191FA0, word02028018 is the R5900 MULT form writing
its low product into rd=s0; a two-operand disassembly printout omits that result.
191FA8 uses logical SRL14. There is no retained sub-unit remainder or diagonal
normalization: both axes can move a full step. Signed speedFF becomes-1 and selects5.

The candidate signed cells must lie within getters14E0F8/14E108. Out-of-bounds
movement releases the route and sets state0 WITHOUT committing either coordinate.
Otherwise candidate halfwords are committed; exact target equality sets state2.

Readiness is separate: unless global2E2920 bypasses it,191E10 must permit movement.
No model permits it; particular control modes9/13 check the model animation byte.
Facing is selected before stepping, with nonzero x taking precedence, then z.
The arithmetic foundation intentionally implements neither readiness nor facing.

Observed speeds: ordinary activation uses15+RNG(15) for N+7E, then copies it to7C.
211A00 is the separate level-template path: active speed from template14/15 and
baseline from16/17 using the centered roll. It has the direct caller160410, not
the bus activation path. Accepted entrance-queue request210E80 temporarily writes15;
210C98 and20EE5C restore7E. Do not conflate baseline and active speed or reseed on
readmission. More temporary-speed producers exist; this is not an exhaustive census.

## Reaching a waypoint is not completing a route

The terminal lifecycle has distinct guest updates:

1.191E98 reaches its current target: state2, slot still present.
2.20D628 sees a present slot and calls191D78: read next link, free old slot,
  install next (7FF becomes-1), set state3. No unused movement carries forward.
3.191E98 with slot-1: set state2 without moving.
4.20D628 with slot-1: finally dispatch the movement-purpose mode.

Direct-slot allocation failure remains distinct from an exhausted route. Likewise
18DA78 returning0 means request admission failed, not that the guest arrived or
that a successful search already established an unreachable destination.

The port's existing GuestWalk merges/carries several of these stages and uses
1000-unit whole-cell edges with chosen speed. The new primitive must not silently
be called a native walker while those lifecycle differences remain.

## Request-pump ordering, independently checked

151928 calls18D7F8 INSIDE the per-update loop, before152A18's entrance-list pass,
then the2EEB9C reset and14BE60's active guests/bus. Requests submitted by that
substep's guest pass are therefore not handled by its already-finished pump.
18D7F8 operates under a195E28()+100 budget; after processing its first task it
selects further tasks by task+34==1, and exits when the timer exceeds its deadline.
The units of that budget and full route-search/pool semantics are not assigned here.
A synchronous BFS return must not be mistaken for this request/completion protocol.

## Checks and limits

55 numeric checks cover signed wrapping, rounding ties, header/link preservation,
quarter-spacing, signed speed minimum, low32 multiplication, logical shift,
independent axes, zero/small/negative delta, no remainder, bounds refusal and
clamped target arrival. Four mutations are caught: removed rounding bias(5),
removed minimum5(5), shift13 instead of14(15), omitted bounds refusal(12).
All restored. `--guest-motion-only` explicitly reports arithmetic-only scope.
No new guest motion, capacity reduction, queue ownership or entrance fees are wired
into the game by this foundation. The next required consumer is an ID-owned native
entrance/route lifecycle, not an invented queue count.
