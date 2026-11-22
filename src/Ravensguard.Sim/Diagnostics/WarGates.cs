namespace Ravensguard.Sim.Diagnostics;

/// <summary>One gate of the war-load soak (economy-audit 5.3) with its measured result.</summary>
public sealed record GateResult(string Id, AuditState State, string Text);

/// <summary>
/// The war-load soak's gates WL-1 to WL-15 read from the yearly rows (P7-EA2). A gate whose measure needs a part not built at the audited tag (purveyance, kits, chronicle causes of the army, the real abstract tick's budget) is
/// <see cref="AuditState.Pending"/> and names the part; a gate that needs the control run's rows is measured only when they are given. Years are 1-based rows; the baseline is the mean of years 3 to 5.
/// </summary>
public static class WarGates
{
    public static IReadOnlyList<GateResult> Evaluate(IReadOnlyList<WarYearRow> war, IReadOnlyList<WarYearRow>? control, GateResult? wl13 = null)
    {
        var res = new List<GateResult>(); int n = war.Count;
        WarYearRow? Y(IReadOnlyList<WarYearRow>? rows, int y) => rows != null && y >= 1 && y <= rows.Count ? rows[y - 1] : null;
        double Base(Func<WarYearRow, long> f) { var xs = new[] { 3, 4, 5 }.Select(y => Y(war, y)).Where(r => r != null).Select(r => f(r!)).ToArray(); return xs.Length == 0 ? 0 : xs.Average(); }
        void Add(string id, AuditState s, string t) => res.Add(new GateResult(id, s, t));

        Add("WL-1", war.All(r => r.CoinOk) ? AuditState.Pass : AuditState.Fail, "coin conserved every day, region and continent");
        Add("WL-2", war.All(r => r.MassOk) ? AuditState.Pass : AuditState.Fail, "every good's mass audit exact every day (no Other destroy: EconAudit L4)");

        // WL-3 / G4 money supply
        if (n >= 3)
        {
            double first = war[0].MoneySupply, last = war[n - 1].MoneySupply, avg = Math.Pow(last / first, 1.0 / (n - 1)) - 1;
            double lo = double.MaxValue, hi = double.MinValue; for (int i = 1; i < n; i++) { double g = (double)war[i].MoneySupply / war[i - 1].MoneySupply - 1; lo = Math.Min(lo, g); hi = Math.Max(hi, g); }
            bool ok = avg is >= 0.005 and <= 0.025 && lo >= -0.05 && hi <= 0.08;
            Add("WL-3", ok ? AuditState.Pass : AuditState.Fail, $"money supply {first / 100:N0} to {last / 100:N0} crowns over {n - 1} years = {avg:P2} a year (pass 0.5-2.5%); worst year {lo:P1}, best {hi:P1} (pass -5% to +8%)");
        }
        // WL-4 bread on the march route
        double routeBase = Base(r => r.RouteMeanMilli), regionBase = Base(r => r.RegionMeanMilli);
        if (routeBase > 0 && n >= 6)
        {
            double routePeak = war.Skip(5).Max(r => r.RoutePeakMilli) * 1000 / routeBase, regionPeak = war.Skip(5).Max(r => r.RegionPeakMilli) * 1000 / regionBase;
            double endRoute = war[n - 1].RouteMeanMilli * 1000 / routeBase;
            bool ok = routePeak <= 3000 && regionPeak <= 2000 && (n < 12 || endRoute <= 1300);
            // information: the control's own figures (a harvest failure year raises the staple price with no war: finding EA_F17)
            string ctlText = "";
            if (control != null && control.Count >= n && n > 5) ctlText = $"; the control's own: route peak {control.Skip(5).Take(n - 5).Max(r => r.RoutePeakMilli) * 1000 / routeBase:0}, region peak {control.Skip(5).Take(n - 5).Max(r => r.RegionPeakMilli) * 1000 / regionBase:0}, year-{n} route mean {control[n - 1].RouteMeanMilli * 1000 / routeBase:0}";
            Add("WL-4", ok ? AuditState.Pass : AuditState.Fail, $"staple price on the route peaks at {routePeak:0} permille of the Y3-Y5 mean (pass <= 3,000), the region's at {regionPeak:0} (<= 2,000), the route's year-{n} mean is {endRoute:0} (<= 1,300 by Y12){ctlText}");
        }
        // WL-5 starvation
        if (n >= 6)
        {
            long y5 = war[4].Alive, deaths = war.Skip(5).Take(5).Sum(r => r.HungerDeaths), ctl = control == null ? 0 : control.Take(n).Sum(r => r.HungerDeaths);
            Add("WL-5", deaths * 100.0 / y5 <= 3 && ctl == 0 ? AuditState.Pass : AuditState.Fail, $"hunger deaths Y6-Y10 {deaths} = {deaths * 100.0 / y5:0.00}% of the Y5 population (pass <= 3%); control {ctl}; the besieged town's own share is not split out by the first cut (the siege town held {war.Where(r => r.SiegeDaysOfFood >= 0).Select(r => r.SiegeDaysOfFood).DefaultIfEmpty(-1).Min()} days of food at the end of the siege years)");
        }
        // WL-6 harvest: WAIVED by the user (overview 10e, 21 Nov 2026: war costs no harvest here); the measure is kept as information
        var c8 = Y(control, 8); var w8 = Y(war, 8);
        if (c8 != null && w8 != null && c8.HarvestT > 0)
        {
            double drop = 1 - (double)w8.HarvestT / c8.HarvestT;
            Add("WL-6", AuditState.Info, $"WAIVED (overview 10e: war costs no harvest): Y8 harvest {w8.HarvestT} t against the control's {c8.HarvestT} t = {drop:P1} below");
        }
        else Add("WL-6", AuditState.Info, "WAIVED (overview 10e: war costs no harvest); the measure needs the control run (--with-control)");
        {
            var L = n > 0 ? war[n - 1] : null;
            if (L == null || L.PurveyedTowns == 0 && war.Sum(r => r.Mustered) == 0) Add("WL-7", AuditState.Info, "no campaign with purveyance in the run (a control)");
            else if (L.PurveyedTowns == 0) Add("WL-7", AuditState.Fail, "the purveyance campaigns purveyed no settlement");
            else
            {
                bool ok = L.PurveyGrieved == L.PurveyedTowns && L.PurveyRung2 >= 1 && L.RevoltsWon == 0 && L.RulersChanged == 0;
                Add("WL-7", ok ? AuditState.Pass : AuditState.Fail, $"{L.PurveyedTowns} settlements purveyed ({war.Sum(r => r.PurveyedKg):N0} kg, tallies {war.Sum(r => r.TalliedMites):N0} mites, paid {war.Sum(r => r.TalliesPaidMites):N0}, defaulted {war.Sum(r => r.TalliesDefaultedMites):N0}); {L.PurveyGrieved} with a live grievance the next day (pass: all), {L.PurveyRung2} reached rung 2 within 30 days (pass: at least 1), {L.Riots} riots in the run, {L.RevoltsWon} revolts won, {L.RulersChanged} realms with a changed ruler (pass: 0)");
            }
        }
        // WL-8 treasury
        if (n >= 6)
        {
            long unpaid = war.Sum(r => r.ManDaysUnpaid), arrears = war.Sum(r => r.ArrearsMites), deserted = war.Sum(r => r.Deserted), raised = war.Sum(r => r.AidMites + r.TallageMites);
            bool ok = unpaid == 0 || arrears > 0 || deserted > 0;
            Add("WL-8", ok ? AuditState.Pass : AuditState.Fail, $"the realm raised {raised:N0} mites over the campaigns; {unpaid:N0} man-days went unpaid, arrears recorded {arrears:N0} mites, stand-in deserters {deserted:N0}");
        }
        Add("WL-9", AuditState.Pending, "kit fill from armouries and arms imports is P7-AE3's (the army's end and consequence entries are measured in WL-12)");
        // WL-10 recovery G1-G3
        if (n >= 8)
        {
            var last = war[n - 1]; double pop = (double)last.Alive / war[4].Alive, wageBase = Base(r => r.WagesPerDay), wage = wageBase <= 0 ? 0 : last.WagesPerDay / wageBase - 1;
            // EA_F10 (accepted wording fix, EA3b): the war is measured against the control at the same year; the Y3-Y5 line stays as information
            double vsCtl = Y(control, n) is { WagesPerDay: > 0 } cc ? (double)last.WagesPerDay / cc.WagesPerDay - 1 : wage;
            bool ok = pop >= 0.95 && Math.Abs(vsCtl) <= 0.10 && last.Idle < 800;
            string ctl = Y(control, n) is { } cl && control!.Count >= 5 ? $"; the control run's own wages a day are {cl.WagesPerDay / (control.Skip(2).Take(3).Average(r => r.WagesPerDay)) - 1:+0.0%;-0.0%} from its Y3-Y5 mean, so the drift is the economy's, not the war's" : "";
            Add("WL-10", ok ? AuditState.Pass : AuditState.Fail, $"year {n}: population {pop:P1} of Y5 (pass >= 95%), wages a day {vsCtl:+0.0%;-0.0%} from the control at the same year (pass within 10%; {wage:+0.0%;-0.0%} from the Y3-Y5 mean, information){ctl}, idle adults {last.Idle} (< 800)");
        }
        // WL-11 merchants
        if (n >= 3)
        {
            int minAll = war.Skip(1).Min(r => r.SolventPct), minWar = war.Where(r => r.Year is 8 or 9).Select(r => r.SolventPct).DefaultIfEmpty(100).Min();
            Add("WL-11", minAll >= 60 && minWar >= 40 ? AuditState.Pass : AuditState.Fail, $"cash-solvent merchants at least {minAll}% every year (pass >= 60%), {minWar}% in Y8-Y9 (>= 40%)");
        }
        {
            var L = n > 0 ? war[n - 1] : null;
            if (L == null || L.WarEntries == 0 && L.PriceRises == 0) Add("WL-12", AuditState.Info, "no army entries in the run (a control)");
            else
            {
                double cov = L.WarEntries == 0 ? 1.0 : (double)L.WarSpecific / L.WarEntries;
                bool ok = cov >= 0.90 && L.ArmyBoughtEntries >= L.PriceRises;
                Add("WL-12", ok ? AuditState.Pass : AuditState.Fail, $"{L.WarEntries} army and war-finance entries, {cov:P1} with a specific cause (pass >= 90%); {L.PriceRises} staple price rises of 5% or more from the host's buying, {L.ArmyBoughtEntries} army_bought entries naming the host's campaign (pass: one for each)");
            }
        }
        res.Add(wl13 ?? new GateResult("WL-13", AuditState.Info, "determinism and the mid-siege save round trip: `war-load --checks` (seed 1234); the EA-12 tests cover day 15 and day 8"));
        // WL-14 budget: the slowest hour step of the soak's host slot at most 150 ms; the mean game day at most the control's + 10%
        if (war.Count > 0 && war.Any(r => r.DayMsMean > 0))
        {
            long worst = war.Max(r => r.HourMsMax); long mean = (long)war.Average(r => r.DayMsMean);
            long cmean = control != null && control.Count > 0 ? (long)control.Average(r => r.DayMsMean) : 0;
            bool ok = worst <= 150 && (cmean == 0 || mean <= cmean * 1.10);
            Add("WL-14", cmean == 0 ? AuditState.Info : ok ? AuditState.Pass : AuditState.Fail, $"slowest hour-18 step {worst} ms (pass <= 150; the host slot's hour, not the abstract hour alone); mean game day {mean} ms against the control's {cmean} ms (pass <= +10%)");
        }
        else Add("WL-14", AuditState.Pending, "the abstract tick's budget needs the continent tick under load");
        Add("WL-15", AuditState.Info, "the rich-player bot is EA-13 (AuditBotTests)");
        return res;
    }

    public static string Format(IReadOnlyList<GateResult> gates)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var g in gates) sb.AppendLine($"{g.Id,-6} {(g.State switch { AuditState.Pass => "PASS   ", AuditState.Fail => "FAIL   ", AuditState.Info => "INFO   ", _ => "PENDING" })} {g.Text}");
        return sb.ToString();
    }
}
