using Ravensguard.Sim.Core;
using Ravensguard.Sim.Law;

namespace Ravensguard.Sim.Heists;

public sealed partial class HeistRun
{
    /// <summary>The index of the site's outer door in <see cref="HSite.Door"/>: the way in from the street, locked by the guards when the alarm goes up (a Good lock).</summary>
    public static int OuterIndex(HeistMap m) => m.Doors.Count;

    private void StepGuards(HSite s)
    {
        var m = s.Map!;
        for (int i = 0; i < s.Guards.Count; i++)
        {
            var g = s.Guards[i];
            if (g.State == GState.Down) { if (--g.Timer <= 0) { g.State = GState.Return; g.Aware = true; } continue; }
            if (g.State == GState.Bribed) continue;
            if ((Sec + g.Slot) % R.S.LookEveryS == 0)
            {
                Listen(s, g);
                if (g.Awake) Look(s, g);
            }
            if (!g.Awake) continue;
            Walk(s, m, g);
        }
    }

    private int Dwell(HeistMap m, ulong siteId) => GuardRoster.DwellS(m, GuardRoster.PeriodS(R, Seed, siteId, Sec / 86_400));

    private void StepToward(HSite s, HGuard g, int tx, int ty)
    {
        if (g.X == tx && g.Y == ty) return;
        var f = Field(s, tx, ty);
        var (nx, ny) = s.Map!.Down(f, g.X, g.Y);
        g.X = nx; g.Y = ny;
    }

    private void Walk(HSite s, HeistMap m, HGuard g)
    {
        switch (g.State)
        {
            case GState.Post: break;
            case GState.Dwell:
                if (--g.Timer <= 0 && m.Waypoints.Count > 1) { g.Route = (g.Route + 1) % m.Waypoints.Count; g.State = GState.Patrol; }
                break;
            case GState.Patrol:
            {
                var w = m.Waypoints[g.Route % m.Waypoints.Count];
                if (g.X == w.X && g.Y == w.Y) { g.State = GState.Dwell; g.Timer = Dwell(m, s.Id); }
                else StepToward(s, g, w.X, w.Y);
                break;
            }
            case GState.Investigate:
                if (g.X == g.TargetX && g.Y == g.TargetY) { g.State = GState.Looking; g.Timer = R.S.InvestigateS; }
                else StepToward(s, g, g.TargetX, g.TargetY);
                break;
            case GState.Fire:
                if (g.X == g.TargetX && g.Y == g.TargetY) { g.State = GState.Alert; g.Timer = (int)Math.Max(1, s.FireUntil - Sec); }          // at the fire: he stands watching it until it is done
                else StepToward(s, g, g.TargetX, g.TargetY);
                break;
            case GState.Looking:
            case GState.Alert:
                if (--g.Timer <= 0) g.State = GState.Return;
                break;
            case GState.Return:
            {
                bool post = m.Posts.Contains((g.HomeX, g.HomeY)) || m.Waypoints.Count == 0;
                var home = post ? (g.HomeX, g.HomeY) : m.Waypoints[g.Route % m.Waypoints.Count];
                if (g.X == home.Item1 && g.Y == home.Item2) { g.State = post ? GState.Post : GState.Dwell; g.Timer = 0; g.Aware = false; }
                else StepToward(s, g, home.Item1, home.Item2);
                break;
            }
            case GState.Pursue:
                if (Cur != s.Id || !Active) { g.State = GState.Return; break; }
                if (Cheb(g.X, g.Y, X, Y) <= 1) { Caught(g); break; }
                StepToward(s, g, X, Y);
                if (Cheb(g.X, g.Y, X, Y) <= 1) Caught(g);
                break;
        }
    }

    private void Caught(HGuard g)
    {
        if (!Active) return;
        Outcome = RunOutcome.Caught; BusyLeft = 0; BusyKind = 0;
        Add(HeistEventKind.Caught, g.Person);
    }

    // ------------------------------------------------------------------ listening

    private void Listen(HSite s, HGuard g)
    {
        long last = g.Listened;
        foreach (var n in s.Noises)
        {
            if (n[5] <= last) continue;
            g.Listened = Math.Max(g.Listened, n[5]);
            int nx = (int)n[1], ny = (int)n[2];
            int walls = WallsBetween(s, g.X, g.Y, nx, ny);
            int dist = Ravensguard.Sim.Law.Witnessing.DistCm(new Ravensguard.Sim.World.Cell3(g.X, g.Y, 0), new Ravensguard.Sim.World.Cell3(nx, ny, 0));
            if (!Noise.Heard(R.S, (int)n[3], Weather, walls, g.State == GState.Asleep, dist)) continue;
            Hear(s, g, nx, ny, n[4] != 0);
        }
    }

