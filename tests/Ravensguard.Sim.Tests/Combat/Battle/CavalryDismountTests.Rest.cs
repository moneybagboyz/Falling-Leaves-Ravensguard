using Ravensguard.Content;
using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Combat.Melee2;

namespace Ravensguard.Sim.Tests.Combat.Battle;

// CV-B, the second half: the captain's remount, CV-8 kicks, CV-9 horse archers, CV-12 fallen horses, CV-14 determinism.
public partial class CavalryDismountTests
{
    [Fact]
    public void CV7_ACaptainDoesNotDismountAnUnhurtLine_AndMountsAgainWhenNoEnemyIsNear()
    {
        // an unhurt line of men-at-arms with a foe at 60 m keeps its saddles (it charges)
        {
            var w = fx.World(11);
            var ids = MenAtArms(w, 16, tactics: 60);
            var foe = Foot(w, 20, 60_000, -5000, spear: true, perRank: 5);
            var book = w.EnsureFormations();
            var cav = book.Form(w, 0, 1, FormationKind.Cavalry, ids, 0, 0, 1024, 0, Formation.Line, 0, 500, place: true);
            var enemy = book.Form(w, 1, 1, FormationKind.Infantry, foe, 60_000, 0, -1024, 0, Formation.Line, 4, 500, place: true);
            cav.CaptainAlways = true; enemy.Micro = int.MaxValue / 2;
            book.Apply(w, enemy, FormationOrder.Of(OrderKind.Hold), w.Tick + 1);
            for (int t = 0; t < 200; t++) w.StepWithAi();
            Assert.Equal(FormationBook.FootNone, cav.Foot);
            Assert.Equal(FormationKind.Cavalry, cav.Kind);
        }
        // dismounted by order with nobody about, the captain mounts them again by himself
        {
            var w = fx.World(12);
            var ids = MenAtArms(w, 8, tactics: 60);
            var book = w.EnsureFormations();
            var cav = book.Form(w, 0, 1, FormationKind.Cavalry, ids, 0, 0, 1024, 0, Formation.Line, 0, 500, place: true);
            book.Apply(w, cav, FormationOrder.Of(OrderKind.Dismount), w.Tick + 1);
            StepUntil(w, () => cav.Foot == FormationBook.FootOn, 200, "the dismount");
            cav.CaptainAlways = true;
            StepUntil(w, () => cav.Foot == FormationBook.FootNone && cav.Kind == FormationKind.Cavalry, 3000, "the captain's remount");
            Assert.All(ids, id => Assert.True(M(w, id).Mounted));
            output.WriteLine($"the captain had them mounted again at tick {w.Tick}");
        }
    }

    // ---------------------------------------------------------------- CV-8 kicks

    [Fact]
    public void CV8_Kicks_ATrainedCourserKicksAManBehindIt_OnceEach3s_ABluntBlowOf28800Milli_AtTheLowerOrUpperBody()
    {
        var w = fx.World(21); w.Kicks = true;
        var rider = w.AddMounted(fx.Kit("R", 0, 0, weapon: FightFixture.Sword, worn: FightCalibration.Mailed.Worn) with { Courage = 100 }, Mount(trained: 900), true);
        var man = w.Add(fx.Kit("man", 1, -1000, 0, weapon: FightFixture.Sword, worn: FightCalibration.Mailed.Worn, skill: 30) with { Courage = 100 }, true);
        var m = w.Riding(rider)!;
        var blows = new List<(int Tick, StrikeV2 S, long Initial, string Part)>();
        w.OnBlow = (a, t, res, s) => { if (a.Id == m.Horse && t.Id == man.Id) blows.Add((w.Tick, s, res.InitialMilli, man.Plan.Parts[s.TargetPart].BaseId)); };
        for (int t = 0; t < 600 && man.Active; t++) w.StepWithAi();
        var kicks = w.Events.Where(e => e.Kind == FightEventKind.Kicked).ToList();
        output.WriteLine($"{kicks.Count} kicks at ticks {string.Join(", ", kicks.Select(k => k.Tick))}; parts {string.Join(", ", blows.Select(b => b.Part))}");
        Assert.True(kicks.Count >= 4, $"kicks {kicks.Count}");
        for (int i = 1; i < kicks.Count; i++) Assert.InRange(kicks[i].Tick - kicks[i - 1].Tick, 60, 64);          // once each 3 s
        Assert.Equal(blows.Count, kicks.Count);
        Assert.All(blows, b =>
        {
            Assert.Equal(60L * m.Gaits.MassKg, b.S.ExtraMomentumMilli);                                             // M = 60 x kg milli: 28,800 for a 480 kg courser
            Assert.Equal(28_800L, b.S.ExtraMomentumMilli);
            Assert.Equal(AttackType.Blunt, b.S.Weapon.Attacks[b.S.Attack].Type);
            Assert.Equal(60, b.S.Weapon.Attacks[b.S.Attack].Def.ContactArea);
            Assert.Contains(b.Part, new[] { "lower_body", "upper_body" });
        });
        Assert.Contains(blows, b => b.Initial > 0);                                                                // a real resolver result
    }

