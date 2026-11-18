using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Tests.Combat;

[Collection("combat")]
public class CombatTests(CombatFixture f)
{
    private StrikeResult Impact(WeaponInstance w, string attack, string part, Defender d, Attacker? a = null,
        string? gap = null, AttackStyle style = AttackStyle.Normal, uint stream = 1)
    {
        var rng = CombatFixture.Rng(stream);
        return f.Resolver.ResolveImpact(new StrikeInput(a ?? CombatFixture.Avg(), w, attack, part, d, gap, Facing.Front, style), ref rng);
    }

    private static readonly WeaponInstance SteelSword = new("arming_sword", "steel");
    private static readonly WornItem SteelPlate = new("breastplate", "steel");

    // ---------- content / tables ----------

    [Fact]
    public void ContentHasMaterialsShapesAndBodyPlan()
    {
        Assert.True(f.Db.All<MaterialDef>().Count() >= 20);
        Assert.Equal(12, f.Db.All<WeaponShapeDef>().Count());   // 9 weapons and the 3 blunted practice weapons of the yard
        Assert.Equal(20, f.Db.All<ArmourShapeDef>().Count());
        Assert.True(f.Tables.WeaponEntries > 100 && f.Tables.ArmourEntries > 20);
        // copy_from: wrought iron overrides shear but inherits the impact numbers; ash inherits oak's strength.
        var wi = f.Tables.Materials["wrought_iron"]; var iron = f.Tables.Materials["iron"];
        Assert.Equal(iron.ImpactFracture, wi.ImpactFracture);
        Assert.True(wi.ShearFracture < iron.ShearFracture);
        Assert.Equal(f.Tables.Materials["oak"].ShearFracture, f.Tables.Materials["ash"].ShearFracture);
    }

    [Fact]
    public void HumanBodyPlanUsesDfRelativeSizes()
    {
        var h = f.Human;
        Assert.Equal(0.18, h.ById["upper_body"].RelativeSize);
        Assert.Equal(0.054, h.ById["head"].RelativeSize);
        Assert.Equal(0.036, h.ById["upper_arm_l"].RelativeSize);
        Assert.Equal(0.036, h.ById["upper_arm_r"].RelativeSize);
        Assert.Equal(0.014, h.ById["hand_l"].RelativeSize);
        Assert.Equal(0.072, h.ById["lower_leg_r"].RelativeSize);
        Assert.Equal("lower_arm_l", h.ById["hand_l"].Parent);
        Assert.Equal(16, h.Parts.Count);
        // DF: a 70000 human has a 12621-ish upper body (18%) and a 2524 upper arm (3.6%).
        Assert.Equal(2520, (int)(70000 * h.ById["upper_arm_l"].RelativeSize));
        Assert.Contains(h.ById["upper_leg_l"].Structures, s => s.Kind == StructureKind.Artery);
        Assert.Contains(h.ById["upper_body"].Organs, o => o.Id == "heart");
    }

    [Fact]
    public void ArmingSwordHasRealWeight()
    {
        var w = f.Tables.Weapon("arming_sword", "steel");
        Assert.InRange(w.MassKg, 1.3, 1.6); // 0.18 L blade ≈ 1.41 kg
        Assert.InRange(f.Tables.Weapon("dagger", "steel").MassKg, 0.25, 0.5);
        Assert.InRange(f.Tables.Weapon("longsword", "steel").MassKg, 1.5, 2.2);
    }

    [Fact]
    public void ArmourFlagsApplyToDerivedLayers()
    {
        var mail = f.Tables.Armour("mail_hauberk", "steel").Props;
        Assert.Equal(50_000, mail.ShearStrain);
        Assert.Equal(50_000, mail.ImpactStrain);
        Assert.Equal(215, f.Tables.Armour("breastplate", "steel").Props.ShearStrain);
        var gam = f.Tables.Armour("gambeson", "linen").Props;
        Assert.True(gam.ShearFracture <= 30_000 && gam.ShearYield <= 20_000); // woven
        Assert.InRange(f.Tables.Armour("breastplate", "steel").MassKg(f.Human, 70000), 2, 9);
    }

    // ---------- momentum (DF wiki golden values) ----------

    private static double AxeMomentum(double density, double dwarfSize) =>
        Momentum.Base(1250, 1.25, 60000, dwarfSize, density * 800);

