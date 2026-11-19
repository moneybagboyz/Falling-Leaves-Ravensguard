using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Save;
using Ravensguard.Content;
using Ravensguard.Sim.Tests.Combat.Resolver2;

namespace Ravensguard.Sim.Tests.Combat.Wounds;

/// <summary>Wounds, bleeding, pain and healing (wounds-bleeding-pain-healing.md 8: WB-1 to WB-16).</summary>
[Collection("resolver2")]
public class WoundTests(V2Fixture f)
{
    private BodyPlanV2 H => f.Human;
    private const long Day = 86_400;

    private WoundBook Book(ulong seed = 1, ulong key = 7) => new(seed, key, 5000, 10, "human") { RegenPm = 0 };

    private WoundV2 Struct(string part, string sid, WoundFlags fl, int rateMl10)
    {
        int p = f.P(part); var pt = H.Parts[p];
        int si = Array.FindIndex(pt.Structures, s => s.Id == sid); Assert.True(si >= 0, sid);
        return new WoundV2 { Part = (short)p, Tissue = (short)pt.Structures[si].Tissue.Index, Structure = (short)si, Organ = -1, Kind = WoundKind.Cut, DepthPm = 1000, BleedX10 = rateMl10, Flags = fl };
    }
    private WoundV2 Skin(string part, int rate10 = 5)
    {
        int p = f.P(part); var l = H.Parts[p].Layers[0];
        return new WoundV2 { Part = (short)p, Tissue = (short)l.Tissue.Index, Structure = -1, Organ = -1, Kind = WoundKind.Cut, DepthPm = 300, BleedX10 = rate10 };
    }
    private WoundV2 Muscle(string part, int rate10, byte grade = 1, WoundKind k = WoundKind.Cut)
    {
        int p = f.P(part); var l = H.Parts[p].Layers.First(x => x.IsMuscle);
        return new WoundV2 { Part = (short)p, Tissue = (short)l.Tissue.Index, Structure = -1, Organ = -1, Kind = k, DepthPm = 600, Grade = grade, BleedX10 = rate10 };
    }
    private WoundV2 Bone(string part, byte grade, WoundFlags extra = 0)
    {
        int p = f.P(part); var pt = H.Parts[p];
        var l = pt.Layers.FirstOrDefault(x => x.IsBone);
        return new WoundV2 { Part = (short)p, Tissue = (short)(l?.Tissue.Index ?? 0), Structure = -1, Organ = -1, Kind = WoundKind.Crush, DepthPm = 1000, Grade = grade, Flags = WoundFlags.BoneBroken | extra };
    }
    private WoundV2 Severed(string part) => new() { Part = (short)f.P(part), Tissue = (short)H.Parts[f.P(part)].Layers[0].Tissue.Index, Structure = -1, Organ = -1, Kind = WoundKind.Cut, DepthPm = 1000, BleedX10 = 900, Flags = WoundFlags.Severed };
    private void Hit(WoundBook b, long sec, params WoundV2[] w) => b.Apply(H, w, sec, sec, 0, 0, false);

    /// <summary>The tick (game second) at which the predicate first holds, stepping one tick at a time like the live tier.</summary>
    private static long First(WoundBook b, Func<WoundBook, bool> p, long max = 20_000)
    {
        for (long t = b.LastSec + 1; t <= max; t++) { b.AdvanceTo(t); if (p(b)) return t; }
        return -1;
    }
    private static double S(long tick) => tick / 20.0;

    [Theory]
    [InlineData(1000, 15.0, 20.0, 25.0)]
    [InlineData(800, 18.75, 25.0, 31.25)]
    [InlineData(1200, 12.5, 16.7, 20.8)]
    public void WB1_Artery_DownsAndKillsOnTheGoldenTimings(int rate10, double col, double unc, double dead)
    {
        var b = Book(); Hit(b, 0, Struct("upper_leg_l", "femoral_artery", WoundFlags.ArteryHit, rate10));
        Assert.InRange(S(First(b, x => x.State != Consciousness.Awake)), col - 0.1, col + 0.1);
        Assert.InRange(S(First(b, x => x.State is Consciousness.Unconscious or Consciousness.Dying)), unc - 0.1, unc + 0.1);
        Assert.InRange(S(First(b, x => x.Dead)), dead - 0.1, dead + 0.1);
        Assert.Equal(DeathKind.BloodLoss, b.Death);
    }

