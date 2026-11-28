using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;

namespace Ravensguard.Sim.Diagnostics;

/// <summary>One year of the economy and growth audit (P8-EA-1): read at a barrier, never written to the sim, never saved.</summary>
public sealed class EconomyYearRow
{
    public int Year;
    public long Alive, Households, Idle, HungryHouseholds, HungerDeaths;
    /// <summary>Food held in the households' stores, in tenths of a measure (a person-day) a head.</summary>
    public long FoodDaysX10PerHead;
    public long Fields, FallowFields, FertilityMeanX10, ClearedRows;
    public long WoodStockT, WoodCapT, FishStockKg, FishCapKg, GameAnimals, GameCapAnimals;
    public long MinesOpen, MinesWorking, MinesWorkedOut, MinesClosed, OreLeftT, OreInitialT, VeinOreT, MinedThisYearT;
    /// <summary>Median mid price over the books with a merchant, milli-mites a kilogram (a piece for a piece good): grain (wheat), timber (logs), planks, charcoal, bar iron, woollen cloth.</summary>
    public long Grain, Timber, Planks, Charcoal, Iron, Cloth;
    public long CoinInPurses, CoinCirculating;
    public long BusinessesOpen, BusinessesClosed, BusinessesEmpty, BusinessesFailing;
    public long WoodlotsOpen, CharcoalOpen, FisheriesOpen;
    /// <summary>The brakes on births (the marriage and dwelling gate, against hunger): co-resident wives of 18 to 44, those whose food reserve cuts their conception (multiplier under 1000), the mean of
    /// that multiplier (permille), those in a crowded house; unmarried adults of 18 to 44 and those of them waiting for a home; the dwellings standing empty; births and marriages so far.</summary>
    public long Wives, WivesFoodBraked, WivesFoodMultPm, WivesCrowded, Singles, SinglesWaiting, FreeDwellings, Births, Marriages, Immigrants, Emigrants;
}

/// <summary>
/// The measures of the economy and growth audit (P8-EA-1, overview 2.6): one row a game year over the home region (population, food a head, famine and hunger, fields and soil, cleared land,
/// the woodland, fish, game and ore left, the mines open and worked out, the prices of grain, timber, charcoal, iron and cloth, the coin, idle adults, and empty or failing works).
/// A pure read of the world: the century soak calls it at its yearly barrier and prints the decade table. Integer maths throughout.
/// </summary>
public static class EconomyCentury
{
    private static readonly string[] PriceGoods = ["wheat", "logs", "timber_planks", "charcoal", "iron_bar", "cloth_wool"];

