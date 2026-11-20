using Ravensguard.Sim.Core;
using Ravensguard.Sim.Law;

namespace Ravensguard.Sim.Heists;

/// <summary>One guard as the screen may show him: where, what he is doing, and whether the hero can see him now (lit and in the line of sight, or within two metres).</summary>
public sealed record HeistGuardView(int Index, int X, int Y, GState State, bool Visible, bool Keeper);

/// <summary>
/// What the heist screen reads (heists-and-jailbreaks 6), built at a barrier from the run: the map as glyphs with the state of its doors, the light of each cell, the hero, the guards he can see, the eye (Seen or Unseen before he acts), the ear (Silent,
/// Quiet or Loud, and the nearest listener's state), the clock, the busy action and what he carries. No game state lives in the screen.
/// </summary>
public sealed class HeistView
{
    public string Template { get; init; } = "";
    public HeistKind Kind { get; init; }
    public int Settlement { get; init; }
    public int W { get; init; }
    public int H { get; init; }
    /// <summary>One glyph per cell (row-major): # wall, space rock, . floor, ~ culvert, + shut door, / open door, 1-5 locked door, c cell bars, o open cell, g grate, G open grate, T chest, x the way out, u and D the drain, s and S a sconce (out, lit), h hiding spot, b bed, P a prisoner, f a freed cell.</summary>
    public char[] Glyphs { get; init; } = [];
    public byte[] Light { get; init; } = [];
    public int HeroX { get; init; }
    public int HeroY { get; init; }
    public int Sneak { get; init; }
    public bool Torch { get; init; }
    public bool Livery { get; init; }
    public ulong LiveryLord { get; init; }
    public bool Alarm { get; init; }
    /// <summary>The eye: how many guards would see him now (0 is Unseen); and whether the livery would carry him past those who do.</summary>
    public int Eye { get; init; }
    public bool Disguised { get; init; }
    /// <summary>The ear: 0 Silent, 1 Quiet, 2 Loud; the nearest guard's state and his distance in metres, -1 with none.</summary>
    public int Ear { get; init; }
    public GState Nearest { get; init; }
    public int NearestM { get; init; } = -1;
    public long Sec { get; init; }
    public int Busy { get; init; }
    public int BusyKind { get; init; }
    public int BusyTotal { get; init; }
    public int Keys { get; init; }
    public int Followers { get; init; }
    public RunOutcome Outcome { get; init; }
    public int SeenBy { get; init; }
    public long Stolen { get; init; }
    public bool Partner { get; init; }
    public IReadOnlyList<HeistGuardView> Guards { get; init; } = [];
    public IReadOnlyList<(int X, int Y)> Heard { get; init; } = [];

