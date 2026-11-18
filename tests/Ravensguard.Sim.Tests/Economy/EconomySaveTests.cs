using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Tests.Economy;

public class EconomySaveTests
{
    private static byte[] Bytes(WorldState w) => SaveCodec.Encode(WorldSave.Capture(w, EconFixture.ContentHash, "0.1.0"));
    private static WorldState Load(byte[] b) => WorldSave.Restore(SaveCodec.Decode(b), EconFixture.Content, EconFixture.ContentHash);

    /// <summary>An economy that has lived a bit: needs, spoilage, transfers, items, a granary, a hoard.</summary>
    private static EconomyState Lived()
    {
        var pop = EconFixture.Pop; var e = EconFixture.NewEconomy();
        for (int d = 0; d < 5; d++)
        {
            for (int h = 0; h < Math.Min(400, pop.Households.Count); h++) HouseholdNeeds.RunDay(e, pop, h, pop.NowDay + d, d - 2);
            e.ApplyDailySpoilage(EconFixture.Seed, (pop.NowDay + d) * 24L, 5);
        }
        var l = e.Ledger;
        for (int h = 0; h < 50; h++) l.Transfer(e.HouseholdPurse[h], e.HouseholdPurse[h + 50], new Coin(10 + h), h % 2 == 0 ? TransferReason.Wage : TransferReason.Trade);
        var hoard = l.Open(PurseKind.Hoard, EntityId.Make(IdKind.Settlement, 1)); l.Transfer(e.HouseholdPurse[3], hoard, new Coin(25), TransferReason.Hoard);
        l.Sink(e.HouseholdPurse[7], new Coin(5), SinkReason.Melt);
        l.Mint(l.Open(PurseKind.Treasury, EntityId.Make(IdKind.Faction, 1)), new Coin(1234), SourceReason.Mint);
        var granary = e.OpenStore(StoreKind.Granary, EntityId.Make(IdKind.Settlement, 1), 0, 80_000_000);
        granary.Create(e.Goods.IdOf("wheat"), 4_000_000, CreateReason.Harvest);
        var chest = e.OpenStore(StoreKind.Chest, EntityId.Make(IdKind.Other, 3), 100_000, 150_000);
        chest.Create(e.Goods.IdOf("arming_sword"), 2, CreateReason.Crafted);
        var f = new ItemFactory(EconFixture.Content, e.Goods); var ids = new IdAllocator();
        var sword = f.Materialise(chest, e.Items, ids, e.Goods.IdOf("arming_sword"), EntityId.Make(IdKind.Person, 5))!.Value;
        e.Items.Get(sword).Wear = 77; e.Items.Get(sword).ConditionPermille = 640; e.Items.Get(sword).Stains = 1; e.Items.Get(sword).Flags = ItemFlags.Stained | ItemFlags.Wet;
        var barrel = e.Items.Add(ids, "barrel", ItemQuality.Fine, EntityId.Make(IdKind.Household, 2), [new("oak", 18_000, 900)], labourHours: 6, skill: 40);
        e.Items.At(e.Items.IndexOf(barrel)).ContentsGood = e.Goods.IdOf("ale"); e.Items.At(e.Items.IndexOf(barrel)).ContentsQty = 100_000;
        e.SeedReserveG[10] = 12_345;
        return e;
    }

    [Fact]
    public void SaveLoadSave_IsByteIdentical_WithAnEconomy()
    {
        var w = EconFixture.WorldWith(Lived());
        var bytes = Bytes(w);
        var loaded = Load(bytes);
        Assert.NotNull(loaded.Economy);
        Assert.Equal(bytes, Bytes(loaded));
        Assert.Equal(w.Economy!.ComputeHash(), loaded.Economy!.ComputeHash());
    }

