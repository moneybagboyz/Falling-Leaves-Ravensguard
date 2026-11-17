using System.Diagnostics;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;

namespace Ravensguard.WorldTool;

/// <summary>dotnet run --project tools/Ravensguard.WorldTool -- history --seed 1234 --years 250 --out art/out/worldgen</summary>
public static class HistoryCommand
{
    public static int Run(string[] args)
    {
        var baseDir = BaseContent.Require();
        var cs = ContinentSettings.LoadDefaults(baseDir);
        var hs = HistorySettings.LoadDefaults(baseDir);
        string outDir = "art/out/worldgen", layout = "Continents";
        int scale = 3;
        for (int i = 1; i + 1 < args.Length; i += 2)
        {
            string v = args[i + 1];
            switch (args[i])
            {
                case "--seed": cs.Seed = hs.Seed = ulong.Parse(v); break;
                case "--years": hs.Years = int.Parse(v); break;
                case "--out": outDir = v; break;
                case "--layout": layout = v; break;
                case "--scale": scale = int.Parse(v); break;
                default: Console.Error.WriteLine($"unknown option {args[i]}"); return 1;
            }
        }
        cs.Layout = Enum.Parse<ContinentLayout>(layout, true);
        var sw = Stopwatch.StartNew();
        var continent = ContinentGenerator.Generate(cs);
        long contMs = sw.ElapsedMilliseconds;
        sw.Restart();
        var h = HistoryGenerator.Generate(continent, hs);
        long histMs = sw.ElapsedMilliseconds;

        Directory.CreateDirectory(outDir);
        var lines = new List<string>
        {
            $"# Legends of the world (seed {hs.Seed}, {hs.Years} years, {h.Chronicle.Count} events)",
        };
        lines.AddRange(h.Legends());
        File.WriteAllLines(Path.Combine(outDir, "legends.txt"), lines);
        HistoryMaps.Write(continent, h, Path.Combine(outDir, "history.png"), Path.Combine(outDir, "history_timeline.png"), scale);

        Console.WriteLine($"history seed {hs.Seed}, {hs.Years} years: continent {contMs} ms, history {histMs} ms, {h.Chronicle.Count} events, hash {h.ComputeHash()[..16]}");
        Console.WriteLine("  events by type: " + string.Join(", ", h.Chronicle.All.GroupBy(e => e.TypeId).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key}={g.Count()}")));
        int initial = continent.Realms.Count;
        Console.WriteLine($"  realms: {initial} at start, {h.Realms.Count(r => r.Alive)} alive at end, {h.Realms.Count} ever (ended: {string.Join(", ", h.Realms.Where(r => !r.Alive).GroupBy(r => r.EndKind).Select(g => $"{g.Key}={g.Count()}"))})");
        Console.WriteLine($"  population: start {h.StartPopulation:N0}, end {h.EndPopulation:N0} ({h.EndPopulation * 100 / Math.Max(1, h.StartPopulation)}%)");
        Console.WriteLine($"  settlements {h.Settlements.Count} ({h.Settlements.Count(s => !s.IsRuin)} alive, {h.Settlements.Count(s => s.IsRuin)} ruins: {string.Join(", ", h.Settlements.Where(s => s.IsRuin).GroupBy(s => s.RuinKind).Select(g => $"{g.Key}={g.Count()}"))}), routes {h.TradeRoutes.Count(r => !r.Abandoned)}");
        Console.WriteLine($"  wars {h.Wars.Count}, battles {h.Battlefields.Count}, notables {h.Notables.Count}, dynasties {h.Dynasties.Count}, grudges {h.Grudges.Count(g => g.Strength > 0)}");
        Console.WriteLine($"  artefacts {h.Artefacts.Count}: held {h.Artefacts.Count(a => a.Status == ArtefactStatus.Held)}, lost {h.Artefacts.Count(a => a.Status == ArtefactStatus.Lost)}, buried {h.Artefacts.Count(a => a.Status == ArtefactStatus.Buried)}; cause coverage {h.Chronicle.CauseCoverage():P0}");
        Console.WriteLine($"  -> {Path.Combine(outDir, "legends.txt")}, history.png, history_timeline.png");
        return 0;
    }
}
