using Godot;

namespace TPWPS2Viewer;

/// <summary>⭐ Where the music starts and stops -- the park's on park load, the lobby's on entering and on every
/// move, nothing on the front end (no front-end music map exists on the disc; the menu's sound is its MPC
/// movies'). The READ behind each hook is in <see cref="GameMusic"/>.</summary>
public partial class Viewer
{
    GameMusic _music;
    /// <summary>`--music-by-guests` (or `TPW_MUSIC_BY_GUESTS=1`): hand the park's guest value to the music
    /// event's own steering selector instead of the console's 2, so the authored levels play. OFF by default:
    /// the PS2 stays on level 1 (see <see cref="TPW.PS2.Data.MusicSequencer"/>).</summary>
    bool _musicByGuests;

    GameMusic Music()
    {
        if (_music == null && _lib?.Disc != null) _music = new GameMusic(this, _lib.Disc);
        if (_music != null) _music.ByGuests = _musicByGuests;
        return _music;
    }

    /// <summary>`0x111E30(audio, world)`: the open world's `MUSIC/MUSSFX.MAP`, event 2.</summary>
    void StartParkMusic()
    {
        string world = System.IO.Path.GetFileNameWithoutExtension(_lib?.WadName ?? "").ToUpperInvariant();
        if (world.Length == 0) return;
        Music()?.PlayPark(world);
    }

    /// <summary>`0x2195C0`: the selected record's world byte picks the lobby music; 4 and up play nothing.</summary>
    void StartLobbyMusic()
    {
        if (_lobbySlots == null || _lobbyRecord < 0 || _lobbyRecord >= _lobbySlots.All.Count) return;
        string world = _lobbySlots.All[_lobbyRecord].World switch
        {
            0 => "JUNGLE", 1 => "HALLOW", 2 => "FANTASY", 3 => "SPACE", _ => null
        };
        if (world == null) { _music?.Stop(); return; }
        Music()?.PlayLobby(world);
    }

    void StopMusic() => _music?.Stop();
}
