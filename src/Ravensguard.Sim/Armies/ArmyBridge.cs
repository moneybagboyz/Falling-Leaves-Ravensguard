using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;

namespace Ravensguard.Sim.Armies;

/// <summary>What a live part of a host is. Append only.</summary>
public enum ColumnPartKind : byte { Van, Foot, Wagon, Sleeper, Sentry, Camper }

/// <summary>One live part of a host's column or camp: where it stands (continent millimetres), which way it faces (unit vector x1024), and who it is (a person, or a wagon by its index).</summary>
public readonly record struct ColumnPart(int Host, ColumnPartKind Kind, int Index, ulong Person, long XMm, long YMm, int HeadX, int HeadY, bool Asleep = false);

/// <summary>A camp inside a bubble: where it is (its centre), its heading and the pieces of its plan that stand now, each as an offset from the centre already turned to the heading (the pitching is a function of the time since the host arrived and the hands it had).</summary>
public sealed record LiveCamp(int Host, long XMm, long YMm, int HeadX, int HeadY, int Serial, int Men, bool Palisade, IReadOnlyList<CampPiece> Pieces, int PlanPieces);

/// <summary>The column's measures (4.4): three abreast with 1.5 m between ranks.</summary>
public static class MarchLayout
{
    /// <summary>The length in metres of a column of <paramref name="men"/> men on foot (a thousand: 501 m); the baggage follows it, wagons 12 m apart.</summary>
    public static int ColumnLengthM(int men) => (men + MarchConfig.Abreast - 1) / MarchConfig.Abreast * MarchConfig.RankMm / 1000;
}

/// <summary>What the bubble holds of the armies at one moment.</summary>
public sealed record ArmyFrame(long NowSec, IReadOnlyList<ColumnPart> Parts, IReadOnlyList<LiveCamp> Camps, int Promoted, int Demoted);

/// <summary>
/// The LODBridge for hosts (armies-on-the-map 4.4, 4.5; the pattern of <c>CaravanBridge</c>): decides which men and wagons of the hosts on the road the bubbles hold, and where each stands. It owns no state of the record:
/// the place of every man is a pure function of the march (<see cref="HostMotion"/>) and the game second, so a promoted column can never drift from the abstract one and a demoted one hands nothing back (the men's
/// wounds live in the world's ledger already; <see cref="ArmyWorld.SyncDead"/> takes the dead off the host's rolls). A part is promoted when it comes within 150 m of a bubble's centre and demoted when it has stayed beyond
/// 200 m for 5 real seconds (the people bridge's rule), at most 50 a frame, nearest first, so an arriving army appears as a column marching into view. The column: a van of scouts, the foot three abreast with 1.5 m between
/// ranks (1,000 men: 500 m), then the baggage, wagons 12 m apart. By night the host is its camp: sleepers in their tents, one man in 25 on watch at the edge.
/// </summary>
public sealed class ArmyBridge
{
    public const int MaxPerFrame = Bubble.MaxPromotionsPerFrame;

    private readonly Dictionary<(int Host, ulong Person, int Wagon), long> _live = [];
    private readonly Dictionary<(int Host, ulong Person, int Wagon), (int Kind, int Index)> _kinds = [];
    public int Promotions { get; private set; }
    public int Demotions { get; private set; }
    public int MaxPromotedInAFrame { get; private set; }
    public int LiveCount => _live.Count;
    public IReadOnlyCollection<(int Host, ulong Person, int Wagon)> Live => _live.Keys;

    private readonly record struct Cand(int Host, ColumnPartKind Kind, int Index, ulong Person, int Wagon, long X, long Y, int Hx, int Hy, bool Asleep, long D2);

