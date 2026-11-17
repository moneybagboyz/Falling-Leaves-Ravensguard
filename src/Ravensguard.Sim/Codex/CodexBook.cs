using System.Globalization;
using System.Text;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Codex;

/// <summary>
/// The Codex of one world: legends from the world history (major events, realms, dynasties, famous artefacts and people), the places and landmarks of the
/// embark region, and the rules of play. Entries are locked until "heard of": for now that means they lie within the embark region's realm(s), or hang on
/// a site or landmark of the region; landmarks themselves unlock when the hero has been near them. Rules are always open.
/// </summary>
public sealed class CodexBook
{
    /// <summary>Event types that make a legend worth a page.</summary>
    public static readonly HashSet<string> MajorTypes = new(StringComparer.Ordinal)
    {
        "battle", "old_battle", "realm_founded", "realm_fell", "realm_merged", "realm_split", "coup", "usurpation", "succession_crisis", "rebellion", "rebellion_crushed",
        "plague_begins", "plague_ends", "plague_spreads", "famine", "flood", "saint_rises", "saint_died", "saint_martyred", "settlement_sacked", "settlement_captured",
        "settlement_abandoned", "settlement_emptied", "siege_begins", "war_declared", "peace_signed", "artefact_forged", "line_extinct", "mine_collapse", "outlaw_band",
        "outlaw_slain", "claimant_executed", "betrayal", "master_crafter", "treaty_lapses",
    };

    private readonly List<CodexEntry> _entries = [];
    private readonly Dictionary<string, CodexEntry> _byId = new(StringComparer.Ordinal);
    private readonly List<(CodexEntry Entry, int X, int Y)> _landmarkPlaces = [];

    public IReadOnlyList<CodexEntry> Entries => _entries;
    public CodexEntry? Get(string id) => _byId.GetValueOrDefault(id);
    public int UnlockedCount => _entries.Count(e => e.Unlocked);
    public IEnumerable<CodexEntry> In(CodexCategory c) => _entries.Where(e => e.Category == c);