    [Fact]
    public void CV8_Kicks_OnlyWarTrainedOrPanickedHorses_OnlyInTheRearArc_AndOnlyInABattle()
    {
        int Kicks(int trained, int panic, int manX, int manY, bool? force = true)
        {
            var w = fx.World(22); w.Kicks = force;
            var rider = w.AddMounted(fx.Kit("R", 0, 0, weapon: FightFixture.Sword, worn: FightCalibration.Mailed.Worn) with { Courage = 100 }, Mount(trained: trained), true);
            w.Add(fx.Kit("man", 1, manX, manY, weapon: FightFixture.Sword, worn: FightCalibration.Mailed.Worn) with { Courage = 100 }, true);
            for (int t = 0; t < 200; t++) { var pm = w.Mounts!.PairOf(rider.Id)!; pm.PanicPm = Math.Max(pm.PanicPm, panic); w.StepWithAi(); }
            return w.Events.Count(e => e.Kind == FightEventKind.Kicked);
        }
        Assert.True(Kicks(900, 0, -1000, 0) > 0);                    // war trained
        Assert.True(Kicks(600, 650, -1000, 0) > 0);                  // panicked (a horse of any training)
        Assert.Equal(0, Kicks(700, 0, -1000, 0));                    // neither
        Assert.Equal(0, Kicks(900, 0, 1000, 0));                     // in front, not behind
        Assert.Equal(0, Kicks(900, 0, -1000, 1000));                 // 45 degrees off, 1.41 m away: out of reach
        Assert.Equal(0, Kicks(900, 0, -1500, 0));                    // behind but beyond 1.2 m
        Assert.True(Kicks(900, 0, -800, 700) > 0);                   // within the 90 degree rear arc
        Assert.Equal(0, Kicks(900, 0, -1000, 0, force: false));      // switched off
        Assert.Equal(0, Kicks(900, 0, -1000, 0, force: null));       // a Phase 4 fight (no formation book): off by default
    }

    [Fact]
    public void CV8_ATrainedDestrierBitesAManPressingItsRider_OnceEach5s_AnEdgelessBlowOf8xKgMilli()
    {
        var w = fx.World(23); w.Kicks = true;
        var rider = w.AddMounted(fx.Kit("R", 0, 0, weapon: FightFixture.Sword, worn: FightCalibration.Mailed.Worn) with { Courage = 100 }, Mount(trained: 950), true);
        var man = w.Add(fx.Kit("man", 1, 1000, 0, weapon: FightFixture.Sword, worn: FightCalibration.Mailed.Worn, skill: 30) with { Courage = 100 }, true);
        man.Brain.Target = rider.Id;                                                                              // he is pressing the rider
        var m = w.Riding(rider)!;
        var blows = new List<StrikeV2>();
        w.OnBlow = (a, t, res, s) => { if (a.Id == m.Horse && t.Id == man.Id && s.Weapon.Shape.Id == "mounted.bite") blows.Add(s); };
        for (int t = 0; t < 600 && man.Active; t++) w.StepWithAi();
        var bites = w.Events.Where(e => e.Kind == FightEventKind.Bitten).ToList();
        output.WriteLine($"{bites.Count} bites at {string.Join(", ", bites.Select(b => b.Tick))}");
        Assert.True(bites.Count >= 3);
        for (int i = 1; i < bites.Count; i++) Assert.InRange(bites[i].Tick - bites[i - 1].Tick, 100, 104);
        Assert.All(blows, s => { Assert.Equal(8L * m.Gaits.MassKg, s.ExtraMomentumMilli); Assert.Equal(AttackType.Blunt, s.Weapon.Attacks[0].Type); Assert.Equal(20, s.Weapon.Attacks[0].Def.ContactArea); });
        Assert.DoesNotContain(w.Events, e => e.Kind == FightEventKind.Kicked);                                        // he is in front: no kick
    }

