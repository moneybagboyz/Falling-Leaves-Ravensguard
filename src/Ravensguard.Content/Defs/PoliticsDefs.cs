namespace Ravensguard.Content;

/// <summary>
/// The numbers of diplomacy and the AI lords (diplomacy-and-ai-lords 3, 4; <c>politics/settings.jsonc</c>): the war utility's terms and thresholds, peace by war score and weariness, treaty terms, the road to lord and the lords' planning.
/// The defaults are the spec's. Integer only; money in mites (1 crown = 100 mites).
/// </summary>
[ContentType("politics_settings")]
public sealed record PoliticsSettingsDef : ContentDef
{
    // ---- war utility (4.1)
    public int ClaimPts { get; init; } = 30;
    /// <summary>The casus belli weights by kind: insult, unpaid dues, a broken treaty, a seized fief or a raid, excommunication.</summary>
    public int CbInsult { get; init; } = 10;
    public int CbUnpaidDues { get; init; } = 20;
    public int CbBrokenTreaty { get; init; } = 30;
    public int CbSeizedFief { get; init; } = 40;
    public int CbRaid { get; init; } = 40;
    public int CbExcommunication { get; init; } = 50;
    /// <summary>The strength term: points for each whole step of (ratio - 1), clamped to plus or minus <see cref="RatioClamp"/>.</summary>
    public int RatioPts { get; init; } = 30;
    public int RatioClamp { get; init; } = 60;
    public int TreasurySeasonPts { get; init; } = 5;
    public int TreasurySeasonsMax { get; init; } = 20;
    public int SpringPts { get; init; } = 10;
    public int SummerPts { get; init; } = 10;
    public int AutumnPts { get; init; } = -10;
    public int WinterPts { get; init; } = -30;
    /// <summary>The harvest cost is the levies' lost yield in permille of a year's receipts divided by this.</summary>
    public int HarvestCostDiv { get; init; } = 10;
    /// <summary>The levies' lost yield as permille of a year's receipts when nothing else is known (a realm that raised the host in spring).</summary>
    public int HarvestLostPermille { get; init; } = 100;
    public int AllyPenalty { get; init; } = 20;
    public int TrucePenalty { get; init; } = 30;
    public int DeclareAt { get; init; } = 50;
    public int CautiousDeclareAt { get; init; } = 70;
    public int MartialDeclareAt { get; init; } = 40;
    // ---- peace (4.1)
    public int WearinessPerSeason { get; init; } = 10;
    /// <summary>Weariness for each percent of the population dead, times this.</summary>
    public int WearinessDeadPct { get; init; } = 5;
    public int WearinessThinTreasury { get; init; } = 10;
    public int SueGap { get; init; } = 40;
    public int CedeScore { get; init; } = 60;
    public int TributePct { get; init; } = 5;
    public int TributeSeasons { get; init; } = 8;
    public int TruceSeasons { get; init; } = 2;
    public int AllianceSeasons { get; init; } = 8;
    public int TradePactSeasons { get; init; } = 4;
    /// <summary>War score points a season of the stronger side's advantage moves, per 10 percent of strength lead.</summary>
    public int ScorePerLeadStep { get; init; } = 4;
    public int ScoreMaxStep { get; init; } = 14;
    /// <summary>The share of its strength a realm loses in a season's fighting, permille, for each side (the loser loses more).</summary>
    public int LossPermille { get; init; } = 40;
    public int LossLoserPermille { get; init; } = 90;
    public int LossRecoverPermille { get; init; } = 60;
    // ---- the road to lord (4.3)
    public int FealtyStanding { get; init; } = 300;
    public int FealtyRenown { get; init; } = 100;
    public int FealtyContracts { get; init; } = 3;
    public int FealtyOpinion { get; init; } = 30;
    public int ManorOpinion { get; init; } = 50;
    public int ManorServiceContracts { get; init; } = 6;
    public int FiefOpinion { get; init; } = 40;
    public int RenounceLiegeOpinion { get; init; } = -60;
    /// <summary>Unpaid ransom, a lord's defence or ransom the liege owes his vassal: opinion when he does not.</summary>
    public int NoProtectionOpinion { get; init; } = -60;
    // ---- lords (4.2)
    public int ActMin { get; init; } = 300;
    public int ActionsPerSeason { get; init; } = 2;
    public int StaleSeasons { get; init; } = 2;
    public int GiftPerTenCrowns { get; init; } = 1;
    public int GiftCap { get; init; } = 20;
    public int GiftMaxMitesPerSeason { get; init; } = 20000;
    /// <summary>Envoys that find their road: percent of the seasons a ruler learns a rival's strength afresh.</summary>
    public int EnvoyArrivePct { get; init; } = 70;
    public int OpinionMax { get; init; } = 100;
    /// <summary>Opinion points the hero's petition for a fief needs of the ruler, and the least of service he must have done (a war contract or a deed).</summary>
    public int PetitionOpinion { get; init; } = 40;
    /// <summary>Of a ruler's choice of a grantee: opinion + standing / <see cref="GrantStandingDiv"/> + service done.</summary>
    public int GrantStandingDiv { get; init; } = 10;
    /// <summary>A realm's levy in permille of its people (history's own); a founded realm's host is counted the same way.</summary>
    public int LevyPermille { get; init; } = 10;
    /// <summary>What a manor's holder takes of the hearth tax of its village, percent.</summary>
    public int ManorRentPct { get; init; } = 30;
    /// <summary>Of a captured lord: percent chance a decisive battle takes the loser's best-known lord (stream 0x671).</summary>
    public int CapturePct { get; init; } = 25;
    /// <summary>The lead (score points of one season) that makes a battle decisive.</summary>
    public int DecisiveStep { get; init; } = 8;
    /// <summary>A captured lord's ransom, the Baron rank price of the captivity rules (the wiring copies the content's own figure over this default).</summary>
    public long LordRansomMites { get; init; } = 100000;
    /// <summary>Percent chance a season that a ruler who has a casus belli or a claim and a utility over his threshold takes the field (the pace of declarations: a realm does not fight every season it could).</summary>
    public int DeclareChancePct { get; init; } = 30;
}

