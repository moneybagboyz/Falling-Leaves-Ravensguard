using Ravensguard.Game.UI.NewGame;
using System;
using System.Collections.Generic;
using System.Linq;
using Ravensguard.Content;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Game.World.Build;

/// <summary>The palette's categories (build-mode-and-construction 6: Walls, Floors, Roofs, Doors and windows, Stairs, Furniture, Workstations, Fences).</summary>
public enum BuildCategory { Walls, Floors, Roofs, Openings, Stairs, Furniture, Workstations, Fences }

/// <summary>One piece of the palette: its cost (content, as integers), its category and its material's name.</summary>
public sealed record PalettePiece(string Id, string Name, PieceKind Kind, string Material, string MaterialName, BuildCategory Category, PieceCost Cost);

/// <summary>What a brush or a plan costs (spec 6: cost, worker-hours, material list): cells, worker milli-hours at skill 50, grams by good, and the coin at the goods' book price plus a labourer's wage.</summary>
public sealed record BrushCost(int Cells, long MilliHours, IReadOnlyList<(string Good, string Name, long Grams)> Materials, long Mites);

/// <summary>
/// P5-C: build mode's palette over the loaded content (no game state): every piece with a build cost by category and material, and the cost of a set of pieces. Pure; read on the main
/// thread from the content database, which never changes while a game runs.
/// </summary>
public sealed class BuildCatalog
{
    public const long LabourerMitesPerHour = 15;          // production's labourer, 150 mites a 10-hour day
    readonly ContentDatabase _db;
    public PieceCosts Costs { get; }
    public IReadOnlyList<BuildCategory> Categories { get; } = Enum.GetValues<BuildCategory>();
    readonly Dictionary<BuildCategory, List<PalettePiece>> _by = new();

    public BuildCatalog(ContentDatabase db)
    {
        _db = db;
        Costs = PieceCosts.Of(db);
        var mats = db.All<MaterialDef>().ToDictionary(m => m.Id, m => m.Name, StringComparer.Ordinal);
        foreach (var c in Categories) _by[c] = [];
        foreach (var p in db.All<BuildPieceDef>())
        {
            if (p.Build == null || Costs.Get(p.Id) is not { HasBuild: true } cost) continue;
            if (CategoryOf(p) is not { } cat) continue;
            string mat = p.Material ?? "";
            _by[cat].Add(new PalettePiece(p.Id, p.Name, p.Kind, mat, mats.GetValueOrDefault(mat) ?? UI.NewGame.UiKit.Pretty(mat), cat, cost));
        }
        foreach (var l in _by.Values) l.Sort((a, b) => a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind) : string.CompareOrdinal(a.MaterialName + a.Name, b.MaterialName + b.Name));
    }

    /// <summary>Where a piece sits in the palette; null for the ones placed only with another (the stair down goes with the stair up) or never by hand (ground).</summary>
    public static BuildCategory? CategoryOf(BuildPieceDef p)
    {
        string id = p.Id;
        if (id.Contains("fence") || id.Contains("palisade") || id.Contains("hurdle") || id.StartsWith("gate", StringComparison.Ordinal)) return BuildCategory.Fences;
        return p.Kind switch
        {
            PieceKind.Wall or PieceKind.Post or PieceKind.Beam => BuildCategory.Walls,
            PieceKind.Floor => BuildCategory.Floors,
            PieceKind.Roof => BuildCategory.Roofs,
            PieceKind.Door or PieceKind.Window => BuildCategory.Openings,
            PieceKind.StairsUp or PieceKind.Ladder or PieceKind.Trapdoor => BuildCategory.Stairs,
            PieceKind.Furniture => BuildCategory.Furniture,
            PieceKind.Workstation => BuildCategory.Workstations,
            _ => null,
        };
    }

    public IReadOnlyList<PalettePiece> In(BuildCategory c) => _by[c];

    public static string CategoryName(BuildCategory c) => L.T("BUILD_CAT_" + c.ToString().ToUpperInvariant());

    /// <summary>The cost of these pieces (one per cell): hours at skill 50 for one worker, materials, and the coin at book price with a labourer's wage.</summary>
    public BrushCost Cost(IEnumerable<string> pieces)
    {
        var list = pieces.ToList();
        var (h, grams) = Costs.Sum(list.Where(p => Costs.Get(p) != null));
        long mites = h * LabourerMitesPerHour / 1000;
        var mats = new List<(string, string, long)>();
        foreach (var (good, g) in grams.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            string name = good;
            if (_db.TryGet<GoodDef>(good, out var gd)) { name = gd.Name; mites += (long)Math.Round(g * gd.BasePriceMitesPerKg / 1000.0); }
            mats.Add((good, name, g));
        }
        return new BrushCost(list.Count, h, mats, mites);
    }

    /// <summary>"6 cells: 39 worker-hours; wattle 96 kg, clay 474 kg; about 1 crown 20" (the palette's cost line).</summary>
    public static string Describe(BrushCost c, int maxGoods = 4)
    {
        string mats = string.Join(", ", c.Materials.Take(maxGoods).Select(m => L.F("BUILD_MAT_KG", m.Name, Kg(m.Grams))));
        if (c.Materials.Count > maxGoods) mats += ", ...";
        return L.F("BUILD_COST_LINE", c.Cells, Hours(c.MilliHours), mats.Length > 0 ? mats : L.T("BUILD_NO_MATERIALS"), UiKit.Money(c.Mites));
    }

    public static string Kg(long grams) => grams >= 10_000 ? (grams / 1000).ToString("N0") : (grams / 1000.0).ToString("0.#");
    public static string Hours(long milliHours) => milliHours >= 100_000 ? (milliHours / 1000).ToString("N0") : (milliHours / 1000.0).ToString("0.#");

    /// <summary>The sprite a piece is drawn with (the street's own art; P5-B's ghost layer reads the same table).</summary>
    public static string? Sprite(string piece, PieceKind kind) => kind switch
    {
        PieceKind.Wall => piece.Contains("wattle") ? "terrain.wall_wattle" : piece.Contains("stone") || piece.Contains("brick") ? "terrain.wall_stone" : "terrain.wall_timber",
        PieceKind.Window => piece.Contains("arrow") || piece.Contains("open") ? "terrain.wall_stone_win" : "terrain.wall_timber_win",
        PieceKind.Door => "terrain.door_h",
        PieceKind.Floor => piece == "floor_plank" ? "terrain.wood_floor" : piece == "floor_packed_earth" ? "terrain.earth_floor" : "terrain.flagstone",
        PieceKind.StairsUp => "terrain.stairs_up",
        PieceKind.StairsDown => "terrain.stairs_down",
        PieceKind.Ladder => "object.ladder",
        PieceKind.Trapdoor => "object.trapdoor",
        PieceKind.Post => "object.stake",
        PieceKind.Furniture or PieceKind.Workstation => piece switch
        {
            "bed" or "bunk" or "straw_pallet" => "object.bed", "forge" => "object.forge", "anvil" => "object.anvil", "table" => "object.table", "chest" => "object.chest",
            "hearth" or "hearth_central" or "hearth_chimney" => "object.hearth", _ => "object.crate",
        },
        _ => null,
    };
}