    [Fact]
    public void WB1_ArmVein_DownsIn110Seconds()
    {
        var b = Book(); Hit(b, 0, Struct("upper_arm_l", "brachial_vein", WoundFlags.VeinHit, 150));
        Assert.InRange(S(First(b, x => x.State != Consciousness.Awake, 40_000)), 99.9, 100.1);
        Assert.InRange(S(First(b, x => x.State is Consciousness.Unconscious or Consciousness.Dying, 40_000)), 133.1, 133.5);
        Assert.InRange(S(First(b, x => x.Dead, 40_000)), 166.5, 166.8);
    }

    [Fact]
    public void WB2_Clotting_MuscleCutLosesAbout390AndNeverCollapses()
    {
        var b = Book(); Hit(b, 0, Muscle("upper_leg_l", 30));
        Assert.InRange(b.RateMilliAt(90), 1400, 1500);                       // 1.5 ml/s at 90 s
        b.AdvanceTo(600 * 20);
        long lost = (b.BloodMaxUl - b.BloodUl) / 1000;
        Assert.InRange(lost, 380, 400);
        Assert.Equal(Consciousness.Awake, b.State);
    }

    [Fact]
    public void WB3_Pressure_Bandage_Tourniquet_RatesAndStaleness()
    {
        var art = Struct("upper_arm_l", "brachial_artery", WoundFlags.ArteryHit, 1000);
        var b = Book(); Hit(b, 0, art);
        Assert.Equal(100_000, b.RateMilliAt(0));
        Assert.True(Treatment.Perform(b, H, new(TreatKind.Pressure, f.P("upper_arm_l"), 0)).Ok);
        Assert.Equal(100_000, b.RateMilliAt(3));                              // bites after 4 sim-s
        Assert.Equal(20_000, b.RateMilliAt(4));
        Assert.True(b.RateMilliAt(100) < 6_000);                               // a dressed wound seals
        var t = Book(); Hit(t, 0, art);
        var bag = new ItemBag().Add("rope", 1);
        Assert.True(Treatment.Perform(t, H, new(TreatKind.Tourniquet, f.P("upper_arm_l"), 20), bag).Ok);
        Assert.Equal(2_000, t.RateMilliAt(1));
        Assert.False(Treatment.Perform(Book(), H, new(TreatKind.Tourniquet, f.P("upper_body"), 0), new ItemBag().Add("rope", 1)).Ok);
        var bb = Book(); Hit(bb, 0, art);
        Assert.False(Treatment.Perform(bb, H, new(TreatKind.Bandage, -1, 20), new ItemBag()).Ok);   // no bandage in hand
        Assert.True(Treatment.Perform(bb, H, new(TreatKind.Bandage, -1, 20), new ItemBag().Add("bandage", 1)).Ok);
        Assert.Equal(20_000, bb.RateMilliAt(1));
    }

    [Fact]
    public void WB4_Pain_KnocksOutAboveThresholdAndWakesBelowSeventyPercent()
    {
        var b = Book();
        Hit(b, 0, Severed("upper_arm_r"), Bone("upper_leg_l", 3));
        Assert.Equal(8500, b.PainX100);                                         // 45 + 40 stays under T = 100
        Assert.True(b.Conscious || b.State == Consciousness.Collapsed);
        Assert.Equal(0, b.Latch & 4);
        Hit(b, 0, Muscle("upper_leg_r", 0, 3, WoundKind.Crush));                // + mangled muscle 20 = 105
        Assert.NotEqual(0, b.Latch & 4);
        Assert.True(b.State is Consciousness.Unconscious or Consciousness.Dying);
        // the pool alone: from 130 to below 70 takes 31 sim-seconds
        var c = Book(); c.PainX100 = 13_000; c.AdvanceTo(1); Assert.NotEqual(0, c.Latch & 4);
        c.AdvanceTo(30 * 20); Assert.NotEqual(0, c.Latch & 4);
        c.AdvanceTo(32 * 20); Assert.Equal(0, c.Latch & 4);
        c.AdvanceTo(60 * 20); Assert.Equal(Consciousness.Awake, c.State);
    }

