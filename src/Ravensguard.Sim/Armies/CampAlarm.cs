using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Armies;

/// <summary>What a night attack did to a camp's sleepers: who saw it first and when, and the game second each man was awake by (rank in the camp's order; men on watch were awake already).</summary>
public sealed class AlarmResult
{
    public int Sentry = -1;
    public long AlarmSec = -1;
    public SortedDictionary<int, long> WakeSec { get; } = [];
    public int Asleep, Woken;
    public long LastWakeSec => WakeSec.Count == 0 ? -1 : WakeSec.Values.Max();
    public bool Raised => Sentry >= 0;
}

/// <summary>
/// The alarm rule of a camp at night (armies-on-the-map 4.5): a sleeping fighter wakes when hit, at an alarm within 30 m of him, or on the wake jitter (stream 0x424, 5 to 20 game seconds, keyed on the second of the call and
/// the sleeper). A sentry who sees a man within the night's sight (60 m in the dark) shouts; every sleeper within 30 m of the shouter stirs 5 to 20 seconds on, and each man who wakes shouts in his turn, so the alarm runs through
/// the camp from tent to tent. It is a pure function of the plan, the seed and the inputs: the same night gives the same wake times.
/// </summary>
public static class CampAlarm
{
    /// <summary>
    /// Runs the alarm. <paramref name="sentries"/> are the ranks on watch (awake), <paramref name="attacker"/> the first man of the enemy to come into sight (camp frame, millimetres) and <paramref name="sightSec"/> the second he
    /// does; <paramref name="hits"/> are sleepers struck before the alarm reached them (rank to the second). <paramref name="key"/> keys the jitter (the host's id).
    /// </summary>
    public static AlarmResult Run(CampPlan plan, int men, IReadOnlyList<int> sentries, ulong seed, ulong key, (int X, int Y) attacker, long sightSec, IReadOnlyDictionary<int, long>? hits = null, int sightM = MarchConfig.NightSightM)
    {
        var res = new AlarmResult();
        var awake = new HashSet<int>(sentries.Where(r => r < men));
        res.Asleep = men - awake.Count;
        long sight2 = (long)sightM * 1000 * sightM * 1000;
        int sentry = -1; long best = long.MaxValue;
        for (int k = 0; k < sentries.Count; k++)
        {
            int r = sentries[k]; if (r >= men) continue;
            var (sx, sy) = plan.SentryAt(k, sentries.Count);
            long d2 = Sq(sx - attacker.X) + Sq(sy - attacker.Y);
            if (d2 <= sight2 && (d2 < best || (d2 == best && r < sentry))) { best = d2; sentry = r; }
        }
        // who stands where: the sleepers by cell of a 30 m grid
        int cell = MarchConfig.AlarmM * 1000;
        var pos = new (int X, int Y)[men]; var grid = new Dictionary<(int, int), List<int>>();
        for (int r = 0; r < men; r++)
        {
            pos[r] = plan.SleeperAt(r);
            var c = (Div(pos[r].X, cell), Div(pos[r].Y, cell));
            if (!grid.TryGetValue(c, out var l)) grid[c] = l = [];
            l.Add(r);
        }
        var wake = new SortedDictionary<int, long>(); var queue = new PriorityQueue<(int Rank, bool Shout), (long, int, int)>();
        long Jitter(int rank, long at) => DetRng.Range(seed, at, key ^ ((ulong)(uint)rank << 32), RngStreams.WakeJitter, 0, MarchConfig.WakeMinSec, MarchConfig.WakeMaxSec + 1);
        void Shout(int who, (int X, int Y) at, long sec)
        {
            int cx = Div(at.X, cell), cy = Div(at.Y, cell); long r2 = (long)MarchConfig.AlarmM * 1000 * MarchConfig.AlarmM * 1000;
            for (int gx = cx - 1; gx <= cx + 1; gx++)
                for (int gy = cy - 1; gy <= cy + 1; gy++)
                    if (grid.TryGetValue((gx, gy), out var l))
                        foreach (int r in l)
                        {
                            if (awake.Contains(r) || r == who) continue;
                            if (Sq(pos[r].X - at.X) + Sq(pos[r].Y - at.Y) > r2) continue;
                            long t = sec + Jitter(r, sec);
                            if (!wake.TryGetValue(r, out long old) || t < old) { wake[r] = t; queue.Enqueue((r, true), (t, r, 1)); }
                        }
        }
        if (hits != null) foreach (var (r, sec) in hits.OrderBy(h => h.Key)) if (r < men && !awake.Contains(r) && (!wake.TryGetValue(r, out long o) || sec < o)) { wake[r] = sec; queue.Enqueue((r, true), (sec, r, 0)); }
        if (sentry >= 0)
        {
            res.Sentry = sentry; res.AlarmSec = sightSec;
            var (sx, sy) = plan.SentryAt(Array.IndexOf(sentries.ToArray(), sentry), sentries.Count);
            Shout(-1, (sx, sy), sightSec);
        }
        var settled = new HashSet<int>();
        while (queue.Count > 0)
        {
            queue.TryDequeue(out var item, out var pr);
            int r = item.Rank; if (settled.Contains(r) || wake[r] != pr.Item1) continue;
            settled.Add(r); awake.Add(r);
            Shout(r, pos[r], pr.Item1);                                   // a man who wakes shouts in his turn
        }
        foreach (var (r, t) in wake) res.WakeSec[r] = t;
        res.Woken = res.WakeSec.Count;
        return res;
    }

    private static long Sq(long v) => v * v;
    private static int Div(int v, int d) => v >= 0 ? v / d : -((-v + d - 1) / d);

    /// <summary>Wakes the sleepers of a live fight: every fighter of <paramref name="wakes"/> (fight id to the game second he stirs) who is <see cref="FAct.Asleep"/> and whose second has come goes to <see cref="FAct.Idle"/>. Returns how many woke.</summary>
    public static int WakeFighters(FightWorld w, IReadOnlyDictionary<int, long> wakes, long nowSec)
    {
        int n = 0;
        foreach (var f in w.Fighters)
            if (f.Act == FAct.Asleep && wakes.TryGetValue(f.Id, out long t) && t <= nowSec) { f.Act = FAct.Idle; f.ActTicks = 0; n++; }
        return n;
    }
}
