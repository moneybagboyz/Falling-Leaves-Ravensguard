using System.Diagnostics;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Economy;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Construction;

public class ConstructionCheapPathDebug(ITestOutputHelper output)
{
    [Fact]
    public void Debug_RefreshCost()
    {
        var r = ConstructionRig.Make(crowns: 100_000);
        long t = ConstructionRig.Day10;
        var pr = r.W.PlacePlan(r.Hero, PurseKind.Person, -1, "test_cottage", 3000, 3000, 0, false, t);
        r.StockAll(pr.Site);
        r.W.DevFinish(pr.Site, 10, 0, 0, t);
        var s = r.W.Site(pr.Site)!;
        var built = new List<long> { r.W.Book.Built.Keys.Count > 0 ? ConstructionIds.CellKey(r.W.Book.Built.Values.First().X, r.W.Book.Built.Values.First().Y, 0) : 0 };
        for (int i = 0; i < 1000; i++) r.W.Refresh(s, built);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 20000; i++) r.W.Refresh(s, built);
        output.WriteLine($"refresh after a finish: {sw.Elapsed.TotalMilliseconds * 1000 / 20000:F2} us; ghosts {r.W.GhostsOf(s.Id).Count()}");
        sw.Restart();
        for (int i = 0; i < 20000; i++) r.W.Refresh(s);
        output.WriteLine($"full refresh: {sw.Elapsed.TotalMilliseconds * 1000 / 20000:F2} us");
    }
}
