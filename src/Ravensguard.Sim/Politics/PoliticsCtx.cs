using System.Runtime.CompilerServices;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.WorldGen.History;

namespace Ravensguard.Sim.Politics;

/// <summary>What the politics' tick reads from outside the sim world (installed by <see cref="PoliticsWiring"/>, not saved): the history the realms come from and the hero's side of the road.</summary>
public sealed class PoliticsLinks
{
    public WorldHistory? History { get; set; }
    /// <summary>A person's standing with a fief in permille (the standing book's), 0 when none.</summary>
    public Func<EntityId, int, int> StandingWithFief { get; set; } = (_, _) => 0;
    /// <summary>A person's renown in permille.</summary>
    public Func<EntityId, int> RenownPm { get; set; } = _ => 0;
    /// <summary>The contracts a person completed in a fief's settlements.</summary>
    public Func<EntityId, int, int> ContractsDone { get; set; } = (_, _) => 0;
    /// <summary>Whether a person is the hero (the lords' brain leaves his holdings to him).</summary>
    public Func<ulong, bool> IsHero { get; set; } = _ => false;
}

/// <summary>
/// What a politics pass reads and writes: the politics book, the rule's context (the economy, the people, the chronicle), the history the realms come from and the hero's links. Built per tick or per barrier; a test builds one by hand.
/// </summary>
public sealed class PoliticsCtx
{
    public required PoliticsBook B { get; init; }
    public required RuleCtx R { get; init; }
    public WorldHistory? H { get; init; }
    public PoliticsLinks Links { get; init; } = new();
    public int Day { get; init; }

    public RuleBook RB => R.B;
    public PoliticsSettingsDef S => B.Settings;
    public RuleChron Chron => R.Chron;
    public ulong Seed => R.Seed;
    public Season SeasonNow => GameTime.FromGameDays(Day).Season;

    // ------------------------------------------------------------------ realms

    public int HistoryRealms => H?.Realms.Count ?? 0;

    /// <summary>Whether a realm exists and rules: a history realm alive at the start, or one founded in play, until its last fief is gone.</summary>
    public bool Alive(int realm)
    {
        var row = B.RealmIfAny(realm);
        if (row != null && row.Dead) return false;
        if (row is { Founded: true }) return true;
        return H != null && (uint)realm < (uint)H.Realms.Count && H.Realms[realm].Alive;
    }

    /// <summary>Every realm that rules now, ascending: history's, then the founded.</summary>
    public IEnumerable<int> AliveRealms()
    {
        for (int r = 0; r < HistoryRealms; r++) if (Alive(r)) yield return r;
        foreach (var (k, row) in B.Realms) if (row.Founded && !row.Dead && k >= HistoryRealms) yield return k;
    }

    public string NameOf(int realm)
    {
        var row = B.RealmIfAny(realm); if (row is { Founded: true }) return row.Name;
        return H != null && (uint)realm < (uint)H.Realms.Count ? H.Realms[realm].Name : R.RealmName(realm);
    }

    /// <summary>The key an opinion of a realm is kept under: the realm's treasury id, as rule and income keys it.</summary>
    public ulong KeyOf(int realm) => R.RealmKey(realm).Value;

    /// <summary>The ruler's id: a founded realm's (a person), else history's notable; 0 when it has none.</summary>
    public ulong RulerOf(int realm)
    {
        var row = B.RealmIfAny(realm); if (row != null && row.Ruler != 0) return row.Ruler;
        if (RB.Rulers.TryGetValue(realm, out long p)) return (ulong)p;
        return H != null && (uint)realm < (uint)H.Realms.Count ? H.Realms[realm].Ruler.Value : 0;
    }

    /// <summary>The hero rules the realm (so the brain leaves its declarations to him).</summary>
    public bool HeroRules(int realm) { ulong r = RulerOf(realm); return r != 0 && Links.IsHero(r); }

    public LordTrait TraitsOf(int realm)
    {
        ulong r = RulerOf(realm); if (r == 0) return LordTrait.None;
        int amb = H?.GetNotable(new EntityId(r))?.Ambition ?? 50;
        return LordTraits.OfRuler(Seed, r, amb);
    }

    public LordTrait TraitsOfPerson(ulong person) => LordTraits.Of(Seed, person);

