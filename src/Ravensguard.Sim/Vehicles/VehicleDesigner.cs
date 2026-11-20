using System.Globalization;
using System.Text;
using Ravensguard.Content;
using Ravensguard.Sim.Animals;

namespace Ravensguard.Sim.Carts;

/// <summary>Why a part could not be placed in the designer (vehicle-designer 4.1).</summary>
public enum PlaceResult : byte { Ok, OutOfGrid, UnknownPart, NeedsBelow, Full }

/// <summary>One row of the designer's speed table: a surface, the speed with the chosen team (cm/s) and the pull ratio.</summary>
public readonly record struct SurfaceSpeed(Surface Surface, int SpeedCmps, int RatioPm);

/// <summary>
/// The designer's stats pane (vehicle-designer 4.1): the derived numbers, the pull needed fully loaded at full speed (ratio 600) on dry road and on mud and the team that gives it, speeds per surface for
/// the chosen team, the turning radius, the axle load, and for a boat the draft empty and full, freeboard, GM, the speeds rowing, poled, towed and downstream; and the warnings.
/// </summary>
public sealed record DesignerStats
{
    public required VehicleStats Stats { get; init; }
    public required IReadOnlyList<DesignIssue> Issues { get; init; }
    public long PullRoadN { get; init; }
    public long PullMudN { get; init; }
    public int AnimalsRoad { get; init; }
    public int AnimalsMud { get; init; }
    public string Beast { get; init; } = "";
    public int TeamAnimals { get; init; }
    public IReadOnlyList<SurfaceSpeed> Speeds { get; init; } = [];
    public int AxleLoadPm { get; init; }
    public BoatLoad Empty { get; init; }
    public BoatLoad Full { get; init; }
    public int RowMmps { get; init; }
    public int PoleMmps { get; init; }
    public int TowMmps { get; init; }
    public int DownstreamMmps { get; init; }
    public int DeepestRiverWidthClass { get; init; } = -1;
    public bool CanBuild => !Issues.Any(i => i.IsError);
}

/// <summary>
/// The vehicle designer's model (vehicle-designer 4.1, the sim side of Godot's <c>UI/VehicleDesigner</c>): a top-down grid of 1 m cells with decks, each cell a stack of parts ground up. A part is placed
/// on top of a cell's stack only where what it needs below is there; the top part is removed; the design can be turned and mirrored; every edit can be undone. The stats and warnings are the same
/// <see cref="PartGrid"/> numbers a built vehicle has, so a design saved and built drives exactly as the pane said. Pure and deterministic: no engine, no clock.
/// </summary>
public sealed class VehicleDesigner
{
    public VehicleCatalog Catalog { get; }
    public AnimalCatalog? Breeds { get; }
    public int Width { get; private set; }
    public int Length { get; private set; }
    public int Deck { get; set; }
    public int Decks { get; private set; } = 1;
    public string Id { get; set; } = "player.design";
    public string Name { get; set; } = "New design";
    /// <summary>The facing the design is shown in (T turns it a quarter, 0 east .. 3 south): a view of the same grid, not part of the design.</summary>
    public int Facing { get; private set; }
    /// <summary>The team the speed table uses (0: one team of the reference beast).</summary>
    public int TeamAnimals { get; set; }
    /// <summary>The deepest river near the design's workshop, mm (0: unknown): a full-load draft deeper than it is a warning.</summary>
    public int LocalDepthMm { get; set; }
    private readonly Dictionary<(int X, int Y, int D), List<string>> _cells = [];
    private readonly Stack<string> _undo = new(), _redo = new();
    private DesignerStats? _stats;
    public int Revision { get; private set; }

    public VehicleDesigner(VehicleCatalog cat, AnimalCatalog? breeds = null, int width = 2, int length = 5) { Catalog = cat; Breeds = breeds; Width = width; Length = length; }

    public IReadOnlyList<string> Stack(int x, int y, int deck = -1) => _cells.TryGetValue((x, y, deck < 0 ? Deck : deck), out var s) ? s : [];

    /// <summary>The palette (vehicle-designer 4.1): parts by category in the content's order of categories, each in id order.</summary>
    public static readonly string[] Categories = ["frame", "wheels", "hitch", "cargo", "seats", "hull", "steering", "sail", "cover", "workstations"];
    public IReadOnlyList<VehiclePartDef> PartsOf(string category) => [.. Catalog.PartList.Where(p => p.Category == category)];

    // ------------------------------------------------------------------ editing

