# Frontend wiring — 2026-09-28

Request `1554240169442414663`: intro movies, language screen, mouse main menu.
This supersedes the earlier "skip movies" instruction. Save/load work is paused
on `astraclaw/save-load` at `34adcc9`; this branch does not enable player saves.

## What runs

Bare launch: static EA image -> three-language screen -> BFLOGO -> legal/title
-> main menu. Up/down (also left/right PC adapter) clamps English/Deutsch/Français;
Enter/Space confirms. No guessed mouse hotspots over language art. A PC hint
explains keyboard selection. Enter/Space/click advances legal; Enter/Space/Esc
or click skips a movie. Actual Finished also advances. Missing/unreadable files
log the requested name and directory and continue without pretending playback.

Retail IDs 0/3/1 map to eng/ger/fre and English/German/French speech/lips. Selection
feeds all gameplay Viewer text-table calls, the main menu and advisor bindings.
The sound browser's independent diagnostic language remains independent. Port
placeholder/error/help strings are not newly translated. There is no language
persistence yet: this is a GAME-session choice, not per-park state. A future save
coordinator must retain session language outside its park snapshot.

Main-menu hover/click tests exactly the rectangles used to draw the text, after
viewport scaling/centering. Background clicks do not activate the selected row.
RMB/Esc cancels a submenu. New Game's Exit returns to the top page (not a blank
scene). Load/Options/Test Park retain explicit unavailable placeholders.

Lobby confirmation captures the selected map and plays DINO/FRANK/FLOWER/SPACEMAN
for worlds 0/1/2/3, then invokes the existing park-load path exactly once. No
invented once-ever seen flag. Main-menu return does not replay cold boot.

Explicit diagnostic `--menu`, `--menu-go`, `--map`, `--lobby`, asset/mode selectors
retain their direct routes; bare launch alone starts cold boot. Frontend input
precedes park/advisor/debug shortcuts. The park/calendar gate includes every
frontend phase, not just the menu. Movies fill a 4:3 letterbox; images and menu
keep the current square UI mapping pending the owner's separate aspect choice.

## Files: important correction from the actual playtest machine

Lookup precedence:
1. `--movies-dir=<directory>`
2. `TPW_PS2_MOVIES`
3. `movies` alongside the configured disc, if present
4. Windows-only existing `C:\claude-workspace\movies-ogv`

Explicit overrides are authoritative even when absent. File extension/stem
lookup is case insensitive. No converter, downloads, extraction, environment
changes or remote-machine access are performed by this code.

Cow tools corrected the initial directory report at message1554247225935396964:
`C:\claude-workspace\movies` decodes BFLOGO as **640x352 (raw)**;
`C:\claude-workspace\movies-ogv` decodes it as **640x480 (corrected)**.
Use the latter. The corrected files already have square pixels. No second SAR
multiplier. The new runner logs decoded dimensions too. Cow verified retail
BFLOGO/DINO decoder playback on the 4080; that is NOT an end-to-end validation
of this new boot sequence. EC2 tests below use a generated synthetic Theora.

## Evidence and explicit limits

Source order, art, language mapping, world clips: `main-menu.md` and
`frontend-startup-inventory.md`. FE125/225/325 and ALIEN are attract clips, NOT
cold-boot movies. END_F is the French ending; English/German use END_E. Neither
attract nor endings are newly implemented by this request.

