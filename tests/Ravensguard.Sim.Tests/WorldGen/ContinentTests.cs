using System.Diagnostics;
using Ravensguard.Sim.WorldGen;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.WorldGen;

public class ContinentTests(ITestOutputHelper output)
{
    private const int N = Continent.Size;
    private static readonly ulong[] Seeds = [1, 2, 1234];

    private static ContinentSettings Settings(ulong seed, ContinentLayout layout)
    {
        var s = ContinentSettings.LoadDefaults(ContinentSettings.FindBaseContent());
        s.Seed = seed; s.Layout = layout;
        return s;
    }

    private static readonly Dictionary<(ulong, ContinentLayout), Continent> Cache = [];
    private static Continent Get(ulong seed, ContinentLayout layout)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue((seed, layout), out var c)) Cache[(seed, layout)] = c = ContinentGenerator.Generate(Settings(seed, layout));
            return c;
        }
    }

    public static IEnumerable<object[]> AllCases() =>
        from l in Enum.GetValues<ContinentLayout>() from s in Seeds select new object[] { s, l };

    [Fact]
    public void Defaults_load_from_content_pipeline()
    {
        var path = ContinentSettings.FindBaseContent();
        Assert.NotNull(path);
        var s = ContinentSettings.LoadDefaults(path);
        Assert.Equal(13, s.PlateCount);
        Assert.Equal(22938, s.SeaLevelRaw);
        Assert.Equal(8, s.CultureNames.Count);
        Assert.Equal(16, s.RealmNames.Count);
    }

    [Theory, MemberData(nameof(AllCases))]
    public void Same_seed_same_hash_and_timing(ulong seed, ContinentLayout layout)
    {
        var sw = Stopwatch.StartNew();
        var a = ContinentGenerator.Generate(Settings(seed, layout));
        sw.Stop();
        var b = ContinentGenerator.Generate(Settings(seed, layout));
        output.WriteLine($"{layout} seed {seed}: generated 256x256 in {sw.ElapsedMilliseconds} ms (incl. JIT on first call), hash {a.ComputeHash()[..16]}");
#if DEBUG
        Assert.True(sw.ElapsedMilliseconds < 30000);
#else
        Assert.True(sw.ElapsedMilliseconds < 5000);
#endif
        Assert.Equal(a.ComputeHash(), b.ComputeHash());
    }

    [Fact]
    public void Different_seed_or_layout_differs()
    {
        Assert.NotEqual(Get(1, ContinentLayout.Continents).ComputeHash(), Get(2, ContinentLayout.Continents).ComputeHash());
        Assert.NotEqual(Get(1, ContinentLayout.Continents).ComputeHash(), Get(1, ContinentLayout.Pangea).ComputeHash());
    }

    [Theory, MemberData(nameof(AllCases))]
    public void Land_fraction_is_sensible(ulong seed, ContinentLayout layout)
    {
        double f = Get(seed, layout).LandFraction();
        var (lo, hi) = layout switch { ContinentLayout.Pangea => (0.35, 0.60), ContinentLayout.Continents => (0.25, 0.50), _ => (0.15, 0.35) };
        output.WriteLine($"{layout} {seed}: land {f:P1}");
        Assert.InRange(f, lo, hi);
    }

    [Theory, MemberData(nameof(AllCases))]
    public void Every_river_and_lake_cell_drains_to_the_sea(ulong seed, ContinentLayout layout)
    {
        var c = Get(seed, layout);
        int checkedCells = 0;
        for (int i = 0; i < Continent.Cells; i++)
        {
            if (c.Ocean[i]) continue;
            if (c.RiverFlow[i] < c.Settings.RiverFlowThreshold && !c.Lake[i]) continue;
            checkedCells++;
            int cur = i, steps = 0;
            while (!c.Ocean[cur] && c.FlowTo[cur] >= 0)
            {
                int next = c.FlowTo[cur];
                Assert.True(c.RiverFlow[next] >= c.RiverFlow[cur], "flow must not decrease downstream");
                cur = next;
                Assert.True(++steps <= Continent.Cells, "flow loop");
            }
            Assert.True(c.Ocean[cur], $"cell {i} dead-ends at {cur} (land)");
        }
        Assert.True(checkedCells > 20);
        Assert.True(c.Rivers.Count > 5);
        foreach (var r in c.Rivers) Assert.True(r.Cells.Length >= 2);
    }

    [Theory, MemberData(nameof(AllCases))]
    public void Temperature_falls_toward_the_pole_and_with_height(ulong seed, ContinentLayout layout)
    {
        var c = Get(seed, layout);
        double Mean(Func<int, bool> pick)
        {
            long sum = 0; int n = 0;
            for (int i = 0; i < Continent.Cells; i++) if (!c.Ocean[i] && pick(i)) { sum += c.Temperature[i]; n++; }
            return n == 0 ? double.NaN : sum / (double)n;
        }
        double north = Mean(i => i / N < 90), south = Mean(i => i / N >= 166);
        if (!double.IsNaN(north) && !double.IsNaN(south)) Assert.True(north < south - 50, $"north {north} south {south}");
        // Same latitude band: high ground is colder than lowland.
        for (int band = 0; band < 4; band++)
        {
            int y0 = band * 64;
            double hi = Mean(i => i / N >= y0 && i / N < y0 + 64 && AbovePct(c, i) >= 20);
            double lo = Mean(i => i / N >= y0 && i / N < y0 + 64 && AbovePct(c, i) < 5);
            if (!double.IsNaN(hi) && !double.IsNaN(lo)) Assert.True(hi < lo - 20, $"band {band}: high {hi} low {lo}");
        }
    }

    private static int AbovePct(Continent c, int i) => (int)((long)Math.Max(0, c.Elevation[i] - c.SeaLevelRaw) * 100 / (65536 - c.SeaLevelRaw));

    [Theory]
    [InlineData(ContinentLayout.Pangea)]
    [InlineData(ContinentLayout.Continents)]
    public void Rain_shadow_east_of_ranges(ContinentLayout layout)
    {
        long wind = 0, lee = 0; int n = 0;
        foreach (var seed in new ulong[] { 1, 2, 1234, 3, 4, 5 })
        {
            var c = Get(seed, layout);
            for (int y = 0; y < N; y++)
                for (int x = 8; x < N - 12; x++)
                {
                    int i = y * N + x;
                    if (c.Ocean[i] || AbovePct(c, i) < 25) continue;
                    int w = y * N + x - 5, l = y * N + x + 7;
                    if (c.Ocean[w] || c.Ocean[l] || AbovePct(c, w) >= 15 || AbovePct(c, l) >= 15) continue;
                    wind += c.Rainfall[w]; lee += c.Rainfall[l]; n++;
                }
        }
        output.WriteLine($"{layout}: {n} samples, windward {wind / Math.Max(1, n)} mm, lee {lee / Math.Max(1, n)} mm");
        Assert.True(n > 30, "not enough ridge samples");
        Assert.True(lee * 100 < wind * 85, $"lee {lee / n} vs windward {wind / n}");
    }

    [Theory, MemberData(nameof(AllCases))]
    public void Minerals_present_and_strata_separate(ulong seed, ContinentLayout layout)
    {
        var c = Get(seed, layout);
        Assert.True(c.Veins.Count > 100);
        var types = c.Veins.Select(v => v.Type).Distinct().ToHashSet();
        Assert.Contains(MineralType.Iron, types);
        Assert.Contains(MineralType.Copper, types);
        Assert.Contains(MineralType.Stone, types);
        Assert.True(types.Count >= 6, $"only {types.Count} mineral types");
        foreach (var v in c.Veins) { Assert.False(c.Ocean[v.Cell]); Assert.True(v.Tonnage > 0); }
        Assert.True(c.Veins.Select(v => v.Cell).Distinct().Count() > 50);
    }

    [Theory, MemberData(nameof(AllCases))]
    public void Realms_and_cultures_sit_on_good_land(ulong seed, ContinentLayout layout)
    {
        var c = Get(seed, layout);
        var fert = new List<int>();
        for (int i = 0; i < Continent.Cells; i++) if (!c.Ocean[i] && !c.Lake[i]) fert.Add(c.Fertility[i]);
        fert.Sort();
        int median = fert[fert.Count / 2];
        Assert.Equal(c.Settings.RealmCount, c.Realms.Count);
        Assert.InRange(c.Cultures.Count, 3, 8);
        foreach (var r in c.Realms)
        {
            Assert.False(c.Ocean[r.SeedCell]); Assert.False(c.Lake[r.SeedCell]);
            Assert.True(c.Fertility[r.SeedCell] > median, $"realm {r.Name} fertility {c.Fertility[r.SeedCell]} median {median}");
            Assert.Equal(r.Id, c.RealmId[r.SeedCell]);
        }
        foreach (var cu in c.Cultures) { Assert.False(c.Ocean[cu.SeedCell]); Assert.Equal(cu.Id, c.CultureId[cu.SeedCell]); }
        Assert.Equal(c.Realms.Count, c.Realms.Select(r => r.SeedCell).Distinct().Count());
        // Most land is claimed by some realm and some culture.
        int land = 0, claimed = 0;
        for (int i = 0; i < Continent.Cells; i++) if (!c.Ocean[i]) { land++; if (c.RealmId[i] != Continent.None && c.CultureId[i] != Continent.None) claimed++; }
        Assert.True(claimed * 100 > land * 95, $"{claimed}/{land} claimed");
    }

    [Theory, MemberData(nameof(AllCases))]
    public void Biomes_soils_and_layers_are_consistent(ulong seed, ContinentLayout layout)
    {
        var c = Get(seed, layout);
        var biomes = new HashSet<Biome>();
        long valleyFert = 0, landFert = 0; int valleyN = 0, landN = 0;
        for (int i = 0; i < Continent.Cells; i++)
        {
            Assert.Equal(c.Ocean[i], c.Biomes[i] == Biome.Sea);
            Assert.Equal(c.Lake[i], c.Biomes[i] == Biome.Lake);
            Assert.InRange((int)c.Fertility[i], 0, 100);
            if (c.Ocean[i]) { Assert.True(c.CoastDistance[i] <= 0); continue; }
            Assert.True(c.CoastDistance[i] >= 1);
            biomes.Add(c.Biomes[i]);
            landFert += c.Fertility[i]; landN++;
            if (c.Biomes[i] == Biome.RiverValley) { valleyFert += c.Fertility[i]; valleyN++; }
        }
        output.WriteLine($"{layout} {seed}: {biomes.Count} land biomes: {string.Join(',', biomes)}");
        Assert.True(biomes.Count >= 6);
        if (valleyN > 20) Assert.True(valleyFert * landN > landFert * valleyN, "alluvium should make valleys more fertile than average");
        Assert.True(c.Plates.Count == c.Settings.PlateCount);
    }
}