    [Fact]
    public void WB5_HeartWound_DownAtSixSecondsDeadAtTwentyOne()
    {
        var b = Book();
        int p = f.P("upper_body"); int oi = Array.FindIndex(H.Parts[p].Organs, o => o.Id == "heart");
        Hit(b, 0, new WoundV2 { Part = (short)p, Tissue = (short)H.Parts[p].Organs[oi].Tissue.Index, Structure = -1, Organ = (short)oi, Kind = WoundKind.Cut, DepthPm = 1000, BleedX10 = 1200, Flags = WoundFlags.OrganHit | WoundFlags.Vital });
        Assert.Equal(6.0, S(First(b, x => (x.Down & DownReason.Heart) != 0)), 1);
        Assert.InRange(S(First(b, x => x.Dead)), 20.7, 20.9);
        Assert.Equal(DeathKind.Organ, b.Death);
    }

    [Fact]
    public void WB6_Concussion_KnockOutThirtyFourSecondsThenDazed()
    {
        var b = Book();
        var w = Bone("skull", 2, WoundFlags.Concussion);
        b.Apply(H, [w], 0, 0, 0, 100_000, false);
        long until = b.UnconsciousUntilSec;
        Assert.InRange(until / 20.0, 30.6, 37.4);                              // 34 s +-10%
        Assert.True(b.State is Consciousness.Unconscious or Consciousness.Dying);
        Assert.Equal(1 + 2, b.ConcussedUntilDay);                               // dazed 1 + grade days
        b.AdvanceTo(until + 400); Assert.Equal(Consciousness.Dazed, b.State);
        b.DailyStep(3); Assert.Equal(0, b.ConcussedUntilDay);
    }

    [Fact]
    public void WB7_Fracture_SetHealsInTwentyDaysUnsetInThirtyFiveWithMalunion()
    {
        int Heal(bool set)
        {
            var b = Book(); Hit(b, 0, Bone("upper_arm_l", 2));
            if (set) { for (ulong a = 0; a < 50 && !b.Records[0].Is(Aid.Set); a++) Treatment.Perform(b, H, new(TreatKind.SetBone, -1, 100 + (long)a * 20, 100, a + 1)); Assert.True(b.Records[0].Is(Aid.Set)); }
            for (int d = 1; d <= 60; d++) { b.DailyStep(d); if (b.Records[0].State == WoundState.Scar) return d; }
            return -1;
        }
        Assert.Equal(20, Heal(true));
        Assert.Equal(35, Heal(false));
        int mal = 0; const int N = 10_000;
        for (ulong s = 0; s < N; s++)
        {
            var b = Book(s); Hit(b, 0, Bone("upper_arm_l", 2));
            for (int d = 1; d <= 35; d++) b.DailyStep(d);
            if (b.Permanent.Malunions > 0) { mal++; Assert.Equal(2, b.Permanent.StrengthLoss); }
        }
        Assert.InRange(mal * 100.0 / N, 48.5, 51.5);
        var sh = Book(); Hit(sh, 0, Bone("upper_leg_l", 4)); for (int d = 1; d <= 60; d++) sh.DailyStep(d);
        Assert.Equal(2, sh.Permanent.DexterityLoss);                            // shattered: 100% malunion, a leg costs dexterity
    }

    [Fact]
    public void WB8_Tendon_RepairAtSkill50WithinADaySucceedsFortyPercent_AfterADayNever()
    {
        int ok = 0, late = 0; const int N = 10_000;
        for (ulong s = 0; s < N; s++)
        {
            var b = Book(s); Hit(b, 0, Struct("upper_arm_l", "biceps_tendon", WoundFlags.TendonHit, 0));
            var r = Treatment.Perform(b, H, new(TreatKind.TendonRepair, -1, 3600, 50, 9));
            if (r.Ok && r.Success) ok++;
            if (Treatment.Perform(b, H, new(TreatKind.TendonRepair, -1, Day + 100, 100, 9)).Ok) late++;
        }
        Assert.InRange(ok * 100.0 / N, 38.5, 41.5);
        Assert.Equal(0, late);
        var low = Book(); Hit(low, 0, Struct("upper_arm_l", "biceps_tendon", WoundFlags.TendonHit, 0));
        Assert.False(Treatment.Perform(low, H, new(TreatKind.TendonRepair, -1, 100, 49)).Ok);
        Assert.True(low.Function(H).Parts[f.P("upper_arm_l")].TendonCut);
    }

