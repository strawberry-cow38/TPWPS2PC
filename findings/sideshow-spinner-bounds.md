# Sideshow spinner bounds — 2026-10-01

Separate numeric-control slice above the layout/regression commit `11af220`.
Tinyclaw assigned this follow-up after gating that commit on Staging-Tinyclaw.
No guest-performance, model, tour, loan, research or save/load changes are included.

## Native evidence

Read the shared decompile, then independently checked the selected MIPS against
the owner's disc in memory. No assets were extracted or committed.

- Setup `0x1D7F48`: `0x1D7FD4/0x1D7FD8` load minimum1 and maximum1000; both
  prize (`screen+0xC68`) and price (`screen+0xCE8`) receive those bounds.
- Incoming raw values subsequently REPLACE the initially clamped spinner memory.
  `0x1D8040/44` and `0x1D808C/94` perform a signed upper clip only:
  `loaded = min(raw,1000)`. Incoming0 or negative prize can survive this setup.
- Generic spinner `0x207B10` returns immediately when inactive. Active mode6 is
  non-wrapping; `0x207B70/0x207B8C` subtract/add the step with non-trapping32-bit
  arithmetic, then `0x207BE0..0x207C08` clamp signed value to1..1000. Those clamp
  checks also run with NO directional input, but only on an active control.
- Price getter/setter `0x1D2A34/0x1D2A2C` are `lhu`/`sh`, unsigned16. Prize
  getter/setter `0x1D2A4C/0x1D2A54` are `lw`/`sw`, signed32.
- Input `0x1D8118` still skips chance/prize writeback if the GAME's raw prize is
  exactly0. It always writes the price spinner. Do not globally clamp stored
  prize0 to1: that would change the conditional Sideshow presentation/game.

For a freshly bound ACTIVE control, the relevant table is:

| raw | loaded | decrement | increment |
|---:|---:|---:|---:|
|-1 (prize)|-1|1|1|
|0|0|1|1|
|1|1|1|2|
|1000|1000|999|1000|
|1001|1000|999|1000|
|65535 (price getter's 0xFFFF)|1000|999|1000|

**Stored prize0 is the exception:** its handler rejects writeback and keeps0,
regardless of the spinner arithmetic. Inactive controls need not clamp0. Price
bits0xFFFF are read as65535, NOT signed-1. A1001 decrement reaches999 because the
load clip happens BEFORE the step; a direct clamp(raw-1) would incorrectly yield1000.

## Implementation and scope

`SideshowSpinner.Load/Step` implement that fresh-control arithmetic. Both canonical
money-arrow handlers use it, keeping the existing raw-prize0 guard. The unsigned
price cast occurs AFTER the bounded result, preventing ushort wrap at high values.
The helper is deliberately Sideshow-specific; the gate-price control keeps its
native0 minimum. No mutable screen-wide clamp on ParkRide state was added.

This slice implements numeric CONTROL EVENTS. It is NOT a port of all native pad
focus/active flags, passive per-frame spinner writeback, simultaneous button-mask
handling, held-repeat timing, or feedback sound IDs0xAF/0xD6. Core no-input cases
are arithmetic fixtures, not proof of shipping passive focus behavior. Existing
out-of-range stored fields are normalized when their control is nudged; opening
this mouse UI alone does not reproduce every native controller-frame writeback.

## Targeted regression evidence

The core Sideshow presentation family now has **136 actual assertions**: the
original92 plus44 bounds/arithmetic/native-instruction checks. Its required floor
and semantic witnesses were raised accordingly; the Python tool suite passes103.

The rendered shipping-Viewer integration fixture passes **876 checks at640x360**
on JUNGLE/1. Twenty fresh UNPLACED real-DBA fixtures independently cover both arrows
at0/1/1000/1001, signed negative prize and unsigned65535 price, both directions,
unchanged sibling money/chance/play-counter fields, canonical callback identity,
and the bounded digits submitted to drawing. Only invisible prize0 controls use
separately named stale-HANDLER injections; other bounds events use actual GUI input.
These remain integration fixtures, not a player-built attraction or retail capture.

The former regression's real1->0 decrement expectation was WRONG for these native
bounds. It now asserts that the real decrement retains1 and a valid chance drag.
Then a DECLARED fixture-owned raw transition changes the SAME object to prize0 and
binds it again, preserving immediate hidden-control/stale-hitbox/drag cancellation
coverage. Zero-prize coverage was retained, not deleted to make the bounds pass.

Six compiled controls were rejected by assertions; none relied on build failure:

| control | rejection |
|---|---|
| old money handlers restored, new regression retained | rendered minimum1 assertion |
| helper lower clamp changed to0 | core lower-edge cases |
| helper upper clamp removed | core1000/1001/65535 increments |
| setup upper clip removed before stepping | rendered prize1001 decrement expected999 |
| raw-prize0 handler guard removed | explicit stale-handler zero-prize assertion |
| only price handler kept old0-floor arithmetic | rendered price0 decrement expected1 |

All controls were restored before the final builds/gates. The registered viewer
case's coverage floor is now876, not the old779. Both matrix classifiers retain
strict error/leak detection and exact loaded-map witnesses. Integration artifacts
under `/tmp/tpw-sideshow-bounds-*` distinguish targeted runs from the final clean
matrix runs; only a completed successful manifest supports a cross-park claim.

## Commands

```sh
DISC=/home/ec2-user/tpw-ps2/tpw_ps2.bin
GODOT=/home/ec2-user/godot46/Godot_v4.6-stable_mono_linux_arm64/Godot_v4.6-stable_mono_linux.arm64

dotnet build tools/TPW.PS2.ParkSimAudit/TPW.PS2.ParkSimAudit.csproj
dotnet run --no-build --project tools/TPW.PS2.ParkSimAudit -- "$DISC" JUNGLE --sideshow-presentation-only
PYTHONPATH=tools:launcher python3 -m unittest discover -s tools -p 'test_*.py'
python3 tools/viewer_matrix.py --disc "$DISC" --godot "$GODOT" --out /tmp/sideshow-bounds-viewer-NEW --scenes sideshow-presentation --timeout 105
python3 tools/audit_matrix.py --disc "$DISC" --out /tmp/sideshow-bounds-core-NEW --worlds JUNGLE --terrains 1
```

The all-eight-park selected scene is not the whole Viewer matrix or all three full
integration matrices. See `sideshow-conditional-layout.md` for the unchanged native
layout quirks and the explicit absence of a retail zero-prize screenshot comparison.
