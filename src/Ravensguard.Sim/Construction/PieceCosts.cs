using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Construction;

/// <summary>One piece's cost as integers (spec 3.1): worker milli-hours at skill 50, the skill, tool qualities, materials in grams of each good, hit points, the storeys it carries.</summary>
public sealed class PieceCost
{
    public required string Id { get; init; }
    public required PieceKind Kind { get; init; }
    public required string Name { get; init; }
    public Skill Skill { get; init; }
    public long MilliHours { get; init; }
    public (string Quality, int Level)[] Tools { get; init; } = [];
    /// <summary>Good ids and grams, in the content's order.</summary>
    public (string Good, long Grams)[] Materials { get; init; } = [];
    public int Hp { get; init; }
    public int Carries { get; init; }
    public int SalvagePm { get; init; }
    public bool Outdoor { get; init; } = true;
    public bool RainStops { get; init; }
    public int MinSkill { get; init; }
    public bool Heavy { get; init; }
    public bool HasBuild { get; init; }

    /// <summary>The work in standard worker-microseconds (1,000,000 a second of a skill-50 worker with the tools).</summary>
    public long WorkUnits => MilliHours * 3_600_000;
    public GhostSlot Slot => PieceCosts.IsFixture(Kind) ? GhostSlot.Fixture : GhostSlot.Structure;
    /// <summary>The difficulty a skill gain reads (crafting's rule: min skill + 20).</summary>
    public int Difficulty => MinSkill + 20;
    public long TotalGrams => Materials.Sum(m => m.Grams);

    // P5-D: the engine's memo for one pick or refresh (whether the site's pile holds this piece's materials), keyed by a stamp; derived, never saved
    internal long PileMemoStamp;
    internal bool PileMemo;
}

/// <summary>The piece costs of the loaded content (spec 3.1) and the sums of a plan (BC-2).</summary>
public sealed class PieceCosts
{
    private readonly Dictionary<string, PieceCost> _by = new(StringComparer.Ordinal);
    public BuildingLibrary Library { get; }
    public ConstructionRulesDef Rules { get; }

    public PieceCosts(ContentDatabase db) : this(BuildingLibrary.FromDatabase(db), RulesOf(db)) { }

    public PieceCosts(BuildingLibrary lib, ConstructionRulesDef rules)
    {
        Library = lib; Rules = rules;
        foreach (var p in lib.Pieces.All.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            var b = p.Build;
            _by[p.Id] = new PieceCost
            {
                Id = p.Id, Kind = p.Kind, Name = p.Name, HasBuild = b != null,
                Skill = SkillOf(b?.Skill), MilliHours = (long)Math.Round((b?.WorkerHours ?? 1.0) * 1000.0),
                Tools = b == null ? [] : b.Tools.Select(t => (t.Quality, t.Level)).ToArray(),
                Materials = b == null ? [] : b.Materials.Select(m => (m.Good, (long)Math.Round(m.Kg * 1000.0))).Where(m => m.Item2 > 0).ToArray(),
                Hp = b?.Hp ?? 100, Carries = b?.CarriesStoreys ?? 0, SalvagePm = b?.SalvagePm ?? 0, Outdoor = b?.OutdoorWork ?? true, RainStops = b?.RainStops ?? false,
                MinSkill = b?.MinSkill ?? 0, Heavy = b?.Effort == "heavy",
            };
        }
    }

    public static ConstructionRulesDef RulesOf(ContentDatabase db) => db.All<ConstructionRulesDef>().OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault() ?? new ConstructionRulesDef { Id = "construction" };

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContentDatabase, PieceCosts> Cache = new();
    /// <summary>The costs of a database, built once.</summary>
    public static PieceCosts Of(ContentDatabase db) => Cache.GetValue(db, d => new PieceCosts(d));

    public static Skill SkillOf(string? s) => s switch { "carpentry" => Skill.Carpentry, "masonry" => Skill.Masonry, "smithing" => Skill.Smithing, _ => Skill.None };

    public static bool IsFixture(PieceKind k) => k is PieceKind.Furniture or PieceKind.Workstation;
    /// <summary>Pieces that need what is below to hold them (floors, roofs, stairs, ladders, beams).</summary>
    public static bool IsFloorBearing(PieceKind k) => PieceCatalog.IsRoomKind(k) || k is PieceKind.Roof or PieceKind.Beam;
    /// <summary>Pieces that stand on what is directly below them (walls, posts, the openings in a wall).</summary>
    public static bool IsUpright(PieceKind k) => k is PieceKind.Wall or PieceKind.Post or PieceKind.Door or PieceKind.Window;
    /// <summary>What carries a floor within reach (plan 2.2a: walls and posts).</summary>
    public static bool IsSupport(PieceKind k) => PieceCatalog.IsSupport(k);

