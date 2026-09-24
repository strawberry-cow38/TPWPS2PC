# Native quarter-cell route consumer — branch integration boundary

September 24, 2026. This is **research-branch code, not automatic park admission**.
The normal bus still calls the existing arrival path. Neither its backlog input
nor its departure-pressure input is repaired merely by adding this actuator.

## Actual consumer, not a second disconnected arithmetic implementation

`ParkVisitors.BeginEntranceRoute` takes an existing live guest under a caller-owned
entrance lease. `GuestWalk.Step` dispatches that lease to `NativeGuestRoute`, which
uses `NativeGuestMotion`'s signed 1/256-cell coordinates, quarter-cell target codec
and per-update integer movement. It does not run the legacy 1000-units-per-cell
walker as well. The lease exposes its exact coordinate through `Guest.Position`;
ordinary `Viewer.PlaceActors` renders that position and its native facing.

The cursor reproduces the observed `191E98` / `191D78` / `20D628` execution phases:
reaching a target changes walking state3 to state2 without releasing the slot;
the next update retires/advances it without moving; a terminal state3/no-slot
update changes to state2 before checking animation readiness; a following update
reports completion once. Even a start-equals-target route takes these phases.
Targets use the decoded slot codec, but the current coordinate is retained exactly
when the same owner replaces a route. An in-flight 15/256-cell coordinate must not
be rounded back to a quarter cell.

`NativeMotionInputs` requires explicit speed, delta and animation readiness
getters. They are not read on native no-slot/retirement paths. The test's ready=true
is a fixture, not evidence that model9/13 readiness has been integrated.

## Managed ownership policy, distinguished from native findings

The owner token is a reference-identity capability local to the port, not a native
pointer or pool index. A different owner cannot replace, inspect or release the
route. Existing attraction ownership and a legacy mid-edge handoff refuse.
Ordinary destination selection, departure recovery and delivery skip leased
guests, including completed leases: completion is not permission to steal one.
Removal and map clear revoke leases, including references retained by a caller.
The same guest ID, numeric needs and cash survive movement and handoff; this is
not a despawn/respawn adapter. Thought icons can still change normally.

Handoff to the legacy walker is allowed only after completion at an exact cell
centre. Native accepted mode13 does target a centre; an arbitrary fractional
handoff must retain its owner rather than teleport. Bounds failure is latched and
left for the owner to resolve. These two guard policies are managed integration
constraints, **not claims about all native failure and cancellation callbacks**.

The initial cursor used a copied array. This is now superseded by the actual
shared output-pool consumer documented in native-route-slot-pool.md: SlotIndex
is a real handle, targets/links live in the packed table and retirement frees one.
Search resources, request scheduling, full route selection and native indirect
cancellation callbacks remain separate from output ownership.

## Discriminating tests through production consumers

* `NativeGuestRouteChecks`: 76 checks, including exact terminal phase ordering,
  no-slot readiness bypass, negative coordinates, target copying and one-shot
  completion/failure.
* `NativeWalkConsumerChecks`: 75 checks through actual `GuestWalk.Step` and
  `ParkVisitors.Step`. Wrong-owner attempts, idle/departure stealing, fractional
  snapping, same-owner precise replacement, map clear/reused IDs and unchanged
  numeric needs/cash are covered.
* `NativeEntranceRouteSmoke`: 113 checks through normal Viewer startup,
  `StepPark`, and actual actor transforms. A controlled entrance-corridor guest
  moves by native increments to a quarter-cell target and back to a cell centre.
  Position uses an independent cell-corner interpolation oracle; both facing
  directions are checked. The same actor and identity persist. The fixture
  disables automatic bus arrivals and explicitly takes the lease: it does not
  prove automatic admission, queue membership, charging or departure vetoes.

Mutations were applied and restored: skipping native dispatch caused45 failures;
finishing at target contact caused40; removing idle-owner protection caused10;
allowing fractional release caused9. Removing the Viewer native-facing branch
failed the rendered check at73. The restored rendered scene passed113 with clean
shutdown. These are tests of the shipping consumer functions on this branch,
not a claim that their automatic entrance caller already exists.

## Next integration, not optional finishing work

Implement the ID-owned incoming controller and ordered request/result service:
state24 request -> movement15 -> staging2E -> coordinator9 -> movement16 ->
registration2A -> quarter-cell queue -> head release25 -> acceptance/charge ->
accepted2D/movement13 -> normal decision state. Use the two actual incoming list
counts; do not substitute nearby walkers. Preserve membership, staging and
same-tick ordering across cancellation/removal. Native global activation serial,
normal idle decision ordering and sticky departure deferral are distinct gaps.
See `native-entrance-lifecycle.md` for positive code addresses and source limits.
