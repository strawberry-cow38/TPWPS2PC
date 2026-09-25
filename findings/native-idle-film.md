# Idle animation A/B film (queue item 5)

2026-09-25, tinyclaw, on cow tools' `--idle-scene` (main 2268664). That scene is a standing crowd,
with ParkVisitors suppressed. Guests still Dawdle between stands.

## How it was filmed

`game/tests/IdleSceneFilm` is the SAME file in two builds:
- **A** is main: today's idle, a slot-2 variant chosen by `id % 6`.
- **B** is this branch with `TPW_IDLE_NATIVE=1`: the 2AAD48 dispatcher drives every guest through
  `--native-idle-all`, and, with no entrance flow, it is ticked from TickNativeBus.

Guest placement, Dawdle and the camera consume nothing the dispatcher touches, so the two runs put
the same guests on the same cells at the same ticks. Setup:
- JUNGLE-1;
- 12 guests, in two patches; the camera frames the larger one, 8 guests;
- 200 settle ticks, then 120 ticks with a frame every 4.

Frames were generated and then inspected: the guests are visible, and their poses differ between A
and B. The side-by-side clip is 4.8 s at real time.

## What B's own counters say

Over 974 standing guest-ticks, B was:

| State | Guest-ticks | Share |
|---|---:|---:|
| playing a slot-2 idle clip | 772 | 79% |
| holding the last pose (F) | 147 | 15% |
| other sections | 55 | 6% |

**That contradicts the "93% of standing guests hold their last pose" reading** in plan.md item 5, in
Viewer.cs's idle comment and in chat. 93 is the weight of the F variant **per selection**, not a share
of time:
- the six idle descriptors carry flag 4, so at every loop end 10E800 draws `rand % 400` and keeps the
  clip at >= 100, which is 3 times in 4;
- only the remaining quarter re-rolls, and 93% of those re-rolls land on F;
- F itself has flags 2 without 4, so it re-rolls with `rand % 100` at every update in this port.

⚠ **Blind spot: that cadence is an adapter.** The port runs one model update per 40 ms park tick, and
the native update cadence is unknown. A slower native re-roll out of F would raise the 15%. The
retention that makes clips dominate does not depend on the cadence.

So the visible difference between A and B is small:
- B picks its idles by weighted roll rather than by id, and sometimes plays section 6;
- guests in B now and then freeze for a moment on their last frame.

Which one ships is strawberry's decision.
