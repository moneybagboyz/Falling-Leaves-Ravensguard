namespace Ravensguard.Sim.Core;

public enum CauseKind { Fire, Raid, Battle, Crime, Wound, Death, Law, Famine, MineFlood, War, Weather, MarketDrift, Unknown, Other,
    // Phase 2 (overview 2.4): append only, never renumber
    Tax, Trade, Disease, Migration,
    // Phase 6 (overview 8.1, chronicle-full 3): append only, never renumber
    Justice, Faith, Slavery, Family, Unrest, Diplomacy, Office, Illness }

/// <summary>
/// Why something happened; may reference another chronicle entry. <see cref="Note"/> is the legacy English note (v8 saves keep it and show it as is); new writers pass
/// <see cref="NoteKey"/> and <see cref="NoteParams"/> instead (chronicle-full 3, 4.1): a text key and typed parameters, no English in the sim.
/// </summary>
public sealed record Cause(CauseKind Kind, EntityId? EventId = null, string Note = "", string NoteKey = "", IReadOnlyList<ChronParam>? NoteParams = null);

/// <summary>
/// One chronicle entry. <see cref="TextKey"/> and <see cref="Params"/> (chronicle-full 3) are the key and the typed values (ids, not names) the view formats with <c>tr()</c>;
/// entries from v8 saves have an empty key and keep their English in <see cref="Data"/> and the causes' notes. <see cref="Data"/> stays for machine fields.
/// </summary>
public sealed record ChronicleEntry(
    EntityId Id,
    GameTime When,
    string TypeId,
    IReadOnlyList<EntityId> Subjects,
    IReadOnlyList<Cause> Causes,
    IReadOnlyDictionary<string, string> Data,
    string TextKey = "",
    IReadOnlyList<ChronParam>? Params = null);

/// <summary>Append-only store of chronicle entries, with the effect index (entry id -> the entries whose causes name it) kept on append and so rebuilt at load, never saved.</summary>
public sealed class Chronicle
{
    private readonly Dictionary<EntityId, ChronicleEntry> _byId = new();
    private readonly List<ChronicleEntry> _all = new();
    private readonly EffectIndex _effects = new();

    public int Count => _all.Count;
    public IReadOnlyList<ChronicleEntry> All => _all;
    /// <summary>What followed each entry (chronicle-full 3): O(causes) on append.</summary>
    public EffectIndex Effects => _effects;

    public void Add(ChronicleEntry entry)
    {
        if (!_byId.TryAdd(entry.Id, entry)) throw new ArgumentException($"Duplicate chronicle id {entry.Id}.");
        _all.Add(entry);
        _effects.Add(entry);
    }

    public ChronicleEntry? Get(EntityId id) => _byId.GetValueOrDefault(id);

    public IEnumerable<ChronicleEntry> ByType(string typeId) => _all.Where(e => e.TypeId == typeId);

    /// <summary>Fraction of entries with at least one cause other than MarketDrift/Unknown (1 when empty). Kept for the Phase 2-5 tests; the Phase 6 gate is <see cref="SpecificCoverage"/>.</summary>
    public double CauseCoverage()
    {
        if (_all.Count == 0) return 1.0;
        int good = _all.Count(e => e.Causes.Any(c => c.Kind != CauseKind.MarketDrift && c.Kind != CauseKind.Unknown));
        return (double)good / _all.Count;
    }

    /// <summary>
    /// Specific causes, measured (chronicle-full 4.2): entries with at least one specific cause over the entries that are not market-drift labels (1 when none). A cause is
    /// specific when its kind is not Unknown, Other or MarketDrift and it names an earlier entry, or (for a type whose row accepts a note) carries a note key from the row's allowed list.
    /// </summary>
    public CoverageReport SpecificCoverage(ChronicleTypes? types = null, Func<ChronicleEntry, bool>? filter = null)
    {
        int total = 0, specific = 0, drift = 0, noCause = 0;
        foreach (var e in _all)
        {
            if (filter != null && !filter(e)) continue;
            total++;
            bool isDrift = e.Causes.Any(c => c.Kind == CauseKind.MarketDrift);
            if (e.Causes.Count == 0) noCause++;
            if (isDrift) { drift++; continue; }
            var row = types?.Get(e.TypeId);
            if (e.Causes.Any(c => IsSpecific(c, e, row))) specific++;
        }
        return new CoverageReport(total, specific, drift, noCause);
    }

    /// <summary>
    /// <c>ChronicleHash</c> (chronicle-full 5, overview 8.9): FNV-1a over every entry's id, time, type, text key, parameters, subjects and causes (kind, event, note key and parameters;
    /// a legacy English note only where the cause has no key). Not the formatted text of a keyed entry, so renaming a person or editing en.csv never moves it.
    /// </summary>
    public ulong Hash()
    {
        ulong h = 1469598103934665603UL;
        void U(ulong v) { for (int i = 0; i < 8; i++) { h = (h ^ (v & 0xFF)) * 1099511628211UL; v >>= 8; } }
        void S(string s) { foreach (char c in s) { h = (h ^ (c & 0xFFu)) * 1099511628211UL; h = (h ^ (ulong)(c >> 8)) * 1099511628211UL; } U(0xFF); }
        void P(IReadOnlyList<ChronParam>? ps) { U((ulong)(ps?.Count ?? 0)); if (ps != null) foreach (var p in ps) { S(p.Name); U((ulong)p.Kind); U((ulong)p.Value); S(p.Key); } }
        foreach (var e in _all)
        {
            U(e.Id.Value); U((ulong)e.When.Seconds); S(e.TypeId); S(e.TextKey); P(e.Params);
            U((ulong)e.Subjects.Count); foreach (var s in e.Subjects) U(s.Value);
            U((ulong)e.Causes.Count);
            foreach (var c in e.Causes) { U((ulong)c.Kind); U(c.EventId?.Value ?? 0); S(c.NoteKey); P(c.NoteParams); if (c.NoteKey.Length == 0) S(c.Note); }
        }
        return h;
    }

    /// <summary>One cause is specific under <paramref name="row"/>'s rule (<c>event</c>, <c>note</c> or <c>either</c>; no row means <c>event</c>).</summary>
    public bool IsSpecific(Cause c, ChronicleEntry owner, ChronicleTypeRow? row)
    {
        if (c.Kind is CauseKind.Unknown or CauseKind.Other or CauseKind.MarketDrift) return false;
        string need = row?.SpecificRequires ?? "event";
        bool ev = c.EventId is { } id && Get(id) is { } cause && cause.When.Seconds <= owner.When.Seconds && !cause.Id.Equals(owner.Id);
        bool note = c.NoteKey.Length > 0 && row != null && row.NoteKeys.Contains(c.NoteKey);
        return need switch { "note" => note, "either" => ev || note, _ => ev };
    }
}

/// <summary>The measure of <see cref="Chronicle.SpecificCoverage"/>: entries counted, those with a specific cause, the market-drift labels (excluded from the denominator) and entries with no cause at all.</summary>
public readonly record struct CoverageReport(int Entries, int Specific, int Drift, int NoCause)
{
    public int Denominator => Entries - Drift;
    public double Ratio => Denominator <= 0 ? 1.0 : (double)Specific / Denominator;
}