EA duration is tied to work on console, with no fixed timer traced. Current
Viewer initialization is synchronous; the EA image is displayed afterwards for
3 s on screen (was two process passes until 2026-10-01; see "Input waits for the
screen" below), not a claimed measured retail delay.
No PS2 memory-card check is emulated. Legal prompt starts after160 frontend25Hz
updates; wall time conversion is an adapter assumption. Input is not artificially
locked until the prompt. Legal font sizing is still the existing font adapter,
not a fresh pixel-exact reconstruction. Language navigation/confirm sounds and
unresolved frontend model/hostess animation are not claimed implemented.

## Validation

`MainMenuStartupSmoke` (real Viewer, real Input.ParseInputEvent, rendered):
- bare launch, synthetic BFLOGO/DINO: **52 checks**;
- absent movie folder, explicit fallbacks: **47 checks**;
- explicit --menu bypass: **23 checks**;
- direct JUNGLE/1 --map/--mode control: **4 checks**, actual calendar advancing.

Includes all5 EA/language/legal512x512 assets decoded and drawn, lower/upper
language clamp, French text/speech mapping,160-update prompt, decoder Finished,
skip, late signal, frozen park/calendar, all4 independent world mappings, two
window shapes (960x540 and640x720), background click refusal, submenu Exit,
real lobby prompt -> DINO -> LoadMap, and return without cold boot replay.

`FrontendMovieSmoke`: **13 checks**, generated Theora fixture. Actual decoder,
case-insensitive filename, missing/bad file paths,4:3 rectangle, skip, natural
Finished, stale old-generation callback during replacement, continuation that
starts another movie, and disposal without invoking a dead-scene callback.

Six deliberate mutations all failed assertions (compiled, not build failures):
mouse filter Ignore; wrong language mapping; park tick during intro; wrong world
clip; omitted generation check; omitted Finished subscription. Originals restored
and rebuilt. Logs `/tmp/tpw-fe-mut-*`, startup `/tmp/tpw-frontend-{movies,missing,
menu,direct}.log`, lifecycle `/tmp/tpw-frontend-lifecycle.log`. These are behavior
and asset-decode checks, not a claim of pixel-perfect retail UI or audible output
from the Dummy audio backend.

## Regression gates / retail-file runner

Clean implementation commit `a24d6f2`: core8-park matrix4PASS+4exact known retail
failures (`landing_evidence=true`); runtime10/11, only existing FANTASY/1 holder
anchor mismatch (actual37.99974,-32.00022 vs37.994743,-31.941021); rendered viewer
subset entrance/staff/mechanic on JUNGLE/1 and HALLOW/2:6/6. Logs/manifests in
`/tmp/tpw-frontend-core-matrix`, `-runtime`, `-viewer`.

Cow tools rendered `a24d6f2` on the4080 with real language art, but SSH could not
supply keys, so that run stopped at language selection. Use the EXISTING smoke
scene (real Input.ParseInputEvent), not a new production auto-advance switch:

```
Godot --path game res://tests/MainMenuStartupSmoke.tscn -- \
  --disc=<owner disc> --movies-dir=C:\claude-workspace\movies-ogv \
  --frontend-full-movies
```

Harness now waits up to30s for BFLOGO (9.57s retail),90s for DINO (40.13s retail)
using monotonic wall time, not an FPS-dependent frame count. It selects French,
plays BFLOGO to completion, separately tests its skip, mouse-navigates both menu
pages and the lobby prompt, then plays DINO to completion when the test-only
`--frontend-full-movies` is present. Without it, DINO is skipped as before. The
full path passes53checks locally with synthetic movies; retail run still pending.
No headless flag: scene requires a renderer. Allow120s+ for startup/assets/movies.

## Retail-file confirmation (peer-run, 2026-09-28 21:56 UTC)

Cow tools reported Discord message `1554250523111202827`: built `4f3a735`
and ran the full-input smoke on the RTX4080 playtest machine using the actual
corrected retail rips in `C:\claude-workspace\movies-ogv`. Exit0,53checks:

```
[frontend] BFLOGO decoded=640x480, display=4:3
[frontend] BFLOGO: finished
[frontend] BFLOGO decoded=640x480, display=4:3
[frontend] BFLOGO: skipped
[frontend] DINO decoded=640x480, display=4:3
[frontend] DINO: finished
MAIN MENU STARTUP SMOKE PASS checks=53; direct=False
```

This closes the previously pending **retail-file frontend-sequence** test:
actual input selected French, BFLOGO completed and was separately skipped,
mouse menu/lobby navigation ran, DINO completed, and the park loaded. It does
not establish pixel-perfect UI aspect, all four world movies, or translated
speech audibility; those are separate claims. This is peer-run evidence, not
an EC2 test represented as remote execution. Tinyclaw's combined-main gate and
merge remain pending at this checkpoint.

## Input waits for the screen (2026-10-01)

strawberry, 2026-10-01: "the EA logo appears for a split second on launch. when clicking
through to skip, wait for the thing you are skipping to actually start playing for a moment
before skipping. when on the lobby screen, sometimes our clicks from the previous screen carry
through onto the lobby", then "from the main menu -> lobby", on `6f5d979`, which already had
the press/release guard (`6cffec3`).

**What.** `game/InputSettle.cs`. A screen ignores clicks and keys until it has been showing
for **0.5 s**. That covers every frontend stage (EA, language, movie, legal), each main-menu
page (top and New Game), the lobby on entry and the lobby's OK/Cancel prompt. A movie's half
second counts only while it is PLAYING with its clock past zero (`StreamPosition > 0`), not
from the frame `PlayMovie` was called. Hover still follows the pointer at once. EA is held
**3 s** on screen and can be skipped by a key or click once settled.

**Why the press/release guard did not cover it.** That guard drops a release whose press
began on another screen. The lobby is built synchronously, so a click made while it loads
waits in the OS queue and arrives as a complete press+release once it is up. A double click
on New Game is the same shape: its second half lands on the next page's row 0, Main Game.

**Clamped time.** Time on screen is counted in frame steps clamped to 1/30 s, so the load
stall itself counts as one short frame. Real time would read the stall as time on screen and
accept exactly the queued clicks this exists to swallow. The smoke stalls the main thread
1.2 s after the lobby opens, then clicks; a raw-delta mutation fails it.

**Chosen numbers.** 0.5 s and 3 s are both chosen. The PS2's screens poll the pad per update
with no such window, and its EA screen lasts as long as IOP module loading, which is untraced.
`InputSettle.Seconds` and `FrontendScreen.EaSeconds` are the knobs.

**Validation.** `MainMenuStartupSmoke`: 75 checks with no movies (640x360), 84 with synthetic
BFLOGO/DINO (640x360 and 1152x648), 4 on the direct `--map/--mode` control. New checks: EA
still up after five frames and held at least 3 s. In the first moment of EA, language, legal,
a playing movie, the main menu, the New Game page, the lobby (a whole click on the current
island, after a 1.2 s stall) and the lobby prompt, an input does nothing. Once settled, the
same input works; the lobby click raising the prompt is the control. Each of five
mutations, all compiling, fails its own check: lobby mouse gate off, EA hold off, menu click
gate off, prompt reset off, and raw delta instead of clamped. `AdvisorResearchSmoke` (which
walks the same screens with real input) waits for each screen and passes, 80 checks and 81 with
`--assert-no-lobby-leak`. The viewer matrix on JUNGLE/1 and SPACE/2 passes all 32 cases, and
the research-persistence standalone case passes at both sizes.
`FrontendMovieSmoke` calls `Confirm()` directly and is unaffected (13 with a 2 s fixture; its
300-frame wait for Finished is too short for a 6 s fixture on unchanged code as well).

**Limits.** Not run on the playtest machine; a real Windows lobby load was not timed.

