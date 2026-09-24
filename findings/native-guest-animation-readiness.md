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
