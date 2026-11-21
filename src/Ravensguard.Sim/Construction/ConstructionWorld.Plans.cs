using System.Text;
using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Land;

namespace Ravensguard.Sim.Construction;

/// <summary>What stands on a cell besides pieces: nothing, something building clears away (a tree, a bush, rocks, a crate), or something it may not (a well, a grave, a site's marker).</summary>
public enum PropHere : byte { None, Clears, Blocks }

/// <summary>Build mode's preview of a placement (spec 4.1: refusal reasons shown before placing): the cells refused with their reasons, how many would be placed, the cells whose trees or props
/// building clears, and for a plan the lot under it and how it fits.</summary>
public sealed record BuildPreview(IReadOnlyList<PlaceRefusal> Refused, int Ok, IReadOnlyList<(int X, int Y, int Z)> Clears, FitLot? Lot, PlanFit? Fit)
{
    public static readonly BuildPreview Empty = new([], 0, [], null, null);
}

/// <summary>A blueprint saved from the world (spec 4.8): its id, the jsonc text, and the validator's warnings (shown, never blocking).</summary>
public sealed record SavedDesign(string Id, string Name, string Text, IReadOnlyList<BlueprintIssue> Warnings, int Width, int Height, IReadOnlyList<int> Levels);

// P5-C: build mode's side of the construction world (build-mode-and-construction 4.1, 4.2, 4.8): the preview, plans at a culture and wealth fitted to the lot, undo, the brushes that remove and
// take down, Roof over, the stair pair, the rooms a site will make, and save as blueprint. Every change here is a desk command at a barrier, as the rest of construction.
public sealed partial class ConstructionWorld
{
    /// <summary>The trees and props on a cell (the chunk store's objects); null: nothing stands anywhere (tests).</summary>
    public Func<int, int, int, PropHere>? PropAt { get; set; }
    /// <summary>The lot under a point (region metres) with its street side, for the plan fitter; null: no lots (a test world).</summary>
    public Func<int, int, FitLot?>? LotAt { get; set; }
    /// <summary>The save's own designs (the "designs" section): the plan library reads them, save as blueprint writes them.</summary>
    public DesignBook? Designs { get; set; }

    /// <summary>The plan library with this save's designs (the content's part is cached).</summary>
    public PlanLibrary Plans(IEnumerable<(string Id, string Text)>? extra = null) =>
        Content != null ? PlanLibrary.For(Content, Designs, extra) : new PlanLibrary(Costs.Library, [], (Designs?.Designs.Select(kv => (kv.Key, kv.Value.Text)) ?? []).Concat(extra ?? []));

    /// <summary>P7-LB1 (large-buildings 4.6): the water check of a powered plan (set by the power wiring: the region's water and the content's wheels); null: no check.</summary>
    public Func<BuildingGrid, int, int, int, (bool Ok, int HaveW, string Key)>? PowerCheck { get; set; }

    /// <summary>P7-LB1: the power rules over this world's built pieces (set by <see cref="Ravensguard.Sim.Building.PowerWiring"/>); null in a world without them.</summary>
    public Ravensguard.Sim.Building.PowerWorld? Power { get; set; }

    /// <summary>The plan's water check for a need in watts (null when the plan needs no power or no check is wired).</summary>
    public PlanPower? PlanPowerOf(int powerW) => powerW > 0 && PowerCheck is { } c ? new PlanPower(powerW, (g, x, y) => c(g, x, y, powerW)) : null;

    /// <summary>Whether a ground cell takes a plan (the fitter's ground rule): not a road, not steeper than the floor limit, no generated building standing there.</summary>
    public bool GroundTakes(int x, int y) => (Ground?.Invoke(x, y) is not { } g || (!g.Road && g.SlopePm <= Rules.MaxSlopePm)) && Standing?.Invoke(x, y, 0) != true;

    // ------------------------------------------------------------------ the preview (nothing changes)

