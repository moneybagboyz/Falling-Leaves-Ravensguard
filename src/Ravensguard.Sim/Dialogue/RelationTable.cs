using Ravensguard.Content;

namespace Ravensguard.Sim.Dialogue;

[Flags] public enum RelationFlags : byte { None = 0, Kin = 1, Employer = 2, Debtor = 4, Rival = 8, Lover = 16 }

/// <summary>One pair's row (dialogue spec 3.2): trust in permille, familiarity 0..100, the day they last met.</summary>
public struct Relation
{
    public short TrustPm;
    public byte Familiarity;
    public int LastMetDay;
    public RelationFlags Flags;
    public int PerDayCount;
    public int PerDayFor;
}

/// <summary>
/// The sparse relations between people of the world (spec 3.2; the hero's own are the party's <c>Acquaintance</c> rows). A pair's row is made the first time they talk; kin have a baseline of
/// familiarity 70 and trust 400 without a row. Capped (the oldest low-familiarity rows go first); familiarity falls by 1 every 15 game days.
/// </summary>
public sealed class RelationTable(int cap = 20000)
{
    private readonly SortedDictionary<(int, int), Relation> _rows = [];
    public int Count => _rows.Count;
    public IReadOnlyDictionary<(int, int), Relation> Rows => _rows;

    private static (int, int) Key(int a, int b) => a <= b ? (a, b) : (b, a);

    /// <summary>Familiarity and trust of a pair; kin have the baseline when no row says more.</summary>
    public (int Familiarity, int TrustPm) Of(int a, int b, bool kin = false)
    {
        if (_rows.TryGetValue(Key(a, b), out var r)) return (r.Familiarity, r.TrustPm);
        return kin ? (70, 400) : (0, 0);
    }

    public RelationTier TierOf(int a, int b, bool kin = false) { var (f, t) = Of(a, b, kin); return Relations.Tier(f, t); }

    /// <summary>They talked on <paramref name="day"/>: familiarity +2, at most +6 a day.</summary>
    public void Met(int a, int b, int day, TalkSettingsDef s, int trustDelta = 0, bool kin = false)
    {
        var k = Key(a, b);
        if (!_rows.TryGetValue(k, out var r)) { r = new Relation { Familiarity = (byte)(kin ? 70 : 0), TrustPm = (short)(kin ? 400 : 0), Flags = kin ? RelationFlags.Kin : 0 }; }
        if (r.PerDayFor != day) { r.PerDayFor = day; r.PerDayCount = 0; }
        if (r.PerDayCount < s.FamiliarityPerDayMax) { int add = Math.Min(s.FamiliarityPerTalk, s.FamiliarityPerDayMax - r.PerDayCount); r.Familiarity = (byte)Math.Min(100, r.Familiarity + add); r.PerDayCount += add; }
        r.TrustPm = (short)Math.Clamp(r.TrustPm + trustDelta, -1000, 1000); r.LastMetDay = day;
        _rows[k] = r;
        if (_rows.Count > cap) Evict();
    }

    /// <summary>
    /// Over the cap, the worst twentieth of the cap goes at once (least familiar, then longest unmet, then lowest key): one sort every cap/20 new pairs instead of one per new pair. A full sort for each new pair
    /// cost 3 ms once the table was full, 40 game-year seconds from year 6 of a soak (P6-G2); the batch keeps the same order of who goes first.
    /// </summary>
    private void Evict()
    {
        int drop = Math.Max(1, cap / 20);
        foreach (var k in _rows.OrderBy(kv => kv.Value.Familiarity).ThenBy(kv => kv.Value.LastMetDay).ThenBy(kv => kv.Key).Take(drop).Select(kv => kv.Key).ToList()) _rows.Remove(k);
    }

    /// <summary>Familiarity falls by 1 for every <c>FamiliarityDecayDays</c> since the pair last met; rows that reach 0 with no trust are dropped.</summary>
    public void Decay(int day, TalkSettingsDef s)
    {
        List<(int, int)>? gone = null;
        foreach (var k in _rows.Keys.ToList())
        {
            var r = _rows[k]; int steps = (day - r.LastMetDay) / Math.Max(1, s.FamiliarityDecayDays);
            if (steps <= 0 || r.Familiarity == 0) continue;
            r.Familiarity = (byte)Math.Max(0, r.Familiarity - 1); r.LastMetDay += s.FamiliarityDecayDays; _rows[k] = r;
            if (r.Familiarity == 0 && r.TrustPm == 0) (gone ??= []).Add(k);
        }
        if (gone != null) foreach (var k in gone) _rows.Remove(k);
    }

    public void Set((int, int) key, Relation r) => _rows[key] = r;
    public void Clear() => _rows.Clear();
}
