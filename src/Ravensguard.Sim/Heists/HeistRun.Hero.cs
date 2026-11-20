using Ravensguard.Sim.Core;
using Ravensguard.Sim.Law;

namespace Ravensguard.Sim.Heists;

public sealed partial class HeistRun
{
    /// <summary>What the hero carries that the actions need (the barrier sets them from the pack): lockpicks, a prying tool, a flame to set a fire with.</summary>
    public bool HasPicks = true, HasPry = true, HasFlame = true;

    private Act Gate() => !Active ? Act.Ended : Busy ? Act.Busy : Act.Ok;

    public void SetSneak(bool on) { Sneak = on; if (on) Running = false; }
    public void SetRun(bool on) { Running = on; if (on) Sneak = false; }
    public void SetTorch(bool on) => Torch = on;
    public void SetLivery(bool on, ulong lord = 0) { Livery = on; LiveryLord = on ? lord : 0; }

    /// <summary>Waits <paramref name="seconds"/> where he stands (listening, hiding).</summary>
    public Act Wait(int seconds) { if (Gate() is var g && g != Act.Ok) return g; Advance(Math.Max(0, seconds)); return Active ? Act.Ok : Act.Ended; }

    // ------------------------------------------------------------------ moving

    /// <summary>A step (a sneaking step takes two seconds): into a shut door opens it (noise 8), a locked one stops him.</summary>
    public Act Move(int dx, int dy)
    {
        if (Gate() is var g && g != Act.Ok) return g;
        var s = Here; var m = s.Map!; int nx = X + dx, ny = Y + dy;
        if ((dx == 0 && dy == 0) || Math.Abs(dx) > 1 || Math.Abs(dy) > 1) return Act.Nothing;
        if (!m.In(nx, ny) || !m.Walkable(nx, ny)) return Act.Blocked;
        if (dx != 0 && dy != 0 && (m.Tile(X + dx, Y) is HTile.Wall or HTile.Rock || m.Tile(X, Y + dy) is HTile.Wall or HTile.Rock)) return Act.Blocked;
        if (m.DoorAt(nx, ny) is { } d)
        {
            byte st = s.Door[d.Index];
            if (d.Grate && st < 2) return Act.Blocked;
            if (st == 0 && d.Quality >= 0) return Act.Locked;
            if (st < 2) { s.Door[d.Index] = 2; Make(s, nx, ny, R.S.NoiseDoorM, NoiseKind.Door); Add(HeistEventKind.DoorOpened, (ulong)d.Index); }
        }
        X = nx; Y = ny; Moved = true;
        Make(s, X, Y, StepMetres, Sneak ? NoiseKind.Sneak : Running ? NoiseKind.Run : NoiseKind.Walk);
        Advance(Sneak ? 2 : 1);
        return Active ? Act.Ok : Act.Ended;
    }

    /// <summary>Walks the hero along a path of steps (each a direction 0 to 7 in <see cref="HeistMap.Dirs"/>), stopping at the first that does not go.</summary>
    public Act Walk(IEnumerable<int> dirs)
    {
        Act r = Act.Ok;
        foreach (int d in dirs) { var (dx, dy) = HeistMap.Dirs[d & 7]; r = Move(dx, dy); if (r != Act.Ok) break; }
        return r;
    }

    /// <summary>Walks to a cell by the guards' own field (the shortest way over floor and doors), stopping when something stops him; the helper of the UI's click-to-walk and the tests.</summary>
    public Act GoTo(int tx, int ty)
    {
        var s = Here; var m = s.Map!;
        var f = m.Field(tx, ty, culvert: true);
        for (int guard = 0; guard < 400 && (X != tx || Y != ty); guard++)
        {
            var (nx, ny) = m.Down(f, X, Y);
            if (nx == X && ny == Y) return Act.Blocked;
            var r = Move(nx - X, ny - Y);
            if (r != Act.Ok) return r;
        }
        return X == tx && Y == ty ? Act.Ok : Act.Blocked;
    }

    // ------------------------------------------------------------------ locks and keys

    private HDoor? NearDoor(int idx, out HSite s)
    {
        s = Here; var m = s.Map!;
        if (idx < 0 || idx >= m.Doors.Count) return null;
        var d = m.Doors[idx];
        return Cheb(X, Y, d.X, d.Y) <= 1 ? d : null;
    }

