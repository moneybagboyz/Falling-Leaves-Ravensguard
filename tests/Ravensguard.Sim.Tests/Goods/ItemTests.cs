using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Tests.Economy;

namespace Ravensguard.Sim.Tests.Goods;

public class ItemTests
{
    private static readonly GoodCatalog Cat = EconFixture.Catalog();
    private static readonly ItemFactory Factory = new(EconFixture.Content, Cat);
    private static EntityId Owner => EntityId.Make(IdKind.Person, 1);

    [Fact] // G-I1
    public void G_I1_ArmingSwordMass()
    {
        var shape = EconFixture.Content.Get<WeaponShapeDef>("arming_sword");
        var parts = Factory.WeaponParts(shape, "steel");
        Assert.Equal(("steel", 1413), (parts[0].Material, parts[0].MassG));
        Assert.Equal(("ash", 40), (parts[1].Material, parts[1].MassG));                    // 40.8 g floored
        Assert.Equal(1453, parts.Sum(p => p.MassG));
        Assert.Equal(1453, Cat.Def(Cat.IdOf("arming_sword")).PieceMassG);                    // the counted piece weighs the same
        Assert.Equal(1413, ItemMath.ComponentMassG(180, 7850)); Assert.Equal(40, ItemMath.ComponentMassG(60, 680));
        // agrees with the combat tables (mass there is in mg, unfloored)
        var tables = new CombatTables(EconFixture.Content);
        Assert.InRange(tables.Weapon("arming_sword", "steel").MassKg * 1000 - 1453, 0, 1);
    }

    [Fact] // G-I2
    public void G_I2_ValueAtCommonNew_AndQualityAndCondition()
    {
        var items = new ItemTable(); var ids = new IdAllocator();
        var id = Factory.CreateWeapon(items, ids, "arming_sword", "steel", ItemQuality.Common, Owner, labourHours: 24, skill: 60);
        int row = items.IndexOf(id);
        Assert.Equal(2506, items.ValueMites(row, Factory.ValueCrownsPerKg));                  // 1,413 + 1 + 1,092
        items.At(row).Quality = ItemQuality.Fine; Assert.Equal(4010, items.ValueMites(row, Factory.ValueCrownsPerKg));
        items.At(row).Quality = ItemQuality.Crude; Assert.Equal(1253, items.ValueMites(row, Factory.ValueCrownsPerKg));
        items.At(row).Quality = ItemQuality.Common; items.At(row).ConditionPermille = 700; Assert.Equal(1980, items.ValueMites(row, Factory.ValueCrownsPerKg));
        items.At(row).ConditionPermille = 1000; items.At(row).Stains = 2; Assert.Equal(2005, items.ValueMites(row, Factory.ValueCrownsPerKg));   // 2 stains: -20%
        Assert.Equal(1092, ItemMath.LabourMites(24, ItemMath.WageSkilled, 60));
        Assert.Equal(1453, items.MassG(row, Cat));
        items.At(row).Stains = 0; Assert.Equal(2506, Factory.ValueMites(items, id));
    }

    [Theory] // G-I3
    [InlineData(60, -20, 0, ItemQuality.Common)] [InlineData(60, 20, 10, ItemQuality.Masterwork)] [InlineData(10, -20, 0, ItemQuality.Crude)]
    [InlineData(20, 0, 0, ItemQuality.Poor)] [InlineData(75, 0, 0, ItemQuality.Fine)] [InlineData(14, 0, 0, ItemQuality.Crude)] [InlineData(15, 0, 0, ItemQuality.Poor)] [InlineData(89, 0, 0, ItemQuality.Fine)]
    public void G_I3_QualityFromScore(int skill, int roll, int tool, ItemQuality expect) => Assert.Equal(expect, ItemQualityRules.FromScore(skill + roll + tool));

    [Fact]
    public void QualityRoll_IsDeterministicAndWithinTheSpread()
    {
        var crafter = EntityId.Make(IdKind.Person, 17);
        var scores = Enumerable.Range(0, 400).Select(s => ItemQualityRules.Score(9, 100, crafter, (uint)s, 50, 0)).ToList();
        Assert.Equal(scores, Enumerable.Range(0, 400).Select(s => ItemQualityRules.Score(9, 100, crafter, (uint)s, 50, 0)).ToList());
        Assert.InRange(scores.Min(), 30, 35); Assert.InRange(scores.Max(), 68, 70);
        Assert.Equal(0, ItemQualityRules.Score(9, 100, crafter, 0, 50, 99) - ItemQualityRules.Score(9, 100, crafter, 0, 50, 10));   // the tool bonus is clamped to +-10
    }

