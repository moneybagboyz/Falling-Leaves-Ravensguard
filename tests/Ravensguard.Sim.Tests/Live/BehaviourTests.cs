using Ravensguard.Sim.Behaviour;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.World;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Content;
using Ravensguard.Sim.WorldGen.Chunks;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Live;

public class BehaviourTests(ITestOutputHelper output)
{
    private static LiveFacts Facts() => new(ChunkGenerator.For(EconFixture.Pop.Region, EconFixture.Pop, EconFixture.Content));

    [Fact]
    public void L7_ResidentsKeepTheirSchedulesAcrossFloors_BedUpstairs_WorkOnTheGround_ThreeDays()
    {
        var rig = TownRig.Get; var pop = rig.Pop; var g = rig.Ground;
        var cands = new List<PromotionCandidate>();
        for (int i = 0; i < pop.People.Count && cands.Count < 100; i++)
        {
            if (pop.People.Status[i] != PersonStatus.Alive || pop.People.HomeSettlement[i] != rig.TownIndex || pop.People.Occupation[i] is Occupation.Guard) continue;
            var a = rig.Anchors(i);
            if (!a.HasBed || a.BedZ != 1 || !a.HasWork || a.WorkInField || a.WorkLot < 0 || a.WorkSettlement != rig.TownIndex) continue;
            if (!rig.Inside(a.HomeX, a.HomeY, 3) || !rig.Inside(a.WorkX, a.WorkY, 3) || !rig.Inside(a.BedX, a.BedY, 1)) continue;
            cands.Add(new PromotionCandidate(a, 0, 0, 0, 0));
        }
        output.WriteLine($"residents with a bed upstairs and a workplace in the window: {cands.Count}");
        Assert.True(cands.Count >= 50, "enough upstairs sleepers inside the loaded window");
        var b = new PeopleBridge(); long t0 = rig.Day(1, 0);
        b.Enqueue(new ScanResult(t0, cands, [])); while (b.Queued > 0) b.Promote(t0, g);
        int atBed = 0, atWork = 0, stuck = 0, climbed = 0, wallsCrossed = 0;
        var inBed = new HashSet<(long, int)>(); var inWork = new HashSet<(long, int)>();
        long end = t0 + 3 * 86400;
        for (long t = t0 + 10; t < end; t += 20)
        {
            b.Step(t, g, rig.Centre);
            foreach (var (id, idx) in cands.Select((c, k) => (c.Anchors.Id, k)))
            {
                var act = b.Actor(id)!; var r = Schedule.Resolve(act.A, t); var seg = r.Seg; long segStart = r.Day * 86400 + seg.Start;
                if (seg.Kind == JobKind.Sleep && seg.To == PlaceRef.Bed && t >= segStart + 5)
                {
                    bool ok = PathPlan.Dist(act.X - act.A.BedX, act.Y - act.A.BedY) <= 700 && act.Z == act.A.BedZ;
                    if (ok) { atBed++; inBed.Add((r.Day, idx)); } else stuck++;
                }
                // (a stall keeper works on the square, not at the workplace)
                if (seg.Kind == JobKind.Work && seg.To == PlaceRef.Work && t >= segStart + 300)
                {
                    var lot = pop.Region.Settlements[act.A.WorkSettlement].Plan.Lots[act.A.WorkLot].Rect;
                    bool ok = act.Z == 0 && act.X >= (lot.X - 1) * 1000L && act.X <= (lot.Right + 1) * 1000L && act.Y >= (lot.Y - 1) * 1000L && act.Y <= (lot.Bottom + 1) * 1000L;
                    if (ok) { atWork++; inWork.Add((r.Day, idx)); } else stuck++;
                }
                if (act.Walking && act.Z == 1) climbed++;
                if (act.Walking && !g.IsOpen((int)(act.X / 1000), (int)(act.Y / 1000), act.Z)) wallsCrossed++;
            }
        }
        output.WriteLine($"sleeping in bed {atBed}, working at the workplace {atWork}, off-schedule {stuck}, samples walking on the upper floor {climbed}, samples walking through a wall {wallsCrossed}");
        Assert.Equal(0, stuck);
        Assert.True(climbed > 0, "someone must have walked on the upper floor");
        // every resident slept in the bed cell on each of the nights that fall in the three days, and worked at the workplace on the days they work
        for (int k = 0; k < cands.Count; k++) { Assert.Contains((rig.Pop.NowDay + 1L, k), inBed); Assert.Contains((rig.Pop.NowDay + 2L, k), inBed); }
        Assert.True(wallsCrossed <= atBed / 100 + 20, $"people walked through walls {wallsCrossed} times");
    }

