namespace Ravensguard.Content;

/// <summary>
/// P7-LB1 (large-buildings 4.1, 4.2, LBQ-1; content <c>content/base/build/water_power.jsonc</c>): the constants of water, wind and animal power. Every number is content; the defaults are the
/// shipped values. Lengths in metres, heads in millimetres, power in watts, flow in litres a second, shares in permille.
/// </summary>
[ContentType("water_power_rules")]
public sealed record WaterPowerRulesDef : ContentDef
{
    /// <summary>Mean flow a km2 of catchment (litres a second; LBQ-1: 15 for a temperate land).</summary>
    public int FlowLpsPerKm2 { get; init; } = 15;
    /// <summary>The season's share of the mean flow in permille: spring, summer, autumn, winter.</summary>
    public List<int> SeasonPm { get; init; } = [1600, 600, 1000, 800];
    /// <summary>Ice: after this many days below <see cref="FrozenBelowC"/> the flow stops, until <see cref="ThawDays"/> days above <see cref="ThawAboveC"/>.</summary>
    public int FrozenDays { get; init; } = 3;
    public int FrozenBelowC { get; init; } = -5;
    public int ThawDays { get; init; } = 2;
    public int ThawAboveC { get; init; }
    /// <summary>The longest look back for the ice rule (days).</summary>
    public int IceLookbackDays { get; init; } = 120;
    /// <summary>A river-high day multiplies the flow (permille) and brings the flood risk.</summary>
    public int RiverHighFlowPm { get; init; } = 2000;
    /// <summary>With no leat, the head is the fall of the waterway this many metres upstream of the wheel.</summary>
    public int NoLeatFallM { get; init; } = 30;
    /// <summary>The longest leat (metres) and the most head it may give (mm).</summary>
    public int LeatMaxM { get; init; } = 300;
    public int LeatHeadCapMm { get; init; } = 4000;
    /// <summary>A weir adds this head on a stream no wider than <see cref="WeirMaxWidthCm"/>.</summary>
    public int WeirHeadMm { get; init; } = 1000;
    public int WeirMaxWidthCm { get; init; } = 1000;
    /// <summary>A wheel (or a plan's wheel) is on a bank when its cell lies within this many metres of the bank (half the width plus this).</summary>
    public int BankReachM { get; init; } = 6;
    /// <summary>A shaft run reaches at most this far from the wheel (metres, cells along the run).</summary>
    public int ShaftReachM { get; init; } = 12;
    /// <summary>A machine couples to the nearest station piece of its kind within this many metres.</summary>
    public int CoupleReachM { get; init; } = 3;
    /// <summary>A water-site search samples a waterway every this many metres; sites in the list lie at least <see cref="SiteSpacingM"/> apart and give at least <see cref="SiteMinW"/> in autumn.</summary>
    public int SiteSampleM { get; init; } = 32;
    public int SiteSpacingM { get; init; } = 200;
    public int SiteMinW { get; init; } = 2000;
    /// <summary>Wear: hit points lost on a running day, the chance (permille) of a breakdown on a running day and its share of the piece's hit points; below this share of hit points a machine runs at half effect.</summary>
    public int WearHpPerDay { get; init; } = 1;
    public int BreakdownPm { get; init; } = 2;
    public int BreakdownLossPct { get; init; } = 20;
    public int HalfEffectBelowPct { get; init; } = 50;
    /// <summary>A river-high day with the sluice open: the chance (percent) of flood damage to a wheel and its share of hit points.</summary>
    public int FloodPct { get; init; } = 5;
    public int FloodLossPct { get; init; } = 30;
    /// <summary>Wind: <c>P = WindK x v^3</c> for v in m/s (a 20 m rotor), nothing below <see cref="WindMinMps"/> or above <see cref="WindMaxMps"/> (sails furled), capped by the mill's rating.</summary>
    public int WindK { get; init; } = 19;
    public int WindMinMps { get; init; } = 3;
    public int WindMaxMps { get; init; } = 15;
    /// <summary>A windmill needs open ground: no building within this many metres upwind (west).</summary>
    public int WindClearM { get; init; } = 30;
}

/// <summary>
/// P7-LB1 (large-buildings 3.2, 4.1-4.3; content <c>content/base/build/power_pieces.jsonc</c>): what a build piece does in a power net. <see cref="Piece"/> is the build piece; its role is
/// <c>wheel</c> (water), <c>windmill</c>, <c>gin</c> (animals), <c>shaft</c>, <c>machine</c>, <c>switch</c> (the sluice), <c>weir</c>, <c>leat</c> or <c>crane</c> (human power: no watts).
/// </summary>
[ContentType("power_piece")]
public sealed record PowerPieceDef : ContentDef
{
    [ContentRef(typeof(BuildPieceDef))] public required string Piece { get; init; }
    public required string Role { get; init; }
    /// <summary>A wheel: efficiency in permille and the rated output in watts. A windmill or gin: the rated (capped) output.</summary>
    public int EtaPm { get; init; }
    public int RatedW { get; init; }
    /// <summary>A wheel: the head band in mm (a wheel outside it gives nothing; 0 max: no upper limit), the water it needs (<c>any</c> or <c>river</c>) and whether it needs a leat.</summary>
    public int HeadMinMm { get; init; }
    public int HeadMaxMm { get; init; }
    public string Water { get; init; } = "any";
    public bool NeedsLeat { get; init; }
    /// <summary>A machine: the watts it draws while working, the piece ids of the station it couples to (empty: none) and whether its shaft must be a cam shaft.</summary>
    public int DrawW { get; init; }
    public List<string> Couples { get; init; } = [];
    public bool NeedsCam { get; init; }
    /// <summary>The station's <c>labour_pm</c> while this machine is fully powered (1000: no change), and the station's capacity multiplier (a great bellows doubles a bloomery: 2).</summary>
    public int LabourPm { get; init; } = 1000;
    public int CapacityMult { get; init; } = 1;
    /// <summary>False for a machine a windmill's net may not drive (only millstones and drainage pumps take wind power).</summary>
    public bool WindOk { get; init; }
}
