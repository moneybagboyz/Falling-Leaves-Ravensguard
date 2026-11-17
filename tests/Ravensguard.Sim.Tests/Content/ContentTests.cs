using Ravensguard.Content;

namespace Ravensguard.Sim.Tests.Content;

[ContentType("widget")]
public sealed record WidgetDef : ContentDef
{
    public required string Name { get; init; }
    [ContentRef(typeof(MaterialDef))] public string? Material { get; init; }
    [ContentRef(typeof(MaterialDef))] public List<string> Extras { get; init; } = [];
}

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rg_" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public string Mod(string id, string? content = null, string? manifestExtra = null, string file = "a.jsonc")
    {
        var dir = System.IO.Path.Combine(Path, id);
        Directory.CreateDirectory(System.IO.Path.Combine(dir, "sub"));
        File.WriteAllText(System.IO.Path.Combine(dir, "mod.json"),
            $"{{ \"id\": \"{id}\", \"name\": \"{id}\", \"version\": \"1.0\"{(manifestExtra == null ? "" : ", " + manifestExtra)} }}");
        if (content != null) File.WriteAllText(System.IO.Path.Combine(dir, "sub", file), content);
        return dir;
    }
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}

public class ContentTests : IDisposable
{
    private readonly TempDir _t = new();
    public void Dispose() => _t.Dispose();

    private static ContentTypeRegistry Reg() =>
        new ContentTypeRegistry().Register(typeof(MaterialDef)).Register(typeof(WidgetDef));

    private static string Mat(string id, double d = 1000, string extra = "") =>
        $"{{ \"type\": \"material\", \"id\": \"{id}\", \"name\": \"{id}\", \"density_kg_m3\": {d}, \"value_per_kg\": 1 {extra} }}";

    private ModLoadResult Load(ContentVocabulary? v = null, string? cache = null, params string[] mods) =>
        new ModLoader(Reg(), v).Load(mods, cache);

    [Fact]
    public void CommentsAndTrailingCommasParse()
    {
        var m = _t.Mod("a", "// c\n[\n /* b */ " + Mat("iron") + ",\n " + Mat("oak") + ",\n]\n");
        var r = Load(null, null, m);
        Assert.True(r.Success, r.Report.ToString());
        Assert.Equal(1000, r.Database.Get<MaterialDef>("iron").DensityKgM3);
        Assert.Equal(2, r.Database.All<MaterialDef>().Count());
    }