    // ------------------------------------------------------------------ L-1 to L-3: the day follows the sun

    private static (int Wake, int WorkFrom, int WorkTo, int SleepFrom, int Rise, int Set) OutdoorDay(int lat, int doy)
    {
        var plan = BehaviourContent.Default.Plan("outdoor_worker")!; var (sr, ss) = SunCache.Of(lat, doy);
        var env = new AnchorEnv(plan.Anchors, sr, ss);
        return (env.Get("wake"), env.Get("work_from"), env.Get("work_to"), ((env.Get("sleep_from") % 1440) + 1440) % 1440, sr, ss);
    }

    private static string Hm(int m) => $"{m / 60:00}:{m % 60:00}";

    [Fact]
    public void L1_Midsummer_LatitudeFortyEight_OutdoorWorkerWakesAt0229_WorksElevenHours_SleepsFrom1759()
    {
        var d = OutdoorDay(480, 23);
        Assert.Equal("03:59", Hm(d.Rise)); Assert.Equal("20:01", Hm(d.Set));
        Assert.Equal(("02:29", "03:29", "14:29", "17:59"), (Hm(d.Wake), Hm(d.WorkFrom), Hm(d.WorkTo), Hm(d.SleepFrom)));
    }

    [Fact]
    public void L2_Midwinter_LatitudeFortyEight_WorkRunsFromSunriseLessHalfAnHour_SleepGetsAWinterHour()
    {
        var d = OutdoorDay(480, 53);
        Assert.Equal("08:02", Hm(d.Rise)); Assert.Equal("15:58", Hm(d.Set));
        Assert.Equal(("06:32", "07:32", "16:28", "21:02"), (Hm(d.Wake), Hm(d.WorkFrom), Hm(d.WorkTo), Hm(d.SleepFrom)));
        Assert.Equal(8 * 60 + 56, d.WorkTo - d.WorkFrom);
    }

    [Fact]
    public void L3_Equinox_SixToSix_ElevenHourCapBindsAndSleepStartsAtEight()
    {
        var d = OutdoorDay(480, 8);
        Assert.Equal(("06:00", "18:00"), (Hm(d.Rise), Hm(d.Set)));
        Assert.Equal(("04:30", "05:30", "16:30", "20:00"), (Hm(d.Wake), Hm(d.WorkFrom), Hm(d.WorkTo), Hm(d.SleepFrom)));
    }

    [Fact]
    public void AnchorGrammar_ParsesSunTimesMinMaxAndNames_AndRejectsNonsense()
    {
        var env = new AnchorEnv(new Dictionary<string, string> { ["a"] = "sunrise-1:30", ["b"] = "min(sunset+0:30, a+11:00)", ["c"] = "max(06:00, sunrise)" }, 240, 1200);
        Assert.Equal(150, env.Get("a")); Assert.Equal(810, env.Get("b")); Assert.Equal(360, env.Get("c"));
        Assert.Equal(1440, env.Eval("24:00")); Assert.Equal(60, new AnchorEnv(new Dictionary<string, string>(), 500, 1000).Eval("winter"));
        Assert.Throws<FormatException>(() => env.Eval("sunrise +"));
        Assert.Throws<FormatException>(() => env.Eval("6:5"));
        Assert.Throws<KeyNotFoundException>(() => env.Eval("noonish"));
        var loop = new AnchorEnv(new Dictionary<string, string> { ["x"] = "y+1:00", ["y"] = "x" }, 0, 0);
        Assert.Throws<InvalidOperationException>(() => loop.Get("x"));
    }

    [Fact]
    public void TheShippedBehaviourContent_LoadsFromTheContentDatabase_AndMatchesTheEmbeddedDefault()
    {
        var c = BehaviourContent.From(EconFixture.Content);
        Assert.Equal(13, c.Utility.HungerEatHours);
        Assert.Equal(BehaviourContent.Default.Schedules.Select(x => x.Id), c.Schedules.Select(x => x.Id));
        Assert.Equal(1100, c.OddJobs.PayMultiplePm);
        foreach (var plan in c.Schedules) _ = new PlanRt(plan);      // every kind and place parses
        Assert.NotNull(c.PlanFor("Farmer")); Assert.NotNull(c.PlanFor("Smith")); Assert.NotNull(c.PlanFor("Merchant"));
    }

    // ------------------------------------------------------------------ L-4, L-5: the scorer

