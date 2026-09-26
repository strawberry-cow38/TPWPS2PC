# `Tp2.plb`: the 320-byte particle record, read from the code that runs it

`/DATA/PARTICLE.WAD` → `Tp2.plb` (SHA-256 `6564736a…3210`, 35,704 bytes) declares **105 records of
320 bytes** and then **20 records of 104 bytes**. Six fields of the 320 were known; the rest was
inferred from the shape of the numbers. This is the reading from the consumer instead, with every
field labelled `READ` (a walked instruction reads it, and the address is given) or `CANDIDATE`.
`core/TPW.PS2.Data/ParticleTemplate.cs` is the same map as code.

## Method

1. **The record is not interpreted, it is executed in place.** `0x1467b8` copies each 320-byte
   record verbatim into the template table at `0x2ce508` (105 × 320, then the 20 × 104 second
   table at `0x2d6848` — they are one contiguous block, and the save-game code at `0x146a90`/
   `0x146bd0` writes both as a single `0x8b60`-byte `PTCL` chunk). A spawn — `0x18b5a8` for
   `EVENT 1`, `0x18b0f8` for `EVENT 2` — copies the whole 320 bytes again into a live emitter
   slot at `0x2c46cc + slot*320` (119 slots) and then runs it there. **So the record IS the
   emitter struct**, runtime fields included, and the file carries stale runtime values (an old
   position at `+0x14`, list links) that the game overwrites at spawn.
2. **Walked**, with Ghidra 12.1.2 + the R5900 extension (`C:\ghidra-tools\out\FUN_*.c`):
   the two spawns; the birth `0x1888a8`; the per-particle update `0x189e78` (and its loop
   `0x189da8`); the emitter tick `0x1893f8`, its rate function `0x188428`, the emitter mover
   `0x189270`, the ring mode `0x18c120`/`0x18c058`; the density scaler `0x18aff0`; the attractor
   tick `0x189948` and attractor spawn `0x18bda0`; the master tick `0x18af90` and its caller
   `0x1f6af0`; the init `0x18a3d0` (from `0x220800` ← `0x1f6938`) and the sine table `0x1466f0`;
   the render-list builder `0x146290`, the render pass `0x220878`, the sprite quad `0x233458` and
   the display-list submit `0x22a068`; the setter API `0x18a920 0x18a9d8 0x18aa58 0x18aae0
   0x18ab28 0x18ab78 0x18abc8 0x18ac18 0x18ac70 0x18acc0 0x18ad48`; the handle check `0x18bb88`;
   the clock `0x220c78` ← timer-0 interrupt `0x225128` ← setup `0x21afc0`.
3. **Second decoder.** `tools/r5900dis.py` (capstone 5.0.7 on the 4080) was first checked on a
   function whose decompile was already held (`0x1acaf8`), then run over `0x18b0f8–0x18bb00`,
   `0x189e78–0x18a3a0` and `0x1888a8–0x1893f8`. The set of emitter offsets each function touches
   is **identical** in both readings — update `$s1`: `5 8 20 58 6f 74 78 7c 80 84 88 8c 96 98 a6
   aa be c6 128..13c`; spawn `$s1`: `0 5 6 8 a c e 12 14 18 1c 20 2c 30 34 60 6f 84 88 8c ab b4
   b8 ba c0 d0 d2 d4 128..13c`; birth `$s0`: `4 8 14 18 1c 2c..34 38..40 44..4c 50 54 58 5c 6c
   6d 74 78 a0 a8 ac c2 c4 114`. The one reading the decompiler dropped — the second argument of
   `0x188828` — is `lw $a1, 0xac($s0)` in the listing.
4. **Census.** `FindXrefs` over all 320 addresses `0x2c46cc..0x2c480b` lists every instruction
   that names an emitter field absolutely. Every referencing function is in the walk above; the
   only ones that were not already read were the setter API, which was then read. The census
   cannot see pointer-relative reads (`0x189270` takes the emitter as a pointer), which is why
   `+0x24`, `+0x28`, `+0x76` and `+0x9c` are established by reading their functions instead.
