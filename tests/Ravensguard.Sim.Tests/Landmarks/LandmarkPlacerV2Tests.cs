using System.Text.Json;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Landmarks;

/// <summary>P8-LM-1 (landmarks, spec 03 gates LM-1 to LM-4): about 40 templates, placer v2, old worlds on placer v1, lore that resolves.</summary>
public class LandmarkPlacerV2Tests(ITestOutputHelper output)
{
    private static readonly ulong[] Seeds = [1, 2, 3, 4, 5, 6, 7, 8, 9, 1234];

    /// <summary>LM-1: forty templates; every event, biome, prop, site and keeper resolves.</summary>
    [Fact]
    public void LM1_forty_templates_load_and_every_reference_resolves()
    {
        var db = TestWorld.Content_.Db;
        var defs = db.All<LandmarkDef>().ToList();
        Assert.Equal(40, defs.Count);
        Assert.Equal(12, defs.Count(d => d.Placer <= 1)); // the twelve placer v1 knows
        Assert.Equal(defs.Count, defs.Select(d => d.Id).Distinct().Count());
        Assert.Equal(defs.Count, defs.Select(d => d.Name).Distinct().Count());
        var biomes = Enum.GetNames<Ravensguard.Sim.WorldGen.Biome>().Select(n => n.ToLowerInvariant()).ToHashSet();
        foreach (var d in defs)
        {
            foreach (var e in d.AttachEvents) Assert.True(db.Contains("chronicle_type", e), $"{d.Id}: event {e}");
            foreach (var b in d.Biomes) Assert.Contains(b.Replace("_", ""), biomes);
            foreach (var p in d.Props) Assert.True(LandmarkPlacer.TryParseKind(p.Kind, out _), $"{d.Id}: prop {p.Kind}");
            if (d.Site.Length > 0) Assert.True(db.Contains("site_template", d.Site), $"{d.Id}: site {d.Site}");
            if (d.Keeper.Length > 0) Assert.True(db.Contains("dialogue", d.Keeper + ".intro") || db.Contains("dialogue", d.Keeper), $"{d.Id}: keeper {d.Keeper}"); // none named yet: the keepers are P8-LM-2
            Assert.True(d.Props.Count >= 4, d.Id);
            Assert.True(d.Props.Max(p => Math.Max(Math.Abs(p.Dx), Math.Abs(p.Dy))) <= 12, $"{d.Id}: footprint wider than a chunk cell budget");
            Assert.True(d.MinSettlementM <= (d.MaxSettlementM == 0 ? int.MaxValue : d.MaxSettlementM), d.Id);
        }
        // the placer v2 spec's named kinds are all there
        foreach (var id in new[] { "drowned_village", "gallows_hill", "holy_well", "battle_cairn", "lords_ruined_hall", "boundary_oak", "plague_pit", "smugglers_cove", "holy_island", "beacon_hill" })
            Assert.True(db.Contains("landmark", id), id);
    }

    /// <summary>LM-2: ten seeds, 2 to 5 landmarks a region, 3 km apart, no template twice, each attached landmark's event exists and is of a type it names.</summary>
    [Fact]
    public void LM2_placer_v2_keeps_its_caps_and_spacing_on_ten_seeds()
    {
        int attached = 0, distinctTemplates = 0;
        var seen = new HashSet<string>();
        foreach (var seed in Seeds)
        {
            var w = TestWorld.Get(seed);
            Assert.Equal(2, w.Region.PlacerVersion);
            var marks = LandmarkPlacer.Place(w.Region, w.History, w.Content);
            output.WriteLine($"seed {seed}: {marks.Count}: " + string.Join(", ", marks.Select(m => m.TemplateId + (m.Attached ? "*" : ""))));
            Assert.InRange(marks.Count, LandmarkPlacer.MinCountV2, LandmarkPlacer.MaxCountV2);
            Assert.Equal(marks.Count, marks.Select(m => m.TemplateId).Distinct().Count());
            for (int i = 0; i < marks.Count; i++)
                for (int j = i + 1; j < marks.Count; j++)
                {
                    long dx = marks[i].X - marks[j].X, dy = marks[i].Y - marks[j].Y;
                    Assert.True(dx * dx + dy * dy >= 3000L * 3000, $"seed {seed}: {marks[i].TemplateId} and {marks[j].TemplateId} are closer than 3 km");
                }
            foreach (var m in marks)
            {
                seen.Add(m.TemplateId);
                var d = w.Content.Get<LandmarkDef>(m.TemplateId);
                if (m.Attached)
                {
                    attached++;
                    var e = w.History.Chronicle.Get(m.Event);
                    Assert.NotNull(e);
                    Assert.Contains(e!.TypeId, d.AttachEvents);
                }
                else Assert.False(d.RequireAttach, $"{m.TemplateId} needs an event");
            }
            // a pure function of the world: the same again, and with the region's own version
            Assert.Equal(LandmarkPlacer.ComputeHash(marks), LandmarkPlacer.ComputeHash(LandmarkPlacer.Place(w.Region, w.History, w.Content, version: 2)));
        }
        distinctTemplates = seen.Count;
        Assert.True(attached >= 8, $"only {attached} landmarks hung on a history event in ten seeds");
        Assert.True(distinctTemplates >= 15, $"only {distinctTemplates} templates were placed in ten seeds");
    }

