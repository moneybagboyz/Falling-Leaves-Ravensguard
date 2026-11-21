using System.Diagnostics;
using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Combat.Battle;

/// <summary>
/// Phase 7 BB-A (battle-bubble-and-formations 11): the battle grid. BB-1 and BB-2 are the equivalence gates: the same fight with <see cref="FightWorld.UseGrid"/> true and false (false is the
/// old full scan, every neighbour query returns every id) gives the same hash. BB-4 is ML-16 at scale. BB-5 is the sim side of the budget.
/// </summary>
[Collection("fight")]
public class BattleGridTests(FightFixture fx, ITestOutputHelper output)
{
    private static (ulong, ulong) Fingerprint(FightWorld w) => (w.Hash(), w.MoraleHash());

    // ---------------------------------------------------------------- BB-1 grid equivalence

    [Fact]
    public void BB1_GridEquivalence_200SkirmishSeeds_SameHashWithAndWithoutTheGrid()
    {
        var bad = new List<ulong>();
        for (ulong s = 0; s < 200; s++)
        {
            var h = new (ulong, ulong)[2];
            for (int mode = 0; mode < 2; mode++)
            {
                var w = FightCalibration.Skirmish(fx.C, 9000 + s, 5);
                w.UseGrid = mode == 0;
                for (int i = 0; i < 1200 && !w.IsOver; i++) w.StepWithAi();
                h[mode] = Fingerprint(w);
            }
            if (h[0] != h[1]) bad.Add(9000 + s);
        }
        Assert.True(bad.Count == 0, $"seeds with a different hash: {string.Join(", ", bad)}");
    }

    [Fact]
    public void BB1_GridEquivalence_LargeCrowds_EveryHundredTicks()
    {
        foreach (ulong seed in new ulong[] { 1, 2, 3 })
        {
            var a = FightCalibration.Skirmish(fx.C, seed, 60); var b = FightCalibration.Skirmish(fx.C, seed, 60);
            b.UseGrid = false;
            for (int t = 1; t <= 500; t++)
            {
                a.StepWithAi(); b.StepWithAi();
                if (t % 50 == 0) Assert.True(Fingerprint(a) == Fingerprint(b), $"seed {seed}: the grid and the scan diverged by tick {t}");
            }
        }
    }

    [Fact]
    public void BB1_GridEquivalence_ShieldWallsAndDownedBodies()
    {
        // a dense block that bumps, shoves, braces and kills: Separate, the wall helpers, morale and the nearest-enemy searches all see the same crowd
        foreach (ulong seed in new ulong[] { 5, 6 })
        {
            var a = Block(seed, 12, 6, 2400); var b = Block(seed, 12, 6, 2400);
            b.UseGrid = false;
            for (int t = 1; t <= 400; t++)
            {
                a.StepWithAi(); b.StepWithAi();
                if (t % 40 == 0) Assert.True(Fingerprint(a) == Fingerprint(b), $"seed {seed}: diverged by tick {t}");
            }
            Assert.True(a.Fighters.Any(f => f.Act is FAct.Down or FAct.Dead), "the fight should have hurt someone");
        }
    }

    // ---------------------------------------------------------------- BB-2 order of candidates

    [Fact]
    public void BB2_Candidates_ComeBackInAscendingId_AcrossCellBoundariesAndNegativeCoordinates()
    {
        var w = fx.World(1);
        // ids 0..8 placed so a 1 m query around the origin crosses the 4 m cell edges on both axes, with ids deliberately out of spatial order
        (int X, int Y)[] at = [(3999, 0), (-1, -1), (4001, 4001), (0, 0), (-4001, 3), (3, -3999), (4000, 0), (2, 2), (-3999, -4001)];
        foreach (var (x, y) in at) w.Add(fx.Kit("f", 0, x, y), false);
        Assert.True(w.Fighters.Count == 9);
        var ids = w.Near(0, 0, 0, 5000).ToArray();
        Assert.Equal(ids.OrderBy(i => i).ToArray(), ids);
        Assert.Equal(Enumerable.Range(0, 9).Where(i => FightMath.Len(at[i].X, at[i].Y) <= 5000).ToArray(), ids.Where(i => FightMath.Len(at[i].X, at[i].Y) <= 5000).ToArray());
        // every fighter within 5 m is among them
        foreach (int i in Enumerable.Range(0, 9).Where(i => FightMath.Len(at[i].X, at[i].Y) <= 5000)) Assert.Contains(i, ids);
        // the same ordering with the grid off
        w.UseGrid = false;
        Assert.Equal(Enumerable.Range(0, 9).ToArray(), w.Near(0, 0, 0, 5000).ToArray());
    }

