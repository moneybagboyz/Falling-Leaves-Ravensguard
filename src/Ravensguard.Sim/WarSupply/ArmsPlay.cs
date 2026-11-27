using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Contracts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Soldiers;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.WarSupply;

/// <summary>
/// P8-WS-2 in play: the running world's <see cref="ArmsSupply"/>. It is built from the state on use (the book it works on is the war book's, so nothing here is saved): the makers are the open smithies (their own store and purse,
/// 480 minutes a worker a day), the depots are the war depots the host supply opened at a realm's first town, the supplier of a refit is the smithy of the settlement, and a lord's depot is his own purse. The service runs on the
/// tick worker: <see cref="ArmsDaySystem"/> once a day at hour 18 after the host supply, <see cref="ArmsSeasonSystem"/> on a season's first day after the war supply's season. A world that never ordered arms (no job, no load, no steward) is not touched.
/// </summary>
public static class ArmsPlay
{
    public const int MinutesPerWorker = 480;

    /// <summary>The service over the state, or null when there is no economy. Makes the war book and the contract book on first use when <paramref name="create"/> is set (a desk verb); the tick never makes them.</summary>
    public static ArmsSupply? Build(WorldState st, ContentDatabase? content, bool create = false)
    {
        if (st.Economy == null || content == null) return null;
        if (st.WarSupply == null) { if (!create) return null; st.WarSupply = new WarSupplyBook(); }
        if (st.Contracts == null) { if (!create) return null; st.Contracts = new ContractBook(); }
        var e = st.Economy; var sw = SoldierWiring.For(st, content);
        var arms = new ArmsWorld(st.WarSupply.Arms, content, e.Items, sw.Armouries, e.Goods, st.Seed);
        var cw = ContractWiring.From(st, content);
        var prod = st.HomeRecords?.Economy?.Production ?? st.Production;
        var region = st.Population.Region;
        int RealmOf(int settlement) => (uint)settlement < (uint)region.Settlements.Count && region.Settlements[settlement].FiefIndex is var f and >= 0 && f < region.Fiefs.Count ? region.Fiefs[f].Realm : -1;
        var ww = st.HomeRecords?.Sim.Get<WorksWorld>();
        var supply = new ArmsSupply(e, arms, sw.Armouries, cw, st.Contracts)
        {
            Makers = () => MakersOf(prod, e, RealmOf),
            Supplier = s => prod?.Businesses.Where(b => b.State == BusinessState.Open && b.Store != null && b.Settlement == s && prod.KindOf(b).OwnerOccupation == People.Occupation.Smith).OrderBy(b => b.Id.Value).Select(b => ((Ravensguard.Sim.Goods.Store, PurseId)?)(b.Store, b.Purse)).FirstOrDefault(),
            RealmDepot = realm => DepotOf(e, region, realm, RealmOf),
        };
        if (ww != null) supply.Capacity = key => ww.RoomAtKey(key) is { } room ? LargeRooms.ArmouryOf(room, ww.Rules) : ArmsSupply.Roomy;
        return supply;
    }

    private static IEnumerable<ArmsMaker> MakersOf(ProductionSystem? prod, EconomyState e, Func<int, int> realmOf)
    {
        if (prod == null) yield break;
        foreach (var b in prod.Businesses)                                                            // table order is the stable-id order
        {
            if (b.State != BusinessState.Open || b.Store == null || prod.KindOf(b).OwnerOccupation != People.Occupation.Smith) continue;
            int workers = b.Staff.Count + (b.OwnerWorks ? 1 : 0);
            yield return new ArmsMaker(b.Id, realmOf(b.Settlement), b.Settlement, b.Purse, b.Store, Math.Max(1, workers) * MinutesPerWorker);
        }
    }

    /// <summary>A realm's arms depot: the war depot at its lowest-numbered town (a store only once the host supply has opened it).</summary>
    private static ArmsRealmDepot? DepotOf(EconomyState e, Ravensguard.Sim.WorldGen.Region.Region region, int realm, Func<int, int> realmOf)
    {
        foreach (int t in region.Towns.Select(t => region.Settlements.IndexOf(t)).Where(i => i >= 0).Order())
        {
            if (realmOf(t) != realm) continue;
            var owner = HostIds.Depot(t);
            return e.OtherStores.TryGetValue(owner, out var store) ? new ArmsRealmDepot(realm, owner, store, t) : null;
        }
        return null;
    }

