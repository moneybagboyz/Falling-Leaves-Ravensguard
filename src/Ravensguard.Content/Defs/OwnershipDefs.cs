namespace Ravensguard.Content;

/// <summary>
/// The rules of owned businesses, shares, stewards, day labour and stalls (docs/specs/phase5/businesses-stewards-ledger.md, P5-F). Every number is content; the defaults are the shipped
/// values (<c>content/base/production/ownership.jsonc</c>). Money in mites (100 to the crown), shares in permille.
/// </summary>
[ContentType("ownership_rules")]
public sealed record OwnershipRulesDef : ContentDef
{
    // ---------- valuation and shares (spec 3.2, 4.1) ----------
    /// <summary>Goodwill is this many seasons of the mean profit of the last four (a loss-making business has none).</summary>
    public int GoodwillSeasons { get; init; } = 2;
    /// <summary>The most of his business an owner sells (he keeps control above 500).</summary>
    public int ShareCapPm { get; init; } = 490;
    /// <summary>A closed business is offered for sale at this permille of its value.</summary>
    public int ClosedSalePm { get; init; } = 700;
    /// <summary>An owner sells when he is older than this with no heir in the trade, or his business lost money this many seasons running.</summary>
    public int SellOwnerAge { get; init; } = 60;
    public int SellLossSeasons { get; init; } = 2;

    // ---------- stewards (spec 4.4) ----------
    public int StewardMinTrading { get; init; } = 30;
    public int StewardMinLeadership { get; init; } = 20;
    public int StewardWageMitesPerDay { get; init; } = 350;
    /// <summary>A steward runs <c>base + Leadership / per</c> properties, at most <c>max</c>.</summary>
    public int StewardPropertiesBase { get; init; } = 3;
    public int StewardPropertiesPerLeadership { get; init; } = 20;
    public int StewardPropertiesMax { get; init; } = 6;
    /// <summary>The price lag: <c>max(0, lag_max - Trading / lag_per)</c> days.</summary>
    public int StewardLagMaxDays { get; init; } = 10;
    public int StewardLagPerTrading { get; init; } = 10;
    /// <summary>The reserve the steward keeps: <c>reserve_days x (base - per x Leadership) / 1000</c> days of costs.</summary>
    public int ReserveDays { get; init; } = 15;
    public int ReserveBasePm { get; init; } = 1500;
    public int ReservePerLeadershipPm { get; init; } = 5;
    /// <summary>Directives' defaults: send profit every n days, keep this much in the box.</summary>
    public int SendEveryDays { get; init; } = 5;
    public int KeepMites { get; init; } = 1000;

    // ---------- day labour (spec 4.3) ----------
    /// <summary>The hiring hour in the square, the day's wage and hours, the ages taken.</summary>
    public int DayLabourHour { get; init; } = 6;
    public int DayLabourWageMites { get; init; } = 150;
    public int DayLabourHours { get; init; } = 10;
    public int DayLabourMinAge { get; init; } = 16;
    public int DayLabourMaxAge { get; init; } = 59;
    /// <summary>A labourer with no day's work for this many days is idle again (no occupation).</summary>
    public int DayLabourLapseDays { get; init; } = 30;
    /// <summary>Day-labour days in a season that make a worker a candidate for a staff post first.</summary>
    public int DayLabourPriorityDays { get; init; } = 10;
    /// <summary>
    /// The lord's works (demesne, ditches, roads, carting): each day a fief spends up to this permille of the coin its treasury and its lord's household hold above the reserve on day labour
    /// for the idle of its settlements (decided here: the lords' idle coin is the plan's "haulage and farms short of hands" in the abstract).
    /// </summary>
    public int LordWorksPmPerDay { get; init; } = 8;
    /// <summary>Coin a lord's household keeps back before it pays for works.</summary>
    public int LordWorksReserveMites { get; init; } = 20000;

    // ---------- stalls and shops (spec 4.5) ----------
    /// <summary>The share of a book's daily demand of a good one minded stall meets in a market day, permille; and on a fair day (footfall x 1.5).</summary>
    public int StallDemandPm { get; init; } = 1000;
    public int StallFairDemandPm { get; init; } = 1500;
    /// <summary>Stalls of a settlement share the book's demand of a good: a stall sells at most demand x stall_demand / stalls per day.</summary>
    public int StallMarketHours { get; init; } = 8;
    /// <summary>The passers-by a minded stall draws whatever the book's demand for its goods: each household of the place spends up to this much a market day at it (mites; decided here, P5-F).</summary>
    public int StallFootfallMitesPerHousehold { get; init; } = 1;

