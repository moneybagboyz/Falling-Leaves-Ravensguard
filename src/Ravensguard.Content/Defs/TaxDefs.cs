namespace Ravensguard.Content;

/// <summary>
/// Tax, rent, toll and treasury constants (taxes spec 2.2; content/base/economy/taxes.jsonc). Every amount is in mites; every share an integer percent or permille.
/// The four named hearth tax levels are the legacy "tax levels" of the plan (section 4A.2).
/// </summary>
[ContentType("tax_policy_defaults")]
public sealed record TaxPolicyDef : ContentDef
{
    // hearth tax per household and season by level (T1)
    public int HearthLow { get; init; } = 100;
    public int HearthNormal { get; init; } = 200;
    public int HearthHigh { get; init; } = 350;
    public int HearthCrushing { get; init; } = 600;
    /// <summary>Share of every fief's hearth receipts forwarded to the realm's treasury, percent (the fief keeps the floor of the rest, the realm the remainder).</summary>
    public int CrownSharePct { get; init; } = 30;
    // dwelling rent per season for a rented dwelling (a tenant of the lord owes none, only labour service)
    public int DwellingRentCottage { get; init; } = 100;
    public int DwellingRentLonghouse { get; init; } = 150;
    public int DwellingRentBurgage { get; init; } = 400;
    // tolls: bridge and gate tolls charged to caravans (caravans spec 3.5), market toll permille lives in money.jsonc
    public int GateTollMitesPerCart { get; init; } = 25;
    public int BridgeTollMitesPerCart { get; init; } = 10;
    public int GateTollPermilleGoods { get; init; } = 5;
    // the arrears ladder (T2)
    public int ArrearsDebtSeasons { get; init; } = 3;
    public int ArrearsEvictSeasons { get; init; } = 6;
    /// <summary>Labour that works off arrears on the demesne: worker-days a season for each of two working adults, and the mites a worker-day is worth (2 worker-days per 100 mites).</summary>
    public int WorkOffDaysPerSeason { get; init; } = 12;
    public int WorkOffMitesPerDay { get; init; } = 50;
    // evasion and unrest (T4, T6)
    public int EvasionChancePctWithGrievance { get; init; } = 30;
    public int TaxBurdenGrievancePermille { get; init; } = 150;
    /// <summary>Income a member of a household is taken to earn in a season (mites), for the burden test of the tax grievance.</summary>
    public int IncomePerMemberSeason { get; init; } = 1000;
    public int Rung1 { get; init; } = 200;
    public int Rung2 { get; init; } = 350;
    public int Rung3 { get; init; } = 550;
    public int Rung4 { get; init; } = 750;
    public int RiotCooldownDays { get; init; } = 30;
    public int RiotMinHouseholds { get; init; } = 3;
    public int RiotHouseholdPct { get; init; } = 5;
    /// <summary>Share of a parish granary (percent) the rioters take.</summary>
    public int RiotGranaryPct { get; init; } = 20;
    /// <summary>A household in want keeps this much a member when it pays: the steward waives half its hearth tax and takes the rest only from coin above it.</summary>
    public int HungerKeepMitesPerMember { get; init; } = 200;
    // payroll and the lord's share (T5)
    public int StewardWageMitesDay { get; init; } = 350;
    public int ReeveWageMitesDay { get; init; } = 250;
    public int PriestWageMitesDay { get; init; } = 250;
    public int GuardWageMitesDay { get; init; } = 250;
    public int SoldierWageMitesDay { get; init; } = 250;
    public int LordSharePctOfSurplus { get; init; } = 40;
    public int ReserveSeasons { get; init; } = 2;
    /// <summary>Share of the realm's balance spent in the region each season on royal works and the crown's own men (wages to idle hands); a closed region's stand-in for the crown's local outlay.</summary>
    public int CrownOutlayPct { get; init; } = 80;
    public int LevyPeaceHouseholds { get; init; } = 10;
    public int LevyWarHouseholds { get; init; } = 5;
}
