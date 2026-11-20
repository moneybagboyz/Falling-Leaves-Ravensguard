using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Heists;

/// <summary>
/// The world's heist state (heists-and-jailbreaks 3, 5; the optional "heists" section, absent while nobody has visited a site): the sites that have been visited (the lock generation, the day the locks are to be changed, the chest's day), the keys
/// the hero carries, the run in play (a heist half done is saved as it stands) and the manhunts. A site's map is never saved: it is the template again.
/// </summary>
public sealed class HeistBook
{
    public SortedDictionary<ulong, HSite> Sites { get; } = [];
    public List<KeyRef> Keys { get; } = [];
    public HeistRun? Run { get; set; }
    public List<Manhunt> Manhunts { get; } = [];
    public int NextManhunt { get; set; } = 1;
    public long LastHour { get; set; } = -1;

    public bool IsEmpty => Sites.Count == 0 && Keys.Count == 0 && Run == null && Manhunts.Count == 0;

    /// <summary>The <c>HeistHash</c> of the trail (overview 8.9): sites, keys, lock changes, the run and the manhunts.</summary>
    public ulong Hash()
    {
        ulong h = 14695981039346656037UL;
        static ulong M(ulong h, long v) { unchecked { h ^= (ulong)v; h *= 1099511628211UL; h ^= h >> 29; return h; } }
        foreach (var (id, s) in Sites)
        {
            h = M(h, (long)id); h = M(h, s.Settlement); h = M(h, (long)s.Kind); h = M(h, s.LockGen); h = M(h, s.ChangeDay); h = M(h, s.ChestDay); h = M(h, s.Visits);
            foreach (var c in s.Template) h = M(h, c);
            foreach (var e in s.Extras) foreach (var c in e) h = M(h, c);
            foreach (var d in s.Door) h = M(h, d);
            foreach (var b in s.SconceOut) h = M(h, b ? 1 : 0);
            for (int i = 0; i < s.Slot.Length; i++) { h = M(h, (long)s.Slot[i]); h = M(h, s.Freed[i] ? 1 : 0); }
            h = M(h, s.Alarm ? 1 : 0); h = M(h, s.OuterLocked ? 1 : 0); h = M(h, s.FireX); h = M(h, s.FireY); h = M(h, s.FireUntil); h = M(h, s.NoiseSeq);
            foreach (var g in s.Guards)
            {
                h = M(h, (long)g.Person); h = M(h, g.X); h = M(h, g.Y); h = M(h, (long)g.State); h = M(h, g.Route); h = M(h, g.Timer); h = M(h, g.TargetX); h = M(h, g.TargetY);
                h = M(h, g.KeysTaken ? 1 : 0); h = M(h, g.Searches); h = M(h, g.Listened); h = M(h, g.Aware ? 1 : 0); h = M(h, g.Keys.Count); h = M(h, g.Familiarity);
            }
            foreach (var n in s.Noises) foreach (var v in n) h = M(h, v);
        }
        foreach (var k in Keys) { h = M(h, (long)k.Site); h = M(h, k.Door); h = M(h, k.Generation); }
        if (Run is { } r)
        {
            h = M(h, (long)r.Cur); h = M(h, (long)r.Partner); h = M(h, r.X); h = M(h, r.Y); h = M(h, r.Sec); h = M(h, r.Weather); h = M(h, (long)r.Outcome);
            h = M(h, (r.Sneak ? 1 : 0) | (r.Torch ? 2 : 0) | (r.Running ? 4 : 0) | (r.Livery ? 8 : 0) | (r.Carrying ? 16 : 0));
            h = M(h, r.BusyKind); h = M(h, r.BusyTarget); h = M(h, r.BusyLeft); h = M(h, r.BusyTotal); h = M(h, r.BusyAttempt);
            foreach (var (g, k) in r.Seen) { h = M(h, (long)g); h = M(h, k ? 1 : 0); }
            foreach (var f in r.Followers) h = M(h, (long)f);
            foreach (var t in r.Taken) foreach (var c in t) h = M(h, c);
        }
        foreach (var m in Manhunts) { h = M(h, m.Id); h = M(h, m.Settlement); h = M(h, m.StartSec); h = M(h, m.Ended ? 1 : 0); h = M(h, (long)m.Entry); foreach (var p in m.Persons) h = M(h, (long)p); }
        return M(h, NextManhunt);
    }
}

