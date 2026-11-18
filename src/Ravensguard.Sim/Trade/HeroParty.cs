using MessagePack;
using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Inventory;

namespace Ravensguard.Sim.Trade;

/// <summary>What the hero knows of a person (spec 4.6): familiarity from talking and working together, trust in per mille, and whether their reason to join is known.</summary>
public sealed class Acquaintance
{
    public int Familiarity;
    public int TrustPm = 100;
    public bool ReasonKnown;
    public long LastTalkHour = long.MinValue;
}

/// <summary>
/// The hero's side of the world records (trade-inventory-character spec 3.2 to 3.4): what they carry (<see cref="Carried"/>: worn slots and where each stack sits, over item rows and the
/// pack store in the economy), the price notebook, the party, the people they know, the slow attribute deltas and the craft work clock. Saved in the "party" section; everything it points
/// at (item rows, the pack, the purse, craft jobs) is in the economy and crafting sections, so goods and coin are counted once.
/// </summary>
public sealed class HeroParty
{
    public EntityId Hero { get; init; } = EconomyState.HeroOwner;
    public Carried Carried { get; init; } = new(EconomyState.HeroOwner);
    public PriceNotebook Notebook { get; } = new();
    public Squad Squad { get; } = new();
    public SortedDictionary<EntityId, Acquaintance> Known { get; } = [];
    /// <summary>Milli-points over the hero record's byte attributes (spec 3.1 <c>AttrMilli</c>), indexed by <see cref="HeroAttr"/>.</summary>
    public int[] AttrMilli { get; } = new int[5];
    /// <summary>Items whose condition the hero has looked at (selling an item not inspected fetches 70%).</summary>
    public SortedSet<EntityId> Inspected { get; } = [];
    /// <summary>The game second each of the hero's attended craft jobs was last worked to.</summary>
    public SortedDictionary<EntityId, long> WorkedTo { get; } = [];
    /// <summary>Goods the hero has held or traded (rows of the notebook tab).</summary>
    public SortedSet<ushort> Traded { get; } = [];
    public int LastWalkVisitDay { get; set; } = int.MinValue;
    /// <summary>Highest command sequence applied (commands are applied once, in order).</summary>
    public long LastSeq { get; set; }

    public Acquaintance Know(EntityId p) => Known.TryGetValue(p, out var a) ? a : Known[p] = new Acquaintance();

    /// <summary>
    /// A new hero's things in the world (migration "hero_inventory_to_items" for an older save): every <see cref="HeroItem"/> becomes <c>Count</c> item rows at Common quality and full
    /// condition, worn where they go; a belt pouch and a satchel are added; nothing else changes.
    /// </summary>
    public static HeroParty Create(EconomyState e, ContentDatabase db, IdAllocator ids, HeroRecord hero)
    {
        var party = new HeroParty();
        var rules = new ContainerRules(db, e.Goods);
        var f = new ItemFactory(db, e.Goods);
        var c = party.Carried;
        c.Pack(e);
        foreach (var it in hero.Inventory)
            for (int k = 0; k < Math.Max(1, it.Count); k++)
            {
                var id = c.MakeItem(e, db, rules, f, ids, it.Shape, it.Material);
                if (k == 0) c.EquipItem(e, rules, id);
            }
        foreach (var shape in new[] { "belt_pouch", "satchel" })
        {
            var id = c.MakeItem(e, db, rules, f, ids, shape);
            c.EquipItem(e, rules, id);
        }
        return party;
    }
}

// ---------------------------------------------------------------- save form

