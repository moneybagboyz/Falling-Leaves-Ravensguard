using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Armies;

/// <summary>One piece of a camp's plan: a content piece id and where it stands in the camp's own frame (millimetres from the camp's centre, x along the road's heading).</summary>
public readonly record struct CampPiece(string Piece, int XMm, int YMm, int Rot);

/// <summary>
/// The plan of a host's camp (armies-on-the-map 4.5), a pure function of the host, the camp's serial, the seed and the men: tents of six in rows (the jitter of the pitch is stream 0x7A6, keyed on the host and the camp),
/// the commander's pavilion at the front, a fire for three tents, latrines at the back, horse lines at the side, and for a stay of two nights or more the palisade and its ditch round the lot. The pieces are the content's own
/// (<c>tent_soldier</c>, <c>tent_pavilion</c>, <c>campfire</c>, <c>horse_line</c>, <c>latrine</c>, <c>palisade</c>, <c>ditch_shallow</c>). Pitching takes worker-seconds by piece (a tent a quarter of a worker-hour) shared by the men
/// who are not on watch, so a thousand men have a camp up in minutes. Every number is an integer; nothing here draws except the one jitter stream.
/// </summary>
public sealed class CampPlan
{
    public int Men, Animals, Tents, Fires, Latrines, Lines;
    /// <summary>The pieces in pitch order (pavilion, fires, tents, horse lines, latrines) and the ring that goes up later (palisade then ditch), and the camp's half extents (the ring stands just outside them).</summary>
    public CampPiece[] Pieces = [], Ring = [];
    public (int X, int Y)[] TentAt = [];
    public int HalfXMm, HalfYMm;
    private long[] _cum = [];

    /// <summary>The worker-seconds a piece takes.</summary>
    public static int WorkSec(string piece) => piece switch
    {
        "tent_soldier" => MarchConfig.PitchSecPerTent, "campfire" => MarchConfig.PitchSecPerFire, "horse_line" => MarchConfig.PitchSecPerLine, "latrine" => MarchConfig.PitchSecPerLatrine,
        "tent_pavilion" => MarchConfig.PitchSecPavilion, "palisade" => 4 * 3600, "ditch_shallow" => 3600, _ => 900,
    };

    public static CampPlan For(ulong seed, int host, int serial, int men, int animals)
    {
        var p = new CampPlan { Men = Math.Max(0, men), Animals = Math.Max(0, animals) };
        int tents = (p.Men + MarchConfig.MenPerTent - 1) / MarchConfig.MenPerTent;
        p.Tents = tents; p.Fires = (tents + MarchConfig.TentsPerFire - 1) / MarchConfig.TentsPerFire;
        p.Latrines = p.Men >= 10 ? (p.Men + MarchConfig.MenPerLatrine - 1) / MarchConfig.MenPerLatrine : 0;
        p.Lines = (p.Animals + MarchConfig.AnimalsPerLine - 1) / MarchConfig.AnimalsPerLine;
        int cols = Math.Max(1, (int)CaravanPathSqrt(tents * 3 / 2)), rows = tents == 0 ? 0 : (tents + cols - 1) / cols;
        const int dx = 6000, dy = 7000;
        ulong entity = ((ulong)(uint)host << 20) ^ (uint)serial;
        int draw = 0;
        int J(int span) => DetRng.Range(seed, serial, entity, RngStreams.CampPitchJitter, (uint)draw++, -span, span + 1);
        int x0 = -(cols - 1) * dx / 2, y0 = -(rows - 1) * dy / 2;
        var list = new List<CampPiece>(); p.TentAt = new (int, int)[tents];
        if (p.Men >= 20) list.Add(new CampPiece("tent_pavilion", J(500), y0 - dy * 2 + J(500), 0));
        for (int f = 0; f < p.Fires; f++)
        {
            int t = Math.Min(tents - 1, f * MarchConfig.TentsPerFire + 1);
            int cx = x0 + t % cols * dx, cy = y0 + t / cols * dy;
            list.Add(new CampPiece("campfire", cx + J(400), cy + dy / 2 + J(400), 0));
        }
        for (int t = 0; t < tents; t++)
        {
            int px = x0 + t % cols * dx + J(600), py = y0 + t / cols * dy + J(600);
            p.TentAt[t] = (px, py);
            list.Add(new CampPiece("tent_soldier", px, py, 0));
        }
        int hx = x0 + cols * dx;
        for (int l = 0; l < p.Lines; l++) list.Add(new CampPiece("horse_line", hx + 4000 + J(400), y0 + l * 8000 + J(400), 0));
        for (int l = 0; l < p.Latrines; l++) list.Add(new CampPiece("latrine", x0 + l * 5000 + J(400), y0 + rows * dy + 6000 + J(400), 0));
        p.Pieces = [.. list];
        p._cum = new long[p.Pieces.Length]; long c = 0;
        for (int i = 0; i < p.Pieces.Length; i++) { c += WorkSec(p.Pieces[i].Piece); p._cum[i] = c; }
        p.HalfXMm = Math.Max(10_000, (cols * dx) / 2 + 12_000 + (p.Lines > 0 ? 8_000 : 0)); p.HalfYMm = Math.Max(10_000, (rows * dy) / 2 + 14_000);
        // the ring: a palisade piece every 2 m of the perimeter, the ditch just outside it
        var ring = new List<CampPiece>();
        void Perimeter(string piece, int hxm, int hym)
        {
            for (int x = -hxm; x < hxm; x += MarchConfig.PalisadeStepMm) { ring.Add(new CampPiece(piece, x, -hym, 0)); ring.Add(new CampPiece(piece, x + MarchConfig.PalisadeStepMm, hym, 0)); }
            for (int y = -hym + MarchConfig.PalisadeStepMm; y < hym; y += MarchConfig.PalisadeStepMm) { ring.Add(new CampPiece(piece, -hxm, y, 90)); ring.Add(new CampPiece(piece, hxm, y, 90)); }
        }
        if (p.Men >= 20) { Perimeter("palisade", p.HalfXMm, p.HalfYMm); Perimeter("ditch_shallow", p.HalfXMm + 2000, p.HalfYMm + 2000); }
        p.Ring = [.. ring];
        return p;
    }

