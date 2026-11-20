using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Disease;

/// <summary>
/// The economy answers a fall in people (disease-and-plague 4.5): labour gets dear and land gets cheap. Two terms, recomputed at S5 and written to <see cref="EconomyState.WageMultPm"/> and
/// <see cref="EconomyState.LandMultPm"/>, which the labour market, the wage posts and the land book read (coin and mass are untouched: only prices move). Any fall of the working people or the
/// households from their peak triggers them (a war's as well as the plague's); a fall under the dead zone moves nothing, and a peak is forgotten 5 percent of the gap a season.
/// </summary>
public static class PlagueEconomy
{
    /// <summary>Workers a region has now: the living aged 16 to 59.</summary>
    public static int Workers(SimWorld w, int day)
    {
        var pt = w.People; int n = 0;
        for (int i = 0; i < pt.Count; i++)
        {
            if (pt.Status[i] != PersonStatus.Alive) continue;
            int age = w.AgeOn(i, day); if (age >= 16 && age < 60) n++;
        }
        return n;
    }

    /// <summary>Adults of working age with no post, no land and no trade: the idle that fill vacancies before wages rise.</summary>
    public static int Idle(SimWorld w, int day)
    {
        var pt = w.People; var hht = w.Households; int n = 0;
        for (int p = 0; p < pt.Count; p++)
        {
            if (pt.Status[p] != PersonStatus.Alive || pt.Employer[p] >= 0 || pt.Occupation[p] is not (Occupation.None or Occupation.Labourer)) continue;
            int hh = pt.Household[p]; if (hh < 0 || hht.Class[hh] == SocialClass.Gentry || hht.ParcelCount[hh] > 0) continue;
            int age = w.AgeOn(p, day); if (age >= 16 && age < 60) n++;
        }
        return n;
    }

    public static int HouseholdsNow(SimWorld w)
    {
        var hht = w.Households; int n = 0;
        for (int i = 0; i < hht.Count; i++) if (hht.MemberCount[i] > 0) n++;
        return n;
    }

    /// <summary>The wage term from the loss of workers: <c>clamp(1000 + 1500 x (lost - idle) / peak, 1000, 2000)</c> permille (a third of the workers dead and nobody idle: 1,500).</summary>
    public static int WageTarget(int peak, int now, int idle)
    {
        var s = DiseaseRules.Current.S;
        if (peak <= 0) return 1000;
        long lost = Math.Max(0, peak - now);
        if (lost * 1000 < (long)peak * s.DeadZonePm) return 1000;
        long shortage = Math.Max(0, lost - idle);
        return (int)Math.Clamp(1000 + s.WageSlopePm * shortage / Math.Max(1, peak), 1000, s.WageMaxPm);
    }

    /// <summary>The land term from the households that stand empty against the peak: <c>clamp(1000 - 1500 x share, 400, 1000)</c> permille (a third of the holdings empty: 500).</summary>
    public static int LandTarget(int peak, int now)
    {
        var s = DiseaseRules.Current.S;
        if (peak <= 0) return 1000;
        long empty = Math.Max(0, peak - now);
        if (empty * 1000 < (long)peak * s.DeadZonePm) return 1000;
        return (int)Math.Clamp(1000 - s.LandSlopePm * empty / Math.Max(1, peak), s.LandMinPm, 1000);
    }

    /// <summary>Wages fall back, and land recovers, 10 percent of the gap to 1000 a season; a worse target is taken at once.</summary>
    public static int EaseWage(int cur, int target)
    {
        if (target >= cur) return target;
        return Math.Max(target, cur - Math.Max(1, (cur - 1000) * DiseaseRules.Current.S.RecoverPct / 100));
    }

    public static int EaseLand(int cur, int target)
    {
        if (target <= cur) return target;
        return Math.Min(target, cur + Math.Max(1, (1000 - cur) * DiseaseRules.Current.S.RecoverPct / 100));
    }

    /// <summary>The S5 step: measure, ease, write. Runs once for a day.</summary>
    public static void Seasonal(SimWorld w, DiseaseBook b, int day)
    {
        var t = b.Scarcity; if (t.LastDay == day) return;
        t.LastDay = day;
        int workers = Workers(w, day), households = HouseholdsNow(w), decay = DiseaseRules.Current.S.PeakDecayPct;
        int wage = WageTarget(t.PeakWorkers, workers, Idle(w, day)), land = LandTarget(t.PeakHouseholds, households);
        t.WageMultPm = EaseWage(t.WageMultPm, wage); t.LandMultPm = EaseLand(t.LandMultPm, land);
        // the peaks: a higher count is the new peak at once, a lower one is forgotten slowly
        t.PeakWorkers = workers >= t.PeakWorkers ? workers : t.PeakWorkers - (t.PeakWorkers - workers) * decay / 100;
        t.PeakHouseholds = households >= t.PeakHouseholds ? households : t.PeakHouseholds - (t.PeakHouseholds - households) * decay / 100;
        Apply(w, b);
    }

    /// <summary>Hands the terms to the economy that reads them.</summary>
    public static void Apply(SimWorld w, DiseaseBook b)
    {
        w.Economy.WageMultPm = b.Scarcity.WageMultPm; w.Economy.LandMultPm = b.Scarcity.LandMultPm;
    }
}
