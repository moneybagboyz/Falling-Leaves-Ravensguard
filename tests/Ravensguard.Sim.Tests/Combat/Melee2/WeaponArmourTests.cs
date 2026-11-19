using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Flavour;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Dialogue;

namespace Ravensguard.Sim.Tests.Combat.Melee2;

/// <summary>
/// Phase 4 close-out (docs/reviews/phase-4.md items 1 to 5): the weapon x armour matrix's expectation directions (not its numbers), blunt damage to rigid armour
/// (dents, buckles, burst rings, broken straps) in the resolver, the fight, the save and the flavour log, the counter to the spear (inside its reach), and the mounted reach.
/// </summary>
[Collection("fight")]
public class WeaponArmourTests(FightFixture fx)
{
    private WeaponMatrix.Weapon Row(string name) => WeaponMatrix.Weapons(fx.C).Single(w => w.Name == name);
    private WeaponMatrix.Cell Cell(string weapon, WeaponMatrix.Harness h, bool gap = false) => WeaponMatrix.Run(fx.C, Row(weapon), h, gap, n: 60, trials: 30);

    // ---------------------------------------------------------------- item 2: the matrix's expectations

    [Fact]
    public void Matrix_SwordsAndAxesMostlyBruiseOnMail()
    {
        foreach (var w in new[] { "arming_sword slash", "falchion chop", "hand_axe hack", "axe hack", "great_axe hack" })
            foreach (var h in new[] { WeaponMatrix.Mail, WeaponMatrix.MailPadded })
            {
                var c = Cell(w, h);
                Assert.True(c.CutPm < 500 && c.BruisePm >= c.CutPm, $"{w} on {h.Name}: cut {c.CutPm} bruise {c.BruisePm}");
            }
    }

    [Fact]
    public void Matrix_BluntWeaponsStillHurtThroughPlate_EdgesDoNot()
    {
        var sword = Cell("arming_sword slash", WeaponMatrix.Plate);
        foreach (var w in new[] { "mace bash", "war_hammer strike", "maul smash" })
        {
            var c = Cell(w, WeaponMatrix.Plate);
            Assert.True(c.DownedPm >= 500, $"{w} on plate: {c.DownedPm} pm down within a minute");
            Assert.True(c.BodyPm >= 100, $"{w} on plate: {c.BodyPm} pm of the blow reaches the body");
            Assert.True(c.BodyPm > 3 * sword.BodyPm, $"{w} {c.BodyPm} against the sword's {sword.BodyPm}");
        }
    }

    [Fact]
    public void Matrix_DaggersBeatPlateOnlyThroughTheGaps()
    {
        var trunk = Cell("dagger stab", WeaponMatrix.Plate);
        var gap = Cell("dagger stab", WeaponMatrix.Plate, gap: true);
        Assert.True(trunk.CutPm <= 100 && trunk.BodyPm <= 100, $"on the plate: cut {trunk.CutPm}, body {trunk.BodyPm}");
        Assert.True(gap.CutPm >= 900 && gap.SeriousX100 >= 100, $"through the armpit: cut {gap.CutPm}, serious x100 {gap.SeriousX100}");
    }

    [Fact]
    public void Matrix_ArrowsMostlyFailOnPlate_QuarrelsPierceMailClose_JavelinsStickInShields()
    {
        foreach (var w in new[] { "longbow bodkin", "longbow broadhead", "longbow blunt" })
        {
            var c = Cell(w, WeaponMatrix.Plate);
            Assert.True(c.CutPm <= 200 && c.DownedPm < 500, $"{w} on plate: cut {c.CutPm}, down {c.DownedPm}");
        }
        var q = Cell("crossbow quarrel", WeaponMatrix.Mail);
        Assert.True(q.CutPm >= 800, $"quarrel on mail at 10 m: cut {q.CutPm}");
        Assert.True(Cell("thrown javelin", WeaponMatrix.None).ShieldStoppedPm >= 500);
    }

