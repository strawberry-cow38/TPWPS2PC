# Clock rate: how fast the park sim runs in wall-clock time (PAL SLES_500.32)

Sources: raw MIPS (`full.s` listing of the ELF), `.data` read from the ELF through the PT_LOAD headers,
caller census by `jal` grep plus a 4-byte ELF word search for every function named below. No Ghidra run
was needed. READ = seen in MIPS/data. INFERRED = reasoned to.

## Answer

**One park sim pass per rendered frame, and a rendered frame lasts at least 2 PAL fields, so the sim runs
at most 25 passes a second. It runs at exactly 25 whenever a frame's work fits in 40 ms.** Nothing catches up
when a frame overruns: the day advances a fixed amount per pass, not per unit of time. A day is 60 passes,
so it takes **2.4 s at 25/s**, and longer if frames overrun. The port's 4.8 s/day is 2x too slow. It matches
60 passes at 12.5/s, or 120 passes at 25/s, and the code supports neither.

Confidence: high on "1 pass per frame" and "at most 25/s". The pacing gate, its constant, the PAL video
mode and the pass loop are all READ. The only rate the code cannot give is the slower one a busy park drops
to when frames overrun. That depends on CPU/GS load, and only measurement can settle it (see the end).

## 1. The main loop: passes per rendered frame (READ)

`0x2312b8` is one iteration of the game loop:

```
0x2312c0  jal 0x1fb798 / 0x1fb6a8 / 0x1fbda8        input
0x2312d8  N = [0x331510]                             .data initial value 1
          if N > 0: repeat N: jal 0x10eec0           the sim/scene pass
0x2312fc  jal 0x225fc8(0x310d48)                     render ONE frame (pacing wait is in here)
0x231304  jal 0x214b38; 0x230c50; 0x110de0(0x110a68())   (audio etc.)
```

- **N = `[0x331510]` = 1.** Its only writers are `0x17dc64` and `0x17dc70`, inside the debug-console
  command handler `0x17dbf8` (vtable slot at `0x363a54`, command id `0x10000002`). That handler prints
  "Gamespeed set to %d gameticks/rendertick" (`0x363920`). No gameplay code writes it. The only reader is
  `0x2312dc`.
- **`0x10eec0` has two callers:** `0x2312e8` (the loop above) and `0x231558`. `0x231558` runs once at boot,
  in `0x2314e8` after "Running game..." and a `setjmp` (`0x29d174`). No data word points at `0x10eec0`.
- **`0x2312b8` has one caller,** `0x194668` in the pump `0x194628(n)`, which runs it n times. It has 30
  `jal` callers: the scene-change waits at `0x13b020/0x13b034/0x13b078` and the front-end/flow code at
  `0x1c55f0..0x1c7b6c`, among others. However the loop is entered (outer or nested pump), every iteration
  is N passes plus one render.
- **Inside a pass, the park branch runs once:** `0x10eec0` -> `0x13af68` (only caller `0x10eed8`) ->
  `0x1c4aa8` (only caller `0x13af7c`) -> scene `vt+0x24`. That fn word is at `0x360314` = `0x151800`, the
  only reference to `0x151800` in the ELF. In `0x151800` the sim body repeats `n = (0x230260("SpeedUp") ?
  30 : 1)` times (`0x1518fc..0x151908`). **`0x230260` is a stub: `jr ra; move v0,zero`.** So n = 1 always.
- **The render step always renders.** `0x225fc8` gates on `renderer+0x350` (`0x311098`). The constructor
  sets that word to 1 at `0x22589c`. Its setters `0x226b30`/`0x226b40` and getter `0x226b48` have no `jal`,
  no `j` and no data pointer, so nothing can clear it. `0x225fc8` -> `0x225bf8` -> `0x22b0a0` (at
  `0x225f8c`) builds and kicks the frame. `0x22ae80` returns the packet base whenever its only helper
  `0x22ad80` returns 1, and `0x22ad80` always returns 1 (`0x22ae08`). So the frame is kicked unless the
  packet base is 0 (INFERRED not to happen).

**So: sim passes = rendered frames, 1:1, in retail.** Nothing does a fixed-step accumulator, a catch-up
loop or a frame skip. The two multipliers that exist are a debug console command and a stubbed-out flag.

## 2. Frame pacing: what each frame waits on (READ)

Interrupt setup in `0x21afc0`. The syscall stubs: `0x275280` AddIntcHandler (0x10), `0x2752b0`
AddDmacHandler (0x12). The wrappers: `0x275d90`/`0x275df8` Disable/EnableIntc (0x15/0x14) and
`0x275e60`/`0x275ec8` Disable/EnableDmac (0x17/0x16).