    public static EconomyYearRow Sample(SimWorld sim, EconomyWorld ew, int year, long minedBefore = 0)
    {
        var r = new EconomyYearRow { Year = year };
        var p = sim.People; var hht = sim.Households; var e = sim.Economy; long day = sim.State.HourDone / 24;
        for (int i = 0; i < p.Count; i++)
        {
            if (p.Status[i] != PersonStatus.Alive) continue;
            r.Alive++;
            int age = sim.AgeOn(i, day);
            if (age is >= 16 and < 60 && p.Occupation[i] == Occupation.None && !(hht.ParcelCount[Math.Max(0, p.Household[i])] > 0)) r.Idle++;
        }
        var rates = sim.Rates; long multSum = 0;
        for (int i = 0; i < p.Count; i++)
        {
            if (p.Status[i] != PersonStatus.Alive) continue;
            int age = sim.AgeOn(i, day); if (age < rates.FertilityAgeMin || age > rates.FertilityAgeMax) continue;
            int sp = p.Spouse[i];
            if (sp < 0 || p.Status[sp] != PersonStatus.Alive) { r.Singles++; if (p.MarriageWait[i] > 0) r.SinglesWaiting++; continue; }
            if (p.Sex[i] != Sex.Female || p.Household[sp] != p.Household[i] || p.Household[i] < 0) continue;
            r.Wives++; int m = People.Vital.VitalMath.FertilityFoodMult(rates, e.NutritionCd[i]); multSum += m; if (m < 1000) r.WivesFoodBraked++;
            int hh = p.Household[i]; if (People.Vital.VitalMath.Crowding(hht.MemberCount[hh], hht.Beds[hh]) > rates.FertilityCrowdingOverPct) r.WivesCrowded++;
        }
        r.WivesFoodMultPm = r.Wives == 0 ? 0 : multSum / r.Wives;
        var pop = sim.Population;
        for (int st = 0; st < pop.Region.Settlements.Count; st++)
        {
            var lots = pop.Region.Settlements[st].Plan.Lots;
            for (int l = 0; l < lots.Count && l < pop.LotBeds[st].Length; l++) if (pop.LotBeds[st][l] >= 2 && pop.LotHousehold[st][l] < 0 && lots[l].IsDwelling && lots[l].Dwelling != WorldGen.Region.DwellingType.Manor) r.FreeDwellings++;
        }
        r.Births = sim.Vital.Births; r.Marriages = sim.Vital.Marriages; r.Immigrants = sim.Vital.Immigrants; r.Emigrants = sim.Vital.Emigrants;
        long food = 0;
        for (int h = 0; h < hht.Count; h++)
        {
            if (hht.MemberCount[h] == 0) continue;
            r.Households++;
            if ((e.Grievances[h] & Grievance.MissedMeal) != 0) r.HungryHouseholds++;
            food += HouseholdNeeds.FoodStockX100(e, e.HouseholdStore[h]);
        }
        r.FoodDaysX10PerHead = r.Alive == 0 ? 0 : food / 10 / r.Alive;
        r.HungerDeaths = sim.Vital.DeathsByCause[(int)DeathCause.Hunger];

        var f = ew.Farming.State.Fields; long fert = 0;
        for (int i = 0; i < f.Count; i++) { r.Fields++; fert += f.Fertility[i]; if (f.Crop[i] == WorldGen.Region.Crop.Fallow) r.FallowFields++; }
        r.FertilityMeanX10 = r.Fields == 0 ? 0 : fert * 10 / r.Fields;

        var lay = ew.Resources; r.ClearedRows = lay.ClearedCount;
        var (ws, _) = lay.Totals(ResourceKind.Wood); r.WoodStockT = ws / 1_000_000;
        long wcap = 0; for (int i = 0; i < lay.Count; i++) if (lay.KindOf(i) == ResourceKind.Wood) wcap += lay.CapG(i);
        r.WoodCapT = wcap / 1_000_000;          // CapG reads 0 for a cleared row; Totals reads the raw cap
        var (fs, fc) = lay.Totals(ResourceKind.Fish); r.FishStockKg = fs / 1000; r.FishCapKg = fc / 1000;
        var (gs, gc) = lay.Totals(ResourceKind.Game); r.GameAnimals = gs / 1_000_000; r.GameCapAnimals = gc / 1_000_000;

        var prod = ew.Production; long left = 0, init = 0;
        foreach (var b in prod.Businesses)
        {
            var kind = prod.KindOf(b);
            if (b.State == BusinessState.Open)
            {
                r.BusinessesOpen++;
                if (b.Staff.Count == 0 && !kind.Trader) r.BusinessesEmpty++;
                if (b.LossSeasons > 0) r.BusinessesFailing++;
                if (kind.Id == "woodlot") r.WoodlotsOpen++; else if (kind.Id == "charcoal_crew") r.CharcoalOpen++; else if (kind.Id == "fishery") r.FisheriesOpen++;
            }
            else r.BusinessesClosed++;
            if (b.Mine < 0 || b.Mine >= prod.Mines.Count) continue;
            var m = prod.Mines[b.Mine]; left += m.RemainingG; init += m.InitialG;
            if (b.State != BusinessState.Open) { r.MinesClosed++; continue; }
            r.MinesOpen++;
            if (m.RemainingG <= 0) r.MinesWorkedOut++; else if (m.State == MineStateKind.Active && b.Staff.Count > 0) r.MinesWorking++;
        }
        r.OreLeftT = left / 1_000_000; r.OreInitialT = init / 1_000_000; r.MinedThisYearT = (init - left - minedBefore) / 1_000_000;
        foreach (var v in sim.Population.Region.Veins) if (Prospecting.ProspectTruth.IsOre(v.Type)) r.VeinOreT += v.Tonnage;

        var prices = new long[PriceGoods.Length]; var cat = e.Goods;
        for (int k = 0; k < PriceGoods.Length; k++)
        {
            if (!cat.TryIdOf(PriceGoods[k], out var g)) continue;
            var list = new List<long>();
            foreach (var bk in ew.Markets.AllBooks()) if (bk.Merchants.Count > 0) list.Add(cat.IsPiece(g) ? bk.MidMicro(g) / 1000 : bk.MidMicro(g));      // milli-mites a kg (a piece)
            list.Sort(); prices[k] = list.Count == 0 ? 0 : list[list.Count / 2];
        }
        (r.Grain, r.Timber, r.Planks, r.Charcoal, r.Iron, r.Cloth) = (prices[0], prices[1], prices[2], prices[3], prices[4], prices[5]);
        var au = e.Ledger.Audit(); r.CoinInPurses = au.InPurses.Mites; r.CoinCirculating = au.Circulating.Mites;
        return r;
    }

