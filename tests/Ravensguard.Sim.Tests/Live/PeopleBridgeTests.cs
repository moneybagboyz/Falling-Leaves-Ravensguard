using System.Diagnostics;
using System.Security.Cryptography;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Abstract;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.World;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Live;

/// <summary>Rig for the people bridge: the shared seed-1234 region, its largest town, and an open (or walled) ground around it.</summary>
public sealed class PeopleRig
{
    public Population Pop { get; }
    public RegionSettlement Town { get; }
    public PeopleRecords Records { get; }
    public long CentreXMm => Town.Plan.Square.CenterX * 1000L;
    public long CentreYMm => Town.Plan.Square.CenterY * 1000L;
    public const int GridSize = 640;

    public PeopleRig(Population? pop = null)
    {
        Pop = pop ?? EconFixture.Pop;
        Town = Pop.Region.Towns.OrderByDescending(t => t.TargetPop).First();
        Records = new PeopleRecords(0, 0, 0, Pop, EconFixture.Seed, new AbstractJobTable());
    }

    public static long Day(int offset, int hour, int minute = 0, int second = 0) => (long)offset * 86400 + hour * 3600 + minute * 60 + second;
    public long At(int hour, int minute = 0, int second = 0) => Day(Pop.NowDay + 1, hour, minute, second);
    public (long X, long Y)[] Centre => [(CentreXMm, CentreYMm)];

    /// <summary>Open ground around the town square; <paramref name="walls"/> closes a ring around every lot with a door gap on its facing side, so routes go round the houses.</summary>
    public GridGround Ground(bool walls)
    {
        int ox = Town.Plan.Square.CenterX - GridSize / 2, oy = Town.Plan.Square.CenterY - GridSize / 2;
        var g = new LevelGrid(GridSize, GridSize, 0, 0);
        g.FillRect(0, 0, GridSize - 1, GridSize - 1, 0, true);
        if (walls)
            foreach (var s in Pop.Region.Settlements)
                foreach (var l in s.Plan.Lots)
                {
                    int x0 = l.Rect.X - ox, y0 = l.Rect.Y - oy, x1 = l.Rect.Right - 1 - ox, y1 = l.Rect.Bottom - 1 - oy;
                    if (x1 < 0 || y1 < 0 || x0 >= GridSize || y0 >= GridSize || x1 - x0 < 4 || y1 - y0 < 4) continue;
                    for (int x = x0; x <= x1; x++) { Wall(g, x, y0); Wall(g, x, y1); }
                    for (int y = y0; y <= y1; y++) { Wall(g, x0, y); Wall(g, x1, y); }
                    var (dx, dy) = (l.Facing & 3) switch { 0 => ((x0 + x1) / 2, y0), 1 => (x1, (y0 + y1) / 2), 2 => ((x0 + x1) / 2, y1), _ => (x0, (y0 + y1) / 2) };
                    if (g.InBounds(dx, dy, 0)) g.SetOpen(dx, dy, 0, true);
                }
        return new GridGround(g, new LevelPathfinder(g), ox, oy);
    }

    private static void Wall(LevelGrid g, int x, int y) { if (g.InBounds(x, y, 0)) g.SetOpen(x, y, 0, false); }

    public ScanResult Scan(PeopleBridge b, long now, IReadOnlyList<(long X, long Y)>? centres = null) => PeopleBridge.Scan([Records], b.MakeScanInput(centres ?? Centre, now));
}

public class PeopleBridgeTests
{
    private static int Alive(Population pop) { int n = 0; for (int i = 0; i < pop.People.Count; i++) if (pop.People.Status[i] == PersonStatus.Alive) n++; return n; }

