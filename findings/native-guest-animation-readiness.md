# Guest motion readiness: requested versus current logical animation

September 24, 2026. Positive source joins for the next presentation consumer.
**Not yet wired into the Viewer**: its experimental entrance still passes ready=true.
No fixed delay or guest-visible/APS-slot heuristic has replaced that bypass.

## The movement predicate reads the CURRENT logical state

Raw191E10:
- owner virtual+14 returns the visual object; null object permits motion.
- requested low5(owner+30) other than9/13 permits motion.
- otherwise load visual+14 (model handle), call10EC48(handle,&byte,null), and
  permit only when that returned byte is9 OR13. It does NOT require equality to
  the requested value: requested9/current13 and requested13/current9 both pass.

10EC48 looks up2EAAD0[handle] and reads its byte+4C. A zero handle writesFF
instead. Thus **no visual object** and **visual object with zero handle** do not
have the same result. This is not an APS slot number or an actor-visibility test.
The separate global2E2920 bypass and no-slot-before-readiness order remain as
recorded in native-guest-motion.md.

## Requesting a state does not immediately make it current

10E910(logical,handle,flags) addresses the same model's control block+4C:

| block byte | interpretation from consumers |
|---|---|
| +0 / model+4C | current logical state (10EC48 returns this) |
| +1 / model+4D | selected descriptor variant |
| +2 / model+4E | phase0,1,2 (first/main/last descriptor pairs) |
| +3 / model+4F | pending requested logical state; FF means none |

10E950 compares the request against current+0. If different, it ultimately writes
pending+3, not current+0. Flags20 clear pending first; flags2 invokes1ACF20 before
storing the request. Those flag effects must remain distinct from an ordinary
queued request; the exact guest caller's supplied flags still need their own join.

10EA38 advances this control block. If there is a pending request, phase<2 and
an old descriptor's final slot is notF, it returns that final pair and sets phase2
WITHOUT committing the new logical state. Otherwise it selects the new descriptor,
uses its first pair when present or main pair when first slot=F, then commits
pending to current at10EB28..38 and clears pending toFF. Merely observing pending9
or13 therefore cannot satisfy the movement readiness predicate.

## Native playback calls the transition at a record boundary

1ACFC0 is the model animation updater. Raw1AD04C..6C compares current frame+0C
against duration+20 and current slot against sentinelF. While frame<duration AND
slot!=F, it skips10EA38 and continues the existing animation. At a boundary or
inactive sentinel it calls10EA38 at1AD078, validates the returned slot/variant
against the actual APS tables, and changes playback. This is the causal missing
link behind the ready-state test, not a delay guessed from a walk cycle's length.

This still does NOT close native callback/update scheduling, pause timing, request
flags at all guest call sites, or initialization for a freshly created visual.
The renderer must advance the actual control and APS playback together; copying
requested into a diagnostic byte would make a vacuous ready check.

## The logical-to-APS table is file-backed and decoded by its consumer

10E800 uses2AAD48 + logical*8: `(descriptorPointer, count)`.
Each descriptor is32 bytes (10E8D8 adds20hex;10EA80 shifts variant by5):
`firstSlot, firstVariant, mainSlot, mainVariant, lastSlot, lastVariant, flags, weight`.
The meaning of the three pairs comes from10E988/10EA38, not their shapes alone.
Sentinel slotF is inactive/absent, not an attempt to play APS slot15.

Read directly from the owner ELF in RAM:

| logical | descriptor | count | first pair | main pair | last pair | flags |
|---|---:|---:|---|---|---|---:|
| 9 | 2AAA48 | 1 | F,0 | **1,0** | F,0 | 0 |
| 13 | 2AABA8 | 1 | F,0 | **0,0** | F,0 | 0 |

This rules out both `logical==APS slot` and mapping both9/13 blindly to slot1.
The old Viewer's slot1 walk was chosen by examining motion, not this dispatcher.
Do not rename slot0's motion based solely on logical13; inspect its authored data.

Logical11 is also a useful discriminator:2AAA88, count8. All first/last pairsF,0.
Its main pairs, flags and weights are:

| variant | main | flags | weight |
|---:|---|---:|---:|
| 0 | F,0 | 2 | 93 |
| 1..6 | 2,0 .. 2,5 | 6 | 1 each |
| 7 | 6,0 | 2 | 1 |

