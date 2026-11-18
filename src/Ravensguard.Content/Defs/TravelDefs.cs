namespace Ravensguard.Content;

/// <summary>One group of people an encounter puts on the road (travel spec 3.1): an archetype, how many, their kit and where they appear relative to the hero.</summary>
public sealed record EncounterGroupDef
{
    public required string Archetype { get; init; }
    /// <summary>[min, max] people.</summary>
    public List<int> Count { get; init; } = [1, 1];
    public string Kit { get; init; } = "";
    /// <summary>ahead (on the path), cover_ahead (in cover tiles ahead), behind, edge (animals from the woodland edge).</summary>
    public string Place { get; init; } = "ahead";
    public int MinM { get; init; } = 120;
    public int MaxM { get; init; } = 180;
}

/// <summary>
/// A road encounter (docs/specs/phase3/travel-encounters-camp.md 3.1, 3.2). <c>Conditions</c> are the spec's <c>when</c>, named so the vocabulary validator checks every id. The weight is a
/// relative permille among the eligible rows. Scripts, outcomes and archetypes are names the game's behaviour layer resolves.
/// </summary>
[ContentType("encounter")]
public sealed record EncounterDef : ContentDef
{
    public required string Name { get; init; }
    /// <summary>Pass, Trade, Block, Work, Ask, Check, Job, Obstacle, Demand, Ambush, Animal, Column, Omen, Find.</summary>
    public required string Kind { get; init; }
    public required int WeightPm { get; init; }
    public List<ConditionRefDef> Conditions { get; init; } = [];
    public List<EncounterGroupDef> Groups { get; init; } = [];
    public string Script { get; init; } = "pass_by";
    public Dictionary<string, string> Outcomes { get; init; } = [];
    public int CooldownGameHours { get; init; } = 24;
    public int MinFromSettlementM { get; init; }
    public bool OncePerLeg { get; init; }
    /// <summary>A short line the view shows when it appears.</summary>
    public string Bark { get; init; } = "";
}

/// <summary>Numbers of travel that are not an encounter: the roll, danger, speed gates, the camp, sentries, foraging and hunting (spec 4.1 to 4.5).</summary>
[ContentType("travel_settings")]
public sealed record TravelSettingsDef : ContentDef
{
    public int SlotMinutes { get; init; } = 10;
    public int MinGapMinutes { get; init; } = 30;
    /// <summary>Per 10-minute slot base chance (permille) by road tier: highway, road, track, offroad.</summary>
    public Dictionary<string, int> BasePm { get; init; } = [];
    public int NightMultPm { get; init; } = 2000;
    public int CalmMultPm { get; init; } = 500;
    public int CalmWithinM { get; init; } = 600;
    public int DangerRefPm { get; init; } = 500;
    public int SafeDays { get; init; } = 2;
    public int SafeScalePm { get; init; } = 300;
    public List<string> SafeDisabledKinds { get; init; } = [];
    public int DangerBasePm { get; init; } = 100;
    public int DangerPerVagrantPer1000 { get; init; } = 40;
    public int DangerRaidPm { get; init; } = 300;
    public int DangerWarPm { get; init; } = 400;
    public int DangerFamineYearPm { get; init; } = 100;
    public int DangerNightPm { get; init; } = 50;
    public int DangerCampFirePm { get; init; } = 200;
    // ---- speed (permille of the hero's own speed)
    public Dictionary<string, int> TerrainSpeedPm { get; init; } = [];
    public Dictionary<string, int> WeatherSpeedPm { get; init; } = [];
    public int HostileGateM { get; init; } = 200;
    public int HungerEatHours { get; init; } = 13;
    public int MealBreakMinutes { get; init; } = 10;
    public int DuskLeadMinutes { get; init; } = 60;
    // ---- camp
    public int CampMinFromSettlementM { get; init; } = 200;
    public int CampMaxSlopePm { get; init; } = 80;
    public int CampDecayDays { get; init; } = 3;
    public int FireBurnGPerHour { get; init; } = 5000;
    public int FireLightRadiusM { get; init; } = 12;
    public int FireWarmthM { get; init; } = 4;
    public int FireWarmthC { get; init; } = 15;
    public int FireBasePm { get; init; } = 700;
    public int FireSkillPm { get; init; } = 3;
    public int FireRainPm { get; init; } = -300;
    public int FireDryTinderPm { get; init; } = 200;
    public int GatherGPerMinute { get; init; } = 1500;
    public int SentryMax { get; init; } = 3;
    public int SentryShiftHours { get; init; } = 2;
    public int VisionDayM { get; init; } = 120;
    public int VisionMoonM { get; init; } = 30;
    public int VisionFireM { get; init; } = 18;
    public int VisionDarkM { get; init; } = 8;
    public int NoticeBasePm { get; init; } = 400;
    public int NoticePerceptionPm { get; init; } = 6;
    public int NoticeFatiguePenaltyPm { get; init; } = 150;
    public int MealMeasuresPerDay { get; init; } = 1000;
    public int MealLoyalty { get; init; } = 20;
    public int MealFamiliarity { get; init; } = 2;
    public int TalkMinutes { get; init; } = 20;
    public int TalkLoyalty { get; init; } = 50;
    public int TalkFamiliarity { get; init; } = 6;
    public int TalkMood { get; init; } = 5;
    public int TalkSocialPm { get; init; } = -150;
    public int AmbitionNights { get; init; } = 3;
    public int BreakCampMinutes { get; init; } = 10;
    // ---- forage
    public int ForageSweepMinutes { get; init; } = 10;
    public int ForagePctOfStock { get; init; } = 8;
    public int ForageCapG { get; init; } = 250;
    public int ForageWinterPm { get; init; } = 300;
    public int ForageIdBasePm { get; init; } = 300;
    public int ForageIdPerSkillPm { get; init; } = 7;
    public int ForageGainD { get; init; } = 50;
    // ---- hunting
    public int GameKgPerAnimalKm2 { get; init; } = 150;
    public int GameMaxPerKm2 { get; init; } = 8;
    public int AwarenessM { get; init; } = 30;
    public int SneakPm { get; init; } = 500;
    public int RunPm { get; init; } = 1500;
    public int DownwindPm { get; init; } = 600;
    public int BadWeatherPm { get; init; } = 700;
    public int SnareMax { get; init; } = 6;
    public int SnareDeadlineHours { get; init; } = 36;
    public int SnareGraceHours { get; init; } = 12;
    public int SnareBasePm { get; init; } = 15;
    public int SnareSkillPm { get; init; } = 3;
    // ---- butchery (clothing spec 4.8)
    public int ButcherFreshHours { get; init; } = 24;
    public int ButcherMaxHours { get; init; } = 72;
    public int ButcherHotC { get; init; } = 25;
    public int ButcherYieldBasePm { get; init; } = 700;
    public int ButcherYieldPerSkillPm { get; init; } = 6;
    public int ButcherVariancePm { get; init; } = 40;
    /// <summary>2.5 game hours plus 0.015 hours (54 s) per kg of carcass.</summary>
    public int ButcherBaseSeconds { get; init; } = 9000;
    public int ButcherSecondsPerKg { get; init; } = 54;
    public int ButcherHungPct { get; init; } = 25;
}

