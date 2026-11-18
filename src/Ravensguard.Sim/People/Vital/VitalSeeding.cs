using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.People.Vital;

/// <summary>
/// Gives a freshly generated world the vital state it would have had mid-history. World generation makes people, not pregnancies, so on day 0 nobody is
/// expecting and the first 45 days would have no births at all (a hole in year 1 that the G-P12 band does not allow). Called once, when the abstract tick first
/// takes over a world (<see cref="SimWorld.From"/>); never on a loaded save, whose pregnancies are saved.
/// </summary>
public static class VitalSeeding
{
    private const uint StreamDraw = 7;

    /// <summary>
    /// Each married woman of fertile age is pregnant with the steady-state probability <c>pregnancy / (conception wait + pregnancy + postpartum)</c> =
    /// 45 / 260, due on a uniformly drawn day within the next 45; the father is her spouse. Pure function of (seed, start hour, woman).
    /// </summary>
    public static int SeedPregnancies(SimWorld w, GameTime start)
    {
        var pt = w.People; var r = w.Rates; long day = start.TotalDays; int n = 0;
        int waitDays = 1_000_000 / Math.Max(1, VitalMath.ConceptionPpm(r, 3000, 100)); // mean days to conceive when well fed (5,000 ppm: 200)
        int ppm = (int)(1_000_000L * r.PregnancyDays / (waitDays + r.PregnancyDays + r.PostpartumDays));
        for (int p = 0; p < pt.Count; p++)
        {
            if (pt.Status[p] != PersonStatus.Alive || pt.Sex[p] != Sex.Female || pt.PregnantUntilDay[p] >= 0) continue;
            int sp = pt.Spouse[p];
            if (sp < 0 || pt.Status[sp] != PersonStatus.Alive || pt.Household[sp] != pt.Household[p]) continue;
            int age = w.AgeOn(p, day);
            if (age < r.FertilityAgeMin || age > r.FertilityAgeMax) continue;
            ulong id = pt.IdOf(p).Value;
            if (!VitalMath.Roll(w.Seed, start.TotalHours, id, VitalStreams.Conception, StreamDraw, ppm)) continue;
            pt.PregnantUntilDay[p] = (int)day + 1 + DetRng.Range(w.Seed, start.TotalHours, id, VitalStreams.Conception, StreamDraw + 1, 0, r.PregnancyDays);
            pt.FatherOfUnborn[p] = sp; n++;
        }
        return n;
    }
}
