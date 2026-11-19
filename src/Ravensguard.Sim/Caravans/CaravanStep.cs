using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Continental;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;

namespace Ravensguard.Sim.Economy.Markets;

public sealed partial class ContinentTrade
{
    /// <summary>Owner id of the hideout chest and hoard purse that hold what robbers took from this region's caravans (nothing is destroyed: the loot stays findable, caravans spec 3.4).</summary>
    public static EntityId LootOwner(int region) => EntityId.MakeIn(IdKind.Other, region, 0x500_0000);

    /// <summary>H1: one hour of every caravan that belongs to this region, ascending id (caravans spec 4).</summary>
    public void Hour(TickContext ctx)
    {
        var st = Link.State; int region = Link.Region; long hour = ctx.Hour; long endSec = (hour + 1) * 3600;
        if (st.Caravans.Count == 0) return;
        var time = ctx.Time;
        foreach (var c in st.Caravans.All.ToList())
        {
            if (c.Home != region || c.State is CaravanState.Idle or CaravanState.Lost) continue;
            if (ctx.HourOfDay == 0) PayWages(c);
            switch (c.State)
            {
                case CaravanState.Loading:
                    if (endSec >= c.WaitUntilSec) { c.State = CaravanState.Outbound; }
                    Rest(c, endSec);
                    break;
                case CaravanState.Outbound:
                case CaravanState.Inbound:
                    Move(ctx, st, c, endSec, time);
                    break;
                case CaravanState.AtGate:
                    if (endSec >= c.WaitUntilSec) { GateTrade(ctx, st, c, time); }
                    Rest(c, endSec);
                    break;
            }
        }
    }

    private static void Rest(Caravan c, long endSec) { c.PrevDist = c.Dist; c.AnchorSec = endSec; }

    private void Move(TickContext ctx, ContinentState st, Caravan c, long endSec, GameTime time)
    {
        long len = c.Path.LengthMm;
        bool out_ = c.State == CaravanState.Outbound;
        long limit = out_ ? len : 2 * len;
        bool moving = endSec > c.WaitUntilSec && InWindow(time.Season, ctx.HourOfDay);
        long from = c.Dist, to = from;
        if (moving)
        {
            int speed = c.SlowestMmPerGs * c.Path.SpeedPermille(from) / 1000;
            speed = speed * StuckPermille(ctx, c) / 1000;
            to = Math.Min(limit, from + (long)speed * 3600);
        }
        c.PrevDist = from; c.Dist = to; c.AnchorSec = endSec;
        if (to > from)
        {
            PayTolls(c, from, to, out_);
            Encounter(ctx, st, c, time);
            if (c.State == CaravanState.Lost) return;
        }
        if (to >= limit)
        {
            if (out_) { c.State = CaravanState.AtGate; c.Region = c.Neighbour; c.WaitUntilSec = endSec + 3 * 3600L; }
            else Settle(ctx, st, c, time);
        }
    }

    /// <summary>Rain and mud (caravans spec 3.1): most hours full speed, a share of them at 70 percent. A stand-in keyed (caravan, hour) until the weather sample is read here.</summary>
    private static int StuckPermille(TickContext ctx, Caravan c)
    {
        int roll = DetRng.Range(ctx.Seed, ctx.Hour, c.Id.Value, StreamTrip, 7, 0, 100);
        return roll < 12 ? 700 : 1000;
    }

    // ------------------------------------------------------------------ tolls

    private void PayTolls(Caravan c, long from, long to, bool outbound)
    {
        var led = _econ.Ledger; long len = c.Path.LengthMm;
        foreach (var t in c.Path.Tolls)
        {
            if (t.Kind != TollKind.Bridge) continue;                                        // the realm gate is paid at the gate (GateTrade)
            long at = outbound ? t.AtMm : 2 * len - t.AtMm;
            if (at <= from || at > to) continue;
            long pay = Ravensguard.Sim.Taxes.Tolls.ChargeAt(_ew.World, c.Account, c.Settlement, Ravensguard.Sim.Taxes.TollKind.Bridge, c.Fielded, 0);   // taxes spec 10.3: to the holder of the road's settlement
            TollsMites += pay;
        }
    }