    /// <summary>The key of a door, if the hero has one for the lock as it is now.</summary>
    public bool HasKey(HSite s, int door) => Keys.Contains(new KeyRef(s.Id, door, s.LockGen));

    public Act UseKey(int idx)
    {
        if (Gate() is var g && g != Act.Ok) return g;
        var d = NearDoor(idx, out var s); if (d == null) return Act.TooFar;
        if (s.Door[idx] >= 1 && !(s.Door[idx] == 1)) return Act.Nothing;
        if (!HasKey(s, idx)) return Keys.Any(k => k.Site == s.Id && k.Door == idx) ? Act.StaleKey : Act.NoKey;
        s.Door[idx] = 2; Make(s, d.X, d.Y, R.S.NoiseDoorM, NoiseKind.Door); Add(HeistEventKind.DoorOpened, (ulong)idx);
        Advance(1);
        return Active ? Act.Ok : Act.Ended;
    }

    /// <summary>Picks the lock of a door beside him: the time of its quality, a noise of 4 m every five seconds, a roll at the end (stream 0x455) and, failed, another go.</summary>
    public Act Pick(int idx, bool finish = true)
    {
        if (Gate() is var g && g != Act.Ok) return g;
        if (!HasPicks) return Act.NoTool;
        var d = NearDoor(idx, out var s); if (d == null) return Act.TooFar;
        if (d.Quality < 0 || s.Door[idx] != 0) return Act.Nothing;
        StartBusy(1, idx, Locks.PickTimeS(R, d.Quality)); BusyAttempt = 0;
        Make(s, X, Y, R.S.NoisePickM, NoiseKind.Pick);
        if (finish) Finish();
        return Active ? Act.Ok : Act.Ended;
    }

    /// <summary>Breaks a door open (the time of its lock to break, noise 25 m at both ends): for a lock the outer door's quality, for a plain door six seconds.</summary>
    public Act Break(int idx, bool finish = true)
    {
        if (Gate() is var g && g != Act.Ok) return g;
        var d = NearDoor(idx, out var s); if (d == null) return Act.TooFar;
        if (d.Grate || s.Door[idx] >= 2) return Act.Nothing;
        StartBusy(2, idx, d.Quality >= 0 ? Locks.BreakTimeS(R, d.Quality) : 6);
        Make(s, X, Y, R.S.NoiseBreakM, NoiseKind.Break);
        if (finish) Finish();
        return Active ? Act.Ok : Act.Ended;
    }

    /// <summary>Pries a grate loose (30 s with a tool, noise 8 m).</summary>
    public Act Pry(int idx, bool finish = true)
    {
        if (Gate() is var g && g != Act.Ok) return g;
        if (!HasPry) return Act.NoTool;
        var d = NearDoor(idx, out var s); if (d == null) return Act.TooFar;
        if (!d.Grate || s.Door[idx] >= 2) return Act.Nothing;
        StartBusy(5, idx, R.S.GrateS);
        Make(s, X, Y, R.S.NoiseGrateM, NoiseKind.Grate);
        if (finish) Finish();
        return Active ? Act.Ok : Act.Ended;
    }

    /// <summary>The outer door of the site, locked behind an alarm: picked (a Good lock) or broken like any door; the index is <see cref="OuterIndex"/>.</summary>
    public Act PickOuter(bool finish = true)
    {
        if (Gate() is var g && g != Act.Ok) return g;
        var s = Here; var m = s.Map!; int o = OuterIndex(m);
        if (Cheb(X, Y, m.Exit.X, m.Exit.Y) > 1) return Act.TooFar;
        if (s.Door[o] != 0) return Act.Nothing;
        if (!HasPicks) return Act.NoTool;
        StartBusy(1, o, Locks.PickTimeS(R, 2)); BusyAttempt = 0;
        Make(s, X, Y, R.S.NoisePickM, NoiseKind.Pick);
        if (finish) Finish();
        return Active ? Act.Ok : Act.Ended;
    }

    // ------------------------------------------------------------------ the guards

    private HGuard? NearGuard(int gi, int reach, out HSite s)
    {
        s = Here;
        if (gi < 0 || gi >= s.Guards.Count) return null;
        var g = s.Guards[gi];
        return Cheb(X, Y, g.X, g.Y) <= reach ? g : null;
    }

