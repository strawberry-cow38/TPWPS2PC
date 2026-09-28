using Godot;
using System.Text.Json.Serialization;
using TPW.PS2.Data;

namespace TPWPS2Viewer;

public partial class Viewer
{
    /// <summary>Trusted, snapshot-local identity services. Asset IDs are not filesystem paths.
    /// Core must contain the actual scheduler producer/day references on capture, and NEW-world
    /// references on restore. In particular a freshly constructed producer is not a capture binding.
    /// Scheduler exposes these through StateProducers/StateDay. No reflection is used by this adapter.</summary>
    public sealed class AdvisorStateServices
    {
        public required Func<object, string> IdentifyAsset { get; init; }
        public required Func<string, object> ResolveAsset { get; init; }
        public required Func<Viewer, ParkAdvisor, IAdvisorHead, AdvisorStateBindings> Core { get; init; }
        public AdvisorProducers CaptureProducers { get; init; }
        // Called after restoring producer topology, before hydrating the advisor shell.
        public Action<AdvisorStateBindings, AdvisorProducers> BindProducers { get; init; }
        public Func<AdvisorHead.Snapshot, AdvisorHead.SnapshotBindings> Head { get; init; }
        public Func<Viewer,AdvisorStackView.StateBindings> Stack {get;init;}
        public string HeadModelId { get; init; }
        public string HeadAnimationId { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record AdvisorBindingCache(int Message, int Variant, string Asset);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record AdvisorStreamCache(ushort Sound, string Asset);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ViewerAdvisorState
    {
        public required int Version { get; init; }
        public required bool SimEmitter {get;init;}
        public required bool EventEmitter {get;init;}
        public required bool RemovalEmitter {get;init;}
        public required bool StaffEmitter {get;init;}
        public required ParkAdvisor.Snapshot Core { get; init; }
        public required AdvisorProducers.Snapshot Producers { get; init; }
        public required AdvisorHead.Snapshot Head { get; init; }
        public required AdvisorStackView.Snapshot StackView {get;init;}
        public required AdvisorHeadChannel.Snapshot TimedHead { get; init; }
        public required AdvisorVoiceState.Snapshot Voice { get; init; }
        public required GameAudioMix.Snapshot Mix { get; init; }
        public required string Catalogue { get; init; }
        public required string Rules { get; init; }
        public required string Speech { get; init; }
        public required string Lips { get; init; }
        public required string BoxFont { get; init; }
        public required bool SpeechTried { get; init; }
        public required bool Unavailable { get; init; }
        public required bool BoxFontTried { get; init; }
        public required bool SettingsBound { get; init; }
        public required bool L2 { get; init; }
        public required bool Triangle { get; init; }
        public required string BrowserLanguage { get; init; }
        public required string BrowserError { get; init; }
        public required AdvisorBindingCache[] Bindings { get; init; }
        public required AdvisorStreamCache[] Streams { get; init; }
    }

    static void AS(bool ok, string why)
    { if (!ok) throw new InvalidDataException("Viewer advisor: " + why); }

    /// <summary>Quiescent capture of actual Viewer fields. Null cache entries (failed decode/bind)
    /// are retained, not retried. Stock stack *logical* state is exact. The stack drawing widget has
    /// no owner DTO; fail closed rather than lose its smoothed scroll/art-failure caches.</summary>
    public ViewerAdvisorState CaptureAdvisorState(AdvisorStateServices b, bool quiescent)
    {
        ArgumentNullException.ThrowIfNull(b);
        AS(quiescent, "quiescent boundary required");
        AS(_advisorStackView==null||b.Stack!=null,"stack-view provider bindings required");
        string Id(object o) => o == null ? null : b.IdentifyAsset(o)
            ?? throw new InvalidDataException("Missing advisor asset ID");
        var head = _parkAdvisor?.Head;
        AS(head == null || ReferenceEquals(head, _advisorHead) || head is AdvisorTimedHead, "external head owner not supported");
        AS(_advisorHead == null || ReferenceEquals(head, _advisorHead), "split head owner");
        var actualProducers=_parkAdvisor?.Scheduler?.StateProducers as AdvisorProducers;
        AS(_parkAdvisor?.Scheduler == null || actualProducers!=null, "actual supported scheduler producers required");
        AS(b.CaptureProducers==null||ReferenceEquals(b.CaptureProducers,actualProducers),"capture must use the actual scheduler producer");
        var core = b.Core(this, _parkAdvisor, head);
        bool Bound(Delegate actual,Delegate stock){if(actual==null)return false;AS(stock!=null&&actual.Equals(stock),"non-stock advisor emitter requires explicit owner");return true;}
        return new() { Version=1,
            SimEmitter=Bound(_sim?.Advisor,_parkAdvisor?.StateSimAdvisor),EventEmitter=Bound(_sim?.AdvisorEvent,_parkAdvisor?.StateEvent),
            RemovalEmitter=Bound(_sim?.ObjectRemoved,_parkAdvisor?.StateObjectRemoved),StaffEmitter=Bound(_staff?.Advisor,_parkAdvisor?.StateStaffAdvisor), Core=_parkAdvisor?.CaptureState(core, true),
            Producers=actualProducers?.CaptureState(core),
            Head=_advisorHead?.CaptureState(b.HeadModelId,b.HeadAnimationId),
            StackView=_advisorStackView?.CaptureState(b.Stack(this)),
            TimedHead=(head as AdvisorTimedHead)?.Channel.CaptureState(),
            Voice=AdvisorVoiceState.CaptureState(_advisorVoice,Id(_advisorVoice?.Stream) ?? ""),
            Mix=_audioMix.CaptureState(), Catalogue=Id(_advisor), Rules=Id(_advisorRules),
            Speech=Id(_advisorSpeech), Lips=Id(_advisorLips), BoxFont=Id(_advisorBoxFont),
            SpeechTried=_advisorSpeechTried, Unavailable=_advisorUnavailable, BoxFontTried=_advisorBoxFontTried,
            SettingsBound=_advisorSettingsBound, L2=_advisorL2, Triangle=_advisorTriangle,
            BrowserLanguage=_advisorLanguage, BrowserError=_advisorError,
            Bindings=_advisorBindings.Select(p=>new AdvisorBindingCache(p.Key.Message,p.Key.Variant,Id(p.Value))).ToArray(),
            Streams=_advisorStreams.Select(p=>new AdvisorStreamCache(p.Key,Id(p.Value))).ToArray() };
    }

    /// <summary>Join AFTER logical core + actors, onto a detached unpublished Viewer. The caller
    /// supplies asset bindings verified against its manifest and references from WorldCoreRegistry.
    /// Never calls Speak, Play, AttachAdvisor, ParkAdvisor.Attach, Reset, or EnsureAdvisorViews.
    /// Settings subscriptions, sim/staff emitters, stack widget and scene insertion belong to parent.
    /// A failure leaves this stage unusable; discard the entire unpublished world.</summary>
    public StagedAdvisor RestoreAdvisorState(ViewerAdvisorState s, AdvisorStateServices b)
    {
        ArgumentNullException.ThrowIfNull(s); ArgumentNullException.ThrowIfNull(b);
        AS(!IsInsideTree() && GetParent()==null && _parkAdvisor==null && _advisorHead==null &&
            _advisorVoice==null && _advisorStackView==null, "fresh unpublished advisor owner required");
        AS(s.Version==1 && s.Bindings!=null && s.Bindings.Length<=1100 && s.Streams!=null && s.Streams.Length<=65536,
            "version/cache bounds");
        AS(s.Bindings.All(x=>x!=null && x.Message>=0 && x.Message<AdvisorCatalogue.MessageCount && x.Variant is >=0 and <=3)
            && s.Bindings.Select(x=>(x.Message,x.Variant)).Distinct().Count()==s.Bindings.Length
            && s.Streams.All(x=>x!=null) && s.Streams.Select(x=>x.Sound).Distinct().Count()==s.Streams.Length,"cache keys");
        AS(s.Head==null || s.TimedHead==null,"two head owners");
        AS(s.Core!=null || s.Head==null && s.TimedHead==null && s.Producers==null,"orphan advisor owners");
        AS(s.Core?.Scheduler==null || s.Producers!=null,"missing producers");
        T Asset<T>(string id) where T:class => id==null ? null : b.ResolveAsset(id) as T
            ?? throw new InvalidDataException("Advisor asset type/ID: "+id);
        var catalogue=Asset<AdvisorCatalogue>(s.Catalogue); var rules=Asset<AdvisorRules>(s.Rules);
        var speech=Asset<SoundBank>(s.Speech); var lips=Asset<WadArchive>(s.Lips); var font=Asset<FontText>(s.BoxFont);
        var bindings=s.Bindings.ToDictionary(x=>(x.Message,x.Variant),x=>Asset<AdvisorCatalogue.Binding>(x.Asset));
        var streams=s.Streams.ToDictionary(x=>x.Sound,x=>Asset<AudioStreamWav>(x.Asset));
        var mix=new GameAudioMix(); mix.RestoreState(s.Mix);
        AdvisorHead drawn=null; AdvisorVoiceState.Staged voice=null;
        try {
            drawn=s.Head==null?null:AdvisorHead.FromState(s.Head,b.Head?.Invoke(s.Head)
                ?? throw new InvalidDataException("Advisor head asset bindings required"));
            IAdvisorHead head=drawn;
            if(s.TimedHead!=null) { var timed=new AdvisorTimedHead();timed.Channel.RestoreState(s.TimedHead);head=timed; }
            var shell=s.Core==null?null:ParkAdvisor.AllocateShell();
            var core=b.Core(this,shell,head);
            if(s.Producers!=null) {
                var producers=AdvisorProducers.FromState(s.Producers,core);
                AS(ReferenceEquals(producers.Clock,_calendar) && ReferenceEquals(producers.Sim,_sim)
                    && ReferenceEquals(producers.Staff,_staff) && ReferenceEquals(producers.Visitors,_visitors),"producers must target NEW world");
                AS(b.BindProducers!=null,"producer registry binder required"); b.BindProducers(core,producers);
            }
            AS(s.Core==null || ReferenceEquals(core.Resolve<AdvisorCatalogue>(s.Core.CatalogueId),catalogue),"split catalogue asset");
            AS(s.Core?.Scheduler==null || ReferenceEquals(core.Resolve<AdvisorRules>(s.Core.Scheduler.RulesId),rules),"split rules asset");
            shell?.Hydrate(s.Core,core);
            AS(shell==null || ReferenceEquals(shell.Clock,_calendar) && ReferenceEquals(shell.Head,head),"split advisor clock/head");
            voice=AdvisorVoiceState.FromState(s.Voice,new(){SoundAssetId=s.Voice.SoundAssetId,
                Stream=Asset<AudioStream>(s.Voice.SoundAssetId==""?null:s.Voice.SoundAssetId),Bus="Master"});
            _advisor=catalogue;_advisorRules=rules;_advisorSpeech=speech;_advisorLips=lips;_advisorBoxFont=font;
            _advisorSpeechTried=s.SpeechTried;_advisorUnavailable=s.Unavailable;_advisorBoxFontTried=s.BoxFontTried;
            _advisorSettingsBound=s.SettingsBound;_advisorL2=s.L2;_advisorTriangle=s.Triangle;
            _advisorLanguage=s.BrowserLanguage;_advisorError=s.BrowserError;
            _advisorBindings.Clear();foreach(var p in bindings)_advisorBindings.Add(p.Key,p.Value);
            _advisorStreams.Clear();foreach(var p in streams)_advisorStreams.Add(p.Key,p.Value);
            AS(!(s.SimEmitter||s.EventEmitter||s.RemovalEmitter)||shell!=null&&_sim!=null,"missing sim emitter owners");
            AS(!s.StaffEmitter||shell!=null&&_staff!=null,"missing staff emitter owners");
            if(s.SimEmitter)_sim.Advisor=shell.StateSimAdvisor;
            if(s.EventEmitter)_sim.AdvisorEvent=shell.StateEvent;
            if(s.RemovalEmitter)_sim.ObjectRemoved=shell.StateObjectRemoved;
            if(s.StaffEmitter)_staff.Advisor=shell.StateStaffAdvisor;
            _audioMix.RestoreState(s.Mix);_parkAdvisor=shell;_advisorHead=drawn;_advisorVoice=voice.Player;
            _advisorStackView=s.StackView==null?null:AdvisorStackView.FromState(s.StackView,b.Stack?.Invoke(this)
                ??throw new InvalidDataException("stack-view provider bindings required"));
            return new StagedAdvisor(this,voice,drawn,s.SettingsBound);
        } catch { voice?.Dispose(); drawn?.Overlay.Free(); _advisorStackView?.Free();_advisorStackView=null;_advisorVoice=null;_advisorHead=null;_parkAdvisor=null;throw; }
    }

    /// <summary>Parent's checked logical-world join entry point. Actor staging must already have
    /// completed. Keep the returned lifetime beside StagedActorWorld and dispose it FIRST on failure.</summary>
    public StagedAdvisor JoinAdvisorState(ViewerAdvisorState s, AdvisorStateServices b, WorldCoreRegistry world)
    {
        AS(world!=null && world.Hydrated && ReferenceEquals(world.Owner,this)
            && ReferenceEquals(world.Calendar,_calendar) && ReferenceEquals(world.Sim,_sim)
            && ReferenceEquals(world.Staff,_staff) && ReferenceEquals(world.Visitors,_visitors),
            "advisor join requires this hydrated NEW world registry");
        return RestoreAdvisorState(s,b);
    }

    /// <summary>Owns detached presentation until parent publishes. Does not own the Viewer.
    /// Publication never writes global AudioServer buses. Parent handles settings subscription,
    /// overlay parenting and suppression of Viewer._Ready before calling PublishVoice.</summary>
    public sealed class StagedAdvisor : IDisposable
    {
        readonly Viewer owner; readonly AdvisorVoiceState.Staged voice; readonly AdvisorHead head;
        bool published,disposed;
        public bool NeedsSettingsSubscription {get;}
        public AudioStreamPlayer Voice => voice.Player;
        public AdvisorHead Head => head;
        internal StagedAdvisor(Viewer owner,AdvisorVoiceState.Staged voice,AdvisorHead head,bool settings)
        {this.owner=owner;this.voice=voice;this.head=head;NeedsSettingsSubscription=settings;}
        public void PublishVoice()
        { if(disposed||published)throw new InvalidOperationException("Advisor already disposed/published");voice.ApplyAfterCommit();published=true; }
        public void Dispose()
        {
            if(disposed)return;
            if(published)throw new InvalidOperationException("Published advisor belongs to live world");
            disposed=true;voice.Dispose();head?.Overlay.Free();owner._advisorStackView?.Free();owner._advisorStackView=null;
            owner._advisorVoice=null;owner._advisorHead=null;owner._parkAdvisor=null;
        }
    }

    /// <summary>Trusted binder helper for the stock Viewer callbacks. Call on source with capture=true,
    /// on destination shell with capture=false. Additional goals/Rand hooks remain root-owned.
    /// No callback is invoked here. Bind these stable IDs before ParkAdvisor capture/hydration.</summary>
    public void BindStockAdvisorCallbacks(AdvisorStateBindings b, ParkAdvisor advisor, bool capture)
    {
        void Add(string id, object value) {if(value!=null)b.Add("viewer-advisor/"+id,value);}
        Add("speak",capture?advisor.SpeechLength:(Func<int,int,ushort,int>)AdvisorSpeak);
        Add("lips",capture?advisor.Lips:(Func<int,int,LipTrack>)AdvisorLip);
        Add("played",capture?advisor.Played:(Action<AdvisorPlayback>)AdvisorPlayed);
        Add("gold",capture?advisor.GoldTicketsEarned:(Func<int>)(()=>_awards.GoldTicketsEarned));
        Add("ui",capture?advisor.UiSound:(Action<int>)ManagementUiSound);
        Add("stop",capture?advisor.SpeechStopped:(Action)(()=>{if(_advisorVoice?.Playing==true){_advisorVoice.Stop();GD.Print("[advisor] 0x111D78: the voice is stopped");}}));
        Add("over",capture?advisor.GameOver:(Action)(()=>GD.Print("[advisor] 0x13BDD0 game over (123 BANKRUPTED ended) -- not ported, the park goes on")));
        Add("submitted",capture?advisor.Submitted:(Action<AdvisorRequest,bool>)((r,immediate)=>
            GD.Print($"[advisor] submitted 0x{r.Id:X} {AdvisorKey(r.Id)} {(immediate?"immediately":"to the queue")}{(r.Object is ParkRide ride?$" about {DisplayName(ride)}":"")}")));
        Add("stack-ui",capture?advisor.Stack.UiSound:(Action<int>)ManagementUiSound);
        Add("focus",capture?advisor.Stack.FocusObject:(Action<object>)AdvisorFocus);
        Add("tutorial",capture?advisor.Stack.TutorialEvent:(Action<int>)(n=>advisor.TutorialEvent(n)));
        Add("replay",capture?advisor.Stack.Replay:(Action<short>)(row=>{
            GD.Print($"[advisor] Replay of tutorial row {row}: not ported");advisor.TutorialMessage(row);}));
    }
}