    [Fact]
    public void Matrix_EverythingIsDeadlyAgainstAnUnarmouredMan()
    {
        foreach (var w in WeaponMatrix.Weapons(fx.C))
        {
            var c = WeaponMatrix.Run(fx.C, w, WeaponMatrix.None, false, n: 30, trials: 30);
            Assert.True(c.DownedPm >= 800 && c.N - c.Cut - c.Bruise == 0, $"{w.Name}: down {c.DownedPm} pm, no wound in {c.N - c.Cut - c.Bruise} of {c.N}");
        }
    }

    // ---------------------------------------------------------------- item 4: blunt blows damage rigid armour

    private (BlowResult R, StrikeV2 S) Blow(string shape, string attack, WornV2[] worn, int skill = 50, uint serial = 1)
    {
        var plan = fx.C.T.Plan("human");
        var w = fx.C.T.Weapon(shape, "steel");
        var s = StrikeV2.Create(w, w.AttackIndex(attack), plan, plan.IndexOf("upper_body"), skill);
        s.Worn = worn; s.WornCount = worn.Length; s.Facing = Facing.Front;
        var r = new BlowResult();
        new ResolverV2(fx.C.T).ResolveImpact(s, new BlowKey(3, serial, 1, serial), r);
        return (r, s);
    }

    private WornV2[] Plate() => [WornV2.Of(fx.C.T.Armour("gambeson", "linen")), WornV2.Of(fx.C.T.Armour("breastplate", "steel"))];

    private static LayerStep On(BlowResult r, int piece) => r.StepSpan.ToArray().Single(s => s.IsArmour && s.Piece == piece);

    [Fact]
    public void Wear_AMaceDentsABreastplateMoreThanASword_AndProtectionFallsWithCondition()
    {
        var mace = Blow("mace", "bash", Plate()).R;
        var sword = Blow("arming_sword", "slash", Plate()).R;
        Assert.True(On(mace, 1).WearPm > 5 * On(sword, 1).WearPm, $"mace {On(mace, 1).WearPm} pm, sword {On(sword, 1).WearPm} pm");
        Assert.Contains(On(mace, 1).Harm, new[] { ArmourHarm.Dented, ArmourHarm.Buckled });
        Assert.Equal(ArmourHarm.None, On(sword, 1).Harm);
        // the same breastplate under mace blows: condition falls, more of each blow gets through, and the battered plate breaks a strap and hangs loose
        var worn = Plate();
        long first = -1, later = -1; bool told = false;
        for (uint i = 1; i <= 8; i++)
        {
            var r = Blow("mace", "bash", worn, serial: i).R;
            long body = r.StepSpan.ToArray().First(s => !s.IsArmour && s.Tissue >= 0).InMilli;
            if (i == 1) first = body; else later = Math.Max(later, body);
            ArmourWear.Apply(worn, r, (p, h) => told |= p == 1 && h == ArmourHarm.Strap);
        }
        Assert.True(worn[1].ConditionPm < 400, $"condition {worn[1].ConditionPm}");
        Assert.True(later > first, $"first {first}, later {later}");
        Assert.True(worn[1].Slipped && told);
    }

    [Fact]
    public void Wear_HeavyBluntBurstsMail_ACutOpensIt_AndTheHoleCoversLess()
    {
        var mail = new[] { WornV2.Of(fx.C.T.Armour("mail_hauberk", "iron")) };
        var maul = Blow("maul", "smash", mail).R;
        Assert.Equal(ArmourHarm.Burst, On(maul, 0).Harm);
        ArmourWear.Apply(mail, maul);
        var plan = fx.C.T.Plan("human");
        Assert.NotEqual(0UL, mail[0].BreachMask & (1UL << plan.Parts[plan.IndexOf("upper_body")].BaseIndex));
        // a blow that does not burst rings leaves no hole
        var plain = new[] { WornV2.Of(fx.C.T.Armour("mail_hauberk", "iron")) };
        var slash = Blow("arming_sword", "slash", plain, skill: 30).R;
        ArmourWear.Apply(plain, slash);
        Assert.Equal(0UL, plain[0].BreachMask);
        // the breach lowers the piece's coverage there: over many blows the mail is met less often
        int metHoled = 0, metWhole = 0;
        for (uint i = 1; i <= 300; i++)
        {
            if (On(Blow("dagger", "stab", mail, serial: i).R, 0).Outcome != LayerOutcome.NotCovered) metHoled++;
            if (On(Blow("dagger", "stab", plain, serial: i).R, 0).Outcome != LayerOutcome.NotCovered) metWhole++;
        }
        Assert.True(metHoled < metWhole - 40, $"{metHoled} against {metWhole}");
    }

