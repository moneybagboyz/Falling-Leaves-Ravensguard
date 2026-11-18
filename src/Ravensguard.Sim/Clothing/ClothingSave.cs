using MessagePack;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Clothing;

/// <summary>
/// The clothing book on disk: columns in item order (the table compacted) and person order (ascending id), so equal state gives equal bytes. Items carry their side columns (wet, stains, dye, fit);
/// people carry their worn item ids, core, counters and flags. Derived readings are recomputed on the first step.
/// </summary>
[MessagePackObject]
public sealed class ClothingSave
{
    [Key(0)] public int Version { get; set; } = 1;
    [Key(1)] public ulong[] IdState { get; set; } = [];
    // items
    [Key(2)] public ulong[] ItemId { get; set; } = [];
    [Key(3)] public string[] ItemShape { get; set; } = [];
    [Key(4)] public byte[] ItemQuality { get; set; } = [];
    [Key(5)] public short[] ItemCondition { get; set; } = [];
    [Key(6)] public ushort[] ItemWear { get; set; } = [];
    [Key(7)] public byte[] ItemFlags { get; set; } = [];
    [Key(8)] public byte[] ItemStains { get; set; } = [];
    [Key(9)] public ushort[] ItemLabour { get; set; } = [];
    [Key(10)] public byte[] ItemSkill { get; set; } = [];
    [Key(11)] public ulong[] ItemOwner { get; set; } = [];
    [Key(12)] public int[] ItemComponentCount { get; set; } = [];
    [Key(13)] public string[] CompMaterial { get; set; } = [];
    [Key(14)] public int[] CompMassG { get; set; } = [];
    [Key(15)] public short[] CompCondition { get; set; } = [];
    [Key(16)] public int[] ItemWetU { get; set; } = [];
    [Key(17)] public byte[] ItemStainKind { get; set; } = [];
    [Key(18)] public uint[] ItemDye { get; set; } = [];
    [Key(19)] public int[] ItemFit { get; set; } = [];
    [Key(20)] public bool[] ItemHung { get; set; } = [];
    // people
    [Key(21)] public ulong[] PersonId { get; set; } = [];
    [Key(22)] public long[] PersonCoreU { get; set; } = [];
    [Key(23)] public int[] PersonExposureMin { get; set; } = [];
    [Key(24)] public int[] PersonSevereMin { get; set; } = [];
    [Key(25)] public int[] PersonHeatMin { get; set; } = [];
    [Key(26)] public byte[] PersonFlags { get; set; } = [];           // 1 collapsed, 2 fed
    [Key(27)] public byte[] PersonDied { get; set; } = [];
    [Key(28)] public long[] PersonLastMinute { get; set; } = [];
    [Key(29)] public long[] PersonWearDay { get; set; } = [];
    [Key(30)] public int[] PersonResist { get; set; } = [];
    [Key(31)] public int[] PersonSize { get; set; } = [];
    [Key(32)] public int[] PersonWornCount { get; set; } = [];
    [Key(33)] public ulong[] PersonWorn { get; set; } = [];
    [Key(34)] public int[] PersonLife { get; set; } = [];

