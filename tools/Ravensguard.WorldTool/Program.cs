using System.Diagnostics;
using Ravensguard.Sim.WorldGen;
using Ravensguard.WorldTool;

// dotnet run --project tools/Ravensguard.WorldTool -- continent --seed 1234 --layout Continents --out art/out/worldgen
if (args.Length > 0 && args[0] == "history") return HistoryCommand.Run(args);
if (args.Length == 0 || args[0] != "continent")
{
    Console.WriteLine("usage: history [--seed N] [--years N] [--layout L] [--out DIR] [--scale N]\n       continent [--seed N] [--layout Pangea|Continents|Archipelago|All] [--out DIR] [--plates N] [--cultures N] [--realms N]\n" +
                      "       [--minerals PCT] [--savagery PCT] [--moisture PCT] [--temperature TENTHS] [--scale N]");
    return 1;
}

var settings = ContinentSettings.LoadDefaults(ContinentSettings.FindBaseContent());
string outDir = "art/out/worldgen";
string layoutArg = "Continents";
int scale = 3;
for (int i = 1; i + 1 < args.Length; i += 2)
{
    string v = args[i + 1];
    switch (args[i])
    {
        case "--seed": settings.Seed = ulong.Parse(v); break;
        case "--layout": layoutArg = v; break;
        case "--out": outDir = v; break;
        case "--plates": settings.PlateCount = int.Parse(v); break;
        case "--cultures": settings.CultureCount = int.Parse(v); break;
        case "--realms": settings.RealmCount = int.Parse(v); break;
        case "--minerals": settings.MineralDensityPct = int.Parse(v); break;
        case "--savagery": settings.SavageryPct = int.Parse(v); break;
        case "--moisture": settings.MoisturePct = int.Parse(v); break;
        case "--temperature": settings.TemperatureOffsetTenths = int.Parse(v); break;
        case "--scale": scale = int.Parse(v); break;
        default: Console.Error.WriteLine($"unknown option {args[i]}"); return 1;
    }
}

var layouts = layoutArg.Equals("All", StringComparison.OrdinalIgnoreCase)
    ? Enum.GetValues<ContinentLayout>()
    : [Enum.Parse<ContinentLayout>(layoutArg, true)];

foreach (var layout in layouts)
{
    settings.Layout = layout;
    var sw = Stopwatch.StartNew();
    var c = ContinentGenerator.Generate(settings);
    sw.Stop();
    string prefix = Path.Combine(outDir, $"{layout.ToString().ToLowerInvariant()}_{settings.Seed}");
    Maps.WriteAll(c, prefix, scale);
    var veins = c.Veins.GroupBy(v => v.Type).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}");
    Console.WriteLine($"{layout} seed {settings.Seed}: {sw.ElapsedMilliseconds} ms, land {c.LandFraction():P0}, rivers {c.Rivers.Count}, " +
                      $"lakes {c.Lake.Count(x => x)} cells, cultures {c.Cultures.Count}, realms {c.Realms.Count}, veins {c.Veins.Count} ({string.Join(' ', veins)})");
    int land = 0; var bc = new int[16];
    for (int i = 0; i < Continent.Cells; i++) if (!c.Ocean[i]) { land++; bc[(int)c.Biomes[i]]++; }
    Console.WriteLine("  biomes % of land: " + string.Join(' ', Enum.GetValues<Biome>().Where(b => b != Biome.Sea).Select(b => $"{b}={bc[(int)b] * 100 / Math.Max(1, land)}")));
    Console.WriteLine($"  hash {c.ComputeHash()}  -> {prefix}_*.png");
}
return 0;
