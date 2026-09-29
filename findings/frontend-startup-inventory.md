# Frontend startup inventory (research only)

Scope: new intro/language/mouse request supersedes the old movie-skip request. No
implementation, extraction, conversion, network or commits performed. Owner disc
`/home/ec2-user/tpw-ps2/tpw_ps2.bin` was not modified or extracted. Sources below
are existing repository findings/source, plus bounded reads of the **already
existing** `/home/ec2-user/tpw-ps2/ps2.elf`. No rendered validation in this pass.

## Retail sequence — previously read, not a proposed ordering

`findings/main-menu.md`, “boot order”, scene table and state-thread table:

1. EA GAMES static logo, before archives/game initialization (`0x12d268`, called
   by `0x2314e8`). Embedded SHPS is identical to
   `FRONTEND.WAD/EAGames/EAGAMES.ssh`; not an EA movie. Duration is boot work,
   **not** a traced fixed timer.
2. LANGUAGE SELECT (`0x1555f8` Enter), once per boot.
3. MEMCARDCHECKSCREEN, then **BFLOGO.MPC**, then LEGALSCREEN (`0x13a998`).
   Memory-card/save implementation is outside this task; omitting this check in
   the port should be explicit, not presented as retail-exact.
4. LEGALSCREEN uses `/Mainmenu/Mainback1.ssh`, with copyright baked into art.
   After 160 updates, text row 877 (“Press START button to Continue”) appears;
   START proceeds. Menu is NOT Mainback1.
5. FRONT END uses `/Mainmenu/Mainback2.ssh`: New Game / Load Game / Options.
   New Game -> Main Game -> WORLD MAP lobby. Selecting an actual park gates
   that world's story movie before park entry (`0x13b3c8`).

Only Bullfrog is the cold-boot FMV. DINO/FRANK/FLOWER/SPACEMAN are not a chain of
boot intros. World indices 0/1/2/3 map respectively to those four names via
`0x35ed50`. ALIEN is attract-only. The detailed state-body trace gates the world
movie on actual park entry; it does **not** establish a persistent “seen once”
bit, despite the earlier summary wording “first entry”.

Attract (`0x13bb98`) is BFLOGO -> story -> reel, cycling pairs
DINO/FE125, FRANK/FE225, FLOWER/FE325, SPACEMAN/FE125, ALIEN/FE225.
Skip aborts the whole cycle, not just one clip. Retail player polls START or
raw bit 0x40. Menu/title idle thresholds are 450/6750 updates; wall-clock
18/270 seconds are an interpretation, not measured console timings.

## Language selection, art, default, and effects

Previously read in `main-menu.md`: three 512x512 images in FRONTEND.WAD:
`/Lang/LangUK.ssh`, `/Lang/LangGER.ssh`, `/Lang/LangFRE.ssh` (also TGA copies).
Names/map are baked into art. Pointer table `0x2b7520`; language-name table
`0x2b7510`. Visible order is **English / Deutsch / Français**, mapping to retail
IDs **0 / 3 / 1**, text stems **eng / ger / fre**, speech/lip directories
**English / German / French**. Selection clamps to 0..2, no wrapping; move sound
0xd6, confirm sound 0x128. No `.sce` layout exists for this screen.

Fresh bounded ELF word checks in this pass (no new ELF extraction):
- `0x155630 = sw zero,0x50(a0)` in Enter: **default selection is index 0,
  English**, not OS language inference. `+0x54=-1`, `+0x64=0` also initialized.
- Update `0x1557bc` tests `<3`, `0x1557c8` clamps to 2. Thus the fourth zero
  adjacent to `{0,3,1}` at `0x360630` is not a fourth selectable language.
  `findings/advisor-messages.md` §3.3 says “4-way”; its own table's fourth zero
  must not be used to add an option.
- Draw helper `0x1558a0` indexes textures from `this+0x58` by `this+0x50`, calling
  `0x213598/0x2135d8`. Use selected backdrop, not invented flag widgets.
  **Flag appearance/positions and exact hit rectangles were not visually
  inspected or traced here.** Do not claim exact language-screen geometry.
- `0x1553d8` calls `0x1dfa08` (text), `0x110a68` then `0x111c30` (audio),
  and `0x12fec8` (font registration). Repository findings identify its global
  language write and Japanese-font case; Japanese is not a selectable PAL row.

`findings/advisor-messages.md` §4.1/4.2 establishes the voice effect: language
change re-registers advisor class 11 with `AUDIO/ADVISOR/<LANG>/SPCHHD.SDT`
(and SPCHBANK/SPCHSFX maps); lips come from `LIPS.WAD/<Language>/<stem>.LIP`.
This is not just changing menu labels. Ending is END_F for French, END_E for
English/German. Loading art uses `_Fr` / `_Ger` / no suffix. Hostess files
`girl1_uk/ger/fre.mps` exist, but their use is unresolved; don't invent a 3D
hostess requirement. The retail frontend clip/voice linkage is also unresolved.

## Working APIs and current integration gaps

- `game/AssetLibrary.cs:103`, `ReadSide("FRONTEND.WAD", "/Lang/LangUK.ssh")`
  (likewise the other images, EA logo and Mainback1). Case-insensitive suffix
  lookup, skips aliases, caches a separate archive: does NOT replace the active
  world WAD. `game/MainMenu.cs:79` demonstrates the complete decode:
  `new Ssh(raw)` -> `Image.CreateFromData(width,height,false,Rgba8,pixels)` ->
  `ImageTexture.CreateFromImage`. No asset extraction/import is required.
