using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Production;

namespace Ravensguard.Sim.Soldiers;

/// <summary>The sizes the filling rule needs (4.2): the size an armour piece was made for, and a soldier's body. The world supplies them from the crafting state and the person table; tests give their own.</summary>
public interface IKitFit
{
    /// <summary>The size in cm3 a piece was made for (70,000 for a piece the books do not know).</summary>
    int FitOf(ulong item);
    /// <summary>A soldier's body size in cm3.</summary>
    int BodyOf(ulong person);
    /// <summary>Writes the size a refit gives a piece; false when nobody keeps it.</summary>
    bool SetFit(ulong item, int cm3);
}

/// <summary>A soldier as the filling rule sees him: who, in which role, an optional template of his own, and his skill in the role's weapon family (the order within a role).</summary>
public sealed record FillSoldier(ulong Person, string Role, string TemplateId, int Skill);

/// <summary>Where a slot's item comes from: what the soldier already owns (worn kit, issued kit, his own things) or the armoury.</summary>
public enum KitSource : byte { Own, Armoury }

/// <summary>One slot filled: the item, its source, the score it won with, and whether it is an armoury piece that must be refitted before it is issued.</summary>
public sealed record FillEntry(string Slot, ulong Item, KitSource Source, int Score, bool NeedsRefit);

/// <summary>A slot nothing fills: required or optional, and whether it is one the armoury is never asked for (the soldier's own clothes).</summary>
public sealed record FillGap(string Slot, bool Required, bool Own);

/// <summary>One soldier's plan: the entries in template order and the gaps.</summary>
public sealed record SoldierPlan(ulong Person, string Role, string Template, IReadOnlyList<FillEntry> Entries, IReadOnlyList<FillGap> Gaps);

/// <summary>
/// The result of <see cref="ArmouryFill.Plan"/>: every soldier's plan in fill order, and the gaps summed per role and slot ("12 spearmen without helmets"). <see cref="Required"/> counts the required slots the armoury
/// could not fill, <see cref="Optional"/> the optional ones (greyed in the screen). Own-clothing slots are in neither.
/// </summary>
public sealed record FillPlan(IReadOnlyList<SoldierPlan> Soldiers, IReadOnlyDictionary<string, int> Required, IReadOnlyDictionary<string, int> Optional)
{
    public int Issuable => Soldiers.Sum(s => s.Entries.Count(e => e.Source == KitSource.Armoury && !e.NeedsRefit));
    public int Refits => Soldiers.Sum(s => s.Entries.Count(e => e.NeedsRefit));
}

/// <summary>What <see cref="ArmouryFill.Apply"/> did: items issued, refit jobs queued, and the plan's gap totals.</summary>
public sealed record FillResult(int Issued, int RefitsQueued, int Kept, IReadOnlyDictionary<string, int> Required, IReadOnlyDictionary<string, int> Optional);

/// <summary>
/// P7-SC-A (soldiers-and-companies 4.2): filling loadout templates from an armoury. <see cref="Plan"/> is pure (no draw, nothing moves): soldiers in role priority, then skill descending, then person id; slots in template
/// order; per slot the item with the best score, own kit first when it scores at least as well. <see cref="Apply"/> then moves the chosen items (an issue from the armoury's store into the soldier's hands, recorded in the
/// issue book) and queues the refits. It creates and destroys nothing: an item is in the armoury's store or on one soldier, never both.
/// </summary>
public sealed class ArmouryFill
{
    private readonly TroopRoles _roles;
    private readonly Armouries _arm;
    private readonly ItemTable _items;
    private readonly ContentDatabase _db;

    public ArmouryFill(TroopRoles roles, Armouries armouries, ItemTable items)
    {
        _roles = roles; _arm = armouries; _items = items; _db = roles.Db;
    }

    private SoldierRulesDef Rules => _roles.Rules;

    // ------------------------------------------------------------------ scoring

    /// <summary>Whether a piece is armour whose size matters (the armour layer: mail, plate, helms); clothing and padding have none.</summary>
    private bool SizeMatters(string shape) => _db.TryGet<ArmourShapeDef>(shape, out var a) && a.Layer == ArmourLayer.Armour;

