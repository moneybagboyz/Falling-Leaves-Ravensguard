using Ravensguard.Sim.Core;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Tests.EconomyAudit;

/// <summary>
/// The audit's trading bot (economy-audit 3, passes b and c; EA-1, EA-13, EA-14): a purse and a store of its own that, once a game day at hour 12, finds the best loop between two books within <see cref="MaxKm"/> of each other by the
/// books' own mid curves (cheap filter), prices it with the real quotes (ask on one side, bid and the 5% seller's toll on the other), runs it with the real <see cref="MarketBook.Buy"/> and
/// <see cref="MarketBook.SellToMerchants"/>, and repeats it as many times as the day's hours allow. It takes nothing from nowhere: its coin is seed coin the test mints once (the faucet), every later move is a ledger transfer. The
/// numbers it prints are realised coin: purse before and after (unsold stock counts as nothing and is reported).
/// </summary>
public sealed class TradeBot
{
    public const int MaxKm = 10;
    private readonly WarLoadRun _run;
    private readonly MarketParty _party;
    public PurseId Purse => _party.Purse;
    public Store Stock => _party.Store;
    public int LoadKg { get; }
    public double KmPerHour { get; }
    public double LoadHours { get; }
    public long Trips, Loops;
    public long CostMites, TakingsMites, TollMites;
    public string BestLoop = "", LastLoop = "";
    public List<string> Log { get; } = [];
    public long BestTripProfit;
    private readonly List<long> _dayProfit = [];
    public IReadOnlyList<long> DayProfit => _dayProfit;

    public TradeBot(WarLoadRun run, int id, int loadKg, double kmPerHour, double loadHours, long seedMites)
    {
        _run = run; LoadKg = loadKg; KmPerHour = kmPerHour; LoadHours = loadHours;
        var econ = run.Econ; var owner = EntityId.Make(IdKind.Person, 9_100_000UL + (ulong)id);
        var purse = econ.Ledger.Open(PurseKind.Person, owner);
        if (seedMites > 0) econ.Ledger.Mint(purse, Coin.FromMites(seedMites), SourceReason.Seed);       // the test's faucet: the bot's whole capital, once
        _party = new MarketParty(owner, purse, econ.OpenStore(StoreKind.House, owner, 0, 0, false), PartyKind.Household, 1_000_000 + id);
    }

    private static double Km(Ravensguard.Sim.WorldGen.Region.Region r, int a, int b)
    {
        double dx = r.Settlements[a].CenterX - r.Settlements[b].CenterX, dy = r.Settlements[a].CenterY - r.Settlements[b].CenterY;
        return Math.Sqrt(dx * dx + dy * dy) / 1000.0;
    }

