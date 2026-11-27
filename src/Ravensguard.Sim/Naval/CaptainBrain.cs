using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Naval;

/// <summary>What a captain does next (live-war 3.2): close, ram, throw a grapple, send men across, shoot from the castles, or run.</summary>
public enum CaptainAct : byte { Approach, Ram, Grapple, Board, Shoot, Flee }

/// <summary>
/// What a captain knows when he thinks (every 2 game seconds). All whole numbers: the closing speed in mm/s along his heading to the target, the angle between his heading and the normal of the target's side (0 is square on) and the
/// angle between the two headings, both in whole degrees, the gap between the hulls in mm, and the fit fighters aboard each ship.
/// </summary>
public readonly record struct CaptainView(bool HasStem, int ClosingMmps, int RamAngleDeg, int DistMm, int HeadingDiffDeg, bool Grappled, int CrewFit, int CrewStart, int EnemyFit, bool TakingWater);

/// <summary>
/// P8-LW-2, <c>CaptainBrain</c> (live-war 3.2): a pure decision. Flee first (the crew under 40% of its start, or the hull taking water faster than the pumps clear it); then, when grappled, board if the crew is at least 0.8 of the enemy
/// deck's and otherwise shoot from the castles; grapple within 6 m and nearly side by side (headings within 25 degrees); ram with a <c>stem_post</c> at 2.5 m/s or more and within 30 degrees of square on; else close the target. The side it
/// closes by is the upwind one, and when the two are equal a draw on 0x858 keyed on the ship and the minute (<see cref="ApproachSide"/>).
/// </summary>
public static class CaptainBrain
{
    public const int EveryTicks = 40, RamMinClosingMmps = 2500, RamMaxAngleDeg = 30, RamReachMm = 12_000, GrappleRangeMm = 6000, GrappleHeadingDeg = 25, BoardPm = 800, FleePm = 400;

    public static CaptainAct Decide(in CaptainView v)
    {
        if (v.CrewStart > 0 && v.CrewFit * 1000 < v.CrewStart * FleePm || v.TakingWater) return CaptainAct.Flee;
        if (v.Grappled) return v.CrewFit * 1000 >= v.EnemyFit * BoardPm ? CaptainAct.Board : CaptainAct.Shoot;
        if (v.DistMm <= GrappleRangeMm && v.HeadingDiffDeg <= GrappleHeadingDeg) return CaptainAct.Grapple;
        if (v.HasStem && v.ClosingMmps >= RamMinClosingMmps && v.RamAngleDeg <= RamMaxAngleDeg && v.DistMm <= RamReachMm) return CaptainAct.Ram;
        return CaptainAct.Approach;
    }

    /// <summary>The side a ship closes by: -1 port, +1 starboard. The upwind side when the wind favours one (<paramref name="upwindSide"/> not 0), else the 0x858 draw on the ship and the game minute.</summary>
    public static int ApproachSide(ulong seed, ulong ship, long minute, int upwindSide)
        => upwindSide != 0 ? Math.Sign(upwindSide) : DetRng.Range(seed, minute, ship, RngStreams.CaptainApproach, 0, 0, 2) == 0 ? -1 : 1;

    public const int Cos25Pm = 906, Cos30Pm = 866;

    /// <summary>The captain's view (the two angles are quantised: 30 or 90 for the ram, 25 or 90 for the heading, "within the limit or not"; the permille cosines decide) of <paramref name="ship"/> against <paramref name="target"/>: pure geometry on the frames (Q30 trig, no float).</summary>
    public static CaptainView View(CarrierLayer layer, ShipFrame ship, ShipFrame target, int crewTeam, int crewStart, bool hasStem, bool grappled, bool takingWater)
    {
        long dx = target.XMm - ship.XMm, dy = target.YMm - ship.YMm, dist = Math.Max(1, IntMath.Isqrt(dx * dx + dy * dy));
        var (c, s) = ship.CosSin; var (tc, ts) = target.CosSin;
        long towards = (c * dx + s * dy) >> 30;                                   // the bearing component along the heading, mm
        int closing = (int)(ship.SpeedMmps * towards / dist);
        long nDot = Math.Abs((c * -ts + s * tc) >> 30);                           // heading against the target's side normal (-sin, cos): 1.0 is square on
        long hDot = (c * tc + s * ts) >> 30;                                      // heading against heading: 1.0 is the same way
        int ramAngle = nDot * 1000 >> 30 >= Cos30Pm ? RamMaxAngleDeg : 90;
        int diff = hDot * 1000 >> 30 >= Cos25Pm ? GrappleHeadingDeg : 90;
        return new CaptainView(hasStem, closing, ramAngle, (int)Math.Min(int.MaxValue, dist), diff, grappled, layer.CrewFit(ship, crewTeam), crewStart, layer.CrewFit(target, 1 - crewTeam), takingWater);
    }
}
