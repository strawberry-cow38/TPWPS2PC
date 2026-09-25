# Native ride queues, read from the executable

2026-09-25, tinyclaw, at strawberry's request. Sources are raw MIPS from the owner's PAL SLES_500.32
(`tools/r5900dis.py`) and, where it exists, the authorized Ghidra corpus (FUN_0020D628, FUN_00210428,
FUN_001180B0, FUN_0020C6A8). Addresses sit beside every claim. **Ported, opt-in:** see "The port" at
the end.

## How the queue was found

The route planner only lets a request walk on queue tiles (kind 4) when its flags include 0x10 (see
native-route-planner.md). The flags are 18DA78's sixth argument (`t1`, stored at request+2C by 18C698).
Of the 25 call sites, the guest ones carrying 0x10 are:
- 20F460: flags 0x10, queue tiles only;
- 20F54C: flags 0x18;
- 211348: flags 0x11.

Mapping those back through the guest state switch in 2113A8 (jump table 36CB90) gives the queue
states.

## Structures

| Field | Meaning | Evidence |
|---|---|---|
| ride+F4 | queue head; a doubly linked list threaded through each guest's link node (B+0 next, B+4 prev, B = N+8) | 118540 counts it; 1180B0 and 117C90 unlink from it |
| ride+A0 | list of queue CELLS (byte x, byte z), starting at the ride and ending at the far end of the queue | 117340, 20F4D8 via 1A3368 |
| ride vtable +17C | the ride's rotated entrance connection cell, where spots start. 116EC0 adds the placed origin (+74) to the rotated compiled record +0xC that 1E1760 reads, which is connection A: the cell INSIDE the footprint whose door faces the stub (the port's `ShopEntrance.Connection`) | 117340 |
| ride vtable +F4 | the queue MOUTH: the last cell of the list (117280) | quit walk 2112D0 |
| ride+126 | upgrade tier byte | 20D530 |
| ride+108 | rider list; boarding links the guest here | 117C90 |
| ride+120 | u16 count of guests boarded, incremented per boarding | 117C90 |
| guest B+2C bit 2 | "is queued" flag | 20F3F0, 20D628 case 10, 20F2D0 |
| guest N+78 | queue impatience, 0..100 | 210428 |

## Capacity: two limits

1. **Head count (20D530).**
   - Shops, sideshows and features (kinds 4, 5, 2) always pass.
   - Rides (kinds 1, 3, 6, 7) must first pass the open-state test (vtable +2DC).
   - Then the guest either is already queued (bit 2), or the queue length (118540) must be below
     **7 + 4 × tier**: 7 at tier 0, 11 at tier 1, and so on.
2. **Physical length (117340).** Spots step a quarter cell per guest (next section). If the next spot
   would land on the queue's last cell, 117340 returns 0 and the guest cannot join. So the last cell
   (the entry from the path) stays clear, and a queue holds 4 guests per cell before it.

## Spot geometry (117340)

- A guest's spot starts at the **centre of the ride's queue-entrance cell** (vtable +17C).
- It steps **64 units (a quarter cell) per guest ahead** along the queue-cell list at ride+A0.
- The direction comes from the sign of the next cell minus the current one, per axis, and changes
  only at cell centres.
- If the first queue cell coincides with the entrance cell, the direction comes from the ride's
  rotation, `(1E1DE8 + 1E1998) & 3`, giving -z, -x, +z and +x for 0 to 3.

A guest already in the list gets the spot at its own index. A joining guest walks the whole list, gets
the spot after the tail, and is linked in after the last node (or as the head of an empty queue).

## Guest lifecycle

| Step | Code | What happens |
|---|---|---|
| arrive at the ride (mode 10 completes) | 20D628 case 10 | 117340 gives the spot and sets bit 2. Already there: **state 0x12**, animation request 11 (idle). Otherwise **state 0x29**. 117340 refuses: state 0, target cleared |
| 0x29, walk in the queue | 20F3F0 | 20D530 and 117340 again, then a planner route to the spot, **flags 0x10**, movement mode 3, then state 0B. Failing while queued: 1180B0 (leave) |
| mode 3 completes | 20D628 case 3 | at the spot: state 0x12. Otherwise state 0x13 |
| **0x12, waiting** | 210428 | the impatience clock; see below |
| 0x13, move up | 20F2D0 | re-checks 20D530 and 117340, then walks to the new spot on a **direct one-slot route** (192768/192560, movement mode 10); no planner request |
| 0x17, walk out | 20F4D8 | a route to the queue's far end (the last element of ride+A0), **flags 0x18**, mode 4, priority 1 |
| mode 4 completes | 20D628 case 4 | state 0, target cleared, idle request 11: back to ordinary visiting |