5. **Controls with a known answer, stated before looking:** a firework must be an emitter that
   RISES and then EXPLODES — Firework1 has emitter velocity (0, 109, 0), emitter gravity 2, drag
   7, life 60 and on-expiry effect 60 = `FW1explosion`, and FireworkLaser the same shape with 62 =
   `LaserFWexplode`. Mumbo's puff must be a puff — MumboPuff's rates are (20, 1, 1, 1). A death
   effect must belong to a shell — Firework2's is 7 = `Explode3`. The ramp must end at zero alpha
   — read as the consumer reads it, 71 effects do.

## The emitter record

Offsets are into the 320-byte record; the live emitter at `0x2c46cc + slot*320` has the same
layout. "signed rand" is `seed = seed*0x343fd + 0x269ec3; (seed >> 16) % n` with an ARITHMETIC
shift (`sra` at `0x18b7cc`), symmetric in (−n, n); "one-sided" masks `& 0x7fff` first.

| off | type | label | field | read by |
|---|---|---|---|---|
| 0x00 | s16 | runtime | state, 1 while active | spawn, `0x146290`, `0x188428` |
| 0x02 | s16 | runtime | paused: tick and update skip the emitter | `0x1893f8`, `0x189da8`, `0x189948` |
| 0x04 | s8 | READ | attractor class, copied to particle `+0x2a` | `0x1888a8`, `0x189948` |
| 0x05 | u8 | READ | screen-space effect (2D projection, no bbox) | `0x146290`, spawn, `0x1893f8` |
| 0x06 | s16 | runtime | head of the emitter's particle list | |
| 0x08 | s16 | runtime | live particle count | |
| 0x0a | u16 | runtime | serial, high half of the handle | `0x18bb88` |
| 0x0c | s16 | runtime | effect id | spawn |
| 0x0e | s16 | READ | emission mode: 0 normal, 1 expanding ring (`0x18c120`), else none | spawn, `0x1893f8`, `0x18c008` |
| 0x10 | s16 | READ | effect spawned at the emitter's position when its life expires; −1 none | `0x1893f8` |
| 0x12 | s16 | READ | signed rand added to each emitter-velocity component, `EVENT 1` only | `0x18b5a8` |
| 0x14 | s32×3 | runtime | position (stale in the file) | |
| 0x20 | s32 | READ | emitter life, ticks; −1/tick; expired when < 0 | `0x1893f8`, `0x188428`, `0x189e78` |
| 0x24 | s16 | READ | emitter drag, `v -= v*d >> 10` | `0x189270` |
| 0x26 | s16 | READ | immortal: life never counts down | `0x1893f8` |
| 0x28 | s16 | READ | emitter gravity, off Y velocity per tick | `0x189270` |
| 0x2a | s16 | READ | hidden | `0x146290`, setter `0x18ab28` |
| 0x2c | s32×3 | READ | emitter velocity (the rocket); also inherited by newborns if `+0x6c`; also a child's placement offset ×16 | `0x189270`, `0x1888a8`, spawn |
| 0x38 | s32×3 | READ | particle base velocity; `EVENT 2` overwrites with `dir × +0xb0 >> 10` | `0x1888a8`, `0x18b0f8`, `0x18a920` |
| 0x44 | s32×3 | READ | emission extent; disc radius = X unless `+0xc2` | `0x1888a8`, `0x18acc0` |
| 0x50 | s16 | READ | signed rand per velocity axis (mode 0); ring growth per tick (mode 1) | `0x1888a8`, `0x18c120` |
| 0x52 | s16 | READ | screen depth (2D only) | `0x146290` |
| 0x54 | s32 | READ | angle (12-bit) of the `+0xc4` radial offset | `0x1888a8`, `0x1893f8` |
| 0x58 | u32 | READ | colour mode: 0 ramp by life, 1 random step each tick, else random step fixed at birth | `0x1888a8`, `0x189e78` |
| 0x5c | s32 | READ | radial burst speed; nonzero replaces `+0x38` with a random XZ direction | `0x1888a8`, `0x18ac18` |
| 0x60 | s32 | READ | particles born in the spawn call (density-scaled) | spawn, `0x18aff0` |
| 0x64 | s16 | READ | max live particles (density-scaled) | `0x1893f8`, `0x18aff0` |
| 0x66 | s16 | runtime | countdown for negative rates | `0x1893f8` |
| 0x68 | s8×4 | READ | particles/tick in each quarter of the emitter's life, [0] first; negative = one per −n ticks (density-scaled) | `0x188428`, `0x18aff0` |
| 0x6c | u8 | READ | add emitter velocity to newborns | `0x1888a8` |
| 0x6d | u8 | READ | random birth rotation | `0x1888a8` |
| 0x6e | u8 | — | zero in file, no reader | |
| 0x6f | u8 | runtime | `+0x84..0x8c` nonzero, cached at spawn | `0x189e78` |
| 0x70 | u32 | READ | render flags; bit 2 → draw flag `0x60`; bit 16 → 2D | `0x146290`, `0x220878` |
| 0x74 | s16 | READ | **size at birth** (was read as a lifetime — it is not) | `0x1888a8`, `0x189e78` |
| 0x76 | s16 | READ | emitter bounces off terrain (zero on every record) | `0x189270` |
| 0x78 | s32 | READ | **particle life, ticks** + signed rand % (life/4) (was read as a count) | `0x1888a8`, `0x189e78`, `0x18c058` |
| 0x7c | s32 | READ | particle drag, `v -= v*d >> 10` | `0x189e78` |
| 0x80 | s32 | READ | particle gravity, off Y velocity per tick | `0x189e78` |
| 0x84 | s32×3 | READ | turbulence: signed rand per axis per tick | `0x189e78` |
| 0x90 | s32 | — | 1 on record 0 only; no reader | |
| 0x94 | s16 | READ | sprite group | `0x146290` |
| 0x96 | s16 | READ | logical frame count; 0 = untextured | `0x146290`, `0x189e78` |
| 0x98 | s32 | READ | effect spawned where a particle dies; −1 none | `0x189e78` |
| 0x9c | s32 | READ | immune to class −1 attractors | `0x189948` |
| 0xa0 | s16 | READ | Y extent one-sided (0..Y) | `0x1888a8` |
| 0xa2 | s16 | READ | draw position, thousandths of the way from the emitter (1000 = as is); `ADDOBJ`'s 4th operand | `0x146290`, `0x18abc8` |
| 0xa4 | s16 | — | 1 on NULL, YellowStink, GreenPuke, GreenFumes; no reader | |
| 0xa6 | s16 | READ | size at death | `0x189e78` |
| 0xa8 | s16 | READ | disc: born on the edge, not inside | `0x1888a8` |
| 0xaa | u8 | READ | particles die once the emitter has expired | `0x189e78` |
| 0xab | u8 | runtime | attached-child flag | spawn, `0x1893f8` |
| 0xac | s32 | READ | rotation (12-bit) of the local XZ offset, via `0x188828` | `0x1888a8`, `0x18ad48` |
| 0xb0 | s32 | READ | speed applied to `EVENT 2`'s direction | `0x18b0f8`, `0x18a920` |
| 0xb4 | s32 | READ | child effect spawned alongside; −1 none; slot then holds its handle | spawn, `0x1893f8` |
| 0xb8 | s16 | READ | attach the child: offset by its own `+0x2c` ×16, follow the parent | spawn, `0x1893f8` |
| 0xba | u8 | READ | child is from the attractor pool | spawn, `0x1893f8` |
| 0xbb | u8 | READ | on-expiry effect is from the attractor pool | `0x1893f8` |
| 0xbc | s16 | READ | child's life set to −1 when the parent expires | `0x1893f8` |
| 0xbe | s16 | READ | spin per tick, minimum (16-bit turns) | `0x189e78` |
| 0xc0 | u8 | READ | exempt from density scaling | spawn |
| 0xc1 | u8 | READ | not spawned at all when global `0x2c46a8` is set (97 of 105) | spawn (template read) |
| 0xc2 | u8 | READ | box extent (1) instead of disc (0) | `0x1888a8` |
| 0xc3 | u8 | — | 1 on Avatar, OtherAvatar; no reader | |
| 0xc4 | s16 | READ | radial offset distance along `+0x54` | `0x1888a8` |
| 0xc6 | s16 | READ | spin per tick, maximum | `0x189e78` |
| 0xc8 | s32 | READ | angular velocity of `+0x54` (zero on every record) | `0x1893f8` |
| 0xcc | s32 | runtime | ring radius (mode 1) | `0x18c120`, `0x18ac70` |
| 0xd0 | s16×2 | runtime | free-list / active-list links | `0x1885e0` |
| 0xd4 | s32 | runtime | copy of `+0x20` at spawn, the quarter-phase divisor | `0x188428` |
| 0xd8 | u32×16 | READ | colour ramp, `0xAARRGGBB` little-endian | `0x189e78`, `0x1888a8`, `0x146290` |
| 0x118 | char[16] | READ | name | loader |
| 0x128 | s32×6 | runtime | bounding box min/max | `0x189e78`, `0x146630` |