    // ---------------------------------------------------------------- CV-9 horse archers

    private FighterKit Bow(string name, int team, int x, int y, int archery = 60, int ammo = 24, string launcher = "longbow", string arrow = "bodkin_arrow", int tactics = -1) =>
        new() { Name = name, Team = team, X = x, Y = y, Launcher = launcher, Ammo = arrow, AmmoCount = ammo, Worn = [new WornItem("tunic", "linen"), new WornItem("gambeson", "linen")], Skill = 40, ArcherySkill = archery, Courage = 100, Tactics = tactics, FaceX = team == 0 ? 1024 : -1024 };

    [Theory]
    [InlineData(0, 10)]
    [InlineData(50, 6)]
    [InlineData(100, 3)]
    [InlineData(150, 0)]
    public void CV9_TheAimAtATrot_IsMinus10TimesOneMinusRidingOver150_AndWidensTheShotsConeByTheSame(int riding, int points)
    {
        Assert.Equal(-points, ChargeMath.AimPoints(5000, false, riding));                                       // the spec's own function
        var w = fx.World(31);
        var r = w.AddMounted(Bow("B", 0, 0, 0), Mount(riding), true);
        var m = w.Riding(r)!;
        m.Speed = 2000; Assert.Equal(1000, w.MountedSigma(r, 1000));                                             // at a walk: no penalty
        m.Speed = 5000; Assert.Equal(1000 + 30 * points, w.MountedSigma(r, 1000));                               // at a trot: 3% a point
        m.Speed = 11_000; Assert.Equal(1000 + 30 * (20 * (150 - riding) / 150), w.MountedSigma(r, 1000));        // a gallop is worse still (the player may shoot then; the AI does not)
        var foot = w.Add(Bow("F", 0, 5000, 0), true);
        Assert.Equal(777, w.MountedSigma(foot, 777));                                                            // a man on foot is untouched
    }

    private (FightWorld W, FormationRow Row, FormationRow Foe, List<int> Archers, List<int> Foot) ArcherBattle(int archers = 15, int foot = 30, int gap = 100_000, ulong seed = 41, int tactics = 50, int foeBows = 0, int riding = 50)
    {
        var w = fx.World(seed);
        var ids = new List<int>();
        for (int i = 0; i < archers; i++) ids.Add(w.AddMounted(Bow($"H{i}", 0, 0, (i - archers / 2) * 5000, tactics: i == 0 ? tactics : -1), Mount(riding)).Id);
        var f = Foot(w, foot, gap, -((foot / 10) * 500), spear: true, perRank: 10);
        var book = w.EnsureFormations();
        var row = book.Form(w, 0, 1, FormationKind.Cavalry, ids, 0, 0, 1024, 0, Formation.Loose, 0, 500, place: true);
        var foe = book.Form(w, 1, 1, FormationKind.Infantry, f, gap, 0, -1024, 0, Formation.Line, 3, 500, place: true);
        row.CaptainAlways = true;
        book.Apply(w, foe, FormationOrder.Of(OrderKind.Advance), w.Tick + 1);
        foe.Micro = int.MaxValue / 2;
        if (foeBows > 0)
        {
            var bows = new List<int>();
            for (int i = 0; i < foeBows; i++) bows.Add(w.Add(Bow($"B{i}", 1, gap + 4000, (i - foeBows / 2) * 1500, archery: 55) with { Weapon = FightFixture.Sword }).Id);
            var brow = book.Form(w, 1, 2, FormationKind.Archers, bows, gap + 4000, 0, -1024, 0, Formation.Line, 2, 500, place: true);
            brow.Micro = int.MaxValue / 2;
            book.Apply(w, brow, FormationOrder.Of(OrderKind.Advance), w.Tick + 1);
        }
        return (w, row, foe, ids, f);
    }

