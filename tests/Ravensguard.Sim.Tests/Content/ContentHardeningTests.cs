using System.Text;
using Ravensguard.Content;

namespace Ravensguard.Sim.Tests.Content;

[ContentType("trigger")]
public sealed record TriggerDef : ContentDef
{
    public List<System.Text.Json.Nodes.JsonObject> Conditions { get; init; } = [];
}

/// <summary>Phase 0 review fixes: manifest parsing and the content cache.</summary>
public class ContentHardeningTests : IDisposable
{
    private readonly TempDir _t = new();
    public void Dispose() => _t.Dispose();

    private static ContentTypeRegistry Reg() =>
        new ContentTypeRegistry().Register(typeof(MaterialDef)).Register(typeof(WidgetDef));

    private static string Mat(string id, string extra = "") =>
        $"{{ \"type\": \"material\", \"id\": \"{id}\", \"name\": \"{id}\", \"density_kg_m3\": 1000, \"value_per_kg\": 1 {extra} }}";

    private string ModWithManifest(string id, byte[] manifest, string? content = null)
    {
        var dir = Path.Combine(_t.Path, id);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "mod.json"), manifest);
        if (content != null) File.WriteAllText(Path.Combine(dir, "a.jsonc"), content);
        return dir;
    }

    // ------------------------------------------------------------------ manifest

    [Fact]
    public void Manifest_WithUtf8Bom_Loads()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("{ \"id\": \"bom\", \"version\": \"1\" }")).ToArray();
        var dir = ModWithManifest("bom", bytes, "[" + Mat("iron") + "]");
        var r = new ModLoader(Reg()).Load([dir]);
        Assert.True(r.Success, r.Report.ToString());
        Assert.Equal(["bom"], r.LoadOrder);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{ \"id\": \"\" }")]
    [InlineData("{ \"id\": \"   \" }")]
    public void Manifest_NullOrEmptyId_IsAnErrorWithLocation(string json)
    {
        var dir = ModWithManifest("bad", Encoding.UTF8.GetBytes(json));
        var r = new ModLoader(Reg()).Load([dir]);
        Assert.False(r.Success);
        var e = Assert.Single(r.Report.Errors);
        Assert.NotNull(e.Location);
        Assert.EndsWith("mod.json", e.Location!.Value.File);
    }

    // ------------------------------------------------------------------ cache

    [Fact]
    public void CacheStoreFailure_IsAWarning_NotAnException()
    {
        var m = _t.Mod("a", "[" + Mat("iron") + "]");
        var cacheAsFile = Path.Combine(_t.Path, "cache");
        File.WriteAllText(cacheAsFile, "not a directory");
        var r = new ModLoader(Reg()).Load([m], cacheAsFile);
        Assert.True(r.Success, r.Report.ToString());
        Assert.Equal(1000, r.Database.Get<MaterialDef>("iron").DensityKgM3);
        Assert.Contains(r.Report.Warnings, w => w.Message.Contains("cache", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CacheStore_FailureCleansUpItsTempFile()
    {
        var m = _t.Mod("a", "[" + Mat("iron") + "]");
        var db = new ModLoader(Reg()).Load([m]).Database;
        var dir = Path.Combine(_t.Path, "cache");
        Directory.CreateDirectory(Path.Combine(dir, "h.cache")); // the target name is taken by a directory: the final move fails
        Assert.ThrowsAny<Exception>(() => new ContentCache(dir).Store("h", db));
        Assert.Empty(Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Where(f => f.EndsWith(".tmp")));
    }

    [Fact]
    public void CacheStore_ConcurrentWritersDoNotCollide()
    {
        var m = _t.Mod("a", "[" + Mat("iron") + "]");
        var db = new ModLoader(Reg()).Load([m]).Database;
        var cache = new ContentCache(Path.Combine(_t.Path, "cache"));
        var errors = new List<Exception>();
        Parallel.For(0, 16, _ => { try { cache.Store("same", db); } catch (Exception e) { lock (errors) errors.Add(e); } });
        Assert.Empty(errors);
        Assert.NotNull(cache.TryLoad("same", Reg()));
    }

    [Fact]
    public void CacheKey_ChangesWithRegistry_SoValidationIsNotSkipped()
    {
        var cache = Path.Combine(_t.Path, "cache");
        var m = _t.Mod("a", "[" + Mat("iron") + ",\n{ \"type\": \"widget\", \"id\": \"w\", \"name\": \"W\" }]");
        var ok = new ModLoader(Reg()).Load([m], cache);
        Assert.True(ok.Success, ok.Report.ToString());
        Assert.True(new ModLoader(Reg()).Load([m], cache).FromCache);

        var narrower = new ContentTypeRegistry().Register(typeof(MaterialDef)); // 'widget' no longer exists
        var r = new ModLoader(narrower).Load([m], cache);
        Assert.False(r.FromCache);
        Assert.False(r.Success);
        Assert.Contains(r.Report.Errors, e => e.Message.Contains("widget"));
    }

    [Fact]
    public void CacheKey_ChangesWithVocabulary_SoValidationIsNotSkipped()
    {
        var cache = Path.Combine(_t.Path, "cache");
        var reg = Reg().Register(typeof(TriggerDef));
        var m = _t.Mod("a", "[{ \"type\": \"trigger\", \"id\": \"t\", \"conditions\": [ { \"id\": \"has_tag\" } ] }]");
        var wide = new ContentVocabulary().RegisterCondition("has_tag");
        var first = new ModLoader(reg, wide).Load([m], cache);
        Assert.True(first.Success, first.Report.ToString());

        var narrow = new ContentVocabulary().RegisterCondition("other");
        var r = new ModLoader(reg, narrow).Load([m], cache);
        Assert.False(r.FromCache);
        Assert.False(r.Success);
        Assert.Contains(r.Report.Errors, e => e.Message.Contains("has_tag"));
    }
}