**Byte account: 248 of 320 bytes are template fields with a walked reader; 64 are runtime state
the spawn or tick overwrites; 8 are unaccounted for** (`0x6e`, `0x90–0x93`, `0xa4–0xa5`, `0xc3`).
Of the 177 bytes that are ever nonzero in the file, 164 are read, 10 are stale runtime values
(the old positions at `+0x14..0x1f`), and 3 have no reader. The stale runtime bytes are the
tell that the file was written from live editor structs.

## What the code does with it

**Spawn** (`0x18b5a8` / `0x18b0f8`): refuse if `+0xc1` and the global says so; take a slot;
copy the template; unless `+0xc0`, scale `+0x60`, `+0x64`, `+0x68..0x6b` by the density
(`0x18aff0`: `n*d >> 10`, floor 1 if it was nonzero); `+0xd4 = +0x20`; position = argument
`>> 4`; `EVENT 1` adds signed rand % `+0x12` to each emitter-velocity component; `EVENT 2`
sets `+0x38..0x40 = dir × +0xb0 >> 10`; then `+0x60` particles are born at once (mode 0), or the
ring is started (mode 1); then the child `+0xb4` is spawned (recursion refused for a self-
reference), offset and attached if `+0xb8`. Returns `slot | serial << 16`.

**Emitter tick** (`0x1893f8`, every 31 ms): skip if `+0x02`; `+0x20 -= 1` unless `+0x26`; reset
the bbox unless 2D. **Expired** (`+0x20 < 0`): on the first expired tick spawn `+0x10` at the
emitter's position; if `+0xbc`, set the child's life to −1; free the emitter once `+0x08` is 0.
**Alive**: if attached, move the child to this position; `+0x54 = (+0x54 + +0xc8) & 0xfff`;
unless attached, and if any of `+0x2c..0x34` or `+0x28` is nonzero, move the emitter
(`0x189270`: drag `+0x24`, gravity `+0x28`, terrain bounce if `+0x76`); then in mode 0, if
`+0x08 < +0x64`, birth `0x188428(slot)` particles — the rate byte for the current quarter,
`phase = (+0x20 × 4) / +0xd4`, `[0]` for phase ≥ 3, and always `[0]` when `+0xd4` is 0 (an
immortal emitter) — a negative byte births one every −n ticks through `+0x66`.

