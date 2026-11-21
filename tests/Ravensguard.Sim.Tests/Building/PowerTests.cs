using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Construction;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Building;

/// <summary>P7-LB1 golden tests (large-buildings.md 9): LB-1 to LB-7 and LB-13 to LB-15, with the sluice's save and the content's own checks.</summary>
public class PowerTests(ITestOutputHelper output)
{
    private static ContentDatabase Db => EconFixture.Content;
    private static PowerCatalog Cat => PowerCatalog.Of(Db);
    private static WaterPowerRulesDef Rules => Cat.Rules;
    private static PowerPieceDef Def(string piece) => Cat.Get(piece) ?? throw new InvalidOperationException(piece);

    // ---------------------------------------------------------------- the content

    [Fact]
    public void Content_EveryPowerPieceNamesABuildPiece_WithAKnownRole_AndTheRulesAreTheSpecs()
    {
        var pieces = Db.All<BuildPieceDef>().Select(p => p.Id).ToHashSet();
        var roles = new[] { "wheel", "windmill", "gin", "shaft", "machine", "switch", "weir", "leat", "crane" };
        foreach (var d in Db.All<PowerPieceDef>())
        {
            Assert.Contains(d.Piece, pieces);
            Assert.Contains(d.Role, roles);
            if (d.Role == "wheel") { Assert.True(d.EtaPm > 0 && d.RatedW > 0, d.Id); }
            if (d.Role == "machine") Assert.InRange(d.LabourPm, 1, 1000);
        }
        Assert.Equal(400, Def("waterwheel").EtaPm); Assert.Equal(200, Def("undershot_wheel").EtaPm); Assert.Equal(600, Def("overshot_wheel").EtaPm);
        Assert.Equal(3, Cat.Wheels.Count);
        Assert.Equal([1600, 600, 1000, 800], Rules.SeasonPm);
        Assert.Equal(15, Rules.FlowLpsPerKm2);
        foreach (var id in new[] { "undershot_wheel", "overshot_wheel", "weir", "leat", "sluice", "line_shaft", "cam_shaft", "trip_hammer", "great_bellows", "fulling_stocks", "gang_saw_frame", "frame_saw", "stamp_battery", "pug_mill",
                     "hoist_drum", "rag_pump", "horse_gin", "treadwheel_crane", "windmill_post", "rope_wheel", "rope_traveller", "armour_stand", "arms_shelf", "arms_chest", "granary_bin", "staddle_floor", "warehouse_bay" })
            Assert.Contains(id, pieces);
    }

    // ---------------------------------------------------------------- LB-1

    [Fact]
    public void LB1_TheWorkedExample_Is2354WInAutumnAnd1412WInSummer()
    {
        var wheel = Def("waterwheel");
        int qAutumn = WaterPower.FlowLps(Rules, 200, Season.Autumn), qSummer = WaterPower.FlowLps(Rules, 200, Season.Summer);
        Assert.Equal(300, qAutumn); Assert.Equal(180, qSummer);
        Assert.Equal(5886, WaterPower.WaterWatts(qAutumn, 2000));
        var a = WaterPower.Output(wheel, qAutumn, 2000, river: false, leat: true);
        var s = WaterPower.Output(wheel, qSummer, 2000, river: false, leat: true);
        Assert.True(a.Works); Assert.Equal(2354, a.Watts);
        Assert.Equal(1412, s.Watts);
        Assert.Equal(4000, WaterPower.Output(wheel, 600, 2500, false, true).Watts);          // rated caps it
        Assert.Equal(300, WaterPower.Flow(Rules, 100, Season.Autumn, false, true));   // river-high: flow x 2
        Assert.Equal(0, WaterPower.Flow(Rules, 200, Season.Autumn, frozen: true, riverHigh: true));
    }

    // ---------------------------------------------------------------- LB-2 / LB-13 (ice, wear, floods) on a world

