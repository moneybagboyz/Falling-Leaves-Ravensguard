using Ravensguard.Content;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Naval;

namespace Ravensguard.Sim.Battle;

/// <summary>One cheval de frise laid on the ground: its middle (mm of the fight's plane) and the heading of its length (a byte of a full turn).</summary>
public readonly record struct Cheval(int X, int Y, byte Heading);

/// <summary>
/// P8-LW-3 (live-war 3.3): a wagon fort laid over a live fight. A chained ring's armour boards are the fight's walls (<see cref="IFightWalls"/>: a body stops at them and so does a missile, as SG-E built them) and count as braced polearms against a
/// horse (<see cref="Ahead"/> and <see cref="BracedNear"/> feed the one refusal formula); the order Sortie opens one gap of two metres for 20 seconds and then closes it when no enemy stands within 4 m of it; a host with six or more war wagons
/// forms the ring when enemy horse is 30 per cent of the enemy's strength and within 2 km; and a cheval de frise (40 kg, two men to carry it) slows a man to a third and turns a horse. The ring itself is <see cref="WagonFort"/> (Phase 7); the layer
/// steps it a game second at a time. Integer only; <see cref="Pack"/> is its saved form (the vehicles are the ride book's).
/// </summary>
public sealed class FortLayer : IFightWalls
{
    public FightWorld W { get; }
    public WagonFort Fort { get; }
    public WagonRulesDef R => Fort.Rules;
    /// <summary>The side that owns the fort: its horses and men pass the gaps; the other side's horses refuse the boards.</summary>
    public int Team { get; set; }
    /// <summary>The walls the fight had before the layer (a heist's, say); the layer adds its boards to them.</summary>
    public IFightWalls? Inner { get; private set; }
    /// <summary>The host's war wagons the AI may ring (finished vehicles of the ride book), and a driver's Riding for each.</summary>
    public List<VehicleRow> Wagons { get; } = [];
    public int DriverRiding { get; set; } = 50;
    /// <summary>Whether the side's own captain decides to form the ring (the AI); a hero's fort is formed by his order.</summary>
    public bool AiForms { get; set; } = true;
    public List<Cheval> Chevaux { get; } = [];
    /// <summary>Gaps opened by a sortie and the fight tick each stays open until.</summary>
    public List<(int Gap, int UntilTick)> Sorties { get; } = [];
    /// <summary>The tick the AI formed the ring (-1: not yet).</summary>
    public int FormedTick { get; private set; } = -1;

    public FortLayer(FightWorld w, WagonFort fort, int team)
    {
        W = w; Fort = fort; Team = team;
    }

    /// <summary>Lays the layer over the fight: its boards join the fight's walls. Refuses (false) where the fight's walls are an assault's levels (a fort is a field work).</summary>
    public bool Attach()
    {
        if (W.Walls is ILevelWalls) return false;
        if (W.Walls != null && !ReferenceEquals(W.Walls, this)) Inner = W.Walls;
        W.Walls = this; W.Fort = this;
        return true;
    }

    // ------------------------------------------------------------------ the walls

    public bool BlocksMove(int xMm, int yMm) => Fort.BlocksMove(xMm, yMm) || (Inner?.BlocksMove(xMm, yMm) ?? false);
    public bool BlocksMissile(int xMm, int yMm) => Fort.BlocksMissile(xMm, yMm) || (Inner?.BlocksMissile(xMm, yMm) ?? false);

    // ------------------------------------------------------------------ the tick

    /// <summary>Once a game second (every 20th tick): the ring is driven and chained, a sortie gap that has been open its 20 seconds closes when no enemy is within 4 m of it, and the AI decides to form.</summary>
    public void Run()
    {
        if (W.Tick % 20 != 0) return;
        if (Fort.Slots.Count > 0) Fort.Step(1);
        for (int i = Sorties.Count - 1; i >= 0; i--)
        {
            var (gap, until) = Sorties[i];
            if (W.Tick < until) continue;
            if (EnemyWithin(Fort.Chains[gap].X - Fort.OriginX, Fort.Chains[gap].Y - Fort.OriginY, R.SortieEnemyMm)) continue;     // an enemy in the gap holds it open
            Fort.Close(gap);                                                                                                   // 20 s of two men to hang the chain again; false when a hook is gone, and the gap stays open
            Sorties.RemoveAt(i);
        }
        if (AiForms && FormedTick < 0 && Fort.Slots.Count == 0) Think();
    }