    [Fact]
    public void BB2_Candidates_AreNeverMissed_AfterFightersMoveBetweenRebuilds()
    {
        var w = fx.World(2);
        var rng = new Random(7);
        for (int i = 0; i < 300; i++) w.Add(fx.Kit("f", i & 1, rng.Next(-60000, 60000), rng.Next(-60000, 60000)), false);
        w.Add(fx.Kit("far", 0, 900_000_000, -900_000_000), false);                  // an outlier clamps into a border cell
        for (int round = 0; round < 40; round++)
        {
            for (int k = 0; k < 20; k++)                                              // writes between rebuilds: the drift widens the query box
            {
                var f = w.Fighters[rng.Next(w.Fighters.Count)];
                f.X += rng.Next(-1500, 1500); f.Y += rng.Next(-1500, 1500);
            }
            for (int q = 0; q < 25; q++)
            {
                var c = w.Fighters[rng.Next(w.Fighters.Count)];
                long r = rng.Next(500, 40000);
                var got = w.Near(0, c, r).ToArray();
                Assert.Equal(got.OrderBy(i => i).ToArray(), got);
                var want = w.Fighters.Where(o => w.DistMm(c, o) <= r).Select(o => o.Id).ToArray();
                foreach (int id in want) Assert.Contains(id, got);
            }
        }
    }

    [Fact]
    public void BB2_NearestEnemy_MatchesTheFullScan_IncludingTies()
    {
        var w = fx.World(3);
        var rng = new Random(11);
        for (int i = 0; i < 200; i++) w.Add(fx.Kit("f", rng.Next(3), rng.Next(-30000, 30000) / 1000 * 1000, rng.Next(-30000, 30000) / 1000 * 1000), false);   // a 1 m lattice: many equal distances
        foreach (var f in w.Fighters.Take(80))
        {
            w.UseGrid = true; var g = w.NearestEnemyOf(f);
            w.UseGrid = false; var s = w.NearestEnemyOf(f);
            Assert.Equal(s?.Id, g?.Id);
            Assert.Equal(w.NearestEnemy(f)?.Id, g?.Id);
        }
    }

    // ---------------------------------------------------------------- BB-4 near and far at scale

    private (double Down, double Dead, Dictionary<string, double> Wounds) Tally(int seeds, int perSide, int ticks, bool near)
    {
        int downs = 0, deaths = 0, n = 0;
        var wounds = new Dictionary<string, long>();
        var mats = fx.C.T.MaterialList;
        for (ulong s = 0; s < (ulong)seeds; s++)
        {
            var w = FightCalibration.Skirmish(fx.C, 31000 + s, perSide, near ? true : null);
            if (!near) w.LodCentre = (-7000, 0);                                      // the hero stands at one end of the line: 60 m around him is thought at 20 Hz, the rest at 5 Hz
            w.RecordEvents = false;
            w.OnBlow = (_, t, r, _) => { foreach (var wd in r.WoundSpan) { var k = Calibration.Category(t.Plan, mats, wd); wounds[k] = wounds.GetValueOrDefault(k) + 1; } };
            for (int i = 0; i < ticks && !w.IsOver; i++) w.StepWithAi();
            foreach (var f in w.Fighters) { n++; if (f.Act == FAct.Down) downs++; if (f.Act == FAct.Dead) deaths++; }
        }
        long tot = Math.Max(1, wounds.Values.Sum());
        return (100.0 * downs / n, 100.0 * deaths / n, wounds.ToDictionary(kv => kv.Key, kv => 100.0 * kv.Value / tot));
    }

    private void AssertNearFarWithin5(int seeds, int perSide, int ticks)
    {
        var near = Tally(seeds, perSide, ticks, true); var far = Tally(seeds, perSide, ticks, false);
        output.WriteLine($"near: down {near.Down:0.0}% dead {near.Dead:0.0}%   far: down {far.Down:0.0}% dead {far.Dead:0.0}%");
        Assert.InRange(near.Down - far.Down, -5, 5);
        Assert.InRange(near.Dead - far.Dead, -5, 5);
        foreach (var k in near.Wounds.Keys.Union(far.Wounds.Keys)) Assert.InRange(near.Wounds.GetValueOrDefault(k) - far.Wounds.GetValueOrDefault(k), -5, 5);
    }

    [Fact]
    public void BB4_NearAndFar_AtScale_Sample() => AssertNearFarWithin5(4, 150, 900);

    [Fact, Trait("Category", "Soak")]
    public void BB4_NearAndFar_100Seeds_400v400() => AssertNearFarWithin5(100, 400, 1200);

    private FightWorld Block(ulong seed, int perSide, int depth, int gapMm) => BlockOf(fx, seed, perSide, depth, gapMm);

