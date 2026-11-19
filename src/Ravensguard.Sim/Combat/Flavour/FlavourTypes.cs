using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;

namespace Ravensguard.Sim.Combat.Flavour;

// Feedback and flavour (feedback-flavour-log.md, P4-D): presentation over the fight's events. Nothing in this folder is read by the simulation or hashed into the FightHash or the
// state hash; the recorder only listens (FightWorld.OnBlow and OnEvent). Text is built by the formatter from localisable templates.

/// <summary>Bits of <see cref="FightLine.Flags"/>.</summary>
[Flags]
public enum LineFlags : ushort
{
    None = 0, Heavy = 1, Quick = 2, Called = 4, Flank = 8, Rear = 16, Redirected = 32, Missile = 64, Thrown = 128, Shield = 256, Gap = 512, Severed = 1024, Overpowered = 2048,
}

/// <summary>Who stood in the fight (names, side, the body kind for the prose); captured when first seen and saved with the log.</summary>
public sealed class FighterInfo
{
    public int Id;
    public string Name = "";
    public int Team;
    public bool Player;
    /// <summary>The wound book's person (0 for a nameless soldier of the harness); a named person gets a chronicle entry.</summary>
    public ulong Person;
    public bool Female;
    /// <summary>human, horse, draugr or beast (the target kind of the templates).</summary>
    public string Kind = "human";
    public string PlanId = "human";
    /// <summary>The state the fight left this fighter in: a <see cref="FAct"/> value (Idle, Down, Dead, Fled, Surrendered).</summary>
    public byte End;
    // statistics for the report
    public int Swings, Landed, Blocked, Parried, Dodged, Missed;
    public int Edge, Point, Blunt, Missile;
    public int LossHighPm, StaminaLow = 10000;
    /// <summary>The line of the strongest blow this fighter dealt (severity, then momentum), or -1.</summary>
    public int BestLine = -1, BestSeverity = -1;
    public long BestMomentum;
    /// <summary>The last line that wounded this fighter, and the one whose wounds bled most (the cause of a bleed-out), or -1 (line serials).</summary>
    public int LastHitSerial = -1, WorstHitSerial = -1, WorstBleedX10;
    public int DownSerial = -1, DeathSerial = -1;
    public bool Named => Person != 0;
}

/// <summary>One wound of a blow, as text needs it (ids, not indices, so a saved log reads without the content tables).</summary>
public struct WoundSnap
{
    public ushort Part, Tissue, Structure, Organ;
    public WoundKind Kind;
    public byte Grade, Count;
    public int DepthPm, BleedX10;
    public WoundFlags Flags;
}

/// <summary>One step of the blow's path through the layers (the trace the log expands to).</summary>
public struct StepSnap
{
    /// <summary>Index into <see cref="BlowSnap.PieceShapes"/>, or -1 for a tissue layer.</summary>
    public short Piece;
    public ushort Tissue, Part;
    public bool IsArmour;
    public LayerOutcome Outcome;
    public long InMilli, OutMilli, NeedMilli;
    public int WearPm;
}

/// <summary>The layer trace and wound list of one blow, kept beside the lines that tell it. <see cref="FightLog.Str"/> turns the interned ids back into words.</summary>
public sealed class BlowSnap
{
    public int Serial, Tick;
    public short Attacker, Defender;
    public ushort WeaponShape, WeaponMaterial;
    public byte Attack;
    /// <summary>The kind of attack for the prose (<see cref="AttackKinds"/>) and the verb the weapon's attack names ("slashes"), interned; the weapon family likewise.</summary>
    public byte AKind;
    public ushort Verb, Family;
    public AttackType AttackType;
    public bool Missile, Thrown, Called, Redirected, Severed, Hit, Blocked;
    public BlockKind Block;
    public Facing Facing;
    public AttackStyle Style;
    public ushort Part;
    public long InitialMilli, FinalMilli, ExcessMilli;
    public ushort[] PieceShapes = [], PieceMaterials = [];
    public StepSnap[] Steps = [];
    public WoundSnap[] Wounds = [];
}

