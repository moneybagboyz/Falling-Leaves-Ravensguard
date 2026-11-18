using Ravensguard.Content;

namespace Ravensguard.Sim.Carts;

/// <summary>What a template's parts add up to (horses-and-carts spec 3.3, <c>VehicleStats.Derive</c>): mass, cargo by mass and volume, wheels, axle limit, hitch, crew and size.</summary>
public sealed record VehicleStats
{
    public int MassKg { get; init; }
    public int CargoKg { get; init; }
    public int CargoL { get; init; }
    public int Wheels { get; init; }
    public int Axles { get; init; }
    public int AxleLimitKg { get; init; }
    public int HitchSlots { get; init; }
    public string HitchKind { get; init; } = "";
    public int Driver { get; init; }
    public int Passengers { get; init; }
    public int Seats { get; init; }
    public int BaseSpeedCmps { get; init; }
    public int TurningRadiusDm { get; init; }
    public int WidthCells { get; init; }
    public int LengthCells { get; init; }
    public bool Runners { get; init; }
    public int WheelHp { get; init; }
}

/// <summary>A validated template vehicle: the blueprint as content, its parts resolved, and its derived <see cref="VehicleStats"/>.</summary>
public sealed class VehicleBlueprint
{
    public required VehicleBlueprintDef Def { get; init; }
    public required VehicleStats Stats { get; init; }
    public int Index { get; init; }
    public string Id => Def.Id;

    /// <summary>
    /// Builds a blueprint, adding to <paramref name="errors"/> for each rule it breaks (null result when any): every legend letter of a row is known, rows are the same width, every part has one of the
    /// parts it needs below it in its cell, a hitch exists exactly when animals pull, a seat exists when the crew sits, and wheels come in pairs on an axle (one wheel only on a pushed barrow).
    /// </summary>
    public static VehicleBlueprint? Build(VehicleBlueprintDef d, IReadOnlyDictionary<string, VehiclePartDef> parts, int index, List<string> errors)
    {
        int n0 = errors.Count; string who = $"vehicle '{d.Id}'";
        if (d.Rows.Count == 0) { errors.Add($"{who}: no rows"); return null; }
        int width = d.Rows[0].Length;
        int mass = 0, cargoKg = 0, cargoL = 0, wheels = 0, axles = 0, seats = 0, hitches = 0, wheelHp = 0;
        string hitchTag = d.Hitch.Kind;
        var cells = new Dictionary<char, List<VehiclePartDef>>();
        foreach (var (k, ids) in d.Legend)
        {
            if (k.Length != 1) { errors.Add($"{who}: legend key '{k}' must be one character"); continue; }
            var stack = new List<VehiclePartDef>();
            foreach (var id in ids)
            {
                if (!parts.TryGetValue(id, out var p)) { errors.Add($"{who}: unknown part '{id}'"); continue; }
                if (p.NeedsBelow.Count > 0 && !stack.Any(s => p.NeedsBelow.Contains(s.Id))) errors.Add($"{who}: part '{id}' in cell '{k}' needs one of [{string.Join(", ", p.NeedsBelow)}] below it");
                stack.Add(p);
            }
            cells[k[0]] = stack;
        }
        for (int r = 0; r < d.Rows.Count; r++)
        {
            if (d.Rows[r].Length != width) errors.Add($"{who}: row {r} is {d.Rows[r].Length} wide, row 0 is {width}");
            foreach (char c in d.Rows[r])
            {
                if (c == '_') continue;
                if (!cells.TryGetValue(c, out var stack)) { errors.Add($"{who}: row {r} uses '{c}', which is not in the legend"); continue; }
                foreach (var p in stack)
                {
                    mass += p.MassKg; cargoKg += p.CargoKg; cargoL += p.CargoL;
                    if (p.Tags.Contains("wheel")) { wheels++; wheelHp = wheelHp == 0 ? p.Hp : Math.Min(wheelHp, p.Hp); }
                    if (p.Tags.Contains("axle")) axles++;
                    if (p.Tags.Contains("seat")) seats++;
                    if (p.Tags.Contains("hitch") && p.Tags.Contains(hitchTag)) hitches++;
                }
            }
        }
        if (d.Hitch.Slots > 0 && hitches == 0) errors.Add($"{who}: pulled by {d.Hitch.Slots} animals but has no '{hitchTag}' hitch part");
        if (d.Hitch.Slots == 0 && d.Hitch.Kind != "pushed") errors.Add($"{who}: a vehicle with no hitch slots must be pushed");
        if (d.Hitch.Slots > 0 && d.Hitch.Kind is "shafts" or "pole_yoke" && seats == 0) errors.Add($"{who}: a driven vehicle needs a seat");
        if (d.Passengers > 0 && seats == 0 && d.Hitch.Slots > 0) errors.Add($"{who}: carries {d.Passengers} passengers but has no seat");
        bool barrow = d.Hitch.Kind == "pushed" && d.Hitch.Slots == 0 && wheels == 1;
        if (wheels % 2 != 0 && !barrow) errors.Add($"{who}: {wheels} wheels (they come in pairs on an axle)");
        if (wheels > 1 && axles * 2 < wheels) errors.Add($"{who}: {wheels} wheels need {(wheels + 1) / 2} axles, has {axles}");
        if (errors.Count > n0) return null;
        return new VehicleBlueprint
        {
            Def = d, Index = index,
            Stats = new VehicleStats
            {
                MassKg = mass, CargoKg = cargoKg, CargoL = cargoL, Wheels = wheels, Axles = axles, AxleLimitKg = d.AxleLimitKg, HitchSlots = d.Hitch.Slots, HitchKind = d.Hitch.Kind, Driver = d.Driver,
                Passengers = d.Passengers, Seats = seats, BaseSpeedCmps = d.BaseSpeedCmps, TurningRadiusDm = d.TurningRadiusDm, WidthCells = width, LengthCells = d.Rows.Count, Runners = d.Runners, WheelHp = wheelHp,
            },
        };
    }
}

/// <summary>Every vehicle template and part of a content database, validated, in id order (the index is what saves store).</summary>
public sealed class VehicleCatalog
{
    private readonly List<VehicleBlueprint> _list = [];
    private readonly Dictionary<string, VehicleBlueprint> _byId = new(StringComparer.Ordinal);
    public IReadOnlyList<VehicleBlueprint> All => _list;
    public IReadOnlyList<string> Errors { get; }
    public VehicleTuning Tuning { get; }
    public IReadOnlyDictionary<string, VehiclePartDef> Parts { get; }

    private VehicleCatalog(IReadOnlyDictionary<string, VehiclePartDef> parts, IEnumerable<VehicleBlueprintDef> defs, VehicleTuning tuning)
    {
        Parts = parts; Tuning = tuning; var errors = new List<string>();
        foreach (var d in defs.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            var b = VehicleBlueprint.Build(d, parts, _list.Count, errors);
            if (b == null) continue;
            _list.Add(b); _byId[d.Id] = b;
        }
        Errors = errors;
    }

    public static VehicleCatalog From(ContentDatabase db)
    {
        var parts = db.All<VehiclePartDef>().ToDictionary(p => p.Id, StringComparer.Ordinal);
        return new VehicleCatalog(parts, db.All<VehicleBlueprintDef>(), VehicleTuning.From(db));
    }

    public VehicleBlueprint? Find(string id) => _byId.TryGetValue(id, out var b) ? b : null;
    public VehicleBlueprint Get(string id) => _byId[id];
    public VehicleBlueprint At(int index) => _list[index];
}
