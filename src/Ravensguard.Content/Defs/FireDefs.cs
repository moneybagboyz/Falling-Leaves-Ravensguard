namespace Ravensguard.Content;

/// <summary>
/// One fuel class of the fire rule (fire and burning 3, <c>fire/fuels.jsonc</c>): how a piece of this class ignites, spreads and ends. A piece's class is its own id when listed in
/// <see cref="Pieces"/>, else the first class that lists its material (and, when <see cref="Kinds"/> is not empty, its piece kind), else none. A class with no <see cref="IgnitionC"/>
/// never burns (stone, brick, slate, tile, iron, earth). Times are sim-seconds (1 sim-second = 20 game seconds = 1 real second at 1x).
/// </summary>
[ContentType("fuel_class")]
public sealed record FuelClassDef : ContentDef
{
    public required string Name { get; init; }
    /// <summary>The temperature the fuel catches at (null: it does not burn).</summary>
    public int? IgnitionC { get; init; }
    /// <summary>The chance, in permille per spread step (5 sim-s), that a burning neighbour at intensity 2 lights a touching cell of this class.</summary>
    public int SpreadPm { get; init; }
    /// <summary>How long a cell of this class burns before it is spent, sim-seconds.</summary>
    public int BurnS { get; init; }
    /// <summary>The highest intensity a cell of this class reaches (1 to 3).</summary>
    public int MaxIntensity { get; init; } = 3;
    /// <summary>What is left: ash, char, cracked, smoulder or scorched (the scar kind).</summary>
    public string After { get; init; } = "ash";
    public List<string> Materials { get; init; } = [];
    /// <summary>Piece kinds (snake case) the class is limited to for its materials; empty: any.</summary>
    public List<string> Kinds { get; init; } = [];
    /// <summary>Pieces that are this class whatever their material (a straw pallet, a hay stack).</summary>
    public List<string> Pieces { get; init; } = [];
    /// <summary>A roof of this class throws sparks downwind and is reached by them.</summary>
    public bool Sparks { get; init; }
}

/// <summary>
/// The fire rule's other tables (<c>fire/upkeep.jsonc</c>, fire 3 and 4.7): the yearly decay of a piece's condition by fuel class, the condition below which a roof leaks and the one a household
/// posts a repair at, and the goods classes that burn with their room.
/// </summary>
[ContentType("fire_upkeep")]
public sealed record FireUpkeepDef : ContentDef
{
    /// <summary>Condition lost a game year, permille, by fuel class id (thatch 50, daub 100, timber 10).</summary>
    public Dictionary<string, int> DecayPmPerYear { get; init; } = [];
    /// <summary>Below this a roof leaks (rain into the room) and its thatch catches at x1.5.</summary>
    public int LeakBelowPm { get; init; } = 300;
    /// <summary>Below this the household or steward posts a re-thatch or re-daub job.</summary>
    public int RepairBelowPm { get; init; } = 400;
    /// <summary>Good classes that burn with the room they stand in (goods burned go to the Burn sink).</summary>
    public List<string> BurnableGoodClasses { get; init; } = [];
}
