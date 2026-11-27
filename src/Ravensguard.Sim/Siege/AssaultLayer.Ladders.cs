#pragma warning disable CS0675   // hash mixing: sign extension of a small id is deterministic and harmless here
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Siege;

public enum LadderStage : byte { Carried, Raising, Standing, Down }

/// <summary>A climber on a ladder: the fighter and the ticks he has climbed (of the content's 80).</summary>
public sealed class Climb { public int Id, Progress; }

/// <summary>
/// A ladder (sieges-and-engines 3.3, 4.4): carried by two, raised against the wall in 4 s, then a way from the ground (foot cell) to the wall-walk (top cell) that holds three climbers of 4 s each, until a defender at its top pushes it off.
/// </summary>
public sealed class SiegeLadder
{
    public int Id, Team;
    /// <summary>The foot cell at ground level and the top cell on the walk (world cells).</summary>
    public int FootX, FootY, TopX, TopY;
    public LadderStage Stage;
    public long StageSince;
    public List<int> Carriers = [];
    /// <summary>Fighters waiting to climb, in order.</summary>
    public List<int> Queue = [];
    public List<Climb> Climbers = [];
    public int Pusher = -1, Pusher2 = -1, PushTries;
    /// <summary>P8-LW-1: a ladder with a hook holds against a push with 300 permille (0x850).</summary>
    public bool Hooked;
    public long PushSince;
    public int Pushed;
}

public sealed partial class AssaultLayer
{
    private readonly List<SiegeLadder> _ladders = [];
    private int _nextLadder = 1;
    public IReadOnlyList<SiegeLadder> Ladders => _ladders;
    private SiegeLadder? Ladder(int id) { foreach (var l in _ladders) if (l.Id == id) return l; return null; }

    /// <summary>
    /// A ladder party sets out: two carriers (who raise it in 4 s at its foot cell, then climb behind the rest) and the climbers in order. Its top is the first walkable cell of the wall-walk level behind <paramref name="topX"/>, <paramref name="topY"/> (a parapet in
    /// the way is climbed over, through a crenel).
    /// </summary>
    public SiegeLadder AddLadder(int team, int footX, int footY, int topX, int topY, IEnumerable<int> carriers, IEnumerable<int> climbers)
    {
        var l = new SiegeLadder { Id = _nextLadder++, Team = team, FootX = footX, FootY = footY, TopX = topX, TopY = topY, Stage = LadderStage.Carried, StageSince = World.Tick, Carriers = [.. carriers], Queue = [.. climbers] };
        _ladders.Add(l);
        return l;
    }

    /// <summary>A ladder already standing (a scene opened mid-assault, a restore).</summary>
    public SiegeLadder Stand(SiegeLadder l) { l.Stage = LadderStage.Standing; l.StageSince = World.Tick; return l; }

    private void LaddersStep(int t)
    {
        foreach (var l in _ladders)
        {
            switch (l.Stage)
            {
                case LadderStage.Carried: Carry(l, t); break;
                case LadderStage.Raising:
                    if (t - l.StageSince >= R.LadderRaiseTicks)
                    {
                        l.Stage = LadderStage.Standing; l.StageSince = t;
                        foreach (int c in l.Carriers) { Release(c); l.Queue.Add(c); }
                        l.Carriers = [];
                    }
                    break;
                case LadderStage.Standing: Stood(l, t); break;
            }
        }
    }

    private void Carry(SiegeLadder l, int t)
    {
        l.Carriers.RemoveAll(c => !World.Fighters[c].Active);
        if (l.Carriers.Count == 0) { l.Stage = LadderStage.Down; return; }
        long fx = CentreMm(l.FootX), fy = CentreMm(l.FootY);
        bool all = true;
        foreach (int c in l.Carriers)
        {
            var f = World.Fighters[c];
            if (DistTo(f, fx, fy) > 1500) all = false;
            if (Free(f)) March(f, fx, fy);
        }
        if (!all) return;
        l.Stage = LadderStage.Raising; l.StageSince = t;
        foreach (int c in l.Carriers) HoldFor(c, HoldKind.Raise, l.Id, 0);
    }

    private (long X, long Y) TopMm(SiegeLadder l) => (CentreMm(l.TopX), CentreMm(l.TopY));