/// <summary>One kind of opinion modifier (<c>politics/opinion.jsonc</c>): its size, how much of it fades each season (0: it stays while its cause lasts) and the sentence the Faction screen shows.</summary>
[ContentType("opinion_kind")]
public sealed record OpinionKindDef : ContentDef
{
    public int Amount { get; init; }
    /// <summary>Points that fade toward zero at each season's start; 0 means none fades (a marriage, a kinship).</summary>
    public int DecayPerSeason { get; init; }
    /// <summary>The most this kind may add up to for one pair (0: no cap). A gift's cap is +20.</summary>
    public int Cap { get; init; }
    public string TextKey { get; init; } = "";
}

/// <summary>One consideration of a lord's action: a curve over a named input, its output 0..1000 multiplied into the action's utility; <see cref="NoteKey"/> is the cause the chronicle gives when this is the strongest driver.</summary>
public sealed record ConsiderationDef
{
    public required string Input { get; init; }
    /// <summary>Points (x, y) of a piecewise-linear curve, x ascending, y 0..1000; flat beyond the ends.</summary>
    public List<List<int>> Curve { get; init; } = [];
    public string NoteKey { get; init; } = "";
}

/// <summary>An action a lord or a ruler may take (<c>politics/considerations.jsonc</c>): who may take it, the sentence of the chronicle and the considerations multiplied into its utility (diplomacy-and-ai-lords 3, 4.2).</summary>
[ContentType("lord_action")]
public sealed record LordActionDef : ContentDef
{
    /// <summary>lord (a fief's holder) or ruler (the person the realm names).</summary>
    public string Actor { get; init; } = "lord";
    public string TextKey { get; init; } = "";
    /// <summary>The least utility to act; 0 means the settings' <see cref="PoliticsSettingsDef.ActMin"/>.</summary>
    public int Min { get; init; }
    public List<ConsiderationDef> Considerations { get; init; } = [];
}
