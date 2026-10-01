# Advisor research producers v21–v30 — 2026-10-01

Base: `Staging-Tinyclaw` at `9d2b95c`, including the research database/catalogue/UI.
This slice does not contain the independent `Staging-Catboy` lobby/performance/tour work.

## What changed

`AdvisorProducers` now reads the live `ResearchDatabase` rather than answering quiet
stand-ins for v21–v30. The shipping Viewer binds the same database used by its research
manager/screen. The keyed placement census covers live sim objects and registered
scriptless placements; scriptless entries now carry their compiled DBA key.

Explicit producer hooks still override the formulas. An isolated core consumer without
either an explicit database or a staff research database retains the old quiet defaults.
Neither the research system nor save/load is reimplemented here.

## Console reading

READ: `findings/advisor-rules.md` §5 and `findings/research.md` §4.3, plus the decompiles
of `0x103B20`, `0x1044B0`, `0x104A40` and helper `0x104830` supplied by tinyclaw.
Selected MIPS was reread from the owner's disc in memory, not committed as extracted
assets. The research audit checks the executable words behind the mask quirk, feature
priority, standing-status/distinct-type scan and max-installed-tier loop.

* Variety, v21/25/27/29: distinct **nonzero-status** types built / available base
  types, aggregated across selected kinds. A kind with no available types skips
  its placement scan. Individual built types are **not** tested for availability.
  Duplicates count once. Empty availability, or numerator >= denominator, gives 100.
* Research, v22/26/28/30: available base types / all catalogue types. Partial research
  is not a completed type. Preserve the shipped mask bug: bit 4 selects track rides
  **and coasters**; bit 8 is never tested. Any feature bit counts all feature types here.
* Feature variety differs: classify the catalogue DBA flags once in priority order
  toilet (mask 1) → camera (mask 8) → staff room (mask 2) → other. The placement's supplied
  flags do not replace those catalogue flags for this calculation.
* Upgrades in use, v23: sum the **maximum installed tier per built type** /
  sum(Level−1) for those built types. `0x104830` has **no status test**: status 0
  still participates. Unbuilt researched types do not enter this denominator.
* Upgrade research, v24 and producer 53's existing latch: the database's existing
  `UpgradePercent()` counts two possible upgrades per unlocked ride type. Preserve
  producer 53's shipped fallthrough to v76; this slice changes its input, not that quirk.

ADAPTER: native placements carry catalogue indices. The port maps (kind, DBA key) through
the current park's catalogue. Unknown/out-of-catalogue debug placements are excluded;
they have no native catalogue counterpart. The port's held blueprint is a ghost, not
a native pool object. No held-object subtraction is invented for these producers.

## Evidence

| Run on this slice | Result |
|---|---|
| Research audit, real PAL disc | 305 checks, zero failures; 291 are the new advisor/research checks |
| Existing advisor core audit, all four worlds × both ordinary parks | 8/8 passes, 82 checks per case |
| New rendered real-input `AdvisorResearchSmoke`, no research override | PASS, 72 checks with evidence images |
| Existing rendered `AdvisorSmoke`, JUNGLE1, its declared fixtures/overrides | PASS, 63 checks |
| Six formula mutations, each built and run independently | All rejected; restored source rebuilt and passed |

The core cases use literal start lists/percentages from the independently read catalogue
and research spec for all eight ordinary parks. Placements, tiers and `File` changes in
those cases are **explicit core fixtures**, not claims that gameplay constructed or
researched them. They cover partial-versus-completed research, live changes, duplicate
types, status differences, feature masks, empty catalogues, debug availability versus
actual levels, staff database ownership and explicit hook precedence.

Mutation failures: old no-database/quiet behavior 183; "fix" the native mask bug 4;
require each built type available 4; sum duplicate upgrades 1; omit status-0 upgrades 6;
return zero for empty denominators 57. A compile failure is not counted as a caught mutant.

The new Viewer tour uses ordinary `Input.ParseInputEvent` keys/clicks and the actual
window pointer (`WarpMouse`), with normal processing throughout. Private fields/hitboxes
are read only: no handler calls, injected placements/research, manual ticks, forced
needs or cursor override. It cold-boots to JUNGLE1, buys/deletes a bin, then enters
FANTASY1 and checks new advisor/database ownership and the different live values.
JUNGLE's bin **runs a zero-variable script** (`pelbin.mps`); it is not a scriptless
runtime control. This tour proves sim/registry deduplication, purchase/deletion and
map-reset wiring; actual scriptless registration remains covered by static review plus
explicit core inputs, not by this player sequence. Saved PNGs alone are not visual proof.

## Cross-screen input regression (separate owner)

On the staging base, the Close Park mouse release also raised the lobby's island
confirmation. Reported to cow tools, who fixed it independently as `6cffec3`.
The default tour labels this issue and dismisses the prompt through ordinary Cancel input
so the database reset can still be tested. It does **not** silently call the bug fixed.

`--assert-no-lobby-leak` makes that transition a strict regression: the base fails at
check 51; adding **only** cow's patch makes the complete tour pass 68 checks without
the unintended prompt. His patch was then removed and the own tree rebuilt. It is not
carried on this branch and must arrive through `Staging-Catboy`.

## Reproduce

```sh
dotnet build tools/TPW.PS2.ParkSimAudit/TPW.PS2.ParkSimAudit.csproj
dotnet run --no-build --project tools/TPW.PS2.ParkSimAudit -- "$DISC" JUNGLE --research-only
dotnet run --no-build --project tools/TPW.PS2.ParkSimAudit -- "$DISC" JUNGLE --advisor-only
dotnet build game/TPWPS2Viewer.csproj
DISPLAY="$DISPLAY" "$GODOT" --path game --rendering-method gl_compatibility \
  --audio-driver Dummy res://tests/AdvisorResearchSmoke.tscn -- --disc="$DISC"
```

Use a rendered display, no `--all-researched`, direct-map/setup flags or research override
environment. Optional `--advisor-research-shots=/absolute/folder` saves four evidence
images on the unfixed lobby base. After integrating `6cffec3`, add `--assert-no-lobby-leak`.
The audio driver in the reproduced EC2 run is Dummy: numerical/stream assertions are
not a claim that a person heard the advisor. Full viewer/audit/runtime merge matrices
remain the integration gate; these focused runs do not claim to replace all three.