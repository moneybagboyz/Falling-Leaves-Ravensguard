using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Economy;

/// <summary>Which goods feed a household and in what order (households spec 3.2): staples flour, bread, then grain; relish by perishability, most perishable first.</summary>
public sealed class FoodPlan
{
    public ushort[] Staples { get; }
    public ushort[] Relishes { get; }
    public ushort Ale { get; }
    public ushort Salt { get; }
    public ushort Firewood { get; }
    private readonly bool[] _isGrain;

    public FoodPlan(GoodCatalog c)
    {
        int Rank(ushort g) => c.Def(g).Class switch { "flour" => 0, "bread" => 1, _ => 2 };
        Staples = Enumerable.Range(0, c.Count).Select(i => (ushort)i).Where(g => c.HasTag(g, "staple") && c.FoodMeasuresPpm(g) > 0)
            .OrderBy(Rank).ThenBy(g => g).ToArray();
        Relishes = Enumerable.Range(0, c.Count).Select(i => (ushort)i).Where(g => c.HasTag(g, "relish") && c.FoodMeasuresPpm(g) > 0)
            .OrderByDescending(g => c.SpoilPpm(g, StoreKind.House)).ThenBy(g => g).ToArray();
        _isGrain = new bool[c.Count];
        foreach (var g in Staples) _isGrain[g] = c.Def(g).Class == "grain";
        Ale = c.IdOf("ale"); Salt = c.IdOf("salt"); Firewood = c.IdOf("firewood");
    }

    public bool IsGrain(ushort good) => _isGrain[good];
}

/// <summary>What one household's day of needs came to (households spec 3.2). Measures are in hundredths; grams are of the good itself.</summary>
public sealed record NeedsResult(
    int Members, int NeedStapleX100, int NeedRelishX100, int EatenStapleX100, int EatenRelishX100, int SubstitutedX100, int FedPermille,
    long AleNeedG, long AleDrunkG, long SaltNeedG, long SaltEatenG, long FuelNeedG, long FuelBurnedG, long MillTollG,
    bool DietPoor, bool Cold, bool Hungry, HouseholdTier Tier, int FoodCoverDaysX10, int FuelCoverDaysX10);

/// <summary>
/// The daily needs pass of one household (households spec 3.2): eat the staple, eat the relish (grain substitutes a shortfall), drink, burn fuel, update each
/// member's nutrition reserve and the cached cover days. Integer arithmetic throughout; households are independent, so any processing order gives the same
/// result (G-H13) unless two share a miller's store. No deaths here (the population spec owns them). Not modelled yet: the wear of clothes and tools,
/// the seed-corn emergency and parish charity (3.2 step 5, 3.6).
/// </summary>
public static class HouseholdNeeds
{
    /// <summary>1 measure of food is 1,100 g of grain (plan 3a).</summary>
    public const int GramsPerMeasure = 1100;
    /// <summary>Everyone eats 1.0 measure-equivalent a day (decision H1).</summary>
    public const int MaxNutritionCd = 3000;
    private const long FoodPpmScale = 10_000_000; // grams x ppm / this = measures x 100

    /// <summary>Firewood multiplier in percent: 200 at 0 C or colder, 150 at 6 C, 100 from 12 C up.</summary>
    public static int FuelMultX100(int tMeanC) => 100 + Math.Clamp((12 - tMeanC) * 100 / 12, 0, 100);

    /// <summary>Grams of a good needed to supply <paramref name="measuresX100"/> hundredths of a measure (rounded up).</summary>
    public static long GramsFor(GoodCatalog c, ushort good, long measuresX100)
        => (measuresX100 * FoodPpmScale + c.FoodMeasuresPpm(good) - 1) / c.FoodMeasuresPpm(good);

    /// <summary>Hundredths of a measure in <paramref name="grams"/> of the good (rounded down).</summary>
    public static long MeasuresX100(GoodCatalog c, ushort good, long grams) => grams * c.FoodMeasuresPpm(good) / FoodPpmScale;

