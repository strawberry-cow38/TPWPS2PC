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

| mode | runs |
|---:|---|
| 0 | `0x14F820` — the normal park camera |
| 1 | `0x14F820(0)` then `0x137968` |
| 2 | nothing |
| 3 | `0x1503A0` |

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

**A second axis, `0x2B73D0`**, moves ±`0x20` on two more buttons and is clamped
`[-0x800, 0x3C0]`, default 0. ⚠ NOT NAMED: it is adjusted in mode 0, but the only read of it I
have found is in mode 3's function (`0x1502B8`, and as a float there). Calling it "pitch"
because a camera ought to have one is the kind of guess this project keeps having to withdraw.

## The script interface

`TPEmbed.cpp` registers, in this order: `gf_CameraZoom`, `gf_YRotation`, `gui_CameraFlags`,
`gs_RequiredPOIPosition`, `gs_SavedPOIPosition`, `gf_SavedYRotation`. So scripts can move the
camera, and can save and restore a point of interest and a rotation — a cutscene interface.

## ⚠ Two leads that look perfect and are nothing

- `horcam`, `spacam`, `seccam`, `orbiter`, `camera` are **model names**. They sit in a table
  between `bush02` and `monkey`. `orbiter` is a space ride and `seccam` is a prop.
- `fPitchRangeMin` / `fPitchRangeMax` are **track piece data**, next to `fDirectionRangeMin/Max`,
  `fBankRangeMin/Max` and `usCrossSectionIndex`. Nothing to do with the camera.