    // ---------- the Ledger (spec 6) ----------
    /// <summary>A steward's report in the Journal every this many days.</summary>
    public int ReportEveryDays { get; init; } = 7;
}

/// <summary>
/// Prospecting, mineral rights and extraction businesses (docs/specs/phase5/prospecting-and-extraction.md, P5-F): the methods' times and spreads, the walk-by and hint radii, the licence fees
/// and royalties, and which business kind works which resource. Defaults are the shipped values (<c>content/base/production/prospecting.jsonc</c>).
/// </summary>
[ContentType("prospect_rules")]
public sealed record ProspectRulesDef : ContentDef
{
    /// <summary>Confidence a walk-by gives (surface resources only), permille.</summary>
    public int WalkConfidencePm { get; init; } = 200;
    /// <summary>Read the land: skill needed, hours per tile, the radius (m) in which a hidden ore body is hinted at by direction.</summary>
    public int ReadMinSkill { get; init; } = 20;
    public int ReadHours { get; init; } = 1;
    public int ReadHintM { get; init; } = 200;
    public int ReadConfidencePm { get; init; } = 350;
    /// <summary>Pan a stream: hours; how far upstream (tiles along the flow) the catchment is read.</summary>
    public int PanHours { get; init; } = 2;
    public int PanUpstreamTiles { get; init; } = 40;
    /// <summary>A test pit: hours, depth (cm), confidence.</summary>
    public int PitHours { get; init; } = 6;
    public int PitConfidencePm { get; init; } = 700;
    /// <summary>A trial shaft or bore: hours (two days), confidence; the tonnage error spread at skill 50 and at skill 90 (permille of the truth, linear between).</summary>
    public int ShaftHours { get; init; } = 48;
    public int ShaftConfidencePm { get; init; } = 850;
    public int ErrorAt50Pm { get; init; } = 300;
    public int ErrorAt90Pm { get; init; } = 100;
    /// <summary>The spread of a walk-by and a pit at skill 0 (permille), falling linearly to <see cref="ErrorAt90Pm"/> at 90.</summary>
    public int ErrorAt0Pm { get; init; } = 600;
    /// <summary>A hired prospector's fee per day of work, mites; his skill.</summary>
    public int HireMitesPerDay { get; init; } = 350;
    public int HireSkill { get; init; } = 55;
    /// <summary>The survey report item's shape and material.</summary>
    public string ReportShape { get; init; } = "survey_report";
    public string ReportMaterial { get; init; } = "leather";
    public int ReportMassG { get; init; } = 30;

    // ---------- rights (spec 5) ----------
    /// <summary>A mining licence's fee to the right holder, mites (base metals of the lord, precious of the crown); the royalty on the value of what is mined, permille.</summary>
    public int LicenceFeeBaseMites { get; init; } = 2000;
    public int LicenceFeePreciousMites { get; init; } = 5000;
    public int RoyaltyPm { get; init; } = 100;
    /// <summary>Ore bodies of a mine the hero opens: at most this share of a vein's tonnage per mine, permille.</summary>
    public int MineShareOfVeinPm { get; init; } = 1000;
    /// <summary>A new working's hazard rolls at the season (stream 0x5C2): flood and cave-in permille per level (Phase 4 sites' rules), the levels a new adit has.</summary>
    public int NewMineLevels { get; init; } = 1;
    public int NewMineFloodPm { get; init; } = 30;
    public int NewMineCaveInPm { get; init; } = 5;

    /// <summary>The extraction businesses and the resource each must stand on (spec 6).</summary>
    public List<ExtractionSiteDef> Sites { get; init; } = [];
}

/// <summary>One extraction business: its business kind, the resource (a <c>ResourceKind</c> name, or <c>Mine</c> for an ore body), whether a survey must have found it and whether it needs a licence.</summary>
public sealed record ExtractionSiteDef
{
    public string Kind { get; init; } = "";
    public string Resource { get; init; } = "";
    public bool NeedsSurvey { get; init; }
    public bool NeedsLicence { get; init; }
    public string Name { get; init; } = "";
}