    /// <summary>The ore mined so far, grams (the start of a year's <see cref="EconomyYearRow.MinedThisYearT"/>).</summary>
    public static long MinedG(EconomyWorld ew) { long g = 0; foreach (var m in ew.Production.Mines) g += m.InitialG - m.RemainingG; return g; }

    public static string Header =>
        "  year   alive   hh  idle hungryHH hungerD food/hd fert fallow cleared woodT/capT      fishT gameN  mines(open/work/out/closed) oreLeftT/initT minedT  grain timber planks charcoal iron cloth  coinM  circM  biz(open/closed/empty/failing) woodlot charcoal fishery";

    public static string Line(EconomyYearRow r) =>
        $"  {r.Year,4} {r.Alive,7} {r.Households,4} {r.Idle,5} {r.HungryHouseholds,8} {r.HungerDeaths,7} {r.FoodDaysX10PerHead / 10.0,7:0.0} {r.FertilityMeanX10 / 10.0,4:0.0} {r.FallowFields,6} {r.ClearedRows,7} {r.WoodStockT,6}/{r.WoodCapT,-6} {r.FishStockKg / 1000,6}/{r.FishCapKg / 1000,-4} {r.GameAnimals,5} " +
        $"{r.MinesOpen,6}/{r.MinesWorking}/{r.MinesWorkedOut}/{r.MinesClosed,-14} {r.OreLeftT,6}/{r.OreInitialT,-7} {r.MinedThisYearT,6} {r.Grain / 1000.0,6:0.00} {r.Timber / 1000.0,6:0.00} {r.Planks / 1000.0,6:0.00} {r.Charcoal / 1000.0,8:0.00} {r.Iron / 1000.0,6:0.00} {r.Cloth / 1000.0,7:0.00} {r.CoinInPurses / 1_000_000,6} {r.CoinCirculating / 1_000_000,6}  " +
        $"{r.BusinessesOpen,5}/{r.BusinessesClosed}/{r.BusinessesEmpty}/{r.BusinessesFailing,-20} {r.WoodlotsOpen,7} {r.CharcoalOpen,8} {r.FisheriesOpen,7} | wives {r.Wives} foodBraked {r.WivesFoodBraked} mult {r.WivesFoodMultPm} crowded {r.WivesCrowded} singles {r.Singles} waiting {r.SinglesWaiting} freeDwellings {r.FreeDwellings} births {r.Births} marriages {r.Marriages} immig {r.Immigrants} emig {r.Emigrants}";

    /// <summary>Coin growth a year over a span, in basis points (a hundredth of a percent), compounded: the plan asks 100 to 200 (1 to 2 percent a year).</summary>
    public static long CoinGrowthBp(long from, long to, int years)
    {
        if (from <= 0 || to <= 0 || years <= 0) return 0;
        // integer bisection on the yearly factor in basis points: (1 + g/10000)^years ~ to/from
        long lo = -5000, hi = 5000;
        while (lo < hi)
        {
            long mid = (lo + hi + 1) >> 1; Int128 v = from;
            for (int i = 0; i < years && v <= (Int128)to * 4; i++) v = v * (10_000 + mid) / 10_000;
            if (v <= to) lo = mid; else hi = mid - 1;
        }
        return lo;
    }
}
