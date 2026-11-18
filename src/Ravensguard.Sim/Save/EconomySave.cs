using MessagePack;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The "economy" section: the coin ledger (purses and totals by reason; the recent-movements log is not saved), every store's rows, the goods mass ledger,
/// the per-household and per-person needs columns and the item instances. All lists are in index or id order, so bytes are deterministic.
/// </summary>
[MessagePackObject]
public sealed class EconomySave
{
    [Key(0)] public int Version { get; set; } = 1;
    [Key(1)] public string GoodsHash { get; set; } = "";
    [Key(2)] public int GoodCount { get; set; }

    // ledger
    [Key(3)] public ulong[] PurseOwner { get; set; } = [];
    [Key(4)] public byte[] PurseKind { get; set; } = [];
    [Key(5)] public long[] PurseBalance { get; set; } = [];
    [Key(6)] public long[] Sourced { get; set; } = [];
    [Key(7)] public long[] Sunk { get; set; } = [];
    [Key(8)] public long[] FlowCoin { get; set; } = [];
    [Key(9)] public long[] FlowCount { get; set; } = [];
    [Key(10)] public long Sequence { get; set; }

    // stores: households first (index order), then the others by owner id; rows are concatenated
    [Key(11)] public ulong[] StoreOwner { get; set; } = [];
    [Key(12)] public byte[] StoreKind { get; set; } = [];
    [Key(13)] public long[] StoreCapMassG { get; set; } = [];
    [Key(14)] public long[] StoreCapVolumeMl { get; set; } = [];
    [Key(15)] public bool[] StoreEnforce { get; set; } = [];
    [Key(16)] public int[] StoreRowCount { get; set; } = [];
    [Key(17)] public ushort[] RowGood { get; set; } = [];
    [Key(18)] public long[] RowQty { get; set; } = [];
    [Key(19)] public long[] MassCreated { get; set; } = [];
    [Key(20)] public long[] MassDestroyed { get; set; } = [];

    // households and people
    [Key(21)] public int HouseholdCount { get; set; }
    [Key(22)] public short[] NutritionCd { get; set; } = [];
    [Key(23)] public int[] SeedReserveG { get; set; } = [];
    [Key(24)] public short[] FoodCoverDaysX10 { get; set; } = [];
    [Key(25)] public short[] FuelCoverDaysX10 { get; set; } = [];
    [Key(26)] public ushort[] Grievance { get; set; } = [];
    [Key(27)] public byte[] Tier { get; set; } = [];
    [Key(28)] public bool[] DietPoor { get; set; } = [];

    // item instances (compacted)
    [Key(29)] public ulong[] ItemId { get; set; } = [];
    [Key(30)] public string[] ItemShape { get; set; } = [];
    [Key(31)] public ushort[] ItemGood { get; set; } = [];
    [Key(32)] public byte[] ItemQuality { get; set; } = [];
    [Key(33)] public short[] ItemCondition { get; set; } = [];
    [Key(34)] public ushort[] ItemWear { get; set; } = [];
    [Key(35)] public byte[] ItemFlags { get; set; } = [];
    [Key(36)] public byte[] ItemStains { get; set; } = [];
    [Key(37)] public ushort[] ItemLabourHours { get; set; } = [];
    [Key(38)] public byte[] ItemSkill { get; set; } = [];
    [Key(39)] public ulong[] ItemOwner { get; set; } = [];
    [Key(40)] public int[] ItemParent { get; set; } = [];
    [Key(41)] public int[] ItemComponentCount { get; set; } = [];
    [Key(42)] public ushort[] ItemContentsGood { get; set; } = [];
    [Key(43)] public long[] ItemContentsQty { get; set; } = [];
    [Key(44)] public string[] CompMaterial { get; set; } = [];
    [Key(45)] public int[] CompMassG { get; set; } = [];
    [Key(46)] public short[] CompCondition { get; set; } = [];
    [Key(47)] public byte[] DueDurables { get; set; } = [];

    private static T[] Cut<T>(T[] a, int n) => a.AsSpan(0, n).ToArray();