    /// <summary>What placing these cells would do, without doing it: every refusal with its reason, the count placed, and the cells whose trees or props building would clear.</summary>
    public BuildPreview Preview(EntityId owner, PurseKind ownerKind, int household, IReadOnlyList<PlaceCell> cells, bool allOrNothing = false, FitLot? lot = null, PlanFit? fit = null)
    {
        if (cells.Count == 0) return BuildPreview.Empty with { Lot = lot, Fit = fit };
        _planBeams = true;
        PlaceResult r;
        try { r = PlaceCore(owner, ownerKind, household, cells, 0, default, allOrNothing, "", null, default, PurseKind.Person, dryRun: true); }
        finally { _planBeams = false; }
        var clears = new List<(int, int, int)>();
        if (PropAt != null)
            foreach (var c in cells)
                if (c.Z == 0 && PropAt(c.X, c.Y, 0) == PropHere.Clears) clears.Add((c.X, c.Y, 0));
        return new BuildPreview(r.Refused, cells.Count - r.Refused.Count, clears, lot, fit);
    }

    /// <summary>The cells of a plan grid placed with its corner at (ox, oy) (spec 4.1: one command commits every cell).</summary>
    public List<PlaceCell> PlanPlaceCells(BuildingGrid g, int ox, int oy) => [.. PlanLibrary.Cells(g, Costs).Select(c => new PlaceCell(ox + c.X, oy + c.Y, c.Z, c.Piece))];

    /// <summary>A plan from the library fitted to the lot under (x, y): the best of the eight transforms (spec 4.2), or the plan at the given transform (the player turned or mirrored it).</summary>
    public (PlanFit Fit, FitLot? Lot) FitPlan(BuildingGrid g, int x, int y, int index = -1, int powerW = 0)
    {
        var lot = LotAt?.Invoke(x, y);
        if (lot == null) return (PlanFit.No("cn_fit_no_lot"), null);
        var power = PlanPowerOf(powerW);
        var fit = index < 0 ? PlanFitter.Fit(g, lot.Value, Costs.Library.Pieces, GroundTakes, power) : PlanFitter.FitAt(g, index, lot.Value, Costs.Library.Pieces, GroundTakes, power);
        return (fit, lot);
    }

    /// <summary>A plan grid (already resolved at its culture and wealth, and turned) placed as ghosts in one command, all or nothing (P5-B's <see cref="PlacePlan"/> for any grid).</summary>
    public PlaceResult PlacePlanGrid(EntityId owner, PurseKind ownerKind, int household, BuildingGrid g, string blueprint, int ox, int oy, long nowSec, (int X, int Y)? pile = null)
        => Place(owner, ownerKind, household, PlanPlaceCells(g, ox, oy), nowSec, default, allOrNothing: true, blueprint, pile);

    // ------------------------------------------------------------------ undo, the remove and take-down brushes

    /// <summary>
    /// Ctrl+Z (spec 6): the owner's latest placement (the ghosts of his sites placed at the latest game second) is taken off the plan, as long as none of them was started. Read from the
    /// saved ghosts alone, so it does the same after a load.
    /// </summary>
    public ConstructionResult UndoLast(EntityId owner, int household, long nowSec)
    {
        var mine = Book.Ghosts.Values.Where(g => g.Live && g.Work == GhostWork.Build && Site(g.Site) is { } s && s.Owner == owner).ToList();
        if (mine.Count == 0) return ConstructionResult.No("cn_nothing_to_undo");
        long last = mine.Max(g => g.PlacedSec);
        var batch = mine.Where(g => g.PlacedSec == last).ToList();
        if (batch.Any(g => g.WorkDone > 0 || g.Materialised)) return ConstructionResult.No("cn_undo_started");
        var sites = new SortedSet<EntityId>();
        foreach (var g in batch) { sites.Add(g.Site); CancelGhost(g); }
        foreach (var id in sites) Refresh(Site(id)!);
        Publish(nowSec);
        return ConstructionResult.Yes("cn_undone", batch.Count);
    }

