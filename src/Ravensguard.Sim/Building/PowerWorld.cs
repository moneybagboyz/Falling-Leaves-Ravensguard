using Ravensguard.Content;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Building;

/// <summary>Writes a chronicle line for the power rules (the abstract tick's outbox): type, text key, typed parameters, subjects, causes and machine data.</summary>
public delegate EntityId PowerChronicle(GameTime when, string typeId, string textKey, IReadOnlyList<ChronParam> parameters, IReadOnlyList<EntityId> subjects, IReadOnlyList<Cause> causes, (string Key, string Value)[] data);

/// <summary>
/// P7-LB1 (large-buildings 4): the power rules over a home region's built pieces. Once a game day (<see cref="Day"/>, at hour 0 on the abstract tick) the nets are derived from the construction
/// book's fixtures, each source's watts are worked out from the water (flow of the season, ice, river-high days, the head at the wheel), the wind or the animals, the machines share them, and
/// the pieces wear: 1 hit point a running day, a breakdown with 2 permille (stream 0x730), flood damage to a wheel on a river-high day with its sluice open with 5% (stream 0x731). Nets are
/// derived and never saved; the sluices' state is the construction book's, hit points are the built cells'. Integer arithmetic only, nets and pieces walked in slot-key order.
/// </summary>
public sealed class PowerWorld
{
    public ConstructionWorld Cw { get; }
    public PowerCatalog Cat { get; }
    public ulong Seed { get; }
    /// <summary>The region's water (null: no waterways known, wheels find no bank).</summary>
    public WaterSites? Sites { get; set; }
    /// <summary>The mean temperature (C) of a game day at the home settlement; null: 10 C (never frozen).</summary>
    public Func<long, int>? MeanC { get; set; }
    /// <summary>River-high days of a game day (0: not river-high); null: never.</summary>
    public Func<long, int>? RiverHighDays { get; set; }
    /// <summary>The wind (mm/s) of a game day; null: calm.</summary>
    public Func<long, int>? WindMmps { get; set; }
    /// <summary>A windmill's open ground (4.2): whether a cell (region metres) holds part of a building; null: nothing blocks.</summary>
    public Func<int, int, bool>? BuildingAt { get; set; }
    public PowerChronicle? Chronicle { get; set; }

    /// <summary>The nets of the last <see cref="Day"/> (or <see cref="Refresh"/>), by source slot key.</summary>
    public IReadOnlyList<PowerNet> Nets => _nets;
    private List<PowerNet> _nets = [];
    private readonly Dictionary<long, WaterPoint?> _bank = [];
    /// <summary>The day the rules last ran for (never saved: the abstract tick calls once a day).</summary>
    public long LastDay { get; private set; } = long.MinValue;

    public WaterPowerRulesDef Rules => Cat.Rules;

    public PowerWorld(ConstructionWorld cw, PowerCatalog cat, ulong seed) { Cw = cw; Cat = cat; Seed = seed; }

    // ------------------------------------------------------------------ the nets

    /// <summary>The power-relevant fixtures of the construction book, in slot-key order.</summary>
    public List<PowerCell> Cells()
    {
        var list = new List<PowerCell>();
        foreach (var b in Cw.Book.Built.Values)
        {
            if (b.Slot != GhostSlot.Fixture || (Cat.Get(b.Piece) == null && !Cat.Stations.Contains(b.Piece))) continue;
            var c = Cw.Costs.Get(b.Piece);
            list.Add(new PowerCell(b.X, b.Y, b.Z, b.Piece, b.Hp, c == null ? b.Hp : PieceCosts.BuiltHp(c, b.BuiltSkill), b.SlotKey));
        }
        return list;
    }

    public bool IsShut(PowerCell sluice) => Cw.Book.SluiceShut.Contains(sluice.Key);

    /// <summary>Derives the nets and works out the watts of <paramref name="day"/> without wearing anything (the panel's and the plan checks' read).</summary>
    public IReadOnlyList<PowerNet> Refresh(long day)
    {
        var cells = Cells();
        _nets = cells.Count == 0 ? [] : PowerNets.Build(cells, Cat);
        if (_nets.Count == 0) return _nets;
        var season = new GameTime(day * 86400).Season;
        bool frozen = WaterPower.Frozen(Rules, day, d => MeanC?.Invoke(d) ?? 10);
        bool high = (RiverHighDays?.Invoke(day) ?? 0) > 0;
        int wind = WindMmps?.Invoke(day) ?? 0;
        foreach (var n in _nets) Work(n, cells, season, frozen, high, wind);
        return _nets;
    }