10E800 accumulates +1C weights after the native random remainder operation. When
an existing descriptor has flag4, the rand%400 path retains it when remainder>=100;
otherwise weighted selection proceeds. A count<2 returns variant0 without that
random selection. These are authored variant rules, not guest-ID modulo variants.
The animation consumer's random stream is29CF08; do not silently substitute the
entrance helper's1448E0 or consume arbitrary extra draws to make pictures differ.

17C5D8 routes visual types6/7 through10E910 rather than the direct slot player.
Raw jump table362AC0[(type-1)] gives17C6F0 for both;17C6F8 calls10E910,
with flags in the delay-slot move intoa2.
The partial C corpus corroborates it; raw signatures/register arguments remain the
source of truth. Full guest->visual-type/update/request chain still needs checking
before installing this dispatcher in gameplay. This document is a source checkpoint,
not a claim that readiness or all guest animation is implemented.

## Closing the open joins — tinyclaw, September 25, 2026 (resuming astraclaw's branch)

Everything below comes from raw disassembly of the owner's PAL executable, and the address of
each fact is given next to it. The managed port is `core/TPW.PS2.Data/NativeLogicalAnimation.cs`,
and its checks are `ParkSimAudit --logical-animation-only` (50 checks, with 9 named mutations
each required to fail). It is **not yet wired into the Viewer**: the entrance experiment still
passes `ready=true`.

### The whole table: 22 logicals, 33 descriptors, contiguous

The table at 2AAD48 has 22 `(pointer, count)` entries. Their descriptors run back to back in
logical order from 2AA928 and end exactly at 2AAD48, where the table begins. The next word,
2AADF8, holds 22, but no instruction reads it. Neither reader (10E800, 10EA38) bound-checks the
logical. Descriptor fields are u32 words.

| logical | first | main | last | flags | variants |
|---:|---|---|---|---:|---|
| 0, 3, 8, 10 | F | 2/0 | F | 0 | 1 |
| 1, 5 | F | 6/0 | F | 0 | 1 |
| 2 | 8/0 | F | F | 0 | 1 |
| 4 | F | 5/0 | F | 0 | 1 |
| 6 | F | 12/0 | F | 0 | 1 |
| 7 | F | 10/0 | F | 0 | 1 |
| 9 | F | 1/0 | F | 0 | 1 |
| 11 | F | F / 2/0..2/5 / 6/0 | F | 2 / 6 / 2 | 8, weights 93, 1×6, 1 |
| 12 | F | 4/0 | F | 1 | 1 |
| 13 | F | 0/0 | F | 0 | 1 |
| 14, 15 | F | 7/0 | F | 0 | 1 |
| 16 | 4/0 | 5/0 | 6/0 | 0 | 1 |
| 17 | 3/0 | 3/1 | 3/2 | 0 | 1 |
| 18 | F | 3/0 | F | 0 | 1 |
| 19 | F | 13/0 | F | 1 | 1 |
| 20 | F | 14/0 | F | 1 | 1 |
| 21 | 8/0 or F | 2/2, 2/3, 5/0 | F | 2 | 5, weights 5, 40, 5, 40, 10 |

No logical plays the APS section with its own number. Logical 15's first slot is the sentinel F,
not section 15; the first draft of the check counted it and failed.

### A fresh model: current FF, variant 0, phase 0, pending FF

The instance's control block is not constructed. It is copied from the loader's template:

- 1F7ED8 builds a 0x58-byte template on its stack at sp+220 and zeroes it with 29C370 at 1F7F40.
  The FF bytes stored just before that call are overwritten by the zeroing.
- It then calls **10ECF0 at 1F8188** on template+4C. That writes current=FF, variant=0,
  pending=FF. 10ECF0 has no other caller.
- 1F6230 copies the 0x58 template bytes into each instance (1F6354..1F6408).

So a guest that requests 13 at birth is **not** ready until the first playback boundary commits
it.

The 1F68A8 writer that puts FF into bytes +0, +2 and +3 targets one static object at 2EA7E0 and
is a separate case. The `sb +4C` sites at 1DB654 and 1FF6A0 clamp guest attribute bytes to 0..100
and are unrelated to animation.

### What 1ACF20 does (flag 2)