    /// <summary>Where a lord's depot stands for the season order: his purse (a person's, or a treasury), the settlement of the armoury's room and its realm. Null: no purse to pay from.</summary>
    public static ArmsDepot? DepotFor(WorldState st, ulong lord, long armoury)
    {
        var e = st.Economy; if (e == null) return null;
        var id = new EntityId(lord);
        PurseId purse;
        if (st.Party != null && id == st.Party.Hero) { if (!e.Ledger.TryFind(PurseKind.Person, EconomyState.HeroOwner, out purse)) return null; }
        else if (!e.Ledger.TryFind(PurseKind.Treasury, id, out purse)) return null;
        var region = st.Population.Region; var (x, y, _) = Ravensguard.Sim.Building.RoomWorld.Unpack(armoury);
        int best = -1; long bd = long.MaxValue;
        for (int s = 0; s < region.Settlements.Count; s++) { long d = Ravensguard.Sim.WorldGen.Region.Region.Dist2(region.Settlements[s].CenterX - x, region.Settlements[s].CenterY - y); if (d < bd) { bd = d; best = s; } }
        int fief = best >= 0 ? region.Settlements[best].FiefIndex : -1;
        return new ArmsDepot(id, purse, fief >= 0 && fief < region.Fiefs.Count ? region.Fiefs[fief].Realm : -1, Math.Max(0, best), armoury);
    }

    /// <summary>The arms day (tick worker, hour 18): loads on the road, makers at work, contracts closed. A no-op for a world with no job, load or steward.</summary>
    public static void Day(WorldState st, ContentDatabase? content, int day)
    {
        if (st.WarSupply == null || st.WarSupply.Arms.SupplyEmpty) return;
        Build(st, content)?.Day(day);
    }

    /// <summary>The steward's season order (tick worker, a season's first day): every hired steward places his lord's order within his budget. Returns the orders placed.</summary>
    public static int Season(WorldState st, ContentDatabase? content, int day)
    {
        if (st.WarSupply == null || st.WarSupply.Arms.Stewards.Count == 0) return 0;
        var s = Build(st, content); if (s == null) return 0;
        return s.StewardSeason(day, lord => s.Book.Stewards.TryGetValue(lord, out var w) ? DepotFor(st, lord, w.Armoury) : null).Count;
    }
}

/// <summary>The arms day, H1 hour 18 after the host supply (1,150): <see cref="ArmsPlay.Day"/>. The state comes from the world's bag.</summary>
[AbstractSystem]
public sealed class ArmsDaySystem : AbstractSystem
{
    public ArmsDaySystem() : base("arms_day", TickPhase.H1Caravans, SystemOrder.HostSupply + 5) { }
    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != 18) return;
        if (ctx.World.Get<ArmsPlayLink>() is { } l) ArmsPlay.Day(l.State, l.State.Content, (int)ctx.Day);
    }
}

/// <summary>The steward's season order, H6 hour 5 of a season's first day after the war supply's season step.</summary>
[AbstractSystem]
public sealed class ArmsSeasonSystem : AbstractSystem
{
    public ArmsSeasonSystem() : base("arms_season", TickPhase.H6Seasonal, SystemOrder.S4Taxes + 30) { }
    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != 5 || !ctx.IsSeasonStartDay) return;
        if (ctx.World.Get<ArmsPlayLink>() is { } l) ArmsPlay.Season(l.State, l.State.Content, (int)ctx.Day);
    }
}

/// <summary>The state the arms systems read, set in the world's bag by the runtime (<see cref="Attach"/>).</summary>
public sealed class ArmsPlayLink
{
    public WorldState State { get; }
    private ArmsPlayLink(WorldState st) { State = st; }
    public static ArmsPlayLink Attach(RegionWorld home, WorldState state) => Attach(home.Sim, state);
    public static ArmsPlayLink Attach(SimWorld sim, WorldState state)
    {
        if (sim.Get<ArmsPlayLink>() is { } l) return l;
        var made = new ArmsPlayLink(state); sim.Set(made); return made;
    }
}
