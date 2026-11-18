namespace Ravensguard.Sim.Goods;

/// <summary>Mass, volume and value of item instances in integer arithmetic (items spec 3.1, 3.2). Money is mites, mass grams.</summary>
public static class ItemMath
{
    /// <summary>Wage classes (mites per hour): unskilled 15, skilled 35, master 50.</summary>
    public const int WageUnskilled = 15, WageSkilled = 35, WageMaster = 50;

    /// <summary>
    /// <c>floor(volume_cm3 x density_kg_m3 / 1000)</c> grams. Volume is taken to 0.001 cm3 and density to whole kg/m3 so the result does not depend on float rounding
    /// (arming sword: 180 cm3 steel 7,850 = 1,413 g; 60 cm3 ash 680 = 40 g).
    /// </summary>
    public static long ComponentMassG(double volumeCm3, double densityKgM3)
    {
        long milliCm3 = (long)Math.Round(volumeCm3 * 1000.0), dens = (long)Math.Round(densityKgM3);
        return milliCm3 * dens / 1_000_000;
    }

    /// <summary>Material value in mites of a component: <c>mass_g x crowns_per_kg x 100 / 1000</c>, rounded half up.</summary>
    public static long MaterialMites(long massG, double valueCrownsPerKg)
    {
        long mitesPerKg = (long)Math.Round(valueCrownsPerKg * 100.0);
        return (massG * mitesPerKg + 500) / 1000;
    }

    /// <summary>Labour value in mites: <c>hours x wage x (1 + skill/200)</c>, rounded half up.</summary>
    public static long LabourMites(int hours, int wageMitesPerHour, int skill) => ((long)hours * wageMitesPerHour * (200 + skill) + 100) / 200;

    /// <summary>Condition multiplier in permille: <c>0.3 + 0.7 x condition</c>.</summary>
    public static int ConditionPermille(int conditionPermille) => 300 + 700 * Math.Clamp(conditionPermille, 0, 1000) / 1000;

    /// <summary>
    /// <c>(sum(material) + labour) x quality x condition</c> in mites, rounded half up once at the end; each stain takes 10% off (until washed).
    /// Common/new steel arming sword with 24 h skilled labour at skill 60 is 2,506 (spec G-I2).
    /// </summary>
    public static long ValueMites(long materialsAndLabourMites, ItemQuality quality, int conditionPermille, int stains = 0)
    {
        long q = ItemQualityRules.ValuePermille(quality), c = ConditionPermille(conditionPermille);
        long num = materialsAndLabourMites * q * c;                 // x 1e6
        long v = (num + 500_000) / 1_000_000;
        if (stains > 0) v = (v * Math.Max(0, 100 - 10 * stains) + 50) / 100;
        return v;
    }
}
