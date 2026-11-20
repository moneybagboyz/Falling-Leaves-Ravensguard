using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Holdings;

/// <summary>
/// Holdings in the abstract tick (holdings 4): every day at hour 18 the hired hands of the hero's clearings work and are paid, and the coppice that is due stands full again (H7, the outbox phase);
/// once a game year (day 1, hour 7) the mares are tried for foals and the foals due are born (S1). A no-op without a <see cref="HoldingsWorld"/>, and free while nothing is under way.
/// </summary>
[AbstractSystem]
public sealed class HoldingsSystem : AbstractSystem
{
    public const int DailyHour = 18;
    public HoldingsSystem() : base("holdings", TickPhase.H7Outbox, SystemOrder.Outbox + 110) { }

    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != DailyHour) return;
        var hw = ctx.World.Get<HoldingsWorld>(); if (hw == null) return;
        var ob = ctx.Outbox;
        EntityId Chron(GameTime when, string type, IReadOnlyList<EntityId> subjects, IReadOnlyList<Cause> causes, params (string Key, string Value)[] data) => ob.Chronicle(when, type, subjects, causes, data);
        int day = (int)ctx.Day;
        hw.Daily(day, Chron);
        hw.CoppiceDay(day);
        hw.BreedingDay(day);
        if (ctx.IsSeasonStartDay) { hw.Furnisher?.Season(ctx.Hour * 3600, day); hw.TakeApprentices(day); }                       // H6 S6: households furnish the houses they live in
    }
}

/// <summary>Installs holdings on the home region (P6-B6 hook in <see cref="WorldRuntime"/>): the world state's book (the "holdings" section) and the world in the sim's services.</summary>
public static class HoldingsWiring
{
    public static HoldingsWorld Attach(RegionWorld home, WorldState state, ContentDatabase content)
    {
        var book = state.Holdings ??= new HoldingsBook();
        var land = home.Sim.Get<LandWorld>() ?? LandWiring.For(state, content);
        var hw = new HoldingsWorld(book, land, home.Economy, home.Population, home.Sim.Economy, state.Seed) { Rides = () => state.Mounts };
        if (home.Sim.Get<Ravensguard.Sim.Construction.ConstructionWorld>() is { } cw)
            hw.Furnisher = new Ravensguard.Sim.Building.HouseholdFurnisher(cw, home.Population, home.Sim.Economy, book) { HeroHousehold = () => Ravensguard.Sim.Construction.ConstructionDesk.HouseholdOf(state) };
        home.Sim.Set(hw);
        hw.Craft = home.Sim.Get<Ravensguard.Sim.Crafting.CraftService>(); hw.Ids = state.Ids; hw.HeroId = state.Party?.Hero ?? Ravensguard.Sim.Economy.EconomyState.HeroOwner;
        if (home.Economy.Production is { } prodsys)
        {
            var wear = new Ravensguard.Sim.Goods.ToolWear(content);
            var items = home.Sim.Economy.Items;
            prodsys.BatchDone = (b, r, st) => Crafters.WearTool(wear, items, b.Id, b.Store, st.Kind, r.WorkerHours, m => content.TryGet<Ravensguard.Content.MaterialDef>(m, out var md) ? md.Category : null);
            if (home.Sim.Get<Ravensguard.Sim.Construction.ConstructionWorld>() is { } cw2 && home.Sim.Get<Ravensguard.Sim.Production.OwnershipWorld>() is { } ow)
                prodsys.NearStores = b => ow.PremisesStores(b, cw2);
        }
        var prev = home.Economy.Farming.LeasedOut;
        home.Economy.Farming.LeasedOut = p => prev?.Invoke(p) == true || hw.IsHeld(p);          // the lord's re-let leaves a cleared acre of the hero's household alone
        return hw;
    }

    /// <summary>The holdings a desk command works on: the home region's when the runtime installed them, else null (a world without a runtime has no clearings).</summary>
    public static HoldingsWorld? For(WorldState state) => state.HomeRecords?.Sim.Get<HoldingsWorld>();
}
