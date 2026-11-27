using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Soldiers;

/// <summary>One slot of one soldier as the Armoury screen shows it: what is in it (the shape, 0 item when empty), whether it is a gap (required, nothing in it) and whether a fill from this armoury could close it.</summary>
public sealed record ArmourySlotCell(string Slot, bool Required, bool Own, ulong Item, string Shape, bool Gap, bool Fillable);

/// <summary>A soldier's row: who, his role, the template in force, and his cells in the template's slot order.</summary>
public sealed record ArmourySoldierRow(ulong Person, string Name, string Role, string Template, IReadOnlyList<ArmourySlotCell> Cells);

/// <summary>
/// P8-WS-1: the Armoury screen's view (war screens 3.1): the hero's soldiers down the left, the slots across, the template's gaps marked (the screen paints them red), and how many pieces the armoury holds. F (fill all), G (gaps
/// only), T (the template) and Enter (refit one man by hand) are desk commands (<see cref="SoldierDesk"/>); this view and the screen move no item and keep no state.
/// </summary>
public sealed record ArmouryView(long Armoury, int Stock, IReadOnlyList<string> Columns, IReadOnlyList<ArmourySoldierRow> Rows)
{
    public int GapCount => Rows.Sum(r => r.Cells.Count(c => c.Gap));

    public static ArmouryView Build(SoldierWorld sw, long armoury, IReadOnlyList<FillSoldier> soldiers, ItemTable items, Func<ulong, string>? nameOf = null)
    {
        var plan = sw.Plan(armoury, soldiers);
        var byPerson = plan.Soldiers.ToDictionary(p => p.Person);
        var rows = new List<ArmourySoldierRow>(); var cols = new List<string>();
        foreach (var s in soldiers.OrderBy(s => s.Person))
        {
            var tpl = sw.Roles.TemplateFor(s.Role, s.TemplateId);
            var row = sw.Book.Of(s.Person); var sp = byPerson.GetValueOrDefault(s.Person);
            var cells = new List<ArmourySlotCell>();
            if (tpl != null)
                foreach (var slot in tpl.Slots)
                {
                    if (!cols.Contains(slot.Slot)) cols.Add(slot.Slot);
                    ulong item = row != null && row.Worn.TryGetValue(slot.Slot, out var w) ? w : 0;
                    string shape = item != 0 && items.IndexOf(new Ravensguard.Sim.Core.EntityId(item)) is var ix and >= 0 ? items.At(ix).Shape : "";
                    bool fillable = item == 0 && sp != null && sp.Entries.Any(e => e.Slot == slot.Slot);
                    cells.Add(new ArmourySlotCell(slot.Slot, slot.Required, slot.Own, item, shape, item == 0 && slot.Required && !slot.Own, fillable));
                }
            rows.Add(new ArmourySoldierRow(s.Person, nameOf?.Invoke(s.Person) ?? "", s.Role, tpl?.Id ?? s.TemplateId, cells));
        }
        return new ArmouryView(armoury, sw.Armouries.Stock(armoury).Count, cols, rows);
    }
}
