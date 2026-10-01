# Particle fitting lookup: the rejected calls are not a port mask bug

## Outcome and scope (2026-10-01)

**Leave the production fitting mask unchanged.** The native dispatcher passes particle space
`0x100`; its resolver applies the same mask rule as `Model.FindFitting`. The exact rejected
script/model pairs supplied by cow tools have the same result on the raw native table and in
the port. This is a native-code/raw-disc audit, **not a retail framebuffer comparison or proof
that every script site is reached in gameplay**.

The original description, "19 existing node-1 fittings with flags 0x411", was incorrect. The
independently checked population is:

| Count | Opcode / kind | Effect / node | Reason |
|---:|---|---|---|
| 15 | EVENT / 1 | 74 SideShowWin / 1 | Matching ID exists, but no matching record has bit 0x100 |
| 2 | ADDOBJ / 2 | 16 Smoke2 / 1 | Matching ID exists, with different flags, none carrying bit 0x100 |
| 2 | EVENT / 1 or ADDOBJ / 2 | See below | Requested ID is absent, not filtered out |
| 1 control | EVENT / 1 | 74 SideShowWin / 1 | SPACE strength test resolves the second same-ID record, flags 0x111 |

Cow's larger census (516 sites, 402 accepted, 19 rejected, 95 model paths not located) is the
source of this selection, **not a census repeated by this audit**. The 95 search-path gaps,
particle density, rendering, and guest animation/performance are outside this slice.

## Exact cases

All EVENT entries below are at **RSE word PC 32**, kind 1, node 1, effect 74. Models use the
same directory and stem with `.mps`; lookup is case-insensitive. Flags list **all same-ID
records in file order**, including records which the particle-space filter rejects.

| World | Script | Same-ID flags | Result |
|---|---|---|---|
| JUNGLE | /Sideshow/pong/pong.RSE | 0x411 | reject |
| JUNGLE | /Sideshow/sgpuzzle/sgpuzzle.RSE | 0x411, 0x811 | reject |
| JUNGLE | /Sideshow/sgwhack/sgwhack.RSE | 0x411 | reject |
| HALLOW | /Sideshow/pong/pong.RSE | 0x411 | reject |
| HALLOW | /Sideshow/sgpuzzle/sgpuzzle.RSE | 0x411, 0x811 | reject |
| HALLOW | /Sideshow/sgshy/Sgshy.RSE | 0x411 | reject |
| HALLOW | /Sideshow/sgstrtst/sgstrtst.RSE | none | missing ID |
| HALLOW | /Sideshow/sgwhack/sgwhack.RSE | 0x411 | reject |
| SPACE | /Sideshow/pong/pong.RSE | 0x411 | reject |
| SPACE | /Sideshow/sgpuzzle/sgpuzzle.RSE | 0x411, 0x811 | reject |
| SPACE | /Sideshow/sgwhack/sgwhack.RSE | 0x411 | reject |
| FANTASY | /Sideshow/pong/pong.RSE | 0x411 | reject |
| FANTASY | /Sideshow/sgpuzzle/sgpuzzle.RSE | 0x411, 0x811 | reject |
| FANTASY | /Sideshow/sgshy/sgshy.RSE | 0x411 | reject |
| FANTASY | /Sideshow/sgstrtst/sgstrtst.RSE | 0x411, 0x811 | reject |
| FANTASY | /Sideshow/sgwhack/sgwhack.RSE | 0x411 | reject |
| SPACE control | /Sideshow/sgstrtst/sgstrtst.RSE | 0x811, **0x111** | accept second record |

The other three rejected sites are **ADDOBJ kind 2, effect 16 Smoke2**:

| World | Script | Word PC | Node | Same-ID flags |
|---|---|---:|---:|---|
| JUNGLE | /Rides/Bumper/bumper.RSE | 151 | 6 | none |
| FANTASY | /Rides/b_drip/B_DRIP.RSE | 69 | 1 | 0x431, 0x1400031, 0x811 |
| FANTASY | /Rides/candy_c/Candy_c.RSE | 69 | 1 | 0x1400031, 0x431, 0x811 |

An earlier seven-stem selection found 19 sites: **18 rejected and one accepted**. It omitted
Bumper and included SPACE's positive control. It must not be confused with the exact list
of 19 *rejections* above.

## Native route, checked against MIPS rather than decompile alone

### Dispatcher and filter

1. `0x1BBF28` dispatches particle kind 1 and kind 2 to `0x1B9388`. Calls at `0x1BBF8C`
   and `0x1BC01C` set **t0 = 0x100 in their delay slots** (`0x1BBF90`, `0x1BC020`).
2. `0x1B9388` forwards ID a3 to a2 at `0x1B93AC` and space t0 to a1 at `0x1B93B4`,
   then calls `0x1F1F78` at `0x1B93D0`.
