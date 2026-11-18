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

    // ---- clothing and warmth (docs/specs/phase3/clothing-warmth-exposure.md 3.1); all optional, zero for a shape that is only armour ----
    /// <summary>Insulation in clo x 100 at full coverage (15 a linen shirt, 100 a gambeson).</summary>
    public int InsulationCloX100 { get; init; }
    /// <summary>Share of wind chill blocked, permille.</summary>
    public int WindproofPm { get; init; }
    /// <summary>Share of rain kept out, permille.</summary>
    public int WaterproofPm { get; init; }
    /// <summary>Extra heat load in hot weather, permille (armour traps heat).</summary>
    public int HeatRetentionPm { get; init; }
    /// <summary>Encumbrance added per piece, permille of agility.</summary>
    public int EncPm { get; init; }
    /// <summary>Water the piece holds when wet, percent of its dry mass.</summary>
    public int AbsorbPct { get; init; }
    /// <summary>Insulation lost at full wetness, permille (wool 400, linen 650).</summary>
    public int WetLossPm { get; init; }
    /// <summary>Mass in grams of the piece in its default material (the real mass scales with the chosen material's density).</summary>
    public int MassG { get; init; }
    [ContentRef(typeof(MaterialDef))] public string? DefaultMaterial { get; init; }
    /// <summary>The rig layer that draws the piece (tunic, hose, linen, gambeson, cloak, hood ...); empty for a piece the figure does not show.</summary>
    public string RigLayer { get; init; } = "";
    /// <summary>False for a bedroll or a blanket: carried, not worn.</summary>
    public bool Wearable { get; init; } = true;
}

/// <summary>One piece of a starting wardrobe: a shape in a material, worn from a season and with a chance (permille) the person owns it.</summary>
public sealed record WardrobePieceDef
{
    [ContentRef(typeof(ArmourShapeDef))] public required string Shape { get; init; }
    [ContentRef(typeof(MaterialDef))] public required string Material { get; init; }
    /// <summary>all, cold (autumn evenings, winter, the cold half of spring) or warm; the person is issued only pieces of the season of the day they are created or promoted, plus the cold pieces if they own them in any case (a cloak is kept in summer too).</summary>
    public string Season { get; init; } = "all";
    public int ChancePm { get; init; } = 1000;
    /// <summary>Occupations (Occupation enum names) the piece is for; empty: anyone of the class.</summary>
    public List<string> Occupations { get; init; } = [];
    /// <summary>Wear (permille condition lost) at issue: a peasant's cloak is old.</summary>
    public int WornPm { get; init; }
}

/// <summary>A starting wardrobe by social class (clothing spec 4.1): everyone in the bubble is dressed from it, as real items.</summary>
[ContentType("wardrobe")]
public sealed record WardrobeDef : ContentDef
{
    /// <summary>Peasant, Burgher or Gentry.</summary>
    public required string Class { get; init; }
    public List<WardrobePieceDef> Pieces { get; init; } = [];
    /// <summary>Dye colours (hex) the person's visible outer pieces are drawn in, picked from the person's id; empty: undyed.</summary>
    public List<string> Dyes { get; init; } = [];
}

/// <summary>Exposure tuning (clothing spec 3.3): everything is whole numbers in the unit its name carries.</summary>
[ContentType("exposure_settings")]
public sealed record ExposureSettingsDef : ContentDef
{
    public int ComfortC { get; init; } = 22;
    public int CloPerDegreeX100AtMet100 { get; init; } = 1400;
    public int MaxDeficitCloX100 { get; init; } = 300;
    public int CoolCcPerHourAtMax { get; init; } = 80;
    public int RewarmCcPerHour { get; init; } = 150;
    public int RewarmFireCcPerHour { get; init; } = 250;
    public int HeatFreeC { get; init; } = 24;
    public int HeatFullHlX10 { get; init; } = 600;
    public int HeatRiseCcPerHourAtMax { get; init; } = 100;
    public int HeatCoolCcPerHour { get; init; } = 100;
    public int WindFreeMps { get; init; } = 2;
    public int WindChillMaxC { get; init; } = 12;
    public Dictionary<string, int> RegionsPm { get; init; } = [];
    public Dictionary<string, int> MetX100 { get; init; } = [];
    public Dictionary<string, int> IndoorOffsetC { get; init; } = [];
    public int WetGainPmPerMinPerMmH { get; init; } = 5;
    public int WetLeakInnerPct { get; init; } = 20;
    public Dictionary<string, int> DryPmPerHour { get; init; } = [];
    /// <summary>Game minutes of severe cold (core under 32 C) before a person collapses, and game minutes of heatstroke before death unless cooled.</summary>
    public int CollapseMinutes { get; init; } = 30;
    public int HeatstrokeDeathMinutes { get; init; } = 120;
    /// <summary>Cold limit for chilled, moderate, severe, lethal and heat limits for warm, exhausted, stroke, in centi-degrees C.</summary>
    public int ChilledCc { get; init; } = 3600;
    public int ModerateCc { get; init; } = 3500;
    public int SevereCc { get; init; } = 3200;
    public int LethalCc { get; init; } = 2800;
    public int WarmCc { get; init; } = 3800;
    public int ExhaustedCc { get; init; } = 3950;
    public int StrokeCc { get; init; } = 4100;
    public int StrokeCoolCc { get; init; } = 4000;
    /// <summary>Garment condition lost per game day of wear is 1000 / this (the Phase 2 clothes lifetime, 1,095 days).</summary>
    public int ClothesLifetimeDays { get; init; } = 1095;
    /// <summary>Insulation of the household bedding (straw tick, wool blankets) a sleeper in a bed has, clo x 100; and the cold stress (permille) at or below which a person rewarms rather than cools (a person in mild cold keeps their heat).</summary>
    public int BeddingCloX100 { get; init; } = 250;
    public int ComfortStressPm { get; init; } = 100;
}
