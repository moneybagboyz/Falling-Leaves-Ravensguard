using System.Diagnostics;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;

namespace Ravensguard.WorldTool;

/// <summary>dotnet run --project tools/Ravensguard.WorldTool -c Release -- history-stats --seeds 1,2,3 --years 200 --rules 1 [--types]: the calibration measures of history-recalibration 4.4, one line a seed and the means.</summary>
public static class HistoryStatsCommand
{
    public static int Run(string[] args)
    {
        var seeds = new List<ulong> { 1 };
        int years = 200, rules = 1; bool types = false, notes = false; var noteSet = new SortedSet<string>(StringComparer.Ordinal);
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seeds": seeds = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries).SelectMany(Range).ToList(); break;
                case "--years": years = int.Parse(args[++i]); break;
                case "--rules": rules = int.Parse(args[++i]); break;
                case "--types": types = true; break;
                case "--notes": notes = true; break;
                default: Console.Error.WriteLine($"unknown option {args[i]}"); return 1;
            }
        }
        var baseDir = BaseContent.Require();
        var db = new ModLoader(ContentTypeRegistry.Default).Load([baseDir]).Database;
        var ct = ChronicleTypes.From(db);
        var all = new List<HistoryStats>(); double covSum = 0, covMin = 1; long msSum = 0; long msMax = 0;
        foreach (var seed in seeds)
        {
            var cs = ContinentSettings.LoadDefaults(baseDir); cs.Seed = seed;
            var hs = HistorySettings.LoadDefaults(baseDir); hs.Seed = seed; hs.Years = years;
            if (rules >= 1) hs.UseRulesV1(db);
            var c = ContinentGenerator.Generate(cs);
            var sw = Stopwatch.StartNew();
            var h = HistoryGenerator.Generate(c, hs);
            sw.Stop();
            var st = HistoryStats.Of(c, h);
            var cov = h.Chronicle.SpecificCoverage(ct);
            double cr = cov.Ratio;
            covSum += cr; covMin = Math.Min(covMin, cr); msSum += sw.ElapsedMilliseconds; msMax = Math.Max(msMax, sw.ElapsedMilliseconds);
            all.Add(st);
            if (notes) foreach (var e in h.Chronicle.All) { foreach (var cz in e.Causes) if (cz.NoteKey.Length > 0) noteSet.Add($"{e.TypeId}\t{cz.NoteKey}\t{cz.Kind}\t{(e.TextKey.Length > 0 ? "keyed" : "legacy")}"); if (e.TextKey.Length > 0) noteSet.Add($"{e.TypeId}\t-\t-\t{e.TextKey}"); }
            Console.WriteLine($"seed {seed}: {sw.ElapsedMilliseconds} ms, {h.Chronicle.Count} entries, coverage {cr:P1}, {st}");
            if (types)
            {
                var bad = h.Chronicle.All.Where(e => !h.Chronicle.SpecificCoverage(ct, x => x.Id == e.Id).Specific.Equals(1) && !e.Causes.Any(k => k.Kind == CauseKind.MarketDrift))
                    .GroupBy(e => e.TypeId).Select(g => (g.Key, g.Count())).OrderByDescending(g => g.Item2).Take(12);
                Console.WriteLine("   unspecific: " + string.Join(", ", bad.Select(b => $"{b.Key}={b.Item2}")));
            }
        }
        if (notes) foreach (var l in noteSet) Console.WriteLine("NOTE\t" + l);
        int n = Math.Max(1, all.Count);
        Console.WriteLine($"MEAN over {all.Count}: {msSum / n} ms (max {msMax}), coverage {covSum / n:P1} (min {covMin:P1}), wars/pair/cent {all.Sum(a => a.WarsPerPairCenturyMille) / n / 1000.0:F2}, war share {all.Sum(a => a.WarShareMille) / n / 10.0:F1}%, ever-warred {all.Sum(a => a.PairsEverWarredMille) / n / 10.0:F0}%, fell {all.Sum(a => a.RealmsFell * 1000 / Math.Max(1, a.StartRealms)) / n / 10.0:F0}%, plagues {all.Sum(a => a.Plagues) / (double)n:F2}, ruins/region {all.Sum(a => a.RuinsPer1000Regions) / n / 1000.0:F1}, saints/rel {all.Sum(a => a.Saints * 100 / a.Cultures) / n / 100.0:F1}, heresies {all.Sum(a => a.Heresies) / (double)n:F2}, schisms {all.Sum(a => a.Schisms) / (double)n:F2}, excomm {all.Sum(a => a.Excommunications) / (double)n:F2}, raids {all.Sum(a => a.SlaveRaids) / (double)n:F1}, rebellions {all.Sum(a => a.Rebellions) / (double)n:F1}, wars {all.Sum(a => a.Wars) / (double)n:F1}");
        return 0;
    }

    private static IEnumerable<ulong> Range(string s)
    {
        int d = s.IndexOf('-');
        if (d < 0) { yield return ulong.Parse(s); yield break; }
        ulong a = ulong.Parse(s[..d]), b = ulong.Parse(s[(d + 1)..]);
        for (ulong x = a; x <= b; x++) yield return x;
    }
}
