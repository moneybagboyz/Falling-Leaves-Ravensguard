using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Continental;

/// <summary>
/// Constants of the continent tick (continent-tick spec 2.3). They live in code for v1 (the spec's <c>continent.jsonc</c> is not loaded yet); every rate states its unit in its name.
/// All integer.
/// </summary>
public static class ContinentRules
{
    /// <summary>Goods the continent tracks (CT5): grain, fuel, iron, cloth, ale, salt, tools, bread.</summary>
    public static readonly string[] TrackedGoods = ["wheat", "firewood", "iron_bar", "cloth_wool", "ale", "salt", "tools", "bread"];
    public const int K = 8;
    /// <summary>Grams a person consumes a day of each tracked good, and the days of cover a summary region aims at (the books' own targets).</summary>
    public static readonly int[] DailyGramsPerPerson = [1012, 5000, 40, 15, 400, 25, 6, 200];
    public static readonly int[] TargetCoverDays = [30, 20, 60, 45, 5, 60, 60, 2];
    /// <summary>Measures of grain a person eats a day, x100 (0.92).</summary>
    public const int GrainMeasuresPerDayX100 = 92;
    /// <summary>Grain measures a kilogram of wheat holds, x1000 (0.909091).</summary>
    public const int MeasuresPerKgX1000 = 909;
    public const int SpoilagePctPerSeason = 5;
    /// <summary>A normal harvest as a share of a year's need, permille (G-C1: 1,542,195 / 1,380,000 measures).</summary>
    public const int HarvestOverNeedPermille = 1117;
    public const int BirthRatePctX100PerYear = 258, DeathRatePctX100PerYear = 258;
    public const int HouseCapPersonsPerDwellingX10 = 48;
    public const int EmigrateNormalPermille = 5, EmigrateFaminePermille = 30, FamineThresholdDays = 3;
    public const int NewsMetresPerGameSecondX100 = 25;           // 0.25 m per game second
    public const int NewsMaxHops = 3;
    public const int ExportSurplusCoverX100 = 130, ImportDeficitCoverX100 = 70;
    /// <summary>Share of a summary neighbour's coin its merchants pay out in one season, permille (split between the detailed regions that trade with it).</summary>
    public const int PayBudgetPermillePerSeason = 40;
    /// <summary>Kilograms a cart-load of the edge moves, and carts a season by road class (spec 3.6): Lane, Track, Road, Highway.</summary>
    public static readonly int[] EdgeCartsPerSeason = [0, 6, 20, 60, 120];
    public const int CartKg = 500, RoundTripsPerSeasonX10 = 25;
    /// <summary>Metres between the centres of two orthogonally adjacent regions.</summary>
    public const int RegionKm = 32;
    public const int PriceMinPermille = 300, PriceMaxPermille = 4000;
    /// <summary>The 0x250 stream of the continent tick (overview 2.3).</summary>
    public const uint StreamContinent = Ravensguard.Sim.Core.RngStreams.Continent;
}

/// <summary>What a news item is about (continent-tick spec 3.8).</summary>
public enum NewsKind : byte { Famine, Plague, HarvestFailure, BanditRaid, WarStart, WarEnd, PriceSpike, CaravanLost }

/// <summary>One item of word travelling between regions: what, where it happened, how big, on which game day, and how many hops it has made.</summary>
public readonly record struct NewsItem(NewsKind Kind, byte Origin, int Magnitude, int Day, byte Hops);

/// <summary>
/// A region as the continent tick sees it (continent-tick spec 2.1, trimmed to what v1 uses). Detailed regions copy it out of their records every season (C1); the others
/// evolve by the closed forms of C2-C5. Money in mites, food in grams and measures, prices as permille of the good's base price.
/// </summary>
public sealed class RegionSummary
{
    public byte Id;
    public bool Detailed;
    public int Pop, Households, Dwellings, Vacancies;
    /// <summary>Grams of each tracked good in all stores of the region.</summary>
    public long[] StockG = new long[ContinentRules.K];
    public long GrainCapacityNet;
    public long CoinTotal, TreasuryCoin;
    /// <summary>Price of each tracked good as permille of its base price.</summary>
    public int[] PriceIdx = new int[ContinentRules.K];
    /// <summary>Days of cover x 10 per tracked good.</summary>
    public int[] CoverX10 = new int[ContinentRules.K];
    public int FamineDays, Unrest, Prosperity;
    public byte Realm = 255, WarFlags;
    public int PopRemainder;
    /// <summary>Game day the prices above were last set (the age callers discount by).</summary>
    public int PriceDay;
    public int LastDetailDay = -1;
    /// <summary>Recent news this region has heard, oldest first (at most <see cref="NewsCap"/>).</summary>
    public List<NewsItem> News = [];
    public const int NewsCap = 8;

