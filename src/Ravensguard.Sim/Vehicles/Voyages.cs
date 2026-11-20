using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Carts;

/// <summary>
/// A waterway's polyline as a path along which a boat's position is a distance (mm from the upstream end), with its width there (the width class gives depth and current, vehicle-designer 3.4).
/// Integer only: the segment lengths are the integer square roots of their squared lengths in mm.
/// </summary>
public sealed class WaterPath
{
    public int Id { get; }
    private readonly long[] _x, _y, _at; private readonly int[] _w;
    public long LengthMm => _at.Length == 0 ? 0 : _at[^1];

    public WaterPath(Waterway w)
    {
        Id = w.Id; int n = w.Points.Length;
        _x = new long[n]; _y = new long[n]; _at = new long[n]; _w = new int[n];
        for (int i = 0; i < n; i++)
        {
            _x[i] = w.Points[i].X * 1000L; _y[i] = w.Points[i].Y * 1000L; _w[i] = i < w.WidthCm.Length ? w.WidthCm[i] : 300;
            if (i > 0) { long dx = _x[i] - _x[i - 1], dy = _y[i] - _y[i - 1]; _at[i] = _at[i - 1] + IntMath.Isqrt(dx * dx + dy * dy); }
        }
    }

    /// <summary>The point and the width at a distance along the path (clamped to its ends).</summary>
    public (long X, long Y, int WidthCm) At(long mm)
    {
        if (_at.Length == 0) return (0, 0, 300);
        if (mm <= 0) return (_x[0], _y[0], _w[0]);
        if (mm >= LengthMm) return (_x[^1], _y[^1], _w[^1]);
        int i = Array.BinarySearch(_at, mm); if (i < 0) i = ~i;
        long seg = _at[i] - _at[i - 1]; long f = seg <= 0 ? 0 : mm - _at[i - 1];
        return (_x[i - 1] + (_x[i] - _x[i - 1]) * f / Math.Max(1, seg), _y[i - 1] + (_y[i] - _y[i - 1]) * f / Math.Max(1, seg), _w[i - 1]);
    }

    /// <summary>The narrowest width between two distances (a leg's shallowest stretch).</summary>
    public int MinWidthCm(long a, long b)
    {
        long lo = Math.Min(a, b), hi = Math.Max(a, b); int w = int.MaxValue;
        for (int i = 0; i < _at.Length; i++) if (_at[i] >= lo && _at[i] <= hi) w = Math.Min(w, _w[i]);
        return w == int.MaxValue ? At(lo).WidthCm : w;
    }
}

/// <summary>
/// Boats under way on a river (vehicle-designer 4.5, 4.6): a voyage is a leg along a waterway at the boat's ground speed (its power against the water's drag at its draft, plus the current downstream or
/// minus it upstream); the abstract tier moves it hourly, the live tier each game second, with the same speed and the same rounding, so a leg takes the same seconds in both (VD-19). A leg whose shallowest
/// stretch is too shallow for the loaded draft is aground until poled off: a delay of up to two hours (stream 0x552, key barge id and leg).
/// </summary>
public static class Voyages
{
    /// <summary>The boat's ground speed on a stretch of <paramref name="widthCm"/> after <paramref name="highDays"/> river-high days, downstream or up (mm/s, real): its rowers, polers and towing team.</summary>
    public static int GroundMmps(RideBook b, VehicleRow boat, int widthCm, int highDays, bool downstream, int crewKg = -1)
    {
        var st = b.Vehicles.StatsOf(boat); var wt = b.Templates.Water;
        var (depth, current) = wt.River(widthCm, highDays);
        var load = BoatMath.Load(st, b.Vehicles.CargoKg(boat), crewKg >= 0 ? crewKg : (st.Driver + st.Rowers) * BoatMath.PersonKg, wt);
        long tow = st.TowPost && boat.Team.Count > 0 ? b.Vehicles.Team(boat).PullN : 0;
        long f = BoatMath.PowerN(wt, st.Rowers, st.Polers, depth, tow, 0);
        int water = BoatMath.WaterSpeedMmps(f, wt.DragK, BoatMath.WetMm2(st.WaterplaneMm2, st.PerimeterM, load.DraftMm));
        return BoatMath.GroundMmps(water, current, downstream);
    }

