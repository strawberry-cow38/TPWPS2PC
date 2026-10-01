# Research persistence component — 2026-10-01 (integration pending)

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

These are explicit CORE fixtures, with read-only raw-record reflection to avoid
observer queries changing the database. They are not player save/load, a rendered
client roundtrip, or complete-world integration evidence. Those distinctions remain
part of the handoff. Further integration checks are pending before a ready claim.

```sh
DISC=/home/ec2-user/tpw-ps2/tpw_ps2.bin
dotnet build tools/TPW.PS2.ParkSimAudit/TPW.PS2.ParkSimAudit.csproj
dotnet run --no-build --project tools/TPW.PS2.ParkSimAudit -- "$DISC" JUNGLE --research-persistence-only
PYTHONPATH=tools:launcher python3 -m unittest discover -s tools -p 'test_*.py'
```
