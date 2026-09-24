# Native bus — live Viewer integration

Status, 2026-09-24: branch integration tested through normal Viewer startup, not just
an isolated native-controller harness. See `bus-native-lifecycle.md`,
`bus-catalogue-identity-join.md` and `bus-admission-pressure.md` for source evidence
and the inputs that are **not** yet represented by the port.

## Shipping callers

* `PlaceHeld` registers the successful physical node, runtime ID and actual compiled
  `RideDefinition`, before the optional script startup. Unscripted placements are not
  silently dropped by using only the simulation's scripted rides. `BusObjects`
  reconciles these records against the park's still-present physical nodes.
* `TickPark` drives `NativeBus` after the guest update. The original periodic `Gate`
  spawn method is removed, not left beside a decorative bus.
* The actual WAD and selected terrain identify the native scenario/catalogue and
  bus1/bus2 resource. No selection from the currently highlighted build-menu row.
* Phase-2 completion requests a batch. The live ordered objects supply the native
  demand sum and distinct compiled identities supply the representation ceiling.
  Population includes Plans/off-walk service identities, not just visible walkers.
* `ParkVisitors.Arrive(point0, point0)` creates every member of the load at the same
  native entrance point0. It seeds real needs/identity; destination choice happens
  in the normal subsequent guest update. Neither the bus fitting nor the inner gate
  mouth supplies the spawn position.
* The bus root uses the authored model/world frame, with the renderer's Z conversion.
  Its MPS-owned travel curve, APS percentage channel, facing and node visibility are
  evaluated through `AnimatedModel`. State0 does not invent an un-hide/reset.
* Executed updates drive countdowns. Separately accumulated active elapsed time,
  quantized to ten milliseconds, drives animation. This does not claim exact console
  interrupt/pump timing at pause edges or all future pause modes.

## Audio/lifetime join

`Viewer.BusAudio.cs` owns the moving sound registration; `Viewer.Bus.cs` supplies
native state, fitting1/space0x200 and parameter20. Native category1 is the port's
`GlobalAmbient` group8, event6, not script group1. Only the registered bus owner is
made positional; ambient sounds are not globally reclassified. The sound graph's
scheduler remains the existing audio implementation, not newly proven timing here.

The bus can create a sound owner before the first shop. `MakeSounds` installs the
ride-level accessor first; the bus wraps it, so selector20 does not prevent a later
ride from using selector6. The chain is associated with the actual `RideSounds`
instance, not a permanent boolean. A map change stops/unregisters the bus sound,
clears the old root, placements, clock, controller and admissions, and rebuilds the
correct map's owners. Switching Viewer mode hides the bus presentation outside Park.

## Explicit missing constraints — zero is permissive

The native entrance-group lists and sticky `guest+A4` departure-deferral lifecycle
are not yet implemented in the guest adapter. Their supplied zero counts **bypass
both the entrance backlog reduction and the departure-pressure veto**. This is not
a harmless zero contribution, and the startup warning says so. Busy-park admission
can exceed native behavior. `20 - N` can bind even below20: with15 waiting, a demand
bound8 must be reduced to5; supplying0 instead admits8. Checks pin that distinction.
All three batch bounds are exposed/logged from the same calculation used to admit.

No attraction minigame session exists in the port, so that gate is absent. Normal
new-instance upgrade tier0 is supported; a live upgrade-tier UI and associated
state changes are not implemented. The legacy fixture switch `_guestCap == 0`
disables automatic arrivals explicitly; positive legacy caps do not replace the
native ceiling. None of these boundaries is proof of full native visitor AI parity.

## Reproduction and scope of evidence

Build once before starting Godot; do not run a cleaning build concurrently with a
rendered scene launch (that produced a missing-C#-class harness failure, not a bus
failure). Set `TPW_PS2_DISC` to the owner's disc and read it in place.

```
dotnet build game
LIBGL_ALWAYS_SOFTWARE=1 LP_NUM_THREADS=2 xvfb-run -a "$GODOT" \
  --audio-driver Dummy --resolution 640x360 --path game \
  res://tests/BusViewerSmoke.tscn -- --mode=park --map=JUNGLE
```

Repeat with FANTASY, HALLOW and SPACE, and with the exact startup label
`--map="JUNGLE  terrain_2.mps"` (two spaces), likewise for the other worlds.

`BusViewerSmoke` uses real `_Ready`, actual Shops menu/ArmFromList/PlaceHeld, the
real world ice-cream definition/script, and `StepPark(.04)`; it never seeds guests
or constructs a substitute bus. It checks placement identity, phase ordering,
actual first-batch walkers at point0, seeded Plans/Needs, live scene-tree meshes,
selector20 high/reset, preservation of a changing scream level, map teardown and
a new empty park's sound owner before any shop is built. All eight starting parks
passed2039 assertions each. For this one-shop fixture the first batch is one guest
at405 executed ticks/16200ms. This demonstrates a live integration, **not** correct
backlog behavior or human approval of the picture/sound.

Independent headless `NativeBusAudit` covers the full phase loop/restart and actual
model surfaces; `ModelPathAudit` covers the shared path consumer. Existing raw
retail failures (HALLOW Thrill Grill, SPACE Moon Buggies) remain visible in the
four-world matrix; no bus-specific waiver is introduced.

### Standing-service audit shutdown finding

The combined gate exposed an intermittent fixture shutdown race, not a bus assertion:
all155 standing-service checks passed, then Godot reported28 `AudioStreamPlaybackWAV`
and7 `AudioStreamWAV` references still live (no Nodes). Stopping voices and awaiting
two headless process frames can finish before Dummy audio retires its playbacks.
The fixture now allows100ms for that shutdown after freeing its stage. A control
removing only that wait reproduced the same35-object signature4/10 times; with the
wait restored,20 verbose and5 ordinary runs were clean. Leak classification and
production audio code remain unchanged; this delay is test teardown policy, not
console sound timing.
