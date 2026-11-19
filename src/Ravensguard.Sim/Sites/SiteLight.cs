using Ravensguard.Content;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Sim.Sites;

public enum LightKind : byte { None, Torch, Candle, Lantern }

/// <summary>
/// A carried light (exploration-and-sites 3.2 <c>Torch</c>): its kind, the fuel left in milli-sim-seconds (one sim-second is 20 crawl steps, so a step burns 50 at the plain rate)
/// and whether it burns. A snuffed light loses nothing. Integer only: the 1:20 world clock and the speed setting never shorten it (EX-5).
/// </summary>
public struct CarriedLight
{
    public LightKind Kind;
    public long LeftMilliS;
    public bool Lit;

    public static CarriedLight Torch(SiteConstantsDef k) => new() { Kind = LightKind.Torch, LeftMilliS = k.TorchBurnS * 1000L };
    public static CarriedLight Candle(SiteConstantsDef k) => new() { Kind = LightKind.Candle, LeftMilliS = k.CandleBurnS * 1000L };
    /// <summary>A lantern with this much oil: 3,600 sim-seconds per 0.3 l.</summary>
    public static CarriedLight Lantern(SiteConstantsDef k, int oilMl) => new() { Kind = LightKind.Lantern, LeftMilliS = (long)k.LanternBurnS * 1000L * oilMl / 300 };
    public bool Out => Kind == LightKind.None || LeftMilliS <= 0;
    public int LeftS => (int)(LeftMilliS / 1000);
}

/// <summary>What the air does at an index (4.5.1, EX-7).</summary>
public readonly record struct AirEffect(int CandleRadiusPm, int TorchRadiusPm, int RegenPm, int AccuracyPoints, bool Dazed, int SpeedPm, bool FlameOut, bool Foul, bool Warning);

/// <summary>Light, vision and air as mechanics (4.4, 4.5.1, 4.8): burn, guttering, the light a carried flame casts, vision by light, the hit modifier of a target's light, the air's effects.</summary>
public static class SiteLight
{
    /// <summary>Burn one crawl step (a twentieth of a sim-second) at <paramref name="burnPm"/> (rain outdoors x1.5 or x2.5). True when the light just went out.</summary>
    public static bool Burn(ref CarriedLight l, int burnPm = 1000)
    {
        if (!l.Lit || l.Out) return false;
        l.LeftMilliS -= 50L * burnPm / 1000;
        if (l.LeftMilliS > 0) return false;
        l.LeftMilliS = 0; l.Lit = false;
        return true;
    }

    /// <summary>The torch-burn factor of the weather outdoors (4.4: light rain x1.5, heavy rain and storms x2.5; lanterns are rain-proof).</summary>
    public static int BurnPm(WeatherState w, LightKind kind, SiteConstantsDef k) => kind == LightKind.Lantern ? 1000 : w switch
    {
        WeatherState.LightRain => k.RainLightBurnPm,
        WeatherState.HeavyRain or WeatherState.Thunderstorm => k.RainHeavyBurnPm,
        _ => 1000,
    };

    /// <summary>The radius (cells) and intensity a lit light casts, guttering in its last 60 sim-seconds, shrunk by bad air.</summary>
    public static (int Radius, int Intensity) Cast(in CarriedLight l, SiteConstantsDef k, in AirEffect air)
    {
        if (!l.Lit || l.Out) return (0, 0);
        int r, i;
        switch (l.Kind)
        {
            case LightKind.Torch: r = k.TorchRadius; i = k.TorchIntensity; break;
            case LightKind.Candle: r = k.CandleRadius; i = k.CandleIntensity; break;
            case LightKind.Lantern: r = k.LanternRadius; i = k.LanternIntensity; break;
            default: return (0, 0);
        }
        if (l.LeftMilliS <= k.GutterS * 1000L && l.Kind != LightKind.Lantern) { r = Math.Max(1, r - k.GutterRadiusLoss); i = Math.Min(i, k.GutterIntensity); }
        if (air.FlameOut && l.Kind != LightKind.Lantern) return (0, 0);
        int pm = l.Kind == LightKind.Candle ? air.CandleRadiusPm : l.Kind == LightKind.Torch ? air.TorchRadiusPm : 1000;
        r = Math.Max(1, r * pm / 1000);
        return (r, i);
    }

    /// <summary>True in the last 60 sim-seconds of a torch or candle (the flame shrinks and flickers).</summary>
    public static bool Guttering(in CarriedLight l, SiteConstantsDef k) => l.Lit && !l.Out && l.Kind != LightKind.Lantern && l.LeftMilliS <= k.GutterS * 1000L;

    /// <summary>How far something standing in this much light is seen, millimetres (4.4): <c>1 + L x 39/255</c> metres; 235 is 36.9 m, 30 is 5.6 m, 0 is 1 m.</summary>
    public static int VisionRadiusMm(int light) => 1000 + Math.Clamp(light, 0, 255) * 39000 / 255;

    /// <summary>The attacker's skill-delta points for the target's light (4.8): 120 and up 0, 60-119 -5, 30-59 -15, below 30 -30 (heard, not seen).</summary>
    public static int LightPoints(int light) => light >= 120 ? 0 : light >= 60 ? -5 : light >= 30 ? -15 : -30;

    /// <summary>The air's effects at index <paramref name="a"/> (EX-7): 800 and up none; 600-799 the candle gutters (x0.7); 400-599 torches x0.5, regen x0.6, -10 accuracy;
    /// 250-399 dazed, speed x0.7, flames out; below 250 foul (unconscious after 20 sim-seconds, dead after 120).</summary>
    public static AirEffect Air(int a, SiteConstantsDef k)
    {
        if (a >= k.AirGutter) return new(1000, 1000, 1000, 0, false, 1000, false, false, false);
        if (a >= k.AirDim) return new(700, 1000, 1000, 0, false, 1000, false, false, true);
        if (a >= k.AirDazed) return new(500, 500, 600, -10, false, 1000, false, false, true);
        if (a >= k.AirFoul) return new(0, 0, 600, -10, true, 700, true, false, true);
        return new(0, 0, 0, -10, true, 700, true, true, true);
    }
}
