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
    public LawSettingsDef S { get; }
    public IReadOnlyCollection<CrimeDef> Crimes => _crimes.Values;

    public LawContent(IEnumerable<CrimeDef> crimes, LawSettingsDef settings)
    {
        _crimes = crimes.OrderBy(c => c.Id, StringComparer.Ordinal).ToDictionary(c => c.Id, StringComparer.Ordinal);
        S = settings;
    }

    public static LawContent From(ContentDatabase? db) =>
        db == null ? Default : new LawContent(db.All<CrimeDef>(), db.All<LawSettingsDef>().OrderBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault() ?? new LawSettingsDef { Id = "law" });

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