    [Fact]
    public void WB9_NerveAndCord_NeverHeal_PermanentParalysisAndNumbness()
    {
        var b = Book(); Hit(b, 0, Struct("upper_arm_l", "arm_motor", WoundFlags.MotorNerveHit, 0), Struct("upper_arm_r", "arm_sensory", WoundFlags.SensoryNerveHit, 0));
        var back = Struct("lower_body", "cord_back_l", WoundFlags.SpineHit, 0); Hit(b, 0, back);
        for (int d = 1; d <= 400; d++) b.DailyStep(d);
        Assert.All(b.Records, r => Assert.NotEqual(WoundState.Scar, r.State));
        var bs = b.Function(H);
        Assert.True(bs.Parts[f.P("hand_l")].Paralysed);
        Assert.True(bs.Parts[f.P("hand_r")].Numb);
        Assert.Equal(850, bs.BlowMomentumPm[1]);                                // a numb hand strikes weaker
        Assert.Equal(100, bs.WalkSpeedPm);                                      // the cord: legs paralysed, crawl
        // abstract and live agree: one big step and tick steps
        var l = Book(); Hit(l, 0, Struct("upper_arm_l", "arm_motor", WoundFlags.MotorNerveHit, 0));
        var a = Book(); Hit(a, 0, Struct("upper_arm_l", "arm_motor", WoundFlags.MotorNerveHit, 0));
        for (int d = 1; d <= 5; d++) { l.DailyStep(d); a.DailyStep(d); }
        Assert.Equal(a.Hash(), l.Hash());
    }

    [Fact]
    public void WB10_Infection_RatesPerGameDay()
    {
        double Rate(Action<WoundBook> prep)
        {
            int n = 0; const int N = 10_000;
            for (ulong s = 0; s < N; s++)
            {
                var b = Book(s); Hit(b, 0, Skin("upper_arm_l"));
                prep(b);
                b.DailyStep(1);
                if (b.Records[0].InfectionLevel > 0) n++;
            }
            return n * 100.0 / N;
        }
        Assert.InRange(Rate(_ => { }), 7.2, 8.8);                               // undressed 8%
        Assert.InRange(Rate(b => Treatment.Perform(b, H, new(TreatKind.Bandage, -1, 100), new ItemBag().Add("bandage", 1))), 1.4, 2.6);   // bandaged 2%
        Assert.InRange(Rate(b => Treatment.Perform(b, H, new(TreatKind.Stitch, -1, 100, 20), new ItemBag().Add("needle_and_thread", 1))), 0.4, 1.6);
        // a bandage older than two days no longer counts
        Assert.InRange(Rate(b => { Treatment.Perform(b, H, new(TreatKind.Bandage, -1, 100), new ItemBag().Add("bandage", 1)); b.AdvanceTo(3 * Day); }), 0, 100);
        // sepsis kills on day 3 to 5 of level 3
        for (ulong s = 0; s < 40; s++)
        {
            var b = Book(s); Hit(b, 0, Skin("upper_arm_l")); var r = b.Records[0]; r.InfectionLevel = 2; b.Records[0] = r;
            int day = 1; for (; day < 80 && !b.Dead; day++) b.DailyStep(day);
            Assert.True(b.Dead); Assert.Equal(DeathKind.Infection, b.Death);
            Assert.InRange(b.Records[0].DeathDay - b.Records[0].SepsisDay, 3, 5);
        }
    }

    [Fact]
    public void WB10_Clean_ClearsInfectionLevels()
    {
        var b = Book(); Hit(b, 0, Skin("upper_arm_l")); var r = b.Records[0]; r.InfectionLevel = 1; b.Records[0] = r;
        for (long t = 100; t < 100 + 60 * 20 && b.Records[0].InfectionLevel > 0; t += 20) Treatment.Perform(b, H, new(TreatKind.Clean, -1, t, 60, 3));
        Assert.Equal(0, b.Records[0].InfectionLevel);
    }

    [Fact]
    public void WB11_LiveAndAbstractBlood_AgreeToTheMillilitre()
    {
        WoundBook Make() { var b = Book(); Hit(b, 0, Muscle("upper_leg_l", 30), Struct("upper_arm_l", "brachial_vein", WoundFlags.VeinHit, 40), Skin("upper_body")); return b; }
        var live = Make(); for (long t = 1; t <= 3600; t++) live.AdvanceTo(t);        // 20 Hz ticks for one game hour
        var abs = Make(); abs.AdvanceTo(3600);                                      // the hourly closed form: 180 sim-seconds
        Assert.Equal(abs.BloodUl, live.BloodUl);
        Assert.Equal(abs.Hash(), live.Hash());
        var x8 = Make(); for (long t = 8; t <= 3600; t += 8) x8.AdvanceTo(t); x8.AdvanceTo(3600);
        Assert.Equal(abs.BloodUl, x8.BloodUl);
    }

