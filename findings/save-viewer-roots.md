# Remaining Viewer roots for TOTAL save/load (September 28, 2026)

This is a source inventory, not a complete-world implementation or permission to enable Save.
Core owner snapshots are in save-load.md. Final staging must use fresh terrain/model instances,
then publish only after EVERY owner and external reference validates. Nothing below is a reason
to replay Hire/Add/Create/Submit/Speak or advance the old world during capture.

## Native bus and entrance partials

Viewer.Bus.cs: `_busElapsedMs` is double active time; BusClock is DERIVED ten-ms quantization.
Save the double, `_busClockAdvancedForFrame`, traffic, BusParameter20/stamp/pending-reset, batches,
admitted, load-failed/source key, `_nativeLoadsOfKids`, and placement order `(Node,RuntimeId,Definition)`
by placed-object/asset IDs. Catalogue/mesh assets can reload by verified IDs. Do not call
EnsureNativeBus on restore: it consumes `_guestRng.Next`, creates a controller and invokes state0.
NativeBus.State restores an actual detached renderer/controller graph. Current restriction: save
AFTER its last bind has been sampled (normal PresentNativeBus boundary), not an unsampled Bind
cut; and only the verified ordinary/non-texture-history records. It fails explicitly otherwise.
Do NOT call Present as a side effect of Capture to manufacture a valid cut. Queue capture at an
end-of-frame safe point after ordinary presentation, or extend retained-pose ownership first.

Viewer.Entrance.cs: fee, tick, accepted/rejected, legacy flag, shared activation owner, controller,
Walk/Visitors refs, mailbox and hooks. Mailbox is already a real owner. Diagnostics request queue
and request-flag set still need either state or an explicit documented non-gameplay omission.
Tick-hook ordering matters: prior -> entrance -> ride queue; rebuild once, no duplicate subscriptions.
Admission providers read `_guestRng`, geometry/catalogue/placements, native animation readiness.
Controller DTO delegate IDs do not capture those VALUES. Staff's GateEvent9 also targets this owner.

Viewer.BusAudio.cs: `_busAudioLive`, parameter hook/chaining and RideSounds owned voice/graph state.
Rebuild hooking against the NEW sound owner, not an old captured closure. Parameter20's delayed
reset is bus state, not a cosmetic cache. BusSoundPosition is derived from the rebuilt sampled bus.

## Native and staff animation providers

Viewer.NativeAnimation.cs: raw `_nativeGuestAnimation` / `_nativeIdleAll` latches, shared
`_nativeAnimationRand` CURRENT state, NativeAnimationWaits, and every `_nativeAnimations` entry by
GuestGraph object ID (NOT display Guest.Id). Each NativeGuestAnimation now has a DTO requiring the
same restored RNG object, logical table/APS fingerprints and explicit duration provider identity.
Restore shared RNG ONCE, before constructing every animation owner. `_logicalAnimations` is a
verified immutable asset binding. OwnAnimation must not be lazily called to invent a missing
provider during capture; its APS should already be identified from the live cache.
`_nativeDrawn` is last-bound record cache, but clearing it is only safe once actual model pose,
retained visibility/texture state is rebuilt; do not call it automatically discardable merely
because it caches a record. Gait records/from stamps elsewhere in Viewer have the same condition.

Viewer.Staff.cs: `_staffActors` contains Member/Serial/Node/Drawn/Anim/Animation/Records/Showing/
ModelPath/Prev/Yaw. Logical animation gates real movement through StaffAnimationReady and cannot
be restarted. `_staffAnimationRand` is another shared NewlibRand owner. Store Prev/Yaw and bind
new nodes/models to saved member identities; distinguish pool slot reuse by saved Serial.
Litter actors are slot/serial/model-path presentation. `_hireHeld` and `_hireCarried` are VIEWER
counterparts to ParkStaff.HireHeld; preserve/link them without replaying Carry/Drop.
Staff sound handles influence `HandlePlaying`, so their producer cannot simply be reset silently.
Native model/APS caches and model registry reload by validated IDs; failed-model sets/load-failure
latches need an explicit retry policy if intentionally not restored.

