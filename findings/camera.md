# The park camera on PS2, and how it differs from the PSX version

All of it read out of `SLES_500.32`. Addresses are virtual, as the ELF loads them.

## The short answer

⭐⭐ **The PS2 camera is the PSX camera with a zoom control bolted on, and the zoom's
default IS the PSX camera.** Its reset writes distance `0x6E0` and height `0xA10` — the two
constants the PSX version hardwires (`0x80054C1C`). Everything the PSX camera does, the PS2
camera does with the same shifts and the same magic numbers. What is new is that two of the
numbers stopped being constants.

The other difference is structural: PSX has one camera, PS2 has a pool of ten and four modes.

## The object

`PoolOfCameras` allocates **10 cameras of 232 bytes** — 16 byte pool header + 10 × 232 = 2336,
which is the exact size passed. Pool pointer at `0x3952D0`; `Camera::alloc` `0x152528`,
`Camera::free` `0x1525A8`. Above the pool sits a slot table of ten `{inUse, camera, initFn,
param}` records at `0x2AC380` (`0x11A2F8` claims a slot and calls the init function on the new
camera). The game's own camera is created in `0x151498`:

    DAT_003952F8 = allocCamera(0x2AC380, 0x14F758, 0)

so **`0x14F758` is the camera's per-frame function**. It dispatches on `DAT_00395288`:

| mode | runs | what it is |
|---:|---|---|
| 0 | `0x14F820` | the normal park camera |
| 1 | `0x14F820(0)` then `0x137968` | **riding a ride, first person** |
| 2 | nothing | not dispatched |
| 3 | `0x1503A0` | **attached first person — camcorder mode** |

`SetCameraMode` is `0x14DAF8`, and it names them by what it does on the way in:

- **Mode 1 takes the cursor's TILE** (`0x14E138(x >> 8, z >> 8)`, the same 256-units-to-a-tile
  divide as everywhere else), asks `0x1E61E0` whether what is there can be ridden, and if not
  puts up message `0xAF` and stays put. If it can, `0x136E20` mounts it **carrying the current
  yaw** `0x39538C`, so you board facing the way you were looking. It also pulls the view in:
  the pair of distances goes from 2000..6000 to **1200..5000** and the float from `0.8` to
  `0.04`, which is the tighter, closer view you want sitting in a car.
- **Mode 3 computes no camera position at all.** It drives a target object at `DAT_002B72A0`
  through its vtable and watches for a button that calls `SetCameraMode(1)` — get on the thing
  you are looking at. It is the only mode that changes no distances on entry.
- Mode 0 restores 2000..6000 and `0.8`.

⚠ Which also corrects the axis above: **`0x2B73D0` is not a standalone global.** It sits
`0x130` into the camera-target block that starts at `0x2B72A0`, alongside `0x2B73F8` (the vector
the yaw rotation is applied to) and `0x2B7400` (the yaw offset for the quantised facing). It is a
field of the attached-camera state, which is why mode 3 is the only place that reads it.

## The normal camera, `0x14F820`

Everything in this section is the same as the PSX camera, instruction for instruction in the
parts that matter:

- **Yaw.** Target `0x395390`, current `0x39538C`, 4096 to a turn. A button adds or subtracts
  `0x400` — a quarter turn — and the target is wrapped into `[0, 0x1000)`. The ease is the
  PSX's two-step eighth: take `(diff >> 3) * frameTime >> 13` as a half step, re-take the
  difference from there, then `(diff >> 3) * frameTime >> 12`.
- **Focus.** `0x395300` / `0x395310`, x and z in 24.8 fixed point, chasing the cursor at eight
  times the distance, clamped to ±`0x3FFF`, times frame time over 1024.
- **Height.** The ground under the EYE is looked up (`0x149D20` / `0x149D90`), `0x39539C` is
  added, and `0x395398` eases down to it by an eighth per frame while rising to it at once —
  so the camera never sits below the ground beneath it. The height it looks at, `0x395360`,
  eases by an eighth too.
- **Eye.** The focus, minus the yaw's direction vector (`0x395378`) scaled by the distance
  `0x395394`, shifted right by 12.

A second direction vector is built at `0x395380` from the yaw **quantised to `0xFC00`** — whole
quarter turns — which is a separate thing from the smooth yaw the eye uses.

## ⭐ What is actually new

**Zoom.** `0x395394` is the distance behind the focus, and on PS2 it is a variable:

