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
