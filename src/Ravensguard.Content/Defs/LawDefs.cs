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
}