## Waiting: the impatience clock (210428, state 0x12)

The update counter is `now` (1C4930). The phase is the guest's activation serial, N+14.

- **Every 4 updates** on the guest's phase: if the ride's vtable +C4 predicate is true, impatience
  (N+78) goes up by 1, capped at 100. **+C4 is "the ride is broken".** In all four ride families it is
  1E2830, `lbu v0,+9A; addiu v0,-4; jr ra; sltiu v0,v0,2`. The delay slot turns it into
  `state - 4 < 2`, i.e. placed state 4 or 5, which are the breakdown states
  (native-selection-eligibility.md). Read without the delay slot it looks like "state != 4", the
  opposite.
- **Every 8 updates** on the guest's phase:
  - `mismatch = |taste (20C078) - ride value (+1D4)|` and `w = max(50, mismatch)`;
  - `rand(100 - w/2) < 2` adds 1 to impatience, so a worse match leaves sooner;
  - `rand(100) < 10` takes 1 off boredom (N+7B); waiting for a ride relieves boredom slightly.
- **While impatience is below 81:** after each `now + rand(300)` deadline the guest faces a random
  quarter direction (N+3C = rand(4) × 2 × 2π/8).
- **At 81 or more:** thought 9, effect 0x7E at the guest, and 1180B0, which takes the guest out of the
  queue.
- What this adds up to: at a working ride only the 8-update roll runs, +1 with probability
  2/(100 - w/2), so between 2/75 and 2/50. Guests spawn with impatience rand(40) (VisitorNeeds
  `Unknown78`), so leaving takes many thousands of updates. At a broken ride the 4-update tick
  dominates, and guests give up within a few hundred updates.
- The same B+2C bit 3 and 2E28D0 (< 25) bookkeeping as the happiness < 3 departure applies.
- Draw order, from the corpus: rand(100 - w/2), then rand(100), on the eighth-phase update only. Then,
  below 81 and strictly past N+2C: rand(300), rand(4), rand(10). The leave test comes after the
  boredom roll. rand(n) is `rand() % n` with a trap at n = 0 (1448E0: divu, break 7).

