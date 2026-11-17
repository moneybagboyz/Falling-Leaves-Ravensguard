using System.Text.Json.Serialization;

namespace Ravensguard.Content;

[JsonConverter(typeof(SnakeEnumConverter<AttackType>))]
public enum AttackType { Edge, Point, Blunt }

/// <summary>Layer order, outside in: cover, armour, over, under (plan §2.6).</summary>
[JsonConverter(typeof(SnakeEnumConverter<ArmourLayer>))]
public enum ArmourLayer { Under = 0, Over = 1, Armour = 2, Cover = 3 }

/// <summary>One attack of a weapon. Contact area and penetration are in DF-style units (see weapon_shape docs).</summary>
public sealed record AttackDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Verb { get; init; } = "hits";
    public required AttackType Type { get; init; }
    /// <summary>DF contact area A (the §2.6 formulas use min(this, layer area)); stabs are ≤ ~15, slashes tens, blunt 10–60.</summary>
    public required double ContactArea { get; init; }
    /// <summary>Maximum depth in mm an edged/point attack travels before it turns blunt.</summary>
    public required double PenetrationMm { get; init; }
    /// <summary>DF velocity multiplier (1.25 for a hacking axe/sword).</summary>
    public double Velocity { get; init; } = 1.0;
}

/// <summary>
/// A weapon shape (plan §2.10): a head of the instance's material plus an optional haft.
/// Volumes are real cm³ of material (a steel arming-sword blade of 180 cm³ weighs 1.41 kg).
/// </summary>
[ContentType("weapon_shape")]
public sealed record WeaponShapeDef : ContentDef
{
    public required string Name { get; init; }
    public required double HeadVolumeCm3 { get; init; }
    public double HaftVolumeCm3 { get; init; }
    [ContentRef(typeof(MaterialDef))] public string? HaftMaterial { get; init; }
    public bool TwoHanded { get; init; }
    public double WindUpS { get; init; } = 0.5;
    public double RecoverS { get; init; } = 0.5;
    public List<AttackDef> Attacks { get; init; } = [];
}

/// <summary>
/// An armour or clothing piece (plan §2.10). Coverage is 0–1 per base body part id (applies to both sides).
/// <c>gaps</c> are the named gaps this piece leaves open (they bypass the piece, §2.6 step 3).
/// <c>elastic</c> raises strain-at-yield to 50,000 (mail); <c>woven</c> reduces shear to 20–30 (cloth).
/// </summary>
[ContentType("armour_shape")]
public sealed record ArmourShapeDef : ContentDef
{
    public required string Name { get; init; }
    public required ArmourLayer Layer { get; init; }
    /// <summary>Order within the layer; higher is outside (plate over mail).</summary>
    public int Order { get; init; }
    public required double ThicknessMm { get; init; }
    public required Dictionary<string, double> Coverage { get; init; }
    public List<string> Gaps { get; init; } = [];
    /// <summary>DF layer size/permit: a piece fits over another when its LayerSize ≤ the lower piece's LayerPermit.</summary>
    public int LayerSize { get; init; }
    public int LayerPermit { get; init; }
    /// <summary>DF size multiplier (item volume relative to the body part).</summary>
    public double SizeMultiplier { get; init; } = 0.2;
    /// <summary>The body size (cm³) this piece is made for; wearable by anyone within ±1/7.</summary>
    public int FitSizeCm3 { get; init; } = 70000;
    public bool Elastic { get; init; }
    public bool Woven { get; init; }
}