**Birth** (`0x1888a8`): `+0x08 += 1`; particle life = `+0x78 + signed rand % (+0x78 / 4)`;
size = `+0x74`; rotation 0 or random if `+0x6d`; colour = ramp[15] (mode 0) or a random step.
Position: emitter position, plus — if any extent is nonzero — a box `(rand % X, rand % Y,
rand % Z)` when `+0xc2`, else a disc in XZ at a random 12-bit angle and radius `X` (`+0xa8`) or
`(rand & 0x7fff) % X`, with Y as the box; Y one-sided if `+0xa0`; the XZ offset rotated by `+0xac`;
then `+0xc4` along `+0x54`. Velocity: `+0x38..0x40` plus signed rand % `+0x50` per axis, OR if
`+0x5c` is nonzero a random XZ direction at speed `(rand & 0x7fff) % +0x5c` with Y a signed rand
% `+0x5c`; plus the emitter velocity if `+0x6c`. The particle's first tick does not move it.

**Particle update** (`0x189e78`): `remaining -= 1`; dead when negative → spawn `+0x98` at the
particle, free. Colour (mode 0): `ramp[remaining × 15 / initial]`, ⭐ **so the ramp is indexed
by REMAINING life: step 15 at birth, step 0 at death — end-to-start, now from the consumer**;
mode 1 re-rolls the step each tick. Sprite frame `(N−1) − (N−1) × remaining / initial`. Size
`+0xa6 + (+0x74 − +0xa6) × remaining / initial`. Rotation `+= +0xbe + one-sided rand %
(+0xc6 − +0xbe)`. If `+0x6f`, velocity `+= signed rand % +0x84..0x8c`. Drag `v -= v × +0x7c >> 10`.
`vy -= +0x80`. Position `+= velocity`. Bbox grows by `size >> 10`.

