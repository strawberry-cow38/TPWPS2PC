using System.Reflection;
using Godot;
using TPW.PS2.Data;

namespace TPWPS2Viewer.Tests;

/// <summary>Real sound-browser widget callbacks with disc-backed metadata and PCM playback.
/// The browser UI is built without Viewer._Ready/park startup. No assets are extracted.</summary>
public partial class AdvisorBrowserAudit : Node3D
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    int _checks, _bad;
    static FieldInfo Member(string name) => typeof(Viewer).GetField(name, Hidden)
        ?? throw new MissingMemberException($"Viewer.{name} — renamed or removed?");
    static T Field<T>(Viewer viewer, string name) => (T)Member(name).GetValue(viewer);
    static void Set(Viewer viewer, string name, object value) => Member(name).SetValue(viewer, value);
    void Check(bool ok, string message)
    {
        _checks++;
        GD.Print((ok ? "ADVISOR BROWSER ok: " : "ADVISOR BROWSER FAIL: ") + message);
        if (!ok) _bad++;
    }
    async Task WaitSeconds(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    static void SelectBank(OptionButton picker, string name)
    {
        int index = Enumerable.Range(0, picker.ItemCount).Single(i => picker.GetItemText(i).Contains(name, StringComparison.OrdinalIgnoreCase));
        picker.Select(index); picker.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
    }
    static void SelectSound(ItemList list, string name)
    {
        int row = Enumerable.Range(0, list.ItemCount).Single(i => list.GetItemText(i).StartsWith(name + " ", StringComparison.OrdinalIgnoreCase));
        list.Select(row); list.EmitSignal(ItemList.SignalName.ItemSelected, (long)row);
    }

    public override async void _Ready()
    {
        Viewer viewer = null;
        AudioStreamPlayer player = null;
        AssetLibrary lib = null;
        Node3D stage = null;
        var streams = new List<AudioStreamWav>();
        try
        {
            string disc = OS.GetEnvironment("TPW_PS2_DISC");
            if (string.IsNullOrWhiteSpace(disc) || !File.Exists(disc)) throw new InvalidOperationException("Set TPW_PS2_DISC to the existing disc image");
            lib = new AssetLibrary(disc);
            var dataFile = lib.WadFiles().Single(f => f.Path.Equals("/DATA/DATA.WAD", StringComparison.OrdinalIgnoreCase));
            var data = new WadArchive(lib.ReadDisc(dataFile));
            var texts = new[] { "eur", "usa", "jap" }.ToDictionary(s => s, s => TextDatabase.Load(data, s));
            viewer = new Viewer();
            Set(viewer, "_lib", lib); Set(viewer, "_discPath", disc); Set(viewer, "_text", texts["eur"]);
            (typeof(Viewer).GetMethod("BuildUi", Hidden)
                ?? throw new MissingMemberException("Viewer.BuildUi — renamed or removed?")).Invoke(viewer, null);
            // Enter the real controls and players into the tree without invoking the
            // unrelated park/model startup or Viewer._Process. All callbacks remain real.
            // This boundary must be revisited if callbacks acquire a Viewer-ancestor dependency.
            stage = new Node3D(); AddChild(stage);
            foreach (Node child in viewer.GetChildren()) { viewer.RemoveChild(child); stage.AddChild(child); }
            var tabs = Field<TabBar>(viewer, "_tabs");
            var picker = Field<OptionButton>(viewer, "_wadPick");
            var list = Field<ItemList>(viewer, "_rideList");
            var info = Field<Label>(viewer, "_info");
            var play = Field<Button>(viewer, "_playBtn");
            player = Field<AudioStreamPlayer>(viewer, "_player");
            Check(tabs.GetTabTitle(3) == "Sounds", "Sounds tab has its actual UI index, not the Mode enum ordinal");
            tabs.EmitSignal(TabBar.SignalName.TabSelected, 3L);
            Check(play.Visible && picker.ItemCount > 0, "Sounds tab callback populates banks and exposes playback");

            AdvisorCatalogue cachedCatalogue = null;
            WadArchive cachedLips = null;
            foreach (var (region, language, subtitle) in new[] {
                ("eur", "English", "People want to come in but\nyour park is closed. You\nshould think about opening\nup."),
                ("eur", "French", "Des visiteurs veulent entrer mais\nle parc est fermé.\nVous devriez l'ouvrir."),
                ("usa", "French", (string)null),
                ("jap", "German", "Die Leute wollen rein, aber dein\nVergnügungspark ist geschlossen.\nVielleicht solltest du mal drüber\nnachdenken, ob du ihn nicht öffnen\nwillst."),
                ("eur", "English", "People want to come in but\nyour park is closed. You\nshould think about opening\nup.") })
            {
                string name = region + "/" + language;
                Set(viewer, "_text", texts[region]);
                SelectBank(picker, "/ADVISOR/" + language + "/");
                SelectSound(list, "sp_001.mp2");
                Check(Field<string>(viewer, "_advisorLanguage") == language && Field<string>(viewer, "_advisorError") == null,
                      name + " bank selection supplies the correct advisor language");
                Check(info.Text.StartsWith("sp_001.mp2\n", StringComparison.Ordinal) && info.Text.Contains("Lip sync: 3 transitions."),
                      name + " selected row shows OPEN_PARK sound and lip metadata");
                string expectedDialogue = (subtitle ?? "No fre text in usa.") + "\nLip sync: 3 transitions.";
                string[] sections = info.Text.Split("\n\n");
                Check(sections.Length == 2 && sections[1] == expectedDialogue,
                      name + " exact subtitle or explicit missing-language result, without stale fallback");
                var catalogue = Field<AdvisorCatalogue>(viewer, "_advisor");
                var lips = Field<WadArchive>(viewer, "_advisorLips");
                Check(catalogue != null && lips != null && (cachedCatalogue == null || ReferenceEquals(catalogue, cachedCatalogue))
                      && (cachedLips == null || ReferenceEquals(lips, cachedLips)), name + " global catalogue/lips instances are reused across selection");
                cachedCatalogue = catalogue; cachedLips = lips;
                Check(!play.Disabled, name + " real selected speech is playable");
                // Independent bank/slot selection is the integration oracle. Sharing the
                // decoder does NOT establish codec correctness; that has separate evidence.
                var expectedFile = lib.SoundBanks().Single(f => f.Path.Contains("/ADVISOR/" + language + "/", StringComparison.OrdinalIgnoreCase));
                var expectedBank = new SoundBank(lib.ReadDisc(expectedFile));
                var expectedSound = expectedBank.Sounds[47];
                var decoded = Mpeg.DecodeToPcm16(expectedBank.Data[expectedSound.Start..expectedSound.End]).Value;
                play.EmitSignal(Button.SignalName.Pressed);
                var pcm = player.Stream as AudioStreamWav;
                Check(expectedSound.Name == "sp_001.mp2" && pcm != null && !streams.Any(old => ReferenceEquals(old, pcm))
                      && pcm.Format == AudioStreamWav.FormatEnum.Format16Bits && pcm.MixRate == decoded.Rate
                      && pcm.Stereo == (decoded.Channels == 2) && pcm.Data.AsSpan().SequenceEqual(decoded.Pcm),
                      name + " new stream contains the selected language/slot PCM, not prior playback");
                if (pcm != null) streams.Add(pcm);
                ulong deadline = Time.GetTicksMsec() + 2000;
                while (player.GetPlaybackPosition() <= 0 && Time.GetTicksMsec() < deadline) await WaitSeconds(.02);
                Check(pcm != null && pcm.GetLength() > 1 && player.GetPlaybackPosition() > 0
                      && !info.Text.Contains("could not play:"), name + " Play callback decodes Layer II and mixer position advances");
                // Do not manually stop here: the following Play must replace live speech.

            }

            SelectSound(list, "PS2_1.mp2");
            Check(info.Text.Contains("Lip sync unavailable.") && info.Text.Contains("Sound and lip names differ in the disc data.")
                  && !info.Text.Contains("Lip sync: "),
                  "known retail missing-lip/stem mismatch remains visible rather than falsely successful");
            Check(!play.Disabled, "missing lip metadata does not disable the actual speech clip");
            play.EmitSignal(Button.SignalName.Pressed);
            var defectStream = player.Stream as AudioStreamWav;
            if (defectStream != null) streams.Add(defectStream);
            ulong defectDeadline = Time.GetTicksMsec() + 2000;
            while (player.GetPlaybackPosition() <= 0 && Time.GetTicksMsec() < defectDeadline) await WaitSeconds(.02);
            Check(defectStream != null && player.GetPlaybackPosition() > 0 && info.Text.StartsWith("PS2_1.mp2\n"),
                  "missing lip metadata does not prevent actual speech playback");
            int ordinary = Enumerable.Range(0, picker.ItemCount).First(i => !picker.GetItemText(i).Contains("/ADVISOR/", StringComparison.OrdinalIgnoreCase));
            picker.Select(ordinary); picker.EmitSignal(OptionButton.SignalName.ItemSelected, (long)ordinary);
            var ordinaryBank = Field<SoundBank>(viewer, "_bank") ?? throw new InvalidOperationException("ordinary bank failed to load");
            string ordinarySound = ordinaryBank.Sounds.First(sound => !sound.IsEmpty).Name;
            SelectSound(list, ordinarySound);
            Check(info.Text.StartsWith(ordinarySound + "\n", StringComparison.Ordinal) && !play.Disabled,
                  "ordinary-bank control successfully loads and selects a nonempty sound");
            Check(Field<string>(viewer, "_advisorLanguage") == null && Field<string>(viewer, "_advisorError") == null
                  && !info.Text.Contains("Lip sync:") && !info.Text.Contains("Lip sync unavailable.")
                  && !info.Text.Contains("People want to come in"), "ordinary bank clears advisor-only presentation state");
            Check(player.Playing, "teardown begins with speech still active, not a manually stopped fixture");
            stage.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(!GodotObject.IsInstanceValid(player), "browser UI teardown frees the actual playback node");
            ulong retirementDeadline = Time.GetTicksMsec() + 2000;
            while (streams.Any(stream => stream.GetReferenceCount() != 1) && Time.GetTicksMsec() < retirementDeadline) await WaitSeconds(.02);
            Check(streams.Count == 6 && streams.All(stream => stream.GetReferenceCount() == 1),
                  "all replaced/active speech streams retire to their sole audit-owned native reference");
            GD.Print($"ADVISOR BROWSER {(_bad == 0 ? "PASS" : "FAIL")}: {_checks} checks, {_bad} failures");
            GetTree().Quit(_bad == 0 ? 0 : 2);
        }
        catch (Exception ex) { GD.PrintErr("ADVISOR BROWSER ERROR: " + ex); GetTree().Quit(2); }
        finally
        {
            if (GodotObject.IsInstanceValid(player)) { player.Stop(); player.Stream = null; }
            if (GodotObject.IsInstanceValid(stage) && !stage.IsQueuedForDeletion()) stage.QueueFree();
            if (GodotObject.IsInstanceValid(viewer)) viewer.Free();
            foreach (var stream in streams.Distinct()) stream.Dispose();
            lib?.Dispose();
        }
    }
}