    /// <summary>Goes through the pockets of a sleeping or downed guard (6 s, noise 2 m): the keys on his belt are the hero's; a sleeper wakes with 10% a search (stream 0x6B1).</summary>
    public Act Search(int gi, bool finish = true)
    {
        if (Gate() is var gate && gate != Act.Ok) return gate;
        var g = NearGuard(gi, 1, out var s); if (g == null) return Act.TooFar;
        if (g.State is not (GState.Asleep or GState.Down)) return Act.Refused;
        StartBusy(3, gi, R.S.SearchS);
        Make(s, X, Y, R.S.NoiseSearchM, NoiseKind.Search);
        if (finish) Finish();
        return Active ? Act.Ok : Act.Ended;
    }

    /// <summary>A quiet takedown (subdue) of a guard who has not seen the hero: he goes down for half an hour; one who is on his feet and aware is a fight.</summary>
    public Act Subdue(int gi)
    {
        if (Gate() is var gate && gate != Act.Ok) return gate;
        var g = NearGuard(gi, 1, out var s); if (g == null) return Act.TooFar;
        if (g.State == GState.Down) return Act.Nothing;
        if (g.Aware && g.State != GState.Asleep) return Act.Seen;
        g.State = GState.Down; g.Timer = 1800;
        Make(s, X, Y, R.S.NoiseSubdueM, NoiseKind.Subdue);
        Add(HeistEventKind.Subdued, g.Person);
        Advance(3);
        return Active ? Act.Ok : Act.Ended;
    }

    /// <summary>
    /// An offer of coin to a guard within three cells: at or over his price (3 x wage x (60 - honesty) / 20) it is taken with P = 60 - honesty percent (stream 0x613, key official and day) and he looks away; under it, refused,
    /// or taken by an honest man never; a refusal is a crime (bribery), an alert, and he gives chase. The coin itself moves at the barrier (the event).
    /// </summary>
    public (Act Act, BribeResult Result) Bribe(int gi, long offer)
    {
        if (Gate() is var gate && gate != Act.Ok) return (gate, BribeResult.Honest);
        var g = NearGuard(gi, 3, out var s); if (g == null) return (Act.TooFar, BribeResult.Honest);
        if (g.State is GState.Asleep or GState.Down or GState.Bribed) return (Act.Refused, BribeResult.Honest);
        var res = Bribes.Offer(Seed, R.Law, g.Person, g.Honesty, offer, Sec);
        if (res == BribeResult.Taken)
        {
            g.State = GState.Bribed; g.Aware = true;
            Add(HeistEventKind.Bribed, g.Person, 0, offer);
        }
        else
        {
            Add(HeistEventKind.BribeRefused, g.Person, 0, offer);
            Spot(s, g, Witnessing.DistCm(new Ravensguard.Sim.World.Cell3(g.X, g.Y, 0), new Ravensguard.Sim.World.Cell3(X, Y, 0)));
            g.Familiarity = Math.Max(g.Familiarity, 1);                                              // he has looked him in the eye
            int at = Seen.FindIndex(x => x.Guard == g.Person); if (at >= 0) Seen[at] = (g.Person, true);
        }
        Advance(2);
        return (Active ? Act.Ok : Act.Ended, res);
    }

    // ------------------------------------------------------------------ light and diversions

    public Act Douse(int sconce, bool finish = true)
    {
        if (Gate() is var gate && gate != Act.Ok) return gate;
        var s = Here; var m = s.Map!;
        if (sconce < 0 || sconce >= m.Sconces.Count) return Act.Nothing;
        var c = m.Sconces[sconce]; if (Cheb(X, Y, c.X, c.Y) > 1) return Act.TooFar;
        if (s.SconceOut[sconce]) return Act.Nothing;
        StartBusy(4, sconce, R.S.DouseS);
        Make(s, X, Y, R.S.NoiseDouseM, NoiseKind.Douse);
        if (finish) Finish();
        return Active ? Act.Ok : Act.Ended;
    }

    /// <summary>Throws a stone to a cell within ten metres: the noise (10 m) is at the landing; the guards investigate the nearest first.</summary>
    public Act Throw(int tx, int ty)
    {
        if (Gate() is var gate && gate != Act.Ok) return gate;
        var s = Here; var m = s.Map!;
        if (!m.In(tx, ty) || Cheb(X, Y, tx, ty) > 10) return Act.TooFar;
        Make(s, tx, ty, R.S.NoiseStoneM, NoiseKind.Stone);
        Add(HeistEventKind.StoneThrown, (ulong)(uint)tx | (ulong)(uint)ty << 32);
        Advance(1);
        return Active ? Act.Ok : Act.Ended;
    }

