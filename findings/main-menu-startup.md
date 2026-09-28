# Default front end — September 28, 2026

User save/load request1554033583797178469 also asks normal startup to show Main Menu
and Main Game to reach the lobby. A bare launch now takes the existing --menu path;
explicit map/wad/mode/asset selectors keep their direct paths. The existing New Game
submenu's Main Game callback already entered the lobby; no alternate route added.

A separate park-running predicate gates normal simulation and calendar advancement
while Main Menu or lobby is displayed. Their presentation can run without ticking
a hidden park. MainMenuStartupSmoke drives real Confirm handlers and _Process:
bare menu, submenu, real lobby, no park/calendar advancement behind either; a direct
JUNGLE launch still loads and advances. Save-file loading remains a separate ongoing
package, NOT claimed by this startup change.

Repro (owner-disc environment; rendering display required):
- game/tests/MainMenuStartupSmoke.tscn, no --menu/map/mode arguments.
- same scene with --map="JUNGLE  terrain_1.mps" --mode=park.