/// <summary>One thing that happened, with the severity the sim's own wound list gives it. Text is built later (<see cref="FlavourFormatter"/>); the picks are kept so a line reads the same however often it is asked for.</summary>
public struct FightLine
{
    public int Serial, Tick;
    public FightEventKind Kind;
    public sbyte Severity;
    public LineFlags Flags;
    public short Actor, Target, Part;
    public int A, B;
    /// <summary>Index of the blow's snapshot in <see cref="FightLog.Blows"/>, or -1.</summary>
    public int Blow;
    /// <summary>The attacker's weapon at the time (interned shape and material ids, and the attack index) for lines that have no blow.</summary>
    public ushort WeaponShape, WeaponMaterial;
    public byte Attack;
    public byte AKind;
    public ushort Verb, Family;
    /// <summary>The templates picked for this line, as 1 + the book's template index (0: none). Set by the formatter's cursor and saved.</summary>
    public ushort P0, P1, P2, P3, P4;
    public bool Picked;

    public readonly bool IsSerious => Severity >= 2 || Kind is FightEventKind.Down or FightEventKind.Death or FightEventKind.Rout or FightEventKind.Surrender or FightEventKind.Fled || (Flags & LineFlags.Severed) != 0;
}

/// <summary>The kinds of attack the templates are written for (<c>when.attack</c>): the order is the byte stored in a line.</summary>
public static class AttackKinds
{
    public static readonly string[] Names = ["edge", "point", "blunt", "missile", "thrown", "fall", "bite", "claw"];
    public const byte Edge = 0, Point = 1, Blunt = 2, Missile = 3, Thrown = 4, Fall = 5, Bite = 6, Claw = 7;

    public static byte Of(Ravensguard.Sim.Combat.Resolver2.WeaponV2 w, int attack, bool missile, bool thrown)
    {
        var a = w.Attacks[Math.Clamp(attack, 0, w.Attacks.Length - 1)];
        if (a.Id == "bite") return Bite;
        if (a.Id == "claw") return Claw;
        if (missile) return thrown ? Thrown : Missile;
        return (byte)(a.Type == AttackType.Edge ? Edge : a.Type == AttackType.Point ? Point : Blunt);
    }
}

/// <summary>Severity classes (feedback-flavour-log 4.1), set from the wound list, never from text.</summary>
public static class Severity
{
    public const int Graze = 0, Wound = 1, Grievous = 2, Mortal = 3, Fatal = 4;

    /// <summary>0 graze (skin and fat only), 1 wound (muscle bruised or torn), 2 grievous (muscle mangled, bone broken, a structure cut), 3 mortal (an organ, an artery, a severed limb, the spine), 4 instantly fatal (heart, brain, a severed head or neck).</summary>
    public static int Of(ReadOnlySpan<WoundSnap> wounds, Func<ushort, string> str)
    {
        int s = 0;
        foreach (var w in wounds)
        {
            string baseId = BaseOf(str(w.Part));
            int v;
            if ((w.Flags & WoundFlags.Vital) != 0) v = Fatal;
            else if ((w.Flags & WoundFlags.Severed) != 0) v = baseId is "head" or "neck" ? Fatal : Mortal;
            else if ((w.Flags & (WoundFlags.OrganHit | WoundFlags.SpineHit)) != 0) v = Mortal;
            else if ((w.Flags & WoundFlags.ArteryHit) != 0) v = baseId is "upper_arm" or "upper_leg" or "neck" or "upper_body" or "lower_body" or "head" ? Mortal : Grievous;
            else if ((w.Flags & (WoundFlags.BoneBroken | WoundFlags.NerveHit | WoundFlags.TendonHit | WoundFlags.LigamentHit | WoundFlags.VeinHit | WoundFlags.MotorNerveHit | WoundFlags.SensoryNerveHit | WoundFlags.Concussion)) != 0) v = Grievous;
            else
            {
                string tissue = str(w.Tissue);
                v = tissue == "muscle" ? (w.Grade >= (byte)MuscleGrade.Mangled ? Grievous : w.Kind == WoundKind.Cut || w.Grade >= (byte)MuscleGrade.Bruised ? Wound : Graze)
                    : tissue.StartsWith("bone", StringComparison.Ordinal) ? Wound : Graze;
            }
            if (v > s) s = v;
        }
        return s;
    }

    /// <summary>The part id without its side suffix.</summary>
    public static string BaseOf(string part) => part.EndsWith("_l", StringComparison.Ordinal) || part.EndsWith("_r", StringComparison.Ordinal) ? part[..^2] : part;
}
