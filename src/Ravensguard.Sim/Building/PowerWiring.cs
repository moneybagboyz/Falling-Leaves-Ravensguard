using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Building;

/// <summary>
/// P7-LB1 (large-buildings 4.1, 4.7; overview 8.4): the power rules' place in the abstract tick: once a game day, at hour 0 (H5, after the businesses' daily globals), the nets of the home region's
/// built pieces are derived, their watts worked out from the day's water, wind and temperature, and the pieces wear (breakdowns on stream 0x730, floods on 0x731). A no-op without a <see cref="PowerWorld"/>,
/// and free while no wheel, windmill or gin stands.
/// </summary>
[AbstractSystem]
public sealed class PowerSystem : AbstractSystem
{
    public PowerSystem() : base("power", TickPhase.H5DailyGlobals, SystemOrder.DailyGlobalsProduction + 20) { }

    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != 0) return;
        var pw = ctx.World.Get<PowerWorld>(); if (pw == null) return;
        var prev = pw.Chronicle;
        pw.Chronicle = (when, type, key, ps, subjects, causes, data) => ctx.Outbox.Chronicle(when, type, key, ps, subjects, causes, data);
        try { pw.Day(ctx.Day); }
        finally { pw.Chronicle = prev; }
    }
}

/// <summary>Installs the power rules on the home region (a hook in <see cref="WorldRuntime"/>, after construction and weather): the world in the sim's services, the construction world's water check, the weather's hooks.</summary>
public static class PowerWiring
{
    public static PowerWorld? Attach(RegionWorld home, WorldState state, ContentDatabase content, RegionWeather? weather)
    {
        var cw = home.Sim.Get<ConstructionWorld>();
        if (cw == null) return null;
        var cat = PowerCatalog.Of(content);
        var pw = new PowerWorld(cw, cat, state.Seed);
        var region = state.Population?.Region;
        if (region != null) pw.Sites = new WaterSites(region, cat.Rules);
        pw.BuildingAt = (x, y) => cw.BuiltAt(x, y, 0) != null || cw.Standing?.Invoke(x, y, 0) == true;
        if (weather != null)
        {
            int cell = (state.EmbarkCellY + 8) * Continent.Size + state.EmbarkCellX + 8;
            // the day's mean temperature in whole degrees (floor), the river-high count and the noon wind in mm/s
            pw.MeanC = day => weather.DailyMeanTemp(cell, day).Raw >> Fixed.FracBits;
            pw.RiverHighDays = day => weather.RiverHighDays(cell, day);
            pw.WindMmps = day => (int)(((long)weather.AtLite(cell, day * 24 + 12).WindMps.Raw * 1000) >> Fixed.FracBits);
        }
        cw.Power = pw;
        if (pw.Sites != null) cw.PowerCheck = (g, x, y, need) => PlanWater.Check(pw.Sites, cat, g, x, y, need);
        home.Sim.Set(pw);
        return pw;
    }
}