    /// <summary>Right-drag (spec 4.1): every ghost of the person's sites in the rectangle on a level is taken off the plan (both layers; one in work gives its materials back).</summary>
    public ConstructionResult CancelRect(EntityId by, int household, int x0, int y0, int x1, int y1, int z, long nowSec)
    {
        int ax = Math.Min(x0, x1), bx = Math.Max(x0, x1), ay = Math.Min(y0, y1), by2 = Math.Max(y0, y1);
        if ((long)(bx - ax + 1) * (by2 - ay + 1) > 4096) return ConstructionResult.No("cn_brush_too_big");
        int n = 0; var sites = new SortedSet<EntityId>();
        foreach (var g in Book.Ghosts.Values.Where(g => g.Live && g.Z == z && g.X >= ax && g.X <= bx && g.Y >= ay && g.Y <= by2).ToList())
        {
            var s = Site(g.Site)!;
            if (s.Owner != by && May != null && !May(by, household, LandAction.Build, g.X, g.Y).Allowed) continue;
            CancelGhost(g); sites.Add(s.Id); n++;
        }
        if (n == 0) return ConstructionResult.No("cn_no_ghost");
        foreach (var id in sites) Refresh(Site(id)!);
        Publish(nowSec);
        return ConstructionResult.Yes("cn_ghosts_removed", n);
    }

    /// <summary>The Deconstruct brush (Delete, spec 6): every built piece in the rectangle on a level is marked to be taken down (what stands on a floor first; the land rule decides).</summary>
    public ConstructionResult DeconstructRect(EntityId by, PurseKind byKind, int household, int x0, int y0, int x1, int y1, int z, long nowSec)
    {
        int ax = Math.Min(x0, x1), bx = Math.Max(x0, x1), ay = Math.Min(y0, y1), by2 = Math.Max(y0, y1);
        if ((long)(bx - ax + 1) * (by2 - ay + 1) > 4096) return ConstructionResult.No("cn_brush_too_big");
        int n = 0; bool falls = false; ConstructionResult? last = null;
        foreach (var b in Book.Built.Values.Where(b => b.Z == z && b.X >= ax && b.X <= bx && b.Y >= ay && b.Y <= by2).OrderBy(b => b.Slot == GhostSlot.Fixture ? 0 : 1).ThenBy(b => b.SlotKey).ToList())
        {
            if (_ghostAt.ContainsKey(b.SlotKey)) continue;
            if (b.Slot == GhostSlot.Structure && BuiltAt(b.X, b.Y, b.Z, GhostSlot.Fixture) is { } f && !_ghostAt.ContainsKey(f.SlotKey)) continue;
            last = MarkDeconstruct(by, byKind, household, b.X, b.Y, b.Z, b.Slot, nowSec);
            if (last.Ok) { n++; falls |= last.Key == "cn_deconstruct_will_fall"; }
        }
        if (n == 0) return last ?? ConstructionResult.No("cn_nothing_built_here");
        return ConstructionResult.Yes(falls ? "cn_decon_many_will_fall" : "cn_decon_many", n);
    }

    // ------------------------------------------------------------------ Roof over, the stair pair

