using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Building;

public class BlueprintWriterTests(BuildingFixture fx, ITestOutputHelper output) : IClassFixture<BuildingFixture>
{
    private BuildingLibrary Lib => fx.Lib;

    private string Write(BlueprintEntry e, BuildingGrid g) => BlueprintWriter.Write(e.Def, g, Lib.Pieces, Lib.Styles);

    private static string TempMod(string text)
    {
        var dir = Path.Combine(Path.GetTempPath(), "rg_bpw_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "mod.json"), "{ \"id\": \"bpw_test\", \"name\": \"t\", \"dependencies\": [\"base\"] }");
        File.WriteAllText(Path.Combine(dir, "bp.jsonc"), text);
        return dir;
    }

    private BuildingLibrary Reload(string text, out string dir)
    {
        dir = TempMod(text);
        var load = new ModLoader(ContentTypeRegistry.Default).Load([fx.BaseDir, dir]);
        Assert.True(load.Success, load.Report.ToString());
        return BuildingLibrary.FromDatabase(load.Database);
    }

    private static void AssertSameGrid(BuildingGrid a, BuildingGrid b, string id)
    {
        Assert.True(a.ContentEquals(b), $"{id}: grid differs after round trip");
        Assert.Equal(a.Rooms.OrderBy(p => p.Key).ToList(), b.Rooms.OrderBy(p => p.Key).ToList());
        Assert.Equal(a.Levels.Keys, b.Levels.Keys);
    }

    [Fact]
    public void EveryBaseBlueprintRoundTripsThroughTheLoader()
    {
        var entries = Lib.Blueprints.ToList();
        var text = BlueprintWriter.WriteFile(["round trip"], entries.Select(e => Write(e, Lib.Parse(e, [])!)));
        var lib2 = Reload(text, out var dir);
        try
        {
            foreach (var e in entries)
            {
                var g1 = Lib.Parse(e, [])!;
                var e2 = lib2.Entry(e.Def.Id);
                Assert.Equal(e.Def.Name, e2.Def.Name);
                Assert.Equal(e.Def.Wealth, e2.Def.Wealth);
                Assert.Equal(e.Def.Kind, e2.Def.Kind);
                Assert.Equal(e.Def.Style, e2.Def.Style);
                Assert.Equal(e.Def.Dwelling, e2.Def.Dwelling);
                var issues = new List<BlueprintIssue>();
                var g2 = lib2.Parse(e2, issues);
                Assert.True(g2 != null, string.Join("\n", issues));
                AssertSameGrid(g1, g2!, e.Def.Id);
                Assert.Empty(BlueprintValidator.Validate(e2, lib2).Where(i => !BlueprintValidator.Validate(e, Lib).Any(j => j.Code == i.Code)));
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void WritingIsIdempotentAndKeepsStyleTokens()
    {
        foreach (var e in Lib.Blueprints)
        {
            var g = Lib.Parse(e, [])!;
            string t1 = Write(e, g);
            var lib2 = Reload(BlueprintWriter.WriteFile(["x"], [t1]), out var dir);
            try
            {
                var e2 = lib2.Entry(e.Def.Id);
                string t2 = BlueprintWriter.Write(e2.Def, lib2.Parse(e2, [])!, lib2.Pieces, lib2.Styles);
                Assert.Equal(t1, t2);
                Assert.Equal(e.Def.Legend.Values.Count(v => v.StartsWith('$')) > 0, e2.Def.Legend.Values.Any(v => v.StartsWith('$')));
            }
            finally { Directory.Delete(dir, true); }
        }
    }

    [Fact]
    public void SourceTextInCanonicalFormatIsReproducedExactly()
    {
        var dir = Path.Combine(fx.BaseDir, "blueprints");
        int total = 0, same = 0;
        var diffs = new List<string>();
        foreach (var file in Directory.GetFiles(dir, "*.jsonc"))
        {
            var src = File.ReadAllText(file);
            foreach (var e in Lib.Blueprints.Where(b => b.Location is { } l && Path.GetFileName(l.File) == Path.GetFileName(file)))
            {
                total++;
                string orig = BlueprintWriter.EntryText(src, e.Def.Id);
                string now = Write(e, Lib.Parse(e, [])!).TrimStart();
                if (orig == now) same++;
                else { diffs.Add(e.Def.Id); var a = orig.Split('\n'); var b = now.Split('\n'); int k = 0; while (k < a.Length && k < b.Length && a[k] == b[k]) k++; output.WriteLine($"{e.Def.Id}: line {k}\n  src: {(k < a.Length ? a[k] : "<end>")}\n  out: {(k < b.Length ? b[k] : "<end>")}"); }
            }
        }
        output.WriteLine($"{same}/{total} base entries are byte-identical; differing: {string.Join(", ", diffs)}");
        Assert.True(total >= 60, $"only {total} entries matched to files");
        Assert.True(same >= total * 0.85, $"only {same}/{total} entries reproduce byte for byte");
    }

    [Fact]
    public void UpsertReplacesOneEntryAndKeepsComments()
    {
        var e = Lib.Entry("hovel");
        var g = Lib.Parse(e, [])!;
        const string src = "// header\n[\n  // first\n  { \"type\": \"blueprint\", \"id\": \"hovel\", \"name\": \"Old\", \"legend\": {}, \"rows\": [\"#\"] },\n\n  // second\n  { \"type\": \"blueprint\", \"id\": \"other\", \"name\": \"O\", \"legend\": {}, \"rows\": [\"#\"] }, // tail\n]\n";
        var meta = e.Def with { Name = "New hovel" };
        string replaced = BlueprintWriter.Upsert(src, "hovel", BlueprintWriter.Write(meta, g, Lib.Pieces, Lib.Styles));
        Assert.Contains("// header", replaced);
        Assert.Contains("// first", replaced);
        Assert.Contains("// second", replaced);
        Assert.Contains("// tail", replaced);
        Assert.Contains("\"id\": \"other\", \"name\": \"O\"", replaced);
        Assert.Contains("\"name\": \"New hovel\"", replaced);
        Assert.DoesNotContain("\"name\": \"Old\"", replaced);

        var meta2 = e.Def with { Id = "hovel2", Name = "Second" };
        string appended = BlueprintWriter.Upsert(src, "hovel2", BlueprintWriter.Write(meta2, g, Lib.Pieces, Lib.Styles));
        Assert.NotNull(BlueprintWriter.FindEntry(appended, "hovel2"));
        Assert.NotNull(BlueprintWriter.FindEntry(appended, "other"));
        Assert.Contains("// tail", appended);
    }

    [Fact]
    public void EditedGridWithNewPiecesGetsLegendKeysAndTokens()
    {
        var e = Lib.Entry("hovel");
        var g = Lib.Parse(e, [])!.Clone();
        var lv = g.Level(0)!;
        lv.Structure[lv.Idx(1, 1)] = "floor_flagstone"; lv.Letter[lv.Idx(1, 1)] = (char)0; // not in the old legend
        lv.Structure[lv.Idx(2, 1)] = "wall_stone"; lv.Letter[lv.Idx(2, 1)] = (char)0;
        lv.Fixture[lv.Idx(1, 1)] = "bed";
        var def = BlueprintWriter.ToDef(e.Def, g, Lib.Pieces, Lib.Styles);
        var issues = new List<BlueprintIssue>();
        var back = Lib.Parse(BlueprintEntry.Of(def), issues);
        Assert.True(back != null, string.Join("\n", issues));
        AssertSameGrid(g, back!, "hovel*");
        Assert.Equal(def.Legend.Count, def.Legend.Keys.Distinct().Count());
    }

    [Fact]
    public void RoomLetterOnTwoPiecesIsSplitIntoTwoLetters()
    {
        var e = Lib.Entry("hovel");
        var g = Lib.Parse(e, [])!.Clone();
        var lv = g.Level(0)!;
        lv.Structure[lv.Idx(1, 1)] = "floor_flagstone"; // 'k' now sits on two floors
        var def = BlueprintWriter.ToDef(e.Def, g, Lib.Pieces, Lib.Styles);
        var back = Lib.Parse(BlueprintEntry.Of(def), new List<BlueprintIssue>());
        Assert.NotNull(back);
        Assert.Equal(2, back!.Rooms.Count);
        Assert.All(back.Rooms.Values, p => Assert.Equal("hearth", p));
        Assert.Equal(g.Level(0)!.Structure, back.Level(0)!.Structure);
    }

    [Fact]
    public void RoomDetectionAndValidationSurviveTheRoundTrip()
    {
        foreach (var e in Lib.Blueprints.Take(80))
        {
            var g = Lib.Parse(e, [])!;
            var def = BlueprintWriter.ToDef(e.Def, g, Lib.Pieces, Lib.Styles);
            var e2 = BlueprintEntry.Of(def);
            var a = BlueprintValidator.Validate(e, Lib).Select(i => i.Code).OrderBy(c => c).ToList();
            var b = BlueprintValidator.Validate(e2, Lib).Select(i => i.Code).OrderBy(c => c).ToList();
            Assert.Equal(a, b);
        }
    }
}
