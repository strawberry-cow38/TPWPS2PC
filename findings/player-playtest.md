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

## Interactive continuation / newly prioritized map-switch cleanup

Owner1554298445483745381 explicitly deprioritized seams and requested particle
emitters surviving map changes. Do NOT treat the dark edge in the Crazy Ape crop
as that seam: owner clarified the actual seam is a light-blue1–2pixel hole tosky.
No seam reproduction/verification claim survives that clarification.

Live session `/tmp/tpw-player-live-01.log` ran about27min wall time on isolated
Xvfb114, then exited through the broker's quit command (no live engine remains).
All79commands and observations in its input-observations.jsonl/commands.jsonl.
Source3c61a4f, gameplaybase6853bfc+isolatedlanguagefix; no particle/open-reset fix.
No synthetic guests, forced needs, manual ticks or state assignments.

Additional actual UI actions/outcomes:
- Build/Shops/Burger Shop placed atcorner(31,26),cursor(32,27),door(32,25).
  Paid$300; connected actual path from(29,25) to(32,25),observed$5charge.
- Build/Features/Small Toilet (real wheel scrolling to row26) placed at(32,23),
  door(32,22); connectedfrom(29,22) to(32,22). Paid$100.
- Burger Shop reached5payingcustomers/Takings150(port tenths) naturally before
  leaving JUNGLE. Severalguests admitted/walking. No inspected eating/litter/
  toiletcompletion/rideboarding claim; ride Customers may not be the right
  boarding counter, so add read-only seated/queue observations before concluding.

New repeated-park-state bug reproduced through UI:
Open JUNGLE -> Close Park -> lobby Right -> FANTASY1. Freshmap has$30000, no
placements, but no Open Park row: open state carried over. Report1554300832328065077;
cow confirmedmissingreset and supplied19e61f8, resetting to _parkOpenAtStart so
explicit --laptop-park-open remains valid. NOT yet verified by this branch.

Particle case must have a real continuous source, not Crazy Ape timedEVENTs:
read-only source/disc worker confirmed FANTASYDrinks Shop row993, cost750,
normal Create script WAITANIM0;ADDOBJ1,1,58,1;LOOPANIM5 -> Bubbles. Whileopen this
stayscontinuous; manualcloseexecutesKILLOBJ1, so do NOT close/delete beforeexit.
AlternativeJUNGLEHugeHollowRock row765 emitsSteam17 twice afterCreate but research
availability notproved. Nothingextracted; workerfinished,nonepending.

Actualparticle setup in same live session:
- FANTASYBuild/Shops/DrinksShop. Cursor(42,24) refused (no placement); tried
  knownplayable(38,32), placedcorner(37,31) normally.
- Runtimeconfirmed '[fx] Bubbles: CONTINUOUS 12 alive' and
  '143.2s Drinks Shop ADDOBJ kind1 node1 id58 -> Bubbles at(38.0,1.1,-32.0)'.
- Shot34-fantasy-bubbles-active-before-exit.png AFTER the ADDOBJ (shot33 was
  beforeCreatefinished; do not use it as active-emitter evidence).
- ClosePark -> lobby Left(FANTASY4->HALLOW2),Left(no neighbour),Up(JUNGLE2record1),
  Enter/Enter -> JUNGLEterrain2. Newemptypark; aimcell38,32; shots38/39 atoldlocation,
  secondafter10simulationseconds (longerthan1.86s particle life).
- Shot39 projectedgroundpoint~(772,54) in1152x648. Filesposted to cow for visual
  review; visualpersistence NOT yet confirmed. Read-only liveemitter counts were
  not in this version ofbroker; add them to focused replay.

Source mechanism matches cowb6a2283: RideParticles.Clear freesonly_live;
_continuous values/keys persist. Unitfix+mutationtestedbycow, separatefromour
naturalCreate/UIroute. Next run focused normalbootFANTASY/Drinks/map-switch,
logging _continuous count+instanceIDs/emitting/effectivevisibility/position before
andafter. Assert precondition live>=1. Run before/afterb6a2283 and19e61f8; don't
silently treat savedPNGs as visualproof or use no-emitter fixture ascoverage.
Do not restart full10minsetup justfor this: nowknown directinputroute supports
short focused replay. Keep all gameplay actions on real input path.

