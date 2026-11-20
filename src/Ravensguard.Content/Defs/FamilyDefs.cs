namespace Ravensguard.Content;

/// <summary>One line of the ageing table (family-and-lifespan 4.6): an attribute loses <see cref="Amount"/> on each birthday from <see cref="From"/> to <see cref="To"/> that falls every <see cref="Every"/> years.</summary>
public sealed record AgeLossRow
{
    /// <summary>strength, toughness, dexterity, perception or wits.</summary>
    public required string Attr { get; init; }
    public int From { get; init; }
    public int To { get; init; } = 200;
    public int Every { get; init; } = 1;
    public int Amount { get; init; } = 1;
}

/// <summary>One step of the old-age hazard table: from <see cref="From"/> years the yearly chance (percent) of dying of old age.</summary>
public sealed record OldAgeRow
{
    public int From { get; init; }
    public int Pct { get; init; }
}

/// <summary>
/// The numbers of family life (family-and-lifespan 3, 4; <c>society/family.jsonc</c>): courting, the wedding, dowries by class, children and their schooling, the hero's ageing table and the old-age
/// hazard, kin and kidnapping. Every number is an integer; the defaults are the spec's.
/// </summary>
[ContentType("family_settings")]
public sealed record FamilySettingsDef : ContentDef
{
    // ---- courting (4.1): score = trust / TrustDiv + Base - ClassPenalty x |class gap| + WealthSwing x sign(wealth ratio - 1) + standing / StandingDiv + jitter(-J..J)
    public int TrustDiv { get; init; } = 10;
    public int Base { get; init; } = 20;
    public int ClassPenalty { get; init; } = 30;
    public int WealthSwing { get; init; } = 10;
    public int StandingDiv { get; init; } = 20;
    public int Jitter { get; init; } = 10;
    public int YesAt { get; init; } = 60;
    /// <summary>A gift adds trust + value / (household coin / 100) permille, at most this much a gift.</summary>
    public int GiftTrustCapPm { get; init; } = 100;
    /// <summary>A meeting (talk topic court, one a day) adds this much trust (permille) and a little familiarity.</summary>
    public int MeetingTrustPm { get; init; } = 15;
    // ---- the wedding (4.1, 4.3)
    public long WeddingFeeMites { get; init; } = 100;
    /// <summary>Holy days fall every this many days (day number divisible by it); the faith system of C1 replaces the rule with the religion's calendar.</summary>
    public int HolyDayEvery { get; init; } = 6;
    public int WeddingMinDaysAhead { get; init; } = 5;
    public int DowryPeasantPct { get; init; } = 5;
    public int DowryBurgherPct { get; init; } = 10;
    public int DowryGentryPct { get; init; } = 20;
    /// <summary>The wedding is called off when it has waited this many days past its day for a fee, a dwelling or a bride.</summary>
    public int WeddingGiveUpDays { get; init; } = 30;
    // ---- children (4.4)
    public int ChildbirthRiskPct { get; init; } = 1;
    public int AdultAge { get; init; } = 16;
    public int LessonStartAge { get; init; } = 7;
    public int UntaughtStartAge { get; init; } = 10;
    /// <summary>Skill gained per lesson day at skill 0, in thousandths of a point; falls with (1 - s / 100) squared (the Phase 4 learning curve).</summary>
    public int LessonMilli { get; init; } = 250;
    /// <summary>Untaught children learn their household's trade at this percent of the lesson rate.</summary>
    public int UntaughtPct { get; init; } = 50;
    public int ArmsAttrMilliPerDay { get; init; } = 20;
    public long HiredTutorMitesPerDay { get; init; } = 350;
    /// <summary>A priest teaches Letters free to a hero whose standing with the settlement is at least this (permille).</summary>
    public int PriestFreeStandingPm { get; init; } = 300;
    /// <summary>A child's starting attributes are the parents' mean plus or minus this (stream 0x641).</summary>
    public int ChildAttrSpread { get; init; } = 2;
    // ---- kin (4.5)
    public int KidnapPctPerSeason { get; init; } = 4;
    public int KidnapRenownDiv { get; init; } = 500;
    /// <summary>How likely a kidnapper band roams near the hero's home on a season, in permille (a stand-in until bands exist as world objects).</summary>
    public int KidnapBandPresencePm { get; init; } = 350;
    public int TurnBelowPm { get; init; } = -300;
    public int GrievanceWeightPm { get; init; } = 150;
    // ---- arranged matches and alliances (4.2, P6-E3): utility = 30 x alliance + standing / StandingDiv + the head's opinion + dowry / MatchDowryDiv - 20 per year of age gap over 10
    public int MatchAcceptAt { get; init; } = 50;
    public int MatchAllianceValue { get; init; } = 30;
    public int MatchStandingDiv { get; init; } = 10;
    /// <summary>Mites of dowry for one point of utility (0.5 per 100 crowns at 100 mites a crown).</summary>
    public int MatchDowryDiv { get; init; } = 20_000;
    public int MatchAgeGapFree { get; init; } = 10;
    public int MatchAgeGapPenalty { get; init; } = 20;
    /// <summary>The dowry is negotiated within this percent either way of the class share.</summary>
    public int MatchDowrySwingPct { get; init; } = 50;
    /// <summary>A member married off with his own acceptance under this carries the grievance married_off.</summary>
    public int MarriedOffBelow { get; init; } = 30;
    /// <summary>The chance (percent) a lord with an unmarried grown child arranges a match on a season's first day (stream 0x645).</summary>
    public int LordMatchPct { get; init; } = 30;
    /// <summary>The overlord sells his consent to a fief heir's match only while his opinion of the holder is at least this.</summary>
    public int ConsentOpinionMin { get; init; } = -20;
    /// <summary>The marriage alliance's opinion kinds, in both directions (the politics' opinion kinds).</summary>
    public int AllianceKinOpinion { get; init; } = 20;
    // ---- kin who turn (4.5, P6-E3)
    /// <summary>A kin member below the turn line leaves the household with this chance (percent) at his yearly check (stream 0x642).</summary>
    public int KinLeavePct { get; init; } = 30;
    /// <summary>A son the will passed over turns to the court at the hero's death below the turn line; the court's own odds start here (permille) that the law overrides the will.</summary>
    public int ContestLawPm { get; init; } = 600;
    public int ContestStandingDiv { get; init; } = 10;
    // ---- estates of NPCs (4.7, P6-E3)
    public long NpcWillMinMites { get; init; } = 5_000;
    public int NpcWillPct { get; init; } = 20;
    public int NpcWillDislikePm { get; init; } = -300;
    /// <summary>A creditor sells a business he claimed when its household has not paid the staff for this many days.</summary>
    public int CreditorSellDays { get; init; } = 15;
    /// <summary>A steward of a minor heir's business is paid this much a day from its cash box.</summary>
    public long StewardWageMitesPerDay { get; init; } = 60;
    // ---- the hero's ageing (4.6)
    public List<AgeLossRow> AgeLoss { get; init; } = [];
    public List<OldAgeRow> OldAge { get; init; } = [];
    /// <summary>A warning a season before the roll once the hazard is at least this percent.</summary>
    public int OldAgeWarnPct { get; init; } = 3;
}