    /// <summary>The game seconds a leg of <paramref name="lengthMm"/> takes at <paramref name="mmps"/> real mm/s (a real second is 20 game seconds, the caravans' scale): <c>ceil(length x 20 / speed)</c>.</summary>
    public static long LegSeconds(long lengthMm, int mmps) => mmps <= 0 ? long.MaxValue : (lengthMm * 20 + mmps - 1) / mmps;

    /// <summary>Puts a boat on a leg from where it is (<paramref name="atMm"/> along the waterway) to <paramref name="toMm"/>.</summary>
    public static Voyage Start(RideBook b, VehicleRow boat, WaterPath path, long atMm, long toMm, long nowSec, int highDays = 0, bool? shuttle = null)
    {
        bool down = toMm >= atMm;
        int width = path.MinWidthCm(atMm, toMm);
        int speed = GroundMmps(b, boat, path.At(atMm).WidthCm, highDays, down);
        var st = b.Vehicles.StatsOf(boat); var wt = b.Templates.Water;
        var load = BoatMath.Load(st, b.Vehicles.CargoKg(boat), (st.Driver + st.Rowers) * BoatMath.PersonKg, wt);
        var y = b.Works.Voyages.TryGetValue(boat.Id, out var old) ? old : new Voyage { Boat = boat.Id };
        y.Waterway = path.Id; y.AtMm = atMm; y.ToMm = toMm; y.GroundMmps = speed; y.LegStartSec = nowSec; y.Leg++; y.ArrivedSec = 0; if (shuttle != null) y.Shuttle = shuttle.Value;
        y.LegDelaySec = BoatMath.Floats(load.DraftMm, wt.River(width, highDays).DepthMm, wt) ? 0 : BoatMath.LegDelaySeconds(b.Seed, boat.Id, y.Leg);
        b.Works.Voyages[boat.Id] = y;
        boat.Water = WaterState.Afloat;
        var (x0, y0, _) = path.At(atMm); boat.XMm = x0; boat.YMm = y0;
        return y;
    }

    /// <summary>Where a voyage is at <paramref name="nowSec"/> (mm along the waterway), and whether it has arrived.</summary>
    public static (long AtMm, bool Arrived) Position(Voyage y, long nowSec)
    {
        long len = Math.Abs(y.ToMm - y.AtMm);
        long moving = nowSec - y.LegStartSec - y.LegDelaySec;
        if (moving <= 0 || y.GroundMmps <= 0) return (y.AtMm, false);
        if (moving >= LegSeconds(len, y.GroundMmps)) return (y.ToMm, true);
        long d = moving * y.GroundMmps / 20;
        return (y.ToMm >= y.AtMm ? y.AtMm + d : y.AtMm - d, false);
    }

    /// <summary>The hourly step of every voyage (H1): the boats move along their waterways; an arrival is recorded (the boat waits there).</summary>
    public static void Hour(RideBook b, Func<int, WaterPath?> paths, long hour)
    {
        long now = (hour + 1) * 3600;
        foreach (var y in b.Works.Voyages.Values.ToList())
        {
            if (y.ArrivedSec > 0) continue;
            var boat = b.Vehicles.Find(y.Boat); if (boat == null || boat.Driven) continue;       // the hero's own boat moves in the live tier
            var (at, arrived) = Position(y, now);
            if (paths(y.Waterway) is { } p) { var (x, yy, _) = p.At(at); boat.XMm = x; boat.YMm = yy; }
            boat.OdometerMm = Math.Abs(at - y.AtMm);
            if (!arrived) continue;
            y.ArrivedSec = y.LegStartSec + y.LegDelaySec + LegSeconds(Math.Abs(y.ToMm - y.AtMm), y.GroundMmps);
            if (y.Shuttle && paths(y.Waterway) is { } back)
            {
                // a regular run: it turns at the end of the leg and goes back, starting when it arrived
                long from = y.ToMm, to = y.AtMm, when = y.ArrivedSec;
                Start(b, boat, back, from, to, when);
            }
        }
    }
}
