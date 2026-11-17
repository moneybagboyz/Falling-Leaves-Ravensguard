using Ravensguard.Sim.Core;
using Ravensguard.Sim.Lighting;

namespace Ravensguard.Sim.Tests.Lighting;

public class LightTests
{
    private const int Lat = 480;
    private static GameTime At(int dayOfSeason, int hour, int minute = 0, Season s = Season.Spring) => GameTime.FromCalendar(1, s, dayOfSeason, hour, minute);

    private static LightGrid Room()
    {
        // 20 x 11 field, a wall at x=10 from y=0..10 except a door gap at y=5
        var g = new LightGrid(20, 11, -1, 1);
        for (int y = 0; y < 11; y++) if (y != 5) g.SetOpaque(10, y, 0, true);
        g.SetStaticLights([]);
        return g;
    }

    [Fact]
    public void NightIsDarkerThanDay()
    {
        var day = LightModel.AmbientAt(At(8, 12), Lat);
        var night = LightModel.AmbientAt(At(8, 0), Lat);
        Assert.Equal(DayPhase.Day, day.Phase);
        Assert.Equal(DayPhase.Night, night.Phase);
        Assert.True(day.Outdoor > 3 * night.Outdoor, $"day {day.Outdoor} night {night.Outdoor}");
        Assert.True(night.Outdoor > 0, "moonlight keeps the night above zero");
        var g = Room();
        Assert.True(g.Compute(day).Total(3, 3, 0) > g.Compute(night).Total(3, 3, 0));
    }

    [Fact]
    public void DuskAndDawnAreBetween()
    {
        var dawn = LightModel.AmbientAt(At(8, 6, 0), Lat); // equinox: sunrise at 06:00
        Assert.True(dawn.Sun is > 0 and < 255);
        Assert.Equal(DayPhase.Dawn, dawn.Phase);
        var dusk = LightModel.AmbientAt(At(8, 18, 0), Lat);
        Assert.Equal(DayPhase.Dusk, dusk.Phase);
    }

    [Fact]
    public void SeasonChangesDayLength()
    {
        // 19:00 is still light at midsummer in the north and dark at midwinter
        var summer = LightModel.AmbientAt(At(8, 20, 0, Season.Summer), Lat);
        var winter = LightModel.AmbientAt(At(8, 17, 30, Season.Winter), Lat);
        Assert.True(summer.Sun > 0);
        Assert.Equal(0, winter.Sun);
    }

    [Fact]
    public void FullMoonIsBrighterThanNewMoon()
    {
        int Moon(int day) => LightModel.AmbientAt(GameTime.FromGameDays(day), Lat).Moon;
        Assert.True(Moon(15) > Moon(0));
    }

    [Fact]
    public void UndergroundGetsNoAmbientLight()
    {
        var day = LightModel.AmbientAt(At(8, 12), Lat);
        Assert.Equal(0, LightModel.AmbientLevel(day, -1, false));
        Assert.Equal(0, LightModel.AmbientLevel(day, -3, true));
        var g = Room();
        Assert.Equal(0, g.Compute(day).Total(5, 5, -1));
    }

    [Fact]
    public void RoofedCellsKeepPartOfTheSun()
    {
        var day = LightModel.AmbientAt(At(8, 12), Lat);
        Assert.True(LightModel.AmbientLevel(day, 0, true) < LightModel.AmbientLevel(day, 0, false));
        Assert.True(LightModel.AmbientLevel(day, 0, true) > 0);
    }

    [Fact]
    public void TorchLightsItsRadiusAndNothingFarther()
    {
        var g = Room();
        var night = new Ambient(0, 0, DayPhase.Night);
        var f = g.Compute(night, [new LightSource(3, 5, 0, 6, 235)]);
        Assert.True(f.Total(3, 5, 0) >= 230);
        Assert.True(f.Total(6, 5, 0) > 0);
        Assert.True(f.Total(3, 5, 0) > f.Total(6, 5, 0) && f.Total(6, 5, 0) > f.Total(8, 5, 0));
        Assert.Equal(0, f.Point(3 + 7, 5, 0)); // beyond radius 6
        Assert.Equal(0, f.Point(3, 5 + 6, 0)); // exactly at the radius the falloff has reached zero
    }