    [Fact]
    public void Wear_ABackplateIsNotMetFromTheFront_NorABreastplateFromBehind()
    {
        WornV2[] worn = [WornV2.Of(fx.C.T.Armour("breastplate", "steel")), WornV2.Of(fx.C.T.Armour("backplate", "steel"))];
        var plan = fx.C.T.Plan("human");
        var w = fx.C.T.Weapon("mace", "steel");
        foreach (var (facing, met, not) in new[] { (Facing.Front, 0, 1), (Facing.Rear, 1, 0) })
        {
            var s = StrikeV2.Create(w, 0, plan, plan.IndexOf("upper_body"), 50);
            s.Worn = worn; s.WornCount = 2; s.Facing = facing;
            var r = new BlowResult();
            bool sawMet = false;
            for (uint i = 1; i <= 50; i++)
            {
                new ResolverV2(fx.C.T).ResolveImpact(s, new BlowKey(5, i, 1, i), r);
                Assert.DoesNotContain(r.StepSpan.ToArray(), x => x.IsArmour && x.Piece == not);
                sawMet |= r.StepSpan.ToArray().Any(x => x.IsArmour && x.Piece == met && x.Outcome != LayerOutcome.NotCovered);
            }
            Assert.True(sawMet, facing.ToString());
        }
    }

    [Fact]
    public void Wear_InTheFight_IsToldSavedAndHashed()
    {
        // a maceman against a man in plate: the plate dents, the flavour log tells it, and a save mid-fight keeps it byte for byte
        var w = fx.World(41);
        var a = w.Add(fx.Kit("Mace", 0, -1000, weapon: new WeaponInstance("mace", "steel"), skill: 60));
        var t = w.Add(fx.Kit("Plate", 1, 1000, skill: 20, worn: FightCalibration.Plated.Worn));
        var rec = FlavourRecorder.Attach(w);
        int guard = 0;
        while (!w.Events.Any(e => e.Kind == FightEventKind.Broke && e.A == 3) && guard++ < 4000 && !w.IsOver) w.StepWithAi();
        var ev = w.Events.First(e => e.Kind == FightEventKind.Broke && e.A == 3);
        Assert.Equal(t.Id, ev.Actor);
        Assert.Contains(t.Worn, x => !x.Pristine);
        var line = rec.Log.Lines.First(l => l.Kind == FightEventKind.Broke && l.A == 3);
        var book = new FlavourBook(fx.Db);
        var text = new DictText(TalkKit.EnCsv());
        var d = FlavourDescribe.Describe(rec.Log, line, text);
        Assert.Equal("after", d.Slot);
        Assert.Contains(d.Ctx.Cause, new[] { "dented", "buckled", "burst", "strap" });
        Assert.True(book.CountMatches("after", d.Ctx) > 0, d.Ctx.Cause);
        Assert.NotEqual("", d.P["armour_piece"]);
        // save, load, save: the same bytes, the same hash, and the damage came back
        var b1 = FightRecord.Capture(w).ToBytes();
        var back = FightRecord.FromBytes(b1).Restore(fx.C);
        Assert.Equal(b1, FightRecord.Capture(back).ToBytes());
        Assert.Equal(w.Hash(), back.Hash());
        Assert.Equal(t.Worn.Select(x => (x.ConditionPm, x.BreachMask, x.Slipped)), back.Fighters[t.Id].Worn.Select(x => (x.ConditionPm, x.BreachMask, x.Slipped)));
        _ = a;
    }

