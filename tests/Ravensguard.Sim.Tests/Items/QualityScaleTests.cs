using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Ravensguard.Sim.Tests.Combat.Resolver2;
using Ravensguard.Sim.Tests.Content;
using Ravensguard.Sim.Tests.Economy;

namespace Ravensguard.Sim.Tests.Items;

/// <summary>
/// items-quality-and-wear 8 (P5-A1), the pure rules: IQ-1 (the one table), IQ-5 (masterwork rarity), IQ-6 (a dull edge), IQ-7 (wear by tier), IQ-8 (tool wear), IQ-9 (a tool breaks into
/// scrap), IQ-10 (rust), IQ-11 (the repair cap), IQ-13 (an old quality name in a mod), IQ-14 (named artefacts).
/// </summary>
public class QualityScaleTests
{
    private static ContentDatabase Db => EconFixture.Content;

    // ------------------------------------------------------------------ IQ-1

    [Fact]
    public void IQ1_TheContentRowLoads_AndIsTheOneTableCombatValueAndScoresRead()
    {
        var t = QualityTiers.From(Db);
        Assert.Equal(new[] { 800, 900, 1000, 1400, 2000 }, t.CombatPm);
        Assert.Equal(new[] { 500, 800, 1000, 1600, 3000 }, t.ValuePm);
        Assert.Equal(new[] { 1600, 1250, 1000, 800, 600 }, t.WearPm);
        Assert.Equal(new[] { 700, 800, 900, 950, 1000 }, t.RepairCapPm);
        Assert.Equal(new[] { 0, 15, 40, 70, 90 }, t.ScoreFrom);
        for (int q = 0; q < QualityTiers.Count; q++)
        {
            var iq = (ItemQuality)q;
            Assert.Equal(CombatTablesV2.QualityPmOf(iq), t.CombatPm[q]);
            Assert.Equal(ItemQualityRules.ValuePermille(iq), t.ValuePm[q]);
            Assert.Equal(QualityTiers.Default.CombatPm[q], t.CombatPm[q]);
        }
        foreach (var (score, tier) in new[] { (14, ItemQuality.Crude), (15, ItemQuality.Poor), (39, ItemQuality.Poor), (40, ItemQuality.Common), (69, ItemQuality.Common),
                     (70, ItemQuality.Fine), (89, ItemQuality.Fine), (90, ItemQuality.Masterwork) })
            Assert.Equal(tier, ItemQualityRules.FromScore(score));
        // the old seven-tier enum and its table are gone: one scale, five tiers, no artifact
        Assert.Null(typeof(OriginDef).Assembly.GetType("Ravensguard.Content.Quality"));
        Assert.Null(typeof(OriginDef).Assembly.GetType("Ravensguard.Content.QualityTable"));
        Assert.Equal(5, Enum.GetValues<ItemQuality>().Length);
        Assert.Equal(1000, QualityTiers.Default.Combat(ItemQuality.Common));                // Common is DF's standard: no Phase 3 or 4 number moves
    }

    // ------------------------------------------------------------------ IQ-5

