using Ravensguard.Content;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Naval;

namespace Ravensguard.Sim.Battle;

/// <summary>One chariot in a fight: its car (the book's row), the two fighters who ride it (the driver and the archer or spearman) and the persons the book knows them by, and the second's start and end for the crews to be placed between.</summary>
public sealed class ChariotCar
{
    public Chariot Car = null!;
    public int Team, Driver = -1, Rider = -1;
    public EntityId DriverPerson, RiderPerson;
    public long FromX, FromY, ToX, ToY;
}

/// <summary>
/// P8-LW-3 (live-war 3.3, sieges-and-engines 4.9): chariots in a live fight. A car is driven by the <see cref="ChariotBook"/> a game second at a time (its speed, its turn, the ground, a crash) and its crew is carried between the second's two places a
/// tick at a time; the crew's own feet do not move (the car does). The AI charges the nearest unbraced foot on open firm ground and never drives into woods, a ditch or stakes (it looks ahead along the heading the car will actually take, and a car that
/// would stand on forbidden ground is held where it was). The fighter's thrust in passing and the team's hooves land on foot within reach at speed; a crash throws the crew. An archer's spread is x2 above 3 m/s (<see cref="ShotSigma"/>). Integer only.
/// </summary>
public sealed class ChariotLayer
{
    public FightWorld W { get; }
    public ChariotBook Book { get; }
    public FightTerrain Terrain { get; set; }
    public ChariotRulesDef R => Book.Rules;
    public List<ChariotCar> Cars { get; } = [];
    /// <summary>Whether the AI drives the cars (a hero's own car is driven by him and left alone).</summary>
    public bool AiDrives { get; set; } = true;

    public ChariotLayer(FightWorld w, ChariotBook book, FightTerrain? terrain = null)
    {
        W = w; Book = book; Terrain = terrain ?? w.Mounts?.Terrain ?? new FightTerrain();
        w.Chariots = this;
    }

    /// <summary>Puts a finished chariot (already in the book) on the field with its crew: the fighters are carried with the car from now on.</summary>
    public ChariotCar? Add(Chariot car, int team, int driver, int rider, EntityId driverPerson, EntityId riderPerson)
    {
        var v = Book.Rides.Vehicles.Find(car.Vehicle); if (v == null) return null;
        var c = new ChariotCar { Car = car, Team = team, Driver = driver, Rider = rider, DriverPerson = driverPerson, RiderPerson = riderPerson, FromX = v.XMm, FromY = v.YMm, ToX = v.XMm, ToY = v.YMm };
        Cars.Add(c); Cars.Sort((a, b) => a.Car.Vehicle.CompareTo(b.Car.Vehicle));
        Seat(c, 0);
        return c;
    }

    // ------------------------------------------------------------------ the tick

    /// <summary>Before the fight moves anyone: on the second, every car decides and drives; every tick the crews are carried and told to stand.</summary>
    public void Before()
    {
        bool second = W.Tick % 20 == 0;
        foreach (var c in Cars)
        {
            if (second) Drive(c);
            Seat(c, W.Tick % 20);
            Stand(c.Driver); Stand(c.Rider);
        }
    }

    /// <summary>After the fight has moved and pushed: the crews are put back in their cars; once a second at speed the thrust and the hooves land.</summary>
    public void After()
    {
        foreach (var c in Cars)
        {
            Seat(c, W.Tick % 20);
            if (W.Tick % 20 == 10 && !c.Car.Crashed && c.Car.SpeedMmps >= R.ArcherMovingMmps) Strike(c);
        }
    }

    private void Stand(int id) { if (id >= 0) { var f = W.Fighters[id]; f.Cmd.MoveX = 0; f.Cmd.MoveY = 0; } }

    private void Seat(ChariotCar c, int k)
    {
        int x = (int)(c.FromX + (c.ToX - c.FromX) * k / 20), y = (int)(c.FromY + (c.ToY - c.FromY) * k / 20);
        foreach (int id in new[] { c.Driver, c.Rider })
        {
            if (id < 0) continue;
            var f = W.Fighters[id]; if (!f.Active) continue;
            f.X = x; f.Y = y;
        }
    }