    /// <summary>Builds the book. <paramref name="region"/> and <paramref name="rules"/> may be null (a world with no region yet; a game with no manual pages).</summary>
    public static CodexBook Build(WorldHistory h, RegionData? region = null, IEnumerable<CodexRule>? rules = null)
    {
        var b = new CodexBook();
        var inRegionRealms = new HashSet<int>();
        var regionEvents = new HashSet<EntityId>();
        if (region != null)
        {
            foreach (var r in region.Realms) inRegionRealms.Add(r.HistoryIndex);
            foreach (var s in region.Sites) { if (s.HistoryEvent != EntityId.None) regionEvents.Add(s.HistoryEvent); if (s.CauseEvent != EntityId.None) regionEvents.Add(s.CauseEvent); }
            foreach (var l in region.Landmarks ?? []) if (l.Event != EntityId.None) regionEvents.Add(l.Event);
        }
        var realmById = h.Realms.ToDictionary(r => r.Id);
        int RealmOf(EntityId id)
        {
            if (realmById.TryGetValue(id, out var rm)) return rm.Index;
            if (h.GetSettlement(id) is { } s) return s.Realm;
            if (h.GetNotable(id) is { } n) return n.Realm >= 0 ? n.Realm : n.HomeRealm;
            return -1;
        }
        bool CellIn(ChronicleEntry e)
        {
            if (region == null || !e.Data.TryGetValue("cell", out var cs) || !int.TryParse(cs, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cell)) return false;
            int cx = cell % Continent.Size - region.CellX, cy = cell / Continent.Size - region.CellY;
            return (uint)cx < 16 && (uint)cy < 16;
        }

        // ---- legends: the major events
        var unlockedEvents = new HashSet<EntityId>();
        var majorSubjects = new HashSet<EntityId>();
        foreach (var e in h.Chronicle.All)
        {
            if (!MajorTypes.Contains(e.TypeId)) continue;
            bool open = regionEvents.Contains(e.Id) || CellIn(e) || e.Subjects.Any(s => inRegionRealms.Contains(RealmOf(s)));
            string title = e.Data.GetValueOrDefault("title", e.TypeId), text = e.Data.GetValueOrDefault("text", "").Trim();
            var sb = new StringBuilder();
            sb.Append(e.When.Season).Append(' ').Append(e.When.Year).Append(": ").Append(text.Length > 0 ? text : title);
            if (sb[^1] != '.') sb.Append('.');
            foreach (var c in e.Causes)
            {
                string note = c.Note.Length > 0 ? c.Note : c.Kind.ToString().ToLowerInvariant();
                sb.Append("\nBecause of ").Append(note);
                if (c.EventId is { } cid && h.Chronicle.Get(cid) is { } ce) sb.Append(" (").Append(ce.Data.GetValueOrDefault("title", ce.TypeId)).Append(", year ").Append(ce.When.Year).Append(')');
                sb.Append('.');
            }
            b.Add(new CodexEntry { Id = "legend." + e.Id.Counter, Category = CodexCategory.Legend, Title = title, Text = sb.ToString(), Sources = [e.Id], Year = e.When.Year }, open);
            if (open) unlockedEvents.Add(e.Id);
            foreach (var s in e.Subjects) majorSubjects.Add(s);
        }

        // ---- legends: famous artefacts
        foreach (var a in h.Artefacts)
        {
            bool open = (region?.Sites.Any(s => s.Artefact == a.Id) ?? false) || (a.Cell >= 0 && region != null && CellInRegion(region, a.Cell))
                || inRegionRealms.Contains(RealmOf(a.Holder)) || unlockedEvents.Contains(a.CreatedEvent);
            var sb = new StringBuilder();
            sb.Append(a.Name).Append(", a ").Append(a.Kind.Length > 0 ? a.Kind : "treasure").Append('.');
            if (h.GetNotable(a.Creator) is { } maker) sb.Append(" Made by ").Append(maker.Full).Append(" in year ").Append(a.CreatedTick / 4).Append('.');
            sb.Append(a.Status switch
            {
                ArtefactStatus.Held => " It is held to this day" + (h.GetSettlement(a.Holder) is { } hs ? " at " + hs.Name : h.GetNotable(a.Holder) is { } hn ? " by " + hn.Full : "") + ".",
                ArtefactStatus.Lost => " It was lost in year " + (a.LostTick / 4) + (a.LossNote.Length > 0 ? ": " + a.LossNote : "") + ".",
                _ => " It lies buried" + (a.LossNote.Length > 0 ? ": " + a.LossNote : "") + ".",
            });
            var src = new List<EntityId>();
            foreach (var ev in new[] { a.CreatedEvent, a.LossEvent }) if (ev != EntityId.None && h.Chronicle.Get(ev) != null) src.Add(ev);
            b.Add(new CodexEntry { Id = "artefact." + a.Id.Counter, Category = CodexCategory.Legend, Title = a.Name, Text = sb.ToString(), Sources = src, Year = a.CreatedTick / 4 }, open);
        }

        // ---- places: realms
        foreach (var r in h.Realms)
        {
            bool open = inRegionRealms.Contains(r.Index);
            var sb = new StringBuilder();
            sb.Append(r.Name).Append(r.Alive ? ", a realm still standing" : ", a realm that fell").Append(". Founded in year ").Append(r.FoundedTick / 4).Append('.');
            if (r.Capital >= 0 && r.Capital < h.Settlements.Count) sb.Append(" Its capital is ").Append(h.Settlements[r.Capital].Name).Append('.');
            if (r.Alive && h.GetNotable(r.Ruler) is { } ruler) sb.Append(" It is ruled by ").Append(ruler.Full).Append(h.GetDynasty(r.Dynasty) is { } dy ? " of House " + dy.Name : "").Append('.');
            if (!r.Alive) sb.Append(" It ended in year ").Append(r.EndTick / 4).Append(r.EndKind.Length > 0 ? " (" + r.EndKind + ")" : "").Append('.');
            var src = new List<EntityId>();
            foreach (var ev in new[] { r.FoundedEvent, r.EndEvent }) if (ev != EntityId.None && h.Chronicle.Get(ev) != null) src.Add(ev);
            b.Add(new CodexEntry { Id = "realm." + r.Index, Category = CodexCategory.Place, Title = r.Name, Text = sb.ToString(), Sources = src, Year = r.FoundedTick / 4 }, open);
        }

        // ---- places: the towns and villages of the embark region
        if (region != null)
            foreach (var s in region.Settlements.Where(s => s.Class != SettlementClass.Farmstead).OrderBy(s => s.Id.Counter))
            {
                string realm = s.Realm >= 0 && s.Realm < h.Realms.Count ? h.Realms[s.Realm].Name : "no realm";
                string text = $"{s.Name}, a {(s.Class == SettlementClass.Town ? "town" : "village")} of about {s.TargetPop} people in {realm}.";
                var src = new List<EntityId>();
                if (h.GetSettlement(s.HistoryId) is { } hs && hs.FoundedTick > 0) text += $" It was founded in year {hs.FoundedTick / 4}.";
                b.Add(new CodexEntry { Id = "place." + s.Id.Counter, Category = CodexCategory.Place, Title = s.Name, Text = text, Sources = src }, true);
            }

        // ---- people and houses
        var dynastiesShown = new HashSet<EntityId>();
        foreach (var n in h.Notables)
        {
            bool notable = n.Role is NotableRole.Ruler or NotableRole.Saint or NotableRole.Rebel or NotableRole.Outlaw or NotableRole.General or NotableRole.MasterCrafter || majorSubjects.Contains(n.Id);
            if (!notable) continue;
            bool open = inRegionRealms.Contains(n.Realm >= 0 ? n.Realm : n.HomeRealm) || unlockedEvents.Any(ev => h.Chronicle.Get(ev)!.Subjects.Contains(n.Id));
            var sb = new StringBuilder();
            sb.Append(n.Full).Append(", ").Append(RoleName(n.Role)).Append(", born in year ").Append(n.BirthYear).Append('.');
            if (n.DeathYear >= 0) sb.Append(" Died in year ").Append(n.DeathYear).Append(n.DeathCause.Length > 0 ? ", " + n.DeathCause : "").Append('.');
            else sb.Append(" Still living.");
            if (h.GetDynasty(n.Dynasty) is { } d) { sb.Append(" Of House ").Append(d.Name).Append('.'); dynastiesShown.Add(d.Id); }
            if (n.Residence >= 0 && n.Residence < h.Settlements.Count) sb.Append(" Lived at ").Append(h.Settlements[n.Residence].Name).Append('.');
            var src = new List<EntityId>();
            foreach (var ev in new[] { n.RiseEvent, n.DeathEvent }) if (ev != EntityId.None && h.Chronicle.Get(ev) != null) src.Add(ev);
            b.Add(new CodexEntry { Id = "person." + n.Id.Counter, Category = CodexCategory.Person, Title = n.Full, Text = sb.ToString(), Sources = src, Year = n.BirthYear }, open);
        }
        foreach (var r in h.Realms) if (r.Dynasty != EntityId.None) dynastiesShown.Add(r.Dynasty);
        foreach (var d in h.Dynasties)
        {
            if (!dynastiesShown.Contains(d.Id)) continue;
            bool open = h.Realms.Any(r => r.Dynasty == d.Id && inRegionRealms.Contains(r.Index));
            string text = $"House {d.Name}, founded in year {d.FoundedYear}" + (h.GetNotable(d.Founder) is { } f ? $" by {f.Full}" : "") + (d.Ruling ? " and still on the throne." : ".");
            b.Add(new CodexEntry { Id = "dynasty." + d.Id.Counter, Category = CodexCategory.Person, Title = "House " + d.Name, Text = text, Year = d.FoundedYear }, open);
        }

        // ---- landmarks: locked until the hero has been near them
        if (region?.Landmarks is { } marks)
            foreach (var l in marks)
            {
                var src = l.Event != EntityId.None ? new List<EntityId> { l.Event } : [];
                var e = new CodexEntry { Id = "landmark." + l.Index, Category = CodexCategory.Landmark, Title = l.Name, Text = l.Lore, Sources = src, Year = l.Year > 0 ? l.Year : -1 };
                b.Add(e, false);
                b._landmarkPlaces.Add((e, l.X, l.Y));
            }

        // ---- the rules of play
        if (rules != null) foreach (var r in rules) b.Add(new CodexEntry { Id = r.Id, Category = CodexCategory.Rule, Title = r.Title, Text = r.Text }, true);

        b._entries.Sort((x, y) =>
        {
            int c = x.Category.CompareTo(y.Category);
            if (c != 0) return c;
            c = x.Category == CodexCategory.Rule || x.Category == CodexCategory.Landmark ? 0 : x.Year.CompareTo(y.Year);
            return c != 0 ? c : string.CompareOrdinal(x.Id, y.Id);
        });
        return b;
    }

