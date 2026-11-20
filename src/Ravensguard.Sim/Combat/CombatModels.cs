using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat;

public enum AttackStyle { Normal, Quick, Heavy }
public enum Facing { Front, Flank, Rear }
public enum WoundKind { Cut, Crush, Burn }
public enum LayerOutcome
{
    /// <summary>Cut through (edged test passed).</summary>
    Cut,
    /// <summary>Crushed/thrashed (blunt test passed).</summary>
    Crushed,
    /// <summary>Failed both tests: the blow became blunt for good and lost momentum.</summary>
    Stopped,
    /// <summary>Blunt deflected outright (impact yield too low against the armour's density).</summary>
    Deflected,
    /// <summary>The piece did not cover the struck spot (coverage roll failed).</summary>
    NotCovered,
    /// <summary>A named gap let the blow bypass the armour piece.</summary>
    Bypassed,
    // v2 (resolver-v2 3.2), appended
    /// <summary>A block stopped the blow (the trace's first step).</summary>
    Blocked,
    Parried,
    /// <summary>The layer was crushed and its padding soaked up part of the blow.</summary>
    Absorbed,
    Dodged,
}

[Flags]
public enum WoundFlags
{
    None = 0,
    Severed = 1,
    ArteryHit = 2,
    VeinHit = 4,
    NerveHit = 8,
    TendonHit = 16,
    BoneBroken = 32,
    OrganHit = 64,
    /// <summary>The wounded organ is vital (heart, brain).</summary>
    Vital = 128,
    // v2 (resolver-v2 3.2), appended
    LigamentHit = 256,
    MotorNerveHit = 512,
    SensoryNerveHit = 1024,
    SpineHit = 2048,
    /// <summary>The wound is on a hosted part (eye, ear, nose, jaw, teeth, collarbone, kneecap, thumb, fingers).</summary>
    InsetHit = 4096,
    Displaced = 8192,
    /// <summary>A shattered bone through the skin.</summary>
    Compound = 16384,
    Burn = 32768,
    /// <summary>Skull broken or a brain blow short of destruction: the wounds file's concussion (an addition to the spec's list).</summary>
    Concussion = 65536,
    /// <summary>A point blow slipped between the ribs.</summary>
    RibSlip = 131072,
}

/// <summary>
/// One wound record (plan §2.6 step 6). <see cref="Depth"/> is the fraction (0–1) of the tissue layer's thickness reached;
/// <see cref="BleedMlPerS"/> follows plan §4A.3 (capillary 0.5, muscle 3, vein 15, artery 80–120, organs by organ).
/// </summary>
public sealed record Wound(string PartId, string TissueId, string? StructureId, WoundKind Kind, Fixed Depth, Fixed BleedMlPerS, WoundFlags Flags)
{
    public bool IsSevered => (Flags & WoundFlags.Severed) != 0;
}

/// <summary>One step of a blow's path through the layers, for tests, logs and debugging.</summary>
public sealed record LayerTrace(string Layer, bool IsArmour, LayerOutcome Outcome, long MomentumInMilli, long MomentumOutMilli);

public sealed record Attacker(int Strength, int Skill, int SizeCm3 = 70000, int SpeciesSizeCm3 = 70000);

/// <summary>A weapon instance: shape × head material × quality (the haft material comes from the shape).</summary>
public sealed record WeaponInstance(string ShapeId, string MaterialId, Goods.ItemQuality Quality = Goods.ItemQuality.Common);

/// <summary>A worn armour/clothing piece: shape × material × quality.</summary>
public sealed record WornItem(string ShapeId, string MaterialId, Goods.ItemQuality Quality = Goods.ItemQuality.Common);

public sealed record Defender(
    BodyPlan Plan, int SizeCm3, IReadOnlyList<WornItem> Worn, int Skill = 0,
    IReadOnlySet<string>? GuardedParts = null, bool Prone = false);

public sealed record StrikeInput(
    Attacker Attacker, WeaponInstance Weapon, string AttackId, string TargetPart, Defender Defender,
    string? TargetGap = null, Facing Facing = Facing.Front, AttackStyle Style = AttackStyle.Normal);

public sealed record StrikeResult(
    bool Hit, string? PartId, bool Redirected, IReadOnlyList<Wound> Wounds, IReadOnlyList<LayerTrace> Trace,
    long InitialMomentumMilli, long FinalMomentumMilli, bool Severed, bool Deflected)
{
    public static StrikeResult Miss(long initial) => new(false, null, false, [], [], initial, 0, false, false);
}

/// <summary>DF layer size/permit and ±1/7 size fit rules (plan §2.10).</summary>
public static class Fit
{
    /// <summary>A piece fits anyone whose size is within ±1/7 of the size it was made for.</summary>
    public static bool Fits(int itemSizeCm3, int bodySizeCm3) => Math.Abs((long)bodySizeCm3 - itemSizeCm3) * 7 <= itemSizeCm3;

    /// <summary>Can <paramref name="upper"/> be worn over <paramref name="lower"/>? (upper layer size ≤ lower layer permit)</summary>
    public static bool CanWearOver(ArmourShapeDef upper, ArmourShapeDef lower) => upper.LayerSize <= lower.LayerPermit;
}
