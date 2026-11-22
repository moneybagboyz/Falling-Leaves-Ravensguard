using Ravensguard.Sim.Core;
using Ravensguard.Sim.Travel;

namespace Ravensguard.Sim.Armies;

/// <summary>A warband as the main thread sees it (a copy made at the barrier): who, doing what, where in millimetres of the home region's frame (the frame the street view and the travel feed use), with which men.</summary>
public sealed record WarbandView(int Id, ulong Lord, int Fief, WarbandTask Task, long XMm, long YMm, ulong[] Men, int Morale);

/// <summary>A band of bandits as the main thread sees it: where it lurks (millimetres of the home region's frame) and its men.</summary>
public sealed record BandView(int Id, long XMm, long YMm, ulong[] Men, BandOrigin Origin);

/// <summary>
/// The armies as published at a barrier (the pattern of the caravan snapshot): immutable, read by the street view's encounter cast and the travel feed from the main thread while the tick worker goes on. The Army tab
/// and the map's pennants (AM-E) read the same copy.
/// </summary>
public sealed class ArmySnapshot
{
    public static readonly ArmySnapshot Empty = new([], []);
    public WarbandView[] Warbands { get; }
    public BandView[] Bands { get; }
    public ArmySnapshot(WarbandView[] warbands, BandView[] bands) { Warbands = warbands; Bands = bands; }

    public static ArmySnapshot Of(ArmyWorld aw)
    {
        if (aw.Book.Warbands.Count == 0 && aw.Book.Bands.Count == 0) return Empty;
        var ws = new List<WarbandView>(); var bs = new List<BandView>();
        foreach (var w in aw.Book.Warbands.Values)
            if (w.Members.Count > 0 && aw.PositionOf(w) is { } p) ws.Add(new WarbandView(w.Id, w.Lord, w.Fief, w.Task, p.XMm - aw.OriginXMm, p.YMm - aw.OriginYMm, [.. w.Members], w.Morale));
        foreach (var b in aw.Book.Bands.Values)
            if (b.Lair >= 0 && b.Members.Count > 0) { var (x, y) = aw.Center(b.Lair); bs.Add(new BandView(b.Id, x * 1000L, y * 1000L, [.. b.Members], b.Origin)); }
        return new ArmySnapshot([.. ws], [.. bs]);
    }

    /// <summary>The warband nearest a point (millimetres of the home region's frame) within <paramref name="reachM"/> metres, ties by id; null when none is.</summary>
    public (int DistM, WarbandView Wb)? NearestWarband(long xMm, long yMm, int reachM)
    {
        WarbandView? best = null; long bd = long.MaxValue;
        foreach (var w in Warbands) { long d = RoutePath.Dist(w.XMm - xMm, w.YMm - yMm); if (d <= reachM * 1000L && d < bd) { bd = d; best = w; } }
        return best == null ? null : ((int)(bd / 1000), best);
    }

    public (int DistM, BandView Band)? NearestBand(long xMm, long yMm, int reachM)
    {
        BandView? best = null; long bd = long.MaxValue;
        foreach (var b in Bands) { long d = RoutePath.Dist(b.XMm - xMm, b.YMm - yMm); if (d <= reachM * 1000L && d < bd) { bd = d; best = b; } }
        return best == null ? null : ((int)(bd / 1000), best);
    }
}

/// <summary>
/// Who plays an encounter's groups when armies are on the map (armies-on-the-map 3.4, 4.4): the ambushers and toll-takers of the road (<c>bandit</c>) are the men of the band whose lair is nearest, when a band lives
/// within reach of the point (Phase 3's generated cast stands in when none does), and the riders of the road (<c>guard</c>, <c>soldier</c>, <c>warband</c>) are the men of the warband that rides nearest. Everything else is
/// the inner cast's (caravan crews and vagrants). Pure reads of the published snapshot: the same arguments always pick the same people, lowest ids first.
/// </summary>
public sealed class ArmyCast(IEncounterCast? inner, Func<ArmySnapshot?> armies, int bandReachM = 4000, int warbandReachM = 1500) : IEncounterCast
{
    public static readonly string[] BandArchetypes = ["bandit"];
    public static readonly string[] WarbandArchetypes = ["guard", "soldier", "warband"];

    public IReadOnlyList<EntityId> Existing(string archetype, int count, long sec, long xMm, long yMm)
    {
        var snap = armies();
        if (snap != null)
        {
            if (Array.IndexOf(BandArchetypes, archetype) >= 0 && snap.NearestBand(xMm, yMm, bandReachM) is { } band && Take(band.Band.Men, count) is { Count: > 0 } a) return a;
            if (Array.IndexOf(WarbandArchetypes, archetype) >= 0 && snap.NearestWarband(xMm, yMm, warbandReachM) is { } wb && Take(wb.Wb.Men, count) is { Count: > 0 } b) return b;
        }
        return inner?.Existing(archetype, count, sec, xMm, yMm) ?? [];
    }

    private static List<EntityId> Take(ulong[] men, int count) => [.. men.OrderBy(x => x).Take(count).Select(m => new EntityId(m))];
}
