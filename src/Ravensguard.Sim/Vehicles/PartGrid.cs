using Ravensguard.Content;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Carts;

/// <summary>Where a part of a vehicle is in its life (vehicle-designer 3.3): a ghost waiting for its part, being fitted, fitted, or broken (0 hit points).</summary>
public enum PartState : byte { Ghost, InProgress, Built, Broken }

/// <summary>
/// One part on a vehicle (vehicle-designer 3.3): its cell (column, row from the rear, deck), its place in the cell's stack, the part (an index of <see cref="VehicleCatalog.PartList"/>), its state, the quality
/// tier it was made at, its hit points in thousandths and the install work done (worker-milli-hours). Rows of a vehicle are ordered by deck, row, column and stack order.
/// </summary>
public struct PartRow
{
    public short X, Y;
    public byte Deck, Order;
    public ushort Part;
    public PartState State;
    public ItemQuality Tier;
    public int HpMilli;
    public int WorkMilli;
    public readonly bool Built => State == PartState.Built;
    public readonly long SortKey => ((long)Deck << 48) | ((long)(ushort)Y << 32) | ((long)(ushort)X << 16) | Order;
}

/// <summary>What is wrong with a design (vehicle-designer 4.1): an error stops the build (the design may still be saved), a warning is only shown.</summary>
public enum IssueLevel : byte { Warning, Error }

/// <summary>One problem of a design: a code (also the tr() key's tail, <c>vd_issue_{code}</c>), the cell it is at (-1 for the whole design) and an argument.</summary>
public readonly record struct DesignIssue(string Code, IssueLevel Level, int X = -1, int Y = -1, int Deck = 0, string Arg = "")
{
    public bool IsError => Level == IssueLevel.Error;
}

/// <summary>
/// A vehicle's grid of parts (vehicle-designer 3.3, 4.1): a blueprint's rows, legend and decks expanded into part rows, and the derived numbers of the built ones. One function derives the stats a
/// template, a designed vehicle, a half-built site and a damaged wagon all use, so a design rebuilt cell by cell in the designer has the template's numbers exactly (VD-2).
/// </summary>
public static class PartGrid
{
    /// <summary>The structural parts a cell needs (a frame, a runner, a hull, a box body); cells of only these parts need none: hitch, pole, handles, fittings.</summary>
    private static readonly string[] Structure = ["frame", "runner", "hull", "keel", "body", "deck"];
    private static readonly string[] Exempt = ["hitch", "pole", "handle", "fitting", "steer"];

    /// <summary>The part rows of a blueprint: every cell's stack ground up, deck 0 then the upper decks, all built at full hit points (Common). Unknown legend keys and parts are skipped (the validator names them).</summary>
    public static List<PartRow> Expand(VehicleBlueprintDef d, VehicleCatalog cat)
    {
        var rows = new List<PartRow>();
        void Grid(IReadOnlyList<string> grid, int deck)
        {
            for (int y = 0; y < grid.Count; y++)
                for (int x = 0; x < grid[y].Length; x++)
                {
                    char c = grid[y][x]; if (c == '_' || c == ' ') continue;
                    if (!d.Legend.TryGetValue(c.ToString(), out var ids)) continue;
                    byte order = 0;
                    foreach (var id in ids)
                    {
                        int pi = cat.PartIndex(id); if (pi < 0) continue;
                        rows.Add(new PartRow { X = (short)x, Y = (short)y, Deck = (byte)deck, Order = order++, Part = (ushort)pi, State = PartState.Built, Tier = ItemQuality.Common, HpMilli = cat.PartList[pi].Hp * 1000 });
                    }
                }
        }
        Grid(d.Rows, 0);
        foreach (var l in d.Levels) Grid(l.Rows, Math.Max(1, l.Deck));
        rows.Sort((a, b) => a.SortKey.CompareTo(b.SortKey));
        return rows;
    }

