using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Tests.Combat.Resolver2;

/// <summary>Resolver v2 rules goldens (resolver-v2 8: RS-14 to RS-18, RS-20 to RS-24), determinism and the no-float rule.</summary>
[Collection("resolver2")]
public class ResolverV2RulesTests(V2Fixture f)
{
    // ---------------------------------------------------------------- RS-14 hit chance

    [Theory]
    [InlineData("upper_body", 5000, 6000, 4000, 7500)]
    [InlineData("upper_leg_l", 3000, 3600, 2400, 4500)]
    [InlineData("lower_leg_l", 2600, 3120, 2080, 3900)]
    [InlineData("head", 2200, 2640, 1760, 3300)]
    [InlineData("foot_l", 1490, 1790, 1190, 2230)]
    [InlineData("hand_l", 1310, 1570, 1050, 1970)]
    [InlineData("neck", 1400, 1680, 1120, 2100)]
    public void RS14_HitChanceGoldens(string part, int eq, int plus20, int minus20, int plus50)
    {
        var s = f.S("arming_sword", "slash", part, skill: 50);
        void At(int attacker, int want) { s.Skill = attacker; s.DefenderSkill = 50; Assert.InRange(f.R.HitChanceBp(s), want - 10, want + 10); }
        At(50, eq); At(70, plus20); At(30, minus20); At(100, plus50);
    }

    [Fact]
    public void RS14_CalledShotAtAnEye_Is2Point6Percent()
    {
        var s = f.S("spear", "thrust", "eye_l", skill: 50); s.DefenderSkill = 50;
        Assert.InRange(f.R.HitChanceBp(s), 254, 274);
        s.TargetPart = f.P("nose"); Assert.InRange(f.R.HitChanceBp(s), 650, 670);         // 22 x 0.30
        s.TargetPart = f.P("jaw"); Assert.InRange(f.R.HitChanceBp(s), 870, 890);          // 22 x 0.40
    }

    // ---------------------------------------------------------------- RS-15 gaps

    [Fact]
    public void RS15_ArmpitGap_BypassesMail_x035_StructuresX3()
    {
        (string, string)[] worn = [("gambeson", "linen"), ("mail_hauberk", "iron")];
        var gap = f.S("spear", "thrust", "upper_arm_r", worn: worn);
        gap.TargetGap = f.Human.GapIndex("armpit");
        var plain = f.S("spear", "thrust", "upper_arm_r", worn: worn);
        Assert.InRange(f.R.HitChanceBp(gap), f.R.HitChanceBp(plain) * 350 / 1000 - 1, f.R.HitChanceBp(plain) * 350 / 1000 + 1);
        int brachialGap = 0, brachialPlain = 0, ligGap = 0, ligPlain = 0, n = 4000;
        var arm = f.Human.Parts[f.P("upper_arm_r")];
        int bi = Array.FindIndex(arm.Structures, x => x.Id == "brachial_artery"), li = Array.FindIndex(arm.Structures, x => x.Id == "shoulder_lig");
        for (uint k = 0; k < n; k++)
        {
            var r = f.Impact(gap, k);
            foreach (var st in r.StepSpan)
                if (st.IsArmour && gap.Worn![st.Piece].Stats.Shape.Id == "mail_hauberk" && st.Outcome != LayerOutcome.NotCovered) Assert.Equal(LayerOutcome.Bypassed, st.Outcome);
            foreach (var w in r.WoundSpan) { if (w.Structure == bi) brachialGap++; if (w.Structure == li) ligGap++; }
            var p = f.Impact(plain, k);
            foreach (var w in p.WoundSpan) { if (w.Structure == bi) brachialPlain++; if (w.Structure == li) ligPlain++; }
        }
        Assert.InRange(brachialGap / (double)brachialPlain, 2.5, 3.5);
        Assert.InRange(ligGap / (double)ligPlain, 2.5, 3.5);
        // a blunt blow cannot slip through a gap
        var mace = f.S("mace", "bash", "upper_arm_r", worn: worn); mace.TargetGap = gap.TargetGap;
        foreach (var st in f.ImpactCovered(mace).StepSpan) Assert.NotEqual(LayerOutcome.Bypassed, st.Outcome);
    }

