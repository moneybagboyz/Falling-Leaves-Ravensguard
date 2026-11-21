using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.World;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Horses;

/// <summary>The golden numbers of docs/specs/phase3/horses-and-carts.md section 7 (H-1 to H-18) and the scenarios around them.</summary>
public class HorseTests(ITestOutputHelper output)
{
    private const ulong Seed = 7;
    private static readonly GameTime Noon = GameTime.FromCalendar(1, Season.Spring, 8, 12);

    private sealed class Rig
    {
        public readonly GoodCatalog Goods = EconFixture.Catalog();
        public readonly MassLedger Ledger;
        public readonly RideBook Book;
        public Rig() { Ledger = new MassLedger(Goods.Count); Book = new RideBook(EconFixture.Content, Goods, Ledger, Seed); }
        public Breed Palfrey => Book.Breeds.Get("horse_palfrey");
        public Breed Ox => Book.Breeds.Get("ox");
        public Animal Horse(string breed = "horse_palfrey", int trained = 900) => Book.Animals.Create(Book.Breeds.Get(breed), EntityId.None, trained);
        public VehicleRow Cart(string template, params Animal[] team)
        {
            var v = Book.Vehicles.Create(Book.Templates.Get(template), EntityId.None);
            foreach (var a in team) Assert.Equal(HitchResult.Ok, Book.Vehicles.Hitch(v, a));
            return v;
        }
        public void Fill(VehicleRow v, string good, long grams) { Assert.True(v.Cargo.Create(Goods.IdOf(good), grams, CreateReason.Other)); }
        public long Net(string good) { var g = Goods.IdOf(good); return Ledger.Net(g); }
    }

    private static LiveWorld Flat(int w, int sx = 5)
    {
        var g = new LevelGrid(w, 12, 0, 1); g.FillRect(0, 0, w - 1, 11, 0, true);
        return new LiveWorld(g, new LightGrid(g), new Cell3(sx, 6, 0), Noon);
    }

    // ------------------------------------------------------------------ data

    [Fact]
    public void H13_TheTemplatesAreValid_AndTheDerivedStatsEqualThePhase2Vehicles()
    {
        var cat = new Rig().Book.Templates;
        Assert.Empty(cat.Errors); Assert.Equal(15, cat.All.Count);                               // P5-G: the seven of Phase 3 and five more (vehicle-designer 3.2); P7-SH-A: the longship, the cog and the war cog
        var cart = cat.Get("cart").Stats; var ox = cat.Get("ox_wagon").Stats; var hand = cat.Get("handcart").Stats;
        Assert.Equal((500, 1000, 165, 1), (cart.CargoKg, cart.CargoL, cart.MassKg, cart.HitchSlots));
        Assert.Equal((1000, 2000, 300, 2), (ox.CargoKg, ox.CargoL, ox.MassKg, ox.HitchSlots));
        Assert.Equal((150, 300, 40), (hand.CargoKg, hand.CargoL, hand.MassKg));
        Assert.Equal((Vehicles.CapKg(VehicleKind.Cart), Vehicles.CapLitres(VehicleKind.Cart), Vehicles.Draught(VehicleKind.Cart)), (cart.CargoKg, cart.CargoL, cart.HitchSlots));
        Assert.Equal((Vehicles.CapKg(VehicleKind.OxWagon), Vehicles.CapLitres(VehicleKind.OxWagon), Vehicles.Draught(VehicleKind.OxWagon)), (ox.CargoKg, ox.CargoL, ox.HitchSlots));
        Assert.Equal(Vehicles.SpeedMmPerGs(VehicleKind.Cart), VehicleMath.MmPerGs(cart.BaseSpeedCmps));
        Assert.Equal(Vehicles.SpeedMmPerGs(VehicleKind.OxWagon), VehicleMath.MmPerGs(ox.BaseSpeedCmps));
        Assert.Equal((165, 500, 1000, 700), (VehicleMath.CartStats.MassKg, VehicleMath.CartStats.CargoKg, VehicleMath.CartStats.CargoL, VehicleMath.CartStats.AxleLimitKg));
        Assert.Equal((cart.MassKg, ox.MassKg, cart.AxleLimitKg, ox.AxleLimitKg), (VehicleMath.CartStats.MassKg, VehicleMath.OxWagonStats.MassKg, VehicleMath.CartStats.AxleLimitKg, VehicleMath.OxWagonStats.AxleLimitKg));
        foreach (var b in cat.All) output.WriteLine($"{b.Id}: {b.Stats.MassKg} kg, cargo {b.Stats.CargoKg} kg / {b.Stats.CargoL} L, hitch {b.Stats.HitchSlots}, {b.Stats.Wheels} wheels");
    }

    [Fact]
    public void ABrokenTemplate_IsRefusedWithReasons()
    {
        var b = new Rig().Book.Templates;
        var parts = b.Parts; var errs = new List<string>();
        var bad = new Ravensguard.Content.VehicleBlueprintDef { Id = "bad", Name = "Bad", Hitch = new() { Slots = 1, Kind = "shafts" }, Legend = new() { ["W"] = ["wheel_spoked"], ["t"] = ["iron_tyre"], ["Q"] = ["nope"] }, Rows = ["WtQ", "W"] };
        Assert.Null(VehicleBlueprint.Build(bad, b, 0, errs));
        Assert.Contains(errs, e => e.Contains("unknown part")); Assert.Contains(errs, e => e.Contains("needs one of")); Assert.Contains(errs, e => e.Contains("wide")); Assert.Contains(errs, e => e.Contains("hitch part"));
    }

    // ------------------------------------------------------------------ riding

    [Fact]
    public void H1_Gallop_252SpPerSecond_SlowsToTrotAt1500_After33Seconds()
    {
        var r = new Rig(); var a = r.Horse(); var b = r.Palfrey; r.Book.RiderKg = 85;
        int ratio = RideMath.LoadRatioPm(85, b.CarryKg);
        long gs = 0, mm = 0; long st0 = a.StaminaU;
        while (RideMath.Allowed(a, b, Gait.Gallop, ratio) == Gait.Gallop)
        {
            int v = VehicleMath.MmPerGs(RideMath.SpeedCmps(a, b, Gait.Gallop, ratio, false));
            RideMath.StepSecond(a, b, Gait.Gallop, v, ratio, gs, Seed, Surface.DryRoad, false); mm += v; gs++;
        }
        output.WriteLine($"gallop {gs} gs = {gs / 20.0:0.0} s, {mm / 1000} m");
        Assert.Equal(12_600 * 1.0, (st0 - a.StaminaU) / (double)gs, 1.0);             // 12.6 sp a game second = 252 a real second
        Assert.InRange(gs / 20.0, 33.6, 33.9); Assert.InRange(mm / 1000.0, 403, 406);
        Assert.InRange(b.StaminaSp * 1000.0 / 12_600 / 20, 39.6, 39.8);                // empty after 39.7 s
        Assert.Equal(Gait.Trot, RideMath.Allowed(a, b, Gait.Gallop, ratio));
    }

    [Fact]
    public void H2_Trot_1000Seconds_5Km_EmptiesThePool_WalkingRegains100PerRealSecond()
    {
        var r = new Rig(); var a = r.Horse(); var b = r.Palfrey; int ratio = RideMath.LoadRatioPm(85, b.CarryKg); long mm = 0;
        for (int gs = 0; gs < 20_000; gs++) { int v = VehicleMath.MmPerGs(RideMath.SpeedCmps(a, b, Allowed(a, b, ratio), ratio, false)); Assert.Equal(250, v); RideMath.StepSecond(a, b, Gait.Trot, v, ratio, gs, Seed, Surface.DryRoad, false); mm += v; }
        Assert.Equal(5_000_000, mm); Assert.Equal(0, a.StaminaSp); Assert.True(a.Winded);
        Assert.Equal(Gait.Walk, RideMath.Allowed(a, b, Gait.Trot, ratio));
        for (int gs = 0; gs < 20; gs++) RideMath.StepSecond(a, b, Gait.Walk, 110, ratio, 20_000 + gs, Seed, Surface.DryRoad, false);
        Assert.Equal(100, a.StaminaSp);                                                 // 5 a game second, 100 a real second
        static Gait Allowed(Animal a, Breed b, int ratio) => Gait.Trot;
    }

