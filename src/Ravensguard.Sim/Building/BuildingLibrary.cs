using Ravensguard.Content;

namespace Ravensguard.Sim.Building;

/// <summary>Everything the building code needs from a loaded content database.</summary>
public sealed class BuildingLibrary
{
    public PieceCatalog Pieces { get; }
    public IReadOnlyList<RoomTypeDef> RoomTypes { get; }
    public IReadOnlyDictionary<string, CultureStyleDef> Styles { get; }
    public IReadOnlyList<BlueprintEntry> Blueprints { get; }

    public BuildingLibrary(PieceCatalog pieces, IEnumerable<RoomTypeDef> roomTypes, IEnumerable<CultureStyleDef> styles, IEnumerable<BlueprintEntry> blueprints)
    {
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
        return new BuildingLibrary(new PieceCatalog(db.All<BuildPieceDef>()), db.All<RoomTypeDef>(), db.All<CultureStyleDef>(), bps);
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

    /// <summary>Room types that satisfy a declared purpose ("work:*" matches every "work:..." type).</summary>
    public IEnumerable<RoomTypeDef> TypesFor(string purpose) => RoomTypes.Where(t => PurposeMatches(purpose, t.Purpose));

    public static bool PurposeMatches(string declared, string actual) =>
        declared.EndsWith(":*", StringComparison.Ordinal)
            ? actual.StartsWith(declared[..^1], StringComparison.Ordinal)
            : declared == actual;
}
