using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Ravensguard.Sim.Tests.Economy;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Siege;

/// <summary>P8-LW-3, forts and chariots in a live battle (live-war LW-10 to LW-12): the fort's boards as the fight's walls and the sortie, the AI's fort, the chariots, the archer's spread and the cheval de frise.</summary>
[Collection("fight")]
public class LiveWarFortTests(FightFixture fx, ITestOutputHelper output)
{
    private static readonly EntityId Lord = EntityId.Make(IdKind.Person, 77);

    private static RideBook Rides(ulong seed = 7) { var b = new RideBook(EconFixture.Content, EconFixture.NewEconomy(false), seed); Assert.Empty(b.Templates.Errors); return b; }

    private (FightWorld W, RideBook B, FortLayer L) Ring(int wagons, ulong seed = 1, int team = 1, bool chained = true, bool ai = false)
    {
        var w = fx.World(seed); var b = Rides(); var fort = new WagonFort(EconFixture.Content, b, 7);
        var list = new List<VehicleRow>();
        for (int i = 0; i < wagons; i++) list.Add(b.Vehicles.Create(b.Templates.Get("war_wagon"), Lord, -8_000 - i * 6_000, (i % 2) * 2_000));
        var layer = new FortLayer(w, fort, team) { AiForms = ai };
        Assert.True(layer.Attach());
        if (ai) layer.Wagons.AddRange(list);
        else { Assert.True(fort.Form(list, 0, 0, 0, _ => 50)); if (chained) { fort.Step(240); Assert.True(fort.Sealed); } }
        return (w, b, layer);
    }

    private (long X, long Y) Chain(FortLayer l, int i) => (l.Fort.Chains[i].X, l.Fort.Chains[i].Y);

    // ------------------------------------------------------------------ LW-10

    [Fact]
    public void LW10_AChargeOnAChainedRingStopsAtTheBoards_AndTheSortieGapOpensFor20Seconds()
    {
        // a rider driven at the ring from 30 m: the boards are walls and count as braced polearms, so the horse balks and never gets inside
        int balked = 0, runs = 20;
        for (ulong seed = 1; seed <= (ulong)runs; seed++)
        {
            var (w, _, layer) = Ring(8, seed, team: 1);
            var r = w.AddMounted(fx.Kit("Rider", 0, -30_000, weapon: MountedCalibration.Lance, worn: FightCalibration.Mailed.Worn) with { Shield = "heater" },
                new MountKit { Breed = "horse_palfrey", Riding = 0, GearKg = 20, RiderKg = 80 }, true);
            var m = w.Riding(r)!;
            bool inside = false;
            for (int t = 0; t < 600; t++)
            {
                r.Cmd.MoveX = FightMath.One; r.Cmd.MoveY = 0; r.Cmd.Sprint = true;
                w.StepWithAi();
                if (layer.Fort.Inside(r.X, r.Y) || layer.Fort.Inside(w.Fighters[m.Horse].X, w.Fighters[m.Horse].Y)) inside = true;
            }
            Assert.False(inside, $"seed {seed}: the charge got inside the boards");
            if (w.Events.Any(e => e.Kind == FightEventKind.Balked)) balked++;
        }
        output.WriteLine($"balked in {balked} of {runs}");
        Assert.True(balked >= runs * 6 / 10, $"balked in {balked}");

        // a man on foot stops at the boards too
        var (w2, _, l2) = Ring(8, 3);
        var man = w2.Add(fx.Kit("Walker", 0, -30_000), true);
        for (int t = 0; t < 1200; t++) { man.Cmd.MoveX = FightMath.One; man.Cmd.MoveY = 0; w2.Step(); }
        Assert.False(l2.Fort.Inside(man.X, man.Y));

        // the sortie: one gap opens at once, stays open 20 s, then the chain is hung again (no enemy within 4 m)
        var (w3, _, l3) = Ring(8, 5);
        var (cx, cy) = Chain(l3, 2);
        Assert.True(l3.Fort.BlocksMove((int)cx, (int)cy));
        var book = w3.EnsureFormations();
        var own = w3.Add(fx.Kit("Own", 1, 0, 0));
        var row = book.Form(w3, 1, 1, FormationKind.Infantry, [own.Id], 0, 0, 1024, 0, Formation.Line, 1, 500, place: true);
        book.Apply(w3, row, new FormationOrder { Kind = OrderKind.Sortie, TargetX = (int)cx, TargetY = (int)cy, TargetFighter = -1, By = -1, Runner = -1 }, w3.Tick + 1);
        Assert.Equal(ChainState.Open, l3.Fort.Chains[2].State);
        Assert.False(l3.Fort.BlocksMove((int)cx, (int)cy));
        Assert.Single(l3.Sorties);
        Assert.False(l3.Sortie((int)cx, (int)cy));                                              // one gap at a time
        for (int t = 0; t < 19 * 20; t++) w3.Step();
        Assert.Equal(ChainState.Open, l3.Fort.Chains[2].State);                                 // still open at 19 s
        for (int t = 0; t < 2 * 20; t++) w3.Step();
        Assert.Equal(ChainState.Chaining, l3.Fort.Chains[2].State);                             // the chain goes back up
        Assert.Empty(l3.Sorties);
        for (int t = 0; t < 21 * 20; t++) w3.Step();
        Assert.Equal(ChainState.Chained, l3.Fort.Chains[2].State);
        Assert.True(l3.Fort.BlocksMove((int)cx, (int)cy));

        // an enemy within 4 m holds the gap open
        var (w4, _, l4) = Ring(8, 6);
        var (hx, hy) = Chain(l4, 0);
        w4.Add(fx.Kit("Foe", 0, (int)hx + 3000, (int)hy), true);
        Assert.True(l4.Sortie((int)hx, (int)hy));
        for (int t = 0; t < 60 * 20; t++) w4.Step();
        Assert.Equal(ChainState.Open, l4.Fort.Chains[0].State);
        Assert.Single(l4.Sorties);
    }