[MessagePackObject]
public sealed class HeroPartySave
{
    [Key(0)] public int Version { get; set; } = 1;
    [Key(1)] public ulong[] EquipItem { get; set; } = [];
    [Key(2)] public byte[] EquipSlot { get; set; } = [];
    [Key(3)] public ushort[] StackGood { get; set; } = [];
    [Key(4)] public ulong[] StackIn { get; set; } = [];
    [Key(5)] public ulong[] QuickBar { get; set; } = [];
    [Key(6)] public int[] NoteBook { get; set; } = [];
    [Key(7)] public ushort[] NoteGood { get; set; } = [];
    [Key(8)] public long[] NoteAsk { get; set; } = [];
    [Key(9)] public long[] NoteBid { get; set; } = [];
    [Key(10)] public long[] NoteStock { get; set; } = [];
    [Key(11)] public int[] NoteDay { get; set; } = [];
    [Key(12)] public FollowerRow[] Members { get; set; } = [];
    [Key(13)] public int LastPaidDay { get; set; }
    [Key(14)] public ulong[] KnownId { get; set; } = [];
    [Key(15)] public int[] KnownFamiliarity { get; set; } = [];
    [Key(16)] public int[] KnownTrust { get; set; } = [];
    [Key(17)] public bool[] KnownReason { get; set; } = [];
    [Key(18)] public long[] KnownTalkHour { get; set; } = [];
    [Key(19)] public int[] AttrMilli { get; set; } = [];
    [Key(20)] public ulong[] Inspected { get; set; } = [];
    [Key(21)] public ulong[] WorkedJob { get; set; } = [];
    [Key(22)] public long[] WorkedSec { get; set; } = [];
    [Key(23)] public ushort[] Traded { get; set; } = [];
    [Key(24)] public int LastWalkVisitDay { get; set; }
    [Key(25)] public long LastSeq { get; set; }

    public static HeroPartySave Capture(HeroParty p)
    {
        var c = p.Carried; var notes = p.Notebook.Notes.ToArray();
        return new HeroPartySave
        {
            EquipItem = [.. c.Equip.Keys.Select(k => k.Value)], EquipSlot = [.. c.Equip.Values.Select(v => (byte)v)],
            StackGood = [.. c.GoodIn.Keys], StackIn = [.. c.GoodIn.Values.Select(v => v.Value)], QuickBar = [.. c.QuickBar.Select(q => q.Value)],
            NoteBook = [.. notes.Select(n => n.Book)], NoteGood = [.. notes.Select(n => n.Good)], NoteAsk = [.. notes.Select(n => n.AskMicro)], NoteBid = [.. notes.Select(n => n.BidMicro)],
            NoteStock = [.. notes.Select(n => n.StockUnits)], NoteDay = [.. notes.Select(n => n.HeardDay)],
            Members = [.. p.Squad.Members.Select(FollowerRow.Of)], LastPaidDay = p.Squad.LastPaidDay,
            KnownId = [.. p.Known.Keys.Select(k => k.Value)], KnownFamiliarity = [.. p.Known.Values.Select(a => a.Familiarity)], KnownTrust = [.. p.Known.Values.Select(a => a.TrustPm)],
            KnownReason = [.. p.Known.Values.Select(a => a.ReasonKnown)], KnownTalkHour = [.. p.Known.Values.Select(a => a.LastTalkHour)],
            AttrMilli = (int[])p.AttrMilli.Clone(), Inspected = [.. p.Inspected.Select(i => i.Value)],
            WorkedJob = [.. p.WorkedTo.Keys.Select(k => k.Value)], WorkedSec = [.. p.WorkedTo.Values], Traded = [.. p.Traded], LastWalkVisitDay = p.LastWalkVisitDay, LastSeq = p.LastSeq,
        };
    }