    [Fact]
    public void MomentumMatchesWikiTable()
    {
        // Material science table: dwarf of strength 1250 hacking (velocity 1.25) with a size-800 battle axe.
        Assert.Equal(92.8628, AxeMomentum(7850, 60000), 3);
        Assert.Equal(93.2489, AxeMomentum(7850, 33750), 3);
        Assert.Equal(92.3711, AxeMomentum(7850, 93750), 3);
        Assert.Equal(92.9054, AxeMomentum(8250, 60000), 3); // bronze
        Assert.Equal(92.9692, AxeMomentum(8930, 60000), 3); // copper
        Assert.Equal(93.0845, AxeMomentum(10490, 60000), 3); // silver
        Assert.Equal(68.1818, AxeMomentum(200, 60000), 3);  // adamantine
    }

    [Fact]
    public void MomentumOrderingFollowsDensityAndSteelEqualsIron()
    {
        double steel = AxeMomentum(f.Tables.Materials["steel"].DensityKgM3, 60000);
        double iron = AxeMomentum(f.Tables.Materials["iron"].DensityKgM3, 60000);
        double bronze = AxeMomentum(f.Tables.Materials["bronze"].DensityKgM3, 60000);
        double copper = AxeMomentum(f.Tables.Materials["copper"].DensityKgM3, 60000);
        double silver = AxeMomentum(f.Tables.Materials["silver"].DensityKgM3, 60000);
        double wood = AxeMomentum(f.Tables.Materials["oak"].DensityKgM3, 60000);
        Assert.Equal(steel, iron);
        Assert.True(iron < bronze && bronze < copper && copper < silver);
        Assert.True(wood < steel * 0.95); // light weapons lose momentum
        // weight matters little: a 3x heavier weapon gains well under 15%.
        Assert.True(AxeMomentum(7850 * 3, 60000) < steel * 1.15);
        // bigger / stronger creatures hit harder
        Assert.True(Momentum.Base(2000, 1.25, 60000, 60000, 7850 * 800) > Momentum.Base(1250, 1.25, 60000, 60000, 7850 * 800));
    }

    [Fact]
    public void MomentumModifiers()
    {
        var d = f.Man();
        double Mom(int skill, AttackStyle st, bool prone = false) => f.Resolver.InitialMomentum(new StrikeInput(
            CombatFixture.Avg(skill), SteelSword, "slash", "upper_body", d with { Prone = prone }, null, Facing.Front, st));
        double b = Mom(0, AttackStyle.Normal);
        Assert.Equal(2.0, Mom(100, AttackStyle.Normal) / b, 6);
        Assert.Equal(0.5, Mom(0, AttackStyle.Quick) / b, 6);
        Assert.Equal(1.5, Mom(0, AttackStyle.Heavy) / b, 6);
        Assert.Equal(2.0, Mom(0, AttackStyle.Normal, true) / b, 6);
    }

    // ---------- hit chance ----------

    [Fact]
    public void HitChanceModifiers()
    {
        var plain = f.Man();
        var si = new StrikeInput(CombatFixture.Avg(), SteelSword, "slash", "upper_body", plain);
        double torso = f.Resolver.HitChance(si);
        Assert.InRange(torso, 0.49, 0.51);
        Assert.True(f.Resolver.HitChance(si with { TargetPart = "hand_l" }) < torso * 0.4); // small zone
        Assert.True(f.Resolver.HitChance(si with { Defender = plain with { GuardedParts = new HashSet<string> { "upper_body" } } }) < torso);
        Assert.Equal(torso * 0.6, f.Resolver.HitChance(si with { Defender = plain with { GuardedParts = new HashSet<string> { "upper_body" } } }), 6);
        Assert.True(f.Resolver.HitChance(si with { Facing = Facing.Flank }) > torso);
        Assert.True(f.Resolver.HitChance(si with { Facing = Facing.Rear }) > f.Resolver.HitChance(si with { Facing = Facing.Flank }));
        Assert.True(f.Resolver.HitChance(si with { Attacker = CombatFixture.Avg(60) }) > torso);
        // armour gap: x0.35 only when a worn armour piece actually has that gap
        var plated = f.Man(SteelPlate);
        var gapShot = new StrikeInput(CombatFixture.Avg(), SteelSword, "slash", "upper_body", plated, "armpit");
        Assert.Equal(torso * 0.35, f.Resolver.HitChance(gapShot), 6);
        Assert.Equal(torso, f.Resolver.HitChance(gapShot with { Defender = plain }), 6);
    }

    // ---------- layers ----------

