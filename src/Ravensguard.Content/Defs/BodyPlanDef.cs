using System.Text.Json.Serialization;

namespace Ravensguard.Content;

[JsonConverter(typeof(SnakeEnumConverter<StructureKind>))]
public enum StructureKind { Artery, Vein, Nerve, Tendon, MotorNerve, SensoryNerve, Ligament, SpinalCord }

/// <summary>A tissue layer of a body part, outside in. Thickness is radial, in mm.</summary>
public sealed record TissueLayerDef
{
    public required string Tissue { get; init; }
    public required double ThicknessMm { get; init; }
    /// <summary>The tissue's role when a plan's tissue_map replaced it (the draugr's dried flesh is still its muscle); null: the tissue itself.</summary>
    public string? Role { get; init; }
}

/// <summary>A structure inside the muscle layer (tendon, nerve, vessel), hit with probability by area fraction.</summary>
public sealed record StructureDef
{
    public required string Id { get; init; }
    public required StructureKind Kind { get; init; }
    public required string Tissue { get; init; }
    public required double AreaFraction { get; init; }
    /// <summary>Ligaments: the joint they hold (shoulder, elbow, wrist, hip, knee, ankle).</summary>
    public string? Joint { get; init; }
    /// <summary>Spinal cord and other structures reached better from one side: permille multipliers on the area by facing (front, flank, rear), anatomy 3.3 (cord_back_u front 300, flank 800, rear 1600).</summary>
    public Dictionary<string, int> FacingPm { get; init; } = [];
    /// <summary>Area multiplier when the blow was aimed at this named gap and bypassed the armour there (inner_elbow x3 on the elbow ligament).</summary>
    public Dictionary<string, double> GapBoost { get; init; } = [];
    /// <summary>Nerves: the parts that lose function when this nerve is cut (default: this part and every part below it in the parent chain).</summary>
    public List<string> Distal { get; init; } = [];
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
    /// <summary>Lies in front of the bone layer (the trachea): reached when the muscle layer is cut, like a structure, not only through the bone (resolver-v2 4.7).</summary>
    public bool BeforeBone { get; init; }
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
    public List<TissueLayerDef> Layers { get; init; } = [];
    /// <summary>v2: the part lives inside this part (eye in head): reached through it, or by a called shot (anatomy 3.1).</summary>
    public string? Host { get; init; }
    /// <summary>v2: a hosted part is only reachable when no armour piece caught the blow on the host, other than through this named gap (eyes: face).</summary>
    public string? ExposureGap { get; init; }
    /// <summary>v2: a hosted part's share of the host's cross-section, rolled for unaimed blows that breach the host's soft layers.</summary>
    public double AreaFraction { get; init; }
    /// <summary>v2: called-shot multiplier on the host's hit chance, permille (eye 120).</summary>
    public int AimFactorPm { get; init; }
    /// <summary>v2: the part is the host's bone layer promoted to a part (skull, ribs, pelvis): it has no layers of its own and takes the fracture of the host's bone layer.</summary>
    public bool Shell { get; init; }
    /// <summary>v2: what the part contributes, per channel, permille (sight_pm 500 per eye; grip_pm 300 thumb, 700 fingers).</summary>
    public Dictionary<string, int> Function { get; init; } = [];
    /// <summary>v2: pain multiplier for wounds here, permille (1000 plain).</summary>
    public int PainPm { get; init; } = 1000;
    /// <summary>v2: which limb chain the part belongs to (arm or leg), for the derived capacities.</summary>
    public string? Limb { get; init; }
    /// <summary>v2: a point blow that fails this part's bone with enough reach left can slip between the ribs (resolver-v2 4.7, upper body only).</summary>
    public bool RibSlip { get; init; }
    public List<StructureDef> Structures { get; init; } = [];
    public List<OrganDef> Organs { get; init; } = [];
}

/// <summary>A named gap (armpit, inside of the elbow ...) that lies on a body part (plan §2.6 step 3).</summary>
public sealed record NamedGapDef
{
    public required string Name { get; init; }
    public required string Part { get; init; }
    /// <summary>v2: other base parts the gap also opens onto (the armpit lies on the upper body and the upper arm).</summary>
    public List<string> Also { get; init; } = [];
}

[ContentType("body_plan")]
public sealed record BodyPlanDef : ContentDef
{
    public required string Name { get; init; }
    public required int SizeCm3 { get; init; }
    public double BloodMl { get; init; } = 5000;
    public List<BodyPartDef> Parts { get; init; } = [];
    public List<NamedGapDef> NamedGaps { get; init; } = [];