## Focused reset replay — both fixes independently verified (September29)

Test41207ad adds `--play-tour=world-reset --play-expect-clean` to the driver.
Normal coldboot -> FANTASY1 via lobbyRight -> actual OpenPark click -> Build/Shops/
DrinksShop -> ordinary placementcursor38,32. Wait for _continuous to contain an
actually emitting/effectivelyvisible node BEFORE switching. No injected emitter.
Read-only snapshots record instanceIDs, Emitting, effectivevisibility, position,
rootchildcount. Source-node wrapper references are retained only as observations
so we can test whether their Godot instances survive; they do not own/keep the
native nodes alive. LobbyLeft/Up selects JUNGLE2; Enter/Enter loads it normally.
Destination is empty, terrain visible; count+instance+open-state+UI-row checked.
Projected effect position AND full camera transform/FOV/viewport are logged;
comparison images need not be assumed pixel-aligned.

Results, same gameplay path:
- Before patches, `/tmp/tpw-reset-before-01.log`:53observations,4mismatches.
  SAME node258889226347, Emitting=true andeffectivelyvisible, survived map change
  at(37.99974,1.1414464,-32.00022), continuouscount1/rootchildren1. It still emitted
  after3simulationseconds. Newparkopen=true andOpenParkrowabsent. The background
  launch did not retain its shell exitcode; failed expectations are explicit.
- ParticlepatchONLY b6a2283(localcd10827), `/tmp/tpw-reset-particle-01.log`:
  continuous1->0, children1->0, oldinstanceinvalid. Open-state/UI-row stillfail.
  53observations,2mismatches, explicitlyrecordedexit2. Thus particle success is
  not being confused with the separate flag fix.
- Both patches, adding19e61f8(local55e2a15), `/tmp/tpw-reset-both-02.log`:
  56observations,0mismatches, exit0. Emitter entry/nodegone, newparkclosed with
  OpenPark row, clicking that row independentlyopens the destination.
- Explicit --laptop-park-open control `/tmp/tpw-reset-autoopen-01.log`:
  51observations,0mismatches,exit0. Bothparksopen asrequested, OpenParkrowcorrectly
  absent, emittercleanupstillpasses. This is a diagnostic configuration control,
  not represented as an unassisted player action.

Sourcebaseline6853bfc+isolatedlanguagepatch, thenisolatedparticle/open-statepatches;
NOT a claim to have run the entire latestmain/seam/benchmark combination.
No new gameplay implementation byastraclaw in this checkpoint; test+evidence only.
`both-01` printed56/0 but its enclosing build+run tool exhausted120s before anexit
filewasrecorded. Repeated asboth-02 under anindependent180s process guard; confirmed
exit0. It was aninfrastructurebudgetissue, not a gameplay failure. No testprocess
remains running. Wrapper `/tmp/tpw-run-reset-final.sh` serialized thetwofinalruns.

Cow's visualbeforefixconfirmation1554304012210413620: bubbletrail aboveFANTASYshop
persists abovebareJUNGLEgrass, distinguishablefromfoliage; comparison posted and
also visible to parent inchannel. AfterfixPNGs posted for separatevisualreview.

Price correction: the read-only script worker cited the rawSAM cost750. The actual
compiled purchase path charged$250 in this replay. SAMcost is NOT the liveprice;
no expectedprice assertion was built from that worker value.

Afterfixvisualreview closed bycow1554309957980127303: landmarks matched despite
slightlyshiftedframing; oldbubbletrailabsentafterb6a2283 andOpenParkrowpresent.
Parent also received the cropped comparison as animage. Tiny reportsbothfixeson
main19e61f8 (1554310769577820161). Mainnow5b7f113includinginertbenches; merged here.

Nextjourney telemetry correction: ParkRide.Customers is sale bookkeeping, NOT
an amusementride boardingcounter. Added passive scriptOnRide/Queue, rendered
_seated guestIDs/positions, optionalnativequeuecounters, visitorPurchases/Relieved/
LitterDropped/etc, existingNeeds.All valuecopies, staffjobs andLitter.Made/Swept.
CountersBoardings/Rides include facilities too, so pair with ride/guestownership;
no globalcounterdelta alone claims CrazyApecompletion. Never initialize staff
Candidates in telemetry (lazy/RNG-consuming); actualHireGUI can do so normally.
Interactiveidle samples every5wallseconds, with one-time milestone screenshots;
these are instrumented functional runs, NOT performance benchmarks. Screenshots
named for a positivecounter do not assert the event is in the camera's view.
Buildtour now goes straight fromopeningthepark tobuilding, not viaResearch/Hire
first. This is an ordinary alternative player route; note itchanges the timing of
lazycandidateRNG draws. Language tour remains available separately. Commands are
consumed onlythrough their terminatingnewline toavoid partialappend parsefailures.