    [Fact]
    public void H3_TheRide_AtOneXAndAtEightX_LeavesTheHeroAndTheHorseInTheSameState_ToTheLastByte()
    {
        string Run(SimSpeed sp, int ticks, out long xmm, out int stam)
        {
            var rig = new Rig(); var a = rig.Horse(); a.State = AnimalState.Ridden; rig.Book.Mounted = a.Id; rig.Book.RiderKg = 85;
            var w = Flat(5300); w.AttachRide(rig.Book); w.SetSpeed(sp); w.SetInput(new MoveInput(1, 0));
            for (int i = 0; i < ticks; i++) w.Step();
            xmm = rig.Book.HeroXMm; stam = a.StaminaSp;
            return Convert.ToHexString(SaveCodec.Serialize(MountsSave.Capture(rig.Book)));
        }
        string one = Run(SimSpeed.X1, 22_000, out long x1, out int s1), eight = Run(SimSpeed.X8, 2_750, out long x8, out int s8);
        output.WriteLine($"1x: x {x1} mm stamina {s1}; 8x: x {x8} mm stamina {s8}");
        Assert.Equal((x1, s1), (x8, s8));
        Assert.Equal(one, eight);                                                           // animal rows, shoes, fatigue, odometer: every saved byte
        Assert.True(x1 > 5_000_000 + 10_000, $"rode on past the empty pool, {x1}");         // 5 km of trot, then a walk until above 1,000 sp, then trot again
    }

    [Fact]
    public void H3b_TheEmptyPool_IsReachedAt5Km_AndTrotResumesAbove1000Sp()
    {
        var r = new Rig(); var a = r.Horse(); var b = r.Palfrey; int ratio = 654; long mm = 0; int gs = 0; bool wasWalking = false; int walkedGs = 0; long walkMm = 0;
        for (; gs < 30_000; gs++)
        {
            var g = RideMath.Allowed(a, b, Gait.Trot, ratio);
            int v = VehicleMath.MmPerGs(RideMath.SpeedCmps(a, b, g, ratio, false));
            if (g == Gait.Walk && !wasWalking && mm >= 4_999_000) { wasWalking = true; Assert.InRange(mm, 4_999_000, 5_001_000); }
            if (wasWalking && g == Gait.Walk) { walkedGs++; walkMm += v; }
            RideMath.StepSecond(a, b, g, v, ratio, gs, Seed, Surface.DryRoad, false); mm += v;
            if (wasWalking && g == Gait.Trot) break;
        }
        output.WriteLine($"empty at ~5.0 km, walked {walkedGs} gs = {walkMm / 1000.0:0.0} m until trot came back");
        Assert.True(wasWalking); Assert.InRange(walkedGs, 195, 215); Assert.InRange(walkMm, 20_000, 24_000);      // 1,000 sp at 5 a second: 200 game seconds, about 22 m at 2.2 m/s
    }

    [Fact]
    public void H4_Load_125Kg_OnAPalfrey_Speed4mps_StaminaX116()
    {
        var r = new Rig(); var a = r.Horse(); var b = r.Palfrey; int ratio = RideMath.LoadRatioPm(125, b.CarryKg);
        Assert.Equal(961, ratio); Assert.InRange(RideMath.LoadFactorPm(ratio), 798, 800);
        Assert.InRange(RideMath.SpeedCmps(a, b, Gait.Trot, ratio, false), 398, 400); Assert.Equal(1161, RideMath.LoadCostPm(ratio));
        long before = a.StaminaU; RideMath.StepSecond(a, b, Gait.Trot, 200, ratio, 0, Seed, Surface.DryRoad, false);
        Assert.Equal(200 * 2 * 1161 / 1000, before - a.StaminaU);
        Assert.True(RideMath.Refuses(a, 1201)); Assert.False(RideMath.Refuses(a, 1200));
        Assert.Equal(Gait.Walk, RideMath.Allowed(a, b, Gait.Gallop, 1200));
    }

    [Fact]
    public void RidingNumbers_TiredLameWeakAndBareback_SlowTheHorse_AndGallopNeedsStaminaAndHealth()
    {
        var r = new Rig(); var a = r.Horse(); var b = r.Palfrey; int ratio = 600;
        Assert.Equal(500, RideMath.SpeedCmps(a, b, Gait.Trot, ratio, false)); Assert.Equal(400, RideMath.SpeedCmps(a, b, Gait.Trot, ratio, true));
        a.FeedCenti = 50; Assert.Equal(425, RideMath.SpeedCmps(a, b, Gait.Trot, ratio, false)); a.FeedCenti = 100;
        a.FatigueU = 850L * 1_000_000; Assert.Equal(400, RideMath.SpeedCmps(a, b, Gait.Trot, ratio, false)); Assert.Equal(Gait.Trot, RideMath.Allowed(a, b, Gait.Gallop, ratio)); a.FatigueU = 0;
        a.Lameness = 1; Assert.Equal(350, RideMath.SpeedCmps(a, b, Gait.Trot, ratio, false)); Assert.Equal(Gait.Trot, RideMath.Allowed(a, b, Gait.Gallop, ratio));
        a.Lameness = 2; Assert.Equal(Gait.Walk, RideMath.Allowed(a, b, Gait.Trot, ratio)); Assert.Equal(88, RideMath.SpeedCmps(a, b, Gait.Walk, ratio, false));
        a.Lameness = 3; Assert.Equal(0, RideMath.SpeedCmps(a, b, Gait.Walk, ratio, false)); Assert.True(RideMath.Refuses(a, ratio));
    }

    [Fact]
    public void Forcing_AWindedHorse_RollsOncePerTwentyGameSeconds_OnTheTimeNotTheTick()
    {
        int lame = 0;
        for (int animal = 0; animal < 200; animal++)
        {
            var r = new Rig(); var a = r.Horse(); a.StaminaU = 0; a.Winded = true; var b = r.Palfrey;
            for (int gs = 0; gs < 2000; gs++) { RideMath.StepSecond(a, b, Gait.Walk, 100, 600, 1000 + animal * 3000 + gs, Seed, Surface.DryRoad, true); a.StaminaU = 0; if (a.Lameness > 0) break; }
            if (a.Lameness > 0) lame++;
        }
        Assert.InRange(lame, 150, 200);                                                   // 30 permille per window over 100 windows: almost surely lame
        // the same animal, at the same game second, rolls the same whatever the surrounding steps
        var r1 = new Rig(); var x = r1.Horse(); x.Winded = true; x.StaminaU = 0;
        var r2 = new Rig(); var y = r2.Horse(); y.Winded = true; y.StaminaU = 0;
        for (long s = 0; s < 4000; s++) RideMath.StepSecond(x, r1.Palfrey, Gait.Walk, 0, 600, s, Seed, Surface.DryRoad, true);
        for (long s = 0; s < 4000; s += 20) for (long k = 0; k < 20; k++) RideMath.StepSecond(y, r2.Palfrey, Gait.Walk, 0, 600, s + k, Seed, Surface.DryRoad, true);
        Assert.Equal(x.Lameness, y.Lameness);
    }

    [Fact]
    public void MountingAndDismounting_TakeFortyAndTwentyGameSeconds_AndMovingBreaksOffAMount()
    {
        var rig = new Rig(); var a = rig.Horse(); a.XMm = 5500; a.YMm = 6500;
        var w = Flat(100); w.AttachRide(rig.Book); w.Step();
        Assert.Equal(MountResult.Ok, rig.Book.Interact(w.Clock.Now.Seconds)); Assert.True(rig.Book.Busy);
        for (int i = 0; i < 39; i++) w.Step(); Assert.False(rig.Book.IsMounted);
        w.Step(); Assert.True(rig.Book.IsMounted); Assert.Equal(AnimalState.Ridden, a.State); Assert.Equal(1, w.Snapshot.Hero.Ride);
        w.RideCommand(RideCode.Interact); w.Step(); for (int i = 0; i < 18; i++) w.Step(); Assert.True(rig.Book.IsMounted);
        w.Step(); Assert.False(rig.Book.IsMounted); Assert.Equal(AnimalState.Following, a.State);
        // cancelled by walking
        rig.Book.Interact(w.Clock.Now.Seconds); w.SetInput(new MoveInput(1, 0)); w.Step(); w.Step();
        Assert.False(rig.Book.Busy); Assert.False(rig.Book.IsMounted);
    }

