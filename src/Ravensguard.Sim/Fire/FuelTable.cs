using Ravensguard.Content;

namespace Ravensguard.Sim.Burning;

/// <summary>One fuel class as the rule reads it (fire 3): integers only. <see cref="Burns"/> is false for the stone class.</summary>
public sealed class FuelClass
{
    public required int Index { get; init; }
    public required string Id { get; init; }
    public int IgnitionC { get; init; }
    public bool Burns { get; init; }
    public int SpreadPm { get; init; }
    public int BurnS { get; init; }
    public int MaxIntensity { get; init; } = 1;
    public ScarKind After { get; init; } = ScarKind.Scorched;
    public bool Sparks { get; init; }
    public List<string> Materials { get; } = [];
    public List<string> Kinds { get; } = [];
    public List<string> Pieces { get; } = [];
}

/// <summary>
/// The fuel table (fire 3, <c>fire/fuels.jsonc</c> and <c>fire/upkeep.jsonc</c>): the class of every piece, from the piece's own id, else its material and kind. Class 0 is "none" (no material
/// and no override: ground, earth floors, a hearth of loose stones) and never burns. Deterministic: classes resolve in id order; a class limited to some kinds beats one for the material alone.
/// </summary>
public sealed class FuelTable
{
    public const int None = 0;
    private readonly List<FuelClass> _classes = [];
    private readonly Dictionary<string, byte> _byPiece = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte> _byId = new(StringComparer.Ordinal);
    private readonly BuildPieceDef[] _pieces;
    public FireUpkeepDef Upkeep { get; }
    public IReadOnlyList<FuelClass> Classes => _classes;
    public IReadOnlySet<string> BurnableGoodClasses { get; }

    public FuelTable(ContentDatabase db) : this(db.All<FuelClassDef>(), db.All<FireUpkeepDef>().OrderBy(u => u.Id, StringComparer.Ordinal).FirstOrDefault() ?? new FireUpkeepDef { Id = "base" }, db.All<BuildPieceDef>()) { }

    public FuelTable(IEnumerable<FuelClassDef> defs, FireUpkeepDef upkeep, IEnumerable<BuildPieceDef> pieces)
    {
        Upkeep = upkeep;
        BurnableGoodClasses = new HashSet<string>(upkeep.BurnableGoodClasses, StringComparer.Ordinal);
        _classes.Add(new FuelClass { Index = 0, Id = "none" });
        _byId["none"] = 0;
        foreach (var d in defs.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            var c = new FuelClass
            {
                Index = _classes.Count, Id = d.Id, Burns = d.IgnitionC.HasValue, IgnitionC = d.IgnitionC ?? 0, SpreadPm = d.SpreadPm, BurnS = d.BurnS,
                MaxIntensity = Math.Clamp(d.MaxIntensity, 1, 3), After = ScarOf(d.After), Sparks = d.Sparks,
            };
            c.Materials.AddRange(d.Materials); c.Kinds.AddRange(d.Kinds); c.Pieces.AddRange(d.Pieces);
            _byId[d.Id] = (byte)c.Index;
            _classes.Add(c);
        }
        _pieces = pieces.OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
        foreach (var p in _pieces) _byPiece[p.Id] = Resolve(p);
    }

    public static ScarKind ScarOf(string s) => s switch { "char" => ScarKind.Char, "cracked" => ScarKind.Cracked, "smoulder" => ScarKind.Smoulder, "scorched" => ScarKind.Scorched, "rubble" => ScarKind.Rubble, _ => ScarKind.Ash };

    /// <summary>A piece kind's snake-case name, as the content writes it.</summary>
    public static string KindName(PieceKind k) => k switch
    {
        PieceKind.StairsUp => "stairs_up", PieceKind.StairsDown => "stairs_down", _ => k.ToString().ToLowerInvariant(),
    };

    private byte Resolve(BuildPieceDef p)
    {
        foreach (var c in _classes) if (c.Pieces.Contains(p.Id)) return (byte)c.Index;
        string kind = KindName(p.Kind);
        if (p.Material != null)
        {
            foreach (var c in _classes) if (c.Kinds.Count > 0 && c.Materials.Contains(p.Material) && c.Kinds.Contains(kind)) return (byte)c.Index;
            foreach (var c in _classes) if (c.Kinds.Count == 0 && c.Materials.Contains(p.Material)) return (byte)c.Index;
        }
        return None;
    }

    public FuelClass Get(int index) => _classes[index];
    public FuelClass? Find(string id) => _byId.TryGetValue(id, out var i) ? _classes[i] : null;

    /// <summary>The fuel class of a piece (none for an unknown or null id).</summary>
    public FuelClass ClassOf(string? pieceId) => pieceId != null && _byPiece.TryGetValue(pieceId, out var i) ? _classes[i] : _classes[None];
    public int IndexOf(string? pieceId) => pieceId != null && _byPiece.TryGetValue(pieceId, out var i) ? i : None;

    /// <summary>The yearly decay of a piece's condition, permille (0 for a class with no row).</summary>
    public int DecayPmPerYear(FuelClass c) => Upkeep.DecayPmPerYear.GetValueOrDefault(c.Id);
}