    /// <summary>A stream 1,280 m long running east along y = 5,000 with the tile heights falling <paramref name="dCm"/> cm every 64 m (vertices at the tile centres: the fall is exact).</summary>
    private static WaterSites StreamSites(int dCm, int catchTenths = 200, int widthCm = 400, WaterPowerRulesDef? rules = null, WaterKind kind = WaterKind.Stream)
    {
        var r = new Region { Seed = 1 };
        int ty = 5000 / Region.TileM;
        for (int tx = 0; tx < Region.Size; tx++) r.HeightCm[ty * Region.Size + tx] = Math.Max(0, 30000 - (tx - 60) * dCm);
        var pts = new List<PointM>(); for (int k = 60; k <= 80; k++) pts.Add(new PointM(k * 64 + 32, 5000));
        var w = new Waterway { Id = 0, Kind = kind, Points = [.. pts], WidthCm = [.. pts.Select(_ => widthCm)], DepthCm = [.. pts.Select(_ => 60)], CatchmentTenthsKm2 = catchTenths, LengthM = 20 * 64 };
        r.Waterways.Add(w);
        return new WaterSites(r, rules ?? Rules);
    }

    private const int WheelX = 5150, WheelY = 5003;        // 2 m of the bank, near the downstream end: catchment about 200 tenths

    private static void Put(ConstructionRig rig, int x, int y, string piece, int z = 0)
    {
        var c = rig.W.Costs.Get(piece)!;
        var b = new BuiltCell { X = x, Y = y, Z = z, Slot = GhostSlot.Fixture, Piece = piece, Hp = PieceCosts.BuiltHp(c, 50), BuiltSkill = 50 };
        rig.W.Book.Built[b.SlotKey] = b;
    }
    private static BuiltCell Cell(ConstructionRig rig, int x, int y, int z = 0) => rig.W.Book.Built[ConstructionIds.SlotKey(x, y, z, GhostSlot.Fixture)];
    private const long AutumnDay = 35;

    private static (ConstructionRig Rig, PowerWorld Pw) World(WaterPowerRulesDef? rules = null, int dCm = 427, bool sluice = false)
    {
        var rig = ConstructionRig.Make();
        var cat = new PowerCatalog(Db, rules);
        var pw = new PowerWorld(rig.W, cat, 1234) { Sites = StreamSites(dCm, rules: cat.Rules) };
        Put(rig, WheelX, WheelY, "waterwheel");
        for (int i = 1; i <= 2; i++) Put(rig, WheelX - i, WheelY, "cam_shaft");
        Put(rig, WheelX - 3, WheelY, "trip_hammer"); Put(rig, WheelX - 3, WheelY + 2, "anvil");
        if (sluice) Put(rig, WheelX, WheelY + 1, "sluice");
        return (rig, pw);
    }

    [Fact]
    public void LB2_Frozen_ThreeDaysBelowMinus5_StopsTheWheelWithNoDamage_AndItReturnsTwoDaysAboveZero()
    {
        // -8 C on days 20 to 23, +1 on 24 and 25, then -8 again
        int T(long d) => d is >= 20 and <= 23 ? -8 : d is 24 or 25 ? 1 : d >= 26 ? 3 : 10;
        Assert.False(WaterPower.Frozen(Rules, 21, T));          // two cold days: not yet
        Assert.True(WaterPower.Frozen(Rules, 22, T));           // the third
        Assert.True(WaterPower.Frozen(Rules, 24, T));           // one warm day: still frozen
        Assert.False(WaterPower.Frozen(Rules, 25, T));          // the second warm day thaws it
        Assert.Equal(0, WaterPower.Flow(Rules, 200, Season.Winter, true, false));

        var (rig, pw) = World();
        pw.MeanC = d => -8;
        int hp0 = Cell(rig, WheelX, WheelY).Hp;
        pw.Day(AutumnDay + 30 + 4);                             // a winter day after a run of cold ones
        Assert.Equal(0, pw.Nets[0].Watts); Assert.True(pw.Nets[0].Stopped); Assert.Equal("lb_frozen", pw.Nets[0].Why);
        Assert.Equal(hp0, Cell(rig, WheelX, WheelY).Hp);        // no wear, no damage
        pw.MeanC = d => 4;
        pw.Day(AutumnDay + 30 + 6);                             // thawed (two warm days)
        Assert.False(pw.Nets[0].Stopped); Assert.True(pw.Nets[0].Watts > 0);
    }