## Advisor view/audio

CORE ParkAdvisor, Scheduler, MessageStack, real AdvisorProducers, LipTrack.Playback, timed head
channel now have continuation snapshots. Register fresh counterparts for callbacks, including
scheduler DayBinding and the stack callbacks closing over its advisor SHELL. Rebind sim removal/
award/staff/management emitters after all roots validate; Attach itself is not a whole-world loader.

Viewer.Advisor.cs: actual AdvisorHead is NOT AdvisorTimedHead. It owns a channel, `_bound` record,
mutable own Model.D node flags for costume/mouth, Drawn retained visibility/pose, and view/pivot.
Need explicit snapshot/rebuild of those, not Play/Dress based only on logical Costume. The real
voice AudioStreamPlayer needs sound asset ID, seek/progress, pause/play status. AdvisorSpeak
starts at zero and fires through callbacks: never call it to restore ongoing speech.
`_advisorL2`/`_advisorTriangle` are queued input; stack CORE captures its queued buttons/sliding but
not these outer latches. Stack view stores display refs derived from CORE plus its own animation/
pass state: inspect before calling it disposable. Speech/asset/binding caches can reload; failure
latches (`_advisorUnavailable`, `_advisorSpeechTried`, etc.) require explicit policy.
GameAudioMix has a state API now (live levels, targets, gains). RestoreState does NOT change global
AudioServer buses. PublishGains is a separate commit-time operation. GameSettings remains GAME
scope. Rebind Changed once, not twice. Music/SFX physical players/scheduled sound graphs remain
with RideSounds/ToolSounds/etc, not with the mixer DTO.

## Viewer.cs and cross-root registry

UPDATE: `_guestRng` now uses SnapshotRandom at declaration AND Reset. RuntimeState captures its
current state, the two native animation generators, ConsoleClock carry/alpha and entrance/bus/gate
outer scalars. This is NOT the reference/root join.
Preserve one shared stream across bus admission, staff, idle picks and all other consumers.
Calendar `_calendar` has a DTO, but `_parkClock` is ConsoleClock, the frame/tick carry owner:
ConsoleClock is now captured separately. RuntimeState covers `_parkTicks`, guest stage/since,
park-open gate, cap and selected native flags. Guest dwell dictionaries, selections and mode/root
topology still need explicit owners.
Geometry/placement already stage the two HeightField wrappers sharing ONE Cells array. Runtime
ride IDs/placed IDs/Node IDs are distinct; register them explicitly rather than conflating them.
Retired rides in visitor history/advisor stale ring/staff targets need retained object entries even
when not present in Sim.Rides. GuestTarget objects and inactive Guest bodies are distinct objects.
ParkSim now supports allocate -> staff shells -> HydrateStaffState for the mechanic/ride cycle.
Continue that phase ordering through visitors, controllers, advisor and render adapters.

This inventory is NOT a census of every field in the 11k-line Viewer or every helper. Still inspect
ConsoleClock, GameCamera, RenderedRide/Track/Coaster/guest presentation, ScriptEffects, RideSounds
and sound schedulers, native ferry/seaplane game adapters, active editing tools and all partials.
GPU meshes/material instances are rebuilt, not serialized; their retained CPU inputs ARE state.
The final coordinator must establish an omission policy only for truly diagnostic/derived data.

## Critical render-buffer ownership reminder (cow tools, Sept28 16:21UTC)

