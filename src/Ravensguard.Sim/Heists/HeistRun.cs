using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Heists;

/// <summary>What an action of the hero came to.</summary>
public enum Act : byte { Ok, Blocked, Locked, NoKey, StaleKey, Busy, TooFar, Nothing, Refused, Seen, Ended, NoTool }

/// <summary>
/// A heist as it is played (heists-and-jailbreaks 4): the hero in a site (or two joined by the drain), one second a step. The hero's actions take their time (a pick 20 to 240 s, a search 6 s) and make noise; each guard looks and listens every
/// five seconds on his own beat, walks his loop or stands his post, sleeps or wakes; what the run needs of the rest of the world (coin, crimes, the justice book) it leaves as events for the barrier. Integer maths only; the only draws are
/// the registered streams (0x6B0 to 0x6B3, 0x455, 0x613); the state is plain data so the save holds a heist half done.
/// </summary>
public sealed partial class HeistRun
{
    public HeistRules R { get; }
    public ulong Seed { get; }
    public SortedDictionary<ulong, HSite> Sites { get; }
    public List<KeyRef> Keys { get; }
    public ulong Cur, Partner;
    public int X, Y;
    public long Sec;
    public int Weather;
    public bool Sneak, Torch, Running, Livery, Carrying;
    public ulong LiveryLord;
    public int Stealth = 30, Lockpicking = 30;
    public RunOutcome Outcome;
    /// <summary>The busy action: 0 none, 1 pick, 2 break, 3 search, 4 douse, 5 pry, 6 loot, 7 free, 8 move (a sneaking step); its target, seconds left, seconds in all, attempt count.</summary>
    public int BusyKind, BusyTarget, BusyLeft, BusyTotal, BusyAttempt;
    public List<HeistEvent> Events { get; } = [];
    /// <summary>The guards who saw the hero as an intruder: person and whether he knew him.</summary>
    public List<(ulong Guard, bool Knew)> Seen { get; } = [];
    public List<ulong> Followers { get; } = [];
    /// <summary>What was taken from the chest beyond its purse.</summary>
    public List<string> Taken { get; } = [];
    public long StartSec;
    public bool Moved;
    /// <summary>The coin this run has carried out of chests so far (mites): the burglary's value when the run ends.</summary>
    public long Stolen;

    public HeistRun(HeistRules rules, ulong seed, SortedDictionary<ulong, HSite> sites, List<KeyRef> keys, ulong site, long sec, int weather = 0)
    {
        R = rules; Seed = seed; Sites = sites; Keys = keys; Cur = site; Sec = StartSec = sec; Weather = weather;
        var s = Sites[site]; (X, Y) = s.Map!.Exit;
        Events.Add(new HeistEvent(HeistEventKind.Entered, sec, site, 0, 0));
    }

    public HSite Here => Sites[Cur];
    public HeistMap Map => Sites[Cur].Map!;
    public int HourOfDay => (int)(Sec / 3600 % 24);
    public bool Busy => BusyLeft > 0;
    public bool Active => Outcome == RunOutcome.Active;
    public bool SeenByAny => Seen.Count > 0;

    private void Add(HeistEventKind k, ulong a = 0, ulong b = 0, long v = 0) => Events.Add(new HeistEvent(k, Sec, a, b, v));

    /// <summary>The events since the last call, for the barrier to turn into coin, crimes and entries.</summary>
    public List<HeistEvent> TakeEvents() { var l = new List<HeistEvent>(Events); Events.Clear(); return l; }

