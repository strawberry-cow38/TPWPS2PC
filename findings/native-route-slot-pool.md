# Native route-slot pool — producer and actual cursor/entrance consumer

September24,2026; owner PAL ELF read in RAM, no extraction. This is a positive
raw-instruction trace. The research branch now connects NativeRoutePool through
GuestWalk, NativeGuestRoute and the experimental incoming controller: actual
1000-slot output allocation/retirement/retry, not a counter beside arrays. Native
search-node/request resources, global disable/reinit and other walker families
remain separate gaps; see the implementation checkpoint below.

## Capacity is1000, NOT the11-bit link range

`192768` loads allocation hint[37E118] and compares it using literal signed
SLTI0x3E8 at192770. The table starts3AE1B8, stride4. The lower11 bits of the first
halfword are a next-slot link;0x7FF is the TERMINAL SENTINEL, not a capacity.
The high bit0x8000 is allocated. Coordinate packing is already decoded separately.

Allocation scans forward from hint while index<1000, skips allocated records,
sets bit15 on the first free record, sets hint=index+1, decrements available count
[2E2924], and replaces only the low11 link bits with0x7FF. Bits11..14 and the
coordinate bytes survive; this does not zero a slot. Returns index, or-1 if the
forward scan reaches1000. No wrap-around scan is performed on failure.

`1927F8(index)` frees one slot: clears bit15, increments available count, sets
hint=min(oldHint,index). It does not clear link/coordinate fields and does not
validate that the slot was allocated first. A managed double-free/range guard
would be an explicit safety policy, not a native branch present in this body.

`192840(head)` frees the whole chain. Input-1 is a no-op. It reads and saves each
slot's low11 successor BEFORE calling1927F8. Successor0x7FF maps to-1. There is no
cycle guard in the checked body. The underlying free-count word has file value
1000; there is also a real initializer, not merely a data-based assumption.

## Initialization/reset

`192728` walks exactly1000 records (counter starts999 at19272C, decremented before
BGEZ at192748). It clears ONLYbit15, then resets hint0 at19275C and available1000
in its return delay slot192764. Existing packed coordinates/links remain.

The fixed xref sweep independently finds the hint writer19275C and the free-count
writer192764; all addresses were then inspected in raw MIPS including delay slots.
The hint is BSS, whereas free count2E2924 is file-backed. Reset CALLER/lifecycle and
all users competing for this shared pool are not closed by these local routines.
Positive table users include14196C,18D494,191894,191DAC,191EEC,210F98,211058,2111CC.

## Required next consumer work

Join actual shared allocation/release to asynchronous route results, direct
mode12/16/13 assignments, current-slot retirement and cancelled/removed routes.
Do not implement a disconnected1000 counter beside the existing unbounded arrays.
Read planner partial-allocation failure/cleanup and reset timing before claiming
capacity parity. Failed direct allocation must retain the correct retry state;
mode13 can continue its50-cell scan after a failed candidate in the same native
update. Current experimental adapter never fails direct allocation and therefore
has not proved that latter resource-pressure behavior.

## Output builder, retirement and reset lifecycle follow-up

Read-only follow-up checked raw addresses and the authorized partial C; no new
pool implementation is claimed. Distinguish THREE resources:
- output slots:1000, base3AE1B8, available2E2924;
- search nodes:2000, base2D7070 stride0x14, free-index stack2E0CB0,
  available unsigned halfword2E1C50;
- request records:10, stride0xA4, service2E1C58 free+4/active+8/count+C.
Word2E28D0 is another actor-flag count, not output-slot availability.

### Builder18D358 and partial rollback

Owner is request+8 (route subobject); signed-halfword owner+28 is output head.
An old route is disposed FIRST (18D3D8->192840;18D3E0 stores-1); replacement
failure does not keep that old route. Builder follows search-node parent+8,
coordinates bytes+10/+11. 18D418..74 compares consecutive directions and skips
allocation for unchanged direction, so it is NOT one output slot per search cell.
First emitted slot uses exact requested endpoint through192560 (18D4A4..B4);
later turn nodes use cell centres (18D530..48). Walking the parents backward,
it prepends each new slot to the private partial head (18D54C..70), producing
forward actor-consumable order.

Allocation failure branches from18D488 to18D574->192840(partialHead). It returns
the detached goal search node to its separate pool at18D57C..5A0 and returns0.
No partial head is published. Available output count is restored by ordinary
free-one operations; allocation hint is not restored from a saved snapshot.
Success returns the detached search node at18D608..48, publishes head at18D650,
returns1. 18CF30 dispatches notification1 on success,2 on builder failure, then
releases remaining search lists via18D1D0. Concrete virtual callback receiver
still needs joining; these are observed notification codes, not a full event API.

### Request admission, search budget and cancellation

18DA78 refuses if no free request record (18DAAC) or no search node (18DAC4..D4).
It does NOT reserve an output slot at admission. Accepted records move to active,
active count increments;18C698 initializes request+30 to200 (18C6B8/18C6E0) and
allocates its starting search node. 18CF30 tests/decrements that budget once per
service invocation; zero cleans searches, notifies2 and finishes. It is not an
output-slot count or a demonstrated time duration.

18D7F8 skips work while2D7068!=0. Finished requests decrement active count and
return to free list. Its timer195E28()+0x64 units remain unknown (0x64=100). **[2026-09-25: 195E28 is a CALL COUNTER, not a clock: `lw [2E2934]; +1; sw; return old`. Its only 4 callers are inside the planner (18CFF4, 18D128, 18D824, 18D8CC), and 2E2934 has no other writer. In 18CF30 the deadline is start+100 and the counter ticks once per expanded node, so each service call is capped at about 99 node expansions, deterministically.]**
18D1D0 cleans request+40 and four search lists starting+54, returning search-node
indices; it does not change output availability or free the actor's output chain.