    // ------------------------------------------------------------------ LW-11

    private (FightWorld W, FortLayer L) HostAndHorse(int wagons, int riders, int foot, int horseX = 800_000)
    {
        var (w, _, layer) = Ring(wagons, 11, team: 1, ai: true);
        for (int i = 0; i < 10; i++) w.Add(fx.Kit($"H{i}", 1, i * 1500, 0));                                            // the host
        for (int i = 0; i < foot; i++) w.Add(fx.Kit($"F{i}", 0, horseX - 3000, i * 1500));
        for (int i = 0; i < riders; i++) w.AddMounted(fx.Kit($"R{i}", 0, horseX, i * 1500, weapon: MountedCalibration.Lance), new MountKit { Breed = "warhorse_courser", Riding = 50, GearKg = 20, RiderKg = 80 });
        return (w, layer);
    }

    [Theory]
    [InlineData(6, 3, 7, 800_000, true)]        // six wagons, 30% horse, 800 m: the fort
    [InlineData(5, 3, 7, 800_000, false)]       // five wagons
    [InlineData(6, 2, 8, 800_000, false)]       // 20% horse
    [InlineData(6, 3, 7, 2_600_000, false)]     // the horse 2.6 km off
    public void LW11_AHostWithSixWagonsFormsAFortAgainst30PercentHorseWithin2Km(int wagons, int riders, int foot, int horseX, bool forms)
    {
        var (w, layer) = HostAndHorse(wagons, riders, foot, horseX);
        for (int t = 0; t < 5 * 20; t++) w.Step();
        Assert.Equal(forms, layer.FormedTick >= 0);
        Assert.Equal(forms ? wagons : 0, layer.Fort.Slots.Count);
        if (forms) { for (int t = 0; t < 200 * 20; t++) w.Step(); Assert.True(layer.Fort.Formed, "the ring is chained"); }
    }

    // ------------------------------------------------------------------ LW-12

