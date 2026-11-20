using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.History;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Faith;

/// <summary>A saint of a religion: a history saint of its cultures or one of the founders named at world creation, with the holy day set on its feast (0 when the year was full).</summary>
public sealed record SaintRow(string Name, string Epithet, bool Founder, EntityId Notable, int HolyDay)
{
    public string Full => Epithet.Length > 0 ? Name + " " + Epithet : Name;
}

/// <summary>One holy day of a religion: its day of the 60-day year (1..60), what it is (<c>feast</c> of the culture, <c>saint</c>), and its content id (the feast's id, the saint's index) and name.</summary>
public sealed record HolyDay(int DayOfYear, string Kind, string Id, string Name);

/// <summary>
/// A derived religion (faith-and-church 3): never saved, a pure function of the world seed, the content (template, doctrines, the cultures' feast days) and the history (its saints, its reliquaries).
/// Its index is its place in the template order (ascending id), so offices that name it by index are stable while the content is.
/// </summary>
public sealed class Religion
{
    public required int Index { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required ReligionTemplateDef Template { get; init; }
    public required DoctrineDef Doctrine { get; init; }
    /// <summary>The ids of the content cultures that hold this faith.</summary>
    public required IReadOnlyList<string> CultureIds { get; init; }
    public required IReadOnlyList<SaintRow> Saints { get; init; }
    /// <summary>At most <see cref="FaithSettingsDef.MaxHolyDays"/> a year, ascending by day.</summary>
    public required IReadOnlyList<HolyDay> HolyDays { get; init; }
    /// <summary>The history settlement that holds the greatest relic (the holy site), <see cref="EntityId.None"/> when none survives.</summary>
    public EntityId HolySiteHistory { get; init; }
    /// <summary>History settlements that hold a reliquary of one of its saints (shrines), ascending id.</summary>
    public IReadOnlyList<EntityId> ShrineHistory { get; init; } = [];

    public string DeityTerm(int k) => Template.DeityTerms.Count == 0 ? "" : Template.DeityTerms[((k % Template.DeityTerms.Count) + Template.DeityTerms.Count) % Template.DeityTerms.Count];

    /// <summary>Whether a day of the year (1..60) is one of the religion's holy days.</summary>
    public bool IsHoly(int dayOfYear) { foreach (var d in HolyDays) if (d.DayOfYear == dayOfYear) return true; return false; }

    public HolyDay? HolyDayOn(int dayOfYear) { foreach (var d in HolyDays) if (d.DayOfYear == dayOfYear) return d; return null; }

    /// <summary>The next <paramref name="n"/> holy days on or after <paramref name="dayOfYear"/>, with the days until each (wrapping into the next year).</summary>
    public List<(HolyDay Day, int InDays)> Upcoming(int dayOfYear, int n)
    {
        var list = new List<(HolyDay, int)>();
        foreach (var d in HolyDays) list.Add((d, ((d.DayOfYear - dayOfYear) % TimeConst.DaysPerYear + TimeConst.DaysPerYear) % TimeConst.DaysPerYear));
        return [.. list.OrderBy(x => x.Item2).ThenBy(x => x.Item1.DayOfYear).Take(n)];
    }

    /// <summary>A mask of the holy days (bit <c>doy - 1</c>), the form the schedules read.</summary>
    public ulong HolyMask { get { ulong m = 0; foreach (var d in HolyDays) m |= 1UL << (d.DayOfYear - 1); return m; } }
}

/// <summary>
/// The religions of a world (faith-and-church 3): derived by <see cref="Derive"/> from the content and the history; a culture index (the person table's byte, the continent's culture id) maps to its
/// religion through the name pools exactly as the culture's law and styles are found elsewhere.
/// </summary>
public sealed class ReligionBook
{
    public static readonly ReligionBook Empty = new([], new int[256]);
    private readonly int[] _byCulture;
    public IReadOnlyList<Religion> All { get; }
    public int Count => All.Count;

    private ReligionBook(IReadOnlyList<Religion> all, int[] byCulture) { All = all; _byCulture = byCulture; }

    /// <summary>The religion index of a continent culture index (a person's culture byte), -1 for none.</summary>
    public int OfCulture(int culture) => culture >= 0 && culture < _byCulture.Length ? _byCulture[culture] : -1;
    public Religion? Get(int index) => index >= 0 && index < All.Count ? All[index] : null;
    public Religion? Find(string id) => All.FirstOrDefault(r => r.Id == id);

