using Ravensguard.Sim.Core;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Sim.Lighting;

public enum DayPhase { Night, Dawn, Day, Dusk }

/// <summary>Outdoor light at one moment: sun and moon contributions (0-255 each) and the phase of the day.</summary>
public readonly record struct Ambient(int Sun, int Moon, DayPhase Phase)
{
    /// <summary>Outdoor level 0-255 (sun plus whatever moonlight the sun does not already outshine).</summary>
    public int Outdoor => Math.Min(255, Sun + Moon);
}

/// <summary>A radial light. Radius in cells, intensity 0-255 at the centre; falls off with the square of distance and is blocked by opaque cells.</summary>
public readonly record struct LightSource(int X, int Y, int Z, int Radius, int Intensity);

/// <summary>Plan section 2.7: sun by time of day and season (day length from the calendar), moon at night, nothing underground. Integer maths only.</summary>
public static class LightModel
{
    public const int TwilightHalfMinutes = 45;
    public const int MoonCycleDays = 30;
    public const int MoonMin = 22, MoonMax = 58;
    /// <summary>Indoor cells sit under a roof: they keep this percentage of the sun and of the moon.</summary>
    public const int IndoorSunPercent = 60, IndoorMoonPercent = 25;
    public const int TorchRadius = 6, TorchIntensity = 235;

    /// <summary>The ambient light at a moment. <paramref name="cloudPm"/> (P8-LM-3, bestiary 3.5) dims the sun by <c>cloud_pm x 4 / 10</c> permille (full overcast is 600 of a clear 1,000); 0 is a clear sky, the old behaviour.</summary>
    public static Ambient AmbientAt(GameTime t, int latitudeTenths, int cloudPm = 0)
    {
        int m = t.Hour * 60 + t.Minute;
        int rise = Daylight.SunriseMinute(latitudeTenths, t.DayOfYear), set = Daylight.SunsetMinute(latitudeTenths, t.DayOfYear);
        int up = Math.Clamp((m - (rise - TwilightHalfMinutes)) * 255 / (2 * TwilightHalfMinutes), 0, 255);
        int down = Math.Clamp(((set + TwilightHalfMinutes) - m) * 255 / (2 * TwilightHalfMinutes), 0, 255);
        int clearSun = Math.Min(up, down), sun = cloudPm > 0 ? Ravensguard.Sim.Weather.OutdoorLight.SunUnderCloud(clearSun, cloudPm) : clearSun;
        // moon: triangle wave over the cycle, full at the middle
        int d = (int)(t.TotalDays % MoonCycleDays);
        int fullness = MoonCycleDays / 2 - Math.Abs(d - MoonCycleDays / 2); // 0..15
        int moonFull = MoonMin + (MoonMax - MoonMin) * fullness / (MoonCycleDays / 2);
        int moon = moonFull * (255 - clearSun) / 255;                       // the moon is not dimmed by cloud here: a clear night is as it was
        var phase = clearSun >= 255 ? DayPhase.Day : clearSun <= 0 ? DayPhase.Night : (m < 12 * 60 ? DayPhase.Dawn : DayPhase.Dusk);
        return new Ambient(sun, moon, phase);
    }

    /// <summary>Ambient level of a cell: 0 underground, roofed cells keep part of it, open air all of it.</summary>
    public static int AmbientLevel(Ambient a, int z, bool indoor)
    {
        if (z < 0) return 0;
        if (indoor) return Math.Min(255, a.Sun * IndoorSunPercent / 100 + a.Moon * IndoorMoonPercent / 100);
        return a.Outdoor;
    }

    /// <summary>
    /// How far (in whole metres) something standing in this much light can be seen from afar: 1 m in the dark, 40 m in full daylight.
    /// A tile is visible when it lies within the radius for its own light level, so a lit window shows from far off and a dark yard only up close.
    /// </summary>
    public static int VisionRadius(int light) => 1 + Math.Clamp(light, 0, 255) * 39 / 255;
}