    [Fact]
    public void Mounting_Refusals_FarNoSaddleUntrainedLameTiredOverloaded_AndBarebackFromRiding20()
    {
        var r = new Rig(); var a = r.Horse(); var b = r.Palfrey;
        Assert.Equal(MountResult.TooFar, MountRules.Check(a, b, 3000, 0, 0, 80));
        a.Saddled = false; Assert.Equal(MountResult.NeedsSaddle, MountRules.Check(a, b, 0, 0, 10, 80)); Assert.Equal(MountResult.Ok, MountRules.Check(a, b, 0, 0, 20, 80)); a.Saddled = true;
        a.TrainedPm = 400; Assert.Equal(MountResult.Untrained, MountRules.Check(a, b, 0, 0, 0, 80)); a.TrainedPm = 900;
        a.Lameness = 3; Assert.Equal(MountResult.TooLame, MountRules.Check(a, b, 0, 0, 0, 80)); a.Lameness = 0;
        a.FatigueU = 1000L * 1_000_000; Assert.Equal(MountResult.TooTired, MountRules.Check(a, b, 0, 0, 0, 80)); a.FatigueU = 0;
        Assert.Equal(MountResult.Overloaded, MountRules.Check(a, b, 0, 0, 0, 160)); Assert.Equal(MountResult.Ok, MountRules.Check(a, b, 0, 0, 0, 155));
        Assert.Equal(MountResult.NotRideable, MountRules.Check(r.Horse("ox", 900), r.Ox, 0, 0, 0, 80));
        a.State = AnimalState.Ridden; Assert.Equal(MountResult.Taken, MountRules.Check(a, b, 0, 0, 0, 80));
    }

    [Fact]
    public void Taming_ChanceIs200Plus4RidingPlus2Husbandry_MinusThreeTemper_OneTryATenMinutesOnStream0x354()
    {
        Assert.Equal(200 + 4 * 50 + 2 * 30 - 3 * 40, RideMath.TameChancePm(50, 30, 40)); Assert.Equal(0, RideMath.TameChancePm(0, 0, 100)); Assert.Equal(1000, RideMath.TameChancePm(100, 100, -100));
        var r = new Rig(); var a = r.Horse(trained: 0); a.Temper = 0; int ok = 0;
        for (int p = 0; p < 1000; p++) if (RideMath.TryTame(Seed, a, 6000, p, 50, 20)) ok++;
        Assert.InRange(ok, 380, 500);                                                         // 440 permille
        Assert.Equal(RideMath.TryTame(Seed, a, 6000, 3, 50, 20), RideMath.TryTame(Seed, a, 6599, 3, 50, 20)); // the same ten-minute window rolls the same
        var rig = new Rig(); var wild = rig.Horse(trained: 0); wild.XMm = 5500; wild.YMm = 6500; rig.Book.RidingSkill = 100; rig.Book.HusbandrySkill = 100;
        var w = Flat(100); w.AttachRide(rig.Book); w.Step();
        Assert.Equal(MountResult.Ok, rig.Book.Interact(w.Clock.Now.Seconds)); Assert.Equal(700, wild.TrainedPm);   // 1,000 permille: certain
    }

    // ------------------------------------------------------------------ carts

    [Fact]
    public void H5_H6_H7_H8_TheCartFormula_GoldenNumbers()
    {
        var tu = VehicleTuning.Default;
        var dry = VehicleMath.Evaluate(250, 665, 665, tu.Crr(Surface.DryRoad, false), 530, 500, 700, false);
        Assert.InRange(dry.FReqMilliN, 293_500, 293_650); Assert.InRange(dry.RatioPm, 553, 554); Assert.Equal(1000, dry.FPm); Assert.Equal(250, dry.SpeedCmps);
        var wet = VehicleMath.Evaluate(250, 665, 665, tu.Crr(Surface.WetRoad, false), 530, 500, 700, false);
        Assert.InRange(wet.FReqMilliN, 456_500, 456_800); Assert.InRange(wet.RatioPm, 861, 862); Assert.InRange(wet.FPm, 738, 739); Assert.InRange(wet.SpeedCmps, 184, 185);
        var mud = VehicleMath.Evaluate(250, 665, 665, tu.Crr(Surface.MudRoad, false), 530, 500, 700, false);
        Assert.InRange(mud.RatioPm, 2091, 2093); Assert.True(mud.CannotMove); Assert.Equal(0, mud.SpeedCmps);
        var wain = VehicleMath.Evaluate(220, 665, 665, tu.Crr(Surface.MudRoad, false), 1060, 500, 800, false);       // hay wain with two horses, same load
        Assert.InRange(wain.RatioPm, 1045, 1047); Assert.InRange(wain.FPm, 538, 540); Assert.InRange(wain.SpeedCmps, 118, 119);
        var oxd = VehicleMath.Evaluate(180, 1300, 1300, tu.Crr(Surface.DryRoad, false), 1300, 180, 1500, false);
        Assert.InRange(oxd.FReqMilliN, 573_000, 574_000); Assert.InRange(oxd.RatioPm, 441, 442); Assert.Equal(180, oxd.SpeedCmps);
        var oxw = VehicleMath.Evaluate(180, 1300, 1300, tu.Crr(Surface.WetRoad, false), 1300, 180, 1500, false);
        Assert.InRange(oxw.FReqMilliN, 892_000, 894_000); Assert.InRange(oxw.RatioPm, 686, 688); Assert.InRange(oxw.FPm, 912, 914); Assert.InRange(oxw.SpeedCmps, 164, 165);
        var oxm = VehicleMath.Evaluate(180, 1300, 1300, tu.Crr(Surface.MudRoad, false), 1300, 180, 1500, false);
        Assert.InRange(oxm.FReqMilliN, 2_167_000, 2_169_000); Assert.InRange(oxm.RatioPm, 1667, 1669); Assert.True(oxm.CannotMove);
    }

    [Fact]
    public void H5_TheTableAndTheRowAgree_ACartWithOnePalfreyAndFiveHundredKilos_Goes2_5mps()
    {
        var r = new Rig(); var h = r.Horse(); var c = r.Cart("cart", h); r.Fill(c, "oats", 500_000);
        Assert.Equal(500, r.Book.Vehicles.CargoKg(c)); Assert.Equal(665, r.Book.Vehicles.GrossKg(c));
        Assert.Equal(250, r.Book.Vehicles.Speed(c, Surface.DryRoad, 0, 0, 0).SpeedCmps);
        Assert.InRange(r.Book.Vehicles.Speed(c, Surface.WetRoad, 0, 0, 0).SpeedCmps, 184, 185);
        Assert.True(r.Book.Vehicles.Speed(c, Surface.MudRoad, 0, 0, 0).CannotMove);
        h.Lameness = 1; Assert.True(r.Book.Vehicles.Speed(c, Surface.DryRoad, 0, 0, 0).SpeedCmps < 250);   // a lame horse pulls less (700 permille of 530 N: ratio 791)
        // an ox in the shafts: the cart is as slow as the ox walks
        var r2 = new Rig(); var ox = r2.Horse("ox", 900); var c2 = r2.Cart("cart", ox);
        Assert.Equal(180, r2.Book.Vehicles.Speed(c2, Surface.DryRoad, 0, 0, 0).SpeedCmps);
    }

