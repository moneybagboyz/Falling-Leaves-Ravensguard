using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Soldiers;

/// <summary>What a levy brought: the pieces moved from the household to him, by slot, and the slots the household could not fill.</summary>
public sealed record LevyBrought(string Template, IReadOnlyList<(string Slot, ulong Item)> Items, IReadOnlyList<string> Missing);

/// <summary>
/// P7-SC-A (soldiers-and-companies 4.3): levies bring their household's gear by the household's wealth. Poor: a tool-weapon (an axe, a billhook, a flail, a club or a dagger) and his tunic; middling: a spear, a round shield,
/// a cap or kettle hat and a gambeson besides; well-off: a mail shirt or a helmet and a sword or axe besides. They are real items of the household, moved to the man (the owner changes, nothing is made); a household with
/// no item of a kind sends none of it. The kinds are the templates <c>levy_poor</c>, <c>levy_middling</c> and <c>levy_well_off</c>.
/// </summary>
public sealed class LevyGear
{
    private readonly TroopRoles _roles;
    private readonly ArmouryFill _fill;
    private readonly ItemTable _items;

    public LevyGear(TroopRoles roles, ArmouryFill fill, ItemTable items) { _roles = roles; _fill = fill; _items = items; }

    /// <summary>
    /// The levy <paramref name="person"/> goes with the pieces of <paramref name="householdItems"/> his wealth class lists: for each slot of the template, the best item (the filling score) not yet taken; its owner becomes
    /// the person. Makes or replaces his soldier row (role levy, template the class's, lord <paramref name="lord"/>).
    /// </summary>
    public LevyBrought Bring(SoldierBook book, ulong person, LevyWealth wealth, IEnumerable<EntityId> householdItems, long lord, int day)
    {
        var tpl = _roles.Template(TroopRoles.LevyTemplateOf(wealth)) ?? throw new InvalidOperationException("no levy template");
        var pool = householdItems.Select(i => (Id: i, Row: _items.IndexOf(i))).Where(x => x.Row >= 0 && _items.At(x.Row).Parent < 0).OrderBy(x => x.Id.Value).ToList();
        var taken = new HashSet<ulong>(); var given = new List<(string, ulong)>(); var missing = new List<string>();
        if (!book.Rows.TryGetValue(person, out var row)) book.Rows[person] = row = new SoldierRow { Person = person };
        row.Role = "levy"; row.Template = tpl.Id; row.Lord = lord; row.PromotedDay = day;
        foreach (var slot in tpl.Slots)
        {
            int bestScore = int.MinValue; EntityId best = default; int bestRow = -1;
            foreach (var (id, r) in pool)
            {
                if (taken.Contains(id.Value) || !_fill.Qualifies(slot, r)) continue;
                int sc = _fill.Score(r, false, false);
                if (sc > bestScore) { bestScore = sc; best = id; bestRow = r; }                  // ascending id: the first of equals wins
            }
            if (bestRow < 0) { missing.Add(slot.Slot); continue; }
            taken.Add(best.Value);
            _items.At(bestRow).Owner = new EntityId(person);
            row.Worn[slot.Slot] = best.Value;
            given.Add((slot.Slot, best.Value));
        }
        return new LevyBrought(tpl.Id, given, missing);
    }
}