    private long store0Value(Caravan c) => PurchaseValue(c);
    private static long PurchaseValue(Caravan c) => c.PurchaseMites;

    private EntityId TreasuryOf(Caravan c)
    {
        var r = _ew.World.Population.Region;
        int fief = c.Settlement < r.Settlements.Count ? r.Settlements[c.Settlement].FiefIndex : -1;
        return fief >= 0 && fief < r.Fiefs.Count ? r.Fiefs[fief].Id : Demography.ContinentOwner;
    }

    private void PayWages(Caravan c)
    {
        var led = _econ.Ledger; var pt = _ew.World.People; var hht = _ew.World.Households;
        int drivers = c.Crew.Length - c.Guards;
        for (int i = 0; i < c.Crew.Length; i++)
        {
            int p = c.Crew[i]; if (pt.Status[p] != PersonStatus.Alive) continue;
            int hh = pt.Household[p]; if (hh < 0) continue;
            long wage = i < drivers ? DriverWageMites : GuardWageMites;
            long pay = Math.Min(wage, led.Balance(c.Account).Mites);
            if (pay > 0 && led.Transfer(c.Account, _econ.HouseholdPurse[hh], new Coin(pay), TransferReason.Wage)) WagesMites += pay;
            if (pay < wage) c.ArrearsMites += wage - pay;
        }
        _ = hht;
    }

    // ------------------------------------------------------------------ encounters (stream 0x230)

    private void Encounter(TickContext ctx, ContinentState st, Caravan c, GameTime time)
    {
        int danger = Math.Clamp(c.DangerX, 0, 100);
        // Encounters per caravan-hour: 120 ppm on a safe road, rising 30 ppm per point of danger (3,120 ppm at 100). With bandits a share of
        // encounters that grows with danger (up to 40 percent), a 40-hour trip is ambushed about 0.1 percent of the time at danger 15 and about
        // 5 percent at danger 100. (Was 120 x (50 + danger) / 100 ppm: under one ambush in 3,000 trips even at danger 100, so dangerous roads weren't.)
        long ppm = 120L + 30L * danger;
        ulong key = c.Id.Value;
        if (DetRng.Range(ctx.Seed, ctx.Hour, key, StreamCaravan, 0, 0, 1_000_000) >= ppm) return;
        int kind = DetRng.Range(ctx.Seed, ctx.Hour, key, StreamCaravan, 1, 0, 100);
        int banditPct = Math.Clamp(danger * 40 / 100, 0, 40);
        var led = _econ.Ledger;
        if (kind < 30)
        {
            // a toll-taking patrol: a few mites a cart to the holder of the road
            TollsMites += Ravensguard.Sim.Taxes.Tolls.ChargeAt(_ew.World, c.Account, c.Settlement, Ravensguard.Sim.Taxes.TollKind.Gate, c.Fielded, store0Value(c));
            return;
        }
        if (kind < 60 || kind >= 60 + banditPct) return;                                    // merchants and refugees swap news; wolves are a delay
        Ambushes++;
        int band = 3 + DetRng.Range(ctx.Seed, ctx.Hour, key, StreamCaravan, 2, 0, 6);
        if (ctx.World.Get<Ravensguard.Sim.Combat.Abstract.AbstractBattleService>() is { Ready: true } ab) { AmbushBattle(ctx, st, c, time, band, ab); return; }   // P4-G: fought out (CaravanAmbush.cs)
        int guardStr = 4 * c.Guards + 1 + (c.Crew.Length - c.Guards), banditStr = 3 * band;
        int pWin = guardStr * 1000 / (guardStr + banditStr);
        if (DetRng.Range(ctx.Seed, ctx.Hour, key, StreamCaravan, 3, 0, 1000) < pWin)
        {
            ctx.Outbox.Count("caravan_ambush_won");
            return;
        }
        Robbed(ctx, st, c, time, band);
    }

