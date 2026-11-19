using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Carts;

namespace Ravensguard.Sim.Combat.Mounted;

/// <summary>The ground under a horse (mounted-combat 4.5). Append only: saved in a mounted fight.</summary>
public enum Footing : byte { Road, Field, Rough, Mud, Snow, Ice, Woods, Ford, DeepWater, Portal }

/// <summary>One footing class as integers (content <c>combat/mounted.jsonc</c> <c>footing</c>).</summary>
public readonly record struct FootingRule(int SpeedPm, bool Charge, int MaxMmps, int SlipPmPerS, bool Forbidden);

/// <summary>
/// The ground of a fight for horses: a base footing, rectangles of other footing (a mud patch, a wood, a stair), a plane slope (height rises <see cref="SlopeXPm"/> mm per metre east and
/// <see cref="SlopeYPm"/> per metre south) and the weather that matters to horses (heavy rain, a thunderstorm). The live game builds it from the Phase 3 surface; tests and the demo add zones.
/// The base, the slope and the weather are saved with a mounted fight; zones are the setup's (a road fight has none).
/// </summary>
public sealed class FightTerrain
{
    public Footing Base = Footing.Field;
    public int SlopeXPm, SlopeYPm;
    public bool HeavyRain, Thunderstorm;
    public readonly List<(int X0, int Y0, int X1, int Y1, Footing Footing)> Zones = [];

    public Footing At(int x, int y)
    {
        for (int i = Zones.Count - 1; i >= 0; i--)
        {
            var z = Zones[i];
            if (x >= z.X0 && x < z.X1 && y >= z.Y0 && y < z.Y1) return z.Footing;
        }
        return Base;
    }

    /// <summary>Ground height in mm (the plane).</summary>
    public long HeightMm(int x, int y) => (long)x * SlopeXPm / 1000 + (long)y * SlopeYPm / 1000;

    /// <summary>The Phase 3 surface as horse footing (paved and dry or wet roads and tracks are road; grass field; mud mud; snow snow; ice ice; sand rough).</summary>
    public static Footing Of(Surface s) => s switch
    {
        Surface.Paved or Surface.DryRoad or Surface.WetRoad or Surface.Track => Footing.Road,
        Surface.Grass => Footing.Field,
        Surface.MudRoad or Surface.DeepMud => Footing.Mud,
        Surface.PackedSnow or Surface.DeepSnow => Footing.Snow,
        Surface.Ice => Footing.Ice,
        _ => Footing.Rough,
    };
}

/// <summary>Footing rules (4.5): the gallop speed factor, the charge rule, top speed, slips and stumbles, slopes and weather. Pure integer functions.</summary>
public static class TerrainFooting
{
    public static readonly string[] Ids = ["road", "field", "rough", "mud", "snow", "ice", "woods", "ford", "deep_water", "portal"];

    /// <summary>The spec's table, used when content leaves a class out.</summary>
    public static readonly FootingRule[] Defaults =
    [
        new(1000, true, 0, 0, false), new(850, true, 0, 0, false), new(600, false, 8000, 20, false), new(500, false, 5000, 0, false), new(600, false, 8000, 0, false),
        new(400, false, 0, 80, false), new(500, false, 5200, 0, false), new(500, false, 5200, 0, false), new(80, false, 1000, 0, false), new(0, false, 0, 0, true),
    ];

    public static FootingRule[] From(MountedTuningDef d)
    {
        var r = (FootingRule[])Defaults.Clone();
        for (int i = 0; i < Ids.Length; i++)
            if (d.Footing.TryGetValue(Ids[i], out var f)) r[i] = new FootingRule(f.SpeedPm, f.Charge, f.MaxMmps, f.SlipPmPerS, f.Forbidden);
        return r;
    }

    /// <summary>
    /// The top speed a horse may reach on this footing for a wanted gait speed: a gallop (above the trot) scales by the footing (heavy rain on a field x0.8); the footing's cap; a footing
    /// that allows no charge keeps the horse under the charge speed; a forbidden one stops it.
    /// </summary>
    public static int CapMmps(in FootingRule f, Footing kind, int wantMmps, int trotMmps, int chargeMinMmps, bool heavyRain, int rainFieldPm)
    {
        if (f.Forbidden) return 0;
        int v = wantMmps;
        if (v > trotMmps)
        {
            int pm = f.SpeedPm;
            if (heavyRain && kind == Footing.Field) pm = pm * rainFieldPm / 1000;
            v = Math.Max(Math.Min(v, trotMmps), v * pm / 1000);
        }
        if (f.MaxMmps > 0) v = Math.Min(v, f.MaxMmps);
        if (!f.Charge) v = Math.Min(v, chargeMinMmps - 1);
        return Math.Max(0, v);
    }

    /// <summary>
    /// The slope's effect on speed, permille: uphill <c>1 - k_up x slope</c> (at least the minimum), downhill <c>1 + k_down x slope</c> (at most +cap). <paramref name="slopePm"/> is the rise per
    /// metre along the heading (positive uphill).
    /// </summary>
    public static int SlopePm(int slopePm, int upK = 3000, int upMin = 300, int downK = 1500, int downCap = 200)
    {
        if (slopePm >= 0) return Math.Max(upMin, 1000 - (int)((long)upK * slopePm / 1000));
        return Math.Min(1000 + downCap, 1000 + (int)((long)downK * -slopePm / 1000));
    }

    /// <summary>A galloping horse's slip (ice) or stumble (rubble) chance this second, permille (heavy rain x1.5).</summary>
    public static int SlipPm(in FootingRule f, bool galloping, bool heavyRain, int rainSlipPm = 1500) =>
        !galloping || f.SlipPmPerS <= 0 ? 0 : heavyRain ? f.SlipPmPerS * rainSlipPm / 1000 : f.SlipPmPerS;

    /// <summary>The roll for this second (stream 0x473, key FightTick, horse): true when the horse goes down.</summary>
    public static bool Slips(ulong seed, int tick, int horse, int slipPm) =>
        slipPm > 0 && DetRng.Range(seed, tick, (ulong)(horse + 1), RngStreams.TerrainSlip, 0, 0, 1000) < slipPm;
}