    private static ChoiceContext WorkBlock(int minutesInWork = 999, ActionKind current = ActionKind.Work) => new()
    {
        Scheduled = ActionKind.Work, Current = current, MinutesInCurrent = minutesInWork, HasBed = true, CanEat = true, WorkOpen = true, HasRoof = true, Seed = 1, GameSec = 0, Person = 7,
    };

    [Fact]
    public void L4_HungerAgainstADutyBlock_TwelveHoursKeepsWork_ThirteenTiesToEat_CommittedWorkHoldsUntilAboutFourteenAndAHalf()
    {
        var quiet = BehaviourContent.Default.Utility with { NoisePm = 0 };
        var brain = new UtilityBrain(quiet);
        NeedInputs At(double hours) => new() { HungerMilliHours = (int)(hours * 1000), DutyPm = 500 };
        var fresh = WorkBlock(current: ActionKind.Idle);      // not committed to Work: 900
        Assert.Equal(ActionKind.Work, brain.Choose(At(12), fresh).Kind);
        var thirteen = brain.Choose(At(13), fresh);
        Assert.Equal(ActionKind.Eat, thirteen.Kind); Assert.Equal(900, thirteen.Score); Assert.Equal(900, thirteen.RunnerUpScore);
        var committed = WorkBlock(minutesInWork: 10);          // 900 + 150 = 1,050
        Assert.Equal(ActionKind.Work, brain.Choose(At(14.0), committed).Kind);
        Assert.Equal(ActionKind.Work, brain.Choose(At(14.25), committed).Kind);
        Assert.Equal(ActionKind.Eat, brain.Choose(At(14.5), committed).Kind);
        Assert.Equal(13, brain.HungerEatHours);
    }

    [Fact]
    public void L5_Hysteresis_AStartedWorkIsKeptAgainstASlightlyBetterAlternative_UntilThirtyMinutes()
    {
        // a profile in which the social need can reach 1,000: Work started ten minutes ago scores 1,050 and stays; at 31 minutes it is 900 and Socialise wins
        var p = BehaviourContent.Default.Utility with { NoisePm = 0, Needs = [.. BehaviourContent.Default.Utility.Needs.Select(n => n.Id == "social" ? n with { CurvePm = [[0, 0], [1000, 1000]] } : n)] };
        var brain = new UtilityBrain(p);
        var n = new NeedInputs { SocialPm = 1000, DutyPm = 500 };
        var ctx = WorkBlock(minutesInWork: 10); ctx.CanSocialise = true;
        Assert.Equal(ActionKind.Work, brain.Choose(n, ctx).Kind);
        ctx.MinutesInCurrent = 31;
        var later = brain.Choose(n, ctx);
        Assert.Equal(ActionKind.Socialise, later.Kind); Assert.Equal(1000, later.Score);
        Assert.Equal(900, later.RunnerUpScore);
    }

    [Fact]
    public void TheScorer_IsPure_ExhaustionOverridesWork_AndAThreatOverridesEverything_NoiseIsKeyedOnTheWindowNotTheTick()
    {
        var brain = UtilityBrain.Default;
        var n = new NeedInputs { FatiguePm = 900, DutyPm = 500 }; var c = WorkBlock(); c.Dark = true;
        Assert.Equal(ActionKind.Sleep, brain.Choose(n, c).Kind);
        n = new NeedInputs { FatiguePm = 900, DutyPm = 500, ThreatPm = 800 }; c.CanFlee = true;
        Assert.Equal(ActionKind.Flee, brain.Choose(n, c).Kind);
        // two game seconds in the same 20-second window draw the same noise; the whole answer is the same
        var a = WorkBlock(); a.GameSec = 40; var b = WorkBlock(); b.GameSec = 59;
        var nn = new NeedInputs { HungerMilliHours = 12900, DutyPm = 500 };
        Assert.Equal(brain.Choose(nn, a), brain.Choose(nn, b));
        Assert.Contains("hungry", brain.Explain(nn, a));
    }

    // ------------------------------------------------------------------ L-6: beds