    [Fact]
    public void IQ5_MasterworkIsRare_AMasterAt85MakesMasterwork4Point9_Fine82Point9_Common12Point2Percent()
    {
        var n = CraftMath.QualityMilli(75, 10, 0);                                          // skill 75, the best tools +10
        Assert.Equal(new long[] { 0, 0, 5_000, 34_000, 2_000 }, n);
        Assert.Equal(41_000, n.Sum());
        Assert.Equal(2_000, Enumerable.Range(90, 16).Sum(QualityTiers.InspirePm));          // scores 90..105: 50, 60, ... 200
        Assert.Equal("12.2", (100.0 * n[2] / 41_000).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("4.88", (100.0 * n[4] / 41_000).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("82.9", (100.0 * n[3] / 41_000).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(new[] { 50, 50, 60, 300, 300 }, new[] { QualityTiers.InspirePm(85 + 5), QualityTiers.InspirePm(90), QualityTiers.InspirePm(91), QualityTiers.InspirePm(115), QualityTiers.InspirePm(140) });
        Assert.Equal(0, QualityTiers.InspirePm(89));
        // the runner's own draws (streams 0x223 and 0x530) over 41,000 units land on the expectation
        int mw = 0, fine = 0, common = 0; var crafter = EntityId.Make(IdKind.Person, 77);
        for (uint u = 0; u < 41_000; u++)
        {
            int score = ItemQualityRules.Score(5, 1000, crafter, u, 75, 10);
            switch (ItemQualityRules.Crafted(5, 1000, crafter, u, score)) { case ItemQuality.Masterwork: mw++; break; case ItemQuality.Fine: fine++; break; case ItemQuality.Common: common++; break; }
        }
        Assert.InRange(mw, 2_000 - 280, 2_000 + 280);                                         // about 6 sigma of a binomial at 4.9%
        Assert.InRange(common, 5_000 - 400, 5_000 + 400);
        Assert.Equal(41_000, mw + fine + common);
    }

    // ------------------------------------------------------------------ IQ-6

    [Fact]
    public void IQ6_ADullEdgeAtCondition500CutsAt700Permille_BluntAttacksAreUnaffected()
    {
        Assert.Equal(700, QualityTiers.ConditionFactorPm(500));
        Assert.Equal(400, QualityTiers.ConditionFactorPm(0));
        Assert.Equal(1000, QualityTiers.ConditionFactorPm(1000));
        Assert.Equal(700, QualityTiers.EdgePm(1000, 500));
        Assert.Equal(1400, QualityTiers.EdgePm(2000, 500));
        var f = new V2Fixture();
        var sharp = f.S("arming_sword", "slash", "upper_body");
        var dull = sharp; dull.WeaponDullPm = 500;
        Assert.Equal(1000, sharp.EdgeQwPm); Assert.Equal(700, dull.EdgeQwPm);                 // the cut tests read EdgeQwPm (QualityCombatTests.IQ6 runs the blows)
    }

    // ------------------------------------------------------------------ IQ-7

    [Fact]
    public void IQ7_AFineBreastplateTakes8OfA10PmBlow_ACrudeOne16()
    {
        var t = QualityTiers.Default;
        Assert.Equal(8, t.ScaleWear(10, ItemQuality.Fine));
        Assert.Equal(16, t.ScaleWear(10, ItemQuality.Crude));
        Assert.Equal(10, t.ScaleWear(10, ItemQuality.Common));
        Assert.Equal(6, t.ScaleWear(10, ItemQuality.Masterwork));
        Assert.Equal(1, t.ScaleWear(1, ItemQuality.Masterwork));                             // at least 1 when the base was at least 1
        Assert.Equal(0, t.ScaleWear(0, ItemQuality.Crude));
        // on the fight's pieces: the same blow wears a fine plate 8 and a crude one 16
        var f = new V2Fixture();
        var plate = f.T.Armour("breastplate", "steel");
        var r = new BlowResult();
        r.Steps[0] = new LayerStep { Piece = 0, IsArmour = true, WearPm = 10, Outcome = LayerOutcome.Stopped }; r.StepCount = 1;
        WornV2[] fine = [WornV2.Of(plate, f.T.Qa(ItemQuality.Fine))], crude = [WornV2.Of(plate, f.T.Qa(ItemQuality.Crude))], common = [WornV2.Of(plate)];
        ArmourWear.Apply(fine, r); ArmourWear.Apply(crude, r); ArmourWear.Apply(common, r);
        Assert.Equal(992, fine[0].ConditionPm); Assert.Equal(984, crude[0].ConditionPm); Assert.Equal(990, common[0].ConditionPm);
    }

    // ------------------------------------------------------------------ IQ-8, IQ-9

    private static (EconomyState E, IdAllocator Ids, ToolWear W) Tools() => (EconFixture.NewEconomy(), new IdAllocator(), new ToolWear(Db));
    private static string? Cat(string m) => Db.TryGet<MaterialDef>(m, out var d) ? d.Category : null;

    [Fact]
    public void IQ8_ACommonChiselLoses300In100Hours_AMasterworkOne180()
    {
        var (e, ids, w) = Tools();
        Assert.Equal(3000, w.MilliPerHour("chisel", "chiselling"));
        Assert.Equal(500, w.MilliPerHour("needle", "sewing"));                               // 0.5 an hour is exact
        var owner = EntityId.Make(IdKind.Person, 900);
        int Row(ItemQuality q) => e.Items.IndexOf(e.Items.Add(ids, "chisel", q, owner, [new ItemComponent("iron", 250, 1000)]));
        int common = Row(ItemQuality.Common), master = Row(ItemQuality.Masterwork);
        Assert.False(w.UseHours(e.Items, common, "chiselling", 100, null, Cat));
        Assert.False(w.UseHours(e.Items, master, "chiselling", 100, null, Cat));
        Assert.Equal(700, e.Items.At(common).ConditionPermille);
        Assert.Equal(820, e.Items.At(master).ConditionPermille);
        // a needle at 0.5 an hour: 3 hours take 1 pm and carry half of one
        int needle = e.Items.IndexOf(e.Items.Add(ids, "needle", ItemQuality.Common, owner, [new ItemComponent("iron", 2, 1000)]));
        w.UseHours(e.Items, needle, "sewing", 3, null, Cat);
        Assert.Equal(999, e.Items.At(needle).ConditionPermille);
        Assert.Equal(500, QualityTiers.WearFraction(e.Items.At(needle).Wear));
        Assert.Equal(0, ToolWear.Bonus(300)); Assert.Equal(-3, ToolWear.Bonus(299));
    }

    [Fact]
    public void IQ9_AHammerAtZeroBreaks_ItsComponentsMassIsScrapInTheOwnersStore_TheMassLedgerHolds()
    {
        var (e, ids, w) = Tools();
        var owner = EntityId.Make(IdKind.Person, 901);
        var store = e.OpenStore(StoreKind.House, owner);
        var id = e.Items.Add(ids, "hammer", ItemQuality.Common, owner, [new ItemComponent("iron", 900, 1000), new ItemComponent("oak", 300, 1000)], conditionPermille: 5);
        int row = e.Items.IndexOf(id);
        ushort scrap = e.Goods.IdOf("scrap_iron"), shavings = e.Goods.IdOf("shavings");
        long iron0 = e.Mass.Created(scrap, CreateReason.Dematerialised), wood0 = e.Mass.Created(shavings, CreateReason.Dematerialised);
        Assert.True(w.UseHours(e.Items, row, "hammering", 10, store, Cat));               // 1 pm an hour: 5 hours break it
        Assert.False(e.Items.IsLive(id));
        Assert.Equal(900, store.Qty(scrap)); Assert.Equal(300, store.Qty(shavings));
        Assert.Equal(900, e.Mass.Created(scrap, CreateReason.Dematerialised) - iron0);       // counted as the documented faucet, gram for gram
        Assert.Equal(300, e.Mass.Created(shavings, CreateReason.Dematerialised) - wood0);
        foreach (var g in new[] { scrap, shavings }) Assert.Equal(e.Mass.Net(g), e.OtherStores.Values.Sum(s => s.Qty(g)) + Enumerable.Range(0, e.HouseholdCount).Sum(h => e.HouseholdStore[h].Qty(g)));
    }

    // ------------------------------------------------------------------ IQ-10

    [Fact]
    public void IQ10_ASteelSwordInAnUnroofedShedRusts60In30Days_AnOiledOneAQuarterAsFast()
    {
        var e = EconFixture.NewEconomy(); var ids = new IdAllocator(); var owner = EntityId.Make(IdKind.Person, 902);
        int sword = e.Items.IndexOf(e.Items.Add(ids, "arming_sword", ItemQuality.Common, owner, [new ItemComponent("steel", 1413, 1000), new ItemComponent("ash", 40, 1000)]));
        int oiled = e.Items.IndexOf(e.Items.Add(ids, "arming_sword", ItemQuality.Common, owner, [new ItemComponent("steel", 1413, 1000)]));
        int mail = e.Items.IndexOf(e.Items.Add(ids, "mail_hauberk", ItemQuality.Common, owner, [new ItemComponent("iron", 9000, 1000)]));
        int indoors = e.Items.IndexOf(e.Items.Add(ids, "arming_sword", ItemQuality.Common, owner, [new ItemComponent("steel", 1413, 1000)]));
        e.Items.At(oiled).Flags |= ItemFlags.Oiled;
        for (int d = 1; d <= 30; d++)
        {
            Corrosion.Day(e.Items, sword, Exposure.Damp, d, false, 0, null, Cat);
            Corrosion.Day(e.Items, oiled, Exposure.Damp, d, false, Corrosion.OilDays, null, Cat);
            Corrosion.Day(e.Items, mail, Exposure.Damp, d, true, 0, null, Cat);
            Corrosion.Day(e.Items, indoors, Exposure.Dry, d, false, 0, null, Cat);
        }
        Assert.Equal(940, e.Items.At(sword).ConditionPermille);
        Assert.Equal(985, e.Items.At(oiled).ConditionPermille);                            // P6-B6 (HW-12): a quarter of the rate, not none
        Assert.Equal(880, e.Items.At(mail).ConditionPermille);                              // rings rust twice as fast
        Assert.Equal(1000, e.Items.At(indoors).ConditionPermille);
        // the oil wears off after its 30 days
        Corrosion.Day(e.Items, oiled, Exposure.Damp, 31, false, Corrosion.OilDays, null, Cat);
        Assert.Equal(983, e.Items.At(oiled).ConditionPermille);
        Assert.Equal(ItemFlags.None, e.Items.At(oiled).Flags & ItemFlags.Oiled);
    }

    // ------------------------------------------------------------------ IQ-11

    [Fact]
    public void IQ11_AFineSwordReEdgedBySkill45ReachesItsCap950_BySkill20Only850()
    {
        var t = QualityTiers.Default;
        Assert.Equal(950, t.RepairCapFor(ItemQuality.Fine, 45));                             // min(950, common 900 + 50)
        Assert.Equal(850, t.RepairCapFor(ItemQuality.Fine, 20));                             // min(950, poor 800 + 50)
        Assert.Equal(1000, t.RepairCapFor(ItemQuality.Masterwork, 95));                      // a masterwork back to 1,000 only by a maker of its tier
        Assert.Equal(950, t.RepairCapFor(ItemQuality.Masterwork, 45));                       // a journeyman: 900 + 50
        // the value: each repair takes 100 pm off, at most 300
        ushort w = 0;
        Assert.Equal(1000, QualityTiers.RepairValuePm(w));
        w = QualityTiers.WithRepairCount(w, 2); Assert.Equal(800, QualityTiers.RepairValuePm(w));
        w = QualityTiers.WithRepairCount(w, 9); Assert.Equal(700, QualityTiers.RepairValuePm(w));
        Assert.Equal(1_754, ItemMath.ValueMites(2_506, ItemQuality.Common, 1000, 0, w));      // 2,506 x 0.7
        Assert.Equal(2_506, ItemMath.ValueMites(2_506, ItemQuality.Common, 1000));
    }

    // ------------------------------------------------------------------ IQ-13

    [Fact]
    public void IQ13_AModItemWithTheOldSuperiorLoadsAsFine_WithOneWarningNamingFileLineAndField()
    {
        using var t = new TempDir();
        var mod = t.Mod("oldnames", """
            [
              { "type": "origin", "id": "old_knight_son", "name": "Old knight's son", "description": "x", "order": 9,
                "kit": [ { "item": "arming_sword", "quality": "superior" } ], "coin": 5, "start_location": "town", "ambition": "regain_honour", "mentor": "old_knight" }
            ]
            """, "\"load_after\": [\"base\"], \"dependencies\": [\"base\"]", "kits.jsonc");
        var r = new ModLoader(ContentTypeRegistry.Default, BaseVocabulary.Create()).Load([Ravensguard.Sim.WorldGen.ContinentSettings.FindBaseContent()!, mod]);
        Assert.True(r.Success, r.Report.ToString());
        var warn = Assert.Single(r.Report.Warnings, w => w.Message.Contains("superior", StringComparison.Ordinal));
        Assert.EndsWith("kits.jsonc", warn.Location!.Value.File);
        Assert.True(warn.Location.Value.Line > 0);
        Assert.Contains("kit[0].quality", warn.Message);
        Assert.Contains("'fine'", warn.Message);
        var o = r.Database.Get<OriginDef>("old_knight_son");
        var hero = Ravensguard.Sim.Characters.HeroRecord.FromOrigin("Osric", false, o);
        Assert.Equal(ItemQuality.Fine, hero.Inventory.Single(i => i.Shape == "arming_sword").Quality);
        // a name that is neither a tier nor an old name is an error, the five names are silent
        Assert.True(QualityNames.TryParse("masterwork", out int mw, out bool alias, out _) && mw == 4 && !alias);
        Assert.True(QualityNames.TryParse("artifact", out int art, out _, out bool named) && art == 4 && named);
        Assert.False(QualityNames.TryParse("legendary", out _, out _, out _));
    }

    // ------------------------------------------------------------------ IQ-14

    [Fact]
    public void IQ14_TenThousandMasterworks_About1000AreNamed_EachWithItsMakersChronicleLine()
    {
        var items = new ItemTable(); var ids = new IdAllocator(); var chronicle = new Chronicle(); var book = new ArtefactBook();
        var maker = EntityId.Make(IdKind.Person, 4242);
        int named = 0;
        for (int i = 0; i < 10_000; i++)
        {
            var id = items.Add(ids, "arming_sword", ItemQuality.Masterwork, maker, [new ItemComponent("steel", 1413, 1000)]);
            if (Artefacts.TryName(Db, 7, items, id, maker, "Wulfric Ashford", 86_400L * 100, book, chronicle, ids) is { } name)
            {
                named++;
                Assert.EndsWith(" of Wulfric", name);
                Assert.True((items.Get(id).Flags & ItemFlags.Named) != 0);
            }
            else Assert.True((items.Get(id).Flags & ItemFlags.Named) == 0);
        }
        Assert.InRange(named, 1_000 - 95, 1_000 + 95);                                       // 3 sigma
        Assert.Equal(named, book.Count);
        var lines = chronicle.All.Where(c => c.TypeId == Artefacts.ChronicleType).ToList();
        Assert.Equal(named, lines.Count);
        Assert.All(lines, c => Assert.Equal(maker, c.Causes.Single().EventId));
        // not a masterwork, or not a weapon, armour piece or jewel: never named; and no tier above masterwork
        var fine = items.Add(ids, "arming_sword", ItemQuality.Fine, maker, [new ItemComponent("steel", 1413, 1000)]);
        Assert.Null(Artefacts.TryName(Db, 7, items, fine, maker, "W", 0, book, null, null));
        Assert.Equal(ItemQuality.Masterwork, Enum.GetValues<ItemQuality>().Max());
        // the same item id always gets the same name
        Assert.Equal(Artefacts.NameFor(Db, 7, EntityId.Make(IdKind.Item, 5), "Wulfric"), Artefacts.NameFor(Db, 7, EntityId.Make(IdKind.Item, 5), "Wulfric"));
    }
}