    /// <summary>The parts of every host on the road (and its camp) that the bubbles hold at <paramref name="nowSec"/>.</summary>
    public ArmyFrame Update(ArmyWorld aw, IReadOnlyList<(long X, long Y)> centres, long nowSec)
    {
        var all = new List<Cand>(); var camps = new List<LiveCamp>();
        var centreArr = centres.Take(Bubble.MaxBubbles).ToArray();
        foreach (var m in aw.Book.Marches.Values)
        {
            if (m.State == MarchState.Left) continue;
            var route = aw.RouteOf(m); if (route == null) continue;
            var persons = aw.ColumnPersons(new WarSupply.HostId(m.Host));
            int wagons = aw.Supply is { } hs0 && hs0.Exists(new WarSupply.HostId(m.Host)) ? hs0.Account(new WarSupply.HostId(m.Host)).Carts : 0;
            if (persons.Count == 0 && wagons == 0 && m.Camp == null) continue;
            bool camped = m.State is MarchState.Camped or MarchState.Arrived && m.Camp != null;
            long dist = Math.Clamp(HostMotion.DistAt(m, nowSec), 0, route.Length);
            var head = route.Path.At(dist);
            long extent = camped ? 120_000 : (long)((persons.Count + MarchConfig.Abreast - 1) / MarchConfig.Abreast) * MarchConfig.RankMm + wagons * MarchConfig.WagonMm + MarchConfig.VanGapMm + MarchConfig.BaggageGapMm;
            long cx = camped ? m.Camp!.XMm : head.XMm, cy = camped ? m.Camp!.YMm : head.YMm;
            bool anyLive = _live.Keys.Any(k => k.Host == m.Host);
            bool near = false;
            foreach (var c in centreArr) if (Bubble.Sq(c.X - cx) + Bubble.Sq(c.Y - cy) <= Bubble.Sq(Bubble.PromoteM * 1000L + extent + 100_000)) near = true;
            if (!near && !anyLive) continue;
            if (camped) CampParts(aw, m, persons, nowSec, all, centreArr, camps);
            else ColumnParts(m, route, persons, wagons, dist, all, centreArr);
        }
        // promotion: the ones inside 150 m of a centre who are not live yet, nearest first (ties by host, kind, index), at most 50 a frame
        int promoted = 0;
        var seen = new HashSet<(int, ulong, int)>();
        foreach (var c in all.OrderBy(c => c.D2).ThenBy(c => c.Host).ThenBy(c => (int)c.Kind).ThenBy(c => c.Index))
        {
            var key = (c.Host, c.Person, c.Wagon);
            if (_live.ContainsKey(key)) continue;
            if (!Bubble.InsidePromote(c.D2)) break;
            if (promoted >= MaxPerFrame) break;
            _live[key] = -1; _kinds[key] = ((int)c.Kind, c.Index); promoted++; Promotions++;
        }
        MaxPromotedInAFrame = Math.Max(MaxPromotedInAFrame, promoted);
        // the live ones: where they stand now, and the demotion timer
        var parts = new List<ColumnPart>(); int demoted = 0;
        foreach (var c in all.OrderBy(c => c.Host).ThenBy(c => (int)c.Kind).ThenBy(c => c.Index))
        {
            var key = (c.Host, c.Person, c.Wagon);
            if (!_live.TryGetValue(key, out long since)) continue;
            bool drop = Bubble.ShouldDemote(c.D2, nowSec, ref since);
            if (drop) { _live.Remove(key); _kinds.Remove(key); demoted++; Demotions++; continue; }
            _live[key] = since; seen.Add(key);
            parts.Add(new ColumnPart(c.Host, c.Kind, c.Index, c.Person, c.X, c.Y, c.Hx, c.Hy, c.Asleep));
        }
        // a man who is no longer on any host's roll (dead, gone with the host out of the region) leaves the bubble with no fold-back: his record is already right
        foreach (var key in _live.Keys.Where(k => !seen.Contains(k)).ToList()) { _live.Remove(key); _kinds.Remove(key); demoted++; Demotions++; }
        return new ArmyFrame(nowSec, parts, camps, promoted, demoted);
    }

    private static (long D2, bool) Near(IReadOnlyList<(long X, long Y)> centres, long x, long y)
    {
        long best = long.MaxValue;
        foreach (var c in centres) best = Math.Min(best, Bubble.Sq(c.X - x) + Bubble.Sq(c.Y - y));
        return (best, best != long.MaxValue);
    }

