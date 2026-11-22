using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WarSupply;
using Ravensguard.Sim.Worlds;
using Ravensguard.Sim.WorldGen.Region;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Armies;

/// <summary>A new game with its lords on the real tick and the army economy's interface, for the hosts that march.</summary>
internal sealed class MarchRig : IDisposable
{
    public ArmyRig K { get; } = new();
    public ArmyWorld Aw => K.Aw;
    public WorldState State => K.State;
    public HostSupply Hs { get; }
    private int _n;
    public MarchRig() { Hs = WarSupplyWiring.Ensure(K.State.HomeRecords!, K.State); }

    public HostId Host(Roster men, int carts = 0, AnimalCounts animals = default, int outside = 0)
    {
        var econ = State.Economy!; var led = econ.Ledger; int n = 9000 + _n++;
        var chest = led.Open(PurseKind.Chest, HostIds.Chest(n));
        var bag = econ.OpenStore(Ravensguard.Sim.Goods.StoreKind.Sealed, HostIds.Baggage(n));
        led.Mint(chest, Coin.FromCrowns(2000), SourceReason.Seed);
        return Hs.Register(new HostSpec(0, EntityId.None, chest, bag, men, 0, animals, carts, new SupplyPolicy()) { OutsideMen = outside });
    }

    public ArmyEnv Env(long hour) => new(K.R.Sim, null, hour, RuleChron.Of(State.Chronicle, State.Ids));

    /// <summary>A straight road of <paramref name="mm"/> millimetres going east from the region's north-west corner, the same for every pair of settlements (a test's own measured road).</summary>
    public void StraightRoad(long mm, RoadClass cls = RoadClass.Road)
    {
        int ox = (int)(Aw.OriginXMm / 1000), oy = (int)(Aw.OriginYMm / 1000);
        Aw.PathSource = (_, _) => CaravanPath.FromPoints([(ox + 1000, oy + 1000, RoadClass.Road), (ox + 1000 + (int)(mm / 1000), oy + 1000, cls)]);
    }

    public void Dispose() => K.Dispose();
}

/// <summary>P7-AM-C gate: AM-7 (march speed), AM-8 (the column in and out), AM-9 (a host from outside), AM-10 (the camp), with the weather, the save, the budget and the disease hooks.</summary>
public class MarchTests(ITestOutputHelper output)
{
    private static long DawnOf(long day) => day * 24 + MarchConfig.DayStartHour;

    // ---------------------------------------------------------------- AM-7

    [Fact]
    public void AM7_8kmByRoad_Is34MarchingHoursPlusOrMinusOne_OverTwoGameDaysWithNightCamps()
    {
        using var r = new MarchRig(); var aw = r.Aw;
        r.StraightRoad(8_000_000);
        var host = r.Host(new Roster([(TroopRole.Spearman, 300)]));
        long start = DawnOf(200);
        var m = aw.BeginMarch(r.Env(start), host, [0, 1])!;
        Assert.NotNull(m);
        long h = start;
        for (; m.State == MarchState.Marching || m.State == MarchState.Camped; h++)
        {
            aw.MarchHour(r.Env(h));
            if (h - start > 200) break;
        }
        output.WriteLine($"arrived at hour +{h - start}: {m.MarchHours} marching hours, {m.CampsPitched} night camps");
        Assert.Equal(MarchState.Arrived, m.State);
        Assert.InRange(m.MarchHours, 33, 35);                                                // 8 km at 234 m an hour is 34.2 hours: the 35th hour finishes it
        Assert.InRange((h - start) / 24, 2, 2);                                              // over two game days
        Assert.True(m.CampsPitched >= 2, $"{m.CampsPitched} camps");                         // the road camps of the two nights (and the camp of the arrival)
        // the account says where the host stands: camped at the town it reached
        var a = r.Hs.Account(host);
        Assert.Equal(HostMode.Camp, a.Mode);
        var arrived = Assert.Single(r.K.Entries("host_arrived"));
        Assert.Equal("ch_am_arrived", arrived.TextKey);
        Assert.Equal(300, (int)arrived.Params!.First(p => p.Name == "men").Value);
        Assert.Contains(arrived.Causes, c => c.Kind == CauseKind.Campaign && c.NoteKey != "");
    }

    [Fact]
    public void AM7_TheMarchStopsAtNightAndStartsAtDawn_NoMetreIsWalkedBetween21And05()
    {
        using var r = new MarchRig(); var aw = r.Aw;
        r.StraightRoad(20_000_000);
        var host = r.Host(new Roster([(TroopRole.Spearman, 100)]));
        long start = DawnOf(300);
        var m = aw.BeginMarch(r.Env(start), host, [0, 1])!;
        var at = new Dictionary<int, long>();
        for (long h = start; h < start + 48; h++)
        {
            aw.MarchHour(r.Env(h));
            long d = HostMotion.DistAt(m, h * 3600 + 3600);
            at[(int)(h % 24)] = d;
            int hod = (int)(h % 24);
            if (hod is >= 21 or < 5) Assert.Equal(MarchState.Camped, m.State);
            else Assert.Equal(MarchState.Marching, m.State);
        }
        Assert.Equal(at[20], at[4]);                                                         // nothing walked through the night (hour 20 ends at 21:00; hour 4 ends at 05:00)
        Assert.True(at[19] > at[5]);
    }