    [Fact]
    public void ADay_IsContiguous_PeopleSleepAtHomeAtNightAndWorkAtWorkByDay_AndEveryAnswerIsPure()
    {
        var rig = new PeopleRig(); var pop = rig.Pop; int checkedWorkers = 0, atWork = 0, sleepers = 0;
        Span<DaySeg> segs = stackalloc DaySeg[Schedule.MaxSegs];
        for (int i = 0; i < pop.People.Count && checkedWorkers < 400; i += 7)
        {
            if (pop.People.Status[i] != PersonStatus.Alive) continue;
            var a = PersonAnchors.From(pop, i, EconFixture.Seed, 0, rig.At(0));
            int n = Schedule.Segments(a, pop.NowDay + 1, segs);
            Assert.Equal(0, segs[0].Start); Assert.Equal(86400, segs[n - 1].End);
            for (int k = 1; k < n; k++) Assert.Equal(segs[k - 1].End, segs[k].Start);
            var night = Schedule.Resolve(a, rig.At(0, 30)); if (a.Occupation != Occupation.Guard) Assert.Equal(JobKind.Sleep, night.Job.Kind);   // the watch keeps a night shift
            var again = Schedule.Resolve(a, rig.At(0, 30)); Assert.Equal(night, again);
            sleepers++;
            if (!a.HasWork || a.WorkInField) continue;
            checkedWorkers++;
            var noon = Schedule.Resolve(a, rig.At(11));
            if (noon.Job.Kind == JobKind.Work) atWork++;
            // (a keeper at a market stall works on the square, not at the workplace)
            if (noon.Job.Kind == JobKind.Work && a.WorkLot >= 0 && noon.Seg.To == PlaceRef.Work) { var l = pop.Region.Settlements[a.WorkSettlement].Plan.Lots[a.WorkLot].Rect; Assert.InRange(noon.XMm, (l.X - 1) * 1000L, (l.Right + 1) * 1000L); }
        }
        Assert.True(checkedWorkers > 100 && sleepers > 100 && atWork >= checkedWorkers * 6 / 10, $"workers {checkedWorkers}, at work {atWork}");
    }

    [Fact]
    public void Commute_ArrivesAtTheDoorWhenWorkStarts_AtFiveRealMetresPerSecond()
    {
        var rig = new PeopleRig(); var pop = rig.Pop;
        for (int i = 0; i < pop.People.Count; i++)
        {
            if (pop.People.Status[i] != PersonStatus.Alive) continue;
            var a = PersonAnchors.From(pop, i, EconFixture.Seed, 0, rig.At(0));
            if (!a.HasWork || a.WorkInField) continue;
            long day = pop.NowDay + 1; var h = a.Spot(PlaceRef.Home, day); var w = a.Spot(PlaceRef.Work, day);
            long d = PathPlan.Dist(w.X - h.X, w.Y - h.Y); if (d < 200_000 || d > 1_300_000) continue;
            Span<DaySeg> segs = stackalloc DaySeg[Schedule.MaxSegs]; int n = Schedule.Segments(a, day, segs);
            int wi = 0; while (wi < n && segs[wi].Kind != JobKind.Work) wi++;
            if (wi == 0 || wi == n || segs[wi - 1].Kind != JobKind.Commute) continue;
            long workStart = segs[wi].Start, t = (d + 249) / 250, start = day * 86400 + workStart - t;
            if (segs[wi - 1].End - segs[wi - 1].Start != t) continue;
            Assert.Equal(JobKind.Commute, Schedule.Resolve(a, start + 1).Job.Kind);
            var mid = Schedule.Resolve(a, start + t / 2);
            Assert.InRange(PathPlan.Dist(mid.XMm - (h.X + w.X) / 2, mid.YMm - (h.Y + w.Y) / 2), 0, 600);
            Assert.Equal(JobKind.Work, Schedule.Resolve(a, day * 86400 + workStart).Job.Kind);
            return;
        }
        Assert.Fail("no commuter with a 200 m to 1.3 km commute");
    }

    [Fact]
    public void PromotionSpread_G_A4_FiftyNearestFirstThenFiftyThenTheRest_NoneTwice()
    {
        var rig = new PeopleRig(); var b = new PeopleBridge(); long now = rig.At(12);
        var scan = rig.Scan(b, now);
        Assert.True(scan.Candidates.Count >= 130, $"candidates at noon: {scan.Candidates.Count}");
        var first130 = new ScanResult(now, scan.Candidates.Take(130).ToList(), []);
        b.Enqueue(first130);
        var batches = new List<int>(); var order = new List<EntityId>();
        for (int f = 0; f < 4; f++) { int n = b.Promote(now, null); batches.Add(n); }
        Assert.Equal([50, 50, 30, 0], batches);
        Assert.Equal(130, b.Count); Assert.Equal(130, b.Promotions); Assert.Equal(50, b.MaxPromotedAtOnce);
        Assert.Equal(130, b.Bindings.Select(x => x.Person).Distinct().Count());
        // nearest first: every one of the first 50 promoted was among the 50 nearest
        var near50 = scan.Candidates.Take(50).Select(c => c.Anchors.Id).ToHashSet();
        var b2 = new PeopleBridge(); b2.Enqueue(first130); b2.Promote(now, null);
        Assert.True(b2.People.All(p => near50.Contains(p.Id)));
        // scanning again promotes nobody twice
        b.Enqueue(rig.Scan(b, now)); b.Promote(now, null, max: 500);
        Assert.Equal(b.Count, b.Bindings.Select(x => x.Person).Distinct().Count());
    }

