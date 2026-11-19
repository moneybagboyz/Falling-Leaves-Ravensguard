using Ravensguard.Content;

namespace Ravensguard.Sim.Combat;

/// <summary>Mechanical numbers of one layer material, pre-converted for the §2.6 formulas.</summary>
public sealed record LayerProps(
    string MaterialId, double DensityGcm3,
    double ShearYield, double ShearFracture, double ShearStrain,
    double ImpactYieldMpa, double ImpactFractureMpa, double ImpactStrain,
    double Sharpness)
{
    /// <summary>2*IF - IY (MPa units), the blunt fracture term. Floored so soft materials still need some momentum.</summary>
    public double BluntBase => Math.Max(2 * ImpactFractureMpa - ImpactYieldMpa, 1e-6);

    public static LayerProps From(ResolvedMaterial m, bool elastic = false, bool woven = false)
    {
        double sy = m.ShearYield, sf = m.ShearFracture;
        if (woven) { sy = Math.Min(sy, 20_000); sf = Math.Min(sf, 30_000); } // woven cloth: shear reduced to 20-30
        double ss = elastic ? 50_000 : m.ShearStrainAtYield;
        double isr = elastic ? 50_000 : m.ImpactStrainAtYield;
        return new LayerProps(m.Id, m.DensityGcm3, sy, sf, ss, m.ImpactYield / 1e6, m.ImpactFracture / 1e6, isr, m.MaxEdge);
    }
}

public sealed record WeaponStats(
    WeaponShapeDef Shape, ResolvedMaterial HeadMaterial, Quality Quality, LayerProps Head,
    double TotalMassMg, double VolumeCm3, double Qw)
{
    public double MassKg => TotalMassMg / 1e6;
    /// <summary>Edge = material max edge × quality (plan §2.10).</summary>
    public double EdgeFactor => Head.Sharpness * Qw;
    public AttackDef Attack(string attackId) =>
        Shape.Attacks.FirstOrDefault(a => a.Id == attackId)
        ?? throw new KeyNotFoundException($"weapon '{Shape.Id}' has no attack '{attackId}'");
}

/// <summary>Per (armour piece, material) layer properties with the elastic/woven item flags applied.</summary>
public sealed record ArmourStats(ArmourShapeDef Shape, ResolvedMaterial Material, LayerProps Props)
{
    /// <summary>Approximate worn mass: covered shell area (~4·V/depth per part) × thickness × density.</summary>
    public double MassKg(BodyPlan plan, int bodySizeCm3)
    {
        double cm3 = 0;
        foreach (var p in plan.Parts)
            if (Shape.Coverage.TryGetValue(p.BaseId, out var cov))
                cm3 += cov * 4.0 * (bodySizeCm3 * p.RelativeSize) / (p.DepthMm / 10.0) * (Shape.ThicknessMm / 10.0);
        return cm3 * Material.DensityKgM3 / 1e6;
    }
}

/// <summary>
/// Derived stats precomputed at load (plan §2.10): a resolution is then a lookup plus a few multiplications.
/// Weapons: (shape, material, quality) → mass, size, edge. Armour: (piece, material) → layer thresholds inputs.
/// </summary>
public sealed class CombatTables
{
    private readonly Dictionary<(string, string, Quality), WeaponStats> _weapons = [];
    private readonly Dictionary<(string, string), ArmourStats> _armour = [];
    private readonly Dictionary<string, LayerProps> _tissues = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, ResolvedMaterial> Materials { get; }
    public IReadOnlyDictionary<string, WeaponShapeDef> WeaponShapes { get; }
    public IReadOnlyDictionary<string, ArmourShapeDef> ArmourShapes { get; }
    public IReadOnlyDictionary<string, BodyPlan> BodyPlans { get; }
    public int WeaponEntries => _weapons.Count;
    public int ArmourEntries => _armour.Count;

    private static readonly HashSet<string> WeaponCategories = ["metal", "wood", "bone"];
    private static readonly HashSet<string> ArmourCategories = ["metal", "leather", "cloth", "bone", "wood"];

    public CombatTables(ContentDatabase db)
    {
        var materials = MaterialResolver.ResolveAll(db);
        Materials = materials;
        foreach (var m in materials.Values) _tissues[m.Id] = LayerProps.From(m);

        WeaponShapes = db.All<WeaponShapeDef>().ToDictionary(s => s.Id, StringComparer.Ordinal);
        foreach (var s in WeaponShapes.Values)
            foreach (var m in materials.Values.Where(m => WeaponCategories.Contains(m.Category)))
                for (int q = 0; q < QualityTable.Count; q++)
                    _weapons[(s.Id, m.Id, (Quality)q)] = BuildWeapon(s, m, (Quality)q, materials);

        ArmourShapes = db.All<ArmourShapeDef>().ToDictionary(s => s.Id, StringComparer.Ordinal);
        foreach (var s in ArmourShapes.Values)
            foreach (var m in materials.Values.Where(m => ArmourCategories.Contains(m.Category)))
                _armour[(s.Id, m.Id)] = new ArmourStats(s, m, LayerProps.From(m, s.Elastic, s.Woven));

        BodyPlans = db.All<BodyPlanDef>().ToDictionary(p => p.Id, p => BodyPlan.Build(BodyPlanResolver.Resolve(db, p), Tissue), StringComparer.Ordinal);
        V2 = new Resolver2.CombatTablesV2(db, materials);
    }

    /// <summary>The resolver v2 tables (integer derived stats per weapon, armour piece, shield, tissue and body plan; resolver-v2 3.1).</summary>
    public Resolver2.CombatTablesV2 V2 { get; }

    private static WeaponStats BuildWeapon(WeaponShapeDef s, ResolvedMaterial head, Quality q, Dictionary<string, ResolvedMaterial> materials)
    {
        double mass = head.DensityKgM3 * s.HeadVolumeCm3;
        if (s.HaftVolumeCm3 > 0)
        {
            var haft = s.HaftMaterial != null ? materials[s.HaftMaterial] : head;
            mass += haft.DensityKgM3 * s.HaftVolumeCm3;
        }
        return new WeaponStats(s, head, q, LayerProps.From(head), mass, s.HeadVolumeCm3 + s.HaftVolumeCm3, QualityTable.Multiplier(q));
    }

    public LayerProps Tissue(string materialId) =>
        _tissues.TryGetValue(materialId, out var p) ? p : throw new KeyNotFoundException($"unknown material '{materialId}'");

    public WeaponStats Weapon(WeaponInstance w) => Weapon(w.ShapeId, w.MaterialId, w.Quality);
    public WeaponStats Weapon(string shape, string material, Quality quality = Quality.Standard) =>
        _weapons.TryGetValue((shape, material, quality), out var s) ? s
            : throw new KeyNotFoundException($"no weapon stats for shape '{shape}' in material '{material}'");

    public ArmourStats Armour(string shape, string material) =>
        _armour.TryGetValue((shape, material), out var s) ? s
            : throw new KeyNotFoundException($"no armour stats for shape '{shape}' in material '{material}'");
}
