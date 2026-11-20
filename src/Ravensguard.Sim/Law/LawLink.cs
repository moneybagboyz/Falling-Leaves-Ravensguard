using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Law;

/// <summary>
/// The law's hook on the running world's hours (crime-and-justice 4.9): at hour 0 of each day witnesses report what they saw and the lord's bounties are posted and expired; at hour 4 desperate
/// households steal and neighbours quarrel. Runs on the tick worker after the hour's own passes, so a hero's act asked in an hour is on the books before the next day's reports.
/// </summary>
public sealed class LawLink
{
    public WorldRuntime Runtime { get; }
    private LawLink(WorldRuntime rt) { Runtime = rt; }

    public static LawLink Attach(WorldRuntime rt)
    {
        var l = new LawLink(rt);
        rt.AfterHour += l.OnHour;
        Ravensguard.Sim.Heists.HeistLink.Attach(rt);                              // P6-D3: locks changed after a theft, manhunts ended
        Ravensguard.Sim.Bondage.BondLink.Attach(rt);                              // P6-D1: bonds, pens, raids, runaways, revolts (after the law's own hour)
        return l;
    }

    /// <summary>The courts' hour (P6-C1): the justice book is made when first needed and dropped again while empty, so a world that never held anyone keeps its bytes.</summary>
    private static void RunJustice(Ravensguard.Sim.Save.WorldState st, LawWorld w, CrimeBook book, long hour)
    {
        var jb = st.Justice ?? new JusticeBook();
        Justice.OnHour(w, book, jb, hour);
        st.Justice = jb.IsEmpty ? null : jb;
    }

    private void OnHour(long hour)
    {
        int hod = (int)(hour % 24), day = (int)(hour / 24);
        var st = Runtime.State;
        if (hod is not (0 or 4) && !Justice.Acts(LawWorld.Light(st), hod)) return;
        if (st.Population.People.Count == 0 || st.Economy == null) return;
        if (hod == 0)
        {
            if (st.Law is not { } book) return;
            var w = LawWorld.From(st, st.Content);
            CrimeSystem.Daily(w, book, day);
            RunJustice(st, w, book, hour);
        }
        else if (hod != 4)
        {
            if (st.Law is not { } book) return;
            RunJustice(st, LawWorld.From(st, st.Content), book, hour);
        }
        else
        {
            var book = st.Law ??= new CrimeBook();
            NpcCrime.Daily(LawWorld.From(st, st.Content), book, day);
            if (book.IsEmpty) st.Law = null;
        }
    }
}