    private static long NearestFoeMm(FightWorld w, int rider)
    {
        var r = w.Fighters[rider]; long bd = long.MaxValue;
        foreach (var e in w.Fighters) if (e.Team != r.Team && e.Active && !w.IsHorse(e)) bd = Math.Min(bd, w.DistMm(r, e));
        return bd;
    }

    [Fact]
    public void CV9_HorseArchers_KeepSixtyToOneTwentyMetresFromFoot_WheelAwayAt40m_AndNeverShootAtAGallop()
    {
        var (w, row, foe, archers, foot) = ArcherBattle();
        var shots = new List<(int Tick, int Speed, int Sigma)>();
        w.OnEvent = e => { if (e.Kind == FightEventKind.Shot && w.Mounts!.PairOf(e.Actor) is { Mounted: true } p) shots.Add((e.Tick, p.Speed, e.A)); };
        var trace = new List<long>();
        long worst = long.MaxValue; int wheeled = 0; bool wasInside = false;
        for (int t = 0; t < 2400; t++)
        {
            w.StepWithAi();
            if (t % 10 == 0)
            {
                long near = archers.Where(a => w.Fighters[a].Active).Min(a => NearestFoeMm(w, a));
                if (near == long.MaxValue) continue;
                trace.Add(near); worst = Math.Min(worst, near);
                bool inside = near < 40_000; if (inside && !wasInside) wheeled++; wasInside = inside;
            }
        }
        int inBand = trace.Count(d => d is >= 55_000 and <= 125_000);
        output.WriteLine($"{shots.Count} shots; nearest foe to any archer: min {worst / 1000.0:0.0} m, in the band {inBand}/{trace.Count}; entered 40 m {wheeled} times; foot down {foot.Count(i => !w.Fighters[i].Active)}; archers down {archers.Count(i => !w.Fighters[i].Active)}; order {row.Order.Kind}");
        Assert.True(shots.Count > 60, $"shots {shots.Count}");
        Assert.All(shots, s => Assert.True(s.Speed <= 5500, $"a shot at {s.Speed} mm/s"));                    // a walk or a trot
        Assert.True(inBand * 100 >= trace.Count * 80, $"in the band {inBand}/{trace.Count}");
        Assert.True(worst >= 15_000, $"closest {worst}");                                                         // the foot never reached them
        Assert.Equal(0, archers.Count(i => !w.Fighters[i].Active));
        Assert.True(foot.Count(i => !w.Fighters[i].Active) > 0, "arrows told");
    }

    [Fact]
    public void CV9_FoeAt35m_TheFormationWheelsAwayToTheBand_AndTurnsToFace()
    {
        var (w, row, foe, archers, foot) = ArcherBattle(gap: 35_000);
        w.Formations!.Apply(w, foe, FormationOrder.Of(OrderKind.Hold), w.Tick + 1);                           // the foot stand
        long d0 = FightMath.Len(row.AnchorX - foe.AnchorX, row.AnchorY - foe.AnchorY);
        for (int t = 0; t < 400; t++) w.StepWithAi();
        long d1 = FightMath.Len(row.AnchorX - foe.AnchorX, row.AnchorY - foe.AnchorY);
        output.WriteLine($"anchor to anchor {d0 / 1000.0:0.0} m -> {d1 / 1000.0:0.0} m, order {row.Order.Kind}, facing ({row.FaceX},{row.FaceY})");
        Assert.InRange(d1, 70_000, 125_000);
        Assert.True(row.FaceX > 500);                                                                          // turned back to face them
    }

    [Fact]
    public void CV9_ACaptainOfTactics10_GivesNoSkirmishOrder_TheArchersAreNotTheSkirmishersOfThisRule()
    {
        var (w, row, foe, archers, foot) = ArcherBattle(tactics: 10);
        bool skirmish = false;
        for (int t = 0; t < 900; t++) { w.StepWithAi(); if (row.Order.Kind == OrderKind.MoveTo && row.Order.Arg == 2) skirmish = true; }
        Assert.False(skirmish);
    }

