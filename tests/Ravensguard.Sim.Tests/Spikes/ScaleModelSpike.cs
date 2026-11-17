using Ravensguard.Sim.World;
using Xunit.Abstractions;
using S = Ravensguard.Sim.World.ScaleConstants;

namespace Ravensguard.Sim.Tests.Spikes;

/// <summary>
/// Spike 1: the scale-model notebook. Derived numbers are checked against the plan's stated values (docs/REDESIGN_PLAN.md §1),
/// and <see cref="PlanLiterals_PinTheConstants"/> pins the constants themselves to numbers typed in from the plan, so the
/// tests can never agree with the constants merely because both were edited together.
/// </summary>
public class ScaleModelSpike(ITestOutputHelper output)
{
    private static void Near(double expected, double actual, string what, double tol = 0.10)
        => Assert.True(Math.Abs(actual - expected) <= Math.Abs(expected) * tol, $"{what}: plan says {expected}, model gives {actual:F2}");

    private static double RealSec(double meters, double speed) => meters / speed;
    private static double GameHours(double realSec) => realSec * S.GameSecondsPerRealSecond / 3600.0;
    private static double GameDays(double realSec) => realSec * S.GameSecondsPerRealSecond / S.SecondsPerGameDay;

    [Fact]
    public void PlanLiterals_PinTheConstants()
    {
        // §1 table, typed in independently of ScaleConstants
        Assert.Equal(1.0, S.CellMeters);
        Assert.Equal(32, S.StreetPixelsPerMeter);        // "Cell: 1 m; 32 px at street zoom"
        Assert.Equal(32, S.ChunkCells);                  // "Chunk: 32 x 32 cells"
        Assert.Equal(64.0, S.TileMeters);                // "Region tile: 64 m"
        Assert.Equal(32_000.0, S.RegionMeters);          // "32 x 32 km"
        Assert.Equal(20.0, S.GameSecondsPerRealSecond);  // "1 real s = 20 game s"
        Assert.Equal(60, S.DaysPerYear);                 // "4 seasons x 15 days"
        Assert.Equal(15, S.DaysPerSeason);
        Assert.Equal(4, S.SeasonsPerYear);
        Assert.Equal(3.0, S.Jog);
        Assert.Equal(1.5, S.Walk); Assert.Equal(2.5, S.Cart); Assert.Equal(1.3, S.Column);
        Assert.Equal(5.0, S.HorseTrot); Assert.Equal(12.0, S.HorseGallop);
        Assert.Equal(25_000, S.Population);
        Assert.Equal(new[] { 1, 4, 8 }, S.TimeScales);

        // Derived from the literals above by hand, not from the constants:
        // a game day is 86,400 game s = 4,320 real s = 72 real min at 1x (18 at 4x)
        Assert.Equal(4320.0, S.RealSecondsPerGameDay);
        Assert.Equal(72 * 60, S.RealSecondsPerGameDay);
        // 8 km at 3 m/s = 2,666.67 s = 44.4 real min
        Assert.Equal(44.44, S.TownSpacingMeters / 3.0 / 60.0, 2);
        // one game hour = 3,600 game s / 20 = 180 real s = 3 real min
        Assert.Equal(180.0, 3600.0 / S.GameSecondsPerRealSecond);
        // a year is 60 days x 72 min = 72 real hours
        Assert.Equal(72.0, 60 * 72 / 60.0);
        Assert.Equal(60 * 72.0 / 60.0, S.DaysPerYear * S.RealSecondsPerGameDay / 3600.0);
        // 32,000 m region = 1,000 chunks of 32 m, 500 tiles of 64 m
        Assert.Equal(1000, 32_000 / 32);
        Assert.Equal(500, 32_000 / 64);
    }

    [Fact]
    public void ClockAndCalendar()
    {
        // the plan's literals, computed by hand: 1 game hour = 3,600 / 20 = 180 real s
        Near(3, 3600.0 / 20.0 / 60.0, "real minutes per game hour at 1x", 0.01);
        Near(3, 3600.0 / S.GameSecondsPerRealSecond / 60.0, "real minutes per game hour at 1x (constants)", 0.01);
        Near(72, 86_400.0 / 20.0 / 60.0, "real min per game day at 1x (by hand)", 0.001);
        Near(72, S.RealSecondsPerGameDay / 60, "real min per game day at 1x");
        Near(18, S.RealSecondsPerGameDay / 60 / 4, "real min per game day at 4x");
        Near(72, 60 * 72.0 / 60.0, "real hours per year at 1x (by hand)", 0.001);
        Near(72, S.DaysPerYear * S.RealSecondsPerGameDay / 3600, "real hours per year at 1x");
        Assert.Equal(60, S.DaysPerYear);
    }

    [Fact]
    public void RegionGeometry()
    {
        double tiles = S.RegionMeters / S.TileMeters, chunks = S.RegionMeters / (S.ChunkCells * S.CellMeters);
        Assert.Equal(500, tiles); Assert.Equal(1000, chunks);
        double sec = RealSec(S.RegionMeters, S.Jog);
        Near(180, sec / 60, "cross region jogging at 1x (min)", 0.10);
        Near(22, sec / 60 / 8, "cross region at 8x (min)", 0.10);
        Near(2.5, GameDays(sec), "cross region (game days)", 0.10);
        Near(1.0, S.TileMeters * S.TileMeters / S.SquareMetersPerAcre, "tile ~ 1 acre", 0.15);
    }