    internal static FightWorld BlockOf(FightFixture fx, ulong seed, int perSide, int depth, int gapMm)
    {
        var w = new FightWorld(fx.C, seed, seed, 8 * 3600) { RecordEvents = false };
        int cols = (perSide + depth - 1) / depth;
        for (int team = 0; team < 2; team++)
            for (int i = 0; i < perSide; i++)
            {
                int rank = i / cols, col = i % cols;                                  // rank 0 is the front
                int x = (team == 0 ? -1 : 1) * (gapMm / 2 + rank * 900), y = (col * 2 - (cols - 1)) * 450;
                int skill = 30 + (int)(DetRng.NextUInt(seed, 0, (ulong)(team * 100000 + i), 7, 0) % 35);
                int face = team == 0 ? FightMath.One : -FightMath.One;
                var kit = i % 3 == 0 ? FightCalibration.KitOf("s", team, FightCalibration.Spear, FightCalibration.Padded, null, skill, x, face)
                    : FightCalibration.KitOf("s", team, FightCalibration.Sword, i % 3 == 1 ? FightCalibration.Mailed : FightCalibration.Padded, team == 0 ? "heater" : "round_shield", skill, x, face);
                w.Add(kit with { Y = y });
            }
        return w;
    }

}

/// <summary>BB-A's budget tests (BB-5, sim side), by themselves in the timing collection.</summary>
[Collection(TimingCollection.Name)]
public class BattleGridBudgetTests(FightFixture fx, ITestOutputHelper output) : IClassFixture<FightFixture>
{
    private FightWorld Block(ulong seed, int perSide, int depth, int gapMm) => BattleGridTests.BlockOf(fx, seed, perSide, depth, gapMm);

    [Fact]
    public void BB5_Grid_AllocatesNothing_ARebuildAndAQuery()
    {
        var w = Block(1, 600, 6, 2400);
        for (int i = 0; i < 5; i++) w.StepWithAi();
        w.Grid.Rebuild(w.Fighters);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10; i++) { w.Grid.Rebuild(w.Fighters); foreach (int id in w.Grid.Near(0, w.Fighters[i].X, w.Fighters[i].Y, 12000)) { _ = id; } }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private (double Avg, double WorstMinute, double ScanAvg) Budget(int perSide, int ticks, bool grid)
    {
        var w = Block(7, perSide, perSide >= 1000 ? 7 : 6, 2400);
        w.UseGrid = grid;
        for (int i = 0; i < 40; i++) w.StepWithAi();                                  // warm up: JIT, the buffers' growth
        double sum = 0, worst = 0, minuteWorst = 0; int minuteTicks = 0;
        var sw = new Stopwatch();
        for (int i = 0; i < ticks; i++)
        {
            sw.Restart(); w.StepWithAi(); double ms = sw.Elapsed.TotalMilliseconds;
            sum += ms; minuteWorst = Math.Max(minuteWorst, ms);
            if (++minuteTicks == 1200) { worst = Math.Max(worst, minuteWorst); minuteWorst = 0; minuteTicks = 0; }
        }
        worst = Math.Max(worst, minuteWorst);
        return (sum / ticks, worst, 0);
    }

    [Fact]
    public void BB5_Budget_2300Fighters_Sample()
    {
        double avg = TestBudget.BestPass(25, 3, () => Budget(1150, 300, true).Avg);
        output.WriteLine($"2,300 fighters, 300 ticks: average {avg:0.00} ms a tick (gate 25)");
        Assert.True(avg <= TestBudget.Ms(25), $"average {avg:0.00} ms");
    }

    [Fact, Trait("Category", "Soak")]
    public void BB5_Budget_2300Fighters_6000Ticks()
    {
        var (avg, worst, _) = Budget(1150, 6000, true);
        output.WriteLine($"2,300 fighters, 6,000 ticks: average {avg:0.00} ms, worst tick of a minute {worst:0.0} ms (gates 25 and 33)");
        Assert.True(avg <= TestBudget.Ms(25), $"average {avg:0.00} ms");
        Assert.True(worst <= TestBudget.Ms(33), $"worst {worst:0.0} ms");
    }

    [Fact, Trait("Category", "Soak")]
    public void BB5_Report_GridAgainstFullScan()
    {
        foreach (int perSide in new[] { 100, 300, 600, 1150 })
        {
            var g = Budget(perSide, 200, true); var s = Budget(perSide, 200, false);
            output.WriteLine($"{perSide * 2} fighters: grid {g.Avg:0.00} ms (worst {g.WorstMinute:0.0}), full scan {s.Avg:0.00} ms (worst {s.WorstMinute:0.0})");
        }
    }
}
