# Research persistence component — 2026-10-01

Independent slice from Staging-Tinyclaw `cd0a9ec`. The component is
`ResearchPersistence.Length/Save/Load`, acting on a ResearchManager with an attached
ResearchDatabase. It does NOT enable the Save Game menu or implement global cold
world restoration/publication. Staging's SaveGameRequested event remains unhandled.

## Native section and boundaries

Save0x1C2968: catalogue kinds3,7,6,1,2,4,5,8, ordinal order within each list.
For every item: capture byte L=Level(), ask P=Percent(at captured L), emit P THEN L.
Then0x1B66C0/0x1B7678 emit16 bytes: budget, five {active,category,item} triples.
Meaningful section length=2*catalogueCount+16; ordinary park lengths are
98,100,100,102,94,96,94,94 (Jungle/Hallow/Fantasy/Space, slots0/1).

Load0x160AC0 reads unsigned P/L and File()s ALL pairs before0x1B6720 restores the
unsigned budget and attempts slot restarts. Active is any nonzero byte, category
is unsigned, ITEM IS SIGNED (`lb` at0x1B6760). StartResearch's return is ignored.
Inactive triples do nothing, including on an existing busy manager; this is not an
in-place reset/wipe. Ordinarily the outer coordinator supplies a fresh park.

World/park context is NOT in the payload. Equal-length wrong-context payloads
cannot be detected here. The outer native stream aligns its final offset to four
bytes AFTER the manager block; alignment padding is neither represented nor
invented by this meaningful-section codec. Explicit testPark=true skips both
payloads. An attached database is required by this host API.

## Mutating save and byte-width correction

Save is NOT pure. For a group-zero simple item, a single invocation may capture
L3/P100 while leaving live level4/P0. A subsequent invocation captures L4/P100 and
leaves live level5. Native full save0x1C1D48 calls the traversal TWICE: sizing at
0x1C1D58, writing at0x1C1D88. Sizing suppresses copying, not research queries. Thus
a whole native two-pass save may write L4/P100 from an initially unpromoted item;
{100,3} is not an invariant of the whole-file path.

This codec's Save represents ONE research-routine invocation. Length only counts
bytes and is deliberately pure; it is not the native mutating sizing traversal.
A future console-shaped outer coordinator must handle its actual pass count.

File0x12BAF8 compares signed new level to the stored unsigned level BEFORE its
final byte stores (`sb` at0x12BB78/7C). A level255 with percent100 increments to256,
passes that comparison, then stores level0. The old int-only record fields missed
that wrap. Percent/level are now bytes, narrowed only at those final stores; the
usual monotonic rule still applies before narrowing, not after overflow.

## API guarantees and deliberate safety deviation

Save returns owned bytes, not aliases into live records. It omits exact progress,
required work, weights, completion/dirty flags, thresholds, debug flags and callbacks.
Load uses StartResearch to rebuild required work from the DBA and resume from the
filed whole percent. Native busy/threshold refusals stay ignored. Loading/restarting
is not a quantum or completion; it must not post advisor/jingle callbacks.

The host API preflights exact length and active category/signed-item keys BEFORE
any state writes. This avoids the unsafe native loader's invalid catalogue accesses.
Inactive garbage keys are still ignored. This validation is a documented defensive
deviation, not a claim the console rejects malformed data.

## Actual targeted evidence so far

- Component and game projects build (existing warnings remain, no errors).
- Explicit core suite:169 assertions PASS, including all eight ordinary catalogues,
  literal key/byte oracles based on raw DBA words, repeated/two-invocation save effects,
  byte wrap, budget narrowing, all five active slots, stopped/busy/refused projects,
  signed malformed items, atomic rejection, test-park skip and no callbacks.
- Python tool suite:106 tests PASS after adding the required research_persistence
  family floor169 and semantic witnesses. Default audit integration is wired.
- Six compiled mutations were rejected by assertions: swapped P/L bytes; saving the
  post-query rather than captured level; reverting widened record fields; restarting
  before filing DB pairs; unsigned item decode; stopping inactive saved triples.
  Sources were restored after each. Logs/JSON summaries: /tmp/tpw-research-persistence-*.

A test assumption was corrected after measurement: Jungle feature179 has native
research work ZERO. Its restored project correctly has required/progress0 and stays
active until a quantum; the test now checks that literal zero-work case, while the
other slots prove fractional-progress loss. This was not a game-code fix.

The169 assertions are explicit CORE fixtures, with read-only raw-record reflection
to avoid observer queries changing the database. They are not player save/load or
complete-world integration evidence.

## Shipping-client component fixture and controls