    [Fact]
    public void L6_BedCells_AreRealFixturesOfTheDwelling_InAscendingZYXOrder_Stable_AndFloorSleepersLieOnTheSleepingLevel()
    {
        var pop = EconFixture.Pop; var facts = Facts(); var again = new LiveFacts(ChunkGenerator.For(pop.Region, pop, EconFixture.Content));
        int checkedLots = 0, withBunkOrPallet = 0, floorSleepers = 0;
        var pieces = facts.Generator.Library.Pieces; var pal = facts.Generator.Palette;
        for (int s = 0; s < pop.Region.Settlements.Count && checkedLots < 150; s++)
            for (int l = 0; l < pop.Region.Settlements[s].Plan.Lots.Count && checkedLots < 150; l += 2)
            {
                var beds = facts.BedsOf(s, l); if (beds.Length == 0) continue;
                checkedLots++;
                Assert.Equal(beds, again.BedsOf(s, l));
                for (int i = 1; i < beds.Length; i++) Assert.True((beds[i - 1].Z, beds[i - 1].Y, beds[i - 1].X).CompareTo((beds[i].Z, beds[i].Y, beds[i].X)) <= 0, "ascending (z, y, x)");
                var st = facts.Generator.StampOf(s, l)!;
                foreach (var b in beds)
                {
                    var fx = pal.Id(st.Levels[b.Z].Fixture[(b.Y - st.Y0) * st.W + (b.X - st.X0)]);
                    Assert.True(fx != null && pieces.TryGet(fx, out var def) && def.Tags.Contains("bed"), $"a bed fixture at {b}");
                }
                if (beds.Select(b => (b.X, b.Y, b.Z)).Distinct().Count() < beds.Length) withBunkOrPallet++;
                // a person beyond the last bed lies on the floor of the sleeping level, the same cell on every run
                Assert.True(facts.TryBed(s, l, beds.Length + 2, 4242, 99, out var f1)); Assert.True(again.TryBed(s, l, beds.Length + 2, 4242, 99, out var f2));
                Assert.Equal(f1, f2); Assert.Equal(beds[0].Z, f1.Z); floorSleepers++;
                Assert.True(facts.TryBed(s, l, 0, 1, 99, out var own)); Assert.Equal(beds[0], own);
            }
        Assert.True(checkedLots > 100 && floorSleepers > 100, $"lots checked {checkedLots}");
        output.WriteLine($"lots {checkedLots}, with a double bed {withBunkOrPallet}");
    }

    [Fact]
    public void L6_ADwellingWithABedABunkAndAPallet_GivesFourCells_BunkCountedTwice_LowerFirst_ThePalletLast()
    {
        var gen = ChunkGenerator.For(EconFixture.Pop.Region, EconFixture.Pop, EconFixture.Content); var pal = gen.Palette; var pieces = gen.Library.Pieces;
        Assert.True(pieces.TryGet("bunk", out var bunk)); Assert.Equal(2, bunk.Beds);
        var st = new ChunkGenerator.Stamp { X0 = 100, Y0 = 200, W = 4, H = 3 };
        var ground = new int[12]; var up = new int[12];
        ground[1 * 4 + 3] = pal.Of("straw_pallet");     // (3,1) on the ground floor: the pallet
        up[0 * 4 + 1] = pal.Of("bed");                  // (1,0) upstairs
        up[2 * 4 + 2] = pal.Of("bunk");                 // (2,2) upstairs: two beds
        st.Levels[0] = (new int[12], ground); st.Levels[1] = (new int[12], up);
        var beds = LiveFacts.BedsOfStamp(st, pieces, pal);
        Assert.Equal([new BedCell(103, 201, 0), new BedCell(101, 200, 1), new BedCell(102, 202, 1), new BedCell(102, 202, 1)], beds);
        Assert.Equal(beds, LiveFacts.BedsOfStamp(st, pieces, pal));
    }

    // ------------------------------------------------------------------ the crowd, holy days, the watch

    private static IEnumerable<PersonAnchors> Residents(TownRig rig) { for (int i = 0; i < rig.Pop.People.Count; i++) if (rig.Pop.People.Status[i] == PersonStatus.Alive && rig.Pop.People.HomeSettlement[i] == rig.TownIndex) yield return rig.Anchors(i); }

    private static long DayWith(Func<DayInfo, bool> pick, TownRig rig, int hour, int minute = 0)
    {
        for (long d = rig.Pop.NowDay + 1; d < rig.Pop.NowDay + 400; d++) if (pick(DayInfo.Of(d, rig.Anchors(0).LatTenths, BehaviourContent.Default.Crowd))) return d * 86400 + hour * 3600 + minute * 60;
        throw new InvalidOperationException("no such day");
    }

