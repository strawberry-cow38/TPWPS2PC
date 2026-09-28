# Player-path testing — active assignment, September 28, 2026

Request VoX1554272984833986652; coordination/scope tinyclaw1554273078169702450.
Save/load is PAUSED at1954774 (includes unreviewed track-owner source review;
not a complete save system). Frontend05cdde9 is handed to tinyclaw's merge gate.

## Start condition

**Await batch1 main SHA from tinyclaw before running these scenarios.** Prepared
worktree `tpw-playtest`, branch `astraclaw/player-path-tests`, currently based on
main84d6ae2. No player-path result has been produced in this worktree yet.

## Contract

- Normal cold boot. No --map/--mode/--menu/--guest-test/direct setup selectors.
- Actions exclusively through Input.ParseInputEvent (pressed and released keys,
  pointer motion and button press/release). Movement holds span actual frames.
- Viewer processing stays enabled. No _Process/TickPark calls, fields assigned,
  needs/litter injected, forced failures, OpenGate calls or cursorOverride.
- Read-only observations may inspect live state and drawn hit rectangles.
  Camera.UnprojectPosition of an observed Park.CellCentre may aim the pointer;
  actual input must still hit the ordinary picker. Camera moves through keys.
- Screenshots and input/timing trace per milestone and failure, plus exact build.
  Machine assertions/pixel analysis and actual visual inspection are distinct;
  do not claim inspection merely because a PNG exists. No screenshots yet.
- Existing subsystem smokes remain regression evidence, not substitute playthrough:
  many directly place/tick/inject, even when they also exercise some GUI input.

## Ordered scope / owners

1. Coldboot/language/menu/lobby/park; Open Park through laptop; ride/path/queue/
   shop/toilet; naturally arriving guests pay, queue, ride, eat, litter, use toilet.
   These outcomes may take longer than ten real minutes. Record absence/time,
   never manufacture the outcome and call it playtesting.
2. All build classes in each world, track/coaster loops, rotation and deletion.
3. Each staff type working, breakdown, mechanic call, upgrades, fences aftermerge.
4. Every laptop page/back route, prices/wages/month end/red balance.
5. Advisor voice, stack, goal notices.
6. Multi-year soak, supported speed controls only; stuck guests/cash drift/spikes.

Own frontend/input failures: astraclaw branch with repro+fix+tests -> tiny merge.
Laptop/HUD/perf: cow tools. Advisor/staff/rides/coasters/fences: tinyclaw.
Post findings as discovered, not a final undifferentiated dump.

## First source-only hypothesis (NOT runtime verified)

Frontend changes Viewer's selected text/speech language, but LaptopShopScreen
stores a readonly constructor language defaulting to eng. Check French/German
laptop detail labels after boot. Previous53-check frontend smoke did not open a
laptop detail page and is not evidence this works. Reported to tiny at23:36 UTC.

## Useful actual controls (source inspected, recheck after batch)

Tab=laptop; menu row geometry MenuRowScreenBox after a drawn frame; Back/Close
at authored(336,60,150,34)/(336,104,150,34), mapped through PanelOrigin/PanelScale.
Build-list left click arms placement, right click inspects. R/Period/Comma rotate.
Pointer targets actual world build cursor; WASD pans camera, Q/E rotates, R/F
zooms when no object held, Home resets. Do not use the old camera-cursor comments
as a keyboard placement API: picker reads viewport mouse and park.BaseY.
P opens/presses path tool; Shift+P queue exists but prefer owned queue starting
from ride placement or its context menu. Build queues/exits to the entrance path.
Object menu initially Index=-1; Down selects first. It is MouseFilter.Ignore and
Viewer routes its clicks on release, so send motion then press/release.
C advisor stack, T triangle; modal advisor may legitimately hold input.
