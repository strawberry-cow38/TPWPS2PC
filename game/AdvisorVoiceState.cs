using Godot;
using System.Text.Json.Serialization;

namespace TPWPS2Viewer;

/// <summary>Presentation only, not advisor simulation or sound lookup. Never loads save-supplied paths.</summary>
public static class AdvisorVoiceState
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Snapshot
    {
        public required int Version { get; init; }
        public required bool HasPlayer { get; init; }
        public required string SoundAssetId { get; init; } // empty iff stream absent
        public required bool Playing { get; init; }
        public required bool HasPlayback {get;init;}
        public required bool Paused { get; init; }
        public required double PositionSeconds { get; init; }
    }
    public sealed class SnapshotBindings
    {
        public required string SoundAssetId { get; init; }
        public required AudioStream Stream { get; init; } // null only for absent stream
        public required string Bus { get; init; }
    }
    static void Require(bool ok) { if(!ok) throw new InvalidDataException("Invalid advisor voice state/binding"); }
    public static Snapshot CaptureState(AudioStreamPlayer player, string soundAssetId)
    {
        Require(soundAssetId != null && soundAssetId.Length<=1024 &&
            ((player?.Stream == null) == (soundAssetId.Length==0)));
        var s = new Snapshot {Version=1,HasPlayer=player!=null,SoundAssetId=soundAssetId,
            Playing=player?.Playing ?? false,HasPlayback=player?.HasStreamPlayback() ?? false,Paused=player?.StreamPaused ?? false,
            PositionSeconds=player?.GetPlaybackPosition() ?? 0};
        Validate(s); return s;
    }
    static void Validate(Snapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);
        Require(s.Version==1 && s.SoundAssetId!=null && s.SoundAssetId.Length<=1024 &&
            double.IsFinite(s.PositionSeconds) && s.PositionSeconds>=0 && s.PositionSeconds<=86400 &&
            (s.HasPlayer || (!s.Playing && !s.HasPlayback && !s.Paused && s.PositionSeconds==0 && s.SoundAssetId=="")) &&
            (!(s.Playing||s.HasPlayback) || s.SoundAssetId.Length>0) &&
            (!s.Playing || s.HasPlayback) &&
            (s.Playing || s.Paused && s.HasPlayback || s.PositionSeconds==0));
    }
    /// <summary>Owns a detached, stopped player (or null for original no-player state).
    /// Attach Player after commit, then call ApplyAfterCommit exactly once. Discard by Dispose.</summary>
    public sealed class Staged : IDisposable
    {
        readonly Snapshot _snapshot;
        bool _applied;
        public AudioStreamPlayer Player { get; }
        internal Staged(Snapshot s, SnapshotBindings b)
        {
            _snapshot=s;
            if(s.HasPlayer) Player=new AudioStreamPlayer { Stream=b.Stream,Bus=b.Bus,Autoplay=false };
        }
        public void ApplyAfterCommit()
        {
            if(_applied) throw new InvalidOperationException("Voice state already published");
            if(Player != null && !Player.IsInsideTree()) throw new InvalidOperationException("Attach staged voice after commit first");
            _applied=true;
            if(Player==null) return;
            // Pause before Play prevents even one audible buffer for paused loads.
            Player.StreamPaused=_snapshot.Paused;
            if(_snapshot.Playing || (_snapshot.Paused && _snapshot.HasPlayback)) Player.Play((float)_snapshot.PositionSeconds);
            Player.StreamPaused=_snapshot.Paused;
        }
        public void Dispose() { if(Player!=null && GodotObject.IsInstanceValid(Player)) {Player.StreamPaused=false;Player.Stop();Player.Stream=null;Player.Free();} }
    }
    public static Staged FromState(Snapshot s, SnapshotBindings b)
    {
        Validate(s); ArgumentNullException.ThrowIfNull(b);
        Require(b.SoundAssetId==s.SoundAssetId && ((b.Stream==null)==(s.SoundAssetId=="")) &&
            !string.IsNullOrWhiteSpace(b.Bus) && b.Bus.Length<=128);
        if(b.Stream!=null) {
            double length=b.Stream.GetLength();
            Require(double.IsFinite(length) && length>0 && s.PositionSeconds<=length+0.05);
        }
        return new Staged(s,b); // absolutely no Play/Seek or global AudioServer mutation
    }
}
