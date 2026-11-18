using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Tests.RegionsB;

/// <summary>A seamless world of seed 1234 with a region manager (inline builds), record set, chunk store and scheduler, for the part B tests.</summary>
public sealed class RegionRig
{
    public SeamWorld W { get; }
    public RegionServices Sv { get; }
    public RegionManager Mgr { get; }
    public RegionRecordSet Set { get; }
    public ChunkStore Chunks { get; } = new();
    public RegionScheduler Sched { get; }
    public RegionCoord Home { get; }
    public ContinentSettings Cs { get; }
    public HistorySettings Hs { get; }

    public RegionRig(RegionCoord? home = null, int dailyRing = 2)
    {
        W = SeamWorld.Get(1234);
        Home = home ?? W.BlockOrigin.Offset(1, 1);
        var dir = ContinentSettings.FindBaseContent()!;
        Cs = ContinentSettings.LoadDefaults(dir); Cs.Seed = 1234;
        Hs = HistorySettings.LoadDefaults(dir); Hs.Seed = 1234;
        Sv = new RegionServices(1234, W.Content, W.Continent, Home);
        Mgr = new RegionManager(W.Continent, W.History, W.Settings, W.Content) { Synchronous = true };
        Set = new RegionRecordSet(Sv, Mgr, Chunks);
        Sched = new RegionScheduler(Set) { DailyRing = dailyRing };
        Generate(Home);
    }

    public RegionWorld Generate(RegionCoord rc) { Mgr.GenerateNow(rc); Set.Sync(); return Set.TryGet(rc)!; }

    /// <summary>Generates candidates in order until one has people; the first populated region.</summary>
    public RegionWorld FirstPopulated(IEnumerable<RegionCoord> candidates)
    {
        foreach (var rc in candidates) { if (!rc.IsValid) continue; Mgr.GenerateNow(rc); Set.Sync(); if (Set.TryGet(rc) is { } rw) return rw; }
        throw new InvalidOperationException("no populated candidate");
    }

    public static GameTime Start(RegionWorld rw) => GameTime.FromGameHours(rw.HourDone + 1);

    /// <summary>The home region's world state with the record set attached, for the save tests.</summary>
    public WorldState State(GameTime clock)
    {
        var h = Set.HomeWorld!;
        var sim = h.Sim; var ew = h.Economy;
        return new WorldState
        {
            ContinentSettings = Cs, HistorySettings = Hs, RegionSettings = W.Settings, EmbarkCellX = Home.CellX, EmbarkCellY = Home.CellY,
            Continent = W.Continent, History = W.History, Population = h.Population, Clock = new SimClock(clock),
            Ids = h.Ids, Chunks = Chunks, Chronicle = h.Chronicle, Economy = sim.Economy, Farming = ew.Farming.State, Abstract = sim.State,
            Production = ew.Production, Markets = ew.Markets, Regions = Set,
        };
    }
}
