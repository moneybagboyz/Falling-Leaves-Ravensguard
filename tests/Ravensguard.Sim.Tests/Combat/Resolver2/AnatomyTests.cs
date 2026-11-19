using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Tests.Combat.Resolver2;

/// <summary>Anatomy and body plans goldens (anatomy-and-body-plans.md 8: AN-1 to AN-14) and the Phase 4 content checks.</summary>
[Collection("resolver2")]
public class AnatomyTests(V2Fixture f)
{
    private BodyPlanV2 H => f.Human;
    private short Mat(string id) => (short)f.T.Material(id).Index;

    private WoundV2 BoneW(string part, BoneGrade g) => new() { Part = (short)f.P(part), Tissue = Mat("bone_tissue"), Structure = -1, Organ = -1, Kind = WoundKind.Crush, DepthPm = 1000, Grade = (byte)g };
    private WoundV2 MuscleW(string part, MuscleGrade g) => new() { Part = (short)f.P(part), Tissue = Mat("muscle"), Structure = -1, Organ = -1, Kind = WoundKind.Cut, DepthPm = 1000, Grade = (byte)g };
    private static WoundV2 Sever(BodyPlanV2 plan, string part, int count = 0) => new() { Part = (short)plan.IndexOf(part), Tissue = 0, Structure = -1, Organ = -1, Kind = WoundKind.Cut, DepthPm = 1000, Count = (byte)count, Flags = WoundFlags.Severed };
    private static WoundV2 Struct(BodyPlanV2 plan, string part, string id, WoundKind kind = WoundKind.Cut, int depth = 1000, byte grade = 0)
    {
        int p = plan.IndexOf(part);
        int si = Array.FindIndex(plan.Parts[p].Structures, s => s.Id == id);
        Assert.True(si >= 0, $"{part} has no {id}");
        return new WoundV2 { Part = (short)p, Tissue = (short)plan.Parts[p].Structures[si].Tissue.Index, Structure = (short)si, Organ = -1, Kind = kind, DepthPm = depth, Grade = grade };
    }
    private BodyState State(params WoundV2[] w) => BodyState.From(H, w);

    [Fact]
    public void AN1_HumanV2Loads_35Instances_HostsGapsAndAreas()
    {
        var def = f.Db.Get<BodyPlanDef>("human");
        string[] added = ["eye", "ear", "nose", "skull", "jaw", "teeth", "ribs", "collarbone", "pelvis", "kneecap", "thumb", "fingers"];
        Assert.Equal(22, def.Parts.Count);
        Assert.All(added, id => Assert.Contains(def.Parts, p => p.Id == id && p.Host != null));
        Assert.Equal(35, H.Parts.Length);
        Assert.Equal(16, f.Tables.BodyPlans["human"].Parts.Count);                          // resolver v1 keeps its 16
        Assert.Equal(19, H.Parts.Count(p => p.IsHosted));
        foreach (var p in H.Parts.Where(p => p.IsHosted))
        {
            Assert.InRange(p.Host, 0, H.Parts.Length - 1);
            Assert.False(H.Parts[p.Host].IsHosted);
        }
        foreach (var d in def.Parts.Where(p => p.ExposureGap != null)) Assert.Contains(def.NamedGaps, g => g.Name == d.ExposureGap);
        foreach (var p in H.Parts) Assert.True(p.Structures.Sum(s => s.AreaPpm) <= 450_000, $"{p.Id} structures sum {p.Structures.Sum(s => s.AreaPpm)}");
        // six ligament joints; motor and sensory nerves per limb section; the cord in neck, upper and lower body
        Assert.Equal(new[] { "ankle", "elbow", "hip", "knee", "shoulder", "wrist" },
            H.Parts.SelectMany(p => p.Structures).Where(s => s.Kind == StructureKind.Ligament).Select(s => s.Joint!).Distinct().OrderBy(x => x));
        foreach (var b in new[] { "upper_arm", "lower_arm", "hand", "upper_leg", "lower_leg", "foot" })
        {
            var p = H[b + "_l"];
            Assert.Contains(p.Structures, s => s.Kind == StructureKind.MotorNerve);
            Assert.Contains(p.Structures, s => s.Kind == StructureKind.SensoryNerve);
        }
        Assert.Equal(3, H.Parts.SelectMany(p => p.Structures).Count(s => s.Kind == StructureKind.SpinalCord));
        Assert.Equal(new[] { f.P("skull") }, H["head"].Shells);
        Assert.Equal(new[] { f.P("ribs_l"), f.P("ribs_r") }, H["upper_body"].Shells);
    }