    /// <summary>
    /// Roof over (R, spec 4.1): roof ghosts over the enclosed room at (x, y, z) and the walls around it, as it will be when every planned piece is built, on every cell with nothing above.
    /// A cell too far from a wall to hold a roof is refused like any other.
    /// </summary>
    public PlaceResult RoofOver(EntityId owner, PurseKind ownerKind, int household, int x, int y, int z, string roofPiece, long nowSec, bool dryRun = false)
    {
        if (Costs.Get(roofPiece) is not { Kind: PieceKind.Roof }) return PlaceResult.No("cn_unknown_piece", roofPiece);
        if (z + 1 > Rules.MaxLevel) return PlaceResult.No("cn_too_high", Rules.MaxLevel);
        var rw = PlannedWorld(out _);
        RoomV2? room = rw.RoomAt(x, y, z);
        for (int d = 1; room == null && d <= 1; d++)
            foreach (var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) }) { room ??= rw.RoomAt(x + dx, y + dy, z); }
        if (room == null) return PlaceResult.No("cn_roof_no_room");
        if (!room.Enclosed) return PlaceResult.No("cn_roof_not_enclosed");
        var cover = new SortedSet<long>();
        foreach (var (cx, cy) in room.Cells)
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int px = cx + dx, py = cy + dy;
                    if ((dx != 0 || dy != 0) && rw.PieceAt(px, py, z) is not { } p) continue;
                    if (rw.PieceAt(px, py, z + 1) != null) continue;
                    cover.Add(ConstructionIds.CellKey(px, py, z + 1));
                }
        var cells = cover.Select(k => { var (cx, cy, cz) = ConstructionIds.Unpack(k); return new PlaceCell(cx, cy, cz, roofPiece); }).ToList();
        if (cells.Count == 0) return PlaceResult.No("cn_roof_done");
        if (dryRun) { var pv = Preview(owner, ownerKind, household, cells); return new PlaceResult(pv.Ok > 0, default, [], pv.Refused, "cn_placed", [pv.Ok, pv.Refused.Count]); }
        return Place(owner, ownerKind, household, cells, nowSec);
    }

    /// <summary>The Stairs tool (spec 4.1): the up stair at z and its down stair at z + 1, placed together.</summary>
    public PlaceResult PlaceStairs(EntityId owner, PurseKind ownerKind, int household, int x, int y, int z, byte rot, long nowSec) =>
        Place(owner, ownerKind, household, [new PlaceCell(x, y, z, "stairs_up", rot), new PlaceCell(x, y, z + 1, "stairs_down", rot)], nowSec);

    // ------------------------------------------------------------------ the rooms a site will make (the room panel)

    /// <summary>A room world of every built piece and every planned (build) ghost: what stands when the plans are finished.</summary>
    private RoomWorld PlannedWorld(out Dictionary<long, EntityId> ghostCells)
    {
        var rw = Costs.Library.NewWorld();
        var edits = new List<CellEdit>();
        ghostCells = [];
        foreach (var b in Book.Built.Values) edits.Add(new CellEdit(b.X, b.Y, b.Z, b.Slot == GhostSlot.Fixture ? CellLayer.Fixture : CellLayer.Structure, b.Piece));
        foreach (var g in Book.Ghosts.Values)
        {
            if (!g.Live) continue;
            if (g.Work == GhostWork.Deconstruct) { edits.Add(new CellEdit(g.X, g.Y, g.Z, g.Slot == GhostSlot.Fixture ? CellLayer.Fixture : CellLayer.Structure, null)); continue; }
            edits.Add(new CellEdit(g.X, g.Y, g.Z, g.Slot == GhostSlot.Fixture ? CellLayer.Fixture : CellLayer.Structure, g.Piece));
            ghostCells[RoomWorld.Key(g.X, g.Y, g.Z)] = g.Site;
        }
        if (edits.Count > 0) rw.Apply(edits);
        return rw;
    }

    /// <summary>The rooms the site's building will have once every piece of it is built (RoomDetector v2 over the built and planned cells), for build mode's room panel.</summary>
    public List<PlannedRoom> PlannedRooms(EntityId siteId)
    {
        var rw = PlannedWorld(out var ghostCells);
        var mine = new HashSet<long>(ghostCells.Where(kv => kv.Value == siteId).Select(kv => kv.Key));
        foreach (var b in Book.Built.Values) if (b.Site == siteId) mine.Add(RoomWorld.Key(b.X, b.Y, b.Z));
        // a room counts for the site when any of its floor cells, or a cell beside it, is the site's
        var near = new HashSet<long>();
        foreach (var k in mine)
        {
            var (x, y, z) = RoomWorld.Unpack(k);
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) near.Add(RoomWorld.Key(x + dx, y + dy, z));
        }
        return PlanLibrary.Rooms(rw, near);
    }

    // ------------------------------------------------------------------ save as blueprint (spec 4.8)

    /// <summary>
    /// Save as blueprint (Ctrl+S, spec 4.8): the built pieces and planned ghosts in a rectangle (every level) written as a blueprint by <see cref="BlueprintWriter"/>: rows, legend, levels,
    /// fixtures, its roof as drawn, rooms from RoomDetector v2's primary purposes, wealth from the walls' material by the culture styles, <c>cultures: ["any"]</c>, id <c>player.&lt;name&gt;</c>.
    /// The text is parsed back (it must) and <see cref="BlueprintValidator"/>'s findings come back as warnings. Null when nothing stands in the rectangle.
    /// </summary>
    public SavedDesign? Capture(int x0, int y0, int x1, int y1, string name, IReadOnlyCollection<string>? takenIds = null)
    {
        int ax = Math.Min(x0, x1), bx = Math.Max(x0, x1), ay = Math.Min(y0, y1), by = Math.Max(y0, y1);
        if ((long)(bx - ax + 1) * (by - ay + 1) > 4096) return null;
        var cells = new SortedDictionary<long, (string? S, string? F)>();
        void Put(int x, int y, int z, GhostSlot slot, string piece)
        {
            if (x < ax || x > bx || y < ay || y > by || z < 0) return;
            long k = ConstructionIds.CellKey(x, y, z);
            var cur = cells.GetValueOrDefault(k);
            cells[k] = slot == GhostSlot.Fixture ? (cur.S, piece) : (piece, cur.F);
        }
        foreach (var b in Book.Built.Values) Put(b.X, b.Y, b.Z, b.Slot, b.Piece);
        foreach (var g in Book.Ghosts.Values) if (g.Live && g.Work == GhostWork.Build) Put(g.X, g.Y, g.Z, g.Slot, g.Piece);
        foreach (var g in Book.Ghosts.Values) if (g.Live && g.Work == GhostWork.Deconstruct) { long k = ConstructionIds.CellKey(g.X, g.Y, g.Z); if (cells.TryGetValue(k, out var c)) cells[k] = g.Slot == GhostSlot.Fixture ? (c.S, null) : (null, c.F); }
        foreach (var k in cells.Where(kv => kv.Value.S == null && kv.Value.F == null).Select(kv => kv.Key).ToList()) cells.Remove(k);
        if (cells.Count == 0) return null;
        // the footprint: the cells' bounds
        var keys = cells.Keys.Select(ConstructionIds.Unpack).ToList();
        int mx = keys.Min(c => c.X), my = keys.Min(c => c.Y), w = keys.Max(c => c.X) - mx + 1, h = keys.Max(c => c.Y) - my + 1;
        var levels = new SortedDictionary<int, BuildingLevel>();
        foreach (var (k, (s, f)) in cells)
        {
            var (x, y, z) = ConstructionIds.Unpack(k);
            if (!levels.TryGetValue(z, out var lv)) levels[z] = lv = new BuildingLevel(z, w, h);
            int i = lv.Idx(x - mx, y - my);
            lv.Structure[i] = s; lv.Fixture[i] = s != null ? f : null;      // a fixture needs its floor
        }
        if (!levels.ContainsKey(0)) levels[0] = new BuildingLevel(0, w, h);
        // rooms: the primary purpose of each room RoomDetector v2 finds in the plan as drawn, a letter per purpose on the room's floor cells
        var pieces = Costs.Library.Pieces;
        var rw = Costs.Library.NewWorld();
        var edits = new List<CellEdit>();
        foreach (var (z, lv) in levels)
            for (int i = 0; i < lv.Structure.Length; i++)
            {
                if (lv.Structure[i] is { } s) edits.Add(new CellEdit(i % w, i / w, z, CellLayer.Structure, s));
                if (lv.Fixture[i] is { } f) edits.Add(new CellEdit(i % w, i / w, z, CellLayer.Fixture, f));
            }
        rw.Apply(edits);
        var rooms = new Dictionary<char, string>();
        var letterOf = new Dictionary<string, char>(StringComparer.Ordinal);
        const string Letters = "abcdefghijklmnopqrstuvwxyz";
        foreach (var r in rw.Rooms)
        {
            if (r.Primary is not { } t || t.Purpose.Length == 0) continue;
            if (!letterOf.TryGetValue(t.Purpose, out char letter))
            {
                if (letterOf.Count >= Letters.Length) continue;
                letter = Letters[letterOf.Count]; letterOf[t.Purpose] = letter; rooms[letter] = t.Purpose;
            }
            var lv = levels[r.Z];
            foreach (var (cx, cy) in r.Cells)
            {
                int i = lv.Idx(cx, cy);
                if (pieces.KindOf(lv.Structure[i]) == PieceKind.Floor) lv.Letter[i] = letter;
            }
        }
        // a letter whose rooms have no plain floor cell (all stairs) is dropped: the parser wants each declared letter on a floor
        foreach (var (letter, _) in rooms.ToList()) if (!levels.Values.Any(l => l.Letter.Contains(letter))) rooms.Remove(letter);
        string id = DesignId(name, takenIds);
        var grid = new BuildingGrid(id, w, h, rooms, levels.Values);
        var meta = new BlueprintDef
        {
            Id = id, Name = name.Trim().Length > 0 ? name.Trim() : id, Wealth = WealthOfWalls(levels.Values), Legend = [], Rows = [], Cultures = ["any"], Kind = BlueprintKind.Building,
        };
        var noStyles = new Dictionary<string, CultureStyleDef>();      // ids, never $wall / $floor: the plan is built as it was drawn, in any culture
        string entry = BlueprintWriter.Write(meta, grid, pieces, noStyles, pruneLegend: true);
        string text = BlueprintWriter.WriteFile(
            [$"{meta.Name}: a player blueprint saved from build mode (build-mode-and-construction 4.8).", "Rows are text: '_' is empty, every other character is a key of the legend."], [entry]);
        var lib = Plans();
        var e = lib.ParseDesign(text, id, out var problem);
        if (e == null) throw new InvalidOperationException($"save as blueprint wrote a plan it cannot read back ({problem})");
        var warnings = BlueprintValidator.Validate(e, Costs.Library);
        return new SavedDesign(id, meta.Name, text, warnings, w, h, [.. levels.Keys]);
    }

    /// <summary>The id a saved design gets: <c>player.</c> and the name as lower-case letters, digits and underscores; a taken id gets _2, _3 ...</summary>
    public string DesignId(string name, IReadOnlyCollection<string>? takenIds = null)
    {
        var sb = new StringBuilder();
        foreach (char ch in name.Trim().ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
            if (sb.Length >= 32) break;
        }
        string stem = "player." + (sb.ToString().Trim('_') is { Length: > 0 } s ? s : "building");
        bool Taken(string id) => (takenIds?.Contains(id) ?? false) || (Designs?.Designs.ContainsKey(id) ?? false) || Costs.Library.Blueprints.Any(b => b.Def.Id == id);
        if (!Taken(stem)) return stem;
        for (int n = 2; ; n++) if (!Taken(stem + "_" + n)) return stem + "_" + n;
    }

    /// <summary>The wealth of a drawn building (spec 4.8): the rank of its commonest wall piece in the culture styles (the lowest wealth any style builds that wall at), poor without walls.</summary>
    private Wealth WealthOfWalls(IEnumerable<BuildingLevel> levels)
    {
        var walls = levels.SelectMany(l => l.Structure).Where(s => s != null && Costs.Library.Pieces.KindOf(s) == PieceKind.Wall)
            .GroupBy(s => s!).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key).FirstOrDefault();
        if (walls == null) return Wealth.Poor;
        foreach (var wl in new[] { Wealth.Poor, Wealth.Middling, Wealth.Rich })
            foreach (var st in Costs.Library.Styles.Values.OrderBy(s => s.Id, StringComparer.Ordinal))
                if (st.Walls.TryGetValue(wl.ToString().ToLowerInvariant(), out var piece) && piece == walls) return wl;
        return Wealth.Poor;
    }
}
