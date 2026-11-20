using Ravensguard.Content;

namespace Ravensguard.Sim.Contracts;

/// <summary>
/// The contract kinds and numbers as the sim reads them (spec 3), built from the content database or the small default set a world without content uses. <see cref="Validate"/> is the CR-7 validator:
/// every kind has a need, a verifier and a failure rule, and every name is one the code knows.
/// </summary>
public sealed class ContractContent
{
    public static readonly string[] Families = ["escort", "guard", "hunt", "deliver", "collect", "bounty", "rescue", "recover", "clear", "message", "build", "repair", "labour", "political", "criminal", "faith", "event"];
    /// <summary>The needs the scanner posts today; "hook" kinds wait for the part that owns their need (rule, slavery, heists, faith).</summary>
    public static readonly string[] BuiltNeeds = ["stock_short", "caravan_leaving", "crime_reported", "band_seen", "captive_held", "fair_charter", "planner", "hands_short"];
    public static readonly string[] Needs = [.. BuiltNeeds, "debt_late", "runaway", "relic_stolen", "person_missing", "monster_rumour", "buyer_wants", "lord_war", "hook"];
    public static readonly string[] Issuers = ["household", "business", "fief", "church", "guild", "band", "caravan", "person"];
    public static readonly string[] Verifiers = ["store_gain", "culprit_taken", "band_gone", "captive_free", "site_done", "job_done", "body_found", "chronicle_event", "bout_record", "taker_reports", "none_yet"];
    public static readonly string[] Fails = ["standing_only", "bond", "goods_lost", "none"];
    public static readonly string[] Units = ["none", "km_kg", "guard_day", "day", "head", "mite_pct"];

    private readonly Dictionary<string, ContractKindDef> _kinds;
    public ContractSettingsDef S { get; }
    public IReadOnlyCollection<ContractKindDef> Kinds => _kinds.Values;

    public ContractContent(IEnumerable<ContractKindDef> kinds, ContractSettingsDef settings)
    {
        _kinds = kinds.OrderBy(k => k.Id, StringComparer.Ordinal).ToDictionary(k => k.Id, StringComparer.Ordinal);
        S = settings;
    }

    public static ContractContent From(ContentDatabase? db)
    {
        if (db == null) return Default;
        var kinds = db.All<ContractKindDef>().ToList();
        return new ContractContent(kinds.Count > 0 ? kinds : Defaults(), db.All<ContractSettingsDef>().OrderBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault() ?? new ContractSettingsDef { Id = "contracts" });
    }

    public static readonly ContractContent Default = new(Defaults(), new ContractSettingsDef { Id = "contracts" });

    /// <summary>A kind by id, or null (content that dropped a kind an old save still holds: the contract keeps its id string and closes normally).</summary>
    public ContractKindDef? Kind(string id) => _kinds.GetValueOrDefault(id);

    /// <summary>The kinds a scanner need posts, in id order.</summary>
    public IEnumerable<ContractKindDef> ForNeed(string need) => _kinds.Values.Where(k => k.Need == need);

    private static ContractKindDef[] Defaults() =>
    [
        new() { Id = "deliver_goods", Family = "deliver", Need = "stock_short", Issuer = "household", PayBase = 300, PayPerUnitX100 = 15, PayUnit = "km_kg", DeadlineDays = 6, Verify = "store_gain", Fail = "standing_only", SuccessPm = 15, Why = "stock_short" },
        new() { Id = "guard_caravan", Family = "guard", Need = "caravan_leaving", Issuer = "caravan", PayPerUnitX100 = 45_000, PayUnit = "guard_day", DeadlineDays = 8, Verify = "job_done", Fail = "bond", BondPct = 10, SuccessPm = 20, Why = "caravan_leaving" },
        new() { Id = "clear_bandit_cave", Family = "clear", Need = "band_seen", Issuer = "fief", PayBase = 6_000, DeadlineDays = 14, Verify = "band_gone", Fail = "standing_only", SuccessPm = 60, Why = "band_seen" },
        new() { Id = "rescue_captive", Family = "rescue", Need = "captive_held", Issuer = "household", PayBase = 3_000, DeadlineDays = 10, Verify = "captive_free", Fail = "standing_only", SuccessPm = 80, Why = "captive_held" },
    ];

    /// <summary>The CR-7 validator: the problems with the kinds, empty when sound.</summary>
    public List<string> Validate(Func<string, bool>? knownKeys = null)
    {
        var errs = new List<string>();
        foreach (var k in _kinds.Values.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            string who = $"contract_kind '{k.Id}'";
            if (!Families.Contains(k.Family)) errs.Add($"{who}: unknown family '{k.Family}'");
            if (string.IsNullOrEmpty(k.Need) || !Needs.Contains(k.Need)) errs.Add($"{who}: missing or unknown need '{k.Need}'");
            if (string.IsNullOrEmpty(k.Verify) || !Verifiers.Contains(k.Verify)) errs.Add($"{who}: missing or unknown verifier '{k.Verify}'");
            if (string.IsNullOrEmpty(k.Fail) || !Fails.Contains(k.Fail)) errs.Add($"{who}: missing or unknown failure rule '{k.Fail}'");
            if (!Issuers.Contains(k.Issuer)) errs.Add($"{who}: unknown issuer '{k.Issuer}'");
            if (!Units.Contains(k.PayUnit)) errs.Add($"{who}: unknown pay unit '{k.PayUnit}'");
            if (k.DeadlineDays <= 0) errs.Add($"{who}: no deadline");
            if (k.Fail == "bond" && k.BondPct <= 0) errs.Add($"{who}: fail 'bond' needs bond_pct");
            if (k.PayBase <= 0 && k.PayPerUnitX100 <= 0) errs.Add($"{who}: no pay");
            if (k.Crime && k.Family != "criminal") errs.Add($"{who}: crime kinds are of the criminal family");
            if (knownKeys != null && !knownKeys("ct_kind_" + k.Id)) errs.Add($"{who}: key 'ct_kind_{k.Id}' is not in en.csv");
        }
        return errs;
    }
}
