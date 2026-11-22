namespace Ravensguard.Content;

/// <summary>
/// P7-AM-A (armies-on-the-map 3.1, 4.1; content <c>content/base/war/armies.jsonc</c>): the size of a lord's standing warband by his holding. A fief's holding is the first row (ascending <see cref="MinPop"/>) whose
/// population it reaches, and its warband is between <see cref="Min"/> and <see cref="Max"/> men, grown with the fief's people across the row.
/// </summary>
public sealed record HoldingSizeDef
{
    /// <summary>The holding's name (<c>manor</c>, <c>knights_fee</c>, <c>baron</c>, <c>count</c>, <c>ruler</c>): the row's id, shown on the Army tab.</summary>
    public required string Id { get; init; }
    /// <summary>The fief's population from which the row applies.</summary>
    public int MinPop { get; init; }
    public int Min { get; init; }
    public int Max { get; init; }
}

/// <summary>
/// The warbands' constants (armies-on-the-map 3.1, 4.1), one row, <c>army_rules</c>: sizes by holding, the wage roll's cap, the pace, the task scores and thresholds, the patrol and the visit, the search for bandits and
/// the fight's kit. Every number is an integer; distances are metres, times hours or days, scores plain integers.
/// </summary>
[ContentType("army_rules")]
public sealed record ArmyRulesDef : ContentDef
{
    /// <summary>The holdings, ascending by population: manor 3 to 6, a knight's fee 6 to 12, a baron 20 to 40, a count 60 to 120, a ruler's household 150 to 300.</summary>
    public List<HoldingSizeDef> Holdings { get; init; } = [];
    /// <summary>A fief's population over which a holding's size reaches its maximum, counted from <c>min_pop</c> (a row at its last step reaches it at once).</summary>
    public int GrowthPop { get; init; } = 600;
    /// <summary>The wage roll may not exceed this share (per cent) of what a lord takes in a season: the last finished seasons' receipts, else a fourth of the purse he holds.</summary>
    public int WageRollMaxPct { get; init; } = 50;
    /// <summary>The wage in mites a day of a man who is not on a manor's payroll and has no role of his own (a levy kept on in a retinue: the levy's after-service wage, 15 cp).</summary>
    public int RetainerWageMitesDay { get; init; } = 150;
    /// <summary>The age of a man a lord will hire.</summary>
    public int MinAge { get; init; } = 16;
    public int MaxAge { get; init; } = 45;
    /// <summary>The pace in millimetres a game second: riders at the trot (5 m/s), men on foot (1.3 m/s). A warband that rides moves at the trot; one of foot at the column's pace.</summary>
    public int RideMmPerGs { get; init; } = 250;
    public int FootMmPerGs { get; init; } = 65;
    /// <summary>The hours of the day a warband travels (the first, and one past the last): 05:00 to 21:00.</summary>
    public int DayStartHour { get; init; } = 5;
    public int DayEndHour { get; init; } = 21;
    /// <summary>The hours spent at a village on a patrol, at a lord's hall on a visit, searching for bandits, and on a raid.</summary>
    public int PatrolDwellHours { get; init; } = 1;
    public int VisitDwellHours { get; init; } = 14;
    public int SearchHours { get; init; } = 4;
    public int RaidDwellHours { get; init; } = 2;
    /// <summary>A village not visited for this many days is due on the patrol; a farmstead for the second figure; the longest a patrol's loop is (stops).</summary>
    public int PatrolDueDays { get; init; } = 4;
    public int PatrolFarmDueDays { get; init; } = 10;
    public int PatrolMaxStops { get; init; } = 7;
    /// <summary>The scores (4.1): a task is kept until another scores this much more.</summary>
    public int SwitchMargin { get; init; } = 200;
    public int ScoreGarrison { get; init; } = 900;
    public int ScoreChase { get; init; } = 700;
    public int ScoreEscort { get; init; } = 650;
    public int ScoreRaid { get; init; } = 600;
    public int ScoreVisit { get; init; } = 400;
    public int ScorePatrol { get; init; } = 300;
    /// <summary>A lord visits another whose opinion of him is at least this much, and not twice within this many days (two seasons).</summary>
    public int VisitMinOpinion { get; init; } = 20;
    public int VisitGapDays { get; init; } = 30;
    /// <summary>The opinion a visit gives (the content kind <c>visit</c>).</summary>
    public int VisitOpinion { get; init; } = 5;
    /// <summary>A robbery news is acted on within this many days, within a day's ride (metres), and against a band of at most this share (permille) of the warband's strength more than itself (1,500 = 1.5 times).</summary>
    public int ChaseMaxAgeDays { get; init; } = 4;
    public int ChaseReachM { get; init; } = 14400;
    public int ChaseMaxRatioPm { get; init; } = 1500;
    /// <summary>The radius searched round the last-known place.</summary>
    public int SearchRadiusM { get; init; } = 4000;
    /// <summary>A raid in war: the enemy village within this reach (two days' ride), the grain taken a household (kg), and the share of a village's households robbed at most (per cent).</summary>
    public int RaidReachM { get; init; } = 28800;
    public int RaidKgPerHousehold { get; init; } = 40;
    public int RaidMaxHouseholdPct { get; init; } = 60;
    /// <summary>A warband is garrisoned when a raid by the enemy was reported within this reach of the seat (metres) in the last days.</summary>
    public int GarrisonReachM { get; init; } = 15000;
    public int GarrisonDays { get; init; } = 3;
    /// <summary>The tax wagon: it leaves the seat on this day of the season (1 = the first day), travels at the cart's pace and waits at most this many days for an escort.</summary>
    public int WagonSeasonDay { get; init; } = 3;
    public int WagonWaitDays { get; init; } = 1;
    public int WagonMmPerGs { get; init; } = 125;
    /// <summary>A warband escorts a wagon whose road is no longer than this (metres, straight): a longer one goes with its carters alone, so the lord's own villages are not left unridden for days.</summary>
    public int EscortMaxM { get; init; } = 12000;
    /// <summary>The lord pays a warband's wages each day; arrears over this many days of the roll and the men go home (the warband is dismissed).</summary>
    public int DismissArrearsDays { get; init; } = 20;
    /// <summary>Morale: gained a day on duty and paid, lost a day in arrears; a man whose warband's morale falls under this goes home (a share of a day).</summary>
    public int MoraleGain { get; init; } = 1;
    public int MoraleLoss { get; init; } = 3;
    /// <summary>The fight kit of a warband's man when he meets bandits (a <c>fight_kits</c> row), and of a household guard.</summary>
    public string FightKit { get; init; } = "soldier";
    public string GuardKit { get; init; } = "guard";
    /// <summary>The bandits: a band's strength rumour is exact by this share of its men (permille of the truth); deserters far from home are further than this many metres from their household; a member's drift home a season (permille).</summary>
    public int DeserterHomeM { get; init; } = 7400;
    public int BandDriftHomePm { get; init; } = 20;
    /// <summary>A band robs by day with this chance (permille) when it lives within this reach (metres) of a village or farmstead; each robbed household gives this much grain (kg) at most.</summary>
    public int BandRobPm { get; init; } = 250;
    public int BandRobReachM { get; init; } = 3000;
    public int BandRobKg { get; init; } = 20;
}
