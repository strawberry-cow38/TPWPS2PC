# Advisor browser integration gate

Measured September 23 US Eastern / September 24 UTC, 2026, on production baseline
`6aacf63`, with Godot `4.6.stable.mono.official.89cea1439`, SDK 4.6.2, Linux aarch64
and Dummy audio. New `game/tests/AdvisorBrowserAudit.tscn` runs **45 checks** against
the owner's disc read in place. No proprietary assets are written out.

## Boundary and evidence

The fixture calls the real Viewer.BuildUi, moves its controls/players into the audit
scene tree, and emits the actual Sounds-tab, bank-picker, sound-list and Play-button
signals. It does not run Viewer._Ready or Viewer._Process; unrelated model/park
startup is deliberately excluded. Regional TextDatabase objects are injected directly,
so this is a test of browser consumers, not environment/launcher locale selection.

The sequence is EUR English, EUR French, USA French, JAP German, then EUR English:

* OPEN_PARK is selected as `sp_001.mp2`; the full displayed dialogue section must equal
  the expected text and three-transition lip metadata. A valid substring surrounded
  by stale text is not sufficient. USA has no French text and must explicitly say so,
  not silently substitute English or retain the preceding French subtitle.
* Global catalogue/lip objects are reused by identity. This checks reuse, not a proof
  of immutability or absence of repeated validation/I/O. English is the cold-cache
  case; the other language transitions use the cache.
* Play must create a new 16-bit PCM stream matching independently selected language
  bank/slot 47, including bytes, rate and channels. Both sides use the same managed
  decoder: this is an integration/selection oracle, **not independent codec proof**.
* Actual Dummy mixer position advances. The preceding stream is not stopped by the
  fixture between Play presses, so stale-stream retention cannot pass unnoticed.
* Retail message 268's missing `PS2_.lip` and sound-stem mismatch stay visible for
  `PS2_1.mp2`. Missing lip metadata does not disable or prevent actual speech playback.
* A successfully loaded non-advisor bank/nonempty sound clears advisor presentation
  state; a failed bank load alone cannot make this check pass.
* UI teardown begins with speech active. The playback node is freed and all six
  retained stream resources return to their sole audit-owned native reference within
  a bounded retirement window, before those final references are disposed.

The final run exits 0 with 45/45 and no Godot ERROR/SCRIPT ERROR/resource-leak warnings.
The existing disc-backed AdvisorAudit also passes: 106 rules/275 messages plus its
identity, regional data and negative controls. These counts do not establish gameplay
reachability or semantic completeness.

## Controls and review

A narrow read-only subagent review strengthened exact dialogue matching, successful
ordinary-bank selection, active replacement and active teardown. During fixture
development a wrong assumption about the header's blank-line count caused five
assertion failures; correcting the fixture was not a production bug fix.

Six isolated temporary production mutations build successfully and fail assertions:

| Deliberate defect | Failed assertions |
|---|---:|
| Use English subtitle selection for every audio language | 3 |
| Hide missing French with an English fallback | 1 |
| Keep advisor-language state when leaving advisor banks | 1 |
| Describe absent lips as a successful zero-transition track | 1 |
| Play the adjacent sound slot instead of the selected slot | 5 |
| Keep the previous stream on subsequent Play presses | 4 |

All mutations were removed and the original Viewer rebuilt before the final 45/45
pass. No production Viewer edits belong to this package. Local evidence is in
`tpw-advisor-mutations/manifest.json`, `tpw-advisor-browser-final.log` and
`tpw-advisor-data-final.log` in project scratch.

## Remaining integration gaps

A current source-reference census finds no game consumer of AdvisorRules,
LipTrack.Playback, BitmapFont or KanjiTable. The sound browser consumes catalogue,
text and lip metadata; it does not run the advisor VM, drive a mouth mesh, or render
text through the decoded game font/Kanji pipeline. The exact VM scheduling policy
has documented executable evidence, but most gameplay-state producers and day-rate
integration remain unidentified/unwired. Do not invent those meanings to make an
OPEN_PARK demo look like a running advisor.

Signal emission proves callback wiring/state, not physical clicks, highlighted tab
state, focus, input routing, clipping, font glyph quality or layout. No screenshots
were inspected. German text in the JAP regional directory is **not Japanese glyph
rendering evidence**. Missing metadata-load recovery, cold French/German startup,
full Viewer startup, native output/listening, animated lips and complete world swaps
remain separate gates. Playback polling has a monotonic two-second observation
budget; severe host stalls can still invalidate a real-time fixture. Dummy is not
an audible-quality oracle.

## Reproduction

```sh
dotnet build game/TPWPS2Viewer.csproj
TPW_PS2_DISC="$DISC" "$GODOT" --headless --audio-driver Dummy --path game \
  res://tests/AdvisorBrowserAudit.tscn
dotnet run --project tools/TPW.PS2.AdvisorAudit -- "$DISC"
```
