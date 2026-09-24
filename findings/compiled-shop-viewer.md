# Compiled shop settings: the viewer must actually attach them

Peer f96dde8 added an identity-based compiled-data lookup and compiled-first definition
getters. Its helper checks passed, but the viewer called AttachCompiledRecords between
`new RideCatalogue()` and `AddWad`: the attachment loop saw an empty catalogue. A correct
lookup was not evidence that a live definition used it.

`game/tests/CompiledShopViewerAudit.tscn` calls the actual Viewer.IndexRides and DefinitionFor
methods with real disc data. Before the fix it reported zero attached shops in every world
and failed10 of18 checks. Named-definition and authored-value controls passed, so this was
not a fixture selecting the wrong asset. Peer d6f4e75 moved attachment after population,
shared the loop through CompiledAssets.Attach, handled both archive-qualified and bare-world
Source paths, and made an empty input explicitly report NOTHING TO JOIN.

The restored18 numbered checks pass:

- Every populated shop gets compiled settings in JUNGLE, HALLOW, FANTASY and SPACE.
- DefinitionFor selects named Balloon, VampShop, FatFairy and Droid definitions.
- Authored happiness controls remain15/15/10/15, while live getters use compiled10.
- Price/cost are45/30 for the first three and50/35 for Droid.
- Re-indexing replaces the catalogue and returns to the correct JUNGLE definition instead
  of retaining SPACE or silently returning authored15.

Three production mutations are rejected: attachment before population (10 failed assertions),
authored-first happiness (4), and retaining the old catalogue (1). Source was restored.
The scene is part of runtime_audit.py's default **eight-scene** gate, with sequential IDs,
matching18-check summary and named world/re-index witnesses. All eight pass;54 Python
runner/classifier tests pass. The park matrix retains only its two exact retail reds.

## Boundary

The fixture uses an unready Viewer with a live audit-stage UI tree; it invokes the real
catalogue/definition consumers but does not claim normal startup, physical interaction,
rendered shop purchases or visual sign-off. Those are distinct from the normal-startup
small-toilet smoke. The viewer currently selects arsdb/EUR explicitly. This scene verifies
that selection, not an unimplemented runtime region selector.

Independent all-region named-record and purchase-effect controls remain the next step.
Particular discriminators are the four ice-cream records whose EUR/JAP hunger/vomit25/10
differ from USA15/15. The lookup's treatment of duplicate symbolic identities also needs
its own contract check: the original numeric-key first-match behavior is not automatically
proof about symbolic-name collisions. Finally, compiled happiness is a base; the port's
unmodelled shop-quality multiplier must not be silently claimed as retail outcome parity.
