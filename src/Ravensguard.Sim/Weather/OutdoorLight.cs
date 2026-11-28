using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Weather;

/// <summary>
/// The weather fields nobody read until P8-LM-3 (bestiary 3.5, phase-4 carry-over): a carried torch in the rain and the light of an overcast noon. Integer maths; the one draw is on the
/// stream 0x832 keyed on the game minute and the carrier.
/// <list type="bullet">
/// <item>A torch carried outdoors goes out with a chance of <c>rain_pm / 20</c> permille a game minute (heavy rain, 1,000 permille, is 50 a minute); under a roof or below ground it never does.</item>
/// <item>Outdoor light at noon is <c>1000 - cloud_pm x 4 / 10</c> permille of a clear noon (full overcast is 600); night and underground are as they were.</item>
/// <item>Rain is a share of heavy rain: <c>precipitation (tenths of mm/h) x 1000 / 25</c>, capped at 1,000 (2.5 mm/h is the weather settings' heavy rain).</item>
/// </list>
/// </summary>
public static class OutdoorLight
{
    /// <summary>Tenths of a millimetre an hour that count as heavy rain (the weather settings' <c>heavy_rain_mm_h_tenths</c>).</summary>
    public const int HeavyRainTenths = 25;

    /// <summary>Rain as permille of heavy rain, from the precipitation in tenths of mm an hour.</summary>
    public static int RainPm(int precipTenthsMmH, int heavyTenths = HeavyRainTenths) => Math.Clamp((int)((long)Math.Max(0, precipTenthsMmH) * 1000 / Math.Max(1, heavyTenths)), 0, 1000);

    /// <summary>Cloud cover as permille, from the weather sample's fixed-point cloud (0..1).</summary>
    public static int CloudPm(Fixed cloud) => Math.Clamp((int)(cloud.Raw * 1000 / Fixed.One.Raw), 0, 1000);

    /// <summary>The chance a carried torch goes out in one game minute, permille: <c>rain_pm / 20</c> in the open, 0 under a roof or below ground.</summary>
    public static int TorchOutPmPerMinute(int rainPm, bool sheltered) => sheltered ? 0 : Math.Clamp(rainPm, 0, 1000) / 20;

    /// <summary>Does the carrier's torch go out this game minute? One draw on 0x832 keyed on the minute and the carrier (the hero's id, a companion's).</summary>
    public static bool TorchGoesOut(ulong seed, long gameMinute, ulong carrier, int rainPm, bool sheltered)
    {
        int pm = TorchOutPmPerMinute(rainPm, sheltered);
        return pm > 0 && DetRng.Range(seed, gameMinute, carrier, RngStreams.TorchRainOut, 0, 0, 1000) < pm;
    }

    /// <summary>Outdoor light at noon as permille of a clear noon: <c>1000 - cloud_pm x 4 / 10</c>.</summary>
    public static int NoonLightPm(int cloudPm) => 1000 - Math.Clamp(cloudPm, 0, 1000) * 4 / 10;

    /// <summary>The sun's contribution (0..255) under cloud: the clear-sky value scaled by <see cref="NoonLightPm"/>, so full twilight and night stay as they were (a clear sky scales by 1).</summary>
    public static int SunUnderCloud(int clearSun, int cloudPm) => clearSun * NoonLightPm(cloudPm) / 1000;
}
