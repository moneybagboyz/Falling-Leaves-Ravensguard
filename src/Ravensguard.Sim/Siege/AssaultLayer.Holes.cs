#pragma warning disable CS0675   // hash mixing: sign extension of a small id is deterministic and harmless here
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Siege;

/// <summary>
/// A murder hole (sieges-and-engines 4.4): a gap in the gatehouse's floor over the passage, or in a hoarding, through which defenders on <see cref="Level"/> drop a stone or pour boiling water on whoever stands in the 1 m cell below, once every
/// <c>hole_every_ticks</c> (10 s) while they have stones, or a fire and a cauldron. It is worked while a defender stands at it; a pot of fire is thrown on an engine under it.
/// </summary>
public sealed class MurderHole
{
    public int Team, X, Y, Level = 1;
    public int Stones, Pots;
    public bool Cauldron;
    public long NextTick;
    public int Dropped, Poured, Fired;
}

public sealed partial class AssaultLayer
{
    private readonly List<MurderHole> _holes = [];
    public IReadOnlyList<MurderHole> Holes => _holes;

    public MurderHole AddHole(int team, int x, int y, int level, int stones, bool cauldron, int pots = 0)
    {
        var h = new MurderHole { Team = team, X = x, Y = y, Level = level, Stones = stones, Cauldron = cauldron, Pots = pots };
        _holes.Add(h);
        return h;
    }

    private void HolesStep(int t)
    {
        for (int hi = 0; hi < _holes.Count; hi++)
        {
            var ho = _holes[hi];
            if (t < ho.NextTick) continue;
            long cx = CentreMm(ho.X), cy = CentreMm(ho.Y);
            bool manned = false;
            foreach (var d in World.Fighters)
                if (d.Team == ho.Team && d.Active && LevelOf(d.Id) >= ho.Level && RiseOf(d.Id) == 0 && DistTo(d, cx, cy) <= 1500) { manned = true; break; }
            if (!manned) continue;
            // who stands in the cell below (on the ground, within the hole's metre)
            var below = new List<Fighter>();
            foreach (var f in World.Fighters)
                if (f.Active && f.Team != ho.Team && BaseMm(f.Id) < R.LevelMm / 2 && CellOf(f.X + OriginX) == ho.X && CellOf(f.Y + OriginY) == ho.Y) below.Add(f);
            var all = new List<Fighter>();
            foreach (var f in World.Fighters)
                if (f.Active && BaseMm(f.Id) < R.LevelMm / 2 && CellOf(f.X + OriginX) == ho.X && CellOf(f.Y + OriginY) == ho.Y) all.Add(f);
            bool engineBelow = false;
            if (Engines != null) foreach (var e in Engines.Engines.Values) if (Engines.VehicleOf(e) is { } v && Engines.CellAt(v, cx, cy) != null) { engineBelow = true; break; }
            if (below.Count == 0 && !engineBelow) continue;
            ulong key = ((ulong)hi << 40) ^ (ulong)(uint)t;
            int height = ho.Level * R.LevelMm;
            bool water = ho.Cauldron && below.Count > 0 && (ho.Stones == 0 || below.Count >= 2);
            if (water)
            {
                foreach (var f in all) World.Scald(f, R.ScaldIntensity, key);          // every person in the cell is burned
                ho.Poured++;
            }
            else if (ho.Stones > 0)
            {
                ho.Stones--; ho.Dropped++;
                if (below.Count > 0) World.StoneFalls(below[0], R.DropKg, height, key);
                if (engineBelow) StoneOnEngine(cx, cy, height);
            }
            else if (!(ho.Pots > 0 && engineBelow)) continue;
            if (engineBelow && ho.Pots > 0) { PotOnEngine(cx, cy); ho.Pots--; ho.Fired++; }
            ho.NextTick = t + R.HoleEveryTicks;
        }
    }

    /// <summary>A stone falls on the cell of an engine: through the engine's own part rule (a stone breaks the parts from the top of the cell down).</summary>
    private void StoneOnEngine(long wx, long wy, int heightMm)
    {
        long kgms = (long)R.DropKg * AssaultMath.FallSpeedMmps(heightMm) / 1000;
        foreach (var e in Engines!.Engines.Values.ToList())
            if (Engines.VehicleOf(e) is { } v && Engines.CellAt(v, wx, wy) is { } c) Engines.StrikeCell(v, c.X, c.Y, kgms, World.GameSec, EngineW ?? NoWorld, int.MaxValue);
    }

    /// <summary>A pot of fire thrown at an engine: the top part there that burns may catch (wet hides ignite a tenth as often).</summary>
    private void PotOnEngine(long wx, long wy)
    {
        foreach (var e in Engines!.Engines.Values.ToList()) Engines.Ignite(e, wx, wy, World.GameSec);
    }

    private ulong HashHole(ulong h, MurderHole o)
    {
        h = FightMath.Mix(h, o.Team | (long)o.X << 8 | (long)o.Y << 32); h = FightMath.Mix(h, o.Level | (long)o.Stones << 8 | (long)o.Pots << 24 | (o.Cauldron ? 1L << 40 : 0));
        return FightMath.Mix(h, o.NextTick | (long)o.Dropped << 40 | (long)o.Poured << 48 | (long)o.Fired << 56);
    }
}