    /// <summary>
    /// The derived stats of the parts that count (<paramref name="builtOnly"/>: the fitted and unbroken ones; else every part, the design's own numbers): mass, cargo, wheels, axles, hitch, seats, the
    /// turning radius and base speed (the template's numbers where it names them, else the rules of 4.1), and for a hull its waterplane, form, depth, beam and the parts' mass moment for BoatMath.
    /// </summary>
    public static VehicleStats Derive(VehicleBlueprintDef d, IReadOnlyList<PartRow> rows, VehicleCatalog cat, bool builtOnly = true)
    {
        int width = d.Rows.Count > 0 ? d.Rows[0].Length : 0;
        int seatRow = -1, mass = 0, cargoKg = 0, cargoL = 0, wheels = 0, axles = 0, seats = 0, hitches = 0, wheelHp = 0, axleKg = 0, rowers = 0, polers = 0, sail = 0, oars = 0;
        long moment = 0;
        bool pivot = false, rudder = false, steerOar = false, pole = false, shafts = false, handles = false, tow = false, ramps = false, fishWell = false, shop = false, hearth = false, cover = false, keel = false;
        int wpPm = 0, blockPm = 0, depthMm = 0, keelWp = 0, keelBlock = 0, keelDepth = 0;
        var hullCells = new HashSet<(int, int)>();
        string hitchTag = d.Hitch.Kind;
        foreach (var r in rows)
        {
            if (builtOnly && r.State != PartState.Built) continue;
            var p = cat.PartList[r.Part];
            mass += p.MassKg; cargoKg += p.CargoKg; cargoL += p.CargoL;
            var t = p.Tags;
            if (t.Contains("wheel")) { wheels++; wheelHp = wheelHp == 0 ? p.Hp : Math.Min(wheelHp, p.Hp); }
            if (t.Contains("axle")) { axles++; axleKg += p.AxleKg; }
            if (t.Contains("seat")) { seats++; if (seatRow < 0 && r.Deck == 0) seatRow = r.Y; }
            if (t.Contains("hitch") && t.Contains(hitchTag)) hitches++;
            if (t.Contains("hull") && r.Deck == 0)
            {
                hullCells.Add((r.X, r.Y));
                if (t.Contains("keel")) { keel = true; keelWp = p.WaterplanePm; keelBlock = p.BlockPm; keelDepth = p.HullDepthMm; }
                else if (p.WaterplanePm > 0 && wpPm == 0) { wpPm = p.WaterplanePm; blockPm = p.BlockPm; depthMm = p.HullDepthMm; }
            }
            if (t.Contains("tow")) tow = true;
            if (t.Contains("ramp")) ramps = true;
            if (t.Contains("fish_well")) fishWell = true;
            if (t.Contains("shop")) shop = true;
            if (t.Contains("hearth")) hearth = true;
            if (t.Contains("tilt") || t.Contains("cabin")) cover = true;
            if (t.Contains("oars")) oars++;
            rowers += p.Rowers; polers += p.Polers; sail += p.SailM2;
            switch (p.Steer) { case "pivot_axle": pivot = true; break; case "rudder": rudder = true; break; case "steering_oar": steerOar = true; break; case "pole": pole = true; break; case "shafts": shafts = true; break; case "handles": handles = true; break; }
        }
        if (keel) { wpPm = keelWp; blockPm = keelBlock; depthMm = keelDepth; }
        bool boat = hullCells.Count > 0;
        // the hull's parts sit at half its depth; everything else at its own height (KG, vehicle-designer 4.5)
        foreach (var r in rows)
        {
            if (builtOnly && r.State != PartState.Built) continue;
            var p = cat.PartList[r.Part];
            int h = p.Tags.Contains("hull") ? depthMm / 2 : p.HeightMm + (r.Deck > 0 ? depthMm : 0);
            moment += (long)p.MassKg * h;
        }
        int beam = 0, hullLen = 0, perim = 0;
        if (boat)
        {
            int x0 = hullCells.Min(c => c.Item1), x1 = hullCells.Max(c => c.Item1), y0 = hullCells.Min(c => c.Item2), y1 = hullCells.Max(c => c.Item2);
            beam = x1 - x0 + 1; hullLen = y1 - y0 + 1; perim = 2 * (beam + hullLen);
        }
        int axleLimit = d.AxleLimitKg > 0 ? d.AxleLimitKg : axleKg;
        int radius = d.TurningRadiusDm > 0 ? d.TurningRadiusDm
            : boat ? Math.Max(30, hullLen * 10)
            : axles == 0 ? (wheels == 1 ? 10 : 30) : axles == 1 ? (width <= 1 ? 30 : 40) : axles == 2 ? 80 : 100;
        if (pivot && !boat) radius = radius * 600 / 1000;
        int baseSpeed = d.BaseSpeedCmps > 0 ? d.BaseSpeedCmps : boat ? 150 : d.Hitch.Kind switch { "pushed" => 150, "pole_yoke" => 180, _ => 250 };
        string steer = pivot ? "pivot_axle" : pole ? "pole" : shafts ? "shafts" : handles ? "handles" : rudder ? "rudder" : steerOar ? "steering_oar" : d.Hitch.Kind == "rope" ? "rope" : rowers >= 2 && oars >= 2 ? "oars" : "";
        return new VehicleStats
        {
            MassKg = mass, CargoKg = cargoKg, CargoL = cargoL, Wheels = wheels, Axles = axles, AxleLimitKg = axleLimit, HitchSlots = d.Hitch.Slots, MaxAnimals = Math.Max(d.Hitch.Slots, d.Hitch.MaxAnimals), HitchKind = d.Hitch.Kind,
            Driver = d.Driver, Passengers = d.Passengers, Seats = seats, BaseSpeedCmps = baseSpeed, TurningRadiusDm = radius, WidthCells = width, LengthCells = d.Rows.Count, SeatRow = seatRow, Runners = d.Runners, WheelHp = wheelHp,
            Hitches = hitches, Boat = boat, HullCells = hullCells.Count, BeamCells = beam, HullLengthCells = hullLen, PerimeterM = perim, WaterplanePm = wpPm, BlockPm = blockPm, HullDepthMm = depthMm,
            WaterplaneMm2 = (long)hullCells.Count * 1_000_000L * wpPm / 1000, MomentKgMm = moment, Rowers = oars > 0 ? rowers : 0, Polers = polers, SailM2 = sail,
            TowPost = tow, Ramps = ramps, FishWell = fishWell, Shop = shop, Hearth = hearth, Covered = cover, Steer = steer, PivotAxle = pivot, Keel = keel,
        };
    }