    [Fact]
    public void AN2_Migration_AV1ArmNerveWoundLoadsAsMotorAndSensory()
    {
        Assert.Equal(new[] { "arm_motor", "arm_sensory" }, H.MigrateStructure("arm_nerve"));
        var v1 = new Wound("upper_arm_r", "nerve", "arm_nerve", WoundKind.Cut, Fixed.One, Fixed.Zero, WoundFlags.NerveHit);
        var v2 = WoundMigration.FromV1(f.T, H, v1).ToArray();
        Assert.Equal(2, v2.Length);
        Assert.Contains(v2, w => w.Has(WoundFlags.MotorNerveHit));
        Assert.Contains(v2, w => w.Has(WoundFlags.SensoryNerveHit));
        var bs = BodyState.From(H, v2);
        Assert.True(bs.Parts[f.P("hand_r")].Paralysed && bs.Parts[f.P("hand_r")].Numb);
    }

    [Fact]
    public void AN3_Walk_Kneecap_Crutch_Pelvis()
    {
        Assert.Equal(1000, State().WalkSpeedPm);
        var knee = BoneW("kneecap_r", BoneGrade.Broken);
        Assert.Equal(250, State(knee).WalkSpeedPm);
        Assert.Equal(550, BodyState.From(H, [knee], new BodyAids(Crutch: true)).WalkSpeedPm);
        Assert.Equal(100, State(BoneW("pelvis", BoneGrade.Broken)).WalkSpeedPm);
    }

    [Fact]
    public void AN4_Grip_ThumbAndFingers()
    {
        Assert.Equal(1000, State().Grip[1]);
        Assert.Equal(700, State(Sever(H, "thumb_r")).Grip[1]);
        var bs = State(Sever(H, "fingers_r", 4));
        Assert.False(bs.WeaponHand[1]);
        Assert.True(bs.WeaponHand[0]);
    }

    [Fact]
    public void AN5_Collarbone_NoOverhead_ShieldLow()
    {
        var bs = State(BoneW("collarbone_l", BoneGrade.Broken));
        Assert.False(bs.Overhead[0]); Assert.True(bs.Overhead[1]);
        Assert.Equal(-150, bs.ShieldCoverDeltaPm[0]);
    }

    [Fact]
    public void AN6_Sight_OneEyeAndBlind()
    {
        var eye = new WoundV2 { Part = (short)f.P("eye_l"), Tissue = Mat("organ"), Structure = -1, Organ = -1, Kind = WoundKind.Cut, DepthPm = 1000, Flags = WoundFlags.InsetHit };
        var one = State(eye);
        Assert.Equal(500, one.Sight); Assert.Equal(800, one.SpotPm);
        var both = State(eye, eye with { Part = (short)f.P("eye_r") });
        Assert.Equal(0, both.Sight);
        Assert.Equal(1000, both.VisionRadiusMm(12_000));                                   // 1 m whatever the torch
        Assert.Equal(6000, one.VisionRadiusMm(12_000));
    }

    [Fact]
    public void AN7_MotorNerveParalysesDistal_SensoryNumbs()
    {
        var motor = State(Struct(H, "upper_arm_r", "arm_motor"));
        foreach (var p in new[] { "upper_arm_r", "lower_arm_r", "hand_r", "thumb_r", "fingers_r" }) Assert.True(motor.Parts[f.P(p)].Paralysed, p);
        Assert.False(motor.Parts[f.P("upper_arm_l")].Paralysed);
        Assert.Equal(0, motor.Arm[1]); Assert.Equal(0, motor.Grip[1]);
        var sens = State(Struct(H, "upper_arm_r", "arm_sensory"));
        Assert.Equal(1000, sens.Arm[1]);
        Assert.Equal(0, sens.PainPm(f.P("hand_r")));
        Assert.Equal(1000, sens.PainPm(f.P("hand_l")));
        Assert.Equal(850, sens.BlowMomentumPm[1]);
    }