    public static ClothingSave Capture(ClothingBook b)
    {
        var s = new ClothingSave { IdState = b.Ids.GetState() };
        var snap = b.Items.Compact(); var rows = snap.Rows;
        s.ItemId = rows.Select(r => r.Id.Value).ToArray(); s.ItemShape = rows.Select(r => r.Shape).ToArray(); s.ItemQuality = rows.Select(r => (byte)r.Quality).ToArray();
        s.ItemCondition = rows.Select(r => r.ConditionPermille).ToArray(); s.ItemWear = rows.Select(r => r.Wear).ToArray(); s.ItemFlags = rows.Select(r => (byte)r.Flags).ToArray();
        s.ItemStains = rows.Select(r => r.Stains).ToArray(); s.ItemLabour = rows.Select(r => r.LabourHours).ToArray(); s.ItemSkill = rows.Select(r => r.Skill).ToArray();
        s.ItemOwner = rows.Select(r => r.Owner.Value).ToArray(); s.ItemComponentCount = rows.Select(r => r.ComponentCount).ToArray();
        s.CompMaterial = snap.Components.Select(c => c.Material).ToArray(); s.CompMassG = snap.Components.Select(c => c.MassG).ToArray(); s.CompCondition = snap.Components.Select(c => c.ConditionPermille).ToArray();
        var ex = rows.Select(r => b.ExtraOf(r.Id)).ToArray();
        s.ItemWetU = ex.Select(e => e.WetU).ToArray(); s.ItemStainKind = ex.Select(e => e.StainKind).ToArray(); s.ItemDye = ex.Select(e => e.Dye).ToArray();
        s.ItemFit = ex.Select(e => e.FitCm3).ToArray(); s.ItemHung = ex.Select(e => e.Hung).ToArray();
        var ps = b.People.ToArray();
        s.PersonId = ps.Select(p => p.Person.Value).ToArray(); s.PersonCoreU = ps.Select(p => p.CoreU).ToArray(); s.PersonExposureMin = ps.Select(p => p.ExposureMin).ToArray();
        s.PersonSevereMin = ps.Select(p => p.SevereMin).ToArray(); s.PersonHeatMin = ps.Select(p => p.HeatMin).ToArray();
        s.PersonFlags = ps.Select(p => (byte)((p.Collapsed ? 1 : 0) | (p.Fed ? 2 : 0))).ToArray(); s.PersonDied = ps.Select(p => (byte)p.Died).ToArray();
        s.PersonLastMinute = ps.Select(p => p.LastMinute).ToArray(); s.PersonWearDay = ps.Select(p => p.WearDay).ToArray();
        s.PersonLife = ps.Select(p => p.LifeDays).ToArray(); s.PersonResist = ps.Select(p => p.ResistPm).ToArray(); s.PersonSize = ps.Select(p => p.SizeCm3).ToArray();
        s.PersonWornCount = ps.Select(p => p.Worn.Count).ToArray(); s.PersonWorn = ps.SelectMany(p => p.Worn.Select(w => w.Value)).ToArray();
        return s;
    }

    public void Restore(ClothingBook b)
    {
        var rows = new ItemRow[ItemId.Length]; int comp = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = new ItemRow
            {
                Id = new EntityId(ItemId[i]), Shape = ItemShape[i], Good = ItemTable.NoGood, Quality = (ItemQuality)ItemQuality[i], ConditionPermille = ItemCondition[i], Wear = ItemWear[i],
                Flags = (ItemFlags)ItemFlags[i], Stains = ItemStains[i], LabourHours = ItemLabour[i], Skill = ItemSkill[i], Owner = new EntityId(ItemOwner[i]), Parent = -1,
                ComponentStart = comp, ComponentCount = ItemComponentCount[i], ContentsGood = ItemTable.NoGood,
            };
            comp += ItemComponentCount[i];
        }
        var comps = new ItemComponent[CompMaterial.Length];
        for (int i = 0; i < comps.Length; i++) comps[i] = new ItemComponent(CompMaterial[i], CompMassG[i], CompCondition[i]);
        var extra = new SortedDictionary<EntityId, ItemExtra>();
        for (int i = 0; i < rows.Length; i++) extra[rows[i].Id] = new ItemExtra { WetU = ItemWetU[i], StainKind = ItemStainKind[i], Dye = ItemDye[i], FitCm3 = ItemFit[i], Hung = ItemHung[i] };
        var people = new SortedDictionary<EntityId, Tracked>(); int w = 0;
        for (int i = 0; i < PersonId.Length; i++)
        {
            var t = new Tracked
            {
                Person = new EntityId(PersonId[i]), CoreU = PersonCoreU[i], ExposureMin = PersonExposureMin[i], SevereMin = PersonSevereMin[i], HeatMin = PersonHeatMin[i],
                Collapsed = (PersonFlags[i] & 1) != 0, Fed = (PersonFlags[i] & 2) != 0, Died = (ExposureDeath)PersonDied[i], LastMinute = PersonLastMinute[i], WearDay = PersonWearDay[i],
                ResistPm = PersonResist[i], SizeCm3 = PersonSize[i], LifeDays = PersonLife[i],
            };
            for (int k = 0; k < PersonWornCount[i]; k++) t.Worn.Add(new EntityId(PersonWorn[w++]));
            people[t.Person] = t;
        }
        b.Replace(ItemTable.FromSnapshot(new ItemTable.Snapshot(rows, comps)), IdAllocator.FromState(IdState), people, extra);
    }
}