    /// <summary>The caravan lost the fight: crew die, cargo and coin go to the hideout's loot, the survivors turn back or the caravan is lost (caravans spec 3.4).</summary>
    private void Robbed(TickContext ctx, ContinentState st, Caravan c, GameTime time, int band, List<int>? fought = null)
    {
        var led = _econ.Ledger; var w = _ew.World; ulong key = c.Id.Value;
        int dead = fought != null ? 0 : (c.Crew.Length * 30 + 99) / 100;                 // P4-G: a fought ambush brings its own dead (CaravanAmbush.cs)
        var killed = fought ?? new List<int>();
        for (int i = 0; i < dead && i < c.Crew.Length; i++)
        {
            int p = c.Crew[c.Crew.Length - 1 - i];                                           // guards stand in front, drivers behind: the guards are the first to go
            if (w.People.Status[p] == PersonStatus.Alive) { Demography.Kill(w, p, DeathCause.Violence, time, ctx.Outbox, "killed defending a caravan"); killed.Add(p); }
        }
        int lostPct = DetRng.Range(ctx.Seed, ctx.Hour, key, StreamCaravan, 4, 50, 101);
        var chest = _econ.OpenStore(StoreKind.Chest, LootOwner(c.Home), 0, 0, enforce: false);
        var store = _econ.OpenStore(StoreKind.Chest, c.Id, 0, 0, enforce: false);
        long goods = 0;
        foreach (var (g, q) in store.Rows()) { long take = q * lostPct / 100; if (take > 0 && Store.Move(store, chest, g, take)) goods += _econ.Goods.MassG(g, take); }
        var hoard = led.Open(PurseKind.Hoard, LootOwner(c.Home));
        long coin = led.Balance(c.Account).Mites * lostPct / 100;
        if (coin > 0 && led.Transfer(c.Account, hoard, new Coin(coin), TransferReason.Hoard)) LootMites += coin;
        bool masterDead = false;
        if (c.Master >= 0 && w.People.Status[c.Master] == PersonStatus.Alive && DetRng.Range(ctx.Seed, ctx.Hour, key, StreamCaravan, 5, 0, 100) < 15) { Demography.Kill(w, c.Master, DeathCause.Violence, time, ctx.Outbox, "killed in a robbery on the road"); masterDead = true; }
        var survivors = c.Crew.Where(p => w.People.Status[p] == PersonStatus.Alive).ToArray();
        string where = $"on the road from {w.Population.Region.Settlements[c.Settlement].Name}";
        ctx.Outbox.Chronicle(time, "caravan_raid", [c.Id], [new Cause(CauseKind.Raid, null, $"bandits ({band}) took {lostPct} percent of a caravan {where}; danger {c.DangerX}")],
            ("caravan", c.Id.ToString()), ("lost_percent", lostPct.ToString()), ("dead", killed.Count.ToString()), ("goods_g", goods.ToString()), ("coin_mites", coin.ToString()));
        Ravensguard.Sim.Core.EntityId none = EntityId.None; _ = none;
        if (survivors.Length == 0 || masterDead || lostPct >= 100 && store.RowCount == 0 && led.Balance(c.Account).Mites == 0)
        {
            Lose(ctx, st, c, time);
            return;
        }
        // the survivors turn back with what is left; the loot cost the merchant his trip
        c.Crew = survivors; c.Guards = Math.Min(c.Guards, survivors.Length);
        if (c.State == CaravanState.Outbound) { c.Dist = 2 * c.Path.LengthMm - c.Dist; c.PrevDist = c.Dist; c.State = CaravanState.Inbound; c.Cargo.Clear(); }
        c.Region = c.Home;
    }

    private void Lose(TickContext ctx, ContinentState st, Caravan c, GameTime time)
    {
        var led = _econ.Ledger; var pt = _ew.World.People;
        var loot = LootOwner(c.Home);
        var chest = _econ.OpenStore(StoreKind.Chest, loot, 0, 0, enforce: false);
        var store = _econ.OpenStore(StoreKind.Chest, c.Id, 0, 0, enforce: false);
        foreach (var (g, q) in store.Rows()) Store.Move(store, chest, g, q);
        var hoard = led.Open(PurseKind.Hoard, loot);
        long left = led.Balance(c.Account).Mites;
        if (left > 0) { led.Transfer(c.Account, hoard, new Coin(left), TransferReason.Hoard); LootMites += left; }
        c.Crew = []; c.Guards = 0;
        c.State = CaravanState.Lost; c.LostDay = (int)time.TotalDays; c.Region = c.Home;
        CaravansLost++;
        ctx.Outbox.Chronicle(time, "caravan_lost", [c.Id], [new Cause(CauseKind.Raid, null, "robbers took the cargo and the crew; the merchant's wagons are gone")], ("caravan", c.Id.ToString()), ("settlement", _ew.World.Population.Region.Settlements[c.Settlement].Name));
        var home = st.Summaries[c.Home]; home.News.Add(new NewsItem(NewsKind.CaravanLost, (byte)c.Home, 1, (int)time.TotalDays, 0)); if (home.News.Count > RegionSummary.NewsCap) home.News.RemoveAt(0);
    }

