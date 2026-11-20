using Ravensguard.Content;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Save;

/// <summary>
/// One table's old index -> new index, by id: an id still in the content keeps its row; an id the content no longer has follows the content's migration entries
/// (<c>{"type": "migration", "target_type": "good", "from": old, "to": new}</c>, <see cref="ContentDatabase.Resolve"/>) or is dropped (-1).
/// </summary>
public sealed class TableRemap
{
    public required string Table { get; init; }
    public required string[] Old { get; init; }
    public required string[] New { get; init; }
    public required int[] Map { get; init; }
    public bool Identity { get; init; }
    /// <summary>Ids the content no longer has and no migration names (their rows are dropped).</summary>
    public List<string> Dropped { get; } = [];
    /// <summary>"old -> new" for ids that a migration moved.</summary>
    public List<string> Migrated { get; } = [];
    public int Added { get; init; }

    /// <summary>The new index of old index <paramref name="old"/>, -1 when dropped or out of range.</summary>
    public int this[int old] => (uint)old < (uint)Map.Length ? Map[old] : -1;
    /// <summary>Whether old index <paramref name="old"/> lands on a row of the same id (not a migration's target).</summary>
    public bool Same(int old) => this[old] is int n && n >= 0 && string.Equals(Old[old], New[n], StringComparison.Ordinal);

    public static TableRemap Build(string table, string[] old, string[] cur, ContentDatabase? db)
    {
        var at = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < cur.Length; i++) at[cur[i]] = i;
        var map = new int[old.Length]; var mig = new List<string>(); var dropped = new List<string>();
        for (int i = 0; i < old.Length; i++)
        {
            if (at.TryGetValue(old[i], out int n)) { map[i] = n; continue; }
            string to = old[i];
            try { to = db?.Resolve(table, old[i]) ?? old[i]; } catch (ContentException) { }
            if (!string.Equals(to, old[i], StringComparison.Ordinal) && at.TryGetValue(to, out n)) { map[i] = n; mig.Add($"{old[i]} -> {to}"); }
            else { map[i] = -1; dropped.Add(old[i]); }
        }
        var oldSet = new HashSet<string>(old, StringComparer.Ordinal);
        var r = new TableRemap { Table = table, Old = old, New = cur, Map = map, Identity = old.AsSpan().SequenceEqual(cur), Added = cur.Count(c => !oldSet.Contains(c)) };
        r.Migrated.AddRange(mig); r.Dropped.AddRange(dropped);
        return r;
    }
}

/// <summary>What a load did to fit a save to its content: the rewritten file, notes for the player's log, and the tables the loaded world now saves.</summary>
public sealed record RemapResult(SaveFile File, IReadOnlyList<string> Notes, IdTables Tables);

/// <summary>
/// The load-time remap of content ids (P5 goods-table fix): a save stores dense indices of content tables (goods everywhere a stock, lot, order, cargo or item names one; craft recipes,
/// proficiencies, business kinds, the business recipe book and extraction kinds). Those indices are the ordinal order of the content's ids, so any content update or mod that adds,
/// renames or removes an entry moves them. The "tables" section (<see cref="IdTablesSave"/>) names each table's ids in index order; on load every saved index is mapped onto the
/// loaded content by name before any section is read. A new id gets empty rows; a removed one follows the content's migration entries or, with none, its rows are dropped:
/// <list type="bullet">
/// <item>goods: stock is written off with one note; the good's column of the mass ledger (every faucet and sink it ever had) leaves together with its stock, so every remaining
/// good's audit (<c>created - destroyed == stock</c>) stays exact and coin is untouched (no coin is ever held against a good: lots, orders and wishes are dropped, not refunded,
/// because they hold none). A migrated good's stock and ledger columns are added to the target's, quantity as stored (a migration names a good counted the same way).</item>
/// <item>craft recipes and proficiencies: what people know and watch, teaching sessions, offences and crafted-item records of a removed recipe are dropped; an unfinished job,
/// commission or queued craft of one cannot be (its materials and escrow are in the economy), so the load is refused with a clear message.</item>
/// <item>business kinds and extraction kinds in use, removed: refused the same way; a station mid-batch of a removed book recipe is set idle (its inputs were consumed at the start).</item>
/// </list>
/// A remapped save carries the loaded content's tables, so save -> load -> save is byte-identical after the first load.
/// </summary>
public static partial class ContentRemap
{
    /// <summary>Fits <paramref name="file"/> to <paramref name="content"/>. Returns the file unchanged when every table matches.</summary>
    public static RemapResult Apply(SaveFile file, ContentDatabase content)
    {
        var ct = IdTables.Of(content); var cur = ct.Tables;
        var econ = file.Sections.TryGetValue(WorldSave.EconomySection, out var eb) ? Decode<EconomySave>(eb, WorldSave.EconomySection) : null;
        IdTables old;
        bool legacy = false;
        if (file.Sections.TryGetValue(IdTablesSave.SectionName, out var tb))
        {
            try { old = IdTables.FromSave(SaveCodec.Deserialize<IdTablesSave>(tb)); }
            catch (Exception e) when (e is not SaveException) { throw new SaveException("Corrupt 'tables' section in save.", e); }
        }
        else { old = LegacyIdTables.V8; legacy = true; }
        // a save from before the tables were stored: its goods are the frozen list, unless its economy says otherwise
        if (legacy && econ != null && econ.GoodCount != old.Get(IdTables.Goods)!.Length)
        {
            if (econ.GoodsHash == ct.Goods.Hash()) old.Tables[IdTables.Goods] = cur.Get(IdTables.Goods)!;
            else throw new SaveException("The economy section was saved against a different goods table, and this save is too old to name its goods (it was made before goods were saved by name, with content or mods that changed the goods).");
        }
        // what the loaded world will save: the content's tables for the names the save had (a table it did not know stays unknown, so a bare world's bytes keep their shape)
        var keep = cur.Only(old.Tables.Keys.Where(cur.Tables.ContainsKey));
        var remaps = new Dictionary<string, TableRemap>(StringComparer.Ordinal);
        foreach (var (name, ids) in old.Tables)
            if (cur.Get(name) is { } now) remaps[name] = TableRemap.Build(name, ids, now, content);
        var goods = remaps.GetValueOrDefault(IdTables.Goods);
        if (goods != null && econ != null && econ.GoodCount != goods.Old.Length) throw new SaveException("The economy section does not match the goods table this save names.");
        bool hashStale = econ != null && econ.GoodsHash != ct.Goods.Hash();
        if (remaps.Values.All(r => r.Identity) && !hashStale) return new RemapResult(file, [], keep);

        var rw = new Rewriter(remaps, ct);
        var sections = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (name, bytes) in file.Sections) sections[name] = rw.Section(name, bytes);
        sections[IdTablesSave.SectionName] = SaveCodec.Serialize(keep.ToSave());
        return new RemapResult(new SaveFile(file.Header, sections), rw.Notes(), keep);
    }

    internal static T Decode<T>(ReadOnlyMemory<byte> b, string name, bool compressed = true)
    {
        try { return compressed ? SaveCodec.DeserializeCompressed<T>(b) : SaveCodec.Deserialize<T>(b); }
        catch (Exception e) when (e is not SaveException) { throw new SaveException($"Corrupt '{name}' section in save.", e); }
    }
}