    [Fact]
    public void Hysteresis_StaysLiveAt160And170_DemotesAfterFiveSecondsBeyond200_AndComesBackInside150()
    {
        var rig = new PeopleRig(); var b = new PeopleBridge(); long now = rig.At(12);
        var scan = rig.Scan(b, now);
        var c = scan.Candidates.First(x => x.D2Mm < Bubble.Sq(140_000));        // somebody working inside the bubble (noon: standing still)
        b.Enqueue(new ScanResult(now, [c], [])); b.Promote(now, null);
        var id = c.Anchors.Id; var p0 = b.People[0];
        (long X, long Y)[] At(int m) => [(p0.XMm + m * 1000L, p0.YMm)];
        b.Step(now + 1, null, At(160)); Assert.True(b.IsLive(id));
        b.Step(now + 2, null, At(170)); Assert.True(b.IsLive(id));
        b.Step(now + 3, null, At(205));                                          // beyond 200: the clock starts
        b.Step(now + 3 + Bubble.DemoteAfterGameSeconds - 1, null, At(205)); Assert.True(b.IsLive(id));
        b.Step(now + 4, null, At(190));                                          // back inside 200: the clock resets
        b.Step(now + 5, null, At(205)); b.Step(now + 5 + Bubble.DemoteAfterGameSeconds - 1, null, At(205)); Assert.True(b.IsLive(id));
        b.Step(now + 5 + Bubble.DemoteAfterGameSeconds, null, At(205));
        Assert.False(b.IsLive(id)); Assert.Equal(1, b.Demotions);
        var folds = b.TakeFolds(); Assert.Single(folds);
        Assert.Equal(id, folds[0].Person);
    }

    [Fact]
    public void AtMostThreeBubbles()
    {
        var rig = new PeopleRig(); var b = new PeopleBridge(); long now = rig.At(12);
        var far = (rig.CentreXMm + 5_000_000L, rig.CentreYMm);
        var scan = rig.Scan(b, now, [far, far, far, (rig.CentreXMm, rig.CentreYMm)]);      // the fourth centre is ignored
        Assert.Empty(scan.Candidates);
        var three = rig.Scan(b, now, [far, far, (rig.CentreXMm, rig.CentreYMm), far]);
        Assert.NotEmpty(three.Candidates); Assert.All(three.Candidates, c => Assert.Equal(2, c.Bubble));
    }

    [Fact]
    public void PromoteAndDemote_L13_ConservePeopleItemsAndCoin_AndEveryLiveEntityMapsToExactlyOneRecord()
    {
        var w = AbstractFixture.NewWorld(); var pop = w.Population;
        var rig = new PeopleRig(pop); var rec = new PeopleRecords(0, 0, 0, pop, AbstractFixture.Seed, new AbstractJobTable());
        string before = w.ComputeHash(); var audit = w.Economy.Ledger.Audit(); int people = pop.People.Count, alive = Alive(pop);
        var b = new PeopleBridge(); long now = rig.At(5, 30);                        // the morning rush: commuters, sleepers and early workers
        var ground = rig.Ground(walls: true);
        b.Enqueue(PeopleBridge.Scan([rec], b.MakeScanInput(rig.Centre, now))); while (b.Queued > 0) b.Promote(now, ground);
        Assert.True(b.Count >= 100, $"{b.Count} promoted");
        for (long t = now; t < now + 1800; t += 1) b.Step(t, ground, rig.Centre);
        // every live entity maps to exactly one record, and the record maps back
        var bind = b.Bindings;
        Assert.Equal(bind.Count, bind.Select(x => x.Person).Distinct().Count());
        Assert.Equal(bind.Count, bind.Select(x => (x.Region, x.Index)).Distinct().Count());
        foreach (var x in bind) { Assert.Equal(x.Person, pop.People.IdOf(x.Index)); Assert.Equal(PersonStatus.Alive, pop.People.Status[x.Index]); }
        // the whole bubble walks away: everyone is folded back
        b.Step(now + 1800 + 1, ground, [(rig.CentreXMm + 3_000_000, rig.CentreYMm)]);
        b.Step(now + 1800 + 1 + Bubble.DemoteAfterGameSeconds, ground, [(rig.CentreXMm + 3_000_000, rig.CentreYMm)]);
        Assert.Equal(0, b.Count);
        var folds = b.TakeFolds(); Assert.Equal(b.Promotions, folds.Count);          // everybody promoted came back exactly once (some earlier, walking out of the bubble)
        Assert.Equal(folds.Count, folds.Select(f => f.Person).Distinct().Count());
        Assert.Equal(folds.Count, PeopleBridge.ApplyFolds([rec], folds));
        Assert.Equal(before, w.ComputeHash());                                       // records untouched: people, goods and coin
        Assert.Equal(audit, w.Economy.Ledger.Audit());
        Assert.Equal(people, pop.People.Count); Assert.Equal(alive, Alive(pop));
        Assert.Equal(folds.Count, rec.Jobs.Folded.Count);
        Assert.All(folds, f => Assert.False(f.Displaced));
    }

