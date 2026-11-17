using Ravensguard.Sim.World;
using Xunit.Abstractions;
using S = Ravensguard.Sim.World.ScaleConstants;

namespace Ravensguard.Sim.Tests.Spikes;

/// <summary>Spike 1: the scale-model notebook. Every number derives from ScaleConstants; the plan's stated values are asserted within 10%.</summary>
public class ScaleModelSpike(ITestOutputHelper output)
{
    private static void Near(double expected, double actual, string what, double tol = 0.10)
        => Assert.True(Math.Abs(actual - expected) <= Math.Abs(expected) * tol, $"{what}: plan says {expected}, model gives {actual:F2}");

    private static double RealSec(double meters, double speed) => meters / speed;
    private static double GameHours(double realSec) => realSec * S.GameSecondsPerRealSecond / 3600.0;
    private static double GameDays(double realSec) => realSec * S.GameSecondsPerRealSecond / S.SecondsPerGameDay;

    [Fact]
    public void ClockAndCalendar()
    {
        Near(3, 3600.0 / S.GameSecondsPerRealSecond / 60.0 , "real minutes per game hour at 1x", 0.01);
        Near(72, S.RealSecondsPerGameDay / 60, "real min per game day at 1x");
        Near(18, S.RealSecondsPerGameDay / 60 / 4, "real min per game day at 4x");
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
