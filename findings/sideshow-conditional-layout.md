# Sideshow conditional layout — validated slice, 2026-10-01

Base `Staging-Tinyclaw` / `9b3a2bb`. This is a view/input presentation slice;
native prize/price bounds are not changed here. Production snapshot `200df01` is
followed by the rendered/core regression and gate changes described below. This is
ready for integration review, not a claim that main or all three full matrices landed.

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

## Implementation

Nine canonical rows/cells/handler indices stay stable. `LaptopRowPresentation`
separately controls label slot, value, widget and arrow visibility. Only label and
shared-value grid positions flow; authored widgets/scalar values/arrows do not.
Sideshow labels explicitly stay on the native grid. Viewer binds presentation and
subject identity, correct arrow aliases, native bar data destinations, and zero-prize
writeback gates. The panel copies the plan, invalidates stale hitboxes immediately,
and cancels interaction on subject/screen changes or control hiding. Same-subject
slider refreshes must preserve drag; ShowFor(shop) also needs that continuity.

## Preserved awkward zero-prize screen

Cow tools inspected both actual fixture PNGs. The positive case has all nine rows;
the zero case hides Winners/chance/prize labels, retains the unlabelled prize `0`,
and leaves the price controls at their old location. The result LOOKS incoherent:
Game Price now sits beside a fixed bar, while its actual arrows/digits are lower.
This prompted a fresh MIPS check, not a cosmetic adjustment:

- `0x1D8490` skips the Winners row step at `0x1D84AC`.
- Labels 899/743 use flowing `$s0` at `0x1D84E8..0x1D851C`.
- `0x1D8538` skips the chance/prize label draws AND their steps at prize0.
- Bar frame loads/stores `0x1D8898..0x1D8944` use their separate authored globals,
  not that flowing row register. Price digits/arrows similarly use fixed globals.

Both layouts are now literal-coordinate regression fixtures. Do not realign them
just because the zero screen looks broken. **No retail zero-prize screenshot has
been compared.** Evidence supports matching the native code; a differing retail
capture should trigger investigation, not an appeal to these tests as authority.

## Evidence and registration

Actual observed results (counts come from the listed commands/logs):

| run | result | scope |
|---|---:|---|
| explicit core/presentation audit | 92 assertions PASS | canonical IDs, raw0/1/30/-1 plans, immutable/caller-isolated state, authored disc coordinates, ELF branch witnesses |
| registered rendered case, all eight parks at640x360 | 8/8 PASS, 779 checks EACH | shipping Viewer, normal map startup, explicit UNPLACED real-DBA Sideshow fixtures and actual GUI input |
| same JUNGLE/1 fixture with two PNG saves | 781 PASS | positive/zero viewport captures; reviewed by cow tools for gross presence/order, not pixel-perfect alignment |
| existing LaptopShopScreenAudit at640x360 | 119 PASS | existing panel regression fixture |
| existing ManagementSmoke JUNGLE/1 at640x360 | 163 PASS | legacy management smoke, including its explicitly injected setup; NOT an ordinary-player proof |
| CI tool Python suite | 103 tests PASS | audit/viewer classifier controls, including registration/floors |
| full JUNGLE/1 ParkSimAudit classifier | PASS, sideshow_presentation=92 | integrated core families, research14/advisor_research291 retained |

`SideshowPresentationSmoke` creates unplaced public-state fixture rides900001/2/3
(prizes30/0/1, price10, chance60, Winners17, Customers19, Satisfaction23). It invokes
only `ShowSideshowDetails` to bind them during observations, then uses
`Input.ParseInputEvent` for ordinary panel controls. It does NOT claim placement,
preview-model, guest-gameplay, or a console framebuffer comparison. A separately
labelled stale-HANDLER fixture injects canonical events to exercise zero-prize
writeback guards even though hidden controls cannot emit them. A separately
labelled standalone shop-PANEL callback fixture tests same-subject drag continuity;
it is not the shipping selected-shop routing path.

Draw receipts are default-off. When enabled they record actual submitted scalar
arguments/rectangles and text/label anchors. Anchors precede glyph justification
and possible empty-text exits: they are NOT final glyph bounds, surviving pixels,
or human visual review. Tests pin label slots, X/Y, authored widget rectangles,
arrow aliases, actual bar data, all nine canonical row IDs and callbacks6/7/8. They cover
zero/nonzero transitions, nonzero->nonzero subject changes, hidden stale input
before/after redraw, page changes, Hide/reopen, valid drag refreshes and capture-off.