/// <summary>Which places have which heist sites, how a site is first made, and how a run begins (heists-and-jailbreaks 3, 4.1).</summary>
public static class HeistSites
{
    /// <summary>The tier of a place's sites: a castle's (a town that is a fief's seat), a town's, or a manor's cellar.</summary>
    public static int TierOf(RegionSettlement s, bool fiefSeat) => s.Class == SettlementClass.Town ? (fiefSeat ? 2 : 1) : 0;

    /// <summary>
    /// The sites of the home region (generated on first visit, so this is only the plan): every manor, fief seat and town has a gaol; every town and seat a strongroom; a town with a guildmaster a guild vault; every town an undercroft and a drain
    /// (the drain joins the gaol). Villages and farmsteads without a manor have none.
    /// </summary>
    public static List<(int Settlement, HeistKind Kind)> Plan(Region region, PersonTable? people)
    {
        var seats = new HashSet<int>(region.Fiefs.Select(f => f.SeatSettlement));
        var guild = new HashSet<int>();
        if (people != null) for (int i = 0; i < people.Count; i++) if (people.IsAlive(i) && people.Occupation[i] == Occupation.GuildMaster) guild.Add(people.HomeSettlement[i]);
        var l = new List<(int, HeistKind)>();
        for (int i = 0; i < region.Settlements.Count; i++)
        {
            var s = region.Settlements[i]; bool town = s.Class == SettlementClass.Town, seat = seats.Contains(i), manor = s.Plan.ManorLot >= 0;
            if (town || seat || manor) l.Add((i, HeistKind.Gaol));
            if (town || seat) l.Add((i, HeistKind.Strongroom));
            if (town && guild.Contains(i)) l.Add((i, HeistKind.Vault));
            if (town) { l.Add((i, HeistKind.Undercroft)); l.Add((i, HeistKind.Drain)); }
        }
        return l;
    }

    /// <summary>The site of a place and kind: made from its template on first need (the template by the site id, the chest's goods by stream 0x6B2), then kept.</summary>
    public static HSite? Open(HeistBook book, HeistRules rules, ulong seed, int settlement, HeistKind kind, int tier)
    {
        ulong id = HSite.IdOf(settlement, kind);
        if (book.Sites.TryGetValue(id, out var s)) { s.Map ??= rules.Map(s.Template); return s.Map == null ? null : s; }
        var t = rules.Pick(kind, tier, id); if (t == null) return null;
        var map = rules.Map(t.Id)!;
        s = new HSite { Id = id, Settlement = settlement, Kind = kind, Template = t.Id, Map = map, Extras = ExtrasOf(seed, id, kind, map) };
        book.Sites[id] = s;
        return s;
    }

    private static readonly string[][] Goods =
    [
        ["silver_chalice", "silver_brooch", "gold_torc", "pewter_plate"], ["psalter", "hermits_book", "silver_brooch", "reliquary"], ["wine_cask", "salt_barrel", "cloth_bale", "pewter_plate"], [], [], [],
    ];

    /// <summary>The goods and papers in a chest beyond its purse (0x6B2, key the site): a strongroom's plate, a vault's manuscripts, a cellar's casks; none where there is no chest.</summary>
    public static List<string> ExtrasOf(ulong seed, ulong siteId, HeistKind kind, HeistMap map)
    {
        var l = new List<string>(); if (map.Chest.X < 0) return l;
        var pool = kind switch { HeistKind.Strongroom => Goods[0], HeistKind.Vault => Goods[1], HeistKind.Undercroft => Goods[2], _ => [] };
        if (pool.Length == 0) return l;
        int n = 1 + DetRng.Range(seed, 0, siteId, RngStreams.VaultContents, 0, 0, 3);
        for (int i = 0; i < n; i++) l.Add(pool[DetRng.Range(seed, 0, siteId, RngStreams.VaultContents, (uint)(1 + i), 0, pool.Length)]);
        return l;
    }

