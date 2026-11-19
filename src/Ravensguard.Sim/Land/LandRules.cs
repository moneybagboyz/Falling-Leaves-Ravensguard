using Ravensguard.Content;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Land;

/// <summary>What a person may want to do on a cell (land spec 4.1).</summary>
public enum LandAction : byte { Build, BuildFarm, Demolish, Farm, Store }

/// <summary>
/// The answer of <see cref="LandRegistry.May"/>: allowed or not, whose the ground is (a refusal names the holder: "This is Hedda Tanner's yard") and a key for why (en.csv <c>ld_may_*</c>).
/// </summary>
public readonly record struct MayResult(bool Allowed, string HolderName, Ravensguard.Sim.Core.EntityId Deed, string Why)
{
    public static MayResult Yes(Ravensguard.Sim.Core.EntityId d) => new(true, "", d, "");
}

/// <summary>The pure numbers of land (spec 3.3, 4.2, 4.4, 4.5): integer mites, permille and metres only, no draws.</summary>
public static class LandRules
{
    public static LandRulesDef From(ContentDatabase? db) => db?.All<LandRulesDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault() ?? new LandRulesDef { Id = "default" };

    /// <summary>Price of a plot in mites by the square metre, by the settlement's class.</summary>
    public static int PricePerM2(LandRulesDef r, SettlementClass c) => c switch
    {
        SettlementClass.Town => r.PlotPriceTownMitesPerM2, SettlementClass.Village => r.PlotPriceVillageMitesPerM2, _ => r.PlotPriceHamletMitesPerM2,
    };

    /// <summary>The demand factor (permille): 1000 plus <c>DemandPmPerCrowdedPct</c> for each crowded household in a hundred, clamped (spec 4.2).</summary>
    public static int DemandPm(LandRulesDef r, int crowdedPct) => Math.Clamp(1000 + r.DemandPmPerCrowdedPct * crowdedPct, r.DemandMinPm, r.DemandMaxPm);

    /// <summary>
    /// <c>price = area x price[class] x demand / 1000 + building value</c>, in mites (spec 4.2). An 11 x 28 m town plot with no crowding: 46,200; with 10% crowded: 48,510;
    /// a 16 x 38 m village plot: 24,320 (LD-3). Integer: the product is exact for whole square metres.
    /// </summary>
    public static long PlotPrice(LandRulesDef r, int areaM2, SettlementClass c, int demandPm, long buildingValueMites = 0)
        => (long)areaM2 * PricePerM2(r, c) * demandPm / 1000 + Math.Max(0, buildingValueMites);

    /// <summary>The share of a settlement's households that are crowded, percent (members above <c>CrowdedOverPct</c> of their beds; growth's measure).</summary>
    public static int CrowdedPct(LandRulesDef r, HouseholdTable hht, int settlement)
    {
        int all = 0, crowded = 0;
        for (int h = 0; h < hht.Count; h++)
        {
            if (hht.Settlement[h] != settlement || hht.MemberCount[h] == 0) continue;
            all++;
            if (hht.MemberCount[h] * 100 > hht.Beds[h] * r.CrowdedOverPct) crowded++;
        }
        return all == 0 ? 0 : crowded * 100 / all;
    }

    /// <summary>Square metres to whole acres (4,047 m2), at least one.</summary>
    public static int Acres(long areaM2) => (int)Math.Max(1, areaM2 / 4047);

    /// <summary>Quit-rent of a holding of <paramref name="acres"/> acres, mites a season.</summary>
    public static long QuitRent(LandRulesDef r, int acres) => (long)Math.Max(1, acres) * r.QuitRentMitesPerAcreSeason;

    /// <summary>Dues of a bought plot: a burgage rent in a town, else a quit-rent by its acres (at least a mite).</summary>
    public static long PlotDues(LandRulesDef r, SettlementClass c, int areaM2) => c == SettlementClass.Town ? r.BurgageRentMitesPerSeason : Math.Max(1, (long)areaM2 * r.QuitRentMitesPerAcreSeason / 4047);

    public static Tenure PlotTenure(SettlementClass c) => c == SettlementClass.Town ? Tenure.Burgage : Tenure.Freehold;

