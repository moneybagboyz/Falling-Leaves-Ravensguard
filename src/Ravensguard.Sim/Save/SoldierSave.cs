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
    // P7-SC-B: appended keys
    [Key(7)] public long ArrearsMites { get; set; }
    [Key(8)] public DrillSave[] Drill { get; set; } = [];
}

[MessagePackObject]
public sealed class DrillSave
{
    [Key(0)] public string Family { get; set; } = "";
    [Key(1)] public int Hours { get; set; }
}

[MessagePackObject]
public sealed class SkillMicroSave
{
    [Key(0)] public ulong Person { get; set; }
    [Key(1)] public byte Skill { get; set; }
    [Key(2)] public long Micro { get; set; }
    [Key(3)] public long UseHour { get; set; } = -1;
    [Key(4)] public int Uses { get; set; }
}

[MessagePackObject]
public sealed class UnitDrillSave
{
    [Key(0)] public long Unit { get; set; }
    [Key(1)] public int Value { get; set; }
    [Key(2)] public int LastDay { get; set; }
}

[MessagePackObject]
public sealed class OfferSave
{
    [Key(0)] public ulong Person { get; set; }
    [Key(1)] public string Role { get; set; } = "";
    [Key(2)] public int Day { get; set; }
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

[MessagePackObject]
public sealed class CompanySave
{
    [Key(0)] public long Id { get; set; }
    [Key(1)] public string Name { get; set; } = "";
    [Key(2)] public ulong Captain { get; set; }
    [Key(3)] public ulong[] Members { get; set; } = [];
    [Key(4)] public int Purse { get; set; } = -1;
    [Key(5)] public int Reputation { get; set; }
    [Key(6)] public int Settlement { get; set; } = -1;
    [Key(7)] public int ContractId { get; set; } = -1;
    [Key(8)] public long Employer { get; set; } = -1;
    [Key(9)] public int EmployerPurse { get; set; } = -1;
    [Key(10)] public int HiredDay { get; set; } = -1;
    [Key(11)] public int EndDay { get; set; } = -1;
    [Key(12)] public int ArrearsDays { get; set; }
    [Key(13)] public long OwedMites { get; set; }
    [Key(14)] public bool Turned { get; set; }
    [Key(15)] public int WageMultPm { get; set; } = 1000;
    [Key(16)] public int Morale { get; set; }
    [Key(17)] public bool FillGaps { get; set; }
    [Key(18)] public int Kept { get; set; }
}

[MessagePackObject]
public sealed class CompanyStandingSave
{
    [Key(0)] public long Lord { get; set; }
    [Key(1)] public int Value { get; set; }
}

/// <summary>The optional "soldiers" section (a name of <c>Phase7Seeding.SectionNames</c>), written only while the book is not empty, so a world with no soldier keeps its bytes. Later soldier parts append keys.</summary>
[MessagePackObject]
public sealed class SoldierSave
{
    public const string SectionName = "soldiers";

    [Key(0)] public SoldierRowSave[] Rows { get; set; } = [];
    [Key(1)] public RefitJobSave[] Refits { get; set; } = [];
    // P7-SC-B: appended keys (absent in an older save: empty)
    [Key(2)] public SkillMicroSave[] Skills { get; set; } = [];
    [Key(3)] public UnitDrillSave[] Units { get; set; } = [];
    [Key(4)] public OfferSave[] Offers { get; set; } = [];
    // P7-SC-C: appended keys (absent in an older save: empty)
    [Key(5)] public CompanySave[] Companies { get; set; } = [];
    [Key(6)] public CompanyStandingSave[] Standing { get; set; } = [];