    /// <summary>Clears and lays out a site's run state for a run beginning at <paramref name="sec"/>: doors shut (a broken one stays broken), sconces alight, the captives in their slots, the guards for the hour with the keeper's keys on his belt.</summary>
    public static void Prepare(HeistBook book, HeistRules rules, ulong seed, HSite s, long sec, IReadOnlyList<ulong> realGuards, IReadOnlyList<ulong> captives, Func<ulong, int>? honesty = null, Func<ulong, int>? familiarity = null)
    {
        var m = s.Map!; int n = m.Doors.Count + 1;
        var door = new byte[n];
        for (int i = 0; i < m.Doors.Count; i++) door[i] = s.Door.Length == n && s.Door[i] == 3 ? (byte)3 : (byte)0;
        door[n - 1] = 2;
        s.Door = door; s.SconceOut = new bool[m.Sconces.Count];
        s.Slot = new ulong[m.Slots.Count]; s.Freed = new bool[m.Slots.Count];
        for (int i = 0; i < s.Slot.Length && i < captives.Count; i++) s.Slot[i] = captives[i];
        s.Alarm = false; s.OuterLocked = false; s.FireX = s.FireY = -1; s.FireUntil = 0; s.Noises = []; s.NoiseSeq = 0;
        int men = m.Kind == HeistKind.Camp ? 2 : Math.Min(GuardRoster.MaxMen, Math.Max(m.Guards, realGuards.Count));
        var roster = GuardRoster.Build(s.Id, realGuards, m.Guards == 0 ? 0 : men);
        s.Guards = GuardRoster.Layout(rules, seed, m, roster, (int)(sec / 3600 % 24), sec / 86_400, s.Id);
        var locked = m.Doors.Where(d => d.Quality >= 0).Select(d => d.Index).ToList();
        foreach (var g in s.Guards)
        {
            if (g.Keeper) g.Keys = [.. locked];
            if (honesty != null && !g.Synthetic) g.Honesty = honesty(g.Person);
            if (familiarity != null && !g.Synthetic) g.Familiarity = familiarity(g.Person);
        }
        s.Visits++;
    }

    /// <summary>
    /// Begins a heist at a place's site: the site (and, for a drain or a gaol with a drain, its partner) made and prepared for the hour, the hero at the way in. <paramref name="realGuards"/> are the place's guards in id order, <paramref name="captives"/> the
    /// persons held there (in slot order). Null when no template fits.
    /// </summary>
    public static HeistRun? Start(HeistBook b, HeistRules r, ulong seed, int settlement, HeistKind kind, int tier, long sec, int weather, IReadOnlyList<ulong> realGuards, IReadOnlyList<ulong> captives,
        Func<ulong, int>? honesty = null, Func<ulong, int>? familiarity = null)
    {
        var s = Open(b, r, seed, settlement, kind, tier); if (s == null) return null;
        HSite? partner = null;
        if (kind == HeistKind.Drain) partner = Open(b, r, seed, settlement, HeistKind.Gaol, tier);
        else if (kind == HeistKind.Gaol && s.Map!.PortalB.X >= 0) partner = Open(b, r, seed, settlement, HeistKind.Drain, tier);
        Prepare(b, r, seed, s, sec, kind == HeistKind.Drain ? [] : realGuards, kind == HeistKind.Drain ? [] : captives, honesty, familiarity);
        if (partner != null) Prepare(b, r, seed, partner, sec, kind == HeistKind.Drain ? realGuards : [], kind == HeistKind.Drain ? captives : [], honesty, familiarity);
        var run = new HeistRun(r, seed, b.Sites, b.Keys, s.Id, sec, weather) { Partner = partner?.Id ?? 0 };
        b.Run = run;
        return run;
    }

    /// <summary>Ends a run at the barrier: the guards go off, the doors are locked again (a broken one stays broken), what a thief took is gone and the locks will be changed if keys were stolen.</summary>
    public static void Close(HeistBook book, HeistRules rules, long sec)
    {
        if (book.Run is not { } run) return;
        foreach (var s in run.Sites.Values)
        {
            if (s.Guards.Any(g => g.KeysTaken) && s.ChangeDay < 0) s.ChangeDay = (int)(sec / 86_400) + rules.S.ChangeLocksDays;
            for (int i = 0; i < s.Door.Length; i++) if (s.Door[i] != 3) s.Door[i] = 0;
            s.Guards = []; s.Noises = []; s.Slot = []; s.Freed = []; s.SconceOut = []; s.Alarm = false; s.OuterLocked = false; s.FireX = s.FireY = -1;
        }
        book.Run = null;
    }
}