    // ------------------------------------------------------------------ driving

    private static bool Forbidden(Footing f) => f is Footing.Woods or Footing.DeepWater or Footing.Ditch or Footing.Stakes or Footing.Rubble;
    private static bool Firm(Footing f) => f is Footing.Road or Footing.Field;

    private bool Clear(int x, int y)
    {
        if (Forbidden(Terrain.At(x, y))) return false;
        if (W.Fort is { } fl && (fl.Fort.BlocksMove(x, y) || fl.ChevalDistance(x, y) <= 500)) return false;     // a board or a cheval is a wall to a car
        return true;
    }

    private Fighter? Target(ChariotCar c, int x, int y)
    {
        Fighter? best = null; long bd = long.MaxValue;
        foreach (var f in W.Fighters)
        {
            if (f.Team == c.Team || !f.Active || f.Id == c.Driver || f.Id == c.Rider) continue;
            if (W.Mounts != null && (W.Mounts.IsHorse(f.Id) || W.Mounts.Riding(f.Id) != null)) continue;           // foot only
            if (f.Act == FAct.Bracing) continue;                                                                    // an unbraced man
            if (W.Fort is { } fl && fl.Team != c.Team && fl.Fort.Formed && fl.Fort.Inside(f.X, f.Y)) continue;     // not behind a fort's boards
            if (!Firm(Terrain.At(f.X, f.Y))) continue;                                                              // open firm ground only
            long d = FightMath.Len(f.X - x, f.Y - y);
            if (d < bd) { bd = d; best = f; }
        }
        return best;
    }

    private void Drive(ChariotCar c)
    {
        var v = Book.Rides.Vehicles.Find(c.Car.Vehicle); if (v == null) return;
        c.FromX = v.XMm; c.FromY = v.YMm;
        if (c.Car.Crashed || !AiDrives) { c.ToX = v.XMm; c.ToY = v.YMm; return; }
        int px = (int)v.XMm, py = (int)v.YMm;
        long sec = W.Tick / 20;
        int want = 0; int heading = v.Facing;
        var t = Target(c, px, py);
        if (t != null)
        {
            int dir = FightMath.DirIndex((int)Math.Clamp(t.X - (long)px, -1_000_000, 1_000_000), (int)Math.Clamp(t.Y - (long)py, -1_000_000, 1_000_000)) * 4 & 255;
            int speedNext = Math.Min(R.TopMmps, c.Car.SpeedMmps + R.AccelMmps), lim = Book.TurnLimit(speedNext);
            foreach (int off in new[] { 0, 16, -16, 32, -32, 48, -48 })
            {
                int wantH = (dir + off) & 255;
                int turn = ((wantH - v.Facing + 384) & 255) - 128;
                int res = (v.Facing + Math.Clamp(turn, -lim, lim)) & 255;                                          // the heading the car will take
                var (cos, sin) = FrameMath.CosSin(res << 16);
                bool ok = true;
                for (int d = 2000; d <= R.AiLookMm && ok; d += 2000) ok = Clear(px + (int)((cos * d) >> 30), py + (int)((sin * d) >> 30));
                if (!ok) continue;
                heading = wantH; want = R.TopMmps; break;
            }
        }
        var (fc, fs) = FrameMath.CosSin(v.Facing << 16);
        var foot = Terrain.At(px, py);
        int slope = Terrain.SlopeAlongPm(px, py, (int)(fc >> 20), (int)(fs >> 20));
        var step = Book.Step(c.Car, want, heading, foot, slope, sec);
        if ((Forbidden(Terrain.At((int)v.XMm, (int)v.YMm)) && !Forbidden(foot)) || (W.Fort != null && !W.Fort.BlocksMove(px, py) && W.Fort.BlocksMove((int)v.XMm, (int)v.YMm))) { v.XMm = px; v.YMm = py; c.Car.SpeedMmps = 0; }     // a car never stands in the woods, a ditch or stakes
        c.ToX = v.XMm; c.ToY = v.YMm;
        if (step.Crashed) Crash(c, step);
    }