    public long FoodMeasures => StockG[0] * ContinentRules.MeasuresPerKgX1000 / 1_000_000;

    public RegionSummary Clone()
    {
        var c = (RegionSummary)MemberwiseClone();
        c.StockG = (long[])StockG.Clone(); c.PriceIdx = (int[])PriceIdx.Clone(); c.CoverX10 = (int[])CoverX10.Clone(); c.News = [.. News];
        return c;
    }
}

/// <summary>A road link between two adjacent regions (continent-tick spec 2.2): the gates the history's trade routes make on their shared border.</summary>
public sealed class ContinentEdge
{
    public int Index, A, B, Gates;
    public RoadClass Class;
    public int LengthKm = ContinentRules.RegionKm;
    public bool Closed;
    /// <summary>Kilograms a season the road can carry (carts x round trips x 500 kg).</summary>
    public long CapacityKgPerSeason => (long)ContinentRules.EdgeCartsPerSeason[(int)Class] * Math.Max(1, Gates) * ContinentRules.CartKg * ContinentRules.RoundTripsPerSeasonX10 / 10;
    public int Other(int region) => region == A ? B : A;
}

/// <summary>
/// What a detailed region may trade with one neighbour this season: the coin the neighbour can pay out, the grams of each tracked good it can spare, and what the region has
/// already done. Written by the continent tick at the barrier (the limits) and by the region's own thread between ticks (the running totals): one writer each, so no locks.
/// </summary>
public sealed class EdgeTrade
{
    public int Edge, Neighbour;
    public long PayBudgetMites;
    public long[] SpareG = new long[ContinentRules.K];
    /// <summary>Signed coin the region took from (+) or gave to (-) the neighbour since the last tick, not yet booked in the neighbour's summary.</summary>
    public long CoinNetIn;
    /// <summary>Signed grams of tracked goods the region took from (-) or gave to (+) the neighbour since the last tick.</summary>
    public long[] StockNetOut = new long[ContinentRules.K];
    /// <summary>Tolls paid on this edge's road, mites (they become the neighbour's treasury coin).</summary>
    public long TollMites;
    public EdgeTrade Clone() { var c = (EdgeTrade)MemberwiseClone(); c.SpareG = (long[])SpareG.Clone(); c.StockNetOut = (long[])StockNetOut.Clone(); return c; }
}

/// <summary>Everything the continent and one detailed region exchange between two ticks (see <see cref="ContinentState.Links"/>).</summary>
public sealed class RegionLink
{
    public int Region;
    public List<EdgeTrade> Edges = [];
    /// <summary>People who left this region since the last tick, with the coin and grain they took (booked into the neighbours at the tick).</summary>
    public int EmigrantPersons, EmigrantHouseholds;
    public long EmigrantCoin, EmigrantMeasures;
    /// <summary>Grams of every tracked good the emigrants took (grain also counts in <see cref="EmigrantMeasures"/>); goods outside the 8 leave as exports and are not stocked on the continent.</summary>
    public long[] EmigrantG = new long[ContinentRules.K];
    /// <summary>Immigrants the tick sends for the next season: persons, the households they make up, the coin and grain they bring (all taken out of the source summaries already).</summary>
    public int PendingPersons, PendingHouseholds;
    public long PendingCoin, PendingMeasures;
    public int PendingFromRegion = -1;
    /// <summary>Cumulative persons that left through / came in through this link (reports; saved with the link).</summary>
    public long TotalLeft, TotalCame, TotalPosted;
    /// <summary>What the region took of the pending immigrants (consumed by it, returned to the continent at the next tick).</summary>
    public int TakenPersons;
    public long TakenCoin, TakenMeasures;

    public EdgeTrade? ForNeighbour(int neighbour) { foreach (var e in Edges) if (e.Neighbour == neighbour) return e; return null; }
    public RegionLink Clone() { var c = (RegionLink)MemberwiseClone(); c.Edges = Edges.Select(e => e.Clone()).ToList(); return c; }
}
