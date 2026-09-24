# Shared activation serial and departure-phase provenance

September24,2026.1093B0 copies word2AA73C to object+0C, then increments the global
in return-delay store1093C4. For a guest O=N+8, serial isN+14. File value is0;
that is executable-load initialization, NOT a proved per-park reset or first-guest
value. Signed/unsigned interpretations give the same masked low6 bits.

## Positive consumers; do not count constructors or meshes

Direct callers in bounded text[100000,2A420C):114C38,15E290,19136C,1E0F50.
- Balloons: named PoolOfBalloons35FEF0, pool3952C4, ctor114B98 at148AEC,
  vtable35A2F8 activation114C20 through14AE64 ->1093B0.20 preconstructed pool
  slots are NOT20 activations.
- Litter: PoolOfLitter35FEE0, pool3952C0, ctor15E1C8 at148A3C, table361038,
  activation15E280 through14ADAC ->1093B0.40 slots are NOT40 activations.
- Guests:16B5C0->14AC48->14ACE0 virtual36CCE0+34 adjustment-8 ->20BCD0
  ->191360->1093B0. One per activation, including reuse, not per render appearance.
- Guards:140600->1DB618->191360->1093B0. Other staff activation callers of
  1DB618 include entertainers12E0A8, handymen144C90, mechanics1783A8 and
  researchers1B5FC0, tied to named pool construction. Five guard slots do not
  imply five guards activated at park start.
- Placed objects: common1E0F30->1093B0, reached through concrete virtual base
  initializers (e.g.149FF0->1B7A98->1B7C04). Ten placed-family tables include
  features/shops/rides/sideshows/coasters/tours/track objects. Composite rides can
  initialize separate parts; do not assume one serial for the whole composite.

1093B0 also appears as an inherited activation target in13 tables, including
logical entrance table360370 and coaster-track family365510. Table membership
is not proof of an invocation. The inspected logical gate construction154878
uses109308, not an established activation. Bus creation149C90->1476E0 uses a
model-interface lifetime, not this allocator. Do not add a bus-trip or gate-mesh
increment. No positive UI-widget activation consumer was found in this census;
porting a full UI is not a demonstrated prerequisite for departure phases.

## What is and is not known about the starting value

The direct/immediate and bounded constant-propagation scans recovered1093B4 load
and1093C4 store, no other resolved writer/reset. This does NOT exclude aliases,
bulk restores or all indirect paths. Startup versus saved-game populations,
previous parks, temporary activations and every inherited activation remain
unclosed. Current live-object count cannot reconstruct that history.

The research Viewer uses NativeActivationSequence with the owner's ELF seed and
explicit **represented port activation history** provenance. Actual placed objects
and bus-born guests consume that shared sequence; lookup/rendering/route retries
and bus trips do not. It persists across park resets in the same Viewer. Unported
staff/litter/balloons/composite pieces and original startup/restore history mean
this is NOT a verified native phase origin. The port's executed-tick origin is
also not a native process snapshot. Neither fact licenses Guest.Id as the serial.

A verified source history or counter/individual-serial snapshot could replace
that origin later. Constructor requires an explicit origin; diagnostics and
rendered tests assert the current history is unverified, rather than hide a seed
inside the departure state machine. Two real placements before bus birth provide
a rendered discriminator: the departure serial differs from the guest-only ID.