    [Fact]
    public void TorchRadiusIsAboutSixMetres()
    {
        var g = new LightGrid(30, 30, 0, 0); g.SetStaticLights([]);
        var f = g.Compute(new Ambient(0, 0, DayPhase.Night), [new LightSource(15, 15, 0, LightModel.TorchRadius, LightModel.TorchIntensity)]);
        Assert.True(f.Point(15 + 5, 15, 0) > 0);
        Assert.Equal(0, f.Point(15 + 6, 15, 0)); // intensity at exactly r is zero (falloff reaches 0)
        Assert.Equal(0, f.Point(15 + 7, 15, 0));
        Assert.Equal(0, f.Point(15, 15 - 7, 0));
    }

    [Fact]
    public void WallsBlockLightButADoorwayPassesIt()
    {
        var g = Room();
        var night = new Ambient(0, 0, DayPhase.Night);
        // torch right beside the wall at (8,2): the wall cell itself is lit, the cells behind it are not
        var f = g.Compute(night, [new LightSource(8, 2, 0, 6, 255)]);
        Assert.True(f.Point(9, 2, 0) > 0);
        Assert.True(f.Point(10, 2, 0) > 0, "the wall face is lit");
        Assert.Equal(0, f.Point(11, 2, 0));
        Assert.Equal(0, f.Point(12, 2, 0));
        Assert.Equal(0, f.Point(12, 1, 0));
        // torch in line with the gap lights the far side
        var f2 = g.Compute(night, [new LightSource(8, 5, 0, 6, 255)]);
        Assert.True(f2.Point(12, 5, 0) > 0);
        // behind the wall next to the gap stays dark
        Assert.Equal(0, f2.Point(11, 1, 0));
    }

    [Fact]
    public void StaticLightsPersistAndDynamicOnesAreAddedOnTop()
    {
        var g = new LightGrid(20, 11, 0, 0);
        g.SetStaticLights([new LightSource(5, 5, 0, 5, 200)]);
        var night = new Ambient(0, 0, DayPhase.Night);
        var a = g.Compute(night);
        Assert.True(a.Point(5, 5, 0) >= 195);
        var b = g.Compute(night, [new LightSource(6, 5, 0, 5, 200)]);
        Assert.True(b.Point(5, 5, 0) > a.Point(5, 5, 0));
        Assert.Equal(255, g.Compute(night, [new LightSource(5, 5, 0, 5, 255), new LightSource(5, 5, 0, 5, 255)]).Total(5, 5, 0));
    }

    [Fact]
    public void LightsStayOnTheirOwnLevel()
    {
        var g = new LightGrid(10, 10, -1, 1); g.SetStaticLights([]);
        var f = g.Compute(new Ambient(0, 0, DayPhase.Night), [new LightSource(5, 5, 0, 5, 255)]);
        Assert.Equal(0, f.Total(5, 5, 1));
        Assert.Equal(0, f.Total(5, 5, -1));
    }

    [Fact]
    public void VisionRadiusGrowsWithLight()
    {
        Assert.Equal(1, LightGrid.VisionRadius(0));
        Assert.Equal(40, LightGrid.VisionRadius(255));
        Assert.True(LightGrid.VisionRadius(40) is >= 5 and <= 9, "moonlight shows a few metres");
        for (int l = 1; l < 256; l++) Assert.True(LightGrid.VisionRadius(l) >= LightGrid.VisionRadius(l - 1));
    }

    [Fact]
    public void ComputeIsDeterministic()
    {
        var g = Room();
        var a = g.Compute(LightModel.AmbientAt(At(3, 21), Lat), [new LightSource(4, 4, 0, 6, 235)]);
        var b = g.Compute(LightModel.AmbientAt(At(3, 21), Lat), [new LightSource(4, 4, 0, 6, 235)]);
        Assert.True(a.AmbientSpan.SequenceEqual(b.AmbientSpan) && a.PointSpan.SequenceEqual(b.PointSpan));
    }
}
