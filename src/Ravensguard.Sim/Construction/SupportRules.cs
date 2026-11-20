using Ravensguard.Content;

namespace Ravensguard.Sim.Construction;

/// <summary>What a support check sees at a cell: the structure piece there, or null.</summary>
public delegate PieceCost? CellAt(int x, int y, int z);

/// <summary>Why a piece cannot stand (refusal keys of en.csv, cn_*).</summary>
public enum SupportFault : byte { None, NoSupport, Load, Jetty }

/// <summary>
/// Structural support (build-mode-and-construction 4.5, extending Phase 1's <c>StructuralSupport</c>). The rest rule: a piece at z >= 1 rests on the cell below when that holds a wall, a post,
/// an opening of a wall or (for walls) a beam; a floor-bearing piece (floor, roof, stair, ladder, beam) also rests when a wall or post at z - 1 lies within the support reach (3 tiles,
/// Chebyshev), or a carried beam at its own level within the beam reach (4 tiles); a wall with nothing directly below must stand within the jetty reach (1 tile) of a support. A beam run
/// is carried when supports stand under (or one tile beside) both its ends and its clear span is at most 6 cells. The load rule: what holds a piece at z must bear z (a support at level s
/// bears <c>s + carries_storeys</c>, never more than what holds it). Pure functions of the cells; no state.
/// </summary>
public sealed class SupportRules
{
    private readonly PieceCosts _costs;
    private readonly ConstructionRulesDef _r;
    public SupportRules(PieceCosts costs) { _costs = costs; _r = costs.Rules; }

    public readonly record struct Verdict(SupportFault Fault, string? Weakest = null, int WeakestCarries = 0)
    {
        public bool Ok => Fault == SupportFault.None;
    }

