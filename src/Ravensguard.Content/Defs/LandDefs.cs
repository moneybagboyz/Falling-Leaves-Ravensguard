namespace Ravensguard.Content;

/// <summary>
/// The rules of land (docs/specs/phase5/land-and-deeds.md 3.3, P5-A2): plot prices and the demand factor, dues, the lot market's listings, market stalls, claims and their licence,
/// field leases and the arrears ladder, storage volume of a deed's rooms, and founding charters (spec 4a). Every number is content; the defaults are the shipped values
/// (<c>content/base/economy/land.jsonc</c>). Rates carry their unit suffix; money is in mites (100 to the crown).
/// </summary>
[ContentType("land_rules")]
public sealed record LandRulesDef : ContentDef
{
    // ---------- plot prices (spec 4.2) ----------
    /// <summary>Price of a plot by the square metre: in a town, a village and a hamlet (an 11 x 28 m town plot at 150 is 46,200 mites, 462 crowns).</summary>
    public int PlotPriceTownMitesPerM2 { get; init; } = 150;
    public int PlotPriceVillageMitesPerM2 { get; init; } = 40;
    public int PlotPriceHamletMitesPerM2 { get; init; } = 25;
    /// <summary>Demand factor in permille: 1000 plus this many permille for every crowded household in a hundred, clamped to the bounds.</summary>
    public int DemandPmPerCrowdedPct { get; init; } = 5;
    public int DemandMinPm { get; init; } = 800;
    public int DemandMaxPm { get; init; } = 1500;
    /// <summary>A household with more members than this percent of its beds counts as crowded (growth's measure).</summary>
    public int CrowdedOverPct { get; init; } = 130;
    /// <summary>A standing house is worth this permille of what it would cost to build: its materials at the book's price and its worker-hours at the unskilled wage.</summary>
    public int BuildingValuePmOfCost { get; init; } = 800;

    // ---------- dues ----------
    /// <summary>Burgage rent of a town plot to the lord, mites a season; quit-rent of a freehold or a customary holding, mites an acre a season (at least one mite).</summary>
    public int BurgageRentMitesPerSeason { get; init; } = 300;
    public int QuitRentMitesPerAcreSeason { get; init; } = 25;

    // ---------- the lot market ----------
    /// <summary>Plots the lord lists at once in a town and in a village (hamlets list none).</summary>
    public int ListingsTown { get; init; } = 3;
    public int ListingsVillage { get; init; } = 1;
    /// <summary>An NPC buyer of a deed the hero offers pays at least this permille of its price (spec 4.2 resale).</summary>
    public int ResaleBuyerPm { get; init; } = 900;
    /// <summary>A buying household keeps this many days of an unskilled wage in hand after paying.</summary>
    public int ResaleReserveDays { get; init; } = 20;

    // ---------- market stalls (spec 4.3) ----------
    /// <summary>Stalls in the square of a town and of a village; the season's rent and a fair day's, mites (paid to the town's treasury).</summary>
    public int StallsTown { get; init; } = 6;
    public int StallsVillage { get; init; } = 2;
    public int StallRentMitesPerSeason { get; init; } = 1500;
    public int StallFairDayMites { get; init; } = 150;

    // ---------- leases (spec 4.3) ----------
    /// <summary>A lease runs this many days (a year) and is renewed when it is not in arrears; a field's rent in mites a year (paid by the season, a quarter each).</summary>
    public int LeaseTermDays { get; init; } = 60;
    public int LeaseRentMitesPerFieldYear { get; init; } = 320;
    /// <summary>Seasons of arrears before the sum owed becomes a debt, and before the lessor gives notice; days of notice before the lease ends.</summary>
    public int ArrearsDebtSeasons { get; init; } = 3;
    public int ArrearsEvictSeasons { get; init; } = 6;
    public int EvictionNoticeDays { get; init; } = 15;
    /// <summary>Rent of a leased house a season by dwelling type, mites (the tax step's dwelling rents).</summary>
    public int HouseRentCottageMitesPerSeason { get; init; } = 100;
    public int HouseRentLonghouseMitesPerSeason { get; init; } = 150;
    public int HouseRentBurgageMitesPerSeason { get; init; } = 400;

    // ---------- claims (spec 4.4; overview 10a) ----------
    /// <summary>Tiles (an acre each) a claim covers when staked, at most, and gains with every improvement; the days it has to make its first improvement.</summary>
    public int ClaimStartTiles { get; init; } = 8;
    public int ClaimMaxTiles { get; init; } = 40;
    public int ClaimTilesPerImprovement { get; init; } = 8;
    public int ClaimImproveWithinDays { get; init; } = 60;
    /// <summary>Land outside every fief is free to claim at least this many metres from any settlement's plan.</summary>
    public int ClaimMinMFromPlan { get; init; } = 1500;
    /// <summary>The licence a lord sells for a claim on his waste, mites an acre (10 crowns): a licensed claim is protected; one without is squatting.</summary>
    public int ClaimLicenceMitesPerAcre { get; init; } = 1000;
    /// <summary>The item a claimant must carry to stake (empty: none needed until crafting-full adds the banner recipe).</summary>
    public string ClaimBannerItem { get; init; } = "";
    /// <summary>A claim's tile counts as level below this slope and as dry from this drainage; level dry tiles are taken first.</summary>
    public int ClaimLevelSlopePermille { get; init; } = 100;
    public int ClaimDryDrainage { get; init; } = 40;

    // ---------- storage (spec 4.5) ----------
    /// <summary>A storage room holds its floor area times this height (cm) times this fill (permille); the floor bears this many kilograms a square metre on the ground and on an upper floor.</summary>
    public int StoreHeightCm { get; init; } = 200;
    public int StoreFillPm { get; init; } = 500;
    public int StoreGroundKgPerM2 { get; init; } = 2000;
    public int StoreUpperKgPerM2 { get; init; } = 400;

    // ---------- founding charters (spec 4a) ----------
    /// <summary>Tiles of a charter's site; the fee paid at the grant, the dues to the grantor a year and the service owed (men for days a year), mites.</summary>
    public int CharterSiteTiles { get; init; } = 40;
    public int CharterFeeMites { get; init; } = 5000;
    public int CharterDuesMitesPerYear { get; init; } = 2000;
    public int CharterServiceMen { get; init; } = 2;
    public int CharterServiceDays { get; init; } = 20;
    /// <summary>Households that must be settled, and the years they have for it.</summary>
    public int CharterMinHouseholds { get; init; } = 5;
    public int CharterSettleYears { get; init; } = 2;
    /// <summary>The petitioner's standing with the grantor must be at least this (Phase 5's simple rule).</summary>
    public int CharterMinStanding { get; init; } = 0;
    /// <summary>A site is contested within this many metres of an active war.</summary>
    public int CharterWarM { get; init; } = 10_000;
    /// <summary>A charter grows into a village at this many people with a chapel or a mill on its site (the existing growth rule).</summary>
    public int CharterVillagePeople { get; init; } = 80;
}
