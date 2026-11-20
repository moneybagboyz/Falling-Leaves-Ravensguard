namespace Ravensguard.Content;

/// <summary>
/// One kind of crime (crime-and-justice 3, <c>law/crimes.jsonc</c>): the chronicle entry type its act writes (the deed row of <c>society/deeds.jsonc</c> reads the same type), whether a witness
/// raises the hue and cry, and the bounty the lord posts on a report (4.3): <c>BountyFlat + BountyValueX x value + damage / BountyDamageDiv</c> mites, capped at a quarter of the fief treasury.
/// </summary>
[ContentType("crime")]
public sealed record CrimeDef : ContentDef
{
    /// <summary>The chronicle entry type the act writes (empty: the crime's id).</summary>
    public string Entry { get; init; } = "";
    /// <summary>A witness of this crime in a settlement raises the hue and cry (theft, assault, murder, arson).</summary>
    public bool Cry { get; init; }
    /// <summary>The crime is a taboo or a sin against a religion (grave robbing, sacrilege).</summary>
    public bool Taboo { get; init; }
    /// <summary>A civil matter (squatting, debt): no hue and cry, no bounty.</summary>
    public bool Civil { get; init; }
    /// <summary>Whether a bounty is posted on a report (false: the lord only notes it).</summary>
    public bool Bounty { get; init; } = true;
    public long BountyFlat { get; init; }
    /// <summary>Times the value stolen or lost.</summary>
    public int BountyValueX { get; init; }
    /// <summary>Damage divided by this adds to the bounty (arson), 0: none.</summary>
    public int BountyDamageDiv { get; init; }
    /// <summary>The culprit dead pays all (an outlaw or a murderer) rather than half.</summary>
    public bool DeadPaysAll { get; init; }
    /// <summary>The bounty never expires (treason, murder).</summary>
    public bool Permanent { get; init; }
}

/// <summary>The numbers of seeing, recognising, the cry, reports, bounties and NPC crime (crime-and-justice 4.1 to 4.3, 4.8). All distances in metres.</summary>
[ContentType("law_settings")]
public sealed record LawSettingsDef : ContentDef
{
    // ---- seeing (4.1)
    public int SightDayM { get; init; } = 30;
    public int SightDuskM { get; init; } = 15;
    public int SightNightM { get; init; } = 6;
    public int SightLitBonusM { get; init; } = 6;
    public int SightIndoorLitM { get; init; } = 12;
    public int SightIndoorDarkM { get; init; } = 4;
    /// <summary>Heavy rain scales the range to this percentage.</summary>
    public int RainPct { get; init; } = 70;
    /// <summary>A sneaking actor shrinks every observer's range by Stealth / this percent (Stealth 60: 30%).</summary>
    public int StealthDivisor { get; init; } = 2;
    public int RecogniseM { get; init; } = 10;
    public int RecogniseHoodedM { get; init; } = 5;
    /// <summary>A cell with at least this much point light counts as lit.</summary>
    public int LitLevel { get; init; } = 60;
    // ---- the hue and cry (4.2)
    public int CryHearM { get; init; } = 30;
    /// <summary>Walls scale what the cry carries to this percentage.</summary>
    public int CryWallPct { get; init; } = 50;
    public int CryGuardM { get; init; } = 80;
    public int CryJoinMax { get; init; } = 3;
    public int CryJoinMelee { get; init; } = 30;
    /// <summary>A witness fears an actor whose renown is at least this (permille), unless he stands with two others.</summary>
    public int FearRenownPm { get; init; } = 600;
    /// <summary>A witness who trusts the actor this much (permille) raises nothing (kin and friends).</summary>
    public int FriendTrustPm { get; init; } = 500;
    // ---- reports (4.2)
    public int ReportPct { get; init; } = 90;
    public int ReportFearPct { get; init; } = 40;
    public int ReportFriendPct { get; init; } = 60;
    // ---- bounties (4.3)
    public int BountyCapPct { get; init; } = 25;
    public int BountyExpiryDays { get; init; } = 60;
    // ---- NPC crime (4.8)
    /// <summary>Per desperate household a day, in tenths of a permille of a day (150 = 1.5%: the spec's 0.5% gives a tenth of its own volume of 25 thefts a season; see the notes).</summary>
    public int NpcTheftPer10k { get; init; } = 150;
    /// <summary>Per household a day, in tenths of a permille, the chance two neighbours come to blows (assault).</summary>
    public int NpcAssaultPer10k { get; init; } = 6;
    public int NpcStealMaxMites { get; init; } = 300;
    /// <summary>The chance a victim who found the loss himself reports it.</summary>
    public int NpcVictimReportPct { get; init; } = 50;
    public int WitnessDayPct { get; init; } = 40;
    public int WitnessNightPct { get; init; } = 15;
    // ---- justice (P6-C1: arrest, gaol, the court, sentences; crime-and-justice 4.4 to 4.7)
    /// <summary>The daily chance (percent) that a guard on duty in the place takes a known culprit of a reported crime (stream 0x615, key crime, day).</summary>
    public int ArrestPct { get; init; } = 35;
    /// <summary>Mites a day for a prisoner's bread (a measure), paid from the fief treasury or the band's stash.</summary>
    public int RationMites { get; init; } = 3;
    /// <summary>A prisoner not fed for this many days in a row is let go (no starvation in a cell).</summary>
    public int UnfedReleaseDays { get; init; } = 2;
    /// <summary>The gaoler's daily wage, mites (the bribe's measure).</summary>
    public int GuardWageMites { get; init; } = 150;
    /// <summary>The honesty of an official nobody has rolled one for.</summary>
    public int DefaultHonesty { get; init; } = 45;
    /// <summary>An official at or above this honesty never takes a bribe.</summary>
    public int BribeHonestAt { get; init; } = 60;
    /// <summary>A bribe is at least <c>BribeMult x wage x (BribeHonestAt - honesty) / BribeDivisor</c> and then taken with P = BribeHonestAt - honesty percent.</summary>
    public int BribeMult { get; init; } = 3;
    public int BribeDivisor { get; init; } = 20;
    /// <summary>Standing with the fief at or above this (permille) lowers the sentence one step.</summary>
    public int MercyStandingPm { get; init; } = 300;
    /// <summary>A lord's opinion of the accused at or below this (permille) raises the sentence one step.</summary>
    public int HarshOpinionPm { get; init; } = -50;
    /// <summary>When mercy and harshness both apply, a draw (stream 0x612) decides: below this percent mercy, above 100 minus it harshness, else the middle holds.</summary>
    public int MercyTiePct { get; init; } = 20;
    /// <summary>Literacy that claims benefit of clergy where the church holds its own courts.</summary>
    public int ClergyLiteracy { get; init; } = 30;
    /// <summary>A witness further than this (metres) from the court does not come.</summary>
    public int WitnessReachM { get; init; } = 15_000;
    /// <summary>The court sits on days whose number is <c>CourtOffset</c> modulo <c>CourtEveryDays</c> (the last day of each five-day month).</summary>
    public int CourtEveryDays { get; init; } = 5;
    public int CourtOffset { get; init; } = 4;
    /// <summary>The hour of the court day the cases are heard, and the hour (next morning) the sentences are carried out.</summary>
    public int CourtHour { get; init; } = 8;
    public int CarryOutHour { get; init; } = 6;
    /// <summary>Pain (as the wound book counts it) of a day in the stocks, of each lash cut.</summary>
    public int StocksPain { get; init; } = 20;
    public int LashCutPain { get; init; } = 25;
    /// <summary>The executioner's surgery skill for the stump.</summary>
    public int ExecutionerSkill { get; init; } = 40;
    /// <summary>Gaol time for an unpaid debt of the court: mites a day, and the most it is.</summary>
    public int GaolMitesPerDay { get; init; } = 100;
    public int GaolMaxDays { get; init; } = 30;
    /// <summary>Mites of the church penance (alms to the parish).</summary>
    public int PenanceMites { get; init; } = 50;
    /// <summary>The outlaw of a realm may be killed by anyone without a crime; the feud of a blood price lasts this many days.</summary>
    public int FeudDays { get; init; } = 60;
}

