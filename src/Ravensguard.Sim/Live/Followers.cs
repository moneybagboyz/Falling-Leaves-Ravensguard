using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Live;

/// <summary>One crumb of the hero's trail: where the hero stood (continent millimetres, level) and the arc length of the trail up to it. A step of more than <see cref="HeroTrail.JumpMm"/> (a portal) adds no length.</summary>
public readonly record struct TrailPoint(long XMm, long YMm, int Z, long S);

/// <summary>
/// The hero's breadcrumb trail (Phase 3 cleanup B, item 3): a point every half metre the hero moves (and one at every change of level), so a follower that walks the trail goes up the same stairs and through the same
/// portals the hero did, without a path search. A pure function of the hero's positions, fed once per tick; the same positions give the same trail at any clock speed.
/// </summary>
public sealed class HeroTrail
{
    public const long StepMm = 500, JumpMm = 4_000, MaxLengthMm = 120_000;
    private readonly List<TrailPoint> _pts = [];
    public IReadOnlyList<TrailPoint> Points => _pts;
    /// <summary>Arc length of the newest point (the hero's own position along the trail); 0 before the first note.</summary>
    public long HeadS => _pts.Count == 0 ? 0 : _pts[^1].S;
    public long LastStepMm { get; private set; }

    public void Clear() { _pts.Clear(); LastStepMm = 0; }

    /// <summary>Notes the hero's position this tick.</summary>
    public void Note(long xMm, long yMm, int z)
    {
        LastStepMm = 0;
        if (_pts.Count == 0) { _pts.Add(new(xMm, yMm, z, 0)); return; }
        var last = _pts[^1];
        long d = Ravensguard.Sim.Travel.RoutePath.Dist(xMm - last.XMm, yMm - last.YMm);
        if (z == last.Z && d < StepMm) return;
        if (d == 0 && z == last.Z) return;
        bool jump = d > JumpMm;
        long len = jump ? 0 : d;
        LastStepMm = len;
        _pts.Add(new(xMm, yMm, z, last.S + len));
        // keep the recent stretch only: followers further behind than this are brought up (see FollowState.Step)
        if (_pts.Count > 2 && HeadS - _pts[0].S > MaxLengthMm) { int cut = 0; while (cut < _pts.Count - 2 && HeadS - _pts[cut + 1].S > MaxLengthMm) cut++; if (cut > 0) _pts.RemoveRange(0, cut); }
    }

    public long TailS => _pts.Count == 0 ? 0 : _pts[0].S;

    /// <summary>The position at arc length <paramref name="s"/> (clamped to the trail): on a jump the point before it until s reaches the next, on a level change the level of the nearer crumb.</summary>
    public (long X, long Y, int Z, int HeadX, int HeadY) At(long s)
    {
        if (_pts.Count == 0) return (0, 0, 0, 0, 1);
        s = Math.Clamp(s, TailS, HeadS);
        int lo = 0, hi = _pts.Count - 1;
        while (lo < hi) { int mid = (lo + hi + 1) / 2; if (_pts[mid].S <= s) lo = mid; else hi = mid - 1; }
        var a = _pts[lo]; if (lo == _pts.Count - 1) return (a.XMm, a.YMm, a.Z, Facing(lo - 1 >= 0 ? _pts[lo - 1] : a, a).Item1, Facing(lo - 1 >= 0 ? _pts[lo - 1] : a, a).Item2);
        var b = _pts[lo + 1];
        var (hx, hy) = Facing(a, b);
        long seg = b.S - a.S;
        if (seg <= 0) return (a.XMm, a.YMm, a.Z, hx, hy);               // a portal or a stair: the follower stands on the near side until the trail has advanced past it
        long t = s - a.S;
        return (a.XMm + (b.XMm - a.XMm) * t / seg, a.YMm + (b.YMm - a.YMm) * t / seg, t * 2 < seg ? a.Z : b.Z, hx, hy);
    }

    private static (int, int) Facing(TrailPoint a, TrailPoint b) => (Math.Sign(b.XMm - a.XMm), Math.Sign(b.YMm - a.YMm)) is (0, 0) ? (0, 1) : (Math.Sign(b.XMm - a.XMm), Math.Sign(b.YMm - a.YMm));
}

/// <summary>One follower's place along the hero's trail: spacing behind, and how fast they close up.</summary>
public sealed class FollowState
{
    /// <summary>Order in the column (0 is nearest the hero).</summary>
    public int Slot;
    /// <summary>The follower's arc-length position on the trail; long.MinValue until placed.</summary>
    public long S = long.MinValue;
    /// <summary>True: walking behind the hero; false: told to wait, standing where they are.</summary>
    public bool Follow = true;

    public const long SpacingMm = 1_800;
    public static long TargetS(HeroTrail t, int slot) => Math.Max(t.TailS, t.HeadS - SpacingMm * (slot + 1));

    /// <summary>
    /// One tick: a waiting follower stays; a following one closes toward the spot <c>SpacingMm x (slot + 1)</c> behind the hero's head by at most twice what the hero walked this tick (never less than
    /// <paramref name="minStepMm"/>, 150 mm a tick on foot, so they catch up when the hero stops), and is put straight there when more than 12 m behind (they were left behind at a door, a load, a screen).
    /// Returns true when the follower moved.
    /// </summary>
    public bool Step(HeroTrail t, long minStepMm)
    {
        if (t.Points.Count == 0) return false;
        long target = TargetS(t, Slot);
        if (S == long.MinValue) { S = target; return true; }
        if (!Follow) return false;
        if (target - S > 12_000) { S = target; return true; }
        long gap = target - S; if (gap <= 0) return false;
        long step = Math.Max(minStepMm, t.LastStepMm * 2);
        S += Math.Min(gap, step);
        return true;
    }
}
