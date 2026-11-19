using Ravensguard.Content;

namespace Ravensguard.Sim.Combat.Resolver2;

/// <summary>A material's mechanical numbers as integers (DF raw stress values in kPa, strain per 100,000, permille multipliers).</summary>
public sealed class MaterialV2
{
    public required string Id { get; init; }
    public required int Index { get; init; }
    public required string Category { get; init; }
    public int DensityKgM3 { get; init; }
    public long ShearYield { get; init; }
    public long ShearFracture { get; init; }
    public int ShearStrain { get; init; }
    public long ImpactYield { get; init; }
    public long ImpactFracture { get; init; }
    public int ImpactStrain { get; init; }
    /// <summary>DF max edge S, permille (metal 1000, wood and bone 100).</summary>
    public int SharpPm { get; init; }
    public int CutResistPm { get; init; } = 1000;
    public int CrushResistPm { get; init; } = 1000;
    public int BluntAbsorbPm { get; init; }
    public int PainPm { get; init; } = 1000;
}

/// <summary>One attack of a weapon in integer form.</summary>
public sealed class AttackV2
{
    public required string Id { get; init; }
    public required int Index { get; init; }
    public required AttackType Type { get; init; }
    /// <summary>DF contact area, milli-cm2.</summary>
    public long AreaMilli { get; init; }
    /// <summary>Penetration, micrometres.</summary>
    public long PenUm { get; init; }
    /// <summary>Velocity multiplier, permille.</summary>
    public int VelPm { get; init; }
    public int ShieldCoverPm { get; init; } = 1000;
    public required AttackDef Def { get; init; }
}

/// <summary>Derived weapon stats per (shape, head material, quality), precomputed at load (plan §2.10).</summary>
public sealed class WeaponV2
{
    public required WeaponShapeDef Shape { get; init; }
    public required MaterialV2 Head { get; init; }
    public required Quality Quality { get; init; }
    public required AttackV2[] Attacks { get; init; }
    /// <summary>Head plus haft, mg (= kg/m3 x cm3).</summary>
    public long MassMg { get; init; }
    /// <summary>Head plus haft volume, milli-cm3 (the deflection test).</summary>
    public long VolumeMilli { get; init; }
    /// <summary>Quality multiplier Qw, permille.</summary>
    public int QwPm { get; init; }
    public int AttackIndex(string id) { for (int i = 0; i < Attacks.Length; i++) if (Attacks[i].Id == id) return i; return -1; }
}

/// <summary>Derived stats of one armour piece in one material, with the woven and elastic flags applied, and its coverage per body plan.</summary>
public sealed class ArmourV2
{
    public required ArmourShapeDef Shape { get; init; }
    public required MaterialV2 Material { get; init; }
    public required int ShapeIndex { get; init; }
    public ArmourLayer Layer => Shape.Layer;
    public int Order => Shape.Order;
    public long ThicknessUm { get; init; }
    public long ShearYield { get; init; }
    public long ShearFracture { get; init; }
    public int ShearStrain { get; init; }
    public long ImpactYield { get; init; }
    public long ImpactFracture { get; init; }
    public int ImpactStrain { get; init; }
    public int DensityKgM3 { get; init; }
    public bool Elastic { get; init; }
    public int BluntAbsorbPm { get; init; }
    /// <summary>Condition lost when cut and when crushed (resolver-v2 4.3: cloth 30 and 10, mail 10 and 5, plate 5 and 3, leather between).</summary>
    public int WearCutPm { get; init; }
    public int WearCrushPm { get; init; }
    /// <summary>A rigid shell (metal, bone or wood, neither woven nor elastic: plate, lamellar, a helm): blunt blows dent it, a failed edge skids off it (close-out items 2 and 4).</summary>
    public bool Rigid { get; init; }
    /// <summary>The side a rigid shell faces (1 front, 2 back, 0 wraps the body): a blow from the other side does not meet it.</summary>
    public int Side { get; init; }
    /// <summary>Coverage permille per plan index, per base part index.</summary>
    public required int[][] CoverageByPlan { get; init; }
    /// <summary>Bits over the plan's named gaps that the piece leaves open, per plan index.</summary>
    public required ulong[] GapMaskByPlan { get; init; }
}

