namespace Ravensguard.Sim.Core;

/// <summary>
/// Compaction of old commoners' entries (chronicle-full 4.5, CH-9): an entry older than ten game years, every one of whose subjects is a person or household the hero does not know, that no kept
/// entry names as a cause, is dropped and counted in one summary entry for its year. The chain of anything kept stays whole: an entry a kept entry names (however old, however common) is kept, and
/// so are its own causes in turn. Deterministic (a pass over the entries in append order and ids allocated in year order), run at a year's start, never at load, so a saved world's entries keep their
/// meaning until they are ten years old.
/// </summary>
public static class ChronicleCompaction
{
    public const string SummaryType = "chronicle_summary";
    public const int AgeYears = 10;
    public const string SummaryKey = "ch_summary_year", SummaryNote = "ch_note_compacted";

    /// <summary>The year's-start pass on a world: his own, his kin's and the notables' entries are known; everything else old and common is summarised. Cheap when nothing is ten years old.</summary>
    public static int Yearly(Ravensguard.Sim.Save.WorldState st, GameTime now, IReadOnlySet<ulong>? heldEvents = null)
    {
        long cutoff = now.Seconds - (long)AgeYears * TimeConst.SecondsPerYear;
        if (st.Chronicle.Count == 0 || st.Chronicle.All[0].When.Seconds >= cutoff) return 0;
        var mine = ChronicleView.MineOf(st); var pt = st.Population.People;
        var pinned = Pinned(st);
        // P8-PF-2 (phase-6 G2): an entry that ANY live record names is kept, whatever its age, not only the books the list above knows
        pinned.UnionWith(heldEvents ?? HeldIds.Of(st, new HashSet<ulong>()).Events);
        return Compact(st.Chronicle, now, id =>
        {
            if (mine.Contains(id.Value)) return true;
            int i = id.Kind == IdKind.Person ? pt.IndexOf(id) : -1;
            return i >= 0 && pt.Notable[i] != EntityId.None;
        }, st.Ids, pinned);
    }

    /// <summary>The entry ids that saved books hold (a bond's entry and flogging, a crime's entry, witnessing, report and bounty, an excommunication's entry and cause, a contract's cause, posting and evidence, a due's, a loan's and a grievance's entry): each is kept, so a book's "follow the cause" never dead-ends.</summary>
    public static HashSet<ulong> Pinned(Ravensguard.Sim.Save.WorldState st)
    {
        var p = new HashSet<ulong>();
        void A(ulong v) { if (v != 0) p.Add(v); }
        if (st.Bonds != null) foreach (var b in st.Bonds.Bonds.Values) { A(b.Entry); A(b.FlogEntry); }
        if (st.Law != null) foreach (var c in st.Law.Crimes.Values) { A(c.Entry); A(c.Witnessed); A(c.Reported); A(c.BountyEntry); }
        if (st.FaithPol != null) foreach (var x in st.FaithPol.Excomms) { A(x.Entry); A(x.Cause); }
        if (st.Contracts != null) foreach (var w in st.Contracts.Contracts.Values) { A(w.CauseEntry); A(w.PostedEntry); foreach (var e in w.Evidence) A(e); }
        if (st.Rule != null)
        {
            foreach (var d in st.Rule.Dues) A(d.Entry);
            foreach (var l in st.Rule.Loans) A(l.Entry);
            foreach (var g in st.Rule.Grievances.Values) A(g.Entry);
        }
        return p;
    }


    /// <summary>
    /// Compacts <paramref name="c"/> as of <paramref name="now"/>; <paramref name="known"/> says whether the hero knows a subject (himself and his kin, the notable); <paramref name="pinned"/> are entry ids that saved books hold, kept with their causes. Returns how many entries were
    /// dropped (0 when nothing was old enough, and the chronicle is then untouched).
    /// </summary>
    public static int Compact(Chronicle c, GameTime now, Func<EntityId, bool> known, IdAllocator ids, IReadOnlySet<ulong>? pinned = null)
    {
        long cutoff = now.Seconds - (long)AgeYears * TimeConst.DaysPerYear * TimeConst.SecondsPerDay;
        var list = c.All;
        int n = list.Count;
        if (n == 0 || list[0].When.Seconds >= cutoff) return 0;
        var keep = new bool[n];
        var required = new HashSet<EntityId>();
        if (pinned != null) foreach (var v in pinned) required.Add(new EntityId(v));
        for (int i = n - 1; i >= 0; i--)
        {
            var e = list[i];
            bool candidate = e.When.Seconds < cutoff && e.TypeId != SummaryType && e.Subjects.Count > 0
                && e.Subjects.All(s => s.Kind is IdKind.Person or IdKind.Household && !known(s));
            if (candidate && !required.Contains(e.Id)) continue;
            keep[i] = true;
            foreach (var cause in e.Causes) if (cause.EventId is { } id) required.Add(id);
        }
        var perYear = new SortedDictionary<int, int>();
        for (int i = 0; i < n; i++) if (!keep[i]) perYear[list[i].When.Year] = perYear.GetValueOrDefault(list[i].When.Year) + 1;
        if (perYear.Count == 0) return 0;
        var made = new HashSet<int>(); var next = new List<ChronicleEntry>(n);
        int dropped = 0;
        // the summaries take their ids in year order, placed where their year's first dropped entry stood
        var summaryId = new Dictionary<int, EntityId>();
        foreach (var y in perYear.Keys) summaryId[y] = ids.Allocate(IdKind.Event);
        for (int i = 0; i < n; i++)
        {
            var e = list[i];
            if (keep[i]) { next.Add(e); continue; }
            dropped++;
            int y = e.When.Year;
            if (!made.Add(y)) continue;
            next.Add(new ChronicleEntry(summaryId[y], e.When, SummaryType, [], [Why.Of(CauseKind.Death, SummaryNote, ChronParam.Count("n", perYear[y]))], new Dictionary<string, string>(),
                SummaryKey, [ChronParam.Count("year", y), ChronParam.Count("n", perYear[y])]));
        }
        c.Rebuild(next);
        return dropped;
    }
}