    // ------------------------------------------------------------------ at the gate

    /// <summary>Sells the cargo to the neighbour's summary, pays the gate toll, buys the back-haul (caravans spec 3.5, 3.6). Coin and goods are booked as crossing the border.</summary>
    private void GateTrade(TickContext ctx, ContinentState st, Caravan c, GameTime time)
    {
        var led = _econ.Ledger; var cat = _econ.Goods; var rl = Link.Mine; var et = rl.ForNeighbour(c.Neighbour);
        var store = _econ.OpenStore(StoreKind.Chest, c.Id, 0, 0, enforce: false);
        var nb = st.Summaries[c.Neighbour]; int spread = _econ.Money.SpreadHalfPermille;
        var book = _books().FirstOrDefault(b => b.SettlementIndex == c.Settlement);
        int day = (int)time.TotalDays;
        if (et != null && book != null)
        {
            // sell what the caravan carries, at the bid, up to what the neighbour can pay this season
            foreach (var (g, q) in store.Rows())
            {
                if (_local[g]) continue;
                int k = Array.IndexOf(ContinentRules.TrackedGoods, cat.StringId(g)); int idx = k >= 0 ? nb.PriceIdx[k] : 1000;
                long bid = book.Goods[g].RefMicro * idx / 1000 * (1000 - spread) / 1000;
                if (bid <= 0) continue;
                long budget = Math.Max(0, et.PayBudgetMites - et.CoinNetIn);
                long take = Math.Min(q, (long)((Int128)budget * 1_000_000 / bid));
                if (take <= 0) continue;
                long pay = Price(bid, take, 1000);
                if (pay <= 0) continue;
                store.Destroy(g, take, DestroyReason.Export);
                led.Mint(c.Account, new Coin(pay), SourceReason.Immigrate);
                et.CoinNetIn += pay; ExportedMites += pay; ExportedUnits[g] += take;
                if (k >= 0) et.StockNetOut[k] += cat.MassG(g, take);
            }
            // the realm gate's toll
            foreach (var t in c.Path.Tolls)
                if (t.Kind == TollKind.RealmGate)
                {
                    long toll = (long)TollGateMites * c.Vehicles.Length;
                    toll = Math.Min(toll, led.Balance(c.Account).Mites);
                    if (toll > 0 && led.Sink(c.Account, new Coin(toll), SinkReason.Emigrate)) { et.TollMites += toll; TollsMites += toll; }
                }
            // the back-haul: what the merchant wished for, at the ask, limited by the neighbour's spare stock and the coin in the account
            long capMass = 0, capVol = 0; foreach (var v in c.Vehicles) { capMass += Vehicles.CapKg(v) * 1000L; capVol += Vehicles.CapLitres(v) * 1000L; }
            foreach (var line in c.Cargo)
            {
                ushort g = line.Good; long massPerUnit = Math.Max(1, cat.MassG(g, 1)), volPerUnit = Math.Max(1, cat.VolumeMl(g, 1));
                int k = Array.IndexOf(ContinentRules.TrackedGoods, cat.StringId(g)); int idx = k >= 0 ? nb.PriceIdx[k] : 1000;
                long ask = book.Goods[g].RefMicro * idx / 1000 * (1000 + spread) / 1000;
                if (ask <= 0) continue;
                long units = Math.Min(line.Units, Math.Min(capMass / massPerUnit, capVol / volPerUnit));
                if (k >= 0) units = Math.Min(units, (et.SpareG[k] + et.StockNetOut[k]) / massPerUnit);
                long afford = (long)((Int128)led.Balance(c.Account).Mites * 1_000_000 / ask);
                units = Math.Min(units, afford);
                if (units <= 0) continue;
                long cost = Price(ask, units, 1000);
                if (cost <= 0 || !led.Sink(c.Account, new Coin(cost), SinkReason.Emigrate)) continue;
                store.Create(g, units, CreateReason.Import);
                et.CoinNetIn -= cost; ImportedMites += cost; ImportedUnits[g] += units;
                if (k >= 0) et.StockNetOut[k] -= cat.MassG(g, units);
                capMass -= units * massPerUnit; capVol -= units * volPerUnit;
            }
        }
        c.Cargo.Clear();
        c.TradedAtGate = true;
        c.State = CaravanState.Inbound; c.Region = c.Home;
        c.WaitUntilSec = (ctx.Hour + 1) * 3600 + 2 * 3600L;
        _ = day;
    }

