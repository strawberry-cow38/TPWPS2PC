# Interrupted departure: recover after path repair

The departure consumer introduced in dc67e2b exposed a pre-existing retry gap. A guest
already walking home could become Stranded after a path was removed, retain a valid
plan/needs record, and never move again even after that path was repaired. Conservation
was not enough: this was a liveness failure.

A read-only review identified the candidate; `DepartureRecoveryChecks` independently
reproduced it in all four worlds before the peer fix. It uses real terrain/entrance data
with an isolated copied route, removes a non-entrance path cell, and later restores that
exact byte. No disc files or shared source data are changed. The six permanent assertions
cover committed departure, observed movement followed by an actually broken route,
retained identity/needs while blocked, restored reachability, eventual arrival home,
and joint retirement of walker/plan/needs. Growth is frozen and cash0 drives departure.

Peer fix 0618434 retries NoRoute/Stranded walking guests without relying solely on their
old Heading/Leaving intent. Resetting a plan alone did not reset the walk state, and a
failed attempt could already have changed the intent to Wandering. The peer owns this
core change; the independent helper/runner integration is astraclaw's work.

## Test correction and discrimination

The first fixture required `State == Stranded` after a whole coordinator Step. That
assertion failed on the fixed implementation, because a valid retry may already have
moved through that transient state to Arrived or NoRoute. It was over-specific, not a
reason to revert the fix. The corrected blocked-phase check allows retry states, but
requires actual movement, a genuinely absent route, no arrival at the gate, no WentHome
increment, and retained identity/needs.

The blocked-phase assertions pass on broken code too: they establish the fixture and
safety contract, not recovery. The post-repair arrival and retirement assertions are the
regression's discriminating checks. With the actual pre-fix ParkVisitors source from
6fedf61 restored locally, those **two assertions fail** while the first four pass. The
source was restored afterward. With 0618434 all six pass in all four worlds.

The matrix now requires six departure-recovery checks plus the named arrival witness;
omitting the helper cannot silently pass. The four-world result still has only the
exact known HALLOW Thrill Grill/SPACE Moon Buggies failures (raw1/matrix2). All seven
runtime scenes also pass on this integrated tree, including the74-check standing-service
phase follow-up.52 offline runner/classifier tests pass.

This pins the port's repair/retry lifecycle, not decoded PS2 pathfinding semantics.
It is not a proof of every interruption, a route-search performance claim, or evidence
that every coordinator-owned guest has a renderable body.
