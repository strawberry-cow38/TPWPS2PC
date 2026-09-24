# Coordinate-less external shops: preserve the actual arrival position

## Decision and provenance

Six shipped 2x2 shops omit both stand-coordinate keys: JUNGLE Coconut; FANTASY sBurger,
Fries and IceCream; SPACE Burger and Fries. The original absent-key default is unknown.
Selected numeric-encoding searches and executable key strings do not establish either
all possible compiled representations or a live parser/default path. We stopped that
unproductive search rather than promote a guess into a decoded counter coordinate.

**Port policy:** retain the customer at the actual arrival stub throughout accepted service,
facing inward. This preserves a position the guest really occupied, supplied by placement
and the walking coordinator. It is not the console's established default, and it does not
write made-up values into RideDefinition.Fields/Blocks or the nullable authored accessors.

Authored positions still win. The explicit fallback applies only to 2x2 selling shops with
a valid footprint entry, a supplied real entrance and BOTH raw stand keys absent. Keys in
fenced Blocks count as supplied/malformed too, not absence. Partial, malformed or non-finite
supplied values do not silently acquire this fallback. Larger shops, ordinary rides and
missing entrances remain excluded. Actual host hide/seat/WALK ownership still wins over
standing presentation; RideHandlesSprite is not substituted for real ownership.

StandingServicePose.TryCreate stays authored-only. TryCreateAtEntryStub labels its result
IsEntryStubFallback. Viewer tries authored registration first, then this policy, and draws
fallback guests from the actual coordinator plan.At rather than an invented counter point.
The existing built-park frame conversion handles position and inward facing.

## Reproduction and checks

At main6fa5c5d, normal-startup JUNGLE Coconut placement/purchase showed one live body before,
zero while accepted, and one after handback. No LIMBO/WALKON/seat/hide justified its absence.
The drink purchase itself worked: thirst91→51, toilet10→50, sickness0→10, happiness50→55,
cash1234→934, litter9→39..63. The missing-body assertion alone failed after controlling
legitimate thought recomputation. Logs/captures remain outside Git.

The fixed Coconut run keeps one body at its actual retained arrival point through service,
then restores one walker. Other named normal-startup fixtures cover the remaining five
shops and a still-authored JUNGLE IceCream control. Literal EUR profiles include SPACE
Burger's compiled happiness10 and SPACE Fries' price35; no profile is selected by similar
effect values. The fixture requires source coordinates remain absent, the explicit policy
tag, actual arrival-cell/floor alignment, an upright inward-facing body, real handback and
one transaction. Images remain a separate peer-inspection step.

The default StandingServiceAudit adds31 metadata/negative controls for all six records at
all four orientations, including parsed fenced-block malformed keys. Eight additional checks
exercise actual Viewer registration and a real Coconut script/coordinator: accepted service,
body retention, host hide/reveal, and handback. This prevents a dead registration call from
passing merely because the helper works. These eight use a controlled, unready viewer fixture;
the separate rendered smoke provides normal startup/build-menu/placement coverage.

The standing gate is now132 checks and requires the new metadata and consumer witnesses;
filler assertions cannot substitute for a removed consumer block. Source mutations and final
cross-world/render/runtime outcomes are recorded in progress.md when actually performed.

## Reproduction

```sh
TPW_PS2_DISC="$DISC" LIBGL_ALWAYS_SOFTWARE=1 xvfb-run -a -s '-screen 0 1280x720x24' \
  "$GODOT" --path game --rendering-method gl_compatibility --audio-driver Dummy \
  res://tests/ShopServiceSmoke.tscn -- --mode=park --map=JUNGLE --shop-target=coconut \
  --shop-markers --shop-shot=/tmp/new-coconut-capture/serving.png
```

Use FANTASY with targets burger/fries/icecream, or SPACE with burger/fries, for the other
five. The default icecream target remains available in all four worlds: FANTASY exercises
this policy; JUNGLE/HALLOW/SPACE retain their authored positions. Output paths must be new
and outside Git. Markers and alternate inspection cameras are diagnostic overlays/views,
not gameplay artwork or evidence of native input/camera usability.