    /// <summary>Sets a fire in a cell within three metres (the fire system's job is the game's: the event says where): every guard within 20 m who is not on the hero's trail goes to it and watches it for two minutes.</summary>
    public Act SetFire(int tx, int ty)
    {
        if (Gate() is var gate && gate != Act.Ok) return gate;
        if (!HasFlame) return Act.NoTool;
        var s = Here; var m = s.Map!;
        if (!m.In(tx, ty) || Cheb(X, Y, tx, ty) > 3 || !m.Walkable(tx, ty)) return Act.TooFar;
        s.FireX = tx; s.FireY = ty; s.FireUntil = Sec + R.S.FireWatchS;
        Add(HeistEventKind.FireSet, (ulong)(uint)tx | (ulong)(uint)ty << 32);
        foreach (var g in s.Guards)
        {
            if (!g.Awake || g.State is GState.Pursue) continue;
            long d2 = (long)(g.X - tx) * (g.X - tx) + (long)(g.Y - ty) * (g.Y - ty);
            if (d2 > (long)R.S.FireRespondM * R.S.FireRespondM) continue;
            g.State = GState.Fire; g.TargetX = tx; g.TargetY = ty;
        }
        Advance(3);
        return Active ? Act.Ok : Act.Ended;
    }

    // ------------------------------------------------------------------ prisoners, chests, ways out

    /// <summary>Frees the captive in a slot beside him (5 s, noise 2 m): he follows the hero.</summary>
    public Act Free(int slot, bool finish = true)
    {
        if (Gate() is var gate && gate != Act.Ok) return gate;
        var s = Here; var m = s.Map!;
        if (slot < 0 || slot >= m.Slots.Count || slot >= s.Slot.Length || s.Slot[slot] == 0 || s.Freed[slot]) return Act.Nothing;
        var c = m.Slots[slot]; if (Cheb(X, Y, c.X, c.Y) > 1) return Act.TooFar;
        StartBusy(7, slot, 5);
        Make(s, X, Y, R.S.NoiseSearchM, NoiseKind.Search);
        if (finish) Finish();
        return Active ? Act.Ok : Act.Ended;
    }

    /// <summary>Empties the chest beside him (8 s): the coin is the barrier's (the event), the goods and papers are taken here.</summary>
    public Act Loot(bool finish = true)
    {
        if (Gate() is var gate && gate != Act.Ok) return gate;
        var s = Here; var m = s.Map!;
        if (m.Chest.X < 0) return Act.Nothing;
        if (Cheb(X, Y, m.Chest.X, m.Chest.Y) > 1) return Act.TooFar;
        if (s.ChestDay >= 0 && Sec / 86_400 < s.ChestDay + 30) return Act.Nothing;
        StartBusy(6, 0, 8);
        Make(s, X, Y, R.S.NoiseSearchM, NoiseKind.Search);
        if (finish) Finish();
        return Active ? Act.Ok : Act.Ended;
    }

    /// <summary>Takes the drain between a gaol and its culvert: from one portal cell to the other end in the partner site (two seconds).</summary>
    public Act UsePortal()
    {
        if (Gate() is var gate && gate != Act.Ok) return gate;
        var s = Here; var m = s.Map!;
        bool a = (X, Y) == m.PortalA, b = (X, Y) == m.PortalB;
        if ((!a && !b) || Partner == 0 || !Sites.TryGetValue(Partner, out var other)) return Act.Nothing;
        var to = a ? other.Map!.PortalB : other.Map!.PortalA;
        if (to.X < 0) return Act.Nothing;
        Cur = other.Id; Partner = s.Id; X = to.X; Y = to.Y; Moved = true;
        Add(HeistEventKind.Portal, other.Id);
        Advance(2);
        return Active ? Act.Ok : Act.Ended;
    }

    /// <summary>Out by the way in (on the exit cell, the outer door not locked behind an alarm): the run is over. Seen on the way, the barrier makes bounties and a manhunt; unseen, a quiet escape found at the next round.</summary>
    public Act Leave()
    {
        if (Gate() is var gate && gate != Act.Ok) return gate;
        var s = Here; var m = s.Map!;
        if ((X, Y) != m.Exit) return Act.TooFar;
        int o = OuterIndex(m); if (o < s.Door.Length && s.Door[o] == 0) return Act.Locked;
        Outcome = RunOutcome.Left; Add(HeistEventKind.Left, (ulong)Seen.Count);
        return Act.Ok;
    }