    private static long CaravanPathSqrt(long n) => Ravensguard.Sim.Caravans.CaravanPath.Isqrt(n);

    /// <summary>Total worker-seconds of the pitch (the plan without its ring), and with it.</summary>
    public long PitchWorkSec => _cum.Length == 0 ? 0 : _cum[^1];
    public long RingWorkSec { get { long t = 0; foreach (var r in Ring) t += WorkSec(r.Piece); return t; } }

    /// <summary>The game seconds a camp takes to pitch with this many hands (rounded up).</summary>
    public long PitchSeconds(int workers) => (PitchWorkSec + Math.Max(1, workers) - 1) / Math.Max(1, workers);

    /// <summary>How many pieces of the plan stand <paramref name="elapsedSec"/> seconds after the host arrived with <paramref name="workers"/> hands to work on it.</summary>
    public int PiecesUp(long elapsedSec, int workers)
    {
        long done = Math.Max(0, elapsedSec) * Math.Max(1, workers);
        int lo = 0, hi = _cum.Length;
        while (lo < hi) { int mid = (lo + hi) / 2; if (_cum[mid] <= done) lo = mid + 1; else hi = mid; }
        return lo;
    }

    /// <summary>How many pieces of the ring stand when the stay's second day began that long ago (the whole host works at it after its pitch).</summary>
    public int RingUp(long elapsedSec, int workers)
    {
        long done = Math.Max(0, elapsedSec) * Math.Max(1, workers); int n = 0;
        foreach (var r in Ring) { done -= WorkSec(r.Piece); if (done < 0) break; n++; }
        return n;
    }

    /// <summary>Where man number <paramref name="rank"/> (0-based in the camp's order) sleeps: in his tent, one of six places about it. Millimetres in the camp's frame.</summary>
    public (int X, int Y) SleeperAt(int rank)
    {
        if (TentAt.Length == 0) return (0, 0);
        int t = Math.Clamp(rank / MarchConfig.MenPerTent, 0, TentAt.Length - 1), k = rank % MarchConfig.MenPerTent;
        var (x, y) = TentAt[t];
        return (x - 1200 + k % 3 * 1200, y - 900 + k / 3 * 1800);
    }

    /// <summary>Where sentry <paramref name="k"/> of <paramref name="total"/> stands: evenly along the rectangle just outside the camp (or its ring).</summary>
    public (int X, int Y) SentryAt(int k, int total)
    {
        int hx = HalfXMm + 4000, hy = HalfYMm + 4000;
        long per = 4L * (hx + hy);
        long s = per * Math.Clamp(k, 0, Math.Max(0, total - 1)) / Math.Max(1, total);
        long a = 2L * hx;
        if (s < a) return ((int)(-hx + s), -hy);
        s -= a; if (s < 2L * hy) return (hx, (int)(-hy + s));
        s -= 2L * hy; if (s < a) return ((int)(hx - s), hy);
        s -= a; return (-hx, (int)(hy - s));
    }

    /// <summary>The sentries a camp of <paramref name="men"/> posts on the watch <paramref name="watch"/> of night <paramref name="night"/> (4.5): men whose rank is congruent to <c>(3 x night + watch) mod 25</c>, one in 25 of the camp, a different group each watch and each night.</summary>
    public static int[] SentryRanks(int men, int night, int watch)
    {
        int r = (MarchConfig.Watches * night + watch) % MarchConfig.SentryOneIn;
        var l = new List<int>();
        for (int k = r; k < men; k += MarchConfig.SentryOneIn) l.Add(k);
        return [.. l];
    }

    /// <summary>The number of sentries on a watch: the men of the group (one in 25 of the camp: 40 of a thousand).</summary>
    public static int SentryCount(int men, int night = 0, int watch = 0) => SentryRanks(men, night, watch).Length;

    /// <summary>The watch (0 to 2) an hour of the night belongs to: 21:00 to 23:59 the first, then three hours, then the rest to 05:00.</summary>
    public static int WatchOf(int hourOfDay) => Math.Clamp((hourOfDay - MarchConfig.DayEndHour + 24) % 24 * MarchConfig.Watches / (24 - MarchConfig.DayEndHour + MarchConfig.DayStartHour), 0, MarchConfig.Watches - 1);
}
