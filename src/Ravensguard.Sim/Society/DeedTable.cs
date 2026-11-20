using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Society;

/// <summary>
/// The deed rows of the content (<c>society/deeds.jsonc</c>, reputation 3): which chronicle entries count as a deed of their first person subject, and what they move. A row is found by the entry's
/// type and the first <see cref="DeedDef.Match"/> that holds (file order, as rumour rows are). Built through the mod loader from the content database; empty without content.
/// </summary>
public sealed class DeedTable
{
    public static readonly DeedTable Empty = new([]);

    private readonly Dictionary<string, DeedDef[]> _byEntry;
    private readonly Dictionary<string, DeedDef> _byId;
    public int Count => _byId.Count;

    public DeedTable(IEnumerable<DeedDef> rows)
    {
        var list = rows.ToList();
        _byEntry = list.GroupBy(r => r.Entry, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        _byId = list.ToDictionary(r => r.Id, StringComparer.Ordinal);
    }

    public static DeedTable From(ContentDatabase? db) => db == null ? Empty : new DeedTable(db.All<DeedDef>().OrderBy(r => r.Id, StringComparer.Ordinal).ToList());

    public DeedDef? Get(string id) => _byId.GetValueOrDefault(id);

    /// <summary>The row that reads this entry, or null (the entry is not a deed).</summary>
    public DeedDef? RowFor(ChronicleEntry e)
    {
        if (!_byEntry.TryGetValue(e.TypeId, out var rows)) return null;
        foreach (var row in rows)
        {
            bool ok = true;
            foreach (var (k, v) in row.Match) if (!e.Data.TryGetValue(k, out var dv) || !string.Equals(dv, v, StringComparison.OrdinalIgnoreCase)) { ok = false; break; }
            if (ok) return row;
        }
        return null;
    }

    /// <summary>The doer of an entry: its first person subject (null when it has none; the hero's id is person 0, which is why this is not an <see cref="EntityId.None"/> test).</summary>
    public static EntityId? DoerOf(ChronicleEntry e)
    {
        foreach (var s in e.Subjects) if (s.Kind == IdKind.Person) return s;
        return null;
    }

    /// <summary>How much of a row's deltas an entry carries: 1 for a flat row, or the entry's amount (capped) for a row scaled by a data key. The deltas are then multiplied by this over <see cref="DeedDef.PerUnit"/>.</summary>
    public static int AmountOf(DeedDef row, ChronicleEntry e)
    {
        if (row.PerKey.Length == 0) return Math.Max(1, row.PerUnit);
        int amt = e.Data.TryGetValue(row.PerKey, out var v) && int.TryParse(v, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n) ? Math.Max(0, n) : 0;
        if (row.PerMax > 0) amt = Math.Min(amt, row.PerMax * Math.Max(1, row.PerUnit));
        return amt;
    }

    /// <summary>The permille a row moves a group of this kind (before knowledge and age).</summary>
    public static int DeltaFor(DeedDef row, GroupKind kind) => kind switch
    {
        GroupKind.Settlement => row.SettlementPm, GroupKind.Fief => row.FiefPm, GroupKind.Realm => row.RealmPm, GroupKind.Religion => row.ReligionPm, _ => 0,
    };
}