    // ---------------------------------------------------------------- RS-16, RS-17 facing and blocks

    [Fact]
    public void RS16_FlankAndRear_RaiseHitChance_AndAShieldBlocksNeither()
    {
        var s = f.S("arming_sword", "slash", "upper_body", skill: 0);
        int front = f.R.HitChanceBp(s);
        s.Facing = Facing.Flank; Assert.Equal(front * 1250 / 1000, f.R.HitChanceBp(s));
        s.Facing = Facing.Rear; Assert.Equal(front * 1500 / 1000, f.R.HitChanceBp(s));
        var heater = f.T.Shield("heater");
        s.Block = new BlockInput { Kind = BlockKind.Shield, Shield = heater, ArmPart = f.P("lower_arm_l") };
        foreach (var facing in new[] { Facing.Flank, Facing.Rear })
        {
            s.Facing = facing;
            Assert.Equal(LayerOutcome.NotCovered, Blocks.Resolve(s, V2Fixture.Key(0), 156_525, f.T.Cal).Outcome);
        }
        s.Block.Kind = BlockKind.Weapon; s.Facing = Facing.Rear;
        Assert.Equal(LayerOutcome.NotCovered, Blocks.Resolve(s, V2Fixture.Key(0), 156_525, f.T.Cal).Outcome);
        s.Facing = Facing.Flank;                                                           // a weapon can block a flank blow
        Assert.Equal(LayerOutcome.Blocked, Blocks.Resolve(s, V2Fixture.Key(0), 100_000, f.T.Cal).Outcome);
    }

    [Fact]
    public void RS17_Block_CapacityStopsOrPassesTheExcessToTheForearm()
    {
        var heater = f.T.Shield("heater");
        var sword = f.S("arming_sword", "slash", "upper_body");
        sword.Block = new BlockInput { Kind = BlockKind.Shield, Shield = heater, ShieldConditionPm = 1000, ArmPart = f.P("lower_arm_l") };
        var r = new BlowResult();
        f.R.Resolve(sword, V2Fixture.Key(0), r);
        Assert.True(r.Blocked); Assert.Equal(0, r.WoundCount); Assert.True(r.Block.Stopped);
        Assert.Equal(300 + 156_525 / 500, r.Block.StaminaX100);                            // 3 + M/50
        var axe = f.S("axe", "hack", "upper_body", skill: 100, style: AttackStyle.Heavy);
        axe.Block = sword.Block;
        f.R.Resolve(axe, V2Fixture.Key(0), r);
        Assert.True(r.Blocked); Assert.False(r.Block.Stopped); Assert.True(r.Block.Stagger);
        Assert.InRange(r.Block.ExcessMilli, 111_400, 112_400);                             // 311.9 - 200 = 111.9
        Assert.Equal(445, r.Block.ShieldLossPm);
        Assert.Equal(f.P("lower_arm_l"), r.Part);                                          // the excess lands, blunt, on the shield forearm
        Assert.DoesNotContain(r.WoundSpan.ToArray(), w => w.Kind == WoundKind.Cut);
        // a held weapon: base + skill / 2 (the base is content, combat/calibration.jsonc: 150 since the P4-C calibration), braced neighbours +10% each (max 2)
        var b = new BlockInput { Kind = BlockKind.Weapon, BlockerSkill = 50 };
        Assert.Equal((f.T.Cal.WeaponBlockBase + 50 / f.T.Cal.WeaponBlockSkillDivisor) * 1000L, Blocks.CapacityMilli(b, f.T.Cal));
        b = new BlockInput { Kind = BlockKind.Shield, Shield = heater, BracedNeighbours = 5 };
        Assert.Equal(240_000, Blocks.CapacityMilli(b, f.T.Cal));
    }

