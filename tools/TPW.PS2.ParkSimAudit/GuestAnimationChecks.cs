using TPW.PS2.Data;
using Aps = TPW.PS2.Data.Animation;

/// <summary>⭐⭐ DOES EVERY GUEST HAVE AN IDLE TO PLAY? The viewer left a standing guest in BIND
/// POSE -- arms out, dead still -- for as long as this port has had guests, and master saw it the
/// moment they watched a crowd: "we're also missing the idle/wait animations for visitors".
///
/// ⚠ THIS CHECKS THE SELECTION, NOT THE PLAYBACK. Whether the pose actually moves on screen needs
/// a render; what can be checked here is the part that fails SILENTLY -- a record that resolves to
/// null, leaving one particular kid frozen while the rest of the crowd fidgets, which is exactly
/// the kind of thing nobody notices in a screenshot.
///
/// ⚠⚠ AND IT MUST FOLLOW THE SHARED-RECORD RULE. Boy2a/3a/4a carry flag-0x80 records whose tracks
/// live in Boy1a's file; a check that just asked "is there a slot-2 record" would pass on a record
/// with no tracks in it, which is a bind pose wearing a record's name.</summary>
static class GuestAnimationChecks
{
    /// <summary>The kids the viewer actually spawns -- the same list as `Viewer.Kids`.</summary>
    static readonly string[] Kids =
    {
        "/Chars/Girl1a/girl1a.mps", "/Chars/Boy1a/boy1a.mps",
        "/Chars/Girl2a/girl2a.mps", "/Chars/Boy2a/boy2a.mps",
        "/Chars/Girl3a/girl3a.mps", "/Chars/Boy3a/boy3a.mps",
        "/Chars/Girl4a/girl4a.mps", "/Chars/Boy4a/boy4a.mps",
    };

    public static void Run(WadArchive data, Action<bool, string> check)
    {
        void Check(bool ok, string message) => check(ok, "guest anim: " + message);

        Aps Load(string mps)
        {
            string path = System.IO.Path.ChangeExtension(mps, ".aps");
            var entry = data.Find(path);
            return entry == null ? null : new Aps(data.Read(entry));
        }
        // The viewer's own rule: own file when its record has tracks, else the family's *1a file.
        Aps.Record Pick(string mps, int slot)
        {
            var own = Load(mps);
            var rec = own?.Records().FirstOrDefault(r => r.Slot == slot && r.Skeletal && !r.Shared);
            if (rec != null) return rec;
            string family = System.Text.RegularExpressions.Regex.Replace(
                mps, @"(Boy|Girl)\d[a-z]", "${1}1a", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return family.Equals(mps, StringComparison.OrdinalIgnoreCase) ? null
                 : Load(family)?.Records().FirstOrDefault(r => r.Slot == slot && r.Skeletal && !r.Shared);
        }

        int withIdle = 0, withWalk = 0;
        foreach (string kid in Kids)
        {
            var walk = Pick(kid, 1);
            var idle = Pick(kid, 2);
            if (walk != null) withWalk++;
            if (idle != null) withIdle++;
            Check(idle != null, $"{System.IO.Path.GetFileName(kid)} resolves a slot-2 idle with tracks"
                              + (idle == null ? "" : $" ({idle.DurationFrames} frames)"));
        }
        // ⭐ THE CONTROL. Slot 1 is the walk and it has worked on screen for weeks, so if the same
        // resolver came back empty for it the resolver is what is broken, not the idle data --
        // and every line above would be reporting the wrong thing.
        Check(withWalk == Kids.Length, $"CONTROL: the same resolver finds every kid's WALK too ({withWalk} of {Kids.Length})");
        Check(withIdle == Kids.Length, $"every kid the viewer spawns can idle ({withIdle} of {Kids.Length})");

        // ⭐⭐ SIX VARIANTS IS WHY THE CROWD IS NOT A CHORUS LINE. The viewer spreads guests over
        // them by id; if the count were 1 that spreading would be a no-op and worth knowing.
        var boy = Load("/Chars/Boy1a/boy1a.mps");
        int variants = boy?.Records().Count(r => r.Slot == 2 && r.Skeletal && !r.Shared) ?? 0;
        Check(variants > 1, $"boy1a carries more than one idle variant to spread a crowd across ({variants})");
        // ⭐⭐ AND ENOUGH OF THEM TO CYCLE. `FUN_002106E8` picks among **four** idle states
        // (`FUN_001448E0(4)` over `DAT_002EEC18`), and the viewer cycles a standing guest through
        // that many every 120 ticks. ⚠ With fewer than four records the cycle would quietly
        // collapse to however many exist -- still correct, but no longer the console's count, and
        // nobody would notice from a screenshot.
        Check(variants >= 4, $"and enough to cycle the console's four idle states ({variants})");
    }
}
