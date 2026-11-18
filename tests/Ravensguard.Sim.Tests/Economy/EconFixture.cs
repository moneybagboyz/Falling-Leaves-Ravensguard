using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Save;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Tests.Economy;

/// <summary>Shared content and one populated world (seed 1234) for the economy and goods tests; built once.</summary>
public static class EconFixture
{
    public const ulong Seed = 1234;
    public sealed record Built(ContentDatabase Content, string ContentHash, ContinentSettings Cs, HistorySettings Hs, RegionSettings Rs, Continent Continent, WorldHistory History, Population Pop, int CellX, int CellY);

    private static readonly Lazy<(ContentDatabase Db, string Hash)> Loaded = new(() =>
    {
        var dir = ContinentSettings.FindBaseContent()!;
        using var cache = new TempDir();
        var load = new ModLoader(ContentTypeRegistry.Default).Load([dir], cache.Path);
        Assert.True(load.Success, load.Report.ToString());
        return (load.Database, load.Hash!);
    });

    public static ContentDatabase Content => Loaded.Value.Db;
    public static string ContentHash => Loaded.Value.Hash;
    public static GoodCatalog Catalog() => new(Content);

    private static readonly Lazy<Built> World = new(() =>
    {
        var dir = ContinentSettings.FindBaseContent()!;
        var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = Seed;
        var hs = HistorySettings.LoadDefaults(dir); hs.Seed = Seed;
        var rs = RegionSettings.LoadDefaults(dir); rs.Seed = Seed;
        var c = ContinentGenerator.Generate(cs);
        var h = HistoryGenerator.Generate(c, hs);
        var pick = EmbarkSite.AutoPick(c, h);
        var r = RegionGenerator.Generate(c, h, pick.CellX, pick.CellY, rs);
        LandmarkPlacer.Place(r, h, Content);
        var pop = Populator.Populate(r, h, Content);
        return new Built(Content, ContentHash, cs, hs, rs, c, h, pop, pick.CellX, pick.CellY);
    });

    public static Built Shared => World.Value;
    public static Population Pop => Shared.Pop;

    public static EconomyState NewEconomy(bool goods = true) => EconomyState.Create(Pop, Content, Seed, goods);

    public static WorldState WorldWith(EconomyState? econ)
    {
        var b = Shared;
        return new WorldState
        {
            ContinentSettings = b.Cs, HistorySettings = b.Hs, RegionSettings = b.Rs, EmbarkCellX = b.CellX, EmbarkCellY = b.CellY,
            Continent = b.Continent, History = b.History, Population = b.Pop, Clock = new SimClock(GameTime.FromCalendar(b.Pop.NowYear, Season.Summer, 3, 14, 20)),
            Ids = new IdAllocator(), Chunks = new ChunkStore(), Chronicle = new Chronicle(), Economy = econ,
        };
    }

    /// <summary>Small synthetic household tables: one household of the given ages (years at day 0), peasant class.</summary>
    public static (PersonTable People, HouseholdTable Households) Household(params int[] ages) => Household(SocialClass.Peasant, ages);

    public static (PersonTable People, HouseholdTable Households) Household(SocialClass cls, int[] ages)
    {
        var p = new PersonTable(ages.Length, 1); var h = new HouseholdTable(1, 1);
        int hh = h.Add(0, 0, Ravensguard.Sim.WorldGen.Region.DwellingType.Cottage, 6);
        h.Class[hh] = cls;
        foreach (int age in ages)
        {
            int i = p.Add(); p.BirthDay[i] = -age * TimeConst.DaysPerYear; p.Household[i] = hh; h.AddMember(hh, i);
        }
        return (p, h);
    }

    public static EconomyState SmallEconomy(HouseholdTable h, PersonTable p)
    {
        var e = EconomyState.FromContent(Content, h.Count, p.Count);
        e.OpenHouseholds(h);
        return e;
    }
}