3. `0x1F1F78` computes:

   ```text
   mask = (space & 0x03DA1F83) != 0 ? space : 0x03DA1F82
   first record whose ID equals the requested ID and (flags & mask) != 0
   ```

   `0x1F1F7C..0x1F1F94` contains the constants, AND and conditional move. Space
   `0x100` shares a bit with `0x03DA1F83`, so the fallback is **not selected**. In particular,
   `0x411`, `0x431`, `0x1400031` and `0x811` fail; `0x111` passes.
4. Failure returns `0xFFFFFFFF` at `0x1F1FF0..0x1F1FF8`. `0x1B9388` turns that into
   false at `0x1B9460/0x1B9464`; the kind-1 and kind-2 branches skip spawning at
   `0x1BBF94` and `0x1BC024`. No alternate-space retry exists on those branches.
5. **ADDOBJ is covered separately:** its wrapper `0x1BCB08` calls the same dispatcher
   at `0x1BCB44`; kind-1/kind-2 object attachment also supplies `0x100` at `0x1BCBA0`
   and `0x1BCBBC`. The Smoke2 cases are not silently folded into the EVENT-only result.

Negative IDs or an absent model handle have a root-origin branch in `0x1B9388`.
These cases use positive IDs and resolved, initialized models; that branch is not their escape.

### Table provenance

`0x1F1F78` follows `[model+8] -> [+4]` to the model file header, reads count u16 at `+0x36`
and table pointer at `+0x74`, and searches **20-byte fitting records**, ID at `+4`, flags at
`+0`. This is not the separately allocated 24-byte runtime fitting-state table (`0x1F18E8`).

The loader/relocation route was checked as well: `0x169AA0` rebases the table pointer;
`0x169BF8..0x169C24` visits records in 20-byte steps. Its fitting initializer `0x169A78`
is a no-op. The called record relocator `0x169808..0x16982C` rebases pointer fields `+0x0C`
and `+0x10`, not flags or ID. Thus the examined initialization route does **not promote**
these raw flags into particle space. This is not a claim to have enumerated every possible
runtime memory writer in the entire executable.

The port's `Viewer.OnRideEffect -> FittingPosition -> Model.FindFitting` requests the same
space. No production resolver, renderer, emitter or script was changed in this slice.

## Regression and controls

`ParticleFittingChecks.cs` has **109 assertions**: the exact paths and literal script requests,
all matching raw flag records, the native raw-table predicate, the production lookup, the
accepted duplicate-ID ordinal, the population split, and executable instruction anchors.
Expectations do not derive from `Model.FindFitting`; raw records and literal expected flags
are compared to it. The same family is called by the ordinary audit and has a 109-check floor
plus semantic witnesses in `audit_matrix.py`.

```sh
dotnet build tools/TPW.PS2.ParkSimAudit/TPW.PS2.ParkSimAudit.csproj
dotnet run --no-build --project tools/TPW.PS2.ParkSimAudit -- "$DISC" --particle-fitting-only
python3 -m unittest discover -s tools -p 'test_*.py'
python3 tools/audit_matrix.py --disc "$DISC" --out /tmp/particle-fitting-matrix
```

Observed before integration: standalone **109 PASS**; a compiled resolver mutation replacing
space-100's mask with **0x500** produced **17 failing assertions**, exactly the 15 SideShowWin
and two Smoke2 cases. The raw native oracles remained rejected. Restoring the unchanged
resolver restored **109 PASS**. This control is not retained in production.

Final integrated standalone: **109 PASS**. Python tool suite: **89 PASS** on this Catboy base
(do not quote the larger Tinyclaw tree's suite count here). Full eight-park matrix after restoring the controls: **baseline retained**, all cases record
**109 particle-fitting checks**. JUNGLE/FANTASY's four cases pass; HALLOW/SPACE retain their
four exact known retail failures, raw exit 1 (matrix exit 2), rather than being relabelled green.

Removing the actual default `ParticleFittingChecks.Run` call, compiling Release, and executing
JUNGLE/1 produced raw **exit 0 / PASS**, but the matrix rejected it as **missing_coverage** with
particle check count **0**. The original call was restored byte-for-byte, Release rebuilt, and
the full eight-case matrix rerun. Disc-free classifier controls also reject a missing family,
one missing assertion, or any missing semantic witness despite an otherwise green output.

## Limits / remaining retail witness

The lookup result is closed: broadening the mask would depart from the examined native route.
That does **not** establish that nothing visual ever happens when a player wins: other script
animation/effects can exist, runtime reachability was not exercised here, and no retail capture
has been compared. Strawberry was asked about retail particles on these sideshows; no answer
was available when this note was written. A conflicting retail capture should reopen the
consumer/model-state investigation, not justify silently widening the mask.