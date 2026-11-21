using Ravensguard.Content;

namespace Ravensguard.Sim.Production;

/// <summary>
/// Shared fixed costs, pure rules (P7-ES2, economies-of-scale 4.2): a hearth burns whatever works it, so two batches side by side share the lit hours; a firing burns a fixed amount however small the load. Integer
/// arithmetic only; <see cref="ProductionSystem.TryStart"/> applies them to the inputs a batch takes.
/// </summary>
public static class FixedCosts
{
    /// <summary>The heat row of a station kind, or null.</summary>
    public static ScaleHeatRow? HeatOf(ScaleSettingsDef s, string station) { foreach (var r in s.Heat) if (r.Station == station) return r; return null; }
    /// <summary>The firing row of a station kind, or null.</summary>
    public static ScaleFiringRow? FiringOf(ScaleSettingsDef s, string station) { foreach (var r in s.Firing) if (r.Station == station) return r; return null; }

    /// <summary>The fixed part of a recipe's fuel at a heat station: the lit hours at <c>lit_fuel_g_per_h</c>, never more than the recipe's whole fuel (ES-4: <c>forge_bar</c>'s 10 kg is 8 lit hours x 1 kg + 2 kg a batch).</summary>
    public static long HeatFixedG(long fuelQty, long litFuelGPerH, int recipeHours) => Math.Min(fuelQty, litFuelGPerH * recipeHours);

    /// <summary>
    /// The fuel a batch saves because a hearth beside it is already lit (4.2): the fixed part <paramref name="fixedG"/> over the minutes the two batches overlap, <c>fixed x overlap / batch minutes</c>
    /// (a batch started while its mate has the whole of its own hours left pays only its variable fuel: ES-4's second bar burns 2 kg, so two side by side burn 12 kg, 6 kg each).
    /// </summary>
    public static long SharedDiscountG(long fixedG, long batchMinutes, long mateRemainingMinutes)
    {
        if (fixedG <= 0 || batchMinutes <= 0 || mateRemainingMinutes <= 0) return 0;
        return fixedG * Math.Min(batchMinutes, mateRemainingMinutes) / batchMinutes;
    }

    /// <summary>A firing's fuel (4.2): <c>fixed + per-kg x load</c> (grams; the per-kg in permille of the load). ES-5: a Scotch brick kiln, fixed 4,000 kg, 300 permille, 60 t is 22,000 kg, 10 t is 7,000 kg.</summary>
    public static long FiringFuelG(long fixedG, int perKgPm, long loadG) => fixedG + loadG * perKgPm / 1000;

    /// <summary>Fuel a firing burns per kg of load (permille), the figure the planner compares: it falls as the kiln fills (ES-5: 0.37 at full, 0.70 at 10 t).</summary>
    public static long FuelPerKgPm(long fuelG, long loadG) => loadG <= 0 ? 0 : fuelG * 1000 / loadG;

    /// <summary>
    /// The quantity of a firing's fuel a batch of <paramref name="scalePm"/> burns when <paramref name="fixedPm"/> of the full load's fuel <paramref name="fullQty"/> is fixed: the fixed part whole plus the rest by the load.
    /// </summary>
    public static long FiringQty(long fullQty, int fixedPm, int scalePm)
    {
        long fixedG = fullQty * fixedPm / 1000;
        return fixedG + ((fullQty - fixedG) * scalePm + 999) / 1000;
    }

    /// <summary>The largest scale (permille, at most 1,000) a firing can start at with <paramref name="have"/> of the fuel on hand; 0 when even the fixed part is short.</summary>
    public static long FiringScale(long fullQty, int fixedPm, long have)
    {
        long fixedG = fullQty * fixedPm / 1000, rest = fullQty - fixedG;
        if (have < fixedG) return 0;
        if (rest <= 0) return 1000;
        return Math.Min(1000, (have - fixedG) * 1000 / rest);
    }

    /// <summary>Unit cost (4.2) of a day's output: <c>(fixed per day + variable per unit x units) / units</c>; it falls as output rises. Micro-mites throughout.</summary>
    public static long UnitCostMicro(long fixedPerDayMicro, long variablePerUnitMicro, long units) => units <= 0 ? 0 : (fixedPerDayMicro + variablePerUnitMicro * units) / units;
}