    [Fact]
    public void SampleBaseContentLoads()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var baseDir = Path.Combine(root, "content", "base");
        var r = new ModLoader(ContentTypeRegistry.Default).Load([baseDir]);
        Assert.True(r.Success, r.Report.ToString());
        Assert.Equal(2, r.Database.All<MaterialDef>().Count());
    }

    [Fact]
    public void SyntaxErrorReportsLineAndColumn()
    {
        var m = _t.Mod("a", "[\n  { \"type\": \"material\",\n    \"id\": ,\n  }\n]");
        var r = Load(null, null, m);
        var e = Assert.Single(r.Report.Errors);
        Assert.EndsWith("a.jsonc", e.Location!.Value.File);
        Assert.Equal(3, e.Location.Value.Line);
    }

    [Fact]
    public void SemanticErrorReportsObjectLocation()
    {
        var m = _t.Mod("a", "[\n" + Mat("iron") + ",\n\n  { \"type\": \"material\", \"id\": \"bad\", \"name\": \"x\" }\n]");
        var r = Load(null, null, m);
        var e = Assert.Single(r.Report.Errors);
        Assert.Equal(4, e.Location!.Value.Line);
        Assert.Equal(3, e.Location.Value.Column);
        Assert.Contains("bad", e.Message);
    }

    [Fact]
    public void DuplicateIdInOneModIsError()
    {
        var m = _t.Mod("a", "[\n" + Mat("iron") + ",\n" + Mat("iron") + "\n]");
        var r = Load(null, null, m);
        var e = Assert.Single(r.Report.Errors);
        Assert.Contains("Duplicate", e.Message);
        Assert.Equal(3, e.Location!.Value.Line);
    }

    [Fact]
    public void UnknownTypeIsError()
    {
        var m = _t.Mod("a", "[ { \"type\": \"banana\", \"id\": \"x\" } ]");
        var r = Load(null, null, m);
        Assert.Contains(r.Report.Errors, e => e.Message.Contains("Unknown content type 'banana'"));
    }

    [Fact]
    public void UnmappedFieldIsError()
    {
        var m = _t.Mod("a", "[ " + Mat("iron", extra: ", \"densty\": 1") + " ]");
        Assert.True(Load(null, null, m).Report.HasErrors);
    }

    [Fact]
    public void DanglingContentRefIsError_AndAllErrorsCollected()
    {
        var m = _t.Mod("a", "[\n" + Mat("iron") + ",\n" +
            "{ \"type\": \"widget\", \"id\": \"w1\", \"name\": \"W\", \"material\": \"nope\", \"extras\": [\"iron\", \"gone\"] },\n" +
            "{ \"type\": \"widget\", \"id\": \"w2\", \"name\": \"W\", \"material\": \"iron\" }\n]");
        var r = Load(null, null, m);
        Assert.Equal(2, r.Report.Errors.Count());
        Assert.All(r.Report.Errors, e => Assert.Equal(3, e.Location!.Value.Line));
        Assert.Contains(r.Report.Errors, e => e.Message.Contains("'nope'"));
        Assert.Contains(r.Report.Errors, e => e.Message.Contains("'gone'"));
    }

    [Fact]
    public void OverrideAndConflictReport()
    {
        var a = _t.Mod("a", "[" + Mat("iron", 7000) + "]");
        var b = _t.Mod("b", "[" + Mat("iron", 8000) + "]", "\"dependencies\": [\"a\"]");
        var r = Load(null, null, b, a);
        Assert.True(r.Success, r.Report.ToString());
        Assert.Equal(8000, r.Database.Get<MaterialDef>("iron").DensityKgM3);
        var o = Assert.Single(r.Database.Overrides);
        Assert.Equal(("material", "iron", "a", "b"), (o.Type, o.Id, o.PreviousMod, o.NewMod));
        Assert.Contains("'b' overrides 'a'", r.Database.ConflictReport());
    }

    [Fact]
    public void PatchMergesTopLevelFields()
    {
        var a = _t.Mod("a", "[" + Mat("iron", 7000) + "]");
        var b = _t.Mod("b", "[{ \"type\": \"patch\", \"target_type\": \"material\", \"target_id\": \"iron\", \"set\": { \"density_kg_m3\": 1234 } }]",
            "\"load_after\": [\"a\"]");
        var r = Load(null, null, a, b);
        Assert.True(r.Success, r.Report.ToString());
        var m = r.Database.Get<MaterialDef>("iron");
        Assert.Equal(1234, m.DensityKgM3);
        Assert.Equal("iron", m.Name);
        Assert.Empty(r.Database.Overrides);
    }

    [Fact]
    public void PatchToMissingTargetIsError()
    {
        var a = _t.Mod("a", "[{ \"type\": \"patch\", \"target_type\": \"material\", \"target_id\": \"zzz\", \"set\": {} }]");
        Assert.True(Load(null, null, a).Report.HasErrors);
    }

    [Fact]
    public void MigrationResolvesChainsAndGet()
    {
        var m = _t.Mod("a", "[" + Mat("steel") + ",\n" +
            "{ \"type\": \"migration\", \"target_type\": \"material\", \"from\": \"old\", \"to\": \"older_new\" },\n" +
            "{ \"type\": \"migration\", \"target_type\": \"material\", \"from\": \"older_new\", \"to\": \"steel\" },\n" +
            "{ \"type\": \"widget\", \"id\": \"w\", \"name\": \"W\", \"material\": \"old\" }]");
        var r = Load(null, null, m);
        Assert.True(r.Success, r.Report.ToString());
        Assert.Equal("steel", r.Database.Resolve("material", "old"));
        Assert.Equal("steel", r.Database.Resolve("material", "steel"));
        Assert.Equal("steel", r.Database.Get<MaterialDef>("old").Id);
    }

    [Fact]
    public void MigrationCycleIsError()
    {
        var m = _t.Mod("a", "[{ \"type\": \"migration\", \"target_type\": \"material\", \"from\": \"x\", \"to\": \"y\" },\n" +
            "{ \"type\": \"migration\", \"target_type\": \"material\", \"from\": \"y\", \"to\": \"x\" }]");
        var r = Load(null, null, m);
        Assert.True(r.Report.HasErrors);
        Assert.Throws<ContentException>(() => r.Database.Resolve("material", "x"));
    }

    private static ModInfo Info(string id, int i, string[]? deps = null, string[]? after = null) =>
        new(new ModManifest { Id = id, Dependencies = [.. deps ?? []], LoadAfter = [.. after ?? []] }, "", i);

    [Fact]
    public void LoadOrderIsStableTopological()
    {
        var order = ModLoader.Sort([Info("a", 0, deps: ["c"]), Info("b", 1), Info("c", 2), Info("d", 3, after: ["a", "missing"])])
            .Select(m => m.Manifest.Id);
        Assert.Equal(["b", "c", "a", "d"], order);
    }

    [Fact]
    public void ModCycleIsError()
    {
        Assert.Throws<ContentException>(() => ModLoader.Sort([Info("a", 0, deps: ["b"]), Info("b", 1, after: ["a"])]));
        var a = _t.Mod("a", null, "\"dependencies\": [\"b\"]");
        var b = _t.Mod("b", null, "\"dependencies\": [\"a\"]");
        var r = Load(null, null, a, b);
        Assert.Contains(r.Report.Errors, e => e.Message.Contains("cycle"));
    }

    [Fact]
    public void MissingDependencyIsError() =>
        Assert.Throws<ContentException>(() => ModLoader.Sort([Info("a", 0, deps: ["nope"])]));

    [Fact]
    public void VocabularyValidation()
    {
        var v = new ContentVocabulary().RegisterCondition("has_tag").RegisterEffect("add_tag");
        var m = _t.Mod("a", "[" + Mat("iron", extra: ", \"x\": 1").Replace(", \"x\": 1", "") + "]");
        Assert.True(Load(v, null, m).Success);

        var reg = new ContentTypeRegistry().Register(typeof(MaterialDef));
        var db = new ContentDatabase(reg);
        var report = new ContentReport();
        var node = System.Text.Json.Nodes.JsonNode.Parse(
            "{ \"conditions\": [ {\"id\":\"has_tag\"}, {\"id\":\"bogus\"} ], \"inner\": { \"effects\": [ {\"id\":\"add_tag\"}, {\"id\":\"explode\"}, 5 ] } }");
        v.Validate(node, new SourceLocation("f", 1, 1), "thing", report);
        Assert.Equal(3, report.Errors.Count());
        Assert.Contains(report.Errors, e => e.Message.Contains("'bogus'"));
        Assert.Contains(report.Errors, e => e.Message.Contains("'explode'"));
    }

    [Fact]
    public void CacheHitAndMissOnChange()
    {
        var cache = Path.Combine(_t.Path, "cache");
        var m = _t.Mod("a", "[" + Mat("iron", 7000) + "]");
        var r1 = Load(null, cache, m);
        Assert.True(r1.Success);
        Assert.False(r1.FromCache);
        Assert.Single(Directory.GetFiles(cache));

        var r2 = Load(null, cache, m);
        Assert.True(r2.FromCache);
        Assert.Equal(7000, r2.Database.Get<MaterialDef>("iron").DensityKgM3);

        File.WriteAllText(Path.Combine(m, "sub", "a.jsonc"), "[" + Mat("iron", 7100) + "]");
        var r3 = Load(null, cache, m);
        Assert.False(r3.FromCache);
        Assert.Equal(7100, r3.Database.Get<MaterialDef>("iron").DensityKgM3);
        Assert.Equal(2, Directory.GetFiles(cache).Length);
        Assert.NotEqual(r1.Hash, r3.Hash);
    }
}