    /// <summary>Copies the economy into a DTO on the calling thread (so encoding can run elsewhere).</summary>
    public static EconomySave Capture(EconomyState e)
    {
        var l = e.Ledger.Export();
        var s = new EconomySave
        {
            GoodsHash = e.Goods.Hash(), GoodCount = e.Goods.Count,
            PurseOwner = l.Owner, PurseKind = l.Kind, PurseBalance = l.Balance, Sourced = l.Sourced, Sunk = l.Sunk, FlowCoin = l.FlowCoin, FlowCount = l.FlowCount, Sequence = l.Seq,
            MassCreated = e.Mass.CreatedState(), MassDestroyed = e.Mass.DestroyedState(),
            HouseholdCount = e.HouseholdCount, NutritionCd = Cut(e.NutritionCd, e.PersonCount), SeedReserveG = Cut(e.SeedReserveG, e.HouseholdCount),
            FoodCoverDaysX10 = Cut(e.FoodCoverDaysX10, e.HouseholdCount), FuelCoverDaysX10 = Cut(e.FuelCoverDaysX10, e.HouseholdCount),
            Grievance = e.Grievances.Take(e.HouseholdCount).Select(g => (ushort)g).ToArray(), Tier = e.Tiers.Take(e.HouseholdCount).Select(t => (byte)t).ToArray(), DietPoor = Cut(e.DietPoor, e.HouseholdCount),
            DueDurables = Cut(e.DueDurables, e.HouseholdCount * EconomyState.Durables),
        };
        var stores = e.HouseholdStore.Take(e.HouseholdCount).Concat(e.OtherStores.Values).ToList();
        s.StoreOwner = stores.Select(x => x.Owner.Value).ToArray();
        s.StoreKind = stores.Select(x => (byte)x.Kind).ToArray();
        s.StoreCapMassG = stores.Select(x => x.CapacityMassG).ToArray();
        s.StoreCapVolumeMl = stores.Select(x => x.CapacityVolumeMl).ToArray();
        s.StoreEnforce = stores.Select(x => x.EnforceCapacity).ToArray();
        s.StoreRowCount = stores.Select(x => x.RowCount).ToArray();
        var goods = new List<ushort>(); var qty = new List<long>();
        foreach (var st in stores) foreach (var (g, q) in st.Rows()) { goods.Add(g); qty.Add(q); }
        s.RowGood = goods.ToArray(); s.RowQty = qty.ToArray();

        var snap = e.Items.Compact();
        var rows = snap.Rows;
        s.ItemId = rows.Select(r => r.Id.Value).ToArray(); s.ItemShape = rows.Select(r => r.Shape).ToArray(); s.ItemGood = rows.Select(r => r.Good).ToArray();
        s.ItemQuality = rows.Select(r => (byte)r.Quality).ToArray(); s.ItemCondition = rows.Select(r => r.ConditionPermille).ToArray(); s.ItemWear = rows.Select(r => r.Wear).ToArray();
        s.ItemFlags = rows.Select(r => (byte)r.Flags).ToArray(); s.ItemStains = rows.Select(r => r.Stains).ToArray(); s.ItemLabourHours = rows.Select(r => r.LabourHours).ToArray();
        s.ItemSkill = rows.Select(r => r.Skill).ToArray(); s.ItemOwner = rows.Select(r => r.Owner.Value).ToArray(); s.ItemParent = rows.Select(r => r.Parent).ToArray();
        s.ItemComponentCount = rows.Select(r => r.ComponentCount).ToArray(); s.ItemContentsGood = rows.Select(r => r.ContentsGood).ToArray(); s.ItemContentsQty = rows.Select(r => r.ContentsQty).ToArray();
        s.CompMaterial = snap.Components.Select(c => c.Material).ToArray(); s.CompMassG = snap.Components.Select(c => c.MassG).ToArray(); s.CompCondition = snap.Components.Select(c => c.ConditionPermille).ToArray();
        return s;
    }