The initial matrix had all fixture assertions pass but the strict gate rejected
three audio shutdown leak warnings. Verbose HALLOW/2 evidence named
AudioStreamGeneratorPlayback/AudioStreamWAV/AudioStreamPlaybackWAV. Disposing the
fixture's own input events did NOT close that blocker. The final fixture reuses
PointerTilesSmoke's established teardown protocol: ResetNativeBus, StopMusic,
RideSounds.Clear, QueueFree, two ProcessFrames, then a0.1s real-time timer. Only
teardown does this, AFTER observations; there are no Viewer field writes, manual
sim ticks or scheduler disabling in this fixture. All eight strict cases then
passed. No relaxed leak regex or global finalizer-drain workaround is included,
and this does not claim to fix every game's audio/memory lifetime issue.

## Nonvacuous controls

All six implementation mutants COMPILED and were rejected by assertions, not by
build failure. Sources were restored, rebuilt and rerun after them:

| deliberate mutant | rejecting evidence |
|---|---|
| Winner visible at prize0 | literal slots/hidden Winner core assertions, raw exit1 |
| renderer submits `fraction+1` to bars | rendered crossed-DATA argument assertion, raw exit2 |
| removed zero-prize chance writeback guard | separate stale-handler fixture, raw exit2 |
| removed zero-prize prize writeback guard | separate stale-handler fixture, raw exit2 |
| retained old hitboxes across presentation/subject changes | immediate canonical-key assertion before redraw, raw exit2 |
| reset input on EVERY same-shop ShowFor refresh | standalone shop press/refresh continuity assertion, raw exit2 |

The audit gate requires `sideshow_presentation >=92` plus native/zero-layout
semantic witnesses. Removing the ACTUAL default integrated
`SideshowPresentationChecks.Run` call leaves raw exit0/`PASS`, but classification
is `missing_coverage`, count0. Restoring it passes with count92. The viewer matrix
registers `sideshow-presentation` with minimum779 and keeps its normal closed-park
default. Python controls independently pin the floor, reject778 checks, reject a
FAIL even with exit0, and reject another scene's PASS witness.

## Reproduction commands

```sh
DISC=/home/ec2-user/tpw-ps2/tpw_ps2.bin
GODOT=/home/ec2-user/godot46/Godot_v4.6-stable_mono_linux_arm64/Godot_v4.6-stable_mono_linux.arm64

dotnet build game/TPWPS2Viewer.csproj
dotnet build tools/TPW.PS2.ParkSimAudit/TPW.PS2.ParkSimAudit.csproj
dotnet run --no-build --project tools/TPW.PS2.ParkSimAudit -- "$DISC" JUNGLE --sideshow-presentation-only
PYTHONPATH=tools:launcher python3 -m unittest discover -s tools -p 'test_*.py'

# New output directories must be outside Git. All eight parks, exact map witnesses.
python3 tools/viewer_matrix.py --disc "$DISC" --godot "$GODOT"   --out /tmp/sideshow-viewer-NEW --scenes sideshow-presentation --timeout 105
python3 tools/audit_matrix.py --disc "$DISC"   --out /tmp/sideshow-core-NEW --worlds JUNGLE --terrains 1

# Direct optional capture on an owned rendering display. No --shot= alternate startup.
DISPLAY=:114 "$GODOT" --path game --resolution 640x360   --rendering-method gl_compatibility --audio-driver Dummy   res://tests/SideshowPresentationSmoke.tscn -- --disc="$DISC" --map=JUNGLE   --mode=park --shotdir=/tmp/sideshow-shots-NEW
```

No assets are extracted. Local evidence logs/manifests are under
`/tmp/tpw-sideshow-winners-*`, including the six mutant JSON summaries, actual
missing-family control, and the final audio-cleanup viewer matrix.

## Remaining scope limits

Native spinner limits1..1000 were read but NOT changed by this presentation slice;
the existing port's0 floor/uncapped upper bound remains a separate discrepancy.
The1->0 GUI transition tests that existing port behavior, not native bound parity.
No loan/research UI, save/load, guest performance, model skinning or tour work is
included. The eight registered cases are ONE selected scene across eight parks,
not the entire viewer matrix, and not the full runtime/audit matrices.
