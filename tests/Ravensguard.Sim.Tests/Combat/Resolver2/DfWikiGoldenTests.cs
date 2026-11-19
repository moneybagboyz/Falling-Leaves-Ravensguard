using Ravensguard.Content;
using Ravensguard.Sim.Combat.Resolver2;

namespace Ravensguard.Sim.Tests.Combat.Resolver2;

/// <summary>
/// The DF wiki's own numbers as goldens (docs/specs/phase4/df-calibration.md, read Nov 2026): RS-1b (the battle-axe momentum table) is in <see cref="ResolverV2GoldenTests"/>;
/// these are RS-25 to RS-30. Each test names the page it transcribes and the tolerance it allows for our integer units.
/// </summary>
[Collection("resolver2")]
public class DfWikiGoldenTests(V2Fixture f)
{
    // ---------------------------------------------------------------- RS-25 weight matters little

    [Fact]
    public void RS25_WeaponWeight_MattersLittle()
    {
        // Gold (main namespace): "blunt attacks will have less than 1% more momentum than silver (despite having almost twice the density)": a DF war hammer, size 400, velocity 2
        long silver = LayerTest.BaseMomentumMilli(60000, 1250, 2000, 10490L * 400, 60000);
        long gold = LayerTest.BaseMomentumMilli(60000, 1250, 2000, 19320L * 400, 60000);
        Assert.True(gold > silver && (gold - silver) * 100 < silver, $"gold {gold}, silver {silver}");
        // Weapon, "Combat testing": for blunt weapons "all six standard weapon metals perform nearly identically": our mace in our five within 1% of each other
        var m = new[] { "iron", "steel", "bronze", "copper", "silver" }.Select(x => f.R.InitialMomentumMilli(f.S("mace", "bash", "upper_body", material: x))).ToList();
        Assert.True((m.Max() - m.Min()) * 100 < m.Min(), string.Join(", ", m));
    }

    // ---------------------------------------------------------------- RS-26 quality

    [Fact]
    public void RS26_QualityMultipliers_ItemQualityTable()
    {
        // DF2014:Item quality: armour deflect 1, 1.2, 1.4, 1.6, 1.8, 2, artifact 3 (Qa); sharpness 50, 60, 70, 80, 90, 100, artifact 100% of the max edge (Qw relative to standard)
        Assert.Equal(new[] { 1000, 1200, 1400, 1600, 1800, 2000, 3000 }, CombatTablesV2.QualityPm);
        Assert.Equal(new[] { 1000, 1200, 1400, 1600, 1800, 2000, 2000 }, CombatTablesV2.EdgeQualityPm);
        for (int q = 0; q < QualityTable.Count; q++)
        {
            Assert.Equal(CombatTablesV2.EdgeQualityPm[q], f.T.Weapon("arming_sword", "steel", (Quality)q).QwPm);
            Assert.Equal(CombatTablesV2.EdgeQualityPm[q], (int)Math.Round(QualityTable.EdgeMultiplier((Quality)q) * 1000));
            Assert.Equal(CombatTablesV2.QualityPm[q], (int)Math.Round(QualityTable.Multiplier((Quality)q) * 1000));
        }
        Assert.Equal(f.T.Weapon("arming_sword", "steel", Quality.Masterwork).QwPm, f.T.Weapon("arming_sword", "steel", Quality.Artifact).QwPm);
        // Material science: "masterwork armor provides only about 15% more protection than low-quality one": (10 + 2 x 2) / (10 + 2) and (2 + 0.8) / (2 + 0.4), +16.7%
        var sword = f.T.Weapon("arming_sword", "steel");
        var mail = f.T.Armour("mail_hauberk", "iron");
        long cut1 = LayerTest.CutNeedMilli(mail.ShearYield, mail.ShearFracture, sword.Head, sword.QwPm, 30_000, 1000, 1000, 1000);
        long cut5 = LayerTest.CutNeedMilli(mail.ShearYield, mail.ShearFracture, sword.Head, sword.QwPm, 30_000, 2000, 1000, 1000);
        Assert.InRange(cut5 * 1000 / cut1, 1165, 1168);
        long b1 = LayerTest.BluntNeedMilli(mail.ImpactFracture, mail.ImpactYield, 20_000, 1000, 1000, 1000);
        long b5 = LayerTest.BluntNeedMilli(mail.ImpactFracture, mail.ImpactYield, 20_000, 2000, 1000, 1000);
        Assert.InRange(b5 * 1000 / b1, 1165, 1168);
        // and a masterwork blade halves the need of a standard one (Qw 2)
        long cutMw = LayerTest.CutNeedMilli(mail.ShearYield, mail.ShearFracture, sword.Head, 2000, 30_000, 1000, 1000, 1000);
        Assert.InRange(cutMw * 2, cut1 - 2, cut1 + 2);
    }