    [Fact]
    public void WB12_BloodRegeneration_25mlPerGameHour()
    {
        var b = Book(); b.RegenPm = 1000; b.BloodUl -= 500_000; Hit(b, 0, Skin("upper_body", 0));
        b.AdvanceTo(19 * 3600); Assert.True(b.BloodUl < b.BloodMaxUl);
        b.AdvanceTo(20 * 3600); Assert.Equal(b.BloodMaxUl, b.BloodUl);               // 500 ml at 25 per hour is 20 game hours (the spec's "40" is for 12.5/h)
    }

    [Fact]
    public void WB13_Amputation_NeedsSkill30AndATourniquet_StumpBleedsHalfAMl()
    {
        var b = Book(); Hit(b, 0, Severed("lower_arm_l"));
        Assert.Equal(90_000, b.RateMilliAt(0));
        var bag = new ItemBag().Add("rope", 1);
        Assert.False(Treatment.Perform(b, H, new(TreatKind.Amputate, f.P("lower_arm_l"), 40, 40, 2), bag).Ok);             // no tourniquet yet
        Assert.True(Treatment.Perform(b, H, new(TreatKind.Tourniquet, f.P("lower_arm_l"), 40), bag).Ok);
        Assert.False(Treatment.Perform(b, H, new(TreatKind.Amputate, f.P("lower_arm_l"), 400, 29, 2)).Ok);                 // skill 29
        Assert.True(Treatment.Perform(b, H, new(TreatKind.Amputate, f.P("lower_arm_l"), 400, 30, 2)).Ok);
        Assert.True(b.RateMilliAt(40) <= 500);
        Assert.NotEqual(0UL, b.Permanent.AmputatedMask);
        Assert.True(b.Function(H).Parts[f.P("lower_arm_l")].Severed);
    }

    [Fact]
    public void WB14_PromoteAndDemote_KeepBloodPainAndWounds()
    {
        var l = new WoundLedger(5);
        var b = l.For(77, H, 0); Hit(b, 0, Muscle("upper_leg_l", 30), Bone("upper_arm_l", 2), Struct("upper_arm_l", "brachial_vein", WoundFlags.VeinHit, 150));
        l.AdvanceTo(500);
        ulong h0 = l.Hash(); var set = l.BleedingSet();                         // promote: the live set is rebuilt from the rows
        Assert.Contains(77UL, set);
        var bytes = SaveCodec.SerializeCompressed(WoundSave.Capture(l));
        var back = SaveCodec.DeserializeCompressed<WoundSave>(bytes).Restore();  // demote: written back unchanged
        Assert.Equal(h0, back.Hash());
        Assert.Equal(bytes, SaveCodec.SerializeCompressed(WoundSave.Capture(back)));   // save -> load -> save is byte-identical with open wounds
        // and the books keep agreeing afterwards, tick by tick or hour by hour
        for (long t = 501; t <= 7200; t++) l.AdvanceTo(t);
        back.AdvanceTo(7200);
        Assert.Equal(back.Hash(), l.Hash());
    }

    [Fact]
    public void WB16_At8x_ABleederDiesAtTheSameSimTime()
    {
        var one = Book(); Hit(one, 0, Struct("upper_leg_l", "femoral_artery", WoundFlags.ArteryHit, 1000));
        var eight = Book(); Hit(eight, 0, Struct("upper_leg_l", "femoral_artery", WoundFlags.ArteryHit, 1000));
        for (long t = 1; t <= 1000; t++) one.AdvanceTo(t);
        for (long t = 8; t <= 1000; t += 8) eight.AdvanceTo(t);
        Assert.Equal(500, one.DeathSec);
        Assert.Equal(one.DeathSec, eight.DeathSec);                              // 25 sim-s; at 8x that is 3.1 real seconds
    }

