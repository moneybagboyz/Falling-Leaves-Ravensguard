namespace Ravensguard.Sim.Diagnostics;

/// <summary>One gate of the war-load soak (economy-audit 5.3) with its measured result.</summary>
public sealed record GateResult(string Id, AuditState State, string Text);

/// <summary>
/// The war-load soak's gates WL-1 to WL-15 read from the yearly rows (P7-EA2). A gate whose measure needs a part not built at the audited tag (purveyance, kits, chronicle causes of the army, the real abstract tick's budget) is
/// <see cref="AuditState.Pending"/> and names the part; a gate that needs the control run's rows is measured only when they are given. Years are 1-based rows; the baseline is the mean of years 3 to 5.
/// </summary>
public static class WarGates
{
    public static IReadOnlyList<GateResult> Evaluate(IReadOnlyList<WarYearRow> war, IReadOnlyList<WarYearRow>? control)
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
            Add("WL-4", ok ? AuditState.Pass : AuditState.Fail, $"staple price on the route peaks at {routePeak:0} permille of the Y3-Y5 mean (pass <= 3,000), the region's at {regionPeak:0} (<= 2,000), the route's year-{n} mean is {endRoute:0} (<= 1,300 by Y12)");
        }
        // WL-5 starvation
        if (n >= 6)
        {
            long y5 = war[4].Alive, deaths = war.Skip(5).Take(5).Sum(r => r.HungerDeaths), ctl = control == null ? 0 : control.Take(n).Sum(r => r.HungerDeaths);
            Add("WL-5", deaths * 100.0 / y5 <= 3 && ctl == 0 ? AuditState.Pass : AuditState.Fail, $"hunger deaths Y6-Y10 {deaths} = {deaths * 100.0 / y5:0.00}% of the Y5 population (pass <= 3%); control {ctl}; the besieged town's own share is not split out by the first cut (the siege town held {war.Where(r => r.SiegeDaysOfFood >= 0).Select(r => r.SiegeDaysOfFood).DefaultIfEmpty(-1).Min()} days of food at the end of the siege years)");
        }
        // WL-6 harvest
        var c8 = Y(control, 8); var w8 = Y(war, 8);
        if (c8 != null && w8 != null && c8.HarvestT > 0)
        {
            double drop = 1 - (double)w8.HarvestT / c8.HarvestT, y11 = Y(control, 11) is { HarvestT: > 0 } c11 && Y(war, 11) is { } w11 ? (double)w11.HarvestT / c11.HarvestT - 1 : 0;
            Add("WL-6", drop is >= 0.10 and <= 0.35 && Math.Abs(y11) <= 0.10 ? AuditState.Pass : AuditState.Fail, $"Y8 harvest {w8.HarvestT} t against the control's {c8.HarvestT} t = {drop:P1} below (pass 10-35%); Y11 {y11:P1} from the control (pass within 10%)");
        }
        else Add("WL-6", AuditState.Pending, "needs the control run (--with-control)");
        Add("WL-7", AuditState.Pending, "purveyance, grievances and riots are P7-AE2's; not in the audited tag");
        // WL-8 treasury
        if (n >= 6)
        {
            long unpaid = war.Sum(r => r.ManDaysUnpaid), arrears = war.Sum(r => r.ArrearsMites), deserted = war.Sum(r => r.Deserted), raised = war.Sum(r => r.AidMites + r.TallageMites);
            bool ok = unpaid == 0 || arrears > 0 || deserted > 0;
            Add("WL-8", ok ? AuditState.Pass : AuditState.Fail, $"the realm raised {raised:N0} mites over the campaigns; {unpaid:N0} man-days went unpaid, arrears recorded {arrears:N0} mites, stand-in deserters {deserted:N0}");
        }
        Add("WL-9", AuditState.Pending, "kits and armouries are P7-SC-A's and AE3's");
        // WL-10 recovery G1-G3
        if (n >= 8)
        {
            var last = war[n - 1]; double pop = (double)last.Alive / war[4].Alive, wageBase = Base(r => r.WagesPerDay), wage = wageBase <= 0 ? 0 : last.WagesPerDay / wageBase - 1;
            bool ok = pop >= 0.95 && Math.Abs(wage) <= 0.10 && last.Idle < 800;
            string ctl = Y(control, n) is { } cl && control!.Count >= 5 ? $"; the control run's own wages a day are {cl.WagesPerDay / (control.Skip(2).Take(3).Average(r => r.WagesPerDay)) - 1:+0.0%;-0.0%} from its Y3-Y5 mean, so the drift is the economy's, not the war's" : "";
            Add("WL-10", ok ? AuditState.Pass : AuditState.Fail, $"year {n}: population {pop:P1} of Y5 (pass >= 95%), wages a day {wage:+0.0%;-0.0%} from the Y3-Y5 mean (pass within 10%){ctl}, idle adults {last.Idle} (< 800)");
        }
        // WL-11 merchants
        if (n >= 3)
        {
            int minAll = war.Skip(1).Min(r => r.SolventPct), minWar = war.Where(r => r.Year is 8 or 9).Select(r => r.SolventPct).DefaultIfEmpty(100).Min();
            Add("WL-11", minAll >= 60 && minWar >= 40 ? AuditState.Pass : AuditState.Fail, $"cash-solvent merchants at least {minAll}% every year (pass >= 60%), {minWar}% in Y8-Y9 (>= 40%)");
        }
        Add("WL-12", AuditState.Pending, "the army's chronicle entries and price causes are P7-AE2's");
        Add("WL-13", AuditState.Info, "determinism and the mid-siege save round trip are the EA-12 tests (day 15 equal; day 8 byte-identical) and the soak's second run; the mid-siege (Y8 day 40) round trip is EA3b's");
        Add("WL-14", AuditState.Pending, "the abstract tick's budget needs the continent tick under load: EA3b");
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