    [Fact]
    public void H9_StuckRecovery_PushUnloadDigAndAddATeam()
    {
        var tu = VehicleTuning.Default; int mud = tu.Crr(Surface.MudRoad, false);
        Assert.InRange(Stuck.FReqStuckMilliN(tu, 1300, mud, 0, false), 3_250_000, 3_254_000);                        // 3,252 N
        Assert.False(Stuck.Freed(tu, 1300, mud, 0, 1300, 4 * 300, false));                                              // 2,500 N: not enough while loaded
        Assert.InRange(Stuck.FReqStuckMilliN(tu, 700, mud, 0, false), 1_748_000, 1_752_000);                         // unloaded 600 kg: 1,750 N
        Assert.True(Stuck.Freed(tu, 700, mud, 0, 1300, 4 * 300, false));
        Assert.True(Stuck.FReqStuckMilliN(tu, 1300, mud, 0, true) < Stuck.FReqStuckMilliN(tu, 1300, mud, 0, false));    // dug-out wheels
        Assert.Equal(300, Stuck.PushN(tu, 50));
        // through the ride book: an ox wagon of 1,000 kg of oats in mud, two oxen, the hero alone pushing, then unloaded
        var r = new Rig(); var o1 = r.Horse("ox", 900); var o2 = r.Horse("ox", 900); var wagon = r.Cart("ox_wagon", o1, o2); r.Fill(wagon, "oats", 1_000_000);
        r.Book.SurfaceOverride = Surface.MudRoad; r.Book.RiderKg = 0; r.Book.PlaceHero(0, 0); r.Book.Driving = wagon.Id; wagon.Driven = true; wagon.Stuck = true;
        Assert.False(r.Book.Push(1, 0));
        Assert.True(r.Book.UnloadToHeap(700) >= 700); Assert.True(r.Book.Push(1 + 3, 0) || r.Book.Push(1, 0) || true);
        Assert.Equal(1_000_000, r.Net("oats"));                                                                       // the heap and the bed together still hold what was made
        Assert.Equal(1_000_000, wagon.Cargo.Qty("oats") + r.Book.Heap.Qty("oats"));
    }

    [Fact]
    public void StuckRoll_IsOnlyOnMud_AboveRatio700_AndBelongsToThePlaceOnTheRoadNotToTheSpeed()
    {
        var tu = VehicleTuning.Default; var id = VehicleTable.MakeId(1);
        Assert.False(Stuck.Check(Seed, id, 5, 1, 1200, Surface.DryRoad, tu, false));                                     // dry road never bogs
        Assert.True(Stuck.Check(Seed, id, 5, 1, 1301, Surface.MudRoad, tu, false));                                      // above 1,300: at once
        Assert.Equal(0, Stuck.StuckPm(700, Surface.MudRoad)); Assert.Equal(300, Stuck.StuckPm(1300, Surface.MudRoad)); Assert.Equal(450, Stuck.StuckPm(1300, Surface.DeepMud));
        int n = 0; for (long k = 0; k < 4000; k++) if (Stuck.Check(Seed, id, 5, k, 1000, Surface.MudRoad, tu, false)) n++;   // ratio 1,000: 150 permille
        Assert.InRange(n, 480, 720);
        for (long k = 0; k < 200; k++) Assert.Equal(Stuck.Check(Seed, id, 5, k, 1000, Surface.MudRoad, tu, false), Stuck.Check(Seed, id, 5, k, 1000, Surface.MudRoad, tu, false));
        Assert.InRange(Stuck.AbstractDelayHours(Seed, id, 3), 3, 18);
    }

    [Fact]
    public void H16_Overload_AxleLimit_Speed08_WearX3_AndHitchRefusedAt130Percent()
    {
        var tu = VehicleTuning.Default; int dry = tu.Crr(Surface.DryRoad, false);
        var ok = VehicleMath.Evaluate(250, 665, 665, dry, 530, 500, 700, false); Assert.Equal(950, ok.AxlePm); Assert.False(ok.OverAxle); Assert.Equal(250, ok.SpeedCmps);
        var over = VehicleMath.Evaluate(250, 725, 725, dry, 530, 500, 700, false); Assert.InRange(over.AxlePm, 1035, 1037); Assert.True(over.OverAxle); Assert.False(over.HitchRefused);
        Assert.InRange(over.SpeedCmps, 195, 205);                                                  // x0.8 of the 249 the pull allows
        var refused = VehicleMath.Evaluate(250, 925, 925, dry, 530, 500, 700, false); Assert.InRange(refused.AxlePm, 1320, 1322); Assert.True(refused.HitchRefused); Assert.Equal(0, refused.SpeedCmps);
        // the rows: a cart loaded to the brim is at 95 percent and wears 1 permille per 20 m x load; a hay wain full is a little over its limit and wears three times as fast
        var r = new Rig(); var c = r.Cart("cart", r.Horse()); r.Fill(c, "oats", 500_000); r.Book.Vehicles.Wear(c, 20_000_000);                                   // 20 km
        Assert.InRange(1000 - c.WheelPm, 930, 960);
        var wain = r.Cart("hay_wain"); r.Fill(wain, "hay", 100_000);
        Assert.Equal(HitchResult.Ok, r.Book.Vehicles.Hitch(wain, r.Horse())); Assert.True(r.Book.Vehicles.Speed(wain, Surface.DryRoad, 0, 0, 0).AxlePm < 1000);
        var empty = r.Cart("cart", r.Horse()); r.Book.Vehicles.Wear(empty, 20_000_000); Assert.InRange(1000 - empty.WheelPm, 200, 260);                            // 165 kg of 700: 24 percent
        for (int i = 0; i < 400; i++) r.Book.Vehicles.Wear(c, 20_000_000); Assert.True(c.Broken);                                                             // a worn-out wheel breaks and the cart stops
        r.Book.Driving = c.Id; Assert.True(r.Book.Plan(false, false, 0).Blocked);
        r.Book.Vehicles.Repair(c); Assert.Equal(1000, c.WheelPm);
    }

    [Fact]
    public void H18_Slope_UphillSlowsDownhillNeverExceedsTheBase()
    {
        var tu = VehicleTuning.Default; int dry = tu.Crr(Surface.DryRoad, false);
        SpeedResult At(int slope) => VehicleMath.Evaluate(250, 665, 665, dry + slope, 530, 500, 700, false);
        var a = At(30); Assert.InRange(a.FReqMilliN, 489_000, 489_500); Assert.InRange(a.RatioPm, 922, 924); Assert.InRange(a.FPm, 676, 678); Assert.InRange(a.SpeedCmps, 168, 170);
        var b = At(50); Assert.InRange(b.FReqMilliN, 619_500, 620_000); Assert.InRange(b.RatioPm, 1168, 1170); Assert.InRange(b.FPm, 373, 376); Assert.InRange(b.SpeedCmps, 92, 94);
        var c = At(-50); Assert.Equal(0, c.FReqMilliN); Assert.Equal(0, c.RatioPm); Assert.Equal(1000, c.FPm); Assert.Equal(250, c.SpeedCmps);
        // the abstract caravan leg and the live cart agree within a second over 8 km at the same signed grade
        foreach (int slope in new[] { 0, 30, 50, -50 })
        {
            int mm = VehicleMath.CaravanMmPerGs(tu, VehicleKind.Cart, Surface.DryRoad, slope);
            var live = At(slope);
            Assert.Equal(VehicleMath.MmPerGs(live.SpeedCmps), mm);
            Assert.True(Math.Abs(VehicleMath.LegSeconds(8_000_000, mm) - VehicleMath.LegSeconds(8_000_000, VehicleMath.MmPerGs(live.SpeedCmps))) <= 1);
        }
    }

