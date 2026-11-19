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

    /// <summary>Healing days x 10 for a wound, treated or untreated (wounds 3.4). <paramref name="treated"/>: dressed or stitched, set or splinted, or repaired by surgery.</summary>
    public static int NeedX10(WoundCat cat, byte grade, int depthPm, bool treated, bool repaired) => cat switch
    {
        WoundCat.Skin or WoundCat.Inset or WoundCat.Vessel => depthPm < 500 ? (treated ? 60 : 100) : (treated ? 80 : 140),
        WoundCat.Muscle => (grade switch { 1 => 30, 2 => 150, _ => 400 }) * (treated ? 10 : 15) / 10,
        WoundCat.Bone => grade switch
        {
            1 => treated ? 100 : 150,
            2 => treated ? 200 : 350,
            3 => treated ? 280 : 450,
            4 => treated ? 400 : 600,
            _ => 30,
        },
        WoundCat.Ligament => (grade == 1 ? 120 : 300) * (treated ? 10 : 15) / 10,
        WoundCat.Tendon => 400,
        WoundCat.Organ => repaired ? 300 : 0,
        WoundCat.Severed => 100,
        _ => 0,
    };

    /// <summary>Malunion chance for an unset fracture, percent (cracked 0).</summary>
    public static int MalunionPct(byte boneGrade) => boneGrade switch { 2 => 50, 3 => 80, 4 => 100, _ => 0 };
}
