using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Sim.Travel;

/// <summary>The kind of place that may hold a fair: a town with a market charter, any town, a village.</summary>
public enum FairHolder : byte { CharterTown, Town, Village, Other }

/// <summary>
/// Feasts and fairs on the calendar (spec 4.6). A fair starts on the calendar's feast day (<see cref="Calendar.FeastOn"/>) and runs its days; the book answers what is on today, what is coming and what it does to stalls,
/// prices (a tilt of demand for those days only), the odd-job board and the end of the working day. Pure queries; nothing here mints coin or goods.
/// </summary>
public sealed class FairBook
{
    private readonly FairDef[] _defs;
    private readonly Calendar _cal;
    public IReadOnlyList<FairDef> Defs => _defs;

    public FairBook(IEnumerable<FairDef> defs, Calendar? cal = null) { _defs = [.. defs.OrderBy(d => d.Id, StringComparer.Ordinal)]; _cal = cal ?? new Calendar(); }
    public static FairBook From(ContentDatabase db, Calendar? cal = null) => new(db.All<FairDef>(), cal);

    private int StartDay(FairDef d, int culture) => _cal.Names(culture).Feasts.FirstOrDefault(f => f.Name == d.Feast)?.DayOfYear ?? -1;

    /// <summary>The fair running on a day of the year (1..60), with its day number (0 = first), or null.</summary>
    public (FairDef Fair, int DayIndex)? ActiveOn(int doy, int culture = -1)
    {
        foreach (var d in _defs)
        {
            int s = StartDay(d, culture); if (s < 0) continue;
            int k = (doy - s + TimeConst.DaysPerYear) % TimeConst.DaysPerYear;
            if (k < d.Days) return (d, k);
        }
        return null;
    }

    /// <summary>The next fair starting on or after the day (days until, 0 = today).</summary>
    public (FairDef Fair, int DaysUntil)? Next(int doy, int culture = -1)
    {
        (FairDef, int)? best = null;
        foreach (var d in _defs)
        {
            int s = StartDay(d, culture); if (s < 0) continue;
            int k = (s - doy + TimeConst.DaysPerYear) % TimeConst.DaysPerYear;
            if (best == null || k < best.Value.Item2) best = (d, k);
        }
        return best;
    }

    public static bool Holds(FairDef d, FairHolder h) => d.Holders switch { "charter_towns" => h == FairHolder.CharterTown, "villages" => h is FairHolder.Village, _ => true };

    public int StallBonus(int doy, FairHolder h) => ActiveOn(doy) is { } a && Holds(a.Fair, h) ? a.Fair.StallBonus : 0;
    public int JobBoardMult(int doy, FairHolder h) => ActiveOn(doy) is { } a && Holds(a.Fair, h) ? Math.Max(1, a.Fair.JobBoardMult) : 1;
    /// <summary>Demand tilt of a good in permille (1000 = none) for the days the fair runs.</summary>
    public int DemandMultPm(string good, int doy, FairHolder h) => ActiveOn(doy) is { } a && Holds(a.Fair, h) && a.Fair.DemandMultPm.TryGetValue(good, out int m) ? m : 1000;
    /// <summary>The hour the working day ends on a fair day (13:00), or -1 for a normal day.</summary>
    public int WorkEndHour(int doy, FairHolder h) => ActiveOn(doy) is { } a && Holds(a.Fair, h) ? a.Fair.WorkEndHour : -1;
    public int GatheringMultPm(int doy, FairHolder h) => ActiveOn(doy) is { } a && Holds(a.Fair, h) ? a.Fair.GatheringMultPm : 1000;
    public int SocialMult(int doy, FairHolder h) => ActiveOn(doy) is { } a && Holds(a.Fair, h) ? Math.Max(1, a.Fair.SocialMult) : 1;
    /// <summary>The Phase 2 trip decision's bonus for a settlement with a fair within three days (permille).</summary>
    public int TripBonusPm(int doy, FairHolder h)
    {
        if (Next(doy) is not { } n || n.DaysUntil > 3 || !Holds(n.Fair, h)) return ActiveOn(doy) is { } a && Holds(a.Fair, h) ? a.Fair.EventBonusPm : 0;
        return n.Fair.EventBonusPm;
    }

    /// <summary>"Spring Fair at Oakford in 3 days" (spec 4.6 announcement), only within the announce window.</summary>
    public string? Announcement(string place, int doy, FairHolder h)
    {
        if (Next(doy) is not { } n || !Holds(n.Fair, h) || n.DaysUntil > n.Fair.AnnounceDays) return null;
        return n.DaysUntil == 0 ? $"{n.Fair.Name} at {place} today" : $"{n.Fair.Name} at {place} in {n.DaysUntil} day{(n.DaysUntil == 1 ? "" : "s")}";
    }

    /// <summary>The purse a contest pays on a fair day, in crowns (stream 0x345 keyed on the settlement and the day); paid by the treasury, not minted.</summary>
    public int ContestPurse(ulong seed, ulong settlement, long day, int doy, string contest)
    {
        if (ActiveOn(doy) is not { } a || !a.Fair.Contests.Contains(contest) || a.Fair.PurseMax <= 0) return 0;
        return DetRng.Range(seed, day, settlement, RngStreams.FairDraw, (uint)contest.Length, a.Fair.PurseMin, a.Fair.PurseMax + 1);
    }
}

/// <summary>The default calendar's fair days without content (the schedule book reads this): Spring Fair 8-10, Midsummer 23, Harvest Festival 38-39, Midwinter Feast 53-55.</summary>
public static class FairCalendar
{
    public static bool IsFairDay(int doy) => doy is >= 8 and <= 10 or 23 or 38 or 39 or >= 53 and <= 55;

    /// <summary>True when a settlement of this class holds the fair running on the day: the Spring Fair is the charter towns' (8-10), Midsummer the villages' (23), the Harvest Festival and the Midwinter Feast everyone's.</summary>
    public static bool Holds(int doy, WorldGen.Region.SettlementClass c)
        => doy is >= 8 and <= 10 ? c == WorldGen.Region.SettlementClass.Town : doy == 23 ? c == WorldGen.Region.SettlementClass.Village : IsFairDay(doy);

    /// <summary>How many times the usual odd jobs a holder posts on the day (Spring Fair x3, Harvest Festival x2).</summary>
    public static int JobBoardMult(int doy, WorldGen.Region.SettlementClass c) => !Holds(doy, c) ? 1 : doy is >= 8 and <= 10 ? 3 : doy is 38 or 39 ? 2 : 1;

    /// <summary>Extra stalls of a fair-holding town (Spring Fair 12): each is a day's work minding it on the board.</summary>
    public static int StallBonus(int doy, WorldGen.Region.SettlementClass c) => Holds(doy, c) && doy is >= 8 and <= 10 ? 12 : 0;

    /// <summary>Hours after the usual close that the stalls stay open and the session still runs on a fair day (the revelry goes on into the evening).</summary>
    public const int EveningSessionHours = 3;

    /// <summary>Demand tilt in permille for the days of a fair (1000 = none), for the goods a fair crowd buys.</summary>
    public static int DemandMultPm(string good) => good switch { "ale" => 1250, "bread" or "meat_fresh" => 1200, _ => 1000 };
}