    [Fact]
    public void Wear_EveryArmourCauseHasALine()
    {
        var book = new FlavourBook(fx.Db);
        foreach (var cause in new[] { "dented", "buckled", "burst", "strap" })
        {
            var c = FlavourCtx.Blank(); c.Event = "broke"; c.Cause = cause;
            Assert.True(book.CountMatches("after", c) > 0, cause);
        }
        var en = TalkKit.EnCsv();
        foreach (var k in new[] { "fl_after_broke_armour_dented1", "fl_after_broke_armour_buckled1", "fl_after_broke_armour_burst1", "fl_after_broke_armour_strap1" }) Assert.True(en.ContainsKey(k), k);
    }

    // ---------------------------------------------------------------- item 1: the counter to the spear

    [Fact]
    public void Spear_APassedPointPutsTheSwordsmanInside_AndTheCrowdedSpearIsChokedUp()
    {
        Assert.True(fx.C.K.InsideTicks > 0 && fx.C.K.CrowdedMomentumPm < 1000);
        var spear = fx.C.T.Weapon("spear", "steel").Shape;
        Assert.Equal((0.40, 0.50), (spear.WindUpS, spear.RecoverS));                   // plan 4A.3 restored
        // over seeded duels the swordsman gets inside the spear's reach after parrying or slipping its point
        int inside = 0;
        for (int i = 0; i < 20 && inside == 0; i++)
        {
            var w = new FightWorld(fx.C, (ulong)(1000 + i), (ulong)(i + 1), 8 * 3600);
            w.Add(FightCalibration.KitOf("A", 0, FightCalibration.Spear, FightCalibration.Padded, null, 45, -3000, FightMath.One));
            var b = w.Add(FightCalibration.KitOf("B", 1, FightCalibration.Sword, FightCalibration.Padded, null, 45, 3000, -FightMath.One));
            while (!w.IsOver && w.Tick < 2400) { w.StepWithAi(); if (w.Tick <= b.InsideUntil && b.InsideOf == 0) inside++; }
        }
        Assert.True(inside > 0);
        // the crowded thrust keeps the shortened grip's share of the momentum
        var ww = fx.World(3);
        var sp = ww.Add(fx.Kit("Spear", 0, 0, weapon: FightCalibration.Spear));
        var near = ww.Add(fx.Kit("Near", 1, 1000));
        var far = ww.Add(fx.Kit("Far", 1, 2000, y: 3000));
        var sNear = ww.StrikePreview(sp, near, 0, near.Plan.IndexOf("upper_body"));
        var sFar = ww.StrikePreview(sp, far, 0, far.Plan.IndexOf("upper_body"));
        Assert.Equal(sFar.MomentumPm * fx.C.K.CrowdedMomentumPm / 1000, sNear.MomentumPm);
    }

    // ---------------------------------------------------------------- item 5: the mounted reach

    [Fact]
    public void MountedReach_PlusFourHundredAgainstFoot_NoneAgainstARider()
    {
        var w = fx.World(9);
        var sword = new WeaponInstance("arming_sword", "steel");
        var r = w.AddMounted(fx.Kit("Rider", 0, 0, weapon: sword), new MountKit { Riding = 50 });
        var other = w.AddMounted(fx.Kit("Other", 1, 3000, weapon: sword), new MountKit { Riding = 50 });
        var foot = w.Add(fx.Kit("Foot", 1, -3000, weapon: sword));
        int baseReach = fx.C.T.Weapon("arming_sword", "steel").Shape.ReachMm, plus = w.Mounts!.D.MountedReachMm;
        Assert.Equal(400, plus);
        Assert.Equal(baseReach + plus, w.ReachAgainst(r, foot));
        Assert.Equal(baseReach, w.ReachAgainst(r, other));
        Assert.Equal(baseReach, w.ReachAgainst(foot, r));                              // a man on foot has no bonus
        // a lance keeps its own reach against anyone
        var lancer = w.AddMounted(fx.Kit("Lancer", 0, 0, y: 6000, weapon: MountedCalibration.Lance), new MountKit { Riding = 50 });
        Assert.Equal(lancer.ReachMm, w.ReachAgainst(lancer, other));
        Assert.Equal(lancer.ReachMm, w.ReachAgainst(lancer, foot));
    }
}