    /// <summary>A piece is a misfit when the body is more than the tolerance away from the size it was made for (15%).</summary>
    public bool Misfit(string shape, int fit, int body) => SizeMatters(shape) && Math.Abs((long)body - fit) * 1000 > (long)fit * Rules.FitTolerancePm;

    /// <summary>Whether a refit can fix a misfit: an armourer is free and the rule allows it (plate can be taken in, never let out).</summary>
    public bool Refittable(string shape, int fit, int body, int armourersFree)
    {
        if (armourersFree <= 0 || !_db.TryGet<ArmourShapeDef>(shape, out var a)) return false;
        return !(Rules.RefitShrinkOnly.Contains(a.RigLayer) && fit < body);
    }

    internal int Score(int row, bool misfitPenalty, bool wornBonus)
    {
        ref var r = ref _items.At(row);
        int s = (int)r.Quality * Rules.QualityScore + r.ConditionPermille / Math.Max(1, Rules.ConditionDiv) + _roles.MaterialRankOf(_items, row) * Rules.MaterialScore;
        if (misfitPenalty) s -= Rules.MisfitPenalty;
        if (wornBonus) s += Rules.WornBonus;
        return s;
    }

    internal bool Qualifies(LoadoutSlotDef slot, int row)
    {
        ref var r = ref _items.At(row);
        if (!slot.Accept.Contains(r.Shape)) return false;
        if (slot.MinQuality.Length > 0 && Enum.TryParse<ItemQuality>(slot.MinQuality, true, out var q) && r.Quality < q) return false;
        if (slot.MinMaterial.Count > 0)
        {
            if (r.ComponentCount == 0 || !slot.MinMaterial.Contains(_items.Component(r.ComponentStart).Material)) return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ the plan

    /// <summary>
    /// Plans the fill of every soldier from the armoury <paramref name="armoury"/> (the key of its room's origin cell). <paramref name="armourersFree"/> says how many armourers could take a refit job (0: a misfit is issued
    /// misfit and scored down). <paramref name="book"/> (optional) says what each soldier already wears, for the +20 and for keeping what is his.
    /// </summary>
    public FillPlan Plan(long armoury, IReadOnlyList<FillSoldier> soldiers, int armourersFree, IKitFit fit, SoldierBook? book = null, bool gapsOnly = false)
    {
        var stock = new List<(ulong Id, int Row)>();
        foreach (var id in _arm.Stock(armoury)) { int row = _items.IndexOf(id); if (row >= 0) stock.Add((id.Value, row)); }
        var persons = new HashSet<ulong>(soldiers.Select(s => s.Person));
        var owned = new Dictionary<ulong, List<(ulong Id, int Row)>>();
        foreach (int row in _items.LiveRows())
        {
            ref var r = ref _items.At(row);
            if (persons.Contains(r.Owner.Value) && r.Parent < 0) { if (!owned.TryGetValue(r.Owner.Value, out var l)) owned[r.Owner.Value] = l = []; l.Add((r.Id.Value, row)); }
        }
        var claimed = new HashSet<ulong>();
        var ordered = soldiers.OrderBy(s => _roles.Role(s.Role)?.Priority ?? int.MaxValue).ThenByDescending(s => s.Skill).ThenBy(s => s.Person).ToList();
        var plans = new List<SoldierPlan>(); var req = new SortedDictionary<string, int>(StringComparer.Ordinal); var opt = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in ordered)
        {
            var tpl = _roles.TemplateFor(s.Role, s.TemplateId);
            if (tpl == null) { plans.Add(new SoldierPlan(s.Person, s.Role, s.TemplateId, [], [])); continue; }
            var row = book?.Of(s.Person);
            int body = fit.BodyOf(s.Person);
            var entries = new List<FillEntry>(); var gaps = new List<FillGap>();
            var mine = owned.GetValueOrDefault(s.Person) ?? [];
            foreach (var slot in tpl.Slots)
            {
                if (gapsOnly && row != null && row.Worn.TryGetValue(slot.Slot, out var kept)) { claimed.Add(kept); continue; }          // P8-WS-1 (G): what he wears stays, and no other man is given it
                int bestOwnScore = int.MinValue; ulong bestOwn = 0;
                foreach (var (id, r) in mine)
                {
                    if (claimed.Contains(id) || !Qualifies(slot, r)) continue;
                    bool worn = row != null && row.Worn.TryGetValue(slot.Slot, out var w) && w == id;
                    int sc = Score(r, Misfit(_items.At(r).Shape, fit.FitOf(id), body), worn);
                    if (sc > bestOwnScore || (sc == bestOwnScore && id < bestOwn)) { bestOwnScore = sc; bestOwn = id; }
                }
                int bestArmScore = int.MinValue; ulong bestArm = 0; bool armRefit = false;
                if (!slot.Own)
                {
                    foreach (var (id, r) in stock)
                    {
                        if (claimed.Contains(id) || !Qualifies(slot, r)) continue;
                        string shape = _items.At(r).Shape; int f = fit.FitOf(id);
                        bool mis = Misfit(shape, f, body), fixable = mis && Refittable(shape, f, body, armourersFree);
                        int sc = Score(r, mis && !fixable, false);
                        if (sc > bestArmScore || (sc == bestArmScore && id < bestArm)) { bestArmScore = sc; bestArm = id; armRefit = fixable; }
                    }
                }
                if (bestOwnScore != int.MinValue && bestOwnScore >= bestArmScore) { claimed.Add(bestOwn); entries.Add(new FillEntry(slot.Slot, bestOwn, KitSource.Own, bestOwnScore, false)); }
                else if (bestArmScore != int.MinValue) { claimed.Add(bestArm); entries.Add(new FillEntry(slot.Slot, bestArm, KitSource.Armoury, bestArmScore, armRefit)); }
                else
                {
                    gaps.Add(new FillGap(slot.Slot, slot.Required, slot.Own));
                    if (!slot.Own) { var d = slot.Required ? req : opt; string key = s.Role + "/" + slot.Slot; d[key] = d.GetValueOrDefault(key) + 1; }
                }
            }
            plans.Add(new SoldierPlan(s.Person, s.Role, tpl.Id, entries, gaps));
        }
        return new FillPlan(plans, req, opt);
    }

    // ------------------------------------------------------------------ issuing

    /// <summary>
    /// Carries a plan out: each armoury piece is issued to its soldier (the item's owner becomes the soldier, the issue book and the soldier's row say so), a piece that needs a refit is queued as a refit job and stays in the
    /// armoury, and a slot whose old item differs sends an issued old item back. Own kit is only recorded as worn. A soldier with no row gets one (lord <paramref name="lord"/>). Moves items only: no item is made or lost.
    /// </summary>
    public FillResult Apply(long armoury, FillPlan plan, SoldierBook book, IKitFit fit, int day, long lord = -1)
    {
        int issued = 0, queued = 0, kept = 0;
        foreach (var sp in plan.Soldiers)
        {
            if (!book.Rows.TryGetValue(sp.Person, out var row)) book.Rows[sp.Person] = row = new SoldierRow { Person = sp.Person, Role = sp.Role, Lord = lord };
            var holder = new EntityId(sp.Person);
            foreach (var e in sp.Entries)
            {
                if (row.Worn.TryGetValue(e.Slot, out var old) && old == e.Item) { kept++; continue; }
                if (e.Source == KitSource.Armoury)
                {
                    if (e.NeedsRefit)
                    {
                        if (!book.Refits.Any(j => j.Item == e.Item))
                        { book.Refits.Add(new RefitJob(e.Item, sp.Person, armoury, e.Slot, fit.BodyOf(sp.Person), day)); queued++; }
                        continue;
                    }
                    if (!_arm.IssueItem(armoury, new EntityId(e.Item), holder, day)) continue;
                    issued++;
                    row.Issued.Add(new IssuedItem(e.Item, armoury));
                }
                else kept++;
                Unwear(row, e.Slot);
                row.Worn[e.Slot] = e.Item;
            }
        }
        return new FillResult(issued, queued, kept, plan.Required, plan.Optional);
    }

    /// <summary>Takes what is worn in a slot off: an issued item goes back to its armoury; his own stays his, carried.</summary>
    private void Unwear(SoldierRow row, string slot)
    {
        if (!row.Worn.Remove(slot, out var old)) return;
        int at = row.Issued.FindIndex(i => i.Item == old);
        if (at < 0) return;
        var iss = row.Issued[at];
        _arm.Return(iss.Armoury, [new EntityId(old)]);                        // a piece lost or broken on campaign is just gone: the issue book is cleared either way
        row.Issued.RemoveAt(at);
    }

    /// <summary>
    /// A finished refit job (the armourer's time is the crafting spec's; this only records its outcome): the piece takes the wearer's size and is issued to him. False when the job is not queued or the item is no longer
    /// in the armoury.
    /// </summary>
    public bool CompleteRefit(SoldierBook book, IKitFit fit, ulong item, int day)
    {
        int at = book.Refits.FindIndex(j => j.Item == item); if (at < 0) return false;
        var job = book.Refits[at];
        if (!_arm.Stock(job.Armoury).Contains(new EntityId(item))) { book.Refits.RemoveAt(at); return false; }
        fit.SetFit(item, job.ToCm3);
        book.Refits.RemoveAt(at);
        if (!book.Rows.TryGetValue(job.Person, out var row) || !_arm.IssueItem(job.Armoury, new EntityId(item), new EntityId(job.Person), day)) return false;
        row.Issued.Add(new IssuedItem(item, job.Armoury));
        Unwear(row, job.Slot);
        row.Worn[job.Slot] = item;
        return true;
    }

    // ------------------------------------------------------------------ discharge and single pieces

    /// <summary>
    /// Discharges a soldier: every issued item goes back into its armoury (with the wear it has; an item lost on campaign is simply gone from the book), his own kit stays his, his row and any refit job of his are removed.
    /// Returns how many items went back.
    /// </summary>
    public int Discharge(SoldierBook book, ulong person)
    {
        if (!book.Rows.Remove(person, out var row)) return 0;
        int back = 0;
        foreach (var grp in row.Issued.GroupBy(i => i.Armoury).OrderBy(g => g.Key))
            back += _arm.Return(grp.Key, [.. grp.OrderBy(i => i.Item).Select(i => new EntityId(i.Item))]);
        book.Refits.RemoveAll(j => j.Person == person);
        return back;
    }

    /// <summary>
    /// Re-equips one piece (Dwarf Fortress style): puts <paramref name="item"/> in a slot, from his own things or from the armoury <paramref name="armoury"/> (-1: his own). The slot's old item is taken off (an issued one goes
    /// back). False when the slot is not in his template, the item does not qualify, or it is not his or the armoury's.
    /// </summary>
    public bool Equip(SoldierBook book, ulong person, string slot, ulong item, long armoury, int day)
    {
        if (!book.Rows.TryGetValue(person, out var row)) return false;
        var tpl = _roles.TemplateFor(row.Role, row.Template); var def = tpl?.Slots.FirstOrDefault(s => s.Slot == slot);
        int r = _items.IndexOf(new EntityId(item)); if (def == null || r < 0 || !Qualifies(def, r)) return false;
        if (_items.At(r).Owner.Value == person) { Unwear(row, slot); row.Worn[slot] = item; return true; }
        if (armoury < 0 || def.Own || !_arm.Stock(armoury).Contains(new EntityId(item))) return false;
        Unwear(row, slot);
        if (!_arm.IssueItem(armoury, new EntityId(item), new EntityId(person), day)) return false;
        row.Issued.Add(new IssuedItem(item, armoury)); row.Worn[slot] = item;
        return true;
    }

    /// <summary>Takes one slot's item off him: an issued piece goes back to its armoury, his own stays his and carried.</summary>
    public bool Strip(SoldierBook book, ulong person, string slot)
    {
        if (!book.Rows.TryGetValue(person, out var row) || !row.Worn.ContainsKey(slot)) return false;
        Unwear(row, slot);
        return true;
    }
}
