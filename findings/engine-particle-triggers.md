# Engine particle trigger investigation: 83 / 97 / 99

## Scope and current status (2026-10-01)

Assigned: identify the native request sources for `83 Twinkle`, `97 KeySparkle`, and
`99 CongratSparkle`, without inventing an award/research trigger from their names.
The renderer rollout is now explicitly limited to **metadata-requested child83**. On the
shipped table its parents are 75/76/77. Other immediate children remain OFF; this is not generic
ChildEffect support. The clean final gates below passed; the bounded patch is ready for staged review.

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

The backend previously never read `ChildEffect`. `ParticleSpawnLinks.TryParticleChild` resolves
the native particle-pool recipe; `TryTwinkleChild` deliberately selects only requested ID83 for
this rollout. `RideParticles.Emit` dispatches that request after parent creation, with neither
parent direction nor parent persistence inherited. Probes remain single-effect instruments.
The native direct-self guard plus this single-target policy prevent recursion cycles; the earlier
generic prototype's extra recursion guard was removed. The parent API still returns the parent;
`Spawned` counts the actually created parent and child nodes.

**Not implemented by this patch:** attractors; particle death/emitter expiry; ongoing attachment
following; parent/child lifetime coupling; the other emitter fields. In particular, creating
Twinkle83 does not establish the complete Twinkle lifecycle or its retail appearance.

## Review finding which blocks a generic rollout

The eight-link rollout also makes child63 LaserRing reachable. The existing finite renderer
uses `ExpectedTotal()` without a simultaneous-live cap. Raw child63 has burst1, rates -5,
emitter life20, particle life20, max-live1: the renderer plans **five** particles, although the
native live cap is one. Several overlapping large ring sprites are a concrete regression risk.

This is not a child-ID resolution error. **Containment was chosen:** only child83 is enabled;
4->5, 51->54, 60/62->63 and 78->85 remain OFF and are tested through actual renderer calls.
No count/cap/density/scheduling correction is included. ExpectedTotal is the port's unvalidated
rate accumulator, NOT an observed console emission count. The console's actual births for63
require a separate boundary/scheduling trace. Do not blanket-clamp a lifetime total to MaxLive:
a turnover effect can emit more over its lifetime than its simultaneous-live cap.

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

## Registered regression and executed controls

- `ParticleChildChecks`: **236** raw-template/native-ELF assertions, including all105 generic
  recipes, the explicit Twinkle-only policy, child-vs-parent offset controls, unsupported/pool/
  self cases and both native spawn APIs. Integrated into the default core audit with a236
  coverage floor and semantic witnesses. The earlier temporary25 checks are historical only.
- `ParticleChildSpawnAudit`: **62** declared component assertions. It uses real library art,
  attached holders, actual CpuParticles3D nodes, read-only observers, labelled raw clones,
  normal `_Process`/timers and queued teardown. It does not use a Viewer or load a park.
  Geometry/material/sprite/visibility receipts plus completed frames are NOT pixel review.
- `viewer_matrix --standalone-case particle-child` runs640x360 and1152x648, expects ZERO map
  lines only for this named no-Viewer fixture, exact PASS, sequential62 IDs and semantic
  witnesses. Research still requires two maps; ordinary cases still require one. A pre-existing
  malformed-extra-map hole in ordinary classification was also closed, with a negative control.
- Development registered run: **62 PASS at both resolutions**, no error/leak lines. Python
  tools: **126 PASS** after parser/launch/scheduler/family controls. This was a dirty development
  tree; final clean manifests, not that run, are the landing record.
- **Nine compiled mutations rejected:** parent-owned offset(8 core failures), self allowed(2),
  wrong pool(5), generic policy(7); missing child, wrong unit, wrong Z, inherited direction/
  persistence and LaserRing enabled all fail the rendered fixture. LaserRing control was rerun
  after putting the actual Emit/node-count check before its policy assertion: it fails at
  `Emit60 introduces no child`, not merely at a metadata query. Sources restored exactly and
  both projects rebuilt. Logs/JSON: `/tmp/tpw-child-mutant-*`, `/tmp/tpw-child-mutations.json`.
- Actual default family-call omission: compiled JUNGLE/1 exits0/PASS, but the gate rejects it as
  missing_coverage with particle_child count0. Program was restored byte-for-byte and rebuilt.

```sh
dotnet run --project tools/TPW.PS2.ParkSimAudit -- "$DISC" --particle-child-only
python3 tools/audit_matrix.py --disc "$DISC" --out /tmp/child-core-NEW
python3 tools/viewer_matrix.py --disc "$DISC" --godot "$GODOT" \
  --standalone-case particle-child --out /tmp/child-rendered-NEW
python3 -m unittest discover -s tools -p 'test_*.py'
```

The hosted CI has no disc/Godot; its disc-free controls do not constitute automatic rendered
coverage. Full-world save/load remains outside this work. No retail capture or pure-input player
withdrawal/demolition replay has been compared for this patch. Twinkle's death chain and the
other decoded-field leads remain open, not silently marked finished.


## Final clean gate record

All gates ran at clean implementation commit **7fc717f** after restoring the compiled controls.
The following update is documentation-only; no production/test/parser source changed afterward.

- `/tmp/tpw-child-final-core`: full eight-park core audit **baseline retained**, four JUNGLE/
  FANTASY passes and four exact known HALLOW/SPACE retail failures (raw1, matrix2). Every case
  records **236 particle_child checks**; no new failure is accepted as a retail exception.
- `/tmp/tpw-child-final-component`: named rendered fixture **62 PASS at 640x360 and1152x648**,
  zero map witnesses as declared, no errors/leaks, fresh build/source/assembly guards active.
- `/tmp/tpw-child-final-regression`: ordinary pointer **13** and mechanic **48** pass on both
  JUNGLE/1 and HALLOW/2, with exactly one correct map each. This is four selected cases, NOT
  a new full128-case viewer run on this branch.
- `/tmp/tpw-child-final-research`: existing research component **24 PASS at both resolutions**,
  exactly two JUNGLE/1 witnesses each. The standalone-map extension did not weaken that contract.
- `/tmp/tpw-child-final-audio`: existing audio lifecycle **38 PASS**, no error/leak output.
- `/tmp/tpw-child-final-python-run.log`: **126 tests PASS**. The sequential queue exits0 after
  requiring the core's documented baseline exit2 and exit0 from every other gate.

These establish the scoped request integration and regressions described above—not retail
birth counts, full Twinkle behavior, or an observed player-visible demolition sequence.
