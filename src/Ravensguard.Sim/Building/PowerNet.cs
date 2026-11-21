using System.Runtime.CompilerServices;
using Ravensguard.Content;

namespace Ravensguard.Sim.Building;

/// <summary>The power content of a loaded database (large-buildings 3.2, 4.1): the rules and what each build piece does in a net, by piece id. Built once per content.</summary>
public sealed class PowerCatalog
{
    public WaterPowerRulesDef Rules { get; }
    private readonly Dictionary<string, PowerPieceDef> _by = new(StringComparer.Ordinal);
    /// <summary>The water wheels, by piece id: what a mill-site search tries.</summary>
    public IReadOnlyList<PowerPieceDef> Wheels { get; }

    public PowerCatalog(ContentDatabase db)
    {
        Rules = db.All<WaterPowerRulesDef>().OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault() ?? new WaterPowerRulesDef { Id = "water_power" };
        foreach (var d in db.All<PowerPieceDef>().OrderBy(d => d.Id, StringComparer.Ordinal)) _by[d.Piece] = d;
        Wheels = [.. _by.Values.Where(d => d.Role == "wheel").OrderBy(d => d.Piece, StringComparer.Ordinal)];
    }

    private static readonly ConditionalWeakTable<ContentDatabase, PowerCatalog> Cache = new();
    public static PowerCatalog Of(ContentDatabase db) => Cache.GetValue(db, d => new PowerCatalog(d));

    public PowerPieceDef? Get(string? piece) => piece != null && _by.TryGetValue(piece, out var d) ? d : null;
    public bool Any => _by.Count > 0;
}

/// <summary>A built fixture seen by the power rules: its cell, piece, hit points and the most it was built with (<c>Key</c> is its slot key, the order everything is walked in).</summary>
public readonly record struct PowerCell(int X, int Y, int Z, string Piece, int Hp, int MaxHp, long Key);

/// <summary>A machine on a net (large-buildings 4.3): its piece, the shaft distance it is reached at, and the station piece it couples to within 3 m (null when none: a machine that needs a station then idles).</summary>
public sealed class PowerMachine
{
    public required PowerCell Cell { get; init; }
    public required PowerPieceDef Def { get; init; }
    public int ShaftM { get; init; }
    public PowerCell? Station { get; init; }
    /// <summary>It does work when it has what it couples to (or needs nothing) and is not worn out.</summary>
    public bool Working => (Def.Couples.Count == 0 || Station != null) && Cell.Hp > 0;
    /// <summary>Per mille of its full effect from its condition: full, half below the half-effect line, none at 0 hit points.</summary>
    public int ConditionPm(WaterPowerRulesDef r) => Cell.Hp <= 0 ? 0 : Cell.Hp * 100 < Cell.MaxHp * r.HalfEffectBelowPct ? 500 : 1000;
}

/// <summary>
/// P7-LB1 (large-buildings 3.1, 4.1, 4.3): one power net, derived from the built pieces and never saved. A source (a water wheel, a windmill or a horse gin) drives the shaft cells linked to it
/// (orthogonal runs of shaft pieces, at most 12 m from the source) and the machine pieces beside them. The day's watts are set by <see cref="PowerWorld"/>; machines that are working draw their rated
/// watts, and when the sum exceeds the watts each runs at <c>P / sum</c> (<see cref="SharePm"/>).
/// </summary>
public sealed class PowerNet
{
    public required PowerCell Source { get; init; }
    public required PowerPieceDef SourceDef { get; init; }
    public List<PowerCell> Shafts { get; } = [];
    public List<PowerMachine> Machines { get; } = [];
    /// <summary>The sluices beside a wheel (within 2 m): shut, the wheel stops.</summary>
    public List<PowerCell> Sluices { get; } = [];
    /// <summary>The leat length (m) and whether a weir serves the wheel (water nets).</summary>
    public int LeatM { get; set; }
    public bool Weir { get; set; }

    // the day (set by PowerWorld.Day)
    public int Watts { get; set; }
    public int DrawW { get; set; }
    public int SharePm { get; set; }
    public bool Stopped { get; set; }
    public string Why { get; set; } = "";
    public WheelOutput? Water { get; set; }

    /// <summary>Per mille of the net's watts a working machine gets: all of it while the sum is within the watts, else <c>P / sum</c> (rounded).</summary>
    public static int Share(int watts, int drawW) => drawW <= 0 || watts >= drawW ? (watts > 0 || drawW <= 0 ? 1000 : 0) : (int)(((long)watts * 1000 + drawW / 2) / drawW);

    /// <summary>A machine's effect in permille: the net's share times its condition.</summary>
    public int EffectPm(PowerMachine m, WaterPowerRulesDef r) => !m.Working ? 0 : SharePm * m.ConditionPm(r) / 1000;

