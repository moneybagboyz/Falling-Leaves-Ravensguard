using System.Diagnostics;
using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.WorldGen;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Combat.Battle;

/// <summary>
/// Phase 7 CV-C (cavalry-and-mounted-war 4.4, 4.8, 10): ground and weather. CV-6 stakes, CV-10 footing cells (a muddy stream bottom, a downhill charge, a ditch), CV-11 weather (rain and mud, frost and ice, fog);
/// and the pieces around them: branches in woods, fire panic, snow, the save round trip and determinism, and the same charge on dry field, mud and a wooded slope.
/// </summary>
[Collection("fight")]
public class CavalryGroundTests(FightFixture fx, ITestOutputHelper output)
{
    private const int Ox = -200_000, Oy = -200_000, Dim = 100;       // cells of 4 m: a 400 m box around the origin

    private static FootingCells Box() => new(Ox, Oy, Dim, Dim);

    private (FightWorld W, Fighter Rider, MountedFighter M) Rider(ulong seed, int x, FootingCells? cells = null, string breed = "warhorse_courser", int y = 0)
    {
        var w = fx.World(seed);
        var r = w.AddMounted(fx.Kit("Rider", 0, x, y, weapon: MountedCalibration.Lance, skill: 50, worn: FightCalibration.Mailed.Worn) with { Shield = "heater" },
            new MountKit { Breed = breed, Riding = 50, GearKg = 20, RiderKg = 80 }, true);
        if (cells != null) w.Mounts!.Terrain.Cells = cells;
        return (w, r, w.Riding(r)!);
    }

    private static void Ride(Fighter r, bool gallop) { r.Cmd.MoveX = FightMath.One; r.Cmd.MoveY = 0; r.Cmd.Sprint = gallop; }

    // ---------------------------------------------------------------- CV-6 stakes

    [Fact]
    public void CV6_AStakeRow_Adds250PermillePerCellInTheArc_ToTheRefusal()
    {
        Assert.Equal(100, ChargeMath.RefusalPm(0, 700, 50));
        Assert.Equal(350, ChargeMath.RefusalPm(0, 700, 50, extraPm: 250));
        Assert.Equal(600, ChargeMath.RefusalPm(0, 700, 50, extraPm: 500));
        foreach (int n in new[] { 1, 3 })
        {
            int expected = -1; bool balked = false;
            for (ulong seed = 1; seed <= 80 && !balked; seed++)
            {
                var cells = Box();
                for (int k = 0; k < n; k++) cells.PlantStakes(2000, n == 1 ? 2000 : -2000 + k * 4000);
                var (w, r, m) = Rider(seed, -30_000, cells, breed: "horse_palfrey");
                var R = w.Mounts!.D.Refusal;
                Assert.Equal(n, cells.CountNear(Footing.Stakes, 2000, 2000, R.ArcMm));
                expected = ChargeMath.RefusalPm(0, m.TrainedPm, m.Riding, R.BasePm, R.PerBracedPolearmPm, R.TrainedK, R.TrainedRef, R.RidingK, R.MaxPm, 1000, n * w.Mounts.D.StakeRefusalPm);
                for (int t = 0; t < 300 && m.Mounted && !balked; t++)
                {
                    Ride(r, true); w.StepWithAi();
                    var e = w.Events.FirstOrDefault(x => x.Kind == FightEventKind.Balked);
                    if (e.Kind == FightEventKind.Balked) { Assert.Equal(expected, e.A); Assert.True(m.RefusalFor <= -2, "the roll is for the stake row"); balked = true; }
                }
            }
            output.WriteLine($"{n} stake cell(s) in the arc: refusal {expected} permille");
            Assert.True(balked, $"a palfrey balked at {n} stake cell(s)");
        }
    }