`ResearchPersistenceSmoke` passes24 checks at BOTH640x360 and1152x648. Two Viewers
initialize through normal direct-map startup, with no debug research override.
The test DECLARES its public File/StartResearch/Contribute setup on the first live
manager, captures its98-byte component, retires that Viewer normally, then applies
only the section to a distinct fresh shipping manager/database. It verifies saved
budget77 before normal Research-screen opening resets it to100, slot identity,
DBA-derived work, loss of a real sub-percent remainder, shared advisor/database
ownership, and the restored project's name/percent at actual draw sites. Navigation
to the Research page uses real keyboard/mouse input.

This is an explicitly seeded COMPONENT fixture: it does not claim a player-created
project, naturally hired researcher, Save-menu operation, or full-world restoration.
Its private access is read-only apart from existing audio cleanup calls. There are
no private field writes, manual park ticks, disabled processing or snapshot callbacks.
Draw receipts do not claim pixel review. It is intentionally standalone: there are
TWO map witnesses, unlike viewer_matrix's one-map per-case contract.

A compiled no-op Load control fails the live-target restoration assertion; restoring
Load passes24 again. Existing pure-input AdvisorResearchSmoke also passes73 at640x360.
The actual default core-family call was removed as a control: raw exit0/PASS remained,
but the audit gate correctly returned missing_coverage, research_persistence count0.
Restoring the call passes with169. Sources were restored and rebuilt after controls.

Final clean full-audit manifests (known retail failures remain classified, never
turned green) are the integration record, not these prose counts. This component
still does not enable the game's whole-save UI or implement world publication.

```sh
DISC=/home/ec2-user/tpw-ps2/tpw_ps2.bin
GODOT=/home/ec2-user/godot46/Godot_v4.6-stable_mono_linux_arm64/Godot_v4.6-stable_mono_linux.arm64
dotnet build tools/TPW.PS2.ParkSimAudit/TPW.PS2.ParkSimAudit.csproj
dotnet run --no-build --project tools/TPW.PS2.ParkSimAudit -- "$DISC" JUNGLE --research-persistence-only
PYTHONPATH=tools:launcher python3 -m unittest discover -s tools -p 'test_*.py'
# Own rendering display; no TPW_ALL_RESEARCHED=1 override. Also run at1152x648.
DISPLAY=:114 "$GODOT" --path game --resolution 640x360 --rendering-method gl_compatibility --audio-driver Dummy res://tests/ResearchPersistenceSmoke.tscn -- --disc="$DISC" --map=JUNGLE --mode=park
# All8 core parks; known HALLOW/SPACE retail failures stay red/classified.
python3 tools/audit_matrix.py --disc "$DISC" --out /tmp/research-persistence-core-NEW
```


## Named rendered-matrix registration (2026-10-01)

The component fixture is registered separately, NOT in the ordinary eight-park `SCENES`
list. It runs both 640x360 and 1152x648 and requires exactly **two JUNGLE/terrain_1** map
witnesses, one exact `RESEARCH PERSISTENCE SMOKE PASS checks=N;` with N >=24, no FAIL/error
or teardown leak, and successful process completion. Missing, extra, wrong or malformed map
witnesses fail. Ordinary scene cases still require exactly one requested map.

```sh
python3 tools/viewer_matrix.py --disc "$DISC" --godot "$GODOT" \
  --standalone-case research-persistence --out /tmp/research-persistence-viewer-NEW
```

The runner passes the **resolved authorized disc path**, literal `--map=JUNGLE` and
`--mode=park`, and strips inherited TPW overrides without adding `TPW_ALL_RESEARCHED`.
It validates the actual scene, resolution, renderer and user arguments before launch.
`--standalone-case` cannot be mixed with `--scenes` or `--parks`. Both resolution results
and the component-proof scope are recorded; success is `selected_cases_passed`, never an
all-eight-parks claim. Fresh-build/source/assembly stability checks remain active. The final
assembly check also closes a pre-existing gap: changing output during the last case now fails.

Disc-free controls exercise the actual main scheduler/launch contract, the exact two-map
classifier, count/semantic/FAIL controls, inherited-env isolation, both final snapshot gates,
and the unchanged ordinary all-scenes x eight-parks default. Python suite: **118 PASS**.
Mock process receipts are explicitly synthetic, not rendered evidence.

**CI boundary:** `.github/workflows/ci.yml` intentionally has no disc or Godot. Its existing
unittest discovery runs the new registration/parser/orchestration controls, but it does NOT
execute this rendered fixture. The named rendered gate is runnable locally on the authorized
host; no external runner, disc upload or new CI credentials were provisioned. This registration
must not be reported as automatic rendered CI coverage. Actual local matrix evidence follows
only after running the command above.
