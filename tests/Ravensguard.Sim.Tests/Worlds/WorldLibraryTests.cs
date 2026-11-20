using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Worlds;

public class WorldLibraryTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rg-worlds-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private static (ContinentSettings, HistorySettings) Settings(ulong seed)
    {
        var dir = ContinentSettings.FindBaseContent();
        var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = seed;
        var hs = HistorySettings.LoadDefaults(dir); hs.Seed = seed; hs.Years = 60;
        return (cs, hs);
    }

    [Fact]
    public void Create_list_reload_and_delete()
    {
        var lib = new WorldLibrary(_root);
        Assert.Empty(lib.List());
        var (cs, hs) = Settings(7);
        var made = lib.Create("Westmarch Test", cs, hs, "contenthash", "Year 1, day 1");
        var list = lib.List();
        var w = Assert.Single(list);
        Assert.Equal(made.Info.Id, w.Id);
        Assert.Equal("Westmarch Test", w.Name);
        Assert.Equal(7UL, w.Seed);
        Assert.Equal("Year 1, day 1", w.File.Created);
        Assert.Equal(made.Continent.ComputeHash(), w.File.ContinentHash);
        Assert.Equal(made.History.ComputeHash(), w.File.HistoryHash);
        Assert.Equal(made.History.Chronicle.Count, w.File.Summary.ChronicleEntries);
        Assert.True(Directory.Exists(lib.RunsDirectory(w.Id)));

        lib.Delete(w.Id);
        Assert.Empty(lib.List());
    }

    [Fact]
    public void Same_seed_gives_same_hashes_and_world_file_and_reload_matches()
    {
        var a = new WorldLibrary(Path.Combine(_root, "a")); var b = new WorldLibrary(Path.Combine(_root, "b"));
        var (cs, hs) = Settings(11);
        var wa = a.Create("Twin", cs, hs, "h", "t0");
        var (cs2, hs2) = Settings(11);
        var wb = b.Create("Twin", cs2, hs2, "h", "t0");
        Assert.Equal(wa.Info.File.ContinentHash, wb.Info.File.ContinentHash);
        Assert.Equal(wa.Info.File.HistoryHash, wb.Info.File.HistoryHash);
        Assert.Equal(File.ReadAllText(Path.Combine(a.FolderOf(wa.Info.Id), "world.json")), File.ReadAllText(Path.Combine(b.FolderOf(wb.Info.Id), "world.json")));

        var re = a.Load(wa.Info.Id, "h");
        Assert.Equal(wa.Info.File.HistoryHash, re.History.ComputeHash());
        Assert.Equal(wa.Info.File.ContinentHash, re.Continent.ComputeHash());
    }

    [Fact]
    public void Progress_yields_chronicle_entries_as_they_happen_and_can_cancel()
    {
        var lib = new WorldLibrary(_root);
        var (cs, hs) = Settings(5);
        int entries = 0, lastYear = -1; bool sawContinent = false, sawDone = false; double lastFrac = 0;
        var made = lib.Create("Prog", cs, hs, "h", "t", p =>
        {
            if (p.Phase == WorldGenPhase.Continent) sawContinent = true;
            if (p.Phase == WorldGenPhase.Done) sawDone = true;
            if (p.Entry != null) entries++;
            Assert.True(p.Year >= lastYear); lastYear = p.Year;
            Assert.True(p.Fraction >= lastFrac - 1e-9); lastFrac = p.Fraction;
        });
        Assert.True(sawContinent && sawDone);
        Assert.Equal(made.History.Chronicle.Count, entries);

        using var cts = new CancellationTokenSource();
        var (cs2, hs2) = Settings(6);
        Assert.ThrowsAny<OperationCanceledException>(() => lib.Create("Cancelled", cs2, hs2, "h", "t", p => { if (p.Entry != null) cts.Cancel(); }, cts.Token));
        Assert.Single(lib.List()); // nothing half-written
    }

    [Fact]
    public void Library_world_loads_without_regenerating_history_twice()
    {
        var lib = new WorldLibrary(_root);
        var (cs, hs) = Settings(3);
        var before = HistoryGenerator.RunCount;
        lib.Create("Once", cs, hs, "h", "t");
        Assert.Equal(before + 1, HistoryGenerator.RunCount);
        var id = lib.List()[0].Id;

        var content = new ModLoader(ContentTypeRegistry.Default).Load([ContinentSettings.FindBaseContent()!]);
        Assert.True(content.Success, content.Report.ToString());

        // Loading a library world regenerates it exactly once ...
        var mark = HistoryGenerator.RunCount;
        var loaded = lib.Load(id, "h");
        Assert.Equal(mark + 1, HistoryGenerator.RunCount);

        // ... and handing the loaded layers to WorldSave.Restore must not run history again.
        var rs = Ravensguard.Sim.WorldGen.Region.RegionSettings.LoadDefaults(ContinentSettings.FindBaseContent());
        rs.Seed = 3;
        var pick = Ravensguard.Sim.WorldGen.Region.EmbarkSite.AutoPick(loaded.Continent, loaded.History);
        var region = Ravensguard.Sim.WorldGen.Region.RegionGenerator.Generate(loaded.Continent, loaded.History, pick.CellX, pick.CellY, rs);
        var pop = Ravensguard.Sim.People.Populator.Populate(region, loaded.History, content.Database);
        var world = new WorldState
        {
            ContinentSettings = loaded.Info.File.Continent, HistorySettings = loaded.Info.File.History, RegionSettings = rs, EmbarkCellX = pick.CellX, EmbarkCellY = pick.CellY,
            Continent = loaded.Continent, History = loaded.History, Population = pop, Clock = new SimClock(GameTime.FromCalendar(pop.NowYear, Season.Spring, 1)),
            Ids = new IdAllocator(), Chunks = new ChunkStore(), Chronicle = new Chronicle(),
        };
        var file = WorldSave.Capture(world, "h", "test");
        mark = HistoryGenerator.RunCount;
        var restored = WorldSave.Restore(file, content.Database, "h", null, loaded.Continent, loaded.History);
        Assert.Equal(mark, HistoryGenerator.RunCount);
        Assert.Equal(world.Population.ComputeHash(), restored.Population.ComputeHash());
        output.WriteLine($"people {pop.People.Count}");
    }

    [Fact]
    public void Bad_names_are_refused_and_other_content_is_no_gate()
    {
        var lib = new WorldLibrary(_root);
        var (cs, hs) = Settings(2);
        Assert.Throws<WorldLibraryException>(() => lib.Create("  ", cs, hs, "h", "t"));
        Assert.Throws<WorldLibraryException>(() => lib.Delete("../x"));
        lib.Create("Same name", cs, hs, "h", "t");
        var (cs2, hs2) = Settings(2);
        lib.Create("Same name", cs2, hs2, "h", "t2");
        var l = lib.List();
        Assert.Equal(2, l.Count);
        Assert.NotEqual(l[0].Id, l[1].Id);
        Assert.Equal(l[0].Id, lib.Load(l[0].Id, "other").Info.Id);   // other content: reported by LoadReport.ForWorld, loaded when the layers still verify (plan 2.11)
    }
}
