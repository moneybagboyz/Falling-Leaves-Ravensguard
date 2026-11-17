using Ravensguard.Content;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests;

/// <summary>Small generated worlds (continent, history, region, content) shared by the landmark, codex and hero tests. Each seed is built once.</summary>
public sealed record TestWorld(ulong Seed, Continent Continent, WorldHistory History, RegionData Region, EmbarkScore Pick, ContentDatabase Content, string ContentHash, string ContentDir, RegionSettings RegionSettings)
{
    private static readonly Dictionary<ulong, TestWorld> Cache = [];
    private static readonly Lazy<(ContentDatabase Db, string Hash, string Dir)> Loaded = new(() =>
    {
        var dir = ContinentSettings.FindBaseContent()!;
        var load = new ModLoader(ContentTypeRegistry.Default, BaseVocabulary.Create()).Load([dir]);
        Assert.True(load.Success, load.Report.ToString());
        return (load.Database, load.Hash ?? "", dir);
    });

    public static (ContentDatabase Db, string Hash, string Dir) Content_ => Loaded.Value;

    public static TestWorld Get(ulong seed)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(seed, out var w)) return w;
            var (db, hash, dir) = Loaded.Value;
            var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = seed;
            var hs = HistorySettings.LoadDefaults(dir); hs.Seed = seed;
            var rs = RegionSettings.LoadDefaults(dir); rs.Seed = seed;
            var c = ContinentGenerator.Generate(cs);
            var h = HistoryGenerator.Generate(c, hs);
            var pick = EmbarkSite.AutoPick(c, h);
            var r = RegionGenerator.Generate(c, h, pick.CellX, pick.CellY, rs);
            return Cache[seed] = new TestWorld(seed, c, h, r, pick, db, hash, dir, rs);
        }
    }
}