    /// <summary>Grain-equivalent grams for a number of measure hundredths: 4.00 measures is 4,400 g.</summary>
    public static long GrainEquivalentG(long measuresX100) => measuresX100 * GramsPerMeasure / 100;

    /// <summary>The household's needs of the day for its class (households spec 3.2) without touching any stock.</summary>
    public static (int Adults, int Children, int StapleX100, int RelishX100, long AleMl, long SaltG, long FuelG) Basket(EconomyState e, PersonTable people, HouseholdTable households, int hh, int day, int tMeanC)
    {
        var b = e.Basket(households.Class[hh]);
        int adults = 0, kids = 0;
        foreach (int p in households.Members(hh)) { if (people.AgeYears(p, day) >= 12) adults++; else kids++; }
        int n = adults + kids;
        return (adults, kids, n * b.StapleMeasuresPerPersonDayX100, n * b.RelishMeasuresPerPersonDayX100,
            (long)b.AleMlPerPersonDay * (2 * adults + kids) / 2, (long)n * b.SaltGPerPersonDay, (long)n * b.FirewoodGPerPersonDay * FuelMultX100(tMeanC) / 100);
    }

    /// <summary>Grams of vegetables a member's garden gives on each day of its season (doy 11-40): 868 t a game year for 25,000 people (resources spec G-R14, gardens 130,000 measures).</summary>
    public const int GardenGPerPersonDay = 1100, GardenFirstDoy = 11, GardenLastDoy = 40, HenEggGPerPersonDay = 14;

    /// <summary>
    /// Household production of relish (resources spec G-R14, integration decision I12): every household has a garden that gives vegetables in the growing days and hens
    /// that lay all year; created into the house store before the day's meal (reason Harvest). Cows, sheep and fish come through the herds and the market.
    /// </summary>
    public static void Produce(EconomyState e, HouseholdTable hht, int hh, int doy, ushort vegetables, ushort eggs)
    {
        int n = hht.MemberCount[hh]; if (n == 0) return;
        var s = e.HouseholdStore[hh];
        if (doy >= GardenFirstDoy && doy <= GardenLastDoy) s.Create(vegetables, (long)n * GardenGPerPersonDay, CreateReason.Harvest);
        s.Create(eggs, (long)n * HenEggGPerPersonDay, CreateReason.Harvest);
    }

    /// <summary>
    /// The wear of the day (integration decision I10): each member may wear out a pair of shoes (one in <c>shoes_lifetime_days</c>) or a set of clothes, and the household a
    /// tool; a worn item is a count of items due that the household's shopping buys. Lifetimes are in historical days, as the basket says.
    /// </summary>
    public static void RollDurables(EconomyState e, HouseholdTable hht, int hh, ulong seed, long hourKey)
    {
        int n = hht.MemberCount[hh]; if (n == 0) return;
        var b = e.Basket(hht.Class[hh]);
        ulong id = hht.IdOf(hh).Value;
        for (int k = 0; k < EconomyState.Durables; k++)
        {
            int life = k == 0 ? b.ShoesLifetimeDays : k == 1 ? b.ClothesLifetimeDays : b.ToolsLifetimeDays;
            if (life <= 0) continue;
            long ppm = (k == 2 ? Math.Max(1, (n + 2) / 3) : n) * 1_000_000L / life;
            int idx = hh * EconomyState.Durables + k;
            if (e.DueDurables[idx] < 8 && DetRng.Range(seed, hourKey, id, 0x2C8, (uint)k, 0, 1_000_000) < ppm) e.DueDurables[idx]++;
        }
    }