    private void Stood(SiegeLadder l, int t)
    {
        long fx = CentreMm(l.FootX), fy = CentreMm(l.FootY);
        var (tx, ty) = TopMm(l);
        // the climbers climb
        for (int i = 0; i < l.Climbers.Count; i++)
        {
            var c = l.Climbers[i];
            var f = World.Fighters[c.Id];
            if (c.Progress < R.LadderClimbTicks) { c.Progress++; _rise[c.Id] = c.Progress * R.LevelMm / R.LadderClimbTicks; }
            else if (TopFree(l, c.Id)) { Arrive(l, c, f, tx, ty); l.Climbers.RemoveAt(i); i--; }
        }
        // the next in the queue come to the foot and take a rung when there is room (one man to a metre of ladder: three)
        l.Queue.RemoveAll(q => !World.Fighters[q].Active);
        for (int i = 0; i < l.Queue.Count && l.Climbers.Count < R.LadderClimbers; i++)
        {
            var f = World.Fighters[l.Queue[i]];
            if (!Free(f)) continue;
            if (DistTo(f, fx, fy) > 1200) { March(f, fx, fy, 1000); continue; }
            Ensure(f.Id); _level[f.Id] = 0; _rise[f.Id] = 0;
            l.Climbers.Add(new Climb { Id = f.Id });
            HoldFor(f.Id, HoldKind.Climb, l.Id, 0);
            l.Queue.RemoveAt(i); i--;
        }
        foreach (int q in l.Queue) { var f = World.Fighters[q]; if (Free(f)) March(f, fx, fy, 1000); }            // those who wait stand at the foot
        if (Live) { if (PushLive(l, t)) return; }
        // a defender at the top pushes it off: 3 s a try, each try a draw of 0x7C1 keyed by the ladder and the try
        else if (l.Pusher >= 0)
        {
            var p = World.Fighters[l.Pusher];
            if (!p.Active) { Release(l.Pusher); l.Pusher = -1; }
            else if (t - l.PushSince >= R.PushTicks)
            {
                int pm = AssaultMath.PushPm(R, AthleticsOf(p));
                if (DetRng.Range(World.Seed, l.PushTries, (ulong)l.Id, RngStreams.LadderPushedOff, 0, 0, 1000) < pm) { Topple(l, p); return; }
                l.PushTries++; l.PushSince = t;
            }
        }
        else
        {
            var (px, py) = (tx, ty);
            foreach (var d in World.Fighters)
            {
                if (d.Team == l.Team || !Free(d) || LevelOf(d.Id) < 1 || RiseOf(d.Id) != 0) continue;
                if (DistTo(d, px, py) > R.PushReachMm) continue;
                l.Pusher = d.Id; l.PushSince = t; HoldFor(d.Id, HoldKind.Push, l.Id, 0);
                break;
            }
        }
    }

    /// <summary>Whether the top cell is clear for a climber to step onto: nobody stands within 700 mm of its centre on that floor.</summary>
    private bool TopFree(SiegeLadder l, int climber)
    {
        var (tx, ty) = TopMm(l);
        foreach (var f in World.Fighters)
            if (f.Id != climber && f.Active && LevelOf(f.Id) >= 1 && RiseOf(f.Id) == 0 && DistTo(f, tx, ty) < 700) return false;
        return true;
    }

    private void Arrive(SiegeLadder l, Climb c, Fighter f, long tx, long ty)
    {
        Release(c.Id);
        _level[c.Id] = 1; _rise[c.Id] = 0;
        f.X = (int)(tx - OriginX); f.Y = (int)(ty - OriginY);
        if (f.Act == FAct.Stagger) { f.Act = FAct.Idle; f.ActTicks = 0; }
    }

    /// <summary>The ladder goes over: everyone on it falls the length of the way he had climbed, through the resolver, and the defender who pushed it is free.</summary>
    private void Topple(SiegeLadder l, Fighter pusher)
    {
        l.Stage = LadderStage.Down; l.Pushed++;
        Release(pusher.Id); l.Pusher = -1; if (l.Pusher2 >= 0) { Release(l.Pusher2); l.Pusher2 = -1; }
        foreach (var c in l.Climbers.ToList())
        {
            var f = World.Fighters[c.Id];
            Release(c.Id);
            _rise[c.Id] = 0; _level[c.Id] = 0;
            Fell(l, c.Id, Math.Max(1000, c.Progress * R.LadderLenMm / R.LadderClimbTicks));
            if (f.Act == FAct.Stagger && f.Active) { f.Act = FAct.Idle; f.ActTicks = 0; }
        }
        l.Climbers.Clear();
        l.Queue.Clear();
    }

    private void Fell(SiegeLadder l, int id, int heightMm) => World.FallFrom(World.Fighters[id], heightMm, ((ulong)l.Id << 24) ^ (ulong)id ^ (ulong)World.Tick << 32);

    private ulong HashLadder(ulong h, SiegeLadder l)
    {
        h = FightMath.Mix(h, l.Id | (long)l.Team << 8 | (long)l.Stage << 16); h = FightMath.Mix(h, l.FootX | (long)l.FootY << 24); h = FightMath.Mix(h, l.TopX | (long)l.TopY << 24);
        h = FightMath.Mix(h, l.StageSince | (long)l.Pusher << 40); h = FightMath.Mix(h, l.PushTries | (long)l.Pushed << 16); h = FightMath.Mix(h, l.PushSince);
        if (l.Hooked || l.Pusher2 >= 0) h = FightMath.Mix(h, (l.Hooked ? 1L : 0) | (long)(l.Pusher2 + 1) << 8);
        foreach (int c in l.Carriers) h = FightMath.Mix(h, c);
        h = FightMath.Mix(h, -1); foreach (int c in l.Queue) h = FightMath.Mix(h, c);
        h = FightMath.Mix(h, -2); foreach (var c in l.Climbers) h = FightMath.Mix(h, c.Id | (long)c.Progress << 32);
        return h;
    }
}
