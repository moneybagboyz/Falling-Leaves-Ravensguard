namespace Ravensguard.Content;

/// <summary>
/// One kind of work contract (contracts-and-rumours-v2 3, <c>contracts/kinds.jsonc</c>): what posts it (<see cref="Need"/>, a scanner rule), who pays it (<see cref="Issuer"/>), the pay formula, the deadline in game
/// days, how completion is verified from what happened (<see cref="Verify"/>) and what failing costs (<see cref="Fail"/>). A kind whose need is "hook" is content only until the part that owns its need lands (the
/// validator still demands a verifier and a failure rule).
/// </summary>
[ContentType("contract_kind")]
public sealed record ContractKindDef : ContentDef
{
    /// <summary>escort, guard, hunt, deliver, collect, bounty, rescue, recover, clear, message, build, repair, political, criminal, faith, event.</summary>
    public required string Family { get; init; }
    /// <summary>The scanner rule that posts it (see <c>NeedScanner</c>): stock_short, caravan_leaving, crime_reported, band_seen, captive_held, debt_late, runaway, planner, hands_short, fair_charter, or "hook" for a need a later part supplies.</summary>
    public required string Need { get; init; }
    /// <summary>Who pays: household, business, fief, church, guild, band, caravan, person.</summary>
    public required string Issuer { get; init; }
    /// <summary>Flat pay, mites.</summary>
    public long PayBase { get; init; }
    /// <summary>Pay per unit of the kind's measure (<see cref="PayUnit"/>), mites x 100 (so a 0.15 rate is 15).</summary>
    public long PayPerUnitX100 { get; init; }
    /// <summary>The measure the rate multiplies: none, km_kg (kilometres x kilograms), guard_day (days x guards), day, head, mite_pct (percent of the value at risk).</summary>
    public string PayUnit { get; init; } = "none";
    public int DeadlineDays { get; init; } = 10;
    /// <summary>The event pattern that completes it: store_gain (the target store's mass rose), culprit_taken (held or dead by the taker's side), band_gone, captive_free, site_done, job_done, body_found, chronicle_event, bout_record, taker_reports (a hand-in of a carried thing), none_yet.</summary>
    public required string Verify { get; init; }
    /// <summary>What failing or expiring does besides returning the escrow: standing_only, bond, goods_lost, none.</summary>
    public required string Fail { get; init; }
    /// <summary>The taker posts a bond of this percent of the pay (escorts and guards: 10), charged on a failure.</summary>
    public int BondPct { get; init; }
    /// <summary>Standing with the issuer's group on success, permille at full knowledge (failure costs twice this).</summary>
    public int SuccessPm { get; init; } = 10;
    /// <summary>Posted by fences and band leaders, visible only to people they trust (>= 300).</summary>
    public bool Crime { get; init; }
    /// <summary>The pay may vary by up to this percent either way, drawn once at posting from stream 0x691 keyed on the contract (0: the formula's figure exactly).</summary>
    public int JitterPct { get; init; }
    /// <summary>The chronicle sentence's text key suffix for the "why" of the posting (en.csv <c>ct_why_*</c>).</summary>
    public string Why { get; init; } = "";
}

/// <summary>The numbers of contracts v2 (spec 4.1 to 4.4): scan slices, the NPC companies, the tournament and the construction market.</summary>
[ContentType("contract_settings")]
public sealed record ContractSettingsDef : ContentDef
{
    /// <summary>A store below this many days of an input posts a deliver contract.</summary>
    public int StockDaysMin { get; init; } = 3;
    /// <summary>No merchant stock of the good within this many metres.</summary>
    public int MerchantReachM { get; init; } = 8_000;
    /// <summary>Goods worth more than this leaving with a caravan earn a guard contract.</summary>
    public long CaravanGuardValueMites { get; init; } = 5_000;
    /// <summary>A band seen within this distance of a road, at most this many days ago, posts a clearing.</summary>
    public int BandRoadM { get; init; } = 3_000;
    public int BandSeenDays { get; init; } = 5;
    /// <summary>A monster or bandit rumour held by at least this percent of the place counts as believed.</summary>
    public int BelievedPct { get; init; } = 50;
    /// <summary>The hero's autopilot pauses on a contract worth at least this many days of his wage (AP-8).</summary>
    public int OfferBigWageDays { get; init; } = 5;
    /// <summary>Tournament: purse in crowns' mites (20 crowns), the entry fee, the days before the fair it is announced.</summary>
    public long TournamentPurseMites { get; init; } = 20_000;
    public long TournamentFeeMites { get; init; } = 1_000;
    public int TournamentAnnounceDays { get; init; } = 15;
    /// <summary>The closing angle of a joust's lance run, degrees off head-on, at most.</summary>
    public int JoustMaxAngleDeg { get; init; } = 15;
    /// <summary>Construction market: bids are materials + hours x crew wage x (1,000 + margin) / 1,000; the margin is Phase 5's.</summary>
    public int BidMarginPm { get; init; } = 1_450;
    /// <summary>Slander: chance a season (percent) a rival with Speech at least <see cref="SlanderSpeech"/> tells a lie.</summary>
    public int SlanderSeasonPct { get; init; } = 5;
    public int SlanderSpeech { get; init; } = 30;
    /// <summary>Defamation's fine (mites) and the author's standing loss multiple on exposure.</summary>
    public long DefamationFineMites { get; init; } = 300;
    public int ExposedAuthorMult { get; init; } = 2;
    /// <summary>A fact older than this many days weighs half (2 seasons).</summary>
    public int StaleDays { get; init; } = 2 * 30;
    /// <summary>The most open contracts a settlement's board holds.</summary>
    public int BoardCap { get; init; } = 12;
    /// <summary>Scanner: how many settlements a game hour looks at.</summary>
    public int ScanSettlementsPerHour { get; init; } = 1;
    /// <summary>A company takes a contract that scores at least this many mites an hour (the unskilled wage of a ten-hour day over ten).</summary>
    public long CompanyMinHourlyMites { get; init; } = 30;
}
