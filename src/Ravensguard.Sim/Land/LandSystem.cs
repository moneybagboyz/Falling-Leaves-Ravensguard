using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Land;

/// <summary>
/// The land's place in the abstract tick (land spec 4.6): every day at hour 6 the notices, lease ends, claim lapses and charters (<see cref="LandWorld.Daily"/>); on a season's first day at hour 9,
/// after the tax step, the dues, the arrears ladder and the resale (<see cref="LandWorld.Seasonal"/>). A no-op in a world without a <see cref="LandWorld"/> (only the home region has one).
/// Runs in the outbox phase, which every hour runs, a coarse catch-up hour included.
/// </summary>
[AbstractSystem]
public sealed class LandSystem : AbstractSystem
{
    public const int DailyHour = 6, SeasonHour = 9;
    public LandSystem() : base("land", TickPhase.H7Outbox, SystemOrder.Outbox + 100) { }

    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != DailyHour && ctx.HourOfDay != SeasonHour) return;
        var lw = ctx.World.Get<LandWorld>(); if (lw == null) return;
        var ob = ctx.Outbox;
        EntityId Chron(GameTime when, string type, IReadOnlyList<EntityId> subjects, IReadOnlyList<Cause> causes, params (string Key, string Value)[] data) => ob.Chronicle(when, type, subjects, causes, data);
        int day = (int)ctx.Day;
        if (ctx.HourOfDay == DailyHour) lw.Daily(day, Chron);
        else if (ctx.IsSeasonStartDay) lw.Seasonal(day, Chron);
    }
}

/// <summary>Installs the land on the home region (P5-A2 hook in <see cref="WorldRuntime"/>): the world state's book (saved in the "land" section), the land world in the sim's services, and the hooks growth and farming read.</summary>
public static class LandWiring
{
    public static LandWorld Attach(RegionWorld home, WorldState state, ContentDatabase content)
    {
        var book = state.Land ??= new LandBook();
        var ew = home.Economy;
        var lw = new LandWorld(book, home.Population, home.Sim.Economy, content, () => ew.Growth.State, () => ew.Growth.ChunkSink, ew.Markets) { Names = NamesOf(state) };
        home.Sim.Set(lw);
        ew.Growth.LandHolds = lw.HoldsLot;
        ew.Farming.LeasedOut = lw.IsLeased;
        return lw;
    }

    /// <summary>The land world a desk command works on: the home region's when the runtime installed one, else one over the world state's own records (a world without regions: tests, the first-hour bot).</summary>
    public static LandWorld For(WorldState state, ContentDatabase content)
    {
        if (state.HomeRecords?.Sim.Get<LandWorld>() is { } lw) return lw;
        return Loose.GetValue(state, s =>
        {
            var e = s.Economy ?? throw new InvalidOperationException("land needs the world's economy");
            return new LandWorld(s.Land ??= new LandBook(), s.Population, e, content, () => s.Growth ??= new Ravensguard.Sim.Growth.GrowthState(), null, s.Markets) { Names = NamesOf(s) };
        });
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorldState, LandWorld> Loose = new();

    private static Func<EntityId, string> NamesOf(WorldState s) => id => s.Party != null && id == s.Party.Hero ? s.Hero?.Name ?? "" : "";
}
