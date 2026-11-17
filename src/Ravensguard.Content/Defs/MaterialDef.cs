using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ravensguard.Content;

/// <summary>JSON string enum using snake_case names (e.g. "well_crafted").</summary>
public sealed class SnakeEnumConverter<T>() : JsonStringEnumConverter<T>(JsonNamingPolicy.SnakeCaseLower) where T : struct, Enum;

/// <summary>
/// A material (metal, wood, cloth, body tissue ...) with Dwarf Fortress style mechanical properties (plan §2.10, §2.6).
/// <para>Units: density kg/m³. Stress values (yield/fracture) are DF raw values, called kPa in the raws
/// (steel impact yield 1,505,000; the §2.6 blunt formulas use raw/10^6). Strain-at-yield is parts per 100,000
/// (DF raw). <see cref="MaxEdge"/> is DF's sharpness multiplier S (1 for metals, 0.1 for wood/bone).
/// Temperatures in °C. Value is per kg in silver pennies.</para>
/// <para><c>copy_from</c>: every optional numeric/text property that is left out is inherited from the named
/// material (see <see cref="MaterialResolver"/>). Name, density and value stay required so a definition is always
/// self-describing; a mod makes "bog iron" by copying iron and overriding a few numbers.</para>
/// </summary>
[ContentType("material")]
public sealed record MaterialDef : ContentDef
{
    public required string Name { get; init; }
    public required double DensityKgM3 { get; init; }
    public required double ValuePerKg { get; init; }

    [ContentRef(typeof(MaterialDef))] public string? CopyFrom { get; init; }
    /// <summary>metal, wood, bone, leather, cloth, tissue.</summary>
    public string? Category { get; init; }

    public double? ImpactYieldKpa { get; init; }
    public double? ImpactFractureKpa { get; init; }
    public double? ImpactStrainAtYield { get; init; }
    public double? ShearYieldKpa { get; init; }
    public double? ShearFractureKpa { get; init; }
    public double? ShearStrainAtYield { get; init; }
    public double? MaxEdge { get; init; }

    public double? MeltingPointC { get; init; }
    public double? IgnitionPointC { get; init; }
}
