using System.Diagnostics;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Diagnostics;

namespace Ravensguard.WorldTool;

/// <summary>
/// <c>econ-audit [--seed N] [--strict]</c>: builds the seed's world, prints the economy audit's checklist (economy-audit section 4) with a result on every row (the rows the harness measures itself, and for the rest the pass that closes
/// them); exit code 1 with <c>--strict</c> when a row failed (the M-E0 gate: no failed row). <c>war-load [--seed N] [--years Y] [--control] [--quiet] [--no-links] [--with-control] [--l3 none|gate|caravan] [--l3-fuel] [--checks] [--kits] [--no-terms]</c>: the war-load soak (economy-audit 5): a new game of the seed on the real runtime with the
/// game's own links attached (finding G1), the scripted host of <see cref="WarLoadScenario"/> marching, eating, being paid and besieging, one yearly row of the war table; exit code 1 when the coin or mass audit fails on any day (WL-1, WL-2).
/// P7-EA3b: <c>--l3</c> founds the scripted L3 iron works near the route in Y2 (the control carries it too), <c>--checks</c> runs WL-13 (two more ten-year runs: the hash at Y10 day 60 and the save round trips).
/// </summary>
public static class EconAuditCommand
{
    public static int RunAudit(string[] args)
    {
        ulong seed = 1234; bool strict = false; int days = 120;
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seed": seed = ulong.Parse(args[++i]); break;
                case "--strict": strict = true; break;
                case "--days": days = int.Parse(args[++i]); break;          // P7-EA3b: the world is run this many game days first (two game years) so the rows that read the ledger see the mints and the border
                default: Console.Error.WriteLine($"unknown option {args[i]}"); return 1;
            }
        }
        using var run = WarLoadRun.Create(seed, BaseContent.Require());
        int day0 = run.NextSlotDay; for (int d = day0; d < day0 + days; d++) run.AdvanceToDayEnd(d);
        Console.WriteLine($"world of seed {seed} run for {days} game days; coin conserved {run.CoinConserved}, mass exact {run.MassOk}");
        var rows = EconAudit.Run(run.Economy, run.State.Content!);
        Console.Write(EconAudit.Format(rows));
        return strict && rows.Any(r => r.State == AuditState.Fail) ? 1 : 0;
    }

    public static int RunWarLoad(string[] args)
    {
        ulong seed = 1234; int years = 25; bool control = false, quiet = false, links = true, withControl = false, checks = false, l3Fuel = false, kits = false, noTerms = false; string l3 = "none";
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seed": seed = ulong.Parse(args[++i]); break;
                case "--years": years = int.Parse(args[++i]); break;
                case "--control": control = true; break;
                case "--quiet": quiet = true; break;
                case "--no-links": links = false; break;
                case "--with-control": withControl = true; break;
                case "--l3": l3 = args[++i]; break;
                case "--l3-fuel": l3Fuel = true; break;
                case "--checks": checks = true; break;
                case "--kits": kits = true; break;
                case "--no-terms": noTerms = true; break;
                default: Console.Error.WriteLine($"unknown option {args[i]}"); return 1;
            }
        }
        var sw = Stopwatch.StartNew();
        int soakYears = years;
        (WarLoadRun Run, WarLoadSoak Soak) Make(bool ctl, int yrs)
        {
            var r = WarLoadRun.Create(seed, BaseContent.Require(), attachLinks: links);
            var s = new WarLoadSoak(r, yrs, ctl);
            if (noTerms) r.State.HomeRecords!.Sim.Get<Ravensguard.Sim.Production.Industry.IndustryWorld>()!.InputTermsDays = 0;
            if (l3 != "none") s.L3 = new L3IronWorks(r, l3 == "gate" ? L3Sale.Gate : L3Sale.Caravan, l3Fuel);
            return (r, s);
        }
        void Tables(WarLoadSoak s)
        {
            Console.WriteLine("econ table (P7-EA3b):"); Console.WriteLine(WarLoadSoak.HeaderEx());
            foreach (var r in s.Rows) Console.WriteLine(WarLoadSoak.FormatEx(r));
            Console.WriteLine("border split (P7-EA3b):"); Console.WriteLine(WarLoadSoak.HeaderBorder()); foreach (var r in s.Rows) Console.WriteLine(WarLoadSoak.FormatBorder(r));
            foreach (var r in s.Rows) { if (r.Chains.Length > 0) Console.WriteLine($"  Y{r.Year} chains: {r.Chains}"); if (r.L3.Length > 0) Console.WriteLine($"  Y{r.Year} L3: {r.L3}"); if (r.Prices.Length > 0) Console.WriteLine($"  Y{r.Year} prices: {r.Prices}"); }
            if (s.L3 != null) Console.WriteLine($"L3 works: {s.L3.Note}; bill {s.L3.BillMites:N0} (L1 at book prices {s.L3.L1BillMites:N0}); seed {s.L3.SeedMites:N0}; hall hires {s.L3.HallHires}; fuel faucet {s.L3.SeedGoodsG / 1000:N0} kg");
        }
        List<WarYearRow>? ctlRows = null;
        if (withControl && !control)
        {
            var (c, cs) = Make(true, years); using var _c = c;
            Console.WriteLine("control rows:"); Console.WriteLine(WarLoadSoak.Header());
            cs.Run(r => { if (!quiet) Console.WriteLine(WarLoadSoak.Format(r)); }); ctlRows = cs.Rows;
            Console.WriteLine($"control run (no load) done in {sw.Elapsed.TotalSeconds:0.0} s"); Tables(cs);
        }
        var (run, soak) = Make(control, years); using var _run = run;
        Console.WriteLine($"seed {seed}: {run.State.Population.People.Count:N0} people, {run.State.Population.Households.Count:N0} households, built in {sw.ElapsedMilliseconds} ms; links attached: {run.LinksAttached}{(control ? "; CONTROL (no load)" : "")}");
        Console.WriteLine(WarLoadSoak.Header());
        var kitProbe = kits && !control ? new KitProbe() : null; if (kitProbe != null) soak.AtDayStart = kitProbe.AtDay;
        soak.Run(r => { if (!quiet) Console.WriteLine(WarLoadSoak.Format(r)); });
        Console.WriteLine($"{years} years in {sw.Elapsed.TotalSeconds:0.0} s; gates WL-1 (coin) and WL-2 (mass) every day: {(soak.Ok ? "pass" : "FAIL " + soak.FirstFailure)}");
        Tables(soak);
        GateResult? wl13 = null;
        if (checks && !control) { Console.WriteLine("WL-13 checks (two more runs of ten years)..."); wl13 = WarLoadChecks.Run(() => Make(false, 10), Console.WriteLine); }
        if (!control) { Console.WriteLine("war-load gates (economy-audit 5.3):"); Console.Write(WarGates.Format(WarGates.Evaluate(soak.Rows, ctlRows, wl13, kitProbe?.Result()))); }
        return soak.Ok ? 0 : 1;
    }
}
