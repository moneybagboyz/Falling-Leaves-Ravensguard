namespace Ravensguard.Sim.World;

/// <summary>
/// The plan's scale model (docs/REDESIGN_PLAN.md section 1), defined once. Spike 1 derives every
/// published number from these. Production values will live in content/base/world.json.
/// </summary>
public static class ScaleConstants
{
    // Space
    public const double CellMeters = 1.0;
    public const int ChunkCells = 32;
    public const double TileMeters = 64.0;
    public const double RegionMeters = 32_000.0;
    public const double TownSpacingMeters = 8_000.0;
    public const double SquareMetersPerAcre = 4046.86;
    /// <summary>Street zoom: 1 m = 32 px (plan §1).</summary>
    public const int StreetPixelsPerMeter = 32;

    // Time
    public const double GameSecondsPerRealSecond = 20.0; // clock 1:20
    public const int DaysPerSeason = 15;
    public const int SeasonsPerYear = 4;
    public const int DaysPerYear = DaysPerSeason * SeasonsPerYear; // 60

    // Speeds, real metres per second
    public const double Walk = 1.5, Jog = 3.0, Cart = 2.5, Column = 1.3, HorseTrot = 5.0, HorseGallop = 12.0;

    // People and food
    public const int Population = 25_000;
    public const double PeoplePerHousehold = 4.8;
    public const double KgPerMeasure = 1.1;
    public const double MeasuresPerSownAcre = 48.0;
    public const double KgGrainPerSoldierDay = 1.0;
    public const double CartLoadKg = 500.0;

    // Levy: households per man
    public const int LevyHouseholdsPerManPeace = 10;
    public const int LevyHouseholdsPerManWar = 5;

    // Wages, crowns per day
    public const double WageMin = 1.0, WageMax = 2.0;

    public static readonly int[] TimeScales = { 1, 4, 8 };

    public const double SecondsPerGameDay = 86_400.0;
    public const double RealSecondsPerGameDay = SecondsPerGameDay / GameSecondsPerRealSecond; // 4320 = 72 min
}
