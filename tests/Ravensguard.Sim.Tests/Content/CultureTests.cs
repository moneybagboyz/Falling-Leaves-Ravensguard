using Ravensguard.Content;
using Ravensguard.Sim.Building;

namespace Ravensguard.Sim.Tests.Content;

/// <summary>Culture packs (plan §2.9): every reference resolves and the calendar and law fields are complete.</summary>
public sealed class CultureTests
{
    private static ContentDatabase Db()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var r = new ModLoader(ContentTypeRegistry.Default).Load([Path.Combine(root, "content", "base")]);
        Assert.True(r.Success, r.Report.ToString());
        return r.Database;
    }

    [Fact]
    public void CulturesCoverEveryNamePoolAndResolveEveryReference()
    {
        var db = Db();
        var cultures = db.All<CultureDef>().ToList();
        Assert.True(cultures.Count >= 4);
        var pools = db.All<NamePoolDef>().Select(p => p.Id).ToHashSet();
        foreach (var p in pools) Assert.Contains(cultures, c => c.NamePool == p);
        var styles = db.All<CultureStyleDef>().ToDictionary(s => s.Id);
        foreach (var c in cultures)
        {
            Assert.Contains(c.NamePool, pools);
            foreach (var w in new[] { "poor", "middling", "rich" })
            {
                Assert.True(c.BuildingStyles.TryGetValue(w, out var sid), $"{c.Id}: no building style for {w}");
                Assert.True(styles.ContainsKey(sid!), $"{c.Id}: unknown culture_style '{sid}'");
            }
            Assert.Equal(3, c.BuildingStyles.Count);
        }
    }

    [Fact]
    public void EveryStyleCoversEveryWealthWithKnownPieces()
    {
        var db = Db();
        var pieces = db.All<BuildPieceDef>().ToDictionary(p => p.Id);
        foreach (var s in db.All<CultureStyleDef>())
            foreach (var w in new[] { "poor", "middling", "rich" })
            {
                Assert.True(s.Walls.TryGetValue(w, out var wall) && pieces.TryGetValue(wall, out var wp) && wp.Kind == PieceKind.Wall, $"{s.Id} walls {w}");
                Assert.True(s.Floors.TryGetValue(w, out var floor) && pieces.TryGetValue(floor, out var fp) && fp.Kind == PieceKind.Floor, $"{s.Id} floors {w}");
            }
    }

    [Fact]
    public void CalendarAndLawFieldsAreComplete()
    {
        foreach (var c in Db().All<CultureDef>())
        {
            Assert.Equal(12, c.Months.Count);
            Assert.Equal(12, c.Months.Distinct().Count());
            Assert.InRange(c.FeastDays.Count, 3, 5);
            Assert.Equal(c.FeastDays.Count, c.FeastDays.Select(f => f.Id).Distinct().Count());
            foreach (var f in c.FeastDays) { Assert.InRange(f.Month, 1, 12); Assert.InRange(f.Day, 1, 5); }
            Assert.InRange(c.TithePercent, 0, 20);
            Assert.True(c.HearthTax >= 0);
            Assert.InRange(c.MarriageAge.Male, 12, 30); Assert.InRange(c.MarriageAge.Female, 12, 30);
            Assert.NotEmpty(c.Crimes);
            Assert.All(c.Crimes.Values, k => Assert.False(string.IsNullOrWhiteSpace(k)));
            Assert.NotEmpty(c.RoofPalette); Assert.NotEmpty(c.WallPalette);
        }
    }

    [Fact]
    public void BlueprintsReferenceKnownStylesAndPhase1CatalogueIsPresent()
    {
        var db = Db();
        var lib = BuildingLibrary.FromDatabase(db);
        var styles = db.All<CultureStyleDef>().Select(s => s.Id).ToHashSet();
        foreach (var b in lib.Blueprints.Where(b => b.Def.Style != null)) Assert.Contains(b.Def.Style!, styles);
        Assert.True(lib.Blueprints.Count >= 60, $"only {lib.Blueprints.Count} blueprints");
        Assert.True(lib.Blueprints.Count(b => b.Def.Kind == BlueprintKind.RoomTemplate) >= 8);
        // every wealth level is represented among dwellings
        foreach (var w in new[] { Wealth.Poor, Wealth.Middling, Wealth.Rich })
            Assert.Contains(lib.Blueprints, b => b.Def.Wealth == w && b.Def.Dwelling != null);
        foreach (var id in new[] { "hovel", "burgage_middling", "merchant_house", "tower_house", "fortified_manor", "bakery", "brewhouse", "tannery", "pottery", "watermill", "windmill", "sawpit",
                                   "charcoal_clamp_site", "market_stalls", "market_hall", "parish_church", "minster_small", "inn", "gaol", "toll_house", "town_gate", "wall_segment", "wall_corner_tower", "palisade_gate", "keep",
                                   "template_solar", "template_kitchen", "template_storeroom", "template_crypt", "template_great_hall" })
            Assert.Contains(lib.Blueprints, b => b.Def.Id == id);
    }
}
