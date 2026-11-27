using Ravensguard.Content;

namespace Ravensguard.Sim.Characters;

/// <summary>
/// P8-AT-1 (ambitions spec 3.2, 3.4): what the progress rules read of the hero and the world, as plain integers. A world builds one (the hourly check, a big event); a test fills it by hand.
/// Money is <c>long</c> mites; holdings in <see cref="WealthMites"/> are valued at the settlement's last market price by the caller, never as a float.
/// </summary>
public sealed class AmbitionFacts
{
    public long CoinPence { get; set; }
    public long CoinMites { get; set; }
    public int Followers { get; set; }
    public int Contracts { get; set; }
    public int MenLed { get; set; }
    public int BattlesWon { get; set; }
    public int DynastyGenerations { get; set; }
    /// <summary>The highest lair tier of a monster the hero slew; 0 none.</summary>
    public int MonsterTierSlain { get; set; }
    /// <summary>The highest tier of a works the hero owns; 0 none.</summary>
    public int WorksTier { get; set; }
    /// <summary>The hero's fief rank, 0 none (see <see cref="AmbitionRules.FiefRanks"/>).</summary>
    public int FiefRank { get; set; }
    public bool RulesRealm { get; set; }
    /// <summary>How many realms recognise the hero's faction (0 when none is founded).</summary>
    public int FactionRecognisedBy { get; set; }
    public bool GuildOffice { get; set; }
    /// <summary>The church office held: empty, <c>abbot</c> or <c>bishop</c> (or a lesser one).</summary>
    public string ChurchOffice { get; set; } = "";
    public bool OwnsRelic { get; set; }
    public long WealthMites { get; set; }
    public Dictionary<string, int> Skills { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> Items { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> Reputation { get; } = new(StringComparer.Ordinal);
    /// <summary>Person kind to the best trust the hero holds with one of that kind.</summary>
    public Dictionary<string, int> Trust { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Ranks { get; } = new(StringComparer.Ordinal);
    public HashSet<string> BuildingKinds { get; } = new(StringComparer.Ordinal);
    public HashSet<string> PlaceKinds { get; } = new(StringComparer.Ordinal);
}

/// <summary>The progress rule of each ambition condition (spec 3.2): 0 to 1,000 pm, the lowest condition being a step's progress.</summary>
public static class AmbitionRules
{
    public const int Full = 1000;
    /// <summary>Fief ranks by name, lowest first.</summary>
    public static readonly string[] FiefRanks = ["", "landholder", "baron", "count", "duke"];

    private static int I(ConditionRefDef c, string k, int d) => c.Args.TryGetValue(k, out var v) && int.TryParse(v, out int i) ? i : d;
    private static string S(ConditionRefDef c, string k) => c.Args.TryGetValue(k, out var v) ? v : "";

    /// <summary>A numeric condition: <c>min(1000, 1000 x have / need)</c>, integer division; a need of 0 or less is met.</summary>
    public static int Numeric(long have, long need) => need <= 0 ? Full : have <= 0 ? 0 : (int)Math.Min(Full, have * Full / need);
    private static int YesNo(bool b) => b ? Full : 0;

    /// <summary>The condition's progress in pm; an id with no rule reads 0, and a <c>not</c> arg of <c>true</c> turns a met condition into 0 and an unmet one into 1,000.</summary>
    public static int Progress(ConditionRefDef c, AmbitionFacts f)
    {
        int p = Raw(c, f);
        if (S(c, "not") != "true") return p;
        return p >= Full ? 0 : Full;
    }

    private static int Raw(ConditionRefDef c, AmbitionFacts f)
    {
        switch (c.Id)
        {
            case "has_coin_at_least": return c.Args.ContainsKey("mites") ? Numeric(f.CoinMites, I(c, "mites", 0)) : Numeric(f.CoinPence, I(c, "pence", 0));
            case "has_followers_at_least": return Numeric(f.Followers, I(c, "count", 1));
            case "completed_contract_count": return Numeric(f.Contracts, I(c, "count", 1));
            case "has_proficiency": return Numeric(f.Skills.GetValueOrDefault(S(c, "skill")), I(c, "level", 0));
            case "has_item": return Numeric(f.Items.GetValueOrDefault(S(c, "item")), I(c, "count", 1));
            case "has_reputation_at_least": return Numeric(f.Reputation.GetValueOrDefault(S(c, "scope")), I(c, "value", 0));
            case "has_rank": return YesNo(f.Ranks.Contains(S(c, "rank")));
            case "owns_building": return YesNo(f.BuildingKinds.Contains(S(c, "kind")));
            case "is_in_place_kind": return YesNo(f.PlaceKinds.Contains(S(c, "kind")));
            case "knows_person": return YesNo(f.Trust.TryGetValue(S(c, "kind"), out int t) && t >= I(c, "min_trust", 0));
            case "holds_fief_rank": return Numeric(f.FiefRank, Math.Max(1, Array.IndexOf(FiefRanks, S(c, "rank"))));
            case "rules_realm": return YesNo(f.RulesRealm);
            case "founded_faction": return Numeric(f.FactionRecognisedBy, I(c, "recognised", 1));
            case "dynasty_generations": return Numeric(f.DynastyGenerations, I(c, "count", 1));
            case "slew_monster": return Numeric(f.MonsterTierSlain, I(c, "tier", 1));
            case "owns_works_tier": return Numeric(f.WorksTier, I(c, "tier", 1));
            case "holds_guild_office": return YesNo(f.GuildOffice);
            case "holds_church_office": { string k = S(c, "kind"); return YesNo(f.ChurchOffice.Length > 0 && (k is "" or "any" || f.ChurchOffice == k || (k == "abbot" && f.ChurchOffice == "bishop"))); }
            case "owns_relic": return YesNo(f.OwnsRelic);
            case "wealth_at_least": return Numeric(f.WealthMites, long.TryParse(S(c, "mites"), out long m) ? m : 0);
            case "leads_men_at_least": return Numeric(f.MenLed, I(c, "count", 1));
            case "won_battles": return Numeric(f.BattlesWon, I(c, "count", 1));
            default: return 0;
        }
    }

    /// <summary>A step's progress: the lowest of its conditions' (an empty step is 1,000).</summary>
    public static int StepProgress(AmbitionStepDef step, AmbitionFacts f)
    {
        int lo = Full;
        foreach (var c in step.Conditions) lo = Math.Min(lo, Progress(c, f));
        return lo;
    }

    public static bool StepDone(AmbitionStepDef step, AmbitionFacts f) => StepProgress(step, f) >= Full;

    public static bool AllHold(IReadOnlyList<ConditionRefDef> cs, AmbitionFacts f)
    {
        foreach (var c in cs) if (Progress(c, f) < Full) return false;
        return true;
    }

    /// <summary>The ids that have a progress rule: every condition id the tree uses must be one.</summary>
    public static readonly HashSet<string> Ruled = new(StringComparer.Ordinal)
    {
        "has_coin_at_least", "has_followers_at_least", "completed_contract_count", "has_proficiency", "has_item", "has_reputation_at_least", "has_rank", "owns_building", "is_in_place_kind",
        "knows_person", "holds_fief_rank", "rules_realm", "founded_faction", "dynasty_generations", "slew_monster", "owns_works_tier", "holds_guild_office", "holds_church_office", "owns_relic",
        "wealth_at_least", "leads_men_at_least", "won_battles",
    };
}

/// <summary>The ambition tree over the loaded content (spec 3.1): checks, and the offer after an ambition is done.</summary>
public sealed class AmbitionTree
{
    public const int MaxOffered = 4;
    /// <summary>The endings the great ambitions name (endings spec 3.4, decision D1).</summary>
    public static readonly string[] Endings = ["dynasty", "crown", "realm", "legend", "merchant_prince", "holy_life"];

    private readonly List<AmbitionDef> _all;
    private readonly Dictionary<string, AmbitionDef> _byId;
    public AmbitionTree(IEnumerable<AmbitionDef> ambitions)
    {
        _all = [.. ambitions];
        _byId = _all.ToDictionary(a => a.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<AmbitionDef> All => _all;
    public AmbitionDef? Get(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Everything wrong with the tree: an <c>opens</c> that does not resolve, a cycle, a great ambition with no valid ending, a tier that is not one of the three, a condition with no rule. Empty when sound.</summary>
    public List<string> Problems()
    {
        var bad = new List<string>();
        foreach (var a in _all)
        {
            if (a.Tier is not ("start" or "middle" or "great")) bad.Add($"{a.Id}: tier '{a.Tier}'");
            if (a.Tier == "great" && !Endings.Contains(a.Ending)) bad.Add($"{a.Id}: great ambition names no valid ending ('{a.Ending}')");
            if (a.Tier != "great" && a.Ending.Length > 0) bad.Add($"{a.Id}: only a great ambition names an ending");
            foreach (var o in a.Opens.Concat(a.Excludes)) if (!_byId.ContainsKey(o)) bad.Add($"{a.Id}: '{o}' does not resolve");
            foreach (var c in a.Requires.Concat(a.Steps.SelectMany(s => s.Conditions))) if (!AmbitionRules.Ruled.Contains(c.Id)) bad.Add($"{a.Id}: condition '{c.Id}' has no progress rule");
        }
        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 1 visiting, 2 done
        void Visit(string id)
        {
            if (!_byId.TryGetValue(id, out var a)) return;
            if (state.TryGetValue(id, out int s)) { if (s == 1) bad.Add($"{id}: opens cycle"); return; }
            state[id] = 1;
            foreach (var o in a.Opens) Visit(o);
            state[id] = 2;
        }
        foreach (var a in _all) Visit(a.Id);
        return bad;
    }

    /// <summary>
    /// The offer once <paramref name="justDone"/> is finished (3.1): its <c>opens</c> whose <c>requires</c> hold, then any other middle ambition whose <c>requires</c> hold and which no done ambition excludes;
    /// never a done or the current ambition; at most four, the opens in their listed order and the rest in content order.
    /// </summary>
    public List<string> Offer(string justDone, IReadOnlyCollection<string> done, string? current, AmbitionFacts facts)
    {
        var doneSet = new HashSet<string>(done, StringComparer.Ordinal) { justDone };
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in doneSet) if (_byId.TryGetValue(d, out var da)) foreach (var x in da.Excludes) excluded.Add(x);
        var offer = new List<string>();
        bool Eligible(AmbitionDef a) => !doneSet.Contains(a.Id) && a.Id != current && AmbitionRules.AllHold(a.Requires, facts);
        if (_byId.TryGetValue(justDone, out var finished))
            foreach (var o in finished.Opens) if (offer.Count < MaxOffered && !offer.Contains(o) && _byId.TryGetValue(o, out var oa) && Eligible(oa)) offer.Add(o);
        foreach (var a in _all)
            if (offer.Count < MaxOffered && a.Tier == "middle" && !offer.Contains(a.Id) && !excluded.Contains(a.Id) && Eligible(a)) offer.Add(a.Id);
        return offer;
    }

    /// <summary>The offer for a hero with no ambition, such as an heir (3.1): the middle tier its state allows, in content order, at most four.</summary>
    public List<string> OfferFor(IReadOnlyCollection<string> done, AmbitionFacts facts) => Offer("", done, null, facts);
}