    private (FightWorld W, ChariotLayer L, ChariotCar Car, Fighter Rider, Fighter Foe) Car(FightTerrain terrain, bool spear, int foeX, ulong seed = 1, int foeY = 0, bool archer = false)
    {
        var w = fx.World(seed); var b = Rides();
        var book = new ChariotBook(EconFixture.Content, b, 7);
        var v = b.Vehicles.Create(b.Templates.Get("war_chariot"), Lord, 0, 0); v.Facing = 0;
        var dp = EntityId.Make(IdKind.Person, 801); var rp = EntityId.Make(IdKind.Person, 802);
        var ch = book.Raise(v, dp, rp, spear, 50)!;
        var driver = w.Add(fx.Kit("Driver", 0, 0, 0));
        var rider = archer ? w.Add(fx.Kit("Archer", 0, 0, 0, launcher: "longbow", ammo: "bodkin_arrow", ammoCount: 30)) : w.Add(fx.Kit("Spear", 0, 0, 0, weapon: MountedCalibration.Lance));
        var foe = w.Add(fx.Kit("Foe", 1, foeX, foeY));
        var layer = new ChariotLayer(w, book, terrain);
        var car = layer.Add(ch, 0, driver.Id, rider.Id, dp, rp)!;
        return (w, layer, car, rider, foe);
    }

    [Fact]
    public void LW12_AnAiChariotNeverEntersWoods_ItsArchersSpreadDoublesAbove3MetresASecond_AndHorsesRefuseAChevalDeFrise()
    {
        // woods across the road: the car looks ahead and never stands in them, for a whole minute of trying
        var terrain = new FightTerrain();
        terrain.Zones.Add((60_000, -30_000, 100_000, 30_000, Footing.Woods));
        var (w, layer, car, _, foe) = Car(terrain, spear: true, foeX: 140_000);
        var v = layer.Book.Rides.Vehicles.Find(car.Car.Vehicle)!;
        int moved = 0;
        for (int t = 0; t < 90 * 20; t++)
        {
            w.StepWithAi();
            Assert.NotEqual(Footing.Woods, terrain.At((int)v.XMm, (int)v.YMm));
            if (t % 20 == 0 && car.Car.SpeedMmps > 0) moved++;
        }
        Assert.True(moved >= 3 && v.XMm > 30_000, $"the car drove ({moved} s moving, at {v.XMm} mm)");
        output.WriteLine($"car at {v.XMm / 1000},{v.YMm / 1000} m; foe {foe.X / 1000},{foe.Y / 1000}");

        // on open firm ground the same car charges the foot man and the thrust and hooves land
        int hurt = 0;
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var (w2, l2, c2, _, f2) = Car(new FightTerrain(), spear: true, foeX: 60_000, seed: seed);
            for (int t = 0; t < 30 * 20; t++) w2.StepWithAi();
            if (f2.Book.Records.Count > 0 || !f2.Active) hurt++;
        }
        output.WriteLine($"the foot man hurt in {hurt} of 12");
        Assert.True(hurt >= 6, $"hurt in {hurt}");

        // the archer's spread: x2 above 3 m/s
        var (w3, l3, c3, bow, _) = Car(new FightTerrain(), spear: false, foeX: 90_000, archer: true);
        c3.Car.SpeedMmps = 2_000; Assert.Equal(1000, l3.ShotSigma(bow, 1000));
        c3.Car.SpeedMmps = 3_000; Assert.Equal(1000, l3.ShotSigma(bow, 1000));
        c3.Car.SpeedMmps = 4_000; Assert.Equal(2000, l3.ShotSigma(bow, 1000));
        var other = w3.Fighters[c3.Driver]; Assert.Equal(1000, l3.ShotSigma(other, 1000));                  // only the archer shoots from the car
        // in the fight: the shots he looses at speed carry the doubled cone, those from a standing car do not
        var (w4, l4, c4, bow4, _) = Car(new FightTerrain(), spear: false, foeX: 35_000, archer: true);
        l4.AiDrives = false;
        c4.Car.SpeedMmps = 6_000;
        int movingSigma = 0, standingSigma = 0;
        for (int t = 0; t < 12 * 20; t++) { w4.StepWithAi(); foreach (var e in w4.Events) if (e.Kind == FightEventKind.Shot && e.Actor == bow4.Id) { movingSigma = Math.Max(movingSigma, e.A); } }
        var (w5, l5, c5, bow5, _) = Car(new FightTerrain(), spear: false, foeX: 35_000, archer: true);
        l5.AiDrives = false;
        for (int t = 0; t < 12 * 20; t++) { w5.StepWithAi(); foreach (var e in w5.Events) if (e.Kind == FightEventKind.Shot && e.Actor == bow5.Id) { standingSigma = Math.Max(standingSigma, e.A); } }
        output.WriteLine($"shot cone moving {movingSigma}, standing {standingSigma}");
        Assert.True(standingSigma > 0, "the standing archer shot");
        Assert.True(movingSigma >= standingSigma * 3 / 2, $"moving {movingSigma} against standing {standingSigma}");