    // ------------------------------------------------------------------ the busy work

    private void StartBusy(int kind, int target, int seconds) { BusyKind = kind; BusyTarget = target; BusyLeft = Math.Max(1, seconds); BusyTotal = BusyLeft; }

    private void Work()
    {
        BusyLeft--;
        var s = Here; var m = s.Map!; int done = BusyTotal - BusyLeft;
        switch (BusyKind)
        {
            case 1:
                if (done % R.S.LookEveryS == 0 && BusyLeft > 0) Make(s, X, Y, R.S.NoisePickM, NoiseKind.Pick);
                if (BusyLeft == 0) FinishPick(s, m);
                break;
            case 2:
                if (BusyLeft == 0)
                {
                    s.Door[BusyTarget] = 3; var d = m.Doors[BusyTarget];
                    Make(s, d.X, d.Y, R.S.NoiseBreakM, NoiseKind.Break); Add(HeistEventKind.DoorBroken, (ulong)BusyTarget); BusyKind = 0;
                }
                break;
            case 3:
                if (BusyLeft == 0) FinishSearch(s);
                break;
            case 4:
                if (BusyLeft == 0) { s.SconceOut[BusyTarget] = true; Add(HeistEventKind.Doused, (ulong)BusyTarget); BusyKind = 0; }
                break;
            case 5:
                if (done % R.S.LookEveryS == 0 && BusyLeft > 0) Make(s, X, Y, R.S.NoiseGrateM, NoiseKind.Grate);
                if (BusyLeft == 0) { s.Door[BusyTarget] = 2; Add(HeistEventKind.DoorOpened, (ulong)BusyTarget); BusyKind = 0; }
                break;
            case 6:
                if (BusyLeft == 0)
                {
                    s.ChestDay = (int)(Sec / 86_400);
                    foreach (var e in s.Extras) Taken.Add(e);
                    Add(HeistEventKind.Looted, s.Id, (ulong)s.Extras.Count, s.Settlement);
                    s.Extras = []; BusyKind = 0;
                }
                break;
            case 7:
                if (BusyLeft == 0)
                {
                    s.Freed[BusyTarget] = true; ulong p = s.Slot[BusyTarget];
                    if (!Followers.Contains(p)) Followers.Add(p);
                    Add(HeistEventKind.CaptiveFreed, p, s.Id); BusyKind = 0;
                }
                break;
        }
    }

    private void FinishPick(HSite s, HeistMap m)
    {
        int idx = BusyTarget; bool outer = idx == OuterIndex(m);
        int q = outer ? 2 : m.Doors[idx].Quality;
        int pm = Locks.PickPm(R, Lockpicking, q);
        ulong door = s.Id ^ (ulong)idx;
        if (Locks.Roll(Seed, Sec, EntityId.None, door, BusyAttempt++, pm))
        {
            s.Door[idx] = outer ? (byte)2 : (byte)1; BusyKind = 0;
            Add(HeistEventKind.DoorPicked, (ulong)idx);
        }
        else { StartBusy(1, idx, Math.Max(10, Locks.PickTimeS(R, q) / 2)); }                          // slipped: another go, half the time
    }

    private void FinishSearch(HSite s)
    {
        var g = s.Guards[BusyTarget]; BusyKind = 0;
        Make(s, X, Y, R.S.NoiseSearchM, NoiseKind.Search);
        foreach (int door in g.Keys) { var k = new KeyRef(s.Id, door, s.LockGen); if (!Keys.Contains(k)) Keys.Add(k); Add(HeistEventKind.KeyTaken, s.Id, (ulong)door); }
        if (g.Keys.Count > 0) { g.KeysTaken = true; g.Keys = []; }
        int n = g.Searches++;
        if (g.State == GState.Asleep && DetRng.Range(Seed, Sec, g.Person, RngStreams.SleepersWaking, (uint)n, 0, 100) < R.S.SearchWakePct)
        {
            g.State = GState.Alert; g.Aware = true; g.Timer = 2;                                       // he wakes with the hero at his belt: he sees at once at the next look
            Look(s, g);
        }
    }
}
