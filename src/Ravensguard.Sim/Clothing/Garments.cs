using Ravensguard.Content;

namespace Ravensguard.Sim.Clothing;

/// <summary>The six warmth regions of the body (clothing spec 3.3): the ten body parts grouped as head (head, neck), torso (upper and lower body), arms, hands, legs and feet.</summary>
public enum Region : byte { Head, Torso, Arms, Hands, Legs, Feet }

/// <summary>What a person is doing, as far as the body's heat goes (spec 4.3): the metabolic rate.</summary>
public enum Activity : byte { Sleep, Rest, Walk, Work, Sprint, Combat }

/// <summary>
/// The warmth numbers of one garment shape, read once from content: layer, region coverage in permille (the mean of the region's body parts), insulation, windproofing, waterproofing,
/// heat retention, encumbrance, absorption and the insulation lost when soaked (spec 3.1).
/// </summary>
public sealed class Garment
{
    public required string Id { get; init; }
    public required ArmourLayer Layer { get; init; }
    public int Order { get; init; }
    public required int[] RegionPm { get; init; }
    public int InsulationCloX100 { get; init; }
    public int WindproofPm { get; init; }
    public int WaterproofPm { get; init; }
    public int HeatRetentionPm { get; init; }
    public int EncPm { get; init; }
    public int AbsorbPct { get; init; }
    public int WetLossPm { get; init; }
    public int MassG { get; init; }
    public string? DefaultMaterial { get; init; }
    public string RigLayer { get; init; } = "";
    public bool Wearable { get; init; } = true;
    public required ArmourShapeDef Def { get; init; }
    /// <summary>The body part the piece mostly covers (first of the largest coverage): two pieces of one layer in one slot cannot be worn together unless their order differs (a gambeson over a tunic).</summary>
    public string Slot { get; init; } = "";

    /// <summary>Body part ids per region, in region order.</summary>
    public static readonly string[][] Parts =
    [
        ["head", "neck"], ["upper_body", "lower_body"], ["upper_arm", "lower_arm"], ["hand"], ["upper_leg", "lower_leg"], ["foot"],
    ];
    public const int RegionCount = 6;

    public static Garment From(ArmourShapeDef d)
    {
        var rp = new int[RegionCount];
        for (int r = 0; r < RegionCount; r++)
        {
            double sum = 0;
            foreach (var part in Parts[r]) if (d.Coverage.TryGetValue(part, out var c)) sum += c;
            rp[r] = (int)Math.Round(sum * 1000.0 / Parts[r].Length);
        }
        return new Garment
        {
            Id = d.Id, Layer = d.Layer, Order = d.Order, RegionPm = rp, InsulationCloX100 = d.InsulationCloX100, WindproofPm = d.WindproofPm, WaterproofPm = d.WaterproofPm,
            HeatRetentionPm = d.HeatRetentionPm, EncPm = d.EncPm, AbsorbPct = d.AbsorbPct, WetLossPm = d.WetLossPm, MassG = d.MassG, DefaultMaterial = d.DefaultMaterial,
            RigLayer = d.RigLayer, Wearable = d.Wearable, Def = d, Slot = d.Coverage.Count == 0 ? "" : d.Coverage.OrderByDescending(kv => kv.Value).First().Key,
        };
    }

    /// <summary>True when the piece covers the region at least a third (a hood covers the head, a cloak the torso): the threshold that decides what is "outermost" for wetting and wind.</summary>
    public bool Covers(Region r) => RegionPm[(int)r] >= 330;

    /// <summary>The region where the piece does most of its work (largest region weight x coverage), used to decide which pieces are exposed to rain.</summary>
    public Region Principal(int[] weightsPm)
    {
        int best = 0; long bv = -1;
        for (int r = 0; r < RegionCount; r++) { long v = (long)RegionPm[r] * weightsPm[r]; if (v > bv) { bv = v; best = r; } }
        return (Region)best;
    }
}

/// <summary>The exposure tuning in the form the math wants: weights and rates as arrays (spec 3.3). Built from the <c>exposure_settings</c> content.</summary>
public sealed class ExposureTuning
{
    public ExposureSettingsDef Def { get; }
    public int[] RegionsPm { get; } = new int[Garment.RegionCount];
    public int[] MetX100 { get; } = new int[6];
    public int IndoorUnheatedC { get; }
    public int IndoorFireC { get; }
    public int FireWorn, FireHung, IndoorWorn, IndoorHung, OutdoorWorn, WindBonus, SunBonus, RainDry;

    public ExposureTuning(ExposureSettingsDef d)
    {
        Def = d;
        string[] rn = ["head", "torso", "arms", "hands", "legs", "feet"];
        for (int i = 0; i < rn.Length; i++) RegionsPm[i] = d.RegionsPm.TryGetValue(rn[i], out var v) ? v : 0;
        if (RegionsPm.Sum() != 1000) throw new FormatException($"exposure_settings '{d.Id}': regions_pm must sum to 1000, got {RegionsPm.Sum()}");
        string[] an = ["sleep", "rest", "walk", "work", "sprint", "combat"];
        for (int i = 0; i < an.Length; i++) MetX100[i] = d.MetX100.TryGetValue(an[i], out var m) ? m : 100;
        IndoorUnheatedC = d.IndoorOffsetC.GetValueOrDefault("unheated", 5); IndoorFireC = d.IndoorOffsetC.GetValueOrDefault("fire", 15);
        int D(string k, int def) => d.DryPmPerHour.TryGetValue(k, out var v) ? v : def;
        FireWorn = D("fire_worn", 250); FireHung = D("fire_hung", 400); IndoorWorn = D("indoor_worn", 60); IndoorHung = D("indoor_hung", 90);
        OutdoorWorn = D("outdoor_worn", 70); WindBonus = D("wind_bonus", 40); SunBonus = D("sun_bonus", 30); RainDry = D("rain", 20);
    }

    public static ExposureTuning From(ContentDatabase db) => new(db.All<ExposureSettingsDef>().OrderBy(x => x.Id, StringComparer.Ordinal).First());
}