| | PSX | PS2 |
|---|---|---|
| distance behind | `0x6E0`, constant | `0x395394`, ±`0x20` per frame on its own buttons |
| range | — | clamped `[0x180, 0xC3C]` = 384..3132 |
| default | `0x6E0` = 1760 | `0x6E0` = 1760 (set by the reset at `0x14F718`) |
| height above ground | `0xA10`, constant | `0x39539C`, `0xA10` at reset |

So the player can pull in to 22% of the PSX distance or out to 178% of it, and the PSX camera
sits exactly at the middle of that range. The reset also zeroes both yaws.

**⭐ And the zoom IS the pitch control.** The eye's height is pinned to the ground under it plus
`0x39539C` no matter what the distance is — `0x395394` appears only in the x and z of the eye,
never in its height. So changing the distance changes the ANGLE you look down at:

| distance | | look-down angle, `atan(0xA10 / d)` |
|---|---|---:|
| `0x180` = 384 | zoomed right in | **81.5°**, nearly overhead |
| `0x6E0` = 1760 | the default, and the PSX camera | **55.7°** |
| `0xC3C` = 3132 | zoomed right out | **39.4°**, low and long |

The PSX camera is fixed at 55.7° (`atan(0xA10 / 0x6E0)`); the PS2 camera sweeps 39° to 82° about
it. Master said "i believe it is the pitch" and that is right about the camera — it is the
DISTANCE knob that tilts it, as a consequence of the height being pinned, not a pitch angle
anyone stores.

**The second axis, `0x2B73D0`**, moves ±`0x20` on its own two buttons, clamped `[-0x800, 0x3C0]`,
default 0, and it is **not** a pitch. Read at the tail of `0x14F820`:

    dir   = normalise(lookTarget - eye)          // 0x1AE7A8 is a normalise, 0x1AE830 a cross
    eye  += dir * DAT_002B73D0

It scales the UNIT VIEW DIRECTION and adds it to the eye, so it slides the camera along its own
sight line and leaves the direction — and therefore the pitch — exactly as it was. A dolly, in
world units, from 2048 back to 960 forward. ⚠ It can push the eye below the ground-plus-`0xA10`
floor the easing above works so hard to hold.

## ⭐ The scale, proven rather than assumed: 256 world units to a tile

The ground under the eye is looked up as

    FUN_00149D20(eyeX * 0x10000 >> 0x18, eyeZ * 0x10000 >> 0x18)

`(v << 16) >> 24` sign-extends the low 16 bits and then divides by 256, so that argument is a
TILE INDEX and **one tile is 256 world units** — the same as the PSX port's `TileUnits`. The
focus coordinates are masked to 16 bits in the same function, which caps the world at 256 tiles
against a park of about 96, so the two facts agree.

That makes the camera directly expressible in cells, which is what the viewer draws in:

| | world units | cells |
|---|---:|---:|
| height above ground | `0xA10` = 2576 | **10.06** |
| distance, zoomed in | `0x180` = 384 | **1.50** |
| distance, default / PSX | `0x6E0` = 1760 | **6.875** |
| distance, zoomed out | `0xC3C` = 3132 | **12.23** |
| dolly | -2048 .. +960 | **-8.0 .. +3.75** |

And it checks against the PSX write-up independently: `sqrt(1760² + 2576²) / 256 = 12.19`, which
is the "about 12.2 tiles away" recorded there from the other binary.

## A detail worth keeping

The up vector is not straight up. After `up' = normalise(cross(dir, right))` the code adds the
frame's focus-chase velocity divided by **a million** and re-normalises, so the camera leans very
slightly into a pan. It is small enough to be felt rather than seen, and it is the sort of thing
that is invisible until it is missing.

## The script interface

`TPEmbed.cpp` registers, in this order: `gf_CameraZoom`, `gf_YRotation`, `gui_CameraFlags`,
`gs_RequiredPOIPosition`, `gs_SavedPOIPosition`, `gf_SavedYRotation`. So scripts can move the
camera, and can save and restore a point of interest and a rotation — a cutscene interface.

## ⚠ Two leads that look perfect and are nothing

- `horcam`, `spacam`, `seccam`, `orbiter`, `camera` are **model names**. They sit in a table
  between `bush02` and `monkey`. `orbiter` is a space ride and `seccam` is a prop.
- `fPitchRangeMin` / `fPitchRangeMax` are **track piece data**, next to `fDirectionRangeMin/Max`,
  `fBankRangeMin/Max` and `usCrossSectionIndex`. Nothing to do with the camera.
