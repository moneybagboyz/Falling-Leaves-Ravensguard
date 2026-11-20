using Ravensguard.Content;

namespace Ravensguard.Sim.Carts;

/// <summary>
/// What a vehicle's parts add up to (horses-and-carts spec 3.3, <c>VehicleStats.Derive</c>; P5-G <see cref="PartGrid.Derive"/>): mass, cargo by mass and volume, wheels, axle limit, hitch, crew and size, and
/// for a hull what BoatMath needs (waterplane, form, depth, beam, the parts' mass moment, rowers, polers, sail).
/// </summary>
public sealed record VehicleStats
{
    public int MassKg { get; init; }
    public int CargoKg { get; init; }
    public int CargoL { get; init; }
    public int Wheels { get; init; }
    public int Axles { get; init; }
    public int AxleLimitKg { get; init; }
    /// <summary>Animals of one team (a pair of oxen, one horse).</summary>
    public int HitchSlots { get; init; }
    /// <summary>Animals that may be hitched in all (several teams in a row); never below <see cref="HitchSlots"/>.</summary>
    public int MaxAnimals { get; init; }
    public string HitchKind { get; init; } = "";
    public int Driver { get; init; }
    public int Passengers { get; init; }
    public int Seats { get; init; }
    public int BaseSpeedCmps { get; init; }
    public int TurningRadiusDm { get; init; }
    public int WidthCells { get; init; }
    public int LengthCells { get; init; }
    /// <summary>The first row (counted from the rear) that holds a seat, or -1: where the hero sits when driving, and so where the vehicle is placed.</summary>
    public int SeatRow { get; init; } = -1;
    public bool Runners { get; init; }
    public int WheelHp { get; init; }
    // ---- P5-G
    /// <summary>Hitch parts of the hitch's kind.</summary>
    public int Hitches { get; init; }
    public bool Boat { get; init; }
    public int HullCells { get; init; }
    public int BeamCells { get; init; }
    public int HullLengthCells { get; init; }
    public int PerimeterM { get; init; }
    public int WaterplanePm { get; init; }
    public int BlockPm { get; init; }
    public int HullDepthMm { get; init; }
    public long WaterplaneMm2 { get; init; }
    /// <summary>The parts' mass times their height above the keel, kg mm (KG's part).</summary>
    public long MomentKgMm { get; init; }
    public int Rowers { get; init; }
    public int Polers { get; init; }
    public int SailM2 { get; init; }
    public bool TowPost { get; init; }
    public bool Ramps { get; init; }
    public bool FishWell { get; init; }
    public bool Shop { get; init; }
    public bool Hearth { get; init; }
    public bool Covered { get; init; }
    public bool Keel { get; init; }
    public bool PivotAxle { get; init; }
    /// <summary>What steers it: pivot_axle, pole, shafts, handles, rudder, steering_oar, oars, rope (empty: nothing).</summary>
    public string Steer { get; init; } = "";
}

/// <summary>A validated vehicle design: the blueprint as content (or a player's design), its parts resolved, and its derived <see cref="VehicleStats"/>.</summary>
public sealed class VehicleBlueprint
{
    public required VehicleBlueprintDef Def { get; init; }
    public required VehicleStats Stats { get; init; }
    public int Index { get; init; }
    public string Id => Def.Id;
    /// <summary>Its part rows as built: what a new vehicle of it gets (vehicle-designer 3.3).</summary>
    public IReadOnlyList<PartRow> Parts { get; init; } = [];
    /// <summary>A design of the player's (<c>player.*</c>, from the save's designs or the player's mod).</summary>
    public bool Player => Id.StartsWith("player.", StringComparison.Ordinal);
    public IReadOnlyList<DesignIssue> Warnings { get; init; } = [];

    /// <summary>
    /// Builds a blueprint, adding to <paramref name="errors"/> for each rule it breaks (null result when any): the rules of <see cref="PartGrid.Validate"/> (every legend letter known, rows the same width,
    /// every part on what it needs, a frame in every body cell, a hitch exactly when animals pull, a seat when the crew sits, wheels in pairs on an axle, steering, a whole hull that floats).
    /// </summary>
    public static VehicleBlueprint? Build(VehicleBlueprintDef d, VehicleCatalog cat, int index, List<string> errors)
    {
        string who = $"vehicle '{d.Id}'";
        var issues = PartGrid.Validate(d, cat, 0, cat.Water);
        var bad = issues.Where(i => i.IsError).ToList();
        if (bad.Count > 0) { foreach (var i in bad) errors.Add($"{who}: {PartGrid.Describe(i, d, cat)}"); return null; }
        var parts = PartGrid.Expand(d, cat);
        return new VehicleBlueprint { Def = d, Index = index, Stats = PartGrid.Derive(d, parts, cat, builtOnly: false), Parts = parts, Warnings = issues };
    }
}

