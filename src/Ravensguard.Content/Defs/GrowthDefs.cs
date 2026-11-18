namespace Ravensguard.Content;

/// <summary>One building material line of a cottage: <c>kg</c> of the good (plan 4A.2: an 8 x 5 m cottage needs about 40 poles, 1.5 t of daub and 3 t of thatch).</summary>
public sealed record GrowthMaterialDef
{
    public required string Good { get; init; }
    public int Kg { get; init; }
}

/// <summary>
/// The tunables of background growth (docs/specs/phase3/background-growth.md, plan 4A.2): when households and lords put up cottages on vacant parcels, clear new
/// fields from woodland, and open new businesses where demand goes unmet. Every number is content; defaults are the shipped values
/// (<c>content/base/economy/growth.jsonc</c>). Rates carry their unit suffix. A world without the file still grows, with these defaults.
/// </summary>
[ContentType("growth_tuning")]
public sealed record GrowthTuningDef : ContentDef
{
    // ---------- cottages ----------
    /// <summary>Worker-hours of an 8 x 5 m cottage (about 250), the poles, daub and thatch it takes, and the share of both a burgage house needs on top (percent).</summary>
    public int CottageWorkerHours { get; init; } = 250;
    public List<GrowthMaterialDef> CottageMaterials { get; init; } =
    [
        new() { Good = "logs", Kg = 400 }, new() { Good = "clay", Kg = 1500 }, new() { Good = "thatch", Kg = 3000 },
    ];
    public int BurgagePct { get; init; } = 160;
    /// <summary>Beds of a new dwelling by type (the generated ones run 4 to 7 for a cottage, 6 to 12 for a burgage house), and the share (percent) of the work and materials it takes to refit a vacant house instead of building one.</summary>
    public int CottageBeds { get; init; } = 5;
    public int LonghouseBeds { get; init; } = 8;
    public int BurgageBeds { get; init; } = 7;
    public int VacantRepairPct { get; init; } = 40;
    /// <summary>The share (percent) of the poles that are cut in the settlement's woodland (gathered from its stock, at 100 kg an hour); the rest is bought.</summary>
    public int PolesFromWoodlandPct { get; init; } = 70;
    public int GatherKgPerHour { get; init; } = 100;
    /// <summary>Hours a day each adult of the owner's household and each hired idle hand puts into the build, and how many hands a build employs at most.</summary>
    public int OwnHoursPerMemberDay { get; init; } = 2;
    public int HiredHoursPerHandDay { get; init; } = 4;
    public int CrewMax { get; init; } = 6;
    /// <summary>Seasons a build takes at least (one to two in practice: the crew's hours decide the rest).</summary>
    public int MinSeasons { get; init; } = 1;
    /// <summary>A builder keeps this many days of his household's wage for the crew in hand before he starts, and a build that cannot get its poles, daub and thatch for this many seasons is dropped (the plot is released).</summary>
    public int BuilderReserveDays { get; init; } = 20;
    public int GiveUpSeasons { get; init; } = 16;
    /// <summary>Households with more members than this percent of their beds count as crowded, and this many of them ask for one new cottage; each couple waiting for a home asks for one.</summary>
    public int CrowdedOverPct { get; init; } = 130;
    public int CrowdedPerCottage { get; init; } = 3;
    public int WaitingPerCottage { get; init; } = 1;
    /// <summary>Caps on land and pace: at most this many cottages a cluster of villages starts in a season, the region starts at most this many a year for every 10,000 people, and new dwellings never exceed this percent of the generated ones.</summary>
    public int MaxStartsPerClusterSeason { get; init; } = 2;
    public int MaxStartsPerYearPerTenThousand { get; init; } = 25;
    public int MaxNewDwellingsPctOfBase { get; init; } = 18;
    /// <summary>The plot of a new cottage in metres: width and depth in a village, and in a town (a burgage plot is long and narrow).</summary>
    public int PlotWidthVillageM { get; init; } = 16;
    public int PlotDepthVillageM { get; init; } = 38;
    public int PlotWidthTownM { get; init; } = 11;
    public int PlotDepthTownM { get; init; } = 28;
    /// <summary>A plot lies within this many metres of a settlement's farthest lot (its plan bounds).</summary>
    public int PlanMarginM { get; init; } = 30;

    // ---------- assarting ----------
    /// <summary>Households clear new fields while a settlement has fewer arable acres than this a person (tenths: 23 is 2.3, plan 3a).</summary>
    public int TargetAcresPerPersonX10 { get; init; } = 25;
    public int AssartHoursPerAcre { get; init; } = 90;
    public int AssartTilesPerProject { get; init; } = 8;
    public int AssartMaxRadiusM { get; init; } = 2_600;
    public int AssartMinFertility { get; init; } = 35;
    public int AssartMaxSlopePermille { get; init; } = 100;
    public int AssartCrewMax { get; init; } = 12;
    public int AssartMaxStartsPerSeason { get; init; } = 5;
    /// <summary>Share (percent) of the cleared wood that goes to market as firewood; the rest is logs (poles and planks).</summary>
    public int AssartFirewoodPct { get; init; } = 60;
    /// <summary>Worker-hours a day a cleared acre's crew puts in (hired idle hands).</summary>
    public int AssartHoursPerHandDay { get; init; } = 6;

    // ---------- new businesses ----------
    /// <summary>A good is wanted when its book's price is over this many permille of the reference and its stock covers under a day of demand, for this many seasons in a row.</summary>
    public int UnmetPricePermille { get; init; } = 1150;
    public int UnmetSeasons { get; init; } = 3;
    /// <summary>The owner must hold this much coin (mites) in his household, puts half of it in as capital, and the fief's treasury advances up to this much more when his coin falls short.</summary>
    public long BusinessMinOwnerMites { get; init; } = 6_000;
    public long BusinessTreasuryAdvanceMites { get; init; } = 3_000;
    /// <summary>The most new businesses opened in a season, and a settlement keeps at most one open business of a kind for every this many of its people (a stall for every <c>PeoplePerStall</c> instead).</summary>
    public int BusinessMaxPerSeason { get; init; } = 3;
    public int PeoplePerBusiness { get; init; } = 250;
    /// <summary>Wages (mites) a new smithy or tavern's plot takes to clear and fence, paid to idle hands.</summary>
    public long PremisesMites { get; init; } = 2_500;

    // ---------- immigration (decision G9) ----------
    /// <summary>Free dwellings the region's own couples do not need draw settlers, a this-th of them a season, while idle adults are under this permille of the people; at most this permille of the people in a season.</summary>
    public int ImmigrationSurplusDivisor { get; init; } = 4;
    public int ImmigrationMaxIdlePermille { get; init; } = 30;
    public int ImmigrationMaxPermilleOfAlive { get; init; } = 3;
    // ---------- rural to town (decision G12) ----------
    /// <summary>Landless peasant households a town with free houses and unfilled posts takes from the villages in a season.</summary>
    public int TownMovesPerTownSeason { get; init; } = 2;
}
