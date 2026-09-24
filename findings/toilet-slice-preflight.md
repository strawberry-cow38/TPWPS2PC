# CP1 preflight: one small-toilet needs-satisfaction flow

Baseline: `58fbd88`, reviewed September 24, 2026 UTC. **This is read-only scope/interface
preparation, not an implemented or approved feature.** Placement/routing scope confirmation
was requested from strawberry. Cow tools owns the implementation files; astraclaw prepares
independent validation after the observable interface is agreed.

## Independently checked data

A temporary metadata-only C# probe used the existing managed readers to inspect the
owner's disc in memory. It emitted identities, selected SAM fields, sibling presence
and parsed opcode counts; it did not extract assets or add a permanent audit project.
Exactly two non-alias definitions per world have `UsageInfo.ProvidesRelief=1`:

| World | Small toilet | Super toilet | Small entry/exit fractional position |
|---|---|---|---|
| JUNGLE | 1402, `/Features/Toilet/Toilet.sam` | 1419, `/Features/SupBog/SupBog.sam` | (0.5, 0.8) |
| FANTASY | 4405, `/Features/loo/loo.sam` | 4406, `/Features/royaloo/royaloo.sam` | (0.5, 0.6) |
| HALLOW | 2404, `/features/horloo/horloo.sam` | 2406, `/features/horsuloo/horsuloo.sam` | (0.5, 0.7) |
| SPACE | 3402, `/features/loo/loo.sam` | 3403, `/features/loo_big/loo_big.sam` | (0.5, 0.6) |

All eight have `Info.IsChoosable=1`, a same-stem model and script. All four small shapes
are the single character `2`. The generic reader exposes these fields through
`RideDefinition.Int/Float/Fields`; a typed ProvidesRelief property is not present at
this baseline. The marker avoids an invented name/ID-based facility classification.

The four small scripts contain neither LIMBO nor WALK operations. The super scripts
contain LIMBO; FANTASY's royaloo also contains WALKON/WALKOFF/WALKGET and has neither a
same-stem APS nor any APS in its folder. The other seven have their same-stem APS.
The current asset resolver uses own-stem animation, then a unique same-folder animation;
StartScript rejects null animation. That makes royaloo an explicit compatibility question
for that path—not proof of a retail defect or permission to invent an animation.
Keep super toilets, particularly royaloo, out of the first slice.

Opcode presence was parsed through RseProgram, not searched as strings in compiled
bytes. These are static script facts; declared mailbox variables alone do not prove
an executed handback or branch reachability. Child/script-runtime behavior still needs
its actual consumer demonstration.

## Existing placement and service evidence

`Park.Footprint.From` treats `2` as the entrance cell. For the small shape this yields
1x1, EntryX/EntryY=(0,0), no authored compass exit, and the existing small-square entrance
facing tie-break. `BuildCategory`/the build panel and entrance-capability checks already
provide code-side support for these feature assets. The SAM's fractional stand/appear
coordinates are separate from the integer footprint cell; validating one is not proof
that the other is consumed correctly after placement/rotation.

Cow tools is checking its existing placement path. **An actual placed toilet in a running
park remains unconfirmed here.** Parsing a footprint and listing an asset are not that check.
No placement code was added or touched in this preflight.

`findings/visitors.md` documents outside service versus LIMBO-hidden service for existing
shops/sideshows, including runtime evidence and the limitations of its script-chain scan.
That supports choosing the non-LIMBO small toilet as the simpler candidate, while still
requiring a demonstration for this particular facility. It does not make every service
an ordinary seated ride.

## A concrete consumer seam to address

The current coordinator's Deliver removes all handed-over guests from GuestWalk.
Viewer.PlaceActors constructs its live body set from walking guests, seated riders and
scripted WALK poses; its comment explicitly says a queued/unseated guest has no body.
Therefore a small-toilet outside-service flow needs a deliberate visible service-body
representation (or equivalent correct consumer path), not just a UseToilet call after
handover. This is a **code-side integration risk**, not a disappearance reproduced in
a running park during this preflight. It has been shared with the implementation owner.

Keep ownership and presentation distinct: preserving a logical plan does not prove a
visible body, and a visible body does not permit two independent systems to move it.
The corresponding thought/bubble anchor must be tested for the same identity during
service, not only after it returns to the walking set.

## Interface decisions still required at CP1

Before independent tests are written, agree observable inputs/results rather than a
speculative API:

* How an actual placed ProvidesRelief facility registers its instance, availability,
  rotated entry/exit placement and service-body position with the coordinator.
* Which event constitutes successful service: verify the actual script handback/path,
  rather than silently equating reaching the queue tile with completed use.
* How per-instance cumulative soil and completed use can be observed. Removed/replaced
  facilities must not inherit stale work merely because a numeric ID is reused.
* How the toilet path differs from generic Needs.Ride effects and ride counters. Existing
  UseToilet changes only Toilet and returns soil; unrelated mood/cash changes require an
  explicit contract, not accidental reuse of the ride-completion handler.
* What happens on an unavailable/unreachable/removed facility and which owner retains the
  guest. Mid-edge Send refusal is not NoRoute, permission to teleport, or proof of arrival.
* How outside-service body visibility and bubble recomputation are demonstrated in the
  actual viewer without falsely claiming human visual review from logs alone.

The peer corrected an earlier executable interpretation: a threshold helper records
an errand rather than searching for an available facility. That needs its consumer
provenance recorded with the implementation. Current Decide still has availability-gated
single-need branches; do not confuse the proposed correction with already-changed source.

Acceptance remains the bounded plan.md draft: control other needs/rates; 90 versus 91;
reachable versus unreachable; identity/cash continuity; Toilet 91->0 and soil 20 exactly
once; later ticks do not duplicate service; one removal/availability boundary; actual
player-flow evidence. No food/economy/general facility framework or super-toilet support
is implied. Stop/review if the body/placement/handback path makes this materially larger.

Local evidence: task scratch `tpw-toilet-contract-probe.log` and
`tpw-toilet-probe-path.txt` (temporary probe source/build, outside the repository).
No production source changes, image claims, service restart or new gameplay approval.