    /// <summary>Rebuilds the economy for the given population and content; throws <see cref="SaveException"/> when the save does not fit them.</summary>
    public EconomyState Restore(Population pop, ContentDatabase content)
    {
        try
        {
            var e0 = EconomyState.FromContent(content, pop.Households.Count, pop.People.Count);
            if (GoodCount != e0.Goods.Count || GoodsHash != e0.Goods.Hash()) throw new SaveException("The economy section was saved against a different goods table.");
            if (HouseholdCount != pop.Households.Count || NutritionCd.Length != pop.People.Count) throw new SaveException("The economy section does not match the population.");
            var mass = new MassLedger(GoodCount); mass.Restore(MassCreated, MassDestroyed);
            var ledger = Ledger.Import(new Ledger.State(PurseOwner, PurseKind, PurseBalance, Sourced, Sunk, FlowCoin, FlowCount, Sequence));
            var e = new EconomyState(e0.Goods, e0.Money, e0.Baskets, HouseholdCount, NutritionCd.Length, ledger, mass);
            for (int h = 0; h < HouseholdCount; h++)
            {
                if (!ledger.TryFind(Economy.PurseKind.Household, pop.Households.IdOf(h), out var pid)) throw new SaveException("A household has no purse in the economy section.");
                e.HouseholdPurse[h] = pid;
            }
            int row = 0;
            for (int i = 0; i < StoreOwner.Length; i++)
            {
                int n = StoreRowCount[i];
                var st = new Store(e.Goods, mass, (Goods.StoreKind)StoreKind[i], new EntityId(StoreOwner[i]), StoreCapMassG[i], StoreCapVolumeMl[i], StoreEnforce[i]);
                st.LoadRows(RowGood.AsSpan(row, n), RowQty.AsSpan(row, n)); row += n;
                if (i < HouseholdCount) e.HouseholdStore[i] = st; else e.OtherStores[st.Owner] = st;
            }
            Array.Copy(NutritionCd, e.NutritionCd, NutritionCd.Length); Array.Copy(SeedReserveG, e.SeedReserveG, HouseholdCount);
            Array.Copy(FoodCoverDaysX10, e.FoodCoverDaysX10, HouseholdCount); Array.Copy(FuelCoverDaysX10, e.FuelCoverDaysX10, HouseholdCount);
            if (DueDurables.Length == HouseholdCount * EconomyState.Durables) Array.Copy(DueDurables, e.DueDurables, DueDurables.Length);
            for (int h = 0; h < HouseholdCount; h++) { e.Grievances[h] = (Economy.Grievance)Grievance[h]; e.Tiers[h] = (HouseholdTier)Tier[h]; e.DietPoor[h] = DietPoor[h]; }

            var rows = new ItemRow[ItemId.Length]; int comp = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = new ItemRow
                {
                    Id = new EntityId(ItemId[i]), Shape = ItemShape[i], Good = ItemGood[i], Quality = (Goods.ItemQuality)ItemQuality[i], ConditionPermille = ItemCondition[i], Wear = ItemWear[i],
                    Flags = (Goods.ItemFlags)ItemFlags[i], Stains = ItemStains[i], LabourHours = ItemLabourHours[i], Skill = ItemSkill[i], Owner = new EntityId(ItemOwner[i]), Parent = ItemParent[i],
                    ComponentStart = comp, ComponentCount = ItemComponentCount[i], ContentsGood = ItemContentsGood[i], ContentsQty = ItemContentsQty[i],
                };
                comp += ItemComponentCount[i];
            }
            var comps = new ItemComponent[CompMaterial.Length];
            for (int i = 0; i < comps.Length; i++) comps[i] = new ItemComponent(CompMaterial[i], CompMassG[i], CompCondition[i]);
            e.Items = ItemTable.FromSnapshot(new ItemTable.Snapshot(rows, comps));
            if (!e.Ledger.Audit().IsConserved) throw new SaveException("The saved coin ledger does not balance.");
            if (!e.MassAuditHolds()) throw new SaveException("The saved goods do not match the mass ledger.");
            return e;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            throw new SaveException("Corrupt 'economy' section in save.", ex);
        }
    }
}