    /// <summary>The piece's cost, or null for an unknown id.</summary>
    public PieceCost? Get(string id) => _by.GetValueOrDefault(id);
    public PieceCost this[string id] => _by[id];
    public IEnumerable<PieceCost> All => _by.Values;

    /// <summary>Workers that may work one cell of this piece at once (four on a stair or a forge, spec 4.4).</summary>
    public int MaxWorkers(PieceCost c) => c.Kind is PieceKind.StairsUp or PieceKind.StairsDown || c.Id == "forge" ? Rules.WorkersPerBigCell : Rules.WorkersPerCell;

    /// <summary>
    /// The pieces of a plan in build order (structure and fixtures of every level, then a roof over the footprint of each top cell that has nothing above it), as cells relative to
    /// the plan's corner. The roof is the plan library's own: a world-generation blueprint is a finished building whose roof is not drawn (BC-2: test_cottage's 40 thatch cells).
    /// </summary>
    public List<(int X, int Y, int Z, string Piece)> PlanCells(BuildingGrid g, string? roofPiece = "roof_thatch")
    {
        var cells = new List<(int, int, int, string)>();
        foreach (var (z, lv) in g.Levels)
            for (int y = 0; y < lv.Height; y++)
                for (int x = 0; x < lv.Width; x++)
                {
                    int i = lv.Idx(x, y);
                    if (lv.Structure[i] is { } s && s.Length > 0) cells.Add((x, y, z, s));
                    if (lv.Fixture[i] is { } f && f.Length > 0) cells.Add((x, y, z, f));
                }
        if (roofPiece != null)
            foreach (var (z, lv) in g.Levels)
            {
                if (z < 0) continue;
                for (int y = 0; y < lv.Height; y++)
                    for (int x = 0; x < lv.Width; x++)
                    {
                        if (lv.Structure[lv.Idx(x, y)] is not { } sid || Library.Pieces.KindOf(sid) is null or PieceKind.Ground or PieceKind.Roof) continue;
                        if (g.StructureAt(x, y, z + 1) != null) continue;
                        cells.Add((x, y, z + 1, roofPiece));
                    }
            }
        return cells;
    }

    /// <summary>Worker milli-hours and grams per good of a list of pieces (BC-2).</summary>
    public (long MilliHours, SortedDictionary<string, long> Grams) Sum(IEnumerable<string> pieces)
    {
        long h = 0; var g = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var id in pieces)
        {
            var c = this[id]; h += c.MilliHours;
            foreach (var (good, grams) in c.Materials) g[good] = g.GetValueOrDefault(good) + grams;
        }
        return (h, g);
    }

    /// <summary>Time multiplier of a worker (production 3.1): <c>1300 - 6 x skill</c> permille; an unskilled piece reads skill 50 for everyone.</summary>
    public static int TimeMultPm(PieceCost c, int skill) => c.Skill == Skill.None ? 1000 : Math.Max(100, 1300 - 6 * Math.Clamp(skill, 0, 100));

    /// <summary>Work units one worker adds in <paramref name="seconds"/>: a piece of h hours at multiplier T takes h x T / 1000 hours (units are standard microseconds, so the truncation of a call is a millionth of a second).</summary>
    public static long UnitsFor(long seconds, int timePm, int toolPm) => seconds is >= 0 and < 9_000_000 ? seconds * 1_000_000_000_000L / ((long)timePm * toolPm) : (long)((Int128)seconds * 1_000_000_000_000L / ((long)timePm * toolPm));   // P5-D: long arithmetic when it fits (the same result)
    /// <summary>The seconds a worker needs to add <paramref name="units"/>, to the nearest second (whole quanta and split hand-work both land on the exact second).</summary>
    public static long SecondsFor(long units, int timePm, int toolPm) => units is >= 0 and < 2_000_000_000_000L && timePm is > 0 and <= 2000 && toolPm is > 0 and <= 2000 ? (units * timePm * toolPm + 500_000_000_000L) / 1_000_000_000_000L : (long)(((Int128)units * timePm * toolPm + 500_000_000_000L) / 1_000_000_000_000L);

    /// <summary>A built piece's hit points by the builder's skill (spec 4.4): <c>hp x (700 + 6 x min(skill, 50)) / 1000</c>.</summary>
    public static int BuiltHp(PieceCost c, int skill) => (int)((long)c.Hp * (700 + 6 * Math.Clamp(skill, 0, 50)) / 1000);
}