    private void Hear(HSite s, HGuard g, int nx, int ny, bool shout)
    {
        if (g.State is GState.Pursue or GState.Bribed or GState.Down) return;
        if (g.State == GState.Asleep) g.Aware = false;
        if (g.State is GState.Investigate or GState.Looking)
        {
            long cur = (long)(g.TargetX - g.X) * (g.TargetX - g.X) + (long)(g.TargetY - g.Y) * (g.TargetY - g.Y);
            long now = (long)(nx - g.X) * (nx - g.X) + (long)(ny - g.Y) * (ny - g.Y);
            if (now >= cur && g.State == GState.Investigate) return;                       // the nearest noise first
        }
        g.State = GState.Investigate; g.TargetX = nx; g.TargetY = ny; g.Timer = 0;
        _ = shout; _ = s;
    }

    // ------------------------------------------------------------------ looking

    private void Look(HSite s, HGuard g)
    {
        var m = s.Map!;
        if (Cur == s.Id && Active)
        {
            int range = SightCmFor(s);
            int d = Witnessing.DistCm(new Ravensguard.Sim.World.Cell3(g.X, g.Y, 0), new Ravensguard.Sim.World.Cell3(X, Y, 0));
            if (d <= range && Sees(s, g.X, g.Y, X, Y))
            {
                var ch = Disguise.Check(Seed, R.S, g.Person, Sec / 3600, d, Livery, g.Familiarity >= 1, Running, Carrying, InCellBlock(s));
                if (ch != Challenge.Passes) { Spot(s, g, d); return; }
            }
        }
        if (s.Alarm) return;
        // something out of place: an open cell, a broken door, a guard down, a keeper robbed of his keys
        int litCm = R.Law.SightIndoorLitM * 100, darkCm = R.Law.SightIndoorDarkM * 100;
        foreach (var d in m.Doors)
        {
            byte st = s.Door.Length > d.Index ? s.Door[d.Index] : (byte)0;
            if (!((d.Cell && st >= 2) || st == 3)) continue;
            if (Notice(s, g, d.X, d.Y, Lit(s, d.X, d.Y) ? litCm : darkCm)) { Raise(s, g, d.X, d.Y); return; }
        }
        foreach (var o in s.Guards)
        {
            if (o == g || !(o.State == GState.Down || (o.KeysTaken && o.Keeper))) continue;
            if (Notice(s, g, o.X, o.Y, Lit(s, o.X, o.Y) ? litCm : darkCm)) { Raise(s, g, o.X, o.Y); return; }
        }
        foreach (var (slot, k) in s.Slot.Select((p, k) => (p, k)))
            if (k < s.Freed.Length && s.Freed[k] && slot != 0 && Notice(s, g, m.Slots[k].X, m.Slots[k].Y, litCm)) { Raise(s, g, m.Slots[k].X, m.Slots[k].Y); return; }
    }

    private bool Notice(HSite s, HGuard g, int x, int y, int rangeCm) =>
        Witnessing.DistCm(new Ravensguard.Sim.World.Cell3(g.X, g.Y, 0), new Ravensguard.Sim.World.Cell3(x, y, 0)) <= rangeCm && Sees(s, g.X, g.Y, x, y);

    /// <summary>The guard sees the hero as no one who belongs: he shouts, the others who hear come, the outer door is locked, more torches are lit, and he gives chase (heists 4.2).</summary>
    private void Spot(HSite s, HGuard g, int distCm)
    {
        bool knew = g.Familiarity >= 1 || (Lit(s, X, Y) && distCm <= R.Law.RecogniseM * 100);
        int at = Seen.FindIndex(x => x.Guard == g.Person);
        if (at < 0) { Seen.Add((g.Person, knew)); Add(HeistEventKind.Spotted, g.Person, 0, knew ? 1 : 0); }
        else if (knew && !Seen[at].Knew) Seen[at] = (g.Person, true);
        g.Aware = true; g.State = GState.Pursue;
        RaiseAlarm(s, g);
    }

    private void Raise(HSite s, HGuard g, int cx, int cy)
    {
        RaiseAlarm(s, g);
        g.State = GState.Investigate; g.TargetX = cx; g.TargetY = cy; g.Timer = 0;
    }

    private void RaiseAlarm(HSite s, HGuard g)
    {
        if (!s.Alarm)
        {
            s.Alarm = true;
            int o = OuterIndex(s.Map!); if (o < s.Door.Length) s.Door[o] = 0;                       // the outer door is locked behind the alarm
            Add(HeistEventKind.Alarm, g.Person, s.Id);
        }
        Make(s, g.X, g.Y, R.S.NoiseShoutM, NoiseKind.Shout, true);
    }
}