    /// <summary>A realm's real people in the home region: the pop of its fiefs, the levy a founded realm counts on.</summary>
    public int HomePop(int realm) { int n = 0; foreach (var f in RB.Fiefs.Values) if (f.Realm == realm && (uint)f.Fief < (uint)R.Region.Fiefs.Count) n += R.Region.Fiefs[f.Fief].Pop; return n; }

    /// <summary>A realm's host as it stands: history's levy less its losses (recovering), or a founded realm's levy on its fiefs' people.</summary>
    public long Strength(int realm)
    {
        var row = B.RealmIfAny(realm); int loss = row?.LossPm ?? 0;
        long baseS = row is { Founded: true } || H == null || (uint)realm >= (uint)H.Realms.Count ? (long)HomePop(realm) * S.LevyPermille / 1000 : (long)H.Realms[realm].Pop * S.LevyPermille / 1000;
        return Math.Max(1, baseS * (1000 - Math.Clamp(loss, 0, 900)) / 1000);
    }

    /// <summary>Seasons of the realm's own coin its host could be paid with: history's treasury (and what play added) against three pieces of silver a man a season, as history reckoned it.</summary>
    public int TreasurySeasons(int realm)
    {
        long coin = (H != null && (uint)realm < (uint)H.Realms.Count ? H.Realms[realm].Treasury : 0) + (B.RealmIfAny(realm)?.TreasuryDelta ?? 0);
        return (int)Math.Clamp(coin / (Strength(realm) * 3 + 1), 0, 50);
    }

    /// <summary>The realm's receipts in a year in mites: its fiefs' receipts here, or an abstract realm's people at a few mites a head.</summary>
    public long YearReceipts(int realm)
    {
        long sum = 0; foreach (var f in RB.Fiefs.Values) if (f.Realm == realm) sum += f.LastYearReceipts;
        if (sum > 0) return sum;
        long pop = H != null && (uint)realm < (uint)H.Realms.Count && !(B.RealmIfAny(realm)?.Founded ?? false) ? H.Realms[realm].Pop : HomePop(realm);
        return pop * 40;
    }

    public ulong CapitalPlace(int realm)
    {
        if (H != null && (uint)realm < (uint)H.Realms.Count && H.Realms[realm].Capital is int c and >= 0 && c < H.Settlements.Count) return H.Settlements[c].Id.Value;
        foreach (var f in RB.Fiefs.Values) if (f.Realm == realm) return R.SeatPlace(f.Fief).Value;
        return 0;
    }

    /// <summary>The realm a home fief belongs to.</summary>
    public int RealmOfFief(int fief) => RB.FiefOf(fief)?.Realm ?? -1;

    // ------------------------------------------------------------------ contact

    private static readonly ConditionalWeakTable<WorldHistory, HashSet<(int, int)>> ContactCache = new();

    /// <summary>The pairs of history realms whose final lands touch (4-neighbour on the map): the only realms that fight or treat.</summary>
    public HashSet<(int, int)> ContactPairs()
    {
        if (H == null) return [];
        return ContactCache.GetValue(H, h =>
        {
            var set = new HashSet<(int, int)>(); var o = h.FinalOwner; int n = Ravensguard.Sim.WorldGen.Continent.Size;
            if (o.Length < n * n) return set;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int a = o[y * n + x]; if (a == 255) continue;
                    if (x + 1 < n) { int b = o[y * n + x + 1]; if (b != 255 && b != a) set.Add((Math.Min(a, b), Math.Max(a, b))); }
                    if (y + 1 < n) { int b = o[(y + 1) * n + x]; if (b != 255 && b != a) set.Add((Math.Min(a, b), Math.Max(a, b))); }
                }
            return set;
        });
    }

    /// <summary>Whether two realms touch: history's by the map, a founded realm by its parent's contacts (and the parent itself).</summary>
    public bool InContact(int a, int b)
    {
        if (a == b) return false;
        a = Root(a); b = Root(b); if (a == b) return true;
        return ContactPairs().Contains((Math.Min(a, b), Math.Max(a, b)));
    }

    /// <summary>A founded realm stands where its parent stood (its contacts are the parent's); a history realm is its own root.</summary>
    public int Root(int realm) { int guard = 0; while (B.RealmIfAny(realm) is { Founded: true, Parent: >= 0 } row && guard++ < 8) realm = row.Parent; return realm; }
}