    public HeroParty Restore()
    {
        if (EquipItem.Length != EquipSlot.Length || StackGood.Length != StackIn.Length || QuickBar.Length > 8 || NoteBook.Length != NoteGood.Length || NoteBook.Length != NoteDay.Length
            || KnownId.Length != KnownTrust.Length || WorkedJob.Length != WorkedSec.Length || AttrMilli.Length > 5)
            throw new Save.SaveException("Corrupt 'party' section in save.");
        var p = new HeroParty();
        for (int i = 0; i < EquipItem.Length; i++) p.Carried.Equip[new EntityId(EquipItem[i])] = (EquipSlot)EquipSlot[i];
        for (int i = 0; i < StackGood.Length; i++) p.Carried.GoodIn[StackGood[i]] = new EntityId(StackIn[i]);
        for (int i = 0; i < QuickBar.Length; i++) p.Carried.QuickBar[i] = new EntityId(QuickBar[i]);
        p.Notebook.Load(Enumerable.Range(0, NoteBook.Length).Select(i => new PriceNote(NoteBook[i], NoteGood[i], NoteAsk[i], NoteBid[i], NoteStock[i], NoteDay[i])));
        foreach (var m in Members) p.Squad.Members.Add(m.Restore());
        p.Squad.LastPaidDay = LastPaidDay;
        for (int i = 0; i < KnownId.Length; i++) p.Known[new EntityId(KnownId[i])] = new Acquaintance { Familiarity = KnownFamiliarity[i], TrustPm = KnownTrust[i], ReasonKnown = KnownReason[i], LastTalkHour = KnownTalkHour[i] };
        Array.Copy(AttrMilli, p.AttrMilli, AttrMilli.Length);
        foreach (var i in Inspected) p.Inspected.Add(new EntityId(i));
        for (int i = 0; i < WorkedJob.Length; i++) p.WorkedTo[new EntityId(WorkedJob[i])] = WorkedSec[i];
        foreach (var g in Traded) p.Traded.Add(g);
        p.LastWalkVisitDay = LastWalkVisitDay; p.LastSeq = LastSeq;
        return p;
    }
}

[MessagePackObject]
public sealed class FollowerRow
{
    [Key(0)] public ulong Person { get; set; }
    [Key(1)] public int PersonIndex { get; set; }
    [Key(2)] public string Name { get; set; } = "";
    [Key(3)] public byte Origin { get; set; }
    [Key(4)] public byte State { get; set; }
    [Key(5)] public int Loyalty { get; set; }
    [Key(6)] public int JoinedDay { get; set; }
    [Key(7)] public int EndDay { get; set; }
    [Key(8)] public long Wage { get; set; }
    [Key(9)] public byte Terms { get; set; }
    [Key(10)] public int PaidThroughDay { get; set; }
    [Key(11)] public byte UnpaidDays { get; set; }
    [Key(12)] public byte Order { get; set; }
    [Key(13)] public long OrderX { get; set; }
    [Key(14)] public long OrderY { get; set; }
    [Key(15)] public ulong OrderJob { get; set; }
    [Key(16)] public int MaxSkill { get; set; }
    [Key(17)] public string Trade { get; set; } = "";
    [Key(18)] public ulong Household { get; set; }

    public static FollowerRow Of(Follower f) => new()
    {
        Person = f.Person.Value, PersonIndex = f.PersonIndex, Name = f.Name, Origin = (byte)f.Origin, State = (byte)f.State, Loyalty = f.Loyalty, JoinedDay = f.JoinedDay, EndDay = f.EndDay,
        Wage = f.WageMitesPerDay, Terms = (byte)f.Terms, PaidThroughDay = f.PaidThroughDay, UnpaidDays = f.UnpaidDays, Order = (byte)f.Order, OrderX = f.OrderX, OrderY = f.OrderY,
        OrderJob = f.OrderJob.Value, MaxSkill = f.MaxSkill, Trade = f.Trade, Household = f.Household.Value,
    };

    public Follower Restore() => new()
    {
        Person = new EntityId(Person), PersonIndex = PersonIndex, Name = Name, Origin = (FollowerOrigin)Origin, State = (HireState)State, Loyalty = Loyalty, JoinedDay = JoinedDay, EndDay = EndDay,
        WageMitesPerDay = Wage, Terms = (HireTerms)Terms, PaidThroughDay = PaidThroughDay, UnpaidDays = UnpaidDays, Order = (SquadOrder)Order, OrderX = OrderX, OrderY = OrderY,
        OrderJob = new EntityId(OrderJob), MaxSkill = MaxSkill, Trade = Trade, Household = new EntityId(Household),
    };
}