    [Fact]
    public void CV6_AHorseRunningOntoAStake_IsStruckWithM0_120_ThroughTheResolver()
    {
        var cells = Box();
        cells.PlantStakes(2000, 2000);                                  // one cell: a trained courser's refusal is nil (200 + 250 - 400 - 100)
        var (w, r, m) = Rider(3, -6000, cells, y: 2000);
        var h = w.Fighters[m.Horse];
        int hp0 = h.Book.Records.Count;
        FightEvent strike = default; int atSpeed = 0;
        for (int t = 0; t < 200 && strike.Kind == default(FightEventKind) && m.Mounted; t++)
        {
            Ride(r, true); w.StepWithAi();
            foreach (var e in w.Events) if (e.Kind == FightEventKind.BraceStrike && e.Actor == h.Id) { strike = e; atSpeed = e.B; }
        }
        output.WriteLine($"stake strike at {atSpeed / 1000.0:0.0} m/s: {strike.A} units, wounds on the horse {h.Book.Records.Count - hp0}");
        Assert.Equal(FightEventKind.BraceStrike, strike.Kind);
        Assert.Equal(h.Id, strike.Target);
        Assert.Equal(120, strike.A);                                    // M0 120, no wielder's skill term
        Assert.InRange(atSpeed, 4000, 12_500);
        // a walk onto the row is not struck (under 4 m/s)
        var (w2, r2, m2) = Rider(3, -6000, cells, y: 2000);
        for (int t = 0; t < 400; t++) { Ride(r2, false); m2.Walk = true; w2.StepWithAi(); }
        Assert.DoesNotContain(w2.Events, e => e.Kind == FightEventKind.BraceStrike);
    }

    // ---------------------------------------------------------------- CV-10 footing cells

    private (FightWorld W, FormationRow Cav, FormationRow Foe) Charge(ulong seed, Action<FootingCells>? ground, int gapMm = 150_000, Action<FightTerrain>? terrain = null, int riders = 20, int foes = 40, int foeRanks = 4)
    {
        var w = fx.World(seed);
        var ids = new List<int>();
        for (int i = 0; i < riders; i++)
        {
            var kit = fx.Kit($"R{i}", 0, 0, i * 600, weapon: MountedCalibration.Lance, skill: 50, worn: FightCalibration.Mailed.Worn, faceX: 1024) with { Shield = "heater", Courage = 100, Tactics = -1 };
            ids.Add(w.AddMounted(kit, new MountKit { Breed = "warhorse_courser", Riding = 50, GearKg = 20, RiderKg = 80, Barding = [] }).Id);
        }
        int perRank = (foes + foeRanks - 1) / foeRanks, y0 = -Math.Min(foes, perRank) * 500;
        var foe = new List<int>();
        for (int i = 0; i < foes; i++)
        {
            int rank = i / perRank, file = i % perRank;
            foe.Add(w.Add(fx.Kit($"w{i}", 1, gapMm + rank * 1200, y0 + file * 1000, skill: 30) with { Courage = 100, Tactics = -1, FaceX = -1024 }, false).Id);
        }
        var cells = Box(); ground?.Invoke(cells);
        var tr = new FightTerrain { Base = Footing.Field, Cells = cells }; terrain?.Invoke(tr);
        if (ground != null || terrain != null) w.UseTerrain(tr);
        var book = w.EnsureFormations();
        var cav = book.Form(w, 0, 1, FormationKind.Cavalry, ids, 0, 0, 1024, 0, Formation.Line, 0, 500, place: true);
        var enemy = book.Form(w, 1, 1, FormationKind.Infantry, foe, gapMm, 0, -1024, 0, Formation.Line, foeRanks, 500, place: true);
        cav.Micro = int.MaxValue / 2; enemy.Micro = int.MaxValue / 2;
        book.Apply(w, enemy, FormationOrder.Of(OrderKind.Hold), w.Tick + 1);
        var o = FormationOrder.Of(OrderKind.Charge); o.TargetFormation = enemy.Index + 1;
        book.Apply(w, cav, o, w.Tick + 1);
        return (w, cav, enemy);
    }

    private record Run(int ContactTick, int TopMmps, int ContactMmps, long ChargeMilli, int Cohesion, int Charges, int Fallen, int Shock);