    // ---------------------------------------------------------------- RS-18 fit

    [Fact]
    public void RS18_AMisfitPieceLoses25PointsOfCoverage()
    {
        Assert.True(Fit.Fits(70000, 80000)); Assert.False(Fit.Fits(70000, 81000));
        Assert.True(Fit.Fits(70000, 60000)); Assert.False(Fit.Fits(70000, 59000));
        int Uncovered(int fit)
        {
            var s = f.S("arming_sword", "slash", "head");
            s.Worn = [WornV2.Of(f.T.Armour("helm", "steel"), fitCm3: fit)]; s.WornCount = 1;
            int n = 0;
            for (uint k = 0; k < 4000; k++) if (f.Impact(s, k).Steps[0].Outcome == LayerOutcome.NotCovered) n++;
            return n;
        }
        Assert.InRange(Uncovered(70000), 300, 500);                                        // 10% of 4000
        Assert.InRange(Uncovered(50000), 1300, 1500);                                      // 35%
    }

    // ---------------------------------------------------------------- RS-20, RS-21 ribs and the eye

    [Fact]
    public void RS20_APointBlowThatFailsTheRibs_SlipsBetweenThem35Percent()
    {
        var s = f.S("dagger", "stab", "upper_body", skill: 0, style: AttackStyle.Quick);
        int slips = 0, organs = 0;
        for (uint k = 0; k < 10_000; k++)
        {
            var r = f.Impact(s, k);
            var bone = r.StepSpan.ToArray().Last(st => !st.IsArmour && f.T.MaterialList[st.Tissue].Id == "bone_tissue");
            Assert.Equal(LayerOutcome.Stopped, bone.Outcome);                              // the point fails the ribs every time
            if (r.RibSlip) slips++;
            if (r.WoundSpan.ToArray().Any(w => w.Organ >= 0)) organs++;
        }
        Assert.InRange(slips, 3350, 3650);
        Assert.True(organs > 0 && organs <= slips);
    }

    [Fact]
    public void RS21_HostedEye_ClosedVisorNever_OpenFaceByArea()
    {
        int EyeHits(string helm, bool open)
        {
            var s = f.S("spear", "thrust", "head");
            s.Worn = [WornV2.Of(f.T.Armour(helm, "steel"), visorOpen: open)]; s.WornCount = 1;
            s.TargetGap = f.Human.GapIndex("face");
            int eye = f.P("eye_l"), n = 0;
            for (uint k = 0; k < 10_000; k++) if (f.Impact(s, k).WoundSpan.ToArray().Any(w => w.Part == eye)) n++;
            return n;
        }
        Assert.Equal(0, EyeHits("great_helm", false));
        Assert.InRange(EyeHits("helm", false), 430, 530);                                  // 3% x width 1.6 = 4.8%
        Assert.InRange(EyeHits("great_helm", true), 430, 530);                             // the visor raised
    }

    // ---------------------------------------------------------------- RS-22 to RS-24 determinism

    [Fact]
    public void RS22_DrawOrderIsLocked()
    {
        var s = f.S("arming_sword", "slash", "upper_arm_r", skill: 40, worn: [("shirt", "linen"), ("gambeson", "linen"), ("mail_hauberk", "iron"), ("cloak", "wool")]);
        s.Weapon = f.T.Weapon("arming_sword", "steel"); s.Attack = s.Weapon.AttackIndex("stab");
        var r = new BlowResult();
        uint serial = 0;
        for (; serial < 200; serial++) { f.R.Resolve(s, V2Fixture.Key(serial), r); if (r.Hit && !r.Redirected && r.WoundSpan.ToArray().Any(w => w.Structure >= 0)) break; }
        string got = string.Join(",", r.DrawSpan.ToArray().Select(d => $"{d.Stream:X}:{d.K}"));
        Assert.Equal(Golden.DrawOrder, $"{serial}|{got}");
    }