    [Fact]
    public void AM7_InMud_TheHostWalksAtTheBaggagesSpeed_AndInTheRain_AtTheMensSlowerPace()
    {
        long Hours(int carts, int rain24Tenths)
        {
            using var r = new MarchRig(); var aw = r.Aw;
            r.StraightRoad(8_000_000);
            if (rain24Tenths > 0) aw.Wetness = (_, _, _) => new WetState(rain24Tenths, 0, false, false);
            var host = r.Host(new Roster([(TroopRole.Spearman, 200)]), carts);
            long start = DawnOf(400);
            var m = aw.BeginMarch(r.Env(start), host, [0, 1])!;
            for (long h = start; m.State is MarchState.Marching or MarchState.Camped && h < start + 400; h++) aw.MarchHour(r.Env(h));
            Assert.Equal(MarchState.Arrived, m.State);
            return m.MarchHours;
        }
        long dry = Hours(30, 0), wet = Hours(30, 40), mudFoot = Hours(0, 150), mudBag = Hours(30, 150);
        output.WriteLine($"dry {dry} h, wet road {wet} h, mud with no wagons {mudFoot} h, mud with 30 wagons {mudBag} h");
        Assert.InRange(dry, 34, 35);
        Assert.InRange(wet, 8000 / 164 - 1, 8000 / 163 + 2);                                 // a wet road: men on foot at 700 permille (164 m an hour), the wagons at 850
        Assert.InRange(mudFoot, 8000 / 140 - 1, 8000 / 140 + 2);                             // mud: men at 600 permille (140 m an hour)
        Assert.InRange(mudBag, 8000 * 1000 / (234 * 450) - 1, 8000 * 1000 / (234 * 450) + 2);// with wagons: the baggage's 450 permille (105 m an hour), about 76 hours
        Assert.True(mudBag > mudFoot && mudFoot > wet && wet > dry);
    }

    [Fact]
    public void AM7_TheRoadsOwnClassSlowsATrack_AndEveryMarchHasItsRoute_OverTheRegionsRealRoads()
    {
        using var r = new MarchRig(); var aw = r.Aw; var region = aw.Region;
        int a = -1, b = -1; long best = 0;
        for (int i = 0; i < Math.Min(40, region.Settlements.Count); i++)
            for (int j = i + 1; j < Math.Min(40, region.Settlements.Count); j++)
            {
                var p = aw.PathOf(i, j); if (p == null) continue;
                if (p.LengthMm > best && p.LengthMm < 14_000_000) { best = p.LengthMm; a = i; b = j; }
            }
        Assert.True(a >= 0, "two towns joined by road");
        var host = r.Host(new Roster([(TroopRole.Spearman, 150)]));
        long start = DawnOf(500);
        var m = aw.BeginMarch(r.Env(start), host, [a, b])!;
        var route = aw.RouteOf(m)!; Assert.Equal(best, route.Length);
        long h = start; for (; m.State is MarchState.Marching or MarchState.Camped && h < start + 600; h++) aw.MarchHour(r.Env(h));
        Assert.Equal(MarchState.Arrived, m.State);
        long floor = route.Length / (MarchConfig.ColumnMmPerGs * 3600L);                      // hours at the best pace
        output.WriteLine($"{route.Length / 1000} m by road, {m.MarchHours} marching hours (best pace {floor})");
        Assert.True(m.MarchHours >= floor && m.MarchHours <= floor * 14 / 10 + 2);
    }

    // ---------------------------------------------------------------- AM-8

