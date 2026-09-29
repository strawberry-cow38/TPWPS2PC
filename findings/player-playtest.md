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

## First real-input runs — September29 00:35–00:44 UTC

Batch1 `72cbea4` merged into this worktree; driver `e84b737` is test-only.
`PlayerPathSession` drives normal Viewer with ParseInputEvent only; no processing
pause/manual tick/field mutation. Read-only observations inspect rendered label
lookups, hitboxes and effective visibility. Artifacts capture each milestone,
input sequence and time. Normal CLI: disc, --play-language=fre/ger, --play-out=freshdir.
The EC2 machine lacks retail OGVs, so these runs explicitly exercise missing-file
continuations, not retail movie playback (separate4080 verification exists).

Repro route: cold boot -> choose French/German -> legal -> mouse New Game/Main
Game -> lobby Enter/confirm -> park -> wait out any modal greeting -> Tab ->
localized Open Park -> Research -> Back -> Hire -> cleaner candidate -> Close.
Normal park/calendar processing remains enabled throughout.

Confirmed on72cbea4:
- Research label531 was Rides, expected Attractions(fre)/Attraktionen(ger).
  All5 research category labels were English; missing localized label propagation.
- Candidate title Leon, expected Pierrick(fre)/Freddy(ger). Pay Grade/Monthly Wage
  also English. These are multiple manifestations of two language wiring paths,
  not eight independent bugs. Owner cow tools.
- Terrain.Root.Visible=true but IsVisibleInTree=false, Park.Root.Visible=false.
  Matches strawberry's screenshot report. Our coldboot double hide lost visibility
  ownership; tinyclaw already fixed onmain6853bfc. Effective visibility is required:
  checking only terrain's own flag or _loadedMap misses this bug.
- Open Park clicked through the actual translated row changes _laptopParkOpen;
  Research, Back, Hire candidate and Close routes all operated through actual GUI.

Before artifacts/logs: /tmp/tpw-player-fre-01(.log), /tmp/tpw-player-ger-01(.log).
Each directory has12PNG andinput-observations.jsonl. Source72cbea4, test-driver-only
changes. French logged8string mismatches; German additionallyloggedterrainfalse.

Verification candidate: main6853bfc + cherry-picked language6128175 (local4cb2519).
Cow rebased that commit while our run was in progress: newID5af3e2a. Verified BOTH
have stablepatchid dad3acc82afb412b712e78d1e567548ad6413591. Tested the isolated
language patch, not cow's entire seam/benchmark branch.

Same real-input route after fixes, separate French and German runs:
- Research: Attractions / Attraktionen (andallother4labels) correct.
- Candidate: Pierrick / Freddy correct; salarylabels correct ineachlanguage.
- Terrain effectivevisibility andPark.Root.Visible nowtrue inbothruns.
-36observations,zero measured mismatches ineach run.
Logs /tmp/tpw-player-fre-fixed-01.log and /tmp/tpw-player-ger-fixed-01.log,
12screenshots+inputtrace inmatchingdirectories. Label lookup is the actual pure
lookup used by the draw; this is not a screenshot OCR claim. Screenshots saved
and posted for visualreview; localimage-viewing tool unavailable, so visual review
must remain separately attributed/pending. No building/service-use/soak claim.

Next: actual building/connectivity/guestuse. Before that, repeat-entry/hide ownership
regression should be checked without conflating pure-input testing with synthetic
intentional-hidden-node controls (the latter needs a separately labelled fixture).

## Building transport correction and first connected ride

The initial driver extension failed on its own setup, NOT a gameplay regression:
ParseInputEvent(mouse-motion) left GetMousePosition at(576,324) despite event
position(599.37,80.16). Actual window cursor movement with Viewport.WarpMouse
then made the observed position(599,80). World picker reads this position whereas
GUI click handling reads event.Position. Driver now logs both and moves only the
isolated test window's real pointer; never cursorOverride/game-field mutation.
The initial entrance-mouth cell(29,18) is legitimately no-build. A player can
start at the existing playable path at(29,19); the next run uses that observed
cell. These are driver corrections, not reported game bugs.

Run `/tmp/tpw-player-build-03.log`, artifacts same stem directory:
ordinary input coldboot/park/Open Park; extend spine atx29 toz29; mouse catalogue
Build/Rides/Crazy Ape; valid placement cursor(27,24), actualcorner(25,22),turn0;
owned entrance queue(26,21)->(29,21), exitpath(27,26)->(29,26). Actual charges:
7newpath tiles$70, Crazy Ape$2000,3queue tiles$75,1exitpath tile$10; balance$27845.
No placement/tick/needs/cursor fixture injection.70observations; zero measured
mismatches in preceding language/visibility checks. This does NOT mean all guest
behaviors passed: firstpostbuildbus requested/admitted1 visitor; that visitor was
rejected by entrance and advisor submitted ticket-too-expensive message0x49.
No customers/takings yet. One refusal is not evidence allguests cannotenter or
that pricing is buggy. Continue the ordinary player journey with more services.
Simulation time advanced slower than wall time on llvmpipe; record both.

The `--play-interactive` test-scene-only option now retains the live world after
setup and accepts bounded JSONL key/click/menu/aim/shot/wait/read-only-state
commands in its artifact directory. No gameplay handler calls or state writes.
45min real-time stop guard prevents an orphaned renderer. This is NOT a shipping
Viewer command surface. Do not rebuild while that session is running.

Peer visual review: tiny1554292777158840354 and cow1554292972261220473 examined
all3afterfixcaptures: park terrain/path/road/pool visible; French Research and
German candidate fullylocalized includingBack/Close. French longlabel overlap
is a separate fidelity/layout question for cow, not adjudicated by these tests.