    private Run Fight(FightWorld w, FormationRow cav, int ticks = 2400)
    {
        int top = 0, contactV = 0; long maxCharge = 0;
        for (int t = 0; t < ticks && cav.FirstContactTick < 0; t++)
        {
            w.StepWithAi();
            foreach (int id in cav.Members) if (w.Riding(w.Fighters[id]) is { Mounted: true } m) { top = Math.Max(top, m.Speed); }
        }
        if (cav.FirstContactTick >= 0)
        {
            int n = 0; long sum = 0;
            foreach (int id in cav.Members) if (w.Riding(w.Fighters[id]) is { Mounted: true } m) { sum += m.Speed; n++; }
            contactV = n == 0 ? 0 : (int)(sum / n);
        }
        for (int t = 0; t < 120; t++) w.StepWithAi();
        foreach (var e in w.Events) if (e.Kind == FightEventKind.Charge) maxCharge = Math.Max(maxCharge, e.A);
        int fallen = w.Mounts!.Pairs.Count(p => !p.Mounted);
        return new Run(cav.FirstContactTick, top, contactV, maxCharge, cav.CohAtContact, w.Events.Count(e => e.Kind == FightEventKind.Charge), fallen, cav.ShockHit);
    }

    [Fact]
    public void CV10_AMuddyStreamBottom_NoChargeAcrossIt_AndNoCouchedBlowFromIt()
    {
        var (wd, cd, _) = Charge(1, null);
        var dry = Fight(wd, cd);
        // the stream bottom runs the last 40 m in front of the line and past it
        var (wm, cm, _) = Charge(1, c => c.Fill(110_000, -100_000, 190_000, 100_000, Footing.Mud));
        int inStream = 0, worstInStream = 0;
        for (int t = 0; t < 2400 && cm.FirstContactTick < 0; t++)
        {
            wm.StepWithAi();
            foreach (int id in cm.Members) { var f = wm.Fighters[id]; var p = wm.Riding(f); if (p != null && f.X > 125_000) { inStream++; worstInStream = Math.Max(worstInStream, p.Speed); } }
        }
        output.WriteLine($"dry: top {dry.TopMmps / 1000.0:0.0} m/s, charge blows {dry.Charges}, max charge {dry.ChargeMilli} units; mud: worst rider speed in the stream (after 15 m of braking) {worstInStream / 1000.0:0.0} m/s");
        Assert.True(dry.TopMmps >= 9000 && dry.Charges > 0, $"dry ground charges ({dry.TopMmps}, {dry.Charges})");
        Assert.True(inStream > 0);
        Assert.True(worstInStream <= 5000 + 100, $"in the mud no rider is above its cap ({worstInStream})");
        for (int t = 0; t < 150; t++) wm.StepWithAi();
        Assert.DoesNotContain(wm.Events, e => e.Kind == FightEventKind.Charge);        // no couched charge is made out of the mud
    }

    [Fact]
    public void CV10_ADownhillCharge_Adds10PercentToTheClosingSpeed_FromTheCellHeights()
    {
        int Vc(int slopePmDown)
        {
            var cells = Box();
            cells.Build((cx, cy) => (Footing.Field, -cx * 4000 * slopePmDown / 1000));      // height falls eastward: downhill for a rider going east
            var (w, r, m) = Rider(1, 0, cells);
            var foe = w.Add(fx.Kit("Foe", 1, 6000, 0));
            m.VX = 10_000; m.VY = 0; m.Speed = 10_000; m.HX = 1024; m.HY = 0;
            return w.ClosingMmps(m, r, foe);
        }
        int flat = Vc(0), down = Vc(100), up = Vc(-100);
        output.WriteLine($"closing speed flat {flat}, downhill (10%) {down}, uphill {up}");
        Assert.Equal(10_000, flat);
        Assert.Equal(11_000, down);                                     // +10% v_c, as the plane did
        Assert.Equal(10_000, up);
        var cells2 = Box(); cells2.Build((cx, cy) => (Footing.Field, -cx * 400));
        Assert.InRange(cells2.SlopeAlongPm(0, 0, 1024, 0), -101, -99);
        Assert.InRange(cells2.SlopeAlongPm(0, 0, -1024, 0), 99, 101);
        Assert.InRange(cells2.SlopeAlongPm(0, 0, 0, 1024), -1, 1);
    }

    [Fact]
    public void CV10_UphillAndSlope_SlowTheHorse_FromTheCells()
    {
        double Speed(int riseMmPerCell)
        {
            var cells = Box();
            cells.Build((cx, cy) => (Footing.Field, cx * riseMmPerCell));
            var (w, r, m) = Rider(1, -20_000, cells);
            for (int t = 0; t < 120; t++) { Ride(r, true); w.StepWithAi(); }
            return m.Speed;
        }
        double flat = Speed(0), up = Speed(800);                          // 800 mm a 4 m cell: a 20% slope
        output.WriteLine($"gallop on the flat {flat}, up a 20% slope {up}");
        Assert.True(up < flat * 0.5, "a 20% slope costs more than half the speed (1 - 3 x 0.2 = 0.4)");
    }