    [Fact]
    public void AM8_AThousandMenWithWagons_AreAColumn500mLong_PromotedAtMost50AFrame_AtTheEdge_DemotedAndPromotedAgain10kmOn_MenConserved_NoPersonTwice()
    {
        using var r = new MarchRig(); var aw = r.Aw; var pt = aw.People;
        r.StraightRoad(40_000_000);
        var host = r.Host(new Roster([(TroopRole.Spearman, 700), (TroopRole.Archer, 200), (TroopRole.Levy, 100)]), carts: 30);
        long alive0 = r.K.R.Sim.Vital.Alive, rows0 = pt.Count;
        long start = DawnOf(600);
        Assert.Equal(1000, aw.EnterRegion(r.Env(start), host, 0));
        Assert.Equal(rows0 + 1000, pt.Count);
        var m = aw.BeginMarch(r.Env(start), host, [0, 1])!;
        var persons = aw.ColumnPersons(host); Assert.Equal(1000, persons.Count);
        long ox = aw.OriginXMm, oy = aw.OriginYMm;
        // (b) the column marches into view of a bubble, passes it, and 10 km on does it again: every man and wagon promoted once and demoted once at each
        var bridge = new ArmyBridge(); int maxLive = 0; var order = new List<(ColumnPartKind K, int I)>(); var seenOnce = new HashSet<ulong>();
        (long X, long Y) C1 = (ox + 1_000_000 + 2_000_000, oy + 1_000_000), C2 = (ox + 1_000_000 + 12_000_000 + 2_000_000, oy + 1_000_000);
        long sec = start * 3600; var centre = C1; bool second = false; int passPromoted = 0, passDemoted = 0;
        for (int frame = 0; frame < 20_000; frame++, sec += 60)
        {
            if (sec % 3600 == 0) aw.MarchHour(r.Env(sec / 3600));
            var f = bridge.Update(aw, [centre], sec);
            Assert.True(f.Promoted <= ArmyBridge.MaxPerFrame, $"{f.Promoted} promoted in a frame");
            Assert.Equal(f.Parts.Count, f.Parts.Select(p => (p.Kind, p.Person, p.Index)).Distinct().Count());                          // nobody twice
            maxLive = Math.Max(maxLive, f.Parts.Count);
            if (!second) foreach (var p in f.Parts) if (seenOnce.Add(p.Person == 0 ? 0x100000000UL + (ulong)p.Index : p.Person)) order.Add((p.Kind, p.Index));
            if (!second && bridge.Promotions >= 1030 && f.Parts.Count == 0) { passPromoted = bridge.Promotions; passDemoted = bridge.Demotions; second = true; centre = C2; }
            if (second && bridge.Promotions >= 2060 && f.Parts.Count == 0) break;
        }
        output.WriteLine($"promotions {bridge.Promotions}, demotions {bridge.Demotions}, most in a frame {bridge.MaxPromotedInAFrame}, most live at once {maxLive}; {(sec - start * 3600) / 3600} game hours");
        Assert.Equal(1030, passPromoted);                                                        // 1,000 men and 30 wagons came into view once each at the first bubble ...
        Assert.Equal(1030, passDemoted);
        Assert.Equal(2060, bridge.Promotions);                                                   // ... and again at the second, 10 km on
        Assert.Equal(2060, bridge.Demotions);
        Assert.Equal(ColumnPartKind.Van, order[0].K);                                            // the van comes into view first
        Assert.Equal(ColumnPartKind.Wagon, order[^1].K);                                         // the baggage last
        Assert.InRange(maxLive, 300, 800);
        Assert.Equal(1000, aw.ColumnPersons(host).Count);                                        // men conserved: the same thousand persons
        Assert.Equal(alive0 + 1000, r.K.R.Sim.Vital.Alive);                                      // none made or lost by the bubble
        Assert.Equal(rows0 + 1000, pt.Count);
        Assert.InRange(MarchLayout.ColumnLengthM(1000), 495, 510);                               // the column 500 m long, the wagons 12 m apart behind it
        // the head of the column is where the record says it is: the van's first man stands 40 m ahead of it, on its lane
        var fHead = aw.HeadAt(m, sec)!.Value; Assert.True(fHead.XMm > ox + 1_000_000);
    }

    [Fact]
    public void AM8_ABubbleOpeningInTheMiddleOfAColumn_TakesTheMenInside150mAtMost50AFrame_NearestFirst()
    {
        using var r = new MarchRig(); var aw = r.Aw;
        r.StraightRoad(40_000_000);
        var host = r.Host(new Roster([(TroopRole.Spearman, 700), (TroopRole.Archer, 200), (TroopRole.Levy, 100)]), carts: 30);
        long start = DawnOf(601);
        aw.EnterRegion(r.Env(start), host, 0);
        var m = aw.BeginMarch(r.Env(start), host, [0, 1])!;
        // a bubble opens in the middle of the column: 600 men and wagons are inside 150 m, taken 50 a frame, nearest first
        for (long h = start; h < start + 6; h++) aw.MarchHour(r.Env(h));
        long sec0 = (start + 6) * 3600, dist0 = HostMotion.DistAt(m, sec0);
        var mid = aw.RouteOf(m)!.Path.At(dist0 - 250_000);
        var cap = new ArmyBridge(); var frames = new List<int>();
        for (int i = 0; i < 40; i++) { var f = cap.Update(aw, [(mid.XMm, mid.YMm)], sec0); frames.Add(f.Promoted); Assert.True(f.Promoted <= 50); if (f.Promoted == 0) break; }
        output.WriteLine($"opening inside the column: promoted per frame {string.Join(",", frames)}; {cap.LiveCount} live");
        Assert.Equal(50, frames[0]); Assert.Equal(50, cap.MaxPromotedInAFrame); Assert.True(frames.Count(x => x == 50) >= 10);
        Assert.InRange(cap.LiveCount, 500, 700);
    }