⚠ **+0x78 has a second riser, and it is not queueing.** 20C6A8, which the port's `VisitorNeeds.Queue`
cites as "adds +5 while the guest queues", is the destination picker (called from 20DD70 every 8
updates on the guest's phase). It enumerates attractions, tests +2DC and scores them with 20C138.
When NOTHING scores it takes 10 off happiness (+75), adds **5 to +0x78** and writes thought 4
(bored). When the pick scores under 8 it takes 5 off happiness. So +0x78 rises when a guest finds
nothing worth doing, and rises slowly in a queue (210428). The port charges `Needs.Queue` to guests
in its legacy queue; under native queues it does not apply, because native queueing guests are
never `Queued`.

## Leaving and moving up (1180B0)

1180B0 unlinks the guest and sends it event 7 through guest vtable +16C (event record 35A548); it then
calls vtable +2C(1) and 14DA60. **Every guest behind it** gets a move-up event (record 35A530) carrying
a stagger that grows by `rand(3)` per guest. So the queue closes up as a staggered ripple, not in
lockstep.

The guest event handler is 20F588 (guest vtable 36CCE0, slot +16C). It switches on the record's code
getter at +C: 118AB8 returns the record's +4 word, and 118AC0 always returns 6.
- **Code 7 (quit):** free the route slots (192840), clear the stack, state **0x3A**, clear the queued
  bit. State 0x3A (2112D0) routes to the ride's vtable +F4 cell with flags 0x11 and movement mode
  0x16, pushing 0x3A. Mode 0x16 completing (20D628) returns the guest to state 0 with no target. +F4
  (117280) is the **last** cell of the queue list, its mouth. So a quitter walks back out along the
  queue to where it meets the path, then resumes ordinary visiting.
- **Code 10 (queue emptied, from 117798's second mode):** target cleared, slots freed, state 0 at once,
  queued bit cleared.
- **Code 6 (move up):** only when the guest is in state 0x12, or 0x2C in the entrance queue. Free the
  slots, set the deadline N+2C = now + **3 × stagger**, clear the stack, state = the record's +8 word
  (0x13).
- State 0x17 (20F4D8, a flags-0x18 walk to the last ride+A0 cell) is not reached from these events.
  Its producer is still unread.

117798 empties a whole queue, sending each guest event 7, or event 10 in its second mode. **Its
callers are now traced** through the state setter 1E4D70 (it stores placed state +9A, then jumps
through 369A40 to the vtable slot for that state; state 4 lands on slot +21C, state 5 on +224):

| Caller | Where it sits | Mode | So |
|---|---|---|---|
| 1229A0 | coaster vtable 35B060 +21C | 0 (event 7) | entering state 4 (breakdown) empties the queue |
| 1E94B8 | tour vtable 369F10 +21C | 0 | the same |
| 2002D8 | track vtable 36BBF0 +21C | 0 | the same |
| 1B8C28 | ordinary vtable 366330 +21C | 0, only when +2CC (1E1D58: s16 +94) <= 0 | an ordinary ride breaking down keeps its queue while +94 is positive |
| 1B8BF0 | ordinary vtable +224 | 0 | entering state 5 always empties it |
| 116458 | a slot at 35A66C | 1 (event 10) | teardown: it frees the queue-cell list (1A3398) first. Demolition |
| 116048 | the constructor/reset | 0 | a new ride starts with an empty queue |

117758 is 117798 followed by 118018 (unread). **No caller is on the state-3 (closed) path**: closing a
ride sends its queue nothing. Its guests keep waiting on the ordinary clock, nobody boards, and a
guest that has to move up fails 20D530's +2DC test (state 3 is not in 2/10/11) and quits. That is
why 210428's broken tick is not dead code either: an ordinary ride in state 4 with +94 positive
keeps a queue that grows impatient four times as fast.

## Boarding: the queue meets the script (ordinary ride update, 1166A8)

Each ride update:
1. `1FA528(machine)`: the ride is full when script variable 5 >= variable 2 (1C10D8 reads). Full means
   no boarding. All 162 ride scripts on the disc declare **index 2 = VAR_CAPACITY** and **index 5 =
   VAR_ONRIDE**, read from each script's own shipped variable table. So the ride takes the next guest
   only while `VAR_ONRIDE < VAR_CAPACITY`.
2. Otherwise, take the head (ride+F4). **It must be in state 0x12**, actually standing at the front
   (read through guest vtable +DC).
3. `1FA368(machine, guest, 1, 1)` offers the guest to the script. It requires the machine and
   1FA168, then calls 107E48, 1FB3B8(guest, 1) and 1FA260(machine, guest): the LETMEON write the port
   models.
4. If the script takes the guest: 211FE0(guest, 1) sets the guest's animation request to **13 (walk)**
   and plays it immediately (argument 0 would give 11; it is not a fee). Then placed state becomes 10
   through +1F4 unless it is already 4, and **117C90** boards:
   - unlink from the queue;
   - guest state **0x15** (20E0E8), stack cleared, guest vtable +3C and +44;
   - link into ride+108 and increment ride+120;
   - event 0x13 with staggered delays to everyone still queued.

The other families call 117C90 from their own updates: tour 1EA288, track 201AE4, and 1AECCC.

## Against the port today

| | Native | Port |
|---|---|---|
| body | queued guests are physical, standing at their spots | the guest leaves the walking layer (ParkVisitors `Queued`) |
| capacity | 7 + 4×tier, and the physical length at 4 per cell | none: `Queue<int>` is unbounded |
| spacing | a quarter cell per guest along the queue cells | none |
| offer | head in state 0x12 AND riders < capacity (var 5 < var 2) | head whenever LETMEON is 0 (`ParkSim.Handshake`) |
| waiting | impatience, taste-weighted; leaves at 81 with thought 9 | waits forever |
| move-up | staggered ripple (event 0x13 + rand(3)) | instant (dequeue) |
| closure | the whole queue is emptied with event 7 or 10 | ParkVisitors recovery policy |

## Arrival, in order (20D628)

- **Case 0** (the walk to the ride completes): N+2C = now. If the ride has queue cells (the last one
  read through 1A3368), ask 20D530. Refused: target cleared, state 0. Accepted: state 0x29.
- **0x29** (20F3F0): 20D530 and 117340 again (117340 links the guest in and sets bit 2), then the
  flags-0x10 planner route to the spot, mode 3.
- **Case 3**: at the spot, request 11 and state 0x12. Otherwise state 0x13. N+2C is not rewritten, so
  the first waiting update is already past its facing deadline. 117340 failing here drops to case
  0x16 (state 0, no target) WITHOUT unlinking the guest.
- **Case 10** (the direct move-up completes): at the spot, 0x12. Otherwise 0x29 again.

## Spot geometry, one consequence

117340 walks in whole 64-unit steps from a cell centre (offset 128), and a cell is `pos >> 8`. Going
toward +x or +z the entrance cell holds 2 spots (offsets 128 and 192; 256 is the next cell). Going
toward -x or -z it holds **3** (128, 64 and 0, since 0 is still inside). So the same one-cell queue
holds 6 guests facing one way and 7 facing the other. It follows from the arithmetic, not from a
separate rule, and the port reproduces it.

## The port

`--native-ride-queues` (branch `tinyclaw/native-ride-queues`): `core/TPW.PS2.Data/NativeRideQueue.cs`,
seams in ParkVisitors (`Queueing` intent, `NativeQueueMouth`, `NativeQueueArrival`, `AssignQueueRoute`,
`BoardFromQueue`, `ReleaseFromQueue`), and `game/Viewer.RideQueue.cs`, which reads each ride's queue
off the path tool: connection A, then the stub, then along the drawn run links to the `Both` cell.

| | Native | Port under the flag |
|---|---|---|
| body | standing at the spot | the same: the guest stays on the walk, leased to the queue |
| capacity | 7 + 4×tier, and the physical length | the same; tier is 0 (the port has no tiers) |
| spacing | 117340 | the same function |
| waiting | 210428 | the same arithmetic and draw order; phase = guest id (adapter) |
| close-up | 3 × cumulative rand(3), waiting guests only | the same |
| offer | waiting head, VAR_ONRIDE < VAR_CAPACITY | the same; the head then joins the script's queue for LETMEON |
| breakdown | event 7 (with the ordinary +94 exception) | event 7; the exception is not modelled |
| closing | nothing | nothing |
| demolition | event 10, state 0 where they stand | the same, after a walk to the guest's own cell centre (hand-back needs one) |

Other adapters: routes inside the queue follow the queue cells rather than the 0x10/0x11 planner;
effect 0x7E, the sound and 2E28D0 are not ported; walking speed is 15 + (id·7 mod 15); readiness
gates queue walking only under `--native-idle-all`.

Checks: `tools/TPW.PS2.ParkSimAudit/NativeRideQueueChecks.cs` (hand-computed spots and waiting
arithmetic; a walked fixture on the audit's vetted ride covering the head count, tier 1, the physical
limit, the impatience ripple, the boarding gate, breakdown, closure and demolition). Five mutations
each turn it red. The rendered smoke is `game/tests/NativeRideQueueSmoke.tscn`.

## Default play cannot reach a queue longer than one tile

A defect in the LEGACY path, found while answering "what is the benefit" (2026-09-25).
- `GuestWalk.Edge` admits a cell only when it is `ParkPaths.Open` (a path material) or when it is
  the route's own destination.
- Queue tiles are `ParkPathKind.Queue`, so a guest can step onto the first queue tile and cannot
  walk through one.
- Legacy `SendTo` targets `ride.Entrance`, the stub at the FAR end of the queue from the path.

**Probe** (JUNGLE-1, three path cells then three queue cells in a row): path → the queue tile next
to the path routes; path → the tile three in is `NoRoute`, "no route". So in default play a ride
whose drawn queue is longer than one tile never gets a visitor.

Under `--native-ride-queues` the guest routes only to the mouth (the `Both` cell, path material), and
the queue controller walks it along the queue cells. So the flag does not have this defect. Fixing
it without the flag means letting a queue's own tiles carry the guests heading for that ride.

## Open

- 1AECCC's family; 20E0E8 (state 0x15, riding); the producer of state 0x17; 118018.
- Queue-cell list construction at ride+A0, i.e. how laid queue tiles become that list. The port
  reads the drawn run links instead.
- Who calls 116458 (it is only referenced from a vtable at 35A66C).
