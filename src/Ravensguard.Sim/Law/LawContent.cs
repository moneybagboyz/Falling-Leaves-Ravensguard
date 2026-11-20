using Ravensguard.Content;

namespace Ravensguard.Sim.Law;

/// <summary>
/// The crimes and the numbers of the law as the sim reads them (crime-and-justice 3, 4): built from the content database, or the spec's defaults when a world runs without content. A kind that content
/// no longer has is still readable (a saved crime keeps its id string) and counts as a plain crime with no bounty.
/// </summary>
public sealed class LawContent
{
    public static readonly LawContent Default = new(Defaults(), new LawSettingsDef { Id = "law" });

    private readonly Dictionary<string, CrimeDef> _crimes;
    private readonly Dictionary<string, PunishmentDef> _punish;
    public LawSettingsDef S { get; }
    public IReadOnlyCollection<CrimeDef> Crimes => _crimes.Values;
    /// <summary>The punishment tables by key, in key order (P6-C1).</summary>
    public IReadOnlyCollection<PunishmentDef> Punishments => _punish.Values;

    public LawContent(IEnumerable<CrimeDef> crimes, LawSettingsDef settings, IEnumerable<PunishmentDef>? punishments = null)
    {
        _crimes = crimes.OrderBy(c => c.Id, StringComparer.Ordinal).ToDictionary(c => c.Id, StringComparer.Ordinal);
        _punish = (punishments ?? DefaultPunishments()).OrderBy(c => c.Id, StringComparer.Ordinal).ToDictionary(c => c.Id, StringComparer.Ordinal);
        S = settings;
    }

    public static LawContent From(ContentDatabase? db)
    {
        if (db == null) return Default;
        var pun = db.All<PunishmentDef>().ToList();
        return new LawContent(db.All<CrimeDef>(), db.All<LawSettingsDef>().OrderBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault() ?? new LawSettingsDef { Id = "law" }, pun.Count > 0 ? pun : null);
    }

    /// <summary>A punishment table by key, or null (a key content no longer has).</summary>
    public PunishmentDef? Punishment(string key) => _punish.GetValueOrDefault(key);

    private static PunishStepDef Step(string kind, long mites = 0, int valueX = 0, int days = 0, int daysDiv = 0, int lashes = 0, int display = 0, string unpaid = "gaol", int feud = 0) =>
        new() { Kind = kind, Mites = mites, ValueX = valueX, Days = days, DaysDiv = daysDiv, Lashes = lashes, Display = display, Unpaid = unpaid, Feud = feud };

    private static PunishmentDef P(string id, int sev, params PunishStepDef[] steps) => new() { Id = id, Severity = sev, Steps = [.. steps] };

    /// <summary>The spec's tables, for a world that runs without content (the same as <c>law/punishments.jsonc</c>).</summary>
    public static PunishmentDef[] DefaultPunishments() =>
    [
        P("fine", 1, Step("fine", 100, 2), Step("fine", 300, 4), Step("fine", 500, 6)), P("penance", 1, Step("penance", 50)), P("kin_pays", 1, Step("kin_pays", 100, 2)),
        P("restitution_sevenfold", 2, Step("restitution", 100, 7)), P("confiscation_and_fine", 2, Step("confiscation", 100, 2)), P("blood_price", 2, Step("blood_price", 3000)),
        P("gaol", 2, Step("gaol", days: 15, daysDiv: 100)), P("bond_labour", 2, Step("bond_labour", days: 3, daysDiv: 150)),
        P("weregild", 3, Step("weregild", 6000, days: 15)), P("blood_feud_or_price", 3, Step("blood_price", 4000, days: 15, feud: 60)),
        P("flogging", 3, Step("flogging", lashes: 20), Step("flogging", lashes: 40), Step("hand")), P("branding", 3, Step("branding")),
        P("fine_or_hand", 4, Step("fine", 100, 2), Step("fine", 300, 4), Step("hand")), P("stocks_then_hand", 4, Step("stocks", days: 1), Step("flogging", lashes: 20), Step("hand")),
        P("exile", 4, Step("exile", days: 3)), P("weregild_or_outlawry", 4, Step("weregild", 6000, days: 15, unpaid: "outlawry")),
        P("outlawry", 5, Step("outlawry")), P("hanging", 6, Step("hanging")), P("drawn_and_quartered", 6, Step("hanging", display: 15)),
    ];

    public CrimeDef Get(string kind) => _crimes.GetValueOrDefault(kind) ?? new CrimeDef { Id = kind, Bounty = false };

    /// <summary>The chronicle entry type an act of this kind writes.</summary>
    public string EntryOf(string kind) => Get(kind) is { Entry.Length: > 0 } d ? d.Entry : kind;

    private static CrimeDef[] Defaults() =>
    [
        new() { Id = "theft", Cry = true, BountyFlat = 100, BountyValueX = 2 },
        new() { Id = "burglary", Cry = true, BountyFlat = 100, BountyValueX = 2 },
        new() { Id = "assault", Cry = true, BountyFlat = 500 },
        new() { Id = "murder", Cry = true, BountyFlat = 3000, DeadPaysAll = true, Permanent = true },
        new() { Id = "arson", Cry = true, BountyFlat = 2000, BountyDamageDiv = 10 },
        new() { Id = "grave_robbing", Cry = true, Taboo = true, BountyFlat = 1000, BountyValueX = 1 },
    ];
}
