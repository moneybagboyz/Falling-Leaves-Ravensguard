using System.Diagnostics;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.WorldGen.RegionGen;

public class RegionTests(ITestOutputHelper output)
{
    private static readonly ulong[] Seeds = [1, 2, 1234];

    private sealed record World(Continent C, WorldHistory H, EmbarkScore Pick, Region R, long GenMs);
    private static readonly Dictionary<ulong, World> Cache = [];

    private static World Get(ulong seed)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(seed, out var w)) return w;
            var dir = ContinentSettings.FindBaseContent();
            var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = seed;
            var hs = HistorySettings.LoadDefaults(dir); hs.Seed = seed;
            var rs = RegionSettings.LoadDefaults(dir); rs.Seed = seed;
            var c = ContinentGenerator.Generate(cs);
            var h = HistoryGenerator.Generate(c, hs);
            var pick = EmbarkSite.AutoPick(c, h);
            var sw = Stopwatch.StartNew();
            var r = RegionGenerator.Generate(c, h, pick.CellX, pick.CellY, rs);
            sw.Stop();
            return Cache[seed] = new World(c, h, pick, r, sw.ElapsedMilliseconds);
        }
    }

    public static IEnumerable<object[]> SeedCases() => Seeds.Select(s => new object[] { s });

    [Fact]
    public void Defaults_load_from_content_pipeline()
    {
        var s = RegionSettings.LoadDefaults(ContinentSettings.FindBaseContent());
        Assert.Equal(2400, s.ReliefM);
        Assert.Equal(23, s.AcresPerPersonX10);
        Assert.Equal(40, s.VillageMin);
        Assert.Equal(300, s.FarmsteadMax);
    }

    [Fact]
    public void Same_inputs_same_hash()
    {
        var w = Get(1);
        var rs = RegionSettings.LoadDefaults(ContinentSettings.FindBaseContent()); rs.Seed = 1;
        var again = RegionGenerator.Generate(w.C, w.H, w.Pick.CellX, w.Pick.CellY, rs);
        Assert.Equal(w.R.ComputeHash(), again.ComputeHash());
    }

    [Fact]
    public void Different_seeds_differ()
        => Assert.NotEqual(Get(1).R.ComputeHash(), Get(2).R.ComputeHash());

    [Theory, MemberData(nameof(SeedCases))]
    public void Counts_within_plan_ranges(ulong seed)
    {
        var w = Get(seed); var r = w.R;
        int towns = r.Towns.Count(), villages = r.Villages.Count(), farms = r.Farmsteads.Count();
        double acresPerPerson = r.FieldAcres / (double)r.TotalPopulation;
        output.WriteLine($"seed {seed} cell ({r.CellX},{r.CellY}): towns {towns}, villages {villages}, farmsteads {farms}, pop {r.TotalPopulation}, households {r.TotalHouseholds}, field acres {r.FieldAcres} ({acresPerPerson:0.00}/person), fiefs {r.Fiefs.Count}, realms {r.Realms.Count}, gen {w.GenMs} ms");
        output.WriteLine($"  villages pop min/max {r.Villages.Min(v => v.TargetPop)}/{r.Villages.Max(v => v.TargetPop)}; town pops {string.Join(",", r.Towns.Select(t => t.TargetPop))}");
        Assert.InRange(towns, 1, 4);
        Assert.InRange(villages, 40, 80);
        Assert.InRange(farms, 150, 300);
        Assert.InRange(r.TotalPopulation, 15_000, 35_000);
        Assert.InRange(acresPerPerson, 2.3 * 0.75, 2.3 * 1.25);
        Assert.InRange(r.Realms.Count, 1, 3);
        Assert.All(r.Villages, v => Assert.InRange(v.TargetPop, 40, 330));
        Assert.True(w.GenMs < 20_000, $"generation took {w.GenMs} ms");
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void Every_river_reaches_the_edge_or_a_lake(ulong seed)
    {
        var r = Get(seed).R;
        Assert.NotEmpty(r.Waterways.Where(w => w.Kind == WaterKind.River));
        foreach (var w in r.Waterways.Where(w => w.Kind == WaterKind.River))
        {
            var cur = w; int hops = 0;
            while (cur.End is WaterEnd.Joins or WaterEnd.Continues)
            {
                Assert.InRange(cur.EndRef, 0, r.Waterways.Count - 1);
                cur = r.Waterways[cur.EndRef];
                Assert.True(++hops < 10_000, "waterway cycle");
            }
            Assert.True(cur.End is WaterEnd.Edge or WaterEnd.Lake or WaterEnd.Sea, $"river {w.Id} ends {cur.End}");
            if (cur.End == WaterEnd.Edge)
            {
                // The last tile of an edge-ending chain drains off the region.
                int last = cur.Tiles[^1];
                Assert.Equal(-1, r.FlowTo[last]);
            }
        }
        // River and stream widths are within the plan's ranges.
        foreach (var w in r.Waterways.Where(w => w.Kind == WaterKind.River))
            Assert.All(w.WidthCm, c => Assert.InRange(c, 1500, 4000));
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void Roads_connect_every_settlement_with_a_loop(ulong seed)
    {
        var r = Get(seed).R;
        int n = r.RoadNodes.Count;
        var parent = Enumerable.Range(0, n).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        int cycles = 0;
        foreach (var e in r.RoadEdges)
        {
            int a = Find(e.A), b = Find(e.B);
            if (a == b) cycles++; else parent[a] = b;
            Assert.True(e.Points.Length >= 2);
        }
        int comps = Enumerable.Range(0, n).Select(Find).Distinct().Count();
        output.WriteLine($"seed {seed}: road nodes {n}, edges {r.RoadEdges.Count}, components {comps}, independent loops {cycles}, gates {r.RoadNodes.Count(q => q.Kind == RoadNodeKind.Gate)}");
        Assert.Equal(1, comps);
        Assert.True(cycles >= 1, "road graph is a tree");
        Assert.All(r.Settlements, s => Assert.InRange(s.RoadNode, 0, n - 1));
        // Crossings are marked where roads meet rivers.
        foreach (var c in r.RoadEdges.SelectMany(e => e.Crossings)) Assert.True(c.Kind is CrossingKind.Ford or CrossingKind.Bridge);
        // Town-to-town road lengths (reported; history spacing sets them).
        var tt = r.RoadEdges.Where(e => r.RoadNodes[e.A].Kind == RoadNodeKind.Town && r.RoadNodes[e.B].Kind == RoadNodeKind.Town).ToList();
        output.WriteLine("  town-to-town road km: " + string.Join(", ", tt.Select(e => $"{e.LengthM / 1000.0:0.0}")));
        Assert.All(tt, e => Assert.Equal(RoadClass.Highway, e.Class));
        Assert.All(tt, e => Assert.InRange(e.LengthM, 4_000, 45_000));
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void History_sites_inside_the_region_are_placed_with_their_events(ulong seed)
    {
        var w = Get(seed); var r = w.R;
        bool Inside(int cell) { int x = cell % Continent.Size - r.CellX, y = cell / Continent.Size - r.CellY; return x >= 0 && x < 16 && y >= 0 && y < 16; }
        var ruins = w.H.Settlements.Where(s => s.IsRuin && Inside(s.Cell)).ToList();
        output.WriteLine($"seed {seed}: history ruins in region {ruins.Count}; sites {string.Join(' ', r.Sites.GroupBy(s => s.Kind).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}"))}");
        foreach (var ruin in ruins)
        {
            var site = Assert.Single(r.Sites, s => s.Kind == SiteKind.Ruin && s.HistorySettlement == ruin.Id);
            Assert.Equal(ruin.RuinEvent, site.HistoryEvent);
            Assert.NotEqual(EntityId.None, site.HistoryEvent);
            Assert.NotNull(w.H.Chronicle.Get(site.HistoryEvent));
            Assert.InRange(site.X, 0, Region.Meters); Assert.InRange(site.Y, 0, Region.Meters);
        }
        foreach (var b in w.H.Battlefields.Where(b => Inside(b.Cell)))
            Assert.Contains(r.Sites, s => s.Kind == SiteKind.Battlefield && s.HistoryEvent == b.Event);
        foreach (var a in w.H.Artefacts.Where(a => a.Status != ArtefactStatus.Held && a.Cell >= 0 && Inside(a.Cell)))
            Assert.Contains(r.Sites, s => s.Kind == SiteKind.Hoard && s.Artefact == a.Id);
        int caves = r.Sites.Count(s => s.Kind == SiteKind.Cave), barrows = r.Sites.Count(s => s.Kind == SiteKind.Barrow);
        Assert.InRange(caves, 10, 20);
        Assert.InRange(barrows, 3, 8);
        Assert.InRange(r.Sites.Count(s => s.MonsterLair), 1, 3);
        Assert.All(r.Sites.Where(s => s.MonsterLair), s => Assert.True(s.Levels >= 2));
        foreach (var m in r.Sites.Where(s => s.Kind == SiteKind.Mine))
            Assert.NotEqual(0, r.Mineral[Region.Index(m.TileX, m.TileY)]);
        // Start-safety data is exposed.
        Assert.Equal(int.MaxValue == r.NearestActiveWarM(16000, 16000), r.ActiveWars.Count == 0);
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void Settlement_plans_have_square_church_manor_and_blueprints(ulong seed)
    {
        var r = Get(seed).R;
        // Every lot names a blueprint that exists in the base content (or none, for plots without a building yet).
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var db = new Ravensguard.Content.ModLoader(Ravensguard.Content.ContentTypeRegistry.Default).Load([Path.Combine(root, "content", "base")]).Database;
        var known = db.All<Ravensguard.Content.BlueprintDef>().Select(b => b.Id).Append("").ToHashSet();
        Assert.Contains("parish_church", known);
        Assert.Contains("watermill", known);
        int mills = 0;
        for (int i = 0; i < r.Settlements.Count; i++)
        {
            var s = r.Settlements[i]; var p = s.Plan;
            Assert.All(p.Lots, l => Assert.Contains(l.BlueprintId, known));
            Assert.All(p.Lots.Where(l => l.IsDwelling), l => Assert.NotEqual("", l.BlueprintId));
            Assert.All(p.Lots, l => { Assert.InRange(l.Rect.X, 0, Region.Meters); Assert.InRange(l.Rect.Bottom, 0, Region.Meters); Assert.Equal(l.Index, p.Lots.IndexOf(l)); });
            if (p.MillLot >= 0) { mills++; Assert.Equal(LotUse.Mill, p.Lots[p.MillLot].Use); Assert.True(s.OnRiver); }
            if (s.Class == SettlementClass.Farmstead) { Assert.Equal(1, s.Households); Assert.Equal(1, p.DwellingCount); continue; }
            Assert.True(p.Square.W > 0);
            Assert.True(p.ChurchLot >= 0, $"{s.Name} has no church or chapel");
            Assert.Equal(s.Class == SettlementClass.Town ? LotUse.Church : LotUse.Chapel, p.Lots[p.ChurchLot].Use);
            Assert.InRange(p.DwellingCount, (int)(s.Households * 0.95) - 1, s.Households + 1);
            bool seat = s.FiefIndex >= 0 && r.Fiefs[s.FiefIndex].SeatSettlement == i;
            if (s.Class == SettlementClass.Town) Assert.True(seat);
            var homes = p.Lots.Where(l => l.Dwelling is DwellingType.Cottage or DwellingType.Longhouse or DwellingType.Burgage).ToList();
            if (s.Class == SettlementClass.Town) Assert.True(homes.Count(l => l.Dwelling == DwellingType.Burgage) * 2 >= homes.Count, $"{s.Name}: mostly burgage plots");
            else { Assert.DoesNotContain(homes, l => l.Dwelling == DwellingType.Burgage); Assert.Contains(homes, l => l.Dwelling == DwellingType.Cottage); }
            // Lots never overlap each other.
            for (int a = 0; a < p.Lots.Count; a++)
                for (int b = a + 1; b < p.Lots.Count; b++)
                    Assert.False(p.Lots[a].Rect.Overlaps(p.Lots[b].Rect), $"{s.Name}: lots {a} and {b} overlap");
        }
        output.WriteLine($"seed {seed}: mills {mills}, manors {r.Fiefs.Count(f => f.ManorLot >= 0)}/{r.Fiefs.Count}");
        Assert.All(r.Fiefs, f => { Assert.NotEmpty(f.Settlements); Assert.InRange(f.Settlements.Count, 1, 40); });
        Assert.True(r.Fiefs.Count(f => f.ManorLot >= 0) >= r.Fiefs.Count * 9 / 10);
        // Settlement ids are unique.
        Assert.Equal(r.Settlements.Count, r.Settlements.Select(s => s.Id).Distinct().Count());
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void Fields_are_parcels_with_owners_and_a_rotation(ulong seed)
    {
        var r = Get(seed).R;
        Assert.Equal(r.Fields.Length, r.Fields.Select(f => f.Tile).Distinct().Count());
        Assert.Equal(r.Fields.Length, r.Parcels.Sum(p => p.FieldCount));
        foreach (var f in r.Fields)
        {
            Assert.Equal(f.Parcel + 1, r.ParcelMap[f.Tile]);
            Assert.Equal(0, r.Water[f.Tile]);
            Assert.True(f.Fertility > 0);
            Assert.Equal(f.Slot == 2 ? Crop.Fallow : f.Crop, f.Crop);
        }
        var slots = r.Fields.GroupBy(f => f.Slot).ToDictionary(g => g.Key, g => g.Count());
        output.WriteLine($"seed {seed}: parcels {r.Parcels.Count}, tiles/parcel {r.Fields.Length / (double)r.Parcels.Count:0.0}, slots {string.Join('/', Enumerable.Range(0, 3).Select(i => slots.GetValueOrDefault((byte)i)))}");
        foreach (var s in r.Settlements.Where(s => s.Class == SettlementClass.Village && s.FieldAcres > 200))
        {
            var byslot = s.Parcels.Select(i => r.Parcels[i]).GroupBy(p => p.Slot).ToDictionary(g => g.Key, g => g.Sum(p => p.FieldCount));
            for (byte k = 0; k < 3; k++) Assert.InRange(byslot.GetValueOrDefault(k), s.FieldAcres / 5, s.FieldAcres / 2);
        }
        Assert.All(r.Parcels, p => Assert.InRange(p.FieldCount, 1, 40));
        Assert.Contains(r.Parcels, p => p.Owner.Kind == OwnerKind.Demesne);
        Assert.Contains(r.Parcels, p => p.Owner.Kind == OwnerKind.Tenant);
        Assert.Contains(r.Parcels, p => p.Owner.Kind == OwnerKind.Burgher);
        foreach (var p in r.Parcels.Where(p => p.Owner.Kind is OwnerKind.Tenant or OwnerKind.Burgher))
            Assert.True(r.Settlements[p.Settlement].Plan.Lots[p.Owner.LotIndex].IsDwelling);
        // Per settlement, fields lie near it (plan: within about 1 km for villages).
        var vs = r.Settlements.Where(s => s.Class == SettlementClass.Village && s.FieldAcres > 0).ToList();
        double meanDist = vs.Average(s => r.Parcels.Where(p => p.Settlement == r.Settlements.IndexOf(s)).Average(p => Math.Sqrt(Math.Pow((p.TileX - s.TileX) * 64.0, 2) + Math.Pow((p.TileY - s.TileY) * 64.0, 2))));
        output.WriteLine($"  mean parcel distance from a village {meanDist:0} m");
        Assert.InRange(meanDist, 300, 1800);
    }

    [Fact]
    public void Heights_are_consistent_across_overlapping_regions()
    {
        var w = Get(1);
        var dir = ContinentSettings.FindBaseContent();
        var rs = RegionSettings.LoadDefaults(dir); rs.Seed = 1;
        int cx = Math.Min(w.Pick.CellX + 8, Continent.Size - 16), cy = w.Pick.CellY;
        var b = RegionGenerator.Generate(w.C, w.H, cx, cy, rs);
        int shift = (cx - w.Pick.CellX) * RegionSettings.CellM / RegionSettings.TileM;
        int checkedTiles = 0;
        for (int y = 0; y < Region.Size; y += 7)
            for (int x = shift; x < Region.Size; x += 5)
            {
                Assert.Equal(w.R.HeightCm[Region.Index(x, y)], b.HeightCm[Region.Index(x - shift, y)]);
                checkedTiles++;
            }
        Assert.True(checkedTiles > 1000);
    }

    [Fact]
    public void Edges_follow_the_coarse_continental_cells()
    {
        // The region mean over a continental cell stays close to the coarse elevation it was refined from.
        var w = Get(2); var r = w.R;
        int sea = w.C.SeaLevelRaw;
        int checkedCells = 0;
        for (int dy = 0; dy < 16; dy += 5)
            for (int dx = 0; dx < 16; dx += 5)
            {
                int ci = Continent.Index(r.CellX + dx, r.CellY + dy);
                if (w.C.Ocean[ci]) continue;
                long sum = 0; int cnt = 0;
                for (int ty = dy * 31 + 4; ty < dy * 31 + 27 && ty < Region.Size; ty++)
                    for (int tx = dx * 31 + 4; tx < dx * 31 + 27 && tx < Region.Size; tx++) { sum += r.HeightCm[Region.Index(tx, ty)]; cnt++; }
                long mean = sum / cnt;
                long xq = (long)(w.C.Elevation[ci] - sea) * 65536 / (65536 - sea);
                long expect = (((xq * xq) >> 16) * 3 / 4 + xq / 4) * 2400 * 100 >> 16;
                Assert.InRange(mean, expect - 1500 - expect / 4, expect + 1500 + expect / 4);
                checkedCells++;
            }
        Assert.True(checkedCells >= 8);
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void Embark_pick_prefers_one_to_three_towns_a_river_and_clear_of_war(ulong seed)
    {
        var w = Get(seed);
        output.WriteLine($"seed {seed}: pick {w.Pick}");
        Assert.False(w.Pick.Excluded);
        Assert.InRange(w.Pick.Towns, 1, 4);
        Assert.True(w.Pick.RiverCells > 0);
        Assert.True(w.Pick.BiomeCount >= 2);
        Assert.True(w.Pick.NearestWarKm < 0 || w.Pick.NearestWarKm >= 10);
        Assert.Equal(w.Pick.Total, EmbarkSite.Score(w.C, w.H, w.Pick.CellX, w.Pick.CellY).Total);
        Assert.Equal(w.Pick, EmbarkSite.AutoPick(w.C, w.H));
        // A window at the continent edge or in the sea scores as excluded.
        Assert.True(EmbarkSite.Score(w.C, w.H, -1, 0).Excluded);
        Assert.True(EmbarkSite.Score(w.C, w.H, 245, 245).Excluded);
        Assert.True(w.Pick.Total > EmbarkSite.Score(w.C, w.H, 0, 0).Total);
    }

    [Fact]
    public void Terrain_layers_are_sane()
    {
        var r = Get(1).R;
        Assert.Equal(Region.Size * Region.Size, r.HeightCm.Length);
        Assert.All(r.Fertility, f => Assert.InRange((int)f, 0, 100));
        Assert.All(r.Forest, f => Assert.InRange((int)f, 0, 100));
        Assert.All(r.Drainage, f => Assert.InRange((int)f, 0, 100));
        for (int t = 0; t < Region.Tiles; t++)
            if ((r.Water[t] & (WaterFlags.Lake | WaterFlags.Sea)) != 0) Assert.Equal(0, r.Fertility[t]);
        Assert.Contains(r.Biomes, b => b == Biome.Grassland || b == Biome.TemperateForest);
        // Every settlement stands on dry land.
        Assert.All(r.Settlements, s => Assert.Equal(0, r.Water[Region.Index(s.TileX, s.TileY)] & (WaterFlags.Sea | WaterFlags.Lake)));
    }
}
