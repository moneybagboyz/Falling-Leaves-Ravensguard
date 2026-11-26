using Ravensguard.Content;
using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Siege;

/// <summary>
/// P7-SG wiring: the siege world as the campaign brain's <see cref="ISiegeHook"/> (AM-E's seam). A place with walls (the player's built wall pieces) is held as stone or palisade by what stands; a town or village without walls keeps the brain's estimate; a host
/// that arrives at a held place lays a real siege (<see cref="SiegeWorld.Invest"/>) and the verdict is what the siege became (surrendered or stormed: taken; lifted or abandoned: lifted). An open place (a farmstead) and a host the siege cannot take up keep the stub's answer.
/// </summary>
public sealed class SiegeHook(SiegeWorld sw) : ISiegeHook
{
    private readonly StubSiege _stub = new();

    public DefenceClass DefenceOf(ArmyWorld aw, int settlement)
        => sw.WallsOf?.Invoke(settlement) is { } w ? (w.Timber ? DefenceClass.Palisade : DefenceClass.Stone) : _stub.DefenceOf(aw, settlement);

    public int SiegeDays(ArmyWorld aw, int settlement, int garrison) => DefenceOf(aw, settlement) switch
    {
        DefenceClass.Open => 0,
        DefenceClass.Palisade => CampaignConfig.PalisadeDays,
        _ => CampaignConfig.StoneDays + garrison / 100,
    };

    public SiegeVerdict Resolve(ArmyEnv e, ArmyWorld aw, ArmyHost host, int settlement, int daysBesieged)
    {
        if (DefenceOf(aw, settlement) == DefenceClass.Open) return _stub.Resolve(e, aw, host, settlement, daysBesieged);
        var book = sw.Peek;
        if (book?.OfHost(host.Id) is { } open && open.Place == settlement) return SiegeVerdict.Continue;
        int since = (int)e.Day - daysBesieged;
        SiegeRow? done = null;
        if (book != null) foreach (var r in book.Sieges.Values) if (r.Host == host.Id && r.Place == settlement && r.StartDay >= since && !r.Open && (done == null || r.Id > done.Id)) done = r;
        if (done != null) return done.State is SiegeState.Surrendered or SiegeState.Stormed ? SiegeVerdict.Taken : SiegeVerdict.Lifted;
        return sw.Invest(e, host.Id, settlement) != null ? SiegeVerdict.Continue : _stub.Resolve(e, aw, host, settlement, daysBesieged);
    }
}

/// <summary>The walls of a place from what the hero has built: the wall pieces of the construction book near the place (the generator places no civic walls). Cached on the book's size and the wall world's version.</summary>
public sealed class BuiltWalls(SiegeWorld sw, ConstructionWorld cw, WallWorld walls)
{
    public const int RadiusM = 80, MinPieces = 10;
    private int _count = -1, _version = -1;
    private readonly Dictionary<int, SiegeWalls?> _cache = [];

    public SiegeWalls? Of(int place)
    {
        if (cw.Book.Built.Count != _count || walls.Version != _version) { _cache.Clear(); _count = cw.Book.Built.Count; _version = walls.Version; }
        if (_cache.TryGetValue(place, out var hit)) return hit;
        return _cache[place] = Make(place);
    }

    private SiegeWalls? Make(int place)
    {
        if ((uint)place >= (uint)sw.Armies.Region.Settlements.Count) return null;
        var (cx, cy) = sw.Armies.Center(place);
        int n = 0, timber = 0, x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        foreach (var b in cw.Book.Built.Values)
        {
            if (b.Slot != GhostSlot.Structure || b.Piece == "floor_wall_walk" || walls.Walls.Stat(b.Piece) == null || Math.Abs(b.X - cx) > RadiusM || Math.Abs(b.Y - cy) > RadiusM) continue;
            n++; if (b.Piece is "palisade" or "wall_timber" or "gate_wood") timber++;
            x0 = Math.Min(x0, b.X); y0 = Math.Min(y0, b.Y); x1 = Math.Max(x1, b.X); y1 = Math.Max(y1, b.Y);
        }
        if (n < MinPieces) return null;
        return new SiegeWalls
        {
            World = walls, Timber = timber * 2 > n, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1,
            LayXMm = (x0 + x1) * 500L + 500, LayYMm = y0 * 1000L - 60_000, AimXMm = (x0 + x1) * 500L + 500, AimYMm = y0 * 1000L + 500,          // the camp stands 60 m off (a trebuchet's minimum range is 50) and the engines aim at the near wall, not the yard
        };
    }
}
