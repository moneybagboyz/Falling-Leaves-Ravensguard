using MessagePack;
using Ravensguard.Sim.Soldiers;

namespace Ravensguard.Sim.Save;

[MessagePackObject]
public sealed class WornSave
{
    [Key(0)] public string Slot { get; set; } = "";
    [Key(1)] public ulong Item { get; set; }
}

[MessagePackObject]
public sealed class IssuedSave
{
    [Key(0)] public ulong Item { get; set; }
    [Key(1)] public long Armoury { get; set; }
}

[MessagePackObject]
public sealed class SoldierRowSave
{
    [Key(0)] public ulong Person { get; set; }
    [Key(1)] public string Role { get; set; } = "";
    [Key(2)] public long Lord { get; set; } = -1;
    [Key(3)] public string Template { get; set; } = "";
    [Key(4)] public int PromotedDay { get; set; }
    [Key(5)] public WornSave[] Worn { get; set; } = [];
    [Key(6)] public IssuedSave[] Issued { get; set; } = [];
}

[MessagePackObject]
public sealed class RefitJobSave
{
    [Key(0)] public ulong Item { get; set; }
    [Key(1)] public ulong Person { get; set; }
    [Key(2)] public long Armoury { get; set; }
    [Key(3)] public string Slot { get; set; } = "";
    [Key(4)] public int ToCm3 { get; set; }
    [Key(5)] public int Day { get; set; }
}

/// <summary>The optional "soldiers" section (a name of <c>Phase7Seeding.SectionNames</c>), written only while the book is not empty, so a world with no soldier keeps its bytes. Later soldier parts append keys.</summary>
[MessagePackObject]
public sealed class SoldierSave
{
    public const string SectionName = "soldiers";

    [Key(0)] public SoldierRowSave[] Rows { get; set; } = [];
    [Key(1)] public RefitJobSave[] Refits { get; set; } = [];

    public static SoldierSave Capture(SoldierBook b) => new()
    {
        Rows = [.. b.Rows.Values.Select(r => new SoldierRowSave
        {
            Person = r.Person, Role = r.Role, Lord = r.Lord, Template = r.Template, PromotedDay = r.PromotedDay,
            Worn = [.. r.Worn.Select(w => new WornSave { Slot = w.Key, Item = w.Value })],
            Issued = [.. r.Issued.OrderBy(i => i.Item).Select(i => new IssuedSave { Item = i.Item, Armoury = i.Armoury })],
        })],
        Refits = [.. b.Refits.OrderBy(j => j.Item).Select(j => new RefitJobSave { Item = j.Item, Person = j.Person, Armoury = j.Armoury, Slot = j.Slot, ToCm3 = j.ToCm3, Day = j.Day })],
    };

    public SoldierBook Restore()
    {
        var b = new SoldierBook();
        foreach (var r in Rows)
        {
            var row = new SoldierRow { Person = r.Person, Role = r.Role, Lord = r.Lord, Template = r.Template, PromotedDay = r.PromotedDay };
            foreach (var w in r.Worn) row.Worn[w.Slot] = w.Item;
            foreach (var i in r.Issued) row.Issued.Add(new IssuedItem(i.Item, i.Armoury));
            b.Rows[row.Person] = row;
        }
        foreach (var j in Refits) b.Refits.Add(new RefitJob(j.Item, j.Person, j.Armoury, j.Slot, j.ToCm3, j.Day));
        return b;
    }
}
