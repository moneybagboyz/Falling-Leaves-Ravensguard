using Ravensguard.Content;

namespace Ravensguard.Sim.Building;

/// <summary>Everything the building code needs from a loaded content database.</summary>
public sealed class BuildingLibrary
{
    public PieceCatalog Pieces { get; }
    public IReadOnlyList<RoomTypeDef> RoomTypes { get; }
    public IReadOnlyDictionary<string, CultureStyleDef> Styles { get; }
    public IReadOnlyList<BlueprintEntry> Blueprints { get; }

    /// <summary>P5-A3: reference value of a material in mites per kg (room value, roomdetector-v2 4.5); empty when built without a database.</summary>
    public IReadOnlyDictionary<string, long> MaterialMitesPerKg { get; }
    /// <summary>P5-A3: build piece id to the craft_station id it counts as (RoomDetector v2 stations); null = the piece's own station.</summary>
    public IReadOnlyDictionary<string, string>? PieceStation { get; }

    public BuildingLibrary(PieceCatalog pieces, IEnumerable<RoomTypeDef> roomTypes, IEnumerable<CultureStyleDef> styles, IEnumerable<BlueprintEntry> blueprints,
        IReadOnlyDictionary<string, long>? mitesPerKg = null, IReadOnlyDictionary<string, string>? pieceStation = null)
    {
        MaterialMitesPerKg = mitesPerKg ?? new Dictionary<string, long>();
        PieceStation = pieceStation;
        Pieces = pieces;
        RoomTypes = roomTypes.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        Styles = styles.ToDictionary(s => s.Id, StringComparer.Ordinal);
        Blueprints = blueprints.ToList();
    }

    public static BuildingLibrary FromDatabase(ContentDatabase db)
    {
        var bps = db.Entries.Where(e => e.Value is BlueprintDef)
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .Select(e => new BlueprintEntry((BlueprintDef)e.Value!, e.Location, e.PropertyLocations));
        // P5-A3: one mite is a hundredth of a crown (ItemMath); craft stations name the pieces that count for them
        var mites = db.All<MaterialDef>().ToDictionary(m => m.Id, m => (long)Math.Round(m.ValuePerKg * 100.0), StringComparer.Ordinal);
        var station = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var st in db.All<CraftStationDef>().OrderBy(c => c.Id, StringComparer.Ordinal))
            foreach (var piece in st.Pieces) station.TryAdd(piece, st.Id);
        return new BuildingLibrary(new PieceCatalog(db.All<BuildPieceDef>()), db.All<RoomTypeDef>(), db.All<CultureStyleDef>(), bps, mites, station);
    }

    public BuildingGrid? Parse(BlueprintEntry e, List<BlueprintIssue> issues) => BlueprintParser.Parse(e, Pieces, Styles, issues);

    public BlueprintEntry Entry(string id) => Blueprints.First(b => b.Def.Id == id);

    /// <summary>Parsed grid of a content blueprint; throws with the issues if it does not parse.</summary>
    public BuildingGrid Grid(string id)
    {
        var issues = new List<BlueprintIssue>();
        return Parse(Entry(id), issues) ?? throw new InvalidOperationException(string.Join("\n", issues));
    }

    public RoomDetector Detector => new(Pieces, RoomTypes);

    /// <summary>P5-A3: an empty live world with the v2 detector's rules (roomdetector-v2.md).</summary>
    public RoomWorld NewWorld() => new(this);

    /// <summary>Room types that satisfy a declared purpose ("work:*" matches every "work:..." type).</summary>
    public IEnumerable<RoomTypeDef> TypesFor(string purpose) => RoomTypes.Where(t => PurposeMatches(purpose, t.Purpose));

    public static bool PurposeMatches(string declared, string actual) =>
        declared.EndsWith(":*", StringComparison.Ordinal)
            ? actual.StartsWith(declared[..^1], StringComparison.Ordinal)
            : declared == actual;
}
