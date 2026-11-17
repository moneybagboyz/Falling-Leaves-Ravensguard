using Ravensguard.Sim.Core;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.WorldGen;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Weather;

/// <summary>Phase 1 review: snow depth used to restart from zero every 16 days, so the depth jumped at each anchor.</summary>
public class SnowDepthTests(ITestOutputHelper output)
{
    private static readonly Lazy<Continent> Cont = new(() =>
    {
        var s = ContinentSettings.LoadDefaults(ContinentSettings.FindBaseContent());
        s.Seed = 1234; s.Layout = ContinentLayout.Continents;
        return ContinentGenerator.Generate(s);
    });

    private static RegionWeather Make() => RegionWeather.ForContinent(Cont.Value, 77, WeatherSettings.LoadDefaults(ContinentSettings.FindBaseContent()));

    private static IEnumerable<int> ColdLandCells(int count)
    {
        var c = Cont.Value;
        var cold = Enumerable.Range(0, Continent.Cells).Where(i => !c.Ocean[i] && !c.Lake[i] && c.Temperature[i] is < 60 and > -80).ToList();
        for (int k = 0; k < count; k++) yield return cold[k * cold.Count / count];
    }

    [Fact]
    public void Snow_depth_changes_continuously_from_day_to_day()
    {
        var w = Make();
        int jumps = 0, daysWithSnow = 0, maxStep = 0; long worstDay = 0; int worstCell = 0;
        foreach (int cell in ColdLandCells(14))
        {
            Fixed prev = w.SnowDepthEndOfDay(cell, 0);
            for (long day = 1; day < 60 * 4; day++)
            {
                var d = w.SnowDepthEndOfDay(cell, day);
                if (d.Raw > 0) daysWithSnow++;
                int step = Math.Abs((d - prev).Floor());
                // a big change is only allowed on a heavy snowfall day (or a thaw as fast as the model allows: no more than 30 cm a day)
                bool heavySnow = w.DailySnowCm(cell, day).Floor() >= 25 || w.DailyRainMm(cell, day).Floor() >= 40; // heavy snowfall, or a downpour on the snowpack
                if (step > maxStep && !heavySnow) { maxStep = step; worstDay = day; worstCell = cell; }
                if (step > 30 && !heavySnow) jumps++;
                prev = d;
            }
        }
        output.WriteLine($"{daysWithSnow} cell-days with snow; largest non-snowfall change {maxStep} cm (cell {worstCell}, day {worstDay})");
        Assert.True(daysWithSnow > 200, "the sample should include snowy ground");
        Assert.Equal(0, jumps);
    }

    [Fact]
    public void Each_day_follows_from_the_day_before_including_across_every_16_day_and_year_boundary()
    {
        var w = Make();
        int checkedDays = 0;
        foreach (int cell in ColdLandCells(10))
            for (long day = 1; day < 60 * 3; day++)
            {
                var prev = w.SnowDepthEndOfDay(cell, day - 1);
                var t = w.DailyMeanTemp(cell, day);
                var expect = prev * WeatherMath.F(97, 100) + w.DailySnowCm(cell, day);
                if (t > Fixed.Zero) expect -= t * WeatherMath.F(15, 10) + w.DailyRainMm(cell, day) * WeatherMath.F(3, 10);
                expect = Fixed.Clamp(expect, Fixed.Zero, Fixed.FromInt(200));
                Assert.True(expect == w.SnowDepthEndOfDay(cell, day), $"cell {cell} day {day}: expected {expect.ToDouble():0.00}, got {w.SnowDepthEndOfDay(cell, day).ToDouble():0.00}");
                checkedDays++;
            }
        Assert.True(checkedDays > 1500);
    }

    [Fact]
    public void Snow_depth_does_not_depend_on_what_was_asked_before()
    {
        var a = Make(); var b = Make();
        foreach (int cell in ColdLandCells(5))
        {
            // a asked day by day, b jumps straight to each day (and backwards): same answers
            var seq = new List<Fixed>();
            for (long day = 100; day < 190; day++) seq.Add(a.SnowDepthEndOfDay(cell, day));
            for (long day = 189; day >= 100; day -= 7) Assert.Equal(seq[(int)(day - 100)], b.SnowDepthEndOfDay(cell, day));
        }
    }
}