## Live player journey 02: services, riders and cleaner (2026-09-29)

Run `/tmp/tpw-player-live-02.log`, artifacts/commands/observations under
`/tmp/tpw-player-live-02/`, main5b7f113 + driver231fb16. Normal processing,
English coldboot, laptopOpenPark, CrazyApe purchase and owned queue/exit,
BurgerShop and SmallToilet with connecting paths. No fixture guests/needs/litter,
no forced jobs. Instrumented software-rendered run, not a performance benchmark.

Logical ride evidence is guest-specific: guests1/2 enter CrazyApe seats and later
return to walking; guest3 does likewise in a later cycle. Moving _seated
transforms and VAR_ONRIDE agree. Global Boardings/Rides also include facilities,
and CrazyApe.Customers stays zero, so neither is used alone as boarding proof.
Burger successful Purchases advance naturally; SmallToilet Relieved advances
and its condition wears down. These are runtime functional observations, not
claims that every animation/event was photographed.

Rider visual investigation:
- `14-natural-seated-guests-observed.png`: reviewers saw empty-looking frames.
  A timestamped positive seat count does not mean the heads are visible from
  that camera. No rendering defect was declared.
- `17-riders-active-check.png`, state logged immediately after FramePostDraw
  capture at855156ms/tick7073: VAR_ONRIDE3, guests3/7/15, seats15/6/9 on
  Head13/Head03/Head15. Their origins were respectively(28.017,1.601,-24.143),
  (25.970,1.884,-23.609),(28.017,2.382,-23.038). Still rear-view ambiguity.
- Ordinary E twice and R zoom presses, `19-riders-opposite-closer.png` at
  1133312ms: VAR_ONRIDE2, guest24 seat1/Head09 at(28.018,1.477,-23.912),
  guest26 seat13/Head16 at(28.017,1.540,-23.215).
- Tiny's visual review1554321386380591159 identifies a definite pale/red head
  at the lower-right crate and a probable second mostly occluded under the
  ape's chin. Thus head-only rider rendering is visually present; the earlier
  rear-view images do not establish a defect. Not a proof that every seat
  fitting/offset is perfect. Source-only retained-pose/visibility hypotheses
  were not established as bugs and no implementation change was made.

Cleaner hired through Hire -> Cleaners -> Leon -> Enter, then ordinary click
on path(29,25), active/not-held. Natural litter already existed before hiring.
- 941669ms/tick7763: toiletCondition0, cleaner mode18/state51/request13.
- 962357ms/tick7949: toiletCondition100, cleaner returns to sweep-mode routing.
- 994166ms: sweep mode7/state27/request16.
- Swept increments1 at1004639ms,2 at1020098ms,3 at1051198ms,4 at1086974ms,
  5 at1127104ms; Made5, Count0 thereafter. No direct cleanup calls.
This distinguishes actual completed service/work from merely hiring an actor.
Other staff hires are being exercised in this same live session; their hiring
must not be counted as proof of repair/security/research/entertainment outcomes.

At1262106ms/tick10730 all five staff kinds had been hired through the ordinary
candidate screens and dropped onto the path: Handyman, Mechanic, Guard,
Entertainer, Researcher. All Active=true/Held=false. `20-all-staff-hired.png`
is captured but not yet visually reviewed. Mechanic mode6/state14 and entertainer
mode2/state12 observed subsequently; these alone are not completed repair or
entertainment outcome evidence. Research unlocks not claimed (known missing
research database). Session remains live for further player-path work; no build
or reload was done during it.

