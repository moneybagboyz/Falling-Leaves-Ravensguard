using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Tests.Farming;

/// <summary>A year-by-year record of the headless soak (grams of grain; measures are 1,100 g).</summary>
public sealed record SoakYear(int Year, long HarvestG, long SeedG, long SpoiledG, long EatenG, long ShortG, int HungryDays, long StockG, long PopulationDays, string Weather, int ChronicleCauses);

/// <summary>
/// The 10-year farming soak of the seed-1234 region: farming runs daily with the economy's spoilage, and an "ideal market" eats 0.92 measures per person-day from
/// the region's threshed grain (households first, above their seed reserve, then the parish granaries), so a year is a famine year when the region cannot feed itself.
/// </summary>
public sealed class FarmingSoak
{
    public const double MeasuresPerPersonDay = 0.92;
    public EconomyState Econ { get; }
    public FarmingSystem Sys { get; }
    public Chronicle Chron { get; } = new();
    public List<SoakYear> Years { get; } = [];
    private readonly Population _pop;
    private readonly ushort[] _eatOrder;
    private readonly bool[] _winterGood;
    private readonly int _people;

    public FarmingSoak(FarmingSettings? settings = null, int topUpDays = 45)
    {
        var b = EconFixture.Shared; _pop = b.Pop;
        Econ = EconFixture.NewEconomy();
        var rw = RegionWeather.ForContinent(b.Continent);
        var wx = new RegionFarmWeather(rw, rw.Fronts.Long);
        Sys = new FarmingSystem(_pop.Region, _pop, Econ, settings ?? FarmingSettings.FromContent(EconFixture.Content), wx, Chron, new IdAllocator(), EconFixture.Seed);
        Sys.ProvisionInitialStock();
        _people = _pop.People.Count;
        string[] order = ["wheat", "rye", "barley", "oats", "peas", "beans"];
        _eatOrder = order.Select(Econ.Goods.IdOf).ToArray(); _winterGood = [true, true, false, false, false, false];
        // the starting stock is a month of food; the harness adds the carry-over the previous harvest would have left so the first year is not short
        int per = (int)(topUpDays - 30);
        if (per > 0)
            for (int h = 0; h < _pop.Households.Count; h++)
                Econ.HouseholdStore[h].Create(Econ.Goods.IdOf("wheat"), (long)_pop.Households.MemberCount[h] * (long)(MeasuresPerPersonDay * 1100) * per, CreateReason.Seed);
    }

    public int People => _people;

    private long Grain() { long t = 0; foreach (var g in _eatOrder) t += Sys.TotalGrams(g); return t; }

    /// <summary>Eats <paramref name="needG"/> grams of grain; returns the shortfall.</summary>
    private long Eat(long needG, long[] winterKeep, long[] springKeep)
    {
        long rest = needG;
        for (int pass = 0; pass < 3 && rest > 0; pass++)
        {
            if (pass != 1)
            {
                for (int h = 0; h < Econ.HouseholdStore.Length && rest > 0; h++)
                {
                    if ((_pop.Households.Class[h] == SocialClass.Gentry) != (pass == 2)) continue;       // gentry last: their rent stock is the lender of seed
                    var st = Econ.HouseholdStore[h]; long kw = winterKeep[h], ks = springKeep[h];
                    for (int g = 0; g < _eatOrder.Length && rest > 0; g++)
                    {
                        long q = st.Qty(_eatOrder[g]); if (q == 0) continue;
                        long hold = _winterGood[g] ? Math.Min(q, kw) : Math.Min(q, ks);
                        if (_winterGood[g]) kw -= hold; else ks -= hold;
                        long take = Math.Min(rest, q - hold);
                        if (take > 0) { st.Destroy(_eatOrder[g], take, DestroyReason.Eaten); rest -= take; }
                    }
                }
            }
            else
            {
                for (int s = 0; s < _pop.Region.Settlements.Count && rest > 0; s++)
                {
                    var st = Econ.OtherStores[FarmIds.ParishGranary(s)];
                    foreach (var g in _eatOrder) { long take = Math.Min(rest, st.Qty(g)); if (take > 0) { st.Destroy(g, take, DestroyReason.Eaten); rest -= take; } if (rest == 0) break; }
                }
            }
        }
        return rest;
    }

    /// <summary>Runs <paramref name="years"/> years from the world's start (year <c>pop.NowYear</c>, day 1).</summary>
    public void Run(int years)
    {
        int y0 = _pop.NowYear;
        long dayNeedG = (long)(_people * MeasuresPerPersonDay * 1100);
        for (int y = y0; y < y0 + years; y++)
        {
            long harvest0 = 0, sown0 = 0, spoil0 = 0, eaten0 = 0;
            var ids = _eatOrder; long h0 = Mass(ids, true), s0 = Mass(ids, false), sp0 = Spoiled(ids), e0 = EatenMass(ids);
            long shortG = 0; int hungry = 0;
            for (int d = 1; d <= 60; d++)
            {
                var now = Synth.At(y, d);
                Sys.StepDay(now);
                Econ.ApplyDailySpoilage(EconFixture.Seed, now.TotalHours, 10);
                var (w, s) = Sys.SeedNeeds();
                long miss = Eat(dayNeedG, w, s);
                if (miss > 0) { shortG += miss; hungry++; }
            }
            var year = new SoakYear(y, Mass(ids, true) - h0, Mass(ids, false) - s0, Spoiled(ids) - sp0, EatenMass(ids) - e0, shortG, hungry, Grain(), _people * 60L, "", 0);
            Years.Add(year);
        }
    }

    private long Mass(ushort[] ids, bool created) { long t = 0; foreach (var g in ids) t += created ? Econ.Mass.Created(g, CreateReason.Harvest) : Econ.Mass.Destroyed(g, DestroyReason.Sown); return t; }
    private long Spoiled(ushort[] ids) { long t = 0; foreach (var g in ids) t += Econ.Mass.Destroyed(g, DestroyReason.Spoiled); return t; }
    private long EatenMass(ushort[] ids) { long t = 0; foreach (var g in ids) t += Econ.Mass.Destroyed(g, DestroyReason.Eaten); return t; }
}
