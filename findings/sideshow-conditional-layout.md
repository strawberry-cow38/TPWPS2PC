# Sideshow conditional layout — WIP, 2026-10-01

Base `Staging-Tinyclaw` / `9b3a2bb`. This is a view/input presentation slice;
native prize/price bounds are not changed here. **Not ready for landing until the
new rendered/transition tests and regression controls below have run.**

## Native reading

The draw is `0x1D8288`; `0x1D8488` is the prize-getter call inside it, not a
function start. `0x1D2A48` reads the raw 32-bit prize field at sideshow+0xA8.
The condition is exactly prize != 0, not Winners, win percentage or formatted money.

Canonical row labels, X45, native Y:

| index | row | prize != 0 | prize = 0 |
|---|---|---:|---:|
|0|Customers|115|115|
|1|Winners|147|hidden|
|2|Takings|179|147|
|3|Profit|211|179|
|4|Excitement|243|211|
|5|Satisfaction|275|243|
|6|Chance|307|hidden|
|7|Prize cost|339|hidden|
|8|Game price|371|275|

The first four text values use the shared value column and the same flow. This is
NOT whole-row compaction: bar frames stay at246/277; chance slider308 disappears;
prize digits340 STILL draw (including unlabelled0), prize arrows353 disappear;
game-price digits372 and arrows384 stay fixed even though their label moves to275.

Binder `0x1D7A30` plus draw/MIPS proves the value element names are crossed but
logical arrow pairing is adjacent: prize832 -> PricePerGameValue340 +
CostOfPrizeArrow353; price190 -> CostOfPrizeValue372 + PricePerGameArrow384.
Both numbers use digits `0x142B68` at0x1D87BC/0x1D8854, not the money formatter.
Tinyclaw independently confirmed the existing arrow regression/comment correction.

The native bar DATA destinations really are crossed: Sideshow vtable0x3683B8,
vt+0x1D4 ->0x1D2A68 (excitement), saved at draw sp+0x5C and used at screen+0xAD0,
with SatisfactionBar frame277. `0x1D2B48` (satisfaction mean) is saved at sp+0x60,
used at screen+0xA60, with ExcitementBar frame246. Two read-only reviews checked
the binder strings, vtable, native stores and raw disc scene. No native assets committed.

Input `0x1D8118` does not write chance/prize back at prize0, but always writes game
price. Native mouse hitboxes were not read: the port's input must follow its own
drawn presentation without claiming console mouse behavior.

## Implementation in progress

Nine canonical rows/cells/handler indices stay stable. `LaptopRowPresentation`
separately controls label slot, value, widget and arrow visibility. Only label and
shared-value grid positions flow; authored widgets/scalar values/arrows do not.
Sideshow labels explicitly stay on the native grid. Viewer binds presentation and
subject identity, correct arrow aliases, native bar data destinations, and zero-prize
writeback gates. The panel copies the plan, invalidates stale hitboxes immediately,
and cancels interaction on subject/screen changes or control hiding. Same-subject
slider refreshes must preserve drag; ShowFor(shop) also needs that continuity.

## Pending evidence / do not claim green yet

Build and native reading alone are not the requested proof. Add nonvacuous controls
for zero versus one prize with nonzero Winners; exact label/value/widget/arrow geometry;
zero->nonzero->zero; subject switch; other screen; Hide/reopen; stale input before
queued redraw; chance/prize controls cannot change hidden fields; game-price arrows
still target canonical8. Check same-ride and shop drag continuity. Run existing
laptop/management regressions and resolution640x360. Mutation-test omitted Winner
collapse, whole-widget collapse, stale hitboxes and crossed arrow callbacks. Record
actual rendered evidence separately from explicit core/panel fixtures.