Correction after the preceding checkpoint: cow1554322397811965982 challenged the
head identification using the same-arm seat layout; tiny1554322445354279037
withdrew "closes it". Visual riders are OPEN again. The pale/blue-grey candidate
is not identified and the chin speck may be litter. Neither colour counts nor
seat coordinates prove rendered head identity. Next run adds passive postdraw
head-surface bounds, effective visibility, hidden-node membership and projection;
no runtime tick/presentation/pose call is permitted merely to observe the scene.
`GuestTestStage` cannot be invoked passively: it advances ticks and presents.
Its DrawnBounds helper also filters local Visible, not ancestor-effective visibility.
There are no `has no body` or `NO HEAD MESH FOUND` lines in live02 at this check.

Live02 ended through broker Quit at1834276ms, 207 observations/0 automatic
mismatches. Process710797 exited before the next build. Extra navigation exercised
actual mouse Back after Visitor Information, Statistics, Park Finance, Awards,
Balance Sheet, Overall Statistics, Finance Statistics, New Loan (first lender),
Game Options, Ride Information(CrazyApe), Shop Information(BurgerShop), and
Toilet Information(SmallToilet). Each returned to its parent; the next requested
row was found through the GUI. This is navigation coverage, not validation of
all displayed finance/statistics values, every lender/pager, or every laptop
page. PNGs21..33 retained, visual review still pending. No loans were taken,
settings changed, or prices adjusted in this sweep.

Added test-only ObserveGuestRender after each screenshot, no game implementation
changes. Mesh AABB corners transformed with each surface's GlobalTransform,
active capture viewport camera projection, effective visibility and ancestors,
_hidden membership by actual model mesh index. Primitive snapshots, no calls to
TickPark/Present/PlaceActors/Show/SetFrame. Snapshot point tests explicitly do not
claim occlusion or whole-AABB frustum inclusion. Built successfully after fixing
a missing file-local Aps alias; behavioral capture run follows separately.

## Passive head capture and subsequent construction (2026-09-29)

Build01b731e ran normally as `/tmp/tpw-player-live-04` (log beside directory).
An initial detached launch live03 was terminated by the shell-tool timeout while
loading; it produced no park result. Relaunched with a correctly detached process
729781. No gameplay finding is attributed to that infrastructure interruption.

At138964ms/tick935, `14-natural-seated-guests-observed.png` and guest-render JSON:
- Guest1 girl1head/Head03: both surfaces local+effectively visible, camera-layer
  intersection true, meshIndex0 not in _hidden. Transformed AABB corner projection
  x621.92..636.87/y227.82..240.28, positive camera depths12.06..12.41.
- Guest2 boy1head/Head1: all three head surfaces likewise enabled, _hidden empty.
  Projection x623.16..632.60/y217.04..229.67, depths12.29..12.64.
Cow1554326112505438332 checked those exact boxes in the supplied image: guest2's
head is visibly present, while guest1's projected region is covered by the car.
Posted annotated crop is also visible to this assistant. Separately tiny's
side-view fixture1554325241591894167 clearly shows a head under its overturned
car. Thus the original "heads may not draw" concern is not reproduced: visibility
plumbing works and one head survives occlusion in the actual player-path capture.
No visibility/anchor fix made. Do not generalize this to a retail-fidelity claim
for every arm animation or seat. Tiny owns the separate seat-up diagnostic.

Continued normal-input live04:
- Selected Arcade from Sideshows, pressed R, observed Turns0->1, purchased at
  corner(31,26); build log says turned90. Added ordinary path29,27->30,27.
- Built TinyRock at(32,23), placedID3, then Escape/Delete/click/Escape. Runtime
  removes ID3 without removing CrazyApe1 or Arcade2. Before/after PNGs16/17 have
  the same camera; projected cell centre(423.99,249.31). Cow1554326886048337923
  visually confirms rock and selection marker gone, grass restored. No claim
  that this one click covers drag-box/mixed-object deletion.
- DinoKarts station purchased normally at corner(38,30), turns0, station
  exit(42,31)/return(36,31), price35 per track piece. Track draw mode opened.
  Circuit construction is underway; station existence is not a completed loop.

