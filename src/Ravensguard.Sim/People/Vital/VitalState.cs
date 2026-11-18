using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.People.Vital;

/// <summary>One closed (or running) game year of vital counts, for the audit <c>births - deaths + net migration = change</c> (population spec soak 4).</summary>
public sealed class YearStats
{
    public int Year;
    public long Births, Deaths, Marriages, Immigrants, Emigrants, PopulationAtStart;
}

/// <summary>
/// The mutable vital state that is not a table column: per settlement famine and outbreak trackers, running counters and the yearly ledger. Saved in the
/// "abstract" section. Created by <see cref="For"/> from the population it belongs to.
/// </summary>
public sealed class VitalState
{
    /// <summary>Consecutive days the settlement had at least the famine share of hungry households, and consecutive days under it.</summary>
    public int[] FamineStreak = [], QuietStreak = [];
    /// <summary>Id of the open Famine chronicle entry per settlement (0 = no famine now).</summary>
    public ulong[] FamineEntry = [];
    /// <summary>Last day (exclusive) of the outbreak per settlement, 0 = none; and its death multiplier in permille.</summary>
    public int[] OutbreakEndDay = [], OutbreakMult = [];
    public ulong[] OutbreakEntry = [];

    public long Births, Marriages, Immigrants, Emigrants;
    /// <summary>Deaths by <see cref="DeathCause"/> (index = enum value).</summary>
    public long[] DeathsByCause = new long[Enum.GetValues<DeathCause>().Length];
    /// <summary>Living people (kept by births and deaths so no one has to scan the table).</summary>
    public long Alive;
    /// <summary>The game day the non-notable death entry cap counts, and how many entries were written that day.</summary>
    public long ChronicleDay = -1;
    public int ChronicleCount;
    /// <summary>One row per game year seen; the last row is the running year.</summary>
    public List<YearStats> Years { get; } = [];

    public long Deaths => DeathsByCause.Sum();

    public void EnsureSettlements(int n)
    {
        if (FamineStreak.Length == n) return;
        Array.Resize(ref FamineStreak, n); Array.Resize(ref QuietStreak, n); Array.Resize(ref FamineEntry, n);
        Array.Resize(ref OutbreakEndDay, n); Array.Resize(ref OutbreakMult, n); Array.Resize(ref OutbreakEntry, n);
    }

    public YearStats Year(int year)
    {
        if (Years.Count > 0 && Years[^1].Year == year) return Years[^1];
        var y = new YearStats { Year = year, PopulationAtStart = Alive };
        Years.Add(y);
        return y;
    }

    public static VitalState For(Population pop)
    {
        var v = new VitalState();
        v.EnsureSettlements(pop.Region.Settlements.Count);
        long alive = 0;
        for (int i = 0; i < pop.People.Count; i++) if (pop.People.Status[i] == PersonStatus.Alive) alive++;
        v.Alive = alive;
        return v;
    }
}

/// <summary>
/// Children of every person (father and mother lists), updated in place at each birth. The kin graph of Phase 1 is immutable and costs a rebuild per birth
/// batch; guardianship and inheritance need "the children of X" on any day, so they use this index (decision V3). Derived data, never saved.
/// </summary>
public sealed class ChildIndex
{
    private readonly Dictionary<int, List<int>> _kids = [];

    public ChildIndex(PersonTable p)
    {
        for (int i = 0; i < p.Count; i++) Add(p, i);
    }

    public void Add(PersonTable p, int child)
    {
        void Link(int parent)
        {
            if (parent < 0) return;
            if (!_kids.TryGetValue(parent, out var l)) _kids[parent] = l = [];
            l.Add(child);
        }
        Link(p.Father[child]);
        if (p.Mother[child] != p.Father[child]) Link(p.Mother[child]);
    }

    /// <summary>Children of a person in index order (eldest by birth is not implied; callers sort).</summary>
    public IReadOnlyList<int> Of(int parent) => _kids.TryGetValue(parent, out var l) ? l : [];
}