    // ---------------------------------------------------------------- LB-3

    [Fact]
    public void LB3_AnOvershotWheelAt1800mm_GivesNothing_AndTheGhostNamesTheBand()
    {
        var o = WaterPower.Output(Def("overshot_wheel"), 300, 1800, false, leat: true);
        Assert.False(o.Works); Assert.Equal(0, o.Watts); Assert.Equal("lb_head_low", o.Key); Assert.Equal([1800, 2500, 0], o.Args);
        Assert.Equal("lb_need_leat", WaterPower.Output(Def("overshot_wheel"), 300, 3000, false, leat: false).Key);
        Assert.True(WaterPower.Output(Def("overshot_wheel"), 300, 3000, false, leat: true).Works);
        Assert.Equal("lb_head_high", WaterPower.Output(Def("waterwheel"), 300, 2600, false, true).Key);
        Assert.Equal("lb_need_river", WaterPower.Output(Def("undershot_wheel"), 300, 400, river: false, leat: false).Key);
        Assert.Equal(WaterPower.WaterWatts(300, 400) * 200 / 1000, WaterPower.Output(Def("undershot_wheel"), 300, 400, true, false).Watts);
    }

    // ---------------------------------------------------------------- LB-4 / LB-15 (nets)

    private static PowerCell C(int x, int y, string piece, int z = 0, int hp = 500) => new(x, y, z, piece, hp, 500, ConstructionIds.SlotKey(x, y, z, GhostSlot.Fixture));

    [Fact]
    public void LB4_Sharing_2354W_ForATripHammerAndGreatBellows_IsEach785Permille_AndTheForgeLabourIs509()
    {
        var cells = new List<PowerCell> { C(10, 100, "waterwheel"), C(11, 100, "cam_shaft"), C(12, 100, "cam_shaft"), C(13, 100, "cam_shaft"),
            C(14, 100, "trip_hammer"), C(15, 100, "forge"), C(12, 101, "great_bellows"), C(12, 103, "bloomery") };
        var net = Assert.Single(PowerNets.Build(cells, Cat));
        Assert.Equal(2, net.Machines.Count);
        net.Apportion(2354, Rules);
        Assert.Equal(3000, net.DrawW); Assert.Equal(785, net.SharePm);
        var hammer = net.Machines.Single(m => m.Def.Piece == "trip_hammer");
        Assert.Equal(785, net.EffectPm(hammer, Rules));
        Assert.Equal(509, net.LabourPm(hammer, Rules));                 // 1,000 - 625 x 0.785
        net.Apportion(4000, Rules); Assert.Equal(1000, net.SharePm); Assert.Equal(375, net.LabourPm(hammer, Rules));
        net.Apportion(0, Rules); Assert.Equal(0, net.SharePm); Assert.Equal(1000, net.LabourPm(hammer, Rules));
    }

    [Fact]
    public void LB4b_AWornMachine_RunsAtHalfEffectBelowHalfItsHitPoints_AndStopsAtZero()
    {
        var cells = new List<PowerCell> { C(10, 100, "waterwheel"), C(11, 100, "cam_shaft"), C(12, 100, "trip_hammer", hp: 240), C(12, 102, "anvil") };
        var net = Assert.Single(PowerNets.Build(cells, Cat));
        net.Apportion(4000, Rules);
        Assert.Equal(500, net.EffectPm(net.Machines[0], Rules));
        cells[2] = C(12, 100, "trip_hammer", hp: 0);
        net = Assert.Single(PowerNets.Build(cells, Cat)); net.Apportion(4000, Rules);
        Assert.Equal(0, net.EffectPm(net.Machines[0], Rules)); Assert.Equal(0, net.DrawW);
    }

