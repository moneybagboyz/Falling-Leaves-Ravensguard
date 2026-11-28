using Ravensguard.Content;
using Ravensguard.Sim.Building;

namespace Ravensguard.Sim.Construction;

/// <summary>What a plan is for, as the library's purpose filter reads its declared rooms (build-mode-and-construction 4.1).</summary>
public enum PlanPurpose : byte { Any, Dwelling, Workshop, Storage, Livestock, Civic }

/// <summary>One plan of the library: a world-generation blueprint of the loaded content or one of the player's own (the "designs" of the save, or the player's mod on disk).</summary>
public sealed record PlanEntry(string Id, string Name, BlueprintEntry Entry, bool Player, Wealth Wealth, IReadOnlyList<string> Cultures, IReadOnlyList<string> Purposes,
    IReadOnlySet<PlanPurpose> Kinds, int Width, int Height, IReadOnlyList<int> Levels)
{
    public bool ForCulture(string? culture) => culture == null || Cultures.Count == 0 || Cultures.Contains("any") || Cultures.Contains(culture);
}

/// <summary>A room the plan or the site will have once built (RoomDetector v2 over the planned cells): the build mode's room panel.</summary>
public sealed record PlannedRoom(int Z, int X, int Y, string Type, string TypeName, int Area, bool Enclosed, bool Indoor, int Beds, IReadOnlyList<string> Stations, ValueTier Value,
    IReadOnlyList<string> Declared, IReadOnlyList<string> Gaps);

/// <summary>What a plan costs and holds (the library's preview): its cells with the roof, worker milli-hours, grams by good, and the rooms it will make.</summary>
public sealed record PlanPreview(string Id, int Width, int Height, IReadOnlyList<int> Levels, int Cells, long MilliHours, IReadOnlyDictionary<string, long> Grams, IReadOnlyList<PlannedRoom> Rooms,
    IReadOnlyList<string> Rows);

/// <summary>
/// P5-C: the plan library of build mode (build-mode-and-construction 4.1, 4.8): every <see cref="BlueprintKind.Building"/> blueprint of the loaded content (world generation's own) plus the
/// player's saved ones, filtered by culture (the plan's <c>cultures</c>, any by default), wealth and purpose. A plan is resolved at a culture and wealth the way world generation builds a
/// lot: <c>$wall</c> and <c>$floor</c> through the culture's style for that wealth. Levels below ground are left out (nothing is dug in Phase 5: a cellar's stair down becomes floor).
/// Pure: the same inputs give the same list and grids, on any thread.
/// </summary>
public sealed class PlanLibrary
{
    private readonly BuildingLibrary _lib;
    private readonly Dictionary<string, CultureDef> _cultures;
    private readonly List<PlanEntry> _all = [];
    private readonly Dictionary<string, PlanEntry> _by = new(StringComparer.Ordinal);

    public IReadOnlyList<PlanEntry> All => _all;
    public IReadOnlyList<string> CultureIds { get; }
    public BuildingLibrary Library => _lib;
    /// <summary>Player designs that did not parse (id and the problems), shown by the library and never placed.</summary>
    public IReadOnlyList<(string Id, string Problem)> Broken => _broken;
    private readonly List<(string, string)> _broken = [];

