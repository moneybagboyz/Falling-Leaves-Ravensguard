using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Production;

/// <summary>
/// Ownership's place in the abstract tick (businesses spec 4.7): every hour in the outbox phase, after the hour's production and construction: the holdings' daily slices at their business's hour,
/// stalls minded by staff, prospecting jobs finished, new mines' hazards at a season's start. A no-op without an <see cref="OwnershipWorld"/> (only the home region has one).
/// </summary>
[AbstractSystem]
public sealed class OwnershipSystem : AbstractSystem
{
    public OwnershipSystem() : base("ownership", TickPhase.H7Outbox, SystemOrder.Outbox + 120) { }

    public override void Step(TickContext ctx)
    {
        var ow = ctx.World.Get<OwnershipWorld>(); if (ow == null) return;
        var ob = ctx.Outbox;
        ow.Chron = (when, type, subjects, causes, data) => ob.Chronicle(when, type, subjects, causes, data);
        ow.Hour(ctx.Hour);
    }
}

/// <summary>Installs ownership on the home region (P5-F hook in <see cref="WorldRuntime"/>): the world state's book (the "ownership" section), the world in the sim's services, its hooks.</summary>
public static class OwnershipWiring
{
    public static OwnershipWorld Attach(RegionWorld home, WorldState state, ContentDatabase content)
    {
        var ow = Make(state, content, home.Economy.Production, home.Coord.Id);
        ow.Land = home.Sim.Get<LandWorld>();
        ow.Rooms = () => home.Sim.Get<Ravensguard.Sim.Construction.ConstructionWorld>()?.Rooms;
        home.Sim.Set(ow);
        return ow;
    }

    /// <summary>The ownership world a desk command works on: the home region's when the runtime installed one, else one over the world state's own records (tests, a world without regions).</summary>
    public static OwnershipWorld For(WorldState state, ContentDatabase content)
    {
        if (state.HomeRecords?.Sim.Get<OwnershipWorld>() is { } ow) return ow;
        return Loose.GetValue(state, s =>
        {
            var prod = s.Production ?? throw new InvalidOperationException("ownership needs the world's production");
            var w = Make(s, content, prod, s.HomeRegion.Id);
            w.Land = s.HomeRecords?.Sim.Get<LandWorld>() ?? (s.Land != null ? LandWiring.For(s, content) : null);
            w.Rooms = () => (s.HomeRecords?.Sim.Get<Ravensguard.Sim.Construction.ConstructionWorld>() ?? (s.Construction != null ? Ravensguard.Sim.Construction.ConstructionWiring.For(s, content) : null))?.Rooms;
            return w;
        });
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorldState, OwnershipWorld> Loose = new();

    private static OwnershipWorld Make(WorldState state, ContentDatabase content, ProductionSystem prod, int region)
    {
        var book = state.Ownership ??= new OwnershipBook();
        var ow = new OwnershipWorld(book, prod, content, state.Seed, region, state.Ids)
        {
            HeroRow = () => state.Hero?.PersonIndex ?? -1,
            Fairs = Ravensguard.Sim.Travel.FairBook.From(content),
            HeroPerception = () => state.Hero?.Attribute(HeroAttr.Perception) ?? HeroRecord.BaselineAttribute,
        };
        var hero = ow.Hero;
        ow.HeroSkill = s => (state.Crafting ??= new CraftState()).Book.Level(hero, s);
        ow.TrainHero = (s, from, until) =>
        {
            var book2 = (state.Crafting ??= new CraftState()).Book;
            for (long t = from + 3600; t <= until && t - from <= 12 * 3600L; t += 3600) book2.Use(hero, s, 60, 40, t);
        };
        return ow;
    }
}
