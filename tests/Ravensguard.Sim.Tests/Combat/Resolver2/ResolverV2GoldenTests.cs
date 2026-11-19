using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Resolver2;

namespace Ravensguard.Sim.Tests.Combat.Resolver2;

/// <summary>Resolver v2 material-science goldens (resolver-v2 8: RS-1 to RS-13, RS-19; plan §6): the worked table of 4.4 and the metal ranking.</summary>
[Collection("resolver2")]
public class ResolverV2GoldenTests(V2Fixture f)
{
    /// <summary>The layers of a result as (name, outcome, out milli): armour by shape id, tissues by material id.</summary>
    private List<(string Name, LayerOutcome Outcome, long Out)> Layers(BlowResult r, in StrikeV2 s)
    {
        var l = new List<(string, LayerOutcome, long)>();
        foreach (var st in r.StepSpan)
        {
            string name = st.IsArmour ? s.Worn![st.Piece].Stats.Shape.Id : f.T.MaterialList[st.Tissue].Id;
            l.Add((name, st.Outcome, st.OutMilli));
        }
        return l;
    }

    private void Expect(BlowResult r, in StrikeV2 s, params (string Name, LayerOutcome Outcome, long OutMilli)[] want)
    {
        var got = Layers(r, s);
        Assert.True(got.Count >= want.Length, string.Join(" | ", got));
        for (int i = 0; i < want.Length; i++)
        {
            Assert.Equal(want[i].Name, got[i].Name);
            Assert.True(want[i].Outcome == got[i].Outcome, $"{want[i].Name}: {got[i].Outcome}, want {want[i].Outcome} ({string.Join(" | ", got)})");
            if (want[i].OutMilli >= 0) Assert.True(Math.Abs(want[i].OutMilli - got[i].Out) <= 1000, $"{want[i].Name}: {got[i].Out}, want {want[i].OutMilli} ({string.Join(" | ", got)})");
        }
    }

    private static readonly LayerOutcome Cut = LayerOutcome.Cut, Crushed = LayerOutcome.Crushed, Stopped = LayerOutcome.Stopped;

    private WoundV2 Bone(BlowResult r)
    {
        foreach (var w in r.WoundSpan) if (f.T.MaterialList[w.Tissue].Id == "bone_tissue" && w.Structure < 0 && w.Organ < 0) return w;
        throw new InvalidOperationException("no bone wound");
    }

    private WoundV2 Muscle(BlowResult r)
    {
        foreach (var w in r.WoundSpan) if (f.T.MaterialList[w.Tissue].Id == "muscle" && w.Structure < 0) return w;
        throw new InvalidOperationException("no muscle wound");
    }

    // ---------------------------------------------------------------- RS-1 momentum

    [Fact]
    public void RS1_InitialMomentum_IntegerFormula()
    {
        var s = f.S("arming_sword", "slash", "upper_leg_l", skill: 0);
        Assert.Equal(1_453_800, s.Weapon.MassMg);
        Assert.Equal(104_350, f.R.InitialMomentumMilli(s));
        s.Skill = 50; Assert.Equal(156_525, f.R.InitialMomentumMilli(s));
        s.Style = AttackStyle.Heavy; Assert.Equal(234_788, f.R.InitialMomentumMilli(s));
        s.Style = AttackStyle.Quick; Assert.Equal(78_263, f.R.InitialMomentumMilli(s));
        s.Style = AttackStyle.Normal; s.Prone = true; Assert.Equal(313_050, f.R.InitialMomentumMilli(s));
        // axe hack heavy: skill 50 = 234.0, skill 100 = 311.9 (table 4.4)
        var a = f.S("axe", "hack", "neck", skill: 50, style: AttackStyle.Heavy);
        Assert.InRange(f.R.InitialMomentumMilli(a), 233_500, 234_500);
        a.Skill = 100; Assert.InRange(f.R.InitialMomentumMilli(a), 311_400, 312_400);
        // the DF Material science battle-axe table shape: a dwarf (size 60,000, strength 1,250) hacking (1.25) with a size-800 axe
        Assert.Equal(92_862, LayerTest.BaseMomentumMilli(60000, 1250, 1250, 7850L * 800, 60000));
        Assert.Equal(68_181, LayerTest.BaseMomentumMilli(60000, 1250, 1250, 200L * 800, 60000));   // adamantine
    }

