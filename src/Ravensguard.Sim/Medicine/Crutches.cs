using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;

namespace Ravensguard.Sim.Medicine;

/// <summary>
/// Crutches and aids (medicine 4.6, <c>medicine/aids.jsonc</c>). A crutch in a hand gives the Phase 4 aid capacity (one usable leg: the hop from 250 to 550 permille); it occupies that hand, so
/// no shield and no two-handed weapon while it is held, and two crutches occupy both hands. Two crutches with no usable leg give 0 and the patient sits. A litter is carried by two at x0.6 their speed.
/// </summary>
public static class Crutches
{
    private static Ravensguard.Content.MedicalAidDef Crutch => MedicineRules.Current.Aids.TryGetValue("crutch", out var a) ? a : new() { Id = "crutch", Hands = 1, OneLegPm = 550 };

    /// <summary>The body's aids for a person holding <paramref name="crutches"/> crutches (0, 1 or 2); feed it to <see cref="BodyState"/>.</summary>
    public static BodyAids Aids(int crutches, ulong splintedMask = 0, ulong repairedMask = 0) => new(Crutch: crutches > 0, SplintedMask: splintedMask, RepairedMask: repairedMask);

    /// <summary>The walk capacity, permille of a sound man's, with <paramref name="usableLegs"/> legs and <paramref name="crutches"/> crutches in hand (two legs: unchanged by the aid).</summary>
    public static int WalkPm(int usableLegs, int crutches, int soundPm = 1000)
    {
        var a = Crutch;
        if (usableLegs >= 2) return soundPm;
        if (usableLegs == 1) return crutches > 0 ? a.OneLegPm : 250;
        return crutches >= 2 ? a.NoLegPm : 0;
    }

    /// <summary>True when the patient can only sit: no usable leg and not two crutches (or no crutches).</summary>
    public static bool Seated(int usableLegs, int crutches) => usableLegs <= 0;

    /// <summary>Hands a crutch takes of the person's two.</summary>
    public static int HandsTaken(int crutches) => Math.Clamp(crutches, 0, 2) * Crutch.Hands;

    /// <summary>True when a crutch hand cannot hold the item: a shield wants a hand, a two-handed weapon both.</summary>
    public static bool CanHold(int crutches, int handsNeeded) => 2 - HandsTaken(crutches) >= handsNeeded;

    /// <summary>A litter's speed with its carriers, permille of the slowest carrier's (needs two).</summary>
    public static int LitterSpeedPm(int carriers, int carrierSpeedPm)
    {
        MedicineRules.Current.Aids.TryGetValue("litter", out var l);
        int need = l?.Carriers ?? 2, pm = l?.CarrySpeedPm ?? 600;
        return carriers >= need ? carrierSpeedPm * pm / 1000 : 0;
    }
}