Perf7f76494/main reuses AnimatedModel.LastWorld=_worldScratch on animated paths and Part.LivePos=
PosScratch on geometry rebuild. Checkpoints must COPY dictionary values/vertex arrays, never keep
these references. Worse, the no-track path returns Model.BindWorld directly; multiple placed
actors can share this dictionary. CORRECTION (cow,16:26): rides use `_rideMeshes` keyed by ride ID,
so two placed rides are NOT a valid sharing fixture. Guests/staff use `_charModels` keyed by model
path. Skeletal records leave the ordinary transform channels empty and take the BindWorld early-
out. Require explicit shared Model AND shared LastWorld assertions on two skeletal actors before
testing isolation; otherwise the fixture is vacuous. Model.BindWorld/BindLocals/BindParents are READ-ONLY shared
asset caches. Never restore by writing through them; use own dictionaries/WorldTransformsInto.
Current NativeBus snapshot already stores copied arrays/scalars and rebuilds a fresh model, not
LastWorld. Required controls for general actor snapshot: source Advance/SetFrame cannot mutate
saved checkpoint; restoring instance A cannot mutate B sharing a Model; source asset caches remain
unchanged. These controls NOW run in AnimatedModelSaveSmoke: two real skeletal actors share Model AND
LastWorld before capture; retained snapshots stay unchanged after SetFrame; other actor and all
shared caches remain unchanged. Explicit read-only sharing on restore forbids WriteNodeFlags.

Advisor head is the separate writable-model case (tinyclaw,16:26): costume/mouth WriteNodeFlags
operate on its OWN cloned model; staff/guest character assets stay read-only. Stage the head from
immutable asset plus its actual saved Dressed/MouthShown (including initial -1), channel/pose and
retained visibility. Do not infer initial visual mouth solely from CORE MouthShape (0 differs from
head MouthShown=-1), and do not serialize entire model bytes to preserve runtime flag writes.


## September 28 — actual presentation owners checkpoint (still not whole Viewer)

AnimatedModel snapshot copies retained positions/UVs, layer data, surface poses/visibility, texture
choices, hidden flags, LastWorld values and actual record/time/pose. Restore uploads CPU geometry
without SetFrame or predecessor replay, including UNSAMPLED record switches. Assets resolve by
IDs/fingerprints, not save paths. Shared immutable actor Model is opt-in and refuses flag writes;
advisor uses a private clone. UV rewrite is an explicit ID + newly rebound provider; staging does
not execute either world's callback. Extra root children and external mesh/material mutations
still fail closed: world topology must handle them explicitly.

AdvisorHead now owns actual Dressed/MouthShown (-1 initial values included), channel/bound record,
pivot/root adjustment, viewport/camera and Drawn state. AdvisorVoiceState stages stopped audio;
attach + ApplyAfterCommit seeks/resumes once. Godot PAUSED means Playing=false but
HasStreamPlayback=true. Both flags are required to restore a paused, nonzero cursor. Dummy mixer
has an asynchronous ~256-sample block: tests retain immediate seek tolerance and then verify a
stationary paused cursor after it drains. This is not sample-perfect audio-buffer persistence.

RideSounds preserves clip/graph RNGs, repeat due times/order/set, cached streams including nulls,
voices/fades/census and provider bindings. No Cue/Start/resolve/provider call during stage. The
in-memory CaptureBindings helper is NOT a cold-load asset registry: root must rebuild trusted
catalogue/event/stream bindings and moving/parameter closures. Head/voice/sounds helpers are not
yet connected to actual Viewer load publication. GameAudioMix PublishGains remains commit-only.

Next: assemble WORLD asset/entity registry and actual Viewer dictionaries/hooks; do not keep
mistaking owner tests for full-world save. Guest/staff render adapters must join real Guest/Member
identities and shared RNGs; placed/runtime IDs differ; retired references remain addressable.
Bus placements and provider callbacks, ScriptEffects/tools/camera/native adapter census remain.
An unpublished new Viewer currently constructs detached Weather/Flags/Thoughts nodes before Ready;
staging cleanup MUST free those too (scalar smoke now does). No Save/Load UI enabled.
