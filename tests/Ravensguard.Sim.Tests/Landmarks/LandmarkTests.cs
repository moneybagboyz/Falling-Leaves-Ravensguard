using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Landmarks;

public class LandmarkTests(ITestOutputHelper output)
{
    private static readonly ulong[] Seeds = [1, 2, 3, 7, 1234];

    [Fact]
    public void Content_has_a_dozen_templates_with_valid_footprints_and_lore_tokens()
    {
        var db = TestWorld.Content_.Db;
        var defs = db.All<LandmarkDef>().ToList();
        Assert.InRange(defs.Count, 12, 20);
        var biomes = Enum.GetNames<Ravensguard.Sim.WorldGen.Biome>().Select(n => n.ToLowerInvariant()).ToHashSet();
        var allowed = LandmarkPlacer.Tokens.ToHashSet();
        foreach (var d in defs)
        {
            Assert.NotEmpty(d.Props);
            foreach (var p in d.Props) Assert.True(LandmarkPlacer.TryParseKind(p.Kind, out _), $"{d.Id}: prop kind {p.Kind}");
            foreach (var b in d.Biomes) Assert.Contains(b.Replace("_", ""), biomes);
            foreach (var text in new[] { d.Lore, d.LoreUnattached })
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"\{[a-z]+\}"))
                    Assert.Contains(m.Value, allowed);
            if (d.AttachEvents.Count == 0) { Assert.False(d.RequireAttach, d.Id); Assert.DoesNotContain("{event}", d.Lore); }
            if (d.AttachEvents.Count > 0 && !d.RequireAttach) Assert.True(d.LoreUnattached.Length > 0, $"{d.Id} needs an unattached lore text");
            foreach (var t in new[] { "{event}", "{year}", "{text}", "{person}", "{realm}" }) Assert.DoesNotContain(t, d.LoreUnattached);
            Assert.True(d.Lore.Length > 40, d.Id);
        }
        foreach (var id in new[] { "standing_stone_ring", "hermits_cell", "ruined_watchtower", "holy_well", "gallows_crossroads", "battle_cairn", "giants_grave", "ribboned_oak",
                                   "old_bridge_of_the_first_kings", "charcoal_burners_clearing", "smugglers_cove", "plague_pit" })
            Assert.True(db.Contains("landmark", id), id);
        Assert.Equal("The old bridge of the first kings", db.Get<LandmarkDef>("old_bridge_of_the_first_kings").Name);
    }

    [Fact]
    public void Each_region_gets_four_to_ten_landmarks_that_obey_their_rules()
    {
        int attached = 0;
        foreach (var seed in Seeds)
        {
            var w = TestWorld.Get(seed);
            var marks = LandmarkPlacer.Place(w.Region, w.History, w.Content);
            output.WriteLine($"seed {seed}: {marks.Count} landmarks: " + string.Join(", ", marks.Select(m => m.TemplateId + (m.Attached ? "*" : ""))));
            Assert.InRange(marks.Count, LandmarkPlacer.MinCount, LandmarkPlacer.MaxCount);
            Assert.Same(marks, w.Region.Landmarks);
            foreach (var m in marks)
            {
                var d = w.Content.Get<LandmarkDef>(m.TemplateId);
                int t = m.TileY * Ravensguard.Sim.WorldGen.Region.Region.Size + m.TileX;
                Assert.Equal(0, w.Region.Water[t]);
                Assert.Equal(0, w.Region.Built[t]);
                double near = w.Region.Settlements.Min(s => Math.Pow(s.CenterX - m.X, 2) + Math.Pow(s.CenterY - m.Y, 2));
                Assert.True(near >= (long)d.MinSettlementM * d.MinSettlementM - 1, $"{m.TemplateId} too close to a settlement");
                if (d.MaxSettlementM > 0) Assert.True(near <= (double)d.MaxSettlementM * d.MaxSettlementM + 1, $"{m.TemplateId} too far from a settlement");
                if (d.Biomes.Count > 0) Assert.Contains(w.Region.Biomes[t].ToString().ToLowerInvariant(), d.Biomes.Select(b => b.Replace("_", "")));
                if (d.RequireAttach) Assert.True(m.Attached, m.TemplateId);
                if (m.Attached)
                {
                    attached++;
                    var e = w.History.Chronicle.Get(m.Event);
                    Assert.NotNull(e);
                    Assert.Contains(e!.TypeId, d.AttachEvents);
                    if (d.Lore.Contains("{event}")) Assert.Contains(m.EventTitle, m.Lore); // a template may name the realm or the person instead of the event
                    Assert.Equal(e.When.Year, m.Year);
                }
                Assert.DoesNotContain("{", m.Lore);
                Assert.False(string.IsNullOrWhiteSpace(m.Place));
                Assert.InRange(m.Lore.Length, 40, 900);
                if (d.Coastal) Assert.Contains(Enumerable.Range(-4, 9).SelectMany(dy => Enumerable.Range(-4, 9).Select(dx => (dx, dy))),
                    p => Ravensguard.Sim.WorldGen.Region.Region.InBounds(m.TileX + p.dx, m.TileY + p.dy) && (w.Region.Water[(m.TileY + p.dy) * Ravensguard.Sim.WorldGen.Region.Region.Size + m.TileX + p.dx] & WaterFlags.Sea) != 0);
            }
            Assert.Equal(marks.Count, marks.Select(m => m.Event).Where(e => e != EntityId.None).Count() + marks.Count(m => !m.Attached));
            Assert.Equal(marks.Count(m => m.Attached), marks.Where(m => m.Attached).Select(m => m.Event).Distinct().Count()); // one landmark per event
        }
        Assert.True(attached > 0, "no landmark hung on a history event in any seed");
    }

    [Fact]
    public void Placement_is_deterministic_and_leaves_the_region_hash_alone()
    {
        var w = TestWorld.Get(1234);
        string hash = w.Region.ComputeHash();
        var a = LandmarkPlacer.Place(w.Region, w.History, w.Content).Select(Key).ToList();
        var b = LandmarkPlacer.Place(w.Region, w.History, w.Content).Select(Key).ToList();
        Assert.Equal(a, b);
        Assert.Equal(hash, w.Region.ComputeHash());
        // an independently generated region gives the same landmarks
        var r2 = RegionGenerator.Generate(w.Continent, w.History, w.Pick.CellX, w.Pick.CellY, w.RegionSettings);
        Assert.Equal(a, LandmarkPlacer.Place(r2, w.History, w.Content).Select(Key).ToList());
        static string Key(PlacedLandmark m) => $"{m.TemplateId}@{m.X},{m.Y}:{m.Event.Value}:{m.Lore}";
    }

    [Fact]
    public void Chunks_stamp_the_footprint_as_props_and_the_hash_changes_only_there()
    {
        var w = TestWorld.Get(1234);
        var marks = LandmarkPlacer.Place(w.Region, w.History, w.Content);
        var pop = Populator.Populate(w.Region, w.History, w.Content);
        var m = marks[0];
        int cx = m.X / 32, cy = m.Y / 32;
        var gen = new ChunkGenerator(w.Region, pop, w.Content);
        var with = gen.GenerateLevels(cx, cy);
        int found = 0;
        foreach (var o in with.Levels[0].Objects.Values)
        {
            var rec = PropRecord.Decode(o.Data);
            if ((rec.Extra & ChunkGenerator.LandmarkExtraFlag) != 0 && (rec.Extra & 0x7FFF) == m.Index) found++;
        }
        output.WriteLine($"{m.TemplateId}: {found} of {m.Props.Count} footprint props in chunk ({cx},{cy})");
        Assert.True(found > 0, "no landmark props in the chunk of the landmark centre");
        // a region without landmarks gives the plain chunk
        var plain = TestWorld.Get(1234).Region;
        plain.Landmarks = null;
        var without = new ChunkGenerator(plain, pop, w.Content).GenerateLevels(cx, cy);
        Assert.NotEqual(without.ContentHash, with.ContentHash);
        plain.Landmarks = marks;
        Assert.Equal(with.ContentHash, new ChunkGenerator(plain, pop, w.Content).GenerateLevels(cx, cy).ContentHash);
    }
}