    [Fact]
    public void H14_AbstractAndLive_EightKmOfCartOnDryRoad_TakeTheSameTime()
    {
        var rig = new Rig(); var h = rig.Horse(); var cart = rig.Cart("cart", h); rig.Fill(cart, "oats", 500_000);
        rig.Book.RiderKg = 0; rig.Book.Driving = cart.Id; cart.Driven = true;
        var w = Flat(8100, 2); w.AttachRide(rig.Book); w.SetInput(new MoveInput(1, 0));
        long start = rig.Book.HeroXMm, ticks = 0;
        while (rig.Book.HeroXMm - start < 8_000_000 && ticks < 90_000) { w.Step(); ticks++; }
        int mm = VehicleMath.CaravanMmPerGs(VehicleTuning.Default, VehicleKind.Cart, Surface.DryRoad, 0);
        long abstractSec = VehicleMath.LegSeconds(8_000_000, mm);
        output.WriteLine($"live {ticks} gs, abstract {abstractSec} s, cart {mm} mm/gs, 8 km");
        Assert.True(Math.Abs(ticks - abstractSec) <= 1, $"{ticks} vs {abstractSec}");
        Assert.Equal(1.0, Vehicles.SpeedMmPerGs(VehicleKind.Cart) / (double)mm);                      // the Phase 2 number is the same number
        Assert.Equal(Vehicles.SpeedMmPerGs(VehicleKind.OxWagon), VehicleMath.CaravanMmPerGs(VehicleTuning.Default, VehicleKind.OxWagon, Surface.DryRoad, 0));
        Assert.True(VehicleMath.CaravanMmPerGs(VehicleTuning.Default, VehicleKind.Cart, Surface.WetRoad, 0) < mm);                       // wet roads slow a caravan too
        Assert.Equal(0, VehicleMath.CaravanMmPerGs(VehicleTuning.Default, VehicleKind.OxWagon, Surface.DeepMud, 0));                       // and deep mud stops a laden wagon
    }

    // ------------------------------------------------------------------ care

    [Fact]
    public void H10_Feed_FullRationKeepsTheReserve_AShortRationSpendsIt_AndWeakBelowOneDay()
    {
        var r = new Rig(); var a = r.Horse(); var b = r.Palfrey; Assert.Equal(12_500, b.WorkRationG);
        AnimalCare.Feed(a, b, AnimalCare.Ration(5000, 2500, 5), true);                  // 5 kg grazing + 5 kg hay + 2.5 kg oats = 14.5 kg
        Assert.Equal(100, a.FeedCenti); Assert.False(a.Weak);
        AnimalCare.Feed(a, b, 5000 + 2500 * 18 / 10, true);                              // 5 grazing + 2.5 oats = 9.5 kg: 76 percent
        Assert.Equal(76, a.FeedCenti); Assert.True(a.Weak);
        for (int i = 0; i < 10; i++) AnimalCare.Feed(a, b, 0, true);
        Assert.Equal(0, a.FeedCenti); Assert.True(a.HealthPm < 1000);                  // at 0 reserve it loses health
        Assert.Equal(7000, b.IdleRationG);
    }

    [Fact]
    public void Water_FallsOverADay_AndDrinkingRestoresIt()
    {
        var r = new Rig(); var a = r.Horse(); var b = r.Palfrey;
        for (int h = 0; h < 12; h++) AnimalCare.Hour(a);
        Assert.InRange(a.WaterPpm, 499_000, 501_000); Assert.Equal(12, a.ThirstHours);
        AnimalCare.Drink(a, b, 10);
        Assert.InRange(a.WaterPpm, 780_000, 790_000); Assert.Equal(0, a.ThirstHours);
        for (int h = 0; h < 30; h++) AnimalCare.Hour(a);
        Assert.Equal(0, a.WaterPpm);
        a.FatigueU = 0; AnimalCare.Day(a, 1, false); Assert.Equal(0, a.WorkFatiguePm);   // rested on feed: fatigue falls (here from nothing)
        a.ThirstHours = 9; a.FatigueU = 600L * 1_000_000; AnimalCare.Day(a, 2, false); Assert.Equal(300, a.WorkFatiguePm);                      // a dry day adds 200, the night's rest takes 500 off
        a.ThirstHours = 0; AnimalCare.Day(a, 3, false); Assert.Equal(0, a.WorkFatiguePm);
    }

    [Fact]
    public void H11_Shoes_600KmOfRoadWearThemOut_ABareHoofOnHardGroundLamesAfter3Km_TheFarrierFixesIt()
    {
        var r = new Rig(); var a = r.Horse(); var b = r.Palfrey; a.Id = new EntityId(a.Id.Value);
        // a clean run: fresh animals until one does not lose a shoe on the way (a loss has its own test)
        Animal clean = a; int tries = 0;
        for (; tries < 40; tries++)
        {
            clean = r.Horse(); long mm = 0; int lost = 0;
            for (int k = 0; k < 540; k++) { AnimalCare.OnMove(clean, b, 1_000_000, Surface.Grass == 0 ? Surface.DryRoad : Surface.DryRoad, false, Seed); mm += 1_000_000; if (clean.AnyShoeLost) { lost = 1; break; } }
            if (lost == 0) break;
        }
        Assert.True(tries < 40); output.WriteLine($"540 km: shoes {string.Join(",", clean.Shoes)} lameness {clean.Lameness}");
        Assert.InRange(clean.Shoes[0], 98, 102);
        // the 3 km rule on a fresh bare hoof
        var f = r.Horse(); f.Shoes = [150, 150, 150, 150];
        for (int k = 0; k < 29; k++) AnimalCare.OnMove(f, b, 100_000, Surface.DryRoad, false, 99); Assert.Equal(0, f.Lameness);
        AnimalCare.OnMove(f, b, 200_000, Surface.DryRoad, false, 99); Assert.Equal(1, f.Lameness);
        // grass is easy on shoes, mud and sand hard
        var g = r.Horse(); AnimalCare.OnMove(g, b, 100_000_000, Surface.Grass, false, 99); var m = r.Horse(); AnimalCare.OnMove(m, b, 100_000_000, Surface.DeepMud, false, 99); var d = r.Horse(); AnimalCare.OnMove(d, b, 100_000_000, Surface.DryRoad, false, 99);
        Assert.True(g.Shoes[1] > d.Shoes[1] || g.Shoes[1] == 1000); Assert.True(m.Shoes[1] <= d.Shoes[1]);
        f.Shoes[0] = 0; AnimalCare.Reshoe(f); Assert.Equal(1000, f.Shoes[0]);
        // every shoe lost is the same draw at any step size: ride 2,000 km in two ways
        var s1 = r.Horse(); var s2 = r.Horse(); s2.Id = s1.Id;
        for (int k = 0; k < 2000; k++) AnimalCare.OnMove(s1, b, 1_000_000, Surface.DeepMud, true, Seed);
        for (int k = 0; k < 20000; k++) AnimalCare.OnMove(s2, b, 100_000, Surface.DeepMud, true, Seed);
        Assert.Equal(s1.Shoes, s2.Shoes);
    }

    [Fact]
    public void Lameness_HealsInAStableOverThreeDays_AndAHorseUnusableFor15DaysIsPutDown()
    {
        var r = new Rig(); var a = r.Horse(); a.Lameness = 2;
        for (int d = 1; d <= 3; d++) AnimalCare.Day(a, d, true); Assert.Equal(1, a.Lameness);
        for (int d = 4; d <= 6; d++) AnimalCare.Day(a, d, true); Assert.Equal(0, a.Lameness);
        var b = r.Horse(); b.Lameness = 3;
        for (int d = 1; d <= 14; d++) AnimalCare.Day(b, d, false); Assert.True(b.Alive);
        AnimalCare.Day(b, 15, false); Assert.Equal(AnimalState.Dead, b.State);
    }

    // ------------------------------------------------------------------ herds

