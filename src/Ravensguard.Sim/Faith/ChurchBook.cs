using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Faith;

/// <summary>A parish (faith-and-church 3): a settlement with a priest, its church and its chest. The holder is a real person; a vacancy is recorded with the day. The service counters are the last holy day's.</summary>
public sealed class ParishRow
{
    public int Settlement;
    /// <summary>The priest's person id, -1 when the living is vacant.</summary>
    public long Priest = -1;
    /// <summary>The day the living fell vacant, -1 while held.</summary>
    public int VacantSince = -1;
    /// <summary>The share of the season's tithe earmarked for the fabric and for the poor, counted in mites (the coin stays in the chest; the chest's repairs and alms draw on it).</summary>
    public long FabricMites, PoorMites;
    /// <summary>The last holy day's service: the day, the adults who came and the adults of the parish.</summary>
    public int ServiceDay = -1, Attended, Adults;
}

/// <summary>
/// A see (faith-and-church 3): one per religion and realm in the region, the bishop at the largest parish of that faith in the realm. A vacancy has the day it began and the day the chapter
/// elects (1 to 2 seasons later); while it lasts the ruler takes the see's receipts (4.2).
/// </summary>
public sealed class SeeRow
{
    public int Index;
    /// <summary>The religion's template id (stable across a content change that reorders templates).</summary>
    public string Religion = "";
    /// <summary>The history realm index the see lies in, -1 for the free towns.</summary>
    public int Realm = -1;
    /// <summary>The region settlement of the cathedral (the largest parish).</summary>
    public int Seat = -1;
    public long Bishop = -1;
    public int VacantSince = -1, ElectionDay = -1;
    /// <summary>Coin a ruler has taken from this see in vacancies (a running total, for the chronicle and the view).</summary>
    public long TakenMites;
}

/// <summary>The hero's pilgrimage on the road (4.4): where to, which faith, since when. At most one.</summary>
public sealed class PilgrimRow
{
    public string Religion = "";
    public int Target = -1, StartDay;
    /// <summary>The chronicle entry of the vow (the cause of the arrival's entry), 0 when none.</summary>
    public ulong Entry;
}

/// <summary>
/// The church of the home region (faith-and-church 3, the optional "faith" section, no format bump): the parishes and sees with their holders, the piety people hold where it differs from the derived default,
/// and the hero's side (pilgrimage, the last service he attended, the shrine's comfort). Religions are not here: they are derived (<see cref="ReligionBook"/>). No random draw lives here; the systems draw
/// with the registered streams and write the results in.
/// </summary>
public sealed class ChurchBook
{
    public readonly SortedDictionary<int, ParishRow> Parishes = [];
    public readonly SortedDictionary<int, SeeRow> Sees = [];
    /// <summary>Piety by person id, only where it differs from the default (4.3).</summary>
    public readonly SortedDictionary<ulong, byte> Piety = [];
    public PilgrimRow? Pilgrim;
    /// <summary>The day of the last holy day the hero attended a service on (-1 never), the pilgrim's comfort at a shrine runs to the day after <see cref="ComfortUntilDay"/>, and the badges he holds.</summary>
    public int LastServiceDay = -1, ComfortUntilDay = -1, Pilgrimages;
    /// <summary>The person the shrine's comfort is for (the hero who last arrived at one), -1 none.</summary>
    public long HeroId = -1;
    /// <summary>The day the daily pass last ran (-1 never).</summary>
    public int LastDay = -1;

    // ---- not saved: what the systems read, set when the book is attached
    public ReligionBook Religions { get; set; } = ReligionBook.Empty;
    public FaithSettingsDef Settings { get; set; } = new() { Id = "faith" };
    public ulong Seed { get; set; }

    public bool IsEmpty => Parishes.Count == 0 && Sees.Count == 0 && Piety.Count == 0 && Pilgrim == null && LastServiceDay < 0 && ComfortUntilDay < 0 && Pilgrimages == 0 && HeroId < 0;

    public ParishRow? ParishAt(int settlement) => Parishes.GetValueOrDefault(settlement);

    /// <summary>The see a settlement lies in (its religion and realm), or null.</summary>
    public SeeRow? SeeOf(string religion, int realm) { foreach (var s in Sees.Values) if (s.Religion == religion && s.Realm == realm) return s; return null; }

    /// <summary>The <c>FaithHash</c> of the daily trail (faith-and-church 5): offices, piety deltas, the hero's state, in ascending order.</summary>
    public ulong Hash()
    {
        ulong h = 0xCBF29CE484222325UL;
        void M(ulong v) => h = DetRng.Mix(h ^ (v + 0x9E3779B97F4A7C15UL));
        void S(string s) { foreach (char c in s) M(c); M(0xFF); }
        foreach (var (k, p) in Parishes) { M((ulong)(uint)k); M((ulong)p.Priest); M((ulong)(uint)p.VacantSince); M((ulong)p.FabricMites); M((ulong)p.PoorMites); M((ulong)(uint)p.ServiceDay); M((ulong)(uint)p.Attended); M((ulong)(uint)p.Adults); }
        foreach (var (k, s) in Sees) { M((ulong)(uint)k); S(s.Religion); M((ulong)(uint)s.Realm); M((ulong)(uint)s.Seat); M((ulong)s.Bishop); M((ulong)(uint)s.VacantSince); M((ulong)(uint)s.ElectionDay); M((ulong)s.TakenMites); }
        foreach (var (k, v) in Piety) { M(k); M(v); }
        if (Pilgrim is { } pg) { S(pg.Religion); M((ulong)(uint)pg.Target); M((ulong)(uint)pg.StartDay); M(pg.Entry); }
        M((ulong)(uint)LastServiceDay); M((ulong)(uint)ComfortUntilDay); M((ulong)(uint)Pilgrimages); M((ulong)(uint)LastDay); M((ulong)HeroId);
        return h;
    }
}
