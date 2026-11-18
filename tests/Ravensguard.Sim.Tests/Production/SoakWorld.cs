using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.Production;

/// <summary>
/// Seed 1234's region with its own private population (the soak mutates employers), seeded economy, markets, businesses and resource layer, plus a simple
/// driver that runs the hourly steps in the tick order of the overview (H2 work, H3 session, H4 slice, H5 daily, S steps) with stand-ins for the systems that
/// are not built yet: a harvest and wages stub. The driver is a test fixture, not a scheduler.
/// </summary>
public sealed class SoakWorld
{
    public const ulong Seed = 1234;
    public Population Pop { get; private init; } = null!;
    public EconomyState Econ { get; private init; } = null!;
    public MarketSystem Markets { get; private init; } = null!;
    public ProductionSystem Sys { get; private init; } = null!;
    public ResourceLayer Layer { get; private init; } = null!;
    public int Day { get; private set; }
    public ushort[] Grains { get; private init; } = [];

    public static SoakWorld Create()
    {
        var b = EconFixture.Shared;
        var region = RegionGenerator.Generate(b.Continent, b.History, b.CellX, b.CellY, b.Rs);
        LandmarkPlacer.Place(region, b.History, EconFixture.Content);
        var pop = Populator.Populate(region, b.History, EconFixture.Content);
        var econ = EconomyState.Create(pop, EconFixture.Content, Seed);
        var pc = ProdKit.Content;
        var layer = ResourceLayer.Build(region, pc.Settings, Seed);
        var markets = new MarketSystem(econ, pop, Seed);
        var sys = BusinessBuilder.Create(econ, pop, pc, Seed, markets, layer);
        var cat = econ.Goods;
        return new SoakWorld { Pop = pop, Econ = econ, Markets = markets, Sys = sys, Layer = layer, Grains = new[] { "wheat", "rye", "barley", "oats", "peas", "beans" }.Select(cat.IdOf).ToArray() };
    }

    // ---------- stand-ins ----------

    private static int BaseMeasures(Crop c) => c switch { Crop.Wheat => 48, Crop.Rye => 46, Crop.Barley => 48, Crop.Oats => 46, Crop.Peas => 42, Crop.Beans => 42, _ => 0 };
    private static string GoodOf(Crop c) => c.ToString().ToLowerInvariant();

    /// <summary>Harvest stand-in (farming spec 3.1 numbers): each cropped field gives <c>base x 1,100 g x min(fert, 91) / 70</c>, 80% to its holder after seed; <paramref name="scalePct"/> scales it.</summary>
    public long Harvest(int scalePct)
    {
        long total = 0;
        foreach (var f in Pop.Region.Fields)
        {
            int bm = BaseMeasures(f.Crop); if (bm == 0) continue;
            int holder = f.Parcel >= 0 && f.Parcel < Pop.ParcelHolder.Length ? Pop.ParcelHolder[f.Parcel] : -1;
            if (holder < 0) continue;
            long g = (long)bm * 1100 * Math.Min((int)f.Fertility, 91) / 70 * 80 / 100 * scalePct / 100;
            Econ.HouseholdStore[holder].Create(Econ.Goods.IdOf(GoodOf(f.Crop)), g, CreateReason.Harvest);
            total += g;
        }
        return total;
    }

    private static readonly int[] TempBySeason = [8, 18, 9, 0];

    /// <summary>Runs <paramref name="days"/> game days from the current day; calls <paramref name="daily"/> after each day's hour 23.</summary>
    public void Run(int days, Action<int, GameTime>? daily = null, Action<GameTime>? hourly = null)
    {
        var hh = Pop.Households; var ledger = Econ.Ledger;
        for (int d = 0; d < days; d++, Day++)
        {
            for (int hour = 0; hour < 24; hour++)
            {
                var now = GameTime.FromGameHours(Day * 24L + hour);
                int tMean = TempBySeason[(int)now.Season];
                Sys.WorkHour(now);                                       // H2
                Markets.HourlySession(now);                              // H3
                Sys.DailySlice(now);                                     // H4: businesses
                for (int h = hour; h < hh.Count; h += 24)                // H4: households whose index mod 24 is the hour
                {
                    if (hh.MemberCount[h] == 0) continue;
                    HouseholdNeeds.RunDay(Econ, Pop, h, Pop.NowDay + Day, tMean);
                    FarmerSells(h, now);
                    Markets.PostHouseholdOrders(h, Day, tMean);
                }
                if (hour == 0)                                           // H5
                {
                    Markets.DailyGlobals(now, tMean);
                    Sys.DailyGlobals(now);
                    Econ.ApplyDailySpoilage(Seed, now.TotalHours, tMean);
                    foreach (var bk in Markets.AllBooks()) bk.SyncAll();
                    PayEmployees(now);
                    if (now.DayOfYear == 31) Harvest(100);
                    if (now.DayOfYear is 1 or 16 or 31 or 46)            // H6 seasonal, in the overview's S order
                    {
                        Layer.Regrow(now, ProdKit.Content.Settings);
                        Sys.Seasonal(now);                               // S5 dividends and closures
                        Markets.SeasonalDrift(now);                      // S5 price drift
                    }
                }
                hourly?.Invoke(now);
            }
            daily?.Invoke(Day, GameTime.FromGameHours(Day * 24L + 23));
        }
    }

    private int[]? _farm;
    /// <summary>Farm households sell grain above 25 days of their own needs, a twentieth of the surplus a day, merchants first.</summary>
    private void FarmerSells(int h, GameTime now)
    {
        _farm ??= Enumerable.Range(0, Pop.Households.Count).Select(i => Pop.Households.ParcelCount[i] > 0 ? 1 : 0).ToArray();
        if (h >= _farm.Length || _farm[h] == 0) return;
        var book = Markets.BookForHousehold(h); var party = Markets.HouseholdParty(h);
        int members = Pop.Households.MemberCount[h];
        foreach (ushort g in Grains)
        {
            long have = Econ.HouseholdStore[h].Qty(g), keep = (long)members * 1100 * 25 / Grains.Length;
            if (have <= keep) continue;
            book.SellOrList(party, g, Math.Max(1, (have - keep) / 20), 800, Day);
        }
    }

    /// <summary>Wage stand-in: people employed by a lord who are not staff of a business earn the class wage from the employer's household, until it runs dry.</summary>
    private void PayEmployees(GameTime now)
    {
        var p = Pop.People; var staff = new HashSet<int>();
        foreach (var b in Sys.Businesses) foreach (int s in b.Staff) staff.Add(s);
        for (int i = 0; i < p.Count; i++)
        {
            if (p.Employer[i] < 0 || staff.Contains(i) || p.Household[i] < 0) continue;
            if (p.Occupation[i] is not (Occupation.Labourer or Occupation.Servant or Occupation.Guard or Occupation.Reeve or Occupation.Steward or Occupation.Shepherd or Occupation.Ploughman)) continue;
            int payer = p.Household[p.Employer[i]]; if (payer < 0 || payer == p.Household[i]) continue;
            var from = Econ.HouseholdPurse[payer];
            long wage = p.Occupation[i] is Occupation.Servant ? 100 : 150;
            if (Econ.Ledger.Balance(from).Mites >= wage) Econ.Ledger.Transfer(from, Econ.HouseholdPurse[p.Household[i]], new Coin(wage), TransferReason.Stipend);
        }
    }
}
