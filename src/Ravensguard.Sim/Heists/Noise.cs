using Ravensguard.Content;

namespace Ravensguard.Sim.Heists;

/// <summary>What made a noise (heists-and-jailbreaks 3). Append only.</summary>
public enum NoiseKind : byte { Walk, Run, Sneak, Door, Pick, Break, Fight, Shout, Stone, Search, Douse, Grate, Subdue }

/// <summary>
/// Noise and hearing (heists-and-jailbreaks 3): each source has a range in metres in still air; the weather scales it (light rain 85%, heavy rain 70%, a storm 50%), each wall or closed door between halves it, and a sleeper
/// hears at 30%. Pure integer maths in centimetres, no draws; the walls are counted by the caller on the cell line.
/// </summary>
public static class Noise
{
    public static int MetresOf(HeistSettingsDef s, NoiseKind k) => k switch
    {
        NoiseKind.Walk => s.NoiseWalkM, NoiseKind.Run => s.NoiseRunM, NoiseKind.Sneak => s.NoiseSneakM, NoiseKind.Door => s.NoiseDoorM, NoiseKind.Pick => s.NoisePickM, NoiseKind.Break => s.NoiseBreakM,
        NoiseKind.Fight => s.NoiseFightM, NoiseKind.Shout => s.NoiseShoutM, NoiseKind.Stone => s.NoiseStoneM, NoiseKind.Search => s.NoiseSearchM, NoiseKind.Douse => s.NoiseDouseM, NoiseKind.Grate => s.NoiseGrateM,
        NoiseKind.Subdue => s.NoiseSubdueM, _ => 0,
    };

    /// <summary>The weather's scale in permille: 0 clear, 1 light rain, 2 heavy rain, 3 storm.</summary>
    public static int WeatherPm(HeistSettingsDef s, int weather) => weather switch { 1 => s.RainLightPct * 10, 2 => s.RainHeavyPct * 10, >= 3 => s.StormPct * 10, _ => 1000 };

    /// <summary>How far the sound carries to one listener, in centimetres: <c>metres x weather x (wall share ^ walls) x sleeper share</c>.</summary>
    public static int RangeCm(HeistSettingsDef s, int metres, int weather, int walls, bool sleeper)
    {
        long r = metres * 100L;
        r = r * WeatherPm(s, weather) / 1000;
        for (int i = 0; i < walls && r > 0; i++) r = r * s.WallPct / 100;
        if (sleeper) r = r * s.SleeperPct / 100;
        return (int)r;
    }

    /// <summary>Whether a listener at <paramref name="distCm"/> hears it.</summary>
    public static bool Heard(HeistSettingsDef s, int metres, int weather, int walls, bool sleeper, int distCm) => distCm <= RangeCm(s, metres, weather, walls, sleeper);

    /// <summary>The HUD's ear: how loud the hero is making himself now (Silent to 2 m, Quiet to 8 m, Loud beyond), 0 to 2.</summary>
    public static int Loudness(int metres) => metres <= 2 ? 0 : metres <= 8 ? 1 : 2;
}
