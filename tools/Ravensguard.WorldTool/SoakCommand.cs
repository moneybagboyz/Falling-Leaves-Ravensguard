using System.Diagnostics;
using Ravensguard.Sim.Abstract;

namespace Ravensguard.WorldTool;

/// <summary>
/// <c>soak --seed N --years Y [--carry PCT] [--quiet]</c>: builds the seed's world with the whole Phase 2 economy, runs it Y game years on the abstract tick and prints the
/// yearly table (population, under-15 share, births, deaths, food surplus, hungry household-days, staple price index and book-days in band, workshop completion,
/// merchants solvent, coin and its sources and sinks, the mass and coin audits) and, at the end, the tick timings. Exit code 1 when an audit fails.
/// </summary>
public static class SoakCommand
{
    public static int Run(string[] args)
    {
        ulong seed = 1234; int years = 1, carry = 45; bool quiet = false, diag = false; string? probe = null; int probeDays = 15; int every = 1;
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seed": seed = ulong.Parse(args[++i]); break;
                case "--years": years = int.Parse(args[++i]); break;
                case "--carry": carry = int.Parse(args[++i]); break;
                case "--every": every = Math.Max(1, int.Parse(args[++i])); break;
                case "--quiet": quiet = true; break;
                case "--diag": diag = true; break;
                case "--probe": probe = args[++i]; break;
                case "--probe-days": probeDays = int.Parse(args[++i]); break;
                default: Console.Error.WriteLine($"unknown option {args[i]}"); return 1;
            }
        }
        var dir = BaseContent.Require();
        var sw = Stopwatch.StartNew();
        var hw = HeadlessWorld.Create(seed, dir, carryOverPct: carry);
        var w = hw.World;
        Console.WriteLine($"seed {seed}: {w.People.Count:N0} people, {w.Households.Count:N0} households, {hw.Economy.Markets.AllBooks().Count()} market books, {hw.Economy.Production.Businesses.Count} businesses, built in {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"band: staple mid / base price within {EconomySoak.BandLow / 1000.0:0.0}..{EconomySoak.BandHigh / 1000.0:0.0}, book-days of books with a merchant; staples {string.Join(", ", EconomySoak.Staples.Take(5))} (price index columns are the mean mid/base)");
        var soak = new EconomySoak(hw);
        if (probe != null) { soak.Probe(probe, probeDays, Console.Out); return 0; }
        Console.WriteLine(EconomySoak.Header());
        bool ok = true;
        soak.Run(years, r =>
        {
            ok &= r.CoinConserved && r.MassOk && r.MassViolations == 0;
            if (!quiet && (r.Year % every == 0 || r.Year == years || r.Year == 1)) Console.WriteLine(EconomySoak.Format(r));
        });
        Console.WriteLine($"{years} years in {soak.Clock.Elapsed.TotalSeconds:0.0} s");
        Console.WriteLine(EconomySoak.Summary(soak));
        if (diag) Console.WriteLine(soak.Diagnostics());
        Console.WriteLine(soak.Ticker.Stats.Report());
        return ok ? 0 : 1;
    }
}
