using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.WarSupply;

/// <summary>
/// A host's convoys (army-economy 4.1, step 3): when the baggage runs low and the host's depots lie beyond a day's reach, the quartermaster sends carts to the nearest depot with grain in it. A convoy is an ordinary Phase 2 caravan
/// (the same road, speed, daylight, tolls, encounters and fought ambushes; <see cref="CaravanRoads.BuildBetween"/> over the region's real roads) whose owner is the host: its carts leave the host's count, its drivers are hired from the
/// region's idle men and paid from a float of the pay chest, its load is grain moved by <see cref="Store.MoveUpTo"/> from the depot's store onto its own, and on its return the load goes into the host's baggage, the carts back to the
/// host and what is left of the float back to the chest. A convoy raided loses its load to the hideout's chest as every caravan does (the goods stay in the world, out of the army's hands) and its carts with it.
/// </summary>
public sealed class HostConvoys : IOwnCaravans
{
    private readonly HostSupply _hs;
    public HostConvoys(HostSupply hs) { _hs = hs; }

    private WarSupplyBook Book => _hs.Book;
    private EconomyState Econ => _hs.Economy.World.Economy;

    // ------------------------------------------------------------------ sending

    /// <summary>The convoy of the host, if one is on the road.</summary>
    public ConvoyRec? Of(int host) { foreach (var c in Book.Convoys.Values) if (c.Host == host) return c; return null; }

    /// <summary>
    /// Sends a convoy for the host to the depot at <paramref name="depot"/> (a settlement index) for up to <paramref name="wantG"/> grams of grain. False, with nothing changed, when the host has no cart, no driver can be hired,
    /// the roads do not join the host's place to the depot, or the world has no continent trade bound.
    /// </summary>
    public bool Send(HostAccount a, PurseId chest, Store baggage, int depot, long wantG, int day, GameTime now)
    {
        var trade = _hs.Economy.Trade; if (trade == null || a.Carts <= 0 || a.PlaceSettlement < 0 || a.PlaceSettlement == depot) return false;
        var region = _hs.Economy.World.Population.Region;
        var path = CaravanRoads.BuildBetween(region, a.PlaceSettlement, depot, false); if (path == null) return false;
        var kind = VehicleKind.Cart;
        long perCartG = (long)Vehicles.CapKg(kind) * 1000;
        int want = (int)Math.Min(SupplyConfig.ConvoyMaxCarts, Math.Min(a.Carts, Math.Max(1, (wantG + perCartG - 1) / perCartG)));
        var crew = trade.HireCrew(a.PlaceSettlement, (want + 1) / 2, 0, now);
        int use = Math.Min(want, 2 * crew.Length);
        if (use <= 0) return false;
        var led = Econ.Ledger; var st = trade.Link.State; int homeRegion = trade.Link.Region;
        var id = trade.NewCaravanId();
        var car = new Caravan
        {
            Id = id, Home = homeRegion, Region = homeRegion, Settlement = a.PlaceSettlement, OwnerBusiness = -1, Master = -1, OwnerHousehold = -1, Account = led.Open(PurseKind.Caravan, id),
            Vehicles = Enumerable.Repeat(kind, use).ToArray(), Used = use, Crew = [.. crew.Take((use + 1) / 2)], Guards = 0, FoundedSec = now.Seconds, State = CaravanState.Loading, AnchorSec = now.Seconds,
            Path = path, Neighbour = homeRegion, GateIndex = 0, DangerX = trade.DangerOf(path, st), Dist = 0, PrevDist = 0, LastTollMm = -1, TradedAtGate = false,
        };
        // the float: the drivers' wages for the days the round trip takes, from the pay chest (what it holds if less)
        var (w0, w1) = ContinentTrade.Window(now.Season);
        long moveS = 2 * path.LengthMm / Math.Max(1, car.SlowestMmPerGs), days = (moveS + (w1 - w0) * 3600L - 1) / ((w1 - w0) * 3600L) + 3;
        long wage = days * car.Crew.Length * ContinentTrade.DriverWageMites;
        long fl = Math.Min(wage, led.Balance(chest).Mites);
        if (fl > 0 && !led.Transfer(chest, car.Account, new Coin(fl), TransferReason.Trade)) fl = 0;
        car.FloatMites = fl;
        int jitter = DetRng.Range(_hs.Economy.World.Seed, now.TotalHours, id.Value, ContinentTrade.StreamTrip, 0, 0, 4);
        car.WaitUntilSec = now.Seconds + (2 + jitter) * 3600L; car.DepartSec = car.WaitUntilSec;
        Econ.OpenStore(StoreKind.Chest, id, 0, 0, enforce: false);
        st.Caravans.Add(car); trade.CaravansFounded++; trade.TripsStarted++;
        a.Carts -= use;
        Book.Convoys[id.Value] = new ConvoyRec(id.Value, a.Id, depot, use, baggage.Owner.Value, (byte)led.KindOf(chest), led.OwnerOf(chest).Value, fl, day);
        return true;
    }

