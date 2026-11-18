using Ravensguard.Sim.Core;
using Ravensguard.Sim.Carts;

namespace Ravensguard.Sim.Animals;

/// <summary>What a rider may ask of a horse and what it costs (horses-and-carts spec 4.1). Integer maths; every cost is per metre and every regeneration per game second, so a ride at 8x is the ride at 1x.</summary>
public static class RideMath
{
    public const int TiredPm = 800, UnfitPm = 1000, SlowStaminaSp = 1500, WindedFreeSp = 1000;

    /// <summary>Rider and gear against what the horse carries, permille.</summary>
    public static int LoadRatioPm(int loadKg, int carryKg) => carryKg <= 0 ? int.MaxValue : (int)((long)loadKg * 1000 / carryKg);
    /// <summary>Speed factor of a load: 1 - 0.5 x max(0, ratio - 0.8) / 0.4, permille.</summary>
    public static int LoadFactorPm(int ratioPm) => ratioPm <= 800 ? 1000 : Math.Max(0, 1000 - (ratioPm - 800) * 5 / 4);
    /// <summary>Stamina cost multiplier of a load, permille: 1 + max(0, ratio - 0.8).</summary>
    public static int LoadCostPm(int ratioPm) => ratioPm <= 800 ? 1000 : 1000 + (ratioPm - 800);

    /// <summary>Speed multiplier of a lameness level, permille (mild x0.7, severe x0.4, unusable 0).</summary>
    public static int LamePm(int level) => level switch { <= 0 => 1000, 1 => 700, 2 => 400, _ => 0 };

    /// <summary>The gait the horse will actually go at when the rider asks for <paramref name="want"/>: slower when it is tired, lame, winded or low on stamina (spec 4.1).</summary>
    public static Gait Allowed(Animal a, Breed b, Gait want, int ratioPm)
    {
        Gait g = want;
        if (b.TrotCmps <= 0 || ratioPm >= 1200 || a.Lameness >= 2 || a.Winded) g = Gait.Walk;
        else if (g == Gait.Gallop && (b.GallopCmps <= 0 || a.StaminaSp < SlowStaminaSp || a.WorkFatiguePm >= TiredPm || a.Lameness > 0)) g = Gait.Trot;
        return g;
    }

    /// <summary>The speed in cm per real second of a gait with the load, tiredness, weakness, lameness and surface of now.</summary>
    public static int SpeedCmps(Animal a, Breed b, Gait g, int ratioPm, bool bareback, int surfacePm = 1000)
    {
        long v = b.Cmps(g);
        v = v * LoadFactorPm(ratioPm) / 1000;
        if (a.WorkFatiguePm >= TiredPm) v = v * 800 / 1000;
        if (a.Weak) v = v * 850 / 1000;
        v = v * LamePm(a.Lameness) / 1000;
        if (bareback) v = v * 800 / 1000;
        return (int)(v * surfacePm / 1000);
    }

    /// <summary>Whether a rider with this load may get on at all: a horse above 120 percent of its load, or one worn out for the day, refuses.</summary>
    public static bool Refuses(Animal a, int ratioPm) => ratioPm > 1200 || a.WorkFatiguePm >= UnfitPm || a.Lameness >= 3;

    /// <summary>
    /// One game second of riding: <paramref name="movedMm"/> metres were covered at <paramref name="gait"/>. Trot and gallop cost stamina per metre (x the load's multiplier); walking regains
    /// <c>regen_walk</c>, standing <c>regen_rest</c>; the pool empties to "winded" and refills above 1,000 sp; fatigue, shoes and lameness follow the distance (<see cref="AnimalCare.OnMove"/>);
    /// a rider who keeps spurring a winded horse risks injury, rolled once per 20 game seconds (stream 0x350 on the window and the animal, so the roll belongs to the time, not the tick).
    /// </summary>
    public static void StepSecond(Animal a, Breed b, Gait gait, long movedMm, int ratioPm, long gameSec, ulong seed, Surface surface, bool forced, int studySkill = 0)
    {
        long max = b.StaminaSp * 1000L;
        if (movedMm > 0 && gait != Gait.Walk)
        {
            long cost = movedMm * b.CostPerM(gait) * LoadCostPm(ratioPm) / 1000;
            a.StaminaU = Math.Max(0, a.StaminaU - cost);
        }
        else if (movedMm > 0) a.StaminaU = Math.Min(max, a.StaminaU + b.RegenWalk * 1000L);
        else a.StaminaU = Math.Min(max, a.StaminaU + b.RegenRest * 1000L);
        if (a.StaminaU == 0) a.Winded = true;
        else if (a.Winded && a.StaminaSp > WindedFreeSp) a.Winded = false;
        if (forced && a.Winded)
        {
            long window = gameSec / 20;
            if (window != a.ForcedWindow)
            {
                a.ForcedWindow = window;
                if (DetRng.Range(seed, window, a.Id.Value, RngStreams.AnimalInjury, 0, 0, 1000) < 30) a.Lameness = (byte)Math.Max((int)a.Lameness, 1);
            }
        }
        if (movedMm > 0) AnimalCare.OnMove(a, b, movedMm, surface, gait != Gait.Walk, seed);
    }

    /// <summary>The chance (permille) that a try at taming succeeds: 200 + 4 x Riding + 2 x Husbandry - 3 x temper.</summary>
    public static int TameChancePm(int riding, int husbandry, int temper) => Math.Clamp(200 + 4 * riding + 2 * husbandry - 3 * temper, 0, 1000);

    /// <summary>One try at taming, once per ten game minutes: stream 0x354 keyed on the window, the animal and the rider's person index.</summary>
    public static bool TryTame(ulong seed, Animal a, long gameSec, int riderIndex, int riding, int husbandry)
        => DetRng.Range(seed, gameSec / 600, a.Id.Value, RngStreams.Taming, (uint)riderIndex, 0, 1000) < TameChancePm(riding, husbandry, a.Temper);
}

/// <summary>Why a mount is refused (or <see cref="Ok"/>).</summary>
public enum MountResult : byte { Ok, TooFar, NotRideable, NeedsSaddle, Untrained, TooLame, TooTired, Overloaded, Dead, Taken }

public static class MountRules
{
    public const long ReachMm = 2000;
    public const int MountSeconds = 40, DismountSeconds = 20, BarebackRiding = 20;

    /// <summary>
    /// Whether the rider may mount (spec 4.1): within 2 m, a riding breed, a saddle (bareback from Riding 20, at x0.8), trained to 700 (otherwise the caller tries to tame it), not lame beyond use,
    /// not worn out and not overloaded.
    /// </summary>
    public static MountResult Check(Animal a, Breed b, long riderXMm, long riderYMm, int ridingSkill, int riderKg)
    {
        if (!a.Alive) return MountResult.Dead;
        if (a.State is AnimalState.Ridden or AnimalState.Hitched) return MountResult.Taken;
        long dx = a.XMm - riderXMm, dy = a.YMm - riderYMm;
        if (dx * dx + dy * dy > ReachMm * ReachMm) return MountResult.TooFar;
        if (!b.CanRide) return MountResult.NotRideable;
        if (!a.Saddled && ridingSkill < BarebackRiding) return MountResult.NeedsSaddle;
        if (a.TrainedPm < 700) return MountResult.Untrained;
        if (a.Lameness >= 3) return MountResult.TooLame;
        int ratio = RideMath.LoadRatioPm(riderKg, b.CarryKg);
        if (a.WorkFatiguePm >= RideMath.UnfitPm) return MountResult.TooTired;
        if (ratio > 1200) return MountResult.Overloaded;
        return MountResult.Ok;
    }
}
