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
        return l;
    }

    private void OnHour(long hour)
    {
        int hod = (int)(hour % 24), day = (int)(hour / 24);
        if (hod is not (0 or 4)) return;
        var st = Runtime.State;
        if (st.Population.People.Count == 0 || st.Economy == null) return;
        if (hod == 0)
        {
            if (st.Law is not { } book) return;
            CrimeSystem.Daily(LawWorld.From(st, st.Content), book, day);
        }
        else
        {
            var book = st.Law ??= new CrimeBook();
            NpcCrime.Daily(LawWorld.From(st, st.Content), book, day);
            if (book.IsEmpty) st.Law = null;
        }
    }
}