public sealed class ShieldV2
{
    public required ShieldShapeDef Def { get; init; }
    public int CoverPm => Def.CoverPm;
    public long CapacityMilli { get; init; }
    public long DurabilityMilli { get; init; }
}

/// <summary>The calibration row as plain integers (content <c>combat_calibration</c>).</summary>
public sealed class CalibrationV2
{
    public int KeepPm = 950, CutCostPm = 600, CrushCostPm = 600, SeverMomentumPm = 300, BoneEdgeFailDivisor = 2, StrainReference = 50000;
    public int RibSlipPm = 350, RibSlipPenUm = 80_000, MuscleBruisedBelowMilli = 40_000, MuscleTornBelowMilli = 100_000;
    public int CrackedPm = 1000, BrokenPm = 2000, DisplacedPm = 4000, ShatteredPm = 8000, BrainCutMilli = 40_000;
    public int GuardedPm = 600, GapPm = 350, FlankPm = 1250, RearPm = 1500, HitMinBp = 200, HitMaxBp = 9800, RedirectPm = 500, MisfitCoveragePm = 250;
    public int WeaponBlockBase = 110, WeaponBlockSkillDivisor = 2, BracedNeighbourPm = 100, WeaponNotchPmPer50 = 20;
    public int DentFullPm = 150, DentNoticePm = 40, BurstRatioPm = 2000, BreachCoveragePm = 300, StrapBelowPm = 300, SlipCoveragePm = 500;
    public int BurnPmPerSecond = 75, BoneBurnDivisor = 3;
    public long MaxMomentumMilli = 2_000_000;

    public static CalibrationV2 From(CombatCalibrationDef? d)
    {
        var c = new CalibrationV2();
        if (d == null) return c;
        c.KeepPm = d.KeepPm; c.CutCostPm = d.CutCostPm; c.CrushCostPm = d.CrushCostPm; c.SeverMomentumPm = d.SeverMomentumPm;
        c.BoneEdgeFailDivisor = Math.Max(1, d.BoneEdgeFailDivisor); c.StrainReference = Math.Max(1, d.StrainReference);
        c.RibSlipPm = d.RibSlipPm; c.RibSlipPenUm = d.RibSlipPenMm * 1000;
        if (d.MuscleCrushGrades.Count >= 2) { c.MuscleBruisedBelowMilli = d.MuscleCrushGrades[0] * 1000; c.MuscleTornBelowMilli = d.MuscleCrushGrades[1] * 1000; }
        if (d.FracturePm.Count >= 4) { c.CrackedPm = d.FracturePm[0]; c.BrokenPm = d.FracturePm[1]; c.DisplacedPm = d.FracturePm[2]; c.ShatteredPm = d.FracturePm[3]; }
        c.BrainCutMilli = d.BrainCutMomentum * 1000;
        c.GuardedPm = d.GuardedPm; c.GapPm = d.GapPm; c.FlankPm = d.FlankPm; c.RearPm = d.RearPm; c.HitMinBp = d.HitMinBp; c.HitMaxBp = d.HitMaxBp;
        c.RedirectPm = d.RedirectPm; c.MisfitCoveragePm = d.MisfitCoveragePm;
        c.WeaponBlockBase = d.WeaponBlockBase; c.WeaponBlockSkillDivisor = Math.Max(1, d.WeaponBlockSkillDivisor); c.BracedNeighbourPm = d.BracedNeighbourPm;
        c.DentFullPm = d.DentFullPm; c.DentNoticePm = d.DentNoticePm; c.BurstRatioPm = Math.Max(1, d.BurstRatioPm); c.BreachCoveragePm = d.BreachCoveragePm;
        c.StrapBelowPm = d.StrapBelowPm; c.SlipCoveragePm = d.SlipCoveragePm;
        c.WeaponNotchPmPer50 = d.WeaponNotchPmPer50; c.BurnPmPerSecond = d.BurnPmPerSecond; c.BoneBurnDivisor = Math.Max(1, d.BoneBurnDivisor);
        c.MaxMomentumMilli = d.MaxMomentumMilli;
        return c;
    }
}

