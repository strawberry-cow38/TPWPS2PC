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

`_guestRng` is STILL System.Random seeded1 in declaration and Reset; switch these owners to
SnapshotRandom with the proven seeded-compatibility contract BEFORE attempting full capture.
Preserve one shared stream across bus admission, staff, idle picks and all other consumers.
Calendar `_calendar` has a DTO, but `_parkClock` is ConsoleClock, the frame/tick carry owner:
read/capture it separately. `_parkTicks`, legacy guest stage/since/dwell, park-open `_gateClosed`,
guest-cap/test flags, selections and mode/root topology all need explicit ownership.
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
