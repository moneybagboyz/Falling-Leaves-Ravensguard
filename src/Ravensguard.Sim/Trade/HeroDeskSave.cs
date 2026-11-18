using MessagePack;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Inventory;

namespace Ravensguard.Sim.Trade;

/// <summary>
/// The hero's commands still waiting for the barrier of the hour they were asked in (review finding 6), saved in the "hero.pending" section so a load applies them exactly where an
/// uninterrupted run would: after the same hour, in the same sequence order, with the same numbers. Absent when nothing waits, so older saves keep their bytes.
/// </summary>
[MessagePackObject]
public sealed class HeroPendingSave
{
    public const string SectionName = "hero.pending";
    [Key(0)] public int Version { get; set; } = 1;
    [Key(1)] public List<HeroCommandSave> Commands { get; set; } = [];

    public static HeroPendingSave Capture(IEnumerable<HeroCommand> cmds) => new() { Commands = [.. cmds.OrderBy(c => c.Seq).Select(HeroCommandSave.Of)] };
    public List<HeroCommand> Restore() => [.. Commands.Select(c => c.Restore()).OrderBy(c => c.Seq)];
}

/// <summary>One <see cref="HeroCommand"/> as saved: every field, enums as bytes, ids as their values.</summary>
[MessagePackObject]
public sealed class HeroCommandSave
{
    [Key(0)] public long Seq { get; set; }
    [Key(1)] public long IssuedSec { get; set; }
    [Key(2)] public byte Kind { get; set; }
    [Key(3)] public int Settlement { get; set; }
    [Key(4)] public ushort Good { get; set; }
    [Key(5)] public long Qty { get; set; }
    [Key(6)] public long Limit { get; set; }
    [Key(7)] public ulong Item { get; set; }
    [Key(8)] public ulong Container { get; set; }
    [Key(9)] public ulong Person { get; set; }
    [Key(10)] public byte Slot { get; set; }
    [Key(11)] public long Wage { get; set; }
    [Key(12)] public int Days { get; set; }
    [Key(13)] public byte Terms { get; set; }
    [Key(14)] public byte Danger { get; set; }
    [Key(15)] public byte Order { get; set; }
    [Key(16)] public long X { get; set; }
    [Key(17)] public long Y { get; set; }
    [Key(18)] public ulong Job { get; set; }
    [Key(19)] public ushort Recipe { get; set; }
    [Key(20)] public int Batch { get; set; }
    [Key(21)] public int[]? Choice { get; set; }
    [Key(22)] public bool HasSite { get; set; }
    [Key(23)] public int SiteLight { get; set; }
    [Key(24)] public bool SiteWater { get; set; }
    [Key(25)] public string[] SiteTools { get; set; } = [];
    [Key(26)] public string[] SiteStations { get; set; } = [];
    [Key(27)] public string[] SiteImprovised { get; set; } = [];
    [Key(28)] public long UntilSec { get; set; }
    [Key(29)] public byte Origin { get; set; }
    [Key(30)] public ushort[] GiveGood { get; set; } = [];
    [Key(31)] public long[] GiveQty { get; set; } = [];
    [Key(32)] public ushort[] TakeGood { get; set; } = [];
    [Key(33)] public long[] TakeQty { get; set; } = [];
    [Key(34)] public ulong[] GiveItems { get; set; } = [];
    [Key(35)] public long CoinGive { get; set; }
    [Key(36)] public long CoinTake { get; set; }
    [Key(37)] public int Region { get; set; }
    [Key(38)] public string Tag { get; set; } = "";

    public static HeroCommandSave Of(HeroCommand c) => new()
    {
        Seq = c.Seq, IssuedSec = c.IssuedSec, Kind = (byte)c.Kind, Settlement = c.Settlement, Good = c.Good, Qty = c.Qty, Limit = c.Limit, Item = c.Item.Value, Container = c.Container.Value,
        Person = c.Person.Value, Slot = (byte)c.Slot, Wage = c.Wage, Days = c.Days, Terms = (byte)c.Terms, Danger = (byte)c.Danger, Order = (byte)c.Order, X = c.X, Y = c.Y, Job = c.Job.Value,
        Recipe = c.Recipe, Batch = c.Batch, Choice = c.Choice == null ? null : (int[])c.Choice.Clone(), HasSite = c.Site != null, SiteLight = c.Site?.Light ?? 0, SiteWater = c.Site?.Water ?? false,
        SiteTools = c.Site?.Tools ?? [], SiteStations = c.Site?.Stations ?? [], SiteImprovised = c.Site?.Improvised ?? [], UntilSec = c.UntilSec, Origin = (byte)c.Origin,
        GiveGood = [.. c.Give.Select(g => g.Good)], GiveQty = [.. c.Give.Select(g => g.Qty)], TakeGood = [.. c.Take.Select(g => g.Good)], TakeQty = [.. c.Take.Select(g => g.Qty)],
        GiveItems = [.. c.GiveItems.Select(i => i.Value)], CoinGive = c.CoinGive, CoinTake = c.CoinTake, Region = c.Region, Tag = c.Tag,
    };

    public HeroCommand Restore() => new()
    {
        Seq = Seq, IssuedSec = IssuedSec, Kind = (HeroCommandKind)Kind, Settlement = Settlement, Good = Good, Qty = Qty, Limit = Limit, Item = new EntityId(Item), Container = new EntityId(Container),
        Person = new EntityId(Person), Slot = (EquipSlot)Slot, Wage = Wage, Days = Days, Terms = (HireTerms)Terms, Danger = (HireDanger)Danger, Order = (SquadOrder)Order, X = X, Y = Y, Job = new EntityId(Job),
        Recipe = Recipe, Batch = Batch, Choice = Choice, Site = HasSite ? new CraftSiteSpec(SiteLight, SiteWater, SiteTools, SiteStations, SiteImprovised) : null, UntilSec = UntilSec, Origin = (CommandOrigin)Origin,
        Give = [.. GiveGood.Zip(GiveQty, (g, q) => (g, q))], Take = [.. TakeGood.Zip(TakeQty, (g, q) => (g, q))], GiveItems = [.. GiveItems.Select(i => new EntityId(i))], CoinGive = CoinGive, CoinTake = CoinTake,
        Region = Region, Tag = Tag,
    };
}
