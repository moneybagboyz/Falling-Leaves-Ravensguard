using Ravensguard.Content;

namespace Ravensguard.Sim.Combat.Resolver2;

// Resolver v2 records (docs/specs/phase4/resolver-v2.md 3.2). Every number is an integer: momentum in milli-units (1,000 = 1.0),
// lengths in micrometres, areas in milli-cm2, chances in permille, ppm or basis points as named. No floating point anywhere in this folder
// outside the load step (CombatTablesV2 converts the content's decimals once).

public enum MuscleGrade : byte { Sound, Bruised, Torn, Mangled }
public enum BoneGrade : byte { Sound, Cracked, Broken, Displaced, Shattered }
public enum JointGrade : byte { Sound, Sprained, Torn, Ruptured }

/// <summary>Which blow this is, for the counter RNG: every draw is <c>DetRng(seed, FightTick, attacker, stream, swingSerial x 64 + k)</c> (resolver-v2 6).</summary>
public readonly record struct BlowKey(ulong Seed, long FightTick, ulong Attacker, uint SwingSerial)
{
    public uint Draw(int k) => SwingSerial * 64u + (uint)k;
}

/// <summary>A worn piece as the resolver sees it: the precomputed piece stats plus the item's own quality, condition, fit and visor state.</summary>
public struct WornV2
{
    public ArmourV2 Stats;
    /// <summary>Quality multiplier Qa, permille (1000 standard).</summary>
    public int QualityPm;
    /// <summary>Item condition, permille (worn 800, threadbare 500, tattered 250).</summary>
    public int ConditionPm;
    /// <summary>The body size the piece was made for (a piece fits within +-1/7).</summary>
    public int FitCm3;
    public bool VisorOpen;

    public static WornV2 Of(ArmourV2 stats, int qualityPm = 1000, int conditionPm = 1000, int fitCm3 = 70000, bool visorOpen = false) =>
        new() { Stats = stats, QualityPm = qualityPm, ConditionPm = conditionPm, FitCm3 = fitCm3, VisorOpen = visorOpen };
}

public enum BlockKind : byte { None, Weapon, Shield, Parry, Dodge }

/// <summary>The defender's active defence for this blow (resolver-v2 4.8). Timing and the parry roll belong to the melee layer.</summary>
public struct BlockInput
{
    public BlockKind Kind;
    /// <summary>Shield blocks: the shield stats and its condition.</summary>
    public ShieldV2? Shield;
    public int ShieldConditionPm;
    public int BlockerSkill;
    /// <summary>Braced shield neighbours (shield wall, max 2 count).</summary>
    public int BracedNeighbours;
    /// <summary>The blocking arm's forearm part index: the excess of a blow above capacity lands there, blunt.</summary>
    public int ArmPart;
    /// <summary>Dodge: the blow starts inside the dodge window.</summary>
    public bool InDodgeWindow;
    /// <summary>Parry: the melee layer's parry roll succeeded.</summary>
    public bool ParrySucceeded;
}

public struct BlockOutcome
{
    public LayerOutcome Outcome;
    /// <summary>The blow ends here.</summary>
    public bool Stopped;
    /// <summary>Momentum above capacity that continues, blunt, on the blocking forearm.</summary>
    public long ExcessMilli;
    /// <summary>Stamina the blocker pays, x100.</summary>
    public int StaminaX100;
    /// <summary>Shield condition lost, permille; a held weapon's notch, permille.</summary>
    public int ShieldLossPm, NotchPm;
    public bool Stagger;
}

/// <summary>One blow's input (resolver-v2 3.2 <c>StrikeInput</c>). A struct passed by reference: building one allocates nothing.</summary>
public struct StrikeV2
{
    // attacker
    public int Strength;
    public int Skill;
    public int SizeCm3;
    public int SpeciesSizeCm3;
    /// <summary>Multiplier on the blow, permille: 850 when the striking hand's sensory nerve is cut (anatomy 3.6), 500 for blunted practice weapons.</summary>
    public int MomentumPm;
    public WeaponV2 Weapon;
    public int Attack;
    public AttackStyle Style;
    // defender
    public BodyPlanV2 Plan;
    public int DefenderSizeCm3;
    public int DefenderSkill;
    public WornV2[]? Worn;
    public int WornCount;
    /// <summary>Bits over part indices: zones the defender is guarding (hit chance x0.6).</summary>
    public ulong GuardedMask;
    /// <summary>Bits over part indices: parts already severed (a blow there misses).</summary>
    public ulong MissingMask;
    public bool Prone;
    // the aim
    public int TargetPart;
    /// <summary>Index of the named gap aimed at, or -1.</summary>
    public int TargetGap;
    public Facing Facing;
    /// <summary>Overview Q24: light, footing, mounted, wet; added to the skill delta.</summary>
    public int SituationPoints;
    /// <summary>Mounted charge, missile launch, fall (their files), milli-units.</summary>
    public long ExtraMomentumMilli;
    /// <summary>A missile (arrow, bolt, thrown object: melee-and-ranged 4.5, 4.6): the blow's momentum is <see cref="ExtraMomentumMilli"/> alone, from the launcher or the throw, not the arm.</summary>
    public bool Missile;
    /// <summary>True when the blow is shown near the camera. The resolver never reads it (test RS-23).</summary>
    public bool Near;
    public BlockInput Block;

    public static StrikeV2 Create(WeaponV2 weapon, int attack, BodyPlanV2 plan, int targetPart, int skill = 0, int strength = 1250)
        => new()
        {
            Strength = strength, Skill = skill, SizeCm3 = 70000, SpeciesSizeCm3 = 70000, MomentumPm = 1000, Weapon = weapon, Attack = attack,
            Plan = plan, DefenderSizeCm3 = plan.SizeCm3, TargetPart = targetPart, TargetGap = -1,
        };
}