/// <summary>
/// Resolver v2 derived tables (resolver-v2 3.1, plan §2.10): materials, every (weapon shape x material x quality), every (armour piece x material)
/// with its coverage per body plan, shields, body plans and the calibration, all as integers. Built once at load; the resolver only looks up.
/// This constructor is the one place the content's decimals become integers (rounded half away from zero).
/// </summary>
public sealed class CombatTablesV2
{
    /// <summary>Qa, the armour quality multiplier by <see cref="Quality"/>, permille (DF2014:Item quality, the armour-deflect column: an artifact x3).</summary>
    public static readonly int[] QualityPm = [1000, 1200, 1400, 1600, 1800, 2000, 3000];
    /// <summary>Qw, the weapon's edge multiplier by <see cref="Quality"/>, permille (the sharpness column, 50% to 100% of the max edge: an artifact's edge is a masterwork's).</summary>
    public static readonly int[] EdgeQualityPm = [1000, 1200, 1400, 1600, 1800, 2000, 2000];

    private readonly Dictionary<string, MaterialV2> _materials = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, string, Quality), WeaponV2> _weapons = [];
    private readonly Dictionary<(string, string), ArmourV2> _armour = [];
    private readonly Dictionary<string, ShieldV2> _shields = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BodyPlanV2> _plans = new(StringComparer.Ordinal);

    public CalibrationV2 Cal { get; }
    public IReadOnlyList<MaterialV2> MaterialList { get; }
    public IReadOnlyList<BodyPlanV2> Plans { get; }
    public IReadOnlyDictionary<string, ShieldV2> Shields => _shields;
    public int WeaponEntries => _weapons.Count;
    public int ArmourEntries => _armour.Count;

    private static readonly HashSet<string> WeaponCategories = ["metal", "wood", "bone"];
    private static readonly HashSet<string> ArmourCategories = ["metal", "leather", "cloth", "bone", "wood"];

    public CombatTablesV2(ContentDatabase db) : this(db, MaterialResolver.ResolveAll(db)) { }

    private readonly IReadOnlyDictionary<string, ResolvedMaterial> _resolved;
    private readonly Dictionary<(string, string, Quality), WeaponV2> _synthetic = [];

    public CombatTablesV2(ContentDatabase db, IReadOnlyDictionary<string, ResolvedMaterial> resolved)
    {
        _resolved = resolved;
        Cal = CalibrationV2.From(db.All<CombatCalibrationDef>().OrderBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault());
        var mats = new List<MaterialV2>();
        foreach (var m in resolved.Values.OrderBy(m => m.Id, StringComparer.Ordinal))
        {
            var mv = new MaterialV2
            {
                Id = m.Id, Index = mats.Count, Category = m.Category, DensityKgM3 = R(m.DensityKgM3),
                ShearYield = RL(m.ShearYield), ShearFracture = RL(m.ShearFracture), ShearStrain = R(m.ShearStrainAtYield),
                ImpactYield = RL(m.ImpactYield), ImpactFracture = RL(m.ImpactFracture), ImpactStrain = R(m.ImpactStrainAtYield),
                SharpPm = R(m.MaxEdge * 1000), CutResistPm = m.CutResistPm, CrushResistPm = m.CrushResistPm, BluntAbsorbPm = m.BluntAbsorbPm, PainPm = m.PainPm,
            };
            mats.Add(mv); _materials[m.Id] = mv;
        }
        MaterialList = mats;

        // body plans first: armour coverage is stored per plan
        var plans = new List<BodyPlanV2>();
        foreach (var p in db.All<BodyPlanDef>().OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            var bp = BodyPlanV2.Build(BodyPlanResolver.Resolve(db, p), plans.Count, Material);
            plans.Add(bp); _plans[bp.Id] = bp;
        }
        Plans = plans;

        var shapes = db.All<WeaponShapeDef>().OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
        foreach (var s in shapes)
            foreach (var m in resolved.Values.Where(m => WeaponCategories.Contains(m.Category)))
                for (int q = 0; q < QualityPm.Length; q++)
                    _weapons[(s.Id, m.Id, (Quality)q)] = BuildWeapon(s, m, (Quality)q, resolved);

        int si = 0;
        foreach (var s in db.All<ArmourShapeDef>().OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            var cover = plans.Select(p => CoverageFor(s, p)).ToArray();
            var gaps = plans.Select(p => GapMaskFor(s, p)).ToArray();
            foreach (var m in resolved.Values.Where(m => ArmourCategories.Contains(m.Category)))
                _armour[(s.Id, m.Id)] = BuildArmour(s, si, _materials[m.Id], cover, gaps);
            si++;
        }
        foreach (var s in db.All<ShieldShapeDef>().OrderBy(s => s.Id, StringComparer.Ordinal))
            _shields[s.Id] = new ShieldV2 { Def = s, CapacityMilli = s.Capacity * 1000L, DurabilityMilli = Math.Max(1, s.Durability) * 1000L };
    }

    private static int R(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
    private static long RL(double v) => (long)Math.Round(v, MidpointRounding.AwayFromZero);

    private WeaponV2 BuildWeapon(WeaponShapeDef s, ResolvedMaterial head, Quality q, IReadOnlyDictionary<string, ResolvedMaterial> materials)
    {
        long mass = RL(head.DensityKgM3 * s.HeadVolumeCm3);
        if (s.HaftVolumeCm3 > 0)
        {
            var haft = s.HaftMaterial != null ? materials[s.HaftMaterial] : head;
            mass += RL(haft.DensityKgM3 * s.HaftVolumeCm3);
        }
        var attacks = new AttackV2[s.Attacks.Count];
        for (int i = 0; i < attacks.Length; i++)
        {
            var a = s.Attacks[i];
            attacks[i] = new AttackV2
            {
                Id = a.Id, Index = i, Type = a.Type, AreaMilli = RL(a.ContactArea * 1000), PenUm = RL(a.PenetrationMm * 1000), VelPm = R(a.Velocity * 1000),
                ShieldCoverPm = a.ShieldCoverPm, Def = a,
            };
        }
        return new WeaponV2
        {
            Shape = s, Head = _materials[head.Id], Quality = q, Attacks = attacks, MassMg = mass,
            VolumeMilli = RL((s.HeadVolumeCm3 + s.HaftVolumeCm3) * 1000), QwPm = EdgeQualityPm[(int)q],
        };
    }

    private static ArmourV2 BuildArmour(ArmourShapeDef s, int shapeIndex, MaterialV2 m, int[][] cover, ulong[] gaps)
    {
        long sy = m.ShearYield, sf = m.ShearFracture;
        if (s.Woven) { sy = Math.Min(sy, 20_000); sf = Math.Min(sf, 30_000); }
        int wearCut = m.Category switch { "cloth" => 30, "leather" => 20, "metal" => s.Elastic ? 10 : 5, _ => 15 };
        int wearCrush = m.Category switch { "cloth" => 10, "leather" => 7, "metal" => s.Elastic ? 5 : 3, _ => 6 };
        return new ArmourV2
        {
            Shape = s, Material = m, ShapeIndex = shapeIndex, ThicknessUm = RL(s.ThicknessMm * 1000),
            ShearYield = sy, ShearFracture = sf, ShearStrain = s.Elastic ? 50_000 : m.ShearStrain,
            ImpactYield = m.ImpactYield, ImpactFracture = m.ImpactFracture, ImpactStrain = s.Elastic ? 50_000 : m.ImpactStrain,
            DensityKgM3 = m.DensityKgM3, Elastic = s.Elastic, BluntAbsorbPm = s.BluntAbsorbPm, WearCutPm = wearCut, WearCrushPm = wearCrush,
            Rigid = !s.Elastic && !s.Woven && m.Category is "metal" or "bone" or "wood", Side = s.Side switch { "front" => 1, "back" => 2, _ => 0 },
            CoverageByPlan = cover, GapMaskByPlan = gaps,
        };
    }

    private static int[] CoverageFor(ArmourShapeDef s, BodyPlanV2 p)
    {
        var c = new int[p.BaseIds.Count];
        if (s.Body.Length > 0 && s.Body != p.Id) return c;                    // barding covers only the plan it is made for (mounted-combat 3.1)
        for (int b = 0; b < c.Length; b++) if (s.Coverage.TryGetValue(p.BaseIds[b], out var v)) c[b] = R(v * 1000);
        return c;
    }

    private static ulong GapMaskFor(ArmourShapeDef s, BodyPlanV2 p)
    {
        ulong mask = 0;
        foreach (var g in s.Gaps) { int i = p.GapIndex(g); if (i >= 0) mask |= 1UL << i; }
        return mask;
    }

    public MaterialV2 Material(string id) => _materials.TryGetValue(id, out var m) ? m : throw new KeyNotFoundException($"unknown material '{id}'");
    public bool HasMaterial(string id) => _materials.ContainsKey(id);
    public BodyPlanV2 Plan(string id) => _plans.TryGetValue(id, out var p) ? p : throw new KeyNotFoundException($"unknown body plan '{id}'");
    public WeaponV2 Weapon(string shape, string material, Quality q = Quality.Standard) =>
        _weapons.TryGetValue((shape, material, q), out var w) ? w : throw new KeyNotFoundException($"no weapon stats for '{shape}' in '{material}'");
    /// <summary>
    /// A weapon built on demand from a shape that is not in the content's weapon list (an arrow head, an improvised stool, a thrown rock: melee-and-ranged 4.5, 4.6), in any material,
    /// cached by (shape id, material, quality). The same rules as the table's own entries.
    /// </summary>
    public WeaponV2 Synthetic(WeaponShapeDef shape, string material, Quality q = Quality.Standard)
    {
        lock (_synthetic)
        {
            if (_synthetic.TryGetValue((shape.Id, material, q), out var w)) return w;
            if (!_resolved.TryGetValue(material, out var m)) throw new KeyNotFoundException($"unknown material '{material}'");
            w = BuildWeapon(shape, m, q, _resolved);
            _synthetic[(shape.Id, material, q)] = w;
            return w;
        }
    }

    public ArmourV2 Armour(string shape, string material) =>
        _armour.TryGetValue((shape, material), out var a) ? a : throw new KeyNotFoundException($"no armour stats for '{shape}' in '{material}'");
    /// <summary>The weapon and armour lookups without an exception for a pair the content does not have (review finding S6 / W1: no exceptions as control flow).</summary>
    public bool TryWeapon(string shape, string material, out WeaponV2 weapon, Quality q = Quality.Standard) => _weapons.TryGetValue((shape, material, q), out weapon!);
    public bool TryArmour(string shape, string material, out ArmourV2 armour) => _armour.TryGetValue((shape, material), out armour!);
    public ShieldV2 Shield(string id) => _shields[id];

    /// <summary>Qa for a Phase 3 item quality (Crude 800, Poor 900, Common 1000, Fine 1400, Masterwork 2000): DF has nothing below standard.</summary>
    public static int QualityPmOf(Goods.ItemQuality q) => q switch
    {
        Goods.ItemQuality.Crude => 800, Goods.ItemQuality.Poor => 900, Goods.ItemQuality.Fine => 1400, Goods.ItemQuality.Masterwork => 2000, _ => 1000,
    };
}