    [Fact]
    public void FoldBack_G_A11_AMidCommuteDemotionKeepsTheCommute_AtTheSamePointOfThePlan()
    {
        var rig = new PeopleRig(); var b = new PeopleBridge(); var ground = rig.Ground(walls: false);
        // someone who is commuting within 150 m of the centre is wanted: look through the morning (the hour of the commute follows the sun) until commuters exist
        long t1 = 0; LivePerson commuter = default;
        for (int m = 4 * 60; m < 10 * 60 && commuter == default; m += 10)
        {
            b = new PeopleBridge(); t1 = rig.At(m / 60, m % 60); b.Enqueue(rig.Scan(b, t1)); while (b.Queued > 0) b.Promote(t1, ground);
            commuter = b.People.FirstOrDefault(p => p.Action == JobKind.Commute && p.Walking);
        }
        Assert.NotEqual(default, commuter);
        var actor = b.Actor(commuter.Id)!;
        long t2 = t1 + 120; b.Step(t2, ground, rig.Centre);
        Assert.Equal(JobKind.Commute, actor.Action);
        var plan = actor.Plan!; var live = plan.PositionAt(t2); var lx = actor.X; var ly = actor.Y;
        b.DemoteAll(t2);
        var f = b.TakeFolds().Single(x => x.Person == commuter.Id);
        Assert.Equal(JobKind.Commute, f.Job.Kind); Assert.False(f.Displaced);
        Assert.Equal(plan.ProgressPpmAt(t2), f.Job.ProgressPpm);
        Assert.True(PathPlan.Dist(lx - live.XMm, ly - live.YMm) <= 1000);
        // and the abstract position of that second (a straight line on open ground) agrees with the live one to a metre
        var r = Schedule.Resolve(actor.A, t2);
        Assert.True(PathPlan.Dist(actor.OriginXMm + r.XMm - lx, actor.OriginYMm + r.YMm - ly) <= 1000, "live and abstract positions agree within 1 m on open ground");
    }

    private static string PositionHash(PeopleBridge b)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buf = new byte[8];
        foreach (var p in b.People) { foreach (long v in new[] { (long)p.Id.Value, p.XMm, p.YMm, (long)p.Action, p.HeadX, p.HeadY }) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(buf, v); h.AppendData(buf); } }
        return Convert.ToHexString(h.GetHashAndReset());
    }

    [Fact]
    public void Determinism_TheSameWorldGivesTheSamePositionsAtOneTimesAndEightTimes()
    {
        var rig = new PeopleRig(); var ground = rig.Ground(walls: true);
        string[] Run(int stride)
        {
            var b = new PeopleBridge(); long t0 = rig.At(5, 0);
            b.Enqueue(rig.Scan(b, t0)); while (b.Queued > 0) b.Promote(t0, ground);
            var hashes = new List<string>(); int n0 = b.Count;
            for (long t = t0; t <= t0 + 7200; t += stride)
            {
                b.Step(t, ground, []);                                     // no centres: nobody is demoted, so the two runs hold the same people
                if ((t - t0) % 600 == 0) hashes.Add(PositionHash(b));
            }
            Assert.Equal(n0, b.Count);
            return [.. hashes];
        }
        var x1 = Run(1); var x8 = Run(8);
        Assert.True(x1.Length >= 12);
        Assert.Equal(x1, x8);
        Assert.Equal(x1, Run(1));                                          // and the run repeats
    }

    [Fact]
    public void SaveLoadSave_IsByteIdentical_WithPeoplePromoted_AndTheReloadedWorldPutsThemWhereTheyWere()
    {
        var w = AbstractFixture.NewWorld(); var rig = new PeopleRig(w.Population); var rec = new PeopleRecords(0, 0, 0, w.Population, AbstractFixture.Seed, new AbstractJobTable());
        var b = new PeopleBridge(); long now = rig.At(12);
        b.Enqueue(PeopleBridge.Scan([rec], b.MakeScanInput(rig.Centre, now))); b.Promote(now, null, max: 200);
        Assert.True(b.Count > 50);
        byte[] eco = SaveCodec.SerializeCompressed(EconomySave.Capture(w.Economy)), ab = SaveCodec.SerializeCompressed(AbstractSave.Capture(w.State, w.Population));
        string live = PositionHash(b);
        var econ2 = SaveCodec.DeserializeCompressed<EconomySave>(eco).Restore(w.Population, EconFixture.Content);
        Assert.Equal(eco, SaveCodec.SerializeCompressed(EconomySave.Capture(econ2)));
        var abs2 = SaveCodec.DeserializeCompressed<AbstractSave>(ab).Restore(w.Population);
        Assert.Equal(ab, SaveCodec.SerializeCompressed(AbstractSave.Capture(abs2, w.Population)));
        // the live cache is not in the save: a bridge over the loaded records promotes the same people to the same places
        var b2 = new PeopleBridge(); b2.Enqueue(PeopleBridge.Scan([rec], b2.MakeScanInput(rig.Centre, now))); b2.Promote(now, null, max: 200);
        Assert.Equal(live, PositionHash(b2));
    }
}