**Attractors** (`0x189948`, the 104-byte second table, 20 templates, pool at `0x2cdccc`): each
pulls every particle whose emitter's `+0x04` matches its class (or all, if its class is −1 and
the emitter is not `+0x9c`) toward itself by `strength × delta / (|delta/4|² >> 8 + 10)`, and in
its mode 2 kills particles inside a radius. Their 104 bytes are **not** decoded here.

**Render** (`0x146290` → `0x220878` → `0x233458` → `0x22a068`): per live particle an entry of
position ×16, colour with bytes 0 and 2 swapped (so the file is B,G,R,A in memory, i.e. LE
`0xAARRGGBB`), size ×2, sprite handle from `0x182680(group, frame/2)`, rotation, and flags
`+0x70 | 2` (2D: `| 0x18002`). `0x220878` culls on the bbox, converts positions and size by
1/10240 into world units, rotation by 2π/65536, and submits each as a camera-facing quad
(descriptor type `0x282`) with draw flags `0x40`, plus `0x60` when `+0x70` bit 2 is set, plus
`0x200000` for 2D. `0x22a068` files it in the back-to-front list keyed on depth.

## Units and constants

- Positions are **cells × 640**: `0x1bbf28` passes the node position × 10240, the spawn keeps
  `>> 4`, the renderer multiplies the ×16 list value by 1/10240. Velocities: the same per tick.
- Size: drawn width = `size × 2 / 10240` cells (`size / 5120`). ApeSnot grows from 800 to 6000 =
  0.16 → 1.17 cells; Fire shrinks 1500 → 1000.
- Angles into the sine table are 12-bit turns; the table (`0x1466f0`) is 512 × `sin × 256`, and
  every use is `table × r × 4 >> 10` = `sin × r` exactly. Spin and particle rotation are 16-bit turns.
- **Tick = 31 ms.** `0x1f6af0` steps `0x18af90` once per 31 units of `0x2f07a8` (cap 1500);
  `0x220c78` adds 10 per timer-0 interrupt; `0x21afc0` sets `AddIntcHandler(9, 0x225128)`,
  `T0_MODE = 0xdc2` (bus/256 = 576 kHz), `T0_COMP = 0x1680` = 5760 → 100 Hz, and logs
  "timer compare value %d (frequency %d)". A unit is a millisecond.
- **Density = 400/1024.** `0x1f6938` → `0x220800(…, "Data\Particle\Tp2.plb", 400, 0x400)` →
  `0x18a3d0(0, 400, 1024)`: the retail park runs every effect at 39% of the file's counts (floor
  1), with a pool of 1024 particles; the setter at `0x18a4f0` has no caller.

**What that does to the numbers the port was using.** ApeSnot per `EVENT 2`: no burst; rates
(3, 2, 1, 0) → (1, 1, 1, 0) at 400/1024; a 10-tick emitter → about 8 particles over 0.31 s, each
living 75 ± 18 ticks (2.3 s), starting up the node's direction at 112 units/tick (0.17 cells)
with drag 218/1024 per tick, turbulence ±6, no gravity, growing 800 → 6000, spinning 2000/65536
turns per tick, white ramp fading 0x69 → 0 (the green is the sprite, group 11). Fire: burst
40 → 15, then 2 → 1 per tick forever (immortal), cap 200 → 78, life 100 ± 25 ticks (3.1 s),
size 1500 → 1000, disc radius 9. Sparks: 12-tick emitter, radial 37, rates (5, 3, 1, 1) → (1, 1, 1,
1) — about 12 particles, life 8 ± 2 ticks (0.25 s), untextured 100-unit quads, colour re-rolled
from the ramp every tick.

## Corrections to the earlier reading

