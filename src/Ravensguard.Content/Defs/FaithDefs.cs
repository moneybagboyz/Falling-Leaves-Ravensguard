namespace Ravensguard.Content;

/// <summary>
/// The template of a religion (faith-and-church 3, <c>faith/religion_templates.jsonc</c>): one per base culture, or one shared by several cultures (<see cref="Cultures"/>). The religion itself is derived from
/// the template, the cultures' feast days, the history's saints and the world seed (never saved). The words are content: a deity's terms, the names the founders are drawn from, the tone of the holy site.
/// </summary>
[ContentType("religion_template")]
public sealed record ReligionTemplateDef : ContentDef
{
    public required string Name { get; init; }
    /// <summary>The culture ids (<c>content/base/cultures</c>) whose people hold this faith.</summary>
    public required List<string> Cultures { get; init; }
    /// <summary>How the faith names its god or gods ("the Allfather", "the Light").</summary>
    public List<string> DeityTerms { get; init; } = [];
    /// <summary>Given names and epithets the 3 to 5 founders are drawn from at world creation (stream 0x651).</summary>
    public List<string> FounderGiven { get; init; } = [];
    public List<string> FounderEpithets { get; init; } = [];
    /// <summary>What the holy site is called (shrine, minster, holy well, drowned chapel).</summary>
    public string SiteTerm { get; init; } = "shrine";
    /// <summary>What its head is called (the patriarch, the High Priest).</summary>
    public string HeadTitle { get; init; } = "the high priest";
    /// <summary>What a bishop is called ("bishop", "godi", "abbot-bishop").</summary>
    public string SeeTitle { get; init; } = "bishop";
}

/// <summary>The doctrines of one religion (<c>faith/doctrines.jsonc</c>), by the template's id.</summary>
[ContentType("doctrine")]
public sealed record DoctrineDef : ContentDef
{
    /// <summary>The template id this row is for (empty: the default for a religion with no row).</summary>
    public string Religion { get; init; } = "";
    /// <summary><c>forbid</c> (no believer owns a slave), <c>coreligionists_forbidden</c>, or <c>accept</c>.</summary>
    public string SlaveryStance { get; init; } = "accept";
    /// <summary><c>forbid</c> (a believer may not lend at interest) or <c>allow</c>.</summary>
    public string Usury { get; init; } = "allow";
    /// <summary>No meat on one day in five: household demand shifts to fish and pulses.</summary>
    public bool Fasting { get; init; }
    /// <summary>0..100: the standing cost of robbing the dead.</summary>
    public int BarrowTaboo { get; init; } = 50;
    /// <summary>Benefit of clergy: a priest is tried by the church.</summary>
    public bool ChurchCourts { get; init; } = true;
    public bool ClergyMarry { get; init; }
    /// <summary>How the season's tithe receipts are split, in parts: the parish keeps its parts, the see gets its parts, the fabric and the poor take theirs (1 : 1 : 1 by default, the spec's thirds).</summary>
    public int TitheParishParts { get; init; } = 1;
    public int TitheSeeParts { get; init; } = 1;
    public int TitheFabricParts { get; init; } = 1;
}

/// <summary>The numbers of faith and church (faith-and-church 4, <c>faith/settings.jsonc</c>): piety, services, the tithe, the see, pilgrimage.</summary>
[ContentType("faith_settings")]
public sealed record FaithSettingsDef : ContentDef
{
    // ---- piety (4.3)
    /// <summary>A person's default piety is <c>PietyBase + hash % PietySpread</c> (40..70); a priest or monk adds <see cref="ClergyPietyBonus"/>.</summary>
    public int PietyBase { get; init; } = 40;
    public int PietySpread { get; init; } = 31;
    public int ClergyPietyBonus { get; init; } = 20;
    /// <summary>Piety per 100 mites of alms given, and the most one gift gives.</summary>
    public int AlmsPietyPer100 { get; init; } = 1;
    public int AlmsPietyMax { get; init; } = 10;
    // ---- holy days (4.1)
    public int MaxHolyDays { get; init; } = 12;
    /// <summary>The extra relish and ale a household eats on a holy day, percent.</summary>
    public int HolyFoodPct { get; init; } = 50;
    /// <summary>A person attends the service with P = AttendBasePm + AttendPerPietyPm x piety (permille).</summary>
    public int AttendBasePm { get; init; } = 600;
    public int AttendPerPietyPm { get; init; } = 5;
    public int ServicePiety { get; init; } = 1;
    // ---- the tithe and the see (4.2)
    /// <summary>The share of a see's receipts a season that its bishop draws as his living, percent.</summary>
    public int BishopStipendPct { get; init; } = 25;
    /// <summary>A vacant see is filled 1 to 2 seasons later: this many days at least and at most.</summary>
    public int ElectionMinDays { get; init; } = 15;
    public int ElectionMaxDays { get; init; } = 30;
    // ---- pilgrimage (4.4)
    public int PilgrimagePiety { get; init; } = 10;
    public int PilgrimageStandingPm { get; init; } = 80;
    public int PilgrimageRenown { get; init; } = 5;
    /// <summary>The sick who reach a shrine feel better: pain this much lower for this many days (no cure).</summary>
    public int ShrinePainRelief { get; init; } = 10;
    public int ShrinePainDays { get; init; } = 5;
    /// <summary>The site item the hero is given on arrival (a <c>site_item</c> id).</summary>
    public string BadgeItem { get; init; } = "pilgrim_badge";
    // ---- founders (3)
    public int FoundersMin { get; init; } = 3;
    public int FoundersMax { get; init; } = 5;
    // ---- doctrines in play
    /// <summary>Owning a slave against the doctrine costs this much piety a season (4.3).</summary>
    public int SlaveSeasonPiety { get; init; } = 10;
}
