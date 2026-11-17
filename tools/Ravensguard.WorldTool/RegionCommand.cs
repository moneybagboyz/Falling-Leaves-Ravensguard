using System.Diagnostics;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.WorldTool;

/// <summary>dotnet run --project tools/Ravensguard.WorldTool -- region --seed 1234 --out art/out/worldgen [--cell X,Y]</summary>
public static class RegionCommand
{
    public static int Run(string[] args)
    {
        var baseDir = BaseContent.Require();
        var cs = ContinentSettings.LoadDefaults(baseDir);
        var hs = HistorySettings.LoadDefaults(baseDir);
        var rs = RegionSettings.LoadDefaults(baseDir);
        string outDir = "art/out/worldgen";
        bool people = args.Contains("--people"); // flag without a value: strip it before the pairwise option parse
        if (people) args = args.Where(a => a != "--people").ToArray();
        int scale = 2;
        int? cellX = null, cellY = null;
        for (int i = 1; i + 1 < args.Length; i += 2)
        {
            string v = args[i + 1];
            switch (args[i])
            {
                case "--seed": cs.Seed = hs.Seed = rs.Seed = ulong.Parse(v); break;
                case "--out": outDir = v; break;
                case "--scale": scale = int.Parse(v); break;
                case "--cell": { var p = v.Split(','); cellX = int.Parse(p[0]); cellY = int.Parse(p[1]); break; }
                default: Console.Error.WriteLine($"unknown option {args[i]}"); return 1;
            }
        }
        var sw = Stopwatch.StartNew();
        var continent = ContinentGenerator.Generate(cs);
        var history = HistoryGenerator.Generate(continent, hs);
        long preMs = sw.ElapsedMilliseconds;
        sw.Restart();
        EmbarkScore pick = cellX is int px && cellY is int py ? EmbarkSite.Score(continent, history, px, py) : EmbarkSite.AutoPick(continent, history);
        long pickMs = sw.ElapsedMilliseconds;
        sw.Restart();
        var region = RegionGenerator.Generate(continent, history, pick.CellX, pick.CellY, rs);
        long genMs = sw.ElapsedMilliseconds;

        Directory.CreateDirectory(outDir);
        RegionMaps.Write(region, history, Path.Combine(outDir, "region.png"), Path.Combine(outDir, "region_town.png"), scale);

        var towns = region.Towns.ToList();
        Console.WriteLine($"region seed {rs.Seed} cell ({pick.CellX},{pick.CellY}) score {pick.Total} [{pick.Towns} towns, {pick.RiverCells} river cells, {pick.BiomeCount} biomes, war {pick.NearestWarKm} km]: continent+history {preMs} ms, pick {pickMs} ms, region {genMs} ms");
        Console.WriteLine($"  hash {region.ComputeHash()[..16]}  towns {towns.Count} villages {region.Villages.Count()} farmsteads {region.Farmsteads.Count()}  pop {region.TotalPopulation:N0} households {region.TotalHouseholds:N0}");
        Console.WriteLine($"  fields {region.FieldAcres:N0} acres ({region.FieldAcres * 10.0 / Math.Max(1, region.TotalPopulation):0.0} per 10 people), parcels {region.Parcels.Count}, fiefs {region.Fiefs.Count}, realms {region.Realms.Count}");
        Console.WriteLine($"  waterways {region.Waterways.Count} (rivers {region.Waterways.Count(w => w.Kind == WaterKind.River)}), lakes {region.Lakes.Count}, road nodes {region.RoadNodes.Count} edges {region.RoadEdges.Count}: " +
                          string.Join(' ', region.RoadEdges.GroupBy(e => e.Class).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}")) +
                          $", crossings {region.RoadEdges.Sum(e => e.Crossings.Count)} (bridges {region.RoadEdges.Sum(e => e.Crossings.Count(c => c.Kind == CrossingKind.Bridge))})");
        Console.WriteLine($"  sites: {string.Join(' ', region.Sites.GroupBy(s => s.Kind).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}"))}, lairs {region.Sites.Count(s => s.MonsterLair)}, active wars {region.ActiveWars.Count}");
        foreach (var t in towns)
            Console.WriteLine($"  town {t.Name}: pop {t.TargetPop}, {t.Plan.Lots.Count} lots, fields {t.FieldAcres}/{t.FieldAcresDemand} acres");
        var hw = region.RoadEdges.Where(e => e.Class == RoadClass.Highway && region.RoadNodes[e.A].Kind == RoadNodeKind.Town && region.RoadNodes[e.B].Kind == RoadNodeKind.Town).ToList();
        Console.WriteLine("  town-to-town highways (km): " + string.Join(", ", hw.Select(e => $"{e.LengthM / 1000.0:0.0}")));
        Console.WriteLine($"  -> {Path.Combine(outDir, "region.png")}, region_town.png");
        if (people) PeopleReport.Run(region, history, baseDir);
        return 0;
    }
}