    [Fact]
    public void Roundtrip_PreservesLedgerStoresItemsAndColumns()
    {
        var src = Lived(); var loaded = Load(Bytes(EconFixture.WorldWith(src))).Economy!;
        Assert.Equal(src.Ledger.Audit(), loaded.Ledger.Audit()); Assert.True(loaded.Ledger.Audit().IsConserved);
        Assert.Equal(src.Ledger.Export().Balance, loaded.Ledger.Export().Balance);
        Assert.Equal(src.Ledger.Flow(TransferReason.Wage), loaded.Ledger.Flow(TransferReason.Wage)); Assert.Equal(src.Ledger.Sunk(SinkReason.Melt), loaded.Ledger.Sunk(SinkReason.Melt));
        for (int h = 0; h < src.HouseholdCount; h++)
        {
            Assert.Equal(src.HouseholdStore[h].Rows(), loaded.HouseholdStore[h].Rows());
            Assert.Equal(src.HouseholdCoin(h), loaded.HouseholdCoin(h));
        }
        var gid = EntityId.Make(IdKind.Settlement, 1);
        Assert.Equal(src.OtherStores[gid].Rows(), loaded.OtherStores[gid].Rows());
        Assert.Equal((StoreKind.Granary, 80_000_000L, true), (loaded.OtherStores[gid].Kind, loaded.OtherStores[gid].CapacityVolumeMl, loaded.OtherStores[gid].EnforceCapacity));
        Assert.Equal(src.NutritionCd, loaded.NutritionCd); Assert.Equal(src.SeedReserveG, loaded.SeedReserveG); Assert.Equal(src.Grievances, loaded.Grievances); Assert.Equal(src.Tiers, loaded.Tiers);
        Assert.Equal(src.FoodCoverDaysX10, loaded.FoodCoverDaysX10);
        Assert.Equal(2, loaded.Items.Count);
        var sword = loaded.Items.LiveRows().First(); ref var r = ref loaded.Items.At(sword);
        Assert.Equal(("arming_sword", (short)640, (ushort)77, (byte)1), (r.Shape, r.ConditionPermille, r.Wear, r.Stains)); Assert.Equal(ItemFlags.Stained | ItemFlags.Wet, r.Flags);
        Assert.Equal(1453, loaded.Items.MassG(sword, loaded.Goods));
        Assert.True(loaded.MassAuditHolds()); Assert.True(src.MassAuditHolds());
        for (ushort g = 0; g < src.Goods.Count; g++) { Assert.Equal(src.Mass.TotalCreated(g), loaded.Mass.TotalCreated(g)); Assert.Equal(src.Mass.TotalDestroyed(g), loaded.Mass.TotalDestroyed(g)); }
    }

    [Fact]
    public void AfterLoad_TheEconomyKeepsRunning_Identically()
    {
        var src = Lived(); var loaded = Load(Bytes(EconFixture.WorldWith(src))).Economy!; var pop = EconFixture.Pop;
        for (int d = 5; d < 8; d++)
            foreach (var e in new[] { src, loaded })
            {
                for (int h = 0; h < 400; h++) HouseholdNeeds.RunDay(e, pop, h, pop.NowDay + d, 3);
                e.ApplyDailySpoilage(EconFixture.Seed, (pop.NowDay + d) * 24L, 3);
                e.Ledger.Transfer(e.HouseholdPurse[d], e.HouseholdPurse[d + 1], new Coin(3), TransferReason.Rent);
            }
        Assert.Equal(src.ComputeHash(), loaded.ComputeHash());
    }

    [Fact]
    public void WorldWithoutEconomy_HasNoEconomySection_AndLoadsWithNull()
    {
        var w = EconFixture.WorldWith(null);
        var file = WorldSave.Capture(w, EconFixture.ContentHash, "0.1.0");
        Assert.False(file.Sections.ContainsKey(WorldSave.EconomySection));
        Assert.Null(Load(SaveCodec.Encode(file)).Economy);
        Assert.True(WorldSave.Capture(EconFixture.WorldWith(EconFixture.NewEconomy()), EconFixture.ContentHash, "0.1.0").Sections.ContainsKey(WorldSave.EconomySection));
    }

    [Fact]
    public void Economy_DoesNotChangeThePopulationOrGenerationHashes()
    {
        var pop = EconFixture.Pop; string before = pop.ComputeHash();
        var e = EconFixture.NewEconomy(); for (int h = 0; h < 200; h++) HouseholdNeeds.RunDay(e, pop, h, pop.NowDay, 5);
        Assert.Equal(before, pop.ComputeHash());
        Assert.Equal(before, Load(Bytes(EconFixture.WorldWith(e))).Population.ComputeHash());
    }

    [Fact]
    public void TamperedEconomy_IsRejectedWithAReadableError()
    {
        var file = WorldSave.Capture(EconFixture.WorldWith(EconFixture.NewEconomy()), EconFixture.ContentHash, "0.1.0");
        var dto = SaveCodec.DeserializeCompressed<EconomySave>(file.Sections[WorldSave.EconomySection]);
        void Check(Action<EconomySave> tamper, string contains)
        {
            var d = SaveCodec.DeserializeCompressed<EconomySave>(SaveCodec.SerializeCompressed(dto)); tamper(d);
            var sections = file.Sections.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()); sections[WorldSave.EconomySection] = SaveCodec.SerializeCompressed(d);
            var tampered = new SaveFile(file.Header, sections);
            var ex = Assert.Throws<SaveException>(() => Load(SaveCodec.Encode(tampered)));
            Assert.Contains(contains, ex.Message);
        }
        Check(d => d.PurseBalance[0] += 1, "economy");                    // coin from nowhere: the ledger no longer balances
        Check(d => d.GoodsHash = "00", "goods table");
        Check(d => d.RowQty[0] += 5, "mass ledger");                          // goods from nowhere: the mass ledger no longer matches
        Check(d => d.HouseholdCount++, "population");
    }
}