    [Fact]
    public void AN8_SpinalCord_LowerBackLegs_NeckCompleteStopsBreathing()
    {
        var low = State(Struct(H, "lower_body", "cord_back_l"));
        Assert.True(low.Parts[f.P("upper_leg_l")].Paralysed && low.Parts[f.P("foot_r")].Paralysed);
        Assert.False(low.Parts[f.P("upper_arm_l")].Paralysed);
        Assert.Equal(1000, low.Arm[0]);
        Assert.Equal(100, low.WalkSpeedPm);
        var neck = State(Struct(H, "neck", "cord_neck"));
        Assert.True(neck.BreathStopped);
        Assert.True(neck.Parts[f.P("hand_l")].Paralysed);
        Assert.Equal(240, BodyState.CordNeckDeadS);
        Assert.Equal(30, BodyState.CordNeckUnconsciousS);
        Assert.False(State(Struct(H, "neck", "cord_neck", depth: 600)).BreathStopped);    // partial: four limbs, still breathing
    }

    [Fact]
    public void AN9_GradedMuscle()
    {
        Assert.Equal(400, State(MuscleW("upper_arm_r", MuscleGrade.Torn)).Arm[1]);
        Assert.Equal(300, State(MuscleW("upper_arm_r", MuscleGrade.Torn), MuscleW("upper_arm_r", MuscleGrade.Bruised)).Arm[1]);
        Assert.Equal(750, State(MuscleW("lower_arm_l", MuscleGrade.Bruised)).Arm[0]);
        Assert.Equal(0, State(MuscleW("upper_arm_r", MuscleGrade.Mangled)).Arm[1]);
    }

    [Fact]
    public void AN10_HorsePlan_SizesAndHitFactors()
    {
        var horse = f.T.Plan("horse");
        Assert.Equal(866_000, horse.Parts.Where(p => !p.IsHosted && !p.Shell).Sum(p => p.RelPpm));
        Assert.Equal(1000, horse.Parts.Where(p => !p.IsHosted).Sum(p => p.RelPpm) / 1000 + horse.UntargetablePm);
        Assert.Equal(200_000, horse.LargestPpm);
        var s = StrikeV2.Create(f.T.Weapon("spear", "steel"), 0, horse, horse.IndexOf("chest"));
        s.Facing = Facing.Flank;                                                           // no front size bonus
        Assert.Equal(5000 * 1250 / 1000, f.R.HitChanceBp(s));                              // factor 1.0
        s.TargetPart = horse.IndexOf("head");
        Assert.Equal(2000 * 1250 / 1000, f.R.HitChanceBp(s));                              // factor 0.40
        s.Facing = Facing.Front;
        Assert.Equal(400_000L * (500 + 5 * 10) / 100_000, f.R.HitChanceBp(s));             // a large target: +10 points from the front
    }

    [Fact]
    public void AN11_HorseLameness()
    {
        var horse = f.T.Plan("horse");
        Assert.Equal(0, BodyState.From(horse, []).Lameness);
        Assert.Equal(3, BodyState.From(horse, [Struct(horse, "fore_leg_l_l", "fore_flexor")]).Lameness);   // the flexor tendons: a lame horse
        var torn = new WoundV2 { Part = (short)horse.IndexOf("hind_leg_u_r"), Tissue = Mat("muscle"), Structure = -1, Organ = -1, Kind = WoundKind.Cut, DepthPm = 1000, Grade = (byte)MuscleGrade.Torn };
        Assert.Equal(1, BodyState.From(horse, [torn]).Lameness);
    }

