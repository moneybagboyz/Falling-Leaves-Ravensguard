using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Clothing;

/// <summary>
/// The abstract tier's exposure (clothing spec 4.6): no item rows and no per-person wetness, one cached outfit value per person and the same math as the live tier, run only for the hours a person is outdoors
/// in extreme weather. Pure and allocation-free, so it costs a few hundred nanoseconds a person-hour and nothing in mild weather.
/// </summary>
public static class AbstractExposure
{
    /// <summary>
    /// A person's outfit insulation, clo x 100 (spec 4.6): pieces owned per member below one 40, one 90, two or more 140; 40 more for a cloak or fur in the household, 40 more for gentry.
    /// <paramref name="piecesPerMemberX100"/> is pieces x 100 / members.
    /// </summary>
    public static int OutfitI(int piecesPerMemberX100, bool cloakOrFur, bool gentry)
    {
        int i = piecesPerMemberX100 < 100 ? 40 : piecesPerMemberX100 < 200 ? 90 : 140;
        return Math.Min(255, i + (cloakOrFur ? 40 : 0) + (gentry ? 40 : 0));
    }

    /// <summary>True for the hours the abstract exposure runs: outdoors and a wind-chilled temperature under 5 C, or an air temperature over 30 C (spec 4.6).</summary>
    public static bool IsExtreme(ExposureTuning t, in ExposureEnv e, int outfitI)
    {
        if (e.Roofed) return false;
        int tEff = e.TempCenti - ClothingMath.WindChillCenti(t, e.WindCmps, 400);
        return tEff < 500 || e.TempCenti > 3000;
    }

    /// <summary>The cold stress a person of this outfit would feel (permille), for the warmth need of the shared scorer.</summary>
    public static int ColdStressPm(ExposureTuning t, int outfitI, in ExposureEnv e, Activity act)
    {
        int ins = e.PrecipTenthsMmH > 0 && !e.Roofed ? outfitI * 700 / 1000 : outfitI;       // a rainy hour: 30% of the insulation lost on average
        int tEff = ClothingMath.TAir(t, e) - (e.Roofed ? 0 : ClothingMath.WindChillCenti(t, e.WindCmps, 400));
        return ClothingMath.ColdStressPm(t, ClothingMath.IReq(t, tEff, act), ins);
    }

    /// <summary>
    /// One game hour of a person's core in <see cref="ClothingMath.CoreUnit"/> units: the same rate as sixty live minutes. Returns the new core; <paramref name="died"/> is set when the person ends the hour at
    /// lethal cold (an abstract person outside at that point is taken to be unconscious, the live rule's collapse) or in heatstroke range for a whole hour.
    /// </summary>
    public static long StepHour(ExposureTuning t, long coreU, int outfitI, in ExposureEnv e, Activity act, int resistPm, out ExposureDeath died)
    {
        int ins = e.PrecipTenthsMmH > 0 && !e.Roofed ? outfitI * 700 / 1000 : outfitI;
        int tAir = ClothingMath.TAir(t, e);
        int tEff = tAir - (e.Roofed ? 0 : ClothingMath.WindChillCenti(t, e.WindCmps, 400));
        int cold = ClothingMath.ColdStressPm(t, ClothingMath.IReq(t, tEff, act), ins);
        int heat = ClothingMath.HeatStressPm(t, e.Shade ? tAir - 400 : tAir, act, ins, 0);
        long rate = ClothingMath.CoreRatePerHour(t, cold, heat, ClothingMath.CentiOf(coreU), e.Fire, resistPm, true);
        coreU = Math.Max(0, coreU + rate * 60);
        int c = ClothingMath.CentiOf(coreU);
        died = c < t.Def.LethalCc ? ExposureDeath.Cold : c > t.Def.StrokeCc + 100 ? ExposureDeath.Heat : ExposureDeath.None;
        return coreU;
    }
}
