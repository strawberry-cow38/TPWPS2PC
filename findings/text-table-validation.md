# Compiled text-table structural validation

Measured September 23 US Eastern / September 24 UTC, 2026. The production change is
confined to `TextDatabase.ParseTable`; it does not assign new text semantics, decode
Japanese glyphs, change Latin1 byte preservation, or alter missing-region policy.

## Reproduction and fix

Before the fix, 61 synthetic checks produced **21 failures**: 12 malformed tables were
silently accepted and nine failed with incidental argument/overflow exceptions instead
of the format error expected by callers. Accepted examples included missing NULs,
offsets into header/directory bytes, an offset at EOF, overlapping/reversed rows, gaps,
trailing bytes and payload after an empty table. A count was used for output allocation
before proving its directory/payload could fit.

The reader now requires:

* A complete four-byte little-endian count, with at least four offset bytes plus one
  terminating NUL per row available before any count-sized allocation.
* Every offset exactly following the directory or the preceding row's terminating NUL.
* A terminator for every row, and final consumption exactly at EOF.

Malformed structure raises InvalidDataException. Null is an API argument error and
raises ArgumentNullException. A canonical four-byte zero-row table remains valid.
These are the already independently checked constraints of this disc's compiled
regional tables, not a claim that all conceivable offset-table formats forbid aliasing
or padding. Plain-text final*.dat masters and version.dat are not fed to this parser.

The count bound is computed by division before directory-size multiplication. Accepted
strings occupy disjoint contiguous ranges, so searches/decoding are linear in input
length rather than repeatedly scanning overlapping suffixes. The fix does not add a
new arbitrary row-count maximum or normalize whitespace/format specifications.

## Permanent regression checks

`tools/TPW.PS2.AdvisorAudit/TextTableChecks.cs` runs with the normal disc-backed audit,
or separately without a disc. Final result: **73 checks, zero failures**. Coverage includes
malformed counts/directories/offsets/NULs/gaps/trailers; exact empty/Latin1/newline/tab/
format-string preservation; unchanged inputs; consecutive and final empty rows;
literal little-endian offsets beyond byte 255; and deterministic mixed-row round trips.

A million-row header in four input bytes must reject without a count-sized allocation:
the measured current-thread allocation for rejection stays below the generous 64 KiB
regression threshold. This tests allocation order, not a production file-size policy.
Extreme Int32.MaxValue/high-bit counts are enabled only after the bounded reader is in
place; they were deliberately not sent to the old unbounded allocator during baseline
reproduction. The bounded million-row negative control allocates only about 8 MB when
the guard is deliberately removed, rather than risking a multi-gigabyte test process.

A read-only subagent review found no parser correctness issue and suggested additional
independent/end-boundary fixtures; these were added before final validation.

Six temporary source mutations build and are rejected:

| Defect | Failed assertions |
|---|---:|
| Read count before checking header length | 4 |
| Remove the pre-allocation count bound | 2 |
| Allow noncontiguous offsets | 3 |
| Accept EOF instead of a final NUL | 1 |
| Ignore trailing bytes | 3 |
| Decode Latin1 bytes as UTF-8 | 13 |

Mutation runs omit the two extreme-count cases for safety (69 checks); the restored
reader runs all 73. No mutants remain. Local evidence:
`tpw-text-before.log`, `tpw-text-mutations/manifest.json`, and
`tpw-text-final-regional.log` in project scratch.

## Real-data and runtime compatibility

The existing AdvisorAudit's independent offset/NUL/whole-byte checks remain green for
all 28 compiled tables (25 language tables plus three id tables) across EUR/USA/JAP.
Its complete 106-rule/275-message identity and negative-control audit passes too.
Viewer rebuild succeeds; AdvisorBrowserAudit45 and RideSoundLifecycleAudit37 pass on
Godot4.6 with Dummy audio. This is not a warning-free build, an audible test, or a
Japanese-rendering claim. No disc contents were extracted or rewritten.

TextDatabase.Load's pre-existing archive-read exception/missing-file behavior and
cross-table row-count policy are unchanged. The strict parser does not certify a
semantically altered but structurally valid translation or unsupported executable.

```sh
dotnet run --project tools/TPW.PS2.AdvisorAudit -- --text-self-test --extreme-counts
dotnet run --project tools/TPW.PS2.AdvisorAudit -- "$DISC"
```