    private IEnumerable<StrikeV2> Table(int n, bool near = false)
    {
        string[][] weapons = [["arming_sword", "slash"], ["arming_sword", "stab"], ["axe", "hack"], ["mace", "bash"], ["spear", "thrust"], ["dagger", "stab"], ["great_axe", "hack"], ["war_hammer", "spike"]];
        (string, string)[][] kits =
        [
            [], [("tunic", "wool")], [("shirt", "linen"), ("gambeson", "linen")], [("gambeson", "linen"), ("mail_hauberk", "iron"), ("helm", "iron")],
            [("gambeson", "linen"), ("mail_hauberk", "steel"), ("breastplate", "steel"), ("great_helm", "steel"), ("vambraces", "steel"), ("greaves", "steel")],
        ];
        var targets = f.Human.Parts.Where(p => !p.Shell).Select(p => p.Index).ToArray();
        for (int i = 0; i < n; i++)
        {
            uint Rn(int k, uint mod) => DetRng.NextUInt(99, i, 0, 5, (uint)k) % mod;
            var wpn = weapons[Rn(0, (uint)weapons.Length)];
            var s = f.S(wpn[0], wpn[1], "upper_body", skill: (int)Rn(1, 101), style: (AttackStyle)Rn(2, 3), worn: kits[Rn(3, (uint)kits.Length)]);
            s.TargetPart = targets[Rn(4, (uint)targets.Length)];
            s.DefenderSkill = (int)Rn(5, 80);
            s.Facing = (Facing)Rn(6, 3);
            s.TargetGap = Rn(7, 4) == 0 ? (int)Rn(8, (uint)f.Human.Gaps.Length) : -1;
            s.Near = near;
            yield return s;
        }
    }

    private ulong TableHash(ResolverV2 res, int n, bool near = false)
    {
        var r = new BlowResult();
        ulong h = 14695981039346656037UL;
        int i = 0;
        foreach (var s in Table(n, near)) { res.Resolve(s, V2Fixture.Key((uint)i, 7, i / 16, (ulong)(i % 16)), r); h = r.Hash(h); i++; }
        return h;
    }

    [Fact]
    public void RS23_NearEqualsFar() => Assert.Equal(TableHash(f.R, 5000, near: false), TableHash(f.R, 5000, near: true));

    [Fact]
    public void RS24_IntegerDeterminism_100kBlowTableHash() => Assert.Equal(Golden.Table100k, TableHash(f.R, 100_000));

    [Fact]
    public void Determinism_SameSeedSameBlowTrace_AcrossTwoContentLoads()
    {
        var other = new ResolverV2(new CombatTables(V2Fixture.Load()).V2);
        Assert.Equal(TableHash(f.R, 3000), TableHash(other, 3000));
        // and a different seed changes it
        var r1 = new BlowResult(); var r2 = new BlowResult();
        var s = Table(1).First();
        f.R.Resolve(s, V2Fixture.Key(3, seed: 1), r1); other.Resolve(s, V2Fixture.Key(3, seed: 1), r2);
        Assert.Equal(r1.Hash(), r2.Hash());
        Assert.Equal(r1.DrawSpan.ToArray(), r2.DrawSpan.ToArray());
    }

    // ---------------------------------------------------------------- no floating point in the resolver

