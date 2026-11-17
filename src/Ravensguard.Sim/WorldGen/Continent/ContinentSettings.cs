using Ravensguard.Content;

namespace Ravensguard.Sim.WorldGen;

public enum ContinentLayout { Pangea, Continents, Archipelago }

/// <summary>Stage 1 generation settings (plan §1A "Settings"). Plain POCO; defaults come from content/base/worldgen/settings.jsonc.</summary>
public sealed class ContinentSettings
{
    /// <summary>Continent grid is always 256 x 256 cells of 2 km (plan §1).</summary>
    public const int Size = 256;

    public ulong Seed { get; set; } = 1;
    public ContinentLayout Layout { get; set; } = ContinentLayout.Continents;
    public int PlateCount { get; set; } = 13;
    /// <summary>Q16.16 raw of the single sea level shared by all phases.</summary>
    public int SeaLevelRaw { get; set; } = 22938;
    public int MineralDensityPct { get; set; } = 100;
    public int SavageryPct { get; set; } = 100;
    public int MoisturePct { get; set; } = 100;
    public int TemperatureOffsetTenths { get; set; }
    public int CultureCount { get; set; } = 5;
    public int RealmCount { get; set; } = 8;
    public int LandPctPangea { get; set; } = 47;
    public int LandPctContinents { get; set; } = 38;
    public int LandPctArchipelago { get; set; } = 24;
    public int RiverFlowThreshold { get; set; } = 220;
    public int StreamFlowThreshold { get; set; } = 70;
    public List<string> CultureNames { get; set; } = ["Aldmark", "Brennic", "Coravel", "Dunhold", "Eskarri", "Fennish", "Garrow", "Havren"];
    public List<string> RealmNames { get; set; } = ["Ashford", "Blackmere", "Caldor", "Dunmoor", "Eastwatch", "Fairholt", "Greywater", "Harrowgate"];

    public int TargetLandPct => Layout switch
    {
        ContinentLayout.Pangea => LandPctPangea,
        ContinentLayout.Archipelago => LandPctArchipelago,
        _ => LandPctContinents,
    };

    public static ContinentSettings FromDef(WorldGenSettingsDef d) => new()
    {
        PlateCount = d.PlateCount, SeaLevelRaw = d.SeaLevelRaw, MineralDensityPct = d.MineralDensityPct,
        SavageryPct = d.SavageryPct, MoisturePct = d.MoisturePct, TemperatureOffsetTenths = d.TemperatureOffsetTenths,
        CultureCount = d.CultureCount, RealmCount = d.RealmCount,
        LandPctPangea = d.LandPctPangea, LandPctContinents = d.LandPctContinents, LandPctArchipelago = d.LandPctArchipelago,
        RiverFlowThreshold = d.RiverFlowThreshold, StreamFlowThreshold = d.StreamFlowThreshold,
        CultureNames = d.CultureNames.Count > 0 ? [.. d.CultureNames] : new ContinentSettings().CultureNames,
        RealmNames = d.RealmNames.Count > 0 ? [.. d.RealmNames] : new ContinentSettings().RealmNames,
    };

    /// <summary>Loads defaults from a content folder (e.g. content/base) through the content pipeline; falls back to built-ins if absent.</summary>
    public static ContinentSettings LoadDefaults(string? baseModFolder)
    {
        if (baseModFolder != null && Directory.Exists(baseModFolder))
        {
            var r = new ModLoader(ContentTypeRegistry.Default).Load([baseModFolder]);
            if (r.Database.TryGet<WorldGenSettingsDef>("continent_defaults", out var def)) return FromDef(def);
        }
        return new ContinentSettings();
    }

    /// <summary>Walks up from the working directory / app base to find the repo's content/base folder.</summary>
    public static string? FindBaseContent()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var d = new DirectoryInfo(start);
            while (d != null)
            {
                var p = Path.Combine(d.FullName, "content", "base");
                if (File.Exists(Path.Combine(p, "mod.json"))) return p;
                d = d.Parent;
            }
        }
        return null;
    }
}