    private void Crash(ChariotCar c, ChariotStep step)
    {
        foreach (var fall in step.Falls)
        {
            int id = fall.Person == c.DriverPerson ? c.Driver : fall.Person == c.RiderPerson ? c.Rider : -1;
            if (id >= 0) W.ImpactMilli(W.Fighters[id], fall.Milli, (ulong)c.Car.Vehicle.Value << 8 | 0x7E);
        }
    }

    /// <summary>The fighter's thrust in passing (a spearman) and the team's hooves on every enemy footman within reach: one blow each a second, through the one resolver.</summary>
    private void Strike(ChariotCar c)
    {
        int cx = W.Fighters[c.Driver >= 0 ? c.Driver : c.Rider].X, cy = W.Fighters[c.Driver >= 0 ? c.Driver : c.Rider].Y;
        long reach = (long)R.AiReachMm * R.AiReachMm, sec = W.Tick / 20;
        int vc = c.Car.SpeedMmps;
        foreach (var f in W.Fighters)
        {
            if (f.Team == c.Team || !f.Active) continue;
            if (W.Mounts != null && W.Mounts.IsHorse(f.Id)) continue;
            long dx = f.X - cx, dy = f.Y - cy;
            if (dx * dx + dy * dy > reach) continue;
            long milli = c.Car.Spear ? Book.ThrustMilli(c.Car, vc) : 0;
            foreach (long m in Book.Trample(c.Car, vc, sec)) milli += m;
            if (milli > 0) W.ImpactMilli(f, milli, (ulong)c.Car.Vehicle.Value << 8 | (ulong)(sec & 0xFF));
        }
    }

    // ------------------------------------------------------------------ the archer's shot

    /// <summary>A chariot archer's spread: x2 (<c>archer_sigma_pm</c>) while the car moves faster than 3 m/s, as it stands otherwise.</summary>
    public int ShotSigma(Fighter f, int sigmaMdeg)
    {
        foreach (var c in Cars)
            if (c.Rider == f.Id && !c.Car.Crashed && c.Car.SpeedMmps > R.ArcherMovingMmps) return (int)((long)sigmaMdeg * R.ArcherSigmaPm / 1000);
        return sigmaMdeg;
    }

    // ------------------------------------------------------------------ hash and save

    public ulong Hash(ulong h)
    {
        foreach (var c in Cars) h = FightMath.Mix(h, (long)c.Car.SpeedMmps + (c.Car.Crashed ? 1L << 40 : 0) + (c.ToX << 8) + (c.ToY << 28));
        return h;
    }

    /// <summary>The book (its length, then its numbers), then each car: team, driver, rider, the two persons, the second's start and end.</summary>
    public long[] Pack()
    {
        var b = Book.Pack();
        var l = new List<long> { AiDrives ? 1 : 0, b.Length };
        l.AddRange(b);
        l.Add(Cars.Count);
        foreach (var c in Cars) l.AddRange([(long)c.Car.Vehicle.Value, c.Team, c.Driver, c.Rider, (long)c.DriverPerson.Value, (long)c.RiderPerson.Value, c.FromX, c.FromY, c.ToX, c.ToY]);
        return [.. l];
    }

    public static ChariotLayer Unpack(FightWorld w, RideBook rides, long[] d)
    {
        var book = new ChariotBook(w.Content.Db, rides, w.Seed);
        int k = 0; bool ai = d[k++] != 0; int n = (int)d[k++];
        book.Unpack(d[k..(k + n)]); k += n;
        var layer = new ChariotLayer(w, book) { AiDrives = ai };
        for (int i = 0, cnt = (int)d[k++]; i < cnt; i++)
        {
            var car = book.Find(new EntityId((ulong)d[k++]));
            var c = new ChariotCar { Team = (int)d[k++], Driver = (int)d[k++], Rider = (int)d[k++], DriverPerson = new EntityId((ulong)d[k++]), RiderPerson = new EntityId((ulong)d[k++]), FromX = d[k++], FromY = d[k++], ToX = d[k++], ToY = d[k++] };
            if (car != null) { c.Car = car; layer.Cars.Add(c); }
        }
        return layer;
    }
}