| what | where | handler |
|---|---|---|
| DMAC ch1 (VIF1) end | `0x21aff4` | `0x224f68`: if `[0x310c9c] & 4`, clear `[0x310c9c]`; `[0x310ca0]=1` |
| INTC 2 = VBLANK start | `0x21b020` | `0x224c10` (below) |
| INTC 9 = TIMER0 | `0x21b044` | `0x225128`: 64-bit `[0x310ca8] += 1`, ack T0 |
| INTC 5 = VIF1 | `0x229fac` | `0x224cb8`: reads `VIF1_MARK` (`0x10003c30`). For a mark with bit 15 set, low bits 0, and `([0x310c9c]&9)==9`, it sets `[0x310c80]=1` (flip pending) and returns without restarting VIF1, which stays stalled |

**VBLANK handler `0x224c10`:**
```
[0x310c98] += 1                                   field counter
if [0x310c80] and [0x310c98] >= [0x310cb4] + [0x310cbc]:
    [0x310cb8] = [0x310c98] - [0x310cb4]          fields this frame took
    [0x310c80] = 0;  [0x310c9c] |= 4
    *0x10003c10 (VIF1_FBRST) = 8                  STC: release the stalled VIF1, the frame goes out
```

**Frame kick `0x22b0a0`** (per render):
```
0x22b180  while [0x310c9c] != 0: spin              previous frame not yet released and finished
0x22b1b4  [0x310c9c] = 9
0x22b1c0  [0x2ef970+0x9c] += 1                     frame counter
0x22b1e8  [0x310cb4] = [0x310c98]                  baseline = field count at kick
0x22b1ec  jal 0x21d6a8                             start VIF1 DMA chain (D1_TADR/D1_CHCR 0x105)
```

**Threshold `[0x310cbc]` = 2.** That is its `.data` value, and nothing writes it. There is no `sw` with
offset `0xcbc` on a `lui 0x31` base: the six other `0xcbc(...)` accesses in the ELF use object bases.
There is no store at `-0x8c`/`-0x4a4` relative to the renderer bases `0x310d48`/`0x311160`, no ELF word
equal to any address `0x310c80..0x310ccc`, and no `lui 0x31`+`addiu` pair forming such a base. Readers:
`0x224c54` and `0x232360`.

Consequence (INFERRED from the READ mechanism): a frame is released at a vblank where
`count >= kick_count + 2`, and the next kick waits for that release. So **releases are at least 2 fields
apart, which caps the rate at 25 frames/s on PAL.** When EE+GS work per frame is at most about 40 ms, the
spin absorbs the slack and frames go out every 2 fields, exactly 25/s. When work runs longer, the kick is
late, frames take 3+ fields, and the sim slows in proportion, since it gets 1 pass per frame and a fixed D.
The pacing is **variable, with a minimum of 2 fields.**

No other wait sits in the loop. The field counter `[0x310c98]` is read only by the handler and at
`0x22b1dc`. The only sceGsSyncV-style CSR-VSINT waits are `0x21b1e8` (callers `0x1380ac/0x1380b4`, a
special path in `0x137fc8`) and `0x271b08` (callers: the exception screen `0x12fa90` and the FMV player
`0x26a4c8/0x26ab38/0x26b184`).

## 3. PAL vs NTSC (READ)

- Boot `0x2314e8` calls `0x20a0e0(640, 512)` at `0x231500`. `0x20a0e0` -> `0x230c88`: height `0x1c0` (448)
  gives the NTSC branch, anything else gives `0x21aa18(0x2ef970, 640, 512, 2, 0x32, t1=0, t2=1, t3=3)`
  (`0x230ccc..0x230cf4`). `0x21aa18` passes these to `sceGsResetGraph` `0x271640` at `0x21ac98` as
  **(0, 1, 3, 0) = reset, interlace, PAL, field mode**. So 50 vblanks/s.
- The code has no PAL/NTSC rate compensation. The threshold is a constant 2 in both modes (it would be
  30/s on NTSC). The only mode-dependent writes are `0x21aaf8..0x21ab18`: `+0xd4`/`+0xd8` = 2/60 (NTSC) or
  0x12/80 (PAL), which are display parameters, not rates. The one hard-coded NTSC reset
  (`0x12f918`, `sceGsResetGraph(0,0,2,1)`) is in the exception/crash screen `0x12f818`.

## 4. The millisecond clock `0x147158` (READ)

- **Hardware timer, not per frame.** `0x21b068..0x21b084`: `T0_MODE (0x10000010) = 0xDC2`, which is
  CLKS=2 (BUSCLK/256), ZRET, CUE, CMPE. `T0_COMP (0x10000020) = 0x1680 = 5760`. The init prints
  "timer compare value %d (frequency %d)" with 5760, 100 (`0x36e0d0`). BUSCLK 147.456 MHz / 256 / 5760 =
  **100 Hz** (the bus clock value is INFERRED from hardware documentation; the "100" is READ from the
  print). The handler `0x225128` does `[0x310ca8] += 1`.
- **`0x220c78`** (only caller `0x225fd4`, the first thing `0x225fc8` does each render):
  `d = ticks - [0x2f07c0]; [0x2f07c0] = ticks; ms = d*10` (`0x220ca0..0x220cac`: `(d<<2 + d)<<1`).
  `[0x2f07a0] = ms`; `[0x2f0798] += ms` (always); if gate `[0x2f07b8]`: `[0x2f07a8] += ms`;
  `[0x2f07b0] = gate ? ms : 0`.