    private bool EnemyWithin(long x, long y, int mm)
    {
        long m2 = (long)mm * mm;
        foreach (var f in W.Fighters)
        {
            if (f.Team == Team || !f.Active) continue;
            long dx = f.X - x, dy = f.Y - y;
            if (dx * dx + dy * dy <= m2) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ the sortie

    /// <summary>
    /// The order Sortie: the gap nearest (<paramref name="x"/>, <paramref name="y"/>) opens at once and stays open <c>sortie_open_s</c> seconds (20), then closes if no enemy is inside 4 m. One gap at a time. False when no chain is up or
    /// a sortie is already out.
    /// </summary>
    public bool Sortie(int x, int y)
    {
        if (Sorties.Count > 0) return false;
        int best = -1; long bd = long.MaxValue;
        for (int i = 0; i < Fort.Chains.Count; i++)
        {
            var c = Fort.Chains[i]; if (c.State != ChainState.Chained) continue;
            long dx = c.X - Fort.OriginX - x, dy = c.Y - Fort.OriginY - y, d = dx * dx + dy * dy;
            if (d < bd) { bd = d; best = i; }
        }
        if (best < 0 || !Fort.OpenNow(best)) return false;
        Sorties.Add((best, W.Tick + R.SortieOpenS * 20));
        return true;
    }

    // ------------------------------------------------------------------ the AI

    /// <summary>The share of the enemy's strength that sits a horse, in permille, and the distance of the nearest enemy horseman to a point.</summary>
    private (int HorsePm, long NearMm) EnemyHorse(long cx, long cy)
    {
        int total = 0, horse = 0; long near = long.MaxValue;
        foreach (var f in W.Fighters)
        {
            if (f.Team == Team || !f.Active) continue;
            if (W.Mounts != null && W.Mounts.IsHorse(f.Id)) continue;                    // the horse is not a man
            total++;
            if (W.Mounts?.Riding(f.Id) == null) continue;
            horse++;
            long d = FightMath.Len(f.X - cx, f.Y - cy); if (d < near) near = d;
        }
        return (total == 0 ? 0 : (int)((long)horse * 1000 / total), near);
    }

    /// <summary>Whether this side would form a fort now: six wagons or more, enemy horse 30 per cent of the enemy's strength, its nearest horseman within 2 km of the host.</summary>
    public bool WantsFort(out long cx, out long cy)
    {
        cx = cy = 0;
        int n = 0; long sx = 0, sy = 0;
        foreach (var f in W.Fighters) if (f.Team == Team && f.Active) { sx += f.X; sy += f.Y; n++; }
        if (n == 0 || Wagons.Count < R.AiWagons) return false;
        cx = sx / n; cy = sy / n;
        var (pm, near) = EnemyHorse(cx, cy);
        return pm >= R.AiHorsePm && near <= R.AiHorseMm;
    }

    private void Think()
    {
        if (!WantsFort(out long cx, out long cy)) return;
        if (!Fort.Form(Wagons, cx + Fort.OriginX, cy + Fort.OriginY, W.Tick / 20, _ => DriverRiding)) return;
        FormedTick = W.Tick;
        // the host's foot go to the middle of the ring (a captain's own order: no delay)
        var book = W.Formations; if (book == null) return;
        foreach (var r in book.Rows)
        {
            if (r.Side != Team || r.Kind is FormationKind.Cavalry or FormationKind.WagonFort || r.ActiveNow == 0) continue;
            var o = FormationOrder.Of(OrderKind.MoveTo); o.TargetX = (int)cx; o.TargetY = (int)cy;
            book.Apply(W, r, o, W.Tick);
        }
    }

    // ------------------------------------------------------------------ the cheval de frise

    /// <summary>Lays a cheval de frise (40 kg) at a point; it takes two men to carry it, so fewer lay none.</summary>
    public bool PlaceCheval(int x, int y, byte heading, int men)
    {
        if (men < R.ChevalMen) return false;
        Chevaux.Add(new Cheval(x, y, heading));
        return true;
    }

    /// <summary>How far (mm) a point is from the nearest cheval's timber, or long.MaxValue where there is none.</summary>
    public long ChevalDistance(long x, long y)
    {
        long best = long.MaxValue;
        foreach (var c in Chevaux)
        {
            var (cos, sin) = FrameMath.CosSin(c.Heading << 16);
            long rx = x - c.X, ry = y - c.Y, along = (rx * cos + ry * sin) >> 30, half = R.ChevalLenMm / 2;
            along = Math.Clamp(along, -half, half);
            long px = c.X + ((along * cos) >> 30), py = c.Y + ((along * sin) >> 30);
            long d = FightMath.Len(x - px, y - py); if (d < best) best = d;
        }
        return best;
    }

    /// <summary>A man's share of his walk at a point (permille): a third within 600 mm of a cheval's timber.</summary>
    public int FootPm(int x, int y) => Chevaux.Count > 0 && ChevalDistance(x, y) <= 600 ? R.ChevalFootPm : 1000;

    // ------------------------------------------------------------------ the horses

    /// <summary>The standing boards (and a cheval's braced count) a horse would meet at a point, as braced polearms: the larger of the two.</summary>
    public int BracedNear(long x, long y) => Math.Max(Fort.BracedAt(x, y), Chevaux.Count > 0 && ChevalDistance(x, y) <= R.ArcMm ? R.ChevalBraced : 0);

    /// <summary>
    /// The first board, chain or cheval along a horse's heading within <paramref name="rangeMm"/>, sampled every half metre: its distance, where it is and how many braced polearms it counts for; -1 when there is none or the fort is the
    /// horse's own side's.
    /// </summary>
    public int Ahead(int team, int x, int y, int hx, int hy, int rangeMm, out int ox, out int oy, out int braced)
    {
        ox = oy = braced = 0;
        if (team == Team) return -1;
        for (int d = 500; d <= rangeMm; d += 500)
        {
            int px = x + (int)((long)hx * d >> 10), py = y + (int)((long)hy * d >> 10);
            bool board = Fort.BlocksMove(px, py);
            bool cheval = !board && Chevaux.Count > 0 && ChevalDistance(px, py) <= 500;
            if (!board && !cheval) continue;
            ox = px; oy = py; braced = cheval ? R.ChevalBraced : Math.Max(1, Fort.BracedAt(px, py));
            return d;
        }
        return -1;
    }

    // ------------------------------------------------------------------ hash and save

    public ulong Hash(ulong h)
    {
        h = FightMath.Mix(h, Fort.ClockSec); h = FightMath.Mix(h, Fort.FormedAtSec); h = FightMath.Mix(h, FormedTick);
        foreach (var c in Fort.Chains) h = FightMath.Mix(h, (long)c.State | (long)c.Left << 8);
        foreach (var s in Fort.Slots) h = FightMath.Mix(h, s.X ^ s.Y << 20 ^ (s.Arrived ? 1L << 50 : 0));
        foreach (var (g, u) in Sorties) h = FightMath.Mix(h, (long)g << 32 | (uint)u);
        foreach (var c in Chevaux) h = FightMath.Mix(h, (long)c.X << 24 ^ c.Y ^ (long)c.Heading << 56);
        return h;
    }

    /// <summary>Every integer of the layer: the team, the formed tick, the AI flag and the driver's Riding, the fort (length, then its numbers), the sorties, the chevaux, the wagons' ids.</summary>
    public long[] Pack()
    {
        var f = Fort.Pack();
        var l = new List<long> { Team, FormedTick, AiForms ? 1 : 0, DriverRiding, f.Length };
        l.AddRange(f);
        l.Add(Sorties.Count); foreach (var (g, u) in Sorties) { l.Add(g); l.Add(u); }
        l.Add(Chevaux.Count); foreach (var c in Chevaux) { l.Add(c.X); l.Add(c.Y); l.Add(c.Heading); }
        l.Add(Wagons.Count); foreach (var v in Wagons) l.Add((long)v.Id.Value);
        return [.. l];
    }

    /// <summary>Rebuilds a layer from <see cref="Pack"/> over a restored fight; the ride book holds the vehicles.</summary>
    public static FortLayer Unpack(FightWorld w, RideBook rides, long[] d)
    {
        var fort = new WagonFort(w.Content.Db, rides, w.Seed);
        int k = 0;
        var layer = new FortLayer(w, fort, (int)d[k++]) { };
        layer.FormedTick = (int)d[k++]; layer.AiForms = d[k++] != 0; layer.DriverRiding = (int)d[k++];
        int n = (int)d[k++]; fort.Unpack(d[k..(k + n)]); k += n;
        for (int i = 0, c = (int)d[k++]; i < c; i++) layer.Sorties.Add(((int)d[k++], (int)d[k++]));
        for (int i = 0, c = (int)d[k++]; i < c; i++) layer.Chevaux.Add(new Cheval((int)d[k++], (int)d[k++], (byte)d[k++]));
        for (int i = 0, c = (int)d[k++]; i < c; i++) if (rides.Vehicles.Find(new EntityId((ulong)d[k++])) is { } v) layer.Wagons.Add(v); else { }
        layer.Attach();
        return layer;
    }
}
