using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Ravensguard.Sim.Tests.Combat.Resolver2;

namespace Ravensguard.Sim.Tests.Items;

/// <summary>
/// items-quality-and-wear 8 (P5-A1), quality in the fight: IQ-2 (the Phase 4 calibration did not move: the fixed-seed fight hashes and the 2,000-duel harness, pinned from the Phase 4 code
/// before the enum swap), IQ-3 (a better blade cuts mail more often, DF-style: the layer test, not flat damage) and IQ-4 (better mail stops more arrows).
/// </summary>
[Collection("fight")]
public class QualityCombatTests(FightFixture fx)
{
    // ------------------------------------------------------------------ IQ-2

    /// <summary>Captured with the Phase 4 code at 1a10645 (the seven-tier enum, every kit Standard) before the swap: ML-15's skirmish hashes, WM-9's watched fight, and the 2,000-duel
    /// harness of ML-14 (wins of A, wins of B, fatal, knockouts, severs, blows landed, blows thrown).</summary>
    private static readonly (string Name, int WinsA, int WinsB, int Fatal, int Knockouts, long Severs, long Landed, long Thrown)[] Phase4Duels =
    [
        ("sword vs sword (padded)", 1003, 997, 854, 1892, 8, 12562, 42369),
        ("sword vs sword (unarmoured)", 1020, 978, 844, 1886, 6, 13696, 43661),
        ("spear vs sword (unarmoured)", 1242, 737, 607, 1901, 5, 8776, 25480),
        ("spear vs sword (padded)", 1186, 793, 608, 1901, 5, 8940, 26578),
        ("axe vs sword (mail)", 394, 1606, 761, 1909, 4, 12364, 35243),
        ("mace vs sword (padded)", 1390, 610, 272, 1972, 6, 11636, 25220),
        ("sword vs sword (plate)", 988, 1007, 319, 1930, 0, 18002, 53856),
        ("mace vs sword (plate)", 1232, 766, 132, 1963, 0, 17843, 36613),
        ("sword (padded) vs sword (plate)", 809, 1189, 689, 1917, 7, 14698, 47354),
        ("mace (padded) vs sword (plate)", 973, 1027, 470, 1959, 8, 15453, 32492),
        ("sword+heater vs sword (padded)", 1309, 684, 853, 1896, 7, 12251, 47915),
    ];

    [Fact]
    public void IQ2_ThePhase4FixedSeedHashesAndThe2000DuelHarnessDidNotMove()
    {
        ulong Skirmish(ulong seed) { var w = FightCalibration.Skirmish(fx.C, seed, 10); for (int i = 0; i < 600; i++) w.StepWithAi(); return w.Hash(); }
        Assert.Equal(0x18FD03B7D775922CUL, Skirmish(11));                                    // ML-15
        Assert.Equal(0x73DFA7FD5EB01376UL, Skirmish(12));
        var wm = FightCalibration.Skirmish(fx.C, 17, 6); wm.LodFighter = 0; wm.Fighters[0].Player = true; wm.SetWatch(0, true);
        for (int s = 0; s < 1600; s++) wm.StepWithAi();
        Assert.Equal(0x6AD564BBB2CAB7B2UL, wm.Hash());                                        // WM-9 (OV-1 is the "fight" line of GoldenHashes, unchanged)
        var rows = FightCalibration.MainPairings().Select(p => FightCalibration.Run(fx.C, p, 2000)).ToList();
        Assert.Equal(Phase4Duels.Length, rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            var (name, a, b, fatal, ko, severs, landed, thrown) = Phase4Duels[i];
            var r = rows[i];
            Assert.Equal(name, r.Name);
            Assert.Equal((2000, a, b, fatal, ko, severs, landed, thrown), (r.N, r.WinsA, r.WinsB, r.Fatal, r.Knockouts, r.Severs, r.Landed, r.Thrown));
        }
    }

    // ------------------------------------------------------------------ IQ-3

    /// <summary>2,000 blows on gambeson and iron mail, the same seeds and the same spread of arms (strength 500 to 2,000: the layer test is a threshold, so one arm cuts every time or never).</summary>
    private static (int Cuts, long Final) Blows(V2Fixture f, string shape, string attack, string material, ItemQuality q, int dullPm = 0)
    {
        int cut = 0; long final = 0;
        var r = new BlowResult();
        for (uint k = 0; k < 2000; k++)
        {
            var s = f.S(shape, attack, "upper_body", skill: 60, worn: [("gambeson", "linen"), ("mail_hauberk", "iron")]);
            s.Weapon = f.T.Weapon(shape, material, q);
            s.Strength = 500 + (int)(k * 1500 / 2000);
            s.WeaponDullPm = dullPm;
            f.R.ResolveImpact(s, V2Fixture.Key(k), r);
            foreach (var st in r.StepSpan) if (st.IsArmour && st.Piece == 1 && st.Outcome == LayerOutcome.Cut) cut++;
            final += r.FinalMilli;
        }
        return (cut, final);
    }