    [Fact]
    public void LB15_Wiring_AShaftBeyond12m_DoesNotReach_At10mItDoes_AndAStationIsCoupledWithin3m()
    {
        List<PowerCell> Row(int shafts, int anvilDy)
        {
            var l = new List<PowerCell> { C(10, 100, "waterwheel") };
            for (int i = 1; i <= shafts; i++) l.Add(C(10 + i, 100, "cam_shaft"));
            l.Add(C(11 + shafts, 100, "trip_hammer"));
            l.Add(C(11 + shafts, 100 + anvilDy, "anvil"));
            return l;
        }
        var far = Assert.Single(PowerNets.Build(Row(13, 2), Cat));
        Assert.Empty(far.Machines);                                         // 13 m of shaft: the hammer is beyond 12 m
        Assert.Equal(12, far.Shafts.Count);                                 // the run itself stops at 12
        var near = Assert.Single(PowerNets.Build(Row(10, 2), Cat));
        var m = Assert.Single(near.Machines); Assert.NotNull(m.Station); Assert.Equal("anvil", m.Station!.Value.Piece);
        Assert.Equal(10, m.ShaftM);
        var apart = Assert.Single(PowerNets.Build(Row(10, 4), Cat));        // the anvil 4 m from the hammer: powered, but nothing coupled
        Assert.Null(apart.Machines[0].Station); Assert.False(apart.Machines[0].Working);
        // a hammer needs a cam shaft; a plain line shaft does not drive it, but does drive a great bellows
        var line = new List<PowerCell> { C(10, 100, "waterwheel"), C(11, 100, "line_shaft"), C(12, 100, "trip_hammer"), C(11, 101, "great_bellows") };
        Assert.Equal(["great_bellows"], Assert.Single(PowerNets.Build(line, Cat)).Machines.Select(x => x.Def.Piece));
    }

    // ---------------------------------------------------------------- LB-5

    [Fact]
    public void LB5_Windmill_At6mps_Gives4000_At2mps_0_At16mps_0()
    {
        Assert.Equal(4000, WindPower.Watts(Rules, 4000, 6000));
        Assert.Equal(4104, WindPower.Watts(Rules, 10_000, 6000));          // 19 x 216, before the cap
        Assert.Equal(0, WindPower.Watts(Rules, 4000, 2000));
        Assert.Equal(0, WindPower.Watts(Rules, 4000, 16_000));
        Assert.Equal(19 * 27, WindPower.Watts(Rules, 4000, 3500));          // 3 m/s, the least that turns
        Assert.True(WindPower.OpenGround(Rules, 100, 100, (x, y) => false));
        Assert.False(WindPower.OpenGround(Rules, 100, 100, (x, y) => x == 80 && y == 104));
        Assert.True(WindPower.OpenGround(Rules, 100, 100, (x, y) => x == 60));         // beyond 30 m upwind
        // a windmill's net drives millstones and pumps only
        var cells = new List<PowerCell> { C(10, 100, "windmill_post"), C(11, 100, "line_shaft"), C(12, 100, "millstone"), C(11, 101, "pug_mill"), C(11, 99, "rag_pump") };
        Assert.Equal(["millstone", "rag_pump"], Assert.Single(PowerNets.Build(cells, Cat)).Machines.Select(m => m.Def.Piece).Order(StringComparer.Ordinal));
    }

    // ---------------------------------------------------------------- LB-6

    private static BuildingGrid PlanWithWheel(int leats = 0, bool weir = false)
    {
        var lv = new BuildingLevel(0, 3 + leats, 3);
        lv.Fixture[lv.Idx(1, 1)] = "waterwheel";
        for (int i = 0; i < leats; i++) lv.Fixture[lv.Idx(2 + i, 1)] = "leat";
        if (weir) lv.Fixture[lv.Idx(0, 0)] = "weir";
        return new BuildingGrid("t", lv.Width, 3, new Dictionary<char, string>(), [lv]);
    }