/// <summary>One wound record (resolver-v2 3.2). <see cref="Grade"/> is a <see cref="MuscleGrade"/>, <see cref="BoneGrade"/> or <see cref="JointGrade"/> by tissue.</summary>
public struct WoundV2
{
    public short Part;
    public short Tissue;
    /// <summary>Index into the part's structures, or -1.</summary>
    public short Structure;
    /// <summary>Index into the part's organs, or -1.</summary>
    public short Organ;
    public WoundKind Kind;
    /// <summary>Fraction of the layer reached, permille.</summary>
    public int DepthPm;
    public byte Grade;
    /// <summary>Fingers or fifths of teeth lost.</summary>
    public byte Count;
    /// <summary>Partial cord crush: bit per limb group (left arm, right arm, left leg, right leg) that keeps 400 pm.</summary>
    public byte Residual;
    /// <summary>Bleeding in ml/s x 10.</summary>
    public int BleedX10;
    public WoundFlags Flags;

    public readonly MuscleGrade Muscle => (MuscleGrade)Grade;
    public readonly BoneGrade Bone => (BoneGrade)Grade;
    public readonly JointGrade Joint => (JointGrade)Grade;
    public readonly bool Has(WoundFlags f) => (Flags & f) == f;
}

/// <summary>One step of the blow's path (the trace that feeds the log and the tooltip).</summary>
public struct LayerStep
{
    /// <summary>Worn piece index (into the input's worn array) or -1 for a tissue layer.</summary>
    public short Piece;
    public short Tissue;
    public short Part;
    public bool IsArmour;
    public LayerOutcome Outcome;
    public long InMilli, OutMilli;
    /// <summary>The need the layer set (cut need while edged, else crush need), milli-units.</summary>
    public long NeedMilli;
    /// <summary>Condition the worn piece lost, permille (resolver-v2 4.3 wear).</summary>
    public int WearPm;
}

/// <summary>A draw the resolver made: stream and k (test RS-22 locks the sequence).</summary>
public readonly record struct DrawRec(uint Stream, int K);

/// <summary>
/// The result of one blow, written into a reusable buffer (resolver-v2 5: no allocation per resolution once the buffer exists).
/// </summary>
public sealed class BlowResult
{
    public const int MaxWounds = 32, MaxSteps = 32, MaxDraws = 96;
    public bool Hit, Redirected, Severed, Deflected, Blocked, Dodged, Parried, RibSlip;
    public int Part = -1;
    public long InitialMilli, FinalMilli;
    public BlockOutcome Block;
    public readonly WoundV2[] Wounds = new WoundV2[MaxWounds];
    public int WoundCount;
    public readonly LayerStep[] Steps = new LayerStep[MaxSteps];
    public int StepCount;
    public readonly DrawRec[] Draws = new DrawRec[MaxDraws];
    public int DrawCount;

    public ReadOnlySpan<WoundV2> WoundSpan => Wounds.AsSpan(0, WoundCount);
    public ReadOnlySpan<LayerStep> StepSpan => Steps.AsSpan(0, StepCount);
    public ReadOnlySpan<DrawRec> DrawSpan => Draws.AsSpan(0, DrawCount);

    public void Reset()
    {
        Hit = Redirected = Severed = Deflected = Blocked = Dodged = Parried = RibSlip = false;
        Part = -1; InitialMilli = FinalMilli = 0; Block = default; WoundCount = StepCount = DrawCount = 0;
    }

    internal void Add(in WoundV2 w) { if (WoundCount < MaxWounds) Wounds[WoundCount++] = w; }
    internal void Add(in LayerStep s) { if (StepCount < MaxSteps) Steps[StepCount++] = s; }
    internal void Drew(uint stream, int k) { if (DrawCount < MaxDraws) Draws[DrawCount++] = new DrawRec(stream, k); }

    /// <summary>FNV-1a over every field of the result (the ResolverHash of resolver-v2 6).</summary>
    public ulong Hash(ulong h = 14695981039346656037UL)
    {
        static ulong Mix(ulong h, long v) { unchecked { for (int i = 0; i < 8; i++) { h ^= (byte)(v >> (8 * i)); h *= 1099511628211UL; } } return h; }
        h = Mix(h, (Hit ? 1 : 0) | (Redirected ? 2 : 0) | (Severed ? 4 : 0) | (Deflected ? 8 : 0) | (Blocked ? 16 : 0) | (Dodged ? 32 : 0) | (Parried ? 64 : 0) | (RibSlip ? 128 : 0));
        h = Mix(h, Part); h = Mix(h, InitialMilli); h = Mix(h, FinalMilli); h = Mix(h, Block.ExcessMilli);
        for (int i = 0; i < WoundCount; i++)
        {
            ref var w = ref Wounds[i];
            h = Mix(h, w.Part); h = Mix(h, w.Tissue); h = Mix(h, w.Structure); h = Mix(h, w.Organ); h = Mix(h, (long)w.Kind);
            h = Mix(h, w.DepthPm); h = Mix(h, w.Grade); h = Mix(h, w.Count); h = Mix(h, w.Residual); h = Mix(h, w.BleedX10); h = Mix(h, (long)w.Flags);
        }
        for (int i = 0; i < StepCount; i++)
        {
            ref var s = ref Steps[i];
            h = Mix(h, s.Piece); h = Mix(h, s.Tissue); h = Mix(h, s.Part); h = Mix(h, (long)s.Outcome); h = Mix(h, s.InMilli); h = Mix(h, s.OutMilli); h = Mix(h, s.WearPm);
        }
        return h;
    }
}
