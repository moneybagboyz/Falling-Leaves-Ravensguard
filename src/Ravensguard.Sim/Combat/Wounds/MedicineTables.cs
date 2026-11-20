using Ravensguard.Sim.Combat.Resolver2;

namespace Ravensguard.Sim.Combat.Wounds;

/// <summary>The numbers of wounds 3.2 to 3.4 (the plan's constants) in one place. Thresholds in permille of blood lost.</summary>
public static class MedicineTables
{
    public const int TicksPerSimS = 20, SimSPerHour = 180, GameSecPerDay = 86_400, SimSPerDay = 4_320;
    public const int WeakPm = 150, CollapsePm = 300, UnconsciousPm = 400, DeadPm = 500;
    public const int PressurePm = 200, BandagePm = 200, TourniquetPm = 20;
    public const int PressureEffectDelayS = 4;
    public const int HeartCollapseTicks = 120, HeartDeadlySimS = 21;
    public const int BreathUnconsciousS = 30, BreathDeadS = 240;
    /// <summary>Blood regeneration: ml per game hour (x1000 by the feed state).</summary>
    public const int RegenMlPerHour = 25;
    public const int PainDecayPct = 98, WakeCalmS = 10;
    /// <summary>Infection per game day, permille: open, bandaged, stitched.</summary>
    public const int InfectOpenPm = 80, InfectDressedPm = 20, InfectStitchedPm = 10, InfectRisePm = 350;
    public const int TourniquetLossHours = 12;

    // clotting: 0.5^(age/90) in Q32 (integer tables only; no Math.Pow in sim code). Ratio 0.5^(1/90) = 4262016076 / 2^32.
    private const ulong Ratio = 4262016076UL;
    private static readonly ulong[] Clot = BuildClot();
    private static ulong[] BuildClot()
    {
        var t = new ulong[90];
        t[0] = 1UL << 32;
        for (int i = 1; i < t.Length; i++) t[i] = (ulong)(((UInt128)t[i - 1] * Ratio) >> 32);
        return t;
    }
    /// <summary>The share of a clotting wound's rate left after <paramref name="ageS"/> sim-seconds, Q32 (half after 90).</summary>
    public static ulong ClotQ32(long ageS)
    {
        if (ageS < 0) ageS = 0;
        long halves = ageS / 90;
        return halves >= 40 ? 0 : Clot[ageS % 90] >> (int)halves;
    }

    public static int DressPm(Dressing d) => d switch { Dressing.Pressure => PressurePm, Dressing.Bandage => BandagePm, Dressing.Tourniquet => TourniquetPm, _ => 1000 };

    /// <summary>Healing days x 10 for a wound, treated or untreated (wounds 3.4; the table is <c>medicine/healing.jsonc</c>, see <see cref="MedicineRules"/>). <paramref name="treated"/>: dressed or stitched, set or splinted, or repaired by surgery.</summary>
    public static int NeedX10(WoundCat cat, byte grade, int depthPm, bool treated, bool repaired)
    {
        var h = MedicineRules.Current.Healing;
        int mult = treated ? h.Treated : h.Untreated;
        static int At(int[] a, int i) => a[Math.Clamp(i, 0, a.Length - 1)];
        return cat switch
        {
            WoundCat.Skin or WoundCat.Inset or WoundCat.Vessel => depthPm < h.DeepFromPm ? (treated ? h.ShallowTreated : h.Shallow) : (treated ? h.DeepTreated : h.Deep),
            WoundCat.Muscle => At(h.Muscle, grade == 1 ? 0 : grade == 2 ? 1 : int.MaxValue) * mult / 10,
            WoundCat.Bone => grade is >= 1 and <= 4 ? At(treated ? h.BoneTreated : h.Bone, grade - 1) : h.BoneOther,
            WoundCat.Ligament => At(h.Ligament, grade == 1 ? 0 : 1) * mult / 10,
            WoundCat.Tendon => h.Tendon,
            WoundCat.Organ => repaired ? h.OrganRepaired : 0,
            WoundCat.Severed => h.Severed,
            _ => 0,
        };
    }

    /// <summary>Malunion chance for an unset fracture, percent (cracked 0).</summary>
    public static int MalunionPct(byte boneGrade) { var m = MedicineRules.Current.Healing.Malunion; return boneGrade is >= 1 and <= 4 && boneGrade - 1 < m.Length ? m[boneGrade - 1] : 0; }
}