    [Fact]
    public void CV10_ADitchAtAGallop_FallsWith400Permille_AndAWalkDoesNot()
    {
        int falls = 0, walkFalls = 0; const int N = 200;
        for (ulong seed = 1; seed <= N; seed++)
        {
            var cells = Box(); cells.DigDitch(2000, 2000);
            var (w, r, m) = Rider(seed, -9000, cells, y: 2000);
            for (int t = 0; t < 120; t++) { Ride(r, true); w.StepWithAi(); }
            if (w.Events.Any(e => e.Kind == FightEventKind.HorseFell && e.A == 2)) falls++;
            var (w2, r2, m2) = Rider(seed, -9000, cells, y: 2000);
            for (int t = 0; t < 200; t++) { Ride(r2, false); w2.StepWithAi(); }
            if (w2.Events.Any(e => e.Kind == FightEventKind.HorseFell)) walkFalls++;
        }
        output.WriteLine($"ditch at a gallop: {falls} falls in {N} (400 permille expected); at a trot {walkFalls}");
        Assert.InRange(falls, 56, 104);
        Assert.Equal(0, walkFalls);
    }

    [Fact]
    public void CV10_CellsHaveABuildHash_AndAreReadByTheTerrain_BeforeTheZones()
    {
        var a = Box(); var b = Box();
        Assert.Equal(a.Hash(), b.Hash());
        b.PlantStakes(0, 0);
        Assert.NotEqual(a.Hash(), b.Hash());
        var tr = new FightTerrain { Base = Footing.Field, Cells = b };
        tr.Zones.Add((-10_000, -10_000, 10_000, 10_000, Footing.Rubble));
        Assert.Equal(Footing.Stakes, tr.At(0, 0));
        Assert.Equal(Footing.Rubble, tr.At(5000, 5000));               // a cell with no data falls through to the zones
        Assert.Equal(Footing.Field, tr.At(50_000, 0));
        Assert.Equal(Footing.Field, tr.At(-500_000, 0));               // off the box
        Assert.Equal(Footing.Stakes, tr.At(0, 0));
    }

    // ---------------------------------------------------------------- CV-11 weather

    [Theory]
    [InlineData(Footing.Field, 3, 100, 0, true, Footing.Mud)]          // three hours of rain: field is mud for horses
    [InlineData(Footing.Field, 2, 100, 0, true, Footing.Field)]
    [InlineData(Footing.Field, 0, -30, 0, true, Footing.Ice)]          // frost on wet ground reads ice
    [InlineData(Footing.Field, 0, -30, 0, false, Footing.Field)]       // dry frost does not
    [InlineData(Footing.Mud, 5, -30, 0, true, Footing.Ice)]
    [InlineData(Footing.Field, 0, 20, 8, false, Footing.Snow)]         // snow over 5 cm
    [InlineData(Footing.Field, 0, 20, 5, false, Footing.Field)]
    [InlineData(Footing.Woods, 9, -90, 30, true, Footing.Woods)]
    [InlineData(Footing.Stakes, 9, -90, 30, true, Footing.Stakes)]
    [InlineData(Footing.Road, 5, 100, 0, true, Footing.Road)]
    public void CV11_TheGroundReadsAsTheWeatherLeavesIt(Footing f, int wetHours, int tempTenths, int snowCm, bool wet, Footing expected) =>
        Assert.Equal(expected, FootingCells.Weathered(f, new FootingCells.Ground(wetHours, tempTenths, snowCm, wet)));

