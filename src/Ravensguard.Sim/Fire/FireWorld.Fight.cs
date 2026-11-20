using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Burning;

/// <summary>A villager fighting the fire: where he stands and whether he carries a fire hook (fire 4.4).</summary>
public readonly record struct FireFighter(ulong Person, int X, int Y, int Z, bool Hook = false);

/// <summary>
/// The scores and ranges of the response to a fire (fire 4.3, 4.4): who shouts, who is moved to fight, who runs. Pure functions of integers; the live tier calls them when it scores a
/// person (a utility profile never sees fire: <c>FightFire</c> and <c>Flee</c> are chosen here and handed to the brain as the result).
/// </summary>
public static class FireAi
{
    /// <summary>"Fire!" is a shout heard to 100 m (fire 4.4).</summary>
    public const int ShoutM = 100;
    /// <summary>People flee fire within 10 m unless they fight it (4.3).</summary>
    public const int FleeM = 10;

    public static bool Hears(int distM) => distM <= ShoutM;

    /// <summary>
    /// The score of <c>FightFire</c> for someone who heard the shout (0: no): the owner and his household and kin first (900), neighbours (600), others who are guards or hold a bucket (350); a
    /// person whose own house is downwind always (1000); less the distance (2 per metre beyond 20) and cowardice (courage below 300 pm halves it); nobody fights from a bed asleep.
    /// </summary>
    public static int FightScore(bool ownHouse, bool kin, bool neighbour, bool guardOrBucket, bool houseDownwind, int distM, int couragePm, bool asleep)
    {
        if (asleep || !Hears(distM)) return 0;
        int s = houseDownwind || ownHouse ? 1000 : kin ? 900 : neighbour ? 600 : guardOrBucket ? 350 : 0;
        if (s == 0) return 0;
        s -= Math.Max(0, distM - 20) * 2;
        if (couragePm < 300) s /= 2;
        return Math.Max(0, s);
    }

    /// <summary>The threat (permille) fire presents at a distance: 1000 on the cell, falling to 0 at 10 m; the utility brain's <c>Flee</c> reads it as its threat need.</summary>
    public static int FleeThreatPm(int distM) => distM >= FleeM ? 0 : (FleeM - distM) * 1000 / FleeM;
}

public sealed partial class FireWorld
{
    /// <summary>The people fighting the fire this second (the live tier fills them from the persons whose action is <c>FightFire</c>).</summary>
    public Action<List<FireFighter>>? FightersNear { get; set; }
    /// <summary>The wells, troughs and river cells buckets are filled at (x, y).</summary>
    public Func<IEnumerable<(int X, int Y)>>? WaterSources { get; set; }

    private readonly List<FireFighter> _fighters = [];
    private long _bucketMilli;
    private long _hookMilli;

    /// <summary>Water walks at 3 m a sim-second (a jog); a bucket is filled and thrown in 4 (fire 4.4).</summary>
    private const int JogMPerS = 3, FillThrowS = 4;
    /// <summary>A chain forms when 4 or more people stand between the water and the fire; buckets pass at 1 per 3 sim-s down a chain.</summary>
    public const int ChainMin = 4, ChainBucketEveryS = 3;
    /// <summary>Soaked thatch resists for 2 game hours (x0.3).</summary>
    public const int SoakGameSec = 2 * 3600;
    /// <summary>A hook pulls down a thatch cell in 20 sim-s.</summary>
    public const int HookS = 20;

    private void Fighting(long s, long sec)
    {
        if (FightersNear == null || Book.Cells.Count == 0) return;
        _fighters.Clear();
        FightersNear(_fighters);
        if (_fighters.Count == 0) return;
        _fighters.Sort((a, b) => a.Person.CompareTo(b.Person));
        // the water: the nearest source to the fire's middle
        long sx = 0, sy = 0; int n = 0;
        foreach (var c in Book.Cells.Values) { var (x, y, _) = Unpack(c.Key); sx += x; sy += y; n++; }
        int cx = (int)(sx / n), cy = (int)(sy / n);
        int dist = int.MaxValue;
        if (WaterSources != null) foreach (var (wx, wy) in WaterSources()) dist = Math.Min(dist, (int)IntMath.Isqrt((long)(wx - cx) * (wx - cx) + (long)(wy - cy) * (wy - cy)));
        if (dist == int.MaxValue) return;                                   // no water to be had: nothing to throw
        int people = _fighters.Count;
        long perSecMilli;
        if (people >= ChainMin) perSecMilli = (long)(people / ChainMin) * 1000 / ChainBucketEveryS;
        else
        {
            int round = Math.Max(1, 2 * dist / JogMPerS) + FillThrowS;
            perSecMilli = (long)people * 1000 / round;
        }
        _bucketMilli += perSecMilli;
        int hooks = 0; foreach (var f in _fighters) if (f.Hook) hooks++;
        _hookMilli += (long)hooks * 1000 / HookS;
        int buckets = (int)(_bucketMilli / 1000); _bucketMilli %= 1000;
        int pulls = (int)(_hookMilli / 1000); _hookMilli %= 1000;
        if (buckets == 0 && pulls == 0) return;
        // the front: burning cells by threat, then the unburnt thatch they touch
        _keys.Clear(); _keys.AddRange(Book.Cells.Keys);
        int fi = 0;
        for (int b = 0; b < buckets; b++)
        {
            if (!ThrowBucket(sec)) break;
            NoteFighter(_fighters[fi++ % people].Person, sec);
        }
        for (int h = 0; h < pulls; h++)
        {
            if (!PullDown(sec, _fighters)) break;
        }
    }