    private static bool CellInRegion(RegionData region, int cell)
    {
        int cx = cell % Continent.Size - region.CellX, cy = cell / Continent.Size - region.CellY;
        return (uint)cx < 16 && (uint)cy < 16;
    }

    private static string RoleName(NotableRole r) => r switch
    {
        NotableRole.Ruler => "a ruler", NotableRole.Heir => "an heir", NotableRole.General => "a general", NotableRole.Rebel => "a rebel", NotableRole.MasterCrafter => "a master crafter",
        NotableRole.Saint => "a saint", NotableRole.Outlaw => "an outlaw", _ => "a noble",
    };

    private void Add(CodexEntry e, bool unlocked)
    {
        if (_byId.ContainsKey(e.Id)) return;
        e.Unlocked = unlocked;
        _entries.Add(e);
        _byId[e.Id] = e;
    }

    // ------------------------------------------------------------------ playing

    /// <summary>Opens an entry; true when it was locked before.</summary>
    public bool Unlock(string id)
    {
        if (!_byId.TryGetValue(id, out var e) || e.Unlocked) return false;
        e.Unlocked = true;
        return true;
    }

    /// <summary>The hero stands at (x, y) region metres: every landmark within <paramref name="radiusM"/> unlocks its page, and the legend it hangs on. Returns the newly opened entries.</summary>
    public IReadOnlyList<CodexEntry> Visit(int x, int y, int radiusM = 90)
    {
        List<CodexEntry>? opened = null;
        long r2 = (long)radiusM * radiusM;
        foreach (var (e, lx, ly) in _landmarkPlaces)
        {
            long dx = lx - x, dy = ly - y;
            if (e.Unlocked || dx * dx + dy * dy > r2) continue;
            e.Unlocked = true;
            (opened ??= []).Add(e);
            foreach (var src in e.Sources)
                if (_byId.TryGetValue("legend." + src.Counter, out var leg) && !leg.Unlocked) { leg.Unlocked = true; opened.Add(leg); }
        }
        return opened ?? (IReadOnlyList<CodexEntry>)[];
    }