    private void Work(PowerNet n, List<PowerCell> cells, Season season, bool frozen, bool high, int windMmps)
    {
        n.Stopped = false; n.Why = ""; n.Water = null;
        int watts = 0;
        var def = n.SourceDef;
        bool shut = n.Sluices.Any(IsShut);
        switch (def.Role)
        {
            case "wheel":
            {
                if (shut) { n.Stopped = true; n.Why = "lb_sluice_shut"; break; }
                if (Sites == null) { n.Why = "lb_no_water"; break; }
                if (!_bank.TryGetValue(n.Source.Key, out var bank)) _bank[n.Source.Key] = bank = Sites.Bank(n.Source.X, n.Source.Y);
                if (bank is not { } p) { n.Why = "lb_no_bank"; break; }
                (n.LeatM, n.Weir) = Waterworks(n.Source, cells);
                var o = Sites.Wheel(p, def, season, n.LeatM, n.Weir, frozen, high);
                n.Water = o;
                if (frozen) { n.Stopped = true; n.Why = "lb_frozen"; break; }
                if (!o.Works) { n.Why = o.Key; break; }
                watts = o.Watts;
                break;
            }
            case "windmill":
                if (BuildingAt != null && !WindPower.OpenGround(Rules, n.Source.X, n.Source.Y, BuildingAt)) { n.Why = "lb_wind_blocked"; break; }
                watts = WindPower.Watts(Rules, def.RatedW, windMmps);
                if (watts == 0) n.Why = windMmps / 1000 > Rules.WindMaxMps ? "lb_wind_furled" : "lb_wind_calm";
                break;
            case "gin":
                watts = def.RatedW;
                break;
        }
        if (watts > 0 && n.Source.Hp <= 0) { watts = 0; n.Why = "lb_worn_out"; }
        else if (watts > 0 && n.Source.Hp * 100 < n.Source.MaxHp * Rules.HalfEffectBelowPct) watts /= 2;
        n.Apportion(watts, Rules);
    }

