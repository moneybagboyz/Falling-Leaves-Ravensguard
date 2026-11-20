using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Core;

/// <summary>The coverage soak of the chronicle (chronicle-full 4.2, CH-4, CH-5): a new game of seed 1234 run through the whole runtime, its entries measured by type.</summary>
public class ChronicleSoakTests(ITestOutputHelper output)
{
    internal static (SeamWorld W, WorldState State) Game(ulong seed = 1234)
    {
        var w = SeamWorld.Get(seed);
        var home = w.BlockOrigin.Offset(1, 1);
        var dir = Ravensguard.Sim.WorldGen.ContinentSettings.FindBaseContent()!;
        var cs = Ravensguard.Sim.WorldGen.ContinentSettings.LoadDefaults(dir); cs.Seed = seed;
        var hs = Ravensguard.Sim.WorldGen.History.HistorySettings.LoadDefaults(dir); hs.Seed = seed;
        var region = RegionGenerator.Generate(w.Continent, w.History, home, w.Settings);
        LandmarkPlacer.Place(region, w.History, w.Content);
        var pop = Populator.Populate(region, w.History, w.Content);
        var h = HeroRecord.FromOrigin("Aldric Vane", false, w.Content.All<OriginDef>().OrderBy(o => o.Order).First());
        var st = NewGameWorld.Create(w.Content, w.Continent, w.History, cs, hs, w.Settings, home.CellX, home.CellY, pop, h, null);
        st.Content = w.Content;
        return (w, st);
    }

    [Fact]
    public void Probe()
    {
        var (w, st) = Game();
        using var rt = new WorldRuntime(st, w.Content, new MemoryRegionPager());
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int days = int.Parse(Environment.GetEnvironmentVariable("SOAK_DAYS") ?? "60");
        rt.AdvanceTo(GameTime.FromGameHours(rt.HourDone + 1 + days * 24L));
        Hist = w.History.Chronicle; var c = st.Chronicle; var types = ChronicleTypes.From(w.Content);
        var rep = c.SpecificCoverage(types);
        output.WriteLine($"{days} days in {sw.Elapsed.TotalSeconds:0}s: {rep.Entries} entries, specific {rep.Specific}, drift {rep.Drift}, nocause {rep.NoCause}, ratio {rep.Ratio:0.000}");
        var bad = c.All.Where(e => !e.Causes.Any(k => k.Kind == CauseKind.MarketDrift) && !e.Causes.Any(k => c.IsSpecific(k, e, types.Get(e.TypeId)))).GroupBy(e => e.TypeId).OrderByDescending(g => g.Count());
        foreach (var g in bad)
            output.WriteLine($"  {g.Key,-28} {g.Count(),6} of {c.All.Count(e => e.TypeId == g.Key),6} not specific  kinds {string.Join(",", g.SelectMany(e => e.Causes).Select(x => x.Kind).Distinct())}");
        foreach (var e in c.All.Where(e => e.TypeId is "realm_peace" or "pact_ended" or "death" or "farm.yield_loss" && !e.Causes.Any(k => c.IsSpecific(k, e, types.Get(e.TypeId)))).Take(12)) output.WriteLine("    " + Describe(c, e));
        output.WriteLine("types: " + string.Join(", ", c.All.GroupBy(e => e.TypeId).OrderByDescending(g => g.Count()).Select(g => g.Key + " " + g.Count())));
        var h = w.History.Chronicle.SpecificCoverage(types);
        output.WriteLine($"history: {h.Entries} entries, ratio {h.Ratio:0.000}, nocause {h.NoCause}");
    }

    static string Describe(Chronicle c, ChronicleEntry e) => $"{e.TypeId} @{e.When.Seconds} causes: " + string.Join(" | ", e.Causes.Select(k => $"{k.Kind} ev={Ev(c, k)} key={k.NoteKey} note={k.Note}"));
    static string Ev(Chronicle c, Cause k) => k.EventId is not { } id ? "-" : c.Get(id) is { } ce ? $"{id.Value}@{ce.When.Seconds}:{ce.TypeId}" : id.Value + " MISSING" + (Hist?.Get(id) is { } he ? " (history " + he.TypeId + "@" + he.When.Seconds + ")" : "");
    internal static Chronicle? Hist;
}
