using System.Collections.Concurrent;
using Ravensguard.Content;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.Seamless;

/// <summary>A continent and history for a seed, built once per test run, plus a cache of the regions generated from them.</summary>
public sealed class SeamWorld
{
    private static readonly ConcurrentDictionary<ulong, SeamWorld> Worlds = new();
    public static SeamWorld Get(ulong seed) => Worlds.GetOrAdd(seed, s => new SeamWorld(s));

    public ulong Seed { get; }
    public Continent Continent { get; }
    public WorldHistory History { get; }
    public RegionSettings Settings { get; }
    public ContentDatabase Content { get; }
    public EmbarkScore Embark { get; }
    /// <summary>The north-west square of a 3 x 3 block whose centre is the embark region (clamped to fit the grid).</summary>
    public RegionCoord BlockOrigin { get; }
    private readonly ConcurrentDictionary<int, Lazy<RegionData>> _regions = new();

    private SeamWorld(ulong seed)
    {
        Seed = seed;
        var dir = ContinentSettings.FindBaseContent()!;
        var load = new ModLoader(ContentTypeRegistry.Default).Load([dir]);
        Content = load.Database;
        var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = seed;
        var hs = HistorySettings.LoadDefaults(dir); hs.Seed = seed;
        Settings = RegionSettings.LoadDefaults(dir); Settings.Seed = seed;
        Continent = ContinentGenerator.Generate(cs);
        History = HistoryGenerator.Generate(Continent, hs);
        Embark = EmbarkSite.AutoPick(Continent, History);
        var e = RegionCoord.Snap(Embark.CellX, Embark.CellY);
        BlockOrigin = new RegionCoord(Math.Clamp(e.X - 1, 0, 13), Math.Clamp(e.Y - 1, 0, 13));
    }

    public RegionData Region(RegionCoord rc)
        => _regions.GetOrAdd(rc.Id, _ => new Lazy<RegionData>(() => RegionGenerator.Generate(Continent, History, rc, Settings))).Value;

    public RegionData Fresh(RegionCoord rc) => RegionGenerator.Generate(Continent, History, rc, Settings);

    public IEnumerable<RegionCoord> Block3()
    {
        for (int dy = 0; dy < 3; dy++) for (int dx = 0; dx < 3; dx++) yield return BlockOrigin.Offset(dx, dy);
    }
}