18DB70(owner) saves next and cancels every matching request+8:18D698->18D1D0,
active-count decrement, recycle. It does not notify or free completed output in
the inspected bodies.18D7A0 separately cleans searches and sends notification3.
No direct J/JAL callers of those two routines were found in the bounded text
scan; this is NOT proof of absence of indirect use or established actor-removal
reachability. Cancellation caller/virtual dispatch remains an integration gap.

### One-slot retirement versus whole-route invalidation

191D78 loads current index, SAVES low11 successor, maps7FF to-1 in the call delay
slot191DCC, calls1927F8(current) at191DC8, then publishes saved successor191DD4
and state3. 20D628 protects this call with current!=-1 before20DD50.
Normal movement191E98 arriving exactly at192064..94 merely sets state2; it does
NOT free. Invalid coordinates instead call192840(currentHead) at192038, then
clear head/state at192040..50. Do not replace these two different frees with one
'route done' operation.1925A8/1928B0/1928F8 also have explicit chain disposal,
but their complete caller coverage is not established here.

### Reset is not just a one-time park constructor

Direct reset callers are149C08 inside149958 and18C4C0 inside18C4B8.
Initialization:151800 globalstate2 ->151498 ->149958 ->192728, then advance to3.
A SECOND running-state lifecycle is151268(state3), conditional object3953CC
halfword+4==8 ->1526D0 ->18C4B8 ->192728. It rebuilds search stack and10 requests,
clears actor flags and re-enables service2D7068=0. Paired152668->18C340 disables
service after actor cleanup/flagging; one concrete cleanup211DF0 frees chain at
211F98 and clears head211FA8. The precise gameplay name of this lifecycle is
unresolved. Therefore not per-request/per-tick, but also not safely 'once per park'.
The normal update151928->18D7F8 itself contains no reset.

Direct-call scan controls included18D47C->192768,191DC8/192884->1927F8,
18D574->192840 and both reset callers. Allocator positives include141928,18D47C,
191538,1918A8,191CD8,20F344,210F70,2110C4,211188: multiple competing users.
Negatives concern aligned direct J/JAL in text[100000,2A420C), not all indirect
calls or separate table mutation. Next code must connect shared output ownership
to the actual cursor/request consumer, with these partial-failure boundaries.


## 18D358 output selection: do not symmetrize the root

Processing starts at the detached goal, previous direction0xFFFF, and follows
parent+8 through the ROOT inclusive. Nonroot direction uses parent minus current
unsigned-byte cells: dx!=0 -> dx>0?6:2; otherwise dz>0?0:4. Root direction is0.
Equal direction skips emission; different direction allocates/prepends. The first
emission uses exact requested endpoint through192560; later emissions use current
node centre. Root is skipped ONLY if previous direction is already0. A singleton
search chain emits one endpoint, not zero and not an extra root centre. Zero-length
nonroot edges arecode4; diagonals use dx sign, not a diagonal/collinearity test.

Positive controls18D420/474/478,18D5A4..604: loop ends after root, not before it.
NativeRouteOutput.FromCells applies this selection to the existing BFS result.
That preserves the observed output conversion but does NOT establish native
search/parent-choice parity. Empty successful search output is refused as managed
safety; a deliberate empty owner lease while waiting is a different operation.

## Research implementation checkpoint

NativeRoutePool stores the REAL packed output words, allocation bit, links and
coordinates. GuestWalk owns one pool for its native routes; NativeGuestRoute
adopts its exclusively owned head, reads targets there, and holds no substitute
waypoint-array cursor. Isolated cursor tests use a private pool explicitly.
SlotIndex is now an actual handle, not a local array ordinal. Reverse-prepend
allocation means the first waypoint of a fresh two-point route useshandle1 then0;
eleven old combined cursor assertions were corrected for that representation
change without weakening position/phase assertions.

Replacement disposes old output before building new; failure rolls back only
private new output, retains the same guest/owner/coordinate and reports Exhausted
separately from an ownership refusal. Request consumers restore their retry state
on exhaustion. Direct allocation takes the slot BEFORE invoking its target
factory: mode16's1532D8 RNG must not be consumed when allocation fails. Direct
mode13 candidate enumeration and allocation interleave in the SAME tick, so a
failed first candidate does not prematurely end the native scan.

Arrival retains a live slot; the later retirement saves next and frees ONE.
Bounds failure, replacement, removal and explicit teardown free the whole owned
remainder. Clear frees actor-owned chains before reset. Public malformed-link,
double-free, cycle and stale-reset checks are labeled managed safety, not native
branches. TryBuild's private prepend is linear; it does not repeat the public
SetNext whole-tail safety check for each newly allocated slot.

NativeRoutePoolChecks has1082 checks, including all1000 allocations and actual
GuestWalk/entrance resource exhaustion, retry, owner isolation, direct helper
ordering and same-tick candidate iteration. Actual Viewer smoke now asserts its
real guest consumes and returns the shared pool (651 checks in JUNGLE).
Six mutations were caught: corrupt free count34failures; omit partial rollback
8failures then safety guard; omit retirement free9; symmetrize root8; eager direct
helper5; eagerly materialize exit candidates2 then guard. The batch command timed
out during its final restored baseline, NOT as a passing test; source restoration
was checked and a separate restored run passed all1082 afterward.

This removes the unlimited OUTPUT-slot adapter, not all resource placeholders.
BFS/search budget/2000-node ownership/10 pending native records/readiness and the
normal/rejected departure producer remain unported. Only currently native-owned
routes share this pool; unported guards/legacy ordinary walking are not silently
counted as if they had native slots. Do not announce full native pathfinder parity.
