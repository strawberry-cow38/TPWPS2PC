# Busy-park soak (queue item 10)

2026-09-25, tinyclaw. Research branch, `--experimental-native-entrance`. Rendered normal-startup scene
`game/tests/NativeEntranceSoak`, run in all 8 parks by `tools/viewer_matrix.py` (scene `soak`).

## What it drives

In ordinary play the first buses bring one guest each: the demand bound is `(score+offset)*0x1333/divisor`,
1 for a one-ride park. So the incoming groups never fill, and neither the group flip nor the 30-guest
veto can happen. The soak therefore uses three explicit fixture inputs:

1. **LoadsOfKids.** This is the executable's own batch rule, `min(20, 20 - W)`, ignoring demand and
   headroom, as `NativeBusDemand.Batch` records it. The Viewer takes it only from a fixture field,
   `_nativeLoadsOfKids`; it has no command-line switch and is never a default.
2. **The admission off-switch** (`_guestCap = 0`) after 3000 park ticks.
3. **Cash dropped to 50** for every guest the entrance has handed over, from then on. This is
   20C930's cash < 100 arm, so it produces a mass exodus through state 26.

The fee is the ordinary constructor 150, not a fixture price.

## What it checks

- Every 100 ticks, **conservation**: births = WentHome + discarded + live, and one actor per live
  guest.
- After every update: no flow entry is ever Stopped.
- **The ONE group flip happened.** A mode-16 completion found its random group already at 11 and went
  to the other one (`NativeEntranceFlow.GroupFlips`).
- **The 30-guest veto happened.** Sticky A4 pressure reached 30, and 14C2EC's test refused a batch
  while the park was open (`NativeBusController.PressureVetoes`).
- **Drain.** At quiescence there are no live guests, flow entries, incoming memberships or staging
  counts. All 1000 route slots are free. WentHome equals births, and nothing was discarded.
- The runner also fails any ERROR line in the log.

## Results

JUNGLE-1, first run:

| Measure | Value |
|---|---|
| ticks | 3829 |
| births | 100 |
| wentHome | 100 |
| peak live | 70 |
| peak group | 12 |
| group flips | 5 |
| peak pressure | 56 |
| vetoes | 1 |
| made broke | 52 |
| conservation checks | 39 |

All-park numbers are in the viewer-matrix manifest cited by progress.md.

The counters are instrumentation. Nothing reads them to decide, and each check prints the count
beside its verdict.