    /// <summary>The content culture a continent culture index stands for (name pools in id order, a culture by its pool: as the law of inheritance and the building styles find it).</summary>
    public static CultureDef? CultureOf(ContentDatabase db, int cultureIndex)
    {
        var pools = db.All<NamePoolDef>().Select(p => p.Id).OrderBy(i => i, StringComparer.Ordinal).ToList();
        if (pools.Count == 0) return null;
        string pool = pools[((cultureIndex % pools.Count) + pools.Count) % pools.Count];
        return db.All<CultureDef>().Where(c => c.NamePool == pool).OrderBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>The 1-based day of the 60-day year of a feast (month 1..12, day 1..5).</summary>
    public static int DayOfYear(FeastDayDef f) => (Math.Clamp(f.Month, 1, 12) - 1) * 5 + Math.Clamp(f.Day, 1, 5);

    private static ulong KeyOf(string id) { ulong h = 0xCBF29CE484222325UL; foreach (char c in id) h = (h ^ c) * 0x100000001B3UL; return h; }

    /// <summary>
    /// Derives the religions of <paramref name="seed"/>'s world: one per template (ascending id), the doctrines of its row, its saints (the history's saints of its cultures by notable id, then 3 to 5 founders drawn
    /// from the template's names with stream 0x651), its holy days (the cultures' feast days, then a day for each saint on a free day of the year, at most the setting's 12) and its holy site
    /// (the settlement holding the greatest surviving reliquary of its saints). A culture no template names has no religion (-1). Pure: no state, no clock.
    /// </summary>
    public static ReligionBook Derive(ulong seed, ContentDatabase? db, WorldHistory? history)
    {
        if (db == null) return Empty;
        var templates = db.All<ReligionTemplateDef>().OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
        if (templates.Count == 0) return Empty;
        var settings = db.All<FaithSettingsDef>().OrderBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault() ?? new FaithSettingsDef { Id = "faith" };
        var doctrines = db.All<DoctrineDef>().ToList();
        var defaultDoctrine = doctrines.FirstOrDefault(d => d.Religion.Length == 0) ?? new DoctrineDef { Id = "default" };
        var cultures = db.All<CultureDef>().ToDictionary(c => c.Id, StringComparer.Ordinal);
        // the continent culture indexes that belong to each template
        var byCulture = new int[256]; Array.Fill(byCulture, -1);
        for (int c = 0; c < 256; c++)
        {
            var cu = CultureOf(db, c); if (cu == null) continue;
            int t = templates.FindIndex(x => x.Cultures.Contains(cu.Id));
            byCulture[c] = t;
        }
        var all = new List<Religion>();
        for (int ti = 0; ti < templates.Count; ti++)
        {
            var tpl = templates[ti]; ulong key = KeyOf(tpl.Id);
            var doctrine = doctrines.FirstOrDefault(d => d.Religion == tpl.Id) ?? defaultDoctrine;
            // saints: the history's of these cultures, then the founders
            var saints = new List<(string Name, string Epithet, bool Founder, EntityId Notable)>();
            if (history != null)
                foreach (var n in history.Notables.Where(n => n.Role == NotableRole.Saint && n.Culture >= 0 && n.Culture < 256 && byCulture[n.Culture] == ti).OrderBy(n => n.Id.Value))
                    saints.Add((n.Name, n.Epithet, false, n.Id));
            int nFounders = settings.FoundersMin + DetRng.Range(seed, 0, key, RngStreams.FaithFoundersAndHolyDays, 0, 0, Math.Max(1, settings.FoundersMax - settings.FoundersMin + 1));
            var given = tpl.FounderGiven.ToList(); var epith = tpl.FounderEpithets.ToList();
            for (int i = 0; i < nFounders && given.Count > 0; i++)
            {
                int g = DetRng.Range(seed, 0, key, RngStreams.FaithFoundersAndHolyDays, (uint)(10 + 2 * i), 0, given.Count);
                string name = given[g]; given.RemoveAt(g);
                string ep = "";
                if (epith.Count > 0) { int e = DetRng.Range(seed, 0, key, RngStreams.FaithFoundersAndHolyDays, (uint)(11 + 2 * i), 0, epith.Count); ep = epith[e]; epith.RemoveAt(e); }
                saints.Add((name, ep, true, EntityId.None));
            }
            // holy days: the feasts of its cultures, then a day for each saint on a free day
            var days = new List<HolyDay>(); var used = new HashSet<int>();
            foreach (string cid in tpl.Cultures.OrderBy(x => x, StringComparer.Ordinal))
                if (cultures.TryGetValue(cid, out var cu))
                    foreach (var f in cu.FeastDays)
                    {
                        int doy = DayOfYear(f);
                        if (days.Count >= settings.MaxHolyDays || !used.Add(doy)) continue;
                        days.Add(new HolyDay(doy, "feast", f.Id, f.Name));
                    }
            var rows = new List<SaintRow>();
            for (int k = 0; k < saints.Count; k++)
            {
                int doy = 0;
                if (days.Count < settings.MaxHolyDays)
                {
                    var free = Enumerable.Range(1, TimeConst.DaysPerYear).Where(d => !used.Contains(d) && (d - 1) % 5 != 4).ToList();      // never the fifth day of a month (the weekly holy day)
                    if (free.Count > 0)
                    {
                        doy = free[DetRng.Range(seed, 0, key, RngStreams.FaithFoundersAndHolyDays, (uint)(100 + k), 0, free.Count)];
                        used.Add(doy);
                        days.Add(new HolyDay(doy, "saint", k.ToString(System.Globalization.CultureInfo.InvariantCulture), saints[k].Epithet.Length > 0 ? saints[k].Name + " " + saints[k].Epithet : saints[k].Name));
                    }
                }
                rows.Add(new SaintRow(saints[k].Name, saints[k].Epithet, saints[k].Founder, saints[k].Notable, doy));
            }
            // relics: reliquaries of its saints that survive in a settlement
            EntityId site = EntityId.None; var shrines = new SortedSet<EntityId>();
            if (history != null)
            {
                var mine = saints.Where(s => !s.Founder).Select(s => s.Notable).ToHashSet();
                int bestPop = -1;
                foreach (var a in history.Artefacts.Where(a => a.Kind == "reliquary" && a.Status == ArtefactStatus.Held && mine.Contains(a.Creator)).OrderBy(a => a.Id.Value))
                {
                    var s = history.Settlements.FirstOrDefault(x => x.Id == a.Holder);
                    if (s == null || s.IsRuin) continue;
                    shrines.Add(s.Id);
                    if (s.Pop > bestPop) { bestPop = s.Pop; site = s.Id; }
                }
            }
            all.Add(new Religion
            {
                Index = ti, Id = tpl.Id, Name = tpl.Name, Template = tpl, Doctrine = doctrine, CultureIds = [.. tpl.Cultures], Saints = rows, HolyDays = [.. days.OrderBy(d => d.DayOfYear)],
                HolySiteHistory = site, ShrineHistory = [.. shrines],
            });
        }
        return new ReligionBook(all, byCulture);
    }

    // ---------------------------------------------------------------- the region

    /// <summary>The religion of a region settlement (its culture's), -1 when none.</summary>
    public int OfSettlement(RegionData region, int settlement) => settlement >= 0 && settlement < region.Settlements.Count ? OfCulture(region.Settlements[settlement].Culture) : -1;

    /// <summary>
    /// The shrines of a religion in the region, the holy site first: the settlements that hold a reliquary of its saints (the greatest first), else the largest settlement of its faith (its minster, as the
    /// spec's fallback "a shrine with a relic" leaves a region with none).
    /// </summary>
    public List<int> ShrinesIn(RegionData region, int religion)
    {
        var r = Get(religion); var list = new List<int>(); if (r == null) return list;
        var shrine = r.ShrineHistory.ToHashSet();
        var mine = new List<int>();
        for (int s = 0; s < region.Settlements.Count; s++) if (OfSettlement(region, s) == religion) mine.Add(s);
        foreach (int s in mine.Where(s => region.Settlements[s].HistoryId != EntityId.None && shrine.Contains(region.Settlements[s].HistoryId)).OrderByDescending(s => r.HolySiteHistory == region.Settlements[s].HistoryId).ThenByDescending(s => region.Settlements[s].TargetPop).ThenBy(s => s)) list.Add(s);
        if (list.Count == 0 && mine.Count > 0) list.Add(mine.OrderByDescending(s => region.Settlements[s].TargetPop).ThenBy(s => s).First());
        return list;
    }

    /// <summary>The holy site of a religion in the region (the first shrine), -1 when its faith has no settlement here.</summary>
    public int HolySiteIn(RegionData region, int religion) => ShrinesIn(region, religion) is { Count: > 0 } l ? l[0] : -1;
}