    // ---------------------------------------------------------------- RS-27 the blunt need

    [Fact]
    public void RS27_MinimumMaceMomentum_MaterialScienceTable()
    {
        // Material science, the "min M" column: the momentum "some mace" (contact area 20) needs to break armour of each material. The column is (2 IF - IY) x 2 x 20, the crush
        // formula with Qa 0; ours uses the formula's legend, Qa 1 for a standard piece (x 2.4, 20% more), so this calls it with Qa 0. Tolerance: 1 unit (the column rounds 2 IF - IY
        // to two places first), 0.05 for the cloth-weak ones.
        (string Material, long WikiMilli, long TolMilli)[] rows =
        [
            ("steel", 142_000, 1000), ("iron", 65_000, 1000), ("bronze", 43_000, 1000), ("copper", 52_000, 1000), ("silver", 34_000, 1000),
            ("bone", 8_000, 1000), ("leather", 400, 50), ("oak", 400, 50),
        ];
        foreach (var (mat, wiki, tol) in rows)
        {
            var m = f.T.Material(mat);
            long ours = LayerTest.BluntNeedMilli(m.ImpactFracture, m.ImpactYield, 20_000, 0, 1000, 1000);
            Assert.True(Math.Abs(ours - wiki) <= tol, $"{mat}: ours {ours / 1000.0:0.00}, wiki {wiki / 1000.0:0.00}");
            Assert.InRange(LayerTest.BluntNeedMilli(m.ImpactFracture, m.ImpactYield, 20_000, 1000, 1000, 1000), ours * 12 / 10 - 2, ours * 12 / 10 + 2);
        }
    }

    // ---------------------------------------------------------------- RS-28 the materials

    [Fact]
    public void RS28_TheWeaponAndArmourMaterials_AreTheDf2014Raws()
    {
        // inorganic_metal.txt and material_template_default.txt as the wiki shows them: density, impact yield / fracture / strain, shear yield / fracture / strain, max edge (pm)
        (string Id, int Density, long IY, long IF, int IS, long SY, long SF, int SS, int Edge)[] raws =
        [
            ("steel", 7850, 1_505_000, 2_520_000, 940, 430_000, 720_000, 215, 1000),
            ("iron", 7850, 542_500, 1_085_000, 319, 155_000, 310_000, 189, 1000),
            ("bronze", 8250, 602_000, 843_500, 547, 172_000, 241_000, 156, 1000),
            ("copper", 8930, 245_000, 770_000, 175, 70_000, 220_000, 145, 1000),
            ("silver", 10490, 350_000, 595_000, 350, 100_000, 170_000, 333, 1000),
            ("gold", 19320, 175_000, 350_000, 97, 50_000, 100_000, 185, 1000),
            ("lead", 11340, 35_000, 42_000, 760, 10_000, 12_000, 179, 1000),
            ("tin", 7280, 42_000, 350_000, 724, 12_000, 100_000, 66, 1000),
            ("bone", 500, 200_000, 200_000, 100, 115_000, 130_000, 100, 100),
            ("leather", 500, 10_000, 10_000, 50_000, 25_000, 25_000, 50_000, 100),
            ("oak", 0, 10_000, 10_000, 1000, 40_000, 40_000, 1000, 100),    // density real oak's (750), DF's wood template 500: SI by design
        ];
        foreach (var r in raws)
        {
            var m = f.T.Material(r.Id);
            if (r.Density > 0) Assert.Equal(r.Density, m.DensityKgM3);
            Assert.Equal((r.Id, r.IY, r.IF, r.IS, r.SY, r.SF, r.SS), (r.Id, m.ImpactYield, m.ImpactFracture, m.ImpactStrain, m.ShearYield, m.ShearFracture, m.ShearStrain));
            if (r.Id != "leather") Assert.Equal(r.Edge, m.SharpPm);     // DF leather has no edge (0); it is never a weapon here
        }
    }

