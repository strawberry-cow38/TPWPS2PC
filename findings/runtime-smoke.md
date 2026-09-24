# Current Godot runtime smoke evidence

Measured 2026-09-23 US Eastern (runs span September 23–24 UTC) against `c32cb74` plus the audit-only LightingAudit selector fix.
Engine: `4.6.stable.mono.official.89cea1439`; project SDK target 4.6.2, Debug build.
This records tested behavior, not blanket version compatibility or visual sign-off.

## Scene checks

The actual compiled project loaded and ran these Godot scene audits headlessly:

* VisitorAudit: six named world/terrain scenarios (FANTASY, SPACE, HALLOW, two each),
  geometry/identity/position/facing/visibility and live APS bindings passed.
* RseAnimationAudit: script-selected records, geometry/frames and departure checks passed.
* TextureAnimationAudit: 146 surface checks across five models/four worlds, plus actual
  viewer-clock advance/pause/wrap/cache binding checks passed.
* MtrAudit: all four named legacy surface/transform witnesses passed.

All four exited zero with their explicit PASS markers and no ERROR/SCRIPT ERROR lines.
LightingAudit is a framebuffer test and was run with actual display/rendering backends,
not claimed as a headless pixel pass. Its stale material selector was corrected; see
`lighting.md`. Both Compatibility and Forward+ software rendering passed all 12 pixel
probes, and the existing deliberately broken shader was rejected at its expected pixel
mismatch before the restored shader passed again.

## Main viewer: real JUNGLE guest smoke

A separate actual viewer run used Xvfb, Compatibility/OpenGL llvmpipe, Dummy audio and
1280x720 output, with `--mode=park --map=JUNGLE --guest-test` and `--shot`.
It exited zero without ERROR/SCRIPT ERROR lines. Instrumented observations:

* At 60 simulated seconds: 28 cumulative boardings, 8 completed returns, 20 queued/
  riding, 8 walking. Eight occupied seats were reported on Crazy Ape.
* At 180 seconds: 68 cumulative boardings, 40 returns, 28 queued/riding, 0 walking.
  These totals are consistent with 28 identities; repeated rides increase cumulative
  boardings/returns, not the population.
* Needs/bubble instrumentation loaded 16 of 16 bubble resources, reported an attached
  visible/in-tree Hungry bubble, and reported happiness 58 at the 60-second census,
  consistent with the configured +8 completion effect from spawn happiness 50.
* The later “nobody in the park” needs log is a walking-only census; the ride census
  still reports 28 owned identities. It is not evidence those guests disappeared.
  The maintainer subsequently corrected the census wording in `2802335`; the counts
  above remain the observations from this particular capture, not a cross-run replay claim.

Four 1280x720 PNGs were generated: `jungle-w.png` (walking close-up), `jungle-a.png`
(first ride stage), `jungle.png` (later park stage), and `jungle-c.png` (ride close-up).
Their headers/dimensions were checked. They have **not received direct visual review**;
resource/visibility logs do not establish bubble framing, occlusion or readability.
Dummy audio also means this is not an audible playback check. No screenshot is offered
as proof of graphics correctness merely because it was generated.

Local evidence locations are recorded by project scratch artifacts:
`tpw-scene-regression-c32cb74/manifest.json`, `tpw-lighting-artifact-path.txt`, and
`tpw-current-guest-smoke-path.txt` (the last points to captures, viewer.log and captures.json
under a unique /tmp directory). No disc, extracted game payload or image was committed.

## Reproduction

```sh
export TPW_PS2_DISC="$DISC"
dotnet build game/TPWPS2Viewer.csproj
"$GODOT" --headless --path game res://tests/VisitorAudit.tscn
"$GODOT" --headless --path game res://tests/RseAnimationAudit.tscn
"$GODOT" --headless --path game res://tests/TextureAnimationAudit.tscn
"$GODOT" --headless --path game res://tests/MtrAudit.tscn
# Existing lighting harness requires a fresh artifact directory outside Git.
LIBGL_ALWAYS_SOFTWARE=1 xvfb-run -a python3 tools/lighting-check.py "$DISC" "$NEW_OUT" --godot "$GODOT"
LIBGL_ALWAYS_SOFTWARE=1 xvfb-run -a -s '-screen 0 1280x720x24' "$GODOT" \
  --path game --rendering-method gl_compatibility --audio-driver Dummy --resolution 1280x720 \
  -- --disc="$DISC" --mode=park --map=JUNGLE --guest-test --shot="$NEW_CAPTURE_DIR/jungle.png"
```

Neither the main smoke nor these scenes replace the whole-park matrix: the exact
HALLOW Thrill Grill and SPACE Moon Buggies retail findings remain explicit there.
Facility routing/want satisfaction and native Windows/manual release gates remain
separate unfinished work; do not infer them from successful ride/scene execution.

## Follow-up audio/runtime boundary gate

`audio-lifecycle.md` records 37 synthetic actual-player/reset checks on `8889f2d`,
seven rejected mutations, and a separate real JUNGLE60s census on `4ca1a41` with
22 cues/22 resolved/22 started, zero unresolved/silent. This does not change the
visual-review or audible-output limitations above, or certify full world-swap stability.

## Follow-up advisor browser gate

`advisor-browser-audit.md` records 45 actual-control callback/metadata/PCM/lifetime
checks and six rejected mutations on production `6aacf63`. This is deliberately not
Viewer startup, physical input, layout/glyph inspection or a park-driven advisor.

## Reproducible headless runner

`tools/runtime_audit.py` now fresh-builds and runs the six headless-safe scenes together,
recording named coverage, raw statuses and source/dependency/log fingerprints. See
`runtime-audit-runner.md`. All six pass in the measured combined run; a selected subset
is explicitly not a full-gate pass. Lighting/framebuffer and listening gates remain separate.
