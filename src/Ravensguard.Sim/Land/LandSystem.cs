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
        EntityId Keyed(GameTime when, string type, string key, IReadOnlyList<ChronParam> ps, IReadOnlyList<EntityId> subjects, IReadOnlyList<Cause> causes, params (string Key, string Value)[] data) => ob.Chronicle(when, type, key, ps, subjects, causes, data);
        int day = (int)ctx.Day;
        if (ctx.HourOfDay == DailyHour) lw.Daily(day, Chron);
        else if (ctx.IsSeasonStartDay)
        {
            lw.Seasonal(day, Chron);
            LandMarketNpc.Seasonal(lw, day, Chron, Keyed);                    // P6-D4 (holdings 3.3): the households' plots, then the lord's new street if none is left (3.4)
            Zoning.Seasonal(lw, day, Keyed);
        }
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
        lw.ParcelLet = (parcel, lessee, begin) => MoveParcelHolder(home.Population, lw, parcel, lessee, begin);
        lw.Seed = home.Sim.Seed;
        lw.StartBuild = (s, lot, hh, day) => ew.Growth.StartOn(s, lot, hh, GameTime.FromGameHours(day * 24L));                                  // P6-D4: a household that bought a plot builds on it
        lw.TrustOfHero = holder =>
        {
            int hh = home.Population.Households.IndexOf(holder); if (hh < 0) return 0;
            int head = home.Population.Households.Head[hh]; if (head < 0 || state.Party == null) return 0;
            return state.Party.Known.TryGetValue(home.Population.People.IdOf(head), out var k) ? k.TrustPm : 0;
        };
        return lw;
    }

    /// <summary>
    /// P6-B6 (holdings 3.1): a leased field's harvest is its lessee's: the population's parcel holder becomes the lessee's household (a person's own household, or the household itself), and goes
    /// back to the lord's household when the lease ends. Farming reads the holder table at its next daily sync; the lord's rent is the book's, so the farm tables carry no landlord for it.
    /// </summary>
    public static void MoveParcelHolder(Ravensguard.Sim.People.Population pop, LandWorld lw, int parcel, EntityId lessee, bool begin)
    {
        var holders = pop.ParcelHolder; if ((uint)parcel >= (uint)holders.Length) return;
        var hht = pop.Households; int to;
        if (begin)
        {
            int row = pop.People.IndexOf(lessee);
            to = row >= 0 ? pop.People.Household[row] : hht.IndexOf(lessee);
            if (to < 0 || to >= hht.Count) return;
        }
        else to = LandRegistry.LordHousehold(pop, pop.Region.Parcels[parcel].Settlement);
        int from = holders[parcel]; if (from == to) return;
        holders[parcel] = to;
        if (from >= 0 && from < hht.Count && hht.ParcelCount[from] > 0) hht.ParcelCount[from]--;
        if (to >= 0 && to < hht.Count) hht.ParcelCount[to]++;
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