/// <summary>One step of a punishment's escalation (crime-and-justice 3): what is done to the convicted at the first, second, third conviction of the kind.</summary>
public sealed record PunishStepDef
{
    /// <summary>fine, stocks, flogging, branding, hand, hanging, weregild, outlawry, restitution, gaol, bond_labour, penance, exile, blood_price, confiscation, kin_pays.</summary>
    public required string Kind { get; init; }
    /// <summary>Flat mites (a fine, a weregild, a penance).</summary>
    public long Mites { get; init; }
    /// <summary>Times the value of the thing taken or lost.</summary>
    public int ValueX { get; init; }
    /// <summary>Days (stocks, gaol, exile grace, the weregild's term, a body displayed).</summary>
    public int Days { get; init; }
    /// <summary>Days = at least <c>Days</c> and value / this (a bond's labour, a gaol term by the debt), 0: none.</summary>
    public int DaysDiv { get; init; }
    public int Lashes { get; init; }
    /// <summary>Days the body hangs on show after the hanging (drawn and quartered).</summary>
    public int Display { get; init; }
    /// <summary>What an unpaid sum becomes: "gaol" (the default), "outlawry" (the weregild of the north).</summary>
    public string Unpaid { get; init; } = "gaol";
    /// <summary>Days the victim's kin may kill the culprit without a crime (the blood feud), 0: none.</summary>
    public int Feud { get; init; }
}

/// <summary>A punishment table (crime-and-justice 3, <c>law/punishments.jsonc</c>): the key the cultures' crime tables name and its escalation by conviction count. <see cref="Severity"/> orders the keys for a lord's law (any key as mild or milder than the culture's may replace it).</summary>
[ContentType("punishment")]
public sealed record PunishmentDef : ContentDef
{
    public int Severity { get; init; } = 1;
    public required List<PunishStepDef> Steps { get; init; }
}