    [Fact]
    public void ResolverV2_KeepsNoFloatingPoint()
    {
        Type[] types = [typeof(ResolverV2), typeof(LayerTest), typeof(Blocks), typeof(BodyState), typeof(Burn), typeof(BodyVariation), typeof(BlowResult),
            typeof(StrikeV2), typeof(WoundV2), typeof(LayerStep), typeof(BlockInput), typeof(BlockOutcome), typeof(WornV2), typeof(PartState), typeof(CalibrationV2),
            typeof(MaterialV2), typeof(WeaponV2), typeof(AttackV2), typeof(ArmourV2), typeof(ShieldV2), typeof(PartV2), typeof(LayerV2), typeof(StructureV2), typeof(OrganV2)];
        const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
        static bool Float(Type t) => t == typeof(double) || t == typeof(float) || t == typeof(decimal) || t.IsArray && Float(t.GetElementType()!);
        foreach (var t in types)
        {
            foreach (var m in t.GetMethods(all))
            {
                Assert.False(Float(m.ReturnType), $"{t.Name}.{m.Name} returns a float");
                Assert.DoesNotContain(m.GetParameters(), p => Float(p.ParameterType));
            }
            foreach (var fi in t.GetFields(all)) Assert.False(Float(fi.FieldType), $"{t.Name}.{fi.Name} is a float");
            foreach (var pi in t.GetProperties(all)) Assert.False(Float(pi.PropertyType), $"{t.Name}.{pi.Name} is a float");
        }
        // the source of the blow path itself: no double, float, decimal or transcendental Math (the load step in CombatTablesV2 and BodyPlanV2 is the one exception)
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Ravensguard.Sim"))) dir = dir.Parent;
        foreach (var file in new[] { "ResolverV2.cs", "LayerTest.cs", "Blocks.cs", "BodyState.cs", "BodyExtras.cs", "ResolverTypes.cs" })
        {
            string text = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Ravensguard.Sim", "Combat", "Resolver2", file));
            Assert.DoesNotMatch(@"\b(double|float|decimal)\b|Math\.(Round|Pow|Sqrt|Sin|Cos|Exp|Log)|\d\.\d+[fFdDmM]?\b", StripComments(text));
        }
    }

    private static string StripComments(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, @"//[^\n]*|/\*.*?\*/", "", System.Text.RegularExpressions.RegexOptions.Singleline);

    [Fact]
    public void Phase4Streams_LieIn0x400Block()
    {
        uint[] p4 = [RngStreams.BodyVariation, RngStreams.MonsterVariation, RngStreams.HitRoll, RngStreams.Redirect, RngStreams.Coverage, RngStreams.StructureRoll,
            RngStreams.OrganRoll, RngStreams.BleedRate, RngStreams.HitJitter, RngStreams.Stumble, RngStreams.MeleeDefence];
        Assert.All(p4, v => Assert.InRange(v, 0x400u, 0x4FFu));
        Assert.Equal(0x410u, RngStreams.HitRoll); Assert.Equal(0x413u, RngStreams.StructureRoll); Assert.Equal(0x430u, RngStreams.MeleeDefence);
    }

    // ---------------------------------------------------------------- falls (resolver-v2 4.9)

    [Fact]
    public void Falls_3mBruise_6mCrack_12mBreak()
    {
        Assert.Equal(72_000, LayerTest.FallMomentumMilli(80, 3000, 0));
        BoneGrade At(int heightMm)
        {
            var s = f.S("mace", "bash", "lower_leg_l");
            var r = new BlowResult();
            f.R.ResolveFall(s, LayerTest.FallMomentumMilli(80, heightMm, 0), V2Fixture.Key(0), r);
            foreach (var w in r.WoundSpan) if (f.T.MaterialList[w.Tissue].Id == "bone_tissue") return w.Bone;
            return BoneGrade.Sound;
        }
        Assert.Equal(BoneGrade.Sound, At(3000));
        Assert.Equal(BoneGrade.Cracked, At(6000));
        Assert.Equal(BoneGrade.Broken, At(12000));
    }
}

/// <summary>Pinned goldens: recorded from this code; a change here is a change of the resolver's behaviour and must be deliberate.</summary>
internal static class Golden
{
    /// <summary>A sword stab at the right upper arm through cloak, mail, gambeson and shirt (serial 6): the hit roll, four coverage rolls outside in, then the arm's six structures in content order with the artery's rate drawn right after it.</summary>
    public const string DrawOrder = "6|410:0,412:0,412:1,412:2,412:3,413:0,415:0,413:1,413:2,413:3,413:4,413:5";
    // re-pinned in P4-C: the mace (velocity 1.5) and the pommel (0.8) were recalibrated in content (melee-and-ranged Notes); the formulas are unchanged
    public const ulong Table100k = 7788444446239837917UL;
}