    [Fact] // G-I10
    public void G_I10_MaterialiseThenDematerialiseASword()
    {
        var led = new MassLedger(Cat.Count); var store = new Store(Cat, led, StoreKind.House, EntityId.Make(IdKind.Household, 1));
        ushort sword = Cat.IdOf("arming_sword"); var items = new ItemTable(); var ids = new IdAllocator();
        store.Create(sword, 3, CreateReason.Crafted);
        var id = Factory.Materialise(store, items, ids, sword, Owner)!.Value;
        Assert.Equal(2, store.Qty(sword)); Assert.Equal(1, items.Count);
        ref var r = ref items.Get(id);
        Assert.Equal((ItemQuality.Common, (short)1000), (r.Quality, r.ConditionPermille)); Assert.Equal("arming_sword", r.Shape); Assert.Equal(1453, items.MassG(items.IndexOf(id), Cat));
        Assert.Equal(3, led.Created(sword, CreateReason.Crafted)); Assert.Equal(1, led.Destroyed(sword, DestroyReason.Materialised));
        Assert.True(Factory.Dematerialise(store, items, id));
        Assert.Equal(3, store.Qty(sword)); Assert.Equal(0, items.Count); Assert.False(items.IsLive(id));
        var again = Factory.Materialise(store, items, ids, sword, Owner)!.Value;
        Assert.NotEqual(id, again); Assert.Equal(id.Counter + 1, again.Counter);               // the serial is not reused
        // a damaged one is not folded back
        items.Get(again).ConditionPermille = 600;
        Assert.False(Factory.Dematerialise(store, items, again)); Assert.Equal(2, store.Qty(sword));
        Assert.Null(Factory.Materialise(store, items, ids, Cat.IdOf("wheat"), Owner));             // no shape: stays counted
        var empty = new Store(Cat, led, StoreKind.House, EntityId.Make(IdKind.Household, 2));
        Assert.Null(Factory.Materialise(empty, items, ids, sword, Owner));
    }

    [Fact]
    public void ItemTable_Compaction_RemapsParents_AndRoundTrips()
    {
        var items = new ItemTable(); var ids = new IdAllocator();
        var a = items.Add(ids, "barrel", ItemQuality.Common, Owner, [new("oak", 18_000, 1000)]);
        var b = items.Add(ids, "sack", ItemQuality.Poor, Owner, []);
        var c = items.Add(ids, "jar", ItemQuality.Fine, Owner, [new("clay_dummy", 500, 900), new("oak", 50, 800)], parent: items.IndexOf(a));
        items.At(items.IndexOf(a)).ContentsGood = Cat.IdOf("ale"); items.At(items.IndexOf(a)).ContentsQty = 40_000;
        Assert.Equal(58_000, items.MassG(items.IndexOf(a), Cat));
        items.Free(b);
        var snap = items.Compact();
        Assert.Equal(2, snap.Rows.Length); Assert.Equal(-1, snap.Rows[0].Parent); Assert.Equal(0, snap.Rows[1].Parent);
        var back = ItemTable.FromSnapshot(snap);
        Assert.Equal(2, back.Count); Assert.Equal(["clay_dummy", "oak"], back.ComponentsOf(back.IndexOf(c)).Select(x => x.Material));
        Assert.Equal(40_000, back.At(back.IndexOf(a)).ContentsQty);
        Assert.Throws<KeyNotFoundException>(() => back.Get(b));
    }

    [Fact]
    public void ItemIds_ComeFromTheWorldAllocator_NeverReused()
    {
        var items = new ItemTable(); var ids = new IdAllocator();
        var seen = new HashSet<EntityId>();
        for (int i = 0; i < 50; i++)
        {
            var id = items.Add(ids, "x", ItemQuality.Common, Owner, []);
            Assert.True(seen.Add(id)); Assert.Equal(IdKind.Item, id.Kind);
            if (i % 3 == 0) items.Free(id);
        }
        Assert.Equal(50 - 17, items.Count);
    }
}