    /// <summary>The rent of a parcel leased for a season (a quarter of the field's yearly rent for each field).</summary>
    public static long FieldLeasePerSeason(LandRulesDef r, int fields) => (long)Math.Max(1, fields) * r.LeaseRentMitesPerFieldYear / 4;

    /// <summary>The rent of a leased house a season by its dwelling type.</summary>
    public static long HouseRent(LandRulesDef r, DwellingType d) => d switch
    {
        DwellingType.Burgage => r.HouseRentBurgageMitesPerSeason, DwellingType.Longhouse => r.HouseRentLonghouseMitesPerSeason, _ => r.HouseRentCottageMitesPerSeason,
    };

    /// <summary>The licence for a claim of <paramref name="acres"/> acres on a fief's waste, mites (10 crowns an acre: LD-11 pays 8,000 for 8 acres).</summary>
    public static long Licence(LandRulesDef r, int acres) => (long)acres * r.ClaimLicenceMitesPerAcre;

    /// <summary>
    /// The store a storage room gives its deed (spec 4.5): volume <c>area x height x fill</c> in litres and the floor's mass cap in grams. A 6 x 8 m barn: 48 m2 x 2 m x 0.5 = 48,000 L,
    /// and on the ground 48 x 2,000 kg = 96 t (LD-12).
    /// </summary>
    public static (long VolumeL, long MassCapG) StoreFor(LandRulesDef r, int areaM2, bool upperFloor)
    {
        long volumeL = (long)areaM2 * r.StoreHeightCm * 10 * r.StoreFillPm / 1000;          // m2 x cm x 10 = litres
        long cap = (long)areaM2 * (upperFloor ? r.StoreUpperKgPerM2 : r.StoreGroundKgPerM2) * 1000;
        return (volumeL, cap);
    }

    /// <summary>Region tile under a point in region metres, -1 off the region.</summary>
    public static int TileAt(int xM, int yM) => RegionData.InBounds(xM / RegionData.TileM, yM / RegionData.TileM) && xM >= 0 && yM >= 0 ? RegionData.Index(xM / RegionData.TileM, yM / RegionData.TileM) : -1;

    /// <summary>The fief whose area holds a tile (the region's fief map; -1 outside every fief, or when the map was released).</summary>
    public static int FiefOfTile(RegionData r, int t) => t >= 0 && r.FiefMap.Length > t ? r.FiefMap[t] - 1 : -1;

    /// <summary>Distance in metres from a point to the nearest settlement's plan (its centre less its plan radius; 0 inside a plan).</summary>
    public static long MetresFromPlans(RegionData r, int xM, int yM)
    {
        long best = long.MaxValue;
        foreach (var s in r.Settlements)
        {
            long d = RegionData.Isqrt(RegionData.Dist2(s.CenterX - xM, s.CenterY - yM)) - Math.Max(0, s.Plan.RadiusM);
            best = Math.Min(best, Math.Max(0, d));
        }
        return best;
    }

    /// <summary>
    /// Whose land a tile is for claims and charters (spec 4.4): within <c>ClaimMinMFromPlan</c> of a settlement's plan it is the waste of the fief the region's fief map gives (its index), or of no lord
    /// (-2: a chartered town's commons, not claimable); farther out it is wild land outside every lord's land (-1), free to claim and unprotected.
    /// </summary>
    public static int LandFief(RegionData r, LandRulesDef rules, int t)
    {
        if (t < 0) return -2;
        int N = RegionData.Size;
        if (MetresFromPlans(r, (t % N) * RegionData.TileM + RegionData.TileM / 2, (t / N) * RegionData.TileM + RegionData.TileM / 2) >= rules.ClaimMinMFromPlan) return -1;
        int f = FiefOfTile(r, t);
        return f >= 0 ? f : -2;
    }

    /// <summary>Open ground nobody farms or has built on: no parcel, no lot or square, no water, no road.</summary>
    public static bool OpenGround(RegionData r, int t) => t >= 0 && r.ParcelMap[t] == 0 && r.Built[t] == 0 && r.Water[t] == 0 && r.Road[t] == 0;
}