    /// <summary>The leat length (the leat pieces joined to the wheel, orthogonally) and whether a weir stands within a leat's length and 12 m of it.</summary>
    private (int LeatM, bool Weir) Waterworks(PowerCell wheel, List<PowerCell> cells)
    {
        var leat = new Dictionary<(int, int), PowerCell>();
        var weirs = new List<PowerCell>();
        foreach (var c in cells)
        {
            if (c.Z != wheel.Z) continue;
            var role = Cat.Get(c.Piece)?.Role;
            if (role == "leat") leat[(c.X, c.Y)] = c; else if (role == "weir") weirs.Add(c);
        }
        int len = 0;
        if (leat.Count > 0)
        {
            var seen = new HashSet<(int, int)>(); var q = new Queue<(int, int)>();
            foreach (var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
                if (leat.ContainsKey((wheel.X + dx, wheel.Y + dy)) && seen.Add((wheel.X + dx, wheel.Y + dy))) q.Enqueue((wheel.X + dx, wheel.Y + dy));
            while (q.Count > 0)
            {
                var (x, y) = q.Dequeue(); len++;
                foreach (var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
                    if (leat.ContainsKey((x + dx, y + dy)) && seen.Add((x + dx, y + dy))) q.Enqueue((x + dx, y + dy));
            }
        }
        len = Math.Min(len, Rules.LeatMaxM);
        bool weir = weirs.Any(w => Math.Max(Math.Abs(w.X - wheel.X), Math.Abs(w.Y - wheel.Y)) <= len + 12);
        return (len, weir);
    }

    // ------------------------------------------------------------------ the day

    /// <summary>
    /// One game day (hour 0): the nets and their watts, then wear and chance in slot-key order. A piece that runs (a wheel with water and its sluice open, a machine that is working with power) loses
    /// <c>WearHpPerDay</c>; each draws stream 0x730 for a breakdown; a wheel with its sluice open on a river-high day draws stream 0x731 for flood damage, and a wheel brought to 0 by the flood writes
    /// <c>wheel_wrecked_flood</c>. Idempotent for a day already done.
    /// </summary>
    public void Day(long day)
    {
        if (day <= LastDay) return;
        LastDay = day;
        var nets = Refresh(day);
        if (nets.Count == 0) return;
        bool high = (RiverHighDays?.Invoke(day) ?? 0) > 0;
        bool frozen = WaterPower.Frozen(Rules, day, d => MeanC?.Invoke(d) ?? 10);
        foreach (var n in nets)
        {
            bool turning = !n.Stopped && n.Watts > 0;
            if (turning) Wear(n.Source, day);
            if (n.SourceDef.Role == "wheel" && high && !frozen && !n.Sluices.Any(IsShut) && n.Source.Hp > 0)
                Flood(n, day);
            if (!turning) continue;
            foreach (var m in n.Machines)
                if (m.Working && n.SharePm > 0) Wear(m.Cell, day);
        }
        Refresh(day);
    }

    private BuiltCell? Cell(PowerCell c) => Cw.Book.Built.GetValueOrDefault(c.Key);

    private void Wear(PowerCell c, long day)
    {
        var b = Cell(c); if (b == null || b.Hp <= 0) return;
        b.Hp = Math.Max(0, b.Hp - Rules.WearHpPerDay);
        if (b.Hp > 0 && DetRng.Range(Seed, day, (ulong)c.Key, RngStreams.MachineBreakdown, 0, 0, 1000) < Rules.BreakdownPm)
            b.Hp = Math.Max(0, b.Hp - Math.Max(1, c.MaxHp * Rules.BreakdownLossPct / 100));
    }

    private void Flood(PowerNet n, long day)
    {
        var b = Cell(n.Source); if (b == null) return;
        if (DetRng.Range(Seed, day, (ulong)n.Source.Key, RngStreams.FloodDamage, 0, 0, 100) >= Rules.FloodPct) return;
        int before = b.Hp;
        b.Hp = Math.Max(0, b.Hp - Math.Max(1, n.Source.MaxHp * Rules.FloodLossPct / 100));
        if (before > 0 && b.Hp == 0)
        {
            int river = _bank.TryGetValue(n.Source.Key, out var p) && p is { } wp ? wp.Waterway : -1;
            Chronicle?.Invoke(new GameTime(day * 86400), "wheel_wrecked_flood", "ch_works_wheel_flood",
                [ChronParam.Count("x", n.Source.X), ChronParam.Count("y", n.Source.Y), ChronParam.Count("river", river)], [],
                [new Cause(CauseKind.Weather, null, "", "ch_note_wheel_river_high")],
                [("x", n.Source.X.ToString()), ("y", n.Source.Y.ToString()), ("river", river.ToString())]);
        }
    }

    // ------------------------------------------------------------------ the sluice

    /// <summary>Opens or shuts the sluice built at a cell (the machine panel's switch, <c>cn:sluice</c>): false when no sluice stands there. A shut sluice stops the wheel (machines idle, no wear).</summary>
    public bool SetSluice(int x, int y, int z, bool shut)
    {
        var key = ConstructionIds.SlotKey(x, y, z, GhostSlot.Fixture);
        if (Cw.Book.Built.GetValueOrDefault(key) is not { } b || Cat.Get(b.Piece)?.Role != "switch") return false;
        if (shut) Cw.Book.SluiceShut.Add(key); else Cw.Book.SluiceShut.Remove(key);
        return true;
    }

    /// <summary>The labour a station at a cell gets from the machines coupled to it (permille: the lowest among them), or null when no powered net's machine couples to it (4.3).</summary>
    public int? LabourPmAt(int x, int y, int z)
    {
        int? best = null;
        foreach (var n in _nets)
            foreach (var m in n.Machines)
                if (m.Station is { } s && s.X == x && s.Y == y && s.Z == z)
                {
                    int l = n.LabourPm(m, Rules);
                    best = best == null ? l : Math.Min(best.Value, l);
                }
        return best;
    }
}

/// <summary>
/// P7-LB1 (large-buildings 4.6): the water check of a powered plan. The wheels of the plan's grid (turned and placed with its corner at <c>(ox, oy)</c>) must stand on a waterway's bank, and the watts
/// they give at <b>autumn</b> flow, with the plan's own leat pieces (their count is the leat's length) and weir, must meet the plan's need.
/// </summary>
public static class PlanWater
{
    /// <summary>(ok, watts at autumn, key). Keys: <c>cn_fit_power</c> (not enough fall here), <c>cn_fit_no_bank</c> (a wheel stands off the water), <c>cn_fit_no_wheel</c> (a powered plan with no wheel).</summary>
    public static (bool Ok, int HaveW, string Key) Check(WaterSites sites, PowerCatalog cat, BuildingGrid g, int ox, int oy, int needW)
    {
        if (needW <= 0) return (true, 0, "");
        int leat = 0; bool weir = false; var wheels = new List<(int X, int Y, PowerPieceDef Def)>();
        foreach (var lv in g.Levels.Values.OrderBy(l => l.Z))
        {
            if (lv.Z != 0) continue;
            for (int y = 0; y < lv.Height; y++)
                for (int x = 0; x < lv.Width; x++)
                    if (cat.Get(lv.Fixture[lv.Idx(x, y)]) is { } d)
                    {
                        if (d.Role == "leat") leat++;
                        else if (d.Role == "weir") weir = true;
                        else if (d.Role == "wheel") wheels.Add((ox + x, oy + y, d));
                    }
        }
        if (wheels.Count == 0) return (false, 0, "cn_fit_no_wheel");
        int have = 0;
        foreach (var (x, y, d) in wheels)
        {
            if (sites.Bank(x, y) is not { } p) return (false, 0, "cn_fit_no_bank");
            have += sites.Wheel(p, d, Season.Autumn, leat, weir).Watts;
        }
        return have >= needW ? (true, have, "") : (false, have, "cn_fit_power");
    }
}
