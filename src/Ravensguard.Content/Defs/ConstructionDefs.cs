namespace Ravensguard.Content;

/// <summary>
/// P5-B: the rules of hands-on construction (docs/specs/phase5/build-mode-and-construction.md 4.3-4.6; content <c>content/base/build/construction.jsonc</c>). Every number is content;
/// the defaults are the shipped values. Rates carry their unit suffix, money is in mites.
/// </summary>
[ContentType("construction_rules")]
public sealed record ConstructionRulesDef : ContentDef
{
    // ---------- support and collapse (spec 4.5) ----------
    /// <summary>A floor-bearing piece at z >= 1 rests when a wall or post at z - 1 lies within this many tiles (Chebyshev, plan 2.2a).</summary>
    public int SupportReachTiles { get; init; } = 3;
    /// <summary>A carried beam holds up floor-bearing pieces within this many tiles of it at its own level (joists rest on it).</summary>
    public int BeamReachTiles { get; init; } = 4;
    /// <summary>The longest clear span of a beam between the supports under it.</summary>
    public int BeamSpanTiles { get; init; } = 6;
    /// <summary>A wall at z >= 1 with nothing directly below must stand within this many tiles of a support (a jetty).</summary>
    public int JettyReachTiles { get; init; } = 1;
    /// <summary>The highest level anything may be built on (plan 2.2a: z = +1..+3).</summary>
    public int MaxLevel { get; init; } = 3;
    /// <summary>Ground floors are refused on ground steeper than this (permille).</summary>
    public int MaxSlopePm { get; init; } = 100;
    /// <summary>Unsupported cells groan this many game seconds before they fall (decided with the user: about 10).</summary>
    public int GroanSeconds { get; init; } = 10;
    /// <summary>A fall through one missing level (Phase 4 fall rules).</summary>
    public int FallMmPerLevel { get; init; } = 3000;
    /// <summary>The body mass a falling person lands with when nothing better is known (kg).</summary>
    public int FallBodyKg { get; init; } = 70;

    // ---------- the work (spec 4.4) ----------
    /// <summary>Workers per cell, and on a stair pair or a forge.</summary>
    public int WorkersPerCell { get; init; } = 2;
    public int WorkersPerBigCell { get; init; } = 4;
    /// <summary>Time multiplier (permille) when a needed tool quality is missing.</summary>
    public int NoToolPm { get; init; } = 1500;
    /// <summary>A crew's working day: the first hour and how many hours (hours 06-16).</summary>
    public int DayStartHour { get; init; } = 6;
    public int DayHours { get; init; } = 10;
    /// <summary>The hero works by his own light from this hour to the end hour; outdoors outside it only by a lit cell.</summary>
    public int LightFromHour { get; init; } = 5;
    public int LightToHour { get; init; } = 21;
    /// <summary>Rain that stops daub, thatch and lime (tenths of a mm an hour) and the frost line (C).</summary>
    public int RainStopTenthsMmPerHour { get; init; } = 20;
    public int FrostBelowC { get; init; }
    /// <summary>Skill gain of an attended hour on an unskilled piece (Athletics), as crafting's d x 100.</summary>
    public int UnskilledGainDx100 { get; init; } = 20;
    /// <summary>A worker must stand within this many metres of the ghost (the hero's E range).</summary>
    public int ReachM { get; init; } = 3;
    /// <summary>The engine credits work in whole quanta of game time (a pure function of time, whatever the call cadence).</summary>
    public int QuantumSeconds { get; init; } = 300;
    /// <summary>The tools a hired worker brings, by class (craft_tool ids).</summary>
    public List<string> LabourerTools { get; init; } = ["axe", "spade"];
    public List<string> SkilledTools { get; init; } = ["axe", "saw", "hammer", "chisel", "spade"];

    // ---------- crews and wages (spec 4.6) ----------
    public int CrewMax { get; init; } = 8;
    /// <summary>Production's wage classes, mites a 10-hour day; Carpentry or Masonry above this is skilled.</summary>
    public int WageLabourerMitesPerDay { get; init; } = 150;
    public int WageSkilledMitesPerDay { get; init; } = 350;
    public int SkilledAboveSkill { get; init; } = 30;
    /// <summary>A hired worker quits after this many days in a row without wages.</summary>
    public int QuitAfterShortDays { get; init; } = 3;
    /// <summary>Hired hands come from settlements within this many metres of the site (the nearest first).</summary>
    public int HireRadiusM { get; init; } = 6000;

    // ---------- materials on site (spec 4.3) ----------
    /// <summary>The hero unloads his pack (or a cart) into the pile within this many metres.</summary>
    public int PileReachM { get; init; } = 3;
    /// <summary>A haul job carries what one carrier bears at full speed: this permille of a strength-50 carrier's capacity (Encumbrance: 500 = 70 kg).</summary>
    public int HaulLoadPm { get; init; } = 500;
    public int HaulCarrierStrength { get; init; } = 50;
    /// <summary>Haul jobs stay on the board this many hours.</summary>
    public int HaulDeadlineHours { get; init; } = 48;
}