    // ---------------------------------------------------------------- RS-2 to RS-8 (and the rest of table 4.4)

    [Fact]
    public void RS2_SwordSlash_BareThigh_MuscleTorn_BoneBruised()
    {
        var s = f.S("arming_sword", "slash", "upper_leg_l");
        var r = f.ImpactCovered(s);
        Expect(r, s, ("skin", Cut, 148_700), ("fat", Cut, 141_300), ("muscle", Cut, 134_200), ("bone_tissue", Stopped, 91_000));
        Assert.Equal(MuscleGrade.Torn, Muscle(r).Muscle);
        Assert.Equal(1000, Muscle(r).DepthPm);
        Assert.Equal(BoneGrade.Sound, Bone(r).Bone);
        Assert.False(r.Severed);
    }

    [Fact]
    public void RS3_SwordSlash_ThroughGambeson()
    {
        var s = f.S("arming_sword", "slash", "upper_leg_l", worn: [("gambeson", "linen")]);
        var r = f.ImpactCovered(s);
        Expect(r, s, ("gambeson", Cut, 146_900), ("skin", Cut, -1), ("fat", Cut, -1), ("muscle", Cut, 125_900), ("bone_tissue", Stopped, -1));
        Assert.InRange(r.Steps[0].NeedMilli, 15_600, 16_600);                            // cut need 16.1
        Assert.False(r.Severed);
    }

    [Fact]
    public void RS4_SwordSlash_MailOverGambeson_IsABruise()
    {
        var s = f.S("arming_sword", "slash", "upper_leg_l", worn: [("gambeson", "linen"), ("mail_hauberk", "iron")]);
        var r = f.ImpactCovered(s);
        Expect(r, s, ("mail_hauberk", Crushed, 148_700), ("gambeson", Crushed, 77_700), ("skin", Crushed, 72_300), ("fat", Crushed, 63_200),
            ("muscle", Crushed, 52_900), ("bone_tissue", Stopped, 1_100));
        Assert.InRange(r.Steps[0].NeedMilli, 164_000, 165_000);                          // mail cut need 164.5 > 156.5
        Assert.DoesNotContain(r.WoundSpan.ToArray(), w => w.Kind == WoundKind.Cut);
        Assert.Equal(MuscleGrade.Torn, Muscle(r).Muscle);                                  // 63.2 into the muscle: torn, not mangled
    }

    [Fact]
    public void RS5_SwordSlash_MailAlone_MangledMuscleCrackedBone()
    {
        var s = f.S("arming_sword", "slash", "upper_leg_l", worn: [("mail_hauberk", "iron")]);
        var r = f.ImpactCovered(s);
        Expect(r, s, ("mail_hauberk", Crushed, 148_700), ("skin", Crushed, 138_400), ("fat", Crushed, 121_000), ("muscle", Crushed, 101_200),
            ("bone_tissue", Crushed, 58_000));
        Assert.Equal(MuscleGrade.Mangled, Muscle(r).Muscle);
        Assert.Equal(BoneGrade.Cracked, Bone(r).Bone);
    }

    [Fact]
    public void RS6_MaceBash_BareThigh_CracksBone()
    {
        var s = f.S("mace", "bash", "upper_leg_l");
        Assert.InRange(f.R.InitialMomentumMilli(s), 126_600, 127_600);
        var r = f.ImpactCovered(s);
        Expect(r, s, ("skin", Crushed, 118_400), ("fat", Crushed, 103_500), ("muscle", Crushed, 86_500), ("bone_tissue", Crushed, -1));
        Assert.Equal(BoneGrade.Cracked, Bone(r).Bone);
        Assert.Equal(MuscleGrade.Torn, Muscle(r).Muscle);
    }

