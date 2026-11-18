using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Onboarding;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Tests.Onboarding;

/// <summary>Worlds for the onboarding tests: a fresh headless world over the shared region (no ticker), and the origins.</summary>
public static class OnbKit
{
    public static ContentDatabase Db => EconFixture.Content;
    public static IReadOnlyList<OriginDef> Origins => [.. Db.All<OriginDef>().OrderBy(o => o.Order)];

    public static HeadlessWorld NewWorld(ulong seed = EconFixture.Seed)
    {
        var b = EconFixture.Shared;
        var region = RegionGenerator.Generate(b.Continent, b.History, b.CellX, b.CellY, b.Rs);
        LandmarkPlacer.Place(region, b.History, Db);
        var pop = Populator.Populate(region, b.History, Db);
        return HeadlessWorld.FromPopulation(seed, pop, Db, b.Continent, b.History, b.Cs, b.Hs, b.Rs, b.CellX, b.CellY);
    }
}