    /// <summary>One day's trading at the barrier of hour 12 of <paramref name="day"/>; returns the day's realised profit in mites.</summary>
    public long Day(int day)
    {
        var econ = _run.Econ; var led = econ.Ledger; var markets = _run.Economy.Markets; var region = _run.State.Population.Region;
        var books = markets.AllBooks().Where(b => b.Merchants.Count > 0).ToArray(); int ng = econ.Goods.Count;
        long before = Worth(books);
        var cat = econ.Goods; var ok = new bool[ng];
        for (ushort g = 0; g < ng; g++) ok[g] = !cat.IsPiece(g) && cat.Def(g).Tradable && cat.SpoilPpm(g, StoreKind.Granary) < MarketBook.PerishablePpmPerDay;
        // the cheap filter: mid prices, ask at A against bid at B net of the toll, for books within MaxKm
        var mid = new long[books.Length][];
        for (int i = 0; i < books.Length; i++) { mid[i] = new long[ng]; for (ushort g = 0; g < ng; g++) mid[i][g] = ok[g] && books[i].Goods[g].StockUnits > 0 ? books[i].MidMicro(g) : 0; }
        double spread = (1000 + econ.Money.SpreadHalfPermille) / 1000.0, bidf = (1000 - econ.Money.SpreadHalfPermille) / 1000.0, toll = 1 - econ.Money.MarketTollPermille / 1000.0;
        int tripsToday = 0; double hoursLeft = 12;
        long loadUnits = LoadKg * 1000L;
        while (hoursLeft > 0)
        {
            // rank by the mid-price gap times the load the seller can really supply, then price the best few with the real quotes (ask on the buy side, bid and toll on the sell side)
            var cands = new List<(double Score, int A, int B, ushort G, double Km)>();
            for (int a = 0; a < books.Length; a++)
                for (int b = 0; b < books.Length; b++)
                {
                    if (a == b) continue;
                    double km = Km(region, books[a].SettlementIndex, books[b].SettlementIndex); if (km > MaxKm) continue;
                    if (2 * km / KmPerHour + LoadHours > hoursLeft) continue;
                    for (ushort g = 0; g < ng; g++)
                    {
                        long ma = mid[a][g]; if (ma == 0) continue;
                        long mb = books[b].MidMicro(g); if (!ok[g] || mb == 0) continue;
                        double edge = (mb * bidf * toll - ma * spread) / ma; if (edge <= 0) continue;
                        long units = Math.Min(loadUnits, books[a].Goods[g].StockUnits); if (units < 1000) continue;
                        cands.Add((edge * units * ma / 1e6, a, b, g, km));
                    }
                }
            if (cands.Count == 0) break;
            bool done = false; long bestProfit = 0; (double Score, int A, int B, ushort G, double Km) pick = default;
            foreach (var c in cands.OrderByDescending(x => x.Score).Take(60))
            {
                long units = Math.Min(loadUnits, books[c.A].Goods[c.G].StockUnits);
                long qb = books[c.A].QuoteBuy(c.G, units), qs = books[c.B].QuoteSell(c.G, units) * (1000 - econ.Money.MarketTollPermille) / 1000;
                long cash = 0; foreach (var m in books[c.B].Merchants) cash += led.Balance(m.Purse).Mites;      // the buyers at the far book must be able to pay it
                if (qs <= qb || cash < qs || qb > led.Balance(_party.Purse).Mites) continue;
                if (qs - qb > bestProfit) { bestProfit = qs - qb; pick = c; }
            }
            if (bestProfit > 0)
            {
                double h = 2 * pick.Km / KmPerHour + LoadHours;
                if (Trip(books[pick.A], books[pick.B], pick.G, day)) { tripsToday++; hoursLeft -= h; done = true; }
            }
            if (!done) break;
        }
        long after = Worth(books);
        long profit = after - before; _dayProfit.Add(profit); Trips += tripsToday; return profit;
    }

    private bool Trip(MarketBook a, MarketBook b, ushort g, int day)
    {
        var led = _run.Econ.Ledger; long units = Math.Min(LoadKg * 1000L, a.Goods[g].StockUnits); if (units <= 0) return false;
        long cash0 = led.Balance(_party.Purse).Mites;
        var buy = a.Buy(_party, g, units, 4000, day); if (buy.Qty <= 0) return false;
        var sell = b.SellToMerchants(_party, g, buy.Qty, day);
        long profit = led.Balance(_party.Purse).Mites - cash0;
        CostMites += buy.PaidMites; TakingsMites += sell.PaidMites; TollMites += sell.TollMites; Loops++;
        LastLoop = $"{_run.Econ.Goods.Def(g).Id} {a.Name} -> {b.Name}: bought {buy.Qty / 1000.0:0.0} kg for {buy.PaidMites}, sold {sell.Qty / 1000.0:0.0} kg for {sell.PaidMites}, toll {sell.TollMites}, profit {profit}"; Log.Add(LastLoop);
        if (profit > BestTripProfit) { BestTripProfit = profit; BestLoop = $"{_run.Econ.Goods.Def(g).Id} {a.Name} -> {b.Name}: bought {buy.Qty / 1000.0:0.0} kg for {buy.PaidMites}, sold {sell.Qty / 1000.0:0.0} kg for {sell.PaidMites}, toll {sell.TollMites}"; }
        return true;
    }

    /// <summary>The bot's purse (unsold stock is counted at nothing: the profit is realised coin only, and <see cref="LeftoverKg"/> says what was left).</summary>
    private long Worth(MarketBook[] books) => _run.Econ.Ledger.Balance(_party.Purse).Mites;

    public long LeftoverKg => _party.Store.TotalMassG() / 1000;
}
