namespace Ravensguard.Content;

/// <summary>
/// Tunable defaults for Stage 3 world generation, the embark region (plan §1, §1A, §4A.4). All integers:
/// metres, centimetres, percent, permille or tenths as the name says.
/// </summary>
[ContentType("worldgen_region")]
public sealed record RegionSettingsDef : ContentDef
{
    public int ReliefM { get; init; } = 2400;
    public int DetailBaseCm { get; init; } = 2500;
    public int DetailRelPermille { get; init; } = 100;
    public int DetailWavelengthM { get; init; } = 1600;
    public int DetailOctaves { get; init; } = 6;
    public int LakeDepressCm { get; init; } = 700;
    public int StreamCatchmentTenthsKm2 { get; init; } = 20;
    public int RiverCatchmentKm2 { get; init; } = 50;
    public int LakeMinDepthCm { get; init; } = 60;
    public int LakeMinCoreDepthCm { get; init; } = 200;
    public int LakeMinTiles { get; init; } = 8;
    public int MinRegionPopulation { get; init; } = 20000;
    public int MaxRegionPopulation { get; init; } = 30000;
    public int RuralScalePct { get; init; } = 100;
    public int TownMinPop { get; init; } = 500;
    public int TownHouseholdSizeX10 { get; init; } = 42;
    public int VillageHouseholdSizeX10 { get; init; } = 53;
    public int FarmsteadPop { get; init; } = 6;
    public int VillageMin { get; init; } = 40;
    public int VillageMax { get; init; } = 80;
    public int VillagePopMin { get; init; } = 80;
    public int VillagePopMax { get; init; } = 320;
    public int VillageSpacingMinM { get; init; } = 3000;
    public int VillageSpacingMaxM { get; init; } = 4000;
    public int TownClearanceM { get; init; } = 2200;
    public int FarmsteadMin { get; init; } = 150;
    public int FarmsteadMax { get; init; } = 300;
    public int FarmsteadSpacingM { get; init; } = 900;
    public int AcresPerPersonX10 { get; init; } = 23;
    public int MinFieldFertility { get; init; } = 28;
    public int MaxFieldSlopePermille { get; init; } = 100;
    public int ParcelMinTiles { get; init; } = 3;
    public int ParcelMaxTiles { get; init; } = 12;
    public int DemesnePct { get; init; } = 22;
    public int GlebePct { get; init; } = 7;
    public int FiefMaxVillages { get; init; } = 3;
    public int CaveMin { get; init; } = 10;
    public int CaveMax { get; init; } = 20;
    public int BarrowMin { get; init; } = 3;
    public int BarrowMax { get; init; } = 8;
    public int MineMin { get; init; } = 2;
    public int MineMax { get; init; } = 5;
    public int LairMin { get; init; } = 1;
    public int LairMax { get; init; } = 3;
    public int WarSafeKm { get; init; } = 10;
    public int LairSafeKm { get; init; } = 3;
    /// <summary>Landmark placer version a world made from these defaults uses (P8-LM-1): 2 is placer v2; 0 is v1.</summary>
    public int LandmarkPlacer { get; init; }
}
