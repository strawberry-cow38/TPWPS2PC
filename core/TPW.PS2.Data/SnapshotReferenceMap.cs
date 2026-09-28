namespace TPW.PS2.Data;

// Same stock-Dictionary allocation bookkeeping as SnapshotIntMap, for reference-key maps.
// Serialized Slots contain ORDINALS in the accompanying live-entry array, not entity IDs.
// Caller-supplied placeholder keys rebuild holes and are removed before the map is exposed.
// Use side-effect-free plain keys (ParkRide/object), or UNUSED pooled members; never invoke
// gameplay constructors. Factories are explicit so abstract/nonpublic owner types need no reflection.
// Base-class mutation is unsupported; public collection interfaces are tracked.
public sealed class SnapshotReferenceMap<TKey,T> : Dictionary<TKey,T>, IDictionary<TKey,T>, System.Collections.IDictionary where TKey : class
{
    readonly List<TKey> slots = new();
    public SnapshotReferenceMap(IEqualityComparer<TKey> comparer = null) : base(comparer ?? ReferenceEqualityComparer.Instance)
    {positions = new(comparer ?? ReferenceEqualityComparer.Instance);}
    readonly Dictionary<TKey,int> positions;
    readonly List<int> free = new();
    void Added(TKey key)
    {
        int at;
        if(free.Count==0){at=slots.Count;slots.Add(key);}
        else{at=free[^1];free.RemoveAt(free.Count-1);slots[at]=key;}
        positions.Add(key,at);
    }
    void Removed(TKey key)
    {int at=positions[key];positions.Remove(key);slots[at]=null;free.Add(at);}
    public new T this[TKey key]
    {
        get=>base[key];
        set {bool existed=base.ContainsKey(key);base[key]=value;if(!existed)Added(key);}
    }
    public new void Add(TKey key,T value){base.Add(key,value);Added(key);}
    public new bool TryAdd(TKey key,T value)
    {if(!base.TryAdd(key,value))return false;Added(key);return true;}
    public new bool Remove(TKey key)
    {if(!base.Remove(key))return false;Removed(key);return true;}
    public new bool Remove(TKey key,out T value)
    {if(!base.Remove(key,out value))return false;Removed(key);return true;}
    public new void Clear(){base.Clear();positions.Clear();slots.Clear();free.Clear();}
    T IDictionary<TKey,T>.this[TKey key]{get=>this[key];set=>this[key]=value;}
    void IDictionary<TKey,T>.Add(TKey key,T value)=>Add(key,value);
    bool IDictionary<TKey,T>.Remove(TKey key)=>Remove(key);
    void ICollection<KeyValuePair<TKey,T>>.Clear()=>Clear();
    void ICollection<KeyValuePair<TKey,T>>.Add(KeyValuePair<TKey,T> value)=>Add(value.Key,value.Value);
    bool ICollection<KeyValuePair<TKey,T>>.Remove(KeyValuePair<TKey,T> value)
        =>TryGetValue(value.Key,out var current)&&EqualityComparer<T>.Default.Equals(current,value.Value)&&Remove(value.Key);
    object System.Collections.IDictionary.this[object key] {get=>key is TKey k&&TryGetValue(k,out var value)?value:null;set=>this[(TKey)key]=(T)value;}
    void System.Collections.IDictionary.Add(object key,object value)=>Add((TKey)key,(T)value);
    void System.Collections.IDictionary.Remove(object key){if(key is TKey k)Remove(k);}
    void System.Collections.IDictionary.Clear()=>Clear();
    public new void TrimExcess(){base.TrimExcess();Compact();}
    public new void TrimExcess(int capacity){base.TrimExcess(capacity);Compact();}
    void Compact(){positions.Clear();slots.Clear();free.Clear();foreach(TKey key in base.Keys){positions.Add(key,slots.Count);slots.Add(key);}}

    public IntMapLayout CaptureLayout()
    {
        if(slots.Count>100_000 || !base.Keys.SequenceEqual(slots.Where(k=>k != null), Comparer))
            throw new ArgumentException("Reference map layout exceeded bounds or a mutation bypassed tracking.");
        var ordinal = new Dictionary<TKey,int>(Comparer);
        foreach(var key in base.Keys) ordinal.Add(key, ordinal.Count);
        return new(){Slots=slots.Select(key=>key==null ? (int?)null : ordinal[key]).ToArray(),FreeBottomFirst=free.ToArray()};
    }
    public void RestoreLayout(IntMapLayout layout, Func<TKey> placeholder)
    {
        if(layout?.Slots==null || layout.FreeBottomFirst==null || layout.Slots.Length>100_000
            || layout.FreeBottomFirst.Length>layout.Slots.Length)
            throw new ArgumentException("Invalid reference map layout bounds.");
        var live=new HashSet<int>();var holes=new HashSet<int>();
        for(int i=0;i<layout.Slots.Length;i++)
        {
            if(layout.Slots[i] is int key){if(key<0 || key>=Count || !live.Add(key))throw new ArgumentException("Invalid map ordinal.");}
            else holes.Add(i);
        }
        if(live.Count!=Count || holes.Count!=layout.FreeBottomFirst.Length
            || !holes.SetEquals(layout.FreeBottomFirst))throw new ArgumentException("Map layout/key/free-list mismatch.");
        var keys=base.Keys.ToArray();
        var slotCopy=layout.Slots.Select(at=>at.HasValue ? keys[at.Value] : null).ToArray();
        var freeCopy=(int[])layout.FreeBottomFirst.Clone();
        var entries=new KeyValuePair<TKey,T>[slotCopy.Length];var dummyKeys=new Dictionary<int,TKey>();
        for(int i=0;i<slotCopy.Length;i++)
        {
            if(slotCopy[i] is TKey key)entries[i]=new(key,base[key]);
            else
            {
                var dummy=placeholder?.Invoke() ?? throw new ArgumentException("A side-effect-free placeholder key factory is required for holes.");
                if(base.ContainsKey(dummy)||dummyKeys.Values.Contains(dummy,Comparer))throw new ArgumentException("Placeholder key must be distinct and unused.");
                entries[i]=new(dummy,default);dummyKeys.Add(i,dummy);
            }
        }
        // Reserve before logical mutation. Dummy keys are private construction placeholders,
        // removed in the original free-stack order; no callbacks or gameplay see them.
        base.EnsureCapacity(slotCopy.Length);positions.EnsureCapacity(live.Count);
        slots.EnsureCapacity(slotCopy.Length);free.EnsureCapacity(freeCopy.Length);
        base.Clear();foreach(var e in entries)base.Add(e.Key,e.Value);
        foreach(int at in freeCopy)base.Remove(dummyKeys[at]);
        positions.Clear();slots.Clear();slots.AddRange(slotCopy);free.Clear();free.AddRange(freeCopy);
        for(int i=0;i<slotCopy.Length;i++)if(slotCopy[i] is TKey key)positions.Add(key,i);
    }
}
