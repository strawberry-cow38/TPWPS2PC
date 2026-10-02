# Procedural park water implementation — WIP, not ready to merge

2026-10-02, requested by strawberry at Discord message1555509148982968411.
Based on `tinyclaw/particle-schedule`8363918 plus the research trace0051c42.

## What exists

- `core/TPW.PS2.Data/ProceduralParkWater.cs`: guarded native profile/placement, explicit native
  libc RNG, immutable 128-segment cubic vector noise, phase/UV state, N4..16 geometry plus the
  exact eight appended vertices, signed16 UV narrowing and native strip diagonals. Caller-owned
  buffers are reused. It has no Godot or asset-writing dependency.
- `ProceduralParkWaterView`: the separate alpha-bearing mesh, preserving the opaque terrain
  below it. It reflects native Z, supplies constant raw normal(0,112,0) via CUSTOM0, and updates
  existing vertex/attribute regions rather than rebuilding the mesh every render. Actual
  uploaded bounds are updated along with geometry. It uses the existing PS2 lighting/material
  translation, not the obsolete travelling-sine uniform.
- `Viewer.ParkWater`: creates the drawable on terrain load, hides/queues the old one on map
  switch, and drives it after the camera update. Its art is explicitly loaded from
  DATA.WAD `/Generic/extra/justwater.ssh`; the ordinary `TextureNear` API searches the active
  world and correctly could not find this global asset. That failed first component run was
  corrected in the production loader as well as the fixture, not hidden by fixture substitution.

## Important translation limits / choices

The native water consumer uses the gated real-time clock `0x2F07B0`, sampled in10ms steps
(`0x220C78`), **not** fixed simulation D. The view retains sub-10ms time and discards elapsed
paused time. Rendering/projection/clip-Z are translated through Godot; full native VU/GS
rasterization and camera parity are not claimed.

The core receives an explicit RNG seed. The viewer currently uses a reproducible separate
stream beginning at1 with native LCG arithmetic and consumes the second table's384 calls
after generating the water table. The actual game's startup seed and intervening global RNG
consumers have not been replayed. Consequently **exact emulator phase/noise history is not
claimed**. The motion family, cubic generation, native constants and placement are decoded,
not fitted wave parameters.

EE `cvt.w.s` bounded conversion is toward zero; checked against PCSX2's primary interpreter
implementation (`pcsx2/FPU.cpp`, `CVT_W`), alongside the actual conversion instructions in
the owner's executable. Exceptional/saturating EE float behavior is outside the core's bounded
finite-input contract.

The global curve is reused between map loads; each drawable gets fresh phase/UV state. The
current A/B switch `TPW_NATIVE_PARK_WATER=0` omits only this new drawable; it does not replace
terrain art or enable a guessed shader wave.

## Checks completed so far

- Targeted pure-data audit: **76 assertions PASS**, exposed via `--procedural-water-only` and
  called by the default audit. Independent literal oracles cover parameters, eight placements,
  LCG values, cubic samples, phase threshold/overflow, signed UVs, skirts, topology, buffer
  contracts and the actual ELF interpretation guard. Coverage-floor wiring is still pending.
- Game build: clean, zero errors.
- Rendered component at640x360: **25 numbered checks PASS**, exit0, no exit leak/error warnings
  (only the usual unsupported-VSync notice). It verifies actual mesh-region updates through
  `SurfaceGetArrays`, changed Y with fixed X/Z, signed V change, preserved CUSTOM0 normals,
  reused mesh/buffers, pause/remainder behavior and LOD arithmetic.
- Transparency is an **actual raster control**, not just a shader-string check: the same water
  over an artificial red vs green underlay produces center pixels
  `(0.5686,0.1412,0.2078)` vs `(0.0078,0.6980,0.2078)`. Blue establishes water is drawn; changing
  the background through it establishes blending. This is a declared component fixture,
  **not a player-path or emulator comparison**.

Logs: `/tmp/tpw-park-water-core-initial.log`,
`/tmp/tpw-park-water-component-640.log`, `/tmp/tpw-park-water-component-640.png`,
`/tmp/tpw-park-water-game-component-build.log`.

## Must finish before handoff

1. Actual Viewer boot/load, both resolutions, all eight parks: prove the shipping call creates,
   advances and renders the water, not just this independently constructed component.
2. Map switch/lobby/pausing/cleanup assertions; old instance and state must not carry over.
3. Register strict numerical/semantic coverage floors and rendered cases, with missing-call
   controls. No green raw exit accepted when coverage is absent.
4. Compiled motion/UV/alpha/placement/reset mutations, restoring every source afterward.
5. Full baseline/regression gates and a bounded performance comparison. No other agents'
   renders/services may be touched. Build only after own engines have exited.
6. Read/update these limits and the research note after testing; no emulator pixel-parity claim
   or assets committed. Coordinate current staging history before final handoff.