    [Fact]
    public void AN12_Draugr_NoBloodNoPain_HeadEndsIt_FireTakes30s()
    {
        var d = f.T.Plan("draugr");
        Assert.False(d.Bleeds); Assert.False(d.FeelsPain); Assert.Equal(0, d.BloodMl);
        Assert.All(d.Parts, p => Assert.Empty(p.Organs));
        Assert.DoesNotContain(d.Parts.SelectMany(p => p.Structures), s => s.Kind is StructureKind.Artery or StructureKind.Vein);
        Assert.Contains(d.Parts.SelectMany(p => p.Structures), s => s.Kind == StructureKind.MotorNerve);
        Assert.Equal("dried_flesh", d["upper_leg_l"].Layers[2].Tissue.Id);
        Assert.True(d["upper_leg_l"].Layers[2].IsMuscle);
        // blows draw no blood
        var s = StrikeV2.Create(f.T.Weapon("axe", "steel"), 0, d, d.IndexOf("upper_leg_l"), 80);
        s.DefenderSizeCm3 = d.SizeCm3;
        for (uint k = 0; k < 200; k++) Assert.All(f.Impact(s, k).WoundSpan.ToArray(), w => Assert.Equal(0, w.BleedX10));
        Assert.Equal(0, BodyState.From(d, []).PainPm(d.IndexOf("hand_l")));
        Assert.True(BodyState.From(d, [Sever(d, "head")]).Undone);
        Assert.False(BodyState.From(d, [Sever(d, "upper_arm_l"), Sever(d, "upper_arm_r"), Sever(d, "upper_leg_l"), Sever(d, "upper_leg_r")]).Undone);
        int ticks = Burn.TicksToBurnThrough(d["head"], f.T.Cal);
        Assert.InRange(ticks, 25 * 20, 35 * 20);
        var burnt = new WoundV2 { Part = (short)d.IndexOf("head"), Tissue = 0, Structure = -1, Organ = -1, Kind = WoundKind.Burn, DepthPm = 1000, Flags = WoundFlags.Burn };
        Assert.True(BodyState.From(d, [burnt]).Undone);
    }

    [Fact]
    public void AN13_CaveBeast_BlindEyes_ThroatGapX2_FleesBelow30Percent()
    {
        var b = f.T.Plan("cave_beast");
        Assert.Equal(50, b["eye_l"].FunctionPm("sight_pm"));
        Assert.Equal(100, BodyState.From(b, []).Sight);
        Assert.Equal(300, b.FleeBelowBloodPm);
        var neck = b["neck"];
        int carotid = Array.FindIndex(neck.Structures, x => x.Id == "carotid_artery");
        int Hits(bool throat)
        {
            var s = StrikeV2.Create(f.T.Weapon("spear", "steel"), 0, b, neck.Index, 100);
            s.DefenderSizeCm3 = b.SizeCm3; s.Style = AttackStyle.Heavy;
            if (throat) s.TargetGap = b.GapIndex("throat");
            int n = 0;
            for (uint k = 0; k < 4000; k++) if (f.Impact(s, k).WoundSpan.ToArray().Any(w => w.Part == neck.Index && w.Structure == carotid)) n++;
            return n;
        }
        int plain = Hits(false), gap = Hits(true);
        Assert.True(plain > 50, $"plain {plain}");
        Assert.InRange(gap / (double)plain, 1.7, 2.3);
    }

    [Fact]
    public void AN14_BodyHash_SameForTwoContentLoads()
    {
        ulong Run(CombatTablesV2 t)
        {
            var res = new ResolverV2(t);
            var plan = t.Plan("human");
            var r = new BlowResult();
            var w = t.Weapon("axe", "steel");
            ulong h = 14695981039346656037UL;
            var wounds = new List<WoundV2>();
            for (ulong person = 1; person <= 1000; person++)
            {
                var v = BodyVariation.For(77, person, plan);
                wounds.Clear();
                for (uint k = 0; k < 3; k++)
                {
                    var s = StrikeV2.Create(w, 0, plan, (int)(DetRng.NextUInt(77, (long)person, person, 9, k) % 16), 60);
                    s.DefenderSizeCm3 = v.SizeCm3;
                    res.Resolve(s, new BlowKey(77, (long)person, person, k), r);
                    foreach (var x in r.WoundSpan) wounds.Add(x);
                }
                h = BodyState.From(plan, [.. wounds]).Hash(h);
                h = (h ^ (ulong)v.SizeCm3 ^ ((ulong)v.BloodMl << 20) ^ (v.LeftHanded ? 1UL << 40 : 0)) * 1099511628211UL;
            }
            return h;
        }
        Assert.Equal(Run(f.T), Run(new CombatTables(V2Fixture.Load()).V2));
        // body variation is within its bands
        for (ulong p = 1; p < 500; p++)
        {
            var v = BodyVariation.For(3, p, H);
            Assert.InRange(v.SizeCm3, 70000 * 920 / 1000, 70000 * 1080 / 1000);
            Assert.InRange(v.BloodMl, 5000 * 950 / 1000, 5000 * 1050 / 1000);
        }
    }

