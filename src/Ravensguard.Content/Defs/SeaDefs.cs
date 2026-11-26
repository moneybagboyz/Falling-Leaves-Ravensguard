namespace Ravensguard.Content;

/// <summary>
/// Sailing, sea state, lee shores and harbours (ships-and-naval-war 4.1 to 4.3; P7-SH-B; <c>naval/sea.jsonc</c>, one <c>sea_rules</c> row). Millimetres, mm/s, newtons, litres, <c>_pm</c> permille; seconds are the sim's
/// (a tick is a second of the 20x scale the voyages use).
/// </summary>
[ContentType("sea_rules")]
public sealed record SeaRulesDef : ContentDef
{
    /// <summary>A square sail's yards brace round this many degrees: the push is <c>cos(angle off astern - brace)</c>, and none with the wind forward of the beam.</summary>
    public int BraceDeg { get; init; } = 45;
    /// <summary>Hull speed is this permille of the square root of the waterline in metres (1.25) in m/s.</summary>
    public int HullSpeedPm { get; init; } = 1250;
    /// <summary>Leeway on a beam reach, permille of the speed; shallow-keeled ships (the longship) slip more.</summary>
    public int LeewayPm { get; init; } = 150;
    public int LeewayShallowPm { get; init; } = 250;
    /// <summary>A slender hull (length over beam, in tenths, at least this) drags at this permille of the wetted area the Phase 5 rule gives (the longship's long lean lines; a cog's round hull keeps all of it).</summary>
    public int SlenderLbX10 { get; init; } = 40;
    public int SlenderDragPm { get; init; } = 450;
    /// <summary>A draft under this (mm) loaded makes a ship shallow-keeled.</summary>
    public int ShallowDraftMm { get; init; } = 1000;
    /// <summary>Rudder turn rate in degrees a second at 3 m/s: a rudder, a steering oar; and a ship's oars turn it on the spot at this many degrees a second.</summary>
    public int RudderDegS { get; init; } = 3;
    public int SteeringOarDegS { get; init; } = 6;
    public int SpotTurnDegS { get; init; } = 10;
    /// <summary>Rowers' stamina (permille) falls one point every this many seconds at an easy stroke and every <see cref="HardSecPerPoint"/> on a hard one; their power is <c>floor + (1000 - floor) x stamina / 1000</c>; they rest at this many seconds a point.</summary>
    public int EasySecPerPoint { get; init; } = 6;
    public int HardSecPerPoint { get; init; } = 1;
    public int RestSecPerPoint { get; init; } = 2;
    public int PowerFloorPm { get; init; } = 400;
    /// <summary>Waves in mm: <c>wind m/s x min(fetch km, cap) x 1000 / divisor</c> (10 m/s over 50 km: 2 m).</summary>
    public int WaveDiv { get; init; } = 250;
    public int FetchCapKm { get; init; } = 50;
    /// <summary>A beam sea: the waves come within this permille of the sine of the angle across the hull (707: 45 degrees either side of abeam).</summary>
    public int BeamSeaSinPm { get; init; } = 707;
    /// <summary>A storm from this wind; a ship under bare poles drifts at this permille of the wind speed.</summary>
    public int StormMps { get; init; } = 17;
    public int StormDriftPm { get; init; } = 40;
    /// <summary>On rocks each hull cell breaks with this permille (stream 0x7D1); the ship is wrecked from this many broken cells.</summary>
    public int WreckPm { get; init; } = 250;
    public int WreckMinCells { get; init; } = 2;
    /// <summary>A beachable ship runs onto sand at this speed (mm/s); a ship warped into a harbour by two men on a rope moves at this speed.</summary>
    public int BeachMmps { get; init; } = 1000;
    public int WarpMmps { get; init; } = 300;
    /// <summary>A porter carries this many kg a trip of this many seconds; a treadwheel crane lifts this many kg a lift of this many seconds.</summary>
    public int PorterKg { get; init; } = 30;
    public int PorterTripSec { get; init; } = 90;
    public int CraneKg { get; init; } = 500;
    public int CraneLiftSec { get; init; } = 120;
    /// <summary>The customs on a cargo's value at a harbour with no realm rate, percent.</summary>
    public int CustomsPct { get; init; } = 5;
    /// <summary>A beach is sand where the tile's slope is under this (permille) and its height under <see cref="BeachMaxHeightCm"/>; steeper shore is rock.</summary>
    public int BeachSlopePm { get; init; } = 80;
    public int BeachMaxHeightCm { get; init; } = 300;
    // ---- P7-SH-C: sea edges and sea raids (ships-and-naval-war 3.3, 4.4, 4.5)
    /// <summary>Culture ids whose realms raid by sea (<c>raids_by_sea</c>, kept here so no culture row changes).</summary>
    public List<string> RaidCultures { get; init; } = [];
    /// <summary>Permille a season a raider-culture realm raids in peace (overview 10a Q7).</summary>
    public int RaidInPeacePm { get; init; } = 300;
    /// <summary>A raid is 1 to this many longships of this many men each, a tenth to a sixth of them (<see cref="GuardsMin"/> to <see cref="GuardsMax"/>) staying as ship guards.</summary>
    public int RaidShipsMax { get; init; } = 4;
    public int RaidMenMin { get; init; } = 30;
    public int RaidMenMax { get; init; } = 60;
    public int GuardsMin { get; init; } = 6;
    public int GuardsMax { get; init; } = 10;
    /// <summary>A target is weakly guarded with at most this many known armed men; each man known costs the raid this many mites of its score; a day's sail is this many km.</summary>
    public int WeakGuards { get; init; } = 12;
    public int RiskMitesPerGuard { get; init; } = 3000;
    public int SailKmPerDay { get; init; } = 100;
    /// <summary>The raiders beach at this hour of the day (dawn).</summary>
    public int LandHourOfDay { get; init; } = 5;
    /// <summary>The raid's plan: game hours ashore before the withdrawal (less when the bell brings a warband), and the hours the run to the village and the run back take.</summary>
    public int RaidHours { get; init; } = 4;
    public int RunHours { get; init; } = 1;
    /// <summary>The bell carries this far (m); a rider covers this many metres an hour.</summary>
    public int BellM { get; init; } = 3000;
    public int RideMPerHour { get; init; } = 8000;
    /// <summary>What the raiders take: kilograms of grain from each robbed household, the share of the households robbed and of each one's coin (percent), at most this many captives (where the law allows), and barns burned.</summary>
    public int LootGrainKg { get; init; } = 60;
    public int LootHouseholdPct { get; init; } = 60;
    public int LootCoinPct { get; init; } = 50;
    public int CaptivesMax { get; init; } = 6;
    public int BurnMax { get; init; } = 1;
    /// <summary>The grain a mite buys, for scoring a village's stores (mites a kilogram).</summary>
    public int GrainMitesPerKg { get; init; } = 12;
}