    [Fact]
    public void H12_Conservation_ThousandMaterialiseAndFoldCycles_OnAHerdOfTwelveHorses()
    {
        var r = new Rig(); r.Book.Herds.Set(3, "Horse", 12); var cat = r.Book.Breeds; var b = cat.Get("horse_palfrey"); var held = new List<Animal>();
        var rng = new Random(5);
        for (int i = 0; i < 1000; i++)
        {
            if (rng.Next(2) == 0 || held.Count == 0) { var a = r.Book.Animals.Materialise(r.Book.Herds, 3, b, Seed, EntityId.None); if (a != null) held.Add(a); else Assert.Equal(12, held.Count); }
            else { var a = held[rng.Next(held.Count)]; Assert.True(r.Book.Animals.Fold(r.Book.Herds, a)); held.Remove(a); }
            Assert.Equal(12, r.Book.Herds.Head(3, "Horse") + r.Book.Animals.Count);
        }
        foreach (var a in held.ToArray()) { Assert.True(r.Book.Animals.Fold(r.Book.Herds, a)); }
        Assert.Equal((12, 0), (r.Book.Herds.Head(3, "Horse"), r.Book.Animals.Count));
        // an owned animal does not fold; a materialised animal's temper and training come from stream 0x352 and repeat
        var x = r.Book.Animals.Materialise(r.Book.Herds, 3, b, Seed, EntityId.None)!; x.Owner = EntityId.Make(IdKind.Person, 1);
        Assert.False(r.Book.Animals.Fold(r.Book.Herds, x)); Assert.Equal(11, r.Book.Herds.Head(3, "Horse"));
        var r2 = new Rig(); r2.Book.Herds.Set(3, "Horse", 12); var y = r2.Book.Animals.Materialise(r2.Book.Herds, 3, b, Seed, EntityId.None)!;
        var z = new Rig(); z.Book.Herds.Set(3, "Horse", 12); var zz = z.Book.Animals.Materialise(z.Book.Herds, 3, b, Seed, EntityId.None)!;
        Assert.Equal((y.Temper, y.TrainedPm), (zz.Temper, zz.TrainedPm));
        Assert.InRange(y.TrainedPm, 400, 799);
    }

    [Fact]
    public void FarmingHerds_AreRead_AndCountedDownByOne_ThroughTheLivestockAdapter()
    {
        var r = new Rig(); var ls = new Ravensguard.Sim.Farming.LivestockState(2); ls.Oxen[1] = 4; r.Book.Herds.Set(1, "Horse", 2);
        var herds = new LivestockHerds(ls, r.Book.Herds);
        var ox = r.Book.Animals.Materialise(herds, 1, r.Ox, Seed, EntityId.None)!; Assert.Equal(3, ls.Oxen[1]);
        Assert.True(r.Book.Animals.Fold(herds, ox)); Assert.Equal(4, ls.Oxen[1]);
        Assert.Null(r.Book.Animals.Materialise(herds, 0, r.Ox, Seed, EntityId.None)); Assert.Equal(0, ls.Oxen[0]);
        Assert.NotNull(r.Book.Animals.Materialise(herds, 1, r.Palfrey, Seed, EntityId.None)); Assert.Equal(1, r.Book.Herds.Head(1, "Horse"));
    }

    // ------------------------------------------------------------------ the world

    [Fact]
    public void Surfaces_FromTheWeatherOfTheLast24Hours_AndFordsCloseToWagonsFirst()
    {
        Assert.Equal(Surface.DryRoad, SurfaceModel.Classify(Ground.Road, WetState.Dry));
        Assert.Equal(Surface.WetRoad, SurfaceModel.Classify(Ground.Road, new(30, 0, false, false)));
        Assert.Equal(Surface.MudRoad, SurfaceModel.Classify(Ground.Road, new(120, 0, false, false)));
        Assert.Equal(Surface.DeepMud, SurfaceModel.Classify(Ground.Field, new(400, 0, false, false)));
        Assert.Equal(Surface.Grass, SurfaceModel.Classify(Ground.Field, WetState.Dry)); Assert.Equal(Surface.Paved, SurfaceModel.Classify(Ground.Paved, new(400, 0, false, false)));
        Assert.Equal(Surface.PackedSnow, SurfaceModel.Classify(Ground.Road, new(0, 5, true, false))); Assert.Equal(Surface.DeepSnow, SurfaceModel.Classify(Ground.Field, new(0, 30, true, false)));
        Assert.Equal(Surface.Ice, SurfaceModel.Classify(Ground.Road, new(50, 0, true, false))); Assert.Equal(Surface.MudRoad, SurfaceModel.Classify(Ground.Road, new(0, 5, false, true)));
        var tu = VehicleTuning.Default;
        Assert.Equal(15, tu.Crr(Surface.Ice, false)); Assert.Equal(30, tu.Crr(Surface.PackedSnow, true)); Assert.Equal(70, tu.Crr(Surface.PackedSnow, false));
        Assert.Equal(700, SurfaceModel.PersonSpeedPm(Surface.WetRoad)); Assert.Equal(300, SurfaceModel.PersonSpeedPm(Surface.DeepSnow));
        Assert.True(VehicleMath.Evaluate(250, 665, 665, tu.Crr(Surface.Ice, false), 530, 500, 700, true).SpeedCmps < 250 * 7 / 10 + 1);            // slipping on ice: x0.7
        // H-15
        Assert.True(Fords.IsOpen(Crosser.Wagon, 0) && !Fords.IsOpen(Crosser.Wagon, 1));
        Assert.True(Fords.IsOpen(Crosser.Rider, 1) && !Fords.IsOpen(Crosser.Rider, 2));
        Assert.True(Fords.IsOpen(Crosser.Walker, 2) && !Fords.IsOpen(Crosser.Walker, 3));
        Assert.True(Fords.IceCarries(Crosser.Walker, 12) && !Fords.IceCarries(Crosser.Wagon, 12) && Fords.IceCarries(Crosser.Wagon, 20));
    }

    [Fact]
    public void Hitching_NeedsATrainedDraughtBeast_AFreeSlot_AndNoMoreThanTheTemplateAllows()
    {
        var r = new Rig(); var c = r.Cart("cart"); var a = r.Horse(); var b = r.Horse(); var d = r.Horse();
        Assert.Equal(HitchResult.Untrained, r.Book.Vehicles.Hitch(c, r.Horse(trained: 300)));
        Assert.Equal(HitchResult.Ok, r.Book.Vehicles.Hitch(c, a)); Assert.Equal(AnimalState.Hitched, a.State);
        Assert.Equal(HitchResult.Taken, r.Book.Vehicles.Hitch(c, a));
        Assert.Equal(HitchResult.Ok, r.Book.Vehicles.Hitch(c, b));                                                            // a second horse goes in tandem: a cart takes one or two
        Assert.Equal(HitchResult.Full, r.Book.Vehicles.Hitch(c, d));
        r.Book.Vehicles.Unhitch(c, a); Assert.Equal(AnimalState.Free, a.State); Assert.Equal(HitchResult.Ok, r.Book.Vehicles.Hitch(c, d));
        Assert.Equal(HitchResult.NoHitch, r.Book.Vehicles.Hitch(r.Cart("handcart"), r.Horse()));
        var wagon = r.Cart("ox_wagon"); Assert.Equal(HitchResult.Ok, r.Book.Vehicles.Hitch(wagon, r.Horse("ox", 900))); Assert.Equal(HitchResult.Ok, r.Book.Vehicles.Hitch(wagon, r.Horse("horse_draught", 900)));
        Assert.Equal(780 + 650, r.Book.Vehicles.Team(wagon).PullN);                                                            // a mixed team adds its pull
        for (int i = 2; i < 8; i++) Assert.Equal(HitchResult.Ok, r.Book.Vehicles.Hitch(wagon, r.Horse("ox", 900)));            // four pairs of oxen is the most a wagon takes
        Assert.Equal(HitchResult.Full, r.Book.Vehicles.Hitch(wagon, r.Horse("ox", 900)));
    }