- `core/TPW.PS2.Data/TextDatabase.cs`: `Load(wad,"eur")` already loads all
  available language tables; `Text("ger",id)` etc selects one. There is no
  automatic global language selection inside TextDatabase.
- Current menu `Label()` hardcodes `eng`; lobby's `LobbyName` and prompt labels
  also hardcode it. `Viewer.cs` contains additional hardcoded English calls
  (including `TextRow`, laptop rows, asset names). A shared selected-language
  mapping must feed callers; changing only the backdrop is not localization.
- `game/Viewer.Advisor.cs:60` has const `AdvisorAudioLanguage="English"` and
  `AdvisorTextLanguage="eng"`. `AdvisorSpeechBank()` caches `_advisorSpeech`
  guarded by `_advisorSpeechTried`; reset/rebind these on language change, and
  pass matching language to `_advisor.Bind(...)` for text, speech and lips.
  Stop active speech before switching mid-session. `_advisorLanguage` in the
  sound browser is a separate diagnostic selection, not the gameplay setting.
- `Viewer.cs` startup `_wantMenu` branch (~638) calls `EnterMainMenu()` directly.
  `Viewer.Lobby.cs:67` creates/shows it; `OnMenuChosen(MainGame)` enters lobby;
  `LobbyEnterPark()` (~500) immediately leaves lobby and calls `LoadMap(idx)`.
  These are the practical boot/world-movie continuation points. Do not replay
  cold boot every time `EnterMainMenu()` is reached from a park.
- `ParkSimulationRunning` currently excludes only lobby/open MainMenu. Extend
  that guard for startup/movie/legal/language phases; hiding MainMenu during
  an intro alone can otherwise release the simulation gate.
- `MainMenu` currently uses `MouseFilter.Ignore`; keys live in Viewer's
  `_UnhandledKeyInput`. Its actual drawn row rectangles are computed in `_Draw`:
  centered 512-square, scale `min(view.X,view.Y)/512`, x=256, y=250+32*i,
  text-width centered. Share this exact transform/geometry for hover/click,
  then reuse `Confirm()`. Consume frontend pointer events before park picking;
  no PS2 source establishes PC mouse behavior. Don't invent pixel rectangles
  for language art based on these **main menu** coordinates.

## Movies: existing converter/lookup, and missing local assets

`tools/rip_movies.py` is the established conversion recipe (READ ONLY, NOT RUN):
11 disc `/MOVIES/*.MPC` -> uppercase basename `.ogv`; EA demuxer handles source
MPEG-2 + EA ADPCM. Theora q7 + Vorbis q4, **scale=640:480,setsar=1**. Eight
non-FE clips are 640x352 SAR 11:15 (4:3 display); FE clips are 640x480. Godot
ignores source SAR, hence the baked correction. Preserve native 30 fps vs FE's
25 fps. Do not apply SAR correction twice or stretch the result into a square.
The source browser's “nothing off the shelf opens” message is stale; the
converter explicitly documents ffmpeg's native EA demuxer support.

`game/Viewer.cs:1234` `FillMovieList()` lookup:
1. `TPW_PS2_MOVIES` environment override;
2. else `<directory of _discPath>/movies`.
`ShowMovie()` (~1255) uses `new VideoStreamTheora { File = absolutePath }`,
assigns `_video.Stream`; `_video.Play()` starts playback. No Godot import and
no `.MPC` decoder required. `--movie-test=<path>`/`--movie-film` and
`MovieTestFrame()` are existing playback test hooks. Use a fullscreen 4:3
letterboxed player for startup rather than exposing the asset-browser pane.

**No existing converted `.ogv` was found in the searched local locations.**
`TPW_PS2_MOVIES` is unset here; `/home/ec2-user/tpw-ps2/movies` is absent.
Searches covered the owner-disc tree, `/home/ec2-user/tpwps2*`,
`/home/ec2-user/tpw`, the scratch tree (private/secret and .git dirs pruned),
`/tmp`, and `/var/tmp`; inaccessible paths were not inspected. This is a local
inventory result, NOT proof that no owner copy exists elsewhere. Do not claim
an available/validated movie file or silently initiate ripping. Owner must
supply an **already converted** directory through the established override.
Expected boot file: BFLOGO.ogv; world files: DINO.ogv, FRANK.ogv, FLOWER.ogv,
SPACEMAN.ogv; attract adds ALIEN.ogv, FE125.ogv, FE225.ogv, FE325.ogv;
ending adds END_E.ogv, END_F.ogv. Resolve stems case-insensitively on Linux.

## Minimum safe integration path (recommendation, not retail evidence)

Add a one-shot boot coordinator: static EA -> language callback -> Bullfrog
player -> legal -> existing menu. Keep explicit diagnostic map/lobby/movie
launches direct. Selection stores text/speech IDs before opening menu/lobby.
Reuse a movie runner with exactly-once Finished/skip/failure continuation and
input ownership; snapshot chosen world/map before queuing the world intro and
only then run existing park-entry continuation. Missing/unreadable OGV must
emit a clear filename/path diagnostic and continue safely, not hang startup
or claim successful playback. No save/load work is necessary to unblock this.

Remaining evidence gaps: real movie files/playback validation in this sandbox,
language art visual inspection/hitboxes, measured console timing, unresolved
hostess/frontend animation. Default/order/selection mapping and loader APIs
are sufficient to implement without guessing those details.