- `0x147158` returns low32 of `[0x2f07a8]` (gated); `0x147140` returns `[0x2f0798]` (ungated). The gate is
  written by `0x220c68`. The pause helpers `0x11e708` (pause: `[0x2acabc]=1`, gate 0) and `0x11e730`
  (unpause) drive it.
- So the ms clock measures **real elapsed time** in 10 ms steps, sampled once per rendered frame (about
  +40 ms per frame at 25/s). It neither assumes a frame rate nor contradicts 25/s. It is independent of the
  sim. The one place code ties ms to frames is `0x232328` (per render): it takes `[0x2f07b0]`, and if that
  exceeds 1000 ms it substitutes `[0x310cbc] x 18 = 36` ms (`0x232350..0x232364`). That is a nominal
  2-field frame of 36 ms, between NTSC's 33.3 and PAL's 40, and not PAL-tuned.

## 5. The 10000-per-pass counter and D (READ)

- `[0x2acab4]` has one writer, `0x11e770`, in `0x11e758`: `+= 0x2710` unless `[0x2acabc]` (the pause flag,
  set by `0x11e708`, cleared by `0x11e730`). `0x11e758` is called at `0x10eec8` (every pass) and in
  `0x11e660`, which has no callers and no data pointer.
- `0x11e688(&prev)`: `delta = [0x2acab4] - prev; prev = [0x2acab4]; return delta << 7`. The first call
  (flag `[0x2acab8]`) seeds prev and returns 0.
- `0x1c4aa8`: `D = 0x11e688(&[0x397638])`; `if D > 0x4000 (signed, slti 0x4001): D = 0x4000`;
  `[0x397640] = D`; then scene `vt+0x24`. There is no lower clamp. Other writer: `0x1c4990` (0).
- delta is 0 (paused) or 10000 per pass, so D is 0 or `min(1,280,000, 0x4000)` = **0x4000**. The cap always
  binds when not paused. Skipped passes can only make the pre-cap value bigger, so it still binds. The
  counter is a pass count in disguise. Nothing converts it or D to real time: `0x2acab4` is read only
  through `0x11e6f8`, and `0x397640` only through `0x1c4920`. `0x11e688` has two other callers, and both
  work in pass units, not ms. `0x1e12c0` only reseeds an object's snapshot at `obj+0x14` and discards the
  result. `0x1e4ed0` is an object timer: `obj+0x8c += delta` (halved when `0x14dd90()`), UNcapped, so
  1,280,000 per pass. It fires at `>= (n-1) << 12`.
- Calendar `0x16b240` (only caller `0x16b084` in `0x16b060`): `cal+0 += D`; if `> 0xEFFFF`:
  `cal+0 = 0` (`0x16b2a8`, a reset, not a subtraction), then day/month/year roll. Month lengths come from
  `0x361d88` = 31,28,31,30,31,30,31,31,30,31,30,31 (365, no leap day). **Day = 0xF0000/0x4000 = 60 passes**,
  exact.

## 6. Other code encoding a real-time rate (brief, not exhaustive per instructions)

- The ride-stats hook adds `1/30 s` (`0x3d088889`, `0x1aef68`) per coaster step. This is a 30-steps/s
  assumption (coaster-trains.md §2.3), so NTSC-flavoured. On PAL the shown Duration is 25/30 of real time
  (INFERRED).
- APS animations: `currentFrame = (ms - start) x 30/1000` (bus-native-animation.md). This is real-time
  30 fps, driven by the ms clock, not by passes.
- None of these feeds the calendar or D.

## Wall clock (at the 25/s cap)

| | passes | at 25/s |
|---|---|---|
| day | 60 | **2.4 s** |
| 30 days | 1,800 | **72 s** (1 min 12 s) |
| 28 / 31-day month (table) | 1,680 / 1,860 | 67.2 s / 74.4 s |
| year (365 days) | 21,900 | 876 s = **14.6 min** |

These are the fastest the game can run. Under load, every entry scales by 25/(actual fps).

## Still unknown / what would settle it

- **The actual frame rate of a busy park.** The code gives a ceiling (25/s), not a floor. It would be
  settled by one PCSX2 run reading `[0x2ef970+0x9c]` (frame counter), `[0x310cb8]` (fields per frame) or
  `[0x310c98]` against `cal+0x10` over a minute of a loaded park. `[0x310cb8]` is exactly the per-frame
  field count the game computes, so a histogram of it answers "always 2?" directly.
- Where the nested pump structure starts: the whole game appears to run inside the boot-time `0x10eec0`
  call via `0x194628` pumps. The `0x209ca0` flow was not traced. It does not affect the 1:1 ratio, because
  every path to `0x10eec0` after boot goes through `0x2312b8`.
- What `+0xd4`/`+0xd8` (PAL 0x12/80, NTSC 2/60) are. They are not read as rates here.
