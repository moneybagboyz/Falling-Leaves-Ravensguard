using MessagePack;
using Ravensguard.Content;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The "tables" section (P5 goods-table fix, no format bump): the string ids of every content table whose dense index a save stores, in index order, so a load can map each
/// saved index onto the content it is loaded with by name (<see cref="ContentRemap"/>). Always written; a save without it was made before it existed and is read with
/// <see cref="LegacyIdTables"/>. A table missing from a present section is taken to match the content (the world that saved it did not know its content, e.g. a bare test world).
/// </summary>
[MessagePackObject]
public sealed class IdTablesSave
{
    public const string SectionName = "tables";
    [Key(0)] public int Version { get; set; } = 1;
    /// <summary>Table names, ordinal order: the content type each table indexes (<see cref="IdTables.Goods"/> and the others).</summary>
    [Key(1)] public string[] Names { get; set; } = [];
    [Key(2)] public string[][] Ids { get; set; } = [];
}

/// <summary>A set of id tables by name: each the content ids in the order the game numbers them (ordinal id order, as every runtime table builds them).</summary>
public sealed class IdTables
{
    /// <summary>Table names are the content types: the goods catalog, the craft recipes (<c>RecipeDb</c>), proficiencies, business kinds, the business recipe book, extraction kinds.</summary>
    public const string Goods = "good", CraftRecipes = "craft_recipe", Proficiencies = "proficiency", Businesses = "business", BookRecipes = "recipe", Extractions = "extraction";

    public SortedDictionary<string, string[]> Tables { get; } = new(StringComparer.Ordinal);
    public bool IsEmpty => Tables.Count == 0;

    public string[]? Get(string name) => Tables.TryGetValue(name, out var t) ? t : null;

    public IdTablesSave ToSave() => new() { Names = [.. Tables.Keys], Ids = [.. Tables.Values.Select(v => (string[])v.Clone())] };

    public static IdTables FromSave(IdTablesSave s)
    {
        if (s.Names.Length != s.Ids.Length) throw new SaveException("Corrupt 'tables' section in save.");
        var t = new IdTables();
        for (int i = 0; i < s.Names.Length; i++) t.Tables[s.Names[i]] = s.Ids[i];
        return t;
    }

    /// <summary>The same tables restricted to <paramref name="names"/>.</summary>
    public IdTables Only(IEnumerable<string> names)
    {
        var t = new IdTables();
        foreach (var n in names) if (Tables.TryGetValue(n, out var ids)) t.Tables[n] = ids;
        return t;
    }

    /// <summary>The goods table alone, from a catalog (a world whose content is not at hand).</summary>
    public static IdTables OfGoods(GoodCatalog cat)
    {
        var t = new IdTables();
        t.Tables[Goods] = [.. Enumerable.Range(0, cat.Count).Select(i => cat.StringId((ushort)i))];
        return t;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContentDatabase, ContentTables> Cache = new();

    /// <summary>Every table of <paramref name="db"/> (cached per database). A table the content cannot build (a partial test content) is left out.</summary>
    public static ContentTables Of(ContentDatabase db) => Cache.GetValue(db, Build);

    private static ContentTables Build(ContentDatabase db)
    {
        var cat = new GoodCatalog(db);
        var t = OfGoods(cat);
        try
        {
            var rdb = Crafting.RecipeDb.Build(db, cat);
            t.Tables[CraftRecipes] = [.. rdb.Recipes.Select(r => r.Id)];
            t.Tables[Proficiencies] = [.. rdb.Proficiencies.Select(p => p.Id)];
        }
        catch (Exception e) when (e is InvalidDataException or KeyNotFoundException or InvalidOperationException or ArgumentException) { }
        try
        {
            var pc = Production.ProductionContent.Load(db, cat);
            t.Tables[Businesses] = [.. pc.Kinds.Select(k => k.Id)];
            t.Tables[BookRecipes] = [.. pc.Recipes.Recipes.Select(r => r.Id)];
            t.Tables[Extractions] = [.. pc.Extractions.Select(x => x.Id)];
        }
        catch (Exception e) when (e is InvalidDataException or KeyNotFoundException or InvalidOperationException or ArgumentException) { }
        return new ContentTables(t, cat);
    }
}

/// <summary>A content database's id tables and its goods catalog (for the goods hash the economy, production and markets sections carry).</summary>
public sealed record ContentTables(IdTables Tables, GoodCatalog Goods);
