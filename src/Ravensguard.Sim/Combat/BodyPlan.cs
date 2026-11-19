using Ravensguard.Content;

namespace Ravensguard.Sim.Combat;

public sealed record BodyLayer(string TissueId, double ThicknessMm, LayerProps Props);

/// <summary>A concrete body part (sided parts are expanded to id_l / id_r).</summary>
public sealed class BodyPart
{
    public required string Id { get; init; }
    public required string BaseId { get; init; }
    public required string Name { get; init; }
    public required double RelativeSize { get; init; }
    public required double DepthMm { get; init; }
    public required bool Severable { get; init; }
    public string? Parent { get; init; }
    public List<string> Children { get; } = [];
    public required IReadOnlyList<BodyLayer> Layers { get; init; }
    public required IReadOnlyList<StructureDef> Structures { get; init; }
    public required IReadOnlyList<OrganDef> Organs { get; init; }
    public double TotalLayerMm => Layers.Sum(l => l.ThicknessMm);
}

/// <summary>Runtime body plan resolved from a <see cref="BodyPlanDef"/>.</summary>
public sealed class BodyPlan
{
    public required string Id { get; init; }
    public required int SizeCm3 { get; init; }
    public required double BloodMl { get; init; }
    public required IReadOnlyList<BodyPart> Parts { get; init; }
    public required IReadOnlyDictionary<string, BodyPart> ById { get; init; }
    /// <summary>Named gap → base part id it lies on.</summary>
    public required IReadOnlyDictionary<string, string> NamedGaps { get; init; }
    public required double MaxRelativeSize { get; init; }

    public IEnumerable<string> Neighbours(string partId)
    {
        var p = ById[partId];
        if (p.Parent != null) yield return p.Parent;
        foreach (var c in p.Children) yield return c;
    }

    /// <summary>
    /// Builds the v1 plan. Resolver v1 sees only the trunk and limb parts: the v2 hosted parts (eyes, ribs, fingers ...) are skipped, so the
    /// v1 human keeps its 16 instances (the v2 detail is in <see cref="Resolver2.BodyPlanV2"/>).
    /// </summary>
    public static BodyPlan Build(BodyPlanDef def, Func<string, LayerProps> tissue)
    {
        var parts = new List<BodyPart>();
        var byBase = def.Parts.ToDictionary(p => p.Id, StringComparer.Ordinal);
        foreach (var d in def.Parts)
        {
            if (d.Host != null || d.Shell) continue;
            foreach (var side in d.Sided ? new[] { "_l", "_r" } : [""])
            {
                string? parent = null;
                if (d.Parent != null)
                {
                    if (!byBase.TryGetValue(d.Parent, out var pd))
                        throw new ContentException($"body_plan '{def.Id}': part '{d.Id}' has unknown parent '{d.Parent}'");
                    parent = pd.Sided ? d.Parent + (side.Length > 0 ? side : throw new ContentException(
                        $"body_plan '{def.Id}': unsided part '{d.Id}' cannot have sided parent '{d.Parent}'")) : d.Parent;
                }
                BodyLayer L(TissueLayerDef l) => new(l.Tissue, l.ThicknessMm, Lookup(def, d, l.Tissue, tissue));
                foreach (var s in d.Structures) _ = Lookup(def, d, s.Tissue, tissue);
                foreach (var o in d.Organs) _ = Lookup(def, d, o.Tissue, tissue);
                parts.Add(new BodyPart
                {
                    Id = d.Id + side, BaseId = d.Id, Name = d.Name + (side == "_l" ? " (left)" : side == "_r" ? " (right)" : ""),
                    RelativeSize = d.RelativeSize, DepthMm = d.DepthMm, Severable = d.Severable, Parent = parent,
                    Layers = d.Layers.Select(L).ToList(), Structures = d.Structures, Organs = d.Organs,
                });
            }
        }
        var byId = parts.ToDictionary(p => p.Id, StringComparer.Ordinal);
        foreach (var p in parts) if (p.Parent != null) byId[p.Parent].Children.Add(p.Id);
        foreach (var g in def.NamedGaps)
            if (!byBase.ContainsKey(g.Part)) throw new ContentException($"body_plan '{def.Id}': gap '{g.Name}' on unknown part '{g.Part}'");
        return new BodyPlan
        {
            Id = def.Id, SizeCm3 = def.SizeCm3, BloodMl = def.BloodMl, Parts = parts, ById = byId,
            NamedGaps = def.NamedGaps.ToDictionary(g => g.Name, g => g.Part, StringComparer.Ordinal),
            MaxRelativeSize = parts.Max(p => p.RelativeSize),
        };
    }

    private static LayerProps Lookup(BodyPlanDef def, BodyPartDef part, string tissueId, Func<string, LayerProps> tissue)
    {
        try { return tissue(tissueId); }
        catch (KeyNotFoundException) { throw new ContentException($"body_plan '{def.Id}': part '{part.Id}' uses unknown material '{tissueId}'"); }
    }
}