        // a cheval de frise: horses refuse it (braced polearms through the one formula), a man crosses at a third of his pace
        var (w6, _, l6) = Ring(8, 9, team: 1, chained: false);
        l6.Fort.Slots.Clear(); l6.Fort.Chains.Clear();                                                       // chevaux only
        Assert.False(l6.PlaceCheval(30_000, 0, 64, 1));                                                      // one man cannot carry it
        Assert.True(l6.PlaceCheval(30_000, 0, 64, 2));
        Assert.Equal(40, l6.R.ChevalKg);
        Assert.Equal(l6.R.ChevalBraced, l6.BracedNear(30_000, 0));
        Assert.True(l6.Ahead(0, 20_000, 0, 1024, 0, 20_000, out int ox, out _, out int braced) > 0 && braced == l6.R.ChevalBraced);
        Assert.Equal(-1, l6.Ahead(1, 20_000, 0, 1024, 0, 20_000, out _, out _, out _));                      // the fort's own side passes
        int refused = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var (w7, _, l7) = Ring(8, seed, team: 1, chained: false);
            l7.Fort.Slots.Clear(); l7.Fort.Chains.Clear(); l7.PlaceCheval(0, 0, 64, 2);
            var rr = w7.AddMounted(fx.Kit("Rider", 0, -30_000, weapon: MountedCalibration.Lance, worn: FightCalibration.Mailed.Worn) with { Shield = "heater" },
                new MountKit { Breed = "horse_palfrey", Riding = 0, GearKg = 20, RiderKg = 80 }, true);
            for (int t = 0; t < 500; t++) { rr.Cmd.MoveX = FightMath.One; rr.Cmd.MoveY = 0; rr.Cmd.Sprint = true; w7.StepWithAi(); }
            if (w7.Events.Any(e => e.Kind == FightEventKind.Balked)) refused++;
        }
        output.WriteLine($"horses refused the cheval in {refused} of 20");
        Assert.True(refused >= 12, $"refused in {refused}");
        Assert.Equal(333, l6.FootPm(30_000, 0));
        Assert.Equal(1000, l6.FootPm(30_000, 5_000));
        long Walk(int x0)
        {
            var (w8, _, l8) = Ring(8, 4, team: 1, chained: false);
            l8.Fort.Slots.Clear(); l8.Fort.Chains.Clear(); l8.PlaceCheval(0, 0, 64, 2);
            var man = w8.Add(fx.Kit("Walker", 0, x0), true);
            for (int t = 0; t < 20; t++) { man.Cmd.MoveX = FightMath.One; w8.Step(); }
            return man.X - x0;
        }
        long over = Walk(-300), open = Walk(-20_000);
        output.WriteLine($"a second's walk: over the timber {over} mm, in the open {open} mm");
        Assert.InRange(over * 1000 / open, 250, 450);
    }

    [Fact]
    public void TheFortAndTheCarsSaveAndLoadToTheSameBytes_AndTheLayerRoundTrips()
    {
        var (w, b, layer) = Ring(8, 2);
        layer.PlaceCheval(5_000, 5_000, 32, 2);
        layer.Sortie(Chain(layer, 1).X is var x ? (int)x : 0, (int)Chain(layer, 1).Y);
        var pack = layer.Pack();
        var w2 = fx.World(2);
        var back = FortLayer.Unpack(w2, b, pack);
        Assert.Equal(pack, back.Pack());
        Assert.Same(back, w2.Fort);
        Assert.Equal(layer.Hash(0), back.Hash(0));
    }
}