    // ------------------------------------------------------------------ IOwnCaravans

    public bool Owns(Caravan c) => Book.Convoys.ContainsKey(c.Id.Value);

    public void PayWages(Caravan c, GameTime now)
    {
        var led = Econ.Ledger; var pt = _hs.Economy.World.People; long short_ = 0;
        foreach (int p in c.Crew)
        {
            if (pt.Status[p] != PersonStatus.Alive) continue;
            int hh = pt.Household[p]; if (hh < 0) continue;
            long wage = ContinentTrade.DriverWageMites, pay = Math.Min(wage, led.Balance(c.Account).Mites);
            if (pay > 0) led.Transfer(c.Account, Econ.HouseholdPurse[hh], new Coin(pay), TransferReason.Wage);
            if (pay < wage) short_ += wage - pay;
        }
        c.ArrearsMites += short_;
        _ = now;
    }

    /// <summary>The carts are at the depot: they take what the depot's store holds of grain, by mass and volume, up to their capacity, and turn for home.</summary>
    public void Arrive(TickContext ctx, ContinentTrade trade, Caravan c, GameTime time)
    {
        if (!Book.Convoys.TryGetValue(c.Id.Value, out var rec)) return;
        var store = Econ.OpenStore(StoreKind.Chest, c.Id, 0, 0, enforce: false);
        var depot = _hs.DepotStore(rec.Depot);
        if (depot != null)
        {
            var cat = Econ.Goods; long massLeft = 0, volLeft = 0;
            foreach (var v in c.Vehicles) { massLeft += Vehicles.CapKg(v) * 1000L; volLeft += Vehicles.CapLitres(v) * 1000L; }
            foreach (var g in _hs.LoadGoods)
            {
                long have = depot.Qty(g); if (have <= 0) continue;
                long mpu = Math.Max(1, cat.MassG(g, 1)), vpu = Math.Max(1, cat.VolumeMl(g, 1));
                long take = Math.Min(have, Math.Min(massLeft / mpu, volLeft / vpu)); if (take <= 0) continue;
                long moved = Store.MoveUpTo(depot, store, g, take);
                massLeft -= moved * mpu; volLeft -= moved * vpu;
            }
        }
        c.Cargo.Clear(); c.TradedAtGate = true; c.State = CaravanState.Inbound; c.Region = c.Home; c.WaitUntilSec = (ctx.Hour + 1) * 3600 + 2 * 3600L;
        _ = trade; _ = time;
    }

    /// <summary>Home: the load into the host's baggage, the crew's arrears and the float's remainder to the chest, the carts back to the host (when it still exists), the caravan struck from the table.</summary>
    public void Home(TickContext ctx, ContinentTrade trade, Caravan c, GameTime time)
    {
        if (!Book.Convoys.TryGetValue(c.Id.Value, out var rec)) { trade.EndTrip(c); return; }
        var led = Econ.Ledger; var pt = _hs.Economy.World.People;
        var store = Econ.OpenStore(StoreKind.Chest, c.Id, 0, 0, enforce: false);
        if (Econ.OtherStores.TryGetValue(new EntityId(rec.BaggageOwner), out var bag)) foreach (var (g, q) in store.Rows()) Store.Move(store, bag, g, q);
        else if (_hs.DepotStore(rec.Depot) is { } depot) foreach (var (g, q) in store.Rows()) Store.Move(store, depot, g, q);          // the host is gone: the grain goes back where it came from
        long arrears = Math.Min(c.ArrearsMites, led.Balance(c.Account).Mites);
        if (arrears > 0 && c.Crew.Length > 0)
        {
            long share = arrears / c.Crew.Length;
            foreach (int p in c.Crew) if (pt.Status[p] == PersonStatus.Alive && pt.Household[p] >= 0 && share > 0) led.Transfer(c.Account, Econ.HouseholdPurse[pt.Household[p]], new Coin(share), TransferReason.Wage);
        }
        c.ArrearsMites = 0;
        long back = led.Balance(c.Account).Mites;
        if (back > 0 && led.TryFind((PurseKind)rec.ChestKind, new EntityId(rec.ChestOwner), out var chest)) led.Transfer(c.Account, chest, new Coin(back), TransferReason.Trade);
        if (Book.Hosts.TryGetValue(rec.Host, out var a)) a.Carts += rec.Carts;
        Book.Convoys.Remove(c.Id.Value);
        trade.EndTrip(c);
        trade.Link.State.Caravans.Remove(c.Id);
        _ = ctx; _ = time;
    }

    /// <summary>Raided and lost on the road: the load is the hideout's (the caravan step moved it), the carts are gone with it.</summary>
    public void Lost(TickContext ctx, ContinentTrade trade, Caravan c, GameTime time)
    {
        Book.Convoys.Remove(c.Id.Value);
        _ = ctx; _ = trade; _ = time;
    }
}