    [Fact]
    public void Shock_WeakenedAtFifteenPercent_AndTimeToCollapseShown()
    {
        var b = Book(); Hit(b, 0, Struct("upper_arm_l", "brachial_vein", WoundFlags.VeinHit, 150));
        Assert.InRange(b.SecondsToCollapse(), 99, 101);
        long t = First(b, x => x.Weakened, 40_000);
        Assert.InRange(S(t), 49.9, 50.1);                                       // 750 ml at 15 ml/s
        Assert.Equal(800, b.StrengthPm);
    }

    [Fact]
    public void Splints_And_Crutches_RestorePartialMovement()
    {
        var b = Book(); Hit(b, 0, Bone("upper_leg_l", 2));
        Assert.Equal(250, b.Function(H).WalkSpeedPm);                            // hop
        Assert.Equal(550, b.Function(H, crutch: true).WalkSpeedPm);
        Assert.True(Treatment.Perform(b, H, new(TreatKind.Splint, -1, 100), new ItemBag().Add("splint", 1)).Ok);
        Assert.True(b.Function(H).WalkSpeedPm >= 400);
    }

    [Fact]
    public void BrainDestroyed_Kills_AndDeathCausesMapToThePersonRecord()
    {
        var b = Book(); int p = f.P("head"); int oi = Array.FindIndex(H.Parts[p].Organs, o => o.Id == "brain");
        Hit(b, 10, new WoundV2 { Part = (short)p, Tissue = (short)H.Parts[p].Organs[oi].Tissue.Index, Structure = -1, Organ = (short)oi, Kind = WoundKind.Cut, DepthPm = 1000, Flags = WoundFlags.OrganHit | WoundFlags.Vital });
        Assert.True(b.Dead); Assert.Equal(DeathKind.Brain, b.Death);
        Assert.Equal(Ravensguard.Sim.People.DeathCause.Brain, WoundSystem.CauseOf(b.Death));
        Assert.Equal(Ravensguard.Sim.People.DeathCause.BloodLoss, WoundSystem.CauseOf(DeathKind.BloodLoss));
    }

    [Fact]
    public void Determinism_SameInputsSameHash()
    {
        ulong Run() { var b = Book(3); Hit(b, 0, Muscle("upper_leg_l", 30), Bone("upper_arm_l", 3), Struct("upper_arm_l", "brachial_vein", WoundFlags.VeinHit, 60)); for (int d = 1; d <= 30; d++) b.DailyStep(d); b.AdvanceTo(31 * Day); return b.Hash(); }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void TreatAi_TourniquetsASpurtingLimb_ThenBandages()
    {
        var b = Book(); Hit(b, 0, Struct("upper_arm_l", "brachial_artery", WoundFlags.ArteryHit, 1000), Muscle("upper_body", 30));
        var bag = new ItemBag().Add("rope", 1).Add("bandage", 3);
        var r1 = TreatAi.Next(b, H, bag, 40, 30, 2)!.Value; Assert.Equal(TreatKind.Tourniquet, r1.Kind);
        Assert.True(Treatment.Perform(b, H, r1, bag).Ok);
        var r2 = TreatAi.Next(b, H, bag, 200, 30, 2)!.Value; Assert.Equal(TreatKind.Bandage, r2.Kind);
        Assert.True(Treatment.Perform(b, H, r2, bag).Ok);
        Assert.Null(TreatAi.Next(b, H, bag, 400, 30, 2));
    }

    [Fact]
    public void WB15_AMedicWithBandages_CutsTheBleedOutOfKnockedOutFighters()
    {
        var sword = new WeaponInstance("arming_sword", "steel");
        var pairs = new Calibration.Pairing[]
        {
            new("sword vs sword", sword, Calibration.Padded, sword, Calibration.Padded),
            new("spear vs sword", new("spear", "steel"), Calibration.Padded, sword, Calibration.Unarmoured),
            new("axe vs mail", new("axe", "steel"), Calibration.Padded, sword, Calibration.Mailed),
        };
        int ko = 0, bledNo = 0, bledMedic = 0;
        foreach (var p in pairs)
        {
            var a = Calibration.Run(f.Tables, p, 150); var b = Calibration.Run(f.Tables, p, 150, treat: true);
            ko += a.KnockedOut; bledNo += a.KoBledOut; bledMedic += b.KoBledOut;
        }
        Assert.True(bledMedic < bledNo, $"{bledMedic} with a medic vs {bledNo} without");
        Assert.True(bledMedic * 100 <= ko * 35, $"{100.0 * bledMedic / ko:0}% bled out with treatment (target <= 35%)");
    }
}
