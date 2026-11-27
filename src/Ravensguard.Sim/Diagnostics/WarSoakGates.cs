namespace Ravensguard.Sim.Armies;

/// <summary>One gate of the war soak: its name (the spec's, abstract-war 4.6), whether it held, and the numbers behind it.</summary>
public sealed record WarSoakGate(string Name, bool Pass, string Text);

/// <summary>
/// P7-AW-E: the gates of the 25-year war soak (abstract-war 4.6), each over a <see cref="WarSoakResult"/>, every number an integer. <see cref="Exact"/> is the CI part (AW-15: coin, mass and people exact, the worst hour);
/// <see cref="All"/> is the nightly's (AW-16): a world that fails one gate fails the night with the gate named. Settlements stand for regions (the soak's detailed region is one), as noted in the report.
/// </summary>
public static class WarSoakGates
{
    public const int WorstHourMs = 150;

    /// <summary>AW-15: the CI part's gates.</summary>
    public static List<WarSoakGate> Exact(WarSoakResult r, double hourBudgetMs = WorstHourMs) =>
    [
        new("Coin conserved", r.CoinBadDays == 0, $"{r.CoinBadDays} bad days of {r.Years * 60}; coin in purses {r.CoinStart} -> {r.CoinEnd} mites (drift is the seeded and mined money, the audit is exact)"),
        new("Mass conserved", r.MassBadDays == 0, $"{r.MassBadDays} bad days"),
        new("People conserved", r.PeopleBadDays == 0, $"{r.PeopleBadDays} bad days (alive = start + births - deaths + arrivals - departures every day)"),
        new("Worst hour", r.WorstHourMs <= hourBudgetMs, $"{r.WorstHourMs} ms at {r.WorstHourAt} against {hourBudgetMs} (after the first two game days' warm-up)"),
    ];

    /// <summary>AW-16: every gate of 4.6.</summary>
    public static List<WarSoakGate> All(WarSoakResult r, double hourBudgetMs = WorstHourMs)
    {
        var g = Exact(r, hourBudgetMs);
        int share = r.PairSeasons == 0 ? 0 : r.PairSeasonsAtWar * 100 / r.PairSeasons;
        g.Add(new("Wars happen", share is >= 5 and <= 35 && r.WarsDeclared >= 3, $"{r.PairSeasonsAtWar} of {r.PairSeasons} pair-seasons at war ({share}%, 5-35%); {r.WarsDeclared} declared (3 or more)"));
        g.Add(new("Campaign intensity", r.RealmWarYears > 0 && r.HostsFielded * 10 >= r.RealmWarYears * 12, $"{r.HostsFielded} hosts took the field in {r.RealmWarYears} realm-war-years (1.2 a realm a war year or more); a realm at war each campaign season: {r.MusterNotes[7]} raised, {r.MusterNotes[0]} had one, {r.MusterNotes[1]} winning, {r.MusterNotes[5]} short of 30 days of payroll, {r.MusterNotes[6]} no campaign worth the cost, {r.MusterNotes[2] + r.MusterNotes[3] + r.MusterNotes[4]} no commander, point or quota"));
        int dearer = r.RouteSeasons == 0 ? 0 : r.RouteDearer * 100 / r.RouteSeasons, back = r.GoneSeasons == 0 ? 0 : r.GoneBack * 100 / r.GoneSeasons;
        g.Add(new("Dearer bread on march routes", r.RouteSeasons > 0 && dearer >= 70 && (r.GoneSeasons == 0 || back >= 70), $"{r.RouteDearer} of {r.RouteSeasons} route settlement-seasons +15% or more ({dearer}%, 70%); {r.GoneBack} of {r.GoneSeasons} within 10% two seasons after the host ({back}%, 70%)"));
        long ctl = r.ControlWindows == 0 ? 0 : r.ControlChangeSum / r.ControlWindows;
        int up = r.Purveyed == 0 ? 0 : r.PurveyedUnrest * 100 / r.Purveyed;
        g.Add(new("Unrest where purveyance was used", r.Purveyed > 0 && up >= 80 && ctl < 30, $"{r.PurveyedUnrest} of {r.Purveyed} purveyed settlements +100 unrest within 30 days ({up}%, 80%); control mean change {ctl} over {r.ControlWindows} windows (under +30)"));
        int ref5 = r.TouchedHouseholds == 0 ? 0 : (int)(r.WarMigrants * 1000 / r.TouchedHouseholds);
        g.Add(new("Refugees", r.TouchedHouseholds > 0 && r.WarMigrants * 100 >= r.TouchedHouseholds * 5, $"{r.WarMigrants} war or raid migrants ({r.WarReturned} came home) of {r.TouchedHouseholds} households in touched settlements ({ref5 / 10}.{ref5 % 10}%, 5%)"));
        long bPct = r.BorderStart == 0 ? 0 : (r.BorderEnd - r.BorderStart) * 1000 / r.BorderStart, iPct = r.InteriorStart == 0 ? 0 : (r.InteriorEnd - r.InteriorStart) * 1000 / r.InteriorStart;
        g.Add(new("Depopulated borders", r.BorderSettlements > 0 && bPct - iPct <= -100, $"{r.BorderSettlements} touched settlements {bPct / 10}.{Math.Abs(bPct % 10)}% against {r.InteriorSettlements} interior {iPct / 10}.{Math.Abs(iPct % 10)}% over the war years (10 points or more)"));
        int open = r.MarketDaysTotal == 0 ? 0 : (int)(r.MarketDaysOpen * 100L / r.MarketDaysTotal);
        g.Add(new("No collapse", r.MinRealmPopPct >= 500 && r.WorstEmptyStreak <= 30 && open >= 90, $"smallest realm {r.MinRealmPopPct / 10}% of its start (50%); longest empty staple market {r.WorstEmptyStreak} days (30); markets stocked {open}% of days (90%); population {r.Pop0} -> low {r.PopLow} (year {r.PopLowYear}) -> {r.PopEnd}; loans and idle adults are the Phase 6 and economy gates' own"));
        g.Add(new("Recovery", r.Recoverable == 0 || r.Recovered * 100 >= r.Recoverable * 95 || r.Recovered == r.Recoverable, $"{r.Recovered} of {r.Recoverable} settlements that lost 10% or more to war are at 95% fifteen years after ({r.LostTenPct} lost 10%)"));
        int traced = r.Ends.Count(e => e.Traced);
        g.Add(new("War ends traced", r.Ends.Count > 0 && traced == r.Ends.Count, $"{traced} of {r.Ends.Count} ended wars have a peace entry caused by their declaration; by cause: {string.Join(", ", r.Ends.GroupBy(e => e.Cause).OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + " " + x.Count()))}"));
        return g;
    }

    public static string Format(WarSoakResult r, List<WarSoakGate> gates) =>
        $"world {r.World}: " + string.Join("; ", gates.Select(x => (x.Pass ? "PASS " : "FAIL ") + x.Name + " (" + x.Text + ")"));
}