/// <summary>
/// Every vehicle template and part of a content database, validated, in id order (P5-G: a vehicle's design is saved by its id; the index is the running game's), and the player's designs registered
/// after the templates (<see cref="Register"/>). Parts are listed in id order too (<see cref="PartList"/>).
/// </summary>
public sealed class VehicleCatalog
{
    private readonly List<VehicleBlueprint> _list = [];
    private readonly Dictionary<string, VehicleBlueprint> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _partIndex = new(StringComparer.Ordinal);
    public IReadOnlyList<VehicleBlueprint> All => _list;
    /// <summary>The content templates (the player's designs come after them in <see cref="All"/>).</summary>
    public int TemplateCount { get; }
    public IReadOnlyList<string> Errors { get; }
    public VehicleTuning Tuning { get; }
    public WaterTuning Water { get; }
    public IReadOnlyDictionary<string, VehiclePartDef> Parts { get; }
    public IReadOnlyList<VehiclePartDef> PartList { get; }

    private VehicleCatalog(IReadOnlyDictionary<string, VehiclePartDef> parts, IEnumerable<VehicleBlueprintDef> defs, VehicleTuning tuning, WaterTuning water)
    {
        Parts = parts; Tuning = tuning; Water = water; var errors = new List<string>();
        PartList = [.. parts.Values.OrderBy(p => p.Id, StringComparer.Ordinal)];
        for (int i = 0; i < PartList.Count; i++) _partIndex[PartList[i].Id] = i;
        foreach (var d in defs.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            var b = VehicleBlueprint.Build(d, this, _list.Count, errors);
            if (b == null) continue;
            _list.Add(b); _byId[d.Id] = b;
        }
        TemplateCount = _list.Count;
        Errors = errors;
    }

    public static VehicleCatalog From(ContentDatabase db)
    {
        var parts = db.All<VehiclePartDef>().ToDictionary(p => p.Id, StringComparer.Ordinal);
        return new VehicleCatalog(parts, db.All<VehicleBlueprintDef>(), VehicleTuning.From(db), WaterTuning.From(db));
    }

    public int PartIndex(string id) => _partIndex.TryGetValue(id, out int i) ? i : -1;
    public VehiclePartDef Part(int index) => PartList[index];
    public VehicleBlueprint? Find(string id) => _byId.TryGetValue(id, out var b) ? b : null;
    public VehicleBlueprint Get(string id) => _byId[id];
    public VehicleBlueprint At(int index) => _list[index];

    /// <summary>
    /// Registers (or replaces) a player's design so vehicles of it can be built and loaded; null with the reasons when it does not validate. A design already known with the same text keeps its index.
    /// </summary>
    public VehicleBlueprint? Register(VehicleBlueprintDef d, List<string> errors)
    {
        if (_byId.TryGetValue(d.Id, out var old) && old.Index < TemplateCount) { errors.Add($"vehicle '{d.Id}': a content template has this id"); return null; }
        int index = old?.Index ?? _list.Count;
        var b = VehicleBlueprint.Build(d, this, index, errors);
        if (b == null) return null;
        if (old != null) _list[index] = b; else _list.Add(b);
        _byId[d.Id] = b;
        return b;
    }

    /// <summary>A player's design text (one <c>vehicle_blueprint</c> entry) read through the mod loader; null with the problem when it does not parse or its id is not <paramref name="id"/>.</summary>
    public static VehicleBlueprintDef? ParseDesign(string text, string id, out string problem)
    {
        var report = new ContentReport();
        var defs = new ModLoader().ReadText<VehicleBlueprintDef>(text, "designs/" + id + ".jsonc", report);
        problem = "";
        if (report.HasErrors || defs.Count != 1) { problem = report.HasErrors ? report.ToString() : "expected one vehicle blueprint"; return null; }
        var def = defs[0].Item1;
        if (def.Id != id) { problem = $"the text names {def.Id}"; return null; }
        return def;
    }
}
