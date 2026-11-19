using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Resolver2;

/// <summary>
/// Burning (anatomy 4.3, the draugr's weakness): not a blow but a per-second wound step. A part on fire burns a share of its total layer depth per
/// sim-second (calibration <c>burn_pm_per_second</c>, 75), bone at a third of the rate; burnt to full depth it is destroyed, like a severance with
/// no blood. With the head's 11.5 mm (hair, skin, fat, bone) that is about 30 sim-seconds (test AN-12).
/// </summary>
public static class Burn
{
    /// <summary>Micrometres burnt per tick (20 Hz) in soft layers, x1000 for precision.</summary>
    private static long RateMilliUmPerTick(PartV2 part, CalibrationV2 c) => part.TotalLayerUm * c.BurnPmPerSecond / 20;

    /// <summary>Advances a burn by one 20 Hz tick. <paramref name="burntMilliUm"/> is the depth reached (micrometres x 1000). Returns true when the part is burnt through.</summary>
    public static bool Step(PartV2 part, CalibrationV2 c, ref long burntMilliUm)
    {
        long total = part.TotalLayerUm * 1000;
        if (burntMilliUm >= total) return true;
        // which layer is burning now
        long depth = 0;
        foreach (var l in part.Layers)
        {
            depth += l.ThicknessUm * 1000;
            if (burntMilliUm < depth)
            {
                long rate = RateMilliUmPerTick(part, c);
                if (l.IsBone) rate /= c.BoneBurnDivisor;
                burntMilliUm += rate < 1 ? 1 : rate;
                break;
            }
        }
        return burntMilliUm >= total;
    }

    /// <summary>Ticks until the part is burnt through (20 Hz).</summary>
    public static int TicksToBurnThrough(PartV2 part, CalibrationV2 c)
    {
        long b = 0; int t = 0;
        while (!Step(part, c, ref b) && t < 1_000_000) t++;
        return t + 1;
    }
}

/// <summary>Body variation at person creation (anatomy 6, stream 0x400): stature +-8%, blood volume +-5%, 10% left-handed.</summary>
public static class BodyVariation
{
    public readonly record struct Result(int SizeCm3, int BloodMl, bool LeftHanded);

    public static Result For(ulong seed, ulong person, BodyPlanV2 plan)
    {
        int size = DetRng.Range(seed, 0, person, RngStreams.BodyVariation, 0, -80, 81);
        int blood = DetRng.Range(seed, 0, person, RngStreams.BodyVariation, 1, -50, 51);
        bool left = DetRng.Range(seed, 0, person, RngStreams.BodyVariation, 2, 0, 1000) < 100;
        return new Result((int)((long)plan.SizeCm3 * (1000 + size) / 1000), (int)((long)plan.BloodMl * (1000 + blood) / 1000), left);
    }
}