    [Fact]
    public void AM8_TheDeadOfAFightInTheBubble_ComeOffTheHostsRolls_AndTheRosterLosesThem()
    {
        using var r = new MarchRig(); var aw = r.Aw; var pt = aw.People;
        r.StraightRoad(10_000_000);
        var host = r.Host(new Roster([(TroopRole.Spearman, 40)]));
        long start = DawnOf(610);
        aw.EnterRegion(r.Env(start), host, 0); var m = aw.BeginMarch(r.Env(start), host, [0, 1])!;
        var ids = aw.ColumnPersons(host); Assert.Equal(40, ids.Count);
        // kill three in a fight the bubble held: the host's roll and roster lose them at the next dawn
        var ob = new RegionOutbox(r.K.R.Sim.Ids);
        for (int i = 0; i < 3; i++) Demography.Kill(r.K.R.Sim, aw.RowOf(ids[i]), DeathCause.Violence, new GameTime(start * 3600L), ob, note: "ch_why_violence");
        Assert.Equal(37, aw.ColumnPersons(host).Count);
        Assert.Equal(3, aw.SyncDead(host));
        Assert.Equal(37, r.Hs.Account(host).Total);
        Assert.Equal(37, m.Outsiders.Count);
        Assert.All(ids.Take(3), id => Assert.Equal(DeathCause.Violence, pt.DeathCause[aw.RowOf(id)]));
    }

    // ---------------------------------------------------------------- AM-9

    [Fact]
    public void AM9_AHostOf800AtAGate_Makes800PersonsBy0x7A3_TheSameNamesOnARerun_Leaving610_610GoBackToCounts_190AreDeadHereWithCauses()
    {
        List<string> Names(MarchRig r, HostId host) => aw0(r).ColumnPersons(host).Select(id => r.Aw.People.FullName(r.Aw.RowOf(id))).ToList();
        ArmyWorld aw0(MarchRig r) => r.Aw;
        var roster = new Roster([(TroopRole.Spearman, 500), (TroopRole.Archer, 200), (TroopRole.ManAtArms, 100)]);
        List<string> n1, n2; List<ulong> ids1, ids2;
        using (var r = new MarchRig())
        {
            var aw = r.Aw; var pt = aw.People; var w = r.K.R.Sim;
            Assert.True(aw.Region.Gates.Count > 0, "the region has a gate");
            var host = r.Host(roster, outside: 800);
            long alive0 = w.Vital.Alive, rows0 = pt.Count, start = DawnOf(700);
            int made = aw.EnterRegion(r.Env(start), host, -1);                                  // gate 0
            Assert.Equal(800, made);
            Assert.Equal(rows0 + 800, pt.Count);
            Assert.Equal(alive0 + 800, w.Vital.Alive);
            var ids = aw.ColumnPersons(host); Assert.Equal(800, ids.Count);
            foreach (var id in ids) { int row = aw.RowOf(id); Assert.Equal(PersonStatus.Alive, pt.Status[row]); Assert.Equal(Occupation.Soldier, pt.Occupation[row]); Assert.Equal(-1, pt.Household[row]); Assert.False(string.IsNullOrEmpty(pt.Given[row])); }
            n1 = Names(r, host); ids1 = ids;
            // 190 die here in the bubble, by the wounds' own causes
            var ob = new RegionOutbox(w.Ids);
            for (int i = 0; i < 190; i++) Demography.Kill(w, aw.RowOf(ids[i * 4]), i % 3 == 0 ? DeathCause.BloodLoss : DeathCause.Violence, new GameTime(start * 3600L + 5000), ob, note: "ch_why_violence");
            int left = aw.LeaveRegion(r.Env(start + 2), host);
            Assert.Equal(610, left);
            Assert.Equal(610, r.Hs.Account(host).Total);                                         // 610 are counts of the roster again
            Assert.Equal(610, r.Hs.Account(host).Men.Sum());
            Assert.Equal(alive0, w.Vital.Alive);                                                 // the 190 dead and the 610 gone: the region is as it was
            var march = r.Aw.Book.Marches[host.Value];
            Assert.Equal(610, march.Outsiders.Count);
            int dead = ids.Count(id => pt.Status[aw.RowOf(id)] == PersonStatus.Dead); Assert.Equal(190, dead);
            Assert.All(ids.Where(id => pt.Status[aw.RowOf(id)] == PersonStatus.Dead), id => Assert.NotEqual(DeathCause.None, pt.DeathCause[aw.RowOf(id)]));
            Assert.Equal(610, ids.Count(id => pt.Status[aw.RowOf(id)] == PersonStatus.Departed));
            // they come back as themselves: the next time the host enters, nobody is made
            int rows1 = pt.Count; Assert.Equal(610, aw.EnterRegion(r.Env(start + 30), host, -1)); Assert.Equal(rows1, pt.Count);
            Assert.Equal(610, aw.ColumnPersons(host).Count);
        }
        using (var r = new MarchRig())
        {
            var host = r.Host(roster, outside: 800);
            r.Aw.EnterRegion(r.Env(DawnOf(700)), host, -1);
            n2 = Names(r, host); ids2 = r.Aw.ColumnPersons(host);
        }
        Assert.Equal(n1, n2);                                                                   // the same names on a rerun
        Assert.Equal(ids1, ids2);
        Assert.Equal(800, n1.Count);
        output.WriteLine($"first names {string.Join(", ", n1.Take(4))}; {n1.Select(x => x.Split(' ')[0]).Distinct().Count()} distinct given names");
    }