    [Fact]
    public void IQ3_ASteelSwordCutsMailOverPaddingMoreOftenAsItsTierRises_TheMaceIsTheSameAtEveryTier()
    {
        var f = new V2Fixture();
        int c = Blows(f, "arming_sword", "slash", "steel", ItemQuality.Common).Cuts, fi = Blows(f, "arming_sword", "slash", "steel", ItemQuality.Fine).Cuts,
            m = Blows(f, "arming_sword", "slash", "steel", ItemQuality.Masterwork).Cuts;
        Assert.True(c < fi && fi < m, $"cut through the mail: common {c}, fine {fi}, masterwork {m}");
        Assert.True(Blows(f, "arming_sword", "slash", "steel", ItemQuality.Poor).Cuts <= c);
        // a blunt weapon: quality sharpens, it adds no mass (DF): the same blows, to the milli-unit, at every tier
        var mc = Blows(f, "mace", "bash", "steel", ItemQuality.Common); var mf = Blows(f, "mace", "bash", "steel", ItemQuality.Fine); var mm = Blows(f, "mace", "bash", "steel", ItemQuality.Masterwork);
        Assert.Equal(0, mc.Cuts + mf.Cuts + mm.Cuts);
        Assert.Equal(mc.Final, mf.Final); Assert.Equal(mc.Final, mm.Final);
    }

    // ------------------------------------------------------------------ IQ-6 (on the fight's blows)

    [Fact]
    public void IQ6_ABladeAtCondition500CutsMailLessOften_AMaceNotAtAll()
    {
        var f = new V2Fixture();
        var sharp = Blows(f, "arming_sword", "slash", "steel", ItemQuality.Common); var dull = Blows(f, "arming_sword", "slash", "steel", ItemQuality.Common, dullPm: 500);
        Assert.True(dull.Cuts < sharp.Cuts, $"dull {dull.Cuts}, sharp {sharp.Cuts}");
        Assert.Equal(Blows(f, "mace", "bash", "steel", ItemQuality.Common).Final, Blows(f, "mace", "bash", "steel", ItemQuality.Common, dullPm: 500).Final);
    }

    // ------------------------------------------------------------------ IQ-4

    [Fact]
    public void IQ4_FineMailStopsMoreBodkinsThanCommon_OnlyQaDiffers()
    {
        var bodkin = WeaponMatrix.Weapons(fx.C).Single(w => w.Name == "longbow bodkin");
        var (wv, attack, m0) = WeaponMatrix.Arm(new FightWorld(fx.C, 7), fx.C, bodkin);
        var t = fx.C.T; var plan = t.Plan("human"); var res = new ResolverV2(t);
        int Penetrations(string material, ItemQuality q)
        {
            int pen = 0; var r = new BlowResult();
            for (uint k = 0; k < 2000; k++)                                                 // the same 2,000 arrows: the longbow's bodkin from 20% to 200% of its 10 m momentum
            {
                var s = StrikeV2.Create(wv, attack, plan, plan.IndexOf("upper_body"), 50);
                s.Worn = [WornV2.Of(t.Armour("tunic", "linen")), WornV2.Of(t.Armour("mail_hauberk", material), t.Qa(q))]; s.WornCount = 2;
                s.Missile = true; s.ExtraMomentumMilli = m0 * (200 + k * 1800 / 2000) / 1000;
                res.ResolveImpact(s, new BlowKey(7, k, 1, k), r);
                foreach (var st in r.StepSpan) if (st.IsArmour && st.Piece == 1 && st.Outcome == LayerOutcome.Cut) pen++;
            }
            return pen;
        }
        foreach (var mat in new[] { "iron", "steel" })
        {
            int common = Penetrations(mat, ItemQuality.Common), fine = Penetrations(mat, ItemQuality.Fine);
            Assert.True(fine < common, $"{mat} mail: fine {fine}, common {common}");
        }
        // the harness as the resolver reads it differs in Qa only
        var a = WornV2.Of(t.Armour("mail_hauberk", "iron"), t.Qa(ItemQuality.Common)); var b = WornV2.Of(t.Armour("mail_hauberk", "iron"), t.Qa(ItemQuality.Fine));
        Assert.Equal((a.Stats, a.ConditionPm, a.FitCm3), (b.Stats, b.ConditionPm, b.FitCm3));
        Assert.Equal((1000, 1400), (a.QualityPm, b.QualityPm));
    }
}