Live04 track follow-up is NOT a passing circuit. Attempted endpoints48,31;
48,39;34,39;34,31;36,31 and then a short44,31 retry. No `[track] leg` commit
appeared, no exception, station remains. Camera key framing overshot (x63.67 at
one capture) and the current driver does not report track-preview rejection
reasons. Do not call this a game defect or a completed circuit. Screenshot22
`track-blocked-status.png` temporarily exposes the ordinary F3 debug panel for
review; F3 was toggled back afterwards. Asked tiny for visual status/marker
reading (1554328278435631107). Next split is actual geometry rejection versus
input aiming, not forced waypoints. Source readonly control permits only vacant
playable2x2 end blocks, so an invalid site/path is also possible.

Track correction: the short (44,31) retry DID commit after the earlier log poll:
`[track] leg to (44,31): 1 pieces for $35; 4 pieces, closed False`.
The prior no-line statement about that retry was premature while queued camera
commands were still running. Stock32 is consistent with 4 actual piece records,
not an empty layout. Paid step count and baked piece count differ. Subsequent
retries were polled through their final command-done (95 and98 respectively):
the48/31->48/39->34/39->34/31->36/31 sequence and44/35 produced no additional
leg-commit lines. The loop remains unverified, not diagnosed broken.
Live04 quit normally at1204106ms,164observations/0automaticmismatches; process
729781 exited. No game process on this tree was rebuilt underneath a session.

Added test-only tool-input snapshots before mouse press, before release, after
release, and postdraw. Includes actual pointer, CursorCell result, full status,
_trackLegOk/_previewCost/_previewStock, track Price/Waypoints/Pieces/Closed, and
candidate2x2 blocks' playable/vacant/path/ownpiece readings. The exact picker is
invoked ONLY as passive telemetry: CursorCell/CellAtScreen source was audited as
read-only (plane intersection + nearest-cell search, no writes). No tool handler,
forced cursor result, geometry mutation, or manual simulation/presentation call.
This distinguishes failed picking from rejected geometry and stale/clipped text.
Build succeeds; runtime verification follows in a fresh session. The F3 diagnostic
was shown only for a screenshot and hidden before the next click; don't infer
that its left-side input guard caused the prior attempts without cursor evidence.

## Live05: completed track and coaster player paths (2026-09-29)

Run `/tmp/tpw-player-live-05.log`, artifact/JSONL directory of the same name,
built efa320d on5b7f113. Tool observer successfully records actual input boundaries.
The prior track ambiguity is resolved WITHOUT a game patch:
- Station38,30, exit42,31, return36,31. Price35 observed.
- Actual picker48,31, panel hidden: rejected because end block contains
  non-playable49,32. Actual picker44,35 rejected for45,33;44/45,34..36.
- Actual picker44,23 valid: $140 paid, four steps. The earlier bad route was
  invalid terrain, not a picker/renderer failure. No general claim about every
  earlier overshot-camera event is inferred from this clean controlled replay.

Actual committed waypoints:42,31 ->44,31 ->44,23 ->34,23 ->34,31 ->36,31.
Five paid legs1/4/5/4/1 steps, $35/$140/$175/$140/$35 = $525. At closure,
`closed True` and `loop closed`, six waypoints and18piece records. Counts alone
are NOT the closure proof. Queue39,29->29,29; exit40,29->40,28->29,28. The path
crossings caused the expected track bridge/rebuild reports at34/35,28/29.
Tiny1554332807235633194 visually confirms closed circuit, station and bridge.
Its "no karts" remark was corrected bycow1554333251101786123: one parked car
already appears in the construction image. Do not infer spawning from boarding.

Dino guests16/3 first observed seated at461799ms,17/9 at466933ms. Synchronized
525462ms capture21 has actual head bounds around(783,504),(500,531),(708,252),
(321,490). Tiny1554333122928189484 andcow1554333251101786123 confirm all four
coloured karts with rider heads; parent's received crop shows them too.
Same IDs return to walking individually:9 at611583ms,3 at616776,16 at632275,
17 at637431. Track completion is guest-specific, not a global Rides delta.

Chac Atak bought through RollerCoasters menu, R twice, stationcorner29,42,
trackexit31,43 step3 / entry28,43. Accepted pylons:
37,43;42,47;42,53;37,57;31,55;25,53;20,49;22,43.
The original31,57 proposal was correctly refused as not empty land; ensuing
farther clicks were too distant from the still-last37,57. Adjusted route above
accepted. Entry click28,43 closed the ring; GD.Print says8pylons/validTrue.
Enter finished editing: closed and valid, test lap59secs/54metres/peak7kph,
0drops, ratingTooSlow. That's a flat poor design, not failed construction.
Queue30,45->30,46->27,46->27,29->29,29. Separate exit29,45->28,45->28,30->
29,30->29,29. Earlier attempts across the exit stub or queue were refused;
route was adjusted, no path-owner rules bypassed.