    private static void ColumnParts(HostMarch m, HostRoute route, List<ulong> persons, int wagons, long dist, List<Cand> all, (long X, long Y)[] centres)
    {
        int n = persons.Count; int van = n >= 20 ? Math.Max(1, n * MarchConfig.VanSharePct / 100) : 0; int foot = n - van;
        long Clamp(long d) => Math.Clamp(d, 0, route.Length);
        void Add(ColumnPartKind kind, int index, ulong person, int wagon, long d, int lane)
        {
            var p = route.Path.At(Clamp(d));
            long x = p.XMm + (long)p.HeadY * lane / 1024, y = p.YMm - (long)p.HeadX * lane / 1024;
            all.Add(new Cand(m.Host, kind, index, person, wagon, x, y, p.HeadX, p.HeadY, false, Near(centres, x, y).D2));
        }
        for (int i = 0; i < van; i++) Add(ColumnPartKind.Van, i, persons[i], -1, dist + MarchConfig.VanGapMm - (long)(i / MarchConfig.Abreast) * MarchConfig.RankMm, (i % MarchConfig.Abreast - 1) * MarchConfig.LaneMm);
        for (int j = 0; j < foot; j++) Add(ColumnPartKind.Foot, van + j, persons[van + j], -1, dist - (long)(j / MarchConfig.Abreast) * MarchConfig.RankMm, (j % MarchConfig.Abreast - 1) * MarchConfig.LaneMm);
        long footLen = (long)((foot + MarchConfig.Abreast - 1) / MarchConfig.Abreast) * MarchConfig.RankMm;
        for (int w = 0; w < wagons; w++) Add(ColumnPartKind.Wagon, w, 0, w, dist - footLen - MarchConfig.BaggageGapMm - (long)w * MarchConfig.WagonMm, 0);
    }

    private static void CampParts(ArmyWorld aw, HostMarch m, List<ulong> persons, long nowSec, List<Cand> all, (long X, long Y)[] centres, List<LiveCamp> camps)
    {
        var c = m.Camp!; var hs = aw.Supply!; var a = hs.Account(new WarSupply.HostId(m.Host));
        var plan = aw.PlanOf(m, c, a);
        int hod = (int)(nowSec / 3600 % 24); bool night = hod >= MarchConfig.DayEndHour || hod < MarchConfig.DayStartHour;
        long Wx(int lx, int ly) => c.XMm + ((long)lx * c.HeadX - (long)ly * c.HeadY) / 1024;
        long Wy(int lx, int ly) => c.YMm + ((long)lx * c.HeadY + (long)ly * c.HeadX) / 1024;
        var sent = night ? CampPlan.SentryRanks(c.Men, Math.Max(0, c.Nights - 1), CampPlan.WatchOf(hod)) : [];
        var sentSet = new HashSet<int>(sent);
        for (int r = 0; r < persons.Count; r++)
        {
            bool sentry = sentSet.Contains(r); int k = sentry ? Array.IndexOf(sent, r) : 0;
            var (lx, ly) = sentry ? plan.SentryAt(k, sent.Length) : plan.SleeperAt(r);
            long x = Wx(lx, ly), y = Wy(lx, ly);
            all.Add(new Cand(m.Host, sentry ? ColumnPartKind.Sentry : night ? ColumnPartKind.Sleeper : ColumnPartKind.Camper, r, persons[r], -1, x, y, c.HeadX, c.HeadY, night && !sentry, Near(centres, x, y).D2));
        }
        // the pieces up: the plan's, by the time since arrival and the hands working (every man not on watch), then the ring when the camp is a stay
        int workers = Math.Max(1, c.Men - sent.Length);
        int up = plan.PiecesUp(nowSec - c.SinceSec, workers);
        var pieces = new List<CampPiece>(up);
        for (int i = 0; i < up; i++) { var p = plan.Pieces[i]; pieces.Add(new CampPiece(p.Piece, (int)(Wx(p.XMm, p.YMm) - c.XMm), (int)(Wy(p.XMm, p.YMm) - c.YMm), p.Rot)); }
        if (c.Palisade) { int ring = plan.RingUp(nowSec - c.SinceSec - plan.PitchSeconds(workers), workers); for (int i = 0; i < ring; i++) { var p = plan.Ring[i]; pieces.Add(new CampPiece(p.Piece, (int)(Wx(p.XMm, p.YMm) - c.XMm), (int)(Wy(p.XMm, p.YMm) - c.YMm), p.Rot)); } }
        var (d2, _) = Near(centres, c.XMm, c.YMm);
        long reach = Bubble.PromoteM * 1000L + Math.Max(plan.HalfXMm, plan.HalfYMm) + 20_000;
        if (d2 <= Bubble.Sq(reach)) camps.Add(new LiveCamp(m.Host, c.XMm, c.YMm, c.HeadX, c.HeadY, c.Serial, c.Men, c.Palisade, pieces, plan.Pieces.Length));
    }
}
