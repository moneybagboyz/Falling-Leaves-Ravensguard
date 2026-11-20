using Ravensguard.Sim.Core;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Family;

/// <summary>
/// The family's hook on the running world's hours (family-and-lifespan 4.8): at hour 4 of each day (the H4 slice) the hero's household lives a day: birthdays, the wedding, conception and births, lessons and,
/// on a season's first day, the kidnappers' roll. Runs on the tick worker after the hour's own passes (so a command asked in the hour is already applied). Every hour it also notes whether the hero's death
/// of old age or in childbed is still waiting for the heir (<see cref="DeathPending"/>, read by the game's succession screen).
/// </summary>
public sealed class FamilyLink
{
    public WorldRuntime Runtime { get; }
    private volatile bool _pending;
    private volatile int _cause;
    private FamilyLink(WorldRuntime rt) { Runtime = rt; }

    /// <summary>Tests: the family's numbers to run the day with instead of the content's (the old-age table forced, as FL-10 does; OV-7). Null in the game.</summary>
    public FamilyContent? ForcedContent { get; set; }

    /// <summary>The hero has died of age or in childbed and the heir has not taken up yet.</summary>
    public bool DeathPending => _pending;
    /// <summary>The cause key of the succession screen ("su_cause_age", "su_cause_childbed").</summary>
    public string CauseKey => _cause == (int)People.DeathCause.Childbirth ? "su_cause_childbed" : "su_cause_age";

    public static FamilyLink Attach(WorldRuntime rt)
    {
        var l = new FamilyLink(rt);
        rt.AfterHour += l.OnHour;
        return l;
    }

    private void OnHour(long hour)
    {
        var st = Runtime.State;
        if (st.Population.People.Count == 0 || st.Economy == null || st.Hero == null) return;
        int hod = (int)(hour % 24), day = (int)(hour / 24);
        if (hod == 4 && st.Population.People.Count > 0)
        {
            var w = FamilyWorld.From(st, st.Content);
            if (ForcedContent != null) w = new FamilyWorld { St = w.St, Content = w.Content, F = ForcedContent, HeroId = w.HeroId, Rates = w.Rates, HeldHomes = w.HeldHomes, BandNear = w.BandNear };
            FamilySystem.Daily(w, day);
        }
        var d = st.Family?.Death;
        if (d != null && Succession.HeroPerson.RowOf(st.Population.People, st.Hero, st.Party?.Hero ?? Economy.EconomyState.HeroOwner) != d.Row)
        {
            FamilySystem.ClearDeath(FamilyWorld.From(st, st.Content));                        // the heir has taken up: the dead hero's row keeps his cause
            if (st.Family is { IsEmpty: true }) st.Family = null;
            d = null;
        }
        _pending = d != null; _cause = d != null ? (int)d.Cause : 0;
    }
}
