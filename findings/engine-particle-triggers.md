# Engine particle trigger investigation: 83 / 97 / 99

## Scope and current status (2026-10-01)

Assigned: identify the native request sources for `83 Twinkle`, `97 KeySparkle`, and
`99 CongratSparkle`, without inventing an award/research trigger from their names.
The first renderer implementation is **WIP, not ready to merge**. It builds, but rendered
regressions, negative controls and containment of the broader-child hazard below are pending.

## Actual inventories

The in-memory disc inventory opened **16 WADs, zero failed archives**:

- **321 non-alias .sam files**: no parsed field or raw assignment names/numeric values
  matching 83, 97 or 99. Raw assignments were checked as well as last-wins parsed fields.
- **29 non-alias .sce files**: the same field/assignment search found zero matches.
- `Tp2.plb`: **105 particle records plus 20 attractor records**. Incoming child, emitter-expiry,
  particle-death and attractor-expiry links were checked. 97/99 have no incoming link in these
  shipped tables; 83 has the concrete child sources below.

Logs: `/tmp/tpw-engine-trigger-inventory.log` and `/tmp/tpw-engine-trigger-ui-inventory.log`.
No extracted asset was written to disk. These are derived inventories, not a retail playthrough.
The 171-script negative census is cow tools' earlier work, not independently repeated here.
A literal-only script census would not exclude dynamically computed effect operands.

### Immediate particle children (all eight parent records)

| Parent | Child | Attach | Dies with parent |
|---|---|---|---|
| 4 ExplodeFirey | 5 | yes | no |
| 51 Repair | 54 RepairData | no | yes |
| 60 FW1explosion | 63 LaserRing | yes | no |
| 62 LaserFWexplode | 63 LaserRing | yes | no |
| 75 Destroy1 | 83 Twinkle | yes | no |
| 76 Destroy2 | 83 Twinkle | yes | no |
| 77 Destroy3 | 83 Twinkle | yes | no |
| 78 Destroy4 | 85 TwinkleSm | yes | no |

Separate **attractor-pool** children occur on 84, 86, 90 and 98. Those IDs must not be
looked up as particle IDs. Twinkle83 itself has particle-death effect **84**; that is not an
immediate child and this first patch does not implement it. Key87 has emitter-expiry attractor4,
not a demonstrated KeySparkle97 link.

## Native immediate-child request

Both spawn APIs `0x18B5A8` and `0x18B0F8` read the parent child ID at `+0xB4`, the pool byte
at `+0xBA`, and the attachment flag at `+0xB8`. A negative child ID means none. A direct
self-reference is refused. The particle branch recursively calls the **non-directional**
`0x18B5A8`, even when its parent used the directional API.

Selected MIPS confirms the kind-1 offset and call:

- `0x18BA1C..0x18BA5C`: when attached, read the **child template's** `+0x2C/+0x30/+0x34`,
  shift each left four and add to the incoming position components.
- `0x18BA74..0x18BA9C`: compare the parent's original child ID to its own ID; recurse at
  `0x18BA88`, or store -1 for self-reference.
- Raw Destroy75/76/77 child83 velocity words are `(0,625,0)`. Against position inputs in
  10240ths this means initial displacement **(0,625/640,0)** cells: +0.9765625 Y.

The backend previously never read `ChildEffect`. The WIP `ParticleSpawnLinks` resolves the
particle-pool request/initial offset, and `RideParticles.Emit` dispatches it after parent creation.
It does not inherit directional or persistent-parent arguments. Probe modes deliberately remain
single-effect motion instruments. An extra recursion guard protects malformed indirect cycles;
that is host safety, not a claim that the native routine guards more than direct self-links.

**Not implemented by this patch:** attractors; particle death/emitter expiry; ongoing attachment
following; parent/child lifetime coupling; the other emitter fields. In particular, creating
Twinkle83 does not establish the complete Twinkle lifecycle or its retail appearance.

## Review finding which blocks a generic rollout

The eight-link rollout also makes child63 LaserRing reachable. The existing finite renderer
uses `ExpectedTotal()` without a simultaneous-live cap. Raw child63 has burst1, rates -5,
emitter life20, particle life20, max-live1: the renderer plans **five** particles, although the
native live cap is one. Several overlapping large ring sprites are a concrete regression risk.

This is not a child-ID resolution error. Before merge, either contain this first rollout to the
audited Twinkle request or prove a narrowly correct finite-emission/cap correction. Do not
blindly clamp every total to MaxLive: an effect with turnover can emit more over its lifetime.
Rendered parent/child placement, finite/continuous behavior, guard cleanup and map-clear tests,
plus compiled missing-child/wrong-offset controls, are still required.

## 97 / 99: no identified trigger, not declared retail dead code

Focused native follow-up found:

- `0x13FD00` is a UI emitter-count manager. MIPS forwards its third argument unchanged to
  the spawn API. All eleven raw J/JAL callers supply only 0x31, 0x2C, 0x23, or zero for removal.
- The raw executable has fifteen J/JAL call sites to `0x18B5A8`, one to `0x18B0F8`.
  Direct fixed requests use 0x57, 0x2C, 0x32 or 0x13; none is 97/99.
- Four relevant ID getters `0x1FA8A0/A8/B0/B8` are constant-return stubs: 51,79,75,89.
  The examined high-bit selector `0x1FA8C0` returns zero; it does not select 97/99.
- Bounded raw-address and LUI/address-construction searches found no straightforward function-
  pointer alias to the spawn entries or UI manager. Encoded offsets, alternate entry points,
  arithmetic-built pointers and complex indirect paths are not excluded.
- The conspicuous 97 in `0x1476E0` belongs to a **model registry / virtual constructor**, not
  the particle namespace. It is not evidence for KeySparkle.

The dynamic route `0x1BCFA8 -> 0x1BCB08/0x1BBF28` can evaluate script variables through
`0x1BBD18`; that value-flow join remains outside this bounded pass. Save-restored live emitters
and runtime template mutation were not audited. Therefore the outcome for 97/99 is **no
identified request in the examined paths**, not proof that either effect is unreachable in retail.

Repair51 and upgrade89 callers were encountered separately; the port's hoarding-drop timing
remains prior inference, not an oracle. No timing, density, condemnation, tour, or guest-skinning
change is included in this slice.

## WIP implementation receipts (not a merge gate)

- Shipping game builds with this first immediate-child prototype (existing warnings, zero errors).
- Explicit temporary raw-record planner fixtures: **25 PASS**, including all three Destroy->83
  offsets, attractor/none rejection, child-vs-parent offset discrimination, unattached zero offset,
  and negative/self/missing child controls. These are not yet registered shipping regressions.
- No rendered test or visual/pure-input demolition replay has been run on the prototype yet.
- The LaserRing/live-cap review issue above remains open; **do not merge this prototype**.
