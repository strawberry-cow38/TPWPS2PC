using System.Text.Json.Serialization;

namespace TPW.PS2.Data;

/// <summary>Entry slots and free-stack order, NOT values or CLR-private memory. Removing a
/// visitor leaves a slot which a later arrival reuses. Saving only the current enumeration
/// loses that future order, which changes RNG/bubble/queue processing. See .NET 8 Dictionary
/// TryInsert/Remove (MIT runtime source) and IntMapSaveChecks' stock-Dictionary oracle.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IntMapLayout
{
    public required int?[] Slots { get; init; }
    public required int[] FreeBottomFirst { get; init; }
}

/// <summary>Use only through this concrete type internally; expose read-only interfaces.
/// Values/enumerators remain stock Dictionary; bookkeeping only owns allocation history.
/// No reflection/runtime-private serialization. Public collection-interface mutations are
/// tracked too; deliberately casting to the Dictionary base to mutate is unsupported.</summary>
internal sealed class SnapshotIntMap<T> : Dictionary<int,T>, IDictionary<int,T>
{
    readonly List<int?> slots = new();
    readonly Dictionary<int,int> positions = new();
    readonly List<int> free = new();
    void Added(int key)
    {
        int at;
        if(free.Count==0){at=slots.Count;slots.Add(key);}
        else{at=free[^1];free.RemoveAt(free.Count-1);slots[at]=key;}
        positions.Add(key,at);
    }
    void Removed(int key)
    {int at=positions[key];positions.Remove(key);slots[at]=null;free.Add(at);}
    public new T this[int key]
    {
        get=>base[key];
        set {bool existed=base.ContainsKey(key);base[key]=value;if(!existed)Added(key);}
    }
    public new void Add(int key,T value){base.Add(key,value);Added(key);}
    public new bool TryAdd(int key,T value)
    {if(!base.TryAdd(key,value))return false;Added(key);return true;}
    public new bool Remove(int key)
    {if(!base.Remove(key))return false;Removed(key);return true;}
    public new bool Remove(int key,out T value)
    {if(!base.Remove(key,out value))return false;Removed(key);return true;}
    public new void Clear(){base.Clear();positions.Clear();slots.Clear();free.Clear();}
    T IDictionary<int,T>.this[int key]{get=>this[key];set=>this[key]=value;}
    void IDictionary<int,T>.Add(int key,T value)=>Add(key,value);
    bool IDictionary<int,T>.Remove(int key)=>Remove(key);
    void ICollection<KeyValuePair<int,T>>.Clear()=>Clear();
    void ICollection<KeyValuePair<int,T>>.Add(KeyValuePair<int,T> value)=>Add(value.Key,value.Value);
    bool ICollection<KeyValuePair<int,T>>.Remove(KeyValuePair<int,T> value)
        =>TryGetValue(value.Key,out var current)&&EqualityComparer<T>.Default.Equals(current,value.Value)&&Remove(value.Key);
    public new void TrimExcess(){base.TrimExcess();Compact();}
    public new void TrimExcess(int capacity){base.TrimExcess(capacity);Compact();}
    void Compact(){positions.Clear();slots.Clear();free.Clear();foreach(int key in base.Keys){positions.Add(key,slots.Count);slots.Add(key);}}

    internal IntMapLayout CaptureLayout()
    {
        if(slots.Count>100_000 || !base.Keys.SequenceEqual(slots.Where(k=>k.HasValue).Select(k=>k.Value)))
            throw new ArgumentException("Integer map layout exceeded bounds or a mutation bypassed tracking.");
        return new(){Slots=slots.ToArray(),FreeBottomFirst=free.ToArray()};
    }
    internal void RestoreLayout(IntMapLayout layout)
    {
        if(layout?.Slots==null || layout.FreeBottomFirst==null || layout.Slots.Length>100_000
            || layout.FreeBottomFirst.Length>layout.Slots.Length)
            throw new ArgumentException("Invalid integer map layout bounds.");
        var live=new HashSet<int>();var holes=new HashSet<int>();
        for(int i=0;i<layout.Slots.Length;i++)
        {
            if(layout.Slots[i] is int key){if(!live.Add(key))throw new ArgumentException("Duplicate map key.");}
            else holes.Add(i);
        }
        if(!live.SetEquals(base.Keys) || holes.Count!=layout.FreeBottomFirst.Length
            || !holes.SetEquals(layout.FreeBottomFirst))throw new ArgumentException("Map layout/key/free-list mismatch.");
        var slotCopy=(int?[])layout.Slots.Clone();var freeCopy=(int[])layout.FreeBottomFirst.Clone();
        var entries=new KeyValuePair<int,T>[slotCopy.Length];var dummyKeys=new Dictionary<int,int>();
        int dummy=int.MinValue;
        for(int i=0;i<slotCopy.Length;i++)
        {
            if(slotCopy[i] is int key)entries[i]=new(key,base[key]);
            else
            {
                while(live.Contains(dummy))dummy++;
                entries[i]=new(dummy,default);dummyKeys.Add(i,dummy);dummy++;
            }
        }
        // Reserve before logical mutation. Dummy keys are private construction placeholders,
        // removed in the original free-stack order; no callbacks or gameplay see them.
        base.EnsureCapacity(slotCopy.Length);positions.EnsureCapacity(live.Count);
        slots.EnsureCapacity(slotCopy.Length);free.EnsureCapacity(freeCopy.Length);
        base.Clear();foreach(var e in entries)base.Add(e.Key,e.Value);
        foreach(int at in freeCopy)base.Remove(dummyKeys[at]);
        positions.Clear();slots.Clear();slots.AddRange(slotCopy);free.Clear();free.AddRange(freeCopy);
        for(int i=0;i<slotCopy.Length;i++)if(slotCopy[i] is int key)positions.Add(key,i);
    }
}
