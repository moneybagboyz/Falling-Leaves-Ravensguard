namespace Ravensguard.Content;

/// <summary>
/// A container a person carries or that stands in the world (trade-inventory-character spec 3.2, <c>content/base/items/containers.jsonc</c>): its volume, its own mass,
/// the longest thing it takes, where it is carried and how many hands it needs. Liquids need a container with <see cref="Liquid"/>.
/// </summary>
[ContentType("container")]
public sealed record ContainerDef : ContentDef
{
    public string Name { get; init; } = "";
    public int VolumeL { get; init; }
    public int EmptyMassG { get; init; }
    public bool Soft { get; init; } = true;
    public int MaxItemCm { get; init; } = 60;
    /// <summary>belt, shoulder, back, hand, both_hands, horse, placed.</summary>
    public string Slot { get; init; } = "shoulder";
    /// <summary>Hands it takes while carried (0 = worn).</summary>
    public int Hands { get; init; }
    public bool Liquid { get; init; }
    /// <summary>What it is made of (its mass is one component of this material).</summary>
    public string Material { get; init; } = "leather";
}

/// <summary>Trade-screen, notebook, hiring and recruiting numbers (trade-inventory-character spec 3.4, 4.1, 4.2, 4.6; <c>content/base/economy/trade.jsonc</c>).</summary>
[ContentType("trade_settings")]
public sealed record TradeSettingsDef : ContentDef
{
    public int FriendDiscountPm { get; init; }
    public int BuyStolenPm { get; init; } = 500;
    public int PriceAgeFreshDays { get; init; } = 3;
    public int PriceAgeOldDays { get; init; } = 15;
    public int SellUnknownConditionPm { get; init; } = 700;
    /// <summary>The slippage guard: a buy or sell carries the quote plus or minus this (permille).</summary>
    public int SlippagePm { get; init; } = 20;
    /// <summary>Barter: what a person needs counts this much more, what they cannot use this much less (permille).</summary>
    public int WantPm { get; init; } = 200;
    // hiring (4.6)
    public int HireSkillPmPerLevel { get; init; } = 12;
    public int HireFoodPct { get; init; } = 25;
    public int HireBedPct { get; init; } = 25;
    public int HireNeedBonus { get; init; } = 150;
    public int HireTiePenalty { get; init; } = 300;
    public int HireNoise { get; init; } = 100;
    public int UnpaidDaysToQuit { get; init; } = 3;
    // recruiting (4.6)
    public int RecruitFamiliarityMin { get; init; } = 20;
    public int RecruitTrustMin { get; init; } = 100;
    public int SigningBonusDays { get; init; } = 5;
    public int SeveranceDays { get; init; } = 3;
    public int LoyaltyStart { get; init; } = 400;
    public int LoyaltyPaidDay { get; init; } = 10;
    public int LoyaltyUnpaidDay { get; init; } = -100;
    public int LoyaltyAskLeave { get; init; } = 200;
    public int SquadCap { get; init; } = 12;
}