/// <summary>One tissue of a carcass: which good it becomes, its share of the carcass mass and how much of that share is recovered (permille).</summary>
public sealed record ButcherYieldDef
{
    public required string Good { get; init; }
    public int SharePm { get; init; }
    public int RecoverPm { get; init; } = 1000;
}

/// <summary>A game animal or a creature that can be butchered (travel spec 4.5, clothing spec 4.8). <c>Butcher</c> maps a tissue material to the good it gives.</summary>
[ContentType("game_species")]
public sealed record GameSpeciesDef : ContentDef
{
    public required string Name { get; init; }
    public required int MassKg { get; init; }
    /// <summary>Weight among the species a biome offers (permille).</summary>
    public int WeightPm { get; init; } = 100;
    public List<string> Biomes { get; init; } = [];
    /// <summary>Seasons it is found in: Spring Summer Autumn Winter (empty: all).</summary>
    public List<string> Seasons { get; init; } = [];
    public string BodyPlan { get; init; } = "quadruped";
    public bool Taboo { get; init; }
    public Dictionary<string, ButcherYieldDef> Butcher { get; init; } = [];
}

/// <summary>What a foraging sweep finds in a season: items by share, the good the weight is stocked as, food measure per kg comes from the good.</summary>
[ContentType("forage_item")]
public sealed record ForageItemDef : ContentDef
{
    public required string Name { get; init; }
    public required string Good { get; init; }
    public List<string> Seasons { get; init; } = [];
    public int SharePm { get; init; } = 250;
    /// <summary>Chance per 100 g of harm when eaten unidentified, as 1000 minus identification (spec 4.4): true for mushrooms.</summary>
    public bool Risky { get; init; }
    public bool Medicinal { get; init; }
}

/// <summary>A feast or fair on the calendar (spec 4.6): the feast name it hangs on (the calendar's first name), how long it lasts and what it does to the places that hold it.</summary>
[ContentType("fair")]
public sealed record FairDef : ContentDef
{
    public required string Name { get; init; }
    /// <summary>The <c>Calendar.FeastOn</c> name's day is its first day (Spring Fair day 8).</summary>
    public required string Feast { get; init; }
    public int Days { get; init; } = 1;
    /// <summary>charter_towns, villages or all.</summary>
    public string Holders { get; init; } = "all";
    public int StallBonus { get; init; }
    public int StallRentMitesPerDay { get; init; } = 30;
    public int JobBoardMult { get; init; } = 1;
    public int WorkEndHour { get; init; } = 13;
    public int GatheringMultPm { get; init; } = 1500;
    public Dictionary<string, int> DemandMultPm { get; init; } = [];
    public int WeddingBumpPm { get; init; }
    public int SocialMult { get; init; } = 1;
    public int PurseMin { get; init; }
    public int PurseMax { get; init; }
    public List<string> Contests { get; init; } = [];
    public int EventBonusPm { get; init; } = 150;
    public int AnnounceDays { get; init; } = 3;
}

/// <summary>A night event of a camp (spec 4.3): rolled once per game hour in the night hours.</summary>
[ContentType("camp_event")]
public sealed record CampEventDef : ContentDef
{
    public required string Name { get; init; }
    public required int WeightPm { get; init; }
    /// <summary>Only when the camp has no sentry awake.</summary>
    public bool Unwatched { get; init; }
    public List<string> Seasons { get; init; } = [];
    /// <summary>Effect keyword the camp applies: steal_food, wolf_visit, traveller, rain, storm_omen, bad_dream, fox_scare.</summary>
    public required string Effect { get; init; }
    public int DangerAtLeastPm { get; init; }
}