    /// <summary>Survive / Buffer / Profit from cover in days x10 (households spec 3.5); cash cover is optional (null = not tracked yet).</summary>
    public static HouseholdTier TierOf(int foodDaysX10, int fuelDaysX10, int? cashDaysX10, int tMeanC, NeedsBasketDef basket)
    {
        if (foodDaysX10 < basket.SurviveBelowDays * 10 || (fuelDaysX10 < 30 && tMeanC < 0)) return HouseholdTier.Survive;
        if (foodDaysX10 < basket.BufferBelowDays * 10 || fuelDaysX10 < 100 || (cashDaysX10 ?? int.MaxValue) < 150) return HouseholdTier.Buffer;
        return HouseholdTier.Profit;
    }

    /// <summary>Buy order size in grams (households spec 3.4): reorder when stock is under half the target, then buy up to the target; otherwise 0.</summary>
    public static long OrderSizeG(long stockG, long targetG) => stockG < targetG / 2 ? targetG - stockG : 0;

    /// <summary>
    /// Eats up to <paramref name="measuresX100"/> from the staple goods in plan order. Grain with a miller in reach costs 16/15 (the 1/16 toll goes to the
    /// miller's store). Returns the measures actually eaten; <paramref name="tollG"/> is the grams paid to the miller.
    /// </summary>
    public static int EatStaple(EconomyState e, Store store, int measuresX100, int seedReserveG, Store? miller, ref long tollG)
    {
        var c = e.Goods; int left = measuresX100, eaten = 0; long reserve = seedReserveG;
        foreach (ushort g in e.Food.Staples)
        {
            if (left <= 0) break;
            bool grain = e.Food.IsGrain(g);
            long have = store.Qty(g);
            if (grain) { long held = Math.Min(have, reserve); reserve -= held; have -= held; }
            if (have <= 0) continue;
            bool tolled = grain && miller != null;
            long want = GramsFor(c, g, left);                                   // grams of food that feed what is left
            long eatG = Math.Min(want, tolled ? have * 15 / 16 : have);         // grams actually eaten (the toll comes on top)
            if (eatG <= 0) continue;
            long toll = tolled ? eatG * 16 / 15 - eatG : 0;                     // 4,400 g eaten: draw 4,693 g, toll 293 g
            int m = eatG >= want ? left : (int)Math.Min(left, MeasuresX100(c, g, eatG));
            if (m <= 0) continue;
            if (toll > 0 && !Store.Move(store, miller!, g, toll)) toll = 0;
            store.Destroy(g, eatG, DestroyReason.Eaten);
            tollG += toll; left -= m; eaten += m;
        }
        return eaten;
    }

    /// <summary>Eats up to <paramref name="measuresX100"/> from the relish goods, most perishable first. Returns the measures eaten.</summary>
    public static int EatRelish(EconomyState e, Store store, int measuresX100)
    {
        var c = e.Goods; int left = measuresX100, eaten = 0;
        foreach (ushort g in e.Food.Relishes)
        {
            if (left <= 0) break;
            long have = store.Qty(g); if (have <= 0) continue;
            long want = GramsFor(c, g, left), eatG = Math.Min(want, have);
            int m = eatG >= want ? left : (int)Math.Min(left, MeasuresX100(c, g, eatG));
            if (m <= 0) continue;
            store.Destroy(g, eatG, DestroyReason.Eaten);
            left -= m; eaten += m;
        }
        return eaten;
    }

    /// <summary>Hundredths of a measure the household could still eat from its staple and relish stock (for the cover cache).</summary>
    public static long FoodStockX100(EconomyState e, Store store, long seedReserveG = 0)
    {
        long s = 0, reserve = seedReserveG;
        foreach (ushort g in e.Food.Staples)
        {
            long have = store.Qty(g);
            if (e.Food.IsGrain(g)) { long held = Math.Min(have, reserve); reserve -= held; have -= held; }     // seed corn is not food (the eating step keeps it too)
            s += MeasuresX100(e.Goods, g, have);
        }
        foreach (ushort g in e.Food.Relishes) s += MeasuresX100(e.Goods, g, store.Qty(g));
        return s;
    }

