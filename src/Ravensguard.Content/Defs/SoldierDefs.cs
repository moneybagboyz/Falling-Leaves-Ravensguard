namespace Ravensguard.Content;

/// <summary>
/// P7-SC-A (soldiers-and-companies 3.1; content <c>content/base/war/troops.jsonc</c>): one troop role. A person holds the role when he meets the skill requirement (all of <see cref="Requires"/>, and at least one of
/// <see cref="RequiresAny"/> when it is not empty); the role brings a loadout template and a daily wage in copper. Skills are the Phase 3 and 4 skill names (<c>Polearms</c>, <c>Shields</c>, <c>Riding</c> ...).
/// </summary>
[ContentType("troop_role")]
public sealed record TroopRoleDef : ContentDef
{
    public required string Name { get; init; }
    /// <summary>Fill order: the lowest number is kitted first (knight 0 ... levy 7; the filling rule 4.2).</summary>
    public int Priority { get; init; }
    /// <summary>The role's default template (a <see cref="LoadoutTemplateDef"/> id; <c>levy_by_wealth</c> is the household's gear, resolved by wealth).</summary>
    public string Loadout { get; init; } = "";
    /// <summary>Wage in copper a day (a mercenary's is the rules' multiple of it).</summary>
    public int WageCp { get; init; }
    /// <summary>A levy's wage after the owed service: copper a day (0 = none).</summary>
    public int WageAfterServiceCp { get; init; }
    /// <summary>The ransom class (levy, man_at_arms, knight).</summary>
    public string RansomClass { get; init; } = "levy";
    /// <summary>Skills that must all reach the level.</summary>
    public Dictionary<string, int> Requires { get; init; } = [];
    /// <summary>Skills of which one must reach the level (a weapon family).</summary>
    public Dictionary<string, int> RequiresAny { get; init; } = [];
    /// <summary>A knight must be dubbed by a lord (SC-B); the role never comes from skills alone.</summary>
    public bool Dubbed { get; init; }
    /// <summary>The roles a person of this one may be promoted to.</summary>
    public List<string> Next { get; init; } = [];
}

/// <summary>One slot of a loadout template (3.2): the acceptable item shapes in order, the floors, and whether the slot is required.</summary>
public sealed record LoadoutSlotDef
{
    /// <summary>weapon, alt_weapon, shield, helmet, under, over, armour, plate, cover, legs, hands, feet, launcher.</summary>
    public required string Slot { get; init; }
    /// <summary>Acceptable item shape ids (a weapon, shield or armour shape).</summary>
    public List<string> Accept { get; init; } = [];
    /// <summary>The lowest quality tier acceptable (crude, poor, common, fine, masterwork); empty: any.</summary>
    public string MinQuality { get; init; } = "";
    /// <summary>When not empty, the item's first component must be one of these materials.</summary>
    public List<string> MinMaterial { get; init; } = [];
    public bool Required { get; init; }
    /// <summary>The slot is the soldier's own (his clothes, his household's gear): only what he already owns fills it; an armoury is never asked, so it is never an armoury gap.</summary>
    public bool Own { get; init; }
}

/// <summary>A loadout template (3.2): the slots of what a role carries, in fill order.</summary>
[ContentType("loadout_template")]
public sealed record LoadoutTemplateDef : ContentDef
{
    public required string Name { get; init; }
    public List<LoadoutSlotDef> Slots { get; init; } = [];
}

/// <summary>
/// The soldiers' constants (3.3, 4.2, 4.3): the filling score's terms, the fit tolerance, the mercenary wage multiple and the material ranks. One row, <c>soldier_rules</c>.
/// </summary>
[ContentType("soldier_rules")]
public sealed record SoldierRulesDef : ContentDef
{
    /// <summary>Score per quality tier, the divisor of condition (permille), per material rank; the misfit penalty and the bonus for what the soldier already wears.</summary>
    public int QualityScore { get; init; } = 100;
    public int ConditionDiv { get; init; } = 10;
    public int MaterialScore { get; init; } = 50;
    public int MisfitPenalty { get; init; } = 300;
    public int WornBonus { get; init; } = 20;
    /// <summary>A piece fits within this many permille of the size it was made for (armour only): 150 = 15%.</summary>
    public int FitTolerancePm { get; init; } = 150;
    /// <summary>A mercenary's wage is the role's times this (permille, rounded down in copper).</summary>
    public int MercWageMultPm { get; init; } = 1500;
    /// <summary>Material rank by id (wool and linen 0, leather and bronze 1, iron 2, steel 3 ...); an unknown material ranks 0.</summary>
    public Dictionary<string, int> MaterialRank { get; init; } = [];
    /// <summary>Armour rig layers a refit can only take in (plate); mail is refitted both ways, any other armour piece by a tailor, both ways.</summary>
    public List<string> RefitShrinkOnly { get; init; } = [];
    /// <summary>The household wealth, in mites, from which a levy brings the middling kit and the well-off kit (4.3: AE's armed levy floor, 20,000).</summary>
    public int MiddlingMites { get; init; } = 20000;
    public int WellOffMites { get; init; } = 100000;
    /// <summary>The template a lord's armoury fills a levy's gaps from by default (spears and shields only).</summary>
    public string LevyLordFill { get; init; } = "levy_lord_fill";

    // ---- P7-SC-B (4.4, 4.5): growth by fighting and drill, promotions, retinue caps ----
    /// <summary>A meaningful use in a fight: the skill gain's d (100 = the Phase 3 reference), and how many uses one fight counts at most (blows landed or blocked, parries, hits; spread over the fight's hours, twelve an hour).</summary>
    public int FightDx100 { get; init; } = 100;
    public int FightUsesPerFight { get; init; } = 18;
    /// <summary>A drill hour: d and the level at which drill stops (40; shield bearers 35 in Shields), uses an hour, the most hours a day.</summary>
    public int DrillDx100 { get; init; } = 40;
    public int DrillCap { get; init; } = 40;
    public int DrillShieldCap { get; init; } = 35;
    public int DrillUsesPerHour { get; init; } = 8;
    public int DrillHoursDay { get; init; } = 2;
    /// <summary>Sparring with a better partner trains to the partner's skill less the margin, at most the ceiling; d and uses an hour.</summary>
    public int SparDx100 { get; init; } = 100;
    public int SparMargin { get; init; } = 5;
    public int SparMaxLevel { get; init; } = 60;
    public int SparUsesPerHour { get; init; } = 12;
    /// <summary>Formation drill: gained an hour in formation and lost a day without (0..1000); closing ranks waits base less drill / div ticks (battle spec 4.3).</summary>
    public int FormationHourGain { get; init; } = 20;
    public int FormationDayLoss { get; init; } = 5;
    public int CloseRanksBaseTicks { get; init; } = 40;
    public int CloseRanksDrillDiv { get; init; } = 25;
    /// <summary>The most soldiers a lord's retinue holds beyond his squad of companions, by holding id (manor, knights_fee, baron, count, ruler).</summary>
    public Dictionary<string, int> RetinueCaps { get; init; } = [];
}