    /// <summary>
    /// Every problem of a design (vehicle-designer 4.1), in a fixed order: unknown parts and keys, row widths, unsupported parts (a part whose <c>needs_below</c> is not under it), cells without a frame,
    /// the hitch (a pull point exactly when animals pull, one team at least), a seat for a driven vehicle, wheels in pairs on axles, steering (a pole or a pivoting axle on a four-wheeler; a rudder, a
    /// steering oar, oars or a rope on a boat), the hull (gaps in the planking, capsizing at full load, too deep for <paramref name="localDepthMm"/>), and a boat with no way to move.
    /// </summary>
    public static List<DesignIssue> Validate(VehicleBlueprintDef d, VehicleCatalog cat, int localDepthMm = 0, WaterTuning? water = null)
    {
        var issues = new List<DesignIssue>();
        void E(string code, int x = -1, int y = -1, int deck = 0, string arg = "") => issues.Add(new DesignIssue(code, IssueLevel.Error, x, y, deck, arg));
        void W(string code, int x = -1, int y = -1, int deck = 0, string arg = "") => issues.Add(new DesignIssue(code, IssueLevel.Warning, x, y, deck, arg));
        if (d.Rows.Count == 0 || d.Rows[0].Length == 0) { E("empty"); return issues; }
        int width = d.Rows[0].Length;
        foreach (var (k, ids) in d.Legend)
        {
            if (k.Length != 1) E("legend_key", arg: k);
            foreach (var id in ids) if (cat.PartIndex(id) < 0) E("unknown_part", arg: id);
        }
        void CheckGrid(IReadOnlyList<string> grid, int deck)
        {
            for (int y = 0; y < grid.Count; y++)
            {
                if (grid[y].Length != width) E("row_width", -1, y, deck, $"{grid[y].Length}/{width}");
                for (int x = 0; x < grid[y].Length; x++)
                {
                    char c = grid[y][x]; if (c == '_' || c == ' ') continue;
                    if (!d.Legend.TryGetValue(c.ToString(), out var ids)) { E("unknown_key", x, y, deck, c.ToString()); continue; }
                    var stack = new List<VehiclePartDef>();
                    foreach (var id in ids)
                    {
                        if (cat.PartIndex(id) is int pi && pi >= 0)
                        {
                            var p = cat.PartList[pi];
                            if (p.NeedsBelow.Count > 0 && !stack.Any(s => p.NeedsBelow.Contains(s.Id)) && !(deck > 0 && BelowHas(d, x, y, deck, cat, p.NeedsBelow))) E("unsupported", x, y, deck, id);
                            stack.Add(p);
                        }
                    }
                    bool structural = stack.Any(p => p.Tags.Any(t => Structure.Contains(t)));
                    bool exempt = stack.All(p => p.Tags.Any(t => Exempt.Contains(t)));
                    if (!structural && !exempt && !(deck > 0 && BelowHas(d, x, y, deck, cat, null))) E("no_frame", x, y, deck);
                }
            }
        }
        CheckGrid(d.Rows, 0);
        foreach (var l in d.Levels) CheckGrid(l.Rows, Math.Max(1, l.Deck));

        var st = Derive(d, Expand(d, cat), cat, builtOnly: false);
        if (d.Hitch.MaxAnimals != 0 && d.Hitch.MaxAnimals < d.Hitch.Slots) E("hitch_max", arg: $"{d.Hitch.MaxAnimals}/{d.Hitch.Slots}");
        if (d.Hitch.Slots > 0 && st.Hitches == 0) E("no_pull_point", arg: d.Hitch.Kind);
        if (!st.Boat && d.Hitch.Slots == 0 && d.Hitch.Kind != "pushed") E("pushed_only", arg: d.Hitch.Kind);
        if (d.Hitch.Slots > 0 && d.Hitch.Kind is "shafts" or "pole_yoke" && st.Seats == 0) E("no_seat");
        if (!st.Boat && d.Passengers > 0 && st.Seats == 0 && d.Hitch.Slots > 0) E("no_seat_passengers", arg: d.Passengers.ToString(System.Globalization.CultureInfo.InvariantCulture));
        bool barrow = d.Hitch.Kind == "pushed" && d.Hitch.Slots == 0 && st.Wheels == 1;
        if (st.Wheels % 2 != 0 && !barrow) E("unpaired_wheel", arg: st.Wheels.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (st.Wheels > 1 && st.Axles * 2 < st.Wheels) E("axles_short", arg: $"{st.Axles}/{(st.Wheels + 1) / 2}");
        if (!st.Boat && st.Axles >= 2 && st.Steer is not ("pivot_axle" or "pole")) E("no_steering");
        if (st.Boat)
        {
            if (st.Steer is not ("rudder" or "steering_oar" or "oars" or "rope")) E("no_steering");
            // a hull with a gap: a cell of the hull's outline (its rectangle on deck 0) with no hull plank leaks
            // (P7-SH-A: a plank is any hull part that is not the keel, so the sea hulls count as planking too)
            var cells = Expand(d, cat).Where(r => r.Deck == 0 && cat.PartList[r.Part].Tags.Contains("hull") && !cat.PartList[r.Part].Tags.Contains("keel")).Select(r => (X: (int)r.X, Y: (int)r.Y)).ToHashSet();
            var hullish = Expand(d, cat).Where(r => r.Deck == 0 && cat.PartList[r.Part].Tags.Contains("hull")).Select(r => (X: (int)r.X, Y: (int)r.Y)).ToList();
            int x0 = hullish.Min(c => c.X), x1 = hullish.Max(c => c.X), y0 = hullish.Min(c => c.Y), y1 = hullish.Max(c => c.Y);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) if (!cells.Contains((x, y))) E("hull_gap", x, y);
            var wt = water ?? WaterTuning.Default;
            int crewKg = (d.Driver + d.Passengers + st.Rowers) * BoatMath.PersonKg;
            var full = BoatMath.Load(st, st.CargoKg, crewKg, wt);
            var empty = BoatMath.Load(st, 0, Math.Max(d.Driver, st.Rowers > 0 ? 1 : 0) * BoatMath.PersonKg, wt);
            if (full.GmMm <= 0 || empty.GmMm <= 0) E("capsizes", arg: Math.Min(full.GmMm, empty.GmMm).ToString(System.Globalization.CultureInfo.InvariantCulture));
            else if (full.GmMm < wt.TenderGmMm) W("tender", arg: full.GmMm.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (full.FreeboardMm < 0) E("swamps_full", arg: full.FreeboardMm.ToString(System.Globalization.CultureInfo.InvariantCulture));
            else if (SeaHull(d, cat) && full.FreeboardMm < st.HullDepthMm * LowFreeboardPm / 1000) W("overloaded", arg: full.FreeboardMm.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (localDepthMm > 0 && full.DraftMm + wt.KeelClearanceMm > localDepthMm) W("too_deep", arg: $"{full.DraftMm + wt.KeelClearanceMm}/{localDepthMm}");
            bool moves = st.Rowers > 0 || st.Polers > 0 || st.SailM2 > 0 || st.TowPost || d.Hitch.Kind == "rope";
            if (!moves) W("no_power");
        }
        return issues;
    }

    /// <summary>P7-SH-A (ships-and-naval-war 3.1, SH-1): a sea ship fully loaded with under this share (permille) of its hull's depth left above the water is overloaded: the designer warns before it swamps.</summary>
    public const int LowFreeboardPm = 200;

    /// <summary>A design whose hull is of the sea planking (parts tagged <c>sea</c>).</summary>
    private static bool SeaHull(VehicleBlueprintDef d, VehicleCatalog cat)
    {
        foreach (var ids in d.Legend.Values)
            foreach (var id in ids)
                if (cat.PartIndex(id) is int pi && pi >= 0 && cat.PartList[pi].Tags.Contains("hull") && cat.PartList[pi].Tags.Contains("sea")) return true;
        return false;
    }

    /// <summary>The English line of an issue for logs and content errors (the game shows <c>vd_issue_{code}</c> through tr()).</summary>
    public static string Describe(DesignIssue i, VehicleBlueprintDef d, VehicleCatalog cat)
    {
        string at = i.X >= 0 ? $" at ({i.X},{i.Y}{(i.Deck > 0 ? $",deck {i.Deck}" : "")})" : "";
        return i.Code switch
        {
            "unknown_part" => $"unknown part '{i.Arg}'",
            "unknown_key" => $"row {i.Y} uses '{i.Arg}', which is not in the legend",
            "legend_key" => $"legend key '{i.Arg}' must be one character",
            "row_width" => $"row {i.Y} is {i.Arg.Split('/')[0]} wide, row 0 is {i.Arg.Split('/')[^1]}",
            "unsupported" => $"part '{i.Arg}'{at} needs one of [{string.Join(", ", cat.Parts.TryGetValue(i.Arg, out var p) ? p.NeedsBelow : [])}] below it",
            "no_frame" => $"the cell{at} has no frame under its parts",
            "hitch_max" => $"max_animals {i.Arg.Split('/')[0]} is below one team of {i.Arg.Split('/')[^1]}",
            "no_pull_point" => $"pulled by {d.Hitch.Slots} animals but has no '{i.Arg}' hitch part",
            "pushed_only" => "a vehicle with no hitch slots must be pushed",
            "no_seat" => "a driven vehicle needs a seat",
            "no_seat_passengers" => $"carries {i.Arg} passengers but has no seat",
            "unpaired_wheel" => $"{i.Arg} wheels (they come in pairs on an axle)",
            "axles_short" => $"wheels need more axles ({i.Arg})",
            "no_steering" => "nothing steers it (a pole or a pivoting axle on a four-wheeler; a rudder, a steering oar or oars on a boat)",
            "hull_gap" => $"a gap in the hull planking{at}: it leaks",
            "capsizes" => $"it capsizes (GM {i.Arg} mm)",
            "swamps_full" => $"fully loaded it swamps (freeboard {i.Arg} mm)",
            "overloaded" => $"overloaded: fully laden only {i.Arg} mm of side stands above the water",
            "tender" => $"tender (GM {i.Arg} mm): it may capsize in beam seas",
            "too_deep" => $"too deep for the local river ({i.Arg} mm)",
            "no_power" => "nothing moves it on the water",
            "empty" => "no parts",
            _ => i.Code + at,
        };
    }

    /// <summary>Whether the deck under (x, y) holds a part of <paramref name="ids"/> (any structural part when null): an upper deck stands on the one below it.</summary>
    private static bool BelowHas(VehicleBlueprintDef d, int x, int y, int deck, VehicleCatalog cat, IReadOnlyCollection<string>? ids)
    {
        IReadOnlyList<string>? grid = deck - 1 == 0 ? d.Rows : d.Levels.FirstOrDefault(l => Math.Max(1, l.Deck) == deck - 1)?.Rows;
        if (grid == null || y >= grid.Count || x >= grid[y].Length) return false;
        if (!d.Legend.TryGetValue(grid[y][x].ToString(), out var below)) return false;
        foreach (var id in below)
        {
            int pi = cat.PartIndex(id); if (pi < 0) continue;
            var p = cat.PartList[pi];
            if (ids == null ? p.Tags.Any(t => Structure.Contains(t)) : ids.Contains(id)) return true;
        }
        return false;
    }

    /// <summary>Whether every part a vehicle needs to move is fitted and whole: wheels, axles, runners, hitch, a hull's planks (an unfinished wagon or a hull on the slipway stays put).</summary>
    public static bool CanMove(IReadOnlyList<PartRow> rows, VehicleCatalog cat)
    {
        foreach (var r in rows)
        {
            if (r.State == PartState.Built) continue;
            var t = cat.PartList[r.Part].Tags;
            if (t.Contains("wheel") || t.Contains("axle") || t.Contains("runner") || t.Contains("hull") || t.Contains("hitch") || t.Contains("frame") || t.Contains("pole")) return false;
        }
        return true;
    }

    /// <summary>True when any wheel or axle is broken: the vehicle stops and no pushing frees it (vehicle-designer 4.3).</summary>
    public static bool WheelOrAxleBroken(IReadOnlyList<PartRow> rows, VehicleCatalog cat)
    {
        foreach (var r in rows) if (r.State == PartState.Broken) { var t = cat.PartList[r.Part].Tags; if (t.Contains("wheel") || t.Contains("axle")) return true; }
        return false;
    }

    /// <summary>
    /// The text of a design (vehicle-designer 4.1: the Phase 3 format, rows and a legend of part stacks, upper decks as levels) from its cells: each distinct stack gets a letter (A.., then a..), in the
    /// order the cells are read. The same cells always give the same text.
    /// </summary>
    public static VehicleBlueprintDef FromCells(string id, string name, VehicleHitchDef hitch, int driver, int passengers, bool runners, int width, int length, IReadOnlyList<(int X, int Y, int Deck, string[] Stack)> cells,
        string builtAt = "", IReadOnlyList<string>? power = null)
    {
        const string keys = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var legend = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var byStack = new Dictionary<string, char>(StringComparer.Ordinal);
        int decks = cells.Count == 0 ? 1 : cells.Max(c => c.Deck) + 1;
        var grids = new char[decks][,];
        for (int k = 0; k < decks; k++) { grids[k] = new char[length, width]; for (int y = 0; y < length; y++) for (int x = 0; x < width; x++) grids[k][y, x] = '_'; }
        foreach (var c in cells.OrderBy(c => c.Deck).ThenBy(c => c.Y).ThenBy(c => c.X))
        {
            if (c.Stack.Length == 0 || c.X < 0 || c.Y < 0 || c.X >= width || c.Y >= length) continue;
            string sig = string.Join(",", c.Stack);
            if (!byStack.TryGetValue(sig, out char ch)) { ch = keys[Math.Min(byStack.Count, keys.Length - 1)]; byStack[sig] = ch; legend[ch.ToString()] = [.. c.Stack]; }
            grids[c.Deck][c.Y, c.X] = ch;
        }
        List<string> Rows(int k) { var l = new List<string>(); for (int y = 0; y < length; y++) { var sb = new System.Text.StringBuilder(); for (int x = 0; x < width; x++) sb.Append(grids[k][y, x]); l.Add(sb.ToString()); } return l; }
        var levels = new List<VehicleLevelDef>();
        for (int k = 1; k < decks; k++) levels.Add(new VehicleLevelDef { Deck = k, Rows = Rows(k) });
        return new VehicleBlueprintDef { Id = id, Name = name, Hitch = hitch, Driver = driver, Passengers = passengers, Runners = runners, Legend = legend, Rows = Rows(0), Levels = levels, BuiltAt = builtAt, Power = [.. power ?? []] };
    }
}