    [Fact]
    public void RS7_MaceBash_Gambeson_AbsorbsToABruise()
    {
        var s = f.S("mace", "bash", "upper_leg_l", worn: [("gambeson", "linen")]);
        var r = f.ImpactCovered(s);
        Expect(r, s, ("gambeson", Crushed, 66_400), ("skin", Crushed, 61_800), ("fat", Crushed, 54_100), ("muscle", Crushed, 45_200), ("bone_tissue", Stopped, -1));
        Assert.Equal(MuscleGrade.Torn, Muscle(r).Muscle);
        Assert.Equal(BoneGrade.Sound, Bone(r).Bone);
    }

    [Fact]
    public void RS8_SpearThrust_Gambeson_Chest_BreachesRibs()
    {
        var s = f.S("spear", "thrust", "upper_body", worn: [("gambeson", "linen")]);
        Assert.InRange(f.R.InitialMomentumMilli(s), 159_800, 160_800);
        var r = f.ImpactCovered(s);
        Expect(r, s, ("gambeson", Cut, 152_300), ("skin", Cut, 144_600), ("fat", Cut, 137_400), ("muscle", Cut, 130_500), ("bone_tissue", Cut, 84_100));
        Assert.Equal(f.P("ribs_l") == Bone(r).Part || f.P("ribs_r") == Bone(r).Part, true);   // the bone wound is on a rib side
    }

    [Fact]
    public void RS8b_RestOfTable44()
    {
        // spear vs mail over gambeson: mail does not stop a skill-50 spear
        var s = f.S("spear", "thrust", "upper_body", worn: [("gambeson", "linen"), ("mail_hauberk", "iron")]);
        var r = f.ImpactCovered(s);
        Expect(r, s, ("mail_hauberk", Cut, 117_400), ("gambeson", Cut, 111_500), ("skin", Cut, -1), ("fat", Cut, -1), ("muscle", Cut, -1), ("bone_tissue", Cut, 49_200));
        // spear vs steel breastplate over gambeson: plate crushed, padding absorbs, a cracked rib, no organ
        s = f.S("spear", "thrust", "upper_body", worn: [("gambeson", "linen"), ("breastplate", "steel")]);
        r = f.ImpactCovered(s);
        Expect(r, s, ("breastplate", Crushed, 99_200), ("gambeson", Crushed, 51_800), ("skin", Crushed, -1), ("fat", Crushed, -1), ("muscle", Crushed, -1), ("bone_tissue", Crushed, 18_000));
        Assert.InRange(r.Steps[0].NeedMilli, 167_000, 169_000);                          // plate cut need 168
        Assert.Equal(BoneGrade.Cracked, Bone(r).Bone);
        Assert.DoesNotContain(r.WoundSpan.ToArray(), w => w.Organ >= 0);
        // sword vs bronze mail: hard armour eats the blow
        s = f.S("arming_sword", "slash", "upper_leg_l", worn: [("mail_hauberk", "bronze")]);
        r = f.ImpactCovered(s);
        Expect(r, s, ("mail_hauberk", Cut, 78_900), ("skin", Cut, 72_600), ("fat", Cut, 68_900), ("muscle", Cut, 65_100), ("bone_tissue", Stopped, -1));
        // axe hack heavy skill 50 on the bare neck: no sever, cracked vertebra
        s = f.S("axe", "hack", "neck", style: AttackStyle.Heavy);
        r = f.ImpactCovered(s);
        Expect(r, s, ("skin", Cut, 222_300), ("fat", Cut, 211_200), ("muscle", Cut, 200_600), ("bone_tissue", Crushed, -1));
        Assert.Equal(BoneGrade.Cracked, Bone(r).Bone);
        Assert.False(r.Severed);
        // skill 100: the bone is cut with 130.0 left (0.417 of M0): severed
        s = f.S("axe", "hack", "neck", skill: 100, style: AttackStyle.Heavy);
        r = f.ImpactCovered(s);
        Expect(r, s, ("skin", Cut, -1), ("fat", Cut, -1), ("muscle", Cut, 267_500), ("bone_tissue", Cut, 130_000));
        Assert.True(r.Severed);
    }

    // ---------------------------------------------------------------- RS-9 to RS-13