    // ---------------------------------------------------------------- RS-29 body part sizes

    [Fact]
    public void RS29_HumanPartSizes_MaterialScienceTable()
    {
        // Material science, the human column (total 70,000): part size = creature size x relative size. The hand and foot are the page's rounded percent column (1.4% and
        // 2.2%, 2.9% and 1.7% off the size column); the correction to DF's 1.44% and 2.16% waits for the abstract re-fit (df-calibration.md row 25)
        (string Part, int Wiki, int Relsize)[] rows =
        [
            ("upper_body", 12621, 1000), ("lower_body", 12621, 1000), ("neck", 1262, 100), ("head", 3786, 300), ("upper_arm_l", 2524, 200), ("lower_arm_l", 2524, 200),
            ("hand_l", 1009, 80), ("upper_leg_l", 6310, 500), ("lower_leg_l", 5048, 400), ("foot_l", 1514, 120),
        ];
        var h = f.Human;
        Assert.Equal(70000, h.SizeCm3);
        int trunk = h.Parts[f.P("upper_body")].RelPpm;
        foreach (var (part, wiki, rel) in rows)
        {
            var p = h.Parts[f.P(part)];
            long ours = (long)h.SizeCm3 * p.RelPpm / 1_000_000;
            bool deferred = part is "hand_l" or "foot_l";
            Assert.True(Math.Abs(ours - wiki) * 1000 <= wiki * (deferred ? 30 : 5), $"{part}: ours {ours}, wiki {wiki}");   // 0.5% (DF's total relsize is not exactly 1/0.18 of the trunk's)
            if (!deferred) Assert.Equal((long)trunk * rel, (long)p.RelPpm * 1000);                                    // the DF relsize ratios exactly
        }
    }

    // ---------------------------------------------------------------- RS-30 deflection and the strain rule

    [Fact]
    public void RS30_Deflection_AndTheStrainRule()
    {
        // Material science: "Most dedicated blunt weapons cannot be deflected by anything but slade" (2 x Sw x IYw < A x Da): a DF war hammer (size 400, steel, area 10) passes steel
        // plate (density 7.85) and bounces off slade (density 200, the page's table)
        var hammer = f.Db.Get<WeaponShapeDef>("war_hammer") with { Id = "df_war_hammer_400", HeadVolumeCm3 = 400, HaftVolumeCm3 = 0, HaftMaterial = null };
        var w = f.T.Synthetic(hammer, "steel");
        Assert.Equal(10_000, w.Attacks[0].AreaMilli);
        Assert.False(LayerTest.Deflects(w, 10_000, 7850));                                // 1,204 > 78.5
        Assert.True(LayerTest.Deflects(w, 10_000, 200_000));                              // 1,204 < 2,000
        // the strain rule over every weapon metal: rigid metal keeps at most 2% of a stopped blow (impact strain), at most 0.7% of a stopped edge (shear strain); mail keeps all of it
        foreach (var id in new[] { "iron", "steel", "bronze", "copper", "silver" })
        {
            var m = f.T.Material(id);
            Assert.InRange(LayerTest.AfterFail(100_000, m.ImpactStrain, f.T.Cal), 1, 2_000);
            Assert.InRange(LayerTest.AfterFail(100_000, m.ShearStrain, f.T.Cal), 1, 700);
            var mail = f.T.Armour("mail_hauberk", id);
            Assert.Equal(100_000, LayerTest.AfterFail(100_000, mail.ShearStrain, f.T.Cal));
            Assert.Equal(100_000, LayerTest.AfterFail(100_000, mail.ImpactStrain, f.T.Cal));
        }
    }
}
