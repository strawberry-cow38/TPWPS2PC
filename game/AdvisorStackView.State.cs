using Godot;
using System.Text.Json.Serialization;
using TPW.PS2.Data;
namespace TPWPS2Viewer;
public partial class AdvisorStackView
{
    public AdvisorMessageStack StateStack=>_stack;
    public Func<AdvisorStackRecord,string> StateText=>_text;
    public Func<FontText> StateCountFont=>_countFont;
    public IReadOnlyDictionary<string,object> StateAssets=>new Dictionary<string,object>{
        ["closed"]=_closed,["open"]=_open,["tutorial"]=_tutorial,["panel"]=_panel,["text-font"]=_textFont};
    public sealed class StateBindings
    {
        public required AdvisorMessageStack Stack {get;init;}
        public required Func<AdvisorStackRecord,string> Text {get;init;}
        public required Func<FontText> CountFont {get;init;}
        public required Func<object,string> IdentifyAsset {get;init;}
        public required Func<string,object> ResolveAsset {get;init;}
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Snapshot(int Version,int Scroll,bool Allowed,bool Visible,int CountValue,string CountText,
        string Closed,string Open,string Tutorial,string Panel,string Font,string Report);
    public Snapshot CaptureState(StateBindings b)
    {
        if(!ReferenceEquals(b.Stack,_stack)||!Equals(b.Text,_text)||!Equals(b.CountFont,_countFont))
            throw new InvalidDataException("Stack-view capture requires actual providers");
        string Id(object o)=>o==null?null:b.IdentifyAsset(o)??throw new InvalidDataException("stack art asset ID");
        return new(1,SmoothedScroll,Allowed,Visible,_countValue,_countText,Id(_closed),Id(_open),Id(_tutorial),Id(_panel),Id(_textFont),Report);
    }
    /// <summary>No Configure/reload/provider/draw calls. Last-draw diagnostics (rects/blits/text)
    /// are not future inputs and are recomputed on the first ordinary draw. The smoothing state,
    /// failed/null art and cached count ARE retained. Parent owns HUD layout and scene placement.</summary>
    public static AdvisorStackView FromState(Snapshot s,StateBindings b)
    {
        if(s==null||s.Version!=1||b==null||s.CountValue< -1||s.CountText?.Length>64||s.Report?.Length>8192)
            throw new InvalidDataException("Stack-view state bounds");
        T Asset<T>(string id) where T:class=>id==null?null:b.ResolveAsset(id) as T??throw new InvalidDataException("stack-view asset/type");
        // Validate/resolve before node allocation so missing resources cannot leak a Control.
        var closed=Asset<ImageTexture>(s.Closed);var open=Asset<ImageTexture>(s.Open);var tutorial=Asset<ImageTexture>(s.Tutorial);
        var panel=Asset<UiPanel>(s.Panel);var font=Asset<FontText>(s.Font);
        return new(){_stack=b.Stack,_text=b.Text,_countFont=b.CountFont,_closed=closed,_open=open,_tutorial=tutorial,
            _panel=panel,_textFont=font,SmoothedScroll=s.Scroll,Allowed=s.Allowed,Visible=s.Visible,
            _countValue=s.CountValue,_countText=s.CountText,Report=s.Report};
    }
}