    [Fact]
    public void LB6_APoweredPlan_IsRefusedWhereAutumnGivesLessThanItNeeds_AndPlacedWhereItGivesMore()
    {
        var weak = StreamSites(214);                                       // about 1,000 mm of fall in 30 m: about 1,180 W in autumn
        var strong = StreamSites(427);                                     // about 2,000 mm: about 2,350 W
        var g = PlanWithWheel();
        int ox = WheelX - 1, oy = WheelY - 1;
        var (ok1, have1, key1) = PlanWater.Check(weak, Cat, g, ox, oy, 2000);
        Assert.False(ok1); Assert.Equal("cn_fit_power", key1); Assert.InRange(have1, 1100, 1250);
        var (ok2, have2, key2) = PlanWater.Check(strong, Cat, g, ox, oy, 2000);
        Assert.True(ok2); Assert.InRange(have2, 2200, 2500); Assert.Equal("", key2);
        output.WriteLine($"weak {have1} W, strong {have2} W");
        // a wheel off the bank
        Assert.Equal("cn_fit_no_bank", PlanWater.Check(strong, Cat, g, ox, oy + 60, 2000).Key);
        // no wheel
        var lv = new BuildingLevel(0, 2, 2); var bare = new BuildingGrid("b", 2, 2, new Dictionary<char, string>(), [lv]);
        Assert.Equal("cn_fit_no_wheel", PlanWater.Check(strong, Cat, bare, ox, oy, 2000).Key);
        // a plan that needs no power is never asked
        Assert.True(PlanWater.Check(weak, Cat, bare, 0, 0, 0).Ok);
        // the plan's own leat and weir count: the weak stream with a 120 m leat and a weir
        var withWorks = PlanWithWheel(leats: 120 > 20 ? 20 : 0, weir: true);
        var long1 = PlanWater.Check(weak, Cat, withWorks, ox, oy, 100);
        Assert.True(long1.HaveW > have1);

        // through the fitter: the same refusal as the ghost's text, with the watts and the need (a real plan, so a door and a ground floor exist)
        var lib = BuildingLibrary.FromDatabase(Db);
        var cottage = lib.Grid("test_cottage");
        var lot = new FitLot(1000, 1000, 40, 40);
        var no = PlanFitter.Fit(cottage, lot, lib.Pieces, null, new PlanPower(2000, (gr, x, y) => (false, 1180, "cn_fit_power")));
        Assert.False(no.Ok); Assert.Equal("cn_fit_power", no.Key); Assert.Equal([1180, 2000], no.Args);
        Assert.True(PlanFitter.Fit(cottage, lot, lib.Pieces, null, new PlanPower(2000, (gr, x, y) => (true, 2400, ""))).Ok);
        Assert.True(PlanFitter.Fit(cottage, lot, lib.Pieces, null, null).Ok);          // no check wired: as before
        Assert.True(PlanFitter.Fit(cottage, lot, lib.Pieces, null, new PlanPower(0, (gr, x, y) => (false, 0, "cn_fit_power"))).Ok);   // a plan that needs no power is never asked
    }

    // ---------------------------------------------------------------- LB-7

    [Fact]
    public void LB7_MillSites_OnSeed1234_AreListedBestFirst_200mApart_AndTheSameOnTwoRuns()
    {
        var region = EconFixture.Pop.Region;
        var a = new WaterSites(region, Rules).MillSites(Cat.Wheels);
        var b = new WaterSites(region, Rules).MillSites(Cat.Wheels);
        output.WriteLine($"{a.Count} sites; best {a.FirstOrDefault()}");
        Assert.NotEmpty(a);
        Assert.Equal(a, b);
        for (int i = 1; i < a.Count; i++) Assert.True(a[i - 1].AutumnW >= a[i].AutumnW);
        foreach (var s in a) Assert.True(s.AutumnW >= Rules.SiteMinW);
        for (int i = 0; i < a.Count; i++)
            for (int j = i + 1; j < a.Count; j++)
            {
                long dx = a[i].X - a[j].X, dy = a[i].Y - a[j].Y;
                Assert.True(dx * dx + dy * dy >= 200L * 200, $"sites {i} and {j} are {Math.Sqrt(dx * dx + dy * dy):F0} m apart");
            }
    }

