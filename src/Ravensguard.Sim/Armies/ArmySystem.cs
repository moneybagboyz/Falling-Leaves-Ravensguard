using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Armies;

/// <summary>
/// The standing warbands' hour (armies-on-the-map 5, overview 8.4 <c>Armies</c> = 1,100, H1 after the caravans): the warbands ride the road graph, dwell, search for bandits, raid and think (one lord an hour by fief id,
/// inside the hours of the day), and the lords' wagons move. A no-op in a world with no <see cref="ArmyWorld"/> (no rule book, so no lords) and costs a walk of the warbands: a warband an hour is a few comparisons and one
/// cached road path.
/// </summary>
[AbstractSystem]
public sealed class ArmySystem : AbstractSystem
{
    public ArmySystem() : base("armies", TickPhase.H1Caravans, SystemOrder.Armies) { }

    public override void Step(TickContext ctx)
    {
        var aw = ctx.World.Get<ArmyWorld>(); if (aw == null || aw.Book.Warbands.Count == 0 && aw.Book.Errands.Count == 0 && aw.Book.Marches.Count == 0) return;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var env = new ArmyEnv(ctx.World, ctx.Outbox, ctx.Hour);
        aw.Hour(env);
        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        aw.HourCost.Add(t1 - t0);
        if (aw.Book.Marches.Count > 0) { aw.MarchHour(env); aw.MarchCost.Add(System.Diagnostics.Stopwatch.GetTimestamp() - t1); }       // P7-AM-C: the hosts on the road
    }
}

/// <summary>The armies' day (overview 8.4 <c>ArmiesDaily</c> = 1,160, H1 at hour 18): the wages of the warbands from their lords' purses, and the bands' day (what they eat and rob).</summary>
[AbstractSystem]
public sealed class ArmyDailySystem : AbstractSystem
{
    public const int Hour18 = 18;
    public ArmyDailySystem() : base("armies_daily", TickPhase.H1Caravans, SystemOrder.ArmiesDaily) { }

    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != Hour18) return;
        var aw = ctx.World.Get<ArmyWorld>(); if (aw == null) return;
        var env = new ArmyEnv(ctx.World, ctx.Outbox, ctx.Hour);
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        aw.PayDay(env); aw.BandsDay(env); aw.RationsDay(env); aw.MusterDay(env);
        aw.DayCost.Add(System.Diagnostics.Stopwatch.GetTimestamp() - t0);
    }
}

/// <summary>The armies' season (overview 8.4 <c>S11War</c> = 6,970, H6 after the politics, one fief an hour of a season's first day): each lord's warband is raised (the Phase 7 seeding of a new game or an older save) or grown, and the bands drift home.</summary>
[AbstractSystem]
public sealed class ArmySeasonSystem : AbstractSystem
{
    public ArmySeasonSystem() : base("armies_season", TickPhase.H6Seasonal, SystemOrder.S11War) { }

    public override void Step(TickContext ctx)
    {
        if (!ctx.IsSeasonStartDay) return;
        var aw = ctx.World.Get<ArmyWorld>(); if (aw == null) return;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        aw.SeasonHour(new ArmyEnv(ctx.World, ctx.Outbox, ctx.Hour));
        aw.SeasonCost.Add(System.Diagnostics.Stopwatch.GetTimestamp() - t0);
    }
}

public sealed partial class ArmyWorld
{
    /// <summary>
    /// One hour of a season's first day: the lords whose fief id is this hour of the day (one fief an hour): a lord without a warband raises his (a lord who found no man to hire is not asked again until the next season),
    /// one with a warband grows it toward his holding's size and what his wage roll bears; at hour 1 the bands drift. The seeding of overview 7: keyed on the world seed and the ids (the hiring order is pure), run on the first
    /// season day a world sees, so an older save keeps its bytes until then.
    /// </summary>
    public void SeasonHour(ArmyEnv e)
    {
        var rb = Rule; if (rb == null) return;
        int hod = e.HourOfDay;
        if (hod == 1) BandsSeason(e);
        if (hod == Rules.SummonsHour && Rules.MusterAuto != 0 && e.Time.Season == Ravensguard.Sim.Core.Season.Summer) AutoMuster(e);        // the first day of summer, the day after the sowing: a realm at war calls its levy
        foreach (var f in rb.Fiefs.Values.ToList())
        {
            if (f.Fief % 24 != hod) continue;
            var wb = Book.WarbandOfFief(f.Fief);
            if (wb == null)
            {
                if (Book.Barren.TryGetValue(f.Fief, out int until) && e.SeasonNo < until) continue;
                if (Raise(e, f) == null) Book.Barren[f.Fief] = e.SeasonNo + 1; else Book.Barren.Remove(f.Fief);
            }
            else if (f.Holder >= 0 && !f.Forfeited && (ulong)f.Holder == wb.Lord) Grow(e, wb, f);
        }
    }
}
