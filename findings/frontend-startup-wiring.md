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
two process passes to permit a drawn frame, not a claimed measured retail delay.
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