    // ---- v2 (docs/specs/phase4/anatomy-and-body-plans.md 3.1) ----
    /// <summary>Start from this plan's parts and gaps, then apply <see cref="TissueMap"/> and the drops (the draugr is the human plan).</summary>
    [ContentRef(typeof(BodyPlanDef))] public string? CopyFrom { get; init; }
    /// <summary>Replace these tissues everywhere in the copied plan (skin to dried flesh).</summary>
    public Dictionary<string, string> TissueMap { get; init; } = [];
    public bool DropOrgans { get; init; }
    public List<StructureKind> DropStructureKinds { get; init; } = [];
    /// <summary>Blood per kg of body mass (the human 5,000 ml is about 71 ml/kg); 0 with <see cref="Bleeds"/> false.</summary>
    public int BloodMlPerKg { get; init; }
    public bool Bleeds { get; init; } = true;
    public bool FeelsPain { get; init; } = true;
    /// <summary>Monsters: the part whose destruction ends the creature (draugr: head).</summary>
    public string? AnimatingCore { get; init; }
    /// <summary>Vessel bleed rates relative to the human numbers, permille.</summary>
    public int BleedScalePm { get; init; } = 1000;
    public string TissueSet { get; init; } = "";
    /// <summary>Share of the body (permille) that is not targetable (hooves, tail, mane): a blow there lands on a neighbour.</summary>
    public int UntargetablePm { get; init; }
    /// <summary>Points added to the attacker's skill delta when the creature is struck from the front (a horse is a large target).</summary>
    public int TargetBonusPoints { get; init; }
    /// <summary>Animal morale: flees below this share of its blood, permille (0: never).</summary>
    public int FleeBelowBloodPm { get; init; }
    /// <summary>Old structure ids and what a wound on them loads as (neck_nerve to neck_motor and neck_sensory).</summary>
    public Dictionary<string, List<string>> Migrate { get; init; } = [];

    // ---- P8-LM-3: the bestiary's traits live on the plan (a monster is a body plan, materials and a brain; there is no separate monster rule) ----
    /// <summary>Never tires (the draugr): the stamina is held full each tick.</summary>
    public bool Tireless { get; init; }
    /// <summary>Fights as well in the dark as in the light: darkness does not change its aim (the cave beast and every lair monster).</summary>
    public bool DarkSight { get; init; }
    /// <summary>Foot speed, permille of a man's (the barghest runs at 9 m/s); 1000 plain.</summary>
    public int SpeedPm { get; init; } = 1000;
    /// <summary>A hold the creature takes on a foe within reach: a drag toward water (the bog-thing) or a constrict (the lindworm).</summary>
    public GraspDef? Grasp { get; init; }
    /// <summary>A fright it throws once a fight, in the dark of night (the barghest).</summary>
    public FearDef? Fear { get; init; }
}

/// <summary>A creature's hold (P8-LM-3, bestiary 3.3). Force in newtons (int); the hold is taken at most once a sim-second, drawn on stream 0x831.</summary>
public sealed record GraspDef
{
    /// <summary>drag: the foe is hauled toward the nearest water cell and held under it; constrict: the foe is crushed through the chest.</summary>
    public required string Kind { get; init; }
    public int ForceN { get; init; } = 2000;
    /// <summary>Chance per sim-second to take hold of a foe in reach, permille.</summary>
    public int ChancePm { get; init; } = 300;
    public int RangeMm { get; init; } = 1600;
    /// <summary>A drag's speed, mm/s.</summary>
    public int DragMmps { get; init; } = 1200;
    /// <summary>A constrict's crush on the held foe's body part: blunt momentum in milli-units per sim-second, and the contact area in mm2.</summary>
    public long SqueezeMilli { get; init; } = 40_000;
    public int SqueezeAreaMm2 { get; init; } = 120_000;
    /// <summary>The part squeezed (upper_body for a man).</summary>
    public string SqueezePart { get; init; } = "upper_body";
}

/// <summary>A fright (P8-LM-3): foes within the radius lose this much morale (permille of the 0..1000 scale) once a fight, at night only when so set.</summary>
public sealed record FearDef
{
    public int RadiusM { get; init; } = 8;
    public int ShockPm { get; init; } = 150;
    public bool NightOnly { get; init; } = true;
}

/// <summary>Resolves a body plan's <c>copy_from</c> (anatomy 4.3: the draugr is the human plan with its tissues swapped and its organs and vessels dropped).</summary>
public static class BodyPlanResolver
{
    public static BodyPlanDef Resolve(ContentDatabase db, BodyPlanDef def, int depth = 0)
    {
        if (def.CopyFrom == null) return def;
        if (depth > 8) throw new ContentException($"body_plan '{def.Id}': copy_from chain too deep (cycle?)");
        var src = Resolve(db, db.Get<BodyPlanDef>(def.CopyFrom), depth + 1);
        string Map(string t) => def.TissueMap.TryGetValue(t, out var m) ? m : t;
        var drop = new HashSet<StructureKind>(def.DropStructureKinds);
        var parts = (def.Parts.Count > 0 ? def.Parts : src.Parts).Select(p => p with
        {
            Layers = [.. p.Layers.Select(l => l with { Tissue = Map(l.Tissue), Role = l.Role ?? (Map(l.Tissue) != l.Tissue ? l.Tissue : null) })],
            Structures = [.. p.Structures.Where(s => !drop.Contains(s.Kind)).Select(s => s with { Tissue = Map(s.Tissue) })],
            Organs = def.DropOrgans ? [] : [.. p.Organs.Select(o => o with { Tissue = Map(o.Tissue) })],
        }).ToList();
        return def with
        {
            CopyFrom = null, Parts = parts,
            NamedGaps = def.NamedGaps.Count > 0 ? def.NamedGaps : src.NamedGaps,
            Migrate = def.Migrate.Count > 0 ? def.Migrate : src.Migrate,
        };
    }
}