- `+0x74` is the **start size** (short), not a lifetime in milliseconds; `+0x78` is the
  **lifetime in ticks**, not a count. There is no single "count": it is `+0x60` at spawn plus the
  four per-quarter rates over `+0x20` ticks, all × 400/1024.
- `+0x44/0x48/0x4c` are the emission **extents** (a disc radius / box), not a size.
- The ramp direction was right; it is now read, not counted.
- `+0x70` bit 2: the spawn-side reading `(flags & 4) == 0 → additive` has the polarity that the
  names contradict — see below.

## Not settled

- **What draw flag `0x20` means** (from `+0x70` bit 2). It is set on Fire, Flames, every
  explosion, Twinkle/Sparkle, GoldenTicket, ApeSnot and the firework bursts (47 effects) and clear
  on Smoke, Steam, Splash, WaterFall, Bubbles, MumboPuff, ApeSmoke and the untextured Sparks (56).
  `0x22a068` stores the flag in the sorted display list; the walk that turns list flags into a GS
  `ALPHA` register was not found — none of the 22 functions that name the list context
  `0x311160` reads the entry array, so the flush takes it through a pointer the census cannot see.
  On the names, `0x20` is ADDITIVE and everything else is ordinary alpha, which is the OPPOSITE of
  the earlier guess; labelled CANDIDATE until read.
### The display list, read (2026-09-26)

Chasing draw flag `0x20` further. `0x22a068` is now fully read, and the shape of the list is:

- The context (`0x311160`) holds **8192 entries of 16 bytes at `+0x80`**. `entry[0]` = flags,
  `[1]` = the payload word, `[2]` = a depth key, `[3]` = a link or the object pointer.
- Entries are taken from **both ends**: `+0x20080` counts up from the front, `+0x20084` counts
  down from the back (`0x2000` at frame start). Bit `0x40` of the flags picks which.
- **Three heads**: `+0x20088` (plain entries), `+0x20090` (object list, linked through
  `obj[0x10]`, insertion ordered by the object's own bit `0x8000`), `+0x20094`. An object carries
  bit `0x100` = "already in the list this frame", which is what stops it being filed twice.
- `0x22a378` is the **per-frame reset**: all three heads and the count to 0, back index to
  `0x2000`, a double-buffer index at `+0x200d8` flipped, `+0x20098/9c` copied to `+0x200a0/a4`.

**⚠⚠ THE CONSUMER IS NOT IN EE CODE THAT NAMES THE CONTEXT, and that is now a census result
rather than a failed search.** Of the **31** functions that reference `0x311160`, exactly one --
`0x22a068` -- touches the entry array or any head. The context is passed as an argument to only
six functions (`0x22a068`, `0x22a2f8`, `0x22a378`, `0x22b0a0`, `0x229e08`, `0x229dc0`,
`0x229ab0`); of those, `0x22a378` resets, `0x22b0a0` does a DMA `SYNC(0x10)`, and none walks the
entries. So whatever draws the list is reached with a HEAD or an entry pointer, not the context.

⭐ The hypothesis that fits the shape: the sorted list is handed to **VU1 by DMA chain** rather
than walked on the EE -- the same division of labour the winding flag already showed, where VU1
does the culling. If so there is no EE-side walk to find, and the flag reaches the GS through a
GIF A+D packet built from `entry[0]`. That is where the next attempt should start, not in more
EE xrefs.

**⚠ A FALSE LEAD, KILLED.** `FUN_002329c8` tests `& 0x20` and sits in the renderer core, so it
reads exactly like the answer. It is **not**: that `0x20` is on the SPRITE descriptor and swaps
the u/v pairs -- a texture flip. A different struct's bit 2. Reported here because it is the
shape of thing that would have been written up as the resolution by a less suspicious pass, and
because anyone repeating this search will land on it too.

- Bit 13 of `+0x70` (`0x2000`, on 61 effects) has no reader on the walked path.
- `+0x90`, `+0xa4`, `+0xc3`: nonzero on a few records, no reader found (absolute census only).
- The 104-byte attractor record (20 templates); `0x1b9388`'s direction scale for `EVENT 2`
  (the spawn treats the direction as Q10, so a unit direction gives speed `+0xb0`); which of the
  `PARTICLE.WAD` images a sprite group resolves to (`0x182680`, `findings/animated-textures.md`).