    [Fact]
    public void AtFiveInTheEvening_MostPeopleAreHomeOrOnTheirWayHome_TheMarketDrawsSome_AMarketDayMore_AFairMost()
    {
        var rig = TownRig.Get; var people = Residents(rig).ToList(); var sq = rig.Town.Plan.Square;
        double Share(long t, out double workShare)
        {
            int on = 0, work = 0;
            foreach (var a in people) { var r = Schedule.Resolve(a, t); if (r.Job.Kind == JobKind.Shop) on++; if (r.Job.Kind == JobKind.Work) work++; }
            workShare = work / (double)people.Count; return on / (double)people.Count;
        }
        double normal = Share(DayWith(d => !d.Market && !d.Fair && !d.Holy, rig, 17, 15), out var nw), market = Share(DayWith(d => d.Market && !d.Fair, rig, 17, 15), out _), fair = Share(DayWith(d => d.Fair, rig, 17, 15), out _);
        output.WriteLine($"on the square at 17:15 normal {normal:P1}, market day {market:P1}, fair {fair:P1}; still at work {nw:P1}");
        Assert.InRange(normal, 0.01, 0.10); Assert.True(market > normal * 1.8, "a market day draws more"); Assert.True(fair > market * 1.5, "a fair draws more still"); Assert.True(fair < 0.45, "even a fair is not everybody");
        Assert.True(nw < 0.2, "by five most have stopped work");
    }

    [Fact]
    public void ShoppersQueueOnTheCustomerSideOfRealStalls_AndKeepersStandBehindThem()
    {
        var rig = TownRig.Get; var stalls = rig.Facts.StallsOf(rig.TownIndex); Assert.NotEmpty(stalls);
        long t = DayWith(d => d.Market, rig, 17, 15); int shoppers = 0, nearStall = 0, keepers = 0, behind = 0;
        foreach (var a in Residents(rig))
        {
            var r = Schedule.Resolve(a, t);
            if (r.Job.Kind == JobKind.Shop)
            {
                shoppers++;
                if (stalls.Any(s => PathPlan.Dist(r.XMm - (s.X + 1) * 1000L, r.YMm - s.Y * 1000L) < 7000)) nearStall++;
            }
            var noon = Schedule.Resolve(a, t - 5 * 3600);
            if (noon.Seg.To == PlaceRef.Stall && noon.Job.Kind == JobKind.Work)
            {
                keepers++;
                if (stalls.Any(s => PathPlan.Dist(noon.XMm - ((s.X + 1) * 1000L + 500 - s.FaceDx * 1000), noon.YMm - (s.Y * 1000L + 500 - s.FaceDy * 1000)) < 50)) behind++;
            }
        }
        output.WriteLine($"shoppers {shoppers} (near a stall {nearStall}), keepers {keepers} (behind a stall {behind})");
        Assert.True(shoppers > 20 && nearStall == shoppers); Assert.True(keepers > 3 && behind == keepers);
    }

    [Fact]
    public void OnAHolyDay_NearlyEveryAwakeAdultIsAtChurchBetweenNineAndEleven_TheFewOnTheRoadComeFromFarFields_AndOnOtherDaysNobodyIs()
    {
        var rig = TownRig.Get; var people = Residents(rig).Where(a => !a.Child && a.Occupation != Occupation.Guard).ToList();   // the watch stays at its post
        long holy = DayWith(d => d.Holy, rig, 10), plain = DayWith(d => !d.Holy, rig, 10);
        int at = 0, awake = 0, atPlain = 0;
        foreach (var a in people)
        {
            if (a.HasWork) { var h = a.Spot(PlaceRef.Home, 0); var w = a.Spot(PlaceRef.Work, holy / 86400); if (PathPlan.Dist(w.X - h.X, w.Y - h.Y) > ScheduleTuning.MaxCommuteMm) continue; }   // a far field camp is not within reach of the church
            var r = Schedule.Resolve(a, holy); if (r.Job.Kind != JobKind.Sleep) { awake++; if (r.Job.Kind == JobKind.Church) at++; }
            if (Schedule.Resolve(a, plain).Job.Kind == JobKind.Church) atPlain++;
        }
        var miss = people.Where(a => { if (a.HasWork) { var h = a.Spot(PlaceRef.Home, 0); var w = a.Spot(PlaceRef.Work, holy / 86400); if (PathPlan.Dist(w.X - h.X, w.Y - h.Y) > ScheduleTuning.MaxCommuteMm) return false; } var r = Schedule.Resolve(a, holy); return r.Job.Kind != JobKind.Sleep && r.Job.Kind != JobKind.Church; }).GroupBy(a => (a.Occupation, Schedule.Resolve(a, holy).Job.Kind)).OrderByDescending(g => g.Count()).Take(5).Select(g => $"{g.Key.Occupation}/{g.Key.Item2} {g.Count()}");
        Assert.True(at >= awake * 0.94, $"church {at} of {awake} awake; missing: {string.Join(", ", miss)}"); Assert.Equal(0, atPlain);
    }

