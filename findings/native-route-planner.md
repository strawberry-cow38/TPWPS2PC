# Native route planner (18C4B8–18DC74): A*, read from the executable

2026-09-25, tinyclaw. Raw disassembly of the owner's PAL SLES_500.32, with addresses beside every
claim. **No port implementation exists.** The entrance experiment uses a public BFS adapter, and the
planner is deferred behind triggers (plan.md section 6). This file is the "findings first"
deliverable that plan requires, and it provides the inputs for the corridor passability check
(queue item 9).

## Shape

| piece | address | what it does |
|---|---|---|
| admission | 18DA78 | Refuses if no free request record (10 records, stride 0xA4) or no search node (2000, stride 0x14, base 2D7070). No output slot is reserved |
| per-request init | 18C698 | Sets the request's budget word +30 to 200 and allocates the start node |
| pump | 18D7F8 | Skipped while 2D7068 != 0. Services requests, then picks further ones by task+34 == 1 |
| **service one request** | **18CF30** | See the loop below |
| **expand one node** | **18C928** | Neighbour generation, costs and open/closed bookkeeping |
| build output | 18D358 | Walks parents back from the goal and emits a slot per direction change (see native-route-slot-pool.md) |
| notify | 18CF30 | 1 = success, 2 = failure, through the owner vtable +168/+16C |

## 18CF30: one service call

1. Decrement the request budget +30. **At 0 it cleans up (18D1D0), notifies 2 and finishes**
   (18CF60–18CFA4). The budget of 200 counts service CALLS, not nodes.
2. `start = 195E28()`, `deadline = start + 100`. **195E28 is a call counter** (`lw [2E2934]; +1; sw`),
   not a clock. It has 4 callers, all inside the planner, and 2E2934 has no other writer.
3. Loop while the open list (+40) is non-empty:
   - Unlink the head node.
   - If its cell equals the goal (+14, +18), build the route (18D358) and notify 1 (success) or 2
     (builder failure).
   - Otherwise expand it (18C928). A non-zero return (search-node pool exhausted) cleans up and
     notifies 2.
   - File the node in one of four closed lists: `+54 + ((x+z)&3)*0x14`.
   - Tick the counter once per node. Continue only if the running average predicts the next node
     finishes by the deadline (18D128–18D14C), which caps a call at about 99 expansions.
4. **An empty open list with no goal reached means unreachable: notify 2** (18D160).

## 18C928: expanding a node

**Node layout** (0x14 bytes): +0 next, +4 prev, +8 parent, +C g (u16), +E h (u16), +10 x (u8),
+11 z (u8).

**The tile record is the native 8-byte runtime tile:** `14E138(x,z) = [3952EC] + (z*[3952F0] + x)*8`
(see findings/paths.md). Byte +0 is the KIND; +2 is the direction-link byte.

**Leaving direction mask `fp`.**
- 0x55, meaning all four directions, when the node's own tile satisfies 1E63C0 (kind 0 without
  property 2 via 1E6140) or 1E63B0 (kind 5).
- Otherwise it is the tile's byte +2.

**Direction order is random per expansion.**
- 1448E0(4), the guest random stream, picks a row of the table at 2E22D0: `[0,1,2,3] [3,2,1,0]
  [2,1,3,0] [1,3,0,2]`. Only 4 of the 24 possible orders exist.
- Deltas at 364700: dir0 (+1,0), dir1 (0,+1), dir2 (−1,0), dir3 (0,−1).
- Direction bits at 364720: 04, 10, 40, 01.

Neighbours outside the 14E0F8/14E108 bounds are skipped. Then the neighbour's kind is looked up.

**Request flags +2C bit 80: restricted mode.** Only kinds 2, 12 and 14 are passable, at cost 1.
There is no direction check.

**Otherwise, the jump table at 364690 over kind 0..14** (kind ≥ 15 is impassable):

| kind | arm | passable when | cost | direction check (fp & mask[dir]) |
|---:|---|---|---:|---|
| 0 | 18CB98 | flag 02 AND 1E61E0(tile) | 2 | no |
| 1 | 18CBC0 | flag 40 | 1 | no |
| 2 (path) | 18CAE8 | flag 01 | 1 | **yes** |
| 3 | n/a | never | n/a | n/a |
| 4 (queue) | 18CB04 | flag 10 | 1 | **yes** |
| 5 | 18CB3C | flag 08, OR the tile is the request's target tile +3C | 2 | no |
| 6 | n/a | never | n/a | n/a |
| 7 | 18CB60 | only the request's target tile +3C | 1 | **yes** |
| 8 | 18CBDC | always | 1 | no |
| 9, 10, 11 | n/a | never | n/a | n/a |
| 12 | 18CACC | flag 01 | 1 | no |
| 13 (entrance walkway) | 18CAE8 | flag 01 | 1 | **yes** |
| 14 | 18CBCC | flag 20 | 1 | no |

**Cost and heuristic.** `g = parent.g + cost` and `h = |x−goalx| + |z−goalz|` (Manhattan). The closed
list (hashed by (x+z)&3) and then the open list are searched for the same cell. If one is found with
`g+h ≤ new f`, the neighbour is skipped; otherwise that node is unlinked and reused. A new node is
popped from the free-index stack at 2E0CB0, with its count at 2E1C50. **An empty stack returns 1,
which means failure** (18CDBC → 18C9A4).

**Open-list insertion is sorted by f with a two-ended scan** (18CE10–18CEDC):
- Compare |f − head.f| with |f − tail.f|.
- If the head end is nearer, scan forward past nodes with f < new, and insert BEFORE the first
  node with f ≥ new. Equal-f ties go ahead of existing ones (LIFO).
- If the tail end is nearer, scan backward past nodes with new < f, and insert AFTER. Ties go
  behind existing ones (FIFO).
- So the tie order depends on where the list's f range sits. Together with the random direction
  order, this decides between equal-cost paths.

## What this means for the entrance adapter (queue item 9, first pass)

This part is inferred, not yet run against the port's tiles.

- **With uniform costs, A* with a consistent Manhattan heuristic returns a shortest path, as BFS
  does.** Under request flags 0x21 or 1, every passable kind costs 1. So on the same passable set
  the route LENGTH matches BFS, and only the choice between equal paths (random order and tie rule)
  differs.
- **The passable SET can differ from the port's BFS in three ways**, and each must be checked
  against `ParkPaths` as `RequestEntranceRoute` uses it:
  1. Kinds 2, 4 and 13 may only be LEFT in directions the current tile links (byte +2). If the
     port's BFS moves between any adjacent open cells, it can cut corners the native cannot.
  2. Queue tiles (kind 4) need flag 0x10, which none of the entrance requests (0x21, 0x23, 1) set,
     so natively an entrance guest never walks through a queue.
  3. **Flag 0x23** (the mode-14 alternate request, findings/native-rejected-departure.md) adds 02:
     open ground (kind 0, when 1E61E0 holds) becomes passable at cost 2. A native rejected guest
     whose path request fails can cross grass on the alternate. A path-only BFS cannot reproduce
     that.
- **The blocker is still the input.** The port does not build the native tile array: kinds 12, 14,
  5, 7 and 8 have no producer, and neither does the byte +2 link byte. Reproducing the planner
  faithfully needs that producer first (placement 1E2AD0, link builder 1E70F0), which is
  cross-ownership work. See plan.md section 6.
