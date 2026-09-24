# External shop customers: live handover, not only purchase arithmetic

## Reproduced boundary

At baseline6d65a89, normal Viewer startup, the actual Shops menu/PlaceHeld callback and
production park ticks reproduce the JUNGLE IceCream customer's missing body. The decoded
script has neither LIMBO nor WALKON. After acceptance, the coordinator owns the identity,
no host-hide/seat/WALK applies, but the live actor count is1 before /0 serving /1 after.
The real transaction still succeeds: hunger91→66, thirst0→5, toilet10→35, sickness0→10,
happiness50→55, cash1234→934, litter9→39..63. Arithmetic-only checks cannot see this defect.

`ShopServiceSmoke` keeps normal Viewer initialization and scene ancestry. It disables
automatic arrivals and Viewer._Process after startup, seeds one controlled hungry customer,
freezes rise/queue timing, and manually steps real TickPark/PresentScripted/PlaceActors.
Construction uses the real build list, blueprint and cursor-cell override, not a fake ride.
This is deterministic callback/render coverage, not physical-input or real-time-frame testing.

The first run also had a fixture error: presenting the guest legitimately recomputes its
thought. The repaired baseline preserves all seeded values except that it requires the real
Hungry thought; that leaves precisely the missing-body failure while handback still passes.
The smoke defers presentation failures until it has captured and checked handback, rather
than stopping before observing the completed purchase. Before/serving/after share one camera.

## Bounded fix

`StandingServicePose` now accepts either 1x1 relief facilities or **2x2 selling shops**, still
requiring a real entry and finite authored stand coordinates. Row mirroring, quarter-turns,
entry-cell height and inward facing use the existing placement convention. `StandingRiders`
still gates on the actual coordinator instance and actual hide/seat/WALK ownership. Authored
RideHandlesSprite is not used as a substitute for what this port actually draws.

Larger shops do not receive this fallback. Six 2x2 shops lack authored coordinates and remain
unsupported here; we do not silently turn an invented coordinate into an authored value.
FANTASY IceCream is one of them and the bounded smoke reports that limitation explicitly.
Missing coordinates and script hiding are independent questions, not interchangeable gates.

The default headless standing scene now has89 assertions, including literal 2x2 point and
height-cell expectations at all four turns, independent placement-facing checks, and negative
controls for coordinate-less shops, larger LIMBO shops and ordinary rides. The runner requires
those witnesses, not just a larger check count. Existing toilet ownership/host-hide/seat/WALK/
removal/real-placement tests remain in the same scene.

The rendered smoke checks live non-deleting actors after drawing, requires torso and legs
separately and one actor for the identity, and validates its actual accepted position/height/
upright inward basis against literal per-world points and the placement API. The actor's
transform is not accepted merely because a helper or camera calculated the right point.

## Evidence and limits

The fixed production code passes rendered service/handback in JUNGLE at all four turns,
and HALLOW and SPACE at the first fitting turn. Initial scene-graph coverage was tightened
by independent review: one non-head mesh is not proof of both torso and legs, and being on
camera is not proof of correct position/facing. The strengthened version has been rerun in
JUNGLE; subsequent final reruns are recorded in progress.md. Rendered captures require direct
inspection for counter alignment, occlusion and appearance; scene visibility cannot prove it.

HALLOW and SPACE use their own authored ice-cream coordinates (.4/.5 and .7/.7), not
JUNGLE's .6/.4. FANTASY does not participate because its coordinates are absent. No claim
of all-turn/all-shop/all-world rendered coverage, complete queues, or original costume/
carried-object presentation is made. Software OpenGL/Dummy audio are not native Windows
or audible-output qualification. Captures stay outside Git; disc data is read in place.

Example (new directory and filenames required):

```sh
TPW_PS2_DISC="$DISC" LIBGL_ALWAYS_SOFTWARE=1 xvfb-run -a -s '-screen 0 1280x720x24' \
  "$GODOT" --path game --rendering-method gl_compatibility --audio-driver Dummy \
  res://tests/ShopServiceSmoke.tscn -- --mode=park --map=JUNGLE --shop-turn=0 \
  --shop-shot=/tmp/new-shop-capture/serving.png
```

The scene also writes `serving-before.png` and `serving-after.png`. It refuses existing paths,
headless rendering and output under Git/symlink ancestry. `--shop-turn=0..3` pins orientation;
omitting it takes the first fitting one. Without a fresh Debug build, a stale assembly is
not evidence about current source.
