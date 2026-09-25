# PS2 bus entrance-group population inputs — four-minute partial

**Later causal trace:** see `native-entrance-lifecycle.md` and `native-guest-motion.md`.
In particular, N+28 is reused across staging and list selection: both counted
queues are incoming. Mode14's early value1 is NOT departure-list membership.
The literal11 belongs to global park entrance counts, not attraction capacity.
The older bounded-unread list below is historical; the newer notes delimit what
has since been read and what still prevents a faithful runtime join.

Read bus-native-arrivals.md and bus-native-demand.md first (no root demand.md
exists). Primary owner-disc ELF memory only, LBA262773, Mode2/2352+24,
size 0x2B1160, file 0x1000 -> VA 0x100000. Used selector reader; no
binary/assets extraction, gameplay edits, or commits. Addresses below are VAs.
Labels are descriptive, not recovered symbols.

## Positive result: W counts two actual intrusive guest lists

1527B0(i) returns word[3953D0+4*i]. The producers are not inferred from
nearby geometry:

* **1527C8(group, guest allocation N, output point)** registers N+8 in the
  intrusive list whose sentinel is **2B7370 + 20*group**. It walks sentinel+0
  then each node+0, retaining the previous node. If N+8 is already present,
  returns 1 without insertion or count increment (1528F0). Otherwise it
  unlinks N+8 from its existing embedded list via next/previous pointers,
  appends after the last node (or sentinel), and increments
  **[3953D0+4*group] at1529F0**, returning 1. Links are at embedded O+0/+4;
  these are separate from allocation N+0/+4 guest-pool links.
* **152A18** processes exactly group 0 and 1 (152B7C). On updates where
  `(1C4930() & 31) == group`, it examines the head. If head's virtual
  state getter returns **0x2C**, it clears N+1C, sets **N+37=0x25**, decrements
  that group's count at152AC8, unlinks head, and calls **14DA60(N+8)** to
  put it back on the active list rooted at395208. At most one head per
  selected group is released in this pass.
* State getter is positively resolved: guest vtable36CCE0 + D8 adjustment
  is 0, +DC target **192DC8**, which returns byte[O+2F] = byte[N+37].
  No inferred class/slot name is needed for the 0x2C test.
* After optional release, 152A18 updates the remaining list nodes through
  virtual +3C. If a head was released, remaining nodes additionally receive
  a virtual +16C call with a stack descriptor containing literal **0x2B**
  (152B3C..58). Descriptor semantics remain unread.

Thus **W = list-membership count(group0) + list-membership count(group1)**,
not total guests, bus passengers, number of occupied path cells, or number
of guests whose destination happens to be a gate. Registration occurs
before arrival at the assigned point: state/movement evidence below.
Neither decrement on arbitrary guest removal nor cross-group migration is
proved here; do not treat the observed setter scan as exhaustive indirect
write coverage. Registration itself does not decrement another group's count.

Defaults: both counts are BSS loader-zero and explicitly zeroed at149C1C/20
and1516F4/F8. Both 20-byte sentinel records at2B7370/2B7384 have zero ELF
contents;1516E8/F0 and151700/08 explicitly clear their next/previous words.
1527B0 and1527C8 themselves have no selector-range check in the read body;
consumer152A18 proves two groups, not validity of arbitrary caller indices.

## Membership selector, state, and cell path

All four positively found direct callers of1527C8 pass **signed halfword
N+28** as group, not an x coordinate or a computed nearest entrance lane:
20DB94,20DBD8,210E9C,210F40. Concrete setters **20DC2C writes0** and
**20DC50 writes1** to N+28, both setting N+37=0x2E and calling153298.
The dispatch table36C9C0 indexed by N+36 selects20DC28 for value15 and
20DC44 for value14. Earlier causes selecting those modes are unread.

