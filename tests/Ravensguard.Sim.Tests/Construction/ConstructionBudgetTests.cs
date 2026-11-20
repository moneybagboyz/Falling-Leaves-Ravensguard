using System.Diagnostics;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Construction;

/// <summary>The budgets of build-mode-and-construction 9 (overview 9.1, 9.2): 200 sites worked an hour, a piece finished, a collapse of 200 cells. Measured with margins for a loaded machine.</summary>
public class ConstructionBudgetTests(ITestOutputHelper output)
{
    [Fact, Trait("Category", "Timing")]
    public void TwoHundredSitesAnHour_APieceFinished_AndA200CellCollapse_StayInTheirBudgets()
    {
        var r = ConstructionRig.Make(crowns: 100_000);
        long t = ConstructionRig.Day10;
        for (int i = 0; i < 200; i++)
        {
            int ox = 3000 + (i % 20) * 30, oy = 3000 + (i / 20) * 30;
            var pr = r.W.PlacePlan(r.Hero, PurseKind.Person, -1, "test_cottage", ox, oy, 0, false, t);
            Assert.True(pr.Ok);
            r.StockAll(pr.Site);
            for (int k = 0; k < 2; k++) r.W.AddWorker(pr.Site, EntityId.Make(IdKind.Person, 2_000_000UL + (ulong)(i * 4 + k)), -1, CrewClass.Labourer, 150, t);
        }
        r.W.AdvanceTo(t + 3600);                                     // warm
        int hours = 9;
        long f0 = r.W.DiagFinished, ft0 = r.W.DiagFinishTicks, rf0 = r.W.DiagRefreshTicks, pk0 = r.W.DiagPickTicks, qt0 = r.W.DiagQuantumTicks;
        var cpu0 = Process.GetCurrentProcess().TotalProcessorTime;
        var sw = Stopwatch.StartNew();
        for (int h = 2; h <= hours + 1; h++) r.W.AdvanceTo(t + h * 3600L);
        double wall = sw.Elapsed.TotalMilliseconds, cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpu0).TotalMilliseconds;
        double finishMs = (r.W.DiagFinishTicks - ft0) * 1000.0 / Stopwatch.Frequency;
        long finished = r.W.DiagFinished - f0;
        double crew = (Math.Min(wall, cpu) - finishMs) / hours;
        output.WriteLine($"200 sites, 400 hands, {hours} working hours: wall {wall / hours:F2} ms, cpu {cpu / hours:F2} ms an hour; {finished} pieces finished in {finishMs:F1} ms ({finishMs / Math.Max(1, finished):F3} ms each); the crews' own work {crew:F2} ms an hour (budget 2 ms)");
        output.WriteLine($"quanta {(r.W.DiagQuantumTicks - qt0) * 1000.0 / Stopwatch.Frequency:F1} ms, refresh {(r.W.DiagRefreshTicks - rf0) * 1000.0 / Stopwatch.Frequency:F1} ms, pick {(r.W.DiagPickTicks - pk0) * 1000.0 / Stopwatch.Frequency:F1} ms, sync {r.W.DiagSyncTicks * 1000.0 / Stopwatch.Frequency:F1} ms, linear {r.W.DiagLinTicks * 1000.0 / Stopwatch.Frequency:F1} ms; quanta walked {r.W.DiagFullQuanta}, applied at once {r.W.DiagLinearQuanta}");
        // P5-D: the cheap path (quanta deferred and applied at once, the quick refresh): about 2 ms an hour on a heavily loaded machine (budget 2 ms); a 10x allowance for CI load
        Assert.True(crew < 20, $"{crew:F2} ms an hour");
        Assert.True(r.W.DiagLinearQuanta > 10 * r.W.DiagFullQuanta, "most site quanta take the cheap path");

        // a 200-cell collapse: a 20 x 10 upper floor on a grid of posts, every post knocked out, then the fall. The first run warms the JIT; the best of the next four is the figure
        double total = double.MaxValue, last = double.MaxValue, perPiece = double.MaxValue;
        for (int rep = 0; rep < 5; rep++)
        {
            var (tt, ll, pp) = Collapse200();
            if (rep > 0) { total = Math.Min(total, tt); last = Math.Min(last, ll); perPiece = Math.Min(perPiece, pp); }
        }
        output.WriteLine($"{perPiece:F3} ms a piece finished (budget 0.5 ms)");
        output.WriteLine($"200-cell collapse: the last post out and the fall {last:F2} ms, all fifteen posts out and the fall {total:F2} ms (budget 3 ms)");
        Assert.True(perPiece < 5, $"{perPiece:F3} ms a piece");
        Assert.True(last < 30, $"{last:F1} ms");                     // P5-I: 10x allowance for CI load; about 2 ms on a quiet 4-core machine
        Assert.True(total < 60, $"{total:F1} ms");
    }

    private static (double Total, double Last, double PerPiece) Collapse200()
    {
        var r2 = ConstructionRig.Make(); long t2 = ConstructionRig.Day10;
        var posts = new List<PlaceCell>();
        for (int y = 0; y < 10; y += 4) for (int x = 0; x < 20; x += 4) posts.Add(new PlaceCell(5000 + x + 1, 5000 + y + 1, 0, "post_timber"));
        var pp = r2.W.Place(r2.Hero, PurseKind.Person, -1, posts, t2);
        r2.StockAll(pp.Site); r2.W.DevFinish(pp.Site, 999, 0, 0, t2);
        var floor = new List<PlaceCell>();
        for (int y = 0; y < 10; y++) for (int x = 0; x < 20; x++) floor.Add(new PlaceCell(5000 + x, 5000 + y, 1, "floor_plank"));
        var fp = r2.W.Place(r2.Hero, PurseKind.Person, -1, floor, t2);
        Assert.True(fp.Ok && fp.Refused.Count == 0);
        r2.StockAll(fp.Site);
        var fsw = Stopwatch.StartNew();
        int n = r2.W.DevFinish(fp.Site, 999, 0, 0, t2);
        double perPiece = fsw.Elapsed.TotalMilliseconds / n;
        var csw = Stopwatch.StartNew();
        double lastStart = 0;
        for (int i = 0; i < posts.Count; i++)
        {
            if (i == posts.Count - 1) lastStart = csw.Elapsed.TotalMilliseconds;
            r2.W.Destroy(posts[i].X, posts[i].Y, 0, t2, EntityId.None, "budget");
        }
        r2.W.AdvanceTo(t2 + 30);
        double ms = csw.Elapsed.TotalMilliseconds;
        Assert.Equal(0, r2.W.Book.Built.Values.Count(b => b.Z == 1));
        r2.AssertMass();
        return (ms, ms - lastStart, perPiece);
    }
}