    [Fact]
    public void SteelSwordCutsUnarmouredArmThroughSkinFatMuscle()
    {
        var r = Impact(SteelSword, "slash", "upper_arm_l", f.Man(), CombatFixture.Avg(20));
        Assert.True(r.Hit);
        var cuts = r.Wounds.Where(w => w.Kind == WoundKind.Cut).Select(w => w.TissueId).ToList();
        Assert.Contains("skin", cuts); Assert.Contains("fat", cuts); Assert.Contains("muscle", cuts);
        Assert.All(r.Trace.Where(t => t.Layer is "skin" or "fat" or "muscle"), t => Assert.Equal(LayerOutcome.Cut, t.Outcome));
        Assert.True(r.FinalMomentumMilli < r.InitialMomentumMilli); // ~5% per layer
        Assert.True(r.Wounds.Where(w => w.TissueId == "muscle" && w.StructureId == null).All(w => w.BleedMlPerS == Fixed.FromInt(3)));
    }

    [Fact]
    public void MailTurnsCutIntoBluntWithoutDampening()
    {
        var mail = new WornItem("mail_hauberk", "steel");
        var r = Impact(SteelSword, "slash", "upper_body", f.Man(mail));
        var t = r.Trace.Single(x => x.Layer == "mail_hauberk");
        Assert.Equal(LayerOutcome.Stopped, t.Outcome);
        Assert.Equal(t.MomentumInMilli, t.MomentumOutMilli); // strain-at-yield treated as 50000: ×1
        // the blow is blunt from there on: no flesh is cut
        Assert.DoesNotContain(r.Wounds, w => w.Kind == WoundKind.Cut);
    }

    [Fact]
    public void SteelPlateSoaksAtLeast98PercentOfAStoppedBlow()
    {
        var mace = new WeaponInstance("mace", "steel");
        var r = Impact(mace, "bash", "upper_body", f.Man(SteelPlate));
        var t = r.Trace.Single(x => x.Layer == "breastplate");
        Assert.Equal(LayerOutcome.Stopped, t.Outcome);
        Assert.True(t.MomentumOutMilli <= t.MomentumInMilli * 2 / 100, $"{t.MomentumOutMilli} vs {t.MomentumInMilli}");
        // and the edged version: a sword stopped by plate keeps ≤1%
        var s = Impact(SteelSword, "slash", "upper_body", f.Man(SteelPlate));
        var ts = s.Trace.Single(x => x.Layer == "breastplate");
        Assert.Equal(LayerOutcome.Stopped, ts.Outcome);
        Assert.True(ts.MomentumOutMilli <= ts.MomentumInMilli / 100);
    }

    [Fact]
    public void LightWoodenWeaponIsDeflectedByPlate()
    {
        var club = new WeaponInstance("club", "oak");
        var r = Impact(club, "bash", "upper_body", f.Man(SteelPlate));
        Assert.True(r.Deflected);
        Assert.Equal(LayerOutcome.Deflected, r.Trace.Single(t => t.Layer == "breastplate").Outcome);
        Assert.Empty(r.Wounds);
        Assert.Equal(0, r.FinalMomentumMilli);
        // a steel mace is not deflected by the same plate
        Assert.False(Impact(new WeaponInstance("mace", "steel"), "bash", "upper_body", f.Man(SteelPlate)).Deflected);
    }

    [Fact]
    public void CopperWeaponCannotCutSteelPlate()
    {
        var copper = new WeaponInstance("arming_sword", "copper");
        var r = Impact(copper, "slash", "upper_body", f.Man(SteelPlate), CombatFixture.Avg(100), style: AttackStyle.Heavy);
        Assert.NotEqual(LayerOutcome.Cut, r.Trace.Single(t => t.Layer == "breastplate").Outcome);
        Assert.DoesNotContain(r.Wounds, w => w.Kind == WoundKind.Cut);
        // while the same swing with steel on bare flesh does cut
        Assert.Contains(Impact(SteelSword, "slash", "upper_body", f.Man()).Wounds, w => w.Kind == WoundKind.Cut);
    }

    [Fact]
    public void LayersAreTestedOutsideIn()
    {
        var worn = new[] { new WornItem("shirt", "linen"), new WornItem("gambeson", "linen"), new WornItem("mail_hauberk", "steel"),
            new WornItem("cloak", "wool") };
        var r = Impact(SteelSword, "slash", "upper_body", f.Man(worn));
        var names = r.Trace.Where(t => t.IsArmour).Select(t => t.Layer).ToList();
        Assert.Equal(["cloak", "mail_hauberk", "gambeson", "shirt"], names);
        // plate goes outside mail within the armour layer
        var r2 = Impact(SteelSword, "slash", "upper_body", f.Man(new WornItem("mail_hauberk", "steel"), SteelPlate));
        Assert.Equal(["breastplate", "mail_hauberk"], r2.Trace.Where(t => t.IsArmour).Select(t => t.Layer).ToArray());
    }