    // ---------------------------------------------------------------- LB-13

    [Fact]
    public void LB13_ABreakdown_Draws0x730_AndCostsTwentyPercent_AFlood_Draws0x731_AndCostsThirty_ShutSluiceNone()
    {
        Assert.Equal(0x730u, RngStreams.MachineBreakdown); Assert.Equal(0x731u, RngStreams.FloodDamage);
        // a forced breakdown (the chance raised to certain): wear 1, then 20% of the piece's hit points
        var (rig, pw) = World(new WaterPowerRulesDef { Id = "t", BreakdownPm = 1000 });
        var wheel = Cell(rig, WheelX, WheelY); int max = PieceCosts.BuiltHp(rig.W.Costs.Get("waterwheel")!, 50);
        Assert.Equal(max, wheel.Hp);
        pw.Day(AutumnDay);
        output.WriteLine($"nets {pw.Nets.Count} watts {pw.Nets[0].Watts} why {pw.Nets[0].Why} stopped {pw.Nets[0].Stopped} water {pw.Nets[0].Water}");
        Assert.Equal(max - 1 - max * 20 / 100, wheel.Hp);
        var hammer = Cell(rig, WheelX - 3, WheelY); int hmax = PieceCosts.BuiltHp(rig.W.Costs.Get("trip_hammer")!, 50);
        Assert.Equal(hmax - 1 - hmax * 20 / 100, hammer.Hp);                 // the machine is a piece too

        // the odds: 2 permille a day, so 1,000 piece-days give a few, never most
        int hits = 0; for (int d = 0; d < 1000; d++) if (DetRng.Range(1234, d, 77, RngStreams.MachineBreakdown, 0, 0, 1000) < Rules.BreakdownPm) hits++;
        Assert.InRange(hits, 0, 12);

        // a flood on a river-high day with the sluice open: 5% a day, forced; -30% (and the day's wear)
        var flood = new WaterPowerRulesDef { Id = "t", FloodPct = 100, BreakdownPm = 0 };
        var (rig2, pw2) = World(flood, sluice: true);
        pw2.RiverHighDays = _ => 1;
        var w2 = Cell(rig2, WheelX, WheelY); int max2 = w2.Hp;
        pw2.Day(AutumnDay);
        Assert.Equal(max2 - 1 - max2 * 30 / 100, w2.Hp);
        // the sluice shut: the wheel is stopped, nothing breaks
        var (rig3, pw3) = World(flood, sluice: true);
        pw3.RiverHighDays = _ => 1;
        Assert.True(pw3.SetSluice(WheelX, WheelY + 1, 0, shut: true)); Assert.False(pw3.SetSluice(1, 1, 0, true));
        pw3.Day(AutumnDay);
        var w3 = Cell(rig3, WheelX, WheelY);
        Assert.Equal(max2, w3.Hp); Assert.True(pw3.Nets[0].Stopped); Assert.Equal("lb_sluice_shut", pw3.Nets[0].Why);
        Assert.Equal(PieceCosts.BuiltHp(rig3.W.Costs.Get("trip_hammer")!, 50), Cell(rig3, WheelX - 3, WheelY).Hp);
    }

    [Fact]
    public void LB13b_AFloodThatBringsAWheelToZero_WritesWheelWreckedFlood()
    {
        var (rig, pw) = World(new WaterPowerRulesDef { Id = "t", FloodPct = 100, BreakdownPm = 0, FloodLossPct = 100 });
        pw.RiverHighDays = _ => 2;
        var lines = new List<string>();
        pw.Chronicle = (when, type, key, ps, subjects, causes, data) => { lines.Add(type + ":" + key + ":" + causes[0].Kind); return EntityId.None; };
        pw.Day(AutumnDay);
        Assert.Equal(["wheel_wrecked_flood:ch_works_wheel_flood:Weather"], lines);
        Assert.Equal(0, Cell(rig, WheelX, WheelY).Hp);
        pw.Day(AutumnDay + 1); Assert.Single(lines);                         // a wrecked wheel gives nothing and is not wrecked twice
        Assert.Equal(0, pw.Nets[0].Watts); Assert.Equal("lb_worn_out", pw.Nets[0].Why);
    }