    [Fact]
    public void CV9_WorkedExample_HorseArchersAgainstALineOfSpearmen_WithAndWithoutArchersOfTheirOwn()
    {
        (int FootDown, int ArchersDown, int Shots, int Hits, long Closest) Run(int foeBows)
        {
            var (w, row, foe, archers, foot) = ArcherBattle(foeBows: foeBows, seed: 43);
            long closest = long.MaxValue; int shots = 0, hits = 0;
            w.OnEvent = e => { if (e.Kind == FightEventKind.Shot && archers.Contains(e.Actor)) shots++; if (e.Kind == FightEventKind.ProjectileHit && archers.Contains(e.Actor)) hits++; };
            for (int t = 0; t < 2400; t++)
            {
                w.StepWithAi();
                if (t % 20 == 0) closest = Math.Min(closest, archers.Where(a => w.Fighters[a].Active).Select(a => NearestFoeMm(w, a)).DefaultIfEmpty(long.MaxValue).Min());
            }
            return (foot.Count(i => !w.Fighters[i].Active), archers.Count(i => !w.Fighters[i].Active), shots, hits, closest);
        }
        var a = Run(0); var b = Run(12);
        output.WriteLine($"horse archers v 30 spearmen: {a.FootDown} spearmen down, {a.ArchersDown} archers down, {a.Shots} shots, {a.Hits} hits, closest approach {a.Closest / 1000.0:0.0} m");
        output.WriteLine($"horse archers v 30 spearmen + 12 foot archers: {b.FootDown} spearmen down, {b.ArchersDown} archers down, {b.Shots} shots, {b.Hits} hits, closest approach {b.Closest / 1000.0:0.0} m");
        Assert.True(a.Hits > 0 && a.FootDown > 0);
        Assert.Equal(0, a.ArchersDown);
    }

    [Fact]
    public void CV9_AMountedCrossbowmanGetsDownToSpanHisBow_AndRidesAgainWhenLoaded()
    {
        var w = fx.World(51);
        var cb = w.AddMounted(Bow("X", 0, 0, 0, launcher: "crossbow", arrow: "quarrel", ammo: 6), Mount(), false);
        w.Add(fx.Kit("t", 1, 90_000, 0) with { Courage = 100 }, true);
        var m = w.Riding(cb)!;
        cb.Loaded = false;
        int down = -1, up = -1;
        for (int t = 0; t < 1500 && up < 0; t++)
        {
            w.StepWithAi();
            if (down < 0 && !m.Mounted) down = w.Tick;
            if (down >= 0 && m.Mounted) up = w.Tick;
        }
        output.WriteLine($"got down at {down}, up again at {up}; shots {w.Events.Count(e => e.Kind == FightEventKind.Shot)}");
        Assert.True(down > 0 && up > down);
        Assert.Contains(w.Events, e => e.Kind == FightEventKind.Dismounted && e.Actor == cb.Id);
        Assert.Contains(w.Events, e => e.Kind == FightEventKind.Reloaded && e.Actor == cb.Id);                  // spanned on foot
        Assert.True(up - down >= 200 + 40);                                                                     // the reload is 10 s, then the mount
    }

    // ---------------------------------------------------------------- CV-12 a fallen horse is an obstacle

    private (int Falls, int Jumps) Gallop(int trained, int riding, int runs, int baseSeed = 1000)
    {
        int falls = 0, jumps = 0;
        for (int k = 0; k < runs; k++)
        {
            var w = fx.World((ulong)(baseSeed + k));
            var down = w.AddMounted(fx.Kit("D", 0, 60_000, 0, weapon: FightFixture.Sword) with { Courage = 100 }, Mount(), true);
            var dm = w.Riding(down)!; var dh = w.Fighters[dm.Horse];
            dh.Act = FAct.Dead; dh.FX = 1024; dh.FY = 0;                                                       // a horse lying across the path, its rider thrown
            dm.Mounted = false; dm.Phase = MountedFighter.PhFoot;
            w.Fighters[down.Id].Act = FAct.Dead;
            var rider = w.AddMounted(fx.Kit("G", 0, 0, 0, weapon: FightFixture.Sword) with { Courage = 100 }, Mount(riding, trained), true);
            var m = w.Riding(rider)!;
            m.HX = 1024; m.HY = 0;
            for (int t = 0; t < 300; t++)
            {
                rider.Cmd.MoveX = 1024; rider.Cmd.MoveY = 0; rider.Cmd.Sprint = true;
                w.StepWithAi();
            }
            if (w.Events.Any(e => e.Kind == FightEventKind.HorseFell && e.Actor == m.Horse && e.A == 3)) falls++;
            if (w.Events.Any(e => e.Kind == FightEventKind.Jumped && e.Actor == m.Horse)) jumps++;
        }
        return (falls, jumps);
    }