210E80 registers, sets bit **0x4 in u16[N+34]**, writes N+36=0x0B, and
calls18DA78 with current x/z N+24/+26 and returned point x/z. On success
it sets N+7C=15, samples1C4930 into N+2C and sets N+37=0x0B.
210F20 registers, releases the old slot through192840(s16[N+30]), writes
N+30=-1, sets the same flag0x4, sets N+36=0x0C, acquires a slot via192768,
and writes the output point through192560 into the slot table3AE1B8.
It ORs0x7FF into that slot's first halfword and sets N+37=3. Exact slot
semantics and failures beyond these direct instructions remain unread.

The N+36 dispatch values11 and12 lead respectively to20DB8C and20DBD0:
both re-call registration and compare N+24/+26 with returned point x/z.
When equal they set **N+37=0x2C**; otherwise set0x2A or0x2B respectively.
This is a positive link between registered membership, target progress,
and the head-state release test. It is not proof these lists mean
"entering" versus "leaving", nor that N+34 bit4 alone determines membership.

Point production in1527C8 uses table
`T=2B71B0+[3952E4]*0x36+[3952E8]*0x12`, bytesT+4/T+5:
x=byte(T+4)*256+128, z=byte(T+5)*256+128, middle halfword from149D90.
For each earlier list node before the caller (or before append), z subtracts
**0x40**. This gives list-order spacing, not native grid-cell lane IDs.
Important literal control flow: nonzero group first computes x from
(byte(T+4)+1), but then falls through152868 and **overwrites the point with
the same base x calculation used for group0**. Do not invent a one-tile
lane separation from the earlier branch; the stored output is overwritten.
The table entry here is +4/+5, distinct from bus spawn point0 at+0/+1.

## Easy nearby +A4 refinement and scheduler ordering

Existing demand note's +A4 setter is more constrained by210D70..E7C:
14E288 returns1, so the normal read path takes the nonzero branch. It first
compares `(N+14 & 63)` with `(1C4930() & 63)`. A mismatch **also sets
N+A4=1**, without trying18DA78. On matching phase it obtains1532D8(1),
sets N+36=14, and tries18DA78; failure sets+A4=1, success sets N+37=11
and does not clear+A4 in this body. Activation20BCF4 clears+A4 (prior note).
The other branch containing14B368/pool return exists but is not taken with
the currently proved constant14E288=1. Therefore do not translate+A4 into
"currently pathfinding failed" or a clean current-state Boolean.

151954 calls152A18 **before**15195C calls14BE60;151960 clears2EEB9C in
that call's delay slot. Consequently group-only guest updates just performed
by152A18 cannot leave contributions in2EEB9C for the bus gate: those are
cleared immediately afterward.14BE60 contains active-list virtual updates
at14BF7C before its bus-controller segment14C22C. Full per-guest update
coverage/duplicate prevention remains unread; no complete count-equivalence
claim follows. Prior note establishes211724..734 increments2EEB9C iff+A4!=0.

## Port integration boundary

ParkVisitors already owns ID-keyed plans, ride-instance owners, service and
returning guests; a guest need not exist in Walk while still owned. Reuse
that lifetime identity for a future explicit entrance-group membership
collection, with idempotent registration and counted removal. **Do not use
Walk guest count, entrance-cell count, nearest-lane geometry, or WentHome**
as W. This trace establishes what the missing membership lifecycle must
represent; current managed plans/owners are not demonstrated to implement
N+28 and these two embedded-list transfers already. Keep the input explicit
until that lifecycle is implemented, rather than synthesizing groups from
shape. Total active pool ownership remains a distinct H input (parent task).

Bounded unread: upstream N+36 mode14/15 selectors; full153298/1532D8;
arbitrary removals/reset/save restoration and cross-group behavior; full
+A4 lifetime/reset and update coverage; gameplay names of both groups;
slot/cell helpers192768/192840/192560 beyond shown calls. Immediate-reference
search had positive controls149C18,1527B8 and found registration/removal
writers, but is not a full-image absence proof. Critical R5900 MULTs in
index arithmetic write rd as well as LO; conditional MOVN decoded by helper.