    /// <summary>The station's <c>labour_pm</c> under a machine: <c>1,000 - (1,000 - labour_pm) x effect</c> (4.1, LB-4: 785 permille on a 375 forge gives 509).</summary>
    public int LabourPm(PowerMachine m, WaterPowerRulesDef r) => 1000 - (int)(((long)(1000 - m.Def.LabourPm) * EffectPm(m, r) + 500) / 1000);

    /// <summary>Sets <see cref="SharePm"/> and <see cref="DrawW"/> from the net's watts and the machines that are working (a worn-out machine draws nothing).</summary>
    public void Apportion(int watts, WaterPowerRulesDef r)
    {
        Watts = watts;
        long draw = 0;
        foreach (var m in Machines) if (m.Working) draw += m.Def.DrawW;
        DrawW = (int)Math.Min(int.MaxValue, draw);
        SharePm = Share(watts, DrawW);
    }
}

/// <summary>Builds the nets of a set of built fixtures (the pure part of 4.3; cells come in any order and are walked by slot key).</summary>
public static class PowerNets
{
    private static readonly (int dx, int dy)[] Orth = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    public static List<PowerNet> Build(IEnumerable<PowerCell> fixtures, PowerCatalog cat)
    {
        var rules = cat.Rules;
        var at = new SortedDictionary<long, PowerCell>();
        foreach (var c in fixtures) at[c.Key] = c;
        var byXyz = new Dictionary<(int, int, int), PowerCell>();
        foreach (var c in at.Values) byXyz[(c.X, c.Y, c.Z)] = c;
        var nets = new List<PowerNet>();
        var claimed = new HashSet<long>();
        foreach (var src in at.Values)
        {
            if (cat.Get(src.Piece) is not { } sd || sd.Role is not ("wheel" or "windmill" or "gin")) continue;
            var net = new PowerNet { Source = src, SourceDef = sd };
            // the shaft runs: breadth first from the source, orthogonal, at most ShaftReachM cells along the run
            var dist = new Dictionary<long, int> { [src.Key] = 0 };
            var queue = new Queue<PowerCell>(); queue.Enqueue(src);
            var shaftDist = new List<(PowerCell Cell, int D)>();
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue(); int d = dist[cur.Key];
                if (d >= rules.ShaftReachM) continue;
                foreach (var (dx, dy) in Orth)
                {
                    if (!byXyz.TryGetValue((cur.X + dx, cur.Y + dy, cur.Z), out var nb) || dist.ContainsKey(nb.Key)) continue;
                    if (cat.Get(nb.Piece)?.Role != "shaft") continue;
                    dist[nb.Key] = d + 1; shaftDist.Add((nb, d + 1)); net.Shafts.Add(nb); queue.Enqueue(nb);
                }
            }
            // the machines beside the source or a shaft cell
            var found = new SortedDictionary<long, (PowerCell Cell, PowerPieceDef Def, int D)>();
            void Look(PowerCell from, int d, bool cam)
            {
                foreach (var (dx, dy) in Orth)
                {
                    if (!byXyz.TryGetValue((from.X + dx, from.Y + dy, from.Z), out var nb) || claimed.Contains(nb.Key)) continue;
                    if (cat.Get(nb.Piece) is not { Role: "machine" } md) continue;
                    if (md.NeedsCam && !cam) continue;
                    if (sd.Role == "windmill" && !md.WindOk) continue;
                    if (!found.TryGetValue(nb.Key, out var old) || d < old.D) found[nb.Key] = (nb, md, d);
                }
            }
            Look(src, 0, false);
            foreach (var (cell, d) in shaftDist) Look(cell, d, cat.Get(cell.Piece) is { } sp && cell.Piece == "cam_shaft" && sp.Role == "shaft");
            foreach (var (k, (cell, md, d)) in found)
            {
                claimed.Add(k);
                net.Machines.Add(new PowerMachine { Cell = cell, Def = md, ShaftM = d, Station = md.Couples.Count == 0 ? null : Station(cell, md, at.Values, rules.CoupleReachM) });
            }
            // the sluices beside a wheel
            if (sd.Role == "wheel")
                foreach (var c in at.Values)
                    if (c.Z == src.Z && cat.Get(c.Piece)?.Role == "switch" && Math.Max(Math.Abs(c.X - src.X), Math.Abs(c.Y - src.Y)) <= 2) net.Sluices.Add(c);
            nets.Add(net);
        }
        return nets;
    }

    /// <summary>The nearest station piece of the machine's kind within <paramref name="reachM"/> metres (same level; the lowest slot key breaks ties).</summary>
    private static PowerCell? Station(PowerCell m, PowerPieceDef md, IEnumerable<PowerCell> all, int reachM)
    {
        PowerCell? best = null; int bd = int.MaxValue;
        foreach (var c in all)
        {
            if (c.Z != m.Z || !md.Couples.Contains(c.Piece)) continue;
            int d = Math.Max(Math.Abs(c.X - m.X), Math.Abs(c.Y - m.Y));
            if (d <= reachM && d < bd) { bd = d; best = c; }
        }
        return best;
    }
}
