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
        var wset = WeatherSettings.LoadDefaults(baseDir);
        foreach (var seed in seeds)
        {
            var cs = ContinentSettings.LoadDefaults(baseDir); cs.Seed = seed;
            var hs = HistorySettings.LoadDefaults(baseDir); hs.Seed = seed; // every layer takes the world seed, as the game and the tests do
            var rs = RegionSettings.LoadDefaults(baseDir); rs.Seed = seed;
            var continent = ContinentGenerator.Generate(cs);
            Console.WriteLine($"seed {seed} continent {continent.ComputeHash()}");
            var history = HistoryGenerator.Generate(continent, hs);
            Console.WriteLine($"seed {seed} history {history.ComputeHash()}");
            var pick = EmbarkSite.AutoPick(continent, history);
            Console.WriteLine($"seed {seed} embark {pick.CellX},{pick.CellY} score {pick.Total}");
            var region = RegionGenerator.Generate(continent, history, pick.CellX, pick.CellY, rs);
            Console.WriteLine($"seed {seed} region {region.ComputeHash()}");
            var marks = LandmarkPlacer.Place(region, history, db);
            region.Landmarks = marks;
            Console.WriteLine($"seed {seed} landmarks {marks.Count} {LandmarkPlacer.ComputeHash(marks)}");
            var weather = RegionWeather.ForContinent(continent, seed, wset);
            Console.WriteLine($"seed {seed} weather {weather.StandardSampleHash(pick.CellX, pick.CellY)}");
            Console.WriteLine($"seed {seed} codex {CodexBook.Build(history, region).ComputeHash()}");
            var pop = Populator.Populate(region, history, db);
            Console.WriteLine($"seed {seed} people {pop.ComputeHash()}");
            // a few chunks: the largest town's centre and three fixed spots across the region
            var gen = ChunkGenerator.For(region, pop, db);
            var town = region.Towns.OrderByDescending(t => t.TargetPop).FirstOrDefault();
            var spots = new List<(int X, int Y)> { (100, 100), (500, 500), (900, 300) };
            if (town != null) spots.Insert(0, (town.CenterX / ChunkGenerator.Size, town.CenterY / ChunkGenerator.Size));
            foreach (var (x, y) in spots)
                Console.WriteLine($"seed {seed} chunk {x},{y} {gen.GenerateLevels(x, y).ContentHash}");
        }
        return 0;
    }

    static string? Opt(string[] a, string name)
    {
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return null;
    }
}