    [Fact]
    public void CV12_AFallenHorseInThePath_AGalloperFallsOrJumpsByStream0x790_AndATrainedHorseOfRiding40Jumps()
    {
        var plain = Gallop(500, 30, 100);
        output.WriteLine($"untrained (500), Riding 30: {plain.Falls} falls, {plain.Jumps} jumps in 100 gallops");
        Assert.Equal(100, plain.Falls + plain.Jumps);
        Assert.InRange(plain.Falls, 15, 45);                                                                    // 300 permille
        var trained = Gallop(900, 40, 60);
        Assert.Equal((0, 60), trained);                                                                         // trained 900 with Riding 40 jumps it
        var green = Gallop(900, 39, 100);
        Assert.InRange(green.Falls, 15, 45);                                                                    // Riding 39: not enough
        var lowTrain = Gallop(899, 80, 100);
        Assert.InRange(lowTrain.Falls, 15, 45);
    }

    [Fact]
    public void CV12_ATrotterCannotGoThroughAFallenHorse_ItSlidesAlongTheBody()
    {
        var w = fx.World(61);
        var down = w.AddMounted(fx.Kit("D", 0, 30_000, 0, weapon: FightFixture.Sword) with { Courage = 100 }, Mount(), true);
        var dm = w.Riding(down)!; var dh = w.Fighters[dm.Horse];
        dh.Act = FAct.Dead; dh.FX = 1024; dh.FY = 0; dm.Mounted = false; dm.Phase = MountedFighter.PhFoot; w.Fighters[down.Id].Act = FAct.Dead;
        var rider = w.AddMounted(fx.Kit("T", 0, 0, 50, weapon: FightFixture.Sword) with { Courage = 100 }, Mount(), true);
        var m = w.Riding(rider)!;
        bool entered = false;
        for (int t = 0; t < 500; t++)
        {
            rider.Cmd.MoveX = 1024; rider.Cmd.MoveY = 0; rider.Cmd.Sprint = false;
            w.StepWithAi();
            long ax = Math.Abs(rider.X - dh.X), ay = Math.Abs(rider.Y - dh.Y);
            if (ax <= 1000 && ay <= 500) entered = true;
        }
        output.WriteLine($"trotter ended at ({rider.X}, {rider.Y}) against the horse at ({dh.X}, {dh.Y}); entered {entered}");
        Assert.False(entered);
        Assert.True(m.Mounted);                                                                                 // and it did not fall
    }

    // ---------------------------------------------------------------- CV-14 determinism

    /// <summary>400 against 400 with 120 horses: men-at-arms who dismount, lancers who charge, horse archers who skirmish, kicks and fallen horses.</summary>
    private FightWorld Battle(ulong seed = 70)
    {
        var w = fx.World(seed); w.RecordEvents = false;
        var book = w.EnsureFormations();
        // side 0: 20 men-at-arms (told to dismount), 40 horse archers, 340 foot
        var maa = MenAtArms(w, 20, 0, -60_000, -20_000);
        var ha = new List<int>(); for (int i = 0; i < 20; i++) ha.Add(w.AddMounted(Bow($"H{i}", 0, -80_000, (i - 10) * 4000, tactics: i == 0 ? 50 : -1), Mount(50 + i % 7)).Id);
        var r0 = book.Form(w, 0, 1, FormationKind.Cavalry, maa, -60_000, 0, 1024, 0, Formation.Line, 0, 500, place: true);
        var r1 = book.Form(w, 0, 2, FormationKind.Cavalry, ha, -80_000, 0, 1024, 0, Formation.Loose, 0, 500, place: true);
        r1.CaptainAlways = true; r0.Micro = int.MaxValue / 2;
        for (int k = 0; k < 4; k++)
        {
            var ids = Foot(w, 90, -20_000 - k * 2000, -42_500, team: 0, skill: 40, perRank: 18, spear: k % 2 == 1);
            var r = book.Form(w, 0, (byte)(3 + k), FormationKind.Infantry, ids, -20_000, (k - 2) * 20_000, 1024, 0, Formation.Line, 4, 400, place: true);
            r.CaptainAlways = true;
        }
        // side 1: 20 lancers, 360 foot
        var lancers = new List<int>();
        for (int i = 0; i < 20; i++)
        {
            var kit = fx.Kit($"L{i}", 1, 150_000, (i - 10) * 1800, weapon: MountedCalibration.Lance, skill: 50, worn: FightCalibration.Mailed.Worn, faceX: -1024) with { Shield = "heater", Courage = 100, Tactics = i == 0 ? 50 : -1 };
            lancers.Add(w.AddMounted(kit, Mount(50 + i % 5, 900)).Id);
        }
        var rl = book.Form(w, 1, 1, FormationKind.Cavalry, lancers, 150_000, 0, -1024, 0, Formation.Line, 0, 500, place: true);
        rl.CaptainAlways = true;
        for (int k = 0; k < 4; k++)
        {
            var ids = Foot(w, 95, 40_000 + k * 2000, -42_500, team: 1, skill: 40, perRank: 19, spear: k % 2 == 0);
            var r = book.Form(w, 1, (byte)(2 + k), FormationKind.Infantry, ids, 40_000, (k - 2) * 20_000, -1024, 0, Formation.Line, 4, 400, place: true);
            r.CaptainAlways = true;
        }
        return w;
    }