    public static int Cheb(int ax, int ay, int bx, int by) => Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by));

    // ------------------------------------------------------------------ light and sight

    private sealed class LightCtx { public LightGrid Grid = null!; public LightFramePair Pair = null!; public LightFrame? Frame; public long Sig = long.MinValue; }
    private readonly Dictionary<ulong, LightCtx> _light = [];
    private readonly Dictionary<(ulong, int, int), int[]> _fields = [];

    /// <summary>Whether the sconces burn: from 18:00 to 06:00, and always once the alarm has gone up, unless a sconce was doused.</summary>
    public bool SconceHour(HSite s) => s.Alarm || HourOfDay >= R.S.SconceFromHour || HourOfDay < R.S.SconceToHour;

    private LightCtx LightOf(HSite s)
    {
        var m = s.Map!;
        if (!_light.TryGetValue(s.Id, out var c))
        {
            var g = new LightGrid(m.W, m.H, 0, 0);
            for (int y = 0; y < m.H; y++) for (int x = 0; x < m.W; x++) g.SetIndoor(x, y, 0, true);
            _light[s.Id] = c = new LightCtx { Grid = g, Pair = new LightFramePair(g) };
        }
        long sig = 17; bool on = SconceHour(s);
        unchecked
        {
            foreach (var b in s.Door) sig = sig * 31 + b;
            for (int i = 0; i < s.SconceOut.Length; i++) sig = sig * 31 + (s.SconceOut[i] ? 1 : 2);
            sig = sig * 31 + (on ? 1 : 0);
            if (Torch && Cur == s.Id) sig = sig * 31 + X * 1000 + Y + 7;
        }
        if (sig == c.Sig && c.Frame != null) return c;
        c.Sig = sig;
        for (int y = 0; y < m.H; y++)
            for (int x = 0; x < m.W; x++)
            {
                var t = m.Tile(x, y);
                bool opaque = t is HTile.Wall or HTile.Rock;
                if (t == HTile.Door && m.DoorAt(x, y) is { } d && !d.Grate && s.Door.Length > d.Index && s.Door[d.Index] < 2) opaque = true;
                c.Grid.SetOpaque(x, y, 0, opaque);
            }
        var lights = new List<LightSource>();
        if (on) for (int i = 0; i < m.Sconces.Count; i++) if (i >= s.SconceOut.Length || !s.SconceOut[i]) lights.Add(new LightSource(m.Sconces[i].X, m.Sconces[i].Y, 0, 6, 200));
        c.Grid.SetStaticLights(lights);
        var dyn = new List<LightSource>();
        if (Torch && Cur == s.Id) dyn.Add(new LightSource(X, Y, 0, LightModel.TorchRadius, LightModel.TorchIntensity));
        c.Frame = c.Pair.Next(new Ambient(0, 0, DayPhase.Night), dyn);
        return c;
    }

    /// <summary>The light (0 to 255) at a cell of the current site.</summary>
    public int LightAt(int x, int y) { var c = LightOf(Here); return c.Frame!.Total(x, y, 0); }

    public bool Lit(HSite s, int x, int y) { var c = LightOf(s); return c.Frame!.Point(x, y, 0) >= R.Law.LitLevel; }

    public IWitnessScene SceneOf(HSite s) { var c = LightOf(s); return new GridScene(c.Grid, c.Frame, new Ambient(0, 0, DayPhase.Night), false, R.Law.LitLevel); }

    /// <summary>The opaque cells strictly between two cells on the straight line: walls, rock and closed doors count; the bars of a cell and a grate do not.</summary>
    public int WallsBetween(HSite s, int x0, int y0, int x1, int y1)
    {
        var g = LightOf(s).Grid; int n = 0;
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (x0 != x1 || y0 != y1)
        {
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
            if ((x0 != x1 || y0 != y1) && g.IsOpaque(x0, y0, 0)) n++;
        }
        return n;
    }

    /// <summary>Whether a guard of the site has a clear line to a cell.</summary>
    public bool Sees(HSite s, int x0, int y0, int x1, int y1) => SceneOf(s).Los(new Cell3(x0, y0, 0), new Cell3(x1, y1, 0));

    private int[] Field(HSite s, int tx, int ty)
    {
        if (!_fields.TryGetValue((s.Id, tx, ty), out var f)) _fields[(s.Id, tx, ty)] = f = s.Map!.Field(tx, ty);
        return f;
    }

    /// <summary>How the hero looks to a guard now: the seeing rule of the law (crime 4.1) with the indoor ranges, the hero's stealth when he sneaks, and his hiding spot (a guard must be within 2 m to find a hero who keeps still in one).</summary>
    public int SightCmFor(HSite s)
    {
        var scene = SceneOf(s);
        var actor = new WitnessActor(EntityId.None, new Cell3(X, Y, 0), Sneak, Stealth, false, false, 0, ActorLook.None);
        int r = Witnessing.SightCm(R.Law, scene, actor);
        if (Hidden(s)) r = Math.Min(r, 200);
        return r;
    }

    public bool Hidden(HSite s) { var m = s.Map!; return !Moved && m.Hides.Contains((X, Y)); }

    public bool InCellBlock(HSite s)
    {
        var m = s.Map!;
        foreach (var d in m.Doors) if (d.Cell && Cheb(X, Y, d.X, d.Y) <= 2) return true;
        foreach (var p in m.Slots) if (Cheb(X, Y, p.X, p.Y) <= 2) return true;
        return false;
    }

    // ------------------------------------------------------------------ the second

    /// <summary>One second: the hero's busy work goes on, then every guard of every site in play lives his second. A run that has ended does nothing.</summary>
    public void Step()
    {
        if (!Active) return;
        Sec++;
        if (BusyLeft > 0) Work();
        foreach (var s in Sites.Values)
            if (s.Id == Cur || s.Id == Partner) StepGuards(s);
        PruneNoises();
        Moved = false;
    }

    /// <summary>Steps <paramref name="n"/> seconds (stopping when the run ends).</summary>
    public void Advance(int n) { for (int i = 0; i < n && Active; i++) Step(); }

    /// <summary>Plays out the busy action: until it is done, or until the run ends, or until someone sees the hero (the hero stops what he is doing).</summary>
    public void Finish()
    {
        int seen = Seen.Count;
        while (Active && BusyLeft > 0)
        {
            Step();
            if (Seen.Count != seen) { BusyLeft = 0; BusyKind = 0; break; }
        }
    }

    private void Make(HSite s, int x, int y, int metres, NoiseKind kind, bool shout = false)
    {
        s.Noises.Add([Sec, x, y, metres, shout ? 1 : 0, ++s.NoiseSeq]);
    }

    private void PruneNoises() { foreach (var s in Sites.Values) s.Noises.RemoveAll(n => n[0] < Sec - R.S.LookEveryS * 2); }

    /// <summary>The hero makes a noise where he stands.</summary>
    public void HeroNoise(int metres, NoiseKind kind) => Make(Here, X, Y, metres, kind);

    /// <summary>The noise the hero makes by walking now, in metres.</summary>
    public int StepMetres => Sneak ? R.S.NoiseSneakM : Running ? R.S.NoiseRunM : R.S.NoiseWalkM;

    /// <summary>The nearest guard who could hear the hero now and what he is doing (for the HUD's ear): -1 when nobody is in the site.</summary>
    public (int Index, GState State, int DistM)? NearestGuard()
    {
        var s = Here; int best = -1; long bd = long.MaxValue;
        for (int i = 0; i < s.Guards.Count; i++)
        {
            var g = s.Guards[i]; long d = (long)(g.X - X) * (g.X - X) + (long)(g.Y - Y) * (g.Y - Y);
            if (d < bd) { bd = d; best = i; }
        }
        return best < 0 ? null : (best, s.Guards[best].State, (int)IntMath.Isqrt(bd));
    }
}
