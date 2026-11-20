namespace Ravensguard.Content;

/// <summary>
/// The numbers of rule and income (rule-and-income 4, <c>rule/dues.jsonc</c>): service, aids, relief, wardship, marriage, councils, tallage, customs, officials, loans, eviction and charters. The defaults are
/// the spec's; money is in mites (1 crown = 100 mites), rates in percent or permille as named.
/// </summary>
[ContentType("rule_settings")]
public sealed record RuleSettingsDef : ContentDef
{
    // ---- food rents and labour service (4.1)
    /// <summary>A tenant household owes one day in this many during the plough (spring) and harvest (autumn) windows.</summary>
    public int LabourDayEvery { get; init; } = 5;
    /// <summary>A day of service not worked is owed in coin.</summary>
    public int LabourMissedMitesPerDay { get; init; } = 150;
    // ---- what vassals owe (4.2)
    public int ServiceManorMen { get; init; } = 1;
    public int ServiceManorDays { get; init; } = 7;
    /// <summary>A fief owes one man for each this many households, for <see cref="ServiceFiefDays"/> days.</summary>
    public int ServiceFiefHouseholdsPerMan { get; init; } = 50;
    public int ServiceFiefDays { get; init; } = 7;
    public int ReliefPct { get; init; } = 25;
    public int ReliefDueSeasons { get; init; } = 2;
    public int ReliefForfeitSeasons { get; init; } = 4;
    /// <summary>An heir under this age puts the fief in wardship; the guardian is paid <see cref="WardGuardianPct"/> of the receipts for his keep.</summary>
    public int WardAgeYears { get; init; } = 16;
    public int WardGuardianPct { get; init; } = 20;
    public int AidSeasons { get; init; } = 1;
    public int AidDueSeasons { get; init; } = 1;
    public int MarriageFeeSeasons { get; init; } = 1;
    public int MarriageFineSeasons { get; init; } = 2;
    public int GrievanceOpinion { get; init; } = -20;
    public int MarriageFineOpinion { get; init; } = -30;
    // ---- councils, subsidies, tallage (4.3)
    public int SubsidyTownPermille { get; init; } = 100;      // a tenth
    public int SubsidyCountryPermille { get; init; } = 67;    // a fifteenth
    public int TallagePermille { get; init; } = 100;
    public int CauseDefence { get; init; } = 40;
    public int CauseCasusBelli { get; init; } = 10;
    public int CauseNoWar { get; init; } = -30;
    public int CauseSecondSubsidy { get; init; } = -20;
    public int SecondSubsidyYears { get; init; } = 2;
    public int VoteJitter { get; init; } = 10;
    public int TallageGrievances { get; init; } = 2;
    public int SubsidyGrievances { get; init; } = 1;
    public int GrievanceSeasons { get; init; } = 1;
    public int TallageMayorOpinion { get; init; } = -30;
    /// <summary>An NPC ruler calls a council when the treasury holds less than this many seasons of payroll and a war is on.</summary>
    public int NpcCouncilPayrollSeasons { get; init; } = 2;
    // ---- customs (4.4)
    public int CustomsDefaultPct { get; init; } = 3;
    public int CustomsMaxPct { get; init; } = 15;
    /// <summary>Above this rate each point sends this many percent fewer caravans by the gate.</summary>
    public int CustomsFreePct { get; init; } = 8;
    public int CustomsFewerPerPoint { get; init; } = 10;
    // ---- officials (4.8)
    public int HonestyMin { get; init; } = 10;
    public int HonestyMax { get; init; } = 100;
    /// <summary>An official keeps (SkimBase - honesty) / SkimDiv percent of what he collects, when honesty is below SkimBase.</summary>
    public int SkimBase { get; init; } = 60;
    public int SkimDiv { get; init; } = 4;
    public int SeenPct { get; init; } = 20;
    public int AuditBasePct { get; init; } = 30;
    public int AuditMaxPct { get; init; } = 90;
    // ---- loans (4.6)
    public int LoanRatePctSeason { get; init; } = 4;
    public int LoanRiskExtraPct { get; init; } = 2;
    public int LoanLenderMinMultiple { get; init; } = 2;
    public int LoanDefaultLateSeasons { get; init; } = 2;
    public int LoanDefaultOpinion { get; init; } = -40;
    public int LoanCreditClosedYears { get; init; } = 2;
    // ---- eviction (4.7)
    public int EvictNoticeDays { get; init; } = 15;
    public int EvictLicenceMitesPerAcre { get; init; } = 1000;
    // ---- charters (4.9)
    public int CharterGrantThreshold { get; init; } = 40;
    public int CharterContestedPenalty { get; init; } = 50;
    public int CharterOtherRealmPenalty { get; init; } = 20;
    public int CharterFeeTenCrownsBonus { get; init; } = 5;
    public int CharterExtensionFeePct { get; init; } = 50;
    public int CharterExtensionYears { get; init; } = 1;
    public int CharterArrearsSeasons { get; init; } = 2;
    public int MarketCharterPeople { get; init; } = 200;
    public long MarketCharterFeeMites { get; init; } = 5000;
    public long MarketCharterYearMites { get; init; } = 500;
}

/// <summary>An office a lord fills (<c>rule/offices.jsonc</c>): its wage in mites a day and the skill it asks for (Law or Trading, empty for any adult).</summary>
[ContentType("office")]
public sealed record OfficeDef : ContentDef
{
    public required string Name { get; init; }
    public int WageMitesPerDay { get; init; }
    public string Skill { get; init; } = "";
}

/// <summary>A customs rate by good (<c>rule/customs.jsonc</c>): the percent on goods of this id leaving (export) or entering (import) by a gate; -1 means the realm's rate.</summary>
[ContentType("customs_rate")]
public sealed record CustomsRateDef : ContentDef
{
    public string Good { get; init; } = "";
    public int ExportPct { get; init; } = -1;
    public int ImportPct { get; init; } = -1;
}