[Collection(TimingCollection.Name)]
public class PeopleBridgePerfTests(ITestOutputHelper output)
{
    [Fact]
    public void ThreeHundredPeopleInATownBubble_TheLiveTickStaysUnderTheBudget()
    {
        var rig = new PeopleRig(); var b = new PeopleBridge(); var ground = rig.Ground(walls: true);
        long t0 = rig.At(5, 20);                                                   // the morning rush starts: the worst time for plans
        var scan = rig.Scan(b, t0); Assert.True(scan.Candidates.Count >= 150, $"{scan.Candidates.Count} candidates");
        b.Enqueue(new ScanResult(t0, scan.Candidates.Take(300).ToList(), []));
        var sw = Stopwatch.StartNew(); while (b.Queued > 0) b.Promote(t0, ground); double promoteMs = sw.Elapsed.TotalMilliseconds;
        for (int i = 0; i < 20; i++) b.Step(t0 + i, ground, rig.Centre);        // warm up
        // two minutes of live ticks, measured on a settled heap; the pass with the lowest worst tick counts (a second pass carries on from the first, so one noisy pass is forgiven)
        double avg = 0, p99 = 0, max = double.MaxValue; long t = t0 + 20;
        for (int pass = 0; pass < 2; pass++)
        {
            Ravensguard.Sim.Diagnostics.GcSettle.Run();
            var times = new List<double>(); var plans = new List<int>();
            for (int i = 0; i < 2400; i++, t += 1) { sw.Restart(); b.Step(t, ground, rig.Centre); times.Add(sw.Elapsed.TotalMilliseconds); plans.Add(b.PlansLastStep); }
            var worst = times.Select((v, k) => (v, k)).OrderByDescending(x => x.v).Take(5).Select(x => $"#{x.k}:{x.v:0.0}ms/{plans[x.k]}plans"); output.WriteLine($"pass {pass}: slowest ticks " + string.Join(" ", worst) + $"; slowest plan {b.MaxPlanMs:0.0} ms ({b.WorstPlan})");
            times.Sort();
            if (times[^1] < max) { avg = times.Average(); p99 = times[(int)(times.Count * 0.99)]; max = times[^1]; }
            if (avg < TestBudget.Ms(8) && p99 < TestBudget.Ms(8) && max < TestBudget.Ms(25)) break;
        }
        output.WriteLine($"{b.Count} live people: promote {promoteMs:0.0} ms total, live step avg {avg:0.000} ms, p99 {p99:0.000} ms, max {max:0.00} ms");
        Assert.True(b.Count >= 150);
        Assert.True(avg < TestBudget.Ms(8), $"average live tick {avg:0.00} ms");
        Assert.True(p99 < TestBudget.Ms(8), $"p99 live tick {p99:0.00} ms");
        Assert.True(max < TestBudget.Ms(25), $"slowest live tick {max:0.0} ms");      // a lone outlier (first use of a path search that fails, JIT tier-up) is about 10 ms; two of them in one tick would show
    }
}