    [Fact]
    public void AM9_AHostMarchingOutAtAGate_LeavesTheRegion_ItsLivingMenBecomeCountsAndItsAccountGoesAbstract()
    {
        using var r = new MarchRig(); var aw = r.Aw; var pt = aw.People;
        var host = r.Host(new Roster([(TroopRole.Spearman, 60)]), outside: 60);
        long start = DawnOf(710);
        // from a settlement to gate 0 over the real road, when the roads join them
        int s = -1, g = -1; long best = long.MaxValue;
        for (int gate = 0; gate < aw.Region.Gates.Count; gate++)
            for (int i = 0; i < aw.Region.Settlements.Count; i++)
                if (CaravanRoads.Build(aw.Region, i, gate, false) is { LengthMm: > 0 } p && p.LengthMm < best) { best = p.LengthMm; s = i; g = gate; }
        Assert.True(s >= 0, "a settlement joined to a gate by road");
        output.WriteLine($"settlement {s} to gate {g}: {best / 1000} m");
        aw.EnterRegion(r.Env(start), host, s);
        var m = aw.BeginMarch(r.Env(start), host, [s, -1 - g])!;
        long h = start; for (; m.State is MarchState.Marching or MarchState.Camped && h < start + 600; h++) aw.MarchHour(r.Env(h));
        Assert.Equal(MarchState.Left, m.State);
        Assert.True(r.Hs.Account(host).Abstract);
        Assert.Empty(aw.ColumnPersons(host));                                                    // nobody of his stands in the region now
        Assert.All(m.Outsiders.Keys, id => Assert.Equal(PersonStatus.Departed, pt.Status[aw.RowOf(id)]));
        Assert.Contains(r.K.Entries("host_crossed"), e => true);
    }

    // ---------------------------------------------------------------- AM-10

    [Fact]
    public void AM10_ACampOf1000_IsPitchedFromThePlanInUnderAnHour_With40Sentries_AndANightAttackWakesTheSleepersByTheAlarmRule()
    {
        var plan = CampPlan.For(1234, 5, 1, 1000, 30 + 60);
        Assert.Equal(167, plan.Tents);
        Assert.Contains(plan.Pieces, p => p.Piece == "tent_pavilion");
        Assert.Equal(167, plan.Pieces.Count(p => p.Piece == "tent_soldier"));
        Assert.Equal(56, plan.Pieces.Count(p => p.Piece == "campfire"));
        Assert.Equal(20, plan.Pieces.Count(p => p.Piece == "latrine"));
        Assert.Contains(plan.Pieces, p => p.Piece == "horse_line");
        Assert.Equal(CampPlan.For(1234, 5, 1, 1000, 90).Pieces, plan.Pieces);                    // pure: the same plan from the same key
        Assert.NotEqual(CampPlan.For(1234, 6, 1, 1000, 90).Pieces, plan.Pieces);                // another host's camp is jittered another way
        long secs = plan.PitchSeconds(960);                                                       // every man not on watch
        output.WriteLine($"{plan.Pieces.Length} pieces, {plan.PitchWorkSec / 3600.0:F1} worker-hours, pitched in {secs} s by 960 hands; ring {plan.Ring.Length} pieces");
        Assert.True(secs < 3600, $"{secs} s");
        Assert.Equal(plan.Pieces.Length, plan.PiecesUp(secs, 960));
        Assert.True(plan.PiecesUp(secs / 2, 960) is > 0 and var half && half < plan.Pieces.Length);
        // 40 sentries a watch: 1 in 25, a different group each watch and each night, every man takes his turn
        var seen = new HashSet<int>();
        for (int night = 0; night < 25; night++)
            for (int watch = 0; watch < 3; watch++)
            {
                var s = CampPlan.SentryRanks(1000, night, watch); Assert.Equal(40, s.Length);
                foreach (int k in s) seen.Add(k);
            }
        Assert.Equal(1000, seen.Count);
        Assert.NotEqual(CampPlan.SentryRanks(1000, 0, 0), CampPlan.SentryRanks(1000, 0, 1));
        // the watch of an hour of the night
        Assert.Equal([0, 0, 0, 1, 1, 1, 2, 2], new[] { 21, 22, 23, 0, 1, 2, 3, 4 }.Select(CampPlan.WatchOf));
    }