    public static HeistView? Of(HeistRun? run)
    {
        if (run == null || !run.Sites.TryGetValue(run.Cur, out var s) || s.Map == null) return null;
        var m = s.Map; var glyphs = new char[m.W * m.H]; var light = new byte[m.W * m.H];
        for (int y = 0; y < m.H; y++)
            for (int x = 0; x < m.W; x++)
            {
                int i = y * m.W + x; var t = m.Tile(x, y);
                char c = t switch { HTile.Wall => '#', HTile.Rock => ' ', HTile.Culvert => '~', HTile.Chest => 'T', HTile.Exit => 'x', HTile.PortalA => 'u', HTile.PortalB => 'D', _ => '.' };
                if (m.DoorAt(x, y) is { } d)
                {
                    byte st = s.Door.Length > d.Index ? s.Door[d.Index] : (byte)0;
                    c = d.Grate ? (st >= 2 ? 'G' : 'g') : d.Cell ? (st >= 2 ? 'o' : 'c') : st >= 2 ? '/' : st == 0 && d.Quality >= 0 ? (char)('1' + Math.Clamp(d.Quality, 0, 4)) : '+';
                }
                glyphs[i] = c; light[i] = (byte)run.LightAtOf(s, x, y);
            }
        for (int i = 0; i < m.Sconces.Count; i++) { var c = m.Sconces[i]; glyphs[c.Y * m.W + c.X] = i < s.SconceOut.Length && s.SconceOut[i] ? 's' : run.SconceHour(s) ? 'S' : 's'; }
        foreach (var h in m.Hides) glyphs[h.Y * m.W + h.X] = 'h';
        foreach (var b in m.Beds) glyphs[b.Y * m.W + b.X] = 'b';
        for (int k = 0; k < m.Slots.Count; k++) glyphs[m.Slots[k].Y * m.W + m.Slots[k].X] = k < s.Slot.Length && s.Slot[k] != 0 ? (s.Freed[k] ? 'f' : 'P') : '.';
        var guards = new List<HeistGuardView>();
        for (int i = 0; i < s.Guards.Count; i++)
        {
            var g = s.Guards[i];
            bool vis = HeistRun.Cheb(g.X, g.Y, run.X, run.Y) <= 2 || (run.Lit(s, g.X, g.Y) && run.Sees(s, run.X, run.Y, g.X, g.Y) && HeistRun.Cheb(g.X, g.Y, run.X, run.Y) <= 14);
            guards.Add(new HeistGuardView(i, g.X, g.Y, g.State, vis, g.Keeper));
        }
        var near = run.NearestGuard(); var (eye, disguised) = run.Eye();
        return new HeistView
        {
            Template = s.Template, Kind = s.Kind, Settlement = s.Settlement, W = m.W, H = m.H, Glyphs = glyphs, Light = light, HeroX = run.X, HeroY = run.Y, Sneak = run.Sneak ? 1 : run.Running ? 2 : 0, Torch = run.Torch,
            Livery = run.Livery, LiveryLord = run.LiveryLord, Alarm = s.Alarm, Eye = eye, Disguised = disguised, Ear = Noise.Loudness(run.StepMetres), Nearest = near?.State ?? GState.Patrol, NearestM = near?.DistM ?? -1,
            Sec = run.Sec, Busy = run.BusyLeft, BusyKind = run.BusyKind, BusyTotal = run.BusyTotal, Keys = run.Keys.Count(k => k.Site == s.Id && k.Generation == s.LockGen), Followers = run.Followers.Count, Outcome = run.Outcome,
            SeenBy = run.Seen.Count, Stolen = run.Stolen, Partner = run.Partner != 0, Guards = guards,
        };
    }
}

public sealed partial class HeistRun
{
    /// <summary>The light (0 to 255) at a cell of any site of the run.</summary>
    public int LightAtOf(HSite s, int x, int y) { var c = LightOf(s); return c.Frame!.Total(x, y, 0); }

    /// <summary>
    /// The HUD's eye (plan 3.10): how many guards would see the hero now (awake, in range and in line of sight by the law's seeing rule) and whether his livery would carry him past every one of them. Nothing is drawn or rolled: it is the same
    /// geometry the guards use at their next look (the borderline draw of the disguise is shown as a challenge).
    /// </summary>
    public (int Count, bool Disguised) Eye()
    {
        var s = Here; int n = 0, passes = 0; int range = SightCmFor(s);
        foreach (var g in s.Guards)
        {
            if (!g.Awake) continue;
            int d = Witnessing.DistCm(new Ravensguard.Sim.World.Cell3(g.X, g.Y, 0), new Ravensguard.Sim.World.Cell3(X, Y, 0));
            if (d > range || !Sees(s, g.X, g.Y, X, Y)) continue;
            n++;
            if (Disguise.Check(Seed, R.S, g.Person, Sec / 3600, d, Livery, g.Familiarity >= 1, Running, Carrying, InCellBlock(s)) == Challenge.Passes) passes++;
        }
        return (n - passes, Livery && n > 0 && passes == n);
    }
}
