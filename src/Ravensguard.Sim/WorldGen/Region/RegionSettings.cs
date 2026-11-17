using Ravensguard.Content;

namespace Ravensguard.Sim.WorldGen.Region;

/// <summary>Stage 3 settings (plan §1A). Plain POCO; defaults come from content/base/worldgen/region.jsonc.</summary>
public sealed class RegionSettings
{
    public const int Tiles = 500;
    public const int TileM = 64;
    public const int RegionM = Tiles * TileM; // 32,000 m
    public const int CellM = 2000;

    public ulong Seed { get; set; } = 1;
    public int ReliefM { get; set; } = 2400;
    public int DetailBaseCm { get; set; } = 2500;
    public int DetailRelPermille { get; set; } = 100;
    public int DetailWavelengthM { get; set; } = 1600;
    public int DetailOctaves { get; set; } = 6;
    public int LakeDepressCm { get; set; } = 700;
    public int StreamCatchmentTenthsKm2 { get; set; } = 20;
    public int RiverCatchmentKm2 { get; set; } = 50;
    public int LakeMinDepthCm { get; set; } = 60;
    public int LakeMinCoreDepthCm { get; set; } = 200;
    public int LakeMinTiles { get; set; } = 8;
    public int MinRegionPopulation { get; set; } = 20000;
    public int MaxRegionPopulation { get; set; } = 30000;
    public int RuralScalePct { get; set; } = 100;
    public int TownMinPop { get; set; } = 500;
    public int TownHouseholdSizeX10 { get; set; } = 42;
    public int VillageHouseholdSizeX10 { get; set; } = 53;
    public int FarmsteadPop { get; set; } = 6;
    public int VillageMin { get; set; } = 40;
    public int VillageMax { get; set; } = 80;
    public int VillagePopMin { get; set; } = 80;
    public int VillagePopMax { get; set; } = 320;
    public int VillageSpacingMinM { get; set; } = 3000;
    public int VillageSpacingMaxM { get; set; } = 4000;
    public int TownClearanceM { get; set; } = 2200;
    public int FarmsteadMin { get; set; } = 150;
    public int FarmsteadMax { get; set; } = 300;
    public int FarmsteadSpacingM { get; set; } = 900;
    public int AcresPerPersonX10 { get; set; } = 23;
    public int MinFieldFertility { get; set; } = 28;
    public int MaxFieldSlopePermille { get; set; } = 100;
    public int ParcelMinTiles { get; set; } = 3;
    public int ParcelMaxTiles { get; set; } = 12;
    public int DemesnePct { get; set; } = 22;
    public int GlebePct { get; set; } = 7;
    public int FiefMaxVillages { get; set; } = 3;
    public int CaveMin { get; set; } = 10;
    public int CaveMax { get; set; } = 20;
    public int BarrowMin { get; set; } = 3;
    public int BarrowMax { get; set; } = 8;
    public int MineMin { get; set; } = 2;
    public int MineMax { get; set; } = 5;
    public int LairMin { get; set; } = 1;
    public int LairMax { get; set; } = 3;
    public int WarSafeKm { get; set; } = 10;
    public int LairSafeKm { get; set; } = 3;

    public static RegionSettings FromDef(RegionSettingsDef d) => new()
    {
        ReliefM = d.ReliefM, DetailBaseCm = d.DetailBaseCm, DetailRelPermille = d.DetailRelPermille, DetailWavelengthM = d.DetailWavelengthM,
        DetailOctaves = d.DetailOctaves, LakeDepressCm = d.LakeDepressCm, StreamCatchmentTenthsKm2 = d.StreamCatchmentTenthsKm2,
        RiverCatchmentKm2 = d.RiverCatchmentKm2, LakeMinDepthCm = d.LakeMinDepthCm, LakeMinCoreDepthCm = d.LakeMinCoreDepthCm,
        LakeMinTiles = d.LakeMinTiles, MinRegionPopulation = d.MinRegionPopulation, MaxRegionPopulation = d.MaxRegionPopulation,
        RuralScalePct = d.RuralScalePct, TownMinPop = d.TownMinPop, TownHouseholdSizeX10 = d.TownHouseholdSizeX10,
        VillageHouseholdSizeX10 = d.VillageHouseholdSizeX10, FarmsteadPop = d.FarmsteadPop, VillageMin = d.VillageMin,
        VillageMax = d.VillageMax, VillagePopMin = d.VillagePopMin, VillagePopMax = d.VillagePopMax,
        VillageSpacingMinM = d.VillageSpacingMinM, VillageSpacingMaxM = d.VillageSpacingMaxM, TownClearanceM = d.TownClearanceM,
        FarmsteadMin = d.FarmsteadMin, FarmsteadMax = d.FarmsteadMax, FarmsteadSpacingM = d.FarmsteadSpacingM,
        AcresPerPersonX10 = d.AcresPerPersonX10, MinFieldFertility = d.MinFieldFertility, MaxFieldSlopePermille = d.MaxFieldSlopePermille,
        ParcelMinTiles = d.ParcelMinTiles, ParcelMaxTiles = d.ParcelMaxTiles, DemesnePct = d.DemesnePct, GlebePct = d.GlebePct,
        FiefMaxVillages = d.FiefMaxVillages, CaveMin = d.CaveMin, CaveMax = d.CaveMax, BarrowMin = d.BarrowMin, BarrowMax = d.BarrowMax,
        MineMin = d.MineMin, MineMax = d.MineMax, LairMin = d.LairMin, LairMax = d.LairMax, WarSafeKm = d.WarSafeKm, LairSafeKm = d.LairSafeKm,
    };

    /// <summary>Loads defaults through the content pipeline (e.g. content/base); built-in defaults if absent.</summary>
    public static RegionSettings LoadDefaults(string? baseModFolder)
    {
        if (baseModFolder != null && Directory.Exists(baseModFolder))
        {
            var r = new ModLoader(ContentTypeRegistry.Default).Load([baseModFolder]);
            if (r.Database.TryGet<RegionSettingsDef>("region_defaults", out var def)) return FromDef(def);
        }
        return new RegionSettings();
    }
}