    // ------------------------------------------------------------------ home again

    private void Settle(TickContext ctx, ContinentState st, Caravan c, GameTime time)
    {
        var led = _econ.Ledger; var pt = _ew.World.People; var books = _books();
        var book = books.FirstOrDefault(b => b.SettlementIndex == c.Settlement);
        var biz = c.OwnerBusiness >= 0 && c.OwnerBusiness < _ew.Production.Businesses.Count ? _ew.Production.Businesses[c.OwnerBusiness] : null;
        var store = _econ.OpenStore(StoreKind.Chest, c.Id, 0, 0, enforce: false);
        int day = (int)time.TotalDays;
        if (biz != null && book != null)
        {
            var m = biz.Party;
            foreach (var (g, q) in store.Rows())
            {
                if (!Store.Move(store, m.Store, g, q)) continue;
                long left = q;
                foreach (var line in c.Cargo)                                          // each book of the fief gets its share of the imports, the rest is listed at the home book
                {
                    if (line.Good != g || left <= 0 || line.Dest < 0) continue;
                    var dest = books.FirstOrDefault(x => x.SettlementIndex == line.Dest) ?? book;
                    long t = Math.Min(left, line.Units); if (t <= 0) continue;
                    t = dest.List(m, g, t, 1000, day, merchantLot: true); left -= t;
                }
                if (left > 0) book.List(m, g, left, 1000, day, merchantLot: true);
            }
            long arrears = Math.Min(c.ArrearsMites, led.Balance(c.Account).Mites);
            if (arrears > 0)
            {
                int drivers = c.Crew.Length - c.Guards; long share = arrears / Math.Max(1, c.Crew.Length);
                foreach (int p in c.Crew) { int hh = pt.Household[p]; if (hh >= 0 && pt.Status[p] == PersonStatus.Alive && share > 0 && led.Transfer(c.Account, _econ.HouseholdPurse[hh], new Coin(share), TransferReason.Wage)) { WagesMites += share; c.ArrearsMites -= share; } }
                _ = drivers;
            }
            long back = led.Balance(c.Account).Mites;
            if (back > 0) led.Transfer(c.Account, m.Purse, new Coin(back), TransferReason.Trade);
            if (c.DebtMites > 0)                                                        // the lord's advance comes back from the takings, half of what the purse holds at most
            {
                long repay = Math.Min(c.DebtMites, led.Balance(m.Purse).Mites / 2);
                if (repay > 0 && led.Transfer(m.Purse, led.Open(PurseKind.Treasury, new EntityId(c.DebtTo)), new Coin(repay), TransferReason.Trade)) c.DebtMites -= repay;
            }
            c.ProfitTotalMites += back - c.FloatMites - c.PurchaseMites;
        }
        c.State = CaravanState.Idle; c.Region = c.Home; c.Dist = 0; c.PrevDist = 0; c.Trips++; TripsDone++;
        c.GuardsRelease(pt);
    }
}

internal static class CaravanExt
{
    /// <summary>The guards of a trip go back to their own business when the trip ends; drivers stay on the caravan's books between trips.</summary>
    public static void GuardsRelease(this Caravan c, PersonTable pt)
    {
        if (c.Guards <= 0) return;
        int drivers = c.Crew.Length - c.Guards;
        c.Crew = c.Crew.Take(drivers).ToArray(); c.Guards = 0;
    }
}