    [Fact]
    public void NamedGapBypassesArmourLayerButNotPadding()
    {
        var worn = f.Man(SteelPlate, new WornItem("gambeson", "linen"));
        var through = Impact(SteelSword, "stab", "upper_body", worn, CombatFixture.Avg(20), gap: "armpit");
        Assert.Equal(LayerOutcome.Bypassed, through.Trace.Single(t => t.Layer == "breastplate").Outcome);
        Assert.Contains(through.Wounds, w => w.Kind == WoundKind.Cut);
        var closed = Impact(SteelSword, "stab", "upper_body", worn, CombatFixture.Avg(20));
        Assert.DoesNotContain(closed.Wounds, w => w.Kind == WoundKind.Cut);
    }

    [Fact]
    public void CoverageIsRolledPerPiece()
    {
        // The helm covers 90% of the head: over many strikes some pass uncovered, most are covered.
        var d = f.Man(new WornItem("helm", "steel"));
        int notCovered = 0, covered = 0;
        for (uint s = 0; s < 400; s++)
        {
            var r = Impact(SteelSword, "slash", "head", d, stream: s);
            if (r.Trace.Single(t => t.Layer == "helm").Outcome == LayerOutcome.NotCovered) notCovered++; else covered++;
        }
        Assert.InRange(notCovered, 15, 70);
        Assert.True(covered > notCovered * 4);
    }

    // ---------- penetration, severing, structures ----------

    [Fact]
    public void DeepPenetrationSeversAHand()
    {
        var axe = new WeaponInstance("axe", "steel");
        var r = Impact(axe, "hack", "hand_l", f.Man(), CombatFixture.Avg(100), style: AttackStyle.Heavy);
        Assert.True(r.Severed);
        Assert.Contains(r.Wounds, w => w.IsSevered && w.PartId == "hand_l");
        // a dagger slash is too shallow (40 mm) and too weak to take the neck off
        var d = Impact(new WeaponInstance("dagger", "steel"), "slash", "neck", f.Man(), CombatFixture.Avg(0));
        Assert.False(d.Severed);
        // a blunt blow never severs
        Assert.False(Impact(new WeaponInstance("maul", "steel"), "smash", "hand_l", f.Man(), CombatFixture.Avg(100)).Severed);
        // a part that is not severable (upper body) never comes off
        Assert.False(Impact(axe, "hack", "upper_body", f.Man(), CombatFixture.Avg(100), style: AttackStyle.Heavy).Severed);
    }

    [Fact]
    public void PenetrationLimitsHowDeepAStabGoes()
    {
        // A dagger stab (120 mm) into the torso cannot get past skin+fat+muscle+ribs (45 mm)? it can: so use a short blow.
        // A war-hammer spike has 150 mm; a dagger slash only 90 mm and the thigh wall is 68 mm: bone is reached but not the flesh behind.
        var r = Impact(new WeaponInstance("dagger", "steel"), "stab", "upper_body", f.Man(), CombatFixture.Avg(100), style: AttackStyle.Heavy);
        Assert.Contains(r.Wounds, w => w.Kind == WoundKind.Cut && w.TissueId == "skin");
        // partial depth: a very short penetration (club-like point) leaves a depth < 1 wound. Use the dagger on the thick upper leg.
        var leg = Impact(new WeaponInstance("dagger", "steel"), "slash", "upper_leg_l", f.Man(), CombatFixture.Avg(100), style: AttackStyle.Heavy);
        Assert.All(leg.Wounds.Where(w => w.Kind == WoundKind.Cut), w => Assert.True(w.Depth <= Fixed.One && w.Depth > Fixed.Zero));
    }

    [Fact]
    public void SpearReachesOrgansDaggerSlashDoesNot()
    {
        int spearOrgan = 0, daggerOrgan = 0;
        for (uint s = 0; s < 300; s++)
        {
            if (Impact(new WeaponInstance("spear", "steel"), "thrust", "upper_body", f.Man(), CombatFixture.Avg(50), stream: s)
                .Wounds.Any(w => (w.Flags & WoundFlags.OrganHit) != 0)) spearOrgan++;
            if (Impact(new WeaponInstance("dagger", "steel"), "slash", "upper_body", f.Man(), CombatFixture.Avg(0), stream: s)
                .Wounds.Any(w => (w.Flags & WoundFlags.OrganHit) != 0)) daggerOrgan++;
        }
        Assert.True(spearOrgan > 40, $"spear organ hits {spearOrgan}");
        Assert.True(spearOrgan > daggerOrgan);
    }