    /// <summary>Runs one household's day. <paramref name="day"/> is the game day (ages), <paramref name="tMeanC"/> the day's mean temperature, <paramref name="miller"/> the store of a miller within reach (null: none).</summary>
    public static NeedsResult RunDay(EconomyState e, Population pop, int hh, int day, int tMeanC, Store? miller = null)
        => RunDay(e, pop.People, pop.Households, hh, day, tMeanC, miller);

    /// <summary>The same for bare tables (no region needed).</summary>
    public static NeedsResult RunDay(EconomyState e, PersonTable people, HouseholdTable hht, int hh, int day, int tMeanC, Store? miller = null)
    {
        int n = hht.MemberCount[hh];
        if (n == 0) return new NeedsResult(0, 0, 0, 0, 0, 0, 1000, 0, 0, 0, 0, 0, 0, 0, false, false, false, HouseholdTier.Profit, 0, 0);
        var need = Basket(e, people, hht, hh, day, tMeanC);
        var store = e.HouseholdStore[hh]; var c = e.Goods;
        int needTotal = need.StapleX100 + need.RelishX100;

        long toll = 0;
        int eatenStaple = EatStaple(e, store, need.StapleX100, e.SeedReserveG[hh], miller, ref toll);
        int eatenRelish = EatRelish(e, store, need.RelishX100);
        int shortRelish = need.RelishX100 - eatenRelish, subst = 0;
        if (shortRelish > 0) subst = EatStaple(e, store, shortRelish, e.SeedReserveG[hh], miller, ref toll);   // grain stands in, measure for measure
        bool dietPoor = shortRelish > 0;
        int eatenTotal = eatenStaple + eatenRelish + subst;
        int fed = needTotal == 0 ? 1000 : (int)Math.Min(1000, (long)eatenTotal * 1000 / needTotal);

        long aleNeedG = need.AleMl * c.Def(e.Food.Ale).LitreMassG / 1000;
        long aleDrunk = store.DestroyUpTo(e.Food.Ale, aleNeedG, DestroyReason.Eaten);
        long saltEaten = store.DestroyUpTo(e.Food.Salt, need.SaltG, DestroyReason.Eaten);
        long burned = store.DestroyUpTo(e.Food.Firewood, need.FuelG, DestroyReason.Burned);
        bool cold = burned < need.FuelG && tMeanC < 0;

        foreach (int p in hht.Members(hh))
        {
            int cd = e.NutritionCd[p];
            cd = fed >= 1000 ? Math.Min(MaxNutritionCd, cd + 50) : Math.Max(0, cd - (100 * (1000 - fed) + 500) / 1000);
            e.NutritionCd[p] = (short)cd;
        }

        long foodStock = FoodStockX100(e, store, e.SeedReserveG[hh]);
        int foodDays = needTotal == 0 ? 0 : (int)Math.Min(short.MaxValue, foodStock * 10 / needTotal);
        int fuelDays = need.FuelG == 0 ? short.MaxValue : (int)Math.Min(short.MaxValue, store.Qty(e.Food.Firewood) * 10 / need.FuelG);
        bool hungry = foodDays < 10;
        var tier = TierOf(foodDays, fuelDays, null, tMeanC, e.Basket(hht.Class[hh]));
        e.FoodCoverDaysX10[hh] = (short)foodDays; e.FuelCoverDaysX10[hh] = (short)fuelDays; e.Tiers[hh] = tier; e.DietPoor[hh] = dietPoor;
        var gr = e.Grievances[hh];
        gr = cold ? gr | Grievance.Cold : gr & ~Grievance.Cold;
        gr = hungry ? gr | Grievance.Hunger : gr & ~Grievance.Hunger;
        e.Grievances[hh] = gr;

        return new NeedsResult(n, need.StapleX100, need.RelishX100, eatenStaple, eatenRelish, subst, fed, aleNeedG, aleDrunk, need.SaltG, saltEaten, need.FuelG, burned, toll,
            dietPoor, cold, hungry, tier, foodDays, fuelDays);
    }
}
