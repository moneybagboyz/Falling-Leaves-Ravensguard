using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Phase6Overview;

/// <summary>
/// OV-8 (overview 4, "determinism and saves"): the Phase 6 trail, a 60-day scripted run of a new game of seed 1234 through the whole runtime with a court day that flogs, a fire, a riot, a slave sale, a plague's arrival, a council and a
/// heist (<see cref="Phase6Trail"/>): the save, loaded and saved again, is byte for byte the same at six points of the run; two runs give one hash; every scene did what it says. The hash is the <c>phase6_trail</c> line of the
/// <c>hashes</c> tool, pinned in <c>GoldenHashes</c> and compared across Linux and Windows by CI's determinism job.
/// </summary>
public class Ov8TrailTests(ITestOutputHelper output)
{
    private static Phase6TrailReport Run(Action<WorldRuntime, WorldState, int>? at = null)
    {
        var w = SeamWorld.Get(1234);
        var home = w.BlockOrigin.Offset(1, 1);
        var dir = ContinentSettings.FindBaseContent()!;
        var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = 1234;
        var hs = HistorySettings.LoadDefaults(dir); hs.Seed = 1234;
        var region = RegionGenerator.Generate(w.Continent, w.History, home, w.Settings);
        LandmarkPlacer.Place(region, w.History, w.Content);
        var pop = Populator.Populate(region, w.History, w.Content);
        return Phase6Trail.Run(w.Content, w.Continent, w.History, cs, hs, w.Settings, home.CellX, home.CellY, pop, at);
    }

    [Fact]
    public void OV8_ASixtyDayScriptedRun_SavesLoadsAndSavesTheSameBytesAtSixPoints_TwoRunsAreOne_AndEverySceneHappened()
    {
        var w = SeamWorld.Get(1234);
        int checkedPoints = 0; var sizes = new List<int>();
        var rep = Run((rt, st, i) =>
        {
            var file = WorldSave.Capture(st, "h", "0.1");
            var bytes = SaveCodec.Encode(file);
            var back = WorldSave.Restore(SaveCodec.Decode(bytes), w.Content, "h", null, w.Continent, w.History); back.Content = w.Content;
            var bytes2 = SaveCodec.Encode(WorldSave.Capture(back, "h", "0.1"));
            Assert.True(bytes.AsSpan().SequenceEqual(bytes2), $"the save of point {i} (day {Phase6Trail.PointDays[i]}) changed after a load: {Differences(file, WorldSave.Capture(back, "h", "0.1"))}");
            sizes.Add(bytes.Length); checkedPoints++;
        });
        foreach (var l in rep.Scenes) output.WriteLine(l);
        output.WriteLine($"phase6_trail {rep.Hash}; save sizes {string.Join(", ", sizes)}");
        Assert.Equal(6, checkedPoints);

        // every scene happened
        Assert.Contains(rep.Scenes, l => l.StartsWith("court: ") && l.Contains("held for theft"));
        Assert.Contains(rep.Scenes, l => l.StartsWith("fire: ") && l.EndsWith(" lit"));
        Assert.Contains(rep.Scenes, l => l.StartsWith("riot: ") && l.Contains("eviction"));
        Assert.Contains(rep.Scenes, l => l.StartsWith("sale: ") && l.Contains("sl_sold"));
        Assert.Contains(rep.Scenes, l => l.StartsWith("plague: begins"));
        Assert.Contains(rep.Scenes, l => l.StartsWith("council: ") && l.Contains("yes"));
        Assert.Contains(rep.Scenes, l => l.StartsWith("heist: ") && l.Contains("unseen"));

        var chron = rep.State!.Chronicle.All;
        foreach (var type in new[] { "sentence_flogged", "fire_started", "fire_ended", "riot", "riot_rises", "riot_ends", "riot_guards", "slave_sold", "plague_begins", "plague_settlement", "official_appointed", "manhunt_began" })
            output.WriteLine($"{type}: {chron.Count(c => c.TypeId == type)}");
        Assert.Contains(chron, c => c.TypeId == "sentence_flogged");          // the court day flogged him, through the wound book
        Assert.Contains(chron, c => c.TypeId == "fire_ended" && c.Data.GetValueOrDefault("cause") == "hearth");          // the hearth fire burnt out, its cause in the entry
        Assert.Contains(chron, c => c.TypeId is "riot_rises" or "riot");
        Assert.Contains(chron, c => c.TypeId == "slave_sold");
        Assert.Contains(chron, c => c.TypeId == "plague_begins"); Assert.Contains(chron, c => c.TypeId == "plague_settlement");
        Assert.Contains(rep.State.Law!.Crimes.Values, c => c.Kind == "jailbreak");
        Assert.True(rep.State.Economy!.Ledger.Audit().IsConserved);

        var again = Run();
        Assert.Equal(rep.Hash, again.Hash);
        Assert.Equal(rep.Scenes, again.Scenes);
    }

    private static string Differences(SaveFile a, SaveFile b)
    {
        var names = a.Sections.Keys.Union(b.Sections.Keys).Order(StringComparer.Ordinal).Where(k => !(a.Sections.TryGetValue(k, out var x) && b.Sections.TryGetValue(k, out var y) && x.Span.SequenceEqual(y.Span)));
        return string.Join(",", names);
    }
}
