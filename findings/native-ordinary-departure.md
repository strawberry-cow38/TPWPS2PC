# Ordinary departures through state 26 (queue item 7)

2026-09-25, tinyclaw. Raw disassembly of the owner's PAL SLES_500.32 through `tools/r5900dis.py`, with
addresses beside every claim. Research branch only, behind `--experimental-native-entrance`.

## What the executable does: 20C930, the state-0 decision

`a0 = N`, the allocation base, so the execution state is `N+37`, written as `(N+8)+0x2F`.

**The departure predicate (20C950..20C9B4).** The guest leaves when any one of these holds, tested in
this order:

| Test | Address | Port |
|---|---|---|
| `lb N+7B` (boredom) >= 99: leaves unconditionally | 20C950..58 | `VisitorWants.Boredom >= 99` |
| `lb N+75` (happiness) < 5 | 20C960..68 | `Happiness < 5` |
| `lw N+60` (cash, a word) < 100 | 20C97C..84 | `Cash < 100` |
| `211D48() >= 81` AND `rand(20) < 2` | 20C98C..AC | **not ported**; 211D48 is unread |

`VisitorNeeds.WantsToGoHome` is exactly the first three. The fourth arm is random and unported.

**On departure (20C9C0..20CA70).**
- When happiness is below 3, 192D50(B) is consulted. If it returns zero and the global 2E28D0 is under
  25, 2E28D0 is incremented and `B+2C |= 8`.
- Still only below happiness 3, `N+40 = 10` (the thought id) and 111428 is called with event 7, id
  0x133 and the guest's x/z. **This thought and effect are not ported.**
- Then, for every departure, **`N+37 = 0x26` and `N+1C = 0`** (the state stack depth) at 20CA68..70.

**The function does not return there.** It reads the clock (1C4930), draws `rand(6)` and `rand(300)`,
and dispatches the six-arm table at 36C940 (20CA94..20CABC) with the departure state already written:

| Arm | Entry | Effect on a guest just set to 26 |
|---|---|---|
| 0 | 20CAC4 | If `now > N+2C + 60 + rand300` (or N+28 is set): facility selection 153410..20F888; on success it **pushes** N+37 (26) to `N+20+[N+1C]`, increments N+1C and writes state 6 (20CBD8..FC). Otherwise it returns with 26 intact |
| 1 | 20CC00 | `N+38 = (N+38 & ~1F) | 0D`, **pushes** 26 and writes state 1 (20CC00..38). The findings in native-shop-flow.md record that state 1 (1920B8) clears the stack and sets 5 |
| 2 | 20CC3C | Nearby-object interaction with its own probability and distance tests (not read here) |
| 3 | 20CEDC | Litter `N+74 >= 90` calls 20D010(N, 0) (unread) |
| 4 | 20CF00 | Sickness `N+76 >= 93` and `rand(4) == 0`: **clears the stack** (N+1C = 0) and writes **state 0x1D** with its own `now + 15` deadline |
| 5 | 20CFB4 | Happiness below [2EEB98] and `rand(1000) < [2EEB68]` calls 20D010(N, 1) (unread) |

So a native leaver is not guaranteed to reach 210D70 on its next update. Arm 0 or 1 can park 26 on the
stack behind a detour, and arm 4 can discard it. Because the predicate is re-tested at the next state-0
decision, a detour usually delays departure rather than cancelling it. This has not been measured.

## What the port now does (opt-in)

- **ParkVisitors** gained one member, `NativeDeparture` (a `Func<Guest, bool>`, null by default). In
  `Idle`, a guest who `WantsToGoHome` is offered to it before the legacy walk to the gate. True means
  the native owner took the lease. False leaves the legacy walk untouched. ParkVisitors is cow's file:
  this seam needs cow's sign-off before main.
- **NativeEntranceFlow.TryDepart**:
  - Remembers each admitted guest's birth serial and baseline speed at the mode-13 handoff.
  - Re-takes the guest's lease, marks it Leaving, and enters **state 0x26**.
  - From there it runs the path the booth rejection already used: the 210D70 phase gate
    `(serial & 63) == (tick & 63)`, the sticky A4 latch and its pressure count, mode 14 with flags
    0x21, staging, mode 16, mode 9 to point0, and retirement through `CompleteNativeDeparture`, which
    counts WentHome.
  - A guest this flow did not admit has no birth serial to gate on. It returns false and that guest
    keeps the legacy gate walk.
- **The Viewer** wires the seam while the experiment is on, clears it on reset, and keeps a bounded log
  of route requests with their flow tick. The flow itself never reads that log; it is research
  instrumentation.

**Labelled adapters** (also printed at startup):
- Departure enters 26 at once; the six arms after the 26 write are not joined.
- The 211D48 random arm is not ported.
- The happiness < 3 thought 10 and effect 0x133 are not ported.
- Guests not admitted by this flow leave by the legacy gate.
- The activation serial is the represented-activation adapter (plan section 6, won't-fix).

## Evidence

`game/tests/NativeOrdinaryDepartureSmoke` is a rendered normal-startup smoke with an explicit fee-10
admission fixture.
- It follows 8 real bus-born guests from birth.
- Once all 8 are past the booth, every guest still in the park and not already leaving gets its cash
  set to 50. That is the fixture's only input; everything after it is production.
- Checks: every admitted leaver re-enters the flow at state 26 with its birth serial, as Leaving.
  Every mode-14 request lands on its guest's phase. Deferral latches A4 and counts in pressure. Leavers
  stage outgoing. Every followed guest retires at point0, never at the gate, with actor, plan and
  needs removed together. WentHome equals the cohort, nothing is discarded, and the pool returns
  to 1000.
- First JUNGLE-1 pass: 46 checks. followed=8, admittedLeavers=8 (5 left on their own before the
  fixture, 3 were made broke), 8 distinct phases, 8 mode-14 requests, peak pressure 3.
- The 5 natural departures happened while later buses were still arriving. Which predicate fired was
  not instrumented.
