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

Independent all-region named-record and real purchase-effect controls now live in
`CompiledShopPurchaseChecks`; see [compiled-shop-consumer.md](compiled-shop-consumer.md).
They retain the four ice-cream EUR/JAP hunger/vomit25/10 versus USA15/15 counterexamples,
product selection, actual ×10 cash/affordability and verified initial quality defaults.
This does not implement mutable quality or the original carried-object/willingness behavior.
The lookup's treatment of duplicate symbolic identities still needs its own contract review:
original numeric-key first-match behavior is not automatically proof of symbolic collisions.