    public PlanLibrary(BuildingLibrary lib, IEnumerable<CultureDef> cultures, IEnumerable<(string Id, string Text)>? designs = null)
    {
        _lib = lib;
        _cultures = cultures.ToDictionary(c => c.Id, StringComparer.Ordinal);
        CultureIds = [.. _cultures.Keys.OrderBy(k => k, StringComparer.Ordinal)];
        foreach (var e in lib.Blueprints.Where(b => b.Def.Kind == BlueprintKind.Building).OrderBy(b => b.Def.Id, StringComparer.Ordinal)) Add(e, e.Def.Id.StartsWith("player.", StringComparison.Ordinal));
        if (designs != null)
            foreach (var (id, text) in designs.OrderBy(d => d.Id, StringComparer.Ordinal))
            {
                var e = ParseDesign(text, id, out var problem);
                if (e == null) { _broken.Add((id, problem)); continue; }
                if (_by.ContainsKey(e.Def.Id)) _all.RemoveAll(p => p.Id == e.Def.Id);   // the save's copy wins over the disk's
                Add(e, true);
            }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContentDatabase, PlanLibrary> Cache = new();

    /// <summary>The library of a content database with the save's designs (the content part is shared; a book with designs builds its own, cheaply).</summary>
    public static PlanLibrary For(ContentDatabase db, DesignBook? designs = null, IEnumerable<(string Id, string Text)>? extra = null)
    {
        var d = (designs?.Designs.Select(kv => (kv.Key, kv.Value.Text)) ?? []).Concat(extra ?? []).ToList();
        if (d.Count == 0) return Cache.GetValue(db, x => new PlanLibrary(PieceCosts.Of(x).Library, x.All<CultureDef>()));
        // a design in the book wins over the same id on disk: the book's copy comes last
        var dedup = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, text) in (extra ?? []).Concat(designs?.Designs.Select(kv => (kv.Key, kv.Value.Text)) ?? [])) dedup[id] = text;
        return new PlanLibrary(PieceCosts.Of(db).Library, db.All<CultureDef>(), dedup.Select(kv => (kv.Key, kv.Value)));
    }

    /// <summary>A player design's text (one blueprint entry, as <see cref="BlueprintWriter"/> writes it) read through the mod loader; null with the problem when it does not parse.</summary>
    public BlueprintEntry? ParseDesign(string text, string id, out string problem)
    {
        var report = new ContentReport();
        var defs = new ModLoader().ReadText<BlueprintDef>(text, "designs/" + id + ".jsonc", report);
        problem = "";
        if (report.HasErrors || defs.Count != 1) { problem = report.HasErrors ? report.ToString() : "expected one blueprint"; return null; }
        var (def, loc, props) = defs[0];
        if (def.Id != id) { problem = $"the text names {def.Id}"; return null; }
        var e = new BlueprintEntry(def, loc, props);
        var issues = new List<BlueprintIssue>();
        if (_lib.Parse(e, issues) == null) { problem = string.Join("; ", issues.Select(i => i.Code)); return null; }
        return e;
    }

    private void Add(BlueprintEntry e, bool player)
    {
        var d = e.Def;
        var purposes = d.Rooms.Values.Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
        var kinds = new HashSet<PlanPurpose>();
        foreach (var p in purposes)
        {
            if (p is "sleeping" or "hearth") kinds.Add(PlanPurpose.Dwelling);
            else if (p.StartsWith("work:", StringComparison.Ordinal) || p == "shop") kinds.Add(PlanPurpose.Workshop);
            else if (p is "storage" or "cellar" or "granary") kinds.Add(PlanPurpose.Storage);
            else if (p == "livestock" || p == "dovecote") kinds.Add(PlanPurpose.Livestock);
            else kinds.Add(PlanPurpose.Civic);
        }
        if (!string.IsNullOrEmpty(d.Dwelling)) kinds.Add(PlanPurpose.Dwelling);
        int w = d.Rows.Count > 0 ? d.Rows[0].Length : 0, h = d.Rows.Count;
        var levels = new List<int> { 0 };
        levels.AddRange(d.Levels.Select(l => l.Z).Where(z => z > 0));
        levels.Sort();
        var pe = new PlanEntry(d.Id, d.Name, e, player, d.Wealth, d.Cultures, purposes, kinds, w, h, levels);
        _all.Add(pe); _by[d.Id] = pe;
        _all.Sort((a, b) => a.Player != b.Player ? (a.Player ? 1 : -1) : string.CompareOrdinal(a.Id, b.Id));
    }

    public PlanEntry? Get(string id) => _by.GetValueOrDefault(id);

    /// <summary>The plans for a culture (null: every culture), of a wealth (null: any) and purpose, world generation's first, then the player's, each by id.</summary>
    public IEnumerable<PlanEntry> Filter(string? culture, Wealth? wealth, PlanPurpose purpose) =>
        _all.Where(p => p.ForCulture(culture) && (wealth == null || p.Wealth == wealth) && (purpose == PlanPurpose.Any || p.Kinds.Contains(purpose)));

    /// <summary>The culture style a plan is built in: the culture's style for the wealth (as world generation picks a lot's), else the plan's own, else the common style.</summary>
    public string StyleFor(PlanEntry p, string? culture, Wealth wealth)
    {
        string key = wealth.ToString().ToLowerInvariant();
        if (culture != null && _cultures.TryGetValue(culture, out var cu) && cu.BuildingStyles.TryGetValue(key, out var st) && _lib.Styles.ContainsKey(st)) return st;
        return p.Entry.Def.Style ?? "common";
    }

    /// <summary>
    /// The plan's grid at a culture and wealth (null wealth: the plan's own), levels below ground left out. Null when the plan is unknown or does not parse (the issues say why).
    /// </summary>
    public BuildingGrid? Grid(string id, string? culture = null, Wealth? wealth = null, List<BlueprintIssue>? issues = null)
        => GridWithCellar(id, culture, wealth, issues) is { } g ? AboveGround(g) : null;

    /// <summary>
    /// P8-LM-3 (carried in from Phase 6, holdings 3.9): the plan's grid with its levels below ground kept, so a stair down on the ground floor leads into a cellar that is dug and built under the plan
    /// (at most <see cref="ConstructionRulesDef.MaxCellarDepth"/> levels down), not turned into floor. What build mode places for a library plan; world generation's own readers keep <see cref="Grid"/>.
    /// </summary>
    public BuildingGrid? GridWithCellar(string id, string? culture = null, Wealth? wealth = null, List<BlueprintIssue>? issues = null)
    {
        if (Get(id) is not { } p) return null;
        issues ??= [];
        var wl = wealth ?? p.Wealth;
        var def = p.Entry.Def with { Style = StyleFor(p, culture, wl), Wealth = wl };
        var g = _lib.Parse(new BlueprintEntry(def, p.Entry.Location, p.Entry.Props), issues) ?? _lib.Parse(p.Entry, issues);
        return g;
    }

    /// <summary>The grid without its levels below ground; a stair down on the ground floor that led into them becomes the floor most of that level has.</summary>
    public BuildingGrid AboveGround(BuildingGrid g)
    {
        if (g.Levels.Keys.All(z => z >= 0)) return g;
        var levels = g.Levels.Values.Where(l => l.Z >= 0).Select(l => l.Clone()).ToList();
        var ground = levels.FirstOrDefault(l => l.Z == 0);
        if (ground != null)
        {
            string? floor = ground.Structure.Where(s => s != null && _lib.Pieces.KindOf(s) == PieceKind.Floor).GroupBy(s => s!).OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key).FirstOrDefault();
            for (int i = 0; i < ground.Structure.Length; i++)
            {
                if (_lib.Pieces.KindOf(ground.Structure[i]) is not (PieceKind.StairsDown or PieceKind.Trapdoor)) continue;
                int x = i % ground.Width, y = i / ground.Width;
                if (g.StructureAt(x, y, -1) == null) continue;
                ground.Structure[i] = floor;
            }
        }
        return new BuildingGrid(g.Id, g.Width, g.Height, g.Rooms, levels) { OpenAir = g.OpenAir };
    }

    /// <summary>
    /// The cells a plan places, in build order: <see cref="PieceCosts.PlanCells"/> with the library's thatch roof over every top cell, except for a plan that draws its own roof
    /// (a player's saved building keeps exactly the roof it was saved with).
    /// </summary>
    public static List<(int X, int Y, int Z, string Piece)> Cells(BuildingGrid g, PieceCosts costs)
    {
        bool ownRoof = g.OpenAir || g.Levels.Values.Any(l => l.Structure.Any(s => s != null && costs.Library.Pieces.KindOf(s) == PieceKind.Roof));
        return costs.PlanCells(g, ownRoof ? null : "roof_thatch");
    }

    /// <summary>The library's preview of a plan: size, levels, cells, worker milli-hours and grams with the roof, the rooms it will make, and its rows as text (ground floor).</summary>
    public PlanPreview Preview(string id, BuildingGrid g, PieceCosts costs)
    {
        var cells = Cells(g, costs);
        var (h, grams) = costs.Sum(cells.Select(c => c.Piece));
        var rw = _lib.NewWorld();
        var edits = new List<CellEdit>();
        foreach (var c in cells) edits.Add(new CellEdit(c.X, c.Y, c.Z, PieceCosts.IsFixture(costs[c.Piece].Kind) ? CellLayer.Fixture : CellLayer.Structure, c.Piece));
        foreach (var (z, lv) in g.Levels)
            for (int i = 0; i < lv.Letter.Length; i++)
                if (lv.Letter[i] != 0 && g.Rooms.TryGetValue(lv.Letter[i], out var purpose)) edits.Add(new CellEdit(i % lv.Width, i / lv.Width, z, CellLayer.Purpose, purpose));
        rw.Apply(edits);
        var text = BlueprintText.Render(g, _lib.Pieces).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        return new PlanPreview(id, g.Width, g.Height, [.. g.Levels.Keys], cells.Count, h, grams, Rooms(rw, null), text);
    }

    /// <summary>The rooms of a room world (those touching <paramref name="cells"/> when given), in (z, y, x) order of their first cell.</summary>
    public static List<PlannedRoom> Rooms(RoomWorld rw, ISet<long>? cells)
    {
        var list = new List<PlannedRoom>();
        foreach (var r in rw.Rooms)
        {
            if (cells != null && !r.Cells.Any(c => cells.Contains(RoomWorld.Key(c.X, c.Y, r.Z)))) continue;
            var (ox, oy) = r.Origin;
            list.Add(new PlannedRoom(r.Z, ox, oy, r.Primary?.Id ?? "", r.Primary?.Name ?? "", r.Area, r.Enclosed, r.Indoor, r.Beds, [.. r.Stations.Keys], r.ValueTier, [.. r.DeclaredPurposes], r.PurposeGaps));
        }
        return list;
    }
}
