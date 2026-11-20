using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Construction;

/// <summary>
/// P5-D: the cheap path for sites nobody watches (spec 9: 200 abstract sites in 2 ms an hour). The engine defers the quanta in which a site only adds the same work again and applies them
/// at once; the result must be the very same bytes as the walk quantum by quantum, whatever the cadence of the calls (the street's barrier every 2 game seconds, the hourly system).
/// </summary>
public class ConstructionCheapPathTests(ITestOutputHelper output)
{
    /// <summary>Thirty sites of every kind of trouble: crews of 1 to 4 (skilled and labourers, a day's cap), piles short of clay, rain every few hours, a wall taken down under a floor.</summary>
    private static ConstructionRig World(bool eager)
    {
        var r = ConstructionRig.Make(crowns: 100_000);
        r.W.Eager = eager;
        r.W.Weather = h => (h % 7 == 3 || h % 11 == 5 ? 50 : 0, 12);
        long t = ConstructionRig.Day10;
        for (int i = 0; i < 30; i++)
        {
            int ox = 3000 + (i % 6) * 30, oy = 3000 + (i / 6) * 30;
            var pr = r.W.PlacePlan(r.Hero, PurseKind.Person, -1, i % 5 == 4 ? "smithy_house" : "test_cottage", ox, oy, i % 4, i % 3 == 0, t);
            Assert.True(pr.Ok, pr.Key);
            if (i % 4 == 1)
            {
                var need = r.W.Needed(r.W.Site(pr.Site)!);
                foreach (var (g, q) in need) r.W.PileOf(r.W.Site(pr.Site)!).Create(g, r.Econ.Goods.StringId(g) == "clay" ? q / 3 : q, Ravensguard.Sim.Goods.CreateReason.Import);
                r.W.Refresh(r.W.Site(pr.Site)!);
            }
            else r.StockAll(pr.Site);
            int n = 1 + i % 4;
            for (int k = 0; k < n; k++)
            {
                var p = EntityId.Make(IdKind.Person, 3_000_000UL + (ulong)(i * 8 + k));
                r.Skills[(p.Value, Skill.Carpentry)] = 10 + 13 * k; r.Skills[(p.Value, Skill.Masonry)] = 20 + 7 * k;
                Assert.True(r.W.AddWorker(pr.Site, p, -1, k % 2 == 0 ? CrewClass.Skilled : CrewClass.Labourer, k % 2 == 0 ? 350 : 150, t).Ok);
                if (i % 6 == 2 && k == 0) r.W.Site(pr.Site)!.Crew[^1].DayCapSec = 4 * 3600 + 1234;
            }
        }
        return r;
    }

    private static string Run(bool eager, long stepSec, out ConstructionRig rig)
    {
        var r = World(eager);
        long t0 = ConstructionRig.Day10, end = t0 + 3 * 86400;
        var site = r.W.Book.Sites.Values.First(s => s.Blueprint == "smithy_house").Id;
        bool decon = false;
        long tDecon = t0 + 30 * 3600;
        for (long t = t0 + stepSec; t <= end; t += stepSec)
        {
            if (!decon && t > tDecon) t = tDecon;
            r.W.AdvanceTo(t);
            if (!decon && t >= tDecon)
            {
                // a wall of a smithy taken down: a deconstruct ghost, then the support check (a groan, maybe a fall)
                var wall = r.W.Book.Built.Values.FirstOrDefault(b => b.Site == site && b.Z == 0 && r.W.Costs[b.Piece].Kind == Ravensguard.Content.PieceKind.Wall);
                if (wall != null) r.W.MarkDeconstruct(r.Hero, PurseKind.Person, -1, wall.X, wall.Y, wall.Z, GhostSlot.Structure, t);
                decon = true;
            }
        }
        r.W.AdvanceTo(end);
        rig = r;
        r.AssertMass(); r.AssertCoin();
        return r.W.Book.ComputeHash() + "|" + string.Join(",", r.Trained.Select(x => $"{x.P.Value}:{x.S}:{x.Sec}")) + "|" + string.Join(",", r.Chron.Select(c => $"{c.Type}@{c.Sec}"))
            + "|" + r.Econ.Ledger.Audit().InPurses.Mites;
    }

    [Fact]
    public void TheCheapPath_GivesTheSameBytesAsTheFullWalk_AtEveryCadence()
    {
        string eager = Run(true, 300, out var e);
        string eagerHourly = Run(true, 3600, out _);
        output.WriteLine(eager == eagerHourly ? "eager cadence-free" : "EAGER DIFFERS BY CADENCE");
        string hourly = Run(false, 3600, out var h);
        string barrier = Run(false, 600, out var b);
        string daily = Run(false, 86400, out var d);
        output.WriteLine($"pieces built {e.W.Book.Built.Count}; full quanta eager {e.W.DiagFullQuanta}, hourly {h.W.DiagFullQuanta} (+{h.W.DiagLinearQuanta} applied at once), daily {d.W.DiagFullQuanta} (+{d.W.DiagLinearQuanta})");
        Assert.True(e.W.Book.Built.Count > 500);
        var pa = eager.Split('|'); var pb = hourly.Split('|');
        for (int i = 0; i < pa.Length; i++) if (pa[i] != pb[i]) { var x = pa[i].Split(','); var y = pb[i].Split(','); int k = 0; while (k < x.Length && k < y.Length && x[k] == y[k]) k++; output.WriteLine($"part {i} differs at item {k}: {(k < x.Length ? x[k] : "-")} vs {(k < y.Length ? y[k] : "-")} ({x.Length} vs {y.Length})"); }
        Assert.Equal(eager, hourly);
        Assert.Equal(eager, barrier);
        Assert.Equal(eager, daily);
        Assert.True(h.W.DiagLinearQuanta > h.W.DiagFullQuanta, "most quanta are linear");
    }
}
