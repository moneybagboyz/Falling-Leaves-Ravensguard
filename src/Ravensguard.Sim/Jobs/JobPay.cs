using Ravensguard.Content;

namespace Ravensguard.Sim.Jobs;

/// <summary>The pay of each kind of job (live-behaviour spec 4.6), integer mites. <c>W_u</c> is the unskilled wage in mites per game day of a ten hour day, so <c>W_u / 10</c> is the pay of an hour.</summary>
public static class JobPay
{
    private static long CeilDiv(long a, long b) => (a + b - 1) / b;

    /// <summary>Hours a haul is priced at, thousandths: <c>path / pace x 2 + 0.25 h</c> (there and back, and a quarter hour to load and unload).</summary>
    public static long HaulMilliHours(OddJobTuningDef t, long pathMm) => pathMm * 2 / Math.Max(1, t.PaceMetresPerHour) + 250;

    /// <summary><c>ceil(multiple x W_u/10 x hours)</c>, the multiple in permille and the hours in thousandths.</summary>
    public static long Hourly(long multiplePm, long wageU, long milliHours) => CeilDiv(multiplePm * wageU * milliHours, 1000L * 10 * 1000);

    public static long Haul(OddJobTuningDef t, long wageU, long pathMm, long grams)
    {
        long pay = Hourly(t.PayMultiplePm, wageU, HaulMilliHours(t, pathMm));
        // a heavy load (over 100 kg) adds a few mites per started 100 kg-km
        if (grams > 100_000) pay += t.HaulMitesPer100KgKm * CeilDiv(grams / 1000 * (pathMm / 1000) , 100_000);
        return pay;
    }

    public static long Guard(OddJobTuningDef t, long wageU, long hours) => Hourly(t.GuardPayMultiplePm, wageU, hours * 1000);
    public static long Escort(OddJobTuningDef t, long wageU, long hours) => Hourly(t.EscortPayMultiplePm, wageU, hours * 1000);
    public static long Labour(OddJobTuningDef t, long wageU, long hours) => Hourly(t.PayMultiplePm, wageU, hours * 1000);
}
