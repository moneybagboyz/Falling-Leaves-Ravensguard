using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.People;

/// <summary>
/// P8-PF-2 (09-performance-and-tooling 3.3): the yearly compaction of the person table. A dead person whose row is <see cref="DeadYears"/> game years old is dropped when nothing names it: no kept
/// chronicle entry, no book (kin, ledger, contracts, ambitions, prisoners, soldiers, the epilogue, the hero's heir: every id a live record holds, found by <see cref="HeldIds"/>), no notable of the
/// history, nobody living whose parent or spouse it was, no fief, lot or household of the region. The row is blanked and joins the free list; the next birth takes the lowest free row under a new id,
/// so no id is ever reused and an old id answers -1. Dead rows that are kept lose only their links to dropped rows (set to -1). Iterates in row order and builds sets, never in hash order:
/// deterministic. Run at a year's start after the chronicle's compaction, never at load.
/// </summary>
public static class PersonCompaction
{
    public const int DeadYears = 30;
    /// <summary>The last pass's wall time in milliseconds and the rows dropped over the run (for the soak's yearly table).</summary>
    public static double LastMs { get; private set; }
    public static long DroppedTotal { get; private set; }

    /// <summary>The year's-start pass: the chronicle's compaction, then the dead rows nothing names.</summary>
    public static (int Entries, int Rows) Run(WorldState st, GameTime now)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = RunPass(st, now);
        LastMs = sw.Elapsed.TotalMilliseconds; DroppedTotal += r.Rows;
        return r;
    }

    private static (int Entries, int Rows) RunPass(WorldState st, GameTime now)
    {
        var pt = st.Population.People;
        var cand = Candidates(st, now);
        bool chronicleDue = st.Chronicle.Count > 0 && st.Chronicle.All[0].When.Seconds < now.Seconds - (long)ChronicleCompaction.AgeYears * TimeConst.SecondsPerYear;
        if (cand.Count == 0 && !chronicleDue) return (0, 0);
        var candIds = new HashSet<ulong>(); foreach (int r in cand) candIds.Add(pt.IdOf(r).Value);
        var (events, held) = HeldIds.Of(st, candIds);
        int entries = ChronicleCompaction.Yearly(st, now, events);
        if (cand.Count == 0) return (entries, 0);
        held.UnionWith(HeldIds.NamedInChronicle(st.Chronicle, candIds));
        return (entries, Drop(st, cand, held));
    }

    /// <summary>The dead rows old enough to drop and not notable, the hero's or his kin's, in row order.</summary>
    public static List<int> Candidates(WorldState st, GameTime now)
    {
        var pt = st.Population.People; var list = new List<int>();
        long cutoff = now.TotalDays - (long)DeadYears * TimeConst.DaysPerYear;
        int hero = st.Hero != null ? st.Hero.PersonIndex : -1;
        HashSet<ulong>? mine = null;
        for (int i = 0; i < pt.Count; i++)
        {
            if (pt.Status[i] != PersonStatus.Dead || pt.DeathDay[i] < 0 || pt.DeathDay[i] > cutoff || pt.Notable[i] != EntityId.None || i == hero || pt.IsFree(i) || pt.IsAuthored(i)) continue;
            mine ??= ChronicleView.MineOf(st);
            if (mine.Contains(pt.IdOf(i).Value)) continue;
            list.Add(i);
        }
        return list;
    }

    /// <summary>Drops the rows of <paramref name="cand"/> that nothing names (<paramref name="heldIds"/> are the candidate ids a record holds); returns how many went.</summary>
    public static int Drop(WorldState st, List<int> cand, IReadOnlySet<ulong> heldIds)
    {
        var pop = st.Population; var pt = pop.People; var hht = pop.Households; int n = pt.Count;
        var drop = new bool[n];
        foreach (int r in cand) drop[r] = !heldIds.Contains(pt.IdOf(r).Value);
        void Keep(int r) { if (r >= 0 && r < n) drop[r] = false; }
        // kin of the living: a parent or a spouse of someone alive stays (the kin graph and the succession read it)
        for (int i = 0; i < n; i++) if (pt.Status[i] is PersonStatus.Alive or PersonStatus.Hero) { Keep(pt.Father[i]); Keep(pt.Mother[i]); Keep(pt.Spouse[i]); }
        // P8-LM-2: an authored character's row stays for good (a dead keeper's name is in their landmark's lore: "since the death of ...")
        foreach (int r in pt.Authored.Keys) Keep(r);
        // the region's own bindings
        foreach (int r in pop.FiefLord) Keep(r);
        foreach (var a in pop.LotKeeper) foreach (int r in a) Keep(r);
        for (int h = 0; h < hht.Count; h++) { Keep(hht.Head[h]); foreach (int m in hht.Members(h)) Keep(m); }
        int dropped = 0;
        for (int i = 0; i < n; i++) if (drop[i]) dropped++;
        if (dropped == 0) return 0;
        // links of the rows that stay to the rows that go
        for (int i = 0; i < n; i++)
        {
            if (drop[i]) continue;
            if (pt.Father[i] >= 0 && drop[pt.Father[i]]) pt.Father[i] = -1;
            if (pt.Mother[i] >= 0 && drop[pt.Mother[i]]) pt.Mother[i] = -1;
            if (pt.Spouse[i] >= 0 && drop[pt.Spouse[i]]) pt.Spouse[i] = -1;
            if (pt.Employer[i] >= 0 && drop[pt.Employer[i]]) pt.Employer[i] = -1;
            if (pt.FatherOfUnborn[i] >= 0 && drop[pt.FatherOfUnborn[i]]) pt.FatherOfUnborn[i] = -1;
        }
        for (int i = 0; i < n; i++) if (drop[i]) pt.Drop(i);
        pop.ResetKin();
        return dropped;
    }
}