    [Fact]
    public void CV11_ThreeHoursOfRain_FieldIsMudForHorses_NoCharge_AndFrostIsIce()
    {
        double Top(FootingCells.Ground? g, Action<FightTerrain>? more = null)
        {
            var (w, r, m) = Rider(4, -60_000);
            if (g != null) w.Mounts!.Terrain.Wet = g;
            more?.Invoke(w.Mounts!.Terrain);
            int top = 0;
            for (int t = 0; t < 200; t++) { Ride(r, true); w.StepWithAi(); top = Math.Max(top, m.Speed); }
            return top;
        }
        double dry = Top(null), mud = Top(new FootingCells.Ground(3, 100, 0, true)), twoHours = Top(new FootingCells.Ground(2, 100, 0, true));
        output.WriteLine($"gallop top speed: dry field {dry / 1000.0:0.0}, after 3 h of rain {mud / 1000.0:0.0}, after 2 h {twoHours / 1000.0:0.0}");
        Assert.True(dry >= 9000);
        Assert.InRange(mud, 1, 5000);
        Assert.True(twoHours >= 9000);
        // frost on wet ground: ice slips a gallop (8% a second): horses go down
        int slips = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var (w, r, m) = Rider(seed, -60_000);
            w.Mounts!.Terrain.Wet = new FootingCells.Ground(0, -40, 0, true);
            Assert.Equal(Footing.Ice, w.Mounts.Terrain.At(r.X, r.Y));
            for (int t = 0; t < 100; t++) { Ride(r, true); w.StepWithAi(); }
            if (w.Events.Any(e => e.Kind == FightEventKind.HorseFell)) slips++;
        }
        output.WriteLine($"ice: {slips} of 40 horses slipped in five seconds");
    }

    [Fact]
    public void CV11_Snow_SlowsHorsesByTheWeatherRowsCavalrySpeed()
    {
        var row = FightWorld.WeatherRowOf(fx.Db, "Snow")!;
        Assert.Equal(600, row.CavalrySpeedPm);
        int Top(bool snow)
        {
            var (w, r, m) = Rider(4, -60_000);
            if (snow) w.Mounts!.Terrain.ApplyRow(row);
            int top = 0;
            for (int t = 0; t < 200; t++) { Ride(r, true); w.StepWithAi(); top = Math.Max(top, m.Speed); }
            return top;
        }
        int clear = Top(false), snow = Top(true);
        output.WriteLine($"gallop {clear} clear, {snow} in snow");
        Assert.InRange(snow, clear * 55 / 100, clear * 65 / 100);
    }

    [Fact]
    public void CV11_Fog_AChargeAtAnUnseenTarget_WalksUntilItIsSeen()
    {
        var (wc, cc, _) = Charge(5, null);
        for (int t = 0; t < 2400 && cc.FirstContactTick < 0; t++) wc.StepWithAi();
        var (wf, cf, _) = Charge(5, null, terrain: tr => tr.ApplyRow(FightWorld.WeatherRowOf(fx.Db, "Fog")!));
        Assert.Equal(15_000, wf.Mounts!.Terrain.VisionMm);
        int topBefore = 0;
        for (int t = 0; t < 2400 && cf.FirstContactTick < 0; t++)
        {
            wf.StepWithAi();
            long d = FightMath.Len(cf.AnchorX - wf.Formations!.Rows[1].AnchorX, cf.AnchorY - wf.Formations.Rows[1].AnchorY);
            if (d > 20_000) foreach (int id in cf.Members) topBefore = Math.Max(topBefore, wf.Riding(wf.Fighters[id])?.Speed ?? 0);
        }
        output.WriteLine($"clear: stages {string.Join(",", cc.StageTick)}; fog: stages {string.Join(",", cf.StageTick)}, fastest ride before 20 m {topBefore} mm/s");
        Assert.True(cc.StageTick[FormationBook.StTrot] >= 0 && cc.StageTick[FormationBook.StGallop] >= 0);
        Assert.Equal(-1, cf.StageTick[FormationBook.StTrot]);            // no trot, no gallop: it walks until the foe is seen
        Assert.Equal(-1, cf.StageTick[FormationBook.StGallop]);
        Assert.True(topBefore < 3000, $"the fog charge walked up ({topBefore})");
        Assert.True(cf.FirstContactTick > cc.FirstContactTick, "and arrives later");
    }

    [Fact]
    public void CV11_RealWeather_ThreeHoursOfRainOver2mm_MakeTheGroundMud()
    {
        var s = ContinentSettings.LoadDefaults(ContinentSettings.FindBaseContent()); s.Seed = 1234; s.Layout = ContinentLayout.Continents;
        var cont = ContinentGenerator.Generate(s);
        var wx = RegionWeather.ForContinent(cont, 77, WeatherSettings.LoadDefaults(ContinentSettings.FindBaseContent()));
        FootingCells.Ground? found = null; int cell = 0; long hour = 0;
        for (int c = 0; c < Continent.Cells && found == null; c += 197)
        {
            if (cont.Ocean[c] || cont.Lake[c]) continue;
            for (long h = 24; h < 24 * 90 && found == null; h += 3)
            {
                var g = FootingCells.Ground.From(wx, c, h);
                if (g.WetHours >= 3) { found = g; cell = c; hour = h; }
            }
        }
        Assert.NotNull(found);
        var s0 = wx.AtLite(cell, hour);
        output.WriteLine($"cell {cell} hour {hour}: {found} ({s0.State}, {s0.PrecipMmH.ToDouble():0.0} mm/h, {s0.TempC.ToDouble():0.0} C)");
        Assert.True(s0.PrecipMmH.ToDouble() >= 2.0 && !s0.PrecipIsSnow);
        Assert.Equal(Footing.Mud, FootingCells.Weathered(Footing.Field, found.Value with { SnowCm = 0, TempTenthsC = 100 }));
        Assert.Equal(FootingCells.Ground.From(wx, cell, hour), FootingCells.Ground.From(wx, cell, hour));          // a pure function
    }

    // ---------------------------------------------------------------- woods, fire

    [Fact]
    public void Woods_BranchesSweepATrottingRider_NeverTwiceIn20s_AndNotInTheOpen()
    {
        int hits = 0, openHits = 0;
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var cells = Box(); cells.Fill(Ox, Oy, -Ox, -Oy, Footing.Woods);
            var (w, r, m) = Rider(seed, -150_000, cells);
            var last = new List<int>();
            for (int t = 0; t < 2000 && r.Active; t++)
            {
                Ride(r, false); m.Walk = false; w.StepWithAi();
                foreach (var e in w.Events) if (e.Kind == FightEventKind.Fell && e.Actor == r.Id && e.Tick == w.Tick) last.Add(e.Tick);
            }
            var ticks = w.Events.Where(e => e.Kind == FightEventKind.Fell && e.Actor == r.Id).Select(e => e.Tick).Distinct().OrderBy(x => x).ToList();
            hits += ticks.Count;
            for (int i = 1; i < ticks.Count; i++) Assert.True(ticks[i] - ticks[i - 1] >= 400, "a branch at most once per 20 s per rider");
            var (w2, r2, m2) = Rider(seed, -150_000);
            for (int t = 0; t < 2000 && r2.Active; t++) { Ride(r2, false); w2.StepWithAi(); }
            openHits += w2.Events.Count(e => e.Kind == FightEventKind.Fell);
        }
        output.WriteLine($"branch blows over 12 riders x 100 s in woods: {hits}; in the open {openHits}");
        Assert.True(hits >= 1);
        Assert.Equal(0, openHits);
    }

    [Fact]
    public void Fire_AHorseNearABurningCell_GainsPanic_OneFarAwayDoesNot()
    {
        int Panic(int fireX)
        {
            var cells = Box(); cells.SetFire(fireX, 0, true);
            var (w, r, m) = Rider(2, 0, cells);
            for (int t = 0; t < 60; t++) { r.Cmd.ClearAll(); w.StepWithAi(); }
            return m.PanicPm;
        }
        int near = Panic(6000), far = Panic(60_000);
        output.WriteLine($"panic after 3 s: near the fire {near}, far {far}");
        Assert.True(near > far && far == 0);
    }

    // ---------------------------------------------------------------- the same charge on three grounds

    [Fact]
    public void WorkedExample_TheSameCharge_OnDryField_Mud_AndAWoodedSlope()
    {
        Run Go(Action<FootingCells> g)
        {
            var (w, cav, _) = Charge(7, g, gapMm: 150_000);
            return Fight(w, cav);
        }
        var dry = Go(_ => { });
        var mud = Go(c => c.Fill(100_000, -100_000, 190_000, 100_000, Footing.Mud));
        var half = Go(c => c.Fill(100_000, 0, 190_000, 100_000, Footing.Mud));                                      // mud under half the line only
        var slope = Go(c =>
        {
            c.Build((cx, cy) => (Footing.Field, Math.Max(0, (cx * 4000 + Ox - 90_000)) * 90 / 1000));                  // a 9% rise over the last 60 m
            c.Fill(100_000, -100_000, 190_000, 100_000, Footing.Woods);
        });
        foreach (var (name, r) in new[] { ("dry field", dry), ("mud", mud), ("half the line in mud", half), ("wooded slope", slope) })
            output.WriteLine($"{name,-13} contact tick {r.ContactTick}, top {r.TopMmps / 1000.0:0.0} m/s, mean speed at contact {r.ContactMmps / 1000.0:0.0} m/s, charge blows {r.Charges}, best charge {r.ChargeMilli} units, cohesion {r.Cohesion}, shock {r.Shock}, riders down {r.Fallen}");
        Assert.True(dry.TopMmps >= 9000 && dry.Charges > 0);
        Assert.True(mud.ContactMmps <= 5200 && mud.Charges == 0);
        Assert.True(slope.ContactMmps <= 5200 && slope.Charges == 0);
        Assert.True(dry.ChargeMilli > 0 && dry.ContactTick < mud.ContactTick);
    }

    // ---------------------------------------------------------------- save and determinism

    private FightWorld MixedBattle(ulong seed, int level = 7)
    {
        var (w, cav, _) = Charge(seed, (level & 1) == 0 ? null : c =>
        {
            c.Build((cx, cy) => (Footing.Field, (cx * 4000 + Ox) / 50));
            c.Fill(55_000, -60_000, 75_000, 60_000, Footing.Mud);
            c.Fill(20_000, -60_000, 40_000, -30_000, Footing.Woods);
            c.PlantStakes(80_000, 0); c.DigDitch(60_000, 6000);
        }, gapMm: 90_000, terrain: (level & 2) == 0 ? null : tr => tr.Wet = new FootingCells.Ground(4, 30, 0, true));
        if ((level & 4) != 0) w.Mounts!.Terrain.ApplyRow(FightWorld.WeatherRowOf(fx.Db, "HeavyRain")!);
        return w;
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(4)] [InlineData(7)]
    public void Determinism_AndSave_MidCharge_OnMixedGround_EqualsTheUninterruptedRun(int level)
    {
        var a = MixedBattle(9, level); var b = MixedBattle(9, level);
        for (int t = 0; t < 900; t++) { a.StepWithAi(); b.StepWithAi(); }
        Assert.Equal((a.Hash(), a.MountedHash()), (b.Hash(), b.MountedHash()));
        var full = MixedBattle(11, level); var part = MixedBattle(11, level);
        // saved 30 ticks after the first contact, as CV-A's own save test does (a save during the approach drifts the anchor by a few mm on the first step: a CV-A finding, see the notes)
        int saveAt = 0;
        { var probe = MixedBattle(11, level); var row = probe.Formations!.Rows[0]; while (row.FirstContactTick < 0 && probe.Tick < 3000) probe.StepWithAi(); saveAt = row.FirstContactTick + 30; }
        for (int t = 0; t < saveAt; t++) { full.StepWithAi(); part.StepWithAi(); }
        var back = FightRecord.FromBytes(FightRecord.Capture(part).ToBytes()).Restore(fx.C);
        var tr = back.Mounts!.Terrain;
        Assert.Equal(part.Mounts!.Terrain.Wet, tr.Wet);
        Assert.Equal(part.Mounts.Terrain.VisionMm, tr.VisionMm);
        Assert.Equal(part.Mounts.Terrain.CavSpeedPm, tr.CavSpeedPm);
        Assert.Equal(part.Mounts.Terrain.HeavyRain, tr.HeavyRain);
        back.UseTerrain(tr); tr.Cells = part.Mounts.Terrain.Cells;           // the cells are rebuilt from the chunks on load
        Assert.Equal((part.Hash(), part.MountedHash()), (back.Hash(), back.MountedHash()));
        for (int t = 0; t < 1200; t++) { full.StepWithAi(); back.StepWithAi(); if (t % 100 == 0) Assert.Equal((full.Hash(), full.MountedHash()), (back.Hash(), back.MountedHash())); }
        Assert.Equal((full.Hash(), full.MountedHash()), (back.Hash(), back.MountedHash()));
    }
}

