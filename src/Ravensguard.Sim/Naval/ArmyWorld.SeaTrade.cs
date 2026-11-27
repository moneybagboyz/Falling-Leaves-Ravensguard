using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen.Region;

using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Naval;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Armies;

/// <summary>What a day of the running sea did (the numbers the tests and the soak read).</summary>
public sealed record SeaDay(int Boats, int Sailors, long CaughtG, int Crossings, long Fares, int Barges);

public sealed partial class ArmyWorld
{
    public const int FishCapG = 4_000_000, FerryMinWidthCm = 600, BoatCrew = 2, SailorMinAge = 16, SailorMaxAge = 50;
    private const byte KBoat = 0, KFerry = 1, KBarge = 2, KFish = 3, KSeason = 4;

    /// <summary>
    /// P8-LW-4 (live-war 3.4): the running sea, the way the world has it every day (H5 hour 0). Each coastal village (a sand beach within 2 km) has a fishing boat and a crew of two grown men of its own households, who sign on as sailors and land
    /// 12 kg a man (x0.4 in winter) into the first man's household store, never more than the water holds this season (<see cref="FishCapG"/>); each ford at least <see cref="FerryMinWidthCm"/> wide has a ferry and a ferryman, and the households of
    /// the nearest village that send a traveller across pay the 15-mite fare from their purse to his; each waterway of a river's width has a barge on a shuttle voyage the vehicle system steps hourly. The boats are vehicles the village keeps; the fish
    /// is caught from the water (a documented source), fares and nothing else are coin, and nothing is minted. A no-op until the naval host is attached.
    /// </summary>
    public SeaDay SeaTradeDay(ArmyEnv e)
    {
        var sea = Sea; var rides = sea?.Rides(); var eco = W.Economy; if (sea == null || rides == null || eco == null) return new SeaDay(0, 0, 0, 0, 0, 0);
        var rules = sea.Rules; var book = sea.Ensure(); var region = Region; var all = Beaches.Of(region, rules); var pt = People; int day = (int)e.Day;
        var wt = rides.Templates.Water; ushort fish = eco.Goods.IdOf("fish_fresh");
        int season = e.SeasonNo; if (book.Trade.GetValueOrDefault((KSeason, 0), -1) != season) { foreach (var k in book.Trade.Keys.Where(k => k.Kind == KFish).ToList()) book.Trade.Remove(k); book.Trade[(KSeason, 0)] = season; }
        // who can crew: grown men by home village, sailors first, then fishers, then the village's fishers (nobody is taken from a field or a shop)
        var crewPool = new SortedDictionary<int, List<int>>();
        for (int s = 0; s < region.Settlements.Count; s++) if (Beaches.Near(all, region.Settlements[s].CenterX, region.Settlements[s].CenterY, 2000).Count > 0) crewPool[s] = [];
        var fords = new List<(int Key, RoadCrossing C, int Near)>();
        foreach (var edge in region.RoadEdges)
            for (int k = 0; k < edge.Crossings.Count; k++)
            {
                var c = edge.Crossings[k]; if (c.Kind != CrossingKind.Ford || c.WidthCm < FerryMinWidthCm) continue;
                int nr = NearestTo(OriginXMm + c.At.X * 1000L, OriginYMm + c.At.Y * 1000L).Settlement; if (nr < 0) continue;
                fords.Add((edge.Id * 8 + k, c, nr)); if (!crewPool.ContainsKey(nr)) crewPool[nr] = [];
            }
        var coastal = crewPool.Keys.Where(s => Beaches.Near(all, region.Settlements[s].CenterX, region.Settlements[s].CenterY, 2000).Count > 0).ToHashSet();
        for (int i = 0; i < pt.Count; i++)
        {
            if (!pt.IsAlive(i) || pt.Sex[i] != Sex.Male || !crewPool.TryGetValue(pt.HomeSettlement[i], out var pool) || IsHero(pt.IdOf(i).Value) || Soldiers?.Book.Rows.ContainsKey(pt.IdOf(i).Value) == true) continue;
            int age = pt.AgeYears(i, day); if (age < SailorMinAge || age > SailorMaxAge || Book.IsAway(pt.IdOf(i).Value)) continue;
            if (pt.Occupation[i] is Occupation.Sailor or Occupation.Fisher) pool.Add(i);
        }
        int boats = 0, sailors = 0, crossings = 0, barges = 0; long caught = 0, fares = 0;
        foreach (var (s, pool) in crewPool)
        {
            if (!coastal.Contains(s)) continue;
            var crew = pool.OrderBy(i => pt.Occupation[i] == Occupation.Sailor ? 0 : 1).ThenBy(i => i).Take(BoatCrew).ToList();
            if (crew.Count == 0) continue;
            var boat = BoatOf(rides, book, KBoat, s, "fishing_boat", s); if (boat == null) continue;
            foreach (int i in crew) pt.Occupation[i] = Occupation.Sailor;
            sailors += crew.Count; boats++;
            long stock = Math.Max(0, FishCapG - book.Trade.GetValueOrDefault((KFish, s)));
            int hh = pt.Household[crew[0]]; if (hh < 0 || hh >= eco.HouseholdStore.Length) continue;
            long got = Fishing.Day(boat, rides.Vehicles, fish, crew.Count, e.Winter, ref stock, wt);
            long landed = got > 0 ? Store.MoveUpTo(boat.Cargo, eco.HouseholdStore[hh], fish, boat.Cargo.Qty(fish)) : 0;
            book.Trade[(KFish, s)] = FishCapG - stock; caught += got;
        }
        // the ferries: a ford wide enough keeps a ferry and a ferryman of the nearest village
        var led = eco.Ledger;
        foreach (var (key, c, near) in fords)
        {
            var pool = crewPool[near]; if (pool.Count == 0) continue;
            var ferry = BoatOf(rides, book, KFerry, key, "ferry", near); if (ferry == null) continue;
            int man = pool.OrderBy(i => pt.Occupation[i] == Occupation.Sailor ? 0 : 1).ThenBy(i => i).First();
            int fh = pt.Household[man]; if (fh < 0 || fh >= eco.HouseholdPurse.Length) continue;
            pt.Occupation[man] = Occupation.Sailor;
            var biz = new FerryBusiness(key, ferry.Id, pt.IdOf(man), eco.HouseholdPurse[fh], (long)c.WidthCm * 10);
            var ht = Households;
            for (int h = 0; h < ht.Count; h++)
            {
                if (ht.MemberCount[h] <= 0 || ht.Settlement[h] != near || h == fh || (h + day) % 4 != 0) continue;
                var x = biz.Take(led, eco.HouseholdPurse[h], 1, 0, 0, wt); if (x.Paid) { crossings++; fares += x.Fee; }
            }
        }
        // the barges: a river wide enough carries a shuttle voyage from its source to its mouth and back (the vehicle system steps it hourly)
        foreach (var w in region.Waterways.Where(w => w.Points.Length >= 2 && w.WidthCm.Length > 0 && w.WidthCm.Max() >= 400).OrderBy(w => w.Id).Take(2))
        {
            var barge = BoatOf(rides, book, KBarge, w.Id, "barge", -1, w.Points[0].X * 1000L, w.Points[0].Y * 1000L); if (barge == null) continue;
            barges++;
            if (!rides.Works.Voyages.ContainsKey(barge.Id)) Voyages.Start(rides, barge, new WaterPath(w), 0, new WaterPath(w).LengthMm, e.Hour * 3600, 0, true);
        }
        book.Touch();
        return new SeaDay(boats, sailors, caught, crossings, fares, barges);
    }