    [Fact]
    public void RS9_RigidMetalSoaks98To99Point6PercentOfAStoppedBlow()
    {
        var c = f.T.Cal;
        Assert.Equal(430, LayerTest.AfterFail(100_000, f.T.Material("steel").ShearStrain, c));
        Assert.Equal(1_880, LayerTest.AfterFail(100_000, f.T.Material("steel").ImpactStrain, c));
        Assert.Equal(638, LayerTest.AfterFail(100_000, f.T.Material("iron").ImpactStrain, c));
        Assert.Equal(1_094, LayerTest.AfterFail(100_000, f.T.Material("bronze").ImpactStrain, c));
        // through the resolver: a quick dagger slash stopped by a breastplate keeps 0.43%
        var s = f.S("dagger", "slash", "upper_body", skill: 0, style: AttackStyle.Quick, worn: [("breastplate", "steel")]);
        var r = f.ImpactCovered(s);
        var st = r.Steps[0];
        Assert.Equal(LayerOutcome.Stopped, st.Outcome);
        Assert.Equal(st.InMilli * 215 / 50_000, st.OutMilli);
    }

    [Fact]
    public void RS10_MailTurnsACutIntoBlunt_WithoutDampening()
    {
        var s = f.S("arming_sword", "slash", "upper_body", worn: [("mail_hauberk", "iron")]);
        var r = f.ImpactCovered(s);
        Assert.Equal(LayerOutcome.Crushed, r.Steps[0].Outcome);
        Assert.Equal(156_525, r.Steps[0].InMilli);
        Assert.Equal(156_525 * 950 / 1000, r.Steps[0].OutMilli);                          // 148.7 = 0.95 x M in
        Assert.DoesNotContain(r.WoundSpan.ToArray(), w => w.Kind == WoundKind.Cut);         // blunt from there on
    }

    [Fact]
    public void RS11_LightWeaponsDeflectOffDenseArmour()
    {
        var dagger = f.T.Weapon("dagger", "steel");
        Assert.Equal(65_000, dagger.VolumeMilli);
        Assert.False(LayerTest.Deflects(dagger, 10_000, 7850));                            // 195.7 > 78.5
        var club = f.T.Weapon("club", "oak");
        Assert.True(LayerTest.Deflects(club, 40_000, 7850));                               // 20 < 314
        var s = f.S("club", "bash", "upper_body", material: "oak", worn: [("breastplate", "steel")]);
        var r = f.ImpactCovered(s);
        Assert.True(r.Deflected);
        Assert.Equal(LayerOutcome.Deflected, r.Steps[0].Outcome);
        Assert.Equal(0, r.WoundCount);
    }

    [Fact]
    public void RS12_Severing_NeedsReachLeftAfterArmourAndMomentum()
    {
        var bare = f.S("axe", "hack", "neck", skill: 100, style: AttackStyle.Heavy);
        Assert.True(f.ImpactCovered(bare).Severed);
        // a 4 mm mail coif: reach 106 < 110, and the coif eats the blow
        var coif = f.S("axe", "hack", "neck", skill: 100, style: AttackStyle.Heavy, worn: [("mail_coif", "iron")]);
        Assert.False(f.ImpactCovered(coif).Severed);
        // 8 mm of cloth (hood and padded coif): the axe's 110 leaves 102, short of the neck's 110; the great axe's 160 leaves 152
        var cloth = new[] { ("hood", "wool"), ("padded_coif", "linen") };
        Assert.False(f.ImpactCovered(f.S("axe", "hack", "neck", skill: 100, style: AttackStyle.Heavy, worn: cloth)).Severed);
        Assert.True(f.ImpactCovered(f.S("great_axe", "hack", "neck", skill: 100, style: AttackStyle.Heavy, worn: cloth)).Severed);
        // blunt and point blows never sever
        Assert.False(f.ImpactCovered(f.S("maul", "smash", "hand_l", skill: 100, style: AttackStyle.Heavy)).Severed);
        Assert.False(f.ImpactCovered(f.S("spear", "thrust", "hand_l", skill: 100, style: AttackStyle.Heavy)).Severed);
    }