Connected image29 shows station/trough/pylons, queue guests, and cars; peer
reviews1554336354916368405/1554336383899009072. The whole loop is NOT in frame;
closure rests on runtime flags, not a count of visible pylons. Potential overlap
with purple scenery remains UNESTABLISHED: projection/elevation can account for
it, per cow1554336823822651452. Intermediate cell-mask inspection is still open.
Do not record a scenery collision from that image alone.

Chac riders41,44,45,52 first observed1184227..1189560ms;54 at1215917;51,48,50,
53,47,55 at1237574..1248163. All eleven later return to walking withRideId0,
intentWandering, at1453809..1501580ms. Loaded-train image33 was HUD-occluded:
no visual six-head claim. Image34 reframes guest54's actual head box to
x559..572/y267..282, clear of HUD; visual review pending at this checkpoint.

Mechanic Bob hired and dropped normally at29,25. CrazyApe context offered
Details/CallMechanic/EditQueue/Delete. Opening selects NOTHING: first Down
lands on Details, not CallMechanic (ObjectMenu.Move). First attempted call
therefore opened Details. Retried with two Down presses, actual dispatch log:
`Call Mechanic on Crazy Ape (status 5): nobody sent (0x124158)`.
No dispatch/repair success claimed. Assigned mechanic, availability, and Life
are not in the current snapshot, so the refusal's cause is unestablished; core
CallMechanic can refuse an already assigned ride or unavailable/condemned case.
Needs explicit owner-state observation before diagnosing a bug.

## Wrap/hold checkpoint (2026-09-29, owner1554339188017729599)

Owner requested wrapping, pushing and HOLD. No further tests/features started.
Live05 ended via broker Quit at2162686ms,280observations/0automaticmismatches.
This is the driver's outcome, NOT a blanket pass for every gameplay feature.
The eight already-queued synchronized burst captures completed before Quit:
35..42-coaster-open-trough-burst-1..8.png (2009621..2105017ms), with per-surface
projection records in input-observations.jsonl. They are retained but UNREVIEWED.
Image34 did not settle rider visibility: tiny1554338442220011542 and
cow1554338605193760852 identify station occlusion at guest54's projected box.
Thus coaster boarding/completion is established from guest-specific runtime
observations; coaster rider pixels on open trough remain an outstanding visual
check. Do not promote the burst's existence to a visual success claim.

A read-only worker completed a source review before HOLD. Unverified lead, no
patch: findings/coaster-building.md:609-641 describes ten spline samples and a
2.0 clearance floor from runtime tile bit0, with object-height clearance, not a
universal ban on all airborne track over non-buildable cells. It flags
Viewer.Coasters.cs:118-128 as potentially omitting that tile-floor contribution,
plus documented object-height/overlap approximations in CoasterTrack.cs:601-637.
This does NOT establish that this course intersects scenery or hits that gap.
Safe future observation route: existing ParkRide.Coaster.Track, SegmentNode,
pure CoasterTrack.Spline, Park.Field.Raw0/IsPlayable/PlacedAt and path-kind reads;
no rebuilding, test lap, or cache-writing clearance adapter for a passive dump.
Review the lead against source and a control before proposing a fix.

Outstanding work when/if resumed:
- Review the coaster burst; inspect intermediate course cells/scenery clearance.
- Mechanic call refusal: capture ride Life/AssignedMechanic and mechanic target/
  availability/dispatch counters before judging; completed repair not proven here.
- Latest main fences12a3954 and seat-up diagnostic5d6c914 were announced during
  this session; this run remained on5b7f113 plus tests, not those newer changes.
- Remaining worlds/build classes, upgrades, prices/wages/month-end/red-money,
  advisor interaction coverage and multi-year soak are not exhausted.
- Save/load remains PAUSED, full player Save/Load not delivered. No save work
  was resumed as part of these playtests.

All current gameplay actions remained ordinary input. Branch changes in this
checkpoint are test instrumentation and evidence only, not new game fixes.