    /// <summary>The vehicle the village keeps for a purpose (made the first time it is asked for, a boat on the beach of the place), or null when the template is not in the content.</summary>
    private VehicleRow? BoatOf(Ravensguard.Sim.Animals.RideBook rides, NavalBook book, byte kind, int key, string design, int settlement, long xMm = long.MinValue, long yMm = long.MinValue)
    {
        if (book.Trade.TryGetValue((kind, key), out long id) && rides.Vehicles.Find(new EntityId((ulong)id)) is { } have) return have;
        VehicleBlueprint bp; try { bp = rides.Templates.Get(design); } catch (KeyNotFoundException) { return null; }
        if (xMm == long.MinValue) { var (cx, cy) = settlement >= 0 ? CenterMm(settlement) : (OriginXMm, OriginYMm); xMm = cx; yMm = cy; }
        else { xMm += OriginXMm; yMm += OriginYMm; }
        var v = rides.Vehicles.Create(bp, settlement >= 0 ? PlaceId(settlement) : EntityId.None, xMm, yMm);
        v.Water = WaterState.Afloat;
        book.Trade[(kind, key)] = (long)v.Id.Value;
        return v;
    }
}

/// <summary>The running sea's day (H5 hour 0, after the sea raids' season plan): fishing, the ferries and the barges. A no-op until the naval host is attached.</summary>
[Ravensguard.Sim.Abstract.AbstractSystem]
public sealed class SeaTradeDaySystem : Ravensguard.Sim.Abstract.AbstractSystem
{
    public SeaTradeDaySystem() : base("sea_trade_day", Ravensguard.Sim.Abstract.TickPhase.H5DailyGlobals, Ravensguard.Sim.Abstract.SystemOrder.Ships + 1) { }
    public override void Step(Ravensguard.Sim.Abstract.TickContext ctx)
    {
        if (ctx.HourOfDay != 0) return;
        var aw = ctx.World.Get<ArmyWorld>(); if (aw?.Sea == null) return;
        aw.SeaTradeDay(new ArmyEnv(ctx.World, ctx.Outbox, ctx.Hour));
    }
}