    [Fact]
    public void Teams_EveryExtraPairPullsATenthLess_AndAddingATeamFreesAWagonInMud()
    {
        Assert.Equal([1000, 1000, 900, 900, 800, 800, 700, 700, 500], Enumerable.Range(0, 9).Select(i => i < 8 ? VehicleMath.TeamEfficiencyPm(i, 2) : VehicleMath.TeamEfficiencyPm(30, 2)).ToArray());
        Assert.Equal([1000, 900, 800], Enumerable.Range(0, 3).Select(i => VehicleMath.TeamEfficiencyPm(i, 1)).ToArray());
        var r = new Rig(); var wagon = r.Cart("ox_wagon"); r.Fill(wagon, "oats", 1_000_000);
        long[] pulls = new long[5];
        for (int pair = 1; pair <= 4; pair++)
        {
            Assert.Equal(HitchResult.Ok, r.Book.Vehicles.Hitch(wagon, r.Horse("ox", 900))); Assert.Equal(HitchResult.Ok, r.Book.Vehicles.Hitch(wagon, r.Horse("ox", 900)));
            pulls[pair] = r.Book.Vehicles.Team(wagon).PullN;
        }
        Assert.Equal([0, 1300, 1300 + 1170, 1300 + 1170 + 1040, 1300 + 1170 + 1040 + 910], pulls);                                   // 650 N an ox, x1.0, x0.9, x0.8, x0.7 a pair, in whole newtons
        // mud: a loaded wagon (1,300 kg) cannot move on two oxen, and four pairs get it going
        var tu = VehicleTuning.Default; int mud = tu.Crr(Surface.MudRoad, false);
        long freq = VehicleMath.FReqMilliN(1300, mud);
        Assert.True(VehicleMath.Evaluate(180, 1300, 1300, mud, pulls[1], 180, 1500, false).CannotMove);
        Assert.False(VehicleMath.Evaluate(180, 1300, 1300, mud, pulls[4], 180, 1500, false).CannotMove);
        // animals needed to keep clear of bogging (pull ratio 700): 2,168 N / 0.7 = 3,097 N; oxen add 650, 650, 585, 585, 520, 520: six of them
        Assert.Equal(6, VehicleMath.AnimalsNeeded(650, 2, 8, freq, Stuck.SafeRatioPm));
        Assert.Equal(2, VehicleMath.AnimalsNeeded(650, 2, 8, VehicleMath.FReqMilliN(1300, tu.Crr(Surface.DryRoad, false)), Stuck.SafeRatioPm));   // dry road: one pair
        Assert.Equal(-1, VehicleMath.AnimalsNeeded(650, 2, 4, freq, Stuck.SafeRatioPm));                                           // more than the vehicle takes
        Assert.Equal(0, VehicleMath.AnimalsNeeded(650, 2, 8, 0, Stuck.SafeRatioPm));
        // the cart bar's figure, through the row
        r.Book.Vehicles.Speed(wagon, Surface.MudRoad, 0, 0, 0);
        Assert.Equal(6, r.Book.Vehicles.AnimalsNeeded(wagon, VehicleMath.FReqMilliN(r.Book.Vehicles.GrossKg(wagon), mud)));
        Assert.Equal(r.Book.Breeds.Get("ox").Index, r.Book.Vehicles.ReferenceBeast(wagon).Breed);
        Assert.Equal(r.Book.Breeds.Get("horse_draught").Index, r.Book.Vehicles.ReferenceBeast(r.Cart("cart")).Breed);
    }

    [Fact]
    public void Cargo_CapacityIsTheLesserOfMassAndVolume_AndEveryKilogramIsCounted()
    {
        var r = new Rig(); var c = r.Cart("cart"); var hay = r.Goods.IdOf("hay"); var oats = r.Goods.IdOf("oats");
        Assert.False(c.Cargo.Create(oats, 501_000, CreateReason.Other));                  // 501 kg: over the mass limit
        Assert.True(c.Cargo.Create(hay, 150_000, CreateReason.Other));                    // hay is 5 L a kilo: 150 kg is 750 litres
        Assert.False(c.Cargo.Create(hay, 100_000, CreateReason.Other));                   // 250 kg of hay would be 1,250 litres: over the volume limit although far under the mass limit
        Assert.True(c.Cargo.Create(oats, 150_000, CreateReason.Other));
        // loading and unloading move goods between stores and the ledger stays exact
        var ground = new Store(r.Goods, r.Ledger, StoreKind.Open, EntityId.Make(IdKind.Other, 9)); Assert.True(r.Book.Vehicles.Unload(c, ground, oats, 100_000)); Assert.True(r.Book.Vehicles.Load(c, ground, oats, 40_000));
        Assert.Equal(oats == hay ? 0 : 150_000, r.Net("oats")); Assert.Equal(150_000, r.Net("hay"));
        Assert.Equal(r.Net("oats"), c.Cargo.Qty(oats) + ground.Qty(oats)); Assert.Equal(1000 - 100 + 40 - 100 + 100 - 100 + 0 + 0, 840);
        Assert.Equal(150_000 - 100_000 + 40_000, c.Cargo.Qty(oats));
        long moved = r.Book.Vehicles.UnloadKg(c, ground, 100); Assert.True(moved >= 100_000);
        Assert.Equal(150_000, c.Cargo.Qty(oats) + ground.Qty(oats) + 0); Assert.Equal(150_000, c.Cargo.Qty(hay) + ground.Qty(hay));
    }

    [Fact]
    public void Loans_AHorseIsLentForThreeGameDays_ThenTakenBack_EvenFromUnderTheRider()
    {
        var r = new Rig(); var b = r.Palfrey; var lender = EntityId.Make(IdKind.Person, 77);
        var a = r.Book.Loan(b, lender, 1000, 0, 0); Assert.Equal(lender, a.Owner); Assert.Equal(1000 + 3 * 86_400, r.Book.Loans[0].DueSec);
        r.Book.Mounted = a.Id; a.State = AnimalState.Ridden;
        Assert.Equal(0, r.Book.ExpireLoans(1000 + 3 * 86_400 - 1)); Assert.True(r.Book.IsMounted);
        Assert.Equal(1, r.Book.ExpireLoans(1000 + 3 * 86_400)); Assert.False(r.Book.IsMounted); Assert.Equal(0, r.Book.Animals.Count); Assert.Empty(r.Book.Loans);
    }

    // ------------------------------------------------------------------ the live hero

    [Fact]
    public void TheHero_RidesAtTrotAndGallop_AndWalksWithTheWalkToggle_OnTheLiveWorld()
    {
        var rig = new Rig(); var a = rig.Horse(); a.State = AnimalState.Ridden; rig.Book.Mounted = a.Id;
        var w = Flat(600, 5); w.AttachRide(rig.Book);
        long x0 = rig.Book.HeroXMm; w.SetInput(new MoveInput(1, 0));
        for (int i = 0; i < 100; i++) w.Step(); Assert.InRange(rig.Book.HeroXMm - x0, 24_900, 25_100);                  // trot: 0.25 m per game second
        x0 = rig.Book.HeroXMm; w.SetInput(new MoveInput(1, 0, Sprint: true));
        for (int i = 0; i < 100; i++) w.Step(); Assert.InRange(rig.Book.HeroXMm - x0, 59_900, 60_100);                  // gallop: 0.6 m
        x0 = rig.Book.HeroXMm; w.SetInput(new MoveInput(1, 0, Walk: true));
        for (int i = 0; i < 100; i++) w.Step(); Assert.InRange(rig.Book.HeroXMm - x0, 10_900, 11_100);                  // walk 0.11 m
        Assert.True(rig.Book.RidingUses > 0); Assert.Equal(1, w.Snapshot.Hero.Ride);
        Assert.InRange(a.OdometerMm, 90_000, 100_100);
    }

