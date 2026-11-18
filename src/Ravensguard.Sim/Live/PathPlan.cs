namespace Ravensguard.Sim.Live;

/// <summary>
/// A time-parametrised polyline (abstract spec 2.4): <see cref="PositionAt"/> is a pure function of the game second, so the abstract and the live tier evaluate the
/// same plan and a person can never drift. Points are metres of the region frame in millimetres; the walk starts at <see cref="StartSec"/> and ends at
/// <see cref="EndSec"/> (the pace is the length over that time, so the arrival second is the schedule's and not the route's).
/// </summary>
public sealed class PathPlan
{
    public long[] X { get; }
    public long[] Y { get; }
    public long[] CumMm { get; }
    public long StartSec { get; }
    public long EndSec { get; }
    public long LengthMm => CumMm[^1];
    /// <summary>Millimetres per game second the plan moves at (0 for a point).</summary>
    public long SpeedMmPerGs => EndSec > StartSec ? LengthMm / (EndSec - StartSec) : 0;

    /// <summary>The level of each point (a walk up a stair changes level between two points); null: all on level 0.</summary>
    public int[]? Z { get; }

    public PathPlan(long[] x, long[] y, long startSec, long endSec) : this(x, y, null, startSec, endSec) { }

    public PathPlan(long[] x, long[] y, int[]? z, long startSec, long endSec)
    {
        if (x.Length == 0 || x.Length != y.Length || (z != null && z.Length != x.Length)) throw new ArgumentException("A path plan needs at least one point.");
        X = x; Y = y; Z = z; StartSec = startSec; EndSec = Math.Max(startSec, endSec);
        CumMm = new long[x.Length];
        for (int i = 1; i < x.Length; i++) CumMm[i] = CumMm[i - 1] + Dist(x[i] - x[i - 1], y[i] - y[i - 1]);
    }

    public static PathPlan Point(long x, long y, long startSec, long endSec) => new([x], [y], startSec, endSec);
    public static PathPlan Point(long x, long y, int z, long startSec, long endSec) => new([x], [y], [z], startSec, endSec);
    public static PathPlan Straight(long x0, long y0, long x1, long y1, long startSec, long endSec) => new([x0, x1], [y0, y1], startSec, endSec);
    public static PathPlan Straight(long x0, long y0, int z0, long x1, long y1, int z1, long startSec, long endSec) => new([x0, x1], [y0, y1], [z0, z1], startSec, endSec);

    /// <summary>True while the plan is moving (between its start and end second, with somewhere to go).</summary>
    public bool MovingAt(long sec) => LengthMm > 0 && sec > StartSec && sec < EndSec;

    /// <summary>The level at <paramref name="sec"/>: the level of the point just passed, switching to the next level halfway along an edge that joins two levels (a stair).</summary>
    public int ZAt(long sec)
    {
        if (Z == null) return 0;
        if (Z.Length == 1 || LengthMm == 0) return Z[0];
        long d = (long)((Int128)LengthMm * ProgressPpmAt(sec) / 1_000_000);
        int i = Array.BinarySearch(CumMm, d);
        if (i < 0) i = ~i - 1;
        i = Math.Clamp(i, 0, X.Length - 2);
        while (i < X.Length - 2 && CumMm[i + 1] == CumMm[i]) i++;
        if (Z[i] == Z[i + 1]) return Z[i];
        long seg = Math.Max(1, CumMm[i + 1] - CumMm[i]);
        return d - CumMm[i] < seg / 2 ? Z[i] : Z[i + 1];
    }

    /// <summary>Integer square root of the squared length, millimetres.</summary>
    public static long Dist(long dx, long dy)
    {
        long n = dx * dx + dy * dy;
        if (n <= 0) return 0;
        long r = (long)Math.Sqrt(n);
        while (r * r > n) r--;
        while ((r + 1) * (r + 1) <= n) r++;
        return r;
    }

    /// <summary>Progress at <paramref name="sec"/>, parts per million of the walk (0 before the start, 1,000,000 from the end).</summary>
    public int ProgressPpmAt(long sec)
    {
        if (sec <= StartSec) return 0;
        if (sec >= EndSec) return 1_000_000;
        return (int)((sec - StartSec) * 1_000_000 / (EndSec - StartSec));
    }

    /// <summary>The position (millimetres) and the heading (unit vector x1024) at game second <paramref name="sec"/>.</summary>
    public (long XMm, long YMm, int HeadX, int HeadY) PositionAt(long sec)
    {
        long d = LengthMm == 0 ? 0 : (long)((Int128)LengthMm * ProgressPpmAt(sec) / 1_000_000);
        if (X.Length == 1 || LengthMm == 0) return (X[0], Y[0], 0, 1024);
        int i = Array.BinarySearch(CumMm, d);
        if (i < 0) i = ~i - 1;
        i = Math.Clamp(i, 0, X.Length - 2);
        while (i < X.Length - 2 && CumMm[i + 1] == CumMm[i]) i++;       // skip zero-length segments
        long seg = CumMm[i + 1] - CumMm[i], t = Math.Clamp(d - CumMm[i], 0, seg);
        long x = X[i] + (X[i + 1] - X[i]) * t / Math.Max(1, seg), y = Y[i] + (Y[i + 1] - Y[i]) * t / Math.Max(1, seg);
        long dx = X[i + 1] - X[i], dy = Y[i + 1] - Y[i], n = Math.Max(1, Dist(dx, dy));
        return (x, y, (int)(dx * 1024 / n), (int)(dy * 1024 / n));
    }
}