    [Fact]
    public void RS13_BoneIsAStop_SwordSkill50NeverCutsTheThighBone()
    {
        var s = f.S("arming_sword", "slash", "upper_leg_l");
        var r = new BlowResult();
        int hits = 0;
        for (uint k = 0; k < 10_000; k++)
        {
            f.R.Resolve(s, V2Fixture.Key(k), r);
            if (!r.Hit || r.Part != s.TargetPart) continue;
            hits++;
            foreach (var w in r.WoundSpan)
                if (f.T.MaterialList[w.Tissue].Id == "bone_tissue") Assert.NotEqual(WoundKind.Cut, w.Kind);
            Assert.False(r.Severed);
        }
        Assert.InRange(hits, 4_000, 5_000);                                               // the 45% hit chance of an upper leg at +50 skill
    }

    [Fact]
    public void RS19_TatteredArmourNeedsAboutHalfTheMomentum()
    {
        Assert.Equal(625, LayerTest.ConditionPm(250));
        var sword = f.T.Weapon("arming_sword", "steel");
        var gam = f.T.Armour("gambeson", "linen");
        long fresh = LayerTest.CutNeedMilli(gam.ShearYield, gam.ShearFracture, sword.Head, sword.QwPm, 30_000, 1000, LayerTest.ConditionPm(1000), 1000);
        long tattered = LayerTest.CutNeedMilli(gam.ShearYield, gam.ShearFracture, sword.Head, sword.QwPm, 30_000, 1000, LayerTest.ConditionPm(250), 1000);
        Assert.InRange(fresh, 16_000, 16_200);                                            // 16.1
        Assert.InRange(tattered, 9_950, 10_150);                                          // 16.1 x 0.625 = 10.0 (the spec rounds to 10.1)
        // and through the resolver, the trace carries the lower need
        var s = f.S("arming_sword", "slash", "upper_body");
        s.Worn = [WornV2.Of(gam, conditionPm: 250)]; s.WornCount = 1;
        Assert.InRange(f.ImpactCovered(s).Steps[0].NeedMilli, 9_950, 10_150);
    }

    // ---------------------------------------------------------------- the metal ranking (df-calibration.md golden)

    [Fact]
    public void MetalRanking_SilverCopperBronzeIronSteel_CutRateAgainstTheSameArmour()
    {
        // equal skill, shape and quality; a seeded spread of skills (0-100) and styles; the outer piece cut, against leather, bronze mail and iron mail
        string[] metals = ["silver", "copper", "bronze", "iron", "steel"];
        (string, string)[] armour = [("jerkin", "leather"), ("mail_hauberk", "bronze"), ("mail_hauberk", "iron")];
        var leather = f.T.Armour("jerkin", "leather");
        var rates = new List<int>();
        var needs = new List<long>();
        foreach (var m in metals)
        {
            var w = f.T.Weapon("arming_sword", m);
            needs.Add(LayerTest.CutNeedMilli(leather.ShearYield, leather.ShearFracture, w.Head, w.QwPm, 30_000, 1000, 1000, 1000));
            int cuts = 0;
            foreach (var a in armour)
                for (int i = 0; i < 2000; i++)
                {
                    var s = f.S("arming_sword", "slash", "upper_body", skill: (int)(Ravensguard.Sim.Core.DetRng.NextUInt(7, i, 0, 1, 0) % 101),
                        style: (AttackStyle)(Ravensguard.Sim.Core.DetRng.NextUInt(7, i, 0, 1, 1) % 3), material: m, worn: [a]);
                    var r = f.ImpactCovered(s);
                    if (r.Steps[0].Outcome == LayerOutcome.Cut) cuts++;
                }
            rates.Add(cuts);
        }
        for (int i = 1; i < metals.Length; i++)
        {
            Assert.True(needs[i] < needs[i - 1], $"{metals[i]} need {needs[i]} vs {metals[i - 1]} {needs[i - 1]}");
            Assert.True(rates[i] > rates[i - 1], $"cut rates {string.Join(", ", metals.Zip(rates))}");
        }
    }
}
