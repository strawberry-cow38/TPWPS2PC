# Ride audio evidence and world-effect lifetime

Measured September 23 US Eastern / September 24 UTC, 2026. Godot
`4.6.stable.mono.official.89cea1439`, project SDK 4.6.2, Linux aarch64,
Dummy audio. Production baseline `8889f2d`; new audit lives in
`game/tests/RideSoundLifecycleAudit.cs` and its matching scene.

## What the 37 checks establish

The audit generates a short PCM sine wave and a looping variant in memory. It uses
actual Godot 2D/3D audio players through the private `RideSounds.Start` boundary
(reflection, deliberately coupled to that implementation). No disc is required and
no proprietary audio is extracted. It covers:

* Natural mixer `Finished` before the first observation, for both player types.
  A false first `Playing` observation must not invalidate positive completion evidence.
* Actual later playback-position advancement after an initially stopped poll.
* No-evidence voices stay pending through eight fast polls and 0.488 elapsed seconds,
  then fail after 0.508 seconds. Both 2D/3D stopped controls are tested. A separate
  recorded-acceptance control injects only `PlayingAt1=true`: this is a controlled
  diagnostic input, not a claim that a real player remained accepted but unconsumed.
* Kill scopes to the actual ride/tag identity, calls the correct ending once, and
  leaves other voices alone. Drop/Clear do not spawn end effects. Fade stays live,
  reduces volume before ending, preserves another ride and another tag, and ends once.
* Actual `Viewer.MakePathTool` reset clears old sounds and releases their manager;
  retains the global particle-library holder but clears live park particles.
* Deferred player deletion and eventual native stream reference counts returning to
  their pre-playback baseline. Mixer retirement is not assumed after two render frames.

The reset fixture deliberately does not enter Viewer._Ready: it supplies an unready
Viewer with a live synthetic voice and inert particle instance, then invokes the real
reset method. It isolates that boundary; it does not simulate a full initialized
world swap, test every terrain, or establish long-run memory stability. Viewer-owned
roots are parented explicitly and freed in finally. Synthetic fixture objects are
cleaned even when reset assertions fail.

A read-only subagent review found that aggregate voice counts alone could pass while
removing the wrong voice, and that the original fade check could pass an immediate
Kill. Identity/callback checks, a different-tag control and intermediate volume/liveness
checks now reject those mistakes. Exception-path Viewer cleanup was strengthened too.

## Regressions reproduced before peer fixes

`PlayingAt1 && (Finished || position)` produced three failed assertions: short 2D,
short 3D and late-start evidence. Peer fix `1a5d2ab` makes Finished OR position evidence
sufficient. It does not treat acceptance alone as consumption.

An eight-frame timeout produced six pending-window failures. Peer fix `4ca1a41` keeps
positive evidence immediate and waits on elapsed time otherwise, using the explicitly
named **0.5-second diagnostic policy**, not decoded retail timing. A paused native
player was rejected as a fixture during development because pausing did not provide
the intended accepted/no-position state; those fixture failures are not product bugs.

Viewer retained its world-specific sound manager across resets. Peer `9acf578` adds
Clear plus nulling. It also retained the global particle holder's *live instances*;
peer `8889f2d` adds particle Clear without discarding the reusable library holder.
The particle reset probe failed before this second change and passes after it.

## Deliberately broken controls

On the final audit, seven isolated temporary source mutations all build and exit 2
with assertion failures rather than reflection exceptions:

| Mutation | Failed assertions |
|---|---:|
| Require first-poll Playing evidence again | 3 |
| Return to eight-frame observation deadline | 6 |
| Declare every judged voice started | 3 |
| Kill the adjacent tag instead of the requested one | 4 |
| Implement Fade as immediate Kill | 2 |
| Omit Viewer sound cleanup/reset | 1 |
| Omit Viewer live-particle cleanup | 1 |

Original production source was restored and rebuilt: all 37 assertions pass, exit 0.
No mutants are committed. Local logs/manifest: `tpw-audio-mutations/manifest.json`
in project scratch. The normal run has no Godot ERROR/SCRIPT ERROR or resource-leak
warnings. Build still has existing nullable/obsolete/unreachable-code warnings; this
is not a warning-free build claim.

## Actual disc-cue census, separate from synthetic tests

Our actual JUNGLE viewer census on `4ca1a41`, 60 simulated seconds, Xvfb/Mesa
Compatibility rendering and Dummy audio, exits 0: **22 cues, 22 resolved, 22 voices
judged started, zero unresolved/silent, one live at the census**. No ERROR/SCRIPT ERROR
lines. This exercises real cue lookup and in-memory clip decoding rather than the
private synthetic Start boundary. Local artifact pointer:
`tpw-sound-census-fixed-path.txt`.

The peer observed 21/22 before the elapsed-window fix and 22/22 after it; our earlier
run already saw 22/22. That variability is consistent with the frame-sensitive fault,
not a reason to invent a reproducible failing census we did not observe ourselves.
The independent synthetic fast-poll controls reproduced it deterministically.

Dummy playback position/Finished evidence is **not an audible quality test**. No
claim of device output, correct retail attenuation, sample-rate parity, variant
threshold semantics, engine-layer completeness or full-world teardown coverage follows.
See `sound.md` for unresolved decoding/policy boundaries.

## Reproduction

```sh
dotnet build game/TPWPS2Viewer.csproj
"$GODOT" --headless --audio-driver Dummy --path game \
  res://tests/RideSoundLifecycleAudit.tscn
# Read the owner's existing disc in place. Do not copy/extract it.
LIBGL_ALWAYS_SOFTWARE=1 xvfb-run -a -s '-screen 0 960x540x24' "$GODOT" \
  --path game --rendering-method gl_compatibility --audio-driver Dummy \
  --resolution 960x540 -- --disc="$DISC" --mode=park --map=JUNGLE --sound-census=60
```
