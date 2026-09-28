namespace TPW.PS2.Data;

/// <summary>Ordinary APS activation/cleanup, not a rule inferred from missing tracks.
/// 1AB938/1ABA10 -> 1AA460/1AABB0; own-node draw22810C versus child pruning228140.</summary>
public static class AnimationNodeVisibility
{
    public static bool Ordinary(Animation.Record record) => record is {Skeletal:false,Shared:false};
    static uint Flags(Model model,int node)
    {
        int offset=model.NodeOffset(node);
        if(model.NodeIndex(offset)!=node || offset<0 || offset+4>model.D.Length)
            throw new InvalidDataException("animation visibility node outside model");
        return BitConverter.ToUInt32(model.D,offset);
    }
    public static void Initialize(Model model,ISet<int> hidden)
    {
        foreach(int offset in model.LocalTransforms().Keys)
            if((BitConverter.ToUInt32(model.D,offset)&0x10)!=0) hidden.Add(model.NodeIndex(offset));
    }
    public static IEnumerable<int> IndexNodes(Animation animation,Animation.Record record)
    {
        if(!Ordinary(record)) yield break;
        // Both the count and each index are LHU in the actual activation consumer.
        int count=record.IndexCount;
        if(count==0) yield break;
        if(record.Index<=0 || (long)record.Index+count*2L>animation.D.Length)
            throw new InvalidDataException("animation hide list outside resource");
        for(int i=0;i<count;i++) yield return BitConverter.ToUInt16(animation.D,record.Index+i*2);
    }
    public static void Transition(Model model,Animation animation,Animation.Record previous,
        Animation.Record next,ISet<int> hidden,uint playbackFlags=0)
    {
        if(Ordinary(previous) && (playbackFlags&0xC)==0)
        {
            foreach(int node in IndexNodes(animation,previous))
                if((Flags(model,node)&0x80000000)==0) hidden.Remove(node);
            for(int i=0;i<previous.TrackCount;i++)
            {
                int node=animation.TrackNode(animation.TrackAt(previous,i));
                if((Flags(model,node)&0x80000000)==0) hidden.Remove(node);
            }
        }
        if(Ordinary(next) && (playbackFlags&8)==0)
            foreach(int node in IndexNodes(animation,next))
                if((Flags(model,node)&0x80000000)==0) hidden.Add(node);
    }
    /// <summary>⚠ A record's `+0x0C` (u16 count) / `+0x18` (u16 node list) whatever its kind. The
    /// ordinary consumer 1AABB0 reads exactly these two fields (1AAC74/1AAC80/1AACAC) and
    /// <see cref="IndexNodes"/> keeps to ordinary records, because that is the path traced. SKELETAL
    /// records carry the same fields and they read as intent: Handyman s2/s3/s5 list node 2
    /// (`plackard`) and s7, the strike, lists node 0 (`broom`); FatMechanic s7 lists hammer and
    /// toolboxes, s2/s3 the placard. ⚠ The skeletal consumer is NOT traced (1AABB0 also walks the
    /// record's tracks at the 0x30 stride, which a skeletal table is not), so applying these is an
    /// INFERENCE, opt-in for the staff view (AnimatedModel's skeletalHideLists).</summary>
    public static IEnumerable<int> ListedNodes(Animation animation,Animation.Record record)
    {
        if(record==null || record.Shared) yield break;
        int count=record.IndexCount;
        if(count==0 || record.Index<=0) yield break;
        if((long)record.Index+count*2L>animation.D.Length)
            throw new InvalidDataException("animation hide list outside resource");
        for(int i=0;i<count;i++) yield return BitConverter.ToUInt16(animation.D,record.Index+i*2);
    }
    /// <summary>⚠ <see cref="ListedNodes"/> hidden, as 1AABB0 hides an ordinary list: unprotected
    /// nodes (no `0x80000000`) get the self-hidden state.</summary>
    public static void HideListed(Model model,Animation animation,Animation.Record record,ISet<int> hidden)
    {
        foreach(int node in ListedNodes(animation,record))
            if((Flags(model,node)&0x80000000)==0) hidden.Add(node);
    }
    /// <summary>⚠ <see cref="ListedNodes"/> un-hidden, as 1AA460 cleans an old ordinary list.</summary>
    public static void ShowListed(Model model,Animation animation,Animation.Record record,ISet<int> hidden)
    {
        foreach(int node in ListedNodes(animation,record))
            if((Flags(model,node)&0x80000000)==0) hidden.Remove(node);
    }
    public static bool Shown(Model model,int node,ISet<int> hidden)
        => Shown(model,node,model.Ancestry(node),hidden);

    /// <summary>⭐ The same answer from a chain the caller ALREADY HOLDS.
    ///
    /// ⚠ PERF, not semantics: <see cref="Model.Ancestry"/> builds a fresh <c>List&lt;int&gt;</c>
    /// every call, and the per-frame caller asks it once PER PART PER FRAME -- a whole park's
    /// worth of garbage for a walk up a chain that cannot change while the model is loaded. The
    /// caller caches the chain at construction, so it passes it in and this allocates nothing.
    /// Pass exactly <c>model.Ancestry(node)</c>; anything else changes the answer.</summary>
    public static bool Shown(Model model,int node,IReadOnlyList<int> ancestry,ISet<int> hidden)
    {
        uint own=(Flags(model,node)&~0x10u)|(hidden.Contains(node)?0x10u:0u);
        if((own&0x8050)!=0) return false;
        // ⚠ Indexed, not foreach: List<int>'s enumerator boxes when it is walked through the
        // IReadOnlyList interface, which would put the allocation straight back.
        for(int i=0;ancestry!=null && i<ancestry.Count;i++)
        {
            int ancestor=ancestry[i];
            if(ancestor!=node && (Flags(model,ancestor)&0x20)!=0) return false;
        }
        return true;
    }
}