    private void Push() { _undo.Push(Snapshot()); _redo.Clear(); _stats = null; Revision++; }

    /// <summary>LMB: puts a part on top of a cell's stack, where what it needs below it is there (in the cell, or for an upper deck in the cell below).</summary>
    public PlaceResult Place(int x, int y, string part)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Length) return PlaceResult.OutOfGrid;
        int pi = Catalog.PartIndex(part); if (pi < 0) return PlaceResult.UnknownPart;
        var p = Catalog.PartList[pi];
        var stack = Stack(x, y);
        if (stack.Count >= 12) return PlaceResult.Full;
        if (p.NeedsBelow.Count > 0 && !stack.Any(s => p.NeedsBelow.Contains(s)) && !(Deck > 0 && Stack(x, y, Deck - 1).Any(s => p.NeedsBelow.Contains(s)))) return PlaceResult.NeedsBelow;
        Push();
        if (!_cells.TryGetValue((x, y, Deck), out var l)) _cells[(x, y, Deck)] = l = [];
        l.Add(part);
        return PlaceResult.Ok;
    }

    /// <summary>RMB: removes the top part of a cell's stack.</summary>
    public bool RemoveTop(int x, int y)
    {
        if (!_cells.TryGetValue((x, y, Deck), out var l) || l.Count == 0) return false;
        Push(); l.RemoveAt(l.Count - 1); if (l.Count == 0) _cells.Remove((x, y, Deck));
        return true;
    }

    /// <summary>Sets a whole stack at once (the designer's eyedropper, tests): every part must be placeable in order.</summary>
    public PlaceResult PlaceStack(int x, int y, IEnumerable<string> parts)
    {
        foreach (var p in parts) { var r = Place(x, y, p); if (r != PlaceResult.Ok) return r; }
        return PlaceResult.Ok;
    }

    /// <summary>T: turns the shown facing a quarter.</summary>
    public void Rotate() => Facing = (Facing + 1) & 3;

    /// <summary>M: mirrors the design across (columns swapped on every deck).</summary>
    public void Mirror()
    {
        Push();
        var all = _cells.ToList(); _cells.Clear();
        foreach (var (k, v) in all) _cells[(Width - 1 - k.X, k.Y, k.D)] = v;
    }

    /// <summary>Resizes the grid (cells outside it are dropped).</summary>
    public void Resize(int width, int length)
    {
        Push();
        Width = Math.Clamp(width, 1, 6); Length = Math.Clamp(length, 1, 16);
        foreach (var k in _cells.Keys.Where(k => k.X >= Width || k.Y >= Length).ToList()) _cells.Remove(k);
    }

    public void SetDecks(int n) { Push(); Decks = Math.Clamp(n, 1, 3); Deck = Math.Min(Deck, Decks - 1); foreach (var k in _cells.Keys.Where(k => k.D >= Decks).ToList()) _cells.Remove(k); }
    public void DeckUp() { if (Deck + 1 >= Decks) SetDecks(Decks + 1); Deck = Math.Min(Decks - 1, Deck + 1); }
    public void DeckDown() => Deck = Math.Max(0, Deck - 1);

    public void New(int width = 2, int length = 5) { Push(); _cells.Clear(); Width = width; Length = length; Decks = 1; Deck = 0; Name = "New design"; }

    public bool Undo() { if (_undo.Count == 0) return false; _redo.Push(Snapshot()); Restore(_undo.Pop()); return true; }
    public bool Redo() { if (_redo.Count == 0) return false; _undo.Push(Snapshot()); Restore(_redo.Pop()); return true; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    private string Snapshot()
    {
        var sb = new StringBuilder();
        sb.Append(Width).Append(';').Append(Length).Append(';').Append(Decks).Append('|');
        foreach (var (k, v) in _cells.OrderBy(k => k.Key.D).ThenBy(k => k.Key.Y).ThenBy(k => k.Key.X)) sb.Append(k.X).Append(',').Append(k.Y).Append(',').Append(k.D).Append(':').Append(string.Join('+', v)).Append('|');
        return sb.ToString();
    }

    private void Restore(string snap)
    {
        _cells.Clear(); _stats = null; Revision++;
        var parts = snap.Split('|');
        var head = parts[0].Split(';'); Width = int.Parse(head[0], CultureInfo.InvariantCulture); Length = int.Parse(head[1], CultureInfo.InvariantCulture); Decks = int.Parse(head[2], CultureInfo.InvariantCulture);
        Deck = Math.Min(Deck, Decks - 1);
        foreach (var c in parts.Skip(1))
        {
            if (c.Length == 0) continue;
            int colon = c.IndexOf(':'); var xyz = c[..colon].Split(',');
            _cells[(int.Parse(xyz[0], CultureInfo.InvariantCulture), int.Parse(xyz[1], CultureInfo.InvariantCulture), int.Parse(xyz[2], CultureInfo.InvariantCulture))] = [.. c[(colon + 1)..].Split('+')];
        }
    }

    // ------------------------------------------------------------------ the design as data

    /// <summary>The hitch the parts make: shafts (one horse, two in tandem), a pole and yoke (a pair a team, two pairs an axle), traces, a tow post on a boat, a ferry's rope, else pushed or rowed.</summary>
    public VehicleHitchDef Hitch()
    {
        var all = _cells.Where(k => k.Key.D == 0).SelectMany(k => k.Value).Distinct().Select(id => Catalog.Parts.TryGetValue(id, out var p) ? p : null).Where(p => p != null).ToList();
        bool Has(string tag) => all.Any(p => p!.Tags.Contains(tag));
        int axles = _cells.Where(k => k.Key.D == 0).SelectMany(k => k.Value).Count(id => Catalog.Parts.TryGetValue(id, out var p) && p.Tags.Contains("axle"));
        bool boat = Has("hull");
        if (Has("shafts")) return new VehicleHitchDef { Slots = 1, MaxAnimals = 2, Kind = "shafts" };
        if (Has("pole_yoke")) return new VehicleHitchDef { Slots = 2, MaxAnimals = Math.Max(2, 4 * Math.Max(1, axles)), Kind = "pole_yoke" };
        if (Has("traces")) return new VehicleHitchDef { Slots = 1, MaxAnimals = 2, Kind = "traces" };
        if (boat && Has("tow")) return new VehicleHitchDef { Slots = 1, MaxAnimals = 2, Kind = "tow" };
        if (boat) return new VehicleHitchDef { Slots = 0, Kind = Has("ramp") ? "rope" : "rowed" };
        return new VehicleHitchDef { Slots = 0, Kind = "pushed" };
    }

    /// <summary>The design as a blueprint (the Phase 3 text format: rows, a legend of stacks, upper decks as levels).</summary>
    public VehicleBlueprintDef ToDef()
    {
        var hitch = Hitch();
        var cells = _cells.Where(k => k.Value.Count > 0).Select(k => (k.Key.X, k.Key.Y, k.Key.D, k.Value.ToArray())).ToList();
        int seats = cells.Where(c => c.Item3 == 0).Sum(c => c.Item4.Count(id => Catalog.Parts.TryGetValue(id, out var p) && p.Tags.Contains("seat") && p.Rowers == 0));
        bool boat = cells.Any(c => c.Item4.Any(id => Catalog.Parts.TryGetValue(id, out var p) && p.Tags.Contains("hull")));
        bool runners = !boat && cells.Any(c => c.Item4.Contains("runner"));
        int driver = hitch.Slots > 0 || (boat && hitch.Kind is "rope" or "tow") ? 1 : hitch.Kind == "pushed" ? 1 : 0;
        int passengers = Math.Max(0, seats - (driver > 0 && hitch.Kind is "shafts" or "pole_yoke" or "traces" ? 1 : 0));
        string builtAt = boat ? "slipway" : Width * Length <= 6 ? "" : "yard";
        var power = new List<string>();
        if (boat)
        {
            var parts = cells.SelectMany(c => c.Item4).Select(id => Catalog.Parts.GetValueOrDefault(id)).Where(p => p != null).ToList();
            if (parts.Any(p => p!.Rowers > 0)) power.Add("rowed");
            if (parts.Any(p => p!.Polers > 0)) power.Add("poled");
            if (parts.Any(p => p!.Tags.Contains("tow"))) power.Add("towed");
            if (parts.Any(p => p!.SailM2 > 0)) power.Add("sail");
            if (hitch.Kind == "rope") power.Add("rope");
        }
        return PartGrid.FromCells(Id, Name, hitch, driver, passengers, runners, Width, Length, cells, builtAt, power);
    }

    /// <summary>Loads a design into the grid (Ctrl+O, a template to start from); the undo history starts afresh.</summary>
    public void Load(VehicleBlueprintDef d)
    {
        _cells.Clear(); _undo.Clear(); _redo.Clear(); _stats = null; Revision++;
        Id = d.Id; Name = d.Name; Width = d.Rows.Count > 0 ? d.Rows[0].Length : 1; Length = d.Rows.Count; Decks = 1 + d.Levels.Count; Deck = 0;
        void Grid(IReadOnlyList<string> rows, int deck)
        {
            for (int y = 0; y < rows.Count; y++)
                for (int x = 0; x < rows[y].Length; x++)
                    if (d.Legend.TryGetValue(rows[y][x].ToString(), out var ids) && ids.Count > 0) _cells[(x, y, deck)] = [.. ids];
        }
        Grid(d.Rows, 0);
        foreach (var l in d.Levels) Grid(l.Rows, Math.Max(1, l.Deck));
    }

    // ------------------------------------------------------------------ the stats pane

    /// <summary>The numbers and warnings of the design as it stands (recomputed once per edit; vehicle-designer 9: at most 2 ms).</summary>
    public DesignerStats Stats()
    {
        if (_stats != null) return _stats;
        var d = ToDef();
        var issues = _cells.Count == 0 ? [new DesignIssue("empty", IssueLevel.Error)] : PartGrid.Validate(d, Catalog, LocalDepthMm, Catalog.Water);
        var st = PartGrid.Derive(d, PartGrid.Expand(d, Catalog), Catalog, builtOnly: false);
        var tu = Catalog.Tuning; var wt = Catalog.Water;
        int people = (st.Driver + st.Passengers) * BoatMath.PersonKg;
        int full = st.MassKg + st.CargoKg + people;
        long fRoad = VehicleMath.FReqMilliN(full, tu.Crr(Surface.DryRoad, st.Runners)), fMud = VehicleMath.FReqMilliN(full, tu.Crr(Surface.MudRoad, st.Runners));
        var beast = Breeds?.Find(st.HitchKind == "pole_yoke" ? "ox" : "horse_draught");
        int pull = beast?.PullN ?? 0, gait = beast?.WalkCmps ?? 0;
        int slots = Math.Max(1, st.HitchSlots), team = TeamAnimals > 0 ? Math.Min(TeamAnimals, st.MaxAnimals) : st.HitchSlots;
        long teamPull = 0; for (int i = 0; i < team; i++) teamPull += (long)pull * VehicleMath.TeamEfficiencyPm(i, slots) / 1000;
        if (beast != null) gait = st.HitchKind == "pole_yoke" ? beast.WalkCmps : Math.Max(beast.WalkCmps, beast.TrotCmps);
        if (st.HitchSlots == 0 && !st.Boat) { teamPull = (long)Math.Max(1, st.Driver) * 300; gait = 150; }   // pushed: a person's 300 N at a walk
        var speeds = new List<SurfaceSpeed>();
        if (!st.Boat)
            foreach (var s in new[] { Surface.DryRoad, Surface.WetRoad, Surface.Track, Surface.Grass, Surface.MudRoad, Surface.DeepMud })
            {
                var e = VehicleMath.OnSurface(tu, st, st.CargoKg, people, s, 0, teamPull, gait);
                speeds.Add(new SurfaceSpeed(s, e.SpeedCmps, e.RatioPm));
            }
        var res = new DesignerStats
        {
            Stats = st, Issues = issues, Beast = beast?.Id ?? "", TeamAnimals = team, Speeds = speeds,
            PullRoadN = (fRoad * 1000 / 600 + 999) / 1000, PullMudN = (fMud * 1000 / Stuck.SafeRatioPm + 999) / 1000,
            AnimalsRoad = st.HitchSlots > 0 && pull > 0 ? VehicleMath.AnimalsNeeded(pull, slots, st.MaxAnimals, fRoad, 600) : 0,
            // in mud no team reaches full speed: the pane counts the team that keeps clear of bogging (the cart bar's ratio of 700, Stuck.SafeRatioPm)
            AnimalsMud = st.HitchSlots > 0 && pull > 0 ? VehicleMath.AnimalsNeeded(pull, slots, st.MaxAnimals, fMud, Stuck.SafeRatioPm) : 0,
            AxleLoadPm = st.AxleLimitKg > 0 ? (int)((long)(st.MassKg + st.CargoKg) * 1000 / st.AxleLimitKg) : 0,
        };
        if (st.Boat && st.WaterplaneMm2 > 0)
        {
            int crewKg = (st.Driver + st.Rowers) * BoatMath.PersonKg;
            var empty = BoatMath.Load(st, 0, crewKg, wt); var loaded = BoatMath.Load(st, st.CargoKg, crewKg + st.Passengers * BoatMath.PersonKg, wt);
            long wet = BoatMath.WetMm2(st.WaterplaneMm2, st.PerimeterM, loaded.DraftMm);
            int row = BoatMath.WaterSpeedMmps(BoatMath.PowerN(wt, st.Rowers, 0, 0, 0, 0), wt.DragK, wet);
            int pole = BoatMath.WaterSpeedMmps(BoatMath.PowerN(wt, 0, st.Polers, 1000, 0, 0), wt.DragK, wet);
            int tow = st.TowPost && Breeds?.Find("horse_draught") is { } h ? BoatMath.WaterSpeedMmps(h.PullN, wt.DragK, wet) : 0;
            int best = Math.Max(row, Math.Max(pole, tow));
            int deepest = -1;
            for (int c = 0; c < wt.Classes.Length; c++) if (BoatMath.Floats(loaded.DraftMm, wt.Classes[c].DepthMm, wt)) { deepest = c; break; }
            res = res with { Empty = empty, Full = loaded, RowMmps = row, PoleMmps = pole, TowMmps = tow, DownstreamMmps = BoatMath.GroundMmps(best, wt.Classes[Math.Clamp(deepest < 0 ? 2 : deepest, 0, wt.Classes.Length - 1)].CurrentMmps, true), DeepestRiverWidthClass = deepest };
        }
        return _stats = res;
    }

    // ------------------------------------------------------------------ the text

    /// <summary>The design's JSONC text, as saved to the save's designs and <c>user://blueprints/player/vehicle.&lt;id&gt;.jsonc</c>: one <c>vehicle_blueprint</c> entry. The same design gives the same text.</summary>
    public string Text() => WriteText(ToDef());

    public static string WriteText(VehicleBlueprintDef d)
    {
        static string Q(string s) => System.Text.Json.JsonSerializer.Serialize(s);
        static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.Append("// A vehicle design (docs/specs/phase5/vehicle-designer.md 4.1): rows from the rear (first) to the front, one character a 1 m cell, the legend a stack of parts ground up; levels are upper decks.\n[\n");
        sb.Append("  { \"type\": \"vehicle_blueprint\", \"id\": ").Append(Q(d.Id)).Append(", \"name\": ").Append(Q(d.Name));
        sb.Append(", \"hitch\": { \"slots\": ").Append(I(d.Hitch.Slots)).Append(", \"max_animals\": ").Append(I(d.Hitch.MaxAnimals)).Append(", \"kind\": ").Append(Q(d.Hitch.Kind)).Append(" }");
        sb.Append(", \"driver\": ").Append(I(d.Driver)).Append(", \"passengers\": ").Append(I(d.Passengers));
        if (d.Runners) sb.Append(", \"runners\": true");
        if (d.BuiltAt.Length > 0) sb.Append(", \"built_at\": ").Append(Q(d.BuiltAt));
        if (d.Power.Count > 0) sb.Append(", \"power\": [").Append(string.Join(", ", d.Power.Select(Q))).Append(']');
        sb.Append(",\n    \"legend\": { ");
        sb.Append(string.Join(", ", d.Legend.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => Q(k.Key) + ": [" + string.Join(", ", k.Value.Select(Q)) + "]")));
        sb.Append(" },\n    \"rows\": [ ").Append(string.Join(", ", d.Rows.Select(Q))).Append(" ]");
        if (d.Levels.Count > 0)
            sb.Append(",\n    \"levels\": [ ").Append(string.Join(", ", d.Levels.Select(l => "{ \"deck\": " + I(l.Deck) + ", \"rows\": [ " + string.Join(", ", l.Rows.Select(Q)) + " ] }"))).Append(" ]");
        sb.Append(" }\n]\n");
        return sb.ToString();
    }

    /// <summary>A design id from a name: <c>player.</c> and the name in lower case, letters and digits joined by underscores (<c>My ox wagon</c> is <c>player.my_ox_wagon</c>).</summary>
    public static string IdFor(string name)
    {
        var sb = new StringBuilder("player.");
        bool us = false;
        foreach (char c in name.ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') { sb.Append(c); us = false; }
            else if (!us && sb.Length > 7) { sb.Append('_'); us = true; }
        }
        string s = sb.ToString().TrimEnd('_');
        return s.Length > 7 ? s : "player.design";
    }
}
