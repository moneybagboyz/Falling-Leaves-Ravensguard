using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.People;

/// <summary>One culture's name data built from a <see cref="NamePoolDef"/>: shuffled given-name pools plus family and byname generators.</summary>
public sealed class NamePool
{
    public string Id { get; }
    public string Name { get; }
    public IReadOnlyList<string> Male { get; }
    public IReadOnlyList<string> Female { get; }
    private readonly NamePoolDef _d;

    public const int PoolSize = 320;

    public NamePool(NamePoolDef d, ulong seed)
    {
        _d = d; Id = d.Id; Name = d.Name;
        ulong salt = seed ^ Fnv(d.Id);
        Male = Build(d.Onsets, d.Mids, d.MaleEnds, salt ^ 1);
        Female = Build(d.Onsets, d.Mids, d.FemaleEnds, salt ^ 2);
    }

    private static ulong Fnv(string s) { ulong h = 14695981039346656037UL; foreach (char c in s) { h ^= c; h *= 1099511628211UL; } return h; }

    private static List<string> Build(List<string> onsets, List<string> mids, List<string> ends, ulong salt)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var o in onsets) foreach (var m in mids) foreach (var e in ends) set.Add(o + m + e);
        return [.. set.OrderBy(s => DetRng.Mix(salt ^ Fnv(s))).ThenBy(s => s, StringComparer.Ordinal).Take(PoolSize)];
    }

    /// <summary>A given name, common names more often (the pool is in a fixed shuffled order and low indices win).</summary>
    public string Given(bool female, Rnd r)
    {
        var pool = female ? Female : Male;
        return pool[Math.Min(r.Range(0, pool.Count), r.Range(0, pool.Count))];
    }

    public string FamilyName(Rnd r) => _d.FamilyStems[r.Range(0, _d.FamilyStems.Count)] + _d.FamilyEnds[r.Range(0, _d.FamilyEnds.Count)];

    public string Patronymic(string fatherGiven, bool daughter) => fatherGiven + (daughter ? _d.PatronymicDaughter : _d.PatronymicSon);

    public string Byname(Rnd r) => _d.Bynames[r.Range(0, _d.Bynames.Count)];
}

/// <summary>All name pools of a content set, picked by culture id modulo the pool count.</summary>
public sealed class NamePools
{
    public IReadOnlyList<NamePool> Pools { get; }

    public NamePools(IEnumerable<NamePoolDef> defs, ulong seed)
    {
        Pools = [.. defs.OrderBy(d => d.Id, StringComparer.Ordinal).Select(d => new NamePool(d, seed))];
        if (Pools.Count == 0) throw new InvalidOperationException("No name_pool definitions in content (content/base/names).");
    }

    public NamePool For(int culture) => Pools[((culture % Pools.Count) + Pools.Count) % Pools.Count];
}

/// <summary>Small wrapper over <see cref="RngStream"/> so generation code can pass one object around.</summary>
public sealed class Rnd
{
    private RngStream _s;
    public Rnd(ulong seed, ulong entity, uint stream) { _s = new RngStream(seed, 0, entity, stream); }
    public int Range(int min, int maxExclusive) => _s.Range(min, maxExclusive);
    /// <summary>True with probability pct/100.</summary>
    public bool Pct(int pct) => _s.Range(0, 100) < pct;
    public bool PerMille(int pm) => _s.Range(0, 1000) < pm;
    /// <summary>Mean of two draws: a triangular-ish value in [min, maxExclusive).</summary>
    public int Bell(int min, int maxExclusive) => (Range(min, maxExclusive) + Range(min, maxExclusive) + 1) / 2;
    public T Pick<T>(IReadOnlyList<T> list) => list[Range(0, list.Count)];
}
