using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Disease;

/// <summary>
/// Disease on the abstract tick (disease-and-plague 4.6): at H5 hour 0 the persons' illnesses and the region's plague (onset, course, care, death, infection, spread, the gates), at hour 2 of a season's first
/// day (S5, after the taxes and the businesses) the scarcity terms on wages and land. A no-op in a world that has no <see cref="DiseaseBook"/> installed.
/// </summary>
[AbstractSystem]
public sealed class DiseaseSystem : AbstractSystem
{
    public DiseaseSystem() : base("disease", TickPhase.H5DailyGlobals | TickPhase.H6Seasonal, SystemOrder.DailyGlobalsPopulation + 5) { }

    public override void Step(TickContext ctx)
    {
        var b = ctx.World.Get<DiseaseBook>(); if (b == null) return;
        if (ctx.Phase == TickPhase.H5DailyGlobals) { if (ctx.HourOfDay == 0) DiseaseEngine.Daily(ctx.World, b, (int)ctx.Day, ctx.Time, ctx.Outbox); }
        else if (ctx.Phase == TickPhase.H6Seasonal && ctx.IsSeasonStartDay && ctx.HourOfDay == 2) PlagueEconomy.Seasonal(ctx.World, b, (int)ctx.Day);
    }
}
