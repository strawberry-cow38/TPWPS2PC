# Small relief-service bodies: bounded player-flow integration

## Scope and evidence boundary

The port now retains a full standing body for a queued/accepted guest at a registered
**1x1 ProvidesRelief facility with finite authored entry stand coordinates**. This is
not a generic presentation system for every shop, Super Toilet, LIMBO, WALK, or seat.
The four small-toilet definitions fit this slice; the permanent runtime flow currently
uses JUNGLE's actual Small Toilet. It does not establish all-world visual parity.

The actual authoritative `ParkVisitors.QueuedOwner` instance owns the customer.
`Queued` includes waiting and accepted service: `Queue`/the LETMEON mailbox select the
existing queue-stub anchor; an accepted customer uses the authored stand point.
Small Toilets do not HUSH their guest, so `Machine.GuestIds` is not the ownership test.
Removed/reused ride IDs cannot inherit another instance's customer. Placed model roots
must remain live. World setup clears registrations and standing poses.

`EntryStandX/Y` are transformed by the same row mirror and quarter-turn convention as
Placement. Fractional coordinates use H-y, not the integer-cell H-1-y. Upright inward
facing is a **port presentation convention**, not a decoded console facing rule.
Waiting bodies retain the existing stub point; spatial queue spacing is not implemented.
Authored exit appearance coordinates are not newly consumed by this change.

Actual host hiding, seat ownership and WALK ownership take precedence over standing.
`RideHandlesSprite` remains nullable authored ownership metadata, **not a port visibility
switch**: the port does not implement the corresponding replacement person drawing.
Suppressing a fallback merely because that field is 1 reproduces the invisibility bug.
Standing guests join walking guests in thought-bubble presentation; thoughts are placed
only after the current frame's actor transforms, and hidden/retired anchors are swept.

## Permanent runtime evidence

`game/tests/StandingServiceAudit.tscn` uses the owner's disc in place. It decodes assets
in memory, extracts no files, and builds real guest meshes, thought artwork and facility
models. It runs real Viewer methods and the actual coordinator/script rather than a
parallel mock renderer. There are **70 numbered checks**:

- Actual handover retains a real full-body actor without a duplicate walker or HUSH stack.
- Four authored standing rotations, upright/inward basis, visible body/leg meshes and
  current-frame bubble positions.
- Explicit host hide/reveal; unresolved seat/WALK precedence; unchanged transit need/cash.
- Genuine script handback clears toilet 91 to 0, retains cash 1234, contributes soil20
  once, clears the bubble and restores one walking body.
- Removal, immediate numeric-ID reuse, recovery, and absent-definition controls.
- The **real PlaceHeld callback**, with cursor-cell override, places the disc model,
  starts its script and lays a walkable service stub at each of four rotations. Each
  placed facility then accepts a real guest, presents a standing body, serves them and
  hands one walking body back. Registration is not manually injected for this half.

The scene deliberately skips Viewer._Ready and its automatic park population; UI child
nodes are reparented into an audit stage. The audio attachment parent is that live stage,
not the intentionally unready Viewer. Dummy audio is used. Godot's deferred deletion is
allowed to finish before checking retired sprites. This is headless scene-graph/runtime
proof, **not a screenshot inspection, audible check, physical mouse test, or complete
normal-startup playthrough**. Those boundaries must not be promoted into visual sign-off.

The original four-check handover probe failed before the fix and passed after. Five
independent local production mutations were then rejected: omit placement registration,
remove fractional row mirroring, suppress on RideHandlesSprite=1, omit standing thought
anchors, and ignore actual host hiding. The standalone mutation runs used the earlier
62-check version; the final eight additions check actual body/leg visibility and inward
orientation. Restored final code passes all70 checks in the seven-scene runtime gate.
A read-only subagent reviewed the production ownership/geometry/reset/bubble patch and
reported no concrete issue; this is additional review, not a claim of exhaustive proof.

## Reachable-facility routing regression

`ServiceRoutingChecks` adds five engine-free checks per world without modifying peer
`ServiceChecks`. In a cloned in-memory terrain grid, the nearest toilet is isolated,
a farther toilet is reachable, and a closer ordinary attraction makes random fallback
observable. It checks the immediate destination, **one total facility before relief**,
and preserved cash, not merely eventual satisfaction. Reinstating nearest-only selection
with `Take(1)` makes three assertions fail. This recreates the pre-fix policy; it is not
claimed as a build of the entire historical 753a4f1 checkout.

All four worlds pass those five checks. The whole-park matrix still reports JUNGLE and
FANTASY green, HALLOW Thrill Grill and SPACE Moon Buggies as their exact documented
retail failures (raw1, matrix2). Required routing coverage prevents a dropped helper
from silently passing. Runtime runner coverage requires numbered checks, named service
and visibility witnesses, matching totals and all four placed completion witnesses.

## CP5 review / next action

The code-side disappearing-body finding is now reproduced and fixed through actual
placement plus service callbacks; stop re-censusing metadata as a substitute for delivery.
Keep the scope small: do not extrapolate this body policy to FANTASY's APS-less Super
Toilet or fabricate LIMBO behavior. Next, peer review the landed boundary and run a
normal-viewer rendered/manual smoke when available, with explicit image inspection.
Review the peer's new departure/queue consumers for integration effects, not another
unmotivated expansion of generic audits. Soil is still coordinator accounting, not a
verified simulation of facility dirt/cleaning. Native platform/audio qualification and
unknown retail mechanics remain separate open work.

### Peer-review follow-up: rotation phase

Cow tools independently checked the authored 1x1/no-LIMBO partition and the inward
facing reference, then requested a phase discriminator against the actual placement
stub. Four added checks compare the standing point's horizontal distance to that stub
against its reflection through the footprint centre. This tests a front-side placement
convention rather than claiming decoded retail pixel coordinates. Removing the row
mirror makes all four new checks fail; restored code passes74 checks. The runner now
requires those four witnesses and the74-check minimum. The new validation is a selected
standing-scene run, not a fresh all-seven claim; the prior full gate remains at6fedf61.

The peer's suggested global draw census is broader than this slice: existing generic
queues and unresolved seat/WALK poses still need their own presentation contracts.
Do not report all coordinator-owned guests as rendered merely because small toilets
are fixed. Explicit hiding is a legitimate omission, not an invisible-body error.

Subsequent full rerun after integrating peer0618434 passes all seven scenes with74 standing
checks. The selected-only limitation above describes the first phase-control run, not this
later full result. Departure repair now has its own independently discriminating regression
(see departure-recovery.md), rather than a change to standing-service ownership.
