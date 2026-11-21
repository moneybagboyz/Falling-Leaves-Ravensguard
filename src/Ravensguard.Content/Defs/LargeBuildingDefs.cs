namespace Ravensguard.Content;

/// <summary>
/// P7-LB2 (large-buildings 4.4; content <c>content/base/production/large_buildings.jsonc</c>): the constants of the large buildings' rooms. Every number is content; the defaults are the shipped values.
/// Floor space in m2, litres, kilograms, minutes, days; money in mites.
/// </summary>
[ContentType("large_building_rules")]
public sealed record LargeBuildingRulesDef : ContentDef
{
    /// <summary>The floor each worker needs in an ordinary workshop and in a manufactory hall (4 and 3 m2).</summary>
    public int CrowdingM2 { get; init; } = 4;
    public int HallCrowdingM2 { get; init; } = 3;
    /// <summary>A hall's one view across the floor adds this to each overseer's span.</summary>
    public int HallSpanBonus { get; init; } = 4;

    /// <summary>What the armoury's furniture holds: a stand one body armour and its helmet; a rack 10 weapons of up to 3 kg or 4 polearms; a shelf 10 helmets or 6 shields; a chest 40 sheaves of arrows.</summary>
    public int StandArmours { get; init; } = 1;
    public int RackWeapons { get; init; } = 10;
    public int RackPolearms { get; init; } = 4;
    public int ShelfHelmets { get; init; } = 10;
    public int ShelfShields { get; init; } = 6;
    public int ChestSheaves { get; init; } = 40;
    /// <summary>One armourer (smithing at least <see cref="ArmourerSkill"/>) keeps this many items; each item needs <see cref="CareMinutes"/> minutes every <see cref="CareDays"/> days.</summary>
    public int ItemsPerArmourer { get; init; } = 300;
    public int ArmourerSkill { get; init; } = 30;
    public int CareMinutes { get; init; } = 6;
    public int CareDays { get; init; } = 30;
    /// <summary>The rust an uncared item takes in a room with no hearth in the building, in winter, is multiplied by this (permille).</summary>
    public int ColdMultPm { get; init; } = 2000;
    /// <summary>A shelved weapon's mass limit for the rack's ten places, in grams.</summary>
    public int RackWeaponMaxG { get; init; } = 3000;

    /// <summary>A warehouse stacks this many litres a m2 of floor; the owner rents space at this many mites a m3 a season.</summary>
    public int WarehouseLitresPerM2 { get; init; } = 1500;
    public int RentMitesPerM3Season { get; init; } = 20;

    /// <summary>A granary depot holds this many kilograms of grain a m2 of floor and sells what is older than this many seasons at the harvest market.</summary>
    public int DepotKgPerM2 { get; init; } = 750;
    public int DepotRotateSeasons { get; init; } = 2;

    /// <summary>Barracks: two men to a bunk (the bunk's beds), 1 kg of straw a bed every 15 days.</summary>
    public int BarracksStrawKgPerBed { get; init; } = 1;
    public int BarracksStrawDays { get; init; } = 15;
}