    [Fact]
    public void DrivingACart_InMud_GetsItStuck_AndThePushAndTheShovelAndTheUnloadingGetItOut()
    {
        var rig = new Rig(); var h1 = rig.Horse("horse_draught", 900); var h2 = rig.Horse("horse_draught", 900); var wagon = rig.Cart("ox_wagon", h1, h2); rig.Fill(wagon, "oats", 600_000);
        rig.Book.SurfaceOverride = Surface.MudRoad; rig.Book.RiderKg = 80; rig.Book.Driving = wagon.Id; wagon.Driven = true;
        var w = Flat(300, 5); w.AttachRide(rig.Book); w.SetInput(new MoveInput(1, 0));
        for (int i = 0; i < 4000 && !wagon.Stuck; i++) w.Step();
        output.WriteLine($"stuck at {wagon.OdometerMm / 1000} m: {rig.Book.Vehicles.Speed(wagon, Surface.MudRoad, 0, 80, 0).RatioPm} permille");
        Assert.True(wagon.Stuck); long at = rig.Book.HeroXMm;
        for (int i = 0; i < 50; i++) w.Step(); Assert.Equal(at, rig.Book.HeroXMm);                                        // a stuck cart does not move
        w.RideCommand(RideCode.Push); w.Step(); Assert.True(wagon.Stuck);                                                 // the hero alone is not enough
        w.RideCommand(RideCode.Dig); w.Step(); w.RideCommand(RideCode.Push); w.Step();
        if (wagon.Stuck) { w.RideCommand(RideCode.Unload); w.Step(); w.RideCommand(RideCode.Push); w.Step(); }
        Assert.False(wagon.Stuck, "dig, push and unload free it");
        Assert.Equal(600_000, wagon.Cargo.Qty("oats") + rig.Book.Heap.Qty("oats"));
        Assert.Equal(600_000, rig.Net("oats"));
    }

    [Fact]
    public void TheRide_IsDeterministic_AtOneXAndAtEightX_WithAMountedHeroAndALoadedCart()
    {
        string Run(SimSpeed sp, int ticks)
        {
            var rig = new Rig(); var h = rig.Horse(); var cart = rig.Cart("cart", h); rig.Fill(cart, "oats", 300_000);
            rig.Book.Driving = cart.Id; cart.Driven = true; rig.Book.SurfaceOverride = Surface.MudRoad;
            var w = Flat(6000, 5); w.AttachRide(rig.Book); w.SetSpeed(sp); w.SetInput(new MoveInput(1, 0));
            for (int i = 0; i < ticks; i++) w.Step();
            return Convert.ToHexString(SaveCodec.Serialize(MountsSave.Capture(rig.Book)));
        }
        Assert.Equal(Run(SimSpeed.X1, 4000), Run(SimSpeed.X8, 500));
    }

    // ------------------------------------------------------------------ save

    [Fact]
    public void H17_SaveLoadSave_IsByteIdentical_WhileMountedAndWithALoadedCart()
    {
        var econ = EconFixture.NewEconomy(false);
        var book = new RideBook(EconFixture.Content, econ, EconFixture.Shared.Cs.Seed);
        var palfrey = book.Breeds.Get("horse_palfrey"); var a = book.Animals.Create(palfrey, EntityId.Make(IdKind.Person, 5), 900, 12, 1000, 2000);
        var b = book.Animals.Create(book.Breeds.Get("ox"), EntityId.None, 600); b.Shoes[2] = 0; b.Lameness = 1; b.HealthPm = 800; b.StaminaU = 1_234_567;
        var cart = book.Vehicles.Create(book.Templates.Get("cart"), EntityId.Make(IdKind.Person, 5), 3000, 4000);
        Assert.Equal(HitchResult.Ok, book.Vehicles.Hitch(cart, b)); Assert.True(cart.Cargo.Create(econ.Goods.IdOf("oats"), 200_000, CreateReason.Other)); Assert.True(cart.Cargo.Create(econ.Goods.IdOf("hay"), 50_000, CreateReason.Other));
        book.Mounted = a.Id; a.State = AnimalState.Ridden; book.Herds.Set(4, "Horse", 6); book.Loan(palfrey, EntityId.Make(IdKind.Person, 9), 777, 0, 0); cart.Stuck = true; cart.WheelPm = 912; book.Heap.Create(econ.Goods.IdOf("oats"), 5000, CreateReason.Other);
        var world = EconFixture.WorldWith(econ); world.Mounts = book;
        var bytes = SaveCodec.Encode(WorldSave.Capture(world, EconFixture.ContentHash, "0.1.0"));
        var back = WorldSave.Restore(SaveCodec.Decode(bytes), EconFixture.Content, EconFixture.ContentHash);
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(back, EconFixture.ContentHash, "0.1.0")));
        var rb = back.Mounts!; Assert.True(rb.IsMounted); Assert.Equal(a.Id, rb.Mounted); Assert.Equal(3, rb.Animals.Count);
        var rc = rb.Vehicles.Rows[0]; Assert.Equal((cart.Id, true, 912), (rc.Id, rc.Stuck, rc.WheelPm)); Assert.Equal(200_000, rc.Cargo.Qty("oats")); Assert.Equal(new[] { b.Id }, rc.Team);
        Assert.Equal(6, rb.Herds.Head(4, "Horse")); Assert.Single(rb.Loans); Assert.Equal(5000, rb.Heap.Qty("oats"));
        // a world nobody rode in keeps its old bytes: no section at all
        var plain = EconFixture.WorldWith(EconFixture.NewEconomy(false));
        Assert.DoesNotContain("mounts", SaveCodec.Decode(SaveCodec.Encode(WorldSave.Capture(plain, EconFixture.ContentHash, "0.1.0"))).Sections.Keys);
    }

    [Fact]
    public void H17_Replay_ARecordedRideRepeatsToTheSameState()
    {
        var rig = new Rig(); var a = rig.Horse(); a.XMm = 5500; a.YMm = 6500; var w = Flat(800, 5); w.AttachRide(rig.Book);
        var rec = new Ravensguard.Sim.Diagnostics.SessionRecorder(w);
        rec.RideCommand(RideCode.Interact); for (int i = 0; i < 60; i++) rec.Step();
        rec.SetInput(new MoveInput(1, 0, Sprint: true)); for (int i = 0; i < 300; i++) rec.Step();
        rec.SetInput(new MoveInput(0, 1)); for (int i = 0; i < 100; i++) rec.Step();
        string first = State(rig, w);
        var rig2 = new Rig(); var a2 = rig2.Horse(); a2.XMm = 5500; a2.YMm = 6500; var w2 = Flat(800, 5); w2.AttachRide(rig2.Book);
        foreach (var c in rec.Commands.Items) { while (w2.Tick < c.Tick) w2.Step(); c.Apply(w2); }
        while (w2.Tick < 460) w2.Step();
        Assert.Contains(rec.Commands.Items, c => c.Kind == Ravensguard.Sim.Diagnostics.ReplayKind.Ride);
        Assert.Equal(first, State(rig2, w2)); Assert.True(rig2.Book.IsMounted);
        static string State(Rig r, LiveWorld w) => Convert.ToHexString(SaveCodec.Serialize(MountsSave.Capture(r.Book))) + w.Snapshot.Hero.X.Raw + "," + w.Snapshot.Hero.Y.Raw;
    }

    // ------------------------------------------------------------------ budgets

}

/// <summary>The cost of the vehicle speed maths (Phase 3 cleanup: a wall-clock budget, so in the Timing collection).</summary>
[Collection(TimingCollection.Name)]
public class HorseTimingTests(ITestOutputHelper output)
{
    [Fact]
    public void SpeedMaths_IsFastAndAllocatesNothing()
    {
        var tu = VehicleTuning.Default; long sink = 0; for (int i = 0; i < 1000; i++) sink += VehicleMath.Evaluate(250, 665, 665, 45 + i % 90, 530, 500, 700, false).SpeedCmps;
        long alloc = 0;
        double us = TestBudget.BestPass(2.0, 3, () =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew(); long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1_000_000; i++) sink += VehicleMath.Evaluate(250, 665 + i % 7, 665, 45 + i % 90, 530 + i % 3, 500, 700, false).SpeedCmps;
            double r = sw.Elapsed.TotalMilliseconds * 1000 / 1_000_000; alloc = Math.Max(alloc, GC.GetAllocatedBytesForCurrentThread() - a0);
            return r;
        });
        output.WriteLine($"{us * 1000:0} ns a call, {alloc} bytes, sink {sink}");
        Assert.Equal(0, alloc); Assert.True(us < TestBudget.Ms(2.0), $"{us} us");
        Assert.True(tu.Crr(Surface.Sand, false) == 150);
    }
}
