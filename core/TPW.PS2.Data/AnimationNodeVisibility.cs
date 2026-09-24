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
    public static bool Shown(Model model,int node,ISet<int> hidden)
    {
        uint own=(Flags(model,node)&~0x10u)|(hidden.Contains(node)?0x10u:0u);
        if((own&0x8050)!=0) return false;
        foreach(int ancestor in model.Ancestry(node))
            if(ancestor!=node && (Flags(model,ancestor)&0x20)!=0) return false;
        return true;
    }
}