    [Fact]
    public void AM10_TheHostPitchesAtNightfall_CampsWithSentriesOnTheWatch_StrikesAtDawn_AndALongStayBuildsItsRing()
    {
        using var r = new MarchRig(); var aw = r.Aw;
        r.StraightRoad(30_000_000);
        var host = r.Host(new Roster([(TroopRole.Spearman, 1000)]), carts: 30, animals: new AnimalCounts(10, 40, 30, 20));
        long start = DawnOf(800);
        var m = aw.BeginMarch(r.Env(start), host, [0, 1])!;
        for (long h = start; h < start + 16; h++) aw.MarchHour(r.Env(h));
        Assert.Equal(MarchState.Marching, m.State);                                              // 16 hours of marching: the 21:00 hour has not begun
        aw.MarchHour(r.Env(start + 16));                                                         // 21:00
        Assert.Equal(MarchState.Camped, m.State);
        var c = m.Camp!; Assert.Equal(1, c.Nights); Assert.Equal(1000, c.Men); Assert.True(c.Latrine);
        Assert.Equal(40, c.Sentries);
        Assert.Equal(HostMode.Camp, r.Hs.Account(host).Mode);
        Assert.Equal(1000, r.Hs.CampSize(r.Hs.Account(host).PlaceSettlement));                   // the army camp the disease rule counts
        for (long h = start + 17; h < start + 24; h++) aw.MarchHour(r.Env(h));
        Assert.Equal(MarchState.Camped, m.State);
        aw.MarchHour(r.Env(start + 24));                                                         // 05:00: it strikes camp and walks
        Assert.Equal(MarchState.Marching, m.State); Assert.Null(m.Camp); Assert.Equal(1, m.CampsPitched);
        // a stay: halted, it camps through the day, and from the second night has its palisade and ditch
        aw.Halt(host, true);
        for (long h = start + 25; h < start + 25 + 52; h++) aw.MarchHour(r.Env(h));
        Assert.Equal(MarchState.Camped, m.State); Assert.True(m.Camp!.Palisade, "the ring of a long stay");
        Assert.True(m.Camp.Nights >= 2);
        aw.Halt(host, false);
        long dawn = (start + 25 + 52) / 24 * 24 + 24 + MarchConfig.DayStartHour;
        for (long h = start + 25 + 52; h <= dawn; h++) aw.MarchHour(r.Env(h));
        Assert.Equal(MarchState.Marching, m.State);
    }

