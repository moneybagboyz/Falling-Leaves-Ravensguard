using System.Text.Json.Serialization;

namespace Ravensguard.Content;

[JsonConverter(typeof(SnakeEnumConverter<StructureKind>))]
public enum StructureKind { Artery, Vein, Nerve, Tendon }

/// <summary>A tissue layer of a body part, outside in. Thickness is radial, in mm.</summary>
public sealed record TissueLayerDef
{
    public required string Tissue { get; init; }
    public required double ThicknessMm { get; init; }
}

/// <summary>A structure inside the muscle layer (tendon, nerve, vessel), hit with probability by area fraction.</summary>
public sealed record StructureDef
{
    public required string Id { get; init; }
    public required StructureKind Kind { get; init; }
    public required string Tissue { get; init; }
    public required double AreaFraction { get; init; }
}

/// <summary>An internal organ behind the innermost layer of its parent part.</summary>
public sealed record OrganDef
{
    public required string Id { get; init; }
    public required string Tissue { get; init; }
    public required double AreaFraction { get; init; }
    public bool Vital { get; init; }
    /// <summary>ml per second when wounded (liver/spleen 20, heart higher; plan §4A.3).</summary>
    public double BleedMlPerS { get; init; } = 10;
}

public sealed record BodyPartDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>Fraction of the creature's size (DF: upper body 0.18, head 0.054 ...).</summary>
    public required double RelativeSize { get; init; }
    /// <summary>If true the part is created twice as id_l / id_r.</summary>
    public bool Sided { get; init; }
    public string? Parent { get; init; }
    /// <summary>Diameter/thickness in mm; a penetration at least this deep can sever the part.</summary>
    public required double DepthMm { get; init; }
    public bool Severable { get; init; }
    public required List<TissueLayerDef> Layers { get; init; }
    public List<StructureDef> Structures { get; init; } = [];
    public List<OrganDef> Organs { get; init; } = [];
}

/// <summary>A named gap (armpit, inside of the elbow ...) that lies on a body part (plan §2.6 step 3).</summary>
public sealed record NamedGapDef
{
    public required string Name { get; init; }
    public required string Part { get; init; }
}

[ContentType("body_plan")]
public sealed record BodyPlanDef : ContentDef
{
    public required string Name { get; init; }
    public required int SizeCm3 { get; init; }
    public double BloodMl { get; init; } = 5000;
    public required List<BodyPartDef> Parts { get; init; }
    public List<NamedGapDef> NamedGaps { get; init; } = [];
}