1ACF20 leaves playback with slot F untouched. Otherwise it computes
`start(+18) = now(+1C) - round(duration(+20) × 33.333)`.

The playback clock is in milliseconds at 30 frames per second. Its inverse is at 1AD21C..1AD250:
`start = now - frame×1000/30/speed`.

So flag 2 makes the record's whole duration look elapsed, and the next 1ACFC0 update becomes a
boundary. **It cuts the current record short. It does not skip an old last pair.** 10EA38 still
plays that pair (phase 2) before committing.

At a boundary with an unchanged section, 1ACFC0 carries the overshoot through an fmod
(1AD0C8..F8). A new section starts at frame min(carry, duration − 0.0001).

### The guest request path

Guests hold the logical in the low five bits of N+38, which is B+30 with B = N+8.

**1921D0(B, y)** takes a nonzero y as 13 and a zero y as the ground height. It then calls the
visual's vtable +58/+5C → 228958 → 17C5D8 → 10E910 with:

- logical = low5(B+30);
- flags = 2 when bit 200 of halfword B+2C is set, otherwise 0;

and clears the bit afterwards.

**The only setter of that bit found is 140880**, which is called from 140C70 and 141020. It:

1. creates an attached visual through factory 230A98;
2. requests logical 17 on it with flags 2;
3. sets the guest's own request to 16;
4. sets bit 200.

**Its inverse 140990** removes the attachment and sets 13 without the flag. Reading the table,
the guest therefore enters 16 immediately (first 4, then main 5 looping). When it goes back to 13,
it plays 16's last pair (section 6, 40 frames on the kids) and only then commits 13. During that
wait, **191E10 blocks movement.** That is the visible case the readiness gate exists for.

### Census of immediate writes to the guest's requested logical

Immediate writes to N+38 (`and` with −20 followed by `ori`/`addiu`):

| logical | where it is written |
|---:|---|
| 13 | 1409D8 / 140990, 178714, 20CC20, 20E150, 20EE70 and 20F024 / 20EDD8, 20F674, 211C60 / 211A00, 212010; plus B+30 at 191DA0 / 191D78 (route advance) and 192214 |
| 11 | 12E08C, 20BD34, 20D998 / 20DAE8 / 20DB10 / 20DB34 (20D628), 210A8C, 212010 |
| 12 | 20CF48 |
| 14 | 20D780 |
| 16 | 12DF70, 140970, 141024, 1455C0, 1B61E8 |
| 2 | 2107D4 |

2106E8 writes one of `[14, 5, 6, 13]` from the byte table 2EEC18, chosen with 1448E0(4).

**No guest-side producer of 9 was found in this census.** Walking guests request 13. The
predicate still accepts 9 and 13 in either order.

Here, 13 means APS section 0. Every character .aps has a section 0 made of one record with flags
0x01: an `AlternatePlayer` record (track flag 0x40000). Boy1a's has three tracks, covering
head, body and legs; the four kids' have two. Only Boy1a's track targets were read. For the kids and the Boy/Girl sets it lasts **16 frames, the same as the section-1
skeletal walk the Viewer draws**. The readiness timing therefore does not depend on which of the
two is drawn. The Viewer comment saying the state→record table "has not been found" is out of
date: this is that table.

### The random source

29CF08 is newlib `rand` (state×1103515245 + 12345, low 31 bits, state at 3535E4+58), and 46
call sites share it. 10E800 divides by 400 or 100 with signed `div` on a non-negative result.

- The flag-4 retention path reuses its own remainder below 100 as the weighted roll. It does not
  draw again.
- ⚠ **Native defect:** the pending path in 10EA38 passes an uninitialized stack byte as the
  variant. When 10E800 takes the retention path it returns the OLD descriptor without writing
  that byte. The commit then has a garbage variant. It needs a multi-variant pending logical (11
  or 21) together with a flag-4 current descriptor. The port refuses that path instead of
  inventing a variant.

### Still open

- Clock and update order: 1ACFC0 through 1F2E70, against guest movement 191E98 through 140B60,
  within one frame.
- Whether the park simulation delta that drives 191E98 and the playback millisecond clock share a
  pause.
- The owners of 140C70 and 141020 (which activity attaches the item).
- The viewer consumer. It should drive this control at the actual playback boundaries of the
  guest's drawn record, gated as an explicit opt-in, and leave the default gait unchanged until
  that consumer is filmed.