    [Fact]
    public void TownToTown_Trips()
    {
        var rows = new List<string>
        {
            "| Mode | m/s | real min 1x | 4x | 8x | game hours |", "|---|---|---|---|---|---|"
        };
        foreach (var (name, v) in new[] { ("walk", S.Walk), ("jog", S.Jog), ("cart", S.Cart), ("horse trot", S.HorseTrot), ("gallop", S.HorseGallop) })
        {
            double s = RealSec(S.TownSpacingMeters, v);
            rows.Add($"| {name} | {v} | {s / 60:F1} | {s / 60 / 4:F1} | {s / 60 / 8:F1} | {GameHours(s):F1} |");
        }
        output.WriteLine(string.Join("\n", rows));
        Near(44, 8000.0 / 3.0 / 60.0, "8 km at 3 m/s (by hand, min)", 0.02);
        double jog = RealSec(S.TownSpacingMeters, S.Jog);
        Near(44, jog / 60, "8 km jogging 1x (min)");
        Near(5.5, jog / 60 / 8, "8 km jogging 8x (min)");
        Near(15, GameHours(jog), "8 km jogging (game hours)");
    }

    [Fact]
    public void FoodAndWagesPerTrip()
    {
        var sb = new List<string> { "| Mode | game days | food measures | grain kg | wages 1 crown/day | wages 2 crowns/day |", "|---|---|---|---|---|---|" };
        foreach (var (name, v) in new[] { ("jog", S.Jog), ("cart", S.Cart), ("horse trot", S.HorseTrot) })
        {
            double days = GameDays(RealSec(S.TownSpacingMeters, v));
            sb.Add($"| {name} | {days:F2} | {days:F2} | {days * S.KgPerMeasure:F2} | {days * S.WageMin:F2} | {days * S.WageMax:F2} |");
        }
        output.WriteLine(string.Join("\n", sb));
        double jogDays = GameDays(RealSec(S.TownSpacingMeters, S.Jog));
        // "food and wages matter on every trip": a one-way trip costs a meaningful fraction (>half) of a day's ration/wage
        Assert.InRange(jogDays, 0.5, 0.75);
        // Round trip with a cart = ~1.5 game days of food for the driver
        Assert.InRange(2 * GameDays(RealSec(S.TownSpacingMeters, S.Cart)), 1.3, 1.7);
    }

    [Fact]
    public void ArmyMarchAndSupply()
    {
        double sec = RealSec(20_000, S.Column);
        double days = GameDays(sec);
        output.WriteLine($"column 20 km: {sec / 60:F0} real min at 1x ({sec / 60 / 8:F0} at 8x), {GameHours(sec):F1} game hours = {days:F2} game days");
        Near(256, sec / 60, "20 km column (real min)", 0.02);
        double kg = 1000 * 15 * S.KgGrainPerSoldierDay;
        double carts = Math.Ceiling(kg / S.CartLoadKg);
        output.WriteLine($"supply 1000 men x 15 days: {kg / 1000:F1} t, {carts} carts of {S.CartLoadKg} kg");
        Assert.Equal(15.0, kg / 1000);
        Assert.Equal(30, carts);
    }

    [Fact]
    public void PopulationLevyAndFarmland()
    {
        double households = S.Population / S.PeoplePerHousehold;
        double peace = households / S.LevyHouseholdsPerManPeace, war = households / S.LevyHouseholdsPerManWar;
        output.WriteLine($"households {households:F0}; levy peace {peace:F0}, war {war:F0}");
        // Plan 1: "~250-500 men (1 per 10 households; 1 per 5 in war)". Peace matches the top of that band.
        Near(500, peace, "peace levy vs plan's top of range");
        // FINDING: the war levy (~1,040) is double the plan's stated range. Pinned so a plan edit is a conscious choice.
        Assert.InRange(war, 1000, 1100);

        double measuresPerPersonYear = S.DaysPerYear; // 1 measure per person-day, 60-day year
        double acresBare = measuresPerPersonYear / S.MeasuresPerSownAcre;
        double planAcres = 2.3;
        double overhead = planAcres / acresBare;
        output.WriteLine($"acres/person bare {acresBare:F2}; plan {planAcres}; implied overhead x{overhead:F2}; farmland bare {acresBare * S.Population:F0}, plan {planAcres * S.Population:F0} ac");
        // Bare need is 1.25 ac/person (31k acres). The plan's 2.3 ac needs x1.84 for seed, fodder, spoilage, fallow: assert plausibility, not equality.
        Assert.InRange(overhead, 1.5, 2.2);
        Near(58_000, planAcres * S.Population, "plan farmland acres");
        double regionAcres = S.RegionMeters * S.RegionMeters / S.SquareMetersPerAcre;
        output.WriteLine($"region {regionAcres:F0} ac; farmland share {planAcres * S.Population / regionAcres:P0}");
    }
}
