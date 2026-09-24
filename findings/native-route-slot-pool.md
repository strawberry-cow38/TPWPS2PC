# Native route-slot pool — resource producer for the next integration

September24,2026; owner PAL ELF read in RAM, no extraction. This is a positive
raw-instruction trace, not a ported/shared allocator yet. The experimental route
service still has unlimited direct slots and managed waypoint arrays.

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