    [Fact]
    public void ArteryWoundsBleedHeavilyAndStructuresAreFlagged()
    {
        bool artery = false, nerve = false;
        for (uint s = 0; s < 300; s++)
        {
            foreach (var w in Impact(SteelSword, "slash", "upper_leg_l", f.Man(), CombatFixture.Avg(20), stream: s).Wounds)
            {
                if ((w.Flags & WoundFlags.ArteryHit) != 0)
                {
                    artery = true;
                    Assert.InRange(w.BleedMlPerS.ToDouble(), 80, 120);
                    Assert.Equal("femoral_artery", w.StructureId);
                }
                if ((w.Flags & WoundFlags.NerveHit) != 0) { nerve = true; Assert.Equal(Fixed.Zero, w.BleedMlPerS); }
            }
        }
        Assert.True(artery && nerve);
    }

    [Fact]
    public void BluntBlowBreaksBoneThroughSoftLayers()
    {
        var r = Impact(new WeaponInstance("mace", "steel"), "bash", "upper_arm_l", f.Man(), CombatFixture.Avg(20));
        Assert.All(r.Wounds.Where(w => w.StructureId == null), w => Assert.Equal(WoundKind.Crush, w.Kind));
        Assert.Contains(r.Wounds, w => (w.Flags & WoundFlags.BoneBroken) != 0);
    }

    // ---------- full resolution and determinism ----------

    [Fact]
    public void SameRngStreamGivesIdenticalWounds()
    {
        var d = f.Man(new WornItem("gambeson", "linen"), new WornItem("helm", "iron"));
        var input = new StrikeInput(CombatFixture.Avg(30), SteelSword, "slash", "upper_arm_r", d);
        for (uint stream = 0; stream < 50; stream++)
        {
            var r1 = CombatFixture.Rng(stream, 7); var r2 = CombatFixture.Rng(stream, 7);
            var a = f.Resolver.Resolve(input, ref r1); var b = f.Resolver.Resolve(input, ref r2);
            Assert.Equal(a.Hit, b.Hit); Assert.Equal(a.PartId, b.PartId); Assert.Equal(a.Redirected, b.Redirected);
            Assert.Equal(a.Wounds, b.Wounds);
            Assert.Equal(a.Trace, b.Trace);
            Assert.Equal(a.FinalMomentumMilli, b.FinalMomentumMilli);
            Assert.Equal(r1.Draws, r2.Draws);
        }
    }

    [Fact]
    public void MissRedirectsToNeighbouringZoneAboutHalfTheTime()
    {
        int hit = 0, redirected = 0, miss = 0;
        var input = new StrikeInput(CombatFixture.Avg(), SteelSword, "slash", "hand_l", f.Man());
        const int n = 2000;
        for (uint s = 0; s < n; s++)
        {
            var rng = CombatFixture.Rng(s, 3);
            var r = f.Resolver.Resolve(input, ref rng);
            if (!r.Hit) miss++; else if (r.Redirected) { redirected++; Assert.Equal("lower_arm_l", r.PartId); } else hit++;
        }
        double p = f.Resolver.HitChance(input);
        Assert.InRange(hit / (double)n, p - 0.04, p + 0.04);
        Assert.InRange(redirected / (double)(redirected + miss), 0.44, 0.56);
    }

    [Fact]
    public void ArmourFitsWithinOneSeventh()
    {
        Assert.True(Fit.Fits(70000, 70000));
        Assert.True(Fit.Fits(70000, 80000));
        Assert.False(Fit.Fits(70000, 81000));
        Assert.True(Fit.Fits(70000, 60000));
        Assert.False(Fit.Fits(70000, 59000));
        var t = f.Tables.ArmourShapes;
        Assert.True(Fit.CanWearOver(t["gambeson"], t["shirt"]));
        Assert.True(Fit.CanWearOver(t["mail_hauberk"], t["gambeson"]));
        Assert.True(Fit.CanWearOver(t["breastplate"], t["mail_hauberk"]));
        Assert.False(Fit.CanWearOver(t["breastplate"], t["shirt"]));
    }
}