    // ---------------------------------------------------------------- content

    [Fact]
    public void Content_Phase4ItemsAndPlansLoadAndValidate()
    {
        var shapes = f.Db.All<WeaponShapeDef>().ToList();
        Assert.Equal(22, shapes.Count(s => s.Family is not ("natural" or "practice" or "unarmed")));     // 18 and the lance, tourney lance, its stub and the billhook (mounted combat)
        Assert.Equal(46, f.Db.All<ArmourShapeDef>().Count());                                             // 42 and four barding rows
        Assert.Equal(4, f.Db.All<ShieldShapeDef>().Count());
        Assert.Equal(new[] { "cave_beast", "draugr", "horse", "human", "quadruped" }, f.T.Plans.Select(p => p.Id).OrderBy(x => x));
        foreach (var s in shapes)
        {
            Assert.True(s.ReachMm > 0, s.Id);
            Assert.All(s.Attacks, a => Assert.True(a.ContactArea > 0, $"{s.Id}.{a.Id}"));
        }
        var human = f.Db.Get<BodyPlanDef>("human").Parts.Select(p => p.Id).ToHashSet();
        foreach (var a in f.Db.All<ArmourShapeDef>())
        {
            // barding is made for the horse (mounted combat 3.1): its coverage is on horse parts and applies to no other plan
            var plan = a.Body.Length > 0 ? f.Db.Get<BodyPlanDef>(a.Body).Parts.Select(p => p.Id).ToHashSet() : human;
            Assert.All(a.Coverage.Keys, k => Assert.Contains(k, plan));
            Assert.All(a.Gaps, g => Assert.True(f.Human.GapIndex(g) >= 0, $"{a.Id} gap {g}"));
            Assert.InRange(a.BluntAbsorbPm, 0, 600);
        }
        // the layers the four DF layers need are all present
        Assert.All(Enum.GetValues<ArmourLayer>(), l => Assert.True(f.Db.All<ArmourShapeDef>().Count(a => a.Layer == l) >= 4, l.ToString()));
        Assert.True(f.T.WeaponEntries > 500 && f.T.ArmourEntries > 500);
        Assert.Equal(2500, f.T.Material("bone_tissue").CutResistPm);
        Assert.Equal(5000, f.T.Material("bone_tissue").CrushResistPm);
        Assert.Equal(1000, f.T.Material("ligament").CutResistPm);                          // inherited through copy_from (tendon)
        Assert.Equal(450, f.T.Armour("gambeson", "linen").BluntAbsorbPm);
        Assert.Equal(950, f.T.Cal.KeepPm);
    }

    [Fact]
    public void ClothingBookWornLayers_AreTheResolversArmourLayers()
    {
        var book = new Ravensguard.Sim.Clothing.ClothingBook(f.Db);
        var t = book.Track(new EntityId(5UL), Ravensguard.Sim.People.Sex.Male, Ravensguard.Sim.People.SocialClass.Peasant, default, 10, 1, 0, dress: false);
        foreach (var (shape, mat) in new[] { ("shirt", "linen"), ("gambeson", "linen"), ("mail_hauberk", "iron") })
            book.Wear(t, book.Create(book.Garments[shape], mat, t.Person, fitCm3: t.SizeCm3));
        var worn = new WornV2[8];
        int n = ClothingArmour.Fill(book, t, f.T, worn);
        Assert.Equal(3, n);
        var s = f.S("arming_sword", "slash", "upper_body");
        s.Worn = worn; s.WornCount = n; s.DefenderSizeCm3 = t.SizeCm3;
        var r = f.ImpactCovered(s);
        Assert.Equal("mail_hauberk", worn[r.Steps[0].Piece].Stats.Shape.Id);              // outside in
        int before = book.Items.Get(t.Worn[r.Steps[0].Piece]).ConditionPermille;
        ClothingArmour.ApplyWear(book, t, r);
        Assert.True(book.Items.Get(t.Worn[r.Steps[0].Piece]).ConditionPermille < before);
    }
}