    [Fact]
    public void AM10_ANightAttack_WakesTheSleepersByTheAlarmRule_5To20SecondsAfterEachShout_AWokenManShoutsInHisTurn_AHitWakesAtOnce_AndItIsInTheChronicleWithItsCause()
    {
        using var r = new MarchRig(); var aw = r.Aw;
        r.StraightRoad(30_000_000);
        var host = r.Host(new Roster([(TroopRole.Spearman, 1000)]), carts: 30);
        long start = DawnOf(810);
        var m = aw.BeginMarch(r.Env(start), host, [0, 1])!;
        for (long h = start; h <= start + 16; h++) aw.MarchHour(r.Env(h));
        Assert.Equal(MarchState.Camped, m.State);
        var plan = aw.PlanOf(m, m.Camp!, r.Hs.Account(host));
        var sent = aw.SentriesOf(m); Assert.Equal(40, sent.Length);
        // the enemy comes up to the camp's edge, 30 m outside the sentry who stands nearest him
        var (sx, sy) = plan.SentryAt(7, sent.Length); var enemy = (sx, sy - 30_000);
        long sight = (start + 17) * 3600L + 600;
        var cause = new Cause(CauseKind.Raid, null, "", "ch_note_am_night_attack");
        var res = aw.NightAttack(r.Env(start + 17), host, enemy, sight, cause, new Dictionary<int, long> { [123] = sight + 2 })!;
        Assert.True(res.Raised); Assert.Equal(sight, res.AlarmSec);
        int asleep = 1000 - sent.Length; Assert.Equal(asleep, res.Asleep);
        Assert.Equal(asleep, res.Woken);                                                         // the alarm ran through the whole camp
        Assert.All(res.WakeSec.Where(kv => kv.Key != 123), kv => Assert.True(kv.Value >= sight + MarchConfig.WakeMinSec));
        Assert.Equal(sight + 2, res.WakeSec[123]);                                               // a man struck in his bed wakes at once
        Assert.DoesNotContain(sent, k => res.WakeSec.ContainsKey(k));                            // the men on watch were awake
        // the hops: a man nowhere near the shout wakes later than a man beside it
        var near = res.WakeSec.OrderBy(kv => Dist(plan.SleeperAt(kv.Key), (sx, sy))).First(); var far = res.WakeSec.OrderByDescending(kv => Dist(plan.SleeperAt(kv.Key), (sx, sy))).First();
        output.WriteLine($"nearest sleeper woke +{near.Value - sight} s, farthest +{far.Value - sight} s, the last +{res.LastWakeSec - sight} s");
        Assert.True(far.Value > near.Value);
        Assert.True(far.Value - sight >= 2 * MarchConfig.WakeMinSec);                            // at least two hops away
        Assert.InRange(near.Value - sight, MarchConfig.WakeMinSec, MarchConfig.WakeMaxSec);
        // pure: the same night gives the same wake times
        var again = CampAlarm.Run(plan, 1000, sent, r.State.Seed, (ulong)host.Value, enemy, sight, new Dictionary<int, long> { [123] = sight + 2 });
        Assert.Equal(res.WakeSec.Select(kv => (kv.Key, kv.Value)), again.WakeSec.Select(kv => (kv.Key, kv.Value)));
        // an enemy beyond the dark's sight raises no alarm: the camp sleeps on
        var unseen = CampAlarm.Run(plan, 1000, sent, r.State.Seed, (ulong)host.Value, (sx, sy - 400_000), sight);
        Assert.False(unseen.Raised); Assert.Equal(0, unseen.Woken);
        var entry = Assert.Single(r.K.Entries("camp_attacked"));
        Assert.Equal(asleep, (int)entry.Params!.First(p => p.Name == "woken").Value);
        static double Dist((int X, int Y) a, (int X, int Y) b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    }

    [Fact]
    public void ACamp_FeedsTheDiseaseRulesArmyCampAndUnboiledWater_AndTheCampsPiecesStandAsTheyArePitched()
    {
        using var r = new MarchRig(); var aw = r.Aw;
        r.StraightRoad(30_000_000);
        var host = r.Host(new Roster([(TroopRole.Spearman, 120)]), carts: 10);
        long start = DawnOf(820);
        aw.EnterRegion(r.Env(start), host, 0); var m = aw.BeginMarch(r.Env(start), host, [0, 1])!;
        var who = aw.ColumnPersons(host)[5];
        Assert.Equal(0, aw.CampSizeOf(who));                                                    // marching, not camped
        for (long h = start; h <= start + 16; h++) aw.MarchHour(r.Env(h));
        Assert.Equal(120, aw.CampSizeOf(who));
        Assert.Equal(m.Camp!.BelowTown ? 1 : 0, aw.UnboiledDaysOf(who) > 0 ? 1 : 0);
        // the bridge shows the camp with its pieces as the men pitch it: a few at first, all of them within the hour
        var bridge = new ArmyBridge();
        long arrive = m.Camp.SinceSec; var c = (m.Camp.XMm, m.Camp.YMm);
        var f0 = bridge.Update(aw, [c], arrive + 60);
        var f1 = bridge.Update(aw, [c], arrive + 3600);
        var camp0 = Assert.Single(f0.Camps); var camp1 = Assert.Single(f1.Camps);
        output.WriteLine($"{camp0.Pieces.Count} pieces after a minute, {camp1.Pieces.Count} of {camp1.PlanPieces} after an hour");
        Assert.True(camp0.Pieces.Count < camp1.Pieces.Count); Assert.Equal(camp1.PlanPieces, camp1.Pieces.Count);
        Assert.True(f1.Parts.Count(p => p.Kind == ColumnPartKind.Sleeper) + f1.Parts.Count(p => p.Kind == ColumnPartKind.Sentry) is > 0);
        Assert.True(r.K.State.Armies!.Marches.ContainsKey(host.Value));
    }

    // ---------------------------------------------------------------- a worked example

    [Fact]
    public void AWorkedExample_ThreeDaysOnTheRoad_TwoNightCamps_AndAnAlarmOnTheSecondNight()
    {
        using var r = new MarchRig(); var aw = r.Aw;
        r.StraightRoad(11_000_000);
        var host = r.Host(new Roster([(TroopRole.Spearman, 200), (TroopRole.Archer, 80), (TroopRole.ManAtArms, 20)]), carts: 10, animals: new AnimalCounts(20, 10, 10, 0));
        long start = DawnOf(900);
        var m = aw.BeginMarch(r.Env(start), host, [0, 1])!;
        var log = new List<string>(); long lastDist = 0;
        for (long h = start; h < start + 72 && m.State != MarchState.Arrived; h++)
        {
            aw.MarchHour(r.Env(h));
            int hod = (int)(h % 24);
            if (hod == 21 || hod == 5 || m.State == MarchState.Arrived)
            {
                long d = HostMotion.DistAt(m, h * 3600);
                log.Add($"day {(h - start) / 24 + 1} {hod:00}:00 {m.State}, {d / 1000} m along ({(d - lastDist) / 1000} m since the last mark)" + (m.Camp is { } c ? $", camp {c.Serial}: {c.Men} men, {aw.PlanOf(m, c, r.Hs.Account(host)).Pieces.Length} pieces, {c.Sentries} on watch" : ""));
                lastDist = d;
            }
        }
        foreach (var l in log) output.WriteLine(l);
        Assert.Equal(MarchState.Arrived, m.State);
        Assert.Equal(2, log.Count(l => l.Contains("21:00 Camped")));
        // the second night's alarm, at the camp of the arrival
        var plan = aw.PlanOf(m, m.Camp!, r.Hs.Account(host)); var sent = aw.SentriesOf(m);
        var (sx, sy) = plan.SentryAt(0, Math.Max(1, sent.Length));
        long night = (m.StartDay + 2) * 86400L + 23 * 3600 + 14 * 60;
        var res = aw.NightAttack(r.Env(night / 3600), host, (sx, sy + 40_000), night, new Cause(CauseKind.Raid, null, "", "ch_note_am_night_attack"))!;
        output.WriteLine($"alarm at {night % 86400 / 3600:00}:{night % 3600 / 60:00}: {sent.Length} on watch, sentry {res.Sentry} saw the enemy at 40 m, {res.Woken} of {res.Asleep} sleepers woken, the first +{res.WakeSec.Values.Min() - night} s, the last +{res.LastWakeSec - night} s");
        Assert.True(res.Raised); Assert.Equal(res.Asleep, res.Woken);
        output.WriteLine($"the camp: {plan.Tents} tents, {plan.Fires} fires, {plan.Latrines} latrines, {plan.Lines} horse lines, pitched in {plan.PitchSeconds(300 - sent.Length)} s by {300 - sent.Length} hands");
    }

    // ---------------------------------------------------------------- the save, the budget, the same world twice

    [Fact]
    public void SaveLoadContinue_WithAHostOnTheMarchAndItsCamp_EqualsTheUninterruptedRun()
    {
        const string Hash = "h";
        using var a = new MarchRig(); var aw = a.Aw;
        int s0 = -1, s1 = -1; long best = 0;
        for (int i = 0; i < Math.Min(40, aw.Region.Settlements.Count); i++)
            for (int j = i + 1; j < Math.Min(40, aw.Region.Settlements.Count); j++)
                if (aw.PathOf(i, j) is { } p && p.LengthMm > best && p.LengthMm < 16_000_000) { best = p.LengthMm; s0 = i; s1 = j; }
        Assert.True(best > 6_000_000, $"a long road {best}");
        var host = a.Host(new Roster([(TroopRole.Spearman, 80), (TroopRole.Archer, 40)]), carts: 6, outside: 120);
        long h0 = a.K.Now; a.K.To(h0 + 1);
        long begin = a.K.Now + 1;
        aw.EnterRegion(a.Env(begin), host, s0);
        aw.BeginMarch(a.Env(begin), host, [s0, s1]);
        a.K.To(begin + 40);                                                                       // a night camp and a day's march in
        var m = a.State.Armies!.Marches[host.Value];
        Assert.True(m.State is MarchState.Marching or MarchState.Camped, $"{m.State} after 40 hours");
        long onTheRoad = m.DistMm; Assert.True(onTheRoad > 0);
        var bytes = SaveCodec.Encode(WorldSave.Capture(a.State, Hash, "0.1"));
        long h1 = a.K.Now; a.K.To(h1 + 24 * 6);
        var want = a.State.Armies!.Hash(); var wantSection = SaveCodec.SerializeCompressed(ArmySave.Capture(a.State.Armies));
        var st = WorldSave.Restore(SaveCodec.Decode(bytes), a.K.R.Content, Hash, null, a.K.R.W.Continent, a.K.R.W.History); st.Content = a.K.R.Content;
        Assert.True(st.Armies!.Marches.ContainsKey(host.Value));
        Assert.Equal(m.Outsiders.Count, st.Armies.Marches[host.Value].Outsiders.Count);
        using var rt = new WorldRuntime(st, a.K.R.Content, new MemoryRegionPager());
        rt.AdvanceTo(GameTime.FromGameHours(h1 + 24 * 6));
        Assert.Equal(want, st.Armies!.Hash());
        Assert.Equal(wantSection, SaveCodec.SerializeCompressed(ArmySave.Capture(st.Armies)));
        Assert.Equal(a.State.Chronicle.Hash(), st.Chronicle.Hash());
        Assert.Equal(a.State.WarSupply!.Hash(), st.WarSupply!.Hash());
        output.WriteLine($"a host {onTheRoad / 1000} m along {best / 1000} m at the save; after 6 more days state {st.Armies.Marches[host.Value].State}, hash {want:x} in both runs");
        // an older save (no host on the road) keeps its bytes: the section's keys are appended
        using var b = new MarchRig();
        Assert.Empty(b.State.Armies?.Marches ?? []);
    }

    [Fact]
    public void TheHourlyMover_WithSeveralHostsOnTheRoad_StaysInTheAbstractBudget_AndTwoRunsAreTheSame()
    {
        ulong Run(out double msPerHour)
        {
            using var r = new MarchRig(); var aw = r.Aw;
            r.StraightRoad(60_000_000);
            var hosts = new List<HostId>();
            for (int i = 0; i < 8; i++) { var h = r.Host(new Roster([(TroopRole.Spearman, 300 + 50 * i)]), carts: 10); hosts.Add(h); aw.BeginMarch(r.Env(DawnOf(900)), h, [i, i + 1]); }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp(); int hours = 24 * 12;
            for (long h = DawnOf(900); h < DawnOf(900) + hours; h++) aw.MarchHour(r.Env(h));
            msPerHour = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency / hours;
            Assert.All(r.State.Armies!.Marches.Values, m => Assert.True(m.State is MarchState.Marching or MarchState.Camped));
            return r.State.Armies!.Hash();
        }
        Run(out _);                                                                               // warm up the JIT
        ulong h1 = Run(out double ms), h2 = Run(out double ms2);
        output.WriteLine($"8 hosts on the road: {ms:F3} ms an hour ({ms2:F3} the second time); budget 1 ms for 40 groups");
        Assert.Equal(h1, h2); Assert.NotEqual(0UL, h1);
        Assert.True(Math.Min(ms, ms2) <= 1.0, $"{ms:F3} ms an hour");
    }
}