    [Fact]
    public void TheWatchKeepsThreeShifts_ThePostIsNeverEmpty_AndTheNightShiftSleepsByDay()
    {
        var rig = TownRig.Get; var guards = Residents(rig).Where(a => a.Occupation == Occupation.Guard && a.HasWork).ToList();
        if (guards.Count < 3) { output.WriteLine("too few guards in the town for the shift check"); return; }
        long d = rig.Pop.NowDay + 1; int shiftsSeen = 0; var seen = new HashSet<int>();
        foreach (var g in guards) seen.Add(g.Index % 3);
        shiftsSeen = seen.Count; Assert.Equal(3, shiftsSeen);
        foreach (int hour in new[] { 3, 10, 18, 22 }) Assert.True(guards.Count(g => Schedule.Resolve(g, d * 86400 + hour * 3600).Job.Kind == JobKind.Work) >= 1, $"someone keeps the watch at {hour}:00");
        var night = guards.First(g => g.Index % 3 == 0);
        Assert.Equal(JobKind.Work, Schedule.Resolve(night, d * 86400 + 3 * 3600).Job.Kind); Assert.Equal(JobKind.Sleep, Schedule.Resolve(night, d * 86400 + 14 * 3600).Job.Kind);
    }

    [Fact]
    public void ANightInTown_ThePeopleAreAsleepInTheirBeds_AndADayShiftWorks()
    {
        var rig = TownRig.Get; var people = Residents(rig).ToList(); long d = rig.Pop.NowDay + 1;
        int asleep = people.Count(a => Schedule.Resolve(a, d * 86400 + 23 * 3600 + 30 * 60).Job.Kind == JobKind.Sleep);
        int atWork = people.Count(a => Schedule.Resolve(a, d * 86400 + 11 * 3600).Job.Kind == JobKind.Work);
        output.WriteLine($"asleep at 23:30 {asleep}/{people.Count}, at work at 11:00 {atWork}");
        Assert.True(asleep > people.Count * 0.85); Assert.True(atWork > people.Count * 0.3);
    }

}

/// <summary>A town's ground with real buildings: the generator's chunks around the square as one level grid with stairs, the way the street view loads them.</summary>
public sealed class TownRig
{
    public Population Pop { get; } = EconFixture.Pop;
    public ChunkGenerator Gen { get; }
    public LiveFacts Facts { get; }
    public RegionSettlement Town { get; }
    public int TownIndex { get; }
    public WorldWindow Window { get; }
    public GridGround Ground { get; }
    public long CentreXMm => Town.Plan.Square.CenterX * 1000L;
    public long CentreYMm => Town.Plan.Square.CenterY * 1000L;
    public (long X, long Y)[] Centre => [(CentreXMm, CentreYMm)];

    private static readonly Lazy<TownRig> Shared = new(() => new TownRig());
    public static TownRig Get => Shared.Value;

    private TownRig()
    {
        Gen = ChunkGenerator.For(Pop.Region, Pop, EconFixture.Content);
        Facts = new LiveFacts(Gen);
        Town = Pop.Region.Towns.OrderByDescending(t => t.TargetPop).First();
        TownIndex = Pop.Region.Settlements.IndexOf(Town);
        var streamer = new ChunkStreamer(Gen);
        int cx = Town.Plan.Square.CenterX / 32, cy = Town.Plan.Square.CenterY / 32;
        streamer.LoadAround(cx, cy, 4);
        Window = new WorldWindow(streamer, cx, cy);
        Ground = new GridGround(Window.Grid, new LevelPathfinder(Window.Grid), Window.OriginX, Window.OriginY);
    }

    public long Day(int offset, int hour, int minute = 0, int second = 0) => (long)(Pop.NowDay + offset) * 86400 + hour * 3600 + minute * 60 + second;

    public PersonAnchors Anchors(int i, long now = 0) => PersonAnchors.From(Pop, i, EconFixture.Seed, 0, now == 0 ? Day(1, 0) : now, null, Facts);

    public bool Inside(long xMm, long yMm, int pad = 0) => Ground.Covers((int)(xMm / 1000) - pad, (int)(yMm / 1000) - pad) && Ground.Covers((int)(xMm / 1000) + pad, (int)(yMm / 1000) + pad);
}
