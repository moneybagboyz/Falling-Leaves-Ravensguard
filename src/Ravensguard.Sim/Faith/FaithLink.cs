using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Faith;

/// <summary>
/// The faith's politics on the running world's hours (P6-E2, faith-and-church 4.5 to 4.7, overview 10a): at the first hour of a day the heresies spread, the monster rumours raise superstition and the believed
/// miracles are looked for; at hour 2 of a season's first day the heresies begin and are answered, the bishops ban, the bans are kept up, superstition falls and witch hunts are settled, pardoners come and fakes are
/// exposed. Runs on the tick worker after the hour's own passes. No book is written while nothing happens: a world that never meets a heresy keeps its bytes.
/// </summary>
public sealed class FaithLink
{
    public WorldRuntime Runtime { get; }
    private FaithLink(WorldRuntime rt) { Runtime = rt; }

    public static FaithLink Attach(WorldRuntime rt)
    {
        var l = new FaithLink(rt);
        rt.AfterHour += l.OnHour;
        return l;
    }

    private void OnHour(long hour)
    {
        int hod = (int)(hour % 24), day = (int)(hour / 24);
        if (hod != 0 && hod != 2) return;
        var st = Runtime.State;
        if (st.Church == null || st.Population.People.Count == 0 || st.Economy == null || st.Content == null) return;
        if (hod == 0) Daily(st, st.Content, day);
        else if (day > 0 && day % TimeConst.DaysPerSeason == 0) Season(st, st.Content, day);
    }

    /// <summary>The day's pass (see the class). Public so a test can run a world's day.</summary>
    public static void Daily(WorldState st, ContentDatabase db, int day)
    {
        Heresy.Daily(st, db, day);
        Superstition.Daily(st, db, day);
        Miracles.Daily(st, db, day);
    }

    /// <summary>The season's pass (see the class).</summary>
    public static void Season(WorldState st, ContentDatabase db, int day)
    {
        Heresy.Season(st, db, day);
        Excommunication.BishopsBan(st, db, day);
        Excommunication.Season(st, db, day);
        Superstition.Season(st, db, day);
        Relics.PardonersCome(st, db, day);
        Relics.ExposeFakes(st, db, day);
    }
}
