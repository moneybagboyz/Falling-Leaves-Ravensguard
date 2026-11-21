using System.Diagnostics;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Diagnostics;

namespace Ravensguard.WorldTool;

/// <summary>
/// <c>econ-audit [--seed N] [--strict]</c>: builds the seed's world, prints the economy audit's checklist (economy-audit section 4) with a result on every row (the rows the harness measures itself, and for the rest the pass that closes
/// them); exit code 1 with <c>--strict</c> when a row failed (the M-E0 gate: no failed row). <c>war-load [--seed N] [--years Y] [--control] [--quiet] [--no-links]</c>: the war-load soak (economy-audit 5): a new game of the seed on the real runtime with the
/// game's own links attached (finding G1), the scripted host of <see cref="WarLoadScenario"/> marching, eating, being paid and besieging, one yearly row of the war table; exit code 1 when the coin or mass audit fails on any day (WL-1, WL-2).
/// </summary>
public static class EconAuditCommand
{
    public static int RunAudit(string[] args)
    {
        ulong seed = 1234; bool strict = false;
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seed": seed = ulong.Parse(args[++i]); break;
                case "--strict": strict = true; break;
                default: Console.Error.WriteLine($"unknown option {args[i]}"); return 1;
            }
        }
        var hw = HeadlessWorld.Create(seed, BaseContent.Require());
        var rows = EconAudit.Run(hw.Economy, hw.Content);
        Console.Write(EconAudit.Format(rows));
        return strict && rows.Any(r => r.State == AuditState.Fail) ? 1 : 0;
    }

    public static int RunWarLoad(string[] args)
    {
        ulong seed = 1234; int years = 25; bool control = false, quiet = false, links = true, withControl = false;
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
                default: Console.Error.WriteLine($"unknown option {args[i]}"); return 1;
            }
        }
        var sw = Stopwatch.StartNew();
        List<WarYearRow>? ctlRows = null;
        if (withControl && !control)
        {
            using var c = WarLoadRun.Create(seed, BaseContent.Require(), attachLinks: links);
            var cs = new WarLoadSoak(c, years, control: true); Console.WriteLine("control rows:"); Console.WriteLine(WarLoadSoak.Header()); cs.Run(r => { if (!quiet) Console.WriteLine(WarLoadSoak.Format(r)); }); ctlRows = cs.Rows;
            Console.WriteLine($"control run (no load) done in {sw.Elapsed.TotalSeconds:0.0} s");
        }
        using var run = WarLoadRun.Create(seed, BaseContent.Require(), attachLinks: links);
        Console.WriteLine($"seed {seed}: {run.State.Population.People.Count:N0} people, {run.State.Population.Households.Count:N0} households, built in {sw.ElapsedMilliseconds} ms; links attached: {run.LinksAttached}{(control ? "; CONTROL (no load)" : "")}");
        Console.WriteLine(WarLoadSoak.Header());
        var soak = new WarLoadSoak(run, years, control);
        soak.Run(r => { if (!quiet) Console.WriteLine(WarLoadSoak.Format(r)); });
        Console.WriteLine($"{years} years in {sw.Elapsed.TotalSeconds:0.0} s; gates WL-1 (coin) and WL-2 (mass) every day: {(soak.Ok ? "pass" : "FAIL " + soak.FirstFailure)}");
        if (!control) { Console.WriteLine("war-load gates (economy-audit 5.3):"); Console.Write(WarGates.Format(WarGates.Evaluate(soak.Rows, ctlRows))); }
        return soak.Ok ? 0 : 1;
    }
}