    private void NoteFighter(ulong person, long sec)
    {
        foreach (var c in Book.Cells.Values) { if (Book.Incidents.TryGetValue(c.Fire, out var inc)) inc.Fighters.Add(person); break; }
    }

    /// <summary>One bucket: on the hottest burning roof cell (the spark throwers first), else a cell burning at 2, else it wets the nearest unburnt fuel the fire touches, else it falls on a cell burning at 1.</summary>
    private bool ThrowBucket(long sec)
    {
        FireCell? best = null; int bestScore = -1;
        foreach (var k in _keys)
        {
            if (!Book.Cells.TryGetValue(k, out var c)) continue;
            var (x, y, z) = Unpack(k);
            int sc = c.Intensity == 3 ? (IsRoof(x, y, z) ? 400 : 300) : c.Intensity == 2 ? 200 : 100;
            if (sc > bestScore) { bestScore = sc; best = c; }
        }
        if (best == null) return false;
        if (best.Intensity >= 2) { Bucket(best, sec); return true; }
        // the fire is down to its first flames: wet what it will reach next, and if nothing, put it out
        long? wet = NextToBurn(sec);
        if (wet is { } wk) { Book.Soaked[wk] = sec + SoakGameSec; return true; }
        Bucket(best, sec);
        return true;
    }

    private void Bucket(FireCell c, long sec)
    {
        if (++c.Buckets >= 3)
        {
            c.Buckets = 0;
            if (c.Intensity <= 1) { _doused.Clear(); Douse(c.Key, sec); return; }
            c.Knock++;
            c.Intensity--;
        }
    }

    /// <summary>The first unburnt, unsoaked fuel cell next to a burning cell, in key order.</summary>
    private long? NextToBurn(long sec)
    {
        foreach (var k in _keys)
        {
            if (!Book.Cells.TryGetValue(k, out var c)) continue;
            var (x, y, z) = Unpack(k);
            for (int d = 0; d < 8; d++)
            {
                var (dx, dy) = Around[d];
                long tk = Key(x + dx, y + dy, z);
                if (Book.Cells.ContainsKey(tk)) continue;
                if (Book.Soaked.TryGetValue(tk, out var until) && sec < until) continue;
                if (FuelAt(x + dx, y + dy, z).Burns) return tk;
            }
        }
        return null;
    }

    /// <summary>A hook pulls down the unburnt thatch roof cell nearest the fire: removed fuel, a firebreak (fire 4.4).</summary>
    private bool PullDown(long sec, List<FireFighter> fighters)
    {
        long best = -1; long bestD = long.MaxValue;
        foreach (var k in _keys)
        {
            var (x, y, z) = Unpack(k);
            for (int d = 0; d < 8; d++)
            {
                var (dx, dy) = Around[d];
                int tx = x + dx, ty = y + dy;
                long tk = Key(tx, ty, z);
                if (Book.Cells.ContainsKey(tk)) continue;
                var p = Rooms.PieceAt(tx, ty, z);
                if (p == null || p.Kind != PieceKind.Roof || !Fuels.ClassOf(p.Id).Burns) continue;
                long dd = tk;                                              // the lowest key: stable
                if (dd < bestD) { bestD = dd; best = tk; }
            }
        }
        if (best < 0) return false;
        var (bx, by, bz) = Unpack(best);
        if (!(Destroy?.Invoke(bx, by, bz, false, false, sec, EntityId.None) ?? false))
        {
            Rooms.Apply([new CellEdit(bx, by, bz, CellLayer.Structure, null)]);
            Tiles?.Set(bx, by, bz, Rooms.PieceAt(bx, by, bz)?.Id, Rooms.FixtureAt(bx, by, bz)?.Id);
        }
        Book.Scars[best] = ScarKind.Rubble;
        foreach (var f in fighters) if (f.Hook) { NoteFighter(f.Person, sec); break; }
        return true;
    }
}
