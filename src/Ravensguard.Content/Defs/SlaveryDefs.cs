namespace Ravensguard.Content;

/// <summary>
/// The numbers of slavery (slavery.md 3, <c>society/slavery.jsonc</c>): the price table (mites, permille multipliers by age, skill and body), the raids, the owner's rules, the runaways and the revolts. Chances are permille,
/// money is mites, times are game days.
/// </summary>
[ContentType("slavery_rules")]
public sealed record SlaveryRulesDef : ContentDef
{
    // ---- the price of an adult of 16 to 50 in health, and what changes it (3)
    public int BasePriceMites { get; init; } = 9000;
    public int PrimeMinAge { get; init; } = 16;
    public int PrimeMaxAge { get; init; } = 50;
    public int SkillMidMin { get; init; } = 40;
    public int SkillMidPm { get; init; } = 1500;
    public int SkillMasterMin { get; init; } = 70;
    public int SkillMasterPm { get; init; } = 2000;
    public int ChildAge { get; init; } = 12;
    public int ChildPm { get; init; } = 400;
    public int TeenPm { get; init; } = 700;
    public int ElderPm { get; init; } = 400;
    public int MaimedPm { get; init; } = 500;
    public int InfirmPm { get; init; } = 200;
    // ---- law and debt (4.1, 4.3)
    /// <summary>The day labour wage a debt is worked off at, mites a day: a debtor is bound for <c>debt / this</c> days.</summary>
    public int DebtDayWageMites { get; init; } = 150;
    /// <summary>Owning a slave in a culture that forbids it: the fine is this many times the price.</summary>
    public int SlavingFineX { get; init; } = 2;
    // ---- raids and the pens (4.1)
    /// <summary>A region's chance of a raid in a season at 100% bandits, permille (stream 0x630).</summary>
    public int RaidPm { get; init; } = 150;
    public int RaidMin { get; init; } = 2;
    public int RaidMax { get; init; } = 8;
    /// <summary>A village is raided only where its lord keeps fewer guards than this.</summary>
    public int RaidGuardsBelow { get; init; } = 3;
    /// <summary>A market's lot is bought by a household in a season with this chance, permille (stream 0x631).</summary>
    public int BuyerPm { get; init; } = 500;
    /// <summary>A buyer needs coin of this many times the price.</summary>
    public int BuyerCoinX { get; init; } = 3;
    /// <summary>The share of the price a bond sold to a buyer at a market brings, permille.</summary>
    public int SellPm { get; init; } = 900;
    // ---- owning (4.2)
    /// <summary>The daily tip an owner of Fair and Kind treatment lets a slave keep (the peculium), mites.</summary>
    public int FairTipMites { get; init; } = 5;
    public int KindTipMites { get; init; } = 8;
    /// <summary>Morale (0..100): a slave flees below this unless chained; Harsh lowers it a day, Kind raises it toward the cap.</summary>
    public int FleeMorale { get; init; } = 40;
    public int MoraleStart { get; init; } = 50;
    public int HarshMoraleDay { get; init; } = 2;
    public int KindMoraleDay { get; init; } = 1;
    public int KindMoraleCap { get; init; } = 80;
    /// <summary>Hungry days before a bonded person counts as hungry for a revolt; the Harsh basket's percent.</summary>
    public int HungryDaysGrievance { get; init; } = 3;
    public int HarshFoodPct { get; init; } = 70;
    /// <summary>The owner's trust (permille) and the days held that free a slave by the owner's own wish.</summary>
    public int TrustFreePm { get; init; } = 700;
    public int TrustFreeDays { get; init; } = 60;
    // ---- runaways (4.4)
    /// <summary>A runaway's chance each day per point of morale under the flee line, permille (stream 0x632).</summary>
    public int RunawayPmPerPoint { get; init; } = 8;
    /// <summary>The slave catchers find a runaway after this many days at the earliest, then each day with this chance, permille.</summary>
    public int CatchAfterDays { get; init; } = 3;
    public int CatchPm { get; init; } = 120;
    /// <summary>The runaway's bounty, mites.</summary>
    public int RunawayBountyMites { get; init; } = 400;
    // ---- revolts (4.5)
    public int RevoltMinBonded { get; init; } = 10;
    public int RevoltSharePct { get; init; } = 50;
    /// <summary>A holding falls to revolt only while its guards are fewer than bonded / this.</summary>
    public int RevoltGuardDiv { get; init; } = 5;
    public int RevoltLeaderLeadership { get; init; } = 40;
    /// <summary>Days a flogging or a sale counts as a grievance.</summary>
    public int GrievanceDays { get; init; } = 15;
}
