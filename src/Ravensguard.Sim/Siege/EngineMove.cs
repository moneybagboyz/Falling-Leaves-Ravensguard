using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Combat.Melee2;

namespace Ravensguard.Sim.Siege;

/// <summary>
/// Moving an engine (sieges-and-engines 4.2): people push it, each pusher 300 N (Phase 5's <c>PushNPerStrength</c> x strength 50), and the speed is the one cart formula with those newtons as the pull, so a tower is 0.25 m/s on firm
/// level ground and stops in mud or on a slope over 5%. A springald is carried by six. A broken wheel or axle stops what rolls (the cart rule); a fallen tower does not move.
/// </summary>
public sealed partial class EngineBook
{
    private static readonly SpeedResult CannotMove = new(0, 0, int.MaxValue, 0, 0, 0, true, false, false);

    /// <summary>Surfaces a tower cannot be pushed over: mud, deep mud, deep snow, sand.</summary>
    public static bool Soft(Surface s) => s is Surface.MudRoad or Surface.DeepMud or Surface.DeepSnow or Surface.Sand;

    /// <summary>The engine's speed on a surface and slope (permille) with the people on it: the cart formula for a pushed engine, the carriers' walk for a carried one, nothing for the rest.</summary>
    public SpeedResult SpeedOn(Engine e, Surface s, int slopePm, int strength = 50)
    {
        var st = Stat(e); var v = VehicleOf(e);
        if (v == null || !v.Finished || e.Toppled) return CannotMove;
        var stats = Rides.Vehicles.StatsOf(v);
        if (st.Carry > 0)
        {
            bool ok = e.Pushers.Count >= st.Carry && (long)e.Pushers.Count * 80 >= stats.MassKg;
            return ok ? new SpeedResult(0, 0, 0, 1000, st.BaseCmps, 0, false, false, false) : CannotMove;
        }
        if (st.Push <= 0 || v.Broken || e.Pushers.Count == 0) return CannotMove;
        if (st.NeedsFirm && (Soft(s) || slopePm > st.MaxSlopePm)) return CannotMove;
        var tu = Rides.Vehicles.Catalog.Tuning;
        long pull = (long)e.Pushers.Count * Stuck.PushN(tu, strength);
        return VehicleMath.OnSurface(tu, stats, 0, 0, s, slopePm, pull, stats.BaseSpeedCmps);
    }

    /// <summary>
    /// Pushes the engine toward a point for <paramref name="seconds"/> game seconds (a tick is one): it moves <c>speed</c> mm a second, no further than the point, faces the way it goes (64 directions of a byte turn) and its wheels wear as they
    /// roll. Returns the millimetres moved.
    /// </summary>
    public long Push(Engine e, long toX, long toY, long seconds, Surface s, int slopePm, int strength = 50)
    {
        var v = VehicleOf(e); if (v == null || seconds <= 0) return 0;
        var sp = SpeedOn(e, s, slopePm, strength);
        if (sp.CannotMove || sp.SpeedCmps <= 0) return 0;
        long dx = toX - v.XMm, dy = toY - v.YMm, dist = FightMath.Len(dx, dy);
        if (dist == 0) return 0;
        long step = Math.Min(dist, (long)VehicleMath.MmPerGs(sp.SpeedCmps) * seconds);
        v.Facing = (byte)(FightMath.DirIndex((int)Math.Clamp(dx, -1_000_000, 1_000_000), (int)Math.Clamp(dy, -1_000_000, 1_000_000)) * 4 & 255);
        v.XMm += dx * step / dist; v.YMm += dy * step / dist;
        Rides.Vehicles.Wear(v, step, VehicleTable.RoughPm(s));
        return step;
    }
}