    /// <summary>Entries whose title or text contains every word of the query; locked entries never match. Optionally limited to a category.</summary>
    public IEnumerable<CodexEntry> Search(string query, CodexCategory? category = null)
    {
        var terms = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var e in _entries)
        {
            if (category != null && e.Category != category) continue;
            if (terms.Length == 0) { yield return e; continue; }
            if (!e.Unlocked) continue;
            string blob = (e.Title + " " + e.Text).ToLowerInvariant();
            if (terms.All(t => blob.Contains(t, StringComparison.Ordinal))) yield return e;
        }
    }

    /// <summary>SHA-256 over what the book holds (ids, categories, titles, texts, years, sources; not which entries are unlocked), for determinism tests.</summary>
    public string ComputeHash()
    {
        using var h = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        var b8 = new byte[8];
        void L(long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b8, v); h.AppendData(b8); }
        void S(string s) { L(s.Length); h.AppendData(Encoding.UTF8.GetBytes(s)); }
        L(_entries.Count);
        foreach (var e in _entries)
        {
            S(e.Id); L((long)e.Category); S(e.Title); S(e.Text); L(e.Year);
            L(e.Sources.Count); foreach (var s in e.Sources) L((long)s.Value);
        }
        foreach (var (e, x, y) in _landmarkPlaces) { S(e.Id); L(x); L(y); }
        return Convert.ToHexString(h.GetHashAndReset());
    }

    // ------------------------------------------------------------------ saving

    /// <summary>The ids to save: every unlocked entry except the rules (those are always open).</summary>
    public CodexProgress ToProgress()
    {
        var p = new CodexProgress();
        foreach (var e in _entries) if (e.Unlocked && e.Category != CodexCategory.Rule) p.Unlocked.Add(e.Id);
        return p;
    }

    /// <summary>Opens the entries a save recorded; ids this world no longer has are ignored.</summary>
    public void Apply(CodexProgress p) { foreach (var id in p.Unlocked) Unlock(id); }
}
