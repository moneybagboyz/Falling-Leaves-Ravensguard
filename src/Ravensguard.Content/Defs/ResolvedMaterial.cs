namespace Ravensguard.Content;

/// <summary>A <see cref="MaterialDef"/> with <c>copy_from</c> inheritance applied and every number concrete.</summary>
public sealed record ResolvedMaterial(
    string Id, string Name, string Category, double DensityKgM3, double ValuePerKg,
    double ImpactYield, double ImpactFracture, double ImpactStrainAtYield,
    double ShearYield, double ShearFracture, double ShearStrainAtYield,
    double MaxEdge, double? MeltingPointC, double? IgnitionPointC)
{
    public double DensityGcm3 => DensityKgM3 / 1000.0;
    /// <summary>Resolver v2 multipliers (permille; 1000 when the material and its templates leave them out).</summary>
    public int CutResistPm { get; init; } = 1000;
    public int CrushResistPm { get; init; } = 1000;
    public int BluntAbsorbPm { get; init; }
    public int PainPm { get; init; } = 1000;
    /// <summary>P6-B6: the order group of the resolver's material table (0: Phases 1-5, by id; later ones after them).</summary>
    public int AppendOrder { get; init; }
}

/// <summary>Resolves <c>copy_from</c> chains (the content pipeline has no generic inheritance).</summary>
public static class MaterialResolver
{
    public static Dictionary<string, ResolvedMaterial> ResolveAll(ContentDatabase db)
    {
        var result = new Dictionary<string, ResolvedMaterial>(StringComparer.Ordinal);
        foreach (var m in db.All<MaterialDef>().OrderBy(m => m.Id, StringComparer.Ordinal))
            result[m.Id] = Resolve(db, m.Id);
        return result;
    }

    public static ResolvedMaterial Resolve(ContentDatabase db, string id)
    {
        var chain = new List<MaterialDef>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (string? cur = id; cur != null;)
        {
            if (!seen.Add(cur)) throw new ContentException($"copy_from cycle at material '{cur}'");
            var def = db.Get<MaterialDef>(cur);
            chain.Add(def);
            cur = def.CopyFrom;
        }
        // chain[0] = the material itself, last = root template. First non-null wins.
        double? D(Func<MaterialDef, double?> f) { foreach (var m in chain) if (f(m) is { } v) return v; return null; }
        string? S(Func<MaterialDef, string?> f) { foreach (var m in chain) if (f(m) is { } v) return v; return null; }
        int? I(Func<MaterialDef, int?> f) { foreach (var m in chain) if (f(m) is { } v) return v; return null; }
        var self = chain[0];
        return new ResolvedMaterial(self.Id, self.Name, S(m => m.Category) ?? "misc", self.DensityKgM3, self.ValuePerKg,
            D(m => m.ImpactYieldKpa) ?? 0, D(m => m.ImpactFractureKpa) ?? 0, D(m => m.ImpactStrainAtYield) ?? 0,
            D(m => m.ShearYieldKpa) ?? 0, D(m => m.ShearFractureKpa) ?? 0, D(m => m.ShearStrainAtYield) ?? 0,
            D(m => m.MaxEdge) ?? 0.1, D(m => m.MeltingPointC), D(m => m.IgnitionPointC))
        {
            CutResistPm = I(m => m.CutResistPm) ?? 1000, CrushResistPm = I(m => m.CrushResistPm) ?? 1000,
            BluntAbsorbPm = I(m => m.BluntAbsorbPm) ?? 0, PainPm = I(m => m.PainPm) ?? 1000, AppendOrder = self.AppendOrder,
        };
    }
}