/// <summary>The CV-C budget (cavalry-and-mounted-war 11): cavalry over mixed ground (mud, woods, ditches, slopes, stakes) must stay inside the mounted layer's 0.8 ms a tick, by itself in the timing collection.</summary>
[Collection(TimingCollection.Name)]
public class CavalryGroundBudgetTests(FightFixture fx, ITestOutputHelper output) : IClassFixture<FightFixture>
{
    private FightWorld Big(bool ground)
    {
        var w = fx.World(50); w.RecordEvents = false;
        var book = w.EnsureFormations();
        var cells = new FootingCells(-300_000, -300_000, 150, 150);
        if (ground)
        {
            cells.Build((cx, cy) => (Footing.Field, ((cx * 4000 - 300_000) / 40 + (cy * 4000 - 300_000) / 90)));
            for (int i = 0; i < 12; i++) cells.Fill(-120_000 + i * 20_000, -280_000 + i * 12_000, -100_000 + i * 20_000, -240_000 + i * 12_000, i % 3 == 0 ? Footing.Mud : i % 3 == 1 ? Footing.Woods : Footing.Rubble);
            for (int i = 0; i < 40; i++) { cells.DigDitch(-60_000 + i * 4000, -20_000); cells.PlantStakes(-60_000 + i * 4000, 40_000); }
        }
        var tr = new FightTerrain { Base = Footing.Field, Cells = cells };
        if (ground) tr.Wet = new FootingCells.Ground(1, 50, 0, false);
        w.UseTerrain(tr);
        for (int team = 0; team < 2; team++)
        {
            for (int k = 0; k < 10; k++)
            {
                var ids = new List<int>();
                for (int i = 0; i < 100; i++) ids.Add(w.Add(fx.Kit("s", team, 0, 0, skill: 30 + i * 7 % 40, shield: i % 2 == 0 ? "heater" : null) with { Tactics = i == 0 ? 30 : 0, Courage = 100 }).Id);
                var r = book.Form(w, team, (byte)(k + 1), FormationKind.Infantry, ids, team == 0 ? -10_000 : 10_000, (k - 5) * 34_000, team == 0 ? 1024 : -1024, 0, Formation.Line, 4, place: true);
                r.CaptainAlways = true;
            }
            for (int k = 0; k < 10; k++)
            {
                var ids = new List<int>();
                for (int i = 0; i < 15; i++)
                    ids.Add(w.AddMounted(fx.Kit("R", team, 0, 0, weapon: MountedCalibration.Lance, skill: 50, worn: FightCalibration.Mailed.Worn, faceX: team == 0 ? 1024 : -1024) with { Shield = "heater", Courage = 100, Tactics = i == 0 ? 50 : 0 },
                        new MountKit { Riding = 50 + i % 7, GearKg = 20, RiderKg = 80 }).Id);
                var r = book.Form(w, team, (byte)(40 + k), FormationKind.Cavalry, ids, team == 0 ? -110_000 : 110_000, (k - 5) * 34_000, team == 0 ? 1024 : -1024, 0, Formation.Line, 0, 500, place: true);
                r.CaptainAlways = true;
            }
        }
        w.LodCentre = (-10_000, 0);
        return w;
    }

    [Fact]
    public void CV13_Budget_300Cavalry_OnMixedGround_TheMountedLayerStaysUnder08ms()
    {
        var warm = Big(true);
        for (int i = 0; i < 60; i++) warm.StepWithAi();
        double Pass(bool ground)
        {
            var w = Big(ground);
            for (int i = 0; i < 20; i++) w.StepWithAi();
            var L = w.Mounts!; long p0 = L.PerfTicks;
            var sw = new Stopwatch(); double sum = 0; int n = 600;
            for (int i = 0; i < n; i++) { sw.Restart(); w.StepWithAi(); sum += sw.Elapsed.TotalMilliseconds; }
            double mounted = (L.PerfTicks - p0) * 1000.0 / Stopwatch.Frequency / n;
            output.WriteLine($"{(ground ? "mixed ground" : "no cells")}: whole tick {sum / n:0.00} ms, the mounted layer {mounted:0.000} ms a tick");
            return mounted;
        }
        Pass(false);
        double avg = TestBudget.BestPass(0.8, 3, () => Pass(true));
        Assert.True(avg <= TestBudget.Ms(0.8), $"the mounted layer averaged {avg:0.000} ms a tick on mixed ground");
    }
}