    /// <summary>LM-3: an old world (placer v1: settings without the key) keeps its twelve templates, its 4 to 10 landmarks and its landmark hash.</summary>
    [Fact]
    public void LM3_old_worlds_stay_on_placer_v1()
    {
        // the landmark hashes pinned before P8-LM-1 (GoldenHashes keeps them as the landmarks_v1 line)
        var pinned = new Dictionary<ulong, string>
        {
            [1] = "29C6388C290BE1D100AFAF0E1D27F0BE6A9E598A526B75DD17BB5DFFDAD74208",
            [1234] = "7CDDE132D5D5FF67F6606C15F9F5BF10DF47E0289884340759627F311937EF32",
        };
        foreach (var (seed, hash) in pinned)
        {
            var w = TestWorld.Get(seed);
            var v1 = LandmarkPlacer.Place(w.Region, w.History, w.Content, version: 1);
            Assert.Equal(hash, LandmarkPlacer.ComputeHash(v1));
            Assert.InRange(v1.Count, LandmarkPlacer.MinCount, LandmarkPlacer.MaxCount);
            foreach (var m in v1) Assert.Equal(1, w.Content.Get<LandmarkDef>(m.TemplateId).Placer);
            LandmarkPlacer.Place(w.Region, w.History, w.Content); // leave the shared region as v2 placed it
        }
        // a world whose settings carry no placer key is on v1, and its saved settings stay as they were (0 is never written)
        var rs = RegionSettings.LoadDefaults(TestWorld.Content_.Dir); rs.Seed = 1;
        Assert.Equal(2, rs.LandmarkPlacer);
        Assert.Contains("\"LandmarkPlacer\":2", JsonSerializer.Serialize(rs));
        rs.LandmarkPlacer = 0;
        var old = JsonSerializer.Deserialize<RegionSettings>(JsonSerializer.Serialize(rs))!;
        Assert.Equal(0, old.LandmarkPlacer);
        Assert.DoesNotContain("LandmarkPlacer", JsonSerializer.Serialize(old));
        var w1 = TestWorld.Get(1);
        var oldRegion = RegionGenerator.Generate(w1.Continent, w1.History, w1.Pick.CellX, w1.Pick.CellY, old);
        Assert.Equal(0, oldRegion.PlacerVersion);
        Assert.Equal(pinned[1], LandmarkPlacer.ComputeHash(LandmarkPlacer.Place(oldRegion, w1.History, w1.Content)));
        Assert.Equal(w1.Region.ComputeHash(), oldRegion.ComputeHash()); // the version never reaches the generated region
    }

    /// <summary>LM-4: 200 placements of the lore: every token resolves, no empty braces, no hole where an empty event text was.</summary>
    [Fact]
    public void LM4_lore_resolves_for_two_hundred_placements()
    {
        var defs = TestWorld.Content_.Db.All<LandmarkDef>().OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
        var allowed = LandmarkPlacer.Tokens.ToHashSet();
        string[] texts = ["", "The harvest failed and the granaries were opened.", "Fire took the lower town. "];
        int n = 0;
        foreach (var d in defs)
            for (int k = 0; k < 5; k++)
            {
                string text = texts[k % texts.Length];
                string place = k % 2 == 0 ? "Ashford" : "the district";
                string lore = d.AttachEvents.Count > 0 && (d.RequireAttach || k < 4)
                    ? LandmarkPlacer.Compose(d, "The Battle of Holm", 212 + k, text, k == 1 ? "someone whose name is lost" : "Aldred Marsh", k == 2 ? "the older realms" : "Veldmark", place)
                    : (d.LoreUnattached.Length > 0 ? d.LoreUnattached : d.Lore).Replace("{place}", place);
                n++;
                Assert.DoesNotContain("{", lore); Assert.DoesNotContain("}", lore);
                Assert.DoesNotContain("  ", lore); Assert.DoesNotContain(". .", lore); Assert.DoesNotContain(" .", lore); Assert.DoesNotContain("..", lore);
                Assert.True(lore.Length is >= 60 and <= 700, $"{d.Id}: {lore.Length}");
                Assert.EndsWith(".", lore.TrimEnd());
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(d.Lore + d.LoreUnattached, @"\{[a-z]+\}")) Assert.Contains(m.Value, allowed);
            }
        Assert.Equal(200, n);
    }
}