    private static void Drive(FightWorld w, int ticks)
    {
        for (int t = 0; t < ticks; t++)
        {
            if (w.Tick == 250) w.Formations!.Apply(w, w.Formations.Rows[0], FormationOrder.Of(OrderKind.Dismount), w.Tick + 1);
            if (w.Tick == 900) w.Formations!.Apply(w, w.Formations.Rows[0], FormationOrder.Of(OrderKind.Mount), w.Tick + 1);
            w.StepWithAi();
        }
    }

    [Fact]
    public void CV14_Determinism_400v400With120Horse_SameHashesAcrossRuns_AndAcrossASaveAndLoadAtAnyTick()
    {
        const int Total = 1400;
        (ulong, ulong, ulong) Run() { var w = Battle(); Drive(w, Total); return (w.Hash(), w.MountedHash(), w.MoraleHash()); }
        Assert.Equal(Run(), Run());
        var full = Battle(); Drive(full, Total);
        Assert.Equal(120, full.Mounts!.Pairs.Count * 2);
        int people = full.Fighters.Count - full.Mounts.Pairs.Count;
        output.WriteLine($"{people} soldiers and {full.Mounts.Pairs.Count} horses; horses down {full.Mounts.Pairs.Count(p => full.Fighters[p.Horse].Act is FAct.Down or FAct.Dead)}; pairs with a CV-B state {full.Mounts.Pairs.Count(p => p.HasCvb)}; kicking horses {full.Mounts.Pairs.Count(p => p.KickAt != MountedFighter.NoTick)}");
        Assert.Equal(800, people);
        foreach (int at in new[] { 60, 240, 255, 300, 520, 880, 905, 1000 })
        {
            var part = Battle(); Drive(part, at);
            var back = FightRecord.FromBytes(FightRecord.Capture(part).ToBytes()).Restore(fx.C);
            Assert.Equal(part.Formations!.Pack(), back.Formations!.Pack());
            Assert.Equal((part.Hash(), part.MountedHash(), part.MoraleHash()), (back.Hash(), back.MountedHash(), back.MoraleHash()));
            Drive(back, Total - at);
            Assert.True((full.Hash(), full.MountedHash(), full.MoraleHash()) == (back.Hash(), back.MountedHash(), back.MoraleHash()), $"a save at tick {at} drifted");
            Assert.Equal(full.Formations!.Pack(), back.Formations!.Pack());
        }
    }

    [Fact]
    public void CV14_AFightWithNoDismountingKicksOrArchers_SavesWithNoCvbTrailer_AndTheCavalryBookFormatIsCVAs()
    {
        var w = fx.World(80);
        var ids = MenAtArms(w, 6, trained: 700);
        foreach (int id in ids) Assert.False(M(w, id).HasCvb);
        var tail = w.MountedTail(w.Fighters[ids[0]]);
        Assert.DoesNotContain(0x43564231L, tail);                                                              // no CV-B trailer when nothing of it is in use
        var book = w.EnsureFormations();
        book.Form(w, 0, 1, FormationKind.Cavalry, ids, 0, 0, 1024, 0, Formation.Line, 0, 500, place: true);
        Assert.Equal(2, book.Pack()[0]);                                                                       // the CV-A format
    }
}