    /// <summary>Whether <paramref name="c"/> can stand at (x, y, z) among the cells <paramref name="at"/> shows (the cell itself is not read).</summary>
    public Verdict Check(int x, int y, int z, PieceCost c, CellAt at)
    {
        if (z <= 0) return new(SupportFault.None);
        if (PieceCosts.IsFixture(c.Kind)) return new(SupportFault.None);       // a fixture stands on the floor of its own cell (checked by placement)
        string? weakest = null; int weakCarries = 0;
        bool any = false;
        // directly below
        var below = at(x, y, z - 1);
        if (below != null && (PieceCosts.IsUpright(below.Kind) || (below.Kind == PieceKind.Beam && PieceCosts.IsUpright(c.Kind))))
        {
            any = true;
            int bear = Bear(x, y, z - 1, at, 0);
            if (bear >= z) return new(SupportFault.None);
            Note(below, ref weakest, ref weakCarries);
        }
        if (PieceCosts.IsUpright(c.Kind))
        {
            // a jetty: an upright with nothing under it stands within one tile of a support
            int jr = _r.JettyReachTiles;
            for (int dy = -jr; dy <= jr; dy++)
                for (int dx = -jr; dx <= jr; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var s = at(x + dx, y + dy, z - 1);
                    if (s == null || !PieceCosts.IsSupport(s.Kind)) continue;
                    any = true;
                    if (Bear(x + dx, y + dy, z - 1, at, 0) >= z) return new(SupportFault.None);
                    Note(s, ref weakest, ref weakCarries);
                }
            return any ? new(SupportFault.Load, weakest, weakCarries) : new(SupportFault.Jetty);
        }
        if (!PieceCosts.IsFloorBearing(c.Kind)) return any ? new(SupportFault.Load, weakest, weakCarries) : new(SupportFault.NoSupport);
        int reach = _r.SupportReachTiles;
        for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                var s = at(x + dx, y + dy, z - 1);
                if (s == null || !PieceCosts.IsSupport(s.Kind)) continue;
                any = true;
                if (Bear(x + dx, y + dy, z - 1, at, 0) >= z) return new(SupportFault.None);
                Note(s, ref weakest, ref weakCarries);
            }
        if (c.Kind != PieceKind.Beam)
        {
            int br = _r.BeamReachTiles;
            for (int dy = -br; dy <= br; dy++)
                for (int dx = -br; dx <= br; dx++)
                {
                    var b = at(x + dx, y + dy, z);
                    if (b == null || b.Kind != PieceKind.Beam) continue;
                    int bear = BeamBear(x + dx, y + dy, z, at);
                    if (bear < 0) continue;
                    any = true;
                    if (bear >= z) return new(SupportFault.None);
                    Note(b, ref weakest, ref weakCarries);
                }
        }
        else if (BeamBear(x, y, z, at, self: c) is int bb && bb >= 0)
            return bb >= z ? new(SupportFault.None) : new(SupportFault.Load, weakest, weakCarries);
        return any ? new(SupportFault.Load, weakest, weakCarries) : new(SupportFault.NoSupport);
    }

    private static void Note(PieceCost p, ref string? weakest, ref int carries)
    {
        if (weakest == null || p.Carries < carries) { weakest = p.Id; carries = p.Carries; }
    }

    /// <summary>
    /// The highest level a support at (x, y, z) can bear: <c>z + carries_storeys</c>, capped by what holds it up (a post on a wattle wall bears no more than the wattle). -1 when it does not
    /// stand at all.
    /// </summary>
    public int Bear(int x, int y, int z, CellAt at, int depth)
    {
        var p = at(x, y, z);
        if (p == null) return -1;
        int own = z + p.Carries;
        if (z <= 0 || depth > 8) return own;
        var below = at(x, y, z - 1);
        int best = -1;
        if (below != null && (PieceCosts.IsUpright(below.Kind) || below.Kind == PieceKind.Beam))
            best = below.Kind == PieceKind.Beam ? BeamBear(x, y, z - 1, at) : Bear(x, y, z - 1, at, depth + 1);
        if (best < 0)
        {
            int jr = _r.JettyReachTiles;
            for (int dy = -jr; dy <= jr; dy++)
                for (int dx = -jr; dx <= jr; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var s = at(x + dx, y + dy, z - 1);
                    if (s != null && PieceCosts.IsSupport(s.Kind)) best = Math.Max(best, Bear(x + dx, y + dy, z - 1, at, depth + 1));
                }
        }
        return best < 0 ? -1 : Math.Min(own, best);
    }

    /// <summary>
    /// What a beam at (x, y, z) bears when its run (the straight line of beams through it, along x or along y) is carried: supports under or one tile beside both ends, a clear span of
    /// at most <c>beam_span_tiles</c>; the run bears the least of its end supports. -1 when no run through it is carried. <paramref name="self"/> stands in for the cell itself (a ghost
    /// being checked before it is there).
    /// </summary>
    public int BeamBear(int x, int y, int z, CellAt at, PieceCost? self = null)
    {
        int best = -1;
        foreach (var (ux, uy) in new[] { (1, 0), (0, 1) })
        {
            bool IsBeam(int i) => i == 0 ? (self ?? at(x, y, z))?.Kind == PieceKind.Beam : at(x + ux * i, y + uy * i, z)?.Kind == PieceKind.Beam;
            int lo = 0, hi = 0;
            while (lo > -16 && IsBeam(lo - 1)) lo--;
            while (hi < 16 && IsBeam(hi + 1)) hi++;
            int a = EndBear(x + ux * lo, y + uy * lo, z, at), b = EndBear(x + ux * hi, y + uy * hi, z, at);
            if (hi == lo || a < 0 || b < 0) continue;              // a span is two cells or more
            if (hi - lo > _r.BeamSpanTiles) continue;
            best = Math.Max(best, Math.Min(a, b));
        }
        return best;
    }

    private int EndBear(int x, int y, int z, CellAt at)
    {
        int best = -1;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var s = at(x + dx, y + dy, z - 1);
                if (s != null && PieceCosts.IsSupport(s.Kind)) best = Math.Max(best, Bear(x + dx, y + dy, z - 1, at, 1));
            }
        return best;
    }
}