    public static SoldierSave Capture(SoldierBook b) => new()
    {
        Rows = [.. b.Rows.Values.Select(r => new SoldierRowSave
        {
            Person = r.Person, Role = r.Role, Lord = r.Lord, Template = r.Template, PromotedDay = r.PromotedDay, ArrearsMites = r.ArrearsMites,
            Drill = [.. r.DrillHours.Select(d => new DrillSave { Family = d.Key, Hours = d.Value })],
            Worn = [.. r.Worn.Select(w => new WornSave { Slot = w.Key, Item = w.Value })],
            Issued = [.. r.Issued.OrderBy(i => i.Item).Select(i => new IssuedSave { Item = i.Item, Armoury = i.Armoury })],
        })],
        Refits = [.. b.Refits.OrderBy(j => j.Item).Select(j => new RefitJobSave { Item = j.Item, Person = j.Person, Armoury = j.Armoury, Slot = j.Slot, ToCm3 = j.ToCm3, Day = j.Day })],
        Skills = SkillRows(b),
        Units = [.. b.Units.Select(u => new UnitDrillSave { Unit = u.Key, Value = u.Value.Value, LastDay = u.Value.LastDay })],
        Offers = [.. b.Offers.Values.Select(o => new OfferSave { Person = o.Person, Role = o.Role, Day = o.Day })],
        Companies = [.. b.Companies.Values.Select(c => new CompanySave
        {
            Id = c.Id, Name = c.Name, Captain = c.Captain, Members = [.. c.Members], Purse = c.Purse, Reputation = c.Reputation, Settlement = c.Settlement, ContractId = c.ContractId, Employer = c.Employer,
            EmployerPurse = c.EmployerPurse, HiredDay = c.HiredDay, EndDay = c.EndDay, ArrearsDays = c.ArrearsDays, OwedMites = c.OwedMites, Turned = c.Turned, WageMultPm = c.WageMultPm,
            Morale = c.Morale, FillGaps = c.FillGaps, Kept = c.Kept,
        })],
        Standing = [.. b.Standing.Select(s => new CompanyStandingSave { Lord = s.Key, Value = s.Value })],
    };

    private static SkillMicroSave[] SkillRows(SoldierBook b)
    {
        var uses = b.Skills.UseRows().ToDictionary(u => (u.Person, u.Skill));
        return [.. b.Skills.MicroRows().Select(m => uses.TryGetValue((m.Person, m.Skill), out var u)
            ? new SkillMicroSave { Person = m.Person, Skill = m.Skill, Micro = m.Micro, UseHour = u.Hour, Uses = u.Uses }
            : new SkillMicroSave { Person = m.Person, Skill = m.Skill, Micro = m.Micro })];
    }

    public SoldierBook Restore()
    {
        var b = new SoldierBook();
        foreach (var r in Rows)
        {
            var row = new SoldierRow { Person = r.Person, Role = r.Role, Lord = r.Lord, Template = r.Template, PromotedDay = r.PromotedDay, ArrearsMites = r.ArrearsMites };
            foreach (var d in r.Drill) row.DrillHours[d.Family] = d.Hours;
            foreach (var w in r.Worn) row.Worn[w.Slot] = w.Item;
            foreach (var i in r.Issued) row.Issued.Add(new IssuedItem(i.Item, i.Armoury));
            b.Rows[row.Person] = row;
        }
        foreach (var j in Refits) b.Refits.Add(new RefitJob(j.Item, j.Person, j.Armoury, j.Slot, j.ToCm3, j.Day));
        foreach (var m in Skills) { b.Skills.RestoreMicro(m.Person, m.Skill, m.Micro); if (m.UseHour >= 0) b.Skills.RestoreUse(m.Person, m.Skill, m.UseHour, m.Uses); }
        foreach (var u in Units) b.Units[u.Unit] = new UnitDrill { Value = u.Value, LastDay = u.LastDay };
        foreach (var o in Offers) b.Offers[o.Person] = new PromotionOffer(o.Person, o.Role, o.Day);
        foreach (var c in Companies)
        {
            var m = new MercCompany
            {
                Id = c.Id, Name = c.Name, Captain = c.Captain, Purse = c.Purse, Reputation = c.Reputation, Settlement = c.Settlement, ContractId = c.ContractId, Employer = c.Employer,
                EmployerPurse = c.EmployerPurse, HiredDay = c.HiredDay, EndDay = c.EndDay, ArrearsDays = c.ArrearsDays, OwedMites = c.OwedMites, Turned = c.Turned, WageMultPm = c.WageMultPm,
                Morale = c.Morale, FillGaps = c.FillGaps, Kept = c.Kept,
            };
            m.Members.AddRange(c.Members); b.Companies[m.Id] = m;
        }
        foreach (var s in Standing) b.Standing[s.Lord] = s.Value;
        return b;
    }
}
