using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Tests.Review;

/// <summary>A private world (the tests below edit it and put it back), so no other test sees a changed field.</summary>
public sealed class HashWorldFixture
{
    public Continent Continent { get; }
    public WorldHistory History { get; }
    public Ravensguard.Sim.WorldGen.Region.Region Region { get; }
    public Population Population { get; }

    public HashWorldFixture()
    {
        var (db, _, dir) = TestWorld.Content_;
        var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = 5;
        var hs = HistorySettings.LoadDefaults(dir); hs.Seed = 5;
        var rs = RegionSettings.LoadDefaults(dir); rs.Seed = 5;
        Continent = ContinentGenerator.Generate(cs);
        History = HistoryGenerator.Generate(Continent, hs);
        var pick = EmbarkSite.AutoPick(Continent, History);
        Region = RegionGenerator.Generate(Continent, History, pick.CellX, pick.CellY, rs);
        Population = Populator.Populate(Region, History, db);
    }
}

/// <summary>Phase 1 review: every saved or generated field must change the layer hash, or a load cannot tell it came back wrong.</summary>
public class HashCoverageTests(HashWorldFixture fx) : IClassFixture<HashWorldFixture>
{
    private static void Changes(Func<string> hash, Action edit, Action undo, string what)
    {
        string before = hash();
        edit();
        try { Assert.NotEqual(before, hash()); }
        finally { undo(); }
        Assert.Equal(before, hash());
        _ = what;
    }

    // ---- population
    [Fact]
    public void Population_hash_covers_lot_beds()
    {
        int s = Array.FindIndex(fx.Population.LotBeds, a => a.Length > 0), l = 0;
        Changes(fx.Population.ComputeHash, () => fx.Population.LotBeds[s][l]++, () => fx.Population.LotBeds[s][l]--, "LotBeds");
    }

    [Fact]
    public void Population_hash_covers_fief_lord_household_priest_household_and_settlement_people()
    {
        var p = fx.Population;
        int f = Array.FindIndex(p.FiefLordHousehold, v => v >= 0);
        Changes(p.ComputeHash, () => p.FiefLordHousehold[f]++, () => p.FiefLordHousehold[f]--, "FiefLordHousehold");
        int pr = Array.FindIndex(p.PriestHousehold, v => v >= 0);
        Assert.True(pr >= 0, "the world should have a priest's household");
        Changes(p.ComputeHash, () => p.PriestHousehold[pr]++, () => p.PriestHousehold[pr]--, "PriestHousehold");
        Changes(p.ComputeHash, () => p.SettlementPeople[0]++, () => p.SettlementPeople[0]--, "SettlementPeople");
    }

    // ---- region
    [Fact]
    public void Region_hash_covers_vacant_lots_culture_waterway_links_waterway_tiles_and_lake_bounds()
    {
        var r = fx.Region;
        var s = r.Settlements.First(x => x.Plan.Lots.Count > 0);
        var lots = s.Plan.Lots; var lot = lots[0];
        Changes(r.ComputeHash, () => lots[0] = lot with { Vacant = !lot.Vacant }, () => lots[0] = lot, "Lot.Vacant");
        int cult = s.Culture;
        Changes(r.ComputeHash, () => s.Culture = cult + 1, () => s.Culture = cult, "Culture");
        int nw = s.NearWaterway;
        Changes(r.ComputeHash, () => s.NearWaterway = nw + 1, () => s.NearWaterway = nw, "NearWaterway");
        var ww = r.Waterways.First(w => w.Tiles.Length > 0);
        Changes(r.ComputeHash, () => ww.Tiles[0]++, () => ww.Tiles[0]--, "Waterway.Tiles");
        // the embark square may have no lake: take the first square of the grid that has one
        var rs = RegionSettings.LoadDefaults(ContinentSettings.FindBaseContent()); rs.Seed = 5;
        var lr = r.Lakes.Count > 0 ? r : Enumerable.Range(0, RegionCoord.Count).Select(i => RegionGenerator.Generate(fx.Continent, fx.History, RegionCoord.FromId(i), rs)).First(x => x.Lakes.Count > 0);
        Assert.NotEmpty(lr.Lakes);
        var lake = lr.Lakes[0];
        Changes(lr.ComputeHash, () => lake.MinTileX++, () => lake.MinTileX--, "Lake.MinTileX");
        Changes(lr.ComputeHash, () => lake.MaxTileY++, () => lake.MaxTileY--, "Lake.MaxTileY");
    }

    [Fact]
    public void Landmark_hash_covers_every_landmark_field_and_does_not_depend_on_the_region_hash()
    {
        var (db, _, _) = TestWorld.Content_;
        string regionHash = fx.Region.ComputeHash();
        var marks = LandmarkPlacer.Place(fx.Region, fx.History, db).ToList();
        Assert.Equal(regionHash, fx.Region.ComputeHash()); // placing landmarks does not change the region hash...
        Assert.NotEmpty(marks);
        string h = LandmarkPlacer.ComputeHash(marks);
        Assert.Equal(h, LandmarkPlacer.ComputeHash(LandmarkPlacer.Place(fx.Region, fx.History, db))); // ...and is deterministic
        Assert.NotEqual(h, LandmarkPlacer.ComputeHash(marks.Skip(1).ToList()));
        Assert.NotEqual(h, LandmarkPlacer.ComputeHash(marks.Select((m, i) => i == 0 ? m with { X = m.X + 1 } : m).ToList()));
        Assert.NotEqual(h, LandmarkPlacer.ComputeHash(marks.Select((m, i) => i == 0 ? m with { Lore = m.Lore + "." } : m).ToList()));
        Assert.NotEqual(h, LandmarkPlacer.ComputeHash(marks.Select((m, i) => i == 0 ? m with { Props = m.Props.Skip(1).ToList() } : m).ToList()));
        Assert.NotEqual(h, LandmarkPlacer.ComputeHash(null));
    }

    // ---- history
    [Fact]
    public void History_hash_covers_notable_sex_culture_and_residence()
    {
        var h = fx.History;
        var n = h.Notables.First(x => x.Residence >= 0);
        Changes(h.ComputeHash, () => n.Female = !n.Female, () => n.Female = !n.Female, "Female");
        Changes(h.ComputeHash, () => n.Culture++, () => n.Culture--, "Culture");
        Changes(h.ComputeHash, () => n.Residence++, () => n.Residence--, "Residence");
    }

    [Fact]
    public void History_hash_covers_artefact_creation_and_trade_route_cells()
    {
        var h = fx.History;
        Assert.NotEmpty(h.Artefacts);
        var a = h.Artefacts[0];
        var creator = a.Creator;
        Changes(h.ComputeHash, () => a.Creator = new EntityId(creator.Value + 1), () => a.Creator = creator, "Creator");
        Changes(h.ComputeHash, () => a.CreatedTick++, () => a.CreatedTick--, "CreatedTick");
        var ev = a.CreatedEvent;
        Changes(h.ComputeHash, () => a.CreatedEvent = new EntityId(ev.Value + 1), () => a.CreatedEvent = ev, "CreatedEvent");
        var t = h.TradeRoutes.First(x => x.Cells.Length > 0);
        Changes(h.ComputeHash, () => t.Cells[0]++, () => t.Cells[0]--, "TradeRoute.Cells");
    }
}
