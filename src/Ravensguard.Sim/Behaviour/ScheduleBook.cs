using Ravensguard.Content;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Sim.Behaviour;

/// <summary>When a block applies (the <c>when</c> of a block): every day, or only on a kind of day.</summary>
public enum DayKind : byte { Always, Normal, Market, Fair, Holy, Heatwave }

/// <summary>A schedule block with its kind and place parsed once.</summary>
public sealed record BlockRt(string From, string To, JobKind Kind, PlaceRef Place, DayKind When, int SharePm);

/// <summary>A day plan ready to run: the content def and its parsed blocks.</summary>
public sealed class PlanRt
{
    public ScheduleDef Def { get; }
    public BlockRt[] Blocks { get; }
    public PlanRt(ScheduleDef def)
    {
        Def = def;
        Blocks = [.. def.Blocks.Select(b =>
        {
            if (!Enum.TryParse<JobKind>(b.Kind, out var k)) throw new FormatException($"schedule '{def.Id}': unknown kind '{b.Kind}'");
            PlaceRef p = b.Place switch { "home" => PlaceRef.Home, "bed" => PlaceRef.Bed, "work" => PlaceRef.Work, "market" => PlaceRef.Market, "square" => PlaceRef.Square, "church" => PlaceRef.Church, _ => throw new FormatException($"schedule '{def.Id}': unknown place '{b.Place}'") };
            DayKind w = b.When switch { "" => DayKind.Always, "normal" => DayKind.Normal, "market" => DayKind.Market, "fair" => DayKind.Fair, "holy" => DayKind.Holy, "heatwave" => DayKind.Heatwave, _ => throw new FormatException($"schedule '{def.Id}': unknown day kind '{b.When}'") };
            return new BlockRt(b.From, b.To, k, p, w, b.SharePm);
        })];
    }
}

/// <summary>The kind of day, from the calendar (live-behaviour spec 4.3): which day of the month, whether a feast, and the sun.</summary>
public readonly record struct DayInfo(int DayOfYear, int DayOfMonth, bool Holy, bool Market, bool Fair, int Sunrise, int Sunset)
{
    public static DayInfo Of(long day, int latTenths, CrowdTuningDef crowd)
    {
        int doy = (int)(day % TimeConst.DaysPerYear) + 1, dom = Calendar.DayOfMonth(doy);
        bool fair = false; foreach (var f in CalendarNames.Default.Feasts) if (f.DayOfYear == doy) fair = true;
        var (sr, ss) = SunCache.Of(latTenths, doy);
        return new DayInfo(doy, dom, dom == crowd.HolyDayOfMonth, dom == crowd.MarketDayOfMonth, fair, sr, ss);
    }
}

/// <summary>Sunrise and sunset minutes by latitude and day of the year, memoised (the sun is the same every year).</summary>
public static class SunCache
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, (int, int)> Cache = new();
    public static (int Sunrise, int Sunset) Of(int latTenths, int dayOfYear)
        => Cache.GetOrAdd(latTenths * 100 + dayOfYear, _ => (Daylight.SunriseMinute(latTenths, dayOfYear), Daylight.SunsetMinute(latTenths, dayOfYear)));

    /// <summary>Latitude of a continent row (the default weather settings: 66 degrees at the north edge to 30 at the south).</summary>
    public static int LatitudeOfRow(int row, int north = 660, int south = 300) => north + (south - north) * Math.Clamp(row, 0, 255) / 255;
}

/// <summary>The plans of <see cref="BehaviourContent"/> by occupation, parsed once.</summary>
public sealed class ScheduleBook
{
    private readonly PlanRt?[] _byOcc = new PlanRt?[256];
    public PlanRt Household { get; }
    public PlanRt[] Guards { get; }
    public PlanRt Fallback { get; }
    public BehaviourContent Content { get; }

    public ScheduleBook(BehaviourContent c)
    {
        Content = c;
        foreach (var s in c.Schedules)
        {
            var rt = new PlanRt(s);
            foreach (var o in s.Occupations) if (Enum.TryParse<Occupation>(o, out var occ) && _byOcc[(int)occ] == null) _byOcc[(int)occ] = rt;
        }
        PlanRt Get(string id) => new(c.Plan(id) ?? throw new FormatException($"schedule '{id}' is missing"));
        Household = Get("household"); Guards = [Get("guard_night"), Get("guard_day"), Get("guard_evening")];
        Fallback = _byOcc[(int)Occupation.Smith] ?? Get("indoor_trade");
    }

    public static ScheduleBook Default { get; } = new(BehaviourContent.Default);

    /// <summary>The plan a person follows: the guard's shift (three shifts of eight hours, by person index), the household plan for those with no work, else the plan of the occupation.</summary>
    public PlanRt PlanOf(Occupation occ, bool hasWork, int personIndex)
    {
        if (!hasWork) return Household;
        if (occ == Occupation.Guard) return Guards[personIndex % 3];
        return _byOcc[(int)occ] ?? Fallback;
    }
}