## The consumer: who requests what on the entrance path, and the viewer join

This section adds to the one above.

### Section 0 is a walk

Section 0 is a baked-vertex walk, not an idle. 1A7E18 plays each frame by writing int16 xyz for
every vertex group straight into the mesh, with no interpolation. Over the lowest third of the
leg vertices, split left and right:

| file | L/R z correlation | swing (left / right) | frames |
|---|---:|---|---:|
| Boy1a | −0.96 | 7080 / 6219 | 16 |
| JungleKid | −0.97 | 5956 / 5549 | 16 |

The head bobs about 1100 units, twice per cycle. That is the same size as the skeletal
section-1 walk. Girl1a's left/right split came out lopsided on her legs mesh; her body mesh reads
−0.94, so the girls are *likely* walks, not measured ones. cow tools confirmed this independently
from the track formats: section 0 has 2–3 vertex tracks and section 1 has 22–25 bone tracks.

**9 and 13 are the two walk forms**, which is why 191E10 accepts either.

### Guest execution state is N+37, dispatched by 1920D0

1920D0 dispatches on B+2F = N+37 through the table at 364840:

| state | vtable slot | function |
|---:|---|---|
| 1 | +154 | 1920B8 |
| 2 | +144 | 20D628 (arrival or advance, called with a1=0) |
| 3 | +14C | 191E98 (move, including readiness) |
| 5 | +15C | 1913B8 |
| **0B** | **+164** | **2106E8, the idle picker** |

States above 0B are behaviour states and go through 12BEB0. The entrance lifecycle's "0B awaits
the route result" is this 0B.

**2106E8**, reading its branches:

- If request 11 and fewer than 120 updates have passed since the N+2C stamp: keep it.
- If request 11 and 120 or more have passed: pick.
- Any other request: pick when 1448E0(100) < 10.
- A pick is 2EEC18[1448E0(4)×4], which gives 14, 5, 6 or 13.

`FUN_001c4930` is the update counter.

### Entrance-path producers

- **Activation** 20BCD0 sets 11 unconditionally (20BD24..34) and stamps N+2C from 1C4930
  (20BD38).
- **Every route-slot advance** (20D628 with a slot present → 191D78 with a1=0) sets 13.
- **The idle picker** runs while the guest is in state 0B.
- The completion handlers for entrance modes 11, 13, 15 and 16 (20DB8C, 20DC1C, 20DC28, 20DC70)
  write no request.

### The push to the model

The guest's vtable slot +40/+44 is 211D28 → 192438. If B+2C bit 0 is set, it calls 1921D0(B, 0).
192C10 sets and clears that bit, which marks the guest as shown (it is not a movement dirty
flag). A shown guest therefore pushes its request to the model on every update, and readiness
cannot deadlock for lack of a push.

### Hold-last-pose

1F2E70 passes `a1 != 0` to 1ACFC0 as the apply-pose flag.

### Not found

- The per-frame order and cadence of guest update, visual sync and model update. The pairs of
  vtable +3C/+44 calls I found belong to other classes.
- The producer of the shown bit for bus-born guests.

### The viewer join

`--native-guest-animation` works together with `--experimental-native-entrance`. The code is in
`game/Viewer.NativeAnimation.cs`, and cow tools approved the hook, which sits after `Gait()`'s
seated guard.

- 191E10 replaces the entrance `ready=true`.
- The requests come from the three producers above.
- One control and one playback run per flow-owned guest. The durations come from the guest's OWN
  .aps.
- The dispatcher's (section, variant) is what gets drawn. Section 0 is drawn as section 1 (see
  above). The gait caches are cleared on handback.
- Adapters, all labelled in the file:
  - one model update per 40 ms tick, after the steps;
  - every owned guest is pushed;
  - NewlibRand;
  - the update counter is the park tick;
  - the picker uses the flow's 1448E0 stream.

**NativeAnimationReadinessSmoke on JUNGLE park 1:** 12 real bus guests, 7473 checks. One guest
waited 33 ticks (1320 ms) on logical 11's section-6 record (40 frames = 1333 ms), and every one
of those ticks held its position. After 13 committed it moved. No guest deadlocked. Every drawn
record matched the dispatcher's section and duration. The core checks derive the same wait as 34
updates.
