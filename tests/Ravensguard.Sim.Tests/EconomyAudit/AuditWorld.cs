using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.WarSupply;

namespace Ravensguard.Sim.Tests.EconomyAudit;

/// <summary>The audit tests' world: a new game of seed 1234 (the shared generated region, its people cloned) on the real runtime with the game's links attached, the host supply interface installed.</summary>
public static class AuditWorld
{
    public static Ravensguard.Sim.People.Population FreshPop()
    {
        var b = EconFixture.Shared;
        var region = Ravensguard.Sim.WorldGen.Region.RegionGenerator.Generate(b.Continent, b.History, b.CellX, b.CellY, b.Rs);
        Ravensguard.Sim.WorldGen.Region.LandmarkPlacer.Place(region, b.History, b.Content);
        return Ravensguard.Sim.People.Populator.Populate(region, b.History, b.Content);
    }

    public static WarLoadRun Create(bool links = true)
    {
        var b = EconFixture.Shared;
        // a region and people of its own: growth lays lots into the region's plans, and Population.Clone shares the region, so a shared one would be grown by one test and met by the next
        var pop = FreshPop();
        return WarLoadRun.Create(b.Content, b.Continent, b.History, b.Cs, b.Hs, b.Rs, b.CellX, b.CellY, pop, links);
    }

    /// <summary>The settlement with the most people (the host's start) and the one furthest from it (the gate).</summary>
    public static (int Start, int Gate) Stops(WarLoadRun run)
    {
        var region = run.State.Population.Region;
        int start = Enumerable.Range(0, region.Settlements.Count).OrderByDescending(s => region.Settlements[s].TargetPop).ThenBy(s => s).First();
        return (start, WarLoad.FarthestSettlement(region, start));
    }

    /// <summary>A test host: a chest and a baggage store of its own, <paramref name="men"/> in the given roles, funded with <paramref name="crowns"/> of seed coin (the test's faucet).</summary>
    public static HostId Host(WarLoadRun run, int n, Roster men, int crowns, int followers = 0, AnimalCounts animals = default, int outside = 0)
    {
        var econ = run.Econ; var led = econ.Ledger;
        var chest = led.Open(PurseKind.Chest, HostIds.Chest(n));
        var bag = econ.OpenStore(Ravensguard.Sim.Goods.StoreKind.Sealed, HostIds.Baggage(n));
        if (crowns > 0) led.Mint(chest, Coin.FromCrowns(crowns), SourceReason.Seed);
        return run.Supply.Register(new HostSpec(0, Ravensguard.Sim.Core.EntityId.None, chest, bag, men, followers, animals, 0, new SupplyPolicy()) { OutsideMen = outside });
    }
}