    [Fact]
    public void ADay_IsDeterministic_AndTheNetsAreDerivedFromTheBuiltPieces()
    {
        long Run()
        {
            var (rig, pw) = World(new WaterPowerRulesDef { Id = "t", BreakdownPm = 300 });
            for (int d = 0; d < 40; d++) pw.Day(AutumnDay + d);
            return rig.W.Book.Built.Values.Sum(b => (long)b.Hp * (b.X + b.Y));
        }
        Assert.Equal(Run(), Run());
        var (_, p0) = World(); Assert.Single(p0.Refresh(AutumnDay));
        Assert.True(p0.Nets[0].Watts > 0);
        var empty = new PowerWorld(ConstructionRig.Make().W, Cat, 1); Assert.Empty(empty.Refresh(AutumnDay));
    }

    // ---------------------------------------------------------------- the sluice and the save

    [Fact]
    public void TheSluiceState_IsSavedAdditively_AndAnEmptyOneChangesNothing()
    {
        var (rig, pw) = World(sluice: true);
        string clean = rig.W.Book.ComputeHash();
        var empty = ConstructionSave.Capture(rig.W.Book);
        Assert.Empty(empty.SluiceShut);
        Assert.True(pw.SetSluice(WheelX, WheelY + 1, 0, true));
        Assert.NotEqual(clean, rig.W.Book.ComputeHash());
        var back = SaveCodec.Deserialize<ConstructionSave>(SaveCodec.Serialize(ConstructionSave.Capture(rig.W.Book))).Restore();
        Assert.Equal(rig.W.Book.SluiceShut, back.SluiceShut); Assert.Equal(rig.W.Book.ComputeHash(), back.ComputeHash());
        Assert.True(pw.SetSluice(WheelX, WheelY + 1, 0, false));
        Assert.Equal(clean, rig.W.Book.ComputeHash());                       // open again: the hash is what it was
    }

    [Fact]
    public void TheSluiceDeskCommand_ShutsAndOpensItThroughTheHeroDesk()
    {
        var cmd = ConstructionDesk.Sluice(5, 6, 0, true, 100);
        Assert.Equal(Ravensguard.Sim.Trade.HeroCommandKind.Construction, cmd.Kind);
        Assert.Equal("cn:sluice|5|6|0|1", cmd.Tag);
    }

    // ---------------------------------------------------------------- LB-14

    [Fact]
    public void LB14_WaterSawmill_FromAPhase5Save_MigratesToSawMill_AtLabour1000_WithALoadReportLine()
    {
        Assert.Equal("saw_mill", Db.Resolve(IdTables.Businesses, "water_sawmill"));
        var cur = IdTables.Of(Db).Tables.Get(IdTables.Businesses)!;
        Assert.DoesNotContain("water_sawmill", cur); Assert.Contains("saw_mill", cur);
        var old = cur.Select(k => k == "saw_mill" ? "water_sawmill" : k).Order(StringComparer.Ordinal).ToArray();
        var map = TableRemap.Build(IdTables.Businesses, old, cur, Db);
        Assert.Equal(["water_sawmill -> saw_mill"], map.Migrated); Assert.Empty(map.Dropped);
        Assert.Equal(Array.IndexOf(cur, "saw_mill"), map.Map[Array.IndexOf(old, "water_sawmill")]);
        var kind = ProductionContent.Load(Db, EconFixture.Catalog()).Kind("saw_mill");
        Assert.Equal(1000, kind.LabourPm);                                    // no wheel: it keeps running at 1,000
        Assert.Equal(125, kind.PoweredLabourPm);                              // the gang saw's 125 applies only while powered
        Assert.True(kind.FoundedOnly);
    }
}
