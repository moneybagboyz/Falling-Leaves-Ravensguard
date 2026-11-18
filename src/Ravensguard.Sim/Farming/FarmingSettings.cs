using Ravensguard.Content;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Farming;

/// <summary>Runtime crop row: integer constants for one crop (farming spec 2.4).</summary>
public sealed class CropInfo
{
    public required Crop Crop { get; init; }
    public required string GoodId { get; init; }
    public required CropKind Kind { get; init; }
    public required int BaseYieldMeasures { get; init; }
    public required int SeedPct { get; init; }
    public required int FertilityDelta { get; init; }
    public required int SowFirstDoy { get; init; }
    public required int SowLastDoy { get; init; }
    public required int RipeDoy { get; init; }
    public required int ReapGraceDays { get; init; }
    public required int MinFertility { get; init; }
    public ushort Good { get; set; }
    public long BaseG(int gramsPerMeasure) => (long)BaseYieldMeasures * gramsPerMeasure;
    public long SeedG(int gramsPerMeasure) => BaseG(gramsPerMeasure) * SeedPct / 100;
}

/// <summary>Farming tunables and crop table, from content (<see cref="FarmingSettingsDef"/>, <see cref="CropDef"/>) or built-in defaults identical to the shipped content.</summary>
public sealed class FarmingSettings
{
    public FarmingSettingsDef Def { get; }
    private readonly CropInfo[] _crops = new CropInfo[Enum.GetValues<Crop>().Length];

    public FarmingSettings(FarmingSettingsDef? def = null, IEnumerable<CropDef>? crops = null)
    {
        Def = def ?? new FarmingSettingsDef { Id = "farming" };
        foreach (var c in crops ?? DefaultCrops())
        {
            if (!Enum.TryParse<Crop>(c.Id, true, out var crop) || crop == Crop.Fallow) throw new InvalidDataException($"crop '{c.Id}' is not a known crop");
            var kind = c.Kind switch { "winter_grain" => CropKind.WinterGrain, "spring_grain" => CropKind.SpringGrain, "spring_legume" => CropKind.SpringLegume, _ => throw new InvalidDataException($"crop '{c.Id}': unknown kind '{c.Kind}'") };
            if (c.SowDoy.Count != 2) throw new InvalidDataException($"crop '{c.Id}': sow_doy needs two days");
            _crops[(int)crop] = new CropInfo
            {
                Crop = crop, GoodId = c.Good, Kind = kind, BaseYieldMeasures = c.BaseYieldMeasures, SeedPct = c.SeedPct, FertilityDelta = c.FertilityDelta,
                SowFirstDoy = c.SowDoy[0], SowLastDoy = c.SowDoy[1], RipeDoy = c.RipeDoy, ReapGraceDays = c.ReapGraceDays, MinFertility = c.MinFertility,
            };
        }
    }

    public static FarmingSettings FromContent(ContentDatabase db)
        => new(db.All<FarmingSettingsDef>().OrderBy(d => d.Id, StringComparer.Ordinal).FirstOrDefault(), db.All<CropDef>().OrderBy(d => d.Id, StringComparer.Ordinal).ToList() is { Count: > 0 } l ? l : null);

    public CropInfo Info(Crop c) => _crops[(int)c] ?? throw new KeyNotFoundException($"crop {c} has no definition");
    public bool Has(Crop c) => _crops[(int)c] != null;
    public int GramsPerMeasure => Def.GramsPerMeasure;

    /// <summary>Resolves each crop's stock good id against a catalog (call once after the goods table exists).</summary>
    public void BindGoods(Goods.GoodCatalog cat) { foreach (var c in _crops) if (c != null) c.Good = cat.IdOf(c.GoodId); }

    public static readonly Crop[] WinterCrops = [Crop.Wheat, Crop.Rye];
    public static readonly Crop[] SpringCrops = [Crop.Barley, Crop.Oats, Crop.Peas, Crop.Beans];

    private static IEnumerable<CropDef> DefaultCrops() =>
    [
        new() { Id = "wheat", Kind = "winter_grain", Good = "wheat", BaseYieldMeasures = 52, FertilityDelta = -10, SowDoy = [36, 45], RipeDoy = 26, MinFertility = 40 },
        new() { Id = "rye", Kind = "winter_grain", Good = "rye", BaseYieldMeasures = 50, FertilityDelta = -8, SowDoy = [36, 45], RipeDoy = 26, MinFertility = 30 },
        new() { Id = "barley", Kind = "spring_grain", Good = "barley", BaseYieldMeasures = 56, FertilityDelta = -6, SowDoy = [3, 14], RipeDoy = 31, MinFertility = 35 },
        new() { Id = "oats", Kind = "spring_grain", Good = "oats", BaseYieldMeasures = 50, FertilityDelta = -6, SowDoy = [3, 14], RipeDoy = 31, MinFertility = 30 },
        new() { Id = "peas", Kind = "spring_legume", Good = "peas", BaseYieldMeasures = 42, FertilityDelta = 5, SowDoy = [3, 14], RipeDoy = 31, MinFertility = 25 },
        new() { Id = "beans", Kind = "spring_legume", Good = "beans", BaseYieldMeasures = 42, FertilityDelta = 5, SowDoy = [3, 14], RipeDoy = 31, MinFertility = 30 },
    ];
}
