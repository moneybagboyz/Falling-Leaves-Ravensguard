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
        // the DF wiki's numbers are RS-1b (the battle-axe table) and DfWikiGoldenTests (RS-25 to RS-30)
        Assert.Equal(92_862, LayerTest.BaseMomentumMilli(60000, 1250, 1250, 7850L * 800, 60000));
    }

    /// <summary>
    /// The DF wiki's worked example (DF2014:Material science, "Momenta for dwarves of strength 1250 hacking (velocity 1.25) with battle axes (size 800)"): every one of its 28 dwarf
    /// sizes x 8 metals, in ten-thousandths. The species size is the dwarf's 60,000, the row the individual's i_Size, the weapon's mass density x 800. Our milli-units floor where the
    /// wiki rounds to 4 places, so a cell may differ by up to 1 milli (10 here). Steel, iron, bronze, copper and silver take their density from our content, which is the wiki's.
    /// </summary>
    [Fact]
    public void RS1b_DfWiki_BattleAxeMomentumTable()
    {
        (string Name, int Density)[] metals = [("adamantine", 200), ("divine metal", 1000), ("steel", 7850), ("iron", 7850), ("bismuth bronze", 8250), ("bronze", 8250), ("copper", 8930), ("silver", 10490)];
        foreach (var m in new[] { "steel", "iron", "bronze", "copper", "silver" })
            Assert.Equal(metals.Single(x => x.Name == m).Density, f.T.Material(m).DensityKgM3);
        long[][] table =
        [
            [33750, 774194, 899550, 932489, 932489, 932730, 932730, 933092, 933745],
            [42750, 739827, 889944, 931161, 931161, 931467, 931467, 931923, 932748],
            [44100, 734934, 888520, 930963, 930963, 931277, 931277, 931748, 932599],
            [45000, 731707, 887574, 930830, 930830, 931151, 931151, 931632, 932500],
            [45900, 728509, 886630, 930698, 930698, 931025, 931025, 931515, 932400],
            [47250, 723764, 885217, 930499, 930499, 930836, 930836, 931340, 932251],
            [54150, 700444, 878066, 929485, 929485, 929871, 929871, 930447, 931489],
            [55860, 694895, 876312, 929235, 929235, 929632, 929632, 930226, 931301],
            [56250, 693642, 875912, 929177, 929177, 929577, 929577, 930176, 931258],
            [57000, 691244, 875146, 929067, 929067, 929473, 929473, 930079, 931175],
            [57624, 689262, 874509, 928976, 928976, 929386, 929386, 929999, 931107],
            [58140, 687632, 873983, 928900, 928900, 929314, 929314, 929932, 931050],
            [58800, 685558, 873312, 928804, 928804, 929221, 929221, 929847, 930977],
            [59850, 682283, 872245, 928650, 928650, 929075, 929075, 929711, 930861],
            [59976, 681893, 872117, 928631, 928631, 929057, 929057, 929695, 930847],
            [60000, 681818, 872093, 928628, 928628, 929054, 929054, 929692, 930845],
            [61200, 678119, 870878, 928452, 928452, 928887, 928887, 929537, 930713],
            [61740, 676468, 870332, 928373, 928373, 928811, 928811, 929467, 930653],
            [62424, 674388, 869642, 928273, 928273, 928716, 928716, 929379, 930578],
            [63000, 672646, 869061, 928189, 928189, 928636, 928636, 929305, 930514],
            [64260, 668866, 867794, 928004, 928004, 928460, 928460, 929142, 930376],
            [66150, 663277, 865901, 927728, 927728, 928197, 928197, 928899, 930168],
            [71250, 648649, 860832, 926983, 926983, 927487, 927487, 928242, 929607],
            [73500, 642398, 858615, 926655, 926655, 927175, 927175, 927953, 929360],
            [75000, 638298, 857143, 926436, 926436, 926966, 926966, 927760, 929196],
            [76500, 634249, 855676, 926217, 926217, 926758, 926758, 927567, 929031],
            [78750, 628272, 853485, 925890, 925890, 926446, 926446, 927278, 928784],
            [93750, 591133, 839161, 923711, 923711, 924370, 924370, 925357, 927143],
        ];
        foreach (var row in table)
            for (int c = 0; c < metals.Length; c++)
            {
                long ours = LayerTest.BaseMomentumMilli(60000, 1250, 1250, (long)metals[c].Density * 800, (int)row[0]) * 10;
                Assert.True(Math.Abs(ours - row[c + 1]) <= 10, $"size {row[0]} {metals[c].Name}: ours {ours / 10_000.0:0.0000}, wiki {row[c + 1] / 10_000.0:0.0000}");
            }
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

    /// <summary>The mace of resolver-v2 table 4.4 (bash velocity 1.0, skill 50): the content mace was recalibrated to 1.5 in P4-C, the table's worked case keeps its inputs.</summary>
    private StrikeV2 TableMace(string part)
    {
        var def = f.Db.Get<WeaponShapeDef>("mace");
        var table = def with { Id = "mace_table_4_4", Attacks = [def.Attacks[0] with { Velocity = 1.0 }] };
        var s = StrikeV2.Create(f.T.Synthetic(table, "steel"), 0, f.Human, f.P(part), 50);
        s.Worn = []; s.WornCount = 0;
        return s;
    }

    [Fact]
    public void RS6_MaceBash_BareThigh_CracksBone()
    {
        var s = TableMace("upper_leg_l");
        Assert.InRange(f.R.InitialMomentumMilli(s), 126_600, 127_600);
        var r = f.ImpactCovered(s);
        Expect(r, s, ("skin", Crushed, 118_400), ("fat", Crushed, 103_500), ("muscle", Crushed, 86_500), ("bone_tissue", Crushed, -1));
        Assert.Equal(BoneGrade.Cracked, Bone(r).Bone);
        Assert.Equal(MuscleGrade.Torn, Muscle(r).Muscle);
    }

    [Fact]
    public void RS7_MaceBash_Gambeson_AbsorbsToABruise()
    {
        var s = TableMace("upper_leg_l"); s.Worn = [WornV2.Of(f.T.Armour("gambeson", "linen"))]; s.WornCount = 1;
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
        Assert.True(f.P("ribs_l") == Bone(r).Part || f.P("ribs_r") == Bone(r).Part);   // the bone wound is on a rib side
    }

    [Fact]
    public void RS8b_RestOfTable44()
    {
        // spear vs mail over gambeson: mail does not stop a skill-50 spear
        var s = f.S("spear", "thrust", "upper_body", worn: [("gambeson", "linen"), ("mail_hauberk", "iron")]);
        var r = f.ImpactCovered(s);
        Expect(r, s, ("mail_hauberk", Cut, 117_400), ("gambeson", Cut, 111_500), ("skin", Cut, -1), ("fat", Cut, -1), ("muscle", Cut, -1), ("bone_tissue", Cut, 49_200));
        // spear vs steel breastplate over gambeson: the point fails the plate's cut need (168) and skids off it (the strain rule: close-out item 2; table 4.4 had the plate crushed
        // at the point's small area, as a hammer would, and a cracked rib beneath): next to nothing goes on, the plate is only scuffed
        s = f.S("spear", "thrust", "upper_body", worn: [("gambeson", "linen"), ("breastplate", "steel")]);
        r = f.ImpactCovered(s);
        Assert.Equal(LayerOutcome.Stopped, r.Steps[0].Outcome);
        Assert.InRange(r.Steps[0].NeedMilli, 167_000, 169_000);                          // plate cut need 168
        Assert.True(r.Steps[0].OutMilli < 1_000, $"{r.Steps[0].OutMilli}");
        Assert.Equal(ArmourHarm.None, r.Steps[0].Harm);
        Assert.DoesNotContain(r.WoundSpan.ToArray(), w => w.Kind == WoundKind.Cut || w.Organ >= 0 || f.T.MaterialList[w.Tissue].Id == "bone_tissue" && w.Bone >= BoneGrade.Cracked);
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
        // the wiki's rule (Weapon, Material science): a blow failing both tests keeps M x strain / 50,000, "most rigid metal armor reduces blocked attacks by 98%-99%"; the
        // strains are the DF2014 raws (steel shear 215, impact 940; iron impact 319; bronze impact 547)
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
        // equal skill, shape and quality; a seeded spread of skills (0-100) and styles; the outer piece cut, against leather, a bone breastplate, bronze mail and iron mail (the DF
        // wiki's Weapon page, "Combat testing": adamantine > steel > iron >= the bronzes > copper > silver). The bone plate separates copper from bronze: with DF's leather both cut a
        // jerkin every time and neither cuts mail of their own tier
        string[] metals = ["silver", "copper", "bronze", "iron", "steel"];
        (string, string)[] armour = [("jerkin", "leather"), ("breastplate", "bone"), ("mail_hauberk", "bronze"), ("mail_hauberk", "iron")];
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
