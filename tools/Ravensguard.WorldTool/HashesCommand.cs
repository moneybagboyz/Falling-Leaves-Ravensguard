using Ravensguard.Content;
using Ravensguard.Sim.Codex;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.WorldTool;

/// <summary>
/// `hashes [--seeds 1,1234]`: prints one line per generated layer (continent, history, region, people, sample chunks)
/// for each seed. CI runs it on Linux and Windows and fails if the outputs differ (plan §2.2, §5 Phase 1 done-when:
/// "the same seed gives the same continent, history and region hashes on Windows and Linux").
/// </summary>
public static class HashesCommand
{
    public static int Run(string[] args)
    {
        var seeds = (Opt(args, "--seeds") ?? "1,1234").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(ulong.Parse).ToArray();
        var baseDir = BaseContent.Require();
        var db = new ModLoader(ContentTypeRegistry.Default).Load([baseDir]).Database;
        foreach (var seed in seeds)
            foreach (var line in Lines(seed, baseDir, db)) Console.WriteLine(line);
        return 0;
    }

    /// <summary>The lines printed for one seed, in order: continent, history, embark pick, region, landmarks, weather, codex, people and four sample chunks. The golden-hash tests compare these.</summary>
    public static List<string> Lines(ulong seed, string baseDir, ContentDatabase db)
    {
        var lines = new List<string>();
        var wset = WeatherSettings.LoadDefaults(baseDir);
        var cs = ContinentSettings.LoadDefaults(baseDir); cs.Seed = seed;
        var hs = HistorySettings.LoadDefaults(baseDir); hs.Seed = seed; // every layer takes the world seed, as the game and the tests do
        var rs = RegionSettings.LoadDefaults(baseDir); rs.Seed = seed;
        var continent = ContinentGenerator.Generate(cs);
        lines.Add($"seed {seed} continent {continent.ComputeHash()}");
        var history = HistoryGenerator.Generate(continent, hs);
        lines.Add($"seed {seed} history {history.ComputeHash()}");
        var pick = EmbarkSite.AutoPick(continent, history);
        lines.Add($"seed {seed} embark {pick.CellX},{pick.CellY} score {pick.Total}");
        var region = RegionGenerator.Generate(continent, history, pick.CellX, pick.CellY, rs);
        lines.Add($"seed {seed} region {region.ComputeHash()}");
        var marks = LandmarkPlacer.Place(region, history, db);
        region.Landmarks = marks;
        lines.Add($"seed {seed} landmarks {marks.Count} {LandmarkPlacer.ComputeHash(marks)}");
        var weather = RegionWeather.ForContinent(continent, seed, wset);
        lines.Add($"seed {seed} weather {weather.StandardSampleHash(pick.CellX, pick.CellY)}");
        lines.Add($"seed {seed} codex {CodexBook.Build(history, region).ComputeHash()}");
        var pop = Populator.Populate(region, history, db);
        lines.Add($"seed {seed} people {pop.ComputeHash()}");
        // a few chunks: the largest town's centre and three fixed spots across the region
        var gen = ChunkGenerator.For(region, pop, db);
        var town = region.Towns.OrderByDescending(t => t.TargetPop).FirstOrDefault();
        var spots = new List<(int X, int Y)> { (100, 100), (500, 500), (900, 300) };
        if (town != null) spots.Insert(0, (town.CenterX / ChunkGenerator.Size, town.CenterY / ChunkGenerator.Size));
        foreach (var (x, y) in spots)
            lines.Add($"seed {seed} chunk {x},{y} {gen.GenerateLevels(x, y).ContentHash}");
        return lines;
    }

    static string? Opt(string[] a, string name)
    {
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return null;
    }
}
