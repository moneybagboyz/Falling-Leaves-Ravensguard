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

    // ---- heresy (4.5, P6-E2)
    /// <summary>A heresy starts where a priest of Theology and piety at least these has a grievance with his bishop or hunger has struck: P = HeresyStartPm permille a season (stream 0x652).</summary>
    public int HeresyTheology { get; init; } = 50;
    public int HeresyPiety { get; init; } = 80;
    public int HeresyStartPm { get; init; } = 20;
    /// <summary>The share of a settlement's households in hunger that counts as a famine (percent).</summary>
    public int FamineSharePct { get; init; } = 25;
    /// <summary>A hearing converts with P = HeresyBasePm + HeresyPerGrievancePm x grievances - piety / HeresyPietyDivisor, in permille; each adherent is heard by this many a day.</summary>
    public int HeresyBasePm { get; init; } = 100;
    public int HeresyPerGrievancePm { get; init; } = 20;
    public int HeresyPietyDivisor { get; init; } = 5;
    public int HearingsPerAdherent { get; init; } = 1;
    /// <summary>A bishop preaching against a heresy takes this percent of its adherents back a season.</summary>
    public int PreachCutPct { get; init; } = 20;
    /// <summary>A bishop whose piety reaches this tries the preacher; one whose piety is under <see cref="BishopLaxPiety"/> lets it be.</summary>
    public int BishopTryPiety { get; init; } = 70;
    public int BishopLaxPiety { get; init; } = 40;
    /// <summary>A heresy with this percent of a settlement's adults draws an interdict from a bishop of piety at least <see cref="BishopTryPiety"/> - 10; each household then bears this many grievances.</summary>
    public int InterdictSharePct { get; init; } = 30;
    public int InterdictGrievances { get; init; } = 2;
    public int InterdictGrievanceSeasons { get; init; } = 1;
    // ---- excommunication (4.6)
    /// <summary>A ruler asks for a rival's excommunication with the bishop's opinion at least this, at a gift of this much (100 crowns).</summary>
    public int ExcommunicationOpinion { get; init; } = 50;
    public long ExcommunicationGiftMites { get; init; } = 10000;
    /// <summary>His vassals' fealty utility drops this much; his fiefs bear this many grievances for this many seasons (renewed while it lasts).</summary>
    public int ExcommunicationFealty { get; init; } = 40;
    public int ExcommunicationGrievances { get; init; } = 2;
    public int ExcommunicationGrievanceSeasons { get; init; } = 4;
    /// <summary>The chronicle entry types a request may name as its cause.</summary>
    public List<string> ExcommunicationCauses { get; init; } = ["oath_broken", "sacrilege", "heresy", "arson", "grave_robbing", "burglary"];
    /// <summary>Penance: this percent of the sinner's treasury (or household coin) to the see, and a pilgrimage made since.</summary>
    public int PenancePct { get; init; } = 10;
    /// <summary>A new bishop lifts the ban of the one before with this percent chance (stream 0x652).</summary>
    public int GracePct { get; init; } = 30;
    // ---- relics (4.4)
    public int SacrilegeStandingPm { get; init; } = -300;
    public int VeneratePiety { get; init; } = 2;
    /// <summary>A church buys a relic of the hero's at this much (a fake too, until it is exposed).</summary>
    public long RelicPriceMites { get; init; } = 5000;
    /// <summary>A pardoner's fake is spotted with Wits at least this or Theology at least the second number.</summary>
    public int FakeWits { get; init; } = 60;
    public int FakeTheology { get; init; } = 30;
    /// <summary>A fake that a church bought is exposed each season with this chance in percent (stream 0x656).</summary>
    public int FakeExposePct { get; init; } = 25;
    public int FakeExposedPiety { get; init; } = 10;
    // ---- superstition (4.7)
    public int LairPoints { get; init; } = 30;
    public int LairKm { get; init; } = 10;
    public int BarrowPoints { get; init; } = 15;
    public int RumourPoints { get; init; } = 10;
    public int RumourSharePct { get; init; } = 50;
    public int SuperstitionFall { get; init; } = 5;
    /// <summary>At this level villagers avoid the night roads and the barrow and buy charms; at <see cref="WitchHuntAt"/> a witch hunt may start (P = WitchHuntPm permille a season, stream 0x654).</summary>
    public int SuperstitionNight { get; init; } = 40;
    public int WitchHuntAt { get; init; } = 70;
    public int WitchHuntPm { get; init; } = 100;
    public int HeadShownDrop { get; init; } = 40;
    public long CharmMites { get; init; } = 20;
    // ---- believed miracles (overview 10a, stream 0x655)
    /// <summary>After a pilgrim's recovery or a fire that stopped at a church, the folk call it a miracle with this chance in percent.</summary>
    public int MiraclePct { get; init; } = 15;
    public int MiraclePiety { get; init; } = 2;
    /// <summary>A believed miracle is disputed (sceptics, rival clergy) with this chance in percent (stream 0x655 draw 3).</summary>
    public int MiracleDisputePct { get; init; } = 30;
}
